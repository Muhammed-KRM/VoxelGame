#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 1 — Anti-Gomulme (Depenetration), Kapsul-Arazi Carpismasi ve Fiziksel Karakter Hareket Koprusu.
    /// <see cref="PhysicalCharacterControllerSystem"/> motorunu sahnedeki oyuncu transformuna baglar;
    /// oyuncunun kendi icine blok koyup dunyadan dusmesini (%100 Anti-Burial CheckCapsule + Depenetration) onler.
    /// </summary>
    public sealed class Stage1PlayerMovementBridge
    {
        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) kapsul-blok kesisim kontrolu.
        /// Oyuncunun hedef voksel hucresine (wx, wy, wz) blok koymadan once kendi kapsulu ile
        /// kesisip kesismedigini kontrol eder.
        /// </summary>
        public static bool CanPlaceBlockOutsidePlayerCapsule(
            int wx, int wy, int wz, float playerX, float playerY, float playerZ, float capsuleHeight, float brushRadius = 0.5f)
        {
            float radius = 0.36f;
            float height = MathF.Max(0.45f, capsuleHeight);
            float halfExtent = MathF.Max(0.48f, brushRadius * 0.85f);
            float bx = wx + 0.5f, by = wy + 0.5f, bz = wz + 0.5f;

            bool overlapsX = (bx + halfExtent) > (playerX - radius) && (bx - halfExtent) < (playerX + radius);
            bool overlapsY = (by + halfExtent) > (playerY + 0.05f) && (by - halfExtent) < (playerY + height);
            bool overlapsZ = (bz + halfExtent) > (playerZ - radius) && (bz - halfExtent) < (playerZ + radius);
            return !(overlapsX && overlapsY && overlapsZ);
        }

        /// <summary>
        /// Oyuncu suya girdiginde veya hizla yuzdugunde merkez hucredeki suyun kucuk bir kismini (maxPushUnits)
        /// komsu acik hucrelere esit dagitarak %100 kutle korunumlu (net degisim = 0) fiziksel calkanti dalgasi olusturur.
        /// </summary>
        public static int ApplyMassConservingSwimmerSurge(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            HashSet<(int cx, int cz)>? activeFluidChunks,
            int wx,
            int wy,
            int wz,
            int maxPushUnits = 16)
        {
            if (wy < 1 || wy >= 254 || maxPushUnits <= 0)
            {
                return 0;
            }

            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? centerFluids) || centerFluids == null)
            {
                return 0;
            }

            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            int centerIdx = (wy << 8) | (lz << 4) | lx;
            if ((uint)centerIdx >= (uint)centerFluids.Length)
            {
                return 0;
            }

            FluidCell16 src = new FluidCell16(centerFluids[centerIdx]);
            if (src.Type != FluidType.Water || src.Mass < 24)
            {
                return 0;
            }

            int budget = Math.Min(maxPushUnits, src.Mass - 12);
            if (budget <= 0)
            {
                return 0;
            }

            ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };
            int perDir = Math.Max(2, budget / 4);
            int totalMoved = 0;

            for (int d = 0; d < 4 && totalMoved < budget; d++)
            {
                int nwx = wx + dx[d];
                int nwz = wz + dz[d];
                int ncx = nwx >= 0 ? (nwx >> 4) : -(((-nwx) + 15) >> 4);
                int ncz = nwz >= 0 ? (nwz >> 4) : -(((-nwz) + 15) >> 4);
                if (!fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nFluids) || nFluids == null ||
                    !worldChunks.TryGetValue((ncx, ncz), out uint[]? nSolids) || nSolids == null)
                {
                    continue;
                }

                int nlx = nwx - ncx * 16;
                int nlz = nwz - ncz * 16;
                int nIdx = (wy << 8) | (nlz << 4) | nlx;
                if ((uint)nIdx >= (uint)nFluids.Length || (uint)nIdx >= (uint)nSolids.Length)
                {
                    continue;
                }

                byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(nSolids[nIdx]);
                if (cap == 0)
                {
                    continue;
                }

                FluidCell16 dst = new FluidCell16(nFluids[nIdx]);
                if (dst.Type != FluidType.None && dst.Type != FluidType.Water)
                {
                    continue;
                }

                int space = cap - dst.Mass;
                if (space <= 0)
                {
                    continue;
                }

                int move = Math.Min(perDir, Math.Min(budget - totalMoved, space));
                if (move <= 0)
                {
                    continue;
                }

                nFluids[nIdx] = FluidCell16.Pack(FluidType.Water, (byte)(dst.Mass + move), isSettled: false, dst.PressureHead).RawValue;
                totalMoved += move;
                dirtyFluidChunks?.Add((ncx, ncz));
                activeFluidChunks?.Add((ncx, ncz));
            }

            if (totalMoved > 0)
            {
                centerFluids[centerIdx] = FluidCell16.Pack(FluidType.Water, (byte)(src.Mass - totalMoved), isSettled: false, src.PressureHead).RawValue;
                dirtyFluidChunks?.Add((cx, cz));
                activeFluidChunks?.Add((cx, cz));
            }

            return totalMoved;
        }

#if UNITY_5_3_OR_NEWER
        private const float VoidRescueThresholdY = -14.0f;
        private const float MaxStepUpHeightMeters = 0.65f;
        private const float BaseBodyMassKg = 75.0f;
        private const float DodgeStaminaCostEp = 18.0f;
        private const float DodgeDurationSeconds = 0.26f;
        private const float DodgeSpeedMetersPerSec = 6.8f;
        private const float DodgeCooldownSeconds = 0.65f;
        private const float ExhaustedWalkSpeedCapMps = 1.45f;

        private readonly PhysicalCharacterControllerSystem _controllerSystem = new();
        private CharacterMovementState _movementState = CharacterMovementState.CreateDefault();
        private CharacterVitalsComponent _vitals = CharacterVitalsComponent.CreateDefault();
        private float _verticalVelocity, _coyoteGroundedTimer = 0.15f, _jumpLaunchTimer;
        private Vector3 _safeSpawnPosition = new Vector3(-1.4f, 11.5f, -5.2f);
        private bool _isMantling;
        private float _mantleElapsedSeconds;
        private System.Numerics.Vector3 _mantleStartPos;
        private System.Numerics.Vector3 _mantleTargetPos;
        private float _dodgeRemainingSec;
        private float _dodgeCooldownSec;
        private Vector3 _dodgeDirectionWorld;
        private float _lowCeilingDebounceSec;
        private bool _wasTouchingWaterLastFrame;
        private float _swimmerSurgeCooldownSec;

        /// <summary>Oyuncu-su etkilesimi dunya konumu (_StratocraftSwimmerPos.xyz).</summary>
        public Vector3 WaterInteractionWorldPos { get; private set; }
        /// <summary>Oyuncu-su dalga etkilesim siddeti 0..1 (_StratocraftSwimmerPos.w).</summary>
        public float WaterInteractionStrength01 { get; private set; }
        /// <summary>Suya atlama / hizli giris sicrama darbesi 0..1 (_StratocraftSwimmerVel.w).</summary>
        public float WaterSplashPulse01 { get; private set; }

        /// <summary>Anlik fiziksel karakter hareket durumu (Stance, Hiz, Kapsul Boyutu, Stamina Kilidi).</summary>
        public ref CharacterMovementState MovementState => ref _movementState;
        /// <summary>Anlik biyolojik hayatta kalma gostergeleri (HP, Stamina, Oksijen, Sicaklik).</summary>
        public ref CharacterVitalsComponent Vitals => ref _vitals;
        /// <summary>Dusey yercekimi / ziplama hizi (m/s).</summary>
        public float VerticalVelocity { get => _verticalVelocity; set => _verticalVelocity = value; }
        /// <summary>0.55s Kubik Bezier Mantle tirmanisi aktif mi?</summary>
        public bool IsMantling => _isMantling;
        /// <summary>Fiziksel yan-adim / Dodge kacisi aktif mi?</summary>
        public bool IsDodging => _dodgeRemainingSec > 0f;
        /// <summary>Bosluga dusme durumunda geri isinlanilacak guvenli ada koordinatini ayarlar.</summary>
        public void SetSafeSpawnPosition(Vector3 safeWorldPosition) => _safeSpawnPosition = safeWorldPosition;
        /// <summary>Unity Vector3 tabanli kapsul-blok kesisim kontrolu.</summary>
        public static bool CanPlaceBlockOutsidePlayerCapsule(int wx, int wy, int wz, Vector3 playerPos, float capsuleHeight, float brushRadius = 0.5f)
            => CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerPos.x, playerPos.y, playerPos.z, capsuleHeight, brushRadius);
        /// <summary>Ornek bazli kapsul-blok kesisim kontrolu.</summary>
        public bool CanPlaceBlockOutsidePlayerCapsule(Transform playerTransform, int wx, int wy, int wz, float brushRadius = 0.5f)
            => CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerTransform.position, _movementState.CapsuleHeightMeters, brushRadius);

        /// <summary>Dunya (wx, wy, wz) koordinatindaki 32-bit vokseli okur.</summary>
        public static uint ReadWorldVoxelRaw(Dictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wy, int wz)
        {
            if (wy < 0 || wy >= 256) return 0u;
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            return worldChunks.TryGetValue((cx, cz), out uint[]? v) ? v[(wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16)] : 0u;
        }

        /// <summary>Dunya (wx, wy, wz) koordinatina 32-bit voksel yazar ve komsu chunk sinirlarini kirli isaretler.</summary>
        public static void WriteWorldVoxelRaw(
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks, int wx, int wy, int wz, uint rawValue)
        {
            if (wy < 1 || wy >= 255) return;
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;
            int lx = wx - cx * 16, lz = wz - cz * 16;
            voxels[(wy << 8) | (lz << 4) | lx] = rawValue;
            dirtyChunks.Add((cx, cz));
            if (lx <= 1 && worldChunks.ContainsKey((cx - 1, cz))) dirtyChunks.Add((cx - 1, cz));
            if (lx >= 14 && worldChunks.ContainsKey((cx + 1, cz))) dirtyChunks.Add((cx + 1, cz));
            if (lz <= 1 && worldChunks.ContainsKey((cx, cz - 1))) dirtyChunks.Add((cx, cz - 1));
            if (lz >= 14 && worldChunks.ContainsKey((cx, cz + 1))) dirtyChunks.Add((cx, cz + 1));
            Stage1InteractivePlayController.NotifyWorldVoxelEdited(cx, cz, wy, lx, lz, includeNeighbors: true);
        }

        /// <summary>WASD, kosu, kayma, surunme, 2.2m mantle, ziplama, sivi kaldirma/lav hasari ve kapsul-arazi depenetration cozumunu ilerletir.</summary>
        public void StepCharacterPhysics(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, Vector2 moveInput,
            bool wantSprint, bool wantCrouch, bool wantProne, bool jumpOrMantlePressed, bool isBackpackOpen,
            float equippedMassKg, float deltaTime, out string statusFeedback,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks = null,
            SoilMoistureAndMudSystem? soilMoistureSystem = null,
            HashSet<(int cx, int cz)>? dirtyFluidChunks = null,
            HashSet<(int cx, int cz)>? activeFluidChunks = null)
        {
            statusFeedback = string.Empty;
            _movementState.EquippedMassKg = equippedMassKg;
            _movementState.IsBackpackOverlayOpen = isBackpackOpen ? (byte)1 : (byte)0;

            // Usume, sicaklama veya aclik/susuzluk nedeniyle karakterin 0 HP'ye inip yere dusmesi (BleedOutCrawl) ve kalkamamasi engellenir!
            _vitals.CurrentHp = Mathf.Max(1.0f, _vitals.CurrentHp);
            _vitals.HungerKcal = 2000f;
            _vitals.ThirstHydration = 100f;
            if (_movementState.Stance == CharacterStance.BleedOutCrawl)
            {
                _movementState.Stance = CharacterStance.Standing;
            }

            Vector3 pos = playerTransform.position;
            if (pos.y < VoidRescueThresholdY)
            {
                RescueFromVoid(playerTransform, worldChunks);
                statusFeedback = "Guvenlik Kurtarmasi: Oyuncu ada yuzeyine geri tasindi";
                return;
            }
            if (_jumpLaunchTimer > 0f) _jumpLaunchTimer = Mathf.Max(0f, _jumpLaunchTimer - deltaTime);
            if (_dodgeCooldownSec > 0f) _dodgeCooldownSec = Mathf.Max(0f, _dodgeCooldownSec - deltaTime);

            // Aktif 0.55sn Kubik Bezier Mantle tirmanisi devam ediyorsa yorunge boyunca puruzsuz ilerlet
            if (_isMantling)
            {
                _mantleElapsedSeconds += deltaTime;
                float progress01 = Mathf.Clamp01(_mantleElapsedSeconds / PhysicalCharacterControllerSystem.MantleDurationSeconds);
                _movementState.MantleProgress01 = progress01;
                _movementState.Stance = CharacterStance.Climbing;
                _movementState.CapsuleHeightMeters = PhysicalCharacterControllerSystem.GetStanceCapsuleHeight(CharacterStance.Climbing);
                _movementState.CapsuleRadiusMeters = PhysicalCharacterControllerSystem.GetStanceCapsuleRadius(CharacterStance.Climbing);
                System.Numerics.Vector3 bPos = PhysicalCharacterControllerSystem.EvaluateCubicBezierMantlePosition(
                    _mantleStartPos, _mantleTargetPos, progress01);
                playerTransform.position = new Vector3(bPos.X, bPos.Y, bPos.Z);
                _verticalVelocity = 0f;

                if (progress01 >= 1.0f)
                {
                    _isMantling = false;
                    _movementState.MantleProgress01 = 0f;
                    _movementState.Stance = CharacterStance.Standing;
                    _movementState.IsGrounded = 1;
                    _coyoteGroundedTimer = 0.16f;
                }
                return;
            }

            // Kılıç savurma momentumu veya önceki karedeki eğim geçişinden kalan gömülmeyi tavan ölçümünden ÖNCE çöz
            ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
            pos = playerTransform.position;

            float groundY = SampleSupportGroundHeight(worldChunks, pos);
            float groundSnapTolerance = _movementState.IsGrounded != 0 ? 0.65f : 0.24f;
            bool grounded = _jumpLaunchTimer <= 0.001f && pos.y <= groundY + groundSnapTolerance && _verticalVelocity <= 0.25f;
            _coyoteGroundedTimer = grounded ? 0.16f : Mathf.Max(0f, _coyoteGroundedTimer - deltaTime);
            _movementState.IsGrounded = grounded ? (byte)1 : (byte)0;

            bool hasLedgeAhead = DetectClimbableLedgeAhead(playerTransform, worldChunks, out float ledgeTopY);
            Vector3 forwardFlat = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            Vector3 rightFlat = Vector3.ProjectOnPlane(playerTransform.right, Vector3.up).normalized;
            Vector3 wishDir = forwardFlat * moveInput.y + rightFlat * moveInput.x;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

            bool prevSliding = _movementState.Stance == CharacterStance.Sliding;
            if (!prevSliding && wantSprint && wantCrouch && wishDir.sqrMagnitude > 0.04f)
                PhysicalCharacterControllerSystem.TryInitiateSlide(ref _movementState, ref _vitals, (wishDir.x, wishDir.z));

            // Gercek voksel tavan yuksekligi (Headroom Clearance) kontrolu: egimli zemini tavan sanmamak icin acik hava boslugunu olc
            MeasureHeadroomClearance(worldChunks, pos, groundY, out bool rawCanCrouch115, out bool rawCanStand180);
            bool alreadyLowStance = _movementState.Stance == CharacterStance.Crouching || _movementState.Stance == CharacterStance.ProneCrawling;
            if (!rawCanCrouch115 || !rawCanStand180)
            {
                _lowCeilingDebounceSec = Mathf.Min(0.25f, _lowCeilingDebounceSec + deltaTime);
            }
            else
            {
                _lowCeilingDebounceSec = 0f;
            }

            bool enforceCeilingLock = alreadyLowStance || _lowCeilingDebounceSec >= 0.08f;
            bool canCrouch115 = !enforceCeilingLock || rawCanCrouch115;
            bool canStand180 = !enforceCeilingLock || rawCanStand180;

            CharacterStance reqStance = _movementState.Stance == CharacterStance.Sliding
                ? CharacterStance.Sliding
                : (wantProne ? CharacterStance.ProneCrawling : (wantCrouch ? CharacterStance.Crouching : (wantSprint ? CharacterStance.Sprinting : CharacterStance.Standing)));

            int headroomBlocks;
            if (!canCrouch115)
            {
                // 1x1m (0.60m) alcak tunel icinde ayaga kalkmak veya comelmek imkansizdir!
                if (!wantProne && _movementState.Stance != CharacterStance.Sliding)
                {
                    statusFeedback = "Tavan Alcak (1x1m Tunel Kilidi): Ayaga kalkmak veya comelmek icin bas ustu bosluk yok!";
                }
                reqStance = CharacterStance.ProneCrawling;
                headroomBlocks = 1;
            }
            else if (!canStand180)
            {
                // ~1.2m - 1.7m arasi boslukta comelinebilir (1.15m) ancak tam ayaga kalkilamaz (1.80m)!
                if (!wantProne && !wantCrouch && _movementState.Stance != CharacterStance.Sliding)
                {
                    statusFeedback = "Tavan Alcak (1.2m Bosluk): Ayaga kalkilamaz, Comelme (Crouch) durusunda kalindi.";
                }
                if (reqStance == CharacterStance.Standing || reqStance == CharacterStance.Sprinting)
                {
                    reqStance = CharacterStance.Crouching;
                }
                headroomBlocks = 2;
            }
            else
            {
                headroomBlocks = 3;
            }

            float staminaBeforeMove = _vitals.Stamina;
            _controllerSystem.StepMovement(ref _movementState, ref _vitals, reqStance, (wishDir.x, wishDir.z), headroomBlocks, deltaTime);
            float staminaDrainMult = Stage1GameSettings.StaminaDrainMultiplier;
            if (staminaDrainMult <= 0.01f)
            {
                _vitals.Stamina = _vitals.MaxStamina;
                _movementState.IsExhaustedStaminaLock = 0;
            }
            else if (_vitals.Stamina < staminaBeforeMove && Mathf.Abs(staminaDrainMult - 1.0f) > 0.01f)
            {
                float lost = staminaBeforeMove - _vitals.Stamina;
                _vitals.Stamina = Mathf.Clamp(staminaBeforeMove - lost * staminaDrainMult, 0f, _vitals.MaxStamina);
                if (_vitals.Stamina > 0.05f && staminaBeforeMove > 0.05f) _movementState.IsExhaustedStaminaLock = 0;
            }
            if (!prevSliding && _movementState.Stance == CharacterStance.Sliding)
                statusFeedback = "Kayma (Slide) Basladi (-15 EP -> 3.58m)";

            // Fiziksel Yan-Adim / Dodge Kacisi (LeftAlt veya Mouse3/Mouse4: 18 EP, 0.26sn 6.8 m/s patlama, i-frame yok!)
            float effectiveDodgeCost = DodgeStaminaCostEp * staminaDrainMult;
            bool dodgeTriggered = Input.GetKeyDown(Stage1GameSettings.KeyDodge) ||
                                  Input.GetKeyDown(KeyCode.LeftAlt) ||
                                  Input.GetKeyDown(KeyCode.Mouse3) ||
                                  Input.GetKeyDown(KeyCode.Mouse4);
            if (dodgeTriggered &&
                _dodgeRemainingSec <= 0f &&
                _dodgeCooldownSec <= 0f &&
                grounded &&
                _movementState.IsSubmergedInFluid == 0 &&
                _movementState.IsBackpackOverlayOpen == 0 &&
                _movementState.IsExhaustedStaminaLock == 0 &&
                canCrouch115 &&
                _vitals.Stamina >= effectiveDodgeCost)
            {
                _vitals.Stamina = MathF.Max(0f, _vitals.Stamina - effectiveDodgeCost);
                if (_vitals.Stamina <= 0.01f && staminaDrainMult > 0.01f) _movementState.IsExhaustedStaminaLock = 1;
                _dodgeDirectionWorld = wishDir.sqrMagnitude > 0.01f ? wishDir.normalized : -forwardFlat;
                _dodgeRemainingSec = DodgeDurationSeconds;
                _dodgeCooldownSec = DodgeCooldownSeconds;
                statusFeedback = "Fiziksel Yan-Adim / Dodge Kacisi (-18 EP | 6.8 m/s | i-Frame Yok!)";
            }

            // 0 EP Stamina Tukenme Kilidi (IsExhaustedStaminaLock != 0 -> %15 dolana kadar max 1.45 m/s yurume)
            // ve Hipotermi (-%20) / Aclik-Susuzluk (-%15) biyolojik hiz carpanlari:
            ApplyExhaustionAndBiologicalSpeedModifiers(ref statusFeedback);

            if (_dodgeRemainingSec > 0f)
            {
                float dodgeSpd = DodgeSpeedMetersPerSec * Stage1GameSettings.PlayerMoveSpeedMultiplier;
                _dodgeRemainingSec = Mathf.Max(0f, _dodgeRemainingSec - deltaTime);
                _movementState.CurrentSpeedMetersPerSec = dodgeSpd;
                _movementState.VelocityX = _dodgeDirectionWorld.x * dodgeSpd;
                _movementState.VelocityZ = _dodgeDirectionWorld.z * dodgeSpd;
            }

            ApplyJumpOrMantleVerticalImpulse(
                playerTransform, forwardFlat, jumpOrMantlePressed, grounded || _coyoteGroundedTimer > 0f, hasLedgeAhead, ledgeTopY, ref statusFeedback);
            StepFluidBuoyancyAndHazards(
                playerTransform, worldChunks, fluidChunks, dirtyFluidChunks, activeFluidChunks, deltaTime, ref statusFeedback);

            // WP-N1: Doygunlasmis islak camur zeminde (%15 - %35) gercekci hareket yavaslamasi
            if (grounded && _movementState.IsSubmergedInFluid == 0 && _dodgeRemainingSec <= 0f && soilMoistureSystem != null)
            {
                float mudMult = soilMoistureSystem.SampleGroundMudSlowdownMultiplier(
                    worldChunks, pos.x, pos.y, pos.z, out byte mudMoisture);
                if (mudMult < 0.995f)
                {
                    _movementState.CurrentSpeedMetersPerSec *= mudMult;
                    _movementState.VelocityX *= mudMult;
                    _movementState.VelocityZ *= mudMult;
                    if (string.IsNullOrEmpty(statusFeedback) && wishDir.sqrMagnitude > 0.02f)
                    {
                        int slowPct = Mathf.RoundToInt((1f - mudMult) * 100f);
                        statusFeedback = $"Camurlu Toprak (Nem %{(mudMoisture * 100) / 255}): Adimlar %{slowPct} Agirlasti";
                    }
                }
            }

            IntegrateHorizontalAndVerticalMotion(playerTransform, worldChunks, _dodgeRemainingSec > 0f ? _dodgeDirectionWorld : wishDir, deltaTime);
            ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
        }

        private static bool IsHardCollisionVoxel(uint raw)
        {
            if (!VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            return mat != MaterialIdCatalog.Air && mat != MaterialIdCatalog.GlowingMoss;
        }

        private static void MeasureHeadroomClearance(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 feetPos,
            float supportGroundY,
            out bool canCrouch115,
            out bool canStand180)
        {
            int wx = Mathf.FloorToInt(feetPos.x);
            int wz = Mathf.FloorToInt(feetPos.z);
            float refFeetY = supportGroundY > -20f ? Mathf.Max(feetPos.y, supportGroundY) : feetPos.y;
            int airBaseWy = Mathf.FloorToInt(refFeetY + 0.25f);

            // Smooth SurfaceNets egimli arazide zemin kolonu (wx, wz) kesirli refFeetY'den 1-2 voksel yukari uzanabilir;
            // alttaki kesintisiz zemin bloklarini gecerek oyuncunun ayak/govdesinin bulundugu GERCEK HAVA hucresini bul!
            int maxGroundSkipWy = Mathf.FloorToInt(refFeetY) + 2;
            while (airBaseWy <= maxGroundSkipWy && IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy, wz)))
            {
                airBaseWy++;
            }

            // Eger ayak hizasinda acik hava hucresi bulunamadiysa (gomulme cozumu devredeyse) sahte tavan kilidi uygulama
            if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy, wz)))
            {
                canCrouch115 = true;
                canStand180 = true;
                return;
            }

            bool blockedCrouchHead = IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy + 1, wz));
            bool blockedStandHead = IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy + 2, wz));

            canCrouch115 = !blockedCrouchHead;
            canStand180 = canCrouch115 && !blockedStandHead;
        }

        private void ApplyExhaustionAndBiologicalSpeedModifiers(ref string statusFeedback)
        {
            if (_movementState.Stance == CharacterStance.Sliding) return;

            float speed = _movementState.CurrentSpeedMetersPerSec;
            if (_movementState.IsExhaustedStaminaLock != 0)
            {
                speed = Mathf.Min(speed, ExhaustedWalkSpeedCapMps);
                if (string.IsNullOrEmpty(statusFeedback))
                {
                    statusFeedback = $"SOLUK SOLUĞA YORGUNLUK KILIDI! (Stamina %{_vitals.Stamina:F0} < %15 -> Ziplama/Depar Kilitli, Hiz 1.45 m/s)";
                }
            }

            if (Stage1GameSettings.EnableEnvironmentalEffects &&
                _vitals.CoreBodyTempCelsius < ThermalAndBiologySystem.HypothermiaThresholdCelsius)
            {
                speed *= 0.80f; // Hipotermi: -%20 hareket hizi cezasi
            }

            if (_vitals.HungerKcal <= 25.0f || _vitals.ThirstHydration <= 25.0f)
            {
                speed *= 0.85f; // Agir aclik veya susuzluk: -%15 hareket hizi cezasi
            }

            speed *= Stage1GameSettings.PlayerMoveSpeedMultiplier;
            _movementState.CurrentSpeedMetersPerSec = speed;
            Vector2 horiz = new Vector2(_movementState.VelocityX, _movementState.VelocityZ);
            if (horiz.sqrMagnitude > 0.0001f)
            {
                Vector2 dir = horiz.normalized;
                _movementState.VelocityX = dir.x * speed;
                _movementState.VelocityZ = dir.y * speed;
            }
        }

        private void StepFluidBuoyancyAndHazards(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            HashSet<(int cx, int cz)>? activeFluidChunks,
            float dt,
            ref string statusFeedback)
        {
            WaterSplashPulse01 = Mathf.Max(0f, WaterSplashPulse01 - dt * 1.65f);
            if (_swimmerSurgeCooldownSec > 0f)
            {
                _swimmerSurgeCooldownSec = Mathf.Max(0f, _swimmerSurgeCooldownSec - dt);
            }

            if (fluidChunks == null)
            {
                _vitals.SubmergedDepthMeters = 0f;
                _movementState.IsSubmergedInFluid = 0;
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, 0f, dt * 3.0f);
                _wasTouchingWaterLastFrame = false;
                return;
            }
            Vector3 pos = playerTransform.position;
            int wx = Mathf.FloorToInt(pos.x), wz = Mathf.FloorToInt(pos.z);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? f) || f == null)
            {
                _vitals.SubmergedDepthMeters = 0f;
                _movementState.IsSubmergedInFluid = 0;
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, 0f, dt * 3.0f);
                _wasTouchingWaterLastFrame = false;
                return;
            }

            int lx = wx - cx * 16, lz = wz - cz * 16;
            int feetWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 0.25f), 1, 254);
            int waistWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 0.85f), 1, 254);
            int headWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 1.45f), 1, 254);
            FluidCell16 feetCell = new FluidCell16(f[(feetWy << 8) | (lz << 4) | lx]);
            FluidCell16 waistCell = new FluidCell16(f[(waistWy << 8) | (lz << 4) | lx]);
            FluidCell16 headCell = new FluidCell16(f[(headWy << 8) | (lz << 4) | lx]);

            bool inLava = (feetCell.Type == FluidType.Lava && feetCell.Mass >= 36) || (waistCell.Type == FluidType.Lava && waistCell.Mass >= 24);
            bool inAcid = (feetCell.Type == FluidType.Acid && feetCell.Mass >= 36) || (waistCell.Type == FluidType.Acid && waistCell.Mass >= 24);
            bool inWater = waistCell.Type == FluidType.Water && waistCell.Mass >= 40;
            bool wadingWater = feetCell.Type == FluidType.Water && feetCell.Mass >= 14;
            bool touchingWater = inWater || wadingWater;
            bool headSubmerged = headCell.Type != FluidType.None && headCell.Mass >= 48;

            _movementState.IsSubmergedInFluid = (inWater || inLava || inAcid) ? (byte)1 : (byte)0;

            if (touchingWater)
            {
                int surfaceWy = inWater ? waistWy : feetWy;
                for (int sy = surfaceWy; sy <= Mathf.Min(253, surfaceWy + 6); sy++)
                {
                    FluidCell16 c = new FluidCell16(f[(sy << 8) | (lz << 4) | lx]);
                    if (c.Type == FluidType.Water && c.Mass > 0)
                    {
                        surfaceWy = sy;
                    }
                    else
                    {
                        break;
                    }
                }

                float horizSpd = Mathf.Sqrt(_movementState.VelocityX * _movementState.VelocityX + _movementState.VelocityZ * _movementState.VelocityZ);
                WaterInteractionWorldPos = new Vector3(pos.x, surfaceWy + 0.55f, pos.z);
                float targetStrength = Mathf.Clamp01((inWater ? 0.52f : 0.30f) + horizSpd * 0.14f + Mathf.Abs(_verticalVelocity) * 0.06f);
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, targetStrength, dt * 6.5f);

                if (!_wasTouchingWaterLastFrame)
                {
                    float impactSpeed = Mathf.Max(horizSpd * 0.5f, -_verticalVelocity);
                    WaterSplashPulse01 = Mathf.Clamp01(0.45f + impactSpeed * 0.11f);
                    if (inWater && _verticalVelocity < -3.0f)
                    {
                        // Suya yukaridan atlayinca su direnci dususu yumusatir
                        _verticalVelocity *= 0.45f;
                    }

                    int splashUnits = Mathf.Clamp(Mathf.RoundToInt(8f + impactSpeed * 2.2f), 8, 22);
                    ApplyMassConservingSwimmerSurge(
                        worldChunks, fluidChunks, dirtyFluidChunks, activeFluidChunks, wx, surfaceWy, wz, splashUnits);
                    _swimmerSurgeCooldownSec = 0.24f;
                }
                else if (horizSpd > 0.65f && _swimmerSurgeCooldownSec <= 0f)
                {
                    ApplyMassConservingSwimmerSurge(
                        worldChunks, fluidChunks, dirtyFluidChunks, activeFluidChunks, wx, surfaceWy, wz, maxPushUnits: 10);
                    _swimmerSurgeCooldownSec = 0.28f;
                }
            }
            else
            {
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, 0f, dt * 2.4f);
            }
            _wasTouchingWaterLastFrame = touchingWater;

            if (headSubmerged)
            {
                int waterColumnAbove = 0;
                int maxScanY = Mathf.Min(254, headWy + 60);
                for (int sy = headWy + 1; sy <= maxScanY; sy++)
                {
                    FluidCell16 above = new FluidCell16(f[(sy << 8) | (lz << 4) | lx]);
                    if (above.Type == FluidType.None || above.Mass < 32) break;
                    waterColumnAbove++;
                }
                float columnDepth = 1.5f + waterColumnAbove * 1.0f;
                float pressureHeadDepth = headCell.PressureHead * 2.0f;
                _vitals.SubmergedDepthMeters = Mathf.Max(columnDepth, pressureHeadDepth);
            }
            else
            {
                _vitals.SubmergedDepthMeters = 0f;
            }

            if (inLava)
            {
                _movementState.CurrentSpeedMetersPerSec *= 0.42f;
                if (Stage1GameSettings.EnableEnvironmentalEffects)
                {
                    _vitals.CurrentHp = Mathf.Max(20f, _vitals.CurrentHp - 18.0f * dt);
                    _vitals.CoreBodyTempCelsius = Mathf.Min(43.0f, _vitals.CoreBodyTempCelsius + 4.0f * dt);
                    statusFeedback = "VOLKANIK LAV YANIGI! (-18 HP/s) Hemen lav kalderasindan cikin!";
                }
            }
            else if (inAcid)
            {
                _movementState.CurrentSpeedMetersPerSec *= 0.52f;
                if (Stage1GameSettings.EnableEnvironmentalEffects)
                {
                    _vitals.CurrentHp = Mathf.Max(20f, _vitals.CurrentHp - 12.0f * dt);
                    statusFeedback = "KORROZYON ASIDI YANIGI! (-12 HP/s) Kimyasal asit havuzundan hemen cikin!";
                }
            }
            else if (inWater)
            {
                _movementState.Stance = CharacterStance.Swimming;
                _movementState.CurrentSpeedMetersPerSec *= 0.62f;
                _vitals.Wetness01 = 1.0f;

                float totalMassKg = BaseBodyMassKg + _movementState.EquippedMassKg;
                if (totalMassKg > 115.0f)
                {
                    // Agir zirh/yuk (>115kg toplam kutle) Arsimet kaldirma kuvvetini asar ve oyuncuyu dibe batirir!
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, -2.4f, dt * 6.5f);
                    statusFeedback = $"AGIR ZIRH BATISI! (Toplam Kutle={totalMassKg:F0}kg > 115kg -> Suya Batiyorsun! [X] ile Agir Silahi Birak!)";
                }
                else if (Input.GetKey(KeyCode.Space))
                {
                    _verticalVelocity = Mathf.Max(_verticalVelocity, 3.6f);
                    _jumpLaunchTimer = 0.12f;
                    _movementState.IsGrounded = 0;
                    statusFeedback = "Suda Yuzme (Arsimet Kaldirma Kuvveti + Space)";
                }
            }
            else
            {
                _vitals.Wetness01 = Mathf.Max(0f, _vitals.Wetness01 - 0.08f * dt);
            }
        }

        private void ApplyJumpOrMantleVerticalImpulse(
            Transform playerTransform, Vector3 forwardFlat, bool jumpOrMantlePressed, bool canJumpFromGround,
            bool hasLedgeAhead, float ledgeTopY, ref string statusFeedback)
        {
            if (!jumpOrMantlePressed) return;
            if (_movementState.IsExhaustedStaminaLock != 0)
            {
                statusFeedback = "YORGUNLUK KILIDI (Stamina < %15): Ziplamak veya tirmanmak icin nefeslenin!";
                return;
            }

            float drainMult = Stage1GameSettings.StaminaDrainMultiplier;
            float mantleCost = 15.0f * drainMult;
            if (hasLedgeAhead && !_isMantling && _vitals.Stamina >= mantleCost)
            {
                _vitals.Stamina = MathF.Max(0f, _vitals.Stamina - mantleCost);
                if (_vitals.Stamina <= 0.01f && drainMult > 0.01f) _movementState.IsExhaustedStaminaLock = 1;

                Vector3 pos = playerTransform.position;
                Vector3 targetPos = pos + forwardFlat * 0.68f;
                targetPos.y = Mathf.Max(pos.y + 0.45f, Mathf.Min(ledgeTopY + 0.04f, pos.y + 2.25f));

                _isMantling = true;
                _mantleElapsedSeconds = 0f;
                _movementState.MantleProgress01 = 0f;
                _movementState.Stance = CharacterStance.Climbing;
                _movementState.IsGrounded = 0;
                _coyoteGroundedTimer = 0f;
                _verticalVelocity = 0f;
                _mantleStartPos = new System.Numerics.Vector3(pos.x, pos.y, pos.z);
                _mantleTargetPos = new System.Numerics.Vector3(targetPos.x, targetPos.y, targetPos.z);
                statusFeedback = "0.55s Kubik Bezier Mantle Tirmanisi (-15 EP)";
                return;
            }

            float jumpCost = 6.0f * drainMult;
            if (canJumpFromGround && _vitals.Stamina >= jumpCost)
            {
                _vitals.Stamina = MathF.Max(0f, _vitals.Stamina - jumpCost);
                if (_vitals.Stamina <= 0.01f && drainMult > 0.01f) _movementState.IsExhaustedStaminaLock = 1;
                _verticalVelocity = 6.45f * Stage1GameSettings.PlayerJumpMultiplier;
                _jumpLaunchTimer = 0.20f;
                _coyoteGroundedTimer = 0f;
                _movementState.IsGrounded = 0;
                statusFeedback = "Ziplama (-6 EP)";
            }
        }

        private void IntegrateHorizontalAndVerticalMotion(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 wishDir, float deltaTime)
        {
            Vector3 pos = playerTransform.position;
            Vector3 horizVel = new Vector3(_movementState.VelocityX, 0f, _movementState.VelocityZ);
            if (_movementState.Stance == CharacterStance.Sliding && horizVel.sqrMagnitude < 0.01f)
            {
                horizVel = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized * _movementState.CurrentSpeedMetersPerSec;
            }
            else if (wishDir.sqrMagnitude > 0.001f)
            {
                horizVel = wishDir * _movementState.CurrentSpeedMetersPerSec;
            }

            Vector3 stepDelta = horizVel * deltaTime;
            Vector3 candidatePos = new Vector3(pos.x + stepDelta.x, pos.y, pos.z + stepDelta.z);
            if (!IsHorizontalStepBlocked(worldChunks, pos, candidatePos, _movementState.CapsuleHeightMeters))
            {
                pos.x = candidatePos.x;
                pos.z = candidatePos.z;
            }
            else
            {
                // Capraz duvar/agac temasinda oyuncunun tamamen takilip durmasini onlemek icin X ve Z eksenlerinde ayri kayma (Wall-Slide)
                Vector3 candidateX = new Vector3(pos.x + stepDelta.x, pos.y, pos.z);
                if (Mathf.Abs(stepDelta.x) > 0.0001f &&
                    !IsHorizontalStepBlocked(worldChunks, pos, candidateX, _movementState.CapsuleHeightMeters))
                {
                    pos.x = candidateX.x;
                }

                Vector3 candidateZ = new Vector3(pos.x, pos.y, pos.z + stepDelta.z);
                if (Mathf.Abs(stepDelta.z) > 0.0001f &&
                    !IsHorizontalStepBlocked(worldChunks, pos, candidateZ, _movementState.CapsuleHeightMeters))
                {
                    pos.z = candidateZ.z;
                }
            }

            float nextGroundY = SampleSupportGroundHeight(worldChunks, pos);
            bool stickToSlope = _jumpLaunchTimer <= 0.001f &&
                                (_movementState.IsGrounded != 0 || _coyoteGroundedTimer > 0.01f) &&
                                _verticalVelocity <= 0.10f &&
                                (nextGroundY - pos.y) <= 1.45f &&
                                (pos.y - nextGroundY) <= 1.15f;
            if (stickToSlope)
            {
                float diff = nextGroundY - pos.y;
                pos.y = Mathf.Abs(diff) < 0.05f ? nextGroundY : Mathf.MoveTowards(pos.y, nextGroundY, deltaTime * 24f);
                _verticalVelocity = 0f;
                _movementState.IsGrounded = 1;
                _coyoteGroundedTimer = 0.16f;
            }
            else
            {
                _verticalVelocity -= StratocraftConstants.GravityAcceleration * deltaTime;
                pos.y += _verticalVelocity * deltaTime;
                if (_jumpLaunchTimer <= 0.001f && pos.y <= nextGroundY)
                {
                    pos.y = nextGroundY;
                    _verticalVelocity = 0f;
                    _movementState.IsGrounded = 1;
                    _coyoteGroundedTimer = 0.16f;
                }
            }
            playerTransform.position = pos;
        }

        /// <summary>
        /// Kılıcın ağırlığına göre savurma veya ağır siper taşıma sırasında oluşan yatay eylemsizlik çekişini
        /// voksel duvar ve zemin çarpışması korumalı olarak oyuncu gövdesine uygular.
        /// </summary>
        public void ApplyWeaponMomentumHorizontalStep(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 worldHorizontalDelta,
            float deltaTime)
        {
            if (playerTransform == null) return;
            worldHorizontalDelta.y = 0f;
            if (worldHorizontalDelta.sqrMagnitude < 0.000001f) return;

            Vector3 pos = playerTransform.position;
            Vector3 candidatePos = pos + worldHorizontalDelta;
            if (!IsHorizontalStepBlocked(worldChunks, pos, candidatePos, _movementState.CapsuleHeightMeters))
            {
                float nextGroundY = SampleSupportGroundHeight(worldChunks, candidatePos);
                if (nextGroundY - pos.y > 1.25f)
                {
                    return;
                }
                pos.x = candidatePos.x;
                pos.z = candidatePos.z;
                if (_jumpLaunchTimer <= 0.001f && _movementState.IsGrounded != 0 && _verticalVelocity <= 0.05f)
                {
                    float diff = nextGroundY - pos.y;
                    pos.y = diff > 0f ? nextGroundY : (Mathf.Abs(diff) < 0.05f ? nextGroundY : Mathf.MoveTowards(pos.y, nextGroundY, deltaTime * 24f));
                }
                playerTransform.position = pos;
                ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
            }
        }

        /// <summary>Oyuncu kapsulunun kati voksel icine girmesini tespit edip guvenli ust yuzeye cikarir (egimli arazide asla havaya ziplatmaz!).</summary>
        public void ResolveVoxelAndColliderDepenetration(Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (_jumpLaunchTimer > 0.001f) return;
            Vector3 pos = playerTransform.position;
            float supportY = SampleSupportGroundHeight(worldChunks, pos);
            if (pos.y < supportY - 0.005f)
            {
                float burialDepth = supportY - pos.y;
                pos.y = burialDepth <= 0.15f || burialDepth > 1.35f
                    ? supportY
                    : Mathf.MoveTowards(pos.y, supportY, 0.35f);
                if (_verticalVelocity < 0f) _verticalVelocity = 0f;
                _movementState.IsGrounded = 1;
                _coyoteGroundedTimer = 0.16f;
                playerTransform.position = pos;
            }
        }

        private static bool IsHorizontalStepBlocked(
            Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 cur, Vector3 next, float capsuleHeightMeters = 1.8f)
        {
            Vector2 step2D = new Vector2(next.x - cur.x, next.z - cur.z);
            if (step2D.sqrMagnitude < 1e-7f) return false;
            Vector2 dir2D = step2D.normalized;

            int curWx = Mathf.FloorToInt(cur.x), curWz = Mathf.FloorToInt(cur.z);
            int probeWx = Mathf.FloorToInt(next.x + dir2D.x * 0.28f);
            int probeWz = Mathf.FloorToInt(next.z + dir2D.y * 0.28f);

            // Oyuncunun halihazirda icinde bulundugu voksel kolonunda hareketi ASLA kilitleme (sikismayi %100 onler)
            if (probeWx == curWx && probeWz == curWz) return false;

            float topOffset = Mathf.Clamp(capsuleHeightMeters - 0.15f, 0.58f, 1.65f);
            float waistOffset = Mathf.Clamp(topOffset * 0.78f, 0.52f, 1.28f);
            int waistY = Mathf.FloorToInt(cur.y + waistOffset);
            int headY = Mathf.FloorToInt(cur.y + topOffset);

            // <= 1.25m egimler ve basamaklar bel/bas hizasinin (waistY / headY) altinda kaldigi icin puruzsuz cikilir;
            // >= 1.30m dik duvarlar, agac govdeleri ve alcak kirisler bel/bas hizasinda kati voksel barindirir ve engellenir.
            return IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, probeWx, waistY, probeWz)) ||
                   IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, probeWx, headY, probeWz));
        }

        private static bool DetectClimbableLedgeAhead(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, out float ledgeTopY)
        {
            Vector3 probe = playerTransform.position + Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized * 0.85f;
            int px = Mathf.FloorToInt(probe.x), pz = Mathf.FloorToInt(probe.z), baseWy = Mathf.FloorToInt(playerTransform.position.y + 0.5f);
            for (int y = baseWy + 2; y >= baseWy; y--)
            {
                if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, px, y, pz)) &&
                    !IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, px, y + 1, pz)) &&
                    !IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, px, y + 2, pz)))
                {
                    ledgeTopY = y + 1.0f;
                    return true;
                }
            }
            ledgeTopY = playerTransform.position.y;
            return false;
        }

        private static float SampleSingleColumnGroundY(
            Dictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wz, int startY)
        {
            for (int y = startY; y >= 1; y--)
            {
                uint raw = ReadWorldVoxelRaw(worldChunks, wx, y, wz);
                if (!IsHardCollisionVoxel(raw)) continue;
                return VoxelBitUtility.GetShapeId(raw) == 0
                    ? y + Mathf.Clamp(VoxelBitUtility.GetDensityOrLiquid(raw) / 127f, 0.35f, 0.92f)
                    : y + 1.0f;
            }
            return -30f;
        }

        private static float SampleBilinearVoxelGroundHeight(
            Dictionary<(int cx, int cz), uint[]> worldChunks, float worldX, float worldZ, float refY)
        {
            int startY = Mathf.Clamp(Mathf.FloorToInt(refY + 1.35f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2);
            int centerWx = Mathf.FloorToInt(worldX), centerWz = Mathf.FloorToInt(worldZ);
            float centerH = SampleSingleColumnGroundY(worldChunks, centerWx, centerWz, startY);
            if (centerH <= -20f) return centerH;

            int x0 = Mathf.FloorToInt(worldX - 0.5f);
            int z0 = Mathf.FloorToInt(worldZ - 0.5f);
            float tx = Mathf.Clamp01(worldX - 0.5f - x0);
            float tz = Mathf.Clamp01(worldZ - 0.5f - z0);

            float h00 = SampleSingleColumnGroundY(worldChunks, x0, z0, startY);
            float h10 = SampleSingleColumnGroundY(worldChunks, x0 + 1, z0, startY);
            float h01 = SampleSingleColumnGroundY(worldChunks, x0, z0 + 1, startY);
            float h11 = SampleSingleColumnGroundY(worldChunks, x0 + 1, z0 + 1, startY);

            // Ucurum kenarlarinda (>1.5m fark) komsu bosluga dusmemek icin merkez sutun yuksekligini koru;
            // normal egimli arazide (<=1.5m fark) 4 kose arasinda C0 surekli bilineer gecis uygula!
            if (Mathf.Abs(h00 - centerH) > 1.5f) h00 = centerH;
            if (Mathf.Abs(h10 - centerH) > 1.5f) h10 = centerH;
            if (Mathf.Abs(h01 - centerH) > 1.5f) h01 = centerH;
            if (Mathf.Abs(h11 - centerH) > 1.5f) h11 = centerH;

            float hx0 = Mathf.Lerp(h00, h10, tx);
            float hx1 = Mathf.Lerp(h01, h11, tx);
            return Mathf.Lerp(hx0, hx1, tz);
        }

        private static float SampleSupportGroundHeight(Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 pos)
        {
            float smoothVoxelY = SampleBilinearVoxelGroundHeight(worldChunks, pos.x, pos.z, pos.y);
            if (smoothVoxelY > -20f)
            {
                return smoothVoxelY;
            }

            int terrainMask = LayerMask.GetMask("VoxelTerrain");
            int castMask = terrainMask != 0 ? terrainMask : UnityEngine.Physics.DefaultRaycastLayers;

            if (UnityEngine.Physics.Raycast(new Vector3(pos.x, pos.y + 0.95f, pos.z), Vector3.down,
                out RaycastHit hit, 16f, castMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }
            return smoothVoxelY;
        }

        private void RescueFromVoid(Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            int sx = Mathf.FloorToInt(_safeSpawnPosition.x), sz = Mathf.FloorToInt(_safeSpawnPosition.z);
            float rescueY = _safeSpawnPosition.y;
            for (int y = Stage1InteractivePlayController.MaxEditableVoxelY - 2; y >= 1; y--)
            {
                if (VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, sx, y, sz))) { rescueY = y + 1.05f; break; }
            }
            playerTransform.position = new Vector3(_safeSpawnPosition.x, rescueY, _safeSpawnPosition.z);
            _verticalVelocity = 0f;
            _movementState.IsGrounded = 1;
        }
#endif
    }
}
