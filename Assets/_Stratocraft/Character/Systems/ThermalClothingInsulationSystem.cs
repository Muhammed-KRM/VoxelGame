#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;

namespace Stratocraft.Character.Systems
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ClothingSlotWetnessState
    {
        public float Wetness01;         // [0.0 .. 1.0] yağmur veya suya batma ıslaklığı
        public float FrozenStiffness01; // [0.0 .. 1.0] dondurucu rüzgârda ıslak giysinin buz tutması
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PlayerThermalGearSummary
    {
        public float TotalEffectiveClo;          // Toplam etkin yalıtım (clo; 1 clo = 0.155 m²K/W)
        public float AggregateWindBlock01;       // [0.0 .. 0.96] rüzgâr soğuğunu kesme oranı
        public float ThermalBridgePenaltyWatts;  // Astarsız çıplak metal zırhın soğukta iletim ısı kaybı (W)
        public float AverageWetness01;           // [0.0 .. 1.0]
        public float MovementStiffnessPenalty01; // [0.0 .. 0.15] donmuş ıslak giysi hareket cezası
        public float ConductiveMetalLoad01;      // [0.0 .. 1.0] yıldırım hedef ağırlığı girdisi (İP-6)
    }

    /// <summary>
    /// İP-8: UniqueEquipmentInstance (128-Byte) yapısını tek bayt büyütmeden katmanlı giysi (clo),
    /// JAG/TI Wind Chill, ıslanma/donma/kuruma ve metal termal köprü fiziğini hesaplayan sıfır-GC sistem.
    /// </summary>
    public static class ThermalClothingInsulationSystem
    {
        public const float OneCloInM2KPerWatt = 0.155f;
        public const float MaxTotalEffectiveClo = 4.20f;
        public const float MaxAggregateWindBlock01 = 0.96f;
        public const float MaxMovementStiffnessPenalty01 = 0.15f;

        // Kanonik malzeme kuru baz yalıtım (clo) sabitleri (DOK1 §3.8.2, DOK3 §3.4.3, DOK4 §8.2)
        public const float ClothDryClo = 0.45f;
        public const float LeatherDryClo = 0.80f;
        public const float HeavyFurDryClo = 1.45f;
        public const float BareMetalDryClo = 0.05f;

        // Metal termal köprü eşiği ve katsayısı (< 5.0°C altında (5.0 - T_a) * 3.8 W)
        public const float MetalThermalBridgeThresholdCelsius = 5.0f;
        public const float MetalThermalBridgeWattsPerDegree = 3.8f;

        /// <summary>
        /// Osczevski &amp; Bluestein (JAG/TI) Kuzey Amerika / WMO Rüzgâr Soğuğu İndeksi (°C).
        /// T_a &lt;= 10.0°C ve V &gt; 1.34 m/s (4.8 km/h) olduğunda devreye girer; aksi halde T_a döner.
        /// Giysinin AggregateWindBlock01 katmanı cilde ulaşan etkin rüzgâr hızını sönümler!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeJagTiWindChillCelsius(
            float airTempCelsius,
            float localWindSpeedMps,
            float clothingWindBlock01 = 0.0f)
        {
            if (!float.IsFinite(airTempCelsius))
            {
                return 20.0f;
            }

            float safeWindMps = float.IsFinite(localWindSpeedMps) ? MathF.Max(0.0f, localWindSpeedMps) : 0.0f;
            float safeWindBlock = float.IsFinite(clothingWindBlock01) ? Math.Clamp(clothingWindBlock01, 0.0f, 0.95f) : 0.0f;
            float effectiveWindMps = safeWindMps * (1.0f - safeWindBlock);
            float vKmh = effectiveWindMps * 3.6f;
            if (airTempCelsius > 10.0f || vKmh <= 4.8f)
            {
                return airTempCelsius;
            }

            float vPow = MathF.Pow(vKmh, 0.16f);
            float twc = 13.12f + (0.6215f * airTempCelsius) - (11.37f * vPow) + (0.3965f * airTempCelsius * vPow);
            return MathF.Min(airTempCelsius, twc);
        }

        /// <summary>
        /// ISO 9920 clo Giysi Yalıtımı (negatif clo üretmez) ve Ayrı Metal Termal Köprü İletim Kaybı (Watt).
        /// </summary>
        public static float ComputeEffectiveClothingInsulationClo(
            float dryInsulationClo,
            float airPermeabilityCoeff,
            float wetnessDecayExponent,
            float minWetRetentionRatio,
            bool isConductiveMetalOuter,
            bool hasInsulatingInnerLiner,
            float wetness01,
            float localWindSpeedMps,
            float ambientTempCelsius,
            out float thermalBridgePenaltyWatts)
        {
            float w = float.IsFinite(wetness01) ? Math.Clamp(wetness01, 0.0f, 1.0f) : 0.0f;
            float minRet = Math.Clamp(minWetRetentionRatio, 0.0f, 1.0f);
            float exp = MathF.Max(0.1f, wetnessDecayExponent);
            float wetRetention = MathF.Pow(1.0f - w, exp) * (1.0f - minRet) + minRet;

            float safeWind = float.IsFinite(localWindSpeedMps) ? MathF.Max(0.0f, localWindSpeedMps) : 0.0f;
            float windExcess = MathF.Max(0.0f, safeWind - 1.0f);
            float perm = MathF.Max(0.0f, airPermeabilityCoeff);
            float windRetention = 1.0f / (1.0f + perm * windExcess);

            if (isConductiveMetalOuter && !hasInsulatingInnerLiner && ambientTempCelsius < MetalThermalBridgeThresholdCelsius)
            {
                thermalBridgePenaltyWatts = (MetalThermalBridgeThresholdCelsius - ambientTempCelsius) * MetalThermalBridgeWattsPerDegree;
            }
            else
            {
                thermalBridgePenaltyWatts = 0.0f;
            }

            return MathF.Max(0.0f, dryInsulationClo * wetRetention * windRetention);
        }

        /// <summary>
        /// Tek bir UniqueEquipmentInstance (128B) yuvasının birincil dış malzemesi (PrimaryAlloyMaterialId),
        /// ikincil iç astarı (SecondaryHandleMaterialId), ThermalInsulationBonus01 alanı ve anlık ıslaklığından
        /// etkin clo, rüzgâr kesicilik ve metal termal köprü kaybını hesaplar.
        /// </summary>
        public static void EvaluateSingleGarmentSlot(
            in UniqueEquipmentInstance item,
            in ClothingSlotWetnessState wetState,
            float ambientTempCelsius,
            out float slotEffectiveClo,
            out float slotWindBlock01,
            out float slotThermalBridgeWatts,
            out bool isConductiveMetalOuter)
        {
            slotEffectiveClo = 0.0f;
            slotWindBlock01 = 0.0f;
            slotThermalBridgeWatts = 0.0f;
            isConductiveMetalOuter = false;

            if (item.IsBroken)
            {
                return;
            }

            byte primaryMat = item.PrimaryAlloyMaterialId;
            byte linerMat = item.SecondaryHandleMaterialId;
            float bonus01 = float.IsFinite(item.ThermalInsulationBonus01)
                ? Math.Clamp(item.ThermalInsulationBonus01, 0.0f, 1.0f)
                : 0.0f;

            if (primaryMat == MaterialIdCatalog.Air &&
                linerMat == MaterialIdCatalog.Air &&
                item.ArchetypeItemId == 0 &&
                bonus01 <= 0.0001f)
            {
                return;
            }

            ResolveSlotMaterialThermalParameters(
                primaryMat,
                linerMat,
                bonus01,
                out float dryClo,
                out float windBlock01,
                out _,
                out float minWetRetention,
                out float wetnessDecayExp,
                out _,
                out bool metalOuter,
                out bool hasFurOrLeatherLiner);

            isConductiveMetalOuter = metalOuter;
            slotWindBlock01 = windBlock01;

            float w = float.IsFinite(wetState.Wetness01) ? Math.Clamp(wetState.Wetness01, 0.0f, 1.0f) : 0.0f;
            float frozen = float.IsFinite(wetState.FrozenStiffness01) ? Math.Clamp(wetState.FrozenStiffness01, 0.0f, 1.0f) : 0.0f;
            float effectiveWet = MathF.Max(w, frozen);

            float wetRetention = MathF.Pow(1.0f - effectiveWet, wetnessDecayExp) * (1.0f - minWetRetention) + minWetRetention;
            if (frozen > 0.01f)
            {
                // Donmuş ıslak giysi yalıtımı en alt tabana (minWetRetention) doğru çökertir
                wetRetention = minWetRetention + (wetRetention - minWetRetention) * (1.0f - frozen);
            }

            slotEffectiveClo = MathF.Max(0.0f, dryClo * wetRetention);

            float safeAmbient = float.IsFinite(ambientTempCelsius) ? ambientTempCelsius : 20.0f;
            if (metalOuter && !hasFurOrLeatherLiner && safeAmbient < MetalThermalBridgeThresholdCelsius)
            {
                slotThermalBridgeWatts = (MetalThermalBridgeThresholdCelsius - safeAmbient) * MetalThermalBridgeWattsPerDegree;
            }
            else
            {
                slotThermalBridgeWatts = 0.0f;
            }
        }

        /// <summary>
        /// Yağmur maruziyeti, suya batma (submergedDepthMeters), çatı sığınağı ve kamp ateşi/fırın radyatif ısısına
        /// (nearbyFireRadiantFluxKwM2) göre 4 giysi yuvasının Wetness01 ve FrozenStiffness01 durumunu ilerletir (0 B GC).
        /// </summary>
        public static PlayerThermalGearSummary StepClothingWetnessFreezingAndDrying(
            ReadOnlySpan<UniqueEquipmentInstance> equippedGarments,
            Span<ClothingSlotWetnessState> slotStates,
            float ambientTempCelsius,
            float localWindSpeedMps,
            float openSkyRainMmPerHour,
            float submergedWaterDepthMeters,
            float nearbyFireRadiantFluxKwM2,
            float realDtSeconds)
        {
            float dt = float.IsFinite(realDtSeconds) ? Math.Clamp(realDtSeconds, 0.0f, 10.0f) : 0.0f;
            float safeAmbient = float.IsFinite(ambientTempCelsius) ? ambientTempCelsius : 20.0f;
            float safeWind = float.IsFinite(localWindSpeedMps) ? MathF.Max(0.0f, localWindSpeedMps) : 0.0f;
            float safeRain = float.IsFinite(openSkyRainMmPerHour) ? MathF.Max(0.0f, openSkyRainMmPerHour) : 0.0f;
            float safeSubmerge = float.IsFinite(submergedWaterDepthMeters) ? MathF.Max(0.0f, submergedWaterDepthMeters) : 0.0f;
            float safeFireKw = float.IsFinite(nearbyFireRadiantFluxKwM2) ? MathF.Max(0.0f, nearbyFireRadiantFluxKwM2) : 0.0f;

            int slotCount = Math.Max(equippedGarments.Length, slotStates.Length);
            if (slotCount <= 0)
            {
                return default;
            }

            float totalClo = 0.0f;
            float windThroughProduct = 1.0f;
            float maxSlotWindBlock = 0.0f;
            float totalBridgeWatts = 0.0f;
            float wetnessSum = 0.0f;
            float frozenSum = 0.0f;
            int wetTrackedCount = 0;
            int metalSlotCount = 0;
            int activeGarmentCount = 0;

            for (int i = 0; i < slotCount; i++)
            {
                UniqueEquipmentInstance item = i < equippedGarments.Length ? equippedGarments[i] : default;
                ClothingSlotWetnessState state = i < slotStates.Length ? slotStates[i] : default;

                if (!float.IsFinite(state.Wetness01)) state.Wetness01 = 0.0f;
                if (!float.IsFinite(state.FrozenStiffness01)) state.FrozenStiffness01 = 0.0f;
                state.Wetness01 = Math.Clamp(state.Wetness01, 0.0f, 1.0f);
                state.FrozenStiffness01 = Math.Clamp(state.FrozenStiffness01, 0.0f, 1.0f);

                bool hasGarment = !item.IsBroken &&
                                  (item.PrimaryAlloyMaterialId != MaterialIdCatalog.Air ||
                                   item.SecondaryHandleMaterialId != MaterialIdCatalog.Air ||
                                   item.ArchetypeItemId != 0 ||
                                   item.ThermalInsulationBonus01 > 0.0001f);

                float waterRepel01 = 0.10f;
                float dryRateMult = 1.20f;
                float slotWindBlock = 0.0f;

                if (hasGarment)
                {
                    activeGarmentCount++;
                    ResolveSlotMaterialThermalParameters(
                        item.PrimaryAlloyMaterialId,
                        item.SecondaryHandleMaterialId,
                        item.ThermalInsulationBonus01,
                        out _,
                        out slotWindBlock,
                        out waterRepel01,
                        out _,
                        out _,
                        out dryRateMult,
                        out _,
                        out _);
                }

                if (dt > 0.0f)
                {
                    // 1) Islanma kaynakları: Suya batma ve açık gökyüzü yağışı
                    float wettingRate = 0.0f;
                    if (safeSubmerge > 0.10f)
                    {
                        wettingRate += safeSubmerge >= 0.70f ? 0.48f : 0.35f;
                    }

                    if (safeRain > 0.05f)
                    {
                        float rainRate = 0.018f + 0.0030f * Math.Clamp(safeRain, 0.0f, 80.0f);
                        wettingRate += rainRate * (1.0f - Math.Clamp(waterRepel01, 0.0f, 0.95f));
                    }

                    // 2) Kamp ateşi / fırın radyatif kurutma & buz çözme + sığınak konvektif kuruma
                    float dryingRate = 0.0f;
                    if (safeFireKw > 0.01f && safeSubmerge <= 0.05f)
                    {
                        float clampedFire = Math.Clamp(safeFireKw, 0.0f, 40.0f);
                        float thawRate = (0.08f + 0.045f * clampedFire) * dt;
                        state.FrozenStiffness01 = Math.Clamp(state.FrozenStiffness01 - thawRate, 0.0f, 1.0f);
                        dryingRate += (0.035f + 0.022f * clampedFire) * dryRateMult;
                    }

                    if (safeRain <= 0.05f && safeSubmerge <= 0.05f)
                    {
                        float tempDryFactor = Math.Clamp((safeAmbient + 5.0f) / 25.0f, 0.05f, 2.2f);
                        float windConvectiveBoost = 1.0f + 0.05f * Math.Clamp(safeWind, 0.0f, 12.0f);
                        dryingRate += 0.010f * tempDryFactor * windConvectiveBoost * dryRateMult;

                        if (safeAmbient > 2.0f && state.FrozenStiffness01 > 0.0f)
                        {
                            float warmThaw = (safeAmbient - 2.0f) * 0.020f * dt;
                            state.FrozenStiffness01 = Math.Clamp(state.FrozenStiffness01 - warmThaw, 0.0f, 1.0f);
                        }
                    }

                    float netWetDelta = (wettingRate - dryingRate) * dt;
                    state.Wetness01 = Math.Clamp(state.Wetness01 + netWetDelta, 0.0f, 1.0f);

                    // 3) Dondurucu rüzgârda (T_wc <= -4.0°C) ateşten uzakta ıslak giysinin buz tutması
                    float slotTwc = ComputeJagTiWindChillCelsius(safeAmbient, safeWind, slotWindBlock * 0.6f);
                    if (safeFireKw <= 0.15f &&
                        safeSubmerge <= 0.05f &&
                        state.Wetness01 >= 0.40f &&
                        (slotTwc <= -4.0f || safeAmbient <= -4.0f))
                    {
                        float coldBelowFreeze = MathF.Max(0.0f, -4.0f - MathF.Min(slotTwc, safeAmbient));
                        float freezeRate = (0.025f + 0.0085f * coldBelowFreeze) * state.Wetness01;
                        state.FrozenStiffness01 = Math.Clamp(state.FrozenStiffness01 + freezeRate * dt, 0.0f, 1.0f);
                    }
                    else if (state.Wetness01 <= 0.02f && safeFireKw > 0.01f)
                    {
                        state.FrozenStiffness01 = 0.0f;
                    }
                }

                if (i < slotStates.Length)
                {
                    slotStates[i] = state;
                    wetnessSum += state.Wetness01;
                    frozenSum += state.FrozenStiffness01;
                    wetTrackedCount++;
                }

                if (hasGarment)
                {
                    EvaluateSingleGarmentSlot(
                        in item,
                        in state,
                        safeAmbient,
                        out float slotClo,
                        out float slotWb,
                        out float slotBridgeWatts,
                        out bool isMetalOuter);

                    totalClo += slotClo;
                    totalBridgeWatts += slotBridgeWatts;
                    maxSlotWindBlock = MathF.Max(maxSlotWindBlock, slotWb);
                    windThroughProduct *= (1.0f - Math.Clamp(slotWb * 0.55f, 0.0f, 0.85f));
                    if (isMetalOuter)
                    {
                        metalSlotCount++;
                    }
                }
            }

            float combinedWindBlock = activeGarmentCount > 0
                ? Math.Clamp(MathF.Max(maxSlotWindBlock, 1.0f - windThroughProduct), 0.0f, MaxAggregateWindBlock01)
                : 0.0f;

            float avgWetness = wetTrackedCount > 0 ? Math.Clamp(wetnessSum / wetTrackedCount, 0.0f, 1.0f) : 0.0f;
            float avgFrozen = wetTrackedCount > 0 ? Math.Clamp(frozenSum / wetTrackedCount, 0.0f, 1.0f) : 0.0f;
            float stiffnessPenalty = Math.Clamp(avgFrozen * MaxMovementStiffnessPenalty01, 0.0f, MaxMovementStiffnessPenalty01);

            int metalDenom = Math.Max(1, Math.Min(4, activeGarmentCount > 0 ? activeGarmentCount : 4));
            float metalLoad01 = metalSlotCount > 0
                ? Math.Clamp((float)metalSlotCount / metalDenom, 0.25f, 1.0f)
                : 0.0f;

            return new PlayerThermalGearSummary
            {
                TotalEffectiveClo = Math.Clamp(totalClo, 0.0f, MaxTotalEffectiveClo),
                AggregateWindBlock01 = combinedWindBlock,
                ThermalBridgePenaltyWatts = totalBridgeWatts,
                AverageWetness01 = avgWetness,
                MovementStiffnessPenalty01 = stiffnessPenalty,
                ConductiveMetalLoad01 = metalLoad01
            };
        }

        /// <summary>
        /// Kış giysisi veya zırh malzemelerinden 128-Byte UniqueEquipmentInstance oluşturur (0 B GC).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniqueEquipmentInstance CreateGarmentInstance(
            byte primaryMaterialId,
            byte secondaryLinerMaterialId = MaterialIdCatalog.Air,
            float thermalBonus01 = 0.0f,
            ushort archetypeItemId = 1)
        {
            return new UniqueEquipmentInstance
            {
                InstanceGuid = ((ulong)primaryMaterialId << 32) | ((ulong)secondaryLinerMaterialId << 16) | archetypeItemId,
                ArchetypeItemId = archetypeItemId,
                PrimaryAlloyMaterialId = primaryMaterialId,
                SecondaryHandleMaterialId = secondaryLinerMaterialId,
                MassKg = MaterialIdCatalog.IsMetallicConductive(primaryMaterialId) ? 4.5f : 1.4f,
                CurrentDurability = 100.0f,
                MaxDurability = 100.0f,
                ThermalInsulationBonus01 = Math.Clamp(thermalBonus01, 0.0f, 1.0f)
            };
        }

        /// <summary>
        /// PrimitiveCraftOutputId (14..17 kış giysileri) için kanonik 128-Byte UniqueEquipmentInstance döner.
        /// </summary>
        public static UniqueEquipmentInstance CreateWinterGarmentFromCraftOutput(PrimitiveCraftOutputId outputId)
        {
            return outputId switch
            {
                PrimitiveCraftOutputId.WovenClothTunic => CreateGarmentInstance(
                    MaterialIdCatalog.TacticalCloth, MaterialIdCatalog.Air, 0.0f, (ushort)outputId),
                PrimitiveCraftOutputId.OiledLeatherWindbreaker => CreateGarmentInstance(
                    MaterialIdCatalog.LeatherPadding, MaterialIdCatalog.TacticalCloth, 0.10f, (ushort)outputId),
                PrimitiveCraftOutputId.HeavyFurWinterParka => CreateGarmentInstance(
                    MaterialIdCatalog.CreatureFurAsh, MaterialIdCatalog.LeatherPadding, 0.0f, (ushort)outputId),
                PrimitiveCraftOutputId.FurLinedWinterBoots => CreateGarmentInstance(
                    MaterialIdCatalog.CreatureFurAsh, MaterialIdCatalog.LeatherPadding, 0.0f, (ushort)outputId),
                _ => default
            };
        }

        private static void ResolveSlotMaterialThermalParameters(
            byte primaryMat,
            byte linerMat,
            float bonus01,
            out float dryClo,
            out float windBlock01,
            out float waterRepel01,
            out float minWetRetention,
            out float wetnessDecayExp,
            out float dryRateMult,
            out bool isConductiveMetalOuter,
            out bool hasFurOrLeatherLiner)
        {
            float clampedBonus = float.IsFinite(bonus01) ? Math.Clamp(bonus01, 0.0f, 1.0f) : 0.0f;
            isConductiveMetalOuter = MaterialIdCatalog.IsMetallicConductive(primaryMat);
            hasFurOrLeatherLiner = linerMat == MaterialIdCatalog.CreatureFurAsh ||
                                   linerMat == MaterialIdCatalog.PhoenixFeatherGold ||
                                   linerMat == MaterialIdCatalog.LeatherPadding;

            if (isConductiveMetalOuter)
            {
                dryClo = BareMetalDryClo;
                windBlock01 = 0.90f;
                waterRepel01 = 1.00f;
                minWetRetention = 1.00f;
                wetnessDecayExp = 1.0f;
                dryRateMult = 2.50f;

                if (linerMat == MaterialIdCatalog.CreatureFurAsh || linerMat == MaterialIdCatalog.PhoenixFeatherGold)
                {
                    dryClo += 1.30f + (clampedBonus * 0.45f);
                    windBlock01 = 0.95f;
                    waterRepel01 = 0.90f;
                    minWetRetention = 0.65f;
                    wetnessDecayExp = 1.25f;
                    dryRateMult = 0.90f;
                }
                else if (linerMat == MaterialIdCatalog.LeatherPadding)
                {
                    dryClo += 0.90f + (clampedBonus * 0.35f);
                    windBlock01 = 0.95f;
                    waterRepel01 = 0.90f;
                    minWetRetention = 0.65f;
                    wetnessDecayExp = 1.20f;
                    dryRateMult = 1.05f;
                }
                else if (linerMat == MaterialIdCatalog.TacticalCloth)
                {
                    dryClo += 0.30f + (clampedBonus * 0.15f);
                    minWetRetention = 0.30f;
                }

                return;
            }

            if (primaryMat == MaterialIdCatalog.CreatureFurAsh || primaryMat == MaterialIdCatalog.PhoenixFeatherGold)
            {
                dryClo = HeavyFurDryClo + (clampedBonus * 0.55f);
                windBlock01 = linerMat == MaterialIdCatalog.LeatherPadding ? 0.88f : 0.72f;
                waterRepel01 = linerMat == MaterialIdCatalog.LeatherPadding ? 0.75f : 0.55f;
                minWetRetention = 0.35f;
                wetnessDecayExp = 1.35f;
                dryRateMult = 0.65f;
                return;
            }

            if (primaryMat == MaterialIdCatalog.LeatherPadding)
            {
                dryClo = LeatherDryClo + (clampedBonus * 0.35f);
                if (linerMat == MaterialIdCatalog.CreatureFurAsh || linerMat == MaterialIdCatalog.PhoenixFeatherGold)
                {
                    dryClo += 0.65f;
                }
                else if (linerMat == MaterialIdCatalog.TacticalCloth)
                {
                    dryClo += 0.15f;
                }

                windBlock01 = 0.88f;
                waterRepel01 = 0.80f;
                minWetRetention = 0.55f;
                wetnessDecayExp = 1.25f;
                dryRateMult = 0.85f;
                return;
            }

            if (primaryMat == MaterialIdCatalog.TacticalCloth ||
                primaryMat == MaterialIdCatalog.GlowingMoss ||
                primaryMat == MaterialIdCatalog.ThatchTile)
            {
                dryClo = ClothDryClo + (clampedBonus * 0.25f);
                windBlock01 = 0.25f;
                waterRepel01 = 0.10f;
                minWetRetention = 0.18f;
                wetnessDecayExp = 1.60f;
                dryRateMult = 1.50f;
                return;
            }

            // Ahşap veya diğer organik zırhlar için varsayılan orta katman
            dryClo = 0.35f + (clampedBonus * 0.50f);
            windBlock01 = 0.45f;
            waterRepel01 = 0.40f;
            minWetRetention = 0.35f;
            wetnessDecayExp = 1.30f;
            dryRateMult = 1.10f;
        }
    }
}
