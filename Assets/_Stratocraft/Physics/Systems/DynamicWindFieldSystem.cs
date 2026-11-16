#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Weather;
using Stratocraft.Physics.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Belirli bir 3B dunya noktasindaki (x, y, z) engel-filtrelenmis, irtifa-olcekli ve
    /// bora hamlesi eklenmis yerel ruzgar ornegi (28 Byte deger tipi, 0 B GC).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct LocalWindSample3D
    {
        public Vector3 VelocityMps;          // Etkin 3B ruzgar vektoru (m/s)
        public float SpeedMps;               // |VelocityMps|
        public float UpwindExposure01;       // S_wind in [0.0 .. 1.0] (0 = tam duvar/magara siperi, 1 = acik ufuk)
        public float AltitudeFactor;         // Hellmann (dz / 10m)^alpha carpani [0.35 .. 1.85]
        public float InstantGustBonusMps;    // grad(P) + CAPE kaynakli anlik bora hamlesi katkisi (m/s)
    }

    public delegate bool VoxelSolidProbeDelegate(int worldX, int worldY, int worldZ);

    /// <summary>
    /// Paket 4B Faz 4B.4 (IP-4 &amp; IP-6): Bolgesel 2B atmosfer ruzgarini yerel arazi irtifasi (Hellmann yasasi),
    /// 5-isinli voksel engel golgesi (Jensen iz sonumlemesi), barometrik/CAPE bora hamleleri,
    /// comelme duyarli oyuncu suruklemesi, mermi balistik sapmasi ve derinlik-sinirli Beaufort dalga olcegiyle
    /// gercek 3B fiziksel ruzgar alanina donusturen sifir-GC sistem.
    /// </summary>
    public sealed class DynamicWindFieldSystem
    {
        public const float ReferenceHeightMeters = 10.0f;
        public const float HellmannAlphaSea = 0.11f;
        public const float HellmannAlphaOpenLand = 0.16f;
        public const float HellmannAlphaRoughTerrain = 0.26f;

        public const int UpwindRayMaxSteps = 16;            // 16.0m ruzgar-ustu engel tarama menzili
        public const float PlayerDragStartSpeedMps = 12.0f; // Beaufort 6 esigi
        public const float StandingAreaM2 = 0.72f;
        public const float CrouchingAreaM2 = 0.32f;
        public const float HumanDragCoeffCd = 1.05f;

        public const float LightArrowMassKg = 0.045f;
        public const float HeavyJavelinMassKg = 1.80f;
        public const float DefaultArrowDragAreaTimesCdM2 = 0.0012f;
        public const float DefaultJavelinDragAreaTimesCdM2 = 0.0016f;
        public const float SpecificGasConstantDryAir = 287.058f;

        /// <summary>
        /// Hellmann ustel ruzgar profili yasasi: (dz / 10m)^alpha [0.35 .. 1.85].
        /// dz = 25m irtifadaki hiz carpani dz = 2m hiz carpaninin &gt;= 1.30x katidir (AK-6).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateHellmannAltitudeFactor(
            float heightAboveGroundMeters,
            float roughnessAlpha = HellmannAlphaOpenLand)
        {
            float dz = Math.Clamp(
                float.IsFinite(heightAboveGroundMeters) ? heightAboveGroundMeters : ReferenceHeightMeters,
                0.5f,
                250.0f);
            float alpha = Math.Clamp(float.IsFinite(roughnessAlpha) ? roughnessAlpha : HellmannAlphaOpenLand, 0.08f, 0.35f);
            float factor = MathF.Pow(dz / ReferenceHeightMeters, alpha);
            return Math.Clamp(factor, 0.35f, 1.85f);
        }

        /// <summary>
        /// Ruzgarin geldigi yone (-windDirXZ) dogru 5 deterministik voksel isini (merkez, +-18° yatay, +15° ve +35° egimli)
        /// atarak duvar, kaya ve tepe siperi carpanini (S_wind in [0..1]) hesaplar. Kapali odada 0.0 doner.
        /// </summary>
        public static float ComputeUpwindVoxelExposure5Ray(
            Vector3 worldPos,
            Vector2 windDirNormalizedXZ,
            bool isEnclosedIndoorRoom,
            VoxelSolidProbeDelegate? isSolidAt)
        {
            if (isEnclosedIndoorRoom)
            {
                return 0.0f;
            }

            if (isSolidAt == null || windDirNormalizedXZ.LengthSquared() < 1e-4f)
            {
                return 1.0f;
            }

            Vector2 upX = -Vector2.Normalize(windDirNormalizedXZ);
            const float cos18 = 0.9510565f;
            const float sin18 = 0.3090170f;

            Span<Vector3> rayDirs = stackalloc Vector3[5];
            rayDirs[0] = new Vector3(upX.X, 0.04f, upX.Y);                                                   // Merkez yatay
            rayDirs[1] = new Vector3(upX.X * cos18 - upX.Y * sin18, 0.04f, upX.X * sin18 + upX.Y * cos18);  // -18° sol yelpaze
            rayDirs[2] = new Vector3(upX.X * cos18 + upX.Y * sin18, 0.04f, -upX.X * sin18 + upX.Y * cos18); // +18° sag yelpaze
            rayDirs[3] = Vector3.Normalize(new Vector3(upX.X, 0.268f, upX.Y));                              // +15° alcak sirt
            rayDirs[4] = Vector3.Normalize(new Vector3(upX.X, 0.700f, upX.Y));                              // +35° yuksek duvar/yamac

            ReadOnlySpan<float> weights = stackalloc float[5] { 0.32f, 0.20f, 0.20f, 0.16f, 0.12f };
            float exposureSum = 0.0f;

            for (int r = 0; r < 5; r++)
            {
                Vector3 dir = rayDirs[r];
                float rayFactor = 1.0f;
                for (int step = 1; step <= UpwindRayMaxSteps; step++)
                {
                    Vector3 p = worldPos + (dir * step);
                    int vx = (int)MathF.Floor(p.X);
                    int vy = (int)MathF.Floor(p.Y);
                    int vz = (int)MathF.Floor(p.Z);

                    if (isSolidAt(vx, vy, vz))
                    {
                        // Engelin toplam dikey yuksekligini (yukari ve tabana dogru) olc -> Jensen iz orani d / H
                        int wallTopExtra = 1;
                        while (wallTopExtra < 4 && isSolidAt(vx, vy + wallTopExtra, vz))
                        {
                            wallTopExtra++;
                        }

                        int wallBottomExtra = 0;
                        while (wallBottomExtra < 3 && isSolidAt(vx, vy - 1 - wallBottomExtra, vz))
                        {
                            wallBottomExtra++;
                        }

                        int totalWallHeight = Math.Clamp(wallTopExtra + wallBottomExtra, 1, 6);
                        float ratio = step / MathF.Max(1.0f, totalWallHeight);
                        float t = Math.Clamp((ratio - 1.5f) / 8.5f, 0.0f, 1.0f);
                        float smooth = t * t * (3.0f - (2.0f * t));
                        rayFactor = 0.06f + (0.94f * smooth);
                        break;
                    }
                }

                exposureSum += rayFactor * weights[r];
            }

            return Math.Clamp(exposureSum, 0.0f, 1.0f);
        }

        /// <summary>
        /// Basinc gradyani |grad(P)| (hPa/km) ve konvektif CAPE + yagis asagi-akimindan deterministik bora hamlesi uretir (0 RNG).
        /// </summary>
        public static float ComputeBarometricAndCapeGustMps(
            in AtmosphereCellState cell,
            float pressureGradientHpaPerKm,
            double worldTimeSeconds,
            float worldX,
            float worldZ)
        {
            float safeGrad = float.IsFinite(pressureGradientHpaPerKm) ? MathF.Max(0.0f, pressureGradientHpaPerKm) : 0.0f;
            float gradGust = Math.Clamp(safeGrad * 1.80f, 0.0f, 12.0f);

            float capeGust = 0.0f;
            if (cell.CapeJPerKg > 600)
            {
                float capeNorm = Math.Clamp((cell.CapeJPerKg - 600.0f) / 1800.0f, 0.0f, 1.0f);
                float precipFactor = cell.SurfacePrecipMmPerHour > 0.01f
                    ? Math.Clamp(0.45f + ((cell.SurfacePrecipMmPerHour / 24.0f) * 0.55f), 0.45f, 1.0f)
                    : 0.70f;
                capeGust = 9.5f * capeNorm * precipFactor;
            }

            if (gradGust <= 1e-5f && capeGust <= 1e-5f)
            {
                return 0.0f;
            }

            float spatialPhase = (worldX * 0.031f) + (worldZ * 0.027f);
            float tSec = double.IsFinite(worldTimeSeconds) ? (float)(worldTimeSeconds % 86400.0) : 0.0f;
            float w1 = MathF.Cos((float)(tSec * (2.0 * Math.PI * 0.17)) + spatialPhase);
            float w2 = MathF.Cos((float)(tSec * (2.0 * Math.PI * 0.43)) - (spatialPhase * 1.7f));
            float envelope01 = Math.Clamp(0.72f + (0.18f * w1) + (0.10f * w2), 0.52f, 1.0f);
            float pulse = envelope01 * envelope01;

            return (gradGust + capeGust) * pulse;
        }

        public static LocalWindSample3D EvaluateLocal3DWind(
            RegionalAtmosphereSimulationSystem atmosphere,
            Vector3 worldPos,
            float terrainGroundY,
            double worldTimeSeconds,
            bool isEnclosedIndoorRoom,
            VoxelSolidProbeDelegate? isSolidAt)
        {
            if (atmosphere == null)
            {
                return default;
            }

            AtmosphereCellState cell = atmosphere.SampleBilinearAtWorldXZ(worldPos.X, worldPos.Z);
            atmosphere.WorldToCellCoords(worldPos.X, worldPos.Z, out int cx, out int cz);
            float pWest = atmosphere.GetCell(cx - 1, cz).StationPressureHpa;
            float pEast = atmosphere.GetCell(cx + 1, cz).StationPressureHpa;
            float pSouth = atmosphere.GetCell(cx, cz - 1).StationPressureHpa;
            float pNorth = atmosphere.GetCell(cx, cz + 1).StationPressureHpa;
            float dPdx = (pEast - pWest) / (2.0f * RegionalAtmosphereSimulationSystem.CellSizeMeters * 0.001f);
            float dPdz = (pNorth - pSouth) / (2.0f * RegionalAtmosphereSimulationSystem.CellSizeMeters * 0.001f);
            float gradHpaPerKm = MathF.Sqrt((dPdx * dPdx) + (dPdz * dPdz));

            float seaFrac = atmosphere.GetCellSeaFraction(cx, cz);
            float alpha = seaFrac > 0.5f ? HellmannAlphaSea : HellmannAlphaOpenLand;
            float altFactor = EvaluateHellmannAltitudeFactor(worldPos.Y - terrainGroundY, alpha);

            Vector2 u10 = new Vector2(cell.WindVelX, cell.WindVelZ);
            float baseSpeed = u10.Length();
            Vector2 dirXZ = baseSpeed > 1e-3f ? (u10 / baseSpeed) : new Vector2(1.0f, 0.0f);

            float exposure = ComputeUpwindVoxelExposure5Ray(worldPos, dirXZ, isEnclosedIndoorRoom, isSolidAt);
            float gustBonus = ComputeBarometricAndCapeGustMps(in cell, gradHpaPerKm, worldTimeSeconds, worldPos.X, worldPos.Z);

            float effectiveSpeed = exposure * ((baseSpeed * altFactor) + gustBonus);
            Vector3 vel3D = new Vector3(dirXZ.X * effectiveSpeed, 0.0f, dirXZ.Y * effectiveSpeed);

            return new LocalWindSample3D
            {
                VelocityMps = vel3D,
                SpeedMps = effectiveSpeed,
                UpwindExposure01 = exposure,
                AltitudeFactor = altFactor,
                InstantGustBonusMps = gustBonus * exposure
            };
        }

        /// <summary>
        /// IP-6: Comelme ([Sol Ctrl] / KeyCode.LeftControl) ve duvar arkasi ruzgar siperi (S_wind)
        /// karsi-oynanisina sahip aerodinamik oyuncu suruklemesi (Newton).
        /// Comelince kesit kuculur ve tutunma artar -&gt; &lt;= 0.32x (&gt;%68 azalma, AK-10).
        /// </summary>
        public static Vector3 ComputePlayerAerodynamicDragForce(
            in LocalWindSample3D localWind,
            bool isCrouching,
            float airDensityKgM3 = 1.225f)
        {
            if (localWind.SpeedMps <= PlayerDragStartSpeedMps || localWind.UpwindExposure01 <= 0.02f)
            {
                return Vector3.Zero;
            }

            float excessSpeed = localWind.SpeedMps - PlayerDragStartSpeedMps;
            float area = isCrouching ? CrouchingAreaM2 : StandingAreaM2;
            float crouchGripReduction = isCrouching ? 0.58f : 1.0f;
            float safeDensity = float.IsFinite(airDensityKgM3) ? Math.Clamp(airDensityKgM3, 0.5f, 2.0f) : 1.225f;
            float forceMag = 0.5f * safeDensity * HumanDragCoeffCd * area * crouchGripReduction * (excessSpeed * excessSpeed);
            forceMag = Math.Clamp(forceMag, 0.0f, 260.0f);

            Vector3 dir = localWind.SpeedMps > 1e-4f ? (localWind.VelocityMps / localWind.SpeedMps) : Vector3.Zero;
            return dir * forceMag;
        }

        /// <summary>
        /// IP-4 (AK-7): Ok (0.045 kg), mizrak (1.8 kg) ve firlatilan cisimler icin kutleye ve kesit alanina duyarli
        /// balistik ruzgar sapmasi. Kapali galeride (UpwindExposure01 == 0 veya SpeedMps == 0) sapma 0.0f olur.
        /// </summary>
        public static Vector3 StepProjectileWindDeflectionVelocity(
            Vector3 currentProjectileVelocity,
            in LocalWindSample3D localWind,
            float projectileMassKg,
            float dragAreaTimesCdM2,
            float dtSeconds,
            float airDensityKgM3 = 1.225f)
        {
            if (dtSeconds <= 0.0f || localWind.SpeedMps <= 1e-4f || localWind.UpwindExposure01 <= 1e-4f)
            {
                return currentProjectileVelocity;
            }

            float safeMass = MathF.Max(0.035f, float.IsFinite(projectileMassKg) ? projectileMassKg : LightArrowMassKg);
            float defaultCdArea = safeMass < 0.25f ? DefaultArrowDragAreaTimesCdM2 : DefaultJavelinDragAreaTimesCdM2;
            float minCdArea = safeMass < 0.25f ? 0.00095f : 0.00080f;
            float effectiveCdArea = dragAreaTimesCdM2 > 1e-6f && float.IsFinite(dragAreaTimesCdM2)
                ? Math.Clamp(dragAreaTimesCdM2, minCdArea, 0.0060f)
                : defaultCdArea;

            Vector3 vRel = currentProjectileVelocity - localWind.VelocityMps;
            float relSpeed = vRel.Length();
            if (relSpeed <= 1e-4f)
            {
                return currentProjectileVelocity;
            }

            Vector3 windDir = localWind.VelocityMps / localWind.SpeedMps;
            float vAlongWind = Vector3.Dot(currentProjectileVelocity, windDir);
            float windDiff = localWind.SpeedMps - vAlongWind;
            if (MathF.Abs(windDiff) <= 1e-4f)
            {
                return currentProjectileVelocity;
            }

            float safeDensity = float.IsFinite(airDensityKgM3) ? Math.Clamp(airDensityKgM3, 0.5f, 2.0f) : 1.225f;
            float accelMag = (0.5f * safeDensity * effectiveCdArea * relSpeed * windDiff) / safeMass;
            Vector3 nextVel = currentProjectileVelocity + (windDir * (accelMag * dtSeconds));
            return nextVel;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 StepProjectileWindDeflectionVelocity(
            Vector3 currentProjectileVelocity,
            in LocalWindSample3D localWind,
            float projectileMassKg,
            float dtSeconds)
        {
            float cdArea = projectileMassKg < 0.25f ? DefaultArrowDragAreaTimesCdM2 : DefaultJavelinDragAreaTimesCdM2;
            return StepProjectileWindDeflectionVelocity(
                currentProjectileVelocity,
                in localWind,
                projectileMassKg,
                cdArea,
                dtSeconds);
        }

        /// <summary>
        /// Belirli bir ucus mesafesi boyunca (orn. 30m) yan ruzgardaki yanal sapma miktarini (metre) simule eder (0 B GC).
        /// </summary>
        public static float SimulateProjectileCrosswindDeflectionMeters(
            float flightDistanceMeters,
            float initialForwardSpeedMps,
            in LocalWindSample3D localWind,
            float projectileMassKg,
            float dragAreaTimesCdM2 = 0.0f,
            float dtSeconds = 0.01f,
            float airDensityKgM3 = 1.225f)
        {
            if (flightDistanceMeters <= 0.0f || initialForwardSpeedMps <= 0.1f ||
                localWind.SpeedMps <= 1e-4f || localWind.UpwindExposure01 <= 1e-4f)
            {
                return 0.0f;
            }

            float dt = Math.Clamp(dtSeconds, 0.001f, 0.05f);
            Vector3 pos = Vector3.Zero;
            Vector3 vel = new Vector3(initialForwardSpeedMps, 0.0f, 0.0f);
            int maxSteps = (int)MathF.Ceiling((flightDistanceMeters / initialForwardSpeedMps) / dt) + 32;

            for (int step = 0; step < maxSteps && pos.X < flightDistanceMeters; step++)
            {
                vel = StepProjectileWindDeflectionVelocity(
                    vel,
                    in localWind,
                    projectileMassKg,
                    dragAreaTimesCdM2,
                    dt,
                    airDensityKgM3);
                pos += vel * dt;
            }

            return MathF.Sqrt((pos.Y * pos.Y) + (pos.Z * pos.Z));
        }

        /// <summary>
        /// Ideal gaz yasasindan (P / (R * T)) kuru hava yogunlugunu (kg/m^3) hesaplar.
        /// Soguk kis havasinda yogunluk artar (~1.34 kg/m^3).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeAirDensityKgM3(float airTempCelsius, float stationPressureHpa = 1013.25f)
        {
            float tempK = Math.Clamp(airTempCelsius + 273.15f, 200.0f, 340.0f);
            float pressurePa = Math.Clamp(stationPressureHpa * 100.0f, 50000.0f, 108000.0f);
            return pressurePa / (SpecificGasConstantDryAir * tempK);
        }

        /// <summary>
        /// WMO standart 0..12 Beaufort ruzgar olcegi.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EvaluateBeaufortScale0To12(float windSpeedMps)
        {
            float v = float.IsFinite(windSpeedMps) ? MathF.Max(0.0f, windSpeedMps) : 0.0f;
            if (v < 0.5f) return 0;
            if (v < 1.6f) return 1;
            if (v < 3.4f) return 2;
            if (v < 5.5f) return 3;
            if (v < 8.0f) return 4;
            if (v < 10.8f) return 5;
            if (v < 13.9f) return 6;
            if (v < 17.2f) return 7;
            if (v < 20.8f) return 8;
            if (v < 24.5f) return 9;
            if (v < 28.5f) return 10;
            if (v < 32.7f) return 11;
            return 12;
        }

        /// <summary>
        /// IP-6 (DOK3 Bolum 3.2.3): WMO Beaufort 0..12 ve Young-Verhagen sig su derinlik sinirlamali
        /// Gerstner dalga genligini (metre) hesaplar. Sig birikintide veya kapali magarada tasma yapmaz (&lt;= 0.45 * depth).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateDepthLimitedBeaufortWaveAmplitude(
            float windSpeedMps,
            float fluidDepthMeters,
            float upwindExposure01 = 1.0f)
        {
            if (fluidDepthMeters <= 0.04f || upwindExposure01 <= 0.01f)
            {
                return 0.0f;
            }

            float v = MathF.Max(0.0f, float.IsFinite(windSpeedMps) ? windSpeedMps : 0.0f)
                    * Math.Clamp(upwindExposure01, 0.0f, 1.0f);
            float tDepth = Math.Clamp((fluidDepthMeters - 0.08f) / 2.42f, 0.0f, 1.0f);
            float depthSmooth = tDepth * tDepth * (3.0f - (2.0f * tDepth));

            float openWaterAmp = 0.008f + (0.00085f * v * v);
            openWaterAmp = Math.Clamp(openWaterAmp, 0.005f, 0.60f);

            float rawAmp = openWaterAmp * depthSmooth;
            return MathF.Min(0.45f * fluidDepthMeters, rawAmp);
        }

        /// <summary>
        /// WMO Beaufort 0..12 dalga dikligi (Gerstner Q_steep in [0.08 .. 0.72]).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateBeaufortWaveSteepness(
            float windSpeedMps,
            float fluidDepthMeters,
            float upwindExposure01 = 1.0f)
        {
            if (fluidDepthMeters <= 0.08f || upwindExposure01 <= 0.01f)
            {
                return 0.0f;
            }

            float v = MathF.Max(0.0f, float.IsFinite(windSpeedMps) ? windSpeedMps : 0.0f)
                    * Math.Clamp(upwindExposure01, 0.0f, 1.0f);
            float u = Math.Clamp(v / 26.0f, 0.0f, 1.0f);
            float depthMask = Math.Clamp((fluidDepthMeters - 0.08f) / 1.50f, 0.0f, 1.0f);
            return (0.08f + (0.64f * u)) * depthMask;
        }
    }
}
