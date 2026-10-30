using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Her materyalin yapisal basinc dayanimi (Crush Strength), maksimum yatay konsol/kemer uzanimi (Cantilever Limit),
    /// Protodyakonov kayac sertlik katsayisi (Terzaghi Loosening Dome icin) ve birim kutlesi.
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 2.3.
    /// </summary>
    public readonly struct StructuralMaterialProps
    {
        /// <summary>1m^3 blogun kutlesi (kg).</summary>
        public readonly float MassKg;

        /// <summary>Kolonun tasiyabilecegi maksimum dikey yuk (kg).</summary>
        public readonly float CompressiveCrushLimitKg;

        /// <summary>Alttan desteksiz yatay uzanabilecegi maksimum konsol/kemer mesafesi (blok).</summary>
        public readonly int MaxCantileverSpanBlocks;

        /// <summary>Terzaghi gevseme kubbesi (H_arch = min(8, floor(B_span / (2 * f)))) icin Protodyakonov sertlik katsayisi.</summary>
        public readonly float ProtodyakonovHardnessF;

        /// <summary>Yeni bir yapisal materyal ozellik kaydi olusturur.</summary>
        public StructuralMaterialProps(
            float massKg,
            float compressiveCrushLimitKg,
            int maxCantileverSpanBlocks,
            float protodyakonovHardnessF)
        {
            MassKg = massKg;
            CompressiveCrushLimitKg = compressiveCrushLimitKg;
            MaxCantileverSpanBlocks = maxCantileverSpanBlocks;
            ProtodyakonovHardnessF = protodyakonovHardnessF;
        }

        /// <summary>Verilen MaterialID icin kanonik yapisal fizik parametrelerini doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructuralMaterialProps GetForMaterial(byte materialId)
        {
            return materialId switch
            {
                MaterialIdCatalog.Air => new StructuralMaterialProps(0f, 0f, 0, 1f),
                MaterialIdCatalog.Bedrock => new StructuralMaterialProps(10000f, float.MaxValue, 64, 20f),
                MaterialIdCatalog.Sand or MaterialIdCatalog.Gravel => new StructuralMaterialProps(1600f, 16000f, 0, 0.5f),
                MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass => new StructuralMaterialProps(1400f, 28000f, 2, 1.0f),
                MaterialIdCatalog.OakWood => new StructuralMaterialProps(700f, 42000f, 4, 1.5f),
                MaterialIdCatalog.Ironwood or MaterialIdCatalog.SpiritWood => new StructuralMaterialProps(950f, 85000f, 6, 2.5f),
                MaterialIdCatalog.Cobblestone => new StructuralMaterialProps(2200f, 88000f, 3, 2.0f),
                MaterialIdCatalog.Stone or MaterialIdCatalog.Basalt => new StructuralMaterialProps(2500f, 150000f, 4, 3.0f),
                MaterialIdCatalog.NaturalObsidian => new StructuralMaterialProps(2800f, 320000f, 6, 5.0f),
                MaterialIdCatalog.Steel or MaterialIdCatalog.DamascusSteel => new StructuralMaterialProps(7800f, 950000f, 10, 8.0f),
                >= MaterialIdCatalog.DungeonSanctumImmuneStart => new StructuralMaterialProps(5000f, float.MaxValue, 64, 20f),
                _ => new StructuralMaterialProps(2400f, 120000f, 4, 2.5f)
            };
        }

        /// <summary>
        /// Uc Eksenli Kayac Hapsi (Triaxial Rock Confinement):
        /// sigma_c,eff = sigma_c,0 * (1 + 4.0 * N_confined / 6).
        /// Yanal olarak >= 4 yuzu dogal kayayla cevrili bloklar sonsuz tasiyici ana kaya (Bedrock Anchor) kabul edilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeEffectiveCrushLimit(int confinedNeighborFaces)
        {
            if (confinedNeighborFaces >= 4)
            {
                return float.MaxValue;
            }
            return CompressiveCrushLimitKg * (1.0f + (4.0f * confinedNeighborFaces) / 6.0f);
        }

        /// <summary>
        /// Terzaghi Gevseme Kubbesi Yuksekligi: H_arch = min(8, floor(B_span / (2 * f_protodyakonov))).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ComputeTerzaghiArchHeight(int unsupportedSpanBlocks)
        {
            if (unsupportedSpanBlocks <= MaxCantileverSpanBlocks)
            {
                return 0;
            }

            float denom = 2.0f * (ProtodyakonovHardnessF <= 0.1f ? 0.1f : ProtodyakonovHardnessF);
            int arch = (int)(unsupportedSpanBlocks / denom);
            if (arch < 1) arch = 1;
            if (arch > 8) arch = 8;
            return arch;
        }
    }
}
