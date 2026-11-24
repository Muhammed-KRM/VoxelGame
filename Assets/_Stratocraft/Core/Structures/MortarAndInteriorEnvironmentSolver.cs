#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Core.Structures
{
    /// <summary>
    /// Cati kar yuku (SWE) degerlendirme ciktisi (DOK4 §4.1.B, AK-14, T14).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct RoofSnowLoadEvaluation
    {
        public readonly float SweKgPerM2;
        public readonly float SlopeShapeFactorMu;
        public readonly float EffectiveSnowPressureKpa;
        public readonly float RafterLineLoadKnPerM;
        public readonly float BendingMomentKnM;
        public readonly float MomentCapacityKnM;
        public readonly float UtilizationRatio;
        public readonly bool IsOverloaded;

        public RoofSnowLoadEvaluation(
            float sweKgPerM2,
            float slopeShapeFactorMu,
            float effectiveSnowPressureKpa,
            float rafterLineLoadKnPerM,
            float bendingMomentKnM,
            float momentCapacityKnM,
            float utilizationRatio,
            bool isOverloaded)
        {
            SweKgPerM2 = sweKgPerM2;
            SlopeShapeFactorMu = slopeShapeFactorMu;
            EffectiveSnowPressureKpa = effectiveSnowPressureKpa;
            RafterLineLoadKnPerM = rafterLineLoadKnPerM;
            BendingMomentKnM = bendingMomentKnM;
            MomentCapacityKnM = momentCapacityKnM;
            UtilizationRatio = utilizationRatio;
            IsOverloaded = isOverloaded;
        }
    }

    /// <summary>
    /// Kapali ic mekan 3B kabuk flood-fill, derz dolgusu (Chinking), ruzgar gecirgenligi ve isil denge ciktisi (DOK4 §4.2, AK-05, AK-07).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct InteriorEnvironmentEvaluation
    {
        public readonly bool IsRainSheltered;
        public readonly bool IsEnclosedInterior;
        public readonly float SealFactor;
        public readonly float WallWindPermeability01;
        public readonly float ShellThermalResistanceR;
        public readonly float ThermalInsulationBonusCelsius;
        public readonly float EquilibriumIndoorTempCelsius;
        public readonly float IndoorWindAttenuation01;
        public readonly int FloodFilledCellCount;
        public readonly int UnsealedWallCount;
        public readonly int OpenApertureCount;

        public InteriorEnvironmentEvaluation(
            bool isRainSheltered,
            bool isEnclosedInterior,
            float sealFactor,
            float wallWindPermeability01,
            float shellThermalResistanceR,
            float thermalInsulationBonusCelsius,
            float equilibriumIndoorTempCelsius,
            float indoorWindAttenuation01,
            int floodFilledCellCount,
            int unsealedWallCount,
            int openApertureCount)
        {
            IsRainSheltered = isRainSheltered;
            IsEnclosedInterior = isEnclosedInterior;
            SealFactor = sealFactor;
            WallWindPermeability01 = wallWindPermeability01;
            ShellThermalResistanceR = shellThermalResistanceR;
            ThermalInsulationBonusCelsius = thermalInsulationBonusCelsius;
            EquilibriumIndoorTempCelsius = equilibriumIndoorTempCelsius;
            IndoorWindAttenuation01 = indoorWindAttenuation01;
            FloodFilledCellCount = floodFilledCellCount;
            UnsealedWallCount = unsealedWallCount;
            OpenApertureCount = openApertureCount;
        }
    }

    /// <summary>
    /// Somine/ocak baca cekisi (Stack Draft), CO/duman birikimi ve 1.20m yangin mesafesi denetim sonucu (DOK4 §4.3-4.4, AK-15, T15).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct HearthAndChimneyEvaluation
    {
        public readonly bool HasValidChimneyFlue;
        public readonly float ChimneyHeightMeters;
        public readonly float DraftEfficiency01;
        public readonly float CoConcentrationPpm;
        public readonly float CoDangerRatio01;
        public readonly bool HasSmokeCoHazard;
        public readonly float SmokeLayerCeilingY;
        public readonly float EffectiveRoomTempCelsius;
        public readonly float MinCombustibleDistanceMeters;
        public readonly bool HasStoneFirewallShield;
        public readonly bool IsFireClearanceViolated;
        public readonly uint NearestThreatenedCombustibleNodeId;

        public HearthAndChimneyEvaluation(
            bool hasValidChimneyFlue,
            float chimneyHeightMeters,
            float draftEfficiency01,
            float coConcentrationPpm,
            float coDangerRatio01,
            bool hasSmokeCoHazard,
            float smokeLayerCeilingY,
            float effectiveRoomTempCelsius,
            float minCombustibleDistanceMeters,
            bool hasStoneFirewallShield,
            bool isFireClearanceViolated,
            uint nearestThreatenedCombustibleNodeId)
        {
            HasValidChimneyFlue = hasValidChimneyFlue;
            ChimneyHeightMeters = chimneyHeightMeters;
            DraftEfficiency01 = draftEfficiency01;
            CoConcentrationPpm = coConcentrationPpm;
            CoDangerRatio01 = coDangerRatio01;
            HasSmokeCoHazard = hasSmokeCoHazard;
            SmokeLayerCeilingY = smokeLayerCeilingY;
            EffectiveRoomTempCelsius = effectiveRoomTempCelsius;
            MinCombustibleDistanceMeters = minCombustibleDistanceMeters;
            HasStoneFirewallShield = hasStoneFirewallShield;
            IsFireClearanceViolated = isFireClearanceViolated;
            NearestThreatenedCombustibleNodeId = nearestThreatenedCombustibleNodeId;
        }
    }

    /// <summary>
    /// Eurocode 5 ahsap komurlesme (CharDepth0_15) kesit kaybi carpanlari (DOK4 §4.4, AK-15).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct Eurocode5CharringFactors
    {
        public readonly float CharDepthMeters;
        public readonly float RemainingWidthMeters;
        public readonly float RemainingHeightMeters;
        public readonly float AxialAreaFactor01;
        public readonly float BendingSectionModulusFactor01;

        public Eurocode5CharringFactors(
            float charDepthMeters,
            float remainingWidthMeters,
            float remainingHeightMeters,
            float axialAreaFactor01,
            float bendingSectionModulusFactor01)
        {
            CharDepthMeters = charDepthMeters;
            RemainingWidthMeters = remainingWidthMeters;
            RemainingHeightMeters = remainingHeightMeters;
            AxialAreaFactor01 = axialAreaFactor01;
            BendingSectionModulusFactor01 = bendingSectionModulusFactor01;
        }
    }

    /// <summary>
    /// Harc kurlenme egrisi (Karar A2), kar yuku (SWE), 0-GC 3B kapali ic mekan degerlendirmesi (Chinking &amp; SealFactor),
    /// somine baca/CO kontrolu, 1.20m yangin mesafesi ve Eurocode 5 ahsap komurlesme cozucusu.
    /// </summary>
    public static class MortarAndInteriorEnvironmentSolver
    {
        /// <summary>Yanici ahsap duvardan yuksek sicaklikli ocak/somine icin gereken minimum bosluk (m).</summary>
        public const float MinCombustibleWallClearanceMeters = 1.20f;

        /// <summary>Tas yangin duvari / yanmaz yaka (firewall) varken gereken bosluk (m).</summary>
        public const float StoneFirewallRequiredClearanceMeters = 0.0f;

        /// <summary>Kapali odada tehlikeli karbonmonoksit esigi (ppm).</summary>
        public const float DangerousCoThresholdPpm = 150.0f;

        /// <summary>Tam baca cekisi icin gereken minimum baca safti yuksekligi (m).</summary>
        public const float FullDraftChimneyHeightMeters = 1.80f;

        /// <summary>Eurocode 5 kademe basina komurlesme derinligi (12 mm = 0.012 m, DOK4 §4.4).</summary>
        public const float CharDepthPerStepMeters = 0.012f;

        /// <summary>0-GC kapali mekan flood-fill maksimum hucre butcesi.</summary>
        public const int MaxInteriorFloodFillCells = 512;

        private const int GridSizeX = 24;
        private const int GridSizeY = 12;
        private const int GridSizeZ = 24;
        private const int GridVolume = GridSizeX * GridSizeY * GridSizeZ;

        private const byte CellEmpty = 0;
        private const byte CellSealedBarrier = 1;
        private const byte CellLeakyBarrier = 2;
        private const byte CellOpenAperture = 3;

        [ThreadStatic] private static byte[]? _tlsCellTypeGrid;
        [ThreadStatic] private static float[]? _tlsCellSealGrid;
        [ThreadStatic] private static float[]? _tlsCellRValueGrid;
        [ThreadStatic] private static byte[]? _tlsVisitedGrid;
        [ThreadStatic] private static int[]? _tlsBfsQueue;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            ResetThreadStaticScratch();
        }
#endif

        public static void ResetThreadStaticScratch()
        {
            _tlsCellTypeGrid = null;
            _tlsCellSealGrid = null;
            _tlsCellRValueGrid = null;
            _tlsVisitedGrid = null;
            _tlsBfsQueue = null;
        }

        /// <summary>
        /// Karar A2 ve Bolum 2.3 uyarinca tekil dugum/eklem harc/beton/toprak kurlenmesini ilerletir:
        /// Kirec 240s (0.45) / 600s (1.0), Kil 120s (0.45) / 300s (1.0), Horasan 180s (0.45) / 450s (1.0),
        /// Cob 90s (0.45) / 180s (1.0), RammedEarth 0s (tokmak sonrasi) / 240s (1.0),
        /// PortlandConcrete 90s (yan kalip 0.225) / 300s (ana dikme 0.75) / 600s (1.0).
        /// [F4] Yaratici modda aninda 1.0 doner; acik yagmur altinda taze kirec/kil/cob yavaslar/yikanir, Horasan ve Beton suya dayaniklidir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float StepMortarCuring(
            MortarType mortar,
            float currentCureRatio01,
            float deltaTimeSeconds,
            float rainIntensity01 = 0f,
            bool isRainSheltered = true,
            float ambientTempCelsius = 20f,
            bool isCreativeMode = false)
        {
            return StepMortarCuringDetailed(
                mortar,
                currentCureRatio01,
                deltaTimeSeconds,
                rainIntensity01,
                isRainSheltered,
                ambientTempCelsius,
                isCreativeMode,
                out _,
                out _);
        }

        public static float StepMortarCuringDetailed(
            MortarType mortar,
            float currentCureRatio01,
            float deltaTimeSeconds,
            float rainIntensity01,
            bool isRainSheltered,
            float ambientTempCelsius,
            bool isCreativeMode,
            out bool rainWashedDegraded,
            out bool frostCracked)
        {
            rainWashedDegraded = false;
            frostCracked = false;

            if (mortar == MortarType.None || isCreativeMode)
            {
                return 1.0f;
            }

            float clampedCure = System.Math.Clamp(currentCureRatio01, 0f, 1f);
            if (deltaTimeSeconds <= 0f)
            {
                return clampedCure;
            }

            float rain = isRainSheltered ? 0f : System.Math.Clamp(rainIntensity01, 0f, 1f);

            // Don kontrolu (< -3 C taze islak harc/beton/camur)
            float frostVulnerabilityThreshold = mortar == MortarType.PortlandConcrete
                ? MortarConstants.WetCureThreshold
                : MathF.Max(MortarConstants.WetCureThreshold, MortarConstants.GetLoadBearingCureThreshold(mortar));
            float tempMultiplier = 1.0f;
            if (ambientTempCelsius < -3.0f && clampedCure < frostVulnerabilityThreshold)
            {
                frostCracked = mortar != MortarType.Horasan;
                tempMultiplier = mortar == MortarType.Horasan ? 0.40f : 0.05f;
            }
            else if (ambientTempCelsius < 5.0f)
            {
                float t01 = System.Math.Clamp((ambientTempCelsius + 3.0f) / 8.0f, 0.15f, 1.0f);
                tempMultiplier = t01;
            }

            // Yagmur islanma yavaslatmasi ve taze kil/cob/kirec yikanmasi vs suya dayanikli Horasan/PortlandConcrete
            float rainMultiplier = 1.0f;
            float washoutLoss = 0f;
            if (rain > 0f)
            {
                switch (mortar)
                {
                    case MortarType.Clay:
                        rainMultiplier = System.Math.Clamp(1.0f - (0.90f * rain), 0.05f, 1.0f);
                        if (rain >= 0.35f && clampedCure < 0.85f)
                        {
                            washoutLoss = 0.0015f * rain * deltaTimeSeconds;
                            rainWashedDegraded = true;
                        }
                        break;

                    case MortarType.CobMud:
                        rainMultiplier = System.Math.Clamp(1.0f - (0.92f * rain), 0.05f, 1.0f);
                        if (rain >= 0.30f && clampedCure < 0.90f)
                        {
                            washoutLoss = 0.0020f * rain * deltaTimeSeconds;
                            rainWashedDegraded = true;
                        }
                        break;

                    case MortarType.Lime:
                        if (clampedCure < MortarConstants.WetCureThreshold)
                        {
                            rainMultiplier = System.Math.Clamp(1.0f - (0.82f * rain), 0.10f, 1.0f);
                            if (rain >= 0.55f && clampedCure < MortarConstants.WetCureThreshold)
                            {
                                washoutLoss = rain >= 0.70f ? (0.0008f * rain * deltaTimeSeconds) : 0f;
                                rainWashedDegraded = true;
                            }
                        }
                        else if (clampedCure < 1.0f)
                        {
                            rainMultiplier = System.Math.Clamp(1.0f - (0.35f * rain), 0.50f, 1.0f);
                        }
                        break;

                    case MortarType.RammedEarthCompact:
                        rainMultiplier = System.Math.Clamp(1.0f - (0.70f * rain), 0.15f, 1.0f);
                        if (rain >= 0.65f && clampedCure < 0.50f)
                        {
                            washoutLoss = 0.0006f * rain * deltaTimeSeconds;
                            rainWashedDegraded = true;
                        }
                        break;

                    case MortarType.Horasan:
                    case MortarType.PortlandConcrete:
                        // Puzolanik Horasan harci ve hidrolik Portland betonu yagmurda yikanmaz, tam hizda priz alir
                        rainMultiplier = 1.0f;
                        break;
                }
            }

            float effectiveDt = deltaTimeSeconds * tempMultiplier * rainMultiplier;
            float stepped = MortarConstants.StepCureRatio(mortar, clampedCure, effectiveDt, isCreativeMode: false);
            if (washoutLoss > 0f)
            {
                stepped = MathF.Max(0f, stepped - washoutLoss);
            }

            return System.Math.Clamp(stepped, 0f, 1f);
        }

        /// <summary>
        /// Yapidaki tum dugum ve eklemlerin harc kurlenme oranlarini 0-GC olarak ilerletir.
        /// Hala tam sertlesmemis (CureRatio01 &lt; 1.0) aktif dugum sayisini doner.
        /// </summary>
        public static int StepMortarCuring(
            Span<StructuralNode64> nodes,
            Span<StructuralJointEdge32> edges,
            float deltaTimeSeconds,
            float rainIntensity01 = 0f,
            float ambientTempCelsius = 20f,
            bool isCreativeMode = false)
        {
            int uncuredCount = 0;
            float clampedRain = System.Math.Clamp(rainIntensity01, 0f, 1f);

            for (int i = 0; i < nodes.Length; i++)
            {
                ref StructuralNode64 node = ref nodes[i];
                if (IsNodeFailed(in node))
                {
                    continue;
                }

                if (node.Mortar == MortarType.None)
                {
                    node.CureRatio01 = 1.0f;
                    if (clampedRain <= 0f || node.Anchor == AnchorType.NoneFloating)
                    {
                        continue;
                    }
                }

                bool sheltered = clampedRain <= 0f || HasOverheadRoofNode(nodes, i);
                if (!sheltered && clampedRain > 0f && deltaTimeSeconds > 0f)
                {
                    int addWet = (int)MathF.Ceiling(clampedRain * MathF.Min(4f, deltaTimeSeconds * 0.1f));
                    node.Wetness0_15 = (byte)System.Math.Clamp(node.Wetness0_15 + addWet, 0, 15);
                }
                else if (deltaTimeSeconds > 0f && node.Wetness0_15 > 0 && (sheltered || clampedRain <= 0f))
                {
                    int dryStep = deltaTimeSeconds >= 30f ? (int)(deltaTimeSeconds / 30f) : 0;
                    node.Wetness0_15 = (byte)System.Math.Max(0, node.Wetness0_15 - dryStep);
                }

                if (node.Mortar == MortarType.None)
                {
                    continue;
                }

                float nextCure = StepMortarCuringDetailed(
                    node.Mortar,
                    node.CureRatio01,
                    deltaTimeSeconds,
                    clampedRain,
                    sheltered,
                    ambientTempCelsius,
                    isCreativeMode,
                    out bool rainWashed,
                    out _);

                node.CureRatio01 = nextCure;
                if (nextCure >= 0.9999f)
                {
                    node.IsCompactedOrCured = true;
                }
                if (rainWashed)
                {
                    node.Chinking |= ChinkingFlags.RainWashedDamaged;
                }

                if (nextCure < 0.9999f)
                {
                    uncuredCount++;
                }
            }

            // Eklemlerdeki MortarCureByte ve EffectiveStiffness01 senkronizasyonu
            for (int e = 0; e < edges.Length; e++)
            {
                ref StructuralJointEdge32 edge = ref edges[e];
                if (TryResolveJointMortarType(edge.Kind, out MortarType edgeMortar))
                {
                    float cur01 = isCreativeMode ? 1.0f : (edge.MortarCureByte / 255f);
                    float next01 = StepMortarCuringDetailed(
                        edgeMortar,
                        cur01,
                        deltaTimeSeconds,
                        clampedRain,
                        isRainSheltered: clampedRain <= 0f,
                        ambientTempCelsius: ambientTempCelsius,
                        isCreativeMode: isCreativeMode,
                        out bool washed,
                        out _);

                    edge.MortarCureByte = (byte)System.Math.Clamp((int)MathF.Round(next01 * 255f), 0, 255);
                    edge.EffectiveStiffness01 = MortarConstants.EvaluateMortarStrengthFactor(edgeMortar, next01);
                    if (washed)
                    {
                        edge.Flags |= StructuralJointFlags.RainWashedDegraded;
                    }
                }
                else if (isCreativeMode)
                {
                    edge.MortarCureByte = 255;
                }
            }

            return uncuredCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryResolveJointMortarType(JointConnectionKind kind, out MortarType mortar)
        {
            switch (kind)
            {
                case JointConnectionKind.ClayMortar:
                    mortar = MortarType.Clay;
                    return true;
                case JointConnectionKind.LimeMortar:
                    mortar = MortarType.Lime;
                    return true;
                case JointConnectionKind.HorasanMortar:
                    mortar = MortarType.Horasan;
                    return true;
                case JointConnectionKind.CobMudBond:
                    mortar = MortarType.CobMud;
                    return true;
                case JointConnectionKind.CompactedEarthLift:
                    mortar = MortarType.RammedEarthCompact;
                    return true;
                case JointConnectionKind.ConcreteMonolithicPour:
                    mortar = MortarType.PortlandConcrete;
                    return true;
                default:
                    mortar = MortarType.None;
                    return false;
            }
        }

        /// <summary>
        /// DOK4 §4.1.B Eurocode 1 uyumlu cati egim acisi kar tutma sekil katsayisini [0..1] hesaplar.
        /// 0-20° -&gt; 1.00, 45° -&gt; 0.45, &gt;=60° -&gt; 0.00.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeSlopeSnowShapeFactor(float roofPitchDegrees)
        {
            float alpha = MathF.Abs(roofPitchDegrees);
            if (alpha <= 20.0f)
            {
                return 1.0f;
            }

            if (alpha <= 45.0f)
            {
                return 1.0f - (0.55f * ((alpha - 20.0f) / 25.0f));
            }

            if (alpha < 60.0f)
            {
                return 0.45f * ((60.0f - alpha) / 15.0f);
            }

            return 0.0f;
        }

        /// <summary>
        /// Chunk SweUnits (1 birim = 2.0 kg/m^2 SWE) ve cati egim acisindan etkin kar basincini (kPa = kN/m^2) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeRoofSnowLoadKpa(byte sweUnits, float roofPitchDegrees, float indoorTempCelsius = 0f)
        {
            float sweKgPerM2 = sweUnits * 2.0f;
            if (indoorTempCelsius > 10.0f)
            {
                // Sicak cati altindan kismi erime indirimi
                float meltFactor = System.Math.Clamp(1.0f - ((indoorTempCelsius - 10.0f) * 0.015f), 0.55f, 1.0f);
                sweKgPerM2 *= meltFactor;
            }

            float mu = ComputeSlopeSnowShapeFactor(roofPitchDegrees);
            return sweKgPerM2 * 9.81f * 0.001f * mu;
        }

        /// <summary>
        /// AK-14 / T14: Kar Su Esdegeri (SWE), cati egimi (0° vs 45°) ve mertek sikligina (rafterSpacingMeters) gore
        /// cati egilme zorlanma oranini hesaplar.
        /// </summary>
        public static RoofSnowLoadEvaluation EvaluateRoofSnowLoad(
            byte sweUnits,
            float roofPitchDegrees,
            float rafterSpacingMeters,
            float rafterSpanMeters,
            byte rafterMaterialId = MaterialIdCatalog.OakWood,
            ArchitecturalSchool school = ArchitecturalSchool.LogCabin,
            StructureThicknessProfile thickness = StructureThicknessProfile.Standard,
            float indoorTempCelsius = 0f)
        {
            float sweKgM2 = sweUnits * 2.0f;
            float mu = ComputeSlopeSnowShapeFactor(roofPitchDegrees);
            float snowKpa = ComputeRoofSnowLoadKpa(sweUnits, roofPitchDegrees, indoorTempCelsius);

            float safeSpacing = MathF.Max(0.20f, rafterSpacingMeters);
            float safeSpan = MathF.Max(0.50f, rafterSpanMeters);

            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(rafterMaterialId, school);

            // Standart cati mertegi kesiti: b = 0.12m, h = 0.18m (W = b*h^2/6 = 0.000648 m^3)
            float sectionScale = thickness switch
            {
                StructureThicknessProfile.Slender => 0.65f,
                StructureThicknessProfile.HeavyBearing => 1.45f,
                _ => 1.00f
            };

            float widthB = 0.12f * sectionScale;
            float heightH = 0.18f * sectionScale;
            float areaM2 = widthB * heightH;
            float sectionModulusW = (widthB * heightH * heightH) / 6.0f;

            // Mertek + kaplama oz agirligi (kN/m)
            float selfDeadLoadKnPerM = (prof.DensityKgPerM3 * 9.81f * areaM2 * 0.001f) + (0.18f * safeSpacing);
            float snowLineLoadKnPerM = snowKpa * safeSpacing;
            float totalLineLoadKnPerM = selfDeadLoadKnPerM + snowLineLoadKnPerM;

            float mMaxKnM = (totalLineLoadKnPerM * safeSpan * safeSpan) / 8.0f;
            // MPa -> kN/m^2 = * 1000
            float allowableTensileMpa = MathF.Max(0.05f, prof.TensionStrengthMpa);
            float mCapKnM = allowableTensileMpa * 1000.0f * sectionModulusW;

            float utilization = mMaxKnM / MathF.Max(0.01f, mCapKnM);
            return new RoofSnowLoadEvaluation(
                sweKgPerM2: sweKgM2,
                slopeShapeFactorMu: mu,
                enabledSnowKpa(snowKpa),
                rafterLineLoadKnPerM: totalLineLoadKnPerM,
                bendingMomentKnM: mMaxKnM,
                momentCapacityKnM: mCapKnM,
                utilizationRatio: utilization,
                isOverloaded: utilization >= 1.0f);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static float enabledSnowKpa(float v) => MathF.Max(0f, v);
        }

        /// <summary>
        /// AK-05 &amp; AK-07 (T05, T07): 0-GC 3B kabuk flood-fill ile bir ic mekan noktasinin yagmur korumasi (IsRainSheltered),
        /// tam kapali ic mekan durumu (IsEnclosedInterior), sizdirmazlik katsayisi (SealFactor), duvar ruzgar gecirgenligi
        /// ve denge sicakligini hesaplar.
        /// </summary>
        public static InteriorEnvironmentEvaluation EvaluateInteriorEnvironment(
            ReadOnlySpan<StructuralNode64> nodes,
            Vector3 probePosition,
            float ambientTempCelsius = 0f,
            float activeHeatingPowerKw = 0f,
            float heatingEfficiency01 = 1.0f)
        {
            if (nodes.Length == 0)
            {
                return new InteriorEnvironmentEvaluation(
                    isRainSheltered: false,
                    isEnclosedInterior: false,
                    sealFactor: 0f,
                    wallWindPermeability01: 1f,
                    shellThermalResistanceR: 0f,
                    thermalInsulationBonusCelsius: 0f,
                    equilibriumIndoorTempCelsius: ambientTempCelsius,
                    indoorWindAttenuation01: 1f,
                    floodFilledCellCount: 0,
                    unsealedWallCount: 0,
                    openApertureCount: 0);
            }

            EnsureInteriorScratch(
                out byte[] cellType,
                out float[] cellSeal,
                out float[] cellRVal,
                out byte[] visited,
                out int[] queue);

            Array.Clear(cellType, 0, GridVolume);
            Array.Clear(cellSeal, 0, GridVolume);
            Array.Clear(cellRVal, 0, GridVolume);
            Array.Clear(visited, 0, GridVolume);

            int originX = (int)MathF.Floor(probePosition.X) - (GridSizeX / 2);
            int originY = (int)MathF.Floor(probePosition.Y) - 2;
            int originZ = (int)MathF.Floor(probePosition.Z) - (GridSizeZ / 2);

            bool hasOverheadCover = false;

            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 node = ref nodes[i];
                if (MathF.Abs(node.PosX - probePosition.X) > 16.0f ||
                    MathF.Abs(node.PosZ - probePosition.Z) > 16.0f ||
                    IsNodeFailed(in node))
                {
                    continue;
                }

                // 1. Cati / Ust ortu yagmur korumasi denetimi
                if (IsRoofOrOverheadCoverRole(node.Role))
                {
                    float dy = node.PosY - probePosition.Y;
                    if (dy >= 0.4f && dy <= 12.0f)
                    {
                        GetElementHalfExtents(in node, out float halfLen, out float halfThick, out _);
                        float coverRadiusX = MathF.Max(halfLen, halfThick) + 1.15f;
                        float coverRadiusZ = MathF.Max(halfLen, halfThick) + 1.15f;
                        if (MathF.Abs(node.PosX - probePosition.X) <= coverRadiusX &&
                            MathF.Abs(node.PosZ - probePosition.Z) <= coverRadiusZ)
                        {
                            hasOverheadCover = true;
                        }
                    }
                }

                // 2. Duvar, dolgu, kapi ve pencere kabuk elemanlarinin 3B hucre izgarasina damgalanmasi
                if (!ClassifyWallShellElement(in node, out byte barrierKind, out float segSeal, out float segRVal))
                {
                    continue;
                }

                StampShellNodeToGrid(
                    in node,
                    barrierKind,
                    segSeal,
                    segRVal,
                    originX,
                    originY,
                    originZ,
                    cellType,
                    cellSeal,
                    cellRVal);
            }

            if (!hasOverheadCover)
            {
                return new InteriorEnvironmentEvaluation(
                    isRainSheltered: false,
                    isEnclosedInterior: false,
                    sealFactor: 0f,
                    wallWindPermeability01: 1f,
                    shellThermalResistanceR: 0f,
                    thermalInsulationBonusCelsius: 0f,
                    equilibriumIndoorTempCelsius: ambientTempCelsius,
                    indoorWindAttenuation01: 1f,
                    floodFilledCellCount: 0,
                    unsealedWallCount: 0,
                    openApertureCount: 0);
            }

            // 3. Probe noktasindan 2D/3D oda hacmi BFS (yatay kabuk kapanisi)
            int startGx = System.Math.Clamp((int)MathF.Floor(probePosition.X) - originX, 1, GridSizeX - 2);
            int startGy = System.Math.Clamp((int)MathF.Floor(probePosition.Y + 0.5f) - originY, 1, GridSizeY - 2);
            int startGz = System.Math.Clamp((int)MathF.Floor(probePosition.Z) - originZ, 1, GridSizeZ - 2);

            int startIdx = ToGridIndex(startGx, startGy, startGz);
            // Eger probe tam bir duvar hucresi kenarina denk geldiyse en yakin bos komsuyu sec
            if (cellType[startIdx] != CellEmpty)
            {
                for (int dx = -1; dx <= 1 && cellType[startIdx] != CellEmpty; dx++)
                {
                    for (int dz = -1; dz <= 1 && cellType[startIdx] != CellEmpty; dz++)
                    {
                        int nx = System.Math.Clamp(startGx + dx, 1, GridSizeX - 2);
                        int nz = System.Math.Clamp(startGz + dz, 1, GridSizeZ - 2);
                        int candidateIdx = ToGridIndex(nx, startGy, nz);
                        if (cellType[candidateIdx] == CellEmpty)
                        {
                            startGx = nx;
                            startGz = nz;
                            startIdx = candidateIdx;
                        }
                    }
                }
            }

            int head = 0;
            int tail = 0;
            queue[tail++] = startIdx;
            visited[startIdx] = 1;

            bool escapedToOpenExterior = false;
            int filledCells = 0;
            int touchedBoundaryCount = 0;
            int unsealedWallHits = 0;
            int openApertureHits = 0;
            float sumSeal = 0f;
            float sumRValue = 0f;

            ReadOnlySpan<int> stepDx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> stepDz = stackalloc int[4] { 0, 0, 1, -1 };

            while (head < tail && filledCells < MaxInteriorFloodFillCells)
            {
                int cur = queue[head++];
                filledCells++;

                FromGridIndex(cur, out int cx, out int cy, out int cz);
                if (cx <= 0 || cx >= GridSizeX - 1 || cz <= 0 || cz >= GridSizeZ - 1)
                {
                    escapedToOpenExterior = true;
                    break;
                }

                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = cx + stepDx[dir];
                    int nz = cz + stepDz[dir];
                    int nIdx = ToGridIndex(nx, cy, nz);

                    byte bKind = cellType[nIdx];
                    if (bKind != CellEmpty)
                    {
                        touchedBoundaryCount++;
                        sumSeal += cellSeal[nIdx];
                        sumRValue += cellRVal[nIdx];
                        if (bKind == CellLeakyBarrier)
                        {
                            unsealedWallHits++;
                        }
                        else if (bKind == CellOpenAperture)
                        {
                            openApertureHits++;
                        }

                        continue;
                    }

                    if (visited[nIdx] == 0)
                    {
                        visited[nIdx] = 1;
                        if (tail < queue.Length)
                        {
                            queue[tail++] = nIdx;
                        }
                    }
                }
            }

            if (filledCells >= MaxInteriorFloodFillCells || touchedBoundaryCount == 0)
            {
                escapedToOpenExterior = true;
            }

            // AK-05 & AK-07: Acik cephe, Chinking yapilmamis kutuk duvar veya acik kapi/kepenk varsa
            // IsRainSheltered = true kalir, ancak IsEnclosedInterior = false, SealFactor = 0.0 ve yalitim bonusu = 0.0 olur.
            if (escapedToOpenExterior || unsealedWallHits > 0 || openApertureHits > 0)
            {
                float windPerm = escapedToOpenExterior
                    ? 1.0f
                    : System.Math.Clamp((unsealedWallHits + openApertureHits) / (float)System.Math.Max(1, touchedBoundaryCount), 0.15f, 1.0f);

                return new InteriorEnvironmentEvaluation(
                    isRainSheltered: true,
                    isEnclosedInterior: false,
                    sealFactor: 0.0f,
                    wallWindPermeability01: windPerm,
                    shellThermalResistanceR: 0.0f,
                    thermalInsulationBonusCelsius: 0.0f,
                    equilibriumIndoorTempCelsius: ambientTempCelsius,
                    indoorWindAttenuation01: System.Math.Clamp(windPerm, 0.25f, 1.0f),
                    floodFilledCellCount: filledCells,
                    unsealedWallCount: unsealedWallHits,
                    openApertureCount: openApertureHits);
            }

            float avgSeal = System.Math.Clamp(sumSeal / touchedBoundaryCount, 0f, 1f);
            bool isEnclosed = avgSeal >= MortarConstants.ChinkedSealThreshold;
            if (!isEnclosed)
            {
                return new InteriorEnvironmentEvaluation(
                    isRainSheltered: true,
                    isEnclosedInterior: false,
                    sealFactor: avgSeal,
                    wallWindPermeability01: 1.0f - avgSeal,
                    shellThermalResistanceR: 0.0f,
                    thermalInsulationBonusCelsius: 0.0f,
                    equilibriumIndoorTempCelsius: ambientTempCelsius,
                    indoorWindAttenuation01: 1.0f - avgSeal,
                    floodFilledCellCount: filledCells,
                    unsealedWallCount: unsealedWallHits,
                    openApertureCount: openApertureHits);
            }

            // DOK4 §4.2.B Denge Sicakligi Hesabi
            float avgR = MathF.Max(0.50f, sumRValue / touchedBoundaryCount);
            float passiveDeltaT = MathF.Min(9.5f, avgR * 3.95f * avgSeal);
            float uEffective = 1.0f / (avgR * (0.30f + (0.70f * avgSeal)));

            float floorAreaM2 = MathF.Max(6.0f, filledCells * 1.0f);
            float roomVolumeM3 = floorAreaM2 * 2.6f;
            float shellAreaM2 = (2.0f * floorAreaM2) + (4.0f * MathF.Sqrt(floorAreaM2) * 2.6f);
            float infiltrationAch = 0.25f + ((1.0f - avgSeal) * 2.0f);

            float cleanHeatingKw = MathF.Max(0f, activeHeatingPowerKw) * System.Math.Clamp(heatingEfficiency01, 0f, 1f);
            float denomWattsPerK = (uEffective * shellAreaM2) + (roomVolumeM3 * infiltrationAch * 0.34f);
            float activeDeltaT = denomWattsPerK > 0.01f ? (1000.0f * cleanHeatingKw) / denomWattsPerK : 0f;
            float totalBonusC = passiveDeltaT + MathF.Min(42.0f, activeDeltaT);

            return new InteriorEnvironmentEvaluation(
                isRainSheltered: true,
                isEnclosedInterior: true,
                sealFactor: avgSeal,
                wallWindPermeability01: 0.0f,
                shellThermalResistanceR: avgR,
                thermalInsulationBonusCelsius: totalBonusC,
                equilibriumIndoorTempCelsius: ambientTempCelsius + totalBonusC,
                indoorWindAttenuation01: 0.0f,
                floodFilledCellCount: filledCells,
                unsealedWallCount: 0,
                openApertureCount: 0);
        }

        /// <summary>
        /// AK-15 / T15 &amp; DOK4 §4.3-4.4: Somine/ocak baca cekisi (MasonryHearthChimney), CO/duman birikimi ve
        /// yanici ahsap duvardan 1.20m yangin mesafesini (tas yangin duvari varsa 0m) denetler.
        /// </summary>
        public static HearthAndChimneyEvaluation EvaluateHearthAndChimney(
            ReadOnlySpan<StructuralNode64> nodes,
            Vector3 hearthWorldPos,
            bool isFireLit,
            float burnDurationSeconds = 60f,
            float heatingPowerKw = 18f,
            float ambientTempCelsius = 0f)
        {
            InteriorEnvironmentEvaluation interior = EvaluateInteriorEnvironment(
                nodes,
                hearthWorldPos,
                ambientTempCelsius,
                activeHeatingPowerKw: 0f);

            // 1. Yanici ahsap duvardan 1.20m mesafe (tas yangin duvari / yanmaz yaka varsa 0m) kontrolu
            float minCombustibleDist = float.PositiveInfinity;
            bool shieldedByFirewall = false;
            bool clearanceViolated = false;
            uint threatenedNodeId = 0u;

            float maxRoofY = hearthWorldPos.Y + 2.4f;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 node = ref nodes[i];
                if (MathF.Abs(node.PosX - hearthWorldPos.X) > 16.0f ||
                    MathF.Abs(node.PosZ - hearthWorldPos.Z) > 16.0f ||
                    IsNodeFailed(in node))
                {
                    continue;
                }

                if (IsRoofOrOverheadCoverRole(node.Role) &&
                    MathF.Abs(node.PosX - hearthWorldPos.X) <= 4.0f &&
                    MathF.Abs(node.PosZ - hearthWorldPos.Z) <= 4.0f &&
                    node.PosY > maxRoofY)
                {
                    maxRoofY = node.PosY;
                }

                StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetProfile(node.MaterialId);
                if (!prof.IsCombustible)
                {
                    continue;
                }

                float dist = DistanceToNodeSurface(in node, hearthWorldPos);
                if (dist < minCombustibleDist)
                {
                    minCombustibleDist = dist;
                }

                if (dist < MinCombustibleWallClearanceMeters)
                {
                    bool hasFirewall = HasInterveningStoneFirewall(nodes, hearthWorldPos, in node);
                    if (hasFirewall)
                    {
                        shieldedByFirewall = true;
                    }
                    else
                    {
                        clearanceViolated = true;
                        if (threatenedNodeId == 0u)
                        {
                            threatenedNodeId = node.NodeId;
                        }
                    }
                }
            }

            // 2. MasonryHearthChimney baca safti surekliligi ve cati ustu cikisi denetimi (Bernoulli Stack Draft)
            bool hasChimneyBase = false;
            float highestConnectedFlueTopY = hearthWorldPos.Y;

            // Dikey siralama olmadan cok gecisli 0-GC zincir takibi (maks 16 baca modulu)
            Vector3 currentFlueAnchor = hearthWorldPos;
            for (int pass = 0; pass < 16; pass++)
            {
                bool advanced = false;
                for (int i = 0; i < nodes.Length; i++)
                {
                    ref readonly StructuralNode64 n = ref nodes[i];
                    if (IsNodeFailed(in n) || n.Role != StructuralElementRole.MasonryHearthChimney)
                    {
                        continue;
                    }

                    // 45° duman davlumbazi / maks 1.15m yanal sapma toleransi
                    float dx = MathF.Abs(n.PosX - currentFlueAnchor.X);
                    float dz = MathF.Abs(n.PosZ - currentFlueAnchor.Z);
                    if (dx > 1.15f || dz > 1.15f)
                    {
                        continue;
                    }

                    float moduleTopY = n.PosY + 1.0f;
                    float moduleBottomY = n.PosY - 1.0f;
                    if (moduleBottomY <= highestConnectedFlueTopY + 0.65f && moduleTopY > highestConnectedFlueTopY + 0.05f)
                    {
                        hasChimneyBase = true;
                        highestConnectedFlueTopY = moduleTopY;
                        currentFlueAnchor = new Vector3(n.PosX, moduleTopY, n.PosZ);
                        advanced = true;
                    }
                }

                if (!advanced)
                {
                    break;
                }
            }

            float chimneyHeight = hasChimneyBase ? MathF.Max(0f, highestConnectedFlueTopY - hearthWorldPos.Y) : 0f;
            bool clearsRoof = hasChimneyBase && (!interior.IsRainSheltered || highestConnectedFlueTopY >= maxRoofY - 0.15f);
            float draftEfficiency = clearsRoof
                ? System.Math.Clamp(chimneyHeight / FullDraftChimneyHeightMeters, 0f, 1f)
                : 0f;

            bool hasValidFlue = clearsRoof && draftEfficiency >= 0.85f;

            if (!isFireLit)
            {
                return new HearthAndChimneyEvaluation(
                    hasValidChimneyFlue: hasValidFlue,
                    chimneyHeightMeters: chimneyHeight,
                    draftEfficiency01: draftEfficiency,
                    coConcentrationPpm: 0f,
                    coDangerRatio01: 0f,
                    hasSmokeCoHazard: false,
                    smokeLayerCeilingY: maxRoofY,
                    effectiveRoomTempCelsius: interior.EquilibriumIndoorTempCelsius,
                    minCombustibleDistanceMeters: minCombustibleDist,
                    hasStoneFirewallShield: shieldedByFirewall,
                    isFireClearanceViolated: clearanceViolated,
                    nearestThreatenedCombustibleNodeId: threatenedNodeId);
            }

            // 3. Duman/CO birikimi vs temiz isinma
            if (hasValidFlue)
            {
                // Baca acik ve catiyi asiyor -> CO = 0, duman %100 gokyuzune tahliye edilir ve kapali oda guvenle isinir!
                InteriorEnvironmentEvaluation heated = EvaluateInteriorEnvironment(
                    nodes,
                    hearthWorldPos,
                    ambientTempCelsius,
                    activeHeatingPowerKw: heatingPowerKw,
                    heatingEfficiency01: draftEfficiency);

                return new HearthAndChimneyEvaluation(
                    hasValidChimneyFlue: true,
                    chimneyHeightMeters: chimneyHeight,
                    draftEfficiency01: 1.0f,
                    coConcentrationPpm: 0.0f,
                    coDangerRatio01: 0.0f,
                    hasSmokeCoHazard: false,
                    smokeLayerCeilingY: maxRoofY,
                    effectiveRoomTempCelsius: heated.EquilibriumIndoorTempCelsius,
                    minCombustibleDistanceMeters: minCombustibleDist,
                    hasStoneFirewallShield: shieldedByFirewall,
                    isFireClearanceViolated: clearanceViolated,
                    nearestThreatenedCombustibleNodeId: threatenedNodeId);
            }

            // Baca yok veya cati altinda kapali!
            if (interior.IsEnclosedInterior)
            {
                float safeBurnSec = MathF.Max(1.0f, burnDurationSeconds);
                float roomAreaM2 = MathF.Max(8.0f, interior.FloodFilledCellCount * 1.0f);
                float unventedFraction = 1.0f - draftEfficiency;
                float smokeVolumeFlowM3PerSec = 0.28f * (heatingPowerKw / 18.0f) * unventedFraction;
                float smokeDropMeters = (smokeVolumeFlowM3PerSec * safeBurnSec) / roomAreaM2;
                float smokeCeilingY = MathF.Max(hearthWorldPos.Y + 0.40f, maxRoofY - smokeDropMeters);

                float coPpm = (smokeVolumeFlowM3PerSec * safeBurnSec * 420.0f) / MathF.Max(12.0f, roomAreaM2 * 2.5f);
                float coRatio = coPpm / DangerousCoThresholdPpm;
                bool hasHazard = coRatio >= 1.0f || smokeCeilingY <= hearthWorldPos.Y + 1.65f;

                return new HearthAndChimneyEvaluation(
                    hasValidChimneyFlue: false,
                    chimneyHeightMeters: chimneyHeight,
                    draftEfficiency01: draftEfficiency,
                    coConcentrationPpm: coPpm,
                    coDangerRatio01: coRatio,
                    hasSmokeCoHazard: hasHazard,
                    smokeLayerCeilingY: smokeCeilingY,
                    effectiveRoomTempCelsius: interior.EquilibriumIndoorTempCelsius + 2.0f,
                    minCombustibleDistanceMeters: minCombustibleDist,
                    hasStoneFirewallShield: shieldedByFirewall,
                    isFireClearanceViolated: clearanceViolated,
                    nearestThreatenedCombustibleNodeId: threatenedNodeId);
            }

            // Acik hava veya cereyanli sundurma: duman dagilir ama kapali oda isinmasi da yoktur
            return new HearthAndChimneyEvaluation(
                hasValidChimneyFlue: false,
                chimneyHeightMeters: chimneyHeight,
                draftEfficiency01: draftEfficiency,
                coConcentrationPpm: 10.0f * (1.0f - interior.IndoorWindAttenuation01),
                coDangerRatio01: 0.05f,
                hasSmokeCoHazard: false,
                smokeLayerCeilingY: maxRoofY,
                effectiveRoomTempCelsius: ambientTempCelsius,
                minCombustibleDistanceMeters: minCombustibleDist,
                hasStoneFirewallShield: shieldedByFirewall,
                isFireClearanceViolated: clearanceViolated,
                nearestThreatenedCombustibleNodeId: threatenedNodeId);
        }

        /// <summary>
        /// DOK4 §4.4 Eurocode 5 ahsap komurlesme kesit kaybi carpanlarini (N_cap ve M_cap icin) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Eurocode5CharringFactors ComputeEurocode5CharringFactors(
            byte materialId,
            byte charDepth0_15,
            StructureThicknessProfile thickness = StructureThicknessProfile.Standard)
        {
            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetProfile(materialId);
            float baseDim = thickness switch
            {
                StructureThicknessProfile.Slender => 0.26f,
                StructureThicknessProfile.HeavyBearing => 0.48f,
                _ => 0.36f
            };

            if (!prof.IsCombustible || charDepth0_15 == 0)
            {
                return new Eurocode5CharringFactors(0f, baseDim, baseDim, 1.0f, 1.0f);
            }

            int clampedSteps = System.Math.Clamp((int)charDepth0_15, 0, 15);
            float dChar = clampedSteps * CharDepthPerStepMeters;
            float bRem = MathF.Max(0f, baseDim - (2.0f * dChar));
            float hRem = MathF.Max(0f, baseDim - (2.0f * dChar));

            float baseArea = baseDim * baseDim;
            float baseW = baseDim * baseDim * baseDim;
            float axialFactor = baseArea > 0f ? (bRem * hRem) / baseArea : 0f;
            float momentFactor = baseW > 0f ? (bRem * hRem * hRem) / baseW : 0f;

            return new Eurocode5CharringFactors(dChar, bRem, hRem, axialFactor, momentFactor);
        }

        /// <summary>
        /// Yangin maruziyet suresine gore yanici ahsap/saz elemanin CharDepth0_15 komurlesme kademesini ilerletir.
        /// Kademe degistiyse true doner (statik graf cozucusunu uyandirmak icin).
        /// </summary>
        public static bool StepWoodCharring(
            ref StructuralNode64 node,
            float fireExposureSeconds,
            float fireIntensity01 = 1.0f)
        {
            if (fireExposureSeconds <= 0f || fireIntensity01 <= 0f || IsNodeFailed(in node))
            {
                return false;
            }

            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetProfile(node.MaterialId);
            if (!prof.IsCombustible)
            {
                return false;
            }

            float wetnessDamping = 1.0f - (0.55f * (System.Math.Clamp((int)node.Wetness0_15, 0, 15) / 15.0f));
            // Oyun-dostu Eurocode 5 hiz olcegi: OakWood (beta=1.0) tam alevde ~8 sn/kademe (12mm)
            float charRateStepsPerSec = (prof.CombustibilityFactor * System.Math.Clamp(fireIntensity01, 0f, 3f) * wetnessDamping) / 8.0f;
            int deltaSteps = (int)MathF.Floor(fireExposureSeconds * charRateStepsPerSec);
            if (deltaSteps <= 0 && fireExposureSeconds * charRateStepsPerSec >= 0.5f)
            {
                deltaSteps = 1;
            }

            if (deltaSteps <= 0)
            {
                return false;
            }

            byte prev = node.CharDepth0_15;
            byte next = (byte)System.Math.Clamp(prev + deltaSteps, 0, 15);
            node.CharDepth0_15 = next;
            return next != prev;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNodeFailed(in StructuralNode64 node)
        {
            return node.Integrity01 <= 0f ||
                   node.CollapseState == StructureCollapseState.Collapsed ||
                   node.CollapseState == StructureCollapseState.DetachedInstantFall;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRoofOrOverheadCoverRole(StructuralElementRole role)
        {
            return role is StructuralElementRole.InclinedRoofRafter
                or StructuralElementRole.RoofCoveringCourse
                or StructuralElementRole.RoofPurlin
                or StructuralElementRole.RoofRidgeBeam
                or StructuralElementRole.FloorJoistDeck
                or StructuralElementRole.SlabDeckFormwork
                or StructuralElementRole.CorrugatedMetalDeck
                or StructuralElementRole.BarrelOrCrossVault
                or StructuralElementRole.DomeRingCourse
                or StructuralElementRole.DomeOculusCrown;
        }

        private static bool HasOverheadRoofNode(ReadOnlySpan<StructuralNode64> nodes, int targetIndex)
        {
            ref readonly StructuralNode64 target = ref nodes[targetIndex];
            if (IsRoofOrOverheadCoverRole(target.Role))
            {
                return true;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (i == targetIndex)
                {
                    continue;
                }

                ref readonly StructuralNode64 other = ref nodes[i];
                if (IsNodeFailed(in other) || !IsRoofOrOverheadCoverRole(other.Role))
                {
                    continue;
                }

                float dy = other.PosY - target.PosY;
                if (dy >= 0.25f &&
                    dy <= 14.0f &&
                    MathF.Abs(other.PosX - target.PosX) <= 3.2f &&
                    MathF.Abs(other.PosZ - target.PosZ) <= 3.2f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ClassifyWallShellElement(
            in StructuralNode64 node,
            out byte barrierKind,
            out float sealFactor,
            out float thermalR)
        {
            StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetEffectiveProfile(node.MaterialId, node.School);
            thermalR = prof.ThermalResistanceR;

            switch (node.Role)
            {
                case StructuralElementRole.NotchedWallLog:
                case StructuralElementRole.SillBeamOrLog:
                case StructuralElementRole.SaddleNotchedBaseLog:
                case StructuralElementRole.HalfFlatStarterLog:
                case StructuralElementRole.GroovedLogPost:
                {
                    bool hasChinking = node.MaterialId is MaterialIdCatalog.ChinkedLogBlock or MaterialIdCatalog.GroovedLogTimber ||
                                       (node.Chinking & (ChinkingFlags.MossStuffed |
                                                         ChinkingFlags.ClayDaubed |
                                                         ChinkingFlags.LimePlastered |
                                                         ChinkingFlags.WeatherSealed)) != 0;
                    bool rainRuined = (node.Chinking & ChinkingFlags.RainWashedDamaged) != 0 &&
                                      (node.Chinking & ChinkingFlags.WeatherSealed) == 0;

                    if (hasChinking && !rainRuined)
                    {
                        barrierKind = CellSealedBarrier;
                        sealFactor = (node.Chinking & (ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed)) != 0 ||
                                     node.MaterialId == MaterialIdCatalog.ChinkedLogBlock
                            ? 0.95f
                            : 0.85f;
                        thermalR = MathF.Max(thermalR, 2.05f);
                    }
                    else
                    {
                        barrierKind = CellLeakyBarrier;
                        sealFactor = 0.0f;
                        thermalR = 0.0f;
                    }

                    return true;
                }

                case StructuralElementRole.LogChinkingFill:
                case StructuralElementRole.InfillPanelWattleAdobe:
                case StructuralElementRole.PlywoodSheathingPanel:
                {
                    bool rainRuined = (node.Chinking & ChinkingFlags.RainWashedDamaged) != 0 &&
                                      (node.Chinking & ChinkingFlags.WeatherSealed) == 0;
                    if (rainRuined)
                    {
                        barrierKind = CellLeakyBarrier;
                        sealFactor = 0.0f;
                        thermalR = 0.0f;
                        return true;
                    }

                    barrierKind = CellSealedBarrier;
                    sealFactor = node.Role == StructuralElementRole.PlywoodSheathingPanel
                        ? 0.92f
                        : (node.MaterialId is MaterialIdCatalog.AdobeBrickBlock or MaterialIdCatalog.WattleDaubPanelBlock ? 0.88f : 0.82f);
                    thermalR = MathF.Max(thermalR, 1.55f);
                    return true;
                }

                case StructuralElementRole.PlatformStudWall:
                {
                    if (node.MaterialId == MaterialIdCatalog.PlywoodOsbPanel ||
                        (node.Chinking & (ChinkingFlags.ClayDaubed | ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed)) != 0)
                    {
                        barrierKind = CellSealedBarrier;
                        sealFactor = 0.90f;
                        thermalR = MathF.Max(thermalR, 1.50f);
                    }
                    else
                    {
                        barrierKind = CellLeakyBarrier;
                        sealFactor = 0.0f;
                        thermalR = 0.0f;
                    }

                    return true;
                }

                case StructuralElementRole.RubbleMasonryCourse:
                case StructuralElementRole.AshlarMasonryWall:
                case StructuralElementRole.EnglishBondBrickWall:
                case StructuralElementRole.RammedEarthWall:
                case StructuralElementRole.CobCurvedWall:
                case StructuralElementRole.RetainingWallCourse:
                case StructuralElementRole.ExteriorButtressPier:
                case StructuralElementRole.MasonryHearthChimney:
                case StructuralElementRole.RawCrystalMassSegment:
                {
                    bool rainRuinedEarth = node.Role is StructuralElementRole.CobCurvedWall or StructuralElementRole.RammedEarthWall &&
                                           (node.Chinking & ChinkingFlags.RainWashedDamaged) != 0 &&
                                           node.CureRatio01 < MortarConstants.WetCureThreshold;
                    bool sealedMasonry = !rainRuinedEarth &&
                                         (node.Mortar != MortarType.None ||
                                          node.Role is StructuralElementRole.RammedEarthWall or StructuralElementRole.CobCurvedWall or StructuralElementRole.EnglishBondBrickWall ||
                                          node.MaterialId is MaterialIdCatalog.LimeMortarStoneWall
                                              or MaterialIdCatalog.CutAshlarStone
                                              or MaterialIdCatalog.DressedMarbleBlock
                                              or MaterialIdCatalog.FiredClayBrick
                                              or MaterialIdCatalog.EnglishBondBrickMasonry
                                              or MaterialIdCatalog.CobMudBrick
                                              or MaterialIdCatalog.RammedEarthCompacted
                                              or MaterialIdCatalog.DriedRammedEarth
                                              or MaterialIdCatalog.CuredReinforcedConcrete
                                              or MaterialIdCatalog.StoneBrick
                                              or MaterialIdCatalog.AdobeBrickBlock ||
                                          (node.Chinking & (ChinkingFlags.ClayDaubed | ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed)) != 0);

                    if (sealedMasonry)
                    {
                        barrierKind = CellSealedBarrier;
                        sealFactor = node.Mortar == MortarType.None ? 0.85f : 0.98f;
                        thermalR = MathF.Max(thermalR, 1.75f);
                    }
                    else
                    {
                        // Harcsiz kuru tas orgu veya yagmurda erimis taze toprak ruzgar sizdirir
                        barrierKind = CellLeakyBarrier;
                        sealFactor = 0.0f;
                        thermalR = 0.0f;
                    }

                    return true;
                }

                case StructuralElementRole.DoorFrameOpening:
                case StructuralElementRole.WindowFrameOpening:
                {
                    if (node.MaterialId == MaterialIdCatalog.IronGrating || node.IsDoorOrWindowOpen)
                    {
                        barrierKind = CellOpenAperture;
                        sealFactor = 0.0f;
                        thermalR = 0.0f;
                    }
                    else
                    {
                        barrierKind = CellSealedBarrier;
                        sealFactor = node.MaterialId == MaterialIdCatalog.LeadedGlassPane ? 1.00f : 0.90f;
                        thermalR = MathF.Max(thermalR, 1.25f);
                    }

                    return true;
                }

                default:
                    barrierKind = CellEmpty;
                    sealFactor = 0f;
                    thermalR = 0f;
                    return false;
            }
        }

        private static void GetElementHalfExtents(
            in StructuralNode64 node,
            out float halfSpanX,
            out float halfSpanZ,
            out float halfHeightY)
        {
            float lengthAlongAxis = node.Role switch
            {
                StructuralElementRole.MasonryHearthChimney => 1.15f,
                StructuralElementRole.RoofCoveringCourse or StructuralElementRole.InclinedRoofRafter or
                StructuralElementRole.FloorJoistDeck or StructuralElementRole.SlabDeckFormwork or
                StructuralElementRole.CorrugatedMetalDeck or StructuralElementRole.BarrelOrCrossVault or
                StructuralElementRole.DomeRingCourse or StructuralElementRole.DomeOculusCrown => 2.60f,
                _ => 2.20f
            };

            float thickNormal = node.Role switch
            {
                StructuralElementRole.RoofCoveringCourse or StructuralElementRole.InclinedRoofRafter or
                StructuralElementRole.FloorJoistDeck or StructuralElementRole.SlabDeckFormwork or
                StructuralElementRole.CorrugatedMetalDeck or StructuralElementRole.BarrelOrCrossVault or
                StructuralElementRole.DomeRingCourse or StructuralElementRole.DomeOculusCrown => 2.60f,
                _ => 0.70f
            };

            bool rotated90 = (node.YawQuarterTurns & 1) != 0;
            halfSpanX = rotated90 ? thickNormal : lengthAlongAxis;
            halfSpanZ = rotated90 ? lengthAlongAxis : thickNormal;
            halfHeightY = 1.65f;
        }

        private static void StampShellNodeToGrid(
            in StructuralNode64 node,
            byte barrierKind,
            float segSeal,
            float segRVal,
            int originX,
            int originY,
            int originZ,
            byte[] cellType,
            float[] cellSeal,
            float[] cellRVal)
        {
            GetElementHalfExtents(in node, out float halfX, out float halfZ, out float halfY);

            int minGx = System.Math.Max(0, (int)MathF.Floor(node.PosX - halfX) - originX);
            int maxGx = System.Math.Min(GridSizeX - 1, (int)MathF.Floor(node.PosX + halfX) - originX);
            int minGy = System.Math.Max(0, (int)MathF.Floor(node.PosY - halfY) - originY);
            int maxGy = System.Math.Min(GridSizeY - 1, (int)MathF.Floor(node.PosY + halfY) - originY);
            int minGz = System.Math.Max(0, (int)MathF.Floor(node.PosZ - halfZ) - originZ);
            int maxGz = System.Math.Min(GridSizeZ - 1, (int)MathF.Floor(node.PosZ + halfZ) - originZ);

            for (int gy = minGy; gy <= maxGy; gy++)
            {
                for (int gz = minGz; gz <= maxGz; gz++)
                {
                    for (int gx = minGx; gx <= maxGx; gx++)
                    {
                        int idx = ToGridIndex(gx, gy, gz);
                        byte existing = cellType[idx];
                        // OpenAperture (3) ve LeakyBarrier (2) sizinti acisindan SealedBarrier (1)'i ezer;
                        // ancak kapi/pencere kasasi etrafindaki acik/kapali kapi kendi hucresini belirler.
                        if (existing == CellEmpty ||
                            (barrierKind == CellOpenAperture && node.Role is StructuralElementRole.DoorFrameOpening or StructuralElementRole.WindowFrameOpening) ||
                            (existing == CellSealedBarrier && barrierKind == CellLeakyBarrier && segSeal <= 0f))
                        {
                            cellType[idx] = barrierKind;
                            cellSeal[idx] = segSeal;
                            cellRVal[idx] = segRVal;
                        }
                    }
                }
            }
        }

        private static float DistanceToNodeSurface(in StructuralNode64 node, Vector3 point)
        {
            bool rotated90 = (node.YawQuarterTurns & 1) != 0;
            float halfX = rotated90 ? 0.22f : 1.00f;
            float halfZ = rotated90 ? 1.00f : 0.22f;
            float halfY = 1.00f;

            float dx = MathF.Max(0f, MathF.Abs(point.X - node.PosX) - halfX);
            float dy = MathF.Max(0f, MathF.Abs(point.Y - node.PosY) - halfY);
            float dz = MathF.Max(0f, MathF.Abs(point.Z - node.PosZ) - halfZ);
            return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static bool HasInterveningStoneFirewall(
            ReadOnlySpan<StructuralNode64> nodes,
            Vector3 hearthPos,
            in StructuralNode64 targetCombustible)
        {
            Vector3 targetPos = targetCombustible.PositionNumerics;
            Vector3 ab = targetPos - hearthPos;
            float lenSq = ab.LengthSquared();
            if (lenSq <= 1e-5f)
            {
                return false;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 candidate = ref nodes[i];
                if (candidate.NodeId == targetCombustible.NodeId || IsNodeFailed(in candidate))
                {
                    continue;
                }

                StructureMaterialMechanicsProfile prof = StructureMaterialCatalog.GetProfile(candidate.MaterialId);
                if (prof.IsCombustible || candidate.MaterialId == MaterialIdCatalog.Air)
                {
                    continue;
                }

                bool isFirewallCapable = candidate.Role is StructuralElementRole.RubbleMasonryCourse
                    or StructuralElementRole.AshlarMasonryWall
                    or StructuralElementRole.EnglishBondBrickWall
                    or StructuralElementRole.RammedEarthWall
                    or StructuralElementRole.CobCurvedWall
                    or StructuralElementRole.MasonryHearthChimney
                    or StructuralElementRole.StonePlinthFoundation
                    or StructuralElementRole.ExteriorButtressPier
                    or StructuralElementRole.RetainingWallCourse;
                if (!isFirewallCapable)
                {
                    continue;
                }

                // Ocak ile ahsap duvar arasindaki dogru parcasina dik uzaklik kontrolu
                Vector3 ac = candidate.PositionNumerics - hearthPos;
                float t = System.Math.Clamp(Vector3.Dot(ac, ab) / lenSq, 0f, 1f);
                Vector3 closest = hearthPos + (ab * t);
                if (DistanceToNodeSurface(in candidate, closest) <= 0.45f)
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ToGridIndex(int gx, int gy, int gz)
            => gx + (GridSizeX * (gz + (GridSizeZ * gy)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FromGridIndex(int index, out int gx, out int gy, out int gz)
        {
            gx = index % GridSizeX;
            int rem = index / GridSizeX;
            gz = rem % GridSizeZ;
            gy = rem / GridSizeZ;
        }

        private static void EnsureInteriorScratch(
            out byte[] cellType,
            out float[] cellSeal,
            out float[] cellRVal,
            out byte[] visited,
            out int[] queue)
        {
            cellType = _tlsCellTypeGrid ??= new byte[GridVolume];
            cellSeal = _tlsCellSealGrid ??= new float[GridVolume];
            cellRVal = _tlsCellRValueGrid ??= new float[GridVolume];
            visited = _tlsVisitedGrid ??= new byte[GridVolume];
            queue = _tlsBfsQueue ??= new int[MaxInteriorFloodFillCells + 16];
        }
    }
}
