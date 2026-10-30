#nullable enable
using System;

namespace Stratocraft.Core.Exceptions
{
    /// <summary>
    /// Stratocraft yonetilen (Managed) katman temel istisna sinifi.
    /// Her istisna yapilandirilmis bir ErrorCode tasir (Bolum 2.5).
    /// </summary>
    public abstract class StratocraftException : Exception
    {
        /// <summary>Standart hata kodu (orn. ERR_ORE_RESONANCE_IMMUNE).</summary>
        public string ErrorCode { get; }

        /// <summary>Temel Stratocraft istisnasi olusturur.</summary>
        protected StratocraftException(string errorCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode ?? "STRATOCRAFT_UNKNOWN_ERR";
        }
    }

    /// <summary>
    /// Oyun kurali ihlali (orn. yetersiz KGP, dogal kutle ile batarya kurma, cevher vakumlama denemesi).
    /// Oyunu cokertmez; istemciye Turkce kural geri bildirimi dondurur.
    /// </summary>
    public sealed class GameplayRuleException : StratocraftException
    {
        /// <summary>Yeni bir oyun kurali istisnasi olusturur.</summary>
        public GameplayRuleException(string errorCode, string message)
            : base(errorCode, message)
        {
        }
    }

    /// <summary>
    /// Anti-cheat, hiz/mesafe ihlali, gecersiz CAS versiyonu veya sahte Ed25519 kontrat imzasi istisnasi.
    /// Islem reddedilir ve function_error_logs tablosuna kaydedilir.
    /// </summary>
    public sealed class SecurityViolationException : StratocraftException
    {
        /// <summary>Ihlale neden olan oyuncu kimligi (varsa).</summary>
        public ulong OffendingPlayerId { get; }

        /// <summary>Yeni bir guvenlik ihlali istisnasi olusturur.</summary>
        public SecurityViolationException(string errorCode, string message, ulong offendingPlayerId = 0)
            : base(errorCode, message)
        {
            OffendingPlayerId = offendingPlayerId;
        }
    }

    /// <summary>
    /// 16x16x16 Sub-Chunk 4.096 mikro-voxel havuzunda gecersiz indeks veya bellek bozulmasi guvenlik istisnasi.
    /// </summary>
    public sealed class VoxelPoolOverflowException : StratocraftException
    {
        /// <summary>Yeni bir mikro-havuz sinir ihlali istisnasi olusturur.</summary>
        public VoxelPoolOverflowException(string errorCode, string message)
            : base(errorCode, message)
        {
        }
    }

    /// <summary>
    /// Otonom 3B varlik veya mod paketi dogrulama hatasi istisnasi.
    /// </summary>
    public sealed class AssetValidationException : StratocraftException
    {
        /// <summary>Yeni bir varlik dogrulama istisnasi olusturur.</summary>
        public AssetValidationException(string errorCode, string message)
            : base(errorCode, message)
        {
        }
    }
}
