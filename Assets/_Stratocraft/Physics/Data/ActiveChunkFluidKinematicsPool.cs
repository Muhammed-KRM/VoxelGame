#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Yalnizca aktif (!AllSettled) akiskan chunk'lari icin kiralanan sifir-GC kinematik yan-tampon dilimi.
    /// FluidCell16 (2-Byte) sozlesmesine dokunmadan onceki kutle ve Q7 yatay hiz vektorunu tasir.
    /// Bellek ayak izi: 3 * 65,536 Byte = 192 KB / aktif chunk.
    /// </summary>
    public sealed class ActiveChunkFluidKinematicsSlice : IFluidKinematicsSlice
    {
        public const int MaxSliceVolume = 16 * 256 * 16; // 65,536 hucre
        public const float MaxVelocityMetersPerSec = 4.0f;
        public const float InvQ7Scale = MaxVelocityMetersPerSec / 127.0f;
        public const float ToQ7Scale = 127.0f / MaxVelocityMetersPerSec;

        public byte[] PrevMass { get; }
        public sbyte[] VelX_Q7 { get; }
        public sbyte[] VelZ_Q7 { get; }

        public int ChunkX { get; internal set; }
        public int ChunkZ { get; internal set; }
        public int LastTouchedTickIndex { get; internal set; }
        public bool IsInitializedFromCurrentMass { get; internal set; }

        public ActiveChunkFluidKinematicsSlice(int sliceVolume = MaxSliceVolume)
        {
            if (sliceVolume <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceVolume), "Slice volume must be positive.");
            }

            PrevMass = new byte[sliceVolume];
            VelX_Q7 = new sbyte[sliceVolume];
            VelZ_Q7 = new sbyte[sliceVolume];
        }

        /// <summary>
        /// Yeni uyanan duragan bir chunk icin PrevMass dizisini mevcut FluidCell16 kutlesiyle esitler;
        /// boylece ilk uyandigi tikte duragan su yuzeyi sifirdan yukari sicramaz.
        /// </summary>
        public void CaptureBaselineFromFluids(ReadOnlySpan<ushort> fluids, int activeVolume)
        {
            int count = Math.Min(activeVolume, Math.Min(fluids.Length, PrevMass.Length));
            for (int i = 0; i < count; i++)
            {
                ushort raw = fluids[i];
                PrevMass[i] = (byte)(raw & 0xFFu);
                VelX_Q7[i] = 0;
                VelZ_Q7[i] = 0;
            }

            IsInitializedFromCurrentMass = true;
        }

        /// <summary>
        /// Sabit akiskan tikinin basinda mevcut kutleleri PrevMass tamponuna kopyalar ve bosalan hucrelerin hizini sifirlar.
        /// </summary>
        public void SnapshotPrevMassBeforeStep(ReadOnlySpan<ushort> fluids, int activeVolume)
        {
            int count = Math.Min(activeVolume, Math.Min(fluids.Length, PrevMass.Length));
            for (int i = 0; i < count; i++)
            {
                ushort raw = fluids[i];
                byte mass = (byte)(raw & 0xFFu);
                PrevMass[i] = mass;
                if (mass == 0)
                {
                    VelX_Q7[i] = 0;
                    VelZ_Q7[i] = 0;
                }
            }

            IsInitializedFromCurrentMass = true;
        }

        /// <summary>
        /// Dilimi havuza iade ederken tamponlari sifirlar.
        /// </summary>
        public void ClearAll(int activeVolume)
        {
            int count = Math.Min(activeVolume, PrevMass.Length);
            if (count > 0)
            {
                Array.Clear(PrevMass, 0, count);
                Array.Clear(VelX_Q7, 0, count);
                Array.Clear(VelZ_Q7, 0, count);
            }

            IsInitializedFromCurrentMass = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte EncodeVelocityQ7(float velocityMps)
        {
            if (!float.IsFinite(velocityMps))
            {
                return 0;
            }

            int q = (int)MathF.Round(Math.Clamp(velocityMps, -MaxVelocityMetersPerSec, MaxVelocityMetersPerSec) * ToQ7Scale);
            return (sbyte)Math.Clamp(q, -127, 127);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecodeVelocityQ7(sbyte encodedQ7) => encodedQ7 * InvQ7Scale;
    }

    /// <summary>
    /// Aktif akiskan chunk'lari icin sifir-GC kinematik dilim havuzu.
    /// Varsayilan 32 dilim (6.0 MB) on-tahsisli baslar; tavan 64 dilim (12.0 MB) ile sinirlidir.
    /// Dilim tavani asildiginda null donerek statik CurrMass fallback'ini korur.
    /// </summary>
    public sealed class ActiveChunkFluidKinematicsPool
    {
        public const int DefaultPrewarmSlices = 32;
        public const int HardMaxSlices = 64;

        private readonly Stack<ActiveChunkFluidKinematicsSlice> _freeStack;
        private readonly Dictionary<(int cx, int cz), ActiveChunkFluidKinematicsSlice> _leasedByChunk;
        private readonly int _sliceVolume;
        private int _totalAllocatedSlices;

        public int LeasedCount => _leasedByChunk.Count;
        public int FreeCount => _freeStack.Count;
        public int TotalAllocatedSlices => _totalAllocatedSlices;
        public int SliceVolume => _sliceVolume;

        public ActiveChunkFluidKinematicsPool(
            int prewarmSlices = DefaultPrewarmSlices,
            int sliceVolume = ActiveChunkFluidKinematicsSlice.MaxSliceVolume)
        {
            if (sliceVolume <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceVolume), "Slice volume must be positive.");
            }

            _sliceVolume = sliceVolume;
            int initial = Math.Clamp(prewarmSlices, 0, HardMaxSlices);
            _freeStack = new Stack<ActiveChunkFluidKinematicsSlice>(HardMaxSlices);
            _leasedByChunk = new Dictionary<(int cx, int cz), ActiveChunkFluidKinematicsSlice>(HardMaxSlices);

            for (int i = 0; i < initial; i++)
            {
                _freeStack.Push(new ActiveChunkFluidKinematicsSlice(_sliceVolume));
                _totalAllocatedSlices++;
            }
        }

        public bool TryGetLeased(int cx, int cz, out ActiveChunkFluidKinematicsSlice? slice) =>
            _leasedByChunk.TryGetValue((cx, cz), out slice);

        public ActiveChunkFluidKinematicsSlice? RentOrGetAndSyncBaseline(
            int cx,
            int cz,
            ReadOnlySpan<ushort> currentFluids,
            int activeVolume,
            int currentTickIndex)
        {
            if (_leasedByChunk.TryGetValue((cx, cz), out ActiveChunkFluidKinematicsSlice? existing))
            {
                existing.LastTouchedTickIndex = currentTickIndex;
                if (!existing.IsInitializedFromCurrentMass)
                {
                    existing.CaptureBaselineFromFluids(currentFluids, activeVolume);
                }

                return existing;
            }

            ActiveChunkFluidKinematicsSlice slice;
            if (_freeStack.Count > 0)
            {
                slice = _freeStack.Pop();
            }
            else if (_totalAllocatedSlices < HardMaxSlices)
            {
                slice = new ActiveChunkFluidKinematicsSlice(_sliceVolume);
                _totalAllocatedSlices++;
            }
            else
            {
                return null;
            }

            slice.ChunkX = cx;
            slice.ChunkZ = cz;
            slice.LastTouchedTickIndex = currentTickIndex;
            slice.CaptureBaselineFromFluids(currentFluids, activeVolume);
            _leasedByChunk[(cx, cz)] = slice;
            return slice;
        }

        public void ReleaseChunk(int cx, int cz)
        {
            if (_leasedByChunk.Remove((cx, cz), out ActiveChunkFluidKinematicsSlice? slice))
            {
                slice.ClearAll(_sliceVolume);
                _freeStack.Push(slice);
            }
        }
    }
}
