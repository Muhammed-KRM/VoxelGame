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
    /// Her kosede Color32(R=MaterialID, G=Temperature, B=Sharpness/Damage, A=(ao4 &lt;&lt; 4) | wear4) bulunur.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GreedyVoxelQuad
    {
        public byte X;
        public byte Y;
        public byte Z;
        public byte Width;
        public byte Height;
        public byte NormalDir;
        public byte ColorR_MaterialId;
        public byte ColorG_Temperature;
        public byte ColorB_SharpnessOrDamage;
        public byte ColorA_AmbientOcclusion;
    }

    /// <summary>
    /// 16x16x16 (4.096 Voxel = 16 KB, L1 onbellege %100 sigar) Sub-Chunk 6-Eksenli 2B Bitwise Binary Greedy Meshing algoritmasi.
    /// Tum 6 yonde (+X, -X, +Y, -Y, +Z, -Z) 2B greedy dortgen birlestirme yaparak ucgen sayisini %70-90 azaltir.
    /// </summary>
    public static class BinaryGreedyMeshingJob
    {
#if NET5_0_OR_GREATER
        [SkipLocalsInit]
#endif
        public static unsafe int ExecuteSubChunk(
            ReadOnlySpan<uint> chunkVoxels65536,
            int subChunkY,
            Span<GreedyVoxelQuad> outQuads)
        {
            int baseY = subChunkY * StratocraftConstants.SubChunkSize;
            int quadCount = 0;
            int maxQuads = outQuads.Length;

            ushort* colsX = stackalloc ushort[16 * 16]; // (ly, lz) -> X bitleri
            ushort* colsY = stackalloc ushort[16 * 16]; // (lz, lx) -> Y bitleri
            ushort* colsZ = stackalloc ushort[16 * 16]; // (ly, lx) -> Z bitleri

            new Span<ushort>(colsX, 256).Clear();
            new Span<ushort>(colsY, 256).Clear();
            new Span<ushort>(colsZ, 256).Clear();

            bool anySolid = false;

            // 1. Gecis: 16x16x16 Sub-Chunk'i tek geciste 16-bitlik ikili katilik sutunlarina kodla
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
                        if (VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            ushort bitX = (ushort)(1u << lx);
                            rowMaskX |= bitX;
                            colsY[(lz << 4) | lx] |= bitY;
                            colsZ[(ly << 4) | lx] |= bitZ;
                            anySolid = true;
                        }
                    }

                    colsX[(ly << 4) | lz] = rowMaskX;
                }
            }

            if (!anySolid)
            {
                return 0;
            }

            ushort* slicePos = stackalloc ushort[16];
            ushort* sliceNeg = stackalloc ushort[16];

            // 2. Gecis: +Y (dir=2) ve -Y (dir=3) — 2B Greedy (Width=X, Height=Z)
            for (int ly = 0; ly < 16; ly++)
            {
                int worldY = baseY + ly;
                bool hasUpper = worldY + 1 < StratocraftConstants.ChunkSizeY;
                bool hasLower = worldY - 1 >= 0;

                for (int lz = 0; lz < 16; lz++)
                {
                    ushort rowPos = 0;
                    ushort rowNeg = 0;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        ushort col = colsY[(lz << 4) | lx];
                        if (((col >> ly) & 1) == 0)
                        {
                            continue;
                        }

                        bool occPos = ly < 15
                            ? (((col >> (ly + 1)) & 1) != 0)
                            : (hasUpper && VoxelBitUtility.IsPhysicallySolid(chunkVoxels65536[((worldY + 1) << 8) | (lz << 4) | lx]));
                        bool occNeg = ly > 0
                            ? (((col >> (ly - 1)) & 1) != 0)
                            : (hasLower && VoxelBitUtility.IsPhysicallySolid(chunkVoxels65536[((worldY - 1) << 8) | (lz << 4) | lx]));

                        if (!occPos) rowPos |= (ushort)(1u << lx);
                        if (!occNeg) rowNeg |= (ushort)(1u << lx);
                    }
                    slicePos[lz] = rowPos;
                    sliceNeg[lz] = rowNeg;
                }

                quadCount = EmitGreedySliceY(chunkVoxels65536, baseY, ly, normalDir: 2, slicePos, outQuads, quadCount, maxQuads);
                quadCount = EmitGreedySliceY(chunkVoxels65536, baseY, ly, normalDir: 3, sliceNeg, outQuads, quadCount, maxQuads);
            }

            // 3. Gecis: +X (dir=0) ve -X (dir=1) — Tam 2B Greedy (Width=Z, Height=Y)
            for (int lx = 0; lx < 16; lx++)
            {
                for (int ly = 0; ly < 16; ly++)
                {
                    ushort rowPos = 0;
                    ushort rowNeg = 0;
                    for (int lz = 0; lz < 16; lz++)
                    {
                        ushort col = colsX[(ly << 4) | lz];
                        ushort posFaces = (ushort)(col & ~(col >> 1));
                        ushort negFaces = (ushort)(col & ~(col << 1));
                        if (((posFaces >> lx) & 1) != 0) rowPos |= (ushort)(1u << lz);
                        if (((negFaces >> lx) & 1) != 0) rowNeg |= (ushort)(1u << lz);
                    }
                    slicePos[ly] = rowPos;
                    sliceNeg[ly] = rowNeg;
                }

                quadCount = EmitGreedySliceX(chunkVoxels65536, baseY, lx, normalDir: 0, slicePos, outQuads, quadCount, maxQuads);
                quadCount = EmitGreedySliceX(chunkVoxels65536, baseY, lx, normalDir: 1, sliceNeg, outQuads, quadCount, maxQuads);
            }

            // 4. Gecis: +Z (dir=4) ve -Z (dir=5) — Tam 2B Greedy (Width=X, Height=Y)
            for (int lz = 0; lz < 16; lz++)
            {
                for (int ly = 0; ly < 16; ly++)
                {
                    ushort rowPos = 0;
                    ushort rowNeg = 0;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        ushort col = colsZ[(ly << 4) | lx];
                        ushort posFaces = (ushort)(col & ~(col >> 1));
                        ushort negFaces = (ushort)(col & ~(col << 1));
                        if (((posFaces >> lz) & 1) != 0) rowPos |= (ushort)(1u << lx);
                        if (((negFaces >> lz) & 1) != 0) rowNeg |= (ushort)(1u << lx);
                    }
                    slicePos[ly] = rowPos;
                    sliceNeg[ly] = rowNeg;
                }

                quadCount = EmitGreedySliceZ(chunkVoxels65536, baseY, lz, normalDir: 4, slicePos, outQuads, quadCount, maxQuads);
                quadCount = EmitGreedySliceZ(chunkVoxels65536, baseY, lz, normalDir: 5, sliceNeg, outQuads, quadCount, maxQuads);
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
                    int startX = TrailingZeroCount(row);
                    uint seedRaw = chunkVoxels65536[(worldY << 8) | (lz << 4) | startX];
                    byte seedMat = (byte)(seedRaw & 0xFFu);

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

                    outQuads[quadCount++] = CreateQuad((byte)startX, quadY, (byte)lz, (byte)width, (byte)height, normalDir, seedRaw);
                }
            }

            return quadCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int EmitGreedySliceX(
            ReadOnlySpan<uint> chunkVoxels65536,
            int baseY,
            int lx,
            byte normalDir,
            ushort* rowMasksByY16,
            Span<GreedyVoxelQuad> outQuads,
            int quadCount,
            int maxQuads)
        {
            byte quadX = (byte)(normalDir == 0 ? lx + 1 : lx);

            for (int ly = 0; ly < 16; ly++)
            {
                ushort row = rowMasksByY16[ly];
                while (row != 0 && quadCount < maxQuads)
                {
                    int startZ = TrailingZeroCount(row);
                    uint seedRaw = chunkVoxels65536[((baseY + ly) << 8) | (startZ << 4) | lx];
                    byte seedMat = (byte)(seedRaw & 0xFFu);

                    int widthZ = 1;
                    while (startZ + widthZ < 16 && ((row >> (startZ + widthZ)) & 1) != 0)
                    {
                        uint nextRaw = chunkVoxels65536[((baseY + ly) << 8) | ((startZ + widthZ) << 4) | lx];
                        if ((byte)(nextRaw & 0xFFu) != seedMat)
                        {
                            break;
                        }
                        widthZ++;
                    }

                    ushort spanMask = (ushort)(((1u << widthZ) - 1u) << startZ);
                    row &= (ushort)~spanMask;

                    int heightY = 1;
                    while (ly + heightY < 16 && (rowMasksByY16[ly + heightY] & spanMask) == spanMask)
                    {
                        bool allMatch = true;
                        int testWorldY = baseY + ly + heightY;
                        for (int k = 0; k < widthZ; k++)
                        {
                            uint testRaw = chunkVoxels65536[(testWorldY << 8) | ((startZ + k) << 4) | lx];
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

                        rowMasksByY16[ly + heightY] &= (ushort)~spanMask;
                        heightY++;
                    }

                    outQuads[quadCount++] = CreateQuad(quadX, (byte)ly, (byte)startZ, (byte)widthZ, (byte)heightY, normalDir, seedRaw);
                }
            }

            return quadCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int EmitGreedySliceZ(
            ReadOnlySpan<uint> chunkVoxels65536,
            int baseY,
            int lz,
            byte normalDir,
            ushort* rowMasksByY16,
            Span<GreedyVoxelQuad> outQuads,
            int quadCount,
            int maxQuads)
        {
            byte quadZ = (byte)(normalDir == 4 ? lz + 1 : lz);

            for (int ly = 0; ly < 16; ly++)
            {
                ushort row = rowMasksByY16[ly];
                while (row != 0 && quadCount < maxQuads)
                {
                    int startX = TrailingZeroCount(row);
                    uint seedRaw = chunkVoxels65536[((baseY + ly) << 8) | (lz << 4) | startX];
                    byte seedMat = (byte)(seedRaw & 0xFFu);

                    int widthX = 1;
                    while (startX + widthX < 16 && ((row >> (startX + widthX)) & 1) != 0)
                    {
                        uint nextRaw = chunkVoxels65536[((baseY + ly) << 8) | (lz << 4) | (startX + widthX)];
                        if ((byte)(nextRaw & 0xFFu) != seedMat)
                        {
                            break;
                        }
                        widthX++;
                    }

                    ushort spanMask = (ushort)(((1u << widthX) - 1u) << startX);
                    row &= (ushort)~spanMask;

                    int heightY = 1;
                    while (ly + heightY < 16 && (rowMasksByY16[ly + heightY] & spanMask) == spanMask)
                    {
                        bool allMatch = true;
                        int testWorldY = baseY + ly + heightY;
                        for (int k = 0; k < widthX; k++)
                        {
                            uint testRaw = chunkVoxels65536[(testWorldY << 8) | (lz << 4) | (startX + k)];
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

                        rowMasksByY16[ly + heightY] &= (ushort)~spanMask;
                        heightY++;
                    }

                    outQuads[quadCount++] = CreateQuad((byte)startX, (byte)ly, quadZ, (byte)widthX, (byte)heightY, normalDir, seedRaw);
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
            byte wear4 = (byte)(VoxelBitUtility.GetDamageState(rawVoxel) & 0x0Fu);
            const byte ao4 = 0x0F;
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
                ColorB_SharpnessOrDamage = wear4,
                ColorA_AmbientOcclusion = (byte)(((uint)ao4 << 4) | wear4)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TrailingZeroCount(uint value)
        {
            return Stratocraft.Core.Math.BitOps.TrailingZeroCount(value);
        }
    }
}
