#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Yapisal materyal davranis bayraklari (GAME LOGIC BIBLE v7.0 Bolum 2.7.C).
    /// </summary>
    [Flags]
    public enum StructuralMaterialFlags : byte
    {
        None = 0,
        /// <summary>Kum, cakil, gevsek moloz gibi yatay konsol tasimayan taneli malzeme.</summary>
        IsGranular = 1 << 0,
        /// <summary>Kemer/kilit tasi (Keystone) yatay itki aktarimi kurabilen kagir/metal malzeme.</summary>
        IsArchCapable = 1 << 1,
        /// <summary>Floatstone (Ucantas) gibi gokyuzu adasi ankraji (d_sup = 0).</summary>
        IsSkyAnchor = 1 << 2,
        /// <summary>Bedrock veya Kadim Zindan kutsal taslari gibi kirilmaz sonsuz mesnet.</summary>
        IsUnbreakableAnchor = 1 << 3
    }

    /// <summary>
    /// L1 onbellek dostu 8-Byte kanonik yapisal materyal spesifikasyonu (256 giris = 2 KB statik LUT).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
    public readonly struct VoxelMaterialSpec8
    {
        /// <summary>Birim hacim kutlesi (100 kg birimi, orn. 25 = 2.500 kg/m^3).</summary>
        public readonly byte DensityHectoKg;

        /// <summary>Dikey ezilme kapasitesi (maksimum tasinabilir ozdes blok sutunu yuksekligi; ushort.MaxValue = sonsuz).</summary>
        public readonly ushort MaxColumnBlocks;

        /// <summary>Yanal kesme / makaslama kapasitesi orani (0..255 -> 0.0..1.0 * CrushLimit).</summary>
        public readonly byte ShearRatio255;

        /// <summary>Tek kollu maksimum desteksiz yatay konsol uzanimi (blok).</summary>
        public readonly byte MaxCantileverBlocks;

        /// <summary>Yanal jeodezik kemer maliyeti kappa (w_arch = max(1, kappa / 2)).</summary>
        public readonly byte ArchCostKappa;

        /// <summary>Terzaghi Protodyakonov kayac sertlik katsayisi * 10 (orn. 30 = 3.0).</summary>
        public readonly byte ProtodyakonovF10;

        /// <summary>Yapisal davranis bayraklari.</summary>
        public readonly StructuralMaterialFlags Flags;

        public VoxelMaterialSpec8(
            byte densityHectoKg,
            ushort maxColumnBlocks,
            byte shearRatio255,
            byte maxCantileverBlocks,
            byte archCostKappa,
            byte protodyakonovF10,
            StructuralMaterialFlags flags)
        {
            DensityHectoKg = densityHectoKg;
            MaxColumnBlocks = maxColumnBlocks;
            ShearRatio255 = shearRatio255;
            MaxCantileverBlocks = maxCantileverBlocks;
            ArchCostKappa = archCostKappa;
            ProtodyakonovF10 = protodyakonovF10;
            Flags = flags;
        }
    }

    /// <summary>
    /// Her materyalin yapisal basinc dayanimi (Crush Strength), kesme dayanimi (Shear Limit),
    /// maksimum yatay konsol/kemer uzanimi (Cantilever &amp; Arch Limit), Protodyakonov kayac sertlik katsayisi
    /// ve birim kutlesi. GAME LOGIC BIBLE v7.0 Bolum 2.7.C kanonik degerleriyle birebir uyumludur.
    /// </summary>
    public readonly struct StructuralMaterialProps
    {
        private static readonly VoxelMaterialSpec8[] Specs256 = BuildCanonicalLut256();

        /// <summary>1m^3 blogun kutlesi (kg).</summary>
        public readonly float MassKg;

        /// <summary>Kolonun tasiyabilecegi maksimum dikey yuk (kg).</summary>
        public readonly float CompressiveCrushLimitKg;

        /// <summary>Yanal konsol/kemer kesme yuku limiti (kg).</summary>
        public readonly float ShearCrushLimitKg;

        /// <summary>Tek tarafli desteksiz yatay uzanabilecegi maksimum konsol mesafesi (blok).</summary>
        public readonly int MaxCantileverSpanBlocks;

        /// <summary>Iki karsilikli mesnet ve Kilit Tasi (Keystone) ile kapanan kemerin maksimum kol acikligi (blok).</summary>
        public readonly int MaxClosedArchArmBlocks;

        /// <summary>Yanal jeodezik destek maliyeti kappa.</summary>
        public readonly int ArchCostKappa;

        /// <summary>Terzaghi gevseme kubbesi icin Protodyakonov sertlik katsayisi.</summary>
        public readonly float ProtodyakonovHardnessF;

        /// <summary>Yapisal bayraklar (Granular, ArchCapable, SkyAnchor, UnbreakableAnchor).</summary>
        public readonly StructuralMaterialFlags Flags;

        public StructuralMaterialProps(
            float massKg,
            float compressiveCrushLimitKg,
            float shearCrushLimitKg,
            int maxCantileverSpanBlocks,
            int maxClosedArchArmBlocks,
            int archCostKappa,
            float protodyakonovHardnessF,
            StructuralMaterialFlags flags)
        {
            MassKg = massKg;
            CompressiveCrushLimitKg = compressiveCrushLimitKg;
            ShearCrushLimitKg = shearCrushLimitKg;
            MaxCantileverSpanBlocks = maxCantileverSpanBlocks;
            MaxClosedArchArmBlocks = maxClosedArchArmBlocks;
            ArchCostKappa = archCostKappa;
            ProtodyakonovHardnessF = protodyakonovHardnessF;
            Flags = flags;
        }

        public bool IsSkyAnchor => (Flags & StructuralMaterialFlags.IsSkyAnchor) != 0;
        public bool IsUnbreakableAnchor => (Flags & StructuralMaterialFlags.IsUnbreakableAnchor) != 0;
        public bool IsArchCapable => (Flags & StructuralMaterialFlags.IsArchCapable) != 0;
        public bool IsGranular => (Flags & StructuralMaterialFlags.IsGranular) != 0;

        /// <summary>8-Byte L1 LUT kaydina dogrudan erisim saglar.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VoxelMaterialSpec8 GetCompactSpec(byte materialId) => Specs256[materialId];

        /// <summary>Verilen MaterialID icin kanonik yapisal fizik parametrelerini O(1) LUT uzerinden doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructuralMaterialProps GetForMaterial(byte materialId)
        {
            VoxelMaterialSpec8 s = Specs256[materialId];
            if (materialId == MaterialIdCatalog.Air)
            {
                return new StructuralMaterialProps(0f, 0f, 0f, 0, 0, 1, 1f, StructuralMaterialFlags.None);
            }

            float massKg = s.DensityHectoKg * 100f;
            bool infinite = s.MaxColumnBlocks == ushort.MaxValue ||
                            (s.Flags & (StructuralMaterialFlags.IsUnbreakableAnchor | StructuralMaterialFlags.IsSkyAnchor)) != 0;
            float crushKg = infinite ? float.MaxValue : massKg * s.MaxColumnBlocks;
            float shearKg = infinite ? float.MaxValue : crushKg * (s.ShearRatio255 / 255f);
            int maxCantilever = s.MaxCantileverBlocks;
            int maxArchArm = (s.Flags & StructuralMaterialFlags.IsArchCapable) != 0
                ? maxCantilever * 2
                : maxCantilever;

            return new StructuralMaterialProps(
                massKg,
                crushKg,
                shearKg,
                maxCantilever,
                maxArchArm,
                Math.Max(1, (int)s.ArchCostKappa),
                Math.Max(0.1f, s.ProtodyakonovF10 * 0.1f),
                s.Flags);
        }

        /// <summary>
        /// Uc Eksenli Kayac Hapsi (Triaxial Rock Confinement) + Mikro-Voxel Darbogaz Kesiti (A_min / 16):
        /// Yanal olarak &gt;= 4 yuzu dogal kayayla cevrili bloklar sonsuz tasiyici ana kaya kabul edilir.
        /// ShapeID == 63 yontulmus kolonlarda en dar yatay 4x4 kesit (0..16) tasima kapasitesini dogrusal olcekler!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeEffectiveCrushLimit(int confinedNeighborFaces, int microBottleneckCrossSection16 = 16)
        {
            if (CompressiveCrushLimitKg >= float.MaxValue * 0.5f || confinedNeighborFaces >= 4)
            {
                return float.MaxValue;
            }

            float areaScale = Math.Clamp(microBottleneckCrossSection16, 0, 16) / 16.0f;
            return CompressiveCrushLimitKg * areaScale * (1.0f + (4.0f * confinedNeighborFaces) / 6.0f);
        }

        /// <summary>Mikro-voxel kesit daralmasina duyarli efektif yanal kesme (shear) kapasitesi.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeEffectiveShearLimit(int microBottleneckCrossSection16 = 16)
        {
            if (ShearCrushLimitKg >= float.MaxValue * 0.5f)
            {
                return float.MaxValue;
            }

            float areaScale = Math.Clamp(microBottleneckCrossSection16, 0, 16) / 16.0f;
            return ShearCrushLimitKg * areaScale;
        }

        /// <summary>
        /// Terzaghi Gevseme Kubbesi Yuksekligi:
        /// H_arch = min(10, max(ceil(B_excess * 1.25 / f_norm), floor(B_span / (2 * f_protodyakonov)))).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ComputeTerzaghiArchHeight(int unsupportedSpanBlocks)
        {
            if (unsupportedSpanBlocks <= MaxCantileverSpanBlocks || IsUnbreakableAnchor || IsSkyAnchor)
            {
                return 0;
            }

            float denom = 2.0f * (ProtodyakonovHardnessF <= 0.1f ? 0.1f : ProtodyakonovHardnessF);
            int classicArch = (int)(unsupportedSpanBlocks / denom);
            int excessSpan = unsupportedSpanBlocks - MaxCantileverSpanBlocks;
            int bibleDeconfinement = (int)MathF.Ceiling((excessSpan * 1.25f) / MathF.Max(1.0f, ProtodyakonovHardnessF * 0.5f));
            int arch = Math.Max(classicArch, bibleDeconfinement);
            if (arch < 1) arch = 1;
            if (arch > 10) arch = 10;
            return arch;
        }

        private static VoxelMaterialSpec8[] BuildCanonicalLut256()
        {
            var lut = new VoxelMaterialSpec8[256];

            // Varsayilan sert kaya / yapi blogu
            var defaultRock = new VoxelMaterialSpec8(
                densityHectoKg: 24,
                maxColumnBlocks: 1200,
                shearRatio255: 110,
                maxCantileverBlocks: 4,
                archCostKappa: 4,
                protodyakonovF10: 25,
                flags: StructuralMaterialFlags.IsArchCapable);

            for (int i = 1; i < 256; i++)
            {
                lut[i] = defaultRock;
            }

            lut[MaterialIdCatalog.Air] = new VoxelMaterialSpec8(0, 0, 0, 0, 1, 10, StructuralMaterialFlags.None);

            // Kirilmaz Manto / Bedrock & Floatstone Gokyuzu Ankraji
            lut[MaterialIdCatalog.Bedrock] = new VoxelMaterialSpec8(
                100, ushort.MaxValue, 255, 64, 1, 200,
                StructuralMaterialFlags.IsUnbreakableAnchor | StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Floatstone] = new VoxelMaterialSpec8(
                8, ushort.MaxValue, 255, 64, 1, 150,
                StructuralMaterialFlags.IsSkyAnchor | StructuralMaterialFlags.IsArchCapable);

            // Taneli ve Yumusak Zeminler (Bible 2.7.C: Dirt=80, LooseRubble=60, Sand/Gravel=40)
            lut[MaterialIdCatalog.LooseRubble] = new VoxelMaterialSpec8(
                15, 60, 40, 0, 16, 4, StructuralMaterialFlags.IsGranular);
            lut[MaterialIdCatalog.Sand] = new VoxelMaterialSpec8(
                16, 45, 35, 0, 16, 5, StructuralMaterialFlags.IsGranular);
            lut[MaterialIdCatalog.Gravel] = new VoxelMaterialSpec8(
                17, 50, 40, 0, 16, 6, StructuralMaterialFlags.IsGranular);
            lut[MaterialIdCatalog.Dirt] = new VoxelMaterialSpec8(
                14, 80, 64, 2, 8, 10, StructuralMaterialFlags.None);
            lut[MaterialIdCatalog.Grass] = new VoxelMaterialSpec8(
                14, 80, 64, 2, 8, 10, StructuralMaterialFlags.None);
            lut[MaterialIdCatalog.Clay] = new VoxelMaterialSpec8(
                18, 110, 76, 2, 7, 12, StructuralMaterialFlags.None);
            lut[MaterialIdCatalog.PackedIce] = new VoxelMaterialSpec8(
                9, 300, 90, 3, 5, 18, StructuralMaterialFlags.IsArchCapable);

            // Ahsap ve Cati Malzemeleri (Bible 2.7.C: Thatch=120+, Wood=400+)
            // Tum mimari cati/kiris malzemeleri IsArchCapable bayragi ile 45° egimli cati (Ramp45) capraz baglantisini destekler.
            lut[MaterialIdCatalog.ThatchTile] = new VoxelMaterialSpec8(
                4, 180, 140, 5, 3, 14, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.OakWood] = new VoxelMaterialSpec8(
                7, 450, 155, 5, 3, 16, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.WoodPlank] = new VoxelMaterialSpec8(
                7, 450, 165, 5, 3, 18, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Ironwood] = new VoxelMaterialSpec8(
                10, 850, 165, 6, 3, 25, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.SpiritWood] = new VoxelMaterialSpec8(
                9, 950, 170, 6, 3, 28, StructuralMaterialFlags.IsArchCapable);

            // Dogal Kaya ve Kagir Yapi Malzemeleri (Bible 2.7.C: Stone=1200, StoneBrick=2500)
            lut[MaterialIdCatalog.Cobblestone] = new VoxelMaterialSpec8(
                22, 900, 105, 3, 5, 20, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Stone] = new VoxelMaterialSpec8(
                25, 1200, 120, 4, 4, 30, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Basalt] = new VoxelMaterialSpec8(
                28, 1500, 128, 4, 4, 34, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Stalagnate] = new VoxelMaterialSpec8(
                27, 2200, 140, 5, 3, 38, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Marble] = new VoxelMaterialSpec8(
                27, 2400, 145, 5, 3, 36, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.StoneBrick] = new VoxelMaterialSpec8(
                24, 2500, 155, 5, 3, 40, StructuralMaterialFlags.IsArchCapable);

            // 9-Ekol Islenmis Mimari Malzemeler (126..152 — Karar A3 %100 CrystalImmune, 1..4 katli 4..8m aciklikli binalar & catilar)
            lut[MaterialIdCatalog.CutAshlarStone] = new VoxelMaterialSpec8(
                25, 3200, 165, 6, 3, 45, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.DressedMarbleBlock] = new VoxelMaterialSpec8(
                27, 3600, 170, 6, 2, 48, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.FiredClayBrick] = new VoxelMaterialSpec8(
                19, 2200, 155, 5, 3, 36, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.RoofClayTile] = new VoxelMaterialSpec8(
                18, 1400, 150, 5, 3, 28, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.AdobeBrickBlock] = new VoxelMaterialSpec8(
                16, 650, 130, 4, 4, 20, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.WattleDaubPanelBlock] = new VoxelMaterialSpec8(
                11, 380, 130, 4, 4, 16, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.LimeMortarStoneWall] = new VoxelMaterialSpec8(
                24, 2800, 160, 5, 3, 42, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.ChinkedLogBlock] = new VoxelMaterialSpec8(
                7, 520, 165, 5, 3, 20, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.IronTieBeamBlock] = new VoxelMaterialSpec8(
                78, 11000, 215, 10, 2, 82, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.LeadedGlassPane] = new VoxelMaterialSpec8(
                12, 500, 135, 4, 4, 18, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.TimberShingleRoof] = new VoxelMaterialSpec8(
                6, 360, 150, 5, 3, 18, StructuralMaterialFlags.IsArchCapable);

            lut[MaterialIdCatalog.EnglishBondBrickMasonry] = new VoxelMaterialSpec8(
                19, 2600, 160, 5, 3, 38, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.CobMudBrick] = new VoxelMaterialSpec8(
                16, 480, 125, 4, 4, 18, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.RammedEarthCompacted] = new VoxelMaterialSpec8(
                20, 620, 130, 4, 4, 20, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.DriedRammedEarth] = new VoxelMaterialSpec8(
                19, 950, 140, 5, 3, 26, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.PlywoodOsbPanel] = new VoxelMaterialSpec8(
                6, 420, 160, 5, 3, 18, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.TreatedTimberBeam] = new VoxelMaterialSpec8(
                7, 750, 180, 6, 3, 24, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.GroovedLogTimber] = new VoxelMaterialSpec8(
                7, 560, 165, 5, 3, 20, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.LimeWashPlaster] = new VoxelMaterialSpec8(
                17, 720, 135, 4, 4, 22, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.SteelRebarMesh] = new VoxelMaterialSpec8(
                78, 10500, 210, 8, 2, 78, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.WetPortlandConcrete] = new VoxelMaterialSpec8(
                24, 320, 95, 3, 5, 16, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.CuredReinforcedConcrete] = new VoxelMaterialSpec8(
                24, 5200, 195, 8, 2, 62, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.StructuralSteelHBeam] = new VoxelMaterialSpec8(
                78, 14000, 225, 12, 2, 92, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.CorrugatedSteelDeck] = new VoxelMaterialSpec8(
                45, 7500, 205, 8, 2, 72, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.StrawChinkingBundle] = new VoxelMaterialSpec8(
                3, 140, 115, 4, 4, 12, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.CrushedLimeAggregate] = new VoxelMaterialSpec8(
                18, 800, 110, 3, 5, 22, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.FormworkTimberBoard] = new VoxelMaterialSpec8(
                6, 360, 150, 4, 4, 16, StructuralMaterialFlags.IsArchCapable);

            // Ic Mekan Esya ve Ocak Bloklari (166..168): Ahsap dosemeleri gereksiz agirlikla ezmemesi icin gercekci yogunluk
            lut[MaterialIdCatalog.CampfireBlock] = new VoxelMaterialSpec8(
                8, 300, 120, 4, 4, 16, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.PrimitiveStoneKilnBlock] = new VoxelMaterialSpec8(
                18, 900, 140, 4, 4, 24, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.BedBlock] = new VoxelMaterialSpec8(
                5, 300, 140, 4, 4, 15, StructuralMaterialFlags.IsArchCapable);

            // Metaller, Takviyeli Celik ve Obsidyen/Titanyum (Bible 2.7.C: ReinforcedSteel=12000, Titanium/Obsidian=25000)
            lut[MaterialIdCatalog.IronGrating] = new VoxelMaterialSpec8(
                32, 4500, 180, 7, 2, 60, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.Steel] = new VoxelMaterialSpec8(
                78, 12000, 210, 10, 2, 80, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.ReinforcedSteelBlock] = new VoxelMaterialSpec8(
                78, 12000, 220, 10, 2, 85, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.DamascusSteel] = new VoxelMaterialSpec8(
                79, 16000, 225, 11, 2, 95, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.NaturalObsidian] = new VoxelMaterialSpec8(
                28, 25000, 190, 6, 3, 55, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.ObsidianBrick] = new VoxelMaterialSpec8(
                28, 25000, 205, 7, 2, 65, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.TitaniumOre] = new VoxelMaterialSpec8(
                45, 25000, 230, 12, 2, 110, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.MithrilOre] = new VoxelMaterialSpec8(
                42, 32000, 240, 14, 1, 130, StructuralMaterialFlags.IsArchCapable);
            lut[MaterialIdCatalog.StarCore] = new VoxelMaterialSpec8(
                60, 50000, 250, 16, 1, 160, StructuralMaterialFlags.IsArchCapable);

            // Kadim Zindan Kutsal Yapilari (>= 240): Sonsuz Mesnet
            for (int i = MaterialIdCatalog.DungeonSanctumImmuneStart; i < 256; i++)
            {
                lut[i] = new VoxelMaterialSpec8(
                    50, ushort.MaxValue, 255, 64, 1, 200,
                    StructuralMaterialFlags.IsUnbreakableAnchor | StructuralMaterialFlags.IsArchCapable);
            }

            return lut;
        }
    }
}
