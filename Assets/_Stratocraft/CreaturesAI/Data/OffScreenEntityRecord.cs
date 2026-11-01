#nullable enable
using System;
using System.Runtime.InteropServices;
using Stratocraft.Core.Math;
using Stratocraft.CreaturesAI.CreaturesAndBosses;

namespace Stratocraft.CreaturesAI.Data
{
    /// <summary>
    /// MATEMATIKSEL RAPOR Bolum 1.4 — 64-Byte (1 CPU Cache Line) Gorus Disi Canli / Suru Kaydi:
    /// - Q20.12 sabit noktali koordinatlar (0.244 mm hassasiyet, 0 float drift).
    /// - 0 Hz Tembel Analitik DES (Discrete Event Simulation) Min-Heap siralamasi.
    /// - AtomicStateLock ile atomik CAS (Compare-And-Swap) cift-hasar ve terfi yarisi onleme.
    /// - 10.000 varlik yalnizca 640 KB RAM tuketir!
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct OffScreenEntityRecord : IEquatable<OffScreenEntityRecord>
    {
        [FieldOffset(0)]  public uint EntityGuid;
        [FieldOffset(4)]  public int OriginPosX_Q20_12;
        [FieldOffset(8)]  public int OriginPosY_Q20_12;
        [FieldOffset(12)] public int OriginPosZ_Q20_12;
        [FieldOffset(16)] public int TargetPosX_Q20_12;
        [FieldOffset(20)] public int TargetPosY_Q20_12;
        [FieldOffset(24)] public int TargetPosZ_Q20_12;
        [FieldOffset(28)] public double DepartureTimeSec;
        [FieldOffset(36)] public double NextEventTimeSec;
        [FieldOffset(44)] public ushort EffectiveSpeedQ8_8; // 0..255.99 m/s (1/256 m/s)
        [FieldOffset(46)] public uint CurrentTotalHp;
        [FieldOffset(50)] public ushort ClanIdAndArchetype; // Bits[0..9]=ClanID, Bits[10..15]=CreatureId (1..63)
        [FieldOffset(52)] public ushort AnatomyAndUnitCount; // Tekil varlikta AnatomyCapabilityFlags, surude birim adedi
        [FieldOffset(54)] public ushort BoundChunkNavVer;
        [FieldOffset(56)] public int AtomicStateLock; // 0=OFFSCREEN, 1=PROMOTING, 2=ACTIVE
        [FieldOffset(60)] public int DesHeapIndex;

        public const int StateOffscreen = 0;
        public const int StatePromoting = 1;
        public const int StateActive = 2;

        public float MoveSpeedMps => EffectiveSpeedQ8_8 * (1.0f / 256.0f);

        public ushort ClanId => (ushort)(ClanIdAndArchetype & 0x3FF);
        public byte CreatureId => (byte)((ClanIdAndArchetype >> 10) & 0x3F);

        public static OffScreenEntityRecord Create(
            uint guid,
            float startX, float startY, float startZ,
            float targetX, float targetY, float targetZ,
            double departureTimeSec,
            float speedMps,
            uint maxHp,
            byte creatureId,
            ushort clanId,
            AnatomyCapabilityFlags capabilities,
            ushort navVersion)
        {
            var record = new OffScreenEntityRecord
            {
                EntityGuid = guid,
                OriginPosX_Q20_12 = FixedQ20_12.FromFloat(startX).Raw,
                OriginPosY_Q20_12 = FixedQ20_12.FromFloat(startY).Raw,
                OriginPosZ_Q20_12 = FixedQ20_12.FromFloat(startZ).Raw,
                TargetPosX_Q20_12 = FixedQ20_12.FromFloat(targetX).Raw,
                TargetPosY_Q20_12 = FixedQ20_12.FromFloat(targetY).Raw,
                TargetPosZ_Q20_12 = FixedQ20_12.FromFloat(targetZ).Raw,
                DepartureTimeSec = departureTimeSec,
                NextEventTimeSec = departureTimeSec,
                EffectiveSpeedQ8_8 = (ushort)Math.Clamp((int)MathF.Round(speedMps * 256.0f), 0, 65535),
                CurrentTotalHp = maxHp,
                ClanIdAndArchetype = (ushort)((clanId & 0x3FF) | ((creatureId & 0x3F) << 10)),
                AnatomyAndUnitCount = (ushort)((uint)capabilities & 0xFFFF),
                BoundChunkNavVer = navVersion,
                AtomicStateLock = StateOffscreen,
                DesHeapIndex = -1
            };

            // Hedefe varis suresini hesaplayip NextEventTimeSec alanina yaz
            float dx = targetX - startX;
            float dy = targetY - startY;
            float dz = targetZ - startZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            float duration = speedMps > 0.01f ? dist / speedMps : 0f;
            record.NextEventTimeSec = departureTimeSec + duration;

            return record;
        }

        public (float x, float y, float z) EvaluatePositionAtTime(double queryTimeSec)
        {
            if (queryTimeSec <= DepartureTimeSec)
            {
                return (new FixedQ20_12(OriginPosX_Q20_12).ToFloat(),
                        new FixedQ20_12(OriginPosY_Q20_12).ToFloat(),
                        new FixedQ20_12(OriginPosZ_Q20_12).ToFloat());
            }

            if (queryTimeSec >= NextEventTimeSec)
            {
                return (new FixedQ20_12(TargetPosX_Q20_12).ToFloat(),
                        new FixedQ20_12(TargetPosY_Q20_12).ToFloat(),
                        new FixedQ20_12(TargetPosZ_Q20_12).ToFloat());
            }

            double totalDuration = NextEventTimeSec - DepartureTimeSec;
            float t = totalDuration > 1e-6 ? (float)((queryTimeSec - DepartureTimeSec) / totalDuration) : 1.0f;
            t = Math.Clamp(t, 0.0f, 1.0f);

            float ox = new FixedQ20_12(OriginPosX_Q20_12).ToFloat();
            float oy = new FixedQ20_12(OriginPosY_Q20_12).ToFloat();
            float oz = new FixedQ20_12(OriginPosZ_Q20_12).ToFloat();

            float tx = new FixedQ20_12(TargetPosX_Q20_12).ToFloat();
            float ty = new FixedQ20_12(TargetPosY_Q20_12).ToFloat();
            float tz = new FixedQ20_12(TargetPosZ_Q20_12).ToFloat();

            return (ox + (tx - ox) * t, oy + (ty - oy) * t, oz + (tz - oz) * t);
        }

        public bool Equals(OffScreenEntityRecord other) => EntityGuid == other.EntityGuid;
        public override bool Equals(object? obj) => obj is OffScreenEntityRecord other && Equals(other);
        public override int GetHashCode() => (int)EntityGuid;
    }
}
