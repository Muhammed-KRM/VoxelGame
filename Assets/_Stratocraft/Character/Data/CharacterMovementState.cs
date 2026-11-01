using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Character.Data
{
    /// <summary>
    /// Fiziksel karakter durus (stance) durumlari (GAME LOGIC BIBLE v7.0 Bolum 3.1).
    /// 7 aktif hareket modu + 1 agir yarali yerde surunme modu.
    /// </summary>
    public enum CharacterStance : byte
    {
        Standing = 0,       // Ayakta yurume (4.0 m/s, yukseklik 1.80 m, R = 0.35 m)
        Sprinting = 1,      // Depar (7.0 m/s, yukseklik 1.80 m, R = 0.35 m)
        Crouching = 2,      // Comelme (2.0 m/s, yukseklik 1.15 m, R = 0.32 m)
        Sliding = 3,        // Kayma (15 EP, 3.5-3.66m, yukseklik 0.90 m, R = 0.30 m)
        ProneCrawling = 4,  // Yuzustu surunme — 1x1m tunel gecisi (1.0 m/s, yukseklik 0.45 m, R = 0.20 m)
        Swimming = 5,       // Yuzme — Arsimet kaldirma kuvveti (2.8 m/s, yukseklik 0.85 m)
        Climbing = 6,       // Tirmanma / Mantle — Cikinti/halat/merdiven (1.8 m/s, 0.55s Bezier Mantle)
        BleedOutCrawl = 7   // Agir yarali kanama surunmesi (0.65 m/s, 30 sn sayac)
    }

    /// <summary>
    /// DOTS uyumlu fiziksel karakter hareket ve parkur bileseni (unmanaged, 0 GC).
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
        public float SlideRemainingSeconds;
        public float MantleProgress01;
        public CharacterStance Stance;
        public byte IsGrounded;
        public byte IsSubmergedInFluid;
        public byte IsBackpackOverlayOpen; // 1 ise hiz carpani * 0.30f (4.0 -> 1.2 m/s, oyun durmaz)
        public byte IsExhaustedStaminaLock; // 1 ise stamina %15'e dolana kadar depar/kayma/mantle kilitli

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
                CurrentSpeedMetersPerSec = StratocraftConstants.WalkSpeedMps,
                EquippedMassKg = 10.0f,
                CapsuleHeightMeters = StratocraftConstants.StandingHeightMeters,
                CapsuleRadiusMeters = StratocraftConstants.StandingCapsuleRadiusMeters,
                SlideRemainingSeconds = 0f,
                MantleProgress01 = 0f,
                Stance = CharacterStance.Standing,
                IsGrounded = 1,
                IsSubmergedInFluid = 0,
                IsBackpackOverlayOpen = 0,
                IsExhaustedStaminaLock = 0
            };
        }
    }
}
