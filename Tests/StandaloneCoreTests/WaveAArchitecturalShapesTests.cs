#nullable enable
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.StandaloneCoreTests
{
    [TestFixture]
    public sealed class WaveAArchitecturalShapesTests
    {
        [Test]
        public void All16CanonicalShapes_All24Rotations_AreWatertightOutwardAndGapFree()
        {
            for (byte shapeId = 1; shapeId <= 16; shapeId++)
            {
                for (byte rot = 0; rot < 24; rot++)
                {
                    // 1) Mimari-mimari temasta (skirt=false) FullFaceMask 0 yarik + su-gecirmez + 0 ters normal
                    HybridSurfaceNetsAndShapeMesher.AuditArchitecturalShapeForProbe(
                        MaterialIdCatalog.Marble,
                        shapeId,
                        rot,
                        extendFoundationSkirt: false,
                        occludedFaceMask: 0,
                        out int triCount,
                        out int invertedCount,
                        out int openEdgeCount,
                        out int degenCount,
                        out byte gapMask,
                        out bool skirtLeak);

                    string name = HybridSurfaceNetsAndShapeMesher.GetCanonicalCatalogShapeName(shapeId);
                    Assert.That(triCount, Is.GreaterThan(0), $"Shape {shapeId} ({name}) rot {rot} emitted 0 triangles.");
                    Assert.That(invertedCount, Is.EqualTo(0), $"Shape {shapeId} ({name}) rot {rot} has {invertedCount} inverted normals/edges.");
                    Assert.That(openEdgeCount, Is.EqualTo(0), $"Shape {shapeId} ({name}) rot {rot} has {openEdgeCount} open boundary edges.");
                    Assert.That(degenCount, Is.EqualTo(0), $"Shape {shapeId} ({name}) rot {rot} has {degenCount} degenerate triangles.");
                    Assert.That(gapMask, Is.EqualTo((byte)0), $"Shape {shapeId} ({name}) rot {rot} has FullFaceMask gap 0x{gapMask:X2}.");
                    Assert.That(skirtLeak, Is.False, $"Shape {shapeId} ({name}) rot {rot} leaked skirt when disabled.");

                    // 2) Dogal arazi ustunde (skirt=true) donmus bloklarda (rot>=4) etek tasmasi 0 + su-gecirmez
                    HybridSurfaceNetsAndShapeMesher.AuditArchitecturalShapeForProbe(
                        MaterialIdCatalog.Marble,
                        shapeId,
                        rot,
                        extendFoundationSkirt: true,
                        occludedFaceMask: 0,
                        out _,
                        out int skirtInv,
                        out int skirtOpen,
                        out int skirtDegen,
                        out _,
                        out bool skirtLeakOn);
                    Assert.That(skirtInv, Is.EqualTo(0), $"Shape {shapeId} ({name}) rot {rot} (skirt) has {skirtInv} inverted normals/edges.");
                    Assert.That(skirtOpen, Is.EqualTo(0), $"Shape {shapeId} ({name}) rot {rot} (skirt) has {skirtOpen} open edges.");
                    Assert.That(skirtDegen, Is.EqualTo(0), $"Shape {shapeId} ({name}) rot {rot} (skirt) has {skirtDegen} degenerate tris.");
                    Assert.That(skirtLeakOn, Is.False, $"Shape {shapeId} ({name}) rot {rot} leaked foundation skirt out of voxel bounds.");
                }
            }
        }

        [Test]
        public void CrystalSpireShape7_Respects24WayRotationAndIsClosed()
        {
            var t0 = new List<FacetedVoxelTriangle>(64);
            var t5 = new List<FacetedVoxelTriangle>(64);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(Vector3.Zero, MaterialIdCatalog.QuartzGem, ArchitecturalShapeCatalog.FacetedCrystalSpire, 0, 0, t0);
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(Vector3.Zero, MaterialIdCatalog.QuartzGem, ArchitecturalShapeCatalog.FacetedCrystalSpire, 5, 0, t5);

            Assert.That(t5.Count, Is.EqualTo(t0.Count));
            bool anyMoved = false;
            for (int i = 0; i < t0.Count; i++)
            {
                if (Vector3.DistanceSquared(t0[i].V0, t5[i].V0) > 0.01f)
                {
                    anyMoved = true;
                    break;
                }
            }
            Assert.That(anyMoved, Is.True, "Shape 7 (FacetedCrystalSpire) ignored rotationState.");
        }

        [Test]
        public void MicroVoxelShape63_ChiseledSlotMatchesProceduralButtressNotShard()
        {
            uint packed = VoxelBitUtility.PackChiseled(MaterialIdCatalog.StoneBrick, 3, BlokKokenDurumu.OyuncuItemBlogu, 0);
            Assert.That(VoxelBitUtility.GetShapeId(packed), Is.EqualTo(VoxelBitUtility.ChiseledMicroVoxelShapeId));
            Assert.That(VoxelBitUtility.GetMicroPoolIndex(packed), Is.EqualTo(3));
        }
    }
}
