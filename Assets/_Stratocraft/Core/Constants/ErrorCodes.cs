namespace Stratocraft.Core.Constants
{
    /// <summary>
    /// 3 Katmanli telemetri loglarinda ve ozel istisnalarda kullanilan standart hata kodlari (MODUL_ALTMODUL_ERR).
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>Sub-Chunk 4.096 mikro-havuz sinir ihlali veya gecersiz indeks.</summary>
        public const string VoxelMicroPoolOverflow = "VOXEL_MICROPOOL_OVERFLOW_ERR";

        /// <summary>.sregion veya LZ4 chunk paketinde XXH64 saglama toplami uyusmazligi.</summary>
        public const string VoxelCorruptChecksum = "VOXEL_CORRUPT_CHECKSUM_ERR";

        /// <summary>Madde Kristali ile cevher veya mucevher damari vakumlama denemesi.</summary>
        public const string CrystalOreResonanceImmune = "ERR_ORE_RESONANCE_IMMUNE";

        /// <summary>Madde Kristali ile IS_ITEM_BLOCK == 1 oyuncu yapimi blogu vakumlama denemesi.</summary>
        public const string CrystalCraftedBlockForbidden = "ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN";

        /// <summary>Dogal kutle (BlockOriginState == 0) ile batarya veya mayin kurma denemesi.</summary>
        public const string BatteryNaturalBlockForbidden = "BATTERY_NATURAL_BLOCK_FORBIDDEN_ERR";

        /// <summary>Gorsel sandik veya kontrat kasasinda 64-bit CAS versiyon cakismasi.</summary>
        public const string EscrowCasConflict = "ESCROW_CAS_CONFLICT_ERR";

        /// <summary>Kan Imzali Kontratta gecersiz Ed25519 imzasi.</summary>
        public const string ContractInvalidSignature = "ESCROW_INVALID_ED25519_SIGNATURE_ERR";

        /// <summary>Sunucu otoriter hiz veya erisim mesafesi anti-cheat ihlali.</summary>
        public const string AntiCheatBoundsViolation = "SECURITY_ANTICHEAT_BOUNDS_ERR";

        /// <summary>Roslyn AST mod dogrulayicisinda yasakli ad alani veya unsafe kod tespiti.</summary>
        public const string ModForbiddenNamespaceOrUnsafe = "ERR_MOD_FORBIDDEN_NAMESPACE_OR_UNSAFE";

        /// <summary>Otonom 3B varlik dogrulamasinda AABB/Soket/Color32 ihlali.</summary>
        public const string AssetValidationFailed = "ASSET_VALIDATION_FAILED_ERR";
    }
}
