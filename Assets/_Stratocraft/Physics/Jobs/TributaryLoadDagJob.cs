#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.C — Faz 2 Tek Gecisli (0 Jacobi Iterasyonu) Tributary Load DAG
    /// + 2B (X ve Z Eksenli) Terzaghi Kaya Kemerlenmesi / Magara Tavan Gocugu Cozucusu:
    /// - Her Y katmaninda dugumleri azalan d_sup (MaxDialBuckets-1 -&gt; 0) kova sirasinda isleyerek
    ///   yanal konsol/kemer yuk aktariminda %100 yonsuz (simetrik) kesme ve ezilme cozumu uretir!
    /// - Ust pencere siniri (Top Halo) yuk enjeksiyonu ve ShapeID == 63 mikro-voxel darbogaz kesiti (A_min/16) destekler.
    /// - Hem X hem Z eksenindeki yeralti galeri acikliklarinda parabolik Terzaghi gevseme kubbesi hesaplar.
    /// </summary>
    public static class TributaryLoadDagJob
    {
        private static readonly int[] HorizontalDx = { 1, -1, 0, 0 };
        private static readonly int[] HorizontalDz = { 0, 0, 1, -1 };

        // Alt veya ayni katmandaki daha yakin kemer mesnedine yuk aktarma adaylari (once -Y, sonra capraz -Y, sonra yatay)
        private static readonly int[] TransferDx = { 1, -1, 0, 0, 1, -1, 0, 0 };
        private static readonly int[] TransferDy = { -1, -1, -1, -1, 0, 0, 0, 0 };
        private static readonly int[] TransferDz = { 0, 0, 1, -1, 0, 0, 1, -1 };

        [ThreadStatic] private static int[]? _tlsDistHeads;
        [ThreadStatic] private static int[]? _tlsDistNext;
        [ThreadStatic] private static byte[]? _tlsTimberShoredCeiling;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _tlsDistHeads = null;
            _tlsDistNext = null;
            _tlsTimberShoredCeiling = null;
        }
#endif

        public static int Execute(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<float> outAccumulatedLoadKg,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout = VoxelGridMemoryLayout.ZMajorLocal,
            ReadOnlySpan<float> optionalTopHaloLoadKg = default,
            Func<int, int, int, ulong>? microMaskLookup = null,
            bool enableLocalTerzaghiLoosening = false,
            ReadOnlySpan<byte> groundedTimberMask = default)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalVoxels ||
                geodesicDistance.Length < totalVoxels ||
                outAccumulatedLoadKg.Length < totalVoxels ||
                ioUnsupportedOrCrushedMask.Length < totalVoxels)
            {
                throw new ArgumentException("Buffer length is smaller than grid volume.");
            }

            outAccumulatedLoadKg.Slice(0, totalVoxels).Clear();

            // Ust pencere siniri (Top Halo) ust katman yuk enjeksiyonu (hile korumasi)
            if (!optionalTopHaloLoadKg.IsEmpty && optionalTopHaloLoadKg.Length >= sizeX * sizeZ && sizeY > 0)
            {
                int topY = sizeY - 1;
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int topIdx = GeodesicSupportDijkstraJob.GridIndex(x, topY, z, sizeX, sizeY, sizeZ, layout);
                        outAccumulatedLoadKg[topIdx] = Math.Max(0f, optionalTopHaloLoadKg[z * sizeX + x]);
                    }
                }
            }

            int sliceArea = sizeX * sizeZ;
            EnsureSliceBuckets(sliceArea, out int[] distHeads, out int[] distNext);
            Span<int> receiverIndices = stackalloc int[8];
            int newlyFailedCount = 0;
            bool hasGroundedTimberSpan = groundedTimberMask.Length >= totalVoxels;

            // 1. Tek Gecisli Topolojik DAG: Y_max -> Y_min, her Y diliminde d_sup_max -> 0!
            for (int y = sizeY - 1; y >= 0; y--)
            {
                Array.Fill(distHeads, -1, 0, GeodesicSupportDijkstraJob.MaxDialBuckets);
                int maxBucketInSlice = -1;

                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        uint raw = voxels[idx];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw) || ioUnsupportedOrCrushedMask[idx] != 0)
                        {
                            continue;
                        }

                        ushort d = geodesicDistance[idx];
                        int b = d >= GeodesicSupportDijkstraJob.MaxDialBuckets
                            ? GeodesicSupportDijkstraJob.MaxDialBuckets - 1
                            : d;
                        int local2D = z * sizeX + x;
                        distNext[local2D] = distHeads[b];
                        distHeads[b] = local2D;
                        if (b > maxBucketInSlice)
                        {
                            maxBucketInSlice = b;
                        }
                    }
                }

                for (int b = maxBucketInSlice; b >= 0; b--)
                {
                    int cur2D = distHeads[b];
                    while (cur2D != -1)
                    {
                        int next2D = distNext[cur2D];
                        int x = cur2D % sizeX;
                        int z = cur2D / sizeX;
                        cur2D = next2D;

                        int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        uint raw = voxels[idx];
                        if (!VoxelBitUtility.IsPhysicallySolid(raw) || ioUnsupportedOrCrushedMask[idx] != 0)
                        {
                            continue;
                        }

                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(matId);
                        BlokKokenDurumu origin = VoxelBitUtility.GetOriginState(raw);

                        int microCrossSection16 = 16;
                        float volumeFraction = 1.0f;
                        if (VoxelBitUtility.GetShapeId(raw) == VoxelBitUtility.ChiseledMicroVoxelShapeId && microMaskLookup != null)
                        {
                            ulong mask64 = microMaskLookup(x, y, z);
                            microCrossSection16 = MicroVoxelPool4096.ComputeVerticalBottleneckCrossSection16(mask64);
                            volumeFraction = Math.Max(1, BitOps.PopCount(mask64)) / 64.0f;
                        }

                        float selfMass = props.MassKg * volumeFraction;
                        float totalLoad = selfMass + outAccumulatedLoadKg[idx];
                        outAccumulatedLoadKg[idx] = totalLoad;

                        int lateralConfinedFaces = CountConfinedNaturalFaces(
                            voxels, ioUnsupportedOrCrushedMask, x, y, z, sizeX, sizeY, sizeZ, raw, layout, enableLocalTerzaghiLoosening);

                        // A) Dikey Basinc / Ezilme Kontrolu (Mikro-voxel darbogaz kesiti A_min/16 dahil)
                        if (totalLoad > props.ComputeEffectiveCrushLimit(lateralConfinedFaces, microCrossSection16))
                        {
                            ioUnsupportedOrCrushedMask[idx] = 1;
                            newlyFailedCount++;
                            continue;
                        }

                        // Kalici ankrajli dag ortusu / bedrock yuku kendi litostatik kemeriyle dag kutlesine aktarir;
                        // altindaki maden tahkimati direklerini sonsuz dag agirligiyla yapay olarak ezmez.
                        if (props.IsUnbreakableAnchor || props.IsSkyAnchor || origin == BlokKokenDurumu.KaliciYapi)
                        {
                            continue;
                        }

                        if (y == 0)
                        {
                            continue;
                        }

                        int belowIdx = GeodesicSupportDijkstraJob.GridIndex(x, y - 1, z, sizeX, sizeY, sizeZ, layout);
                        if (VoxelBitUtility.IsPhysicallySolid(voxels[belowIdx]) &&
                            ioUnsupportedOrCrushedMask[belowIdx] == 0)
                        {
                            // Terzaghi-Jansen Silo Yasasi: 4 yandan kusatilmis dogal dag kayasi dikey yuku yerel gevseme
                            // derinligiyle (~6 blok) sinirlar; yalnizca acik/oyuncu kolonlari tam agirlik aktarir.
                            float passDownLoad = (lateralConfinedFaces >= 4 && origin == BlokKokenDurumu.DogalKutle)
                                ? MathF.Min(totalLoad, 6.0f * props.MassKg)
                                : totalLoad;
                            outAccumulatedLoadKg[belowIdx] += passDownLoad;
                            continue;
                        }

                        // Altinda dogrudan zemine basan moduler ahsap tahkimat diregi/kirisi (LogPillar3m/LogBeam2m) varsa yuku direk tasir
                        if (hasGroundedTimberSpan && groundedTimberMask[belowIdx] != 0)
                        {
                            continue;
                        }

                        if (geodesicDistance[idx] == 0)
                        {
                            // Gokyuzu ankraji / yuzen ada omurga mesnedi tabani yuku dogrudan ada temelinde sönümler
                            continue;
                        }

                        // B) Alti Bos (Konsol veya Kemer): Kesme (Shear) Dayanimi Kontrolu ve Daha Yakin Mesnetlere (%50-%50 Kilit Tasi Dahil) Aktarim
                        if (lateralConfinedFaces < 4 && totalLoad > props.ComputeEffectiveShearLimit(microCrossSection16))
                        {
                            ioUnsupportedOrCrushedMask[idx] = 1;
                            newlyFailedCount++;
                            continue;
                        }

                        ushort myDist = geodesicDistance[idx];
                        int closerNeighbors = 0;

                        for (int d = 0; d < 8; d++)
                        {
                            int nx = x + TransferDx[d];
                            int ny = y + TransferDy[d];
                            int nz = z + TransferDz[d];
                            if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                            {
                                continue;
                            }

                            int nIdx = GeodesicSupportDijkstraJob.GridIndex(nx, ny, nz, sizeX, sizeY, sizeZ, layout);
                            if (VoxelBitUtility.IsPhysicallySolid(voxels[nIdx]) &&
                                ioUnsupportedOrCrushedMask[nIdx] == 0 &&
                                geodesicDistance[nIdx] < myDist)
                            {
                                receiverIndices[closerNeighbors++] = nIdx;
                            }
                        }

                        if (closerNeighbors > 0)
                        {
                            float share = totalLoad / closerNeighbors;
                            for (int r = 0; r < closerNeighbors; r++)
                            {
                                outAccumulatedLoadKg[receiverIndices[r]] += share;
                            }
                        }
                    }
                }
            }

            // 2. 3B Kemer Tanimali (d^2y/dx^2 < 0) + Tahkimat Destekli Terzaghi Kaya Kemerlenmesi / Tavan Gevseme Kubbesi
            // Not: allowBoundaryOpenSpan DAIMA false kalir; 32x32 komsu chunk halosuyla gercek komsu duvar/sutunlar gorulur!
            if (layout != VoxelGridMemoryLayout.YMajorChunk || enableLocalTerzaghiLoosening)
            {
                newlyFailedCount += Evaluate2DTerzaghiRoofLoosening(
                    voxels, geodesicDistance, sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout,
                    allowBoundaryOpenSpan: false,
                    groundedTimberMask: hasGroundedTimberSpan ? groundedTimberMask : ReadOnlySpan<byte>.Empty);
            }

            return newlyFailedCount;
        }

        private static int CountConfinedNaturalFaces(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            uint raw,
            VoxelGridMemoryLayout layout,
            bool enableLocalTerzaghiLoosening)
        {
            if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle)
            {
                return 0;
            }

            if (layout == VoxelGridMemoryLayout.YMajorChunk && !enableLocalTerzaghiLoosening)
            {
                return 4;
            }

            int lateralConfinedFaces = 0;
            for (int d = 0; d < 4; d++)
            {
                int cnx = x + HorizontalDx[d];
                int cnz = z + HorizontalDz[d];
                if ((uint)cnx >= (uint)sizeX || (uint)cnz >= (uint)sizeZ)
                {
                    lateralConfinedFaces++;
                    continue;
                }

                int cnIdx = GeodesicSupportDijkstraJob.GridIndex(cnx, y, cnz, sizeX, sizeY, sizeZ, layout);
                if (VoxelBitUtility.IsPhysicallySolid(voxels[cnIdx]) && failedMask[cnIdx] == 0)
                {
                    lateralConfinedFaces++;
                }
            }

            return lateralConfinedFaces;
        }

        private static int Evaluate2DTerzaghiRoofLoosening(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout,
            bool allowBoundaryOpenSpan,
            ReadOnlySpan<byte> groundedTimberMask)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            bool hasGroundedTimber = false;
            byte[]? timberShoredCeiling = null;

            if (groundedTimberMask.Length >= totalVoxels)
            {
                hasGroundedTimber = BuildTimberShoredCeilingMap(
                    voxels, ioUnsupportedOrCrushedMask, groundedTimberMask,
                    sizeX, sizeY, sizeZ, layout, out timberShoredCeiling);
            }

            int loosenedTotal = 0;

            // A) X ekseni boyunca acik galeri tavanlari
            // Not: Faz 1'de kopan sutun/bloklar (mask == 1) acik bosluk sayilir; Terzaghi gevsemesi mask = 2 yazar ki
            // X ve Z gecisleri %100 izotropik ayni bosluk geometrisini gorsun.
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    int currentSpan = 0;
                    int spanStartX = 0;

                    for (int x = 0; x <= sizeX; x++)
                    {
                        bool isAirUnderSolid = false;
                        if (x < sizeX)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                            int aboveIdx = GeodesicSupportDijkstraJob.GridIndex(x, y + 1, z, sizeX, sizeY, sizeZ, layout);
                            bool cellEmpty = IsCavityCellEmpty(voxels, ioUnsupportedOrCrushedMask, groundedTimberMask, idx);
                            bool aboveSolid = VoxelBitUtility.IsPhysicallySolid(voxels[aboveIdx]) && ioUnsupportedOrCrushedMask[aboveIdx] != 1;
                            isAirUnderSolid = cellEmpty && aboveSolid;
                        }

                        if (isAirUnderSolid)
                        {
                            if (currentSpan == 0) spanStartX = x;
                            currentSpan++;
                        }
                        else if (currentSpan > 0)
                        {
                            loosenedTotal += ApplyTerzaghiDomeAlongLine(
                                voxels, geodesicDistance, spanStartX, currentSpan, y, fixedCoord: z, alongX: true,
                                sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout, allowBoundaryOpenSpan,
                                groundedTimberMask, hasGroundedTimber ? timberShoredCeiling : null);
                            currentSpan = 0;
                        }
                    }
                }
            }

            // B) Z ekseni boyunca acik galeri tavanlari (90 derece dondurulmus tunel istismarini %100 kapatir!)
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    int currentSpan = 0;
                    int spanStartZ = 0;

                    for (int z = 0; z <= sizeZ; z++)
                    {
                        bool isAirUnderSolid = false;
                        if (z < sizeZ)
                        {
                            int idx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                            int aboveIdx = GeodesicSupportDijkstraJob.GridIndex(x, y + 1, z, sizeX, sizeY, sizeZ, layout);
                            bool cellEmpty = IsCavityCellEmpty(voxels, ioUnsupportedOrCrushedMask, groundedTimberMask, idx);
                            bool aboveSolid = VoxelBitUtility.IsPhysicallySolid(voxels[aboveIdx]) && ioUnsupportedOrCrushedMask[aboveIdx] != 1;
                            isAirUnderSolid = cellEmpty && aboveSolid;
                        }

                        if (isAirUnderSolid)
                        {
                            if (currentSpan == 0) spanStartZ = z;
                            currentSpan++;
                        }
                        else if (currentSpan > 0)
                        {
                            loosenedTotal += ApplyTerzaghiDomeAlongLine(
                                voxels, geodesicDistance, spanStartZ, currentSpan, y, fixedCoord: x, alongX: false,
                                sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout, allowBoundaryOpenSpan,
                                groundedTimberMask, hasGroundedTimber ? timberShoredCeiling : null);
                            currentSpan = 0;
                        }
                    }
                }
            }

            return loosenedTotal;
        }

        private static bool IsCavityCellEmpty(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int idx)
        {
            if (groundedTimberMask.Length > idx && groundedTimberMask[idx] != 0)
            {
                return false;
            }

            return !VoxelBitUtility.IsPhysicallySolid(voxels[idx]) || failedMask[idx] == 1;
        }

        private static bool IsIntactSupportCell(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int idx)
        {
            if (groundedTimberMask.Length > idx && groundedTimberMask[idx] != 0)
            {
                return true;
            }

            return VoxelBitUtility.IsPhysicallySolid(voxels[idx]) && failedMask[idx] != 1;
        }

        /// <summary>
        /// Zemine gercekten basan ahsap maden tahkimat direklerinin / kirislerinin (groundedTimberMask != 0)
        /// temas ettigi tavan kayasi etrafindaki gercek tasima etki alanini (dx^2 + dz^2 &lt;= W_crit^2) O(1) sorgu haritasina isler.
        /// Havadaki ahsap parcalar groundedTimberMask == 0 oldugu icin sifir destek verir.
        /// </summary>
        private static bool BuildTimberShoredCeilingMap(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            out byte[]? outMap)
        {
            int totalVoxels = sizeX * sizeY * sizeZ;
            bool anyShoringCap = false;
            byte[]? map = null;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 1; y < sizeY - 1; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int tIdx = GeodesicSupportDijkstraJob.GridIndex(x, y, z, sizeX, sizeY, sizeZ, layout);
                        if (groundedTimberMask[tIdx] == 0)
                        {
                            continue;
                        }

                        int ceilIdx = GeodesicSupportDijkstraJob.GridIndex(x, y + 1, z, sizeX, sizeY, sizeZ, layout);
                        uint ceilRaw = voxels[ceilIdx];
                        if (!VoxelBitUtility.IsPhysicallySolid(ceilRaw) || failedMask[ceilIdx] == 1)
                        {
                            continue;
                        }

                        if (!anyShoringCap)
                        {
                            if (_tlsTimberShoredCeiling == null || _tlsTimberShoredCeiling.Length < totalVoxels)
                            {
                                _tlsTimberShoredCeiling = new byte[Math.Max(totalVoxels, 4096)];
                            }
                            map = _tlsTimberShoredCeiling;
                            Array.Clear(map, 0, totalVoxels);
                            anyShoringCap = true;
                        }

                        StructuralMaterialProps ceilProps = StructuralMaterialProps.GetForMaterial(VoxelBitUtility.GetMaterialId(ceilRaw));
                        int radius = ceilProps.IsGranular ? 0 : Math.Clamp(ceilProps.MaxCantileverSpanBlocks, 1, 5);
                        int radiusSq = radius * radius;

                        int minDz = Math.Max(-radius, -z);
                        int maxDz = Math.Min(radius, sizeZ - 1 - z);
                        int minDx = Math.Max(-radius, -x);
                        int maxDx = Math.Min(radius, sizeX - 1 - x);

                        for (int dz = minDz; dz <= maxDz; dz++)
                        {
                            int nz = z + dz;
                            for (int dx = minDx; dx <= maxDx; dx++)
                            {
                                if (dx * dx + dz * dz > radiusSq)
                                {
                                    continue;
                                }

                                int nx = x + dx;
                                int targetCeilIdx = GeodesicSupportDijkstraJob.GridIndex(nx, y + 1, nz, sizeX, sizeY, sizeZ, layout);
                                if (!VoxelBitUtility.IsPhysicallySolid(voxels[targetCeilIdx]) || failedMask[targetCeilIdx] == 1)
                                {
                                    continue;
                                }

                                if (HasContinuousSolidCeilingLine(voxels, failedMask, x, z, nx, nz, y + 1, sizeX, sizeY, sizeZ, layout))
                                {
                                    int cavIdx = GeodesicSupportDijkstraJob.GridIndex(nx, y, nz, sizeX, sizeY, sizeZ, layout);
                                    map![cavIdx] = 1;
                                }
                            }
                        }
                    }
                }
            }

            outMap = map;
            return anyShoringCap;
        }

        private static bool HasContinuousSolidCeilingLine(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            int x0,
            int z0,
            int x1,
            int z1,
            int ceilingY,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(z1 - z0));
            if (steps <= 1)
            {
                return true;
            }

            for (int step = 1; step < steps; step++)
            {
                int ix = x0 + ((x1 - x0) * step + (x1 >= x0 ? steps / 2 : -steps / 2)) / steps;
                int iz = z0 + ((z1 - z0) * step + (z1 >= z0 ? steps / 2 : -steps / 2)) / steps;
                int idx = GeodesicSupportDijkstraJob.GridIndex(ix, ceilingY, iz, sizeX, sizeY, sizeZ, layout);
                if (!VoxelBitUtility.IsPhysicallySolid(voxels[idx]) || failedMask[idx] == 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static int ApplyTerzaghiDomeAlongLine(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            int spanStart,
            int spanWidth,
            int cavityY,
            int fixedCoord,
            bool alongX,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout,
            bool allowBoundaryOpenSpan,
            ReadOnlySpan<byte> groundedTimberMask,
            byte[]? timberShoredCeiling)
        {
            int maxCoord = alongX ? sizeX : sizeZ;
            if (cavityY <= 0)
            {
                return 0;
            }
            if (!allowBoundaryOpenSpan && (spanStart <= 0 || (spanStart + spanWidth) >= maxCoord))
            {
                // Acik gokyuzu veya pencere dis siniri boslugu yeralti maden galerisi degildir
                return 0;
            }

            // Eger zemine basan ahsap maden tahkimati varsa, tahkimatli hucreler acikligi gercek alt-acikliklara boler
            if (timberShoredCeiling != null)
            {
                int totalSubLoosened = 0;
                int subStart = -1;
                int spanEnd = spanStart + spanWidth;

                for (int s = spanStart; s <= spanEnd; s++)
                {
                    bool unshoredCell = false;
                    if (s < spanEnd)
                    {
                        int cx = alongX ? s : fixedCoord;
                        int cz = alongX ? fixedCoord : s;
                        int cavIdx = GeodesicSupportDijkstraJob.GridIndex(cx, cavityY, cz, sizeX, sizeY, sizeZ, layout);
                        unshoredCell = timberShoredCeiling[cavIdx] == 0;
                    }

                    if (unshoredCell)
                    {
                        if (subStart < 0) subStart = s;
                    }
                    else if (subStart >= 0)
                    {
                        int subWidth = s - subStart;
                        totalSubLoosened += ApplyTerzaghiDomeToUnshoredSpan(
                            voxels, geodesicDistance, subStart, subWidth, cavityY, fixedCoord, alongX,
                            sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout, groundedTimberMask);
                        subStart = -1;
                    }
                }

                return totalSubLoosened;
            }

            return ApplyTerzaghiDomeToUnshoredSpan(
                voxels, geodesicDistance, spanStart, spanWidth, cavityY, fixedCoord, alongX,
                sizeX, sizeY, sizeZ, ioUnsupportedOrCrushedMask, layout, groundedTimberMask);
        }

        private static int ApplyTerzaghiDomeToUnshoredSpan(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            int spanStart,
            int spanWidth,
            int cavityY,
            int fixedCoord,
            bool alongX,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> ioUnsupportedOrCrushedMask,
            VoxelGridMemoryLayout layout,
            ReadOnlySpan<byte> groundedTimberMask)
        {
            if (spanWidth <= 0)
            {
                return 0;
            }

            int loosenedCount = 0;
            float halfSpan = spanWidth * 0.5f;
            float center = spanStart + halfSpan - 0.5f;

            // Ayni aciklikta kemer kontrolunu malzeme bazinda onbellekle (0 B GC)
            byte checkedArchMat = 0;
            bool checkedArchResult = false;

            for (int s = spanStart; s < spanStart + spanWidth; s++)
            {
                int x = alongX ? s : fixedCoord;
                int z = alongX ? fixedCoord : s;

                // Yalnizca 1 blokluk yariklari degil cok katli magara galerilerini de destekle;
                // yuzen ada altindaki tabansiz gokyuzu bosluklarini haric tutmak icin altta gercek taban ara.
                int floorY = FindSolidFloorBelow(voxels, ioUnsupportedOrCrushedMask, groundedTimberMask, x, cavityY, z, sizeX, sizeY, sizeZ, layout);
                if (floorY < 0)
                {
                    continue;
                }

                int roofCheckIdx = GeodesicSupportDijkstraJob.GridIndex(x, cavityY + 1, z, sizeX, sizeY, sizeZ, layout);
                uint roofRaw = voxels[roofCheckIdx];
                if (!VoxelBitUtility.IsPhysicallySolid(roofRaw) ||
                    VoxelBitUtility.GetOriginState(roofRaw) != BlokKokenDurumu.DogalKutle)
                {
                    continue;
                }

                byte roofMat = VoxelBitUtility.GetMaterialId(roofRaw);
                StructuralMaterialProps props = StructuralMaterialProps.GetForMaterial(roofMat);
                int hArch = props.ComputeTerzaghiArchHeight(spanWidth);
                if (hArch <= 0)
                {
                    continue;
                }

                // 1) Mevcut eksen boyunca gercek yukari kavislenen kaya kemeri (d^2y/dx^2 < 0) kontrolu:
                if (roofMat != checkedArchMat)
                {
                    checkedArchMat = roofMat;
                    checkedArchResult = IsSpanSupportedByCompressionArch(
                        voxels, geodesicDistance, ioUnsupportedOrCrushedMask, groundedTimberMask,
                        spanStart, spanWidth, cavityY, fixedCoord, alongX,
                        sizeX, sizeY, sizeZ, layout, in props, floorY);
                }

                if (checkedArchResult)
                {
                    continue;
                }

                // 2) 3B Dik Eksen (Tonoz / Dar Koridor) Kemer & Kopruleme Kontrolu:
                // Uzun bir magara koridoru kendi uzun ekseni boyunca degil, dik enine ekseni boyunca kemerlenir/koprulenir!
                if (IsCellSupportedTransversely(
                        voxels, geodesicDistance, ioUnsupportedOrCrushedMask, groundedTimberMask,
                        x, cavityY, z, alongX, sizeX, sizeY, sizeZ, layout, in props, floorY))
                {
                    continue;
                }

                float normDist = halfSpan > 0.5f ? Math.Abs(s - center) / halfSpan : 0f;
                int localDomeHeight = Math.Max(1, (int)MathF.Ceiling(hArch * (1f - normDist * normDist)));

                for (int dy = 1; dy <= localDomeHeight && (cavityY + dy) < sizeY; dy++)
                {
                    int domeIdx = GeodesicSupportDijkstraJob.GridIndex(x, cavityY + dy, z, sizeX, sizeY, sizeZ, layout);
                    uint domeRaw = voxels[domeIdx];
                    if (VoxelBitUtility.IsPhysicallySolid(domeRaw) &&
                        VoxelBitUtility.GetOriginState(domeRaw) == BlokKokenDurumu.DogalKutle &&
                        ioUnsupportedOrCrushedMask[domeIdx] == 0)
                    {
                        ioUnsupportedOrCrushedMask[domeIdx] = 2;
                        loosenedCount++;
                    }
                }
            }

            return loosenedCount;
        }

        private static int FindSolidFloorBelow(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int x,
            int cavityY,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            int minFloorY = Math.Max(0, cavityY - 32);
            for (int fy = cavityY - 1; fy >= minFloorY; fy--)
            {
                int floorCheckIdx = GeodesicSupportDijkstraJob.GridIndex(x, fy, z, sizeX, sizeY, sizeZ, layout);
                if (VoxelBitUtility.IsPhysicallySolid(voxels[floorCheckIdx]) && failedMask[floorCheckIdx] == 0)
                {
                    return fy;
                }
                if (groundedTimberMask.Length > floorCheckIdx && groundedTimberMask[floorCheckIdx] != 0)
                {
                    return fy;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gercek yukari kavislenen kaya kemeri (d^2y / ds^2 &lt; 0) tanimasi:
        /// Tavan taci iki yana dogru basamakli alcap (corbeled/parabolic haunches) zemine oturan gercek duvar/sutun
        /// ayaklarina (H_L + H_R = 0) yaslandiginda basinc yanal ayaklara aktarilir; duz tavan (rise == 0) veya
        /// ayagi kesilmis kemer (abutmentGrounded == false) asla kemer sayilmaz!
        /// </summary>
        private static bool IsSpanSupportedByCompressionArch(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int spanStart,
            int spanWidth,
            int cavityY,
            int fixedCoord,
            bool alongX,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            in StructuralMaterialProps props,
            int referenceFloorY)
        {
            if (!props.IsArchCapable || spanWidth <= 0 || spanWidth > props.MaxClosedArchArmBlocks)
            {
                return false;
            }

            // Kemer tacindaki tum bloklar geodezik olarak kopmamis olmali
            for (int s = spanStart; s < spanStart + spanWidth; s++)
            {
                int cx = alongX ? s : fixedCoord;
                int cz = alongX ? fixedCoord : s;
                int crownIdx = GeodesicSupportDijkstraJob.GridIndex(cx, cavityY + 1, cz, sizeX, sizeY, sizeZ, layout);
                if (geodesicDistance[crownIdx] == GeodesicSupportDijkstraJob.UnreachableDistance)
                {
                    return false;
                }
            }

            int maxCoord = alongX ? sizeX : sizeZ;
            int maxFlankSteps = Math.Min(8, Math.Max(4, props.MaxClosedArchArmBlocks));

            bool leftGrounded = TraceArchFlankToGroundedAbutment(
                voxels, geodesicDistance, failedMask, groundedTimberMask,
                startS: spanStart - 1, stepDir: -1, cavityY, fixedCoord, alongX,
                sizeX, sizeY, sizeZ, maxCoord, layout, maxFlankSteps, referenceFloorY,
                out int archRiseLeft);

            if (!leftGrounded)
            {
                return false;
            }

            bool rightGrounded = TraceArchFlankToGroundedAbutment(
                voxels, geodesicDistance, failedMask, groundedTimberMask,
                startS: spanStart + spanWidth, stepDir: 1, cavityY, fixedCoord, alongX,
                sizeX, sizeY, sizeZ, maxCoord, layout, maxFlankSteps, referenceFloorY,
                out int archRiseRight);

            if (!rightGrounded)
            {
                return false;
            }

            int minRise = Math.Min(archRiseLeft, archRiseRight);
            int totalRise = archRiseLeft + archRiseRight;

            if (spanWidth <= props.MaxCantileverSpanBlocks)
            {
                return minRise >= 1;
            }

            if (spanWidth <= props.MaxCantileverSpanBlocks + 2)
            {
                return minRise >= 1 && totalRise >= 2;
            }

            return minRise >= 2 && totalRise >= 4;
        }

        private static bool TraceArchFlankToGroundedAbutment(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int startS,
            int stepDir,
            int cavityY,
            int fixedCoord,
            bool alongX,
            int sizeX,
            int sizeY,
            int sizeZ,
            int maxCoord,
            VoxelGridMemoryLayout layout,
            int maxFlankSteps,
            int referenceFloorY,
            out int archRise)
        {
            archRise = 0;
            if ((uint)startS >= (uint)maxCoord)
            {
                return false;
            }

            int s = startS;
            int curY = cavityY;
            int flatRun = 0;

            for (int step = 0; step <= maxFlankSteps; step++)
            {
                int x = alongX ? s : fixedCoord;
                int z = alongX ? fixedCoord : s;
                int idx = GeodesicSupportDijkstraJob.GridIndex(x, curY, z, sizeX, sizeY, sizeZ, layout);
                if (!IsIntactSupportCell(voxels, failedMask, groundedTimberMask, idx))
                {
                    return false;
                }

                if (IsColumnGroundedToCavityFloor(
                        voxels, geodesicDistance, failedMask, groundedTimberMask,
                        x, curY, z, referenceFloorY, sizeX, sizeY, sizeZ, layout))
                {
                    return true;
                }

                // Bu blok altinda bosluk bulunan gercek bir kemer omuz/bindirme (corbel) basamagidir (d^2y/ds^2 < 0)
                int drop = (cavityY + 1) - curY;
                if (drop > archRise)
                {
                    archRise = drop;
                }

                int nextS = s + stepDir;
                if ((uint)nextS >= (uint)maxCoord)
                {
                    return false;
                }

                int nx = alongX ? nextS : fixedCoord;
                int nz = alongX ? fixedCoord : nextS;

                if (curY > 0)
                {
                    int diagDownIdx = GeodesicSupportDijkstraJob.GridIndex(nx, curY - 1, nz, sizeX, sizeY, sizeZ, layout);
                    if (IsIntactSupportCell(voxels, failedMask, groundedTimberMask, diagDownIdx))
                    {
                        s = nextS;
                        curY--;
                        flatRun = 0;
                        continue;
                    }
                }

                int sameLevelIdx = GeodesicSupportDijkstraJob.GridIndex(nx, curY, nz, sizeX, sizeY, sizeZ, layout);
                if (flatRun == 0 && IsIntactSupportCell(voxels, failedMask, groundedTimberMask, sameLevelIdx))
                {
                    s = nextS;
                    flatRun = 1;
                    continue;
                }

                return false;
            }

            return false;
        }

        private static bool IsColumnGroundedToCavityFloor(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int x,
            int startY,
            int z,
            int referenceFloorY,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout)
        {
            int startIdx = GeodesicSupportDijkstraJob.GridIndex(x, startY, z, sizeX, sizeY, sizeZ, layout);
            if (geodesicDistance[startIdx] != 0)
            {
                return false;
            }

            int targetFloorY = Math.Max(0, Math.Min(startY - 1, referenceFloorY));
            for (int ty = startY - 1; ty >= targetFloorY; ty--)
            {
                int idx = GeodesicSupportDijkstraJob.GridIndex(x, ty, z, sizeX, sizeY, sizeZ, layout);
                if (!IsIntactSupportCell(voxels, failedMask, groundedTimberMask, idx))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCellSupportedTransversely(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> geodesicDistance,
            ReadOnlySpan<byte> failedMask,
            ReadOnlySpan<byte> groundedTimberMask,
            int x,
            int cavityY,
            int z,
            bool primaryAlongX,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelGridMemoryLayout layout,
            in StructuralMaterialProps props,
            int referenceFloorY)
        {
            bool perpAlongX = !primaryAlongX;
            int perpCoord = perpAlongX ? x : z;
            int perpFixed = perpAlongX ? z : x;
            int perpMax = perpAlongX ? sizeX : sizeZ;

            int pStart = perpCoord;
            while (pStart > 0)
            {
                int prev = pStart - 1;
                int px = perpAlongX ? prev : perpFixed;
                int pz = perpAlongX ? perpFixed : prev;
                int pIdx = GeodesicSupportDijkstraJob.GridIndex(px, cavityY, pz, sizeX, sizeY, sizeZ, layout);
                int pAbove = GeodesicSupportDijkstraJob.GridIndex(px, cavityY + 1, pz, sizeX, sizeY, sizeZ, layout);
                if (IsCavityCellEmpty(voxels, failedMask, groundedTimberMask, pIdx) &&
                    VoxelBitUtility.IsPhysicallySolid(voxels[pAbove]) && failedMask[pAbove] != 1)
                {
                    pStart = prev;
                }
                else
                {
                    break;
                }
            }

            int pEnd = perpCoord;
            while (pEnd + 1 < perpMax)
            {
                int next = pEnd + 1;
                int px = perpAlongX ? next : perpFixed;
                int pz = perpAlongX ? perpFixed : next;
                int pIdx = GeodesicSupportDijkstraJob.GridIndex(px, cavityY, pz, sizeX, sizeY, sizeZ, layout);
                int pAbove = GeodesicSupportDijkstraJob.GridIndex(px, cavityY + 1, pz, sizeX, sizeY, sizeZ, layout);
                if (IsCavityCellEmpty(voxels, failedMask, groundedTimberMask, pIdx) &&
                    VoxelBitUtility.IsPhysicallySolid(voxels[pAbove]) && failedMask[pAbove] != 1)
                {
                    pEnd = next;
                }
                else
                {
                    break;
                }
            }

            int perpWidth = pEnd - pStart + 1;
            int maxFlankSteps = Math.Min(8, Math.Max(4, props.MaxClosedArchArmBlocks));

            // A) Basamakli kemer omuzu / bindirme cikintisi (corbel shoulder) kontrolu:
            // Eger bir kenar kati duvar/kemer ayagina yaslanirken diger kenar daha yuksek bir magara bosluguna aciliyorsa
            // (yani kapali 1-blokluk duz kesit degil, yukselen kemerin 1..W_crit blokluk bindirme omuzuysa) kemer omuzu stabildir!
            if (perpWidth <= props.MaxCantileverSpanBlocks)
            {
                bool leftOpensHigher = false;
                if (pStart > 0)
                {
                    int lx = perpAlongX ? (pStart - 1) : perpFixed;
                    int lz = perpAlongX ? perpFixed : (pStart - 1);
                    int lIdx = GeodesicSupportDijkstraJob.GridIndex(lx, cavityY, lz, sizeX, sizeY, sizeZ, layout);
                    int lAbove = GeodesicSupportDijkstraJob.GridIndex(lx, cavityY + 1, lz, sizeX, sizeY, sizeZ, layout);
                    leftOpensHigher = IsCavityCellEmpty(voxels, failedMask, groundedTimberMask, lIdx) &&
                                      IsCavityCellEmpty(voxels, failedMask, groundedTimberMask, lAbove);
                }

                bool rightOpensHigher = false;
                if (pEnd + 1 < perpMax)
                {
                    int rx = perpAlongX ? (pEnd + 1) : perpFixed;
                    int rz = perpAlongX ? perpFixed : (pEnd + 1);
                    int rIdx = GeodesicSupportDijkstraJob.GridIndex(rx, cavityY, rz, sizeX, sizeY, sizeZ, layout);
                    int rAbove = GeodesicSupportDijkstraJob.GridIndex(rx, cavityY + 1, rz, sizeX, sizeY, sizeZ, layout);
                    rightOpensHigher = IsCavityCellEmpty(voxels, failedMask, groundedTimberMask, rIdx) &&
                                       IsCavityCellEmpty(voxels, failedMask, groundedTimberMask, rAbove);
                }

                if (leftOpensHigher != rightOpensHigher)
                {
                    // Yalnizca karsi kenardaki duvar/kemer ayagi gercekten zemine basiyorsa bu omuz guvenlidir!
                    bool wallSideGrounded = leftOpensHigher
                        ? TraceArchFlankToGroundedAbutment(
                            voxels, geodesicDistance, failedMask, groundedTimberMask,
                            pEnd + 1, 1, cavityY, perpFixed, perpAlongX,
                            sizeX, sizeY, sizeZ, perpMax, layout, maxFlankSteps, referenceFloorY, out _)
                        : TraceArchFlankToGroundedAbutment(
                            voxels, geodesicDistance, failedMask, groundedTimberMask,
                            pStart - 1, -1, cavityY, perpFixed, perpAlongX,
                            sizeX, sizeY, sizeZ, perpMax, layout, maxFlankSteps, referenceFloorY, out _);
                    if (wallSideGrounded)
                    {
                        return true;
                    }
                }
            }

            // B) 1-voksel kalinligindaki kapali 2B duz kesit testleri (perpWidth == 1) uzun eksen Terzaghi testini bastirmaz;
            // gercek 3B magara koridorlari (perpWidth >= 2) ise dik kesitteki dar aciklik veya kemer tonozuyla tasinir!
            if (perpWidth < 2 || pStart <= 0 || pEnd + 1 >= perpMax)
            {
                return false;
            }

            if (perpWidth <= props.MaxCantileverSpanBlocks)
            {
                bool leftOk = TraceArchFlankToGroundedAbutment(
                    voxels, geodesicDistance, failedMask, groundedTimberMask,
                    pStart - 1, -1, cavityY, perpFixed, perpAlongX,
                    sizeX, sizeY, sizeZ, perpMax, layout, maxFlankSteps, referenceFloorY, out _);
                if (!leftOk) return false;

                bool rightOk = TraceArchFlankToGroundedAbutment(
                    voxels, geodesicDistance, failedMask, groundedTimberMask,
                    pEnd + 1, 1, cavityY, perpFixed, perpAlongX,
                    sizeX, sizeY, sizeZ, perpMax, layout, maxFlankSteps, referenceFloorY, out _);
                return rightOk;
            }

            return IsSpanSupportedByCompressionArch(
                voxels, geodesicDistance, failedMask, groundedTimberMask,
                pStart, perpWidth, cavityY, perpFixed, perpAlongX,
                sizeX, sizeY, sizeZ, layout, in props, referenceFloorY);
        }

        private static void EnsureSliceBuckets(int sliceArea, out int[] distHeads, out int[] distNext)
        {
            if (_tlsDistHeads == null || _tlsDistHeads.Length < GeodesicSupportDijkstraJob.MaxDialBuckets)
            {
                _tlsDistHeads = new int[GeodesicSupportDijkstraJob.MaxDialBuckets];
            }

            if (_tlsDistNext == null || _tlsDistNext.Length < sliceArea)
            {
                _tlsDistNext = new int[Math.Max(sliceArea, 1024)];
            }

            distHeads = _tlsDistHeads;
            distNext = _tlsDistNext;
        }
    }
}
