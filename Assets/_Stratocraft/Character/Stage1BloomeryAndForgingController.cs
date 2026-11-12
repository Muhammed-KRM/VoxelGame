#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec3 = System.Numerics.Vector3;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 2.4: Odun Komuru Ocagi pirolizi (28 sn), Cevher Kirma, Kil Hava Borusu + Koruk + Pota
    /// montajli Ilkel Izabe Firini (1250 C Bloomery) ve Sicak Orste/Kayada Sunger Demir dovme denetleyicisi.
    /// </summary>
    public sealed class Stage1BloomeryAndForgingController
    {
        private sealed class SparkParticleBurst
        {
            public readonly Transform[] Shards = new Transform[6];
            public readonly Vector3[] Velocities = new Vector3[6];
            public float RemainingSec;
        }

        private sealed class CharcoalPitRuntimeEntry
        {
            public CharcoalPitState Data;
            public GameObject RootGo = null!;
            public Light? WarmPointLight;
            public readonly Transform[] SmokeMotes = new Transform[4];
            public readonly MeshFilter[] SmokeMoteMfs = new MeshFilter[4];
            public SparkParticleBurst SparkBurst = new();
            public CharcoalPitPhase LastRenderedPhase;
        }

        private sealed class BloomeryRuntimeEntry
        {
            public BloomeryFurnaceState Data;
            public GameObject RootGo = null!;
            public GameObject FlameGo = null!;
            public GameObject TuyereGo = null!;
            public GameObject BellowsGo = null!;
            public GameObject CrucibleGo = null!;
            public GameObject ClayPlugGo = null!;
            public GameObject SlagStreamGo = null!;
            public GameObject BloomCoreGo = null!;
            public Light? WarmPointLight;
            public Vector3 BaseBellowsScale = Vector3.one;
            public float BellowsPumpAnimTimer;
            public SparkParticleBurst SparkBurst = new();
            public byte LastThermalGlowByte = 255;
        }

        private sealed class SurfaceWorkpieceEntry
        {
            public bool IsHotForge;
            public OreCrushingSpotState OreSpot;
            public HotForgeWorkpieceState ForgeState;
            public Vector3 WorldPos;
            public Vector3 BaseGroundScale = Vector3.one;
            public GameObject RootGo = null!;
            public GameObject MeshChildGo = null!;
            public MeshFilter MeshFilter = null!;
            public MeshRenderer MeshRenderer = null!;
            public SparkParticleBurst SparkBurst = new();
            public byte LastRenderedStageByte;
            public byte LastRenderedTempByte;
            public bool LastCompletedBar;
        }

        private readonly List<CharcoalPitRuntimeEntry> _charcoalPits = new(8);
        private readonly List<BloomeryRuntimeEntry> _bloomeries = new(8);
        private readonly List<SurfaceWorkpieceEntry> _workpieces = new(8);
        private readonly List<FacetedVoxelTriangle> _meshBuildScratch = new(256);

        private Transform? _stationsContainer;
        private Mesh? _sharedSparkShardMesh;
        private Mesh? _sharedThickGreySmokeMesh;
        private Mesh? _sharedBlueThinSmokeMesh;
        private Mesh? _sharedBloomeryFlameMesh;
        private Mesh? _sharedSlagStreamMesh;
        private Mesh? _sharedPlugClayMesh;

        private AudioSource? _sfxSource;
        private AudioClip? _flintStrikeClip;
        private AudioClip? _igniteWhooshClip;
        private AudioClip? _bellowsWhooshClip;
        private AudioClip? _metallicClangClip;
        private AudioClip? _stoneCrushClip;

        private float _pendingStaminaDrain;
        private float _strikeCooldownSec;

        private GUIStyle? _promptBoxStyle;
        private GUIStyle? _promptTextStyle;

        public int ActiveCharcoalPitCount => _charcoalPits.Count;
        public int ActiveBloomeryCount => _bloomeries.Count;
        public int ActiveWorkpieceCount => _workpieces.Count;

        public float ConsumePendingStaminaDrain()
        {
            float v = _pendingStaminaDrain;
            _pendingStaminaDrain = 0f;
            return v;
        }

        public void RegisterCharcoalPit(
            uint stationId,
            in MindValidationResult res,
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat)
        {
            EnsureSharedMeshesAndContainer();

            int woodUnits = 0;
            int coverUnits = 0;
            var parts = looseBuildSystem.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.BoundStationId != stationId) continue;
                if (p.PartKind == LooseBuildPartKind.Stick || p.PartKind == LooseBuildPartKind.SplitFirewood)
                {
                    woodUnits++;
                }
                else if (p.PartKind == LooseBuildPartKind.LooseDirtClod || p.PartKind == LooseBuildPartKind.ClayLump)
                {
                    coverUnits++;
                }
            }

            CharcoalPitState data = BloomeryThermodynamicsSystem.CreateCharcoalPit(
                stationId,
                res.ClusterCenter,
                res.ClusterBaseY,
                woodUnits,
                coverUnits);

            Vector3 worldCenter = new Vector3(data.Center.X, data.BaseY, data.Center.Z);
            GameObject root = new GameObject($"CharcoalPit_Station_{stationId}");
            if (_stationsContainer != null) root.transform.SetParent(_stationsContainer, false);
            root.transform.position = worldCenter;

            GameObject lightGo = new GameObject("CharcoalPit_EmberLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            Light ptLight = lightGo.AddComponent<Light>();
            ptLight.type = LightType.Point;
            ptLight.color = new Color(1.0f, 0.45f, 0.14f);
            ptLight.range = 4.2f;
            ptLight.intensity = 0f;
            ptLight.shadows = LightShadows.None;
            ptLight.enabled = false;

            var entry = new CharcoalPitRuntimeEntry
            {
                Data = data,
                RootGo = root,
                WarmPointLight = ptLight,
                LastRenderedPhase = CharcoalPitPhase.Unlit
            };

            for (int m = 0; m < entry.SmokeMotes.Length; m++)
            {
                GameObject moteGo = new GameObject($"CharcoalPit_SmokeMote_{m}");
                moteGo.transform.SetParent(root.transform, false);
                MeshFilter mmf = moteGo.AddComponent<MeshFilter>();
                mmf.sharedMesh = _sharedThickGreySmokeMesh;
                MeshRenderer mmr = moteGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) mmr.sharedMaterial = sharedVoxelMat;
                mmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                moteGo.SetActive(false);
                entry.SmokeMotes[m] = moteGo.transform;
                entry.SmokeMoteMfs[m] = mmf;
            }

            InitSparkBurstPool(root.transform, entry.SparkBurst, "CharcoalPit_SparkShard", sharedVoxelMat);
            _charcoalPits.Add(entry);
        }

        public void RegisterBloomeryFurnace(
            uint stationId,
            in MindValidationResult res,
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat)
        {
            EnsureSharedMeshesAndContainer();

            bool hasTuyere = false;
            bool hasBellows = false;
            bool hasCrucible = false;

            var parts = looseBuildSystem.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.BoundStationId != stationId) continue;

                if (p.PartKind == LooseBuildPartKind.ClayTuyerePipe &&
                    (p.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) == 0)
                {
                    hasTuyere = true;
                }
                else if (p.PartKind == LooseBuildPartKind.LeatherBellows)
                {
                    hasBellows = true;
                }
                else if (p.PartKind == LooseBuildPartKind.ClayCruciblePot &&
                         (p.ShapeDna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) == 0)
                {
                    hasCrucible = true;
                }
            }

            BloomeryFurnaceState data = BloomeryThermodynamicsSystem.CreateBloomeryFurnace(
                stationId,
                res.ClusterCenter,
                res.ClusterBaseY,
                hasTuyere,
                hasBellows,
                hasCrucible);

            Vector3 worldCenter = new Vector3(data.Center.X, data.BaseY, data.Center.Z);
            GameObject root = new GameObject($"Bloomery_Station_{stationId}");
            if (_stationsContainer != null) root.transform.SetParent(_stationsContainer, false);
            root.transform.position = worldCenter;

            PhysicalItemShapeDna defaultDna = PhysicalItemShapeDna.CreateNatural(stationId ^ 0x4B100u);

            // 1) Top CO chimney flame
            GameObject flameGo = new GameObject("Bloomery_ChimneyCOFlame");
            flameGo.transform.SetParent(root.transform, false);
            flameGo.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            MeshFilter flameMf = flameGo.AddComponent<MeshFilter>();
            flameMf.sharedMesh = _sharedBloomeryFlameMesh;
            MeshRenderer flameMr = flameGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) flameMr.sharedMaterial = sharedVoxelMat;
            flameMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flameGo.SetActive(false);

            // 2) Side Clay Tuyere Pipe
            Mesh tuyereMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                PhysicalItemArchetype.ClayTuyerePipe, 0, in defaultDna, out Vector3 tuyereNat);
            GameObject tuyereGo = new GameObject("Bloomery_TuyerePipe");
            tuyereGo.transform.SetParent(root.transform, false);
            tuyereGo.transform.localPosition = new Vector3(0.30f, 0.14f, 0f);
            tuyereGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            tuyereGo.transform.localScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.ClayTuyerePipe, tuyereNat);
            MeshFilter tuyereMf = tuyereGo.AddComponent<MeshFilter>();
            tuyereMf.sharedMesh = tuyereMesh;
            MeshRenderer tuyereMr = tuyereGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) tuyereMr.sharedMaterial = sharedVoxelMat;
            tuyereGo.SetActive(hasTuyere);

            // 3) Side Leather Bellows
            Mesh bellowsMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                PhysicalItemArchetype.LeatherBellows, 0, in defaultDna, out Vector3 bellowsNat);
            Vector3 bellowsScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.LeatherBellows, bellowsNat);
            GameObject bellowsGo = new GameObject("Bloomery_LeatherBellows");
            bellowsGo.transform.SetParent(root.transform, false);
            bellowsGo.transform.localPosition = new Vector3(0.58f, 0.15f, 0f);
            bellowsGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            bellowsGo.transform.localScale = bellowsScale;
            MeshFilter bellowsMf = bellowsGo.AddComponent<MeshFilter>();
            bellowsMf.sharedMesh = bellowsMesh;
            MeshRenderer bellowsMr = bellowsGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) bellowsMr.sharedMaterial = sharedVoxelMat;
            bellowsGo.SetActive(hasBellows);

            // 4) Interior Clay Crucible Pot
            Mesh crucibleMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                PhysicalItemArchetype.ClayCruciblePot, 0, in defaultDna, out Vector3 crucibleNat);
            GameObject crucibleGo = new GameObject("Bloomery_ClayCruciblePot");
            crucibleGo.transform.SetParent(root.transform, false);
            crucibleGo.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            crucibleGo.transform.localScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.ClayCruciblePot, crucibleNat);
            MeshFilter crucibleMf = crucibleGo.AddComponent<MeshFilter>();
            crucibleMf.sharedMesh = crucibleMesh;
            MeshRenderer crucibleMr = crucibleGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) crucibleMr.sharedMaterial = sharedVoxelMat;
            crucibleGo.SetActive(hasCrucible);

            // 5) Front lower Clay Plug
            GameObject plugGo = new GameObject("Bloomery_FrontClayPlug");
            plugGo.transform.SetParent(root.transform, false);
            plugGo.transform.localPosition = new Vector3(0f, 0.12f, 0.26f);
            MeshFilter plugMf = plugGo.AddComponent<MeshFilter>();
            plugMf.sharedMesh = _sharedPlugClayMesh;
            MeshRenderer plugMr = plugGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) plugMr.sharedMaterial = sharedVoxelMat;
            plugGo.SetActive(true);

            // 6) Bottom Slag Stream
            GameObject slagGo = new GameObject("Bloomery_SlagStream");
            slagGo.transform.SetParent(root.transform, false);
            slagGo.transform.localPosition = new Vector3(0f, 0.03f, 0.30f);
            MeshFilter slagMf = slagGo.AddComponent<MeshFilter>();
            slagMf.sharedMesh = _sharedSlagStreamMesh;
            MeshRenderer slagMr = slagGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) slagMr.sharedMaterial = sharedVoxelMat;
            slagMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            slagGo.SetActive(false);

            // 7) Bottom Glowing Iron Bloom Sponge Core
            PhysicalItemShapeDna hotBloomDna = defaultDna.WithTemperature(
                BloomeryThermodynamicsSystem.TemperatureToUberByte(HotForgeWorkpieceState.InitialBloomTempCelsius));
            Mesh bloomMesh = PhysicalItemMeshFactory.GetOrCreateMesh(
                PhysicalItemArchetype.IronBloomSponge, 0, in hotBloomDna, out Vector3 bloomNat);
            GameObject bloomGo = new GameObject("Bloomery_ReadyBloomCore");
            bloomGo.transform.SetParent(root.transform, false);
            bloomGo.transform.localPosition = new Vector3(0f, 0.10f, 0.08f);
            bloomGo.transform.localScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                PhysicalItemArchetype.IronBloomSponge, bloomNat);
            MeshFilter bloomMf = bloomGo.AddComponent<MeshFilter>();
            bloomMf.sharedMesh = bloomMesh;
            MeshRenderer bloomMr = bloomGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) bloomMr.sharedMaterial = sharedVoxelMat;
            bloomGo.SetActive(false);

            // 8) Warm Point Light (LightShadows.None)
            GameObject lightGo = new GameObject("Bloomery_WarmPointLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.56f, 0f);
            Light ptLight = lightGo.AddComponent<Light>();
            ptLight.type = LightType.Point;
            ptLight.color = new Color(1.0f, 0.54f, 0.18f);
            ptLight.range = 7.5f;
            ptLight.intensity = 0f;
            ptLight.shadows = LightShadows.None;
            ptLight.enabled = false;

            var entry = new BloomeryRuntimeEntry
            {
                Data = data,
                RootGo = root,
                FlameGo = flameGo,
                TuyereGo = tuyereGo,
                BellowsGo = bellowsGo,
                CrucibleGo = crucibleGo,
                ClayPlugGo = plugGo,
                SlagStreamGo = slagGo,
                BloomCoreGo = bloomGo,
                WarmPointLight = ptLight,
                BaseBellowsScale = bellowsScale
            };

            InitSparkBurstPool(root.transform, entry.SparkBurst, "Bloomery_SparkShard", sharedVoxelMat);
            _bloomeries.Add(entry);
        }

        public bool HasAimedInteractiveTarget(UnityEngine.Camera? cam, Vector3 playerPos, Transform? anvilTransform)
        {
            if (FindAimedWorkpieceIndex(cam, playerPos, anvilTransform, 3.25f) >= 0) return true;
            if (FindAimedBloomeryIndex(cam, playerPos, 3.45f) >= 0) return true;
            if (FindAimedCharcoalPitIndex(cam, playerPos, 3.35f) >= 0) return true;
            return false;
        }

        public bool TryHandleInteractKeyE(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Transform? anvilTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat)
        {
            // 1) Active surface workpiece collection (RoastedOreChunk, IronBloomSponge, or WroughtIronBar)
            int wpIdx = FindAimedWorkpieceIndex(cam, playerPos, anvilTransform, 3.25f);
            if (wpIdx >= 0)
            {
                SurfaceWorkpieceEntry wp = _workpieces[wpIdx];
                InventoryItemKind pickupKind;
                byte pickupSubId;
                PhysicalItemShapeDna pickupDna;
                string pickupMsg;

                if (!wp.IsHotForge)
                {
                    pickupKind = InventoryItemKind.RoastedOreChunk;
                    pickupSubId = wp.OreSpot.OreMaterialId;
                    pickupDna = wp.OreSpot.SourceDna;
                    pickupMsg = "🪨 Kavrulmuş Cevher yüzeyden geri alındı.";
                }
                else if (wp.ForgeState.IsCompletedWroughtBar)
                {
                    pickupKind = InventoryItemKind.WroughtIronBar;
                    pickupSubId = 0;
                    pickupDna = wp.ForgeState.ShapeDna;
                    pickupMsg = "⚒️ Dövme Demir Lama alındı! (T2 Demir alet ve silah dövümü için hazır)";
                }
                else
                {
                    pickupKind = InventoryItemKind.IronBloomSponge;
                    pickupSubId = 0;
                    pickupDna = wp.ForgeState.ShapeDna;
                    pickupMsg = $"🔥 Sünger Demir ({wp.ForgeState.TemperatureCelsius:0} °C) yüzeyden alındı.";
                }

                if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(pickupKind, pickupSubId, 1, miningCtrl, pickupDna))
                {
                    miningCtrl.SpawnPhysicalWorldItem(
                        wp.WorldPos + Vector3.up * 0.08f, pickupKind, pickupSubId, 1, pickupDna, sharedVoxelMat);
                    miningCtrl.LastFeedbackText = "Çanta dolu olduğu için iş parçası yere bırakıldı ([E] ile al)!";
                }
                else
                {
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    miningCtrl.LastFeedbackText = pickupMsg;
                }

                if (wp.RootGo != null) UnityEngine.Object.Destroy(wp.RootGo);
                _workpieces.RemoveAt(wpIdx);
                return true;
            }

            // 2) CharcoalPit open/harvest
            int pitIdx = FindAimedCharcoalPitIndex(cam, playerPos, 3.35f);
            if (pitIdx >= 0)
            {
                CharcoalPitRuntimeEntry pit = _charcoalPits[pitIdx];
                if (pit.Data.Phase == CharcoalPitPhase.RuinedAsh)
                {
                    looseBuildSystem.RemoveAndDestroyStationParts(pit.Data.StationId);
                    if (pit.RootGo != null) UnityEngine.Object.Destroy(pit.RootGo);
                    _charcoalPits.RemoveAt(pitIdx);
                    miningCtrl.LastFeedbackText = "💨 Sönmüş kül yığını temizlendi.";
                    return true;
                }

                if (BloomeryThermodynamicsSystem.TryOpenOrHarvestCharcoalPit(
                        ref pit.Data, out int yieldedCharcoal, out bool ruinedByEarlyOpen))
                {
                    if (ruinedByEarlyOpen)
                    {
                        looseBuildSystem.UpdateStationAllThermalGlow(pit.Data.StationId, 0);
                        SetCharcoalPitSmokeActive(pit, false, false);
                        if (pit.WarmPointLight != null) pit.WarmPointLight.enabled = false;
                        miningCtrl.LastFeedbackText =
                            "💥 ERKEN AÇILDI! 28 sn dolmadan oksijen girdiği için odunların tamamı KÜL oldu! ([E] ile temizle)";
                        return true;
                    }

                    int added = 0;
                    for (int i = 0; i < yieldedCharcoal; i++)
                    {
                        PhysicalItemShapeDna coalDna = PhysicalItemShapeDna.CreateNatural(
                            pit.Data.StationId * 977u + (uint)(i * 31 + 7));
                        if (holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                                InventoryItemKind.CharcoalChunk, 0, 1, miningCtrl, coalDna))
                        {
                            added++;
                        }
                        else
                        {
                            Vector3 dropOffset = new Vector3((i % 3 - 1) * 0.16f, 0.22f, (i / 3 - 1) * 0.16f);
                            miningCtrl.SpawnPhysicalWorldItem(
                                pit.RootGo.transform.position + dropOffset,
                                InventoryItemKind.CharcoalChunk, 0, 1, coalDna, sharedVoxelMat);
                        }
                    }

                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    looseBuildSystem.RemoveAndDestroyStationParts(pit.Data.StationId);
                    if (pit.RootGo != null) UnityEngine.Object.Destroy(pit.RootGo);
                    _charcoalPits.RemoveAt(pitIdx);

                    miningCtrl.LastFeedbackText = added == yieldedCharcoal
                        ? $"🪵 ODUN KÖMÜRÜ HASADI: +{yieldedCharcoal}x Odun Kömürü! (İzabe Fırınında 1300 °C yakıt)"
                        : $"🪵 ODUN KÖMÜRÜ HASADI: {added}x çantaya, {yieldedCharcoal - added}x yere düştü!";
                    return true;
                }
            }

            // 3) BloomeryFurnace collect products or pump bellows
            int bloomIdx = FindAimedBloomeryIndex(cam, playerPos, 3.45f);
            if (bloomIdx >= 0)
            {
                BloomeryRuntimeEntry b = _bloomeries[bloomIdx];

                if (b.Data.HasMoltenCopperReady)
                {
                    b.Data.HasMoltenCopperReady = false;
                    byte hotCopperTemp = BloomeryThermodynamicsSystem.TemperatureToUberByte(
                        BloomeryFurnaceState.CopperMeltThresholdCelsius);
                    int added = 0;
                    for (int i = 0; i < 2; i++)
                    {
                        PhysicalItemShapeDna cuDna = PhysicalItemShapeDna.CreateNatural(
                            b.Data.StationId * 431u + (uint)(i * 53 + 11)).WithTemperature(hotCopperTemp);
                        if (holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                                InventoryItemKind.MetalIngot, 1, 1, miningCtrl, cuDna))
                        {
                            added++;
                        }
                        else
                        {
                            miningCtrl.SpawnPhysicalWorldItem(
                                b.RootGo.transform.position + new Vector3(0.18f * i, 0.35f, 0.36f),
                                InventoryItemKind.MetalIngot, 1, 1, cuDna, sharedVoxelMat);
                        }
                    }
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    miningCtrl.LastFeedbackText = added == 2
                        ? "🪙 POTADAN DÖKÜLDÜ: +2x Saf Bakır Külçe!"
                        : $"🪙 POTADAN DÖKÜLDÜ: {added}x çantaya, {2 - added}x yere bırakıldı!";
                    return true;
                }

                if (b.Data.HasReadyIronBloom && !b.Data.IsFrontClayPlugIntact)
                {
                    b.Data.HasReadyIronBloom = false;
                    b.BloomCoreGo.SetActive(false);

                    byte hotTempByte = BloomeryThermodynamicsSystem.TemperatureToUberByte(
                        HotForgeWorkpieceState.InitialBloomTempCelsius);
                    PhysicalItemShapeDna bloomDna = PhysicalItemShapeDna.CreateNatural(
                        b.Data.StationId * 719u + 19u).WithTemperature(hotTempByte);

                    if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                            InventoryItemKind.IronBloomSponge, 0, 1, miningCtrl, bloomDna))
                    {
                        miningCtrl.SpawnPhysicalWorldItem(
                            b.RootGo.transform.position + new Vector3(0f, 0.25f, 0.42f),
                            InventoryItemKind.IronBloomSponge, 0, 1, bloomDna, sharedVoxelMat);
                        miningCtrl.LastFeedbackText =
                            "🔥 1180 °C Akkor Sünger Demir çıkarıldı ama çanta dolu — yere düştü! Soğumadan örse/düz kayaya koy!";
                    }
                    else
                    {
                        holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                        miningCtrl.LastFeedbackText =
                            "🔥 1180 °C AKKOR SÜNGER DEMİR ÇIKARILDI! Hızla Örse veya düz taşa [Sağ Tık] ile koyup Taş Çekiçle 8 kez döv!";
                    }
                    return true;
                }

                if (b.Data.HasTappedSlagShard)
                {
                    b.Data.HasTappedSlagShard = false;
                    b.Data.SlagTappedVisual01 = 0f;
                    b.SlagStreamGo.SetActive(false);

                    PhysicalItemShapeDna slagDna = PhysicalItemShapeDna.CreateNatural(b.Data.StationId * 613u + 5u);
                    if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                            InventoryItemKind.SlagShard, 0, 1, miningCtrl, slagDna))
                    {
                        miningCtrl.SpawnPhysicalWorldItem(
                            b.RootGo.transform.position + new Vector3(0f, 0.22f, 0.44f),
                            InventoryItemKind.SlagShard, 0, 1, slagDna, sharedVoxelMat);
                    }
                    else
                    {
                        holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    }
                    miningCtrl.LastFeedbackText = b.Data.HasReadyIronBloom && b.Data.IsFrontClayPlugIntact
                        ? "🪨 +1x Cüruf Cam Parçası alındı! Şimdi Çekiç/Kazma ile [Sol Tık] yaparak ön kil tıkacı kır!"
                        : "🪨 +1x Cüruf Cam Parçası alındı!";
                    return true;
                }

                if (b.Data.HasReadyIronBloom && b.Data.IsFrontClayPlugIntact)
                {
                    miningCtrl.LastFeedbackText =
                        "🏺 Sünger Demir içeride hazır! Çıkarmak için Taş Çekiç veya Kazma ile ön alt kil tıkaca [Sol Tık] yapıp kır.";
                    return true;
                }

                return TryPumpBloomeryBellows(b, b.RootGo.transform, miningCtrl);
            }

            return false;
        }

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Transform? anvilTransform,
            bool uiBlocked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat,
            out bool consumedMouseClick)
        {
            consumedMouseClick = false;
            if (_strikeCooldownSec > 0f)
            {
                _strikeCooldownSec = Mathf.Max(0f, _strikeCooldownSec - dt);
            }

            // 1) Player interactions
            if (!uiBlocked && !miningCtrl.IsCrystalActive && !holsterCtrl.IsCarryingShoulderLog)
            {
                VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                Vector3 playerPos = playerTransform.position;

                int aimedWpIdx = FindAimedWorkpieceIndex(cam, playerPos, anvilTransform, 3.25f);
                int aimedBloomeryIdx = FindAimedBloomeryIndex(cam, playerPos, 3.45f);
                int aimedPitIdx = FindAimedCharcoalPitIndex(cam, playerPos, 3.35f);

                bool pressedLmb = Input.GetMouseButtonDown(0);
                bool pressedRmb = Input.GetMouseButtonDown(1);

                // A) Flint ignition (LMB / RMB) on CharcoalPit or Bloomery
                if (activeSlot.Kind == InventoryItemKind.FlintStone && (pressedLmb || pressedRmb) && _strikeCooldownSec <= 0f)
                {
                    if (aimedBloomeryIdx >= 0)
                    {
                        consumedMouseClick = true;
                        _strikeCooldownSec = 0.24f;
                        HandleFlintStrikeOnBloomery(_bloomeries[aimedBloomeryIdx], playerTransform, miningCtrl, holsterCtrl);
                    }
                    else if (aimedPitIdx >= 0)
                    {
                        consumedMouseClick = true;
                        _strikeCooldownSec = 0.24f;
                        HandleFlintStrikeOnCharcoalPit(_charcoalPits[aimedPitIdx], playerTransform, miningCtrl, holsterCtrl);
                    }
                }
                // B) Hammer / Pickaxe LMB actions
                else if (pressedLmb && _strikeCooldownSec <= 0f)
                {
                    if (activeSlot.Kind == InventoryItemKind.ToolStoneHammer && aimedWpIdx >= 0)
                    {
                        consumedMouseClick = true;
                        _strikeCooldownSec = 0.26f;
                        HandleHammerStrikeOnWorkpiece(
                            aimedWpIdx, playerTransform, miningCtrl, holsterCtrl, combatBridge, sharedVoxelMat);
                    }
                    else if ((activeSlot.Kind == InventoryItemKind.ToolStoneHammer ||
                              activeSlot.Kind == InventoryItemKind.ToolPickaxe) &&
                             aimedBloomeryIdx >= 0)
                    {
                        BloomeryRuntimeEntry b = _bloomeries[aimedBloomeryIdx];
                        if (b.Data.IsFrontClayPlugIntact && b.Data.HasReadyIronBloom)
                        {
                            consumedMouseClick = true;
                            _strikeCooldownSec = 0.26f;
                            BreakBloomeryFrontPlug(b, playerTransform, miningCtrl);
                        }
                    }
                    else if (activeSlot.IsEmpty && aimedBloomeryIdx >= 0)
                    {
                        consumedMouseClick = true;
                        _strikeCooldownSec = 0.22f;
                        TryPumpBloomeryBellows(_bloomeries[aimedBloomeryIdx], playerTransform, miningCtrl);
                    }
                }

                // C) RMB interactions on Bloomery or Forging Surface
                if (!consumedMouseClick && pressedRmb)
                {
                    if (aimedBloomeryIdx >= 0)
                    {
                        consumedMouseClick = HandleRightClickOnBloomery(
                            _bloomeries[aimedBloomeryIdx], in activeSlot, playerTransform,
                            miningCtrl, holsterCtrl, combatBridge);
                    }
                    else if (activeSlot.Kind == InventoryItemKind.RoastedOreChunk ||
                             activeSlot.Kind == InventoryItemKind.IronBloomSponge)
                    {
                        if (TryResolveAimedForgingSurface(
                                cam, playerPos, anvilTransform, miningCtrl, looseBuildSystem,
                                out Vector3 surfPos, out string surfLabel))
                        {
                            consumedMouseClick = true;
                            HandlePlaceWorkpieceOnSurface(
                                in activeSlot, surfPos, surfLabel, miningCtrl, holsterCtrl, combatBridge, sharedVoxelMat);
                        }
                    }
                }
            }

            // 2) Step all registered CharcoalPits, Bloomeries, and HotForge Workpieces
            float timeNow = Time.time;
            StepCharcoalPits(dt, timeNow, looseBuildSystem);
            StepBloomeries(dt, timeNow, looseBuildSystem);
            StepWorkpieces(dt);
        }

        public float EvaluateThermalBonus(Vector3 playerChestPos)
        {
            float bestHeatBonus = 0f;

            for (int i = 0; i < _bloomeries.Count; i++)
            {
                BloomeryRuntimeEntry b = _bloomeries[i];
                if (!b.Data.IsLit || b.RootGo == null) continue;
                float dist = Vector3.Distance(playerChestPos, b.RootGo.transform.position + new Vector3(0f, 0.45f, 0f));
                const float maxBloomeryRadius = 6.5f;
                if (dist >= maxBloomeryRadius) continue;

                float tempRatio = Mathf.Clamp01((b.Data.TemperatureCelsius - 350.0f) / 880.0f);
                float peakBonus = Mathf.Lerp(14.0f, 26.0f, tempRatio);
                float bonus = (1.0f - dist / maxBloomeryRadius) * peakBonus;
                if (bonus > bestHeatBonus) bestHeatBonus = bonus;
            }

            for (int i = 0; i < _charcoalPits.Count; i++)
            {
                CharcoalPitRuntimeEntry pit = _charcoalPits[i];
                if (pit.Data.Phase != CharcoalPitPhase.Smoldering || pit.RootGo == null) continue;
                float dist = Vector3.Distance(playerChestPos, pit.RootGo.transform.position + new Vector3(0f, 0.25f, 0f));
                const float maxPitRadius = 4.0f;
                if (dist >= maxPitRadius) continue;

                float bonus = (1.0f - dist / maxPitRadius) * 8.0f;
                if (bonus > bestHeatBonus) bestHeatBonus = bonus;
            }

            return bestHeatBonus;
        }

        public void DrawCrosshairPromptGui(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Transform? anvilTransform,
            bool showHud,
            bool uiBlocked)
        {
            if (!showHud || uiBlocked) return;
            EnsureGuiStyles();

            int wpIdx = FindAimedWorkpieceIndex(cam, playerPos, anvilTransform, 3.25f);
            if (wpIdx >= 0)
            {
                SurfaceWorkpieceEntry wp = _workpieces[wpIdx];
                if (!wp.IsHotForge)
                {
                    DrawPromptBanner(
                        $"⛏️ Kavrulmuş Cevher ({wp.OreSpot.HammerStrikeCount}/{OreCrushingSpotState.RequiredHammerStrikes} Çekiç Darbesi)",
                        "Taş Çekiç ile [Sol Tık] Kır (-> 2x Kırılmış Cevher)  |  [E] Geri Al");
                }
                else if (wp.ForgeState.IsCompletedWroughtBar)
                {
                    DrawPromptBanner(
                        $"⚒️ DÖVME DEMİR LAMA HAZIR! ({wp.ForgeState.TemperatureCelsius:0} °C)",
                        "[E] Dövme Demir Lamayı Al");
                }
                else
                {
                    string heatTag = wp.ForgeState.TemperatureCelsius >= HotForgeWorkpieceState.MinHotForgeCelsius
                        ? "SICAK — DÖVÜLEBİLİR"
                        : "SOĞUK (<720 °C) — IZABE FIRININDA ISIT!";
                    DrawPromptBanner(
                        $"🔥 Akkor Sünger Demir ({wp.ForgeState.CompletedStrikes}/{HotForgeWorkpieceState.RequiredHotStrikes} Darbe | {wp.ForgeState.TemperatureCelsius:0} °C — {heatTag})",
                        "Taş Çekiç ile [Sol Tık] Döv  |  [E] Al");
                }
                return;
            }

            int bloomIdx = FindAimedBloomeryIndex(cam, playerPos, 3.45f);
            if (bloomIdx >= 0)
            {
                BloomeryRuntimeEntry b = _bloomeries[bloomIdx];
                string oreName = b.Data.OreMaterialId == BloomeryFurnaceState.CopperMatId
                    ? "Bakır"
                    : (b.Data.OreMaterialId == BloomeryFurnaceState.IronMatId ? "Demir" : "Yok");
                string stateTag = b.Data.IsLit
                    ? $"Yanıyor {b.Data.TemperatureCelsius:0} °C"
                    : $"Sönük ({b.Data.SparkStrikeCount}/{BloomeryFurnaceState.RequiredSparkStrikes} Kıvılcım)";
                string line1 = $"🏺 İzabe Fırını [{stateTag} | Kömür:{b.Data.CharcoalUnits}/{BloomeryFurnaceState.MaxCharcoalUnits} | Cevher:{b.Data.CrushedOreUnits}/{BloomeryFurnaceState.MaxCrushedOreUnits} ({oreName})]";

                string line2;
                if (b.Data.HasMoltenCopperReady)
                {
                    line2 = "🪙 SAF BAKIR HAZIR! — [E] 2x Saf Bakır Külçe Al";
                }
                else if (b.Data.HasReadyIronBloom && !b.Data.IsFrontClayPlugIntact)
                {
                    line2 = "🔥 1180 °C SÜNGER DEMİR HAZIR! — [E] Çıkar";
                }
                else if (b.Data.HasReadyIronBloom && b.Data.IsFrontClayPlugIntact)
                {
                    line2 = "💥 SÜNGER DEMİR OLUŞTU! — Taş Çekiç/Kazma ile [Sol Tık] Ön Kil Tıkacı Kır";
                }
                else if (b.Data.HasTappedSlagShard)
                {
                    line2 = "🪨 Cüruf Aktı! — [E] Cüruf Parçası Al";
                }
                else if (!b.Data.IsFrontClayPlugIntact)
                {
                    line2 = "🏺 Ön delik açık! — 1x Kil Topağı ile [Sağ Tık] Mühürle";
                }
                else if (!b.Data.HasClayTuyere)
                {
                    line2 = "[Sağ Tık] Pişmiş Kil Hava Borusu Tak (Çiğ boruyu önce Kamp Ateşinde pişir!)";
                }
                else if (!b.Data.HasLeatherBellows)
                {
                    line2 = "[Sağ Tık] Deri Körük Tak  |  [Sağ Tık] Kömür/Cevher Ekle";
                }
                else if (!b.Data.IsLit)
                {
                    line2 = "Çakmaktaşı ile [Sol/Sağ Tık] Tutuştur  |  [Sağ Tık] Kömür/Cevher/Pota Ekle";
                }
                else
                {
                    int pct = Mathf.RoundToInt(b.Data.SmeltProgress01 * 100f);
                    string smeltTxt = b.Data.CrushedOreUnits > 0 ? $"İzabe: %{pct} | " : string.Empty;
                    line2 = $"{smeltTxt}[E / Boş El Sol Tık] Körük Pompala (-1.8 EP)  |  [Sağ Tık] Kömür/Cevher Ekle";
                }

                DrawPromptBanner(line1, line2);
                return;
            }

            int pitIdx = FindAimedCharcoalPitIndex(cam, playerPos, 3.35f);
            if (pitIdx >= 0)
            {
                CharcoalPitRuntimeEntry pit = _charcoalPits[pitIdx];
                switch (pit.Data.Phase)
                {
                    case CharcoalPitPhase.Unlit:
                        DrawPromptBanner(
                            $"🪵 Odun Kömürü Ocağı ({pit.Data.SparkStrikeCount}/{CharcoalPitState.RequiredSparkStrikes} Kıvılcım | Odun:{pit.Data.WoodUnits} Örtü:{pit.Data.CoverUnits})",
                            pit.Data.HasEarthCover
                                ? "Çakmaktaşı ile [Sol/Sağ Tık] Tutuştur"
                                : "⚠️ Toprak/kil örtüsü eksik (<4) — tutuşturulursa kül olur!");
                        break;

                    case CharcoalPitPhase.Smoldering:
                        int rem = Mathf.Max(1, Mathf.CeilToInt(CharcoalPitState.PyrolysisDurationSec - pit.Data.ElapsedPyrolysisSec));
                        DrawPromptBanner(
                            $"💨 Odun Kömürü Pirolizi Sürüyor ({rem} sn kaldı | Kalın Gri Duman)",
                            "DİKKAT: [E] ile erken açarsan tüm odun KÜLE dönüşür!");
                        break;

                    case CharcoalPitPhase.ReadyCharcoal:
                        DrawPromptBanner(
                            "✨ ODUN KÖMÜRÜ HAZIR! (İnce Mavi Duman)",
                            "[E] Ocağı Aç ve 6-8x Odun Kömürü Topla");
                        break;

                    case CharcoalPitPhase.RuinedAsh:
                        DrawPromptBanner(
                            "💨 Sönmüş Kül Yığını (Erken açıldı veya örtüsüz yandı)",
                            "[E] Kül Yığınını Temizle");
                        break;
                }
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < _charcoalPits.Count; i++)
            {
                if (_charcoalPits[i].RootGo != null) UnityEngine.Object.Destroy(_charcoalPits[i].RootGo);
            }
            _charcoalPits.Clear();

            for (int i = 0; i < _bloomeries.Count; i++)
            {
                if (_bloomeries[i].RootGo != null) UnityEngine.Object.Destroy(_bloomeries[i].RootGo);
            }
            _bloomeries.Clear();

            for (int i = 0; i < _workpieces.Count; i++)
            {
                if (_workpieces[i].RootGo != null) UnityEngine.Object.Destroy(_workpieces[i].RootGo);
            }
            _workpieces.Clear();

            _pendingStaminaDrain = 0f;
            _strikeCooldownSec = 0f;
        }

        private bool HandleRightClickOnBloomery(
            BloomeryRuntimeEntry b,
            in VisualInventorySlot activeSlot,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            // 1) Mount ClayTuyerePipe (fired only)
            if (activeSlot.Kind == InventoryItemKind.ClayTuyerePipe)
            {
                PhysicalItemShapeDna dna = activeSlot.GetEffectiveShapeDna();
                if ((dna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0)
                {
                    miningCtrl.LastFeedbackText =
                        "🔥 Bu Kil Hava Borusu henüz ÇİĞ (pişmemiş)! Önce Kamp Ateşinde 12 saniye pişirip seramikleştirin.";
                    return true;
                }
                if (b.Data.HasClayTuyere)
                {
                    miningCtrl.LastFeedbackText = "Fırında zaten Pişmiş Kil Hava Borusu takılı!";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.HasClayTuyere = true;
                b.TuyereGo.SetActive(true);
                miningCtrl.LastFeedbackText = "🏺 Pişmiş Kil Hava Borusu (Tuyere) fırının yan alt deliğine takıldı! Şimdi Deri Körük takabilirsin.";
                return true;
            }

            // 2) Mount LeatherBellows
            if (activeSlot.Kind == InventoryItemKind.LeatherBellows)
            {
                if (!b.Data.HasClayTuyere)
                {
                    miningCtrl.LastFeedbackText = "⚠️ Deri Körüğü bağlamadan önce fırının yan deliğine Pişmiş Kil Hava Borusu (Tuyere) takmalısın!";
                    return true;
                }
                if (b.Data.HasLeatherBellows)
                {
                    miningCtrl.LastFeedbackText = "Fırına zaten Deri Körük bağlı!";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.HasLeatherBellows = true;
                b.BellowsGo.SetActive(true);
                miningCtrl.LastFeedbackText = "🌬️ Deri Körük takıldı! Ateş yandığında [E] veya boş elle [Sol Tık] yaparak 1250 °C'ye üfle!";
                return true;
            }

            // 3) Mount ClayCruciblePot (fired only)
            if (activeSlot.Kind == InventoryItemKind.ClayCruciblePot)
            {
                PhysicalItemShapeDna dna = activeSlot.GetEffectiveShapeDna();
                if ((dna.Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0)
                {
                    miningCtrl.LastFeedbackText =
                        "🔥 Bu Kil Pota henüz ÇİĞ (pişmemiş)! Önce Kamp Ateşinde 12 saniye pişirip seramikleştirin.";
                    return true;
                }
                if (b.Data.HasClayCrucible)
                {
                    miningCtrl.LastFeedbackText = "Fırının içinde zaten Pişmiş Kil Pota yerleştirilmiş!";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.HasClayCrucible = true;
                b.CrucibleGo.SetActive(true);
                miningCtrl.LastFeedbackText = "🥣 Pişmiş Kil Pota fırına yerleştirildi! Artık Kırılmış Bakır Cevheri + Odun Kömürü eritebilirsin (1085 °C).";
                return true;
            }

            // 4) Reseal front clay plug with 1x ClayLump
            if (activeSlot.Kind == InventoryItemKind.ClayLump)
            {
                if (b.Data.IsFrontClayPlugIntact)
                {
                    miningCtrl.LastFeedbackText = "Fırının ön alt kil tıkacı zaten sağlam ve mühürlü.";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.IsFrontClayPlugIntact = true;
                b.ClayPlugGo.SetActive(true);
                miningCtrl.LastFeedbackText = "🏺 Ön alt delik 1x Kil Topağı ile yeniden mühürlendi! Fırın tekrar hava kaçırmadan 1250 °C'ye çıkabilir.";
                return true;
            }

            // 5) Top-charge CharcoalChunk
            if (activeSlot.Kind == InventoryItemKind.CharcoalChunk)
            {
                if (b.Data.CharcoalUnits >= BloomeryFurnaceState.MaxCharcoalUnits)
                {
                    miningCtrl.LastFeedbackText = $"İzabe fırınının odun kömürü haznesi dolu ({BloomeryFurnaceState.MaxCharcoalUnits}/{BloomeryFurnaceState.MaxCharcoalUnits})!";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.CharcoalUnits++;
                miningCtrl.LastFeedbackText =
                    $"🖤 Üstten +1x Odun Kömürü dolduruldu ({b.Data.CharcoalUnits}/{BloomeryFurnaceState.MaxCharcoalUnits})!";
                return true;
            }

            // 6) Top-charge Stick / SplitFirewood
            if (activeSlot.Kind == InventoryItemKind.Stick || activeSlot.Kind == InventoryItemKind.SplitFirewood)
            {
                if (b.Data.WoodFuelUnits >= BloomeryFurnaceState.MaxWoodUnits)
                {
                    miningCtrl.LastFeedbackText = $"Fırının odun yakıt haznesi dolu ({BloomeryFurnaceState.MaxWoodUnits}/{BloomeryFurnaceState.MaxWoodUnits})!";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.WoodFuelUnits++;
                miningCtrl.LastFeedbackText =
                    $"🪵 Üstten +1x Odun yakıtı eklendi ({b.Data.WoodFuelUnits}/{BloomeryFurnaceState.MaxWoodUnits}) — NOT: 1085-1230 °C izabe için Odun Kömürü gerekir!";
                return true;
            }

            // 7) Top-charge CrushedOrePowder
            if (activeSlot.Kind == InventoryItemKind.CrushedOrePowder)
            {
                byte oreMat = activeSlot.SubId != 0 ? activeSlot.SubId : BloomeryFurnaceState.IronMatId;
                if (oreMat != BloomeryFurnaceState.CopperMatId && oreMat != BloomeryFurnaceState.IronMatId)
                {
                    miningCtrl.LastFeedbackText = "⚠️ İlkel İzabe Fırınında yalnızca Kırılmış Bakır veya Demir Cevheri işlenebilir!";
                    return true;
                }
                if (b.Data.CrushedOreUnits > 0 && b.Data.OreMaterialId != oreMat)
                {
                    miningCtrl.LastFeedbackText = "⚠️ Aynı fırın şarjında Bakır ve Demir cevherini karıştırma! Önce mevcut izabeyi tamamla.";
                    return true;
                }
                if (b.Data.CrushedOreUnits >= BloomeryFurnaceState.MaxCrushedOreUnits)
                {
                    miningCtrl.LastFeedbackText = $"Fırının cevher haznesi dolu ({BloomeryFurnaceState.MaxCrushedOreUnits}/{BloomeryFurnaceState.MaxCrushedOreUnits})!";
                    return true;
                }
                if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge)) return true;
                b.Data.OreMaterialId = oreMat;
                b.Data.CrushedOreUnits++;
                string oreLabel = oreMat == BloomeryFurnaceState.CopperMatId ? "Bakır" : "Demir";
                miningCtrl.LastFeedbackText =
                    $"⛏️ Üstten +1x Kırılmış {oreLabel} Cevheri yüklendi ({b.Data.CrushedOreUnits}/{BloomeryFurnaceState.MaxCrushedOreUnits})!";
                return true;
            }

            // 8) Reheat cold IronBloomSponge inside lit Bloomery (>= 850 C)
            if (activeSlot.Kind == InventoryItemKind.IronBloomSponge)
            {
                if (!b.Data.IsLit || b.Data.TemperatureCelsius < 850.0f)
                {
                    miningCtrl.LastFeedbackText =
                        $"🔥 Sünger Demiri yeniden ısıtmak için fırın yanıyor ve en az 850 °C olmalı! (Şu an: {b.Data.TemperatureCelsius:0} °C — Körük pompala!)";
                    return true;
                }

                int slotIdx = holsterCtrl.InventoryUi.SelectedHotbarIndex;
                if (slotIdx >= 0 && slotIdx < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount)
                {
                    float reheatTemp = Mathf.Clamp(b.Data.TemperatureCelsius, 980.0f, 1220.0f);
                    byte hotByte = BloomeryThermodynamicsSystem.TemperatureToUberByte(reheatTemp);
                    VisualInventorySlot updated = holsterCtrl.InventoryUi.HotbarSlots[slotIdx];
                    updated.ShapeDna = updated.GetEffectiveShapeDna().WithTemperature(hotByte);
                    holsterCtrl.InventoryUi.HotbarSlots[slotIdx] = updated;
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    PlayProceduralSfx(playerTransform, SfxKind.IgniteWhoosh);
                    TriggerSparkBurst(b.SparkBurst, new Vector3(0f, 0.28f, 0.12f));
                    miningCtrl.LastFeedbackText =
                        $"🔥 Sünger Demir fırında {reheatTemp:0} °C akkora ısıtıldı! Şimdi düz kayaya/örse [Sağ Tık] ile koyup Taş Çekiçle döv!";
                }
                return true;
            }

            return false;
        }

        private void HandleFlintStrikeOnCharcoalPit(
            CharcoalPitRuntimeEntry pit,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            if (pit.Data.Phase != CharcoalPitPhase.Unlit)
            {
                miningCtrl.LastFeedbackText = pit.Data.Phase == CharcoalPitPhase.Smoldering
                    ? "Odun Kömürü Ocağı zaten için için yanıyor — 28 sn dolana kadar açma!"
                    : "Bu ocak zaten tutuşma aşamasını geçti.";
                return;
            }

            if (!holsterCtrl.IsCreativeMode && CountTotalStrikerStones(holsterCtrl) < 2)
            {
                miningCtrl.LastFeedbackText =
                    "🪨 KIVILCIM ÇAKILAMADI: Çakmaktaşını vurmak için envanterinde ikinci bir Çakmaktaşı veya Kaba Taş daha olmalı!";
                return;
            }

            _pendingStaminaDrain += CampfireStationData.SparkStaminaCost;
            TriggerSparkBurst(pit.SparkBurst, new Vector3(0f, 0.16f, 0f));
            PlayProceduralSfx(playerTransform, SfxKind.FlintStrike);

            BloomeryThermodynamicsSystem.ApplyCharcoalPitFlintStrike(ref pit.Data, out bool ignited);
            if (pit.Data.Phase == CharcoalPitPhase.RuinedAsh)
            {
                miningCtrl.LastFeedbackText =
                    "💥 ÖRTÜ EKSİK (<4 Toprak/Kil)! Hava aldığı için tüm odunlar kontrolsüz yanıp KÜLE dönüştü! ([E] ile temizle)";
            }
            else if (ignited)
            {
                PlayProceduralSfx(playerTransform, SfxKind.IgniteWhoosh);
                miningCtrl.LastFeedbackText =
                    "🔥 ODUN KÖMÜRÜ OCAĞI TUTUŞTU! Kalın gri duman çıkıyor — 28 sn sonra ince mavi dumana dönünce [E] ile aç!";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"✨ Çakmaktaşı Kıvılcımı ({pit.Data.SparkStrikeCount}/{CharcoalPitState.RequiredSparkStrikes}) — kav tütüyor, tekrar çak!";
            }
        }

        private void HandleFlintStrikeOnBloomery(
            BloomeryRuntimeEntry b,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl)
        {
            if (b.Data.IsLit)
            {
                miningCtrl.LastFeedbackText = "İzabe Fırını zaten yanıyor! Körüğü pompalayarak sıcaklığı yükseltebilirsin.";
                return;
            }

            if (b.Data.CharcoalUnits <= 0 && b.Data.WoodFuelUnits <= 0)
            {
                miningCtrl.LastFeedbackText =
                    "⚠️ Fırının içinde yakıt yok! Önce eline Odun Kömürü (veya Dal/Odun) alıp fırına [Sağ Tık] ile doldur.";
                return;
            }

            if (!holsterCtrl.IsCreativeMode && CountTotalStrikerStones(holsterCtrl) < 2)
            {
                miningCtrl.LastFeedbackText =
                    "🪨 KIVILCIM ÇAKILAMADI: Çakmaktaşını vurmak için envanterinde ikinci bir Çakmaktaşı veya Kaba Taş daha olmalı!";
                return;
            }

            _pendingStaminaDrain += CampfireStationData.SparkStaminaCost;
            TriggerSparkBurst(b.SparkBurst, new Vector3(0f, 0.22f, 0.22f));
            PlayProceduralSfx(playerTransform, SfxKind.FlintStrike);

            b.Data.SparkStrikeCount++;
            if (b.Data.SparkStrikeCount >= BloomeryFurnaceState.RequiredSparkStrikes)
            {
                b.Data.SparkStrikeCount = BloomeryFurnaceState.RequiredSparkStrikes;
                b.Data.IsLit = true;
                b.Data.TemperatureCelsius = Mathf.Max(b.Data.TemperatureCelsius, 480.0f);
                PlayProceduralSfx(playerTransform, SfxKind.IgniteWhoosh);
                miningCtrl.LastFeedbackText = b.Data.HasClayTuyere && b.Data.HasLeatherBellows
                    ? "🔥 İZABE FIRINI TUTUŞTU! [E] veya boş elle [Sol Tık] ile körüğü ritmik pompalayarak 1250 °C'ye çık!"
                    : "🔥 İZABE FIRINI TUTUŞTU! (Doğal çekiş ~795 °C ile sınırlıdır — 1250 °C için Pişmiş Kil Hava Borusu + Deri Körük tak!)";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"✨ Çakmaktaşı Kıvılcımı ({b.Data.SparkStrikeCount}/{BloomeryFurnaceState.RequiredSparkStrikes}) — bacadan sıcak hava yükseliyor, tekrar çak!";
            }
        }

        private void BreakBloomeryFrontPlug(
            BloomeryRuntimeEntry b,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            b.Data.IsFrontClayPlugIntact = false;
            b.ClayPlugGo.SetActive(false);
            _pendingStaminaDrain += 1.5f;
            TriggerSparkBurst(b.SparkBurst, new Vector3(0f, 0.14f, 0.30f));
            PlayProceduralSfx(playerTransform, SfxKind.StoneCrush);
            miningCtrl.LastFeedbackText =
                "💥 ÖN KİL TIKAÇ KIRILDI! Fırının dibindeki 1180 °C Akkor Sünger Demiri almak için [E] tuşuna bas!";
        }

        private bool TryPumpBloomeryBellows(
            BloomeryRuntimeEntry b,
            Transform listenerRef,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (!b.Data.HasClayTuyere)
            {
                miningCtrl.LastFeedbackText = "⚠️ Önce fırının yan deliğine [Sağ Tık] ile Pişmiş Kil Hava Borusu (Tuyere) takmalısın!";
                return true;
            }
            if (!b.Data.HasLeatherBellows)
            {
                miningCtrl.LastFeedbackText = "⚠️ Körük takılı değil! Kil Hava Borusuna [Sağ Tık] ile Deri Körük tak.";
                return true;
            }

            if (!BloomeryThermodynamicsSystem.PumpBellows(ref b.Data))
            {
                return false;
            }

            _pendingStaminaDrain += 1.8f;
            b.BellowsPumpAnimTimer = 0.42f;
            PlayProceduralSfx(listenerRef, SfxKind.BellowsWhoosh);
            if (b.Data.IsLit)
            {
                TriggerSparkBurst(b.SparkBurst, new Vector3(0f, 0.78f, 0f));
            }

            int airPct = Mathf.RoundToInt(b.Data.BellowsAirflow01 * 100f);
            if (!b.Data.IsLit)
            {
                miningCtrl.LastFeedbackText = $"🌬️ Körük üflendi (Hava: %{airPct}) — ancak fırın sönük! Önce Çakmaktaşı ile yak.";
            }
            else if (!b.Data.IsFrontClayPlugIntact)
            {
                miningCtrl.LastFeedbackText = "⚠️ Ön kil tıkaç kırık olduğu için basınç kaçıyor! Tekrar ısıtmak için 1x Kil Topağı ile [Sağ Tık] mühürle.";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"🌬️ KÖRÜK POMPALANDI (-1.8 EP | Hava: %{airPct} | Fırın: {b.Data.TemperatureCelsius:0} °C — Ritmik ~0.8 sn aralıklarla pompala!)";
            }
            return true;
        }

        private void HandlePlaceWorkpieceOnSurface(
            in VisualInventorySlot activeSlot,
            Vector3 surfPos,
            string surfLabel,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Material? sharedVoxelMat)
        {
            for (int i = 0; i < _workpieces.Count; i++)
            {
                if (Vector3.Distance(_workpieces[i].WorldPos, surfPos) <= 0.34f)
                {
                    miningCtrl.LastFeedbackText = $"⚠️ {surfLabel} üzerinde zaten aktif bir iş parçası var! Önce [E] ile alın.";
                    return;
                }
            }

            EnsureSharedMeshesAndContainer();

            InventoryItemKind kind = activeSlot.Kind;
            byte subId = activeSlot.SubId;
            PhysicalItemShapeDna slotDna = activeSlot.GetEffectiveShapeDna();

            if (!ConsumeActiveItem(holsterCtrl, miningCtrl, combatBridge))
            {
                return;
            }

            GameObject root = new GameObject($"ForgeSurfaceWorkpiece_{kind}_{_workpieces.Count + 1}");
            if (_stationsContainer != null) root.transform.SetParent(_stationsContainer, false);
            root.transform.position = surfPos;

            GameObject meshChild = new GameObject("WorkpieceMesh");
            meshChild.transform.SetParent(root.transform, false);
            MeshFilter mf = meshChild.AddComponent<MeshFilter>();
            MeshRenderer mr = meshChild.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;

            var entry = new SurfaceWorkpieceEntry
            {
                RootGo = root,
                MeshChildGo = meshChild,
                MeshFilter = mf,
                MeshRenderer = mr,
                WorldPos = surfPos
            };

            if (kind == InventoryItemKind.RoastedOreChunk)
            {
                byte oreMat = subId != 0 ? subId : BloomeryFurnaceState.IronMatId;
                entry.IsHotForge = false;
                entry.OreSpot = new OreCrushingSpotState
                {
                    IsActive = true,
                    OreMaterialId = oreMat,
                    HammerStrikeCount = 0,
                    SourceDna = slotDna
                };
                Mesh m = PhysicalItemMeshFactory.GetOrCreateMesh(
                    PhysicalItemArchetype.RoastedCrackedOre, oreMat, in slotDna, out Vector3 natScale);
                Vector3 groundScale = PhysicalItemMeshFactory.GetWorldGroundScale(
                    PhysicalItemArchetype.RoastedCrackedOre, natScale);
                mf.sharedMesh = m;
                entry.BaseGroundScale = groundScale;
                meshChild.transform.localScale = groundScale;

                miningCtrl.LastFeedbackText =
                    $"🪨 Kavrulmuş Cevher {surfLabel} üzerine kondu! Taş Çekiç ile 3 kez [Sol Tık] vurarak 2x Kırılmış Cevher çıkar.";
            }
            else
            {
                float initialTemp = slotDna.ThermalTempByte > 0
                    ? BloomeryThermodynamicsSystem.UberByteToApproximateCelsius(slotDna.ThermalTempByte)
                    : 20.0f;
                entry.IsHotForge = true;
                entry.ForgeState = BloomeryThermodynamicsSystem.CreateHotBloomWorkpiece(in slotDna, initialTemp);

                RefreshHotForgeWorkpieceMesh(entry);
                miningCtrl.LastFeedbackText = initialTemp >= HotForgeWorkpieceState.MinHotForgeCelsius
                    ? $"🔥 Sünger Demir ({initialTemp:0} °C) {surfLabel} üzerine kondu! 720 °C altına düşmeden Taş Çekiç ile 8 kez döv!"
                    : $"❄️ Soğuk Sünger Demir ({initialTemp:0} °C) {surfLabel} üzerine kondu — dövmek için önce [E] alıp yanan İzabe Fırınında ısıtmalısın!";
            }

            InitSparkBurstPool(root.transform, entry.SparkBurst, "Workpiece_SparkShard", sharedVoxelMat);
            _workpieces.Add(entry);
        }

        private void HandleHammerStrikeOnWorkpiece(
            int wpIdx,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            Material? sharedVoxelMat)
        {
            SurfaceWorkpieceEntry wp = _workpieces[wpIdx];

            if (!wp.IsHotForge)
            {
                if (!BloomeryThermodynamicsSystem.ApplyOreCrushHammerStrike(
                        ref wp.OreSpot, out bool shatteredIntoPowder))
                {
                    return;
                }

                _pendingStaminaDrain += 1.5f;
                TriggerSparkBurst(wp.SparkBurst, new Vector3(0f, 0.08f, 0f));
                PlayProceduralSfx(playerTransform, SfxKind.StoneCrush);

                if (shatteredIntoPowder)
                {
                    byte oreMat = wp.OreSpot.OreMaterialId;
                    PhysicalItemShapeDna sourceDna = wp.OreSpot.SourceDna;
                    Vector3 dropPos = wp.WorldPos;

                    if (wp.RootGo != null) UnityEngine.Object.Destroy(wp.RootGo);
                    _workpieces.RemoveAt(wpIdx);

                    int added = 0;
                    for (int i = 0; i < 2; i++)
                    {
                        PhysicalItemShapeDna powderDna = sourceDna.DeriveStackInstance(i).WithTemperature(0);
                        if (holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(
                                InventoryItemKind.CrushedOrePowder, oreMat, 1, miningCtrl, powderDna))
                        {
                            added++;
                        }
                        else
                        {
                            miningCtrl.SpawnPhysicalWorldItem(
                                dropPos + new Vector3((i - 0.5f) * 0.14f, 0.08f, 0f),
                                InventoryItemKind.CrushedOrePowder, oreMat, 1, powderDna, sharedVoxelMat);
                        }
                    }

                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
                    string oreLabel = Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(oreMat);
                    miningCtrl.LastFeedbackText = added == 2
                        ? $"⛏️ CEVHER KIRILDI (3/3): +2x Kırılmış {oreLabel} Cevheri! Şimdi İzabe Fırınına üstten doldur."
                        : $"⛏️ CEVHER KIRILDI (3/3): {added}x çantaya, {2 - added}x yere düştü!";
                }
                else
                {
                    miningCtrl.LastFeedbackText =
                        $"⛏️ Çekiç Darbesi ({wp.OreSpot.HammerStrikeCount}/{OreCrushingSpotState.RequiredHammerStrikes}) — kavrulmuş taş çatlıyor!";
                }
                return;
            }

            // HotForgeWorkpieceState strike
            if (wp.ForgeState.IsCompletedWroughtBar)
            {
                miningCtrl.LastFeedbackText = "⚒️ Dövme Demir Lama zaten tamamlandı! [E] tuşu ile alabilirsin.";
                return;
            }

            bool hitApplied = BloomeryThermodynamicsSystem.ApplyHotForgeStrike(
                ref wp.ForgeState, out bool rejectedCold, out bool completedWroughtBar);

            if (rejectedCold)
            {
                PlayProceduralSfx(playerTransform, SfxKind.StoneCrush);
                miningCtrl.LastFeedbackText =
                    $"❄️ ÇEKİÇ SEKTİ ({wp.ForgeState.TemperatureCelsius:0} °C < 720 °C): Sünger Demir soğudu! [E] ile alıp yanan İzabe Fırınına (>=850 °C) [Sağ Tık] ile ısıt!";
                return;
            }

            if (!hitApplied) return;

            _pendingStaminaDrain += 2.0f;
            TriggerSparkBurst(wp.SparkBurst, new Vector3(0f, 0.08f, 0f));
            PlayProceduralSfx(playerTransform, SfxKind.MetallicClang);
            RefreshHotForgeWorkpieceMesh(wp);

            if (completedWroughtBar)
            {
                miningCtrl.LastFeedbackText =
                    "⚒️ DÖVME DEMİR LAMA HAZIR (8/8)! Cüruftan arındırıldı — [E] tuşu ile al!";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"⚒️ SICAK DÖVME ({wp.ForgeState.CompletedStrikes}/{HotForgeWorkpieceState.RequiredHotStrikes} | {wp.ForgeState.TemperatureCelsius:0} °C) — kütle yassılaşıyor!";
            }
        }

        private void StepCharcoalPits(float dt, float timeNow, WorldLooseBuildSystem looseBuildSystem)
        {
            for (int i = 0; i < _charcoalPits.Count; i++)
            {
                CharcoalPitRuntimeEntry pit = _charcoalPits[i];
                if (pit.RootGo == null) continue;

                BloomeryThermodynamicsSystem.StepCharcoalPit(ref pit.Data, dt);

                if (pit.Data.Phase != pit.LastRenderedPhase)
                {
                    pit.LastRenderedPhase = pit.Data.Phase;
                    if (pit.Data.Phase == CharcoalPitPhase.Smoldering)
                    {
                        SetCharcoalPitSmokeActive(pit, true, isReadyBlue: false);
                        if (pit.WarmPointLight != null) pit.WarmPointLight.enabled = true;
                        looseBuildSystem.UpdateStationAllThermalGlow(pit.Data.StationId, 135);
                    }
                    else if (pit.Data.Phase == CharcoalPitPhase.ReadyCharcoal)
                    {
                        SetCharcoalPitSmokeActive(pit, true, isReadyBlue: true);
                        if (pit.WarmPointLight != null) pit.WarmPointLight.enabled = false;
                        looseBuildSystem.UpdateStationAllThermalGlow(pit.Data.StationId, 65);
                    }
                    else
                    {
                        SetCharcoalPitSmokeActive(pit, false, isReadyBlue: false);
                        if (pit.WarmPointLight != null) pit.WarmPointLight.enabled = false;
                        looseBuildSystem.UpdateStationAllThermalGlow(pit.Data.StationId, 0);
                    }
                }

                if (pit.Data.Phase == CharcoalPitPhase.Smoldering || pit.Data.Phase == CharcoalPitPhase.ReadyCharcoal)
                {
                    bool isBlue = pit.Data.Phase == CharcoalPitPhase.ReadyCharcoal;
                    float speed = isBlue ? 0.45f : 0.68f;
                    float baseScale = isBlue ? 0.56f : 1.08f;

                    for (int m = 0; m < pit.SmokeMotes.Length; m++)
                    {
                        Transform mt = pit.SmokeMotes[m];
                        float cycle = Mathf.Repeat(timeNow * speed + m * 0.25f, 1.0f);
                        float swayX = Mathf.Sin(timeNow * 1.9f + m * 1.7f) * (0.05f + cycle * 0.16f);
                        float swayZ = Mathf.Cos(timeNow * 1.6f + m * 2.1f) * (0.05f + cycle * 0.16f);
                        mt.localPosition = new Vector3(swayX, 0.28f + cycle * (isBlue ? 1.45f : 1.75f), swayZ);
                        float sc = Mathf.Sin(cycle * Mathf.PI) * baseScale;
                        mt.localScale = new Vector3(sc, sc, sc);
                    }

                    if (pit.WarmPointLight != null && pit.Data.Phase == CharcoalPitPhase.Smoldering)
                    {
                        pit.WarmPointLight.intensity = 0.65f + 0.15f * Mathf.Sin(timeNow * 7.5f + pit.Data.StationId);
                    }
                }

                StepSparkBurst(pit.SparkBurst, dt);
            }
        }

        private void StepBloomeries(float dt, float timeNow, WorldLooseBuildSystem looseBuildSystem)
        {
            for (int i = 0; i < _bloomeries.Count; i++)
            {
                BloomeryRuntimeEntry b = _bloomeries[i];
                if (b.RootGo == null) continue;

                BloomeryThermodynamicsSystem.StepBloomery(ref b.Data, dt);

                if (b.FlameGo.activeSelf != b.Data.IsLit)
                {
                    b.FlameGo.SetActive(b.Data.IsLit);
                }

                if (b.ClayPlugGo.activeSelf != b.Data.IsFrontClayPlugIntact)
                {
                    b.ClayPlugGo.SetActive(b.Data.IsFrontClayPlugIntact);
                }

                bool showSlag = b.Data.SlagTappedVisual01 > 0.02f || b.Data.HasTappedSlagShard;
                if (b.SlagStreamGo.activeSelf != showSlag)
                {
                    b.SlagStreamGo.SetActive(showSlag);
                }
                if (showSlag)
                {
                    float slagScale = Mathf.Lerp(0.35f, 1.05f, Mathf.Clamp01(b.Data.SlagTappedVisual01));
                    b.SlagStreamGo.transform.localScale = new Vector3(slagScale, 1.0f, slagScale);
                }

                if (b.BloomCoreGo.activeSelf != b.Data.HasReadyIronBloom)
                {
                    b.BloomCoreGo.SetActive(b.Data.HasReadyIronBloom);
                }

                if (b.Data.IsLit)
                {
                    float temp01 = Mathf.Clamp01((b.Data.TemperatureCelsius - 400.0f) / 900.0f);
                    float flicker = Mathf.Sin(timeNow * 16.5f + b.Data.StationId * 1.3f) * 0.12f;
                    float airBoost = b.Data.BellowsAirflow01 * 0.65f;
                    float hScale = Mathf.Lerp(0.55f, 1.45f, temp01) + airBoost + flicker;
                    float wScale = Mathf.Lerp(0.72f, 1.22f, temp01) + airBoost * 0.25f;
                    b.FlameGo.transform.localScale = new Vector3(wScale, Mathf.Max(0.25f, hScale), wScale);
                    b.FlameGo.transform.localRotation = Quaternion.Euler(0f, (timeNow * 42f + b.Data.StationId * 31f) % 360f, 0f);
                }

                bool lightOn = b.Data.IsLit || b.Data.HasReadyIronBloom;
                if (b.WarmPointLight != null)
                {
                    if (b.WarmPointLight.enabled != lightOn) b.WarmPointLight.enabled = lightOn;
                    if (lightOn)
                    {
                        float temp01 = b.Data.IsLit
                            ? Mathf.Clamp01((b.Data.TemperatureCelsius - 350.0f) / 950.0f)
                            : 0.45f;
                        float flicker = Mathf.Sin(timeNow * 14.2f + b.Data.StationId) * 0.16f;
                        b.WarmPointLight.intensity = Mathf.Lerp(0.9f, 2.8f, temp01) + flicker;
                        b.WarmPointLight.range = Mathf.Lerp(5.5f, 8.2f, temp01);
                    }
                }

                if (b.BellowsPumpAnimTimer > 0f)
                {
                    b.BellowsPumpAnimTimer = Mathf.Max(0f, b.BellowsPumpAnimTimer - dt);
                    float p01 = b.BellowsPumpAnimTimer / 0.42f;
                    float squeeze = Mathf.Sin(p01 * Mathf.PI);
                    b.BellowsGo.transform.localScale = new Vector3(
                        b.BaseBellowsScale.x * (1.0f + 0.12f * squeeze),
                        b.BaseBellowsScale.y * (1.0f - 0.38f * squeeze),
                        b.BaseBellowsScale.z * (1.0f + 0.08f * squeeze));
                }
                else if (b.BellowsGo.transform.localScale != b.BaseBellowsScale)
                {
                    b.BellowsGo.transform.localScale = b.BaseBellowsScale;
                }

                byte targetGlowByte = b.Data.IsLit
                    ? BloomeryThermodynamicsSystem.TemperatureToUberByte(b.Data.TemperatureCelsius)
                    : (byte)0;
                if (targetGlowByte != b.LastThermalGlowByte)
                {
                    b.LastThermalGlowByte = targetGlowByte;
                    looseBuildSystem.UpdateStationAllThermalGlow(b.Data.StationId, targetGlowByte);
                }

                StepSparkBurst(b.SparkBurst, dt);
            }
        }

        private void StepWorkpieces(float dt)
        {
            for (int i = 0; i < _workpieces.Count; i++)
            {
                SurfaceWorkpieceEntry wp = _workpieces[i];
                if (wp.RootGo == null) continue;

                if (wp.IsHotForge)
                {
                    BloomeryThermodynamicsSystem.StepHotForgeWorkpiece(ref wp.ForgeState, dt);
                    if (wp.ForgeState.ShapeDna.ThermalTempByte != wp.LastRenderedTempByte ||
                        wp.ForgeState.ShapeDna.StageByte != wp.LastRenderedStageByte ||
                        wp.ForgeState.IsCompletedWroughtBar != wp.LastCompletedBar)
                    {
                        RefreshHotForgeWorkpieceMesh(wp);
                    }
                }

                StepSparkBurst(wp.SparkBurst, dt);
            }
        }

        private static void RefreshHotForgeWorkpieceMesh(SurfaceWorkpieceEntry wp)
        {
            wp.LastRenderedStageByte = wp.ForgeState.ShapeDna.StageByte;
            wp.LastRenderedTempByte = wp.ForgeState.ShapeDna.ThermalTempByte;
            wp.LastCompletedBar = wp.ForgeState.IsCompletedWroughtBar;

            PhysicalItemArchetype arch = wp.ForgeState.IsCompletedWroughtBar
                ? PhysicalItemArchetype.WroughtIronBar
                : PhysicalItemArchetype.IronBloomSponge;

            PhysicalItemShapeDna renderDna = (!wp.ForgeState.IsCompletedWroughtBar && wp.ForgeState.ShapeDna.ThermalTempByte == 0)
                ? wp.ForgeState.ShapeDna.WithTemperature(1)
                : wp.ForgeState.ShapeDna;
            Mesh m = PhysicalItemMeshFactory.GetOrCreateMesh(arch, 0, in renderDna, out Vector3 natScale);
            Vector3 groundScale = PhysicalItemMeshFactory.GetWorldGroundScale(arch, natScale);
            wp.MeshFilter.sharedMesh = m;
            wp.BaseGroundScale = groundScale;

            if (wp.ForgeState.IsCompletedWroughtBar)
            {
                wp.MeshChildGo.transform.localScale = groundScale;
            }
            else
            {
                float progress01 = Mathf.Clamp01(
                    wp.ForgeState.ShapeDna.StageByte / (float)HotForgeWorkpieceState.RequiredHotStrikes);
                wp.MeshChildGo.transform.localScale = new Vector3(
                    groundScale.x * Mathf.Lerp(1.0f, 0.72f, progress01),
                    groundScale.y * Mathf.Lerp(1.0f, 0.42f, progress01),
                    groundScale.z * Mathf.Lerp(1.0f, 1.48f, progress01));
            }
        }

        private void SetCharcoalPitSmokeActive(CharcoalPitRuntimeEntry pit, bool active, bool isReadyBlue)
        {
            Mesh? targetMesh = isReadyBlue ? _sharedBlueThinSmokeMesh : _sharedThickGreySmokeMesh;
            for (int m = 0; m < pit.SmokeMotes.Length; m++)
            {
                if (pit.SmokeMotes[m] == null) continue;
                if (active && targetMesh != null && pit.SmokeMoteMfs[m] != null && pit.SmokeMoteMfs[m].sharedMesh != targetMesh)
                {
                    pit.SmokeMoteMfs[m].sharedMesh = targetMesh;
                }
                pit.SmokeMotes[m].gameObject.SetActive(active);
            }
        }

        private int FindAimedCharcoalPitIndex(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach)
        {
            if (_charcoalPits.Count == 0) return -1;
            GetRay(cam, playerPos, out Vector3 rayOrigin, out Vector3 rayDir);

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _charcoalPits.Count; i++)
            {
                CharcoalPitRuntimeEntry pit = _charcoalPits[i];
                if (pit.RootGo == null) continue;
                Vector3 center = pit.RootGo.transform.position + new Vector3(0f, 0.24f, 0f);
                if (TryScoreRayTarget(rayOrigin, rayDir, playerPos, center, maxReach, 0.78f, out float score) &&
                    score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private int FindAimedBloomeryIndex(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach)
        {
            if (_bloomeries.Count == 0) return -1;
            GetRay(cam, playerPos, out Vector3 rayOrigin, out Vector3 rayDir);

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _bloomeries.Count; i++)
            {
                BloomeryRuntimeEntry b = _bloomeries[i];
                if (b.RootGo == null) continue;
                Vector3 center = b.RootGo.transform.position + new Vector3(0f, 0.34f, 0f);
                if (TryScoreRayTarget(rayOrigin, rayDir, playerPos, center, maxReach, 0.85f, out float score) &&
                    score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private int FindAimedWorkpieceIndex(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Transform? anvilTransform,
            float maxReach)
        {
            if (_workpieces.Count == 0) return -1;
            GetRay(cam, playerPos, out Vector3 rayOrigin, out Vector3 rayDir);
            Vector3? anvilTop = anvilTransform != null ? anvilTransform.position + Vector3.up * 0.62f : null;

            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _workpieces.Count; i++)
            {
                SurfaceWorkpieceEntry wp = _workpieces[i];
                if (wp.RootGo == null) continue;
                Vector3 center = wp.WorldPos + new Vector3(0f, 0.05f, 0f);
                float radius = anvilTop.HasValue && Vector3.Distance(wp.WorldPos, anvilTop.Value) < 0.35f ? 0.52f : 0.44f;
                if (TryScoreRayTarget(rayOrigin, rayDir, playerPos, center, maxReach, radius, out float score) &&
                    score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private static bool TryResolveAimedForgingSurface(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Transform? anvilTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            out Vector3 surfacePos,
            out string surfaceLabel)
        {
            surfacePos = Vector3.zero;
            surfaceLabel = string.Empty;
            GetRay(cam, playerPos, out Vector3 rayOrigin, out Vector3 rayDir);

            // 1) Anvil top surface (anvilTransform.position + Vector3.up * 0.62f)
            if (anvilTransform != null)
            {
                Vector3 anvilTop = anvilTransform.position + Vector3.up * 0.62f;
                if (TryScoreRayTarget(rayOrigin, rayDir, playerPos, anvilTop, 3.15f, 0.56f, out _))
                {
                    surfacePos = anvilTop;
                    surfaceLabel = "Örs";
                    return true;
                }
            }

            // 2) Any loose RoughStoneChunk (WorldLooseBuildSystem OR dropped ground stone)
            float bestStoneScore = float.MaxValue;
            Vector3 bestStonePos = Vector3.zero;
            bool foundStone = false;

            var parts = looseBuildSystem.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.PartKind != LooseBuildPartKind.RoughStoneChunk || p.VisualGo == null) continue;
                if (TryScoreRayTarget(rayOrigin, rayDir, playerPos, p.Position, 3.15f, 0.48f, out float sc) &&
                    sc < bestStoneScore)
                {
                    bestStoneScore = sc;
                    bestStonePos = p.Position + Vector3.up * 0.08f;
                    foundStone = true;
                }
            }

            int droppedStoneIdx = miningCtrl.FindAimedGroundRoughStoneIndex(cam, playerPos, 3.15f);
            if (droppedStoneIdx >= 0 &&
                miningCtrl.TryGetDroppedWorldItemInfo(droppedStoneIdx, out Vector3 dPos, out _, out _, out _, out _))
            {
                if (TryScoreRayTarget(rayOrigin, rayDir, playerPos, dPos, 3.15f, 0.52f, out float dScore) &&
                    dScore < bestStoneScore)
                {
                    bestStoneScore = dScore;
                    bestStonePos = dPos + Vector3.up * 0.08f;
                    foundStone = true;
                }
            }

            if (foundStone)
            {
                surfacePos = bestStonePos;
                surfaceLabel = "Düz Taş";
                return true;
            }

            // 3) Flat stone terrain voxel (Stone, Basalt, Cobblestone, Marble, StoneBrick within 3.15m, normal.y >= 0.72f)
            if (miningCtrl.HasAimHit &&
                miningCtrl.AimHit.distance <= 3.15f &&
                miningCtrl.AimHit.normal.y >= 0.72f &&
                IsFlatStoneMaterial(miningCtrl.AimMaterialId))
            {
                surfacePos = miningCtrl.AimHit.point + Vector3.up * 0.03f;
                surfaceLabel = "Düz Kaya";
                return true;
            }

            return false;
        }

        private static bool IsFlatStoneMaterial(byte mat)
        {
            return mat == MaterialIdCatalog.Stone ||
                   mat == MaterialIdCatalog.Basalt ||
                   mat == MaterialIdCatalog.Cobblestone ||
                   mat == MaterialIdCatalog.Marble ||
                   mat == MaterialIdCatalog.StoneBrick;
        }

        private static void GetRay(UnityEngine.Camera? cam, Vector3 playerPos, out Vector3 rayOrigin, out Vector3 rayDir)
        {
            rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();
        }

        private static bool TryScoreRayTarget(
            Vector3 rayOrigin,
            Vector3 rayDir,
            Vector3 playerPos,
            Vector3 targetPos,
            float maxReach,
            float maxPerpRadius,
            out float score)
        {
            score = float.MaxValue;
            float distPlayer = Vector3.Distance(playerPos + new Vector3(0f, 0.85f, 0f), targetPos);
            float distCam = Vector3.Distance(rayOrigin, targetPos);
            if (distPlayer > maxReach && distCam > maxReach + 0.50f) return false;

            Vector3 toTarget = targetPos - rayOrigin;
            float along = Vector3.Dot(toTarget, rayDir);
            if (along < -0.15f || along > maxReach + 0.65f) return false;

            Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
            float perp = Vector3.Distance(closest, targetPos);
            if (perp > maxPerpRadius) return false;

            score = perp * 2.1f + along * 0.25f;
            return true;
        }

        private static bool ConsumeActiveItem(
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1))
                {
                    return false;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }
            return true;
        }

        private static int CountTotalStrikerStones(Stage1HolsterAndBackpackController holsterCtrl)
        {
            int total = 0;
            var ui = holsterCtrl.InventoryUi;
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.HotbarSlotCount; i++)
            {
                InventoryItemKind k = ui.HotbarSlots[i].Kind;
                if ((k == InventoryItemKind.FlintStone || k == InventoryItemKind.RoughStoneChunk) && ui.HotbarSlots[i].Count > 0)
                {
                    total += ui.HotbarSlots[i].Count;
                }
            }
            for (int i = 0; i < Stage1VisualHotbarAndInventoryUi.BackpackSlotCount; i++)
            {
                InventoryItemKind k = ui.BackpackSlots[i].Kind;
                if ((k == InventoryItemKind.FlintStone || k == InventoryItemKind.RoughStoneChunk) && ui.BackpackSlots[i].Count > 0)
                {
                    total += ui.BackpackSlots[i].Count;
                }
            }
            return total;
        }

        private void InitSparkBurstPool(Transform parent, SparkParticleBurst burst, string prefix, Material? sharedVoxelMat)
        {
            for (int k = 0; k < burst.Shards.Length; k++)
            {
                GameObject spGo = new GameObject($"{prefix}_{k}");
                spGo.transform.SetParent(parent, false);
                MeshFilter spMf = spGo.AddComponent<MeshFilter>();
                spMf.sharedMesh = _sharedSparkShardMesh;
                MeshRenderer spMr = spGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) spMr.sharedMaterial = sharedVoxelMat;
                spMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spGo.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                spGo.SetActive(false);
                burst.Shards[k] = spGo.transform;
            }
        }

        private static void TriggerSparkBurst(SparkParticleBurst burst, Vector3 localOrigin)
        {
            burst.RemainingSec = 0.42f;
            for (int k = 0; k < burst.Shards.Length; k++)
            {
                float ang = (k / (float)burst.Shards.Length) * Mathf.PI * 2f + Time.time * 3.1f;
                float speedH = 0.85f + (k % 3) * 0.35f;
                Transform sh = burst.Shards[k];
                sh.localPosition = localOrigin;
                burst.Velocities[k] = new Vector3(
                    Mathf.Cos(ang) * speedH,
                    1.65f + (k % 2) * 0.55f,
                    Mathf.Sin(ang) * speedH);
                sh.gameObject.SetActive(true);
            }
        }

        private static void StepSparkBurst(SparkParticleBurst burst, float dt)
        {
            if (burst.RemainingSec <= 0f) return;
            burst.RemainingSec -= dt;
            bool active = burst.RemainingSec > 0f;
            for (int k = 0; k < burst.Shards.Length; k++)
            {
                Transform sh = burst.Shards[k];
                if (!active)
                {
                    if (sh.gameObject.activeSelf) sh.gameObject.SetActive(false);
                    continue;
                }
                burst.Velocities[k] += new Vector3(0f, -6.8f * dt, 0f);
                sh.localPosition += burst.Velocities[k] * dt;
                sh.Rotate(380f * dt, 240f * dt, 0f, Space.Self);
            }
        }

        private void EnsureSharedMeshesAndContainer()
        {
            if (_stationsContainer == null)
            {
                GameObject existing = GameObject.Find("06_Faz23_WorldLooseBuildRoot");
                if (existing == null) existing = new GameObject("06_Faz23_WorldLooseBuildRoot");
                _stationsContainer = existing.transform;
            }

            if (_sharedSparkShardMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.025f, 0f), new SysVec3(0f, 0.035f, 0f), 0.022f, MaterialIdCatalog.MagmaBlock, 255, _meshBuildScratch);
                _sharedSparkShardMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_BloomerySparkShard");
            }

            if (_sharedThickGreySmokeMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.05f, 0f), new SysVec3(0f, 0.07f, 0f), 0.068f, MaterialIdCatalog.Marble, 0, _meshBuildScratch);
                _sharedThickGreySmokeMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_CharcoalPitGreySmoke");
            }

            if (_sharedBlueThinSmokeMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.04f, 0f), new SysVec3(0f, 0.06f, 0f), 0.038f, MaterialIdCatalog.AstralOre, 140, _meshBuildScratch);
                _sharedBlueThinSmokeMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_CharcoalPitBlueSmoke");
            }

            if (_sharedBloomeryFlameMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, 0.01f, 0f), new SysVec3(0f, 0.42f, 0f), 0.13f, MaterialIdCatalog.AstralOre, 240, _meshBuildScratch);
                EmitConeTongue(new SysVec3(-0.06f, 0.02f, 0.04f), new SysVec3(-0.01f, 0.30f, 0.01f), 0.085f, MaterialIdCatalog.MagmaBlock, 250, _meshBuildScratch);
                EmitConeTongue(new SysVec3(0.06f, 0.02f, -0.04f), new SysVec3(0.01f, 0.28f, -0.01f), 0.080f, MaterialIdCatalog.MagmaBlock, 245, _meshBuildScratch);
                _sharedBloomeryFlameMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_BloomeryCOFlame");
            }

            if (_sharedSlagStreamMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, 0.02f, 0f), new SysVec3(0f, 0.0f, 0.24f), 0.065f, MaterialIdCatalog.MagmaBlock, 240, _meshBuildScratch);
                _sharedSlagStreamMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_BloomerySlagStream");
            }

            if (_sharedPlugClayMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.04f, -0.02f), new SysVec3(0f, 0.04f, 0.06f), 0.095f, MaterialIdCatalog.Clay, 0, _meshBuildScratch);
                _sharedPlugClayMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_BloomeryClayPlug");
            }
        }

        private static void EmitConeTongue(
            SysVec3 baseCenter,
            SysVec3 apex,
            float baseRadius,
            byte matId,
            byte tempByte,
            List<FacetedVoxelTriangle> outTris)
        {
            const int sides = 5;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                SysVec3 p0 = baseCenter + new SysVec3(MathF.Cos(a0) * baseRadius, 0f, MathF.Sin(a0) * baseRadius);
                SysVec3 p1 = baseCenter + new SysVec3(MathF.Cos(a1) * baseRadius, 0f, MathF.Sin(a1) * baseRadius);

                SysVec3 n = SysVec3.Cross(p1 - p0, apex - p0);
                n = n.LengthSquared() > 1e-6f ? SysVec3.Normalize(n) : SysVec3.UnitY;
                outTris.Add(new FacetedVoxelTriangle
                {
                    V0 = p0,
                    V1 = p1,
                    V2 = apex,
                    Normal = n,
                    MaterialId = matId,
                    Temperature = tempByte,
                    FacetShade = 235,
                    AmbientOcclusion = 0xE2
                });
            }
        }

        private enum SfxKind : byte
        {
            FlintStrike = 0,
            IgniteWhoosh = 1,
            BellowsWhoosh = 2,
            MetallicClang = 3,
            StoneCrush = 4
        }

        private void PlayProceduralSfx(Transform listenerRef, SfxKind kind)
        {
            if (_sfxSource == null && listenerRef != null)
            {
                _sfxSource = listenerRef.GetComponent<AudioSource>();
                if (_sfxSource == null)
                {
                    _sfxSource = listenerRef.gameObject.AddComponent<AudioSource>();
                    _sfxSource.spatialBlend = 0f;
                    _sfxSource.playOnAwake = false;
                }
            }
            if (_sfxSource == null) return;

            const int sampleRate = 22050;
            switch (kind)
            {
                case SfxKind.FlintStrike:
                {
                    if (_flintStrikeClip == null)
                    {
                        int samples = sampleRate / 12;
                        float[] pcm = new float[samples];
                        uint rng = 0x91A34Bu;
                        for (int i = 0; i < samples; i++)
                        {
                            rng = rng * 1664525u + 1013904223u;
                            float noise = ((rng & 0xFFFFu) / 32768f) - 1f;
                            float env = MathF.Exp(-i / (samples * 0.16f));
                            float ring = MathF.Sin(i * 0.62f) * 0.55f + MathF.Sin(i * 1.17f) * 0.35f;
                            pcm[i] = (noise * 0.45f + ring * 0.55f) * env * 0.42f;
                        }
                        _flintStrikeClip = AudioClip.Create("Sfx_BloomeryFlintClack", samples, 1, sampleRate, false);
                        _flintStrikeClip.SetData(pcm, 0);
                    }
                    _sfxSource.PlayOneShot(_flintStrikeClip, 0.65f);
                    break;
                }
                case SfxKind.IgniteWhoosh:
                {
                    if (_igniteWhooshClip == null)
                    {
                        int samples = sampleRate / 4;
                        float[] pcm = new float[samples];
                        uint rng = 0x55C19Fu;
                        float lp = 0f;
                        for (int i = 0; i < samples; i++)
                        {
                            rng = rng * 1664525u + 1013904223u;
                            float noise = ((rng & 0xFFFFu) / 32768f) - 1f;
                            lp = lp * 0.82f + noise * 0.18f;
                            float t = i / (float)samples;
                            float env = MathF.Sin(t * MathF.PI) * MathF.Exp(-t * 1.6f);
                            pcm[i] = lp * env * 0.85f;
                        }
                        _igniteWhooshClip = AudioClip.Create("Sfx_BloomeryIgniteWhoosh", samples, 1, sampleRate, false);
                        _igniteWhooshClip.SetData(pcm, 0);
                    }
                    _sfxSource.PlayOneShot(_igniteWhooshClip, 0.75f);
                    break;
                }
                case SfxKind.BellowsWhoosh:
                {
                    if (_bellowsWhooshClip == null)
                    {
                        int samples = (sampleRate * 32) / 100;
                        float[] pcm = new float[samples];
                        uint rng = 0x3377A1u;
                        float lp = 0f;
                        for (int i = 0; i < samples; i++)
                        {
                            rng = rng * 1664525u + 1013904223u;
                            float noise = ((rng & 0xFFFFu) / 32768f) - 1f;
                            lp = lp * 0.76f + noise * 0.24f;
                            float t = i / (float)samples;
                            float env = MathF.Sin(t * MathF.PI) * (0.65f + 0.35f * MathF.Exp(-t * 2.2f));
                            pcm[i] = lp * env * 0.80f;
                        }
                        _bellowsWhooshClip = AudioClip.Create("Sfx_BloomeryBellowsWhoosh", samples, 1, sampleRate, false);
                        _bellowsWhooshClip.SetData(pcm, 0);
                    }
                    _sfxSource.PlayOneShot(_bellowsWhooshClip, 0.75f);
                    break;
                }
                case SfxKind.MetallicClang:
                {
                    if (_metallicClangClip == null)
                    {
                        int samples = (sampleRate * 28) / 100;
                        float[] pcm = new float[samples];
                        uint rng = 0x7E123Du;
                        for (int i = 0; i < samples; i++)
                        {
                            rng = rng * 1664525u + 1013904223u;
                            float noise = ((rng & 0xFFFFu) / 32768f) - 1f;
                            float tSec = i / (float)sampleRate;
                            float hitEnv = MathF.Exp(-tSec * 45.0f);
                            float ringEnv = MathF.Exp(-tSec * 9.5f);
                            float clang = MathF.Sin(tSec * 2f * MathF.PI * 520f) * 0.50f
                                        + MathF.Sin(tSec * 2f * MathF.PI * 1065f) * 0.32f
                                        + MathF.Sin(tSec * 2f * MathF.PI * 1690f) * 0.18f;
                            pcm[i] = (noise * hitEnv * 0.38f + clang * ringEnv * 0.62f) * 0.60f;
                        }
                        _metallicClangClip = AudioClip.Create("Sfx_AnvilHammerClang", samples, 1, sampleRate, false);
                        _metallicClangClip.SetData(pcm, 0);
                    }
                    _sfxSource.PlayOneShot(_metallicClangClip, 0.78f);
                    break;
                }
                case SfxKind.StoneCrush:
                {
                    if (_stoneCrushClip == null)
                    {
                        int samples = sampleRate / 6;
                        float[] pcm = new float[samples];
                        uint rng = 0xA812E3u;
                        for (int i = 0; i < samples; i++)
                        {
                            rng = rng * 1664525u + 1013904223u;
                            float noise = ((rng & 0xFFFFu) / 32768f) - 1f;
                            float tSec = i / (float)sampleRate;
                            float env = MathF.Exp(-tSec * 22.0f);
                            float thud = MathF.Sin(tSec * 2f * MathF.PI * 165f) * 0.45f;
                            pcm[i] = (noise * 0.55f + thud) * env * 0.60f;
                        }
                        _stoneCrushClip = AudioClip.Create("Sfx_StoneOreCrush", samples, 1, sampleRate, false);
                        _stoneCrushClip.SetData(pcm, 0);
                    }
                    _sfxSource.PlayOneShot(_stoneCrushClip, 0.70f);
                    break;
                }
            }
        }

        private void EnsureGuiStyles()
        {
            if (_promptBoxStyle != null) return;
            _promptBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 6, 6)
            };
            _promptTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.0f, 0.95f, 0.82f) }
            };
        }

        private void DrawPromptBanner(string line1, string line2)
        {
            float w = 580f;
            float h = string.IsNullOrEmpty(line2) ? 34f : 52f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.56f;

            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.07f, 0.06f, 0.82f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _promptBoxStyle!);
            GUI.color = prev;

            if (string.IsNullOrEmpty(line2))
            {
                GUI.Label(new Rect(x + 8f, y + 6f, w - 16f, 22f), line1, _promptTextStyle!);
            }
            else
            {
                GUI.Label(new Rect(x + 8f, y + 5f, w - 16f, 20f), line1, _promptTextStyle!);
                Color subPrev = _promptTextStyle!.normal.textColor;
                _promptTextStyle.normal.textColor = new Color(0.84f, 0.92f, 0.98f);
                GUI.Label(new Rect(x + 8f, y + 26f, w - 16f, 20f), line2, _promptTextStyle);
                _promptTextStyle.normal.textColor = subPrev;
            }
        }
    }
}
#endif
