using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Jobs
{
    /// <summary>
    /// Hacim korunumlu (Volume-Conservative) 16-Bit Su (mu=1) ve Lav (mu=4) akisi ile
    /// Tek Kullanimlik Stokiyometrik Reaksiyon (Water &gt;= 128 &amp;&amp; Lava &gt;= 128 -&gt; 1 adet Dogal Obsidyen, OriginState=0;
    /// dusuk lav hacminde Bazalt veya Kiriktas) ureten Burst uyumlu is.
    /// Sonsuz obsidyen jeneratoru fiziksel olarak imkansizdir!
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 2.4 ve GELISTIRICI DOKUMANI Bolum 4.13.5.
    /// </summary>
    public static class FluidCellularAutomataJob
    {
        /// <summary>
        /// Iki komsu akiskan hucresi (Su ve Lav) temas ettiginde stokiyometrik faz gecisi reaksiyonunu calistirir.
        /// Eger reaksiyon gerceklesirse her iki akiskan tuketilir ve olusan kati VoxelCell (NaturalObsidian, Basalt veya Cobblestone) doner.
        /// </summary>
        public static bool TryReactWaterAndLava(
            ref FluidCell16 waterCell,
            ref FluidCell16 lavaCell,
            out VoxelCell formedSolidBlock)
        {
            formedSolidBlock = VoxelCell.Air;

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

            // Durum 1: Water >= 128 ve Lava >= 128 -> Tek Kullanimlik Stokiyometrik Dogal Obsidyen (OriginState = DogalKutle = 0)
            if (waterMass >= threshold && lavaMass >= threshold)
            {
                byte remWater = (byte)(waterMass - threshold);
                byte remLava = (byte)(lavaMass - threshold);
                waterCell = FluidCell16.Pack(FluidType.Water, remWater);
                lavaCell = FluidCell16.Pack(FluidType.Lava, remLava);

                uint obsidianRaw = VoxelBitUtility.PackStandard(
                    MaterialIdCatalog.NaturalObsidian,
                    VoxelBitUtility.FullDensity,
                    shapeId: 1,
                    rot24: 0,
                    BlokKokenDurumu.DogalKutle,
                    dmg4: 0);
                formedSolidBlock = new VoxelCell(obsidianRaw);
                return true;
            }

            // Durum 2: Dusuk hacimli temas (Lava < 128 veya Water < 128) -> Lav tuketilir, Bazalt veya Kiriktas olusur!
            byte consumed = Math.Min(waterMass, lavaMass);
            waterCell = FluidCell16.Pack(FluidType.Water, (byte)(waterMass - consumed));
            lavaCell = FluidCell16.Empty; // Lav tamamen katilasir

            byte solidMatId = lavaMass >= 64 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Cobblestone;
            uint rockRaw = VoxelBitUtility.PackStandard(
                solidMatId,
                VoxelBitUtility.FullDensity,
                shapeId: 1,
                rot24: 0,
                BlokKokenDurumu.DogalKutle,
                dmg4: 0);
            formedSolidBlock = new VoxelCell(rockRaw);
            return true;
        }

        /// <summary>
        /// 16x256x16 (veya N boyutlu) izgara uzerinde hacim korunumlu (Toplam Kutle Sabit) 1 adim akiskan simulasyonu calistirir.
        /// </summary>
        public static long StepFluidGridVolumeConservative(
            Span<uint> voxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            out int obsidianFormedCount,
            out int basaltOrCobbleFormedCount)
        {
            obsidianFormedCount = 0;
            basaltOrCobbleFormedCount = 0;

            int strideY = sizeZ * sizeX;

            // Yukaridan asagiya degil, asagidan yukariya yercekimi + yanal yayilim
            for (int y = 1; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int idx = y * strideY + z * sizeX + x;
                        var cell = new FluidCell16(fluids[idx]);
                        if (cell.Mass == 0 || cell.Type == FluidType.None)
                        {
                            continue;
                        }

                        // 1. Alt komsuya (-Y) yercekimi akisi veya Su+Lav reaksiyonu
                        int belowIdx = (y - 1) * strideY + z * sizeX + x;
                        if ((voxels[belowIdx] & 0xFFu) == 0u)
                        {
                            var belowFluid = new FluidCell16(fluids[belowIdx]);
                            if (belowFluid.Type == FluidType.None || belowFluid.Type == cell.Type)
                            {
                                int space = 255 - belowFluid.Mass;
                                if (space > 0)
                                {
                                    // Lav (mu=4) daha yavas akar, Su (mu=1) tam akar
                                    int maxTransfer = cell.Type == FluidType.Lava ? Math.Min(cell.Mass, (byte)64) : cell.Mass;
                                    int transfer = Math.Min(space, maxTransfer);
                                    if (transfer > 0)
                                    {
                                        fluids[belowIdx] = FluidCell16.Pack(cell.Type, (byte)(belowFluid.Mass + transfer), flowDir: 3).RawValue;
                                        byte rem = (byte)(cell.Mass - transfer);
                                        cell = FluidCell16.Pack(cell.Type, rem);
                                        fluids[idx] = cell.RawValue;
                                    }
                                }
                            }
                            else if ((cell.Type == FluidType.Water && belowFluid.Type == FluidType.Lava) ||
                                     (cell.Type == FluidType.Lava && belowFluid.Type == FluidType.Water))
                            {
                                FluidCell16 w = cell.Type == FluidType.Water ? cell : belowFluid;
                                FluidCell16 l = cell.Type == FluidType.Lava ? cell : belowFluid;
                                if (TryReactWaterAndLava(ref w, ref l, out VoxelCell formed))
                                {
                                    voxels[belowIdx] = formed.RawValue;
                                    fluids[belowIdx] = (belowFluid.Type == FluidType.Water ? w : l).RawValue;
                                    cell = cell.Type == FluidType.Water ? w : l;
                                    fluids[idx] = cell.RawValue;
                                    if (formed.MaterialId == MaterialIdCatalog.NaturalObsidian) obsidianFormedCount++;
                                    else basaltOrCobbleFormedCount++;
                                }
                            }
                        }

                        if (cell.Mass <= 1)
                        {
                            continue;
                        }

                        // 2. Yanal (+X, -X, +Z, -Z) basinc dengeleme ve Su+Lav reaksiyonu
                        TryLateralTransferOrReact(
                            voxels, fluids, idx, x + 1 < sizeX ? idx + 1 : -1,
                            ref cell, ref obsidianFormedCount, ref basaltOrCobbleFormedCount);
                        TryLateralTransferOrReact(
                            voxels, fluids, idx, x - 1 >= 0 ? idx - 1 : -1,
                            ref cell, ref obsidianFormedCount, ref basaltOrCobbleFormedCount);
                        TryLateralTransferOrReact(
                            voxels, fluids, idx, z + 1 < sizeZ ? idx + sizeX : -1,
                            ref cell, ref obsidianFormedCount, ref basaltOrCobbleFormedCount);
                        TryLateralTransferOrReact(
                            voxels, fluids, idx, z - 1 >= 0 ? idx - sizeX : -1,
                            ref cell, ref obsidianFormedCount, ref basaltOrCobbleFormedCount);
                    }
                }
            }

            long totalRemainingMass = 0;
            for (int i = 0; i < fluids.Length; i++)
            {
                totalRemainingMass += (fluids[i] & 0xFF);
            }
            return totalRemainingMass;
        }

        private static void TryLateralTransferOrReact(
            Span<uint> voxels,
            Span<ushort> fluids,
            int srcIdx,
            int dstIdx,
            ref FluidCell16 srcCell,
            ref int obsidianFormedCount,
            ref int basaltOrCobbleFormedCount)
        {
            if (dstIdx < 0 || srcCell.Mass == 0 || (voxels[dstIdx] & 0xFFu) != 0u)
            {
                return;
            }

            var dstCell = new FluidCell16(fluids[dstIdx]);
            if (dstCell.Type == FluidType.None || dstCell.Type == srcCell.Type)
            {
                int diff = srcCell.Mass - dstCell.Mass;
                int viscosityDiv = srcCell.Type == FluidType.Lava ? 4 : 2;
                int transfer = diff / viscosityDiv;
                if (transfer > 0 && dstCell.Mass + transfer <= 255)
                {
                    dstCell = FluidCell16.Pack(srcCell.Type, (byte)(dstCell.Mass + transfer));
                    srcCell = FluidCell16.Pack(srcCell.Type, (byte)(srcCell.Mass - transfer));
                    fluids[dstIdx] = dstCell.RawValue;
                    fluids[srcIdx] = srcCell.RawValue;
                }
            }
            else if ((srcCell.Type == FluidType.Water && dstCell.Type == FluidType.Lava) ||
                     (srcCell.Type == FluidType.Lava && dstCell.Type == FluidType.Water))
            {
                FluidCell16 w = srcCell.Type == FluidType.Water ? srcCell : dstCell;
                FluidCell16 l = srcCell.Type == FluidType.Lava ? srcCell : dstCell;
                if (TryReactWaterAndLava(ref w, ref l, out VoxelCell formed))
                {
                    voxels[dstIdx] = formed.RawValue;
                    fluids[dstIdx] = (dstCell.Type == FluidType.Water ? w : l).RawValue;
                    srcCell = srcCell.Type == FluidType.Water ? w : l;
                    fluids[srcIdx] = srcCell.RawValue;
                    if (formed.MaterialId == MaterialIdCatalog.NaturalObsidian) obsidianFormedCount++;
                    else basaltOrCobbleFormedCount++;
                }
            }
        }
    }
}
