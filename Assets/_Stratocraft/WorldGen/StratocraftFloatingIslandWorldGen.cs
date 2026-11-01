#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// GAME LOGIC BIBLE Bolum 2.2 ve 2.6:
    /// Yuzen adanin 3B kapali hacimsel voksel arazisini (ust plato + falez duvarlari + alt bazalt omurga),
    /// sonlu akiskan (su/lav) cukurlarini, catenary dome magarayi ve 7DTD mimari kemer/sutunlarini
    /// hem Editor sahne insasinda hem de Play Mode calisma aninda sifir sapmayla ureten deterministik dunya ureteci.
    /// Pure C# (.NET 10 ve Unity 6 uyumlu).
    /// </summary>
    public static class StratocraftFloatingIslandWorldGen
    {
        public const int MaxEditableVoxelY = 30;
        public const int ChunkMin = -2;
        public const int ChunkMax = 1;

        private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
        private static int RoundToInt(float v) => (int)MathF.Round(v);

        public static float ComputeIslandCoastRadius(float worldX, float worldZ)
        {
            float angle = MathF.Atan2(worldZ, worldX);
            return 20.8f
                + MathF.Sin(angle * 3f + 0.65f) * 2.25f
                + MathF.Cos(angle * 5f - 0.40f) * 1.35f
                + MathF.Sin(angle * 7f + 1.20f) * 0.65f;
        }

        public static float SampleIslandTopSurfaceY(float worldX, float worldZ)
        {
            float ridge = MathF.Sin(worldX * 0.16f) * 1.45f + MathF.Cos(worldZ * 0.15f) * 1.35f;
            float detail = MathF.Sin((worldX + worldZ) * 0.29f) * 0.48f;
            float baseH = 9.35f + ridge + detail;

            // Merkez atolye, CAS sandigi ve 7DTD kemer meydanini duzlestir (y ~ 9.15m)
            float distPlaza = MathF.Sqrt(worldX * worldX + (worldZ + 2.2f) * (worldZ + 2.2f));
            if (distPlaza < 8.4f)
            {
                float t = Clamp01(distPlaza / 8.4f);
                baseH = Lerp(9.15f, baseH, t * t);
            }

            // Kuzeybati (worldX < -1.0, worldZ > 1.2) Yurunebilir Parabolik Kubbe Magara Dagi
            if (worldX < -1.0f && worldZ > 1.2f)
            {
                float hillX = Clamp01((-worldX - 1.0f) / 9.5f);
                float hillZ = Clamp01((worldZ - 1.2f) / 9.5f);
                baseH += hillX * hillZ * 6.8f;
            }

            // Kiyi falezlerine yaklasirken hafif pahli kaya omzu
            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            float coastR = ComputeIslandCoastRadius(worldX, worldZ);
            if (r > coastR - 3.2f)
            {
                float rimT = Clamp01((r - (coastR - 3.2f)) / 3.2f);
                baseH -= rimT * 1.45f;
            }

            return Math.Clamp(baseH, 6.2f, 18.2f);
        }

        public static float SampleIslandUnderbellyY(float worldX, float worldZ)
        {
            float r = MathF.Sqrt(worldX * worldX + worldZ * worldZ);
            float coastR = ComputeIslandCoastRadius(worldX, worldZ);
            float normR = Clamp01(r / MathF.Max(1f, coastR));

            float crag = MathF.Sin(worldX * 0.27f + 0.5f) * 0.85f + MathF.Cos(worldZ * 0.25f - 0.3f) * 0.85f;
            float spire = MathF.Sin(worldX * 0.48f) * MathF.Cos(worldZ * 0.44f) * 0.75f;
            float underY = 2.35f + (normR * normR) * 4.45f + crag * 0.55f + spire * 0.45f;
            return Math.Clamp(underY, 1.8f, 7.6f);
        }

        public static (uint[] voxels, ushort[] fluids) PopulateVolumetricFloatingIslandChunk(int chunkX, int chunkZ)
        {
            uint[] voxels = new uint[65536];
            ushort[] fluids = new ushort[65536];
            int worldOffsetX = chunkX * 16;
            int worldOffsetZ = chunkZ * 16;

            for (int lz = 0; lz < 16; lz++)
            {
                int wz = worldOffsetZ + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    int wx = worldOffsetX + lx;
                    float r = MathF.Sqrt(wx * wx + wz * wz);
                    float coastR = ComputeIslandCoastRadius(wx, wz);

                    float radialMargin = coastR - r;
                    if (radialMargin < -1.5f)
                    {
                        continue;
                    }

                    float topH = SampleIslandTopSurfaceY(wx, wz);
                    float bottomH = SampleIslandUnderbellyY(wx, wz);

                    // Guneydogu Volkanik Lav Kalderasi & Kaynak Suyu Havuzu + Stokiyometrik Obsidyen Damari
                    bool isObsidianSeam = radialMargin > 1.2f &&
                                          wz >= -9.8f && wz <= -4.6f &&
                                          wx >= 4.8f && wx <= 6.2f;

                    float calderaDx = wx - 8.2f;
                    float calderaDz = wz - (-7.2f);
                    float calderaDist = MathF.Sqrt(calderaDx * calderaDx + calderaDz * calderaDz);
                    bool inLavaCaldera = !isObsidianSeam && calderaDist < 4.1f && radialMargin > 1.2f;
                    if (inLavaCaldera)
                    {
                        topH = MathF.Min(topH, 9.15f);
                    }

                    float waterDx = wx - 3.1f;
                    float waterDz = wz - (-7.2f);
                    float waterPoolDist = MathF.Sqrt(waterDx * waterDx + waterDz * waterDz);
                    bool inWaterPool = !isObsidianSeam && waterPoolDist < 2.9f && !inLavaCaldera && radialMargin > 1.2f;
                    if (inWaterPool)
                    {
                        topH = MathF.Min(topH, 9.15f);
                    }

                    if (isObsidianSeam)
                    {
                        topH = MathF.Min(topH, 9.35f);
                    }

                    for (int y = 1; y < MaxEditableVoxelY - 1; y++)
                    {
                        int idx = (y << 8) | (lz << 4) | lx;

                        if (inLavaCaldera && y >= 7 && y <= 9)
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: true).RawValue;
                            continue;
                        }

                        if (inWaterPool && y >= 7 && y <= 9)
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Water, 255, isSettled: true).RawValue;
                            continue;
                        }

                        float distFromTop = topH - y;
                        float distFromBottom = y - bottomH;
                        float cliffSdf = radialMargin * 1.35f + MathF.Sin(y * 0.55f + wx * 0.22f) * 0.35f;

                        float signedInside = MathF.Min(MathF.Min(distFromTop, distFromBottom), cliffSdf);
                        int densityInt = RoundToInt(64f + signedInside * 28f);

                        // Kuzeybati Parabolik Kubbe Magara + Magara Giris Kemeri
                        const float caveCenterX = -8.2f;
                        const float caveCenterZ = 8.2f;
                        float dxCave = wx - caveCenterX;
                        float dzCave = wz - caveCenterZ;
                        float radialCaveSq = dxCave * dxCave + dzCave * dzCave;
                        float domeRoofY = 13.8f - 0.17f * radialCaveSq;

                        bool isStalagnatePillar = radialCaveSq < 1.45f;
                        bool inCaveDome = !isStalagnatePillar && y >= 9 && y <= domeRoofY && radialCaveSq < 23.0f;

                        float portalDist = MathF.Abs((wx + 5.2f) * 0.72f + (wz - 5.2f) * 0.72f);
                        bool inCaveMouth = wx >= -7.8f && wx <= -2.8f && wz >= 2.6f && wz <= 7.2f &&
                                           portalDist < 2.2f && y >= 9 && y <= 12;

                        if (inCaveDome || inCaveMouth)
                        {
                            densityInt = Math.Min(densityInt, 14);
                        }

                        if (densityInt < 10)
                        {
                            continue;
                        }

                        byte clampedDensity = (byte)Math.Clamp(densityInt, 10, 127);
                        byte matId = ResolveGeologicalMaterial(
                            wx,
                            y,
                            wz,
                            topH,
                            bottomH,
                            radialMargin,
                            inLavaCaldera,
                            inWaterPool,
                            isObsidianSeam,
                            isStalagnatePillar);

                        voxels[idx] = VoxelBitUtility.PackStandard(
                            matId: matId,
                            density: clampedDensity,
                            shapeId: 0,
                            rot24: 0,
                            origin: BlokKokenDurumu.DogalKutle,
                            dmg4: 0);
                    }
                }
            }

            PopulateArchitecturalStructuresForChunk(chunkX, chunkZ, voxels);
            return (voxels, fluids);
        }

        public static byte ResolveGeologicalMaterial(
            int wx,
            int y,
            int wz,
            float topH,
            float bottomH,
            float radialMargin,
            bool inLavaCaldera,
            bool inWaterPool,
            bool isObsidianSeam,
            bool isStalagnatePillar)
        {
            if (isObsidianSeam)
            {
                return MaterialIdCatalog.NaturalObsidian;
            }

            if (inLavaCaldera)
            {
                return y >= 5 ? MaterialIdCatalog.MagmaBlock : MaterialIdCatalog.Basalt;
            }

            if (inWaterPool)
            {
                return MaterialIdCatalog.Sand;
            }

            if (y <= bottomH + 1.55f || radialMargin < 0.85f)
            {
                return y <= bottomH + 1.55f ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone;
            }

            if (y + 1.55f >= topH)
            {
                if (topH > 12.6f || isStalagnatePillar)
                {
                    return MaterialIdCatalog.Stone;
                }
                return MaterialIdCatalog.Grass;
            }

            if (y + 2.85f >= topH)
            {
                return MaterialIdCatalog.Dirt;
            }

            int oreHash = Math.Abs(wx * 73 + y * 37 + wz * 19) & 31;
            return oreHash switch
            {
                0 => MaterialIdCatalog.GoldOre,
                1 => MaterialIdCatalog.MithrilOre,
                2 => MaterialIdCatalog.IronOre,
                3 => MaterialIdCatalog.CopperOre,
                4 => MaterialIdCatalog.AstralOre,
                _ => (y < 6 ? MaterialIdCatalog.Basalt : MaterialIdCatalog.Stone)
            };
        }

        public static void PopulateArchitecturalStructuresForChunk(int chunkX, int chunkZ, uint[] voxels)
        {
            if (chunkX == -1 && chunkZ == -1)
            {
                const int baseY = 9;
                const int archLz = 14;

                SetArchitecturalVoxel(voxels, 6, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 63, rot: 0);
                SetArchitecturalVoxel(voxels, 10, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 63, rot: 1);

                for (int dy = 1; dy <= 2; dy++)
                {
                    SetArchitecturalVoxel(voxels, 6, baseY + dy, archLz, MaterialIdCatalog.Stone, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, 10, baseY + dy, archLz, MaterialIdCatalog.Stone, shapeId: 2, rot: 0);
                }

                SetArchitecturalVoxel(voxels, 6, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 7, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 0);
                SetArchitecturalVoxel(voxels, 8, baseY + 3, archLz, MaterialIdCatalog.GoldOre, shapeId: 5, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 2);
                SetArchitecturalVoxel(voxels, 10, baseY + 3, archLz, MaterialIdCatalog.Stone, shapeId: 4, rot: 2);

                SetArchitecturalVoxel(voxels, 5, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 3, rot: 1);
                SetArchitecturalVoxel(voxels, 11, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 3, rot: 3);
                SetArchitecturalVoxel(voxels, 4, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 4, baseY + 1, archLz, MaterialIdCatalog.Stone, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY, archLz, MaterialIdCatalog.Stone, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY + 1, archLz, MaterialIdCatalog.Stone, shapeId: 6, rot: 0);

                SetArchitecturalVoxel(voxels, 13, baseY, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == 0 && chunkZ == -1)
            {
                SetArchitecturalVoxel(voxels, 5, 9, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 10, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, 9, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
                SetArchitecturalVoxel(voxels, 12, 9, 7, MaterialIdCatalog.StarCore, shapeId: 7, rot: 1);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                int p1Y = RoundToInt(SampleIslandTopSurfaceY(5f, 5f));
                SetArchitecturalVoxel(voxels, 5, p1Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 0);
                SetArchitecturalVoxel(voxels, 5, p1Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, p1Y + 2, 5, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);

                int p2Y = RoundToInt(SampleIslandTopSurfaceY(11f, 5f));
                SetArchitecturalVoxel(voxels, 11, p2Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 1);
                SetArchitecturalVoxel(voxels, 11, p2Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, p2Y + 2, 5, MaterialIdCatalog.StarCore, shapeId: 7, rot: 0);
            }
        }

        public static void SetArchitecturalVoxel(
            uint[] voxels,
            int lx,
            int ly,
            int lz,
            byte matId,
            byte shapeId,
            byte rot)
        {
            if (lx < 0 || lx >= 16 || ly < 0 || ly >= 256 || lz < 0 || lz >= 16)
            {
                return;
            }

            int idx = (ly << 8) | (lz << 4) | lx;
            voxels[idx] = VoxelBitUtility.PackStandard(
                matId: matId,
                density: VoxelBitUtility.FullDensity,
                shapeId: shapeId,
                rot24: rot,
                origin: BlokKokenDurumu.OyuncuItemBlogu,
                dmg4: 0);
        }
    }
}
