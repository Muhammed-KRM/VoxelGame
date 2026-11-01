#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Stratocraft.Voxel.Data
{
    [Flags]
    public enum NavSpanHazardFlags : byte
    {
        None = 0,
        SubmergedWater = 1 << 0,
        LavaLethal = 1 << 1,
        SteepCliffDrop = 1 << 2,
        ClanFenceBoundary = 1 << 3,
        SupportPillarTarget = 1 << 4,
        AcidCorrosive = 1 << 5,
        WallClimbable = 1 << 6,
        StaticTrapMine = 1 << 7
    }

    public enum NavSpanTerrainCode : byte
    {
        DryGround = 0,
        ShallowWater = 1,
        DeepWater = 2,
        LavaLethal = 3,
        PlayerWallOrGate = 4,
        WoodScaffold = 5,
        SteepCliffEdge = 6,
        AcidCorrosive = 7
    }

    /// <summary>
    /// 4-Byte (32-Bit) Ust-Yuzey Dikey Gezinme Ozeti (Geriye donuk uyumlu hizli yuzey sorgusu).
    /// Bellek duzeni: [ushort SurfaceY (2B)] + [byte HeadroomBlocks (1B)] + [byte HazardFlags (1B)] = 4 Byte.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct NavSpanColumnSummary : IEquatable<NavSpanColumnSummary>
    {
        [FieldOffset(0)]
        public ushort SurfaceY;

        [FieldOffset(2)]
        public byte HeadroomBlocks;

        [FieldOffset(3)]
        public NavSpanHazardFlags HazardFlags;

        public NavSpanColumnSummary(ushort surfaceY, byte headroomBlocks, NavSpanHazardFlags hazardFlags = NavSpanHazardFlags.None)
        {
            SurfaceY = surfaceY;
            HeadroomBlocks = headroomBlocks;
            HazardFlags = hazardFlags;
        }

        public readonly bool IsWalkableForHeight(int requiredHeightBlocks)
        {
            return HeadroomBlocks >= requiredHeightBlocks &&
                   (HazardFlags & (NavSpanHazardFlags.LavaLethal | NavSpanHazardFlags.AcidCorrosive)) == 0;
        }

        public readonly bool Equals(NavSpanColumnSummary other)
        {
            return SurfaceY == other.SurfaceY &&
                   HeadroomBlocks == other.HeadroomBlocks &&
                   HazardFlags == other.HazardFlags;
        }

        public override readonly bool Equals(object? obj) => obj is NavSpanColumnSummary other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(SurfaceY, HeadroomBlocks, (byte)HazardFlags);
    }

    /// <summary>
    /// MATEMATIKSEL RAPOR Bolum 1.2.A — 32-Bit (4-Byte) Kanonik Multi-Span Kaydi:
    /// Bits [0..9]   (10-bit): Y_floor (0.5m kuantizasyonlu yurunebilir katman tabani: 0..511.5m lokal Y)
    /// Bits [10..13] (4-bit) : dY_max (4 komsuya gore tirmanilabilir basamak farki, 0..15)
    /// Bits [14..17] (4-bit) : NavSpanTerrainCode (Kuru, Su, Lav, Oyuncu Suru, Iskele, Ucurum, Asit)
    /// Bits [18..20] (3-bit) : Clearance (Katman ustu tavan acikligi: 0..7+ blok)
    /// Bit  [21]     (1-bit) : HAS_STATIC_HAZARD (Mayin / Tuzak / Destek Sutunu)
    /// Bits [22..31] (10-bit): ClanID (0 = Tarafsiz, 1..1023 = Klan Mulkiyeti)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public readonly struct NavSpanRecord32 : IEquatable<NavSpanRecord32>
    {
        [FieldOffset(0)]
        public readonly uint RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NavSpanRecord32(uint rawValue)
        {
            RawValue = rawValue;
        }

        public bool IsValid => RawValue != 0u;

        public float FloorY => (RawValue & 0x3FFu) * 0.5f;

        public int FloorBlockY => (int)((RawValue & 0x3FFu) >> 1);

        public byte MaxStepBlocks => (byte)((RawValue >> 10) & 0x0Fu);

        public NavSpanTerrainCode TerrainCode => (NavSpanTerrainCode)((RawValue >> 14) & 0x0Fu);

        public byte ClearanceBlocks => (byte)((RawValue >> 18) & 0x07u);

        public bool HasStaticHazardOrPillar => ((RawValue >> 21) & 0x01u) != 0u;

        public ushort ClanId => (ushort)((RawValue >> 22) & 0x3FFu);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NavSpanRecord32 Pack(
            float localFloorY,
            byte maxStepBlocks,
            NavSpanTerrainCode terrainCode,
            byte clearanceBlocks,
            bool hasStaticHazardOrPillar,
            ushort clanId = 0)
        {
            uint yQuant = (uint)Math.Clamp((int)MathF.Round(localFloorY * 2.0f), 1, 1023);
            uint step4 = (uint)(maxStepBlocks & 0x0F);
            uint terr4 = (uint)((byte)terrainCode & 0x0F);
            uint clear3 = (uint)Math.Min((int)clearanceBlocks, 7);
            uint haz1 = hasStaticHazardOrPillar ? 1u : 0u;
            uint clan10 = (uint)(clanId & 0x3FF);

            uint raw = yQuant
                     | (step4 << 10)
                     | (terr4 << 14)
                     | (clear3 << 18)
                     | (haz1 << 21)
                     | (clan10 << 22);
            return new NavSpanRecord32(raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWalkableFor(int requiredHeightBlocks, bool lavaImmune = false)
        {
            if (!IsValid) return false;
            int effectiveClearance = ClearanceBlocks == 7 ? 16 : ClearanceBlocks;
            if (effectiveClearance < requiredHeightBlocks) return false;
            NavSpanTerrainCode tc = TerrainCode;
            if (tc == NavSpanTerrainCode.AcidCorrosive) return false;
            if (tc == NavSpanTerrainCode.LavaLethal && !lavaImmune) return false;
            return true;
        }

        public bool Equals(NavSpanRecord32 other) => RawValue == other.RawValue;
        public override bool Equals(object? obj) => obj is NavSpanRecord32 other && Equals(other);
        public override int GetHashCode() => (int)RawValue;
    }

    /// <summary>
    /// Cok Katmanli (4-Layer Multi-Span) 16-Byte Sutun Ozeti:
    /// Ayni (X, Z) koordinatinda ayni anda:
    /// - Span0: En ust yuzey (cati / ucan ada / acik arazi)
    /// - Span1: Kopru alti veya 2. bina kati
    /// - Span2: Zemin tuneli veya sig magara galerisi
    /// - Span3: Derin yeralti maden galerisi
    /// bilgisini sifir heap tahsisiyle tutar! Magaralar ve kopru altlari AI icin %100 gorunurdur.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct NavSpanColumn4Layer
    {
        [FieldOffset(0)]  public NavSpanRecord32 Span0; // En ust yurunebilir katman
        [FieldOffset(4)]  public NavSpanRecord32 Span1; // 2. katman (yukaridan asagiya)
        [FieldOffset(8)]  public NavSpanRecord32 Span2; // 3. katman
        [FieldOffset(12)] public NavSpanRecord32 Span3; // 4. katman (derin magara)

        public readonly int ActiveLayerCount =>
            (Span0.IsValid ? 1 : 0) +
            (Span1.IsValid ? 1 : 0) +
            (Span2.IsValid ? 1 : 0) +
            (Span3.IsValid ? 1 : 0);

        public readonly ushort SurfaceY => Span0.IsValid ? (ushort)Span0.FloorBlockY : (ushort)0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly NavSpanRecord32 GetLayer(int index)
        {
            return index switch
            {
                0 => Span0,
                1 => Span1,
                2 => Span2,
                3 => Span3,
                _ => default
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetLayer(int index, NavSpanRecord32 record)
        {
            switch (index)
            {
                case 0: Span0 = record; break;
                case 1: Span1 = record; break;
                case 2: Span2 = record; break;
                case 3: Span3 = record; break;
            }
        }

        /// <summary>
        /// Katman Surekliligi (Span Continuity) Kurali:
        /// Verilen mevcut yukseklikten (currentY) maxStepDiff dahilinde ve yeterli tavan acikligina sahip
        /// en uygun komsu katmani O(1) (4 kayit) icinde bulur!
        /// </summary>
        public readonly bool TryFindContinuousLayer(
            float currentY,
            float maxStepDiffMeters,
            int requiredHeightBlocks,
            bool lavaImmune,
            out NavSpanRecord32 matchedSpan,
            out int matchedLayerIndex)
        {
            float bestDiff = float.MaxValue;
            matchedSpan = default;
            matchedLayerIndex = -1;

            for (int i = 0; i < 4; i++)
            {
                NavSpanRecord32 candidate = GetLayer(i);
                if (!candidate.IsWalkableFor(requiredHeightBlocks, lavaImmune))
                {
                    continue;
                }

                float diff = MathF.Abs(candidate.FloorY - currentY);
                if (diff <= maxStepDiffMeters && diff < bestDiff)
                {
                    bestDiff = diff;
                    matchedSpan = candidate;
                    matchedLayerIndex = i;
                }
            }

            return matchedLayerIndex >= 0;
        }
    }
}
