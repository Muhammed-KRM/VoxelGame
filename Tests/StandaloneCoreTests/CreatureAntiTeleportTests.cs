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

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class CreatureAntiTeleportTests
    {
        [Test]
        public void Patrol_AggroChase_EvadeDodge_Flee_Patrol_HasZeroFrameToFrameTeleportJumps()
        {
            const float dt = 1.0f / 60.0f;
            var startSpawn = new Vector3(82.0f, 96.0f, 44.0f);
            var agent = new Stage1CreatureSimulationBridge.AntiTeleportKinematicAgent
            {
                ArchetypeId = 3, // Ulu Kurt (Direwolf)
                Capabilities = Creature21BehaviorSystem.ResolveInitialCapabilities(3),
                State = Stage1CreatureSimulationBridge.CreatureAiState.Patrol,
                Position = startSpawn,
                AnchorPos = startSpawn,
                BaseSpeed = 5.6f,
                SpeedMultiplier = 1.0f,
                MaxHp = 230f,
                CurrentHp = 230f,
                PhaseOffset = 1.35f,
                IsFlying = false,
                IsBiped = false,
                HasBeenProvoked = true
            };

            static float SampleGentleTerrain(float wx, float wz) =>
                96.0f + MathF.Sin(wx * 0.04f) * 0.6f + MathF.Cos(wz * 0.04f) * 0.6f;

            agent.Position.Y = SampleGentleTerrain(agent.Position.X, agent.Position.Z);
            agent.AnchorPos = agent.Position;

            bool sawPatrolInitial = false;
            bool sawAggroChase = false;
            bool sawEvadeDodge = false;
            bool sawFlee = false;
            bool sawPatrolReturn = false;
            float maxSingleFrameStepMeters = 0f;

            var playerFwd = new Vector3(0f, 0f, -1f);
            float simTime = 0f;

            // Phase 1 (0..1.5s): Player far away (60m) -> Creature patrols smoothly without snapping to Anchor + offset
            for (int f = 0; f < 90; f++)
            {
                Vector3 prev = agent.Position;
                simTime += dt;
                Stage1CreatureSimulationBridge.StepAntiTeleportKinematicAgent(
                    ref agent,
                    new Vector3(145.0f, 96.0f, 110.0f),
                    playerFwd,
                    simTime,
                    dt,
                    SampleGentleTerrain);

                if (agent.State == Stage1CreatureSimulationBridge.CreatureAiState.Patrol) sawPatrolInitial = true;
                float jump = Vector3.Distance(prev, agent.Position);
                if (jump > maxSingleFrameStepMeters) maxSingleFrameStepMeters = jump;
                Assert.That(jump, Is.LessThanOrEqualTo(0.25f), $"Phase 1 Patrol frame {f} jumped {jump:F3}m!");
            }

            // Phase 2 (1.5..6.0s): Player lures creature > 20m away from original spawn via AggroChase + mid-fight EvadeDodge
            for (int f = 0; f < 270; f++)
            {
                Vector3 prev = agent.Position;
                simTime += dt;
                float lureProgress = f / 270.0f;
                var movingPlayer = Vector3.Lerp(
                    new Vector3(90.0f, 96.0f, 50.0f),
                    new Vector3(108.0f, 96.0f, 64.0f),
                    lureProgress);

                if (f == 120)
                {
                    agent.DodgeTimer = 0.26f;
                    agent.DodgeDirXZ = new Vector3(1f, 0f, -0.5f);
                }

                Stage1CreatureSimulationBridge.StepAntiTeleportKinematicAgent(
                    ref agent,
                    movingPlayer,
                    playerFwd,
                    simTime,
                    dt,
                    SampleGentleTerrain);

                if (agent.State == Stage1CreatureSimulationBridge.CreatureAiState.AggroChase) sawAggroChase = true;
                if (agent.State == Stage1CreatureSimulationBridge.CreatureAiState.EvadeDodge) sawEvadeDodge = true;

                float jump = Vector3.Distance(prev, agent.Position);
                if (jump > maxSingleFrameStepMeters) maxSingleFrameStepMeters = jump;
                Assert.That(jump, Is.LessThanOrEqualTo(0.25f), $"Phase 2 Chase/Dodge frame {f} jumped {jump:F3}m!");
            }

            Assert.That(Vector2.Distance(new Vector2(startSpawn.X, startSpawn.Z), new Vector2(agent.Position.X, agent.Position.Z)),
                Is.GreaterThan(18.0f), "Creature must have chased >18m away from initial spawn.");

            // Phase 3 (6.0..8.5s): Creature drops below 32% HP -> transitions to wounded Flee
            agent.CurrentHp = agent.MaxHp * 0.22f;
            Vector3 fleeThreatPlayer = agent.Position + new Vector3(-4.0f, 0f, -3.0f);
            for (int f = 0; f < 150; f++)
            {
                Vector3 prev = agent.Position;
                simTime += dt;
                Stage1CreatureSimulationBridge.StepAntiTeleportKinematicAgent(
                    ref agent,
                    fleeThreatPlayer,
                    playerFwd,
                    simTime,
                    dt,
                    SampleGentleTerrain);

                if (agent.State == Stage1CreatureSimulationBridge.CreatureAiState.Flee) sawFlee = true;
                float jump = Vector3.Distance(prev, agent.Position);
                if (jump > maxSingleFrameStepMeters) maxSingleFrameStepMeters = jump;
                Assert.That(jump, Is.LessThanOrEqualTo(0.25f), $"Phase 3 Flee frame {f} jumped {jump:F3}m!");
            }

            Vector3 postFleePos = agent.Position;
            Assert.That(Vector3.Distance(agent.AnchorPos, postFleePos), Is.LessThan(0.01f),
                "AnchorPos must continuously track Root.position during Chase/Dodge/Flee!");

            // Phase 4 (8.5..11.0s): Player leaves aggro range -> Creature returns to Patrol smoothly with ZERO snap back!
            for (int f = 0; f < 150; f++)
            {
                Vector3 prev = agent.Position;
                simTime += dt;
                Stage1CreatureSimulationBridge.StepAntiTeleportKinematicAgent(
                    ref agent,
                    new Vector3(220.0f, 96.0f, 220.0f),
                    playerFwd,
                    simTime,
                    dt,
                    SampleGentleTerrain);

                if (agent.State == Stage1CreatureSimulationBridge.CreatureAiState.Patrol) sawPatrolReturn = true;
                float jump = Vector3.Distance(prev, agent.Position);
                if (jump > maxSingleFrameStepMeters) maxSingleFrameStepMeters = jump;
                Assert.That(jump, Is.LessThanOrEqualTo(0.25f), $"Phase 4 Return-to-Patrol frame {f} snapped {jump:F3}m!");
            }

            Assert.That(sawPatrolInitial, Is.True);
            Assert.That(sawAggroChase, Is.True);
            Assert.That(sawEvadeDodge, Is.True);
            Assert.That(sawFlee, Is.True);
            Assert.That(sawPatrolReturn, Is.True);
            Assert.That(Vector2.Distance(new Vector2(startSpawn.X, startSpawn.Z), new Vector2(agent.Position.X, agent.Position.Z)),
                Is.GreaterThan(18.0f), "Returning to Patrol must stay near post-flee AnchorPos, never snapping back to initial spawn!");
            Assert.That(maxSingleFrameStepMeters, Is.LessThanOrEqualTo(0.25f));
        }

        [Test]
        public void Live3DRetentionAndFallbackDemoteRadii_Prevent48mViewConeVanishPop()
        {
            Assert.That(Stage1CreatureSimulationBridge.Live3DRetentionRadiusMeters, Is.EqualTo(118.0f).Within(0.001f));
            Assert.That(Stage1CreatureSimulationBridge.FallbackSpreadsheetDemoteRadiusMeters, Is.EqualTo(128.0f).Within(0.001f));

            // Within 118m (including former 48m..117m vanish zone), live 3D creatures are retained
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                48.5f, Stage1CreatureSimulationBridge.CreatureAiState.Patrol), Is.True);
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                95.0f, Stage1CreatureSimulationBridge.CreatureAiState.Patrol), Is.True);
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                117.9f, Stage1CreatureSimulationBridge.CreatureAiState.Patrol), Is.True);

            // Beyond 118m, idle Patrol can demote, whereas active combat/flee/dodge/dash stays retained
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                122.0f, Stage1CreatureSimulationBridge.CreatureAiState.Patrol), Is.False);
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                135.0f, Stage1CreatureSimulationBridge.CreatureAiState.AggroChase), Is.True);
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                135.0f, Stage1CreatureSimulationBridge.CreatureAiState.Flee), Is.True);
            Assert.That(Stage1CreatureSimulationBridge.IsEngagedInCombatOrNear(
                135.0f, Stage1CreatureSimulationBridge.CreatureAiState.Patrol, isBlinkDashActive: true), Is.True);
        }

        [Test]
        public void CapBlinkTeleport_RestrictedToVoidPhantomOnly_AndExecutesSmooth18MpsDash()
        {
            Assert.That(Creature21BehaviorSystem.BlinkDashSpeedMetersPerSecond, Is.EqualTo(18.0f).Within(0.001f));

            // 1) Natural beasts, mounts, golems, arachnids, and starter Ash Stalker/Phoenix MUST NOT have CapBlinkTeleport
            for (byte id = 1; id <= 21; id++)
            {
                AnatomyCapabilityFlags resolved = Creature21BehaviorSystem.ResolveInitialCapabilities(id);
                if (!Creature21BehaviorSystem.IsVoidOrAstralBlinkArchetype(id))
                {
                    Assert.That((resolved & AnatomyCapabilityFlags.CapBlinkTeleport) == 0, Is.True,
                        $"Archetype #{id} ({Creature21BehaviorSystem.GetById(id).CodeName}) must NOT have CapBlinkTeleport.");
                    Assert.That(Creature21BehaviorSystem.CanUseVoidBlinkDash(
                        id, resolved | AnatomyCapabilityFlags.CapBlinkTeleport), Is.False,
                        $"Natural/non-void archetype #{id} must be blocked from VoidBlinkDash even if flag is passed.");
                }
            }

            AnatomyCapabilityFlags starterStalkerCaps = Creature21BehaviorSystem.ResolveInitialCapabilities(4, isStarterAshStalker: true);
            Assert.That((starterStalkerCaps & AnatomyCapabilityFlags.CapBlinkTeleport) == 0, Is.True);

            // 2) Void Stalker (#21) executes CapBlinkTeleport as a smooth 18 m/s multi-frame dash (zero 1-frame snap!)
            const float dt = 1.0f / 60.0f;
            float maxAllowedStepPerFrame = Creature21BehaviorSystem.BlinkDashSpeedMetersPerSecond * dt + 0.015f; // ~0.315m
            var voidAgent = new Stage1CreatureSimulationBridge.AntiTeleportKinematicAgent
            {
                ArchetypeId = 21,
                Capabilities = Creature21BehaviorSystem.ResolveInitialCapabilities(21),
                State = Stage1CreatureSimulationBridge.CreatureAiState.AggroChase,
                Position = new Vector3(0f, 50f, 0f),
                AnchorPos = new Vector3(0f, 50f, 0f),
                BaseSpeed = 8.8f,
                SpeedMultiplier = 1.0f,
                MaxHp = 10800f,
                CurrentHp = 10800f,
                HasBeenProvoked = true
            };

            var playerPos = new Vector3(0f, 50f, 12f);
            var playerFwd = new Vector3(0f, 0f, -1f);
            bool dashStarted = false;
            bool dashCompleted = false;
            int dashFrames = 0;

            for (int f = 0; f < 90; f++)
            {
                Vector3 prev = voidAgent.Position;
                Stage1CreatureSimulationBridge.StepAntiTeleportKinematicAgent(
                    ref voidAgent,
                    playerPos,
                    playerFwd,
                    f * dt,
                    dt,
                    (_, _) => 50.0f);

                float stepDist = Vector3.Distance(prev, voidAgent.Position);
                Assert.That(stepDist, Is.LessThanOrEqualTo(maxAllowedStepPerFrame),
                    $"Void blink dash frame {f} snapped {stepDist:F3}m instead of smooth 18 m/s motion!");

                if (voidAgent.IsBlinkDashActive)
                {
                    dashStarted = true;
                    dashFrames++;
                }
                else if (dashStarted && !dashCompleted)
                {
                    dashCompleted = true;
                }
            }

            Assert.That(dashStarted, Is.True, "Void Stalker should trigger smooth blink dash within closing range.");
            Assert.That(dashCompleted, Is.True, "Void Stalker blink dash should complete smoothly.");
            Assert.That(dashFrames, Is.GreaterThanOrEqualTo(15), "Dash over ~8-14m at 18 m/s must span many frames, never 1 frame!");
        }

        [Test]
        public void AllSixCreatureGlbs_ExistWithZeroTexturesValidColor0True8LegSpider_AndMapAll21Archetypes()
        {
            string repoRoot = FindRepoRoot();
            string creaturesDir = Path.Combine(repoRoot, "Assets", "Art", "Generated", "Creatures_21");
            string previewsDir = Path.Combine(repoRoot, "BuildLogs", "Previews");

            Assert.That(Stage1CreatureSimulationBridge.CanonicalCreatureGlbFileNames.Length, Is.EqualTo(6));

            foreach (string glbFileName in Stage1CreatureSimulationBridge.CanonicalCreatureGlbFileNames)
            {
                string glbPath = Path.Combine(creaturesDir, glbFileName);
                Assert.That(File.Exists(glbPath), Is.True, $"Missing generated creature rig: {glbFileName}");

                byte[] bytes = File.ReadAllBytes(glbPath);
                Assert.That(bytes.Length, Is.GreaterThan(2048), $"{glbFileName} is unexpectedly small.");
                Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(0x46546C67u), $"{glbFileName} invalid glTF magic.");
                Assert.That(BitConverter.ToUInt32(bytes, 4), Is.EqualTo(2u), $"{glbFileName} must be glTF 2.0.");

                int jsonLen = BitConverter.ToInt32(bytes, 12);
                uint jsonChunkType = BitConverter.ToUInt32(bytes, 16);
                Assert.That(jsonChunkType, Is.EqualTo(0x4E4F534Au), $"{glbFileName} missing JSON chunk.");
                string jsonText = Encoding.UTF8.GetString(bytes, 20, jsonLen);

                using JsonDocument doc = JsonDocument.Parse(jsonText);
                JsonElement root = doc.RootElement;

                // Constitutional Rule: 0 embedded textures, 0 images, UV-free COLOR_0 on all primitives
                int textureCount = root.TryGetProperty("textures", out JsonElement texArr) ? texArr.GetArrayLength() : 0;
                int imageCount = root.TryGetProperty("images", out JsonElement imgArr) ? imgArr.GetArrayLength() : 0;
                Assert.That(textureCount, Is.EqualTo(0), $"{glbFileName} must have 0 embedded textures.");
                Assert.That(imageCount, Is.EqualTo(0), $"{glbFileName} must have 0 embedded images.");

                Assert.That(root.TryGetProperty("meshes", out JsonElement meshesArr), Is.True);
                Assert.That(meshesArr.GetArrayLength(), Is.GreaterThanOrEqualTo(10),
                    $"{glbFileName} must be a multi-part articulated rig (>=10 limb meshes).");

                foreach (JsonElement meshEl in meshesArr.EnumerateArray())
                {
                    JsonElement prims = meshEl.GetProperty("primitives");
                    Assert.That(prims.GetArrayLength(), Is.GreaterThan(0));
                    foreach (JsonElement prim in prims.EnumerateArray())
                    {
                        JsonElement attrs = prim.GetProperty("attributes");
                        Assert.That(attrs.TryGetProperty("COLOR_0", out _), Is.True,
                            $"{glbFileName} primitive missing UV-free COLOR_0 vertex color attribute.");
                        Assert.That(attrs.TryGetProperty("TEXCOORD_0", out _), Is.False,
                            $"{glbFileName} must not export UV TEXCOORD_0.");
                    }
                }

                if (string.Equals(glbFileName, "creature_arachnid_stalker.glb", StringComparison.OrdinalIgnoreCase))
                {
                    var nodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonElement nodeEl in root.GetProperty("nodes").EnumerateArray())
                    {
                        if (nodeEl.TryGetProperty("name", out JsonElement nProp) && nProp.GetString() is string s)
                        {
                            nodeNames.Add(s);
                        }
                    }

                    for (int legIdx = 1; legIdx <= 4; legIdx++)
                    {
                        Assert.That(nodeNames.Contains($"Arachnid_Leg{legIdx}_L"), Is.True, $"Missing Arachnid_Leg{legIdx}_L");
                        Assert.That(nodeNames.Contains($"Arachnid_Leg{legIdx}_R"), Is.True, $"Missing Arachnid_Leg{legIdx}_R");
                    }
                }

                string assetId = Path.GetFileNameWithoutExtension(glbFileName);
                string sheetPath = Path.Combine(previewsDir, $"{assetId}_contact_sheet.png");
                Assert.That(File.Exists(sheetPath), Is.True, $"Missing 4-angle preview contact sheet for {assetId}");
                byte[] pngHeader = new byte[24];
                using (FileStream fs = File.OpenRead(sheetPath))
                {
                    int read = fs.Read(pngHeader, 0, pngHeader.Length);
                    Assert.That(read, Is.EqualTo(24));
                }
                int width = (pngHeader[16] << 24) | (pngHeader[17] << 16) | (pngHeader[18] << 8) | pngHeader[19];
                int height = (pngHeader[20] << 24) | (pngHeader[21] << 16) | (pngHeader[22] << 8) | pngHeader[23];
                Assert.That(width, Is.EqualTo(1024), $"{assetId}_contact_sheet.png should be 2x2 of 512x512 = 1024px wide.");
                Assert.That(height, Is.EqualTo(1024), $"{assetId}_contact_sheet.png should be 2x2 of 512x512 = 1024px tall.");
            }

            // Verify all 21 canonical creature archetypes + 5 cave variants map cleanly to existing .glb rigs
            var coveredRigs = new HashSet<Stage1CreatureSimulationBridge.CreatureGlbRigKind>();
            for (byte archId = 1; archId <= 21; archId++)
            {
                CreatureArchetypeDefinition def = Creature21BehaviorSystem.GetById(archId);
                bool canFly = (def.InitialCapabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                bool isBiped = !canFly && (def.InitialCapabilities & AnatomyCapabilityFlags.BipedalWalk) != 0;
                var rigKind = Stage1CreatureSimulationBridge.ResolveCreatureGlbRigKind(archId, canFly, isBiped);
                coveredRigs.Add(rigKind);
                string mappedFile = Stage1CreatureSimulationBridge.ResolveCreatureGlbFileName(rigKind);
                Assert.That(File.Exists(Path.Combine(creaturesDir, mappedFile)), Is.True);
            }

            Assert.That(coveredRigs.Count, Is.EqualTo(6), "All 6 generated creature rigs should be actively mapped across the 21 archetypes.");
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
