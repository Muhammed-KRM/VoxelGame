#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Weather;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// 64m x 64m bolgesel atmosfer sutununun tam 36 baytlik (9 x 4B) blittable fiziksel durumu (0 B GC).
    /// Tum ham alanlar ilkel tiplerden olusur; hem unsafe sizeof hem Marshal.SizeOf tam 36 bayt verir.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct AtmosphereCellState
    {
        public const int StructByteSize = 36;

        // [0..23] : 6 adet FP32 termodinamik ve hidrolojik ana buyukluk (24 Bayt)
        public float SurfaceTempCelsius;       // 0..3   : Yuzey sicakligi Ts (°C)
        public float AirTempCelsius;           // 4..7   : 2m hava sicakligi Ta (°C)
        public float StationPressureHpa;       // 8..11  : Istasyon basinci P (hPa)
        public float VaporPressureHpa;         // 12..15 : Gercek su buhari basinci ea (hPa)
        public float CloudWaterKgPerM2;        // 16..19 : Yogusmus bulut sivi su sutunu qc (kg/m^2)
        public float SurfacePrecipMmPerHour;   // 20..23 : Yuzeye inen aktif yagis siddeti (mm/saat)

        // [24..33] : 5 adet 16-bit kuantize ruzgar, irtifa ve CAPE alani (10 Bayt)
        private short _windVelXCentiMps;       // 24..25 : Bati->Dogu (+X) ruzgar hizi (0.01 m/s cozunurluk)
        private short _windVelZCentiMps;       // 26..27 : Guney->Kuzey (+Z) ruzgar hizi (0.01 m/s cozunurluk)
        public ushort CloudBaseWorldY;         // 28..29 : LCL bulut taban kotu (m)
        public ushort CapeJPerKg;              // 30..31 : Konvektif potansiyel enerji CAPE (J/kg)
        private ushort _hazardFlagsRaw;        // 32..33 : AtmosphericHazardFlags bit maskesi

        // [34..35] : 2 adet 8-bit yagis turu ve bulut kalinligi alani (2 Bayt) -> Toplam = 36 Bayt
        private byte _activeKindRaw;           // 34     : PrecipitationKind (None / Rain)
        private byte _cloudDepth32m;           // 35     : Bulut kalinligi (32m adimlarla 0..8160m)

        public float PrecipitationMmPerHour
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => SurfacePrecipMmPerHour;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => SurfacePrecipMmPerHour = value;
        }

        public float WindVelX
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => _windVelXCentiMps * 0.01f;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _windVelXCentiMps = (short)Math.Clamp((int)MathF.Round(value * 100f), short.MinValue, short.MaxValue);
        }

        public float WindVelZ
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => _windVelZCentiMps * 0.01f;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _windVelZCentiMps = (short)Math.Clamp((int)MathF.Round(value * 100f), short.MinValue, short.MaxValue);
        }

        public float WindX
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => WindVelX;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => WindVelX = value;
        }

        public float WindZ
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => WindVelZ;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => WindVelZ = value;
        }

        public ushort CloudTopWorldY
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (ushort)Math.Min(ushort.MaxValue, CloudBaseWorldY + (_cloudDepth32m * 32));
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                int depth = value > CloudBaseWorldY ? value - CloudBaseWorldY : 0;
                _cloudDepth32m = (byte)Math.Clamp((depth + 16) / 32, 0, 255);
            }
        }

        public PrecipitationKind ActiveKind
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (PrecipitationKind)_activeKindRaw;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _activeKindRaw = (byte)value;
        }

        public PrecipitationKind Precipitation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => ActiveKind;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => ActiveKind = value;
        }

        public AtmosphericHazardFlags Hazards
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (AtmosphericHazardFlags)_hazardFlagsRaw;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _hazardFlagsRaw = (ushort)value;
        }

        public readonly float WindSpeedMps
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                float ux = WindVelX;
                float uz = WindVelZ;
                return MathF.Sqrt(ux * ux + uz * uz);
            }
        }

        public readonly float RelativeHumidity01
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                float tClamped = Math.Clamp(AirTempCelsius, -65f, 75f);
                float es = 6.1094f * MathF.Exp((17.625f * tClamped) / (243.04f + tClamped));
                return es > 1e-4f ? Math.Clamp(VaporPressureHpa / es, 0f, 1f) : 0f;
            }
        }

        public readonly float DewPointCelsius
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                float tClamped = Math.Clamp(AirTempCelsius, -65f, 75f);
                float rh = Math.Clamp(RelativeHumidity01, 0.01f, 1f);
                float gamma = MathF.Log(rh) + ((17.625f * tClamped) / (243.04f + tClamped));
                float denom = MathF.Max(0.25f, 17.625f - gamma);
                return (243.04f * gamma) / denom;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly AtmosphereAoiCellSample ToAoiSample()
        {
            return AtmosphereAoiCellSample.FromPhysical(
                AirTempCelsius,
                StationPressureHpa,
                CloudWaterKgPerM2,
                SurfacePrecipMmPerHour,
                WindVelX,
                WindVelZ,
                CloudBaseWorldY,
                CloudTopWorldY,
                ActiveKind,
                Hazards);
        }
    }

    /// <summary>
    /// Jin (1997, J. Atmos. Sci.) ENSO Recharge Oscillator iki degiskenli cok yillik iklim hafizasi.
    /// Analitik matris usteli (exact matrix exponential) ile keyfi buyuklukteki zaman adimlarinda
    /// (~3.5 oyun yili periyotlu) %100 kararli sicak/kurak (El Nino) ve serin/yagisli (La Nina) salinim uretir.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct EnsoRechargeOscillatorState
    {
        /// <summary>Dogu pasifik / bolgesel sicaklik ve kuraklik anomalisi Te (K / °C). +: sicak/kurak, -: serin/yagisli.</summary>
        public float TeAnomalyK;

        /// <summary>Okyanus termoklin derinlik / isi rezervuari anomalisi h (metre).</summary>
        public float ThermoclineDepthAnomalyM;

        public float TempAnomalyCelsius
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => TeAnomalyK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => TeAnomalyK = value;
        }

        public float ThermoclineAnomaly
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => ThermoclineDepthAnomalyM;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => ThermoclineDepthAnomalyM = value;
        }

        public static EnsoRechargeOscillatorState CreateDefault(int worldSeed = 14532026)
        {
            uint h = (uint)worldSeed * 0x9E3779B9u ^ 0x85EBCA6Bu;
            float phase = ((h & 0xFFFFu) / 65535.0f) * MathF.PI * 2.0f;
            return new EnsoRechargeOscillatorState
            {
                TeAnomalyK = 1.45f * MathF.Cos(phase),
                ThermoclineDepthAnomalyM = 2.25f * MathF.Sin(phase)
            };
        }

        /// <summary>
        /// Jin (1997) dTe/dt = R*Te + F1*h, dh/dt = -F2*Te - eps*h sistemini analitik matris usteliyle ilerletir.
        /// Tr(A) = 0 ve Det(A) = omega^2 secildiginden 600x hizda bile genlik ne sonumlenir ne de sonsuza kacar.
        /// </summary>
        public void Step(double gameDeltaSeconds)
        {
            if (gameDeltaSeconds <= 0.0 || !double.IsFinite(gameDeltaSeconds))
            {
                return;
            }

            if (!float.IsFinite(TeAnomalyK) || !float.IsFinite(ThermoclineDepthAnomalyM))
            {
                this = CreateDefault();
                return;
            }

            // Sifir baslangicta deterministik kucuk tohum uyarimi (tam sifirda donup kalmamasi icin)
            if (MathF.Abs(TeAnomalyK) < 1e-5f && MathF.Abs(ThermoclineDepthAnomalyM) < 1e-5f)
            {
                TeAnomalyK = 1.25f;
                ThermoclineDepthAnomalyM = 0.50f;
            }

            // Periyot T_enso = 3.5 oyun yili = 3.5 * 365 * 3600 = 4,599,000 oyun saniyesi
            const double targetPeriodSeconds = 3.5 * StratocraftConstants.SecondsPerInGameYear;
            const double omega0 = (2.0 * Math.PI) / targetPeriodSeconds;

            // Jin (1997) katsayilari: R = eps -> Tr(A) = 0, F1*F2 - R*eps = omega^2
            const double rGrowth = 0.18 * omega0;
            const double epsOcean = 0.18 * omega0;
            const double f1Coupling = 0.65 * omega0;
            const double f2Coupling = 1.60 * omega0;
            double omega = Math.Sqrt((f1Coupling * f2Coupling) - (rGrowth * epsOcean));

            double angle = omega * gameDeltaSeconds;
            // Buyuk dt'de faz hassasiyeti icin 2*PI moduna indirge
            angle %= (2.0 * Math.PI);

            double cosW = Math.Cos(angle);
            double sinOverW = Math.Sin(angle) / omega;

            double te0 = TeAnomalyK;
            double h0 = ThermoclineDepthAnomalyM;

            double teNext = (cosW * te0) + (sinOverW * ((rGrowth * te0) + (f1Coupling * h0)));
            double hNext = (cosW * h0) + (sinOverW * ((-f2Coupling * te0) - (epsOcean * h0)));

            TeAnomalyK = Math.Clamp((float)teNext, -3.5f, 3.5f);
            ThermoclineDepthAnomalyM = Math.Clamp((float)hNext, -8.0f, 8.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StepExactOrRk2(double gameDeltaSeconds) => Step(gameDeltaSeconds);
    }
}
