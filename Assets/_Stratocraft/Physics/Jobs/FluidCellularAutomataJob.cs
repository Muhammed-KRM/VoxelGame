#nullable enable
using System;
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
        private static int _globalStampCounter = 1;

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
        /// </summary>
        public static long StepFluidGridVolumeConservative(
            Span<uint> voxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int stepParity = 0)
        {
            obsidianFormedCount = 0;
            steamSlagFizzleCount = 0;

            int totalCells = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalCells || fluids.Length < totalCells)
            {
                throw new ArgumentException("Fluid or voxel buffer smaller than grid dimensions.");
            }

            // Hizli %0 CPU Durgunluk Erken Cikisi: Hic aktif (IsSettled == false) sivi hucresi yoksa yalnizca kutle topla ve cik!
            bool anyActive = false;
            long fastMassSum = 0;
            for (int i = 0; i < totalCells; i++)
            {
                ushort raw = fluids[i];
                int m = raw & 0xFF;
                if (m > 0)
                {
                    fastMassSum += m;
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

            int strideY = sizeZ * sizeX;

            // 1. Gecis: -Y Yercekimi Akisi ve Dikey Su+Lav Temas Kimyasi
            for (int y = 1; y < sizeY; y++)
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
                                int maxRate = cell.Type == FluidType.Lava ? Math.Min((int)cell.Mass, 64) : cell.Mass;
                                int transfer = Math.Min(space, maxRate);
                                if (transfer > 0)
                                {
                                    fluids[belowIdx] = FluidCell16.Pack(cell.Type, (byte)(belowCell.Mass + transfer), isSettled: false).RawValue;
                                    byte rem = (byte)(cell.Mass - transfer);
                                    fluids[idx] = FluidCell16.Pack(cell.Type, rem, isSettled: false).RawValue;
                                    MarkChangedWithNeighbors(changedMask, x, y, z, sizeX, sizeY, sizeZ);
                                    MarkChangedWithNeighbors(changedMask, x, y - 1, z, sizeX, sizeY, sizeZ);
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
                                ref steamSlagFizzleCount);
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
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: true, oddOffset: 0, ref obsidianFormedCount, ref steamSlagFizzleCount);
                        break;
                    case 1: // Odd-X: (1,2), (3,4), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: true, oddOffset: 1, ref obsidianFormedCount, ref steamSlagFizzleCount);
                        break;
                    case 2: // Even-Z: (0,1), (2,3), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: false, oddOffset: 0, ref obsidianFormedCount, ref steamSlagFizzleCount);
                        break;
                    default: // Odd-Z: (1,2), (3,4), ...
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, alongX: false, oddOffset: 1, ref obsidianFormedCount, ref steamSlagFizzleCount);
                        break;
                }
            }

            // 3. Gecis: 5-Bit Hidrostatik Basinc Yuksekligi (PressureHead) + U-Borusu & <= 10m Sifon Dengeleme!
            UpdateHydrostaticHeadAndSolveUPipes(
                voxels, fluids, changedMask, visitedStamp, bfsQueue, sizeX, sizeY, sizeZ);

            // 4. Gecis: Degismeyen durgun hucreleri IsSettled = 1 (%0 CPU) uykusuna yatir ve toplam kutleyi don
            long totalRemainingMass = 0;
            for (int i = 0; i < totalCells; i++)
            {
                ushort raw = fluids[i];
                int m = raw & 0xFF;
                if (m == 0)
                {
                    fluids[i] = 0;
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
            ref int steamSlagFizzleCount)
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
                                    ref steamSlagFizzleCount);
                            }
                            continue;
                        }

                        FluidType activeType = aCell.Mass > 0 ? aCell.Type : bCell.Type;
                        if (activeType == FluidType.None)
                        {
                            continue;
                        }

                        int diff = aCell.Mass - bCell.Mass;
                        int absDiff = Math.Abs(diff);

                        // Lav Bingham Plastik Akma Esigi (16) ve Su 1-birim denge kohezyonu
                        int minDiffThreshold = activeType == FluidType.Lava ? LavaBinghamYieldThreshold : 2;
                        if (absDiff < minDiffThreshold)
                        {
                            continue;
                        }

                        int viscosityDiv = activeType == FluidType.Lava ? 4 : 2;
                        int transfer = Math.Max(1, absDiff / viscosityDiv);

                        if (diff > 0)
                        {
                            transfer = Math.Min(transfer, Math.Min((int)aCell.Mass, bCap - bCell.Mass));
                            if (transfer > 0)
                            {
                                fluids[aIdx] = FluidCell16.Pack(activeType, (byte)(aCell.Mass - transfer), isSettled: false).RawValue;
                                fluids[bIdx] = FluidCell16.Pack(activeType, (byte)(bCell.Mass + transfer), isSettled: false).RawValue;
                                MarkChangedWithNeighbors(changedMask, ax, y, az, sizeX, sizeY, sizeZ);
                                MarkChangedWithNeighbors(changedMask, bx, y, bz, sizeX, sizeY, sizeZ);
                            }
                        }
                        else
                        {
                            transfer = Math.Min(transfer, Math.Min((int)bCell.Mass, aCap - aCell.Mass));
                            if (transfer > 0)
                            {
                                fluids[bIdx] = FluidCell16.Pack(activeType, (byte)(bCell.Mass - transfer), isSettled: false).RawValue;
                                fluids[aIdx] = FluidCell16.Pack(activeType, (byte)(aCell.Mass + transfer), isSettled: false).RawValue;
                                MarkChangedWithNeighbors(changedMask, ax, y, az, sizeX, sizeY, sizeZ);
                                MarkChangedWithNeighbors(changedMask, bx, y, bz, sizeX, sizeY, sizeZ);
                            }
                        }
                    }
                }
            }
        }

        private static void UpdateHydrostaticHeadAndSolveUPipes(
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int[] visitedStamp,
            int[] bfsQueue,
            int sizeX,
            int sizeY,
            int sizeZ)
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

                        while (qHead < qTail && qTail < 2048)
                        {
                            int cur = bfsQueue[qHead++];
                            int cy = cur / strideY;
                            int rem = cur % strideY;
                            int cz = rem / sizeX;
                            int cx = rem % sizeX;

                            if (cy > maxPipeCrestY) maxPipeCrestY = cy;

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

                                if ((nCell.Type == FluidType.None || nCell.Type == pipeType) && (nCap - nCell.Mass) >= 8)
                                {
                                    // Yukari (+Y) veya yan cikis agzi adayi (gercek bosluk tasiyan alcak U-kolu!)
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
                        // Kaynak kotu > Cikis kotu VE boru taci (sifon tepesi) kaynak kotundan en fazla 10m yukarida!
                        if (bestSourceIdx >= 0 &&
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
                                fluids[bestSourceIdx] = FluidCell16.Pack(pipeType, (byte)(srcCell.Mass - transfer), isSettled: false).RawValue;
                                fluids[bestOutletIdx] = FluidCell16.Pack(pipeType, (byte)(outCell.Mass + transfer), isSettled: false, outCell.PressureHead).RawValue;

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
            ref int steamSlagFizzleCount)
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
            changedMask[idx] = 1;

            if (x > 0) changedMask[idx - 1] = 1;
            if (x + 1 < sizeX) changedMask[idx + 1] = 1;
            if (z > 0) changedMask[idx - sizeX] = 1;
            if (z + 1 < sizeZ) changedMask[idx + sizeX] = 1;
            if (y > 0) changedMask[idx - strideY] = 1;
            if (y + 1 < sizeY) changedMask[idx + strideY] = 1;
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
    }
}
