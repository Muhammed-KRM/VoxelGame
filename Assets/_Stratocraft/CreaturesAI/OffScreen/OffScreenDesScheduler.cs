#nullable enable
using System;
using Stratocraft.CreaturesAI.Data;

namespace Stratocraft.CreaturesAI.OffScreen
{
    public enum OffScreenDesEventKind : byte
    {
        WaypointArrival = 0,
        EncounterCheck = 1,
        WorldMutationWork = 2,
        EcologyMacroTick = 3
    }

    public readonly struct ScheduledDesEvent
    {
        public readonly int SlotId;
        public readonly double EventTimeSec;
        public readonly OffScreenDesEventKind Kind;
        public readonly int SecondarySlotId;

        public ScheduledDesEvent(int slotId, double eventTimeSec, OffScreenDesEventKind kind, int secondarySlotId = -1)
        {
            SlotId = slotId;
            EventTimeSec = eventTimeSec;
            Kind = kind;
            SecondarySlotId = secondarySlotId;
        }
    }

    /// <summary>
    /// Paket 4C-3: OffScreenEntityRecord.DesHeapIndex ile birebir senkronize O(log N) Ikili Min-Heap DES Zamanlayicisi.
    /// 0 Hz tembel analitik zamanlama; kararli durumda 0 B/kare GC.
    /// </summary>
    public sealed class OffScreenDesScheduler
    {
        private int[] _heapSlots;
        private double[] _heapTimes;
        private byte[] _heapKinds;
        private int[] _heapSecondarySlots;
        private int _count;

        public int Count => _count;

        public OffScreenDesScheduler(int initialCapacity = 256)
        {
            int cap = Math.Max(32, initialCapacity);
            _heapSlots = new int[cap];
            _heapTimes = new double[cap];
            _heapKinds = new byte[cap];
            _heapSecondarySlots = new int[cap];
        }

        public double PeekNextEventTimeSec()
        {
            return _count > 0 ? _heapTimes[0] : double.PositiveInfinity;
        }

        public void ScheduleOrUpdate(
            int slotId,
            OffScreenEntityRecord[] records,
            double eventTimeSec,
            OffScreenDesEventKind kind,
            int secondarySlotId = -1)
        {
            if ((uint)slotId >= (uint)records.Length) return;

            int existingIdx = records[slotId].DesHeapIndex;
            if ((uint)existingIdx < (uint)_count && _heapSlots[existingIdx] == slotId)
            {
                double oldTime = _heapTimes[existingIdx];
                _heapTimes[existingIdx] = eventTimeSec;
                _heapKinds[existingIdx] = (byte)kind;
                _heapSecondarySlots[existingIdx] = secondarySlotId;
                if (eventTimeSec < oldTime)
                {
                    SiftUp(existingIdx, records);
                }
                else if (eventTimeSec > oldTime)
                {
                    SiftDown(existingIdx, records);
                }
                return;
            }

            EnsureCapacity(_count + 1);
            int idx = _count++;
            _heapSlots[idx] = slotId;
            _heapTimes[idx] = eventTimeSec;
            _heapKinds[idx] = (byte)kind;
            _heapSecondarySlots[idx] = secondarySlotId;
            records[slotId].DesHeapIndex = idx;
            SiftUp(idx, records);
        }

        public bool Remove(int slotId, OffScreenEntityRecord[] records)
        {
            if ((uint)slotId >= (uint)records.Length) return false;
            int idx = records[slotId].DesHeapIndex;
            if ((uint)idx >= (uint)_count || _heapSlots[idx] != slotId)
            {
                records[slotId].DesHeapIndex = -1;
                return false;
            }

            RemoveAtIndex(idx, records);
            return true;
        }

        public bool TryPopDue(double currentSimTimeSec, OffScreenEntityRecord[] records, out ScheduledDesEvent ev)
        {
            if (_count <= 0 || _heapTimes[0] > currentSimTimeSec)
            {
                ev = default;
                return false;
            }

            ev = new ScheduledDesEvent(
                _heapSlots[0],
                _heapTimes[0],
                (OffScreenDesEventKind)_heapKinds[0],
                _heapSecondarySlots[0]);

            RemoveAtIndex(0, records);
            return true;
        }

        public void Clear(OffScreenEntityRecord[] records)
        {
            for (int i = 0; i < _count; i++)
            {
                int slot = _heapSlots[i];
                if ((uint)slot < (uint)records.Length)
                {
                    records[slot].DesHeapIndex = -1;
                }
            }
            _count = 0;
        }

        private void RemoveAtIndex(int idx, OffScreenEntityRecord[] records)
        {
            int removedSlot = _heapSlots[idx];
            if ((uint)removedSlot < (uint)records.Length)
            {
                records[removedSlot].DesHeapIndex = -1;
            }

            int last = --_count;
            if (idx == last)
            {
                return;
            }

            double removedTime = _heapTimes[idx];
            int movedSlot = _heapSlots[last];
            double movedTime = _heapTimes[last];
            _heapSlots[idx] = movedSlot;
            _heapTimes[idx] = movedTime;
            _heapKinds[idx] = _heapKinds[last];
            _heapSecondarySlots[idx] = _heapSecondarySlots[last];
            if ((uint)movedSlot < (uint)records.Length)
            {
                records[movedSlot].DesHeapIndex = idx;
            }

            if (movedTime < removedTime)
            {
                SiftUp(idx, records);
            }
            else
            {
                SiftDown(idx, records);
            }
        }

        private void SiftUp(int idx, OffScreenEntityRecord[] records)
        {
            while (idx > 0)
            {
                int parent = (idx - 1) >> 1;
                if (_heapTimes[idx] >= _heapTimes[parent])
                {
                    break;
                }
                Swap(idx, parent, records);
                idx = parent;
            }
        }

        private void SiftDown(int idx, OffScreenEntityRecord[] records)
        {
            while (true)
            {
                int left = (idx << 1) + 1;
                if (left >= _count) break;
                int right = left + 1;
                int smallest = (right < _count && _heapTimes[right] < _heapTimes[left]) ? right : left;
                if (_heapTimes[idx] <= _heapTimes[smallest])
                {
                    break;
                }
                Swap(idx, smallest, records);
                idx = smallest;
            }
        }

        private void Swap(int a, int b, OffScreenEntityRecord[] records)
        {
            int sa = _heapSlots[a];
            int sb = _heapSlots[b];
            _heapSlots[a] = sb;
            _heapSlots[b] = sa;

            (_heapTimes[a], _heapTimes[b]) = (_heapTimes[b], _heapTimes[a]);
            (_heapKinds[a], _heapKinds[b]) = (_heapKinds[b], _heapKinds[a]);
            (_heapSecondarySlots[a], _heapSecondarySlots[b]) = (_heapSecondarySlots[b], _heapSecondarySlots[a]);

            if ((uint)sa < (uint)records.Length) records[sa].DesHeapIndex = b;
            if ((uint)sb < (uint)records.Length) records[sb].DesHeapIndex = a;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _heapSlots.Length) return;
            int nextCap = Math.Max(required, _heapSlots.Length * 2);
            Array.Resize(ref _heapSlots, nextCap);
            Array.Resize(ref _heapTimes, nextCap);
            Array.Resize(ref _heapKinds, nextCap);
            Array.Resize(ref _heapSecondarySlots, nextCap);
        }
    }
}
