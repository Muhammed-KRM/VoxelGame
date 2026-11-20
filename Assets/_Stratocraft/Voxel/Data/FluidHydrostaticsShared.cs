#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// Fizik CA (<c>FluidPorosityUtility</c> / <c>FluidCellularAutomataJob</c>) ile <c>FluidSurfaceSkirtMesher</c>
    /// arasindaki ORTAK hidrostatik matematik kaynagi (Tek Gercek Kaynak — Paket 4B.3 F0 + F1-a).
    /// 0-GC, Burst ve Standalone .NET 10 uyumludur.
    /// </summary>
    public static class FluidHydrostaticsShared
    {
        public const int FluidTypeMask = 0x03;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float UnitsToMeters(int units) => units * (1.0f / 255.0f);

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
                    // Hava tarafindaki dar-bant SDF hucresi (kazma erozyonu d < 64 dahil) tam akiskan alir
                    return 255;
                }

                // SurfaceNets kati arazi vokselleri (Density >= 64) gecirimsiz tabandir
                return 0;
            }

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
        /// Bir (x, y, z) sivi hucresinin hidrostatik taban yuksekligini [0..255] sabit noktali kutle esdegeri
        /// biriminde (255 birim = 1.0 metre) hesaplar.
        /// F1-a Surekli Taban Kurali: Alt hucrede (y - 1) ayni tipte akiskan varsa, dolu olmasa bile
        /// (y - 1) * 255'e 1m sicrama yapmaz; alt hucrenin gercek doluluk kotunu doner (belowFMass -> belowCap iken y * 255'e surekli yaklasir).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeCellHydrostaticBedUnits255(
            uint belowSolidRaw,
            ushort belowFluidRaw,
            uint currentSolidRaw,
            byte activeFluidType,
            int y)
        {
            byte curMat = VoxelBitUtility.GetMaterialId(currentSolidRaw);
            byte curShape = VoxelBitUtility.GetShapeId(currentSolidRaw);
            if (curMat != MaterialIdCatalog.Air && curShape != 0 && curShape != VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                byte cap = ComputeCellFluidCapacity(currentSolidRaw);
                if (cap > 0 && cap < 255)
                {
                    return y * 255 + (255 - cap);
                }
            }

            if (y <= 0)
            {
                return 0;
            }

            byte belowFMass = (byte)(belowFluidRaw & 0xFFu);
            byte belowFType = (byte)((belowFluidRaw >> 8) & FluidTypeMask);
            if (belowFMass > 0 && (activeFluidType == 0 || belowFType == activeFluidType))
            {
                byte belowCap = ComputeCellFluidCapacity(belowSolidRaw);
                if (belowCap > 0)
                {
                    if (belowFMass >= belowCap)
                    {
                        return y * 255;
                    }

                    int solidBase = 255 - belowCap;
                    return (y - 1) * 255 + solidBase + belowFMass;
                }
            }

            return ComputeCellBedElevationUnits255(belowSolidRaw, currentSolidRaw, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeCellBedElevationUnits255(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            byte curMat = VoxelBitUtility.GetMaterialId(currentSolidRaw);
            byte curShape = VoxelBitUtility.GetShapeId(currentSolidRaw);
            if (curMat != MaterialIdCatalog.Air && curShape != 0 && curShape != VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                byte cap = ComputeCellFluidCapacity(currentSolidRaw);
                if (cap > 0 && cap < 255)
                {
                    return y * 255 + (255 - cap);
                }
            }

            if (y <= 0)
            {
                return 0;
            }

            if (!VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
            {
                return (y - 1) * 255;
            }

            byte belowShape = VoxelBitUtility.GetShapeId(belowSolidRaw);
            if (belowShape == 0)
            {
                int dBelow = VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw);
                if (dBelow < StratocraftConstants.SdfIsoLevel)
                {
                    return (y - 1) * 255;
                }

                int dCurr = (curMat != MaterialIdCatalog.Air && curShape == 0)
                    ? VoxelBitUtility.GetDensityOrLiquid(currentSolidRaw)
                    : 0;

                int isoOffset255 = dCurr > 0
                    ? Math.Clamp(((dBelow - StratocraftConstants.SdfIsoLevel) * 255) / Math.Max(1, dBelow - dCurr), 4, 251)
                    : Math.Clamp(((dBelow - StratocraftConstants.SdfIsoLevel) * 255 + 30) / 60, 4, 255);
                return (y - 1) * 255 + isoOffset255;
            }

            return y * 255;
        }

        /// <summary>
        /// Faz 4B.4-B: Dogal SDF (ShapeId==0) kati bir (belowSolidRaw) hucresi ile hemen ustundeki
        /// kati-olmayan (aboveSolidRaw) hucre arasindaki gercek HybridSurfaceNets izo-yuzey ofsetini [0.016..0.984m] doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeNaturalSdfSurfaceOffsetMeters(uint belowSolidRaw, uint aboveSolidRaw)
        {
            if (VoxelBitUtility.GetShapeId(belowSolidRaw) != 0)
            {
                return 1.0f;
            }

            int dBelow = VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw);
            if (dBelow < StratocraftConstants.SdfIsoLevel)
            {
                return 0.0f;
            }

            int dAbove = 0;
            if (!VoxelBitUtility.IsPhysicallySolid(aboveSolidRaw) &&
                VoxelBitUtility.GetMaterialId(aboveSolidRaw) != MaterialIdCatalog.Air &&
                VoxelBitUtility.GetShapeId(aboveSolidRaw) == 0)
            {
                dAbove = VoxelBitUtility.GetDensityOrLiquid(aboveSolidRaw);
            }

            int denom = Math.Max(1, dBelow - dAbove);
            int isoOffset255 = Math.Clamp(((dBelow - StratocraftConstants.SdfIsoLevel) * 255) / denom, 4, 251);
            return isoOffset255 * (1.0f / 255.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeCellHydrostaticHeadUnits255(int bedUnits, int y, byte mass, byte capacity = 255)
        {
            if (mass == 0)
            {
                return bedUnits;
            }

            int ceilUnits = (y + 1) * 255;
            if (capacity == 0 || mass >= capacity)
            {
                return bedUnits > (y - 1) * 255 ? Math.Max(bedUnits + mass, ceilUnits) : (bedUnits + mass);
            }

            if (capacity != 255)
            {
                return bedUnits + (mass * 255) / capacity;
            }

            if (bedUnits >= y * 255 || bedUnits <= (y - 1) * 255)
            {
                return bedUnits + mass;
            }

            if (mass <= 32)
            {
                return bedUnits + mass;
            }

            int base32 = bedUnits + 32;
            int remSpan = ceilUnits - base32;
            return base32 + ((mass - 32) * remSpan + 111) / 223;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ComputeMassNeededForTargetHeadUnits255(int bedUnits, int y, int targetHeadUnits, byte capacity = 255)
        {
            if (capacity == 0 || targetHeadUnits <= bedUnits)
            {
                return 0;
            }

            int ceilUnits = (y + 1) * 255;
            if (targetHeadUnits >= ceilUnits)
            {
                return capacity;
            }

            if (capacity != 255)
            {
                int scaled = ((targetHeadUnits - bedUnits) * capacity + 127) / 255;
                return (byte)Math.Clamp(scaled, 1, (int)capacity);
            }

            if (bedUnits >= y * 255 || bedUnits <= (y - 1) * 255)
            {
                return (byte)Math.Clamp(targetHeadUnits - bedUnits, 1, 255);
            }

            int delta = targetHeadUnits - bedUnits;
            if (delta <= 32)
            {
                return (byte)Math.Clamp(delta, 1, 32);
            }

            int base32 = bedUnits + 32;
            int remSpan = Math.Max(223, ceilUnits - base32);
            int m = 32 + ((targetHeadUnits - base32) * 223 + (remSpan >> 1)) / remSpan;
            return (byte)Math.Clamp(m, 1, 255);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeColumnSurfaceNetsBedMeters(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            return ComputeCellBedElevationUnits255(belowSolidRaw, currentSolidRaw, y) * (1.0f / 255.0f);
        }
    }
}
