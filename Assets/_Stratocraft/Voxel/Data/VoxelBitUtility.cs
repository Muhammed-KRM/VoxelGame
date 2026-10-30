using System.Runtime.CompilerServices;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// 32-Bit VoxelCell Context-Sensitive Union bit okuma ve yazma yardimcisi (DRY tek dogruluk kaynagi).
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 1.1 ve GELISTIRICI DOKUMANI Bolum 4.13.5.
    /// </summary>
    public static class VoxelBitUtility
    {
        /// <summary>4x4x4 Mikro-Voxel yontulmus bloklari temsil eden ozel ShapeID degeri (63).</summary>
        public const byte ChiseledMicroVoxelShapeId = 63;

        /// <summary>Varsayilan tam dolu SDF yogunlugu (127).</summary>
        public const byte FullDensity = 127;

        /// <summary>Bit [0..7] (8-bit): MaterialID doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetMaterialId(uint raw) => (byte)(raw & 0xFFu);

        /// <summary>Bit [8..14] (7-bit): Density veya sivi seviyesini doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetDensityOrLiquid(uint raw) => (byte)((raw >> 8) & 0x7Fu);

        /// <summary>Bit [15..20] (6-bit): ShapeID (0..63) doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetShapeId(uint raw) => (byte)((raw >> 15) & 0x3Fu);

        /// <summary>Bit [21..25] (5-bit): Rotation (0..23) doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetRotationState(uint raw) => (byte)((raw >> 21) & 0x1Fu);

        /// <summary>Bit [26..27] (2-bit): BlokKokenDurumu doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BlokKokenDurumu GetOriginState(uint raw) => (BlokKokenDurumu)((raw >> 26) & 0x3u);

        /// <summary>Bit [28..31] (4-bit): DamageState (0..15) doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetDamageState(uint raw) => (byte)((raw >> 28) & 0xFu);

        /// <summary>
        /// Context-Sensitive Bit Union: ShapeID == 63 oldugunda Bit [8..14] (alt 7 bit) ve
        /// Bit [21..25] (ust 5 bit) birleserek 12-bitlik LocalMicroPoolIndex (0..4095) adresini olusturur.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetMicroPoolIndex(uint raw)
        {
            return (ushort)(((raw >> 8) & 0x7Fu) | (((raw >> 21) & 0x1Fu) << 7));
        }

        /// <summary>Standart (yontulmamis, ShapeID != 63) bir 32-bit VoxelCell degeri paketler.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackStandard(
            byte matId,
            byte density,
            byte shapeId,
            byte rot24,
            BlokKokenDurumu origin,
            byte dmg4)
        {
            return (uint)matId
                | (((uint)density & 0x7Fu) << 8)
                | (((uint)shapeId & 0x3Fu) << 15)
                | (((uint)rot24 & 0x1Fu) << 21)
                | (((uint)origin & 0x3u) << 26)
                | (((uint)dmg4 & 0xFu) << 28);
        }

        /// <summary>
        /// ShapeID == 63 olan yontulmus (4x4x4 mikro-voxel) bir 32-bit VoxelCell degeri paketler.
        /// 12-bitlik localMicroPoolIdx12 (0..4095) degerini 7 alt bit + 5 ust bit olarak boler.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint PackChiseled(
            byte matId,
            ushort localMicroPoolIdx12,
            BlokKokenDurumu origin,
            byte dmg4)
        {
            uint low7 = (uint)localMicroPoolIdx12 & 0x7Fu;
            uint high5 = ((uint)localMicroPoolIdx12 >> 7) & 0x1Fu;

            return (uint)matId
                | (low7 << 8)
                | ((uint)ChiseledMicroVoxelShapeId << 15)
                | (high5 << 21)
                | (((uint)origin & 0x3u) << 26)
                | (((uint)dmg4 & 0xFu) << 28);
        }

        /// <summary>Mevcut bir VoxelCell'in yalnizca BlokKokenDurumu (Bits 26..27) alanini gunceller.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint WithOriginState(uint raw, BlokKokenDurumu newOrigin)
        {
            return (raw & ~(0x3u << 26)) | (((uint)newOrigin & 0x3u) << 26);
        }

        /// <summary>Mevcut bir VoxelCell'in yalnizca DamageState (Bits 28..31) alanini gunceller.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint WithDamageState(uint raw, byte newDamage4)
        {
            return (raw & ~(0xFu << 28)) | (((uint)newDamage4 & 0xFu) << 28);
        }
    }
}
