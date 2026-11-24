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
    /// Devir #087: Yuksek Performansli (60+ FPS) "Kit-Stamp Voxel Meshing" Sablon Bankasi.
    /// - ShapeID 21..62 x 8 Alt-Varyant (rotationState = (yaw &amp; 3) | (variant &lt;&lt; 2)) mimari ve ic/dis mekan sekilleri.
    /// - 6-yonlu komsu yuzey gizleme (CullFaceBit + O(1) InverseRotateFaceMaskYaw) ile bitisik duvar/doseme yuzeylerinde 0 ic ucgen.
    /// - Yakin (LOD0 &lt;= 3 chunk) ve Uzak (LOD1 &gt;= 4 chunk, distantExteriorOnly) cift kademeli sablon ile ~35x ucgen azalmasi.
    /// - Sifir bosluklu butunlesik 2.0x2.2m kapi, orta denizliksiz seffaf 1x2m pencere, 4-yon sacakli 45° cati,
    ///   sicak sivali ic duvar yuzeyi, 3B altin bugday/ekin, sokak feneri, duvar apligi, cumba konsolu ve degirmen kanadi.
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
            public readonly byte CullFaceBit;        // Lokal uzayda hangi komsu yuz doluysa elenecegi (FacePosX..FaceNegZ)
            public readonly bool AllowSkirtDrop;

            public LocalKitTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                byte overrideMaterialId,
                byte facetShade,
                byte overrideTemp = 0,
                bool allowSkirtDrop = false,
                byte cullFaceBit = 0)
            {
                A = a;
                B = b;
                C = c;
                OverrideMaterialId = overrideMaterialId;
                OverrideTemp = overrideTemp;
                FacetShade = facetShade;
                AllowSkirtDrop = allowSkirtDrop;
                CullFaceBit = cullFaceBit;
            }
        }

        private const int FirstKitShapeId = 21;
        private const int LastKitShapeId = 62;
        private const int KitCount = LastKitShapeId - FirstKitShapeId + 1;
        private const int VariantsPerKit = 8;
        private const int TotalSlots = KitCount * VariantsPerKit;

        private static readonly object SyncRoot = new();
        private static LocalKitTriangle[][]? _nearTemplates;
        private static LocalKitTriangle[][]? _distantTemplates;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReloadDisabled()
        {
            lock (SyncRoot)
            {
                _nearTemplates = null;
                _distantTemplates = null;
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
            List<FacetedVoxelTriangle> outTriangles,
            byte occludedFaceMask = 0,
            bool distantExteriorOnly = false)
        {
            if (shapeId < FirstKitShapeId || shapeId > LastKitShapeId)
            {
                return false;
            }

            // Uzak (> 64m / >= 4 chunk) binalarin kapali ic mekan esyalari (49..61) 0 ucgen uretir!
            if (distantExteriorOnly && ArchitecturalShapeCatalog.IsIndoorCulledDistantPropKit(shapeId))
            {
                return true;
            }

            int rawRot = rotationState & 0x1F;
            byte effectiveYaw = (byte)(rawRot & 3);
            int variant = (rawRot >> 2) & 7;

            EnsureTemplatesInitialized(out LocalKitTriangle[][] nearBank, out LocalKitTriangle[][] distantBank);
            int slotIndex = (shapeId - FirstKitShapeId) * VariantsPerKit + variant;
            LocalKitTriangle[] tris = (distantExteriorOnly ? distantBank : nearBank)[slotIndex];

            if (tris.Length == 0)
            {
                return true;
            }

            byte localOccluded = occludedFaceMask != 0
                ? InverseRotateFaceMaskYaw(occludedFaceMask, effectiveYaw)
                : (byte)0;

            for (int i = 0; i < tris.Length; i++)
            {
                ref readonly LocalKitTriangle t = ref tris[i];
                if (localOccluded != 0 && (t.CullFaceBit & localOccluded) != 0)
                {
                    continue;
                }

                Vector3 la = t.A;
                Vector3 lb = t.B;
                Vector3 lc = t.C;
                if (skirtDrop > 0.001f && t.AllowSkirtDrop)
                {
                    if (la.Y <= -0.46f) la.Y -= skirtDrop;
                    if (lb.Y <= -0.46f) lb.Y -= skirtDrop;
                    if (lc.Y <= -0.46f) lc.Y -= skirtDrop;
                }

                Vector3 wa = ArchitecturalShapeCatalog.TransformLocalPoint(la, cellCenter, effectiveYaw);
                Vector3 wb = ArchitecturalShapeCatalog.TransformLocalPoint(lb, cellCenter, effectiveYaw);
                Vector3 wc = ArchitecturalShapeCatalog.TransformLocalPoint(lc, cellCenter, effectiveYaw);

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

        /// <summary>Dunya uzayindaki 6-bit komsu dolu yuz maskesini O(1) olarak lokal kit uzayina cevirir.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte InverseRotateFaceMaskYaw(byte worldMask, byte yaw)
        {
            byte yBits = (byte)(worldMask & (ArchitecturalShapeCatalog.FacePosY | ArchitecturalShapeCatalog.FaceNegY));
            return (yaw & 3) switch
            {
                0 => worldMask,
                1 => (byte)(yBits
                    | ((worldMask & ArchitecturalShapeCatalog.FaceNegZ) != 0 ? ArchitecturalShapeCatalog.FacePosX : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FacePosZ) != 0 ? ArchitecturalShapeCatalog.FaceNegX : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FacePosX) != 0 ? ArchitecturalShapeCatalog.FacePosZ : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FaceNegX) != 0 ? ArchitecturalShapeCatalog.FaceNegZ : 0)),
                2 => (byte)(yBits
                    | ((worldMask & ArchitecturalShapeCatalog.FaceNegX) != 0 ? ArchitecturalShapeCatalog.FacePosX : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FacePosX) != 0 ? ArchitecturalShapeCatalog.FaceNegX : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FaceNegZ) != 0 ? ArchitecturalShapeCatalog.FacePosZ : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FacePosZ) != 0 ? ArchitecturalShapeCatalog.FaceNegZ : 0)),
                _ => (byte)(yBits
                    | ((worldMask & ArchitecturalShapeCatalog.FacePosZ) != 0 ? ArchitecturalShapeCatalog.FacePosX : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FaceNegZ) != 0 ? ArchitecturalShapeCatalog.FaceNegX : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FaceNegX) != 0 ? ArchitecturalShapeCatalog.FacePosZ : 0)
                    | ((worldMask & ArchitecturalShapeCatalog.FacePosX) != 0 ? ArchitecturalShapeCatalog.FaceNegZ : 0)),
            };
        }

        private static void EnsureTemplatesInitialized(
            out LocalKitTriangle[][] nearBank,
            out LocalKitTriangle[][] distantBank)
        {
            LocalKitTriangle[][]? n = _nearTemplates;
            LocalKitTriangle[][]? d = _distantTemplates;
            if (n != null && d != null)
            {
                nearBank = n;
                distantBank = d;
                return;
            }

            lock (SyncRoot)
            {
                if (_nearTemplates != null && _distantTemplates != null)
                {
                    nearBank = _nearTemplates;
                    distantBank = _distantTemplates;
                    return;
                }

                string? glbDir = TryLocateModularGlbDirectory();
                var builtNear = new LocalKitTriangle[TotalSlots][];
                var builtDist = new LocalKitTriangle[TotalSlots][];
                var scratchNear = new List<LocalKitTriangle>(256);
                var scratchDist = new List<LocalKitTriangle>(64);

                for (int i = 0; i < KitCount; i++)
                {
                    byte shapeId = (byte)(FirstKitShapeId + i);
                    int baseSlot = i * VariantsPerKit;

                    if (!HasDistinctSubVariants(shapeId))
                    {
                        scratchNear.Clear();
                        scratchDist.Clear();
                        BuildShapeTemplate(shapeId, 0, glbDir, distantMode: false, scratchNear);
                        BuildShapeTemplate(shapeId, 0, glbDir, distantMode: true, scratchDist);
                        LocalKitTriangle[] arrN = scratchNear.ToArray();
                        LocalKitTriangle[] arrD = scratchDist.ToArray();
                        for (int v = 0; v < VariantsPerKit; v++)
                        {
                            builtNear[baseSlot + v] = arrN;
                            builtDist[baseSlot + v] = arrD;
                        }
                    }
                    else
                    {
                        for (int v = 0; v < VariantsPerKit; v++)
                        {
                            scratchNear.Clear();
                            scratchDist.Clear();
                            BuildShapeTemplate(shapeId, v, glbDir, distantMode: false, scratchNear);
                            BuildShapeTemplate(shapeId, v, glbDir, distantMode: true, scratchDist);
                            builtNear[baseSlot + v] = scratchNear.ToArray();
                            builtDist[baseSlot + v] = scratchDist.ToArray();
                        }
                    }
                }

                _distantTemplates = builtDist;
                _nearTemplates = builtNear;
                nearBank = builtNear;
                distantBank = builtDist;
            }
        }

        private static bool HasDistinctSubVariants(byte shapeId) => shapeId is
            ArchitecturalShapeCatalog.KitTimberPostCorner or
            ArchitecturalShapeCatalog.KitRusticatedStoneWall or
            ArchitecturalShapeCatalog.KitBrickCourseWall or
            ArchitecturalShapeCatalog.KitRoofEaveOverhang45 or
            ArchitecturalShapeCatalog.KitGableWall45 or
            ArchitecturalShapeCatalog.KitChimneyFlue or
            ArchitecturalShapeCatalog.KitShutteredWindow or
            ArchitecturalShapeCatalog.KitArchedStoneWindow or
            ArchitecturalShapeCatalog.KitDoorFrameJamb or
            ArchitecturalShapeCatalog.KitDoorArchLintel or
            ArchitecturalShapeCatalog.KitHingedDoorClosed or
            ArchitecturalShapeCatalog.KitHingedDoorOpen or
            ArchitecturalShapeCatalog.KitWellAndFence;

        private static string? TryLocateModularGlbDirectory()
        {
#if UNITY_5_3_OR_NEWER
            string unityPath = Path.Combine(UnityEngine.Application.dataPath, "Art", "Generated", "Structures_Modular");
            if (Directory.Exists(unityPath))
            {
                return unityPath;
            }
#endif
            string? probe = AppContext.BaseDirectory;
            for (int depth = 0; depth < 7 && !string.IsNullOrEmpty(probe); depth++)
            {
                string cand = Path.Combine(probe, "Assets", "Art", "Generated", "Structures_Modular");
                if (Directory.Exists(cand)) return cand;
                probe = Directory.GetParent(probe)?.FullName;
            }

            return null;
        }

        private static void BuildShapeTemplate(
            byte shapeId,
            int variant,
            string? glbDir,
            bool distantMode,
            List<LocalKitTriangle> dst)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;
            const byte FPosZ = ArchitecturalShapeCatalog.FacePosZ;
            const byte CullWallEndsXY = FPosX | FNegX | FPosY | FNegY;

            switch (shapeId)
            {
                // =============================================================
                // A) DUVAR, KUTUK, KARKAS, KESME TAS, TUGLA VE SUTUNLAR (21..33)
                // =============================================================
                case ArchitecturalShapeCatalog.KitChinkedLogWall:
                    // Gercek koyu kabuklu yuvarlak tomruk duvar: ince koyu yosun/derz cekirdegi + 2 adet belirgin disari tasan silindirik kutuk!
                    if (distantMode)
                    {
                        AddCullableBox(dst, -0.50f, 0.50f, -0.50f,  0.00f, -0.24f, 0.20f, 0, 206, CullWallEndsXY | FPosZ, skirt: true);
                        AddCullableBox(dst, -0.50f, 0.50f,  0.00f,  0.50f, -0.25f, 0.20f, 0, 218, CullWallEndsXY | FPosZ);
                    }
                    else
                    {
                        AddCullableBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.13f, 0.13f, MaterialIdCatalog.TreatedTimberBeam, 132, CullWallEndsXY, skirt: true);
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f, -0.25f, 0.0f, 0.255f, 8, 0, 212, capEnds: true, endCapMat: MaterialIdCatalog.WoodPlank, cullEndCapsX: true);
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f,  0.25f, 0.0f, 0.255f, 8, 0, 222, capEnds: true, endCapMat: MaterialIdCatalog.WoodPlank, cullEndCapsX: true);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitNotchedLogCorner:
                    // Kesisen ve koseden +10cm disari tasan gercek kertikli (saddle-notch) yuvarlak kose tomruk bogumu!
                    if (distantMode)
                    {
                        AddCullableBox(dst, -0.56f, 0.56f, -0.50f, 0.50f, -0.56f, 0.56f, 0, 212, FPosY | FNegY, skirt: true);
                    }
                    else
                    {
                        AddCullableBox(dst, -0.28f, 0.28f, -0.50f, 0.50f, -0.28f, 0.28f, MaterialIdCatalog.TreatedTimberBeam, 140, FPosY | FNegY, skirt: true);
                        AddHorizontalCylinderX(dst, -0.60f, 0.60f, -0.25f, 0.0f, 0.25f, 8, 0, 214, capEnds: true, endCapMat: MaterialIdCatalog.WoodPlank);
                        AddHorizontalCylinderZ(dst, -0.60f, 0.60f,  0.25f, 0.0f, 0.25f, 8, 0, 222, capEnds: true, endCapMat: MaterialIdCatalog.WoodPlank);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitTimberFrameWall:
                    // Tudor/Ortacag ahsap karkas: Dista siva + cikintili koyu mese dikme/kusak, icte (+Z) sicak krem siva + ahsap supurgelik!
                    BuildPlasteredInteriorAndCoreWall(dst, 0, 216, distantMode, skirt: true);
                    AddFrontReliefBox(dst, -0.50f, 0.50f, -0.50f, -0.36f, -0.28f, -0.21f, MaterialIdCatalog.TreatedTimberBeam, 178, CullWallEndsXY, skirt: true);
                    AddFrontReliefBox(dst, -0.50f, 0.50f,  0.36f,  0.50f, -0.28f, -0.21f, MaterialIdCatalog.TreatedTimberBeam, 184, CullWallEndsXY);
                    if (!distantMode)
                    {
                        AddFrontReliefBox(dst, -0.50f, -0.37f, -0.36f, 0.36f, -0.27f, -0.21f, MaterialIdCatalog.TreatedTimberBeam, 174, FNegX);
                        AddFrontReliefBox(dst,  0.37f,  0.50f, -0.36f, 0.36f, -0.27f, -0.21f, MaterialIdCatalog.TreatedTimberBeam, 174, FPosX);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitTimberBraceWall:
                    BuildPlasteredInteriorAndCoreWall(dst, 0, 216, distantMode, skirt: true);
                    AddFrontReliefBox(dst, -0.50f, 0.50f, -0.50f, -0.36f, -0.28f, -0.21f, MaterialIdCatalog.TreatedTimberBeam, 178, CullWallEndsXY, skirt: true);
                    AddFrontReliefBox(dst, -0.50f, 0.50f,  0.36f,  0.50f, -0.28f, -0.21f, MaterialIdCatalog.TreatedTimberBeam, 184, CullWallEndsXY);
                    AddStrut(dst, new Vector3(-0.42f, -0.38f, -0.24f), new Vector3(0.42f, 0.38f, -0.24f), 0.09f, 0.07f, MaterialIdCatalog.TreatedTimberBeam, 172);
                    break;

                case ArchitecturalShapeCatalog.KitTimberPostCorner:
                    if (variant == ArchitecturalShapeCatalog.TimberVarCeilingJoist)
                    {
                        // Tavan altinda gorunen sicak mese tasiyici tavan kirisi (2m aralikli, kafa mesafesi 3.74m!)
                        if (!distantMode)
                        {
                            AddCullableBox(dst, -0.50f, 0.50f, 0.24f, 0.50f, -0.14f, 0.14f, MaterialIdCatalog.TreatedTimberBeam, 184, FNegX | FPosX | FPosY);
                        }
                    }
                    else
                    {
                        AddCullableBox(dst, -0.33f, 0.33f, -0.50f, 0.50f, -0.33f, 0.33f, MaterialIdCatalog.TreatedTimberBeam, 184, FPosY | FNegY, skirt: true);
                        if (!distantMode)
                        {
                            AddCullableBox(dst, -0.37f, 0.37f, -0.06f, 0.06f, -0.37f, 0.37f, MaterialIdCatalog.IronTieBeamBlock, 152, 0);
                        }
                    }
                    break;

                case ArchitecturalShapeCatalog.KitAshlarQuoinBlock:
                    // Uzun-kisa disli kesme kose tasi (ic yuzler komsu duvarla gizlenir -> 8-14 ucgen!)
                    AddCullableBox(dst, -0.36f, 0.36f, -0.50f,  0.00f, -0.36f, 0.32f, 0, 222, FPosY | FNegY, skirt: true);
                    AddCullableBox(dst, -0.32f, 0.36f,  0.00f,  0.50f, -0.36f, 0.36f, 0, 230, FPosY | FNegY);
                    break;

                case ArchitecturalShapeCatalog.KitRusticatedStoneWall:
                case ArchitecturalShapeCatalog.KitBrickCourseWall:
                    // StratocraftUberShader zaten harc-tugla/tas derzlerini piksellerde urettigi icin
                    // 84 ucgenlik mikro-tugla kutulari yerine:
                    // - Dista (-Z) hafif silmeli gercek tugla/tas yuzeyi (2-6 ucgen!)
                    // - Icte (+Z) sicak krem ic cephe sivasi + mese supurgelik (variant==1 ic bolme duvarinda cift yuz sivali!)
                    if (variant == 1)
                    {
                        if (!distantMode)
                        {
                            AddCullableBox(dst, -0.50f, 0.50f, -0.50f, 0.50f, -0.18f, 0.18f, MaterialIdCatalog.LimeWashPlaster, 226, CullWallEndsXY, skirt: true);
                            AddCullableBox(dst, -0.50f, 0.50f, -0.50f, -0.36f, -0.21f, 0.21f, MaterialIdCatalog.TreatedTimberBeam, 176, CullWallEndsXY, skirt: true);
                        }
                    }
                    else
                    {
                        BuildPlasteredInteriorAndCoreWall(dst, 0, 218, distantMode, skirt: true);
                        if (!distantMode)
                        {
                            // Dista ince mimari tas/tugla damlalik kusagi (yalnizca on -Z yuzunde +4 ucgen)
                            AddFrontReliefBox(dst, -0.50f, 0.50f, -0.50f, -0.38f, -0.28f, -0.24f, 0, 204, CullWallEndsXY, skirt: true);
                        }
                    }
                    break;

                case ArchitecturalShapeCatalog.KitAdobeCurvedWall:
                    BuildPlasteredInteriorAndCoreWall(dst, 0, 214, distantMode, skirt: true);
                    if (!distantMode)
                    {
                        AddHorizontalCylinderZ(dst, -0.42f, -0.20f, 0.30f, 0.0f, 0.085f, 6, MaterialIdCatalog.TreatedTimberBeam, 178, capEnds: true);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitPlankFramedWall:
                    BuildPlasteredInteriorAndCoreWall(dst, 0, 212, distantMode, skirt: true);
                    if (!distantMode)
                    {
                        AddFrontReliefBox(dst, -0.50f, 0.50f, -0.50f, -0.02f, -0.28f, -0.23f, 0, 206, CullWallEndsXY, skirt: true);
                        AddFrontReliefBox(dst, -0.50f, 0.50f,  0.00f,  0.48f, -0.28f, -0.23f, 0, 218, CullWallEndsXY);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitClassicalColumnBase:
                    AddCullableBox(dst, -0.44f, 0.44f, -0.50f, -0.22f, -0.44f, 0.44f, 0, 198, FNegY, skirt: true);
                    AddVerticalCylinderY(dst, -0.22f, 0.50f, 0f, 0f, 0.38f, 0.31f, distantMode ? 6 : 10, 0, 220, cullCapsY: true);
                    break;

                case ArchitecturalShapeCatalog.KitClassicalColumnShaft:
                    AddVerticalCylinderY(dst, -0.50f, 0.50f, 0f, 0f, 0.31f, 0.30f, distantMode ? 6 : 10, 0, 222, cullCapsY: true);
                    break;

                case ArchitecturalShapeCatalog.KitClassicalColumnCap:
                    AddVerticalCylinderY(dst, -0.50f, 0.05f, 0f, 0f, 0.30f, 0.42f, distantMode ? 6 : 10, 0, 218, cullCapsY: true);
                    AddCullableBox(dst, -0.48f, 0.48f, 0.05f, 0.50f, -0.48f, 0.48f, 0, 232, FPosY);
                    break;

                // =============================================================
                // B) 3B MERTEKLI, KIREMITLI / PEDAVRALI CATILAR & BACA (34..40)
                // =============================================================
                case ArchitecturalShapeCatalog.KitRoofClayTile45:
                    BuildSculptedRoofSlope45(dst, isClayBarrelTile: true, hasOverhangTail: false, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitRoofShingle45:
                    BuildSculptedRoofSlope45(dst, isClayBarrelTile: false, hasOverhangTail: false, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitRoofEaveOverhang45:
                    BuildSculptedRoofSlope45(
                        dst,
                        isClayBarrelTile: (variant & 1) == 0,
                        hasOverhangTail: true,
                        distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitRoofRidgeCrest:
                    AddPitchedPrismX(dst, -0.50f, 0.50f, -0.46f, 0.36f, 0.52f, 0, 224, cullEndsX: true);
                    if (!distantMode)
                    {
                        AddHorizontalCylinderX(dst, -0.50f, 0.50f, 0.36f, 0.0f, 0.12f, 6, 0, 236, capEnds: false, cullEndCapsX: true);
                    }
                    break;

                case ArchitecturalShapeCatalog.KitGableWall45:
                    BuildSmoothGableAndBargeboard45(dst, variant, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitChimneyFlue:
                    // Seker cubugu beyaz seritler kaldirildi! Dogal isli tas/tugla baca govdesi (v=0) + yalnizca en tepede tas sapka (v=1)
                    if (variant == ArchitecturalShapeCatalog.ChimneyVarCrownCap)
                    {
                        AddCullableBox(dst, -0.38f, 0.38f, -0.50f, 0.18f, -0.38f, 0.38f, 0, 186, FNegY, skirt: true);
                        AddCullableBox(dst, -0.46f, 0.46f,  0.18f, 0.36f, -0.46f, 0.46f, MaterialIdCatalog.StoneBrick, 198, 0);
                        AddCullableBox(dst, -0.24f, 0.24f,  0.36f, 0.50f, -0.24f, 0.24f, MaterialIdCatalog.Basalt, 110, 0);
                    }
                    else
                    {
                        AddCullableBox(dst, -0.37f, 0.37f, -0.50f, 0.50f, -0.37f, 0.37f, 0, 184, FPosY | FNegY, skirt: true);
                        if (!distantMode)
                        {
                            AddCullableBox(dst, -0.40f, 0.40f, -0.06f, 0.04f, -0.40f, 0.40f, MaterialIdCatalog.StoneBrick, 168, 0);
                        }
                    }
                    break;

                case ArchitecturalShapeCatalog.KitDomeRibVault:
                    AddSlopedWedgeZ(dst, -0.50f, 0.50f, -0.50f, 0.50f, 0, 222, cullSidesX: true);
                    if (!distantMode)
                    {
                        AddStrut(dst, new Vector3(0f, -0.46f, -0.48f), new Vector3(0f, 0.52f, 0.48f), 0.16f, 0.12f, MaterialIdCatalog.DressedMarbleBlock, 238);
                    }
                    break;

                // =============================================================
                // C) SEFFAF HEMZEMIN 1x2m PENCERELER & BUTUNLESIK 2.0x2.2m KAPILAR (41..48)
                // =============================================================
                case ArchitecturalShapeCatalog.KitShutteredWindow:
                    BuildCohesiveWindow3D(dst, variant, isArchedStone: false, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitArchedStoneWindow:
                    BuildCohesiveWindow3D(dst, variant, isArchedStone: true, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitDoorFrameJamb:
                    BuildFullWallWithDoorPilasterJamb(dst, isRightSide: (variant & 1) != 0, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitDoorArchLintel:
                    BuildArchedTransomDoorLintel(dst, isRightHalf: (variant & 1) != 0, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitHingedDoorClosed:
                    BuildCohesiveDoorLeaf3D(dst, variant, isOpen: false, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitHingedDoorOpen:
                    BuildCohesiveDoorLeaf3D(dst, variant, isOpen: true, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitWideTimberStair:
                    BuildOpenRiserTimberStair3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.KitBalconyBalustrade:
                    BuildBalustrade3D(dst, distantMode);
                    break;

                // =============================================================
                // D) %100 3B HEYKELLENMIS COK-MALZEMELI IC & DIS MEKAN ESYALARI (49..62)
                // =============================================================
                case ArchitecturalShapeCatalog.KitDiningTable:
                    if (!distantMode) BuildDiningTable3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitChairWithBack:
                    if (!distantMode) BuildChair3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBedHeadboard:
                    if (!distantMode) BuildBedHead3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBedFoot:
                    if (!distantMode) BuildBedFoot3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitBookshelf:
                    if (!distantMode) BuildBookshelf3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitFireplaceHearth:
                    if (!distantMode) BuildFireplaceHearth3D(dst, glbDir);
                    break;

                case ArchitecturalShapeCatalog.KitKitchenCounter:
                    if (!distantMode) BuildKitchenCounter3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitStorageChest:
                    if (!distantMode) BuildStorageChest3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitWoodenBarrel:
                    if (!distantMode) BuildWoodenBarrel3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitRoyalThrone:
                    if (!distantMode) BuildRoyalThrone3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitAnvilOnStump:
                    if (!distantMode) BuildAnvilOnStump3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitSawhorseBench:
                    if (!distantMode) BuildSawhorseBench3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitWeaponRack:
                    if (!distantMode) BuildWeaponRack3D(dst);
                    break;

                case ArchitecturalShapeCatalog.KitWellAndFence:
                    BuildUtilityPropVariant3D(dst, variant, distantMode);
                    break;
            }
        }

        // =====================================================================
        // DUVAR, CATI, PENCERE, KAPI VE SEHIR/TARLA PROPLARI URETICILERI
        // =====================================================================

        /// <summary>
        /// Dis yuzu (-Z) vokselin kendi cephe malzemesi (tugla/tas/siva), ic yuzu (+Z) ise sicak krem ic siva
        /// ve koyu mese supurgelik olan Yuksek-FPS (komsu kenarlari %100 elenen!) ana duvar govdesi.
        /// </summary>
        private static void BuildPlasteredInteriorAndCoreWall(
            List<LocalKitTriangle> dst,
            byte frontMatOverride,
            byte frontShade,
            bool distantMode,
            bool skirt)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;
            const byte FPosZ = ArchitecturalShapeCatalog.FacePosZ;
            const byte FNegZ = ArchitecturalShapeCatalog.FaceNegZ;

            float x0 = -0.50f, x1 = 0.50f;
            float y0 = -0.50f, y1 = 0.50f;
            float z0 = -0.24f, z1 = 0.24f;

            Vector3 p000 = new(x0, y0, z0), p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p001 = new(x0, y0, z1);
            Vector3 p010 = new(x0, y1, z0), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1), p011 = new(x0, y1, z1);

            // On dis cephe (-Z): her mesafede cizilir (yalnizca onunde tam dolu blok varsa gizlenir)
            AddQuad(dst, p000, p010, p110, p100, frontMatOverride, frontShade, 0, skirt, FNegZ);

            // Ust/Alt/Sol/Sag kenar kapaklari: komsu duvar veya doseme varsa 0 ucgen!
            AddQuad(dst, p010, p011, p111, p110, frontMatOverride, (byte)Math.Min(245, frontShade + 12), 0, false, FPosY);
            AddQuad(dst, p001, p000, p100, p101, frontMatOverride, (byte)Math.Max(115, frontShade - 44), 0, skirt, FNegY);
            AddQuad(dst, p001, p011, p010, p000, frontMatOverride, (byte)Math.Max(125, frontShade - 10), 0, skirt, FNegX);
            AddQuad(dst, p100, p110, p111, p101, frontMatOverride, (byte)Math.Max(125, frontShade - 10), 0, skirt, FPosX);

            // Arka ic mekan yuzeyi (+Z): Uzak (> 64m) kapali binalarda hic cizilmez! Yakinda sicak krem siva + mese supurgelik!
            if (!distantMode)
            {
                Vector3 m001 = new(x0, -0.35f, z1), m101 = new(x1, -0.35f, z1);
                AddQuad(dst, p101, m101, m001, p001, MaterialIdCatalog.TreatedTimberBeam, 178, 0, skirt, FPosZ);
                AddQuad(dst, m101, p111, p011, m001, MaterialIdCatalog.LimeWashPlaster, 232, 0, false, FPosZ);
            }
        }

        private static void BuildSculptedRoofSlope45(
            List<LocalKitTriangle> dst,
            bool isClayBarrelTile,
            bool hasOverhangTail,
            bool distantMode)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;

            float zMin = hasOverhangTail ? -0.62f : -0.50f;
            float yMin = hasOverhangTail ? -0.62f : -0.50f;

            // Ana 45° egimli sizdirmaz cati plakasi (Ust yuz: kiremit/pedavra, Alt yuz: sicak ahsap tavan kaplamasi!)
            AddSlopedRoofDeck(dst, -0.50f, 0.50f, zMin, 0.50f, yMin, 0.50f, 0.10f, 0, MaterialIdCatalog.WoodPlank, 222, distantMode);

            if (distantMode)
            {
                return;
            }

            // Yakin mesafede sacak altinda gorunen oymali mese mertek uclari
            if (hasOverhangTail)
            {
                AddStrut(dst, new Vector3(-0.36f, yMin - 0.04f, zMin + 0.02f), new Vector3(-0.36f, 0.40f, 0.46f), 0.11f, 0.11f, MaterialIdCatalog.TreatedTimberBeam, 172);
                AddStrut(dst, new Vector3( 0.36f, yMin - 0.04f, zMin + 0.02f), new Vector3( 0.36f, 0.40f, 0.46f), 0.11f, 0.11f, MaterialIdCatalog.TreatedTimberBeam, 172);
            }

            if (isClayBarrelTile)
            {
                // 3 sira hafif oluklu Akdeniz/Alaturka kiremit sirti (yalnizca ust-yan gorunur yuzler!)
                float[] xs = { -0.33f, 0.0f, 0.33f };
                for (int i = 0; i < xs.Length; i++)
                {
                    AddRoofTileRidgeStrip(dst, xs[i], 0.16f, zMin, 0.50f, yMin + 0.10f, 0.60f, 0.07f, 0, (byte)(216 + (i & 1) * 14));
                }
            }
            else
            {
                // 2 kademeli bindirmeli (overlapping) sedir pedavra / zona kademesi
                for (int s = 0; s < 2; s++)
                {
                    float t0 = s * 0.50f;
                    float t1 = t0 + 0.54f;
                    float z0 = zMin + t0 * (0.50f - zMin);
                    float z1 = MathF.Min(0.50f, zMin + t1 * (0.50f - zMin));
                    float y0 = yMin + 0.09f + t0 * (0.50f - yMin);
                    float y1 = yMin + 0.12f + t1 * (0.50f - yMin);
                    Vector3 a0 = new(-0.50f, y0 + 0.04f, z0), a1 = new(0.50f, y0 + 0.04f, z0);
                    Vector3 b1 = new( 0.50f, y1, z1),          b0 = new(-0.50f, y1, z1);
                    AddQuad(dst, a0, b0, b1, a1, 0, (byte)(208 + s * 14));
                    Vector3 lip0 = new(-0.50f, y0 - 0.02f, z0), lip1 = new(0.50f, y0 - 0.02f, z0);
                    AddQuad(dst, lip0, a0, a1, lip1, 0, 176);
                }
            }
            _ = FPosX; _ = FNegX;
        }

        private static void BuildSmoothGableAndBargeboard45(List<LocalKitTriangle> dst, int variant, bool distantMode)
        {
            bool isShingle = (variant & 1) != 0;
            bool isPureOverhang = variant >= ArchitecturalShapeCatalog.GableVarClayBargeOverhang;
            byte roofMat = isShingle ? MaterialIdCatalog.TimberShingleRoof : MaterialIdCatalog.RoofClayTile;

            if (!isPureOverhang)
            {
                // 1) Puruzsuz 45° egimli ucgen kalkan duvar kamasi (vokselin kendi duvar malzemesiyle: kutuk/tugla/tas!)
                // Basamak goruntusu %100 sifirlanir!
                AddSlopedWedgeZ(dst, -0.24f, 0.24f, -0.50f, 0.50f, 0, 214, cullSidesX: false);
            }

            // 2) Kalkan duvarin tam ustunu kapatan ve disari tasan 45° cati plakasi + oymali mese alin tahtasi (bargeboard)!
            AddSlopedRoofDeck(dst, -0.50f, 0.50f, -0.54f, 0.50f, -0.50f, 0.50f, 0.11f, roofMat, MaterialIdCatalog.TreatedTimberBeam, 224, distantMode);
            if (!distantMode)
            {
                // Alin tahtasi altinda yatay asik kirisi ucu
                AddHorizontalCylinderX(dst, -0.52f, 0.52f, -0.06f, 0.02f, 0.09f, 6, MaterialIdCatalog.TreatedTimberBeam, 178, capEnds: true);
            }
        }

        private static void BuildCohesiveWindow3D(
            List<LocalKitTriangle> dst,
            int variant,
            bool isArchedStone,
            bool distantMode)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;

            bool isLower = variant == ArchitecturalShapeCatalog.WinVarLowerSill;
            bool isUpper = variant == ArchitecturalShapeCatalog.WinVarUpperHead;
            // variant >= 2: tek blokluk 1x1m tavan arasi / kalkan duvar penceresi (hem alt denizlik hem ust lento var)
            bool hasSill = isLower || (!isLower && !isUpper);
            bool hasHeader = isUpper || (!isLower && !isUpper);

            byte frameMat = isArchedStone ? MaterialIdCatalog.DressedMarbleBlock : MaterialIdCatalog.TreatedTimberBeam;
            byte sillMat = isArchedStone ? MaterialIdCatalog.DressedMarbleBlock : MaterialIdCatalog.CutAshlarStone;

            // Uzak (> 64m) LOD1 modunda: 8 ucgenlik keskin dis cephe cercevesi + koyu yansimali cam silueti!
            if (distantMode)
            {
                const byte FNegZ = ArchitecturalShapeCatalog.FaceNegZ;
                float zF = -0.24f;
                AddQuad(dst, new(-0.50f, -0.50f, zF), new(-0.50f, 0.50f, zF), new(-0.34f, 0.50f, zF), new(-0.34f, -0.50f, zF), 0, 214, 0, false, FNegZ);
                AddQuad(dst, new( 0.34f, -0.50f, zF), new( 0.34f, 0.50f, zF), new( 0.50f, 0.50f, zF), new( 0.50f, -0.50f, zF), 0, 214, 0, false, FNegZ);
                float oy0 = hasSill ? -0.34f : -0.50f;
                float oy1 = hasHeader ? 0.30f : 0.50f;
                if (hasSill)
                {
                    AddQuad(dst, new(-0.36f, -0.50f, zF - 0.04f), new(-0.36f, oy0, zF - 0.04f), new(0.36f, oy0, zF - 0.04f), new(0.36f, -0.50f, zF - 0.04f), sillMat, 226);
                }
                if (hasHeader)
                {
                    AddQuad(dst, new(-0.36f, oy1, zF - 0.02f), new(-0.36f, 0.50f, zF - 0.02f), new(0.36f, 0.50f, zF - 0.02f), new(0.36f, oy1, zF - 0.02f), frameMat, 222);
                }
                AddQuad(dst, new(-0.34f, oy0, zF + 0.04f), new(-0.34f, oy1, zF + 0.04f), new(0.34f, oy1, zF + 0.04f), new(0.34f, oy0, zF + 0.04f), MaterialIdCatalog.LeadedGlassPane, 140);
                return;
            }

            // 1) Sol ve sag duvar yanaklari (z = -0.24..+0.24 -> ic duvarla %100 HEMZEMIN, sifir kaba ic cikinti!)
            byte leftCheekCull = (byte)(FNegX | (hasHeader ? FPosY : 0) | (hasSill ? FNegY : 0));
            byte rightCheekCull = (byte)(FPosX | (hasHeader ? FPosY : 0) | (hasSill ? FNegY : 0));
            AddWallCheekBox(dst, -0.50f, -0.34f, -0.50f, 0.50f, 0, leftCheekCull, distantMode);
            AddWallCheekBox(dst,  0.34f,  0.50f, -0.50f, 0.50f, 0, rightCheekCull, distantMode);

            float openY0 = hasSill ? -0.36f : -0.50f;
            float openY1 = hasHeader ? 0.30f : 0.50f;

            // 2) Alt Denizlik (Yalnizca alt yarida! Orta denizlik %100 kaldirildi -> tek parca 1x2m pencere!)
            if (hasSill)
            {
                AddCullableBox(dst, -0.36f, 0.36f, -0.50f, -0.36f, -0.32f, 0.24f, sillMat, 228, (byte)(FNegY | FNegX | FPosX));
            }

            // 3) Ust Lento veya Kilit Tasli Kemer (Yalnizca ust yarida!)
            if (hasHeader)
            {
                AddCullableBox(dst, -0.36f, 0.36f, 0.30f, 0.50f, -0.27f, 0.24f, frameMat, 222, (byte)(FPosY | FNegX | FPosX));
                if (isArchedStone && !distantMode)
                {
                    AddFrontReliefBox(dst, -0.10f, 0.10f, 0.26f, 0.50f, -0.32f, -0.26f, MaterialIdCatalog.DressedMarbleBlock, 238, FPosY);
                }
            }

            if (!distantMode)
            {
                // 4) Ince kursun/mese arti (+) cam kayitlari (aralari %100 acik seffaf -> disarisi net gorunur, yalnizca 16 ucgen!)
                byte muntinMat = isArchedStone ? MaterialIdCatalog.IronTieBeamBlock : MaterialIdCatalog.TreatedTimberBeam;
                AddCullableBox(dst, -0.022f, 0.022f, openY0, openY1, -0.035f, 0.035f, muntinMat, 165, (byte)(FPosY | FNegY));
                float crossY = isLower ? 0.08f : (isUpper ? -0.08f : 0.0f);
                AddCullableBox(dst, -0.34f, 0.34f, crossY - 0.022f, crossY + 0.022f, -0.035f, 0.035f, muntinMat, 168, (byte)(FNegX | FPosX));
            }

            // 5) Disa acilmis 3B ahsap panjur kanatlari (yalnizca on cephede!)
            if (!isArchedStone)
            {
                AddFrontReliefBox(dst, -0.49f, -0.34f, openY0 + 0.02f, openY1 - 0.02f, -0.31f, -0.24f, MaterialIdCatalog.WoodPlank, 202, 0);
                AddFrontReliefBox(dst,  0.34f,  0.49f, openY0 + 0.02f, openY1 - 0.02f, -0.31f, -0.24f, MaterialIdCatalog.WoodPlank, 202, 0);
            }
        }

        private static void BuildFullWallWithDoorPilasterJamb(
            List<LocalKitTriangle> dst,
            bool isRightSide,
            bool distantMode)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;

            // Tam genislikte (x = -0.50..+0.50) kati duvar + kapi acikligina bakan kenarda oymali sove pilasteri!
            // Boylece sove ile kapi kanadi arasinda 0.00m bosluk kalir!
            byte outerCull = (byte)((isRightSide ? FPosX : FNegX) | FPosY | FNegY);
            BuildPlasteredInteriorAndCoreWall(dst, 0, 216, distantMode, skirt: true);
            if (isRightSide)
            {
                // bx = +1: Kapi acikligi -X kenarindadir (-0.50)
                AddCullableBox(dst, -0.50f, -0.32f, -0.50f, 0.50f, -0.31f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 182, FPosY | FNegY, skirt: true);
            }
            else
            {
                // bx = -2: Kapi acikligi +X kenarindadir (+0.50)
                AddCullableBox(dst, 0.32f, 0.50f, -0.50f, 0.50f, -0.31f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 182, FPosY | FNegY, skirt: true);
            }
            _ = outerCull;
        }

        private static void BuildArchedTransomDoorLintel(
            List<LocalKitTriangle> dst,
            bool isRightHalf,
            bool distantMode)
        {
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            // dy = 3 kapi ustu: Altta kalin mese lento kirisi (2.16m net kapi yuksekligi), ortada acik vasistas isiklik, ustte tas kemer!
            AddCullableBox(dst, -0.50f, 0.50f, -0.34f, -0.18f, -0.28f, 0.26f, MaterialIdCatalog.TreatedTimberBeam, 182, 0);
            AddCullableBox(dst, -0.50f, 0.50f,  0.26f,  0.50f, -0.28f, 0.26f, 0, 222, FPosY);

            if (isRightHalf)
            {
                AddCullableBox(dst, 0.32f, 0.50f, -0.18f, 0.26f, -0.26f, 0.24f, 0, 214, 0);
                if (!distantMode)
                {
                    AddCullableBox(dst, -0.50f, -0.44f, -0.18f, 0.26f, -0.05f, 0.05f, MaterialIdCatalog.TreatedTimberBeam, 176, 0);
                }
            }
            else
            {
                AddCullableBox(dst, -0.50f, -0.32f, -0.18f, 0.26f, -0.26f, 0.24f, 0, 214, 0);
                if (!distantMode)
                {
                    AddCullableBox(dst, 0.44f, 0.50f, -0.18f, 0.26f, -0.05f, 0.05f, MaterialIdCatalog.TreatedTimberBeam, 176, 0);
                }
            }
        }

        private static void BuildCohesiveDoorLeaf3D(
            List<LocalKitTriangle> dst,
            int variant,
            bool isOpen,
            bool distantMode)
        {
            bool isRightLeaf = (variant & 2) != 0;
            bool isUpperHalf = (variant & 1) != 0;
            float yTop = isUpperHalf ? 0.66f : 0.50f; // dy=2 ust kanat dy=3 lentosuna (+0.66 = 2.16m) sifir boslukla uzanir!

            if (!isOpen)
            {
                // Kapali tek parca 3B mese kapi kanadi (alt ve ust yari birlestiginde TEK kapi, TEK kol, 2 mentese!)
                AddCullableBox(dst, -0.50f, 0.50f, -0.50f, yTop, -0.12f, -0.02f, MaterialIdCatalog.WoodPlank, 208, 0);
                if (distantMode) return;

                if (!isUpperHalf)
                {
                    // Alt baza kusagi + tek alt demir mentese + bel hizasinda (y=+0.38) simetrik demir halka kol!
                    AddCullableBox(dst, -0.48f, 0.48f, -0.46f, -0.32f, -0.15f, 0.01f, MaterialIdCatalog.TreatedTimberBeam, 178, 0);
                    float hx0 = isRightLeaf ? 0.08f : -0.50f;
                    float hx1 = isRightLeaf ? 0.50f : -0.08f;
                    AddCullableBox(dst, hx0, hx1, -0.34f, -0.28f, -0.17f, 0.02f, MaterialIdCatalog.IronTieBeamBlock, 148, 0);

                    float kx0 = isRightLeaf ? -0.42f : 0.30f;
                    float kx1 = isRightLeaf ? -0.30f : 0.42f;
                    AddCullableBox(dst, kx0, kx1, 0.34f, 0.46f, -0.19f, 0.05f, MaterialIdCatalog.IronTieBeamBlock, 168, 0);
                }
                else
                {
                    // Ust baslik kusagi + tek ust demir mentese (orta yerde kesinti veya 2. kapi kolu YOK!)
                    AddCullableBox(dst, -0.48f, 0.48f, yTop - 0.16f, yTop - 0.02f, -0.15f, 0.01f, MaterialIdCatalog.TreatedTimberBeam, 178, 0);
                    float hx0 = isRightLeaf ? 0.08f : -0.50f;
                    float hx1 = isRightLeaf ? 0.50f : -0.08f;
                    AddCullableBox(dst, hx0, hx1, 0.24f, 0.30f, -0.17f, 0.02f, MaterialIdCatalog.IronTieBeamBlock, 148, 0);
                }
            }
            else
            {
                // 90° iceriye (+Z) yan sovelere sifir boslukla yaslanmis acik kapi kanadi -> ortada 1.78m net gecis!
                float lx0 = isRightLeaf ? 0.39f : -0.50f;
                float lx1 = isRightLeaf ? 0.50f : -0.39f;
                AddCullableBox(dst, lx0, lx1, -0.50f, yTop, -0.18f, 0.68f, MaterialIdCatalog.WoodPlank, 206, 0);
                if (distantMode) return;

                float bx0 = isRightLeaf ? 0.37f : -0.50f;
                float bx1 = isRightLeaf ? 0.50f : -0.37f;
                if (!isUpperHalf)
                {
                    AddCullableBox(dst, bx0, bx1, -0.46f, -0.32f, -0.16f, 0.66f, MaterialIdCatalog.TreatedTimberBeam, 178, 0);
                    AddCullableBox(dst, bx0 - 0.01f, bx1 + 0.01f, 0.34f, 0.46f, 0.48f, 0.60f, MaterialIdCatalog.IronTieBeamBlock, 168, 0);
                }
                else
                {
                    AddCullableBox(dst, bx0, bx1, yTop - 0.16f, yTop - 0.02f, -0.16f, 0.66f, MaterialIdCatalog.TreatedTimberBeam, 178, 0);
                }
            }
        }

        private static void BuildOpenRiserTimberStair3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            if (!distantMode)
            {
                AddStrut(dst, new Vector3(-0.43f, -0.48f, -0.46f), new Vector3(-0.43f, 0.46f, 0.46f), 0.12f, 0.18f, MaterialIdCatalog.TreatedTimberBeam, 178);
                AddStrut(dst, new Vector3( 0.43f, -0.48f, -0.46f), new Vector3( 0.43f, 0.46f, 0.46f), 0.12f, 0.18f, MaterialIdCatalog.TreatedTimberBeam, 178);
            }
            AddCullableBox(dst, -0.50f, 0.50f, -0.10f, 0.00f, -0.50f, 0.04f, 0, 224, 0);
            AddCullableBox(dst, -0.50f, 0.50f,  0.40f, 0.50f, -0.02f, 0.50f, 0, 228, 0);
        }

        private static void BuildBalustrade3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            AddCullableBox(dst, -0.50f, 0.50f, -0.50f, -0.40f, -0.48f, -0.32f, 0, 196, 0);
            AddCullableBox(dst, -0.50f, 0.50f,  0.34f,  0.46f, -0.50f, -0.30f, MaterialIdCatalog.TreatedTimberBeam, 218, 0);
            if (distantMode)
            {
                AddCullableBox(dst, -0.06f, 0.06f, -0.40f, 0.34f, -0.44f, -0.36f, 0, 206, 0);
                return;
            }
            float[] xs = { -0.38f, 0.0f, 0.38f };
            for (int i = 0; i < xs.Length; i++)
            {
                AddVerticalCylinderY(dst, -0.40f, 0.34f, xs[i], -0.40f, 0.06f, 0.05f, 6, 0, 208, cullCapsY: true);
            }
        }

        private static void BuildUtilityPropVariant3D(List<LocalKitTriangle> dst, int variant, bool distantMode)
        {
            switch (variant)
            {
                case ArchitecturalShapeCatalog.UtilVarFence:
                    BuildSplitRailFence3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.UtilVarWellWindlass:
                    BuildWellWindlass3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.UtilVarGoldenWheat:
                    // Gercek 3B Altin Bugday Basaklari ve Sap Demeti (tarlalarda ruzgar acili, icinden yurulen ekin!)
                    BuildGoldenWheatCrop3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.UtilVarGreenCrops:
                    // Gercek 3B Yesil Sebze / Lahana-Pancar Yaprak Kumeleri
                    BuildGreenLeafyCrop3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.UtilVarStreetLantern:
                    // Dokme demir sokak feneri basligi + sicak parlayan amber isik cekirdegi (tempOverride = 228!)
                    BuildStreetLanternHead3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.UtilVarWallSconce:
                    // Ic mekan duvara monte sicak mesale/fener apligi (-Z duvara saplanir)
                    if (!distantMode) BuildInteriorWallSconce3D(dst);
                    break;

                case ArchitecturalShapeCatalog.UtilVarJettyCorbel:
                    // 2. kat Tudor cumba (jetty) ve sundurma altini tasiyan 45° oymali mese konsol payandasi!
                    BuildTimberJettyCorbel3D(dst, distantMode);
                    break;

                case ArchitecturalShapeCatalog.UtilVarWindmillSail:
                    // Yel degirmeni ahsap kol + krem yelken kafesi!
                    BuildWindmillSailLattice3D(dst, distantMode);
                    break;
            }
        }

        private static void BuildGoldenWheatCrop3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            if (distantMode)
            {
                // Uzaktan altin bugday tarlasi ortusu (4 capraz ucgen)
                AddCrossedBladeQuad(dst, -0.42f, 0.42f, -0.50f, 0.32f, MaterialIdCatalog.StrawChinkingBundle, 232);
                return;
            }

            // 5 adet hafif egimli bugday sapi + dolgun altin basaklar
            ReadOnlySpan<(float x, float z, float tiltX, float tiltZ, float h)> stalks = stackalloc (float, float, float, float, float)[5]
            {
                (-0.28f, -0.24f, -0.06f,  0.04f, 0.36f),
                ( 0.26f, -0.22f,  0.05f, -0.05f, 0.40f),
                ( 0.00f,  0.02f,  0.04f,  0.06f, 0.44f),
                (-0.24f,  0.26f, -0.05f, -0.04f, 0.38f),
                ( 0.28f,  0.24f,  0.06f,  0.05f, 0.34f)
            };

            for (int i = 0; i < stalks.Length; i++)
            {
                var (sx, sz, tx, tz, topY) = stalks[i];
                AddStrut(dst, new Vector3(sx, -0.50f, sz), new Vector3(sx + tx, topY - 0.14f, sz + tz), 0.045f, 0.045f, MaterialIdCatalog.StrawChinkingBundle, 212);
                AddCullableBox(dst, sx + tx - 0.055f, sx + tx + 0.055f, topY - 0.16f, topY + 0.06f, sz + tz - 0.055f, sz + tz + 0.055f, MaterialIdCatalog.StrawChinkingBundle, 242, 0);
            }
        }

        private static void BuildGreenLeafyCrop3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            AddCullableBox(dst, -0.26f, 0.26f, -0.50f, -0.14f, -0.26f, 0.26f, MaterialIdCatalog.Grass, 218, 0);
            if (!distantMode)
            {
                AddCrossedBladeQuad(dst, -0.38f, 0.38f, -0.48f, 0.04f, MaterialIdCatalog.Grass, 234);
            }
        }

        private static void BuildStreetLanternHead3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            // Alt demir bilezik + sicak parlayan fener cami + piramit demir sapka
            AddCullableBox(dst, -0.16f, 0.16f, -0.50f, -0.30f, -0.16f, 0.16f, MaterialIdCatalog.IronTieBeamBlock, 152, 0);
            AddGlowingBox(dst, -0.20f, 0.20f, -0.30f, 0.14f, -0.20f, 0.20f, MaterialIdCatalog.CampfireBlock, 250, tempOverride: 230);
            AddPitchedPrismX(dst, -0.28f, 0.28f, 0.14f, 0.42f, 0.28f, MaterialIdCatalog.IronTieBeamBlock, 164, cullEndsX: false);
            _ = distantMode;
        }

        private static void BuildInteriorWallSconce3D(List<LocalKitTriangle> dst)
        {
            // +Z ic duvara monte dovme demir kol + sicak kor mesale/fener alevi!
            AddCullableBox(dst, -0.08f, 0.08f, -0.18f, 0.10f, 0.32f, 0.48f, MaterialIdCatalog.IronTieBeamBlock, 150, 0);
            AddStrut(dst, new Vector3(0f, -0.12f, 0.40f), new Vector3(0f, 0.14f, 0.14f), 0.06f, 0.06f, MaterialIdCatalog.IronTieBeamBlock, 156);
            AddGlowingBox(dst, -0.10f, 0.10f, 0.10f, 0.34f, 0.06f, 0.24f, MaterialIdCatalog.CampfireBlock, 250, tempOverride: 226);
        }

        private static void BuildTimberJettyCorbel3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            // Ust kati tasiyan 45° mese konsol kirisi (+Z binaya baglanir, -Z sokaga uzanir)
            AddCullableBox(dst, -0.14f, 0.14f, 0.28f, 0.50f, -0.48f, 0.50f, MaterialIdCatalog.TreatedTimberBeam, 188, 0);
            AddCullableBox(dst, -0.14f, 0.14f, -0.48f, 0.50f, 0.28f, 0.50f, MaterialIdCatalog.TreatedTimberBeam, 182, 0);
            if (!distantMode)
            {
                AddStrut(dst, new Vector3(0f, -0.38f, 0.36f), new Vector3(0f, 0.36f, -0.38f), 0.22f, 0.16f, MaterialIdCatalog.TreatedTimberBeam, 194);
            }
        }

        private static void BuildWindmillSailLattice3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            // Ana ahsap yelken diregi + krem keten yelken cercevesi
            AddCullableBox(dst, -0.10f, 0.10f, -0.50f, 0.50f, -0.12f, 0.04f, MaterialIdCatalog.TreatedTimberBeam, 186, 0);
            AddCullableBox(dst, -0.48f, 0.48f, -0.48f, 0.48f, -0.20f, -0.13f, MaterialIdCatalog.LimeWashPlaster, 242, 0);
            if (!distantMode)
            {
                AddCullableBox(dst, -0.50f, 0.50f, -0.04f, 0.04f, -0.23f, -0.11f, MaterialIdCatalog.WoodPlank, 198, 0);
                AddCullableBox(dst, -0.50f, -0.42f, -0.50f, 0.50f, -0.22f, -0.11f, MaterialIdCatalog.WoodPlank, 198, 0);
                AddCullableBox(dst,  0.42f,  0.50f, -0.50f, 0.50f, -0.22f, -0.11f, MaterialIdCatalog.WoodPlank, 198, 0);
            }
        }

        private static void BuildDiningTable3D(List<LocalKitTriangle> dst)
        {
            float[] lx = { -0.38f, 0.38f };
            float[] lz = { -0.38f, 0.38f };
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                    AddVerticalCylinderY(dst, -0.50f, 0.36f, lx[ix], lz[iz], 0.065f, 0.055f, 6, MaterialIdCatalog.TreatedTimberBeam, 180, cullCapsY: true);

            AddCullableBox(dst, -0.40f, 0.40f, 0.24f, 0.36f, -0.40f, 0.40f, MaterialIdCatalog.TreatedTimberBeam, 174, 0);
            AddCullableBox(dst, -0.49f, 0.49f, 0.36f, 0.46f, -0.49f, 0.49f, MaterialIdCatalog.WoodPlank, 226, 0);
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
                    AddVerticalCylinderY(dst, -0.50f, topY, lx[ix], lz[iz], 0.045f, 0.04f, 6, MaterialIdCatalog.TreatedTimberBeam, 182, cullCapsY: true);
                }
            }
            AddCullableBox(dst, -0.28f, 0.28f, -0.06f, 0.02f, -0.28f, 0.28f, MaterialIdCatalog.WoodPlank, 220, 0);
            AddCullableBox(dst, -0.25f, 0.25f,  0.16f, 0.26f,  0.21f, 0.27f, MaterialIdCatalog.WoodPlank, 212, 0);
            AddCullableBox(dst, -0.25f, 0.25f,  0.34f, 0.44f,  0.21f, 0.27f, MaterialIdCatalog.WoodPlank, 218, 0);
        }

        private static void BuildBedHead3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, 0.48f, -0.42f, 0.40f, 0.07f, 0.06f, 6, MaterialIdCatalog.TreatedTimberBeam, 184, cullCapsY: false);
            AddVerticalCylinderY(dst, -0.50f, 0.48f,  0.42f, 0.40f, 0.07f, 0.06f, 6, MaterialIdCatalog.TreatedTimberBeam, 184, cullCapsY: false);
            AddCullableBox(dst, -0.42f, 0.42f, -0.22f, 0.38f,  0.36f, 0.44f, MaterialIdCatalog.OakWood, 198, 0);
            AddCullableBox(dst, -0.45f, 0.45f, -0.32f, -0.14f, -0.50f, 0.42f, MaterialIdCatalog.TreatedTimberBeam, 178, 0);
            AddCullableBox(dst, -0.42f, 0.42f, -0.14f, 0.06f, -0.50f, 0.36f, MaterialIdCatalog.LimeWashPlaster, 238, 0);
            AddCullableBox(dst, -0.34f, 0.34f,  0.06f, 0.18f,  0.02f, 0.34f, MaterialIdCatalog.LimeWashPlaster, 246, 0);
            AddCullableBox(dst, -0.43f, 0.43f, -0.12f, 0.09f, -0.50f, -0.04f, MaterialIdCatalog.BedBlock, 224, 0);
        }

        private static void BuildBedFoot3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, 0.18f, -0.42f, -0.40f, 0.07f, 0.06f, 6, MaterialIdCatalog.TreatedTimberBeam, 184, cullCapsY: false);
            AddVerticalCylinderY(dst, -0.50f, 0.18f,  0.42f, -0.40f, 0.07f, 0.06f, 6, MaterialIdCatalog.TreatedTimberBeam, 184, cullCapsY: false);
            AddCullableBox(dst, -0.42f, 0.42f, -0.28f, 0.12f, -0.44f, -0.36f, MaterialIdCatalog.OakWood, 196, 0);
            AddCullableBox(dst, -0.45f, 0.45f, -0.32f, -0.14f, -0.42f, 0.50f, MaterialIdCatalog.TreatedTimberBeam, 178, 0);
            AddCullableBox(dst, -0.43f, 0.43f, -0.14f, 0.09f, -0.36f, 0.50f, MaterialIdCatalog.BedBlock, 224, 0);
        }

        private static void BuildBookshelf3D(List<LocalKitTriangle> dst)
        {
            AddCullableBox(dst, -0.48f, 0.48f, -0.50f, 0.50f,  0.22f, 0.38f, MaterialIdCatalog.TreatedTimberBeam, 168, 0);
            AddCullableBox(dst, -0.48f, -0.38f, -0.50f, 0.50f, -0.18f, 0.38f, MaterialIdCatalog.TreatedTimberBeam, 184, 0);
            AddCullableBox(dst,  0.38f,  0.48f, -0.50f, 0.50f, -0.18f, 0.38f, MaterialIdCatalog.TreatedTimberBeam, 184, 0);
            float[] shelfYs = { -0.46f, -0.14f, 0.18f, 0.44f };
            for (int s = 0; s < shelfYs.Length; s++)
            {
                AddCullableBox(dst, -0.38f, 0.38f, shelfYs[s], shelfYs[s] + 0.06f, -0.18f, 0.24f, MaterialIdCatalog.WoodPlank, 212, 0);
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
                for (int b = 0; b < 4; b++)
                {
                    float bx0 = -0.32f + b * 0.17f;
                    float bh = 0.20f + ((r + b) % 3) * 0.025f;
                    AddFrontReliefBox(dst, bx0, bx0 + 0.13f, by0, by0 + bh, -0.12f, 0.21f, bookMats[(r * 2 + b) % bookMats.Length], (byte)(206 + b * 8), ArchitecturalShapeCatalog.FaceNegY);
                }
            }
        }

        private static void BuildFireplaceHearth3D(List<LocalKitTriangle> dst, string? glbDir)
        {
            _ = glbDir;
            AddCullableBox(dst, -0.48f, 0.48f, -0.50f, -0.32f, -0.42f, 0.46f, MaterialIdCatalog.CutAshlarStone, 208, 0, skirt: true);
            AddCullableBox(dst, -0.48f, 0.48f, -0.32f,  0.42f,  0.22f, 0.46f, MaterialIdCatalog.StoneBrick, 188, 0);
            AddCullableBox(dst, -0.48f, -0.26f, -0.32f, 0.42f, -0.36f, 0.22f, MaterialIdCatalog.StoneBrick, 198, 0);
            AddCullableBox(dst,  0.26f,  0.48f, -0.32f, 0.42f, -0.36f, 0.22f, MaterialIdCatalog.StoneBrick, 198, 0);
            AddCullableBox(dst, -0.50f,  0.50f,  0.36f, 0.50f, -0.42f, 0.46f, MaterialIdCatalog.TreatedTimberBeam, 188, 0);
            // Ic ates haznesinde parlayan sicak kor kutukler!
            AddHorizontalCylinderX(dst, -0.20f, 0.20f, -0.24f, -0.04f, 0.07f, 6, MaterialIdCatalog.CampfireBlock, 240, capEnds: true, tempOverride: 222);
            AddHorizontalCylinderZ(dst, -0.18f, 0.16f, -0.18f,  0.02f, 0.06f, 6, MaterialIdCatalog.CampfireBlock, 248, capEnds: true, tempOverride: 228);
        }

        private static void BuildKitchenCounter3D(List<LocalKitTriangle> dst)
        {
            AddCullableBox(dst, -0.48f, 0.48f, -0.50f, 0.34f, -0.36f, 0.44f, MaterialIdCatalog.OakWood, 196, 0);
            AddCullableBox(dst, -0.42f, -0.03f, -0.40f, 0.26f, -0.40f, -0.35f, MaterialIdCatalog.WoodPlank, 212, 0);
            AddCullableBox(dst,  0.03f,  0.42f, -0.40f, 0.26f, -0.40f, -0.35f, MaterialIdCatalog.WoodPlank, 212, 0);
            AddCullableBox(dst, -0.50f, 0.50f,  0.34f, 0.44f, -0.44f, 0.48f, MaterialIdCatalog.DressedMarbleBlock, 236, 0);
            AddVerticalCylinderY(dst, 0.44f, 0.58f, 0.22f, 0.08f, 0.09f, 0.06f, 6, MaterialIdCatalog.RoofClayTile, 218, cullCapsY: false);
        }

        private static void BuildStorageChest3D(List<LocalKitTriangle> dst)
        {
            AddCullableBox(dst, -0.42f, 0.42f, -0.50f, 0.04f, -0.30f, 0.30f, MaterialIdCatalog.OakWood, 198, 0);
            AddHorizontalCylinderX(dst, -0.42f, 0.42f, 0.04f, 0.0f, 0.30f, 6, MaterialIdCatalog.WoodPlank, 214, capEnds: true);
            AddCullableBox(dst, -0.34f, -0.24f, -0.50f, 0.35f, -0.32f, 0.32f, MaterialIdCatalog.IronTieBeamBlock, 152, 0);
            AddCullableBox(dst,  0.24f,  0.34f, -0.50f, 0.35f, -0.32f, 0.32f, MaterialIdCatalog.IronTieBeamBlock, 152, 0);
            AddCullableBox(dst, -0.07f,  0.07f, -0.08f, 0.12f, -0.34f, -0.28f, MaterialIdCatalog.Bronze, 224, 0);
        }

        private static void BuildWoodenBarrel3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, -0.02f, 0f, 0f, 0.31f, 0.38f, 8, MaterialIdCatalog.OakWood, 204, cullCapsY: false);
            AddVerticalCylinderY(dst, -0.02f,  0.46f, 0f, 0f, 0.38f, 0.31f, 8, MaterialIdCatalog.OakWood, 212, cullCapsY: false);
            AddVerticalCylinderY(dst, -0.34f, -0.24f, 0f, 0f, 0.35f, 0.36f, 8, MaterialIdCatalog.IronTieBeamBlock, 148, cullCapsY: true);
            AddVerticalCylinderY(dst,  0.18f,  0.28f, 0f, 0f, 0.36f, 0.35f, 8, MaterialIdCatalog.IronTieBeamBlock, 148, cullCapsY: true);
        }

        private static void BuildRoyalThrone3D(List<LocalKitTriangle> dst)
        {
            AddCullableBox(dst, -0.48f, 0.48f, -0.50f, -0.32f, -0.46f, 0.46f, MaterialIdCatalog.DressedMarbleBlock, 228, 0);
            AddCullableBox(dst, -0.38f, 0.38f, -0.32f, -0.02f, -0.34f, 0.36f, MaterialIdCatalog.DressedMarbleBlock, 236, 0);
            AddCullableBox(dst, -0.34f, 0.34f, -0.02f,  0.08f, -0.32f, 0.30f, MaterialIdCatalog.BedBlock, 224, 0);
            AddCullableBox(dst, -0.40f, -0.28f, -0.02f, 0.28f, -0.32f, 0.34f, MaterialIdCatalog.Bronze, 226, 0);
            AddCullableBox(dst,  0.28f,  0.40f, -0.02f, 0.28f, -0.32f, 0.34f, MaterialIdCatalog.Bronze, 226, 0);
            AddCullableBox(dst, -0.36f,  0.36f,  0.08f, 0.68f,  0.24f, 0.38f, MaterialIdCatalog.DressedMarbleBlock, 238, 0);
            AddCullableBox(dst, -0.28f,  0.28f,  0.10f, 0.58f,  0.20f, 0.25f, MaterialIdCatalog.BedBlock, 228, 0);
        }

        private static void BuildAnvilOnStump3D(List<LocalKitTriangle> dst)
        {
            AddVerticalCylinderY(dst, -0.50f, 0.02f, 0f, 0f, 0.34f, 0.30f, 8, MaterialIdCatalog.OakWood, 192, cullCapsY: false);
            AddCullableBox(dst, -0.24f, 0.24f, 0.02f, 0.14f, -0.16f, 0.16f, MaterialIdCatalog.Steel, 165, 0);
            AddCullableBox(dst, -0.14f, 0.14f, 0.14f, 0.28f, -0.11f, 0.11f, MaterialIdCatalog.Steel, 155, 0);
            AddCullableBox(dst, -0.28f, 0.22f, 0.28f, 0.44f, -0.15f, 0.15f, MaterialIdCatalog.Steel, 188, 0);
            AddHorizontalConeX(dst, 0.22f, 0.48f, 0.36f, 0f, 0.12f, 6, MaterialIdCatalog.Steel, 196);
        }

        private static void BuildSawhorseBench3D(List<LocalKitTriangle> dst)
        {
            AddStrut(dst, new Vector3(-0.36f, -0.50f, -0.28f), new Vector3(-0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddStrut(dst, new Vector3(-0.36f, -0.50f,  0.28f), new Vector3(-0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddStrut(dst, new Vector3( 0.36f, -0.50f, -0.28f), new Vector3( 0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddStrut(dst, new Vector3( 0.36f, -0.50f,  0.28f), new Vector3( 0.36f, 0.26f, 0.0f), 0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 182);
            AddCullableBox(dst, -0.48f, 0.48f, 0.20f, 0.34f, -0.14f, 0.14f, MaterialIdCatalog.WoodPlank, 222, 0);
        }

        private static void BuildWeaponRack3D(List<LocalKitTriangle> dst)
        {
            AddCullableBox(dst, -0.44f, 0.44f, -0.50f, -0.38f, -0.22f, 0.22f, MaterialIdCatalog.TreatedTimberBeam, 180, 0);
            AddCullableBox(dst, -0.42f, -0.34f, -0.38f, 0.38f, -0.06f, 0.06f, MaterialIdCatalog.TreatedTimberBeam, 184, 0);
            AddCullableBox(dst,  0.34f,  0.42f, -0.38f, 0.38f, -0.06f, 0.06f, MaterialIdCatalog.TreatedTimberBeam, 184, 0);
            AddCullableBox(dst, -0.42f,  0.42f,  0.18f, 0.26f, -0.08f, 0.08f, MaterialIdCatalog.TreatedTimberBeam, 188, 0);
            float[] sx = { -0.20f, 0.0f, 0.20f };
            for (int i = 0; i < sx.Length; i++)
            {
                AddVerticalCylinderY(dst, -0.38f, 0.36f, sx[i], 0.0f, 0.025f, 0.025f, 6, MaterialIdCatalog.OakWood, 200, cullCapsY: true);
                AddVerticalCylinderY(dst,  0.36f, 0.62f, sx[i], 0.0f, 0.045f, 0.008f, 6, MaterialIdCatalog.Steel, 232, cullCapsY: false);
            }
        }

        private static void BuildSplitRailFence3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            int sides = distantMode ? 4 : 6;
            AddVerticalCylinderY(dst, -0.50f, 0.46f, 0f, 0f, 0.09f, 0.08f, sides, MaterialIdCatalog.OakWood, 192, cullCapsY: false);
            AddHorizontalCylinderX(dst, -0.50f, 0.50f, -0.12f, 0f, 0.055f, sides, MaterialIdCatalog.WoodPlank, 212, capEnds: false, cullEndCapsX: true);
            AddHorizontalCylinderX(dst, -0.50f, 0.50f,  0.22f, 0f, 0.055f, sides, MaterialIdCatalog.WoodPlank, 218, capEnds: false, cullEndCapsX: true);
        }

        private static void BuildWellWindlass3D(List<LocalKitTriangle> dst, bool distantMode)
        {
            // 5x5m gercek koy kuyusunun merkezindeki doner mese tambur, demir kol ve sarkitilmis su kovasi!
            AddHorizontalCylinderX(dst, -0.50f, 0.50f, 0.28f, 0f, 0.11f, distantMode ? 6 : 8, MaterialIdCatalog.OakWood, 210, capEnds: true);
            if (!distantMode)
            {
                AddCullableBox(dst, -0.03f, 0.03f, -0.18f, 0.28f, -0.03f, 0.03f, MaterialIdCatalog.StrawChinkingBundle, 198, 0);
                AddVerticalCylinderY(dst, -0.44f, -0.16f, 0f, 0f, 0.14f, 0.17f, 8, MaterialIdCatalog.OakWood, 196, cullCapsY: false);
                AddVerticalCylinderY(dst, -0.26f, -0.22f, 0f, 0f, 0.16f, 0.165f, 8, MaterialIdCatalog.IronTieBeamBlock, 148, cullCapsY: true);
            }
        }

        // =====================================================================
        // YUKSEK PERFORMANSLI YUZ-GIZLEMELI PRIMITIF YARDIMCILARI
        // =====================================================================

        private static void AddQuad(
            List<LocalKitTriangle> dst,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            byte mat,
            byte shade,
            byte temp = 0,
            bool skirt = false,
            byte cullFaceBit = 0)
        {
            dst.Add(new LocalKitTriangle(v0, v1, v2, mat, shade, temp, skirt, cullFaceBit));
            dst.Add(new LocalKitTriangle(v0, v2, v3, mat, (byte)Math.Max(110, shade - 8), temp, skirt, cullFaceBit));
        }

        private static void AddCrossedBladeQuad(
            List<LocalKitTriangle> dst,
            float halfSpan,
            float extent,
            float y0,
            float y1,
            byte mat,
            byte shade)
        {
            _ = halfSpan;
            Vector3 a0 = new(-extent, y0, -extent), a1 = new(-extent, y1, -extent), a2 = new(extent, y1, extent), a3 = new(extent, y0, extent);
            Vector3 b0 = new(-extent, y0,  extent), b1 = new(-extent, y1,  extent), b2 = new(extent, y1, -extent), b3 = new(extent, y0, -extent);
            AddQuad(dst, a0, a1, a2, a3, mat, shade);
            AddQuad(dst, a3, a2, a1, a0, mat, (byte)(shade - 8));
            AddQuad(dst, b0, b1, b2, b3, mat, shade);
            AddQuad(dst, b3, b2, b1, b0, mat, (byte)(shade - 8));
        }

        private static void AddWallCheekBox(
            List<LocalKitTriangle> dst,
            float x0,
            float x1,
            float y0,
            float y1,
            byte frontMat,
            byte cullMask,
            bool distantMode)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;
            const byte FPosZ = ArchitecturalShapeCatalog.FacePosZ;
            const byte FNegZ = ArchitecturalShapeCatalog.FaceNegZ;

            float z0 = -0.24f, z1 = 0.24f;
            Vector3 p000 = new(x0, y0, z0), p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p001 = new(x0, y0, z1);
            Vector3 p010 = new(x0, y1, z0), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1), p011 = new(x0, y1, z1);

            AddQuad(dst, p000, p010, p110, p100, frontMat, 214, 0, false, FNegZ);
            if (!distantMode)
            {
                AddQuad(dst, p101, p111, p011, p001, MaterialIdCatalog.LimeWashPlaster, 232, 0, false, FPosZ);
            }
            AddQuad(dst, p010, p011, p111, p110, frontMat, 220, 0, false, (byte)(cullMask & FPosY));
            AddQuad(dst, p001, p000, p100, p101, frontMat, 170, 0, false, (byte)(cullMask & FNegY));
            AddQuad(dst, p001, p011, p010, p000, frontMat, 200, 0, false, (byte)(cullMask & FNegX));
            AddQuad(dst, p100, p110, p111, p101, frontMat, 200, 0, false, (byte)(cullMask & FPosX));
        }

        private static void AddFrontReliefBox(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float y0, float y1,
            float z0, float z1,
            byte mat,
            byte topShade,
            byte cullMask,
            bool skirt = false)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;

            // Arka (+Z) yuzu duvara gomulu oldugu icin HIC uretilmez!
            Vector3 p000 = new(x0, y0, z0), p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p001 = new(x0, y0, z1);
            Vector3 p010 = new(x0, y1, z0), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1), p011 = new(x0, y1, z1);
            byte side = (byte)Math.Clamp(topShade - 16, 120, 240);
            byte bot = (byte)Math.Clamp(topShade - 48, 108, 220);

            AddQuad(dst, p000, p010, p110, p100, mat, side, 0, skirt, 0);
            AddQuad(dst, p010, p011, p111, p110, mat, topShade, 0, false, (byte)(cullMask & FPosY));
            AddQuad(dst, p001, p000, p100, p101, mat, bot, 0, skirt, (byte)(cullMask & FNegY));
            AddQuad(dst, p001, p011, p010, p000, mat, side, 0, skirt, (byte)(cullMask & FNegX));
            AddQuad(dst, p100, p110, p111, p101, mat, side, 0, skirt, (byte)(cullMask & FPosX));
        }

        private static void AddCullableBox(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float y0, float y1,
            float z0, float z1,
            byte mat,
            byte topShade,
            byte cullMask,
            bool skirt = false)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;
            const byte FPosY = ArchitecturalShapeCatalog.FacePosY;
            const byte FNegY = ArchitecturalShapeCatalog.FaceNegY;
            const byte FPosZ = ArchitecturalShapeCatalog.FacePosZ;
            const byte FNegZ = ArchitecturalShapeCatalog.FaceNegZ;

            Vector3 p000 = new(x0, y0, z0), p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p001 = new(x0, y0, z1);
            Vector3 p010 = new(x0, y1, z0), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1), p011 = new(x0, y1, z1);
            byte side = (byte)Math.Clamp(topShade - 22, 120, 240);
            byte bot = (byte)Math.Clamp(topShade - 56, 105, 220);

            AddQuad(dst, p010, p011, p111, p110, mat, topShade, 0, skirt, (byte)(cullMask & FPosY));
            AddQuad(dst, p001, p000, p100, p101, mat, bot, 0, skirt, (byte)(cullMask & FNegY));
            AddQuad(dst, p000, p010, p110, p100, mat, side, 0, skirt, (byte)(cullMask & FNegZ));
            AddQuad(dst, p101, p111, p011, p001, mat, side, 0, skirt, (byte)(cullMask & FPosZ));
            AddQuad(dst, p001, p011, p010, p000, mat, (byte)(side - 8), 0, skirt, (byte)(cullMask & FNegX));
            AddQuad(dst, p100, p110, p111, p101, mat, (byte)(side - 8), 0, skirt, (byte)(cullMask & FPosX));
        }

        private static void AddGlowingBox(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float y0, float y1,
            float z0, float z1,
            byte mat,
            byte shade,
            byte tempOverride)
        {
            Vector3 p000 = new(x0, y0, z0), p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p001 = new(x0, y0, z1);
            Vector3 p010 = new(x0, y1, z0), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1), p011 = new(x0, y1, z1);
            AddQuad(dst, p010, p011, p111, p110, mat, shade, tempOverride);
            AddQuad(dst, p001, p000, p100, p101, mat, shade, tempOverride);
            AddQuad(dst, p000, p010, p110, p100, mat, shade, tempOverride);
            AddQuad(dst, p101, p111, p011, p001, mat, shade, tempOverride);
            AddQuad(dst, p001, p011, p010, p000, mat, shade, tempOverride);
            AddQuad(dst, p100, p110, p111, p101, mat, shade, tempOverride);
        }

        private static void AddSlopedRoofDeck(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float z0, float z1,
            float y0, float y1,
            float thickness,
            byte topMat,
            byte undersideMat,
            byte shade,
            bool distantMode)
        {
            const byte FPosX = ArchitecturalShapeCatalog.FacePosX;
            const byte FNegX = ArchitecturalShapeCatalog.FaceNegX;

            Vector3 f0 = new(x0, y0, z0),             f1 = new(x1, y0, z0);
            Vector3 f2 = new(x1, y0 + thickness, z0), f3 = new(x0, y0 + thickness, z0);
            Vector3 b0 = new(x0, y1, z1),             b1 = new(x1, y1, z1);
            Vector3 b2 = new(x1, y1 + thickness, z1), b3 = new(x0, y1 + thickness, z1);

            // Ust cati yuzu + on sacak alni (her mesafede gorunur!)
            AddQuad(dst, f3, b3, b2, f2, topMat, shade);
            AddQuad(dst, f0, f3, f2, f1, undersideMat, (byte)(shade - 24));
            if (distantMode)
            {
                return;
            }

            // Alt ahsap kaplama + komsu cati vokselleriyle gizlenen yan alinliklar
            AddQuad(dst, b0, b1, f1, f0, undersideMat, (byte)(shade - 36));
            AddQuad(dst, b0, f0, f3, b3, undersideMat, (byte)(shade - 18), 0, false, FNegX);
            AddQuad(dst, f1, b1, b2, f2, undersideMat, (byte)(shade - 18), 0, false, FPosX);
        }

        private static void AddRoofTileRidgeStrip(
            List<LocalKitTriangle> dst,
            float cx,
            float width,
            float z0, float z1,
            float y0, float y1,
            float height,
            byte mat,
            byte shade)
        {
            float hw = width * 0.5f;
            Vector3 fL = new(cx - hw, y0, z0), fM = new(cx, y0 + height, z0), fR = new(cx + hw, y0, z0);
            Vector3 bL = new(cx - hw, y1, z1), bM = new(cx, y1 + height, z1), bR = new(cx + hw, y1, z1);
            // Yalnizca ust-sol, ust-sag ve on kiremit agzi (alt taban cati plakasina gomulu -> 5 ucgen!)
            AddQuad(dst, fL, bL, bM, fM, mat, shade);
            AddQuad(dst, fM, bM, bR, fR, mat, (byte)(shade - 12));
            dst.Add(new LocalKitTriangle(fL, fM, fR, mat, (byte)(shade - 18)));
        }

        private static void AddVerticalCylinderY(
            List<LocalKitTriangle> dst,
            float y0, float y1,
            float cx, float cz,
            float rBot, float rTop,
            int sides,
            byte mat,
            byte baseShade,
            bool cullCapsY)
        {
            byte topCull = cullCapsY ? ArchitecturalShapeCatalog.FacePosY : (byte)0;
            byte botCull = cullCapsY ? ArchitecturalShapeCatalog.FaceNegY : (byte)0;
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
                dst.Add(new LocalKitTriangle(topC, u1, u0, mat, baseShade, 0, false, topCull));
                dst.Add(new LocalKitTriangle(botC, b0, b1, mat, (byte)Math.Max(115, baseShade - 55), 0, false, botCull));
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
            byte tempOverride = 0,
            bool cullEndCapsX = false)
        {
            byte capM = endCapMat != 0 ? endCapMat : mat;
            byte lCull = cullEndCapsX ? ArchitecturalShapeCatalog.FaceNegX : (byte)0;
            byte rCull = cullEndCapsX ? ArchitecturalShapeCatalog.FacePosX : (byte)0;
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
                    dst.Add(new LocalKitTriangle(lC, l1, l0, capM, (byte)Math.Min(245, baseShade + 10), tempOverride, false, lCull));
                    dst.Add(new LocalKitTriangle(rC, r0, r1, capM, (byte)Math.Min(245, baseShade + 10), tempOverride, false, rCull));
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
                Vector3 p0 = new(xA, yA, z1), p1 = new(xB, yA, z1);
                p1.Y = yB;
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
        }

        private static void AddSlopedWedgeZ(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float yMin, float yMax,
            byte mat,
            byte shade,
            bool cullSidesX)
        {
            byte lCull = cullSidesX ? ArchitecturalShapeCatalog.FaceNegX : (byte)0;
            byte rCull = cullSidesX ? ArchitecturalShapeCatalog.FacePosX : (byte)0;
            Vector3 bFL = new(x0, yMin, -0.50f), bFR = new(x1, yMin, -0.50f);
            Vector3 bBL = new(x0, yMin,  0.50f), bBR = new(x1, yMin,  0.50f);
            Vector3 tBL = new(x0, yMax,  0.50f), tBR = new(x1, yMax,  0.50f);

            AddQuad(dst, bFL, tBL, tBR, bFR, mat, shade);
            AddQuad(dst, bBR, tBR, tBL, bBL, mat, (byte)(shade - 18), 0, false, ArchitecturalShapeCatalog.FacePosZ);
            AddQuad(dst, bBL, bFL, bFR, bBR, mat, (byte)(shade - 40), 0, false, ArchitecturalShapeCatalog.FaceNegY);
            dst.Add(new LocalKitTriangle(bFL, bBL, tBL, mat, (byte)(shade - 12), 0, false, lCull));
            dst.Add(new LocalKitTriangle(bFR, tBR, bBR, mat, (byte)(shade - 12), 0, false, rCull));
        }

        private static void AddPitchedPrismX(
            List<LocalKitTriangle> dst,
            float x0, float x1,
            float yBase, float yApex,
            float halfZ,
            byte mat,
            byte shade,
            bool cullEndsX)
        {
            byte lCull = cullEndsX ? ArchitecturalShapeCatalog.FaceNegX : (byte)0;
            byte rCull = cullEndsX ? ArchitecturalShapeCatalog.FacePosX : (byte)0;
            Vector3 lS = new(x0, yBase, -halfZ), rS = new(x1, yBase, -halfZ);
            Vector3 lN = new(x0, yBase,  halfZ), rN = new(x1, yBase,  halfZ);
            Vector3 lA = new(x0, yApex,  0f),    rA = new(x1, yApex,  0f);

            AddQuad(dst, lS, lA, rA, rS, mat, shade);
            AddQuad(dst, rN, rA, lA, lN, mat, (byte)(shade - 10));
            AddQuad(dst, lN, lS, rS, rN, mat, (byte)(shade - 42), 0, false, ArchitecturalShapeCatalog.FaceNegY);
            dst.Add(new LocalKitTriangle(lS, lN, lA, mat, (byte)(shade - 16), 0, false, lCull));
            dst.Add(new LocalKitTriangle(rS, rA, rN, mat, (byte)(shade - 16), 0, false, rCull));
        }
    }
}
