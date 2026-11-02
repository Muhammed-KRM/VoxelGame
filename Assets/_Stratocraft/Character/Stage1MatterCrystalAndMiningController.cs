#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 3 — Gercek 264-Byte Q24.8 Madde Kristali Deposu (<see cref="CrystalMassStorage"/>),
    /// Scroll/T Kirma Alani Secimi (1x1x1..5x5x5), V Tusu Dogal/Mimari Sekil Secimi, B/I Malzeme ve Q Birakma Kontrolcusu.
    /// </summary>
    public sealed class Stage1MatterCrystalAndMiningController
    {
        private static readonly float[] BrushRadiusSteps = { 0.5f, 1.15f, 1.85f, 2.85f };
        private static readonly string[] BrushAreaLabels = { "1x1x1 Tek Blok", "2x2x2 Orta Alan", "3x3x3 Genis Alan", "5x5x5 Dev Kazi" };
        private static readonly byte[] BuildableMaterials = { MaterialIdCatalog.Stone, MaterialIdCatalog.Dirt, MaterialIdCatalog.Sand, MaterialIdCatalog.Basalt, MaterialIdCatalog.NaturalObsidian, MaterialIdCatalog.Cobblestone, MaterialIdCatalog.OakWood, MaterialIdCatalog.LooseRubble };
        private static readonly byte[] ShapeWheelCycle = { 0, 1, 2, 3, 4, 5, 6, 7, 63 };
        private static readonly float[] RayMarchOffsets = { 0.15f, 0.38f, 0.62f, 0.88f, 1.15f };

        public readonly CrystalMassStorage CrystalStorage = new(maxTotalBlocksCapacity: 16384, crystalTier: 4);
        public readonly MatterCrystalVacuumSystem VacuumSystem = new();
        private readonly List<FacetedVoxelTriangle> _ghostTris = new(64);
        private GameObject? _placementGhostGo;
        private MeshFilter? _placementGhostMf;
        private byte _lastGhostMat, _lastGhostShape;
        private int _brushRadiusIdx, _materialIdx, _shapeIdx;
        private float _actionCooldown, _miningDamageAccumulator;

        public bool IsCrystalActive { get; set; }
        public byte SelectedMaterialId => BuildableMaterials[_materialIdx];
        public byte SelectedShapeId => ShapeWheelCycle[_shapeIdx];
        public byte SelectedRotation { get; private set; }
        public float BrushRadiusMeters => BrushRadiusSteps[_brushRadiusIdx];
        public string BrushAreaLabel => BrushAreaLabels[_brushRadiusIdx];
        public int LooseRubbleShardCount { get; set; } = 12;
        public int HarvestedOreCount { get; private set; }
        public string LastFeedbackText { get; set; } = "Hazir (Tekerlek/T: Kirma Alani | V: Dogal/Sekil | B/I: Malzeme | Q: Elimden Birak)";
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
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, 64u * b); CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Dirt, 32u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Sand, 16u * b); CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Basalt, 16u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.NaturalObsidian, 8u * b); CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Cobblestone, 16u * b);
            CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.OakWood, 16u * b); CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.LooseRubble, 12u * b);
        }

        public void SelectMaterial(byte materialId) => _materialIdx = Mathf.Max(0, Array.IndexOf(BuildableMaterials, materialId));

        public void HandleHotkeysAndAim(float dt, UnityEngine.Camera? cam, bool backpackOpen, int activeSlot,
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
                    LastFeedbackText = $"Secili Malzeme: {GetMaterialDisplayName(mat)} (Stok: {cnt}) -> [4] Elinde Tut / [Q] Birak";
                }
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.001f || Input.GetKeyDown(KeyCode.T))
                {
                    int delta = Mathf.Abs(scroll) > 0.001f ? (scroll > 0f ? 1 : -1) : 1;
                    _brushRadiusIdx = (_brushRadiusIdx + delta + BrushRadiusSteps.Length) % BrushRadiusSteps.Length;
                    LastFeedbackText = $"Kirma/Kazma Alani [Tekerlek/T]: {BrushAreaLabel} (Yaricap: {BrushRadiusMeters:0.1}m)";
                }
            }
            UpdateTargetAimProbe(cam, worldChunks);
            UpdatePlacementGhostVisual(!backpackOpen && HasAimHit && AimHit.distance <= 8.5f, activeSlot, sharedMat);
        }

        public void ExecuteMiningOrBuilding(float dt, int activeHolsterSlot, bool backpackOpen, int maxEditableY, Vector3 playerPosition,
            float playerCapsuleHeight, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat)
        {
            if (backpackOpen || !HasAimHit || _actionCooldown > 0f || AimHit.distance > 9.5f) return;
            bool lmb = Input.GetMouseButton(0), rmb = Input.GetMouseButtonDown(1) || (IsCrystalActive && Input.GetMouseButton(1));
            if (rmb && (activeHolsterSlot == 4 || IsCrystalActive))
            {
                _actionCooldown = 0.14f;
                ExecuteCrystalOrRubblePlacement(maxEditableY, playerPosition, playerCapsuleHeight, worldChunks, dirtyChunks, collapseBridge, sharedMat);
                return;
            }
            if (!lmb) return;
            _actionCooldown = 0.09f;
            if (IsCrystalActive) ExecuteCrystalVacuumBrush(maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat);
            else if (activeHolsterSlot != 1)
                ExecutePhysicalToolStrike(dt, activeHolsterSlot, maxEditableY, worldChunks, dirtyChunks, collapseBridge, sharedMat);
        }

        private void ExecutePhysicalToolStrike(float dt, int slot, int maxY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat)
        {
            int cWx = AimVoxelCoord.x, cWy = Mathf.Clamp(AimVoxelCoord.y, 1, maxY - 2), cWz = AimVoxelCoord.z;
            if (!worldChunks.TryGetValue((FloorDiv16(cWx), FloorDiv16(cWz)), out uint[]? centerChunk)) return;
            byte centerMat = VoxelBitUtility.GetMaterialId(centerChunk[(cWy << 8) | ((cWz - FloorDiv16(cWz) * 16) << 4) | (cWx - FloorDiv16(cWx) * 16)]);
            if (centerMat == MaterialIdCatalog.Air || centerMat == MaterialIdCatalog.Bedrock) return;

            float toolPower = (slot == 3 && centerMat == MaterialIdCatalog.OakWood) ? 24f : (slot == 2 ? 16f : 12f);
            _miningDamageAccumulator += (toolPower / GetMaterialHardness(centerMat)) * Mathf.Max(0.10f, dt * 7f);
            int dmgStep = Mathf.Max(3, Mathf.FloorToInt(_miningDamageAccumulator));
            _miningDamageAccumulator -= dmgStep;

            int ext = _brushRadiusIdx == 0 ? 0 : (_brushRadiusIdx <= 2 ? 1 : 2);
            int brokenCount = 0, maxNextDmg = 0;
            (int cx, int cz) lastChunk = (FloorDiv16(cWx), FloorDiv16(cWz));
            for (int dy = -ext; dy <= ext; dy++)
            for (int dz = -ext; dz <= ext; dz++)
            for (int dx = -ext; dx <= ext; dx++)
            {
                if (_brushRadiusIdx == 1 && (dx < 0 || dy < 0 || dz < 0)) continue;
                int wx = cWx + dx, wy = Mathf.Clamp(cWy + dy, 1, maxY - 2), wz = cWz + dz;
                int cx = FloorDiv16(wx), cz = FloorDiv16(wz), lx = wx - cx * 16, lz = wz - cz * 16;
                if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) continue;
                int idx = (wy << 8) | (lz << 4) | lx;
                uint raw = voxels[idx];
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                if (mat == MaterialIdCatalog.Air || mat == MaterialIdCatalog.Bedrock) continue;
                int nextDmg = VoxelBitUtility.GetDamageState(raw) + dmgStep;
                if (nextDmg > maxNextDmg) maxNextDmg = nextDmg;
                if (nextDmg < 15) { voxels[idx] = VoxelBitUtility.WithDamageState(raw, (byte)nextDmg); MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks); continue; }
                voxels[idx] = 0u; brokenCount++; lastChunk = (cx, cz);
                MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
                HarvestBrokenVoxel(mat, VoxelBitUtility.GetShapeId(raw));
            }
            if (brokenCount == 0)
            {
                AimDamageState = (byte)Mathf.Min(15, maxNextDmg);
                LastFeedbackText = $"{GetMaterialDisplayName(centerMat)} Kiriliyor [{BrushAreaLabel}]... Hasar: {AimDamageState}/15";
                return;
            }
            if (worldChunks.TryGetValue(lastChunk, out uint[]? lv))
            {
                collapseBridge.EvaluateChunkStability(lastChunk.cx, lastChunk.cz, lv, maxY, new Vector3(cWx + 0.5f, cWy + 0.5f, cWz + 0.5f), sharedMat, dirty, out string msg);
                LastFeedbackText = !string.IsNullOrEmpty(msg) ? msg : $"{brokenCount} Blok Kirildi [{BrushAreaLabel}] (+{GetMaterialDisplayName(centerMat)} Depoya Eklendi)";
            }
        }

        private void HarvestBrokenVoxel(byte mat, byte shape)
        {
            if (mat == MaterialIdCatalog.LooseRubble || shape == VoxelBitUtility.ChiseledMicroVoxelShapeId) LooseRubbleShardCount += 2;
            else if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(mat))
            { HarvestedOreCount++; CrystalStorage.TryDepositMassQ24_8(MaterialIdCatalog.Stone, CrystalMassStorageData.OneBlockQ24_8); }
            else CrystalStorage.TryDepositMassQ24_8(mat == MaterialIdCatalog.Grass ? MaterialIdCatalog.Dirt : mat, CrystalMassStorageData.OneBlockQ24_8);
        }

        private void ExecuteCrystalVacuumBrush(int maxY, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat)
        {
            Vector3 center = new Vector3(AimVoxelCoord.x + 0.5f, AimVoxelCoord.y + 0.5f, AimVoxelCoord.z + 0.5f);
            float r = BrushRadiusMeters;
            int minWx = Mathf.FloorToInt(center.x - r - 0.5f), maxWx = Mathf.CeilToInt(center.x + r + 0.5f);
            int minWy = Mathf.Max(1, Mathf.FloorToInt(center.y - r - 0.5f)), maxWy = Mathf.Min(maxY - 2, Mathf.CeilToInt(center.y + r + 0.5f));
            int minWz = Mathf.FloorToInt(center.z - r - 0.5f), maxWz = Mathf.CeilToInt(center.z + r + 0.5f);
            bool forceBreak = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift), hitImmuneOre = false;
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
                if (!forceBreak && MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(curMat)) { hitImmuneOre = true; continue; }

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
                touched = (cx, cz); MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
            }
            if (vacuumedQ24_8 > 0u && worldChunks.TryGetValue(touched, out uint[]? tv))
            {
                collapseBridge.EvaluateChunkStability(touched.cx, touched.cz, tv, maxY, center, sharedMat, dirty, out string msg);
                LastFeedbackText = !string.IsNullOrEmpty(msg) ? msg : $"Vakumlandi: +{vacuumedQ24_8 / 256f:0.00} Blok ({BrushAreaLabel} | Depo: {CrystalStorage.CurrentTotalBlocks})";
            }
            else if (hitImmuneOre) LastFeedbackText = "ERR_ORE_RESONANCE_IMMUNE: Cevherler (30..46) [2] Demir Kazma ile kirilmalidir!";
        }

        private void ExecuteCrystalOrRubblePlacement(int maxY, Vector3 playerPos, float capsuleH, Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirty, Stage1StructuralCollapseBridge collapseBridge, Material? sharedMat)
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
            MarkChunkAndNeighborsDirty(cx, cz, lx, lz, dirty, worldChunks);
            collapseBridge.EvaluateChunkStability(cx, cz, voxels, maxY, new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f), sharedMat, dirty, out string msg);
            LastFeedbackText = !string.IsNullOrEmpty(msg) ? msg : $"{GetMaterialDisplayName(mat)} ({GetShapeDisplayName(SelectedShapeId)}) Konuldu! ([Q] Birak | [V] Sekil)";
        }

        private void UpdateTargetAimProbe(UnityEngine.Camera? cam, Dictionary<(int cx, int cz), uint[]> chunks)
        {
            HasAimHit = false;
            if (cam == null || !UnityEngine.Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 24f, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            HasAimHit = true; AimHit = hit;
            for (int i = 0; i < RayMarchOffsets.Length; i++)
            {
                Vector3 p = i < 3 ? (hit.point - hit.normal * RayMarchOffsets[i]) : (hit.point + cam.transform.forward * RayMarchOffsets[i]);
                if (!TryReadNonAirVoxel(p, chunks, out Vector3Int vc, out uint raw)) continue;
                AimVoxelCoord = vc; AimMaterialId = VoxelBitUtility.GetMaterialId(raw);
                AimDensity = VoxelBitUtility.GetDensityOrLiquid(raw); AimDamageState = VoxelBitUtility.GetDamageState(raw);
                Vector3Int step = Mathf.Abs(hit.normal.y) >= 0.45f ? new Vector3Int(0, hit.normal.y >= 0f ? 1 : -1, 0)
                    : (Mathf.Abs(hit.normal.x) >= Mathf.Abs(hit.normal.z) ? new Vector3Int(hit.normal.x >= 0f ? 1 : -1, 0, 0) : new Vector3Int(0, 0, hit.normal.z >= 0f ? 1 : -1));
                Vector3Int cand = vc + step;
                PlacementVoxelCoord = !TryReadNonAirVoxel(new Vector3(cand.x + 0.5f, cand.y + 0.5f, cand.z + 0.5f), chunks, out _, out _) ? cand : new Vector3Int(vc.x, Mathf.Min(28, vc.y + 1), vc.z);
                return;
            }
            AimVoxelCoord = new Vector3Int(Mathf.FloorToInt(hit.point.x), Mathf.Clamp(Mathf.FloorToInt(hit.point.y) - 1, 1, 28), Mathf.FloorToInt(hit.point.z));
            PlacementVoxelCoord = new Vector3Int(AimVoxelCoord.x, AimVoxelCoord.y + 1, AimVoxelCoord.z);
        }

        private static bool TryReadNonAirVoxel(Vector3 p, Dictionary<(int cx, int cz), uint[]> chunks, out Vector3Int vc, out uint raw)
        {
            int wx = Mathf.FloorToInt(p.x), wy = Mathf.Clamp(Mathf.FloorToInt(p.y), 1, 29), wz = Mathf.FloorToInt(p.z);
            vc = new Vector3Int(wx, wy, wz); raw = 0u;
            int cx = FloorDiv16(wx), cz = FloorDiv16(wz);
            if (!chunks.TryGetValue((cx, cz), out uint[]? c)) return false;
            raw = c[(wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16)];
            return VoxelBitUtility.GetMaterialId(raw) != MaterialIdCatalog.Air;
        }

        private void UpdatePlacementGhostVisual(bool canShow, int activeSlot, Material? sharedMat)
        {
            bool showBuild = canShow && activeSlot == 4;
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
            byte mat = showBuild ? SelectedMaterialId : MaterialIdCatalog.GoldOre;
            byte shape = showBuild ? (SelectedShapeId == 0 ? (byte)7 : SelectedShapeId) : (byte)5;
            if (_placementGhostMf != null && (_lastGhostMat != mat || _lastGhostShape != shape || _placementGhostMf.sharedMesh == null))
            {
                _lastGhostMat = mat; _lastGhostShape = shape; _ghostTris.Clear();
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
                float boxScale = _brushRadiusIdx == 0 ? 1.04f : (_brushRadiusIdx == 1 ? 2.04f : (_brushRadiusIdx == 2 ? 3.04f : 5.04f));
                Vector3 offset = _brushRadiusIdx == 1 ? new Vector3(1.0f, 1.0f, 1.0f) : new Vector3(0.5f, 0.5f, 0.5f);
                _placementGhostGo.transform.position = new Vector3(AimVoxelCoord.x + offset.x, AimVoxelCoord.y + offset.y, AimVoxelCoord.z + offset.z);
                _placementGhostGo.transform.localScale = new Vector3(boxScale, boxScale * 0.25f, boxScale);
            }
        }

        public string FormatCrystalStockHudLine() => $"Tas:{CrystalStorage.GetCount(MaterialIdCatalog.Stone)} | Toprak:{CrystalStorage.GetCount(MaterialIdCatalog.Dirt)} | Bazalt:{CrystalStorage.GetCount(MaterialIdCatalog.Basalt)} | Kiriktas:{CrystalStorage.GetCount(MaterialIdCatalog.Cobblestone)} | Odun:{CrystalStorage.GetCount(MaterialIdCatalog.OakWood)} | Moloz:{LooseRubbleShardCount} | Cevher:{HarvestedOreCount}";
        public static string GetShapeDisplayName(byte s) => s switch { 0 => "0: Dogal Hal (Organik Arazi)", 1 => "1: Tam Kup Blok", 2 => "2: 45° Rampa", 3 => "3: Ic Kose", 4 => "4: Dis Kose", 5 => "5: Kemer", 6 => "6: Kilit Tasi", 7 => "7: Sekizgen Sutun", 63 => "63: Mikro-Blok", _ => $"Sekil #{s}" };
        public static float GetMaterialHardness(byte m) => m switch { MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass or MaterialIdCatalog.Sand or MaterialIdCatalog.LooseRubble => 0.8f, MaterialIdCatalog.OakWood => 1.4f, MaterialIdCatalog.Stone or MaterialIdCatalog.Cobblestone => 2.2f, MaterialIdCatalog.Basalt or MaterialIdCatalog.MagmaBlock => 3.2f, MaterialIdCatalog.NaturalObsidian or MaterialIdCatalog.MithrilOre or MaterialIdCatalog.TitaniumOre => 5.2f, _ => 3.5f };
        public static string GetMaterialDisplayName(byte m) => m switch { MaterialIdCatalog.Air => "Hava", MaterialIdCatalog.Stone => "Ham Tas", MaterialIdCatalog.Dirt => "Toprak", MaterialIdCatalog.Grass => "Cimenli Toprak", MaterialIdCatalog.Sand => "Kum", MaterialIdCatalog.Basalt => "Bazalt", MaterialIdCatalog.NaturalObsidian => "Dogal Obsidyen", MaterialIdCatalog.Cobblestone => "Kiriktas", MaterialIdCatalog.OakWood => "Mese Odunu", MaterialIdCatalog.LooseRubble => "Tas Moloz", MaterialIdCatalog.CopperOre => "Bakir Cevheri", MaterialIdCatalog.IronOre => "Demir Cevheri", MaterialIdCatalog.GoldOre => "Altin Cevheri", MaterialIdCatalog.MithrilOre => "Mithril Cevheri", _ => $"Materyal #{m}" };
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