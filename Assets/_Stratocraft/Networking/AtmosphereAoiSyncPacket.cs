#nullable enable
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Weather;

namespace Stratocraft.Networking
{
    /// <summary>
    /// Zaman olcegi otoritesi ve ag topolojisi modu.
    /// </summary>
    public enum NetworkTopologyMode : byte
    {
        Singleplayer = 0,
        HostLoopback = 1,
        DedicatedServer = 2,
        RemoteClient = 3,
        Client = RemoteClient
    }

    /// <summary>
    /// 9x9 atmosfer AOI penceresi icin 8 baytlik 1 Hz paket basligi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct AtmosphereAoiPacketHeader
    {
        public const int ByteSize = 8;

        public readonly short CenterCellX;
        public readonly short CenterCellZ;
        public readonly ushort QuantizedTimeScaleDeci;
        public readonly sbyte QuantizedEnsoAnomalyDeciK;
        public readonly byte ChangedCellCount;

        public float TimeScaleMultiplier => QuantizedTimeScaleDeci * 0.1f;
        public float EnsoTeAnomalyK => QuantizedEnsoAnomalyDeciK * 0.1f;

        public AtmosphereAoiPacketHeader(
            short centerCellX,
            short centerCellZ,
            float timeScaleMultiplier,
            float ensoTeAnomalyK,
            byte changedCellCount)
        {
            CenterCellX = centerCellX;
            CenterCellZ = centerCellZ;
            float clampedScale = Math.Clamp(
                float.IsFinite(timeScaleMultiplier) ? timeScaleMultiplier : 1.0f,
                StratocraftConstants.MinTimeAccelerationMultiplier,
                StratocraftConstants.MaxTimeAccelerationMultiplier);
            QuantizedTimeScaleDeci = (ushort)Math.Clamp((int)MathF.Round(clampedScale * 10.0f), 10, 6000);
            QuantizedEnsoAnomalyDeciK = (sbyte)Math.Clamp((int)MathF.Round((float.IsFinite(ensoTeAnomalyK) ? ensoTeAnomalyK : 0.0f) * 10.0f), sbyte.MinValue, sbyte.MaxValue);
            ChangedCellCount = changedCellCount;
        }
    }

    /// <summary>
    /// Tek bir degisen atmosfer AOI hucresinin tam 14 baytlik (&lt;= 16 bayt) kuantize ag deltasi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct AtmosphereAoiCellDelta
    {
        public const int ByteSize = 14;

        public readonly byte CellIndex0To80;              // 1B: 0..80 (localZ * 9 + localX)
        public readonly byte PackedKindAndHazards;        // 1B: [0..2]=PrecipitationKind, [3..7]=AtmosphericHazardFlags
        public readonly short AirTempDeciC;               // 2B: 0.1 °C
        public readonly ushort StationPressureDeciHpa;    // 2B: (P - 500.0) * 10.0 hPa
        public readonly ushort CloudWaterCentiKgPerM2;    // 2B: 0.01 kg/m^2
        public readonly ushort PrecipDeciMmPerHour;       // 2B: 0.1 mm/sa
        public readonly sbyte WindXHalfMps;               // 1B: 0.5 m/s (-64 .. +63.5 m/s)
        public readonly sbyte WindZHalfMps;               // 1B: 0.5 m/s (-64 .. +63.5 m/s)
        public readonly byte CloudBase16m;                // 1B: 16m adimlarla 0..4080m
        public readonly byte CloudThickness32m;           // 1B: 32m adimlarla 0..8160m

        public PrecipitationKind ActiveKind => (PrecipitationKind)(PackedKindAndHazards & 0x07);
        public AtmosphericHazardFlags Hazards => (AtmosphericHazardFlags)((PackedKindAndHazards >> 3) & 0x1F);
        public float AirTempCelsius => AirTempDeciC * 0.1f;
        public float StationPressureHpa => 500.0f + (StationPressureDeciHpa * 0.1f);
        public float CloudWaterKgPerM2 => CloudWaterCentiKgPerM2 * 0.01f;
        public float SurfacePrecipMmPerHour => PrecipDeciMmPerHour * 0.1f;
        public float WindVelX => WindXHalfMps * 0.5f;
        public float WindVelZ => WindZHalfMps * 0.5f;
        public ushort CloudBaseWorldY => (ushort)(CloudBase16m * 16);
        public ushort CloudTopWorldY => (ushort)((CloudBase16m * 16) + (CloudThickness32m * 32));

        public AtmosphereAoiCellDelta(
            byte cellIndex0To80,
            byte packedKindAndHazards,
            short airTempDeciC,
            ushort stationPressureDeciHpa,
            ushort cloudWaterCentiKgPerM2,
            ushort precipDeciMmPerHour,
            sbyte windXHalfMps,
            sbyte windZHalfMps,
            byte cloudBase16m,
            byte cloudThickness32m)
        {
            CellIndex0To80 = cellIndex0To80;
            PackedKindAndHazards = packedKindAndHazards;
            AirTempDeciC = airTempDeciC;
            StationPressureDeciHpa = stationPressureDeciHpa;
            CloudWaterCentiKgPerM2 = cloudWaterCentiKgPerM2;
            PrecipDeciMmPerHour = precipDeciMmPerHour;
            WindXHalfMps = windXHalfMps;
            WindZHalfMps = windZHalfMps;
            CloudBase16m = cloudBase16m;
            CloudThickness32m = cloudThickness32m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AtmosphereAoiCellDelta FromSample(byte cellIndex0To80, in AtmosphereAoiCellSample sample)
        {
            byte kindBits = (byte)((byte)AtmosphereAoiCellSample.SanitizePrecipitationForPaket4A(sample.ActiveKind) & 0x07u);
            byte hazardBits = (byte)(((ushort)AtmosphereAoiCellSample.SanitizeHazardsForPaket4A(sample.Hazards) & 0x1Fu) << 3);
            sbyte qWx = (sbyte)Math.Clamp((int)MathF.Round((sample.WindXCentiMps * 0.01f) * 2.0f), sbyte.MinValue, sbyte.MaxValue);
            sbyte qWz = (sbyte)Math.Clamp((int)MathF.Round((sample.WindZCentiMps * 0.01f) * 2.0f), sbyte.MinValue, sbyte.MaxValue);

            return new AtmosphereAoiCellDelta(
                cellIndex0To80: cellIndex0To80,
                packedKindAndHazards: (byte)(kindBits | hazardBits),
                airTempDeciC: sample.AirTempDeciC,
                stationPressureDeciHpa: sample.StationPressureDeciHpa,
                cloudWaterCentiKgPerM2: sample.CloudWaterCentiKgPerM2,
                precipDeciMmPerHour: sample.PrecipDeciMmPerHour,
                windXHalfMps: qWx,
                windZHalfMps: qWz,
                cloudBase16m: sample.CloudBase16m,
                cloudThickness32m: sample.CloudThickness32m);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AtmosphereAoiCellSample ToSample()
        {
            return new AtmosphereAoiCellSample
            {
                AirTempDeciC = AirTempDeciC,
                StationPressureDeciHpa = StationPressureDeciHpa,
                CloudWaterCentiKgPerM2 = CloudWaterCentiKgPerM2,
                PrecipDeciMmPerHour = PrecipDeciMmPerHour,
                WindXCentiMps = (short)(WindXHalfMps * 50),
                WindZCentiMps = (short)(WindZHalfMps * 50),
                CloudBase16m = CloudBase16m,
                CloudThickness32m = CloudThickness32m,
                ActiveKindRaw = (byte)ActiveKind,
                HazardFlagsLowByte = (byte)Hazards
            };
        }
    }

    /// <summary>
    /// Oyuncu cevresindeki 9x9 atmosfer hucresi icin 1 Hz frekansli, sifir GC (stackalloc)
    /// kuantize delta paketleyici ve cozucu (&lt;= 14 Bayt/hucre, &lt; 512 Bayt/sn bant genisligi garantili).
    /// </summary>
    public static class AtmosphereAoiSyncPacket
    {
        public const int AoiSideCells = StratocraftConstants.AtmosphereAoiGridSideCells; // 9
        public const int AoiCellCount = AoiSideCells * AoiSideCells;                     // 81
        public const int HeaderByteSize = AtmosphereAoiPacketHeader.ByteSize;            // 8
        public const int CellDeltaByteSize = AtmosphereAoiCellDelta.ByteSize;            // 14 (<= 16)
        public const int SyncRateHz = 1;
        public const int MaxTargetBytesPerSecond = StratocraftConstants.MaxAtmosphereAoiBytesPerSecond; // 512
        public const int MaxCellsPerBudgetPacket = 32; // 8 + 32 * 14 = 456 Bayt (< 512 B/sn!)
        public const int MaxWirePacketBytes = HeaderByteSize + (MaxCellsPerBudgetPacket * CellDeltaByteSize); // 456

        /// <summary>
        /// Mevcut 9x9 AOI penceresini istemcinin bilinen son baz durumuyla karsilastirir,
        /// degisim onceligine gore en fazla <paramref name="maxCellsPerPacket"/> hucreyi (varsayilan 32 -> maks 456 Bayt)
        /// <paramref name="outWireBuffer"/> icine yazar ve gonderilen hucrelerin baz durumunu gunceller (0 B GC).
        /// </summary>
        public static int EncodeDeltaPacket(
            short centerCellX,
            short centerCellZ,
            float serverTimeScale,
            float ensoTeAnomalyK,
            ReadOnlySpan<AtmosphereAoiCellSample> currentWindow81,
            Span<AtmosphereAoiCellSample> clientBaseline81,
            ref bool hasValidBaseline,
            Span<byte> outWireBuffer,
            int maxCellsPerPacket = MaxCellsPerBudgetPacket)
        {
            if (currentWindow81.Length < AoiCellCount ||
                clientBaseline81.Length < AoiCellCount ||
                outWireBuffer.Length < HeaderByteSize)
            {
                return 0;
            }

            int maxPossibleByBuf = (outWireBuffer.Length - HeaderByteSize) / CellDeltaByteSize;
            int cellBudget = Math.Clamp(Math.Min(maxCellsPerPacket, maxPossibleByBuf), 0, AoiCellCount);

            Span<byte> candidateIndices = stackalloc byte[AoiCellCount];
            Span<int> candidateScores = stackalloc int[AoiCellCount];
            int candidateCount = 0;

            for (int i = 0; i < AoiCellCount; i++)
            {
                int score = !hasValidBaseline
                    ? 100_000 - i
                    : ComputeSignificanceScore(in currentWindow81[i], in clientBaseline81[i]);

                if (score > 0)
                {
                    candidateIndices[candidateCount] = (byte)i;
                    candidateScores[candidateCount] = score;
                    candidateCount++;
                }
            }

            // Kucuk 81 elemanli yigin dizisinde en yuksek oncelikli cellBudget elemani sec (0 B GC kismi secim siralama)
            int emitCount = Math.Min(candidateCount, cellBudget);
            for (int sel = 0; sel < emitCount; sel++)
            {
                int bestIdx = sel;
                int bestScore = candidateScores[sel];
                for (int j = sel + 1; j < candidateCount; j++)
                {
                    if (candidateScores[j] > bestScore)
                    {
                        bestScore = candidateScores[j];
                        bestIdx = j;
                    }
                }

                if (bestIdx != sel)
                {
                    (candidateIndices[sel], candidateIndices[bestIdx]) = (candidateIndices[bestIdx], candidateIndices[sel]);
                    (candidateScores[sel], candidateScores[bestIdx]) = (candidateScores[bestIdx], candidateScores[sel]);
                }
            }

            var header = new AtmosphereAoiPacketHeader(
                centerCellX,
                centerCellZ,
                serverTimeScale,
                ensoTeAnomalyK,
                (byte)emitCount);
            WriteHeader(in header, outWireBuffer);

            int offset = HeaderByteSize;
            for (int k = 0; k < emitCount; k++)
            {
                byte cellIdx = candidateIndices[k];
                AtmosphereAoiCellDelta delta = AtmosphereAoiCellDelta.FromSample(cellIdx, in currentWindow81[cellIdx]);
                WriteCellDelta(in delta, outWireBuffer.Slice(offset, CellDeltaByteSize));
                offset += CellDeltaByteSize;

                // Istemci baz durumuna kuantize edilmis karsiligini kaydet (kayma birikimini onler)
                clientBaseline81[cellIdx] = delta.ToSample();
            }

            if (!hasValidBaseline && emitCount > 0)
            {
                // Ilk pakette gonderilmeyen kuru/sakin hucreleri sifir-oncelikli degil, bir sonraki 1 Hz tikte gonderilecek sekilde isaretle
                for (int i = 0; i < AoiCellCount; i++)
                {
                    bool sent = false;
                    for (int k = 0; k < emitCount; k++)
                    {
                        if (candidateIndices[k] == i)
                        {
                            sent = true;
                            break;
                        }
                    }

                    if (!sent)
                    {
                        clientBaseline81[i] = new AtmosphereAoiCellSample { AirTempDeciC = short.MinValue };
                    }
                }

                hasValidBaseline = true;
            }

            return offset;
        }

        /// <summary>
        /// 1 Hz atmosfer AOI delta paketini cozer ve istemcinin 9x9 gorunum penceresine uygular (0 B GC).
        /// </summary>
        public static bool TryDecodeAndApplyDeltaPacket(
            ReadOnlySpan<byte> wireBytes,
            Span<AtmosphereAoiCellSample> clientWindow81,
            out AtmosphereAoiPacketHeader header)
        {
            if (wireBytes.Length < HeaderByteSize || clientWindow81.Length < AoiCellCount)
            {
                header = default;
                return false;
            }

            header = ReadHeader(wireBytes);
            int expectedBytes = HeaderByteSize + (header.ChangedCellCount * CellDeltaByteSize);
            if (wireBytes.Length < expectedBytes || header.ChangedCellCount > AoiCellCount)
            {
                return false;
            }

            int offset = HeaderByteSize;
            for (int i = 0; i < header.ChangedCellCount; i++)
            {
                AtmosphereAoiCellDelta delta = ReadCellDelta(wireBytes.Slice(offset, CellDeltaByteSize));
                offset += CellDeltaByteSize;
                if (delta.CellIndex0To80 >= AoiCellCount)
                {
                    return false;
                }

                clientWindow81[delta.CellIndex0To80] = delta.ToSample();
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeSignificanceScore(in AtmosphereAoiCellSample cur, in AtmosphereAoiCellSample prev)
        {
            if (prev.AirTempDeciC == short.MinValue)
            {
                return 50_000;
            }

            AtmosphereAoiCellDelta qCur = AtmosphereAoiCellDelta.FromSample(0, in cur);
            AtmosphereAoiCellDelta qPrev = AtmosphereAoiCellDelta.FromSample(0, in prev);

            int dTemp = Math.Abs(qCur.AirTempDeciC - qPrev.AirTempDeciC);                         // 1 birim = 0.1 °C
            int dPress = Math.Abs(qCur.StationPressureDeciHpa - qPrev.StationPressureDeciHpa);    // 1 birim = 0.1 hPa
            int dCloud = Math.Abs(qCur.CloudWaterCentiKgPerM2 - qPrev.CloudWaterCentiKgPerM2);    // 1 birim = 0.01 kg/m^2
            int dPrecip = Math.Abs(qCur.PrecipDeciMmPerHour - qPrev.PrecipDeciMmPerHour);         // 1 birim = 0.1 mm/sa
            int dWindX = Math.Abs(qCur.WindXHalfMps - qPrev.WindXHalfMps);                        // 1 birim = 0.5 m/s
            int dWindZ = Math.Abs(qCur.WindZHalfMps - qPrev.WindZHalfMps);
            int dBase = Math.Abs(qCur.CloudBase16m - qPrev.CloudBase16m);
            int dThick = Math.Abs(qCur.CloudThickness32m - qPrev.CloudThickness32m);

            bool kindChanged = qCur.PackedKindAndHazards != qPrev.PackedKindAndHazards;
            bool hazardChanged = qCur.Hazards != qPrev.Hazards;

            // Olu-bant (deadband) filtresi: gozle gorulmez mikro-dalgalanmalarda 0 Bayt harca
            if (!kindChanged &&
                !hazardChanged &&
                dTemp < 5 &&      // < 0.5 °C
                dPress < 5 &&     // < 0.5 hPa
                dCloud < 12 &&    // < 0.12 kg/m^2
                dPrecip < 5 &&    // < 0.5 mm/sa
                dWindX < 1 &&     // < 0.5 m/s
                dWindZ < 1 &&
                dBase < 1 &&
                dThick < 1)
            {
                return 0;
            }

            int score = (kindChanged ? 20_000 : 0)
                      + (hazardChanged ? 25_000 : 0)
                      + (dPrecip * 40)
                      + (dCloud * 12)
                      + (dPress * 8)
                      + (dTemp * 6)
                      + ((dWindX + dWindZ) * 10)
                      + ((dBase + dThick) * 8);
            return Math.Max(1, score);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteHeader(in AtmosphereAoiPacketHeader header, Span<byte> dst)
        {
            BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(0, 2), header.CenterCellX);
            BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(2, 2), header.CenterCellZ);
            BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(4, 2), header.QuantizedTimeScaleDeci);
            dst[6] = (byte)header.QuantizedEnsoAnomalyDeciK;
            dst[7] = header.ChangedCellCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AtmosphereAoiPacketHeader ReadHeader(ReadOnlySpan<byte> src)
        {
            short cx = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(0, 2));
            short cz = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(2, 2));
            ushort scaleDeci = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4, 2));
            sbyte ensoDeci = (sbyte)src[6];
            byte count = src[7];
            return new AtmosphereAoiPacketHeader(cx, cz, scaleDeci * 0.1f, ensoDeci * 0.1f, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteCellDelta(in AtmosphereAoiCellDelta d, Span<byte> dst)
        {
            dst[0] = d.CellIndex0To80;
            dst[1] = d.PackedKindAndHazards;
            BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(2, 2), d.AirTempDeciC);
            BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(4, 2), d.StationPressureDeciHpa);
            BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(6, 2), d.CloudWaterCentiKgPerM2);
            BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(8, 2), d.PrecipDeciMmPerHour);
            dst[10] = (byte)d.WindXHalfMps;
            dst[11] = (byte)d.WindZHalfMps;
            dst[12] = d.CloudBase16m;
            dst[13] = d.CloudThickness32m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AtmosphereAoiCellDelta ReadCellDelta(ReadOnlySpan<byte> src)
        {
            return new AtmosphereAoiCellDelta(
                cellIndex0To80: src[0],
                packedKindAndHazards: src[1],
                airTempDeciC: BinaryPrimitives.ReadInt16LittleEndian(src.Slice(2, 2)),
                stationPressureDeciHpa: BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(4, 2)),
                cloudWaterCentiKgPerM2: BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(6, 2)),
                precipDeciMmPerHour: BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(8, 2)),
                windXHalfMps: (sbyte)src[10],
                windZHalfMps: (sbyte)src[11],
                cloudBase16m: src[12],
                cloudThickness32m: src[13]);
        }
    }
}
