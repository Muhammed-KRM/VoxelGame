#nullable enable
using System.Runtime.InteropServices;

namespace Stratocraft.Inventory.Data
{
    /// <summary>
    /// Tum dogal ve zanaat esyalarinin 3B fiziksel arketip sinifi.
    /// <c>Stratocraft.Inventory</c> katmaninda tanimlanir ki hem Dunya, hem El, hem de Canta ayni kaynagi kullansin.
    /// </summary>
    public enum PhysicalItemArchetype : byte
    {
        ArchitecturalBlock = 0,
        NaturalStick = 1,
        SplitFirewood = 2,
        LeafPile = 3,
        PlantFiber = 4,
        RoughStone = 5,
        FlintStone = 6,
        QuartzCrystalCluster = 7,
        OreBearingRock = 8,
        DirtClod = 9,
        SandMound = 10,
        ClayLump = 11,
        KnappedAxeHead = 12,
        KnappedPickaxeHead = 13,
        KnappedKnifeBlade = 14,
        KnappedHammerHead = 15,
        RawMeatCut = 16,
        CookedMeatCut = 17,
        BurntCharredMeat = 18,
        WildBerryCluster = 19,
        RawHideRoll = 20,
        CharcoalChunk = 21,
        RoastedCrackedOre = 22,
        CrushedOreGravel = 23,
        ClayTuyerePipe = 24,
        ClayCruciblePot = 25,
        LeatherBellows = 26,
        IronBloomSponge = 27,
        WroughtIronBar = 28,
        SlagGlassShard = 29,
        WoodenWheel = 30,
        MetalIngotBar = 31,
        WildBushFoliage = 32,
        WildBushBareRoots = 33,
        ToolPrimitiveAxe = 34,
        ToolPrimitivePickaxe = 35,
        ToolPrimitiveKnife = 36,
        ToolPrimitiveHammer = 37,
        None = 255
    }

    /// <summary>
    /// Her fiziksel esya orneginin deterministik 3B sekil DNA'si (Tam 16 Byte unmanaged).
    /// Yerde == Elde == Cantada == Dunyaya Kondugunda birebir ayni 3B modeli uretmeyi garanti eder.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PhysicalItemShapeDna
    {
        /// <summary>Rastgele dogal sekil varyasyon tohumu (0 ise varsayilan tohum kullanilir).</summary>
        public uint ShapeSeed;

        /// <summary>Tas yontma (8x8 = 64 hucre) bit maskesi. Flags bit-0 aciksa ozel kirik sekli gosterir.</summary>
        public ulong KnapMask64;

        /// <summary>35..100 arasi iscilik kalitesi yuzdesi (0 ise 100 kabul edilir).</summary>
        public byte QualityPct;

        /// <summary>StratocraftUberShader sicaklik parlama kanali (0 = soguk, 255 = ~1300 C akkor).</summary>
        public byte ThermalTempByte;

        /// <summary>Pisirme, kavurma veya kuruma evresi (0..255).</summary>
        public byte StageByte;

        /// <summary>Bit 0: HasCustomKnapMask, Bit 1: IsBrokenStick, Bit 2: HasBerries, Bit 3: IsUnfiredClay.</summary>
        public byte Flags;

        public const byte FlagCustomKnapMask = 1 << 0;
        public const byte FlagBrokenStick = 1 << 1;
        public const byte FlagHasBerries = 1 << 2;
        public const byte FlagUnfiredClay = 1 << 3;

        public PhysicalItemShapeDna(uint shapeSeed, ulong knapMask64, float quality01, byte thermalTempByte, uint flags)
        {
            ShapeSeed = shapeSeed == 0u ? 0x9E3779B9u : shapeSeed;
            KnapMask64 = knapMask64;
            QualityPct = (byte)System.Math.Clamp((int)System.MathF.Round(quality01 * 100f), 1, 100);
            ThermalTempByte = thermalTempByte;
            StageByte = 0;
            Flags = (byte)(flags & 0xFFu);
        }

        public uint EffectiveSeed => ShapeSeed != 0u ? ShapeSeed : 0x9E3779B9u;

        public byte EffectiveQualityPct => QualityPct == 0 ? (byte)100 : QualityPct;

        public float EffectiveQuality01 => EffectiveQualityPct * 0.01f;

        public bool HasCustomKnapMask => (Flags & FlagCustomKnapMask) != 0 && KnapMask64 != 0UL;

        public static PhysicalItemShapeDna CreateNatural(uint seed, byte qualityPct = 100)
        {
            return new PhysicalItemShapeDna
            {
                ShapeSeed = MixHash(seed == 0u ? 0x6C8E9CF5u : seed),
                KnapMask64 = 0UL,
                QualityPct = qualityPct == 0 ? (byte)100 : qualityPct,
                ThermalTempByte = 0,
                StageByte = 0,
                Flags = 0
            };
        }

        public static PhysicalItemShapeDna CreateFromWorldCoord(int wx, int wy, int wz, int salt = 0)
        {
            uint h = (uint)(wx * 73856093) ^ (uint)(wy * 19349663) ^ (uint)(wz * 83492791) ^ (uint)(salt * 265443576);
            return CreateNatural(h == 0u ? 0xA5A5F00Du : h);
        }

        public static PhysicalItemShapeDna CreateFromWorldCoord(int wx, int wy, int wz, uint salt)
        {
            uint h = (uint)(wx * 73856093) ^ (uint)(wy * 19349663) ^ (uint)(wz * 83492791) ^ (salt * 265443576u);
            return CreateNatural(h == 0u ? 0xA5A5F00Du : h);
        }

        public PhysicalItemShapeDna DeriveStackInstance(int stackPieceIndex)
        {
            if (stackPieceIndex <= 0) return this;
            PhysicalItemShapeDna copy = this;
            copy.ShapeSeed = MixHash(EffectiveSeed + (uint)stackPieceIndex * 0x85EBCA6Bu);
            return copy;
        }

        public readonly PhysicalItemShapeDna WithTemperature(byte thermalTempByte)
        {
            PhysicalItemShapeDna copy = this;
            copy.ThermalTempByte = thermalTempByte;
            return copy;
        }

        public readonly PhysicalItemShapeDna WithStage(byte stageByte)
        {
            PhysicalItemShapeDna copy = this;
            copy.StageByte = stageByte;
            return copy;
        }

        public readonly PhysicalItemShapeDna WithFlags(byte flags)
        {
            PhysicalItemShapeDna copy = this;
            copy.Flags = flags;
            return copy;
        }

        public static uint MixHash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x == 0u ? 0x9E3779B9u : x;
        }
    }
}
