using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.NativeBridge;

namespace Stratocraft.Networking
{
    /// <summary>
    /// LiteNetLib Kanal 1 uzerinden gonderilen LZ4 sikistirmali ve XXH64 saglama toplamli chunk paketi.
    /// Bkz. STRATOCRAFT_GELISTIRICI_DOKUMANI Bolum 4.7.
    /// </summary>
    public readonly struct VoxelChunkCompressedPacket
    {
        /// <summary>Chunk X koordinati.</summary>
        public readonly int ChunkX;

        /// <summary>Chunk Z koordinati.</summary>
        public readonly int ChunkZ;

        /// <summary>Sikistirilmamis bayt boyutu.</summary>
        public readonly uint UncompressedSize;

        /// <summary>XXH64 saglama toplami.</summary>
        public readonly ulong Xxh64Checksum;

        /// <summary>LZ4 ile sikistirilmis yuk.</summary>
        public readonly byte[] Lz4Payload;

        /// <summary>Yeni bir sikistirilmis voxel chunk ag paketi olusturur.</summary>
        public VoxelChunkCompressedPacket(
            int chunkX,
            int chunkZ,
            uint uncompressedSize,
            ulong xxh64Checksum,
            byte[] lz4Payload)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            UncompressedSize = uncompressedSize;
            Xxh64Checksum = xxh64Checksum;
            Lz4Payload = lz4Payload;
        }
    }

    /// <summary>
    /// Aktif voxel chunk'larini C++20 LZ4 + XXH64 ile paketleyip acan ag akis yardimcisi.
    /// </summary>
    public static class LiteNetLibVoxelStreamer
    {
        /// <summary>65.536 VoxelCell dizisini LZ4 + XXH64 paketi olarak sikistirir.</summary>
        public static unsafe VoxelChunkCompressedPacket CompressChunkPacket(int chunkX, int chunkZ, ReadOnlySpan<uint> voxels65536)
        {
            int srcByteLen = voxels65536.Length * sizeof(uint);
            int maxDstCapacity = srcByteLen + 4096;
            byte[] tempCompressed = new byte[maxDstCapacity];
            ulong checksum = 0;
            int compressedLen;

            fixed (uint* srcPtr = voxels65536)
            fixed (byte* dstPtr = tempCompressed)
            {
                compressedLen = StratocraftNativeBindings.CompressChunkLz4(
                    srcPtr, srcByteLen, dstPtr, maxDstCapacity, &checksum);
            }

            if (compressedLen <= 0)
            {
                throw new InvalidOperationException("Chunk LZ4 sikistirma basarisiz oldu.");
            }

            byte[] payload = new byte[compressedLen];
            Buffer.BlockCopy(tempCompressed, 0, payload, 0, compressedLen);
            return new VoxelChunkCompressedPacket(chunkX, chunkZ, (uint)srcByteLen, checksum, payload);
        }

        /// <summary>LZ4 + XXH64 paketini dogrulayip 65.536 VoxelCell dizisine acar.</summary>
        public static unsafe bool TryDecompressChunkPacket(in VoxelChunkCompressedPacket packet, Span<uint> outVoxels65536)
        {
            int dstByteCapacity = outVoxels65536.Length * sizeof(uint);
            if (dstByteCapacity < packet.UncompressedSize)
            {
                return false;
            }

            fixed (byte* srcPtr = packet.Lz4Payload)
            fixed (uint* dstPtr = outVoxels65536)
            {
                int res = StratocraftNativeBindings.DecompressChunkLz4(
                    srcPtr,
                    packet.Lz4Payload.Length,
                    dstPtr,
                    dstByteCapacity,
                    packet.Xxh64Checksum);
                return res == (int)packet.UncompressedSize;
            }
        }
    }
}
