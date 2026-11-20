#nullable enable
using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.Data;
using Stratocraft.CreaturesAI.Systems;
using Stratocraft.Voxel.Data;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class WaveACreatureAndSurvivalTests
    {
        [Test]
        public void RegionalDamage_DeductsExactSingleAmount_AndAllowsZeroHpWhenLethal()
        {
            var v = CharacterVitalsComponent.CreateDefault();
            v.CurrentHp = 40f;
            float dmg = v.TakeDamageRegional(HitZone.Torso, 15f, out _, out _, allowLethal: true);
            Assert.That(dmg, Is.EqualTo(15f).Within(0.01f));
            Assert.That(v.CurrentHp, Is.EqualTo(25f).Within(0.01f));

            // Lethal hit reaches 0 HP (no 1 HP or 20 HP floor!)
            v.TakeDamageRegional(HitZone.Torso, 50f, out _, out _, allowLethal: true);
            Assert.That(v.CurrentHp, Is.EqualTo(0f).Within(0.001f));
            Assert.That(v.IsDownedBleedOut, Is.True);

            // Non-lethal training duel stops at 1 HP
            v.CurrentHp = 10f;
            v.TakeDamageRegional(HitZone.Torso, 50f, out _, out _, allowLethal: false);
            Assert.That(v.CurrentHp, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void CreatureForwardConeHitbox_WhiffsWhenPlayerStepsAsideOrOutOfReach()
        {
            Vector3 cPos = new Vector3(0f, 10f, 0f);
            Vector3 cFwd = new Vector3(0f, 0f, 1f);

            // Directly in front within reach -> HIT
            Assert.That(CreaturePatternMemory.EvaluateForwardConeMeleeHit(cPos, cFwd, new Vector3(0f, 10f, 2.2f), 3.0f, 45f), Is.True);
            // Stepped out of reach -> WHIFF
            Assert.That(CreaturePatternMemory.EvaluateForwardConeMeleeHit(cPos, cFwd, new Vector3(0f, 10f, 3.6f), 3.0f, 45f), Is.False);
            // Dodged sideways outside cone -> WHIFF
            Assert.That(CreaturePatternMemory.EvaluateForwardConeMeleeHit(cPos, cFwd, new Vector3(2.4f, 10f, 0.2f), 3.0f, 45f), Is.False);
        }

        [Test]
        public void CreaturePatternMemory_AdaptsToRepeatedPlayerDirection_AndDirectionalGuardLeavesOppositeOpen()
        {
            var profile = CreatureCombatAiCatalog.ResolveProfile(archetypeId: 13, isFlyingBoss: false, isBiped: true, isCaveVariant: false);
            var mem = new CreaturePatternMemory();

            // Fresh creature rarely blocks on roll=0.55
            var firstReaction = mem.EvaluateDefenseReaction(in profile, CreatureAttackDirection.Right, intelligenceBonus01: 0.10f, roll01: 0.55f, out _);
            Assert.That(firstReaction, Is.EqualTo(CreatureDefenseAction.None));

            // Player spams Right 6 times -> creature learns and blocks Right at same roll!
            for (int i = 0; i < 6; i++) mem.RecordPlayerAttackDirection(CreatureAttackDirection.Right);
            var adaptedReaction = mem.EvaluateDefenseReaction(in profile, CreatureAttackDirection.Right, intelligenceBonus01: 0.10f, roll01: 0.55f, out var guardDir);
            Assert.That(adaptedReaction, Is.EqualTo(CreatureDefenseAction.DirectionalBlock));
            Assert.That(guardDir, Is.EqualTo(CreatureAttackDirection.Right));

            // Guarding Right blocks Right, but leaves Left open!
            Assert.That(CreaturePatternMemory.DoesCreatureGuardBlockAttack(guardDir, CreatureAttackDirection.Right), Is.True);
            Assert.That(CreaturePatternMemory.DoesCreatureGuardBlockAttack(guardDir, CreatureAttackDirection.Left), Is.False);
        }

        [Test]
        public void DistanceDifficultyScaling_IncreasesPowerAndIntelligenceFarFromOrigin()
        {
            CreatureCombatAiCatalog.ComputeDistanceDifficultyMultipliers(20f, 15f, out float nearPwr, out float nearIntel);
            CreatureCombatAiCatalog.ComputeDistanceDifficultyMultipliers(900f, 600f, out float farPwr, out float farIntel);

            Assert.That(farPwr, Is.GreaterThan(nearPwr + 0.5f));
            Assert.That(farIntel, Is.GreaterThan(nearIntel + 0.2f));
        }

        [Test]
        public void BedSpawnPointService_RespawnsAtIntactBed_AndFallsBackToOriginWhenBroken()
        {
            var svc = new BedSpawnPointService();
            var origin = new Vector3(0.5f, 98f, 0.5f);
            svc.SetBedSpawnPoint(12, 96, -8);

            uint intactBed = VoxelBitUtility.PackStandard(MaterialIdCatalog.BedBlock, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0);
            Vector3 pIntact = svc.ResolveRespawnPosition(
                (x, y, z) => (x == 12 && y == 96 && z == -8) ? intactBed : 0u,
                origin, out bool usedBed, out _);

            Assert.That(usedBed, Is.True);
            Assert.That(pIntact.X, Is.EqualTo(12.5f).Within(0.01f));
            Assert.That(pIntact.Y, Is.EqualTo(97.15f).Within(0.01f));

            // Bed broken -> falls back to world origin and clears saved bed
            Vector3 pBroken = svc.ResolveRespawnPosition((_, _, _) => 0u, origin, out bool usedAfterBreak, out _);
            Assert.That(usedAfterBreak, Is.False);
            Assert.That(pBroken, Is.EqualTo(origin));
            Assert.That(svc.HasBedSpawn, Is.False);
        }

        [Test]
        public void SurvivalMetabolismBleedOutAndBandage_WorkAtBalancedPace()
        {
            var v = CharacterVitalsComponent.CreateDefault();
            v.HungerKcal = 2500f;
            v.ThirstHydration = 100f;

            // Simulate 10 minutes (600s) of normal survival
            for (int s = 0; s < 600; s++)
            {
                ThermalAndBiologySystem.StepSurvivalMetabolismAndBleedOut(ref v, 1.0f, isCreativeMode: false, isSprintingOrCombat: false);
            }
            // ~35 min hunger -> after 10 min should be ~1810 kcal; ~23 min thirst -> after 10 min should be ~56.8%
            Assert.That(v.HungerKcal, Is.InRange(1700f, 1950f));
            Assert.That(v.ThirstHydration, Is.InRange(45f, 65f));

            // Heavy bleeding cleared by bandage
            ThermalAndBiologySystem.ApplyRegionalBleeding(ref v, 2.0f);
            Assert.That((v.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0, Is.True);
            ThermalAndBiologySystem.ClearHeavyBleeding(ref v);
            Assert.That((v.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.HeavyBleeding) != 0, Is.False);

            // 0 HP -> 30s bleed out reaches IsDead
            v.CurrentHp = 0f;
            v.BleedOutTimerSeconds = 30.0f;
            ThermalAndBiologySystem.StepSurvivalMetabolismAndBleedOut(ref v, 31.0f, isCreativeMode: false);
            Assert.That(v.IsDead, Is.True);
        }
    }
}
