#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// PT-02: Minecraft tarzi seyrek ama duzenli ve gercekci 3 katmanli yerlesim kategorisi.
    /// </summary>
    public enum SettlementTier : byte
    {
        None = 0,
        Village = 1, // Koy: 3x3 chunk (meydan + kuyu + 5 hizali ev + toprak yollar + tarla/ahir + yel degirmeni)
        Town = 2,    // Kasaba: 5x5 chunk (tas ana cadde + meydan + kuyu + 12 hizali ev + demirci + pazar + ahir/tarla + degirmen)
        City = 3     // Sehir: 9x9 chunk (izgara/radyal tas bulvarlar + buyuk meydan/cesme + 23+ ev + pazar + demirci/marangoz + depo + sur/kule/kapilar + cevre tarlalar & degirmenler)
    }

    /// <summary>
    /// Bir 24x24 chunk (384m x 384m) makro bolgedeki yerlesimin tam deterministik tanimi (0-GC deger turu).
    /// </summary>
    public readonly struct SettlementDescriptor
    {
        public readonly int RegionX;
        public readonly int RegionZ;
        public readonly int CenterChunkX;
        public readonly int CenterChunkZ;
        public readonly SettlementTier Tier;
        public readonly int AnchorBaseY;
        public readonly int BiomeId;
        public readonly int FootprintRadiusChunks;
        public readonly uint DeterministicSeed;

        public SettlementDescriptor(
            int regionX,
            int regionZ,
            int centerChunkX,
            int centerChunkZ,
            SettlementTier tier,
            int anchorBaseY,
            int biomeId,
            int footprintRadiusChunks,
            uint deterministicSeed)
        {
            RegionX = regionX;
            RegionZ = regionZ;
            CenterChunkX = centerChunkX;
            CenterChunkZ = centerChunkZ;
            Tier = tier;
            AnchorBaseY = anchorBaseY;
            BiomeId = biomeId;
            FootprintRadiusChunks = footprintRadiusChunks;
            DeterministicSeed = deterministicSeed;
        }

        public bool ContainsChunk(int chunkX, int chunkZ) =>
            Tier != SettlementTier.None &&
            Math.Abs(chunkX - CenterChunkX) <= FootprintRadiusChunks &&
            Math.Abs(chunkZ - CenterChunkZ) <= FootprintRadiusChunks;
    }

    /// <summary>
    /// PT-02: Dağınık tekil ev spam'ını tamamen kaldıran, 24x24 chunk (384m x 384m) makro-ızgara tabanlı
    /// 3 katmanlı (Köy / Kasaba / Şehir) gerçekçi yerleşim planlayıcısı ve arazi/yol/meydan/tarla damgalayıcısı (0 B GC).
    /// </summary>
    public static class SettlementLayoutSystem
    {
        public const int MacroRegionSizeChunks = 24; // 384m x 384m makro bolge basina en fazla 1 yerlesim!
        public const int RegionBorderMarginChunks = 5; // 9x9 Sehir (+/-4) bile bolge sinirini ASLA asmaz -> 0 cakisma, O(1) sorgu!
        public const int MaxSitesPerChunk = 4;

        private struct RegionCacheSlot
        {
            public byte Initialized;
            public byte HasSettlement;
            public byte InfiniteMode;
            public int Seed;
            public int RegionX;
            public int RegionZ;
            public SettlementDescriptor Descriptor;
        }

        [ThreadStatic]
        private static RegionCacheSlot[]? _tsRegionCache;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _tsRegionCache = null;
        }
#endif

        public static void ClearThreadCacheForTests()
        {
            _tsRegionCache = null;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static int FloorDiv(int value, int divisor) =>
            value >= 0 ? (value / divisor) : -(((-value) + divisor - 1) / divisor);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static uint HashU32(int a, int b, int c, int seed)
        {
            uint h = (uint)(a * 374761393) ^ (uint)(b * 668265263) ^ (uint)(c * 2147483647) ^ (uint)(seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        public static string GetTierNameTurkish(SettlementTier tier) => tier switch
        {
            SettlementTier.Village => "Köy",
            SettlementTier.Town => "Kasaba",
            SettlementTier.City => "Şehir",
            _ => "Yok"
        };

        public static int GetFootprintRadiusChunks(SettlementTier tier) => tier switch
        {
            SettlementTier.Village => 1, // 3x3 chunk (48m x 48m)
            SettlementTier.Town => 2,    // 5x5 chunk (80m x 80m)
            SettlementTier.City => 4,    // 9x9 chunk (144m x 144m — surlar + cevre tarlalar ve degirmenler dahil)
            _ => 0
        };

        /// <summary>
        /// Verilen makro bolgede (rx, rz) bir yerlesim (Koy, Kasaba veya Sehir) varsa O(1) onbellekten cozer.
        /// </summary>
        public static bool TryGetSettlementInMacroRegion(int regionX, int regionZ, out SettlementDescriptor settlement)
        {
            RegionCacheSlot[] cache = _tsRegionCache ??= new RegionCacheSlot[16];
            int slotIdx = ((regionX * 31) ^ (regionZ * 17)) & 15;
            byte infFlag = StratocraftFloatingIslandWorldGen.InfiniteProceduralWorldMode ? (byte)1 : (byte)0;
            int seed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;

            ref RegionCacheSlot slot = ref cache[slotIdx];
            if (slot.Initialized != 0 &&
                slot.RegionX == regionX &&
                slot.RegionZ == regionZ &&
                slot.Seed == seed &&
                slot.InfiniteMode == infFlag)
            {
                settlement = slot.Descriptor;
                return slot.HasSettlement != 0;
            }

            bool found = ComputeSettlementInMacroRegionUncached(regionX, regionZ, seed, out settlement);
            slot.Initialized = 1;
            slot.HasSettlement = found ? (byte)1 : (byte)0;
            slot.InfiniteMode = infFlag;
            slot.Seed = seed;
            slot.RegionX = regionX;
            slot.RegionZ = regionZ;
            slot.Descriptor = settlement;
            return found;
        }

        /// <summary>
        /// Verilen chunk herhangi bir Koy, Kasaba veya Sehir yerlesiminin sinirlari icindeyse ilgili yerlesimi doner.
        /// </summary>
        public static bool TryGetSettlementCoveringChunk(int chunkX, int chunkZ, out SettlementDescriptor settlement)
        {
            int rx = FloorDiv(chunkX, MacroRegionSizeChunks);
            int rz = FloorDiv(chunkZ, MacroRegionSizeChunks);
            if (!TryGetSettlementInMacroRegion(rx, rz, out settlement))
            {
                return false;
            }
            return settlement.ContainsChunk(chunkX, chunkZ);
        }

        private static bool ComputeSettlementInMacroRegionUncached(
            int regionX,
            int regionZ,
            int worldSeed,
            out SettlementDescriptor settlement)
        {
            settlement = default;
            uint regHash = HashU32(regionX, 0x53455454, regionZ, worldSeed);

            // Baslangic cevresindeki 3 komsu makro bolgede kesif dostu 1 Koy + 1 Kasaba + 1 Sehir (baslangic vadisinden uzakta!):
            // Diger tum dunyada Minecraft mantigiyla ~%55 makro bolge olasiligi (ortalama ~400-650m aralikli seyrek yerlesimler)
            bool isStarterVillageRegion = regionX == 0 && regionZ == 0;
            bool isStarterTownRegion = regionX == -1 && regionZ == 0;
            bool isStarterCityRegion = regionX == 0 && regionZ == -1;
            bool isShowcaseRegion = isStarterVillageRegion || isStarterTownRegion || isStarterCityRegion;

            if (!isShowcaseRegion && (regHash % 100u) >= 55u)
            {
                return false;
            }

            SettlementTier tier;
            if (isStarterVillageRegion) tier = SettlementTier.Village;
            else if (isStarterTownRegion) tier = SettlementTier.Town;
            else if (isStarterCityRegion) tier = SettlementTier.City;
            else
            {
                uint tierRoll = (regHash >> 8) % 100u;
                tier = tierRoll < 60u
                    ? SettlementTier.Village
                    : (tierRoll < 88u ? SettlementTier.Town : SettlementTier.City);
            }

            int radius = GetFootprintRadiusChunks(tier);
            int minLocal = Math.Max(RegionBorderMarginChunks, radius + 1);
            int maxLocal = MacroRegionSizeChunks - 1 - minLocal;
            int span = Math.Max(1, maxLocal - minLocal + 1);

            int baseCx = regionX * MacroRegionSizeChunks;
            int baseCz = regionZ * MacroRegionSizeChunks;

            // Deterministik aday taramasi: su/lav/asit/ucurum/magara agzi ve baslangic vadisi haricinde uygun kuru duzluk bul!
            int startOffX = (int)((regHash >> 16) % (uint)span);
            int startOffZ = (int)((regHash >> 22) % (uint)span);

            for (int attempt = 0; attempt < 25; attempt++)
            {
                int ox = (startOffX + (attempt % 5) * 3) % span;
                int oz = (startOffZ + (attempt / 5) * 3) % span;
                int candCx = baseCx + minLocal + ox;
                int candCz = baseCz + minLocal + oz;

                // Baslangic Atolyesi ve Zumrut Vadi gobegini (r < ~88m) kesinlikle koru — asla ev spami yapma!
                if (Math.Abs(candCx) <= radius + 4 && Math.Abs(candCz) <= radius + 4)
                {
                    continue;
                }

                if (EvaluateSettlementCenterSuitability(candCx, candCz, radius, out int anchorY, out int biomeId))
                {
                    settlement = new SettlementDescriptor(
                        regionX, regionZ, candCx, candCz, tier, anchorY, biomeId, radius, regHash);
                    return true;
                }
            }

            // Eger kesif bolgesinde (starter Village/Town/City) tum standart yamalar deniz/dag olduysa daha genis toleransla dene:
            if (isShowcaseRegion)
            {
                for (int attempt = 0; attempt < 25; attempt++)
                {
                    int ox = (startOffX + (attempt % 5) * 3) % span;
                    int oz = (startOffZ + (attempt / 5) * 3) % span;
                    int candCx = baseCx + minLocal + ox;
                    int candCz = baseCz + minLocal + oz;
                    if (Math.Abs(candCx) <= radius + 4 && Math.Abs(candCz) <= radius + 4) continue;

                    int wx = candCx * 16 + 8;
                    int wz = candCz * 16 + 8;
                    float topH = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
                    if (topH < StratocraftFloatingIslandWorldGen.SeaLevelY + 1.0f ||
                        topH > StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 18)
                    {
                        continue;
                    }
                    int biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                    if (biomeId == 8 || biomeId == 11) continue;

                    int anchorY = Math.Clamp((int)MathF.Round(topH), StratocraftFloatingIslandWorldGen.SeaLevelY + 2, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 20);
                    settlement = new SettlementDescriptor(
                        regionX, regionZ, candCx, candCz, tier, anchorY, biomeId, radius, regHash);
                    return true;
                }
            }

            return false;
        }

        private static bool EvaluateSettlementCenterSuitability(
            int centerCx,
            int centerCz,
            int radiusChunks,
            out int anchorBaseY,
            out int biomeId)
        {
            anchorBaseY = StratocraftFloatingIslandWorldGen.DefaultSurfaceBaseY;
            int wx = centerCx * 16 + 8;
            int wz = centerCz * 16 + 8;
            biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);

            // Volkanik kaldera (8), Hiclik Ucurumu (11) ve Asit Batakligi (3) icinde yerlesim kurulmaz:
            if (biomeId == 8 || biomeId == 11 || biomeId == 3)
            {
                return false;
            }

            if (StratocraftFloatingIslandWorldGen.IsNearNaturalCaveEntranceMouth(wx, wz, clearanceRadius: 20.0f))
            {
                return false;
            }

            float centerTop = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            if (centerTop < StratocraftFloatingIslandWorldGen.SeaLevelY + 2.0f || centerTop > 113.0f)
            {
                return false;
            }

            // Cevre 4 yonde deniz altina dusme veya asiri dik ucurum kontrolu
            float sampleOffset = MathF.Max(16.0f, radiusChunks * 11.0f);
            float hN = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz + sampleOffset);
            float hS = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz - sampleOffset);
            float hE = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx + sampleOffset, wz);
            float hW = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx - sampleOffset, wz);

            float minH = MathF.Min(MathF.Min(hN, hS), MathF.Min(hE, hW));
            float maxH = MathF.Max(MathF.Max(hN, hS), MathF.Max(hE, hW));
            if (minH < StratocraftFloatingIslandWorldGen.SeaLevelY + 0.5f)
            {
                return false;
            }
            if ((maxH - minH) > 14.0f)
            {
                return false;
            }

            anchorBaseY = Math.Clamp(
                (int)MathF.Round(centerTop),
                StratocraftFloatingIslandWorldGen.SeaLevelY + 2,
                StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 20);
            return true;
        }

        /// <summary>
        /// Verilen chunk'a ait tum moduler yerlesim binalarini (en fazla MaxSitesPerChunk) 0-GC olarak doldurur.
        /// Tum binalar yollara ve meydanlara hizali, kapi yonleri caddeye bakacak sekilde yerlestirilir!
        /// </summary>
        public static int PopulateChunkSettlementSites(
            int chunkX,
            int chunkZ,
            Span<ProceduralModularSiteSpec> outSites)
        {
            if (outSites.Length == 0 || !TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return 0;
            }

            int dx = chunkX - s.CenterChunkX;
            int dz = chunkZ - s.CenterChunkZ;
            int count = 0;

            switch (s.Tier)
            {
                case SettlementTier.Village:
                    PopulateVillageChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    break;
                case SettlementTier.Town:
                    PopulateTownChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    break;
                case SettlementTier.City:
                    PopulateCityChunkSites(in s, chunkX, chunkZ, dx, dz, outSites, ref count);
                    break;
            }

            return count;
        }

        private static void EmitPlot(
            in SettlementDescriptor s,
            int chunkX,
            int chunkZ,
            byte localCx,
            byte localCz,
            byte halfExtent,
            ProceduralModularSiteKind kind,
            float yawDeg,
            bool hasLootChest,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            if (count >= outSites.Length) return;
            int wx = chunkX * 16 + localCx;
            int wz = chunkZ * 16 + localCz;
            int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
            int biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
            byte terraceMat = StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(biomeId, baseY, isTopLayer: true);
            uint seed = HashU32(wx, (int)kind + 7, wz, StratocraftFloatingIslandWorldGen.ActiveWorldSeed ^ 0x53495445);

            outSites[count++] = new ProceduralModularSiteSpec(
                chunkX: chunkX,
                chunkZ: chunkZ,
                kind: kind,
                baseY: baseY,
                biomeId: biomeId,
                terraceMaterialId: terraceMat,
                localCenterX: localCx,
                localCenterZ: localCz,
                terraceHalfExtentBlocks: halfExtent,
                worldCenterX: wx + 0.5f,
                worldBaseY: baseY + 0.5f,
                worldCenterZ: wz + 0.5f,
                yawDegrees: yawDeg,
                deterministicSeed: seed,
                hasLootChest: hasLootChest);
        }

        // =====================================================================
        // 1) KOY (VILLAGE) — 3x3 chunk: Meydan + Kuyu + 5 Hizali Ev + Toprak Yollar + 2 Tarla + Ahir + Yel Degirmeni
        // =====================================================================
        private static void PopulateVillageChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            if (dx == 0 && dz == 0)
            {
                // Koy meydani ortasinda tas kuyu
                EmitPlot(in s, cx, cz, 8, 8, 2, ProceduralModularSiteKind.VillageWell, 0f, false, outSites, ref count);
            }
            else if (dx == 0 && dz == -1)
            {
                // Guney toprak yolunun iki yaninda caddeye bakan 2 hizali ev
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dx == 0 && dz == 1)
            {
                // Kuzey toprak yolunun batisinda caddeye bakan hizali ev
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == 0)
            {
                // Bati toprak yolunun kuzeyinde caddeye bakan hizali ev
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
            }
            else if (dx == 1 && dz == 0)
            {
                // Dogu toprak yolunun guneyinde caddeye bakan hizali ev
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
            }
            else if (dx == 1 && dz == -1)
            {
                // Tarlalarin yaninda Yel Degirmeni
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 90f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == 1)
            {
                // Ciftlik ahiri / samanlik
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 0f, true, outSites, ref count);
            }
            // (-1, -1) ve (1, 1): Citli sulamali koy tarlalari (voxel arazi katmaninda damgalanir)
        }

        // =====================================================================
        // 2) KASABA (TOWN) — 5x5 chunk: Tas Ana Cadde + Merkez Meydan + Kuyu + 12 Hizali Ev + Demirci + 3 Pazar + Ahir/Tarla + Degirmen
        // =====================================================================
        private static void PopulateTownChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            if (dx == 0 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 8, 2, ProceduralModularSiteKind.VillageWell, 0f, false, outSites, ref count);
                return;
            }

            // Ana Cadde & Meydan Cevresi: Demirci Atolyesi + 3 Pazar Tezgahi + 12 Hizali Ev
            if (dx == 1 && dz == 0)
            {
                // Ana caddenin guneyinde Demirci Atolyesi, kuzeyinde Pazar Tezgahi
                EmitPlot(in s, cx, cz, 8, 3, 3, ProceduralModularSiteKind.BlacksmithForge, 180f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.MarketStall, 0f, false, outSites, ref count);
            }
            else if (dx == -1 && dz == 0)
            {
                // Ana caddenin kuzeyinde 2 Katli Kasaba Evi (#1), guneyinde Pazar Tezgahi
                EmitPlot(in s, cx, cz, 8, 13, 3, ProceduralModularSiteKind.TownHouseTwoStory, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.MarketStall, 180f, false, outSites, ref count);
            }
            else if (dx == 0 && dz == 1)
            {
                // Kuzey caddesinde 1 hizali ev (#2) + meydan kenari Pazar Tezgahi
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.MarketStall, 90f, false, outSites, ref count);
            }
            else if (dx == 0 && dz == -1)
            {
                // Guney caddesinde 2 hizali ev (#3, #4)
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dx == -2 && dz == 0)
            {
                // Bati giris caddesinde 2 hizali ev (#5, #6)
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
            }
            else if (dx == 2 && dz == 0)
            {
                // Dogu giris caddesinde 2 hizali ev (#7, #8)
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
            }
            else if (dx == 0 && dz == -2)
            {
                // Guney ucunda 2 hizali ev (#9, #10)
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dx == 0 && dz == 2)
            {
                // Kuzey ucunda 2 hizali ev (#11, #12)
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dx == -2 && dz == -1)
            {
                // Kasaba ciftlik ahiri (tarlalarin yani)
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 180f, true, outSites, ref count);
            }
            else if (dx == 2 && dz == -2)
            {
                // Kasaba yel degirmeni
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 90f, true, outSites, ref count);
            }
            // (-2,-2), (-1,-2), (1,-2), (2,-1): Kasaba cevre tarlalari
        }

        // =====================================================================
        // 3) SEHIR (CITY) — 9x9 chunk: Izgara/Radyal Tas Yollar + Buyuk Meydan/Cesme + 23+ Ev + Pazar + Demirci/Marangoz + Depo + Sur/Kule/Kapi + Tarlalar & 2 Degirmen
        // =====================================================================
        private static void PopulateCityChunkSites(
            in SettlementDescriptor s,
            int cx, int cz, int dx, int dz,
            Span<ProceduralModularSiteSpec> outSites,
            ref int count)
        {
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            // A) Buyuk Merkez Meydani & Cesme (0, 0)
            if (dx == 0 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityFountain, 0f, false, outSites, ref count);
                return;
            }

            // B) Sur Hatti, 4 Ana Kapi ve 4 Kose Gozetleme Kulesi (|dx| == 3 veya |dz| == 3, ic alan [-3..3])
            if (adx <= 3 && adz <= 3 && (adx == 3 || adz == 3))
            {
                if (adx == 3 && adz == 3)
                {
                    float towerYaw = (dx < 0 && dz < 0) ? 0f : (dx > 0 && dz < 0 ? 270f : (dx < 0 ? 90f : 180f));
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.MeadHallWatchtower, towerYaw, true, outSites, ref count);
                }
                else if (dx == 0 && dz == -3)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 0f, false, outSites, ref count);
                }
                else if (dx == 0 && dz == 3)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 180f, false, outSites, ref count);
                }
                else if (dx == -3 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 90f, false, outSites, ref count);
                }
                else if (dx == 3 && dz == 0)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CityGatehouse, 270f, false, outSites, ref count);
                }
                else if (adz == 3)
                {
                    // Kuzey & Guney sur hatti (2 adet yan yana 6m sur segmenti -> kesintisiz sur!)
                    float wallYaw = dz < 0 ? 0f : 180f;
                    EmitPlot(in s, cx, cz, 4, 8, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                    EmitPlot(in s, cx, cz, 12, 8, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                }
                else
                {
                    // Bati & Dogu sur hatti
                    float wallYaw = dx < 0 ? 90f : 270f;
                    EmitPlot(in s, cx, cz, 8, 4, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                    EmitPlot(in s, cx, cz, 8, 12, 2, ProceduralModularSiteKind.CityWallSegment, wallYaw, false, outSites, ref count);
                }
                return;
            }

            // C) Sur Disi Tarim Kusagi: Cevre Tarlalar, 2 Ahir ve 2 Yel Degirmeni (|dx| == 4 veya |dz| == 4)
            if (adx == 4 || adz == 4)
            {
                if (dx == 1 && dz == 4)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 0f, true, outSites, ref count);
                }
                else if (dx == -4 && dz == -2)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WindmillTower, 270f, true, outSites, ref count);
                }
                else if (dx == -1 && dz == -4)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 180f, true, outSites, ref count);
                }
                else if (dx == 4 && dz == 2)
                {
                    EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BarnStable, 90f, true, outSites, ref count);
                }
                return;
            }

            // D) Sehir Ici Mahalleler (adx <= 2 && adz <= 2)
            // 1) Pazar Meydani (4 Pazar Tezgahi: Dogu & Bati bulvar girisleri)
            if (dx == 1 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.MarketStall, 0f, false, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.MarketStall, 180f, false, outSites, ref count);
            }
            else if (dx == -1 && dz == 0)
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.MarketStall, 0f, false, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.MarketStall, 180f, false, outSites, ref count);
            }
            // 2) Zanaat, Depo & Kamu Meydani (Demirci, Marangoz, 2 Depo, Mermer Salon)
            else if (dx == 1 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.BlacksmithForge, 90f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CarpenterWorkshop, 270f, true, outSites, ref count);
            }
            else if (dx == -1 && dz == 1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WarehouseHall, 270f, true, outSites, ref count);
            }
            else if (dx == 2 && dz == -1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.WarehouseHall, 90f, true, outSites, ref count);
            }
            else if (dx == 1 && dz == 1)
            {
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.CarvedMarbleShrine, 0f, true, outSites, ref count);
            }
            // 3) Bulvar Boyunca Hizali Evler (12 adet) + Ic Ring 2 Katli Sehir Evleri (11 adet) -> 23 konut + 9 kamu/ticaret = 32 ic yapi!
            else if (dx == 0 && (dz == -1 || dz == 1 || dz == -2 || dz == 2))
            {
                EmitPlot(in s, cx, cz, 3, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 270f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 13, 8, 2, ProceduralModularSiteKind.NordicTimberCabin, 90f, true, outSites, ref count);
            }
            else if (dz == 0 && (dx == -2 || dx == 2))
            {
                EmitPlot(in s, cx, cz, 8, 13, 2, ProceduralModularSiteKind.NordicTimberCabin, 0f, true, outSites, ref count);
                EmitPlot(in s, cx, cz, 8, 3, 2, ProceduralModularSiteKind.NordicTimberCabin, 180f, true, outSites, ref count);
            }
            else if (adx <= 2 && adz <= 2)
            {
                float yaw = dz < 0 ? 180f : (dz > 0 ? 0f : (dx < 0 ? 270f : 90f));
                EmitPlot(in s, cx, cz, 8, 8, 3, ProceduralModularSiteKind.TownHouseTwoStory, yaw, true, outSites, ref count);
            }
        }

        /// <summary>
        /// Verilen yerlesim icindeki (wx, wz) sutununun yumusak teraslanmis taban kotunu hesaplar:
        /// Merkez meydan ve caddeler puruzsuz tek duzlemde kalir; dis cevre dogal araziyle +/-2m yumusak gecis yapar.
        /// </summary>
        public static int ResolveSettlementColumnSurfaceY(in SettlementDescriptor s, int wx, int wz)
        {
            float centerWx = s.CenterChunkX * 16 + 8;
            float centerWz = s.CenterChunkZ * 16 + 8;
            float maxDist = MathF.Max(16.0f, (s.FootprintRadiusChunks + 0.5f) * 16.0f);
            float dist = MathF.Sqrt((wx - centerWx) * (wx - centerWx) + (wz - centerWz) * (wz - centerWz));
            float t = Math.Clamp((dist - maxDist * 0.62f) / MathF.Max(8.0f, maxDist * 0.42f), 0f, 1f);
            if (t <= 0.001f)
            {
                return s.AnchorBaseY;
            }

            float naturalY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            float blended = s.AnchorBaseY + (naturalY - s.AnchorBaseY) * (t * t * (3f - 2f * t));
            int clamped = Math.Clamp((int)MathF.Round(blended), s.AnchorBaseY - 2, s.AnchorBaseY + 2);
            return Math.Clamp(clamped, StratocraftFloatingIslandWorldGen.SeaLevelY + 2, StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 18);
        }

        /// <summary>
        /// Verilen dunya kolonu (wx, wz) bir yerlesimin yolu, meydani, tarlasi veya bina terasi icinde mi?
        /// Agac, yabani cali ve kaya cikintilarinin ev veya cadde icine cikmasini %100 onler (0 B GC).
        /// </summary>
        public static bool IsColumnReservedBySettlement(int wx, int wz)
        {
            int cx = FloorDiv(wx, 16);
            int cz = FloorDiv(wz, 16);
            if (!TryGetSettlementCoveringChunk(cx, cz, out SettlementDescriptor s))
            {
                return false;
            }

            int dx = cx - s.CenterChunkX;
            int dz = cz - s.CenterChunkZ;
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;

            if (IsRoadOrPlazaColumn(in s, dx, dz, lx, lz, out _)) return true;
            if (IsFarmPlotChunk(in s, dx, dz) && lx >= 1 && lx <= 14 && lz >= 1 && lz <= 14) return true;

            Span<ProceduralModularSiteSpec> sites = stackalloc ProceduralModularSiteSpec[MaxSitesPerChunk];
            int count = PopulateChunkSettlementSites(cx, cz, sites);
            for (int i = 0; i < count; i++)
            {
                int ext = sites[i].TerraceHalfExtentBlocks + 2;
                if (Math.Abs(lx - sites[i].LocalCenterX) <= ext &&
                    Math.Abs(lz - sites[i].LocalCenterZ) <= ext)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsFarmPlotChunk(in SettlementDescriptor s, int dx, int dz)
        {
            return s.Tier switch
            {
                SettlementTier.Village => (dx == -1 && dz == -1) || (dx == 1 && dz == 1),
                SettlementTier.Town => (dx == -2 && dz == -2) || (dx == -1 && dz == -2) ||
                                       (dx == 1 && dz == -2) || (dx == 2 && dz == -1),
                SettlementTier.City => (Math.Abs(dx) == 4 && Math.Abs(dz) == 1) ||
                                       (Math.Abs(dz) == 4 && Math.Abs(dx) == 2),
                _ => false
            };
        }

        public static bool IsRoadOrPlazaColumn(
            in SettlementDescriptor s,
            int dx,
            int dz,
            int lx,
            int lz,
            out byte surfaceMat)
        {
            surfaceMat = MaterialIdCatalog.Dirt;
            int adx = Math.Abs(dx);
            int adz = Math.Abs(dz);

            switch (s.Tier)
            {
                case SettlementTier.Village:
                    // Merkez koy meydani (10x10m)
                    if (dx == 0 && dz == 0 && lx >= 3 && lx <= 12 && lz >= 3 && lz <= 12)
                    {
                        surfaceMat = (lx == 3 || lx == 12 || lz == 3 || lz == 12)
                            ? MaterialIdCatalog.Cobblestone
                            : MaterialIdCatalog.Gravel;
                        return true;
                    }
                    // N-S ve E-W toprak koy yollari (3m genis)
                    if (dx == 0 && lx >= 7 && lx <= 9)
                    {
                        surfaceMat = lx == 8 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Gravel;
                        return true;
                    }
                    if (dz == 0 && lz >= 7 && lz <= 9)
                    {
                        surfaceMat = lz == 8 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Gravel;
                        return true;
                    }
                    return false;

                case SettlementTier.Town:
                    // Merkez kasaba meydani (14x14m tas doseme)
                    if (dx == 0 && dz == 0 && lx >= 1 && lx <= 14 && lz >= 1 && lz <= 14)
                    {
                        surfaceMat = ((lx + lz) & 1) == 0 ? MaterialIdCatalog.Cobblestone : MaterialIdCatalog.StoneBrick;
                        return true;
                    }
                    // Tas doseli ana caddeler (4m genis: Cobblestone + StoneBrick bordur)
                    if (dx == 0 && lx >= 6 && lx <= 9)
                    {
                        surfaceMat = (lx == 6 || lx == 9) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone;
                        return true;
                    }
                    if (dz == 0 && lz >= 6 && lz <= 9)
                    {
                        surfaceMat = (lz == 6 || lz == 9) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone;
                        return true;
                    }
                    // Ic baglanti sokaklari (|dx|==1 veya |dz|==1 kenarlari)
                    if (adx <= 1 && adz <= 1 && ((lx >= 1 && lx <= 2 && adx == 1) || (lz >= 1 && lz <= 2 && adz == 1)))
                    {
                        surfaceMat = MaterialIdCatalog.Gravel;
                        return true;
                    }
                    return false;

                case SettlementTier.City:
                    // Buyuk Sehir Meydani (16x16m Mermer + Yontma Tas Tugla)
                    if (dx == 0 && dz == 0)
                    {
                        bool border = lx <= 1 || lx >= 14 || lz <= 1 || lz >= 14;
                        bool innerRing = Math.Abs(lx - 8) <= 4 && Math.Abs(lz - 8) <= 4;
                        surfaceMat = innerRing
                            ? MaterialIdCatalog.Marble
                            : (border ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                        return true;
                    }
                    // 4 Ana Bulvar (5m genis, tum sehir + kapilardan disari uzanan mermer seritli tas bulvar)
                    if (dx == 0 && lx >= 6 && lx <= 10)
                    {
                        surfaceMat = lx == 8
                            ? MaterialIdCatalog.Marble
                            : ((lx == 6 || lx == 10) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                        return true;
                    }
                    if (dz == 0 && lz >= 6 && lz <= 10)
                    {
                        surfaceMat = lz == 8
                            ? MaterialIdCatalog.Marble
                            : ((lz == 6 || lz == 10) ? MaterialIdCatalog.StoneBrick : MaterialIdCatalog.Cobblestone);
                        return true;
                    }
                    // Ic halka tas yolu (|dx| == 2 veya |dz| == 2 ic mahalle izgara baglantisi)
                    if (adx <= 2 && adz <= 2)
                    {
                        if ((dx == -2 && lx >= 13 && lx <= 15) || (dx == 2 && lx >= 0 && lx <= 2) ||
                            (dz == -2 && lz >= 13 && lz <= 15) || (dz == 2 && lz >= 0 && lz <= 2))
                        {
                            surfaceMat = MaterialIdCatalog.Cobblestone;
                            return true;
                        }
                        // Radyal kose caddeleri ((±1, ±1) icinde capraz tas yol)
                        if (adx == 1 && adz == 1)
                        {
                            int rx = dx > 0 ? lx : (15 - lx);
                            int rz = dz > 0 ? lz : (15 - lz);
                            if (Math.Abs(rx - rz) <= 1)
                            {
                                surfaceMat = MaterialIdCatalog.StoneBrick;
                                return true;
                            }
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Chunk uretimi sirasinda yerlesim yollarini, meydanlarini, tarlalarini, kuyu/cesme havuzlarini
        /// ve tum moduler bina teraslarini voksel haritasina damgalar (0 kubik ev duvari, 0 B GC).
        /// </summary>
        public static bool StampSettlementTerrainFeaturesForChunk(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ushort[]? fluids = null)
        {
            if (!TryGetSettlementCoveringChunk(chunkX, chunkZ, out SettlementDescriptor s))
            {
                return false;
            }

            int dx = chunkX - s.CenterChunkX;
            int dz = chunkZ - s.CenterChunkZ;

            // 1) Yollar ve Meydanlar (duz teras + tas/toprak kaplama + acik bas-ustu hava)
            for (int lz = 0; lz < 16; lz++)
            {
                int wz = chunkZ * 16 + lz;
                for (int lx = 0; lx < 16; lx++)
                {
                    if (!IsRoadOrPlazaColumn(in s, dx, dz, lx, lz, out byte roadMat))
                    {
                        continue;
                    }

                    int wx = chunkX * 16 + lx;
                    int baseY = ResolveSettlementColumnSurfaceY(in s, wx, wz);
                    StampLeveledSurfaceColumn(voxels, lx, baseY, lz, roadMat, s.BiomeId, clearHeight: 8);
                }
            }

            // 2) Tarla / Ciftlik Parselleri (surulmus toprak siralari + 1m sulama kanali + ahsap cit direkleri)
            if (IsFarmPlotChunk(in s, dx, dz))
            {
                StampIrrigatedFarmPlot(in s, chunkX, chunkZ, voxels, fluids);
            }

            // 3) Kuyu veya Sehir Cesmesi icin gercek tas havuz / su cukuru (merkez (0,0) chunk)
            if (dx == 0 && dz == 0)
            {
                int baseY = s.AnchorBaseY;
                int basinRadius = s.Tier == SettlementTier.City ? 2 : 0;
                for (int bz = 8 - basinRadius; bz <= 8 + basinRadius; bz++)
                {
                    for (int bx = 8 - basinRadius; bx <= 8 + basinRadius; bx++)
                    {
                        bool isEdge = basinRadius > 0 && (Math.Abs(bx - 8) == basinRadius || Math.Abs(bz - 8) == basinRadius);
                        if (isEdge)
                        {
                            StampLeveledSurfaceColumn(voxels, bx, baseY, bz, MaterialIdCatalog.Marble, s.BiomeId, clearHeight: 6);
                        }
                        else if (fluids != null)
                        {
                            int poolY = baseY;
                            int floorIdx = ((poolY - 1) << 8) | (bz << 4) | bx;
                            int waterIdx = (poolY << 8) | (bz << 4) | bx;
                            if (floorIdx >= 0 && waterIdx < voxels.Length && waterIdx < fluids.Length)
                            {
                                voxels[floorIdx] = VoxelBitUtility.PackStandard(
                                    MaterialIdCatalog.StoneBrick, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                                voxels[waterIdx] = 0u;
                                fluids[waterIdx] = FluidCell16.Pack(FluidType.Water, 200, isSettled: true, 0).RawValue;
                            }
                        }
                    }
                }
            }

            // 4) Chunk icindeki tum moduler bina parsellerinin dogal zemin teraslarini duzle
            Span<ProceduralModularSiteSpec> sites = stackalloc ProceduralModularSiteSpec[MaxSitesPerChunk];
            int siteCount = PopulateChunkSettlementSites(chunkX, chunkZ, sites);
            for (int i = 0; i < siteCount; i++)
            {
                LevelModularBuildingTerrace(voxels, in sites[i]);
            }

            return true;
        }

        private static void StampIrrigatedFarmPlot(
            in SettlementDescriptor s,
            int chunkX,
            int chunkZ,
            uint[] voxels,
            ushort[]? fluids)
        {
            int centerBaseY = ResolveSettlementColumnSurfaceY(in s, chunkX * 16 + 8, chunkZ * 16 + 8);
            for (int lz = 2; lz <= 13; lz++)
            {
                for (int lx = 2; lx <= 13; lx++)
                {
                    bool isBorder = lx == 2 || lx == 13 || lz == 2 || lz == 13;
                    bool isGate = (lx == 7 || lx == 8) && (lz == 2 || lz == 13);
                    bool isWaterTrench = !isBorder && (lx == 7 || lx == 8);

                    if (isBorder)
                    {
                        StampLeveledSurfaceColumn(voxels, lx, centerBaseY, lz, MaterialIdCatalog.Grass, s.BiomeId, clearHeight: 6);
                        // Cit kose ve aralik direkleri (giris kapisi acik)
                        if (!isGate && ((lx + lz) % 3 == 0 || ((lx == 2 || lx == 13) && (lz == 2 || lz == 13))))
                        {
                            int postIdx = ((centerBaseY + 1) << 8) | (lz << 4) | lx;
                            if (postIdx < voxels.Length)
                            {
                                voxels[postIdx] = VoxelBitUtility.PackStandard(
                                    MaterialIdCatalog.OakWood, VoxelBitUtility.FullDensity, 2, 0, BlokKokenDurumu.DogalKutle, 0);
                            }
                        }
                    }
                    else if (isWaterTrench)
                    {
                        StratocraftFloatingIslandWorldGen.EnsureSolidFoundationUnder(voxels, lx, centerBaseY - 1, lz, s.BiomeId);
                        int bedIdx = ((centerBaseY - 1) << 8) | (lz << 4) | lx;
                        int trenchIdx = (centerBaseY << 8) | (lz << 4) | lx;
                        if (bedIdx >= 0 && trenchIdx < voxels.Length)
                        {
                            voxels[bedIdx] = VoxelBitUtility.PackStandard(
                                MaterialIdCatalog.Dirt, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                            voxels[trenchIdx] = 0u;
                            if (fluids != null && trenchIdx < fluids.Length)
                            {
                                fluids[trenchIdx] = FluidCell16.Pack(FluidType.Water, 196, isSettled: true, 0).RawValue;
                            }
                        }
                        for (int cy = centerBaseY + 1; cy <= Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2, centerBaseY + 6); cy++)
                        {
                            voxels[(cy << 8) | (lz << 4) | lx] = 0u;
                        }
                    }
                    else
                    {
                        // Surulmus bereketli tarla topragi (alternatif toprak/ekin sirasi)
                        byte farmMat = (lz & 1) == 0 ? MaterialIdCatalog.Dirt : MaterialIdCatalog.Grass;
                        StampLeveledSurfaceColumn(voxels, lx, centerBaseY, lz, farmMat, s.BiomeId, clearHeight: 6);
                    }
                }
            }
        }

        private static void StampLeveledSurfaceColumn(
            uint[] voxels,
            int lx,
            int baseY,
            int lz,
            byte surfaceMat,
            int biomeId,
            int clearHeight)
        {
            if ((uint)lx >= 16u || (uint)lz >= 16u || baseY < 4 || baseY >= StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2)
            {
                return;
            }

            StratocraftFloatingIslandWorldGen.EnsureSolidFoundationUnder(voxels, lx, baseY, lz, biomeId);
            voxels[(baseY << 8) | (lz << 4) | lx] = VoxelBitUtility.PackStandard(
                surfaceMat, VoxelBitUtility.FullDensity, 0, 0, BlokKokenDurumu.DogalKutle, 0);

            int maxClearY = Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 2, baseY + clearHeight);
            for (int y = baseY + 1; y <= maxClearY; y++)
            {
                voxels[(y << 8) | (lz << 4) | lx] = 0u;
            }
        }

        private static void LevelModularBuildingTerrace(uint[] voxels, in ProceduralModularSiteSpec site)
        {
            int minLx = Math.Max(0, site.LocalCenterX - site.TerraceHalfExtentBlocks);
            int maxLx = Math.Min(15, site.LocalCenterX + site.TerraceHalfExtentBlocks);
            int minLz = Math.Max(0, site.LocalCenterZ - site.TerraceHalfExtentBlocks);
            int maxLz = Math.Min(15, site.LocalCenterZ + site.TerraceHalfExtentBlocks);
            int baseY = site.BaseY;
            byte topMat = StratocraftFloatingIslandWorldGen.ResolveBiomeFoundationMaterial(site.BiomeId, baseY, isTopLayer: true);

            for (int lz = minLz; lz <= maxLz; lz++)
            {
                for (int lx = minLx; lx <= maxLx; lx++)
                {
                    StampLeveledSurfaceColumn(voxels, lx, baseY, lz, topMat, site.BiomeId, clearHeight: 9);
                }
            }
        }
    }
}
