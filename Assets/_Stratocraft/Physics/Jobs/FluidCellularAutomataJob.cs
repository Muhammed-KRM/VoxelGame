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
        [ThreadStatic] private static int[]? _tlsFluxX;
        [ThreadStatic] private static int[]? _tlsFluxZ;
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
            return StepFluidGridVolumeConservative(
                voxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                stepParity,
                salinityBits,
                kinematicsSlice: null);
        }

        /// <summary>
        /// 3B izgara uzerinde hacim korunumlu 1 adim akiskan + U-borusu/sifon + stokiyometrik obsidyen
        /// ve sifir-GC kinematik yan-tampon (PrevMass, VelX_Q7, VelZ_Q7) simulasyonu calistirir.
        /// </summary>
        public static long StepFluidGridVolumeConservative(
            Span<uint> voxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int stepParity,
            ulong[]? salinityBits,
            ActiveChunkFluidKinematicsSlice? kinematicsSlice)
        {
            ChunkFluidActivityMask transientMask = default;
            return StepFluidGridVolumeConservative(
                voxels,
                fluids,
                sizeX,
                sizeY,
                sizeZ,
                out obsidianFormedCount,
                out steamSlagFizzleCount,
                stepParity,
                salinityBits,
                kinematicsSlice,
                ref transientMask);
        }

        /// <summary>
        /// Paket 4B.2 Alt-Hacim (192-Bit Y-Katman Bitmask) + Kiyi Capraz-Katman Hidrostatik Koprulu
        /// Hacim Korunumlu Akiskan Otomati.
        /// </summary>
        public static long StepFluidGridVolumeConservative(
            Span<uint> voxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int steamSlagFizzleCount,
            int stepParity,
            ulong[]? salinityBits,
            ActiveChunkFluidKinematicsSlice? kinematicsSlice,
            ref ChunkFluidActivityMask activityMask)
        {
            obsidianFormedCount = 0;
            steamSlagFizzleCount = 0;
            _lastStepSiphonTransfers = 0;

            int totalCells = sizeX * sizeY * sizeZ;
            if (voxels.Length < totalCells || fluids.Length < totalCells)
            {
                throw new ArgumentException("Fluid or voxel buffer smaller than grid dimensions.");
            }

            if (kinematicsSlice != null &&
                (kinematicsSlice.PrevMass.Length < totalCells ||
                 kinematicsSlice.VelX_Q7.Length < totalCells ||
                 kinematicsSlice.VelZ_Q7.Length < totalCells))
            {
                throw new ArgumentException("Kinematics slice buffer smaller than grid dimensions.", nameof(kinematicsSlice));
            }

            // Hizli %0 CPU Durgunluk Erken Cikisi + Aktif Y-Dilimi Penceresi [minFluidY..maxFluidY]:
            // Eger activityMask onceden baslatilmissa, bos 150+ Y katmanini HIC OKUMADAN yalnizca islak katmanlari gezer!
            bool anyActive = false;
            long fastMassSum = 0;
            int strideY = sizeZ * sizeX;
            int minFluidY = sizeY - 1;
            int maxFluidY = 0;

            bool canUseFastMask = activityMask.IsInitialized != 0 && strideY == 256 && sizeY <= 192;
            if (canUseFastMask)
            {
                if (!activityMask.HasAnyWetLayer())
                {
                    activityMask.TotalWetCellCount = 0;
                    activityMask.HasAnyLava = 0;
                    return 0L;
                }

                int scanMinY = Math.Clamp((int)activityMask.MinWetY - 1, 0, sizeY - 1);
                int scanMaxY = Math.Clamp((int)activityMask.MaxWetY + 1, scanMinY, sizeY - 1);
                for (int y = scanMinY; y <= scanMaxY; y++)
                {
                    int yBase = y * strideY;
                    for (int c = 0; c < strideY; c++)
                    {
                        ushort raw = fluids[yBase + c];
                        int m = raw & 0xFF;
                        if (m > 0)
                        {
                            fastMassSum += m;
                            if (y < minFluidY) minFluidY = y;
                            if (y > maxFluidY) maxFluidY = y;
                            if ((raw & FluidCell16.SettledBitMask) == 0)
                            {
                                anyActive = true;
                            }
                        }
                    }
                }
            }
            else
            {
                for (int y = 0; y < sizeY; y++)
                {
                    int yBase = y * strideY;
                    for (int c = 0; c < strideY; c++)
                    {
                        ushort raw = fluids[yBase + c];
                        int m = raw & 0xFF;
                        if (m > 0)
                        {
                            fastMassSum += m;
                            if (y < minFluidY) minFluidY = y;
                            if (y > maxFluidY) maxFluidY = y;
                            if ((raw & FluidCell16.SettledBitMask) == 0)
                            {
                                anyActive = true;
                            }
                        }
                    }
                }
            }

            if (fastMassSum == 0)
            {
                activityMask = default;
                activityMask.IsInitialized = 1;
                return 0L;
            }

            // Cok-eksenli (X + Z + Y) y -> y-2 dik dag yamaci gecisleri icin windowMinY = minFluidY - 6 acilir;
            // boylece tum yeni islak hucrelerin changedMask/flux tamponu icinde kalmasi ve Pass 4'te erken
            // IsSettled=1 donmamasi %100 garanti edilir!
            int windowMinY = Math.Max(0, minFluidY - 6);
            int windowMaxY = Math.Min(sizeY - 1, maxFluidY + 1);
            int windowStartCell = windowMinY * strideY;
            int windowEndCell = (windowMaxY + 1) * strideY;
            int windowCellCount = windowEndCell - windowStartCell;

            if (!anyActive)
            {
                if (kinematicsSlice != null)
                {
                    kinematicsSlice.SnapshotPrevMassWindow(fluids, windowStartCell, windowEndCell);
                    for (int i = windowStartCell; i < windowEndCell; i++)
                    {
                        if (kinematicsSlice.VelX_Q7[i] != 0) kinematicsSlice.VelX_Q7[i] = 0;
                        if (kinematicsSlice.VelZ_Q7[i] != 0) kinematicsSlice.VelZ_Q7[i] = 0;
                    }
                }

                activityMask.ActiveY0 = 0UL;
                activityMask.ActiveY1 = 0UL;
                activityMask.ActiveY2 = 0UL;
                return fastMassSum;
            }

            if (kinematicsSlice != null)
            {
                kinematicsSlice.SnapshotPrevMassWindow(fluids, windowStartCell, windowEndCell);
            }

            bool trackKinematics = kinematicsSlice != null;
            EnsureScratch(totalCells, out int[] visitedStamp, out int[] bfsQueue, out byte[] changedMask, trackKinematics, out int[]? fluxX, out int[]? fluxZ);

            // 368 KB tam Array.Clear yerine YALNIZCA aktif Y-bandini sifirla (~50x-100x bellek bant genisligi tasarrufu!)
            Array.Clear(changedMask, windowStartCell, windowCellCount);
            if (trackKinematics && fluxX != null && fluxZ != null)
            {
                Array.Clear(fluxX, windowStartCell, windowCellCount);
                Array.Clear(fluxZ, windowStartCell, windowCellCount);
            }

            int startY = Math.Max(1, minFluidY);
            int endY = windowMaxY;

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
                                    if (kinematicsSlice != null && belowWasDry)
                                    {
                                        kinematicsSlice.VelX_Q7[belowIdx] = (sbyte)((kinematicsSlice.VelX_Q7[idx] * 3) / 4);
                                        kinematicsSlice.VelZ_Q7[belowIdx] = (sbyte)((kinematicsSlice.VelZ_Q7[idx] * 3) / 4);
                                    }
                                    PropagateGridSalinityOnTransfer(salinityBits, cell.Type, idx, rem, belowIdx, belowWasDry);
                                    MarkChangedWithNeighbors(changedMask, x, y, z, sizeX, sizeY, sizeZ);
                                    MarkChangedWithNeighbors(changedMask, x, y - 1, z, sizeX, sizeY, sizeZ);
                                    if (y - 1 < windowMinY)
                                    {
                                        windowMinY = Math.Max(0, y - 2);
                                        windowStartCell = windowMinY * strideY;
                                    }

                                    if (belowWasDry)
                                    {
                                        bool belowHitFloor = IsFluidColumnGrounded(voxels, fluids, belowIdx, y - 1, strideY, cell.Type);
                                        if (!belowHitFloor)
                                        {
                                            changedMask[belowIdx] |= NewlyWetFromGravityFlag;
                                        }
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

            // 2. Gecis: 4-Asamali Ayrik Kenar Degisimi (Yalnizca [windowMinY..windowMaxY] bandi + Kiyi Capraz-Katman Koprusu!)
            for (int pass = 0; pass < 4; pass++)
            {
                int mode = (pass + (stepParity & 3)) & 3;
                switch (mode)
                {
                    case 0:
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, windowMinY, windowMaxY, stepParity, alongX: true, oddOffset: 0, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits, kinematicsSlice, fluxX, fluxZ);
                        break;
                    case 1:
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, windowMinY, windowMaxY, stepParity, alongX: true, oddOffset: 1, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits, kinematicsSlice, fluxX, fluxZ);
                        break;
                    case 2:
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, windowMinY, windowMaxY, stepParity, alongX: false, oddOffset: 0, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits, kinematicsSlice, fluxX, fluxZ);
                        break;
                    default:
                        RunLateralPairPass(voxels, fluids, changedMask, sizeX, sizeY, sizeZ, windowMinY, windowMaxY, stepParity, alongX: false, oddOffset: 1, ref obsidianFormedCount, ref steamSlagFizzleCount, salinityBits, kinematicsSlice, fluxX, fluxZ);
                        break;
                }
            }

            // 2.5 Gecis: Capraz (45°) Dik Dogal Yamac Kacisi (89bb3ba8.jpg Tamiri):
            // 4-bagli CA'da capraz egimli koyu kaya yamacindaki sanal SDF cebinde kilitlenen kucuk su birikintileri,
            // YALNIZCA en az bir ara kardinal komsu aciksa (duvar delinmez -> T14/T15/T16 %100 muhurlu!) ve caprazda
            // >= 0.63m (160 birim) daha alcak dogal zemin varsa kutle koruyarak (dM == 0) asagi suzulur!
            RunDiagonalSteepSlopeEscapePass(
                voxels, fluids, changedMask, sizeX, sizeY, sizeZ, windowMinY, windowMaxY, stepParity,
                salinityBits, fluxX, fluxZ);

            // 3. Gecis: 5-Bit Hidrostatik Basinc Yuksekligi (PressureHead) + U-Borusu & <= 10m Sifon Dengeleme!
            UpdateHydrostaticHeadAndSolveUPipes(
                voxels, fluids, changedMask, visitedStamp, bfsQueue, sizeX, sizeY, sizeZ, windowMinY, windowMaxY, salinityBits);

            // 4. Gecis: Yalnizca aktif Y-penceresini guncelle, durgunlasanlari IsSettled = 1 uykusuna yatir ve 192-bit maskeyi yenile!
            int settleMinY = Math.Max(0, windowMinY - 1);
            int settleMaxY = Math.Min(sizeY - 1, windowMaxY + 1);
            long totalRemainingMass = 0;
            ulong nextWet0 = 0UL, nextWet1 = 0UL, nextWet2 = 0UL;
            ulong nextAct0 = 0UL, nextAct1 = 0UL, nextAct2 = 0UL;
            byte nextMinWetY = 255;
            byte nextMaxWetY = 0;
            int wetCellCount = 0;
            byte anyLava = 0;
            bool anyMassOrVelChanged = false;

            for (int y = settleMinY; y <= settleMaxY; y++)
            {
                int yBase = y * strideY;
                bool layerWet = false;
                bool layerActive = false;

                for (int c = 0; c < strideY; c++)
                {
                    int i = yBase + c;
                    ushort raw = fluids[i];
                    int m = raw & 0xFF;
                    if (m == 0)
                    {
                        if (raw != 0)
                        {
                            fluids[i] = 0;
                        }
                        if (salinityBits != null && GetGridSalinityBit(salinityBits, i))
                        {
                            SetGridSalinityBit(salinityBits, i, false);
                        }
                        if (kinematicsSlice != null)
                        {
                            if (kinematicsSlice.PrevMass[i] != 0) anyMassOrVelChanged = true;
                            if (kinematicsSlice.VelX_Q7[i] != 0) kinematicsSlice.VelX_Q7[i] = 0;
                            if (kinematicsSlice.VelZ_Q7[i] != 0) kinematicsSlice.VelZ_Q7[i] = 0;
                        }
                        continue;
                    }

                    layerWet = true;
                    wetCellCount++;
                    totalRemainingMass += m;
                    FluidType cellType = (FluidType)((raw >> 8) & 0x03u);
                    if (cellType == FluidType.Lava) anyLava = 1;

                    if (kinematicsSlice != null)
                    {
                        if (kinematicsSlice.PrevMass[i] != m) anyMassOrVelChanged = true;
                        float visc = cellType == FluidType.Lava ? 0.45f : (cellType == FluidType.Acid ? 0.85f : 0.80f);
                        float maxV = cellType == FluidType.Lava ? 1.20f : ActiveChunkFluidKinematicsSlice.MaxVelocityMetersPerSec;
                        float prevVx = ActiveChunkFluidKinematicsSlice.DecodeVelocityQ7(kinematicsSlice.VelX_Q7[i]);
                        float prevVz = ActiveChunkFluidKinematicsSlice.DecodeVelocityQ7(kinematicsSlice.VelZ_Q7[i]);
                        float netFluxX = (y >= windowMinY && y <= windowMaxY) ? (fluxX![i] * (1.0f / 64.0f)) : 0f;
                        float netFluxZ = (y >= windowMinY && y <= windowMaxY) ? (fluxZ![i] * (1.0f / 64.0f)) : 0f;
                        float newVx = Math.Clamp((prevVx * visc) + (netFluxX * (1.0f - visc) * 7.5f), -maxV, maxV);
                        float newVz = Math.Clamp((prevVz * visc) + (netFluxZ * (1.0f - visc) * 7.5f), -maxV, maxV);
                        if (MathF.Abs(newVx) < 0.018f) newVx = 0f;
                        if (MathF.Abs(newVz) < 0.018f) newVz = 0f;
                        kinematicsSlice.VelX_Q7[i] = ActiveChunkFluidKinematicsSlice.EncodeVelocityQ7(newVx);
                        kinematicsSlice.VelZ_Q7[i] = ActiveChunkFluidKinematicsSlice.EncodeVelocityQ7(newVz);
                    }

                    bool isChanged = (y >= windowMinY && y <= windowMaxY) && (changedMask[i] != 0);
                    if (!isChanged)
                    {
                        fluids[i] = FluidCell16.SettleRaw(raw);
                    }
                    else
                    {
                        anyMassOrVelChanged = true;
                        layerActive = true;
                        fluids[i] = FluidCell16.WakeRaw(raw);
                    }
                }

                if (layerWet && y < 192)
                {
                    if (y < nextMinWetY) nextMinWetY = (byte)y;
                    if (y > nextMaxWetY) nextMaxWetY = (byte)y;
                    int word = y >> 6;
                    ulong bit = 1UL << (y & 63);
                    if (word == 0)
                    {
                        nextWet0 |= bit;
                        if (layerActive) nextAct0 |= bit;
                    }
                    else if (word == 1)
                    {
                        nextWet1 |= bit;
                        if (layerActive) nextAct1 |= bit;
                    }
                    else
                    {
                        nextWet2 |= bit;
                        if (layerActive) nextAct2 |= bit;
                    }
                }
            }

            activityMask.WetY0 = nextWet0;
            activityMask.WetY1 = nextWet1;
            activityMask.WetY2 = nextWet2;
            activityMask.ActiveY0 = nextAct0;
            activityMask.ActiveY1 = nextAct1;
            activityMask.ActiveY2 = nextAct2;
            activityMask.TotalWetCellCount = (ushort)Math.Min(ushort.MaxValue, wetCellCount);
            activityMask.MinWetY = wetCellCount > 0 ? nextMinWetY : (byte)0;
            activityMask.MaxWetY = wetCellCount > 0 ? nextMaxWetY : (byte)0;
            activityMask.HasAnyLava = anyLava;
            if (anyMassOrVelChanged || obsidianFormedCount > 0 || steamSlagFizzleCount > 0)
            {
                activityMask.DirtyVisualFlag = 1;
            }
            activityMask.IsInitialized = 1;

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
        /// Bir sivi hucresinin altinda gercekten kati bir zemin veya kesintisiz kati-tabanli dolu sivi sutunu
        /// bulunup bulunmadigini denetler. Havada serbest dusen 255-kutleli paketlerin sahte zemin sanilip
        /// havada yanlara patlamasini (09524c95.jpg kopuk silindir kok nedeni) %100 onler!
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool IsFluidColumnGrounded(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            int cellIdx,
            int y,
            int strideY,
            FluidType fluidType)
        {
            if (y <= 0) return true;

            int minCheckY = Math.Max(0, y - 12);
            int scanIdx = cellIdx - strideY;
            for (int sy = y - 1; sy >= minCheckY; sy--)
            {
                byte belowCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[scanIdx]);
                if (belowCap == 0)
                {
                    return true;
                }

                ushort belowRaw = fluids[scanIdx];
                byte belowMass = (byte)(belowRaw & 0xFF);
                if (belowMass < belowCap || ((belowRaw >> 8) & 0x03) != (int)fluidType)
                {
                    return false;
                }

                if (sy == 0)
                {
                    return true;
                }
                scanIdx -= strideY;
            }

            // 12 blok kesintisiz dolu sivi sutunu (derin okyanus/gol) hidrostatik olarak desteklidir
            return true;
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
            ChunkSalinityBitset? salinityBitset = null,
            ActiveChunkFluidKinematicsPool? kinematicsPool = null)
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
                            salinityBitset,
                            kinematicsPool);
                    }

                    // -X Sınırı: (cx - 1, cz) (lx = 15) <-> keyA (lx = 0) (iki aktif chunk arasinda cift kosumu onle!)
                    (int cx, int cz) keyWest = (keyA.cx - 1, keyA.cz);
                    bool westAlreadyActive = wakeActiveFluidChunks != null && wakeActiveFluidChunks.Contains(keyWest);
                    if (!westAlreadyActive &&
                        fluidChunks.TryGetValue(keyWest, out ushort[]? fluidsWest) && fluidsWest != null &&
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
                            salinityBitset,
                            kinematicsPool);
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
                            salinityBitset,
                            kinematicsPool);
                    }

                    // -Z Sınırı: (cx, cz - 1) (lz = 15) <-> keyA (lz = 0)
                    (int cx, int cz) keySouth = (keyA.cx, keyA.cz - 1);
                    bool southAlreadyActive = wakeActiveFluidChunks != null && wakeActiveFluidChunks.Contains(keySouth);
                    if (!southAlreadyActive &&
                        fluidChunks.TryGetValue(keySouth, out ushort[]? fluidsSouth) && fluidsSouth != null &&
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
                            salinityBitset,
                            kinematicsPool);
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
                        salinityBitset,
                        kinematicsPool);
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
                        salinityBitset,
                        kinematicsPool);
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
            ChunkSalinityBitset? salinityBitset = null,
            ActiveChunkFluidKinematicsPool? kinematicsPool = null)
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

                    byte aCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsA[idxA]);
                    byte bCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsB[idxB]);
                    if (aCap == 0 || bCap == 0)
                    {
                        continue;
                    }

                    FluidCell16 aCell = new FluidCell16(fluidsA[idxA]);
                    FluidCell16 bCell = new FluidCell16(fluidsB[idxB]);

                    // Paket 4B.2 Sınır Ötesi Çapraz-Katman (y <-> y-1) Sığ Kıyı / Yamaç Hidrostatik Köprüsü
                    if (y > 0 && (aCell.Mass == 0 || bCell.Mass == 0))
                    {
                        int crossMoved = TryExchangeSeamCoastalStep(
                            keyA, voxelsA, fluidsA, idxA, ax, az, aCap, aCell,
                            keyB, voxelsB, fluidsB, idxB, bx, bz, bCap, bCell,
                            y, limitY, alongXSeam, salinityBitset, kinematicsPool,
                            ref dirtyFluidA, ref dirtyFluidB, out bool handledCoastalPair);
                        if (crossMoved > 0)
                        {
                            seamTransfers++;
                        }
                        if (handledCoastalPair)
                        {
                            continue;
                        }
                    }

                    if (aCell.Mass == 0 && bCell.Mass == 0)
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
                                if (kinematicsPool != null)
                                {
                                    if (kinematicsPool.TryGetLeased(keyA.cx, keyA.cz, out ActiveChunkFluidKinematicsSlice? sliceA) &&
                                        sliceA != null && (uint)idxA < (uint)sliceA.VelX_Q7.Length)
                                    {
                                        sliceA.VelX_Q7[idxA] = 0;
                                        sliceA.VelZ_Q7[idxA] = 0;
                                    }
                                    if (kinematicsPool.TryGetLeased(keyB.cx, keyB.cz, out ActiveChunkFluidKinematicsSlice? sliceB) &&
                                        sliceB != null && (uint)idxB < (uint)sliceB.VelX_Q7.Length)
                                    {
                                        sliceB.VelX_Q7[idxB] = 0;
                                        sliceB.VelZ_Q7[idxB] = 0;
                                    }
                                }
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
                    ushort belowFluidA = y > 0 ? fluidsA[idxA - 256] : (ushort)0;
                    ushort belowFluidB = y > 0 ? fluidsB[idxB - 256] : (ushort)0;
                    int bedA = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowSolidA, belowFluidA, voxelsA[idxA], activeType, y);
                    int bedB = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowSolidB, belowFluidB, voxelsB[idxB], activeType, y);
                    int headA = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedA, y, aCell.Mass, aCap);
                    int headB = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedB, y, bCell.Mass, bCap);

                    if (aCell.Mass > 0 && headA > headB)
                    {
                        if (!IsFluidColumnGrounded(voxelsA, fluidsA, idxA, y, 256, activeType))
                        {
                            continue;
                        }
                        if (bedA <= bedB && (headA - headB) < 1)
                        {
                            continue;
                        }

                        int transfer = ComputeStableLateralTransfer(
                            activeType, aCell.Mass, aCap, bCell.Mass, bCap, bedA, bedB, y, headA, headB);
                        if (transfer == 0 && activeType == FluidType.Water && (headA - headB) == 1 && (aCell.Mass - bCell.Mass) >= 2 && bCap > bCell.Mass)
                        {
                            transfer = 1;
                        }

                        if (transfer > 0)
                        {
                            ActiveChunkFluidKinematicsSlice? sliceA = null;
                            ActiveChunkFluidKinematicsSlice? sliceB = null;
                            if (kinematicsPool != null)
                            {
                                sliceA = kinematicsPool.RentOrGetAndSyncBaseline(keyA.cx, keyA.cz, fluidsA, fluidsA.Length, 0);
                                sliceB = kinematicsPool.RentOrGetAndSyncBaseline(keyB.cx, keyB.cz, fluidsB, fluidsB.Length, 0);
                            }

                            byte remA = (byte)(aCell.Mass - transfer);
                            fluidsA[idxA] = FluidCell16.Pack(activeType, remA, isSettled: false, aCell.PressureHead).RawValue;
                            fluidsB[idxB] = FluidCell16.Pack(activeType, (byte)(bCell.Mass + transfer), isSettled: false, bCell.PressureHead).RawValue;
                            if (kinematicsPool != null)
                            {
                                ApplySeamVelocityImpulse(sliceA, idxA, alongXSeam, dirSign: 1, transfer, activeType, remA == 0);
                                ApplySeamVelocityImpulse(sliceB, idxB, alongXSeam, dirSign: 1, transfer, activeType, clearOnEmpty: false);
                            }
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
                        if (!IsFluidColumnGrounded(voxelsB, fluidsB, idxB, y, 256, activeType))
                        {
                            continue;
                        }
                        if (bedB <= bedA && (headB - headA) < 1)
                        {
                            continue;
                        }

                        int transfer = ComputeStableLateralTransfer(
                            activeType, bCell.Mass, bCap, aCell.Mass, aCap, bedB, bedA, y, headB, headA);
                        if (transfer == 0 && activeType == FluidType.Water && (headB - headA) == 1 && (bCell.Mass - aCell.Mass) >= 2 && aCap > aCell.Mass)
                        {
                            transfer = 1;
                        }

                        if (transfer > 0)
                        {
                            ActiveChunkFluidKinematicsSlice? sliceA = null;
                            ActiveChunkFluidKinematicsSlice? sliceB = null;
                            if (kinematicsPool != null)
                            {
                                sliceA = kinematicsPool.RentOrGetAndSyncBaseline(keyA.cx, keyA.cz, fluidsA, fluidsA.Length, 0);
                                sliceB = kinematicsPool.RentOrGetAndSyncBaseline(keyB.cx, keyB.cz, fluidsB, fluidsB.Length, 0);
                            }

                            byte remB = (byte)(bCell.Mass - transfer);
                            fluidsB[idxB] = FluidCell16.Pack(activeType, remB, isSettled: false, bCell.PressureHead).RawValue;
                            fluidsA[idxA] = FluidCell16.Pack(activeType, (byte)(aCell.Mass + transfer), isSettled: false, aCell.PressureHead).RawValue;
                            if (kinematicsPool != null)
                            {
                                ApplySeamVelocityImpulse(sliceB, idxB, alongXSeam, dirSign: -1, transfer, activeType, remB == 0);
                                ApplySeamVelocityImpulse(sliceA, idxA, alongXSeam, dirSign: -1, transfer, activeType, clearOnEmpty: false);
                            }
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

        private static void ApplySeamVelocityImpulse(
            ActiveChunkFluidKinematicsSlice? slice,
            int cellIdx,
            bool alongX,
            int dirSign,
            int transfer,
            FluidType fluidType,
            bool clearOnEmpty)
        {
            if (slice == null || (uint)cellIdx >= (uint)slice.VelX_Q7.Length)
            {
                return;
            }

            if (clearOnEmpty)
            {
                slice.VelX_Q7[cellIdx] = 0;
                slice.VelZ_Q7[cellIdx] = 0;
                return;
            }

            float visc = fluidType == FluidType.Lava ? 0.45f : (fluidType == FluidType.Acid ? 0.85f : 0.82f);
            float maxV = fluidType == FluidType.Lava ? 1.20f : ActiveChunkFluidKinematicsSlice.MaxVelocityMetersPerSec;
            float flux = (dirSign * transfer) * (1.0f / 64.0f);
            if (alongX)
            {
                float prev = ActiveChunkFluidKinematicsSlice.DecodeVelocityQ7(slice.VelX_Q7[cellIdx]);
                float next = Math.Clamp((prev * visc) + (flux * (1.0f - visc) * 7.5f), -maxV, maxV);
                slice.VelX_Q7[cellIdx] = ActiveChunkFluidKinematicsSlice.EncodeVelocityQ7(next);
            }
            else
            {
                float prev = ActiveChunkFluidKinematicsSlice.DecodeVelocityQ7(slice.VelZ_Q7[cellIdx]);
                float next = Math.Clamp((prev * visc) + (flux * (1.0f - visc) * 7.5f), -maxV, maxV);
                slice.VelZ_Q7[cellIdx] = ActiveChunkFluidKinematicsSlice.EncodeVelocityQ7(next);
            }
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

        private static int TryExchangeSeamCoastalStep(
            (int cx, int cz) keyA, uint[] voxelsA, ushort[] fluidsA, int idxA, int ax, int az, byte aCap, FluidCell16 aCell,
            (int cx, int cz) keyB, uint[] voxelsB, ushort[] fluidsB, int idxB, int bx, int bz, byte bCap, FluidCell16 bCell,
            int y, int limitY, bool alongXSeam,
            ChunkSalinityBitset? salinityBitset,
            ActiveChunkFluidKinematicsPool? kinematicsPool,
            ref bool dirtyFluidA,
            ref bool dirtyFluidB,
            out bool handledCoastalPair)
        {
            handledCoastalPair = false;
            if (y <= 0 || aCap == 0 || bCap == 0) return 0;

            int botIdxA = idxA - 256;
            int botIdxB = idxB - 256;
            bool aFloorSolid = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsA[botIdxA]) == 0 && VoxelBitUtility.IsPhysicallySolid(voxelsA[botIdxA]);
            bool bFloorSolid = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsB[botIdxB]) == 0 && VoxelBitUtility.IsPhysicallySolid(voxelsB[botIdxB]);

            // Simetrik Durum 1: A sig kiyi kumu ustunde (y), B derin havuz/yamac (y - 1 veya dik y - 2) ve B ustu (y) bos!
            if (aFloorSolid && !bFloorSolid && bCell.Mass == 0)
            {
                int tr = ExecuteSeamCrossLayerTransfer(
                    keyA, voxelsA, fluidsA, idxA, ax, az, aCap, aCell,
                    keyB, voxelsB, fluidsB, botIdxB, bx, bz, shoreY: y, poolY: y - 1, limitY, alongXSeam, dirSignFromShore: 1,
                    salinityBitset, kinematicsPool, ref dirtyFluidA, ref dirtyFluidB, out handledCoastalPair);
                if (handledCoastalPair) return tr;

                // Dik (45-65 derece) dag yamaci chunk sinirinda y -> y-2 koprusu
                if (y >= 2 && aCell.Mass > 0 && VoxelBitUtility.GetShapeId(voxelsA[botIdxA]) == 0 && (fluidsB[botIdxB] & 0xFF) == 0 &&
                    FluidPorosityUtility.ComputeCellFluidCapacity(voxelsB[botIdxB]) > 0)
                {
                    return ExecuteSeamCrossLayerTransfer(
                        keyA, voxelsA, fluidsA, idxA, ax, az, aCap, aCell,
                        keyB, voxelsB, fluidsB, botIdxB - 256, bx, bz, shoreY: y, poolY: y - 2, limitY, alongXSeam, dirSignFromShore: 1,
                        salinityBitset, kinematicsPool, ref dirtyFluidA, ref dirtyFluidB, out handledCoastalPair);
                }
                return 0;
            }

            // Simetrik Durum 2: B sig kiyi kumu ustunde (y), A derin havuz/yamac (y - 1 veya dik y - 2) ve A ustu (y) bos!
            if (bFloorSolid && !aFloorSolid && aCell.Mass == 0)
            {
                int tr = ExecuteSeamCrossLayerTransfer(
                    keyB, voxelsB, fluidsB, idxB, bx, bz, bCap, bCell,
                    keyA, voxelsA, fluidsA, botIdxA, ax, az, shoreY: y, poolY: y - 1, limitY, alongXSeam, dirSignFromShore: -1,
                    salinityBitset, kinematicsPool, ref dirtyFluidB, ref dirtyFluidA, out handledCoastalPair);
                if (handledCoastalPair) return tr;

                if (y >= 2 && bCell.Mass > 0 && VoxelBitUtility.GetShapeId(voxelsB[botIdxB]) == 0 && (fluidsA[botIdxA] & 0xFF) == 0 &&
                    FluidPorosityUtility.ComputeCellFluidCapacity(voxelsA[botIdxA]) > 0)
                {
                    return ExecuteSeamCrossLayerTransfer(
                        keyB, voxelsB, fluidsB, idxB, bx, bz, bCap, bCell,
                        keyA, voxelsA, fluidsA, botIdxA - 256, ax, az, shoreY: y, poolY: y - 2, limitY, alongXSeam, dirSignFromShore: -1,
                        salinityBitset, kinematicsPool, ref dirtyFluidB, ref dirtyFluidA, out handledCoastalPair);
                }
                return 0;
            }

            return 0;
        }

        private static int ExecuteSeamCrossLayerTransfer(
            (int cx, int cz) keyShore, uint[] voxelsShore, ushort[] fluidsShore, int shoreTopIdx, int sx, int sz, byte shoreTopCap, FluidCell16 shoreTopCell,
            (int cx, int cz) keyPool, uint[] voxelsPool, ushort[] fluidsPool, int poolBotIdx, int px, int pz,
            int shoreY, int poolY, int limitY, bool alongXSeam, int dirSignFromShore,
            ChunkSalinityBitset? salinityBitset,
            ActiveChunkFluidKinematicsPool? kinematicsPool,
            ref bool dirtyShore,
            ref bool dirtyPool,
            out bool handledCoastalPair)
        {
            handledCoastalPair = false;
            byte poolBotCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxelsPool[poolBotIdx]);
            if (poolBotCap == 0) return 0;

            FluidCell16 poolBotCell = new FluidCell16(fluidsPool[poolBotIdx]);
            if (shoreTopCell.Mass == 0 && poolBotCell.Mass == 0) return 0;

            FluidType fType = shoreTopCell.Mass > 0 ? shoreTopCell.Type : poolBotCell.Type;
            if (fType == FluidType.None) return 0;
            if ((shoreTopCell.Mass > 0 && shoreTopCell.Type != fType) || (poolBotCell.Mass > 0 && poolBotCell.Type != fType))
            {
                return 0;
            }

            // Yalnizca gercekten kati tabana oturan (havada dusen 255 paketi OLMAYAN!) havuz/yamac basamaklarini koprule:
            if (!IsFluidColumnGrounded(voxelsPool, fluidsPool, poolBotIdx, poolY, 256, fType))
            {
                return 0;
            }

            handledCoastalPair = true;

            uint shoreBelowSolid = voxelsShore[shoreTopIdx - 256];
            uint poolBelow2Solid = poolY > 0 ? voxelsPool[poolBotIdx - 256] : 0u;
            ushort poolBelow2Fluid = poolY > 0 ? fluidsPool[poolBotIdx - 256] : (ushort)0;

            int bedShore = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(shoreBelowSolid, 0, voxelsShore[shoreTopIdx], fType, shoreY);
            int bedPool = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(poolBelow2Solid, poolBelow2Fluid, voxelsPool[poolBotIdx], fType, poolY);
            int headShore = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedShore, shoreY, shoreTopCell.Mass, shoreTopCap);
            int headPool = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedPool, poolY, poolBotCell.Mass, poolBotCap);
            int diff = headShore - headPool;
            if (Math.Abs(diff) <= 1) return 0;

            int shoreSlope223 = (bedShore > (shoreY - 1) * 255 && bedShore < shoreY * 255) ? Math.Max(223, (shoreY + 1) * 255 - (bedShore + 32)) : 223;
            int poolSlope223 = (bedPool > (poolY - 1) * 255 && bedPool < poolY * 255) ? Math.Max(223, (poolY + 1) * 255 - (bedPool + 32)) : 223;
            int slopeSum = shoreSlope223 + poolSlope223;

            if (diff > 1 && shoreTopCell.Mass > 0)
            {
                int spacePool = poolBotCap - poolBotCell.Mass;
                if (spacePool <= 0)
                {
                    // Alt havuz (y-1) zaten 255 doluysa kiyi suyu ayni-Y (y) uzerinden havuzun ustune tasabilmelidir!
                    handledCoastalPair = false;
                    return 0;
                }
                int eqMass = Math.Max(1, (diff * 223 + (slopeSum >> 1)) / slopeSum);
                // Pürüzsüz surekli aktarim egrisi (Mass=24->25 ani %54->%96 baraj patlamasi yok!)
                int maxStepCap = shoreTopCell.Mass == 1
                    ? 1
                    : Math.Min(shoreTopCell.Mass - 1, Math.Max(1, (shoreTopCell.Mass * 15) / 24));
                int transfer = Math.Min((int)shoreTopCell.Mass, Math.Min(spacePool, Math.Min(maxStepCap, eqMass)));
                if (transfer <= 0) return 0;

                ActiveChunkFluidKinematicsSlice? sliceShore = null;
                ActiveChunkFluidKinematicsSlice? slicePool = null;
                if (kinematicsPool != null)
                {
                    sliceShore = kinematicsPool.RentOrGetAndSyncBaseline(keyShore.cx, keyShore.cz, fluidsShore, fluidsShore.Length, 0);
                    slicePool = kinematicsPool.RentOrGetAndSyncBaseline(keyPool.cx, keyPool.cz, fluidsPool, fluidsPool.Length, 0);
                }

                byte remShore = (byte)(shoreTopCell.Mass - transfer);
                fluidsShore[shoreTopIdx] = FluidCell16.Pack(fType, remShore, isSettled: false, shoreTopCell.PressureHead).RawValue;
                fluidsPool[poolBotIdx] = FluidCell16.Pack(fType, (byte)(poolBotCell.Mass + transfer), isSettled: false, poolBotCell.PressureHead).RawValue;
                ApplySeamVelocityImpulse(sliceShore, shoreTopIdx, alongXSeam, dirSignFromShore, transfer, fType, clearOnEmpty: remShore == 0);
                ApplySeamVelocityImpulse(slicePool, poolBotIdx, alongXSeam, dirSignFromShore, transfer, fType, clearOnEmpty: false);
                PropagateSeamSalinityOnTransfer(salinityBitset, fType, keyShore, shoreTopIdx, remShore, keyPool, poolBotIdx, poolBotCell.Mass == 0);
                WakeChunkCellAndNeighbors(fluidsShore, sx, shoreY, sz, limitY);
                WakeChunkCellAndNeighbors(fluidsPool, px, poolY, pz, limitY);
                dirtyShore = true;
                dirtyPool = true;
                return transfer;
            }

            if (diff < -1 && poolBotCell.Mass > 0)
            {
                int spaceShore = shoreTopCap - shoreTopCell.Mass;
                if (spaceShore <= 0) return 0;
                int eqMass = Math.Max(1, (((-diff) * 223) + (slopeSum >> 1)) / slopeSum);
                int transfer = Math.Min((int)poolBotCell.Mass, Math.Min(spaceShore, eqMass));
                if (transfer <= 0) return 0;

                ActiveChunkFluidKinematicsSlice? sliceShore = null;
                ActiveChunkFluidKinematicsSlice? slicePool = null;
                if (kinematicsPool != null)
                {
                    sliceShore = kinematicsPool.RentOrGetAndSyncBaseline(keyShore.cx, keyShore.cz, fluidsShore, fluidsShore.Length, 0);
                    slicePool = kinematicsPool.RentOrGetAndSyncBaseline(keyPool.cx, keyPool.cz, fluidsPool, fluidsPool.Length, 0);
                }

                byte remPool = (byte)(poolBotCell.Mass - transfer);
                fluidsPool[poolBotIdx] = FluidCell16.Pack(fType, remPool, isSettled: false, poolBotCell.PressureHead).RawValue;
                fluidsShore[shoreTopIdx] = FluidCell16.Pack(fType, (byte)(shoreTopCell.Mass + transfer), isSettled: false, shoreTopCell.PressureHead).RawValue;
                ApplySeamVelocityImpulse(slicePool, poolBotIdx, alongXSeam, -dirSignFromShore, transfer, fType, clearOnEmpty: remPool == 0);
                ApplySeamVelocityImpulse(sliceShore, shoreTopIdx, alongXSeam, -dirSignFromShore, transfer, fType, clearOnEmpty: false);
                PropagateSeamSalinityOnTransfer(salinityBitset, fType, keyPool, poolBotIdx, remPool, keyShore, shoreTopIdx, shoreTopCell.Mass == 0);
                WakeChunkCellAndNeighbors(fluidsShore, sx, shoreY, sz, limitY);
                WakeChunkCellAndNeighbors(fluidsPool, px, poolY, pz, limitY);
                dirtyShore = true;
                dirtyPool = true;
                return transfer;
            }

            return 0;
        }

        private static void RunLateralPairPass(
            Span<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int sizeX,
            int sizeY,
            int sizeZ,
            int startY,
            int endY,
            int stepParity,
            bool alongX,
            int oddOffset,
            ref int obsidianFormedCount,
            ref int steamSlagFizzleCount,
            ulong[]? salinityBits = null,
            ActiveChunkFluidKinematicsSlice? kinematicsSlice = null,
            int[]? fluxX = null,
            int[]? fluxZ = null)
        {
            int strideY = sizeZ * sizeX;
            int primaryLimit = alongX ? sizeX - 1 : sizeZ - 1;
            int secondaryLimit = alongX ? sizeZ : sizeX;
            int clampedStartY = Math.Clamp(startY, 0, sizeY - 1);
            int clampedEndY = Math.Clamp(endY, clampedStartY, sizeY - 1);

            for (int y = clampedStartY; y <= clampedEndY; y++)
            {
                int yBase = y * strideY;

                // Bos katman hizli atlama: ne bu katmanda ne de alt katmanda (y-1 kiyi koprusu icin) sivi yoksa 0 islem!
                bool hasFluidNearLayer = false;
                int belowBase = y > 0 ? (yBase - strideY) : yBase;
                for (int c = 0; c < strideY; c++)
                {
                    if (((fluids[yBase + c] | fluids[belowBase + c]) & 0xFF) != 0)
                    {
                        hasFluidNearLayer = true;
                        break;
                    }
                }
                if (!hasFluidNearLayer)
                {
                    continue;
                }

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

                        byte aCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[aIdx]);
                        byte bCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[bIdx]);
                        if (aCap == 0 || bCap == 0)
                        {
                            continue;
                        }

                        FluidCell16 aCell = new FluidCell16(fluids[aIdx]);
                        FluidCell16 bCell = new FluidCell16(fluids[bIdx]);

                        // Paket 4B.2 Capraz-Katman (y <-> y-1) Sig Kiyi / Yamac Hidrostatik Koprusu
                        if (y > 0 && (aCell.Mass == 0 || bCell.Mass == 0))
                        {
                            if (TryExchangeInChunkCoastalStep(
                                    voxels, fluids, changedMask,
                                    aIdx, ax, az, aCap, aCell,
                                    bIdx, bx, bz, bCap, bCell,
                                    y, sizeX, sizeY, sizeZ, strideY, alongX,
                                    salinityBits, fluxX, fluxZ))
                            {
                                continue;
                            }
                        }

                        if (aCell.Mass == 0 && bCell.Mass == 0)
                        {
                            continue;
                        }

                        if (aCell.IsSettled && bCell.IsSettled && changedMask[aIdx] == 0 && changedMask[bIdx] == 0)
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
                        ushort belowFluidA = y > 0 ? fluids[aIdx - strideY] : (ushort)0;
                        ushort belowFluidB = y > 0 ? fluids[bIdx - strideY] : (ushort)0;
                        int bedA = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowA, belowFluidA, voxels[aIdx], activeType, y);
                        int bedB = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowB, belowFluidB, voxels[bIdx], activeType, y);
                        int headA = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedA, y, aCell.Mass, aCap);
                        int headB = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedB, y, bCell.Mass, bCap);

                        byte axisNewlyWetFlag = alongX ? (byte)0x02 : (byte)0x04;
                        const byte gravityNewlyWetMask = 0x08;

                        if (aCell.Mass > 0 && headA > headB)
                        {
                            if ((changedMask[aIdx] & (axisNewlyWetFlag | gravityNewlyWetMask)) != 0 ||
                                !IsFluidColumnGrounded(voxels, fluids, aIdx, y, strideY, activeType))
                            {
                                continue;
                            }

                            EvaluateDonorSlopeRouting(
                                voxels, fluids, ax, y, az, bedA, headA, activeType, sizeX, strideY,
                                bx, bz, bedB, out bool hasSteeperAlternativeA, out int slopeShare256A);
                            if (slopeShare256A <= 0)
                            {
                                continue;
                            }

                            float forwardVelA = 0f;
                            if (kinematicsSlice != null)
                            {
                                forwardVelA = ActiveChunkFluidKinematicsSlice.DecodeVelocityQ7(
                                    alongX ? kinematicsSlice.VelX_Q7[aIdx] : kinematicsSlice.VelZ_Q7[aIdx]);
                            }

                            int transfer = ComputeStableLateralTransfer(
                                activeType, aCell.Mass, aCap, bCell.Mass, bCap, bedA, bedB, y, headA, headB,
                                hasSteeperAlternativeA, slopeShare256A, forwardVelA);

                            // Duz gol/deniz tesviyesi: diffH == 1 ve mass farki >= 2 iken pariteli 1 birim esitleme (0 merdiven!)
                            if (transfer == 0 && activeType == FluidType.Water && (headA - headB) == 1 &&
                                bCell.Mass > 0 && (aCell.Mass - bCell.Mass) >= 2 && bCap > bCell.Mass &&
                                ((stepParity + ax + az) & 1) == 0)
                            {
                                transfer = 1;
                            }

                            if (transfer > 0)
                            {
                                bool bWasDry = bCell.Mass == 0;
                                byte remA = (byte)(aCell.Mass - transfer);
                                fluids[aIdx] = FluidCell16.Pack(activeType, remA, isSettled: false).RawValue;
                                fluids[bIdx] = FluidCell16.Pack(activeType, (byte)(bCell.Mass + transfer), isSettled: false).RawValue;
                                if (fluxX != null && fluxZ != null)
                                {
                                    if (alongX)
                                    {
                                        fluxX[aIdx] += transfer;
                                        fluxX[bIdx] += transfer;
                                    }
                                    else
                                    {
                                        fluxZ[aIdx] += transfer;
                                        fluxZ[bIdx] += transfer;
                                    }
                                }
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
                            if ((changedMask[bIdx] & (axisNewlyWetFlag | gravityNewlyWetMask)) != 0 ||
                                !IsFluidColumnGrounded(voxels, fluids, bIdx, y, strideY, activeType))
                            {
                                continue;
                            }

                            EvaluateDonorSlopeRouting(
                                voxels, fluids, bx, y, bz, bedB, headB, activeType, sizeX, strideY,
                                ax, az, bedA, out bool hasSteeperAlternativeB, out int slopeShare256B);
                            if (slopeShare256B <= 0)
                            {
                                continue;
                            }

                            float forwardVelB = 0f;
                            if (kinematicsSlice != null)
                            {
                                forwardVelB = -ActiveChunkFluidKinematicsSlice.DecodeVelocityQ7(
                                    alongX ? kinematicsSlice.VelX_Q7[bIdx] : kinematicsSlice.VelZ_Q7[bIdx]);
                            }

                            int transfer = ComputeStableLateralTransfer(
                                activeType, bCell.Mass, bCap, aCell.Mass, aCap, bedB, bedA, y, headB, headA,
                                hasSteeperAlternativeB, slopeShare256B, forwardVelB);

                            if (transfer == 0 && activeType == FluidType.Water && (headB - headA) == 1 &&
                                aCell.Mass > 0 && (bCell.Mass - aCell.Mass) >= 2 && aCap > aCell.Mass &&
                                ((stepParity + bx + bz) & 1) == 0)
                            {
                                transfer = 1;
                            }

                            if (transfer > 0)
                            {
                                bool aWasDry = aCell.Mass == 0;
                                byte remB = (byte)(bCell.Mass - transfer);
                                fluids[bIdx] = FluidCell16.Pack(activeType, remB, isSettled: false).RawValue;
                                fluids[aIdx] = FluidCell16.Pack(activeType, (byte)(aCell.Mass + transfer), isSettled: false).RawValue;
                                if (fluxX != null && fluxZ != null)
                                {
                                    if (alongX)
                                    {
                                        fluxX[bIdx] -= transfer;
                                        fluxX[aIdx] -= transfer;
                                    }
                                    else
                                    {
                                        fluxZ[bIdx] -= transfer;
                                        fluxZ[aIdx] -= transfer;
                                    }
                                }
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

        private static bool TryExchangeInChunkCoastalStep(
            Span<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int aIdx, int ax, int az, byte aCap, FluidCell16 aCell,
            int bIdx, int bx, int bz, byte bCap, FluidCell16 bCell,
            int y, int sizeX, int sizeY, int sizeZ, int strideY, bool alongX,
            ulong[]? salinityBits,
            int[]? fluxX,
            int[]? fluxZ)
        {
            int botA = aIdx - strideY;
            int botB = bIdx - strideY;
            bool aFloorSolid = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[botA]) == 0 && VoxelBitUtility.IsPhysicallySolid(voxels[botA]);
            bool bFloorSolid = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[botB]) == 0 && VoxelBitUtility.IsPhysicallySolid(voxels[botB]);

            if (aFloorSolid && !bFloorSolid && bCell.Mass == 0)
            {
                if (ExecuteInChunkCrossLayerTransfer(
                        voxels, fluids, changedMask,
                        aIdx, ax, az, aCap, aCell,
                        botB, bx, bz, shoreY: y, poolY: y - 1, sizeX, sizeY, sizeZ, strideY, alongX, dirSignFromShore: 1,
                        salinityBits, fluxX, fluxZ))
                {
                    return true;
                }

                // Dik (45-65 derece) dogal dag yamaci (b9c3b024.jpg):
                // Eger (bx, y-1) acik havaysa fakat hemen altindaki (bx, y-2) kati yamac uzerine oturuyorsa,
                // yagmur suyunu 1 tik havada asili birakip isinlatmak yerine dogrudan (bx, y-2) yamac hucresine indir!
                if (y >= 2 && aCell.Mass > 0 && VoxelBitUtility.GetShapeId(voxels[botA]) == 0 && (fluids[botB] & 0xFF) == 0 &&
                    FluidPorosityUtility.ComputeCellFluidCapacity(voxels[botB]) > 0)
                {
                    int bot2B = botB - strideY;
                    if (ExecuteInChunkCrossLayerTransfer(
                            voxels, fluids, changedMask,
                            aIdx, ax, az, aCap, aCell,
                            bot2B, bx, bz, shoreY: y, poolY: y - 2, sizeX, sizeY, sizeZ, strideY, alongX, dirSignFromShore: 1,
                            salinityBits, fluxX, fluxZ))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (bFloorSolid && !aFloorSolid && aCell.Mass == 0)
            {
                if (ExecuteInChunkCrossLayerTransfer(
                        voxels, fluids, changedMask,
                        bIdx, bx, bz, bCap, bCell,
                        botA, ax, az, shoreY: y, poolY: y - 1, sizeX, sizeY, sizeZ, strideY, alongX, dirSignFromShore: -1,
                        salinityBits, fluxX, fluxZ))
                {
                    return true;
                }

                if (y >= 2 && bCell.Mass > 0 && VoxelBitUtility.GetShapeId(voxels[botB]) == 0 && (fluids[botA] & 0xFF) == 0 &&
                    FluidPorosityUtility.ComputeCellFluidCapacity(voxels[botA]) > 0)
                {
                    int bot2A = botA - strideY;
                    if (ExecuteInChunkCrossLayerTransfer(
                            voxels, fluids, changedMask,
                            bIdx, bx, bz, bCap, bCell,
                            bot2A, ax, az, shoreY: y, poolY: y - 2, sizeX, sizeY, sizeZ, strideY, alongX, dirSignFromShore: -1,
                            salinityBits, fluxX, fluxZ))
                    {
                        return true;
                    }
                }
                return false;
            }

            return false;
        }

        private static bool ExecuteInChunkCrossLayerTransfer(
            Span<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int shoreTopIdx, int sx, int sz, byte shoreTopCap, FluidCell16 shoreTopCell,
            int poolBotIdx, int px, int pz,
            int shoreY, int poolY, int sizeX, int sizeY, int sizeZ, int strideY, bool alongX, int dirSignFromShore,
            ulong[]? salinityBits,
            int[]? fluxX,
            int[]? fluxZ)
        {
            byte poolBotCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[poolBotIdx]);
            if (poolBotCap == 0) return false;

            FluidCell16 poolBotCell = new FluidCell16(fluids[poolBotIdx]);
            if (shoreTopCell.Mass == 0 && poolBotCell.Mass == 0) return false;

            FluidType fType = shoreTopCell.Mass > 0 ? shoreTopCell.Type : poolBotCell.Type;
            if (fType == FluidType.None) return true;
            if ((shoreTopCell.Mass > 0 && shoreTopCell.Type != fType) || (poolBotCell.Mass > 0 && poolBotCell.Type != fType)) return false;

            if (!IsFluidColumnGrounded(voxels, fluids, poolBotIdx, poolY, strideY, fType))
            {
                return false;
            }

            if (shoreTopCell.IsSettled && poolBotCell.IsSettled && changedMask[shoreTopIdx] == 0 && changedMask[poolBotIdx] == 0)
            {
                return true;
            }

            byte axisNewlyWetFlag = alongX ? (byte)0x02 : (byte)0x04;
            const byte gravityNewlyWetMask = 0x08;

            uint shoreBelowSolid = voxels[shoreTopIdx - strideY];
            uint poolBelow2Solid = poolY > 0 ? voxels[poolBotIdx - strideY] : 0u;
            ushort poolBelow2Fluid = poolY > 0 ? fluids[poolBotIdx - strideY] : (ushort)0;

            int bedShore = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(shoreBelowSolid, 0, voxels[shoreTopIdx], fType, shoreY);
            int bedPool = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(poolBelow2Solid, poolBelow2Fluid, voxels[poolBotIdx], fType, poolY);
            int headShore = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedShore, shoreY, shoreTopCell.Mass, shoreTopCap);
            int headPool = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedPool, poolY, poolBotCell.Mass, poolBotCap);
            int diff = headShore - headPool;
            if (Math.Abs(diff) <= 1) return true;

            int shoreSlope223 = (bedShore > (shoreY - 1) * 255 && bedShore < shoreY * 255) ? Math.Max(223, (shoreY + 1) * 255 - (bedShore + 32)) : 223;
            int poolSlope223 = (bedPool > (poolY - 1) * 255 && bedPool < poolY * 255) ? Math.Max(223, (poolY + 1) * 255 - (bedPool + 32)) : 223;
            int slopeSum = shoreSlope223 + poolSlope223;

            if (diff > 1 && shoreTopCell.Mass > 0)
            {
                if ((changedMask[shoreTopIdx] & (axisNewlyWetFlag | gravityNewlyWetMask)) != 0) return true;

                int spacePool = poolBotCap - poolBotCell.Mass;
                if (spacePool <= 0)
                {
                    // Alt havuz (y-1) tamamen 255 doluysa kiyi suyu ayni-Y (y) uzerinden havuzun ustune tasabilmelidir!
                    return false;
                }
                int eqMass = Math.Max(1, (diff * 223 + (slopeSum >> 1)) / slopeSum);
                // Pürüzsüz surekli aktarim egrisi (Mass=24->25 ani %54->%96 baraj patlamasi yok!)
                int maxStepCap = shoreTopCell.Mass == 1
                    ? 1
                    : Math.Min(shoreTopCell.Mass - 1, Math.Max(1, (shoreTopCell.Mass * 15) / 24));
                int transfer = Math.Min((int)shoreTopCell.Mass, Math.Min(spacePool, Math.Min(maxStepCap, eqMass)));
                if (transfer <= 0) return true;

                bool poolWasDry = poolBotCell.Mass == 0;
                byte remShore = (byte)(shoreTopCell.Mass - transfer);
                fluids[shoreTopIdx] = FluidCell16.Pack(fType, remShore, isSettled: false, shoreTopCell.PressureHead).RawValue;
                fluids[poolBotIdx] = FluidCell16.Pack(fType, (byte)(poolBotCell.Mass + transfer), isSettled: false, poolBotCell.PressureHead).RawValue;
                if (fluxX != null && fluxZ != null)
                {
                    int signedFlux = dirSignFromShore * transfer;
                    if (alongX) { fluxX[shoreTopIdx] += signedFlux; fluxX[poolBotIdx] += signedFlux; }
                    else { fluxZ[shoreTopIdx] += signedFlux; fluxZ[poolBotIdx] += signedFlux; }
                }
                PropagateGridSalinityOnTransfer(salinityBits, fType, shoreTopIdx, remShore, poolBotIdx, poolWasDry);
                MarkChangedWithNeighbors(changedMask, sx, shoreY, sz, sizeX, sizeY, sizeZ);
                MarkChangedWithNeighbors(changedMask, px, poolY, pz, sizeX, sizeY, sizeZ);
                if (poolWasDry)
                {
                    // Dik 2-blokluk (y -> y-2) dususte her iki yatay ekseni de isaretle (0x06) ki
                    // su tek tikte 6 blok inip windowMinY disinda erken IsSettled=1 donmasin!
                    changedMask[poolBotIdx] |= (poolY <= shoreY - 2) ? (byte)0x06 : axisNewlyWetFlag;
                }
                return true;
            }

            if (diff < -1 && poolBotCell.Mass > 0)
            {
                if ((changedMask[poolBotIdx] & (axisNewlyWetFlag | gravityNewlyWetMask)) != 0) return true;

                int spaceShore = shoreTopCap - shoreTopCell.Mass;
                if (spaceShore <= 0) return true;
                int eqMass = Math.Max(1, (((-diff) * 223) + (slopeSum >> 1)) / slopeSum);
                int transfer = Math.Min((int)poolBotCell.Mass, Math.Min(spaceShore, eqMass));
                if (transfer <= 0) return true;

                bool shoreWasDry = shoreTopCell.Mass == 0;
                byte remPool = (byte)(poolBotCell.Mass - transfer);
                fluids[poolBotIdx] = FluidCell16.Pack(fType, remPool, isSettled: false, poolBotCell.PressureHead).RawValue;
                fluids[shoreTopIdx] = FluidCell16.Pack(fType, (byte)(shoreTopCell.Mass + transfer), isSettled: false, shoreTopCell.PressureHead).RawValue;
                if (fluxX != null && fluxZ != null)
                {
                    int signedFlux = -dirSignFromShore * transfer;
                    if (alongX) { fluxX[poolBotIdx] += signedFlux; fluxX[shoreTopIdx] += signedFlux; }
                    else { fluxZ[poolBotIdx] += signedFlux; fluxZ[shoreTopIdx] += signedFlux; }
                }
                PropagateGridSalinityOnTransfer(salinityBits, fType, poolBotIdx, remPool, shoreTopIdx, shoreWasDry);
                MarkChangedWithNeighbors(changedMask, sx, shoreY, sz, sizeX, sizeY, sizeZ);
                MarkChangedWithNeighbors(changedMask, px, poolY, pz, sizeX, sizeY, sizeZ);
                if (shoreWasDry) changedMask[shoreTopIdx] |= axisNewlyWetFlag;
                return true;
            }

            return true;
        }

        /// <summary>
        /// Quinn MFD (Multi-Flow Direction) yamac onceligi:
        /// Asagi egimli tum vadi/oluk komsularina egimle orantili pay vererek suyun tek cizgide kilitlenmek yerine
        /// dogal talweg oluklarinda toplanmasini saglar.
        /// </summary>
        private static void EvaluateDonorSlopeRouting(
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            int sx,
            int y,
            int sz,
            int bedSrc,
            int headSrc,
            FluidType activeType,
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

            for (int d = 0; d < 4; d++)
            {
                int nx = sx + (d == 0 ? 1 : (d == 1 ? -1 : 0));
                int nz = sz + (d == 2 ? 1 : (d == 3 ? -1 : 0));
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
                ushort belowFluidN = y > 0 ? fluids[nIdx - strideY] : (ushort)0;
                // Hayali Ucurum Kilitlenme Tamiri (0e08a74a.jpg):
                // Eger komsu y'de bos ama y-1'de ayni tipte dengeli su (Mass < 255 olsa bile!) varsa,
                // gercek alt-katman hidrostatik kotunu kontrol et; zaten esit seviyedeki havuzu 1m ucurum sanip yan akisi kilitleme!
                if (nMass == 0 && y > 0 && !VoxelBitUtility.IsPhysicallySolid(belowN) &&
                    (belowFluidN & 0xFF) > 0 && (FluidType)((belowFluidN >> 8) & 0x03u) == activeType)
                {
                    byte belowPoolCap = FluidPorosityUtility.ComputeCellFluidCapacity(belowN);
                    if (belowPoolCap > 0)
                    {
                        uint below2N = y > 1 ? voxels[nIdx - 2 * strideY] : 0u;
                        ushort below2FluidN = y > 1 ? fluids[nIdx - 2 * strideY] : (ushort)0;
                        int bedBelowPool = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(below2N, below2FluidN, belowN, activeType, y - 1);
                        int headBelowPool = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedBelowPool, y - 1, (byte)(belowFluidN & 0xFF), belowPoolCap);
                        if (headBelowPool >= headSrc - 1)
                        {
                            continue;
                        }
                    }
                }
                int bedN = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowN, belowFluidN, voxels[nIdx], activeType, y);
                int headN = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedN, y, nMass, nCap);
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
                return;
            }

            int targetDrop = bedSrc - bedTarget;
            if (targetDrop + 6 < maxDownhillDrop)
            {
                hasSteeperAlternative = true;
                // Yalnizca duz veya yokus yukari yan komsuda (targetDrop <= 3) ince suyun dagilmasini kes;
                // egimli oluk yonlerinin (targetDrop > 3) Quinn MFD payi almasina izin ver!
                if (targetDrop <= 3 && srcMass < 135)
                {
                    slopeShare256 = 0;
                    return;
                }
            }

            for (int d = 0; d < 4; d++)
            {
                int nx = sx + (d == 0 ? 1 : (d == 1 ? -1 : 0));
                int nz = sz + (d == 2 ? 1 : (d == 3 ? -1 : 0));
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
                ushort belowFluidN = y > 0 ? fluids[nIdx - strideY] : (ushort)0;
                if (nMass == 0 && y > 0 && !VoxelBitUtility.IsPhysicallySolid(belowN) &&
                    (belowFluidN & 0xFF) > 0 && (FluidType)((belowFluidN >> 8) & 0x03u) == activeType)
                {
                    byte belowPoolCap = FluidPorosityUtility.ComputeCellFluidCapacity(belowN);
                    if (belowPoolCap > 0)
                    {
                        uint below2N = y > 1 ? voxels[nIdx - 2 * strideY] : 0u;
                        ushort below2FluidN = y > 1 ? fluids[nIdx - 2 * strideY] : (ushort)0;
                        int bedBelowPool = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(below2N, below2FluidN, belowN, activeType, y - 1);
                        int headBelowPool = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedBelowPool, y - 1, (byte)(belowFluidN & 0xFF), belowPoolCap);
                        if (headBelowPool >= headSrc - 1)
                        {
                            continue;
                        }
                    }
                }
                int bedN = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowN, belowFluidN, voxels[nIdx], activeType, y);
                int headN = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedN, y, nMass, nCap);
                int dh = headSrc - headN;
                if (dh <= 0)
                {
                    continue;
                }

                int drop = Math.Max(0, bedSrc - bedN);
                int w = drop > 3
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

            slopeShare256 = Math.Clamp((targetWeight * 256) / sumWeights, 20, 256);
        }

        /// <summary>
        /// Pass 2.5 — 45° Capraz Dik Dogal Yamac Kacisi (0-GC, Katı Duvar Sizdirmaz, dM == 0):
        /// 4-bagli Pass 2'de komsu kardinal hucrelerin yogunluk/SDF tabani yuzunden kilitlenen (changedMask == 0)
        /// kucuk su birikintilerinin, en az 1 ara kardinal gecit acik (cap > 0) olmak sartiyla, >= 0.63m (160 br)
        /// daha alcak capraz dogal arazi sutununa (y, y-1, y-2) suzulmesini saglar.
        /// </summary>
        private static void RunDiagonalSteepSlopeEscapePass(
            Span<uint> voxels,
            Span<ushort> fluids,
            byte[] changedMask,
            int sizeX,
            int sizeY,
            int sizeZ,
            int windowMinY,
            int windowMaxY,
            int stepParity,
            ulong[]? salinityBits,
            int[]? fluxX,
            int[]? fluxZ)
        {
            int strideY = sizeX * sizeZ;
            int minY = Math.Max(1, windowMinY);
            int maxY = Math.Min(sizeY - 1, windowMaxY);
            int startDir = stepParity & 3;

            for (int y = maxY; y >= minY; y--)
            {
                int yBase = y * strideY;
                for (int z = 1; z < sizeZ - 1; z++)
                {
                    int rowBase = yBase + z * sizeX;
                    for (int x = 1; x < sizeX - 1; x++)
                    {
                        int idx = rowBase + x;
                        if (changedMask[idx] != 0)
                        {
                            continue;
                        }

                        ushort raw = fluids[idx];
                        byte mass = (byte)(raw & 0xFF);
                        if (mass == 0 || mass > 120 || (raw & 0x0400u) != 0u)
                        {
                            continue;
                        }

                        FluidType fType = (FluidType)((raw >> 8) & 0x03u);
                        if (fType != FluidType.Water)
                        {
                            continue;
                        }

                        // Yalnizca dogal SDF arazi zemini (ShapeId == 0) ustunde oturan su; mimari havuz/kanala dokunma!
                        uint belowSrc = voxels[idx - strideY];
                        if (!VoxelBitUtility.IsPhysicallySolid(belowSrc) || VoxelBitUtility.GetShapeId(belowSrc) != 0)
                        {
                            continue;
                        }

                        byte srcCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[idx]);
                        if (srcCap == 0)
                        {
                            continue;
                        }

                        int bedSrc = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowSrc, fluids[idx - strideY], voxels[idx], fType, y);
                        int headSrc = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedSrc, y, mass, srcCap);

                        int bestTargetIdx = -1;
                        int bestTargetY = y;
                        int bestDx = 0;
                        int bestDz = 0;
                        int bestBedDrop = 159; // En az 160 br (~0.63m) daha alcak capraz yamaç gerektirir!
                        byte bestTargetMass = 0;
                        byte bestTargetCap = 0;

                        for (int k = 0; k < 4; k++)
                        {
                            int d = (startDir + k) & 3;
                            int ox = (d & 1) == 0 ? -1 : 1;
                            int oz = d < 2 ? -1 : 1;

                            // KATI DUVAR GUVENCESI (T14/T15/T16): En az bir ara kardinal hucre (x+ox, z) veya (x, z+oz) acik olmali!
                            byte flankCapA = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[yBase + z * sizeX + (x + ox)]);
                            byte flankCapB = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[yBase + (z + oz) * sizeX + x]);
                            if (flankCapA == 0 && flankCapB == 0)
                            {
                                continue;
                            }

                            int nx = x + ox;
                            int nz = z + oz;
                            int nColBase = nz * sizeX + nx;
                            // Hedef sutunun y katmani kapaliysa (duvar) capraz gecis yasak
                            if (FluidPorosityUtility.ComputeCellFluidCapacity(voxels[yBase + nColBase]) == 0)
                            {
                                continue;
                            }

                            // y, y-1 ve y-2 (en fazla 2 blok dusus) katmanlarinda dogal araziye oturan en ust acik hedefi bul
                            for (int dy = 0; dy <= 2; dy++)
                            {
                                int ty = y - dy;
                                if (ty < 1) break;
                                int tIdx = ty * strideY + nColBase;
                                byte tCap = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[tIdx]);
                                if (tCap == 0) break;

                                uint tBelowS = voxels[tIdx - strideY];
                                ushort tBelowF = fluids[tIdx - strideY];
                                bool tBelowSolid = VoxelBitUtility.IsPhysicallySolid(tBelowS);
                                bool tBelowSame = !tBelowSolid && (tBelowF & 0xFF) > 0 && (FluidType)((tBelowF >> 8) & 0x03u) == fType;

                                if (!tBelowSolid && !tBelowSame)
                                {
                                    // Havada (y-1 bos), bir alt katmana inmeye devam et
                                    continue;
                                }

                                if (tBelowSolid && VoxelBitUtility.GetShapeId(tBelowS) != 0)
                                {
                                    break;
                                }

                                ushort tRaw = fluids[tIdx];
                                byte tMass = (byte)(tRaw & 0xFF);
                                if (tMass > 0 && (FluidType)((tRaw >> 8) & 0x03u) != fType)
                                {
                                    break;
                                }

                                if (tCap <= tMass)
                                {
                                    break;
                                }

                                int bedDst = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(tBelowS, tBelowF, voxels[tIdx], fType, ty);
                                int headDst = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedDst, ty, tMass, tCap);
                                int bedDrop = bedSrc - bedDst;
                                if (bedDrop > bestBedDrop && headSrc > headDst + 1)
                                {
                                    bestBedDrop = bedDrop;
                                    bestTargetIdx = tIdx;
                                    bestTargetY = ty;
                                    bestDx = ox;
                                    bestDz = oz;
                                    bestTargetMass = tMass;
                                    bestTargetCap = tCap;
                                }
                                break;
                            }
                        }

                        if (bestTargetIdx >= 0)
                        {
                            int maxStepCap = mass == 1 ? 1 : Math.Min(mass - 1, Math.Max(1, (mass * 13) / 24));
                            int transfer = Math.Min(maxStepCap, bestTargetCap - bestTargetMass);
                            if (transfer > 0)
                            {
                                bool targetWasDry = bestTargetMass == 0;
                                byte rem = (byte)(mass - transfer);
                                fluids[idx] = rem > 0 ? FluidCell16.Pack(fType, rem, isSettled: false).RawValue : (ushort)0;
                                fluids[bestTargetIdx] = FluidCell16.Pack(fType, (byte)(bestTargetMass + transfer), isSettled: false).RawValue;
                                PropagateGridSalinityOnTransfer(salinityBits, fType, idx, rem, bestTargetIdx, targetWasDry);
                                MarkChangedWithNeighbors(changedMask, x, y, z, sizeX, sizeY, sizeZ);
                                MarkChangedWithNeighbors(changedMask, x + bestDx, bestTargetY, z + bestDz, sizeX, sizeY, sizeZ);
                                if (bestTargetY <= y - 2)
                                {
                                    changedMask[bestTargetIdx] |= 0x06;
                                }
                                if (fluxX != null && fluxZ != null)
                                {
                                    int fScaled = Math.Max(1, (transfer * 181) >> 8);
                                    fluxX[idx] += bestDx * fScaled;
                                    fluxZ[idx] += bestDz * fScaled;
                                    fluxX[bestTargetIdx] += bestDx * fScaled;
                                    fluxZ[bestTargetIdx] += bestDz * fScaled;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Asla hedef yuksekligini kaynak yuksekliginin ustune tasirmayan (overshoot = 0), dik egime oncelik veren
        /// ve yamactaki ince yagmur filminin kilitlenmeden suzulmesini saglayan sanal boru transfer cozucusu.
        /// </summary>
        private static int ComputeStableLateralTransfer(
            FluidType activeType,
            byte srcMass,
            byte srcCap,
            byte dstMass,
            byte dstCap,
            int bedSrc,
            int bedDst,
            int y,
            int headSrc,
            int headDst,
            bool hasSteeperAlternative = false,
            int slopeShare256 = 256,
            float forwardVelocityMps = 0f)
        {
            if (srcMass == 0 || dstCap <= dstMass || slopeShare256 <= 0)
            {
                return 0;
            }

            int diffH = headSrc - headDst;
            if (diffH <= 0)
            {
                return 0;
            }

            if (activeType == FluidType.Lava && diffH < LavaBinghamYieldThreshold)
            {
                return 0;
            }

            int srcSlope223 = (srcCap == 255 && bedSrc > (y - 1) * 255 && bedSrc < y * 255)
                ? Math.Max(223, (y + 1) * 255 - (bedSrc + 32))
                : 223;
            int dstSlope223 = (dstCap == 255 && bedDst > (y - 1) * 255 && bedDst < y * 255)
                ? Math.Max(223, (y + 1) * 255 - (bedDst + 32))
                : 223;
            int slopeSum = srcSlope223 + dstSlope223;
            int maxEquilibriumTransfer = Math.Max(1, (diffH * 223 + (slopeSum >> 1)) / slopeSum);

            int viscosityDiv = activeType == FluidType.Lava ? 4 : 2;
            bool isDownhill = bedSrc > bedDst;
            int desired;

            if (isDownhill)
            {
                int safeDrain = bedSrc - headDst;
                if (activeType == FluidType.Water && srcMass == 1 && safeDrain >= 1 && !hasSteeperAlternative)
                {
                    desired = 1;
                }
                else if (activeType == FluidType.Water && srcMass <= 24 && safeDrain >= srcMass && !hasSteeperAlternative)
                {
                    // KURAL (Paket 4B.2 Yagmur Akis Surekliligi):
                    // srcMass >= 2 iken tek tikte %100 aktarim YAPMA (srcMass - 1 ust siniri);
                    // boylece kaynak ve hedef hucre ayni anda islak kalarak _FluidSubTickAlpha ile isinlanmadan kesintisiz akar!
                    desired = Math.Min(srcMass - 1, Math.Max(1, (srcMass * 13) / 24));
                }
                else if (activeType == FluidType.Water && srcMass <= 24 && (bedSrc - bedDst) >= 4)
                {
                    int slopeCap = Math.Max(1, (srcMass * 13) / 24);
                    desired = Math.Min(maxEquilibriumTransfer, slopeCap);
                    if (slopeShare256 < 256)
                    {
                        desired = Math.Max(1, (desired * slopeShare256 + 128) >> 8);
                    }
                }
                else
                {
                    if (diffH < 2 && !(activeType == FluidType.Water && forwardVelocityMps >= 0.25f))
                    {
                        return 0;
                    }

                    int momentumBonus = activeType == FluidType.Water && forwardVelocityMps > 0.0f
                        ? (int)MathF.Round(forwardVelocityMps * 4.5f)
                        : 0;
                    int rawPipe = activeType == FluidType.Water
                        ? Math.Max(1, ((diffH * 168) >> 8) + momentumBonus)
                        : diffH / viscosityDiv;
                    if (rawPipe <= 0)
                    {
                        return 0;
                    }

                    int slopeCap = activeType == FluidType.Water
                        ? Math.Max(2, (srcMass * 15) / 24)
                        : Math.Max(24, (srcMass * 65) / 100);
                    desired = Math.Min(rawPipe, Math.Min(slopeCap, maxEquilibriumTransfer));
                    if (slopeShare256 < 256)
                    {
                        desired = Math.Max(1, (desired * slopeShare256 + 128) >> 8);
                    }
                }
            }
            else
            {
                if (diffH < 2 && !(activeType == FluidType.Water && forwardVelocityMps >= 0.35f && dstMass > 0))
                {
                    return 0;
                }

                if (activeType == FluidType.Water && dstMass == 0 && bedSrc == bedDst && srcMass < 6)
                {
                    return 0;
                }

                if (hasSteeperAlternative)
                {
                    desired = Math.Min(32, Math.Max(1, ((diffH / 2) * slopeShare256) >> 8));
                }
                else
                {
                    int momentumBonus = activeType == FluidType.Water && forwardVelocityMps > 0.0f
                        ? (int)MathF.Round(forwardVelocityMps * 3.5f)
                        : 0;
                    desired = activeType == FluidType.Water
                        ? Math.Min(112, Math.Max(1, ((diffH * 164) >> 8) + momentumBonus))
                        : diffH / viscosityDiv;
                }

                desired = Math.Min(desired, maxEquilibriumTransfer);
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
            int windowMinY,
            int windowMaxY,
            ulong[]? salinityBits = null)
        {
            int strideY = sizeZ * sizeX;
            int scanMinY = Math.Clamp(windowMinY, 0, sizeY - 1);
            int scanMaxY = Math.Clamp(windowMaxY, scanMinY, sizeY - 1);

            // A) Yukaridan asagiya 5-Bit PressureHead (0..31) dikey sutun hesaplamasi (Yalnizca islak Y bandi!)
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    byte head = 0;
                    FluidType colType = FluidType.None;
                    for (int y = scanMaxY; y >= scanMinY; y--)
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

            // B) Birlesik Kaplar (U-Borusu) & <= 10m Sifon BFS (Yalnizca [scanMinY..scanMaxY] bandi!)
            int stamp = ++_globalStampCounter;
            if (stamp >= int.MaxValue - 16)
            {
                Array.Clear(visitedStamp, 0, visitedStamp.Length);
                _globalStampCounter = 1;
                stamp = 1;
            }

            for (int y = scanMinY; y <= scanMaxY; y++)
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
                                int dOx = d == 0 ? 1 : (d == 1 ? -1 : 0);
                                int dOy = d == 2 ? 1 : (d == 3 ? -1 : 0);
                                int dOz = d == 4 ? 1 : (d == 5 ? -1 : 0);
                                int nx = cx + dOx;
                                int ny = cy + dOy;
                                int nz = cz + dOz;
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
                                if (dOy == 1 && nCell.Type == pipeType && nCell.Mass > 0)
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

                                if (dOy == 1 && (nCell.Type == FluidType.None || nCell.Type == pipeType) && nCap >= 128 && (nCap - nCell.Mass) >= 16)
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

        private static void EnsureScratch(
            int totalCells,
            out int[] visitedStamp,
            out int[] bfsQueue,
            out byte[] activeNext,
            bool needFluxBuffers,
            out int[]? fluxX,
            out int[]? fluxZ)
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

            if (needFluxBuffers)
            {
                if (_tlsFluxX == null || _tlsFluxX.Length < totalCells || _tlsFluxZ == null || _tlsFluxZ.Length < totalCells)
                {
                    int cap = Math.Max(totalCells, 4096);
                    _tlsFluxX = new int[cap];
                    _tlsFluxZ = new int[cap];
                }

                fluxX = _tlsFluxX;
                fluxZ = _tlsFluxZ;
            }
            else
            {
                fluxX = null;
                fluxZ = null;
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
