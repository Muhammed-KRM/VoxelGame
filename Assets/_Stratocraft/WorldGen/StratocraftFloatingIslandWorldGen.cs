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
    /// %100 sizdirmaz (FullDensity=127) sonlu akiskan (su/lav) canaklarini, catenary dome magarayi,
    /// tohum (ActiveWorldSeed) tabanli kesilebilir/devrilebilir voksel agaclari ve cok duvarli yikilabilir
    /// 7DTD mimari kule/tapinak/kemer yapilarini ureten deterministik dunya ureteci.
    /// Pure C# (.NET 10 ve Unity 6 uyumlu).
    /// </summary>
    public static class StratocraftFloatingIslandWorldGen
    {
        public const int MaxEditableVoxelY = 30;
        public const int ChunkMin = -2;
        public const int ChunkMax = 1;

        /// <summary>Aktif dunya tohumu (Varsayilan 1453; Play Mode icinde [F7] ile rastgele degistirilebilir).</summary>
        public static int ActiveWorldSeed { get; set; } = 1453;

        private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
        private static int RoundToInt(float v) => (int)MathF.Round(v);

        private static float SeedPhaseOffset(int salt)
        {
            int s = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 73856093) ^ (salt * 19349663));
            return (s & 1023) * (MathF.PI * 2f / 1024f);
        }

        public static float ComputeIslandCoastRadius(float worldX, float worldZ)
        {
            float angle = MathF.Atan2(worldZ, worldX);
            float p1 = SeedPhaseOffset(1);
            float p2 = SeedPhaseOffset(2);
            float p3 = SeedPhaseOffset(3);
            return 21.4f
                + MathF.Sin(angle * 3f + 0.65f + p1) * 2.25f
                + MathF.Cos(angle * 5f - 0.40f + p2) * 1.35f
                + MathF.Sin(angle * 7f + 1.20f + p3) * 0.65f;
        }

        public static float SampleIslandTopSurfaceY(float worldX, float worldZ)
        {
            float p1 = SeedPhaseOffset(4);
            float p2 = SeedPhaseOffset(5);
            float ridge = MathF.Sin(worldX * 0.16f + p1) * 1.45f + MathF.Cos(worldZ * 0.15f + p2) * 1.35f;
            float detail = MathF.Sin((worldX + worldZ) * 0.29f + p1 * 0.5f) * 0.48f;
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

            // Guneydogu Su Havuzu ve Lav Kalderasi altinda kalin bazalt taban garantisi (delinmeyi onler)
            if (worldX >= -1.0f && worldX <= 13.5f && worldZ >= -12.5f && worldZ <= -3.0f)
            {
                underY = MathF.Min(underY, 2.8f);
            }

            return Math.Clamp(underY, 1.8f, 7.6f);
        }

        private static void EvaluateBasinState(
            int wx,
            int wz,
            float radialMargin,
            out bool inWaterPool,
            out bool inLavaCaldera,
            out bool isObsidianSeam,
            out bool inBasinWall)
        {
            isObsidianSeam = radialMargin > 1.2f &&
                             wx >= 5 && wx <= 6 &&
                             wz >= -10 && wz <= -4;

            float calderaDx = wx - 8.5f;
            float calderaDz = wz - (-7.2f);
            float calderaDist = MathF.Sqrt(calderaDx * calderaDx + calderaDz * calderaDz);
            inLavaCaldera = !isObsidianSeam &&
                            wx >= 7 && wx <= 11 &&
                            wz >= -10 && wz <= -5 &&
                            calderaDist < 3.1f &&
                            radialMargin > 2.2f;

            float waterDx = wx - 2.6f;
            float waterDz = wz - (-7.2f);
            float waterPoolDist = MathF.Sqrt(waterDx * waterDx + waterDz * waterDz);
            inWaterPool = !isObsidianSeam &&
                          !inLavaCaldera &&
                          wx >= 1 && wx <= 4 &&
                          wz >= -9 && wz <= -5 &&
                          waterPoolDist < 2.25f &&
                          radialMargin > 2.2f;

            bool nearWater = wx >= 0 && wx <= 5 && wz >= -11 && wz <= -3 && waterPoolDist < 3.65f;
            bool nearLava = wx >= 6 && wx <= 13 && wz >= -11 && wz <= -3 && calderaDist < 4.55f;
            inBasinWall = !inWaterPool && !inLavaCaldera && (isObsidianSeam || nearWater || nearLava);
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

                    EvaluateBasinState(
                        wx, wz, radialMargin,
                        out bool inWaterPool,
                        out bool inLavaCaldera,
                        out bool isObsidianSeam,
                        out bool inBasinWall);

                    if (inLavaCaldera || inWaterPool)
                    {
                        topH = 6.75f;
                        bottomH = MathF.Min(bottomH, 2.5f);
                    }
                    else if (inBasinWall)
                    {
                        topH = MathF.Max(topH, 9.65f);
                        bottomH = MathF.Min(bottomH, 2.8f);
                    }

                    for (int y = 1; y < MaxEditableVoxelY - 1; y++)
                    {
                        int idx = (y << 8) | (lz << 4) | lx;

                        if (inLavaCaldera && (y == 7 || y == 8))
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Lava, 255, isSettled: false, pressureHead: y == 7 ? (byte)1 : (byte)0).RawValue;
                            continue;
                        }

                        if (inWaterPool && (y == 7 || y == 8))
                        {
                            fluids[idx] = FluidCell16.Pack(FluidType.Water, 255, isSettled: false, pressureHead: y == 7 ? (byte)1 : (byte)0).RawValue;
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

                        // %100 Sizdirmaz Su ve Lav Canagi Tabani (y=3..6) ve Canak Duvarlari (y=3..8)
                        if ((inWaterPool || inLavaCaldera) && y >= 3 && y <= 6)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
                        }
                        else if (inBasinWall && y >= 3 && y <= 8)
                        {
                            densityInt = VoxelBitUtility.FullDensity;
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
            PopulateIslandTreesForChunk(chunkX, chunkZ, voxels);
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
                if (y <= 4 && radialMargin > 5.0f && ((wx + wz) & 1) == 0)
                {
                    return MaterialIdCatalog.Bedrock;
                }
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

            int seedXor = ActiveWorldSeed * 19349663;
            int depthFromSurface = Math.Max(1, (int)(topH - y));
            int mappedGeologicalY = -(depthFromSurface * 12) - (Math.Abs(wx * 13 + wz * 29 + seedXor) % 45);
            int ringTier = Math.Clamp((Math.Abs(wx) + Math.Abs(wz)) / 6, 0, 6);
            float geoNoise01 = ((Math.Abs((wx * 73856093) ^ (y * 19349663) ^ (wz * 83492791) ^ seedXor) & 1023) / 1023.0f);
            byte sampledOre = OreDistributionTable.SampleOreMaterial(mappedGeologicalY, ringTier, geoNoise01);
            if (sampledOre != MaterialIdCatalog.Stone)
            {
                return sampledOre;
            }

            int oreHash = Math.Abs(wx * 73 + y * 37 + wz * 19 + seedXor) & 31;
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
                // Merkez Meydan Giris Kemeri ve Yan Sur Duvarlari
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
                // Kaldera ve Su Havuzu Gozlem Dikmeleri
                SetArchitecturalVoxel(voxels, 5, 9, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 5, 10, 12, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, 9, 11, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
                SetArchitecturalVoxel(voxels, 12, 9, 7, MaterialIdCatalog.StarCore, shapeId: 7, rot: 1);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                // Kuzeydogu Cok Duvarli Yikilabilir Gozetleme Kulesi (Domino Çökme Test Yapisi: lx=5..9, lz=4..8)
                int baseY = Math.Max(9, RoundToInt(SampleIslandTopSurfaceY(7f, 6f)));
                int[] cornerX = { 5, 9, 5, 9 };
                int[] cornerZ = { 4, 4, 8, 8 };

                // 4 Tasiyici Köse Sütunu (Ayaklar kirildiginda ust kat ve kubbe Domino sistemiyle coker!)
                for (int c = 0; c < 4; c++)
                {
                    int cx = cornerX[c];
                    int cz = cornerZ[c];
                    EnsureSolidFoundationUnder(voxels, cx, baseY, cz);
                    SetArchitecturalVoxel(voxels, cx, baseY, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: (byte)(c & 3));
                    SetArchitecturalVoxel(voxels, cx, baseY + 1, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, cx, baseY + 2, cz, MaterialIdCatalog.StoneBrick, shapeId: 2, rot: 0);
                }

                // Ust Kat Kemer Kirisleri ve Ahsap/Tas Tavan Cati Halkasi (y = baseY + 3)
                for (int x = 5; x <= 9; x++)
                {
                    byte shape = (x == 7) ? (byte)5 : (byte)4;
                    byte mat = (x == 7) ? MaterialIdCatalog.GoldOre : MaterialIdCatalog.StoneBrick;
                    SetArchitecturalVoxel(voxels, x, baseY + 3, 4, mat, shapeId: shape, rot: 0);
                    SetArchitecturalVoxel(voxels, x, baseY + 3, 8, mat, shapeId: shape, rot: 2);
                }
                for (int z = 5; z <= 7; z++)
                {
                    byte shape = (z == 6) ? (byte)5 : (byte)4;
                    SetArchitecturalVoxel(voxels, 5, baseY + 3, z, MaterialIdCatalog.StoneBrick, shapeId: shape, rot: 1);
                    SetArchitecturalVoxel(voxels, 9, baseY + 3, z, MaterialIdCatalog.StoneBrick, shapeId: shape, rot: 3);
                }

                // Kule Ust Teras Dosemesi ve Siperlik Mazgallari (y = baseY + 3..4)
                for (int z = 5; z <= 7; z++)
                {
                    for (int x = 6; x <= 8; x++)
                    {
                        SetArchitecturalVoxel(voxels, x, baseY + 3, z, MaterialIdCatalog.OakWood, shapeId: 1, rot: 0);
                    }
                }
                SetArchitecturalVoxel(voxels, 5, baseY + 4, 4, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 0);
                SetArchitecturalVoxel(voxels, 9, baseY + 4, 4, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 1);
                SetArchitecturalVoxel(voxels, 5, baseY + 4, 8, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 2);
                SetArchitecturalVoxel(voxels, 9, baseY + 4, 8, MaterialIdCatalog.StoneBrick, shapeId: 6, rot: 3);
                SetArchitecturalVoxel(voxels, 7, baseY + 4, 6, MaterialIdCatalog.StarCore, shapeId: 7, rot: 0);

                // Ek Bati Dikmesi (lx=11, lz=5)
                int p2Y = RoundToInt(SampleIslandTopSurfaceY(11f, 5f));
                EnsureSolidFoundationUnder(voxels, 11, p2Y, 5);
                SetArchitecturalVoxel(voxels, 11, p2Y, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 63, rot: 1);
                SetArchitecturalVoxel(voxels, 11, p2Y + 1, 5, MaterialIdCatalog.NaturalObsidian, shapeId: 2, rot: 0);
                SetArchitecturalVoxel(voxels, 11, p2Y + 2, 5, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
            else if (chunkX == -2 && chunkZ == -1)
            {
                // Guneybati Kadim Tapinak Kameriyesi (lx=10..14, lz=8..12)
                int baseY = Math.Max(8, RoundToInt(SampleIslandTopSurfaceY(-20f, -6f)));
                int[] colX = { 10, 14, 10, 14 };
                int[] colZ = { 8, 8, 12, 12 };
                for (int i = 0; i < 4; i++)
                {
                    EnsureSolidFoundationUnder(voxels, colX[i], baseY, colZ[i]);
                    SetArchitecturalVoxel(voxels, colX[i], baseY, colZ[i], MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                    SetArchitecturalVoxel(voxels, colX[i], baseY + 1, colZ[i], MaterialIdCatalog.Marble, shapeId: 2, rot: 0);
                }
                for (int x = 10; x <= 14; x++)
                {
                    SetArchitecturalVoxel(voxels, x, baseY + 2, 8, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                    SetArchitecturalVoxel(voxels, x, baseY + 2, 12, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                }
                for (int z = 9; z <= 11; z++)
                {
                    SetArchitecturalVoxel(voxels, 10, baseY + 2, z, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                    SetArchitecturalVoxel(voxels, 14, baseY + 2, z, MaterialIdCatalog.Marble, shapeId: 1, rot: 0);
                }
                EnsureSolidFoundationUnder(voxels, 12, baseY, 10);
                SetArchitecturalVoxel(voxels, 12, baseY, 10, MaterialIdCatalog.NaturalObsidian, shapeId: 1, rot: 0);
                SetArchitecturalVoxel(voxels, 12, baseY + 1, 10, MaterialIdCatalog.MithrilOre, shapeId: 7, rot: 0);
            }
        }

        /// <summary>
        /// Adanin farkli biyom bolgelerine tohum (ActiveWorldSeed) ile konumlanan, Balta [Slot 3] ile govdesi
        /// kesildiginde Domino Yapısal Çökme sistemiyle devrilen 3B voksel agaclar (Mese ve Demir Agaci) yerlestirir.
        /// </summary>
        public static void PopulateIslandTreesForChunk(int chunkX, int chunkZ, uint[] voxels)
        {
            int seedShiftX = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 17) % 3) - 1;
            int seedShiftZ = ActiveWorldSeed == 1453 ? 0 : ((ActiveWorldSeed * 31) % 3) - 1;

            if (chunkX == -1 && chunkZ == -1)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 2, 4), Math.Clamp(5 + seedShiftZ, 3, 7), trunkHeight: 4, isIronwood: false);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(2 + seedShiftX, 2, 3), Math.Clamp(11 + seedShiftZ, 10, 13), trunkHeight: 5, isIronwood: false);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 13), Math.Clamp(11 + seedShiftZ, 9, 13), trunkHeight: 4, isIronwood: true);
            }
            else if (chunkX == -1 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(13 + seedShiftX, 12, 14), Math.Clamp(3 + seedShiftZ, 2, 5), trunkHeight: 5, isIronwood: true);
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 2, 5), Math.Clamp(12 + seedShiftZ, 11, 13), trunkHeight: 4, isIronwood: false);
            }
            else if (chunkX == 1 && chunkZ == -1)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(2 + seedShiftX, 2, 4), Math.Clamp(12 + seedShiftZ, 10, 13), trunkHeight: 4, isIronwood: false);
            }
            else if (chunkX == 1 && chunkZ == 0)
            {
                BuildVoxelTree(voxels, chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 2, 4), Math.Clamp(4 + seedShiftZ, 2, 6), trunkHeight: 4, isIronwood: false);
            }
        }

        private static void BuildVoxelTree(
            uint[] voxels,
            int chunkX,
            int chunkZ,
            int lx,
            int lz,
            int trunkHeight,
            bool isIronwood)
        {
            int wx = chunkX * 16 + lx;
            int wz = chunkZ * 16 + lz;
            float r = MathF.Sqrt(wx * wx + wz * wz);
            float coastR = ComputeIslandCoastRadius(wx, wz);
            if (coastR - r < 3.2f)
            {
                return;
            }

            int baseY = Math.Clamp(RoundToInt(SampleIslandTopSurfaceY(wx, wz)), 7, 18);
            EnsureSolidFoundationUnder(voxels, lx, baseY, lz);

            byte woodMat = isIronwood ? MaterialIdCatalog.Ironwood : MaterialIdCatalog.OakWood;

            // Govdenin alt bogumlarinin (dy=1..2) yan arazi egimlerine (Grass/Dirt) yapisip havada asili kalmamasi icin
            // govde cevresindeki 1 blokluk bogumu temizle; boylece tek bir kutuk kesildiginde agac %100 devrilir.
            for (int dy = 1; dy <= 2; dy++)
            {
                int cy = baseY + dy;
                if (cy > 0 && cy < 255)
                {
                    if (lx > 0) voxels[(cy << 8) | (lz << 4) | (lx - 1)] = 0u;
                    if (lx < 15) voxels[(cy << 8) | (lz << 4) | (lx + 1)] = 0u;
                    if (lz > 0) voxels[(cy << 8) | ((lz - 1) << 4) | lx] = 0u;
                    if (lz < 15) voxels[(cy << 8) | ((lz + 1) << 4) | lx] = 0u;
                }
            }

            // Govde (Octagonal Pillar shapeId=2, balta ile kesildiginde ust kisim ve yaprak taci Domino ile yikilir)
            for (int dy = 0; dy <= trunkHeight; dy++)
            {
                SetArchitecturalVoxel(voxels, lx, baseY + dy, lz, woodMat, shapeId: 2, rot: 0);
            }

            int crownBaseY = baseY + trunkHeight;
            // Yan Dallar
            SetArchitecturalVoxel(voxels, lx - 1, crownBaseY - 1, lz, woodMat, shapeId: 4, rot: 1);
            SetArchitecturalVoxel(voxels, lx + 1, crownBaseY - 1, lz, woodMat, shapeId: 4, rot: 3);
            SetArchitecturalVoxel(voxels, lx, crownBaseY - 1, lz - 1, woodMat, shapeId: 4, rot: 2);
            SetArchitecturalVoxel(voxels, lx, crownBaseY - 1, lz + 1, woodMat, shapeId: 4, rot: 0);

            // 3B Yaprak Taci (Alt genis katman + ust kubbe; Grass MaxCantileverSpanBlocks=2 sinirina %100 uyumlu)
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int distMan = Math.Abs(dx) + Math.Abs(dz);
                    if (distMan > 2) continue;
                    byte leafMat = (distMan == 0 && isIronwood) ? MaterialIdCatalog.GlowingMoss : MaterialIdCatalog.Grass;
                    if (distMan > 0)
                    {
                        SetArchitecturalVoxel(voxels, lx + dx, crownBaseY, lz + dz, leafMat, shapeId: 1, rot: 0);
                    }
                    SetArchitecturalVoxel(voxels, lx + dx, crownBaseY + 1, lz + dz, MaterialIdCatalog.Grass, shapeId: 1, rot: 0);
                    if (distMan <= 1)
                    {
                        SetArchitecturalVoxel(voxels, lx + dx, crownBaseY + 2, lz + dz, leafMat, shapeId: 6, rot: 0);
                    }
                }
            }
        }

        private static void EnsureSolidFoundationUnder(uint[] voxels, int lx, int baseY, int lz)
        {
            if (lx < 0 || lx >= 16 || lz < 0 || lz >= 16) return;
            for (int y = Math.Max(2, baseY - 2); y < baseY; y++)
            {
                int idx = (y << 8) | (lz << 4) | lx;
                uint cur = voxels[idx];
                byte mat = VoxelBitUtility.GetMaterialId(cur);
                if (mat == MaterialIdCatalog.Air)
                {
                    voxels[idx] = VoxelBitUtility.PackStandard(
                        MaterialIdCatalog.Stone, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                }
                else if (VoxelBitUtility.GetShapeId(cur) == 0 && VoxelBitUtility.GetDensityOrLiquid(cur) < VoxelBitUtility.FullDensity)
                {
                    voxels[idx] = VoxelBitUtility.WithDensityOrLiquid(cur, VoxelBitUtility.FullDensity);
                }
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
