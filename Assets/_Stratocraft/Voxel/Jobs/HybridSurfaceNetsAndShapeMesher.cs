using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Jobs
{
    /// <summary>
    /// Ufak fasetli poligonlardan (Deep Rock Galactic / Astroneer / 7 Days to Die tarzi Flat-Shaded Low-Poly)
    /// olusan tekil ucgen (Triangle) verisi.
    /// Her ucgen kendi ayrik kose noktalarina ve yuzey normaline sahiptir (keskin faset gorunumu icin).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FacetedVoxelTriangle
    {
        public Vector3 V0;
        public Vector3 V1;
        public Vector3 V2;
        public Vector3 Normal;
        public byte MaterialId;
        public byte Temperature;
        public byte FacetShade;
        public byte AmbientOcclusion;
    }

    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.7.B "Cift Kanalli Hibrit Yuzey Orme (Two-Channel Hybrid Meshing)" motoru:
    /// - Kanal 1 (ShapeID == 0 Dogal Arazi): 7-bit SDF Density (Bits [8..14], IsoLevel=64) uzerinde
    ///   Dual Contouring / Surface Nets + Jeodezik Faset Jitter calistirarak Astroneer ve Deep Rock Galactic
    ///   tarzi ufak ucgen poligonlardan (ufak fasetler) olusan puruzsuz/fasetli dogal arazi, magara kubbeleri,
    ///   sarkit-dikit sutunlar ve cevher cikintilari uretir. Mimari bloklarla (ShapeID >= 1) birlestigi
    ///   sinirlarda "Skirt / Vertex Clamping" ile sifir catlak garantisi saglar.
    /// - Kanal 2B (ShapeID == 1..62 Radyal Sekil Carki): 7 Days to Die tarzi 62 mimari sekil
    ///   (2=Sekizgen Sutun, 3=45 Derece Rampa/Cati, 4=Kemer Tasi/Voussoir, 5=Kilit Tasi/Keystone,
    ///   6=Pahli Savunma Duvari/Mazgal, 7=Fasetli Kristal Salkimi/Kaide, 1=Pahli Mimari Blok) uretir.
    /// </summary>
    public static class HybridSurfaceNetsAndShapeMesher
    {
        public const byte IsoLevel = 64;

        private static readonly int[] CubeEdgeCorners = new int[24]
        {
            0, 1,  1, 3,  3, 2,  2, 0, // alt yuzey (dy=0)
            4, 5,  5, 7,  7, 6,  6, 4, // ust yuzey (dy=1)
            0, 4,  1, 5,  2, 6,  3, 7  // dikey kenarlar
        };

        /// <summary>
        /// 16 x maxY x 16 boyutundaki Voxel Chunk dilimi icin Cift Kanalli Hibrit Mesh (Kanal 1 Surface Nets SDF
        /// + Kanal 2B 7 Days to Die Mimari Sekiller) uretir ve outTriangles listesine ekler.
        /// </summary>
        public static void GenerateHybridChunkMesh(
            ReadOnlySpan<uint> chunkVoxels65536,
            int maxY,
            int chunkWorldOffsetX,
            int chunkWorldOffsetZ,
            List<FacetedVoxelTriangle> outTriangles)
        {
            if (maxY > 256)
            {
                maxY = 256;
            }

            int gridX = 16;
            int gridZ = 16;
            int gridY = maxY;

            // Dual hucre merkezleri: [0..14] x [0..maxY-2] x [0..14]
            int dualCount = gridX * gridY * gridZ;
            Vector3[] dualVertices = new Vector3[dualCount];
            byte[] dualMaterials = new byte[dualCount];
            byte[] dualAo = new byte[dualCount];
            bool[] dualValid = new bool[dualCount];

            // 1. GECIS (KANAL 1): Dual Contouring / Surface Nets + Jeodezik Faset Jitter + Skirt Clamping
            Span<uint> cornerRaws = stackalloc uint[8];
            Span<float> cornerDensities = stackalloc float[8];

            for (int y = 0; y < gridY - 1; y++)
            {
                for (int z = 0; z < gridZ - 1; z++)
                {
                    for (int x = 0; x < gridX - 1; x++)
                    {
                        cornerRaws[0] = GetNaturalSdfSample(chunkVoxels65536, x, y, z);
                        cornerRaws[1] = GetNaturalSdfSample(chunkVoxels65536, x + 1, y, z);
                        cornerRaws[2] = GetNaturalSdfSample(chunkVoxels65536, x, y, z + 1);
                        cornerRaws[3] = GetNaturalSdfSample(chunkVoxels65536, x + 1, y, z + 1);
                        cornerRaws[4] = GetNaturalSdfSample(chunkVoxels65536, x, y + 1, z);
                        cornerRaws[5] = GetNaturalSdfSample(chunkVoxels65536, x + 1, y + 1, z);
                        cornerRaws[6] = GetNaturalSdfSample(chunkVoxels65536, x, y + 1, z + 1);
                        cornerRaws[7] = GetNaturalSdfSample(chunkVoxels65536, x + 1, y + 1, z + 1);

                        int insideMask = 0;
                        byte dominantMat = 0;
                        byte highestMatPriority = 0;
                        float densitySum = 0f;
                        bool touchesArchitecturalBlock = false;
                        Vector3 archClampTarget = Vector3.Zero;
                        int archTouchCount = 0;

                        for (int c = 0; c < 8; c++)
                        {
                            int cx = x + (c & 1);
                            int cz = z + ((c >> 1) & 1);
                            int cy = y + ((c >> 2) & 1);

                            uint rawOrig = chunkVoxels65536[(cy << 8) | (cz << 4) | cx];
                            byte origMat = VoxelBitUtility.GetMaterialId(rawOrig);
                            byte origShape = VoxelBitUtility.GetShapeId(rawOrig);

                            if (origMat != 0 && origShape >= 1)
                            {
                                touchesArchitecturalBlock = true;
                                archClampTarget += new Vector3(cx, cy, cz);
                                archTouchCount++;
                            }

                            uint raw = cornerRaws[c];
                            byte dByte = VoxelBitUtility.GetDensityOrLiquid(raw);
                            byte mat = VoxelBitUtility.GetMaterialId(raw);
                            if (mat == 0)
                            {
                                dByte = 0;
                            }

                            cornerDensities[c] = dByte;
                            densitySum += dByte;

                            if (dByte >= IsoLevel && mat != 0)
                            {
                                insideMask |= (1 << c);
                                byte prio = GetMaterialVisualPriority(mat);
                                if (prio >= highestMatPriority)
                                {
                                    highestMatPriority = prio;
                                    dominantMat = mat;
                                }
                            }
                        }

                        if (insideMask == 0 || insideMask == 0xFF)
                        {
                            continue;
                        }

                        Vector3 edgeAccum = Vector3.Zero;
                        int edgeCrossings = 0;

                        for (int e = 0; e < 12; e++)
                        {
                            int cA = CubeEdgeCorners[e * 2];
                            int cB = CubeEdgeCorners[e * 2 + 1];
                            bool inA = (insideMask & (1 << cA)) != 0;
                            bool inB = (insideMask & (1 << cB)) != 0;

                            if (inA != inB)
                            {
                                float dA = cornerDensities[cA];
                                float dB = cornerDensities[cB];
                                float denom = dB - dA;
                                float t = Math.Abs(denom) > 0.001f ? (IsoLevel - dA) / denom : 0.5f;
                                t = Math.Clamp(t, 0.08f, 0.92f);

                                Vector3 pA = GetCornerOffset(cA);
                                Vector3 pB = GetCornerOffset(cB);
                                edgeAccum += Vector3.Lerp(pA, pB, t);
                                edgeCrossings++;
                            }
                        }

                        Vector3 localPos = edgeCrossings > 0
                            ? (edgeAccum / edgeCrossings)
                            : new Vector3(0.5f, 0.5f, 0.5f);

                        // Deep Rock Galactic / Astroneer tarzi ufak kristal/kaya poligon faset kirilmasi (Geodesic Jitter)
                        Vector3 jitter = ComputeDeterministicFacetJitter(
                            x + chunkWorldOffsetX,
                            y,
                            z + chunkWorldOffsetZ);

                        // Sivi yuzeyleri (Su=9, Magma=10) puruzsuz/hafif dalgali kalir, kayalar keskin fasetli olur
                        if (dominantMat == 9 || dominantMat == 10)
                        {
                            localPos.X = Math.Clamp(localPos.X + jitter.X * 0.35f, 0.12f, 0.88f);
                            localPos.Z = Math.Clamp(localPos.Z + jitter.Z * 0.35f, 0.12f, 0.88f);
                            localPos.Y = Math.Clamp(localPos.Y + jitter.Y * 0.12f, 0.15f, 0.85f);
                        }
                        else
                        {
                            localPos.X = Math.Clamp(localPos.X + jitter.X, 0.08f, 0.92f);
                            localPos.Y = Math.Clamp(localPos.Y + jitter.Y, 0.08f, 0.92f);
                            localPos.Z = Math.Clamp(localPos.Z + jitter.Z, 0.08f, 0.92f);
                        }

                        Vector3 worldCellPos = new Vector3(x, y, z) + localPos;

                        // Bolum 2.7.B: Mimari bloklarla (ShapeID >= 1) dogal arazi (ShapeID == 0) birlesiminde
                        // Skirt / Vertex Clamping ile catlaklarin sifirlanmasi
                        if (touchesArchitecturalBlock && archTouchCount > 0)
                        {
                            Vector3 avgArch = archClampTarget / archTouchCount;
                            worldCellPos = Vector3.Lerp(worldCellPos, avgArch, 0.28f);
                        }

                        int dualIdx = (y * gridZ + z) * gridX + x;
                        dualVertices[dualIdx] = worldCellPos;
                        dualMaterials[dualIdx] = dominantMat != 0 ? dominantMat : (byte)1;

                        // Cavity Ambient Occlusion: hucre yogunlugu arttikca magara/oyuk ici karartma artar
                        float avgDensity01 = Math.Clamp(densitySum / (8f * 127f), 0f, 1f);
                        byte cavityAo = (byte)Math.Clamp((int)(255f - avgDensity01 * 72f), 145, 255);
                        dualAo[dualIdx] = cavityAo;
                        dualValid[dualIdx] = true;
                    }
                }
            }

            // 2. GECIS (KANAL 1 UCGENLEME): Aktif SDF kenarlari etrafindaki 4 Dual Vertex'i
            // 2 adet ufak Flat-Shaded ucgense (Facet) bol ve her ucgene kendi keskin yuzey normalini ata
            for (int y = 1; y < gridY - 1; y++)
            {
                for (int z = 1; z < gridZ - 1; z++)
                {
                    for (int x = 1; x < gridX - 1; x++)
                    {
                        bool centerSolid = IsNaturalSdfSolid(chunkVoxels65536, x, y, z);

                        // +X kenari (x, y, z) -> (x+1, y, z): y ve z komsu 4 dual hucreyi baglar
                        bool xpSolid = IsNaturalSdfSolid(chunkVoxels65536, x + 1, y, z);
                        if (centerSolid != xpSolid)
                        {
                            int d0 = ((y - 1) * gridZ + (z - 1)) * gridX + x;
                            int d1 = ((y) * gridZ + (z - 1)) * gridX + x;
                            int d2 = ((y) * gridZ + (z)) * gridX + x;
                            int d3 = ((y - 1) * gridZ + (z)) * gridX + x;

                            EmitDualQuadAsFacetedTriangles(
                                dualVertices, dualMaterials, dualAo, dualValid,
                                d0, d1, d2, d3,
                                flipWinding: !centerSolid,
                                seedHash: HashCoord(x + chunkWorldOffsetX, y, z + chunkWorldOffsetZ, 1),
                                outTriangles);
                        }

                        // +Y kenari (x, y, z) -> (x, y+1, z): x ve z komsu 4 dual hucreyi baglar
                        bool ypSolid = IsNaturalSdfSolid(chunkVoxels65536, x, y + 1, z);
                        if (centerSolid != ypSolid)
                        {
                            int d0 = (y * gridZ + (z - 1)) * gridX + (x - 1);
                            int d1 = (y * gridZ + (z)) * gridX + (x - 1);
                            int d2 = (y * gridZ + (z)) * gridX + (x);
                            int d3 = (y * gridZ + (z - 1)) * gridX + (x);

                            EmitDualQuadAsFacetedTriangles(
                                dualVertices, dualMaterials, dualAo, dualValid,
                                d0, d1, d2, d3,
                                flipWinding: !centerSolid,
                                seedHash: HashCoord(x + chunkWorldOffsetX, y, z + chunkWorldOffsetZ, 2),
                                outTriangles);
                        }

                        // +Z kenari (x, y, z) -> (x, y, z+1): x ve y komsu 4 dual hucreyi baglar
                        bool zpSolid = IsNaturalSdfSolid(chunkVoxels65536, x, y, z + 1);
                        if (centerSolid != zpSolid)
                        {
                            int d0 = ((y - 1) * gridZ + z) * gridX + (x - 1);
                            int d1 = ((y - 1) * gridZ + z) * gridX + (x);
                            int d2 = ((y) * gridZ + z) * gridX + (x);
                            int d3 = ((y) * gridZ + z) * gridX + (x - 1);

                            EmitDualQuadAsFacetedTriangles(
                                dualVertices, dualMaterials, dualAo, dualValid,
                                d0, d1, d2, d3,
                                flipWinding: !centerSolid,
                                seedHash: HashCoord(x + chunkWorldOffsetX, y, z + chunkWorldOffsetZ, 3),
                                outTriangles);
                        }
                    }
                }
            }

            // 3. GECIS (KANAL 2B): 7 Days to Die Tarzi Mimari Sekiller (ShapeID >= 1)
            for (int y = 0; y < gridY; y++)
            {
                for (int z = 0; z < gridZ; z++)
                {
                    for (int x = 0; x < gridX; x++)
                    {
                        uint raw = chunkVoxels65536[(y << 8) | (z << 4) | x];
                        byte matId = VoxelBitUtility.GetMaterialId(raw);
                        if (matId == 0)
                        {
                            continue;
                        }

                        byte shapeId = VoxelBitUtility.GetShapeId(raw);
                        if (shapeId == 0)
                        {
                            continue; // Kanal 1'de (Surface Nets SDF) islendi
                        }

                        byte rot = VoxelBitUtility.GetRotationState(raw);
                        byte dmg = VoxelBitUtility.GetDamageState(raw);
                        EmitArchitecturalShape(
                            new Vector3(x, y, z),
                            matId,
                            shapeId,
                            rot,
                            dmg,
                            outTriangles);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetNaturalSdfSample(ReadOnlySpan<uint> chunkVoxels65536, int x, int y, int z)
        {
            uint raw = chunkVoxels65536[(y << 8) | (z << 4) | x];
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == 0)
            {
                return 0u;
            }

            byte shape = VoxelBitUtility.GetShapeId(raw);
            if (shape >= 1)
            {
                // Mimari bloklar kendi Kanal 2B geometrisini cizer; ancak altindaki dogal zeminle
                // Skirt birlesimi icin zeminde hafif pozitif SDF destegi birakir
                return 0u;
            }

            return raw;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNaturalSdfSolid(ReadOnlySpan<uint> chunkVoxels65536, int x, int y, int z)
        {
            uint raw = GetNaturalSdfSample(chunkVoxels65536, x, y, z);
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            if (mat == 0)
            {
                return false;
            }
            return VoxelBitUtility.GetDensityOrLiquid(raw) >= IsoLevel;
        }

        private static void EmitDualQuadAsFacetedTriangles(
            Vector3[] dualVertices,
            byte[] dualMaterials,
            byte[] dualAo,
            bool[] dualValid,
            int i0,
            int i1,
            int i2,
            int i3,
            bool flipWinding,
            uint seedHash,
            List<FacetedVoxelTriangle> outTriangles)
        {
            if (!dualValid[i0] || !dualValid[i1] || !dualValid[i2] || !dualValid[i3])
            {
                return;
            }

            Vector3 v0 = dualVertices[i0];
            Vector3 v1 = dualVertices[i1];
            Vector3 v2 = dualVertices[i2];
            Vector3 v3 = dualVertices[i3];

            byte matA = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i1], dualMaterials[i2]);
            byte matB = SelectDominantMaterial(dualMaterials[i0], dualMaterials[i2], dualMaterials[i3]);

            byte aoA = (byte)((dualAo[i0] + dualAo[i1] + dualAo[i2]) / 3);
            byte aoB = (byte)((dualAo[i0] + dualAo[i2] + dualAo[i3]) / 3);

            //Her ufak ucgen poligona (faset) kendine ozgu hafif ton farki (110..245) vererek
            // Deep Rock Galactic / Astroneer kristal faset gorunumunu guclendir
            byte shadeA = (byte)(128 + (int)(seedHash & 0x7Fu) - 48);
            byte shadeB = (byte)(128 + (int)((seedHash >> 7) & 0x7Fu) - 48);

            // Quad'i daha kisa olan kosegen boyunca iki keskin ucgense bol
            float diag02 = Vector3.DistanceSquared(v0, v2);
            float diag13 = Vector3.DistanceSquared(v1, v3);

            if (diag02 <= diag13)
            {
                if (!flipWinding)
                {
                    AddFacetedTriangle(v0, v1, v2, matA, shadeA, aoA, outTriangles);
                    AddFacetedTriangle(v0, v2, v3, matB, shadeB, aoB, outTriangles);
                }
                else
                {
                    AddFacetedTriangle(v0, v2, v1, matA, shadeA, aoA, outTriangles);
                    AddFacetedTriangle(v0, v3, v2, matB, shadeB, aoB, outTriangles);
                }
            }
            else
            {
                if (!flipWinding)
                {
                    AddFacetedTriangle(v1, v2, v3, matA, shadeA, aoA, outTriangles);
                    AddFacetedTriangle(v1, v3, v0, matB, shadeB, aoB, outTriangles);
                }
                else
                {
                    AddFacetedTriangle(v1, v3, v2, matA, shadeA, aoA, outTriangles);
                    AddFacetedTriangle(v1, v0, v3, matB, shadeB, aoB, outTriangles);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddFacetedTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            byte matId,
            byte facetShade,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 cross = Vector3.Cross(ab, ac);
            float lenSq = cross.LengthSquared();
            if (lenSq < 1e-8f)
            {
                return;
            }

            Vector3 normal = cross / MathF.Sqrt(lenSq);
            byte temp = matId == 10 ? (byte)245 : (byte)20;

            outTriangles.Add(new FacetedVoxelTriangle
            {
                V0 = a,
                V1 = b,
                V2 = c,
                Normal = normal,
                MaterialId = matId,
                Temperature = temp,
                FacetShade = facetShade,
                AmbientOcclusion = ao
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddFacetedQuad(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 v3,
            byte matId,
            byte shadeA,
            byte shadeB,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            AddFacetedTriangle(v0, v1, v2, matId, shadeA, ao, outTriangles);
            AddFacetedTriangle(v0, v2, v3, matId, shadeB, ao, outTriangles);
        }

        /// <summary>
        /// GAME LOGIC BIBLE Bolum 2.7.A &amp; 2.7.B Kanal 2B:
        /// 7 Days to Die tarzi Radyal Sekil Carki (V tusu) mimari parcalarini ufak fasetli poligonlarla uretir.
        /// </summary>
        public static void EmitArchitecturalShape(
            Vector3 cellOrigin,
            byte matId,
            byte shapeId,
            byte rotationState,
            byte damageState,
            List<FacetedVoxelTriangle> outTriangles)
        {
            byte baseAo = (byte)Math.Clamp(245 - damageState * 6, 150, 250);
            Vector3 center = cellOrigin + new Vector3(0.5f, 0.5f, 0.5f);
            float yawRad = (rotationState & 3) * (MathF.PI * 0.5f);

            switch (shapeId)
            {
                case 2: // Sekizgen / Silindirik Tas Sutun (Octagonal Column with Capital & Plinth)
                {
                    EmitOctagonalPillar(cellOrigin, matId, baseAo, outTriangles);
                    break;
                }
                case 3: // 45 Derece Egimli Rampa / Cati (Sloped Roof / Ramp)
                {
                    EmitSlopedRamp(center, yawRad, matId, baseAo, outTriangles);
                    break;
                }
                case 4: // Kemer Tasi (Arch Voussoir - Parabolik/Romen Kemer Dilimi)
                {
                    EmitArchVoussoir(center, yawRad, matId, baseAo, outTriangles);
                    break;
                }
                case 5: // Kilit Tasi (Trapezoidal Keystone - Kemer Merkez Kilidi)
                {
                    EmitTrapezoidalKeystone(center, yawRad, matId, baseAo, outTriangles);
                    break;
                }
                case 6: // Pahli Savunma Duvari / Mazgal (Beveled Battlement Wall)
                {
                    EmitBeveledBattlementWall(center, yawRad, matId, baseAo, outTriangles);
                    break;
                }
                case 7: // Fasetli Cevher / Kristal Salkimi (Faceted Crystal Cluster)
                {
                    EmitFacetedCrystalCluster(center, matId, baseAo, outTriangles);
                    break;
                }
                default: // ShapeID == 1 veya 63: Pahli (Chamfered) Mimari Kesme Tas Blok
                {
                    EmitChamferedMasonryBlock(center, matId, baseAo, outTriangles);
                    break;
                }
            }
        }

        private static void EmitOctagonalPillar(
            Vector3 cellOrigin,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            const int sides = 8;
            float cx = cellOrigin.X + 0.5f;
            float cz = cellOrigin.Z + 0.5f;
            float y0 = cellOrigin.Y;
            float y1 = cellOrigin.Y + 0.16f;
            float y2 = cellOrigin.Y + 0.84f;
            float y3 = cellOrigin.Y + 1.0f;

            float rBase = 0.46f;
            float rShaft = 0.36f;

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f + (MathF.PI / 8f);
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f + (MathF.PI / 8f);

                float c0 = MathF.Cos(a0);
                float s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1);
                float s1 = MathF.Sin(a1);

                byte shade1 = (byte)(155 + (i % 3) * 22);
                byte shade2 = (byte)(140 + ((i + 1) % 3) * 22);

                // Alt kaide pahli halkasi
                Vector3 b0 = new Vector3(cx + c0 * rBase, y0, cz + s0 * rBase);
                Vector3 b1 = new Vector3(cx + c1 * rBase, y0, cz + s1 * rBase);
                Vector3 m0 = new Vector3(cx + c0 * rShaft, y1, cz + s0 * rShaft);
                Vector3 m1 = new Vector3(cx + c1 * rShaft, y1, cz + s1 * rShaft);
                AddFacetedQuad(b0, m0, m1, b1, matId, shade1, shade2, ao, outTriangles);

                // Orta sekizgen sutun govdesi
                Vector3 t0 = new Vector3(cx + c0 * rShaft, y2, cz + s0 * rShaft);
                Vector3 t1 = new Vector3(cx + c1 * rShaft, y2, cz + s1 * rShaft);
                AddFacetedQuad(m0, t0, t1, m1, matId, shade1, shade2, ao, outTriangles);

                // Ust baslik (Capital) pahli halkasi
                Vector3 u0 = new Vector3(cx + c0 * rBase, y3, cz + s0 * rBase);
                Vector3 u1 = new Vector3(cx + c1 * rBase, y3, cz + s1 * rBase);
                AddFacetedQuad(t0, u0, u1, t1, matId, shade1, shade2, ao, outTriangles);

                // Ust ve alt kapaklar
                AddFacetedTriangle(new Vector3(cx, y3, cz), u1, u0, matId, 210, ao, outTriangles);
                AddFacetedTriangle(new Vector3(cx, y0, cz), b0, b1, matId, 130, ao, outTriangles);
            }
        }

        private static void EmitSlopedRamp(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            // Pahli 45 derece egimli cati/rampa prizmasi
            Vector3 bFL = RotateY(new Vector3(-0.48f, -0.50f, -0.48f), center, yawRad);
            Vector3 bFR = RotateY(new Vector3(0.48f, -0.50f, -0.48f), center, yawRad);
            Vector3 bBL = RotateY(new Vector3(-0.48f, -0.50f, 0.48f), center, yawRad);
            Vector3 bBR = RotateY(new Vector3(0.48f, -0.50f, 0.48f), center, yawRad);

            Vector3 tBL = RotateY(new Vector3(-0.46f, 0.50f, 0.25f), center, yawRad);
            Vector3 tBR = RotateY(new Vector3(0.46f, 0.50f, 0.25f), center, yawRad);
            Vector3 tBackL = RotateY(new Vector3(-0.48f, 0.50f, 0.48f), center, yawRad);
            Vector3 tBackR = RotateY(new Vector3(0.48f, 0.50f, 0.48f), center, yawRad);

            // Ana egimli rampa yuzeyi
            AddFacetedQuad(bFL, tBL, tBR, bFR, matId, 210, 195, ao, outTriangles);
            // Ust duz sahanlik seridi
            AddFacetedQuad(tBL, tBackL, tBackR, tBR, matId, 225, 215, ao, outTriangles);
            // Arka dikey yuzey
            AddFacetedQuad(bBR, tBackR, tBackL, bBL, matId, 150, 140, ao, outTriangles);
            // Alt taban
            AddFacetedQuad(bBL, bFL, bFR, bBR, matId, 120, 120, ao, outTriangles);
            // Yan ucgen/yamuk yanaklar
            AddFacetedTriangle(bFL, bBL, tBL, matId, 175, ao, outTriangles);
            AddFacetedTriangle(bBL, tBackL, tBL, matId, 165, ao, outTriangles);
            AddFacetedTriangle(bFR, tBR, bBR, matId, 175, ao, outTriangles);
            AddFacetedTriangle(bBR, tBR, tBackR, matId, 165, ao, outTriangles);
        }

        private static void EmitArchVoussoir(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            // Kemer Tasi (Voussoir): Alti kavisli (2 fasetli kemer yayi), ustu basamak/pahli kesme tas
            Vector3 outerBL = RotateY(new Vector3(-0.48f, -0.15f, -0.44f), center, yawRad);
            Vector3 outerBR = RotateY(new Vector3(-0.48f, -0.15f, 0.44f), center, yawRad);
            Vector3 archMidL = RotateY(new Vector3(-0.05f, 0.18f, -0.42f), center, yawRad);
            Vector3 archMidR = RotateY(new Vector3(-0.05f, 0.18f, 0.42f), center, yawRad);
            Vector3 innerTL = RotateY(new Vector3(0.48f, 0.36f, -0.42f), center, yawRad);
            Vector3 innerTR = RotateY(new Vector3(0.48f, 0.36f, 0.42f), center, yawRad);

            Vector3 topL0 = RotateY(new Vector3(-0.48f, 0.50f, -0.44f), center, yawRad);
            Vector3 topR0 = RotateY(new Vector3(-0.48f, 0.50f, 0.44f), center, yawRad);
            Vector3 topL1 = RotateY(new Vector3(0.48f, 0.50f, -0.44f), center, yawRad);
            Vector3 topR1 = RotateY(new Vector3(0.48f, 0.50f, 0.44f), center, yawRad);

            // Alt kavisli kemer karin yuzeyi (Intrados - 2 fasetli)
            AddFacetedQuad(outerBL, outerBR, archMidR, archMidL, matId, 155, 145, ao, outTriangles);
            AddFacetedQuad(archMidL, archMidR, innerTR, innerTL, matId, 165, 150, ao, outTriangles);

            // Ust duz/pahli sirt (Extrados)
            AddFacetedQuad(topL0, topL1, topR1, topR0, matId, 220, 205, ao, outTriangles);

            // Sol ve sag yuzler
            AddFacetedQuad(outerBL, topL0, topR0, outerBR, matId, 180, 170, ao, outTriangles);
            AddFacetedQuad(innerTR, topR1, topL1, innerTL, matId, 180, 170, ao, outTriangles);

            // On ve arka kemer yanaklari
            AddFacetedQuad(outerBL, archMidL, topL1, topL0, matId, 195, 185, ao, outTriangles);
            AddFacetedTriangle(archMidL, innerTL, topL1, matId, 200, ao, outTriangles);

            AddFacetedQuad(outerBR, topR0, topR1, archMidR, matId, 195, 185, ao, outTriangles);
            AddFacetedTriangle(archMidR, topR1, innerTR, matId, 200, ao, outTriangles);
        }

        private static void EmitTrapezoidalKeystone(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            // Kilit Tasi (Keystone): Yukaridan asagi daralan kama/yamuk form + on/arka piramit kabartma
            EmitTaperedBox(
                center,
                yawRad,
                bottomHalfX: 0.30f,
                bottomHalfZ: 0.46f,
                topHalfX: 0.50f,
                topHalfZ: 0.48f,
                minY: -0.42f,
                maxY: 0.52f,
                matId,
                ao,
                outTriangles);
        }

        private static void EmitBeveledBattlementWall(
            Vector3 center,
            float yawRad,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            // Pahli ince kale savunma duvari / mazgal
            EmitTaperedBox(
                center,
                yawRad,
                bottomHalfX: 0.49f,
                bottomHalfZ: 0.28f,
                topHalfX: 0.46f,
                topHalfZ: 0.20f,
                minY: -0.50f,
                maxY: 0.50f,
                matId,
                ao,
                outTriangles);
        }

        private static void EmitFacetedCrystalCluster(
            Vector3 center,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            // 3 adet farkli acilarla fiskiran altigen prizma + piramit uclu kristal salkimi
            EmitSingleCrystalSpire(center + new Vector3(0f, -0.45f, 0f), new Vector3(0.05f, 0.95f, -0.04f), 0.22f, matId, ao, outTriangles);
            EmitSingleCrystalSpire(center + new Vector3(-0.18f, -0.45f, 0.12f), new Vector3(-0.22f, 0.68f, 0.16f), 0.15f, matId, ao, outTriangles);
            EmitSingleCrystalSpire(center + new Vector3(0.18f, -0.45f, -0.10f), new Vector3(0.24f, 0.62f, -0.14f), 0.14f, matId, ao, outTriangles);
        }

        private static void EmitSingleCrystalSpire(
            Vector3 basePos,
            Vector3 axisVec,
            float radius,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            const int sides = 6;
            Vector3 midPos = basePos + axisVec * 0.72f;
            Vector3 tipPos = basePos + axisVec * 1.12f;

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * MathF.PI * 2f;

                Vector3 r0 = new Vector3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius);
                Vector3 r1 = new Vector3(MathF.Cos(a1) * radius, 0f, MathF.Sin(a1) * radius);

                Vector3 b0 = basePos + r0;
                Vector3 b1 = basePos + r1;
                Vector3 m0 = midPos + r0 * 0.82f;
                Vector3 m1 = midPos + r1 * 0.82f;

                byte shade = (byte)(170 + (i % 3) * 30);
                AddFacetedQuad(b0, m0, m1, b1, matId, shade, (byte)(shade - 15), ao, outTriangles);
                AddFacetedTriangle(m0, tipPos, m1, matId, (byte)Math.Min(255, shade + 25), ao, outTriangles);
            }
        }

        private static void EmitChamferedMasonryBlock(
            Vector3 center,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            EmitTaperedBox(
                center,
                yawRad: 0f,
                bottomHalfX: 0.49f,
                bottomHalfZ: 0.49f,
                topHalfX: 0.46f,
                topHalfZ: 0.46f,
                minY: -0.50f,
                maxY: 0.50f,
                matId,
                ao,
                outTriangles);
        }

        private static void EmitTaperedBox(
            Vector3 center,
            float yawRad,
            float bottomHalfX,
            float bottomHalfZ,
            float topHalfX,
            float topHalfZ,
            float minY,
            float maxY,
            byte matId,
            byte ao,
            List<FacetedVoxelTriangle> outTriangles)
        {
            Vector3 b0 = RotateY(new Vector3(-bottomHalfX, minY, -bottomHalfZ), center, yawRad);
            Vector3 b1 = RotateY(new Vector3(bottomHalfX, minY, -bottomHalfZ), center, yawRad);
            Vector3 b2 = RotateY(new Vector3(bottomHalfX, minY, bottomHalfZ), center, yawRad);
            Vector3 b3 = RotateY(new Vector3(-bottomHalfX, minY, bottomHalfZ), center, yawRad);

            Vector3 t0 = RotateY(new Vector3(-topHalfX, maxY, -topHalfZ), center, yawRad);
            Vector3 t1 = RotateY(new Vector3(topHalfX, maxY, -topHalfZ), center, yawRad);
            Vector3 t2 = RotateY(new Vector3(topHalfX, maxY, topHalfZ), center, yawRad);
            Vector3 t3 = RotateY(new Vector3(-topHalfX, maxY, topHalfZ), center, yawRad);

            // Ust ve alt
            AddFacetedQuad(t0, t3, t2, t1, matId, 225, 210, ao, outTriangles);
            AddFacetedQuad(b0, b1, b2, b3, matId, 130, 120, ao, outTriangles);

            // 4 yan yuz
            AddFacetedQuad(b0, t0, t1, b1, matId, 190, 175, ao, outTriangles);
            AddFacetedQuad(b1, t1, t2, b2, matId, 180, 165, ao, outTriangles);
            AddFacetedQuad(b2, t2, t3, b3, matId, 175, 160, ao, outTriangles);
            AddFacetedQuad(b3, t3, t0, b0, matId, 185, 170, ao, outTriangles);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 RotateY(Vector3 localOffset, Vector3 center, float yawRad)
        {
            float c = MathF.Cos(yawRad);
            float s = MathF.Sin(yawRad);
            return new Vector3(
                center.X + localOffset.X * c - localOffset.Z * s,
                center.Y + localOffset.Y,
                center.Z + localOffset.X * s + localOffset.Z * c);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 GetCornerOffset(int cornerIndex)
        {
            return new Vector3(
                cornerIndex & 1,
                (cornerIndex >> 2) & 1,
                (cornerIndex >> 1) & 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ComputeDeterministicFacetJitter(int wx, int wy, int wz)
        {
            uint hX = HashCoord(wx, wy, wz, 101u);
            uint hY = HashCoord(wx, wy, wz, 211u);
            uint hZ = HashCoord(wx, wy, wz, 307u);

            float jx = (((hX & 0xFFu) / 255f) - 0.5f) * 0.34f;
            float jy = (((hY & 0xFFu) / 255f) - 0.5f) * 0.28f;
            float jz = (((hZ & 0xFFu) / 255f) - 0.5f) * 0.34f;
            return new Vector3(jx, jy, jz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashCoord(int x, int y, int z, uint salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (salt * 2654435761u);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte SelectDominantMaterial(byte m0, byte m1, byte m2)
        {
            byte best = m0 != 0 ? m0 : (byte)1;
            byte bestPrio = GetMaterialVisualPriority(best);

            byte p1 = GetMaterialVisualPriority(m1);
            if (m1 != 0 && p1 > bestPrio)
            {
                best = m1;
                bestPrio = p1;
            }

            byte p2 = GetMaterialVisualPriority(m2);
            if (m2 != 0 && p2 > bestPrio)
            {
                best = m2;
            }

            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte GetMaterialVisualPriority(byte matId)
        {
            return matId switch
            {
                0 => 0,
                1 => 10,  //Bazalt / Granit Kaya
                2 => 15,  // Toprak
                3 => 25,  //imenli Ust Toprak
                4 => 40,  // Demir Cevheri
                5 => 42,  // Bakir Cevheri
                6 => 48,  // Altin Cevheri
                7 => 55,  // Titanyum / Mitril
                8 => 52,  // Obsidyen
                9 => 30,  // Su
                10 => 60, // Magma / Lav
                _ => 20
            };
        }
    }
}
