#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using Stratocraft.Character;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Rendering;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class FpsHandsAndItemCatalogTests
    {
        private static string ResolveRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "Assets", "Art", "Generated", "Items")))
                {
                    return dir;
                }
                ParentStep:
                DirectoryInfo? parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
                goto CheckAgain;
                CheckAgain:
                if (Directory.Exists(Path.Combine(dir, "Assets", "Art", "Generated", "Items")))
                {
                    return dir;
                }
                goto ParentStep;
            }
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
        }

        [Test]
        public void Pt06_FpsBareHandsPunchAndHeldItems_StayInFrontCameraFrameAndWithinTwoBoneIkReach()
        {
            const float upperArmLen = 0.295f;
            const float forearmLen = 0.265f;

            // FPS omuz konumlari (kamera yerel uzayinda: (+-0.21, -0.22, -0.02))
            Vector3 rightShoulderCamLocal = new Vector3(0.21f, -0.22f, -0.02f);
            Vector3 leftShoulderCamLocal = new Vector3(-0.21f, -0.22f, -0.02f);
            Vector3 rightPole = Vector3.Normalize(new Vector3(0.70f, -0.72f, -0.35f));
            Vector3 leftPole = Vector3.Normalize(new Vector3(-0.70f, -0.72f, -0.35f));

            // 1) Bos el FPS hazir durus hedefleri kamera on kadrajinda (Z >= 0.34m, Y >= -0.20m) olmali
            Vector3 idleRightPalm = new Vector3(0.20f, -0.175f, 0.37f);
            Vector3 idleLeftPalm = new Vector3(-0.20f, -0.185f, 0.35f);

            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                rightShoulderCamLocal, idleRightPalm, rightPole, upperArmLen, forearmLen,
                out Vector3 idleElbowR, out Vector3 idleWristR);
            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                leftShoulderCamLocal, idleLeftPalm, leftPole, upperArmLen, forearmLen,
                out Vector3 idleElbowL, out Vector3 idleWristL);

            Assert.That(idleWristR.Z, Is.GreaterThan(0.32f), "Sag el bos durusta kameranin onunde gorunur olmali.");
            Assert.That(idleWristR.Y, Is.GreaterThan(-0.24f), "Sag el bos durusta ekranin altinda kaybolmamali.");
            Assert.That(idleWristL.Z, Is.GreaterThan(0.30f), "Sol el bos durusta kameranin onunde gorunur olmali.");
            Assert.That(idleWristL.Y, Is.GreaterThan(-0.24f), "Sol el bos durusta ekranin altinda kaybolmamali.");
            Assert.That(Vector3.Distance(idleRightPalm, idleWristR), Is.LessThan(0.02f));
            Assert.That(Vector3.Distance(idleLeftPalm, idleWristL), Is.LessThan(0.02f));
            Assert.That(float.IsNaN(idleElbowR.X) || float.IsNaN(idleElbowL.X), Is.False);

            // 2) Yumruk zirve aninda (punch01 = 1.0) sag ve sol yumruklar ekranin on-merkez nisangah hizasina cikmali
            Vector3 punchRightPalm = new Vector3(0.035f, -0.065f, 0.54f);
            Vector3 punchLeftPalm = new Vector3(-0.035f, -0.065f, 0.54f);

            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                rightShoulderCamLocal, punchRightPalm, rightPole, upperArmLen, forearmLen,
                out _, out Vector3 punchWristR);
            MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                leftShoulderCamLocal, punchLeftPalm, leftPole, upperArmLen, forearmLen,
                out _, out Vector3 punchWristL);

            Assert.That(punchWristR.Z, Is.GreaterThan(0.46f), "Sag yumruk ileri firladiginda kameranin onunde net gorunmeli.");
            Assert.That(punchWristR.Y, Is.GreaterThan(-0.12f), "Sag yumruk ekran merkez hizasina yukselmeli.");
            Assert.That(MathF.Abs(punchWristR.X), Is.LessThan(0.08f), "Sag yumruk merkeze yakin vurulabilmeli.");
            Assert.That(punchWristL.Z, Is.GreaterThan(0.46f), "Sol yumruk ileri firladiginda kameranin onunde net gorunmeli.");
            Assert.That(punchWristL.Y, Is.GreaterThan(-0.12f), "Sol yumruk ekran merkez hizasina yukselmeli.");

            // 3) Elde tutulan esyalar (tas, dal, cakmaktasi, mesale, kova, iksir, cekic) FPS kadrajinda tam erisilebilir olmali
            Vector3[] heldItemPalms =
            {
                new Vector3(0.22f, -0.155f, 0.43f), // Sapli alet / dal / cekic / keski
                new Vector3(0.18f, -0.165f, 0.45f), // Kova / kazan / kamp atesi / odun
                new Vector3(0.21f, -0.150f, 0.42f), // Matara / iksir / yiyecek / bandaj
                new Vector3(0.21f, -0.155f, 0.43f)  // Tas / cakmaktasi / cevher / kulce
            };

            foreach (Vector3 heldPalm in heldItemPalms)
            {
                MeleeBiomechanicsSolver.SolveTwoBoneArmIk(
                    rightShoulderCamLocal, heldPalm, rightPole, upperArmLen, forearmLen,
                    out Vector3 heldElbow, out Vector3 heldWrist);

                Assert.That(heldWrist.Z, Is.InRange(0.38f, 0.52f));
                Assert.That(heldWrist.Y, Is.InRange(-0.20f, -0.05f));
                Assert.That(Vector3.Distance(heldPalm, heldWrist), Is.LessThan(0.015f));
                Assert.That(float.IsNaN(heldElbow.Y), Is.False);
            }
        }

        [Test]
        public void Pt07_All24BlenderGeneratedItemCatalogGlbs_ExistWithValidColor32AndZeroEmbeddedTextures()
        {
            string repoRoot = ResolveRepoRoot();
            string itemsDir = Path.Combine(repoRoot, "Assets", "Art", "Generated", "Items");
            Assert.That(Directory.Exists(itemsDir), Is.True, $"Items GLB klasoru bulunamadi: {itemsDir}");

            string[] expectedGlbs =
            {
                "item_tool_axe_iron.glb",
                "item_tool_primitive_axe.glb",
                "item_tool_stone_hammer.glb",
                "item_tool_primitive_knife.glb",
                "item_tool_micro_chisel.glb",
                "item_tool_shovel_spade.glb",
                "item_torch_campfire.glb",
                "item_fluid_flask.glb",
                "item_fluid_bucket.glb",
                "item_fluid_cauldron.glb",
                "item_flint_stone.glb",
                "item_rough_stone.glb",
                "item_ore_chunk.glb",
                "item_quartz_cluster.glb",
                "item_metal_ingot.glb",
                "item_natural_stick.glb",
                "item_split_firewood.glb",
                "item_meat_cut.glb",
                "item_wild_berries.glb",
                "item_potion_bottle.glb",
                "item_medical_bandage.glb",
                "item_leather_bellows.glb",
                "item_clay_crucible.glb",
                "item_martial_scroll.glb"
            };

            foreach (string fileName in expectedGlbs)
            {
                string fullPath = Path.Combine(itemsDir, fileName);
                Assert.That(File.Exists(fullPath), Is.True, $"Eksik GLB modeli: {fileName}");

                byte[] bytes = File.ReadAllBytes(fullPath);
                Assert.That(bytes.Length, Is.GreaterThan(1024), $"{fileName} boyutu cok kucuk.");
                uint magic = BitConverter.ToUInt32(bytes, 0);
                Assert.That(magic, Is.EqualTo(0x46546C67u), $"{fileName} gecerli bir glTF 2.0 binary degil.");

                int jsonLen = BitConverter.ToInt32(bytes, 12);
                uint jsonChunkType = BitConverter.ToUInt32(bytes, 16);
                Assert.That(jsonChunkType, Is.EqualTo(0x4E4F534Au));
                string json = Encoding.UTF8.GetString(bytes, 20, jsonLen);

                Assert.That(json, Does.Contain("\"COLOR_0\""), $"{fileName} icinde COLOR_0 vertex color niteligi yok.");
                Assert.That(json, Does.Contain("\"Socket_Grip_Main\""), $"{fileName} icinde Socket_Grip_Main dugumu yok.");
                Assert.That(json, Does.Not.Contain("\"images\""), $"{fileName} icinde gomulu doku (images) olmamali (0 VRAM).");
                Assert.That(json, Does.Not.Contain("\"textures\""), $"{fileName} icinde gomulu doku (textures) olmamali (0 VRAM).");
            }
        }

        [Test]
        public void Pt07_PhysicalItemMeshFactory_ResolvesCatalogGlbMappingsAndEmitsDetailedFallbackMeshes()
        {
            PhysicalItemArchetype[] mappedArchetypes =
            {
                PhysicalItemArchetype.NaturalStick,
                PhysicalItemArchetype.SplitFirewood,
                PhysicalItemArchetype.RoughStone,
                PhysicalItemArchetype.FlintStone,
                PhysicalItemArchetype.QuartzCrystalCluster,
                PhysicalItemArchetype.OreBearingRock,
                PhysicalItemArchetype.RawMeatCut,
                PhysicalItemArchetype.CookedMeatCut,
                PhysicalItemArchetype.WildBerryCluster,
                PhysicalItemArchetype.ClayCruciblePot,
                PhysicalItemArchetype.LeatherBellows,
                PhysicalItemArchetype.MetalIngotBar,
                PhysicalItemArchetype.ToolPrimitiveAxe,
                PhysicalItemArchetype.ToolPrimitiveKnife,
                PhysicalItemArchetype.ToolPrimitiveHammer
            };

            var tris = new List<FacetedVoxelTriangle>(256);
            PhysicalItemShapeDna dna = PhysicalItemShapeDna.CreateNatural(0x12345678u);

            foreach (PhysicalItemArchetype arch in mappedArchetypes)
            {
                bool resolved = PhysicalItemMeshFactory.TryResolveCatalogItemGlbInfo(
                    arch, out string glbFile, out byte templatePrimaryMat);
                Assert.That(resolved, Is.True, $"{arch} icin katalog GLB eslemesi bulunamadi.");
                Assert.That(glbFile, Does.EndWith(".glb"));
                Assert.That(templatePrimaryMat, Is.GreaterThan((byte)0));

                tris.Clear();
                byte primaryMat = PhysicalItemMeshFactory.ResolvePrimaryMaterialId(arch, 0);
                PhysicalItemMeshFactory.EmitArchetypeTriangles(arch, primaryMat, in dna, tris);
                Assert.That(tris.Count, Is.GreaterThanOrEqualTo(12), $"{arch} en az 12 ucgen uretmeli.");
                for (int i = 0; i < tris.Count; i++)
                {
                    Assert.That(tris[i].MaterialId, Is.Not.EqualTo(MaterialIdCatalog.Air));
                }
            }
        }
    }
}
