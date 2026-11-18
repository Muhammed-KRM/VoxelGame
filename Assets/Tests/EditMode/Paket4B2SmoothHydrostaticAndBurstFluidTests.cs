#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    /// <summary>
    /// Paket 4B.2 (Faz 4B.2-A .. Faz 4B.2-E):
    /// Puruzsuz Yamac Su Geometrisi, Kurganov-Petrova Sahil Hidrostatigi, Sualti/Yan Duvar Sifirlama
    /// ve 192-Bit Mask + 2B Planar Greedy Sifir-GC Akiskan Optimizasyonu T01..T20 kabul test paketi.
    /// Hem .NET 10 Standalone hem Unity 6 EditMode NUnit altinda birebir calisir.
    /// </summary>
    [TestFixture]
    public sealed class Paket4B2SmoothHydrostaticAndBurstFluidTests
    {
        private static uint PackSolid(byte matId, byte density = VoxelBitUtility.FullDensity) =>
            VoxelBitUtility.PackStandard(matId, density, 0, 0, BlokKokenDurumu.KaliciYapi, 0);

        private static uint PackArchitecturalShape(byte matId, byte shapeId) =>
            VoxelBitUtility.PackStandard(matId, VoxelBitUtility.FullDensity, shapeId, 0, BlokKokenDurumu.KaliciYapi, 0);

        // =====================================================================
        // T01 (AK-1): Hidrostatik Taban Kotu (Altinda 255 Dolu Su Sutunu == y * 255)
        // =====================================================================
        [Test]
        public void T01_HydrostaticBedUnits_RecognizesFullFluidBelow_AndPreventsReverseShoreDrain()
        {
            ushort fullWaterBelow = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;

            int deepSeaBedAt90 = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(
                belowSolidRaw: 0u,
                belowFluidRaw: fullWaterBelow,
                currentSolidRaw: 0u,
                activeFluidType: FluidType.Water,
                y: 90);

            int cliffVoidBedAt90 = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(
                belowSolidRaw: 0u,
                belowFluidRaw: 0,
                currentSolidRaw: 0u,
                activeFluidType: FluidType.Water,
                y: 90);

            Assert.That(deepSeaBedAt90, Is.EqualTo(90 * 255),
                "A cell supported by a full (255) water column at y-1 must have hydrostatic bed == y * 255!");
            Assert.That(cliffVoidBedAt90, Is.EqualTo(89 * 255),
                "An unsupported empty drop at y-1 must keep bed == (y - 1) * 255 for gravity drainage.");

            // Kiyi kum hucresi (x=4, y=6) ile derin su sutunu (x=5, y=5..6) esit head dengesindeyken ters akis 0 olmalidir
            const int sx = 12, sy = 10, sz = 8;
            var voxels = new uint[sx * sy * sz];
            var fluids = new ushort[sx * sy * sz];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    voxels[(2 * sz + z) * sx + x] = stone;
                    if (x == 0 || x == sx - 1 || z == 0 || z == sz - 1 || x <= 3)
                    {
                        for (int y = 3; y <= 7; y++) voxels[(y * sz + z) * sx + x] = stone;
                    }
                }
            }

            // x = 4: sig SDF kum sahil (y=5 kati kum d=96), x = 5..10: derin deniz (y=3..5 su=255)
            uint sandBeach = PackSolid(MaterialIdCatalog.Sand, 96);
            int sandBed6 = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(sandBeach, 0, 0u, FluidType.Water, 6);
            byte shoreMassAt6 = FluidPorosityUtility.ComputeMassNeededForTargetHeadUnits255(sandBed6, 6, 6 * 255 + 40, 255);
            byte seaTopMassAt6 = 40; // Derin deniz head = 6*255 + 40 == sig sahil head!

            for (int z = 1; z < sz - 1; z++)
            {
                voxels[(3 * sz + z) * sx + 4] = stone;
                voxels[(4 * sz + z) * sx + 4] = stone;
                voxels[(5 * sz + z) * sx + 4] = sandBeach;
                fluids[(6 * sz + z) * sx + 4] = FluidCell16.Pack(FluidType.Water, shoreMassAt6, isSettled: false).RawValue;

                for (int x = 5; x <= sx - 2; x++)
                {
                    fluids[(3 * sz + z) * sx + x] = fullWaterBelow;
                    fluids[(4 * sz + z) * sx + x] = fullWaterBelow;
                    fluids[(5 * sz + z) * sx + x] = fullWaterBelow;
                    fluids[(6 * sz + z) * sx + x] = FluidCell16.Pack(FluidType.Water, seaTopMassAt6, isSettled: false).RawValue;
                }
            }

            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                voxels, fluids, sx, sy, sz, out _, out _, stepParity: 0);

            byte afterShoreMass = new FluidCell16(fluids[(6 * sz + 4) * sx + 4]).Mass;
            Assert.That(Math.Abs(afterShoreMass - shoreMassAt6), Is.LessThanOrEqualTo(1),
                "Shallow beach cell at equal hydrostatic head must never drain into deep sea column!");
        }

        // =====================================================================
        // T02 (AK-1): Capraz-Katman (ax, y) <-> (bx, y-1) Sahil Dengeleme Koprusu & dM == 0
        // =====================================================================
        [Test]
        public void T02_CrossLayerCoastalBridge_RefillsDriedShallowBeachFromLowerSeaLayer_WithExactMassConservation()
        {
            const int sx = 12, sy = 10, sz = 8;
            var voxels = new uint[sx * sy * sz];
            var fluids = new ushort[sx * sy * sz];
            uint stone = PackSolid(MaterialIdCatalog.Stone);
            uint shallowSand = PackSolid(MaterialIdCatalog.Sand, 88); // y=5 SDF kum: ust yuzeyi ~5.40m (< 6.00m deniz kotu!)

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    voxels[(2 * sz + z) * sx + x] = stone;
                    if (x == 0 || x == sx - 1 || z == 0 || z == sz - 1 || x <= 3)
                    {
                        for (int y = 3; y <= 7; y++) voxels[(y * sz + z) * sx + x] = stone;
                    }
                }
            }

            // x = 4: sig kum sahil (y=5 kati kum, y=6 baslangicta KURU m=0!)
            // x = 5..10: derin deniz (y=3..5 m=255 dolu, y=6 KURU -> su yuzeyi tam 6.00m = 6*255!)
            long initialMass = 0;
            for (int z = 1; z < sz - 1; z++)
            {
                voxels[(3 * sz + z) * sx + 4] = stone;
                voxels[(4 * sz + z) * sx + 4] = stone;
                voxels[(5 * sz + z) * sx + 4] = shallowSand;

                for (int x = 5; x <= sx - 2; x++)
                {
                    for (int y = 3; y <= 5; y++)
                    {
                        fluids[(y * sz + z) * sx + x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                        initialMass += 255;
                    }
                }
            }

            for (int tick = 0; tick < 60; tick++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    voxels, fluids, sx, sy, sz, out _, out _, stepParity: tick & 1);
            }

            long finalMass = 0;
            for (int i = 0; i < fluids.Length; i++)
            {
                finalMass += new FluidCell16(fluids[i]).Mass;
            }

            Assert.That(finalMass, Is.EqualTo(initialMass), "Cross-layer coastal equilibration must preserve exact mass (dM == 0)!");

            byte refilledBeachMass = new FluidCell16(fluids[(6 * sz + 4) * sx + 4]).Mass;
            Assert.That(refilledBeachMass, Is.GreaterThan((byte)20),
                "Water from (bx, y-1) deep sea layer must climb across the step into (ax, y) shallow beach cell!");

            int bedA = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(shallowSand, 0, 0u, FluidType.Water, 6);
            int headA = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedA, 6, refilledBeachMass, 255);
            byte seaMassY5 = new FluidCell16(fluids[(5 * sz + 4) * sx + 5]).Mass;
            byte seaMassY6 = new FluidCell16(fluids[(6 * sz + 4) * sx + 5]).Mass;
            int headB = seaMassY6 > 0
                ? FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(6 * 255, 6, seaMassY6, 255)
                : FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(5 * 255, 5, seaMassY5, 255);

            Assert.That(Math.Abs(headA - headB), Is.LessThanOrEqualTo(3),
                $"Hydrostatic head between shallow beach ({headA}) and adjacent sea ({headB}) must equilibrate without step jump!");
        }

        // =====================================================================
        // T03 (AK-2): Kumsalda ve Derin Deniz Gecisinde <= 0.002 m Ayna Duzlugu (0.915m Kambur Yok!)
        // =====================================================================
        [Test]
        public void T03_CoastalToDeepSeaMesh_MaintainsMirrorFlatSurfaceWithin2Millimeters()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            // Deniz kotu: Y = 8.000m (y < 8 tam 255 dolu; x=2 kuru sahil; x=3..6 sig SDF kum y=7; x=7..14 derin deniz y=4)
            const int seaY = 8;
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = stone;

                    if (x <= 2)
                    {
                        for (int y = 4; y <= 7; y++) solids[(y << 8) | (z << 4) | x] = stone;
                        solids[(8 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Sand, 108); // Kuru sahil > 8.0m
                    }
                    else if (x <= 6)
                    {
                        for (int y = 4; y <= 6; y++) solids[(y << 8) | (z << 4) | x] = stone;
                        byte sandDens = (byte)(116 - (x - 3) * 12); // 116, 104, 92, 80 -> yumusak alcalan kum sahil
                        uint belowSand = PackSolid(MaterialIdCatalog.Sand, sandDens);
                        solids[(7 << 8) | (z << 4) | x] = belowSand;

                        int bedUnits = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(belowSand, 0, 0u, FluidType.Water, seaY);
                        byte neededMass = FluidPorosityUtility.ComputeMassNeededForTargetHeadUnits255(bedUnits, seaY, seaY * 255, 255);
                        fluids[(seaY << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, neededMass, isSettled: true).RawValue;
                    }
                    else
                    {
                        // Derin deniz: y = 4..7 tam 255 dolu, y = 8 bos -> su yuzeyi tam 8.000m!
                        for (int y = 4; y < seaY; y++)
                        {
                            fluids[(y << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                        }
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            float minTopY = float.MaxValue;
            float maxTopY = float.MinValue;
            int topVertexCount = 0;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                if (tri.C0.Normal.Y < 0.75f && tri.C1.Normal.Y < 0.75f && tri.C2.Normal.Y < 0.75f) continue;

                InspectSeaTopVertex(in tri.C0, ref minTopY, ref maxTopY, ref topVertexCount);
                InspectSeaTopVertex(in tri.C1, ref minTopY, ref maxTopY, ref topVertexCount);
                InspectSeaTopVertex(in tri.C2, ref minTopY, ref maxTopY, ref topVertexCount);
            }

            Assert.That(topVertexCount, Is.GreaterThan(12));
            Assert.That(maxTopY - minTopY, Is.LessThanOrEqualTo(0.025f),
                $"Coastal-to-deep-sea top surface spread ({maxTopY - minTopY:F4}m, [{minTopY:F4}..{maxTopY:F4}]) must be mirror-flat (zero 0.915m hump and zero shore-feathering dip)!");
            Assert.That(MathF.Abs(((minTopY + maxTopY) * 0.5f) - 8.0f), Is.LessThanOrEqualTo(0.015f));

            static void InspectSeaTopVertex(in FluidCornerSample c, ref float minY, ref float maxY, ref int count)
            {
                if (c.Normal.Y < 0.75f) return;
                // Chunk dis kenar sinirlarindan icerideki (z=2..14, x=3..14) gercek sahil-deniz gecis kesiti
                if (c.PositionCurr.Z < 2.0f || c.PositionCurr.Z > 14.0f || c.PositionCurr.X > 14.0f) return;
                if (c.PositionCurr.Y < minY) minY = c.PositionCurr.Y;
                if (c.PositionCurr.Y > maxY) maxY = c.PositionCurr.Y;
                count++;
            }
        }

        // =====================================================================
        // T04 (AK-3): WorldGen Dogal Deniz Kotu Y == 90.000m Kalibrasyonu
        // =====================================================================
        [Test]
        public void T04_WorldGenNaturalSea_SpawnsExact90MeterSeaPlane_MatchingShallowCoastHydrostatics()
        {
            Assert.That(StratocraftFloatingIslandWorldGen.SeaLevelY, Is.EqualTo(90));

            int oldSeed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;
            StratocraftFloatingIslandWorldGen.ActiveWorldSeed = 1453;
            try
            {
                var salt = new ChunkSalinityBitset();
                int testedSeaColumns = 0;
                int testedShallowBeachColumns = 0;

                // Kita icinde dogal tuzlu deniz (continentalness < -0.18) iceren bir chunk bul ve dogrula
                for (int cz = -24; cz <= 24 && testedSeaColumns < 32; cz += 3)
                {
                    for (int cx = -24; cx <= 24 && testedSeaColumns < 32; cx += 3)
                    {
                        StratocraftFloatingIslandWorldGen.SampleClimateAndTectonicFields(
                            cx * 16 + 8, cz * 16 + 8,
                            out _, out _, out float cont, out _, out float smallIsl, out _);
                        if (cont > -0.30f || smallIsl > 0.1f) continue;

                        (uint[] voxels, ushort[] fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz, salt);
                        int maxY = fluids.Length >> 8;

                        for (int col = 0; col < 256; col++)
                        {
                            int lx = col & 15;
                            int lz = col >> 4;
                            if (!salt.IsSaltWater(cx, cz, lx, 89, lz) && !salt.IsSaltWater(cx, cz, lx, 90, lz))
                            {
                                continue;
                            }

                            int solidFloorY = -1;
                            for (int y = 92; y >= 1; y--)
                            {
                                if (VoxelBitUtility.IsPhysicallySolid(voxels[(y << 8) | col]))
                                {
                                    solidFloorY = y;
                                    break;
                                }
                            }

                            if (solidFloorY < 89)
                            {
                                byte mass89 = new FluidCell16(fluids[(89 << 8) | col]).Mass;
                                byte mass90 = (90 < maxY) ? new FluidCell16(fluids[(90 << 8) | col]).Mass : (byte)0;
                                Assert.That(mass89, Is.EqualTo((byte)255), "Deep sea at y=89 must be 255 full!");
                                Assert.That(mass90, Is.EqualTo((byte)0), "Deep sea at y=90 must be 0 so surface sits at 90.000m (not 91.000m)!");
                                testedSeaColumns++;
                            }
                            else if (solidFloorY == 89 && 90 < maxY)
                            {
                                byte mass90 = new FluidCell16(fluids[(90 << 8) | col]).Mass;
                                if (mass90 > 0)
                                {
                                    int bed90 = FluidPorosityUtility.ComputeCellHydrostaticBedUnits255(
                                        voxels[(89 << 8) | col], 0, voxels[(90 << 8) | col], FluidType.Water, 90);
                                    byte cap90 = FluidPorosityUtility.ComputeCellFluidCapacity(voxels[(90 << 8) | col]);
                                    int headUnits = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bed90, 90, mass90, cap90);
                                    Assert.That(Math.Abs(headUnits - (90 * 255)), Is.LessThanOrEqualTo(3),
                                        "Shallow beach water at y=90 must match exact 90.000m sea plane!");
                                    testedShallowBeachColumns++;
                                }
                            }
                        }
                    }
                }

                Assert.That(testedSeaColumns + testedShallowBeachColumns, Is.GreaterThan(0),
                    "WorldGen scan must locate and validate natural sea columns at SeaLevelY == 90.");
            }
            finally
            {
                StratocraftFloatingIslandWorldGen.ActiveWorldSeed = oldSeed;
            }
        }

        // =====================================================================
        // T05 (AK-4): Duz Egimli SurfaceNets Yamacinda KesintisizEgimli Tabaka & y -> y-1 Dikis < 0.002m
        // =====================================================================
        [Test]
        public void T05_SmoothSurfaceNetsSlope_EmitsSlopedWaterSheetAlignedWithTerrain_WithoutStairSteps()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            // +X yonunde duzgun alcalan (%25 egim: her blokta -0.25m, x=7->8 noktasinda y=7 -> y=6 katman sinirini gecer!)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float h = 8.20f - (x - 2) * 0.25f; // x=2: 8.20m, x=6: 7.20m, x=7: 6.95m, x=10: 6.20m
                    h = Math.Clamp(h, 4.2f, 9.2f);
                    int floorY = (int)MathF.Floor(h);
                    float frac = h - floorY;

                    for (int y = 0; y < floorY; y++)
                    {
                        solids[(y << 8) | (z << 4) | x] = fullRock;
                    }

                    byte slopeDens = (byte)Math.Clamp(66 + (int)MathF.Round(frac * 58.0f), 66, 124);
                    solids[(floorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, slopeDens);

                    if (x >= 3 && x <= 11 && z >= 5 && z <= 10)
                    {
                        int waterY = floorY + 1;
                        fluids[(waterY << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 16, isSettled: false).RawValue;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(16));

            // 1) Merkez seritteki (z=7..8, x=4..10) ust ucgenlerin normali yatay (0,1,0) basamak degil, +X egimli olmalidir!
            Vector3 expectedTerrainNormal = Vector3.Normalize(new Vector3(0.25f, 1.0f, 0.0f));
            float angleSumDeg = 0f;
            int centerTriCount = 0;

            // 2) x == 7.0f (y=8 -> y=7 voksel katman gecis siniri!) uzerindeki koselerde dikey kopma 0 olmalidir!
            float minSeamYAt7 = float.MaxValue;
            float maxSeamYAt7 = float.MinValue;
            int seamSamples = 0;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                if (tri.C0.Normal.Y < 0.6f) continue;

                Vector3 centroid = (tri.C0.PositionCurr + tri.C1.PositionCurr + tri.C2.PositionCurr) / 3.0f;
                if (centroid.X >= 4.5f && centroid.X <= 9.5f && centroid.Z >= 7.0f && centroid.Z <= 9.0f)
                {
                    Vector3 faceNorm = Vector3.Normalize(Vector3.Cross(
                        tri.C1.PositionCurr - tri.C0.PositionCurr,
                        tri.C2.PositionCurr - tri.C0.PositionCurr));
                    if (faceNorm.Y < 0f) faceNorm = -faceNorm;

                    float dot = Math.Clamp(Vector3.Dot(faceNorm, expectedTerrainNormal), -1f, 1f);
                    float angleDeg = MathF.Acos(dot) * (180.0f / MathF.PI);
                    angleSumDeg += angleDeg;
                    centerTriCount++;
                }

                CheckSeamAtX7Z8(in tri.C0, ref minSeamYAt7, ref maxSeamYAt7, ref seamSamples);
                CheckSeamAtX7Z8(in tri.C1, ref minSeamYAt7, ref maxSeamYAt7, ref seamSamples);
                CheckSeamAtX7Z8(in tri.C2, ref minSeamYAt7, ref maxSeamYAt7, ref seamSamples);
            }

            Assert.That(centerTriCount, Is.GreaterThan(4));
            float meanNormalDeviationDeg = angleSumDeg / centerTriCount;
            Assert.That(meanNormalDeviationDeg, Is.LessThanOrEqualTo(4.0f),
                $"Sloped SurfaceNets water sheet normal must follow terrain slope within <= 4 deg (actual {meanNormalDeviationDeg:F2} deg)!");

            Assert.That(seamSamples, Is.GreaterThanOrEqualTo(2));
            Assert.That(maxSeamYAt7 - minSeamYAt7, Is.LessThanOrEqualTo(0.002f),
                $"Cross-layer y -> y-1 slope transition at x=7.0 must be C0 watertight (< 0.002m jump, actual {maxSeamYAt7 - minSeamYAt7:F5}m)!");

            static void CheckSeamAtX7Z8(in FluidCornerSample s, ref float minY, ref float maxY, ref int count)
            {
                if (s.Normal.Y < 0.6f) return;
                if (MathF.Abs(s.PositionCurr.X - 7.0f) < 0.02f && MathF.Abs(s.PositionCurr.Z - 8.0f) < 0.02f)
                {
                    if (s.PositionCurr.Y < minY) minY = s.PositionCurr.Y;
                    if (s.PositionCurr.Y > maxY) maxY = s.PositionCurr.Y;
                    count++;
                }
            }
        }

        // =====================================================================
        // T06 (AK-5): 45 Derece Diyagonal Yamac Kanalinda Marching Squares Ic L-Kose Koprusu
        // =====================================================================
        [Test]
        public void T06_DiagonalSlopeChannel_BridgesConcaveLCornersWith45DegreeDiagonalTriangles()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            // Dogal SurfaceNets zemin ustunde 45 derece diyagonal dere hatti: (6,6)->(7,6)->(7,7)->(8,7)->(8,8)->(9,8)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 4; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    solids[(5 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, 96);
                }
            }

            (int x, int z)[] diagCells =
            {
                (6, 6), (7, 6), (7, 7), (8, 7), (8, 8), (9, 8), (9, 9)
            };
            foreach (var (cx, cz) in diagCells)
            {
                fluids[(6 << 8) | (cz << 4) | cx] = FluidCell16.Pack(FluidType.Water, 36, isSettled: false).RawValue;
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            // (7,8) kuru hucresinin ic L-kosesinde (x ~ [7..8], z ~ [8..9]) 45 derece diyagonal kenarli pah ucgeni uretilmeli!
            int diagonalBridgeEdges = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                if (tri.C0.Normal.Y < 0.5f) continue;

                CountDiagonalEdgeInDryLCell(tri.C0.PositionCurr, tri.C1.PositionCurr, ref diagonalBridgeEdges);
                CountDiagonalEdgeInDryLCell(tri.C1.PositionCurr, tri.C2.PositionCurr, ref diagonalBridgeEdges);
                CountDiagonalEdgeInDryLCell(tri.C2.PositionCurr, tri.C0.PositionCurr, ref diagonalBridgeEdges);
            }

            Assert.That(diagonalBridgeEdges, Is.GreaterThan(0),
                "Marching Squares concave L-corner bridge must emit diagonal (~45 deg) shore edges across stepped L-turns!");

            static void CountDiagonalEdgeInDryLCell(Vector3 a, Vector3 b, ref int count)
            {
                Vector3 mid = (a + b) * 0.5f;
                // (6,7), (7,8) veya (8,9) ic L-kose bolgeleri
                if (mid.X >= 6.0f && mid.X <= 9.5f && mid.Z >= 6.0f && mid.Z <= 9.5f)
                {
                    float dx = MathF.Abs(b.X - a.X);
                    float dz = MathF.Abs(b.Z - a.Z);
                    if (dx > 0.20f && dz > 0.20f && MathF.Abs(dx - dz) < 0.25f)
                    {
                        count++;
                    }
                }
            }
        }

        // =====================================================================
        // T07 (AK-6): Kucuk Suyun Kuculmesi (Mass = 2 -> Kalinlik <= 0.015m & XZ Alani <= 0.25 m^2)
        // =====================================================================
        [Test]
        public void T07_MicroRainPuddle_ShrinksBothThicknessAndHorizontalFootprintArea()
        {
            float thickMass2 = FluidSurfaceSkirtMesher.MassToFluidThickness(2);
            Assert.That(thickMass2, Is.LessThanOrEqualTo(0.015f),
                $"Mass=2 thickness ({thickMass2:F4}m) must be <= 0.015m instead of flooding at 0.034m+!");

            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    solids[(4 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, 96);
                }
            }

            float bedY = FluidPorosityUtility.ComputeColumnSurfaceNetsBedMeters(solids[(4 << 8) | (8 << 4) | 8], 0u, 5);
            fluids[(5 << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 2, isSettled: true).RawValue;

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(32);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            float totalHorizontalAreaM2 = 0f;
            float maxHeightAboveBed = 0f;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                if (tri.C0.Normal.Y < 0.5f) continue;

                Vector2 p0 = new Vector2(tri.C0.PositionCurr.X, tri.C0.PositionCurr.Z);
                Vector2 p1 = new Vector2(tri.C1.PositionCurr.X, tri.C1.PositionCurr.Z);
                Vector2 p2 = new Vector2(tri.C2.PositionCurr.X, tri.C2.PositionCurr.Z);
                float cross2D = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                totalHorizontalAreaM2 += 0.5f * MathF.Abs(cross2D);

                maxHeightAboveBed = MathF.Max(maxHeightAboveBed, tri.C0.PositionCurr.Y - bedY);
                maxHeightAboveBed = MathF.Max(maxHeightAboveBed, tri.C1.PositionCurr.Y - bedY);
                maxHeightAboveBed = MathF.Max(maxHeightAboveBed, tri.C2.PositionCurr.Y - bedY);
            }

            Assert.That(totalHorizontalAreaM2, Is.LessThanOrEqualTo(0.25f),
                $"Mass=2 micro-puddle horizontal area ({totalHorizontalAreaM2:F4} m^2) must shrink to <= 0.25 m^2 instead of 1.00 m^2 block!");
            Assert.That(maxHeightAboveBed, Is.LessThanOrEqualTo(0.015f + 1e-3f),
                $"Mass=2 micro-puddle height above bed ({maxHeightAboveBed:F4}m) must stay <= 0.015m!");
        }

        // =====================================================================
        // T08 (AK-7): Yamac CFL Gecisinde (y-1: Mass=3, y: Mass=2) 1.034m Sahte Derinlik Tamiri
        // =====================================================================
        [Test]
        public void T08_SlopeStepCflRainTransition_NeverProducesFalseOneMeterColumnDepth()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    solids[(4 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, 118);
                }
            }

            // Yamac basamaginda ayni kolonda hem y=5 (Mass=3) hem y=6 (Mass=2) anlik CFL gecis suyu
            fluids[(5 << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 3, isSettled: false).RawValue;
            fluids[(6 << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 2, isSettled: false).RawValue;

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(64);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            float maxDepthAttr = 0f;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                maxDepthAttr = MathF.Max(maxDepthAttr, tri.C0.DepthMeters);
                maxDepthAttr = MathF.Max(maxDepthAttr, tri.C1.DepthMeters);
                maxDepthAttr = MathF.Max(maxDepthAttr, tri.C2.DepthMeters);
            }

            Assert.That(maxDepthAttr, Is.LessThanOrEqualTo(0.03f),
                $"Thin rain CFL step (Mass=3 + Mass=2) must have true DepthMeters <= 0.03m, NEVER false 1.034m ocean depth (actual {maxDepthAttr:F4}m)!");
        }

        // =====================================================================
        // T09 (AK-8): Sifir Sualti Beyaz Duvar / Hava Sutunu (Basamakli Deniz Tabaninda 0 Ic Duvar!)
        // =====================================================================
        [Test]
        public void T09_SubmergedSteppedSeafloor_EmitsZeroUnderwaterVerticalWalls()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            // 6m derinliginde (ust yuzey Y = 10.0m) deniz icinde x = 3..12 boyunca 5 adet ShapeId==0 kum/kaya basamagi!
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int stepFloorY = Math.Clamp(3 + (x / 3), 3, 8); // y = 3, 4, 5, 6, 7, 8 basamaklari!
                    for (int y = 0; y < stepFloorY; y++)
                    {
                        solids[(y << 8) | (z << 4) | x] = stone;
                    }
                    solids[(stepFloorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Sand, 102);

                    for (int y = stepFloorY + 1; y <= 9; y++)
                    {
                        fluids[(y << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            int underwaterVerticalTriangles = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                // Chunk dis sinirini haric tut, icerideki (1..15) tum basamak gecislerini denetle
                float cx = (tri.C0.PositionCurr.X + tri.C1.PositionCurr.X + tri.C2.PositionCurr.X) / 3.0f;
                float cz = (tri.C0.PositionCurr.Z + tri.C1.PositionCurr.Z + tri.C2.PositionCurr.Z) / 3.0f;
                if (cx < 0.5f || cx > 15.5f || cz < 0.5f || cz > 15.5f) continue;

                float maxTriY = MathF.Max(tri.C0.PositionCurr.Y, MathF.Max(tri.C1.PositionCurr.Y, tri.C2.PositionCurr.Y));
                if (maxTriY < 9.95f && MathF.Abs(tri.C0.Normal.Y) < 0.5f)
                {
                    underwaterVerticalTriangles++;
                }
            }

            Assert.That(underwaterVerticalTriangles, Is.EqualTo(0),
                "Submerged stepped seafloor must emit ZERO underwater vertical wall/curtain triangles!");
        }

        // =====================================================================
        // T10 (AK-9): Egimli Yamacta +X Akan Suyun -Z/+Z Kuru Yanlarinda Sifir Sahte Selale / Yan Duvar
        // =====================================================================
        [Test]
        public void T10_SlopedStreamWithLowerDrySideSlopes_EmitsZeroSideWaterfallsOrBoxWalls()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            // +X yonunde alcalan bir sirt uzerinde z = 7..8 koridorunda akan dere;
            // z <= 6 ve z >= 9 yan yamaclari KURU ve 0.8m daha ALCAK (eski kodda sahte beyaz selale perdesi firlatiyordu!)
            for (int z = 0; z < 16; z++)
            {
                float sideDrop = (z >= 7 && z <= 8) ? 0.0f : 0.85f;
                for (int x = 0; x < 16; x++)
                {
                    float h = 8.40f - x * 0.22f - sideDrop;
                    h = Math.Clamp(h, 3.5f, 9.5f);
                    int floorY = (int)MathF.Floor(h);
                    float frac = h - floorY;

                    for (int y = 0; y < floorY; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    byte dens = (byte)Math.Clamp(66 + (int)MathF.Round(frac * 58.0f), 66, 124);
                    solids[(floorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, dens);

                    if (z >= 7 && z <= 8 && x >= 3 && x <= 12)
                    {
                        fluids[((floorY + 1) << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 22, isSettled: false).RawValue;
                    }
                }
            }

            var kinPool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 2, sliceVolume: vol);
            ActiveChunkFluidKinematicsSlice slice = kinPool.RentOrGetAndSyncBaseline(0, 0, fluids, vol, 1)!;
            for (int i = 0; i < vol; i++)
            {
                if (fluids[i] != 0)
                {
                    slice.VelX_Q7[i] = 48; // +X yonunde hizli akis; VelZ == 0!
                    slice.VelZ_Q7[i] = 0;
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var kinMap = new Dictionary<(int cx, int cz), IFluidKinematicsSlice> { [(0, 0)] = slice };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                0, 0, maxY, solidMap, fluidMap, tris, minY: 0, kinematicsChunks: kinMap);
            kinPool.ReleaseChunk(0, 0);

            int sideWaterfallOrVerticalWallCount = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                float cx = (tri.C0.PositionCurr.X + tri.C1.PositionCurr.X + tri.C2.PositionCurr.X) / 3.0f;
                if (cx < 3.5f || cx > 11.5f) continue;

                // -Z / +Z yan kenarlarinda sahte selale kopugu veya dikey yan duvar uretilmemeli!
                bool isSideFacing = MathF.Abs(tri.C0.Normal.Z) > 0.55f && MathF.Abs(tri.C0.Normal.Y) < 0.45f;
                bool hasSideFoam = tri.C0.WaterfallFoam01 >= 0.30f || tri.C1.WaterfallFoam01 >= 0.30f || tri.C2.WaterfallFoam01 >= 0.30f;
                if (isSideFacing || hasSideFoam)
                {
                    sideWaterfallOrVerticalWallCount++;
                }
            }

            Assert.That(sideWaterfallOrVerticalWallCount, Is.EqualTo(0),
                "A +X sloped stream must emit ZERO perpendicular (-Z/+Z) false waterfalls or vertical side walls!");
        }

        // =====================================================================
        // T11 (AK-10): Gercek 3m Ucurum Agzinda Rouse Savak Bukulmesi & Kesintisiz Selale Perdesi (< 1e-4m)
        // =====================================================================
        [Test]
        public void T11_TrueCliffOverfall_EmitsWeirDrawdownAndWatertight3MeterWaterfallCurtain()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint archBlock = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    solids[(4 << 8) | (z << 4) | x] = archBlock;
                    if (x <= 8)
                    {
                        for (int y = 5; y <= 7; y++) solids[(y << 8) | (z << 4) | x] = archBlock;
                    }
                }
            }

            // Yuksek nehir y=8 (x=4..8, z=7..8) -> x=9'da 3m dikey ucurumdan y=5 havuzuna dokulur!
            for (int z = 7; z <= 8; z++)
            {
                for (int x = 4; x <= 8; x++)
                {
                    fluids[(8 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 160, isSettled: false).RawValue;
                }
                for (int x = 9; x <= 12; x++)
                {
                    fluids[(5 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 160, isSettled: false).RawValue;
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            int waterfallTris = 0;
            float maxCurtainTopY = float.MinValue;
            float minCurtainBotY = float.MaxValue;
            float upstreamDepthAt6 = 0f;
            float brinkDepthAt9 = 0f;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                bool isCurtain = tri.C0.WaterfallFoam01 >= 0.30f || tri.C1.WaterfallFoam01 >= 0.30f || tri.C2.WaterfallFoam01 >= 0.30f;
                if (isCurtain)
                {
                    waterfallTris++;
                    maxCurtainTopY = MathF.Max(maxCurtainTopY, MathF.Max(tri.C0.PositionCurr.Y, MathF.Max(tri.C1.PositionCurr.Y, tri.C2.PositionCurr.Y)));
                    minCurtainBotY = MathF.Min(minCurtainBotY, MathF.Min(tri.C0.PositionCurr.Y, MathF.Min(tri.C1.PositionCurr.Y, tri.C2.PositionCurr.Y)));
                }
                else if (tri.C0.Normal.Y > 0.7f)
                {
                    SampleBrinkProfile(in tri.C0, ref upstreamDepthAt6, ref brinkDepthAt9);
                    SampleBrinkProfile(in tri.C1, ref upstreamDepthAt6, ref brinkDepthAt9);
                    SampleBrinkProfile(in tri.C2, ref upstreamDepthAt6, ref brinkDepthAt9);
                }
            }

            Assert.That(waterfallTris, Is.GreaterThanOrEqualTo(6), "3m cliff overfall must generate parabolic waterfall curtain triangles!");
            Assert.That(upstreamDepthAt6, Is.GreaterThan(0.4f));
            float weirRatio = brinkDepthAt9 / upstreamDepthAt6;
            Assert.That(weirRatio, Is.InRange(0.64f, 0.80f), $"Rouse weir brink drawdown ratio ({weirRatio:F3}) must lie in [0.64..0.80]!");
            Assert.That(MathF.Abs(maxCurtainTopY - (8.0f + brinkDepthAt9)), Is.LessThan(1e-4f), "Curtain top must weld seamlessly (< 1e-4m) to brink!");
            Assert.That(minCurtainBotY, Is.LessThanOrEqualTo(5.65f), "Curtain bottom must reach the lower plunge pool!");

            static void SampleBrinkProfile(in FluidCornerSample c, ref float h6, ref float h9)
            {
                if (c.PositionCurr.Y < 8.0f || MathF.Abs(c.PositionCurr.Z - 8.0f) > 0.05f) return;
                if (MathF.Abs(c.PositionCurr.X - 6.0f) < 0.05f) h6 = c.PositionCurr.Y - 8.0f;
                if (MathF.Abs(c.PositionCurr.X - 9.0f) < 0.05f) h9 = c.PositionCurr.Y - 8.0f;
            }
        }

        // =====================================================================
        // T12 (AK-3 / ILK-3): ShapeId != 0 Mimari Tas Kanalda Tam Koseli Oturan Su Yuzeyi
        // =====================================================================
        [Test]
        public void T12_ArchitecturalStoneChannel_PreservesCrispWallAlignmentWithoutOrganicRounding()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint brick = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            for (int z = 5; z <= 9; z++)
            {
                for (int x = 3; x <= 12; x++)
                {
                    solids[(4 << 8) | (z << 4) | x] = brick;
                    if (z == 6 || z == 8)
                    {
                        solids[(5 << 8) | (z << 4) | x] = brick; // z=6 ve z=8 mimari su kemeri yan duvarlari!
                    }
                }
            }

            for (int x = 4; x <= 11; x++)
            {
                fluids[(5 << 8) | (7 << 4) | x] = FluidCell16.Pack(FluidType.Water, 140, isSettled: true).RawValue;
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            float minChannelZ = float.MaxValue;
            float maxChannelZ = float.MinValue;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                if (tri.C0.Normal.Y < 0.8f) continue;

                CheckChannelZ(in tri.C0, ref minChannelZ, ref maxChannelZ);
                CheckChannelZ(in tri.C1, ref minChannelZ, ref maxChannelZ);
                CheckChannelZ(in tri.C2, ref minChannelZ, ref maxChannelZ);
            }

            Assert.That(MathF.Abs(minChannelZ - 7.0f), Is.LessThanOrEqualTo(1e-4f), "Water in architectural channel must meet z=7.0 wall crisply!");
            Assert.That(MathF.Abs(maxChannelZ - 8.0f), Is.LessThanOrEqualTo(1e-4f), "Water in architectural channel must meet z=8.0 wall crisply!");

            static void CheckChannelZ(in FluidCornerSample c, ref float minZ, ref float maxZ)
            {
                if (c.PositionCurr.X < 5.0f || c.PositionCurr.X > 10.0f) return;
                if (c.PositionCurr.Z < minZ) minZ = c.PositionCurr.Z;
                if (c.PositionCurr.Z > maxZ) maxZ = c.PositionCurr.Z;
            }
        }

        // =====================================================================
        // T13 (AK-11): Savak / Selale Ust Kenari ile Ust Yuzey GPU Deplasman Kilidi (0.000m Yirtilma)
        // =====================================================================
        [Test]
        public void T13_WaterfallTopSeam_SharesIdenticalSubTickDyAndUpwardDisplacementMaskWithRiverTop()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint archBlock = PackArchitecturalShape(MaterialIdCatalog.StoneBrick, 1);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    solids[(4 << 8) | (z << 4) | x] = archBlock;
                    if (x <= 8)
                    {
                        for (int y = 5; y <= 7; y++) solids[(y << 8) | (z << 4) | x] = archBlock;
                    }
                }
            }

            for (int z = 6; z <= 8; z++)
            {
                for (int x = 5; x <= 8; x++)
                {
                    fluids[(8 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 180, isSettled: false).RawValue;
                }
                fluids[(5 << 8) | (z << 4) | 9] = FluidCell16.Pack(FluidType.Water, 120, isSettled: false).RawValue;
            }

            // Alt-tik degisimi uretmek icin PrevMass = 110 (< 180) ata -> EncodedSubTickDy != 128!
            var kinPool = new ActiveChunkFluidKinematicsPool(prewarmSlices: 2, sliceVolume: vol);
            ActiveChunkFluidKinematicsSlice slice = kinPool.RentOrGetAndSyncBaseline(0, 0, fluids, vol, 1)!;
            for (int z = 6; z <= 8; z++)
            {
                for (int x = 5; x <= 8; x++)
                {
                    int idx = (8 << 8) | (z << 4) | x;
                    slice.PrevMass[idx] = 110;
                    slice.VelX_Q7[idx] = 40;
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var kinMap = new Dictionary<(int cx, int cz), IFluidKinematicsSlice> { [(0, 0)] = slice };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(
                0, 0, maxY, solidMap, fluidMap, tris, minY: 0, kinematicsChunks: kinMap);
            kinPool.ReleaseChunk(0, 0);

            FluidCornerSample? topBrinkSample = null;
            FluidCornerSample? curtainTopSample = null;

            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle tri = tris[i];
                bool isCurtain = tri.C0.WaterfallFoam01 >= 0.30f || tri.C1.WaterfallFoam01 >= 0.30f || tri.C2.WaterfallFoam01 >= 0.30f;
                InspectSeamVertex(in tri.C0, isCurtain, ref topBrinkSample, ref curtainTopSample);
                InspectSeamVertex(in tri.C1, isCurtain, ref topBrinkSample, ref curtainTopSample);
                InspectSeamVertex(in tri.C2, isCurtain, ref topBrinkSample, ref curtainTopSample);
            }

            Assert.That(topBrinkSample.HasValue, Is.True, "Must locate top river brink vertex at (9.0, ~8.x, 7.0)!");
            Assert.That(curtainTopSample.HasValue, Is.True, "Must locate top waterfall curtain vertex at (9.0, ~8.x, 7.0)!");

            FluidCornerSample topV = topBrinkSample!.Value;
            FluidCornerSample curV = curtainTopSample!.Value;

            Assert.That(Vector3.Distance(topV.PositionCurr, curV.PositionCurr), Is.LessThanOrEqualTo(1e-4f));
            Assert.That(curV.EncodedSubTickDy, Is.EqualTo(topV.EncodedSubTickDy),
                "Curtain top edge must carry identical EncodedSubTickDy so sub-tick interpolation never tears!");
            // Shader upwardSurfaceMask = saturate(normalWS.y * 1.25) == 1.0 icin Normal.Y >= 0.80f olmali!
            Assert.That(Math.Clamp(curV.Normal.Y * 1.25f, 0f, 1f), Is.EqualTo(1.0f).Within(1e-3f),
                "Curtain top vertex normal.y must saturate upwardSurfaceMask to 1.0 in StratocraftFluidSurface.shader!");

            static void InspectSeamVertex(
                in FluidCornerSample c,
                bool isCurtain,
                ref FluidCornerSample? topBrink,
                ref FluidCornerSample? curtainTop)
            {
                if (MathF.Abs(c.PositionCurr.X - 9.0f) > 0.02f || MathF.Abs(c.PositionCurr.Z - 7.0f) > 0.02f || c.PositionCurr.Y < 8.0f)
                {
                    return;
                }

                if (isCurtain) curtainTop = c;
                else if (c.Normal.Y > 0.7f) topBrink = c;
            }
        }

        // =====================================================================
        // T14 (AK-12): 500 Tik Karma Vadi dM == 0 + Kapali U-Borusu + 128 Esikli Obsidyen
        // =====================================================================
        [Test]
        public void T14_CoreMechanics_Preserve500TickValleyMass_UTubeEquilibrium_And128StoichiometricObsidian()
        {
            // 1) 500 tik engebeli kapali vadi kutle korunumu (dM == 0)
            const int sx = 16, sy = 16, sz = 16;
            int vol = sx * sy * sz;
            var valleyVoxels = new uint[vol];
            var valleyFluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    valleyVoxels[(0 * sz + z) * sx + x] = stone;
                    if (x == 0 || x == sx - 1 || z == 0 || z == sz - 1)
                    {
                        for (int y = 1; y < sy - 1; y++) valleyVoxels[(y * sz + z) * sx + x] = stone;
                    }
                    else
                    {
                        int h = 2 + ((x * 3 + z * 5) % 4);
                        for (int y = 1; y < h; y++) valleyVoxels[(y * sz + z) * sx + x] = stone;
                        byte sdf = (byte)(72 + ((x * 11 + z * 7) % 48));
                        valleyVoxels[(h * sz + z) * sx + x] = PackSolid(MaterialIdCatalog.Stone, sdf);
                    }
                }
            }

            long initialMass = 0;
            for (int z = 3; z <= 7; z++)
            {
                for (int x = 3; x <= 7; x++)
                {
                    int idx = (9 * sz + z) * sx + x;
                    byte m = (byte)(90 + (x + z) * 10);
                    valleyFluids[idx] = FluidCell16.Pack(FluidType.Water, m, isSettled: false).RawValue;
                    initialMass += m;
                }
            }

            for (int t = 0; t < 500; t++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    valleyVoxels, valleyFluids, sx, sy, sz, out _, out _, stepParity: t & 1);
            }

            long finalMass = 0;
            for (int i = 0; i < vol; i++) finalMass += new FluidCell16(valleyFluids[i]).Mass;
            Assert.That(finalMass, Is.EqualTo(initialMass), "500-tick sloped valley simulation must preserve exact mass (dM == 0)!");

            // 2) Kapali U-Borusu (Bilesik Kaplar) basinc dengelemesi: sol koldaki yuksek su alt gecitten sag kola yukselmeli!
            var uVoxels = new uint[vol];
            var uFluids = new ushort[vol];
            for (int i = 0; i < vol; i++) uVoxels[i] = stone;

            // Sol dikey kol (x=4, z=8, y=2..10), alt yatay gecit (x=4..8, z=8, y=2), sag dikey kol (x=8, z=8, y=2..10)
            for (int y = 2; y <= 10; y++)
            {
                uVoxels[(y * sz + 8) * sx + 4] = 0u;
                uVoxels[(y * sz + 8) * sx + 8] = 0u;
            }
            for (int x = 4; x <= 8; x++) uVoxels[(2 * sz + 8) * sx + x] = 0u;

            // Sol kolu y=2..9 (8 * 255 kutle: 5 hucre y=2 tabanini doldurur, kalan 3*255 iki kolda yukari yukselir!) doldur
            for (int y = 2; y <= 9; y++)
            {
                uFluids[(y * sz + 8) * sx + 4] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
            }

            var uSim = new FluidSimulationSystem();
            for (int t = 0; t < 60; t++)
            {
                uSim.StepChunkFluid(uVoxels, uFluids, sx, sy, sz, out _, out _);
            }

            Assert.That(new FluidCell16(uFluids[(3 * sz + 8) * sx + 8]).Mass, Is.GreaterThan((byte)180),
                "Closed U-tube hydrostatics must push water up the right arm above y=2 bend!");

            // 3) 128 Stokiyometrik Su + Lav Kimyasi (>= 128 -> NaturalObsidian, < 128 -> 0 blok / buharlasma!)
            var rxVoxels = new uint[vol];
            var rxFluids = new ushort[vol];
            for (int x = 0; x < sx; x++)
            {
                for (int z = 0; z < sz; z++) rxVoxels[(1 * sz + z) * sx + x] = stone;
            }

            // Yuksek kutle (>= 128): (4, 3, 4) Lav=200 ustune (4, 4, 4) Su=200
            rxFluids[(3 * sz + 4) * sx + 4] = FluidCell16.Pack(FluidType.Lava, 200, isSettled: false).RawValue;
            rxFluids[(4 * sz + 4) * sx + 4] = FluidCell16.Pack(FluidType.Water, 200, isSettled: false).RawValue;

            // Dusuk kutle (< 128): (10, 3, 10) Lav=40 ustune (10, 4, 10) Su=40
            rxFluids[(3 * sz + 10) * sx + 10] = FluidCell16.Pack(FluidType.Lava, 40, isSettled: false).RawValue;
            rxFluids[(4 * sz + 10) * sx + 10] = FluidCell16.Pack(FluidType.Water, 40, isSettled: false).RawValue;

            for (int t = 0; t < 8; t++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    rxVoxels, rxFluids, sx, sy, sz, out _, out _, stepParity: t & 1);
            }

            bool highFormedObsidian =
                VoxelBitUtility.GetMaterialId(rxVoxels[(3 * sz + 4) * sx + 4]) == MaterialIdCatalog.NaturalObsidian ||
                VoxelBitUtility.GetMaterialId(rxVoxels[(4 * sz + 4) * sx + 4]) == MaterialIdCatalog.NaturalObsidian;
            bool lowFormedObsidian =
                VoxelBitUtility.GetMaterialId(rxVoxels[(3 * sz + 10) * sx + 10]) == MaterialIdCatalog.NaturalObsidian ||
                VoxelBitUtility.GetMaterialId(rxVoxels[(4 * sz + 10) * sx + 10]) == MaterialIdCatalog.NaturalObsidian;

            Assert.That(highFormedObsidian, Is.True, "Water + Lava >= 128 must synthesize NaturalObsidian!");
            Assert.That(lowFormedObsidian, Is.False, "Water + Lava < 128 must dissipate as steam/slag without spawning obsidian!");
        }

        // =====================================================================
        // T15 (AK-12): Dalton Buharlasma, Acik Kap Yagmur Dolumu ve "Tastan Su Gecmez" Korunumu
        // =====================================================================
        [Test]
        public void T15_HydrologyInvariants_PreserveDaltonEvaporation_OpenBasinRainFill_AndImpermeableStone()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            // 1) Gecirimsiz Stone taban ("Tastan Su Gecmez"): SoilMoistureAndMudSystem kaya icine 1 damla bile su sizdirmamali!
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 4; y++) voxels[(y << 8) | (z << 4) | x] = stone;
                }
            }

            // Sol cukur (x=3..5, z=3..5): gokyuzune acik tas kap; Sag cukur (x=10..12, z=10..12): ustu tas tavanla kapali kap!
            for (int z = 2; z <= 6; z++)
            {
                for (int x = 2; x <= 6; x++)
                {
                    if (x == 2 || x == 6 || z == 2 || z == 6) voxels[(5 << 8) | (z << 4) | x] = stone;
                }
            }
            for (int z = 9; z <= 13; z++)
            {
                for (int x = 9; x <= 13; x++)
                {
                    if (x == 9 || x == 13 || z == 9 || z == 13) voxels[(5 << 8) | (z << 4) | x] = stone;
                    voxels[(9 << 8) | (z << 4) | x] = stone; // Tavan!
                }
            }

            var soilSys = new SoilMoistureAndMudSystem();
            var waterCycle = new WaterCycleAndEvaporationSystem();
            var skyMap = new ChunkSkyOcclusionMap256();
            skyMap.RebuildAllColumns(voxels);

            waterCycle.StepRainSurfaceWettingAndPonding(
                0, 0, voxels, fluids, soilSys, maxY,
                precipMmPerHour: 28.0f, airTempC: 18.0f, gameDeltaSeconds: 600.0f,
                out _, out _, skyMap: skyMap);

            int openBasinWater = new FluidCell16(fluids[(5 << 8) | (4 << 4) | 4]).Mass;
            int roofedBasinWater = new FluidCell16(fluids[(5 << 8) | (11 << 4) | 11]).Mass;

            Assert.That(openBasinWater, Is.GreaterThan(0), "Open stone basin under open sky must collect rainwater!");
            Assert.That(roofedBasinWater, Is.EqualTo(0), "Roofed stone basin must stay completely dry during rain!");

            // Tastan Su Gecmez teyidi: 20 adim toprak sizma kosumunda tas cukurdaki su kutlesi 0 kayip vermeli!
            int beforeInfil = openBasinWater;
            for (int s = 0; s < 20; s++)
            {
                soilSys.StepChunkSoilInfiltration(0, 0, voxels, fluids, maxY, out _, out _);
            }
            Assert.That(new FluidCell16(fluids[(5 << 8) | (4 << 4) | 4]).Mass, Is.EqualTo(beforeInfil),
                "Impermeable Stone ('Tastan Su Gecmez') must never absorb water!");

            // Dalton buharlasma teyidi: gunesli kuru ruzgarli havada acik kaptaki su buharlasmali!
            waterCycle.StepSurfaceWaterEvaporation(
                0, 0, voxels, fluids, maxY,
                airTempC: 32.0f, relativeHumidity01: 0.20f, windSpeedMps: 6.0f, gameDeltaSeconds: 36000.0f,
                out float evapKg, out bool evapChanged, skyMap: skyMap);
            Assert.That(evapKg, Is.GreaterThan(0.0f));
            Assert.That(evapChanged, Is.True);
        }

        // =====================================================================
        // T16 (AK-13): StepChunkSoilInfiltration -> StepChunkFluid Sirasiyla Uyku Kilidi & Hash Korumasi
        // =====================================================================
        [Test]
        public void T16_SettledPool_SleepsCleanlyAfterInfiltrationFirstOrder_AndKeepsStableSurfaceHash()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 4; y++) voxels[(y << 8) | (z << 4) | x] = stone;
                    if (x < 3 || x > 12 || z < 3 || z > 12) voxels[(5 << 8) | (z << 4) | x] = stone;
                    else fluids[(5 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 180, isSettled: false).RawValue;
                }
            }

            var soilSys = new SoilMoistureAndMudSystem();
            var fluidSys = new FluidSimulationSystem();

            long remainingMass = 0;
            for (int tick = 0; tick < 24; tick++)
            {
                // Stage1InteractivePlayController Paket 4B.2 dogru sirasi: ONCE Infiltration, SONRA StepChunkFluid!
                soilSys.StepChunkSoilInfiltration(0, 0, voxels, fluids, maxY, out _, out _);
                remainingMass = fluidSys.StepChunkFluid(0, 0, tick, voxels, fluids, 16, maxY, 16, null, out _, out _);
            }

            Assert.That(remainingMass, Is.EqualTo(100L * 180L), "Flat impermeable pool must conserve 100% of its 18,000 mass!");

            ChunkFluidActivityMask mask1 = ChunkFluidActivityMask.BuildFromChunkFluids(fluids, maxY);
            Assert.That(mask1.HasAnyActiveLayer(), Is.False, "Settled pool must sleep cleanly with ActiveY0..2 == 0!");

            uint hash2 = mask1.ComputeFastSurfaceVisualHash(fluids, maxY);
            uint hash3 = mask1.ComputeFastSurfaceVisualHash(fluids, maxY);
            Assert.That(hash3, Is.EqualTo(hash2),
                "Unchanged settled fluid chunk must keep an identical surface hash (0 redundant dirty remeshes)!");
        }

        // =====================================================================
        // T17 (AK-5 / IP-5): Gecirimsiz Tas Sirt Uzerinde Iki Kademeli Yagmur -> Vadi Olugu (Talweg) Akisi
        // =====================================================================
        [Test]
        public void T17_ImpermeableStoneRidgeRain_RoutesThinRunoffIntoValleyTalwegBeforeRidgeFlooding()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            // V-vadisi gecirimsiz tas kesiti: x=1 ve x=14 dik sirt tepesi, x=8 vadi olugu (talweg)!
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float distFromTalweg = MathF.Abs(x - 8);
                    float h = 5.15f + distFromTalweg * 0.35f;
                    int floorY = (int)MathF.Floor(h);
                    float frac = h - floorY;

                    for (int y = 0; y < floorY; y++) voxels[(y << 8) | (z << 4) | x] = stone;
                    byte dens = (byte)Math.Clamp(66 + (int)MathF.Round(frac * 58.0f), 66, 124);
                    voxels[(floorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, dens);
                }
            }

            var soilSys = new SoilMoistureAndMudSystem();
            var waterCycle = new WaterCycleAndEvaporationSystem();

            // Orta siddette kisa yagmur adimlari (~0.35 sivi birimi/kolon/adim):
            // Sirt tepeleri (x=1, x=14) carry birikimini %82 oraninda yamac asagi iterken vadi olugu (x=8) once su dogurur!
            for (int step = 0; step < 5; step++)
            {
                waterCycle.StepRainSurfaceWettingAndPonding(
                    0, 0, voxels, fluids, soilSys, maxY,
                    precipMmPerHour: 12.0f, airTempC: 16.0f, gameDeltaSeconds: 420.0f,
                    out _, out _);
            }

            int ridgeCrestWater = 0;
            int valleyTalwegWater = 0;
            for (int z = 2; z <= 13; z++)
            {
                for (int y = 5; y < maxY; y++)
                {
                    ridgeCrestWater += new FluidCell16(fluids[(y << 8) | (z << 4) | 1]).Mass;
                    ridgeCrestWater += new FluidCell16(fluids[(y << 8) | (z << 4) | 14]).Mass;
                    valleyTalwegWater += new FluidCell16(fluids[(y << 8) | (z << 4) | 7]).Mass;
                    valleyTalwegWater += new FluidCell16(fluids[(y << 8) | (z << 4) | 8]).Mass;
                    valleyTalwegWater += new FluidCell16(fluids[(y << 8) | (z << 4) | 9]).Mass;
                }
            }

            Assert.That(valleyTalwegWater, Is.GreaterThan(ridgeCrestWater),
                $"Sub-unit talweg rain routing must concentrate initial runoff in valley trough ({valleyTalwegWater}) instead of flooding ridge crests ({ridgeCrestWater})!");
        }

        // =====================================================================
        // T18 (AK-14): 192-Bit ChunkFluidActivityMask Katman Daraltimi & HasAnyLava == 0 O(1) Atlamasi
        // =====================================================================
        [Test]
        public void T18_ChunkFluidActivityMask_IsolatesSingleRainLayerAtY95_AndSkipsLavaCrustingInO1()
        {
            const int maxY = 160;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];

            // 40.960 hucrelik dev chunk icinde YALNIZCA y = 95 katmaninda 4 adet yagmur hucresi var!
            fluids[(95 << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 12, isSettled: false).RawValue;
            fluids[(95 << 8) | (8 << 4) | 9] = FluidCell16.Pack(FluidType.Water, 10, isSettled: false).RawValue;
            fluids[(95 << 8) | (9 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 8, isSettled: true).RawValue;
            fluids[(95 << 8) | (9 << 4) | 9] = FluidCell16.Pack(FluidType.Water, 6, isSettled: true).RawValue;

            ChunkFluidActivityMask mask = ChunkFluidActivityMask.BuildFromChunkFluids(fluids, maxY);

            Assert.That(mask.TotalWetCellCount, Is.EqualTo((ushort)4));
            Assert.That(mask.MinWetY, Is.EqualTo((byte)95));
            Assert.That(mask.MaxWetY, Is.EqualTo((byte)95));
            Assert.That(mask.HasAnyLava, Is.EqualTo((byte)0));
            Assert.That(mask.WetY0, Is.EqualTo(0UL));
            Assert.That(mask.WetY2, Is.EqualTo(0UL));
            Assert.That(mask.WetY1, Is.EqualTo(1UL << (95 - 64)), "Only bit 95 must be set in 192-bit WetY mask!");

            int visitedLayers = 0;
            int visitedY = -1;
            for (int y = 0; y < maxY; y++)
            {
                if (mask.IsWetLayer(y))
                {
                    visitedLayers++;
                    visitedY = y;
                }
            }
            Assert.That(visitedLayers, Is.EqualTo(1));
            Assert.That(visitedY, Is.EqualTo(95));

            // HasAnyLava == 0 iken StepChunkLavaCrusting sicaklik callback'ini 0 kez cagirarak O(1) donmeli!
            var lavaSys = new LavaSolidificationAndCoolingSystem();
            int callbackCount = 0;
            int solidified = lavaSys.StepChunkLavaCrusting(
                0, 0, voxels, fluids, maxY, 0.25f,
                (wx, wy, wz) =>
                {
                    callbackCount++;
                    return 20.0f;
                },
                out ushort dirtyMask,
                skipIfKnownLavaFree: mask.HasAnyLava == 0);

            Assert.That(solidified, Is.EqualTo(0));
            Assert.That(dirtyMask, Is.EqualTo((ushort)0));
            Assert.That(callbackCount, Is.EqualTo(0), "Lava-free chunk must exit StepChunkLavaCrusting in O(1) with 0 cell reads!");
        }

        // =====================================================================
        // T19 (AK-15): 16x16 Durgun Deniz Chunk'inda 4x4m 2B Planar Greedy Quad Birlestirme (<= 64 Ucgen!)
        // =====================================================================
        [Test]
        public void T19_PlanarGreedyPoolMeshing_Compresses16x16SeaChunkToAtMost64Triangles_AndWeldsChunkSeam()
        {
            const int maxY = 12;
            const int vol = 16 * maxY * 16;
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            var solidsA = new uint[vol];
            var solidsB = new uint[vol];
            var fluidsA = new ushort[vol];
            var fluidsB = new ushort[vol];

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++)
                    {
                        solidsA[(y << 8) | (z << 4) | x] = stone;
                        solidsB[(y << 8) | (z << 4) | x] = stone;
                    }
                    for (int y = 4; y <= 7; y++)
                    {
                        fluidsA[(y << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                        fluidsB[(y << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                    }
                }
            }

            // Komsu chunk'larla cevrelenmis acik deniz karosu (0,0) ve (1,0)
            var solidMap = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = solidsA, [(1, 0)] = solidsB, [(-1, 0)] = solidsA,
                [(0, 1)] = solidsA, [(0, -1)] = solidsA, [(1, 1)] = solidsB,
                [(1, -1)] = solidsB, [(-1, 1)] = solidsA, [(-1, -1)] = solidsA
            };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]>
            {
                [(0, 0)] = fluidsA, [(1, 0)] = fluidsB, [(-1, 0)] = fluidsA,
                [(0, 1)] = fluidsA, [(0, -1)] = fluidsA, [(1, 1)] = fluidsB,
                [(1, -1)] = fluidsB, [(-1, 1)] = fluidsA, [(-1, -1)] = fluidsA
            };

            var trisA = new List<FluidRichTriangle>(64);
            var trisB = new List<FluidRichTriangle>(64);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, trisA);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(1, 0, maxY, solidMap, fluidMap, trisB);

            // 16x16 = 256m^2 durgun deniz 4x4m kuadlarla 4*4 = 16 kuad == TAM 32 ucgen (<= 64 kabul siniri!) uretmeli!
            Assert.That(trisA.Count, Is.InRange(2, 64),
                $"16x16 settled sea chunk must compress from ~2,048 triangles down to <= 64 via 4x4m Planar Greedy Meshing (actual: {trisA.Count})!");
            Assert.That(trisB.Count, Is.InRange(2, 64));

            // (0,0) chunk x==16.0f siniri ile (1,0) chunk x==0.0f siniri 0.000m dikissiz Y == 8.000f kotunda birlesmeli!
            float maxSeamDiff = 0f;
            int seamMatches = 0;
            for (int i = 0; i < trisA.Count; i++)
            {
                FluidRichTriangle ta = trisA[i];
                CheckBoundarySeamY(in ta.C0, 16.0f, ref maxSeamDiff, ref seamMatches);
                CheckBoundarySeamY(in ta.C1, 16.0f, ref maxSeamDiff, ref seamMatches);
                CheckBoundarySeamY(in ta.C2, 16.0f, ref maxSeamDiff, ref seamMatches);
            }
            for (int i = 0; i < trisB.Count; i++)
            {
                FluidRichTriangle tb = trisB[i];
                CheckBoundarySeamY(in tb.C0, 0.0f, ref maxSeamDiff, ref seamMatches);
                CheckBoundarySeamY(in tb.C1, 0.0f, ref maxSeamDiff, ref seamMatches);
                CheckBoundarySeamY(in tb.C2, 0.0f, ref maxSeamDiff, ref seamMatches);
            }

            Assert.That(seamMatches, Is.GreaterThanOrEqualTo(10));
            Assert.That(maxSeamDiff, Is.LessThanOrEqualTo(1e-5f),
                "Greedy-merged sea chunks (0,0) and (1,0) must meet at exact Y=8.000m with 0.000m seam gap!");

            static void CheckBoundarySeamY(in FluidCornerSample s, float targetX, ref float maxDiff, ref int count)
            {
                if (MathF.Abs(s.PositionCurr.X - targetX) < 1e-3f)
                {
                    float diff = MathF.Abs(s.PositionCurr.Y - 8.0f);
                    if (diff > maxDiff) maxDiff = diff;
                    count++;
                }
            }
        }

        // =====================================================================
        // T20 (AK-16 / AK-17): 50 Aktif Islak Chunk Sifir-GC (0 Byte) & Hiz Butcesi Benchmark'i
        // =====================================================================
        [Test]
        public void T20_FiftyActiveRainChunksSimulationStep_AllocatesZeroBytesGc_AndCompletesWithinFrameBudget()
        {
            const int chunkCount = 50;
            const int maxY = 32;
            const int chunkVol = 16 * maxY * 16;
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            var chunkVoxels = new uint[chunkCount][];
            var chunkFluids = new ushort[chunkCount][];
            var fluidSys = new FluidSimulationSystem();

            for (int c = 0; c < chunkCount; c++)
            {
                var vox = new uint[chunkVol];
                var fl = new ushort[chunkVol];
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        for (int y = 0; y <= 14; y++) vox[(y << 8) | (z << 4) | x] = stone;
                        byte slope = (byte)(72 + ((x * 5 + z * 3 + c) & 31));
                        vox[(15 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, slope);

                        if (((x + z + c) & 3) == 0 && x >= 1 && x <= 14 && z >= 1 && z <= 14)
                        {
                            fl[(16 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 14, isSettled: false).RawValue;
                        }
                    }
                }
                chunkVoxels[c] = vox;
                chunkFluids[c] = fl;
                _ = fluidSys.GetOrBuildActivityMask(c, 0, fl, maxY);
            }

            // Tier-1 JIT isinma kosumu
            for (int warm = 0; warm < 6; warm++)
            {
                _ = Step50ChunksZeroGcHarness(fluidSys, chunkVoxels, chunkFluids, chunkCount, maxY, warm);
            }
            System.Threading.Thread.Sleep(15);
            _ = Step50ChunksZeroGcHarness(fluidSys, chunkVoxels, chunkFluids, chunkCount, maxY, 99);
            _ = GC.GetAllocatedBytesForCurrentThread();

            Stopwatch sw = Stopwatch.StartNew();
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            long totalActiveRemaining = Step50ChunksZeroGcHarness(fluidSys, chunkVoxels, chunkFluids, chunkCount, maxY, 100);
            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            sw.Stop();

            Assert.That(allocDelta, Is.EqualTo(0L),
                $"50-chunk active rain fluid simulation step must allocate 0 Bytes of managed GC (actual: {allocDelta} B)!");
            Assert.That(totalActiveRemaining, Is.GreaterThanOrEqualTo(0L));
            Assert.That(sw.Elapsed.TotalMilliseconds, Is.LessThan(45.0),
                $"Single-threaded 50-chunk active rain simulation took {sw.Elapsed.TotalMilliseconds:F2}ms (must stay well within windowed sub-volume budget)!");
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static long Step50ChunksZeroGcHarness(
            FluidSimulationSystem fluidSys,
            uint[][] chunkVoxels,
            ushort[][] chunkFluids,
            int chunkCount,
            int maxY,
            int tick)
        {
            long sumActive = 0;
            for (int c = 0; c < chunkCount; c++)
            {
                sumActive += fluidSys.StepChunkFluid(c, 0, tick, chunkVoxels[c], chunkFluids[c], 16, maxY, 16, null, out _, out _);
            }
            return sumActive;
        }

        // =====================================================================
        // T21: Yanlis/Egrili Dogan Havuzun Derinlesen Taban (solidFloorY == 88) Uzerinde Fizikle Kendiliginden Duzlesmesi
        // =====================================================================
        [Test]
        public void T21_UnevenPoolOverVariableSeafloor_SelfLevelsByPhysicsAndEliminatesTransitionTrench()
        {
            const int maxY = 14;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);

            // Kapali havza (x = 3..12, z = 3..12):
            // - x = 3..5: sig kum taban (floorY = 7, d = 104 -> ~7.68m)
            // - x = 6..8: 1-2m gecis bandi (floorY = 6, d = 72 -> ~6.13m, Gorsel 1 & 2'de coken bolge!)
            // - x = 9..12: derin deniz/havuz tabani (floorY = 4, d = 127)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = stone;

                    if (x < 3 || x > 12 || z < 3 || z > 12)
                    {
                        for (int y = 4; y <= 10; y++) solids[(y << 8) | (z << 4) | x] = stone;
                    }
                    else if (x <= 5)
                    {
                        for (int y = 4; y <= 6; y++) solids[(y << 8) | (z << 4) | x] = stone;
                        solids[(7 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Sand, 104);
                    }
                    else if (x <= 8)
                    {
                        for (int y = 4; y <= 5; y++) solids[(y << 8) | (z << 4) | x] = stone;
                        solids[(6 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Sand, 72);
                    }
                    else
                    {
                        solids[(4 << 8) | (z << 4) | x] = stone;
                    }
                }
            }

            // KASITLI OLARAK YANLIS / YAMUK SU OLUSTUR:
            // Derin taraf yuksek (y=7 Mass=245), gecis bandi (x=6..8) COKUK (y=7 Mass=130), sig taraf yuksek (y=8 Mass=80)!
            long initialMass = 0;
            for (int z = 3; z <= 12; z++)
            {
                for (int x = 3; x <= 12; x++)
                {
                    if (x <= 5)
                    {
                        fluids[(8 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 80, isSettled: false).RawValue;
                        initialMass += 80;
                    }
                    else if (x <= 8)
                    {
                        fluids[(7 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 130, isSettled: false).RawValue;
                        initialMass += 130;
                    }
                    else
                    {
                        fluids[(5 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                        fluids[(6 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false).RawValue;
                        fluids[(7 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 245, isSettled: false).RawValue;
                        initialMass += 255 + 255 + 245;
                    }
                }
            }

            for (int tick = 0; tick < 120; tick++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    solids, fluids, 16, maxY, 16, out _, out _, stepParity: tick & 1);
            }

            long finalMass = 0;
            for (int i = 0; i < vol; i++) finalMass += new FluidCell16(fluids[i]).Mass;
            Assert.That(finalMass, Is.EqualTo(initialMass), "Self-leveling variable-depth basin must preserve exact mass (dM == 0)!");

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            float minTopY = float.MaxValue;
            float maxTopY = float.MinValue;
            int topCount = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (t.C0.Normal.Y < 0.8f) continue;
                SamplePoolY(in t.C0, ref minTopY, ref maxTopY, ref topCount);
                SamplePoolY(in t.C1, ref minTopY, ref maxTopY, ref topCount);
                SamplePoolY(in t.C2, ref minTopY, ref maxTopY, ref topCount);
            }

            Assert.That(topCount, Is.GreaterThan(16));
            Assert.That(maxTopY - minTopY, Is.LessThanOrEqualTo(0.025f),
                $"Unevenly spawned water over variable seafloor must self-level into a single flat plane without any dip (spread={maxTopY - minTopY:F4}m)!");

            static void SamplePoolY(in FluidCornerSample s, ref float minY, ref float maxY, ref int count)
            {
                if (s.PositionCurr.X < 4.0f || s.PositionCurr.X > 12.0f || s.PositionCurr.Z < 4.0f || s.PositionCurr.Z > 12.0f) return;
                if (s.PositionCurr.Y < minY) minY = s.PositionCurr.Y;
                if (s.PositionCurr.Y > maxY) maxY = s.PositionCurr.Y;
                count++;
            }
        }

        // =====================================================================
        // T22: Yagmur Altinda Kazilmis Toprak/Tas Golun Dolup Savak Kenarindan Asagi Tasmasi
        // =====================================================================
        [Test]
        public void T22_RainOnDugBasinWithSoilWatershed_FillsPondAndOverflowsDownSpillway()
        {
            const int maxY = 14;
            const int vol = 16 * maxY * 16;
            var voxels = new uint[vol];
            var fluids = new ushort[vol];
            uint stone = PackSolid(MaterialIdCatalog.Stone);
            uint dirt = PackSolid(MaterialIdCatalog.Dirt);

            // Sol yamac (x=2..5): yuksek toprak yamac; Orta canak (x=6..9, z=5..10): kazilmis gol cukuru;
            // x=10, z=7..8: alcak tasma dudagi (spillway) -> x=11..14: asagi tahliye olugu!
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 4; y++) voxels[(y << 8) | (z << 4) | x] = stone;

                    bool inBasin = x >= 6 && x <= 9 && z >= 5 && z <= 10;
                    bool inSpillway = x >= 10 && x <= 14 && z >= 7 && z <= 8;

                    if (inBasin)
                    {
                        // Gol tabani y=4 tas, icinde baslangicta yarim gol suyu var (y=5 Mass=170)
                        fluids[(5 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 170, isSettled: true).RawValue;
                    }
                    else if (inSpillway)
                    {
                        int spillFloor = x == 10 ? 5 : 4;
                        if (spillFloor == 5)
                        {
                            voxels[(5 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, 78); // ~5.24m tasma dudagi
                        }
                    }
                    else
                    {
                        voxels[(5 << 8) | (z << 4) | x] = stone;
                        voxels[(6 << 8) | (z << 4) | x] = dirt;
                    }
                }
            }

            var soilSys = new SoilMoistureAndMudSystem();
            var waterCycle = new WaterCycleAndEvaporationSystem();

            for (int step = 0; step < 24; step++)
            {
                waterCycle.StepRainSurfaceWettingAndPonding(
                    0, 0, voxels, fluids, soilSys, maxY,
                    precipMmPerHour: 24.0f, airTempC: 19.0f, gameDeltaSeconds: 300.0f,
                    out _, out _);

                for (int fTick = 0; fTick < 4; fTick++)
                {
                    FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                        voxels, fluids, 16, maxY, 16, out _, out _, stepParity: fTick & 1);
                }
            }

            int downstreamOverflowMass = 0;
            for (int z = 7; z <= 8; z++)
            {
                for (int x = 11; x <= 14; x++)
                {
                    for (int y = 5; y <= 8; y++)
                    {
                        downstreamOverflowMass += new FluidCell16(fluids[(y << 8) | (z << 4) | x]).Mass;
                    }
                }
            }

            Assert.That(downstreamOverflowMass, Is.GreaterThan(20),
                "Sustained rain on a dug pond and surrounding soil watershed must fill the pond and overflow down the spillway!");
        }

        // =====================================================================
        // T23: Kazanla Dokulen Cok Katmanli Yamac Suyunun Duvara Gomulmesi & 0 Beyaz Kutu Duvari
        // =====================================================================
        [Test]
        public void T23_PouredCauldronStackedSlopeWater_EmbedsIntoSdfBank_AndEmitsZeroWhiteBoxWalls()
        {
            const int maxY = 14;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            // z = 5 ve z = 10 yuksek dogal SurfaceNets kaya duvarlari; ortada +X egimli yamac
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float h = 7.40f - x * 0.18f;
                    if (z <= 5 || z >= 10) h += 2.2f;
                    int floorY = Math.Clamp((int)MathF.Floor(h), 2, 11);
                    float frac = Math.Clamp(h - floorY, 0f, 0.99f);

                    for (int y = 0; y < floorY; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    byte dens = (byte)Math.Clamp(66 + (int)MathF.Round(frac * 58.0f), 66, 124);
                    solids[(floorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, dens);

                    // Kazan dokumu: x = 6..8, z = 6..9 bolgesinde su anlik 2 katman ust uste binmis!
                    if (x >= 6 && x <= 8 && z >= 6 && z <= 9)
                    {
                        fluids[((floorY + 1) << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 210, isSettled: false).RawValue;
                        fluids[((floorY + 2) << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 130, isSettled: false).RawValue;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(0));

            int whiteBoxWallTris = 0;
            float minBankZ = float.MaxValue;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                bool isVertical = MathF.Abs(t.C0.Normal.Y) < 0.25f && MathF.Abs(t.C1.Normal.Y) < 0.25f;
                bool hasBoxWallFoam = MathF.Abs(t.C0.WaterfallFoam01 - 0.75f) < 0.02f;
                if (isVertical && hasBoxWallFoam)
                {
                    whiteBoxWallTris++;
                }

                if (t.C0.Normal.Y > 0.5f && t.C0.PositionCurr.X >= 6.2f && t.C0.PositionCurr.X <= 8.8f)
                {
                    minBankZ = MathF.Min(minBankZ, MathF.Min(t.C0.PositionCurr.Z, MathF.Min(t.C1.PositionCurr.Z, t.C2.PositionCurr.Z)));
                }
            }

            Assert.That(whiteBoxWallTris, Is.EqualTo(0),
                "Stacked cauldron water on natural SurfaceNets terrain must emit ZERO 1m x 1m white box walls (Gorsel 8 fix)!");
            Assert.That(minBankZ, Is.LessThanOrEqualTo(5.55f),
                $"Poured non-settled water must still embed >= 0.45m into z=5 SurfaceNets rock wall so no dry gap appears (minBankZ={minBankZ:F3}, Gorsel 3 & 4 fix)!");
        }

        // =====================================================================
        // T24: 4x4 Greedy Deniz Blogu ile Sahil Hucreleri Arasinda Sifir T-Junction Yarigi (658d76ed.jpg Tamiri)
        // =====================================================================
        [Test]
        public void T24_CoastalTransitionBetween4x4SeaAndShore_HasZeroTJunctionEdgeCracks()
        {
            const int maxY = 12;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);
            uint beachSand = PackSolid(MaterialIdCatalog.Sand, 110);

            // x = 0..9: acik durgun deniz (bx=0 ve bx=1 tam 4x4 bloklar, x=8.0 sinirinda bx=2 parcali sahil bloguna komsu!)
            // x = 10..15: yukselen kumsal (y=6 kati kum -> bx=2 parcali sahil blogu)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 5; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    if (x >= 11)
                    {
                        solids[(6 << 8) | (z << 4) | x] = beachSand;
                    }
                    else
                    {
                        fluids[(6 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);

            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            Assert.That(tris.Count, Is.GreaterThan(16));

            // x = 8.0f gecis cizgisi uzerinde 1.05m'den uzun HICBIR kaba (4m) kenar olmamali:
            // bx=1 blogu x=8.0f kenarini 4 adet 1.0m segmente bolerek bx=2'nin 1m hucre kose dugumleriyle 1:1 dikmelidir!
            int seamEdgesChecked = 0;
            float maxSeamEdgeLenZ = 0f;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                CheckSeamEdgeAtX8(in t.C0.PositionCurr, in t.C1.PositionCurr, ref seamEdgesChecked, ref maxSeamEdgeLenZ);
                CheckSeamEdgeAtX8(in t.C1.PositionCurr, in t.C2.PositionCurr, ref seamEdgesChecked, ref maxSeamEdgeLenZ);
                CheckSeamEdgeAtX8(in t.C2.PositionCurr, in t.C0.PositionCurr, ref seamEdgesChecked, ref maxSeamEdgeLenZ);
            }

            Assert.That(seamEdgesChecked, Is.GreaterThanOrEqualTo(16),
                "Both sides of the x=8.0f transition between 4x4 pool blocks and 1x1 shore cells must emit matching 1m edges.");
            Assert.That(maxSeamEdgeLenZ, Is.LessThanOrEqualTo(1.02f),
                $"T-Junction crack detected at x=8.0f! Coarse >1m edge ({maxSeamEdgeLenZ:F3}m) next to 1m cells causes underwater slits under shader waves!");

            static void CheckSeamEdgeAtX8(in System.Numerics.Vector3 a, in System.Numerics.Vector3 b, ref int count, ref float maxDz)
            {
                if (MathF.Abs(a.X - 8.0f) <= 0.01f && MathF.Abs(b.X - 8.0f) <= 0.01f)
                {
                    float dz = MathF.Abs(a.Z - b.Z);
                    if (dz > 0.01f)
                    {
                        count++;
                        if (dz > maxDz) maxDz = dz;
                    }
                }
            }
        }

        // =====================================================================
        // T25: Acik Yamaca Dokulen Kazan Kubbesinin Zemine Muhurlenmesi (a891c944.jpg) & Isinlanmayan Yamac Yagmuru (e31f9a2c.jpg)
        // =====================================================================
        [Test]
        public void T25_OpenHillsideCauldronWaterMound_SealsOuterRimToGround_AndSlopeRillDoesNotTeleportInOneTick()
        {
            const int maxY = 14;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            // 1) Acik cimen yamac (duvar YOK — etrafi tamamen acik kuru hava!): y=6 d=96 (~6.55m cimen yuzeyi)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 5; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    solids[(6 << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Grass, 96);
                }
            }

            // Ortaya (x=6..8, z=6..8) kazanla Mass=220 yogun su dokuldu (y=7):
            for (int z = 6; z <= 8; z++)
            {
                for (int x = 6; x <= 8; x++)
                {
                    fluids[(7 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 220, isSettled: false).RawValue;
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(128);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            float maxOuterRimY = float.MinValue;
            int outerRimVerts = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                SampleOuterRimY(in t.C0.PositionCurr, ref outerRimVerts, ref maxOuterRimY);
                SampleOuterRimY(in t.C1.PositionCurr, ref outerRimVerts, ref maxOuterRimY);
                SampleOuterRimY(in t.C2.PositionCurr, ref outerRimVerts, ref maxOuterRimY);
            }

            Assert.That(outerRimVerts, Is.GreaterThan(0));
            Assert.That(maxOuterRimY, Is.LessThanOrEqualTo(6.65f),
                $"Open hillside cauldron water mound must tuck its outer rim down into the grass (~6.55m) instead of floating at ~7.86m with a transparent hollow underside (maxOuterRimY={maxOuterRimY:F3})!");

            // 2) Yamac yagmur derecigi (Mass = 2 ve Mass = 4): tek tikte %100 karsi hucreye isinlanmamali!
            Array.Clear(solids, 0, vol);
            Array.Clear(fluids, 0, vol);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float h = 8.60f - x * 0.22f;
                    int floorY = Math.Clamp((int)MathF.Floor(h), 2, 11);
                    float frac = Math.Clamp(h - floorY, 0f, 0.99f);
                    for (int y = 0; y < floorY; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                    byte dens = (byte)Math.Clamp(66 + (int)MathF.Round(frac * 58.0f), 66, 124);
                    solids[(floorY << 8) | (z << 4) | x] = PackSolid(MaterialIdCatalog.Stone, dens);
                }
            }

            int srcY = 0;
            for (int y = maxY - 2; y >= 1; y--)
            {
                if (solids[(y << 8) | (8 << 4) | 4] != 0u) { srcY = y + 1; break; }
            }
            int dstY = 0;
            for (int y = maxY - 2; y >= 1; y--)
            {
                if (solids[(y << 8) | (8 << 4) | 5] != 0u) { dstY = y + 1; break; }
            }

            fluids[(srcY << 8) | (8 << 4) | 4] = FluidCell16.Pack(FluidType.Water, 2, isSettled: false).RawValue;
            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                solids, fluids, 16, maxY, 16, out _, out _, stepParity: 0);

            byte remSrc = new FluidCell16(fluids[(srcY << 8) | (8 << 4) | 4]).Mass;
            byte dstMass = new FluidCell16(fluids[(dstY << 8) | (8 << 4) | 5]).Mass;
            Assert.That(remSrc, Is.EqualTo(1), "Mass=2 slope rill must leave 1 unit behind so source & destination stay connected!");
            Assert.That(dstMass, Is.EqualTo(1), "Mass=2 slope rill must advance 1 unit to downhill neighbor (zero 100% single-tick teleportation)!");

            tris.Clear();
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);
            bool touchesSeamX5 = false;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (MathF.Abs(t.C0.PositionCurr.X - 5.0f) <= 0.02f ||
                    MathF.Abs(t.C1.PositionCurr.X - 5.0f) <= 0.02f ||
                    MathF.Abs(t.C2.PositionCurr.X - 5.0f) <= 0.02f)
                {
                    touchesSeamX5 = true;
                    break;
                }
            }
            Assert.That(touchesSeamX5, Is.True,
                "Connected Mass=1+1 slope rill cells must meet continuously at the x=5.0f cell boundary instead of shrinking into isolated dots!");

            static void SampleOuterRimY(in System.Numerics.Vector3 p, ref int count, ref float maxY)
            {
                if (p.X <= 6.02f || p.X >= 8.98f || p.Z <= 6.02f || p.Z >= 8.98f)
                {
                    count++;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }
        }

        // =====================================================================
        // T26: Baslangic Goleti (Chunk 0, -1) Icbukey SurfaceNets Kose Muhru (67a8ad6a.jpg & 07180972.jpg Tamiri)
        // =====================================================================
        [Test]
        public void T26_SpawnPondChunk0Neg1_SealsAllConcaveSurfaceNetsShoreCorners_BeyondDualWallVertex()
        {
            const int maxY = 12;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullDirt = PackSolid(MaterialIdCatalog.Dirt);

            // 3 su + 1 kati SurfaceNets kosesi (Chunk 0,-1 baslangic goleti icbukey kosesi: (1,7) kati, (2,7)-(1,8)-(2,8) su!):
            // SurfaceNets dual duvari (1.25, 7.25) noktasina cekildigi icin (2,8) su kosesi en az (<= 1.22, <= 7.22) icine gomulmelidir!
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 5; y++) solids[(y << 8) | (z << 4) | x] = fullDirt;
                    bool inPond = (x >= 1 && x <= 3 && z >= 7 && z <= 9) && !(x == 1 && z == 7);
                    if (inPond)
                    {
                        fluids[(6 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                        fluids[(7 << 8) | (z << 4) | x] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                    }
                    else
                    {
                        solids[(6 << 8) | (z << 4) | x] = fullDirt;
                        solids[(7 << 8) | (z << 4) | x] = fullDirt;
                    }
                }
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            float minConcaveDiagDistTo125 = float.MaxValue;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (t.C0.Normal.Y < 0.7f) continue;
                CheckEmbedPast125(in t.C0.PositionCurr, ref minConcaveDiagDistTo125);
                CheckEmbedPast125(in t.C1.PositionCurr, ref minConcaveDiagDistTo125);
                CheckEmbedPast125(in t.C2.PositionCurr, ref minConcaveDiagDistTo125);
            }

            Assert.That(minConcaveDiagDistTo125, Is.LessThanOrEqualTo(0.0f),
                $"Concave SurfaceNets pond corner (2,8) must extend past the (1.25, 7.25) SurfaceNets dual wall so zero square pit hole appears (signedGap={minConcaveDiagDistTo125:F3}m)!");

            static void CheckEmbedPast125(in System.Numerics.Vector3 p, ref float minSignedGap)
            {
                if (p.X >= 1.0f && p.X <= 1.8f && p.Z >= 7.0f && p.Z <= 7.8f)
                {
                    float signedGap = MathF.Max(p.X - 1.25f, p.Z - 7.25f);
                    if (signedGap < minSignedGap) minSignedGap = signedGap;
                }
            }
        }

        // =====================================================================
        // T27: Havada Dusen Yuksek Selale Suyunun 1m Beyaz Kutu Yerine Silindirik Sutun Uretmesi (706fc114.jpg Tamiri)
        // =====================================================================
        [Test]
        public void T27_MidAirHighDropWaterfallStream_EmitsTaperedCylindricalColumn_ZeroOneMeterWhiteBoxPillars()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint fullRock = PackSolid(MaterialIdCatalog.Stone);

            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = 0; y <= 3; y++) solids[(y << 8) | (z << 4) | x] = fullRock;
                }
            }

            // (8, 8) sutununda y = 7, 8, 9, 10 boyunca havada serbest dusen su sutunu (Mass = 70)
            for (int y = 7; y <= 10; y++)
            {
                fluids[(y << 8) | (8 << 4) | 8] = FluidCell16.Pack(FluidType.Water, 70, isSettled: false).RawValue;
            }

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(256);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            Assert.That(tris.Count, Is.GreaterThan(16));
            float maxRadialOffsetFromCenter = 0f;
            int chalkWhiteCurtainTris = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                maxRadialOffsetFromCenter = MathF.Max(maxRadialOffsetFromCenter, RadialDist(in t.C0.PositionCurr));
                maxRadialOffsetFromCenter = MathF.Max(maxRadialOffsetFromCenter, RadialDist(in t.C1.PositionCurr));
                maxRadialOffsetFromCenter = MathF.Max(maxRadialOffsetFromCenter, RadialDist(in t.C2.PositionCurr));
                if (t.C0.WaterfallFoam01 >= 0.65f || t.C1.WaterfallFoam01 >= 0.65f || t.C2.WaterfallFoam01 >= 0.65f)
                {
                    chalkWhiteCurtainTris++;
                }
            }

            Assert.That(maxRadialOffsetFromCenter, Is.LessThanOrEqualTo(0.32f),
                $"Mid-air falling stream must taper to a <=0.32m radius cylindrical jet instead of a 1.0m x 1.0m square box (maxR={maxRadialOffsetFromCenter:F3}m)!");
            Assert.That(chalkWhiteCurtainTris, Is.EqualTo(0),
                "Mid-air falling stream must emit ZERO chalk-white (>=0.65 foam) vertical laser bars!");

            static float RadialDist(in System.Numerics.Vector3 p)
            {
                float dx = p.X - 8.5f;
                float dz = p.Z - 8.5f;
                return MathF.Sqrt(dx * dx + dz * dz);
            }
        }

        // =====================================================================
        // T28: Aktif Yamac Yagmurunun XZ Yanip Sonmemesi & 2-Blokluk Dik Dag Yamacindan Havaya Cikmadan Inmesi (b9c3b024.jpg)
        // =====================================================================
        [Test]
        public void T28_ActiveSlopeRainwater_NeverBlinksFootprintArea_AndGlidesAcrossTwoBlockSteepMountainDrop()
        {
            const int maxY = 16;
            const int vol = 16 * maxY * 16;
            var solids = new uint[vol];
            var fluids = new ushort[vol];
            uint ice = PackSolid(MaterialIdCatalog.PackedIce);

            // 2-blok/metre dik buzul dagi: x=5 y=10, x=6 y=8 (y=11'den y=9'a dogrudan yamac gecisi!)
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int topY = x <= 5 ? 10 : 8;
                    for (int y = 0; y <= topY; y++) solids[(y << 8) | (z << 4) | x] = ice;
                }
            }

            // Aktif (isSettled: false) tekil Mass=3 yagmur parseli: 0.08 m^2 noktaya buzulup "yok olmus" gibi gorunmemeli!
            fluids[(11 << 8) | (8 << 4) | 5] = FluidCell16.Pack(FluidType.Water, 3, isSettled: false).RawValue;

            var solidMap = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = solids };
            var fluidMap = new Dictionary<(int cx, int cz), ushort[]> { [(0, 0)] = fluids };
            var tris = new List<FluidRichTriangle>(64);
            FluidSurfaceSkirtMesher.GenerateChunkFluidRichSurfaceMesh(0, 0, maxY, solidMap, fluidMap, tris);

            float activeArea = 0f;
            for (int i = 0; i < tris.Count; i++)
            {
                FluidRichTriangle t = tris[i];
                if (t.C0.Normal.Y < 0.5f) continue;
                Vector2 p0 = new Vector2(t.C0.PositionCurr.X, t.C0.PositionCurr.Z);
                Vector2 p1 = new Vector2(t.C1.PositionCurr.X, t.C1.PositionCurr.Z);
                Vector2 p2 = new Vector2(t.C2.PositionCurr.X, t.C2.PositionCurr.Z);
                activeArea += 0.5f * MathF.Abs((p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X));
            }
            Assert.That(activeArea, Is.GreaterThanOrEqualTo(0.70f),
                $"Active (!IsSettled) moving rainwater must retain its full circular footprint (~0.71 m^2) and NOT shrink to a <=0.25 m^2 dot that blinks on/off (area={activeArea:F3} m^2)!");

            // 1 tik simulasyon adimi: (x=5, y=11) -> havadaki (x=6, y=11)'e DEGIL, dogrudan zemindeki (x=6, y=9) hucresine inmeli!
            FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                solids, fluids, 16, maxY, 16, out _, out _, stepParity: 0);

            byte remUpperSlope = new FluidCell16(fluids[(11 << 8) | (8 << 4) | 5]).Mass;
            byte midAirGhost = new FluidCell16(fluids[(11 << 8) | (8 << 4) | 6]).Mass;
            byte groundedLowerSlope = new FluidCell16(fluids[(9 << 8) | (8 << 4) | 6]).Mass;

            Assert.That(midAirGhost, Is.EqualTo(0), "Steep 2-block mountain rain must NOT step into mid-air (x=6, y=11) and vanish/pop!");
            Assert.That(remUpperSlope, Is.GreaterThanOrEqualTo(1), "Upper slope cell must retain >=1 unit so stream stays connected!");
            Assert.That(groundedLowerSlope, Is.GreaterThanOrEqualTo(1), "Lower grounded slope cell (x=6, y=9) must directly receive the stream!");
        }
    }
}
