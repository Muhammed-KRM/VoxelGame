#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using SysVec3 = System.Numerics.Vector3;

#if UNITY_5_3_OR_NEWER
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    public enum CampfirePayloadKind : byte
    {
        Meat = 0,
        OreRoast = 1,
        ClayFiring = 2,
        IronReheat = 3
    }

    /// <summary>
    /// Kamp atesi uzerindeki tek bir et / cevher / seramik / demir parcasinin 3 asamali fiziksel pisirme durumu.
    /// </summary>
    public enum CookingMeatStage : byte
    {
        Empty = 0,
        RawSizzling = 1, // 0 .. 12 sn: Cig, cizirdayan kirmizi et / Kavrulmakta / Pisirilmekte / Isinmakta
        Cooked = 2,      // 12 .. 26 sn: Pismis Et (+35 HP, +40 EP) / Kavrulmus Cevher / Pismis Kil / Akkor Demir
        Burnt = 3        // > 26 sn: Yalnizca Et icin komurlesmis siyah yanik et (0 besin degeri)
    }

    /// <summary>
    /// Kamp atesi uzerindeki 3 fiziksel pisirme yuvasindan birinin verisi.
    /// </summary>
    public struct CampfireCookingSlotState
    {
        public CookingMeatStage Stage;
        public float ElapsedCookSec;
        public PhysicalItemShapeDna ItemDna;
        public CampfirePayloadKind PayloadKind;
        public byte SourceItemKindByte;
        public byte SubMaterialId;

        public const float CookedThresholdSec = 12.0f;
        public const float BurntThresholdSec = 26.0f;
        public const float OreRoastThresholdSec = 10.0f;
        public const float ClayFireThresholdSec = 12.0f;
        public const float IronReheatThresholdSec = 8.0f;
    }

    /// <summary>
    /// Muhurlenmis bir fiziksel Kamp Atesi istasyonunun saf simülasyon durumu.
    /// </summary>
    public struct CampfireStationData
    {
        public uint StationId;
        public SysVec3 Center;
        public float BaseY;
        public bool IsLit;
        public int SparkStrikeCount;
        public float RemainingFuelSec;
        public float EfficiencyBonus01;
        public bool HasStoneRing;
        public float TemperatureCelsius;
        public CampfireCookingSlotState Slot0;
        public CampfireCookingSlotState Slot1;
        public CampfireCookingSlotState Slot2;

        public const int MaxCookingSlots = 3;
        public const int RequiredSparkStrikes = 3;
        public const float SparkStaminaCost = 1.0f;
        public const float InitialFuelSeconds = 180.0f;
        public const float StickFuelBonusSeconds = 60.0f;
        public const float SplitFirewoodFuelBonusSeconds = 140.0f;
        public const float MaxFuelSeconds = 600.0f;

        public readonly CampfireCookingSlotState GetSlot(int index) => index switch
        {
            0 => Slot0,
            1 => Slot1,
            2 => Slot2,
            _ => default
        };

        public void SetSlot(int index, in CampfireCookingSlotState value)
        {
            switch (index)
            {
                case 0: Slot0 = value; break;
                case 1: Slot1 = value; break;
                case 2: Slot2 = value; break;
            }
        }
    }

    /// <summary>
    /// Faz 2.3 (Asama 3.4 &amp; 3.5): Bagimsiz .NET 10 ve Unity EditMode testlerinde dogrudan kosturulabilen
    /// sifir-GC kamp atesi kivilcim tutusturma, yakit besleme ve 3B et pisirme durum makinesi.
    /// </summary>
    public static class CampfireSimulationLogic
    {
        public static CampfireStationData CreateUnlitStation(
            uint stationId,
            SysVec3 clusterCenter,
            float baseY,
            float efficiencyBonus01,
            bool hasStoneRing)
        {
            return new CampfireStationData
            {
                StationId = stationId,
                Center = clusterCenter,
                BaseY = baseY,
                IsLit = false,
                SparkStrikeCount = 0,
                RemainingFuelSec = 0f,
                EfficiencyBonus01 = Math.Clamp(efficiencyBonus01, 0f, 0.50f),
                HasStoneRing = hasStoneRing,
                TemperatureCelsius = 20.0f
            };
        }

        public const string ExtinguishedToastMessage = "Kamp ateşi söndürüldü (yakıt korundu).";
        public const float LightRainThresholdMmPerHour = 0.5f;
        public const float HeavyRainExtinguishThresholdMmPerHour = 6.0f;

        public static bool ApplyFlintSparkStrike(ref CampfireStationData station, out bool justIgnited)
        {
            return ApplyFlintSparkStrike(ref station, precipMmPerHour: 0f, hasOpenSkyAbove: false, out justIgnited);
        }

        public static bool ApplyFlintSparkStrike(
            ref CampfireStationData station,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            out bool justIgnited)
        {
            justIgnited = false;
            if (station.IsLit) return false;

            if (hasOpenSkyAbove && precipMmPerHour >= HeavyRainExtinguishThresholdMmPerHour)
            {
                station.SparkStrikeCount = 0;
                return false;
            }

            int requiredStrikes = (hasOpenSkyAbove && precipMmPerHour > LightRainThresholdMmPerHour)
                ? CampfireStationData.RequiredSparkStrikes + 2
                : CampfireStationData.RequiredSparkStrikes;

            station.SparkStrikeCount++;
            if (station.SparkStrikeCount >= requiredStrikes)
            {
                station.SparkStrikeCount = CampfireStationData.RequiredSparkStrikes;
                station.IsLit = true;
                float mult = 1.0f + station.EfficiencyBonus01;
                if (station.RemainingFuelSec <= 0.01f)
                {
                    station.RemainingFuelSec = MathF.Min(
                        CampfireStationData.MaxFuelSeconds,
                        CampfireStationData.InitialFuelSeconds * mult);
                }
                station.TemperatureCelsius = (hasOpenSkyAbove && precipMmPerHour > LightRainThresholdMmPerHour)
                    ? 360.0f
                    : 480.0f;
                justIgnited = true;
            }
            return true;
        }

        /// <summary>
        /// Paket 4A (Faz 4A.4 / AK-8) — Kamp atesinin yagmur maruziyetini ilerletir.
        /// Ustu acikken (hasOpenSkyAbove == true) hafif yagmurda isitma yavaslar ve tutusturma zorlasir;
        /// siddetli yagmurda (&gt;= 6.0 mm/sa) ates soner (IsLit = false, yakit korunur) ve sicaklik hizla ortama duser.
        /// Ustu cati/magara altindaysa (hasOpenSkyAbove == false) saganakta bile %100 yanmaya devam eder.
        /// </summary>
        public static bool StepRainExposure(
            ref CampfireStationData station,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds,
            float ambientTempCelsius = 20.0f)
        {
            if (!hasOpenSkyAbove || precipMmPerHour <= LightRainThresholdMmPerHour || gameDeltaSeconds <= 0f)
            {
                return false;
            }

            float dt = MathF.Max(0f, gameDeltaSeconds);
            if (precipMmPerHour >= HeavyRainExtinguishThresholdMmPerHour)
            {
                bool wasLit = station.IsLit;
                station.IsLit = false;
                station.SparkStrikeCount = 0;

                float rapidDrop = MathF.Max(260.0f * dt, (station.TemperatureCelsius - ambientTempCelsius) * 0.85f);
                station.TemperatureCelsius = MathF.Max(ambientTempCelsius, station.TemperatureCelsius - rapidDrop);
                return wasLit;
            }

            // Hafif / orta yagmur (0.5 .. 6.0 mm/sa): isinmayi yavaslatir, sonukse kivilcim ilerlemesini nemlendirir
            if (!station.IsLit)
            {
                if (station.SparkStrikeCount > 0 && dt >= 0.5f)
                {
                    station.SparkStrikeCount = Math.Max(0, station.SparkStrikeCount - 1);
                }
            }
            else
            {
                float dampCooling = precipMmPerHour * 18.0f * dt;
                station.TemperatureCelsius = MathF.Max(
                    MathF.Max(ambientTempCelsius, 160.0f),
                    station.TemperatureCelsius - dampCooling);
            }

            return false;
        }

        public static bool StepRainExposure(
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds,
            ref CampfireStationData station,
            float ambientTempCelsius = 20.0f) =>
            StepRainExposure(ref station, precipMmPerHour, hasOpenSkyAbove, gameDeltaSeconds, ambientTempCelsius);

        /// <summary>
        /// Yanmakta olan kamp atesini sondurur (IsLit = false).
        /// Kalan yakit suresini (RemainingFuelSec) ve 3 yuvadaki pisirme ilerlemesini (ElapsedCookSec / Stage) korur.
        /// </summary>
        public static bool TryExtinguishStation(ref CampfireStationData station)
        {
            if (!station.IsLit) return false;
            station.IsLit = false;
            station.SparkStrikeCount = 0;
            station.TemperatureCelsius = 20.0f;
            return true;
        }

        public static bool TryExtinguishCampfire(ref CampfireStationData station) =>
            TryExtinguishStation(ref station);

        public static bool TryAddWoodFuel(ref CampfireStationData station, bool isSplitFirewood, out float addedSeconds)
        {
            addedSeconds = 0f;
            if (station.RemainingFuelSec >= CampfireStationData.MaxFuelSeconds - 1.0f)
            {
                return false;
            }

            float rawBonus = isSplitFirewood
                ? CampfireStationData.SplitFirewoodFuelBonusSeconds
                : CampfireStationData.StickFuelBonusSeconds;
            addedSeconds = rawBonus * (1.0f + station.EfficiencyBonus01);
            station.RemainingFuelSec = MathF.Min(CampfireStationData.MaxFuelSeconds, station.RemainingFuelSec + addedSeconds);
            return true;
        }

        public static bool TryPlaceRawMeat(
            ref CampfireStationData station,
            in PhysicalItemShapeDna rawMeatDna,
            out int placedSlotIndex)
        {
            return TryPlacePayload(
                ref station,
                CampfirePayloadKind.Meat,
                sourceItemKindByte: 37,
                subMaterialId: 0,
                in rawMeatDna,
                out placedSlotIndex);
        }

        public static bool TryPlacePayload(
            ref CampfireStationData station,
            CampfirePayloadKind payloadKind,
            byte sourceItemKindByte,
            byte subMaterialId,
            in PhysicalItemShapeDna sourceDna,
            out int placedSlotIndex)
        {
            placedSlotIndex = -1;
            if (!station.IsLit) return false;

            for (int i = 0; i < CampfireStationData.MaxCookingSlots; i++)
            {
                CampfireCookingSlotState slot = station.GetSlot(i);
                if (slot.Stage == CookingMeatStage.Empty)
                {
                    slot.Stage = CookingMeatStage.RawSizzling;
                    slot.ElapsedCookSec = 0f;
                    slot.PayloadKind = payloadKind;
                    slot.SourceItemKindByte = sourceItemKindByte;
                    slot.SubMaterialId = subMaterialId;
                    slot.ItemDna = payloadKind == CampfirePayloadKind.IronReheat
                        ? sourceDna.WithTemperature(120)
                        : sourceDna.WithStage(0).WithTemperature(60);
                    station.SetSlot(i, in slot);
                    placedSlotIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Kamp atesinden alinacak en uygun parcayi secer (Once hazir olanlar -> Yanmis -> Halen pisenler).
        /// </summary>
        public static bool TryCollectBestMeat(
            ref CampfireStationData station,
            out int collectedSlotIndex,
            out CookingMeatStage collectedStage,
            out PhysicalItemShapeDna collectedDna)
        {
            return TryCollectBestPayload(
                ref station,
                out collectedSlotIndex,
                out collectedStage,
                out _,
                out _,
                out _,
                out collectedDna);
        }

        public static bool TryCollectBestPayload(
            ref CampfireStationData station,
            out int collectedSlotIndex,
            out CookingMeatStage collectedStage,
            out CampfirePayloadKind collectedPayloadKind,
            out byte collectedSourceItemKindByte,
            out byte collectedSubMaterialId,
            out PhysicalItemShapeDna collectedDna)
        {
            collectedSlotIndex = -1;
            collectedStage = CookingMeatStage.Empty;
            collectedPayloadKind = CampfirePayloadKind.Meat;
            collectedSourceItemKindByte = 0;
            collectedSubMaterialId = 0;
            collectedDna = default;

            int bestIdx = -1;
            int bestPriority = -1;

            for (int i = 0; i < CampfireStationData.MaxCookingSlots; i++)
            {
                CookingMeatStage st = station.GetSlot(i).Stage;
                int prio = st switch
                {
                    CookingMeatStage.Cooked => 3,
                    CookingMeatStage.Burnt => 2,
                    CookingMeatStage.RawSizzling => 1,
                    _ => -1
                };
                if (prio > bestPriority)
                {
                    bestPriority = prio;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            CampfireCookingSlotState chosen = station.GetSlot(bestIdx);
            collectedSlotIndex = bestIdx;
            collectedStage = chosen.Stage;
            collectedPayloadKind = chosen.PayloadKind;
            collectedSourceItemKindByte = chosen.SourceItemKindByte;
            collectedSubMaterialId = chosen.SubMaterialId;

            if (chosen.PayloadKind == CampfirePayloadKind.Meat)
            {
                byte stageByte = chosen.Stage switch
                {
                    CookingMeatStage.Cooked => (byte)1,
                    CookingMeatStage.Burnt => (byte)2,
                    _ => (byte)0
                };
                byte tempByte = chosen.Stage == CookingMeatStage.Cooked ? (byte)95 : (byte)25;
                collectedDna = chosen.ItemDna.WithStage(stageByte).WithTemperature(tempByte);
            }
            else if (chosen.PayloadKind == CampfirePayloadKind.IronReheat && chosen.Stage == CookingMeatStage.Cooked)
            {
                collectedDna = chosen.ItemDna.WithTemperature(215);
            }
            else
            {
                collectedDna = chosen.ItemDna;
            }

            CampfireCookingSlotState empty = default;
            station.SetSlot(bestIdx, in empty);
            return true;
        }

        public static void StepStation(ref CampfireStationData station, float dt, out byte slotStageChangedMask)
        {
            slotStageChangedMask = 0;
            if (dt <= 0f) return;

            if (station.IsLit)
            {
                station.RemainingFuelSec -= dt;
                station.TemperatureCelsius = MathF.Min(650.0f, station.TemperatureCelsius + dt * 95.0f);

                if (station.RemainingFuelSec <= 0f)
                {
                    station.RemainingFuelSec = 0f;
                    station.IsLit = false;
                    station.SparkStrikeCount = 0;
                }
            }
            else
            {
                station.TemperatureCelsius = MathF.Max(20.0f, station.TemperatureCelsius - dt * 65.0f);
            }

            if (!station.IsLit && station.TemperatureCelsius < 180.0f) return;

            for (int i = 0; i < CampfireStationData.MaxCookingSlots; i++)
            {
                CampfireCookingSlotState slot = station.GetSlot(i);
                if (slot.Stage == CookingMeatStage.Empty) continue;

                CookingMeatStage prevStage = slot.Stage;
                slot.ElapsedCookSec += dt;

                switch (slot.PayloadKind)
                {
                    case CampfirePayloadKind.OreRoast:
                        if (slot.ElapsedCookSec >= CampfireCookingSlotState.OreRoastThresholdSec)
                        {
                            slot.Stage = CookingMeatStage.Cooked;
                            slot.ItemDna = slot.ItemDna.WithStage(1).WithTemperature(115);
                        }
                        break;

                    case CampfirePayloadKind.ClayFiring:
                        if (slot.ElapsedCookSec >= CampfireCookingSlotState.ClayFireThresholdSec)
                        {
                            slot.Stage = CookingMeatStage.Cooked;
                            slot.ItemDna = slot.ItemDna
                                .WithFlags((byte)(slot.ItemDna.Flags & ~PhysicalItemShapeDna.FlagUnfiredClay))
                                .WithStage(1)
                                .WithTemperature(70);
                        }
                        break;

                    case CampfirePayloadKind.IronReheat:
                        if (slot.ElapsedCookSec >= CampfireCookingSlotState.IronReheatThresholdSec)
                        {
                            slot.Stage = CookingMeatStage.Cooked;
                            slot.ItemDna = slot.ItemDna.WithTemperature(215);
                        }
                        break;

                    default:
                        if (slot.ElapsedCookSec > CampfireCookingSlotState.BurntThresholdSec)
                        {
                            slot.Stage = CookingMeatStage.Burnt;
                            slot.ItemDna = slot.ItemDna.WithStage(2).WithTemperature(210);
                        }
                        else if (slot.ElapsedCookSec >= CampfireCookingSlotState.CookedThresholdSec)
                        {
                            slot.Stage = CookingMeatStage.Cooked;
                            slot.ItemDna = slot.ItemDna.WithStage(1).WithTemperature(145);
                        }
                        break;
                }

                if (slot.Stage != prevStage)
                {
                    slotStageChangedMask |= (byte)(1 << i);
                }

                station.SetSlot(i, in slot);
            }
        }
    }

    /// <summary>
    /// Faz 2.3 (Asama 3.4 &amp; 3.5) &amp; Paket 4A (AK-8): Sahnedeki muhurlenmis Kamp Ateslerinin kivilcimla tutusturulmasi,
    /// yagmur maruziyeti, prosedurel 3B alev/kor/duman gorselleri, termal isinma alani ve 3B et pisirme etkilesim denetleyicisi.
    /// </summary>
    public sealed class Stage1CampfireAndCookingController
    {
        public static bool StepRainExposure(
            ref CampfireStationData station,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds,
            float ambientTempCelsius = 20.0f) =>
            CampfireSimulationLogic.StepRainExposure(
                ref station, precipMmPerHour, hasOpenSkyAbove, gameDeltaSeconds, ambientTempCelsius);

        public static bool StepRainExposure(
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds,
            ref CampfireStationData station,
            float ambientTempCelsius = 20.0f) =>
            CampfireSimulationLogic.StepRainExposure(
                ref station, precipMmPerHour, hasOpenSkyAbove, gameDeltaSeconds, ambientTempCelsius);

#if UNITY_5_3_OR_NEWER
        private sealed class SparkParticleBurst
        {
            public readonly Transform[] Shards = new Transform[6];
            public readonly Vector3[] Velocities = new Vector3[6];
            public float RemainingSec;
        }

        private sealed class CampfireRuntimeEntry
        {
            public CampfireStationData Data;
            public GameObject RootGo = null!;
            public GameObject FlameClusterGo = null!;
            public Light? WarmPointLight;
            public readonly GameObject[] MeatSlotGos = new GameObject[CampfireStationData.MaxCookingSlots];
            public readonly MeshFilter[] MeatSlotMfs = new MeshFilter[CampfireStationData.MaxCookingSlots];
            public readonly Transform[] RisingMotes = new Transform[4];
            public readonly MeshRenderer[] RisingMoteRenderers = new MeshRenderer[4];
            public SparkParticleBurst SparkBurst = new();
            public bool WasLitLastStep;
        }

        private static readonly Vector3[] CookingSlotLocalOffsets =
        {
            new(-0.14f, 0.21f, 0.08f),
            new(0.15f, 0.22f, 0.06f),
            new(0.01f, 0.23f, -0.14f)
        };

        private readonly List<CampfireRuntimeEntry> _campfires = new(8);
        private readonly List<FacetedVoxelTriangle> _meshBuildScratch = new(256);
        private Transform? _stationsContainer;
        private Mesh? _sharedFlameClusterMesh;
        private Mesh? _sharedSparkShardMesh;
        private Mesh? _sharedSteamMoteMesh;
        private Mesh? _sharedDarkSmokeMoteMesh;
        private AudioSource? _sfxSource;
        private AudioClip? _flintStrikeClip;
        private AudioClip? _igniteWhooshClip;
        private float _pendingStaminaDrain;
        private float _strikeCooldownSec;

        private GUIStyle? _promptBoxStyle;
        private GUIStyle? _promptTextStyle;

        public int ActiveCampfireCount => _campfires.Count;

        public float ConsumePendingStaminaDrain()
        {
            float v = _pendingStaminaDrain;
            _pendingStaminaDrain = 0f;
            return v;
        }

        public void RegisterUnlitCampfire(
            uint stationId,
            in MindValidationResult validation,
            Material? sharedVoxelMat)
        {
            EnsureSharedMeshesAndContainer();
            CampfireStationData data = CampfireSimulationLogic.CreateUnlitStation(
                stationId,
                validation.ClusterCenter,
                validation.ClusterBaseY,
                validation.BonusEfficiency01,
                validation.HasStoneRing);

            Vector3 worldCenter = new Vector3(data.Center.X, data.BaseY, data.Center.Z);
            GameObject root = new GameObject($"PhysicalCampfireStation_{stationId}");
            if (_stationsContainer != null) root.transform.SetParent(_stationsContainer, false);
            root.transform.position = worldCenter;

            // 1) Prosedurel 3B Alev & Kor Cekirdegi (Baslangicta sonuk -> kapali!)
            GameObject flameGo = new GameObject("Campfire_ProceduralFlameCore");
            flameGo.transform.SetParent(root.transform, false);
            flameGo.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            MeshFilter flameMf = flameGo.AddComponent<MeshFilter>();
            flameMf.sharedMesh = _sharedFlameClusterMesh;
            MeshRenderer flameMr = flameGo.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) flameMr.sharedMaterial = sharedVoxelMat;
            flameMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flameGo.SetActive(false);

            // 2) Sicak Kamp Atesi Nokta Isigi (Golgesiz, sifir VRAM maliyeti)
            GameObject lightGo = new GameObject("Campfire_WarmPointLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            Light ptLight = lightGo.AddComponent<Light>();
            ptLight.type = LightType.Point;
            ptLight.color = new Color(1.0f, 0.62f, 0.24f);
            ptLight.range = data.HasStoneRing ? 7.2f : 6.2f;
            ptLight.intensity = 0f;
            ptLight.shadows = LightShadows.None;
            ptLight.enabled = false;

            var entry = new CampfireRuntimeEntry
            {
                Data = data,
                RootGo = root,
                FlameClusterGo = flameGo,
                WarmPointLight = ptLight
            };

            // 3) 3B Et Pisirme Yuvalari (0..2)
            for (int s = 0; s < CampfireStationData.MaxCookingSlots; s++)
            {
                GameObject slotGo = new GameObject($"Campfire_MeatSlot_{s}");
                slotGo.transform.SetParent(root.transform, false);
                slotGo.transform.localPosition = CookingSlotLocalOffsets[s];
                slotGo.transform.localRotation = Quaternion.Euler(12f, s * 118f + 25f, -8f);
                MeshFilter smf = slotGo.AddComponent<MeshFilter>();
                MeshRenderer smr = slotGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) smr.sharedMaterial = sharedVoxelMat;
                slotGo.SetActive(false);
                entry.MeatSlotGos[s] = slotGo;
                entry.MeatSlotMfs[s] = smf;
            }

            // 4) Yukari suzulen duman / beyaz yemek buhari tanecikleri (4 adet havuzlanmis, 0 GC)
            for (int m = 0; m < entry.RisingMotes.Length; m++)
            {
                GameObject moteGo = new GameObject($"Campfire_SteamSmokeMote_{m}");
                moteGo.transform.SetParent(root.transform, false);
                MeshFilter mmf = moteGo.AddComponent<MeshFilter>();
                mmf.sharedMesh = _sharedSteamMoteMesh;
                MeshRenderer mmr = moteGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) mmr.sharedMaterial = sharedVoxelMat;
                mmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                moteGo.SetActive(false);
                entry.RisingMotes[m] = moteGo.transform;
                entry.RisingMoteRenderers[m] = mmr;
            }

            // 5) Cakmaktasi Kivilcim Parcaciklari (6 adet havuzlanmis, 0 GC)
            for (int k = 0; k < entry.SparkBurst.Shards.Length; k++)
            {
                GameObject spGo = new GameObject($"Campfire_SparkShard_{k}");
                spGo.transform.SetParent(root.transform, false);
                MeshFilter spMf = spGo.AddComponent<MeshFilter>();
                spMf.sharedMesh = _sharedSparkShardMesh;
                MeshRenderer spMr = spGo.AddComponent<MeshRenderer>();
                if (sharedVoxelMat != null) spMr.sharedMaterial = sharedVoxelMat;
                spMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spGo.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                spGo.SetActive(false);
                entry.SparkBurst.Shards[k] = spGo.transform;
            }

            _campfires.Add(entry);
        }

        public int FindAimedCampfireIndex(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach = 3.25f)
        {
            if (_campfires.Count == 0) return -1;
            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null ? cam.transform.forward : Vector3.forward;
            if (rayDir.sqrMagnitude < 0.001f) rayDir = Vector3.forward;
            rayDir.Normalize();

            int bestIdx = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _campfires.Count; i++)
            {
                CampfireRuntimeEntry e = _campfires[i];
                if (e.RootGo == null) continue;
                Vector3 center = e.RootGo.transform.position + new Vector3(0f, 0.14f, 0f);

                float distPlayer = Vector3.Distance(playerPos + new Vector3(0f, 0.85f, 0f), center);
                float distCam = Vector3.Distance(rayOrigin, center);
                if (distPlayer > maxReach && distCam > maxReach + 0.50f) continue;

                Vector3 toFire = center - rayOrigin;
                float along = Vector3.Dot(toFire, rayDir);
                if (along < -0.15f || along > maxReach + 0.65f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, center);
                if (perp > 0.68f) continue;

                float score = perp * 2.1f + along * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        public bool IsTargetCampfireLit(UnityEngine.Camera? cam, Vector3 playerPos, float maxReach = 3.25f)
        {
            int idx = FindAimedCampfireIndex(cam, playerPos, maxReach);
            return idx >= 0 && _campfires[idx].Data.IsLit;
        }

        public static bool IsWaterFilledContainerSlot(in VisualInventorySlot slot)
        {
            if (slot.IsEmpty) return false;
            if (Stage1VisualHotbarAndInventoryUi.IsFluidContainerKind(slot.Kind) && slot.SubId == 1)
            {
                return true;
            }
            return slot.Kind == InventoryItemKind.FoodCleanWater;
        }

        public bool TryExtinguishTargetedCampfire(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController? miningCtrl = null,
            WorldLooseBuildSystem? looseBuildSystem = null,
            float maxReach = 3.25f)
        {
            int idx = FindAimedCampfireIndex(cam, playerPos, maxReach);
            if (idx < 0) return false;
            return TryExtinguishCampfireAtIndex(idx, miningCtrl, looseBuildSystem);
        }

        private bool TryExtinguishCampfireAtIndex(
            int idx,
            Stage1MatterCrystalAndMiningController? miningCtrl,
            WorldLooseBuildSystem? looseBuildSystem)
        {
            if ((uint)idx >= (uint)_campfires.Count) return false;
            CampfireRuntimeEntry entry = _campfires[idx];
            if (!CampfireSimulationLogic.TryExtinguishStation(ref entry.Data))
            {
                return false;
            }

            if (entry.FlameClusterGo != null) entry.FlameClusterGo.SetActive(false);
            if (entry.WarmPointLight != null) entry.WarmPointLight.enabled = false;
            for (int m = 0; m < entry.RisingMotes.Length; m++)
            {
                if (entry.RisingMotes[m] != null && entry.RisingMotes[m].gameObject.activeSelf)
                {
                    entry.RisingMotes[m].gameObject.SetActive(false);
                }
            }

            if (looseBuildSystem != null)
            {
                entry.WasLitLastStep = false;
                looseBuildSystem.UpdateStationWoodThermalGlow(entry.Data.StationId, 30);
            }

            if (miningCtrl != null)
            {
                miningCtrl.LastFeedbackText = CampfireSimulationLogic.ExtinguishedToastMessage;
            }

            return true;
        }

        public bool TryHandleInteractKeyE(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            WorldLooseBuildSystem? looseBuildSystem = null)
        {
            int idx = FindAimedCampfireIndex(cam, playerPos, 3.20f);
            if (idx < 0) return false;

            CampfireRuntimeEntry entry = _campfires[idx];
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
            {
                if (entry.Data.IsLit)
                {
                    return TryExtinguishCampfireAtIndex(idx, miningCtrl, looseBuildSystem);
                }
                return false;
            }

            if (!CampfireSimulationLogic.TryCollectBestPayload(
                    ref entry.Data,
                    out int slotIdx,
                    out CookingMeatStage stage,
                    out CampfirePayloadKind payloadKind,
                    out byte srcKindByte,
                    out byte subMatId,
                    out PhysicalItemShapeDna outDna))
            {
                return false;
            }

            entry.MeatSlotGos[slotIdx].SetActive(false);

            InventoryItemKind targetKind;
            byte targetSubId = 0;
            string collectedMsg;

            switch (payloadKind)
            {
                case CampfirePayloadKind.OreRoast:
                    if (stage == CookingMeatStage.Cooked)
                    {
                        targetKind = InventoryItemKind.RoastedOreChunk;
                        targetSubId = subMatId;
                        collectedMsg =
                            "🔥 KAVRULMUŞ CEVHER ALINDI! Şimdi düz bir kayaya (veya Örse) [Sağ Tık] ile koyup Taş Çekiçle 3 kez kırarak Cevher Tozuna çevir!";
                    }
                    else
                    {
                        targetKind = InventoryItemKind.OreChunk;
                        targetSubId = subMatId;
                        collectedMsg = "🪨 Cevher henüz tam kavrulmadan ateşten geri alındı.";
                    }
                    break;

                case CampfirePayloadKind.ClayFiring:
                    targetKind = srcKindByte == (byte)InventoryItemKind.ClayCruciblePot
                        ? InventoryItemKind.ClayCruciblePot
                        : InventoryItemKind.ClayTuyerePipe;
                    targetSubId = 0;
                    collectedMsg = stage == CookingMeatStage.Cooked
                        ? $"🏺 PİŞMİŞ SERAMİK ALINDI: {(targetKind == InventoryItemKind.ClayCruciblePot ? "Kil Pota" : "Kil Hava Borusu (Tuyere)")} artık İzabe Fırınına takılmaya hazır!"
                        : "🏺 Çiğ kil parçası pişmeden ateşten geri alındı (hâlâ çiğ!).";
                    break;

                case CampfirePayloadKind.IronReheat:
                    targetKind = srcKindByte == (byte)InventoryItemKind.WroughtIronBar
                        ? InventoryItemKind.WroughtIronBar
                        : InventoryItemKind.IronBloomSponge;
                    targetSubId = 0;
                    collectedMsg = stage == CookingMeatStage.Cooked
                        ? "🔥 AKKOR DEMİR (~1020 °C) ALINDI! Soğumadan (~720 °C üstü) hemen Örs / Düz Kayaya koyup Çekiçle döv!"
                        : "⚒️ Demir parçası tam ısınmadan ateşten geri alındı.";
                    break;

                default:
                    targetKind = stage switch
                    {
                        CookingMeatStage.Cooked => InventoryItemKind.FoodCookedMeat,
                        CookingMeatStage.Burnt => InventoryItemKind.FoodBurntMeat,
                        _ => InventoryItemKind.FoodRawMeat
                    };
                    collectedMsg = stage switch
                    {
                        CookingMeatStage.Cooked =>
                            "🍖 Pişmiş Et ateşten alındı! (Eline alıp [Sol/Sağ Tık] ile ye: +35 Can, +40 Enerji, +0.8°C Vücut Isısı)",
                        CookingMeatStage.Burnt =>
                            "💀 Kömürleşmiş Yanık Et ateşten alındı! (26 saniyeyi geçtiği için yandı — besin değeri yok)",
                        _ =>
                            "🥩 Çiğ Et pişmeden ateşten geri alındı."
                    };
                    break;
            }

            if (!holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(targetKind, targetSubId, 1, miningCtrl, outDna))
            {
                miningCtrl.SpawnPhysicalWorldItem(
                    entry.RootGo.transform.position + new Vector3(0f, 0.38f, 0f),
                    targetKind, targetSubId, 1, outDna, null);
                miningCtrl.LastFeedbackText = "Çanta dolu olduğu için ateşten alınan parça yere bırakıldı ([E] ile al)!";
                return true;
            }

            holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            miningCtrl.LastFeedbackText = collectedMsg;
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
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat,
            out bool consumedMouseClick)
        {
            consumedMouseClick = false;
            if (_strikeCooldownSec > 0f)
            {
                _strikeCooldownSec = Mathf.Max(0f, _strikeCooldownSec - dt);
            }

            // 1) Oyuncu etkilesimleri (Cakmaktasi Kivilcimi / Dal-Odun Besleme / Cig Et Koyma)
            if (!uiBlocked && !miningCtrl.IsCrystalActive && !holsterCtrl.IsCarryingShoulderLog)
            {
                VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                int aimedIdx = FindAimedCampfireIndex(cam, playerTransform.position, 3.25f);

                if (aimedIdx >= 0)
                {
                    CampfireRuntimeEntry aimed = _campfires[aimedIdx];
                    bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                    // 0) Shift + [E] veya Su Dolu Kap (SubId == 1) ile Sag Tik -> Kamp Atesini Sondur (Yakit ve pisirme ilerlemesi korunur!)
                    if (shiftHeld &&
                        (Input.GetKeyDown(Stage1GameSettings.KeyInteract) || Input.GetKeyDown(KeyCode.E)) &&
                        aimed.Data.IsLit)
                    {
                        TryExtinguishCampfireAtIndex(aimedIdx, miningCtrl, looseBuildSystem);
                    }
                    else if (Input.GetMouseButtonDown(1) && IsWaterFilledContainerSlot(in activeSlot))
                    {
                        consumedMouseClick = true;
                        if (aimed.Data.IsLit)
                        {
                            TryExtinguishCampfireAtIndex(aimedIdx, miningCtrl, looseBuildSystem);
                        }
                        else
                        {
                            miningCtrl.LastFeedbackText = "Kamp ateşi zaten sönük.";
                        }
                    }
                    // A) Elinde Cakmaktasi varken Sol veya Sag Tik -> Kivilcim Cakma!
                    else if (activeSlot.Kind == InventoryItemKind.FlintStone &&
                        (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) &&
                        _strikeCooldownSec <= 0f)
                    {
                        consumedMouseClick = true;
                        _strikeCooldownSec = 0.24f;
                        HandleFlintStrikeOnCampfire(aimed, playerTransform, miningCtrl, holsterCtrl, looseBuildSystem);
                    }
                    // B) Elinde Kuru Dal veya Yarilmis Odun varken Sag Tik -> Yakit Ekleme!
                    else if ((activeSlot.Kind == InventoryItemKind.Stick || activeSlot.Kind == InventoryItemKind.SplitFirewood) &&
                             Input.GetMouseButtonDown(1))
                    {
                        consumedMouseClick = true;
                        HandleWoodFuelAddOnCampfire(aimed, in activeSlot, miningCtrl, holsterCtrl, combatBridge, looseBuildSystem, sharedVoxelMat);
                    }
                    // C) Elinde Cig Et, Ham Cevher, Cig Kil Seramik veya Soguk Demir varken Sag Tik -> Köz/Isi Yuvasi!
                    else if (Input.GetMouseButtonDown(1) &&
                             (activeSlot.Kind == InventoryItemKind.FoodRawMeat ||
                              (activeSlot.Kind == InventoryItemKind.OreChunk && (activeSlot.SubId == MaterialIdCatalog.CopperOre || activeSlot.SubId == MaterialIdCatalog.IronOre)) ||
                              ((activeSlot.Kind == InventoryItemKind.ClayTuyerePipe || activeSlot.Kind == InventoryItemKind.ClayCruciblePot) &&
                               (activeSlot.GetEffectiveShapeDna().Flags & PhysicalItemShapeDna.FlagUnfiredClay) != 0) ||
                              activeSlot.Kind == InventoryItemKind.IronBloomSponge ||
                              activeSlot.Kind == InventoryItemKind.WroughtIronBar))
                    {
                        consumedMouseClick = true;
                        HandlePlacePayloadOnCampfire(aimed, in activeSlot, miningCtrl, holsterCtrl, combatBridge);
                    }
                }
                else if (activeSlot.Kind == InventoryItemKind.FoodRawMeat &&
                         (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
                {
                    consumedMouseClick = true;
                    miningCtrl.LastFeedbackText =
                        "🥩 Çiğ Et çiğ yenmez! Yanan bir Kamp Ateşine bakıp [Sağ Tık] ile közlerin üzerine koyarak pişirin.";
                }
                else if (activeSlot.Kind == InventoryItemKind.FoodBurntMeat &&
                         (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
                {
                    consumedMouseClick = true;
                    miningCtrl.LastFeedbackText =
                        "💀 Bu et kömürleşmiş! Yenemez (0 besin değeri) — [Q] ile yere atabilirsiniz.";
                }
            }

            // 2) Tum kayitli Kamp Ateslerinin simülasyon ve 3B gorsel adimi
            float timeNow = Time.time;
            for (int i = 0; i < _campfires.Count; i++)
            {
                CampfireRuntimeEntry e = _campfires[i];
                if (e.RootGo == null) continue;

                CampfireSimulationLogic.StepStation(ref e.Data, dt, out byte stageChangedMask);

                if (e.Data.IsLit != e.WasLitLastStep)
                {
                    e.WasLitLastStep = e.Data.IsLit;
                    e.FlameClusterGo.SetActive(e.Data.IsLit);
                    if (e.WarmPointLight != null) e.WarmPointLight.enabled = e.Data.IsLit;
                    looseBuildSystem.UpdateStationWoodThermalGlow(e.Data.StationId, e.Data.IsLit ? (byte)195 : (byte)30);
                }

                if (stageChangedMask != 0)
                {
                    for (int s = 0; s < CampfireStationData.MaxCookingSlots; s++)
                    {
                        if ((stageChangedMask & (1 << s)) != 0)
                        {
                            RefreshCookingSlotMesh(e, s);
                        }
                    }
                }

                StepCampfireVisualEffects(e, dt, timeNow);
            }
        }

        public float EvaluateThermalBonusAndDrying(
            Vector3 playerChestPos,
            float dt,
            bool canDryWetness,
            ref float playerWetness01)
        {
            float bestHeatBonus = 0f;
            for (int i = 0; i < _campfires.Count; i++)
            {
                CampfireRuntimeEntry e = _campfires[i];
                if (!e.Data.IsLit || e.RootGo == null) continue;

                float dist = Vector3.Distance(playerChestPos, e.RootGo.transform.position + new Vector3(0f, 0.35f, 0f));
                const float maxWarmRadius = 5.8f;
                if (dist >= maxWarmRadius) continue;

                float peak = e.Data.HasStoneRing ? 21.5f : 18.0f;
                float bonus = (1.0f - dist / maxWarmRadius) * peak;
                if (bonus > bestHeatBonus) bestHeatBonus = bonus;

                if (canDryWetness && dist <= 3.8f && playerWetness01 > 0f)
                {
                    float dryRate = e.Data.HasStoneRing ? 0.18f : 0.15f;
                    playerWetness01 = Mathf.Max(0f, playerWetness01 - dryRate * dt);
                }
            }

            return bestHeatBonus;
        }

        public void ClearAllStations()
        {
            for (int i = 0; i < _campfires.Count; i++)
            {
                if (_campfires[i].RootGo != null)
                {
                    UnityEngine.Object.Destroy(_campfires[i].RootGo);
                }
            }
            _campfires.Clear();
        }

        public void DrawCrosshairPromptGui(
            UnityEngine.Camera? cam,
            Vector3 playerPos,
            bool showHud,
            bool uiBlocked,
            Stage1HolsterAndBackpackController holsterCtrl,
            WorldLooseBuildSystem looseBuildSystem)
        {
            if (!showHud || uiBlocked) return;
            EnsureGuiStyles();

            int fireIdx = FindAimedCampfireIndex(cam, playerPos, 3.25f);
            if (fireIdx >= 0)
            {
                CampfireRuntimeEntry e = _campfires[fireIdx];
                string line1;
                string line2 = string.Empty;

                if (!e.Data.IsLit)
                {
                    line1 = $"🔥 Sönük Kamp Ateşi ({e.Data.SparkStrikeCount}/{CampfireStationData.RequiredSparkStrikes} Kıvılcım)";
                    line2 = "Çakmaktaşı ile [Sol/Sağ Tık] Kıvılcım Çak  |  [Sağ Tık] Dal/Odun Ekle";
                }
                else
                {
                    int fuelSec = Mathf.CeilToInt(e.Data.RemainingFuelSec);
                    string ringTxt = e.Data.HasStoneRing ? " | Taş Çember +%15" : string.Empty;
                    line1 = $"🔥 Kamp Ateşi Yanıyor ({fuelSec} sn yakıt{ringTxt})";

                    int readyCount = 0, processingCount = 0, burntCount = 0;
                    CampfirePayloadKind sampleKind = CampfirePayloadKind.Meat;
                    float minRemainToReady = 99f;
                    float minRemainToBurn = 99f;
                    for (int s = 0; s < CampfireStationData.MaxCookingSlots; s++)
                    {
                        CampfireCookingSlotState sl = e.Data.GetSlot(s);
                        if (sl.Stage == CookingMeatStage.Cooked)
                        {
                            readyCount++;
                            sampleKind = sl.PayloadKind;
                            if (sl.PayloadKind == CampfirePayloadKind.Meat)
                            {
                                float remBurn = CampfireCookingSlotState.BurntThresholdSec - sl.ElapsedCookSec;
                                if (remBurn < minRemainToBurn) minRemainToBurn = remBurn;
                            }
                        }
                        else if (sl.Stage == CookingMeatStage.RawSizzling)
                        {
                            processingCount++;
                            sampleKind = sl.PayloadKind;
                            float threshold = sl.PayloadKind switch
                            {
                                CampfirePayloadKind.OreRoast => CampfireCookingSlotState.OreRoastThresholdSec,
                                CampfirePayloadKind.ClayFiring => CampfireCookingSlotState.ClayFireThresholdSec,
                                CampfirePayloadKind.IronReheat => CampfireCookingSlotState.IronReheatThresholdSec,
                                _ => CampfireCookingSlotState.CookedThresholdSec
                            };
                            float remReady = threshold - sl.ElapsedCookSec;
                            if (remReady < minRemainToReady) minRemainToReady = remReady;
                        }
                        else if (sl.Stage == CookingMeatStage.Burnt)
                        {
                            burntCount++;
                        }
                    }

                    if (readyCount > 0)
                    {
                        line2 = sampleKind switch
                        {
                            CampfirePayloadKind.OreRoast =>
                                $"🔥 {readyCount}x KAVRULMUŞ CEVHER HAZIR! — [E] Al (Düz kayada Çekiçle kır!)",
                            CampfirePayloadKind.ClayFiring =>
                                $"🏺 {readyCount}x PİŞMİŞ SERAMİK HAZIR! — [E] Al (İzabe Fırınına takılabilir!)",
                            CampfirePayloadKind.IronReheat =>
                                $"🔥 {readyCount}x AKKOR DEMİR (~1020 °C) HAZIR! — [E] Al (Hemen kayada/örste döv!)",
                            _ =>
                                $"🍖 {readyCount}x PİŞMİŞ ET HAZIR! — [E] Al (Yanmasına {Mathf.Max(1, Mathf.CeilToInt(minRemainToBurn))} sn!)"
                        };
                    }
                    else if (processingCount > 0)
                    {
                        string procLabel = sampleKind switch
                        {
                            CampfirePayloadKind.OreRoast => "Cevher Kavruluyor",
                            CampfirePayloadKind.ClayFiring => "Kil Seramik Pişiyor",
                            CampfirePayloadKind.IronReheat => "Demir Akkor Isınıyor",
                            _ => "Et Cızırdıyor"
                        };
                        line2 = $"🔥 {processingCount}x {procLabel} ({Mathf.Max(1, Mathf.CeilToInt(minRemainToReady))} sn kaldı)  |  [E] Geri Al";
                    }
                    else if (burntCount > 0)
                    {
                        line2 = $"💀 {burntCount}x Kömürleşmiş Et — [E] Al";
                    }
                    else
                    {
                        line2 = "[Sağ Tık] Çiğ Et / Ham Cevher / Çiğ Kil / Soğuk Demir Koy  |  Dal (+60sn) / Odun (+140sn)";
                    }

                    line2 += "  |  [Shift+E] Ateşi Söndür";
                }

                DrawPromptBanner(line1, line2);
                return;
            }

            int looseIdx = looseBuildSystem.FindBestAimedUnboundPartIndex(cam, playerPos, 2.95f);
            if (looseIdx >= 0 && looseIdx < looseBuildSystem.Parts.Count)
            {
                var p = looseBuildSystem.Parts[looseIdx];
                var tempSlot = new VisualInventorySlot { Kind = p.ItemKind, SubId = p.SubId, Count = 1, ShapeDna = p.ShapeDna };
                string partName = Stage1VisualHotbarAndInventoryUi.GetSlotDisplayName(in tempSlot);
                DrawPromptBanner($"🤲 [E] Geri Al: {partName}", "[T] Zihin İnşa Menüsü ile kümeyi doğrula");
            }
        }

        private void HandleFlintStrikeOnCampfire(
            CampfireRuntimeEntry entry,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            WorldLooseBuildSystem looseBuildSystem)
        {
            if (entry.Data.IsLit)
            {
                miningCtrl.LastFeedbackText = "Kamp ateşi zaten gürül gürül yanıyor! [Sağ Tık] ile Çiğ Et pişirebilir veya Dal ekleyebilirsiniz.";
                return;
            }

            if (!holsterCtrl.IsCreativeMode && CountTotalStrikerStones(holsterCtrl) < 2)
            {
                miningCtrl.LastFeedbackText =
                    "🪨 KIVILCIM ÇAKILAMADI: Çakmaktaşını birbirine vurmak için çantanda/kemerinde ikinci bir Çakmaktaşı veya Kaba Taş Parçası daha olmalı!";
                return;
            }

            _pendingStaminaDrain += CampfireStationData.SparkStaminaCost;
            TriggerSparkBurst(entry);
            PlayProceduralSfx(playerTransform, igniteWhoosh: false);

            CampfireSimulationLogic.ApplyFlintSparkStrike(ref entry.Data, out bool justIgnited);
            if (justIgnited)
            {
                entry.WasLitLastStep = true;
                entry.FlameClusterGo.SetActive(true);
                if (entry.WarmPointLight != null) entry.WarmPointLight.enabled = true;
                looseBuildSystem.UpdateStationWoodThermalGlow(entry.Data.StationId, 195);
                PlayProceduralSfx(playerTransform, igniteWhoosh: true);

                miningCtrl.LastFeedbackText =
                    $"🔥 KAV TUTUŞTU! Kamp Ateşi Yandı ({Mathf.RoundToInt(entry.Data.RemainingFuelSec)} sn yakıt | [Sağ Tık] Çiğ Et koyarak pişir veya Dal ekle!)";
            }
            else
            {
                miningCtrl.LastFeedbackText =
                    $"✨ Çakmaktaşı Kıvılcımı Çakıldı ({entry.Data.SparkStrikeCount}/{CampfireStationData.RequiredSparkStrikes}, -1 EP) — Kuru yaprak tütüyor, tekrar çak!";
            }
        }

        private void HandleWoodFuelAddOnCampfire(
            CampfireRuntimeEntry entry,
            in VisualInventorySlot activeSlot,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge,
            WorldLooseBuildSystem looseBuildSystem,
            Material? sharedVoxelMat)
        {
            bool isFirewood = activeSlot.Kind == InventoryItemKind.SplitFirewood;
            if (!CampfireSimulationLogic.TryAddWoodFuel(ref entry.Data, isFirewood, out float addedSec))
            {
                miningCtrl.LastFeedbackText = "Kamp ateşinin yakıt kapasitesi şu an tamamen dolu (600 sn)!";
                return;
            }

            PhysicalItemShapeDna woodDna = activeSlot.GetEffectiveShapeDna();
            byte subId = activeSlot.SubId;
            InventoryItemKind woodKind = activeSlot.Kind;

            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1)) return;
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            // Gorsel gercekcilik: Atese atilan dal/odun, istasyona bagli (BoundStationId) gercek 3B parca olarak atesin ustune eklenir!
            int currentBoundWood = looseBuildSystem.CountBoundWoodParts(entry.Data.StationId);
            if (currentBoundWood < 9 && entry.RootGo != null)
            {
                float angleRad = (currentBoundWood * 137.5f) * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
                Vector3 inward = -outward;
                Vector3 stickDir = (inward * Mathf.Cos(40f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized;
                Quaternion rot = Quaternion.LookRotation(stickDir, Vector3.up);
                Vector3 pos = entry.RootGo.transform.position + outward * 0.14f + new Vector3(0f, 0.15f, 0f);

                PhysicalItemShapeDna placedDna = woodKind == InventoryItemKind.Stick
                    ? woodDna.WithFlags((byte)(woodDna.Flags | PhysicalItemShapeDna.FlagBrokenStick))
                    : woodDna;
                if (entry.Data.IsLit)
                {
                    placedDna = placedDna.WithTemperature(195);
                }

                looseBuildSystem.SpawnBoundOrLoosePartDirect(
                    woodKind,
                    isFirewood ? LooseBuildPartKind.SplitFirewood : LooseBuildPartKind.Stick,
                    subId,
                    in placedDna,
                    pos,
                    rot,
                    entry.Data.StationId,
                    sharedVoxelMat);
            }

            string woodName = isFirewood ? "Yarılmış Odun" : "Kuru Dal";
            miningCtrl.LastFeedbackText =
                $"🪵 Ateşe +1 {woodName} eklendi (+{Mathf.RoundToInt(addedSec)} sn yakıt | Toplam: {Mathf.RoundToInt(entry.Data.RemainingFuelSec)} sn)";
        }

        private static void HandlePlacePayloadOnCampfire(
            CampfireRuntimeEntry entry,
            in VisualInventorySlot activeSlot,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1DirectionalCombatBridge? combatBridge)
        {
            if (!entry.Data.IsLit)
            {
                miningCtrl.LastFeedbackText =
                    "🔥 Kamp ateşi henüz sönük! Önce Çakmaktaşı ile 3 kez kıvılcım çakarak ateşi yakmalısın.";
                return;
            }

            CampfirePayloadKind payloadKind = activeSlot.Kind switch
            {
                InventoryItemKind.OreChunk => CampfirePayloadKind.OreRoast,
                InventoryItemKind.ClayTuyerePipe or InventoryItemKind.ClayCruciblePot => CampfirePayloadKind.ClayFiring,
                InventoryItemKind.IronBloomSponge or InventoryItemKind.WroughtIronBar => CampfirePayloadKind.IronReheat,
                _ => CampfirePayloadKind.Meat
            };

            PhysicalItemShapeDna srcDna = activeSlot.GetEffectiveShapeDna();
            byte srcKindByte = (byte)activeSlot.Kind;
            byte subId = activeSlot.SubId;
            if (!CampfireSimulationLogic.TryPlacePayload(
                    ref entry.Data, payloadKind, srcKindByte, subId, in srcDna, out int slotIdx))
            {
                miningCtrl.LastFeedbackText =
                    "🔥 Kamp ateşinin üzerindeki 3 yuva da dolu! Önce [E] ile hazır olanları al.";
                return;
            }

            if (!holsterCtrl.IsCreativeMode)
            {
                if (!holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1))
                {
                    CampfireCookingSlotState empty = default;
                    entry.Data.SetSlot(slotIdx, in empty);
                    return;
                }
                holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl, combatBridge);
            }

            RefreshCookingSlotMesh(entry, slotIdx);
            miningCtrl.LastFeedbackText = payloadKind switch
            {
                CampfirePayloadKind.OreRoast =>
                    "🪨 Ham Cevher közlere konuldu (~10 sn kavrulacak, ardından Çekiçle kırılabilecek!)",
                CampfirePayloadKind.ClayFiring =>
                    "🏺 Çiğ Kil Seramik közlere konuldu (~12 sn sonra dayanıklı pişmiş seramiğe dönüşecek!)",
                CampfirePayloadKind.IronReheat =>
                    "⚒️ Demir kütlesi közlerde yeniden akkor (~1020 °C) hale ısıtılıyor (~8 sn)!",
                _ =>
                    "🥩 Çiğ Et közlerin üzerine yerleştirildi! (~12 sn sonra altın-kahverengi pişecek; 26 sn'yi geçerse kömürleşir!)"
            };
        }

        private static void RefreshCookingSlotMesh(CampfireRuntimeEntry entry, int slotIdx)
        {
            CampfireCookingSlotState slot = entry.Data.GetSlot(slotIdx);
            GameObject go = entry.MeatSlotGos[slotIdx];
            MeshFilter mf = entry.MeatSlotMfs[slotIdx];

            if (slot.Stage == CookingMeatStage.Empty)
            {
                go.SetActive(false);
                return;
            }

            PhysicalItemArchetype arch;
            byte matSubId = slot.SubMaterialId;

            switch (slot.PayloadKind)
            {
                case CampfirePayloadKind.OreRoast:
                    arch = slot.Stage == CookingMeatStage.Cooked
                        ? PhysicalItemArchetype.RoastedCrackedOre
                        : PhysicalItemArchetype.OreBearingRock;
                    break;
                case CampfirePayloadKind.ClayFiring:
                    arch = slot.SourceItemKindByte == (byte)InventoryItemKind.ClayCruciblePot
                        ? PhysicalItemArchetype.ClayCruciblePot
                        : PhysicalItemArchetype.ClayTuyerePipe;
                    matSubId = 0;
                    break;
                case CampfirePayloadKind.IronReheat:
                    arch = slot.SourceItemKindByte == (byte)InventoryItemKind.WroughtIronBar
                        ? PhysicalItemArchetype.WroughtIronBar
                        : PhysicalItemArchetype.IronBloomSponge;
                    matSubId = 0;
                    break;
                default:
                    arch = slot.Stage switch
                    {
                        CookingMeatStage.Cooked => PhysicalItemArchetype.CookedMeatCut,
                        CookingMeatStage.Burnt => PhysicalItemArchetype.BurntCharredMeat,
                        _ => PhysicalItemArchetype.RawMeatCut
                    };
                    matSubId = 0;
                    break;
            }

            Mesh m = PhysicalItemMeshFactory.GetOrCreateMesh(arch, matSubId, in slot.ItemDna, out Vector3 natScale);
            mf.sharedMesh = m;
            go.transform.localScale = Vector3.Scale(natScale, new Vector3(1.42f, 1.42f, 1.42f));
            go.SetActive(true);
        }

        private void StepCampfireVisualEffects(CampfireRuntimeEntry e, float dt, float timeNow)
        {
            // 1) Kivilcim patlamasi balistigi
            if (e.SparkBurst.RemainingSec > 0f)
            {
                e.SparkBurst.RemainingSec -= dt;
                bool active = e.SparkBurst.RemainingSec > 0f;
                for (int k = 0; k < e.SparkBurst.Shards.Length; k++)
                {
                    Transform sh = e.SparkBurst.Shards[k];
                    if (!active)
                    {
                        if (sh.gameObject.activeSelf) sh.gameObject.SetActive(false);
                        continue;
                    }
                    e.SparkBurst.Velocities[k] += new Vector3(0f, -6.8f * dt, 0f);
                    sh.localPosition += e.SparkBurst.Velocities[k] * dt;
                    sh.Rotate(380f * dt, 240f * dt, 0f, Space.Self);
                }
            }

            // 2) Alev titresimi ve nokta isigi
            if (e.Data.IsLit)
            {
                float flickerA = Mathf.Sin(timeNow * 13.7f + e.Data.StationId * 1.7f);
                float flickerB = Mathf.Sin(timeNow * 21.3f + e.Data.StationId * 3.1f);
                float fuelRatio = Mathf.Clamp01(e.Data.RemainingFuelSec / 45.0f);
                float hScale = (0.85f + 0.18f * flickerA + 0.08f * flickerB) * Mathf.Lerp(0.45f, 1.05f, fuelRatio);
                float wScale = 0.92f + 0.08f * flickerB;
                e.FlameClusterGo.transform.localScale = new Vector3(wScale, hScale, wScale);
                e.FlameClusterGo.transform.localRotation = Quaternion.Euler(0f, (timeNow * 28f + e.Data.StationId * 47f) % 360f, 0f);

                if (e.WarmPointLight != null)
                {
                    e.WarmPointLight.intensity = (1.55f + 0.28f * flickerA + 0.14f * flickerB) * Mathf.Lerp(0.45f, 1.0f, fuelRatio);
                }

                // Et cizirdama mikro titresimi
                for (int s = 0; s < CampfireStationData.MaxCookingSlots; s++)
                {
                    CampfireCookingSlotState sl = e.Data.GetSlot(s);
                    if (sl.Stage == CookingMeatStage.Empty) continue;
                    float sizzle = sl.Stage == CookingMeatStage.RawSizzling
                        ? Mathf.Sin(timeNow * 38f + s * 2.1f) * 0.0045f
                        : 0f;
                    e.MeatSlotGos[s].transform.localPosition = CookingSlotLocalOffsets[s] + new Vector3(0f, sizzle, 0f);
                }

                // Yukari suzulen duman / buhar tanecikleri
                bool hasBurnt = e.Data.Slot0.Stage == CookingMeatStage.Burnt ||
                                e.Data.Slot1.Stage == CookingMeatStage.Burnt ||
                                e.Data.Slot2.Stage == CookingMeatStage.Burnt;
                Mesh? targetMoteMesh = hasBurnt ? _sharedDarkSmokeMoteMesh : _sharedSteamMoteMesh;

                for (int m = 0; m < e.RisingMotes.Length; m++)
                {
                    Transform mt = e.RisingMotes[m];
                    if (!mt.gameObject.activeSelf) mt.gameObject.SetActive(true);
                    MeshFilter? mmf = mt.GetComponent<MeshFilter>();
                    if (mmf != null && targetMoteMesh != null && mmf.sharedMesh != targetMoteMesh)
                    {
                        mmf.sharedMesh = targetMoteMesh;
                    }

                    float cycle = Mathf.Repeat(timeNow * 0.62f + m * 0.25f, 1.0f);
                    float swayX = Mathf.Sin(timeNow * 2.4f + m * 1.6f) * (0.06f + cycle * 0.14f);
                    float swayZ = Mathf.Cos(timeNow * 2.1f + m * 2.3f) * (0.06f + cycle * 0.14f);
                    mt.localPosition = new Vector3(swayX, 0.28f + cycle * 1.15f, swayZ);
                    float sc = Mathf.Sin(cycle * Mathf.PI) * (hasBurnt ? 0.95f : 0.72f);
                    mt.localScale = new Vector3(sc, sc, sc);
                }
            }
            else
            {
                for (int m = 0; m < e.RisingMotes.Length; m++)
                {
                    if (e.RisingMotes[m].gameObject.activeSelf)
                    {
                        e.RisingMotes[m].gameObject.SetActive(false);
                    }
                }
            }
        }

        private static void TriggerSparkBurst(CampfireRuntimeEntry entry)
        {
            entry.SparkBurst.RemainingSec = 0.42f;
            for (int k = 0; k < entry.SparkBurst.Shards.Length; k++)
            {
                float ang = (k / (float)entry.SparkBurst.Shards.Length) * Mathf.PI * 2f + Time.time * 3.1f;
                float speedH = 0.85f + (k % 3) * 0.35f;
                Transform sh = entry.SparkBurst.Shards[k];
                sh.localPosition = new Vector3(0f, 0.14f, 0f);
                entry.SparkBurst.Velocities[k] = new Vector3(Mathf.Cos(ang) * speedH, 1.65f + (k % 2) * 0.55f, Mathf.Sin(ang) * speedH);
                sh.gameObject.SetActive(true);
            }
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

        private void EnsureSharedMeshesAndContainer()
        {
            if (_stationsContainer == null)
            {
                GameObject existing = GameObject.Find("06_Faz23_WorldLooseBuildRoot");
                if (existing == null) existing = new GameObject("06_Faz23_WorldLooseBuildRoot");
                _stationsContainer = existing.transform;
            }

            if (_sharedFlameClusterMesh == null)
            {
                _meshBuildScratch.Clear();
                // Merkez kor yatagi + 4 titrek alev dili (StratocraftUberShader yuksek sicaklik turuncu-altin kor parlakligi!)
                EmitConeTongue(new SysVec3(0f, 0.02f, 0f), new SysVec3(0.01f, 0.34f, 0.01f), 0.11f, MaterialIdCatalog.MagmaBlock, 250, _meshBuildScratch);
                EmitConeTongue(new SysVec3(-0.07f, 0.03f, 0.04f), new SysVec3(-0.02f, 0.25f, 0.02f), 0.075f, MaterialIdCatalog.GoldOre, 235, _meshBuildScratch);
                EmitConeTongue(new SysVec3(0.07f, 0.03f, 0.03f), new SysVec3(0.02f, 0.27f, -0.01f), 0.075f, MaterialIdCatalog.MagmaBlock, 240, _meshBuildScratch);
                EmitConeTongue(new SysVec3(0.0f, 0.03f, -0.08f), new SysVec3(0.01f, 0.24f, -0.02f), 0.070f, MaterialIdCatalog.GoldOre, 230, _meshBuildScratch);
                _sharedFlameClusterMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_CampfireFlameCluster");
            }

            if (_sharedSparkShardMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.025f, 0f), new SysVec3(0f, 0.035f, 0f), 0.022f, MaterialIdCatalog.GoldOre, 255, _meshBuildScratch);
                _sharedSparkShardMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_FlintSparkShard");
            }

            if (_sharedSteamMoteMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.04f, 0f), new SysVec3(0f, 0.05f, 0f), 0.048f, MaterialIdCatalog.Marble, 0, _meshBuildScratch);
                _sharedSteamMoteMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_CampfireSteamMote");
            }

            if (_sharedDarkSmokeMoteMesh == null)
            {
                _meshBuildScratch.Clear();
                EmitConeTongue(new SysVec3(0f, -0.05f, 0f), new SysVec3(0f, 0.06f, 0f), 0.058f, MaterialIdCatalog.Basalt, 40, _meshBuildScratch);
                _sharedDarkSmokeMoteMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshBuildScratch, "Mesh_CampfireDarkSmokeMote");
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

        private void PlayProceduralSfx(Transform listenerRef, bool igniteWhoosh)
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

            if (!igniteWhoosh)
            {
                if (_flintStrikeClip == null)
                {
                    const int sampleRate = 22050;
                    int samples = sampleRate / 12; // ~83ms keskin cakmaktasi carpma sesi
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
                    _flintStrikeClip = AudioClip.Create("Sfx_FlintSparkClack", samples, 1, sampleRate, false);
                    _flintStrikeClip.SetData(pcm, 0);
                }
                _sfxSource.PlayOneShot(_flintStrikeClip, 0.65f);
            }
            else
            {
                if (_igniteWhooshClip == null)
                {
                    const int sampleRate = 22050;
                    int samples = sampleRate / 4; // ~250ms kav tutusma harlamasi
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
                    _igniteWhooshClip = AudioClip.Create("Sfx_CampfireIgniteWhoosh", samples, 1, sampleRate, false);
                    _igniteWhooshClip.SetData(pcm, 0);
                }
                _sfxSource.PlayOneShot(_igniteWhooshClip, 0.75f);
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
            float w = 640f;
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
#endif
    }
}
