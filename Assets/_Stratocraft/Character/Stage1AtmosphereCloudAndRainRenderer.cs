#nullable enable
using System;
using SysVec3 = System.Numerics.Vector3;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 4A Faz 4A.3: 64m x 64m atmosfer hucresi basina 3B bulut hacmi, golge izdusumu
    /// ve yerel yagmur perdesi/cizgisi icin kullanilan hafifsiklet, sifir-GC (blittable) ornek yapisi.
    /// </summary>
    public struct CloudCellRenderSample
    {
        public float WorldCenterX;
        public float WorldCenterZ;
        public float CloudWaterKgPerM2;
        public float CloudBaseY;
        public float CloudTopY;
        public float PrecipMmPerHour;
        public float WindX;
        public float WindZ;

        public float X
        {
            get => WorldCenterX;
            set => WorldCenterX = value;
        }

        public float Z
        {
            get => WorldCenterZ;
            set => WorldCenterZ = value;
        }

        public int CellX => Stage1AtmosphereCloudAndRainRenderer.WorldToCellCoord(WorldCenterX);
        public int CellZ => Stage1AtmosphereCloudAndRainRenderer.WorldToCellCoord(WorldCenterZ);

        public float PrecipitationMmPerHour
        {
            get => PrecipMmPerHour;
            set => PrecipMmPerHour = value;
        }

        public float WindVelX
        {
            get => WindX;
            set => WindX = value;
        }

        public float WindVelZ
        {
            get => WindZ;
            set => WindZ = value;
        }

        public CloudCellRenderSample(
            float worldCenterX,
            float worldCenterZ,
            float cloudWaterKgPerM2,
            float cloudBaseY,
            float cloudTopY,
            float precipMmPerHour,
            float windX = 0.0f,
            float windZ = 0.0f)
        {
            WorldCenterX = worldCenterX;
            WorldCenterZ = worldCenterZ;
            CloudWaterKgPerM2 = cloudWaterKgPerM2;
            CloudBaseY = cloudBaseY;
            CloudTopY = cloudTopY;
            PrecipMmPerHour = precipMmPerHour;
            WindX = windX;
            WindZ = windZ;
        }

        public static CloudCellRenderSample FromCellIndices(
            int cellX,
            int cellZ,
            float cloudWaterKgPerM2,
            float cloudBaseY = 180.0f,
            float cloudTopY = 235.0f,
            float precipMmPerHour = 0.0f,
            float windX = 0.0f,
            float windZ = 0.0f)
        {
            const float cellSize = Stage1AtmosphereCloudAndRainRenderer.AtmosphereCellSizeMeters;
            return new CloudCellRenderSample(
                (cellX + 0.5f) * cellSize,
                (cellZ + 0.5f) * cellSize,
                cloudWaterKgPerM2,
                cloudBaseY,
                cloudTopY,
                precipMmPerHour,
                windX,
                windZ);
        }
    }

    /// <summary>
    /// Paket 4A Faz 4A.3: Rayleigh (lambda^-4) ve Mie sacilmasina dayali gun dogumu / gun batimi
    /// kizil ufuk optik sonucu ("Aksam kizilligi gune iyilik, sabah kizilligi firtina habercisi").
    /// </summary>
    public readonly struct RayleighHorizonTint
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float CrimsonIntensity01;
        public readonly bool IsFairWeatherEveningGlow;
        public readonly bool IsStormWarningMorningGlow;

        public float X => R;
        public float Y => G;
        public float Z => B;
        public float RedToBlueRatio => R / MathF.Max(0.01f, B);

        public RayleighHorizonTint(
            float r,
            float g,
            float b,
            float crimsonIntensity01,
            bool isFairWeatherEveningGlow,
            bool isStormWarningMorningGlow)
        {
            R = r;
            G = g;
            B = b;
            CrimsonIntensity01 = crimsonIntensity01;
            IsFairWeatherEveningGlow = isFairWeatherEveningGlow;
            IsStormWarningMorningGlow = isStormWarningMorningGlow;
        }

        public SysVec3 ToVector3() => new SysVec3(R, G, B);

        public static implicit operator SysVec3(RayleighHorizonTint tint) => new SysVec3(tint.R, tint.G, tint.B);
    }

    /// <summary>
    /// Paket 4A Faz 4A.3: Sifir ek doku VRAM (0.0 MB) ve kare basina 0 B GC ile calisan
    /// fiziksel 3B dunya-uzayi bulut hacmi, dinamik bulut golgesi ve yalnizca yagisli bulutun
    /// 64m x 64m izdusumu altina yagan yerel yagmur perdesi denetleyicisi.
    /// </summary>
    public sealed class Stage1AtmosphereCloudAndRainRenderer
    {
        public const float AtmosphereCellSizeMeters = 64.0f;
        public const float HalfCellSizeMeters = 32.0f;
        public const float MinLocalRainPrecipMmPerHour = 0.1f;
        public const float MinVisibleCloudWaterKgPerM2 = 0.05f;
        public const float MinCloudShadowAttenuation = 0.25f;
        public const float MaxCloudShadowAttenuation = 1.00f;
        public const float DefaultCloudBaseWorldY = 180.0f;
        public const float DefaultCloudTopWorldY = 236.0f;
        public const int MaxActiveCloudCells = 81; // 9x9 AOI penceresi (576m x 576m)

        private readonly CloudCellRenderSample[] _activeCells = new CloudCellRenderSample[MaxActiveCloudCells];
        private int _activeCellCount;

        public int ActiveCellCount => _activeCellCount;
        public ReadOnlySpan<CloudCellRenderSample> ActiveCells => new ReadOnlySpan<CloudCellRenderSample>(_activeCells, 0, _activeCellCount);

        // ==============================================================================
        // 1. SAF C# MATEMATIK, YEREL YAGMUR IZDUSUMU, BULUT GOLGESI VE RAYLEIGH OPTIGI
        // (.NET 10 StandaloneCoreTests + Unity 6 EditMode uzerinde %100 birim test edilebilir)
        // ==============================================================================

        public static int WorldToCellCoord(float worldCoord)
        {
            return (int)MathF.Floor(worldCoord / AtmosphereCellSizeMeters);
        }

        public static bool IsPointInsideCellFootprintXZ(float worldX, float worldZ, float cellCenterX, float cellCenterZ)
        {
            int ptCellX = WorldToCellCoord(worldX);
            int ptCellZ = WorldToCellCoord(worldZ);
            int sampleCellX = WorldToCellCoord(cellCenterX);
            int sampleCellZ = WorldToCellCoord(cellCenterZ);

            if (ptCellX == sampleCellX && ptCellZ == sampleCellZ)
            {
                return true;
            }

            return MathF.Abs(worldX - cellCenterX) <= HalfCellSizeMeters &&
                   MathF.Abs(worldZ - cellCenterZ) <= HalfCellSizeMeters;
        }

        /// <summary>
        /// AK-4 &amp; AK-8: Yalnizca ilgili 64m x 64m atmosfer hucresinde precipMmPerHour &gt; 0.1f ise
        /// VE worldY &gt;= skyOcclusionHeightY (ustu aciksa!) true doner; cati/magara altinda veya
        /// komsu kuru/bulutsuz hucrede kesinlikle false doner.
        /// </summary>
        public static bool IsWorldPointUnderLocalRain(
            float worldX,
            float worldZ,
            float worldY,
            int skyOcclusionHeightY,
            float cellPrecipMmPerHour,
            float cloudBaseY = DefaultCloudBaseWorldY)
        {
            if (float.IsNaN(worldX) || float.IsNaN(worldZ) || float.IsNaN(worldY) || float.IsNaN(cellPrecipMmPerHour))
            {
                return false;
            }

            if (cellPrecipMmPerHour <= MinLocalRainPrecipMmPerHour)
            {
                return false;
            }

            // Cati, sundurma veya magara tavani altindaysa yagmur gecmez
            if (worldY < skyOcclusionHeightY)
            {
                return false;
            }

            // Bulut tabaninin ustundeki irtifaya yagmur dusmez
            float effectiveCloudBaseY = cloudBaseY > 1.0f ? cloudBaseY : DefaultCloudBaseWorldY;
            if (worldY > effectiveCloudBaseY)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tekil bir 64m x 64m bulut hucresi ornegine gore dunya noktasinin yerel yagmur altinda olup olmadigini denetler.
        /// </summary>
        public static bool IsWorldPointUnderLocalRain(
            float worldX,
            float worldZ,
            float worldY,
            int skyOcclusionHeightY,
            in CloudCellRenderSample cellSample)
        {
            if (!IsPointInsideCellFootprintXZ(worldX, worldZ, cellSample.WorldCenterX, cellSample.WorldCenterZ))
            {
                return false;
            }

            if (cellSample.CloudWaterKgPerM2 < 0.0f)
            {
                return false;
            }

            return IsWorldPointUnderLocalRain(
                worldX,
                worldZ,
                worldY,
                skyOcclusionHeightY,
                cellSample.PrecipMmPerHour,
                cellSample.CloudBaseY);
        }

        /// <summary>
        /// Aktif 64m x 64m bulut hucreleri dizisi icinde (worldX, worldZ) noktasini kapsayan hucreyi bulur;
        /// yalnizca o hucrede yagis &gt; 0.1 mm/sa ise ve worldY &gt;= skyOcclusionHeightY ise true doner.
        /// Komsu kuru hucrelerde kesinlikle false doner (AK-4).
        /// </summary>
        public static bool IsWorldPointUnderLocalRain(
            float worldX,
            float worldZ,
            float worldY,
            int skyOcclusionHeightY,
            ReadOnlySpan<CloudCellRenderSample> cells)
        {
            if (worldY < skyOcclusionHeightY || cells.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < cells.Length; i++)
            {
                ref readonly CloudCellRenderSample cell = ref cells[i];
                if (IsPointInsideCellFootprintXZ(worldX, worldZ, cell.WorldCenterX, cell.WorldCenterZ))
                {
                    return IsWorldPointUnderLocalRain(
                        worldX,
                        worldZ,
                        worldY,
                        skyOcclusionHeightY,
                        cell.PrecipMmPerHour,
                        cell.CloudBaseY);
                }
            }

            return false;
        }

        public static bool IsWorldPointUnderLocalRain(
            float worldX,
            float worldZ,
            float worldY,
            int skyOcclusionHeightY,
            CloudCellRenderSample[]? cells)
        {
            if (cells == null || cells.Length == 0)
            {
                return false;
            }

            return IsWorldPointUnderLocalRain(worldX, worldZ, worldY, skyOcclusionHeightY, new ReadOnlySpan<CloudCellRenderSample>(cells));
        }

        /// <summary>
        /// Gunes yonune gore dunya-uzayi bulut yogunluk golge carpanini [0.25 .. 1.0] araliginda hesaplar.
        /// Bulutsuz acik gokyuzunde 1.0, yogun Cumulonimbus altinda 0.25'e kadar duser.
        /// </summary>
        public static float ComputeCloudShadowAttenuation(
            float worldX,
            float worldZ,
            float cloudWaterKgPerM2,
            float sunDirY = 0.85f)
        {
            if (float.IsNaN(cloudWaterKgPerM2) || cloudWaterKgPerM2 <= 0.005f || sunDirY <= -0.05f)
            {
                return MaxCloudShadowAttenuation;
            }

            // Dunya-uzayi 3B deterministik Perlin-Worley bulut ic yogunluk modulasyonu
            float noiseMod = EvaluateDeterministicCloudDensityNoise01(worldX, worldZ);
            float slantPathFactor = 1.0f / MathF.Max(0.25f, MathF.Min(1.0f, sunDirY));
            float opticalDepth = cloudWaterKgPerM2 * (0.48f + 0.32f * noiseMod) * MathF.Min(1.65f, slantPathFactor);
            float beerLambert = MathF.Exp(-opticalDepth);

            return ClampFloat(beerLambert, MinCloudShadowAttenuation, MaxCloudShadowAttenuation);
        }

        /// <summary>
        /// Yuzey noktasindan (worldX, worldY, worldZ) gunes vektoru (sunDirX, sunDirY, sunDirZ) yonunde
        /// bulut taban kotuna isin izdusumu alarak tekil bulut hucresinin golge zayiflatmasini [0.25 .. 1.0] hesaplar.
        /// </summary>
        public static float ComputeCloudShadowAttenuation(
            float worldX,
            float worldY,
            float worldZ,
            float sunDirX,
            float sunDirY,
            float sunDirZ,
            in CloudCellRenderSample cellSample)
        {
            if (cellSample.CloudWaterKgPerM2 <= 0.005f || sunDirY <= -0.05f)
            {
                return MaxCloudShadowAttenuation;
            }

            float cloudBaseY = cellSample.CloudBaseY > 1.0f ? cellSample.CloudBaseY : DefaultCloudBaseWorldY;
            float rayT = MathF.Max(0.0f, (cloudBaseY - worldY) / MathF.Max(0.08f, sunDirY));
            float projX = worldX + sunDirX * rayT;
            float projZ = worldZ + sunDirZ * rayT;

            float dx = MathF.Abs(projX - cellSample.WorldCenterX);
            float dz = MathF.Abs(projZ - cellSample.WorldCenterZ);
            // 64m x 64m hucre sinirinda yumusak kenar gecisi (32m tam + 16m yarim-golge penumbra)
            if (dx > HalfCellSizeMeters + 16.0f || dz > HalfCellSizeMeters + 16.0f)
            {
                return MaxCloudShadowAttenuation;
            }

            float edgeX = Clamp01(1.0f - MathF.Max(0.0f, dx - 24.0f) / 24.0f);
            float edgeZ = Clamp01(1.0f - MathF.Max(0.0f, dz - 24.0f) / 24.0f);
            float effectiveWater = cellSample.CloudWaterKgPerM2 * edgeX * edgeZ;

            return ComputeCloudShadowAttenuation(projX, projZ, effectiveWater, sunDirY);
        }

        /// <summary>
        /// Aktif bulut hucreleri uzerinde gunes vektoru izdusumu ile dunya-uzayi bulut golge carpanini [0.25 .. 1.0] hesaplar.
        /// </summary>
        public static float ComputeCloudShadowAttenuation(
            float worldX,
            float worldZ,
            ReadOnlySpan<CloudCellRenderSample> cells,
            float sunDirX = 0.0f,
            float sunDirY = 1.0f,
            float sunDirZ = 0.0f,
            float surfaceWorldY = 90.0f)
        {
            if (cells.Length == 0 || sunDirY <= -0.05f)
            {
                return MaxCloudShadowAttenuation;
            }

            float minAtten = MaxCloudShadowAttenuation;
            for (int i = 0; i < cells.Length; i++)
            {
                float cellAtten = ComputeCloudShadowAttenuation(
                    worldX,
                    surfaceWorldY,
                    worldZ,
                    sunDirX,
                    sunDirY,
                    sunDirZ,
                    in cells[i]);
                if (cellAtten < minAtten)
                {
                    minAtten = cellAtten;
                }
            }

            return ClampFloat(minAtten, MinCloudShadowAttenuation, MaxCloudShadowAttenuation);
        }

        /// <summary>
        /// Rayleigh (lambda^-4) ve Mie sacilmasina dayali sabah/aksam kizil gokyuzu denizci kuralini hesaplar:
        /// - Gunes ufka indikce (|sunY| &lt;= 0.35) mavi dalga boyu sacilir, kizil/kehribar ton (R &gt; G &gt; B) artar.
        /// - Bati kuru ve temizken (westHumidity01 dusuk, basinc normal/yuksek) sicak mercan-kizil gun batimi ("Aksam kizilligi gune iyilik").
        /// - Dogudan/ufuktan nemli alcak basinc cephesi gelirken (eastHumidity01 yuksek veya airPressureHpa dusuk) koyu kizil gun dogumu firtina uyarisi.
        /// </summary>
        public static RayleighHorizonTint ComputeRayleighSunriseSunsetTint(
            float sunY,
            float westHumidity01,
            float eastHumidity01,
            float airPressureHpa)
        {
            float clampedWestHum = Clamp01(westHumidity01);
            float clampedEastHum = Clamp01(eastHumidity01);
            float safePressure = (float.IsNaN(airPressureHpa) || airPressureHpa <= 100.0f) ? 1013.25f : airPressureHpa;

            // Gunes ufka yaklastiginda (sunY in [-0.12 .. +0.38]) optik hava kutlesi (air mass) hizla buyur
            float absSunY = MathF.Abs(sunY);
            float horizonProximity01 = Clamp01(1.0f - (absSunY / 0.42f));
            float airMassFactor = horizonProximity01 * horizonProximity01;

            // 1) Bati kuru/temizken sicak kizil gun batimi ("Red sky at night, sailor's delight")
            float dryWestScore = Clamp01((0.65f - clampedWestHum) * 1.65f) * Clamp01((safePressure - 1002.0f) / 18.0f);
            bool isFairEvening = horizonProximity01 > 0.25f && dryWestScore > 0.35f && clampedWestHum < 0.55f;

            // 2) Nemli alcak basinc cephesi varken kizil gun dogumu uyarisi ("Red sky at morning, sailors take warning")
            float lowPressureDrop = Clamp01((1014.0f - safePressure) / 22.0f);
            float stormMorningScore = Clamp01(clampedEastHum * 0.72f + lowPressureDrop * 0.55f);
            bool isStormMorning = horizonProximity01 > 0.25f && (clampedEastHum >= 0.55f || safePressure <= 1006.0f);

            // Bati tamamen kapali firtina duvarina gomulmusse (westHumidity cok yuksek, eastHumidity dusuk) kizillik bogulur
            float overcastBlockDamp = (clampedWestHum > 0.85f && clampedEastHum < 0.40f && safePressure >= 1008.0f)
                ? 0.42f
                : 1.0f;

            float crimsonWeight = Clamp01(airMassFactor * MathF.Max(0.45f, MathF.Max(dryWestScore, stormMorningScore)) * overcastBlockDamp);

            // Ogle vakti (sunY >= 0.5) dengeli gun isigi; ufukta Rayleigh lambda^-4 ile mavi/yesil sogurulur, kirmizi baskin kalir
            float noonR = 0.96f;
            float noonG = 0.97f;
            float noonB = 1.00f;

            float horizonR = isStormMorning ? 0.98f : 1.00f;
            float horizonG = isStormMorning ? 0.28f : 0.46f;
            float horizonB = isStormMorning ? 0.16f : 0.22f;

            float r = LerpFloat(noonR, horizonR, crimsonWeight);
            float g = LerpFloat(noonG, horizonG, crimsonWeight);
            float b = LerpFloat(noonB, horizonB, crimsonWeight);

            return new RayleighHorizonTint(
                r,
                g,
                b,
                crimsonWeight,
                isFairEvening,
                isStormMorning);
        }

        public void ClearActiveCloudCells()
        {
            _activeCellCount = 0;
        }

        public void UpdateActiveCloudCells(ReadOnlySpan<CloudCellRenderSample> samples)
        {
            int count = Math.Min(samples.Length, MaxActiveCloudCells);
            for (int i = 0; i < count; i++)
            {
                _activeCells[i] = samples[i];
            }
            _activeCellCount = count;
        }

        public bool IsWorldPointUnderActiveRain(float worldX, float worldY, float worldZ, int skyOcclusionHeightY)
        {
            return IsWorldPointUnderLocalRain(worldX, worldZ, worldY, skyOcclusionHeightY, ActiveCells);
        }

        public float EvaluateCloudShadowAtWorldPoint(
            float worldX,
            float worldY,
            float worldZ,
            float sunDirX = 0.0f,
            float sunDirY = 0.85f,
            float sunDirZ = 0.0f)
        {
            return ComputeCloudShadowAttenuation(worldX, worldZ, ActiveCells, sunDirX, sunDirY, sunDirZ, worldY);
        }

        private static float EvaluateDeterministicCloudDensityNoise01(float worldX, float worldZ)
        {
            float px = worldX * 0.025f;
            float pz = worldZ * 0.025f;
            float n1 = MathF.Sin(px * 1.7f + MathF.Cos(pz * 1.3f));
            float n2 = MathF.Cos(pz * 2.9f - MathF.Sin(px * 2.1f));
            return Clamp01(0.5f + 0.28f * n1 + 0.22f * n2);
        }

        private static float Clamp01(float v) => v < 0.0f ? 0.0f : (v > 1.0f ? 1.0f : v);

        private static float ClampFloat(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        private static float LerpFloat(float a, float b, float t) => a + (b - a) * Clamp01(t);

#if UNITY_5_3_OR_NEWER
        // ==============================================================================
        // 2. UNITY 6 PROSEDUREL 3B BULUT & YEREL YAGMUR RENDER KOPRUSU (0 B/kare GC)
        // ==============================================================================
        private static readonly int s_globalCloudGridParamsId = Shader.PropertyToID("_GlobalCloudGridParams");
        private static readonly int s_globalWindVectorId = Shader.PropertyToID("_GlobalWindVector");
        private static readonly int s_globalHorizonOpticsParamsId = Shader.PropertyToID("_GlobalHorizonOpticsParams");

        private readonly Matrix4x4[] _cloudMatrices = new Matrix4x4[MaxActiveCloudCells];
        private readonly Matrix4x4[] _rainShaftMatrices = new Matrix4x4[MaxActiveCloudCells];
        private Mesh? _sharedCloudVolumeMesh;
        private Mesh? _sharedRainCurtainMesh;
        private Material? _proceduralCloudRainMaterial;

        public void EnsureRenderResourcesInitialized()
        {
            if (_sharedCloudVolumeMesh == null)
            {
                _sharedCloudVolumeMesh = BuildFacetedCloudVolumeMesh(isRainShaft: false);
            }

            if (_sharedRainCurtainMesh == null)
            {
                _sharedRainCurtainMesh = BuildFacetedCloudVolumeMesh(isRainShaft: true);
            }

            if (_proceduralCloudRainMaterial == null)
            {
                Shader? cloudShader = Shader.Find("Stratocraft/ProceduralClouds");
                if (cloudShader != null)
                {
                    _proceduralCloudRainMaterial = new Material(cloudShader)
                    {
                        name = "StratocraftProceduralClouds_RuntimeMat",
                        enableInstancing = true,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
            }
        }

        /// <summary>
        /// Kuresel bulut golgesi, ruzgar vektoru ve Rayleigh ufuk parametrelerini GPU'ya yazar.
        /// Bulut olmadiginda _GlobalCloudGridParams.w = 0.0 yazilarak StratocraftUberShader.hlsl
        /// nesnelerinin %100 orijinal parlakligini korumasi garanti edilir.
        /// </summary>
        public void ApplyGlobalAtmosphereShaderUniforms(
            float sunDirY,
            float westHumidity01 = 0.45f,
            float eastHumidity01 = 0.45f,
            float airPressureHpa = 1013.25f)
        {
            float sumWater = 0.0f;
            float sumBaseY = 0.0f;
            float sumTopY = 0.0f;
            float sumWindX = 0.0f;
            float sumWindZ = 0.0f;
            float maxPrecip = 0.0f;
            int cloudCount = 0;

            for (int i = 0; i < _activeCellCount; i++)
            {
                ref readonly CloudCellRenderSample c = ref _activeCells[i];
                sumWindX += c.WindX;
                sumWindZ += c.WindZ;
                if (c.PrecipMmPerHour > maxPrecip)
                {
                    maxPrecip = c.PrecipMmPerHour;
                }

                if (c.CloudWaterKgPerM2 > MinVisibleCloudWaterKgPerM2)
                {
                    sumWater += c.CloudWaterKgPerM2;
                    sumBaseY += c.CloudBaseY > 1.0f ? c.CloudBaseY : DefaultCloudBaseWorldY;
                    sumTopY += c.CloudTopY > 2.0f ? c.CloudTopY : DefaultCloudTopWorldY;
                    cloudCount++;
                }
            }

            float invTotal = _activeCellCount > 0 ? 1.0f / _activeCellCount : 0.0f;
            float avgWindX = sumWindX * invTotal;
            float avgWindZ = sumWindZ * invTotal;
            float windSpeed = Mathf.Sqrt(avgWindX * avgWindX + avgWindZ * avgWindZ);
            float stormDarkening01 = Mathf.Clamp01(maxPrecip / 18.0f);

            if (cloudCount > 0)
            {
                float invCloud = 1.0f / cloudCount;
                float avgWater = sumWater * invCloud;
                float avgBaseY = sumBaseY * invCloud;
                float avgTopY = sumTopY * invCloud;
                float shadowStrength = Mathf.Clamp01(avgWater * 0.55f);

                Shader.SetGlobalVector(s_globalCloudGridParamsId, new Vector4(avgWater, avgBaseY, avgTopY, shadowStrength));
            }
            else
            {
                Shader.SetGlobalVector(s_globalCloudGridParamsId, Vector4.zero);
            }

            Shader.SetGlobalVector(s_globalWindVectorId, new Vector4(avgWindX, windSpeed, avgWindZ, stormDarkening01));

            RayleighHorizonTint horizonTint = ComputeRayleighSunriseSunsetTint(sunDirY, westHumidity01, eastHumidity01, airPressureHpa);
            Shader.SetGlobalVector(
                s_globalHorizonOpticsParamsId,
                new Vector4(westHumidity01, eastHumidity01, airPressureHpa, horizonTint.CrimsonIntensity01));
        }

        /// <summary>
        /// Aktif 64m x 64m atmosfer hucrelerindeki 3B bulut hacimlerini ve yalnizca yagisli hucrelerin
        /// altindaki yerel yagmur perdelerini 0 B/kare GC ile Graphics.DrawMeshInstanced uzerinden cizer.
        /// </summary>
        public void RenderProceduralCloudsAndLocalRain(float defaultGroundOccluderY = 90.0f)
        {
            if (_activeCellCount <= 0)
            {
                return;
            }

            EnsureRenderResourcesInitialized();
            if (_proceduralCloudRainMaterial == null || _sharedCloudVolumeMesh == null || _sharedRainCurtainMesh == null)
            {
                return;
            }

            int cloudBatchCount = 0;
            int rainBatchCount = 0;

            for (int i = 0; i < _activeCellCount; i++)
            {
                ref readonly CloudCellRenderSample cell = ref _activeCells[i];
                if (cell.CloudWaterKgPerM2 > MinVisibleCloudWaterKgPerM2)
                {
                    float baseY = cell.CloudBaseY > 1.0f ? cell.CloudBaseY : DefaultCloudBaseWorldY;
                    float topY = cell.CloudTopY > baseY + 4.0f ? cell.CloudTopY : baseY + 42.0f;
                    float thicknessY = topY - baseY;
                    float centerY = baseY + thicknessY * 0.5f;

                    Vector3 pos = new Vector3(cell.WorldCenterX, centerY, cell.WorldCenterZ);
                    Vector3 scale = new Vector3(AtmosphereCellSizeMeters * 0.96f, thicknessY, AtmosphereCellSizeMeters * 0.96f);
                    _cloudMatrices[cloudBatchCount++] = Matrix4x4.TRS(pos, Quaternion.identity, scale);

                    if (cell.PrecipMmPerHour > MinLocalRainPrecipMmPerHour && baseY > defaultGroundOccluderY + 1.0f)
                    {
                        float rainHeight = baseY - defaultGroundOccluderY;
                        float rainCenterY = defaultGroundOccluderY + rainHeight * 0.5f;
                        Vector3 rainPos = new Vector3(cell.WorldCenterX, rainCenterY, cell.WorldCenterZ);
                        Vector3 rainScale = new Vector3(AtmosphereCellSizeMeters * 0.88f, rainHeight, AtmosphereCellSizeMeters * 0.88f);
                        _rainShaftMatrices[rainBatchCount++] = Matrix4x4.TRS(rainPos, Quaternion.identity, rainScale);
                    }
                }
            }

            if (cloudBatchCount > 0)
            {
                Graphics.DrawMeshInstanced(
                    _sharedCloudVolumeMesh,
                    0,
                    _proceduralCloudRainMaterial,
                    _cloudMatrices,
                    cloudBatchCount,
                    null,
                    ShadowCastingMode.Off,
                     false,
                    0);
            }

            if (rainBatchCount > 0)
            {
                Graphics.DrawMeshInstanced(
                    _sharedRainCurtainMesh,
                    0,
                    _proceduralCloudRainMaterial,
                    _rainShaftMatrices,
                    rainBatchCount,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    0);
            }
        }

        public void Dispose()
        {
            Shader.SetGlobalVector(s_globalCloudGridParamsId, Vector4.zero);
            Shader.SetGlobalVector(s_globalWindVectorId, Vector4.zero);
            Shader.SetGlobalVector(s_globalHorizonOpticsParamsId, Vector4.zero);

            if (_sharedCloudVolumeMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_sharedCloudVolumeMesh);
                _sharedCloudVolumeMesh = null;
            }

            if (_sharedRainCurtainMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_sharedRainCurtainMesh);
                _sharedRainCurtainMesh = null;
            }

            if (_proceduralCloudRainMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_proceduralCloudRainMaterial);
                _proceduralCloudRainMaterial = null;
            }
        }

        private static Mesh BuildFacetedCloudVolumeMesh(bool isRainShaft)
        {
            Mesh mesh = new Mesh
            {
                name = isRainShaft ? "StratocraftLocalRainShaftMesh" : "StratocraftProceduralCloudBoxMesh",
                hideFlags = HideFlags.HideAndDontSave
            };

            Vector3[] verts = new Vector3[8]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f)
            };

            Vector3[] normals = new Vector3[8]
            {
                new Vector3(-0.577f, -0.577f, -0.577f),
                new Vector3( 0.577f, -0.577f, -0.577f),
                new Vector3( 0.577f,  0.577f, -0.577f),
                new Vector3(-0.577f,  0.577f, -0.577f),
                new Vector3(-0.577f, -0.577f,  0.577f),
                new Vector3( 0.577f, -0.577f,  0.577f),
                new Vector3( 0.577f,  0.577f,  0.577f),
                new Vector3(-0.577f,  0.577f,  0.577f)
            };

            Color32 vertexTag = isRainShaft
                ? new Color32(255, 180, 90, 220)   // R=255 (>=0.5): Yerel Yagmur Perdesi/Cizgisi
                : new Color32(0, 200, 80, 240);    // R=0 (<0.5): 3B Bulut Hacmi

            Color32[] colors = new Color32[8];
            for (int i = 0; i < 8; i++)
            {
                colors[i] = vertexTag;
            }

            int[] indices = new int[36]
            {
                0, 2, 1, 0, 3, 2, // Front
                1, 6, 5, 1, 2, 6, // Right
                5, 7, 4, 5, 6, 7, // Back
                4, 3, 0, 4, 7, 3, // Left
                3, 6, 2, 3, 7, 6, // Top
                4, 1, 5, 4, 0, 1  // Bottom
            };

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }
#endif
    }
}
