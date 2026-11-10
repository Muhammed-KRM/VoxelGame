#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Character
{
    /// <summary>
    /// Silah veya aletin fiziksel carpismadaki davraniş sinifi.
    /// </summary>
    public enum WeaponImpactCategory : byte
    {
        NormalSword = 0,
        HeavyCleaver = 1,
        BluntMace = 2,
        Pickaxe = 3,
        WoodAxe = 4,
        Axe = 4
    }

    /// <summary>
    /// Vurulan vokselin veya dunya nesnesinin (devrik kutuk vb.) malzeme sinifi.
    /// </summary>
    public enum MaterialImpactClass : byte
    {
        FoliageOrAir = 0,
        WoodOrLog = 1,
        Wood = 1,
        SoftEarth = 2,
        HardStoneOrOre = 3,
        HardStone = 3
    }

    /// <summary>
    /// Fiziksel carpısma cozumleme sonucu:
    /// - PassThrough: Yaprak/ot gibi direncsiz hedeflerden takilmadan gecer.
    /// - CleaveThrough: Hedef bu vurusta kirildi/koparildi; alet saplanmadan yarip gecer.
    /// - Ricochet: Sert yuzeyden, yassi agizdan veya kabza yakini (Forte) vurustan kivilcim/ses ile seker.
    /// - Lodge: Hedefe fiziksel olarak saplanir; tekrar savurmak icin cekip cikarmak (Pull-Out) gerekir.
    /// </summary>
    public enum ImpactResponseOutcome : byte
    {
        PassThrough = 0,
        CleaveThrough = 1,
        Ricochet = 2,
        Lodge = 3
    }

    public readonly struct ImpactResolutionResult
    {
        public readonly ImpactResponseOutcome Outcome;
        public readonly float PenetrationDepthMeters;
        public readonly float PullResistanceWork;
        public readonly float BounceRestitution;

        public ImpactResolutionResult(
            ImpactResponseOutcome outcome,
            float penetrationDepthMeters,
            float pullResistanceWork,
            float bounceRestitution)
        {
            Outcome = outcome;
            PenetrationDepthMeters = penetrationDepthMeters;
            PullResistanceWork = pullResistanceWork;
            BounceRestitution = bounceRestitution;
        }
    }

    /// <summary>
    /// Kilic, Balta ve Kazma vuruslarinin hangi yuzeye saplanacagini (Lodge) ve hangi yuzeyden sekecegini (Ricochet)
    /// belirleyen saf (0-GC) karar motoru.
    /// </summary>
    public static class ImpactResponseResolver
    {
        public const float MinFoibleNormalizedU = 0.45f;
        public const float MinSharpEdgeAlignment01 = 0.40f;
        public const float HeavyBladeStoneLodgeEnergyThreshold = 1.15f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MaterialImpactClass ClassifyVoxelMaterial(byte materialId, bool isFallenTreeLog = false)
        {
            if (isFallenTreeLog)
            {
                return MaterialImpactClass.WoodOrLog;
            }

            if (materialId == MaterialIdCatalog.Air ||
                materialId == MaterialIdCatalog.ThatchTile ||
                materialId == MaterialIdCatalog.TacticalCloth)
            {
                return MaterialImpactClass.FoliageOrAir;
            }

            if (materialId == MaterialIdCatalog.OakWood ||
                materialId == MaterialIdCatalog.Ironwood ||
                materialId == MaterialIdCatalog.SpiritWood ||
                materialId == MaterialIdCatalog.YggdrasilBranch ||
                materialId == MaterialIdCatalog.WoodPlank)
            {
                return MaterialImpactClass.WoodOrLog;
            }

            if (materialId == MaterialIdCatalog.Dirt ||
                materialId == MaterialIdCatalog.Grass ||
                materialId == MaterialIdCatalog.Sand ||
                materialId == MaterialIdCatalog.Clay ||
                materialId == MaterialIdCatalog.Gravel ||
                materialId == MaterialIdCatalog.LooseRubble ||
                materialId == MaterialIdCatalog.GlowingMoss)
            {
                return MaterialImpactClass.SoftEarth;
            }

            return MaterialImpactClass.HardStoneOrOre;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WeaponImpactCategory ClassifyWeaponOrTool(int hotbarSlot, int weaponProfileIndex)
        {
            if (hotbarSlot == 2)
            {
                return WeaponImpactCategory.Pickaxe;
            }

            if (hotbarSlot == 3)
            {
                return WeaponImpactCategory.WoodAxe;
            }

            return weaponProfileIndex switch
            {
                1 => WeaponImpactCategory.HeavyCleaver, // Zweihander / Greatsword
                _ => WeaponImpactCategory.NormalSword   // Longsword, Katana, Rapier
            };
        }

        public static ImpactResolutionResult Resolve(
            WeaponImpactCategory weaponCategory,
            MaterialImpactClass materialClass,
            float normalizedBladeU,
            float edgeAlignment01,
            float impactEnergy,
            bool hasRequiredToolTier = true,
            bool targetBrokeOnHit = false)
        {
            float hitNormalizedU = normalizedBladeU;
            if (targetBrokeOnHit)
            {
                return new ImpactResolutionResult(ImpactResponseOutcome.CleaveThrough, 0f, 0f, 0f);
            }

            if (materialClass == MaterialImpactClass.FoliageOrAir)
            {
                return new ImpactResolutionResult(ImpactResponseOutcome.PassThrough, 0f, 0f, 0f);
            }

            // Gurz (BluntMace) kesici agza sahip degildir; her zaman ezer / tok geri teper
            if (weaponCategory == WeaponImpactCategory.BluntMace)
            {
                float maceRest = materialClass == MaterialImpactClass.HardStoneOrOre ? 0.48f : 0.28f;
                return new ImpactResolutionResult(ImpactResponseOutcome.Ricochet, 0f, 0f, maceRest);
            }

            // Forte (kabza yakini sap kismi, u < 0.45) veya yassi agiz (edgeAlignment < 0.40) vurusu saplanmaz, seker
            if (hitNormalizedU < MinFoibleNormalizedU || edgeAlignment01 < MinSharpEdgeAlignment01)
            {
                float glanceRest = materialClass == MaterialImpactClass.HardStoneOrOre ? 0.56f : 0.34f;
                return new ImpactResolutionResult(ImpactResponseOutcome.Ricochet, 0f, 0f, glanceRest);
            }

            float energyScale = Math.Clamp(impactEnergy, 0.75f, 1.65f);

            switch (weaponCategory)
            {
                case WeaponImpactCategory.WoodAxe:
                    return materialClass switch
                    {
                        MaterialImpactClass.WoodOrLog => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.14f * energyScale,
                            pullResistanceWork: 1.35f,
                            bounceRestitution: 0f),
                        MaterialImpactClass.SoftEarth => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.12f * energyScale,
                            pullResistanceWork: 1.00f,
                            bounceRestitution: 0f),
                        _ => new ImpactResolutionResult(
                            ImpactResponseOutcome.Ricochet,
                            penetrationDepthMeters: 0f,
                            pullResistanceWork: 0f,
                            bounceRestitution: 0.58f)
                    };

                case WeaponImpactCategory.Pickaxe:
                    if (materialClass == MaterialImpactClass.HardStoneOrOre && !hasRequiredToolTier)
                    {
                        return new ImpactResolutionResult(ImpactResponseOutcome.Ricochet, 0f, 0f, 0.62f);
                    }

                    return materialClass switch
                    {
                        MaterialImpactClass.HardStoneOrOre => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.10f * energyScale,
                            pullResistanceWork: 1.60f,
                            bounceRestitution: 0f),
                        MaterialImpactClass.SoftEarth => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.16f * energyScale,
                            pullResistanceWork: 1.00f,
                            bounceRestitution: 0f),
                        _ => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.10f * energyScale,
                            pullResistanceWork: 1.20f,
                            bounceRestitution: 0f)
                    };

                case WeaponImpactCategory.NormalSword:
                    return materialClass switch
                    {
                        MaterialImpactClass.WoodOrLog => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.11f * energyScale,
                            pullResistanceWork: 1.25f,
                            bounceRestitution: 0f),
                        MaterialImpactClass.SoftEarth => new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.13f * energyScale,
                            pullResistanceWork: 0.95f,
                            bounceRestitution: 0f),
                        _ => new ImpactResolutionResult(
                            ImpactResponseOutcome.Ricochet,
                            penetrationDepthMeters: 0f,
                            pullResistanceWork: 0f,
                            bounceRestitution: 0.60f)
                    };

                case WeaponImpactCategory.HeavyCleaver:
                    if (materialClass == MaterialImpactClass.HardStoneOrOre)
                    {
                        if (impactEnergy >= HeavyBladeStoneLodgeEnergyThreshold)
                        {
                            return new ImpactResolutionResult(
                                ImpactResponseOutcome.Lodge,
                                penetrationDepthMeters: 0.08f * energyScale,
                                pullResistanceWork: 1.75f,
                                bounceRestitution: 0f);
                        }

                        return new ImpactResolutionResult(ImpactResponseOutcome.Ricochet, 0f, 0f, 0.52f);
                    }

                    return materialClass == MaterialImpactClass.WoodOrLog
                        ? new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.18f * energyScale,
                            pullResistanceWork: 1.45f,
                            bounceRestitution: 0f)
                        : new ImpactResolutionResult(
                            ImpactResponseOutcome.Lodge,
                            penetrationDepthMeters: 0.20f * energyScale,
                            pullResistanceWork: 1.10f,
                            bounceRestitution: 0f);

                default:
                    return new ImpactResolutionResult(ImpactResponseOutcome.Ricochet, 0f, 0f, 0.45f);
            }
        }
    }
}
