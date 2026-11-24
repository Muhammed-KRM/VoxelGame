#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

#if !UNITY_5_3_OR_NEWER
using System.Text.Json;
#endif

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// Devir #086: "Kit-Stamp Voxel Meshing" — ShapeID 21..62 arasindaki 42 adet 3B mimari ve ic mekan sekli icin
    /// hem diskteki gercek UV-free COLOR_0 .glb modellerini (Assets/Art/Generated/Structures_Modular/*.glb) okuyan
    /// hem de cok-malzemeli 3B silindirik kutuk, karkas, oluklu kiremit/mertek cati, pencereli kapi, genis merdiven ve
    /// 3B mobilya (masa, sandalye, yatak, kitaplik, somine, tezgah, sandik, fici, taht, ors, silahlik, kuyu, cit)
    /// geometrilerini bir kez CPU belleginde derleyip tekil voksel chunk mesh'ine 0 B GC ile gomen sablon bankasi.
    /// </summary>
    public static class GlbVoxelKitTemplateBank
    {
        public readonly struct LocalKitTriangle
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;
            public readonly byte OverrideMaterialId; // 0 ise vokselin kendi MaterialId degeri kullanilir
            public readonly byte OverrideTemp;       // 0 ise materyalin varsayilan sicakligi kullanilir
            public readonly byte FacetShade;
            public readonly bool AllowSkirtDrop;

            public LocalKitTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                byte overrideMaterialId,
                byte facetShade,
                byte overrideTemp = 0,
                bool allowSkirtDrop = false)
            {
                A = a;
                B = b;
                C = c;
                OverrideMaterialId = overrideMaterialId;
                OverrideTemp = overrideTemp;
                FacetShade = facetShade;
                AllowSkirtDrop = allowSkirtDrop;
            }
        }

        private const int FirstKitShapeId = 21;
        private const int LastKitShapeId = 62;
        private const int KitCount = LastKitShapeId - FirstKitShapeId + 1;

        private static readonly object SyncRoot = new();
        private static LocalKitTriangle[][]? _templates;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReloadDisabled()
        {
            lock (SyncRoot)
            {
                _templates = null;
            }
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryEmitSculptedKitShape(
            Vector3 cellCenter,
            byte voxelMatId,
            byte shapeId,
            byte rotationState,
            byte baseAo,
            float skirtDrop,
            List<FacetedVoxelTriangle> outTriangles)
        {
            if (shapeId < FirstKitShapeId || shapeId > LastKitShapeId)
            {
                return false;
            }

            // Shape 62 (KitWellAndFence): rot 4..7 kuyu cikrigi, rot 0..3 tarla/bahce citi
            byte effectiveRot = rotationState;
            LocalKitTriangle[] tris;
            if (shapeId == ArchitecturalShapeCatalog.KitWellAndFence && (rotationState & 0x1F) >= 4 && (rotationState & 0x1F) < 8)
            {
                effectiveRot = (byte)(rotationState & 3);
                tris = GetWellWindlassTemplate();
            }
            else
            {
                LocalKitTriangle[][] bank = EnsureTemplatesInitialized();
                tris = bank[shapeId - FirstKitShapeId];
            }

            if (tris.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < tris.Length; i++)
            {
                ref readonly LocalKitTriangle t = ref tris[i];
                Vector3 la = t.A;
                Vector3 lb = t.B;
                Vector3 lc = t.C;
                if (skirtDrop > 0.001f && t.AllowSkirtDrop)
                {
                    if (la.Y <= -0.46f) la.Y -= skirtDrop;
                    if (lb.Y <= -0.46f) lb.Y -= skirtDrop;
                    if (lc.Y <= -0.46f) lc.Y -= skirtDrop;
                }

                Vector3 wa = ArchitecturalShapeCatalog.TransformLocalPoint(la, cellCenter, effectiveRot);
                Vector3 wb = ArchitecturalShapeCatalog.TransformLocalPoint(lb, cellCenter, effectiveRot);
                Vector3 wc = ArchitecturalShapeCatalog.TransformLocalPoint(lc, cellCenter, effectiveRot);

                byte resolvedMat = t.OverrideMaterialId != 0 ? t.OverrideMaterialId : voxelMatId;
                if (t.OverrideTemp != 0)
                {
                    HybridSurfaceNetsAndShapeMesher.AddFacetedTriangleWithTemp(
                        wa, wb, wc, resolvedMat, t.OverrideTemp, t.FacetShade, baseAo, outTriangles);
                }
                else
                {
                    HybridSurfaceNetsAndShapeMesher.AddFacetedTriangle(
                        wa, wb, wc, resolvedMat, t.FacetShade, baseAo, outTriangles);
                }
            }

            return true;
        }

        private static LocalKitTriangle[]? _wellWindlassCached;
        private static LocalKitTriangle[] GetWellWindlassTemplate()
        {
            if (_wellWindlassCached != null) return _wellWindlassCached;
            var b = new List<LocalKitTriangle>(128);
            BuildWellWindlass3D(b);
            _wellWindlassCached = b.ToArray();
            return _wellWindlassCached;
        }

        private static LocalKitTriangle[][] EnsureTemplatesInitialized()
        {
            LocalKitTriangle[][]? existing = _templates;
            if (existing != null)
            {
                return existing;
            }

            lock (SyncRoot)
            {
                if (_templates != null)
                {
                    return _templates;
                }

                string? glbDir = TryLocateModularGlbDirectory();
                var built = new LocalKitTriangle[KitCount][];
                var scratch = new List<LocalKitTriangle>(256);

                for (int s = FirstKitShapeId; s <= LastKitShapeId; s++)
                {
                    scratch.Clear();
                    BuildShapeTemplate((byte)s, glbDir, scratch);
                    built[s - FirstKitShapeId] = scratch.ToArray();
                }

                _templates = built;
                return built;
            }
        }

        private static string? TryLocateModularGlbDirectory()
        {
#if UNITY_5_3_OR_NEWER
            string dataPath = UnityEngine.Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                string p = Path.Combine(dataPath, "Art", "Generated", "Structures_Modular");
                if (Directory.Exists(p)) return p;
            }
#endif
            string cwd = Directory.GetCurrentDirectory();
            string rel = Path.Combine(cwd, "Assets", "Art", "Generated", "Structures_Modular");
            if (Directory.Exists(rel)) return rel;

            string? probe = AppContext.BaseDirectory;
            for (int depth = 0; depth < 7 && !string.IsNullOrEmpty(probe); depth++)
            {
                string cand = Path.Combine(probe, "Assets", "Art", "Generated", "Structures_Modular");
                if (Directory.Exists(cand)) return cand;
                probe = Directory.GetParent(probe)?.FullName;
            }

            return null;
        }

        private static void BuildShapeTemplate(byte shapeId, string? glbDir, List<LocalKitTriangle> dst)
        {
            switch (shapeId)
            {
                // =============================================================
                // A) DUVAR, KUTUK, KARKAS, KESME TAS, TUGLA VE SUTUNLAR (21..33)
                // =============================================================
                case ArchitecturalShapeCatalog.KitChinkedLogWall:
                    // Icerden/disardan sifir isik sizintisi icin ince derz cekirdegi + 2 kat yuvarlak silindirik kutuk!
                    AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.18f, 0.18f, MaterialIdCatalog.StrawChinkingBundle, 168, skirt: true);
                    if (!TryAppendFittedGlb(glbDir, "mod_chinked_log_course_2m.glb", dst, fitX: 1.00f, fitY: 1.00f, fitZ: 0.48f))
                    {
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f, -0.25f, 0.0f, 0.24f, 10, 0, 212, capEnds: true);
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f,  0.25f, 0.0f, 0.24f, 10, 0, 220, capEnds: true);
                    }
                    else
                    {
                        // Yuvarlak kutuk konturunu yan yuzlerde ekstra belirginlestir
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f, -0.25f, 0.0f, 0.245f, 10, 0, 214, capEnds: false);
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f,  0.25f, 0.0f, 0.245f, 10, 0, 222, capEnds: false);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitNotchedLogCorner:
                    // Kesisen ve disari +8cm tasan gercek kertikli (saddle-notch) yuvarlak kose kutuk bogumu!
                    AddBox(dst, -0.34f, 0.34f, -0.50f, 0.50f, -0.34f, 0.34f, MaterialIdCatalog.StrawChinkingBundle, 160, skirt: true);
                    AddHorizontalCylinderX(dst, -0.58f, 0.58f, -0.25f, 0.0f, 0.24f, 10, 0, 214, capEnds: true, endCapMat: MaterialIdCatalog.WoodPlank);
                    AddHorizontalCylinderZ(dst, -0.58f, 0.58f,  0.25f, 0.0f, 0.24f, 10, 0, 222, capEnds: true, endCapMat: MaterialIdCatalog.WoodPlank);
                    break;

                case ArchitecturalShapeCatalog.KitTimberFrameWall:
                    // Gercek Tudors/Ortacag ahsap karkas: Girintili krem siva + disari cikintili koyu mese dikme ve kusaklar!
                    AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.20f, 0.20f, 0, 214, skirt: true);
                    AddBox(dst, -0.50f, 0.50f, -0.50f, -0.36f, -0.27f, 0.27f, MaterialIdCatalog.TreatedTimberBeam, 178, skirt: true);
                    AddBox(dst, -0.50f, 0.50f,  0.36f,  0.50f, -0.27f, 0.27f, MaterialIdCatalog.TreatedTimberBeam, 184);
                    AddBox(dst, -0.50f, -0.36f, -0.36f, 0.36f, -0.26f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 175);
                    AddBox(dst,  0.36f,  0.50f, -0.36f, 0.36f, -0.26f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 175);
                    break;

                case ArchitecturalShapeCatalog.KitTimberBraceWall:
                    AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.20f, 0.20f, 0, 214, skirt: true);
                    AddBox(dst, -0.50f, 0.50f, -0.50f, -0.36f, -0.27f, 0.27f, MaterialIdCatalog.TreatedTimberBeam, 178, skirt: true);
                    AddBox(dst, -0.50f, 0.50f,  0.36f,  0.50f, -0.27f, 0.27f, MaterialIdCatalog.TreatedTimberBeam, 184);
                    AddStrut(dst, new Vector3(-0.42f, -0.40f, 0f), new Vector3(0.42f, 0.40f, 0f), 0.08f, 0.27f, MaterialIdCatalog.TreatedTimberBeam, 172);
                    break;

                case ArchitecturalShapeCatalog.KitTimberPostCorner:
                    AddBox(dst, -0.34f, 0.34f, -0.50f, 0.50f, -0.34f, 0.34f, MaterialIdCatalog.TreatedTimberBeam, 182, skirt: true);
                    AddBox(dst, -0.40f, 0.40f,  0.34f, 0.50f, -0.40f, 0.40f, MaterialIdCatalog.GroovedLogTimber, 196);
                    AddBox(dst, -0.38f, 0.38f, -0.08f, 0.08f, -0.38f, 0.38f, MaterialIdCatalog.IronTieBeamBlock, 152);
                    break;

                case ArchitecturalShapeCatalog.KitAshlarQuoinBlock:
                    AddBox(dst, -0.44f, 0.44f, -0.50f, 0.50f, -0.44f, 0.44f, MaterialIdCatalog.LimeMortarStoneWall, 165, skirt: true);
                    AddBox(dst, -0.50f, 0.50f, -0.48f, -0.02f, -0.50f, 0.38f, 0, 218, skirt: true);
                    AddBox(dst, -0.38f, 0.50f,  0.02f,  0.48f, -0.50f, 0.50f, 0, 226);
                    break;

                case ArchitecturalShapeCatalog.KitRusticatedStoneWall:
                    AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.28f, 0.28f, MaterialIdCatalog.LimeMortarStoneWall, 160, skirt: true);
                    // 2 sira kabartmali kesme/moloz tas bloklari
                    AddBox(dst, -0.48f, -0.02f, -0.48f, -0.02f, -0.36f, 0.36f, 0, 212, skirt: true);
                    AddBox(dst,  0.02f,  0.48f, -0.48f, -0.02f, -0.35f, 0.35f, 0, 204, skirt: true);
                    AddBox(dst, -0.24f,  0.24f,  0.02f,  0.48f, -0.36f, 0.36f, 0, 220);
                    AddBox(dst, -0.50f, -0.28f,  0.02f,  0.48f, -0.34f, 0.34f, 0, 198);
                    AddBox(dst,  0.28f,  0.50f,  0.02f,  0.48f, -0.34f, 0.34f, 0, 208);
                    break;

                case ArchitecturalShapeCatalog.KitBrickCourseWall:
                    AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.28f, 0.28f, MaterialIdCatalog.LimeMortarStoneWall, 168, skirt: true);
                    for (int row = 0; row < 3; row++)
                    {
                        float y0 = -0.48f + row * 0.33f;
                        float y1 = y0 + 0.28f;
                        float shift = (row & 1) == 0 ? 0f : 0.24f;
                        AddBox(dst, -0.49f + shift * 0.2f, -0.02f + shift * 0.2f, y0, y1, -0.34f, 0.34f, 0, (byte)(206 + row * 6), skirt: row == 0);
                        AddBox(dst,  0.02f - shift * 0.2f,  0.49f - shift * 0.2f, y0, y1, -0.34f, 0.34f, 0, (byte)(214 - row * 4), skirt: row == 0);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitAdobeCurvedWall:
                    if (!TryAppendFittedGlb(glbDir, "mod_cob_curved_wall_2m.glb", dst, fitX: 1.00f, fitY: 1.00f, fitZ: 0.62f))
                    {
                        AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.32f, 0.32f, 0, 212, skirt: true);
                    }
                    // Disari uzanan Pueblo/Anadolu ahsap hatil (viga) uclari
                    AddHorizontalCylinderZ(dst, -0.46f, 0.46f, 0.32f, 0.0f, 0.09f, 8, MaterialIdCatalog.TreatedTimberBeam, 178, capEnds: true);
                    break;

                case ArchitecturalShapeCatalog.KitPlankFramedWall:
                    AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.20f, 0.20f, MaterialIdCatalog.TreatedTimberBeam, 170, skirt: true);
                    for (int s = 0; s < 4; s++)
                    {
                        float y0 = -0.50f + s * 0.25f;
                        float y1 = y0 + 0.27f;
                        AddSlantedClapboard(dst, y0, y1, 0, (byte)(206 + (s & 1) * 12));
                    }
                    break;

                case ArchitecturalShapeCatalog.KitClassicalColumnBase:
                    if (!TryAppendFittedGlb(glbDir, "mod_classical_column_base.glb", dst, fitX: 0.92f, fitY: 1.00f, fitZ: 0.92f))
                    {
                        AddBox(dst, -0.46f, 0.46f, -0.50f, -0.22f, -0.46f, 0.46f, 0, 198, skirt: true);
                        AddVerticalCylinderY(dst, -0.22f, 0.02f, 0f, 0f, 0.42f, 0.36f, 12, 0, 216);
                        AddVerticalCylinderY(dst,  0.02f, 0.50f, 0f, 0f, 0.33f, 0.32f, 12, 0, 220);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitClassicalColumnShaft:
                    if (!TryAppendFittedGlb(glbDir, "mod_classical_column_shaft_3m.glb", dst, fitX: 0.68f, fitY: 1.00f, fitZ: 0.68f))
                    {
                        AddVerticalCylinderY(dst, -0.50f, 0.50f, 0f, 0f, 0.32f, 0.31f, 14, 0, 222);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitClassicalColumnCap:
                    if (!TryAppendFittedGlb(glbDir, "mod_classical_column_capital.glb", dst, fitX: 0.94f, fitY: 1.00f, fitZ: 0.94f))
                    {
                        AddVerticalCylinderY(dst, -0.50f, -0.05f, 0f, 0f, 0.31f, 0.42f, 12, 0, 216);
                        AddBox(dst, -0.47f, 0.47f, -0.05f, 0.28f, -0.47f, 0.47f, 0, 228);
                        AddBox(dst, -0.50f, 0.50f,  0.28f, 0.50f, -0.50f, 0.50f, 0, 232);
                    }
                    break;

                // =============================================================
                // B) 3B MERTEKLI, KIREMITLI / PEDAVRALI CATILAR & BACA (34..40)
                // =============================================================
                case ArchitecturalShapeCatalog.KitRoofClayTile45:
                    BuildSculptedRoofSlope45(dst, isClayBarrelTile: true, hasOverhangTail: false);
                    break;

                case ArchitecturalShapeCatalog.KitRoofShingle45:
                    BuildSculptedRoofSlope45(dst, isClayBarrelTile: false, hasOverhangTail: false);
                    break;

                case ArchitecturalShapeCatalog.KitRoofEaveOverhang45:
                    BuildSculptedRoofSlope45(dst, isClayBarrelTile: true, hasOverhangTail: true);
                    break;

                case ArchitecturalShapeCatalog.KitRoofRidgeCrest:
                    // Alt destek kirisi + A-bicimli cift yone egimli mahya kiremidi ve tepe sirigi!
                    AddBox(dst, -0.50f, 0.50f, -0.50f, -0.08f, -0.46f, 0.46f, MaterialIdCatalog.TreatedTimberBeam, 176);
                    AddPitchedPrismX(dst, -0.50f, 0.50f, -0.10f, 0.38f, 0.52f, 0, 224);
                    AddHorizontalCylinderX(dst, -0.50f, 0.50f, 0.38f, 0.0f, 0.13f, 8, 0, 236, capEnds: true);
                    break;

                case ArchitecturalShapeCatalog.KitGableWall45:
                    // 45° egimli ucgen kalkan duvar (-Z alcalan, +Z yukselen) + oymali ahsap alin kirisi (bargeboard)
                    AddSlopedWedgeZ(dst, -0.30f, 0.30f, -0.50f, 0.50f, 0, 212);
                    AddStrut(dst, new Vector3(0f, -0.48f, -0.50f), new Vector3(0f, 0.52f, 0.50f), 0.36f, 0.12f, MaterialIdCatalog.TreatedTimberBeam, 182);
                    break;

                case ArchitecturalShapeCatalog.KitChimneyFlue:
                    if (!TryAppendFittedGlb(glbDir, "mod_chimney_flue_2m.glb", dst, fitX: 0.84f, fitY: 1.00f, fitZ: 0.84f))
                    {
                        AddBox(dst, -0.38f, 0.38f, -0.50f, 0.28f, -0.38f, 0.38f, MaterialIdCatalog.FiredClayBrick, 208, skirt: true);
                        AddBox(dst, -0.46f, 0.46f,  0.28f, 0.44f, -0.46f, 0.46f, MaterialIdCatalog.CutAshlarStone, 224);
                        AddBox(dst, -0.24f, 0.24f,  0.44f, 0.50f, -0.24f, 0.24f, MaterialIdCatalog.Basalt, 95);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitDomeRibVault:
                    AddSlopedWedgeZ(dst, -0.50f, 0.50f, -0.50f, 0.50f, 0, 220);
                    AddStrut(dst, new Vector3(0f, -0.46f, -0.48f), new Vector3(0f, 0.52f, 0.48f), 0.18f, 0.14f, MaterialIdCatalog.DressedMarbleBlock, 240);
                    break;

                // =============================================================
                // C) 3B PENCERELER, 2x3m GECILEBILIR KAPILAR VE GENIS MERDIVEN (41..48)
                // =============================================================
                case ArchitecturalShapeCatalog.KitShutteredWindow:
                    BuildShutteredWindow3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitArchedStoneWindow:
                    BuildArchedStoneWindow3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitDoorFrameJamb:
                    // Sol kenarda (-X: -0.50..-0.28) zarif oymali kapi sogesi -> ortadaki gecisi 100% acik birakir!
                    AddBox(dst, -0.50f, -0.26f, -0.50f, 0.50f, -0.32f, 0.32f, 0, 198, skirt: true);
                    AddBox(dst, -0.30f, -0.20f, -0.50f, 0.50f, -0.20f, 0.20f, MaterialIdCatalog.TreatedTimberBeam, 178, skirt: true);
                    break;

                case ArchitecturalShapeCatalog.KitDoorArchLintel:
                    // Kapi ustu kalin tas/ahsap lento + tahfif kemeri silmesi
                    AddBox(dst, -0.50f, 0.50f, -0.50f, -0.18f, -0.34f, 0.34f, MaterialIdCatalog.TreatedTimberBeam, 182);
                    AddBox(dst, -0.50f, 0.50f, -0.18f,  0.50f, -0.30f, 0.30f, 0, 218);
                    AddBox(dst, -0.50f, 0.50f,  0.34f,  0.50f, -0.38f, 0.38f, MaterialIdCatalog.CutAshlarStone, 228);
                    break;

                case ArchitecturalShapeCatalog.KitHingedDoorClosed:
                    BuildHingedDoorLeaf3D(dst, isOpen: false);
                    break;

                case ArchitecturalShapeCatalog.KitHingedDoorOpen:
                    // 90° iceriye (-X kenarina: -0.50..-0.36) tam yaslanmis 3B acik kapi kanadi -> oyuncu takilmadan yurur!
                    BuildHingedDoorLeaf3D(dst, isOpen: true);
                    break;

                case ArchitecturalShapeCatalog.KitWideTimberStair:
                    BuildOpenRiserTimberStair3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBalconyBalustrade:
                    BuildBalustrade3D(dst);
                    break;

                // =============================================================
                // D) %100 3B HEYKELLENMIS COK-MALZEMELI IC MEKAN ESYALARI (49..62)
                // =============================================================
                case ArchitecturalShapeCatalog.KitDiningTable:
                    BuildDiningTable3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitChairWithBack:
                    BuildChair3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBedHeadboard:
                    BuildBedHead3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBedFoot:
                    BuildBedFoot3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBookshelf:
                    BuildBookshelf3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitFireplaceHearth:
                    BuildFireplaceHearth3D(dst, glbDir);
                    break;

                case ArchitecturalShapeCatalog.KitKitchenCounter:
                    BuildKitchenCounter3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitStorageChest:
                    BuildStorageChest3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitWoodenBarrel:
                    BuildWoodenBarrel3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitRoyalThrone:
                    BuildRoyalThrone3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitAnvilOnStump:
                    BuildAnvilOnStump3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitSawhorseBench:
                    BuildSawhorseBench3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitWeaponRack:
                    BuildWeaponRack3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitWellAndFence:
                    BuildSplitRailFence3D(dst);
                    break;
            }
        }

        // =====================================================================
        // GERCEK 3B CATI, PENCERE, KAPI, MERDIVEN VE MOBILYA MODEL URETICILERI
        // =====================================================================

        private static void BuildSculptedRoofSlope45(List<LocalKitTriangle> dst, bool isClayBarrelTile, bool hasOverhangTail)
        {
            // 1) Altta egimli tasiyici ahsap mertekler (sol, orta, sag kiris) + kaplama tahtasi (ic tavandan bakinca gercek ahsap cati gorunur!)
            float zMin = hasOverhangTail ? -0.64f : -0.50f;
            float yMin = hasOverhangTail ? -0.64f : -0.50f;
            AddStrut(dst, new Vector3(-0.38f, yMin + 0.06f, zMin), new Vector3(-0.38f, 0.44f, 0.50f), 0.11f, 0.12f, MaterialIdCatalog.TreatedTimberBeam, 168);
            AddStrut(dst, new Vector3( 0.00f, yMin + 0.06f, zMin), new Vector3( 0.00f, 0.44f, 0.50f), 0.10f, 0.12f, MaterialIdCatalog.TreatedTimberBeam, 172);
            AddStrut(dst, new Vector3( 0.38f, yMin + 0.06f, zMin), new Vector3( 0.38f, 0.44f, 0.50f), 0.11f, 0.12f, MaterialIdCatalog.TreatedTimberBeam, 168);

            // Sizmazyalitim plakasi (egimli ince tahta alti)
            AddSlopedSlab45(dst, -0.50f, 0.50f, -0.05f, 0.03f, MaterialIdCatalog.WoodPlank, 182);

            if (isClayBarrelTile)
            {
                // Ustte 4 sira yarim-silindirik oluklu Alaturka/Akdeniz kiremidi!
                float[] xs = { -0.375f, -0.125f, 0.125f, 0.375f };
                for (int i = 0; i < xs.Length; i++)
                {
                    AddStrut(
                        dst,
                        new Vector3(xs[i], -0.42f, -0.52f),
                        new Vector3(xs[i],  0.56f,  0.50f),
                        0.19f, 0.11f,
                        0, (byte)(216 + (i & 1) * 14));
                }
            }
            else
            {
                // Ustte 3 basamakli bindirmeli (overlapping) yarma sedir zona / pedavra tahtalari!
                for (int s = 0; s < 3; s++)
                {
                    float t0 = s / 3.0f;
                    float t1 = (s + 1.08f) / 3.0f;
                    float z0 = -0.52f + t0 * 1.02f;
                    float z1 = -0.52f + t1 * 1.02f;
                    float y0 = -0.44f + t0 * 1.00f;
                    float y1 = -0.42f + t1 * 0.98f;
                    AddStrut(dst, new Vector3(0f, y0, z0), new Vector3(0f, y1, z1), 1.00f, 0.08f, 0, (byte)(204 + s * 12));
                }
            }
        }

        private static void BuildShutteredWindow3D(List<LocalKitTriangle> dst)
        {
            // Tas denizlik + kalin ahsap cerceve + arti (+) orta kayit + yarim saydam mavi cam + iki yana acik 3B panjur kanatlari!
            AddBox(dst, -0.50f, 0.50f, -0.50f, -0.34f, -0.34f, 0.34f, MaterialIdCatalog.CutAshlarStone, 224);
            AddBox(dst, -0.50f, 0.50f,  0.36f,  0.50f, -0.28f, 0.28f, MaterialIdCatalog.TreatedTimberBeam, 188);
            AddBox(dst, -0.50f, -0.36f, -0.34f, 0.36f, -0.26f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddBox(dst,  0.36f,  0.50f, -0.34f, 0.36f, -0.26f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 182);
            // Cam panel + arti kayit
            AddBox(dst, -0.36f, 0.36f, -0.34f, 0.36f, -0.04f, 0.04f, MaterialIdCatalog.LeadedGlassPane, 238);
            AddBox(dst, -0.04f, 0.04f, -0.34f, 0.36f, -0.09f, 0.09f, MaterialIdCatalog.TreatedTimberBeam, 176);
            AddBox(dst, -0.36f, 0.36f, -0.03f, 0.05f, -0.09f, 0.09f, MaterialIdCatalog.TreatedTimberBeam, 176);
            // Disa acilmis sol/sag panjurlar (-Z on cephede)
            AddBox(dst, -0.54f, -0.35f, -0.32f, 0.34f, -0.36f, -0.24f, MaterialIdCatalog.WoodPlank, 198);
            AddBox(dst,  0.35f,  0.54f, -0.32f, 0.34f, -0.36f, -0.24f, MaterialIdCatalog.WoodPlank, 198);
        }

        private static void BuildArchedStoneWindow3D(List<LocalKitTriangle> dst)
        {
            AddBox(dst, -0.50f, 0.50f, -0.50f, -0.34f, -0.36f, 0.36f, MaterialIdCatalog.DressedMarbleBlock, 232);
            AddBox(dst, -0.50f, -0.34f, -0.34f, 0.50f, -0.30f, 0.30f, MaterialIdCatalog.DressedMarbleBlock, 222);
            AddBox(dst,  0.34f,  0.50f, -0.34f, 0.50f, -0.30f, 0.30f, MaterialIdCatalog.DressedMarbleBlock, 222);
            AddBox(dst, -0.34f,  0.34f,  0.30f, 0.50f, -0.32f, 0.32f, MaterialIdCatalog.DressedMarbleBlock, 228);
            AddBox(dst, -0.34f,  0.34f, -0.34f, 0.30f, -0.04f, 0.04f, MaterialIdCatalog.LeadedGlassPane, 242);
            AddBox(dst, -0.03f,  0.03f, -0.34f, 0.30f, -0.08f, 0.08f, MaterialIdCatalog.IronTieBeamBlock, 160);
        }

        private static void BuildHingedDoorLeaf3D(List<LocalKitTriangle> dst, bool isOpen)
        {
            if (!isOpen)
            {
                // Kapali 3B ahsap kapi: Dikey tahtalar + yatay/capraz Z-kusak + dovme demir mentese ve halka tokmak!
                AddBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.08f, 0.06f, MaterialIdCatalog.WoodPlank, 208);
                AddBox(dst, -0.48f, 0.48f,  0.26f, 0.38f, -0.12f, 0.10f, MaterialIdCatalog.TreatedTimberBeam, 178);
                AddBox(dst, -0.48f, 0.48f, -0.38f, -0.26f, -0.12f, 0.10f, MaterialIdCatalog.TreatedTimberBeam, 178);
                AddStrut(dst, new Vector3(-0.42f, -0.30f, 0f), new Vector3(0.42f, 0.30f, 0f), 0.10f, 0.20f, MaterialIdCatalog.TreatedTimberBeam, 172);
                // Dovme demir mentese kayislari ve kapi kolu
                AddBox(dst, -0.50f, -0.10f,  0.30f, 0.35f, -0.14f, 0.12f, MaterialIdCatalog.IronTieBeamBlock, 145);
                AddBox(dst, -0.50f, -0.10f, -0.35f, -0.30f, -0.14f, 0.12f, MaterialIdCatalog.IronTieBeamBlock, 145);
                AddBox(dst,  0.28f,  0.40f, -0.06f, 0.08f, -0.16f, 0.14f, MaterialIdCatalog.IronTieBeamBlock, 165);
            }
            else
            {
                // 90° yana (-X kenarina: x = -0.48..-0.36) tam acilmis 3B kapi kanadi -> gecis %100 ferah!
                AddBox(dst, -0.49f, -0.37f, -0.50f, 0.50f, -0.48f, 0.48f, MaterialIdCatalog.WoodPlank, 206);
                AddBox(dst, -0.50f, -0.35f,  0.26f, 0.38f, -0.46f, 0.46f, MaterialIdCatalog.TreatedTimberBeam, 178);
                AddBox(dst, -0.50f, -0.35f, -0.38f, -0.26f, -0.46f, 0.46f, MaterialIdCatalog.TreatedTimberBeam, 178);
                AddBox(dst, -0.50f, -0.34f, -0.06f, 0.08f,  0.26f, 0.38f, MaterialIdCatalog.IronTieBeamBlock, 165);
            }
        }

        private static void BuildOpenRiserTimberStair3D(List<LocalKitTriangle> dst)
        {
            // Iki yan egimli limon kirisi (stringer) + 2 kalin yurunebilir basamak tahtasi!
            AddStrut(dst, new Vector3(-0.43f, -0.48f, -0.46f), new Vector3(-0.43f, 0.46f, 0.46f), 0.12f, 0.18f, MaterialIdCatalog.TreatedTimberBeam, 178);
            AddStrut(dst, new Vector3( 0.43f, -0.48f, -0.46f), new Vector3( 0.43f, 0.46f, 0.46f), 0.12f, 0.18f, MaterialIdCatalog.TreatedTimberBeam, 178);
            // Alt basamak (y = -0.08..0.00, z = -0.50..0.04) ve ust basamak (y = 0.42..0.50, z = -0.02..0.50)
            AddBox(dst, -0.50f, 0.50f, -0.10f, 0.00f, -0.50f, 0.04f, 0, 224);
            AddBox(dst, -0.50f, 0.50f,  0.40f, 0.50f, -0.02f, 0.50f, 0, 228);
        }

        private static void BuildBalustrade3D(List<LocalKitTriangle> dst)
        {
            // -Z kenarinda alt etek rayi + 3 torna korkuluk dikmesi + ust kupeste
            AddBox(dst, -0.50f, 0.50f, -0.50f, -0.40f, -0.48f, -0.32f, 0, 196);
            AddBox(dst, -0.50f, 0.50f,  0.34f,  0.46f, -0.50f, -0.30f, MaterialIdCatalog.TreatedTimberBeam, 218);
            float[] xs = { -0.38f, 0.0f, 0.38f };
            for (int i = 0; i < xs.Length; i++)
            {
                AddVerticalCylinderY(dst, -0.40f, 0.34f, xs[i], -0.40f, 0.065f, 0.055f, 8, 0, 208);
            }
        }

        private static void BuildDiningTable3D(List<LocalKitTriangle> dst)
        {
            // 4 torna ahsap ayak + ust cerceve kusagi + tasan kalin masa tablasi
            float[] lx = { -0.38f, 0.38f };
            float[] lz = { -0.38f, 0.38f };
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                    AddVerticalCylinderY(dst, -0.50f, 0.36f, lx[ix], lz[iz], 0.065f, 0.055f, 8, MaterialIdCatalog.TreatedTimberBeam, 180);

            AddBox(dst, -0.40f, 0.40f, 0.24f, 0.36f, -0.40f, 0.40f, MaterialIdCatalog.TreatedTimberBeam, 174);
            AddBox(dst, -0.49f, 0.49f, 0.36f, 0.46f, -0.49f, 0.49f, MaterialIdCatalog.WoodPlank, 226);
        }

        private static void BuildChair3D(List<LocalKitTriangle> dst)
        {
            float[] lx = { -0.24f, 0.24f };
            float[] lz = { -0.24f, 0.24f };
            for (int ix = 0; ix < 2; ix++)
            {
                for (int iz = 0; iz < 2; iz++)
                {
                    float topY = iz == 1 ? 0.46f : -0.04f;
                    AddVerticalCylinderY(dst, -0.50f, topY, lx[ix], lz[iz], 0.045f, 0.04f, 6, MaterialIdCatalog.TreatedTimberBeam, 182);
                }
            }
            AddBox(dst, -0.28f, 0.28f, -0.06f, 0.02f, -0.28f, 0.28f, MaterialIdCatalog.WoodPlank, 220);
            AddBox(dst, -0.25f, 0.25f,  0.16f, 0.26f,  0.21f, 0.27f, MaterialIdCatalog.WoodPlank, 212);
            AddBox(dst, -0.25f, 0.25f,  0.34f, 0.44f,  0.21f, 0.27f, MaterialIdCatalog.WoodPlank, 218);
        }

        private static void BuildBedHead3D(List<LocalKitTriangle> dst)
        {
            // 2 yuksek topuzlu baslik dikmesi + oymali baslik + beyaz yastik + kirmizi yorgan!
            AddVerticalCylinderY(dst, -0.50f, 0.48f, -0.42f, 0.40f, 0.07f, 0.06f, 8, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddVerticalCylinderY(dst, -0.50f, 0.48f,  0.42f, 0.40f, 0.07f, 0.06f, 8, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddBox(dst, -0.42f, 0.42f, -0.22f, 0.38f,  0.36f, 0.44f, MaterialIdCatalog.OakWood, 198);
            AddBox(dst, -0.45f, 0.45f, -0.32f, -0.14f, -0.50f, 0.42f, MaterialIdCatalog.TreatedTimberBeam, 178);
            // Beyaz carsaf & kabarık yastik + kirmizi battaniye ucu
            AddBox(dst, -0.42f, 0.42f, -0.14f, 0.06f, -0.50f, 0.36f, MaterialIdCatalog.LimeWashPlaster, 238);
            AddBox(dst, -0.34f, 0.34f,  0.06f, 0.18f,  0.02f, 0.34f, MaterialIdCatalog.LimeWashPlaster, 246);
            AddBox(dst, -0.43f, 0.43f, -0.12f, 0.09f, -0.50f, -0.04f, MaterialIdCatalog.BedBlock, 224);
        }

        private static void BuildBedFoot3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, 0.18f, -0.42f, -0.40f, 0.07f, 0.06f, 8, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddVerticalCylinderY(dst, -0.50f, 0.18f,  0.42f, -0.40f, 0.07f, 0.06f, 8, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddBox(dst, -0.42f, 0.42f, -0.28f, 0.12f, -0.44f, -0.36f, MaterialIdCatalog.OakWood, 196);
            AddBox(dst, -0.45f, 0.45f, -0.32f, -0.14f, -0.42f, 0.50f, MaterialIdCatalog.TreatedTimberBeam, 178);
            AddBox(dst, -0.43f, 0.43f, -0.14f, 0.09f, -0.36f, 0.50f, MaterialIdCatalog.BedBlock, 224);
        }

        private static void BuildBookshelf3D(List<LocalKitTriangle> dst)
        {
            // Arka ve yan ahsap govde + 3 raf + her rafta farkli boy ve renkte 3B kitaplar!
            AddBox(dst, -0.48f, 0.48f, -0.50f, 0.50f,  0.22f, 0.38f, MaterialIdCatalog.TreatedTimberBeam, 168);
            AddBox(dst, -0.48f, -0.38f, -0.50f, 0.50f, -0.18f, 0.38f, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddBox(dst,  0.38f,  0.48f, -0.50f, 0.50f, -0.18f, 0.38f, MaterialIdCatalog.TreatedTimberBeam, 184);
            float[] shelfYs = { -0.46f, -0.14f, 0.18f, 0.44f };
            for (int s = 0; s < shelfYs.Length; s++)
            {
                AddBox(dst, -0.38f, 0.38f, shelfYs[s], shelfYs[s] + 0.06f, -0.18f, 0.24f, MaterialIdCatalog.WoodPlank, 212);
            }

            byte[] bookMats =
            {
                MaterialIdCatalog.BedBlock,
                MaterialIdCatalog.LeadedGlassPane,
                MaterialIdCatalog.Bronze,
                MaterialIdCatalog.RoofClayTile,
                MaterialIdCatalog.DressedMarbleBlock
            };
            for (int r = 0; r < 3; r++)
            {
                float by0 = shelfYs[r] + 0.06f;
                for (int b = 0; b < 5; b++)
                {
                    float bx0 = -0.34f + b * 0.14f;
                    float bh = 0.20f + ((r + b) % 3) * 0.025f;
                    AddBox(dst, bx0, bx0 + 0.11f, by0, by0 + bh, -0.12f, 0.21f, bookMats[(r * 2 + b) % bookMats.Length], (byte)(206 + b * 8));
                }
            }
        }

        private static void BuildFireplaceHearth3D(List<LocalKitTriangle> dst, string? glbDir)
        {
            if (!TryAppendFittedGlb(glbDir, "mod_fireplace_hearth.glb", dst, fitX: 0.96f, fitY: 1.00f, fitZ: 0.88f))
            {
                AddBox(dst, -0.48f, 0.48f, -0.50f, -0.32f, -0.42f, 0.46f, MaterialIdCatalog.CutAshlarStone, 208, skirt: true);
                AddBox(dst, -0.48f, 0.48f, -0.32f,  0.42f,  0.22f, 0.46f, MaterialIdCatalog.FiredClayBrick, 196);
                AddBox(dst, -0.48f, -0.26f, -0.32f, 0.42f, -0.36f, 0.22f, MaterialIdCatalog.FiredClayBrick, 206);
                AddBox(dst,  0.26f,  0.48f, -0.32f, 0.42f, -0.36f, 0.22f, MaterialIdCatalog.FiredClayBrick, 206);
                AddBox(dst, -0.50f,  0.50f,  0.36f, 0.50f, -0.42f, 0.46f, MaterialIdCatalog.TreatedTimberBeam, 188);
            }
            // Ic ates haznesinde parlayan sicak kor kutukler!
            AddHorizontalCylinderX(dst, -0.20f, 0.20f, -0.24f, -0.04f, 0.07f, 6, MaterialIdCatalog.CampfireBlock, 240, capEnds: true, tempOverride: 215);
            AddHorizontalCylinderZ(dst, -0.18f, 0.16f, -0.18f,  0.02f, 0.06f, 6, MaterialIdCatalog.CampfireBlock, 248, capEnds: true, tempOverride: 225);
        }

        private static void BuildKitchenCounter3D(List<LocalKitTriangle> dst)
        {
            AddBox(dst, -0.48f, 0.48f, -0.50f, 0.34f, -0.36f, 0.44f, MaterialIdCatalog.OakWood, 196);
            AddBox(dst, -0.42f, -0.03f, -0.40f, 0.26f, -0.40f, -0.35f, MaterialIdCatalog.WoodPlank, 212);
            AddBox(dst,  0.03f,  0.42f, -0.40f, 0.26f, -0.40f, -0.35f, MaterialIdCatalog.WoodPlank, 212);
            AddBox(dst, -0.50f, 0.50f,  0.34f, 0.44f, -0.44f, 0.48f, MaterialIdCatalog.DressedMarbleBlock, 236);
            // Tezgah ustu seramik testi
            AddVerticalCylinderY(dst, 0.44f, 0.58f, 0.22f, 0.08f, 0.09f, 0.06f, 8, MaterialIdCatalog.RoofClayTile, 218);
        }

        private static void BuildStorageChest3D(List<LocalKitTriangle> dst)
        {
            AddBox(dst, -0.42f, 0.42f, -0.50f, 0.04f, -0.30f, 0.30f, MaterialIdCatalog.OakWood, 198);
            // Bombeli sandik kapagi + demir cemberler ve on kilit plakasi
            AddHorizontalCylinderX(dst, -0.42f, 0.42f, 0.04f, 0.0f, 0.30f, 8, MaterialIdCatalog.WoodPlank, 214, capEnds: true);
            AddBox(dst, -0.34f, -0.24f, -0.50f, 0.35f, -0.32f, 0.32f, MaterialIdCatalog.IronTieBeamBlock, 152);
            AddBox(dst,  0.24f,  0.34f, -0.50f, 0.35f, -0.32f, 0.32f, MaterialIdCatalog.IronTieBeamBlock, 152);
            AddBox(dst, -0.07f,  0.07f, -0.08f, 0.12f, -0.34f, -0.28f, MaterialIdCatalog.Bronze, 224);
        }

        private static void BuildWoodenBarrel3D(List<LocalKitTriangle> dst)
        {
            // Gercek gobekli fici (alt/orta/ust konik silindir halkalari + 2 demir fici cemberi)
            AddVerticalCylinderY(dst, -0.50f, -0.02f, 0f, 0f, 0.31f, 0.38f, 12, MaterialIdCatalog.OakWood, 204);
            AddVerticalCylinderY(dst, -0.02f,  0.46f, 0f, 0f, 0.38f, 0.31f, 12, MaterialIdCatalog.OakWood, 212);
            AddVerticalCylinderY(dst, -0.34f, -0.24f, 0f, 0f, 0.35f, 0.36f, 12, MaterialIdCatalog.IronTieBeamBlock, 148);
            AddVerticalCylinderY(dst,  0.18f,  0.28f, 0f, 0f, 0.36f, 0.35f, 12, MaterialIdCatalog.IronTieBeamBlock, 148);
        }

        private static void BuildRoyalThrone3D(List<LocalKitTriangle> dst)
        {
            AddBox(dst, -0.48f, 0.48f, -0.50f, -0.32f, -0.46f, 0.46f, MaterialIdCatalog.DressedMarbleBlock, 228);
            AddBox(dst, -0.38f, 0.38f, -0.32f, -0.02f, -0.34f, 0.36f, MaterialIdCatalog.DressedMarbleBlock, 236);
            AddBox(dst, -0.34f, 0.34f, -0.02f,  0.08f, -0.32f, 0.30f, MaterialIdCatalog.BedBlock, 224);
            AddBox(dst, -0.40f, -0.28f, -0.02f, 0.28f, -0.32f, 0.34f, MaterialIdCatalog.Bronze, 226);
            AddBox(dst,  0.28f,  0.40f, -0.02f, 0.28f, -0.32f, 0.34f, MaterialIdCatalog.Bronze, 226);
            AddBox(dst, -0.36f,  0.36f,  0.08f, 0.68f,  0.24f, 0.38f, MaterialIdCatalog.DressedMarbleBlock, 238);
            AddBox(dst, -0.28f,  0.28f,  0.10f, 0.58f,  0.20f, 0.25f, MaterialIdCatalog.BedBlock, 228);
        }

        private static void BuildAnvilOnStump3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, 0.02f, 0f, 0f, 0.34f, 0.30f, 10, MaterialIdCatalog.OakWood, 192);
            AddBox(dst, -0.24f, 0.24f, 0.02f, 0.14f, -0.16f, 0.16f, MaterialIdCatalog.Steel, 165);
            AddBox(dst, -0.14f, 0.14f, 0.14f, 0.28f, -0.11f, 0.11f, MaterialIdCatalog.Steel, 155);
            AddBox(dst, -0.28f, 0.22f, 0.28f, 0.44f, -0.15f, 0.15f, MaterialIdCatalog.Steel, 188);
            // Sivri ors boynuzu (+X yonune uzanan koni)
            AddHorizontalConeX(dst, 0.22f, 0.48f, 0.36f, 0f, 0.12f, 8, MaterialIdCatalog.Steel, 196);
        }

        private static void BuildSawhorseBench3D(List<LocalKitTriangle> dst)
        {
            AddStrut(dst, new Vector3(-0.36f, -0.50f, -0.28f), new Vector3(-0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddStrut(dst, new Vector3(-0.36f, -0.50f,  0.28f), new Vector3(-0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddStrut(dst, new Vector3( 0.36f, -0.50f, -0.28f), new Vector3( 0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddStrut(dst, new Vector3( 0.36f, -0.50f,  0.28f), new Vector3( 0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddBox(dst, -0.48f, 0.48f, 0.20f, 0.34f, -0.14f, 0.14f, MaterialIdCatalog.WoodPlank, 222);
        }

        private static void BuildWeaponRack3D(List<LocalKitTriangle> dst)
        {
            AddBox(dst, -0.44f, 0.44f, -0.50f, -0.38f, -0.22f, 0.22f, MaterialIdCatalog.TreatedTimberBeam, 180);
            AddBox(dst, -0.42f, -0.34f, -0.38f, 0.38f, -0.06f, 0.06f, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddBox(dst,  0.34f,  0.42f, -0.38f, 0.38f, -0.06f, 0.06f, MaterialIdCatalog.TreatedTimberBeam, 184);
            AddBox(dst, -0.42f,  0.42f,  0.18f, 0.26f, -0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 188);
            float[] sx = { -0.20f, 0.0f, 0.20f };
            for (int i = 0; i < sx.Length; i++)
            {
                AddVerticalCylinderY(dst, -0.38f, 0.36f, sx[i], 0.0f, 0.025f, 0.025f, 6, MaterialIdCatalog.OakWood, 200);
                AddVerticalCylinderY(dst,  0.36f, 0.62f, sx[i], 0.0f, 0.045f, 0.008f, 6, MaterialIdCatalog.Steel, 232);
            }
        }

        private static void BuildSplitRailFence3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, 0.46f, 0f, 0f, 0.09f, 0.08f, 8, MaterialIdCatalog.OakWood, 192);
            AddHorizontalCylinderX(dst, -0.50f, 0.50f, -0.12f, 0f, 0.055f, 6, MaterialIdCatalog.WoodPlank, 212, capEnds: true);
            AddHorizontalCylinderX(dst, -0.50f, 0.50f,  0.22f, 0f, 0.055f, 6, MaterialIdCatalog.WoodPlank, 218, capEnds: true);
        }

        private static void BuildWellWindlass3D(List<LocalKitTriangle> dst)
        {
            AddBox(dst, -0.46f, -0.34f, -0.50f, 0.46f, -0.10f, 0.10f, MaterialIdCatalog.TreatedTimberBeam, 186);
            AddBox(dst,  0.34f,  0.46f, -0.50f, 0.46f, -0.10f, 0.10f, MaterialIdCatalog.TreatedTimberBeam, 186);
            AddHorizontalCylinderX(dst, -0.36f, 0.36f, 0.32f, 0f, 0.10f, 8, MaterialIdCatalog.OakWood, 208, capEnds: true);
            AddPitchedPrismX(dst, -0.52f, 0.52f, 0.44f, 0.72f, 0.44f, MaterialIdCatalog.TimberShingleRoof, 216);
            AddVerticalCylinderY(dst, -0.24f, -0.02f, 0f, 0f, 0.11f, 0.13f, 8, MaterialIdCatalog.OakWood, 198);
        }

        // =====================================================================
        // SAF C# (.NET 10 + UNITY 6) .GLB IKILI OKUYUCU VE PRIMITIF YARDIMCILARI
        // =====================================================================

        private static bool TryAppendFittedGlb(
            string? glbDir,
            string fileName,
            List<LocalKitTriangle> dst,
            float fitX,
            float fitY,
            float fitZ)
        {
            if (string.IsNullOrEmpty(glbDir)) return false;
            string fullPath = Path.Combine(glbDir, fileName);
            if (!File.Exists(fullPath)) return false;

            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes.Length < 28 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) return false;

                int offset = 12;
                string? jsonText = null;
                int binStart = -1;
                while (offset + 8 <= bytes.Length)
                {
                    int chunkLen = BitConverter.ToInt32(bytes, offset);
                    uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                    int dataStart = offset + 8;
                    if (chunkLen < 0 || dataStart + chunkLen > bytes.Length) break;
                    if (chunkType == 0x4E4F534Au) jsonText = Encoding.UTF8.GetString(bytes, dataStart, chunkLen);
                    else if (chunkType == 0x004E4942u) binStart = dataStart;
                    offset = dataStart + chunkLen;
                }
                if (string.IsNullOrEmpty(jsonText) || binStart < 0) return false;

                if (!TryExtractGlbTriangles(bytes, binStart, jsonText, out List<(Vector3 a, Vector3 b, Vector3 c, byte shade)> rawTris) ||
                    rawTris.Count == 0 || rawTris.Count > 320)
                {
                    return false;
                }

                Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < rawTris.Count; i++)
                {
                    min = Vector3.Min(min, Vector3.Min(rawTris[i].a, Vector3.Min(rawTris[i].b, rawTris[i].c)));
                    max = Vector3.Max(max, Vector3.Max(rawTris[i].a, Vector3.Max(rawTris[i].b, rawTris[i].c)));
                }

                Vector3 size = max - min;
                if (size.X < 1e-4f || size.Y < 1e-4f || size.Z < 1e-4f) return false;
                Vector3 center = (min + max) * 0.5f;
                Vector3 scale = new(fitX / size.X, fitY / size.Y, fitZ / size.Z);

                for (int i = 0; i < rawTris.Count; i++)
                {
                    var (ra, rb, rc, sh) = rawTris[i];
                    Vector3 a = (ra - center) * scale;
                    Vector3 b = (rb - center) * scale;
                    Vector3 c = (rc - center) * scale;
                    dst.Add(new LocalKitTriangle(a, b, c, 0, sh, 0, allowSkirtDrop: true));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

#if UNITY_5_3_OR_NEWER
        [Serializable]
        private sealed class MiniGltfRoot
        {
            public MiniGltfMesh[]? meshes;
            public MiniGltfBufferView[]? bufferViews;
            public MiniGltfAccessor[]? accessors;
        }

        [Serializable]
        private sealed class MiniGltfMesh
        {
            public MiniGltfPrim[]? primitives;
        }

        [Serializable]
        private sealed class MiniGltfPrim
        {
            public MiniGltfAttr? attributes;
            public int indices = -1;
        }

        [Serializable]
        private sealed class MiniGltfAttr
        {
            public int POSITION = -1;
            public int COLOR_0 = -1;
        }

        [Serializable]
        private sealed class MiniGltfBufferView
        {
            public int byteOffset;
            public int byteStride;
        }

        [Serializable]
        private sealed class MiniGltfAccessor
        {
            public int bufferView = -1;
            public int byteOffset;
            public int componentType;
            public int count;
        }
#endif

        private static bool TryExtractGlbTriangles(
            byte[] bytes,
            int binStart,
            string jsonText,
            out List<(Vector3 a, Vector3 b, Vector3 c, byte shade)> tris)
        {
            tris = new List<(Vector3, Vector3, Vector3, byte)>(128);
#if UNITY_5_3_OR_NEWER
            MiniGltfRoot? root = UnityEngine.JsonUtility.FromJson<MiniGltfRoot>(jsonText);
            if (root?.meshes == null || root.accessors == null || root.bufferViews == null) return false;
            for (int m = 0; m < root.meshes.Length; m++)
            {
                var prims = root.meshes[m].primitives;
                if (prims == null) continue;
                for (int p = 0; p < prims.Length; p++)
                {
                    var pr = prims[p];
                    if (pr.attributes == null || pr.attributes.POSITION < 0) continue;
                    var posAcc = root.accessors[pr.attributes.POSITION];
                    var posBv = root.bufferViews[posAcc.bufferView];
                    int posStride = posBv.byteStride > 0 ? posBv.byteStride : 12;
                    int posBase = binStart + posBv.byteOffset + posAcc.byteOffset;

                    Vector3[] verts = new Vector3[posAcc.count];
                    for (int i = 0; i < posAcc.count; i++)
                    {
                        int ptr = posBase + i * posStride;
                        verts[i] = new Vector3(
                            -BitConverter.ToSingle(bytes, ptr),
                            BitConverter.ToSingle(bytes, ptr + 4),
                            BitConverter.ToSingle(bytes, ptr + 8));
                    }

                    int[] idx = ReadIdx(bytes, binStart, pr.indices >= 0 ? root.accessors[pr.indices] : null, pr.indices >= 0 ? root.bufferViews[root.accessors[pr.indices].bufferView] : null, verts.Length);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        Vector3 a = verts[idx[i]];
                        Vector3 b = verts[idx[i + 2]];
                        Vector3 c = verts[idx[i + 1]];
                        byte sh = ComputeNormalShade(a, b, c);
                        tris.Add((a, b, c, sh));
                    }
                }
            }
            return tris.Count > 0;

            static int[] ReadIdx(byte[] buf, int bStart, MiniGltfAccessor? acc, MiniGltfBufferView? bv, int vLen)
            {
                if (acc == null || bv == null)
                {
                    int[] seq = new int[vLen];
                    for (int i = 0; i < vLen; i++) seq[i] = i;
                    return seq;
                }
                int ptr = bStart + bv.byteOffset + acc.byteOffset;
                int[] arr = new int[acc.count];
                for (int i = 0; i < acc.count; i++)
                {
                    arr[i] = acc.componentType switch
                    {
                        5125 => (int)BitConverter.ToUInt32(buf, ptr + i * 4),
                        5123 => BitConverter.ToUInt16(buf, ptr + i * 2),
                        _ => buf[ptr + i]
                    };
                }
                return arr;
            }
#else
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("meshes", out JsonElement meshes) ||
                !root.TryGetProperty("accessors", out JsonElement accessors) ||
                !root.TryGetProperty("bufferViews", out JsonElement bufferViews))
            {
                return false;
            }

            foreach (JsonElement mesh in meshes.EnumerateArray())
            {
                if (!mesh.TryGetProperty("primitives", out JsonElement prims)) continue;
                foreach (JsonElement pr in prims.EnumerateArray())
                {
                    if (!pr.TryGetProperty("attributes", out JsonElement attrs) ||
                        !attrs.TryGetProperty("POSITION", out JsonElement posIdxEl))
                    {
                        continue;
                    }

                    JsonElement posAcc = accessors[posIdxEl.GetInt32()];
                    JsonElement posBv = bufferViews[posAcc.GetProperty("bufferView").GetInt32()];
                    int posCount = posAcc.GetProperty("count").GetInt32();
                    int posAccOff = posAcc.TryGetProperty("byteOffset", out JsonElement pao) ? pao.GetInt32() : 0;
                    int posBvOff = posBv.TryGetProperty("byteOffset", out JsonElement pbo) ? pbo.GetInt32() : 0;
                    int posStride = posBv.TryGetProperty("byteStride", out JsonElement pbs) && pbs.GetInt32() > 0 ? pbs.GetInt32() : 12;
                    int posBase = binStart + posBvOff + posAccOff;

                    Vector3[] verts = new Vector3[posCount];
                    for (int i = 0; i < posCount; i++)
                    {
                        int ptr = posBase + i * posStride;
                        verts[i] = new Vector3(
                            -BitConverter.ToSingle(bytes, ptr),
                            BitConverter.ToSingle(bytes, ptr + 4),
                            BitConverter.ToSingle(bytes, ptr + 8));
                    }

                    int[] idx;
                    if (pr.TryGetProperty("indices", out JsonElement idxEl))
                    {
                        JsonElement iAcc = accessors[idxEl.GetInt32()];
                        JsonElement iBv = bufferViews[iAcc.GetProperty("bufferView").GetInt32()];
                        int iCount = iAcc.GetProperty("count").GetInt32();
                        int cType = iAcc.GetProperty("componentType").GetInt32();
                        int iOff = (iBv.TryGetProperty("byteOffset", out JsonElement ibo) ? ibo.GetInt32() : 0) +
                                   (iAcc.TryGetProperty("byteOffset", out JsonElement iao) ? iao.GetInt32() : 0);
                        int ptr = binStart + iOff;
                        idx = new int[iCount];
                        for (int i = 0; i < iCount; i++)
                        {
                            idx[i] = cType switch
                            {
                                5125 => (int)BitConverter.ToUInt32(bytes, ptr + i * 4),
                                5123 => BitConverter.ToUInt16(bytes, ptr + i * 2),
                                _ => bytes[ptr + i]
                            };
                        }
                    }
                    else
                    {
                        idx = new int[verts.Length];
                        for (int i = 0; i < verts.Length; i++) idx[i] = i;
                    }

                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        Vector3 a = verts[idx[i]];
                        Vector3 b = verts[idx[i + 2]];
                        Vector3 c = verts[idx[i + 1]];
                        tris.Add((a, b, c, ComputeNormalShade(a, b, c)));
                    }
                }
            }
            return tris.Count > 0;
#endif
        }

        private static byte ComputeNormalShade(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cr = Vector3.Cross(b - a, c - a);
            float len = cr.Length();
            if (len < 1e-6f) return 196;
            Vector3 n = cr / len;
            float sun = 0.55f + 0.30f * n.Y + 0.15f * MathF.Abs(n.X) + 0.10f * (-n.Z);
            return (byte)Math.Clamp((int)(sun * 240f), 128, 246);
        }

        private static void AddQuad(List<LocalKitTriangle> dst, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, byte mat, byte shade, byte temp = 0, bool skirt = false)
        {
            dst.Add(new LocalKitTriangle(v0, v1, v2, mat, shade, temp, skirt));
            dst.Add(new LocalKitTriangle(v0, v2, v3, mat, (byte)Math.Max(110, shade - 8), temp, skirt));
        }

        private static void AddBox(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float y0, float y1,
            float z0, float z1,
            byte mat,
            byte topShade,
            bool skirt = false)
        {
            Vector3 p000 = new(x0, y0, z0), p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p001 = new(x0, y0, z1);
            Vector3 p010 = new(x0, y1, z0), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1), p011 = new(x0, y1, z1);
            byte side = (byte)Math.Clamp(topShade - 22, 120, 240);
            byte bot = (byte)Math.Clamp(topShade - 56, 105, 220);

            AddQuad(dst, p010, p011, p111, p110, mat, topShade, 0, skirt); // +Y
            AddQuad(dst, p001, p000, p100, p101, mat, bot, 0, skirt);      // -Y
            AddQuad(dst, p000, p010, p110, p100, mat, side, 0, skirt);     // -Z
            AddQuad(dst, p101, p111, p011, p001, mat, side, 0, skirt);     // +Z
            AddQuad(dst, p001, p011, p010, p000, mat, (byte)(side - 8), 0, skirt); // -X
            AddQuad(dst, p100, p110, p111, p101, mat, (byte)(side - 8), 0, skirt); // +X
        }

        private static void AddVerticalCylinderY(
            List<LocalKitTriangle> dst,
            float y0, float y1,
            float cx, float cz,
            float rBot, float rTop,
            int sides,
            byte mat,
            byte baseShade)
        {
            Vector3 topC = new(cx, y1, cz);
            Vector3 botC = new(cx, y0, cz);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                Vector3 b0 = new(cx + MathF.Cos(a0) * rBot, y0, cz + MathF.Sin(a0) * rBot);
                Vector3 b1 = new(cx + MathF.Cos(a1) * rBot, y0, cz + MathF.Sin(a1) * rBot);
                Vector3 u0 = new(cx + MathF.Cos(a0) * rTop, y1, cz + MathF.Sin(a0) * rTop);
                Vector3 u1 = new(cx + MathF.Cos(a1) * rTop, y1, cz + MathF.Sin(a1) * rTop);
                byte sh = (byte)Math.Clamp(baseShade - 18 + (int)(MathF.Cos(a0) * 20f), 128, 246);
                AddQuad(dst, b0, u0, u1, b1, mat, sh);
                dst.Add(new LocalKitTriangle(topC, u1, u0, mat, baseShade));
                dst.Add(new LocalKitTriangle(botC, b0, b1, mat, (byte)Math.Max(115, baseShade - 55)));
            }
        }

        private static void AddHorizontalCylinderX(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float cy, float cz,
            float radius,
            int sides,
            byte mat,
            byte baseShade,
            bool capEnds,
            byte endCapMat = 0,
            byte tempOverride = 0)
        {
            byte capM = endCapMat != 0 ? endCapMat : mat;
            Vector3 lC = new(x0, cy, cz);
            Vector3 rC = new(x1, cy, cz);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                float yA = cy + MathF.Sin(a0) * radius, zA = cz + MathF.Cos(a0) * radius;
                float yB = cy + MathF.Sin(a1) * radius, zB = cz + MathF.Cos(a1) * radius;
                Vector3 l0 = new(x0, yA, zA), l1 = new(x0, yB, zB);
                Vector3 r0 = new(x1, yA, zA), r1 = new(x1, yB, zB);
                byte sh = (byte)Math.Clamp(baseShade - 14 + (int)(MathF.Sin(a0) * 26f), 124, 248);
                AddQuad(dst, l0, l1, r1, r0, mat, sh, tempOverride);
                if (capEnds)
                {
                    dst.Add(new LocalKitTriangle(lC, l1, l0, capM, (byte)Math.Min(245, baseShade + 10), tempOverride));
                    dst.Add(new LocalKitTriangle(rC, r0, r1, capM, (byte)Math.Min(245, baseShade + 10), tempOverride));
                }
            }
        }

        private static void AddHorizontalCylinderZ(
            List<LocalKitTriangle> dst,
            float z0, float z1,
            float cy, float cx,
            float radius,
            int sides,
            byte mat,
            byte baseShade,
            bool capEnds,
            byte endCapMat = 0,
            byte tempOverride = 0)
        {
            byte capM = endCapMat != 0 ? endCapMat : mat;
            Vector3 nC = new(cx, cy, z0);
            Vector3 pC = new(cx, cy, z1);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                float xA = cx + MathF.Cos(a0) * radius, yA = cy + MathF.Sin(a0) * radius;
                float xB = cx + MathF.Cos(a1) * radius, yB = cy + MathF.Sin(a1) * radius;
                Vector3 n0 = new(xA, yA, z0), n1 = new(xB, yB, z0);
                Vector3 p0 = new(xA, yA, z1), p1 = new(xB, yB, z1);
                byte sh = (byte)Math.Clamp(baseShade - 14 + (int)(MathF.Sin(a0) * 26f), 124, 248);
                AddQuad(dst, n0, p0, p1, n1, mat, sh, tempOverride);
                if (capEnds)
                {
                    dst.Add(new LocalKitTriangle(nC, n0, n1, capM, (byte)Math.Min(245, baseShade + 10), tempOverride));
                    dst.Add(new LocalKitTriangle(pC, p1, p0, capM, (byte)Math.Min(245, baseShade + 10), tempOverride));
                }
            }
        }

        private static void AddHorizontalConeX(
            List<LocalKitTriangle> dst,
            float xBase, float xTip,
            float cy, float cz,
            float rBase,
            int sides,
            byte mat,
            byte baseShade)
        {
            Vector3 tip = new(xTip, cy, cz);
            Vector3 baseC = new(xBase, cy, cz);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;
                Vector3 b0 = new(xBase, cy + MathF.Sin(a0) * rBase, cz + MathF.Cos(a0) * rBase);
                Vector3 b1 = new(xBase, cy + MathF.Sin(a1) * rBase, cz + MathF.Cos(a1) * rBase);
                dst.Add(new LocalKitTriangle(b0, b1, tip, mat, baseShade));
                dst.Add(new LocalKitTriangle(baseC, b1, b0, mat, (byte)(baseShade - 25)));
            }
        }

        private static void AddStrut(
            List<LocalKitTriangle> dst,
            Vector3 p0,
            Vector3 p1,
            float width,
            float depth,
            byte mat,
            byte shade)
        {
            Vector3 axis = Vector3.Normalize(p1 - p0);
            Vector3 refUp = MathF.Abs(axis.Y) > 0.92f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 right = Vector3.Normalize(Vector3.Cross(refUp, axis)) * (width * 0.5f);
            Vector3 perp = Vector3.Normalize(Vector3.Cross(axis, right)) * (depth * 0.5f);

            Vector3 a0 = p0 - right - perp, a1 = p0 + right - perp, a2 = p0 + right + perp, a3 = p0 - right + perp;
            Vector3 b0 = p1 - right - perp, b1 = p1 + right - perp, b2 = p1 + right + perp, b3 = p1 - right + perp;

            AddQuad(dst, a3, b3, b2, a2, mat, shade);
            AddQuad(dst, a0, a1, b1, b0, mat, (byte)(shade - 28));
            AddQuad(dst, a0, b0, b3, a3, mat, (byte)(shade - 14));
            AddQuad(dst, a1, a2, b2, b1, mat, (byte)(shade - 14));
            AddQuad(dst, a0, a3, a2, a1, mat, (byte)(shade - 18));
            AddQuad(dst, b0, b1, b2, b3, mat, (byte)(shade - 10));
        }

        private static void AddSlopedSlab45(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float yOffsetBot, float yOffsetTop,
            byte mat,
            byte shade)
        {
            Vector3 f0 = new(x0, -0.50f + yOffsetBot, -0.50f), f1 = new(x1, -0.50f + yOffsetBot, -0.50f);
            Vector3 f2 = new(x1, -0.50f + yOffsetTop, -0.50f), f3 = new(x0, -0.50f + yOffsetTop, -0.50f);
            Vector3 b0 = new(x0,  0.50f + yOffsetBot,  0.50f), b1 = new(x1,  0.50f + yOffsetBot,  0.50f);
            Vector3 b2 = new(x1,  0.50f + yOffsetTop,  0.50f), b3 = new(x0,  0.50f + yOffsetTop,  0.50f);

            AddQuad(dst, f3, b3, b2, f2, mat, shade);
            AddQuad(dst, b0, b1, f1, f0, mat, (byte)(shade - 32));
            AddQuad(dst, f0, f3, f2, f1, mat, (byte)(shade - 14));
            AddQuad(dst, b1, b2, b3, b0, mat, (byte)(shade - 14));
            AddQuad(dst, b0, f0, f3, b3, mat, (byte)(shade - 18));
            AddQuad(dst, f1, b1, b2, f2, mat, (byte)(shade - 18));
        }

        private static void AddSlopedWedgeZ(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float yMin, float yMax,
            byte mat,
            byte shade)
        {
            Vector3 bFL = new(x0, yMin, -0.50f), bFR = new(x1, yMin, -0.50f);
            Vector3 bBL = new(x0, yMin,  0.50f), bBR = new(x1, yMin,  0.50f);
            Vector3 tBL = new(x0, yMax,  0.50f), tBR = new(x1, yMax,  0.50f);

            AddQuad(dst, bFL, tBL, tBR, bFR, mat, shade);
            AddQuad(dst, bBR, tBR, tBL, bBL, mat, (byte)(shade - 18));
            AddQuad(dst, bBL, bFL, bFR, bBR, mat, (byte)(shade - 40));
            dst.Add(new LocalKitTriangle(bFL, bBL, tBL, mat, (byte)(shade - 12)));
            dst.Add(new LocalKitTriangle(bFR, tBR, bBR, mat, (byte)(shade - 12)));
        }

        private static void AddPitchedPrismX(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float yBase, float yApex,
            float halfZ,
            byte mat,
            byte shade)
        {
            Vector3 lS = new(x0, yBase, -halfZ), rS = new(x1, yBase, -halfZ);
            Vector3 lN = new(x0, yBase,  halfZ), rN = new(x1, yBase,  halfZ);
            Vector3 lA = new(x0, yApex,  0f),    rA = new(x1, yApex,  0f);

            AddQuad(dst, lS, lA, rA, rS, mat, shade);
            AddQuad(dst, rN, rA, lA, lN, mat, (byte)(shade - 10));
            AddQuad(dst, lN, lS, rS, rN, mat, (byte)(shade - 42));
            dst.Add(new LocalKitTriangle(lS, lN, lA, mat, (byte)(shade - 16)));
            dst.Add(new LocalKitTriangle(rS, rA, rN, mat, (byte)(shade - 16)));
        }

        private static void AddSlantedClapboard(List<LocalKitTriangle> dst, float y0, float y1, byte mat, byte shade)
        {
            AddBox(dst, -0.50f, 0.50f, y0, y1, -0.28f, 0.28f, mat, shade);
        }
    }
}
