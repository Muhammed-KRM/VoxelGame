using System;
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
        SupportPillarTarget = 1 << 4
    }

    /// <summary>
    /// 4-Byte (32-Bit) Sıkıştırılmış Dikey Gezinme Özeti (NavSpanColumnSummary).
    /// Kamera dışı (Off-Screen / LoD Kademe 1: 128m - 512m) canlıların ve gece dalgası
    /// sürülerinin tam 3B voksel çarpışması yerine 16 KB'lık önbellek dostu yüzey özetinde
    /// 10 Hz frekansla yürümesini sağlar.
    /// Bellek düzeni: [ushort SurfaceY (2B)] + [byte HeadroomBlocks (1B)] + [byte HazardFlags (1B)] = 4 Byte.
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
                   (HazardFlags & NavSpanHazardFlags.LavaLethal) == 0;
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
}
