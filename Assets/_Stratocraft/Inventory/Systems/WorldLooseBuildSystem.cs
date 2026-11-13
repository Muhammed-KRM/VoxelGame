#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Inventory.Data;
#if !UNITY_5_3_OR_NEWER
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.WorldGen;
#endif
using SysVec3 = System.Numerics.Vector3;
using SysQuat = System.Numerics.Quaternion;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Faz 2.3 (Asama 3.1): Saf C# yerlestirme, dizde dal kirma / cadir (teepee) egimi ve tas/kil ust uste yigma cozucusu.
    /// Hem bagimsiz .NET 10 testlerinde hem de Unity 6 sahnesinde sifir GC ve sifir ust-katman bagimliligiyla calisir.
    /// </summary>
    public static class WorldLoosePlacementMath
    {
        public const float TeepeeSnapRadiusMeters = 0.58f;
        public const float TeepeeTiltAngleDegrees = 42.0f;

        public static float GetPartApproximateHeight(LooseBuildPartKind kind) => kind switch
        {
            LooseBuildPartKind.LeafPile => 0.055f,
            LooseBuildPartKind.PlantFiber => 0.055f,
            LooseBuildPartKind.Stick => 0.085f,
            LooseBuildPartKind.SplitFirewood => 0.145f,
            LooseBuildPartKind.RoughStoneChunk => 0.175f,
            LooseBuildPartKind.LooseDirtClod => 0.155f,
            LooseBuildPartKind.ClayLump => 0.155f,
            LooseBuildPartKind.SandPile => 0.125f,
            LooseBuildPartKind.WoodenWheel => 0.340f,
            LooseBuildPartKind.ClayTuyerePipe => 0.120f,
            LooseBuildPartKind.ClayCruciblePot => 0.220f,
            LooseBuildPartKind.LeatherBellows => 0.180f,
            _ => 0.130f
        };

        public static float GetPartHorizontalFootprintRadius(LooseBuildPartKind kind) => kind switch
        {
            LooseBuildPartKind.LeafPile => 0.20f,
            LooseBuildPartKind.PlantFiber => 0.16f,
            LooseBuildPartKind.Stick => 0.14f,
            LooseBuildPartKind.SplitFirewood => 0.17f,
            LooseBuildPartKind.RoughStoneChunk => 0.18f,
            LooseBuildPartKind.LooseDirtClod => 0.18f,
            LooseBuildPartKind.ClayLump => 0.17f,
            LooseBuildPartKind.SandPile => 0.20f,
            LooseBuildPartKind.WoodenWheel => 0.24f,
            _ => 0.16f
        };

        /// <summary>
        /// Hedef zemin noktasi ve etraftaki mevcut serbest parcalara gore yeni parcanin:
        /// - Konumunu (ust uste binme / yigma cozumu dahil),
        /// - Donusunu (yuzey normaline uyum veya Sons of the Forest konik cadir egimi),
        /// - Guncellenmis ShapeDna bayraklarini (FlagBrokenStick) hesaplar.
        /// </summary>
        public static void ComputePlacementPose(
            LooseBuildPartKind kind,
            in PhysicalItemShapeDna inputDna,
            SysVec3 surfacePoint,
            SysVec3 surfaceNormal,
            float userYawDegrees,
            ReadOnlySpan<LoosePartPoint> existingParts,
            out SysVec3 finalPosition,
            out SysQuat finalRotation,
            out PhysicalItemShapeDna finalDna,
            out bool snappedStickIntoTeepee,
            out bool stackedOnExistingPart)
        {
            finalDna = inputDna;
            snappedStickIntoTeepee = false;
            stackedOnExistingPart = false;

            SysVec3 n = surfaceNormal.LengthSquared() > 0.0001f
                ? SysVec3.Normalize(surfaceNormal)
                : SysVec3.UnitY;

            // 1) Kuru Dal (Stick) yerlestirilirken 0.58m cevrede Kav (Yaprak/Lif) veya baska Dal varsa
            //    Sons of the Forest tarzi dizde kirip (FlagBrokenStick) merkeze dogru 42 derece cadir egimi ver!
            if (kind == LooseBuildPartKind.Stick)
            {
                SysVec3 clusterSum = SysVec3.Zero;
                float clusterMinY = surfacePoint.Y;
                int tinderOrStickCount = 0;
                int existingStickCount = 0;

                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint p = ref existingParts[i];
                    if (p.BoundStationId != 0u) continue;
                    float dx = p.Position.X - surfacePoint.X;
                    float dz = p.Position.Z - surfacePoint.Z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq > TeepeeSnapRadiusMeters * TeepeeSnapRadiusMeters) continue;
                    if (MathF.Abs(p.Position.Y - surfacePoint.Y) > 0.65f) continue;

                    if (p.Kind == LooseBuildPartKind.LeafPile ||
                        p.Kind == LooseBuildPartKind.PlantFiber ||
                        p.Kind == LooseBuildPartKind.Stick)
                    {
                        if (p.Kind == LooseBuildPartKind.LeafPile || p.Kind == LooseBuildPartKind.PlantFiber)
                        {
                            clusterSum += p.Position * 2.0f;
                            tinderOrStickCount += 2;
                        }
                        else
                        {
                            clusterSum += p.Position;
                            tinderOrStickCount += 1;
                            existingStickCount++;
                        }
                        if (p.Position.Y < clusterMinY) clusterMinY = p.Position.Y;
                    }
                }

                if (tinderOrStickCount > 0)
                {
                    snappedStickIntoTeepee = true;
                    finalDna = inputDna.WithFlags((byte)(inputDna.Flags | PhysicalItemShapeDna.FlagBrokenStick));

                    SysVec3 center = clusterSum / tinderOrStickCount;
                    SysVec3 fromCenter = new SysVec3(surfacePoint.X - center.X, 0f, surfacePoint.Z - center.Z);
                    float horizLen = fromCenter.Length();

                    float radialAngleRad;
                    if (horizLen < 0.08f)
                    {
                        float deg = existingStickCount * 112.5f + userYawDegrees;
                        radialAngleRad = deg * (MathF.PI / 180f);
                    }
                    else
                    {
                        radialAngleRad = MathF.Atan2(fromCenter.X, fromCenter.Z) + (userYawDegrees * 0.15f) * (MathF.PI / 180f);
                    }

                    SysVec3 outwardDir = new SysVec3(MathF.Sin(radialAngleRad), 0f, MathF.Cos(radialAngleRad));
                    SysVec3 inwardDir = -outwardDir;

                    float tiltRad = TeepeeTiltAngleDegrees * (MathF.PI / 180f);
                    SysVec3 stickForward = SysVec3.Normalize(inwardDir * MathF.Cos(tiltRad) + SysVec3.UnitY * MathF.Sin(tiltRad));
                    finalPosition = new SysVec3(
                        center.X + outwardDir.X * 0.135f,
                        clusterMinY + 0.145f,
                        center.Z + outwardDir.Z * 0.135f);
                    finalRotation = LookRotationSafe(stickForward, SysVec3.UnitY);
                    return;
                }
            }

            // 2) Tas, Kil, Toprak, Odun vb. parcalarin mevcut parcalarin uzerine istiflenmesi (baca / ocak orme)
            float partHalfH = GetPartApproximateHeight(kind) * 0.5f;
            float footprintR = GetPartHorizontalFootprintRadius(kind);
            float resolvedY = surfacePoint.Y + partHalfH * 0.55f;
            SysVec3 resolvedXZ = new SysVec3(surfacePoint.X, 0f, surfacePoint.Z);

            for (int i = 0; i < existingParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref existingParts[i];
                float otherR = GetPartHorizontalFootprintRadius(p.Kind);
                float otherH = GetPartApproximateHeight(p.Kind);
                float dx = resolvedXZ.X - p.Position.X;
                float dz = resolvedXZ.Z - p.Position.Z;
                float horizDist = MathF.Sqrt(dx * dx + dz * dz);

                if (horizDist < (otherR * 0.82f) &&
                    kind != LooseBuildPartKind.LeafPile &&
                    p.Kind != LooseBuildPartKind.LeafPile &&
                    p.Kind != LooseBuildPartKind.PlantFiber)
                {
                    float candidateTopY = p.Position.Y + otherH * 0.76f;
                    if (candidateTopY > resolvedY && candidateTopY <= surfacePoint.Y + 1.55f)
                    {
                        resolvedY = candidateTopY;
                        stackedOnExistingPart = true;
                    }
                }
                else if (!stackedOnExistingPart &&
                         horizDist > 0.001f &&
                         horizDist < (footprintR + otherR) * 0.62f &&
                         MathF.Abs(resolvedY - p.Position.Y) < otherH * 0.65f &&
                         p.Kind != LooseBuildPartKind.LeafPile &&
                         p.Kind != LooseBuildPartKind.PlantFiber)
                {
                    float push = (footprintR + otherR) * 0.62f - horizDist;
                    resolvedXZ.X += (dx / horizDist) * push;
                    resolvedXZ.Z += (dz / horizDist) * push;
                }
            }

            finalPosition = new SysVec3(resolvedXZ.X, resolvedY, resolvedXZ.Z);

            float yawRad = userYawDegrees * (MathF.PI / 180f);
            SysVec3 horizFwd = new SysVec3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));
            SysVec3 tangentFwd = horizFwd - n * SysVec3.Dot(horizFwd, n);
            if (tangentFwd.LengthSquared() < 0.0001f)
            {
                tangentFwd = SysVec3.Cross(n, SysVec3.UnitX);
            }
            tangentFwd = SysVec3.Normalize(tangentFwd);
            finalRotation = LookRotationSafe(tangentFwd, n);
        }

        public static SysQuat LookRotationSafe(SysVec3 forward, SysVec3 up)
        {
            SysVec3 z = forward.LengthSquared() > 1e-6f ? SysVec3.Normalize(forward) : SysVec3.UnitZ;
            SysVec3 x = SysVec3.Cross(up, z);
            if (x.LengthSquared() < 1e-6f)
            {
                SysVec3 fallbackUp = MathF.Abs(z.Y) < 0.9f ? SysVec3.UnitY : SysVec3.UnitX;
                x = SysVec3.Cross(fallbackUp, z);
            }
            x = SysVec3.Normalize(x);
            SysVec3 y = SysVec3.Cross(z, x);

            float m00 = x.X, m01 = x.Y, m02 = x.Z;
            float m10 = y.X, m11 = y.Y, m12 = y.Z;
            float m20 = z.X, m21 = z.Y, m22 = z.Z;

            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1.0f) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    (m12 - m21) / s,
                    (m20 - m02) / s,
                    (m01 - m10) / s,
                    0.25f * s));
            }
            if (m00 > m11 && m00 > m22)
            {
                float s = MathF.Sqrt(1.0f + m00 - m11 - m22) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    0.25f * s,
                    (m01 + m10) / s,
                    (m20 + m02) / s,
                    (m12 - m21) / s));
            }
            if (m11 > m22)
            {
                float s = MathF.Sqrt(1.0f + m11 - m00 - m22) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    (m01 + m10) / s,
                    0.25f * s,
                    (m12 + m21) / s,
                    (m20 - m02) / s));
            }
            else
            {
                float s = MathF.Sqrt(1.0f + m22 - m00 - m11) * 2f;
                return SysQuat.Normalize(new SysQuat(
                    (m20 + m02) / s,
                    (m12 + m21) / s,
                    0.25f * s,
                    (m01 - m10) / s));
            }
        }

        /// <summary>
        /// Paket 4A: Verilen zanaat ciktisinin 5 meteoroloji aletinden biri veya Metal Paratoner olup olmadigini doner.
        /// </summary>
        public static bool IsWeatherInstrumentOrLightningRod(PrimitiveCraftOutputId outputKind) => outputKind switch
        {
            PrimitiveCraftOutputId.WeatherBarometer => true,
            PrimitiveCraftOutputId.WeatherThermometer => true,
            PrimitiveCraftOutputId.WeatherHygrometer => true,
            PrimitiveCraftOutputId.WeatherWindVaneAnemometer => true,
            PrimitiveCraftOutputId.WeatherRainGauge => true,
            PrimitiveCraftOutputId.MetalLightningRod => true,
            _ => false
        };

        public static float GetWeatherStructureHeightMeters(PrimitiveCraftOutputId outputKind) => outputKind switch
        {
            PrimitiveCraftOutputId.WeatherBarometer => 0.36f,
            PrimitiveCraftOutputId.WeatherThermometer => 0.44f,
            PrimitiveCraftOutputId.WeatherHygrometer => 0.44f,
            PrimitiveCraftOutputId.WeatherWindVaneAnemometer => 1.35f,
            PrimitiveCraftOutputId.WeatherRainGauge => 0.52f,
            PrimitiveCraftOutputId.MetalLightningRod => 1.95f,
            _ => 0.40f
        };

        public static float GetWeatherStructureFootprintRadiusMeters(PrimitiveCraftOutputId outputKind) => outputKind switch
        {
            PrimitiveCraftOutputId.WeatherWindVaneAnemometer => 0.28f,
            PrimitiveCraftOutputId.WeatherRainGauge => 0.24f,
            PrimitiveCraftOutputId.MetalLightningRod => 0.26f,
            _ => 0.20f
        };
    }

    /// <summary>
    /// Paket 4A: Dunyaya yerlestirilmis tek bir meteoroloji aleti veya Metal Paratonerin canli durumu.
    /// Barometre 3 saatlik basinc gecmisi, yagmur olcer birikimi, termometre min/maks ve ruzgar gulu/kepce acilarini saklar.
    /// </summary>
    public struct PlacedWeatherInstrumentRecord
    {
        public uint InstanceId;
        public PrimitiveCraftOutputId OutputKind;
        public SysVec3 Position;
        public SysQuat Rotation;
        public PhysicalItemShapeDna ShapeDna;
        public bool HasOpenSkyAbove;

        // Barometre durumu (hPa + 3 saatlik dP/dt gecmis halkasi + Zambretti harfi/etiketi)
        public float StationPressureHpa;
        public float SeaLevelPressureHpa;
        public float ThreeHourDeltaHpa;
        public float P0Hpa;
        public float P1Hpa;
        public float P2Hpa;
        public float P3Hpa;
        public double LastPressureSampleGameSec;
        public char LastZambrettiLetter;
        public byte LastZambrettiIndex1To26;
        public string LastZambrettiForecastLabel;
        public bool FitzRoyStormWarning;

        // Termometre durumu (°C + gunluk min/maks)
        public float CurrentTempCelsius;
        public float DailyMinTempCelsius;
        public float DailyMaxTempCelsius;
        public int LastRecordedDayIndex;

        // Higrometre durumu (%RH + Cig Noktasi Td + LCL Bulut Tabani)
        public float RelativeHumidity01;
        public float DewPointCelsius;
        public float EstimatedCloudBaseWorldY;
        public float FogRisk01;

        // Ruzgar Gulu + Anemometre durumu (m/s, yon, Beaufort 0..12, canli ibre/kepce acilari)
        public float WindSpeedMps;
        public float WindFromDegrees;
        public byte BeaufortScale;
        public string LastBeaufortMaritimeLabel;
        public string LastWindCompassCode;
        public string LastWindCompassFullName;
        public float WindVaneYawRad;
        public float AnemometerRotorPhaseRad;

        // Yagmur Olcer durumu (aktif firtina mm, 24s toplam mm, kuraklik sayaci)
        public float CurrentStormAccumulatedMm;
        public float Rolling24hAccumulatedMm;
        public float DryGapTimerGameSec;

        // Metal Paratoner durumu (kayitli voksel koordinati, yakalanan yildirim sayisi, korona parlamasi)
        public int RegisteredRodWorldX;
        public int RegisteredRodWorldYTop;
        public int RegisteredRodWorldZ;
        public int InterceptedStrikeCount;
        public float CoronaGlow01;
    }

    /// <summary>
    /// Paket 4A: 5 Meteoroloji Aleti (Barometre, Termometre, Higrometre, Ruzgar Gulu+Anemometre, Yagmur Olcer)
    /// ve Metal Paratoner yapilarinin dunyaya yerlestirilmesi, durumlarinin saklanmasi/guncellenmesi ve
    /// toplanip/sokulmesi (Dismantle/Pickup) sistemini yoneten saf C# sinifi (0 B/kare GC).
    /// </summary>
    public sealed class PlacedWeatherInstrumentsSystem
    {
        private readonly List<PlacedWeatherInstrumentRecord> _records = new(32);
        private uint _nextInstanceId = 1u;

        public int Count => _records.Count;
        public IReadOnlyList<PlacedWeatherInstrumentRecord> Records => _records;

        public PlacedWeatherInstrumentRecord GetRecordAt(int index) => _records[index];

        public void SetRecordAt(int index, in PlacedWeatherInstrumentRecord record)
        {
            if ((uint)index < (uint)_records.Count)
            {
                _records[index] = record;
            }
        }

        public bool TryGetRecordById(uint instanceId, out PlacedWeatherInstrumentRecord record)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].InstanceId == instanceId)
                {
                    record = _records[i];
                    return true;
                }
            }
            record = default;
            return false;
        }

        /// <summary>
        /// Yeni bir meteoroloji aletini veya Metal Paratoneri dunyaya yerlestirir (assembly-bagimsiz temel imza).
        /// </summary>
        public bool TryPlaceInstrument(
            PrimitiveCraftOutputId outputKind,
            SysVec3 position,
            SysQuat rotation,
            in PhysicalItemShapeDna dna,
            out uint instanceId,
            bool hasOpenSkyAbove = true,
            double initialGameSeconds = 0.0)
        {
            if (!WorldLoosePlacementMath.IsWeatherInstrumentOrLightningRod(outputKind))
            {
                instanceId = 0u;
                return false;
            }

            instanceId = _nextInstanceId++;
            int rodWx = (int)MathF.Floor(position.X);
            int rodWyTop = (int)MathF.Round(position.Y + WorldLoosePlacementMath.GetWeatherStructureHeightMeters(outputKind));
            int rodWz = (int)MathF.Floor(position.Z);

            var rec = new PlacedWeatherInstrumentRecord
            {
                InstanceId = instanceId,
                OutputKind = outputKind,
                Position = position,
                Rotation = rotation.LengthSquared() > 1e-6f ? SysQuat.Normalize(rotation) : SysQuat.Identity,
                ShapeDna = dna.ShapeSeed != 0u ? dna : PhysicalItemShapeDna.CreateNatural(instanceId * 7919u),
                HasOpenSkyAbove = hasOpenSkyAbove,
                StationPressureHpa = 1013.25f,
                SeaLevelPressureHpa = 1013.25f,
                ThreeHourDeltaHpa = 0.0f,
                P0Hpa = 1013.25f,
                P1Hpa = 1013.25f,
                P2Hpa = 1013.25f,
                P3Hpa = 1013.25f,
                LastPressureSampleGameSec = initialGameSeconds,
                LastZambrettiLetter = 'B',
                LastZambrettiIndex1To26 = 2,
                LastZambrettiForecastLabel = "B - Güzel ve Açık Hava",
                FitzRoyStormWarning = false,
                CurrentTempCelsius = 20.0f,
                DailyMinTempCelsius = 20.0f,
                DailyMaxTempCelsius = 20.0f,
                LastRecordedDayIndex = -1,
                RelativeHumidity01 = 0.55f,
                DewPointCelsius = 10.7f,
                EstimatedCloudBaseWorldY = 210.0f,
                FogRisk01 = 0.0f,
                WindSpeedMps = 2.0f,
                WindFromDegrees = 270.0f,
                BeaufortScale = 2,
                LastBeaufortMaritimeLabel = "2 - Hafif Meltem",
                LastWindCompassCode = "B",
                LastWindCompassFullName = "Batı (Günbatısı)",
                WindVaneYawRad = 0.0f,
                AnemometerRotorPhaseRad = 0.0f,
                CurrentStormAccumulatedMm = 0.0f,
                Rolling24hAccumulatedMm = 0.0f,
                DryGapTimerGameSec = 0.0f,
                RegisteredRodWorldX = rodWx,
                RegisteredRodWorldYTop = rodWyTop,
                RegisteredRodWorldZ = rodWz,
                InterceptedStrikeCount = 0,
                CoronaGlow01 = 0.0f
            };

            _records.Add(rec);
            return true;
        }

        public bool TryDismantleOrPickupById(
            uint instanceId,
            out PlacedWeatherInstrumentRecord removedRecord)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].InstanceId == instanceId)
                {
                    return TryDismantleOrPickupAt(i, out removedRecord);
                }
            }
            removedRecord = default;
            return false;
        }

        public bool TryDismantleOrPickupAt(
            int index,
            out PlacedWeatherInstrumentRecord removedRecord)
        {
            if ((uint)index >= (uint)_records.Count)
            {
                removedRecord = default;
                return false;
            }

            removedRecord = _records[index];
            _records.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Yagmur Olcer ([Shift+E]) su haznesini bosaltip birikimi sifirlar.
        /// </summary>
        public bool TryResetRainGaugeById(uint instanceId, out float clearedMm)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].InstanceId == instanceId)
                {
                    return TryResetRainGaugeAt(i, out clearedMm);
                }
            }
            clearedMm = 0.0f;
            return false;
        }

        public bool TryResetRainGaugeAt(int index, out float clearedMm)
        {
            if ((uint)index >= (uint)_records.Count ||
                _records[index].OutputKind != PrimitiveCraftOutputId.WeatherRainGauge)
            {
                clearedMm = 0.0f;
                return false;
            }

            PlacedWeatherInstrumentRecord rec = _records[index];
            clearedMm = rec.CurrentStormAccumulatedMm;
            rec.CurrentStormAccumulatedMm = 0.0f;
            rec.Rolling24hAccumulatedMm = 0.0f;
            rec.DryGapTimerGameSec = 0.0f;
            rec.ShapeDna = rec.ShapeDna.WithStage(0);
            _records[index] = rec;
            return true;
        }

        public void SetOpenSkyStateAt(int index, bool hasOpenSkyAbove)
        {
            if ((uint)index >= (uint)_records.Count) return;
            PlacedWeatherInstrumentRecord rec = _records[index];
            rec.HasOpenSkyAbove = hasOpenSkyAbove;
            _records[index] = rec;
        }

        public void NotifyLightningInterceptedNear(int rodWorldX, int rodWorldZ)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                PlacedWeatherInstrumentRecord rec = _records[i];
                if (rec.OutputKind == PrimitiveCraftOutputId.MetalLightningRod &&
                    rec.RegisteredRodWorldX == rodWorldX &&
                    rec.RegisteredRodWorldZ == rodWorldZ)
                {
                    rec.InterceptedStrikeCount++;
                    rec.CoronaGlow01 = 1.0f;
                    rec.ShapeDna = rec.ShapeDna.WithTemperature(255);
                    _records[i] = rec;
                }
            }
        }

        /// <summary>
        /// Barometrenin 3 saatlik basinc gecmis halkasini (P0..P3) gunceller ve 3 saatlik dP/dt (hPa) farkini doner.
        /// </summary>
        public static float AdvanceBarometer3HourHistory(
            ref PlacedWeatherInstrumentRecord rec,
            float currentSeaLevelPressureHpa,
            double totalSimulatedGameSeconds)
        {
            const double historyShiftIntervalGameSec = 2700.0; // 45 oyun dakikasi * 4 = 3 oyun saati (10.800 sn)

            if (rec.LastPressureSampleGameSec <= 0.0)
            {
                rec.P0Hpa = currentSeaLevelPressureHpa;
                rec.P1Hpa = currentSeaLevelPressureHpa;
                rec.P2Hpa = currentSeaLevelPressureHpa;
                rec.P3Hpa = currentSeaLevelPressureHpa;
                rec.LastPressureSampleGameSec = totalSimulatedGameSeconds;
            }
            else
            {
                rec.P0Hpa = currentSeaLevelPressureHpa;
                while (totalSimulatedGameSeconds - rec.LastPressureSampleGameSec >= historyShiftIntervalGameSec)
                {
                    rec.P3Hpa = rec.P2Hpa;
                    rec.P2Hpa = rec.P1Hpa;
                    rec.P1Hpa = currentSeaLevelPressureHpa;
                    rec.LastPressureSampleGameSec += historyShiftIntervalGameSec;
                }
            }

            return currentSeaLevelPressureHpa - rec.P3Hpa;
        }

        /// <summary>
        /// Hesaplanmis meteorolojik olcum degerlerini kayda yazar ve 3B model DNA alt-parca (StageByte/ThermalTempByte) acilarini gunceller.
        /// </summary>
        public static void ApplyComputedTelemetryAndDna(
            ref PlacedWeatherInstrumentRecord rec,
            float stationPressureHpa,
            float seaLevelPressureHpa,
            float threeHourDeltaHpa,
            char zambrettiLetter,
            byte zambrettiIndex1To26,
            string zambrettiLabel,
            bool fitzRoyStormWarning,
            float airTempCelsius,
            float relativeHumidity01,
            float dewPointCelsius,
            float estimatedCloudBaseWorldY,
            float windSpeedMps,
            float windFromDegrees,
            byte beaufortScale,
            string beaufortLabel,
            string compassCode,
            string compassFullName,
            float capeJPerKg,
            int currentDayIndex,
            float gameDeltaSeconds)
        {
            rec.StationPressureHpa = stationPressureHpa;
            rec.SeaLevelPressureHpa = seaLevelPressureHpa;
            rec.ThreeHourDeltaHpa = threeHourDeltaHpa;
            rec.LastZambrettiLetter = zambrettiLetter;
            rec.LastZambrettiIndex1To26 = zambrettiIndex1To26;
            rec.LastZambrettiForecastLabel = zambrettiLabel;
            rec.FitzRoyStormWarning = fitzRoyStormWarning;

            if (rec.LastRecordedDayIndex != currentDayIndex)
            {
                rec.LastRecordedDayIndex = currentDayIndex;
                rec.DailyMinTempCelsius = airTempCelsius;
                rec.DailyMaxTempCelsius = airTempCelsius;
            }
            else
            {
                rec.DailyMinTempCelsius = MathF.Min(rec.DailyMinTempCelsius, airTempCelsius);
                rec.DailyMaxTempCelsius = MathF.Max(rec.DailyMaxTempCelsius, airTempCelsius);
            }
            rec.CurrentTempCelsius = airTempCelsius;

            rec.RelativeHumidity01 = relativeHumidity01;
            rec.DewPointCelsius = dewPointCelsius;
            rec.EstimatedCloudBaseWorldY = estimatedCloudBaseWorldY;
            rec.FogRisk01 = Math.Clamp(1.0f - MathF.Max(0.0f, airTempCelsius - dewPointCelsius) / 3.5f, 0.0f, 1.0f);

            rec.WindSpeedMps = windSpeedMps;
            rec.WindFromDegrees = windFromDegrees;
            rec.BeaufortScale = beaufortScale;
            rec.LastBeaufortMaritimeLabel = beaufortLabel;
            rec.LastWindCompassCode = compassCode;
            rec.LastWindCompassFullName = compassFullName;
            rec.WindVaneYawRad = windFromDegrees * (MathF.PI / 180.0f);
            rec.AnemometerRotorPhaseRad = (rec.AnemometerRotorPhaseRad + windSpeedMps * 1.95f * gameDeltaSeconds) % (MathF.PI * 2.0f);

            switch (rec.OutputKind)
            {
                case PrimitiveCraftOutputId.WeatherBarometer:
                {
                    float normP = Math.Clamp((rec.SeaLevelPressureHpa - 965.0f) / 75.0f, 0.02f, 1.0f);
                    byte stage = (byte)Math.Clamp((int)MathF.Round(normP * 255.0f), 1, 255);
                    rec.ShapeDna = rec.ShapeDna.WithStage(stage);
                    break;
                }
                case PrimitiveCraftOutputId.WeatherThermometer:
                {
                    float normT = Math.Clamp((rec.CurrentTempCelsius + 30.0f) / 85.0f, 0.08f, 1.0f);
                    byte stage = (byte)Math.Clamp((int)MathF.Round(normT * 255.0f), 20, 255);
                    rec.ShapeDna = rec.ShapeDna.WithStage(stage);
                    break;
                }
                case PrimitiveCraftOutputId.WeatherHygrometer:
                {
                    byte stage = (byte)Math.Clamp((int)MathF.Round(rec.RelativeHumidity01 * 255.0f), 1, 255);
                    rec.ShapeDna = rec.ShapeDna.WithStage(stage);
                    break;
                }
                case PrimitiveCraftOutputId.WeatherWindVaneAnemometer:
                {
                    float normVane01 = ((rec.WindVaneYawRad / (MathF.PI * 2.0f)) % 1.0f + 1.0f) % 1.0f;
                    float normRotor01 = ((rec.AnemometerRotorPhaseRad / (MathF.PI * 2.0f)) % 1.0f + 1.0f) % 1.0f;
                    byte vaneByte = (byte)Math.Clamp((int)MathF.Round(normVane01 * 255.0f), 0, 255);
                    byte rotorByte = (byte)Math.Clamp((int)MathF.Round(normRotor01 * 255.0f), 0, 255);
                    rec.ShapeDna = rec.ShapeDna.WithStage(rotorByte).WithTemperature(vaneByte);
                    break;
                }
                case PrimitiveCraftOutputId.WeatherRainGauge:
                {
                    float fill01 = Math.Clamp(rec.CurrentStormAccumulatedMm / 50.0f, 0.0f, 1.0f);
                    byte stage = (byte)Math.Clamp((int)MathF.Round(fill01 * 255.0f), 0, 255);
                    rec.ShapeDna = rec.ShapeDna.WithStage(stage);
                    break;
                }
                case PrimitiveCraftOutputId.MetalLightningRod:
                {
                    float capeGlow = capeJPerKg >= 1000.0f
                        ? Math.Clamp((capeJPerKg - 900.0f) / 1600.0f, 0.15f, 0.85f)
                        : 0.0f;
                    rec.CoronaGlow01 = MathF.Max(capeGlow, rec.CoronaGlow01 * MathF.Exp(-0.8f * gameDeltaSeconds));
                    byte tempByte = (byte)Math.Clamp((int)MathF.Round(rec.CoronaGlow01 * 255.0f), 0, 255);
                    rec.ShapeDna = rec.ShapeDna.WithTemperature(tempByte);
                    break;
                }
            }
        }

        public void ClearAll()
        {
            _records.Clear();
        }

#if !UNITY_5_3_OR_NEWER
        public bool TryPlaceInstrument(
            PrimitiveCraftOutputId outputKind,
            SysVec3 position,
            SysQuat rotation,
            in PhysicalItemShapeDna dna,
            ConvectiveLightningAndRodSystem? lightningSystem,
            out uint instanceId,
            bool hasOpenSkyAbove = true,
            double initialGameSeconds = 0.0)
        {
            if (!TryPlaceInstrument(
                    outputKind,
                    position,
                    rotation,
                    in dna,
                    out instanceId,
                    hasOpenSkyAbove,
                    initialGameSeconds))
            {
                return false;
            }

            if (outputKind == PrimitiveCraftOutputId.MetalLightningRod && lightningSystem != null)
            {
                PlacedWeatherInstrumentRecord rec = _records[_records.Count - 1];
                lightningSystem.RegisterLightningRod(
                    rec.RegisteredRodWorldX,
                    rec.RegisteredRodWorldYTop,
                    rec.RegisteredRodWorldZ);
            }
            return true;
        }

        public bool TryDismantleOrPickupById(
            uint instanceId,
            ConvectiveLightningAndRodSystem? lightningSystem,
            out PlacedWeatherInstrumentRecord removedRecord)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].InstanceId == instanceId)
                {
                    return TryDismantleOrPickupAt(i, lightningSystem, out removedRecord);
                }
            }
            removedRecord = default;
            return false;
        }

        public bool TryDismantleOrPickupAt(
            int index,
            ConvectiveLightningAndRodSystem? lightningSystem,
            out PlacedWeatherInstrumentRecord removedRecord)
        {
            if (!TryDismantleOrPickupAt(index, out removedRecord))
            {
                return false;
            }

            if (removedRecord.OutputKind == PrimitiveCraftOutputId.MetalLightningRod && lightningSystem != null)
            {
                lightningSystem.UnregisterLightningRod(removedRecord.RegisteredRodWorldX, removedRecord.RegisteredRodWorldZ);
            }
            return true;
        }

        public void StepAllPlacedInstruments(
            RegionalAtmosphereSimulationSystem atmosphere,
            double totalSimulatedGameSeconds,
            float gameDeltaSeconds,
            SeasonType currentSeason,
            int currentDayIndex,
            float seaLevelWorldY = 90.0f)
        {
            if (_records.Count == 0 || gameDeltaSeconds <= 0.0f) return;

            for (int i = 0; i < _records.Count; i++)
            {
                PlacedWeatherInstrumentRecord rec = _records[i];
                AtmosphereCellState cell = atmosphere.SampleBilinearAtWorldXZ(rec.Position.X, rec.Position.Z);

                float altitudeAboveSea = MathF.Max(0.0f, rec.Position.Y - seaLevelWorldY);
                float slpNow = WeatherForecastInstrumentsSystem.ComputeSeaLevelReducedPressureHpa(
                    cell.StationPressureHpa, altitudeAboveSea, cell.AirTempCelsius);
                float threeHourDelta = AdvanceBarometer3HourHistory(ref rec, slpNow, totalSimulatedGameSeconds);

                ZambrettiForecastResult zamb = WeatherForecastInstrumentsSystem.EvaluateZambrettiForecast(
                    slpNow,
                    threeHourDelta,
                    cell.WindVelX,
                    cell.WindVelZ,
                    currentSeason);

                WeatherForecastInstrumentsSystem.EvaluatePsychrometer(
                    cell.AirTempCelsius,
                    cell.VaporPressureHpa,
                    out _,
                    out float rh01,
                    out float dewPointC,
                    out float lclAboveGroundM);

                float windSpeed = MathF.Sqrt(cell.WindVelX * cell.WindVelX + cell.WindVelZ * cell.WindVelZ);
                byte beaufort = WeatherForecastInstrumentsSystem.EvaluateBeaufortScale0To12(
                    windSpeed,
                    out _,
                    out string beaufortTr);
                WeatherForecastInstrumentsSystem.EvaluateWindCompassDirection8(
                    cell.WindVelX,
                    cell.WindVelZ,
                    out _,
                    out string compassCode,
                    out string compassFullName);
                float windDeg = MathF.Atan2(cell.WindVelX, cell.WindVelZ) * (180.0f / MathF.PI);
                if (windDeg < 0.0f) windDeg += 360.0f;

                WeatherForecastInstrumentsSystem.StepRainGaugeAccumulation(
                    ref rec.CurrentStormAccumulatedMm,
                    ref rec.Rolling24hAccumulatedMm,
                    cell.SurfacePrecipMmPerHour,
                    rec.HasOpenSkyAbove,
                    gameDeltaSeconds);

                ApplyComputedTelemetryAndDna(
                    ref rec,
                    cell.StationPressureHpa,
                    zamb.SeaLevelPressureHpa,
                    zamb.ThreeHourPressureDeltaHpa,
                    zamb.Letter,
                    zamb.Index1To26,
                    zamb.TurkishForecastLabel,
                    zamb.FitzRoyStormWarning,
                    cell.AirTempCelsius,
                    rh01,
                    dewPointC,
                    rec.Position.Y + lclAboveGroundM,
                    windSpeed,
                    windDeg,
                    beaufort,
                    beaufortTr,
                    compassCode,
                    compassFullName,
                    cell.CapeJPerKg,
                    currentDayIndex,
                    gameDeltaSeconds);

                _records[i] = rec;
            }
        }

        public void ClearAll(ConvectiveLightningAndRodSystem? lightningSystem)
        {
            if (lightningSystem != null)
            {
                for (int i = 0; i < _records.Count; i++)
                {
                    if (_records[i].OutputKind == PrimitiveCraftOutputId.MetalLightningRod)
                    {
                        lightningSystem.UnregisterLightningRod(
                            _records[i].RegisteredRodWorldX,
                            _records[i].RegisteredRodWorldZ);
                    }
                }
            }
            _records.Clear();
        }
#endif
    }
}
