#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.CreaturesAI.HybridLod;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;

namespace Stratocraft.CreaturesAI.OffScreen
{
    /// <summary>
    /// Paket 4C-3: 128-Chunk LRU Sabit Bellekli (512 KB, 0 B/kare GC) Ekran-Disi NavSpan Onbellegi.
    /// - Chunk unload edilirken 256-Bit Bitboard ile 4-katmanli NavSpanColumn4Layer[256] yakalar (< 30 us).
    /// - Ekran-disi yaratiklari OffScreenNavSpanStepJob.ExecuteMultiLayerStep ile magara/kopru/mayin/sutun
    ///   farkindaligiyla yurutur; hic ziyaret edilmemis chunk'larda deterministik WorldGen yuzeyini kullanir.
    /// </summary>
    public sealed class OffScreenNavSpanCache
    {
        public const int MaxCachedChunks = 128;

        private readonly NavSpanColumn4Layer[][] _grids = new NavSpanColumn4Layer[MaxCachedChunks][];
        private readonly (int cx, int cz)[] _slotCoords = new (int, int)[MaxCachedChunks];
        private readonly ushort[] _slotNavVersions = new ushort[MaxCachedChunks];
        private readonly uint[] _slotLastUseTick = new uint[MaxCachedChunks];
        private readonly bool[] _slotOccupied = new bool[MaxCachedChunks];
        private readonly Dictionary<(int cx, int cz), int> _coordToSlot = new(MaxCachedChunks);
        private uint _lruClock;
        private int _occupiedCount;

        public int CachedChunkCount => _occupiedCount;

        public OffScreenNavSpanCache()
        {
            for (int i = 0; i < MaxCachedChunks; i++)
            {
                _grids[i] = new NavSpanColumn4Layer[NavSpanRebuildJob.ColumnsPerChunk];
            }
        }

        public int CaptureFromLoadedChunk(
            int cx,
            int cz,
            ReadOnlySpan<uint> voxels,
            ReadOnlySpan<ushort> fluids,
            int maxEditableY,
            ushort clanId = 0,
            ushort navVersion = 1)
        {
            var key = (cx, cz);
            int slot;
            if (!_coordToSlot.TryGetValue(key, out slot))
            {
                if (_occupiedCount < MaxCachedChunks)
                {
                    slot = _occupiedCount++;
                }
                else
                {
                    // LRU tahliye (0 GC)
                    slot = 0;
                    uint oldest = uint.MaxValue;
                    for (int i = 0; i < MaxCachedChunks; i++)
                    {
                        if (_slotLastUseTick[i] < oldest)
                        {
                            oldest = _slotLastUseTick[i];
                            slot = i;
                        }
                    }
                    if (_slotOccupied[slot])
                    {
                        _coordToSlot.Remove(_slotCoords[slot]);
                    }
                }
                _coordToSlot[key] = slot;
                _slotCoords[slot] = key;
                _slotOccupied[slot] = true;
            }

            _slotNavVersions[slot] = navVersion;
            _slotLastUseTick[slot] = ++_lruClock;
            NavSpanRebuildJob.ExecuteMultiLayer(voxels, fluids, maxEditableY, clanId, _grids[slot], out int totalSpans);
            return totalSpans;
        }

        public bool TryGetChunkGrid(int cx, int cz, out ReadOnlySpan<NavSpanColumn4Layer> grid256, out ushort navVersion)
        {
            if (_coordToSlot.TryGetValue((cx, cz), out int slot))
            {
                _slotLastUseTick[slot] = ++_lruClock;
                grid256 = _grids[slot];
                navVersion = _slotNavVersions[slot];
                return true;
            }

            grid256 = ReadOnlySpan<NavSpanColumn4Layer>.Empty;
            navVersion = 1;
            return false;
        }

        public void StepWaypointSegment(
            float startX,
            float startY,
            float startZ,
            float goalX,
            float goalZ,
            float speedMps,
            float stepDt,
            byte requiredHeadroom,
            bool lavaImmune,
            bool isFlying,
            out float outX,
            out float outY,
            out float outZ,
            out ushort outNavVersion,
            out byte outHazardOrPillar)
        {
            outX = startX;
            outY = startY;
            outZ = startZ;
            outNavVersion = 1;
            outHazardOrPillar = 0;

            float dx = goalX - startX;
            float dz = goalZ - startZ;
            float distSq = dx * dx + dz * dz;
            if (distSq < 1e-4f || speedMps <= 0.01f || stepDt <= 0.0f)
            {
                return;
            }

            float dist = MathF.Sqrt(distSq);
            float step = MathF.Min(dist, speedMps * stepDt);
            float rawNextX = startX + (dx / dist) * step;
            float rawNextZ = startZ + (dz / dist) * step;

            int curCx = FloorDiv16((int)MathF.Floor(startX));
            int curCz = FloorDiv16((int)MathF.Floor(startZ));
            int dstCx = FloorDiv16((int)MathF.Floor(rawNextX));
            int dstCz = FloorDiv16((int)MathF.Floor(rawNextZ));

            if (!isFlying && TryGetChunkGrid(curCx, curCz, out ReadOnlySpan<NavSpanColumn4Layer> grid, out ushort navVer))
            {
                outNavVersion = navVer;
                float chunkBaseX = curCx * 16.0f;
                float chunkBaseZ = curCz * 16.0f;

                // Tek chunk icindeyse <= 0.85m alt-adimlarla OffScreenNavSpanStepJob.ExecuteMultiLayerStep kullan
                // (uzun dt adiminda lav/duvar/mayin ustunden tunelleme yapilmasini %100 engeller!)
                if (dstCx == curCx && dstCz == curCz)
                {
                    Span<OffScreenNavSpanStepJob.OffScreenMultiLayerAgentState> one = stackalloc OffScreenNavSpanStepJob.OffScreenMultiLayerAgentState[1];
                    one[0] = new OffScreenNavSpanStepJob.OffScreenMultiLayerAgentState
                    {
                        WorldX = Math.Clamp(startX - chunkBaseX, 0.0f, 15.99f),
                        WorldY = startY,
                        WorldZ = Math.Clamp(startZ - chunkBaseZ, 0.0f, 15.99f),
                        GoalWorldX = Math.Clamp(rawNextX - chunkBaseX, 0.0f, 15.99f),
                        GoalWorldZ = Math.Clamp(rawNextZ - chunkBaseZ, 0.0f, 15.99f),
                        MoveSpeedMps = speedMps,
                        RequiredClearanceBlocks = Math.Max((byte)1, requiredHeadroom),
                        LavaImmune = lavaImmune ? (byte)1 : (byte)0
                    };
                    int subSteps = Math.Clamp((int)MathF.Ceiling(step / 0.85f), 1, 24);
                    float subDt = stepDt / subSteps;
                    byte maxHazard = 0;
                    for (int s = 0; s < subSteps; s++)
                    {
                        float prevX = one[0].WorldX;
                        float prevZ = one[0].WorldZ;
                        OffScreenNavSpanStepJob.ExecuteMultiLayerStep(grid, one, subDt);
                        if (one[0].ReachedPillarOrMine > maxHazard) maxHazard = one[0].ReachedPillarOrMine;
                        if (one[0].ReachedPillarOrMine == 2) break;
                        float movedSq = (one[0].WorldX - prevX) * (one[0].WorldX - prevX) +
                                        (one[0].WorldZ - prevZ) * (one[0].WorldZ - prevZ);
                        if (movedSq < 1e-6f) break; // Lav / ucurum / tavan engeli -> dur!
                    }
                    outX = chunkBaseX + one[0].WorldX;
                    outY = one[0].WorldY;
                    outZ = chunkBaseZ + one[0].WorldZ;
                    outHazardOrPillar = maxHazard;
                    return;
                }

                // Chunk sinirindan geciste hedef chunk onbellekteyse oradaki surekli katmani kontrol et
                if (TryGetChunkGrid(dstCx, dstCz, out ReadOnlySpan<NavSpanColumn4Layer> dstGrid, out ushort dstNavVer))
                {
                    outNavVersion = dstNavVer;
                    int lx = Math.Clamp((int)MathF.Floor(rawNextX) - dstCx * 16, 0, 15);
                    int lz = Math.Clamp((int)MathF.Floor(rawNextZ) - dstCz * 16, 0, 15);
                    if (dstGrid[(lz << 4) | lx].TryFindContinuousLayer(
                        startY, 2.5f, Math.Max((byte)1, requiredHeadroom), lavaImmune,
                        out NavSpanRecord32 matchedSpan, out _))
                    {
                        outX = rawNextX;
                        outY = matchedSpan.FloorBlockY;
                        outZ = rawNextZ;
                        outHazardOrPillar = matchedSpan.HasStaticHazardOrPillar ? (byte)1 : (byte)0;
                        return;
                    }
                    // Hedef chunk'ta lav/ucurum varsa su anki konumda kal
                    return;
                }
            }

            // Onbellekte olmayan (hic ziyaret edilmemis) uzak chunk'ta analitik WorldGen yuzeyini ornekle
            float surfaceY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(rawNextX, rawNextZ);
            if (!isFlying && surfaceY < StratocraftFloatingIslandWorldGen.SeaLevelY - 1.5f)
            {
                // Kara yaratigi derin okyanusa girmez
                return;
            }

            outX = rawNextX;
            outZ = rawNextZ;
            outY = isFlying ? MathF.Max(surfaceY + 10.0f, startY) : (surfaceY + 0.5f);
        }

        public void Clear()
        {
            _coordToSlot.Clear();
            Array.Clear(_slotOccupied, 0, _slotOccupied.Length);
            Array.Clear(_slotLastUseTick, 0, _slotLastUseTick.Length);
            _occupiedCount = 0;
            _lruClock = 0;
        }

        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v - 1) >> 4) + 1);
    }
}
