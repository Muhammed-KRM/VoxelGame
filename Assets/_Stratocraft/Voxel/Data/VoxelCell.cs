using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// 2-Bit Blok Koken Durumu (Bits 26..27) — Ekonomi, Madde Kristali Vakumu ve Batarya Sömürü Koruması.
    /// Bkz. STRATOCRAFT_GELISTIRICI_DOKUMANI Bolum 4.5.1 ve 4.6.1.
    /// </summary>
    public enum BlokKokenDurumu : byte
    {
        /// <summary>
        /// 0 (00b): Dogal dunya blogu (IS_ITEM_BLOCK = 0).
        /// Madde Kristali ile vakumlanabilir (14 cevher damari haric); batarya/mayin kurulamaz; pistonla kirilirsa 0 drop.
        /// </summary>
        DogalKutle = 0,

        /// <summary>
        /// 1 (01b): Oyuncu aletiyle kirilmis veya atolyede islenmis gercek item blogu (IS_ITEM_BLOCK = 1).
        /// Batarya, mayin, klan yapisi ve otomasyon kurulumunda zorunludur; Madde Kristali ile vakumlanamaz.
        /// </summary>
        OyuncuItemBlogu = 1,

        /// <summary>
        /// 2 (10b): Olusturma Bataryasi (#26..#50) tarafindan savasta uretilen gecici/taktiksel blok.
        /// Kirildiginda %0 esya dusurur (Zero-Drop Exploit Prevention).
        /// </summary>
        BataryaUretimi = 2,

        /// <summary>
        /// 3 (11b): Aktif bir batarya, mekanizma veya kusatma silahi govdesine kilitlenmis atomik blok.
        /// </summary>
        VoxelLockedAtomic = 3
    }

    /// <summary>
    /// Tek bir 1m^3 dunya blogunun 32-bit (4 Byte) baglama duyarli (Context-Sensitive Union) bellek temsili.
    /// Her 16x16x16 (4.096 Voxel = 16 KB) Sub-Chunk icin 12-Bit LocalMicroPoolIndex (0..4095) %100 kayipsizdir.
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 1.1 ve GELISTIRICI DOKUMANI Bolum 4.6.1.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct VoxelCell : IEquatable<VoxelCell>
    {
        /// <summary>Ham 32-bit isaretsiz tamsayi temsili.</summary>
        [FieldOffset(0)]
        public uint RawValue;

        /// <summary>Ham 32-bit degerden VoxelCell olusturur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VoxelCell(uint rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>Bos (Hava) voxel sabiti.</summary>
        public static VoxelCell Air => new VoxelCell(0u);

        /// <summary>Bit [0..7] (8-bit): MaterialID (0 = Hava, 1..239 = Blok/Cevher/Yapi, 240..255 = DUNGEON_SANCTUM_IMMUNE).</summary>
        public byte MaterialId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetMaterialId(RawValue);
        }

        /// <summary>Bit [8..14] (7-bit): Density / SDF (0..127) (ShapeID != 63 iken).</summary>
        public byte DensityOrLiquid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetDensityOrLiquid(RawValue);
        }

        /// <summary>Bit [15..20] (6-bit): ShapeID (0 = Smooth SDF, 1 = Tam Kup, 2..62 = Mimari Sekil, 63 = 4x4x4 Mikro-Voxel).</summary>
        public byte ShapeId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetShapeId(RawValue);
        }

        /// <summary>Bit [21..25] (5-bit): Rotation (0..23 Kubik Grup SO(3)) (ShapeID != 63 iken).</summary>
        public byte RotationState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetRotationState(RawValue);
        }

        /// <summary>Bit [26..27] (2-bit): Blok Koken Durumu (DogalKutle, OyuncuItemBlogu, BataryaUretimi, VoxelLockedAtomic).</summary>
        public BlokKokenDurumu OriginState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetOriginState(RawValue);
        }

        /// <summary>Bit [28..31] (4-bit): DamageState (0..15).</summary>
        public byte DamageState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetDamageState(RawValue);
        }

        /// <summary>ShapeID == 63 oldugunda 12-bitlik LocalMicroPoolIndex (0..4095) adresini doner.</summary>
        public ushort MicroPoolIndex
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VoxelBitUtility.GetMicroPoolIndex(RawValue);
        }

        /// <summary>Blok yontulmus (ShapeID == 63) bir mikro-voxel hucresi mi?</summary>
        public bool IsChiseledMicroVoxel
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ShapeId == VoxelBitUtility.ChiseledMicroVoxelShapeId;
        }

        /// <summary>Blok kati (Hava olmayan) bir madde mi?</summary>
        public bool IsSolid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MaterialId != 0;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(VoxelCell other) => RawValue == other.RawValue;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is VoxelCell other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (int)RawValue;

        /// <summary>Esitlik operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(VoxelCell left, VoxelCell right) => left.RawValue == right.RawValue;

        /// <summary>Esitsizlik operatoru.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(VoxelCell left, VoxelCell right) => left.RawValue != right.RawValue;
    }
}
