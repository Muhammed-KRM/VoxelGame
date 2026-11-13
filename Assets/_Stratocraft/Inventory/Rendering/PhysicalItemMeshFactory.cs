#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Jobs;
using SysVec3 = System.Numerics.Vector3;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Inventory.Rendering
{
    /// <summary>
    /// Dogal ve fiziksel esyalarin 3B modellerini ureten tekil merkez fabrika (Single Source of Truth).
    /// Yerdeki esya, elde tutulan esya, [I] canta bezindeki esya ve dunyaya geri konan parca
    /// %100 ayni <see cref="GetOrCreateMesh"/> metodunu cagirarak tam gorsel esitlik saglar.
    /// </summary>
    public static class PhysicalItemMeshFactory
    {
        public const int VariantsPerArchetype = 12;
        private const byte DefaultAo = 0xE2;

        // Paket 4A (Faz 4A.6) — 5 Fiziksel Meteoroloji Aleti + Metal Paratoner 3B Arketip Kimlikleri
        public const PhysicalItemArchetype WeatherBarometer = (PhysicalItemArchetype)38;
        public const PhysicalItemArchetype WeatherThermometer = (PhysicalItemArchetype)39;
        public const PhysicalItemArchetype WeatherHygrometer = (PhysicalItemArchetype)40;
        public const PhysicalItemArchetype WeatherWindVaneAnemometer = (PhysicalItemArchetype)41;
        public const PhysicalItemArchetype WeatherRainGauge = (PhysicalItemArchetype)42;
        public const PhysicalItemArchetype MetalLightningRod = (PhysicalItemArchetype)43;

        private static readonly List<FacetedVoxelTriangle> SharedBuildTris = new(256);

#if UNITY_5_3_OR_NEWER
        private static readonly Dictionary<ulong, Mesh> StandardMeshCache = new(192);
        private static readonly Dictionary<ulong, Mesh> CustomKnapMeshCache = new(64);

        public static Mesh GetOrCreateMesh(
            PhysicalItemArchetype archetype,
            byte subMaterialId,
            in PhysicalItemShapeDna dna,
            out Vector3 naturalScale)
        {
            SysVec3 sv = ComputeNaturalVariantScale(archetype, in dna);
            naturalScale = new Vector3(sv.X, sv.Y, sv.Z);

            byte resolvedMat = ResolvePrimaryMaterialId(archetype, subMaterialId);
            if (dna.HasCustomKnapMask && (IsKnappedHeadArchetype(archetype) || IsCustomKnappedToolArchetype(archetype)))
            {
                ulong knapKey = dna.KnapMask64
                              ^ ((ulong)archetype * 0xBF58476D1CE4E5B9UL)
                              ^ ((ulong)resolvedMat * 0x9E3779B97F4A7C15UL)
                              ^ ((ulong)dna.ThermalTempByte << 48);
                if (CustomKnapMeshCache.TryGetValue(knapKey, out Mesh? cachedKnap) && cachedKnap != null)
                {
                    return cachedKnap;
                }

                SharedBuildTris.Clear();
                if (IsKnappedHeadArchetype(archetype))
                {
                    EmitKnappedMaskSlab(dna.KnapMask64, resolvedMat, dna.ThermalTempByte, SharedBuildTris);
                }
                else
                {
                    EmitArchetypeTriangles(archetype, resolvedMat, in dna, SharedBuildTris);
                }
                Mesh builtKnap = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    SharedBuildTris, $"Mesh_CustomKnap_{archetype}_{resolvedMat}_{dna.KnapMask64:X16}");
                if (CustomKnapMeshCache.Count >= 160)
                {
                    CustomKnapMeshCache.Clear();
                }
                CustomKnapMeshCache[knapKey] = builtKnap;
                return builtKnap;
            }

            int variantIdx = (int)(dna.EffectiveSeed % VariantsPerArchetype);
            ulong key = ((ulong)archetype << 32)
                      | ((ulong)resolvedMat << 24)
                      | ((ulong)dna.ThermalTempByte << 16)
                      | ((ulong)(dna.Flags & 0x0Fu) << 8)
                      | (uint)variantIdx;

            if (StandardMeshCache.TryGetValue(key, out Mesh? cached) && cached != null)
            {
                return cached;
            }

            SharedBuildTris.Clear();
            EmitArchetypeTriangles(archetype, resolvedMat, in dna, SharedBuildTris);
            Mesh built = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                SharedBuildTris, $"Mesh_PhysItem_{archetype}_{resolvedMat}_v{variantIdx}");
            StandardMeshCache[key] = built;
            return built;
        }

        /// <summary>
        /// Dunyada yerde duran (dogal olusan, kazidan dusen veya [Q] ile atilan) tum fiziksel esyalarin
        /// uzaktan ve cimenlerin arasinda net gorunmesi icin dunya zemin olcegini dondurur.
        /// ([I] Canta bezi ise kendi dar yigin olcegini korur, boylece bez uzerinde ust uste tasma olmaz.)
        /// </summary>
        public static Vector3 GetWorldGroundScale(PhysicalItemArchetype archetype, Vector3 naturalScale)
        {
            return archetype switch
            {
                PhysicalItemArchetype.ArchitecturalBlock => new Vector3(0.46f, 0.46f, 0.46f),
                PhysicalItemArchetype.DirtClod or
                PhysicalItemArchetype.SandMound or
                PhysicalItemArchetype.ClayLump or
                PhysicalItemArchetype.RoughStone or
                PhysicalItemArchetype.FlintStone or
                PhysicalItemArchetype.LeafPile or
                PhysicalItemArchetype.PlantFiber or
                PhysicalItemArchetype.WildBerryCluster or
                PhysicalItemArchetype.CharcoalChunk or
                PhysicalItemArchetype.CrushedOreGravel or
                PhysicalItemArchetype.SlagGlassShard => naturalScale * 1.82f,

                PhysicalItemArchetype.OreBearingRock or
                PhysicalItemArchetype.QuartzCrystalCluster or
                PhysicalItemArchetype.RoastedCrackedOre or
                PhysicalItemArchetype.KnappedAxeHead or
                PhysicalItemArchetype.KnappedPickaxeHead or
                PhysicalItemArchetype.KnappedKnifeBlade or
                PhysicalItemArchetype.KnappedHammerHead or
                PhysicalItemArchetype.RawMeatCut or
                PhysicalItemArchetype.CookedMeatCut or
                PhysicalItemArchetype.BurntCharredMeat or
                PhysicalItemArchetype.RawHideRoll or
                PhysicalItemArchetype.ClayTuyerePipe or
                PhysicalItemArchetype.ClayCruciblePot or
                PhysicalItemArchetype.IronBloomSponge or
                PhysicalItemArchetype.WroughtIronBar or
                PhysicalItemArchetype.MetalIngotBar => naturalScale * 1.68f,

                PhysicalItemArchetype.NaturalStick or
                PhysicalItemArchetype.SplitFirewood or
                PhysicalItemArchetype.LeatherBellows or
                PhysicalItemArchetype.WoodenWheel => naturalScale * 1.50f,

                PhysicalItemArchetype.ToolPrimitiveAxe or
                PhysicalItemArchetype.ToolPrimitivePickaxe or
                PhysicalItemArchetype.ToolPrimitiveKnife or
                PhysicalItemArchetype.ToolPrimitiveHammer => naturalScale * 1.38f,

                PhysicalItemArchetype.WildBushFoliage or
                PhysicalItemArchetype.WildBushBareRoots => naturalScale * 1.15f,

                _ => naturalScale * 1.65f
            };
        }
#endif

        public static bool IsKnappedHeadArchetype(PhysicalItemArchetype archetype)
        {
            return archetype == PhysicalItemArchetype.KnappedAxeHead ||
                   archetype == PhysicalItemArchetype.KnappedPickaxeHead ||
                   archetype == PhysicalItemArchetype.KnappedKnifeBlade ||
                   archetype == PhysicalItemArchetype.KnappedHammerHead;
        }

        public static bool IsCustomKnappedToolArchetype(PhysicalItemArchetype archetype)
        {
            return archetype == PhysicalItemArchetype.ToolPrimitiveAxe ||
                   archetype == PhysicalItemArchetype.ToolPrimitivePickaxe ||
                   archetype == PhysicalItemArchetype.ToolPrimitiveKnife ||
                   archetype == PhysicalItemArchetype.ToolPrimitiveHammer;
        }

        public static byte ResolvePrimaryMaterialId(PhysicalItemArchetype archetype, byte subMaterialId)
        {
            return archetype switch
            {
                PhysicalItemArchetype.NaturalStick => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.OakWood,
                PhysicalItemArchetype.SplitFirewood => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.OakWood,
                PhysicalItemArchetype.LeafPile => MaterialIdCatalog.Grass,
                PhysicalItemArchetype.PlantFiber => MaterialIdCatalog.GlowingMoss,
                PhysicalItemArchetype.RoughStone => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.Stone,
                PhysicalItemArchetype.FlintStone => MaterialIdCatalog.Basalt,
                PhysicalItemArchetype.QuartzCrystalCluster => MaterialIdCatalog.QuartzGem,
                PhysicalItemArchetype.OreBearingRock => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.CopperOre,
                PhysicalItemArchetype.DirtClod => MaterialIdCatalog.Dirt,
                PhysicalItemArchetype.SandMound => MaterialIdCatalog.Sand,
                PhysicalItemArchetype.ClayLump => MaterialIdCatalog.Clay,
                PhysicalItemArchetype.KnappedAxeHead or PhysicalItemArchetype.KnappedKnifeBlade => MaterialIdCatalog.Basalt,
                PhysicalItemArchetype.KnappedPickaxeHead or PhysicalItemArchetype.KnappedHammerHead => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.Cobblestone,
                PhysicalItemArchetype.RawMeatCut => MaterialIdCatalog.RubyGem,
                PhysicalItemArchetype.CookedMeatCut => MaterialIdCatalog.LeatherPadding,
                PhysicalItemArchetype.BurntCharredMeat or PhysicalItemArchetype.CharcoalChunk => MaterialIdCatalog.Coal,
                PhysicalItemArchetype.WildBerryCluster => MaterialIdCatalog.RedDiamondGem,
                PhysicalItemArchetype.RawHideRoll => MaterialIdCatalog.CreatureFurAsh,
                PhysicalItemArchetype.RoastedCrackedOre or PhysicalItemArchetype.CrushedOreGravel => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.IronOre,
                PhysicalItemArchetype.ClayTuyerePipe or PhysicalItemArchetype.ClayCruciblePot => MaterialIdCatalog.Clay,
                PhysicalItemArchetype.LeatherBellows => MaterialIdCatalog.LeatherPadding,
                PhysicalItemArchetype.IronBloomSponge => MaterialIdCatalog.IronOre,
                PhysicalItemArchetype.WroughtIronBar => MaterialIdCatalog.Steel,
                PhysicalItemArchetype.SlagGlassShard => MaterialIdCatalog.NaturalObsidian,
                PhysicalItemArchetype.WoodenWheel => MaterialIdCatalog.OakWood,
                PhysicalItemArchetype.MetalIngotBar => subMaterialId switch
                {
                    1 => MaterialIdCatalog.Bronze,
                    2 => MaterialIdCatalog.Steel,
                    3 => MaterialIdCatalog.MithrilOre,
                    _ => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.Steel
                },
                PhysicalItemArchetype.WildBushFoliage => MaterialIdCatalog.Grass,
                PhysicalItemArchetype.WildBushBareRoots => MaterialIdCatalog.OakWood,
                WeatherBarometer => MaterialIdCatalog.Bronze,
                WeatherThermometer => MaterialIdCatalog.QuartzGem,
                WeatherHygrometer => MaterialIdCatalog.OakWood,
                WeatherWindVaneAnemometer => MaterialIdCatalog.Steel,
                WeatherRainGauge => MaterialIdCatalog.QuartzGem,
                MetalLightningRod => MaterialIdCatalog.CopperOre,
                _ => subMaterialId != 0 ? subMaterialId : MaterialIdCatalog.Stone
            };
        }

        public static PhysicalItemArchetype MapCraftOutputToArchetype(PrimitiveCraftOutputId outputId) => outputId switch
        {
            PrimitiveCraftOutputId.PrimitiveFlintAxe => PhysicalItemArchetype.ToolPrimitiveAxe,
            PrimitiveCraftOutputId.PrimitiveStonePickaxe => PhysicalItemArchetype.ToolPrimitivePickaxe,
            PrimitiveCraftOutputId.PrimitiveFlintKnife => PhysicalItemArchetype.ToolPrimitiveKnife,
            PrimitiveCraftOutputId.WeatherBarometer => WeatherBarometer,
            PrimitiveCraftOutputId.WeatherThermometer => WeatherThermometer,
            PrimitiveCraftOutputId.WeatherHygrometer => WeatherHygrometer,
            PrimitiveCraftOutputId.WeatherWindVaneAnemometer => WeatherWindVaneAnemometer,
            PrimitiveCraftOutputId.WeatherRainGauge => WeatherRainGauge,
            PrimitiveCraftOutputId.MetalLightningRod => MetalLightningRod,
            _ => PhysicalItemArchetype.RoughStone
        };

        public static void EmitWeatherInstrumentTriangles(
            PrimitiveCraftOutputId instrumentId,
            float primaryReading01,
            float secondaryAngleRad,
            List<FacetedVoxelTriangle> outTris)
        {
            byte dialStep = (byte)Math.Clamp((int)MathF.Round(Math.Clamp(primaryReading01, 0f, 1f) * 255f), 0, 255);
            float wrappedAngle = secondaryAngleRad % (MathF.PI * 2f);
            if (wrappedAngle < 0f) wrappedAngle += MathF.PI * 2f;
            byte yawByte = (byte)Math.Clamp((int)MathF.Round((wrappedAngle / (MathF.PI * 2f)) * 255f), 0, 255);

            PhysicalItemShapeDna dna = PhysicalItemShapeDna.CreateNatural(0x4A600001u + (uint)instrumentId)
                .WithStage(dialStep)
                .WithTemperature(yawByte);

            PhysicalItemArchetype arch = MapCraftOutputToArchetype(instrumentId);
            EmitArchetypeTriangles(arch, ResolvePrimaryMaterialId(arch, 0), in dna, outTris);
        }

        public static SysVec3 ComputeNaturalVariantScale(PhysicalItemArchetype archetype, in PhysicalItemShapeDna dna)
        {
            if (archetype == PhysicalItemArchetype.ArchitecturalBlock)
            {
                return new SysVec3(0.28f, 0.28f, 0.28f);
            }

            uint h = PhysicalItemShapeDna.MixHash(dna.EffectiveSeed ^ 0x517CC1B7u);
            float sx = 0.90f + ((h & 0xFFu) / 255f) * 0.22f;
            float sy = 0.90f + (((h >> 8) & 0xFFu) / 255f) * 0.20f;
            float sz = 0.90f + (((h >> 16) & 0xFFu) / 255f) * 0.22f;

            if (archetype == PhysicalItemArchetype.CookedMeatCut)
            {
                return new SysVec3(sx * 0.90f, sy * 0.86f, sz * 0.90f);
            }
            if (archetype == PhysicalItemArchetype.BurntCharredMeat)
            {
                return new SysVec3(sx * 0.78f, sy * 0.72f, sz * 0.78f);
            }

            return new SysVec3(sx, sy, sz);
        }

        public static void EmitArchetypeTriangles(
            PhysicalItemArchetype archetype,
            byte primaryMatId,
            in PhysicalItemShapeDna dna,
            List<FacetedVoxelTriangle> outTris)
        {
            uint vSeed = PhysicalItemShapeDna.MixHash((uint)(dna.EffectiveSeed % VariantsPerArchetype) * 0x9E3779B9u + 0x13579BDFu);
            byte temp = dna.ThermalTempByte;

            switch (archetype)
            {
                case PhysicalItemArchetype.NaturalStick:
                    EmitBentNaturalBranch(vSeed, primaryMatId, temp, (dna.Flags & PhysicalItemShapeDna.FlagBrokenStick) != 0, outTris);
                    break;

                case PhysicalItemArchetype.SplitFirewood:
                    EmitSplitFirewoodWedge(vSeed, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.LeafPile:
                    EmitOverlappingLeafPile(vSeed, temp, outTris);
                    break;

                case PhysicalItemArchetype.PlantFiber:
                    EmitBraidedFiberCoil(vSeed, temp, outTris);
                    break;

                case PhysicalItemArchetype.RoughStone:
                    EmitIrregularRiverCobble(vSeed, primaryMatId, temp, 0.148f, 0.088f, 0.128f, outTris);
                    break;

                case PhysicalItemArchetype.FlintStone:
                    EmitSharpFlintCore(vSeed, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.QuartzCrystalCluster:
                    EmitQuartzPrismCluster(vSeed, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.OreBearingRock:
                case PhysicalItemArchetype.RoastedCrackedOre:
                {
                    byte rockHostMat = archetype == PhysicalItemArchetype.RoastedCrackedOre
                        ? MaterialIdCatalog.Cobblestone
                        : MaterialIdCatalog.Stone;
                    byte effectiveTemp = archetype == PhysicalItemArchetype.RoastedCrackedOre && temp < 35 ? (byte)35 : temp;
                    EmitIrregularRiverCobble(vSeed, rockHostMat, effectiveTemp, 0.150f, 0.105f, 0.134f, outTris);
                    EmitOreVeinNuggetsOnRock(vSeed, primaryMatId, effectiveTemp, outTris);
                    break;
                }

                case PhysicalItemArchetype.CrushedOreGravel:
                    EmitCrushedGravelCluster(vSeed, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.DirtClod:
                case PhysicalItemArchetype.SandMound:
                case PhysicalItemArchetype.ClayLump:
                    EmitSoftEarthMound(vSeed, primaryMatId, temp, archetype == PhysicalItemArchetype.SandMound ? 0.082f : 0.104f, outTris);
                    break;

                case PhysicalItemArchetype.KnappedAxeHead:
                    EmitKnappedMaskSlab(0x3C7E7E7E3C180000UL, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.KnappedPickaxeHead:
                    EmitKnappedMaskSlab(0x003C7EE7C3181800UL, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.KnappedKnifeBlade:
                    EmitKnappedMaskSlab(0x081C1C3C38181000UL, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.KnappedHammerHead:
                    EmitKnappedMaskSlab(0x003C7E7E7E3C0000UL, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.RawMeatCut:
                case PhysicalItemArchetype.CookedMeatCut:
                case PhysicalItemArchetype.BurntCharredMeat:
                    EmitOrganicMeatCut(vSeed, primaryMatId, archetype == PhysicalItemArchetype.RawMeatCut, temp, outTris);
                    break;

                case PhysicalItemArchetype.WildBerryCluster:
                    EmitWildBerryCluster(vSeed, outTris);
                    break;

                case PhysicalItemArchetype.RawHideRoll:
                    EmitRolledAnimalHide(vSeed, primaryMatId, outTris);
                    break;

                case PhysicalItemArchetype.CharcoalChunk:
                    EmitCharcoalLumpCluster(vSeed, temp, outTris);
                    break;

                case PhysicalItemArchetype.ClayTuyerePipe:
                    EmitHollowTubeSegment(new SysVec3(0f, 0f, -0.14f), new SysVec3(0f, 0f, 0.14f), 0.052f, 0.042f, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.ClayCruciblePot:
                    EmitHollowTubeSegment(new SysVec3(0f, -0.08f, 0f), new SysVec3(0f, 0.09f, 0f), 0.075f, 0.098f, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.LeatherBellows:
                    EmitLeatherBellowsModel(temp, outTris);
                    break;

                case PhysicalItemArchetype.IronBloomSponge:
                {
                    byte bloomTemp = temp == 0 ? (byte)205 : temp;
                    EmitIrregularRiverCobble(vSeed, MaterialIdCatalog.IronOre, bloomTemp, 0.12f, 0.095f, 0.11f, outTris);
                    EmitOreVeinNuggetsOnRock(vSeed ^ 0xA5A5u, MaterialIdCatalog.MagmaBlock, bloomTemp, outTris);
                    break;
                }

                case PhysicalItemArchetype.WroughtIronBar:
                case PhysicalItemArchetype.MetalIngotBar:
                    EmitOrientedBox(SysVec3.Zero, new SysVec3(0.048f, 0.024f, 0.155f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, primaryMatId, temp, outTris);
                    break;

                case PhysicalItemArchetype.SlagGlassShard:
                    EmitSharpFlintCore(vSeed ^ 0x7711u, MaterialIdCatalog.NaturalObsidian, temp, outTris);
                    break;

                case PhysicalItemArchetype.WoodenWheel:
                    EmitWoodenWheelModel(primaryMatId, outTris);
                    break;

                case PhysicalItemArchetype.WildBushFoliage:
                    EmitWildBushModel(vSeed, bareRootsOnly: false, hasBerries: (dna.Flags & PhysicalItemShapeDna.FlagHasBerries) != 0, outTris);
                    break;

                case PhysicalItemArchetype.WildBushBareRoots:
                    EmitWildBushModel(vSeed, bareRootsOnly: true, hasBerries: false, outTris);
                    break;

                case PhysicalItemArchetype.ToolPrimitiveAxe:
                case PhysicalItemArchetype.ToolPrimitivePickaxe:
                case PhysicalItemArchetype.ToolPrimitiveKnife:
                case PhysicalItemArchetype.ToolPrimitiveHammer:
                {
                    float shaftHalf = archetype == PhysicalItemArchetype.ToolPrimitiveKnife ? 0.14f : 0.30f;
                    EmitHollowTubeSegment(new SysVec3(0f, -shaftHalf, 0f), new SysVec3(0f, shaftHalf, 0f), 0.024f, 0.021f, MaterialIdCatalog.OakWood, temp, outTris, sides: 6);
                    // Capraz bitki lifi dugumu
                    EmitHollowTubeSegment(new SysVec3(0f, shaftHalf - 0.055f, 0f), new SysVec3(0f, shaftHalf + 0.015f, 0f), 0.031f, 0.031f, MaterialIdCatalog.GlowingMoss, temp, outTris, sides: 6);
                    EmitOrientedBox(new SysVec3(0f, shaftHalf - 0.020f, 0f), new SysVec3(0.035f, 0.022f, 0.035f),
                        SysVec3.Normalize(new SysVec3(1f, 0.35f, 0f)), SysVec3.Normalize(new SysVec3(-0.35f, 1f, 0f)), SysVec3.UnitZ,
                        MaterialIdCatalog.GlowingMoss, temp, outTris);

                    byte stoneMat = (primaryMatId != 0 && primaryMatId != 1) ? primaryMatId : MaterialIdCatalog.Cobblestone;
                    if (dna.HasCustomKnapMask)
                    {
                        // Oyuncunun bizzat yonttugu 8x8 KnapMask64 tas basligini aletin ucuna dikey duzlemde monte et!
                        SysVec3 mountPos = archetype == PhysicalItemArchetype.ToolPrimitiveKnife
                            ? new SysVec3(0f, shaftHalf + 0.10f, 0f)
                            : new SysVec3(archetype == PhysicalItemArchetype.ToolPrimitiveAxe ? 0.035f : 0f, shaftHalf - 0.015f, 0f);
                        float scaleMult = archetype == PhysicalItemArchetype.ToolPrimitivePickaxe ? 1.18f : 0.96f;
                        // Slab normali Z eksenine baksin, X genisligi yatay/dikey alet agzi olsun
                        EmitKnappedMaskSlabTransformed(
                            dna.KnapMask64, stoneMat, temp, mountPos,
                            axisRight: SysVec3.UnitX * scaleMult,
                            axisThickness: SysVec3.UnitZ * (archetype == PhysicalItemArchetype.ToolPrimitiveHammer ? 1.65f : 0.85f),
                            axisForward: SysVec3.UnitY * scaleMult,
                            outTris);
                    }
                    else
                    {
                        SysVec3 headExt = archetype switch
                        {
                            PhysicalItemArchetype.ToolPrimitivePickaxe => new SysVec3(0.16f, 0.028f, 0.028f),
                            PhysicalItemArchetype.ToolPrimitiveHammer => new SysVec3(0.095f, 0.052f, 0.055f),
                            PhysicalItemArchetype.ToolPrimitiveKnife => new SysVec3(0.024f, 0.12f, 0.012f),
                            _ => new SysVec3(0.095f, 0.055f, 0.022f)
                        };
                        SysVec3 headPos = archetype == PhysicalItemArchetype.ToolPrimitiveKnife
                            ? new SysVec3(0f, shaftHalf + 0.10f, 0f)
                            : new SysVec3(archetype == PhysicalItemArchetype.ToolPrimitiveAxe ? 0.045f : 0f, shaftHalf - 0.015f, 0f);
                        EmitOrientedBox(headPos, headExt, SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, stoneMat, temp, outTris);
                    }
                    break;
                }

                case WeatherBarometer:
                    EmitWeatherBarometerModel(in dna, outTris);
                    break;

                case WeatherThermometer:
                    EmitWeatherThermometerModel(in dna, outTris);
                    break;

                case WeatherHygrometer:
                    EmitWeatherHygrometerModel(in dna, outTris);
                    break;

                case WeatherWindVaneAnemometer:
                    EmitWeatherWindVaneAnemometerModel(in dna, outTris);
                    break;

                case WeatherRainGauge:
                    EmitWeatherRainGaugeModel(in dna, outTris);
                    break;

                case MetalLightningRod:
                    EmitMetalLightningRodModel(in dna, outTris);
                    break;

                default:
                    HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                        new SysVec3(-0.5f, -0.5f, -0.5f), primaryMatId, 1, 0, 0, outTris);
                    break;
            }
        }

        public static void EmitKnappedMaskSlab(ulong mask64, byte matId, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            EmitKnappedMaskSlabTransformed(mask64, matId, temp, SysVec3.Zero, SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, outTris);
        }

        public static void EmitKnappedMaskSlabTransformed(
            ulong mask64,
            byte matId,
            byte temp,
            SysVec3 originOffset,
            SysVec3 axisRight,
            SysVec3 axisThickness,
            SysVec3 axisForward,
            List<FacetedVoxelTriangle> outTris)
        {
            const float cellSize = 0.032f;
            const float halfSpan = cellSize * 4f;
            const float halfThick = 0.024f;

            float rightLen = axisRight.Length();
            float thickLen = axisThickness.Length();
            float fwdLen = axisForward.Length();
            SysVec3 uR = rightLen > 0.0001f ? (axisRight / rightLen) : SysVec3.UnitX;
            SysVec3 uT = thickLen > 0.0001f ? (axisThickness / thickLen) : SysVec3.UnitY;
            SysVec3 uF = fwdLen > 0.0001f ? (axisForward / fwdLen) : SysVec3.UnitZ;

            for (int cz = 0; cz < 8; cz++)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    int bit = cz * 8 + cx;
                    if ((mask64 & (1UL << bit)) == 0UL) continue;

                    bool edgeCell = cx == 0 || cx == 7 || cz == 0 || cz == 7 ||
                                    (mask64 & (1UL << (cz * 8 + (cx - 1)))) == 0UL ||
                                    (mask64 & (1UL << (cz * 8 + (cx + 1)))) == 0UL ||
                                    (mask64 & (1UL << ((cz - 1) * 8 + cx))) == 0UL ||
                                    (mask64 & (1UL << ((cz + 1) * 8 + cx))) == 0UL;

                    float microRelief = (((cx * 17 + cz * 31) % 5) - 2) * 0.0018f;
                    float bevelH = (edgeCell ? halfThick * 0.64f : halfThick) + microRelief;
                    float localX = -halfSpan + (cx + 0.5f) * cellSize;
                    float localZ = -halfSpan + (cz + 0.5f) * cellSize;
                    SysVec3 c = originOffset + axisRight * localX + axisForward * localZ;
                    EmitOrientedBox(
                        c,
                        new SysVec3(cellSize * 0.495f * rightLen, bevelH * thickLen, cellSize * 0.495f * fwdLen),
                        uR, uT, uF, matId, temp, outTris);
                }
            }
        }

#if UNITY_5_3_OR_NEWER
        public const float KnappingSlabCellSizeMeters = 0.052f;
        public const float KnappingSlabHalfSpanMeters = KnappingSlabCellSizeMeters * 4f; // 0.208m -> 0.416m x 0.416m dolgun yer tasi

        /// <summary>
        /// Faz 2.2 (Asama 2.2): Yerdeki aktif yontulan tasin 3B modelini, ustundeki tebesir/cizik hayalet kalip cizgisini
        /// (outlineDrawProgress01: 0..1) ve farenin ustunde durdugu hucre vurgusunu sifir doku VRAM ile uretir.
        /// </summary>
        public static Mesh BuildInteractiveKnappingSlabMesh(
            ulong currentMask64,
            ulong templateMask64,
            byte stoneMatId,
            int hoveredCellX,
            int hoveredCellZ,
            float outlineDrawProgress01)
        {
            SharedBuildTris.Clear();
            byte baseMat = stoneMatId != 0 ? stoneMatId : MaterialIdCatalog.Stone;
            const float cellSize = KnappingSlabCellSizeMeters;
            const float halfSpan = KnappingSlabHalfSpanMeters;
            const float halfThick = 0.038f;

            // 1) Mevcut 8x8 tas kutlesi + hedef hucre vurgusu
            for (int cz = 0; cz < 8; cz++)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    int bit = cz * 8 + cx;
                    ulong bitFlag = 1UL << bit;
                    if ((currentMask64 & bitFlag) == 0UL) continue;

                    bool edgeCell = cx == 0 || cx == 7 || cz == 0 || cz == 7 ||
                                    (currentMask64 & (1UL << (cz * 8 + (cx - 1)))) == 0UL ||
                                    (currentMask64 & (1UL << (cz * 8 + (cx + 1)))) == 0UL ||
                                    (currentMask64 & (1UL << ((cz - 1) * 8 + cx))) == 0UL ||
                                    (currentMask64 & (1UL << ((cz + 1) * 8 + cx))) == 0UL;

                    bool isHovered = cx == hoveredCellX && cz == hoveredCellZ;
                    bool isCore = (templateMask64 & bitFlag) != 0UL;
                    byte cellMat = baseMat;
                    if (isHovered)
                    {
                        // Cizgi disi fazlaliksa yesilimsi-acik vurgu, cizgi ici cekirdekse turuncu uyari vurgusu!
                        cellMat = isCore ? MaterialIdCatalog.CopperOre : MaterialIdCatalog.GlowingMoss;
                    }
                    else if (!isCore && outlineDrawProgress01 >= 0.99f)
                    {
                        // Dis fazlalik bolgesini cok hafif koyu tas tonuyla ayirt et ki kalip daha rahat okunsun
                        cellMat = baseMat == MaterialIdCatalog.Stone ? MaterialIdCatalog.Cobblestone : baseMat;
                    }

                    float microH = (((cx * 23 + cz * 13) % 5) - 2) * 0.002f;
                    float h = (edgeCell ? halfThick * 0.72f : halfThick) + microH + (isHovered ? 0.004f : 0f);
                    SysVec3 c = new SysVec3(-halfSpan + (cx + 0.5f) * cellSize, 0f, -halfSpan + (cz + 0.5f) * cellSize);
                    EmitOrientedBox(c, new SysVec3(cellSize * 0.495f, h, cellSize * 0.495f),
                        SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, cellMat, 0, SharedBuildTris);
                }
            }

            // 2) Cakmaktasi ile tasin ust yuzeyine cizilen hayalet kalip sinir cizgisi (0..1 ilerlemeli!)
            if (outlineDrawProgress01 > 0.01f && templateMask64 != 0UL)
            {
                int totalEdges = CountTemplateBoundaryEdges(templateMask64);
                int edgesToDraw = Math.Max(1, (int)MathF.Round(totalEdges * Math.Clamp(outlineDrawProgress01, 0f, 1f)));
                int drawn = 0;
                const float lineY = halfThick + 0.0055f;
                const float lineHalfW = 0.0032f;

                for (int cz = 0; cz < 8 && drawn < edgesToDraw; cz++)
                {
                    for (int cx = 0; cx < 8 && drawn < edgesToDraw; cx++)
                    {
                        if ((templateMask64 & (1UL << (cz * 8 + cx))) == 0UL) continue;
                        // Sadece altinda hala tas varsa tebesir cizgisini goster
                        bool cellIntact = (currentMask64 & (1UL << (cz * 8 + cx))) != 0UL;
                        float x0 = -halfSpan + cx * cellSize;
                        float x1 = x0 + cellSize;
                        float z0 = -halfSpan + cz * cellSize;
                        float z1 = z0 + cellSize;
                        float xm = (x0 + x1) * 0.5f;
                        float zm = (z0 + z1) * 0.5f;

                        // Sol sinir
                        if (cx == 0 || (templateMask64 & (1UL << (cz * 8 + (cx - 1)))) == 0UL)
                        {
                            drawn++;
                            if (cellIntact)
                                EmitOrientedBox(new SysVec3(x0, lineY, zm), new SysVec3(lineHalfW, 0.0028f, cellSize * 0.51f),
                                    SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.QuartzGem, 25, SharedBuildTris);
                        }
                        // Sag sinir
                        if (drawn < edgesToDraw && (cx == 7 || (templateMask64 & (1UL << (cz * 8 + (cx + 1)))) == 0UL))
                        {
                            drawn++;
                            if (cellIntact)
                                EmitOrientedBox(new SysVec3(x1, lineY, zm), new SysVec3(lineHalfW, 0.0028f, cellSize * 0.51f),
                                    SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.QuartzGem, 25, SharedBuildTris);
                        }
                        // Alt sinir
                        if (drawn < edgesToDraw && (cz == 0 || (templateMask64 & (1UL << ((cz - 1) * 8 + cx))) == 0UL))
                        {
                            drawn++;
                            if (cellIntact)
                                EmitOrientedBox(new SysVec3(xm, lineY, z0), new SysVec3(cellSize * 0.51f, 0.0028f, lineHalfW),
                                    SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.QuartzGem, 25, SharedBuildTris);
                        }
                        // Ust sinir
                        if (drawn < edgesToDraw && (cz == 7 || (templateMask64 & (1UL << ((cz + 1) * 8 + cx))) == 0UL))
                        {
                            drawn++;
                            if (cellIntact)
                                EmitOrientedBox(new SysVec3(xm, lineY, z1), new SysVec3(cellSize * 0.51f, 0.0028f, lineHalfW),
                                    SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.QuartzGem, 25, SharedBuildTris);
                        }
                    }
                }
            }

            return HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(SharedBuildTris, "Mesh_InteractiveStoneKnappingSlab");
        }

        private static int CountTemplateBoundaryEdges(ulong templateMask64)
        {
            int count = 0;
            for (int cz = 0; cz < 8; cz++)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    if ((templateMask64 & (1UL << (cz * 8 + cx))) == 0UL) continue;
                    if (cx == 0 || (templateMask64 & (1UL << (cz * 8 + (cx - 1)))) == 0UL) count++;
                    if (cx == 7 || (templateMask64 & (1UL << (cz * 8 + (cx + 1)))) == 0UL) count++;
                    if (cz == 0 || (templateMask64 & (1UL << ((cz - 1) * 8 + cx))) == 0UL) count++;
                    if (cz == 7 || (templateMask64 & (1UL << ((cz + 1) * 8 + cx))) == 0UL) count++;
                }
            }
            return Math.Max(1, count);
        }
#endif

        private static void EmitBentNaturalBranch(uint seed, byte woodMat, byte temp, bool isBrokenShort, List<FacetedVoxelTriangle> outTris)
        {
            float len = isBrokenShort ? 0.30f : (0.46f + Rand01(ref seed) * 0.16f);
            float r0 = 0.024f + Rand01(ref seed) * 0.009f;
            float bendX = (Rand01(ref seed) - 0.5f) * 0.075f;
            float bendY = (Rand01(ref seed) - 0.35f) * 0.045f;

            SysVec3 p0 = new SysVec3(-bendX * 0.4f, 0f, -len * 0.5f);
            SysVec3 p1 = new SysVec3(bendX * 0.65f, bendY, -len * 0.08f);
            SysVec3 p2 = new SysVec3(-bendX * 0.35f, bendY * 0.5f, len * 0.24f);
            SysVec3 p3 = new SysVec3(bendX * 0.25f, 0.01f, len * 0.5f);

            EmitHollowTubeSegment(p0, p1, r0, r0 * 0.88f, woodMat, temp, outTris, sides: 6);
            EmitHollowTubeSegment(p1, p2, r0 * 0.88f, r0 * 0.74f, woodMat, temp, outTris, sides: 6);
            EmitHollowTubeSegment(p2, p3, r0 * 0.74f, r0 * 0.56f, woodMat, temp, outTris, sides: 6);

            if (!isBrokenShort && Rand01(ref seed) > 0.32f)
            {
                float sideSign = Rand01(ref seed) > 0.5f ? 1f : -1f;
                SysVec3 forkEnd = p1 + new SysVec3(sideSign * 0.11f, 0.035f, 0.13f);
                EmitHollowTubeSegment(p1, forkEnd, r0 * 0.55f, r0 * 0.28f, woodMat, temp, outTris, sides: 5);
            }
        }

        private static void EmitSplitFirewoodWedge(uint seed, byte barkMat, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            float halfLen = 0.22f + Rand01(ref seed) * 0.04f;
            float radius = 0.075f + Rand01(ref seed) * 0.018f;
            SysVec3 a0 = new SysVec3(0f, -0.03f, -halfLen);
            SysVec3 b0 = new SysVec3(-radius, -0.02f, -halfLen);
            SysVec3 c0 = new SysVec3(radius, -0.02f, -halfLen);
            SysVec3 d0 = new SysVec3(0f, radius * 0.85f, -halfLen);

            SysVec3 a1 = new SysVec3(0f, -0.03f, halfLen);
            SysVec3 b1 = new SysVec3(-radius * 0.92f, -0.02f, halfLen);
            SysVec3 c1 = new SysVec3(radius * 0.92f, -0.02f, halfLen);
            SysVec3 d1 = new SysVec3(0f, radius * 0.80f, halfLen);

            byte innerWood = MaterialIdCatalog.WoodPlank;
            AddQuad(b0, b1, d1, d0, innerWood, temp, 210, outTris);
            AddQuad(d0, d1, c1, c0, innerWood, temp, 200, outTris);
            AddQuad(b0, a0, a1, b1, barkMat, temp, 165, outTris);
            AddQuad(a0, c0, c1, a1, barkMat, temp, 165, outTris);
            AddQuad(b0, d0, c0, a0, innerWood, temp, 185, outTris);
            AddQuad(a1, c1, d1, b1, innerWood, temp, 185, outTris);
        }

        private static void EmitOverlappingLeafPile(uint seed, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            int leafCount = 4 + (int)(seed % 3u);
            for (int i = 0; i < leafCount; i++)
            {
                float angle = i * 1.35f + Rand01(ref seed) * 0.55f;
                float dist = 0.025f + i * 0.012f;
                float y = -0.015f + i * 0.009f;
                SysVec3 center = new SysVec3(MathF.Cos(angle) * dist, y, MathF.Sin(angle) * dist);
                SysVec3 fwd = new SysVec3(MathF.Cos(angle), 0.14f - i * 0.02f, MathF.Sin(angle));
                fwd = SysVec3.Normalize(fwd);
                SysVec3 right = SysVec3.Normalize(SysVec3.Cross(SysVec3.UnitY, fwd));
                SysVec3 up = SysVec3.Normalize(SysVec3.Cross(fwd, right));

                byte leafMat = (i & 1) == 0 ? MaterialIdCatalog.Grass : MaterialIdCatalog.GlowingMoss;
                EmitSingleDiamondLeaf(center, right, up, fwd, 0.084f, 0.138f, leafMat, temp, outTris);
            }
        }

        private static void EmitSingleDiamondLeaf(
            SysVec3 center, SysVec3 right, SysVec3 up, SysVec3 fwd,
            float halfW, float halfL, byte mat, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 stem = center - fwd * halfL;
            SysVec3 tip = center + fwd * halfL - up * 0.008f;
            SysVec3 midTop = center + up * 0.007f;
            SysVec3 midBot = center - up * 0.005f;
            SysVec3 sideL = center - right * halfW;
            SysVec3 sideR = center + right * halfW;

            AddTri(stem, sideL, midTop, mat, temp, 215, outTris);
            AddTri(sideL, tip, midTop, mat, temp, 210, outTris);
            AddTri(stem, midTop, sideR, mat, temp, 200, outTris);
            AddTri(midTop, tip, sideR, mat, temp, 195, outTris);

            AddTri(stem, midBot, sideL, mat, temp, 155, outTris);
            AddTri(sideL, midBot, tip, mat, temp, 150, outTris);
            AddTri(stem, sideR, midBot, mat, temp, 155, outTris);
            AddTri(midBot, sideR, tip, mat, temp, 150, outTris);
        }

        private static void EmitBraidedFiberCoil(uint seed, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            const int segments = 8;
            float ringR = 0.092f + Rand01(ref seed) * 0.014f;
            float strandR = 0.024f;
            for (int s = 0; s < segments; s++)
            {
                float a0 = (s / (float)segments) * MathF.PI * 2f;
                float a1 = ((s + 1) / (float)segments) * MathF.PI * 2f;
                float wave0 = MathF.Sin(a0 * 3f) * 0.008f;
                float wave1 = MathF.Sin(a1 * 3f) * 0.008f;
                SysVec3 p0 = new SysVec3(MathF.Cos(a0) * ringR, wave0, MathF.Sin(a0) * ringR);
                SysVec3 p1 = new SysVec3(MathF.Cos(a1) * ringR, wave1, MathF.Sin(a1) * ringR);
                byte mat = (s & 1) == 0 ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.ThatchTile;
                EmitHollowTubeSegment(p0, p1, strandR, strandR, mat, temp, outTris, sides: 5);
            }
        }

        private static void EmitIrregularRiverCobble(
            uint seed, byte matId, byte temp, float rx, float ry, float rz, List<FacetedVoxelTriangle> outTris)
        {
            EmitDeformedOctaSphere(seed, SysVec3.Zero, rx, ry, rz, flattenBottom: 0.55f, sharpnessJitter: 0.24f, matId, temp, outTris);
        }

        private static void EmitSharpFlintCore(uint seed, byte matId, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            float len = 0.128f + Rand01(ref seed) * 0.026f;
            float wid = 0.088f + Rand01(ref seed) * 0.020f;
            float hgt = 0.056f + Rand01(ref seed) * 0.014f;

            SysVec3 tipF = new SysVec3((Rand01(ref seed) - 0.5f) * 0.02f, -0.005f, len);
            SysVec3 buttB = new SysVec3(0f, 0f, -len * 0.85f);
            SysVec3 ridgeTop = new SysVec3((Rand01(ref seed) - 0.5f) * 0.018f, hgt, 0.01f);
            SysVec3 keelBot = new SysVec3(0f, -hgt * 0.65f, -0.01f);
            SysVec3 edgeL = new SysVec3(-wid, -0.004f, 0.015f);
            SysVec3 edgeR = new SysVec3(wid, -0.004f, -0.010f);

            AddTri(buttB, edgeL, ridgeTop, matId, temp, 225, outTris);
            AddTri(edgeL, tipF, ridgeTop, matId, temp, 235, outTris);
            AddTri(buttB, ridgeTop, edgeR, matId, temp, 195, outTris);
            AddTri(ridgeTop, tipF, edgeR, matId, temp, 205, outTris);

            AddTri(buttB, keelBot, edgeL, matId, temp, 145, outTris);
            AddTri(edgeL, keelBot, tipF, matId, temp, 150, outTris);
            AddTri(buttB, edgeR, keelBot, matId, temp, 145, outTris);
            AddTri(keelBot, edgeR, tipF, matId, temp, 150, outTris);
        }

        private static void EmitQuartzPrismCluster(uint seed, byte matId, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            EmitDeformedOctaSphere(seed, new SysVec3(0f, -0.055f, 0f), 0.075f, 0.032f, 0.070f, 0.7f, 0.18f, MaterialIdCatalog.Stone, temp, outTris);
            int spires = 3 + (int)(seed & 1u);
            for (int i = 0; i < spires; i++)
            {
                float ang = i * 2.1f + Rand01(ref seed) * 0.5f;
                float tilt = i == 0 ? 0.12f : 0.42f + Rand01(ref seed) * 0.22f;
                float h = i == 0 ? 0.165f : (0.105f + Rand01(ref seed) * 0.045f);
                float rad = i == 0 ? 0.036f : 0.025f;
                SysVec3 baseP = new SysVec3(MathF.Cos(ang) * 0.025f, -0.045f, MathF.Sin(ang) * 0.025f);
                SysVec3 dir = SysVec3.Normalize(new SysVec3(MathF.Cos(ang) * tilt, 1f, MathF.Sin(ang) * tilt));
                SysVec3 midP = baseP + dir * (h * 0.72f);
                SysVec3 tipP = baseP + dir * h;
                EmitHollowTubeSegment(baseP, midP, rad, rad * 0.92f, matId, temp, outTris, sides: 6);
                EmitHollowTubeSegment(midP, tipP, rad * 0.92f, 0.003f, matId, temp, outTris, sides: 6);
            }
        }

        private static void EmitOreVeinNuggetsOnRock(uint seed, byte oreMat, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            for (int i = 0; i < 4; i++)
            {
                float ang = i * 1.57f + Rand01(ref seed) * 0.6f;
                SysVec3 offset = new SysVec3(MathF.Cos(ang) * 0.068f, 0.022f + (i & 1) * 0.028f, MathF.Sin(ang) * 0.062f);
                EmitDeformedOctaSphere(seed + (uint)(i * 97), offset, 0.038f, 0.032f, 0.036f, 0f, 0.28f, oreMat, temp, outTris);
            }
        }

        private static void EmitCrushedGravelCluster(uint seed, byte oreMat, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            for (int i = 0; i < 6; i++)
            {
                float ang = i * 1.05f + Rand01(ref seed) * 0.35f;
                float r = i == 0 ? 0f : 0.052f + (i & 1) * 0.018f;
                SysVec3 c = new SysVec3(MathF.Cos(ang) * r, -0.018f + (i == 0 ? 0.022f : 0f), MathF.Sin(ang) * r);
                EmitDeformedOctaSphere(seed + (uint)(i * 53), c, 0.034f, 0.026f, 0.032f, 0.5f, 0.30f, oreMat, temp, outTris);
            }
        }

        private static void EmitSoftEarthMound(uint seed, byte matId, byte temp, float height, List<FacetedVoxelTriangle> outTris)
        {
            EmitDeformedOctaSphere(seed, new SysVec3(0f, -0.008f, 0f), 0.142f, height, 0.138f, flattenBottom: 0.85f, sharpnessJitter: 0.18f, matId, temp, outTris);
        }

        private static void EmitOrganicMeatCut(uint seed, byte meatMat, bool showRawBone, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            EmitDeformedOctaSphere(seed, SysVec3.Zero, 0.095f, 0.042f, 0.072f, flattenBottom: 0.45f, sharpnessJitter: 0.14f, meatMat, temp, outTris);
            if (showRawBone)
            {
                EmitHollowTubeSegment(new SysVec3(-0.02f, 0.005f, 0f), new SysVec3(0.115f, 0.012f, 0.01f),
                    0.016f, 0.019f, MaterialIdCatalog.BoneIvory, temp, outTris, sides: 5);
            }
        }

        private static void EmitWildBerryCluster(uint seed, List<FacetedVoxelTriangle> outTris)
        {
            EmitSingleDiamondLeaf(new SysVec3(0f, -0.015f, 0f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, 0.055f, 0.075f, MaterialIdCatalog.Grass, 0, outTris);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.57f + Rand01(ref seed) * 0.4f;
                SysVec3 p = new SysVec3(MathF.Cos(a) * 0.026f, 0.012f + (i == 0 ? 0.015f : 0f), MathF.Sin(a) * 0.026f);
                EmitDeformedOctaSphere(seed + (uint)i, p, 0.021f, 0.020f, 0.021f, 0f, 0.08f, MaterialIdCatalog.RedDiamondGem, 0, outTris);
            }
        }

        private static void EmitRolledAnimalHide(uint seed, byte hideMat, List<FacetedVoxelTriangle> outTris)
        {
            float halfL = 0.15f + Rand01(ref seed) * 0.02f;
            EmitHollowTubeSegment(new SysVec3(0f, 0f, -halfL), new SysVec3(0f, 0f, halfL), 0.055f, 0.050f, hideMat, 0, outTris, sides: 7);
            EmitHollowTubeSegment(new SysVec3(0f, 0f, -0.02f), new SysVec3(0f, 0f, 0.02f), 0.060f, 0.060f, MaterialIdCatalog.GlowingMoss, 0, outTris, sides: 7);
        }

        private static void EmitCharcoalLumpCluster(uint seed, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.09f + Rand01(ref seed) * 0.4f;
                SysVec3 p = new SysVec3(MathF.Cos(a) * 0.038f, (i - 1) * 0.012f, MathF.Sin(a) * 0.038f);
                SysVec3 dir = SysVec3.Normalize(new SysVec3(MathF.Cos(a + 1.1f), 0.25f, MathF.Sin(a + 1.1f)));
                EmitHollowTubeSegment(p - dir * 0.065f, p + dir * 0.065f, 0.030f, 0.026f, MaterialIdCatalog.Coal, temp, outTris, sides: 5);
            }
        }

        private static void EmitLeatherBellowsModel(byte temp, List<FacetedVoxelTriangle> outTris)
        {
            EmitOrientedBox(new SysVec3(0f, -0.025f, -0.03f), new SysVec3(0.085f, 0.012f, 0.13f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.OakWood, temp, outTris);
            EmitOrientedBox(new SysVec3(0f, 0.030f, -0.03f), new SysVec3(0.085f, 0.012f, 0.13f), SysVec3.UnitX, SysVec3.Normalize(new SysVec3(0f, 1f, -0.15f)), SysVec3.Normalize(new SysVec3(0f, 0.15f, 1f)), MaterialIdCatalog.OakWood, temp, outTris);
            EmitHollowTubeSegment(new SysVec3(0f, -0.015f, -0.11f), new SysVec3(0f, 0.022f, 0.06f), 0.068f, 0.045f, MaterialIdCatalog.LeatherPadding, temp, outTris, sides: 6);
            EmitHollowTubeSegment(new SysVec3(0f, 0f, 0.08f), new SysVec3(0f, 0f, 0.19f), 0.024f, 0.014f, MaterialIdCatalog.Bronze, temp, outTris, sides: 6);
        }

        private static void EmitWoodenWheelModel(byte woodMat, List<FacetedVoxelTriangle> outTris)
        {
            const int segs = 8;
            const float r = 0.18f;
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i / (float)segs) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)segs) * MathF.PI * 2f;
                SysVec3 p0 = new SysVec3(0f, MathF.Sin(a0) * r, MathF.Cos(a0) * r);
                SysVec3 p1 = new SysVec3(0f, MathF.Sin(a1) * r, MathF.Cos(a1) * r);
                EmitHollowTubeSegment(p0, p1, 0.024f, 0.024f, woodMat, 0, outTris, sides: 5);
            }
            EmitHollowTubeSegment(new SysVec3(0f, -r, 0f), new SysVec3(0f, r, 0f), 0.016f, 0.016f, woodMat, 0, outTris, sides: 4);
            EmitHollowTubeSegment(new SysVec3(0f, 0f, -r), new SysVec3(0f, 0f, r), 0.016f, 0.016f, woodMat, 0, outTris, sides: 4);
        }

        private static void EmitWildBushModel(uint seed, bool bareRootsOnly, bool hasBerries, List<FacetedVoxelTriangle> outTris)
        {
            int stems = 5 + (int)(seed % 3u);
            for (int i = 0; i < stems; i++)
            {
                float ang = (i / (float)stems) * MathF.PI * 2f + Rand01(ref seed) * 0.45f;
                float reach = bareRootsOnly ? 0.24f : (0.38f + Rand01(ref seed) * 0.16f);
                float topY = bareRootsOnly ? 0.22f : (0.52f + Rand01(ref seed) * 0.24f);
                SysVec3 baseP = new SysVec3(MathF.Cos(ang) * 0.05f, 0f, MathF.Sin(ang) * 0.05f);
                SysVec3 tipP = new SysVec3(MathF.Cos(ang) * reach, topY, MathF.Sin(ang) * reach);
                EmitHollowTubeSegment(baseP, tipP, 0.026f, 0.012f, MaterialIdCatalog.OakWood, 0, outTris, sides: 5);

                if (!bareRootsOnly)
                {
                    byte foliageMat = (i & 1) == 0 ? MaterialIdCatalog.Grass : MaterialIdCatalog.GlowingMoss;
                    EmitDeformedOctaSphere(seed + (uint)(i * 31), tipP, 0.22f, 0.16f, 0.22f, 0.15f, 0.22f, foliageMat, 0, outTris);
                    if (hasBerries && (i & 1) == 0)
                    {
                        SysVec3 berryPos = tipP + new SysVec3(MathF.Cos(ang) * 0.14f, 0.04f, MathF.Sin(ang) * 0.14f);
                        EmitDeformedOctaSphere(seed + (uint)(i * 71), berryPos, 0.045f, 0.042f, 0.045f, 0f, 0.10f, MaterialIdCatalog.RedDiamondGem, 0, outTris);
                    }
                }
            }

            if (!bareRootsOnly)
            {
                EmitDeformedOctaSphere(seed ^ 0xCC11u, new SysVec3(0f, 0.42f, 0f), 0.30f, 0.22f, 0.30f, 0.20f, 0.20f, MaterialIdCatalog.Grass, 0, outTris);
            }
        }

        private static void EmitWeatherBarometerModel(in PhysicalItemShapeDna dna, List<FacetedVoxelTriangle> outTris)
        {
            // Sekizgen mese govde + pirinc cerceve + kuvars/mermer kadran + hareketli basinc ibresi ve sabit referans ibresi
            EmitHollowTubeSegment(new SysVec3(0f, 0f, -0.045f), new SysVec3(0f, 0f, 0.015f), 0.165f, 0.155f, MaterialIdCatalog.OakWood, 0, outTris, sides: 8);
            EmitHollowTubeSegment(new SysVec3(0f, 0f, 0.015f), new SysVec3(0f, 0f, 0.040f), 0.140f, 0.132f, MaterialIdCatalog.Bronze, 0, outTris, sides: 8);
            EmitHollowTubeSegment(new SysVec3(0f, 0f, 0.036f), new SysVec3(0f, 0f, 0.044f), 0.118f, 0.116f, MaterialIdCatalog.Marble, 0, outTris, sides: 8);

            // Ust pirinc asma halkasi
            EmitHollowTubeSegment(new SysVec3(0f, 0.160f, -0.01f), new SysVec3(0f, 0.205f, -0.01f), 0.018f, 0.022f, MaterialIdCatalog.GoldOre, 0, outTris, sides: 6);

            // Basinc ibresi acisi (StageByte: 0..255 -> -125 deg .. +125 deg)
            float norm01 = dna.StageByte == 0 ? 0.52f : (dna.StageByte / 255f);
            float needleAngle = (norm01 - 0.5f) * 2.18f;
            SysVec3 needleDir = new SysVec3(MathF.Sin(needleAngle), MathF.Cos(needleAngle), 0f);
            EmitHollowTubeSegment(new SysVec3(0f, 0f, 0.046f), new SysVec3(0f, 0f, 0.046f) + needleDir * 0.098f, 0.009f, 0.003f, MaterialIdCatalog.Steel, 0, outTris, sides: 4);

            // Sabit referans ibresi (pirinc)
            SysVec3 refDir = SysVec3.Normalize(new SysVec3(0.32f, 0.94f, 0f));
            EmitHollowTubeSegment(new SysVec3(0f, 0f, 0.049f), new SysVec3(0f, 0f, 0.049f) + refDir * 0.088f, 0.007f, 0.003f, MaterialIdCatalog.GoldOre, 0, outTris, sides: 4);
        }

        private static void EmitWeatherThermometerModel(in PhysicalItemShapeDna dna, List<FacetedVoxelTriangle> outTris)
        {
            // Dikey ahsap plaka + kuvars kapiler tup + alt hazne + sicaklikla yukselen kirmizi sivi sutunu
            EmitOrientedBox(new SysVec3(0f, 0f, -0.018f), new SysVec3(0.062f, 0.215f, 0.016f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.OakWood, 0, outTris);
            EmitHollowTubeSegment(new SysVec3(0f, -0.145f, 0.008f), new SysVec3(0f, 0.175f, 0.008f), 0.016f, 0.016f, MaterialIdCatalog.QuartzGem, 0, outTris, sides: 6);
            EmitDeformedOctaSphere(0x71E201u, new SysVec3(0f, -0.158f, 0.012f), 0.028f, 0.028f, 0.026f, 0f, 0.04f, MaterialIdCatalog.RubyGem, 45, outTris);

            float fill01 = dna.StageByte == 0 ? 0.55f : Math.Clamp(dna.StageByte / 255f, 0.08f, 1.0f);
            float colTopY = -0.135f + fill01 * 0.285f;
            EmitHollowTubeSegment(new SysVec3(0f, -0.145f, 0.014f), new SysVec3(0f, colTopY, 0.014f), 0.010f, 0.010f, MaterialIdCatalog.RedDiamondGem, 60, outTris, sides: 5);

            // Pirinc olcek cizgileri (Min / Donma 0C / Maks)
            EmitOrientedBox(new SysVec3(0.032f, -0.06f, 0.004f), new SysVec3(0.018f, 0.004f, 0.005f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.Bronze, 0, outTris);
            EmitOrientedBox(new SysVec3(0.032f, 0.02f, 0.004f), new SysVec3(0.022f, 0.004f, 0.005f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.GoldOre, 0, outTris);
            EmitOrientedBox(new SysVec3(0.032f, 0.11f, 0.004f), new SysVec3(0.018f, 0.004f, 0.005f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.Bronze, 0, outTris);
        }

        private static void EmitWeatherHygrometerModel(in PhysicalItemShapeDna dna, List<FacetedVoxelTriangle> outTris)
        {
            // Kuru ve Islak Hazneli Cift Tup Psikrometre + Alt Su Haznesi + Ust LCL Kadrani
            EmitOrientedBox(new SysVec3(0f, 0f, -0.020f), new SysVec3(0.095f, 0.210f, 0.018f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.OakWood, 0, outTris);

            // Sol tup: Kuru hazne (Dry Bulb)
            EmitHollowTubeSegment(new SysVec3(-0.042f, -0.120f, 0.008f), new SysVec3(-0.042f, 0.145f, 0.008f), 0.014f, 0.014f, MaterialIdCatalog.QuartzGem, 0, outTris, sides: 6);
            EmitDeformedOctaSphere(0x8811u, new SysVec3(-0.042f, -0.135f, 0.010f), 0.022f, 0.022f, 0.022f, 0f, 0.04f, MaterialIdCatalog.RubyGem, 30, outTris);

            // Sag tup: Islak hazne (Wet Bulb) + islak pamuk fitil + su canagi
            EmitHollowTubeSegment(new SysVec3(0.042f, -0.120f, 0.008f), new SysVec3(0.042f, 0.145f, 0.008f), 0.014f, 0.014f, MaterialIdCatalog.SapphireGem, 0, outTris, sides: 6);
            EmitHollowTubeSegment(new SysVec3(0.042f, -0.165f, 0.010f), new SysVec3(0.042f, -0.115f, 0.010f), 0.018f, 0.016f, MaterialIdCatalog.GlowingMoss, 0, outTris, sides: 6);
            EmitHollowTubeSegment(new SysVec3(0.042f, -0.195f, 0.012f), new SysVec3(0.042f, -0.155f, 0.012f), 0.032f, 0.036f, MaterialIdCatalog.Clay, 0, outTris, sides: 6);

            // Ortada LCL / %RH pirinc ibre skalasi
            float rh01 = dna.StageByte == 0 ? 0.60f : (dna.StageByte / 255f);
            float indY = -0.08f + rh01 * 0.18f;
            EmitOrientedBox(new SysVec3(0f, indY, 0.014f), new SysVec3(0.026f, 0.008f, 0.008f), SysVec3.UnitX, SysVec3.UnitY, SysVec3.UnitZ, MaterialIdCatalog.Bronze, 0, outTris);
        }

        private static void EmitWeatherWindVaneAnemometerModel(in PhysicalItemShapeDna dna, List<FacetedVoxelTriangle> outTris)
        {
            // Dikey demir direk + sabit K-D-G-B yon kollari + donen ruzgar gulu oku + 3 kepceli anemometre carki
            EmitHollowTubeSegment(new SysVec3(0f, -0.26f, 0f), new SysVec3(0f, 0.26f, 0f), 0.020f, 0.016f, MaterialIdCatalog.Steel, 0, outTris, sides: 6);
            EmitHollowTubeSegment(new SysVec3(-0.14f, -0.06f, 0f), new SysVec3(0.14f, -0.06f, 0f), 0.008f, 0.008f, MaterialIdCatalog.Bronze, 0, outTris, sides: 4);
            EmitHollowTubeSegment(new SysVec3(0f, -0.06f, -0.14f), new SysVec3(0f, -0.06f, 0.14f), 0.008f, 0.008f, MaterialIdCatalog.Bronze, 0, outTris, sides: 4);

            // Ruzgar gulu oku (ThermalTempByte veya StageByte ile yaw acisi)
            float vaneYaw = (dna.ThermalTempByte / 255f) * MathF.PI * 2f;
            SysVec3 vaneFwd = new SysVec3(MathF.Sin(vaneYaw), 0f, MathF.Cos(vaneYaw));
            SysVec3 vaneRight = new SysVec3(vaneFwd.Z, 0f, -vaneFwd.X);
            EmitHollowTubeSegment(new SysVec3(0f, 0.08f, 0f) - vaneFwd * 0.16f, new SysVec3(0f, 0.08f, 0f) + vaneFwd * 0.16f, 0.010f, 0.008f, MaterialIdCatalog.CopperOre, 0, outTris, sides: 5);
            // Ok ucu ve kuyruk kanadi
            EmitHollowTubeSegment(new SysVec3(0f, 0.08f, 0f) + vaneFwd * 0.16f, new SysVec3(0f, 0.08f, 0f) + vaneFwd * 0.22f, 0.028f, 0.002f, MaterialIdCatalog.GoldOre, 0, outTris, sides: 4);
            EmitOrientedBox(new SysVec3(0f, 0.09f, 0f) - vaneFwd * 0.15f, new SysVec3(0.006f, 0.042f, 0.045f), vaneRight, SysVec3.UnitY, vaneFwd, MaterialIdCatalog.CopperOre, 0, outTris);

            // Ustte 3 kollu kepceli anemometre rotor carki
            float rotorPhase = (dna.StageByte / 255f) * MathF.PI * 2f;
            for (int c = 0; c < 3; c++)
            {
                float ang = rotorPhase + c * (MathF.PI * 2f / 3f);
                SysVec3 armDir = new SysVec3(MathF.Cos(ang), 0f, MathF.Sin(ang));
                SysVec3 cupCenter = new SysVec3(0f, 0.22f, 0f) + armDir * 0.13f;
                EmitHollowTubeSegment(new SysVec3(0f, 0.22f, 0f), cupCenter, 0.007f, 0.007f, MaterialIdCatalog.Steel, 0, outTris, sides: 4);
                EmitDeformedOctaSphere(0xA4E0u + (uint)c, cupCenter, 0.028f, 0.026f, 0.028f, 0.35f, 0.05f, MaterialIdCatalog.Bronze, 0, outTris);
            }
        }

        private static void EmitWeatherRainGaugeModel(in PhysicalItemShapeDna dna, List<FacetedVoxelTriangle> outTris)
        {
            // Tas/kil kaide + seffaf kuvars olcek silindiri + ust pirinc toplama hunisi + yukselen tatli su sutunu
            EmitHollowTubeSegment(new SysVec3(0f, -0.20f, 0f), new SysVec3(0f, -0.14f, 0f), 0.115f, 0.095f, MaterialIdCatalog.Stone, 0, outTris, sides: 7);
            EmitHollowTubeSegment(new SysVec3(0f, -0.14f, 0f), new SysVec3(0f, 0.14f, 0f), 0.068f, 0.068f, MaterialIdCatalog.QuartzGem, 0, outTris, sides: 8);
            EmitHollowTubeSegment(new SysVec3(0f, 0.14f, 0f), new SysVec3(0f, 0.21f, 0f), 0.068f, 0.112f, MaterialIdCatalog.Bronze, 0, outTris, sides: 8);

            // Iceride biriken yagmur suyu sutunu
            float waterLevel01 = dna.StageByte == 0 ? 0.35f : Math.Clamp(dna.StageByte / 255f, 0.05f, 1.0f);
            float waterTopY = -0.13f + waterLevel01 * 0.25f;
            EmitHollowTubeSegment(new SysVec3(0f, -0.135f, 0f), new SysVec3(0f, waterTopY, 0f), 0.052f, 0.052f, MaterialIdCatalog.SapphireGem, 0, outTris, sides: 7);
        }

        private static void EmitMetalLightningRodModel(in PhysicalItemShapeDna dna, List<FacetedVoxelTriangle> outTris)
        {
            // Yalitkan tas kaide + dovme demir bilezik + iletken bakir direk + 3 catalli altin/bakir korona yakalayici ucu
            EmitHollowTubeSegment(new SysVec3(0f, -0.34f, 0f), new SysVec3(0f, -0.24f, 0f), 0.110f, 0.088f, MaterialIdCatalog.StoneBrick, 0, outTris, sides: 6);
            EmitHollowTubeSegment(new SysVec3(0f, -0.24f, 0f), new SysVec3(0f, -0.16f, 0f), 0.048f, 0.040f, MaterialIdCatalog.Steel, 0, outTris, sides: 6);
            EmitHollowTubeSegment(new SysVec3(0f, -0.16f, 0f), new SysVec3(0f, 0.26f, 0f), 0.028f, 0.018f, MaterialIdCatalog.CopperOre, dna.ThermalTempByte, outTris, sides: 6);

            // Orta rezonans bobin halkasi
            EmitHollowTubeSegment(new SysVec3(0f, 0.04f, 0f), new SysVec3(0f, 0.09f, 0f), 0.046f, 0.046f, MaterialIdCatalog.Bronze, dna.ThermalTempByte, outTris, sides: 6);

            // Ust 3 catalli paratoner yakalayici tac (Franklin finial)
            EmitHollowTubeSegment(new SysVec3(0f, 0.26f, 0f), new SysVec3(0f, 0.38f, 0f), 0.018f, 0.002f, MaterialIdCatalog.GoldOre, dna.ThermalTempByte, outTris, sides: 5);
            EmitHollowTubeSegment(new SysVec3(0f, 0.25f, 0f), new SysVec3(-0.065f, 0.34f, 0f), 0.012f, 0.002f, MaterialIdCatalog.GoldOre, dna.ThermalTempByte, outTris, sides: 4);
            EmitHollowTubeSegment(new SysVec3(0f, 0.25f, 0f), new SysVec3(0.065f, 0.34f, 0f), 0.012f, 0.002f, MaterialIdCatalog.GoldOre, dna.ThermalTempByte, outTris, sides: 4);
        }

        private static void EmitDeformedOctaSphere(
            uint seed, SysVec3 center, float rx, float ry, float rz,
            float flattenBottom, float sharpnessJitter, byte matId, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            const int rings = 3;
            const int sectors = 6;
            Span<SysVec3> pts = stackalloc SysVec3[(rings + 1) * sectors];

            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float phi = (v - 0.5f) * MathF.PI;
                float cosPhi = MathF.Cos(phi);
                float sinPhi = MathF.Sin(phi);

                for (int s = 0; s < sectors; s++)
                {
                    float theta = (s / (float)sectors) * MathF.PI * 2f;
                    uint h = PhysicalItemShapeDna.MixHash(seed + (uint)(r * 37 + s * 101));
                    float radialJit = (r == 0 || r == rings) ? 1f : (1f + (((h & 0xFFu) / 255f) - 0.5f) * 2f * sharpnessJitter);

                    float x = MathF.Cos(theta) * cosPhi * rx * radialJit;
                    float y = sinPhi * ry * radialJit;
                    float z = MathF.Sin(theta) * cosPhi * rz * radialJit;
                    if (y < 0f && flattenBottom > 0f)
                    {
                        y *= (1f - flattenBottom * 0.65f);
                    }
                    pts[r * sectors + s] = center + new SysVec3(x, y, z);
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sectors; s++)
                {
                    int nextS = (s + 1) % sectors;
                    SysVec3 p00 = pts[r * sectors + s];
                    SysVec3 p10 = pts[r * sectors + nextS];
                    SysVec3 p01 = pts[(r + 1) * sectors + s];
                    SysVec3 p11 = pts[(r + 1) * sectors + nextS];

                    byte shade = (byte)Math.Clamp(150 + r * 28, 120, 235);
                    if (r == 0)
                    {
                        AddTri(p00, p01, p11, matId, temp, shade, outTris);
                    }
                    else if (r == rings - 1)
                    {
                        AddTri(p00, p01, p10, matId, temp, shade, outTris);
                    }
                    else
                    {
                        AddQuad(p00, p01, p11, p10, matId, temp, shade, outTris);
                    }
                }
            }
        }

        private static void EmitHollowTubeSegment(
            SysVec3 p0, SysVec3 p1, float r0, float r1,
            byte matId, byte temp, List<FacetedVoxelTriangle> outTris, int sides = 6)
        {
            SysVec3 axis = p1 - p0;
            float len = axis.Length();
            if (len < 0.0005f) return;
            SysVec3 fwd = axis / len;
            SysVec3 refUp = MathF.Abs(fwd.Y) > 0.92f ? SysVec3.UnitX : SysVec3.UnitY;
            SysVec3 right = SysVec3.Normalize(SysVec3.Cross(refUp, fwd));
            SysVec3 up = SysVec3.Normalize(SysVec3.Cross(fwd, right));

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                SysVec3 dir0 = right * MathF.Cos(a0) + up * MathF.Sin(a0);
                SysVec3 dir1 = right * MathF.Cos(a1) + up * MathF.Sin(a1);

                SysVec3 v00 = p0 + dir0 * r0;
                SysVec3 v10 = p0 + dir1 * r0;
                SysVec3 v01 = p1 + dir0 * r1;
                SysVec3 v11 = p1 + dir1 * r1;

                AddQuad(v00, v01, v11, v10, matId, temp, 195, outTris);
                AddTri(p0, v00, v10, matId, temp, 155, outTris);
                AddTri(p1, v11, v01, matId, temp, 210, outTris);
            }
        }

        private static void EmitOrientedBox(
            SysVec3 center, SysVec3 halfExtents, SysVec3 axisX, SysVec3 axisY, SysVec3 axisZ,
            byte matId, byte temp, List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 ex = axisX * halfExtents.X;
            SysVec3 ey = axisY * halfExtents.Y;
            SysVec3 ez = axisZ * halfExtents.Z;

            SysVec3 b0 = center - ex - ey - ez;
            SysVec3 b1 = center + ex - ey - ez;
            SysVec3 b2 = center + ex - ey + ez;
            SysVec3 b3 = center - ex - ey + ez;
            SysVec3 t0 = center - ex + ey - ez;
            SysVec3 t1 = center + ex + ey - ez;
            SysVec3 t2 = center + ex + ey + ez;
            SysVec3 t3 = center - ex + ey + ez;

            AddQuad(t0, t3, t2, t1, matId, temp, 225, outTris);
            AddQuad(b0, b1, b2, b3, matId, temp, 135, outTris);
            AddQuad(b0, t0, t1, b1, matId, temp, 188, outTris);
            AddQuad(b1, t1, t2, b2, matId, temp, 180, outTris);
            AddQuad(b2, t2, t3, b3, matId, temp, 175, outTris);
            AddQuad(b3, t3, t0, b0, matId, temp, 185, outTris);
        }

        private static void AddQuad(SysVec3 v0, SysVec3 v1, SysVec3 v2, SysVec3 v3, byte matId, byte temp, byte shade, List<FacetedVoxelTriangle> outTris)
        {
            AddTri(v0, v1, v2, matId, temp, shade, outTris);
            AddTri(v0, v2, v3, matId, temp, (byte)Math.Max(100, shade - 10), outTris);
        }

        private static void AddTri(SysVec3 v0, SysVec3 v1, SysVec3 v2, byte matId, byte temp, byte shade, List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 cross = SysVec3.Cross(v1 - v0, v2 - v0);
            float lenSq = cross.LengthSquared();
            if (lenSq < 1e-12f) return;
            SysVec3 n = cross / MathF.Sqrt(lenSq);
            outTris.Add(new FacetedVoxelTriangle
            {
                V0 = v0,
                V1 = v1,
                V2 = v2,
                Normal = n,
                MaterialId = matId,
                Temperature = temp,
                FacetShade = shade,
                AmbientOcclusion = DefaultAo
            });
        }

        private static float Rand01(ref uint state)
        {
            state = PhysicalItemShapeDna.MixHash(state + 0x9E3779B9u);
            return (state & 0xFFFFu) / 65535f;
        }
    }
}
