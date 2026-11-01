#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D.1 — Clamped SDF Kiyi Gozenekliligi (Porosity) ve
    /// ShapeID == 63 Mikro-Voxel Su Olugu / Boru Kapasitesi Hesaplayicisi:
    /// - Hava hucresi: V_max = 255
    /// - Demir Izgara (IronGrating = 125): V_max = 224 (su ve oksijen icinden gecer!)
    /// - Dogal SDF Kiyi Hucresi (ShapeID == 0, Density &lt; 96): Egimli SDF arazi kiyisinda suyun
    ///    yuzey altina girmesini saglayan kismi gozenek kapasitesi!
    /// - Yontulmus Mikro-Voxel (ShapeID == 63): (64 - PopCount(mask64)) * 255 / 64 ile 0.25m su arklarina izin verir!
    /// </summary>
    public static class FluidPorosityUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ComputeCellFluidCapacity(uint solidVoxelRaw, ulong optionalMicroMask64 = ulong.MaxValue)
        {
            byte matId = VoxelBitUtility.GetMaterialId(solidVoxelRaw);
            if (matId == MaterialIdCatalog.Air)
            {
                return 255;
            }

            if (matId == MaterialIdCatalog.IronGrating)
            {
                return 224;
            }

            byte shapeId = VoxelBitUtility.GetShapeId(solidVoxelRaw);
            if (shapeId == VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                int filledSubVoxels = BitOps.PopCount(optionalMicroMask64);
                int openSubVoxels = Math.Clamp(64 - filledSubVoxels, 0, 64);
                return (byte)((openSubVoxels * 255) / 64);
            }

            if (shapeId == 0)
            {
                byte density = VoxelBitUtility.GetDensityOrLiquid(solidVoxelRaw);
                if (density < StratocraftConstants.SdfIsoLevel)
                {
                    // Hava tarafindaki dar-bant SDF hucresi tam akiskan alabilir
                    return 255;
                }

                if (density >= StratocraftConstants.SdfConfinedSolidLevel)
                {
                    // Derin kaya cekirdegi gecirimsizdir
                    return 0;
                }

                // 64 <= Density < 96 kiyi yuzeyi hucresi: egimli Smooth Surface Nets kiyisiyla bosluksuz temas icin kismi kapasite
                int shoreOpen = StratocraftConstants.SdfConfinedSolidLevel - density; // 1..32
                return (byte)Math.Clamp((shoreOpen * 96) / 32, 0, 96);
            }

            // Mimari yarim levha / kiris sekillerinde kismi bosluk kapasitesi, tam kup (1) ise 0
            return shapeId switch
            {
                ArchitecturalShapeCatalog.HalfSlab => 128,
                ArchitecturalShapeCatalog.QuarterBeam => 192,
                ArchitecturalShapeCatalog.Ramp22Low => 160,
                ArchitecturalShapeCatalog.Ramp45 => 128,
                _ => 0
            };
        }

        /// <summary>
        /// Bir hucreye kati blok konuldugunda veya Madde Kristali ile SDF kutlesi eklendiginde,
        /// azalan V_max kapasitesini asan siviyi yok etmek yerine komsu acik hucrelere (+Y, +-X, +-Z) koruyarak iter!
        /// </summary>
        public static int DisplaceExcessFluidOnSolidEdit(
            ReadOnlySpan<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            int editX,
            int editY,
            int editZ)
        {
            int strideY = sizeZ * sizeX;
            int idx = editY * strideY + editZ * sizeX + editX;
            FluidCell16 cell = new FluidCell16(fluids[idx]);
            if (cell.Mass == 0 || cell.Type == FluidType.None)
            {
                return 0;
            }

            byte newCap = ComputeCellFluidCapacity(solidVoxels[idx]);
            if (cell.Mass <= newCap)
            {
                fluids[idx] = FluidCell16.WakeRaw(fluids[idx]);
                return 0;
            }

            int excess = cell.Mass - newCap;
            FluidType fType = cell.Type;
            fluids[idx] = newCap > 0 ? FluidCell16.Pack(fType, newCap, isSettled: false).RawValue : (ushort)0;

            // Oncelikle yukari (+Y), sonra 4 yatay komsuya, en son asagiya (-Y) hacim korunumlu dagit
            ReadOnlySpan<int> dx = stackalloc int[6] { 0, 1, -1, 0, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[6] { 1, 0, 0, 0, 0, -1 };
            ReadOnlySpan<int> dz = stackalloc int[6] { 0, 0, 0, 1, -1, 0 };

            int displaced = 0;
            for (int d = 0; d < 6 && excess > 0; d++)
            {
                int nx = editX + dx[d];
                int ny = editY + dy[d];
                int nz = editZ + dz[d];
                if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                {
                    continue;
                }

                int nIdx = ny * strideY + nz * sizeX + nx;
                byte nCap = ComputeCellFluidCapacity(solidVoxels[nIdx]);
                if (nCap == 0)
                {
                    continue;
                }

                FluidCell16 nCell = new FluidCell16(fluids[nIdx]);
                if (nCell.Type != FluidType.None && nCell.Type != fType)
                {
                    continue;
                }

                int space = nCap - nCell.Mass;
                if (space <= 0)
                {
                    continue;
                }

                int push = Math.Min(excess, space);
                fluids[nIdx] = FluidCell16.Pack(fType, (byte)(nCell.Mass + push), isSettled: false).RawValue;
                excess -= push;
                displaced += push;
            }

            return displaced;
        }
    }
}
