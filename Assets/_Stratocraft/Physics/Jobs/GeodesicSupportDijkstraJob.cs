#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// 3B voxel izgarasi bellek dizilimi:
    /// ZMajorLocal = yerel test/pencere dizilimi x + sizeX * (y + sizeY * z);
    /// YMajorChunk = VoxelChunkComponent dogal dizilimi (y * sizeZ + z) * sizeX + x.
    /// </summary>
    public enum VoxelGridMemoryLayout : byte
    {
        ZMajorLocal = 0,
        YMajorChunk = 1
    }

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.C — Faz 1 Jeodezik Destek ve Kemer Kilit Tasi (Keystone) Cozucusu:
    /// - 96 Kovali Dial's Bucket Dijkstra O(V) + [ThreadStatic] Sifir-GC (0 Byte Heap Alloc) tampon havuzu
    /// - SDF Katilik Esigi (ShapeID != 0 || Density &gt;= 64) ve 2-Blok Derinlikli Triaxial Kaya Ankraji (Density &gt;= 96)
    /// - Dikey Kolon Maliyeti w_vert = 0 (yuksek kuleler jeodezik mesafeden kopmaz, yalnizca Faz 2 ezilme limitiyle yikilir)
    /// - Capraz Kemer Maliyeti w_arch = max(1, kappa / 2) ve Karsilikli Yatay Itki (H_L + H_R = 0) Kilit Tasi Dogrulamasi!
    /// </summary>
    public static class GeodesicSupportDijkstraJob
    {
        public const ushort UnreachableDistance = ushort.MaxValue;
        public const int MaxDialBuckets = 96;

        // Ilk 6 komsu ortogonaldir (+X, -X, +Y, -Y, +Z, -Z); 6..9 komsular capraz yukselen kemer adimlaridir
        private static readonly int[] StepDx = { 1, -1, 0, 0, 0, 0, 1, -1, 0, 0 };
        private static readonly int[] StepDy = { 0, 0, 1, -1, 0, 0, 1, 1, 1, 1 };
        private static readonly int[] StepDz = { 0, 0, 0, 0, 1, -1, 0, 0, 1, -1 };

        [ThreadStatic] private static int[]? _tlsBucketHeads;
        [ThreadStatic] private static int[]? _tlsPrevInBucket;
        [ThreadStatic] private static int[]? _tlsNextInBucket;
        [ThreadStatic] private static short[]? _tlsNodeBucket;
        [ThreadStatic] private static int[]? _tlsAnchorCol;
        [ThreadStatic] private static int[]? _tlsParentIdx;
        [ThreadStatic] private static short[]? _tlsSpringY;
        [ThreadStatic] private static byte[]? _tlsArchLocked;

        public static int Execute(
            ReadOnlySpan<uint> voxels,
            int sizeX,
            int sizeY,
            int sizeZ,
            int supportFloorY,
            Span<ushort> outGeodesicDistance,
            Span<ushort> outCantileverSpan,
            Span<byte> outUnsupportedMask,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.ZMajorLocal)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalVoxels ||
                outGeodesicDistance.Length < totalVoxels ||
                outCantileverSpan.Length < totalVoxels ||
                outUnsupportedMask.Length < totalVoxels)
            {
                throw new ArgumentException("Buffer length is smaller than grid volume.");
            }

            EnsureScratchBuffers(
                totalVoxels,
                out int[] bucketHeads,
                out int[] prevInBucket,
                out int[] nextInBucket,
                out short[] nodeBucket,
                out int[] anchorCol,
                out int[] parentIdx,
                out short[] springY,
                out byte[] archLocked);

            Array.Fill(bucketHeads, -1, 0, MaxDialBuckets);
            Array.Fill(prevInBucket, -1, 0, totalVoxels);
            Array.Fill(nextInBucket, -1, 0, totalVoxels);
            Array.Fill(nodeBucket, (short)-1, 0, totalVoxels);
            Array.Fill(anchorCol, -1, 0, totalVoxels);
            Array.Fill(parentIdx, -1, 0, totalVoxels);
            Array.Clear(archLocked, 0, totalVoxels);

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        uint raw = voxels[idx];
                        outUnsupportedMask[idx] = 0;

                        if (!VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            outGeodesicDistance[idx] = UnreachableDistance;
                            outCantileverSpan[idx] = UnreachableDistance;
                            continue;
                        }

                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);
                        bool isSupportFloor = y <= supportFloorY;
                        bool isPermanentOrSkyAnchor =
                            props.IsUnbreakableAnchor ||
                            props.IsSkyAnchor ||
                            VoxelBitUtility.GetOriginState(raw) == BlokKokenDurumu.KaliciYapi;
                        bool isTriaxialAnchor = IsTwoDeepTriaxiallyConfinedRock(
                            voxels, x, y, z, sizeX, sizeY, sizeZ, supportFloorY, raw, layout) ||
                            IsFloatingIslandNaturalSpineFoundation(
                                voxels, x, y, z, sizeX, sizeY, sizeZ, raw, layout);

                        if (isSupportFloor || isPermanentOrSkyAnchor || isTriaxialAnchor)
                        {
                            outGeodesicDistance[idx] = 0;
                            outCantileverSpan[idx] = 0;
                            anchorCol[idx] = EncodeAnchorColumn(x, z);
                            springY[idx] = (short)y;
                            PushBucket(bucketHeads, prevInBucket, nextInBucket, nodeBucket, 0, idx);
                        }
                        else
                        {
                            outGeodesicDistance[idx] = UnreachableDistance;
                            outCantileverSpan[idx] = UnreachableDistance;
                        }
                    }
                }
            }

            // Dial's Bucket Geodesic Yayilim (0..95)
            for (int dist = 0; dist < MaxDialBuckets; dist++)
            {
                while (bucketHeads[dist] != -1)
                {
                    int current = bucketHeads[dist];
                    int nextHead = nextInBucket[current];
                    bucketHeads[dist] = nextHead;
                    if (nextHead != -1)
                    {
                        prevInBucket[nextHead] = -1;
                    }
                    prevInBucket[current] = -1;
                    nextInBucket[current] = -1;
                    nodeBucket[current] = -1;

                    if (outGeodesicDistance[current] < dist)
                    {
                        continue;
                    }

                    DecodeGridIndex(current, sizeX, sizeY, sizeZ, layout, out int cx, out int cy, out int cz);
                    uint curRaw = voxels[current];
                    byte curMat = VoxelBitUtility.GetMaterialId(curRaw);
                    StructuralMaterialProps curProps = StructuralMaterialProps.GetForMaterial(curMat);
                    ushort currentCantilever = outCantileverSpan[current];
                    int curAnchor = anchorCol[current];
                    short curSpringY = springY[current];

                    int maxDir = curProps.IsArchCapable ? 10 : 6;
                    for (int dir = 0; dir < maxDir; dir++)
                    {
                        int nx = cx + StepDx[dir];
                        int ny = cy + StepDy[dir];
                        int nz = cz + StepDz[dir];

                        if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                        {
                            continue;
                        }

                        int nIdx = GridIndex(nx, ny, nz, sizeX, sizeY, sizeZ, layout);
                        uint nRaw = voxels[nIdx];
                        if (!VoxelBitUtility.IsPhysicallySolid(nRaw))
                        {
                            continue;
                        }

                        byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                        StructuralMaterialProps nProps = StructuralMaterialProps.GetForMaterial(nMat);
                        bool isDiagonalArchStep = dir >= 6;
                        if (isDiagonalArchStep && !nProps.IsArchCapable)
                        {
                            continue;
                        }

                        bool isConfinedNaturalOverburden =
                            layout == VoxelGridMemoryLayout.YMajorChunk &&
                            VoxelBitUtility.GetOriginState(nRaw) == BlokKokenDurumu.DogalKutle &&
                            HasContinuousNaturalOverburden(voxels, nx, ny, nz, sizeX, sizeY, sizeZ, layout, minOverburden: 3);

                        bool isPureVerticalColumn = ((dir == 2) && (currentCantilever == 0)) || isConfinedNaturalOverburden;
                        int horizDelta = Math.Abs(StepDx[dir]) + Math.Abs(StepDz[dir]);
                        ushort candCantilever = isPureVerticalColumn
                            ? (ushort)0
                            : (ushort)(currentCantilever + (horizDelta > 0 ? 1 : 0));

                        int maxReachAllowed = isConfinedNaturalOverburden
                            ? MaxDialBuckets - 1
                            : (nProps.IsArchCapable
                                ? nProps.MaxClosedArchArmBlocks
                                : nProps.MaxCantileverSpanBlocks);
                        if (candCantilever > maxReachAllowed)
                        {
                            continue;
                        }

                        // w_vert = 0 (dikey kolon), w_arch = max(1, kappa/2) (capraz yukselen kemer), w_lat = max(1, kappa/2) veya 1
                        int stepCost;
                        if (isPureVerticalColumn)
                        {
                            stepCost = 0;
                        }
                        else if (isDiagonalArchStep || (StepDy[dir] == 1 && currentCantilever > 0 && nProps.IsArchCapable))
                        {
                            stepCost = Math.Max(1, nProps.ArchCostKappa / 2);
                        }
                        else if (horizDelta > 0)
                        {
                            stepCost = Math.Max(1, Math.Min(4, nProps.ArchCostKappa / 2));
                        }
                        else
                        {
                            // Dikey (+Y veya -Y asili/konsol uzeri yukselen tas)
                            stepCost = 1;
                        }

                        int candDistInt = dist + stepCost;
                        if (candDistInt >= MaxDialBuckets || candDistInt >= 64 && !isPureVerticalColumn)
                        {
                            continue;
                        }

                        ushort candDist = (ushort)candDistInt;
                        short nextSpringY = isPureVerticalColumn ? (short)ny : curSpringY;

                        if (candDist < outGeodesicDistance[nIdx] ||
                            (candDist == outGeodesicDistance[nIdx] && candCantilever < outCantileverSpan[nIdx]))
                        {
                            outGeodesicDistance[nIdx] = candDist;
                            outCantileverSpan[nIdx] = candCantilever;
                            anchorCol[nIdx] = isPureVerticalColumn ? EncodeAnchorColumn(nx, nz) : curAnchor;
                            springY[nIdx] = nextSpringY;
                            parentIdx[nIdx] = current;
                            PushBucket(bucketHeads, prevInBucket, nextInBucket, nodeBucket, candDist, nIdx);
                        }
                    }
                }
            }

            // Karsilikli Yatay Itki (H_L + H_R = 0) Kilit Tasi (Keystone) Dogrulama Gecisi
            VerifyAndLockClosedArches(
                voxels,
                outGeodesicDistance,
                outCantileverSpan,
                anchorCol,
                parentIdx,
                springY,
                archLocked,
                sizeX,
                sizeY,
                sizeZ,
                layout);

            int unsupportedCount = 0;
            for (int i = 0; i < totalVoxels; i++)
            {
                uint raw = voxels[i];
                if (!VoxelBitUtility.IsPhysicallySolid(raw))
                {
                    continue;
                }

                byte matId = VoxelBitUtility.GetMaterialId(raw);
                StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);
                ushort dist = outGeodesicDistance[i];
                ushort cant = outCantileverSpan[i];

                bool detached = dist == UnreachableDistance ||
                                (cant > props.MaxCantileverSpanBlocks && archLocked[i] == 0);
                if (detached)
                {
                    outUnsupportedMask[i] = 1;
                    unsupportedCount++;
                }
            }

            return unsupportedCount;
        }

        private static void VerifyAndLockClosedArches(
            ReadOnlySpan<uint> voxels,
            Span<ushort> ioGeodesicDistance,
            ReadOnlySpan<ushort> cantileverSpan,
            int[] anchorCol,
            int[] parentIdx,
            short[] springY,
            byte[] archLocked,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 1; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int u = GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        if (ioGeodesicDistance[u] == UnreachableDistance || anchorCol[u] < 0)
                        {
                            continue;
                        }

                        uint uRaw = voxels[u];
                        StructuralMaterialProps uProps = StructuralMaterialProps.GetForMaterial(VoxelBitUtility.GetMaterialId(uRaw));
                        if (!uProps.IsArchCapable)
                        {
                            continue;
                        }

                        DecodeAnchorColumn(anchorCol[u], out int uAx, out int uAz);
                        int uThrustX = x - uAx;
                        int uThrustZ = z - uAz;

                        // Yalnizca +X, +Z ve capraz +Y komsulari kontrol ederek her kemer dugum ciftini tek kez tara
                        for (int dir = 0; dir < 10; dir++)
                        {
                            if (StepDx[dir] < 0 || StepDz[dir] < 0 || (StepDx[dir] == 0 && StepDz[dir] == 0))
                            {
                                continue;
                            }

                            int vx = x + StepDx[dir];
                            int vy = y + StepDy[dir];
                            int vz = z + StepDz[dir];
                            if ((uint)vx >= (uint)sizeX || (uint)vy >= (uint)sizeY || (uint)vz >= (uint)sizeZ)
                            {
                                continue;
                            }

                            int v = GridIndex(vx, vy, vz, sizeX, sizeY, sizeZ, layout);
                            if (ioGeodesicDistance[v] == UnreachableDistance ||
                                anchorCol[v] < 0 ||
                                anchorCol[v] == anchorCol[u])
                            {
                                continue;
                            }

                            uint vRaw = voxels[v];
                            StructuralMaterialProps vProps = StructuralMaterialProps.GetForMaterial(VoxelBitUtility.GetMaterialId(vRaw));
                            if (!vProps.IsArchCapable)
                            {
                                continue;
                            }

                            DecodeAnchorColumn(anchorCol[v], out int vAx, out int vAz);
                            int vThrustX = vx - vAx;
                            int vThrustZ = vz - vAz;

                            // Karsilikli itki dengesi: H_L ve H_R zit isaretli olmali ve kemer taci en az 1 blok yukselmis olmali!
                            bool opposesX = (uThrustX > 0 && vThrustX <= 0 && uAx < vAx) || (uThrustX >= 0 && vThrustX < 0 && uAx < vAx);
                            bool opposesZ = (uThrustZ > 0 && vThrustZ <= 0 && uAz < vAz) || (uThrustZ >= 0 && vThrustZ < 0 && uAz < vAz);
                            int crownY = Math.Max(y, vy);
                            bool hasArchRise = crownY > Math.Min(springY[u], springY[v]) ||
                                               VoxelBitUtility.GetShapeId(uRaw) == ArchitecturalShapeCatalog.TrapezoidalKeystone ||
                                               VoxelBitUtility.GetShapeId(vRaw) == ArchitecturalShapeCatalog.TrapezoidalKeystone;

                            if ((opposesX || opposesZ) && hasArchRise)
                            {
                                // Kilit Tasi d_key = min(d_L, d_R) + 1 (dongusuz %50-%50 simetrik yuk aktarimi)
                                ushort dMin = Math.Min(ioGeodesicDistance[u], ioGeodesicDistance[v]);
                                ushort dKey = (ushort)Math.Min(MaxDialBuckets - 1, dMin + 1);
                                if (cantileverSpan[u] >= cantileverSpan[v])
                                {
                                    ioGeodesicDistance[u] = Math.Max(ioGeodesicDistance[u], dKey);
                                }
                                else
                                {
                                    ioGeodesicDistance[v] = Math.Max(ioGeodesicDistance[v], dKey);
                                }

                                TraceLockArchArm(u, parentIdx, archLocked, voxels.Length);
                                TraceLockArchArm(v, parentIdx, archLocked, voxels.Length);
                            }
                        }
                    }
                }
            }
        }

        private static void TraceLockArchArm(int startIdx, int[] parentIdx, byte[] archLocked, int maxGuard)
        {
            int cur = startIdx;
            int steps = 0;
            while (cur >= 0 && steps < maxGuard)
            {
                if (archLocked[cur] != 0)
                {
                    break;
                }
                archLocked[cur] = 1;
                cur = parentIdx[cur];
                steps++;
            }
        }

        /// <summary>
        /// 2-Blok Derinlikli Triaxial Kaya Hapsi:
        /// Dogal kutle (OriginState == DogalKutle) ve yuksek yogunluklu (Density &gt;= 96 veya Shape != 0) kaya,
        /// alt mesnet tabaninda veya tum 4 yatay yonde + altinda kati dogal kayayla kusatilmissa ana kaya ankrajidir.
        /// </summary>
        private static bool IsTwoDeepTriaxiallyConfinedRock(
            ReadOnlySpan<uint> voxels,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            int supportFloorY,
            uint raw,
            VoxelGridMemoryLayout layout)
        {
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
            {
                return false;
            }

            byte shape = VoxelBitUtility.GetShapeId(raw);
            byte dens = VoxelBitUtility.GetDensityOrLiquid(raw);
            if (shape == 0 && dens < StratocraftConstants.SdfConfinedSolidLevel)
            {
                return false;
            }

            if (y <= supportFloorY)
            {
                return true;
            }

            // Taban (-Y) kati kaya kontrolu veya YMajorChunk / kalin dogal tavan kemeri dis sinir ankraji
            int belowIdx = GridIndex(x, y - 1, z, sizeX, sizeY, sizeZ, layout);
            bool hasSolidBelow = VoxelBitUtility.IsPhysicallySolid(voxels[belowIdx]);

            // Yalnizca pencere yan sinirlarindaki (kesilmemis ana kaya devam eden) dogal bloklar yanal ankraj olur;
            // icerde alttan kazilmis tavan bloklari yanlis ankrajlanmaz!
            bool touchesOuterRockBoundary = (x == 0 || x == sizeX - 1 || z == 0 || z == sizeZ - 1);
            if (!touchesOuterRockBoundary)
            {
                return false;
            }

            if (!hasSolidBelow)
            {
                // Chunk / pencere sinirini kesen dogal magara kemerlerinde ustte en az 4 blok kesintisiz dogal kaya
                // ortusu (overburden) varsa komsu chunk'taki ana kaya kutlesine yatay kemer ankraji olusturur.
                if (layout != VoxelGridMemoryLayout.YMajorChunk ||
                    !HasContinuousNaturalOverburden(voxels, x, y, z, sizeX, sizeY, sizeZ, layout, minOverburden: 4))
                {
                    return false;
                }
                return true;
            }

            int confinedDirs = 0;
            ReadOnlySpan<int> hDx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> hDz = stackalloc int[4] { 0, 0, 1, -1 };

            for (int d = 0; d < 4; d++)
            {
                int nx1 = x + hDx[d];
                int nz1 = z + hDz[d];
                if ((uint)nx1 >= (uint)sizeX || (uint)nz1 >= (uint)sizeZ)
                {
                    confinedDirs++;
                    continue;
                }

                uint r1 = voxels[GridIndex(nx1, y, nz1, sizeX, sizeY, sizeZ, layout)];
                if (!VoxelBitUtility.IsPhysicallySolid(r1) ||
                    VoxelBitUtility.GetOriginState(r1) != BlokKokenDurumu.DogalKutle)
                {
                    continue;
                }

                int nx2 = x + hDx[d] * 2;
                int nz2 = z + hDz[d] * 2;
                if ((uint)nx2 >= (uint)sizeX || (uint)nz2 >= (uint)sizeZ)
                {
                    confinedDirs++;
                    continue;
                }

                uint r2 = voxels[GridIndex(nx2, y, nz2, sizeX, sizeY, sizeZ, layout)];
                if (VoxelBitUtility.IsPhysicallySolid(r2) &&
                    VoxelBitUtility.GetOriginState(r2) == BlokKokenDurumu.DogalKutle)
                {
                    confinedDirs++;
                }
            }

            return confinedDirs >= 4;
        }

        private static bool HasContinuousNaturalOverburden(
            ReadOnlySpan<uint> voxels,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            int minOverburden)
        {
            int count = 0;
            for (int ny = y + 1; ny < sizeY && count < minOverburden; ny++)
            {
                uint aboveRaw = voxels[GridIndex(x, ny, z, sizeX, sizeY, sizeZ, layout)];
                if (VoxelBitUtility.IsPhysicallySolid(aboveRaw) &&
                    VoxelBitUtility.GetOriginState(aboveRaw) == BlokKokenDurumu.DogalKutle)
                {
                    count++;
                }
                else
                {
                    break;
                }
            }

            if (count >= minOverburden)
            {
                return true;
            }

            // Eger kendi ustundeki dikey ortu < minOverburden ise (ornegin dogal magara tavaninin tam alt kenari veya
            // agac govdesi/yaprak kumesi), 4 yatay komsusundan en az birinin kesintisiz kalin ortuye bagli olup olmadigina bak:
            for (int d = 0; d < 4; d++)
            {
                int nx = x + StepDx[d];
                int nz = z + StepDz[d];
                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ) continue;
                uint nRaw = voxels[GridIndex(nx, y, nz, sizeX, sizeY, sizeZ, layout)];
                if (VoxelBitUtility.IsPhysicallySolid(nRaw) &&
                    VoxelBitUtility.GetOriginState(nRaw) == BlokKokenDurumu.DogalKutle)
                {
                    int nCount = 0;
                    for (int ny = y + 1; ny < sizeY && nCount < minOverburden; ny++)
                    {
                        uint naRaw = voxels[GridIndex(nx, ny, nz, sizeX, sizeY, sizeZ, layout)];
                        if (VoxelBitUtility.IsPhysicallySolid(naRaw) &&
                            VoxelBitUtility.GetOriginState(naRaw) == BlokKokenDurumu.DogalKutle)
                        {
                            nCount++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (nCount >= minOverburden) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Yuzen Ada (Floating Island) dogal omurga mesnedi:
        /// YMajorChunk duzeninde (yuzen ada chunk'lari) bir sutundaki en alt dogal bazalt/kaya kabugu (y &lt;= 9)
        /// adanin kaldırma omurgasina baglidir; ustteki dogal bloklar ise aralarinda kazilmis bosluk olmadigi surece
        /// dikey kolon (w_vert = 0) uzerinden bu omurgadan destek alir.
        /// </summary>
        private static bool IsFloatingIslandNaturalSpineFoundation(
            ReadOnlySpan<uint> voxels,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            uint raw,
            VoxelGridMemoryLayout layout)
        {
            if (layout != VoxelGridMemoryLayout.YMajorChunk ||
                y > 10 ||
                VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
            {
                return false;
            }

            // Bu sutundaki en alt kati/SDF kabuk vokselinden y'ye kadar kazilmis bir hava boslugu (Air) var mi?
            int lowestShellY = -1;
            for (int scanY = 1; scanY <= y; scanY++)
            {
                uint scanRaw = voxels[GridIndex(x, scanY, z, sizeX, sizeY, sizeZ, layout)];
                bool isNaturalShellOrSolid = VoxelBitUtility.GetMaterialId(scanRaw) != MaterialIdCatalog.Air &&
                                             VoxelBitUtility.GetDensityOrLiquid(scanRaw) >= 16;
                if (isNaturalShellOrSolid)
                {
                    if (lowestShellY < 0)
                    {
                        lowestShellY = scanY;
                    }
                }
                else if (lowestShellY >= 0)
                {
                    // Altinda kazilmis gercek bir hava boslugu / magara var -> artik dogrudan ada omurgasina oturmuyor!
                    return false;
                }
            }

            if (lowestShellY < 0 || lowestShellY > 9)
            {
                return false;
            }

            // Tek basina havada kalan 1 blokluk parcalar degil, en az 2 dogal kati komsusu olan ada govdesi
            int solidNeighbors = 0;
            if (x > 0 && VoxelBitUtility.IsPhysicallySolid(voxels[GridIndex(x - 1, y, z, sizeX, sizeY, sizeZ, layout)])) solidNeighbors++;
            if (x + 1 < sizeX && VoxelBitUtility.IsPhysicallySolid(voxels[GridIndex(x + 1, y, z, sizeX, sizeY, sizeZ, layout)])) solidNeighbors++;
            if (z > 0 && VoxelBitUtility.IsPhysicallySolid(voxels[GridIndex(x, y, z - 1, sizeX, sizeY, sizeZ, layout)])) solidNeighbors++;
            if (z + 1 < sizeZ && VoxelBitUtility.IsPhysicallySolid(voxels[GridIndex(x, y, z + 1, sizeX, sizeY, sizeZ, layout)])) solidNeighbors++;
            if (y + 1 < sizeY && VoxelBitUtility.IsPhysicallySolid(voxels[GridIndex(x, y + 1, z, sizeX, sizeY, sizeZ, layout)])) solidNeighbors++;

            return solidNeighbors >= 2;
        }

        private static void EnsureScratchBuffers(
            int totalVoxels,
            out int[] bucketHeads,
            out int[] prevInBucket,
            out int[] nextInBucket,
            out short[] nodeBucket,
            out int[] anchorCol,
            out int[] parentIdx,
            out short[] springY,
            out byte[] archLocked)
        {
            if (_tlsBucketHeads == null || _tlsBucketHeads.Length < MaxDialBuckets)
            {
                _tlsBucketHeads = new int[MaxDialBuckets];
            }

            if (_tlsNextInBucket == null || _tlsNextInBucket.Length < totalVoxels)
            {
                int cap = Math.Max(totalVoxels, 4096);
                _tlsPrevInBucket = new int[cap];
                _tlsNextInBucket = new int[cap];
                _tlsNodeBucket = new short[cap];
                _tlsAnchorCol = new int[cap];
                _tlsParentIdx = new int[cap];
                _tlsSpringY = new short[cap];
                _tlsArchLocked = new byte[cap];
            }

            bucketHeads = _tlsBucketHeads;
            prevInBucket = _tlsPrevInBucket!;
            nextInBucket = _tlsNextInBucket!;
            nodeBucket = _tlsNodeBucket!;
            anchorCol = _tlsAnchorCol!;
            parentIdx = _tlsParentIdx!;
            springY = _tlsSpringY!;
            archLocked = _tlsArchLocked!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EncodeAnchorColumn(int x, int z) => ((x & 0x7FFF) << 15) | (z & 0x7FFF);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DecodeAnchorColumn(int code, out int x, out int z)
        {
            x = (code >> 15) & 0x7FFF;
            z = code & 0x7FFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PushBucket(
            int[] bucketHeads,
            int[] prevInBucket,
            int[] nextInBucket,
            short[] nodeBucket,
            int bucket,
            int nodeIndex)
        {
            int curBucket = nodeBucket[nodeIndex];
            if (curBucket == bucket)
            {
                return;
            }

            if (curBucket != -1)
            {
                int p = prevInBucket[nodeIndex];
                int n = nextInBucket[nodeIndex];
                if (p != -1)
                {
                    nextInBucket[p] = n;
                }
                else
                {
                    bucketHeads[curBucket] = n;
                }

                if (n != -1)
                {
                    prevInBucket[n] = p;
                }
            }

            int oldHead = bucketHeads[bucket];
            prevInBucket[nodeIndex] = -1;
            nextInBucket[nodeIndex] = oldHead;
            if (oldHead != -1)
            {
                prevInBucket[oldHead] = nodeIndex;
            }

            bucketHeads[bucket] = nodeIndex;
            nodeBucket[nodeIndex] = (short)bucket;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GridIndex(
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            return layout == VoxelGridMemoryLayout.YMajorChunk
                ? (y * sizeZ + z) * sizeX + x
                : x + sizeX * (y + sizeY * z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeGridIndex(
            int index,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            out int x,
            out int y,
            out int z)
        {
            if (layout == VoxelGridMemoryLayout.YMajorChunk)
            {
                x = index % sizeX;
                int rem = index / sizeX;
                z = rem % sizeZ;
                y = rem / sizeZ;
            }
            else
            {
                x = index % sizeX;
                int rem = index / sizeX;
                y = rem % sizeY;
                z = rem / sizeY;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Index3D(int x, int y, int z, int sizeX, int sizeY)
        {
            return x + sizeX * (y + sizeY * z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexYMajor(int x, int y, int z, int sizeX, int sizeZ)
        {
            return (y * sizeZ + z) * sizeX + x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Decode3D(int index, int sizeX, int sizeY, out int x, out int y, out int z)
        {
            x = index % sizeX;
            int rem = index / sizeX;
            y = rem % sizeY;
            z = rem / sizeY;
        }
    }
}
