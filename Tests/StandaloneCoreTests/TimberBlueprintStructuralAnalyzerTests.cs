using System;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Physics.RigidTimber;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class TimberBlueprintStructuralAnalyzerTests
    {
        [Test]
        public void TwoGroundedPostsWithWallLog_StandsSafeAndBlueGreen()
        {
            var nodes = new BlueprintNodeSpec[]
            {
                new()
                {
                    Id = 1, Kind = BlueprintPieceKind.Post,
                    Position = new Vector3(0f, 1.15f, 0f), Rotation = Quaternion.Identity,
                    LengthM = 3.6f, DiameterM = 0.34f, HalfExtents = new Vector3(0.17f, 1.8f, 0.17f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = true, PlannedEmbedDepthM = 0.65f
                },
                new()
                {
                    Id = 2, Kind = BlueprintPieceKind.Post,
                    Position = new Vector3(2.6f, 1.15f, 0f), Rotation = Quaternion.Identity,
                    LengthM = 3.6f, DiameterM = 0.34f, HalfExtents = new Vector3(0.17f, 1.8f, 0.17f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = true, PlannedEmbedDepthM = 0.65f
                },
                new()
                {
                    Id = 3, Kind = BlueprintPieceKind.WallLog,
                    Position = new Vector3(1.3f, 0.25f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f),
                    LengthM = 2.6f, DiameterM = 0.30f, HalfExtents = new Vector3(0.15f, 1.3f, 0.15f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = false
                }
            };

            var evals = new BlueprintNodeEvaluation[nodes.Length];
            BlueprintPlanSummary summary = TimberBlueprintStructuralAnalyzer.AnalyzePlan(nodes, evals);

            Assert.That(summary.WillStand, Is.True);
            Assert.That(summary.CollapseCount, Is.EqualTo(0));
            Assert.That(summary.GroundedPosts, Is.EqualTo(2));
            Assert.That(evals[0].Tier, Is.EqualTo(BlueprintStabilityTier.GroundedBlue));
            Assert.That(evals[1].Tier, Is.EqualTo(BlueprintStabilityTier.GroundedBlue));
            Assert.That(evals[2].Tier, Is.EqualTo(BlueprintStabilityTier.SafeGreen));
        }

        [Test]
        public void FloatingWallWithoutPosts_FlagsCriticalRedNoGroundPath()
        {
            var nodes = new BlueprintNodeSpec[]
            {
                new()
                {
                    Id = 1, Kind = BlueprintPieceKind.WallLog,
                    Position = new Vector3(0f, 2.0f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f),
                    LengthM = 2.6f, DiameterM = 0.30f, HalfExtents = new Vector3(0.15f, 1.3f, 0.15f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = false
                }
            };

            var evals = new BlueprintNodeEvaluation[1];
            BlueprintPlanSummary summary = TimberBlueprintStructuralAnalyzer.AnalyzePlan(nodes, evals);

            Assert.That(summary.WillStand, Is.False);
            Assert.That(summary.CollapseCount, Is.EqualTo(1));
            Assert.That(evals[0].Tier, Is.EqualTo(BlueprintStabilityTier.CriticalRed));
            Assert.That(evals[0].Reason, Is.EqualTo(BlueprintFailureReason.NoGroundPath));
        }

        [Test]
        public void OverspannedBeam5_2m_CollapsesUntilMidPostAdded()
        {
            var beamRot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f);
            var nodesWithoutMid = new BlueprintNodeSpec[]
            {
                new()
                {
                    Id = 1, Kind = BlueprintPieceKind.Post,
                    Position = new Vector3(0f, 1.15f, 0f), Rotation = Quaternion.Identity,
                    LengthM = 3.6f, DiameterM = 0.34f, HalfExtents = new Vector3(0.17f, 1.8f, 0.17f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = true, PlannedEmbedDepthM = 0.65f
                },
                new()
                {
                    Id = 2, Kind = BlueprintPieceKind.Post,
                    Position = new Vector3(5.2f, 1.15f, 0f), Rotation = Quaternion.Identity,
                    LengthM = 3.6f, DiameterM = 0.34f, HalfExtents = new Vector3(0.17f, 1.8f, 0.17f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = true, PlannedEmbedDepthM = 0.65f
                },
                new()
                {
                    Id = 3, Kind = BlueprintPieceKind.FloorBeam,
                    Position = new Vector3(2.6f, 2.55f, 0f), Rotation = beamRot,
                    LengthM = 5.2f, DiameterM = 0.30f, HalfExtents = new Vector3(0.15f, 2.6f, 0.15f),
                    Species = TimberWoodSpecies.Oak, IsGrounded = false
                }
            };

            var evalsBefore = new BlueprintNodeEvaluation[3];
            BlueprintPlanSummary sumBefore = TimberBlueprintStructuralAnalyzer.AnalyzePlan(nodesWithoutMid, evalsBefore);
            Assert.That(sumBefore.WillStand, Is.False);
            Assert.That(evalsBefore[2].Tier, Is.EqualTo(BlueprintStabilityTier.CriticalRed));
            Assert.That(evalsBefore[2].Reason, Is.EqualTo(BlueprintFailureReason.UnsupportedSpanTooLong));

            var nodesWithMid = new BlueprintNodeSpec[4];
            Array.Copy(nodesWithoutMid, nodesWithMid, 3);
            nodesWithMid[3] = new BlueprintNodeSpec
            {
                Id = 4, Kind = BlueprintPieceKind.Post,
                Position = new Vector3(2.6f, 1.15f, 0f), Rotation = Quaternion.Identity,
                LengthM = 3.6f, DiameterM = 0.34f, HalfExtents = new Vector3(0.17f, 1.8f, 0.17f),
                Species = TimberWoodSpecies.Oak, IsGrounded = true, PlannedEmbedDepthM = 0.65f
            };

            var evalsAfter = new BlueprintNodeEvaluation[4];
            BlueprintPlanSummary sumAfter = TimberBlueprintStructuralAnalyzer.AnalyzePlan(nodesWithMid, evalsAfter);
            Assert.That(sumAfter.WillStand, Is.True);
            Assert.That(sumAfter.CollapseCount, Is.EqualTo(0));
            Assert.That(evalsAfter[2].Tier, Is.EqualTo(BlueprintStabilityTier.SafeGreen));
        }
    }
}
