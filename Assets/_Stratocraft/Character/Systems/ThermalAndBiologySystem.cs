#nullable enable
using System;
using Stratocraft.Character.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 3.3 — 6-Bilesenli Cevresel Sicaklik T_env,
    /// Surekli Islaklik (Wetness01 -&gt; Sogukta 3x Isi Kaybi!) ve A-Kararli Analitik Ustel (Exponential)
    /// Vucut Cekirdek Isisi T_core(t+dt) = T_eq + (T_core - T_eq)*exp(-lambda*dt) Biyoloji Sistemi.
    /// Herhangi bir dt adiminda (%100 kosulsuz sayisal kararli) sifir asim garantisi verir.
    /// </summary>
    public sealed class ThermalAndBiologySystem
    {
        public const float NormalBodyTempCelsius = 37.0f;
        public const float HypothermiaThresholdCelsius = 35.0f;
        public const float SevereHypothermiaThresholdCelsius = 33.5f;
        public const float HyperthermiaThresholdCelsius = 39.5f;
        public const float SevereHyperthermiaThresholdCelsius = 40.5f;

        /// <summary>
        /// Paket 4A: Son hesaplanan oyuncu yagmur/su islaklik orani (0.0 = Tam Kuru, 1.0 = Sirilsiklam).
        /// </summary>
        public float Wetness01;

        /// <summary>
        /// Paket 4B (İP-8): Son hesaplanan JAG/TI rüzgâr soğuğu sıcaklığı (°C).
        /// </summary>
        public float EffectiveWindChillCelsius { get; private set; } = 21.0f;

        /// <summary>Cevresel sicaklik efektleri kapaliyken HUD tutarliligi icin WindChill'i ortam sicakligiyla esler.</summary>
        public void SyncWindChillFallbackCelsius(float ambientCelsius)
        {
            if (float.IsFinite(ambientCelsius)) EffectiveWindChillCelsius = ambientCelsius;
        }

        /// <summary>
        /// Paket 4B (İP-8): Son hesaplanan toplam etkin giysi yalıtımı (clo).
        /// </summary>
        public float TotalEffectiveClo { get; private set; }

        /// <summary>
        /// Paket 4B (İP-8): Son hesaplanan astarsız metal zırh termal köprü iletim ısı kaybı (W).
        /// </summary>
        public float ThermalBridgePenaltyWatts { get; private set; }

        /// <summary>
        /// Paket 4B (İP-8): Son hesaplanan ıslak yüzey buharlaşmalı ısı kaybı (W).
        /// </summary>
        public float EvaporativeHeatLossWatts { get; private set; }

        /// <summary>
        /// Paket 4B (DOK3 §3.4.4): Toplam etkin giysi yalıtımı (clo) ve metabolik aktiviteye göre
        /// minimum nötr konfor sıcaklığı eşiğini (°C) hesaplar (baz 15.0°C, her 1.0 clo için -6.5°C).
        /// </summary>
        public static float ComputeComfortThresholdCelsius(
            float totalEffectiveClo,
            float metabolicActivityBonusCelsius = 0.0f)
        {
            float safeClo = float.IsFinite(totalEffectiveClo) ? MathF.Max(0.0f, totalEffectiveClo) : 0.0f;
            float safeMetabolic = float.IsFinite(metabolicActivityBonusCelsius) ? MathF.Max(0.0f, metabolicActivityBonusCelsius) : 0.0f;
            return 15.0f - (6.5f * safeClo) - safeMetabolic;
        }

        /// <summary>
        /// Paket 4B (İP-8): Islaklık oranı (0..1) ve cilde ulaşan etkin rüzgâr hızına göre
        /// buharlaşmalı + ıslak iletim ısı kaybını (Watt) hesaplar.
        /// </summary>
        public static float ComputeEvaporativeWetHeatLossWatts(
            float wetness01,
            float localWindSpeedMps,
            float clothingWindBlock01 = 0.0f)
        {
            float w = float.IsFinite(wetness01) ? Math.Clamp(wetness01, 0.0f, 1.0f) : 0.0f;
            if (w <= 0.0001f)
            {
                return 0.0f;
            }

            float safeWind = float.IsFinite(localWindSpeedMps) ? MathF.Max(0.0f, localWindSpeedMps) : 0.0f;
            float safeBlock = float.IsFinite(clothingWindBlock01) ? Math.Clamp(clothingWindBlock01, 0.0f, 0.95f) : 0.0f;
            float effectiveWind = safeWind * (1.0f - safeBlock);
            return w * (42.0f + (6.5f * Math.Clamp(effectiveWind, 0.0f, 30.0f)));
        }

        /// <summary>
        /// Paket 4A: Acik gokyuzunde yagmur altinda islanma, cati altinda veya ates/firin isisi yaninda hizla kuruma
        /// adimini ilerletir ve vucut isisi kaybi / hipotermi hizlandirma carpanini (1.0x .. 3.0x+) dondurur.
        /// Saf .NET 10 altinda (0 B GC) dogrudan birim testi edilebilir.
        /// </summary>
        public static float StepRainWetnessAndDrying(
            ref float wetness01,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float nearFireHeatCelsius,
            float gameDeltaSeconds)
        {
            if (!float.IsFinite(wetness01))
            {
                wetness01 = 0.0f;
            }

            if (!float.IsFinite(gameDeltaSeconds) || gameDeltaSeconds <= 0.0f)
            {
                wetness01 = Math.Clamp(wetness01, 0.0f, 1.0f);
                return ComputeHypothermiaHeatLossMultiplier(wetness01, 5.0f);
            }

            float safePrecip = float.IsFinite(precipMmPerHour) ? MathF.Max(0.0f, precipMmPerHour) : 0.0f;
            float safeFireHeat = float.IsFinite(nearFireHeatCelsius) ? MathF.Max(0.0f, nearFireHeatCelsius) : 0.0f;

            if (hasOpenSkyAbove && safePrecip > 0.05f)
            {
                // Acik gokyuzunde aktif yagmur altinda: yagis siddetiyle (mm/saat) orantili islanma
                float wettingRatePerSec = 0.016f + 0.0028f * Math.Clamp(safePrecip, 0.0f, 80.0f);
                wetness01 = Math.Clamp(wetness01 + wettingRatePerSec * gameDeltaSeconds, 0.0f, 1.0f);
            }
            else
            {
                // Cati/magara altinda (!hasOpenSkyAbove) veya yagmursuz havada: baz kuruma + ates yaninda hizli termal kuruma
                float dryingRatePerSec = hasOpenSkyAbove ? 0.010f : 0.020f;
                if (safeFireHeat > 0.0f)
                {
                    dryingRatePerSec += 0.0075f * Math.Clamp(safeFireHeat, 0.0f, 120.0f);
                }
                wetness01 = Math.Clamp(wetness01 - dryingRatePerSec * gameDeltaSeconds, 0.0f, 1.0f);
            }

            return ComputeHypothermiaHeatLossMultiplier(wetness01, 5.0f);
        }

        /// <summary>
        /// Paket 4A: Ortam sicakligini da hesaba katan asiri yuklenmis (overload) yagmur islakligi ve kuruma adimi.
        /// </summary>
        public static float StepRainWetnessAndDrying(
            ref float wetness01,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float nearFireHeatCelsius,
            float gameDeltaSeconds,
            float ambientTempCelsius)
        {
            StepRainWetnessAndDrying(ref wetness01, precipMmPerHour, hasOpenSkyAbove, nearFireHeatCelsius, gameDeltaSeconds);
            return ComputeHypothermiaHeatLossMultiplier(wetness01, ambientTempCelsius);
        }

        /// <summary>
        /// Paket 4A: Islaklik (0..1) ve ortam sicakligina gore vucut isisi kaybi / hipotermi hizlanma carpanini (1.0x .. 3.5x) hesaplar.
        /// Sogukta islak kiyafet/deri suyun yuksek isil iletkenligi nedeniyle isi kaybini 3 kata kadar hizlandirir.
        /// </summary>
        public static float ComputeHypothermiaHeatLossMultiplier(float wetness01, float ambientTempCelsius = 5.0f)
        {
            float w = float.IsFinite(wetness01) ? Math.Clamp(wetness01, 0.0f, 1.0f) : 0.0f;
            float baseMult = 1.0f + w * 2.0f;
            if (float.IsFinite(ambientTempCelsius) && ambientTempCelsius < 15.0f && w > 0.001f)
            {
                float coldSeverity01 = Math.Clamp((15.0f - ambientTempCelsius) / 30.0f, 0.0f, 1.0f);
                baseMult += w * coldSeverity01 * 0.5f;
            }
            return baseMult;
        }

        /// <summary>Ornek bazli atardamar kanama hizi (HP/s).</summary>
        public float BleedRateHpPerSec { get; set; }

        /// <summary>Canli oyuncu savas koprusu tarafindan tetiklenen kuresel atardamar kanama hizi (HP/s).</summary>
        public static float GlobalPlayerBleedRateHpPerSec { get; set; }

        /// <summary>Oyuncuya bolgesel atardamar kanamasi (HeavyBleeding) ekler.</summary>
        public static void ApplyRegionalBleeding(ref CharacterVitalsComponent vitals, float addBleedRateHpPerSec = 1.5f)
        {
            if (addBleedRateHpPerSec <= 0f) return;
            GlobalPlayerBleedRateHpPerSec = Math.Min(12.0f, GlobalPlayerBleedRateHpPerSec + addBleedRateHpPerSec);
            vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.HeavyBleeding;
        }

        /// <summary>Dalga A (Faz A.5): Tibbi Bandaj ile agir kanamayi (HeavyBleeding) kesin olarak temizler.</summary>
        public static void ClearHeavyBleeding(ref CharacterVitalsComponent vitals, ThermalAndBiologySystem? instance = null)
        {
            GlobalPlayerBleedRateHpPerSec = 0f;
            if (instance != null) instance.BleedRateHpPerSec = 0f;
            vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.HeavyBleeding;
        }

        /// <summary>
        /// Dalga A (Faz A.4 &amp; A.5): Cevresel sicaklik efektleri kapali olsa dahi Hayatta Kalma modunda
        /// dengeli aclik (~35 dk), susuzluk (~23 dk), agir kanama ve 30 sn BleedOutCrawl sayacini ilerletir.
        /// </summary>
        public static void StepSurvivalMetabolismAndBleedOut(
            ref CharacterVitalsComponent vitals,
            float deltaTime,
            bool isCreativeMode,
            bool isSprintingOrCombat = false)
        {
            if (deltaTime <= 0f || !float.IsFinite(deltaTime)) return;
            if (isCreativeMode)
            {
                vitals.CurrentHp = vitals.MaxHp;
                vitals.Stamina = vitals.MaxStamina;
                vitals.HungerKcal = 2500.0f;
                vitals.ThirstHydration = 100.0f;
                vitals.BleedOutTimerSeconds = 30.0f;
                return;
            }

            float activityMult = isSprintingOrCombat ? 1.45f : 1.0f;
            // ~35 dk aclik (2500 kcal / 1.15 kcal/s), ~23 dk susuzluk (100% / 0.072 %/s)
            vitals.HungerKcal = MathF.Max(0.0f, vitals.HungerKcal - 1.15f * activityMult * deltaTime);
            vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.072f * activityMult * deltaTime);

            if (vitals.HungerKcal <= 0.01f || vitals.ThirstHydration <= 0.01f)
            {
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * deltaTime);
            }

            if (vitals.CurrentHp > 0.0f)
            {
                vitals.BleedOutTimerSeconds = 30.0f;
            }
            else if (vitals.BleedOutTimerSeconds > 0.0f)
            {
                vitals.BleedOutTimerSeconds = MathF.Max(0.0f, vitals.BleedOutTimerSeconds - deltaTime);
            }
        }

        /// <summary>
        /// Faz 3.2 (DOK3 §2.3 &amp; DOK4 §3.2.A): Speleolojik Soguk Hava Tuzagi ve Jeotermal Gradyan (C1-Surekli U-Egrisi).
        /// - Giris gecisi (d = 0..11m, Y = 96..85m): T_yuzey -&gt; sig soguk cep sicakligina C1 Hermite gecis
        /// - 1. Katman Sig Soguk Hava Tuzagi (Y = 85..55m, d ~ 11..41m): -6.0 °C .. +4.0 °C
        /// - 2. Katman Iliman Jeotermal Denge (Y = 55..45m, d ~ 41..51m): +5.0 °C .. +24.0 °C
        /// - 3. Katman Derin Bazalt &amp; Titanyum (Y = 44..25m, d ~ 52..71m): +28.0 °C .. +65.0 °C
        /// - 4. Katman Kaynayan Magma Kubbeleri (Y = 24..5m, d ~ 72..91m): +65.0 °C .. +110.0 °C
        /// </summary>
        public static float EvaluateUndergroundGeothermalTemperatureCelsius(
            float surfaceBiomeTempCelsius,
            float depthBelowSurfaceMeters,
            float absoluteWorldY)
        {
            float effectiveDepth = depthBelowSurfaceMeters;
            float effectiveY = absoluteWorldY;

            if (float.IsNaN(effectiveY) || (effectiveY <= 0.0f && effectiveDepth > 0.0f))
            {
                effectiveY = 96.0f - MathF.Max(0.0f, effectiveDepth);
            }

            if (effectiveDepth <= 0.0f && effectiveY > 0.0f && effectiveY < 96.0f)
            {
                effectiveDepth = 96.0f - effectiveY;
            }

            float clampedBiome = Math.Clamp(surfaceBiomeTempCelsius, -30.0f, 30.0f);
            float coldEdge = Math.Clamp(1.8f + 0.06f * clampedBiome, 0.2f, 3.6f);
            float coldPocketDrop = 4.6f + MathF.Max(0.0f, -clampedBiome) * 0.08f;

            // Giris agzi gecisi (0 < d < 11m ve Y > 85m): yuzey sicakligindan sig magara soguk tuzagina C1 gecis
            if (effectiveDepth < 11.0f && effectiveY > 85.0f)
            {
                float tEntrance = Math.Clamp((effectiveDepth - 1.5f) / 9.5f, 0.0f, 1.0f);
                float smoothEntrance = tEntrance * tEntrance * (3.0f - 2.0f * tEntrance);
                return surfaceBiomeTempCelsius + (coldEdge - surfaceBiomeTempCelsius) * smoothEntrance;
            }

            // 1. Katman: Sig Magara Soguk Hava Tuzagi (Y = 85..55m, depth ~ 11..41m) -> [-6.0 °C .. +4.0 °C]
            if (effectiveY >= 55.0f)
            {
                float u = Math.Clamp((85.0f - effectiveY) / 30.0f, 0.0f, 1.0f);
                float sinVal = MathF.Sin(MathF.PI * u);
                float coldTrapTemp = coldEdge - coldPocketDrop * (sinVal * sinVal);
                return Math.Clamp(coldTrapTemp, -6.0f, 4.0f);
            }

            // 2. Katman: Gecis Bolgesi / Iliman Jeotermal Denge (Y = 55..45m) -> [+5.0 °C .. +24.0 °C]
            if (effectiveY >= 45.0f)
            {
                float tMid = Math.Clamp((55.0f - effectiveY) / 10.0f, 0.0f, 1.0f);
                float smoothMid = tMid + 0.35f * tMid * (1.0f - tMid);
                float startMid = MathF.Max(coldEdge, 3.8f);
                return Math.Clamp(startMid + (24.0f - startMid) * smoothMid, 5.0f, 24.0f);
            }

            // Y = 45..44m dar omuz gecisi (+24.0 °C -> +28.5 °C)
            if (effectiveY > 44.0f)
            {
                float tShoulder = Math.Clamp(45.0f - effectiveY, 0.0f, 1.0f);
                float smoothShoulder = tShoulder * tShoulder * (3.0f - 2.0f * tShoulder);
                return 24.0f + 4.5f * smoothShoulder;
            }

            // 3. Katman: Derin Bazalt Katmani (Y = 44..25m) -> [+28.0 °C .. +65.0 °C]
            if (effectiveY >= 25.0f)
            {
                float tDeep = Math.Clamp((44.0f - effectiveY) / 19.0f, 0.0f, 1.0f);
                float smoothDeep = 0.65f * tDeep + 0.35f * (tDeep * tDeep * (3.0f - 2.0f * tDeep));
                return Math.Clamp(28.5f + (65.0f - 28.5f) * smoothDeep, 28.0f, 65.0f);
            }

            // 4. Katman: Kaynayan Magma Kubbeleri (Y = 24..5m) -> [+65.0 °C .. +110.0 °C]
            float tMagma = Math.Clamp((25.0f - effectiveY) / 20.0f, 0.0f, 1.0f);
            float quadMagma = 0.75f * tMagma + 0.25f * (tMagma * tMagma * (3.0f - 2.0f * tMagma));
            return Math.Clamp(65.0f + 45.0f * quadMagma, 65.0f, 110.0f);
        }

        /// <summary>DOK4 tablo referansi ile %100 uyumlu es-anlamli yardimci.</summary>
        public static float EvaluateUndergroundGeothermalCurveCelsius(
            float surfaceBiomeTempCelsius,
            float depthBelowSurfaceMeters,
            float absoluteWorldY)
            => EvaluateUndergroundGeothermalTemperatureCelsius(surfaceBiomeTempCelsius, depthBelowSurfaceMeters, absoluteWorldY);

        /// <summary>
        /// 6 bilesenli cevresel hissedilen sicaklik hesaplayicisi:
        /// T_env = T_season + dT_altitude + dT_underground + dT_dayNight + dT_windChill + dT_heatSources.
        /// Yeraltinda (isUndergroundEnclosed &amp;&amp; worldAltitudeMeters &lt; -1.5f) C1-surekli Jeotermal U-Egrisi uygulanir.
        /// </summary>
        public static float EvaluateAmbientTemperatureCelsius(
            float baseSeasonTempCelsius,
            float worldAltitudeMeters,
            bool isUndergroundEnclosed,
            bool isNightTime,
            float windSpeedMetersPerSec,
            float nearbyHeatSourceBonusCelsius,
            float absoluteWorldY = float.NaN)
        {
            if (isUndergroundEnclosed && worldAltitudeMeters < -1.5f)
            {
                float depthBelowSurfaceMeters = -worldAltitudeMeters;
                float resolvedWorldY = float.IsNaN(absoluteWorldY)
                    ? (96.0f - depthBelowSurfaceMeters)
                    : absoluteWorldY;
                float geothermalTemp = EvaluateUndergroundGeothermalTemperatureCelsius(
                    baseSeasonTempCelsius,
                    depthBelowSurfaceMeters,
                    resolvedWorldY);
                return geothermalTemp + nearbyHeatSourceBonusCelsius;
            }

            // Yuksek irtifa sogumasi (-6.5 C / 100m oyun olcekli atmosfer gradyani)
            float altitudeDelta = worldAltitudeMeters > 0f
                ? -Math.Min(28.0f, worldAltitudeMeters * 0.14f)
                : 0f;

            float dayNightDelta = (!isUndergroundEnclosed && isNightTime) ? -6.5f : 0f;
            float windChillDelta = (!isUndergroundEnclosed && baseSeasonTempCelsius < 18.0f)
                ? -Math.Min(10.0f, windSpeedMetersPerSec * 0.35f)
                : 0f;

            return baseSeasonTempCelsius + altitudeDelta + dayNightDelta + windChillDelta + nearbyHeatSourceBonusCelsius;
        }

        /// <summary>
        /// Ortam sicakligi, zirh yalitimi (0..1) ve islaklik ODE durumuna gore biyolojik degerleri A-kararli gunceller.
        /// </summary>
        public void StepBiology(
            ref CharacterVitalsComponent vitals,
            float ambientTempCelsius,
            float armorThermalInsulation01,
            bool isSubmergedOrInRain,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // 1. Surekli Islaklik (Wetness01) Diferansiyel Denklemi
            if (isSubmergedOrInRain)
            {
                float wetTarget = vitals.SubmergedDepthMeters > 0.1f ? 1.0f : 0.85f;
                float wetAlpha = 1.0f - MathF.Exp(-1.2f * deltaTime);
                vitals.Wetness01 = Math.Clamp(vitals.Wetness01 + (wetTarget - vitals.Wetness01) * wetAlpha, 0f, 1f);
            }
            else
            {
                // Sicak ortamda / kamp atesi yaninda hizla kurur
                float dryRate = 0.025f + MathF.Max(0f, ambientTempCelsius - 15.0f) * 0.004f;
                vitals.Wetness01 = Math.Clamp(vitals.Wetness01 * MathF.Exp(-dryRate * deltaTime), 0f, 1f);
            }
            Wetness01 = vitals.Wetness01;

            // 2. Zirh Yalitimi ve Islaklikta 3x Soguk Isi Kaybi (GAME LOGIC BIBLE v7.0 Bolum 3.3)
            float effectiveWetness = isSubmergedOrInRain ? MathF.Max(vitals.Wetness01, 0.75f) : vitals.Wetness01;
            float insulation = Math.Clamp(armorThermalInsulation01, 0.0f, 0.95f) * (1.0f - 0.60f * effectiveWetness);

            float ambientDeltaFromComfort = ambientTempCelsius - 22.0f;
            bool isColdEnvironment = ambientTempCelsius < 22.0f;
            float wetColdLossMultiplier = isColdEnvironment ? (1.0f + 2.0f * effectiveWetness) : 1.0f; // Tam islakken 3.0x soguma!

            float equilibriumShift = ambientDeltaFromComfort * (1.0f - insulation) * 0.095f * wetColdLossMultiplier;
            float targetCoreTemp = Math.Clamp(NormalBodyTempCelsius + equilibriumShift, 26.0f, 44.0f);

            // A-Kararli Analitik Ustel Adim: T(t+dt) = T_eq + (T(t) - T_eq) * exp(-lambda * dt)
            float baseConductance = 0.035f * wetColdLossMultiplier;
            float decay = MathF.Exp(-baseConductance * deltaTime);
            vitals.CoreBodyTempCelsius = targetCoreTemp + (vitals.CoreBodyTempCelsius - targetCoreTemp) * decay;

            // 3. Hipotermi (< 35.0 C) ve Hipertermi (> 39.5 C) Etkileri (GAME LOGIC BIBLE §3.2.A & §3.5.B)
            bool isHypothermic = vitals.CoreBodyTempCelsius < HypothermiaThresholdCelsius;
            bool isHyperthermic = vitals.CoreBodyTempCelsius > HyperthermiaThresholdCelsius;

            if (isHypothermic)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hypothermia;
                float severity = vitals.CoreBodyTempCelsius < SevereHypothermiaThresholdCelsius
                    ? (1.0f + (SevereHypothermiaThresholdCelsius - vitals.CoreBodyTempCelsius) * 0.8f)
                    : 1.0f;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * severity * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hypothermia;
            }

            if (isHyperthermic)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hyperthermia;
                vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.9f * deltaTime);
                vitals.Stamina = MathF.Max(0.0f, vitals.Stamina - 2.5f * deltaTime);
                if (vitals.CoreBodyTempCelsius > SevereHyperthermiaThresholdCelsius)
                {
                    vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.8f * deltaTime);
                }
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hyperthermia;
            }

            // 4. Bazal Metabolizma Tuketimi: Hipotermide titreme kCal tuketimini 2.0x artirir, Hipertermide su kaybi 2.0x artar!
            float coldShiverMultiplier = isHypothermic
                ? 2.0f
                : (vitals.CoreBodyTempCelsius < 36.0f ? 1.6f : 1.0f);
            float heatThirstMultiplier = isHyperthermic ? 2.0f : 1.0f;
            // Faz A.5: Dengeli hayatta kalma hizi (~35 dk aclik, ~23 dk susuzluk)
            vitals.HungerKcal = MathF.Max(0.0f, vitals.HungerKcal - 1.15f * coldShiverMultiplier * deltaTime);
            vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.072f * heatThirstMultiplier * deltaTime);

            // 5. Aclik / Susuzluk Cezasi vs. Tokluk (Well-Fed) Yenilenme Bonusu
            if (vitals.HungerKcal <= 0.01f || vitals.ThirstHydration <= 0.01f)
            {
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * deltaTime);
                float maxStarvedStamina = vitals.MaxStamina * 0.40f;
                if (vitals.Stamina > maxStarvedStamina)
                {
                    vitals.Stamina = maxStarvedStamina;
                }
            }
            else if (vitals.HungerKcal >= 1600.0f &&
                     vitals.ThirstHydration >= 80.0f &&
                     vitals.CurrentHp > 0.0f &&
                     !isHypothermic &&
                     !isHyperthermic &&
                     (vitals.ActiveDiseaseFlags & (byte)(BiologicalAfflictionFlags.Hypoxia | BiologicalAfflictionFlags.HeavyBleeding | BiologicalAfflictionFlags.BarotraumaCrush)) == 0)
            {
                vitals.CurrentHp = MathF.Min(vitals.MaxHp, vitals.CurrentHp + 0.8f * deltaTime);
                vitals.Stamina = MathF.Min(vitals.MaxStamina, vitals.Stamina + 4.0f * deltaTime);
            }

            // 6. Atardamar Kanamasi (Arterial Bleeding) ve Dogal Pihtilasma (0.35 HP/s^2 azalma)
            float activeBleed = MathF.Max(BleedRateHpPerSec, GlobalPlayerBleedRateHpPerSec);
            if ((vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0 && activeBleed <= 0.001f)
            {
                activeBleed = 1.5f;
                BleedRateHpPerSec = 1.5f;
            }
            if (activeBleed > 0.001f)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.HeavyBleeding;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - activeBleed * deltaTime);
                float clotStep = 0.35f * deltaTime;
                BleedRateHpPerSec = MathF.Max(0.0f, BleedRateHpPerSec - clotStep);
                GlobalPlayerBleedRateHpPerSec = MathF.Max(0.0f, GlobalPlayerBleedRateHpPerSec - clotStep);
                if (MathF.Max(BleedRateHpPerSec, GlobalPlayerBleedRateHpPerSec) <= 0.001f)
                {
                    vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.HeavyBleeding;
                }
            }

            // 7. 0 HP durumunda 30 saniyelik BleedOut geri sayimi
            if (vitals.CurrentHp <= 0.0f && vitals.BleedOutTimerSeconds > 0.0f)
            {
                vitals.BleedOutTimerSeconds = MathF.Max(0.0f, vitals.BleedOutTimerSeconds - deltaTime);
            }
        }

        /// <summary>
        /// Paket 4B (İP-8): Çıplak hava sıcaklığı yerine JAG/TI EffectiveWindChillCelsius, giysi etkin TotalEffectiveClo
        /// ısıl direnci, astarsız metal zırh ThermalBridgePenaltyWatts iletim kaybı ve ıslak buharlaşma ısı kaybını
        /// 37.0°C çekirdek termodinamiğine bağlayan aşırı yüklenmiş biyoloji adımı.
        /// </summary>
        public void StepBiology(
            ref CharacterVitalsComponent vitals,
            float airTempCelsius,
            float localWindSpeedMps,
            in PlayerThermalGearSummary gearSummary,
            float deltaTime)
        {
            StepBiology(
                ref vitals,
                airTempCelsius,
                localWindSpeedMps,
                in gearSummary,
                isSubmergedOrInRain: false,
                deltaTime: deltaTime);
        }

        /// <summary>
        /// Paket 4B (İP-8): JAG/TI Wind Chill, katmanlı clo, ıslak buharlaşma ve metal termal köprü
        /// ile tam entegre A-kararlı çekirdek vücut ısısı ve biyoloji adımı.
        /// </summary>
        public void StepBiology(
            ref CharacterVitalsComponent vitals,
            float airTempCelsius,
            float localWindSpeedMps,
            in PlayerThermalGearSummary gearSummary,
            bool isSubmergedOrInRain,
            float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime <= 0.0f)
            {
                return;
            }

            float safeAirTemp = float.IsFinite(airTempCelsius) ? airTempCelsius : 20.0f;
            float safeWind = float.IsFinite(localWindSpeedMps) ? MathF.Max(0.0f, localWindSpeedMps) : 0.0f;
            float effectiveClo = float.IsFinite(gearSummary.TotalEffectiveClo)
                ? Math.Clamp(gearSummary.TotalEffectiveClo, 0.0f, ThermalClothingInsulationSystem.MaxTotalEffectiveClo)
                : 0.0f;
            float windBlock01 = float.IsFinite(gearSummary.AggregateWindBlock01)
                ? Math.Clamp(gearSummary.AggregateWindBlock01, 0.0f, ThermalClothingInsulationSystem.MaxAggregateWindBlock01)
                : 0.0f;
            float bridgeWatts = float.IsFinite(gearSummary.ThermalBridgePenaltyWatts)
                ? MathF.Max(0.0f, gearSummary.ThermalBridgePenaltyWatts)
                : 0.0f;
            float gearWetness = float.IsFinite(gearSummary.AverageWetness01)
                ? Math.Clamp(gearSummary.AverageWetness01, 0.0f, 1.0f)
                : 0.0f;

            if (isSubmergedOrInRain && gearWetness <= 0.001f)
            {
                float wetTarget = vitals.SubmergedDepthMeters > 0.1f ? 1.0f : 0.85f;
                float wetAlpha = 1.0f - MathF.Exp(-1.2f * deltaTime);
                vitals.Wetness01 = Math.Clamp(vitals.Wetness01 + (wetTarget - vitals.Wetness01) * wetAlpha, 0.0f, 1.0f);
                gearWetness = MathF.Max(gearWetness, vitals.Wetness01);
            }
            else
            {
                vitals.Wetness01 = gearWetness;
            }

            Wetness01 = gearWetness;
            TotalEffectiveClo = effectiveClo;
            ThermalBridgePenaltyWatts = bridgeWatts;

            float effectiveWindChill = ThermalClothingInsulationSystem.ComputeJagTiWindChillCelsius(
                safeAirTemp,
                safeWind,
                windBlock01);
            EffectiveWindChillCelsius = effectiveWindChill;

            float evapWatts = ComputeEvaporativeWetHeatLossWatts(gearWetness, safeWind, windBlock01);
            EvaporativeHeatLossWatts = evapWatts;

            // Termal denge (T_eq) ve iletkenlik (lambda) hesabı:
            // - Kuru kürk parka (1.45 clo) veya kürk astarlı metal zırh (1.35 clo) T_a = -10°C, V = 18 m/s tipide >= 36.5°C normotermiyi korur.
            // - Çıplak, ıslak kumaşlı (0.08 clo) veya astarsız metal zırhlı (> 40W köprü cezası) oyuncu hızla < 35.0°C hipotermiye girer.
            float targetCoreTemp;
            float baseConductance;

            if (effectiveWindChill < 22.0f)
            {
                float cloWarmthShift = effectiveClo * 19.5f * (1.0f - 0.65f * gearWetness);
                float bridgeCoolingShift = bridgeWatts * 0.32f;
                float evapCoolingShift = evapWatts * 0.08f;
                float microclimateTemp = effectiveWindChill + cloWarmthShift - bridgeCoolingShift - evapCoolingShift;

                float coldDeficit = MathF.Min(0.0f, microclimateTemp - 18.0f);
                float cloResistance = 1.0f + (1.85f * effectiveClo * (1.0f - 0.75f * gearWetness));
                float equilibriumShift = (coldDeficit * 0.145f) / MathF.Max(1.0f, cloResistance);
                targetCoreTemp = Math.Clamp(NormalBodyTempCelsius + equilibriumShift, 26.0f, NormalBodyTempCelsius);

                float coldLossMultiplier = 1.0f + (2.2f * gearWetness) + (bridgeWatts * 0.025f);
                baseConductance = (0.065f * coldLossMultiplier) / MathF.Max(1.0f, cloResistance);
            }
            else
            {
                float heatExcess = effectiveWindChill - 22.0f;
                float heatRetention = 1.0f + (0.12f * effectiveClo) - (0.35f * gearWetness);
                float equilibriumShift = heatExcess * 0.085f * Math.Clamp(heatRetention, 0.4f, 1.4f);
                targetCoreTemp = Math.Clamp(NormalBodyTempCelsius + equilibriumShift, NormalBodyTempCelsius, 44.0f);
                baseConductance = 0.035f;
            }

            float decay = MathF.Exp(-baseConductance * deltaTime);
            vitals.CoreBodyTempCelsius = targetCoreTemp + (vitals.CoreBodyTempCelsius - targetCoreTemp) * decay;

            // Hipotermi (< 35.0 C) ve Hipertermi (> 39.5 C) Etkileri
            bool isHypothermic = vitals.CoreBodyTempCelsius < HypothermiaThresholdCelsius;
            bool isHyperthermic = vitals.CoreBodyTempCelsius > HyperthermiaThresholdCelsius;

            if (isHypothermic)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hypothermia;
                float severity = vitals.CoreBodyTempCelsius < SevereHypothermiaThresholdCelsius
                    ? (1.0f + (SevereHypothermiaThresholdCelsius - vitals.CoreBodyTempCelsius) * 0.8f)
                    : 1.0f;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * severity * deltaTime);
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hypothermia;
            }

            if (isHyperthermic)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hyperthermia;
                vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.9f * deltaTime);
                vitals.Stamina = MathF.Max(0.0f, vitals.Stamina - 2.5f * deltaTime);
                if (vitals.CoreBodyTempCelsius > SevereHyperthermiaThresholdCelsius)
                {
                    vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.8f * deltaTime);
                }
            }
            else
            {
                vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.Hyperthermia;
            }

            float coldShiverMultiplier = isHypothermic
                ? 2.0f
                : (vitals.CoreBodyTempCelsius < 36.0f ? 1.6f : 1.0f);
            float heatThirstMultiplier = isHyperthermic ? 2.0f : 1.0f;
            // Faz A.5: Dengeli hayatta kalma hizi (~35 dk aclik, ~23 dk susuzluk)
            vitals.HungerKcal = MathF.Max(0.0f, vitals.HungerKcal - 1.15f * coldShiverMultiplier * deltaTime);
            vitals.ThirstHydration = MathF.Max(0.0f, vitals.ThirstHydration - 0.072f * heatThirstMultiplier * deltaTime);

            if (vitals.HungerKcal <= 0.01f || vitals.ThirstHydration <= 0.01f)
            {
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - 1.5f * deltaTime);
                float maxStarvedStamina = vitals.MaxStamina * 0.40f;
                if (vitals.Stamina > maxStarvedStamina)
                {
                    vitals.Stamina = maxStarvedStamina;
                }
            }
            else if (vitals.HungerKcal >= 1600.0f &&
                     vitals.ThirstHydration >= 80.0f &&
                     vitals.CurrentHp > 0.0f &&
                     !isHypothermic &&
                     !isHyperthermic &&
                     (vitals.ActiveDiseaseFlags & (byte)(BiologicalAfflictionFlags.Hypoxia | BiologicalAfflictionFlags.HeavyBleeding | BiologicalAfflictionFlags.BarotraumaCrush)) == 0)
            {
                vitals.CurrentHp = MathF.Min(vitals.MaxHp, vitals.CurrentHp + 0.8f * deltaTime);
                vitals.Stamina = MathF.Min(vitals.MaxStamina, vitals.Stamina + 4.0f * deltaTime);
            }

            float activeBleed = MathF.Max(BleedRateHpPerSec, GlobalPlayerBleedRateHpPerSec);
            if ((vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0 && activeBleed <= 0.001f)
            {
                activeBleed = 1.5f;
                BleedRateHpPerSec = 1.5f;
            }
            if (activeBleed > 0.001f)
            {
                vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.HeavyBleeding;
                vitals.CurrentHp = MathF.Max(0.0f, vitals.CurrentHp - activeBleed * deltaTime);
                float clotStep = 0.35f * deltaTime;
                BleedRateHpPerSec = MathF.Max(0.0f, BleedRateHpPerSec - clotStep);
                GlobalPlayerBleedRateHpPerSec = MathF.Max(0.0f, GlobalPlayerBleedRateHpPerSec - clotStep);
                if (MathF.Max(BleedRateHpPerSec, GlobalPlayerBleedRateHpPerSec) <= 0.001f)
                {
                    vitals.ActiveDiseaseFlags &= (byte)~BiologicalAfflictionFlags.HeavyBleeding;
                }
            }

            if (vitals.CurrentHp <= 0.0f && vitals.BleedOutTimerSeconds > 0.0f)
            {
                vitals.BleedOutTimerSeconds = MathF.Max(0.0f, vitals.BleedOutTimerSeconds - deltaTime);
            }
        }

        /// <summary>
        /// Paket 4B (İP-8): Eş-anlamlı açık adlandırılmış termal giysi biyoloji adımı.
        /// </summary>
        public void StepBiologyWithThermalGear(
            ref CharacterVitalsComponent vitals,
            float airTempCelsius,
            float localWindSpeedMps,
            in PlayerThermalGearSummary gearSummary,
            float deltaTime,
            bool isSubmergedOrInRain = false)
        {
            StepBiology(
                ref vitals,
                airTempCelsius,
                localWindSpeedMps,
                in gearSummary,
                isSubmergedOrInRain,
                deltaTime);
        }
    }
}
