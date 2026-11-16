#nullable enable
using System;
#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec2 = System.Numerics.Vector2;
using SysVec3 = System.Numerics.Vector3;
#endif

namespace Stratocraft.Character
{
#if UNITY_5_3_OR_NEWER
    /// <summary>
    /// Paket 4B Faz 4B.5 (IP-5): [T] -&gt; [5] Zihin Insasi ile kurulan Fiziksel Yel Degirmeni sahne denetleyicisi.
    /// Doner ust sapka (CapYaw), 4 kanatli Betz rotoru (RotorAngle), [Sag Tik] hammadde yukleme / yelken onarimi,
    /// [E] ogutulmus un / cevher tozu toplama ve [Shift+E] mekanik firtina serit frenini sifir-GC ile yonetir.
    /// </summary>
    public sealed class Stage1WindmillController
    {
        private sealed class WindmillVisualSlot
        {
            public uint StationId;
            public GameObject? RootGo;
            public MeshFilter? TowerMf;
            public Mesh? TowerMesh;
            public Transform? CapYawPivot;
            public Transform? RotorSpinPivot;
            public MeshFilter? RotorMf;
            public Mesh? RotorMesh;
            public Material? SharedVoxelMat;
            public LocalWindSample3D CachedHubWind;
            public float WindSampleTimerSec;
            public int LastBuiltSailVisualBucket;
            public bool LastBuiltBrakeState;
            public int LastBuiltFillBucket;
        }

        public const int MaxActiveWindmills = 16;
        public const int MaxHopperBatchCapacity = 16;
        public const float HubHeightOffsetMeters = 3.20f;
        public const float MaxInteractReachMeters = 3.85f;
        public const float WindSampleIntervalSec = 0.250f;

        private readonly WindmillInstanceState[] _states = new WindmillInstanceState[MaxActiveWindmills];
        private readonly WindmillVisualSlot[] _visuals = new WindmillVisualSlot[MaxActiveWindmills];
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(512);

        private Transform? _windmillRoot;
        private Mesh? _sharedPreviewTowerMesh;
        private Mesh? _sharedPreviewRotorMesh;
        private int _activeCount;

        private GUIStyle? _promptBoxStyle;
        private GUIStyle? _promptTextStyle;

        public Stage1WindmillController()
        {
            for (int i = 0; i < MaxActiveWindmills; i++)
            {
                _visuals[i] = new WindmillVisualSlot();
            }
        }

        public int ActiveWindmillCount => _activeCount;

        public ReadOnlySpan<WindmillInstanceState> ActiveWindmillStates => _states.AsSpan(0, _activeCount);

        public WindmillInstanceState GetWindmillState(int index)
        {
            if ((uint)index >= (uint)_activeCount) return default;
            return _states[index];
        }

        public ref WindmillInstanceState GetWindmillStateRef(int index) => ref _states[index];

        /// <summary>
        /// [B] Hayatta Kalma Rehber Kitabi 3B onizlemesi icin kule + 4 kanatli rotor modelini disari verir.
        /// </summary>
        public void AppendRecipePreviewParts(List<(Mesh mesh, Matrix4x4 local)> outParts)
        {
            EnsureRootAndPreviewMeshes();
            if (_sharedPreviewTowerMesh != null)
            {
                outParts.Add((_sharedPreviewTowerMesh, Matrix4x4.TRS(
                    new Vector3(0f, -1.2f, 0f),
                    Quaternion.identity,
                    new Vector3(0.42f, 0.42f, 0.42f))));
            }

            if (_sharedPreviewRotorMesh != null)
            {
                outParts.Add((_sharedPreviewRotorMesh, Matrix4x4.TRS(
                    new Vector3(0f, -1.2f + (HubHeightOffsetMeters * 0.42f), 0f),
                    Quaternion.identity,
                    new Vector3(0.42f, 0.42f, 0.42f))));
            }
        }

        public void RegisterWindmillStation(
            uint stationId,
            in MindValidationResult res,
            WorldLooseBuildSystem? looseBuildSystem,
            Material? sharedVoxelMat)
        {
            looseBuildSystem?.RemoveAndDestroyStationParts(stationId);
            EnsureRootAndPreviewMeshes();

            int slotIdx = _activeCount < MaxActiveWindmills ? _activeCount++ : (MaxActiveWindmills - 1);
            WindmillVisualSlot vis = _visuals[slotIdx];
            if (vis.RootGo != null)
            {
                UnityEngine.Object.Destroy(vis.RootGo);
            }

            Vector3 spawnPos = new Vector3(res.ClusterCenter.X, res.ClusterBaseY, res.ClusterCenter.Z);
            SysVec3 baseSys = new SysVec3(spawnPos.x, spawnPos.y, spawnPos.z);
            _states[slotIdx] = WindmillAerodynamicsAndWorkSystem.CreateDefaultState(baseSys, initialCapYawRad: 0.0f);

            GameObject rootGo = new GameObject($"PhysicalWindmill_{stationId}");
            if (_windmillRoot != null)
            {
                rootGo.transform.SetParent(_windmillRoot, false);
            }
            rootGo.transform.position = spawnPos;
            rootGo.transform.rotation = Quaternion.identity;

            // 1) Statik Kule + Degirmen Tasi + Fren Kolu Mesi
            GameObject towerGo = new GameObject("Windmill_TowerAndMillstone");
            towerGo.transform.SetParent(rootGo.transform, false);
            MeshFilter towerMf = towerGo.AddComponent<MeshFilter>();
            MeshRenderer towerMr = towerGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null)
            {
                towerMr.sharedMaterial = sharedVoxelMat;
            }

            _meshScratch.Clear();
            PhysicalItemMeshFactory.EmitWindmillTowerTriangles(
                isBrakeEngaged: false,
                millstoneFillRatio01: 0.0f,
                _meshScratch);
            Mesh towerMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _meshScratch, $"Mesh_WindmillTower_{stationId}");
            towerMf.sharedMesh = towerMesh;

            // 2) Ust Sapka Yaw Pivoti (y = +3.20m) ve Doner 4 Kanatli Rotor Pivoti
            GameObject capPivotGo = new GameObject("Windmill_CapYawPivot");
            capPivotGo.transform.SetParent(rootGo.transform, false);
            capPivotGo.transform.localPosition = new Vector3(0f, HubHeightOffsetMeters, 0f);
            capPivotGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            GameObject rotorPivotGo = new GameObject("Windmill_RotorSpinPivot");
            rotorPivotGo.transform.SetParent(capPivotGo.transform, false);
            rotorPivotGo.transform.localPosition = Vector3.zero;
            rotorPivotGo.transform.localRotation = Quaternion.identity;

            MeshFilter rotorMf = rotorPivotGo.AddComponent<MeshFilter>();
            MeshRenderer rotorMr = rotorPivotGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null)
            {
                rotorMr.sharedMaterial = sharedVoxelMat;
            }

            _meshScratch.Clear();
            PhysicalItemMeshFactory.EmitWindmillRotorTriangles(
                sailIntegrity01: 1.0f,
                _meshScratch);
            Mesh rotorMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _meshScratch, $"Mesh_WindmillRotor_{stationId}");
            rotorMf.sharedMesh = rotorMesh;

            vis.StationId = stationId;
            vis.RootGo = rootGo;
            vis.TowerMf = towerMf;
            vis.TowerMesh = towerMesh;
            vis.CapYawPivot = capPivotGo.transform;
            vis.RotorSpinPivot = rotorPivotGo.transform;
            vis.RotorMf = rotorMf;
            vis.RotorMesh = rotorMesh;
            vis.SharedVoxelMat = sharedVoxelMat;
            vis.CachedHubWind = default;
            vis.WindSampleTimerSec = 0.0f;
            vis.LastBuiltSailVisualBucket = 2;
            vis.LastBuiltBrakeState = false;
            vis.LastBuiltFillBucket = 0;
        }

        public bool HasAimedWindmill(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            out int index)
        {
            index = -1;
            if (_activeCount <= 0) return false;

            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.0001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            Vector3 playerRef = playerPos + new Vector3(0f, 1.0f, 0f);
            float bestScore = float.MaxValue;

            for (int i = 0; i < _activeCount; i++)
            {
                WindmillVisualSlot vis = _visuals[i];
                if (vis.RootGo == null) continue;

                Vector3 basePos = vis.RootGo.transform.position;
                float horizDist = Vector2.Distance(
                    new Vector2(playerRef.x, playerRef.z),
                    new Vector2(basePos.x, basePos.z));
                if (horizDist > MaxInteractReachMeters + 0.65f) continue;

                Vector3 targetCenter = basePos + new Vector3(0f, 1.35f, 0f);
                Vector3 toCenter = targetCenter - rayOrigin;
                float along = Vector3.Dot(toCenter, rayDir);
                if (along < -0.25f || along > MaxInteractReachMeters + 1.15f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, targetCenter);
                if (perp > 1.35f) continue;

                float score = (perp * 1.8f) + (Mathf.Max(0f, along) * 0.25f);
                if (score < bestScore)
                {
                    bestScore = score;
                    index = i;
                }
            }

            return index >= 0;
        }

        public bool TryHandleInteractKeyE(
            bool isShiftHeld,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            return TryHandleInteractKeyE(cam, playerTransform, miningCtrl, holsterCtrl, combatBridge, isShiftHeld);
        }

        public bool TryHandleInteractKeyE(
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null,
            bool isShiftHeld = false)
        {
            if (!HasAimedWindmill(cam, playerTransform.position, out int idx))
            {
                return false;
            }

            ref WindmillInstanceState state = ref _states[idx];
            WindmillVisualSlot vis = _visuals[idx];

            // [Shift+E]: Mekanik Firtina Serit Frenini Kilitle / Coz
            if (isShiftHeld)
            {
                state.IsMechanicalBrakeLocked = !state.IsMechanicalBrakeLocked;
                RefreshTowerMeshIfNeeded(idx);
                miningCtrl.LastFeedbackText = state.IsMechanicalBrakeLocked
                    ? "🛑 Yel Değirmeni Mekanik Şerit Freni KİLİTLENDİ! (Fırtına aşırı-devir yelken aşınması %100 önlenir)"
                    : "🌬️ Yel Değirmeni Mekanik Freni ÇÖZÜLDÜ! (Rotor rüzgârla serbest döner ve öğütme yapar)";
                return true;
            }

            // [E]: Hazir ogutulmus un / zengin cevher tozu / kalsiyum aki tozunu topla
            if (state.ReadyOutputCount > 0)
            {
                int countToCollect = state.ReadyOutputCount;
                WindmillGrindRecipeKind recipe = state.ActiveRecipe;
                byte inputMat = state.InputItemMaterialId;
                state.ReadyOutputCount = 0;

                if (state.InputBatchCount <= 0)
                {
                    state.ActiveRecipe = WindmillGrindRecipeKind.None;
                    state.AccumulatedWorkJoules = 0.0f;
                }

                GiveMilledOutputToPlayer(
                    recipe,
                    inputMat,
                    countToCollect,
                    vis,
                    playerTransform.position,
                    miningCtrl,
                    holsterCtrl,
                    combatBridge);
                RefreshTowerMeshIfNeeded(idx);
                return true;
            }

            // Hazir cikti yoksa anlik telemetri/durum bilgisini goster
            miningCtrl.LastFeedbackText = BuildWindmillStatusString(in state, in vis.CachedHubWind);
            return true;
        }

        public bool TryLoadOrRepairAimedWindmillRightClick(
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge = null)
        {
            if (!HasAimedWindmill(cam, playerTransform.position, out int idx))
            {
                return false;
            }

            VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            if (activeSlot.IsEmpty)
            {
                return false;
            }

            ref WindmillInstanceState state = ref _states[idx];

            // 1) Yirtilmis yelken varsa (SailIntegrity01 < 0.99f) kumas / lif / yelken kanadi ile onarim
            bool isSailRepairItem = activeSlot.Kind == InventoryItemKind.PlantFiber
                || (activeSlot.Kind == InventoryItemKind.VoxelBlock &&
                    (activeSlot.SubId == MaterialIdCatalog.TacticalCloth || activeSlot.SubId == MaterialIdCatalog.LeatherPadding));
            if (state.SailIntegrity01 < 0.98f && isSailRepairItem)
            {
                if (!holsterCtrl.IsCreativeMode)
                {
                    if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1))
                    {
                        return false;
                    }
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                }

                state.SailIntegrity01 = 1.0f;
                RefreshRotorMeshIfNeeded(idx, force: true);
                miningCtrl.LastFeedbackText = "⛵ Yel Değirmeni kumaş yelken kanatları onarıldı! (Sağlamlık: %100)";
                return true;
            }

            // 2) Degirmen tasi haznesine hammadde (Tahil / Ham Cevher / Mermer-Tas) yukleme
            if (!TryClassifyInventorySlotForGrinding(in activeSlot, out WindmillGrindRecipeKind recipe, out byte matId, out string recipeTitle))
            {
                return false;
            }

            if (state.InputBatchCount > 0 && state.ActiveRecipe != WindmillGrindRecipeKind.None && state.ActiveRecipe != recipe)
            {
                miningCtrl.LastFeedbackText = "⚠️ Değirmen haznesinde farklı türde hammadde öğütülüyor! Önce mevcut işlemin bitmesini bekleyin.";
                return true;
            }

            if (state.InputBatchCount >= MaxHopperBatchCapacity)
            {
                miningCtrl.LastFeedbackText = $"⚠️ Değirmen taşı besleme hunisi dolu ({MaxHopperBatchCapacity}/{MaxHopperBatchCapacity})!";
                return true;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int desired = shiftHeld ? 4 : 1;
            int avail = holsterCtrl.IsCreativeMode ? desired : Mathf.Min(desired, activeSlot.Count);
            int toLoad = Mathf.Min(avail, MaxHopperBatchCapacity - state.InputBatchCount);
            if (toLoad <= 0) return false;

            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(toLoad))
                {
                    return false;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            state.ActiveRecipe = recipe;
            state.InputItemMaterialId = matId;
            state.InputBatchCount += toLoad;
            RefreshTowerMeshIfNeeded(idx);

            float reqJ = WindmillAerodynamicsAndWorkSystem.GetRequiredWorkJoules(recipe);
            miningCtrl.LastFeedbackText =
                $"🌾 Yel Değirmeni hunisine +{toLoad} yüklendi ({recipeTitle}, {reqJ:0} J/birim | Hazne: {state.InputBatchCount}/{MaxHopperBatchCapacity})";
            return true;
        }

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            bool uiBlocked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            RegionalAtmosphereSimulationSystem? atmosphere,
            double worldTimeSeconds,
            VoxelSolidProbeDelegate? isSolidAt,
            float workScaleMultiplier,
            out bool consumedMouseClick)
        {
            consumedMouseClick = false;
            StepAllWindmills(dt, atmosphere, worldTimeSeconds, isSolidAt, workScaleMultiplier);

            if (!uiBlocked && !miningCtrl.IsCrystalActive && Input.GetMouseButtonDown(1))
            {
                if (TryLoadOrRepairAimedWindmillRightClick(cam, playerTransform, miningCtrl, holsterCtrl, combatBridge))
                {
                    consumedMouseClick = true;
                }
            }
        }

        public void StepAllWindmills(
            float realDtSeconds,
            RegionalAtmosphereSimulationSystem? atmosphere,
            double worldTimeSeconds,
            VoxelSolidProbeDelegate? isSolidAt = null,
            float workScaleMultiplier = 1.0f)
        {
            if (_activeCount <= 0 || realDtSeconds <= 0.0f) return;

            for (int i = 0; i < _activeCount; i++)
            {
                WindmillVisualSlot vis = _visuals[i];
                if (vis.RootGo == null) continue;

                ref WindmillInstanceState state = ref _states[i];

                // 4 Hz (0.25 sn) kadansla gobek irtifasindaki (y_base + 3.2m) 3B yerel ruzgari ornekle
                vis.WindSampleTimerSec -= realDtSeconds;
                if (vis.WindSampleTimerSec <= 0.0f)
                {
                    vis.WindSampleTimerSec = WindSampleIntervalSec;
                    if (atmosphere != null)
                    {
                        vis.CachedHubWind = DynamicWindFieldSystem.EvaluateLocal3DWind(
                            atmosphere,
                            state.HubWorldPos,
                            state.BaseWorldPos.Y,
                            worldTimeSeconds,
                            isEnclosedIndoorRoom: false,
                            isSolidAt);
                    }
                    else
                    {
                        vis.CachedHubWind = new LocalWindSample3D
                        {
                            VelocityMps = new SysVec3(6.5f, 0.0f, 0.0f),
                            SpeedMps = 6.5f,
                            UpwindExposure01 = 1.0f,
                            AltitudeFactor = 1.0f,
                            InstantGustBonusMps = 0.0f
                        };
                    }
                }

                SysVec2 windDirXZ = vis.CachedHubWind.SpeedMps > 1e-3f
                    ? SysVec2.Normalize(new SysVec2(vis.CachedHubWind.VelocityMps.X, vis.CachedHubWind.VelocityMps.Z))
                    : SysVec2.Zero;

                float wakeFactor = WindmillAerodynamicsAndWorkSystem.EvaluateTurbineWakeInterferenceFactor(
                    state.HubWorldPos,
                    _states.AsSpan(0, _activeCount),
                    i,
                    windDirXZ);

                WindmillAerodynamicsAndWorkSystem.StepWindmillPhysicsAndGrinding(
                    ref state,
                    in vis.CachedHubWind,
                    wakeFactor,
                    realDtSeconds,
                    workScaleMultiplier);

                if (vis.CapYawPivot != null)
                {
                    vis.CapYawPivot.localRotation = Quaternion.Euler(0f, (-Mathf.Rad2Deg * state.CapYawAngleRad) + 90f, 0f);
                }

                if (vis.RotorSpinPivot != null)
                {
                    vis.RotorSpinPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Rad2Deg * state.RotorAngleRad);
                }

                RefreshTowerMeshIfNeeded(i);
                RefreshRotorMeshIfNeeded(i, force: false);
            }
        }

        public bool TryGetAimedWindmillHudPrompt(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            out string promptText)
        {
            if (!HasAimedWindmill(cam, playerPos, out int idx))
            {
                promptText = string.Empty;
                return false;
            }

            promptText = BuildWindmillStatusString(in _states[idx], in _visuals[idx].CachedHubWind);
            return true;
        }

        public void DrawGui(
            bool showHud,
            UnityEngine.Camera? cam,
            Transform playerTransform)
        {
            if (!showHud || _activeCount <= 0) return;
            if (!TryGetAimedWindmillHudPrompt(cam, playerTransform.position, out string statusText))
            {
                return;
            }

            EnsureGuiStyles();
            float w = Mathf.Min(760f, Screen.width - 40f);
            float h = 42f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - 142f;

            Color prev = GUI.color;
            GUI.color = new Color(0.07f, 0.11f, 0.16f, 0.88f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _promptBoxStyle!);
            GUI.color = prev;

            GUI.Label(new Rect(x + 10f, y + 5f, w - 20f, h - 10f), statusText, _promptTextStyle!);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                if (_visuals[i].RootGo != null)
                {
                    UnityEngine.Object.Destroy(_visuals[i].RootGo);
                    _visuals[i].RootGo = null;
                }
                _states[i] = default;
            }
            _activeCount = 0;
        }

        private static bool TryClassifyInventorySlotForGrinding(
            in VisualInventorySlot slot,
            out WindmillGrindRecipeKind recipe,
            out byte matId,
            out string recipeTitle)
        {
            recipe = WindmillGrindRecipeKind.None;
            matId = 0;
            recipeTitle = string.Empty;

            if (slot.Kind == InventoryItemKind.OreChunk || slot.Kind == InventoryItemKind.RoastedOreChunk)
            {
                recipe = WindmillGrindRecipeKind.RawOreToCrushedPowder;
                matId = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.IronOre;
                recipeTitle = "Ham Cevher -> Zengin Cevher Tozu";
                return true;
            }

            if (slot.Kind == InventoryItemKind.RoughStoneChunk ||
                (slot.Kind == InventoryItemKind.VoxelBlock &&
                 (slot.SubId == MaterialIdCatalog.Marble ||
                  slot.SubId == MaterialIdCatalog.Stone ||
                  slot.SubId == MaterialIdCatalog.Cobblestone)))
            {
                recipe = WindmillGrindRecipeKind.MarbleOrStoneToFluxPowder;
                matId = slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Marble;
                recipeTitle = "Mermer/Kalker -> Kalsiyum Akı Tozu";
                return true;
            }

            if (slot.Kind == InventoryItemKind.LeafPile ||
                slot.Kind == InventoryItemKind.PlantFiber ||
                slot.Kind == InventoryItemKind.FoodWildBerries ||
                (slot.Kind == InventoryItemKind.VoxelBlock && slot.SubId == MaterialIdCatalog.ThatchTile))
            {
                recipe = WindmillGrindRecipeKind.WildGrainToFlour;
                matId = MaterialIdCatalog.ThatchTile;
                recipeTitle = "Yabani Tahıl -> Öğütülmüş Un";
                return true;
            }

            return false;
        }

        private static void GiveMilledOutputToPlayer(
            WindmillGrindRecipeKind recipe,
            byte inputMatId,
            int outputCount,
            WindmillVisualSlot vis,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            Vector3 dropPos = vis.RootGo != null
                ? vis.RootGo.transform.position + new Vector3(0f, 1.15f, 0.85f)
                : playerPos + new Vector3(0f, 0.65f, 0f);

            if (recipe == WindmillGrindRecipeKind.RawOreToCrushedPowder)
            {
                byte oreSub = inputMatId != 0 ? inputMatId : MaterialIdCatalog.IronOre;
                PhysicalItemShapeDna powderDna = PhysicalItemShapeDna.CreateNatural(0x4F524550u ^ oreSub);
                bool addedPowder = holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                    InventoryItemKind.CrushedOrePowder, oreSub, outputCount, miningCtrl, powderDna);
                if (!addedPowder)
                {
                    miningCtrl.SpawnPhysicalWorldItem(
                        dropPos, InventoryItemKind.CrushedOrePowder, oreSub, outputCount, powderDna, vis.SharedVoxelMat);
                }

                PhysicalItemShapeDna stoneDna = PhysicalItemShapeDna.CreateNatural(0x53544E31u);
                bool addedStone = holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                    InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, outputCount, miningCtrl, stoneDna);
                if (!addedStone)
                {
                    miningCtrl.SpawnPhysicalWorldItem(
                        dropPos + new Vector3(0.2f, 0f, 0f),
                        InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone, outputCount, stoneDna, vis.SharedVoxelMat);
                }

                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                miningCtrl.LastFeedbackText = $"⚒️ Değirmenden toplandı: +{outputCount} Zengin Cevher Tozu ve +{outputCount} Taş Parçası";
                return;
            }

            if (recipe == WindmillGrindRecipeKind.MarbleOrStoneToFluxPowder)
            {
                PhysicalItemShapeDna fluxDna = PhysicalItemShapeDna.CreateNatural(0x464C5558u);
                bool addedFlux = holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                    InventoryItemKind.CrushedOrePowder, MaterialIdCatalog.Marble, outputCount, miningCtrl, fluxDna);
                if (!addedFlux)
                {
                    miningCtrl.SpawnPhysicalWorldItem(
                        dropPos, InventoryItemKind.CrushedOrePowder, MaterialIdCatalog.Marble, outputCount, fluxDna, vis.SharedVoxelMat);
                }

                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                miningCtrl.LastFeedbackText = $"🪨 Değirmenden toplandı: +{outputCount} Kalsiyum Akı / Harç Tozu (CrushedFluxPowder)";
                return;
            }

            PhysicalItemShapeDna flourDna = PhysicalItemShapeDna.CreateNatural(0x464C4F55u).WithStage((byte)PhysicalItemArchetype.MilledFlour);
            bool addedFlour = holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                InventoryItemKind.FoodWildBerries, MaterialIdCatalog.Marble, outputCount, miningCtrl, flourDna);
            if (!addedFlour)
            {
                miningCtrl.SpawnPhysicalWorldItem(
                    dropPos, InventoryItemKind.FoodWildBerries, MaterialIdCatalog.Marble, outputCount, flourDna, vis.SharedVoxelMat);
            }

            holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            miningCtrl.LastFeedbackText = $"🌾 Değirmenden toplandı: +{outputCount} Öğütülmüş Un (MilledFlour)";
        }

        private static string BuildWindmillStatusString(in WindmillInstanceState state, in LocalWindSample3D hubWind)
        {
            float rpm = state.AngularVelocityRadSec * (60.0f / (2.0f * MathF.PI));
            float reqJ = WindmillAerodynamicsAndWorkSystem.GetRequiredWorkJoules(state.ActiveRecipe);
            int workPct = reqJ > 0.0f
                ? Mathf.Clamp(Mathf.RoundToInt((state.AccumulatedWorkJoules / reqJ) * 100f), 0, 100)
                : 0;
            int sailPct = Mathf.Clamp(Mathf.RoundToInt(state.SailIntegrity01 * 100f), 0, 100);
            string brakeStr = state.IsMechanicalBrakeLocked ? "Açık (Kilitli)" : "Kapalı";
            return $"Yel Değirmeni | Rüzgâr: {hubWind.SpeedMps:0.0} m/s | Devir: {rpm:0} RPM | Güç: {state.LastMechanicalPowerWatts:0} W | Fren: {brakeStr} | Yelken: %{sailPct} | İş: %{workPct} (Hazır: {state.ReadyOutputCount})";
        }

        private void RefreshTowerMeshIfNeeded(int idx)
        {
            WindmillVisualSlot vis = _visuals[idx];
            if (vis.TowerMf == null || vis.TowerMesh == null) return;

            ref readonly WindmillInstanceState state = ref _states[idx];
            int fillBucket = state.InputBatchCount <= 0
                ? 0
                : Mathf.Clamp(1 + ((state.InputBatchCount * 3) / MaxHopperBatchCapacity), 1, 4);

            if (vis.LastBuiltBrakeState == state.IsMechanicalBrakeLocked &&
                vis.LastBuiltFillBucket == fillBucket)
            {
                return;
            }

            vis.LastBuiltBrakeState = state.IsMechanicalBrakeLocked;
            vis.LastBuiltFillBucket = fillBucket;

            _meshScratch.Clear();
            PhysicalItemMeshFactory.EmitWindmillTowerTriangles(
                state.IsMechanicalBrakeLocked,
                fillBucket * 0.25f,
                _meshScratch);
            HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, vis.TowerMesh.name, vis.TowerMesh);
        }

        private void RefreshRotorMeshIfNeeded(int idx, bool force)
        {
            WindmillVisualSlot vis = _visuals[idx];
            if (vis.RotorMf == null || vis.RotorMesh == null) return;

            float integrity = _states[idx].SailIntegrity01;
            int bucket = integrity <= 0.05f ? 0 : (integrity < 0.50f ? 1 : 2);
            if (!force && vis.LastBuiltSailVisualBucket == bucket)
            {
                return;
            }

            vis.LastBuiltSailVisualBucket = bucket;
            _meshScratch.Clear();
            PhysicalItemMeshFactory.EmitWindmillRotorTriangles(integrity, _meshScratch);
            HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, vis.RotorMesh.name, vis.RotorMesh);
        }

        private void EnsureRootAndPreviewMeshes()
        {
            if (_windmillRoot == null)
            {
                GameObject existing = GameObject.Find("06_Faz4B_PhysicalWindmillRoot");
                if (existing == null) existing = new GameObject("06_Faz4B_PhysicalWindmillRoot");
                _windmillRoot = existing.transform;
            }

            if (_sharedPreviewTowerMesh == null)
            {
                _meshScratch.Clear();
                PhysicalItemMeshFactory.EmitWindmillTowerTriangles(false, 0.5f, _meshScratch);
                _sharedPreviewTowerMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _meshScratch, "Mesh_WindmillTower_SharedPreview");
            }

            if (_sharedPreviewRotorMesh == null)
            {
                _meshScratch.Clear();
                PhysicalItemMeshFactory.EmitWindmillRotorTriangles(1.0f, _meshScratch);
                _sharedPreviewRotorMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    _meshScratch, "Mesh_WindmillRotor_SharedPreview");
            }
        }

        private void EnsureGuiStyles()
        {
            if (_promptBoxStyle != null) return;
            _promptBoxStyle = new GUIStyle(GUI.skin.box);
            _promptTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.97f, 1.0f) }
            };
        }
    }
#endif
}
