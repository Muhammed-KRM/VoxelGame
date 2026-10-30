using System;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// 7 durus (Standing, Sprinting, Crouching, Sliding, ProneCrawling, Swimming, Climbing) +
    /// BleedOutCrawl fiziksel karakter kontrolcusu.
    /// - 1x1m voksel tunellerinden yalnizca ProneCrawling (0.65m yukseklik) veya BleedOutCrawl gecebilir.
    /// - I tusuyla 3B Kumas Canta (IsBackpackOverlayOpen == 1) acildiginda hiz %70 duser (4.0 -> 1.2 m/s).
    /// - Suda Arsimet kaldirma kuvveti (F_buoyancy = rho * V_submerged * g - m_total * g) uygulanir.
    /// </summary>
    public sealed class PhysicalCharacterControllerSystem
    {
        public const float BaseStandingSpeed = 4.0f;
        public const float BaseSprintingSpeed = 6.8f;
        public const float BaseCrouchingSpeed = 2.2f;
        public const float BaseSlidingSpeed = 5.4f;
        public const float BaseProneCrawlingSpeed = 1.1f;
        public const float BaseSwimmingSpeed = 2.8f;
        public const float BaseClimbingSpeed = 1.8f;
        public const float BaseBleedOutCrawlSpeed = 0.65f;

        public const float BackpackOpenSpeedMultiplier = 0.30f; // %70 yavaslama: 4.0 -> 1.2 m/s

        /// <summary>
        /// Durusa gore kapsul yuksekligini (metre) dondurur.
        /// 1 voksel = 1.0 metre oldugundan 1x1m tunelden gecmek icin yukseklik &lt;= 0.95m olmalidir.
        /// </summary>
        public static float GetStanceCapsuleHeight(CharacterStance stance)
        {
            return stance switch
            {
                CharacterStance.Standing => 1.80f,
                CharacterStance.Sprinting => 1.80f,
                CharacterStance.Crouching => 1.25f,
                CharacterStance.Sliding => 0.90f,
                CharacterStance.ProneCrawling => 0.65f,
                CharacterStance.Swimming => 0.85f,
                CharacterStance.Climbing => 1.70f,
                CharacterStance.BleedOutCrawl => 0.55f,
                _ => 1.80f
            };
        }

        public static float GetStanceBaseSpeed(CharacterStance stance)
        {
            return stance switch
            {
                CharacterStance.Standing => BaseStandingSpeed,
                CharacterStance.Sprinting => BaseSprintingSpeed,
                CharacterStance.Crouching => BaseCrouchingSpeed,
                CharacterStance.Sliding => BaseSlidingSpeed,
                CharacterStance.ProneCrawling => BaseProneCrawlingSpeed,
                CharacterStance.Swimming => BaseSwimmingSpeed,
                CharacterStance.Climbing => BaseClimbingSpeed,
                CharacterStance.BleedOutCrawl => BaseBleedOutCrawlSpeed,
                _ => BaseStandingSpeed
            };
        }

        /// <summary>
        /// Hedef voksel hucresindeki dikey boslugu (headroom) olcer ve durusun sigip sigmadigini denetler.
        /// </summary>
        public static bool CanFitInVoxelClearance(VoxelChunkComponent chunk, int localX, int y, int localZ, CharacterStance stance)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            float requiredHeight = GetStanceCapsuleHeight(stance);
            int requiredVoxelBlocks = requiredHeight <= 1.0f ? 1 : 2;

            for (int dy = 0; dy < requiredVoxelBlocks; dy++)
            {
                int checkY = y + dy;
                if (checkY < 0 || checkY >= StratocraftConstants.ChunkSizeY)
                {
                    return false;
                }

                VoxelCell voxel = chunk.GetVoxel(localX, checkY, localZ);
                if (voxel.IsSolid)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Karakter durumunu, hiz carpanlarini ve Arsimet kaldirma kuvvetini gunceller.
        /// </summary>
        public void StepMovement(
            ref CharacterMovementState movement,
            ref CharacterVitalsComponent vitals,
            CharacterStance requestedStance,
            (float X, float Z) moveInputDir,
            int availableVoxelHeadroomBlocks,
            float deltaTime)
        {
            if (vitals.CurrentHp <= 0.0f)
            {
                movement.Stance = CharacterStance.BleedOutCrawl;
            }
            else
            {
                float requestedHeight = GetStanceCapsuleHeight(requestedStance);
                int neededBlocks = requestedHeight <= 1.0f ? 1 : 2;
                if (availableVoxelHeadroomBlocks >= neededBlocks)
                {
                    movement.Stance = requestedStance;
                }
                else if (availableVoxelHeadroomBlocks == 1)
                {
                    // 1x1m tunel icinde ayaga kalkmaya calisilirsa ProneCrawling'de kalinir
                    movement.Stance = CharacterStance.ProneCrawling;
                }
            }

            movement.CapsuleHeightMeters = GetStanceCapsuleHeight(movement.Stance);
            float speed = GetStanceBaseSpeed(movement.Stance);

            // 3B Kumas Canta (I tusu) acikken oyun durmaz, yurume hizi %70 duser (4.0 -> 1.2 m/s)
            if (movement.IsBackpackOverlayOpen != 0)
            {
                speed *= BackpackOpenSpeedMultiplier;
                if (movement.Stance == CharacterStance.Sprinting)
                {
                    movement.Stance = CharacterStance.Standing;
                    speed = BaseStandingSpeed * BackpackOpenSpeedMultiplier;
                }
            }

            // Agirlik cezasi (80 kg uzeri kademeli yavaslama)
            if (movement.EquippedMassKg > 80.0f)
            {
                float overWeightRatio = Math.Clamp((movement.EquippedMassKg - 80.0f) / 80.0f, 0.0f, 0.65f);
                speed *= (1.0f - overWeightRatio);
            }

            movement.CurrentSpeedMetersPerSec = speed;

            float dirLenSq = moveInputDir.X * moveInputDir.X + moveInputDir.Z * moveInputDir.Z;
            float invLen = dirLenSq > 1.0f ? (1.0f / MathF.Sqrt(dirLenSq)) : 1.0f;

            movement.VelocityX = moveInputDir.X * invLen * speed;
            movement.VelocityZ = moveInputDir.Z * invLen * speed;

            // Suda Arsimet kaldirma kuvveti vs Zirh kutlesi
            if (movement.IsSubmergedInFluid != 0)
            {
                const float fluidDensityNormalized = 85.0f; // 85 kg esdeger kaldirma
                float totalMass = 70.0f + movement.EquippedMassKg;
                float netBuoyancyAccel = (fluidDensityNormalized - totalMass) * 0.12f;
                movement.VelocityY = Math.Clamp(movement.VelocityY + netBuoyancyAccel * deltaTime, -4.5f, 2.5f);
            }
            else if (movement.IsGrounded != 0)
            {
                movement.VelocityY = 0.0f;
            }
            else
            {
                movement.VelocityY -= 9.81f * deltaTime;
            }

            // Depar stamina tuketimi
            if (movement.Stance == CharacterStance.Sprinting && dirLenSq > 0.01f)
            {
                vitals.Stamina = MathF.Max(0.0f, vitals.Stamina - 8.5f * deltaTime);
                if (vitals.Stamina <= 0.0f)
                {
                    movement.Stance = CharacterStance.Standing;
                    movement.CurrentSpeedMetersPerSec = BaseStandingSpeed * (movement.IsBackpackOverlayOpen != 0 ? BackpackOpenSpeedMultiplier : 1.0f);
                }
            }
            else
            {
                vitals.Stamina = MathF.Min(vitals.MaxStamina, vitals.Stamina + 6.0f * deltaTime);
            }
        }
    }
}
