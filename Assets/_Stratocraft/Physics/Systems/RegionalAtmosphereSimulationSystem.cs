#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Weather;
using Stratocraft.Physics.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Faz 4A.2 (IP-2): 32x32 (64m x 64m hucre = 2048m x 2048m) 2B Bolgesel Fiziksel Atmosfer Motoru.
    /// Gunes isinimi -> Yuzey isinmasi -> Tetens/Dalton buharlasma -> LCL bulut tabani -> Kessler yogusma/yagmur ->
    /// Deniz-kara meltemi + Basinc gradyani + Yari-Lagrange adveksiyon -> Orografik yagis & Fohn yagmur golgesi ->
    /// Konvektif CAPE (>1000 J/kg simsek) zincirini sifir rastgele zar (0 RNG) ve 0 B/kare GC ile calistirir.
    /// </summary>
    public sealed class RegionalAtmosphereSimulationSystem
    {
        public const int CellSizeMeters = StratocraftConstants.AtmosphereCellSizeMeters; // 64m
        public const int GridSideCells = StratocraftConstants.AtmosphereGridSideCells;   // 32
        public const int TotalCells = GridSideCells * GridSideCells;                     // 1024
        public const float DomainSizeMeters = GridSideCells * CellSizeMeters;            // 2048.0f
        public const float MaxSubstepGameSeconds = StratocraftConstants.MaxAtmosphereSubstepGameSeconds; // 15.0f

        /// <summary>Atmosfer sinir katmaninda 1 hPa su buhari basincina karsilik gelen su sutunu kutlesi (kg/m^2).</summary>
        public const float VaporKgPerM2PerHpa = 1.35f;

        /// <summary>CAPE konvektif simsek esigi (J/kg).</summary>
        public const ushort ThunderstormCapeThresholdJPerKg = 1000;

        private const float DegToRad = MathF.PI / 180.0f;
        private const float InvCellSize = 1.0f / CellSizeMeters;

        // Onceden ayrilmis sabit diziler -> Calisma zamaninda 0 B/kare GC
        private AtmosphereCellState[] _cellsFront = new AtmosphereCellState[TotalCells];
        private AtmosphereCellState[] _cellsBack = new AtmosphereCellState[TotalCells];

        private readonly float[] _terrainHeightMeters = new float[TotalCells];
        private readonly float[] _seaFraction01 = new float[TotalCells];
        private readonly float[] _surfaceAlbedo01 = new float[TotalCells];
        private readonly float[] _seaLevelPressureAnomHpa = new float[TotalCells];

        private float _originWorldX = -0.5f * DomainSizeMeters; // -1024m
        private float _originWorldZ = -0.5f * DomainSizeMeters; // -1024m
        private EnsoRechargeOscillatorState _ensoState;

        public float OriginWorldX => _originWorldX;
        public float OriginWorldZ => _originWorldZ;

        public float LatitudeDegrees { get; set; } = StratocraftConstants.DefaultClimateLatitudeDegrees;

        /// <summary>Hakim bati ruzgari (+X: Bati -> Dogu, m/s).</summary>
        public float PrevailingWindX { get; set; } = 6.0f;

        /// <summary>Hakim meridyonel ruzgar bileseni (+Z: Guney -> Kuzey, m/s).</summary>
        public float PrevailingWindZ { get; set; } = 0.5f;

        public EnsoRechargeOscillatorState EnsoState
        {
            get => _ensoState;
            set => _ensoState = value;
        }

        public ReadOnlySpan<AtmosphereCellState> Cells => _cellsFront;

        public RegionalAtmosphereSimulationSystem(int worldSeed = 14532026)
        {
            _ensoState = EnsoRechargeOscillatorState.CreateDefault(worldSeed);
            InitializeDefaultMesoscaleTerrain(worldSeed);
            InitializeEquilibriumAtmosphere(totalWorldSeconds: 1800.0);
        }

        /// <summary>
        /// Varsayilan bolgesel kesit: Bati okyanusu (cx 0..7) -> Kiyi ovasi (8..12) ->
        /// Siradag yamaci ve zirvesi (13..19) -> Dogu Fohn yagmur golgesi havzasi (20..31).
        /// </summary>
        public void InitializeDefaultMesoscaleTerrain(int worldSeed = 14532026)
        {
            float seedPhase = ((worldSeed & 0xFF) / 255.0f) * MathF.PI * 2.0f;
            for (int cz = 0; cz < GridSideCells; cz++)
            {
                float zWave = 1.0f + (0.12f * MathF.Sin((cz * 0.28f) + seedPhase));
                for (int cx = 0; cx < GridSideCells; cx++)
                {
                    int idx = (cz * GridSideCells) + cx;
                    if (cx < 8)
                    {
                        _terrainHeightMeters[idx] = 0.0f;
                        _seaFraction01[idx] = 1.0f;
                        _surfaceAlbedo01[idx] = 0.08f;
                    }
                    else if (cx < 13)
                    {
                        float t = (cx - 7) / 5.0f;
                        _terrainHeightMeters[idx] = 8.0f + (36.0f * t);
                        _seaFraction01[idx] = cx == 8 ? 0.25f : 0.0f;
                        _surfaceAlbedo01[idx] = 0.18f;
                    }
                    else if (cx <= 20)
                    {
                        // Siradag sirti: cx=16.5 civarinda ~260m zirve
                        float dist = (cx - 16.5f) / 3.8f;
                        float bell = MathF.Exp(-dist * dist * 1.65f);
                        _terrainHeightMeters[idx] = (32.0f + (235.0f * bell)) * zWave;
                        _seaFraction01[idx] = 0.0f;
                        _surfaceAlbedo01[idx] = 0.22f;
                    }
                    else
                    {
                        _terrainHeightMeters[idx] = 22.0f + (4.0f * MathF.Sin(cz * 0.2f));
                        _seaFraction01[idx] = 0.0f;
                        _surfaceAlbedo01[idx] = 0.28f;
                    }
                }
            }
        }

        public void InitializeEquilibriumAtmosphere(double totalWorldSeconds = 1800.0)
        {
            EvaluateInternalSolar(totalWorldSeconds, LatitudeDegrees, out float declDeg, out _, out float clearSkyWm2);
            float baseSeaLevelTempC = 18.5f + (9.5f * (declDeg / StratocraftConstants.SolarAxialTiltDegrees)) + (1.4f * _ensoState.TeAnomalyK);

            for (int i = 0; i < TotalCells; i++)
            {
                float h = _terrainHeightMeters[i];
                float sea = _seaFraction01[i];
                float airT = baseSeaLevelTempC - (0.0065f * h);
                float surfT = airT + ((1.0f - sea) * (clearSkyWm2 * 0.003f));
                float pStation = ComputeHydrostaticStationPressureHpa(1013.25f, h, airT);
                float es = ComputeTetensSaturationVaporHpa(airT);
                float rhInit = (0.84f * sea) + (0.52f * (1.0f - sea));
                float ea = es * rhInit;
                float td = ComputeDewPointCelsius(airT, ea);
                float lcl = ComputeLclCloudBaseWorldY(h, airT, td);
                ushort baseY = (ushort)Math.Clamp((int)MathF.Round(lcl), 0, ushort.MaxValue);

                _cellsFront[i] = new AtmosphereCellState
                {
                    SurfaceTempCelsius = surfT,
                    AirTempCelsius = airT,
                    StationPressureHpa = pStation,
                    VaporPressureHpa = ea,
                    CloudWaterKgPerM2 = sea > 0.5f ? 0.12f : 0.02f,
                    SurfacePrecipMmPerHour = 0.0f,
                    WindVelX = PrevailingWindX,
                    WindVelZ = PrevailingWindZ,
                    CloudBaseWorldY = baseY,
                    CloudTopWorldY = sea > 0.5f ? (ushort)Math.Min(65535, baseY + 128) : baseY,
                    CapeJPerKg = 0,
                    ActiveKind = PrecipitationKind.None,
                    Hazards = AtmosphericHazardFlags.None
                };
            }
        }

        public void SetGridOrigin(float originWorldX, float originWorldZ)
        {
            if (float.IsFinite(originWorldX))
            {
                _originWorldX = originWorldX;
            }

            if (float.IsFinite(originWorldZ))
            {
                _originWorldZ = originWorldZ;
            }
        }

        /// <summary>
        /// Bolgesel 32x32 pencereyi oyuncu merkezine 64m hucre adimlarinda kaydirir (0 B GC).
        /// </summary>
        public void RecenterAroundWorldXZ(float centerWorldX, float centerWorldZ)
        {
            float targetOriginX = MathF.Floor((centerWorldX - (0.5f * DomainSizeMeters)) * InvCellSize) * CellSizeMeters;
            float targetOriginZ = MathF.Floor((centerWorldZ - (0.5f * DomainSizeMeters)) * InvCellSize) * CellSizeMeters;

            int shiftX = (int)MathF.Round((targetOriginX - _originWorldX) * InvCellSize);
            int shiftZ = (int)MathF.Round((targetOriginZ - _originWorldZ) * InvCellSize);
            if (shiftX == 0 && shiftZ == 0)
            {
                return;
            }

            _originWorldX = targetOriginX;
            _originWorldZ = targetOriginZ;

            for (int cz = 0; cz < GridSideCells; cz++)
            {
                int srcZ = Math.Clamp(cz + shiftZ, 0, GridSideCells - 1);
                for (int cx = 0; cx < GridSideCells; cx++)
                {
                    int srcX = Math.Clamp(cx + shiftX, 0, GridSideCells - 1);
                    _cellsBack[(cz * GridSideCells) + cx] = _cellsFront[(srcZ * GridSideCells) + srcX];
                }
            }

            (_cellsFront, _cellsBack) = (_cellsBack, _cellsFront);
        }

        public void ResetUniformTerrain(float altitudeAboveSeaMeters = 0.0f, float openWaterFraction01 = 0.0f)
        {
            float h = Math.Clamp(float.IsFinite(altitudeAboveSeaMeters) ? altitudeAboveSeaMeters : 0.0f, 0.0f, 5000.0f);
            float sea = Math.Clamp(float.IsFinite(openWaterFraction01) ? openWaterFraction01 : 0.0f, 0.0f, 1.0f);
            float albedo = (0.08f * sea) + (0.20f * (1.0f - sea));

            for (int i = 0; i < TotalCells; i++)
            {
                _terrainHeightMeters[i] = h;
                _seaFraction01[i] = sea;
                _surfaceAlbedo01[i] = albedo;
            }
        }

        public void SetCellTerrain(
            int cellX,
            int cellZ,
            float altitudeAboveSeaMeters,
            float openWaterFraction01,
            float surfaceAlbedo01 = -1.0f)
        {
            if ((uint)cellX >= GridSideCells || (uint)cellZ >= GridSideCells)
            {
                return;
            }

            int idx = (cellZ * GridSideCells) + cellX;
            float h = Math.Clamp(float.IsFinite(altitudeAboveSeaMeters) ? altitudeAboveSeaMeters : 0.0f, 0.0f, 5000.0f);
            float sea = Math.Clamp(float.IsFinite(openWaterFraction01) ? openWaterFraction01 : 0.0f, 0.0f, 1.0f);
            _terrainHeightMeters[idx] = h;
            _seaFraction01[idx] = sea;
            _surfaceAlbedo01[idx] = surfaceAlbedo01 >= 0.0f
                ? Math.Clamp(surfaceAlbedo01, 0.04f, 0.85f)
                : ((0.08f * sea) + (0.20f * (1.0f - sea)));

            ref AtmosphereCellState cell = ref _cellsFront[idx];
            cell.StationPressureHpa = ComputeHydrostaticStationPressureHpa(1013.25f, h, cell.AirTempCelsius);
            float td = ComputeDewPointCelsius(cell.AirTempCelsius, cell.VaporPressureHpa);
            cell.CloudBaseWorldY = (ushort)Math.Clamp((int)MathF.Round(ComputeLclCloudBaseWorldY(h, cell.AirTempCelsius, td)), 0, 65535);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCellTerrain(int cellX, int cellZ, float altitudeAboveSeaMeters, bool isSeaOrOpenWater)
        {
            SetCellTerrain(cellX, cellZ, altitudeAboveSeaMeters, isSeaOrOpenWater ? 1.0f : 0.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCellTerrainHeight(int cellX, int cellZ)
        {
            int cx = Math.Clamp(cellX, 0, GridSideCells - 1);
            int cz = Math.Clamp(cellZ, 0, GridSideCells - 1);
            return _terrainHeightMeters[(cz * GridSideCells) + cx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCellSeaFraction(int cellX, int cellZ)
        {
            int cx = Math.Clamp(cellX, 0, GridSideCells - 1);
            int cz = Math.Clamp(cellZ, 0, GridSideCells - 1);
            return _seaFraction01[(cz * GridSideCells) + cx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AtmosphereCellState GetCell(int cellX, int cellZ)
        {
            int cx = Math.Clamp(cellX, 0, GridSideCells - 1);
            int cz = Math.Clamp(cellZ, 0, GridSideCells - 1);
            return _cellsFront[(cz * GridSideCells) + cx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly AtmosphereCellState GetCellRef(int cellX, int cellZ)
        {
            int cx = Math.Clamp(cellX, 0, GridSideCells - 1);
            int cz = Math.Clamp(cellZ, 0, GridSideCells - 1);
            return ref _cellsFront[(cz * GridSideCells) + cx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCell(int cellX, int cellZ, in AtmosphereCellState state)
        {
            if ((uint)cellX >= GridSideCells || (uint)cellZ >= GridSideCells)
            {
                return;
            }

            _cellsFront[(cellZ * GridSideCells) + cellX] = state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WorldToCellCoords(float worldX, float worldZ, out int cellX, out int cellZ)
        {
            int rawX = (int)MathF.Floor((worldX - _originWorldX) * InvCellSize);
            int rawZ = (int)MathF.Floor((worldZ - _originWorldZ) * InvCellSize);
            bool inside = (uint)rawX < GridSideCells && (uint)rawZ < GridSideCells;
            cellX = Math.Clamp(rawX, 0, GridSideCells - 1);
            cellZ = Math.Clamp(rawZ, 0, GridSideCells - 1);
            return inside;
        }

        /// <summary>
        /// Yuzey su/toprak buharlasmasindan (Dalton Yasasi) atmosfere eklenen tatli su buhari kutlesini
        /// (kg/m^2 == mm su yuksekligi) dogrudan ilgili atmosfer sutununa kutle korunumlu aktarir.
        /// </summary>
        public void InjectSurfaceEvaporationMass(int cellX, int cellZ, float evaporatedKgPerM2)
        {
            if ((uint)cellX >= GridSideCells || (uint)cellZ >= GridSideCells)
            {
                return;
            }

            if (evaporatedKgPerM2 <= 0.0f || !float.IsFinite(evaporatedKgPerM2))
            {
                return;
            }

            int idx = (cellZ * GridSideCells) + cellX;
            ref AtmosphereCellState cell = ref _cellsFront[idx];

            float addedHpa = evaporatedKgPerM2 / VaporKgPerM2PerHpa;
            float totalEa = cell.VaporPressureHpa + addedHpa;
            float es = ComputeTetensSaturationVaporHpa(cell.AirTempCelsius);

            if (totalEa > es)
            {
                float excessKgM2 = (totalEa - es) * VaporKgPerM2PerHpa;
                cell.VaporPressureHpa = Math.Clamp(es, 0.05f, 95.0f);
                cell.CloudWaterKgPerM2 = Math.Clamp(cell.CloudWaterKgPerM2 + excessKgM2, 0.0f, 18.0f);
            }
            else
            {
                cell.VaporPressureHpa = Math.Clamp(totalEa, 0.05f, 95.0f);
            }

            float h = _terrainHeightMeters[idx];
            float td = ComputeDewPointCelsius(cell.AirTempCelsius, cell.VaporPressureHpa);
            ushort baseY = (ushort)Math.Clamp((int)MathF.Round(ComputeLclCloudBaseWorldY(h, cell.AirTempCelsius, td)), 0, 65535);
            cell.CloudBaseWorldY = baseY;
            if (cell.CloudWaterKgPerM2 > 0.02f)
            {
                int depth = Math.Clamp(64 + (int)MathF.Round(cell.CloudWaterKgPerM2 * 320.0f), 64, 4800);
                cell.CloudTopWorldY = (ushort)Math.Min(65535, baseY + depth);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InjectSurfaceEvaporationMassAtWorldXZ(float worldX, float worldZ, float evaporatedKgPerM2)
        {
            WorldToCellCoords(worldX, worldZ, out int cx, out int cz);
            InjectSurfaceEvaporationMass(cx, cz, evaporatedKgPerM2);
        }

        /// <summary>
        /// Dunya (worldX, worldZ) koordinatindaki atmosfer durumunu cift-dogrusal (bilinear) enterpolasyonla ornekler (0 B GC).
        /// </summary>
        public AtmosphereCellState SampleBilinearAtWorldXZ(float worldX, float worldZ)
        {
            float gx = ((worldX - _originWorldX) * InvCellSize) - 0.5f;
            float gz = ((worldZ - _originWorldZ) * InvCellSize) - 0.5f;

            gx = Math.Clamp(float.IsFinite(gx) ? gx : 0.0f, 0.0f, GridSideCells - 1.0001f);
            gz = Math.Clamp(float.IsFinite(gz) ? gz : 0.0f, 0.0f, GridSideCells - 1.0001f);

            int x0 = (int)gx;
            int z0 = (int)gz;
            int x1 = Math.Min(x0 + 1, GridSideCells - 1);
            int z1 = Math.Min(z0 + 1, GridSideCells - 1);

            float tx = gx - x0;
            float tz = gz - z0;

            ref readonly AtmosphereCellState c00 = ref _cellsFront[(z0 * GridSideCells) + x0];
            ref readonly AtmosphereCellState c10 = ref _cellsFront[(z0 * GridSideCells) + x1];
            ref readonly AtmosphereCellState c01 = ref _cellsFront[(z1 * GridSideCells) + x0];
            ref readonly AtmosphereCellState c11 = ref _cellsFront[(z1 * GridSideCells) + x1];

            float w00 = (1.0f - tx) * (1.0f - tz);
            float w10 = tx * (1.0f - tz);
            float w01 = (1.0f - tx) * tz;
            float w11 = tx * tz;

            ref readonly AtmosphereCellState nearest = ref (tx < 0.5f
                ? ref (tz < 0.5f ? ref c00 : ref c01)
                : ref (tz < 0.5f ? ref c10 : ref c11));

            float precip = (c00.SurfacePrecipMmPerHour * w00)
                         + (c10.SurfacePrecipMmPerHour * w10)
                         + (c01.SurfacePrecipMmPerHour * w01)
                         + (c11.SurfacePrecipMmPerHour * w11);

            float cape = (c00.CapeJPerKg * w00)
                       + (c10.CapeJPerKg * w10)
                       + (c01.CapeJPerKg * w01)
                       + (c11.CapeJPerKg * w11);

            ushort capeUshort = (ushort)Math.Clamp((int)MathF.Round(cape), 0, 65535);
            ushort baseY = (ushort)Math.Clamp((int)MathF.Round(
                (c00.CloudBaseWorldY * w00) + (c10.CloudBaseWorldY * w10) + (c01.CloudBaseWorldY * w01) + (c11.CloudBaseWorldY * w11)), 0, 65535);
            ushort topY = (ushort)Math.Clamp((int)MathF.Round(
                (c00.CloudTopWorldY * w00) + (c10.CloudTopWorldY * w10) + (c01.CloudTopWorldY * w01) + (c11.CloudTopWorldY * w11)), 0, 65535);

            return new AtmosphereCellState
            {
                SurfaceTempCelsius = (c00.SurfaceTempCelsius * w00) + (c10.SurfaceTempCelsius * w10) + (c01.SurfaceTempCelsius * w01) + (c11.SurfaceTempCelsius * w11),
                AirTempCelsius = (c00.AirTempCelsius * w00) + (c10.AirTempCelsius * w10) + (c01.AirTempCelsius * w01) + (c11.AirTempCelsius * w11),
                StationPressureHpa = (c00.StationPressureHpa * w00) + (c10.StationPressureHpa * w10) + (c01.StationPressureHpa * w01) + (c11.StationPressureHpa * w11),
                VaporPressureHpa = (c00.VaporPressureHpa * w00) + (c10.VaporPressureHpa * w10) + (c01.VaporPressureHpa * w01) + (c11.VaporPressureHpa * w11),
                CloudWaterKgPerM2 = (c00.CloudWaterKgPerM2 * w00) + (c10.CloudWaterKgPerM2 * w10) + (c01.CloudWaterKgPerM2 * w01) + (c11.CloudWaterKgPerM2 * w11),
                SurfacePrecipMmPerHour = precip,
                WindVelX = (c00.WindVelX * w00) + (c10.WindVelX * w10) + (c01.WindVelX * w01) + (c11.WindVelX * w11),
                WindVelZ = (c00.WindVelZ * w00) + (c10.WindVelZ * w10) + (c01.WindVelZ * w01) + (c11.WindVelZ * w11),
                CloudBaseWorldY = baseY,
                CloudTopWorldY = Math.Max(baseY, topY),
                CapeJPerKg = capeUshort,
                ActiveKind = precip >= 0.1f ? PrecipitationKind.Rain : nearest.ActiveKind == PrecipitationKind.Rain && precip > 0.05f ? PrecipitationKind.Rain : PrecipitationKind.None,
                Hazards = capeUshort > ThunderstormCapeThresholdJPerKg ? AtmosphericHazardFlags.ThunderLightning : AtmosphericHazardFlags.None
            };
        }

        /// <summary>
        /// Oyuncu dunya koordinati cevresindeki 9x9 atmosfer AOI penceresini 0 B GC ile cikarir.
        /// </summary>
        public void ExtractAoiWindow9x9(
            float playerWorldX,
            float playerWorldZ,
            Span<AtmosphereAoiCellSample> outWindow81,
            out short centerCellX,
            out short centerCellZ)
        {
            WorldToCellCoords(playerWorldX, playerWorldZ, out int cxCenter, out int czCenter);
            centerCellX = (short)cxCenter;
            centerCellZ = (short)czCenter;

            int side = StratocraftConstants.AtmosphereAoiGridSideCells; // 9
            int half = side / 2;                                        // 4
            if (outWindow81.Length < side * side)
            {
                return;
            }

            int writeIdx = 0;
            for (int dz = -half; dz <= half; dz++)
            {
                int cz = Math.Clamp(czCenter + dz, 0, GridSideCells - 1);
                for (int dx = -half; dx <= half; dx++)
                {
                    int cx = Math.Clamp(cxCenter + dx, 0, GridSideCells - 1);
                    outWindow81[writeIdx++] = _cellsFront[(cz * GridSideCells) + cx].ToAoiSample();
                }
            }
        }

        /// <summary>
        /// 1x..600x zaman hizlandirma altinda kosulsuz kararli (substepDt &lt;= 15.0s + yari-Lagrange + analitik ustel)
        /// tam fiziksel atmosfer adimini yurutur.
        /// </summary>
        public void StepAtmosphere(double worldTimeSeconds, float gameDeltaSeconds)
        {
            if (gameDeltaSeconds <= 0.0f || !float.IsFinite(gameDeltaSeconds))
            {
                return;
            }

            double baseTime = double.IsFinite(worldTimeSeconds) ? Math.Max(0.0, worldTimeSeconds) : 0.0;
            float clampedTotalDt = Math.Min(gameDeltaSeconds, (float)StratocraftConstants.SecondsPerInGameYear);
            int substepCount = Math.Max(1, (int)Math.Ceiling(clampedTotalDt / (double)MaxSubstepGameSeconds));
            float subDt = clampedTotalDt / substepCount;

            double currentSimTime = baseTime;
            for (int step = 0; step < substepCount; step++)
            {
                StepSingleSubstep(currentSimTime, subDt);
                currentSimTime += subDt;
            }
        }

        public void StepAtmosphere(double worldTimeSeconds, float realDeltaTime, float timeScaleMultiplier)
        {
            if (realDeltaTime <= 0.0f || !float.IsFinite(realDeltaTime) || timeScaleMultiplier <= 0.0f || !float.IsFinite(timeScaleMultiplier))
            {
                return;
            }

            float clampedScale = Math.Clamp(
                timeScaleMultiplier,
                StratocraftConstants.MinTimeAccelerationMultiplier,
                StratocraftConstants.MaxTimeAccelerationMultiplier);
            StepAtmosphere(worldTimeSeconds, realDeltaTime * clampedScale);
        }

        private void StepSingleSubstep(double simTimeSeconds, float dt)
        {
            // 1) Cok yillik Jin (1997) ENSO osilatorunu analitik matris usteliyle ilerlet
            _ensoState.Step(dt);
            float ensoTe = _ensoState.TeAnomalyK;

            // 2) Anlik gunes deklinasyonu ve kisa dalga radyasyon (substep basina 1 kez hesaplanir)
            EvaluateInternalSolar(simTimeSeconds, LatitudeDegrees, out float declDeg, out _, out float clearSkyWm2);
            float seasonalFactor = declDeg / StratocraftConstants.SolarAxialTiltDegrees; // [-1 .. +1]
            float baseSeaTempC = 19.0f + (5.5f * seasonalFactor) + (1.35f * ensoTe);
            float baseSynopticLandTempC = 17.5f + (11.5f * seasonalFactor) + (1.65f * ensoTe);

            // Analitik A-kararli ustel gevseme katsayilari: alpha = 1 - exp(-dt / tau) in (0, 1)
            float alphaSeaSurf = 1.0f - MathF.Exp(-dt / 5400.0f);
            float alphaLandSurf = 1.0f - MathF.Exp(-dt / 850.0f);
            float alphaAirTemp = 1.0f - MathF.Exp(-dt / 540.0f);
            float alphaPressure = 1.0f - MathF.Exp(-dt / 280.0f);
            float alphaWind = 1.0f - MathF.Exp(-dt / 160.0f);
            float alphaCondense = 1.0f - MathF.Exp(-dt / 75.0f);
            float alphaCloudEvap = 1.0f - MathF.Exp(-dt / 220.0f);

            // 3) Radyatif yuzey isinmasi ve deniz seviyesi termal basinc anomalisi on-adimi
            for (int i = 0; i < TotalCells; i++)
            {
                ref AtmosphereCellState cell = ref _cellsFront[i];
                float h = _terrainHeightMeters[i];
                float sea = _seaFraction01[i];
                float albedo = _surfaceAlbedo01[i];

                float cloudAlbedo = Math.Clamp(cell.CloudWaterKgPerM2 / (cell.CloudWaterKgPerM2 + 1.8f), 0.0f, 0.78f);
                float netShortwaveWm2 = clearSkyWm2 * (1.0f - cloudAlbedo) * (1.0f - albedo);

                float eqSeaTs = baseSeaTempC + (0.0022f * netShortwaveWm2);
                float nightLongwaveCooling = clearSkyWm2 <= 0.0f ? (-6.8f * (1.0f - (0.65f * cloudAlbedo))) : -2.2f;
                float eqLandTs = baseSynopticLandTempC - (0.0065f * h) + (0.0185f * netShortwaveWm2) + nightLongwaveCooling;

                float eqTs = (sea * eqSeaTs) + ((1.0f - sea) * eqLandTs);
                float alphaTs = (sea * alphaSeaSurf) + ((1.0f - sea) * alphaLandSurf);
                cell.SurfaceTempCelsius = ClampFinite(cell.SurfaceTempCelsius + (alphaTs * (eqTs - cell.SurfaceTempCelsius)), -65.0f, 70.0f);

                // Deniz seviyesine indirgenmis termal + gizli isi basinc anomalisi (dag hidrostatik hatasindan arindirilmis!)
                float thermalLow = -0.38f * (cell.SurfaceTempCelsius - baseSeaTempC);
                float latentCloudLow = -0.90f * cell.CloudWaterKgPerM2;
                float vaporLow = -0.16f * (cell.VaporPressureHpa - 12.0f);
                float ensoPressure = 1.15f * ensoTe;
                _seaLevelPressureAnomHpa[i] = Math.Clamp(thermalLow + latentCloudLow + vaporLow + ensoPressure, -38.0f, 32.0f);
            }

            // 4) Ruzgar guncelleme + Yari-Lagrange adveksiyon + Orografi/Fohn + LCL + Kessler yagmur + CAPE
            float prevWindX = ClampFinite(PrevailingWindX - (0.55f * ensoTe), -30.0f, 30.0f);
            float prevWindZ = ClampFinite(PrevailingWindZ, -30.0f, 30.0f);

            for (int cz = 0; cz < GridSideCells; cz++)
            {
                int zPrev = Math.Max(0, cz - 1);
                int zNext = Math.Min(GridSideCells - 1, cz + 1);
                float invSpanZ = 1.0f / (Math.Max(1, zNext - zPrev) * CellSizeMeters);

                for (int cx = 0; cx < GridSideCells; cx++)
                {
                    int idx = (cz * GridSideCells) + cx;
                    int xPrev = Math.Max(0, cx - 1);
                    int xNext = Math.Min(GridSideCells - 1, cx + 1);
                    float invSpanX = 1.0f / (Math.Max(1, xNext - xPrev) * CellSizeMeters);

                    ref readonly AtmosphereCellState cur = ref _cellsFront[idx];
                    float h = _terrainHeightMeters[idx];
                    float sea = _seaFraction01[idx];

                    // Basinc gradyani (-grad P_anom) + Kara-Deniz termal meltemi (grad Ts) + Hakim ruzgar
                    float dPdx = (_seaLevelPressureAnomHpa[(cz * GridSideCells) + xNext] - _seaLevelPressureAnomHpa[(cz * GridSideCells) + xPrev]) * invSpanX;
                    float dPdz = (_seaLevelPressureAnomHpa[(zNext * GridSideCells) + cx] - _seaLevelPressureAnomHpa[(zPrev * GridSideCells) + cx]) * invSpanZ;

                    float dTsdx = (_cellsFront[(cz * GridSideCells) + xNext].SurfaceTempCelsius - _cellsFront[(cz * GridSideCells) + xPrev].SurfaceTempCelsius) * invSpanX;
                    float dTsdz = (_cellsFront[(zNext * GridSideCells) + cx].SurfaceTempCelsius - _cellsFront[(zPrev * GridSideCells) + cx].SurfaceTempCelsius) * invSpanZ;

                    float targetU = prevWindX - (220.0f * dPdx) + (42.0f * dTsdx);
                    float targetV = prevWindZ - (220.0f * dPdz) + (42.0f * dTsdz);

                    float u = cur.WindVelX + (alphaWind * (targetU - cur.WindVelX));
                    float v = cur.WindVelZ + (alphaWind * (targetV - cur.WindVelZ));
                    ClampWindVector(ref u, ref v, 35.0f);

                    // Yari-Lagrange geriye iz surme: x_dep = x - v_w * dt / 64m (Kosulsuz kararli!)
                    float depX = Math.Clamp(cx - (u * dt * InvCellSize), 0.0f, GridSideCells - 1.0001f);
                    float depZ = Math.Clamp(cz - (v * dt * InvCellSize), 0.0f, GridSideCells - 1.0001f);

                    SampleAdvectedFields(
                        depX,
                        depZ,
                        out float advVaporHpa,
                        out float advCloudKgM2,
                        out float advAirTempC,
                        out float depTerrainH);

                    // Orografik dikey hiz w_oro = v_w . grad(h) ve adim boyu tirmanis Delta_h
                    float dhDx = (_terrainHeightMeters[(cz * GridSideCells) + xNext] - _terrainHeightMeters[(cz * GridSideCells) + xPrev]) * invSpanX;
                    float dhDz = (_terrainHeightMeters[(zNext * GridSideCells) + cx] - _terrainHeightMeters[(zPrev * GridSideCells) + cx]) * invSpanZ;
                    float wOrographic = (u * dhDx) + (v * dhDz);
                    float stepClimbMeters = h - depTerrainH;

                    // Adyabatik sicaklik degisimi: yukselirken nemli/kuru soguma, arka yamacta alcalirken kuru Fohn isinmasi!
                    float lapseRate = stepClimbMeters >= 0.0f
                        ? (advCloudKgM2 > 0.1f ? 0.0060f : 0.0098f)
                        : 0.0125f; // Fohn kuru adyabatik isinma > nemli soguma!
                    float parcelAirTemp = advAirTempC - (lapseRate * stepClimbMeters);

                    float eqLocalAirT = (0.64f * cur.SurfaceTempCelsius) + (0.36f * (baseSynopticLandTempC - (0.0065f * h)));
                    if (wOrographic < -0.05f)
                    {
                        // Arka yamac Fohn isinma katkisi
                        eqLocalAirT += Math.Min(7.5f, -wOrographic * 1.35f);
                    }

                    float nextAirTempC = ClampFinite(parcelAirTemp + (alphaAirTemp * (eqLocalAirT - parcelAirTemp)), -60.0f, 60.0f);

                    // Dalton acik su / okyanus buharlasmasi (La Nina Te < 0 iken denizel nem artar, El Nino Te > 0 iken azalir)
                    float windSpeed = MathF.Sqrt((u * u) + (v * v));
                    float ea = advVaporHpa;
                    float qc = advCloudKgM2;

                    if (sea > 0.001f)
                    {
                        float esSurf = ComputeTetensSaturationVaporHpa(cur.SurfaceTempCelsius);
                        float ensoMoistureMod = Math.Clamp(1.0f - (0.065f * ensoTe), 0.72f, 1.25f);
                        float marineTargetEa = Math.Min(esSurf * 0.96f, esSurf * 0.86f * ensoMoistureMod);
                        if (marineTargetEa > ea)
                        {
                            float evapArg = dt * sea * (1.0f + (0.20f * windSpeed)) * (1.0f / 420.0f);
                            float alphaEvap = FastOneMinusExpNeg(evapArg);
                            ea += alphaEvap * (marineTargetEa - ea);
                        }
                    }

                    // Birlesik fiziksel dikey yukselme hizi: Orografi + Termal Konveksiyon + Alcak Basinc Yakinsamasi
                    float dUdx = (_cellsFront[(cz * GridSideCells) + xNext].WindVelX - _cellsFront[(cz * GridSideCells) + xPrev].WindVelX) * invSpanX;
                    float dVdz = (_cellsFront[(zNext * GridSideCells) + cx].WindVelZ - _cellsFront[(zPrev * GridSideCells) + cx].WindVelZ) * invSpanZ;
                    float windConvergence = -(dUdx + dVdz);
                    float wConvective = MathF.Max(0.0f, 0.28f * (cur.SurfaceTempCelsius - nextAirTempC - 1.2f));
                    float wConvergence = Math.Clamp(windConvergence * 85.0f, -0.6f, 2.5f);
                    float totalUpdraftMps = wOrographic + wConvective + wConvergence;

                    // Yukselme sogumasiyla bulut yogusmasi vs Fohn/cokme altinda bulut buharlasmasi (kutle korunumlu!)
                    float liftedCoolingC = totalUpdraftMps > 0.0f
                        ? Math.Min(14.5f, (2.25f * totalUpdraftMps) + (wOrographic > 0.05f ? 0.012f * h : 0.0f))
                        : 0.0f;
                    float liftedTempC = nextAirTempC - liftedCoolingC;
                    float esAmbient = ComputeTetensSaturationVaporHpa(nextAirTempC);
                    float esLifted = liftedCoolingC > 0.01f ? ComputeTetensSaturationVaporHpa(liftedTempC) : esAmbient;

                    if (ea > esLifted && wOrographic >= -0.08f)
                    {
                        float boost = totalUpdraftMps > 0.2f ? Math.Min(1.0f, alphaCondense * (1.0f + (0.65f * totalUpdraftMps))) : alphaCondense;
                        float deltaHpa = (ea - esLifted) * boost;
                        ea -= deltaHpa;
                        qc += deltaHpa * VaporKgPerM2PerHpa;
                    }
                    else if (qc > 0.0f)
                    {
                        float rhAmbient = ea / MathF.Max(1e-4f, esAmbient);
                        if (rhAmbient < 0.85f || wOrographic < -0.04f)
                        {
                            // Kuru havada veya arka yamac Fohn cokmesinde bulut sivi suyu hizla buharlasir -> Yagmur Golgesi!
                            float foehnFactor = wOrographic < -0.04f ? (1.8f + Math.Min(6.0f, -wOrographic * 2.6f)) : 1.0f;
                            float evapFrac = Math.Min(1.0f, alphaCloudEvap * foehnFactor * Math.Max(0.20f, 0.94f - rhAmbient));
                            float maxEvapKgM2 = MathF.Max(0.0f, (esAmbient * 0.92f) - ea) * VaporKgPerM2PerHpa;
                            float evaporatedCloudKgM2 = Math.Min(qc * evapFrac, maxEvapKgM2);
                            qc -= evaporatedCloudKgM2;
                            ea += evaporatedCloudKgM2 / VaporKgPerM2PerHpa;
                        }
                    }

                    ea = ClampFinite(ea, 0.05f, 95.0f);
                    qc = ClampFinite(qc, 0.0f, 6.0f);

                    // Kessler (1969) oto-donusum ile yagmur olusumu (0 RNG! Gercekci MODIS LWP 0.3..6.0 kg/m^2 -> 0.5..24 mm/saat!)
                    float qcCrit = totalUpdraftMps > 0.25f ? 0.28f : 0.48f;
                    float precipMmPerHour = 0.0f;
                    PrecipitationKind activeKind = PrecipitationKind.None;

                    if (qc > qcCrit && wOrographic >= -0.08f)
                    {
                        float updraftEnhance = 1.0f + Math.Clamp(totalUpdraftMps * 0.28f, -0.35f, 1.35f);
                        float rainRatePerSec = 0.00032f * updraftEnhance;
                        float removedFraction = FastOneMinusExpNeg(rainRatePerSec * dt);
                        float rainedOutKgM2 = (qc - qcCrit) * removedFraction;

                        qc = MathF.Max(0.0f, qc - rainedOutKgM2);
                        precipMmPerHour = ClampFinite((rainedOutKgM2 / MathF.Max(0.05f, dt)) * 3600.0f, 0.0f, 85.0f);
                        if (precipMmPerHour >= 0.1f)
                        {
                            activeKind = PrecipitationKind.Rain;
                        }
                        else
                        {
                            precipMmPerHour = 0.0f;
                        }
                    }

                    // LCL Bulut Tabani: z_LCL = h + 125 * max(0.15, T - Td) (esAmbient yeniden kullanilir)
                    float dewPointC = ComputeDewPointFromEs(ea, esAmbient);
                    float lclWorldY = ComputeLclCloudBaseWorldY(h, nextAirTempC, dewPointC);
                    ushort cloudBaseY = (ushort)Math.Clamp((int)MathF.Round(lclWorldY), 0, 65535);

                    // Konvektif CAPE ve ThunderLightning (CAPE > 1000 J/kg)
                    ushort capeJPerKg = EvaluateColumnCapeJPerKg(cur.SurfaceTempCelsius, nextAirTempC, ea, qc);
                    AtmosphericHazardFlags hazards = capeJPerKg > ThunderstormCapeThresholdJPerKg
                        ? AtmosphericHazardFlags.ThunderLightning
                        : AtmosphericHazardFlags.None;

                    ushort cloudTopY = cloudBaseY;
                    if (qc > 0.02f)
                    {
                        int cloudDepthM = Math.Clamp(
                            64 + (int)MathF.Round((qc * 340.0f) + (capeJPerKg * 0.45f)),
                            64,
                            5600);
                        cloudTopY = (ushort)Math.Min(65535, cloudBaseY + cloudDepthM);
                    }

                    // Istasyon basinci guncellemesi
                    float targetSeaLevelP = 1013.25f + _seaLevelPressureAnomHpa[idx];
                    float eqStationP = ComputeHydrostaticStationPressureHpa(targetSeaLevelP, h, nextAirTempC);
                    float nextStationP = ClampFinite(cur.StationPressureHpa + (alphaPressure * (eqStationP - cur.StationPressureHpa)), 700.0f, 1070.0f);

                    _cellsBack[idx] = new AtmosphereCellState
                    {
                        SurfaceTempCelsius = cur.SurfaceTempCelsius,
                        AirTempCelsius = nextAirTempC,
                        StationPressureHpa = nextStationP,
                        VaporPressureHpa = ea,
                        CloudWaterKgPerM2 = qc,
                        SurfacePrecipMmPerHour = precipMmPerHour,
                        WindVelX = u,
                        WindVelZ = v,
                        CloudBaseWorldY = cloudBaseY,
                        CloudTopWorldY = cloudTopY,
                        CapeJPerKg = capeJPerKg,
                        ActiveKind = activeKind,
                        Hazards = hazards
                    };
                }
            }

            (_cellsFront, _cellsBack) = (_cellsBack, _cellsFront);
        }

        private void SampleAdvectedFields(
            float gx,
            float gz,
            out float vaporHpa,
            out float cloudKgM2,
            out float airTempC,
            out float terrainH)
        {
            int x0 = (int)gx;
            int z0 = (int)gz;
            int x1 = Math.Min(x0 + 1, GridSideCells - 1);
            int z1 = Math.Min(z0 + 1, GridSideCells - 1);

            float tx = gx - x0;
            float tz = gz - z0;

            int i00 = (z0 * GridSideCells) + x0;
            int i10 = (z0 * GridSideCells) + x1;
            int i01 = (z1 * GridSideCells) + x0;
            int i11 = (z1 * GridSideCells) + x1;

            float w00 = (1.0f - tx) * (1.0f - tz);
            float w10 = tx * (1.0f - tz);
            float w01 = (1.0f - tx) * tz;
            float w11 = tx * tz;

            ref readonly AtmosphereCellState c00 = ref _cellsFront[i00];
            ref readonly AtmosphereCellState c10 = ref _cellsFront[i10];
            ref readonly AtmosphereCellState c01 = ref _cellsFront[i01];
            ref readonly AtmosphereCellState c11 = ref _cellsFront[i11];

            vaporHpa = (c00.VaporPressureHpa * w00) + (c10.VaporPressureHpa * w10) + (c01.VaporPressureHpa * w01) + (c11.VaporPressureHpa * w11);
            cloudKgM2 = (c00.CloudWaterKgPerM2 * w00) + (c10.CloudWaterKgPerM2 * w10) + (c01.CloudWaterKgPerM2 * w01) + (c11.CloudWaterKgPerM2 * w11);
            airTempC = (c00.AirTempCelsius * w00) + (c10.AirTempCelsius * w10) + (c01.AirTempCelsius * w01) + (c11.AirTempCelsius * w11);
            terrainH = (_terrainHeightMeters[i00] * w00) + (_terrainHeightMeters[i10] * w10) + (_terrainHeightMeters[i01] * w01) + (_terrainHeightMeters[i11] * w11);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeTetensSaturationVaporHpa(float tempCelsius)
        {
            float t = ClampFinite(tempCelsius, -65.0f, 75.0f);
            return 6.1094f * FastExp((17.625f * t) / (243.04f + t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeDewPointCelsius(float tempCelsius, float vaporPressureHpa)
        {
            float es = ComputeTetensSaturationVaporHpa(tempCelsius);
            return ComputeDewPointFromEs(vaporPressureHpa, es);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeDewPointFromEs(float vaporPressureHpa, float saturationEsHpa)
        {
            // Tetens ters bagintisi: gamma = ln(ea / 6.1094) -> tek log/ters sadelestirmesi!
            float eaClamped = Math.Clamp(ClampFinite(vaporPressureHpa, 0.01f, 95.0f), 0.01f, MathF.Max(0.01f, saturationEsHpa));
            float gamma = FastLn(eaClamped * (1.0f / 6.1094f));
            return (243.04f * gamma) / MathF.Max(0.25f, 17.625f - gamma);
        }

        /// <summary>
        /// Kanonik LCL bulut tabani formulu: z_LCL = h + 125 * max(0.15, T - Td).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeLclCloudBaseWorldY(float terrainHeightMeters, float airTempCelsius, float dewPointCelsius)
        {
            float hSafe = MathF.Max(0.0f, ClampFinite(terrainHeightMeters, 0.0f, 8000.0f));
            float spread = MathF.Max(0.15f, airTempCelsius - dewPointCelsius);
            return hSafe + (125.0f * spread);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeHydrostaticStationPressureHpa(float seaLevelPressureHpa, float altitudeMeters, float airTempCelsius)
        {
            float h = ClampFinite(altitudeMeters, 0.0f, 8000.0f);
            if (h <= 0.01f)
            {
                return seaLevelPressureHpa;
            }

            // Hipsometrik barometrik denklem (ISA g*M/R = 0.034163 K/m; Pow yerine hizli ustel, <0.03 hPa fark)
            float meanTempK = MathF.Max(185.0f, ClampFinite(airTempCelsius, -65.0f, 65.0f) + (0.00325f * h) + 273.15f);
            float exponent = (-0.0341632f * h) / meanTempK;
            return seaLevelPressureHpa * FastExp(exponent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FastExp(float x)
        {
            float xc = Math.Clamp(x, -20.0f, 20.0f);
            // 2^(x * log2(e)) IEEE-754 aralik indirgeme + 4. derece minimax polinom (< 2e-6 bagil hata)
            float t = xc * 1.44269504089f;
            float kf = MathF.Floor(t);
            float f = t - kf;
            int ki = (int)kf;
            float poly = 1.0f + (f * (0.69314718f + (f * (0.24022651f + (f * (0.05550411f + (f * 0.00961813f)))))));
            return BitConverter.Int32BitsToSingle((ki + 127) << 23) * poly;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FastOneMinusExpNeg(float x)
        {
            if (x <= 0.0f) return 0.0f;
            if (x < 0.25f)
            {
                return x * (1.0f - (0.5f * x * (1.0f - (0.33333334f * x))));
            }

            return x >= 16.0f ? 1.0f : (1.0f - FastExp(-x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FastLn(float x)
        {
            int bits = BitConverter.SingleToInt32Bits(MathF.Max(1e-12f, x));
            int exp2 = ((bits >> 23) & 0xFF) - 127;
            float m = BitConverter.Int32BitsToSingle((bits & 0x007FFFFF) | 0x3F800000); // [1, 2)
            float y = (m - 1.0f) / (m + 1.0f);
            float y2 = y * y;
            float lnM = 2.0f * y * (1.0f + (y2 * (0.33333334f + (y2 * (0.2f + (y2 * 0.14285715f))))));
            return (exp2 * 0.69314718f) + lnM;
        }

        /// <summary>
        /// Yuzey nemli statik enerjisi ve termal kararsizliktan konvektif potansiyel enerjiyi (CAPE, J/kg) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort EvaluateColumnCapeJPerKg(
            float surfaceTempC,
            float airTempC,
            float vaporPressureHpa,
            float cloudWaterKgPerM2)
        {
            float ts = ClampFinite(surfaceTempC, -65.0f, 70.0f);
            float ta = ClampFinite(airTempC, -65.0f, 65.0f);
            float ea = ClampFinite(vaporPressureHpa, 0.05f, 95.0f);
            float qc = ClampFinite(cloudWaterKgPerM2, 0.0f, 18.0f);

            if (ea < 8.0f || ts < 12.0f)
            {
                return 0;
            }

            float moistStaticExcess = MathF.Max(0.0f, ts + (1.85f * ea) - 46.0f);
            float superAdiabaticLapse = MathF.Max(0.0f, ts - ta - 1.2f);
            float latentCloudBuoyancy = MathF.Max(0.0f, qc - 0.65f);

            float rawCape = (56.0f * moistStaticExcess) + (175.0f * superAdiabaticLapse) + (110.0f * latentCloudBuoyancy);
            return (ushort)Math.Clamp((int)MathF.Round(rawCape), 0, 6000);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EvaluateInternalSolar(
            double totalSimSeconds,
            float latitudeDeg,
            out float declinationDeg,
            out float cosZenith,
            out float clearSkyInsolationWm2)
        {
            double dayExact = Math.Max(0.0, totalSimSeconds) / StratocraftConstants.SecondsPerInGameDay;
            double dayInYear = dayExact % StratocraftConstants.DaysPerYear;
            double dayFraction = dayExact - Math.Floor(dayExact);

            double orbitalRad = (2.0 * Math.PI * (dayInYear - 80.0)) / StratocraftConstants.DaysPerYear;
            declinationDeg = StratocraftConstants.SolarAxialTiltDegrees * (float)Math.Sin(orbitalRad);
            float declRad = declinationDeg * DegToRad;

            float latRad = Math.Clamp(float.IsFinite(latitudeDeg) ? latitudeDeg : 38.0f, -89.5f, 89.5f) * DegToRad;
            float hourAngleRad = (float)((dayFraction * 24.0) - 12.0) * 15.0f * DegToRad;

            cosZenith = Math.Clamp(
                (MathF.Sin(latRad) * MathF.Sin(declRad)) + (MathF.Cos(latRad) * MathF.Cos(declRad) * MathF.Cos(hourAngleRad)),
                -1.0f,
                1.0f);
            clearSkyInsolationWm2 = cosZenith > 0.0f ? StratocraftConstants.SolarConstantWm2 * cosZenith : 0.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClampWindVector(ref float u, ref float v, float maxSpeedMps)
        {
            if (!float.IsFinite(u)) u = 0.0f;
            if (!float.IsFinite(v)) v = 0.0f;
            float speedSq = (u * u) + (v * v);
            float maxSq = maxSpeedMps * maxSpeedMps;
            if (speedSq > maxSq)
            {
                float scale = maxSpeedMps / MathF.Sqrt(speedSq);
                u *= scale;
                v *= scale;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ClampFinite(float value, float min, float max)
        {
            if (!float.IsFinite(value))
            {
                return 0.5f * (min + max);
            }

            return Math.Clamp(value, min, max);
        }
    }
}
