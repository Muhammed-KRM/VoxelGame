using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Character.Camera;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.UI;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Tests.PlayMode
{
    [TestFixture]
    public sealed class CharacterAndTacticalBackpackPlayTests
    {
        [Test]
        public void PhysicalCharacter_StancesAnd1x1Tunnel_BackpackReducesSpeedTo1Point2MetersPerSec()
        {
            using var chunk = new VoxelChunkComponent(0, 0);
            // Y=10 zemin, Y=11 hava (1 blok = 1.0m yukseklik), Y=12 tavan -> 1x1m tunel
            chunk.SetVoxel(4, 10, 4, new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0)));
            chunk.SetVoxel(4, 11, 4, VoxelCell.Air);
            chunk.SetVoxel(4, 12, 4, new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0)));

            // 1x1m tunelde Standing (1.80m) ve Crouching (1.25m) sigmaz; ProneCrawling (0.65m) sigar
            Assert.That(PhysicalCharacterControllerSystem.CanFitInVoxelClearance(chunk, 4, 11, 4, CharacterStance.Standing), Is.False);
            Assert.That(PhysicalCharacterControllerSystem.CanFitInVoxelClearance(chunk, 4, 11, 4, CharacterStance.Crouching), Is.False);
            Assert.That(PhysicalCharacterControllerSystem.CanFitInVoxelClearance(chunk, 4, 11, 4, CharacterStance.ProneCrawling), Is.True);

            var controller = new PhysicalCharacterControllerSystem();
            var movement = CharacterMovementState.CreateDefault();
            var vitals = CharacterVitalsComponent.CreateDefault();
            var backpack = new TacticalClothBackpackSystem();

            // Acik alanda (headroom = 3) normal yurume = 4.0 m/s
            controller.StepMovement(ref movement, ref vitals, CharacterStance.Standing, (0f, 1f), availableVoxelHeadroomBlocks: 3, deltaTime: 0.016f);
            Assert.That(movement.CurrentSpeedMetersPerSec, Is.EqualTo(4.0f).Within(0.001f));

            // I tusu ile 3B Kumas Canta acildiginda oyun durmaz, hiz %70 duser: 4.0 -> 1.2 m/s
            backpack.ToggleBackpack(ref movement);
            Assert.That(backpack.IsBackpackOpen, Is.True);
            controller.StepMovement(ref movement, ref vitals, CharacterStance.Standing, (0f, 1f), availableVoxelHeadroomBlocks: 3, deltaTime: 0.016f);
            Assert.That(movement.CurrentSpeedMetersPerSec, Is.EqualTo(1.2f).Within(0.001f));

            // Canta icinden zirh kusanma 3.5 sn kanal suresi gerektirir
            var plateArmor = new UniqueEquipmentInstance { InstanceGuid = 9001UL, ArchetypeItemId = 42, MassKg = 14.5f };
            backpack.AddUniqueEquipment(plateArmor);
            Assert.That(backpack.BeginEquipFromBackpack(9001UL, durationSeconds: 3.5f), Is.True);
            Assert.That(backpack.StepEquipChannel(2.0f, out _), Is.False, "2.0 saniyede henuz kusanilmamis olmali.");
            Assert.That(backpack.StepEquipChannel(1.6f, out UniqueEquipmentInstance equipped), Is.True, "3.5 saniye doldugunda kusanilmali.");
            Assert.That(equipped.InstanceGuid, Is.EqualTo(9001UL));
        }

        [Test]
        public void EnclosedOxygenFloodFill_1x1x2SealedBox_DepletesOxygenInExactly30Seconds()
        {
            using var chunk = new VoxelChunkComponent(0, 0);
            uint stoneRaw = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            // 5x5x5 tas kup olustur, ortasinda yalnizca (7,10,7) ve (7,11,7) 1x1x2 = 2 voksel hava birak
            for (int x = 5; x <= 9; x++)
            {
                for (int y = 9; y <= 13; y++)
                {
                    for (int z = 5; z <= 9; z++)
                    {
                        chunk.SetVoxel(x, y, z, new VoxelCell(stoneRaw));
                    }
                }
            }

            chunk.SetVoxel(7, 10, 7, VoxelCell.Air);
            chunk.SetVoxel(7, 11, 7, VoxelCell.Air);

            var oxygenSystem = new EnclosedOxygenFloodFillSystem();
            int airVolume = oxygenSystem.MeasureConnectedAirVolume(chunk, 7, 10, 7);
            Assert.That(airVolume, Is.EqualTo(2), "1x1x2 kapali kutuda bagli hava hacmi tam 2 voksel olmalidir.");

            var vitals = CharacterVitalsComponent.CreateDefault();
            // 15 saniye sonra %50 oksijen kalmali
            oxygenSystem.StepOxygen(ref vitals, airVolume, isHoldingLitTorch: false, deltaTime: 15.0f);
            Assert.That(vitals.LungOxygenPercent, Is.EqualTo(50.0f).Within(0.05f));

            // Toplam 30 saniye sonunda %0 oksijene inmeli ve Hypoxia bayragi aktif olmali
            oxygenSystem.StepOxygen(ref vitals, airVolume, isHoldingLitTorch: false, deltaTime: 15.0f);
            Assert.That(vitals.LungOxygenPercent, Is.EqualTo(0.0f).Within(0.05f));
            Assert.That((vitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.Hypoxia) != 0, Is.True);
        }

        [Test]
        public void BodyHolsters_QuickDropG_Creatures21AndNavSpanHybridLod_OperateWithinSpec()
        {
            // 1. Kilif sureleri (Bel 0.6s, Sirt 1.3s, G Quick-Drop 0.0s)
            var holsters = new BodyHolsterStateMachine();
            holsters.AssignHolsterSlot(HolsterSlotIndex.WaistLeft_Slot1, new UniqueEquipmentInstance { InstanceGuid = 101UL });
            holsters.AssignHolsterSlot(HolsterSlotIndex.BackLeft_Slot3, new UniqueEquipmentInstance { InstanceGuid = 303UL });

            float backDrawTime = holsters.RequestDrawSlot(HolsterSlotIndex.BackLeft_Slot3);
            Assert.That(backDrawTime, Is.EqualTo(BodyHolsterStateMachine.BackDrawSeconds).Within(0.001f));
            holsters.StepHolster(1.35f);
            Assert.That(holsters.ActiveDrawnSlot, Is.EqualTo(HolsterSlotIndex.BackLeft_Slot3));

            // G Tusu ile 0.0 sn'de eldeki sirttan cekilmis mizragi yere atip bel kilifini 0.6 sn'de cek
            bool dropped = holsters.ExecuteQuickDropG(out UniqueEquipmentInstance droppedSpear);
            Assert.That(dropped, Is.True);
            Assert.That(droppedSpear.InstanceGuid, Is.EqualTo(303UL));
            Assert.That(holsters.ActiveDrawnSlot, Is.EqualTo(HolsterSlotIndex.None));

            float waistDrawTime = holsters.RequestDrawSlot(HolsterSlotIndex.WaistLeft_Slot1);
            Assert.That(waistDrawTime, Is.EqualTo(BodyHolsterStateMachine.WaistDrawSeconds).Within(0.001f));

            // 2. 3-Bolgeli Kamera Debriyaji (Camera-Clutch)
            Assert.That(CameraClutchController.EvaluatePiecewiseClutchDelta(0.20f), Is.EqualTo(0.0f), "1. Bolge (<=0.35 rad) olu bolge olmalidir.");
            Assert.That(CameraClutchController.EvaluatePiecewiseClutchDelta(0.60f), Is.GreaterThan(0.0f));

            // 3. 21 Canli Katalogu, Uzuv Kopma Cezasi ve Anti-Pillar-Cheese (TARGET_SUPPORT_PILLARS)
            Assert.That(Creature21BehaviorSystem.All21Creatures.Count, Is.EqualTo(21));
            CreatureArchetypeDefinition troll = Creature21BehaviorSystem.GetById(10); // mob_glacier_troll
            var (afterRightArmCut, _) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(troll.InitialCapabilities, "UpperArm.R");
            Assert.That((afterRightArmCut & AnatomyCapabilityFlags.HasRightClawOrWeapon) == 0, Is.True);

            using var chunk = new VoxelChunkComponent(0, 0);
            // Oyuncu (8, 10, 8) noktasina bir sütun (OyuncuItemBlogu) dikmis
            chunk.SetVoxel(8, 10, 8, new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0)));

            var creatureSys = new Creature21BehaviorSystem();
            bool struckPillar = creatureSys.TryExecuteSupportPillarStrike(
                chunk, 8, 10, 8, playerVerticalOffsetAboveCreature: 4.0f, troll.InitialCapabilities, siegeDamageSteps: 16);
            Assert.That(struckPillar, Is.True);
            Assert.That(chunk.GetVoxel(8, 10, 8).IsSolid, Is.False, "Yaratik sütunun taban blogunu kirarak Domino yikimini tetiklemelidir.");

            // 4. 4-Byte NavSpan ve Hibrit AI LoD Histerezis
            var navGrid = new NavSpanColumnSummary[NavSpanRebuildJob.ColumnsPerChunk];
            chunk.SetVoxel(2, 5, 2, new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0)));
            NavSpanRebuildJob.Execute(chunk, navGrid);
            Assert.That(navGrid[2 * 16 + 2].SurfaceY, Is.EqualTo(6));

            AiSimulationTier tier = HybridAiLodPromotionSystem.EvaluateTierWithHysteresis(AiSimulationTier.Tier0_Full3DEntity, 140.0f);
            Assert.That(tier, Is.EqualTo(AiSimulationTier.Tier1_NavSpanVatCrowd));
            // 125m'de histerezis (128 - 8 = 120m) nedeniyle hala Tier 1 kalmali
            Assert.That(HybridAiLodPromotionSystem.EvaluateTierWithHysteresis(tier, 125.0f), Is.EqualTo(AiSimulationTier.Tier1_NavSpanVatCrowd));
            // 115m'de Tier 0'a terfi etmeli
            Assert.That(HybridAiLodPromotionSystem.EvaluateTierWithHysteresis(tier, 115.0f), Is.EqualTo(AiSimulationTier.Tier0_Full3DEntity));
        }

        [Test]
        public void ParkourSlide15Ep_Stamina15PercentLock_CrossChunkOxygenGratingBarotrauma_Wet3xColdAnd3sHudHide()
        {
            // 1. 5-Duzlemli CollideAndSlide: 2 duvar kosesinde (X ve Z duvarlari) yalnizca Y ekseni boyunca kayar, 3 duzlem kesisiminde tam durur
            Span<Vector3> cornerPlanes = stackalloc Vector3[3]
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, 1f, 0f)
            };
            Vector3 cornerSlide = PhysicalCharacterControllerSystem.SolveCollideAndSlide5Planes(new Vector3(3f, -2f, 4f), cornerPlanes.Slice(0, 2));
            Assert.That(cornerSlide.X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(cornerSlide.Z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(cornerSlide.Y, Is.EqualTo(-2f).Within(1e-4f));

            Vector3 threePlaneStop = PhysicalCharacterControllerSystem.SolveCollideAndSlide5Planes(new Vector3(3f, -2f, 4f), cornerPlanes);
            Assert.That(threePlaneStop.Length(), Is.EqualTo(0f).Within(1e-4f));

            // 2. 15 EP Kayma (Slide) ve %15 Stamina Tukenme Histerezis Kilidi
            var controller = new PhysicalCharacterControllerSystem();
            var movement = CharacterMovementState.CreateDefault();
            var vitals = CharacterVitalsComponent.CreateDefault();
            Assert.That(PhysicalCharacterControllerSystem.TryInitiateSlide(ref movement, ref vitals, (0f, 1f)), Is.True);
            Assert.That(vitals.Stamina, Is.EqualTo(85.0f).Within(0.01f));
            Assert.That(movement.Stance, Is.EqualTo(CharacterStance.Sliding));

            vitals.Stamina = 0.0f;
            controller.StepMovement(ref movement, ref vitals, CharacterStance.Sprinting, (0f, 1f), availableVoxelHeadroomBlocks: 3, deltaTime: 0.016f);
            Assert.That(movement.IsExhaustedStaminaLock, Is.EqualTo(1));
            Assert.That(movement.Stance, Is.Not.EqualTo(CharacterStance.Sprinting), "Stamina sifirlandiginda depar kilitlenmelidir.");

            vitals.Stamina = 10.0f; // %10 (< %15 esik) -> hala kilitli!
            movement.SlideRemainingSeconds = 0f;
            controller.StepMovement(ref movement, ref vitals, CharacterStance.Sprinting, (0f, 1f), availableVoxelHeadroomBlocks: 3, deltaTime: 0.016f);
            Assert.That(movement.IsExhaustedStaminaLock, Is.EqualTo(1));
            Assert.That(movement.Stance, Is.EqualTo(CharacterStance.Standing));

            vitals.Stamina = 16.0f; // >= %15 esik -> kilit acilir ve depar (7.0 m/s) aktif olur!
            controller.StepMovement(ref movement, ref vitals, CharacterStance.Sprinting, (0f, 1f), availableVoxelHeadroomBlocks: 3, deltaTime: 0.016f);
            Assert.That(movement.IsExhaustedStaminaLock, Is.EqualTo(0));
            Assert.That(movement.CurrentSpeedMetersPerSec, Is.EqualTo(7.0f).Within(0.01f));

            // 3. Coklu-Chunk Kapali Magara O2 BFS + Demir Izgara (IronGrating=125) Gecirgenligi + Boyle-Mariotte >50m Basinc Ezilmesi
            using var chunkA = new VoxelChunkComponent(0, 0);
            using var chunkB = new VoxelChunkComponent(1, 0);
            uint stoneRaw = VoxelBitUtility.PackStandard(MaterialIdCatalog.Stone, 127, 1, 0, BlokKokenDurumu.DogalKutle, 0);

            for (int x = 13; x <= 15; x++)
                for (int y = 18; y <= 22; y++)
                    for (int z = 6; z <= 10; z++)
                        chunkA.SetVoxel(x, y, z, new VoxelCell(stoneRaw));

            for (int x = 0; x <= 2; x++)
                for (int y = 18; y <= 22; y++)
                    for (int z = 6; z <= 10; z++)
                        chunkB.SetVoxel(x, y, z, new VoxelCell(stoneRaw));

            // Chunk sinirini (chunkA x=15 <-> chunkB x=0) Demir Izgara (IronGrating) ile ayiran 3 hucrelik kapali tunel
            chunkA.SetVoxel(14, 20, 8, VoxelCell.Air);
            chunkA.SetVoxel(15, 20, 8, new VoxelCell(VoxelBitUtility.PackStandard(MaterialIdCatalog.IronGrating, 127, 1, 0, BlokKokenDurumu.OyuncuItemBlogu, 0)));
            chunkB.SetVoxel(0, 20, 8, VoxelCell.Air);

            var worldMap = new Dictionary<(int cx, int cz), VoxelChunkComponent>
            {
                [(0, 0)] = chunkA,
                [(1, 0)] = chunkB
            };

            var oxygenSys = new EnclosedOxygenFloodFillSystem();
            int crossChunkVolume = oxygenSys.MeasureConnectedAirVolumeWorld(chunkA, worldMap, startWorldX: 14, startY: 20, startWorldZ: 8);
            Assert.That(crossChunkVolume, Is.EqualTo(3), "Chunk sinirindaki kapali tunel gokyuzune sizmamali ve Demir Izgara uzerinden 3 hucre olcmelidir.");

            var diverVitals = CharacterVitalsComponent.CreateDefault();
            diverVitals.SubmergedDepthMeters = 60.0f; // > 50m -> Barotravma basinc ezilmesi!
            oxygenSys.StepOxygen(ref diverVitals, connectedAirVoxels: 0, isHoldingLitTorch: false, deltaTime: 2.0f);
            Assert.That((diverVitals.ActiveDiseaseFlags & (byte)BiologicalAfflictionFlags.BarotraumaCrush) != 0, Is.True);
            Assert.That(diverVitals.CurrentHp, Is.LessThan(diverVitals.MaxHp));

            // 4. Surekli Islaklik (Wetness01) Sogukta 3x Isi Kaybi ve 3.0s Otomatik HUD Gizleme
            var thermal = new ThermalAndBiologySystem();
            var dryVitals = CharacterVitalsComponent.CreateDefault();
            var wetVitals = CharacterVitalsComponent.CreateDefault();
            wetVitals.Wetness01 = 1.0f;
            wetVitals.SubmergedDepthMeters = 0.5f;

            thermal.StepBiology(ref dryVitals, ambientTempCelsius: -10.0f, armorThermalInsulation01: 0.4f, isSubmergedOrInRain: false, deltaTime: 5.0f);
            thermal.StepBiology(ref wetVitals, ambientTempCelsius: -10.0f, armorThermalInsulation01: 0.4f, isSubmergedOrInRain: true, deltaTime: 5.0f);
            float dryDrop = 37.0f - dryVitals.CoreBodyTempCelsius;
            float wetDrop = 37.0f - wetVitals.CoreBodyTempCelsius;
            Assert.That(wetDrop, Is.GreaterThan(dryDrop * 2.5f), "Islak karakter sogukta kuru karaktere gore ~3x daha hizli isi kaybetmelidir.");

            var hud = new HudAndCrosshairController();
            var calmVitals = CharacterVitalsComponent.CreateDefault();
            var calmMove = CharacterMovementState.CreateDefault();
            Assert.That(hud.BuildHudSnapshot(in calmVitals, in calmMove, HolsterSlotIndex.None, deltaTime: 2.0f).HudOpacity01, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(hud.BuildHudSnapshot(in calmVitals, in calmMove, HolsterSlotIndex.None, deltaTime: 2.0f).HudOpacity01, Is.LessThan(0.1f), "3.0s tam dolu sakinlikten sonra HUD gizlenmelidir.");
        }
    }
}
