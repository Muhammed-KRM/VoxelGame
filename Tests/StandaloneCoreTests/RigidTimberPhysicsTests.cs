#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Diagnostics;
using Stratocraft.Physics.RigidTimber;
using Stratocraft.Voxel.Data;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class RigidTimberPhysicsTests
    {
        [Test]
        public void KM0_WoodSpeciesLut_ComputesRealisticMassInertiaAndYieldMoment()
        {
            float pine6m = WoodSpeciesPhysicsLut.ComputeCylinderMassKg(TimberWoodSpecies.Pine, 6.0f, 0.30f, isDry: false);
            float oak6m = WoodSpeciesPhysicsLut.ComputeCylinderMassKg(TimberWoodSpecies.Oak, 6.0f, 0.30f, isDry: false);
            float ironwood6m = WoodSpeciesPhysicsLut.ComputeCylinderMassKg(TimberWoodSpecies.Ironwood, 6.0f, 0.30f, isDry: false);

            Assert.That(pine6m, Is.InRange(330f, 350f), "6m x 30cm yas cam ~339 kg olmali.");
            Assert.That(oak6m, Is.GreaterThan(pine6m));
            Assert.That(ironwood6m, Is.GreaterThan(oak6m));

            float pineYield = WoodSpeciesPhysicsLut.ComputeYieldMomentNm(TimberWoodSpecies.Pine, 0.30f);
            float oakYield = WoodSpeciesPhysicsLut.ComputeYieldMomentNm(TimberWoodSpecies.Oak, 0.30f);
            Assert.That(pineYield, Is.GreaterThan(40_000f));
            Assert.That(oakYield, Is.GreaterThan(pineYield));

            Vector3 invI = WoodSpeciesPhysicsLut.ComputeLocalInvInertiaCylinderY(pine6m, 6.0f, 0.30f);
            Assert.That(invI.X, Is.GreaterThan(0f));
            Assert.That(invI.Y, Is.GreaterThan(invI.X), "Silindirde eksenel atalet (Iy) en kucuk oldugundan 1/Iy > 1/Ix olmali.");
        }

        [Test]
        public void KM0_RigidStructurePiece_PreservesSinglePieceIdentityAndSplitParent()
        {
            RigidStructurePiece parent = RigidStructurePiece.Create(
                pieceId: 1001,
                kind: RigidTimberPieceKind.Post,
                species: TimberWoodSpecies.Oak,
                lengthM: 4.5f,
                diameterM: 0.32f,
                position: new Vector3(10f, 96f, -5f),
                rotation: Quaternion.Identity,
                initialState: RigidTimberState.StaticAnchored,
                notchFlags: TimberJoineryKind.SaddleNotch);

            RigidStructurePiece splitChild = RigidStructurePiece.Create(
                pieceId: 1002,
                kind: RigidTimberPieceKind.Log,
                species: parent.Species,
                lengthM: 2.25f,
                diameterM: parent.DiameterM,
                position: parent.Position,
                rotation: parent.Rotation,
                initialState: RigidTimberState.DynamicActive,
                notchFlags: TimberJoineryKind.FrictionOnly,
                splitParentId: parent.PieceId);

            Assert.That(parent.InvMass, Is.EqualTo(0f), "StaticAnchored parca sonsuz kutle (InvMass=0) olmali.");
            Assert.That(splitChild.InvMass, Is.GreaterThan(0f), "DynamicActive parca pozitif InvMass tasimali.");
            Assert.That(splitChild.SplitParentId, Is.EqualTo(parent.PieceId));
            Assert.That(splitChild.MassKg, Is.EqualTo(parent.MassKg * 0.5f).Within(0.1f));
        }

        [Test]
        public void KM0_TraceRecorder_RecordsRingAndFlushesValidJsonl()
        {
            var recorder = new RigidTimberPhysicsTraceRecorder(capacity: 64);
            var ev = new RigidPhysicsTraceEvent
            {
                Tick = 42,
                PieceId = 77,
                IslandId = 3,
                EventKind = RigidTimberTraceEventKind.SoilEvaluated,
                State = RigidTimberState.StaticAnchored,
                SoilDecision = SoilEmbedDecision.ElasticBalanced,
                BrokenMask = 0,
                EmbedDepthM = 1.25f,
                DriveMomentNm = 1200f,
                ResistMomentNm = 8400f,
                WoodYieldMomentNm = 62000f,
                TiltDeg = 0.85f,
                StressRatio01 = 0.143f,
                DirX = 1f,
                DirY = 0f,
                DirZ = 0f
            };

            recorder.Record(in ev, "SoilEmbedmentEvaluator.Evaluate");
            Assert.That(recorder.PendingCount, Is.EqualTo(1));

            string tempDir = Path.Combine(Path.GetTempPath(), "stratocraft_km0_trace_" + Guid.NewGuid().ToString("N"));
            try
            {
                int flushed = recorder.FlushToJsonl(tempDir);
                Assert.That(flushed, Is.EqualTo(1));
                Assert.That(recorder.PendingCount, Is.EqualTo(0));

                string filePath = Path.Combine(tempDir, "rigid_timber_trace.jsonl");
                Assert.That(File.Exists(filePath), Is.True);
                string content = File.ReadAllText(filePath);
                Assert.That(content, Does.Contain("\"piece\":77"));
                Assert.That(content, Does.Contain("\"embedD\":1.250"));
                Assert.That(content, Does.Contain("\"soilDecision\":\"ElasticBalanced\""));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void KM0_PlayerMovement_OpenDoorAndCeilingJoistArePassableInLiveCollision()
        {
            byte openDoorRot = ArchitecturalShapeCatalog.PackKitYawAndVariant(0, ArchitecturalShapeCatalog.DoorVarLeftLower);
            uint openDoorRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.WoodPlank,
                VoxelBitUtility.FullDensity,
                ArchitecturalShapeCatalog.KitHingedDoorOpen,
                openDoorRot,
                BlokKokenDurumu.DogalKutle,
                0);

            byte joistRot = ArchitecturalShapeCatalog.PackKitYawAndVariant(0, ArchitecturalShapeCatalog.TimberVarCeilingJoist);
            uint joistRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.TreatedTimberBeam,
                VoxelBitUtility.FullDensity,
                ArchitecturalShapeCatalog.KitTimberPostCorner,
                joistRot,
                BlokKokenDurumu.DogalKutle,
                0);

            uint closedDoorRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.WoodPlank,
                VoxelBitUtility.FullDensity,
                ArchitecturalShapeCatalog.KitHingedDoorClosed,
                openDoorRot,
                BlokKokenDurumu.DogalKutle,
                0);

            Assert.That(Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(openDoorRaw), Is.False, "Acik kapi gecirgen olmali.");
            Assert.That(Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(joistRaw), Is.False, "Tavan kirisi (dy=4) ziplarken kafa carpmamali.");
            Assert.That(Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(closedDoorRaw), Is.True, "Kapali kapi kati engel olmali.");
        }

        [Test]
        public void KM1_ResolveTreeTrunkProfile_ProducesShortMediumAndTallTreesAcrossForest()
        {
            int shortCount = 0;
            int mediumCount = 0;
            int tallCount = 0;
            int giantCount = 0;
            int shoulderCarryCount = 0;
            int logArchRequiredCount = 0;

            for (int x = -120; x <= 120; x += 12)
            {
                for (int z = -120; z <= 120; z += 12)
                {
                    TreeTrunkProfile p = TimberLogHarvestCatalog.ResolveTreeTrunkProfile(x, z, biomeId: 0, worldSeed: 1453u);
                    if (p.TrunkLengthM < 4.0f) shortCount++;
                    else if (p.TrunkLengthM < 6.8f) mediumCount++;
                    else if (p.TrunkLengthM < 10.2f) tallCount++;
                    else giantCount++;

                    if (p.CanShoulderCarry) shoulderCarryCount++;
                    else logArchRequiredCount++;
                }
            }

            Assert.That(shortCount, Is.GreaterThan(10), "Kisa (2.5-3.8m) agaclar olmali.");
            Assert.That(mediumCount, Is.GreaterThan(15), "Orta (4.2-6.5m) agaclar olmali.");
            Assert.That(tallCount, Is.GreaterThan(15), "Buyuk (7.0-9.8m) agaclar olmali.");
            Assert.That(giantCount, Is.GreaterThan(10), "3 katli eve yetecek kadim dev (10.5-14.0m+) agaclar olmali.");
            Assert.That(shoulderCarryCount, Is.GreaterThan(0), "Omuzlanabilir kucuk kutukler olmali.");
            Assert.That(logArchRequiredCount, Is.GreaterThan(0), "Log Arch / El Arabasi gerektiren uzun/agir kutukler olmali.");
        }

        [Test]
        public void KM1_TrySplitLogAtOffset_SplitsFullLogIntoTwoPermanentSeparatePiecesAndLogsTrace()
        {
            var recorder = new RigidTimberPhysicsTraceRecorder(64);
            RigidStructurePiece fullLog = RigidStructurePiece.Create(
                pieceId: 500,
                kind: RigidTimberPieceKind.Log,
                species: TimberWoodSpecies.Pine,
                lengthM: 6.0f,
                diameterM: 0.34f,
                position: new Vector3(0f, 96f, 0f),
                rotation: Quaternion.Identity,
                initialState: RigidTimberState.StaticLooseEntity);

            // Merkezden +1.0m ileriden kes -> 4.0m + 2.0m iki kalici parca!
            bool ok = TimberLogHarvestCatalog.TrySplitLogAtOffset(
                in fullLog,
                cutOffsetFromCenterM: 1.0f,
                childAId: 501,
                childBId: 502,
                currentTick: 100,
                out RigidStructurePiece childA,
                out RigidStructurePiece childB,
                traceSink: recorder);

            Assert.That(ok, Is.True);
            Assert.That(childA.LengthM, Is.EqualTo(4.0f).Within(0.01f));
            Assert.That(childB.LengthM, Is.EqualTo(2.0f).Within(0.01f));
            Assert.That(childA.MassKg + childB.MassKg, Is.EqualTo(fullLog.MassKg).Within(0.25f), "Toplam kutle korunmali.");
            Assert.That(childA.SplitParentId, Is.EqualTo(500u));
            Assert.That(childB.SplitParentId, Is.EqualTo(500u));
            Assert.That(childA.PieceId, Is.Not.EqualTo(childB.PieceId));
            Assert.That(recorder.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void KM2_LogArchCart_BindsFull14mLogWithRealisticCompoundMassCgAndPartialSkid()
        {
            var recorder = new RigidTimberPhysicsTraceRecorder(64);
            LogArchCartState cart = LogArchCartState.CreateEmpty(cartId: 1);

            RigidStructurePiece longLog = RigidStructurePiece.Create(
                pieceId: 901,
                kind: RigidTimberPieceKind.Log,
                species: TimberWoodSpecies.Oak,
                lengthM: 14.0f,
                diameterM: 0.42f,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                initialState: RigidTimberState.StaticLooseEntity);

            bool bound = LogArchCartPhysics.TryBindFullLog(
                ref cart,
                in longLog,
                logCgOffsetFromAxleM: 0.16f,
                currentTick: 200,
                out string msg,
                traceSink: recorder);

            Assert.That(bound, Is.True, msg);
            Assert.That(cart.HasBoundLog, Is.EqualTo(1));
            Assert.That(cart.PartialSkiddingMode, Is.EqualTo(1), "6m+ kutuklerde kismi surukleme modu acilmali.");
            Assert.That(cart.TotalMassKg, Is.GreaterThan(cart.EmptyCartMassKg + 500f));
            Assert.That(cart.TongueWeightKg, Is.GreaterThan(0f), "CG dingilin onundeyken dil agirligi pozitif olmali.");
            Assert.That(cart.PullSpeedMultiplier, Is.InRange(0.30f, 0.85f));
            Assert.That(recorder.PendingCount, Is.EqualTo(1));

            cart.BrakeEngaged = 1;
            LogArchCartPhysics.RecomputeCompoundMassAndBalance(ref cart, terrainSlopeSin: -0.25f);
            Assert.That(cart.PullSpeedMultiplier, Is.LessThan(0.15f), "Takoz freni basiliyken hiz cok dusmeli.");
        }

        [Test]
        public void KM2_LogArchCart_ClampedSuspensionNeverProducesNegativePullOrExplosionSpike()
        {
            float noHitForce = LogArchCartPhysics.ComputeClampedSuspensionForce(
                restLengthM: 0.55f, currentHitDistanceM: 0.70f, verticalPointVelocityMps: -2f,
                springStiffnessNPerM: 12000f, damperNsPerM: 1500f, maxForceN: 25000f);
            Assert.That(noHitForce, Is.EqualTo(0f), "Tekerlek havadayken negatif cekme kuvveti olmamali.");

            float spikeForce = LogArchCartPhysics.ComputeClampedSuspensionForce(
                restLengthM: 0.55f, currentHitDistanceM: 0.0f, verticalPointVelocityMps: -50f,
                springStiffnessNPerM: 12000f, damperNsPerM: 1500f, maxForceN: 25000f);
            Assert.That(spikeForce, Is.EqualTo(25000f), "Penetrasyonda maxForce clamp patlamayi engellemeli.");
        }

        private sealed class SyntheticVoxelSoilSampler : IVoxelSoilSampler
        {
            public float GroundY = 100.0f;
            public byte DefaultSoilMat = MaterialIdCatalog.Dirt;
            public bool ExcavatePosXSide;
            public float ExcavateDepthM = 1.5f;

            public bool TrySampleSolidMaterialAt(float worldX, float worldY, float worldZ, out byte materialId)
            {
                if (ExcavatePosXSide && worldX > 0.15f && worldY > GroundY - ExcavateDepthM)
                {
                    materialId = MaterialIdCatalog.Air;
                    return false;
                }
                if (worldY <= GroundY)
                {
                    materialId = DefaultSoilMat;
                    return true;
                }
                materialId = MaterialIdCatalog.Air;
                return false;
            }

            public bool TrySampleGroundSurfaceY(float worldX, float worldZ, float referenceY, out float surfaceY, out byte materialId)
            {
                surfaceY = GroundY;
                materialId = DefaultSoilMat;
                return true;
            }
        }

        [Test]
        public void KM3_SoilEmbedment_UnembeddedPostOverturnsWhileDeepEmbeddedPostStands()
        {
            var soil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Dirt };
            var recorder = new RigidTimberPhysicsTraceRecorder(64);

            // 1) Yuzeye gommeden konulan 4.5m sutun (tabani y=100 -> embedD = 0)
            RigidStructurePiece surfacePost = RigidStructurePiece.Create(
                2001, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 4.5f, 0.30f,
                new Vector3(0f, 102.25f, 0f), Quaternion.Identity);
            SoilEmbedProfile surfProf = SoilEmbedmentEvaluator.SampleAndBuildProfile(in surfacePost, soil);
            SoilEmbedDecision surfDec = SoilEmbedmentEvaluator.EvaluatePostStability(
                ref surfacePost, ref surfProf, topLoadKg: 200f,
                lateralForceWorldN: new Vector3(900f, 0f, 0f), lateralHeightAboveGroundM: 3.0f,
                topLoadEccentricityXZ: Vector2.Zero, currentTick: 1, traceSink: recorder);

            Assert.That(surfProf.EmbedDepthM, Is.LessThan(0.1f));
            Assert.That(surfDec, Is.EqualTo(SoilEmbedDecision.GeotechnicalOverturn), "Gomulmemis sutun yanal yukte devrilmeli!");

            // 2) 1.5m derine gomulup sikistirilmis ayni sutun (tabani y=98.5 -> embedD = 1.5m)
            RigidStructurePiece deepPost = RigidStructurePiece.Create(
                2002, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 4.5f, 0.30f,
                new Vector3(0f, 100.75f, 0f), Quaternion.Identity);
            SoilEmbedProfile deepProf = SoilEmbedmentEvaluator.SampleAndBuildProfile(in deepPost, soil, compactionFactor: 0.95f, hasStoneCollar: true);
            SoilEmbedDecision deepDec = SoilEmbedmentEvaluator.EvaluatePostStability(
                ref deepPost, ref deepProf, topLoadKg: 200f,
                lateralForceWorldN: new Vector3(900f, 0f, 0f), lateralHeightAboveGroundM: 3.0f,
                topLoadEccentricityXZ: Vector2.Zero, currentTick: 2, traceSink: recorder);

            Assert.That(deepProf.EmbedDepthM, Is.GreaterThanOrEqualTo(1.25f));
            Assert.That(deepDec, Is.EqualTo(SoilEmbedDecision.ElasticBalanced), "1.5m gomulu sutun ayni yukte dimdik durmali!");
            Assert.That(recorder.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void KM3_SoilEmbedment_AsymmetricExcavationTipsDirectlyTowardDugSide()
        {
            var soil = new SyntheticVoxelSoilSampler
            {
                GroundY = 100f,
                DefaultSoilMat = MaterialIdCatalog.Sand,
                ExcavatePosXSide = true,
                ExcavateDepthM = 1.5f
            };

            RigidStructurePiece post = RigidStructurePiece.Create(
                2003, RigidTimberPieceKind.Post, TimberWoodSpecies.Pine, 4.5f, 0.30f,
                new Vector3(0f, 100.75f, 0f), Quaternion.Identity);

            SoilEmbedProfile prof = SoilEmbedmentEvaluator.SampleAndBuildProfile(in post, soil, compactionFactor: 0.70f);
            SoilEmbedDecision dec = SoilEmbedmentEvaluator.EvaluatePostStability(
                ref post, ref prof, topLoadKg: 350f,
                lateralForceWorldN: Vector3.Zero, lateralHeightAboveGroundM: 3.0f,
                topLoadEccentricityXZ: new Vector2(0.05f, 0f), currentTick: 3);

            Assert.That(prof.DirDepthPosX, Is.EqualTo(0f), "+X tarafi kazildigi icin derinlik 0 olmali.");
            Assert.That(prof.DirDepthNegX, Is.GreaterThan(1.0f), "-X tarafi hala dolu olmali.");
            Assert.That(prof.OverturnDirX, Is.GreaterThan(0.8f), "Sutun dogrudan kazilan (+X) yonune devrilmeli!");
            Assert.That(dec, Is.EqualTo(SoilEmbedDecision.GeotechnicalOverturn));
        }

        [Test]
        public void KM3_SoilEmbedment_BedrockDeepEmbedFracturesWoodShaftInsteadOfOverturning()
        {
            var rockSoil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Bedrock };
            RigidStructurePiece post = RigidStructurePiece.Create(
                2004, RigidTimberPieceKind.Post, TimberWoodSpecies.Pine, 4.5f, 0.24f,
                new Vector3(0f, 100.75f, 0f), Quaternion.Identity);

            SoilEmbedProfile prof = SoilEmbedmentEvaluator.SampleAndBuildProfile(in post, rockSoil, compactionFactor: 1.0f);
            SoilEmbedDecision dec = SoilEmbedmentEvaluator.EvaluatePostStability(
                ref post, ref prof, topLoadKg: 200f,
                lateralForceWorldN: new Vector3(50_000f, 0f, 0f), lateralHeightAboveGroundM: 3.0f,
                topLoadEccentricityXZ: Vector2.Zero, currentTick: 4);

            Assert.That(dec, Is.EqualTo(SoilEmbedDecision.WoodShaftFracture), "Ana kayada zemin tutmali, asiri yukte ahsap govde kirilmali!");
        }

        [Test]
        public void KM4_XpbdSolver_FallingPostLandsOnVoxelGroundAndSleepsAsStaticLooseEntity()
        {
            var soil = new SyntheticVoxelSoilSampler { GroundY = 100f };
            var recorder = new RigidTimberPhysicsTraceRecorder(64);
            var solver = new XpbdRigidTimberSolver();

            var pieces = new RigidStructurePiece[1];
            pieces[0] = RigidStructurePiece.Create(
                3001, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 4.0f, 0.30f,
                new Vector3(0f, 103.5f, 0f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.35f),
                initialState: RigidTimberState.DynamicActive);
            pieces[0].AngularVelocity = new Vector3(0f, 0f, 1.2f);

            var bonds = Array.Empty<CornerBond4Interface>();
            for (int frame = 0; frame < 360; frame++)
            {
                int active = solver.StepActiveDynamics(pieces, bonds, soil, 1f / 60f, frame, recorder);
                if (active == 0) break;
            }

            Assert.That(pieces[0].Position.Y, Is.GreaterThanOrEqualTo(99.9f), "Kutuk zeminin icinden gecmemeli!");
            Assert.That(pieces[0].State, Is.EqualTo(RigidTimberState.StaticLooseEntity), "Durduktan sonra StaticLooseEntity olarak uyumali!");
            Assert.That(pieces[0].InvMass, Is.EqualTo(0f), "Uyuyan parca 0 ms statik olmali.");
        }

        [Test]
        public void KM4_ReducedAem_DovetailResistsTensionWhileFrictionOnlyBreaks()
        {
            RigidStructurePiece a = RigidStructurePiece.Create(4001, RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, 4f, 0.30f, Vector3.Zero, Quaternion.Identity);
            RigidStructurePiece b = RigidStructurePiece.Create(4002, RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, 4f, 0.30f, new Vector3(0f, 0.30f, 0f), Quaternion.Identity);

            CornerBond4Interface frictionBond = ReducedAemBondEvaluator.CreateInterface(1, in a, in b, new Vector3(0f, 0.15f, 0f), Vector3.UnitY, 0.15f, 0.15f, TimberJoineryKind.FrictionOnly);
            CornerBond4Interface dovetailBond = ReducedAemBondEvaluator.CreateInterface(2, in a, in b, new Vector3(0f, 0.15f, 0f), Vector3.UnitY, 0.15f, 0.15f, TimberJoineryKind.Dovetail);

            // 1/720s substep'te 400 N cekme -> FrictionOnly (T_max=0) kopmali, Dovetail saglam kalmali!
            float h = 1f / 720f;
            float lamFor400N = 400f * h * h;
            frictionBond.LambdaN0 = lamFor400N;
            frictionBond.LambdaN1 = lamFor400N;
            dovetailBond.LambdaN0 = lamFor400N;
            dovetailBond.LambdaN1 = lamFor400N;

            bool fricBroke = ReducedAemBondEvaluator.EvaluateAndFractureCorners(ref frictionBond, h, 1, 10);
            bool doveBroke = ReducedAemBondEvaluator.EvaluateAndFractureCorners(ref dovetailBond, h, 1, 10);

            Assert.That(fricBroke, Is.True, "Kertiksiz bag cekmede hemen kopmali.");
            Assert.That(frictionBond.CornerBrokenMask, Is.EqualTo(0x03), "Yalnizca cekme alan 2 kose kopmali (kismi catlama!).");
            Assert.That(doveBroke, Is.False, "Dovetail geometrik cekme kilidi 400N cekmeyi rahatca tutmali.");
        }

        [Test]
        public void KM5_StaticEquilibrium_RemovingSupportPostWakesUpperBeamWithZeroFloatingPieces()
        {
            var soil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Gravel };
            var solver = new TimberStaticEquilibriumSolver();

            // 2 gomulu sutun + ustunde 1 kiriş
            var pieces = new RigidStructurePiece[3];
            pieces[0] = RigidStructurePiece.Create(5001, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 4.5f, 0.30f, new Vector3(-2f, 100.75f, 0f), Quaternion.Identity);
            pieces[1] = RigidStructurePiece.Create(5002, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 4.5f, 0.30f, new Vector3(2f, 100.75f, 0f), Quaternion.Identity);
            pieces[2] = RigidStructurePiece.Create(5003, RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, 4.0f, 0.28f, new Vector3(0f, 103.1f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f));
            pieces[0].IslandId = 1; pieces[1].IslandId = 1; pieces[2].IslandId = 1;

            var profiles = new SoilEmbedProfile[2];
            profiles[0] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in pieces[0], soil, 0.95f, true);
            profiles[1] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in pieces[1], soil, 0.95f, true);

            var bonds = new CornerBond4Interface[2];
            bonds[0] = ReducedAemBondEvaluator.CreateInterface(10, in pieces[0], in pieces[2], new Vector3(-2f, 103f, 0f), Vector3.UnitY, 0.15f, 0.15f, TimberJoineryKind.JoistPocket);
            bonds[1] = ReducedAemBondEvaluator.CreateInterface(11, in pieces[1], in pieces[2], new Vector3(2f, 103f, 0f), Vector3.UnitY, 0.15f, 0.15f, TimberJoineryKind.JoistPocket);

            int wokenStable = solver.EvaluateDirtyIsland(1, pieces, profiles, bonds, Vector3.Zero, currentTick: 1);
            Assert.That(wokenStable, Is.EqualTo(0), "Saglam 2 sutun + kiris 0 uyanma ile dengede durmali.");

            // Her iki sutunun bagini kir -> kiris havada asili kalmadan aninda DynamicActive uyanmali!
            bonds[0].IsActive = 0;
            bonds[1].IsActive = 0;
            int wokenCollapsed = solver.EvaluateDirtyIsland(1, pieces, profiles, bonds, Vector3.Zero, currentTick: 2);
            Assert.That(wokenCollapsed, Is.EqualTo(1));
            Assert.That(pieces[2].State, Is.EqualTo(RigidTimberState.DynamicActive), "Desteksiz kalan kiris asla havada asili kalmamali!");
        }

        [Test]
        public void KM6_TimberHoist_Parbuckle2To1AndProgressCapturePreventsSlipBack()
        {
            // 180 kg tam boy kiriş (~1765 N agirlik): tek oyuncu doğrudan kaldiramaz, ama 32 deg 2:1 Parbuckle ile ~480 N < 520 N!
            TimberHoistState hoist = TimberHoistPhysics.CreateParbuckleRamp(1, 6001, logMassKg: 180f, targetLiftHeightM: 3.0f, rampAngleDeg: 30f);
            float reqPull = TimberHoistPhysics.ComputeRequiredPlayerPullN(in hoist);

            Assert.That(reqPull, Is.LessThan(TimberHoistPhysics.PlayerMaxPullForceN),
                "2:1 Parbuckle rampa 180 kg kirişi tek oyuncunun çekebileceği kuvvete indirmeli.");

            for (int i = 0; i < 60; i++)
            {
                TimberHoistPhysics.StepHoist(ref hoist, pullInput01: 1.0f, dt: 0.05f);
            }
            float progressAfterPull = hoist.ProgressM;
            Assert.That(progressAfterPull, Is.GreaterThan(0.5f));

            // Oyuncu halati biraktiginda (pullInput01 = 0) progress-capture mandali geri kaymayi engellemeli!
            for (int i = 0; i < 40; i++)
            {
                TimberHoistPhysics.StepHoist(ref hoist, pullInput01: 0.0f, dt: 0.05f);
            }
            Assert.That(hoist.ProgressM, Is.EqualTo(progressAfterPull).Within(1e-4f),
                "Halat bırakıldığında progress-capture mandalı kütüğü yerinde tutmalı.");
        }

        [Test]
        public void KM6_TimberHoist_GinPolePulleyLiftsHeavyBeamToThirdStory()
        {
            TimberHoistState hoist = TimberHoistPhysics.CreateGinPole(2, 6002, logMassKg: 160f, liftHeightM: 6.0f, pulleySheaves: 4);
            for (int i = 0; i < 400 && hoist.IsCompleted == 0; i++)
            {
                TimberHoistPhysics.StepHoist(ref hoist, pullInput01: 1.0f, dt: 0.1f);
            }
            Assert.That(hoist.IsCompleted, Is.EqualTo(1), "4 makaralı Gin Pole 160 kg kirişi 3. kata kadar kaldırabilmeli.");
            Assert.That(hoist.ProgressM, Is.EqualTo(6.0f).Within(0.03f));
        }

        [Test]
        public void KM6_ThreeStoryCabin_StandingBalancedThenCollapsesWhenTieBeamAndSupportsCut()
        {
            var soil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Gravel };
            var staticSolver = new TimberStaticEquilibriumSolver();
            var xpbdSolver = new XpbdRigidTimberSolver();

            // 2 kose sutunu + 1 ust kiris + 1 cati mertegi + 1 gergi kirisi
            var pieces = new RigidStructurePiece[4];
            pieces[0] = RigidStructurePiece.Create(6101, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 10.5f, 0.34f, new Vector3(-2.2f, 103.75f, 0f), Quaternion.Identity);
            pieces[1] = RigidStructurePiece.Create(6102, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 10.5f, 0.34f, new Vector3(2.2f, 103.75f, 0f), Quaternion.Identity);
            pieces[2] = RigidStructurePiece.Create(6103, RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, 4.4f, 0.26f, new Vector3(0f, 109.0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f));
            pieces[3] = RigidStructurePiece.Create(6104, RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, 3.0f, 0.20f, new Vector3(-1.1f, 110.2f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f));
            for (int i = 0; i < pieces.Length; i++) pieces[i].IslandId = 7;

            var profiles = new SoilEmbedProfile[4];
            profiles[0] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in pieces[0], soil, 0.95f, true);
            profiles[1] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in pieces[1], soil, 0.95f, true);

            var bonds = new CornerBond4Interface[3];
            bonds[0] = ReducedAemBondEvaluator.CreateInterface(101, in pieces[0], in pieces[2], new Vector3(-2.2f, 109.0f, 0f), Vector3.UnitY, 0.14f, 0.14f, TimberJoineryKind.SaddleNotch);
            bonds[1] = ReducedAemBondEvaluator.CreateInterface(102, in pieces[1], in pieces[2], new Vector3(2.2f, 109.0f, 0f), Vector3.UnitY, 0.14f, 0.14f, TimberJoineryKind.SaddleNotch);
            bonds[2] = ReducedAemBondEvaluator.CreateInterface(103, in pieces[2], in pieces[3], new Vector3(-2.2f, 109.1f, 0f), Vector3.UnitY, 0.12f, 0.12f, TimberJoineryKind.Pegged);

            Assert.That(staticSolver.EvaluateDirtyIsland(7, pieces, profiles, bonds, Vector3.Zero, currentTick: 1), Is.EqualTo(0));

            // Her iki kose bagini kes -> ust kiris + mertek XPBD ile dusup yerde StaticLooseEntity olarak uyumali!
            bonds[0].IsActive = 0;
            bonds[1].IsActive = 0;
            int woken = staticSolver.EvaluateDirtyIsland(7, pieces, profiles, bonds, Vector3.Zero, currentTick: 2);
            Assert.That(woken, Is.GreaterThanOrEqualTo(2));

            for (int step = 0; step < 480; step++)
            {
                xpbdSolver.StepActiveDynamics(pieces, bonds, soil, deltaTimeSec: 1f / 60f, currentTick: 3 + step);
            }
            Assert.That(pieces[2].Position.Y, Is.LessThan(102.0f), "Kesilen 3. kat kirişi yere düşmeli!");
            Assert.That(pieces[2].State, Is.EqualTo(RigidTimberState.StaticLooseEntity),
                $"Yere inen kiriş uyku moduna geçmeli! posY={pieces[2].Position.Y:0.000}, v={pieces[2].LinearVelocity.Length():0.000}, w={pieces[2].AngularVelocity.Length():0.000}, sleep={pieces[2].SleepTimerSec:0.00}");
        }

        [Test]
        public void SoilBackfill_PacksNaturalSmoothSoilWithPlayerOrigin_ProtectedFromCrystal()
        {
            uint packed = CrystalTerrainFlattenUtility.PackCrystalImmuneBackfillSoilVoxel(MaterialIdCatalog.Dirt);
            Assert.That(VoxelBitUtility.GetMaterialId(packed), Is.EqualTo(MaterialIdCatalog.Dirt));
            Assert.That(VoxelBitUtility.GetShapeId(packed), Is.EqualTo(0), "Zeminle birlesmesi icin ShapeID=0 (Smooth SurfaceNets) olmali.");
            Assert.That(VoxelBitUtility.GetOriginState(packed), Is.EqualTo(BlokKokenDurumu.OyuncuItemBlogu));
            Assert.That(CrystalTerrainFlattenUtility.IsProtectedFromCrystalVacuum(packed, isCreativeMode: false), Is.True,
                "Oyuncu eliyle doldurulan toprak [C] Madde Kristali ile kirilamamali!");
            Assert.That(CrystalTerrainFlattenUtility.IsProtectedFromCrystalVacuum(packed, isCreativeMode: true), Is.True,
                "Yaratici Mod acikken bile oyuncu eliyle [V] ile doldurulan toprak Kristal ile kirilamamali!");
        }

        [Test]
        public void CrystalFlatten_ShavesBumpsAndFillsPits_ProtectsPlayerPlacedBackfillSoil()
        {
            var chunks = new Dictionary<(int cx, int cz), uint[]>();
            var dirty = new HashSet<(int cx, int cz)>();
            var vox = new uint[16 * 16 * 256];
            chunks[(0, 0)] = vox;

            // Zemin y=0..10 duz, (5,5)'te y=11,12 dogal tepe, (6,5)'te y=10 cukur, (4,5)'te y=11 oyuncu eliyle konulmus toprak!
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            for (int y = 1; y <= 10; y++)
            {
                vox[(y << 8) | (z << 4) | x] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            }

            vox[(11 << 8) | (5 << 4) | 5] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            vox[(12 << 8) | (5 << 4) | 5] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            vox[(10 << 8) | (5 << 4) | 6] = 0u; // Cukur
            vox[(11 << 8) | (5 << 4) | 4] = CrystalTerrainFlattenUtility.PackCrystalImmuneBackfillSoilVoxel(MaterialIdCatalog.Dirt); // Oyuncu dolgusu

            int deposited = 0;
            int withdrawn = 0;
            var res = CrystalTerrainFlattenUtility.ApplyFlattenBrush(
                centerWx: 5, centerWz: 5, targetSurfaceY: 10, radiusMeters: 2.0f, maxEditableY: 250,
                fillMaterialId: MaterialIdCatalog.Dirt, isCreativeMode: false, chunks, dirty,
                onShavedDeposit: _ => deposited++,
                onFillWithdraw: _ => { withdrawn++; return true; });

            Assert.That(res.ShavedVoxels, Is.EqualTo(2), "Dogal 2 blokluk tepe traslanmali.");
            Assert.That(res.FilledVoxels, Is.EqualTo(1), "1 blokluk cukur doldurulmali.");
            Assert.That(res.SkippedProtectedVoxels, Is.EqualTo(1), "Oyuncu eliyle konulan toprak korunmali.");
            Assert.That(VoxelBitUtility.GetMaterialId(vox[(11 << 8) | (5 << 4) | 5]), Is.EqualTo(MaterialIdCatalog.Air));
            Assert.That(VoxelBitUtility.GetMaterialId(vox[(10 << 8) | (5 << 4) | 6]), Is.EqualTo(MaterialIdCatalog.Dirt));
            Assert.That(VoxelBitUtility.GetMaterialId(vox[(11 << 8) | (5 << 4) | 4]), Is.EqualTo(MaterialIdCatalog.Dirt),
                "Oyuncu eliyle konulmus toprak yer duzleme ile ASLA silinmemeli!");
            Assert.That(deposited, Is.EqualTo(2));
            Assert.That(withdrawn, Is.EqualTo(1));
        }

        [Test]
        public void BoxPanel_8CornerGroundContact_LandsFlushOnGroundNeverFloats()
        {
            var soil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Dirt };
            var xpbd = new XpbdRigidTimberSolver();
            var pieces = new RigidStructurePiece[1];
            pieces[0] = RigidStructurePiece.Create(7001, RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, 5.2f, 0.24f,
                new Vector3(0f, 104.0f, 0f), Quaternion.Identity, RigidTimberState.DynamicActive);
            pieces[0].HalfExtents = new Vector3(2.6f, 0.12f, 2.9f); // Yatay doseme levhasi!

            for (int s = 0; s < 300; s++)
            {
                xpbd.StepActiveDynamics(pieces, Span<CornerBond4Interface>.Empty, soil, 1f / 60f, 1 + s);
            }

            Assert.That(pieces[0].Position.Y, Is.InRange(100.05f, 100.25f),
                "Yatay doseme levhasi 2.8m havada kalmamali, tam 0.12m yari kalinligiyla zemine oturmali!");
            Assert.That(pieces[0].State, Is.EqualTo(RigidTimberState.StaticLooseEntity));
        }

        [Test]
        public void ImpactFracture_CascadingEnergyThroughMiddleBeamBreaksLowerWeakPlank()
        {
            // Ustten dusen 900 kg agir kütük -> ortadaki saglam Ironwood kirise (kirilmaz) carpar
            // -> enerji altindaki zayif Cedar kalasa aktarilir ve alttaki kalas kirilir!
            var pieces = new RigidStructurePiece[3];
            pieces[0] = RigidStructurePiece.Create(7101, RigidTimberPieceKind.Log, TimberWoodSpecies.Ironwood, 4.0f, 0.55f, new Vector3(0f, 103.0f, 0f), Quaternion.Identity, RigidTimberState.DynamicActive);
            pieces[0].HalfExtents = new Vector3(0.30f, 0.30f, 2.0f);
            pieces[1] = RigidStructurePiece.Create(7102, RigidTimberPieceKind.Beam, TimberWoodSpecies.Ironwood, 4.0f, 0.55f, new Vector3(0f, 102.2f, 0f), Quaternion.Identity, RigidTimberState.StaticAnchored);
            pieces[1].HalfExtents = new Vector3(0.30f, 0.30f, 2.0f);
            pieces[2] = RigidStructurePiece.Create(7103, RigidTimberPieceKind.Plank, TimberWoodSpecies.Cedar, 3.0f, 0.20f, new Vector3(0f, 101.6f, 0f), Quaternion.Identity, RigidTimberState.StaticAnchored);
            pieces[2].HalfExtents = new Vector3(1.5f, 0.10f, 1.5f);

            Span<int> frac = stackalloc int[8];
            int count = TimberImpactFractureSystem.EvaluateImpactAndCascadeEnergy(0, impactSpeedMps: 9.5f, pieces, frac);

            Assert.That(pieces[1].Damage01, Is.GreaterThan(0.1f).And.LessThan(1.0f), "Ortadaki saglam Ironwood kiris hasar almali ama tek darbede kirilmamali.");
            Assert.That(pieces[2].Damage01, Is.GreaterThanOrEqualTo(1.0f), "Ortadaki kiris kirilmasa bile carpma enerjisini altindaki zayif kalasa aktarip onu kirmali!");
            Assert.That(pieces[2].State, Is.EqualTo(RigidTimberState.DynamicActive));
            Assert.That(count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SupportPolygon_CuttingOneCornerPostOf3StoryCabinTipsEntireCabinTowardCutCorner()
        {
            var soil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Gravel };
            var solver = new TimberStaticEquilibriumSolver();

            // 3 katli evde 4 kose sutunundan 1'i kesilmis (3 sutun kalmis: (-3,-3), (3,-3), (3,3); kesilen (-3,3))
            // + ustte 8 adet kütük/kiris (merkez 0,0 civarinda, yukseklik 103..109m)
            var pieces = new RigidStructurePiece[11];
            pieces[0] = RigidStructurePiece.Create(7201, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 10.5f, 0.60f, new Vector3(-3f, 103.75f, -3f), Quaternion.Identity);
            pieces[1] = RigidStructurePiece.Create(7202, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 10.5f, 0.60f, new Vector3(3f, 103.75f, -3f), Quaternion.Identity);
            pieces[2] = RigidStructurePiece.Create(7203, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 10.5f, 0.60f, new Vector3(3f, 103.75f, 3f), Quaternion.Identity);
            for (int k = 0; k < 8; k++)
            {
                pieces[3 + k] = RigidStructurePiece.Create((uint)(7204 + k), RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, 6.0f, 0.42f,
                    new Vector3(0f, 103f + (k % 3) * 3.0f, 0f), Quaternion.Identity);
            }
            for (int i = 0; i < pieces.Length; i++) pieces[i].IslandId = 9;

            var profiles = new SoilEmbedProfile[3];
            for (int c = 0; c < 3; c++) profiles[c] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in pieces[c], soil, 0.95f, true);

            var bonds = new CornerBond4Interface[8];
            for (int k = 0; k < 8; k++)
            {
                bonds[k] = ReducedAemBondEvaluator.CreateInterface((uint)(200 + k), in pieces[k % 3], in pieces[3 + k], pieces[3 + k].Position, Vector3.UnitY, 0.2f, 0.2f, TimberJoineryKind.SaddleNotch);
            }

            int woken = solver.EvaluateDirtyIsland(9, pieces, profiles, bonds, Vector3.Zero, currentTick: 1);
            Assert.That(woken, Is.EqualTo(11), "3 katli evde 1 kose sutunu kesildiginde tum ev kesilen kose yonune dogru devrilmeye baslamali!");
            // Kesilen kose (-3, +3) yonunde (X < 0, Z > 0) itme hizi olusmali!
            Assert.That(pieces[5].LinearVelocity.X, Is.LessThan(0f));
            Assert.That(pieces[5].LinearVelocity.Z, Is.GreaterThan(0f));
        }

        [Test]
        public void TipIn_FlatGroundFailsButDugPitHoldsPostTemporarilyAndUpgradesWithAxeNotch()
        {
            var flatSoil = new SyntheticVoxelSoilSampler { GroundY = 100f, DefaultSoilMat = MaterialIdCatalog.Dirt };
            TipInEvaluation flatEval = TimberTipInAndJoineryLogic.EvaluateTipInHole(0f, 0f, 100f, flatSoil);
            Assert.That(flatEval.Result, Is.EqualTo(TipInPlacementResult.NoHoleFlatGroundTipsOver),
                "Duz zeminde cukur kazmadan koca kutuk dik duramaz!");

            // Balta vurusuyla FrictionOnly -> SaddleNotch -> Dovetail kertik yukseltmesi
            var pA = RigidStructurePiece.Create(8001, RigidTimberPieceKind.Post, TimberWoodSpecies.Oak, 4f, 0.5f, Vector3.Zero, Quaternion.Identity);
            var pB = RigidStructurePiece.Create(8002, RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, 4f, 0.5f, new Vector3(0f, 2f, 0f), Quaternion.Identity);
            var bond = ReducedAemBondEvaluator.CreateInterface(301, in pA, in pB, new Vector3(0f, 2f, 0f), Vector3.UnitY, 0.25f, 0.25f, TimberJoineryKind.FrictionOnly);

            Assert.That(TimberTipInAndJoineryLogic.UpgradeJoineryWithAxeNotch(ref bond, out var j1), Is.True);
            Assert.That(j1, Is.EqualTo(TimberJoineryKind.SaddleNotch));
            Assert.That(TimberTipInAndJoineryLogic.UpgradeJoineryWithAxeNotch(ref bond, out var j2), Is.True);
            Assert.That(j2, Is.EqualTo(TimberJoineryKind.Dovetail));
        }

        [Test]
        public void CabinInterior_ChinkingAndClosedDoorEnablesWarmRoomAndChimneyPreventsSmokeHazard()
        {
            // 1) Derz yalitimi (Chinking) yok -> sicak kapali oda degil!
            var unsealed = new CabinInteriorThermalState
            {
                TotalWallLogs = 20, ChinkedWallLogs = 0, TotalOpenings = 2, ClosedDoorsOrShutters = 2,
                HasRoofOverhead = 1, HasLitFireplace = 1, HasRoofChimneyFlue = 1
            };
            TimberCabinInteriorSystem.EvaluateCabinThermalAndSmoke(ref unsealed);
            Assert.That(unsealed.IsEnclosedWarmRoom, Is.False, "Chinking olmadan sicak kapali oda sayilmamali (AK-05).");

            // 2) Chinking tam + kapilar kapali + baca YOK -> duman/O2 uyarisi!
            var smoky = unsealed;
            smoky.ChinkedWallLogs = 20;
            smoky.HasRoofChimneyFlue = 0;
            TimberCabinInteriorSystem.EvaluateCabinThermalAndSmoke(ref smoky);
            Assert.That(smoky.IsEnclosedWarmRoom, Is.True);
            Assert.That(smoky.SmokeHazardWarning, Is.EqualTo(1), "Kapali odada bacasiz ates duman/O2 uyarisi vermeli!");

            // 3) Baca var -> duman = 0 ve yuksek isinma (+24 C)!
            var cozy = smoky;
            cozy.HasRoofChimneyFlue = 1;
            TimberCabinInteriorSystem.EvaluateCabinThermalAndSmoke(ref cozy);
            Assert.That(cozy.SmokeHazardWarning, Is.EqualTo(0));
            Assert.That(cozy.IndoorTempDeltaC, Is.GreaterThanOrEqualTo(20.0f));
        }

        private sealed class WideDugPitSoilSampler : IVoxelSoilSampler
        {
            public bool TrySampleSolidMaterialAt(float worldX, float worldY, float worldZ, out byte materialId)
            {
                float rSq = worldX * worldX + worldZ * worldZ;
                float topY = rSq <= 1.25f * 1.25f ? 98.5f : 100.0f; // 1.25m yaricapli (2.5m genis!), 1.5m derin cukur
                if (worldY < topY)
                {
                    materialId = MaterialIdCatalog.Dirt;
                    return true;
                }
                materialId = MaterialIdCatalog.Air;
                return false;
            }

            public bool TrySampleGroundSurfaceY(float worldX, float worldZ, float referenceY, out float groundSurfaceY, out byte topMaterialId)
            {
                float rSq = worldX * worldX + worldZ * worldZ;
                groundSurfaceY = rSq <= 1.25f * 1.25f ? 98.5f : 100.0f;
                topMaterialId = MaterialIdCatalog.Dirt;
                return true;
            }
        }

        [Test]
        public void TipIn_Wide250cmDugPit_DetectedByMultiRingSamplerAndHoldsPostUpright()
        {
            // Genis (2.5m capli) kazilmis cukurda 0.85m halkasi cukur icinde kalsa bile 1.65m halkasi kenar dudagini yakalamali!
            var widePit = new WideDugPitSoilSampler();
            TipInEvaluation eval = TimberTipInAndJoineryLogic.EvaluateTipInHole(0f, 0f, 99f, widePit);

            Assert.That(eval.Result, Is.Not.EqualTo(TipInPlacementResult.NoHoleFlatGroundTipsOver),
                "2.5m genisligindeki kazilmis cukur duz zemin sayilip kutugu devirmemeli!");
            Assert.That(eval.HoleDepthMeters, Is.EqualTo(1.5f).Within(0.05f));
            Assert.That(eval.SolidWallSides, Is.EqualTo(4));
        }

        [Test]
        public void LogArchCart_Heavy14mFullOakLog_BindsWithoutPayloadRejectionOrLoss()
        {
            var cart = LogArchCartState.CreateEmpty(1);
            var heavyOak = RigidStructurePiece.Create(
                pieceId: 9001,
                kind: RigidTimberPieceKind.Log,
                species: TimberWoodSpecies.Oak,
                lengthM: 14.0f,
                diameterM: 0.54f,
                position: Vector3.Zero,
                rotation: Quaternion.Identity,
                initialState: RigidTimberState.DynamicActive);

            bool bound = LogArchCartPhysics.TryBindFullLog(ref cart, in heavyOak, 0.14f, 1, out string msg);
            Assert.That(bound, Is.True, $"14m kalin mese kutugu arabadan geri alinirken reddedilmemeli: {msg}");
            Assert.That(cart.HasBoundLog, Is.EqualTo(1));
        }
    }
}
