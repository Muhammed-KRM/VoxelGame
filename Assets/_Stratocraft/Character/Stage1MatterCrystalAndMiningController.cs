#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 3 (P2 &amp; P3) — Gercek 264-Byte Q24.8 Madde Kristali Deposu (<see cref="CrystalMassStorage"/>),
    /// Kristal Kademesi Kapisi (<c>CrystalTier 1..4</c>) + Hasar Durumu (<c>AvgDamage4Bit</c>) Korunumu,
    /// Alet Kademesi (<c>ToolTier</c>) &amp; Alet Dayanikliligi (<c>CurrentToolDurability</c>),
    /// 24 Yonelimli 3B Rotasyon (<c>[R]</c> / <c>[Shift+R]</c>) ve <c>0.25m</c> Mikro-Keski Alt-Kup Hedefleme Kontrolcusu.
    /// </summary>
    public sealed class Stage1MatterCrystalAndMiningController
    {
        private static readonly float[] BrushRadiusSteps = { 0.5f, 1.15f, 1.85f, 2.85f };
        private static readonly string[] BrushAreaLabels = { "1x1x1 Tek Blok", "2x2x2 Orta Alan", "3x3x3 Genis Alan", "5x5x5 Dev Kazi" };
        private static readonly byte[] BuildableMaterials =
        {
            MaterialIdCatalog.Stone, MaterialIdCatalog.Dirt, MaterialIdCatalog.Sand, MaterialIdCatalog.Basalt,
            MaterialIdCatalog.NaturalObsidian, MaterialIdCatalog.Cobblestone, MaterialIdCatalog.OakWood, MaterialIdCatalog.LooseRubble,
            MaterialIdCatalog.Marble, MaterialIdCatalog.StoneBrick, MaterialIdCatalog.Ironwood, MaterialIdCatalog.WoodPlank
        };
        private static readonly byte[] ShapeWheelCycle = { 0, 1, 2, 3, 4, 5, 6, 7, 63 };
        private static readonly float[] RayMarchOffsets = { 0.15f, 0.38f, 0.62f, 0.88f, 1.15f };

        public readonly CrystalMassStorage CrystalStorage = new(maxTotalBlocksCapacity: 16384, crystalTier: 3);
        public readonly MatterCrystalVacuumSystem VacuumSystem = new();
        private readonly List<FacetedVoxelTriangle> _ghostTris = new(64);
        private readonly List<FacetedVoxelTriangle> _beamTris = new(48);
        private readonly List<FacetedVoxelTriangle> _dropItemTris = new(36);
        private readonly List<DroppedWorldItem> _droppedWorldItems = new(64);
        private readonly Dictionary<int, Mesh> _droppedItemMeshCache = new(32);
        private GameObject? _placementGhostGo, _miningTargetWireframeGo, _vacuumBeamGo;
        private MeshFilter? _placementGhostMf, _vacuumBeamMf;
        private Material? _lastSharedVoxelMat;
        private byte _selectedRotation24;
        private byte _customSelectedMaterialId;
        private byte _lastGhostMat, _lastGhostShape, _lastGhostColorCode, _lastGhostRot;
        private int _brushRadiusIdx, _materialIdx, _shapeIdx = 1;
        private float _actionCooldown, _miningDamageAccumulator;
        private int _lastToolSwingId;
        private int _lastActiveToolHolsterSlot = -1;
        private Vector3Int _lastMinedVoxelCoord = new(int.MinValue, int.MinValue, int.MinValue);

        public struct DroppedWorldItem
        {
            public GameObject? VisualGo;
            public Vector3 Position;
            public Vector3 Velocity;
            public byte MaterialId;
            public byte ShapeId;
            public int Count;
            public float SpawnAge;
            public string? CustomLabel;
        }

        public int CurrentCrystalTier = 3;
        public int CurrentToolTier = 2;
        public float CurrentToolDurability = 120f;
        public float MaxToolDurability = 120f;

        public float ToolDurability01 => Mathf.Clamp01(CurrentToolDurability / Mathf.Max(1f, MaxToolDurability));
        public int ActiveDroppedItemCount => _droppedWorldItems.Count;
        public bool IsCrystalActive { get; set; }
        public byte SelectedMaterialId => _customSelectedMaterialId != 0 ? _customSelectedMaterialId : BuildableMaterials[_materialIdx];
        public byte SelectedShapeId => ShapeWheelCycle[_shapeIdx];
        public byte SelectedRotation => _selectedRotation24;
        public float BrushRadiusMeters => BrushRadiusSteps[_brushRadiusIdx];
        public string BrushAreaLabel => BrushAreaLabels[_brushRadiusIdx];
        public int LooseRubbleShardCount { get; set; } = 8;
        public int HarvestedOreCount { get; private set; }
        public int HarvestedCopperCount { get; private set; }
        public int HarvestedIronCount { get; private set; }
        public int HarvestedGoldCount { get; private set; }
        public int HarvestedMithrilCount { get; private set; }
        public StructuralPreviewColor CurrentGhostPreviewColor { get; private set; } = StructuralPreviewColor.GreenSafe;
        public string LastFeedbackText { get; set; } = "Hazir ([C] Kristal | [1-9/0] Alet & Kilif | [V] Sekil | [R] 24-Yon | [B/I] Malzeme)";
        public bool HasAimHit { get; private set; }
        public RaycastHit AimHit { get; private set; }
        public Vector3Int AimVoxelCoord { get; private set; }
        public Vector3Int PlacementVoxelCoord { get; private set; }
        public int AimSubVoxelX { get; private set; }
        public int AimSubVoxelY { get; private set; }
        public int AimSubVoxelZ { get; private set; }
        public byte AimMaterialId { get; private set; }
        public byte AimDensity { get; private set; }
        public byte AimDamageState { get; private set; }

        public Stage1MatterCrystalAndMiningController()
        {
            const uint b = CrystalMassStorageData.OneBlockQ24_8;
            CrystalStorage.CrystalTier = (byte)CurrentCrystalTier;
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, 16u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Dirt, 12u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Sand, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Basalt, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.NaturalObsidian, 4u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Cobblestone, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.OakWood, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.LooseRubble, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Marble, 12u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.StoneBrick, 12u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Ironwood, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.WoodPlank, 8u * b);
        }

        public static ReadOnlySpan<byte> GetCrystalBuildableMaterials() => BuildableMaterials;

        public int GetMaterialCountInCrystal(byte matId)
        {
            if (matId == MaterialIdCatalog.LooseRubble)
            {
                return LooseRubbleShardCount + CrystalStorage.GetCount(MaterialIdCatalog.LooseRubble);
            }
            return CrystalStorage.GetCount(matId);
        }

        public void CycleCrystalMaterial(int delta)
        {
            _customSelectedMaterialId = 0;
            int step = delta >= 0 ? 1 : -1;
            int len = BuildableMaterials.Length;
            // Önce kristalde stoğu bulunan malzemeler arasında geçiş yap; hiçbiri yoksa tüm listede dön
            for (int attempt = 0; attempt < len; attempt++)
            {
                _materialIdx = (_materialIdx + step + len) % len;
                byte candidate = BuildableMaterials[_materialIdx];
                if (GetMaterialCountInCrystal(candidate) > 0)
                {
                    break;
                }
            }
            byte mat = SelectedMaterialId;
            int cnt = GetMaterialCountInCrystal(mat);
            LastFeedbackText = $"Kristal İnşa Malzemesi [Tekerlek/B]: {GetMaterialDisplayName(mat)} (Stok: x{cnt}) -> [C + Sağ Tık] Dünyaya Koy";
        }

        public void SelectMaterial(byte materialId)
        {
            if (materialId == MaterialIdCatalog.Air) return;
            int idx = Array.IndexOf(BuildableMaterials, materialId);
            if (idx >= 0)
            {
                _materialIdx = idx;
                _customSelectedMaterialId = 0;
            }
            else
            {
                _customSelectedMaterialId = materialId;
            }
        }

        public bool CanCrystalVacuumMaterial(byte mat, out byte requiredTier)
        {
            CrystalStorage.CrystalTier = (byte)Mathf.Clamp(CurrentCrystalTier, 1, 4);
            requiredTier = MaterialIdCatalog.GetRequiredCrystalTierForTerrain(mat);
            return requiredTier > 0 && requiredTier <= CurrentCrystalTier;
        }

        public static byte GetRequiredToolTierForMaterial(byte mat)
        {
            if (mat == MaterialIdCatalog.NaturalObsidian || mat == MaterialIdCatalog.MithrilOre ||
                mat == MaterialIdCatalog.TitaniumOre || mat >= MaterialIdCatalog.OrichalcumOre && mat <= MaterialIdCatalog.VoidMetalOre)
            {
                return 3;
            }
            if (mat == MaterialIdCatalog.IronOre || mat == MaterialIdCatalog.GoldOre || mat == MaterialIdCatalog.Basalt || mat == MaterialIdCatalog.MagmaBlock)
            {
                return 2;
            }
            return 1;
        }

        public void HandleHotkeysAndAim(
            float dt, UnityEngine.Camera? cam, bool backpackOpen, int activeSlot,
            Dictionary<(int cx, int cz), uint[]> worldChunks, Material? sharedMat)
        {
            if (sharedMat != null) _lastSharedVoxelMat = sharedMat;
            CrystalStorage.CrystalTier = (byte)Mathf.Clamp(CurrentCrystalTier, 1, 4);
            if (_actionCooldown > 0f) _actionCooldown = Mathf.Max(0f, _actionCooldown - dt);
            if (!backpackOpen)
            {
                if (Input.GetKeyDown(Stage1GameSettings.KeyMatterCrystal) || Input.GetKeyDown(KeyCode.C)) IsCrystalActive = !IsCrystalActive;
                if (Input.GetKeyDown(KeyCode.V))
                {
                    _shapeIdx = (_shapeIdx + 1) % ShapeWheelCycle.Length;
                    LastFeedbackText = $"Insa Modu [V]: {GetShapeDisplayName(SelectedShapeId)} (0=Dogal Arazi, 1..7=Mimari Sekil, 63=Mikro)";
                }
                if (Input.GetKeyDown(KeyCode.R))
                {
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    _selectedRotation24 = shift
                        ? (byte)((_selectedRotation24 + 1) & 3)
                        : (byte)((_selectedRotation24 + 1) % 24);
                    LastFeedbackText = shift
                        ? $"Yatay Yaw Rotasyonu [Shift+R]: {_selectedRotation24}/3 (90° Yatay Donus)"
                        : $"3B Tam Oryantasyon [R]: {_selectedRotation24}/23 (24-Yonlu Mimari Donus)";
                }
                if (Input.GetKeyDown(KeyCode.B))
                {
                    CycleCrystalMaterial(1);
                }
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (altHeld && Mathf.Abs(scroll) > 0.001f)
                {
                    int delta = scroll > 0f ? 1 : -1;
                    _shapeIdx = (_shapeIdx + delta + ShapeWheelCycle.Length) % ShapeWheelCycle.Length;
                    LastFeedbackText = $"İnşa Şekli [Alt+Tekerlek]: {GetShapeDisplayName(SelectedShapeId)}";
                }
                else if (Input.GetKeyDown(KeyCode.T) || (IsCrystalActive && shiftHeld && Mathf.Abs(scroll) > 0.001f))
                {
                    int delta = (IsCrystalActive && shiftHeld && Mathf.Abs(scroll) > 0.001f) ? (scroll > 0f ? 1 : -1) : 1;
                    _brushRadiusIdx = (_brushRadiusIdx + delta + BrushRadiusSteps.Length) % BrushRadiusSteps.Length;
                    LastFeedbackText = $"Kristal Vakum Alanı [T / Shift+Tekerlek]: {BrushAreaLabel} (Yarıçap: {BrushRadiusMeters:0.1}m | Kademe: T{CurrentCrystalTier})";
                }
                else if (IsCrystalActive && Mathf.Abs(scroll) > 0.001f)
                {
                    CycleCrystalMaterial(scroll > 0f ? -1 : 1);
                }
            }
            UpdateTargetAimProbe(cam, worldChunks);
            UpdatePlacementGhostVisual(!backpackOpen && HasAimHit && AimHit.distance <= 8.5f, activeSlot, worldChunks, sharedMat);
        }

        public void ExecuteMiningOrBuilding(
            float dt, int activeHolsterSlot, bool backpackOpen, int maxEditableY, Vector3 playerPosition,
            float playerCapsuleHeight, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks = null,
            HashSet<(int cx, int cz)>? dirtyFluidChunks = null,
            Transform? wristCrystalTransform = null,
            Stage1DirectionalCombatBridge? combatBridge = null,
            Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            if (sharedMat != null) _lastSharedVoxelMat = sharedMat;
            bool lmb = !backpackOpen && Input.GetMouseButton(0);
            UpdateVacuumBeamVisual(IsCrystalActive && lmb && HasAimHit && AimHit.distance <= 9.5f, wristCrystalTransform, sharedMat);

            if (backpackOpen) return;

            bool isHoldingMaterialBlock = !IsCrystalActive && (activeHolsterSlot == 4 || activeHolsterSlot >= 7);
            if (isHoldingMaterialBlock && holsterCtrl != null)
            {
                VisualInventorySlot activeUiSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                byte slotMat = ResolveSlotBuildableMaterialId(in activeUiSlot);
                if (slotMat != MaterialIdCatalog.Air)
                {
                    SelectMaterial(slotMat);
                }
            }

            if (activeHolsterSlot != _lastActiveToolHolsterSlot)
            {
                _lastActiveToolHolsterSlot = activeHolsterSlot;
                if (combatBridge != null)
                {
                    combatBridge.ConsumePendingToolVoxelHit(out _, out _);
                    _lastToolSwingId = combatBridge.CurrentSwingId;
                }
            }

            if (!IsCrystalActive && (activeHolsterSlot == 2 || activeHolsterSlot == 3) && combatBridge != null)
            {
                bool hasBladeVoxelHit = combatBridge.ConsumePendingToolVoxelHit(out Vector3Int bladeHitCoord, out float bladeImpactPower);
                bool isNewIntentionalSwing = combatBridge.CurrentSwingId > 0 && _lastToolSwingId != combatBridge.CurrentSwingId;
                bool hasCloseSwingStrike = !hasBladeVoxelHit &&
                                           isNewIntentionalSwing &&
                                           combatBridge.SlashPhase == BallisticSlashPhase.ActiveFullArcSwing &&
                                           combatBridge.SlashProgress01 >= 0.30f &&
                                           HasAimHit && AimHit.distance <= 3.85f;

                if (isNewIntentionalSwing && (hasBladeVoxelHit || hasCloseSwingStrike))
                {
                    _lastToolSwingId = combatBridge.CurrentSwingId;
                    Vector3Int targetCoord = (HasAimHit && AimHit.distance <= 3.85f &&
                                              (!hasBladeVoxelHit || Vector3Int.Distance(bladeHitCoord, AimVoxelCoord) <= 2.1f))
                        ? AimVoxelCoord
                        : bladeHitCoord;
                    float impactMult = hasBladeVoxelHit
                        ? Mathf.Clamp(bladeImpactPower / 24f, 0.85f, 2.25f)
                        : Mathf.Clamp(combatBridge.AngularSpeedMagnitude / 8.5f, 0.90f, 2.10f);

                    ExecutePhysicalToolStrike(
                        activeHolsterSlot, maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat,
                        fluidChunks, dirtyFluidChunks, targetCoord, impactMult, holsterCtrl);
                    return;
                }
            }

            if (!HasAimHit || _actionCooldown > 0f || AimHit.distance > 9.5f) return;

            if (!IsCrystalActive && activeHolsterSlot == 5 && AimHit.distance <= 4.8f)
            {
                bool chiselLmb = Input.GetMouseButtonDown(0) || Input.GetMouseButton(0);
                bool chiselRmb = Input.GetMouseButtonDown(1);
                if (chiselLmb || chiselRmb)
                {
                    _actionCooldown = 0.14f;
                    ExecuteMicroChiselAction(
                        carveMode: chiselLmb, maxEditableY, playerPosition, playerCapsuleHeight,
                        worldChunks, dirtyChunks, collapseBridge, sharedMat, fluidChunks, dirtyFluidChunks);
                    return;
                }
            }

            if (!IsCrystalActive && activeHolsterSlot == 6 && AimHit.distance <= 5.5f)
            {
                bool flaskLmb = Input.GetMouseButtonDown(0);
                bool flaskRmb = Input.GetMouseButtonDown(1);
                if (flaskLmb || flaskRmb)
                {
                    _actionCooldown = 0.16f;
                    ExecuteFluidFlaskAction(
                        scoopMode: flaskLmb, maxEditableY, worldChunks, dirtyChunks,
                        fluidChunks, dirtyFluidChunks, holsterCtrl);
                    return;
                }
            }

            bool placeClick = isHoldingMaterialBlock
                ? (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButton(1))
                : (IsCrystalActive && (Input.GetMouseButtonDown(1) || Input.GetMouseButton(1)));
            if (placeClick)
            {
                _actionCooldown = 0.14f;
                ExecuteCrystalOrRubblePlacement(
                    maxEditableY, playerPosition, playerCapsuleHeight, worldChunks, dirtyChunks,
                    collapseBridge, sharedMat, fluidChunks, dirtyFluidChunks, activeHolsterSlot, holsterCtrl);
                return;
            }
            if (!lmb) return;

            if (IsCrystalActive)
            {
                _actionCooldown = 0.09f / Mathf.Clamp(Stage1GameSettings.MiningSpeedMultiplier, 0.5f, 4.0f);
                ExecuteCrystalVacuumBrush(maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat, fluidChunks, dirtyFluidChunks, holsterCtrl);
            }
            else if (activeHolsterSlot == 0 && AimHit.distance <= 3.5f)
            {
                _actionCooldown = 0.16f;
                ExecutePhysicalToolStrike(
                    activeHolsterSlot, maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat,
                    fluidChunks, dirtyFluidChunks, AimVoxelCoord, impactMultiplier: 0.55f, holsterCtrl: holsterCtrl);
            }
            else if ((activeHolsterSlot == 2 || activeHolsterSlot == 3) && AimHit.distance <= 3.85f &&
                     (combatBridge == null || combatBridge.SlashPhase == BallisticSlashPhase.IdleInGuard))
            {
                LastFeedbackText = "FIZIKSEL VURUS GEREKLI: Aleti savurmak icin [LMB] basili tutarak fareyi savurun! (Uzaktan cekim icin [C] Kristal)";
            }
        }

        private void ExecuteMicroChiselAction(
            bool carveMode, int maxY, Vector3 playerPos, float capsuleH,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirty,
            Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks)
        {
            if (carveMode)
            {
                int wx = AimVoxelCoord.x, wy = Mathf.Clamp(AimVoxelCoord.y, 1, maxY - 2), wz = AimVoxelCoord.z;
                int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
                if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;
                int idx = (wy << 8) | (lz << 4) | lx;
                uint raw = voxels[idx];
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                if (mat == MaterialIdCatalog.Air || mat == MaterialIdCatalog.Bedrock) return;

                byte depositMat = mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat;
                byte dmg4 = VoxelBitUtility.GetDamageState(raw);
                byte curShape = VoxelBitUtility.GetShapeId(raw);

                // Kutle Korunumu: Yontulan 0.25m alt-voksel (1/64 blok = 4 Q24.8 birim) Madde Kristaline aktarilir!
                CrystalStorage.TryDepositMassQ24_8(depositMat, CrystalMassStorageData.OneMicroSubVoxelQ24_8, dmg4);

                if (curShape != VoxelBitUtility.ChiseledMicroVoxelShapeId)
                {
                    byte poolIdx = (byte)(32 + ((wx * 19 + wy * 37 + wz * 11 + AimSubVoxelX * 3 + AimSubVoxelY * 5 + AimSubVoxelZ * 7) & 0x7F));
                    voxels[idx] = VoxelBitUtility.PackChiseled(
                        depositMat, poolIdx, VoxelBitUtility.GetOriginState(raw), dmg4);
                    LooseRubbleShardCount++;
                    MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                    LastFeedbackText =
                        $"0.25m MIKRO-KESKI: {GetMaterialDisplayName(mat)} Alt-Kup ({AimSubVoxelX},{AimSubVoxelY},{AimSubVoxelZ}) Yontuldu! (+4 Q24.8 Kutle & +1 Moloz)";
                }
                else
                {
                    voxels[idx] = 0u;
                    MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                    WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);
                    SpawnDroppedWorldItem(new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), mat, VoxelBitUtility.ChiseledMicroVoxelShapeId, 1, sharedMat);
                    collapseBridge.EvaluateChunkStability(cx, cz, voxels, maxY, new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), sharedMat, dirty, out _);
                    LastFeedbackText = $"0.25m MIKRO-KESKI: {GetMaterialDisplayName(mat)} Mikro-Blok sokuldu ve yere dustu!";
                }
            }
            else
            {
                int wx = PlacementVoxelCoord.x, wy = Mathf.Clamp(PlacementVoxelCoord.y, 1, maxY - 2), wz = PlacementVoxelCoord.z;
                if (!Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerPos, capsuleH))
                {
                    LastFeedbackText = "ANTI-GOMULME KORUMASI: Oyuncu kapsulu icine mikro-blok konulamaz!";
                    return;
                }
                int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
                if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;
                byte mat = SelectedMaterialId;
                byte restoredDmg = 0;
                if (!CrystalStorage.TryWithdrawMassQ24_8(mat, CrystalMassStorageData.OneMicroSubVoxelQ24_8, out restoredDmg))
                {
                    if (LooseRubbleShardCount <= 0)
                    {
                        LastFeedbackText = "Stokta 0.25m mikro-blok icin Q24.8 kutle veya Tas Moloz yok!";
                        return;
                    }
                    LooseRubbleShardCount--;
                }
                byte poolIdx = (byte)(64 + ((wx * 17 + wy * 31 + wz * 13 + AimSubVoxelX) & 0x7F));
                voxels[(wy << 8) | (lz << 4) | lx] = VoxelBitUtility.PackChiseled(
                    mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat,
                    poolIdx, BlokKokenDurumu.OyuncuItemBlogu, restoredDmg);
                MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                LastFeedbackText = $"0.25m MIKRO-KESKI [RMB]: {GetMaterialDisplayName(mat)} Alt-Kup ({AimSubVoxelX},{AimSubVoxelY},{AimSubVoxelZ}) Konuldu (Hasar:{restoredDmg}/15)!";
            }
        }

        private void ExecuteFluidFlaskAction(
            bool scoopMode, int maxY,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirty,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks,
            Stage1HolsterAndBackpackController? holsterCtrl)
        {
            if (fluidChunks == null) return;
            byte currentFlaskFluid = holsterCtrl != null ? holsterCtrl.InventoryUi.GetSelectedFlaskFluidType() : (byte)1;

            if (scoopMode)
            {
                for (int dy = 1; dy >= -1; dy--)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int wx = AimVoxelCoord.x + dx, wy = Mathf.Clamp(AimVoxelCoord.y + dy, 1, maxY - 2), wz = AimVoxelCoord.z + dz;
                    int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
                    if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null) continue;
                    int idx = (wy << 8) | (lz << 4) | lx;
                    FluidCell16 cell = new FluidCell16(fluids[idx]);
                    if (cell.Type != FluidType.None && cell.Mass >= 32)
                    {
                        byte pickedType = (byte)cell.Type;
                        fluids[idx] = 0;
                        WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);
                        holsterCtrl?.InventoryUi.SetSelectedFlaskFluidType(pickedType);
                        string fName = cell.Type == FluidType.Lava ? "Lav (Magma)" : "Su";
                        LastFeedbackText = $"SIVI MATARASI [LMB]: Havzadan {fName} dolduruldu! ([RMB] ile istediginiz yere dokebilirsiniz)";
                        return;
                    }
                }
                byte nextFluid = currentFlaskFluid == 1 ? (byte)2 : (byte)1;
                holsterCtrl?.InventoryUi.SetSelectedFlaskFluidType(nextFluid);
                LastFeedbackText = $"SIVI MATARASI [LMB]: Matara icerigi {(nextFluid == 2 ? "Lav (Magma)" : "Su")} olarak ayarlandi! ([RMB] ile dokun)";
            }
            else
            {
                FluidType pourType = currentFlaskFluid == 2 ? FluidType.Lava : FluidType.Water;
                int wx = PlacementVoxelCoord.x, wy = Mathf.Clamp(PlacementVoxelCoord.y, 1, maxY - 2), wz = PlacementVoxelCoord.z;
                int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
                if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null) return;
                int idx = (wy << 8) | (lz << 4) | lx;

                FluidCell16 existing = new FluidCell16(fluids[idx]);
                if (existing.Type != FluidType.None && existing.Type != pourType && worldChunks.TryGetValue((cx, cz), out uint[]? voxels))
                {
                    if (existing.Mass >= 128)
                    {
                        fluids[idx] = 0;
                        voxels[idx] = VoxelBitUtility.PackStandard(
                            MaterialIdCatalog.NaturalObsidian, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, 0);
                        MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                        WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);
                        LastFeedbackText = "STOKIYOMETRIK REAKSIYON: Matara ile Su + Lav (>=128) birlesti -> Dogal Obsidyen Blogu olustu!";
                        return;
                    }
                }

                fluids[idx] = FluidCell16.Pack(pourType, 255, isSettled: false, pressureHead: 2).RawValue;
                WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);
                string fName = pourType == FluidType.Lava ? "Lav (Magma)" : "Su";
                LastFeedbackText = $"SIVI MATARASI [RMB]: ({wx},{wy},{wz}) noktasina 255 birim {fName} dokuldu! (Akiskan fizigi aktif)";
            }
        }

        private void ExecutePhysicalToolStrike(
            int slot, int maxY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks,
            Vector3Int targetCoord, float impactMultiplier = 1.0f, Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            int wx = targetCoord.x, wy = Mathf.Clamp(targetCoord.y, 1, maxY - 2), wz = targetCoord.z;
            int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;
            int idx = (wy << 8) | (lz << 4) | lx;
            uint raw = voxels[idx];
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == MaterialIdCatalog.Air) return;
            if (mat == MaterialIdCatalog.Bedrock)
            {
                LastFeedbackText = "KIRILMAZ ZEMIN (Bedrock): Ana kaya katmani hicbir fiziksel aletle kirilamaz!";
                return;
            }

            if (_lastMinedVoxelCoord != targetCoord)
            {
                _lastMinedVoxelCoord = targetCoord;
                _miningDamageAccumulator = 0f;
            }

            float hardness = GetMaterialHardness(mat);
            bool isWood = mat == MaterialIdCatalog.OakWood || mat == MaterialIdCatalog.Ironwood || mat == MaterialIdCatalog.WoodPlank;
            bool isSoftEarth = mat == MaterialIdCatalog.Dirt || mat == MaterialIdCatalog.Grass ||
                               mat == MaterialIdCatalog.Sand || mat == MaterialIdCatalog.LooseRubble ||
                               mat == MaterialIdCatalog.GlowingMoss;

            float toolPower;
            if (slot == 0)
            {
                if ((!isSoftEarth && !isWood) || hardness >= 2.0f)
                {
                    LastFeedbackText = $"BOS EL [0]: {GetMaterialDisplayName(mat)} (Sertlik:{hardness:0.1}) ciplak elle kirilamaz! [2] Kazma cekin.";
                    return;
                }
                toolPower = isSoftEarth ? 9.5f : 5.0f;
            }
            else if (slot == 3)
            {
                if (!isWood && !isSoftEarth)
                {
                    LastFeedbackText = $"YANLIS ALET [3]: {GetMaterialDisplayName(mat)} baltayla kirilamaz! [2] Demir Kazma kullanin.";
                    return;
                }
                if (CurrentToolDurability <= 0f)
                {
                    LastFeedbackText = "ERR_TOOL_BROKEN: Balta asinmis! Ors yaninda [E] ile onarin.";
                    return;
                }
                toolPower = isWood ? 36.0f : 18.0f;
            }
            else
            {
                if (CurrentToolDurability <= 0f)
                {
                    LastFeedbackText = "ERR_TOOL_BROKEN: Kazma asinmis (%0 Dayaniklilik)! Ors yaninda [E] ile onarin.";
                    return;
                }
                byte reqToolTier = GetRequiredToolTierForMaterial(mat);
                if (reqToolTier > CurrentToolTier)
                {
                    LastFeedbackText = $"ERR_TOOL_TIER_TOO_LOW: {GetMaterialDisplayName(mat)} icin Kademe T{reqToolTier} Kazma gerekli (Mevcut: T{CurrentToolTier} — Ors yaninda [E] ile yukseltin)!";
                    return;
                }
                toolPower = isWood ? 14.0f : 26.0f * (1f + (CurrentToolTier - 2) * 0.25f);
            }

            if (slot == 2 || slot == 3)
            {
                CurrentToolDurability = Mathf.Max(0f, CurrentToolDurability - 0.5f * Stage1GameSettings.ToolDurabilityLossMultiplier);
                holsterCtrl?.InventoryUi.UpdateSelectedToolDurability(ToolDurability01);
            }

            float strikeGain = (slot == 0
                ? (toolPower / hardness) * 0.14f
                : (toolPower / hardness) * 0.34f * Mathf.Max(0.75f, impactMultiplier)) * Stage1GameSettings.MiningSpeedMultiplier;
            _miningDamageAccumulator += strikeGain;
            int dmgStep = Mathf.FloorToInt(_miningDamageAccumulator);
            if (dmgStep <= 0)
            {
                AimDamageState = VoxelBitUtility.GetDamageState(raw);
                LastFeedbackText = $"{GetMaterialDisplayName(mat)} Darbe Aliyor... Hasar: {AimDamageState}/15 (Sertlik: {hardness:0.1} | Alet:%{ToolDurability01 * 100f:0})";
                return;
            }
            _miningDamageAccumulator -= dmgStep;

            int nextDmg = VoxelBitUtility.GetDamageState(raw) + dmgStep;
            if (nextDmg < 15)
            {
                voxels[idx] = VoxelBitUtility.WithDamageState(raw, (byte)nextDmg);
                AimDamageState = (byte)nextDmg;
                MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                LastFeedbackText = $"FIZIKSEL DARBE! {GetMaterialDisplayName(mat)} Catliyor... Hasar: {AimDamageState}/15 (+{dmgStep} | Alet:%{ToolDurability01 * 100f:0})";
                return;
            }

            byte brokenShape = VoxelBitUtility.GetShapeId(raw);
            voxels[idx] = 0u;
            if (brokenShape == 0)
            {
                CarveSmoothSdfCraterNeighbors(wx, wy, wz, maxY, worldChunks, dirty);
            }
            MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
            WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);

            Vector3 dropWorldPos = new Vector3(wx + 0.5f, wy + 0.55f, wz + 0.5f);
            SpawnDroppedWorldItem(dropWorldPos, mat, brokenShape, count: 1, sharedMat);
            string harvestNote = $"Yere 3B {GetMaterialDisplayName(mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat)} Item Dustu";

            collapseBridge.EvaluateChunkStability(cx, cz, voxels, maxY, new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), sharedMat, dirty, out string msg);
            LastFeedbackText = !string.IsNullOrEmpty(msg) ? $"{msg} | {harvestNote}" : $"{GetMaterialDisplayName(mat)} Kirildi! ({harvestNote})";
        }

        private static void CarveSmoothSdfCraterNeighbors(
            int wx, int wy, int wz, int maxY,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty)
        {
            ReadOnlySpan<int> dx = stackalloc int[6] { 1, -1, 0, 0, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[6] { 0, 0, 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[6] { 0, 0, 0, 0, 1, -1 };

            for (int d = 0; d < 6; d++)
            {
                int nx = wx + dx[d], ny = wy + dy[d], nz = wz + dz[d];
                if (ny <= 1 || ny >= maxY - 1) continue;

                int ncx = FloorDiv16(nx), ncz = FloorDiv16(nz);
                int nlx = nx - ncx * 16, nlz = nz - ncz * 16;
                if (!worldChunks.TryGetValue((ncx, ncz), out uint[]? nChunk)) continue;

                int nIdx = (ny << 8) | (nlz << 4) | nlx;
                uint nRaw = nChunk[nIdx];
                byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                if (nMat == MaterialIdCatalog.Air || nMat == MaterialIdCatalog.Bedrock) continue;
                if (VoxelBitUtility.GetShapeId(nRaw) != 0) continue;

                if (dy[d] == 1 && !VoxelBitUtility.IsPhysicallySolid(nRaw))
                {
                    nChunk[nIdx] = 0u;
                }
                else
                {
                    byte curDens = VoxelBitUtility.GetDensityOrLiquid(nRaw);
                    int reduce = dy[d] == -1 ? 26 : 36;
                    int nextDens = curDens - reduce;
                    nChunk[nIdx] = nextDens < 28 ? 0u : VoxelBitUtility.WithDensityOrLiquid(nRaw, (byte)nextDens);
                }

                MarkChunkAndNeighborsDirty(ncx, ncz, nlx, nlz, dirty, worldChunks);
            }
        }

        private string HarvestBrokenVoxel(byte mat, byte shape, int count = 1, string? customLabel = null)
        {
            int safeCount = Mathf.Max(1, count);
            if (mat == MaterialIdCatalog.LooseRubble)
            {
                LooseRubbleShardCount += 2 * safeCount;
                return $"+{2 * safeCount} Tas Moloz Parca";
            }
            if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(mat))
            {
                HarvestedOreCount += safeCount;
                if (mat == MaterialIdCatalog.CopperOre) HarvestedCopperCount += safeCount;
                else if (mat == MaterialIdCatalog.IronOre) HarvestedIronCount += safeCount;
                else if (mat == MaterialIdCatalog.GoldOre) HarvestedGoldCount += safeCount;
                else if (mat == MaterialIdCatalog.MithrilOre) HarvestedMithrilCount += safeCount;
                CrystalStorage.TryDepositMassQ24_8(mat, (uint)safeCount * CrystalMassStorageData.OneBlockQ24_8);
                string label = !string.IsNullOrEmpty(customLabel) ? customLabel! : GetMaterialDisplayName(mat);
                return $"+{safeCount} {label} (Cevher/Ganimet Kesesi: {HarvestedOreCount})";
            }
            byte depositMat = mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat;
            CrystalStorage.TryDepositMassQ24_8(depositMat, (uint)safeCount * CrystalMassStorageData.OneBlockQ24_8);
            string displayLabel = !string.IsNullOrEmpty(customLabel) ? customLabel! : GetMaterialDisplayName(depositMat);
            return $"+{safeCount} {displayLabel} Envantere Alindi";
        }

        public void SpawnDroppedItem(Vector3 worldPos, byte materialId, int count = 1, string? customLabel = null)
        {
            SpawnDroppedWorldItem(worldPos, materialId, shape: 1, count: count, _lastSharedVoxelMat, customLabel);
        }

        public void SpawnDroppedWorldItem(Vector3 worldPos, byte mat, byte shape, int count, Material? sharedMat, string? customLabel = null)
        {
            if (mat == MaterialIdCatalog.Air) return;
            if (sharedMat != null) _lastSharedVoxelMat = sharedMat;
            if (_droppedWorldItems.Count >= 48)
            {
                if (_droppedWorldItems[0].VisualGo != null) UnityEngine.Object.Destroy(_droppedWorldItems[0].VisualGo);
                _droppedWorldItems.RemoveAt(0);
            }

            byte displayMat = mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat;
            bool isWoodLog = displayMat == MaterialIdCatalog.OakWood || displayMat == MaterialIdCatalog.Ironwood;
            byte meshShape = shape == VoxelBitUtility.ChiseledMicroVoxelShapeId ? (byte)63 : (isWoodLog ? (byte)7 : (byte)1);

            int meshKey = (displayMat << 8) | meshShape;
            if (!_droppedItemMeshCache.TryGetValue(meshKey, out Mesh? itemMesh) || itemMesh == null)
            {
                _dropItemTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), displayMat, meshShape, 0, 0, _dropItemTris);
                itemMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_dropItemTris, $"Mesh_DroppedItem_{displayMat}_{meshShape}");
                _droppedItemMeshCache[meshKey] = itemMesh;
            }

            string objName = !string.IsNullOrEmpty(customLabel) ? $"DroppedLoot_{customLabel}" : $"DroppedItem_{GetMaterialDisplayName(displayMat)}";
            GameObject go = new GameObject(objName);
            go.AddComponent<MeshFilter>().sharedMesh = itemMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (_lastSharedVoxelMat != null) mr.sharedMaterial = _lastSharedVoxelMat;

            go.transform.position = worldPos;
            go.transform.localScale = isWoodLog ? new Vector3(0.28f, 0.44f, 0.28f) : new Vector3(0.34f, 0.34f, 0.34f);

            float angle = (_droppedWorldItems.Count * 1.9f) + Time.time * 3.7f;
            Vector3 popVel = new Vector3(Mathf.Cos(angle) * 1.65f, 3.25f, Mathf.Sin(angle) * 1.65f);

            _droppedWorldItems.Add(new DroppedWorldItem
            {
                VisualGo = go,
                Position = worldPos,
                Velocity = popVel,
                MaterialId = displayMat,
                ShapeId = shape,
                Count = Mathf.Max(1, count),
                SpawnAge = 0f,
                CustomLabel = customLabel
            });
        }

        public void StepDroppedWorldItems(
            float dt, Vector3 playerPos, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (_droppedWorldItems.Count == 0) return;
            Vector3 magnetTarget = playerPos + new Vector3(0f, 0.92f, 0f);
            bool manualPickupKey = Input.GetKey(Stage1GameSettings.KeyInteract) || Input.GetKey(KeyCode.E);

            for (int i = _droppedWorldItems.Count - 1; i >= 0; i--)
            {
                DroppedWorldItem item = _droppedWorldItems[i];
                if (item.VisualGo == null)
                {
                    _droppedWorldItems.RemoveAt(i);
                    continue;
                }

                item.SpawnAge += dt;
                float distToPlayer = Vector3.Distance(item.Position, magnetTarget);
                float pickupPullRadius = manualPickupKey ? 4.5f : 2.65f;

                if (item.SpawnAge >= 0.32f && distToPlayer <= pickupPullRadius)
                {
                    Vector3 toPlayer = (magnetTarget - item.Position).normalized;
                    item.Velocity = Vector3.Lerp(item.Velocity, toPlayer * 11.5f, Mathf.Clamp01(dt * 14f));
                    item.Position += item.Velocity * dt;

                    if (distToPlayer <= 0.88f)
                    {
                        string note = HarvestBrokenVoxel(item.MaterialId, item.ShapeId, item.Count, item.CustomLabel);
                        LastFeedbackText = $"YERDEN TOPLANDI: {note}";
                        UnityEngine.Object.Destroy(item.VisualGo);
                        _droppedWorldItems.RemoveAt(i);
                        continue;
                    }
                }
                else
                {
                    item.Velocity.y -= 9.81f * dt;
                    Vector3 nextPos = item.Position + item.Velocity * dt;
                    if (TryReadNonAirVoxel(nextPos + new Vector3(0f, -0.18f, 0f), worldChunks, out Vector3Int groundVc, out _))
                    {
                        float floorY = groundVc.y + 1.18f;
                        if (nextPos.y < floorY)
                        {
                            nextPos.y = floorY;
                            item.Velocity.y = Mathf.Abs(item.Velocity.y) > 1.2f ? -item.Velocity.y * 0.28f : 0f;
                            item.Velocity.x *= 0.82f;
                            item.Velocity.z *= 0.82f;
                        }
                    }
                    item.Position = nextPos;
                }

                float bobOffset = Mathf.Sin(item.SpawnAge * 4.2f + i) * 0.04f;
                item.VisualGo.transform.position = item.Position + new Vector3(0f, bobOffset, 0f);
                item.VisualGo.transform.Rotate(0f, 110f * dt, 0f, Space.World);
                _droppedWorldItems[i] = item;
            }
        }

        private void ExecuteCrystalVacuumBrush(
            int maxY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks,
            Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            Vector3Int targetVc = AimVoxelCoord;
            if (!TryReadNonAirVoxel(new Vector3(targetVc.x + 0.5f, targetVc.y + 0.5f, targetVc.z + 0.5f), worldChunks, out _, out _) &&
                targetVc.y > 1 &&
                TryReadNonAirVoxel(new Vector3(targetVc.x + 0.5f, targetVc.y - 0.5f, targetVc.z + 0.5f), worldChunks, out Vector3Int belowVc, out _))
            {
                targetVc = belowVc;
            }

            Vector3 center = new Vector3(targetVc.x + 0.5f, targetVc.y + 0.5f, targetVc.z + 0.5f);
            float r = BrushRadiusMeters;
            int minWx = Mathf.FloorToInt(center.x - r - 0.5f), maxWx = Mathf.CeilToInt(center.x + r + 0.5f);
            int minWy = Mathf.Max(1, Mathf.FloorToInt(center.y - r - 0.5f)), maxWy = Mathf.Min(maxY - 2, Mathf.CeilToInt(center.y + r + 0.5f));
            int minWz = Mathf.FloorToInt(center.z - r - 0.5f), maxWz = Mathf.CeilToInt(center.z + r + 0.5f);
            bool forceBreak = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool hitImmuneOre = false, hitCraftedBlock = false, hitHigherTierBlock = false;
            byte neededTier = 0;
            uint vacuumedQ24_8 = 0u;
            (int cx, int cz) touched = (FloorDiv16(targetVc.x), FloorDiv16(targetVc.z));

            for (int wy = minWy; wy <= maxWy; wy++)
            for (int wz = minWz; wz <= maxWz; wz++)
            for (int wx = minWx; wx <= maxWx; wx++)
            {
                float dist = Vector3.Distance(new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), center);
                if (dist > r) continue;
                int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
                if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) continue;
                int idx = (wy << 8) | (lz << 4) | lx;
                uint curRaw = voxels[idx];
                byte curMat = VoxelBitUtility.GetMaterialId(curRaw);
                if (curMat == MaterialIdCatalog.Air || curMat == MaterialIdCatalog.Bedrock) continue;
                if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(curMat)) { hitImmuneOre = true; continue; }
                // Oyuncunun kristalle veya eliyle koyduğu bloklar da kristalle (Sol Tık) doğrudan kırılıp geri çekilebilir!
                if (!CanCrystalVacuumMaterial(curMat, out byte reqTier))
                {
                    if (reqTier > CurrentCrystalTier) { hitHigherTierBlock = true; neededTier = reqTier; }
                    continue;
                }

                byte depositMat = curMat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : curMat;
                byte dmg4 = VoxelBitUtility.GetDamageState(curRaw);
                byte curShape = VoxelBitUtility.GetShapeId(curRaw);
                if (curShape != 0 || forceBreak || r <= 1.25f || (wx == targetVc.x && wy == targetVc.y && wz == targetVc.z))
                {
                    voxels[idx] = 0u;
                    if (curShape == 0)
                    {
                        CarveSmoothSdfCraterNeighbors(wx, wy, wz, maxY, worldChunks, dirty);
                    }
                    CrystalStorage.TryDepositMassQ24_8(depositMat, CrystalMassStorageData.OneBlockQ24_8, dmg4);
                    vacuumedQ24_8 += CrystalMassStorageData.OneBlockQ24_8;
                }
                else
                {
                    byte curDens = VoxelBitUtility.GetDensityOrLiquid(curRaw);
                    int delta = Mathf.Max(14, Mathf.RoundToInt((96f / GetMaterialHardness(curMat)) * (1f - dist / Mathf.Max(0.4f, r))));
                    byte nextDens = (curDens - delta) < 28 ? (byte)0 : (byte)(curDens - delta);
                    uint gained = CrystalMassStorage.MassQ24_8FromDensity7((byte)(curDens - nextDens));
                    CrystalStorage.TryDepositMassQ24_8(depositMat, gained, dmg4);
                    vacuumedQ24_8 += gained;
                    voxels[idx] = nextDens == 0 ? 0u : VoxelBitUtility.WithDensityOrLiquid(curRaw, nextDens);
                }
                touched = (cx, cz);
                MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);
            }
            if (vacuumedQ24_8 > 0u && worldChunks.TryGetValue(touched, out uint[]? tv))
            {
                holsterCtrl?.InventoryUi.SyncWithCrystalStorage(this);
                collapseBridge.EvaluateChunkStability(touched.cx, touched.cz, tv, maxY, center, sharedMat, dirty, out string msg);
                LastFeedbackText = !string.IsNullOrEmpty(msg) ? msg : $"Vakumlandi: +{vacuumedQ24_8 / 256f:0.00} Blok ({BrushAreaLabel} | Kademe: T{CurrentCrystalTier} | Depo: {CrystalStorage.CurrentTotalBlocks})";
            }
            else if (hitHigherTierBlock)
                LastFeedbackText = $"ERR_CRYSTAL_TIER_TOO_LOW: Bu kaya icin Kristal Kademe T{neededTier} gerekli (Ors yaninda [E] ile yukseltebilirsiniz)!";
            else if (hitImmuneOre)
                LastFeedbackText = "ERR_ORE_RESONANCE_IMMUNE: Cevherler (30..46) [2] Demir Kazma ile kirilmalidir!";
            else if (hitCraftedBlock)
                LastFeedbackText = "ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN: Oyuncu yapisi bloklar icin [Shift+LMB] veya [2] Kazma kullanin!";
        }

        private void ExecuteCrystalOrRubblePlacement(
            int maxY, Vector3 playerPos, float capsuleH, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int activeHolsterSlot = 4, Stage1HolsterAndBackpackController? holsterCtrl = null)
        {
            int wx = PlacementVoxelCoord.x, wy = Mathf.Clamp(PlacementVoxelCoord.y, 1, maxY - 2), wz = PlacementVoxelCoord.z;
            if (!Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerPos, capsuleH))
            {
                // İlk aday hücre oyuncu kapsülüne değiyorsa hemen bir üst veya hedef yönündeki komşu hücreyi dene
                if (wy + 1 < maxY - 2 &&
                    !TryReadNonAirVoxel(new Vector3(wx + 0.5f, wy + 1.5f, wz + 0.5f), worldChunks, out _, out _) &&
                    Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(wx, wy + 1, wz, playerPos, capsuleH))
                {
                    wy += 1;
                }
                else
                {
                    LastFeedbackText = "ANTI-GOMULME KORUMASI: Oyuncu kapsulu icine blok konulamaz!";
                    return;
                }
            }
            int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;

            bool placingFromHandSlot = !IsCrystalActive && activeHolsterSlot >= 4;
            if (placingFromHandSlot && holsterCtrl != null)
            {
                VisualInventorySlot activeUiSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                byte slotMat = ResolveSlotBuildableMaterialId(in activeUiSlot);
                if (slotMat != MaterialIdCatalog.Air)
                {
                    SelectMaterial(slotMat);
                }
            }
            else if (IsCrystalActive && GetMaterialCountInCrystal(SelectedMaterialId) <= 0)
            {
                // Seçili malzemeden kalmadıysa kristalde stoğu olan ilk malzemeye otomatik geç
                for (int i = 0; i < BuildableMaterials.Length; i++)
                {
                    byte cand = BuildableMaterials[i];
                    if (GetMaterialCountInCrystal(cand) > 0)
                    {
                        SelectMaterial(cand);
                        break;
                    }
                }
            }

            byte mat = SelectedMaterialId;
            byte preservedDmg = 0;
            byte placeShape = (placingFromHandSlot && SelectedShapeId == 0) ? (byte)1 : SelectedShapeId;

            if (mat == MaterialIdCatalog.LooseRubble)
            {
                if (LooseRubbleShardCount <= 0 &&
                    !CrystalStorage.TryWithdrawNaturalBlock(mat, out preservedDmg) &&
                    !(placingFromHandSlot && TryConsumeOneFromActiveHotbarSlot(holsterCtrl)))
                { LastFeedbackText = "Stokta Tas Moloz yok!"; return; }
                if (LooseRubbleShardCount > 0) { LooseRubbleShardCount--; preservedDmg = 6; }
                voxels[(wy << 8) | (lz << 4) | lx] = VoxelBitUtility.PackChiseled(
                    MaterialIdCatalog.LooseRubble, (byte)(64 + ((wx * 17 + wy * 31 + wz * 13) & 0x7F)), BlokKokenDurumu.OyuncuItemBlogu, preservedDmg);
            }
            else
            {
                bool consumedFromStorage = CrystalStorage.TryWithdrawNaturalBlock(mat, out preservedDmg);
                if (!consumedFromStorage)
                {
                    if (CrystalStorage.TryWithdrawMassQ24_8(mat, 64u, out preservedDmg))
                    {
                        consumedFromStorage = true;
                    }
                }
                if (!consumedFromStorage && !(placingFromHandSlot && TryConsumeOneFromActiveHotbarSlot(holsterCtrl)))
                { LastFeedbackText = $"Depoda {GetMaterialDisplayName(mat)} kalmadi! ([Tekerlek/B] ile baska malzeme secin)"; return; }

                bool isNatural = placeShape == 0;
                BlokKokenDurumu origin = placingFromHandSlot
                    ? BlokKokenDurumu.OyuncuItemBlogu
                    : (placeShape == 0 ? BlokKokenDurumu.DogalKutle : BlokKokenDurumu.OyuncuItemBlogu);
                voxels[(wy << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                    mat, VoxelBitUtility.FullDensity, placeShape, placeShape == 0 ? (byte)0 : _selectedRotation24, origin, preservedDmg);
                if (isNatural)
                {
                    ReinforceSmoothSdfPlacementNeighbors(wx, wy, wz, maxY, mat, worldChunks, dirty);
                }
            }

            if (holsterCtrl != null)
            {
                holsterCtrl.InventoryUi.SyncWithCrystalStorage(this);
                if (placingFromHandSlot && holsterCtrl.InventoryUi.GetActiveHotbarSlot().IsEmpty)
                {
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(this);
                }
            }

            if (fluidChunks != null && fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) && fluids != null)
            {
                int sliceLen = 16 * maxY * 16;
                FluidPorosityUtility.DisplaceExcessFluidOnSolidEdit(voxels.AsSpan(0, sliceLen), fluids.AsSpan(0, sliceLen), 16, maxY, 16, lx, wy, lz);
                FluidSimulationSystem.WakeFluidNeighborhood(fluids.AsSpan(0, sliceLen), 16, maxY, 16, lx, wy, lz, radius: 2);
                dirtyFluidChunks?.Add((cx, cz));
            }
            MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
            collapseBridge.EvaluateChunkStability(cx, cz, voxels, maxY, new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), sharedMat, dirty, out string msg);
            LastFeedbackText = !string.IsNullOrEmpty(msg)
                ? msg
                : $"{GetMaterialDisplayName(mat)} ({GetShapeDisplayName(placeShape)} | Yon:{_selectedRotation24} | Hasar:{preservedDmg}/15) Konuldu! (Stres: {CurrentGhostPreviewColor})";
        }

        private static byte ResolveSlotBuildableMaterialId(in VisualInventorySlot slot)
        {
            if (slot.IsEmpty) return MaterialIdCatalog.Air;
            if (slot.Kind == InventoryItemKind.VoxelBlock || slot.Kind == InventoryItemKind.OreChunk)
            {
                return slot.SubId;
            }
            if (slot.Kind == InventoryItemKind.MetalIngot)
            {
                return slot.SubId switch
                {
                    1 => MaterialIdCatalog.CopperOre,
                    2 => MaterialIdCatalog.IronOre,
                    3 => MaterialIdCatalog.MithrilOre,
                    _ => slot.SubId > 3 ? slot.SubId : MaterialIdCatalog.IronOre
                };
            }
            return MaterialIdCatalog.Air;
        }

        private static bool TryConsumeOneFromActiveHotbarSlot(Stage1HolsterAndBackpackController? holsterCtrl)
        {
            if (holsterCtrl == null) return false;
            int idx = holsterCtrl.InventoryUi.SelectedHotbarIndex;
            if (idx < 0 || idx >= Stage1VisualHotbarAndInventoryUi.HotbarSlotCount) return false;
            ref VisualInventorySlot slot = ref holsterCtrl.InventoryUi.HotbarSlots[idx];
            if (slot.IsEmpty || slot.Count <= 0) return false;
            slot.Count--;
            if (slot.Count <= 0)
            {
                slot = default;
            }
            return true;
        }

        private static void ReinforceSmoothSdfPlacementNeighbors(
            int wx, int wy, int wz, int maxY, byte mat,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty)
        {
            ReadOnlySpan<int> dx = stackalloc int[5] { 0, 1, -1, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[5] { -1, 0, 0, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[5] { 0, 0, 0, 1, -1 };

            for (int d = 0; d < 5; d++)
            {
                int nx = wx + dx[d], ny = wy + dy[d], nz = wz + dz[d];
                if (ny <= 1 || ny >= maxY - 1) continue;
                int ncx = FloorDiv16(nx), ncz = FloorDiv16(nz);
                int nlx = nx - ncx * 16, nlz = nz - ncz * 16;
                if (!worldChunks.TryGetValue((ncx, ncz), out uint[]? nChunk)) continue;

                int nIdx = (ny << 8) | (nlz << 4) | nlx;
                uint nRaw = nChunk[nIdx];
                byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                if (dy[d] == -1)
                {
                    if (nMat != MaterialIdCatalog.Air && VoxelBitUtility.GetShapeId(nRaw) == 0)
                    {
                        nChunk[nIdx] = VoxelBitUtility.WithDensityOrLiquid(nRaw, VoxelBitUtility.FullDensity);
                        MarkChunkAndNeighborsDirty(ncx, ncz, nlx, nlz, dirty, worldChunks);
                    }
                }
                else if (nMat == MaterialIdCatalog.Air)
                {
                    nChunk[nIdx] = VoxelBitUtility.PackStandard(mat, 48, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                    MarkChunkAndNeighborsDirty(ncx, ncz, nlx, nlz, dirty, worldChunks);
                }
                else if (VoxelBitUtility.GetShapeId(nRaw) == 0)
                {
                    byte curDens = VoxelBitUtility.GetDensityOrLiquid(nRaw);
                    if (curDens < 96)
                    {
                        nChunk[nIdx] = VoxelBitUtility.WithDensityOrLiquid(nRaw, (byte)Mathf.Min(127, curDens + 32));
                        MarkChunkAndNeighborsDirty(ncx, ncz, nlx, nlz, dirty, worldChunks);
                    }
                }
            }
        }

        private static void WakeChunkFluidsOnEdit(
            int cx, int cz, int lx, int wy, int lz, int maxY,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks)
        {
            if (fluidChunks == null || !fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null) return;
            FluidSimulationSystem.WakeFluidNeighborhood(fluids.AsSpan(0, 16 * maxY * 16), 16, maxY, 16, lx, wy, lz, radius: 2);
            dirtyFluidChunks?.Add((cx, cz));
        }

        private void UpdateVacuumBeamVisual(bool active, Transform? wristTr, Material? sharedMat)
        {
            if (!active || wristTr == null) { if (_vacuumBeamGo != null) _vacuumBeamGo.SetActive(false); return; }
            if (_vacuumBeamGo == null)
            {
                _vacuumBeamGo = new GameObject("MatterCrystal_HelicalVacuumStream");
                _vacuumBeamMf = _vacuumBeamGo.AddComponent<MeshFilter>();
                var mr = _vacuumBeamGo.AddComponent<MeshRenderer>();
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
                _beamTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.AstralOre, 7, 0, 0, _beamTris);
                _vacuumBeamMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_beamTris, "Mesh_HelicalVacuumBeam", _vacuumBeamMf.sharedMesh);
            }
            _vacuumBeamGo.SetActive(true);
            Vector3 start = AimHit.point, end = wristTr.position;
            Vector3 delta = end - start;
            float dist = Mathf.Max(0.15f, delta.magnitude);
            _vacuumBeamGo.transform.position = (start + end) * 0.5f + Vector3.up * (Mathf.Sin(Time.time * 18f) * 0.04f);
            _vacuumBeamGo.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(90f, Time.time * 360f, 0f);
            _vacuumBeamGo.transform.localScale = new Vector3(0.08f, dist * 0.5f, 0.08f);
        }

        private void UpdateTargetAimProbe(UnityEngine.Camera? cam, Dictionary<(int cx, int cz), uint[]> chunks)
        {
            HasAimHit = false;
            if (cam == null || chunks == null || chunks.Count == 0) return;

            int maxY = StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1;
            Vector3 camPos = cam.transform.position;
            Vector3 fwd = cam.transform.forward.sqrMagnitude > 0.0001f ? cam.transform.forward.normalized : Vector3.forward;

            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(camPos, fwd, 24f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit = default;
            float bestDist = float.MaxValue;
            bool foundVoxelMeshHit = false;
            RaycastHit fallbackAnyHit = default;
            float fallbackAnyDist = float.MaxValue;
            bool foundAnyHit = false;

            if (hits != null)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col == null) continue;
                    string goName = col.gameObject.name;
                    if (goName.StartsWith("Dropped", StringComparison.Ordinal) ||
                        goName.StartsWith("PlacementGhost", StringComparison.Ordinal) ||
                        goName.StartsWith("MatterCrystal_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bool isVoxelChunk = goName.StartsWith("VoxelChunk_", StringComparison.Ordinal) ||
                                        (col.transform.parent != null && col.transform.parent.name.StartsWith("VoxelChunk_", StringComparison.Ordinal));
                    if (isVoxelChunk)
                    {
                        if (hits[i].distance < bestDist)
                        {
                            bestDist = hits[i].distance;
                            hit = hits[i];
                            foundVoxelMeshHit = true;
                        }
                    }
                    else if (!goName.Contains("Player", StringComparison.OrdinalIgnoreCase) && hits[i].distance < fallbackAnyDist)
                    {
                        fallbackAnyDist = hits[i].distance;
                        fallbackAnyHit = hits[i];
                        foundAnyHit = true;
                    }
                }
            }

            if (!foundVoxelMeshHit && foundAnyHit)
            {
                hit = fallbackAnyHit;
                foundVoxelMeshHit = true;
            }

            // 1) ÖNCE doğrudan 3B Voksel DDA (Amanatides & Woo) ile gerçek voksel dizisini (_worldChunks) tara:
            // Bu sayede MeshCollider gecikmesi veya krater kenarı yüzey hataları blok koymayı/kırmayı ASLA engelleyemez!
            if (TryVoxelDdaRaycast(
                    camPos, fwd, 24f, maxY, chunks,
                    out Vector3Int ddaHitVc, out uint ddaRaw, out Vector3Int ddaPrevEmptyVc,
                    out Vector3 ddaHitWorldPos, out Vector3 ddaNormal, out float ddaDist))
            {
                Vector3 exactHitPoint = (foundVoxelMeshHit && Mathf.Abs(hit.distance - ddaDist) <= 0.95f)
                    ? hit.point
                    : ddaHitWorldPos;
                Vector3 exactNormal = (foundVoxelMeshHit && Mathf.Abs(hit.distance - ddaDist) <= 0.95f && hit.normal.sqrMagnitude > 0.001f)
                    ? hit.normal.normalized
                    : ddaNormal;

                RaycastHit resolvedHit = foundVoxelMeshHit ? hit : default;
                resolvedHit.point = exactHitPoint;
                resolvedHit.normal = exactNormal;
                resolvedHit.distance = ddaDist;
                AimHit = resolvedHit;
                HasAimHit = true;

                Vector3 subProbe = exactHitPoint - exactNormal * 0.03f;
                AimSubVoxelX = Mathf.Clamp(Mathf.FloorToInt((subProbe.x - Mathf.Floor(subProbe.x)) * 4f), 0, 3);
                AimSubVoxelY = Mathf.Clamp(Mathf.FloorToInt((subProbe.y - Mathf.Floor(subProbe.y)) * 4f), 0, 3);
                AimSubVoxelZ = Mathf.Clamp(Mathf.FloorToInt((subProbe.z - Mathf.Floor(subProbe.z)) * 4f), 0, 3);

                AimVoxelCoord = ddaHitVc;
                AimMaterialId = VoxelBitUtility.GetMaterialId(ddaRaw);
                AimDensity = VoxelBitUtility.GetDensityOrLiquid(ddaRaw);
                AimDamageState = VoxelBitUtility.GetDamageState(ddaRaw);
                PlacementVoxelCoord = ddaPrevEmptyVc;
                return;
            }

            if (foundVoxelMeshHit)
            {
                HasAimHit = true;
                AimHit = hit;
                Vector3 n = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : Vector3.up;

                Vector3 subProbe = hit.point - n * 0.03f;
                AimSubVoxelX = Mathf.Clamp(Mathf.FloorToInt((subProbe.x - Mathf.Floor(subProbe.x)) * 4f), 0, 3);
                AimSubVoxelY = Mathf.Clamp(Mathf.FloorToInt((subProbe.y - Mathf.Floor(subProbe.y)) * 4f), 0, 3);
                AimSubVoxelZ = Mathf.Clamp(Mathf.FloorToInt((subProbe.z - Mathf.Floor(subProbe.z)) * 4f), 0, 3);

                Vector3Int normalStep = Mathf.Abs(n.y) >= 0.45f
                    ? new Vector3Int(0, n.y >= 0f ? 1 : -1, 0)
                    : (Mathf.Abs(n.x) >= Mathf.Abs(n.z)
                        ? new Vector3Int(n.x >= 0f ? 1 : -1, 0, 0)
                        : new Vector3Int(0, 0, n.z >= 0f ? 1 : -1));

                for (int i = 0; i < RayMarchOffsets.Length; i++)
                {
                    Vector3 pInward = hit.point - n * RayMarchOffsets[i];
                    if (!TryReadNonAirVoxel(pInward, chunks, out Vector3Int vc, out uint raw))
                    {
                        Vector3 pForward = hit.point + fwd * RayMarchOffsets[i];
                        if (!TryReadNonAirVoxel(pForward, chunks, out vc, out raw))
                        {
                            if (i <= 2)
                            {
                                Vector3 pBelow = hit.point + fwd * (RayMarchOffsets[i] * 0.5f) + Vector3.down * (0.18f + i * 0.16f);
                                if (!TryReadNonAirVoxel(pBelow, chunks, out vc, out raw)) continue;
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }

                    AimVoxelCoord = vc;
                    AimMaterialId = VoxelBitUtility.GetMaterialId(raw);
                    AimDensity = VoxelBitUtility.GetDensityOrLiquid(raw);
                    AimDamageState = VoxelBitUtility.GetDamageState(raw);
                    PlacementVoxelCoord = ResolveEmptyPlacementNeighbor(vc, normalStep, maxY, chunks);
                    return;
                }
            }

            if (foundVoxelMeshHit)
            {
                AimVoxelCoord = new Vector3Int(
                    Mathf.FloorToInt(hit.point.x),
                    Mathf.Clamp(Mathf.FloorToInt(hit.point.y - 0.35f), 1, maxY - 1),
                    Mathf.FloorToInt(hit.point.z));
                PlacementVoxelCoord = new Vector3Int(
                    AimVoxelCoord.x,
                    Mathf.Clamp(AimVoxelCoord.y + 1, 1, maxY - 1),
                    AimVoxelCoord.z);
            }
        }

        private static Vector3Int ResolveEmptyPlacementNeighbor(
            Vector3Int solidVc,
            Vector3Int normalStep,
            int maxY,
            Dictionary<(int cx, int cz), uint[]> chunks)
        {
            int safeMaxY = Mathf.Max(1, maxY - 1);
            Vector3Int step = normalStep.sqrMagnitude > 0 ? normalStep : Vector3Int.up;

            Vector3Int cand1 = new Vector3Int(
                solidVc.x + step.x,
                Mathf.Clamp(solidVc.y + step.y, 1, safeMaxY),
                solidVc.z + step.z);
            if (!TryReadNonAirVoxel(new Vector3(cand1.x + 0.5f, cand1.y + 0.5f, cand1.z + 0.5f), chunks, out _, out _))
                return cand1;

            Vector3Int cand2 = new Vector3Int(
                solidVc.x + step.x * 2,
                Mathf.Clamp(solidVc.y + step.y * 2, 1, safeMaxY),
                solidVc.z + step.z * 2);
            if (!TryReadNonAirVoxel(new Vector3(cand2.x + 0.5f, cand2.y + 0.5f, cand2.z + 0.5f), chunks, out _, out _))
                return cand2;

            Vector3Int candUp = new Vector3Int(
                solidVc.x,
                Mathf.Clamp(solidVc.y + 1, 1, safeMaxY),
                solidVc.z);
            if (!TryReadNonAirVoxel(new Vector3(candUp.x + 0.5f, candUp.y + 0.5f, candUp.z + 0.5f), chunks, out _, out _))
                return candUp;

            return new Vector3Int(
                solidVc.x,
                Mathf.Clamp(solidVc.y + 2, 1, safeMaxY),
                solidVc.z);
        }

        /// <summary>
        /// Amanatides &amp; Woo (1987) 3D Fast Voxel Traversal (DDA) algoritmasi ile dogrudan <paramref name="chunks"/> uzerinde
        /// isin yuruterek ilk kati vokseli (<paramref name="hitVc"/>) ve hemen oncesindeki bos yerlestirme hucresini (<paramref name="prevEmptyVc"/>) bulur.
        /// </summary>
        private static bool TryVoxelDdaRaycast(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            int maxY,
            Dictionary<(int cx, int cz), uint[]> chunks,
            out Vector3Int hitVc,
            out uint hitRaw,
            out Vector3Int prevEmptyVc,
            out Vector3 hitWorldPos,
            out Vector3 hitNormal,
            out float hitDist)
        {
            hitVc = default;
            hitRaw = 0u;
            prevEmptyVc = default;
            hitWorldPos = origin;
            hitNormal = Vector3.up;
            hitDist = 0f;

            Vector3 dir = direction.sqrMagnitude > 0.00001f ? direction.normalized : Vector3.forward;
            Vector3 startPos = origin + dir * 0.08f;

            int x = Mathf.FloorToInt(startPos.x);
            int y = Mathf.FloorToInt(startPos.y);
            int z = Mathf.FloorToInt(startPos.z);

            int stepX = dir.x > 0f ? 1 : (dir.x < 0f ? -1 : 0);
            int stepY = dir.y > 0f ? 1 : (dir.y < 0f ? -1 : 0);
            int stepZ = dir.z > 0f ? 1 : (dir.z < 0f ? -1 : 0);

            float tDeltaX = Mathf.Abs(dir.x) > 1e-5f ? Mathf.Abs(1f / dir.x) : float.MaxValue;
            float tDeltaY = Mathf.Abs(dir.y) > 1e-5f ? Mathf.Abs(1f / dir.y) : float.MaxValue;
            float tDeltaZ = Mathf.Abs(dir.z) > 1e-5f ? Mathf.Abs(1f / dir.z) : float.MaxValue;

            float nextBoundX = stepX > 0 ? (x + 1f) : x;
            float nextBoundY = stepY > 0 ? (y + 1f) : y;
            float nextBoundZ = stepZ > 0 ? (z + 1f) : z;

            float tMaxX = Mathf.Abs(dir.x) > 1e-5f ? (nextBoundX - startPos.x) / dir.x : float.MaxValue;
            float tMaxY = Mathf.Abs(dir.y) > 1e-5f ? (nextBoundY - startPos.y) / dir.y : float.MaxValue;
            float tMaxZ = Mathf.Abs(dir.z) > 1e-5f ? (nextBoundZ - startPos.z) / dir.z : float.MaxValue;

            int safeMaxY = Mathf.Max(1, maxY - 1);
            Vector3Int lastEmpty = new Vector3Int(x, Mathf.Clamp(y, 1, safeMaxY), z);
            Vector3 currentNormal = Vector3.up;
            float traveled = 0f;
            int maxSteps = Mathf.CeilToInt(maxDistance * 3.0f) + 8;

            for (int step = 0; step < maxSteps; step++)
            {
                if (y >= 1 && y <= maxY)
                {
                    if (TryReadNonAirVoxel(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), chunks, out Vector3Int vc, out uint raw))
                    {
                        if (traveled >= 0.12f)
                        {
                            hitVc = vc;
                            hitRaw = raw;
                            hitNormal = currentNormal;
                            hitDist = traveled + 0.08f;
                            hitWorldPos = startPos + dir * traveled;
                            prevEmptyVc = !TryReadNonAirVoxel(new Vector3(lastEmpty.x + 0.5f, lastEmpty.y + 0.5f, lastEmpty.z + 0.5f), chunks, out _, out _)
                                ? lastEmpty
                                : ResolveEmptyPlacementNeighbor(vc, new Vector3Int(Mathf.RoundToInt(currentNormal.x), Mathf.RoundToInt(currentNormal.y), Mathf.RoundToInt(currentNormal.z)), maxY, chunks);
                            return true;
                        }
                    }
                    else
                    {
                        lastEmpty = new Vector3Int(x, Mathf.Clamp(y, 1, safeMaxY), z);
                    }
                }

                if (tMaxX < tMaxY)
                {
                    if (tMaxX < tMaxZ)
                    {
                        traveled = tMaxX;
                        tMaxX += tDeltaX;
                        x += stepX;
                        currentNormal = new Vector3(-stepX, 0f, 0f);
                    }
                    else
                    {
                        traveled = tMaxZ;
                        tMaxZ += tDeltaZ;
                        z += stepZ;
                        currentNormal = new Vector3(0f, 0f, -stepZ);
                    }
                }
                else
                {
                    if (tMaxY < tMaxZ)
                    {
                        traveled = tMaxY;
                        tMaxY += tDeltaY;
                        y += stepY;
                        currentNormal = new Vector3(0f, -stepY, 0f);
                    }
                    else
                    {
                        traveled = tMaxZ;
                        tMaxZ += tDeltaZ;
                        z += stepZ;
                        currentNormal = new Vector3(0f, 0f, -stepZ);
                    }
                }

                if (traveled > maxDistance) break;
            }

            return false;
        }

        private static bool TryReadNonAirVoxel(Vector3 p, Dictionary<(int cx, int cz), uint[]> chunks, out Vector3Int vc, out uint raw)
        {
            int maxY = StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1;
            int wx = Mathf.FloorToInt(p.x), wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, maxY), wz = Mathf.FloorToInt(p.z);
            vc = new Vector3Int(wx, wy, wz);
            raw = 0u;
            int cx = FloorDiv16(wx), cz = FloorDiv16(wz);
            if (!chunks.TryGetValue((cx, cz), out uint[]? c) || c == null || c.Length == 0) return false;
            int chunkMaxY = Mathf.Max(1, (c.Length >> 8) - 1);
            if (wy > chunkMaxY)
            {
                wy = chunkMaxY;
                vc = new Vector3Int(wx, wy, wz);
            }
            raw = c[(wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16)];
            return VoxelBitUtility.IsPhysicallySolid(raw);
        }

        private void UpdatePlacementGhostVisual(bool canShow, int activeSlot, Dictionary<(int cx, int cz), uint[]> chunks, Material? sharedMat)
        {
            bool isHoldingBlock = !IsCrystalActive && (activeSlot == 4 || activeSlot >= 7);
            bool showBuild = canShow && (isHoldingBlock || activeSlot == 6 || (IsCrystalActive && Input.GetMouseButton(1)));
            bool showMineFrame = canShow && !showBuild && (activeSlot == 2 || activeSlot == 3 || activeSlot == 5 || IsCrystalActive);
            if (!showBuild && !showMineFrame) { if (_placementGhostGo != null) _placementGhostGo.SetActive(false); return; }
            if (_placementGhostGo == null)
            {
                _placementGhostGo = new GameObject("PlacementGhostPreview_Cube");
                _miningTargetWireframeGo = _placementGhostGo;
                _placementGhostMf = _placementGhostGo.AddComponent<MeshFilter>();
                var mr = _placementGhostGo.AddComponent<MeshRenderer>();
                if (sharedMat != null) mr.sharedMaterial = sharedMat;
            }
            _placementGhostGo.SetActive(true);

            bool hasBelow = TryReadNonAirVoxel(new Vector3(PlacementVoxelCoord.x + 0.5f, PlacementVoxelCoord.y - 0.5f, PlacementVoxelCoord.z + 0.5f), chunks, out _, out uint belowRaw);
            bool hasHoriz = TryReadNonAirVoxel(new Vector3(PlacementVoxelCoord.x + 1.5f, PlacementVoxelCoord.y + 0.5f, PlacementVoxelCoord.z + 0.5f), chunks, out _, out _) ||
                            TryReadNonAirVoxel(new Vector3(PlacementVoxelCoord.x - 0.5f, PlacementVoxelCoord.y + 0.5f, PlacementVoxelCoord.z + 0.5f), chunks, out _, out _) ||
                            TryReadNonAirVoxel(new Vector3(PlacementVoxelCoord.x + 0.5f, PlacementVoxelCoord.y + 0.5f, PlacementVoxelCoord.z + 1.5f), chunks, out _, out _) ||
                            TryReadNonAirVoxel(new Vector3(PlacementVoxelCoord.x + 0.5f, PlacementVoxelCoord.y + 0.5f, PlacementVoxelCoord.z - 0.5f), chunks, out _, out _);
            CurrentGhostPreviewColor = StructuralCollapseSystem.EvaluateGhostPlacementPreview(
                SelectedMaterialId, hasBelow, hasBelow ? (ushort)0 : GeodesicSupportDijkstraJob.UnreachableDistance,
                0f, hasBelow ? VoxelBitUtility.GetMaterialId(belowRaw) : MaterialIdCatalog.Air,
                hasHoriz ? (ushort)1 : GeodesicSupportDijkstraJob.UnreachableDistance, out _);

            byte stressMat = CurrentGhostPreviewColor switch
            {
                StructuralPreviewColor.GreenSafe => MaterialIdCatalog.Grass,
                StructuralPreviewColor.YellowCantilever => MaterialIdCatalog.GoldOre,
                StructuralPreviewColor.OrangeCritical => MaterialIdCatalog.CopperOre,
                _ => MaterialIdCatalog.MagmaBlock
            };
            byte mat = showBuild ? stressMat : MaterialIdCatalog.GoldOre;
            byte effectiveBuildShape = (isHoldingBlock && SelectedShapeId == 0) ? (byte)1 : SelectedShapeId;
            byte shape = showBuild ? (effectiveBuildShape == 0 ? (byte)7 : effectiveBuildShape) : (activeSlot == 5 ? (byte)1 : (byte)5);
            byte colorCode = (byte)CurrentGhostPreviewColor;
            if (_placementGhostMf != null &&
                (_lastGhostMat != mat || _lastGhostShape != shape || _lastGhostColorCode != colorCode || _lastGhostRot != _selectedRotation24 || _placementGhostMf.sharedMesh == null))
            {
                _lastGhostMat = mat; _lastGhostShape = shape; _lastGhostColorCode = colorCode; _lastGhostRot = _selectedRotation24;
                _ghostTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), mat, shape, _selectedRotation24, 0, _ghostTris);
                _placementGhostMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_ghostTris, "Mesh_PlacementGhost", _placementGhostMf.sharedMesh);
            }
            if (showBuild)
            {
                _placementGhostGo.transform.position = new Vector3(PlacementVoxelCoord.x + 0.5f, PlacementVoxelCoord.y + 0.5f, PlacementVoxelCoord.z + 0.5f);
                _placementGhostGo.transform.localScale = effectiveBuildShape == 0 ? new Vector3(0.92f, 0.72f, 0.92f) : new Vector3(0.96f, 0.96f, 0.96f);
            }
            else if (activeSlot == 5 && !IsCrystalActive)
            {
                // 0.25m x 0.25m x 0.25m Mikro-Keski Alt-Kup (Sub-Voxel) Hedef Cercevesi!
                _miningTargetWireframeGo ??= _placementGhostGo;
                _miningTargetWireframeGo.transform.position = new Vector3(
                    AimVoxelCoord.x + (AimSubVoxelX + 0.5f) * 0.25f,
                    AimVoxelCoord.y + (AimSubVoxelY + 0.5f) * 0.25f,
                    AimVoxelCoord.z + (AimSubVoxelZ + 0.5f) * 0.25f);
                _miningTargetWireframeGo.transform.localScale = Vector3.one * 0.26f;
            }
            else
            {
                bool useArea = IsCrystalActive;
                float boxScale = !useArea || _brushRadiusIdx == 0 ? 1.04f : (_brushRadiusIdx == 1 ? 2.04f : (_brushRadiusIdx == 2 ? 3.04f : 5.04f));
                Vector3 offset = (useArea && _brushRadiusIdx == 1) ? new Vector3(1.0f, 1.0f, 1.0f) : new Vector3(0.5f, 0.5f, 0.5f);
                _placementGhostGo.transform.position = new Vector3(AimVoxelCoord.x + offset.x, AimVoxelCoord.y + offset.y, AimVoxelCoord.z + offset.z);
                _placementGhostGo.transform.localScale = new Vector3(boxScale, boxScale * 0.25f, boxScale);
            }
        }

        public string FormatCrystalStockHudLine() => $"[Kristal:T{CurrentCrystalTier} | Alet:T{CurrentToolTier}(%{ToolDurability01 * 100f:0})] Tas:{CrystalStorage.GetCount(MaterialIdCatalog.Stone)} | Toprak:{CrystalStorage.GetCount(MaterialIdCatalog.Dirt)} | Bazalt:{CrystalStorage.GetCount(MaterialIdCatalog.Basalt)} | Kiriktas:{CrystalStorage.GetCount(MaterialIdCatalog.Cobblestone)} | Odun:{CrystalStorage.GetCount(MaterialIdCatalog.OakWood)} | Moloz:{LooseRubbleShardCount} | Cevher:{HarvestedOreCount}(Cu:{HarvestedCopperCount} Fe:{HarvestedIronCount} Au:{HarvestedGoldCount} Mi:{HarvestedMithrilCount})";
        public static string GetShapeDisplayName(byte s) => s switch { 0 => "0: Dogal Hal (Organik Arazi)", 1 => "1: Tam Kup Blok", 2 => "2: 45° Rampa", 3 => "3: Ic Kose", 4 => "4: Dis Kose", 5 => "5: Kemer", 6 => "6: Kilit Tasi", 7 => "7: Sekizgen Sutun", 63 => "63: Mikro-Blok", _ => $"Sekil #{s}" };
        public static float GetMaterialHardness(byte m) => m switch { MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass or MaterialIdCatalog.Sand or MaterialIdCatalog.LooseRubble or MaterialIdCatalog.GlowingMoss => 0.8f, MaterialIdCatalog.OakWood or MaterialIdCatalog.WoodPlank => 1.4f, MaterialIdCatalog.Ironwood => 2.1f, MaterialIdCatalog.Stone or MaterialIdCatalog.Cobblestone or MaterialIdCatalog.StoneBrick or MaterialIdCatalog.Marble => 2.2f, MaterialIdCatalog.CopperOre or MaterialIdCatalog.IronOre or MaterialIdCatalog.GoldOre => 3.4f, MaterialIdCatalog.Basalt or MaterialIdCatalog.MagmaBlock => 3.8f, MaterialIdCatalog.NaturalObsidian or MaterialIdCatalog.MithrilOre or MaterialIdCatalog.TitaniumOre => 6.2f, _ => 3.5f };
        public static string GetMaterialDisplayName(byte m) => m switch { MaterialIdCatalog.Air => "Hava", MaterialIdCatalog.Bedrock => "Kirilmaz Ana Kaya (Bedrock)", MaterialIdCatalog.Stone => "Ham Tas", MaterialIdCatalog.Dirt => "Toprak", MaterialIdCatalog.Grass => "Cimenli Toprak", MaterialIdCatalog.Sand => "Kum", MaterialIdCatalog.Basalt => "Bazalt", MaterialIdCatalog.NaturalObsidian => "Dogal Obsidyen", MaterialIdCatalog.Cobblestone => "Kiriktas", MaterialIdCatalog.StoneBrick => "Tas Tugla", MaterialIdCatalog.Marble => "Kadim Mermer", MaterialIdCatalog.OakWood => "Mese Odunu", MaterialIdCatalog.Ironwood => "Demiragac Odunu", MaterialIdCatalog.WoodPlank => "Ahsap Kalas", MaterialIdCatalog.GlowingMoss => "Parlayan Yosun Yapragi", MaterialIdCatalog.LooseRubble => "Tas Moloz", MaterialIdCatalog.CopperOre => "Bakir Cevheri", MaterialIdCatalog.IronOre => "Demir Cevheri", MaterialIdCatalog.GoldOre => "Altin Cevheri", MaterialIdCatalog.MithrilOre => "Mithril Cevheri", MaterialIdCatalog.AstralOre => "Astral Cevheri", MaterialIdCatalog.StarCore => "Yildiz Cekirdegi", MaterialIdCatalog.PhoenixFeatherGold => "Anka Altin-Kor Tuyu", MaterialIdCatalog.CreatureFurAsh => "Yaratik Postu", MaterialIdCatalog.BoneIvory => "Canli Kemigi/Disi", _ => $"Materyal #{m}" };
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
        private static void MarkChunkAndNeighborsDirty(int cx, int cz, int lx, int lz, HashSet<(int cx, int cz)> dirty, Dictionary<(int cx, int cz), uint[]> chunks)
        {
            dirty.Add((cx, cz));
            if (lx <= 1 && chunks.ContainsKey((cx - 1, cz))) dirty.Add((cx - 1, cz));
            if (lx >= 14 && chunks.ContainsKey((cx + 1, cz))) dirty.Add((cx + 1, cz));
            if (lz <= 1 && chunks.ContainsKey((cx, cz - 1))) dirty.Add((cx, cz - 1));
            if (lz >= 14 && chunks.ContainsKey((cx, cz + 1))) dirty.Add((cx, cz + 1));
        }
    }
}
#endif
