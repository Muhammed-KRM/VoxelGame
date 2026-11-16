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
        public const float MinThinFilmThicknessMeters = 0.025f;
        public const float MinVisibleFilmThickness = 0.025f;
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
        }

        /// <summary>
        /// Geriye donuk uyumlu parcali sivi kalinligi (legacy mesher ve hareket koprusu tarafindan kullanilir).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeVisualFluidThickness(byte mass, byte capacity)
        {
            if (mass == 0) return 0f;
            if (capacity == 0) return MaxVisualFluidThicknessMeters;

            float effMass = capacity < 255 ? Math.Clamp((mass * 255.0f) / capacity, 1.0f, 255.0f) : mass;
            if (effMass <= 12.0f)
            {
                return MinThinFilmThicknessMeters + (0.095f - MinThinFilmThicknessMeters) * ((effMass - 1.0f) / 11.0f);
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
        /// Paket 4B C1-surekli ve kesin monoton artan sivi kalinlik egrisi (m = 1..255).
        /// Kirik parcali dirsek icermez; m=0 -> 0, m=1 -> ~0.034m, m=255 -> 1.000m.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MassToFluidThickness(byte mass)
        {
            if (mass == 0) return 0.0f;
            if (mass >= 255) return 1.0f;
            float t = mass * (1.0f / 255.0f);
            return MinVisibleFilmThickness + ((1.0f - MinVisibleFilmThickness) * MathF.Pow(t, 0.85f));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MassToFluidThicknessWithCapacity(byte mass, byte capacity)
        {
            if (mass == 0) return 0.0f;
            if (capacity == 0) return 1.0f;
            if (capacity >= 255) return MassToFluidThickness(mass);
            float effMass = Math.Clamp((mass * 255.0f) / capacity, 1.0f, 255.0f);
            float t = effMass * (1.0f / 255.0f);
            return MinVisibleFilmThickness + ((1.0f - MinVisibleFilmThickness) * MathF.Pow(t, 0.85f));
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
                int flatIdx = (y << 8) | ((localZ & 15) << 4) | (localX & 15);
                int word = flatIdx >> 6;
                if ((uint)word < (uint)bits.Length && ((bits[word] >> (flatIdx & 63)) & 1UL) != 0UL)
                {
                    return 12;
                }
            }

            return 1;
        }

        /// <summary>
        /// Paket 4B Zengin Sivi Yuzey Mesher'i:
        /// C1 kalinlik egrisi, wetSamples==1 sicrama tamiri, 4-kose meniskus (Curr &amp; Prev),
        /// Rouse savak cokmesi, kesintisiz parabolik selale perdesi ve tatli/tuzlu Color32 kodlamasi uretir.
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

            IFluidKinematicsSlice? centerKinematics = null;
            kinematicsChunks?.TryGetValue((chunkX, chunkZ), out centerKinematics);

            int maxBufferY = Math.Min(centerFluids.Length >> 8, centerSolids.Length >> 8);
            maxY = Math.Clamp(maxY, 2, Math.Min(StratocraftConstants.ChunkSizeY, maxBufferY));
            int startY = Math.Clamp(minY, 0, maxY - 1);
            bool suppressUndergroundCaveFluids = minY >= 68;
            Span<RichContourVertex> ring = stackalloc RichContourVertex[16];

            for (int y = startY; y < maxY; y++)
            {
                int yOffset = y << 8;

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
                        int cellIdx = rowOffset | lx;
                        ushort rawFluid = centerFluids[cellIdx];
                        byte level = (byte)(rawFluid & 0xFFu);
                        byte fType = (byte)((rawFluid >> 8) & 0x03u);
                        if (level == 0 || fType == 0)
                        {
                            continue;
                        }

                        byte pressureHead = (byte)((rawFluid >> 11) & 0x1Fu);

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

                        byte renderId = ResolveFluidRenderId(fType, chunkX, chunkZ, lx, y, lz, salinityChunkBitsMap);

                        RichCornerResult c00 = EvaluateRichCorner(
                            chunkX, chunkZ, lx, y, lz, 0, 0, fType, hasFreeTopSurface, !hasExposedBottom,
                            maxBufferY, centerSolids, centerFluids, centerKinematics,
                            solidChunks, fluidChunks, kinematicsChunks);
                        RichCornerResult c10 = EvaluateRichCorner(
                            chunkX, chunkZ, lx, y, lz, 1, 0, fType, hasFreeTopSurface, !hasExposedBottom,
                            maxBufferY, centerSolids, centerFluids, centerKinematics,
                            solidChunks, fluidChunks, kinematicsChunks);
                        RichCornerResult c11 = EvaluateRichCorner(
                            chunkX, chunkZ, lx, y, lz, 1, 1, fType, hasFreeTopSurface, !hasExposedBottom,
                            maxBufferY, centerSolids, centerFluids, centerKinematics,
                            solidChunks, fluidChunks, kinematicsChunks);
                        RichCornerResult c01 = EvaluateRichCorner(
                            chunkX, chunkZ, lx, y, lz, 0, 1, fType, hasFreeTopSurface, !hasExposedBottom,
                            maxBufferY, centerSolids, centerFluids, centerKinematics,
                            solidChunks, fluidChunks, kinematicsChunks);

                        RichContourVertex v00 = BuildCornerVertex(lx, lz, in c00, -1, 0);
                        RichContourVertex v10 = BuildCornerVertex(lx + 1, lz, in c10, 0, -1);
                        RichContourVertex v11 = BuildCornerVertex(lx + 1, lz + 1, in c11, 1, 0);
                        RichContourVertex v01 = BuildCornerVertex(lx, lz + 1, in c01, 0, 1);

                        // Puruzsuz yuzey normali: hucre icindeki 8 ucgen diliminin elmas/kup seklinde kirik faset golgesi olusturmasini onler
                        float dYdX = 0.5f * ((v10.TopCurr.Y + v11.TopCurr.Y) - (v00.TopCurr.Y + v01.TopCurr.Y));
                        float dYdZ = 0.5f * ((v01.TopCurr.Y + v11.TopCurr.Y) - (v00.TopCurr.Y + v10.TopCurr.Y));
                        Vector3 smoothCellNormal = Vector3.Normalize(new Vector3(-dYdX, 1.0f, -dYdZ));
                        v00.SmoothTopNormal = smoothCellNormal;
                        v10.SmoothTopNormal = smoothCellNormal;
                        v11.SmoothTopNormal = smoothCellNormal;
                        v01.SmoothTopNormal = smoothCellNormal;

                        bool wetNegX = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx - 1, y, lz, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallNegX);
                        bool wetPosX = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx + 1, y, lz, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallPosX);
                        bool wetNegZ = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx, y, lz - 1, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallNegZ);
                        bool wetPosZ = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx, y, lz + 1, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wallPosZ);

                        bool wet00Diag = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx - 1, y, lz - 1, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall00Diag);
                        bool wet01Diag = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx - 1, y, lz + 1, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall01Diag);
                        bool wet11Diag = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx + 1, y, lz + 1, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall11Diag);
                        bool wet10Diag = HasSlopeConnectedSameFluidAt(chunkX, chunkZ, lx + 1, y, lz - 1, fType, hasFreeTopSurface, maxBufferY, centerSolids, centerFluids, solidChunks, fluidChunks, out bool wall10Diag);

                        bool bevel00 = !wetNegX && !wetNegZ && !wet00Diag && !c00.TouchesSdfShore && !c00.HasSlopeConnection && !wallNegX && !wallNegZ && !wall00Diag;
                        bool bevel01 = !wetNegX && !wetPosZ && !wet01Diag && !c01.TouchesSdfShore && !c01.HasSlopeConnection && !wallNegX && !wallPosZ && !wall01Diag;
                        bool bevel11 = !wetPosX && !wetPosZ && !wet11Diag && !c11.TouchesSdfShore && !c11.HasSlopeConnection && !wallPosX && !wallPosZ && !wall11Diag;
                        bool bevel10 = !wetPosX && !wetNegZ && !wet10Diag && !c10.TouchesSdfShore && !c10.HasSlopeConnection && !wallPosX && !wallNegZ && !wall10Diag;

                        RichContourVertex midNegX = LerpRichContourVertex(in v00, in v01, 0.5f, -1, 0);
                        RichContourVertex midPosZ = LerpRichContourVertex(in v01, in v11, 0.5f, 0, 1);
                        RichContourVertex midPosX = LerpRichContourVertex(in v11, in v10, 0.5f, 1, 0);
                        RichContourVertex midNegZ = LerpRichContourVertex(in v10, in v00, 0.5f, 0, -1);

                        // Merkez ornegi: savak dususunde Rouse orani [0.65..0.78] korunacak sekilde hucre kendi kalinligini yansitir
                        uint selfSolid = centerSolids[cellIdx];
                        byte selfCap = ComputeSdfPorousCapacity(selfSolid);
                        float selfBedY = !hasExposedBottom
                            ? y
                            : (y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolid)
                                ? ComputeColumnSurfaceNetsBedMeters(belowSolid, selfSolid, y)
                                : y);
                        float selfCurrThick = MassToFluidThicknessWithCapacity(level, selfCap);
                        byte selfPrevMass = (centerKinematics != null &&
                                             centerKinematics.IsInitializedFromCurrentMass &&
                                             (uint)cellIdx < (uint)centerKinematics.PrevMass.Length)
                            ? centerKinematics.PrevMass[cellIdx]
                            : level;
                        float selfPrevThick = MassToFluidThicknessWithCapacity(selfPrevMass, selfCap);
                        bool cellHasOverfall = c00.HasOverfall || c10.HasOverfall || c11.HasOverfall || c01.HasOverfall;
                        bool cellHasSlopeConn = c00.HasSlopeConnection || c10.HasSlopeConnection || c11.HasSlopeConnection || c01.HasSlopeConnection;

                        float avgCornerCurrY = 0.25f * (v00.TopCurr.Y + v10.TopCurr.Y + v11.TopCurr.Y + v01.TopCurr.Y);
                        float avgCornerPrevY = 0.25f * (c00.TopPrevY + c10.TopPrevY + c11.TopPrevY + c01.TopPrevY);
                        float centerCurrY = hasFreeTopSurface && cellHasOverfall && !cellHasSlopeConn
                            ? MathF.Min(y + 0.995f, MathF.Max(avgCornerCurrY, selfBedY + selfCurrThick))
                            : avgCornerCurrY;
                        float centerPrevY = hasFreeTopSurface && cellHasOverfall && !cellHasSlopeConn
                            ? MathF.Min(y + 0.995f, MathF.Max(avgCornerPrevY, selfBedY + selfPrevThick))
                            : avgCornerPrevY;

                        Vector3 pCenter = new Vector3(
                            0.25f * (v00.TopCurr.X + v10.TopCurr.X + v11.TopCurr.X + v01.TopCurr.X),
                            centerCurrY,
                            0.25f * (v00.TopCurr.Z + v10.TopCurr.Z + v11.TopCurr.Z + v01.TopCurr.Z));
                        Vector3 bCenter = 0.25f * (v00.Bottom + v10.Bottom + v11.Bottom + v01.Bottom);

                        float selfVelX = 0f;
                        float selfVelZ = 0f;
                        if (centerKinematics != null &&
                            centerKinematics.IsInitializedFromCurrentMass &&
                            (uint)cellIdx < (uint)centerKinematics.VelX_Q7.Length)
                        {
                            selfVelX = centerKinematics.VelX_Q7[cellIdx] * (MaxKinematicsVelocityMps / 127.0f);
                            selfVelZ = centerKinematics.VelZ_Q7[cellIdx] * (MaxKinematicsVelocityMps / 127.0f);
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

                        if (bevel00)
                        {
                            RichContourVertex arc00 = LerpRichContourVertex(in centerTopSample, LerpRichContourVertex(in midNegZ, in midNegX, 0.5f, 0, 0), 0.90f, 0, 0);
                            ring[ringCount++] = arc00;
                            ring[ringCount++] = midNegX;
                        }
                        else
                        {
                            ring[ringCount++] = v00;
                            ring[ringCount++] = midNegX;
                        }

                        if (bevel01)
                        {
                            RichContourVertex arc01 = LerpRichContourVertex(in centerTopSample, LerpRichContourVertex(in midNegX, in midPosZ, 0.5f, 0, 0), 0.90f, 0, 0);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = arc01;
                            ring[ringCount++] = midPosZ;
                        }
                        else
                        {
                            ring[ringCount++] = v01;
                            ring[ringCount++] = midPosZ;
                        }

                        if (bevel11)
                        {
                            RichContourVertex arc11 = LerpRichContourVertex(in centerTopSample, LerpRichContourVertex(in midPosZ, in midPosX, 0.5f, 0, 0), 0.90f, 0, 0);
                            ring[ringCount - 1].EdgeDirX = 0;
                            ring[ringCount - 1].EdgeDirZ = 0;
                            ring[ringCount++] = arc11;
                            ring[ringCount++] = midPosX;
                        }
                        else
                        {
                            ring[ringCount++] = v11;
                            ring[ringCount++] = midPosX;
                        }

                        if (bevel10)
                        {
                            RichContourVertex arc10 = LerpRichContourVertex(in centerTopSample, LerpRichContourVertex(in midPosX, in midNegZ, 0.5f, 0, 0), 0.90f, 0, 0);
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
                            midNegZ.EdgeDirX = 0;
                            midNegZ.EdgeDirZ = bevel00 ? (sbyte)0 : (sbyte)-1;
                            ring[ringCount++] = midNegZ;
                        }

                        for (int i = 0; i < ringCount; i++)
                        {
                            int next = (i + 1 == ringCount) ? 0 : (i + 1);
                            RichContourVertex va = ring[i];
                            RichContourVertex vb = ring[next];

                            if (hasFreeTopSurface)
                            {
                                float topFoam = (cellHasOverfall && !cellHasSlopeConn) ? 0.12f : 0.0f;
                                outRichTriangles.Add(new FluidRichTriangle
                                {
                                    C0 = MakeSample(pCenter, smoothCellNormal, renderId, centerEncDy, centerEncVx, centerEncVz, centerDepth, 0.0f),
                                    C1 = MakeSample(va.TopCurr, va.SmoothTopNormal, renderId, va.EncodedSubTickDy, va.EncodedVelX, va.EncodedVelZ, va.DepthMeters, topFoam),
                                    C2 = MakeSample(vb.TopCurr, vb.SmoothTopNormal, renderId, vb.EncodedSubTickDy, vb.EncodedVelX, vb.EncodedVelZ, vb.DepthMeters, topFoam)
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
                                EmitRichBevelSkirtQuad(in vb, in va, renderId, outRichTriangles);
                            }
                            else
                            {
                                EmitRichSideOrWaterfallCurtain(
                                    chunkX, chunkZ, lx, y, lz, va.EdgeDirX, va.EdgeDirZ, fType, renderId,
                                    pressureHead, hasFreeTopSurface, maxBufferY,
                                    in vb, in va, centerSolids, centerFluids, solidChunks, fluidChunks, outRichTriangles);
                            }
                        }
                    }
                }
            }
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
        /// Belirtilen (sx, sy, sz) hucresinin kati zemin ustunde oturan serbest yuzeyli bir sivi hucresi olup olmadigini denetler.
        /// Bu kosul hem ustten bakan (sy = y - 1) hem alttan bakan (sy = y + 1) yamac hucresi icin %100 simetriktir;
        /// boylece yamacta akan su (y) ve (y - 1) katmanlari arasinda 0.000m dikisle puruzsuz egimli carsaf olusturur.
        /// </summary>
        private static bool TrySampleSlopeGroundedFluid(
            int centerCx,
            int centerCz,
            int sx,
            int sy,
            int sz,
            byte fluidType,
            int maxBufferY,
            uint[] centerSolids,
            ushort[] centerFluids,
            IFluidKinematicsSlice? centerKinematics,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            IReadOnlyDictionary<(int cx, int cz), IFluidKinematicsSlice>? kinematicsChunks,
            out float outBedY,
            out float outCurrThick,
            out float outPrevThick,
            out float outCurrSurfY,
            out float outPrevSurfY,
            out float outVx,
            out float outVz)
        {
            outBedY = sy;
            outCurrThick = 0f;
            outPrevThick = 0f;
            outCurrSurfY = sy;
            outPrevSurfY = sy;
            outVx = 0f;
            outVz = 0f;

            if (sy < 0 || sy >= maxBufferY)
            {
                return false;
            }

            SampleRichCell(
                centerCx, centerCz, sx, sy, sz,
                centerSolids, centerFluids, centerKinematics,
                solidChunks, fluidChunks, kinematicsChunks,
                out uint sRaw, out ushort fRaw, out byte pMass, out float vx, out float vz);

            if ((fRaw & 0xFFu) == 0u || ((fRaw >> 8) & 0x03u) != fluidType || VoxelBitUtility.IsPhysicallySolid(sRaw))
            {
                return false;
            }

            // Ustunde ayni sivi varsa bu gomulu bir sutundur, serbest yamac yuzeyi degildir
            if (sy + 1 < maxBufferY)
            {
                SampleCell(centerCx, centerCz, sx, sy + 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out _, out ushort aboveFRaw);
                if ((aboveFRaw & 0xFFu) > 0u && ((aboveFRaw >> 8) & 0x03u) == fluidType)
                {
                    return false;
                }
            }

            // Altinda kati zemin destegi olmalidir (serbest dusen selale sutunu degil, yamac akisi!)
            uint belowSRaw = 0u;
            if (sy > 0)
            {
                SampleCell(centerCx, centerCz, sx, sy - 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out belowSRaw, out _);
                if (!VoxelBitUtility.IsPhysicallySolid(belowSRaw))
                {
                    return false;
                }
            }

            float bedY = sy > 0 ? ComputeColumnSurfaceNetsBedMeters(belowSRaw, sRaw, sy) : 0f;
            byte cellMass = (byte)(fRaw & 0xFFu);
            byte cap = ComputeSdfPorousCapacity(sRaw);
            float currThick = MassToFluidThicknessWithCapacity(cellMass, cap);
            float prevThick = MassToFluidThicknessWithCapacity(pMass, cap);

            outBedY = bedY;
            outCurrThick = currThick;
            outPrevThick = prevThick;
            outCurrSurfY = Math.Min(sy + 0.995f, bedY + currThick);
            outPrevSurfY = Math.Min(sy + 0.995f, bedY + prevThick);
            outVx = vx;
            outVz = vz;
            return true;
        }

        private static bool HasSlopeConnectedSameFluidAt(
            int centerCx,
            int centerCz,
            int nx,
            int y,
            int nz,
            byte fluidType,
            bool selfHasFreeTopSurface,
            int maxBufferY,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            out bool touchesWall)
        {
            if (HasSameFluidAt(centerCx, centerCz, nx, y, nz, fluidType, centerSolids, centerFluids, solidChunks, fluidChunks, out touchesWall))
            {
                return true;
            }

            if (!selfHasFreeTopSurface)
            {
                return false;
            }

            SampleCell(centerCx, centerCz, nx, y, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nSolid, out _);
            bool isSolid = VoxelBitUtility.IsPhysicallySolid(nSolid);

            // Yamac asagi (y - 1) zemin-ustu sivi baglantisi
            if (!isSolid && y > 0 && TrySampleSlopeGroundedFluid(
                    centerCx, centerCz, nx, y - 1, nz, fluidType, maxBufferY,
                    centerSolids, centerFluids, null, solidChunks, fluidChunks, null,
                    out _, out _, out _, out _, out _, out _, out _))
            {
                touchesWall = false;
                return true;
            }

            // Yamac yukari (y + 1) zemin-ustu sivi baglantisi
            if (isSolid && y + 1 < maxBufferY && TrySampleSlopeGroundedFluid(
                    centerCx, centerCz, nx, y + 1, nz, fluidType, maxBufferY,
                    centerSolids, centerFluids, null, solidChunks, fluidChunks, null,
                    out _, out _, out _, out _, out _, out _, out _))
            {
                touchesWall = false;
                return true;
            }

            return false;
        }

        private static RichCornerResult EvaluateRichCorner(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            int cornerDx,
            int cornerDz,
            byte fluidType,
            bool selfHasFreeTopSurface,
            bool embedFoundationIntoGround,
            int maxBufferY,
            uint[] centerSolids,
            ushort[] centerFluids,
            IFluidKinematicsSlice? centerKinematics,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            IReadOnlyDictionary<(int cx, int cz), IFluidKinematicsSlice>? kinematicsChunks)
        {
            int baseCornerX = lx + cornerDx;
            int baseCornerZ = lz + cornerDz;

            float wetCurrThickSum = 0f;
            float wetPrevThickSum = 0f;
            float wetCurrSurfSum = 0f;
            float wetPrevSurfSum = 0f;
            int wetSamples = 0;
            int submergedStackCount = 0;
            int slopeConnectedCount = 0;
            int shoreAirSamples = 0;
            int overfallSamples = 0;
            bool touchesSdfShore = false;

            float wetBedSum = 0f;
            float minWetBedY = float.MaxValue;
            int wetBedSamples = 0;

            float velXSum = 0f;
            float velZSum = 0f;
            float depthSum = 0f;

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
                    SampleRichCell(
                        centerCx, centerCz, sx, y, sz,
                        centerSolids, centerFluids, centerKinematics,
                        solidChunks, fluidChunks, kinematicsChunks,
                        out uint solidRaw, out ushort fluidRaw, out byte prevMass, out float vx, out float vz);

                    uint belowSolidRaw = 0u;
                    ushort belowFluidRaw = 0;
                    if (y > 0)
                    {
                        SampleCell(centerCx, centerCz, sx, y - 1, sz, centerSolids, centerFluids, solidChunks, fluidChunks, out belowSolidRaw, out belowFluidRaw);
                    }

                    uint aboveSolidRaw = 0u;
                    ushort aboveFluidRaw = 0;
                    byte abovePrevMass = 0;
                    float aboveVx = 0f;
                    float aboveVz = 0f;
                    if (y + 1 < maxBufferY)
                    {
                        SampleRichCell(
                            centerCx, centerCz, sx, y + 1, sz,
                            centerSolids, centerFluids, centerKinematics,
                            solidChunks, fluidChunks, kinematicsChunks,
                            out aboveSolidRaw, out aboveFluidRaw, out abovePrevMass, out aboveVx, out aboveVz);
                    }

                    bool isCurrentCellSolidWall = VoxelBitUtility.IsPhysicallySolid(solidRaw);
                    bool hasBelowSameFluid = y > 0 && (belowFluidRaw & 0xFFu) > 0u && ((belowFluidRaw >> 8) & 0x03u) == fluidType;
                    bool hasCurrentSameFluid = !isCurrentCellSolidWall && (fluidRaw & 0xFFu) > 0u && ((fluidRaw >> 8) & 0x03u) == fluidType;
                    bool hasAboveSameFluid = y + 1 < maxBufferY && !VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw) &&
                                             (aboveFluidRaw & 0xFFu) > 0u && ((aboveFluidRaw >> 8) & 0x03u) == fluidType;

                    float cellBedY = y;
                    if (!hasBelowSameFluid && y > 0 && VoxelBitUtility.IsPhysicallySolid(belowSolidRaw) && !isCurrentCellSolidWall)
                    {
                        cellBedY = ComputeColumnSurfaceNetsBedMeters(belowSolidRaw, solidRaw, y);
                    }

                    if (hasCurrentSameFluid && hasAboveSameFluid)
                    {
                        // Ust uste 2 sivi blogu olan gercek derin gol/selale sutunu
                        submergedStackCount++;
                        wetBedSum += cellBedY;
                        if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                        wetBedSamples++;

                        byte aboveCurrMass = (byte)(aboveFluidRaw & 0xFFu);
                        float aboveCurrT = MassToFluidThicknessWithCapacity(aboveCurrMass, 255);
                        float abovePrevT = MassToFluidThicknessWithCapacity(abovePrevMass, 255);

                        float colCurrThick = MathF.Min(1.0f, 0.88f + 0.12f * aboveCurrT);
                        float colPrevThick = MathF.Min(1.0f, 0.88f + 0.12f * abovePrevT);

                        wetCurrThickSum += colCurrThick;
                        wetPrevThickSum += colPrevThick;
                        wetCurrSurfSum += y + colCurrThick;
                        wetPrevSurfSum += y + colPrevThick;
                        wetSamples++;
                        velXSum += vx;
                        velZSum += vz;

                        int belowColDepth = CountSameFluidBelow(
                            centerCx, centerCz, sx, y, sz, fluidType,
                            centerSolids, centerFluids, solidChunks, fluidChunks);
                        depthSum += Math.Clamp(1.0f + aboveCurrT + belowColDepth, 0.0f, 8.0f);
                    }
                    else if (hasCurrentSameFluid)
                    {
                        wetBedSum += cellBedY;
                        if (cellBedY < minWetBedY) minWetBedY = cellBedY;
                        wetBedSamples++;

                        byte cellMass = (byte)(fluidRaw & 0xFFu);
                        byte cap = ComputeSdfPorousCapacity(solidRaw);
                        float currThick = MassToFluidThicknessWithCapacity(cellMass, cap);
                        float prevThick = MassToFluidThicknessWithCapacity(prevMass, cap);

                        wetCurrThickSum += currThick;
                        wetPrevThickSum += prevThick;
                        wetCurrSurfSum += Math.Min(y + 0.995f, cellBedY + currThick);
                        wetPrevSurfSum += Math.Min(y + 0.995f, cellBedY + prevThick);
                        wetSamples++;
                        velXSum += vx;
                        velZSum += vz;

                        int belowColDepth = CountSameFluidBelow(
                            centerCx, centerCz, sx, y, sz, fluidType,
                            centerSolids, centerFluids, solidChunks, fluidChunks);
                        depthSum += Math.Clamp(currThick + belowColDepth, 0.0f, 8.0f);
                    }
                    else if (selfHasFreeTopSurface && isCurrentCellSolidWall && hasAboveSameFluid &&
                             TrySampleSlopeGroundedFluid(
                                 centerCx, centerCz, sx, y + 1, sz, fluidType, maxBufferY,
                                 centerSolids, centerFluids, centerKinematics,
                                 solidChunks, fluidChunks, kinematicsChunks,
                                 out float upBedY, out float upCurrThick, out float upPrevThick,
                                 out float upCurrSurfY, out float upPrevSurfY, out float upVx, out float upVz))
                    {
                        // Yamac yukari (y + 1) komsu sivi: kati arazi basamaginin ustundeki ince/orta akintiyi puruzsuz birlestir!
                        slopeConnectedCount++;
                        wetBedSum += upBedY;
                        if (upBedY < minWetBedY) minWetBedY = upBedY;
                        wetBedSamples++;

                        wetCurrThickSum += upCurrThick;
                        wetPrevThickSum += upPrevThick;
                        wetCurrSurfSum += upCurrSurfY;
                        wetPrevSurfSum += upPrevSurfY;
                        wetSamples++;
                        velXSum += (MathF.Abs(upVx) > 1e-4f ? upVx : aboveVx);
                        velZSum += (MathF.Abs(upVz) > 1e-4f ? upVz : aboveVz);
                        depthSum += Math.Clamp(upCurrThick, 0.0f, 8.0f);
                    }
                    else if (selfHasFreeTopSurface && !isCurrentCellSolidWall && y > 0 &&
                             TrySampleSlopeGroundedFluid(
                                 centerCx, centerCz, sx, y - 1, sz, fluidType, maxBufferY,
                                 centerSolids, centerFluids, centerKinematics,
                                 solidChunks, fluidChunks, kinematicsChunks,
                                 out float downBedY, out float downCurrThick, out float downPrevThick,
                                 out float downCurrSurfY, out float downPrevSurfY, out float downVx, out float downVz))
                    {
                        // Yamac asagi (y - 1) komsu sivi: ucurum basamagi degil, arazi egimiyle inen kesintisiz akinti!
                        slopeConnectedCount++;
                        wetBedSum += downBedY;
                        if (downBedY < minWetBedY) minWetBedY = downBedY;
                        wetBedSamples++;

                        wetCurrThickSum += downCurrThick;
                        wetPrevThickSum += downPrevThick;
                        wetCurrSurfSum += downCurrSurfY;
                        wetPrevSurfSum += downPrevSurfY;
                        wetSamples++;
                        velXSum += downVx;
                        velZSum += downVz;
                        depthSum += Math.Clamp(downCurrThick, 0.0f, 8.0f);
                    }
                    else if (!isCurrentCellSolidWall)
                    {
                        // Bos hava komsusu: altinda kati zemin varsa sığ kiyi, altinda bosluk/dusus varsa gercek ucurum savagi!
                        if (y > 0 && !VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
                        {
                            overfallSamples++;
                        }
                        else
                        {
                            shoreAirSamples++;
                        }
                    }

                    byte sMat = VoxelBitUtility.GetMaterialId(solidRaw);
                    byte sShape = VoxelBitUtility.GetShapeId(solidRaw);
                    byte sDens = (sMat != 0 && sShape == 0) ? VoxelBitUtility.GetDensityOrLiquid(solidRaw) : (byte)0;
                    if (sDens >= 32 && !hasAboveSameFluid)
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
            float adaptiveEmbed = Math.Clamp(meanRawThick * 0.65f, 0.015f, FoundationEmbedDepthMeters);
            float outCornerBedY = embedFoundationIntoGround
                ? Math.Max(0f, baseMinBedY - adaptiveEmbed)
                : avgWetBedY;

            // Yamac ic birlesim dikisinde (slopeConnectedCount > 0) iki komsu hucre 0.000m ayni XZ kosesini paylasmalidir;
            // kiyi etegi yalnizca gercek dis kiyi sinirinda ve sivi kalinligiyla orantili uygulanir!
            Vector3 skirtOffset = Vector3.Zero;
            if (touchesSdfShore && slopeConnectedCount == 0 && wetSamples < 4)
            {
                float adaptiveSkirtExt = Math.Clamp(meanRawThick * 0.90f, 0.08f, ShorelineSkirtExtensionMeters);
                Vector3 gradXZ = new Vector3(sdfPosX - sdfNegX, 0f, sdfPosZ - sdfNegZ);
                float lenSq = gradXZ.LengthSquared();
                if (lenSq > 1e-4f)
                {
                    skirtOffset = (gradXZ / MathF.Sqrt(lenSq)) * adaptiveSkirtExt;
                }
                else
                {
                    Vector3 outward = Vector3.Normalize(new Vector3(cornerDx == 0 ? -1f : 1f, 0f, cornerDz == 0 ? -1f : 1f));
                    skirtOffset = outward * adaptiveSkirtExt;
                }
            }

            float avgVelX = wetSamples > 0 ? (velXSum / wetSamples) : 0f;
            float avgVelZ = wetSamples > 0 ? (velZSum / wetSamples) : 0f;
            float avgDepth = wetSamples > 0 ? (depthSum / wetSamples) : MinVisibleFilmThickness;

            // Kendi hucresinin ustunde ayni sivi varsa (gomulu/selale sutunu), ust katman tabani y+1.0f ile 0.000m dikis sagla
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
                    HasSlopeConnection = false
                };
            }

            float avgCurrSurfY;
            float avgPrevSurfY;
            if (wetSamples > 0)
            {
                float avgCurrThick = wetCurrThickSum / wetSamples;
                float avgPrevThick = wetPrevThickSum / wetSamples;
                float avgBedPortion = (wetCurrSurfSum - wetCurrThickSum) / wetSamples;

                if (overfallSamples > 0 && slopeConnectedCount == 0)
                {
                    // Rouse savak cokmesi: [0.65 .. 0.78] oraninda disbukey bukulme (gercek ucurum/selale agzi)
                    float brinkFactor = overfallSamples == 1
                        ? 0.76f
                        : (overfallSamples == 2 ? WeirBrinkDepthRatio : 0.66f);
                    avgCurrThick *= brinkFactor;
                    avgPrevThick *= brinkFactor;
                }
                else if (shoreAirSamples > 0 && slopeConnectedCount == 0)
                {
                    // Sifir-kenar kiyi inceltmesi (Shore Feathering):
                    // Suyun kuru zemine degdigi tum dis kenarlar kup gibi dik kesilmez, mercek gibi zemine incelir!
                    if (wetSamples == 1)
                    {
                        avgCurrThick = MathF.Min(0.055f, MathF.Max(0.010f, avgCurrThick * 0.28f));
                        avgPrevThick = avgPrevThick > 0.0f
                            ? MathF.Min(0.055f, MathF.Max(0.010f, avgPrevThick * 0.28f))
                            : 0.0f;
                    }
                    else if (wetSamples == 2)
                    {
                        avgCurrThick *= 0.46f;
                        avgPrevThick *= 0.46f;
                    }
                    else if (wetSamples == 3)
                    {
                        avgCurrThick *= 0.74f;
                        avgPrevThick *= 0.74f;
                    }
                }

                avgCurrSurfY = avgBedPortion + avgCurrThick;
                avgPrevSurfY = avgBedPortion + avgPrevThick;
            }
            else
            {
                avgCurrSurfY = avgWetBedY + MinVisibleFilmThickness;
                avgPrevSurfY = avgCurrSurfY;
            }

            if (y + 1 < maxBufferY)
            {
                SampleCell(centerCx, centerCz, lx, y + 1, lz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint selfAboveSolid, out _);
                if (VoxelBitUtility.IsPhysicallySolid(selfAboveSolid))
                {
                    avgCurrSurfY = Math.Min(y + 0.96f, avgCurrSurfY);
                    avgPrevSurfY = Math.Min(y + 0.96f, avgPrevSurfY);
                }
            }

            // Yamac yukari komsuyla birlesen koselerin ust basamak seviyesine (y + 2.0f'ye kadar) cikmasina izin ver!
            float maxAllowedCornerY = slopeConnectedCount > 0 ? (y + 2.0f) : (y + 1.0f);
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
                HasSlopeConnection = slopeConnectedCount > 0
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
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, norm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.0f),
                C1 = MakeSample(v0.TopCurr, norm, renderId, v0.EncodedSubTickDy, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.0f),
                C2 = MakeSample(v1.TopCurr, norm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.0f)
            });
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, norm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, 0.0f),
                C1 = MakeSample(v1.TopCurr, norm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.0f),
                C2 = MakeSample(v1.Bottom, norm, renderId, 128, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, 0.0f)
            });
        }

        private static void EmitRichSideOrWaterfallCurtain(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            int dirX,
            int dirZ,
            byte fluidType,
            byte renderId,
            byte pressureHead,
            bool selfHasFreeTopSurface,
            int maxBufferY,
            in RichContourVertex v0,
            in RichContourVertex v1,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            List<FluidRichTriangle> outRichTriangles)
        {
            int nx = lx + dirX;
            int nz = lz + dirZ;
            SampleCell(centerCx, centerCz, nx, y, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint nSolid, out ushort nFluid);

            if ((nFluid & 0xFFu) > 0u && ((nFluid >> 8) & 0x03u) == fluidType)
            {
                return;
            }

            bool neighborIsSolid = VoxelBitUtility.IsPhysicallySolid(nSolid);
            if (neighborIsSolid && VoxelBitUtility.GetShapeId(nSolid) != 0)
            {
                return;
            }

            // Yamac birlestirme: komsu sutunda (y - 1) veya (y + 1) zemin-ustu sivi varsa ust yuzeyler koselerde zaten 0.000m dikisle birlesmistir;
            // araya kup/basamak gorunumu yaratan ic dik duvar veya kisa perde ORME!
            if (selfHasFreeTopSurface)
            {
                if (!neighborIsSolid && y > 0 && TrySampleSlopeGroundedFluid(
                        centerCx, centerCz, nx, y - 1, nz, fluidType, maxBufferY,
                        centerSolids, centerFluids, null, solidChunks, fluidChunks, null,
                        out _, out _, out _, out _, out _, out _, out _))
                {
                    return;
                }

                if (neighborIsSolid && y + 1 < maxBufferY && TrySampleSlopeGroundedFluid(
                        centerCx, centerCz, nx, y + 1, nz, fluidType, maxBufferY,
                        centerSolids, centerFluids, null, solidChunks, fluidChunks, null,
                        out _, out _, out _, out _, out _, out _, out _))
                {
                    return;
                }
            }

            // Komsuda tam kati SDF arazi blogu varsa ve sığ akintiysa, opak kayanin icine gomulu dik sivi duvari orme
            if (neighborIsSolid && v0.DepthMeters < 0.35f && v1.DepthMeters < 0.35f)
            {
                return;
            }

            // Komsuda kati duvar yoksa ve altinda gercek ucurum boslugu / dusus varsa: kesintisiz parabolik selale perdesi uret!
            bool neighborIsOpenAir = !neighborIsSolid && ((nFluid & 0xFFu) == 0u || ((nFluid >> 8) & 0x03u) != fluidType);
            uint neighborBelowSolid = 0u;
            ushort neighborBelowFluid = 0;
            if (y > 0)
            {
                SampleCell(centerCx, centerCz, nx, y - 1, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out neighborBelowSolid, out neighborBelowFluid);
            }

            bool isOverfallDrop = neighborIsOpenAir && y > 0 && !VoxelBitUtility.IsPhysicallySolid(neighborBelowSolid);
            if (isOverfallDrop && selfHasFreeTopSurface)
            {
                float meanEdgeDepth = 0.5f * (v0.DepthMeters + v1.DepthMeters);
                if (meanEdgeDepth < 0.045f)
                {
                    return;
                }

                // Komsu sutunda dusen siviya veya tabana kadar kesintisiz baglanti yuksekligini bul
                float targetBotY = y - 1.0f;
                bool hitLowerFluid = false;
                if ((neighborBelowFluid & 0xFFu) > 0u && ((neighborBelowFluid >> 8) & 0x03u) == fluidType)
                {
                    byte belowMass = (byte)(neighborBelowFluid & 0xFFu);
                    float belowSurfY = (y - 1) + MassToFluidThickness(belowMass) * 0.92f;
                    targetBotY = MathF.Min(y - 0.05f, belowSurfY);
                    hitLowerFluid = true;
                }
                else
                {
                    int minScanY = Math.Max(0, y - 16);
                    for (int sy = y - 2; sy >= minScanY; sy--)
                    {
                        SampleCell(centerCx, centerCz, nx, sy, nz, centerSolids, centerFluids, solidChunks, fluidChunks, out uint scanSolid, out ushort scanFluid);
                        if ((scanFluid & 0xFFu) > 0u && ((scanFluid >> 8) & 0x03u) == fluidType)
                        {
                            byte scanMass = (byte)(scanFluid & 0xFFu);
                            targetBotY = sy + MassToFluidThickness(scanMass) * 0.92f;
                            hitLowerFluid = true;
                            break;
                        }
                        if (VoxelBitUtility.IsPhysicallySolid(scanSolid))
                        {
                            targetBotY = ComputeColumnSurfaceNetsBedMeters(scanSolid, 0u, sy + 1);
                            break;
                        }
                        targetBotY = sy;
                    }
                }

                float foamScale = Math.Clamp((meanEdgeDepth - 0.12f) / 0.28f, 0.0f, 1.0f);
                float botPinch = (1.0f - Math.Clamp((meanEdgeDepth - 0.05f) / 0.25f, 0.0f, 1.0f)) * 0.28f;

                Vector3 outDir = new Vector3(dirX, 0f, dirZ);
                float jetOut = Math.Clamp(0.06f * MathF.Sqrt(Math.Max(1, (int)pressureHead)), 0.08f, 0.18f) *
                               Math.Clamp(meanEdgeDepth * 2.5f, 0.35f, 1.0f);

                // Ust kenar birebir ust savak koseleri (v0.TopCurr, v1.TopCurr) ile 0.000m dikis paylasir!
                Vector3 top0 = v0.TopCurr;
                Vector3 top1 = v1.TopCurr;
                Vector3 edgeSpan = top1 - top0;
                Vector3 pinched0 = top0 + edgeSpan * botPinch;
                Vector3 pinched1 = top1 - edgeSpan * botPinch;
                Vector3 bot0 = new Vector3(pinched0.X + outDir.X * jetOut, MathF.Min(top0.Y - 0.02f, targetBotY), pinched0.Z + outDir.Z * jetOut);
                Vector3 bot1 = new Vector3(pinched1.X + outDir.X * jetOut, MathF.Min(top1.Y - 0.02f, targetBotY), pinched1.Z + outDir.Z * jetOut);

                // Parabolik orta egim noktasi: x(0.5) = sqrt(0.5) * jetOut ≈ 0.707 * jetOut
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

                Vector3 topNorm = Vector3.Normalize(outDir * 0.50f + Vector3.UnitY * 0.866f);
                Vector3 midNorm = Vector3.Normalize(outDir * 0.92f + Vector3.UnitY * 0.39f);
                Vector3 botNorm = outDir;

                byte flowVx = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(dirX * 0.75f, -1.0f, 1.0f));
                byte flowVz = FluidCornerSample.EncodeSignedUnitToByte(Math.Clamp(dirZ * 0.75f, -1.0f, 1.0f));
                float topFoam = 0.32f * foamScale;
                float midFoam = 0.65f * foamScale;
                float botFoam = (hitLowerFluid ? 0.95f : 0.85f) * foamScale;

                // Ust perde segmenti (top0, top1 -> mid0, mid1) — ust savakla < 1e-4f kesintisiz!
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

                // Alt perde segmenti (mid0, mid1 -> bot0, bot1)
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

            if (v0.TopCurr.Y <= v0.Bottom.Y + 0.005f && v1.TopCurr.Y <= v1.Bottom.Y + 0.005f)
            {
                return;
            }

            float wallFoam = !selfHasFreeTopSurface && neighborIsOpenAir ? 0.80f : 0.0f;
            Vector3 wallNorm = SafeCrossNormalize(v0.TopCurr - v0.Bottom, v1.TopCurr - v0.Bottom, new Vector3(dirX, 0f, dirZ));
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, wallNorm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, wallFoam),
                C1 = MakeSample(v0.TopCurr, wallNorm, renderId, v0.EncodedSubTickDy, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, wallFoam),
                C2 = MakeSample(v1.TopCurr, wallNorm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, wallFoam)
            });
            outRichTriangles.Add(new FluidRichTriangle
            {
                C0 = MakeSample(v0.Bottom, wallNorm, renderId, 128, v0.EncodedVelX, v0.EncodedVelZ, v0.DepthMeters, wallFoam),
                C1 = MakeSample(v1.TopCurr, wallNorm, renderId, v1.EncodedSubTickDy, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, wallFoam),
                C2 = MakeSample(v1.Bottom, wallNorm, renderId, 128, v1.EncodedVelX, v1.EncodedVelZ, v1.DepthMeters, wallFoam)
            });
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

        private static int CountSameFluidBelow(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            byte fluidType,
            uint[] centerSolids,
            ushort[] centerFluids,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks)
        {
            int count = 0;
            int minY = Math.Max(0, y - 7);
            for (int sy = y - 1; sy >= minY; sy--)
            {
                SampleCell(centerCx, centerCz, lx, sy, lz, centerSolids, centerFluids, solidChunks, fluidChunks, out _, out ushort fRaw);
                if ((fRaw & 0xFFu) > 0u && ((fRaw >> 8) & 0x03u) == fluidType)
                {
                    count++;
                }
                else
                {
                    break;
                }
            }
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SampleRichCell(
            int centerCx,
            int centerCz,
            int lx,
            int y,
            int lz,
            uint[] centerSolids,
            ushort[] centerFluids,
            IFluidKinematicsSlice? centerKinematics,
            IReadOnlyDictionary<(int cx, int cz), uint[]> solidChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            IReadOnlyDictionary<(int cx, int cz), IFluidKinematicsSlice>? kinematicsChunks,
            out uint solidRaw,
            out ushort fluidRaw,
            out byte prevMass,
            out float velX,
            out float velZ)
        {
            if ((uint)lx < 16u && (uint)lz < 16u)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                solidRaw = centerSolids[idx];
                fluidRaw = centerFluids[idx];
                if (centerKinematics != null &&
                    centerKinematics.IsInitializedFromCurrentMass &&
                    (uint)idx < (uint)centerKinematics.PrevMass.Length)
                {
                    prevMass = centerKinematics.PrevMass[idx];
                    velX = centerKinematics.VelX_Q7[idx] * (MaxKinematicsVelocityMps / 127.0f);
                    velZ = centerKinematics.VelZ_Q7[idx] * (MaxKinematicsVelocityMps / 127.0f);
                }
                else
                {
                    prevMass = (byte)(fluidRaw & 0xFFu);
                    velX = 0f;
                    velZ = 0f;
                }
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
                if (kinematicsChunks != null &&
                    kinematicsChunks.TryGetValue((ncx, ncz), out IFluidKinematicsSlice? nk) &&
                    nk != null &&
                    nk.IsInitializedFromCurrentMass &&
                    (uint)nIdx < (uint)nk.PrevMass.Length)
                {
                    prevMass = nk.PrevMass[nIdx];
                    velX = nk.VelX_Q7[nIdx] * (MaxKinematicsVelocityMps / 127.0f);
                    velZ = nk.VelZ_Q7[nIdx] * (MaxKinematicsVelocityMps / 127.0f);
                }
                else
                {
                    prevMass = (byte)(fluidRaw & 0xFFu);
                    velX = 0f;
                    velZ = 0f;
                }
                return;
            }

            if (solidChunks.Count > 4)
            {
                int cIdx = (y << 8) | (Math.Clamp(lz, 0, 15) << 4) | Math.Clamp(lx, 0, 15);
                solidRaw = centerSolids[cIdx];
                fluidRaw = centerFluids[cIdx];
                if (centerKinematics != null &&
                    centerKinematics.IsInitializedFromCurrentMass &&
                    (uint)cIdx < (uint)centerKinematics.PrevMass.Length)
                {
                    prevMass = centerKinematics.PrevMass[cIdx];
                    velX = centerKinematics.VelX_Q7[cIdx] * (MaxKinematicsVelocityMps / 127.0f);
                    velZ = centerKinematics.VelZ_Q7[cIdx] * (MaxKinematicsVelocityMps / 127.0f);
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
