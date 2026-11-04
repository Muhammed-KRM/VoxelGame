#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
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

#if UNITY_5_3_OR_NEWER
        private const float VoidRescueThresholdY = -14.0f;
        private const float MaxStepUpHeightMeters = 0.65f;

        private readonly PhysicalCharacterControllerSystem _controllerSystem = new();
        private CharacterMovementState _movementState = CharacterMovementState.CreateDefault();
        private CharacterVitalsComponent _vitals = CharacterVitalsComponent.CreateDefault();
        private float _verticalVelocity, _coyoteGroundedTimer = 0.15f, _jumpLaunchTimer;
        private Vector3 _safeSpawnPosition = new Vector3(-1.4f, 11.5f, -5.2f);

        /// <summary>Anlik fiziksel karakter hareket durumu (Stance, Hiz, Kapsul Boyutu, Stamina Kilidi).</summary>
        public ref CharacterMovementState MovementState => ref _movementState;
        /// <summary>Anlik biyolojik hayatta kalma gostergeleri (HP, Stamina, Oksijen, Sicaklik).</summary>
        public ref CharacterVitalsComponent Vitals => ref _vitals;
        /// <summary>Dusey yercekimi / ziplama hizi (m/s).</summary>
        public float VerticalVelocity { get => _verticalVelocity; set => _verticalVelocity = value; }
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
        }

        /// <summary>WASD, kosu, kayma, surunme, 2.2m mantle, ziplama, sivi kaldirma/lav hasari ve kapsul-arazi depenetration cozumunu ilerletir.</summary>
        public void StepCharacterPhysics(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, Vector2 moveInput,
            bool wantSprint, bool wantCrouch, bool wantProne, bool jumpOrMantlePressed, bool isBackpackOpen,
            float equippedMassKg, float deltaTime, out string statusFeedback,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks = null)
        {
            statusFeedback = string.Empty;
            _movementState.EquippedMassKg = equippedMassKg;
            _movementState.IsBackpackOverlayOpen = isBackpackOpen ? (byte)1 : (byte)0;

            Vector3 pos = playerTransform.position;
            if (pos.y < VoidRescueThresholdY)
            {
                RescueFromVoid(playerTransform, worldChunks);
                statusFeedback = "Guvenlik Kurtarmasi: Oyuncu ada yuzeyine geri tasindi";
                return;
            }
            if (_jumpLaunchTimer > 0f) _jumpLaunchTimer = Mathf.Max(0f, _jumpLaunchTimer - deltaTime);

            float groundY = SampleSupportGroundHeight(worldChunks, pos);
            bool grounded = _jumpLaunchTimer <= 0.001f && pos.y <= groundY + 0.22f && _verticalVelocity <= 0.25f;
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

            CharacterStance reqStance = _movementState.Stance == CharacterStance.Sliding
                ? CharacterStance.Sliding
                : (wantProne ? CharacterStance.ProneCrawling : (wantCrouch ? CharacterStance.Crouching : (wantSprint ? CharacterStance.Sprinting : CharacterStance.Standing)));
            int headroomBlocks = VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y + 1.55f), Mathf.FloorToInt(pos.z))) ? 1 : 2;
            _controllerSystem.StepMovement(ref _movementState, ref _vitals, reqStance, (wishDir.x, wishDir.z), headroomBlocks, deltaTime);
            if (!prevSliding && _movementState.Stance == CharacterStance.Sliding)
                statusFeedback = "Kayma (Slide) Basladi (-15 EP -> 3.58m)";

            ApplyJumpOrMantleVerticalImpulse(playerTransform, jumpOrMantlePressed, grounded || _coyoteGroundedTimer > 0f, hasLedgeAhead, ledgeTopY, ref statusFeedback);
            StepFluidBuoyancyAndHazards(playerTransform, fluidChunks, deltaTime, ref statusFeedback);
            IntegrateHorizontalAndVerticalMotion(playerTransform, worldChunks, wishDir, deltaTime);
            ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
        }

        private void StepFluidBuoyancyAndHazards(
            Transform playerTransform, Dictionary<(int cx, int cz), ushort[]>? fluidChunks, float dt, ref string statusFeedback)
        {
            if (fluidChunks == null) { _vitals.SubmergedDepthMeters = 0f; return; }
            Vector3 pos = playerTransform.position;
            int wx = Mathf.FloorToInt(pos.x), wz = Mathf.FloorToInt(pos.z);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? f) || f == null) { _vitals.SubmergedDepthMeters = 0f; return; }

            int lx = wx - cx * 16, lz = wz - cz * 16;
            int feetWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 0.25f), 1, 28);
            int waistWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 0.85f), 1, 28);
            int headWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 1.45f), 1, 28);
            FluidCell16 feetCell = new FluidCell16(f[(feetWy << 8) | (lz << 4) | lx]);
            FluidCell16 waistCell = new FluidCell16(f[(waistWy << 8) | (lz << 4) | lx]);
            FluidCell16 headCell = new FluidCell16(f[(headWy << 8) | (lz << 4) | lx]);

            bool inLava = (feetCell.Type == FluidType.Lava && feetCell.Mass >= 24) || (waistCell.Type == FluidType.Lava && waistCell.Mass >= 24);
            bool inWater = (feetCell.Type == FluidType.Water && feetCell.Mass >= 24) || (waistCell.Type == FluidType.Water && waistCell.Mass >= 24);
            bool headSubmerged = headCell.Type != FluidType.None && headCell.Mass >= 64;
            _vitals.SubmergedDepthMeters = headSubmerged ? 1.5f : 0f;

            if (inLava)
            {
                _movementState.CurrentSpeedMetersPerSec *= 0.42f;
                _vitals.CurrentHp = Mathf.Max(1f, _vitals.CurrentHp - 18.0f * dt);
                _vitals.CoreBodyTempCelsius = Mathf.Min(43.0f, _vitals.CoreBodyTempCelsius + 4.0f * dt);
                statusFeedback = "VOLKANIK LAV YANIGI! (-18 HP/s) Hemen lav kalderasindan cikin!";
            }
            else if (inWater)
            {
                _movementState.CurrentSpeedMetersPerSec *= 0.62f;
                _vitals.Wetness01 = 1.0f;
                if (Input.GetKey(KeyCode.Space))
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
            Transform playerTransform, bool jumpOrMantlePressed, bool canJumpFromGround,
            bool hasLedgeAhead, float ledgeTopY, ref string statusFeedback)
        {
            if (!jumpOrMantlePressed) return;
            if (hasLedgeAhead && _movementState.IsExhaustedStaminaLock == 0 && _vitals.Stamina >= 15.0f)
            {
                _vitals.Stamina = MathF.Max(0f, _vitals.Stamina - 15.0f);
                _verticalVelocity = 5.6f; _jumpLaunchTimer = 0.20f; _coyoteGroundedTimer = 0f; _movementState.IsGrounded = 0;
                Vector3 pos = playerTransform.position;
                pos.y = Mathf.Max(pos.y + 0.35f, Mathf.Min(ledgeTopY + 0.05f, pos.y + 1.45f));
                playerTransform.position = pos;
                statusFeedback = "2.2m Kubik Bezier Mantle Tirmanisi (-15 EP)";
                return;
            }
            if (canJumpFromGround)
            {
                _vitals.Stamina = MathF.Max(0f, _vitals.Stamina - 6.0f);
                _verticalVelocity = 6.45f; _jumpLaunchTimer = 0.20f; _coyoteGroundedTimer = 0f; _movementState.IsGrounded = 0;
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

            Vector3 candidatePos = pos + horizVel * deltaTime;
            if (!IsHorizontalStepBlocked(worldChunks, pos, candidatePos))
            {
                pos.x = candidatePos.x;
                pos.z = candidatePos.z;
            }

            float nextGroundY = SampleSupportGroundHeight(worldChunks, pos);
            if (_jumpLaunchTimer <= 0.001f && _movementState.IsGrounded != 0 && _verticalVelocity <= 0.05f)
            {
                float diff = nextGroundY - pos.y;
                pos.y = Mathf.Abs(diff) < 0.025f ? nextGroundY : Mathf.MoveTowards(pos.y, nextGroundY, deltaTime * 14f);
                _verticalVelocity = 0f;
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
            if (!IsHorizontalStepBlocked(worldChunks, pos, candidatePos))
            {
                pos.x = candidatePos.x;
                pos.z = candidatePos.z;
                if (_jumpLaunchTimer <= 0.001f && _movementState.IsGrounded != 0 && _verticalVelocity <= 0.05f)
                {
                    float nextGroundY = SampleSupportGroundHeight(worldChunks, pos);
                    float diff = nextGroundY - pos.y;
                    pos.y = Mathf.Abs(diff) < 0.03f ? nextGroundY : Mathf.MoveTowards(pos.y, nextGroundY, deltaTime * 14f);
                }
                playerTransform.position = pos;
            }
        }

        /// <summary>Oyuncu kapsulunun kati voksel icine girmesini tespit edip guvenli ust yuzeye cikarir (ziplama titremesi yapmaz).</summary>
        public void ResolveVoxelAndColliderDepenetration(Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            Vector3 pos = playerTransform.position;
            int wx = Mathf.FloorToInt(pos.x), wz = Mathf.FloorToInt(pos.z), waistWy = Mathf.FloorToInt(pos.y + 0.85f);
            if (VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, wx, waistWy, wz)))
            {
                int safeY = waistWy + 1;
                while (safeY < Stage1InteractivePlayController.MaxEditableVoxelY - 2 &&
                       VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, wx, safeY, wz))) safeY++;
                pos.y = safeY + 0.02f;
                if (_verticalVelocity < 0f) _verticalVelocity = 0f;
                playerTransform.position = pos;
                return;
            }
            if (_jumpLaunchTimer <= 0.001f)
            {
                float supportY = SampleSupportGroundHeight(worldChunks, pos);
                if (pos.y < supportY - 0.04f)
                {
                    pos.y = supportY;
                    if (_verticalVelocity < 0f) _verticalVelocity = 0f;
                    _movementState.IsGrounded = 1;
                    playerTransform.position = pos;
                }
            }
        }

        private static bool IsHorizontalStepBlocked(Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 cur, Vector3 next)
        {
            int nx = Mathf.FloorToInt(next.x), nz = Mathf.FloorToInt(next.z);
            int chestY = Mathf.FloorToInt(cur.y + 0.95f), headY = Mathf.FloorToInt(cur.y + 1.55f);
            return VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, nx, chestY, nz)) ||
                   VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, nx, headY, nz));
        }

        private static bool DetectClimbableLedgeAhead(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, out float ledgeTopY)
        {
            Vector3 probe = playerTransform.position + Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized * 0.85f;
            int px = Mathf.FloorToInt(probe.x), pz = Mathf.FloorToInt(probe.z), baseWy = Mathf.FloorToInt(playerTransform.position.y + 0.5f);
            for (int y = baseWy + 2; y >= baseWy; y--)
            {
                if (VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, px, y, pz)) &&
                    !VoxelBitUtility.IsPhysicallySolid(ReadWorldVoxelRaw(worldChunks, px, y + 1, pz)))
                {
                    ledgeTopY = y + 1.0f;
                    return true;
                }
            }
            ledgeTopY = playerTransform.position.y;
            return false;
        }

        private static float SampleSupportGroundHeight(Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 pos)
        {
            int wx = Mathf.FloorToInt(pos.x), wz = Mathf.FloorToInt(pos.z);
            int startY = Mathf.Clamp(Mathf.FloorToInt(pos.y + MaxStepUpHeightMeters), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 2);
            float voxelTopY = -30f;
            for (int y = startY; y >= 1; y--)
            {
                uint raw = ReadWorldVoxelRaw(worldChunks, wx, y, wz);
                if (!VoxelBitUtility.IsPhysicallySolid(raw)) continue;
                voxelTopY = VoxelBitUtility.GetShapeId(raw) == 0
                    ? y + Mathf.Clamp(VoxelBitUtility.GetDensityOrLiquid(raw) / 127f, 0.35f, 0.92f)
                    : y + 1.0f;
                break;
            }
            if (UnityEngine.Physics.Raycast(new Vector3(pos.x, pos.y + MaxStepUpHeightMeters, pos.z), Vector3.down,
                out RaycastHit hit, 28f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return voxelTopY > -20f ? Mathf.Max(hit.point.y, voxelTopY - 0.38f) : hit.point.y;
            }
            return voxelTopY;
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
