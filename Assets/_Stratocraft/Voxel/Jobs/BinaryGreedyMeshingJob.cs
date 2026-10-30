using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// UV-Free StratocraftUberShader ile %100 uyumlu sikistirilmis voxel yuzey dortgeni (Quad) ve Color32 verisi.
    /// Her kosede Color32(R=MaterialID, G=Temperature, B=Sharpness/Damage, A=AmbientOcclusion) bulunur.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GreedyVoxelQuad
    {
        /// <summary>Sub-Chunk icindeki baslangic X koordinati (0..16).</summary>
        public byte X;

        /// <summary>Sub-Chunk icindeki baslangic Y koordinati (0..16).</summary>
        public byte Y;

        /// <summary>Sub-Chunk icindeki baslangic Z koordinati (0..16).</summary>
        public byte Z;

        /// <summary>Yuzeyin birlesmis genisligi (1..16).</summary>
        public byte Width;

        /// <summary>Yuzeyin birlesmis yuksekligi (1..16).</summary>
        public byte Height;

        /// <summary>Yuzey normal yonu (0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z).</summary>
        public byte NormalDir;

        /// <summary>Color32.R: MaterialID (1..255).</summary>
        public byte ColorR_MaterialId;

        /// <summary>Color32.G: Sicaklik / Isil durum.</summary>
        public byte ColorG_Temperature;

        /// <summary>Color32.B: Keskinlik / Hasar durumu.</summary>
        public byte ColorB_SharpnessOrDamage;

        /// <summary>Color32.A: Kose Ambient Occlusion (AO).</summary>
        public byte ColorA_AmbientOcclusion;
    }

    /// <summary>
    /// 16x16x16 (4.096 Voxel = 16 KB, L1 onbellege %100 sigar) Sub-Chunk 64-bit Binary Greedy Meshing algoritmasi.
    /// colMask &amp; ~(colMask &lt;&lt; 1) bit operasyonlari ve tzcnt (TrailingZeroCount) donanim komutu ile
    /// &lt; 0.15 ms hedefinin cok altinda sifir-GC mesh uretir.
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 2.2 ve GELISTIRICI DOKUMANI Bolum 4.13.5.
    /// </summary>
    public static class BinaryGreedyMeshingJob
    {
        /// <summary>
        /// 16x256x16 chunk dizisinin belirtilen Sub-Chunk dilimi (subChunkY = 0..15) icin Binary Greedy Meshing calistirir.
        /// Uretilen dortgenleri (Quad) onceden tahsis edilmis outQuads tamponuna yazar ve toplam quad sayisini doner.
        /// </summary>
        [SkipLocalsInit]
        public static unsafe int ExecuteSubChunk(
            ReadOnlySpan<uint> chunkVoxels65536,
            int subChunkY,
            Span<GreedyVoxelQuad> outQuads)
        {
            int baseY = subChunkY * StratocraftConstants.SubChunkSize;
            int quadCount = 0;
            int maxQuads = outQuads.Length;

            // Yigin (stack) uzerinde sifir-GC gecici bit maskeleri (L1 cache dostu)
            // axisCols[axis][a * 16 + b] -> 16-bitlik katilik sutunu
            ushort* colsX = stackalloc ushort[16 * 16]; // X ekseni boyunca 16-bitlik (Y, Z) sutunlari
            ushort* colsY = stackalloc ushort[16 * 16]; // Y ekseni boyunca 16-bitlik (Z, X) sutunlari
            ushort* colsZ = stackalloc ushort[16 * 16]; // Z ekseni boyunca 16-bitlik (Y, X) sutunlari

            new Span<ushort>(colsX, 256).Clear();
            new Span<ushort>(colsY, 256).Clear();
            new Span<ushort>(colsZ, 256).Clear();

            // 1. Gecis: 16x16x16 Sub-Chunk'i tek geciste 16-bitlik ikili (binary) katilik sutunlarina kodla
            for (int ly = 0; ly < 16; ly++)
            {
                int worldY = baseY + ly;
                int yOffset = worldY * 256;
                ushort bitY = (ushort)(1u << ly);

                for (int lz = 0; lz < 16; lz++)
                {
                    int rowOffset = yOffset + (lz << 4);
                    ushort bitZ = (ushort)(1u << lz);
                    ushort rowMaskX = 0;

                    for (int lx = 0; lx < 16; lx++)
                    {
                        uint raw = chunkVoxels65536[rowOffset + lx];
                        if ((raw & 0xFFu) != 0u)
                        {
                            ushort bitX = (ushort)(1u << lx);
                            rowMaskX |= bitX;
                            colsY[(lz << 4) | lx] |= bitY;
                            colsZ[(ly << 4) | lx] |= bitZ;
                        }
                    }

                    colsX[(ly << 4) | lz] = rowMaskX;
                }
            }

            // 2. Gecis: +Y (Ust: dir=2) ve -Y (Alt: dir=3) yuzeylerini bitmask & tzcnt ile cikar ve Greedy birlestir
            ushort* sliceMasksPos = stackalloc ushort[16];
            ushort* sliceMasksNeg = stackalloc ushort[16];

            // Y ekseni dilimleri (ly = 0..15)
            for (int ly = 0; ly < 16; ly++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    ushort rowPos = 0;
                    ushort rowNeg = 0;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        ushort col = colsY[(lz << 4) | lx];
                        // col & ~(col >> 1) -> +Y bakan acik yuzeyler
                        ushort posFaces = (ushort)(col & ~(col >> 1));
                        // col & ~(col << 1) -> -Y bakan acik yuzeyler
                        ushort negFaces = (ushort)(col & ~(col << 1));

                        if (((posFaces >> ly) & 1) != 0) rowPos |= (ushort)(1u << lx);
                        if (((negFaces >> ly) & 1) != 0) rowNeg |= (ushort)(1u << lx);
                    }
                    sliceMasksPos[lz] = rowPos;
                    sliceMasksNeg[lz] = rowNeg;
                }

                quadCount = EmitGreedySliceY(
                    chunkVoxels65536, baseY, ly, normalDir: 2, sliceMasksPos, outQuads, quadCount, maxQuads);
                quadCount = EmitGreedySliceY(
                    chunkVoxels65536, baseY, ly, normalDir: 3, sliceMasksNeg, outQuads, quadCount, maxQuads);
            }

            // 3. Gecis: +X (dir=0) ve -X (dir=1) yuzeylerini bitmask & tzcnt ile cikar
            for (int ly = 0; ly < 16; ly++)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    ushort col = colsX[(ly << 4) | lz];
                    ushort posFaces = (ushort)(col & ~(col >> 1));
                    ushort negFaces = (ushort)(col & ~(col << 1));

                    while (posFaces != 0 && quadCount < maxQuads)
                    {
                        int lx = BitOperations.TrailingZeroCount(posFaces);
                        posFaces &= (ushort)(posFaces - 1);
                        uint raw = chunkVoxels65536[((baseY + ly) << 8) | (lz << 4) | lx];
                        outQuads[quadCount++] = CreateQuad((byte)(lx + 1), (byte)ly, (byte)lz, 1, 1, 0, raw);
                    }

                    while (negFaces != 0 && quadCount < maxQuads)
                    {
                        int lx = BitOperations.TrailingZeroCount(negFaces);
                        negFaces &= (ushort)(negFaces - 1);
                        uint raw = chunkVoxels65536[((baseY + ly) << 8) | (lz << 4) | lx];
                        outQuads[quadCount++] = CreateQuad((byte)lx, (byte)ly, (byte)lz, 1, 1, 1, raw);
                    }
                }
            }

            // 4. Gecis: +Z (dir=4) ve -Z (dir=5) yuzeylerini bitmask & tzcnt ile cikar
            for (int ly = 0; ly < 16; ly++)
            {
                for (int lx = 0; lx < 16; lx++)
                {
                    ushort col = colsZ[(ly << 4) | lx];
                    ushort posFaces = (ushort)(col & ~(col >> 1));
                    ushort negFaces = (ushort)(col & ~(col << 1));

                    while (posFaces != 0 && quadCount < maxQuads)
                    {
                        int lz = BitOperations.TrailingZeroCount(posFaces);
                        posFaces &= (ushort)(posFaces - 1);
                        uint raw = chunkVoxels65536[((baseY + ly) << 8) | (lz << 4) | lx];
                        outQuads[quadCount++] = CreateQuad((byte)lx, (byte)ly, (byte)(lz + 1), 1, 1, 4, raw);
                    }

                    while (negFaces != 0 && quadCount < maxQuads)
                    {
                        int lz = BitOperations.TrailingZeroCount(negFaces);
                        negFaces &= (ushort)(negFaces - 1);
                        uint raw = chunkVoxels65536[((baseY + ly) << 8) | (lz << 4) | lx];
                        outQuads[quadCount++] = CreateQuad((byte)lx, (byte)ly, (byte)lz, 1, 1, 5, raw);
                    }
                }
            }

            return quadCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int EmitGreedySliceY(
            ReadOnlySpan<uint> chunkVoxels65536,
            int baseY,
            int ly,
            byte normalDir,
            ushort* rowMasks16,
            Span<GreedyVoxelQuad> outQuads,
            int quadCount,
            int maxQuads)
        {
            int worldY = baseY + ly;
            byte quadY = (byte)(normalDir == 2 ? ly + 1 : ly);

            for (int lz = 0; lz < 16; lz++)
            {
                ushort row = rowMasks16[lz];
                while (row != 0 && quadCount < maxQuads)
                {
                    int startX = BitOperations.TrailingZeroCount(row);
                    uint seedRaw = chunkVoxels65536[(worldY << 8) | (lz << 4) | startX];
                    byte seedMat = (byte)(seedRaw & 0xFFu);

                    // X yonunde ayni MaterialID'ye sahip bitisik bitleri genislet
                    int width = 1;
                    while (startX + width < 16 && ((row >> (startX + width)) & 1) != 0)
                    {
                        uint nextRaw = chunkVoxels65536[(worldY << 8) | (lz << 4) | (startX + width)];
                        if ((byte)(nextRaw & 0xFFu) != seedMat)
                        {
                            break;
                        }
                        width++;
                    }

                    ushort spanMask = (ushort)(((1u << width) - 1u) << startX);
                    row &= (ushort)~spanMask;

                    // Z yonunde ayni seridi asagi dogru Greedy birlestir
                    int height = 1;
                    while (lz + height < 16 && (rowMasks16[lz + height] & spanMask) == spanMask)
                    {
                        bool allMatch = true;
                        int testZ = lz + height;
                        for (int k = 0; k < width; k++)
                        {
                            uint testRaw = chunkVoxels65536[(worldY << 8) | (testZ << 4) | (startX + k)];
                            if ((byte)(testRaw & 0xFFu) != seedMat)
                            {
                                allMatch = false;
                                break;
                            }
                        }

                        if (!allMatch)
                        {
                            break;
                        }

                        rowMasks16[testZ] &= (ushort)~spanMask;
                        height++;
                    }

                    outQuads[quadCount++] = CreateQuad(
                        (byte)startX,
                        quadY,
                        (byte)lz,
                        (byte)width,
                        (byte)height,
                        normalDir,
                        seedRaw);
                }
            }

            return quadCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static GreedyVoxelQuad CreateQuad(
            byte x,
            byte y,
            byte z,
            byte width,
            byte height,
            byte normalDir,
            uint rawVoxel)
        {
            return new GreedyVoxelQuad
            {
                X = x,
                Y = y,
                Z = z,
                Width = width,
                Height = height,
                NormalDir = normalDir,
                ColorR_MaterialId = VoxelBitUtility.GetMaterialId(rawVoxel),
                ColorG_Temperature = 20,
                ColorB_SharpnessOrDamage = VoxelBitUtility.GetDamageState(rawVoxel),
                ColorA_AmbientOcclusion = 255
            };
        }
    }
}
