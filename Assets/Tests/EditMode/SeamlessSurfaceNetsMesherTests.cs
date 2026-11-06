using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.Voxel.Systems;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.Tests.EditMode
{
    [TestFixture]
    public sealed class SeamlessSurfaceNetsMesherTests
    {
        [Test]
        public void GenerateSeamlessHybridChunkMesh_AdjacentChunks_HaveBitExactWeldedSeamAndClosed3DUnderside()
        {
            var world = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = new uint[65536],
                [(1, 0)] = new uint[65536]
            };

            // 3B hacimsel yuzen ada kesiti: y = 4..10 arasi dolu, y = 0..3 ve dis sinirlar Hava (0u)
            for (int cz = 0; cz <= 0; cz++)
            {
                for (int cx = 0; cx <= 1; cx++)
                {
                    uint[] chunk = world[(cx, cz)];
                    for (int lz = 2; lz <= 13; lz++)
                    {
                        for (int lx = 0; lx < 16; lx++)
                        {
                            int wx = cx * 16 + lx;
                            if (wx < 2 || wx > 29)
                            {
                                continue; // Dis ucurum sinirlari Hava
                            }

                            float topH = 9.4f + MathF.Sin(wx * 0.25f) * 1.1f;
                            float botH = 3.8f + MathF.Cos(lz * 0.30f) * 0.6f;

                            for (int y = 2; y < 14; y++)
                            {
                                float topDiff = topH - y;
                                float botDiff = y - botH;
                                float minDiff = MathF.Min(topDiff, botDiff);
                                int dens = (int)MathF.Round(64f + minDiff * 28f);
                                if (dens < 12)
                                {
                                    continue;
                                }

                                byte mat = y >= topH - 0.9f ? MaterialIdCatalog.Grass : MaterialIdCatalog.Stone;
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
            }

            var trisLeft = new List<FacetedVoxelTriangle>(4096);
            var trisRight = new List<FacetedVoxelTriangle>(4096);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, 16, world, trisLeft);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(1, 0, 16, world, trisRight);

            Assert.That(trisLeft.Count, Is.GreaterThan(100));
            Assert.That(trisRight.Count, Is.GreaterThan(100));

            // 1) 3B Adanin hem ust yuzeyi, hem ALT KABUGU (Normal.Y < -0.3), hem de yan ucurumlari olusmali!
            bool hasTop = false, hasUnderbelly = false, hasWestCliff = false, hasSouthCliff = false;
            foreach (FacetedVoxelTriangle t in trisLeft)
            {
                if (t.Normal.Y > 0.35f) hasTop = true;
                if (t.Normal.Y < -0.35f) hasUnderbelly = true;
                if (t.Normal.X < -0.35f) hasWestCliff = true;
                if (t.Normal.Z < -0.35f) hasSouthCliff = true;
            }

            Assert.That(hasTop, Is.True, "3B adanin ust yuzeyi eksik!");
            Assert.That(hasUnderbelly, Is.True, "3B adanin alt kaya kabugu (underside) eksik!");
            Assert.That(hasWestCliff, Is.True, "3B adanin bati ucurum duvari eksik!");
            Assert.That(hasSouthCliff, Is.True, "3B adanin guney ucurum duvari eksik!");

            // 2) Chunk (0,0) ile Chunk (1,0) birlesim hatti (world X in [15.0 .. 16.0]) uzerindeki tum kose noktalari
            // iki chunk arasinda 0.000000m (bit-exact IEEE-754) eslesmelidir!
            var seamVertsRight = new HashSet<Vector3>();
            Vector3 rightOffset = new Vector3(16f, 0f, 0f);
            foreach (FacetedVoxelTriangle t in trisRight)
            {
                Vector3 w0 = t.V0 + rightOffset;
                Vector3 w1 = t.V1 + rightOffset;
                Vector3 w2 = t.V2 + rightOffset;
                if (w0.X >= 15.0f && w0.X <= 16.0f) seamVertsRight.Add(w0);
                if (w1.X >= 15.0f && w1.X <= 16.0f) seamVertsRight.Add(w1);
                if (w2.X >= 15.0f && w2.X <= 16.0f) seamVertsRight.Add(w2);
            }

            Assert.That(seamVertsRight.Count, Is.GreaterThan(10), "Sinir bolgesinde ortak dual vertex bulunamadi.");

            int matchedSeamVertices = 0;
            foreach (FacetedVoxelTriangle t in trisLeft)
            {
                if (t.V0.X >= 15.0f && t.V0.X <= 16.0f)
                {
                    Assert.That(seamVertsRight.Contains(t.V0), Is.True, $"Seam catlagi tespit edildi: {t.V0}");
                    matchedSeamVertices++;
                }
                if (t.V1.X >= 15.0f && t.V1.X <= 16.0f)
                {
                    Assert.That(seamVertsRight.Contains(t.V1), Is.True, $"Seam catlagi tespit edildi: {t.V1}");
                    matchedSeamVertices++;
                }
                if (t.V2.X >= 15.0f && t.V2.X <= 16.0f)
                {
                    Assert.That(seamVertsRight.Contains(t.V2), Is.True, $"Seam catlagi tespit edildi: {t.V2}");
                    matchedSeamVertices++;
                }
            }

            Assert.That(matchedSeamVertices, Is.GreaterThan(20));
        }

        [Test]
        public void SubChunk16x16x16PartialMeshing_MatchesFullChunkMesh_AndDistantHorizonLod64Is8Bytes()
        {
            var world = new Dictionary<(int cx, int cz), uint[]>
            {
                [(0, 0)] = new uint[16 * 16 * 64]
            };

            uint[] chunk = world[(0, 0)];
            for (int y = 4; y < 44; y++)
            {
                for (int lz = 2; lz <= 13; lz++)
                {
                    for (int lx = 2; lx <= 13; lx++)
                    {
                        byte mat = y >= 40 ? MaterialIdCatalog.Grass : MaterialIdCatalog.Stone;
                        chunk[(y << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                            mat, 108, shapeId: 0, rot24: 0, BlokKokenDurumu.DogalKutle, dmg4: 0);
                    }
                }
            }

            var fullTris = new List<FacetedVoxelTriangle>(4096);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, 64, world, fullTris, minY: 0);

            var subTris = new List<FacetedVoxelTriangle>(4096);
            var scratch = new List<FacetedVoxelTriangle>(1024);
            for (int subY = 0; subY < 4; subY++)
            {
                scratch.Clear();
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    0, 0, (subY + 1) * 16, world, scratch, minY: subY * 16);
                subTris.AddRange(scratch);
            }

            Assert.That(subTris.Count, Is.EqualTo(fullTris.Count), "16x16x16 Sub-Chunk parcali orme tam chunk ormesiyle birebir ayni sayida ucgen uretmelidir!");

            // DistantHorizonLodSystem ColumnLod64 (8 Byte) ve 64m x 64m kabuk testi
            ColumnLod64 col = DistantHorizonLodSystem.SampleAnalyticalColumnLod64(0f, 0f);
            Assert.That(col.IsValidLand, Is.True);
            Assert.That(col.TopY, Is.GreaterThan(80f));

            var lodSys = new DistantHorizonLodSystem();
            var shellTris = new List<FacetedVoxelTriangle>(1024);
            lodSys.BuildMacroTileHorizonShellMesh(0, 0, shellTris);
            Assert.That(shellTris.Count, Is.GreaterThan(100), "64m x 64m Distant Horizon kabuk mesh'i ucgen uretmelidir!");
        }

        [Test]
        public void RealWorldGenAdjacentChunks_SeamVertices_WeldWithZeroDiscrepancy()
        {
            var world = new Dictionary<(int cx, int cz), uint[]>();
            for (int cz = -1; cz <= 1; cz++)
            {
                for (int cx = -1; cx <= 2; cx++)
                {
                    var (vox, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    world[(cx, cz)] = vox;
                }
            }

            var trisLeft = new List<FacetedVoxelTriangle>(8192);
            var trisRight = new List<FacetedVoxelTriangle>(8192);

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(0, 0, 128, world, trisLeft, minY: 0);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(1, 0, 128, world, trisRight, minY: 0);

            Assert.That(trisLeft.Count, Is.GreaterThan(500));
            Assert.That(trisRight.Count, Is.GreaterThan(500));

            // Chunk (0,0) ile Chunk (1,0) gercek Surface Nets birlesim hatti: X in (15.01..15.99)
            // (X=15.0f veya X=16.0f tamsayilari chunk icindeki mimari blok kenarlaridir, seam dual kosesi degildir)
            var seamVertsRight = new HashSet<Vector3>();
            Vector3 rightOffset = new Vector3(16f, 0f, 0f);
            foreach (FacetedVoxelTriangle t in trisRight)
            {
                Vector3 w0 = t.V0 + rightOffset;
                Vector3 w1 = t.V1 + rightOffset;
                Vector3 w2 = t.V2 + rightOffset;
                if (w0.X > 15.01f && w0.X < 15.99f) seamVertsRight.Add(w0);
                if (w1.X > 15.01f && w1.X < 15.99f) seamVertsRight.Add(w1);
                if (w2.X > 15.01f && w2.X < 15.99f) seamVertsRight.Add(w2);
            }

            int checkedSeam = 0;
            int missingSeam = 0;
            var missingList = new List<string>();
            foreach (FacetedVoxelTriangle t in trisLeft)
            {
                bool b0 = t.V0.X > 15.01f && t.V0.X < 15.99f;
                bool b1 = t.V1.X > 15.01f && t.V1.X < 15.99f;
                bool b2 = t.V2.X > 15.01f && t.V2.X < 15.99f;
                if (b0 || b1 || b2)
                {
                    if (b0) checkedSeam++;
                    if (b1) checkedSeam++;
                    if (b2) checkedSeam++;

                    bool m0 = b0 && !seamVertsRight.Contains(t.V0);
                    bool m1 = b1 && !seamVertsRight.Contains(t.V1);
                    bool m2 = b2 && !seamVertsRight.Contains(t.V2);

                    if (m0 || m1 || m2)
                    {
                        if (m0) missingSeam++;
                        if (m1) missingSeam++;
                        if (m2) missingSeam++;

                        if (missingList.Count < 50)
                        {
                            missingList.Add($"Tri: V0={t.V0}, V1={t.V1}, V2={t.V2}, Norm={t.Normal}, Mat={t.MaterialId}");
                        }
                    }
                }
            }

            Console.WriteLine($"[Test Result] checkedSeam={checkedSeam}, missingSeam={missingSeam}, seamVertsRight={seamVertsRight.Count}");
            foreach (var m in missingList)
            {
                Console.WriteLine($"  Mismatch {m}");
            }
            Assert.That(checkedSeam, Is.GreaterThan(50), "Sinirda incelenen kose bulunamadi.");
            Assert.That(missingSeam, Is.EqualTo(0), $"Sinirda {missingSeam}/{checkedSeam} eslesmeyen kose tespit edildi!");
        }

        [Test]
        public void DynamicStreamingCornerIntersection_AtXNeg160_ZNeg64_WeldsWithZeroSeamInBothFullAndSubChunkModes()
        {
            // Kullanicinin ekran goruntulerindeki (X = -160m, Z = -64m, Y = 95.7m -> cx = -11..-10, cz = -5..-4)
            // 4-chunk kose kesisimi ve 8-komsu apronu testi:
            var world = new Dictionary<(int cx, int cz), uint[]>();
            for (int cz = -6; cz <= -3; cz++)
            {
                for (int cx = -12; cx <= -8; cx++)
                {
                    var (vox, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(cx, cz);
                    world[(cx, cz)] = vox;
                }
            }

            var trisNW = new List<FacetedVoxelTriangle>(8192); // (-11, -4)
            var trisNE = new List<FacetedVoxelTriangle>(8192); // (-10, -4)
            var trisSW = new List<FacetedVoxelTriangle>(8192); // (-11, -5)
            var trisSE = new List<FacetedVoxelTriangle>(8192); // (-10, -5) - SubChunk parcali orme ile!

            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(-11, -4, 160, world, trisNW, minY: 0);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(-10, -4, 160, world, trisNE, minY: 0);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(-11, -5, 160, world, trisSW, minY: 0);

            var subScratch = new List<FacetedVoxelTriangle>(1024);
            for (int subY = 0; subY < 10; subY++)
            {
                subScratch.Clear();
                HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(
                    -10, -5, (subY + 1) * 16, world, subScratch, minY: subY * 16);
                trisSE.AddRange(subScratch);
            }

            var trisSEFull = new List<FacetedVoxelTriangle>(8192);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(-10, -5, 160, world, trisSEFull, minY: 0);
            Assert.That(trisSE.Count, Is.EqualTo(trisSEFull.Count), "(-10, -5) Sub-Chunk parcali orme tam chunk ormesiyle birebir ayni ucgen sayisini uretmelidir!");

            // 4 komsu chunk'in ((-11,-5), (-10,-5), (-11,-4), (-10,-4)) birlesik mesh'inde
            // X = -160m ve Z = -64m sinir bolgesinde (-164.. -156, Y >= 75, -68.. -60) hicbir ACIK KENAR (Open Edge / Catlak) olmamalidir!
            var allWorldTris = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 norm, byte mat, string src)>(16384);
            void AppendWorldTris(List<FacetedVoxelTriangle> list, int cx, int cz, string label)
            {
                Vector3 off = new Vector3(cx * 16f, 0f, cz * 16f);
                foreach (var t in list)
                {
                    allWorldTris.Add((t.V0 + off, t.V1 + off, t.V2 + off, t.Normal, t.MaterialId, label));
                }
            }

            AppendWorldTris(trisNW, -11, -4, "NW(-11,-4)");
            AppendWorldTris(trisNE, -10, -4, "NE(-10,-4)");
            AppendWorldTris(trisSW, -11, -5, "SW(-11,-5)");
            AppendWorldTris(trisSE, -10, -5, "SE(-10,-5)");

            var undirectedEdgeCounts = new Dictionary<(Vector3 u, Vector3 v), int>();
            var edgeOwnerSample = new Dictionary<(Vector3 u, Vector3 v), string>();

            static (Vector3 u, Vector3 v) MakeCanonicalEdge(Vector3 p, Vector3 q)
            {
                if (p.X != q.X) return p.X < q.X ? (p, q) : (q, p);
                if (p.Y != q.Y) return p.Y < q.Y ? (p, q) : (q, p);
                return p.Z <= q.Z ? (p, q) : (q, p);
            }

            foreach (var tri in allWorldTris)
            {
                if (tri.a.Y < 72f && tri.b.Y < 72f && tri.c.Y < 72f) continue;
                var e0 = MakeCanonicalEdge(tri.a, tri.b);
                var e1 = MakeCanonicalEdge(tri.b, tri.c);
                var e2 = MakeCanonicalEdge(tri.c, tri.a);

                undirectedEdgeCounts[e0] = undirectedEdgeCounts.TryGetValue(e0, out int c0) ? c0 + 1 : 1;
                undirectedEdgeCounts[e1] = undirectedEdgeCounts.TryGetValue(e1, out int c1) ? c1 + 1 : 1;
                undirectedEdgeCounts[e2] = undirectedEdgeCounts.TryGetValue(e2, out int c2) ? c2 + 1 : 1;

                edgeOwnerSample[e0] = $"{tri.src} mat={tri.mat} norm={tri.norm}";
                edgeOwnerSample[e1] = $"{tri.src} mat={tri.mat} norm={tri.norm}";
                edgeOwnerSample[e2] = $"{tri.src} mat={tri.mat} norm={tri.norm}";
            }

            int seamEdgesChecked = 0;
            int openCrackEdges = 0;
            foreach (var kv in undirectedEdgeCounts)
            {
                Vector3 mid = (kv.Key.u + kv.Key.v) * 0.5f;
                // 4 chunk'in ic kesisim bolgesi: X in [-168..-152], Z in [-72..-56], Y >= 75
                if (mid.Y >= 75f && mid.X >= -168f && mid.X <= -152f && mid.Z >= -72f && mid.Z <= -56f)
                {
                    bool nearSeam = (mid.X >= -161.2f && mid.X <= -158.8f) || (mid.Z >= -65.2f && mid.Z <= -62.8f);
                    if (!nearSeam) continue;
                    seamEdgesChecked++;
                    if (kv.Value == 1)
                    {
                        if (openCrackEdges < 20)
                        {
                            Console.WriteLine($"[OPEN CRACK EDGE] u={kv.Key.u} v={kv.Key.v} mid={mid} owner={edgeOwnerSample[kv.Key]}");
                        }
                        openCrackEdges++;
                    }
                }
            }

            Console.WriteLine($"[Seam Manifold Check] seamEdgesChecked={seamEdgesChecked}, openCrackEdges={openCrackEdges}");
            Assert.That(seamEdgesChecked, Is.GreaterThan(100));
            Assert.That(openCrackEdges, Is.EqualTo(0), $"X=-160m / Z=-64m sinirinda {openCrackEdges} adet acik yirtik kenar (catlak) tespit edildi!");

            // Ayrica Gorsel 2'deki (X = -174m, Z = -63m -> X = -176m, Z = -64m, yani cx = -12..-11, cz = -5..-4) kesisimini de dogrula:
            for (int cz = -6; cz <= -3; cz++)
            {
                var (vox, _) = StratocraftFloatingIslandWorldGen.PopulateVolumetricFloatingIslandChunk(-13, cz);
                world[(-13, cz)] = vox;
            }

            var trisNW2 = new List<FacetedVoxelTriangle>(8192); // (-12, -4)
            var trisSW2 = new List<FacetedVoxelTriangle>(8192); // (-12, -5)
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(-12, -4, 160, world, trisNW2, minY: 0);
            HybridSurfaceNetsAndShapeMesher.GenerateSeamlessHybridChunkMesh(-12, -5, 160, world, trisSW2, minY: 0);

            AppendWorldTris(trisNW2, -12, -4, "NW2(-12,-4)");
            AppendWorldTris(trisSW2, -12, -5, "SW2(-12,-5)");

            undirectedEdgeCounts.Clear();
            foreach (var tri in allWorldTris)
            {
                if (tri.a.Y < 72f && tri.b.Y < 72f && tri.c.Y < 72f) continue;
                var e0 = MakeCanonicalEdge(tri.a, tri.b);
                var e1 = MakeCanonicalEdge(tri.b, tri.c);
                var e2 = MakeCanonicalEdge(tri.c, tri.a);
                undirectedEdgeCounts[e0] = undirectedEdgeCounts.TryGetValue(e0, out int c0) ? c0 + 1 : 1;
                undirectedEdgeCounts[e1] = undirectedEdgeCounts.TryGetValue(e1, out int c1) ? c1 + 1 : 1;
                undirectedEdgeCounts[e2] = undirectedEdgeCounts.TryGetValue(e2, out int c2) ? c2 + 1 : 1;
            }

            int seam2EdgesChecked = 0, openCrack2Edges = 0;
            foreach (var kv in undirectedEdgeCounts)
            {
                Vector3 mid = (kv.Key.u + kv.Key.v) * 0.5f;
                if (mid.Y >= 75f && mid.X >= -184f && mid.X <= -168f && mid.Z >= -72f && mid.Z <= -56f)
                {
                    bool nearSeam = (mid.X >= -177.2f && mid.X <= -174.8f) || (mid.Z >= -65.2f && mid.Z <= -62.8f);
                    if (!nearSeam) continue;
                    seam2EdgesChecked++;
                    if (kv.Value == 1) openCrack2Edges++;
                }
            }

            Assert.That(seam2EdgesChecked, Is.GreaterThan(100));
            Assert.That(openCrack2Edges, Is.EqualTo(0), $"X=-176m / Z=-64m sinirinda {openCrack2Edges} adet acik yirtik kenar (catlak) tespit edildi!");
        }
    }
}
