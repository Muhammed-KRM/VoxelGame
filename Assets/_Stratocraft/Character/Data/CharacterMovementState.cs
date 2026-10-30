using System.Runtime.InteropServices;

namespace Stratocraft.Character.Data
{
    /// <summary>
    /// Fiziksel karakter duruş (stance) durumları.
    /// 7 aktif hareket modu + 1 ağır yaralı yerde sürünme modu.
    /// </summary>
    public enum CharacterStance : byte
    {
        Standing = 0,       // Ayakta yürüme (4.0 m/s, yükseklik 1.80 m)
        Sprinting = 1,      // Depar (6.8 m/s, yükseklik 1.80 m)
        Crouching = 2,      // Çömelme (2.2 m/s, yükseklik 1.25 m)
        Sliding = 3,        // Kayma (ivmeli sönümlü, yükseklik 0.90 m)
        ProneCrawling = 4,  // Yüzüstü sürünme — 1x1m tünel geçişi (1.1 m/s, yükseklik 0.65 m)
        Swimming = 5,       // Yüzme — Arşimet kaldırma kuvveti (2.8 m/s)
        Climbing = 6,       // Tırmanma — Çıkıntı/halat/merdiven (1.8 m/s)
        BleedOutCrawl = 7   // Ağır yaralı kanama sürünmesi (0.65 m/s, 30 sn sayaç)
    }

    /// <summary>
    /// DOTS uyumlu fiziksel karakter hareket bileşeni (StructLayout Sequential).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CharacterMovementState
    {
        public float VelocityX;
        public float VelocityY;
        public float VelocityZ;
        public float GroundNormalX;
        public float GroundNormalY;
        public float GroundNormalZ;
        public float CurrentSpeedMetersPerSec;
        public float EquippedMassKg;
        public float CapsuleHeightMeters;
        public float CapsuleRadiusMeters;
        public CharacterStance Stance;
        public byte IsGrounded;
        public byte IsSubmergedInFluid;
        public byte IsBackpackOverlayOpen; // 1 ise hız çarpanı * 0.30f (4.0 -> 1.2 m/s)

        public static CharacterMovementState CreateDefault()
        {
            return new CharacterMovementState
            {
                VelocityX = 0f,
                VelocityY = 0f,
                VelocityZ = 0f,
                GroundNormalX = 0f,
                GroundNormalY = 1f,
                GroundNormalZ = 0f,
                CurrentSpeedMetersPerSec = 4.0f,
                EquippedMassKg = 10.0f,
                CapsuleHeightMeters = 1.80f,
                CapsuleRadiusMeters = 0.35f,
                Stance = CharacterStance.Standing,
                IsGrounded = 1,
                IsSubmergedInFluid = 0,
                IsBackpackOverlayOpen = 0
            };
        }
    }
}
