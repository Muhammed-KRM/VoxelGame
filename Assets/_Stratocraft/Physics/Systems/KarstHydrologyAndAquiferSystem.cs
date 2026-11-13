#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Magara odasinin ust ortu kayac gecirgenligine gore karst hidrolojisi sinifi.
    /// </summary>
    public enum CaveChamberHydrologyKind : byte
    {
        /// <summary>Kalin gecirimsiz masif kaya altindaki Kuru Magara (&gt;= %60, 0 tavan suzulmesi).</summary>
        DryImpermeable = 0,

        /// <summary>Gecirgen toprak/epikarst altindaki Islak Magara (talweg tas taban birikintileri ve vadoz suzulme).</summary>
        WetTalwegSeepage = 1,

        /// <summary>Islak magaralarin 1/5..1/8'inde (%12.5..%20) olusan Buyuk Yeralti Karstik Akifer Golu.</summary>
        WetMajorKarstAquifer = 2
    }

    /// <summary>
    /// Deterministik karst magara odasi hidroloji siniflandirma sonucu.
    /// </summary>
    public readonly struct CaveChamberHydrologyEvaluation
    {
        public readonly CaveChamberHydrologyKind Kind;
        public readonly bool IsImpermeableDryRoof;
        public readonly bool IsWetKarstCave;
        public readonly bool HasActiveStandingPool;
        public readonly bool HasMajorKarstAquiferLake;
        public readonly float OverburdenPermeability01;
        public readonly int TargetWaterFloorY;

        public CaveChamberHydrologyEvaluation(
            CaveChamberHydrologyKind kind,
            bool isImpermeableDryRoof,
            bool hasActiveStandingPool,
            bool hasMajorKarstAquiferLake,
            float overburdenPermeability01,
            int targetWaterFloorY)
        {
            Kind = kind;
            IsImpermeableDryRoof = isImpermeableDryRoof;
            IsWetKarstCave = !isImpermeableDryRoof;
            HasActiveStandingPool = hasActiveStandingPool;
            HasMajorKarstAquiferLake = hasMajorKarstAquiferLake;
            OverburdenPermeability01 = overburdenPermeability01;
            TargetWaterFloorY = targetWaterFloorY;
        }
    }

    /// <summary>
    /// Tukenebilir ve yuzey yagmuruyla yavasca yeniden dolabilen yeralti karst su yatagi kaydi (Sabit dizide tutulur, 0 B GC).
    /// </summary>
    public struct KarstAquiferBasinRecord
    {
        public int BasinId;
        public short AnchorChunkX;
        public short AnchorChunkZ;
        public byte EquilibriumWaterY;

        /// <summary>true ise ust tavan gecirimsiz masif kayadir; yagmur suzulmesi = 0 ("tastan su gecmez").</summary>
        public bool IsImpermeableDryRoof;

        /// <summary>true ise Buyuk Yeralti Karst Akifer Golu, false ise sig talweg birikintisi.</summary>
        public bool IsMajorAquiferLake;

        /// <summary>Ust ortu gecirgenlik katsayisi (0.0 .. 1.0; IsImpermeableDryRoof == true iken 0.0).</summary>
        public float OverburdenPermeability01;

        /// <summary>Su an yatakta depolanan Tatli Su birimi (su cekildikce / buharlastikca 0'a kadar bosalabilir).</summary>
        public float CurrentStoredUnits;

        /// <summary>Tam denge kotundaki (EquilibriumWaterY) maksimum su kapasitesi birimi.</summary>
        public float MaxCapacityUnits;

        /// <summary>Yuzey yagmurundan topraga/epikarsa suzulup magara tavanindan damlamayi bekleyen gecikmeli Tatli Su tamponu.</summary>
        public float PendingVadosePercolation;

        /// <summary>Alt-birim kesirli su cekme / doldurma muhasebe artigi.</summary>
        public float FractionalFluidSyncCarry;
    }

    /// <summary>
    /// Paket 4A Faz 4A.5 (IP-7 / AK-10) — Karst Magara Hidrolojisi ve Tukenip Dolabilen Yeralti Su Yataklari Sistemi:
    /// - Magara odalarinin <c>&gt;= %60</c>'i (<c>%58..%65</c>) kalin gecirimsiz masif kaya altindadir -&gt; Kuru Magara (<c>0</c> tavan suzulmesi;
    ///   yalnizca acik yuzey agzindan sel suyu akarsa su girebilir).
    /// - <c>~%40</c>'i (<c>%35..%42</c>) gecirgen toprak/epikarst altindadir -&gt; Islak Magara (talweg tas taban birikintileri).
    /// - Islak magaralarin <c>5'te 1 ila 8'de 1</c>'inde (<c>%12.5..%20</c>) Buyuk Yeralti Karstik Akifer Golu olusur.
    /// - Tum yeralti sulari %100 Tatli Su'dur (<c>Salinity = false</c>); su cekildiginde (<see cref="ExtractWaterUnits(ref KarstAquiferBasinRecord, float, Span{ushort}, int, int)"/>)
    ///   tamamen kuruyabilir (<c>0</c>) ve yuzeye yagmur yagdiginda yalnizca gecirgen tavan altindaki yataklar yavasca yeniden dolar (<c>0 B/kare</c> GC).
    /// </summary>
    public sealed class KarstHydrologyAndAquiferSystem
    {
        public const int ChamberSizeMeters = 24;
        public const int MaxTrackedBasins = 512;

        /// <summary>Vadoz kusak gecikme zaman sabiti (~12 oyun dakikasi): yagmur yuzeyden yeraltina aninda degil suzulerek iner.</summary>
        public const float VadosePercolationTauSeconds = 720.0f;

        /// <summary>24m x 24m magara beslenme havzasinin etkin epikarst toplama alani (m^2).</summary>
        public const float DefaultEpikarstCatchmentAreaM2 = 48.0f;

        private readonly KarstAquiferBasinRecord[] _basins = new KarstAquiferBasinRecord[MaxTrackedBasins];
        private int _basinCount;
        private int _nextBasinId = 1;

        public int BasinCount => _basinCount;

        public ReadOnlySpan<KarstAquiferBasinRecord> ActiveBasins => _basins.AsSpan(0, _basinCount);

        /// <summary>
        /// Yuzey/ortu malzemesinin gecirimsiz masif kaya, buz veya volkanik kabuk olup olmadigini doner ("Tastan Su Gecmez").
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsImpermeableCapRockMaterial(byte materialId)
        {
            return materialId == MaterialIdCatalog.Stone ||
                   materialId == MaterialIdCatalog.Cobblestone ||
                   materialId == MaterialIdCatalog.Basalt ||
                   materialId == MaterialIdCatalog.NaturalObsidian ||
                   materialId == MaterialIdCatalog.MagmaBlock ||
                   materialId == MaterialIdCatalog.Bedrock ||
                   materialId == MaterialIdCatalog.PackedIce ||
                   materialId == MaterialIdCatalog.StoneBrick ||
                   materialId == MaterialIdCatalog.ObsidianBrick ||
                   materialId == MaterialIdCatalog.ReinforcedSteelBlock;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPermeableOverburdenMaterial(byte materialId)
        {
            return materialId == MaterialIdCatalog.Dirt ||
                   materialId == MaterialIdCatalog.Grass ||
                   materialId == MaterialIdCatalog.Sand ||
                   materialId == MaterialIdCatalog.Gravel ||
                   materialId == MaterialIdCatalog.LooseRubble ||
                   materialId == MaterialIdCatalog.Marble ||
                   materialId == MaterialIdCatalog.GlowingMoss;
        }

        /// <summary>
        /// Uretilmis bir chunk sutununda magara tavaninin (<paramref name="caveRoofY"/>) ustundeki ortu tabakasini
        /// dogrudan voksel verisinden olcer (0 B GC).
        /// </summary>
        public static float MeasureColumnOverburdenPermeability(
            ReadOnlySpan<uint> chunkVoxels,
            int lx,
            int lz,
            int caveRoofY,
            int maxY,
            out bool isImpermeableMassiveRockRoof)
        {
            isImpermeableMassiveRockRoof = true;
            if ((uint)lx >= 16u || (uint)lz >= 16u)
            {
                return 0.0f;
            }

            int safeMaxY = Math.Min(maxY - 1, (chunkVoxels.Length >> 8) - 1);
            int startY = Math.Max(1, caveRoofY + 1);
            if (startY > safeMaxY)
            {
                return 0.0f;
            }

            int col = (lz << 4) | lx;
            int permeableSoilCount = 0;
            int massiveRockCount = 0;
            byte topSurfaceMat = MaterialIdCatalog.Air;

            for (int y = safeMaxY; y >= startY; y--)
            {
                uint raw = chunkVoxels[(y << 8) | col];
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                if (mat == MaterialIdCatalog.Air || !VoxelBitUtility.IsPhysicallySolid(raw))
                {
                    continue;
                }

                if (topSurfaceMat == MaterialIdCatalog.Air)
                {
                    topSurfaceMat = mat;
                }

                if (IsPermeableOverburdenMaterial(mat))
                {
                    permeableSoilCount++;
                }
                else
                {
                    massiveRockCount++;
                }
            }

            if (topSurfaceMat == MaterialIdCatalog.Air ||
                IsImpermeableCapRockMaterial(topSurfaceMat) ||
                permeableSoilCount == 0 ||
                massiveRockCount >= 42)
            {
                isImpermeableMassiveRockRoof = true;
                return 0.0f;
            }

            float soilFactor = Math.Clamp(permeableSoilCount / 4.0f, 0.20f, 1.0f);
            float rockAttenuation = Math.Clamp(1.0f - (massiveRockCount - 16) / 32.0f, 0.15f, 1.0f);
            float perm = Math.Clamp(soilFactor * rockAttenuation, 0.0f, 1.0f);
            isImpermeableMassiveRockRoof = perm < 0.22f;
            return isImpermeableMassiveRockRoof ? 0.0f : perm;
        }

        /// <summary>
        /// 24m x 24m makro-magara odasinin ortu gecirgenligini ve karst hidroloji sinifini deterministik olarak hesaplar:
        /// - <c>&gt;= %60</c> (<c>~%61..%64</c>, <c>[%58..%65]</c>): Kalin gecirimsiz masif kaya -&gt; <see cref="CaveChamberHydrologyKind.DryImpermeable"/> (<c>0</c> suzulme).
        /// - <c>~%36..%39</c> (<c>[%35..%42]</c>): Gecirgen toprak/epikarst -&gt; Islak Magara (<see cref="CaveChamberHydrologyKind.WetTalwegSeepage"/> + <see cref="CaveChamberHydrologyKind.WetMajorKarstAquifer"/>).
        /// - Islak magaralarin <c>~%15.4</c>'unde (<c>6 / 39 ~ 1/6.5</c>, <c>[%12.5..%20.0]</c> = <c>1/8..1/5</c>): <see cref="CaveChamberHydrologyKind.WetMajorKarstAquifer"/>.
        /// - Tum odalarin <c>~%14.5..%15.0</c>'inde (<c>[%12.5..%17.5]</c>): <c>HasActiveStandingPool == true</c> (Paket 3 AK-5 tam uyumu).
        /// </summary>
        public static CaveChamberHydrologyEvaluation EvaluateCaveChamberHydrology(
            int wx,
            int wz,
            int worldSeed = 1453,
            byte surfaceMaterialId = MaterialIdCatalog.Grass,
            int solidOverburdenThicknessBlocks = 24)
        {
            int chamberX = FloorDiv(wx, ChamberSizeMeters);
            int chamberZ = FloorDiv(wz, ChamberSizeMeters);
            const int chamberY = 3; // Katman 1-2 (y=46..73) 24m x 16m x 24m makro-oda katmani
            uint hChamber = HashChamberPcg(chamberX, chamberY, chamberZ, worldSeed);
            int yWater = 48 + (int)((hChamber >> 8) % 22u); // [48..69]

            // Baslangic vadisi merkez meydani ve [F12] vitrin odalarinda (r < 28m) su olusturma:
            if (wx * wx + wz * wz < 28 * 28)
            {
                return new CaveChamberHydrologyEvaluation(
                    CaveChamberHydrologyKind.DryImpermeable,
                    isImpermeableDryRoof: true,
                    hasActiveStandingPool: false,
                    hasMajorKarstAquiferLake: false,
                    overburdenPermeability01: 0.0f,
                    targetWaterFloorY: yWater);
            }

            // Ust yuzey masif kaya/buz/bazalt ise veya ortu kayaci asiri kalinsa (> 48m catlaksiz masif kaya) "tastan su gecmez":
            if (IsImpermeableCapRockMaterial(surfaceMaterialId) || solidOverburdenThicknessBlocks > 48)
            {
                return new CaveChamberHydrologyEvaluation(
                    CaveChamberHydrologyKind.DryImpermeable,
                    isImpermeableDryRoof: true,
                    hasActiveStandingPool: false,
                    hasMajorKarstAquiferLake: false,
                    overburdenPermeability01: 0.0f,
                    targetWaterFloorY: yWater);
            }

            uint roll100 = hChamber % 100u;

            // roll100 in [40..99] -> %60 gecirimsiz masif anakaya tavani (buzul/volkanik kapaklarla kita genelinde ~%62.5-%63.5 Kuru!)
            if (roll100 >= 40u)
            {
                return new CaveChamberHydrologyEvaluation(
                    CaveChamberHydrologyKind.DryImpermeable,
                    isImpermeableDryRoof: true,
                    hasActiveStandingPool: false,
                    hasMajorKarstAquiferLake: false,
                    overburdenPermeability01: 0.0f,
                    targetWaterFloorY: yWater);
            }

            // roll100 in [0..38] -> %39 gecirgen toprak + catlakli epikarst (Islak Karst Magarasi!)
            float basePerm = 0.42f + (((hChamber >> 16) & 255u) / 255.0f) * 0.48f; // [0.42 .. 0.90]
            if (surfaceMaterialId == MaterialIdCatalog.Marble || surfaceMaterialId == MaterialIdCatalog.Gravel || surfaceMaterialId == MaterialIdCatalog.Sand)
            {
                basePerm = MathF.Min(0.96f, basePerm + 0.08f);
            }

            // Islak magaralarin 6 / 39 = %15.38'i (1 / 6.5 -> tam [1/8 .. 1/5] araliginin merkezi!) Buyuk Yeralti Karstik Akifer Golu:
            if (roll100 < 6u)
            {
                return new CaveChamberHydrologyEvaluation(
                    CaveChamberHydrologyKind.WetMajorKarstAquifer,
                    isImpermeableDryRoof: false,
                    hasActiveStandingPool: true,
                    hasMajorKarstAquiferLake: true,
                    overburdenPermeability01: basePerm,
                    targetWaterFloorY: yWater);
            }

            // roll100 in [6..15] -> Aktif talweg su canagi (roll100 < 16u toplami kita genelinde ~%15.0 -> Paket 3 AK-5 [%12.5..%17.5] tam merkezi!)
            bool hasStandingPool = roll100 < 16u;
            return new CaveChamberHydrologyEvaluation(
                CaveChamberHydrologyKind.WetTalwegSeepage,
                isImpermeableDryRoof: false,
                hasActiveStandingPool: hasStandingPool,
                hasMajorKarstAquiferLake: false,
                overburdenPermeability01: basePerm * 0.85f,
                targetWaterFloorY: yWater);
        }

        public int RegisterOrUpdateBasin(
            short anchorChunkX,
            short anchorChunkZ,
            byte equilibriumWaterY,
            bool isImpermeableDryRoof,
            float currentStoredUnits,
            float maxCapacityUnits,
            float overburdenPermeability01 = 0.65f,
            bool isMajorAquiferLake = false)
        {
            float clampedMax = MathF.Max(0.0f, maxCapacityUnits);
            float clampedCur = Math.Clamp(currentStoredUnits, 0.0f, clampedMax);
            float clampedPerm = isImpermeableDryRoof ? 0.0f : Math.Clamp(overburdenPermeability01, 0.05f, 1.0f);

            for (int i = 0; i < _basinCount; i++)
            {
                ref KarstAquiferBasinRecord existing = ref _basins[i];
                if (existing.AnchorChunkX == anchorChunkX &&
                    existing.AnchorChunkZ == anchorChunkZ &&
                    existing.EquilibriumWaterY == equilibriumWaterY)
                {
                    existing.IsImpermeableDryRoof = isImpermeableDryRoof;
                    existing.IsMajorAquiferLake = isMajorAquiferLake;
                    existing.OverburdenPermeability01 = clampedPerm;
                    existing.CurrentStoredUnits = clampedCur;
                    existing.MaxCapacityUnits = clampedMax;
                    return i;
                }
            }

            int targetIdx;
            if (_basinCount < MaxTrackedBasins)
            {
                targetIdx = _basinCount++;
            }
            else
            {
                targetIdx = _nextBasinId % MaxTrackedBasins;
            }

            _basins[targetIdx] = new KarstAquiferBasinRecord
            {
                BasinId = _nextBasinId++,
                AnchorChunkX = anchorChunkX,
                AnchorChunkZ = anchorChunkZ,
                EquilibriumWaterY = equilibriumWaterY,
                IsImpermeableDryRoof = isImpermeableDryRoof,
                IsMajorAquiferLake = isMajorAquiferLake,
                OverburdenPermeability01 = clampedPerm,
                CurrentStoredUnits = clampedCur,
                MaxCapacityUnits = clampedMax,
                PendingVadosePercolation = 0.0f,
                FractionalFluidSyncCarry = 0.0f
            };
            return targetIdx;
        }

        /// <summary>
        /// Uretilmis bir chunk icindeki magara su hucrelerini (<paramref name="minCaveY"/>..<paramref name="maxCaveY"/>)
        /// tarayarak dogrudan <see cref="KarstAquiferBasinRecord"/> kaydeder (0 B GC).
        /// </summary>
        public int RegisterBasinFromChunkFluids(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<ushort> fluids,
            bool isImpermeableDryRoof,
            float overburdenPermeability01 = 0.65f,
            bool isMajorAquiferLake = false,
            int minCaveY = 46,
            int maxCaveY = 73)
        {
            int safeMaxY = Math.Min(maxCaveY, (fluids.Length >> 8) - 1);
            int safeMinY = Math.Max(1, minCaveY);
            long totalMass = 0;
            int topWaterY = safeMinY;

            for (int y = safeMinY; y <= safeMaxY; y++)
            {
                int yBase = y << 8;
                for (int c = 0; c < 256; c++)
                {
                    ushort raw = fluids[yBase | c];
                    if (raw == 0) continue;
                    FluidCell16 fc = new FluidCell16(raw);
                    if (fc.Type == FluidType.Water && fc.Mass > 0)
                    {
                        totalMass += fc.Mass;
                        if (y > topWaterY) topWaterY = y;
                    }
                }
            }

            if (totalMass <= 0)
            {
                return -1;
            }

            return RegisterOrUpdateBasin(
                (short)chunkX,
                (short)chunkZ,
                (byte)Math.Clamp(topWaterY, 1, 254),
                isImpermeableDryRoof,
                currentStoredUnits: totalMass,
                maxCapacityUnits: totalMass,
                overburdenPermeability01: overburdenPermeability01,
                isMajorAquiferLake: isMajorAquiferLake);
        }

        public ref KarstAquiferBasinRecord GetBasinRef(int basinIndex)
        {
            if ((uint)basinIndex >= (uint)_basinCount)
            {
                throw new ArgumentOutOfRangeException(nameof(basinIndex));
            }

            return ref _basins[basinIndex];
        }

        public bool TryFindBasinForChunk(int chunkX, int chunkZ, out int basinIndex)
        {
            for (int i = 0; i < _basinCount; i++)
            {
                if (_basins[i].AnchorChunkX == chunkX && _basins[i].AnchorChunkZ == chunkZ)
                {
                    basinIndex = i;
                    return true;
                }
            }

            basinIndex = -1;
            return false;
        }

        /// <summary>
        /// Akifer yatagindan kova/tulum/pompa veya buharlasma ile su ceker.
        /// Sonsuz su kaynagi degildir: cekildikce azalir ve <c>0</c> birime kadar tamamen bosalabilir!
        /// Opsiyonel <paramref name="chunkFluids"/> verilirse fiziksel <see cref="FluidCell16"/> su hucrelerini de eszamanli bosaltir.
        /// </summary>
        public static float ExtractWaterUnits(
            ref KarstAquiferBasinRecord basin,
            float requestedUnits,
            Span<ushort> chunkFluids = default,
            int minBasinY = 46,
            int maxBasinY = 73)
        {
            if (requestedUnits <= 0.0f || basin.CurrentStoredUnits <= 0.0f)
            {
                return 0.0f;
            }

            float extracted = MathF.Min(requestedUnits, basin.CurrentStoredUnits);
            basin.CurrentStoredUnits -= extracted;
            if (basin.CurrentStoredUnits <= 1e-4f)
            {
                basin.CurrentStoredUnits = 0.0f;
            }

            if (!chunkFluids.IsEmpty)
            {
                basin.FractionalFluidSyncCarry -= extracted;
                int wholeDrain = basin.CurrentStoredUnits == 0.0f
                    ? int.MaxValue
                    : (int)MathF.Floor(-basin.FractionalFluidSyncCarry);

                if (wholeDrain > 0)
                {
                    int drainedCellsMass = DrainCaveWaterCellsTopDown(chunkFluids, wholeDrain, minBasinY, maxBasinY);
                    if (basin.CurrentStoredUnits == 0.0f)
                    {
                        basin.FractionalFluidSyncCarry = 0.0f;
                    }
                    else
                    {
                        basin.FractionalFluidSyncCarry += drainedCellsMass;
                    }
                }
            }

            return extracted;
        }

        public float ExtractWaterUnits(
            int basinIndex,
            float requestedUnits,
            Span<ushort> chunkFluids = default,
            int minBasinY = 46,
            int maxBasinY = 73)
        {
            if ((uint)basinIndex >= (uint)_basinCount)
            {
                return 0.0f;
            }

            return ExtractWaterUnits(ref _basins[basinIndex], requestedUnits, chunkFluids, minBasinY, maxBasinY);
        }

        /// <summary>
        /// Yuzeye yagmur yagdiginda gecirgen tavan altindaki akifer yatagini vadoz gecikme tamponu uzerinden
        /// yavasca yeniden Tatli Su (<c>Salinity = false</c>) ile doldurur.
        /// KURAL ("Tastan Su Gecmez"): <c>IsImpermeableDryRoof == true</c> ise her kosulda <c>0</c> suzulme ve <c>0</c> dolum yapar!
        /// </summary>
        public static float StepBasinRainRecharge(
            ref KarstAquiferBasinRecord basin,
            float surfacePrecipMmPerHour,
            float gameDeltaSeconds,
            ReadOnlySpan<uint> chunkVoxels = default,
            Span<ushort> chunkFluids = default,
            ChunkSalinityBitset? salinityBitset = null,
            int minBasinY = 46)
        {
            if (gameDeltaSeconds <= 0.0f || basin.MaxCapacityUnits <= 0.0f)
            {
                return 0.0f;
            }

            // KURAL: Kalin gecirimsiz masif kaya tavan altindaki kuru magaralara tek damla yagmur suzulmez!
            if (basin.IsImpermeableDryRoof || basin.OverburdenPermeability01 <= 0.0f)
            {
                basin.PendingVadosePercolation = 0.0f;
                return 0.0f;
            }

            // 1) Yuzey yagisinin gecirgen topraktan vadoz tamponuna suzulmesi (1 mm = 1 kg/m^2 = 255 / 1000 akiskan birimi/m^2)
            if (surfacePrecipMmPerHour > 0.01f)
            {
                float rainMm = surfacePrecipMmPerHour * (gameDeltaSeconds / 3600.0f);
                float unitsPerM2 = rainMm * WaterCycleAndEvaporationSystem.FluidUnitsPerMm;
                float percolatedInflow = unitsPerM2 * DefaultEpikarstCatchmentAreaM2 * basin.OverburdenPermeability01;
                float maxVadoseBuffer = MathF.Max(255.0f, basin.MaxCapacityUnits * 1.25f);
                basin.PendingVadosePercolation = MathF.Min(maxVadoseBuffer, basin.PendingVadosePercolation + percolatedInflow);
            }

            if (basin.PendingVadosePercolation <= 1e-5f || basin.CurrentStoredUnits >= basin.MaxCapacityUnits)
            {
                if (basin.CurrentStoredUnits >= basin.MaxCapacityUnits)
                {
                    // Yatak denge kotuna ulastiginda fazla suzulme doygun kaya yatagindan yanlara tahliye olur
                    basin.PendingVadosePercolation *= MathF.Exp(-gameDeltaSeconds / (VadosePercolationTauSeconds * 2.0f));
                }

                return 0.0f;
            }

            // 2) A-kararli ustel gecikmeyle vadoz tampondan yeralti canagina yavas damlama (600x hizda dahi kararli!)
            float releaseFraction = 1.0f - MathF.Exp(-gameDeltaSeconds / VadosePercolationTauSeconds);
            float releasedUnits = basin.PendingVadosePercolation * releaseFraction;
            float roomLeft = basin.MaxCapacityUnits - basin.CurrentStoredUnits;
            float actualRecharged = MathF.Min(roomLeft, releasedUnits);

            basin.PendingVadosePercolation = MathF.Max(0.0f, basin.PendingVadosePercolation - actualRecharged);
            basin.CurrentStoredUnits = MathF.Min(basin.MaxCapacityUnits, basin.CurrentStoredUnits + actualRecharged);

            // 3) Eger chunk voksel ve akiskan dizileri verilmisse tabandan EquilibriumWaterY kotuna kadar Tatli Su ekle
            if (actualRecharged > 0.0f && !chunkVoxels.IsEmpty && !chunkFluids.IsEmpty)
            {
                basin.FractionalFluidSyncCarry += actualRecharged;
                int wholeAdd = (int)MathF.Floor(basin.FractionalFluidSyncCarry);
                if (wholeAdd > 0)
                {
                    int addedToGrid = RefillSealedBasinCellsBottomUp(
                        basin.AnchorChunkX,
                        basin.AnchorChunkZ,
                        chunkVoxels,
                        chunkFluids,
                        salinityBitset,
                        wholeAdd,
                        minBasinY,
                        basin.EquilibriumWaterY);
                    basin.FractionalFluidSyncCarry -= addedToGrid;
                    if (basin.FractionalFluidSyncCarry < 0.0f)
                    {
                        basin.FractionalFluidSyncCarry = 0.0f;
                    }
                }
            }

            return actualRecharged;
        }

        public float StepAllBasinsRainRecharge(float surfacePrecipMmPerHour, float gameDeltaSeconds)
        {
            if (_basinCount == 0 || gameDeltaSeconds <= 0.0f)
            {
                return 0.0f;
            }

            float totalRecharged = 0.0f;
            for (int i = 0; i < _basinCount; i++)
            {
                totalRecharged += StepBasinRainRecharge(ref _basins[i], surfacePrecipMmPerHour, gameDeltaSeconds);
            }

            return totalRecharged;
        }

        private static int DrainCaveWaterCellsTopDown(Span<ushort> fluids, int unitsToDrain, int minY, int maxY)
        {
            int safeMaxY = Math.Min(maxY, (fluids.Length >> 8) - 1);
            int safeMinY = Math.Max(1, minY);
            int remaining = unitsToDrain;
            int drained = 0;

            for (int y = safeMaxY; y >= safeMinY && remaining > 0; y--)
            {
                int yBase = y << 8;
                for (int c = 0; c < 256 && remaining > 0; c++)
                {
                    int idx = yBase | c;
                    ushort raw = fluids[idx];
                    if (raw == 0) continue;

                    FluidCell16 fc = new FluidCell16(raw);
                    if (fc.Type != FluidType.Water || fc.Mass == 0) continue;

                    int take = Math.Min((int)fc.Mass, remaining);
                    int left = fc.Mass - take;
                    fluids[idx] = left <= 0
                        ? (ushort)0
                        : FluidCell16.Pack(FluidType.Water, (byte)left, isSettled: true, fc.PressureHead).RawValue;
                    remaining -= take;
                    drained += take;
                }
            }

            return drained;
        }

        private static int RefillSealedBasinCellsBottomUp(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<uint> voxels,
            Span<ushort> fluids,
            ChunkSalinityBitset? salinityBitset,
            int unitsToAdd,
            int minY,
            int equilibriumY)
        {
            int safeMaxY = Math.Min(equilibriumY, Math.Min((voxels.Length >> 8) - 1, (fluids.Length >> 8) - 1));
            int safeMinY = Math.Max(2, minY);
            int remaining = unitsToAdd;
            int added = 0;

            for (int y = safeMinY; y <= safeMaxY && remaining > 0; y++)
            {
                int yBase = y << 8;
                int belowBase = (y - 1) << 8;

                for (int lz = 2; lz <= 13 && remaining > 0; lz++)
                {
                    for (int lx = 2; lx <= 13 && remaining > 0; lx++)
                    {
                        int c = (lz << 4) | lx;
                        int idx = yBase | c;
                        if (voxels[idx] != 0u) continue;

                        // Alt tabanin kati sizdirmaz kaya veya dolu alt su katmani oldugunu dogrula
                        uint belowVox = voxels[belowBase | c];
                        FluidCell16 belowFl = new FluidCell16(fluids[belowBase | c]);
                        bool sealedFloor = (belowVox != 0u && VoxelBitUtility.IsPhysicallySolid(belowVox)) ||
                                           (belowVox == 0u && belowFl.Type == FluidType.Water && belowFl.Mass == 255);
                        if (!sealedFloor) continue;

                        // 4 yatay kenarin kapali kaya veya ayni havza boslugu icinde oldugunu kontrol et
                        FluidCell16 curFl = new FluidCell16(fluids[idx]);
                        if (curFl.Type != FluidType.None && curFl.Type != FluidType.Water) continue;
                        if (curFl.Mass >= 255) continue;

                        // Yalnizca mevcut su hucresini veya etrafinda kaya canagi bulunan kapali cukuru doldur
                        if (curFl.Mass == 0)
                        {
                            int rockWalls = 0;
                            if (voxels[yBase | (c - 1)] != 0u || new FluidCell16(fluids[yBase | (c - 1)]).Type == FluidType.Water) rockWalls++;
                            if (voxels[yBase | (c + 1)] != 0u || new FluidCell16(fluids[yBase | (c + 1)]).Type == FluidType.Water) rockWalls++;
                            if (voxels[yBase | (c - 16)] != 0u || new FluidCell16(fluids[yBase | (c - 16)]).Type == FluidType.Water) rockWalls++;
                            if (voxels[yBase | (c + 16)] != 0u || new FluidCell16(fluids[yBase | (c + 16)]).Type == FluidType.Water) rockWalls++;
                            if (rockWalls < 4) continue;
                        }

                        int space = 255 - curFl.Mass;
                        int put = Math.Min(space, remaining);
                        byte head = y < safeMaxY ? (byte)2 : (byte)1;
                        fluids[idx] = FluidCell16.Pack(FluidType.Water, (byte)(curFl.Mass + put), isSettled: true, pressureHead: head).RawValue;
                        salinityBitset?.SetSaltWaterFlat(chunkX, chunkZ, idx, false); // %100 Tatli Su!
                        remaining -= put;
                        added += put;
                    }
                }
            }

            return added;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashChamberPcg(int chamberX, int chamberY, int chamberZ, int seed)
        {
            uint h = (uint)(chamberX * 73856093) ^
                     (uint)(chamberY * 19349663) ^
                     (uint)(chamberZ * 83492791) ^
                     (uint)(seed * 374761393);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            int rem = a % b;
            if (rem != 0 && ((rem < 0) != (b < 0)))
            {
                q--;
            }

            return q;
        }
    }
}
