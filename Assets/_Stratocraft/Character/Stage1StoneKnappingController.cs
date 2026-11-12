#if UNITY_5_3_OR_NEWER
#nullable enable
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 2.2 (Aşama 2.2) — Yerdeki Ham Taş (<see cref="InventoryItemKind.RoughStoneChunk"/>) Üzerinde
    /// Çakmaktaşı (<see cref="InventoryItemKind.FlintStone"/>) ile Fiziksel 3B Taş Yontma Kontrolcüsü.
    /// </summary>
    public sealed class Stage1StoneKnappingController
    {
        public enum KnappingSessionPhase : byte
        {
            Idle = 0,
            RadialTemplateMenu = 1,
            CrouchAndOutlineAnim = 2,
            ActiveChipping = 3,
            CameraExitAnim = 4
        }

        public enum GroundStoneTargetSource : byte
        {
            None = 0,
            MiningWorldDrop = 1,
            HolsterPlayerDrop = 2,
            WorldStoneVoxel = 3
        }

        private struct FlyingStoneChip
        {
            public GameObject? Go;
            public Vector3 Pos;
            public Vector3 Vel;
            public Vector3 AngularVelDeg;
            public float Age;
            public float MaxLife;
        }

        private const float CellSizeMeters = PhysicalItemMeshFactory.KnappingSlabCellSizeMeters; // 0.052m
        private const float SlabWidthMeters = CellSizeMeters * 8f; // 0.416m
        private const float SlabTopSurfaceY = 0.046f;
        private const float CrouchOutlineDurationSec = 0.92f;
        private const float CameraExitDurationSec = 0.26f;

        private readonly List<FlyingStoneChip> _flyingChips = new(32);
        private StoneKnappingState _session;
        private GroundStoneTargetSource _targetSource = GroundStoneTargetSource.None;
        private int _groundStoneIndex = -1;
        private Vector3Int _groundStoneVoxelCoord;
        private Vector3 _groundStoneWorldPos;
        private Quaternion _slabWorldRot = Quaternion.identity;
        private byte _groundStoneMatId = MaterialIdCatalog.Stone;
        private PhysicalItemShapeDna _groundStoneDna;
        private Dictionary<(int cx, int cz), uint[]>? _lastWorldChunks;
        private HashSet<(int cx, int cz)>? _lastDirtyChunks;
        private float _toastTimer;
        private string _toastMessage = string.Empty;

        private Vector3 _savedCamPos;
        private Quaternion _savedCamRot = Quaternion.identity;
        private Vector3 _targetCamPos;
        private Quaternion _targetCamRot = Quaternion.identity;

        private GameObject? _slabRootGo;
        private MeshFilter? _slabMf;
        private MeshRenderer? _slabMr;
        private Mesh? _dynamicSlabMesh;

        private GameObject? _strikerFlintGo;
        private float _phaseTimer;
        private float _strikeAnimTimer;
        private Vector3 _strikeTargetWorldPos;
        private int _hoveredCellX = -1;
        private int _hoveredCellZ = -1;
        private int _lastBuiltHoverX = -99;
        private int _lastBuiltHoverZ = -99;
        private ulong _lastBuiltMask64;
        private float _lastBuiltOutlineProgress = -1f;
        private int _flintStrikeWearCounter;
        private float _pendingStaminaDrain;
        private float _shatterPauseTimer;

        private GUIStyle? _panelStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _bodyStyle;
        private GUIStyle? _buttonStyle;
        private GUIStyle? _accentBtnStyle;

        public KnappingSessionPhase Phase { get; private set; } = KnappingSessionPhase.Idle;
        public bool IsBusy => Phase != KnappingSessionPhase.Idle;
        public bool ForcePlayerCrouch => Phase == KnappingSessionPhase.CrouchAndOutlineAnim || Phase == KnappingSessionPhase.ActiveChipping;
        public bool HasAimedGroundStone { get; private set; }
        public bool HasVisibleToast => _toastTimer > 0.001f && !string.IsNullOrEmpty(_toastMessage);
        public string StatusBannerText { get; private set; } = string.Empty;

        public float ConsumePendingStaminaDrain()
        {
            float v = _pendingStaminaDrain;
            _pendingStaminaDrain = 0f;
            return v;
        }

        private void ShowToast(string message, Stage1MatterCrystalAndMiningController miningCtrl, float durationSec = 3.4f)
        {
            _toastMessage = message;
            _toastTimer = durationSec;
            miningCtrl.LastFeedbackText = message;
        }

        public static bool TryResolveBestAimedKnappableStone(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            out GroundStoneTargetSource bestSource,
            out int bestIndex,
            out Vector3Int bestVoxelCoord,
            out Vector3 bestWorldPos,
            out byte bestMatId,
            out PhysicalItemShapeDna bestDna)
        {
            bestSource = GroundStoneTargetSource.None;
            bestIndex = -1;
            bestVoxelCoord = default;
            bestWorldPos = Vector3.zero;
            bestMatId = MaterialIdCatalog.Stone;
            bestDna = default;

            float bestScore = float.MaxValue;

            // 1) Doga / Maden havuzundaki yer taslari (RoughStoneChunk veya Stone/Cobblestone/Basalt VoxelBlock)
            if (miningCtrl.TryFindAimedGroundStone(cam, playerPos, 3.35f, out int mIdx, out float mScore) &&
                miningCtrl.TryGetDroppedWorldItemInfo(mIdx, out Vector3 mPos, out _, out byte mMat, out PhysicalItemShapeDna mDna, out _))
            {
                bestSource = GroundStoneTargetSource.MiningWorldDrop;
                bestIndex = mIdx;
                bestWorldPos = mPos;
                bestMatId = mMat != 0 ? mMat : MaterialIdCatalog.Stone;
                bestDna = mDna;
                bestScore = mScore;
            }

            // 2) Oyuncunun [Q] / [I] ile yere biraktigi taslar (Stage1HolsterAndBackpackController._droppedWorldItems)
            if (holsterCtrl.TryFindAimedPlayerDroppedStone(cam, playerPos, 3.35f, out int hIdx, out float hScore) &&
                hScore < bestScore &&
                holsterCtrl.TryGetPlayerDroppedStoneInfo(hIdx, out Vector3 hPos, out byte hMat, out PhysicalItemShapeDna hDna))
            {
                bestSource = GroundStoneTargetSource.HolsterPlayerDrop;
                bestIndex = hIdx;
                bestWorldPos = hPos;
                bestMatId = hMat != 0 ? hMat : MaterialIdCatalog.Stone;
                bestDna = hDna;
                bestScore = hScore;
            }

            // 3) Yere [Sag Tik] ile konmus tas vokseli veya oyuncunun onundeki acik kaya blogu (<= 3.15m)
            if (bestSource == GroundStoneTargetSource.None &&
                miningCtrl.HasAimHit &&
                miningCtrl.AimHit.distance <= 3.15f &&
                Stage1MatterCrystalAndMiningController.IsKnappableStoneVoxelMaterial(miningCtrl.AimMaterialId))
            {
                bestSource = GroundStoneTargetSource.WorldStoneVoxel;
                bestIndex = 0;
                bestVoxelCoord = miningCtrl.AimVoxelCoord;
                bestWorldPos = miningCtrl.AimHit.point + Vector3.up * 0.03f;
                bestMatId = miningCtrl.AimMaterialId;
                bestDna = PhysicalItemShapeDna.CreateFromWorldCoord(
                    bestVoxelCoord.x, bestVoxelCoord.y, bestVoxelCoord.z, 0x510E11u);
            }

            return bestSource != GroundStoneTargetSource.None;
        }

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform? playerRoot,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Dictionary<(int cx, int cz), uint[]>? worldChunks = null,
            HashSet<(int cx, int cz)>? dirtyChunks = null)
        {
            if (worldChunks != null) _lastWorldChunks = worldChunks;
            if (dirtyChunks != null) _lastDirtyChunks = dirtyChunks;
            if (_toastTimer > 0f) _toastTimer = Mathf.Max(0f, _toastTimer - dt);

            StepFlyingChips(dt);

            if (cam == null || playerRoot == null)
            {
                if (IsBusy) AbortAndRestoreImmediate(cam, miningCtrl, holsterCtrl);
                HasAimedGroundStone = false;
                return;
            }

            if (Phase == KnappingSessionPhase.Idle)
            {
                if (miningCtrl.IsCrystalActive || holsterCtrl.IsBackpackOpen)
                {
                    HasAimedGroundStone = false;
                    return;
                }

                VisualInventorySlot activeHand = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                bool holdingFlint = activeHand.Kind == InventoryItemKind.FlintStone;
                bool ownsFlint = holdingFlint || holsterCtrl.OwnsAnyFlintStone();

                bool aimedAtStone = TryResolveBestAimedKnappableStone(
                    cam,
                    playerRoot.position,
                    miningCtrl,
                    holsterCtrl,
                    out GroundStoneTargetSource src,
                    out int stoneIdx,
                    out Vector3Int stoneVox,
                    out Vector3 stonePos,
                    out byte stoneMat,
                    out PhysicalItemShapeDna stoneDna);

                HasAimedGroundStone = aimedAtStone && (src != GroundStoneTargetSource.WorldStoneVoxel || holdingFlint);

                if (Input.GetKeyDown(KeyCode.R))
                {
                    if (aimedAtStone)
                    {
                        if (holsterCtrl.TryAutoEquipFlintForKnapping(miningCtrl, combatBridge))
                        {
                            _toastTimer = 0f;
                            OpenTemplateSelector(cam, playerRoot, src, stoneIdx, stoneVox, stonePos, stoneMat, stoneDna);
                        }
                        else
                        {
                            ShowToast(
                                "⚠️ TAŞ YONTMAK İÇİN ÇAKMAKTAŞI GEREKLİ!\nÖnce yerden koyu gri sivri Çakmaktaşı ([E]) toplayın, sonra bu taşa bakıp [R] tuşuna basın.",
                                miningCtrl);
                        }
                    }
                    else if (Stage1MatterCrystalAndMiningController.IsKnappableStoneItem(activeHand.Kind, activeHand.SubId))
                    {
                        ShowToast(
                            "🪨 TAŞ ŞU AN ELİNİZDE!\nÖnce taşı [Q] ile önünüzdeki yere bırakın, ardından yerdeki taşa bakarak [R] tuşuna basın!",
                            miningCtrl);
                    }
                    else if (holdingFlint)
                    {
                        ShowToast(
                            "🪨 TAŞ YONTMA [R]: Önünüzdeki yerdeki bir Kaba Taşa veya Kayaya yakından (<=3.2m) doğrudan bakıp [R] tuşuna basın!",
                            miningCtrl);
                    }
                }
                return;
            }

            // Eğer çanta açıldıysa veya eldeki çakmaktaşı kaybolduysa güvenli çıkış yap
            if (holsterCtrl.IsBackpackOpen ||
                holsterCtrl.InventoryUi.GetActiveHotbarSlot().Kind != InventoryItemKind.FlintStone)
            {
                BeginExitSession(miningCtrl, holsterCtrl, savePartialOnStone: Phase == KnappingSessionPhase.ActiveChipping);
            }

            switch (Phase)
            {
                case KnappingSessionPhase.RadialTemplateMenu:
                    StepTemplateSelectorInput(cam, playerRoot, miningCtrl, holsterCtrl);
                    break;

                case KnappingSessionPhase.CrouchAndOutlineAnim:
                    StepCrouchAndOutlineAnim(dt, cam, miningCtrl);
                    break;

                case KnappingSessionPhase.ActiveChipping:
                    StepActiveChipping(dt, cam, miningCtrl, holsterCtrl, combatBridge);
                    break;

                case KnappingSessionPhase.CameraExitAnim:
                    StepCameraExitAnim(dt, cam);
                    break;
            }
        }

        private void OpenTemplateSelector(
            UnityEngine.Camera cam,
            Transform playerRoot,
            GroundStoneTargetSource source,
            int groundStoneIdx,
            Vector3Int stoneVoxelCoord,
            Vector3 stonePos,
            byte stoneMat,
            PhysicalItemShapeDna stoneDna)
        {
            _targetSource = source;
            _groundStoneIndex = groundStoneIdx;
            _groundStoneVoxelCoord = stoneVoxelCoord;
            _groundStoneWorldPos = stonePos;
            _groundStoneMatId = stoneMat != 0 ? stoneMat : MaterialIdCatalog.Stone;
            _groundStoneDna = stoneDna;
            _savedCamPos = cam.transform.position;
            _savedCamRot = cam.transform.rotation;

            Vector3 flatToStone = stonePos - playerRoot.position;
            flatToStone.y = 0f;
            if (flatToStone.sqrMagnitude < 0.001f)
            {
                flatToStone = new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z);
            }
            if (flatToStone.sqrMagnitude < 0.001f) flatToStone = Vector3.forward;
            flatToStone.Normalize();

            _slabWorldRot = Quaternion.LookRotation(flatToStone, Vector3.up);
            _targetCamPos = stonePos - flatToStone * 0.42f + Vector3.up * 0.76f;
            Vector3 lookTarget = stonePos + Vector3.up * 0.02f + flatToStone * 0.02f;
            _targetCamRot = Quaternion.LookRotation((lookTarget - _targetCamPos).normalized, Vector3.up);

            Phase = KnappingSessionPhase.RadialTemplateMenu;
            _phaseTimer = 0f;
            StatusBannerText = "Çakmaktaşı ile Yontulacak Alet Kalıbını Seçin ([1..4] veya Tıklayın | [Esc] İptal)";
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void StepTemplateSelectorInput(
            UnityEngine.Camera cam,
            Transform playerRoot,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Phase = KnappingSessionPhase.Idle;
                _targetSource = GroundStoneTargetSource.None;
                _groundStoneIndex = -1;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                StartKnappingWithTemplate(KnappingTemplateId.PickaxeHead, miningCtrl, holsterCtrl);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                StartKnappingWithTemplate(KnappingTemplateId.AxeHead, miningCtrl, holsterCtrl);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                StartKnappingWithTemplate(KnappingTemplateId.HammerHead, miningCtrl, holsterCtrl);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                StartKnappingWithTemplate(KnappingTemplateId.KnifeBlade, miningCtrl, holsterCtrl);
            }
        }

        private void StartKnappingWithTemplate(
            KnappingTemplateId template,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            Vector3 liveStonePos = _groundStoneWorldPos;
            byte liveMat = _groundStoneMatId;
            PhysicalItemShapeDna liveDna = _groundStoneDna;

            if (_targetSource == GroundStoneTargetSource.MiningWorldDrop)
            {
                if (!miningCtrl.TryGetDroppedWorldItemInfo(
                        _groundStoneIndex, out liveStonePos, out _, out liveMat, out liveDna, out _))
                {
                    CancelMissingTargetStone(miningCtrl);
                    return;
                }
                miningCtrl.SetDroppedWorldItemVisibility(_groundStoneIndex, false);
            }
            else if (_targetSource == GroundStoneTargetSource.HolsterPlayerDrop)
            {
                if (!holsterCtrl.TryGetPlayerDroppedStoneInfo(_groundStoneIndex, out liveStonePos, out liveMat, out liveDna))
                {
                    CancelMissingTargetStone(miningCtrl);
                    return;
                }
                holsterCtrl.SetPlayerDroppedItemVisibility(_groundStoneIndex, false);
            }

            _groundStoneWorldPos = liveStonePos;
            _groundStoneMatId = liveMat != 0 ? liveMat : MaterialIdCatalog.Stone;
            _groundStoneDna = liveDna;

            ulong startMask = ((liveDna.Flags & PhysicalItemShapeDna.FlagCustomKnapMask) != 0 &&
                               liveDna.KnapMask64 != 0UL)
                ? liveDna.KnapMask64
                : ulong.MaxValue;

            _session = StoneKnappingSessionSystem.BeginSession(template, startMask);

            EnsureInteractiveSlabAndStrikerCreated(miningCtrl.SharedVoxelMaterial);
            _hoveredCellX = -1;
            _hoveredCellZ = -1;
            _lastBuiltOutlineProgress = -1f;
            RebuildInteractiveSlabMesh(outlineProgress01: 0f);

            Phase = KnappingSessionPhase.CrouchAndOutlineAnim;
            _phaseTimer = 0f;
            _strikeAnimTimer = 0f;
            _shatterPauseTimer = 0f;
            StatusBannerText = $"✏️ {StoneKnappingSessionSystem.GetTemplateDisplayName(template)} hayalet kalıbı çakmaktaşı ile çiziliyor...";
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CancelMissingTargetStone(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            Phase = KnappingSessionPhase.Idle;
            _targetSource = GroundStoneTargetSource.None;
            _groundStoneIndex = -1;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            miningCtrl.LastFeedbackText = "⚠️ Hedef taş artık yerde değil!";
        }

        private void StepCrouchAndOutlineAnim(float dt, UnityEngine.Camera cam, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _phaseTimer += dt;
            float camT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseTimer / 0.45f));
            cam.transform.position = Vector3.Lerp(_savedCamPos, _targetCamPos, camT);
            cam.transform.rotation = Quaternion.Slerp(_savedCamRot, _targetCamRot, camT);

            float drawT = Mathf.Clamp01((_phaseTimer - 0.24f) / (CrouchOutlineDurationSec - 0.24f));
            if (Mathf.Abs(drawT - _lastBuiltOutlineProgress) >= 0.04f || drawT >= 1f)
            {
                RebuildInteractiveSlabMesh(drawT);
            }

            if (_strikerFlintGo != null)
            {
                _strikerFlintGo.SetActive(true);
                float angle = drawT * Mathf.PI * 2f;
                Vector3 localTrace = new Vector3(
                    Mathf.Cos(angle) * 0.075f,
                    SlabTopSurfaceY + 0.032f + Mathf.Sin(drawT * 28f) * 0.004f,
                    Mathf.Sin(angle) * 0.065f);
                Vector3 traceWorld = _groundStoneWorldPos + _slabWorldRot * localTrace;
                _strikerFlintGo.transform.position = traceWorld;
                _strikerFlintGo.transform.rotation = _slabWorldRot * Quaternion.Euler(62f, drawT * 120f, -18f);
            }

            if (_phaseTimer >= CrouchOutlineDurationSec)
            {
                RebuildInteractiveSlabMesh(1f);
                Phase = KnappingSessionPhase.ActiveChipping;
                _phaseTimer = 0f;
                UpdateStatusBanner();
            }
        }

        private void StepActiveChipping(
            float dt,
            UnityEngine.Camera cam,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            cam.transform.position = Vector3.Lerp(cam.transform.position, _targetCamPos, Mathf.Clamp01(dt * 14f));
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, _targetCamRot, Mathf.Clamp01(dt * 14f));

            if (_shatterPauseTimer > 0f)
            {
                _shatterPauseTimer -= dt;
                if (_shatterPauseTimer <= 0f)
                {
                    BeginExitSession(miningCtrl, holsterCtrl, savePartialOnStone: false);
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                miningCtrl.LastFeedbackText =
                    "🪨 TAŞ YONTMA DURAKLATILDI: Yontulan taş yerde olduğu şekliyle bırakıldı (Çakmaktaşı + [R] ile devam edebilirsiniz).";
                BeginExitSession(miningCtrl, holsterCtrl, savePartialOnStone: true);
                return;
            }

            if (_session.CanFinishHarvest && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(Stage1GameSettings.KeyInteract)))
            {
                CompleteHarvestAndGiveHead(miningCtrl, holsterCtrl, combatBridge);
                return;
            }

            UpdateHoveredSlabCell(cam);

            if (_hoveredCellX != _lastBuiltHoverX || _hoveredCellZ != _lastBuiltHoverZ || _session.CurrentMask64 != _lastBuiltMask64)
            {
                RebuildInteractiveSlabMesh(1f);
            }

            UpdateStrikerFlintPose(dt);

            if (Input.GetMouseButtonDown(0) && _hoveredCellX >= 0 && _hoveredCellZ >= 0)
            {
                ulong hoveredBit = StoneKnappingSessionSystem.CellBit(_hoveredCellX, _hoveredCellZ);
                if ((_session.CurrentMask64 & hoveredBit) != 0UL)
                {
                    ExecuteChipStrikeAtCell(_hoveredCellX, _hoveredCellZ, miningCtrl, holsterCtrl, combatBridge);
                }
            }
        }

        private void ExecuteChipStrikeAtCell(
            int cellX,
            int cellZ,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            _strikeTargetWorldPos = GetCellCenterWorldPos(cellX, cellZ);
            _strikeAnimTimer = 0.12f;
            _pendingStaminaDrain += 0.5f;
            combatBridge?.TriggerHitStop(0.018f);

            bool changed = StoneKnappingSessionSystem.ApplyFlintStrike(
                ref _session, cellX, cellZ, out ulong removedBits, out bool hitCoreCell, out bool shattered);

            if (!changed) return;

            SpawnStoneChipDebrisForRemovedBits(removedBits, miningCtrl.SharedVoxelMaterial, countPerCell: shattered ? 3 : 2);
            RebuildInteractiveSlabMesh(1f);

            // Çakmaktaşı hafif aşınır (~36 vuruşta 1 çakmaktaşı aşınır)
            _flintStrikeWearCounter++;
            VisualInventorySlot activeFlint = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            if (activeFlint.Kind == InventoryItemKind.FlintStone)
            {
                float nextDur = Mathf.Clamp01((activeFlint.Durability01 > 0.01f ? activeFlint.Durability01 : 1f) - 0.028f);
                if (nextDur <= 0.02f)
                {
                    holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1);
                    if (holsterCtrl.InventoryUi.GetActiveHotbarSlot().Kind == InventoryItemKind.FlintStone)
                    {
                        holsterCtrl.InventoryUi.UpdateSelectedToolDurability(1f);
                    }
                }
                else
                {
                    holsterCtrl.InventoryUi.UpdateSelectedToolDurability(nextDur);
                }
            }

            if (shattered)
            {
                SpawnStoneChipDebrisForRemovedBits(_session.TemplateMask64, miningCtrl.SharedVoxelMaterial, countPerCell: 1);
                ConsumeTargetStone(miningCtrl, holsterCtrl);
                if (_slabRootGo != null) _slabRootGo.SetActive(false);
                StatusBannerText = "💥 TAŞ PARÇALANDI! Çekirdek kalıbın yarısından fazlası kırıldığı için taş dağıldı.";
                miningCtrl.LastFeedbackText = StatusBannerText;
                _shatterPauseTimer = 0.50f;
                return;
            }

            UpdateStatusBanner(hitCoreCell);
        }

        private void ConsumeTargetStone(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            if (_targetSource == GroundStoneTargetSource.MiningWorldDrop && _groundStoneIndex >= 0)
            {
                miningCtrl.ConsumeOneDroppedWorldItemAt(_groundStoneIndex);
            }
            else if (_targetSource == GroundStoneTargetSource.HolsterPlayerDrop && _groundStoneIndex >= 0)
            {
                holsterCtrl.ConsumeOnePlayerDroppedStoneAt(_groundStoneIndex);
            }
            else if (_targetSource == GroundStoneTargetSource.WorldStoneVoxel)
            {
                miningCtrl.TryConsumeOrChipKnappedStoneVoxel(_groundStoneVoxelCoord, _lastWorldChunks, _lastDirtyChunks);
            }
            _targetSource = GroundStoneTargetSource.None;
            _groundStoneIndex = -1;
        }

        private static InventoryItemKind GetTemplateResultKind(KnappingTemplateId template) => template switch
        {
            KnappingTemplateId.PickaxeHead => InventoryItemKind.KnappedPickaxeHead,
            KnappingTemplateId.AxeHead => InventoryItemKind.KnappedAxeHead,
            KnappingTemplateId.KnifeBlade => InventoryItemKind.KnappedKnifeBlade,
            KnappingTemplateId.HammerHead => InventoryItemKind.KnappedHammerHead,
            _ => InventoryItemKind.KnappedPickaxeHead
        };

        private void CompleteHarvestAndGiveHead(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            byte qualityPct = _session.QualityPct;
            InventoryItemKind resultKind = GetTemplateResultKind(_session.Template);

            PhysicalItemShapeDna headDna = _groundStoneDna;
            if (headDna.ShapeSeed == 0u)
            {
                headDna = PhysicalItemShapeDna.CreateFromWorldCoord(
                    Mathf.RoundToInt(_groundStoneWorldPos.x * 4f),
                    Mathf.RoundToInt(_groundStoneWorldPos.y * 4f),
                    Mathf.RoundToInt(_groundStoneWorldPos.z * 4f),
                    (uint)(_session.StrikeCount + 17));
            }
            headDna.KnapMask64 = _session.CurrentMask64;
            headDna.QualityPct = qualityPct;
            headDna.Flags = (byte)(headDna.Flags | PhysicalItemShapeDna.FlagCustomKnapMask);

            ConsumeTargetStone(miningCtrl, holsterCtrl);

            var previewSlot = new VisualInventorySlot
            {
                Kind = resultKind,
                SubId = 0,
                Count = 1,
                Durability01 = 1f,
                ShapeDna = headDna
            };
            string itemTitle = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in previewSlot);
            string tierWord = qualityPct >= 92 ? "KUSURSUZ USTA İŞİ" : (qualityPct >= 72 ? "İYİ YONTULMUŞ" : "KABA / ZAYIF");

            if (holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(resultKind, 0, 1, miningCtrl, headDna))
            {
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                miningCtrl.LastFeedbackText =
                    $"✨ YONTMA TAMAMLANDI (%{qualityPct} {tierWord}): +1 {itemTitle}! Şimdi [I] Çanta bezinin ortasında Kuru Dal + Bitki Lifi ile birleştirin!";
            }
            else
            {
                miningCtrl.SpawnPhysicalWorldItem(
                    _groundStoneWorldPos + Vector3.up * 0.14f, resultKind, 0, 1, headDna, miningCtrl.SharedVoxelMaterial);
                miningCtrl.LastFeedbackText =
                    $"✨ YONTMA TAMAMLANDI (%{qualityPct} {tierWord}): Çanta dolu olduğu için {itemTitle} yere bırakıldı!";
            }

            BeginExitSession(miningCtrl, holsterCtrl, savePartialOnStone: false);
        }

        private void BeginExitSession(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            bool savePartialOnStone)
        {
            if (_targetSource == GroundStoneTargetSource.MiningWorldDrop && _groundStoneIndex >= 0)
            {
                if (savePartialOnStone && _session.StrikeCount > 0 && !_session.IsShattered)
                {
                    PhysicalItemShapeDna partialDna = _groundStoneDna;
                    partialDna.KnapMask64 = _session.CurrentMask64;
                    partialDna.QualityPct = _session.QualityPct;
                    partialDna.Flags = (byte)(partialDna.Flags | PhysicalItemShapeDna.FlagCustomKnapMask);
                    miningCtrl.SavePartialKnappingOnDroppedStone(_groundStoneIndex, partialDna);
                }
                else
                {
                    miningCtrl.SetDroppedWorldItemVisibility(_groundStoneIndex, true);
                }
            }
            else if (_targetSource == GroundStoneTargetSource.HolsterPlayerDrop && _groundStoneIndex >= 0)
            {
                if (savePartialOnStone && _session.StrikeCount > 0 && !_session.IsShattered)
                {
                    PhysicalItemShapeDna partialDna = _groundStoneDna;
                    partialDna.KnapMask64 = _session.CurrentMask64;
                    partialDna.QualityPct = _session.QualityPct;
                    partialDna.Flags = (byte)(partialDna.Flags | PhysicalItemShapeDna.FlagCustomKnapMask);
                    holsterCtrl.SavePartialKnappingOnPlayerDroppedStone(_groundStoneIndex, partialDna);
                }
                else
                {
                    holsterCtrl.SetPlayerDroppedItemVisibility(_groundStoneIndex, true);
                }
            }

            _targetSource = GroundStoneTargetSource.None;
            _groundStoneIndex = -1;
            if (_slabRootGo != null) _slabRootGo.SetActive(false);
            if (_strikerFlintGo != null) _strikerFlintGo.SetActive(false);

            Phase = KnappingSessionPhase.CameraExitAnim;
            _phaseTimer = 0f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void StepCameraExitAnim(float dt, UnityEngine.Camera cam)
        {
            _phaseTimer += dt;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseTimer / CameraExitDurationSec));
            cam.transform.position = Vector3.Lerp(_targetCamPos, _savedCamPos, t);
            cam.transform.rotation = Quaternion.Slerp(_targetCamRot, _savedCamRot, t);

            if (_phaseTimer >= CameraExitDurationSec)
            {
                cam.transform.position = _savedCamPos;
                cam.transform.rotation = _savedCamRot;
                Phase = KnappingSessionPhase.Idle;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void AbortAndRestoreImmediate(
            UnityEngine.Camera? cam,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            if (_targetSource == GroundStoneTargetSource.MiningWorldDrop && _groundStoneIndex >= 0)
            {
                miningCtrl.SetDroppedWorldItemVisibility(_groundStoneIndex, true);
            }
            else if (_targetSource == GroundStoneTargetSource.HolsterPlayerDrop && _groundStoneIndex >= 0)
            {
                holsterCtrl.SetPlayerDroppedItemVisibility(_groundStoneIndex, true);
            }
            _targetSource = GroundStoneTargetSource.None;
            _groundStoneIndex = -1;
            if (_slabRootGo != null) _slabRootGo.SetActive(false);
            if (_strikerFlintGo != null) _strikerFlintGo.SetActive(false);
            if (cam != null && Phase != KnappingSessionPhase.Idle && Phase != KnappingSessionPhase.RadialTemplateMenu)
            {
                cam.transform.position = _savedCamPos;
                cam.transform.rotation = _savedCamRot;
            }
            Phase = KnappingSessionPhase.Idle;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void UpdateHoveredSlabCell(UnityEngine.Camera cam)
        {
            _hoveredCellX = -1;
            _hoveredCellZ = -1;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 planeNormal = _slabWorldRot * Vector3.up;
            Vector3 planePoint = _groundStoneWorldPos + planeNormal * SlabTopSurfaceY;
            Plane slabPlane = new Plane(planeNormal, planePoint);

            if (!slabPlane.Raycast(ray, out float enterDist) || enterDist <= 0.02f || enterDist > 3.5f)
            {
                return;
            }

            Vector3 worldHit = ray.GetPoint(enterDist);
            Vector3 localHit = Quaternion.Inverse(_slabWorldRot) * (worldHit - _groundStoneWorldPos);

            float half = SlabWidthMeters * 0.5f;
            int cx = Mathf.FloorToInt((localHit.x + half) / CellSizeMeters);
            int cz = Mathf.FloorToInt((localHit.z + half) / CellSizeMeters);
            if ((uint)cx < 8u && (uint)cz < 8u)
            {
                _hoveredCellX = cx;
                _hoveredCellZ = cz;
            }
        }

        private Vector3 GetCellCenterWorldPos(int cx, int cz)
        {
            float half = SlabWidthMeters * 0.5f;
            Vector3 local = new Vector3(
                -half + (cx + 0.5f) * CellSizeMeters,
                SlabTopSurfaceY,
                -half + (cz + 0.5f) * CellSizeMeters);
            return _groundStoneWorldPos + _slabWorldRot * local;
        }

        private void UpdateStrikerFlintPose(float dt)
        {
            if (_strikerFlintGo == null) return;
            _strikerFlintGo.SetActive(true);

            Vector3 baseHoverWorld = (_hoveredCellX >= 0 && _hoveredCellZ >= 0)
                ? GetCellCenterWorldPos(_hoveredCellX, _hoveredCellZ) + Vector3.up * 0.068f
                : _groundStoneWorldPos + _slabWorldRot * new Vector3(0.14f, 0.085f, -0.10f);

            if (_strikeAnimTimer > 0f)
            {
                _strikeAnimTimer = Mathf.Max(0f, _strikeAnimTimer - dt);
                float u = 1f - (_strikeAnimTimer / 0.12f); // 0 -> 1
                float dip = u < 0.42f ? (u / 0.42f) : (1f - (u - 0.42f) / 0.58f);
                Vector3 strikePos = Vector3.Lerp(baseHoverWorld, _strikeTargetWorldPos + Vector3.up * 0.008f, dip);
                _strikerFlintGo.transform.position = strikePos;
                _strikerFlintGo.transform.rotation = _slabWorldRot * Quaternion.Euler(42f + dip * 34f, -22f, -15f);
            }
            else
            {
                _strikerFlintGo.transform.position = Vector3.Lerp(
                    _strikerFlintGo.transform.position, baseHoverWorld, Mathf.Clamp01(dt * 22f));
                _strikerFlintGo.transform.rotation = Quaternion.Slerp(
                    _strikerFlintGo.transform.rotation,
                    _slabWorldRot * Quaternion.Euler(42f, -22f, -15f),
                    Mathf.Clamp01(dt * 18f));
            }
        }

        private void EnsureInteractiveSlabAndStrikerCreated(Material? sharedMat)
        {
            if (_slabRootGo == null)
            {
                _slabRootGo = new GameObject("InteractiveStoneKnappingSlab_8x8");
                _slabMf = _slabRootGo.AddComponent<MeshFilter>();
                _slabMr = _slabRootGo.AddComponent<MeshRenderer>();
            }
            if (_slabMr != null && sharedMat != null)
            {
                _slabMr.sharedMaterial = sharedMat;
            }
            _slabRootGo.transform.position = _groundStoneWorldPos + Vector3.up * 0.006f;
            _slabRootGo.transform.rotation = _slabWorldRot;
            _slabRootGo.transform.localScale = Vector3.one;
            _slabRootGo.SetActive(true);

            if (_strikerFlintGo == null)
            {
                _strikerFlintGo = new GameObject("InteractiveFlintStriker_3D");
                MeshFilter mf = _strikerFlintGo.AddComponent<MeshFilter>();
                MeshRenderer mr = _strikerFlintGo.AddComponent<MeshRenderer>();
                var flintDna = PhysicalItemShapeDna.CreateNatural(0x51F7A9u, 100);
                mf.sharedMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                    PhysicalItemArchetype.FlintStone, 0, in flintDna, out Vector3 natScale);
                _strikerFlintGo.transform.localScale = natScale * 0.85f;
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
            }
            else if (sharedMat != null)
            {
                MeshRenderer? mr = _strikerFlintGo.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = sharedMat;
            }
            _strikerFlintGo.SetActive(true);
        }

        private void RebuildInteractiveSlabMesh(float outlineProgress01)
        {
            if (_slabMf == null) return;
            if (_dynamicSlabMesh != null)
            {
                Object.Destroy(_dynamicSlabMesh);
                _dynamicSlabMesh = null;
            }

            _dynamicSlabMesh = PhysicalItemMeshFactory.BuildInteractiveKnappingSlabMesh(
                _session.CurrentMask64,
                _session.TemplateMask64,
                _groundStoneMatId,
                _hoveredCellX,
                _hoveredCellZ,
                outlineProgress01);
            _slabMf.sharedMesh = _dynamicSlabMesh;

            _lastBuiltHoverX = _hoveredCellX;
            _lastBuiltHoverZ = _hoveredCellZ;
            _lastBuiltMask64 = _session.CurrentMask64;
            _lastBuiltOutlineProgress = outlineProgress01;
        }

        private void SpawnStoneChipDebrisForRemovedBits(ulong removedBits, Material? sharedMat, int countPerCell)
        {
            if (removedBits == 0UL) return;
            var chipDna = PhysicalItemShapeDna.CreateNatural((uint)(0x9E3779B9u ^ (_session.StrikeCount * 7919)), 100);
            Mesh chipMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                PhysicalItemArchetype.RoughStone, _groundStoneMatId, in chipDna, out Vector3 natScale);

            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    ulong bit = 1UL << ((z << 3) | x);
                    if ((removedBits & bit) == 0UL) continue;

                    Vector3 origin = GetCellCenterWorldPos(x, z);
                    Vector3 outDir = (origin - _groundStoneWorldPos);
                    outDir.y = 0f;
                    if (outDir.sqrMagnitude < 0.0001f) outDir = Vector3.right;
                    outDir.Normalize();

                    for (int k = 0; k < countPerCell && _flyingChips.Count < 40; k++)
                    {
                        GameObject chipGo = new GameObject("KnapStoneFlakeChip");
                        chipGo.AddComponent<MeshFilter>().sharedMesh = chipMesh;
                        MeshRenderer mr = chipGo.AddComponent<MeshRenderer>();
                        if (sharedMat != null) mr.sharedMaterial = sharedMat;

                        float spread = (k - 0.5f) * 0.55f;
                        Vector3 side = Vector3.Cross(Vector3.up, outDir);
                        Vector3 vel = (outDir + side * spread) * Random.Range(0.75f, 1.55f) + Vector3.up * Random.Range(0.95f, 1.65f);

                        chipGo.transform.position = origin;
                        chipGo.transform.localScale = natScale * Random.Range(0.18f, 0.28f);

                        _flyingChips.Add(new FlyingStoneChip
                        {
                            Go = chipGo,
                            Pos = origin,
                            Vel = vel,
                            AngularVelDeg = new Vector3(Random.Range(-420f, 420f), Random.Range(-420f, 420f), Random.Range(-420f, 420f)),
                            Age = 0f,
                            MaxLife = Random.Range(0.42f, 0.68f)
                        });
                    }
                }
            }
        }

        private void StepFlyingChips(float dt)
        {
            for (int i = _flyingChips.Count - 1; i >= 0; i--)
            {
                FlyingStoneChip c = _flyingChips[i];
                c.Age += dt;
                if (c.Go == null || c.Age >= c.MaxLife)
                {
                    if (c.Go != null) Object.Destroy(c.Go);
                    _flyingChips.RemoveAt(i);
                    continue;
                }

                c.Vel.y -= 9.81f * dt;
                c.Pos += c.Vel * dt;
                c.Go.transform.position = c.Pos;
                c.Go.transform.Rotate(c.AngularVelDeg * dt, Space.Self);
                _flyingChips[i] = c;
            }
        }

        private void UpdateStatusBanner(bool justHitCore = false)
        {
            int excessLeft = _session.RemainingExcessCells;
            int coreLost = _session.MissingCoreCells;
            int qPct = _session.QualityPct;
            string tmplName = StoneKnappingSessionSystem.GetTemplateDisplayName(_session.Template);

            if (justHitCore)
            {
                StatusBannerText =
                    $"⚠️ ÇEKİRDEK ÇİZGİSİNİN İÇİ KIRILDI! ({tmplName} | Tahmini Kalite: %{qPct} | Fazla Kenar: {excessLeft}) — Çizginin dışındaki parçaları kırın!";
                return;
            }

            if (excessLeft == 0)
            {
                StatusBannerText =
                    $"✨ Mükemmel Şekil! ({tmplName} | Kalite: %{qPct}) -> Başlığı almak için [E] tuşuna basın!";
            }
            else if (_session.CanFinishHarvest)
            {
                StatusBannerText =
                    $"🪨 {tmplName} Şekilleniyor (Kalite: %{qPct} | Kalan Dış Fazlalık: {excessLeft} | Kırılan İç: {coreLost}) -> Daha keskin yontun veya [E] ile bu haliyle alın!";
            }
            else
            {
                StatusBannerText =
                    $"🪨 {tmplName}: Beyaz çizginin DIŞINDAKİ kenarlara [Sol Tık] ile vurarak taşı yontun! (Kalan Dış Fazlalık: {excessLeft} | [Esc] Duraklat)";
            }
        }

        private void EnsureGuiStyles()
        {
            if (_panelStyle != null) return;

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(14, 14, 10, 10)
            };
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.98f, 0.92f, 0.72f, 1f) }
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.90f, 0.93f, 0.96f, 1f) }
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            _accentBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.72f, 1.0f, 0.78f, 1f) }
            };
        }

        public void DrawOnGui(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (!IsBusy && !HasVisibleToast) return;
            EnsureGuiStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            if (!IsBusy)
            {
                Rect toastRect = new Rect(sw * 0.5f - 340f, sh * 0.5f - 115f, 680f, 62f);
                GUI.color = new Color(0.12f, 0.10f, 0.08f, 0.92f);
                GUI.Box(toastRect, GUIContent.none, _panelStyle!);
                GUI.color = Color.white;
                GUI.Label(new Rect(toastRect.x + 12f, toastRect.y + 8f, toastRect.width - 24f, 46f), _toastMessage, _titleStyle!);
                return;
            }

            if (Phase == KnappingSessionPhase.RadialTemplateMenu)
            {
                DrawRadialTemplateMenuGui(sw, sh, miningCtrl, holsterCtrl);
                return;
            }

            if (Phase == KnappingSessionPhase.CrouchAndOutlineAnim || Phase == KnappingSessionPhase.ActiveChipping)
            {
                Rect topBar = new Rect(sw * 0.5f - 390f, 22f, 780f, 64f);
                GUI.color = new Color(0.08f, 0.10f, 0.12f, 0.88f);
                GUI.Box(topBar, GUIContent.none, _panelStyle!);
                GUI.color = Color.white;

                GUI.Label(new Rect(topBar.x + 12f, topBar.y + 6f, topBar.width - 24f, 24f),
                    $"🪨 FİZİKSEL TAŞ YONTMA — {StoneKnappingSessionSystem.GetTemplateDisplayName(_session.Template)}  |  Anlık Kalite: %{_session.QualityPct}",
                    _titleStyle!);
                GUI.Label(new Rect(topBar.x + 12f, topBar.y + 30f, topBar.width - 24f, 28f), StatusBannerText, _bodyStyle!);

                if (Phase == KnappingSessionPhase.ActiveChipping && _session.CanFinishHarvest && _shatterPauseTimer <= 0f)
                {
                    Rect harvestBtn = new Rect(sw * 0.5f - 175f, sh - 118f, 350f, 42f);
                    GUI.color = new Color(0.16f, 0.42f, 0.22f, 0.95f);
                    if (GUI.Button(harvestBtn, $"✋ Yontulmuş Başlığı Al [E]  (Kalite: %{_session.QualityPct})", _accentBtnStyle!))
                    {
                        CompleteHarvestAndGiveHead(miningCtrl, holsterCtrl, combatBridge);
                    }
                    GUI.color = Color.white;
                }
            }
        }

        private void DrawRadialTemplateMenuGui(
            float sw,
            float sh,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            float cx = sw * 0.5f;
            float cy = sh * 0.5f;

            Rect header = new Rect(cx - 270f, cy - 228f, 540f, 56f);
            GUI.color = new Color(0.07f, 0.09f, 0.12f, 0.92f);
            GUI.Box(header, GUIContent.none, _panelStyle!);
            GUI.color = Color.white;
            GUI.Label(new Rect(header.x + 10f, header.y + 6f, header.width - 20f, 22f),
                "🪨 ÇAKMAKTAŞI İLE TAŞ YONTMA KALIBI SEÇİN", _titleStyle!);
            GUI.Label(new Rect(header.x + 10f, header.y + 28f, header.width - 20f, 22f),
                "[1..4] Tuşuna Basın veya Kartı Tıklayın  |  [Sağ Tık / Esc] Vazgeç", _bodyStyle!);

            DrawTemplateOptionCard(
                new Rect(cx - 115f, cy - 155f, 230f, 88f),
                "[1] Kazma Başı",
                "Kavisli çift uçlu taş kazma başı\n(Bağlama: +1 Dal + 1 Lif)",
                KnappingTemplateId.PickaxeHead,
                miningCtrl,
                holsterCtrl);

            DrawTemplateOptionCard(
                new Rect(cx - 265f, cy - 44f, 230f, 88f),
                "[2] Balta Başı",
                "Geniş kama ağızlı taş balta başı\n(Bağlama: +1 Dal + 1 Lif)",
                KnappingTemplateId.AxeHead,
                miningCtrl,
                holsterCtrl);

            DrawTemplateOptionCard(
                new Rect(cx + 35f, cy - 44f, 230f, 88f),
                "[3] Ağır Taş Çekiç Başı",
                "Küt darbe ve cevher kırma çekici\n(Bağlama: +1 Dal + 2 Lif)",
                KnappingTemplateId.HammerHead,
                miningCtrl,
                holsterCtrl);

            DrawTemplateOptionCard(
                new Rect(cx - 115f, cy + 67f, 230f, 88f),
                "[4] Keskin Bıçak Ağzı",
                "İnce keskin yüzme ve kesme bıçağı\n(Bağlama: +1 Dal + 1 Lif)",
                KnappingTemplateId.KnifeBlade,
                miningCtrl,
                holsterCtrl);
        }

        private void DrawTemplateOptionCard(
            Rect rect,
            string title,
            string subtitle,
            KnappingTemplateId template,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            GUI.color = new Color(0.14f, 0.17f, 0.21f, 0.95f);
            if (GUI.Button(rect, $"{title}\n{subtitle}", _buttonStyle!))
            {
                GUI.color = Color.white;
                StartKnappingWithTemplate(template, miningCtrl, holsterCtrl);
                return;
            }
            GUI.color = Color.white;
        }
    }
}
#endif
