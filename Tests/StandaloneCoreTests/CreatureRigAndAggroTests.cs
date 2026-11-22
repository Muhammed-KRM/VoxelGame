#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.Data;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class CreatureRigAndAggroTests
    {
        [Test]
        public void ResolveCreatureGlbRigKind_MapsNightGhoulWildBoarAndRestrictsAncientGolemToConstructsOnly()
        {
            // Explicit Catalog21 regression checks (#4 Night Ghoul, #2 Wild Boar, #11 Ancient Golem)
            var ghoulRig = Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(4, isFlyingBoss: false, isBiped: true);
            Assert.That(ghoulRig, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.NightGhoul));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbFileName(ghoulRig), Is.EqualTo("creature_night_ghoul.glb"));

            var boarRig = Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(2, isFlyingBoss: false, isBiped: false);
            Assert.That(boarRig, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.WildBoar));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbFileName(boarRig), Is.EqualTo("creature_wild_boar.glb"));

            var golemRig = Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(11, isFlyingBoss: false, isBiped: true);
            Assert.That(golemRig, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.AncientGolem));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbFileName(golemRig), Is.EqualTo("creature_ancient_golem.glb"));

            // Verify all 21 Catalog21 archetypes follow the canonical anatomical mapping
            for (byte id = 1; id <= 21; id++)
            {
                CreatureArchetypeDefinition def = Creature21BehaviorSystem.GetById(id);
                bool canFly = (def.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                bool isBiped = !canFly && (def.InitialCapabilities & AnatomyCapabilityFlags.BipedalWalk) != 0;
                var resolved = Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(id, canFly, isBiped);

                if (id == 11 || id == 17 || id == 20)
                {
                    Assert.That(resolved, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.AncientGolem),
                        $"Construct archetype #{id} ({def.DisplayName}) must map to AncientGolem.");
                }
                else
                {
                    Assert.That(resolved, Is.Not.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.AncientGolem),
                        $"Organic creature #{id} ({def.DisplayName}) must NEVER use creature_ancient_golem.glb!");
                }
            }

            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(3), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.DireWolf));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(12), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.DireWolf));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(21), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.DireWolf));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(5), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.StagSteed));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(8), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.HornedBehemoth));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(10), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.HornedBehemoth));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(13, isBiped: true), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.HornedBehemoth));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(19), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.HornedBehemoth));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(9), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.ArachnidStalker));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(15), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.ArachnidStalker));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(14, isFlyingBoss: true), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.GriffinRaptor));
            Assert.That(Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(16, isFlyingBoss: true), Is.EqualTo(Stage1CreatureSimulationBridge.CreatureGlbRigKind.GriffinRaptor));
        }

        [Test]
        public void MatteOrganicMaterialPolicy_AppliesMatteParametersToOrganicBeasts_AndAllowsMetallicOnlyOnConstructs()
        {
            Assert.That(Stage1CreatureSimulationBridge.MatteOrganicMatCapBlendWeight, Is.EqualTo(0.04f).Within(0.0001f));
            Assert.That(Stage1CreatureSimulationBridge.MatteOrganicRimLightStrength, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(Stage1CreatureSimulationBridge.MatteOrganicSmoothFacetBlend, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(Stage1CreatureSimulationBridge.MatteOrganicMicroDetailStrength, Is.EqualTo(0.35f).Within(0.0001f));

            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(2), Is.True, "Wild Boar #2 must use matte fur/hide material.");
            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(4), Is.True, "Night Ghoul #4 must use matte organic skin/bone material.");
            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(3), Is.True);
            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(5), Is.True);

            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(11), Is.False);
            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(17), Is.False);
            Assert.That(Stage1CreatureSimulationBridge.UsesMatteOrganicCreatureMaterial(20), Is.False);
        }

        [Test]
        public void SculptedWildBoarAndNightGhoulGlbs_ContainExactArticulatedLimbNodesAndZeroTextures()
        {
            string repoRoot = FindRepoRoot();
            string creaturesDir = Path.Combine(repoRoot, "Assets", "Art", "Generated", "Creatures_21");

            VerifyArticulatedGlbNodes(
                Path.Combine(creaturesDir, "creature_wild_boar.glb"),
                new[]
                {
                    "Boar_Torso", "Boar_Head", "Boar_Jaw", "Boar_Tail",
                    "Boar_FrontUpper_L", "Boar_FrontLower_L", "Boar_FrontUpper_R", "Boar_FrontLower_R",
                    "Boar_HindUpper_L", "Boar_HindLower_L", "Boar_HindUpper_R", "Boar_HindLower_R"
                });

            VerifyArticulatedGlbNodes(
                Path.Combine(creaturesDir, "creature_night_ghoul.glb"),
                new[]
                {
                    "Ghoul_Torso", "Ghoul_Head", "Ghoul_Jaw",
                    "Ghoul_ArmUpper_L", "Ghoul_ArmLower_L", "Ghoul_ArmUpper_R", "Ghoul_ArmLower_R",
                    "Ghoul_LegUpper_L", "Ghoul_LegLower_L", "Ghoul_LegUpper_R", "Ghoul_LegLower_R"
                });
        }

        [Test]
        public void CreatureCombatAiCatalog_MatchesRealCatalog21Temperaments_OnlyWarHorseIsPassive()
        {
            // Explicit check for Night Ghoul (#4) and hostile raiders/predators (1, 3, 4, 6, 7, 8, 9, 10..21)
            byte[] expectedAggressiveIds =
            {
                1, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21
            };

            foreach (byte id in expectedAggressiveIds)
            {
                CreatureArchetypeDefinition def = Creature21BehaviorSystem.GetById(id);
                bool canFly = (def.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                bool isBiped = !canFly && (def.InitialCapabilities & AnatomyCapabilityFlags.BipedalWalk) != 0;
                CreatureSpeciesAiProfile profile = CreatureCombatAiCatalog.ResolveProfile(id, canFly, isBiped, isCaveVariant: false);

                Assert.That(profile.Temperament, Is.EqualTo(CreatureTemperament.Aggressive),
                    $"Catalog21 #{id} ({def.DisplayName}) must have CreatureTemperament.Aggressive!");
            }

            // #5 War Horse (Savas Ati) is the ONLY Passive mount archetype
            CreatureSpeciesAiProfile horseProfile = CreatureCombatAiCatalog.ResolveProfile(5, isFlyingBoss: false, isBiped: false, isCaveVariant: false);
            Assert.That(horseProfile.Temperament, Is.EqualTo(CreatureTemperament.Passive));

            // #2 Wild Boar (Yaban Domuzu) is territorial Neutral (never Passive!)
            CreatureSpeciesAiProfile boarProfile = CreatureCombatAiCatalog.ResolveProfile(2, isFlyingBoss: false, isBiped: false, isCaveVariant: false);
            Assert.That(boarProfile.Temperament, Is.Not.EqualTo(CreatureTemperament.Passive));
            Assert.That(boarProfile.Temperament, Is.EqualTo(CreatureTemperament.Neutral));
        }

        [Test]
        public void StepLiveCreatureStatePure_NightGhoulAt1Point7MetersUnprovoked_ImmediatelyExitsPatrolIntoAttackEvenInCreativeMode()
        {
            const float dt = 1.0f / 60.0f;
            var playerPos = new Vector3(100.0f, 96.0f, 100.0f);
            var playerFwd = new Vector3(0f, 0f, 1f);

            // 1) Night Ghoul (#4) at 1.7m unprovoked (normal survival mode) -> exits Patrol into MeleeAttack immediately!
            var ghoulNormal = new Stage1CreatureSimulationBridge.AntiTeleportKinematicAgent
            {
                ArchetypeId = 4,
                Capabilities = Creature21BehaviorSystem.ResolveInitialCapabilities(4),
                State = Stage1CreatureSimulationBridge.CreatureAiState.Patrol,
                Position = new Vector3(100.0f, 96.0f, 101.7f), // 1.7m horizontal distance!
                AnchorPos = new Vector3(100.0f, 96.0f, 101.7f),
                BaseSpeed = 5.5f,
                SpeedMultiplier = 1.0f,
                MaxHp = 250f,
                CurrentHp = 250f,
                IsBiped = true,
                HasBeenProvoked = false
            };

            Stage1CreatureSimulationBridge.StepLiveCreatureStatePure(
                ref ghoulNormal,
                playerPos,
                playerFwd,
                timeSec: 1.0f,
                deltaTime: dt,
                groundHeightSampler: (_, _) => 96.0f,
                isCreativeMode: false);

            Assert.That(ghoulNormal.State, Is.Not.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.Patrol),
                "Unprovoked Night Ghoul (#4) at 1.7m must NEVER stay in [Patrol]!");
            Assert.That(ghoulNormal.State, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.MeleeAttack));

            // 2) Night Ghoul (#4) at 1.7m & 9.5m in [F4] Creative Mode -> STILL engages immediately!
            var ghoulCreativeClose = ghoulNormal;
            ghoulCreativeClose.State = Stage1CreatureSimulationBridge.CreatureAiState.Patrol;
            Stage1CreatureSimulationBridge.StepLiveCreatureStatePure(
                ref ghoulCreativeClose,
                playerPos,
                playerFwd,
                timeSec: 1.0f,
                deltaTime: dt,
                groundHeightSampler: (_, _) => 96.0f,
                isCreativeMode: true,
                aggressionMultiplier: 0.0f);
            Assert.That(ghoulCreativeClose.State, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.MeleeAttack));

            var ghoulCreativeMid = ghoulNormal;
            ghoulCreativeMid.State = Stage1CreatureSimulationBridge.CreatureAiState.Patrol;
            ghoulCreativeMid.Position = new Vector3(100.0f, 96.0f, 111.5f); // 11.5m <= 14.0m aggroRange
            ghoulCreativeMid.AnchorPos = ghoulCreativeMid.Position;
            Stage1CreatureSimulationBridge.StepLiveCreatureStatePure(
                ref ghoulCreativeMid,
                playerPos,
                playerFwd,
                timeSec: 1.0f,
                deltaTime: dt,
                groundHeightSampler: (_, _) => 96.0f,
                isCreativeMode: true);
            Assert.That(ghoulCreativeMid.State, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.AggroChase));

            // 3) Wild Boar (#2): does NOT flee at 6.5m unprovoked, and attacks when player intrudes < 4.5m!
            var boarAgent = new Stage1CreatureSimulationBridge.AntiTeleportKinematicAgent
            {
                ArchetypeId = 2,
                Capabilities = Creature21BehaviorSystem.ResolveInitialCapabilities(2),
                State = Stage1CreatureSimulationBridge.CreatureAiState.Patrol,
                Position = new Vector3(100.0f, 96.0f, 106.5f),
                AnchorPos = new Vector3(100.0f, 96.0f, 106.5f),
                BaseSpeed = 6.2f,
                SpeedMultiplier = 1.0f,
                MaxHp = 200f,
                CurrentHp = 200f,
                HasBeenProvoked = false
            };

            Stage1CreatureSimulationBridge.StepLiveCreatureStatePure(
                ref boarAgent, playerPos, playerFwd, 1.0f, dt, (_, _) => 96.0f);
            Assert.That(boarAgent.State, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.Patrol),
                "Unprovoked Wild Boar at 6.5m must hold ground (Patrol), never flee!");

            boarAgent.Position = new Vector3(100.0f, 96.0f, 103.8f); // 3.8m < 4.5m territorial threshold
            boarAgent.AnchorPos = boarAgent.Position;
            Stage1CreatureSimulationBridge.StepLiveCreatureStatePure(
                ref boarAgent, playerPos, playerFwd, 1.0f, dt, (_, _) => 96.0f);
            Assert.That(boarAgent.State, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.AggroChase),
                "Wild Boar must charge when player intrudes within < 4.5m!");

            // 4) War Horse (#5): Passive mount flees when unprovoked within 7.5m
            var horseAgent = new Stage1CreatureSimulationBridge.AntiTeleportKinematicAgent
            {
                ArchetypeId = 5,
                Capabilities = Creature21BehaviorSystem.ResolveInitialCapabilities(5),
                State = Stage1CreatureSimulationBridge.CreatureAiState.Patrol,
                Position = new Vector3(100.0f, 96.0f, 105.0f),
                AnchorPos = new Vector3(100.0f, 96.0f, 105.0f),
                BaseSpeed = 8.5f,
                SpeedMultiplier = 1.0f,
                MaxHp = 310f,
                CurrentHp = 310f,
                HasBeenProvoked = false
            };

            Stage1CreatureSimulationBridge.StepLiveCreatureStatePure(
                ref horseAgent, playerPos, playerFwd, 1.0f, dt, (_, _) => 96.0f);
            Assert.That(horseAgent.State, Is.EqualTo(Stage1CreatureSimulationBridge.CreatureAiState.Flee));
        }

        private static void VerifyArticulatedGlbNodes(string glbPath, string[] requiredNodes)
        {
            Assert.That(File.Exists(glbPath), Is.True, $"Missing sculpted GLB: {glbPath}");
            byte[] bytes = File.ReadAllBytes(glbPath);
            Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(0x46546C67u));
            int jsonLen = BitConverter.ToInt32(bytes, 12);
            string jsonText = Encoding.UTF8.GetString(bytes, 20, jsonLen);

            using JsonDocument doc = JsonDocument.Parse(jsonText);
            JsonElement root = doc.RootElement;

            int texCount = root.TryGetProperty("textures", out JsonElement texArr) ? texArr.GetArrayLength() : 0;
            Assert.That(texCount, Is.EqualTo(0));

            var nodeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement nodeEl in root.GetProperty("nodes").EnumerateArray())
            {
                if (nodeEl.TryGetProperty("name", out JsonElement nProp) && nProp.GetString() is string s)
                {
                    nodeNames.Add(s);
                }
            }

            foreach (string req in requiredNodes)
            {
                Assert.That(nodeNames.Contains(req), Is.True, $"GLB {Path.GetFileName(glbPath)} missing articulated node '{req}'.");
            }
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "_Stratocraft")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Tools", "AutonomousAssetFactory")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
