#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Stratocraft.Core.Weather
{
    /// <summary>
    /// IP-9: Gelecek hava olaylarina hazir kanonik yagis tipi.
    /// Paket 4A kapsaminda aktif simule edilen yagislar: <see cref="None"/> ve <see cref="Rain"/>.
    /// </summary>
    public enum PrecipitationKind : byte
    {
        None = 0,
        Rain = 1,
        Snow = 2,
        Hail = 3,
        VolcanicAsh = 4
    }

    /// <summary>
    /// IP-9: Gelecek atmosfer tehlikelerine hazir bit bayraklari.
    /// Paket 4A kapsaminda aktif uretilen bayrak: <see cref="ThunderLightning"/> (CAPE &gt; 1000 J/kg).
    /// </summary>
    [Flags]
    public enum AtmosphericHazardFlags : ushort
    {
        None             = 0,
        ThunderLightning = 1 << 0,
        GroundFog        = 1 << 1,
        Sandstorm        = 1 << 2,
        Blizzard         = 1 << 3,
        Heatwave         = 1 << 4
    }

    /// <summary>
    /// Physics ve Networking katmanlari arasinda dongusel asmdef bagimliligi olusturmadan
    /// 9x9 atmosfer AOI penceresini aktarmak icin kullanilan 16 baytlik blittable orneklem yapisi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct AtmosphereAoiCellSample
    {
        public short AirTempDeciC;
        public ushort StationPressureDeciHpa;
        public ushort CloudWaterCentiKgPerM2;
        public ushort PrecipDeciMmPerHour;
        public short WindXCentiMps;
        public short WindZCentiMps;
        public byte CloudBase16m;
        public byte CloudThickness32m;
        public byte ActiveKindRaw;
        public byte HazardFlagsLowByte;

        public PrecipitationKind ActiveKind
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (PrecipitationKind)ActiveKindRaw;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => ActiveKindRaw = (byte)value;
        }

        public AtmosphericHazardFlags Hazards
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (AtmosphericHazardFlags)HazardFlagsLowByte;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => HazardFlagsLowByte = (byte)((ushort)value & 0xFFu);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AtmosphereAoiCellSample FromPhysical(
            float airTempC,
            float stationPressureHpa,
            float cloudWaterKgPerM2,
            float precipMmPerHour,
            float windXMps,
            float windZMps,
            ushort cloudBaseWorldY,
            ushort cloudTopWorldY,
            PrecipitationKind activeKind,
            AtmosphericHazardFlags hazards)
        {
            int depthM = cloudTopWorldY > cloudBaseWorldY ? cloudTopWorldY - cloudBaseWorldY : 0;
            return new AtmosphereAoiCellSample
            {
                AirTempDeciC = (short)System.Math.Clamp((int)MathF.Round(airTempC * 10f), short.MinValue, short.MaxValue),
                StationPressureDeciHpa = (ushort)System.Math.Clamp((int)MathF.Round(MathF.Max(0f, stationPressureHpa - 500f) * 10f), 0, ushort.MaxValue),
                CloudWaterCentiKgPerM2 = (ushort)System.Math.Clamp((int)MathF.Round(MathF.Max(0f, cloudWaterKgPerM2) * 100f), 0, ushort.MaxValue),
                PrecipDeciMmPerHour = (ushort)System.Math.Clamp((int)MathF.Round(MathF.Max(0f, precipMmPerHour) * 10f), 0, ushort.MaxValue),
                WindXCentiMps = (short)System.Math.Clamp((int)MathF.Round(windXMps * 100f), short.MinValue, short.MaxValue),
                WindZCentiMps = (short)System.Math.Clamp((int)MathF.Round(windZMps * 100f), short.MinValue, short.MaxValue),
                CloudBase16m = (byte)System.Math.Clamp((cloudBaseWorldY + 8) / 16, 0, 255),
                CloudThickness32m = (byte)System.Math.Clamp((depthM + 16) / 32, 0, 255),
                ActiveKindRaw = (byte)SanitizePrecipitationForPaket4A(activeKind),
                HazardFlagsLowByte = (byte)((ushort)SanitizeHazardsForPaket4A(hazards) & 0xFFu)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PrecipitationKind SanitizePrecipitationForPaket4A(PrecipitationKind kind)
            => kind == PrecipitationKind.Rain ? PrecipitationKind.Rain : PrecipitationKind.None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AtmosphericHazardFlags SanitizeHazardsForPaket4A(AtmosphericHazardFlags flags)
            => flags & AtmosphericHazardFlags.ThunderLightning;
    }
}

namespace Stratocraft.WorldGen
{
    public enum SeasonType : byte
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }
}
