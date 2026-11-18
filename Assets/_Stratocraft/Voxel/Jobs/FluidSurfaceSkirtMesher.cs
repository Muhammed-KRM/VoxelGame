#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

#if UNITY_5_3_OR_NEWER
using Unity.Collections;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// Sivi yuzeyinin tek bir kosesine ait tum fiziksel ve gorsel oznitelikler (Standalone .NET 10 uyumlu).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FluidCornerSample
    {
        public Vector3 PositionCurr;
        public Vector3 Normal;
        public byte RenderFluidId;     // 1 = Tatli Su, 12 = Tuzlu Su, 2 = Lav, 3 = Asit
        public byte EncodedSubTickDy;  // [1..255], 128 == 0.0m, [-1.0m .. +1.0m]
        public byte EncodedVelX;       // [1..255], 128 == 0.0 m/s, [-4.0 .. +4.0 m/s]
        public byte EncodedVelZ;       // [1..255], 128 == 0.0 m/s, [-4.0 .. +4.0 m/s]
        public float DepthMeters;      // [0.0 .. 8.0m] yerel su sutunu derinligi
        public float WaterfallFoam01;  // [0.0 .. 1.0] selale perdesi ve dusus havuzu havalandirma kopugu

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte EncodeSignedUnitToByte(float normalizedSigned)
        {
            float clamped = Math.Clamp(float.IsFinite(normalizedSigned) ? normalizedSigned : 0.0f, -1.0f, 1.0f);
            int raw = 128 + (int)MathF.Round(clamped * 127.0f);
            return (byte)Math.Clamp(raw, 1, 255);
        }
    }

    /// <summary>
    /// Kose-basina alt-tik yukseklik farki, hiz, derinlik ve selale kopugu tasiyan zengin akiskan ucgeni.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FluidRichTriangle
    {
        public FluidCornerSample C0;
        public FluidCornerSample C1;
        public FluidCornerSample C2;
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>
    /// Paket 4B akiskanlara ozel 36-Byte GPU tepe duzeni (0.0 MB doku VRAM; HybridSurfaceNetsAndShapeMesher'dan bagimsiz).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FluidMeshVertex
    {
        public UnityEngine.Vector3 Position; // 12B (VertexAttribute.Position, Float32 x3)
        public UnityEngine.Vector3 Normal;   // 12B (VertexAttribute.Normal, Float32 x3)
        public UnityEngine.Color32 Color;    //  4B (VertexAttribute.Color, UNorm8 x4: R=Id, G=Dy, B=Vx, A=Vz)
        public UnityEngine.Vector2 AuxUv0;   //  8B (VertexAttribute.TexCoord0, Float32 x2: x=Depth, y=WaterfallFoam)
    }
#endif

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D.1 &amp; Paket 4B (IP-2):
    /// Sonlu Akiskan (Su, Lav, Asit) 3B Hacimli Prizma Yuzey Orme, C1 Ince Film, Rouse Savak Cokmesi,
    /// Kesintisiz Parabolik Selale Perdesi ve Sifir-GC Kinematik Kose Kodlama motoru.
    /// </summary>
    public static partial class FluidSurfaceSkirtMesher
    {
        public const float ShorelineSkirtExtensionMeters = 0.65f;
        public const float MinThinFilmThicknessMeters = 0.006f;
        public const float MinVisibleFilmThickness = 0.004f;
        public const float MinVisualFluidThicknessMeters = 0.22f;
        public const float MaxVisualFluidThicknessMeters = 0.88f;
        public const float MinSlopeStreamClearanceMeters = 0.24f;
        public const float WeirBrinkDepthRatio = 0.68f;
        public const float MaxKinematicsVelocityMps = 4.0f;
        private const float FoundationEmbedDepthMeters = 0.18f;

        private struct ContourVertex
        {
            public Vector3 Top;
            public Vector3 Bottom;
            public sbyte EdgeDirX; // -1, 0, +1 (0,0 = acik dis kose pah/yuvarlatma kenari)
            public sbyte EdgeDirZ;
        }

        private struct RichContourVertex
        {
            public Vector3 TopCurr;
            public Vector3 Bottom;
            public Vector3 SmoothTopNormal;
            public byte EncodedSubTickDy;
            public byte EncodedVelX;
            public byte EncodedVelZ;
            public float DepthMeters;
            public sbyte EdgeDirX;
            public sbyte EdgeDirZ;
        }

        private struct RichCornerResult
        {
            public float TopCurrY;
            public float TopPrevY;
            public float BedY;
            public Vector3 SkirtOffset;
            public float VelX;
            public float VelZ;
            public float DepthMeters;
            public bool TouchesSdfShore;
            public bool HasOverfall;
            public bool HasSlopeConnection;
            public bool IsPoolRegime;
            public bool HasSharpArchitecturalFloor;
        }

        /// <summary>
        /// Merkez + 8 komsu chunk'in tum dizilerini tek seferde on-yukleyerek hucre/kose dongusundeki
        /// ~44.000 IReadOnlyDictionary ve IFluidKinematicsSlice sanal cagrisini %100 sifirlayan duz baglam.
        /// Slot indeksi: (dz + 1) * 3 + (dx + 1), merkez == 4.
        /// </summary>
        private sealed class FluidNeighborhood3x3Cache
        {
            public readonly uint[]?[] Solids = new uint[]?[9];
            public readonly ushort[]?[] Fluids = new ushort[]?[9];
            public readonly byte[]?[] PrevMass = new byte[]?[9];
            public readonly sbyte[]?[] VelX = new sbyte[]?[9];
            public readonly sbyte[]?[] VelZ = new sbyte[]?[9];
            public readonly bool[] KinInit = new bool[9];
            public ulong[]? CenterSalinity;
            public bool ClampMissingToCenter;

            public void Bind(
                int centerCx,
                int centerCz,
                IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
                IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
                IReadOnlyDictionary<(int cx, int cz), IFluidKinematicsSlice>? kinematicsChunks,
                IReadOnlyDictionary<(int cx, int cz), ulong[]>? salinityChunkBitsMap)
            {
                ClampMissingToCenter = solidChunks.Count > 4;
                CenterSalinity = null;
                salinityChunkBitsMap?.TryGetValue((centerCx, centerCz), out CenterSalinity);

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int slot = (dz + 1) * 3 + (dx + 1);
                        (int cx, int cz) key = (centerCx + dx, centerCz + dz);

                        if (solidChunks.TryGetValue(key, out uint[]? sArr))
                        {
                            Solids[slot] = sArr;
                            Fluids[slot] = fluidChunks.TryGetValue(key, out ushort[]? fArr) ? fArr : null;
                            if (kinematicsChunks != null &&
                                kinematicsChunks.TryGetValue(key, out IFluidKinematicsSlice? kSlice) &&
                                kSlice != null &&
                                kSlice.IsInitializedFromCurrentMass)
                            {
                                PrevMass[slot] = kSlice.PrevMass;
                                VelX[slot] = kSlice.VelX_Q7;
                                VelZ[slot] = kSlice.VelZ_Q7;
                                KinInit[slot] = true;
                            }
                            else
                            {
                                PrevMass[slot] = null;
                                VelX[slot] = null;
                                VelZ[slot] = null;
                                KinInit[slot] = false;
                            }
                        }
                        else
                        {
                            Solids[slot] = null;
                            Fluids[slot] = null;
                            PrevMass[slot] = null;
                            VelX[slot] = null;
                            VelZ[slot] = null;
                            KinInit[slot] = false;
                        }
                    }
                }
            }

            public void ClearReferences()
            {
                Array.Clear(Solids, 0, 9);
                Array.Clear(Fluids, 0, 9);
                Array.Clear(PrevMass, 0, 9);
                Array.Clear(VelX, 0, 9);
                Array.Clear(VelZ, 0, 9);
                Array.Clear(KinInit, 0, 9);
                CenterSalinity = null;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SampleCellFast(int lx, int y, int lz, out uint solidRaw, out ushort fluidRaw)
            {
                if ((uint)lx < 16u && (uint)lz < 16u)
                {
                    int idx = (y << 8) | (lz << 4) | lx;
                    uint[] s4 = Solids[4]!;
                    ushort[] f4 = Fluids[4]!;
                    solidRaw = (uint)idx < (uint)s4.Length ? s4[idx] : 0u;
                    fluidRaw = (uint)idx < (uint)f4.Length ? f4[idx] : (ushort)0;
                    return;
                }

                int cdx = lx < 0 ? -1 : (lx >= 16 ? 1 : 0);
                int cdz = lz < 0 ? -1 : (lz >= 16 ? 1 : 0);
                int nlx = lx - cdx * 16;
                int nlz = lz - cdz * 16;
                if ((uint)nlx < 16u && (uint)nlz < 16u)
                {
                    int slot = (cdz + 1) * 3 + (cdx + 1);
                    uint[]? ns = Solids[slot];
                    if (ns != null)
                    {
                        int nIdx = (y << 8) | (nlz << 4) | nlx;
                        if ((uint)nIdx < (uint)ns.Length)
                        {
                            solidRaw = ns[nIdx];
                            ushort[]? nf = Fluids[slot];
                            fluidRaw = (nf != null && (uint)nIdx < (uint)nf.Length) ? nf[nIdx] : (ushort)0;
                            return;
                        }
                    }
                }

                if (ClampMissingToCenter)
                {
                    int cIdx = (y << 8) | (Math.Clamp(lz, 0, 15) << 4) | Math.Clamp(lx, 0, 15);
                    uint[] s4 = Solids[4]!;
                    ushort[] f4 = Fluids[4]!;
                    solidRaw = (uint)cIdx < (uint)s4.Length ? s4[cIdx] : 0u;
                    fluidRaw = (uint)cIdx < (uint)f4.Length ? f4[cIdx] : (ushort)0;
                    return;
                }

                solidRaw = 0u;
                fluidRaw = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SampleRichCellFast(
                int lx,
                int y,
                int lz,
                out uint solidRaw,
                out ushort fluidRaw,
                out byte prevMass,
                out float velX,
                out float velZ)
            {
                if ((uint)lx < 16u && (uint)lz < 16u)
                {
                    int idx = (y << 8) | (lz << 4) | lx;
                    uint[] s4 = Solids[4]!;
                    ushort[] f4 = Fluids[4]!;
                    solidRaw = (uint)idx < (uint)s4.Length ? s4[idx] : 0u;
                    fluidRaw = (uint)idx < (uint)f4.Length ? f4[idx] : (ushort)0;
                    if (KinInit[4] && (uint)idx < (uint)PrevMass[4]!.Length)
                    {
                        prevMass = PrevMass[4]![idx];
                        velX = VelX[4]![idx] * (MaxKinematicsVelocityMps / 127.0f);
                        velZ = VelZ[4]![idx] * (MaxKinematicsVelocityMps / 127.0f);
                    }
                    else
                    {
                        prevMass = (byte)(fluidRaw & 0xFFu);
                        velX = 0f;
                        velZ = 0f;
                    }
                    return;
                }

                int cdx = lx < 0 ? -1 : (lx >= 16 ? 1 : 0);
                int cdz = lz < 0 ? -1 : (lz >= 16 ? 1 : 0);
                int nlx = lx - cdx * 16;
                int nlz = lz - cdz * 16;
                if ((uint)nlx < 16u && (uint)nlz < 16u)
                {
                    int slot = (cdz + 1) * 3 + (cdx + 1);
                    uint[]? ns = Solids[slot];
                    if (ns != null)
                    {
                        int nIdx = (y << 8) | (nlz << 4) | nlx;
                        if ((uint)nIdx < (uint)ns.Length)
                        {
                            solidRaw = ns[nIdx];
                            ushort[]? nf = Fluids[slot];
                            fluidRaw = (nf != null && (uint)nIdx < (uint)nf.Length) ? nf[nIdx] : (ushort)0;
                            if (KinInit[slot] && (uint)nIdx < (uint)PrevMass[slot]!.Length)
                            {
                                prevMass = PrevMass[slot]![nIdx];
                                velX = VelX[slot]![nIdx] * (MaxKinematicsVelocityMps / 127.0f);
                                velZ = VelZ[slot]![nIdx] * (MaxKinematicsVelocityMps / 127.0f);
                            }
                            else
                            {
                                prevMass = (byte)(fluidRaw & 0xFFu);
                                velX = 0f;
                                velZ = 0f;
                            }
                            return;
                        }
                    }
                }

                if (ClampMissingToCenter)
                {
                    int cIdx = (y << 8) | (Math.Clamp(lz, 0, 15) << 4) | Math.Clamp(lx, 0, 15);
                    uint[] s4 = Solids[4]!;
                    ushort[] f4 = Fluids[4]!;
                    solidRaw = (uint)cIdx < (uint)s4.Length ? s4[cIdx] : 0u;
                    fluidRaw = (uint)cIdx < (uint)f4.Length ? f4[cIdx] : (ushort)0;
                    if (KinInit[4] && (uint)cIdx < (uint)PrevMass[4]!.Length)
                    {
                        prevMass = PrevMass[4]![cIdx];
                        velX = VelX[4]![cIdx] * (MaxKinematicsVelocityMps / 127.0f);
                        velZ = VelZ[4]![cIdx] * (MaxKinematicsVelocityMps / 127.0f);
                    }
                    else
                    {
                        prevMass = (byte)(fluidRaw & 0xFFu);
                        velX = 0f;
                        velZ = 0f;
                    }
                    return;
                }

                solidRaw = 0u;
                fluidRaw = 0;
                prevMass = 0;
                velX = 0f;
                velZ = 0f;
            }
        }

        [ThreadStatic] private static FluidNeighborhood3x3Cache? _tlsNeighborhoodCache;

        /// <summary>
        /// Geriye donuk uyumlu parcali sivi kalinligi (legacy mesher tarafindan kullanilir).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeVisualFluidThickness(byte mass, byte capacity)
        {
            if (mass == 0) return 0f;
            if (capacity == 0) return MaxVisualFluidThicknessMeters;

            float effMass = capacity < 255 ? Math.Clamp((mass * 255.0f) / capacity, 1.0f, 255.0f) : mass;
            if (effMass <= 12.0f)
            {
                return 0.025f + (0.095f - 0.025f) * ((effMass - 1.0f) / 11.0f);
            }

            if (effMass <= 32.0f)
            {
                return 0.095f + (MinVisualFluidThicknessMeters - 0.095f) * ((effMass - 12.0f) / 20.0f);
            }

            float highNorm = Math.Clamp((effMass - 32.0f) / (255.0f - 32.0f), 0.0f, 1.0f);
            return MinVisualFluidThicknessMeters +
                   (MaxVisualFluidThicknessMeters - MinVisualFluidThicknessMeters) * MathF.Pow(highNorm, 0.60f);
        }

        /// <summary>
        /// Paket 4B.2 C1-surekli, dogrusal ve 0 MathF.Pow iceren fiziksel sivi kalinligi (m = 1..255).
        /// m = 0 -> 0.0m, m = 1 -> ~0.0079m (ince islak su filmi, sel sisirmesi yok!), m = 255 -> 1.000m.
        /// Fizik cozucunun bed + m/255 hidrostatik dengesiyle %100 uyumludur (sahil kamburu uretmez).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MassToFluidThickness(byte mass)
        {
            if (mass == 0) return 0.0f;
            if (mass >= 255) return 1.0f;
            return MinVisibleFilmThickness + ((1.0f - MinVisibleFilmThickness) * (1.0f / 255.0f)) * mass;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MassToFluidThicknessWithCapacity(byte mass, byte capacity)
        {
            if (mass == 0) return 0.0f;
            if (capacity == 0) return 1.0f;
            if (capacity >= 255) return MassToFluidThickness(mass);
            float effMass = Math.Clamp((mass * 255.0f) / capacity, 1.0f, 255.0f);
            return MinVisibleFilmThickness + ((1.0f - MinVisibleFilmThickness) * (1.0f / 255.0f)) * effMass;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeExactHydrostaticThickness(byte mass, byte capacity)
        {
            if (mass == 0) return 0.0f;
            if (capacity == 0 || mass >= capacity) return 1.0f;
            return mass / (float)capacity;
        }

        /// <summary>
        /// Paket 4B.2 Kesintisiz Hidrostatik Yuzey Kotu (FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255 ikizi):
        /// Altinda sub-voxel SurfaceNets tabani ((y-1) &lt; bedY &lt; y) olan hucrelerde:
        /// - mass &lt;= 32 ince yagmur/dere filminde sabit film kalinligini korur,
        /// - mass &gt; 32 havuz/deniz/kazan suyunda kesin monoton olarak (y + 1.0f) tavanina baglanir;
        /// boylece solidFloorY == 88 gecis halkasindaki ~0.80m yuzey cokugu %100 ortadan kalkar!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeContinuousHydrostaticSurfaceY(float bedY, int y, byte mass, byte capacity, bool isPoolSample)
        {
            if (mass == 0) return bedY;
            float ceilY = y + 1.0f;
            if (capacity == 0 || mass >= capacity)
            {
                return bedY > (y - 1.0f + 1e-4f) ? MathF.Max(bedY + 1.0f, ceilY) : (bedY + 1.0f);
            }

            if (capacity != 255)
            {
                float frac = mass / (float)capacity;
                return MathF.Min(ceilY, bedY + (isPoolSample ? frac : MassToFluidThicknessWithCapacity(mass, capacity)));
            }

            if (bedY >= y - 1e-4f || bedY <= (y - 1.0f + 1e-4f))
            {
                return MathF.Min(ceilY, bedY + (isPoolSample ? (mass * (1.0f / 255.0f)) : MassToFluidThickness(mass)));
            }

            if (mass <= 32)
            {
                return MathF.Min(ceilY, bedY + MassToFluidThickness(mass));
            }

            float base32 = bedY + (32.0f / 255.0f);
            float t = (mass - 32.0f) * (1.0f / 223.0f);
            return base32 + t * (ceilY - base32);
        }

        /// <summary>
        /// Tuzluluk bit haritasi (ChunkSalinityBitset.ChunkBitsMap uyumlu) uzerinden shader FluidId'sini cozer:
        /// Tatli su -> 1, Tuzlu deniz suyu -> 12, Lav -> 2, Asit -> 3.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ResolveFluidRenderId(
            byte fluidType,
            int chunkX,
            int chunkZ,
            int localX,
            int y,
            int localZ,
            IReadOnlyDictionary<(int cx, int cz), ulong[]>? salinityChunkBitsMap)
        {
            if (fluidType == 2) return 2;
            if (fluidType == 3) return 3;

            if (salinityChunkBitsMap != null &&
                salinityChunkBitsMap.TryGetValue((chunkX, chunkZ), out ulong[]? bits) &&
                bits != null)
            {
                return ResolveFluidRenderIdFromBits(fluidType, localX, y, localZ, bits);
            }

            return 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ResolveFluidRenderIdFromBits(byte fluidType, int localX, int y, int localZ, ulong[]? centerSaltBits)
        {
            if (fluidType == 2) return 2;
            if (fluidType == 3) return 3;
            if (centerSaltBits != null)
            {
                int flatIdx = (y << 8) | ((localZ & 15) << 4) | (localX & 15);
                int word = flatIdx >> 6;
                if ((uint)word < (uint)centerSaltBits.Length && ((centerSaltBits[word] >> (flatIdx & 63)) & 1UL) != 0UL)
                {
                    return 12;
                }
            }
            return 1;
        }

        /// <summary>
        /// Paket 4B.2 Zengin, Hidrostatik-Duz, SurfaceNets-Uyumlu, Marching-Squares Kirpimli ve Planar-Greedy Sivi Mesher'i:
        /// 1) 3x3 duz komsu on-yukleme (0 Dictionary / 0 Interface sanal cagrisi) + 17x17 kose onbellegi,
        /// 2) Kurganov-Petrova cift-rejimli (havuzda η=sabit, yamacta z_SN + d_film) yuzey kotu,
        /// 3) Capraz akista 90° zigzag bitiren Marching Squares ic L-kose dolgusu ve kucuk su daralmasi,
        /// 4) Sualtinda 0 ic kum/kaya duvari, yamacta 0 sahte yan selale ve 4x4m Planar Greedy Quad birlestirme!
        /// </summary>
        public static void GenerateChunkFluidRichSurfaceMesh(
            int chunkX,
            int chunkZ,
            int maxY,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            List<FluidRichTriangle> outRichTriangles,
            int minY = 0,
            IReadOnlyDictionary<(int cx, int cz), IFluidKinematicsSlice>? kinematicsChunks = null,
            IReadOnlyDictionary<(int cx, int cz), ulong[]>? salinityChunkBitsMap = null)
        {
            if (solidChunks == null) throw new ArgumentNullException(nameof(solidChunks));
            if (fluidChunks == null) throw new ArgumentNullException(nameof(fluidChunks));
            if (outRichTriangles == null) throw new ArgumentNullException(nameof(outRichTriangles));

            if (!fluidChunks.TryGetValue((chunkX, chunkZ), out ushort[]? centerFluids) ||
                centerFluids == null ||
                !solidChunks.TryGetValue((chunkX, chunkZ), out uint[]? centerSolids) ||
                centerSolids == null)
            {
                return;
            }

            FluidNeighborhood3x3Cache nb = _tlsNeighborhoodCache ??= new FluidNeighborhood3x3Cache();
            nb.Bind(chunkX, chunkZ, solidChunks, fluidChunks, kinematicsChunks, salinityChunkBitsMap);

            try
            {
                int maxBufferY = Math.Min(centerFluids.Length >> 8, centerSolids.Length >> 8);
                maxY = Math.Clamp(maxY, 2, Math.Min(StratocraftConstants.ChunkSizeY, maxBufferY));
                int startY = Math.Clamp(minY, 0, maxY - 1);
                bool suppressUndergroundCaveFluids = minY >= 68;

                Span<RichContourVertex> ring = stackalloc RichContourVertex[16];
                Span<RichCornerResult> cornerGrid = stackalloc RichCornerResult[17 * 17];
                Span<byte> cornerBuiltType = stackalloc byte[17 * 17];
                Span<byte> planarPoolRenderIds = stackalloc byte[256];
                Span<ushort> mergedCellMask16 = stackalloc ushort[16];

                for (int y = startY; y < maxY; y++)
                {
                    int yOffset = y << 8;

                    // Bu katmanda veya capraz L-kose dolgusu icin y+-1 katmaninda sivi var mi?
                    bool sliceHasFluid = false;
                    for (int i = 0; i < 256; i++)
                    {
                        if ((centerFluids[yOffset | i] & 0xFFu) != 0u)
                        {
                            sliceHasFluid = true;
                            break;
                        }
                    }

                    if (!sliceHasFluid)
                    {
                        continue;
                    }

                    cornerBuiltType.Clear();
                    planarPoolRenderIds.Clear();
                    mergedCellMask16.Clear();

                    // A) 2B Planar Greedy Quad on-secimi: Tam ic durgun deniz/gol hucrelerini belirle ve 4x4m kuadlara birlestir!
                    for (int lz = 0; lz < 16; lz++)
                    {
                        int rowOffset = yOffset | (lz << 4);
                        for (int lx = 0; lx < 16; lx++)
                        {
                            int cellIdx = rowOffset | lx;
                            ushort rawFluid = centerFluids[cellIdx];
                            byte level = (byte)(rawFluid & 0xFFu);
                            byte fType = (byte)((rawFluid >> 8) & 0x03u);
                            if (level == 0 || fType == 0) continue;

                            if (suppressUndergroundCaveFluids && y < 74 && HasThickUndergroundCaveRoof(centerSolids, lx, y, lz, maxBufferY))
                            {
                                continue;
                            }

                            ushort aboveFluid = (y + 1 < maxBufferY) ? centerFluids[((y + 1) << 8) | (lz << 4) | lx] : (ushort)0;
                            bool hasFreeTop = ((aboveFluid & 0xFFu) == 0u) || (((aboveFluid >> 8) & 0x03u) != fType);
                            if (!hasFreeTop) continue;

                            ushort belowFluid = (y > 0) ? centerFluids[((y - 1) << 8) | (lz << 4) | lx] : (ushort)0;
                            uint belowSolid = (y > 0) ? centerSolids[((y - 1) << 8) | (lz << 4) | lx] : 0u;
                            bool hasExposedBottom = (y == 0) ||
                                (((belowFluid & 0xFFu) == 0u || ((belowFluid >> 8) & 0x03u) != fType) &&
                                 !VoxelBitUtility.IsPhysicallySolid(belowSolid));
                            if (hasExposedBottom) continue;

                            // Durgun ve kinematik degisimi 0 olmali
                            if (nb.KinInit[4] && (uint)cellIdx < (uint)nb.PrevMass[4]!.Length)
                            {
                                if (nb.PrevMass[4]![cellIdx] != level ||
                                    nb.VelX[4]![cellIdx] != 0 ||
                                    nb.VelZ[4]![cellIdx] != 0)
                                {
                                    continue;
                                }
                            }

                            RichCornerResult c00 = GetOrComputeFreeSurfaceCorner(nb, lx, lz, y, fType, maxBufferY, cornerGrid, cornerBuiltType);
                            RichCornerResult c10 = GetOrComputeFreeSurfaceCorner(nb, lx + 1, lz, y, fType, maxBufferY, cornerGrid, cornerBuiltType);
                            RichCornerResult c11 = GetOrComputeFreeSurfaceCorner(nb, lx + 1, lz + 1, y, fType, maxBufferY, cornerGrid, cornerBuiltType);
                            RichCornerResult c01 = GetOrComputeFreeSurfaceCorner(nb, lx, lz + 1, y, fType, maxBufferY, cornerGrid, cornerBuiltType);

                            if (!c00.IsPoolRegime || !c10.IsPoolRegime || !c11.IsPoolRegime || !c01.IsPoolRegime) continue;
                            if (c00.HasOverfall || c10.HasOverfall || c11.HasOverfall || c01.HasOverfall) continue;
                            if (c00.SkirtOffset != Vector3.Zero || c10.SkirtOffset != Vector3.Zero ||
                                c11.SkirtOffset != Vector3.Zero || c01.SkirtOffset != Vector3.Zero) continue;

                            float refY = c00.TopCurrY;
                            if (MathF.Abs(c10.TopCurrY - refY) > 0.0015f ||
                                MathF.Abs(c11.TopCurrY - refY) > 0.0015f ||
                                MathF.Abs(c01.TopCurrY - refY) > 0.0015f)
                            {
                                continue;
                            }

                            if (MathF.Abs(c00.TopCurrY - c00.TopPrevY) > 0.0015f ||
                                MathF.Abs(c10.TopCurrY - c10.TopPrevY) > 0.0015f ||
                                MathF.Abs(c11.TopCurrY - c11.TopPrevY) > 0.0015f ||
                                MathF.Abs(c01.TopCurrY - c01.TopPrevY) > 0.0015f)
                            {
                                continue;
                            }

                            if (MathF.Abs(c00.VelX) > 0.02f || MathF.Abs(c00.VelZ) > 0.02f ||
                                MathF.Abs(c11.VelX) > 0.02f || MathF.Abs(c11.VelZ) > 0.02f)
                            {
                                continue;
                            }

                            // 4 kardinal komsuda da ust/ic kenar gerektirmeyecek ayni sivi veya yamac-havuz baglantisi olmali
                            if (!HasSlopeOrPoolConnectedFluidAt(nb, lx - 1, y, lz, fType, true, maxBufferY, out _) ||
                                !HasSlopeOrPoolConnectedFluidAt(nb, lx + 1, y, lz, fType, true, maxBufferY, out _) ||
                                !HasSlopeOrPoolConnectedFluidAt(nb, lx, y, lz - 1, fType, true, maxBufferY, out _) ||
                                !HasSlopeOrPoolConnectedFluidAt(nb, lx, y, lz + 1, fType, true, maxBufferY, out _))
                            {
                                continue;
                            }

                            planarPoolRenderIds[(lz << 4) | lx] = ResolveFluidRenderIdFromBits(fType, lx, y, lz, nb.CenterSalinity);
                        }
                    }

                    EmitGreedyMergedPlanarPoolQuads(nb, y, maxBufferY, cornerGrid, planarPoolRenderIds, mergedCellMask16, outRichTriangles);

                    // B) Kiyi, Nehir, Yamac, Selale ve Marching-Squares Ic L-Kose Hucreleri
                    for (int lz = 0; lz < 16; lz++)
                    {
                        int rowOffset = yOffset | (lz << 4);
                        for (int lx = 0; lx < 16; lx++)
                        {
                            if (((mergedCellMask16[lz] >> lx) & 1) != 0)
                            {
                                continue; // 2B Greedy Quad tarafindan zaten tek parca oruldu!
                            }

                            int cellIdx = rowOffset | lx;
                            ushort rawFluid = centerFluids[cellIdx];
                            byte level = (byte)(rawFluid & 0xFFu);
                            byte fType = (byte)((rawFluid >> 8) & 0x03u);

                            if (level == 0 || fType == 0)
                            {
                                // Kuru hucre: Capraz (45°) akista veya capraz sahilde 90° merdiven disini yok eden
                                // Marching Squares tek-kose (Case 1, 2, 4, 8) diyagonal ic L-kose dolgusunu kontrol et!
                                TryEmitMarchingSquaresConcaveCorners(nb, lx, y, lz, maxBufferY, cornerGrid, cornerBuiltType, outRichTriangles);
                                continue;
                            }

                            byte pressureHead = (byte)((rawFluid >> 11) & 0x1Fu);

                            if (suppressUndergroundCaveFluids && y < 74 && HasThickUndergroundCaveRoof(centerSolids, lx, y, lz, maxBufferY))
                            {
                                continue;
                            }

                            ushort aboveFluid = (y + 1 < maxBufferY) ? centerFluids[((y + 1) << 8) | (lz << 4) | lx] : (ushort)0;
                            byte aboveMass = (byte)(aboveFluid & 0xFFu);
                            bool hasAboveSameType = aboveMass > 0 && (((aboveFluid >> 8) & 0x03u) == fType);

                            // CFL Yamac Gecis Korumasi (AK-7): Yamactan asagi inen ince yagmurda (level < 180 && aboveMass < 120)
                            // alt hucre henuz tam dolmadan ustten damla geciyorsa gomulu 1m okyanus sutunu uretme; yuzeyi ust hucre cizer!
                            if (hasAboveSameType && level < 180 && aboveMass < 120)
                            {
                                continue;
                            }

                            bool hasFreeTopSurface = !hasAboveSameType;

                            ushort belowFluid = (y > 0) ? centerFluids[((y - 1) << 8) | (lz << 4) | lx] : (ushort)0;
                            uint belowSolid = (y > 0) ? centerSolids[((y - 1) << 8) | (lz << 4) | lx] : 0u;
                            bool hasExposedBottom = (y == 0) ||
                                (((belowFluid & 0xFFu) == 0u || ((belowFluid >> 8) & 0x03u) != fType) &&
                                 !VoxelBitUtility.IsPhysicallySolid(belowSolid));

                            // Gomulu su hucresi (!hasFreeTopSurface) ne alt yuzeye ne de acik hava selale kenarina sahipse 0 ucgen -> aninda atla!
                            if (!hasFreeTopSurface && !hasExposedBottom && !HasAnyOpenAirSideNeighbor(nb, lx, y, lz, fType))
                            {
                                continue;
                            }

                            byte renderId = ResolveFluidRenderIdFromBits(fType, lx, y, lz, nb.CenterSalinity);

                            // Paket 4B.2 Yuksekten Dokulen Su Silindirik Sutun Tamiri (706fc114.jpg):
                            // Havada serbest dusen sivi hucrelerini 1m x 1m keskin koseli beyaz kutu (veya 4 yone firlayan duz kagit perde)
                            // olarak ormek yerine, merkeze daraltilmis 8-kenarli puruzsuz yari-seffaf su sutunu olarak or!
                            if (y >= 2 && IsMidAirFreeFallStreamCell(nb, lx, y, lz, fType))
                            {
                                byte fallPrevMass = (nb.KinInit[4] && (uint)cellIdx < (uint)nb.PrevMass[4]!.Length)
                                    ? nb.PrevMass[4]![cellIdx]
                                    : level;
                                EmitMidAirFallingStreamSegment(
                                    nb, lx, y, lz, fType, renderId, level, fallPrevMass, maxBufferY, outRichTriangles);
                                continue;
                            }

                            RichCornerResult c00 = hasFreeTopSurface && !hasExposedBottom
                                ? GetOrComputeFreeSurfaceCorner(nb, lx, lz, y, fType, maxBufferY, cornerGrid, cornerBuiltType)
                                : EvaluateRichCornerFast(nb, lx, lz, y, fType, hasFreeTopSurface, !hasExposedBottom, maxBufferY);
                            RichCornerResult c10 = hasFreeTopSurface && !hasExposedBottom
                                ? GetOrComputeFreeSurfaceCorner(nb, lx + 1, lz, y, fType, maxBufferY, cornerGrid, cornerBuiltType)
                                : EvaluateRichCornerFast(nb, lx + 1, lz, y, fType, hasFreeTopSurface, !hasExposedBottom, maxBufferY);
                            RichCornerResult c11 = hasFreeTopSurface && !hasExposedBottom
                                ? GetOrComputeFreeSurfaceCorner(nb, lx + 1, lz + 1, y, fType, maxBufferY, cornerGrid, cornerBuiltType)
                                : EvaluateRichCornerFast(nb, lx + 1, lz + 1, y, fType, hasFreeTopSurface, !hasExposedBottom, maxBufferY);
                            RichCornerResult c01 = hasFreeTopSurface && !hasExposedBottom
                                ? GetOrComputeFreeSurfaceCorner(nb, lx, lz + 1, y, fType, maxBufferY, cornerGrid, cornerBuiltType)
                                : EvaluateRichCornerFast(nb, lx, lz + 1, y, fType, hasFreeTopSurface, !hasExposedBottom, maxBufferY);

                            RichContourVertex v00 = BuildCornerVertex(lx, lz, in c00, -1, 0);
                            RichContourVertex v10 = BuildCornerVertex(lx + 1, lz, in c10, 0, -1);
                            RichContourVertex v11 = BuildCornerVertex(lx + 1, lz + 1, in c11, 1, 0);
                            RichContourVertex v01 = BuildCornerVertex(lx, lz + 1, in c01, 0, 1);

                            bool wetNegX = HasSlopeOrPoolConnectedFluidAt(nb, lx - 1, y, lz, fType, hasFreeTopSurface, maxBufferY, out bool wallNegX);
                            bool wetPosX = HasSlopeOrPoolConnectedFluidAt(nb, lx + 1, y, lz, fType, hasFreeTopSurface, maxBufferY, out bool wallPosX);
                            bool wetNegZ = HasSlopeOrPoolConnectedFluidAt(nb, lx, y, lz - 1, fType, hasFreeTopSurface, maxBufferY, out bool wallNegZ);
                            bool wetPosZ = HasSlopeOrPoolConnectedFluidAt(nb, lx, y, lz + 1, fType, hasFreeTopSurface, maxBufferY, out bool wallPosZ);

                            bool wet00Diag = HasSlopeOrPoolConnectedFluidAt(nb, lx - 1, y, lz - 1, fType, hasFreeTopSurface, maxBufferY, out bool wall00Diag);
                            bool wet01Diag = HasSlopeOrPoolConnectedFluidAt(nb, lx - 1, y, lz + 1, fType, hasFreeTopSurface, maxBufferY, out bool wall01Diag);
                            bool wet11Diag = HasSlopeOrPoolConnectedFluidAt(nb, lx + 1, y, lz + 1, fType, hasFreeTopSurface, maxBufferY, out bool wall11Diag);
                            bool wet10Diag = HasSlopeOrPoolConnectedFluidAt(nb, lx + 1, y, lz - 1, fType, hasFreeTopSurface, maxBufferY, out bool wall10Diag);

                            // Paket 4B.2 Kucuk Su Yatay Daralmasi (AK-6):
                            // YALNIZCA tek basina duran durgun mikro birikintiler (level <= 8, komsusu yok) merkeze daralir;
                            // yamacta akan veya komsu su hucresine bagli olan yagmur sulari 0..1 boyuna kenar temasini korur
                            // ki su hucreden hucreye "isinlanir" gibi kopuk noktalar halinde degil kesintisiz serit halinde aksin!
                            uint selfSolid = centerSolids[cellIdx];
                            bool isSettledCell = (rawFluid & 0x0400u) != 0u;
                            bool isArchFloor = (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolid) && VoxelBitUtility.GetShapeId(belowSolid) != 0) ||
                                               (VoxelBitUtility.GetMaterialId(selfSolid) != 0 && VoxelBitUtility.GetShapeId(selfSolid) != 0);
                            bool isIsolatedMicroPuddle = !wetNegX && !wetPosX && !wetNegZ && !wetPosZ &&
                                                         !wet00Diag && !wet01Diag && !wet11Diag && !wet10Diag &&
                                                         !c00.HasSlopeConnection && !c10.HasSlopeConnection &&
                                                         !c11.HasSlopeConnection && !c01.HasSlopeConnection;
                            // ONEMLI (b9c3b024.jpg Tamiri): YALNIZCA tamamen durgunlasmis (isSettledCell == true) mikro su birikintileri daralir!
                            // Hareket halindeki yagmur damlalari (!isSettledCell) asla 0.08 m^2 <-> 2.0 m^2 arasinda yanip sonerek "yok olmus/isinlanmis" gibi gorunmez!
                            if (hasFreeTopSurface && isSettledCell && !isArchFloor && level <= 12 && isIsolatedMicroPuddle && !c00.IsPoolRegime && !c11.IsPoolRegime)
                            {
                                float shrinkRadius = Math.Clamp(0.16f + 0.042f * level, 0.18f, 1.0f);
                                ApplyMicroFootprintShrink(
                                    lx + 0.5f, lz + 0.5f, shrinkRadius,
                                    wallNegX, wallPosX, wallNegZ, wallPosZ,
                                    ref v00, ref v10, ref v11, ref v01);
                            }

                            float dYdX = 0.5f * ((v10.TopCurr.Y + v11.TopCurr.Y) - (v00.TopCurr.Y + v01.TopCurr.Y));
                            float dYdZ = 0.5f * ((v01.TopCurr.Y + v11.TopCurr.Y) - (v00.TopCurr.Y + v10.TopCurr.Y));
                            Vector3 smoothCellNormal = Vector3.Normalize(new Vector3(-dYdX, 1.0f, -dYdZ));
                            v00.SmoothTopNormal = smoothCellNormal;
                            v10.SmoothTopNormal = smoothCellNormal;
                            v11.SmoothTopNormal = smoothCellNormal;
                            v01.SmoothTopNormal = smoothCellNormal;

                            bool bevel00 = !wetNegX && !wetNegZ && !wet00Diag && !wallNegX && !wallNegZ && !wall00Diag && !c00.HasSlopeConnection;
                            bool bevel01 = !wetNegX && !wetPosZ && !wet01Diag && !wallNegX && !wallPosZ && !wall01Diag && !c01.HasSlopeConnection;
                            bool bevel11 = !wetPosX && !wetPosZ && !wet11Diag && !wallPosX && !wallPosZ && !wall11Diag && !c11.HasSlopeConnection;
                            bool bevel10 = !wetPosX && !wetNegZ && !wet10Diag && !wallPosX && !wallNegZ && !wall10Diag && !c10.HasSlopeConnection;

                            RichContourVertex midNegX = LerpRichContourVertex(in v00, in v01, 0.5f, -1, 0);
                            RichContourVertex midPosZ = LerpRichContourVertex(in v01, in v11, 0.5f, 0, 1);
                            RichContourVertex midPosX = LerpRichContourVertex(in v11, in v10, 0.5f, 1, 0);
                            RichContourVertex midNegZ = LerpRichContourVertex(in v10, in v00, 0.5f, 0, -1);

                            byte selfCap = ComputeSdfPorousCapacity(selfSolid);
                            float selfBedY = !hasExposedBottom
                                ? y
                                : (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolid)
                                    ? ComputeHydrostaticSurfaceNetsBedMeters(belowSolid, selfSolid, y)
                                    : y);
                            // Not: Ustteki uclu kosulda !hasExposedBottom zemine oturan hucre demektir; dogru yatak kotunu al:
                            if (!hasExposedBottom)
                            {
                                bool hasBelowSame = y > 0 && (belowFluid & 0xFFu) > 0u && ((belowFluid >> 8) & 0x03u) == fType;
                                selfBedY = (!hasBelowSame && y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolid))
                                    ? ComputeHydrostaticSurfaceNetsBedMeters(belowSolid, selfSolid, y)
                                    : y;
                            }

                            float selfCurrThick = MassToFluidThicknessWithCapacity(level, selfCap);
                            byte selfPrevMass = (nb.KinInit[4] && (uint)cellIdx < (uint)nb.PrevMass[4]!.Length)
                                ? nb.PrevMass[4]![cellIdx]
                                : level;
                            float selfPrevThick = MassToFluidThicknessWithCapacity(selfPrevMass, selfCap);
                            bool cellHasOverfall = c00.HasOverfall || c10.HasOverfall || c11.HasOverfall || c01.HasOverfall;
                            bool cellHasSlopeConn = c00.HasSlopeConnection || c10.HasSlopeConnection || c11.HasSlopeConnection || c01.HasSlopeConnection;

                            float avgCornerCurrY = 0.25f * (v00.TopCurr.Y + v10.TopCurr.Y + v11.TopCurr.Y + v01.TopCurr.Y);
                            float avgCornerPrevY = 0.25f * (c00.TopPrevY + c10.TopPrevY + c11.TopPrevY + c01.TopPrevY);
                            float centerCurrY = hasFreeTopSurface && cellHasOverfall && !cellHasSlopeConn
                                ? MathF.Min(y + 1.0f, MathF.Max(avgCornerCurrY, selfBedY + selfCurrThick))
                                : avgCornerCurrY;
                            float centerPrevY = hasFreeTopSurface && cellHasOverfall && !cellHasSlopeConn
                                ? MathF.Min(y + 1.0f, MathF.Max(avgCornerPrevY, selfBedY + selfPrevThick))
                                : avgCornerPrevY;

                            Vector3 pCenter = new Vector3(
                                0.25f * (v00.TopCurr.X + v10.TopCurr.X + v11.TopCurr.X + v01.TopCurr.X),
                                centerCurrY,
                                0.25f * (v00.TopCurr.Z + v10.TopCurr.Z + v11.TopCurr.Z + v01.TopCurr.Z));
                            Vector3 bCenter = 0.25f * (v00.Bottom + v10.Bottom + v11.Bottom + v01.Bottom);

                            float selfVelX = 0f;
                            float selfVelZ = 0f;
                            if (nb.KinInit[4] && (uint)cellIdx < (uint)nb.VelX[4]!.Length)
                            {
                                selfVelX = nb.VelX[4]![cellIdx] * (MaxKinematicsVelocityMps / 127.0f);
                                selfVelZ = nb.VelZ[4]![cellIdx] * (MaxKinematicsVelocityMps / 127.0f);
                            }

                            byte centerEncDy = FluidCornerSample.EncodeSignedUnitToByte(
                                Math.Clamp(centerCurrY - centerPrevY, -1.0f, 1.0f));
                            byte centerEncVx = FluidCornerSample.EncodeSignedUnitToByte(selfVelX / MaxKinematicsVelocityMps);
                            byte centerEncVz = FluidCornerSample.EncodeSignedUnitToByte(selfVelZ / MaxKinematicsVelocityMps);
                            float centerDepth = 0.25f * (c00.DepthMeters + c10.DepthMeters + c11.DepthMeters + c01.DepthMeters);

                            RichContourVertex centerTopSample = new RichContourVertex
                            {
                                TopCurr = pCenter,
                                Bottom = bCenter,
                                SmoothTopNormal = smoothCellNormal,
                                EncodedSubTickDy = centerEncDy,
                                EncodedVelX = centerEncVx,
                                EncodedVelZ = centerEncVz,
                                DepthMeters = centerDepth
                            };

                            int ringCount = 0;

                            // T-Junction Sifir-Yarik Kurali: Her iki ucu da pahsiz (!bevel) olan duz hucrelerde
                            // gereksiz 0.5m orta tepe (midNegX/PosZ/PosX/NegZ) ekleme; dogrudan 1.0m kose kenarlari kullan!
                            if (!bevel00 && !bevel01 && !bevel11 && !bevel10)
                            {
                                v00.EdgeDirX = -1; v00.EdgeDirZ = 0;
                                v01.EdgeDirX = 0;  v01.EdgeDirZ = 1;
                                v11.EdgeDirX = 1;  v11.EdgeDirZ = 0;
                                v10.EdgeDirX = 0;  v10.EdgeDirZ = -1;
                                ring[ringCount++] = v00;
                                ring[ringCount++] = v01;
                                ring[ringCount++] = v11;
                                ring[ringCount++] = v10;
                            }
                            else
                            {
                                if (bevel00)
                                {
                                    RichContourVertex arc00 = LerpRichContourVertex(in centerTopSample, in v00, 0.7071f, 0, 0);
                                    ring[ringCount++] = arc00;
                                    ring[ringCount++] = midNegX;
                                }
                                else
                                {
                                    ring[ringCount++] = v00;
                                    if (bevel01) ring[ringCount++] = midNegX;
                                }

                                if (bevel01)
                                {
                                    RichContourVertex arc01 = LerpRichContourVertex(in centerTopSample, in v01, 0.7071f, 0, 0);
                                    ring[ringCount - 1].EdgeDirX = 0;
                                    ring[ringCount - 1].EdgeDirZ = 0;
                                    ring[ringCount++] = arc01;
                                    ring[ringCount++] = midPosZ;
                                }
                                else
                                {
                                    ring[ringCount++] = v01;
                                    if (bevel11) ring[ringCount++] = midPosZ;
                                }

                                if (bevel11)
                                {
                                    RichContourVertex arc11 = LerpRichContourVertex(in centerTopSample, in v11, 0.7071f, 0, 0);
                                    ring[ringCount - 1].EdgeDirX = 0;
                                    ring[ringCount - 1].EdgeDirZ = 0;
                                    ring[ringCount++] = arc11;
                                    ring[ringCount++] = midPosX;
                                }
                                else
                                {
                                    ring[ringCount++] = v11;
                                    if (bevel10) ring[ringCount++] = midPosX;
                                }

                                if (bevel10)
                                {
                                    RichContourVertex arc10 = LerpRichContourVertex(in centerTopSample, in v10, 0.7071f, 0, 0);
                                    ring[ringCount - 1].EdgeDirX = 0;
                                    ring[ringCount - 1].EdgeDirZ = 0;
                                    ring[ringCount++] = arc10;
                                    midNegZ.EdgeDirX = 0;
                                    midNegZ.EdgeDirZ = bevel00 ? (sbyte)0 : (sbyte)-1;
                                    ring[ringCount++] = midNegZ;
                                }
                                else
                                {
                                    ring[ringCount++] = v10;
                                    if (bevel00)
                                    {
                                        midNegZ.EdgeDirX = 0;
                                        midNegZ.EdgeDirZ = 0;
                                        ring[ringCount++] = midNegZ;
                                    }
                                }
                            }

                            if (!bevel00 && !bevel01 && !bevel11 && !bevel10 && !cellHasOverfall && hasFreeTopSurface && !hasExposedBottom)
                            {
                                // Duz kose kenarli (selale savak esigi olmayan) hucre: 8 ucgenli merkez yelpazesi yerine 2 dogrudan kuad ucgeni (4x daha az ucgen!)
                                FluidCornerSample s00 = MakeSample(v00.TopCurr, v00.SmoothTopNormal, renderId, v00.EncodedSubTickDy, v00.EncodedVelX, v00.EncodedVelZ, v00.DepthMeters, 0.0f);
                                FluidCornerSample s01 = MakeSample(v01.TopCurr, v01.SmoothTopNormal, renderId, v01.EncodedSubTickDy, v01.EncodedVelX, v01.EncodedVelZ, v01.DepthMeters, 0.0f);
                                FluidCornerSample s11 = MakeSample(v11.TopCurr, v11.SmoothTopNormal, renderId, v11.EncodedSubTickDy, v11.EncodedVelX, v11.EncodedVelZ, v11.DepthMeters, 0.0f);
                                FluidCornerSample s10 = MakeSample(v10.TopCurr, v10.SmoothTopNormal, renderId, v10.EncodedSubTickDy, v10.EncodedVelX, v10.EncodedVelZ, v10.DepthMeters, 0.0f);
                                outRichTriangles.Add(new FluidRichTriangle { C0 = s00, C1 = s01, C2 = s11 });
                                outRichTriangles.Add(new FluidRichTriangle { C0 = s00, C1 = s11, C2 = s10 });

                                for (int i = 0; i < 4; i++)
                                {
                                    int next = (i + 1) & 3;
                                    RichContourVertex va = ring[i];
                                    RichContourVertex vb = ring[next];
                                    EmitRichSideOrWaterfallCurtain(
                                        nb, lx, y, lz, va.EdgeDirX, va.EdgeDirZ, fType, renderId,
                                        level, pressureHead, selfVelX, selfVelZ, centerCurrY,
                                        hasFreeTopSurface, isArchFloor, maxBufferY,
                                        in vb, in va, outRichTriangles);
                                }
                            }
                            else
                            {
                                for (int i = 0; i < ringCount; i++)
                                {
                                    int next = (i + 1 == ringCount) ? 0 : (i + 1);
                                    RichContourVertex va = ring[i];
                                    RichContourVertex vb = ring[next];

                                    if (hasFreeTopSurface)
                                    {
                                        float topFoam = (cellHasOverfall && !cellHasSlopeConn && level >= 28) ? 0.12f : 0.0f;
                                        outRichTriangles.Add(new FluidRichTriangle
                                        {
                                            C0 = MakeSample(pCenter, smoothCellNormal, renderId, centerEncDy, centerEncVx, centerEncVz, centerDepth, 0.0f),
                                            C1 = MakeSample(va.TopCurr, va.SmoothTopNormal, renderId, va.EncodedSubTickDy, va.EncodedVelX, va.EncodedVelZ, va.DepthMeters, topFoam),
                                            C2 = MakeSample(vb.TopCurr, va.SmoothTopNormal, renderId, vb.EncodedSubTickDy, vb.EncodedVelX, vb.EncodedVelZ, vb.DepthMeters, topFoam)
                                        });
                                    }

                                    if (hasExposedBottom)
                                    {
                                        Vector3 botNorm = SafeCrossNormalize(vb.Bottom - bCenter, va.Bottom - bCenter, -Vector3.UnitY);
                                        outRichTriangles.Add(new FluidRichTriangle
                                        {
                                            C0 = MakeSample(bCenter, botNorm, renderId, 128, centerEncVx, centerEncVz, centerDepth, 0.0f),
                                            C1 = MakeSample(vb.Bottom, botNorm, renderId, 128, vb.EncodedVelX, vb.EncodedVelZ, vb.DepthMeters, 0.0f),
                                            C2 = MakeSample(va.Bottom, botNorm, renderId, 128, va.EncodedVelX, va.EncodedVelZ, va.DepthMeters, 0.0f)
                                        });
                                    }

                                    if (va.EdgeDirX == 0 && va.EdgeDirZ == 0)
                                    {
                                        if (isArchFloor || hasExposedBottom)
                                        {
                                            EmitRichBevelSkirtQuad(in vb, in va, renderId, outRichTriangles);
                                        }
                                    }
                                    else
                                    {
                                        EmitRichSideOrWaterfallCurtain(
                                            nb, lx, y, lz, va.EdgeDirX, va.EdgeDirZ, fType, renderId,
                                            level, pressureHead, selfVelX, selfVelZ, centerCurrY,
                                            hasFreeTopSurface, isArchFloor || hasExposedBottom, maxBufferY,
                                            in vb, in va, outRichTriangles);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                nb.ClearReferences();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RichCornerResult GetOrComputeFreeSurfaceCorner(
            FluidNeighborhood3x3Cache nb,
            int cornerX,
            int cornerZ,
            int y,
            byte fType,
            int maxBufferY,
            Span<RichCornerResult> cornerGrid,
            Span<byte> cornerBuiltType)
        {
            int idx = cornerZ * 17 + cornerX;
            if (cornerBuiltType[idx] == fType)
            {
                return cornerGrid[idx];
            }

            RichCornerResult res = EvaluateRichCornerFast(nb, cornerX, cornerZ, y, fType, selfHasFreeTopSurface: true, embedFoundationIntoGround: true, maxBufferY);
            cornerGrid[idx] = res;
            cornerBuiltType[idx] = fType;
            return res;
        }

        /// <summary>
        /// Kısıtlı Dörtlü-Ağaç (Restricted Quadtree) Sifir-T-Junction Planar Havuz Birlestirici:
        /// - 16x16 dilim 4x4 bloklara ({0,4,8,12}) bolunur.
        /// - Tam 4x4 olan bir blok, komsu 4x4 blogu da tam birlesmisse o kenarda tek 4m kenar kullanir (acik deniz == 32 ucgen);
        /// - Komsu 4x4 blok parcaliysa (sahil/ada/aktif dalga gecisi!), paylasilan kenar 4 adet 1.0m segmente bolunup
        ///   blok merkezine dikilir (Crack-Stitching) -> Ruzgar dalgasi ve yuzucu halkasi altinda 0.000m T-Junction yarigi!
        /// </summary>
        private static void EmitGreedyMergedPlanarPoolQuads(
            FluidNeighborhood3x3Cache nb,
            int y,
            int maxBufferY,
            ReadOnlySpan<RichCornerResult> cornerGrid,
            ReadOnlySpan<byte> planarPoolRenderIds,
            Span<ushort> mergedCellMask16,
            List<FluidRichTriangle> outRichTriangles)
        {
            Span<byte> block4x4RenderId = stackalloc byte[16];
            Span<float> block4x4RefY = stackalloc float[16];

            for (int bz = 0; bz < 4; bz++)
            {
                for (int bx = 0; bx < 4; bx++)
                {
                    int x0 = bx << 2;
                    int z0 = bz << 2;
                    byte id0 = planarPoolRenderIds[(z0 << 4) | x0];
                    if (id0 == 0) continue;

                    float refY = cornerGrid[z0 * 17 + x0].TopCurrY;
                    bool fullBlock = true;
                    for (int dz = 0; dz < 4 && fullBlock; dz++)
                    {
                        int z = z0 + dz;
                        for (int dx = 0; dx < 4; dx++)
                        {
                            int x = x0 + dx;
                            if (planarPoolRenderIds[(z << 4) | x] != id0 ||
                                MathF.Abs(cornerGrid[z * 17 + x].TopCurrY - refY) > 0.0015f ||
                                MathF.Abs(cornerGrid[(z + 1) * 17 + (x + 1)].TopCurrY - refY) > 0.0015f)
                            {
                                fullBlock = false;
                                break;
                            }
                        }
                    }

                    if (fullBlock)
                    {
                        block4x4RenderId[(bz << 2) | bx] = id0;
                        block4x4RefY[(bz << 2) | bx] = refY;
                        ushort spanBits = (ushort)(0x0Fu << x0);
                        for (int dz = 0; dz < 4; dz++)
                        {
                            mergedCellMask16[z0 + dz] |= spanBits;
                        }
                    }
                }
            }

            // 1) Tam 4x4 bloklari komsu blok durumuna gore dikişsiz (crack-stitched) veya saf 2-ucgen kuad olarak uret
            for (int bz = 0; bz < 4; bz++)
            {
                for (int bx = 0; bx < 4; bx++)
                {
                    int bIdx = (bz << 2) | bx;
                    byte renderId = block4x4RenderId[bIdx];
                    if (renderId == 0) continue;

                    float refY = block4x4RefY[bIdx];
                    int x0 = bx << 2;
                    int z0 = bz << 2;
                    int x1 = x0 + 4;
                    int z1 = z0 + 4;

                    bool coarseNegX = bx > 0
                        ? (block4x4RenderId[(bz << 2) | (bx - 1)] == renderId && MathF.Abs(block4x4RefY[(bz << 2) | (bx - 1)] - refY) <= 0.0015f)
                        : IsNeighborChunk4x4BlockUniformPool(nb, -4, z0, y, maxBufferY);
                    bool coarsePosX = bx < 3
                        ? (block4x4RenderId[(bz << 2) | (bx + 1)] == renderId && MathF.Abs(block4x4RefY[(bz << 2) | (bx + 1)] - refY) <= 0.0015f)
                        : IsNeighborChunk4x4BlockUniformPool(nb, 16, z0, y, maxBufferY);
                    bool coarseNegZ = bz > 0
                        ? (block4x4RenderId[((bz - 1) << 2) | bx] == renderId && MathF.Abs(block4x4RefY[((bz - 1) << 2) | bx] - refY) <= 0.0015f)
                        : IsNeighborChunk4x4BlockUniformPool(nb, x0, -4, y, maxBufferY);
                    bool coarsePosZ = bz < 3
                        ? (block4x4RenderId[((bz + 1) << 2) | bx] == renderId && MathF.Abs(block4x4RefY[((bz + 1) << 2) | bx] - refY) <= 0.0015f)
                        : IsNeighborChunk4x4BlockUniformPool(nb, x0, 16, y, maxBufferY);

                    if (coarseNegX && coarsePosX && coarseNegZ && coarsePosZ)
                    {
                        FluidCornerSample s00 = MakePoolGridSample(cornerGrid, x0, z0, refY, renderId);
                        FluidCornerSample s01 = MakePoolGridSample(cornerGrid, x0, z1, refY, renderId);
                        FluidCornerSample s11 = MakePoolGridSample(cornerGrid, x1, z1, refY, renderId);
                        FluidCornerSample s10 = MakePoolGridSample(cornerGrid, x1, z0, refY, renderId);
                        outRichTriangles.Add(new FluidRichTriangle { C0 = s00, C1 = s01, C2 = s11 });
                        outRichTriangles.Add(new FluidRichTriangle { C0 = s00, C1 = s11, C2 = s10 });
                    }
                    else
                    {
                        FluidCornerSample center = MakePoolGridSample(cornerGrid, x0 + 2, z0 + 2, refY, renderId);
                        // -X kenari: (x0, z0) -> (x0, z1)
                        EmitStitched4mEdge(cornerGrid, center, x0, z0, 0, 1, coarseNegX, refY, renderId, outRichTriangles);
                        // +Z kenari: (x0, z1) -> (x1, z1)
                        EmitStitched4mEdge(cornerGrid, center, x0, z1, 1, 0, coarsePosZ, refY, renderId, outRichTriangles);
                        // +X kenari: (x1, z1) -> (x1, z0)
                        EmitStitched4mEdge(cornerGrid, center, x1, z1, 0, -1, coarsePosX, refY, renderId, outRichTriangles);
                        // -Z kenari: (x1, z0) -> (x0, z0)
                        EmitStitched4mEdge(cornerGrid, center, x1, z0, -1, 0, coarseNegZ, refY, renderId, outRichTriangles);
                    }
                }
            }

            // 2) Parcali 4x4 bloklar icinde kalan tekil planar hucreleri 1m x 1m dikişsiz kuad olarak uret
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    if (((mergedCellMask16[z] >> x) & 1) != 0) continue;
                    byte renderId = planarPoolRenderIds[(z << 4) | x];
                    if (renderId == 0) continue;

                    mergedCellMask16[z] |= (ushort)(1 << x);
                    float refY = cornerGrid[z * 17 + x].TopCurrY;
                    FluidCornerSample s00 = MakePoolGridSample(cornerGrid, x, z, refY, renderId);
                    FluidCornerSample s01 = MakePoolGridSample(cornerGrid, x, z + 1, refY, renderId);
                    FluidCornerSample s11 = MakePoolGridSample(cornerGrid, x + 1, z + 1, refY, renderId);
                    FluidCornerSample s10 = MakePoolGridSample(cornerGrid, x + 1, z, refY, renderId);
                    outRichTriangles.Add(new FluidRichTriangle { C0 = s00, C1 = s01, C2 = s11 });
                    outRichTriangles.Add(new FluidRichTriangle { C0 = s00, C1 = s11, C2 = s10 });
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FluidCornerSample MakePoolGridSample(
            ReadOnlySpan<RichCornerResult> cornerGrid, int gx, int gz, float refY, byte renderId)
        {
            return MakeSample(new Vector3(gx, refY, gz), Vector3.UnitY, renderId, 128, 128, 128, cornerGrid[gz * 17 + gx].DepthMeters, 0f);
        }

        private static void EmitStitched4mEdge(
            ReadOnlySpan<RichCornerResult> cornerGrid,
            in FluidCornerSample center,
            int startX,
            int startZ,
            int stepX,
            int stepZ,
            bool isCoarse,
            float refY,
            byte renderId,
            List<FluidRichTriangle> outRichTriangles)
        {
            if (isCoarse)
            {
                FluidCornerSample a = MakePoolGridSample(cornerGrid, startX, startZ, refY, renderId);
                FluidCornerSample b = MakePoolGridSample(cornerGrid, startX + stepX * 4, startZ + stepZ * 4, refY, renderId);
                outRichTriangles.Add(new FluidRichTriangle { C0 = center, C1 = a, C2 = b });
                return;
            }

            for (int s = 0; s < 4; s++)
            {
                FluidCornerSample a = MakePoolGridSample(cornerGrid, startX + stepX * s, startZ + stepZ * s, refY, renderId);
                FluidCornerSample b = MakePoolGridSample(cornerGrid, startX + stepX * (s + 1), startZ + stepZ * (s + 1), refY, renderId);
                outRichTriangles.Add(new FluidRichTriangle { C0 = center, C1 = a, C2 = b });
            }
        }

        private static bool IsNeighborChunk4x4BlockUniformPool(
            FluidNeighborhood3x3Cache nb, int baseX, int baseZ, int y, int maxBufferY)
        {
            int cdx = baseX < 0 ? -1 : (baseX >= 16 ? 1 : 0);
            int cdz = baseZ < 0 ? -1 : (baseZ >= 16 ? 1 : 0);
            int slot = (cdz + 1) * 3 + (cdx + 1);
            if (nb.Fluids[slot] == null || nb.Solids[slot] == null)
            {
                // Komsu chunk yuklu degilse (dunya dis siniri veya izole tek-chunk testi) gereksiz dikis uretme
                return true;
            }

            // Komsu 4x4 blogun tam planar havuz olabilmesi icin kendi 4x4 alani + 1 hucrelik cevresi (6x6) de ayni durgun havuz olmalidir
            byte expectedType = 0;
            for (int dz = -1; dz <= 4; dz++)
            {
                int nz = baseZ + dz;
                for (int dx = -1; dx <= 4; dx++)
                {
                    int nx = baseX + dx;
                    nb.SampleCellFast(nx, y, nz, out uint sRaw, out ushort fRaw);
                    if (VoxelBitUtility.IsPhysicallySolid(sRaw)) return false;
                    byte mass = (byte)(fRaw & 0xFFu);
                    byte fType = (byte)((fRaw >> 8) & 0x03u);
                    if (mass < 200 || fType == 0) return false;
                    if (expectedType == 0) expectedType = fType;
                    else if (fType != expectedType) return false;

                    if (y > 0)
                    {
                        nb.SampleCellFast(nx, y - 1, nz, out uint belowS, out ushort belowF);
                        if (!VoxelBitUtility.IsPhysicallySolid(belowS) && (belowF & 0xFFu) < 200u)
                        {
                            return false;
                        }
                    }

                    if (y + 1 < maxBufferY)
                    {
                        nb.SampleCellFast(nx, y + 1, nz, out _, out ushort aRaw);
                        if ((aRaw & 0xFFu) != 0u) return false;
                    }

                    int ncdx = nx < 0 ? -1 : (nx >= 16 ? 1 : 0);
                    int ncdz = nz < 0 ? -1 : (nz >= 16 ? 1 : 0);
                    int nSlot = (ncdz + 1) * 3 + (ncdx + 1);
                    if (nb.KinInit[nSlot] && nb.PrevMass[nSlot] != null)
                    {
                        int nlx = nx - ncdx * 16;
                        int nlz = nz - ncdz * 16;
                        int nIdx = (y << 8) | (nlz << 4) | nlx;
                        if ((uint)nIdx < (uint)nb.PrevMass[nSlot]!.Length &&
                            (nb.PrevMass[nSlot]![nIdx] != mass ||
                             nb.VelX[nSlot]![nIdx] != 0 ||
                             nb.VelZ[nSlot]![nIdx] != 0))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private static void ApplyMicroFootprintShrink(
            float cx,
            float cz,
            float r,
            bool pinNegX,
            bool pinPosX,
            bool pinNegZ,
            bool pinPosZ,
            ref RichContourVertex v00,
            ref RichContourVertex v10,
            ref RichContourVertex v11,
            ref RichContourVertex v01)
        {
            if (!pinNegX)
            {
                v00.TopCurr.X = cx + (v00.TopCurr.X - cx) * r;
                v00.Bottom.X = v00.TopCurr.X;
                v01.TopCurr.X = cx + (v01.TopCurr.X - cx) * r;
                v01.Bottom.X = v01.TopCurr.X;
            }
            if (!pinPosX)
            {
                v10.TopCurr.X = cx + (v10.TopCurr.X - cx) * r;
                v10.Bottom.X = v10.TopCurr.X;
                v11.TopCurr.X = cx + (v11.TopCurr.X - cx) * r;
                v11.Bottom.X = v11.TopCurr.X;
            }
            if (!pinNegZ)
            {
                v00.TopCurr.Z = cz + (v00.TopCurr.Z - cz) * r;
                v00.Bottom.Z = v00.TopCurr.Z;
                v10.TopCurr.Z = cz + (v10.TopCurr.Z - cz) * r;
                v10.Bottom.Z = v10.TopCurr.Z;
            }
            if (!pinPosZ)
            {
                v01.TopCurr.Z = cz + (v01.TopCurr.Z - cz) * r;
                v01.Bottom.Z = v01.TopCurr.Z;
                v11.TopCurr.Z = cz + (v11.TopCurr.Z - cz) * r;
                v11.Bottom.Z = v11.TopCurr.Z;
            }
        }

        /// <summary>
        /// Marching Squares Tek-Kose (Case 1, 2, 4, 8) Diyagonal Ic L-Kose Dolgusu:
        /// Capraz (45°) bir olukta veya capraz kiyi seridinde kuru bir acik hava hucresinin bitisik iki kardinal
        /// komsusu ve aralarindaki capraz komsu ayni tipte sivi iceriyorsa, o 90° merdiven oyugunu puruzsuz
        /// 45° diyagonal ucgenle kapatir!
        /// </summary>
        private static void TryEmitMarchingSquaresConcaveCorners(
            FluidNeighborhood3x3Cache nb,
            int lx,
            int y,
            int lz,
            int maxBufferY,
            Span<RichCornerResult> cornerGrid,
            Span<byte> cornerBuiltType,
            List<FluidRichTriangle> outRichTriangles)
        {
            nb.SampleCellFast(lx, y, lz, out uint selfSolid, out _);
            if (VoxelBitUtility.IsPhysicallySolid(selfSolid))
            {
                return;
            }

            // Altinda y-1 katmaninda zaten serbest sivi yuzeyi varsa (bu hucre alt basamagin ustundeki bos havadir!) asla ustte ikiz ucgen cizme!
            if (y > 0)
            {
                nb.SampleCellFast(lx, y - 1, lz, out _, out ushort belowF);
                if ((belowF & 0xFFu) != 0u) return;
            }
            if (y + 1 < maxBufferY)
            {
                nb.SampleCellFast(lx, y + 1, lz, out _, out ushort aboveF);
                if ((aboveF & 0xFFu) != 0u) return;
            }

            byte wNegX = ProbeNeighborFluidTypeForConcave(nb, lx - 1, y, lz, maxBufferY);
            byte wPosX = ProbeNeighborFluidTypeForConcave(nb, lx + 1, y, lz, maxBufferY);
            byte wNegZ = ProbeNeighborFluidTypeForConcave(nb, lx, y, lz - 1, maxBufferY);
            byte wPosZ = ProbeNeighborFluidTypeForConcave(nb, lx, y, lz + 1, maxBufferY);

            int wetCardinals = (wNegX != 0 ? 1 : 0) + (wPosX != 0 ? 1 : 0) + (wNegZ != 0 ? 1 : 0) + (wPosZ != 0 ? 1 : 0);
            if (wetCardinals < 2) return;

            // Kose 00 (-X ve -Z islak, karsi kenarlardan en az biri kuru)
            if (wNegX != 0 && wNegX == wNegZ && (wPosX == 0 || wPosZ == 0) &&
                ProbeNeighborFluidTypeForConcave(nb, lx - 1, y, lz - 1, maxBufferY) == wNegX)
            {
                EmitSingleConcaveBridgeTriangle(nb, lx, y, lz, wNegX, 0, 0, maxBufferY, cornerGrid, cornerBuiltType, outRichTriangles);
            }

            // Kose 01 (-X ve +Z islak)
            if (wNegX != 0 && wNegX == wPosZ && (wPosX == 0 || wNegZ == 0) &&
                ProbeNeighborFluidTypeForConcave(nb, lx - 1, y, lz + 1, maxBufferY) == wNegX)
            {
                EmitSingleConcaveBridgeTriangle(nb, lx, y, lz, wNegX, 0, 1, maxBufferY, cornerGrid, cornerBuiltType, outRichTriangles);
            }

            // Kose 11 (+X ve +Z islak)
            if (wPosX != 0 && wPosX == wPosZ && (wNegX == 0 || wNegZ == 0) &&
                ProbeNeighborFluidTypeForConcave(nb, lx + 1, y, lz + 1, maxBufferY) == wPosX)
            {
                EmitSingleConcaveBridgeTriangle(nb, lx, y, lz, wPosX, 1, 1, maxBufferY, cornerGrid, cornerBuiltType, outRichTriangles);
            }

            // Kose 10 (+X ve -Z islak)
            if (wPosX != 0 && wPosX == wNegZ && (wNegX == 0 || wPosZ == 0) &&
                ProbeNeighborFluidTypeForConcave(nb, lx + 1, y, lz - 1, maxBufferY) == wPosX)
            {
                EmitSingleConcaveBridgeTriangle(nb, lx, y, lz, wPosX, 1, 0, maxBufferY, cornerGrid, cornerBuiltType, outRichTriangles);
            }
        }

        private static byte ProbeNeighborFluidTypeForConcave(FluidNeighborhood3x3Cache nb, int nx, int y, int nz, int maxBufferY)
        {
            nb.SampleCellFast(nx, y, nz, out uint s, out ushort f);
            if (!VoxelBitUtility.IsPhysicallySolid(s) && (f & 0xFFu) >= 16u)
            {
                byte ft = (byte)((f >> 8) & 0x03u);
                if (y + 1 < maxBufferY)
                {
                    nb.SampleCellFast(nx, y + 1, nz, out _, out ushort af);
                    if ((af & 0xFFu) != 0u && ((af >> 8) & 0x03u) == ft) return 0;
                }
                return ft;
            }

            if (VoxelBitUtility.IsPhysicallySolid(s) && VoxelBitUtility.GetShapeId(s) == 0 && y + 1 < maxBufferY)
            {
                nb.SampleCellFast(nx, y + 1, nz, out uint us, out ushort uf);
                if (!VoxelBitUtility.IsPhysicallySolid(us) && (uf & 0xFFu) >= 16u)
                {
                    return (byte)((uf >> 8) & 0x03u);
                }
            }

            if (!VoxelBitUtility.IsPhysicallySolid(s) && y > 0)
            {
                nb.SampleCellFast(nx, y - 1, nz, out uint ds, out ushort df);
                if (!VoxelBitUtility.IsPhysicallySolid(ds) && (df & 0xFFu) >= 16u)
                {
                    return (byte)((df >> 8) & 0x03u);
                }
            }

            return 0;
        }

        private static void EmitSingleConcaveBridgeTriangle(
            FluidNeighborhood3x3Cache nb,
            int lx,
            int y,
            int lz,
            byte fType,
            int cxOff,
            int czOff,
            int maxBufferY,
            Span<RichCornerResult> cornerGrid,
            Span<byte> cornerBuiltType,
            List<FluidRichTriangle> outRichTriangles)
        {
            RichCornerResult c00 = GetOrComputeFreeSurfaceCorner(nb, lx, lz, y, fType, maxBufferY, cornerGrid, cornerBuiltType);
            RichCornerResult c10 = GetOrComputeFreeSurfaceCorner(nb, lx + 1, lz, y, fType, maxBufferY, cornerGrid, cornerBuiltType);
            RichCornerResult c11 = GetOrComputeFreeSurfaceCorner(nb, lx + 1, lz + 1, y, fType, maxBufferY, cornerGrid, cornerBuiltType);
            RichCornerResult c01 = GetOrComputeFreeSurfaceCorner(nb, lx, lz + 1, y, fType, maxBufferY, cornerGrid, cornerBuiltType);

            RichContourVertex v00 = BuildCornerVertex(lx, lz, in c00, 0, 0);
            RichContourVertex v10 = BuildCornerVertex(lx + 1, lz, in c10, 0, 0);
            RichContourVertex v11 = BuildCornerVertex(lx + 1, lz + 1, in c11, 0, 0);
            RichContourVertex v01 = BuildCornerVertex(lx, lz + 1, in c01, 0, 0);

            byte renderId = ResolveFluidRenderIdFromBits(
                fType,
                Math.Clamp(lx + (cxOff == 0 ? -1 : 1), 0, 15),
                y,
                Math.Clamp(lz, 0, 15),
                nb.CenterSalinity);

            // Saat yonunde (yukari normal +Y veren) Marching Squares diyagonal kose ucgeni:
            RichContourVertex pCorner, pClockA, pClockB;
            if (cxOff == 0 && czOff == 0)
            {
                pCorner = v00;
                pClockA = LerpRichContourVertex(in v00, in v01, 0.5f, 0, 0);
                pClockB = LerpRichContourVertex(in v10, in v00, 0.5f, 0, 0);
            }
            else if (cxOff == 0 && czOff == 1)
            {
                pCorner = v01;
                pClockA = LerpRichContourVertex(in v01, in v11, 0.5f, 0, 0);
                pClockB = LerpRichContourVertex(in v00, in v01, 0.5f, 0, 0);
            }
            else if (cxOff == 1 && czOff == 1)
            {
                pCorner = v11;
                pClockA = LerpRichContourVertex(in v11, in v10, 0.5f, 0, 0);
                pClockB = LerpRichContourVertex(in v01, in v11, 0.5f, 0, 0);
            }
            else
            {
                pCorner = v10;
                pClockA = LerpRichContourVertex(in v10, in v00, 0.5f, 0, 0);
                pClockB = LerpRichContourVertex(in v11, in v10, 0.5f, 0, 0);
            }

            Vector3 nrm = SafeCrossNormalize(pClockA.TopCurr - pCorner.TopCurr, pClockB.TopCurr - pCorner.TopCurr, Vector3.UnitY);
            if (nrm.Y < 0f) nrm = -nrm;

            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(pCorner.TopCurr, nrm, renderId, pCorner.EncodedSubTickDy, pCorner.EncodedVelX, pCorner.EncodedVelZ, pCorner.DepthMeters, 0f),
                C1 = MakeSample(pClockA.TopCurr, nrm, renderId, pClockA.EncodedSubTickDy, pClockA.EncodedVelX, pClockA.EncodedVelZ, pClockA.DepthMeters, 0f),
                C2 = MakeSample(pClockB.TopCurr, nrm, renderId, pClockB.EncodedSubTickDy, pClockB.EncodedVelX, pClockB.EncodedVelZ, pClockB.DepthMeters, 0f)
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAnyOpenAirSideNeighbor(FluidNeighborhood3x3Cache nb, int lx, int y, int lz, byte fType)
        {
            return IsSideNeighborOpenAir(nb, lx - 1, y, lz, fType) ||
                   IsSideNeighborOpenAir(nb, lx + 1, y, lz, fType) ||
                   IsSideNeighborOpenAir(nb, lx, y, lz - 1, fType) ||
                   IsSideNeighborOpenAir(nb, lx, y, lz + 1, fType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSideNeighborOpenAir(FluidNeighborhood3x3Cache nb, int nx, int y, int nz, byte fType)
        {
            nb.SampleCellFast(nx, y, nz, out uint nSolid, out ushort nFluid);
            if (VoxelBitUtility.IsPhysicallySolid(nSolid)) return false;
            return (nFluid & 0xFFu) == 0u || ((nFluid >> 8) & 0x03u) != fType;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FluidCornerSample MakeSample(
            Vector3 posCurr,
            Vector3 normal,
            byte renderFluidId,
            byte encDy,
            byte encVx,
            byte encVz,
            float depthMeters,
            float waterfallFoam01)
        {
            return new FluidCornerSample
            {
                PositionCurr = posCurr,
                Normal = normal,
                RenderFluidId = renderFluidId,
                EncodedSubTickDy = encDy,
                EncodedVelX = encVx,
                EncodedVelZ = encVz,
                DepthMeters = depthMeters,
                WaterfallFoam01 = waterfallFoam01
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RichContourVertex BuildCornerVertex(
            float x,
            float z,
            in RichCornerResult c,
            sbyte dirX,
            sbyte dirZ)
        {
            return new RichContourVertex
            {
                TopCurr = new Vector3(x + c.SkirtOffset.X, c.TopCurrY, z + c.SkirtOffset.Z),
                Bottom = new Vector3(x + c.SkirtOffset.X, c.BedY, z + c.SkirtOffset.Z),
                EncodedSubTickDy = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(c.TopCurrY - c.TopPrevY, -1.0f, 1.0f)),
                EncodedVelX = FluidCornerSample.EncodeSignedUnitToByte(c.VelX / MaxKinematicsVelocityMps),
                EncodedVelZ = FluidCornerSample.EncodeSignedUnitToByte(c.VelZ / MaxKinematicsVelocityMps),
                DepthMeters = c.DepthMeters,
                EdgeDirX = dirX,
                EdgeDirZ = dirZ
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RichContourVertex LerpRichContourVertex(
            in RichContourVertex a,
            in RichContourVertex b,
            float t,
            sbyte edgeDirX,
            sbyte edgeDirZ)
        {
            Vector3 nrm = Vector3.Lerp(a.SmoothTopNormal, b.SmoothTopNormal, t);
            float nLenSq = nrm.LengthSquared();
            nrm = nLenSq > 1e-8f ? (nrm / MathF.Sqrt(nLenSq)) : Vector3.UnitY;

            return new RichContourVertex
            {
                TopCurr = Vector3.Lerp(a.TopCurr, b.TopCurr, t),
                Bottom = Vector3.Lerp(a.Bottom, b.Bottom, t),
                SmoothTopNormal = nrm,
                EncodedSubTickDy = (byte)Math.Clamp((int)MathF.Round(a.EncodedSubTickDy + (b.EncodedSubTickDy - a.EncodedSubTickDy) * t), 1, 255),
                EncodedVelX = (byte)Math.Clamp((int)MathF.Round(a.EncodedVelX + (b.EncodedVelX - a.EncodedVelX) * t), 1, 255),
                EncodedVelZ = (byte)Math.Clamp((int)MathF.Round(a.EncodedVelZ + (b.EncodedVelZ - a.EncodedVelZ) * t), 1, 255),
                DepthMeters = a.DepthMeters + (b.DepthMeters - a.DepthMeters) * t,
                EdgeDirX = edgeDirX,
                EdgeDirZ = edgeDirZ
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 SafeCrossNormalize(Vector3 a, Vector3 b, Vector3 fallback)
        {
            Vector3 cross = Vector3.Cross(a, b);
            float lenSq = cross.LengthSquared();
            return lenSq > 1e-9f ? (cross / MathF.Sqrt(lenSq)) : fallback;
        }

        /// <summary>
        /// Belirtilen (sx, sy, sz) hucresinin kati zemin VEYA dolu sivi sutunu ustunde oturan serbest yuzeyli
        /// bir sivi hucresi olup olmadigini denetler.
        /// Paket 4B.2 Kiyi Duzluk Tamiri: sy = y - 1 derin deniz/havuz sutununun altinda (sy - 1) kati kum yerine
        /// dolu deniz suyu bulundugunda da bunu gecerli havuz komsusu kabul eder ("ucurum" sanip kiyiyi cokertmez!).
        /// </summary>
        private static bool TrySampleSlopeOrPoolFluid(
            FluidNeighborhood3x3Cache nb,
            int sx,
            int sy,
            int sz,
            byte fluidType,
            int maxBufferY,
            out float outBedY,
            out float outCurrThick,
            out float outPrevThick,
            out float outCurrSurfY,
            out float outPrevSurfY,
            out float outVx,
            out float outVz,
            out float outColDepth,
            out bool outIsDeepPool)
        {
            outBedY = sy;
            outCurrThick = 0f;
            outPrevThick = 0f;
            outCurrSurfY = sy;
            outPrevSurfY = sy;
            outVx = 0f;
            outVz = 0f;
            outColDepth = 0f;
            outIsDeepPool = false;

            if (sy < 0 || sy >= maxBufferY)
            {
                return false;
            }

            nb.SampleRichCellFast(sx, sy, sz, out uint sRaw, out ushort fRaw, out byte pMass, out float vx, out float vz);
            byte cellMass = (byte)(fRaw & 0xFFu);
            if (cellMass == 0 || ((fRaw >> 8) & 0x03u) != fluidType || VoxelBitUtility.IsPhysicallySolid(sRaw))
            {
                return false;
            }

            if (sy + 1 < maxBufferY)
            {
                nb.SampleCellFast(sx, sy + 1, sz, out _, out ushort aboveFRaw);
                if ((aboveFRaw & 0xFFu) > 0u && ((aboveFRaw >> 8) & 0x03u) == fluidType)
                {
                    return false;
                }
            }

            uint belowSRaw = 0u;
            ushort belowFRaw = 0;
            bool supportedByPool = false;
            if (sy > 0)
            {
                nb.SampleCellFast(sx, sy - 1, sz, out belowSRaw, out belowFRaw);
                bool belowSolid = VoxelBitUtility.IsPhysicallySolid(belowSRaw);
                byte belowFMass = (byte)(belowFRaw & 0xFFu);
                supportedByPool = !belowSolid && belowFMass >= 200 && ((belowFRaw >> 8) & 0x03u) == fluidType;
                if (!belowSolid && !supportedByPool)
                {
                    return false;
                }
            }

            float bedY = (sy > 0 && !supportedByPool)
                ? ComputeHydrostaticSurfaceNetsBedMeters(belowSRaw, sRaw, sy)
                : sy;
            byte cap = ComputeSdfPorousCapacity(sRaw);

            bool isHydrostaticPool = supportedByPool || cellMass >= 220;
            float currThick = isHydrostaticPool
                ? ComputeExactHydrostaticThickness(cellMass, cap)
                : MassToFluidThicknessWithCapacity(cellMass, cap);
            float prevThick = isHydrostaticPool
                ? ComputeExactHydrostaticThickness(pMass, cap)
                : MassToFluidThicknessWithCapacity(pMass, cap);

            outBedY = bedY;
            outCurrSurfY = ComputeContinuousHydrostaticSurfaceY(bedY, sy, cellMass, cap, isHydrostaticPool);
            outPrevSurfY = ComputeContinuousHydrostaticSurfaceY(bedY, sy, pMass, cap, isHydrostaticPool);
            outCurrThick = MathF.Max(MinVisibleFilmThickness, outCurrSurfY - bedY);
            outPrevThick = pMass > 0 ? MathF.Max(MinVisibleFilmThickness, outPrevSurfY - bedY) : 0f;
            outVx = vx;
            outVz = vz;
            outColDepth = ComputeTrueFluidColumnDepthMeters(nb, sx, sy, sz, fluidType, outCurrThick);
            outIsDeepPool = supportedByPool || outColDepth >= 0.45f;
            return true;
        }

        private static bool HasSlopeOrPoolConnectedFluidAt(
            FluidNeighborhood3x3Cache nb,
            int nx,
            int y,
            int nz,
            byte fluidType,
            bool selfHasFreeTopSurface,
            int maxBufferY,
            out bool touchesWall)
        {
            nb.SampleCellFast(nx, y, nz, out uint nSolid, out ushort nFluid);
            byte mat = VoxelBitUtility.GetMaterialId(nSolid);
            byte shape = VoxelBitUtility.GetShapeId(nSolid);
            touchesWall = mat != 0 && (shape != 0 || VoxelBitUtility.GetDensityOrLiquid(nSolid) >= 32);

            if ((nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType)
            {
                if (selfHasFreeTopSurface && y >= 2 && IsMidAirFreeFallStreamCell(nb, nx, y, nz, fluidType))
                {
                    return false;
                }
                return true;
            }

            if (!selfHasFreeTopSurface)
            {
                return false;
            }

            bool isSolid = VoxelBitUtility.IsPhysicallySolid(nSolid);
            if (!isSolid && y > 0)
            {
                if (TrySampleSlopeOrPoolFluid(
                        nb, nx, y - 1, nz, fluidType, maxBufferY,
                        out _, out _, out _, out _, out _, out _, out _, out _, out _))
                {
                    touchesWall = false;
                    return true;
                }

                // Dik (45-65 derece) dogal dag yamaci (y - 2 baglantisi)
                nb.SampleCellFast(nx, y - 1, nz, out uint midDownSolid, out ushort midDownFluid);
                if (!VoxelBitUtility.IsPhysicallySolid(midDownSolid) && (midDownFluid & 0xFFu) == 0u && y >= 2 &&
                    TrySampleSlopeOrPoolFluid(
                        nb, nx, y - 2, nz, fluidType, maxBufferY,
                        out _, out _, out _, out _, out _, out _, out _, out _, out _))
                {
                    touchesWall = false;
                    return true;
                }
            }

            if (isSolid && y + 1 < maxBufferY)
            {
                if (TrySampleSlopeOrPoolFluid(
                        nb, nx, y + 1, nz, fluidType, maxBufferY,
                        out _, out _, out _, out _, out _, out _, out _, out _, out _))
                {
                    touchesWall = false;
                    return true;
                }

                if (VoxelBitUtility.GetShapeId(nSolid) == 0 && y + 2 < maxBufferY)
                {
                    nb.SampleCellFast(nx, y + 1, nz, out uint midUpSolid, out _);
                    if (VoxelBitUtility.IsPhysicallySolid(midUpSolid) && VoxelBitUtility.GetShapeId(midUpSolid) == 0 &&
                        TrySampleSlopeOrPoolFluid(
                            nb, nx, y + 2, nz, fluidType, maxBufferY,
                            out _, out _, out _, out _, out _, out _, out _, out _, out _))
                    {
                        touchesWall = false;
                        return true;
                    }
                }
            }

            return false;
        }

        private static RichCornerResult EvaluateRichCornerFast(
            FluidNeighborhood3x3Cache nb,
            int baseCornerX,
            int baseCornerZ,
            int y,
            byte fluidType,
            bool selfHasFreeTopSurface,
            bool embedFoundationIntoGround,
            int maxBufferY)
        {
            float wetCurrThickSum = 0f;
            float wetPrevThickSum = 0f;
            float wetCurrSurfSum = 0f;
            float wetPrevSurfSum = 0f;
            float minWetSurfY = float.MaxValue;
            float maxWetSurfY = float.MinValue;

            int wetSamples = 0;
            int submergedStackCount = 0;
            int slopeConnectedCount = 0;
            int poolSampleCount = 0;
            int shoreAirSamples = 0;
            int overfallSamples = 0;
            bool touchesSdfShore = false;
            bool hasSharpArchFloor = false;

            float wetBedSum = 0f;
            float minWetBedY = float.MaxValue;
            float maxWetBedY = float.MinValue;
            int wetBedSamples = 0;

            float cornerTerrainSum = 0f;
            int cornerTerrainCount = 0;

            float velXSum = 0f;
            float velZSum = 0f;
            float depthSum = 0f;
            float maxSampleDepth = 0f;

            float sdfNegX = 0f;
            float sdfPosX = 0f;
            float sdfNegZ = 0f;
            float sdfPosZ = 0f;

            for (int dz = -1; dz <= 0; dz++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int sx = baseCornerX + dx;
                    int sz = baseCornerZ + dz;
                    nb.SampleRichCellFast(sx, y, sz, out uint solidRaw, out ushort fluidRaw, out byte prevMass, out float vx, out float vz);

                    uint belowSolidRaw = 0u;
                    ushort belowFluidRaw = 0;
                    if (y > 0)
                    {
                        nb.SampleCellFast(sx, y - 1, sz, out belowSolidRaw, out belowFluidRaw);
                    }

                    uint aboveSolidRaw = 0u;
                    ushort aboveFluidRaw = 0;
                    byte abovePrevMass = 0;
                    float aboveVx = 0f;
                    float aboveVz = 0f;
                    if (y + 1 < maxBufferY)
                    {
                        nb.SampleRichCellFast(sx, y + 1, sz, out aboveSolidRaw, out aboveFluidRaw, out abovePrevMass, out aboveVx, out aboveVz);
                    }

                    bool isCurrentCellSolidWall = VoxelBitUtility.IsPhysicallySolid(solidRaw);
                    byte belowFMass = (byte)(belowFluidRaw & 0xFFu);
                    bool hasBelowSameFluid = y > 0 && belowFMass > 0 && ((belowFluidRaw >> 8) & 0x03u) == fluidType;
                    byte curFMass = (byte)(fluidRaw & 0xFFu);
                    bool isSampleMidAirDrop = selfHasFreeTopSurface && embedFoundationIntoGround && y >= 2 &&
                                              !isCurrentCellSolidWall && curFMass > 0 && ((fluidRaw >> 8) & 0x03u) == fluidType &&
                                              IsMidAirFreeFallStreamCell(nb, sx, y, sz, fluidType);
                    bool hasCurrentSameFluid = !isCurrentCellSolidWall && !isSampleMidAirDrop && curFMass > 0 && ((fluidRaw >> 8) & 0x03u) == fluidType;
                    byte aboveFMass = (byte)(aboveFluidRaw & 0xFFu);
                    bool hasAboveSameFluid = y + 1 < maxBufferY && !VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw) &&
                                             aboveFMass > 0 && ((aboveFluidRaw >> 8) & 0x03u) == fluidType;

                    if (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && VoxelBitUtility.GetShapeId(belowSolidRaw) != 0)
                    {
                        hasSharpArchFloor = true;
                    }

                    float cellBedY = y;
                    if (!hasBelowSameFluid && y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && !isCurrentCellSolidWall)
                    {
                        cellBedY = ComputeHydrostaticSurfaceNetsBedMeters(belowSolidRaw, solidRaw, y);
                        cornerTerrainSum += cellBedY;
                        cornerTerrainCount++;
                    }
                    else if (isCurrentCellSolidWall && VoxelBitUtility.GetShapeId(solidRaw) == 0 && !VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw))
                    {
                        cornerTerrainSum += ComputeHydrostaticSurfaceNetsBedMeters(solidRaw, aboveSolidRaw, y + 1);
                        cornerTerrainCount++;
                    }

                    if (hasCurrentSameFluid && hasAboveSameFluid)
                    {
                        // AK-7 Korumasi: Eger alt hucre tam dolu degilse (curFMass < 180) ve ustte de az su varsa (aboveFMass < 120),
                        // bu derin gol sutunu DEGIL, yamactan inen CFL yagmur gecisidir! 0.88m ve 1.03m sisirmesi YAPMA!
                        if (curFMass < 180 && aboveFMass < 120)
                        {
                            byte combinedCurr = (byte)Math.Min(255, curFMass + aboveFMass);
                            byte combinedPrev = (byte)Math.Min(255, prevMass + abovePrevMass);
                            byte cap = ComputeSdfPorousCapacity(solidRaw);
                            float cThick = MassToFluidThicknessWithCapacity(combinedCurr, cap);
                            float pThick = MassToFluidThicknessWithCapacity(combinedPrev, cap);
                            float cSurf = Math.Min(y + 1.0f, cellBedY + cThick);
                            float pSurf = Math.Min(y + 1.0f, cellBedY + pThick);

                            wetBedSum += cellBedY;
                            if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                            if (cellBedY > maxWetBedY) maxWetBedY = cellBedY;
                            wetBedSamples++;

                            wetCurrThickSum += cThick;
                            wetPrevThickSum += pThick;
                            wetCurrSurfSum += cSurf;
                            wetPrevSurfSum += pSurf;
                            if (cSurf < minWetSurfY) minWetSurfY = cSurf;
                            if (cSurf > maxWetSurfY) maxWetSurfY = cSurf;
                            wetSamples++;
                            velXSum += 0.5f * (vx + aboveVx);
                            velZSum += 0.5f * (vz + aboveVz);

                            float trueD = ComputeTrueFluidColumnDepthMeters(nb, sx, y + 1, sz, fluidType, aboveFMass / 255.0f);
                            depthSum += trueD;
                            if (trueD > maxSampleDepth) maxSampleDepth = trueD;
                        }
                        else
                        {
                            // Gercek derin gol / deniz / selale sutunu
                            submergedStackCount++;
                            poolSampleCount++;
                            wetBedSum += cellBedY;
                            if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                            if (cellBedY > maxWetBedY) maxWetBedY = cellBedY;
                            wetBedSamples++;

                            float aboveCurrT = ComputeExactHydrostaticThickness(aboveFMass, 255);
                            float abovePrevT = ComputeExactHydrostaticThickness(abovePrevMass, 255);
                            float colCurrSurf = (y + 1) + aboveCurrT;
                            float colPrevSurf = (y + 1) + abovePrevT;
                            // Kendi katmanimiz gomulu degilse (komsu sutunda ust katmana tasan dalga/su varsa) kesintisiz bagla:
                            float clampedCurr = selfHasFreeTopSurface ? colCurrSurf : (y + 1.0f);
                            float clampedPrev = selfHasFreeTopSurface ? colPrevSurf : (y + 1.0f);

                            wetCurrThickSum += clampedCurr - y;
                            wetPrevThickSum += clampedPrev - y;
                            wetCurrSurfSum += clampedCurr;
                            wetPrevSurfSum += clampedPrev;
                            if (clampedCurr < minWetSurfY) minWetSurfY = clampedCurr;
                            if (clampedCurr > maxWetSurfY) maxWetSurfY = clampedCurr;
                            wetSamples++;
                            velXSum += vx;
                            velZSum += vz;

                            float trueD = ComputeTrueFluidColumnDepthMeters(nb, sx, y + 1, sz, fluidType, aboveCurrT);
                            depthSum += trueD;
                            if (trueD > maxSampleDepth) maxSampleDepth = trueD;
                        }
                    }
                    else if (hasCurrentSameFluid)
                    {
                        wetBedSum += cellBedY;
                        if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                        if (cellBedY > maxWetBedY) maxWetBedY = cellBedY;
                        wetBedSamples++;

                        byte cap = ComputeSdfPorousCapacity(solidRaw);
                        bool isSupportedByBelowPool = hasBelowSameFluid && belowFMass >= 200;
                        float trueD = ComputeTrueFluidColumnDepthMeters(nb, sx, y, sz, fluidType, curFMass / (float)Math.Max((byte)1, cap));
                        bool sampleIsPool = isSupportedByBelowPool || curFMass >= 180 || trueD >= 0.45f;
                        if (sampleIsPool) poolSampleCount++;

                        float currThick = sampleIsPool
                            ? ComputeExactHydrostaticThickness(curFMass, cap)
                            : MassToFluidThicknessWithCapacity(curFMass, cap);
                        float prevThick = sampleIsPool
                            ? ComputeExactHydrostaticThickness(prevMass, cap)
                            : MassToFluidThicknessWithCapacity(prevMass, cap);

                        float cSurf = ComputeContinuousHydrostaticSurfaceY(cellBedY, y, curFMass, cap, sampleIsPool);
                        float pSurf = curFMass > 0 && prevMass == 0
                            ? cellBedY
                            : ComputeContinuousHydrostaticSurfaceY(cellBedY, y, prevMass, cap, sampleIsPool);
                        currThick = MathF.Max(MinVisibleFilmThickness, cSurf - cellBedY);
                        prevThick = prevMass > 0 ? MathF.Max(MinVisibleFilmThickness, pSurf - cellBedY) : 0f;

                        wetCurrThickSum += currThick;
                        wetPrevThickSum += prevThick;
                        wetCurrSurfSum += cSurf;
                        wetPrevSurfSum += pSurf;
                        if (cSurf < minWetSurfY) minWetSurfY = cSurf;
                        if (cSurf > maxWetSurfY) maxWetSurfY = cSurf;
                        wetSamples++;
                        velXSum += vx;
                        velZSum += vz;

                        depthSum += trueD;
                        if (trueD > maxSampleDepth) maxSampleDepth = trueD;
                    }
                    else if (selfHasFreeTopSurface && isCurrentCellSolidWall && hasAboveSameFluid &&
                             TrySampleSlopeOrPoolFluid(
                                 nb, sx, y + 1, sz, fluidType, maxBufferY,
                                 out float upBedY, out float upCurrThick, out float upPrevThick,
                                 out float upCurrSurfY, out float upPrevSurfY, out float upVx, out float upVz,
                                 out float upDepth, out bool upIsPool))
                    {
                        slopeConnectedCount++;
                        if (upIsPool) poolSampleCount++;
                        wetBedSum += upBedY;
                        if (upBedY < minWetBedY) minWetBedY = upBedY;
                        if (upBedY > maxWetBedY) maxWetBedY = upBedY;
                        wetBedSamples++;

                        wetCurrThickSum += upCurrThick;
                        wetPrevThickSum += upPrevThick;
                        wetCurrSurfSum += upCurrSurfY;
                        wetPrevSurfSum += upPrevSurfY;
                        if (upCurrSurfY < minWetSurfY) minWetSurfY = upCurrSurfY;
                        if (upCurrSurfY > maxWetSurfY) maxWetSurfY = upCurrSurfY;
                        wetSamples++;
                        velXSum += MathF.Abs(upVx) > 1e-4f ? upVx : aboveVx;
                        velZSum += MathF.Abs(upVz) > 1e-4f ? upVz : aboveVz;
                        depthSum += upDepth;
                        if (upDepth > maxSampleDepth) maxSampleDepth = upDepth;
                    }
                    else if (selfHasFreeTopSurface && isCurrentCellSolidWall && VoxelBitUtility.GetShapeId(solidRaw) == 0 &&
                             VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw) && VoxelBitUtility.GetShapeId(aboveSolidRaw) == 0 &&
                             y + 2 < maxBufferY &&
                             TrySampleSlopeOrPoolFluid(
                                 nb, sx, y + 2, sz, fluidType, maxBufferY,
                                 out float up2BedY, out float up2CurrThick, out float up2PrevThick,
                                 out float up2CurrSurfY, out float up2PrevSurfY, out float up2Vx, out float up2Vz,
                                 out float up2Depth, out bool up2IsPool))
                    {
                        slopeConnectedCount++;
                        if (up2IsPool) poolSampleCount++;
                        wetBedSum += up2BedY;
                        if (up2BedY < minWetBedY) minWetBedY = up2BedY;
                        if (up2BedY > maxWetBedY) maxWetBedY = up2BedY;
                        wetBedSamples++;
                        wetCurrThickSum += up2CurrThick;
                        wetPrevThickSum += up2PrevThick;
                        wetCurrSurfSum += up2CurrSurfY;
                        wetPrevSurfSum += up2PrevSurfY;
                        if (up2CurrSurfY < minWetSurfY) minWetSurfY = up2CurrSurfY;
                        if (up2CurrSurfY > maxWetSurfY) maxWetSurfY = up2CurrSurfY;
                        wetSamples++;
                        velXSum += up2Vx;
                        velZSum += up2Vz;
                        depthSum += up2Depth;
                        if (up2Depth > maxSampleDepth) maxSampleDepth = up2Depth;
                    }
                    else if (selfHasFreeTopSurface && !isCurrentCellSolidWall && y > 0 &&
                             TrySampleSlopeOrPoolFluid(
                                 nb, sx, y - 1, sz, fluidType, maxBufferY,
                                 out float downBedY, out float downCurrThick, out float downPrevThick,
                                 out float downCurrSurfY, out float downPrevSurfY, out float downVx, out float downVz,
                                 out float downDepth, out bool downIsPool))
                    {
                        slopeConnectedCount++;
                        if (downIsPool) poolSampleCount++;
                        wetBedSum += downBedY;
                        if (downBedY < minWetBedY) minWetBedY = downBedY;
                        if (downBedY > maxWetBedY) maxWetBedY = downBedY;
                        wetBedSamples++;

                        wetCurrThickSum += downCurrThick;
                        wetPrevThickSum += downPrevThick;
                        wetCurrSurfSum += downCurrSurfY;
                        wetPrevSurfSum += downPrevSurfY;
                        if (downCurrSurfY < minWetSurfY) minWetSurfY = downCurrSurfY;
                        if (downCurrSurfY > maxWetSurfY) maxWetSurfY = downCurrSurfY;
                        wetSamples++;
                        velXSum += downVx;
                        velZSum += downVz;
                        depthSum += downDepth;
                        if (downDepth > maxSampleDepth) maxSampleDepth = downDepth;
                    }
                    else if (selfHasFreeTopSurface && !isCurrentCellSolidWall && !isSampleMidAirDrop &&
                             y >= 2 && !VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && belowFMass == 0 &&
                             TrySampleSlopeOrPoolFluid(
                                 nb, sx, y - 2, sz, fluidType, maxBufferY,
                                 out float down2BedY, out float down2CurrThick, out float down2PrevThick,
                                 out float down2CurrSurfY, out float down2PrevSurfY, out float down2Vx, out float down2Vz,
                                 out float down2Depth, out bool down2IsPool))
                    {
                        slopeConnectedCount++;
                        if (down2IsPool) poolSampleCount++;
                        wetBedSum += down2BedY;
                        if (down2BedY < minWetBedY) minWetBedY = down2BedY;
                        if (down2BedY > maxWetBedY) maxWetBedY = down2BedY;
                        wetBedSamples++;
                        wetCurrThickSum += down2CurrThick;
                        wetPrevThickSum += down2PrevThick;
                        wetCurrSurfSum += down2CurrSurfY;
                        wetPrevSurfSum += down2PrevSurfY;
                        if (down2CurrSurfY < minWetSurfY) minWetSurfY = down2CurrSurfY;
                        if (down2CurrSurfY > maxWetSurfY) maxWetSurfY = down2CurrSurfY;
                        wetSamples++;
                        velXSum += down2Vx;
                        velZSum += down2Vz;
                        depthSum += down2Depth;
                        if (down2Depth > maxSampleDepth) maxSampleDepth = down2Depth;
                    }
                    else if (!isCurrentCellSolidWall)
                    {
                        if (isSampleMidAirDrop || (y > 0 && !VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && belowFMass == 0))
                        {
                            overfallSamples++;
                        }
                        else if (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) &&
                                 VoxelBitUtility.GetShapeId(belowSolidRaw) == 0 &&
                                 VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw) >= 90)
                        {
                            // Gorsel 2 Cukur Kenar Boslugu Tamiri:
                            // Bu kuru hucrenin y-1 tabani yuksek yogunluklu SDF toprak/kaya ise VE disa bakan komsusu (y katmaninda)
                            // kati yokus duvariysa, SurfaceNets ucgeni bu hucrenin icinden yukari tirmaniyor demektir!
                            // Bunu acik hava (shoreAirSamples) sayip su kosesini cokertme; kiyi duvari kabul edip icine uzat!
                            int outStepX = dx == 0 ? 1 : -1;
                            int outStepZ = dz == 0 ? 1 : -1;
                            nb.SampleCellFast(sx + outStepX, y, sz, out uint outSolidX, out _);
                            nb.SampleCellFast(sx, y, sz + outStepZ, out uint outSolidZ, out _);
                            bool risesIntoSdfBank =
                                (VoxelBitUtility.IsPhysicallySolid(outSolidX) && VoxelBitUtility.GetShapeId(outSolidX) == 0) ||
                                (VoxelBitUtility.IsPhysicallySolid(outSolidZ) && VoxelBitUtility.GetShapeId(outSolidZ) == 0);
                            if (risesIntoSdfBank)
                            {
                                touchesSdfShore = true;
                                byte virtualWallDens = VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw);
                                if (dx == -1) sdfNegX += virtualWallDens; else sdfPosX += virtualWallDens;
                                if (dz == -1) sdfNegZ += virtualWallDens; else sdfPosZ += virtualWallDens;
                            }
                            else
                            {
                                shoreAirSamples++;
                            }
                        }
                        else
                        {
                            shoreAirSamples++;
                        }
                    }

                    byte sMat = VoxelBitUtility.GetMaterialId(solidRaw);
                    byte sShape = VoxelBitUtility.GetShapeId(solidRaw);
                    byte sDens = (sMat != 0 && sShape == 0) ? VoxelBitUtility.GetDensityOrLiquid(solidRaw) : (byte)0;
                    if (sDens >= 48)
                    {
                        touchesSdfShore = true;
                    }

                    if (dx == -1) sdfNegX += sDens;
                    else sdfPosX += sDens;

                    if (dz == -1) sdfNegZ += sDens;
                    else sdfPosZ += sDens;
                }
            }

            float avgWetBedY = wetBedSamples > 0 ? (wetBedSum / wetBedSamples) : y;
            float baseMinBedY = minWetBedY < float.MaxValue ? minWetBedY : avgWetBedY;
            float meanRawThick = wetSamples > 0 ? (wetCurrThickSum / wetSamples) : MinVisibleFilmThickness;
            float adaptiveEmbed = Math.Clamp(meanRawThick * 0.65f, 0.012f, FoundationEmbedDepthMeters);
            float outCornerBedY = embedFoundationIntoGround
                ? Math.Max(0f, baseMinBedY - adaptiveEmbed)
                : avgWetBedY;

            // Kurganov-Petrova Hidrostatik Havuz Tespiti (Gorsel 5 Kubbe Tamiri):
            // Gercek bir havuz/deniz kosesinde etraftaki kuru hucreler kati kiyidir (shoreAirSamples == 0)!
            // Eger shoreAirSamples > 0 ise suyun yaninda ACIK KURU HAVA vardir (orn. yamaca kazanla dokulen su kubbesi);
            // bu durumda kenar havada 1m yuksekte asili kalmamali, asagidaki meniskus dalinda dogrudan topraga kapanmalidir!
            float surfSpread = (wetSamples > 1 && maxWetSurfY >= minWetSurfY) ? (maxWetSurfY - minWetSurfY) : 0f;
            float bedSpread = (wetBedSamples > 1 && maxWetBedY >= minWetBedY) ? (maxWetBedY - minWetBedY) : 0f;
            bool isPoolRegime = wetSamples > 0 && shoreAirSamples == 0 &&
                ((poolSampleCount > 0 && surfSpread <= 0.18f) ||
                 (wetSamples >= 2 && surfSpread <= 0.06f && bedSpread <= 0.40f && overfallSamples == 0));

            // Paket 4B.2 Kiyi-Duvar Sizdirmaz Gomulme + Icbukey SurfaceNets Kose Muhru (67a8ad6a.jpg & 07180972.jpg Tamiri):
            // 3 su + 1 kati SurfaceNets kosesinde (wetSamples == 3, shoreAirSamples == 0) SurfaceNets dual duvari kosegen boyunca
            // 1.06m (0.75m X, 0.75m Z) geriye cekildiginden, 1.18m uzatma ile su kosesini arazi duvarinin 8cm icine gom!
            Vector3 skirtOffset = Vector3.Zero;
            if (touchesSdfShore && wetSamples < 4 && !hasSharpArchFloor)
            {
                bool isConcavePoolCorner = wetSamples == 3 && shoreAirSamples == 0 && (isPoolRegime || meanRawThick >= 0.07f);
                float maxShoreExt = isConcavePoolCorner ? 1.18f : (shoreAirSamples == 0 ? 0.80f : ShorelineSkirtExtensionMeters);
                float minWallEmbed = (isPoolRegime || meanRawThick >= 0.07f)
                    ? (isConcavePoolCorner ? 1.16f : (shoreAirSamples == 0 ? 0.78f : 0.62f))
                    : 0.16f;
                float adaptiveSkirtExt = Math.Clamp(MathF.Max(minWallEmbed, meanRawThick * 1.25f), 0.14f, maxShoreExt);
                Vector3 gradXZ = new Vector3(sdfPosX - sdfNegX, 0f, sdfPosZ - sdfNegZ);
                float lenSq = gradXZ.LengthSquared();
                if (lenSq > 1e-4f)
                {
                    skirtOffset = (gradXZ / MathF.Sqrt(lenSq)) * adaptiveSkirtExt;
                }
            }

            float avgVelX = wetSamples > 0 ? (velXSum / wetSamples) : 0f;
            float avgVelZ = wetSamples > 0 ? (velZSum / wetSamples) : 0f;
            float avgDepth = wetSamples > 0 ? (depthSum / wetSamples) : MinVisibleFilmThickness;

            if (!selfHasFreeTopSurface || submergedStackCount == 4)
            {
                return new RichCornerResult
                {
                    TopCurrY = y + 1.0f,
                    TopPrevY = y + 1.0f,
                    BedY = outCornerBedY,
                    SkirtOffset = skirtOffset,
                    VelX = avgVelX,
                    VelZ = avgVelZ,
                    DepthMeters = avgDepth,
                    TouchesSdfShore = touchesSdfShore && slopeConnectedCount == 0,
                    HasOverfall = false,
                    HasSlopeConnection = false,
                    IsPoolRegime = true,
                    HasSharpArchitecturalFloor = hasSharpArchFloor
                };
            }

            float avgCurrSurfY;
            float avgPrevSurfY;
            if (wetSamples > 0)
            {
                if (isPoolRegime && (overfallSamples == 0 || slopeConnectedCount > 0))
                {
                    // KURGANOV-PETROVA WELL-BALANCED LAKE-AT-REST KURALI (AK-2):
                    // Deniz ve gollerde dogrudan serbest yuzey kotu η = wetCurrSurfSum / wetSamples kullanilir;
                    // Shore Feathering (*0.28 / *0.46) KESINLIKLE UYGULANMAZ -> Kiyidan ufuga 0.000m ayna duzlugu!
                    avgCurrSurfY = wetCurrSurfSum / wetSamples;
                    avgPrevSurfY = wetPrevSurfSum / wetSamples;
                }
                else
                {
                    float avgCurrThick = wetCurrThickSum / wetSamples;
                    float avgPrevThick = wetPrevThickSum / wetSamples;
                    float avgBedPortion = (wetCurrSurfSum - wetCurrThickSum) / wetSamples;

                    if (overfallSamples > 0 && slopeConnectedCount == 0)
                    {
                        float brinkFactor = overfallSamples == 1
                            ? 0.76f
                            : (overfallSamples == 2 ? WeirBrinkDepthRatio : 0.66f);
                        avgCurrThick *= brinkFactor;
                        avgPrevThick *= brinkFactor;
                        avgCurrSurfY = avgBedPortion + avgCurrThick;
                        avgPrevSurfY = avgBedPortion + avgPrevThick;
                    }
                    else if (shoreAirSamples > 0)
                    {
                        // Gorsel 5 Kubbe & Yamac Kenar Kapanisi:
                        // Acik kuru havaya (shoreAirSamples > 0) bakan su kenarini dogrudan dual-grid SurfaceNets arazi kotuna indir!
                        float targetGroundY = (!hasSharpArchFloor && cornerTerrainCount > 0)
                            ? (cornerTerrainSum / cornerTerrainCount)
                            : avgBedPortion;

                        if (!hasSharpArchFloor && avgCurrThick > 0.08f)
                        {
                            // Kazanla dokulen kalin su kubbesinin kenari SurfaceNets cimeninin 2.5cm icine gomulur -> 0 alt bosluk!
                            avgCurrSurfY = targetGroundY - 0.025f;
                            avgPrevSurfY = avgPrevThick > 0f ? (targetGroundY - 0.025f) : targetGroundY;
                            outCornerBedY = Math.Min(outCornerBedY, targetGroundY - 0.05f);
                        }
                        else
                        {
                            float feather = wetSamples == 1 ? 0.25f : (wetSamples == 2 ? 0.45f : 0.72f);
                            float thinCurr = MathF.Min(0.065f, MathF.Max(0.004f, avgCurrThick * feather));
                            float thinPrev = avgPrevThick > 0f ? MathF.Min(0.065f, MathF.Max(0.004f, avgPrevThick * feather)) : 0f;
                            avgCurrSurfY = targetGroundY + thinCurr;
                            avgPrevSurfY = targetGroundY + thinPrev;
                            outCornerBedY = Math.Min(outCornerBedY, targetGroundY - 0.015f);
                        }
                    }
                    else
                    {
                        avgCurrSurfY = avgBedPortion + avgCurrThick;
                        avgPrevSurfY = avgBedPortion + avgPrevThick;
                    }
                }
            }
            else
            {
                avgCurrSurfY = avgWetBedY + MinVisibleFilmThickness;
                avgPrevSurfY = avgCurrSurfY;
            }

            if (y + 1 < maxBufferY)
            {
                nb.SampleCellFast(baseCornerX == 16 ? 15 : baseCornerX, y + 1, baseCornerZ == 16 ? 15 : baseCornerZ, out uint selfAboveSolid, out _);
                if (VoxelBitUtility.IsPhysicallySolid(selfAboveSolid) && slopeConnectedCount == 0)
                {
                    avgCurrSurfY = Math.Min(y + 0.98f, avgCurrSurfY);
                    avgPrevSurfY = Math.Min(y + 0.98f, avgPrevSurfY);
                }
            }

            float maxAllowedCornerY = (slopeConnectedCount > 0 || isPoolRegime) ? (y + 2.0f) : (y + 1.0f);
            avgCurrSurfY = Math.Min(maxAllowedCornerY, avgCurrSurfY);
            avgPrevSurfY = Math.Min(maxAllowedCornerY, avgPrevSurfY);

            return new RichCornerResult
            {
                TopCurrY = avgCurrSurfY,
                TopPrevY = avgPrevSurfY,
                BedY = outCornerBedY,
                SkirtOffset = skirtOffset,
                VelX = avgVelX,
                VelZ = avgVelZ,
                DepthMeters = avgDepth,
                TouchesSdfShore = touchesSdfShore && slopeConnectedCount == 0,
                HasOverfall = overfallSamples > 0 && slopeConnectedCount == 0,
                HasSlopeConnection = slopeConnectedCount > 0,
                IsPoolRegime = isPoolRegime,
                HasSharpArchitecturalFloor = hasSharpArchFloor
            };
        }

        private static void EmitRichBevelSkirtQuad(
            in RichContourVertex v0,
            in RichContourVertex v1,
            byte renderId,
            List<FluidRichTriangle> outRichTriangles)
        {
            if (v0.TopCurr.Y <= v0.Bottom.Y + 0.004f && v1.TopCurr.Y <= v1.Bottom.Y + 0.004f)
            {
                return;
            }

            Vector3 norm = SafeCrossNormalize(v0.TopCurr - v0.Bottom, v1.TopCurr - v0.Bottom, Vector3.UnitX);
            // Ust koseye ust yuzey normali verilerek GPU deplasman kilidi (upwardSurfaceMask == 1.0) korunur!
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, norm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.0f),
                C1 = MakeSample(v0.TopCurr, v0.SmoothTopNormal, renderId, v0.EncodedSubTickDy, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.0f),
                C2 = MakeSample(v1.TopCurr, v1.SmoothTopNormal, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.0f)
            });
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, norm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.0f),
                C1 = MakeSample(v1.TopCurr, v1.SmoothTopNormal, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.0f),
                C2 = MakeSample(v1.Bottom, norm, renderId, 128, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.0f)
            });
        }

        private static void EmitRichSideOrWaterfallCurtain(
            FluidNeighborhood3x3Cache nb,
            int lx,
            int y,
            int lz,
            int dirX,
            int dirZ,
            byte fluidType,
            byte renderId,
            byte selfMass,
            byte pressureHead,
            float selfVelX,
            float selfVelZ,
            float selfSurfaceY,
            bool selfHasFreeTopSurface,
            bool allowSharpSkirtWall,
            int maxBufferY,
            in RichContourVertex v0,
            in RichContourVertex v1,
            List<FluidRichTriangle> outRichTriangles)
        {
            int nx = lx + dirX;
            int nz = lz + dirZ;
            nb.SampleCellFast(nx, y, nz, out uint nSolid, out ushort nFluid);
            bool neighborHasSameFluid = (nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType;
            bool neighborIsMidAirDrop = neighborHasSameFluid && y >= 2 && IsMidAirFreeFallStreamCell(nb, nx, y, nz, fluidType);

            if (neighborHasSameFluid && !neighborIsMidAirDrop)
            {
                return;
            }

            bool neighborIsSolid = VoxelBitUtility.IsPhysicallySolid(nSolid);

            // PAKET 4B.2 KURAL 1 (AK-8): Sualtinda veya kiyida kati bloga (kum/kaya/duvar) bakan HICBIR ic dikey su duvari ORULMEZ!
            // Bu tek kural Gorsel 5'teki sualti beyaz hava tuneli/duvar hatasini %100 ortadan kaldirir!
            if (neighborIsSolid)
            {
                return;
            }

            // Yamac / Havuz birlestirme: Komsu sutunda y-1 veya y-2 sivi baglantisi varsa ust yuzeyler zaten 0.000m dikisli!
            if (selfHasFreeTopSurface && !neighborIsMidAirDrop && y > 0)
            {
                if (TrySampleSlopeOrPoolFluid(
                        nb, nx, y - 1, nz, fluidType, maxBufferY,
                        out _, out _, out _, out _, out _, out _, out _, out _, out _))
                {
                    return;
                }

                nb.SampleCellFast(nx, y - 1, nz, out uint midDS, out ushort midDF);
                if (!VoxelBitUtility.IsPhysicallySolid(midDS) && (midDF & 0xFFu) == 0u && y >= 2 &&
                    TrySampleSlopeOrPoolFluid(
                        nb, nx, y - 2, nz, fluidType, maxBufferY,
                        out _, out _, out _, out _, out _, out _, out _, out _, out _))
                {
                    return;
                }
            }

            // Savak agzindan hemen onundeki silindirik hava dusus sutununa (neighborIsMidAirDrop) yumusak baglanti basligi!
            if (neighborIsMidAirDrop && selfHasFreeTopSurface)
            {
                byte dropMass = (byte)(nFluid & 0xFFu);
                float rDrop = ComputeFallingStreamRadius(dropMass);
                Vector3 streamCenter = new Vector3(nx + 0.5f, y + 0.72f, nz + 0.5f);
                Vector3 top0 = v0.TopCurr;
                Vector3 top1 = v1.TopCurr;
                Vector3 sideSpan = Vector3.Normalize(new Vector3(top1.X - top0.X, 0f, top1.Z - top0.Z));
                Vector3 bot0 = streamCenter - sideSpan * rDrop;
                Vector3 bot1 = streamCenter + sideSpan * rDrop;
                Vector3 outDir = new Vector3(dirX, 0f, dirZ);
                Vector3 topNorm = Vector3.Normalize(outDir * 0.45f + Vector3.UnitY * 0.893f);
                Vector3 hoodNorm = Vector3.Normalize(outDir * 0.55f + Vector3.UnitY * 0.835f);
                byte flowVx = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(dirX * 0.65f, -1.0f, 1.0f));
                byte flowVz = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(dirZ * 0.65f, -1.0f, 1.0f));

                outRichTriangles.Add(new FluidRichTriangle
                {
                    C0 = MakeSample(bot0, hoodNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, 0.22f),
                    C1 = MakeSample(top0, topNorm, renderId, v0.EncodedSubTickDy, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.16f),
                    C2 = MakeSample(top1, topNorm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.16f)
                });
                outRichTriangles.Add(new FluidRichTriangle
                {
                    C0 = MakeSample(bot0, hoodNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, 0.22f),
                    C1 = MakeSample(top1, topNorm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.16f),
                    C2 = MakeSample(bot1, hoodNorm, renderId, 128, flowVx, flowVz, v1.DepthMeters, 0.22f)
                });
                return;
            }

            bool neighborIsOpenAir = !neighborIsSolid && ((nFluid & 0xFFu) == 0u || ((nFluid >> 8) & 0x03u) != fluidType);
            uint neighborBelowSolid = 0u;
            ushort neighborBelowFluid = 0;
            if (y > 0)
            {
                nb.SampleCellFast(nx, y - 1, nz, out neighborBelowSolid, out neighborBelowFluid);
            }

            // PAKET 4B.2 KURAL 2 (AK-9 & AK-10): Akis-Yonlu Gercek Ucurum Selalesi Kriteri!
            bool isOverfallDrop = neighborIsOpenAir && y > 0 &&
                                  !VoxelBitUtility.IsPhysicallySolid(neighborBelowSolid) &&
                                  ((neighborBelowFluid & 0xFFu) == 0u || ((neighborBelowFluid >> 8) & 0x03u) != fluidType);

            if (isOverfallDrop && selfHasFreeTopSurface)
            {
                float meanEdgeDepth = 0.5f * (v0.DepthMeters + v1.DepthMeters);
                float edgeFlowDot = selfVelX * dirX + selfVelZ * dirZ;

                // Komsu sutunda dusen siviya veya tabana kadar gercek dusus yuksekligini olc
                float targetBotY = y - 1.0f;
                bool hitLowerFluid = false;
                int minScanY = Math.Max(0, y - 16);
                for (int sy = y - 1; sy >= minScanY; sy--)
                {
                    nb.SampleCellFast(nx, sy, nz, out uint scanSolid, out ushort scanFluid);
                    if ((scanFluid & 0xFFu) > 0u && ((scanFluid >> 8) & 0x03u) == fluidType)
                    {
                        byte scanMass = (byte)(scanFluid & 0xFFu);
                        targetBotY = sy + MassToFluidThickness(scanMass) * 0.95f;
                        hitLowerFluid = true;
                        break;
                    }
                    if (VoxelBitUtility.IsPhysicallySolid(scanSolid))
                    {
                        targetBotY = ComputeHydrostaticSurfaceNetsBedMeters(scanSolid, 0u, sy + 1);
                        break;
                    }
                    targetBotY = sy;
                }

                float verticalDrop = selfSurfaceY - targetBotY;
                float orthoSpeed = MathF.Abs(selfVelX * dirZ - selfVelZ * dirX);
                bool flowsAlongSideSlope = orthoSpeed > 0.18f && edgeFlowDot <= 0.05f;
                float minRequiredDrop = allowSharpSkirtWall ? 1.25f : 1.95f;

                // Kose ucurumlarda (706fc114.jpg) ayni hucreden 90 derece dik iki ayri duz beyaz kagit serit firlatma!
                bool isPrimaryDropSide = IsPrimaryOverfallDirection(nb, lx, y, lz, dirX, dirZ, fluidType, verticalDrop, selfVelX, selfVelZ);

                if (verticalDrop >= minRequiredDrop && meanEdgeDepth >= 0.055f && (selfMass >= 28 || edgeFlowDot > 0.10f) &&
                    !flowsAlongSideSlope && isPrimaryDropSide)
                {
                    float foamScale = Math.Clamp((meanEdgeDepth - 0.10f) / 0.28f, 0.25f, 1.0f);
                    float botPinch = Math.Clamp(0.32f - meanEdgeDepth * 0.30f, 0.12f, 0.30f);

                    Vector3 outDir = new Vector3(dirX, 0f, dirZ);
                    float jetOut = Math.Clamp(0.06f * MathF.Sqrt(Math.Max(1, (int)pressureHead)), 0.08f, 0.18f) *
                                   Math.Clamp(meanEdgeDepth * 2.5f, 0.35f, 1.0f);

                    Vector3 top0 = v0.TopCurr;
                    Vector3 top1 = v1.TopCurr;
                    Vector3 edgeSpan = top1 - top0;
                    Vector3 pinched0 = top0 + edgeSpan * botPinch;
                    Vector3 pinched1 = top1 - edgeSpan * botPinch;
                    Vector3 bot0 = new Vector3(pinched0.X + outDir.X * jetOut, MathF.Min(top0.Y - 0.02f, targetBotY), pinched0.Z + outDir.Z * jetOut);
                    Vector3 bot1 = new Vector3(pinched1.X + outDir.X * jetOut, MathF.Min(top1.Y - 0.02f, targetBotY), pinched1.Z + outDir.Z * jetOut);

                    Vector3 midBase0 = Vector3.Lerp(top0, pinched0, 0.5f);
                    Vector3 midBase1 = Vector3.Lerp(top1, pinched1, 0.5f);
                    Vector3 mid0 = new Vector3(
                        midBase0.X + outDir.X * (jetOut * 0.7071f),
                        0.5f * (top0.Y + bot0.Y),
                        midBase0.Z + outDir.Z * (jetOut * 0.7071f));
                    Vector3 mid1 = new Vector3(
                        midBase1.X + outDir.X * (jetOut * 0.7071f),
                        0.5f * (top1.Y + bot1.Y),
                        midBase1.Z + outDir.Z * (jetOut * 0.7071f));

                    // topNorm.Y >= 0.893f -> ust savakla 0.000m GPU dikisi; mid/bot yukari egimli normal ile yan Fresnel beyaz cubugunu onler!
                    Vector3 topNorm = Vector3.Normalize(outDir * 0.45f + Vector3.UnitY * 0.893f);
                    Vector3 midNorm = Vector3.Normalize(outDir * 0.62f + Vector3.UnitY * 0.785f);
                    Vector3 botNorm = Vector3.Normalize(outDir * 0.70f + Vector3.UnitY * 0.714f);

                    byte flowVx = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(dirX * 0.75f, -1.0f, 1.0f));
                    byte flowVz = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(dirZ * 0.75f, -1.0f, 1.0f));
                    float topFoam = MathF.Max(0.32f, 0.34f * foamScale);
                    float midFoam = MathF.Max(0.36f, 0.44f * foamScale);
                    float botFoam = MathF.Max(0.42f, (hitLowerFluid ? 0.56f : 0.48f) * foamScale);

                    outRichTriangles.Add(new FluidRichTriangle
                    {
                        C0 = MakeSample(mid0, midNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, midFoam),
                        C1 = MakeSample(top0, topNorm, renderId, v0.EncodedSubTickDy, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, topFoam),
                        C2 = MakeSample(top1, topNorm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, topFoam)
                    });
                    outRichTriangles.Add(new FluidRichTriangle
                    {
                        C0 = MakeSample(mid0, midNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, midFoam),
                        C1 = MakeSample(top1, topNorm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, topFoam),
                        C2 = MakeSample(mid1, midNorm, renderId, 128, flowVx, flowVz, v1.DepthMeters, midFoam)
                    });

                    outRichTriangles.Add(new FluidRichTriangle
                    {
                        C0 = MakeSample(bot0, botNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, botFoam),
                        C1 = MakeSample(mid0, midNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, midFoam),
                        C2 = MakeSample(mid1, midNorm, renderId, 128, flowVx, flowVz, v1.DepthMeters, midFoam)
                    });
                    outRichTriangles.Add(new FluidRichTriangle
                    {
                        C0 = MakeSample(bot0, botNorm, renderId, 128, flowVx, flowVz, v0.DepthMeters, botFoam),
                        C1 = MakeSample(mid1, midNorm, renderId, 128, flowVx, flowVz, v1.DepthMeters, midFoam),
                        C2 = MakeSample(bot1, botNorm, renderId, 128, flowVx, flowVz, v1.DepthMeters, botFoam)
                    });
                    return;
                }
            }

            // PAKET 4B.2 KURAL 3 (AK-9 & Gorsel 8 Kazan Dokumu Tamiri):
            // Dogal SurfaceNets arazisi (!allowSharpSkirtWall) uzerinde su 2 katman ust uste binse dahi (!selfHasFreeTopSurface)
            // havaya dikilen 1m x 1m beyaz kopuklu kutu yan duvar (wallFoam = 0.75) ASLA ORME!
            // Keskin yan duvar yalnizca mimari kup/kanal ustunde (allowSharpSkirtWall) uretilir.
            if (!allowSharpSkirtWall)
            {
                return;
            }

            if (v0.TopCurr.Y <= v0.Bottom.Y + 0.005f && v1.TopCurr.Y <= v1.Bottom.Y + 0.005f)
            {
                return;
            }

            float wallFoam = (!selfHasFreeTopSurface && neighborIsOpenAir) ? 0.75f : 0.0f;
            Vector3 wallNorm = SafeCrossNormalize(v0.TopCurr - v0.Bottom, v1.TopCurr - v0.Bottom, new Vector3(dirX, 0f, dirZ));
            Vector3 topWallNorm0 = selfHasFreeTopSurface ? v0.SmoothTopNormal : wallNorm;
            Vector3 topWallNorm1 = selfHasFreeTopSurface ? v1.SmoothTopNormal : wallNorm;

            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, wallNorm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, wallFoam),
                C1 = MakeSample(v0.TopCurr, topWallNorm0, renderId, v0.EncodedSubTickDy, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, wallFoam),
                C2 = MakeSample(v1.TopCurr, topWallNorm1, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, wallFoam)
            });
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, wallNorm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, wallFoam),
                C1 = MakeSample(v1.TopCurr, topWallNorm1, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, wallFoam),
                C2 = MakeSample(v1.Bottom, wallNorm, renderId, 128, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, wallFoam)
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeFallingStreamRadius(byte mass)
        {
            return Math.Clamp(0.095f + 0.185f * MathF.Sqrt(mass / 255.0f), 0.10f, 0.28f);
        }

        /// <summary>
        /// Bir sivi hucresinin kati zemin/havuz sutununa oturmak yerine gercekten havada serbest dusen bir selale/dokulme
        /// damlasi veya sutunu olup olmadigini denetler (altinda y=0'a kadar kati zemin/havuz bulunan gercek dunya sutunlarinda aktif).
        /// </summary>
        private static bool IsMidAirFreeFallStreamCell(FluidNeighborhood3x3Cache nb, int lx, int y, int lz, byte fluidType)
        {
            if (y <= 1) return false;

            nb.SampleCellFast(lx, y - 1, lz, out uint belowSolid, out ushort belowFluid);
            if (VoxelBitUtility.IsPhysicallySolid(belowSolid))
            {
                return false;
            }

            byte belowMass = (byte)(belowFluid & 0xFFu);
            bool belowSame = belowMass > 0 && ((belowFluid >> 8) & 0x03u) == fluidType;

            // T08 Korumasi: Eger y-1'de ince yagmur suyu varsa VE y-2 dogrudan kati zemin ise (1-blokluk yamac CFL gecisi!),
            // bu yuksek selale dususu degil zemin ustu yamac akisidir!
            if (belowSame)
            {
                nb.SampleCellFast(lx, y - 2, lz, out uint twoBelowSolid, out _);
                if (VoxelBitUtility.IsPhysicallySolid(twoBelowSolid))
                {
                    return false;
                }
            }

            // Altindaki sutun 200+ dolu hidrostatik havuz sutunu olarak kesintisiz kati tabana inerse serbest dusus degildir (deniz/gol/T23!)
            bool continuousHydrostaticSupport = belowSame && belowMass >= 200;
            bool foundAnyFloorOrPoolBelow = false;
            bool foundFreeFallGap = !belowSame || belowMass < 200;

            int minCheckY = Math.Max(0, y - 24);
            for (int sy = y - 1; sy >= minCheckY; sy--)
            {
                nb.SampleCellFast(lx, sy, lz, out uint sRaw, out ushort fRaw);
                if (VoxelBitUtility.IsPhysicallySolid(sRaw))
                {
                    foundAnyFloorOrPoolBelow = true;
                    break;
                }

                byte m = (byte)(fRaw & 0xFFu);
                bool same = m > 0 && ((fRaw >> 8) & 0x03u) == fluidType;
                if (same)
                {
                    foundAnyFloorOrPoolBelow = true;
                    if (m < 200)
                    {
                        continuousHydrostaticSupport = false;
                        foundFreeFallGap = true;
                    }
                }
                else
                {
                    continuousHydrostaticSupport = false;
                    foundFreeFallGap = true;
                }
            }

            if (!foundAnyFloorOrPoolBelow || continuousHydrostaticSupport)
            {
                return false;
            }

            return foundFreeFallGap;
        }

        /// <summary>
        /// Kose kaya cikintilarinda (706fc114.jpg) tek hucreden iki dik yone (orn. +X ve -Z) 90 derece acili iki ayri
        /// duz beyaz serit firlatilmasini engeller; dusuk yatay hizda yalnizca en derin/baskin tek ucurum kenarini secer.
        /// </summary>
        private static bool IsPrimaryOverfallDirection(
            FluidNeighborhood3x3Cache nb,
            int lx,
            int y,
            int lz,
            int dirX,
            int dirZ,
            byte fluidType,
            float myDrop,
            float selfVelX,
            float selfVelZ)
        {
            float speedSq = selfVelX * selfVelX + selfVelZ * selfVelZ;
            if (speedSq >= 0.04f)
            {
                float myDot = selfVelX * dirX + selfVelZ * dirZ;
                return myDot >= 0.02f;
            }

            ReadOnlySpan<int> dxs = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dzs = stackalloc int[4] { 0, 0, 1, -1 };
            int myRank = dirX > 0 ? 4 : (dirX < 0 ? 3 : (dirZ > 0 ? 2 : 1));

            for (int k = 0; k < 4; k++)
            {
                int ox = dxs[k];
                int oz = dzs[k];
                if (ox == dirX && oz == dirZ) continue;
                // Ayni yondeki paralel savak degil; yalnizca baska yone bakan alternatif kenarlari kiyasla
                int nx = lx + ox;
                int nz = lz + oz;
                nb.SampleCellFast(nx, y, nz, out uint ns, out ushort nf);
                if (VoxelBitUtility.IsPhysicallySolid(ns) || ((nf & 0xFFu) > 0u && ((nf >> 8) & 0x03u) == fluidType)) continue;
                nb.SampleCellFast(nx, y - 1, nz, out uint nbs, out ushort nbf);
                if (VoxelBitUtility.IsPhysicallySolid(nbs) || ((nbf & 0xFFu) > 0u && ((nbf >> 8) & 0x03u) == fluidType)) continue;

                float otherBotY = y - 1.0f;
                int minScanY = Math.Max(0, y - 16);
                for (int sy = y - 1; sy >= minScanY; sy--)
                {
                    nb.SampleCellFast(nx, sy, nz, out uint ss, out ushort sf);
                    if ((sf & 0xFFu) > 0u && ((sf >> 8) & 0x03u) == fluidType)
                    {
                        otherBotY = sy + MassToFluidThickness((byte)(sf & 0xFFu)) * 0.95f;
                        break;
                    }
                    if (VoxelBitUtility.IsPhysicallySolid(ss))
                    {
                        otherBotY = ComputeHydrostaticSurfaceNetsBedMeters(ss, 0u, sy + 1);
                        break;
                    }
                    otherBotY = sy;
                }

                float otherDrop = (y + 0.5f) - otherBotY;
                int otherRank = ox > 0 ? 4 : (ox < 0 ? 3 : (oz > 0 ? 2 : 1));
                if (otherDrop > myDrop + 0.35f || (MathF.Abs(otherDrop - myDrop) <= 0.35f && otherRank > myRank))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Havada dusen su hucresini 1m x 1m beyaz kare kutu yerine merkeze daraltilmis 8-kenarli puruzsuz yari-seffaf
        /// silindirik su sutunu olarak orer; alt/ust dusen hucrelerle ve alt havuzla 0.000m kesintisiz birlesir!
        /// </summary>
        private static void EmitMidAirFallingStreamSegment(
            FluidNeighborhood3x3Cache nb,
            int lx,
            int y,
            int lz,
            byte fluidType,
            byte renderId,
            byte selfMass,
            byte prevMass,
            int maxBufferY,
            List<FluidRichTriangle> outRichTriangles)
        {
            float cx = lx + 0.5f;
            float cz = lz + 0.5f;

            byte aboveMass = 0;
            bool hasAboveSame = false;
            if (y + 1 < maxBufferY)
            {
                nb.SampleCellFast(lx, y + 1, lz, out uint upS, out ushort upF);
                aboveMass = (byte)(upF & 0xFFu);
                hasAboveSame = !VoxelBitUtility.IsPhysicallySolid(upS) && aboveMass > 0 && ((upF >> 8) & 0x03u) == fluidType;
            }

            // Eger hemen altta durgun/derin karsilama havuzu varsa sutunun alt ucunu havuz yuzeyinin icine batir;
            // eger altta baska bir dusen damla varsa tam y sinirinda onun rTop yaricapiyla (= rBot) 0.000m dikisle birles!
            float botY = y;
            float rBot = ComputeFallingStreamRadius(selfMass);
            if (y > 0)
            {
                nb.SampleCellFast(lx, y - 1, lz, out uint dnS, out ushort dnF);
                byte dnMass = (byte)(dnF & 0xFFu);
                bool dnSame = !VoxelBitUtility.IsPhysicallySolid(dnS) && dnMass > 0 && ((dnF >> 8) & 0x03u) == fluidType;
                if (dnSame && !IsMidAirFreeFallStreamCell(nb, lx, y - 1, lz, fluidType))
                {
                    botY = (y - 1) + MassToFluidThickness(dnMass) * 0.90f;
                    rBot = MathF.Min(0.34f, rBot * 1.22f);
                }
                else if (!dnSame && y >= 2)
                {
                    // Alt hucre bos hava ama 2 altinda zemin/havuz varsa damla kuyrugunu asagi dogru uzat ki kopuk nokta kalmasin
                    nb.SampleCellFast(lx, y - 2, lz, out uint dn2S, out ushort dn2F);
                    if (VoxelBitUtility.IsPhysicallySolid(dn2S))
                    {
                        botY = MathF.Max(y - 0.55f, ComputeHydrostaticSurfaceNetsBedMeters(dn2S, 0u, y - 1));
                    }
                    else if ((dn2F & 0xFFu) > 0u && ((dn2F >> 8) & 0x03u) == fluidType)
                    {
                        botY = y - 0.45f;
                    }
                }
            }

            float topY = hasAboveSame
                ? (y + 1.0f)
                : (y + Math.Clamp(0.45f + 0.55f * (selfMass / 255.0f), 0.45f, 1.0f));
            float rTop = hasAboveSame ? ComputeFallingStreamRadius(aboveMass) : (rBot * 0.85f);

            float prevTopY = prevMass > 0
                ? (y + Math.Clamp(0.35f + 0.65f * (prevMass / 255.0f), 0.35f, 1.0f))
                : botY;
            byte encTopDy = hasAboveSame
                ? (byte)128
                : FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(topY - prevTopY, -1.0f, 1.0f));

            float colDepth = Math.Clamp(rBot * 1.6f, 0.14f, 0.48f);
            const float streamFoam = 0.22f;

            ReadOnlySpan<float> sin8 = stackalloc float[8] { 0.0f, 0.70710678f, 1.0f, 0.70710678f, 0.0f, -0.70710678f, -1.0f, -0.70710678f };
            ReadOnlySpan<float> cos8 = stackalloc float[8] { 1.0f, 0.70710678f, 0.0f, -0.70710678f, -1.0f, -0.70710678f, 0.0f, 0.70710678f };

            for (int k = 0; k < 8; k++)
            {
                int kNext = (k + 1) & 7;
                float s0 = sin8[kNext], c0 = cos8[kNext];
                float s1 = sin8[k], c1 = cos8[k];

                Vector3 pBot0 = new Vector3(cx + s0 * rBot, botY, cz + c0 * rBot);
                Vector3 pTop0 = new Vector3(cx + s0 * rTop, topY, cz + c0 * rTop);
                Vector3 pBot1 = new Vector3(cx + s1 * rBot, botY, cz + c1 * rBot);
                Vector3 pTop1 = new Vector3(cx + s1 * rTop, topY, cz + c1 * rTop);

                // Yukari-egimli yumusak silindirik normal (N.y = 0.84 -> yan kenarda sert beyaz Fresnel cubugu %0!)
                Vector3 n0 = Vector3.Normalize(new Vector3(s0 * 0.54f, 0.84f, c0 * 0.54f));
                Vector3 n1 = Vector3.Normalize(new Vector3(s1 * 0.54f, 0.84f, c1 * 0.54f));

                outRichTriangles.Add(new FluidRichTriangle
                {
                    C0 = MakeSample(pBot0, n0, renderId, 128, 128, 128, colDepth, streamFoam),
                    C1 = MakeSample(pTop0, n0, renderId, encTopDy, 128, 128, colDepth, streamFoam),
                    C2 = MakeSample(pTop1, n1, renderId, encTopDy, 128, 128, colDepth, streamFoam)
                });
                outRichTriangles.Add(new FluidRichTriangle
                {
                    C0 = MakeSample(pBot0, n0, renderId, 128, 128, 128, colDepth, streamFoam),
                    C1 = MakeSample(pTop1, n1, renderId, encTopDy, 128, 128, colDepth, streamFoam),
                    C2 = MakeSample(pBot1, n1, renderId, 128, 128, 128, colDepth, streamFoam)
                });

                if (!hasAboveSame)
                {
                    Vector3 pApex = new Vector3(cx, topY + rTop * 0.25f, cz);
                    Vector3 pCapA = new Vector3(cx + sin8[k] * rTop, topY, cz + cos8[k] * rTop);
                    Vector3 pCapB = new Vector3(cx + sin8[kNext] * rTop, topY, cz + cos8[kNext] * rTop);
                    outRichTriangles.Add(new FluidRichTriangle
                    {
                        C0 = MakeSample(pApex, Vector3.UnitY, renderId, encTopDy, 128, 128, colDepth, 0.14f),
                        C1 = MakeSample(pCapA, n1, renderId, encTopDy, 128, 128, colDepth, 0.14f),
                        C2 = MakeSample(pCapB, n0, renderId, encTopDy, 128, 128, colDepth, 0.14f)
                    });
                }
            }
        }

        private static bool HasThickUndergroundCaveRoof(uint[] centerSolids, int lx, int y, int lz, int maxBufferY)
        {
            int consecutiveSolid = 0;
            int checkTopY = Math.Min(maxBufferY - 1, y + 20);
            for (int sy = y + 2; sy <= checkTopY; sy++)
            {
                uint aboveRaw = centerSolids[(sy << 8) | (lz << 4) | lx];
                if (VoxelBitUtility.IsPhysicallySolid(aboveRaw) && VoxelBitUtility.GetShapeId(aboveRaw) == 0)
                {
                    consecutiveSolid++;
                    if (consecutiveSolid >= 6)
                    {
                        return true;
                    }
                }
                else
                {
                    consecutiveSolid = 0;
                }
            }
            return false;
        }

        /// <summary>
        /// Paket 4B.2 Gercek Sivi Sutunu Derinligi (AK-7):
        /// Eski CountSameFluidBelow her islak hucreye kutlesine bakmaksizin +1.0m ekliyordu (2 birim yagmuru 1.0m okyanus sayiyordu!).
        /// Yeni fonksiyon yalnizca bitisik sivi hucrelerinin gercek (mass / 255.0f) fiziksel yuksekligini toplar!
        /// </summary>
        private static float ComputeTrueFluidColumnDepthMeters(
            FluidNeighborhood3x3Cache nb,
            int lx,
            int topY,
            int lz,
            byte fluidType,
            float topCellThickness)
        {
            float sumMeters = Math.Max(0f, topCellThickness);
            int minY = Math.Max(0, topY - 7);
            for (int sy = topY - 1; sy >= minY; sy--)
            {
                nb.SampleCellFast(lx, sy, lz, out uint sRaw, out ushort fRaw);
                byte m = (byte)(fRaw & 0xFFu);
                if (m > 0 && ((fRaw >> 8) & 0x03u) == fluidType)
                {
                    sumMeters += m * (1.0f / 255.0f);
                    if (VoxelBitUtility.IsPhysicallySolid(sRaw)) break;
                }
                else
                {
                    break;
                }
            }
            return Math.Clamp(sumMeters, 0.0f, 8.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeHydrostaticSurfaceNetsBedMeters(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            byte curMat = VoxelBitUtility.GetMaterialId(currentSolidRaw);
            byte curShape = VoxelBitUtility.GetShapeId(currentSolidRaw);
            if (curMat != MaterialIdCatalog.Air && curShape != 0 && curShape != VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                byte cap = ComputeSdfPorousCapacity(currentSolidRaw);
                if (cap > 0 && cap < 255)
                {
                    return y + ((255 - cap) * (1.0f / 255.0f));
                }
            }

            if (y <= 0) return 0f;
            if (!VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
            {
                return y - 1;
            }

            byte belowShape = VoxelBitUtility.GetShapeId(belowSolidRaw);
            if (belowShape == 0)
            {
                int dBelow = VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw);
                if (dBelow <= StratocraftConstants.SdfIsoLevel)
                {
                    return y - 1;
                }

                int dCurr = (curMat != MaterialIdCatalog.Air && curShape == 0)
                    ? VoxelBitUtility.GetDensityOrLiquid(currentSolidRaw)
                    : 0;
                int isoOffset255 = dCurr > 0
                    ? Math.Clamp(((dBelow - StratocraftConstants.SdfIsoLevel) * 255) / Math.Max(1, dBelow - dCurr), 4, 251)
                    : Math.Clamp(((dBelow - StratocraftConstants.SdfIsoLevel) * 255 + 30) / 60, 4, 255);
                return (y - 1) + (isoOffset255 * (1.0f / 255.0f));
            }

            return y;
        }

        // ==================== GERIYE DONUK UYUMLU LEGACY MESHER (FacetedVoxelTriangle) ====================

        /// <summary>
        /// Tek bir chunk'in 16-bit FluidCell16 katmanindan (komsu chunk'lari da dikkate alarak)
        /// 3B hacimli sivi yuzeyi + 4 su gecirmez yan duvar + SDF kiyi eteklerini uretir.
        /// </summary>
        public static void GenerateChunkFluidSurfaceMesh(
            int chunkX,
            int chunkZ,
            int maxY,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            List<FacetedVoxelTriangle> outFluidTriangles,
            int minY = 0)
        {
            if (solidChunks == null) throw new ArgumentNullException(nameof(solidChunks));
            if (fluidChunks == null) throw new ArgumentNullException(nameof(fluidChunks));

            if (!fluidChunks.TryGetValue((chunkX, chunkZ), out ushort[]? centerFluids) ||
                centerFluids == null ||
                !solidChunks.TryGetValue((chunkX, chunkZ), out uint[]? centerSolids) ||
                centerSolids == null)
            {
                return;
            }

            int maxBufferY = Math.Min(centerFluids.Length >> 8, centerSolids.Length >> 8);
            maxY = Math.Clamp(maxY, 2, Math.Min(StratocraftConstants.ChunkSizeY, maxBufferY));
            int startY = Math.Clamp(minY, 0, maxY - 1);
            bool suppressUndergroundCaveFluids = minY >= 68;
            Span<ContourVertex> ring = stackalloc ContourVertex[16];

            for (int y = startY; y < maxY; y++)
            {
                int yOffset = y << 8;

                // Y-dilimi erken cikis: 16x16 katmanda hic sivi yoksa bos katmanlari aninda atla
                bool sliceHasFluid = false;
                for (int i = 0; i < 256; i++)
                {
                    if ((centerFluids[yOffset | i] & 0xFFu) != 0u)
                    {
                        sliceHasFluid = true;
                        break;
                    }
                }

                if (!sliceHasFluid)
                {
                    continue;
                }

                for (int lz = 0; lz < 16; lz++)
                {
                    int rowOffset = yOffset | (lz << 4);
                    for (int lx = 0; lx < 16; lx++)
                    {
                        ushort rawFluid = centerFluids[rowOffset | lx];
                        byte level = (byte)(rawFluid & 0xFFu);
                        byte fType = (byte)((rawFluid >> 8) & 0x03u);
                        if (level == 0 || fType == 0)
                        {
                            continue;
                        }

                        // Yalnizca derin yeralti magaralarini (y < 74 ve ustunde >= 6 blok kalin yekpare kaya tavani olanlar) gizle;
                        // oyuncunun yuzeyde actigi cukurlar (Y = 74..120) ustunde toprak dudagi olsa bile ASLA gizlenmez!
                        if (suppressUndergroundCaveFluids && y < 74)
                        {
                            if (HasThickUndergroundCaveRoof(centerSolids, lx, y, lz, maxBufferY))
                            {
                                continue;
                            }
                        }

                        ushort aboveFluid = (y + 1 < maxBufferY)
                            ? centerFluids[((y + 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        bool hasFreeTopSurface = ((aboveFluid & 0xFFu) == 0u) || (((aboveFluid >> 8) & 0x03u) != fType);

                        ushort belowFluid = (y > 0)
                            ? centerFluids[((y - 1) << 8) | (lz << 4) | lx]
                            : (ushort)0;
                        uint belowSolid = (y > 0)
                            ? centerSolids[((y - 1) << 8) | (lz << 4) | lx]
                            : 0u;
                        bool hasExposedBottom = (y == 0) ||
                            (((belowFluid & 0xFFu) == 0u || ((belowFluid >> 8) & 0x03u) != fType) &&
                             !VoxelBitUtility.IsPhysicallySolid(belowSolid));

                        byte renderMat = ResolveFluidRenderMaterial(fType);
                        byte temp = fType == 2 ? (byte)242 : (byte)20;
                        byte packedAo = HybridSurfaceNetsAndShapeMesher.PackAoAndWear(240, 0);

                        float h00 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 0, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt00, out float bed00, out bool shore00);
                        float h10 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 0, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt10, out float bed10, out bool shore10);
                        float h11 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 1, cornerDz: 1, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt11, out float bed11, out bool shore11);
                        float h01 = ComputeCornerHeightAndSkirt(
                            chunkX, chunkZ, lx, y, lz, cornerDx: 0, cornerDz: 1, fType, !hasExposedBottom,
                            centerSolids, centerFluids, solidChunks, fluidChunks, out Vector3 skirt01, out float bed01, out bool shore01);

                        Vector3 p00 = new Vector3(lx, h00, lz) + skirt00;
                        Vector3 p10 = new Vector3(lx + 1, h10, lz) + skirt10;
                        Vector3 p11 = new Vector3(lx + 1, h11, lz + 1) + skirt11;
                        Vector3 p01 = new Vector3(lx, h01, lz + 1) + skirt01;

                        Vector3 b00 = new Vector3(p00.X, bed00, p00.Z);
                        Vector3 b10 = new Vector3(p10.X, bed10, p10.Z);
                        Vector3 b11 = new Vector3(p11.X, bed11, p11.Z);
                        Vector3 b01 = new Vector3(p01.X, bed01, p01.Z);

                        bool wetNegX = HasSameFluidAt(chunkX, chunkZ, lx - 1, y, lz, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallNegX);
                        bool wetPosX = HasSameFluidAt(chunkX, chunkZ, lx + 1, y, lz, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallPosX);
                        bool wetNegZ = HasSameFluidAt(chunkX, chunkZ, lx, y, lz - 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallNegZ);
                        bool wetPosZ = HasSameFluidAt(chunkX, chunkZ, lx, y, lz + 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallPosZ);

                        bool wet00Diag = HasSameFluidAt(chunkX, chunkZ, lx - 1, y, lz - 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall00Diag);
                        bool wet01Diag = HasSameFluidAt(chunkX, chunkZ, lx - 1, y, lz + 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall01Diag);
                        bool wet11Diag = HasSameFluidAt(chunkX, chunkZ, lx + 1, y, lz + 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall11Diag);
                        bool wet10Diag = HasSameFluidAt(chunkX, chunkZ, lx + 1, y, lz - 1, fType, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall10Diag);

                        bool bevel00 = !wetNegX && !wetNegZ && !wet00Diag && !shore00 && !wallNegX && !wallNegZ && !wall00Diag;
                        bool bevel01 = !wetNegX && !wetPosZ && !wet01Diag && !shore01 && !wallNegX && !wallPosZ && !wall01Diag;
                        bool bevel11 = !wetPosX && !wetPosZ && !wet11Diag && !shore11 && !wallPosX && !wallPosZ && !wall11Diag;
                        bool bevel10 = !wetPosX && !wetNegZ && !wet10Diag && !shore10 && !wallPosX && !wallNegZ && !wall10Diag;

                        Vector3 midNegXTop = 0.5f * (p00 + p01);
                        Vector3 midNegXBot = 0.5f * (b00 + b01);
                        Vector3 midPosZTop = 0.5f * (p01 + p11);
                        Vector3 midPosZBot = 0.5f * (b01 + b11);
                        Vector3 midPosXTop = 0.5f * (p11 + p10);
                        Vector3 midPosXBot = 0.5f * (b11 + b10);
                        Vector3 midNegZTop = 0.5f * (p10 + p00);
                        Vector3 midNegZBot = 0.5f * (b10 + b00);

                        Vector3 pCenter = 0.25f * (p00 + p10 + p11 + p01);
                        Vector3 bCenter = 0.25f * (b00 + b10 + b11 + b01);

                        int ringCount = 0;

                        if (bevel00)
                        {
                            Vector3 arc00Top = Vector3.Lerp(pCenter, 0.5f * (midNegZTop + midNegXTop), 0.96f);
                            Vector3 arc00Bot = Vector3.Lerp(bCenter, 0.5f * (midNegZBot + midNegXBot), 0.96f);
                            ring[ringCount++] = new ContourVertex { Top = arc00Top, Bottom = arc00Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midNegXTop, Bottom = midNegXBot, EdgeDirX = -1, EdgeDirZ = 0 };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p00, Bottom = b00, EdgeDirX = -1, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midNegXTop, Bottom = midNegXBot, EdgeDirX = -1, EdgeDirZ = 0 };
                        }

                        if (bevel01)
                        {
                            Vector3 arc01Top = Vector3.Lerp(pCenter, 0.5f * (midNegXTop + midPosZTop), 0.96f);
                            Vector3 arc01Bot = Vector3.Lerp(bCenter, 0.5f * (midNegXBot + midPosZBot), 0.96f);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = new ContourVertex { Top = arc01Top, Bottom = arc01Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midPosZTop, Bottom = midPosZBot, EdgeDirX = 0, EdgeDirZ = 1 };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p01, Bottom = b01, EdgeDirX = 0, EdgeDirZ = 1 };
                            ring[ringCount++] = new ContourVertex { Top = midPosZTop, Bottom = midPosZBot, EdgeDirX = 0, EdgeDirZ = 1 };
                        }

                        if (bevel11)
                        {
                            Vector3 arc11Top = Vector3.Lerp(pCenter, 0.5f * (midPosZTop + midPosXTop), 0.96f);
                            Vector3 arc11Bot = Vector3.Lerp(bCenter, 0.5f * (midPosZBot + midPosXBot), 0.96f);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = new ContourVertex { Top = arc11Top, Bottom = arc11Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midPosXTop, Bottom = midPosXBot, EdgeDirX = 1, EdgeDirZ = 0 };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p11, Bottom = b11, EdgeDirX = 1, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex { Top = midPosXTop, Bottom = midPosXBot, EdgeDirX = 1, EdgeDirZ = 0 };
                        }

                        if (bevel10)
                        {
                            Vector3 arc10Top = Vector3.Lerp(pCenter, 0.5f * (midPosXTop + midNegZTop), 0.96f);
                            Vector3 arc10Bot = Vector3.Lerp(bCenter, 0.5f * (midPosXBot + midNegZBot), 0.96f);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = new ContourVertex { Top = arc10Top, Bottom = arc10Bot, EdgeDirX = 0, EdgeDirZ = 0 };
                            ring[ringCount++] = new ContourVertex
                            {
                                Top = midNegZTop,
                                Bottom = midNegZBot,
                                EdgeDirX = 0,
                                EdgeDirZ = bevel00 ? (sbyte)0 : (sbyte)-1
                            };
                        }
                        else
                        {
                            ring[ringCount++] = new ContourVertex { Top = p10, Bottom = b10, EdgeDirX = 0, EdgeDirZ = -1 };
                            ring[ringCount++] = new ContourVertex
                            {
                                Top = midNegZTop,
                                Bottom = midNegZBot,
                                EdgeDirX = 0,
                                EdgeDirZ = bevel00 ? (sbyte)0 : (sbyte)-1
                            };
                        }

                        for (int i = 0; i < ringCount; i++)
                        {
                            int next = (i + 1 == ringCount) ? 0 : (i + 1);
                            ContourVertex va = ring[i];
                            ContourVertex vb = ring[next];

                            if (hasFreeTopSurface)
                            {
                                byte shade = (i & 1) == 0 ? (byte)210 : (byte)202;
                                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                    pCenter, va.Top, vb.Top, renderMat, temp, shade, packedAo, outFluidTriangles);
                            }

                            if (hasExposedBottom)
                            {
                                HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                                    bCenter, vb.Bottom, va.Bottom, renderMat, temp, 180, packedAo, outFluidTriangles);
                            }

                            if (va.EdgeDirX == 0 && va.EdgeDirZ == 0)
                            {
                                EmitBevelSkirtQuad(
                                    vb.Bottom, vb.Top, va.Top, va.Bottom,
                                    renderMat, temp, packedAo, outFluidTriangles);
                            }
                            else
                            {
                                EmitSideWallIfExposed(
                                    chunkX, chunkZ, lx, y, lz, va.EdgeDirX, va.EdgeDirZ, fType, renderMat, temp, packedAo,
                                    vb.Bottom, vb.Top, va.Top, va.Bottom,
                                    centerSolids, centerFluids, solidChunks, fluidChunks, outFluidTriangles);
                            }
                        }
                    }
                }
            }
        }

        private static bool HasSameFluidAt(
            int centerCx,
            int centerCz,
            int nx,
            int y,
            int nz,
            byte fluidType,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out bool touchesWall)
        {
            SampleCell(centerCx, centerCz, nx, y, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nSolid, out ushort nFluid);
            byte mat = VoxelBitUtility.GetMaterialId(nSolid);
            byte shape = VoxelBitUtility.GetShapeId(nSolid);
            touchesWall = mat != 0 && (shape != 0 || VoxelBitUtility.GetDensityOrLiquid(nSolid) >= 32);
            return (nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType;
        }

        private static void EmitBevelSkirtQuad(
            Vector3 b0,
            Vector3 t0,
            Vector3 t1,
            Vector3 b1,
            byte renderMat,
            byte temp,
            byte packedAo,
            List<FacetedVoxelTriangle> outFluidTriangles)
        {
            if (t0.Y <= b0.Y + 0.004f && t1.Y <= b1.Y + 0.004f)
            {
                return;
            }

            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t0, t1, renderMat, temp, 178, packedAo, outFluidTriangles);
            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t1, b1, renderMat, temp, 168, packedAo, outFluidTriangles);
        }

        private static float ComputeCornerHeightAndSkirt(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            int cornerDx,
            int cornerDz,
            byte fluidType,
            bool embedFoundationIntoGround,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out Vector3 outSkirtOffset,
            out float outCornerBedY,
            out bool outTouchesSdfShore)
        {
            int baseCornerX = lx + cornerDx;
            int baseCornerZ = lz + cornerDz;

            float wetSurfaceSum = 0f;
            int wetSamples = 0;
            byte maxWetMass = 0;
            bool touchesSdfShore = false;
            bool anyColumnAboveFluid = false;

            float wetBedSum = 0f;
            float minWetBedY = float.MaxValue;
            int wetBedSamples = 0;

            float dualTerrainSum = 0f;
            int dualTerrainSamples = 0;

            float sdfNegX = 0f;
            float sdfPosX = 0f;
            float sdfNegZ = 0f;
            float sdfPosZ = 0f;

            for (int dz = -1; dz <= 0; dz++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int sx = baseCornerX + dx;
                    int sz = baseCornerZ + dz;
                    SampleCell(centerCx, centerCz, sx, y, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint solidRaw, out ushort fluidRaw);

                    uint belowSolidRaw = 0u;
                    ushort belowFluidRaw = 0;
                    if (y > 0)
                    {
                        SampleCell(centerCx, centerCz, sx, y - 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out belowSolidRaw, out belowFluidRaw);
                    }

                    uint aboveSolidRaw = 0u;
                    ushort aboveFluidRaw = 0;
                    if (y + 1 < 256)
                    {
                        SampleCell(centerCx, centerCz, sx, y + 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out aboveSolidRaw, out aboveFluidRaw);
                    }

                    bool isCurrentCellSolidWall = VoxelBitUtility.IsPhysicallySolid(solidRaw);
                    bool isAboveCellSolidWall = VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw);
                    bool hasBelowSameFluid = y > 0 && (belowFluidRaw & 0xFFu) > 0u && ((belowFluidRaw >> 8) & 0x03u) == fluidType;
                    bool hasCurrentSameFluid = (fluidRaw & 0xFFu) > 0u && ((fluidRaw >> 8) & 0x03u) == fluidType;

                    float cellBedY = y;
                    if (!hasBelowSameFluid && y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && !isCurrentCellSolidWall)
                    {
                        cellBedY = ComputeColumnSurfaceNetsBedMeters(belowSolidRaw, solidRaw, y);
                        dualTerrainSum += cellBedY;
                        dualTerrainSamples++;
                    }
                    else if (isCurrentCellSolidWall && VoxelBitUtility.GetShapeId(solidRaw) == 0 && !isAboveCellSolidWall)
                    {
                        float slopeStepTopY = ComputeColumnSurfaceNetsBedMeters(solidRaw, aboveSolidRaw, y + 1);
                        dualTerrainSum += slopeStepTopY;
                        dualTerrainSamples++;
                    }

                    if (hasCurrentSameFluid || hasBelowSameFluid)
                    {
                        wetBedSum += cellBedY;
                        if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                        wetBedSamples++;
                    }

                    if (y + 1 < 256 && (aboveFluidRaw & 0xFFu) > 0u && ((aboveFluidRaw >> 8) & 0x03u) == fluidType)
                    {
                        wetSurfaceSum += y + 1.0f;
                        wetSamples++;
                        maxWetMass = 255;
                        anyColumnAboveFluid = true;
                    }
                    else if (hasCurrentSameFluid)
                    {
                        byte cellMass = (byte)(fluidRaw & 0xFFu);
                        if (cellMass > maxWetMass) maxWetMass = cellMass;
                        byte cap = ComputeSdfPorousCapacity(solidRaw);
                        float visThickness = ComputeVisualFluidThickness(cellMass, cap);
                        wetSurfaceSum += Math.Min(y + 0.98f, cellBedY + visThickness);
                        wetSamples++;
                    }

                    byte sMat = VoxelBitUtility.GetMaterialId(solidRaw);
                    byte sShape = VoxelBitUtility.GetShapeId(solidRaw);
                    byte sDens = (sMat != 0 && sShape == 0) ? VoxelBitUtility.GetDensityOrLiquid(solidRaw) : (byte)0;
                    if (sDens >= 32)
                    {
                        touchesSdfShore = true;
                    }

                    if (dx == -1) sdfNegX += sDens;
                    else sdfPosX += sDens;

                    if (dz == -1) sdfNegZ += sDens;
                    else sdfPosZ += sDens;
                }
            }

            outTouchesSdfShore = touchesSdfShore;
            float avgWetBedY = wetBedSamples > 0 ? (wetBedSum / wetBedSamples) : y;
            float baseMinBedY = minWetBedY < float.MaxValue ? minWetBedY : avgWetBedY;
            outCornerBedY = embedFoundationIntoGround
                ? Math.Max(0f, baseMinBedY - FoundationEmbedDepthMeters)
                : avgWetBedY;

            float avgWetSurfaceY = wetSamples > 0
                ? (wetSurfaceSum / wetSamples)
                : (avgWetBedY + MinThinFilmThicknessMeters);

            outSkirtOffset = Vector3.Zero;
            if (touchesSdfShore)
            {
                Vector3 gradXZ = new Vector3(sdfPosX - sdfNegX, 0f, sdfPosZ - sdfNegZ);
                float lenSq = gradXZ.LengthSquared();
                if (lenSq > 1e-4f)
                {
                    Vector3 dirToWall = gradXZ / MathF.Sqrt(lenSq);
                    outSkirtOffset = dirToWall * ShorelineSkirtExtensionMeters;
                }
                else
                {
                    Vector3 outward = Vector3.Normalize(new Vector3(cornerDx == 0 ? -1f : 1f, 0f, cornerDz == 0 ? -1f : 1f));
                    outSkirtOffset = outward * ShorelineSkirtExtensionMeters;
                }
            }

            if (anyColumnAboveFluid)
            {
                return y + 1.0f;
            }

            float cornerTopY = avgWetSurfaceY;

            if (embedFoundationIntoGround && wetSamples == 1 && maxWetMass < 80 && dualTerrainSamples > 0)
            {
                float dualTerrainY = dualTerrainSum / dualTerrainSamples;
                float clearance = maxWetMass < 18
                    ? (0.030f + (MinSlopeStreamClearanceMeters - 0.030f) * (maxWetMass / 18.0f))
                    : MinSlopeStreamClearanceMeters;
                float minVisibleStreamTopY = dualTerrainY + clearance;
                if (cornerTopY < minVisibleStreamTopY)
                {
                    cornerTopY = minVisibleStreamTopY;
                }
            }

            if (y + 1 < 256)
            {
                SampleCell(centerCx, centerCz, lx, y + 1, lz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint selfAboveSolid, out _);
                if (VoxelBitUtility.IsPhysicallySolid(selfAboveSolid))
                {
                    return Math.Min(y + 0.96f, cornerTopY);
                }
            }

            return Math.Min(y + 1.0f, cornerTopY);
        }

        private static void EmitSideWallIfExposed(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            int dirX,
            int dirZ,
            byte fluidType,
            byte renderMat,
            byte temp,
            byte packedAo,
            Vector3 b0,
            Vector3 t0,
            Vector3 t1,
            Vector3 b1,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            List<FacetedVoxelTriangle> outFluidTriangles)
        {
            int nx = lx + dirX;
            int nz = lz + dirZ;
            SampleCell(centerCx, centerCz, nx, y, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nSolid, out ushort nFluid);

            if ((nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType)
            {
                return;
            }

            if (VoxelBitUtility.IsPhysicallySolid(nSolid) && VoxelBitUtility.GetShapeId(nSolid) != 0)
            {
                return;
            }

            if (t0.Y <= b0.Y + 0.005f && t1.Y <= b1.Y + 0.005f)
            {
                return;
            }

            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t0, t1, renderMat, temp, 178, packedAo, outFluidTriangles);
            HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(b0, t1, b1, renderMat, temp, 168, packedAo, outFluidTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SampleCell(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out uint solidRaw,
            out ushort fluidRaw)
        {
            if ((uint)lx < 16u && (uint)lz < 16u)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                solidRaw = centerSolids[idx];
                fluidRaw = centerFluids[idx];
                return;
            }

            int wx = centerCx * 16 + lx;
            int wz = centerCz * 16 + lz;
            int ncx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int ncz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int nlx = wx - ncx * 16;
            int nlz = wz - ncz * 16;
            int nIdx = (y << 8) | (nlz << 4) | nlx;

            if (solidChunks.TryGetValue((ncx, ncz), out uint[]? ns) && ns != null && (uint)nIdx < (uint)ns.Length)
            {
                solidRaw = ns[nIdx];
                fluidRaw = fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nf) && nf != null && (uint)nIdx < (uint)nf.Length ? nf[nIdx] : (ushort)0;
                return;
            }

            if (solidChunks.Count > 4)
            {
                int cIdx = (y << 8) | (Math.Clamp(lz, 0, 15) << 4) | Math.Clamp(lx, 0, 15);
                solidRaw = centerSolids[cIdx];
                fluidRaw = centerFluids[cIdx];
                return;
            }

            solidRaw = 0u;
            fluidRaw = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeColumnSurfaceNetsBedMeters(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            byte curMat = VoxelBitUtility.GetMaterialId(currentSolidRaw);
            byte curShape = VoxelBitUtility.GetShapeId(currentSolidRaw);
            byte curDens = (curMat != 0 && curShape == 0) ? VoxelBitUtility.GetDensityOrLiquid(currentSolidRaw) : (byte)0;

            if (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
            {
                byte belowShape = VoxelBitUtility.GetShapeId(belowSolidRaw);
                if (belowShape == 0)
                {
                    float dBelow = Math.Max((int)VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw), 64);
                    float dCurr = curDens;
                    float denom = Math.Max(1f, dBelow - dCurr);
                    float frac = Math.Clamp((dBelow - 64f) / denom, 0.08f, 0.92f);
                    return (y - 1) + frac;
                }
                return y;
            }

            if (curDens > 0 && curDens < 64)
            {
                return y + (curDens / 64.0f) * 0.45f;
            }

            return y;
        }

        private static byte ComputeSdfPorousCapacity(uint solidRaw)
        {
            byte mat = VoxelBitUtility.GetMaterialId(solidRaw);
            if (mat == 0) return 255;
            if (VoxelBitUtility.GetShapeId(solidRaw) != 0) return 255;
            int rawDens = VoxelBitUtility.GetDensityOrLiquid(solidRaw);
            if (rawDens <= 32) return 255;
            if (rawDens >= 96) return 0;
            float rho = (rawDens - 64) * (1.0f / 63.0f);
            return (byte)Math.Clamp((int)MathF.Floor(255.0f * (0.5f - rho)), 0, 255);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ResolveFluidRenderMaterial(byte fluidType)
        {
            return fluidType switch
            {
                2 => MaterialIdCatalog.FiniteLavaSurface,
                3 => MaterialIdCatalog.FiniteAcidSurface,
                _ => MaterialIdCatalog.FiniteWaterSurface
            };
        }

#if UNITY_5_3_OR_NEWER
        private static readonly VertexAttributeDescriptor[] FluidVertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
        };

        /// <summary>
        /// Dogrudan FluidSurfaceSkirtMesher icinde tanimlanan sifir-GC 36-Byte Unity Mesh yukleyicisi.
        /// HybridSurfaceNetsAndShapeMesher.cs'e hicbir bagimlilik olusturmaz.
        /// </summary>
        public static void BuildFluidUnityMeshZeroGc(
            List<FluidRichTriangle> richTriangles,
            UnityEngine.Mesh targetMesh)
        {
            if (richTriangles == null) throw new ArgumentNullException(nameof(richTriangles));
            if (targetMesh == null) throw new ArgumentNullException(nameof(targetMesh));

            int triCount = richTriangles.Count;
            if (triCount == 0)
            {
                targetMesh.Clear(keepVertexLayout: true);
                targetMesh.bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one);
                return;
            }

            int vertCount = triCount * 3;
            int indexCount = triCount * 3;

            UnityEngine.Mesh.MeshDataArray dataArray = UnityEngine.Mesh.AllocateWritableMeshData(1);
            UnityEngine.Mesh.MeshData data = dataArray[0];
            data.SetVertexBufferParams(vertCount, FluidVertexLayout);
            IndexFormat idxFormat = vertCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            data.SetIndexBufferParams(indexCount, idxFormat);

            NativeArray<FluidMeshVertex> vBuf = data.GetVertexData<FluidMeshVertex>(0);
            UnityEngine.Vector3 bMin = new UnityEngine.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            UnityEngine.Vector3 bMax = new UnityEngine.Vector3(float.MinValue, float.MinValue, float.MinValue);

            if (idxFormat == IndexFormat.UInt32)
            {
                NativeArray<uint> iBuf = data.GetIndexData<uint>();
                int cursor = 0;
                for (int i = 0; i < triCount; i++)
                {
                    FluidRichTriangle tri = richTriangles[i];
                    WriteRichVertex(ref vBuf, cursor, in tri.C0, ref bMin, ref bMax);
                    iBuf[cursor] = (uint)cursor;
                    cursor++;
                    WriteRichVertex(ref vBuf, cursor, in tri.C1, ref bMin, ref bMax);
                    iBuf[cursor] = (uint)cursor;
                    cursor++;
                    WriteRichVertex(ref vBuf, cursor, in tri.C2, ref bMin, ref bMax);
                    iBuf[cursor] = (uint)cursor;
                    cursor++;
                }
            }
            else
            {
                NativeArray<ushort> iBuf = data.GetIndexData<ushort>();
                int cursor = 0;
                for (int i = 0; i < triCount; i++)
                {
                    FluidRichTriangle tri = richTriangles[i];
                    WriteRichVertex(ref vBuf, cursor, in tri.C0, ref bMin, ref bMax);
                    iBuf[cursor] = (ushort)cursor;
                    cursor++;
                    WriteRichVertex(ref vBuf, cursor, in tri.C1, ref bMin, ref bMax);
                    iBuf[cursor] = (ushort)cursor;
                    cursor++;
                    WriteRichVertex(ref vBuf, cursor, in tri.C2, ref bMin, ref bMax);
                    iBuf[cursor] = (ushort)cursor;
                    cursor++;
                }
            }

            UnityEngine.Bounds bounds = new UnityEngine.Bounds((bMin + bMax) * 0.5f, bMax - bMin);
            data.subMeshCount = 1;
            data.SetSubMesh(
                0,
                new SubMeshDescriptor(0, indexCount, UnityEngine.MeshTopology.Triangles)
                {
                    bounds = bounds,
                    vertexCount = vertCount
                },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(
                dataArray,
                targetMesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            targetMesh.bounds = bounds;
        }

        /// <summary>
        /// Paket 4B.2 Ufuk Okyanusu (DistantHorizon_ClipmapRing_Fluid) Uyumlayicisi:
        /// FacetedVoxelTriangle ufkunu 36-Byte FluidMeshVertex duzenine (Tuzlu Deniz ID=12, Dy=128, Vel=128, Depth=6.0m)
        /// sifir-GC ile donusturerek yakin denizle 0.000m kot ve %100 shader oznitelik uyumu saglar.
        /// </summary>
        public static UnityEngine.Mesh BuildDistantHorizonFluidMeshZeroGc(
            List<FacetedVoxelTriangle> horizonTriangles,
            string meshName,
            UnityEngine.Mesh? existingMesh)
        {
            UnityEngine.Mesh targetMesh = existingMesh != null
                ? existingMesh
                : new UnityEngine.Mesh { name = meshName };

            int triCount = horizonTriangles != null ? horizonTriangles.Count : 0;
            if (triCount == 0)
            {
                targetMesh.Clear(keepVertexLayout: true);
                targetMesh.bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one);
                return targetMesh;
            }

            int vertCount = triCount * 3;
            UnityEngine.Mesh.MeshDataArray dataArray = UnityEngine.Mesh.AllocateWritableMeshData(1);
            UnityEngine.Mesh.MeshData data = dataArray[0];
            data.SetVertexBufferParams(vertCount, FluidVertexLayout);
            IndexFormat idxFormat = vertCount > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            data.SetIndexBufferParams(vertCount, idxFormat);

            NativeArray<FluidMeshVertex> vBuf = data.GetVertexData<FluidMeshVertex>(0);
            UnityEngine.Vector3 bMin = new UnityEngine.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            UnityEngine.Vector3 bMax = new UnityEngine.Vector3(float.MinValue, float.MinValue, float.MinValue);
            UnityEngine.Color32 calmSeaColor = new UnityEngine.Color32(12, 128, 128, 128);
            UnityEngine.Vector2 deepOceanAux = new UnityEngine.Vector2(6.0f, 0.0f);

            int cursor = 0;
            for (int i = 0; i < triCount; i++)
            {
                FacetedVoxelTriangle tri = horizonTriangles![i];
                WriteHorizonVertex(ref vBuf, cursor++, tri.V0, calmSeaColor, deepOceanAux, ref bMin, ref bMax);
                WriteHorizonVertex(ref vBuf, cursor++, tri.V1, calmSeaColor, deepOceanAux, ref bMin, ref bMax);
                WriteHorizonVertex(ref vBuf, cursor++, tri.V2, calmSeaColor, deepOceanAux, ref bMin, ref bMax);
            }

            if (idxFormat == IndexFormat.UInt32)
            {
                NativeArray<uint> iBuf = data.GetIndexData<uint>();
                for (int i = 0; i < vertCount; i++) iBuf[i] = (uint)i;
            }
            else
            {
                NativeArray<ushort> iBuf = data.GetIndexData<ushort>();
                for (int i = 0; i < vertCount; i++) iBuf[i] = (ushort)i;
            }

            UnityEngine.Bounds bounds = new UnityEngine.Bounds((bMin + bMax) * 0.5f, bMax - bMin);
            data.subMeshCount = 1;
            data.SetSubMesh(
                0,
                new SubMeshDescriptor(0, vertCount, UnityEngine.MeshTopology.Triangles)
                {
                    bounds = bounds,
                    vertexCount = vertCount
                },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(
                dataArray,
                targetMesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            targetMesh.bounds = bounds;
            return targetMesh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteHorizonVertex(
            ref NativeArray<FluidMeshVertex> vBuf,
            int index,
            Vector3 p,
            UnityEngine.Color32 col,
            UnityEngine.Vector2 aux,
            ref UnityEngine.Vector3 bMin,
            ref UnityEngine.Vector3 bMax)
        {
            UnityEngine.Vector3 pos = new UnityEngine.Vector3(p.X, p.Y, p.Z);
            vBuf[index] = new FluidMeshVertex
            {
                Position = pos,
                Normal = UnityEngine.Vector3.up,
                Color = col,
                AuxUv0 = aux
            };
            if (pos.x < bMin.x) bMin.x = pos.x;
            if (pos.y < bMin.y) bMin.y = pos.y;
            if (pos.z < bMin.z) bMin.z = pos.z;
            if (pos.x > bMax.x) bMax.x = pos.x;
            if (pos.y > bMax.y) bMax.y = pos.y;
            if (pos.z > bMax.z) bMax.z = pos.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteRichVertex(
            ref NativeArray<FluidMeshVertex> vBuf,
            int index,
            in FluidCornerSample s,
            ref UnityEngine.Vector3 bMin,
            ref UnityEngine.Vector3 bMax)
        {
            UnityEngine.Vector3 pos = new UnityEngine.Vector3(s.PositionCurr.X, s.PositionCurr.Y, s.PositionCurr.Z);
            UnityEngine.Vector3 nrm = new UnityEngine.Vector3(s.Normal.X, s.Normal.Y, s.Normal.Z);

            vBuf[index] = new FluidMeshVertex
            {
                Position = pos,
                Normal = nrm,
                Color = new UnityEngine.Color32(s.RenderFluidId, s.EncodedSubTickDy, s.EncodedVelX, s.EncodedVelZ),
                AuxUv0 = new UnityEngine.Vector2(s.DepthMeters, s.WaterfallFoam01)
            };

            if (pos.x < bMin.x) bMin.x = pos.x;
            if (pos.y < bMin.y) bMin.y = pos.y;
            if (pos.z < bMin.z) bMin.z = pos.z;
            if (pos.x > bMax.x) bMax.x = pos.x;
            if (pos.y > bMax.y) bMax.y = pos.y;
            if (pos.z > bMax.z) bMax.z = pos.z;
        }
#endif
    }
}
