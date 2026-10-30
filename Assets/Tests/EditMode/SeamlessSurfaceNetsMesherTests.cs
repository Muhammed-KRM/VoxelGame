using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

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
    }
}
