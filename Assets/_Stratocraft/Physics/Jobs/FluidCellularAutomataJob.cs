#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D — Sonlu 16-Bit Akiskan Otomati, Hidrostatik U-Borusu &amp; Sifon (&lt;= 10m)
    /// ve Tek Seferlik Stokiyometrik Obsidyen Kimyasi:
    /// 1. %0 CPU IsSettled Uyku Maskesi: Durgun okyanus ve lav denizleri 0 islem tuketir.
    /// 2. -Y Yercekimi + 4-Gecisli Ayrik Kenar Degisimi (Even-X, Odd-X, Even-Z, Odd-Z): %100 yonsuz ve tam kutle korunumlu!
    /// 3. Su Kohezyonu ve Lav Bingham Akma Esigi (16): Su 1-birim sonsuz titresime girmez, lav gercek magma gibi koyu yayilir.
    /// 4. Iki Katmanli Hidrostatik Basinc (5-Bit PressureHead 0..31) + U-Borusu / &lt;= 10m Sifon dengelemesi!
    /// 5. Anayasa 2.7.D.2 Kesin Kimya Kanunu:
    ///    - V_su &gt;= 128 VE V_lav &gt;= 128 -&gt; 1 adet Dogal Obsidyen (OriginState = DogalKutle = 0), HER IKI SIVI SIFIRLANIR (0, 0)!
    ///    - V_su &lt; 128 VEYA V_lav &lt; 128 -&gt; 0 ADET KATI BLOK (Buhar + Curuf sonmesi), HER IKI SIVI SIFIRLANIR (0, 0)!
    ///    Sonsuz obsidyen, bazalt veya kiriktas jeneratoru fiziksel olarak %100 imkansizdir!
    /// </summary>
    public static class FluidCellularAutomataJob
    {
        public const byte LavaBinghamYieldThreshold = 16;
        public const int MaxSiphonCrestBlocks = 10;

        [ThreadStatic] private static int[]? _tlsVisitedStamp;
        [ThreadStatic] private static int[]? _tlsBfsQueue;
        [ThreadStatic] private static byte[]? _tlsActiveNext;
        [ThreadStatic] private static int _lastStepSiphonTransfers;
        private static int _globalStampCounter = 1;

        public static int LastStepSiphonTransfers => _lastStepSiphonTransfers;

        /// <summary>
        /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D.2 Tek Seferlik Stokiyometrik Su + Lav Reaksiyonu:
        /// - Eger V_su &gt;= 128 ve V_lav &gt;= 128 ise 1 adet NaturalObsidian (OriginState = DogalKutle) olusur ve her iki sivi tamamen tuketilir!
        /// - Eger herhangi biri &lt; 128 ise 0 kati blok olusur (formedSolidBlock = VoxelCell.Air, buharlasma!) ve her iki sivi tamamen tuketilir!
        /// </summary>
        public static bool TryReactWaterAndLava(
            ref FluidCell16 waterCell,
            ref FluidCell16 lavaCell,
            out VoxelCell formedSolidBlock,
            out bool formedObsidian)
        {
            formedSolidBlock = VoxelCell.Air;
            formedObsidian = false;

            if (waterCell.Type != FluidType.Water || lavaCell.Type != FluidType.Lava)
            {
                return false;
            }

            byte waterMass = waterCell.Mass;
            byte lavaMass = lavaCell.Mass;
            if (waterMass == 0 || lavaMass == 0)
            {
                return false;
            }

            byte threshold = StratocraftConstants.StoichiometricFluidThreshold; // 128

            // Her iki durumda da reaksiyona giren iki hucredeki akiskanlar tek seferde tuketilir!
            waterCell = FluidCell16.Empty;
            lavaCell = FluidCell16.Empty;

            if (waterMass >= threshold && lavaMass >= threshold)
            {
                uint obsidianRaw = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.NaturalObsidian,
                    VoxelBitUtility.FullDensity,
                    shapeId: 0,
                    rot24: 0,
                    BlokKokenDurumu.DogalKutle,
                    dmg4: 0);
                formedSolidBlock = new VoxelCell(obsidianRaw);
                formedObsidian = true;
            }

            return true;
        }

        /// <summary>Geriye donuk uyumlu 3 parametreli yardimci.</summary>
        public static bool TryReactWaterAndLava(
            ref FluidCell16 waterCell,
            ref FluidCell16 lavaCell,
            out VoxelCell formedSolidBlock)
        {
            return TryReactWaterAndLava(ref waterCell, ref lavaCell, out formedSolidBlock, out _);
        }

        /// <summary>
        /// 3B izgara uzerinde hacim korunumlu 1 adim akiskan + U-borusu/sifon + stokiyometrik obsidyen simulasyonu calistirir.
        /// <paramref name="steamSlagFizzleCount"/>: &lt; 128 yetersiz hacim nedeniyle kati blok olusturmadan buharlasan temas sayisi.
        /// <paramref name="salinityBits"/>: Opsiyonel bit dizisi (<c>1 bit/hucre</c>); verildiginde akan tuzlu suyun tuzluluk bitini tasir (<see cref="FluidCell16"/> 16-bit duzeni degismez).
        /// </summary>
        public static long StepFluidGridVolumeConservative(
            Span<uint> voxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int stepParity = 0,
            ulong[]? salinityBits = null)
        {
            obsidianFormedCount = 0;
            steamSlagFizzleCount = 0;
            _lastStepSiphonTransfers = 0;

            int totalCells = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalCells || fluids.Length < totalCells)
            {
                throw new ArgumentException("Fluid or voxel buffer smaller than grid dimensions.");
            }

            // Hizli %0 CPU Durgunluk Erken Cikisi + Aktif Y-Dilimi Penceresi [minFluidY..maxFluidY]:
            // Hic aktif (IsSettled == false) sivi hucresi yoksa yalnizca kutle topla ve cik!
            bool anyActive = false;
            long fastMassSum = 0;
            int strideY = sizeZ * sizeX;
            int minFluidY = sizeY - 1;
            int maxFluidY = 0;
            for (int i = 0; i < totalCells; i++)
            {
                ushort raw = fluids[i];
                int m = raw & 0xFF;
                if (m > 0)
                {
                    fastMassSum += m;
                    int cy = i / strideY;
                    if (cy < minFluidY) minFluidY = cy;
                    if (cy > maxFluidY) maxFluidY = cy;
                    if ((raw & FluidCell16.SettledBitMask) == 0)
                    {
                        anyActive = true;
                    }
                }
            }

            if (!anyActive)
            {
                return fastMassSum;
            }

            EnsureScratch(totalCells, out int[] visitedStamp, out int[] bfsQueue, out byte[] changedMask);
            Array.Clear(changedMask, 0, totalCells);

            int startY = Math.Max(1, minFluidY);
            int endY = Math.Min(sizeY - 1, maxFluidY + 1);

            const byte NewlyWetFromGravityFlag = 0x08;

            // 1. Gecis: -Y Yercekimi Akisi ve Dikey Su+Lav Temas Kimyasi
            for (int y = startY; y <= endY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = y * strideY + z * sizeX + x;
                        FluidCell16 cell = new FluidCell16(fluids[idx]);
                        if (cell.Mass == 0 || cell.Type == FluidType.None)
                        {
                            continue;
                        }

                        int belowIdx = idx - strideY;
                        FluidCell16 belowCell = new FluidCell16(fluids[belowIdx]);
                        if (cell.IsSettled && belowCell.IsSettled)
                        {
                            continue;
                        }

                        byte belowCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[belowIdx]);
                        if (belowCap == 0)
                        {
                            continue;
                        }

                        if (belowCell.Type == FluidType.None || belowCell.Type == cell.Type)
                        {
                            int space = belowCap - belowCell.Mass;
                            if (space > 0)
                            {
                                bool belowWasDry = belowCell.Mass == 0;
                                int maxRate = cell.Type == FluidType.Lava ? Math.Min((int)cell.Mass, 64) : cell.Mass;
                                int transfer = Math.Min(space, maxRate);
                                if (transfer > 0)
                                {
                                    fluids[belowIdx] = FluidCell16.Pack(cell.Type, (byte)(belowCell.Mass + transfer), isSettled: false).RawValue;
                                    byte rem = (byte)(cell.Mass - transfer);
                                    fluids[idx] = FluidCell16.Pack(cell.Type, rem, isSettled: false).RawValue;
                                    PropagateGridSalinityOnTransfer(salinityBits, cell.Type, idx, rem, belowIdx, belowWasDry);
                                    MarkChangedWithNeighbors(changedMask, x, y, z, sizeX, sizeY, sizeZ);
                                    MarkChangedWithNeighbors(changedMask, x, y - 1, z, sizeX, sizeY, sizeZ);
                                    if (belowWasDry)
                                    {
                                        // Split-Tick Cascade: Bu tikte yukaridan yeni dusen su ayni tikte yatay sicramaz;
                                        // boylece selale/basamak inisi gozle gorulur kademeli ilerler!
                                        changedMask[belowIdx] |= NewlyWetFromGravityFlag;
                                    }
                                }
                            }
                        }
                        else if (IsWaterLavaPair(cell.Type, belowCell.Type))
                        {
                            ExecuteCellPairReaction(
                                voxels, fluids, changedMask,
                                idx, x, y, z,
                                belowIdx, x, y - 1, z,
                                reactionTargetIsSecond: true,
                                sizeX, sizeY, sizeZ,
                                ref obsidianFormedCount,
                                ref steamSlagFizzleCount,
                                salinityBits);
                        }
                    }
                }
            }

            // 2. Gecis: 4-Asamali Ayrik Kenar Degisimi (Disjoint Pairwise Edge Exchange: Even-X, Odd-X, Even-Z, Odd-Z)
            // Adim paritesine gore sira dondurulerek %0 yonlu sapma (bias-free isotropy) garanti edilir!
            for (int pass = 0; pass < 4; pass++)
            {
                int mode = (pass + (stepParity & 3)) & 3;
                switch (mode)
                {
                    case 0: // Even-X: (0,1), (2,3), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: true, oddOffset: 0, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits);
                        break;
                    case 1: // Odd-X: (1,2), (3,4), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: true, oddOffset: 1, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits);
                        break;
                    case 2: // Even-Z: (0,1), (2,3), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: false, oddOffset: 0, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits);
                        break;
                    default: // Odd-Z: (1,2), (3,4), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: false, oddOffset: 1, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits);
                        break;
                }
            }

            // 3. Gecis: 5-Bit Hidrostatik Basinc Yuksekligi (PressureHead) + U-Borusu & <= 10m Sifon Dengeleme!
            UpdateHydrostaticHeadAndSolveUPipes(
                voxels, fluids, changedMask, visitedStamp, bfsQueue, sizeX, sizeY, sizeZ, salinityBits);

            // 4. Gecis: Degismeyen durgun hucreleri IsSettled = 1 (%0 CPU) uykusuna yatir ve toplam kutleyi don
            long totalRemainingMass = 0;
            for (int i = 0; i < totalCells; i++)
            {
                ushort raw = fluids[i];
                int m = raw & 0xFF;
                if (m == 0)
                {
                    fluids[i] = 0;
                    if (salinityBits != null)
                    {
                        SetGridSalinityBit(salinityBits, i, false);
                    }
                    continue;
                }

                totalRemainingMass += m;
                if (changedMask[i] == 0)
                {
                    fluids[i] = FluidCell16.SettleRaw(raw);
                }
                else
                {
                    fluids[i] = FluidCell16.WakeRaw(raw);
                }
            }

            return totalRemainingMass;
        }

        /// <summary>
        /// Su carki (Waterwheel) mekanik tork hesaplayicisi:
        /// tau_water = rho_fluid * Q_flux * v_eff * R_wheel (N*m).
        /// </summary>
        public static float ComputeWaterwheelTorqueNm(
            FluidType fluidType,
            int massUnitsTransferredPerTick,
            float wheelRadiusMeters = 1.5f,
            float tickRateHz = 20.0f)
        {
            if (massUnitsTransferredPerTick <= 0 || fluidType == FluidType.None)
            {
                return 0f;
            }

            float densityKgPerM3 = fluidType == FluidType.Lava ? 2800f : 1000f;
            float volumetricFlowM3PerSec = (massUnitsTransferredPerTick / 255.0f) * tickRateHz;
            float effectiveJetVelocity = MathF.Min(6.0f, 0.75f + volumetricFlowM3PerSec * 1.4f);
            return densityKgPerM3 * volumetricFlowM3PerSec * effectiveJetVelocity * MathF.Max(0.25f, wheelRadiusMeters) * 0.05f;
        }

        /// <summary>
        /// Komşu chunk sınırlarında (lx = 15 &lt;-&gt; 0 ve lz = 15 &lt;-&gt; 0) su, lav ve asit akışkanlarının
        /// görünmez duvara çarpmadan 16m chunk sınırlarını kesintisiz geçmesini ve sınırda stokiyometrik
        /// su+lav reaksiyonuna girmesini sağlar.
        /// Eğer <paramref name="activeChunks"/> verilirse tüm kıtayı (560+ chunk) taramak yerine YALNIZCA
        /// aktif akışkan bulunan chunk'ların 4 sınırını O(AktifChunk) sürede tarar.
        /// </summary>
        public static int ExchangeChunkBorderFluids(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            int maxY,
            HashSet<(int cx, int cz)> dirtyFluidChunks,
            HashSet<(int cx, int cz)>? dirtySolidChunks = null,
            IReadOnlyList<(int cx, int cz)>? activeChunks = null,
            HashSet<(int cx, int cz)>? wakeActiveFluidChunks = null,
            ChunkSalinityBitset? salinityBitset = null)
        {
            if (worldChunks == null || fluidChunks == null || fluidChunks.Count == 0)
            {
                return 0;
            }

            int exchangedBorders = 0;

            if (activeChunks != null && activeChunks.Count > 0)
            {
                for (int i = 0; i < activeChunks.Count; i++)
                {
                    (int cx, int cz) keyA = activeChunks[i];
                    if (!fluidChunks.TryGetValue(keyA, out ushort[]? fluidsA) || fluidsA == null ||
                        !worldChunks.TryGetValue(keyA, out uint[]? voxelsA) || voxelsA == null)
                    {
                        continue;
                    }

                    // +X Sınırı: keyA (lx = 15) <-> (cx + 1, cz) (lx = 0)
                    (int cx, int cz) keyEast = (keyA.cx + 1, keyA.cz);
                    if (fluidChunks.TryGetValue(keyEast, out ushort[]? fluidsEast) && fluidsEast != null &&
                        worldChunks.TryGetValue(keyEast, out uint[]? voxelsEast) && voxelsEast != null)
                    {
                        exchangedBorders += ExchangeSingleChunkSeam(
                            keyA, voxelsA, fluidsA,
                            keyEast, voxelsEast, fluidsEast,
                            alongXSeam: true,
                            maxY,
                            dirtyFluidChunks,
                            dirtySolidChunks,
                            wakeActiveFluidChunks,
                            salinityBitset);
                    }

                    // -X Sınırı: (cx - 1, cz) (lx = 15) <-> keyA (lx = 0)
                    (int cx, int cz) keyWest = (keyA.cx - 1, keyA.cz);
                    if (fluidChunks.TryGetValue(keyWest, out ushort[]? fluidsWest) && fluidsWest != null &&
                        worldChunks.TryGetValue(keyWest, out uint[]? voxelsWest) && voxelsWest != null)
                    {
                        exchangedBorders += ExchangeSingleChunkSeam(
                            keyWest, voxelsWest, fluidsWest,
                            keyA, voxelsA, fluidsA,
                            alongXSeam: true,
                            maxY,
                            dirtyFluidChunks,
                            dirtySolidChunks,
                            wakeActiveFluidChunks,
                            salinityBitset);
                    }

                    // +Z Sınırı: keyA (lz = 15) <-> (cx, cz + 1) (lz = 0)
                    (int cx, int cz) keyNorth = (keyA.cx, keyA.cz + 1);
                    if (fluidChunks.TryGetValue(keyNorth, out ushort[]? fluidsNorth) && fluidsNorth != null &&
                        worldChunks.TryGetValue(keyNorth, out uint[]? voxelsNorth) && voxelsNorth != null)
                    {
                        exchangedBorders += ExchangeSingleChunkSeam(
                            keyA, voxelsA, fluidsA,
                            keyNorth, voxelsNorth, fluidsNorth,
                            alongXSeam: false,
                            maxY,
                            dirtyFluidChunks,
                            dirtySolidChunks,
                            wakeActiveFluidChunks,
                            salinityBitset);
                    }

                    // -Z Sınırı: (cx, cz - 1) (lz = 15) <-> keyA (lz = 0)
                    (int cx, int cz) keySouth = (keyA.cx, keyA.cz - 1);
                    if (fluidChunks.TryGetValue(keySouth, out ushort[]? fluidsSouth) && fluidsSouth != null &&
                        worldChunks.TryGetValue(keySouth, out uint[]? voxelsSouth) && voxelsSouth != null)
                    {
                        exchangedBorders += ExchangeSingleChunkSeam(
                            keySouth, voxelsSouth, fluidsSouth,
                            keyA, voxelsA, fluidsA,
                            alongXSeam: false,
                            maxY,
                            dirtyFluidChunks,
                            dirtySolidChunks,
                            wakeActiveFluidChunks,
                            salinityBitset);
                    }
                }

                return exchangedBorders;
            }

            foreach (KeyValuePair<(int cx, int cz), ushort[]> kv in fluidChunks)
            {
                (int cx, int cz) keyA = kv.Key;
                ushort[] fluidsA = kv.Value;
                if (fluidsA == null || !worldChunks.TryGetValue(keyA, out uint[]? voxelsA) || voxelsA == null)
                {
                    continue;
                }

                // +X Sınırı: keyA (lx = 15) <-> (cx + 1, cz) (lx = 0)
                (int cx, int cz) keyEast = (keyA.cx + 1, keyA.cz);
                if (fluidChunks.TryGetValue(keyEast, out ushort[]? fluidsEast) &&
                    fluidsEast != null &&
                    worldChunks.TryGetValue(keyEast, out uint[]? voxelsEast) &&
                    voxelsEast != null)
                {
                    exchangedBorders += ExchangeSingleChunkSeam(
                        keyA, voxelsA, fluidsA,
                        keyEast, voxelsEast, fluidsEast,
                        alongXSeam: true,
                        maxY,
                        dirtyFluidChunks,
                        dirtySolidChunks,
                        wakeActiveFluidChunks,
                        salinityBitset);
                }

                // +Z Sınırı: keyA (lz = 15) <-> (cx, cz + 1) (lz = 0)
                (int cx, int cz) keyNorth = (keyA.cx, keyA.cz + 1);
                if (fluidChunks.TryGetValue(keyNorth, out ushort[]? fluidsNorth) &&
                    fluidsNorth != null &&
                    worldChunks.TryGetValue(keyNorth, out uint[]? voxelsNorth) &&
                    voxelsNorth != null)
                {
                    exchangedBorders += ExchangeSingleChunkSeam(
                        keyA, voxelsA, fluidsA,
                        keyNorth, voxelsNorth, fluidsNorth,
                        alongXSeam: false,
                        maxY,
                        dirtyFluidChunks,
                        dirtySolidChunks,
                        wakeActiveFluidChunks,
                        salinityBitset);
                }
            }

            return exchangedBorders;
        }

        private static int ExchangeSingleChunkSeam(
            (int cx, int cz) keyA,
            uint[] voxelsA,
            ushort[] fluidsA,
            (int cx, int cz) keyB,
            uint[] voxelsB,
            ushort[] fluidsB,
            bool alongXSeam,
            int maxY,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            HashSet<(int cx, int cz)>? dirtySolidChunks,
            HashSet<(int cx, int cz)>? wakeActiveFluidChunks = null,
            ChunkSalinityBitset? salinityBitset = null)
        {
            int limitY = Math.Min(maxY, Math.Min(fluidsA.Length >> 8, fluidsB.Length >> 8));
            limitY = Math.Min(limitY, Math.Min(voxelsA.Length >> 8, voxelsB.Length >> 8));
            if (limitY <= 0)
            {
                return 0;
            }

            int seamTransfers = 0;
            bool dirtyFluidA = false;
            bool dirtyFluidB = false;
            bool dirtySolidA = false;
            bool dirtySolidB = false;

            for (int y = 0; y < limitY; y++)
            {
                int yBase = y << 8;
                for (int s = 0; s < 16; s++)
                {
                    int ax = alongXSeam ? 15 : s;
                    int az = alongXSeam ? s : 15;
                    int bx = alongXSeam ? 0 : s;
                    int bz = alongXSeam ? s : 0;

                    int idxA = yBase | (az << 4) | ax;
                    int idxB = yBase | (bz << 4) | bx;

                    FluidCell16 aCell = new FluidCell16(fluidsA[idxA]);
                    FluidCell16 bCell = new FluidCell16(fluidsB[idxB]);
                    if (aCell.Mass == 0 && bCell.Mass == 0)
                    {
                        continue;
                    }

                    byte aCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsA[idxA]);
                    byte bCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsB[idxB]);
                    if (aCap == 0 || bCap == 0)
                    {
                        continue;
                    }

                    // 1) Sınır ötesi Su + Lav reaksiyonu
                    if (aCell.Type != FluidType.None && bCell.Type != FluidType.None && aCell.Type != bCell.Type)
                    {
                        if (IsWaterLavaPair(aCell.Type, bCell.Type))
                        {
                            FluidCell16 water = aCell.Type == FluidType.Water ? aCell : bCell;
                            FluidCell16 lava = aCell.Type == FluidType.Lava ? aCell : bCell;
                            if (TryReactWaterAndLava(ref water, ref lava, out VoxelCell formedSolid, out bool formedObsidian))
                            {
                                fluidsA[idxA] = 0;
                                fluidsB[idxB] = 0;
                                salinityBitset?.SetSaltWaterFlat(keyA.cx, keyA.cz, idxA, false);
                                salinityBitset?.SetSaltWaterFlat(keyB.cx, keyB.cz, idxB, false);
                                if (formedObsidian)
                                {
                                    if (bCell.Type == FluidType.Lava)
                                    {
                                        voxelsB[idxB] = formedSolid.RawValue;
                                        dirtySolidB = true;
                                    }
                                    else
                                    {
                                        voxelsA[idxA] = formedSolid.RawValue;
                                        dirtySolidA = true;
                                    }
                                }

                                WakeChunkCellAndNeighbors(fluidsA, ax, y, az, limitY);
                                WakeChunkCellAndNeighbors(fluidsB, bx, y, bz, limitY);
                                dirtyFluidA = true;
                                dirtyFluidB = true;
                                seamTransfers++;
                            }
                        }
                        continue;
                    }

                    // 2) Sınır ötesi yanal (H = b + h sanal boru ve -Y düşüş destekli) kütle korunumlu akış
                    FluidType activeType = aCell.Mass > 0 ? aCell.Type : bCell.Type;
                    if (activeType == FluidType.None)
                    {
                        continue;
                    }

                    uint belowSolidA = y > 0 ? voxelsA[idxA - 256] : 0u;
                    uint belowSolidB = y > 0 ? voxelsB[idxB - 256] : 0u;
                    int bedA = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowSolidA, voxelsA[idxA], y);
                    int bedB = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowSolidB, voxelsB[idxB], y);
                    int headA = bedA + aCell.Mass;
                    int headB = bedB + bCell.Mass;

                    if (aCell.Mass > 0 && headA > headB)
                    {
                        // Durgun gol sinirinda 1-2 birimlik mikro seviye farkinin iki chunk'i sonsuza dek uyanik tutmasini onle
                        if (bedA <= bedB && (headA - headB) < 3)
                        {
                            continue;
                        }

                        int transfer = ComputeStableLateralTransfer(
                            activeType, aCell.Mass, bCell.Mass, bCap, bedA, bedB);
                        if (transfer > 0)
                        {
                            byte remA = (byte)(aCell.Mass - transfer);
                            fluidsA[idxA] = FluidCell16.Pack(activeType, remA, isSettled: false, aCell.PressureHead).RawValue;
                            fluidsB[idxB] = FluidCell16.Pack(activeType, (byte)(bCell.Mass + transfer), isSettled: false, bCell.PressureHead).RawValue;
                            PropagateSeamSalinityOnTransfer(salinityBitset, activeType, keyA, idxA, remA, keyB, idxB, bCell.Mass == 0);
                            WakeChunkCellAndNeighbors(fluidsA, ax, y, az, limitY);
                            WakeChunkCellAndNeighbors(fluidsB, bx, y, bz, limitY);
                            dirtyFluidA = true;
                            dirtyFluidB = true;
                            seamTransfers++;
                        }
                    }
                    else if (bCell.Mass > 0 && headB > headA)
                    {
                        if (bedB <= bedA && (headB - headA) < 3)
                        {
                            continue;
                        }

                        int transfer = ComputeStableLateralTransfer(
                            activeType, bCell.Mass, aCell.Mass, aCap, bedB, bedA);
                        if (transfer > 0)
                        {
                            byte remB = (byte)(bCell.Mass - transfer);
                            fluidsB[idxB] = FluidCell16.Pack(activeType, remB, isSettled: false, bCell.PressureHead).RawValue;
                            fluidsA[idxA] = FluidCell16.Pack(activeType, (byte)(aCell.Mass + transfer), isSettled: false, aCell.PressureHead).RawValue;
                            PropagateSeamSalinityOnTransfer(salinityBitset, activeType, keyB, idxB, remB, keyA, idxA, aCell.Mass == 0);
                            WakeChunkCellAndNeighbors(fluidsA, ax, y, az, limitY);
                            WakeChunkCellAndNeighbors(fluidsB, bx, y, bz, limitY);
                            dirtyFluidA = true;
                            dirtyFluidB = true;
                            seamTransfers++;
                        }
                    }
                }
            }

            if (dirtyFluidChunks != null)
            {
                if (dirtyFluidA) dirtyFluidChunks.Add(keyA);
                if (dirtyFluidB) dirtyFluidChunks.Add(keyB);
            }

            if (wakeActiveFluidChunks != null)
            {
                if (dirtyFluidA) wakeActiveFluidChunks.Add(keyA);
                if (dirtyFluidB) wakeActiveFluidChunks.Add(keyB);
            }

            if (dirtySolidChunks != null)
            {
                if (dirtySolidA) dirtySolidChunks.Add(keyA);
                if (dirtySolidB) dirtySolidChunks.Add(keyB);
            }

            return seamTransfers;
        }

        private static void WakeChunkCellAndNeighbors(ushort[] fluids, int lx, int ly, int lz, int limitY)
        {
            int idx = (ly << 8) | (lz << 4) | lx;
            if ((uint)idx < (uint)fluids.Length && (fluids[idx] & 0xFF) != 0)
            {
                fluids[idx] = FluidCell16.WakeRaw(fluids[idx]);
            }

            if (lx > 0)
            {
                int n = idx - 1;
                if ((fluids[n] & 0xFF) != 0) fluids[n] = FluidCell16.WakeRaw(fluids[n]);
            }
            if (lx < 15)
            {
                int n = idx + 1;
                if ((fluids[n] & 0xFF) != 0) fluids[n] = FluidCell16.WakeRaw(fluids[n]);
            }
            if (lz > 0)
            {
                int n = idx - 16;
                if ((fluids[n] & 0xFF) != 0) fluids[n] = FluidCell16.WakeRaw(fluids[n]);
            }
            if (lz < 15)
            {
                int n = idx + 16;
                if ((fluids[n] & 0xFF) != 0) fluids[n] = FluidCell16.WakeRaw(fluids[n]);
            }
            if (ly > 0)
            {
                int n = idx - 256;
                if ((fluids[n] & 0xFF) != 0) fluids[n] = FluidCell16.WakeRaw(fluids[n]);
            }
            if (ly + 1 < limitY)
            {
                int n = idx + 256;
                if ((uint)n < (uint)fluids.Length && (fluids[n] & 0xFF) != 0) fluids[n] = FluidCell16.WakeRaw(fluids[n]);
            }
        }

        private static void RunLateralPairPass(
            Span<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int sizeX,
            int sizeY,
            int sizeZ,
            bool alongX,
            int oddOffset,
            ref int obsidianFormedCount,
            ref int steamSlagFizzleCount,
            ulong[]? salinityBits = null)
        {
            int strideY = sizeZ * sizeX;
            int primaryLimit = alongX ? sizeX - 1 : sizeZ - 1;
            int secondaryLimit = alongX ? sizeZ : sizeX;

            for (int y = 0; y < sizeY; y++)
            {
                int yBase = y * strideY;
                for (int sec = 0; sec < secondaryLimit; sec++)
                {
                    for (int pri = oddOffset; pri < primaryLimit; pri += 2)
                    {
                        int ax = alongX ? pri : sec;
                        int az = alongX ? sec : pri;
                        int bx = alongX ? pri + 1 : sec;
                        int bz = alongX ? sec : pri + 1;

                        int aIdx = yBase + az * sizeX + ax;
                        int bIdx = yBase + bz * sizeX + bx;

                        FluidCell16 aCell = new FluidCell16(fluids[aIdx]);
                        FluidCell16 bCell = new FluidCell16(fluids[bIdx]);
                        if (aCell.Mass == 0 && bCell.Mass == 0)
                        {
                            continue;
                        }

                        if (aCell.IsSettled && bCell.IsSettled && changedMask[aIdx] == 0 && changedMask[bIdx] == 0)
                        {
                            continue;
                        }

                        byte aCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[aIdx]);
                        byte bCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[bIdx]);
                        if (aCap == 0 || bCap == 0)
                        {
                            continue;
                        }

                        if (aCell.Type != FluidType.None && bCell.Type != FluidType.None && aCell.Type != bCell.Type)
                        {
                            if (IsWaterLavaPair(aCell.Type, bCell.Type))
                            {
                                bool lavaIsB = bCell.Type == FluidType.Lava;
                                ExecuteCellPairReaction(
                                    voxels, fluids, changedMask,
                                    aIdx, ax, y, az,
                                    bIdx, bx, y, bz,
                                    reactionTargetIsSecond: lavaIsB,
                                    sizeX, sizeY, sizeZ,
                                    ref obsidianFormedCount,
                                    ref steamSlagFizzleCount,
                                    salinityBits);
                            }
                            continue;
                        }

                        FluidType activeType = aCell.Mass > 0 ? aCell.Type : bCell.Type;
                        if (activeType == FluidType.None)
                        {
                            continue;
                        }

                        uint belowA = y > 0 ? voxels[aIdx - strideY] : 0u;
                        uint belowB = y > 0 ? voxels[bIdx - strideY] : 0u;
                        int bedA = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowA, voxels[aIdx], y);
                        int bedB = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowB, voxels[bIdx], y);
                        int headA = bedA + aCell.Mass;
                        int headB = bedB + bCell.Mass;

                        byte axisNewlyWetFlag = alongX ? (byte)0x02 : (byte)0x04;
                        const byte gravityNewlyWetMask = 0x08;

                        if (aCell.Mass > 0 && headA > headB)
                        {
                            // CFL <= 1 Hucre/Tick + Split-Tick Cascade: Bu tick icinde yeni islanan veya yukaridan yeni dusen hucre ayni tick'te ileri sicrayamaz!
                            if ((changedMask[aIdx] & (axisNewlyWetFlag | gravityNewlyWetMask)) != 0)
                            {
                                continue;
                            }

                            EvaluateDonorSlopeRouting(
                                voxels, fluids, ax, y, az, bedA, headA, sizeX, strideY,
                                bx, bz, bedB, out bool hasSteeperAlternativeA, out int slopeShare256A);
                            if (slopeShare256A <= 0)
                            {
                                continue;
                            }

                            int transfer = ComputeStableLateralTransfer(
                                activeType, aCell.Mass, bCell.Mass, bCap, bedA, bedB,
                                hasSteeperAlternativeA, slopeShare256A);
                            if (transfer > 0)
                            {
                                bool bWasDry = bCell.Mass == 0;
                                byte remA = (byte)(aCell.Mass - transfer);
                                fluids[aIdx] = FluidCell16.Pack(activeType, remA, isSettled: false).RawValue;
                                fluids[bIdx] = FluidCell16.Pack(activeType, (byte)(bCell.Mass + transfer), isSettled: false).RawValue;
                                PropagateGridSalinityOnTransfer(salinityBits, activeType, aIdx, remA, bIdx, bWasDry);
                                MarkChangedWithNeighbors(changedMask, ax, y, az, sizeX, sizeY, sizeZ);
                                MarkChangedWithNeighbors(changedMask, bx, y, bz, sizeX, sizeY, sizeZ);
                                if (bWasDry)
                                {
                                    changedMask[bIdx] |= axisNewlyWetFlag;
                                }
                            }
                        }
                        else if (bCell.Mass > 0 && headB > headA)
                        {
                            // CFL <= 1 Hucre/Tick + Split-Tick Cascade: Bu tick icinde yeni islanan veya yukaridan yeni dusen hucre ayni tick'te ileri sicrayamaz!
                            if ((changedMask[bIdx] & (axisNewlyWetFlag | gravityNewlyWetMask)) != 0)
                            {
                                continue;
                            }

                            EvaluateDonorSlopeRouting(
                                voxels, fluids, bx, y, bz, bedB, headB, sizeX, strideY,
                                ax, az, bedA, out bool hasSteeperAlternativeB, out int slopeShare256B);
                            if (slopeShare256B <= 0)
                            {
                                continue;
                            }

                            int transfer = ComputeStableLateralTransfer(
                                activeType, bCell.Mass, aCell.Mass, aCap, bedB, bedA,
                                hasSteeperAlternativeB, slopeShare256B);
                            if (transfer > 0)
                            {
                                bool aWasDry = aCell.Mass == 0;
                                byte remB = (byte)(bCell.Mass - transfer);
                                fluids[bIdx] = FluidCell16.Pack(activeType, remB, isSettled: false).RawValue;
                                fluids[aIdx] = FluidCell16.Pack(activeType, (byte)(aCell.Mass + transfer), isSettled: false).RawValue;
                                PropagateGridSalinityOnTransfer(salinityBits, activeType, bIdx, remB, aIdx, aWasDry);
                                MarkChangedWithNeighbors(changedMask, ax, y, az, sizeX, sizeY, sizeZ);
                                MarkChangedWithNeighbors(changedMask, bx, y, bz, sizeX, sizeY, sizeZ);
                                if (aWasDry)
                                {
                                    changedMask[aIdx] |= axisNewlyWetFlag;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Quinn MFD (Multi-Flow Direction) yamac onceligi:
        /// Su koyuldugunda once cok hafif bir yastik yayilimi yapar, fakat acik bir yokus asagi (bedDrop &gt; 6) cikisi varsa
        /// sel gibi 4 yone patlamak yerine akisinin buyuk kismini en dik yokus asagi komsuya yonlendirir.
        /// </summary>
        private static void EvaluateDonorSlopeRouting(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            int sx,
            int y,
            int sz,
            int bedSrc,
            int headSrc,
            int sizeX,
            int strideY,
            int targetX,
            int targetZ,
            int bedTarget,
            out bool hasSteeperAlternative,
            out int slopeShare256)
        {
            hasSteeperAlternative = false;
            slopeShare256 = 256;

            int sizeZ = strideY / sizeX;
            int yBase = y * strideY;
            int srcIdx = yBase + sz * sizeX + sx;
            byte srcMass = (byte)(fluids[srcIdx] & 0xFF);

            int maxDownhillDrop = 0;
            int sumWeights = 0;
            int targetWeight = 0;

            ReadOnlySpan<int> ndx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> ndz = stackalloc int[4] { 0, 0, 1, -1 };

            // 1) En dik acik yokus asagi komsuyu tespit et
            for (int d = 0; d < 4; d++)
            {
                int nx = sx + ndx[d];
                int nz = sz + ndz[d];
                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ)
                {
                    continue;
                }

                int nIdx = yBase + nz * sizeX + nx;
                byte nCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[nIdx]);
                byte nMass = (byte)(fluids[nIdx] & 0xFF);
                if (nCap <= nMass)
                {
                    continue;
                }

                uint belowN = y > 0 ? voxels[nIdx - strideY] : 0u;
                int bedN = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowN, voxels[nIdx], y);
                int headN = bedN + nMass;
                if (headN >= headSrc)
                {
                    continue;
                }

                int drop = bedSrc - bedN;
                if (drop > maxDownhillDrop)
                {
                    maxDownhillDrop = drop;
                }
            }

            if (maxDownhillDrop <= 6)
            {
                // Duz zemin veya durgun canak: normal esit yanal dengeleme (256/256)
                return;
            }

            int targetDrop = bedSrc - bedTarget;
            if (targetDrop + 6 < maxDownhillDrop)
            {
                hasSteeperAlternative = true;
                // Yamacta baska bir komsu acikca yokus asagiyken, duz/yan komsuya yalnizca ilk dokulme aninda (srcMass >= 135)
                // cok hafif bir dogal yastik yayilimi ver; su inceldikce (%100 yokus asagi kanalize ol!) yanlara dagilmayi kes!
                if (srcMass < 135)
                {
                    slopeShare256 = 0;
                    return;
                }
            }

            // 2) Yokus agirlikli pay hesapla
            for (int d = 0; d < 4; d++)
            {
                int nx = sx + ndx[d];
                int nz = sz + ndz[d];
                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ)
                {
                    continue;
                }

                int nIdx = yBase + nz * sizeX + nx;
                byte nCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[nIdx]);
                byte nMass = (byte)(fluids[nIdx] & 0xFF);
                if (nCap <= nMass)
                {
                    continue;
                }

                uint belowN = y > 0 ? voxels[nIdx - strideY] : 0u;
                int bedN = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowN, voxels[nIdx], y);
                int headN = bedN + nMass;
                int dh = headSrc - headN;
                if (dh <= 0)
                {
                    continue;
                }

                int drop = Math.Max(0, bedSrc - bedN);
                int w = drop > 6
                    ? (dh + drop * 3)
                    : (srcMass >= 135 ? Math.Max(1, dh / 5) : 0);

                sumWeights += w;
                if (nx == targetX && nz == targetZ)
                {
                    targetWeight = w;
                }
            }

            if (sumWeights <= 0 || targetWeight <= 0)
            {
                slopeShare256 = 0;
                return;
            }

            slopeShare256 = Math.Clamp((targetWeight * 256) / sumWeights, 24, 256);
        }

        /// <summary>
        /// Asla hedef yuksekligini kaynak yuksekliginin ustune tasirmayan (overshoot = 0), dik egime oncelik veren
        /// ve 1-birimlik kiyi/golet ping-pong titresimini durduran ortak sanal boru transfer cozucusu.
        /// </summary>
        private static int ComputeStableLateralTransfer(
            FluidType activeType,
            byte srcMass,
            byte dstMass,
            byte dstCap,
            int bedSrc,
            int bedDst,
            bool hasSteeperAlternative = false,
            int slopeShare256 = 256)
        {
            if (srcMass == 0 || dstCap <= dstMass || slopeShare256 <= 0)
            {
                return 0;
            }

            int diffH = (bedSrc + srcMass) - (bedDst + dstMass);
            if (diffH <= 0)
            {
                return 0;
            }

            if (activeType == FluidType.Lava && diffH < LavaBinghamYieldThreshold)
            {
                return 0;
            }

            int viscosityDiv = activeType == FluidType.Lava ? 4 : 2;
            bool isDownhill = bedSrc > bedDst;
            int desired;

            if (isDownhill)
            {
                int safeDrain = bedSrc - (bedDst + dstMass);
                if (activeType == FluidType.Water && srcMass <= 24 && safeDrain >= srcMass && !hasSteeperAlternative)
                {
                    // Kuru / dusuk seviyeli yamacta ince su filminin takilmadan tamamen asagi suzulmesi
                    desired = srcMass;
                }
                else
                {
                    if (diffH < 2)
                    {
                        return 0;
                    }

                    int rawPipe = activeType == FluidType.Water
                        ? Math.Max(1, (diffH * 102) >> 8) // ~%40 dengeli su akis hizi (sel patlamasi yok)
                        : diffH / viscosityDiv;
                    if (rawPipe <= 0)
                    {
                        return 0;
                    }

                    int slopeCap = Math.Max(16, (srcMass * 42) / 100);
                    desired = Math.Min(rawPipe, slopeCap);
                    if (slopeShare256 < 256)
                    {
                        desired = Math.Max(1, (desired * slopeShare256 + 128) >> 8);
                    }
                }
            }
            else
            {
                if (diffH < 2)
                {
                    return 0;
                }

                // Tam yatay duzlukte mikro-damlaciklarin (< 8 birim) sonsuza dek kuru hucrelere surunmesini engelle
                if (activeType == FluidType.Water && dstMass == 0 && bedSrc == bedDst && srcMass < 8)
                {
                    return 0;
                }

                if (hasSteeperAlternative)
                {
                    // Yokus varken yan tarafa yalnizca ilk koyuldugunda hafif yastik yayilimi (<= 22 birim) yap
                    desired = Math.Min(22, Math.Max(1, ((diffH / 3) * slopeShare256) >> 8));
                }
                else
                {
                    desired = activeType == FluidType.Water
                        ? Math.Min(72, Math.Max(1, (diffH * 112) >> 8)) // Duzlukte kontrollu yayilim (max 72 birim/tik)
                        : diffH / viscosityDiv;
                }

                if (desired <= 0)
                {
                    return 0;
                }
            }

            return Math.Min(desired, Math.Min((int)srcMass, dstCap - dstMass));
        }

        private static void UpdateHydrostaticHeadAndSolveUPipes(
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int[] visitedStamp,
            int[] bfsQueue,
            int sizeX,
            int sizeY,
            int sizeZ,
            ulong[]? salinityBits = null)
        {
            int strideY = sizeZ * sizeX;

            // A) Yukaridan asagiya 5-Bit PressureHead (0..31) dikey sutun hesaplamasi
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    byte head = 0;
                    FluidType colType = FluidType.None;
                    for (int y = sizeY - 1; y >= 0; y--)
                    {
                        int idx = y * strideY + z * sizeX + x;
                        FluidCell16 cell = new FluidCell16(fluids[idx]);
                        if (cell.Mass == 0 || cell.Type == FluidType.None)
                        {
                            head = 0;
                            colType = FluidType.None;
                            continue;
                        }

                        if (cell.Type != colType)
                        {
                            colType = cell.Type;
                            head = 0;
                        }

                        fluids[idx] = FluidCell16.Pack(cell.Type, cell.Mass, cell.IsSettled, head).RawValue;
                        if (cell.Mass >= 220 && head < FluidCell16.MaxPressureHead)
                        {
                            head++;
                        }
                        else if (cell.Mass < 200)
                        {
                            head = 0;
                        }
                    }
                }
            }

            // B) Birlesik Kaplar (U-Borusu) & <= 10m Sifon BFS:
            // Dolu sivi borusu bilesenlerinde en yuksek basinc kaynagi yuzeyi (maxSourceY) ile
            // en alcak cikis agzi (minOutletY < maxSourceY, tepe noktasi maxPipeY <= maxSourceY + 10) arasinda hidrostatik akis kur!
            ReadOnlySpan<int> dx = stackalloc int[6] { 1, -1, 0, 0, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[6] { 0, 0, 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[6] { 0, 0, 0, 0, 1, -1 };

            int stamp = ++_globalStampCounter;
            if (stamp >= int.MaxValue - 16)
            {
                Array.Clear(visitedStamp, 0, visitedStamp.Length);
                _globalStampCounter = 1;
                stamp = 1;
            }

            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int seedIdx = y * strideY + z * sizeX + x;
                        if (visitedStamp[seedIdx] == stamp)
                        {
                            continue;
                        }

                        FluidCell16 seedCell = new FluidCell16(fluids[seedIdx]);
                        if (seedCell.Mass < 224 || seedCell.Type == FluidType.None || seedCell.PressureHead == 0)
                        {
                            continue;
                        }

                        FluidType pipeType = seedCell.Type;
                        int qHead = 0;
                        int qTail = 0;
                        visitedStamp[seedIdx] = stamp;
                        bfsQueue[qTail++] = seedIdx;

                        int bestSourceIdx = -1;
                        int bestSourceY = -1;
                        int bestOutletIdx = -1;
                        int bestOutletY = int.MaxValue;
                        int maxPipeCrestY = y;
                        int confinedTunnelCells = 0;

                        while (qHead < qTail && qTail < 2048)
                        {
                            int cur = bfsQueue[qHead++];
                            int cy = cur / strideY;
                            int rem = cur % strideY;
                            int cz = rem / sizeX;
                            int cx = rem % sizeX;

                            if (cy > maxPipeCrestY) maxPipeCrestY = cy;
                            if (cy + 1 < sizeY && FluidPorosityUtility.ComputeCellFluidCapacity(voxels[cur + strideY]) == 0)
                            {
                                confinedTunnelCells++;
                            }

                            FluidCell16 curCell = new FluidCell16(fluids[cur]);
                            bool hasSameFluidAbove = cy + 1 < sizeY &&
                                new FluidCell16(fluids[cur + strideY]).Type == pipeType &&
                                new FluidCell16(fluids[cur + strideY]).Mass > 0;

                            if (!hasSameFluidAbove && curCell.Mass > 0 && cy > bestSourceY)
                            {
                                bestSourceY = cy;
                                bestSourceIdx = cur;
                            }

                            for (int d = 0; d < 6; d++)
                            {
                                int nx = cx + dx[d];
                                int ny = cy + dy[d];
                                int nz = cz + dz[d];
                                if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                                {
                                    continue;
                                }

                                int nIdx = ny * strideY + nz * sizeX + nx;
                                byte nCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[nIdx]);
                                if (nCap == 0)
                                {
                                    continue;
                                }

                                FluidCell16 nCell = new FluidCell16(fluids[nIdx]);

                                // Dikey sutunun en tepesindeki kismi dolu (< 192) serbest yuzey hucresi de gecerli kaynak yuzeyidir!
                                if (dy[d] == 1 && nCell.Type == pipeType && nCell.Mass > 0)
                                {
                                    bool topFree = ny + 1 >= sizeY ||
                                                   new FluidCell16(fluids[nIdx + strideY]).Type != pipeType ||
                                                   new FluidCell16(fluids[nIdx + strideY]).Mass == 0;
                                    if (topFree && ny > bestSourceY)
                                    {
                                        bestSourceY = ny;
                                        bestSourceIdx = nIdx;
                                    }
                                }

                                if (dy[d] == 1 && (nCell.Type == FluidType.None || nCell.Type == pipeType) && nCap >= 128 && (nCap - nCell.Mass) >= 16)
                                {
                                    // Yalnizca yukari (+Y) dikey U-kolu cikis agzi adayi (yanal yayilim RunLateralPairPass ile CFL <= 1 olarak cozulur!)
                                    if (ny < bestOutletY)
                                    {
                                        bestOutletY = ny;
                                        bestOutletIdx = nIdx;
                                    }
                                }

                                if (nCell.Type == pipeType && nCell.Mass >= 192)
                                {
                                    bool topFree = ny + 1 >= sizeY ||
                                                   new FluidCell16(fluids[nIdx + strideY]).Type != pipeType ||
                                                   new FluidCell16(fluids[nIdx + strideY]).Mass == 0;
                                    if (topFree && ny > bestSourceY)
                                    {
                                        bestSourceY = ny;
                                        bestSourceIdx = nIdx;
                                    }

                                    if (visitedStamp[nIdx] != stamp && qTail < 2048)
                                    {
                                        visitedStamp[nIdx] = stamp;
                                        bfsQueue[qTail++] = nIdx;
                                    }
                                }
                            }
                        }

                        // Hidrostatik U-borusu ve <= 10m Sifon Kosulu:
                        // En az 2 hucrelik gercek kapali tavanli borudan gecilmis olmali (tek blokluk sarkan cukur dudagini eler!),
                        // Kaynak kotu > Cikis kotu VE boru taci <= kaynak + 10m!
                        if (confinedTunnelCells >= 2 &&
                            bestSourceIdx >= 0 &&
                            bestOutletIdx >= 0 &&
                            bestSourceIdx != bestOutletIdx &&
                            bestSourceY > bestOutletY &&
                            maxPipeCrestY <= bestSourceY + MaxSiphonCrestBlocks)
                        {
                            FluidCell16 srcCell = new FluidCell16(fluids[bestSourceIdx]);
                            FluidCell16 outCell = new FluidCell16(fluids[bestOutletIdx]);
                            byte outCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[bestOutletIdx]);
                            int headDelta = bestSourceY - bestOutletY;
                            int maxRate = pipeType == FluidType.Lava ? 32 : Math.Min(128, 32 * headDelta);
                            int transfer = Math.Min(maxRate, Math.Min((int)srcCell.Mass, outCap - outCell.Mass));

                            if (transfer > 0)
                            {
                                bool outWasDry = outCell.Mass == 0;
                                byte remSrc = (byte)(srcCell.Mass - transfer);
                                fluids[bestSourceIdx] = FluidCell16.Pack(pipeType, remSrc, isSettled: false).RawValue;
                                fluids[bestOutletIdx] = FluidCell16.Pack(pipeType, (byte)(outCell.Mass + transfer), isSettled: false, outCell.PressureHead).RawValue;
                                PropagateGridSalinityOnTransfer(salinityBits, pipeType, bestSourceIdx, remSrc, bestOutletIdx, outWasDry);
                                _lastStepSiphonTransfers++;

                                int sy = bestSourceIdx / strideY;
                                int srem = bestSourceIdx % strideY;
                                MarkChangedWithNeighbors(changedMask, srem % sizeX, sy, srem / sizeX, sizeX, sizeY, sizeZ);

                                int oy = bestOutletIdx / strideY;
                                int orem = bestOutletIdx % strideY;
                                MarkChangedWithNeighbors(changedMask, orem % sizeX, oy, orem / sizeX, sizeX, sizeY, sizeZ);
                            }
                        }
                    }
                }
            }
        }

        private static bool IsWaterLavaPair(FluidType a, FluidType b)
        {
            return (a == FluidType.Water && b == FluidType.Lava) ||
                   (a == FluidType.Lava && b == FluidType.Water);
        }

        private static void ExecuteCellPairReaction(
            Span<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int idxA, int ax, int ay, int az,
            int idxB, int bx, int by, int bz,
            bool reactionTargetIsSecond,
            int sizeX, int sizeY, int sizeZ,
            ref int obsidianFormedCount,
            ref int steamSlagFizzleCount,
            ulong[]? salinityBits = null)
        {
            FluidCell16 cellA = new FluidCell16(fluids[idxA]);
            FluidCell16 cellB = new FluidCell16(fluids[idxB]);
            FluidCell16 water = cellA.Type == FluidType.Water ? cellA : cellB;
            FluidCell16 lava = cellA.Type == FluidType.Lava ? cellA : cellB;

            if (!TryReactWaterAndLava(ref water, ref lava, out VoxelCell formedSolid, out bool formedObsidian))
            {
                return;
            }

            // Her iki hucredeki sivi tamamen tuketilir (0, 0)
            fluids[idxA] = 0;
            fluids[idxB] = 0;
            if (salinityBits != null)
            {
                SetGridSalinityBit(salinityBits, idxA, false);
                SetGridSalinityBit(salinityBits, idxB, false);
            }

            if (formedObsidian)
            {
                int targetIdx = reactionTargetIsSecond ? idxB : idxA;
                voxels[targetIdx] = formedSolid.RawValue;
                obsidianFormedCount++;
            }
            else
            {
                // < 128 hacim: 0 kati blok, yalnizca buhar ve curuf sonmesi!
                steamSlagFizzleCount++;
            }

            MarkChangedWithNeighbors(changedMask, ax, ay, az, sizeX, sizeY, sizeZ);
            MarkChangedWithNeighbors(changedMask, bx, by, bz, sizeX, sizeY, sizeZ);
        }

        private static void MarkChangedWithNeighbors(
            byte[] changedMask,
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            int strideY = sizeZ * sizeX;
            int idx = y * strideY + z * sizeX + x;
            changedMask[idx] |= 1;

            if (x > 0) changedMask[idx - 1] |= 1;
            if (x + 1 < sizeX) changedMask[idx + 1] |= 1;
            if (z > 0) changedMask[idx - sizeX] |= 1;
            if (z + 1 < sizeZ) changedMask[idx + sizeX] |= 1;
            if (y > 0) changedMask[idx - strideY] |= 1;
            if (y + 1 < sizeY) changedMask[idx + strideY] |= 1;
        }

        private static void EnsureScratch(int totalCells, out int[] visitedStamp, out int[] bfsQueue, out byte[] activeNext)
        {
            if (_tlsVisitedStamp == null || _tlsVisitedStamp.Length < totalCells)
            {
                int cap = Math.Max(totalCells, 4096);
                _tlsVisitedStamp = new int[cap];
                _tlsActiveNext = new byte[cap];
            }

            if (_tlsBfsQueue == null || _tlsBfsQueue.Length < 2048)
            {
                _tlsBfsQueue = new int[2048];
            }

            visitedStamp = _tlsVisitedStamp;
            bfsQueue = _tlsBfsQueue;
            activeNext = _tlsActiveNext!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool GetGridSalinityBit(ulong[]? salinityBits, int cellIndex)
        {
            if (salinityBits == null) return false;
            int word = cellIndex >> 6;
            if ((uint)word >= (uint)salinityBits.Length) return false;
            return ((salinityBits[word] >> (cellIndex & 63)) & 1UL) != 0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetGridSalinityBit(ulong[]? salinityBits, int cellIndex, bool isSalty)
        {
            if (salinityBits == null) return;
            int word = cellIndex >> 6;
            if ((uint)word >= (uint)salinityBits.Length) return;
            ulong mask = 1UL << (cellIndex & 63);
            if (isSalty) salinityBits[word] |= mask;
            else salinityBits[word] &= ~mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PropagateGridSalinityOnTransfer(
            ulong[]? salinityBits,
            FluidType fluidType,
            int srcIdx,
            byte srcRemainingMass,
            int dstIdx,
            bool dstWasDry)
        {
            if (salinityBits == null) return;
            if (fluidType != FluidType.Water)
            {
                SetGridSalinityBit(salinityBits, srcIdx, false);
                SetGridSalinityBit(salinityBits, dstIdx, false);
                return;
            }

            if (GetGridSalinityBit(salinityBits, srcIdx))
            {
                SetGridSalinityBit(salinityBits, dstIdx, true);
            }
            else if (dstWasDry)
            {
                SetGridSalinityBit(salinityBits, dstIdx, false);
            }

            if (srcRemainingMass == 0)
            {
                SetGridSalinityBit(salinityBits, srcIdx, false);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PropagateSeamSalinityOnTransfer(
            ChunkSalinityBitset? salinityBitset,
            FluidType fluidType,
            (int cx, int cz) srcKey,
            int srcIdx,
            byte srcRemainingMass,
            (int cx, int cz) dstKey,
            int dstIdx,
            bool dstWasDry)
        {
            if (salinityBitset == null) return;
            if (fluidType != FluidType.Water)
            {
                salinityBitset.SetSaltWaterFlat(srcKey.cx, srcKey.cz, srcIdx, false);
                salinityBitset.SetSaltWaterFlat(dstKey.cx, dstKey.cz, dstIdx, false);
                return;
            }

            if (salinityBitset.IsSaltWaterFlat(srcKey.cx, srcKey.cz, srcIdx))
            {
                salinityBitset.SetSaltWaterFlat(dstKey.cx, dstKey.cz, dstIdx, true);
            }
            else if (dstWasDry)
            {
                salinityBitset.SetSaltWaterFlat(dstKey.cx, dstKey.cz, dstIdx, false);
            }

            if (srcRemainingMass == 0)
            {
                salinityBitset.SetSaltWaterFlat(srcKey.cx, srcKey.cz, srcIdx, false);
            }
        }
    }
}
