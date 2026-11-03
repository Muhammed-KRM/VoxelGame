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
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 3 (P2 &amp; P3) — Gercek 264-Byte Q24.8 Madde Kristali Deposu (<see cref="CrystalMassStorage"/>),
    /// Materyal Sertligi + Alet Uyumlulugu, 5-Kapili Vakum + Helisel Bezier Akis Cizgisi,
    /// Sivi Uyandirma/Itme ve 4-Renk Yapisal Stres Hayalet Onizleme Kontrolcusu.
    /// </summary>
    public sealed class Stage1MatterCrystalAndMiningController
    {
        private static readonly float[] BrushRadiusSteps = { 0.5f, 1.15f, 1.85f, 2.85f };
        private static readonly string[] BrushAreaLabels = { "1x1x1 Tek Blok", "2x2x2 Orta Alan", "3x3x3 Genis Alan", "5x5x5 Dev Kazi" };
        private static readonly byte[] BuildableMaterials =
        {
            MaterialIdCatalog.Stone, MaterialIdCatalog.Dirt, MaterialIdCatalog.Sand, MaterialIdCatalog.Basalt,
            MaterialIdCatalog.NaturalObsidian, MaterialIdCatalog.Cobblestone, MaterialIdCatalog.OakWood, MaterialIdCatalog.LooseRubble
        };
        private static readonly byte[] ShapeWheelCycle = { 0, 1, 2, 3, 4, 5, 6, 7, 63 };
        private static readonly float[] RayMarchOffsets = { 0.15f, 0.38f, 0.62f, 0.88f, 1.15f };

        public readonly CrystalMassStorage CrystalStorage = new(maxTotalBlocksCapacity: 16384, crystalTier: 4);
        public readonly MatterCrystalVacuumSystem VacuumSystem = new();
        private readonly List<FacetedVoxelTriangle> _ghostTris = new(64);
        private readonly List<FacetedVoxelTriangle> _beamTris = new(48);
        private readonly List<FacetedVoxelTriangle> _dropItemTris = new(36);
        private readonly List<DroppedWorldItem> _droppedWorldItems = new(64);
        private GameObject? _placementGhostGo, _vacuumBeamGo;
        private MeshFilter? _placementGhostMf, _vacuumBeamMf;
        private byte _lastGhostMat, _lastGhostShape, _lastGhostColorCode;
        private int _brushRadiusIdx, _materialIdx, _shapeIdx;
        private float _actionCooldown, _miningDamageAccumulator;
        private int _lastToolSwingId = -1;
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
        }

        public int ActiveDroppedItemCount => _droppedWorldItems.Count;

        public bool IsCrystalActive { get; set; }
        public byte SelectedMaterialId => BuildableMaterials[_materialIdx];
        public byte SelectedShapeId => ShapeWheelCycle[_shapeIdx];
        public byte SelectedRotation { get; private set; }
        public float BrushRadiusMeters => BrushRadiusSteps[_brushRadiusIdx];
        public string BrushAreaLabel => BrushAreaLabels[_brushRadiusIdx];
        public int LooseRubbleShardCount { get; set; } = 8;
        public int HarvestedOreCount { get; private set; }
        public int HarvestedCopperCount { get; private set; }
        public int HarvestedIronCount { get; private set; }
        public int HarvestedGoldCount { get; private set; }
        public int HarvestedMithrilCount { get; private set; }
        public StructuralPreviewColor CurrentGhostPreviewColor { get; private set; } = StructuralPreviewColor.GreenSafe;
        public string LastFeedbackText { get; set; } = "Hazir ([C] Kristal | [1-4/0] Alet & Kilif | [V] Sekil | [B/I] Malzeme)";
        public bool HasAimHit { get; private set; }
        public RaycastHit AimHit { get; private set; }
        public Vector3Int AimVoxelCoord { get; private set; }
        public Vector3Int PlacementVoxelCoord { get; private set; }
        public byte AimMaterialId { get; private set; }
        public byte AimDensity { get; private set; }
        public byte AimDamageState { get; private set; }

        public Stage1MatterCrystalAndMiningController()
        {
            const uint b = CrystalMassStorageData.OneBlockQ24_8;
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, 16u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Dirt, 12u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Sand, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Basalt, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.NaturalObsidian, 4u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Cobblestone, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.OakWood, 8u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.LooseRubble, 8u * b);
        }

        public void SelectMaterial(byte materialId) => _materialIdx = Mathf.Max(0, Array.IndexOf(BuildableMaterials, materialId));

        public void HandleHotkeysAndAim(
            float dt, UnityEngine.Camera? cam, bool backpackOpen, int activeSlot,
            Dictionary<(int cx, int cz), uint[]> worldChunks, Material? sharedMat)
        {
            if (_actionCooldown > 0f) _actionCooldown = Mathf.Max(0f, _actionCooldown - dt);
            if (!backpackOpen)
            {
                if (Input.GetKeyDown(KeyCode.C)) IsCrystalActive = !IsCrystalActive;
                if (Input.GetKeyDown(KeyCode.V))
                {
                    _shapeIdx = (_shapeIdx + 1) % ShapeWheelCycle.Length;
                    LastFeedbackText = $"Insa Modu [V]: {GetShapeDisplayName(SelectedShapeId)} (0=Dogal Arazi, 1..7=Mimari Sekil)";
                }
                if (Input.GetKeyDown(KeyCode.R)) SelectedRotation = (byte)((SelectedRotation + 1) & 3);
                if (Input.GetKeyDown(KeyCode.B))
                {
                    _materialIdx = (_materialIdx + 1) % BuildableMaterials.Length;
                    byte mat = SelectedMaterialId;
                    int cnt = mat == MaterialIdCatalog.LooseRubble ? LooseRubbleShardCount : CrystalStorage.GetCount(mat);
                    LastFeedbackText = $"Secili Malzeme: {GetMaterialDisplayName(mat)} (Stok: {cnt}) -> [4] Elinde Tut / [0/Q] Birak";
                }
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.001f || Input.GetKeyDown(KeyCode.T))
                {
                    int delta = Mathf.Abs(scroll) > 0.001f ? (scroll > 0f ? 1 : -1) : 1;
                    _brushRadiusIdx = (_brushRadiusIdx + delta + BrushRadiusSteps.Length) % BrushRadiusSteps.Length;
                    LastFeedbackText = $"Kristal Vakum Alani [Tekerlek/T]: {BrushAreaLabel} (Yaricap: {BrushRadiusMeters:0.1}m)";
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
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            bool lmb = !backpackOpen && Input.GetMouseButton(0);
            UpdateVacuumBeamVisual(IsCrystalActive && lmb && HasAimHit && AimHit.distance <= 9.5f, wristCrystalTransform, sharedMat);

            if (backpackOpen) return;

            // [2] Kazma ve [3] Balta ile fiziksel savurma carpismasi (The Forest tarzi gercek savurus darbesi)
            if (!IsCrystalActive && (activeHolsterSlot == 2 || activeHolsterSlot == 3) && combatBridge != null)
            {
                bool hasBladeVoxelHit = combatBridge.ConsumePendingToolVoxelHit(out Vector3Int bladeHitCoord, out float bladeImpactPower);
                bool hasCloseSwingStrike = !hasBladeVoxelHit &&
                                           combatBridge.SlashPhase == BallisticSlashPhase.ActiveFullArcSwing &&
                                           combatBridge.SlashProgress01 >= 0.30f &&
                                           _lastToolSwingId != combatBridge.CurrentSwingId &&
                                           HasAimHit && AimHit.distance <= 3.85f;

                if (hasBladeVoxelHit || hasCloseSwingStrike)
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
                        fluidChunks, dirtyFluidChunks, targetCoord, impactMult);
                    return;
                }
            }

            if (!HasAimHit || _actionCooldown > 0f || AimHit.distance > 9.5f) return;
            bool rmb = Input.GetMouseButtonDown(1) || (IsCrystalActive && Input.GetMouseButton(1));
            if (rmb && (activeHolsterSlot == 4 || IsCrystalActive))
            {
                _actionCooldown = 0.14f;
                ExecuteCrystalOrRubblePlacement(
                    maxEditableY, playerPosition, playerCapsuleHeight, worldChunks, dirtyChunks,
                    collapseBridge, sharedMat, fluidChunks, dirtyFluidChunks);
                return;
            }
            if (!lmb) return;

            if (IsCrystalActive)
            {
                _actionCooldown = 0.09f;
                ExecuteCrystalVacuumBrush(maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat, fluidChunks, dirtyFluidChunks);
            }
            else if (activeHolsterSlot == 0 && AimHit.distance <= 3.5f)
            {
                _actionCooldown = 0.16f;
                ExecutePhysicalToolStrike(
                    activeHolsterSlot, maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat,
                    fluidChunks, dirtyFluidChunks, AimVoxelCoord, impactMultiplier: 0.55f);
            }
            else if ((activeHolsterSlot == 2 || activeHolsterSlot == 3) && AimHit.distance <= 3.85f &&
                     (combatBridge == null || combatBridge.SlashPhase == BallisticSlashPhase.IdleInGuard))
            {
                LastFeedbackText = "FIZIKSEL VURUS GEREKLI: Aleti savurmak icin [LMB] basili tutarak fareyi savurun! (Uzaktan cekim icin [C] Kristal)";
            }
        }

        private void ExecutePhysicalToolStrike(
            int slot, int maxY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks,
            Vector3Int targetCoord, float impactMultiplier = 1.0f)
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

            bool isWood = mat == MaterialIdCatalog.OakWood || mat == MaterialIdCatalog.Ironwood || mat == MaterialIdCatalog.WoodPlank;
            bool isSoftEarth = mat == MaterialIdCatalog.Dirt || mat == MaterialIdCatalog.Grass ||
                               mat == MaterialIdCatalog.Sand || mat == MaterialIdCatalog.LooseRubble ||
                               mat == MaterialIdCatalog.GlowingMoss;

            float toolPower;
            if (slot == 0)
            {
                if (!isSoftEarth && !isWood)
                {
                    LastFeedbackText = $"BOS EL [0]: {GetMaterialDisplayName(mat)} ciplak elle kirilamaz! [2] Demir Kazma cekin.";
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
                toolPower = isWood ? 36.0f : 18.0f;
            }
            else
            {
                toolPower = isWood ? 14.0f : 26.0f;
            }

            float hardness = GetMaterialHardness(mat);
            float strikeGain = slot == 0
                ? (toolPower / hardness) * 0.14f
                : (toolPower / hardness) * 0.34f * Mathf.Max(0.75f, impactMultiplier);
            _miningDamageAccumulator += strikeGain;
            int dmgStep = Mathf.FloorToInt(_miningDamageAccumulator);
            if (dmgStep <= 0)
            {
                AimDamageState = VoxelBitUtility.GetDamageState(raw);
                LastFeedbackText = $"{GetMaterialDisplayName(mat)} Darbe Aliyor... Hasar: {AimDamageState}/15 (Sertlik: {hardness:0.1})";
                return;
            }
            _miningDamageAccumulator -= dmgStep;

            int nextDmg = VoxelBitUtility.GetDamageState(raw) + dmgStep;
            if (nextDmg < 15)
            {
                voxels[idx] = VoxelBitUtility.WithDamageState(raw, (byte)nextDmg);
                AimDamageState = (byte)nextDmg;
                MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                LastFeedbackText = $"FIZIKSEL DARBE! {GetMaterialDisplayName(mat)} Catliyor... Hasar: {AimDamageState}/15 (+{dmgStep})";
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
                int nx = wx + dx[d];
                int ny = wy + dy[d];
                int nz = wz + dz[d];
                if (ny <= 1 || ny >= maxY - 1) continue;

                int ncx = FloorDiv16(nx);
                int ncz = FloorDiv16(nz);
                int nlx = nx - ncx * 16;
                int nlz = nz - ncz * 16;
                if (!worldChunks.TryGetValue((ncx, ncz), out uint[]? nChunk)) continue;

                int nIdx = (ny << 8) | (nlz << 4) | nlx;
                uint nRaw = nChunk[nIdx];
                byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                if (nMat == MaterialIdCatalog.Air || nMat == MaterialIdCatalog.Bedrock) continue;
                if (VoxelBitUtility.GetShapeId(nRaw) != 0) continue;

                if (dy[d] == 1 && !VoxelBitUtility.IsPhysicallySolid(nRaw))
                {
                    // Ustteki gorunmez SDF hava-gecis bandini temizle ki krater aninda acilsin
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

        private string HarvestBrokenVoxel(byte mat, byte shape, int count = 1)
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
                return $"+{safeCount} {GetMaterialDisplayName(mat)} (Cevher Kesesi: {HarvestedOreCount})";
            }
            byte depositMat = mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat;
            CrystalStorage.TryDepositMassQ24_8(depositMat, (uint)safeCount * CrystalMassStorageData.OneBlockQ24_8);
            string rubbleSuffix = shape == VoxelBitUtility.ChiseledMicroVoxelShapeId ? " (Molozdan)" : string.Empty;
            return $"+{safeCount} {GetMaterialDisplayName(depositMat)}{rubbleSuffix} Envantere Alindi";
        }

        public void SpawnDroppedWorldItem(Vector3 worldPos, byte mat, byte shape, int count, Material? sharedMat)
        {
            if (mat == MaterialIdCatalog.Air) return;
            if (_droppedWorldItems.Count >= 48)
            {
                if (_droppedWorldItems[0].VisualGo != null) UnityEngine.Object.Destroy(_droppedWorldItems[0].VisualGo);
                _droppedWorldItems.RemoveAt(0);
            }

            byte displayMat = mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat;
            bool isWoodLog = displayMat == MaterialIdCatalog.OakWood || displayMat == MaterialIdCatalog.Ironwood;
            byte meshShape = shape == VoxelBitUtility.ChiseledMicroVoxelShapeId ? (byte)63 : (isWoodLog ? (byte)7 : (byte)1);

            _dropItemTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), displayMat, meshShape, 0, 0, _dropItemTris);
            Mesh itemMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_dropItemTris, $"Mesh_DroppedItem_{displayMat}");

            GameObject go = new GameObject($"DroppedItem_{GetMaterialDisplayName(displayMat)}");
            go.AddComponent<MeshFilter>().sharedMesh = itemMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

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
                SpawnAge = 0f
            });
        }

        public void StepDroppedWorldItems(
            float dt, Vector3 playerPos, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (_droppedWorldItems.Count == 0) return;
            Vector3 magnetTarget = playerPos + new Vector3(0f, 0.92f, 0f);
            bool manualPickupKey = Input.GetKey(KeyCode.E);

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
                        string note = HarvestBrokenVoxel(item.MaterialId, item.ShapeId, item.Count);
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
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks)
        {
            Vector3 center = new Vector3(AimVoxelCoord.x + 0.5f, AimVoxelCoord.y + 0.5f, AimVoxelCoord.z + 0.5f);
            float r = BrushRadiusMeters;
            int minWx = Mathf.FloorToInt(center.x - r - 0.5f), maxWx = Mathf.CeilToInt(center.x + r + 0.5f);
            int minWy = Mathf.Max(1, Mathf.FloorToInt(center.y - r - 0.5f)), maxWy = Mathf.Min(maxY - 2, Mathf.CeilToInt(center.y + r + 0.5f));
            int minWz = Mathf.FloorToInt(center.z - r - 0.5f), maxWz = Mathf.CeilToInt(center.z + r + 0.5f);
            bool forceBreak = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool hitImmuneOre = false, hitCraftedBlock = false;
            uint vacuumedQ24_8 = 0u;
            (int cx, int cz) touched = (FloorDiv16(AimVoxelCoord.x), FloorDiv16(AimVoxelCoord.z));

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
                if (!forceBreak && VoxelBitUtility.GetOriginState(curRaw) == BlokKokenDurumu.OyuncuItemBlogu) { hitCraftedBlock = true; continue; }

                byte depositMat = curMat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : curMat;
                if (VoxelBitUtility.GetShapeId(curRaw) != 0 || forceBreak || r <= 0.6f)
                {
                    voxels[idx] = 0u;
                    CrystalStorage.TryDepositMassQ24_8(depositMat, CrystalMassStorageData.OneBlockQ24_8);
                    vacuumedQ24_8 += CrystalMassStorageData.OneBlockQ24_8;
                }
                else
                {
                    byte curDens = VoxelBitUtility.GetDensityOrLiquid(curRaw);
                    int delta = Mathf.Max(14, Mathf.RoundToInt((96f / GetMaterialHardness(curMat)) * (1f - dist / Mathf.Max(0.4f, r))));
                    byte nextDens = (curDens - delta) < 28 ? (byte)0 : (byte)(curDens - delta);
                    uint gained = CrystalMassStorage.MassQ24_8FromDensity7((byte)(curDens - nextDens));
                    CrystalStorage.TryDepositMassQ24_8(depositMat, gained);
                    vacuumedQ24_8 += gained;
                    voxels[idx] = nextDens == 0 ? 0u : VoxelBitUtility.WithDensityOrLiquid(curRaw, nextDens);
                }
                touched = (cx, cz);
                MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                WakeChunkFluidsOnEdit(cx, cz, lx, wy, lz, maxY, fluidChunks, dirtyFluidChunks);
            }
            if (vacuumedQ24_8 > 0u && worldChunks.TryGetValue(touched, out uint[]? tv))
            {
                collapseBridge.EvaluateChunkStability(touched.cx, touched.cz, tv, maxY, center, sharedMat, dirty, out string msg);
                LastFeedbackText = !string.IsNullOrEmpty(msg) ? msg : $"Vakumlandi: +{vacuumedQ24_8 / 256f:0.00} Blok ({BrushAreaLabel} | Depo: {CrystalStorage.CurrentTotalBlocks})";
            }
            else if (hitImmuneOre) LastFeedbackText = "ERR_ORE_RESONANCE_IMMUNE: Cevherler (30..46) [2] Demir Kazma ile kirilmalidir!";
            else if (hitCraftedBlock) LastFeedbackText = "ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN: Oyuncu yapisi bloklar icin [Shift+LMB] veya [2] Kazma kullanin!";
        }

        private void ExecuteCrystalOrRubblePlacement(
            int maxY, Vector3 playerPos, float capsuleH, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks, HashSet<(int cx, int cz)>? dirtyFluidChunks)
        {
            int wx = PlacementVoxelCoord.x, wy = Mathf.Clamp(PlacementVoxelCoord.y, 1, maxY - 2), wz = PlacementVoxelCoord.z;
            if (!Stage1PlayerMovementBridge.CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerPos, capsuleH))
            { LastFeedbackText = "ANTI-GOMULME KORUMASI: Oyuncu kapsulu icine blok konulamaz!"; return; }
            int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;
            byte mat = SelectedMaterialId;
            if (mat == MaterialIdCatalog.LooseRubble)
            {
                if (LooseRubbleShardCount <= 0 && !CrystalStorage.TryWithdrawNaturalBlock(mat, out _)) { LastFeedbackText = "Stokta Tas Moloz yok!"; return; }
                if (LooseRubbleShardCount > 0) LooseRubbleShardCount--;
                voxels[(wy << 8) | (lz << 4) | lx] = VoxelBitUtility.PackChiseled(MaterialIdCatalog.LooseRubble, (byte)(64 + ((wx * 17 + wy * 31 + wz * 13) & 0x7F)), BlokKokenDurumu.OyuncuItemBlogu, 6);
            }
            else
            {
                if (!CrystalStorage.TryWithdrawNaturalBlock(mat, out byte dmg)) { LastFeedbackText = $"Depoda {GetMaterialDisplayName(mat)} kalmadi!"; return; }
                bool isNatural = SelectedShapeId == 0;
                voxels[(wy << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(mat, VoxelBitUtility.FullDensity, SelectedShapeId, isNatural ? (byte)0 : SelectedRotation, isNatural ? BlokKokenDurumu.DogalKutle : BlokKokenDurumu.OyuncuItemBlogu, dmg);
                if (isNatural && wy > 1)
                {
                    int belowIdx = ((wy - 1) << 8) | (lz << 4) | lx;
                    uint belowRaw = voxels[belowIdx];
                    if (VoxelBitUtility.GetMaterialId(belowRaw) != MaterialIdCatalog.Air && VoxelBitUtility.GetShapeId(belowRaw) == 0)
                        voxels[belowIdx] = VoxelBitUtility.WithDensityOrLiquid(belowRaw, VoxelBitUtility.FullDensity);
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
            LastFeedbackText = !string.IsNullOrEmpty(msg) ? msg : $"{GetMaterialDisplayName(mat)} ({GetShapeDisplayName(SelectedShapeId)}) Konuldu! (Stres: {CurrentGhostPreviewColor})";
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
            if (cam == null || !UnityEngine.Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 24f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            HasAimHit = true; AimHit = hit;
            Vector3 fwd = cam.transform.forward;

            // Dual Surface Nets yuzeyi (IsoLevel=64) grid noktasinin 0.2..0.7m uzerinde olustugu icin
            // egik ucgen normali (-hit.normal) yerine dogrudan bakis ekseni (fwd) ve yuzeyin hemen altindaki kati voksel taranir.
            for (int i = 0; i < RayMarchOffsets.Length; i++)
            {
                Vector3 p = hit.point + fwd * RayMarchOffsets[i];
                if (!TryReadNonAirVoxel(p, chunks, out Vector3Int vc, out uint raw))
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
                AimVoxelCoord = vc; AimMaterialId = VoxelBitUtility.GetMaterialId(raw);
                AimDensity = VoxelBitUtility.GetDensityOrLiquid(raw); AimDamageState = VoxelBitUtility.GetDamageState(raw);
                Vector3Int step = Mathf.Abs(hit.normal.y) >= 0.45f ? new Vector3Int(0, hit.normal.y >= 0f ? 1 : -1, 0)
                    : (Mathf.Abs(hit.normal.x) >= Mathf.Abs(hit.normal.z) ? new Vector3Int(hit.normal.x >= 0f ? 1 : -1, 0, 0) : new Vector3Int(0, 0, hit.normal.z >= 0f ? 1 : -1));
                Vector3Int cand = vc + step;
                PlacementVoxelCoord = !TryReadNonAirVoxel(new Vector3(cand.x + 0.5f, cand.y + 0.5f, cand.z + 0.5f), chunks, out _, out _) ? cand : new Vector3Int(vc.x, Mathf.Min(28, vc.y + 1), vc.z);
                return;
            }
            AimVoxelCoord = new Vector3Int(Mathf.FloorToInt(hit.point.x), Mathf.Clamp(Mathf.FloorToInt(hit.point.y - 0.35f), 1, 28), Mathf.FloorToInt(hit.point.z));
            PlacementVoxelCoord = new Vector3Int(AimVoxelCoord.x, AimVoxelCoord.y + 1, AimVoxelCoord.z);
        }

        private static bool TryReadNonAirVoxel(Vector3 p, Dictionary<(int cx, int cz), uint[]> chunks, out Vector3Int vc, out uint raw)
        {
            int wx = Mathf.FloorToInt(p.x), wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, 29), wz = Mathf.FloorToInt(p.z);
            vc = new Vector3Int(wx, wy, wz); raw = 0u;
            int cx = FloorDiv16(wx), cz = FloorDiv16(wz);
            if (!chunks.TryGetValue((cx, cz), out uint[]? c)) return false;
            raw = c[(wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16)];
            // Gorunmez SDF hava-gecis bandi (density < 64, shape == 0) kazma hedefi olarak secilmez!
            return VoxelBitUtility.IsPhysicallySolid(raw);
        }

        private void UpdatePlacementGhostVisual(bool canShow, int activeSlot, Dictionary<(int cx, int cz), uint[]> chunks, Material? sharedMat)
        {
            bool showBuild = canShow && (activeSlot == 4 || (IsCrystalActive && Input.GetMouseButton(1)));
            bool showMineFrame = canShow && !showBuild && (activeSlot == 2 || activeSlot == 3 || IsCrystalActive);
            if (!showBuild && !showMineFrame) { if (_placementGhostGo != null) _placementGhostGo.SetActive(false); return; }
            if (_placementGhostGo == null)
            {
                _placementGhostGo = new GameObject("PlacementGhostPreview_Cube");
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
            byte shape = showBuild ? (SelectedShapeId == 0 ? (byte)7 : SelectedShapeId) : (byte)5;
            byte colorCode = (byte)CurrentGhostPreviewColor;
            if (_placementGhostMf != null && (_lastGhostMat != mat || _lastGhostShape != shape || _lastGhostColorCode != colorCode || _placementGhostMf.sharedMesh == null))
            {
                _lastGhostMat = mat; _lastGhostShape = shape; _lastGhostColorCode = colorCode; _ghostTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), mat, shape, SelectedRotation, 0, _ghostTris);
                _placementGhostMf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_ghostTris, "Mesh_PlacementGhost", _placementGhostMf.sharedMesh);
            }
            if (showBuild)
            {
                _placementGhostGo.transform.position = new Vector3(PlacementVoxelCoord.x + 0.5f, PlacementVoxelCoord.y + 0.5f, PlacementVoxelCoord.z + 0.5f);
                _placementGhostGo.transform.localScale = SelectedShapeId == 0 ? new Vector3(0.92f, 0.72f, 0.92f) : new Vector3(0.96f, 0.96f, 0.96f);
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

        public string FormatCrystalStockHudLine() => $"Tas:{CrystalStorage.GetCount(MaterialIdCatalog.Stone)} | Toprak:{CrystalStorage.GetCount(MaterialIdCatalog.Dirt)} | Bazalt:{CrystalStorage.GetCount(MaterialIdCatalog.Basalt)} | Kiriktas:{CrystalStorage.GetCount(MaterialIdCatalog.Cobblestone)} | Odun:{CrystalStorage.GetCount(MaterialIdCatalog.OakWood)} | Moloz:{LooseRubbleShardCount} | Cevher:{HarvestedOreCount}(Cu:{HarvestedCopperCount} Fe:{HarvestedIronCount} Au:{HarvestedGoldCount} Mi:{HarvestedMithrilCount})";
        public static string GetShapeDisplayName(byte s) => s switch { 0 => "0: Dogal Hal (Organik Arazi)", 1 => "1: Tam Kup Blok", 2 => "2: 45° Rampa", 3 => "3: Ic Kose", 4 => "4: Dis Kose", 5 => "5: Kemer", 6 => "6: Kilit Tasi", 7 => "7: Sekizgen Sutun", 63 => "63: Mikro-Blok", _ => $"Sekil #{s}" };
        public static float GetMaterialHardness(byte m) => m switch { MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass or MaterialIdCatalog.Sand or MaterialIdCatalog.LooseRubble or MaterialIdCatalog.GlowingMoss => 0.8f, MaterialIdCatalog.OakWood or MaterialIdCatalog.WoodPlank => 1.4f, MaterialIdCatalog.Ironwood => 2.1f, MaterialIdCatalog.Stone or MaterialIdCatalog.Cobblestone or MaterialIdCatalog.StoneBrick or MaterialIdCatalog.Marble => 2.2f, MaterialIdCatalog.CopperOre or MaterialIdCatalog.IronOre or MaterialIdCatalog.GoldOre => 3.4f, MaterialIdCatalog.Basalt or MaterialIdCatalog.MagmaBlock => 3.8f, MaterialIdCatalog.NaturalObsidian or MaterialIdCatalog.MithrilOre or MaterialIdCatalog.TitaniumOre => 6.2f, _ => 3.5f };
        public static string GetMaterialDisplayName(byte m) => m switch { MaterialIdCatalog.Air => "Hava", MaterialIdCatalog.Bedrock => "Kirilmaz Ana Kaya (Bedrock)", MaterialIdCatalog.Stone => "Ham Tas", MaterialIdCatalog.Dirt => "Toprak", MaterialIdCatalog.Grass => "Cimenli Toprak", MaterialIdCatalog.Sand => "Kum", MaterialIdCatalog.Basalt => "Bazalt", MaterialIdCatalog.NaturalObsidian => "Dogal Obsidyen", MaterialIdCatalog.Cobblestone => "Kiriktas", MaterialIdCatalog.StoneBrick => "Tas Tugla", MaterialIdCatalog.Marble => "Kadim Mermer", MaterialIdCatalog.OakWood => "Mese Odunu", MaterialIdCatalog.Ironwood => "Demiragac Odunu", MaterialIdCatalog.WoodPlank => "Ahsap Kalas", MaterialIdCatalog.GlowingMoss => "Parlayan Yosun Yapragi", MaterialIdCatalog.LooseRubble => "Tas Moloz", MaterialIdCatalog.CopperOre => "Bakir Cevheri", MaterialIdCatalog.IronOre => "Demir Cevheri", MaterialIdCatalog.GoldOre => "Altin Cevheri", MaterialIdCatalog.MithrilOre => "Mithril Cevheri", _ => $"Materyal #{m}" };
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
