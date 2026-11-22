#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// Olusum Aninda Kemerli ve Jeolojik On-Dengelenmis (Pre-Settled) Magara Duzenleyicisi:
    /// - Hileli dondurma (enableLocalTerzaghiLoosening == false) olmadan tum yeralti magara bosluklarini
    ///   dogal parabolik/bindirmeli kaya kemerine (vaulted/corbeled arch, d^2y/dx^2 &lt; 0) donusturur.
    /// - Malzeme kritik acikligini (W_crit) asan genis salonlara deterministik taban-tavan tasiyici
    ///   dogal Stalagnate kaya sutunlari ekler.
    /// - Ardindan gercek StructuralCollapseSystem (tam fizik + Terzaghi acikken) calistirir; hala gevsek
    ///   kalan tavan bloklari daha chunk olusurken kirilip tabana dogal LooseRubble molozu olarak yerlesir.
    /// - 0 B/kare GC Alloc ([ThreadStatic] tamponlar) ve .NET 10 / Unity 6 ortak derlenebilir.
    /// </summary>
    public static class CaveVaultEquilibriumGenerator
    {
        public const int MaxPreSettlePasses = 8;
        public const int CaveOverburdenMarginBlocks = 6;
        private const int MaxPillarHeightBlocks = 28;
        private const int ChunkSizeXZ = 16;

        [ThreadStatic] private static uint[]? _tlsSolveScratch;
        [ThreadStatic] private static StructuralCollapseSystem? _tlsCollapseSystem;
        [ThreadStatic] private static FallingStructureClusterComponent[]? _tlsClusters;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _tlsSolveScratch = null;
            _tlsCollapseSystem = null;
            _tlsClusters = null;
        }
#endif

        /// <summary>
        /// Yeni uretilen bir chunk icindeki yeralti magaralarini gercek fiziksel dengeye kavusturur:
        /// 1) Genis salonlara deterministik Stalagnate sutunlari yerlestirir.
        /// 2) Duz magara tavanlarini parabolik/bindirmeli kaya kemerine (d^2y/dx^2 &lt; 0) donusturur.
        /// 3) Tam fizik acik (enableLocalTerzaghiLoosening: true) jeolojik on-dengeleme calistirip
        ///    kalan gevsek bloklari tabana LooseRubble molozu olarak oturtur.
        /// </summary>
        public static void StabilizeGeneratedChunkCaves(
            int chunkX,
            int chunkZ,
            Span<uint> chunkVoxels,
            int maxY,
            out int archedPlaced,
            out int preSettledCollapsed)
        {
            archedPlaced = 0;
            preSettledCollapsed = 0;

            int effectiveMaxY = Math.Min(maxY, chunkVoxels.Length >> 8);
            if (effectiveMaxY < StratocraftFloatingIslandWorldGen.DeepMantleFloorY + 6)
            {
                return;
            }

            Span<short> surfaceTopY = stackalloc short[256];
            if (!AnalyzeUndergroundCaveBand(
                    chunkVoxels, ChunkSizeXZ, effectiveMaxY, ChunkSizeXZ,
                    VoxelGridMemoryLayout.YMajorChunk,
                    StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
                    surfaceTopY, out int minCaveY, out int maxCaveCeilingY))
            {
                return;
            }

            // 1A) Genis salonlarda (W > W_crit) deterministik tasiyici dogal Stalagnate kaya sutunlari:
            archedPlaced += PlaceDeterministicSupportPillars(
                chunkX, chunkZ, chunkVoxels, effectiveMaxY, surfaceTopY, minCaveY, maxCaveCeilingY);

            // 1B) Genis duz magara tavanlarini dogal parabolik/bindirmeli kaya kemerine (d^2y/dx^2 < 0) donustur:
            archedPlaced += VaultWideFlatCaveCeilings(
                chunkVoxels, effectiveMaxY, surfaceTopY, minCaveY, maxCaveCeilingY);

            // 2) Gercek Jeolojik On-Dengeleme (StructuralCollapseSystem tam fizik + Terzaghi acikken!):
            Span<byte> totalColFallCount = stackalloc byte[256];
            Span<short> totalColFallMinY = stackalloc short[256];
            Span<byte> passColModified = stackalloc byte[256];
            totalColFallCount.Clear();
            totalColFallMinY.Fill(short.MaxValue);
            var originWorld = new Vector3(chunkX * 16f, 0f, chunkZ * 16f);

            for (int pass = 0; pass < MaxPreSettlePasses; pass++)
            {
                if (pass > 0 &&
                    !AnalyzeUndergroundCaveBand(
                        chunkVoxels, ChunkSizeXZ, effectiveMaxY, ChunkSizeXZ,
                        VoxelGridMemoryLayout.YMajorChunk,
                        StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
                        surfaceTopY, out minCaveY, out maxCaveCeilingY))
                {
                    break;
                }

                int solveMaxY = Math.Min(effectiveMaxY, maxCaveCeilingY + CaveOverburdenMarginBlocks + 2);
                int solveVolume = ChunkSizeXZ * solveMaxY * ChunkSizeXZ;
                EnsureTlsBuffers(solveVolume, out uint[] scratch, out StructuralCollapseSystem collapseSys, out FallingStructureClusterComponent[] clusters);

                var centerPivot = new Vector3(chunkX * 16f + 8f, (minCaveY + maxCaveCeilingY) * 0.5f, chunkZ * 16f + 8f);
                PrepareCaveEquilibriumScratch(
                    chunkVoxels, scratch.AsSpan(0, solveVolume),
                    ChunkSizeXZ, effectiveMaxY, solveMaxY, ChunkSizeXZ,
                    VoxelGridMemoryLayout.YMajorChunk,
                    surfaceTopY, maxCaveCeilingY);

                collapseSys.EvaluateAndFracture(
                    scratch.AsSpan(0, solveVolume),
                    ChunkSizeXZ,
                    solveMaxY,
                    ChunkSizeXZ,
                    supportFloorY: StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
                    worldOriginOffset: originWorld,
                    anchorPivotWorld: centerPivot,
                    outClusters: clusters,
                    out int collapsedCount,
                    layout: VoxelGridMemoryLayout.YMajorChunk,
                    enableLocalTerzaghiLoosening: true);

                if (collapsedCount <= 0)
                {
                    break;
                }

                passColModified.Clear();
                int removedThisPass = 0;

                for (int y = StratocraftFloatingIslandWorldGen.DeepMantleFloorY + 1; y < solveMaxY; y++)
                {
                    int yShift = y << 8;
                    for (int col = 0; col < 256; col++)
                    {
                        int idx = yShift | col;
                        if (scratch[idx] != 0u || chunkVoxels[idx] == 0u)
                        {
                            continue;
                        }

                        uint origRaw = chunkVoxels[idx];
                        if (!CanPreSettleModifyVoxel(origRaw))
                        {
                            continue;
                        }

                        chunkVoxels[idx] = 0u;
                        preSettledCollapsed++;
                        removedThisPass++;
                        passColModified[col] = 1;
                        if (totalColFallCount[col] < 255)
                        {
                            totalColFallCount[col]++;
                        }
                        if (y < totalColFallMinY[col])
                        {
                            totalColFallMinY[col] = (short)y;
                        }
                    }
                }

                if (removedThisPass == 0)
                {
                    break;
                }

                // Havada kalan 1..4 blokluk kirik parcalari bir sonraki fizik gecisinden once temizle
                for (int col = 0; col < 256; col++)
                {
                    if (passColModified[col] != 0)
                    {
                        CleanFloatingCaveSliversInColumn(chunkVoxels, col, Math.Min(effectiveMaxY - 2, surfaceTopY[col] - 2));
                    }
                }
            }

            // 3) On-dengeleme yakinsadiktan sonra kirilan gevsek tavan bloklarini saglam magara tabanina dogal LooseRubble olarak yerlestir:
            for (int col = 0; col < 256; col++)
            {
                if (totalColFallCount[col] > 0)
                {
                    SettleNaturalRubbleOnCaveFloor(chunkVoxels, effectiveMaxY, col, totalColFallMinY[col], totalColFallCount[col]);
                }
            }
        }

        /// <summary>
        /// Verilen chunk (veya yerel izgara) icindeki yeralti magaralarinda tam fizik + Terzaghi acikken
        /// hala dengesiz / gocecek tavan blogu kalip kalmadigini gercek fizik motoruyla dogrular (0 B GC).
        /// </summary>
        public static bool HasUnbalancedCaveCeiling(
            ReadOnlySpan<uint> chunkVoxels,
            int maxY,
            int supportFloorY = StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.YMajorChunk,
            int sizeX = ChunkSizeXZ,
            int sizeZ = ChunkSizeXZ)
        {
            return CountUnbalancedCaveCeilingVoxels(chunkVoxels, maxY, supportFloorY, layout, sizeX, sizeZ) > 0;
        }

        /// <summary>
        /// Tam fizik + Terzaghi acikken yeralti magaralarinda hala dengesiz kalan dogal tavan voksel sayisini doner.
        /// </summary>
        public static int CountUnbalancedCaveCeilingVoxels(
            ReadOnlySpan<uint> chunkVoxels,
            int maxY,
            int supportFloorY = StratocraftFloatingIslandWorldGen.DeepMantleFloorY,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.YMajorChunk,
            int sizeX = ChunkSizeXZ,
            int sizeZ = ChunkSizeXZ)
        {
            int sliceArea = sizeX * sizeZ;
            if (sliceArea <= 0 || chunkVoxels.Length < sliceArea * 4)
            {
                return 0;
            }

            int effectiveMaxY = Math.Min(maxY, chunkVoxels.Length / sliceArea);
            if (effectiveMaxY < supportFloorY + 4)
            {
                return 0;
            }

            Span<short> surfaceTopY = sliceArea <= 1024 ? stackalloc short[sliceArea] : new short[sliceArea];
            if (!AnalyzeUndergroundCaveBand(
                    chunkVoxels, sizeX, effectiveMaxY, sizeZ, layout, supportFloorY,
                    surfaceTopY, out _, out int maxCaveCeilingY))
            {
                return 0;
            }

            int solveMaxY = Math.Min(effectiveMaxY, maxCaveCeilingY + CaveOverburdenMarginBlocks + 2);
            int solveVolume = sizeX * solveMaxY * sizeZ;
            EnsureTlsBuffers(solveVolume, out uint[] scratch, out StructuralCollapseSystem collapseSys, out FallingStructureClusterComponent[] clusters);

            PrepareCaveEquilibriumScratch(
                chunkVoxels, scratch.AsSpan(0, solveVolume),
                sizeX, effectiveMaxY, solveMaxY, sizeZ, layout, surfaceTopY, maxCaveCeilingY);

            collapseSys.EvaluateAndFracture(
                scratch.AsSpan(0, solveVolume),
                sizeX,
                solveMaxY,
                sizeZ,
                supportFloorY: supportFloorY,
                worldOriginOffset: Vector3.Zero,
                anchorPivotWorld: new Vector3(sizeX * 0.5f, maxCaveCeilingY * 0.5f, sizeZ * 0.5f),
                outClusters: clusters,
                out int collapsedVoxels,
                layout: layout,
                enableLocalTerzaghiLoosening: true);

            return collapsedVoxels;
        }

        private static bool AnalyzeUndergroundCaveBand(
            ReadOnlySpan<uint> voxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            int supportFloorY,
            Span<short> outSurfaceTopY,
            out int minCaveY,
            out int maxCaveCeilingY)
        {
            minCaveY = int.MaxValue;
            maxCaveCeilingY = -1;
            bool foundAny = false;
            int minScanY = Math.Max(1, supportFloorY + 1);

            for (int lz = 0; lz < sizeZ; lz++)
            {
                for (int lx = 0; lx < sizeX; lx++)
                {
                    int col2D = lz * sizeX + lx;
                    int surfY = -1;

                    // Yuzey agaclarini ve mimari yapilari atlayarak gercek dogal arazi tavanini (en az 2 kati DogalKutle) bul
                    for (int y = sizeY - 1; y >= minScanY; y--)
                    {
                        uint raw = voxels[GeodesicSupportDijkstraJob.GridIndex(lx, y, lz, sizeX, sizeY, sizeZ, layout)];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw) ||
                            VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
                        {
                            continue;
                        }

                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (IsSurfaceWoodOrCanopy(raw, mat))
                        {
                            continue;
                        }

                        if (y > 0)
                        {
                            uint below = voxels[GeodesicSupportDijkstraJob.GridIndex(lx, y - 1, lz, sizeX, sizeY, sizeZ, layout)];
                            if (VoxelBitUtility.IsPhysicallySolid(below) &&
                                VoxelBitUtility.GetOriginState(below) == BlokKokenDurumu.DogalKutle &&
                                !IsSurfaceWoodOrCanopy(below, VoxelBitUtility.GetMaterialId(below)))
                            {
                                surfY = y;
                                break;
                            }
                        }
                        else
                        {
                            surfY = y;
                            break;
                        }
                    }

                    outSurfaceTopY[col2D] = (short)surfY;
                    if (surfY < minScanY + 2)
                    {
                        continue;
                    }

                    bool hasFloor = false;
                    int topCheckY = Math.Min(sizeY - 2, surfY - 1);
                    for (int y = supportFloorY; y <= topCheckY; y++)
                    {
                        uint raw = voxels[GeodesicSupportDijkstraJob.GridIndex(lx, y, lz, sizeX, sizeY, sizeZ, layout)];
                        if (VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            hasFloor = true;
                            continue;
                        }

                        if (!hasFloor || y < minScanY)
                        {
                            continue;
                        }

                        uint above = voxels[GeodesicSupportDijkstraJob.GridIndex(lx, y + 1, lz, sizeX, sizeY, sizeZ, layout)];
                        if (VoxelBitUtility.IsPhysicallySolid(above) &&
                            VoxelBitUtility.GetOriginState(above) == BlokKokenDurumu.DogalKutle)
                        {
                            foundAny = true;
                            if (y < minCaveY) minCaveY = y;
                            if (y + 1 > maxCaveCeilingY) maxCaveCeilingY = y + 1;
                        }
                    }
                }
            }

            return foundAny;
        }

        private static int PlaceDeterministicSupportPillars(
            int chunkX,
            int chunkZ,
            Span<uint> voxels,
            int maxY,
            ReadOnlySpan<short> surfaceTopY,
            int minCaveY,
            int maxCaveCeilingY)
        {
            int placedTotal = 0;
            int topCeilScan = Math.Min(maxY - 2, maxCaveCeilingY);

            for (int y = minCaveY; y <= topCeilScan; y++)
            {
                int yShift = y << 8;
                int aboveShift = (y + 1) << 8;

                // Ayni tavan duzleminde 2 geçiş: ilk sutun 14m salonu ikiye boldukten sonra kalan alt-acikliklari da denetle
                for (int round = 0; round < 2; round++)
                {
                    bool placedInRound = false;

                    for (int lz = 1; lz < 15; lz++)
                    {
                        for (int lx = 1; lx < 15; lx++)
                        {
                            int col = (lz << 4) | lx;
                            if (y >= surfaceTopY[col] - 2)
                            {
                                continue;
                            }

                            if (VoxelBitUtility.IsPhysicallySolid(voxels[yShift | col]))
                            {
                                continue;
                            }

                            uint roofRaw = voxels[aboveShift | col];
                            if (!VoxelBitUtility.IsPhysicallySolid(roofRaw) ||
                                VoxelBitUtility.GetOriginState(roofRaw) != BlokKokenDurumu.DogalKutle)
                            {
                                continue;
                            }

                            StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(VoxelBitUtility.GetMaterialId(roofRaw));
                            int wCrit = Math.Max(2, props.MaxCantileverSpanBlocks);
                            int archMax = Math.Max(wCrit + 2, props.MaxClosedArchArmBlocks);

                            MeasureOpenCeilingSpanX(voxels, y, lz, lx, out int x0, out int x1);
                            int spanX = x1 - x0 + 1;
                            MeasureOpenCeilingSpanZ(voxels, y, lx, lz, out int z0, out int z1);
                            int spanZ = z1 - z0 + 1;

                            // Genis 2B salon (her iki eksende de kritik acikligi asan) veya tek eksende kemer limitini asan tavan:
                            bool needsPillar = (spanX > wCrit + 2 && spanZ > wCrit + 2) ||
                                               (spanX > archMax && spanZ >= 3) ||
                                               (spanZ > archMax && spanX >= 3);
                            if (!needsPillar)
                            {
                                continue;
                            }

                            // Deterministik ancak organik gorunumlu sutun merkezi secimi (salon ortasina yakin)
                            int midX = (x0 + x1) >> 1;
                            int midZ = (z0 + z1) >> 1;
                            uint h = HashPcg(chunkX * 16 + midX, y, chunkZ * 16 + midZ);
                            int jitterX = spanX >= 9 ? ((int)(h & 3u) % 3) - 1 : 0;
                            int jitterZ = spanZ >= 9 ? ((int)((h >> 2) & 3u) % 3) - 1 : 0;
                            int minPx = Math.Max(1, x0 + (spanX >= 5 ? 2 : 1));
                            int maxPx = Math.Min(14, x1 - (spanX >= 5 ? 2 : 1));
                            if (minPx > maxPx) { minPx = Math.Clamp(midX, 1, 14); maxPx = minPx; }
                            int minPz = Math.Max(1, z0 + (spanZ >= 5 ? 2 : 1));
                            int maxPz = Math.Min(14, z1 - (spanZ >= 5 ? 2 : 1));
                            if (minPz > maxPz) { minPz = Math.Clamp(midZ, 1, 14); maxPz = minPz; }
                            int px = Math.Clamp(midX + jitterX, minPx, maxPx);
                            int pz = Math.Clamp(midZ + jitterZ, minPz, maxPz);

                            if (TryStampGroundedStalagnatePillar(voxels, maxY, px, y, pz, surfaceTopY[(pz << 4) | px], out int count))
                            {
                                placedTotal += count;
                                placedInRound = true;
                            }
                        }
                    }

                    if (!placedInRound)
                    {
                        break;
                    }
                }
            }

            return placedTotal;
        }

        private static bool TryStampGroundedStalagnatePillar(
            Span<uint> voxels,
            int maxY,
            int lx,
            int cavityTopY,
            int lz,
            int colSurfaceY,
            out int placedCount)
        {
            placedCount = 0;
            if (cavityTopY >= colSurfaceY - 2 || cavityTopY + 1 >= maxY)
            {
                return false;
            }

            int col = (lz << 4) | lx;
            if (VoxelBitUtility.IsPhysicallySolid(voxels[(cavityTopY << 8) | col]))
            {
                return false;
            }

            uint roofRaw = voxels[((cavityTopY + 1) << 8) | col];
            if (!VoxelBitUtility.IsPhysicallySolid(roofRaw) ||
                VoxelBitUtility.GetOriginState(roofRaw) != BlokKokenDurumu.DogalKutle)
            {
                return false;
            }

            int minFloorSearch = Math.Max(StratocraftFloatingIslandWorldGen.DeepMantleFloorY, cavityTopY - MaxPillarHeightBlocks);
            int floorY = -1;
            for (int fy = cavityTopY - 1; fy >= minFloorSearch; fy--)
            {
                uint belowRaw = voxels[(fy << 8) | col];
                if (VoxelBitUtility.IsPhysicallySolid(belowRaw))
                {
                    floorY = fy;
                    break;
                }
            }

            int cavityHeight = cavityTopY - floorY;
            if (floorY < StratocraftFloatingIslandWorldGen.DeepMantleFloorY || cavityHeight < 2 || cavityHeight > MaxPillarHeightBlocks)
            {
                return false;
            }

            // Tabandan tavana kesintisiz dogal Stalagnate kaya sutunu (0 havada sutun!)
            uint stalagnateRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stalagnate, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int py = floorY + 1; py <= cavityTopY; py++)
            {
                int pIdx = (py << 8) | col;
                if (!VoxelBitUtility.IsPhysicallySolid(voxels[pIdx]))
                {
                    voxels[pIdx] = stalagnateRaw;
                    placedCount++;
                }
            }

            // Yukseklik >= 4 blok ise sutun tepesinde dogal mantar/kemer basligi (corbel capital) olustur
            if (cavityHeight >= 4)
            {
                ReadOnlySpan<int> dX = stackalloc int[4] { -1, 1, 0, 0 };
                ReadOnlySpan<int> dZ = stackalloc int[4] { 0, 0, -1, 1 };
                for (int d = 0; d < 4; d++)
                {
                    int nx = lx + dX[d];
                    int nz = lz + dZ[d];
                    if ((uint)nx >= 16u || (uint)nz >= 16u) continue;
                    int nCol = (nz << 4) | nx;
                    int capIdx = (cavityTopY << 8) | nCol;
                    int capRoofIdx = ((cavityTopY + 1) << 8) | nCol;
                    if (!VoxelBitUtility.IsPhysicallySolid(voxels[capIdx]) &&
                        VoxelBitUtility.IsPhysicallySolid(voxels[capRoofIdx]) &&
                        VoxelBitUtility.GetOriginState(voxels[capRoofIdx]) == BlokKokenDurumu.DogalKutle)
                    {
                        voxels[capIdx] = stalagnateRaw;
                        placedCount++;
                    }
                }
            }

            return placedCount > 0;
        }

        private static int VaultWideFlatCaveCeilings(
            Span<uint> voxels,
            int maxY,
            ReadOnlySpan<short> surfaceTopY,
            int minCaveY,
            int maxCaveCeilingY)
        {
            int placedTotal = 0;
            int topScanY = Math.Min(maxY - 2, maxCaveCeilingY);

            // Hem X hem Z eksenlerinde genis duz tavanlara parabolik/bindirmeli kemer omuzlari (d^2y/ds^2 < 0) isle
            for (int y = topScanY; y >= minCaveY; y--)
            {
                for (int lz = 0; lz < 16; lz++)
                {
                    placedTotal += VaultLineSpans(voxels, maxY, surfaceTopY, y, fixedCoord: lz, alongX: true);
                }

                for (int lx = 0; lx < 16; lx++)
                {
                    placedTotal += VaultLineSpans(voxels, maxY, surfaceTopY, y, fixedCoord: lx, alongX: false);
                }
            }

            return placedTotal;
        }

        private static int VaultLineSpans(
            Span<uint> voxels,
            int maxY,
            ReadOnlySpan<short> surfaceTopY,
            int cavityY,
            int fixedCoord,
            bool alongX)
        {
            int placed = 0;
            int spanStart = -1;

            for (int s = 0; s <= 16; s++)
            {
                bool isCaveCeilingAir = false;
                if (s < 16)
                {
                    int lx = alongX ? s : fixedCoord;
                    int lz = alongX ? fixedCoord : s;
                    int col = (lz << 4) | lx;
                    if (cavityY < surfaceTopY[col] - 2 && cavityY + 1 < maxY)
                    {
                        uint curRaw = voxels[(cavityY << 8) | col];
                        uint aboveRaw = voxels[((cavityY + 1) << 8) | col];
                        if (!VoxelBitUtility.IsPhysicallySolid(curRaw) &&
                            VoxelBitUtility.IsPhysicallySolid(aboveRaw) &&
                            VoxelBitUtility.GetOriginState(aboveRaw) == BlokKokenDurumu.DogalKutle)
                        {
                            isCaveCeilingAir = true;
                        }
                    }
                }

                if (isCaveCeilingAir)
                {
                    if (spanStart < 0) spanStart = s;
                }
                else if (spanStart >= 0)
                {
                    int spanWidth = s - spanStart;
                    placed += TryVaultSingleCeilingSpan(voxels, maxY, surfaceTopY, spanStart, spanWidth, cavityY, fixedCoord, alongX);
                    spanStart = -1;
                }
            }

            return placed;
        }

        private static int TryVaultSingleCeilingSpan(
            Span<uint> voxels,
            int maxY,
            ReadOnlySpan<short> surfaceTopY,
            int spanStart,
            int spanWidth,
            int cavityY,
            int fixedCoord,
            bool alongX)
        {
            int midS = spanStart + (spanWidth >> 1);
            int midX = alongX ? midS : fixedCoord;
            int midZ = alongX ? fixedCoord : midS;
            uint midRoofRaw = voxels[((cavityY + 1) << 8) | (midZ << 4) | midX];
            byte roofMat = VoxelBitUtility.GetMaterialId(midRoofRaw);
            StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(roofMat);
            int wCrit = Math.Max(2, props.MaxCantileverSpanBlocks);

            if (spanWidth <= wCrit)
            {
                return 0;
            }

            // Dik eksende zaten dar (<= wCrit) bir tunel kesitiyse ekstra kemerleme gerekmez
            int perpSpan = alongX
                ? MeasureOpenCeilingSpanZLength(voxels, cavityY, midX, midZ)
                : MeasureOpenCeilingSpanXLength(voxels, cavityY, midZ, midX);
            if (perpSpan >= 2 && perpSpan <= wCrit)
            {
                return 0;
            }

            bool hasLeftWall = spanStart > 0 && IsSolidAt(voxels, alongX ? spanStart - 1 : fixedCoord, cavityY, alongX ? fixedCoord : spanStart - 1);
            bool hasRightWall = (spanStart + spanWidth) < 16 && IsSolidAt(voxels, alongX ? spanStart + spanWidth : fixedCoord, cavityY, alongX ? fixedCoord : spanStart + spanWidth);

            // En az bir duvara/sutuna yaslanan genis tavanlarda parabolik bindirme kemer (corbeled arch) olustur
            if (!hasLeftWall && !hasRightWall)
            {
                return 0;
            }

            // Kemer malzemesi: kemerlenebilir yerel kaya (Stone/Basalt/Marble/Stalagnate); gevsek toprak/kum ise yerel kaya kemer halkasi
            byte archMat = props.IsArchCapable ? roofMat : MaterialIdCatalog.Stone;
            uint archRaw = VoxelBitUtility.PackStandard(archMat, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            int minHeadroom = MeasureMinHeadroomInSpan(voxels, spanStart, spanWidth, cavityY, fixedCoord, alongX);
            if (minHeadroom < 2)
            {
                return 0;
            }

            int placed = 0;

            // Yukseklik >= 3 bloksa yan duvarlardan iceriye basamakli parabolik bindirme kemer omuzlari (d^2y/ds^2 < 0) yerlestir
            if (minHeadroom >= 3)
            {
                int targetCrown = Math.Max(2, Math.Min(wCrit, spanWidth - 2));
                int maxSteps = Math.Min(3, Math.Min(minHeadroom - 2, (spanWidth - targetCrown + 1) / 2));
                if (maxSteps < 1) maxSteps = 1;

                if (hasLeftWall)
                {
                    for (int k = 0; k < maxSteps; k++)
                    {
                        int sPos = spanStart + k;
                        if (sPos >= spanStart + spanWidth - 1) break;
                        int lx = alongX ? sPos : fixedCoord;
                        int lz = alongX ? fixedCoord : sPos;
                        int col = (lz << 4) | lx;
                        int colFloor = FindFloorYBelow(voxels, col, cavityY);
                        int depth = maxSteps - k;
                        for (int dy = 0; dy < depth; dy++)
                        {
                            int ty = cavityY - dy;
                            if (ty < colFloor + 3) break;
                            int idx = (ty << 8) | col;
                            if (!VoxelBitUtility.IsPhysicallySolid(voxels[idx]))
                            {
                                voxels[idx] = archRaw;
                                placed++;
                            }
                        }
                    }
                }

                if (hasRightWall)
                {
                    int spanEnd = spanStart + spanWidth - 1;
                    for (int k = 0; k < maxSteps; k++)
                    {
                        int sPos = spanEnd - k;
                        if (sPos <= spanStart) break;
                        int lx = alongX ? sPos : fixedCoord;
                        int lz = alongX ? fixedCoord : sPos;
                        int col = (lz << 4) | lx;
                        int colFloor = FindFloorYBelow(voxels, col, cavityY);
                        int depth = maxSteps - k;
                        for (int dy = 0; dy < depth; dy++)
                        {
                            int ty = cavityY - dy;
                            if (ty < colFloor + 3) break;
                            int idx = (ty << 8) | col;
                            if (!VoxelBitUtility.IsPhysicallySolid(voxels[idx]))
                            {
                                voxels[idx] = archRaw;
                                placed++;
                            }
                        }
                    }
                }
            }
            else if (hasLeftWall && hasRightWall && spanWidth >= 5)
            {
                // Alcak 2m koridorlarda yurunebilir yuksekligi daraltmadan ortadaki taci 1 blok yukari oyarak
                // parabolik kubbe kemeri olustur (yalnizca ustte kalin kaya ortusu varsa!)
                bool safeToCarveCrown = true;
                for (int sPos = spanStart + 1; sPos < spanStart + spanWidth - 1; sPos++)
                {
                    int lx = alongX ? sPos : fixedCoord;
                    int lz = alongX ? fixedCoord : sPos;
                    int col = (lz << 4) | lx;
                    if (cavityY + 5 >= surfaceTopY[col] || cavityY + 3 >= maxY)
                    {
                        safeToCarveCrown = false;
                        break;
                    }
                    uint aboveRaw = voxels[((cavityY + 1) << 8) | col];
                    if (!CanPreSettleModifyVoxel(aboveRaw))
                    {
                        safeToCarveCrown = false;
                        break;
                    }
                }

                if (safeToCarveCrown)
                {
                    for (int sPos = spanStart + 1; sPos < spanStart + spanWidth - 1; sPos++)
                    {
                        int lx = alongX ? sPos : fixedCoord;
                        int lz = alongX ? fixedCoord : sPos;
                        int col = (lz << 4) | lx;
                        voxels[((cavityY + 1) << 8) | col] = 0u;
                        placed++;
                    }
                }
            }

            return placed;
        }

        private static void PrepareCaveEquilibriumScratch(
            ReadOnlySpan<uint> src,
            Span<uint> dst,
            int sizeX,
            int srcSizeY,
            int solveMaxY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            ReadOnlySpan<short> surfaceTopY,
            int maxCaveCeilingY)
        {
            int anchorAboveY = maxCaveCeilingY + CaveOverburdenMarginBlocks;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int col2D = z * sizeX + x;
                    int surfY = surfaceTopY[col2D];
                    int colAnchorY = surfY > 0 ? Math.Min(anchorAboveY, surfY - 1) : anchorAboveY;

                    for (int y = 0; y < solveMaxY; y++)
                    {
                        int srcIdx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, srcSizeY, sizeZ, layout);
                        int dstIdx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, solveMaxY, sizeZ, layout);
                        uint raw = src[srcIdx];
                        if (raw != 0u && VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            BlokKokenDurumu origin = VoxelBitUtility.GetOriginState(raw);
                            if (origin == BlokKokenDurumu.OyuncuItemBlogu ||
                                (y > colAnchorY && origin == BlokKokenDurumu.DogalKutle))
                            {
                                dst[dstIdx] = VoxelBitUtility.WithOriginState(raw, BlokKokenDurumu.KaliciYapi);
                                continue;
                            }
                        }

                        dst[dstIdx] = raw;
                    }
                }
            }
        }

        private static void SettleNaturalRubbleOnCaveFloor(
            Span<uint> voxels,
            int maxY,
            int col,
            int minCollapsedY,
            int fallenCount)
        {
            if (fallenCount <= 0 || minCollapsedY <= StratocraftFloatingIslandWorldGen.DeepMantleFloorY + 1)
            {
                return;
            }

            int floorY = FindFloorYBelow(voxels, col, Math.Min(maxY - 2, minCollapsedY));
            if (floorY < StratocraftFloatingIslandWorldGen.DeepMantleFloorY)
            {
                return;
            }

            // Taban ince bir alt magara tavan koprusuysa (< 2 blok kati kaya), molozu oraya yigma
            if (floorY > StratocraftFloatingIslandWorldGen.DeepMantleFloorY &&
                !VoxelBitUtility.IsPhysicallySolid(voxels[((floorY - 1) << 8) | col]))
            {
                return;
            }

            // Yeni tavan yuksekligini bul ve yurume boslugunu (>= 2 blok) asla tikama
            int newCeilingY = maxY - 1;
            for (int cy = floorY + 1; cy < maxY; cy++)
            {
                if (VoxelBitUtility.IsPhysicallySolid(voxels[(cy << 8) | col]))
                {
                    newCeilingY = cy;
                    break;
                }
            }

            int availableHeadroom = newCeilingY - (floorY + 1);
            int maxRubbleLayers = Math.Max(0, availableHeadroom - 2);
            int rubbleLayers = Math.Clamp((fallenCount + 1) / 2, 1, Math.Min(2, maxRubbleLayers));
            if (rubbleLayers <= 0)
            {
                return;
            }

            uint rubbleRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.LooseRubble, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 3);

            for (int r = 0; r < rubbleLayers; r++)
            {
                int ty = floorY + 1 + r;
                if (ty >= maxY - 1) break;
                int idx = (ty << 8) | col;
                if (!VoxelBitUtility.IsPhysicallySolid(voxels[idx]))
                {
                    voxels[idx] = rubbleRaw;
                }
            }
        }

        private static void CleanFloatingCaveSliversInColumn(Span<uint> voxels, int col, int maxCaveY)
        {
            int y = StratocraftFloatingIslandWorldGen.DeepMantleFloorY + 2;
            while (y <= maxCaveY)
            {
                if (!VoxelBitUtility.IsPhysicallySolid(voxels[(y << 8) | col]))
                {
                    y++;
                    continue;
                }

                int s0 = y;
                bool hasProtected = false;
                while (y <= maxCaveY && VoxelBitUtility.IsPhysicallySolid(voxels[(y << 8) | col]))
                {
                    if (!CanPreSettleModifyVoxel(voxels[(y << 8) | col]))
                    {
                        hasProtected = true;
                    }
                    y++;
                }

                int s1 = y - 1;
                if (!hasProtected &&
                    (s1 - s0 + 1) <= 4 &&
                    !VoxelBitUtility.IsPhysicallySolid(voxels[((s0 - 1) << 8) | col]) &&
                    !VoxelBitUtility.IsPhysicallySolid(voxels[((s1 + 1) << 8) | col]))
                {
                    for (int cy = s0; cy <= s1; cy++)
                    {
                        voxels[(cy << 8) | col] = 0u;
                    }
                }
            }
        }

        private static void MeasureOpenCeilingSpanX(ReadOnlySpan<uint> voxels, int y, int lz, int seedX, out int x0, out int x1)
        {
            int yShift = y << 8;
            int aboveShift = (y + 1) << 8;
            int zShift = lz << 4;

            x0 = seedX;
            while (x0 > 0 &&
                   !VoxelBitUtility.IsPhysicallySolid(voxels[yShift | zShift | (x0 - 1)]) &&
                   VoxelBitUtility.IsPhysicallySolid(voxels[aboveShift | zShift | (x0 - 1)]))
            {
                x0--;
            }

            x1 = seedX;
            while (x1 < 15 &&
                   !VoxelBitUtility.IsPhysicallySolid(voxels[yShift | zShift | (x1 + 1)]) &&
                   VoxelBitUtility.IsPhysicallySolid(voxels[aboveShift | zShift | (x1 + 1)]))
            {
                x1++;
            }
        }

        private static void MeasureOpenCeilingSpanZ(ReadOnlySpan<uint> voxels, int y, int lx, int seedZ, out int z0, out int z1)
        {
            int yShift = y << 8;
            int aboveShift = (y + 1) << 8;

            z0 = seedZ;
            while (z0 > 0 &&
                   !VoxelBitUtility.IsPhysicallySolid(voxels[yShift | ((z0 - 1) << 4) | lx]) &&
                   VoxelBitUtility.IsPhysicallySolid(voxels[aboveShift | ((z0 - 1) << 4) | lx]))
            {
                z0--;
            }

            z1 = seedZ;
            while (z1 < 15 &&
                   !VoxelBitUtility.IsPhysicallySolid(voxels[yShift | ((z1 + 1) << 4) | lx]) &&
                   VoxelBitUtility.IsPhysicallySolid(voxels[aboveShift | ((z1 + 1) << 4) | lx]))
            {
                z1++;
            }
        }

        private static int MeasureOpenCeilingSpanXLength(ReadOnlySpan<uint> voxels, int y, int lz, int seedX)
        {
            MeasureOpenCeilingSpanX(voxels, y, lz, seedX, out int x0, out int x1);
            return x1 - x0 + 1;
        }

        private static int MeasureOpenCeilingSpanZLength(ReadOnlySpan<uint> voxels, int y, int lx, int seedZ)
        {
            MeasureOpenCeilingSpanZ(voxels, y, lx, seedZ, out int z0, out int z1);
            return z1 - z0 + 1;
        }

        private static int MeasureMinHeadroomInSpan(
            ReadOnlySpan<uint> voxels,
            int spanStart,
            int spanWidth,
            int cavityY,
            int fixedCoord,
            bool alongX)
        {
            int minH = int.MaxValue;
            for (int s = spanStart; s < spanStart + spanWidth; s++)
            {
                int lx = alongX ? s : fixedCoord;
                int lz = alongX ? fixedCoord : s;
                int col = (lz << 4) | lx;
                int floorY = FindFloorYBelow(voxels, col, cavityY);
                if (floorY < 0)
                {
                    return 0;
                }
                int h = cavityY - floorY;
                if (h < minH) minH = h;
            }
            return minH == int.MaxValue ? 0 : minH;
        }

        private static int FindFloorYBelow(ReadOnlySpan<uint> voxels, int col, int startY)
        {
            int minY = Math.Max(StratocraftFloatingIslandWorldGen.DeepMantleFloorY, startY - MaxPillarHeightBlocks);
            for (int fy = startY - 1; fy >= minY; fy--)
            {
                if (VoxelBitUtility.IsPhysicallySolid(voxels[(fy << 8) | col]))
                {
                    return fy;
                }
            }
            return -1;
        }

        private static bool IsSolidAt(ReadOnlySpan<uint> voxels, int lx, int y, int lz)
        {
            if ((uint)lx >= 16u || (uint)lz >= 16u || y < 0)
            {
                return false;
            }
            int idx = (y << 8) | (lz << 4) | lx;
            return (uint)idx < (uint)voxels.Length && VoxelBitUtility.IsPhysicallySolid(voxels[idx]);
        }

        private static bool IsSurfaceWoodOrCanopy(uint raw, byte mat)
        {
            if (mat == MaterialIdCatalog.OakWood ||
                mat == MaterialIdCatalog.Ironwood ||
                mat == MaterialIdCatalog.SpiritWood)
            {
                return true;
            }

            if ((mat == MaterialIdCatalog.Grass || mat == MaterialIdCatalog.GlowingMoss || mat == MaterialIdCatalog.Marble) &&
                VoxelBitUtility.GetShapeId(raw) != 0)
            {
                return true;
            }

            return false;
        }

        private static bool CanPreSettleModifyVoxel(uint raw)
        {
            if (raw == 0u) return false;
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
            {
                return false;
            }

            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == MaterialIdCatalog.Bedrock ||
                mat == MaterialIdCatalog.Floatstone ||
                mat >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                return false;
            }

            return true;
        }

        private static uint HashPcg(int x, int y, int z)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }

        private static void EnsureTlsBuffers(
            int requiredVolume,
            out uint[] scratch,
            out StructuralCollapseSystem collapseSys,
            out FallingStructureClusterComponent[] clusters)
        {
            if (_tlsSolveScratch == null || _tlsSolveScratch.Length < requiredVolume)
            {
                _tlsSolveScratch = new uint[Math.Max(requiredVolume, 16 * 96 * 16)];
            }

            _tlsCollapseSystem ??= new StructuralCollapseSystem();
            _tlsClusters ??= new FallingStructureClusterComponent[GreedyBoxFractureJob.MaxCompoundBoxes];

            scratch = _tlsSolveScratch;
            collapseSys = _tlsCollapseSystem;
            clusters = _tlsClusters;
        }
    }
}
