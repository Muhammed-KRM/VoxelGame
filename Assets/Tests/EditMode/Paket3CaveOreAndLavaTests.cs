#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Character;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Jobs;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class Paket3CaveOreAndLavaTests
    {
        private static (Vector3 u, Vector3 v) MakeCanonicalEdge(Vector3 p, Vector3 q)
        {
            if (p.X != q.X) return p.X < q.X ? (p, q) : (q, p);
            if (p.Y != q.Y) return p.Y < q.Y ? (p, q) : (q, p);
            return p.Z <= q.Z ? (p, q) : (q, p);
        }

        private static bool TryRayIntersectTriangleVerticalXZ(
            float px,
            float pz,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float hitY)
        {
            hitY = 0f;

            float v0x = c.X - a.X, v0z = c.Z - a.Z;
            float v1x = b.X - a.X, v1z = b.Z - a.Z;
            float v2x = px - a.X, v2z = pz - a.Z;

            float dot00 = v0x * v0x + v0z * v0z;
            float dot01 = v0x * v1x + v0z * v1z;
            float dot02 = v0x * v2x + v0z * v2z;
            float dot11 = v1x * v1x + v1z * v1z;
            float dot12 = v1x * v2x + v1z * v2z;

            float denom = dot00 * dot11 - dot01 * dot01;
            if (MathF.Abs(denom) < 1e-8f)
            {
                return false;
            }

            float invDenom = 1.0f / denom;
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            if (u < -1e-4f || v < -1e-4f || (u + v) > 1.0002f)
            {
                return false;
            }

            float w = 1.0f - u - v;
            hitY = w * a.Y + v * b.Y + u * c.Y;
            return true;
        }

        private static Dictionary<(int cx, int cz), uint[]> BuildTwoChunkOreTestWorld(bool includeOreVeins)
        {
            var world = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = new uint[16 * 16 * 16],
                [(1, 0)] = new uint[16 * 16 * 16]
            };

            const float topH = 11.35f;
            const float botH = 2.65f;

            for (int cx = 0; cx <= 1; cx++)
            {
                uint[] chunk = world[(cx, 0)];
                for (int lz = 2; lz <= 13; lz++)
                {
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int wx = cx * 16 + lx;
                        if (wx < 2 || wx > 29)
                        {
                            continue;
                        }

                        for (int y = 2; y <= 12; y++)
                        {
                            float topDiff = topH - y;
                            float botDiff = y - botH;
                            float minDiff = MathF.Min(topDiff, botDiff);
                            int dens = (int)MathF.Round(64f + minDiff * 36f);
                            if (dens < 12)
                            {
                                continue;
                            }

                            byte mat = MaterialIdCatalog.Stone;
                            if (includeOreVeins)
                            {
                                // 1) Yatay cevher zemini (chunk (0,0) icinde + chunk (0,0)-(1,0) sinirinda)
                                if (y >= 10 && y <= 11 && wx >= 5 && wx <= 21 && lz >= 5 && lz <= 10)
                                {
                                    mat = (wx >= 8 && wx <= 10 && lz >= 6 && lz <= 8)
                                        ? MaterialIdCatalog.QuartzGem
                                        : MaterialIdCatalog.CopperOre;
                                }
                                // 2) Dikey ucurum duvarinda cevher damari
                                else if (wx >= 2 && wx <= 4 && y >= 5 && y <= 8 && lz >= 5 && lz <= 10)
                                {
                                    mat = MaterialIdCatalog.IronOre;
                                }
                                // 3) Alt tavan yuzeyinde kristal/cevher cikintisi
                                else if (y >= 2 && y <= 4 && wx >= 5 && wx <= 11 && lz >= 5 && lz <= 10)
                                {
                                    mat = MaterialIdCatalog.QuartzGem;
                                }
                            }

                            chunk[(y << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                                mat,
                                (byte)Math.Clamp(dens, 12, 127),
                                shapeId: 0,
                                rot24: 0,
                                BlokKokenDurumu.DogalKutle,
                                dmg4: 0);
                        }
                    }
                }
            }

            return world;
        }

        [Test]
        public void OreNuggetRelief_PreservesWatertightManifoldAndGroundSamplerTolerance()
        {
            Dictionary<(int cx, int cz), uint[]> oreWorld = BuildTwoChunkOreTestWorld(includeOreVeins: true);
            Dictionary<(int cx, int cz), uint[]> stoneWorld = BuildTwoChunkOreTestWorld(includeOreVeins: false);

            var trisLeft = new List<FacetedVoxelTriangle>(8192);
            var trisRight = new List<FacetedVoxelTriangle>(8192);
            var stoneBaselineLeft = new List<FacetedVoxelTriangle>(8192);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, 16, oreWorld, trisLeft, worldMicroPools: null, minY: 0);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(1, 0, 16, oreWorld, trisRight, worldMicroPools: null, minY: 0);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, 16, stoneWorld, stoneBaselineLeft, worldMicroPools: null, minY: 0);

            Assert.That(trisLeft.Count, Is.GreaterThan(stoneBaselineLeft.Count),
                "AK-1: 3B cevher rolyef piramitleri (4 ucgen/kuad) toplam ucgen sayisini duz kaya tabanina gore artirmalidir.");

            // 1) Su gecirmez manifold denetimi: komsu chunk ciftinin birlesiminde 0 acik ve 0 non-manifold kenar
            var edgeCounts = new Dictionary<(Vector3 u, Vector3 v), int>(trisLeft.Count * 2);
            void AccumulateEdges(List<FacetedVoxelTriangle> tris, Vector3 chunkOffset)
            {
                for (int i = 0; i < tris.Count; i++)
                {
                    FacetedVoxelTriangle t = tris[i];
                    Vector3 a = t.V0 + chunkOffset;
                    Vector3 b = t.V1 + chunkOffset;
                    Vector3 c = t.V2 + chunkOffset;

                    var e0 = MakeCanonicalEdge(a, b);
                    var e1 = MakeCanonicalEdge(b, c);
                    var e2 = MakeCanonicalEdge(c, a);

                    edgeCounts[e0] = edgeCounts.TryGetValue(e0, out int c0) ? c0 + 1 : 1;
                    edgeCounts[e1] = edgeCounts.TryGetValue(e1, out int c1) ? c1 + 1 : 1;
                    edgeCounts[e2] = edgeCounts.TryGetValue(e2, out int c2) ? c2 + 1 : 1;
                }
            }

            AccumulateEdges(trisLeft, Vector3.Zero);
            AccumulateEdges(trisRight, new Vector3(16f, 0f, 0f));

            int openEdges = 0;
            int nonManifoldEdges = 0;
            foreach (KeyValuePair<(Vector3 u, Vector3 v), int> kv in edgeCounts)
            {
                if (kv.Value == 1) openEdges++;
                else if (kv.Value > 2) nonManifoldEdges++;
            }

            Assert.That(edgeCounts.Count, Is.GreaterThan(200));
            Assert.That(openEdges, Is.EqualTo(0), $"AK-1: Cevher rolyefli chunk ciftinde {openEdges} adet acik sinir kenari tespit edildi.");
            Assert.That(nonManifoldEdges, Is.EqualTo(0), $"AK-1: Cevher rolyefli chunk ciftinde {nonManifoldEdges} adet non-manifold kenar tespit edildi.");

            // 2) Yatay cevher zemininde VoxelSurfaceNetsGroundSampler farkinin <= 0.05m kaldigini dogrula
            float maxGroundDiff = 0f;
            int sampledGroundPoints = 0;
            for (int wz = 6; wz <= 9; wz++)
            {
                for (int wx = 6; wx <= 10; wx++)
                {
                    float px = wx + 0.41f;
                    float pz = wz + 0.43f;
                    bool ok = VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormalNumerics(
                        px, pz, referenceFootY: 11.5f, maxSearchUpAboveRef: 3.0f, oreWorld,
                        out float samplerY, out _);
                    Assert.That(ok, Is.True, $"Zemin orneklemesi ({px}, {pz}) basarisiz oldu.");

                    float bestMeshY = float.NegativeInfinity;
                    for (int i = 0; i < trisLeft.Count; i++)
                    {
                        FacetedVoxelTriangle t = trisLeft[i];
                        if (t.Normal.Y <= 0.35f) continue;
                        if (TryRayIntersectTriangleVerticalXZ(px, pz, t.V0, t.V1, t.V2, out float hitY) && hitY > bestMeshY)
                        {
                            bestMeshY = hitY;
                        }
                    }

                    Assert.That(bestMeshY, Is.GreaterThan(10.0f), $"Mesh uzerinde ({px}, {pz}) zemin isabeti bulunamadi.");
                    float diff = MathF.Abs(bestMeshY - samplerY);
                    if (diff > maxGroundDiff) maxGroundDiff = diff;
                    sampledGroundPoints++;
                }
            }

            Assert.That(sampledGroundPoints, Is.GreaterThanOrEqualTo(12));
            Assert.That(maxGroundDiff, Is.LessThanOrEqualTo(0.05f),
                $"AK-1: Yatay cevher zemininde rolyef - GroundSampler farki {maxGroundDiff:F4}m > 0.05m.");

            // 3) Duvar ve tavanda belirgin 3B faset cikintisi oldugunu dogrula (V2/apex normal boyunca >= 0.08m cikinti)
            int wallOreTriangles = 0;
            int ceilingOreTriangles = 0;
            float maxWallProtrusion = 0f;
            float maxCeilingProtrusion = 0f;

            // Duz bazalt/kaya tavan ve duvar duzlemleriyle kiyaslamak icin baz tabanin en uc X/Y degerlerini olc
            float stoneMinWallX = float.PositiveInfinity;
            float stoneMinCeilingY = float.PositiveInfinity;
            for (int i = 0; i < stoneBaselineLeft.Count; i++)
            {
                FacetedVoxelTriangle st = stoneBaselineLeft[i];
                if (st.Normal.X < -0.7f && st.V0.Z >= 5f && st.V0.Z <= 10f && st.V0.Y >= 5f && st.V0.Y <= 8f)
                {
                    stoneMinWallX = MathF.Min(stoneMinWallX, MathF.Min(st.V0.X, MathF.Min(st.V1.X, st.V2.X)));
                }
                if (st.Normal.Y < -0.7f && st.V0.X >= 5f && st.V0.X <= 11f && st.V0.Z >= 5f && st.V0.Z <= 10f)
                {
                    stoneMinCeilingY = MathF.Min(stoneMinCeilingY, MathF.Min(st.V0.Y, MathF.Min(st.V1.Y, st.V2.Y)));
                }
            }

            for (int i = 0; i < trisLeft.Count; i++)
            {
                FacetedVoxelTriangle t = trisLeft[i];
                if (!HybridSurfaceNetsAndShapeMesher.IsReliefOreOrCrystalMaterial(t.MaterialId))
                {
                    continue;
                }

                if (t.MaterialId == MaterialIdCatalog.IronOre && t.Normal.X < -0.35f)
                {
                    wallOreTriangles++;
                    float minTriX = MathF.Min(t.V0.X, MathF.Min(t.V1.X, t.V2.X));
                    float prot = stoneMinWallX - minTriX;
                    if (prot > maxWallProtrusion) maxWallProtrusion = prot;
                }
                else if (t.MaterialId == MaterialIdCatalog.QuartzGem && t.Normal.Y < -0.35f)
                {
                    ceilingOreTriangles++;
                    float minTriY = MathF.Min(t.V0.Y, MathF.Min(t.V1.Y, t.V2.Y));
                    float prot = stoneMinCeilingY - minTriY;
                    if (prot > maxCeilingProtrusion) maxCeilingProtrusion = prot;
                }
            }

            Assert.That(wallOreTriangles, Is.GreaterThanOrEqualTo(8), "Duvar demir damari fasetli cikinti uretmeli.");
            Assert.That(ceilingOreTriangles, Is.GreaterThanOrEqualTo(8), "Tavan kuvars damari fasetli cikinti uretmeli.");
            Assert.That(maxWallProtrusion, Is.GreaterThanOrEqualTo(0.075f),
                $"Duvar cevher cikintisi ({maxWallProtrusion:F4}m) beklenenden kucuk.");
            Assert.That(maxCeilingProtrusion, Is.GreaterThanOrEqualTo(0.080f),
                $"Tavan kristal cikintisi ({maxCeilingProtrusion:F4}m) beklenenden kucuk.");
        }

        [Test]
        public void SurfaceOreOutcrops_MatchUnderlyingDeepVeinColumns()
        {
            // Merkez r < 12m koruma kuralini dogrula
            Assert.That(
                StratocraftFloatingIslandWorldGen.TrySampleUnderlyingRichOreForOutcrop(4, 4, 96.0f, 0),
                Is.EqualTo(MaterialIdCatalog.Air),
                "AK-3: r < 12m Baslangic Atolyesi ve Ors meydaninda yuzey outcrop uretilmemeli.");

            int verifiedOreOutcrops = 0;
            for (int cz = -5; cz <= 5; cz++)
            {
                for (int cx = -5; cx <= 5; cx++)
                {
                    var (voxels, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    int maxChunkY = voxels.Length >> 8;

                    for (int lz = 1; lz < 15; lz++)
                    {
                        int wz = cz * 16 + lz;
                        for (int lx = 1; lx < 15; lx++)
                        {
                            int wx = cx * 16 + lx;
                            float rSq = (float)wx * wx + (float)wz * wz;
                            if (rSq < 144.0f)
                            {
                                continue;
                            }

                            float surfTopY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                            if (surfTopY < StratocraftFloatingIslandWorldGen.SeaLevelY + 1.2f)
                            {
                                continue;
                            }

                            int baseY = Math.Clamp((int)MathF.Round(surfTopY), 12, maxChunkY - 8);
                            int outcropY = baseY + 1;
                            if (outcropY >= maxChunkY)
                            {
                                continue;
                            }

                            uint raw = voxels[(outcropY << 8) | (lz << 4) | lx];
                            byte shapeId = VoxelBitUtility.GetShapeId(raw);
                            byte matId = VoxelBitUtility.GetMaterialId(raw);
                            if (shapeId == 0 ||
                                VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle ||
                                !MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId))
                            {
                                continue;
                            }

                            verifiedOreOutcrops++;

                            Assert.That(MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId), Is.True);
                            Assert.That(MaterialIdCatalog.GetRequiredCrystalTierForTerrain(matId), Is.EqualTo((byte)0),
                                $"AK-3: Yuzey cevher izi (mat={matId}) vakum bagisik olmalidir.");

                            float r = MathF.Sqrt(rSq);
                            int radialRing = r < 120.0f ? 0 : Math.Clamp(1 + (int)(r / 1200.0f), 1, 6);
                            byte sampledDeepOre = StratocraftFloatingIslandWorldGen.TrySampleUnderlyingRichOreForOutcrop(
                                wx, wz, surfTopY, radialRing);
                            Assert.That(sampledDeepOre, Is.EqualTo(matId),
                                $"AK-3: ({wx},{wz}) yuzeyindeki outcrop ({matId}) derin damar izdusumu ({sampledDeepOre}) ile eslesmiyor.");

                            // Gercek uretilen chunk sutununun altinda [baseY-58 .. baseY-18] araliginda en az 2 ayni cevher bloku bulunmali
                            int startY = baseY - 18;
                            int endY = Math.Max(14, baseY - 58);
                            int matchingDeepCount = 0;
                            int col = (lz << 4) | lx;
                            for (int y = startY; y >= endY; y--)
                            {
                                if (VoxelBitUtility.GetMaterialId(voxels[(y << 8) | col]) == matId)
                                {
                                    matchingDeepCount++;
                                }
                            }

                            Assert.That(matchingDeepCount, Is.GreaterThanOrEqualTo(2),
                                $"AK-3: ({wx},{wz}) yuzey outcrop ({matId}) altindaki derin sutunda eslesen damar sayisi ({matchingDeepCount}) < 2.");
                        }
                    }
                }
            }

            Assert.That(verifiedOreOutcrops, Is.GreaterThanOrEqualTo(5),
                "AK-3: Taramada yeterli sayida derin damar izdusumlu yuzey cevher outcrop'u bulunamadi.");
        }

        [Test]
        public void GeothermalUCurve_CoolsInShallowCaveAndHeatsInDeepMagma()
        {
            const float surfaceY = 96.0f;

            // 1) Y = 75m sig magarada negatif alt-kot (-21m) ile [-6.0, +4.0] C araligi
            float[] testBiomes = { -10.0f, 18.0f, 22.0f, 30.0f };
            for (int i = 0; i < testBiomes.Length; i++)
            {
                float shallowTemp = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    baseSeasonTempCelsius: testBiomes[i],
                    worldAltitudeMeters: 75.0f - surfaceY,
                    isUndergroundEnclosed: true,
                    isNightTime: false,
                    windSpeedMetersPerSec: 0f,
                    nearbyHeatSourceBonusCelsius: 0f,
                    absoluteWorldY: 75.0f);

                Assert.That(shallowTemp, Is.InRange(-6.0f, 4.0f),
                    $"AK-4: Y=75m sig magarada (biyom={testBiomes[i]}C) sicaklik [-6, +4] C disinda: {shallowTemp:F2}C");

                // Y = 12m derin magmada [+65.0, +110.0] C araligi
                float deepMagmaTemp = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    baseSeasonTempCelsius: testBiomes[i],
                    worldAltitudeMeters: 12.0f - surfaceY,
                    isUndergroundEnclosed: true,
                    isNightTime: false,
                    windSpeedMetersPerSec: 0f,
                    nearbyHeatSourceBonusCelsius: 0f,
                    absoluteWorldY: 12.0f);

                Assert.That(deepMagmaTemp, Is.InRange(65.0f, 110.0f),
                    $"AK-4: Y=12m derin magmada (biyom={testBiomes[i]}C) sicaklik [+65, +110] C disinda: {deepMagmaTemp:F2}C");
            }

            // Negatif worldAltitudeMeters kesilip 0'a kelepcelenmemeli
            float clampedZeroRef = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                baseSeasonTempCelsius: 22.0f,
                worldAltitudeMeters: 0.0f,
                isUndergroundEnclosed: true,
                isNightTime: false,
                windSpeedMetersPerSec: 0f,
                nearbyHeatSourceBonusCelsius: 0f,
                absoluteWorldY: 75.0f);
            float negativeAltVal = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                baseSeasonTempCelsius: 22.0f,
                worldAltitudeMeters: 75.0f - surfaceY,
                isUndergroundEnclosed: true,
                isNightTime: false,
                windSpeedMetersPerSec: 0f,
                nearbyHeatSourceBonusCelsius: 0f,
                absoluteWorldY: 75.0f);
            Assert.That(clampedZeroRef, Is.EqualTo(22.0f).Within(0.01f));
            Assert.That(negativeAltVal, Is.LessThan(5.0f));

            // 2) Yuzeyden (95.5m) derin magmaya (5.0m) C1-surekli U-egrisi taramasi
            float prevTemp = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                22.0f, 95.5f - surfaceY, isUndergroundEnclosed: true, false, 0f, 0f, 95.5f);
            float minCaveTemp = prevTemp;
            float maxDeepTemp = prevTemp;

            const float stepY = 0.25f;
            for (float y = 95.25f; y >= 5.0f; y -= stepY)
            {
                float temp = ThermalAndBiologySystem.EvaluateAmbientTemperatureCelsius(
                    22.0f, y - surfaceY, isUndergroundEnclosed: true, false, 0f, 0f, y);

                if (temp < minCaveTemp) minCaveTemp = temp;
                if (temp > maxDeepTemp) maxDeepTemp = temp;

                float jump = MathF.Abs(temp - prevTemp);
                Assert.That(jump, Is.LessThanOrEqualTo(3.0f),
                    $"AK-4: Y={y:F2}m noktasinda sicaklik sicramasi tespit edildi (delta={jump:F2}C).");

                // C1 ic-bolge turev surekliligi kontrolu (soguk tuzak [84..56] ve derin jeotermal rampa [43..6])
                if ((y <= 84.0f && y >= 56.0f) || (y <= 43.0f && y >= 6.0f))
                {
                    float tPlus = ThermalAndBiologySystem.EvaluateUndergroundGeothermalTemperatureCelsius(22.0f, surfaceY - (y + 0.1f), y + 0.1f);
                    float tMinus = ThermalAndBiologySystem.EvaluateUndergroundGeothermalTemperatureCelsius(22.0f, surfaceY - (y - 0.1f), y - 0.1f);
                    float leftSlope = (tPlus - temp) / 0.1f;
                    float rightSlope = (temp - tMinus) / 0.1f;
                    Assert.That(MathF.Abs(leftSlope - rightSlope), Is.LessThan(0.5f),
                        $"AK-4: Y={y:F2}m C1 egim sureksizligi.");
                }

                prevTemp = temp;
            }

            Assert.That(minCaveTemp, Is.InRange(-6.0f, 4.0f), "U-egrisinin dibi sig soguk tuzak bandinda olmali.");
            Assert.That(maxDeepTemp, Is.InRange(65.0f, 110.0f), "U-egrisinin derin ucu magma bandina tirmanmali.");
        }

        [Test]
        public void UndergroundCaveLakes_SpawnWithin12To18PercentAndRemainSettledLeakFree()
        {
            // 1) Katman 1-2 (y in 46..73) 24m x 24m makro-oda orneklemesinde %12 - %18 su golu orani
            int sampledChambers = 0;
            int lakeChambers = 0;
            int sampleLakeChunkX = int.MinValue;
            int sampleLakeChunkZ = int.MinValue;
            int sampleLakeWaterY = -1;

            for (int cz = -12; cz <= 12; cz++)
            {
                for (int cx = -12; cx <= 12; cx++)
                {
                    // localX=10, localZ=10 secilerek hem tek/cift chamberlarda canak kenari (lx/lz=0,15) disinda kalinir
                    int wx = cx * 24 + 10;
                    int wz = cz * 24 + 10;
                    if (wx * wx + wz * wz < 32 * 32)
                    {
                        continue;
                    }

                    sampledChambers++;
                    for (int y = 46; y <= 73; y++)
                    {
                        if (StratocraftFloatingIslandWorldGen.TryEvaluateCaveChamberWaterLake(
                                wx, y, wz, swell01: 0.68f, provinceNoise: 0.65f,
                                out bool inLakeWater, out _) &&
                            inLakeWater)
                        {
                            lakeChambers++;
                            if (sampleLakeChunkX == int.MinValue)
                            {
                                int chunkX = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                                int chunkZ = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                                // [F12] veya giris bolgelerinden uzak temiz bir chunk sec
                                var (testVox, testFluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(chunkX, chunkZ);
                                bool foundWaterInChunk = false;
                                for (int idx = 0; idx < testFluids.Length; idx++)
                                {
                                    int cy = idx >> 8;
                                    if (cy >= 46 && cy <= 73)
                                    {
                                        FluidCell16 fc = new FluidCell16(testFluids[idx]);
                                        if (fc.Type == FluidType.Water && fc.Mass > 0)
                                        {
                                            foundWaterInChunk = true;
                                            break;
                                        }
                                    }
                                }

                                if (foundWaterInChunk && testVox.Length > 0)
                                {
                                    sampleLakeChunkX = chunkX;
                                    sampleLakeChunkZ = chunkZ;
                                    sampleLakeWaterY = y;
                                }
                            }
                            break;
                        }
                    }
                }
            }

            Assert.That(sampledChambers, Is.GreaterThanOrEqualTo(400));
            float lakePercentage = (lakeChambers * 100.0f) / sampledChambers;
            Assert.That(lakePercentage, Is.InRange(12.0f, 18.0f),
                $"AK-5: Katman 1-2 yeralti gol orani {lakePercentage:F2}% (%12-%18 hedef bandi disinda).");
            Assert.That(sampleLakeChunkX, Is.Not.EqualTo(int.MinValue),
                "AK-5: Gercek uretilen chunk icinde Katman 1-2 yeralti su golu bulunamadi.");

            // 2) Gercek uretilen gol chunk'inda tum gol hucrelerinin IsSettled == true ve FluidType.Water oldugunu dogrula
            var (voxels, fluids) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(
                sampleLakeChunkX, sampleLakeChunkZ);
            int maxChunkY = voxels.Length >> 8;

            long initialCaveLakeMass = 0;
            int settledLakeCells = 0;
            long initialBelowBasinMass = 0;

            for (int y = 0; y < maxChunkY; y++)
            {
                int yBase = y << 8;
                for (int i = 0; i < 256; i++)
                {
                    ushort raw = fluids[yBase | i];
                    if (raw == 0) continue;

                    FluidCell16 fc = new FluidCell16(raw);
                    if (y >= 46 && y <= 73 && fc.Type == FluidType.Water && fc.Mass > 0)
                    {
                        Assert.That(fc.IsSettled, Is.True,
                            $"AK-5: Magara golu su hucresi (y={y}) IsSettled=true olarak uretilmelidir.");
                        initialCaveLakeMass += fc.Mass;
                        settledLakeCells++;
                    }
                    else if (y < 46 && fc.Type == FluidType.Water)
                    {
                        initialBelowBasinMass += fc.Mass;
                    }
                }
            }

            Assert.That(settledLakeCells, Is.GreaterThan(0));
            Assert.That(initialCaveLakeMass, Is.GreaterThan(0));

            // 3) StepFluidGridVolumeConservative ile hem uyku halinde hem de uyanik (IsSettled temizlenmis) durumda
            // gol tabanindan alt bosluga 0 birim sizinti oldugunu dogrula
            for (int step = 0; step < 5; step++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    voxels, fluids, 16, maxChunkY, 16, out _, out _, stepParity: step);
            }

            // Canak tabani muhrunu zorlamak icin gol hucrelerinin IsSettled bitini temizleyip 15 adim simule et
            for (int y = 46; y <= 73; y++)
            {
                int yBase = y << 8;
                for (int i = 0; i < 256; i++)
                {
                    ushort raw = fluids[yBase | i];
                    if (raw != 0)
                    {
                        FluidCell16 fc = new FluidCell16(raw);
                        if (fc.Type == FluidType.Water && fc.Mass > 0)
                        {
                            fluids[yBase | i] = (ushort)(raw & ~FluidCell16.SettledBitMask);
                        }
                    }
                }
            }

            for (int step = 0; step < 15; step++)
            {
                FluidCellularAutomataJob.StepFluidGridVolumeConservative(
                    voxels, fluids, 16, maxChunkY, 16, out _, out _, stepParity: step);
            }

            long postSimCaveLakeMass = 0;
            long postSimBelowBasinMass = 0;
            for (int y = 0; y < maxChunkY; y++)
            {
                int yBase = y << 8;
                for (int i = 0; i < 256; i++)
                {
                    ushort raw = fluids[yBase | i];
                    if (raw == 0) continue;

                    FluidCell16 fc = new FluidCell16(raw);
                    if (y >= 46 && y <= 73 && fc.Type == FluidType.Water)
                    {
                        postSimCaveLakeMass += fc.Mass;
                    }
                    else if (y < 46 && fc.Type == FluidType.Water)
                    {
                        postSimBelowBasinMass += fc.Mass;
                    }
                }
            }

            Assert.That(postSimBelowBasinMass, Is.EqualTo(initialBelowBasinMass),
                $"AK-5: Gol tabanindan alt bosluga su sizintisi tespit edildi (once={initialBelowBasinMass}, sonra={postSimBelowBasinMass}, yWater~{sampleLakeWaterY}).");
            Assert.That(postSimCaveLakeMass, Is.EqualTo(initialCaveLakeMass),
                "AK-5: Canak icindeki gol su kutlesi StepFluidGridVolumeConservative sonrasi %100 korunmalidir.");
        }

        [Test]
        public void LavaSolidification_CrustsEdgeFirstInCoolZoneAndPreservesStoichiometricObsidian()
        {
            const int maxY = 16;
            const int chunkLen = 16 * 16 * maxY;

            // --- BOLUM A: T_env = 20 C serin bolgede kenar-oncelikli 8-15 sn kabuklasma, 3.5m isi bonusu ve 150 sn Basalt ---
            var coolSolids = new uint[chunkLen];
            var coolFluids = new ushort[chunkLen];
            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            // Katman y=2 alt taban
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    coolSolids[(2 << 8) | (z << 4) | x] = stoneRaw;
                }
            }

            // 3x3x3 sivi lav kubu: lx in [6..8], y in [3..5], lz in [6..8].
            // Merkez hucre (7, 4, 7) 6 taraftan lav ile cevrili oldugu icin kenarlar katilasmadan ONCE asla katilasmamali!
            ushort lavaCellRaw = FluidCell16.Pack(FluidType.Lava, 255).RawValue;
            for (int y = 3; y <= 5; y++)
            {
                for (int lz = 6; lz <= 8; lz++)
                {
                    for (int lx = 6; lx <= 8; lx++)
                    {
                        coolFluids[(y << 8) | (lz << 4) | lx] = lavaCellRaw;
                    }
                }
            }

            var lavaSystem = new LavaSolidificationAndCoolingSystem();
            Func<int, int, int, float> coolTempSampler = (_, _, _) => 20.0f;

            float elapsedSeconds = 0f;
            float firstCrustTime = -1f;
            int totalCrustConversions = 0;
            const float dt = 0.5f;
            int centerIdx = (4 << 8) | (7 << 4) | 7;

            for (int step = 0; step < 32; step++) // 16.0 sn
            {
                elapsedSeconds += dt;
                int converted = lavaSystem.StepChunkLavaCrusting(
                    0, 0, coolSolids, coolFluids, maxY, dt, coolTempSampler, out ushort dirtyMask);

                if (converted > 0)
                {
                    Assert.That(dirtyMask, Is.Not.EqualTo((ushort)0));
                    if (firstCrustTime < 0f)
                    {
                        firstCrustTime = elapsedSeconds;
                        // Dis kenarlar katilastigi anda ic merkez (7,4,7) hala sivi lav olmali!
                        FluidCell16 centerFluidAtFirstCrust = new FluidCell16(coolFluids[centerIdx]);
                        Assert.That(centerFluidAtFirstCrust.Type, Is.EqualTo(FluidType.Lava),
                            "AK-6: Dis kenarlar katilasirken ic cekirdek lav hucresi henuz sivi olmalidir (kenar-oncelikli kabuklasma).");
                        Assert.That(VoxelBitUtility.GetMaterialId(coolSolids[centerIdx]), Is.EqualTo(MaterialIdCatalog.Air));
                    }

                    totalCrustConversions += converted;
                }
            }

            Assert.That(firstCrustTime, Is.InRange(8.0f, 15.0f),
                $"AK-6: Ilk dis kenar kabuklasma suresi ({firstCrustTime:F2} sn) 8-15 sn bandinda olmalidir.");
            Assert.That(totalCrustConversions, Is.GreaterThanOrEqualTo(8));

            // Katilasan ilk dis koselerden birinin (6, 3, 6) MagmaBlock (11) oldugunu ve r < 3.5m icinde isi bonusu verdigini dogrula
            int cornerIdx = (3 << 8) | (6 << 4) | 6;
            Assert.That(VoxelBitUtility.GetMaterialId(coolSolids[cornerIdx]), Is.EqualTo(MaterialIdCatalog.MagmaBlock));

            var worldChunks = new Dictionary<(int cx, int cz), uint[]> { [(0, 0)] = coolSolids };
            var dirtyChunks = new HashSet<(int cx, int cz)>();

            float nearBonus = lavaSystem.EvaluateNearbyHotRockThermalBonus(6.5f, 4.5f, 6.5f, worldChunks, out bool withinDryingZone);
            Assert.That(nearBonus, Is.GreaterThan(0.0f));
            Assert.That(nearBonus, Is.LessThanOrEqualTo(LavaSolidificationAndCoolingSystem.HotRockPeakBonusCelsius));
            Assert.That(withinDryingZone, Is.True);

            float farBonus = lavaSystem.EvaluateNearbyHotRockThermalBonus(13.0f, 4.5f, 13.0f, worldChunks, out bool farDryingZone);
            Assert.That(farBonus, Is.EqualTo(0.0f));
            Assert.That(farDryingZone, Is.False);

            // 150 sn boyunca soguma adimini kos -> tum MagmaBlock kayalari Basalt (7) olmali
            int cooledToBasalt = 0;
            for (int sec = 0; sec < 155; sec++)
            {
                cooledToBasalt += lavaSystem.StepCoolingVolcanicRocks(1.0f, worldChunks, dirtyChunks);
            }

            Assert.That(cooledToBasalt, Is.EqualTo(totalCrustConversions),
                "AK-6: Katilasan tum MagmaBlock bloklari ~150 sn sonunda Basalt (7) bloguna donusmelidir.");
            Assert.That(VoxelBitUtility.GetMaterialId(coolSolids[cornerIdx]), Is.EqualTo(MaterialIdCatalog.Basalt));
            Assert.That(dirtyChunks.Contains((0, 0)), Is.True);
            Assert.That(lavaSystem.ActiveCoolingRockCount, Is.EqualTo(0));

            // --- BOLUM B: T_env = 68 C sicak bolgede 60 sn boyunca %100 sivi kalma ---
            var hotSolids = new uint[chunkLen];
            var hotFluids = new ushort[chunkLen];
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    hotSolids[(2 << 8) | (z << 4) | x] = stoneRaw;
                }
            }
            for (int lz = 6; lz <= 8; lz++)
            {
                for (int lx = 6; lx <= 8; lx++)
                {
                    hotFluids[(3 << 8) | (lz << 4) | lx] = lavaCellRaw;
                }
            }

            var hotLavaSystem = new LavaSolidificationAndCoolingSystem();
            Func<int, int, int, float> hotTempSampler = (_, _, _) => 68.0f;
            int hotConverted = 0;
            for (int step = 0; step < 120; step++) // 60 sn
            {
                hotConverted += hotLavaSystem.StepChunkLavaCrusting(
                    0, 0, hotSolids, hotFluids, maxY, 0.5f, hotTempSampler, out _);
            }

            Assert.That(hotConverted, Is.EqualTo(0), "AK-6: T_env = 68 C sicak ortamda lav katilasmamalidir.");
            Assert.That(hotLavaSystem.ActiveChillCellCount, Is.EqualTo(0));
            for (int lz = 6; lz <= 8; lz++)
            {
                for (int lx = 6; lx <= 8; lx++)
                {
                    FluidCell16 fc = new FluidCell16(hotFluids[(3 << 8) | (lz << 4) | lx]);
                    Assert.That(fc.Type, Is.EqualTo(FluidType.Lava));
                    Assert.That(fc.Mass, Is.EqualTo((byte)255));
                }
            }

            // --- BOLUM C: Water >= 128 + Lava >= 128 -> 1 NaturalObsidian stokiyometrik kurali ---
            var reactSolids = new uint[chunkLen];
            var reactFluids = new ushort[chunkLen];
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    reactSolids[(2 << 8) | (z << 4) | x] = stoneRaw;
                }
            }
            int reactLavaIdx = (3 << 8) | (5 << 4) | 5;
            int reactWaterIdx = (4 << 8) | (5 << 4) | 5;
            reactFluids[reactLavaIdx] = FluidCell16.Pack(FluidType.Lava, 180).RawValue;
            reactFluids[reactWaterIdx] = FluidCell16.Pack(FluidType.Water, 200).RawValue;

            // Serin ortamda dahi su komsulugu varsa LavaSolidificationAndCoolingSystem o hucreye dokunmamali
            int stolen = lavaSystem.StepChunkLavaCrusting(0, 0, reactSolids, reactFluids, maxY, 12.0f, coolTempSampler, out _);
            Assert.That(stolen, Is.EqualTo(0), "AK-6: Su temasindaki lav hucresi MagmaBlock'a cevrilmemelidir.");

            var fluidSim = new FluidSimulationSystem();
            long remainingReactMass = fluidSim.StepChunkFluid(
                reactSolids, reactFluids, 16, maxY, 16, out int obsidianCount, out int fizzleCount);

            Assert.That(obsidianCount, Is.EqualTo(1), "AK-6: Water>=128 + Lava>=128 temasinda tam 1 NaturalObsidian olusmalidir.");
            Assert.That(fizzleCount, Is.EqualTo(0));
            Assert.That(remainingReactMass, Is.EqualTo(0));
            Assert.That(VoxelBitUtility.GetMaterialId(reactSolids[reactLavaIdx]), Is.EqualTo(MaterialIdCatalog.NaturalObsidian));
        }

        [Test]
        public void CaveEcology_EnforcesSafeZone75mAndTriggersPreAmbushTelegraph()
        {
            // 1) r < 75m Baslangic Vadisi icinde 0 dogum ve acik havada 0 dogum
            for (int angleDeg = 0; angleDeg < 360; angleDeg += 15)
            {
                float rad = angleDeg * (MathF.PI / 180.0f);
                float[] radii = { 0f, 25f, 50f, 74.5f };
                for (int ri = 0; ri < radii.Length; ri++)
                {
                    float wx = MathF.Cos(rad) * radii[ri];
                    float wz = MathF.Sin(rad) * radii[ri];
                    Assert.That(CaveEcologyAndSpawnSystem.CanSpawnInCaveAt(wx, 55.0f, wz, isEnclosedCave: true), Is.False,
                        $"AK-7: r={radii[ri]}m < 75m icinde magara canlisi dogmamalidir.");
                }
            }

            Assert.That(CaveEcologyAndSpawnSystem.CanSpawnInCaveAt(100.0f, 55.0f, 0.0f, isEnclosedCave: false), Is.False,
                "AK-7: Acik gokyuzu altinda (isEnclosedCave=false) magara canlisi dogmamalidir.");
            Assert.That(CaveEcologyAndSpawnSystem.CanSpawnInCaveAt(75.5f, 55.0f, 0.0f, isEnclosedCave: true), Is.True,
                "AK-7: r >= 75m kapali magarada doguma izin verilmelidir.");

            // 2) Katman / cevre kurallarina gore 5 magara varyantinin secildigini dogrula
            bool sawSpider = false;
            bool sawBat = false;
            bool sawScorpion = false;
            bool sawKnight = false;
            bool sawSerpent = false;

            for (int seed = 0; seed < 32; seed++)
            {
                CaveCreatureVariantKind l12 = CaveEcologyAndSpawnSystem.SelectCaveVariantForDepth(
                    worldY: 64, nearCrystalWall: false, nearLavaOrMagma: false, deterministicSeed: seed);
                if (l12 == CaveCreatureVariantKind.GiantCaveSpider) sawSpider = true;
                if (l12 == CaveCreatureVariantKind.CaveBatCrawler) sawBat = true;

                CaveCreatureVariantKind l3 = CaveEcologyAndSpawnSystem.SelectCaveVariantForDepth(
                    worldY: 32, nearCrystalWall: false, nearLavaOrMagma: false, deterministicSeed: seed);
                if (l3 == CaveCreatureVariantKind.AbyssalSkeletonKnight) sawKnight = true;
            }

            CaveCreatureVariantKind crystalVariant = CaveEcologyAndSpawnSystem.SelectCaveVariantForDepth(
                worldY: 52, nearCrystalWall: true, nearLavaOrMagma: false, deterministicSeed: 1);
            if (crystalVariant == CaveCreatureVariantKind.CrystalCavernScorpion) sawScorpion = true;

            CaveCreatureVariantKind magmaVariant = CaveEcologyAndSpawnSystem.SelectCaveVariantForDepth(
                worldY: 16, nearCrystalWall: false, nearLavaOrMagma: true, deterministicSeed: 1);
            if (magmaVariant == CaveCreatureVariantKind.MagmaSerpent) sawSerpent = true;

            Assert.That(sawSpider, Is.True, "AK-7: GiantCaveSpider Katman 1-2'de secilmeli.");
            Assert.That(sawBat, Is.True, "AK-7: CaveBatCrawler Katman 1-2'de secilmeli.");
            Assert.That(sawScorpion, Is.True, "AK-7: CrystalCavernScorpion kristal duvar yakininda secilmeli.");
            Assert.That(sawKnight, Is.True, "AK-7: AbyssalSkeletonKnight Katman 3'te secilmeli.");
            Assert.That(sawSerpent, Is.True, "AK-7: MagmaSerpent Katman 4/lav yakininda secilmeli.");

            Assert.That(
                CaveEcologyAndSpawnSystem.GetVariantCapabilities(CaveCreatureVariantKind.MagmaSerpent)
                    .HasFlag(AnatomyCapabilityFlags.CapLavaImmunity),
                Is.True);
            Assert.That(
                CaveEcologyAndSpawnSystem.GetVariantCapabilities(CaveCreatureVariantKind.GiantCaveSpider)
                    .HasFlag(AnatomyCapabilityFlags.WallClimbable),
                Is.True);
            Assert.That(CaveEcologyAndSpawnSystem.IsCeilingAmbushVariant(CaveCreatureVariantKind.GiantCaveSpider), Is.True);
            Assert.That(CaveEcologyAndSpawnSystem.IsCeilingAmbushVariant(CaveCreatureVariantKind.CaveBatCrawler), Is.True);

            // 3) Tavan/taban galerisi algilama ve tavan pususundan >= 1.2 sn once telgraf tetiklenmesi
            uint[] caveChunk = new uint[16 * 16 * 32];
            uint stoneRaw = VoxelBitUtility.PackStandard(
                MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    caveChunk[(10 << 8) | (z << 4) | x] = stoneRaw; // Taban y=10
                    caveChunk[(18 << 8) | (z << 4) | x] = stoneRaw; // Tavan y=18
                }
            }

            bool foundGallery = CaveEcologyAndSpawnSystem.TrySampleCaveFloorAndCeiling(
                caveChunk, lx: 8, refY: 14, lz: 8, maxY: 32, out int floorY, out int ceilingY);
            Assert.That(foundGallery, Is.True);
            Assert.That(floorY, Is.EqualTo(10));
            Assert.That(ceilingY, Is.EqualTo(18));
            Assert.That(ceilingY - floorY, Is.GreaterThanOrEqualTo(CaveEcologyAndSpawnSystem.MinCeilingGalleryHeightForAmbush));
            Assert.That(CaveEcologyAndSpawnSystem.ComputeCeilingHangWorldY(ceilingY), Is.EqualTo(18.0f - 0.65f).Within(0.001f));

            // Uzak mesafede (16m > 11.5m) telgraf tetiklenmez
            Assert.That(
                CaveEcologyAndSpawnSystem.ShouldTriggerCaveAmbushTelegraph(
                    horizDistSq: 16.0f * 16.0f,
                    isSleepingOnCeiling: true,
                    currentTimer: 0f,
                    out float farTelegraphSec),
                Is.False);
            Assert.That(farTelegraphSec, Is.EqualTo(0f));

            // Yaklasma mesafesinde (8m <= 11.5m) tavan pususundan >= 1.2 sn once telgraf bayragi ve suresi tetiklenir
            bool triggered = CaveEcologyAndSpawnSystem.ShouldTriggerCaveAmbushTelegraph(
                horizDistSq: 8.0f * 8.0f,
                isSleepingOnCeiling: true,
                currentTimer: 0f,
                out float telegraphDurationSec);
            Assert.That(triggered, Is.True, "AK-7: Oyuncu yaklastiginda tavan pususu telgrafi tetiklenmelidir.");
            Assert.That(telegraphDurationSec, Is.GreaterThanOrEqualTo(CaveEcologyAndSpawnSystem.MinAmbushTelegraphDurationSeconds),
                "AK-7: Telgraf suresi tavan pususundan >= 1.2 sn once olmalidir.");

            // Aktif telgraf suresi devam ederken yeniden tetiklenmez
            Assert.That(
                CaveEcologyAndSpawnSystem.ShouldTriggerCaveAmbushTelegraph(
                    horizDistSq: 4.0f * 4.0f,
                    isSleepingOnCeiling: true,
                    currentTimer: 0.75f,
                    out _),
                Is.False);
        }

        [Test]
        public void CaveRockComposition_IsPredominantlyNaturalStoneWithDepthHardeningAndCoherentVeins()
        {
            long totalUndergroundSolids = 0;
            long naturalHostRockCount = 0;
            long oreCount = 0;

            long shallowSolids = 0;
            long shallowStoneCobble = 0;
            long shallowBasalt = 0;

            long deepSolids = 0;
            long deepBasaltObsidian = 0;

            uint[] voxels = new uint[16 * 16 * StratocraftFloatingIslandWorldGen.MaxEditableVoxelY];
            ushort[] fluids = new ushort[voxels.Length];

            for (int cz = -3; cz <= 3; cz++)
            {
                for (int cx = -3; cx <= 3; cx++)
                {
                    // [F12] showcase ve baslangic reaksiyon odalarini haric tut
                    if (cx >= -1 && cx <= 0 && cz >= -1 && cz <= 1)
                    {
                        continue;
                    }

                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(cx, cz, voxels, fluids);
                    int maxChunkY = voxels.Length >> 8;

                    for (int lz = 0; lz < 16; lz++)
                    {
                        int wz = cz * 16 + lz;
                        for (int lx = 0; lx < 16; lx++)
                        {
                            int wx = cx * 16 + lx;
                            float topH = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                            int maxUndergroundY = Math.Min(82, Math.Min(maxChunkY - 1, (int)MathF.Floor(topH) - 4));
                            if (maxUndergroundY < 12)
                            {
                                continue;
                            }

                            int col = (lz << 4) | lx;
                            for (int y = 12; y <= maxUndergroundY; y++)
                            {
                                uint raw = voxels[(y << 8) | col];
                                if (raw == 0u || VoxelBitUtility.GetDensityOrLiquid(raw) < StratocraftConstants.SdfIsoLevel)
                                {
                                    continue;
                                }

                                byte matId = VoxelBitUtility.GetMaterialId(raw);
                                totalUndergroundSolids++;

                                if (matId == MaterialIdCatalog.Stone ||
                                    matId == MaterialIdCatalog.Cobblestone ||
                                    matId == MaterialIdCatalog.Stalagnate ||
                                    matId == MaterialIdCatalog.Marble ||
                                    matId == MaterialIdCatalog.Basalt ||
                                    matId == MaterialIdCatalog.NaturalObsidian ||
                                    matId == MaterialIdCatalog.Bedrock ||
                                    matId == MaterialIdCatalog.Dirt)
                                {
                                    naturalHostRockCount++;
                                }

                                if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId))
                                {
                                    oreCount++;
                                }

                                if (y >= 66 && y <= 78)
                                {
                                    shallowSolids++;
                                    if (matId == MaterialIdCatalog.Stone || matId == MaterialIdCatalog.Cobblestone)
                                    {
                                        shallowStoneCobble++;
                                    }
                                    else if (matId == MaterialIdCatalog.Basalt)
                                    {
                                        shallowBasalt++;
                                    }
                                }
                                else if (y >= 20 && y <= 34)
                                {
                                    deepSolids++;
                                    if (matId == MaterialIdCatalog.Basalt || matId == MaterialIdCatalog.NaturalObsidian)
                                    {
                                        deepBasaltObsidian++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(totalUndergroundSolids, Is.GreaterThan(50000));
            Assert.That(shallowSolids, Is.GreaterThan(5000));
            Assert.That(deepSolids, Is.GreaterThan(5000));

            double hostRockPct = (naturalHostRockCount * 100.0) / totalUndergroundSolids;
            double orePct = (oreCount * 100.0) / totalUndergroundSolids;
            double shallowStonePct = (shallowStoneCobble * 100.0) / shallowSolids;
            double shallowBasaltPct = (shallowBasalt * 100.0) / shallowSolids;
            double deepBasaltObsidianPct = (deepBasaltObsidian * 100.0) / deepSolids;

            Assert.That(hostRockPct, Is.GreaterThanOrEqualTo(88.0),
                $"Yeralti kati voksellerinin ana kayac orani ({hostRockPct:F2}%) >= %88.0 olmalidir.");
            Assert.That(orePct, Is.InRange(1.0, 10.0),
                $"Yeralti cevher orani ({orePct:F2}%) [%1.0, %10.0] araliginda kalmalidir.");
            Assert.That(shallowStonePct, Is.GreaterThan(shallowBasaltPct),
                $"Sig yeraltinda Stone+Cobblestone ({shallowStonePct:F2}%) Basalt'tan ({shallowBasaltPct:F2}%) yuksek olmalidir.");
            Assert.That(deepBasaltObsidianPct, Is.GreaterThan(65.0),
                $"Derin yeraltinda Basalt+NaturalObsidian orani ({deepBasaltObsidianPct:F2}%) > %65 olmalidir.");
        }

        [Test]
        public void CaveWaterPools_HaveZeroFloatingClayPedestalsAndRestInSolidNaturalRock()
        {
            var lakeChunks = new List<(int cx, int cz)>();
            uint[] voxels = new uint[16 * 16 * StratocraftFloatingIslandWorldGen.MaxEditableVoxelY];
            ushort[] fluids = new ushort[voxels.Length];

            // AK-5 ile ayni deterministik gol odasi taramasindan gercek uretilen gol chunk'larini topla
            for (int cz = -10; cz <= 10 && lakeChunks.Count < 6; cz++)
            {
                for (int cx = -10; cx <= 10 && lakeChunks.Count < 6; cx++)
                {
                    int wx = cx * 24 + 10;
                    int wz = cz * 24 + 10;
                    if (wx * wx + wz * wz < 32 * 32)
                    {
                        continue;
                    }

                    bool chamberHasLake = false;
                    for (int y = 46; y <= 73; y++)
                    {
                        if (StratocraftFloatingIslandWorldGen.TryEvaluateCaveChamberWaterLake(
                                wx, y, wz, swell01: 0.68f, provinceNoise: 0.65f,
                                out bool inLakeWater, out _) &&
                            inLakeWater)
                        {
                            chamberHasLake = true;
                            break;
                        }
                    }

                    if (!chamberHasLake)
                    {
                        continue;
                    }

                    int chunkX = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
                    int chunkZ = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
                    if (lakeChunks.Contains((chunkX, chunkZ)))
                    {
                        continue;
                    }

                    StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(chunkX, chunkZ, voxels, fluids);
                    for (int y = 46; y <= 73; y++)
                    {
                        int yBase = y << 8;
                        bool found = false;
                        for (int i = 0; i < 256; i++)
                        {
                            FluidCell16 fc = new FluidCell16(fluids[yBase | i]);
                            if (fc.Type == FluidType.Water && fc.Mass > 0)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found)
                        {
                            lakeChunks.Add((chunkX, chunkZ));
                            break;
                        }
                    }
                }
            }

            Assert.That(lakeChunks.Count, Is.GreaterThanOrEqualTo(1),
                "Test edilecek en az 1 gercek yeralti gol chunk'i bulunmalidir.");

            int clayCountInCaveLakeBand = 0;
            int verifiedWaterCells = 0;

            for (int ci = 0; ci < lakeChunks.Count; ci++)
            {
                (int chunkX, int chunkZ) = lakeChunks[ci];
                StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunkInto(chunkX, chunkZ, voxels, fluids);
                int maxChunkY = voxels.Length >> 8;

                for (int lz = 0; lz < 16; lz++)
                {
                    int wz = chunkZ * 16 + lz;
                    for (int lx = 0; lx < 16; lx++)
                    {
                        int wx = chunkX * 16 + lx;
                        if (wx * wx + wz * wz < 28 * 28)
                        {
                            continue;
                        }

                        float topH = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                        int maxCheckY = Math.Min(73, Math.Min(maxChunkY - 1, (int)MathF.Floor(topH - 8.0f)));
                        int col = (lz << 4) | lx;

                        for (int y = 46; y <= maxCheckY; y++)
                        {
                            uint vRaw = voxels[(y << 8) | col];
                            if (vRaw != 0u && VoxelBitUtility.GetMaterialId(vRaw) == MaterialIdCatalog.Clay)
                            {
                                clayCountInCaveLakeBand++;
                            }

                            FluidCell16 fc = new FluidCell16(fluids[(y << 8) | col]);
                            if (fc.Type != FluidType.Water || fc.Mass == 0)
                            {
                                continue;
                            }

                            verifiedWaterCells++;

                            // 1) Hemen altinda (y - 1) kati ve Clay olmayan dogal kaya veya bagli su havuzu olmali
                            int belowIdx = ((y - 1) << 8) | col;
                            FluidCell16 belowFluid = new FluidCell16(fluids[belowIdx]);
                            if (belowFluid.Type != FluidType.Water || belowFluid.Mass == 0)
                            {
                                uint belowVox = voxels[belowIdx];
                                Assert.That(belowVox, Is.Not.EqualTo(0u),
                                    $"({chunkX},{chunkZ}) y={y} su hucresi altinda bosluk tespit edildi.");
                                Assert.That(VoxelBitUtility.GetDensityOrLiquid(belowVox), Is.GreaterThanOrEqualTo(StratocraftConstants.SdfIsoLevel),
                                    $"({chunkX},{chunkZ}) y={y} su hucresi altindaki kaya SdfIsoLevel altinda.");
                                Assert.That(VoxelBitUtility.GetMaterialId(belowVox), Is.Not.EqualTo(MaterialIdCatalog.Clay),
                                    $"({chunkX},{chunkZ}) y={y} su hucresi altinda Clay kaidesi olmamalidir.");
                            }

                            // 2) Yatay dis kenarlarda su yoksa sizdirmaz dogal kaya bulunmali
                            int yBase = y << 8;
                            for (int dir = 0; dir < 4; dir++)
                            {
                                int nx = dir == 0 ? lx - 1 : (dir == 1 ? lx + 1 : lx);
                                int nz = dir == 2 ? lz - 1 : (dir == 3 ? lz + 1 : lz);
                                if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16)
                                {
                                    continue;
                                }

                                int nIdx = yBase | (nz << 4) | nx;
                                FluidCell16 nFluid = new FluidCell16(fluids[nIdx]);
                                if (nFluid.Type == FluidType.Water && nFluid.Mass > 0)
                                {
                                    continue;
                                }

                                uint nVox = voxels[nIdx];
                                Assert.That(nVox, Is.Not.EqualTo(0u),
                                    $"({chunkX},{chunkZ}) y={y} gol dis kenarinda sizinti acikligi tespit edildi.");
                                Assert.That(VoxelBitUtility.GetDensityOrLiquid(nVox), Is.GreaterThanOrEqualTo(StratocraftConstants.SdfIsoLevel),
                                    $"({chunkX},{chunkZ}) y={y} gol dis kenar kayasi sizdirmaz (Density >= SdfIsoLevel) olmalidir.");
                                Assert.That(VoxelBitUtility.GetMaterialId(nVox), Is.Not.EqualTo(MaterialIdCatalog.Clay),
                                    $"({chunkX},{chunkZ}) y={y} gol dis kenarinda Clay olmamalidir.");
                            }
                        }
                    }
                }
            }

            Assert.That(clayCountInCaveLakeBand, Is.EqualTo(0),
                $"r >= 28m, y <= 73 (topY - 8 altinda) yeralti gol cevrelerinde Clay sayisi 0 olmalidir (bulunan: {clayCountInCaveLakeBand}).");
            Assert.That(verifiedWaterCells, Is.GreaterThan(0),
                "En az bir gercek yeralti su hucresi dogrulanmalidir.");
        }
    }
}
