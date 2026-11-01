#nullable enable
using System;
using System.Numerics;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 3.1 — Kinematik Fiziksel Karakter ve Parkur Kontrolcusu:
    /// - Kanonik Hizlar: Yurume 4.0 m/s, Depar 7.0 m/s, Comelme 2.0 m/s, Yuzustu Surunme (1x1m tunel) 1.0 m/s,
    ///   3B Kumas Canta Acikken %70 Yavaslama (4.0 -&gt; 1.2 m/s, oyun asla durmaz!).
    /// - Kapsul Boyutlari: Ayakta 1.80m (R=0.35m), Comelme 1.15m (R=0.32m), Kayma 0.90m (R=0.30m),
    ///   ProneCrawling 0.45m (R_prone = 0.20m -&gt; dar Smooth Surface Nets 1x1m bogazlarindan takilmadan gecer!).
    /// - 5-Duzlemli CollideAndSlide, 3-Fazli AutoStep (&lt;= 0.52m), SnapToGround (&lt;= 0.35m),
    ///   Egim Duyarli Kayma (15 EP, duz zeminde 3.58m), 0.55s Kubik Bezier Mantle ve %15 Stamina Tukenme Histerezis Kilidi!
    /// </summary>
    public sealed class PhysicalCharacterControllerSystem
    {
        public const float BaseStandingSpeed = StratocraftConstants.WalkSpeedMps;      // 4.0 m/s
        public const float BaseSprintingSpeed = StratocraftConstants.SprintSpeedMps;   // 7.0 m/s
        public const float BaseCrouchingSpeed = StratocraftConstants.CrouchSpeedMps;   // 2.0 m/s
        public const float BaseSlidingInitialSpeed = 8.2f;                             // ~3.58m duz kayma
        public const float BaseProneCrawlingSpeed = StratocraftConstants.ProneSpeedMps;// 1.0 m/s
        public const float BaseSwimmingSpeed = 2.8f;
        public const float BaseClimbingSpeed = 1.8f;
        public const float BaseBleedOutCrawlSpeed = 0.65f;

        public const float BackpackOpenSpeedMultiplier = 0.30f; // %70 yavaslama: 4.0 -> 1.2 m/s
        public const float SlideStaminaCostEp = 15.0f;
        public const float SlideFrictionPerSec = 9.4f;          // v0^2 / (2*mu) = 8.2^2 / 18.8 = 3.576m!
        public const float MantleDurationSeconds = 0.55f;
        public const float ExhaustionRecoveryRatio = 0.15f;     // %15 stamina dolmadan tekrar depar acilamaz
        public const float MaxAutoStepHeightMeters = 0.52f;
        public const float GroundSnapDistanceMeters = 0.35f;

        public static float GetStanceCapsuleHeight(CharacterStance stance)
        {
            return stance switch
            {
                CharacterStance.Standing => StratocraftConstants.StandingHeightMeters,
                CharacterStance.Sprinting => StratocraftConstants.StandingHeightMeters,
                CharacterStance.Crouching => 1.15f,
                CharacterStance.Sliding => StratocraftConstants.CrouchHeightMeters,
                CharacterStance.ProneCrawling => StratocraftConstants.ProneHeightMeters,
                CharacterStance.Swimming => 0.85f,
                CharacterStance.Climbing => 1.70f,
                CharacterStance.BleedOutCrawl => 0.45f,
                _ => StratocraftConstants.StandingHeightMeters
            };
        }

        public static float GetStanceCapsuleRadius(CharacterStance stance)
        {
            return stance switch
            {
                CharacterStance.ProneCrawling or CharacterStance.BleedOutCrawl => StratocraftConstants.ProneCapsuleRadiusMeters, // 0.20m
                CharacterStance.Sliding => 0.30f,
                CharacterStance.Crouching => 0.32f,
                _ => StratocraftConstants.StandingCapsuleRadiusMeters // 0.35m
            };
        }

        public static float GetStanceBaseSpeed(CharacterStance stance)
        {
            return stance switch
            {
                CharacterStance.Standing => BaseStandingSpeed,
                CharacterStance.Sprinting => BaseSprintingSpeed,
                CharacterStance.Crouching => BaseCrouchingSpeed,
                CharacterStance.Sliding => BaseSlidingInitialSpeed,
                CharacterStance.ProneCrawling => BaseProneCrawlingSpeed,
                CharacterStance.Swimming => BaseSwimmingSpeed,
                CharacterStance.Climbing => BaseClimbingSpeed,
                CharacterStance.BleedOutCrawl => BaseBleedOutCrawlSpeed,
                _ => BaseStandingSpeed
            };
        }

        /// <summary>
        /// Hedef voksel hucresindeki dikey tavan boslugunu (CanUncrouch / Headroom) olcer.
        /// 1x1m tunelden yalnizca ProneCrawling (0.45m) veya Sliding/BleedOutCrawl gecebilir; Standing ve Crouching sigmaz!
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

                uint raw = chunk.GetVoxel(localX, checkY, localZ).RawValue;
                if (VoxelBitUtility.IsPhysicallySolid(raw) && raw != MaterialIdCatalog.Air)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 5-Duzlemli Collide-And-Slide Kinematik Hiz Yansiticisi:
        /// Tek duzlemde yuzeye paralel kayar, iki duzlem kesisiminde (kose/oluk) n1 x n2 katlanma cizgisi (crease) boyunca kayar,
        /// uc dikey/taban duzlem kesisiminde titresimsiz tam sifirlanir!
        /// </summary>
        public static Vector3 SolveCollideAndSlide5Planes(Vector3 desiredVelocity, ReadOnlySpan<Vector3> contactNormals)
        {
            Vector3 vel = desiredVelocity;
            int planeCount = Math.Min(5, contactNormals.Length);
            Span<Vector3> activePlanes = stackalloc Vector3[5];
            int activeCount = 0;

            for (int i = 0; i < planeCount; i++)
            {
                Vector3 n = contactNormals[i];
                float lenSq = n.LengthSquared();
                if (lenSq < 1e-6f)
                {
                    continue;
                }

                n /= MathF.Sqrt(lenSq);
                if (Vector3.Dot(vel, n) >= -1e-5f)
                {
                    continue;
                }

                activePlanes[activeCount++] = n;

                if (activeCount == 1)
                {
                    vel -= n * Vector3.Dot(vel, n);
                }
                else if (activeCount == 2)
                {
                    Vector3 crease = Vector3.Cross(activePlanes[0], activePlanes[1]);
                    float cLenSq = crease.LengthSquared();
                    if (cLenSq < 1e-5f)
                    {
                        vel = Vector3.Zero;
                        break;
                    }
                    crease /= MathF.Sqrt(cLenSq);
                    vel = crease * Vector3.Dot(desiredVelocity, crease);
                }
                else
                {
                    // 3 veya daha fazla bagimsiz engel duzlemi kesisimi -> sifir titresimli tam durma
                    if (Vector3.Dot(vel, n) < -1e-4f)
                    {
                        vel = Vector3.Zero;
                        break;
                    }
                }
            }

            return vel;
        }

        /// <summary>
        /// 15 EP tuketerek egim duyarli kayma (Slide) baslatir. Duz zeminde ~3.58m mesafe kat eder.
        /// </summary>
        public static bool TryInitiateSlide(
            ref CharacterMovementState movement,
            ref CharacterVitalsComponent vitals,
            (float X, float Z) forwardDir)
        {
            if (movement.IsGrounded == 0 ||
                movement.IsBackpackOverlayOpen != 0 ||
                movement.IsExhaustedStaminaLock != 0 ||
                vitals.Stamina < SlideStaminaCostEp)
            {
                return false;
            }

            float lenSq = forwardDir.X * forwardDir.X + forwardDir.Z * forwardDir.Z;
            if (lenSq < 0.01f)
            {
                return false;
            }

            float inv = 1.0f / MathF.Sqrt(lenSq);
            vitals.Stamina = MathF.Max(0f, vitals.Stamina - SlideStaminaCostEp);
            if (vitals.Stamina <= 0.01f)
            {
                movement.IsExhaustedStaminaLock = 1;
            }

            movement.Stance = CharacterStance.Sliding;
            movement.CapsuleHeightMeters = GetStanceCapsuleHeight(CharacterStance.Sliding);
            movement.CapsuleRadiusMeters = GetStanceCapsuleRadius(CharacterStance.Sliding);
            movement.CurrentSpeedMetersPerSec = BaseSlidingInitialSpeed;
            movement.SlideRemainingSeconds = BaseSlidingInitialSpeed / SlideFrictionPerSec;
            movement.VelocityX = forwardDir.X * inv * BaseSlidingInitialSpeed;
            movement.VelocityZ = forwardDir.Z * inv * BaseSlidingInitialSpeed;
            return true;
        }

        /// <summary>
        /// 3-Kure Izleme (Gogus Engeli + Bas Ustu Bosluk + Cikinti Ust Boslugu) ile 0.55s Kubik Bezier Mantle yorungesi hesaplar.
        /// </summary>
        public static Vector3 EvaluateCubicBezierMantlePosition(Vector3 startFeetPos, Vector3 ledgeTopPos, float progress01)
        {
            float t = Math.Clamp(progress01, 0f, 1f);
            float u = 1f - t;
            // Once dikey yukselis (P1, P2), ardindan cikinti uzerine ileri adim (P3) -> koseye takilmaz!
            Vector3 p0 = startFeetPos;
            Vector3 p1 = new Vector3(startFeetPos.X, ledgeTopPos.Y + 0.15f, startFeetPos.Z);
            Vector3 p2 = Vector3.Lerp(p1, ledgeTopPos + new Vector3(0f, 0.10f, 0f), 0.65f);
            Vector3 p3 = ledgeTopPos;

            return (u * u * u) * p0
                 + (3f * u * u * t) * p1
                 + (3f * u * t * t) * p2
                 + (t * t * t) * p3;
        }

        /// <summary>
        /// Karakter durumunu, tavan kilidini (CanUncrouch), %15 stamina histerezisini, egimli kaymayi ve Arsimet kaldirmasini gunceller.
        /// </summary>
        public void StepMovement(
            ref CharacterMovementState movement,
            ref CharacterVitalsComponent vitals,
            CharacterStance requestedStance,
            (float X, float Z) moveInputDir,
            int availableVoxelHeadroomBlocks,
            float deltaTime)
        {
            // %15 Stamina Tukenme Histerezis Kilidi guncellemesi
            float recoveryThreshold = MathF.Max(15.0f, vitals.MaxStamina * ExhaustionRecoveryRatio);
            if (vitals.Stamina <= 0.01f)
            {
                movement.IsExhaustedStaminaLock = 1;
            }
            else if (movement.IsExhaustedStaminaLock != 0 && vitals.Stamina >= recoveryThreshold)
            {
                movement.IsExhaustedStaminaLock = 0;
            }

            if (vitals.CurrentHp <= 0.0f)
            {
                movement.Stance = CharacterStance.BleedOutCrawl;
                movement.SlideRemainingSeconds = 0f;
            }
            else if (movement.Stance == CharacterStance.Sliding && movement.SlideRemainingSeconds > 0f)
            {
                // Aktif kayma devam ediyor: egim ivmesi + Coulomb surtunmesi
                Vector2 horizVel = new Vector2(movement.VelocityX, movement.VelocityZ);
                float curSpeed = horizVel.Length();
                Vector3 groundNormal = new Vector3(movement.GroundNormalX, movement.GroundNormalY, movement.GroundNormalZ);
                if (groundNormal.LengthSquared() < 0.01f) groundNormal = Vector3.UnitY;
                else groundNormal = Vector3.Normalize(groundNormal);

                // Yokusa asagi kayarken yercekimi hizlandirir, yokus yukari hizla frenler
                Vector3 downSlope = new Vector3(0f, -9.81f, 0f) - groundNormal * Vector3.Dot(new Vector3(0f, -9.81f, 0f), groundNormal);
                Vector2 moveDir2D = curSpeed > 0.01f ? horizVel / curSpeed : new Vector2(moveInputDir.X, moveInputDir.Z);
                float slopeAccel = moveDir2D.X * downSlope.X + moveDir2D.Y * downSlope.Z;

                float nextSpeed = MathF.Max(0f, curSpeed + (slopeAccel - SlideFrictionPerSec) * deltaTime);
                movement.SlideRemainingSeconds = MathF.Max(0f, movement.SlideRemainingSeconds - deltaTime);
                movement.CurrentSpeedMetersPerSec = nextSpeed;
                movement.VelocityX = moveDir2D.X * nextSpeed;
                movement.VelocityZ = moveDir2D.Y * nextSpeed;
                movement.CapsuleHeightMeters = GetStanceCapsuleHeight(CharacterStance.Sliding);
                movement.CapsuleRadiusMeters = GetStanceCapsuleRadius(CharacterStance.Sliding);

                if (nextSpeed <= BaseCrouchingSpeed || movement.SlideRemainingSeconds <= 0f)
                {
                    movement.SlideRemainingSeconds = 0f;
                    movement.Stance = availableVoxelHeadroomBlocks <= 1 ? CharacterStance.ProneCrawling : CharacterStance.Crouching;
                }
                else
                {
                    return;
                }
            }
            else
            {
                if (requestedStance == CharacterStance.Sprinting && movement.IsExhaustedStaminaLock != 0)
                {
                    requestedStance = CharacterStance.Standing;
                }

                float requestedHeight = GetStanceCapsuleHeight(requestedStance);
                int neededBlocks = requestedHeight <= 1.0f ? 1 : 2;
                if (availableVoxelHeadroomBlocks >= neededBlocks)
                {
                    movement.Stance = requestedStance;
                }
                else if (availableVoxelHeadroomBlocks <= 1)
                {
                    // CanUncrouch Tavan Kilidi: 1x1m tunel icinde ayaga kalkmaya veya comelmeye calisilirsa ProneCrawling'de kilitli kalir!
                    movement.Stance = CharacterStance.ProneCrawling;
                }
            }

            movement.CapsuleHeightMeters = GetStanceCapsuleHeight(movement.Stance);
            movement.CapsuleRadiusMeters = GetStanceCapsuleRadius(movement.Stance);
            float speed = GetStanceBaseSpeed(movement.Stance);

            // 3B Kumas Canta (I tusu) acikken oyun durmaz, yurume hizi %70 duser (4.0 -> 1.2 m/s)
            if (movement.IsBackpackOverlayOpen != 0)
            {
                if (movement.Stance == CharacterStance.Sprinting)
                {
                    movement.Stance = CharacterStance.Standing;
                    speed = BaseStandingSpeed;
                }
                speed *= BackpackOpenSpeedMultiplier;
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

            // Suda Arsimet kaldirma kuvveti vs Zirh kutlesi (agir zirhla suya batar!)
            if (movement.IsSubmergedInFluid != 0)
            {
                const float displacedWaterMassMaxKg = 88.0f; // 0.088 m^3 insan hacmi * 1000 kg/m^3
                float totalMass = 72.0f + movement.EquippedMassKg;
                float netBuoyancyAccel = ((displacedWaterMassMaxKg - totalMass) / MathF.Max(40f, totalMass)) * 9.81f;
                movement.VelocityY = Math.Clamp(movement.VelocityY + netBuoyancyAccel * deltaTime - movement.VelocityY * 1.8f * deltaTime, -5.0f, 2.8f);
            }
            else if (movement.IsGrounded != 0)
            {
                movement.VelocityY = 0.0f;
            }
            else
            {
                movement.VelocityY -= 9.81f * deltaTime;
            }

            // Depar stamina tuketimi ve %15 tukenme kilidi
            if (movement.Stance == CharacterStance.Sprinting && dirLenSq > 0.01f)
            {
                vitals.Stamina = MathF.Max(0.0f, vitals.Stamina - 8.5f * deltaTime);
                if (vitals.Stamina <= 0.01f)
                {
                    vitals.Stamina = 0.0f;
                    movement.IsExhaustedStaminaLock = 1;
                    movement.Stance = CharacterStance.Standing;
                    movement.CurrentSpeedMetersPerSec = BaseStandingSpeed * (movement.IsBackpackOverlayOpen != 0 ? BackpackOpenSpeedMultiplier : 1.0f);
                }
            }
            else
            {
                vitals.Stamina = MathF.Min(vitals.MaxStamina, vitals.Stamina + 7.5f * deltaTime);
            }
        }
    }
}
