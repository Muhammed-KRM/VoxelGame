using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// 16x256x16 (65.536 voxel) dikey chunk sutunu ve 16 adet 16x16x16 Sub-Chunk bileseni.
    /// Her Sub-Chunk kendi 16 KB (4.096 VoxelCell) dilimine, 0-bit Uniform sikistirma bayragina,
    /// bagimsiz MicroVoxelPool4096 havuzuna ve chunk-geneli 256B SkyOcclusionY haritasina sahiptir.
    /// </summary>
    public sealed class VoxelChunkComponent : IDisposable
    {
        /// <summary>Chunk X koordinati (dunya X &gt;&gt; 4).</summary>
        public int ChunkX { get; }

        /// <summary>Chunk Z koordinati (dunya Z &gt;&gt; 4).</summary>
        public int ChunkZ { get; }

        /// <summary>Floating Origin kaydirmalari sonrasi guncel gorsel dunya konumu (X, Y, Z metre).</summary>
        public (float X, float Y, float Z) VisualWorldPosition { get; set; }

        /// <summary>65.536 adet 32-bit VoxelCell dizisi (16 adet 16x16x16 Sub-Chunk).</summary>
        public uint[] Voxels { get; }

        /// <summary>65.536 adet 16-bit FluidCell16 dizisi.</summary>
        public ushort[] Fluids { get; }

        /// <summary>16 adet dikey Sub-Chunk (Y=0..15) icin kirli (yeniden meshleme gerekiyor) bit maskesi.</summary>
        public ushort DirtySubChunkMask { get; set; }

        /// <summary>16 adet dikey Sub-Chunk (Y=0..15) icin sıvı yuzeyi kirli bit maskesi.</summary>
        public ushort DirtyFluidSubChunkMask { get; set; }

        /// <summary>
        /// 0-bit Uniform Sub-Chunk Palet Maskesi: Bit=1 ise ilgili 16x16x16 Sub-Chunk'taki 4.096 hucrenin
        /// tamami tek bir degerden (ornegin saf Hava veya saf Tas) olusur ve ic yuzey uretmez.
        /// </summary>
        public ushort UniformSubChunkMask { get; private set; }

        /// <summary>Chunk basina 256 Byte (16x16) dikey gokyuzu tikaniklik haritasi (O(1) Oksijen BFS acik-gokyuzu testi).</summary>
        public ChunkSkyOcclusionMap256 SkyOcclusionMap { get; }

        /// <summary>Blok koyma/kirma veya Domino yikimi sonrasi artan NavSpan surum sayaci (0 Hz Off-Screen AI NavVersion dogrulamasi).</summary>
        public ushort NavVersion { get; private set; } = 1;

        /// <summary>Chunk diske kaydedilmeyi bekleyen degisiklik iceriyor mu?</summary>
        public bool IsPersistenceDirty { get; set; }

        /// <summary>Son ziyaret edilen oyun gunu.</summary>
        public uint LastVisitedGameDay { get; set; }

        /// <summary>Son ziyaret edilen gercek UTC zamani (Unix Epoch'tan itibaren dakika — 14 Gercek Gun = 20.160 dk).</summary>
        public uint LastVisitedUtcMinutes { get; set; }

        /// <summary>Chunk klan siniri icinde mi?</summary>
        public bool IsInsideClan { get; set; }

        /// <summary>Chunk icinde aktif oyuncu yapisi (OriginState &gt;= 1), sandik veya batarya var mi?</summary>
        public bool HasActiveStructures { get; set; }

        private readonly MicroVoxelPool4096?[] _subChunkMicroPools;
        private readonly uint[] _uniformSubChunkValues;

        /// <summary>Yeni bir 16x256x16 VoxelChunkComponent olusturur.</summary>
        public VoxelChunkComponent(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            VisualWorldPosition = (chunkX * StratocraftConstants.ChunkSizeX, 0f, chunkZ * StratocraftConstants.ChunkSizeZ);
            Voxels = new uint[StratocraftConstants.VoxelsPerChunk];
            Fluids = new ushort[StratocraftConstants.VoxelsPerChunk];
            DirtySubChunkMask = 0xFFFF;
            DirtyFluidSubChunkMask = 0xFFFF;
            UniformSubChunkMask = 0xFFFF;
            SkyOcclusionMap = new ChunkSkyOcclusionMap256();
            _subChunkMicroPools = new MicroVoxelPool4096?[StratocraftConstants.SubChunksPerColumn];
            _uniformSubChunkValues = new uint[StratocraftConstants.SubChunksPerColumn];
            LastVisitedUtcMinutes = GetCurrentUtcMinutes();
        }

        /// <summary>Sub-Chunk mikro-havuz dizisine tahsissiz salt-okunur erisim.</summary>
        public MicroVoxelPool4096?[] SubChunkMicroPools => _subChunkMicroPools;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetCurrentUtcMinutes()
        {
            long unixMinutes = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60L;
            return unixMinutes <= 0 ? 0u : (uint)unixMinutes;
        }

        /// <summary>Verilen (lx, y, lz) lokal koordinatinin 0..65535 dogrusal indeksini hesaplar (Y-Major: (y*16 + lz)*16 + lx).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFlatIndex(int lx, int y, int lz)
        {
            return (y * StratocraftConstants.ChunkSizeZ + lz) * StratocraftConstants.ChunkSizeX + lx;
        }

        /// <summary>Verilen lokal (lx, y, lz) koordinatindaki VoxelCell degerini okur.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VoxelCell GetVoxel(int lx, int y, int lz)
        {
            return new VoxelCell(Voxels[GetFlatIndex(lx, y, lz)]);
        }

        /// <summary>Verilen lokal (lx, y, lz) koordinatina VoxelCell yazar, SkyOcclusionY ve kirli maskeleri gunceller.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVoxel(int lx, int y, int lz, VoxelCell cell)
        {
            int idx = GetFlatIndex(lx, y, lz);
            if (Voxels[idx] != cell.RawValue)
            {
                Voxels[idx] = cell.RawValue;
                int subChunkY = y >> 4;
                DirtySubChunkMask |= (ushort)(1 << subChunkY);
                UniformSubChunkMask &= (ushort)~(1 << subChunkY);
                IsPersistenceDirty = true;

                if (cell.OriginState == BlokKokenDurumu.OyuncuItemBlogu ||
                    cell.OriginState == BlokKokenDurumu.VoxelLockedAtomic)
                {
                    HasActiveStructures = true;
                }

                SkyOcclusionMap.UpdateOnCellChanged(Voxels, lx, y, lz, cell.RawValue);
                unchecked { NavVersion++; }
                if (NavVersion == 0) NavVersion = 1;
            }
        }

        /// <summary>Belirtilen Sub-Chunk (0..15) icin mevcut MicroVoxelPool4096 havuzunu dondurur (yoksa tahsis etmeden null doner).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MicroVoxelPool4096? TryGetMicroPool(int subChunkY)
        {
            return (uint)subChunkY < StratocraftConstants.SubChunksPerColumn ? _subChunkMicroPools[subChunkY] : null;
        }

        /// <summary>Belirtilen Sub-Chunk (0..15) icin MicroVoxelPool4096 havuzunu dondurur (yoksa olusturur).</summary>
        public MicroVoxelPool4096 GetOrCreateMicroPool(int subChunkY)
        {
            if ((uint)subChunkY >= StratocraftConstants.SubChunksPerColumn)
            {
                throw new ArgumentOutOfRangeException(nameof(subChunkY));
            }

            MicroVoxelPool4096? pool = _subChunkMicroPools[subChunkY];
            if (pool == null)
            {
                pool = new MicroVoxelPool4096();
                _subChunkMicroPools[subChunkY] = pool;
            }
            return pool;
        }

        /// <summary>Belirtilen Sub-Chunk (0..15) tamamen tek bir degerden (0-bit Uniform) mi olusuyor?</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSubChunkUniform(int subChunkY, out uint uniformRawValue)
        {
            if ((uint)subChunkY < StratocraftConstants.SubChunksPerColumn &&
                (UniformSubChunkMask & (1 << subChunkY)) != 0)
            {
                uniformRawValue = _uniformSubChunkValues[subChunkY];
                return true;
            }

            uniformRawValue = 0u;
            return false;
        }

        /// <summary>SIMD uretimi veya disk yuklemesi sonrasi SkyOcclusionMap, 0-bit Uniform ve HasActiveStructures bayraklarini yeniler.</summary>
        public void RebuildMetadataCaches()
        {
            SkyOcclusionMap.RebuildAllColumns(Voxels);

            ushort uniformMask = 0;
            bool hasPlayerStruct = HasActiveStructures;

            for (int subY = 0; subY < StratocraftConstants.SubChunksPerColumn; subY++)
            {
                int startIdx = subY * StratocraftConstants.VoxelsPerSubChunk;
                int endIdx = startIdx + StratocraftConstants.VoxelsPerSubChunk;
                uint first = Voxels[startIdx];
                bool isUniform = true;

                for (int i = startIdx; i < endIdx; i++)
                {
                    uint v = Voxels[i];
                    if (v != first)
                    {
                        isUniform = false;
                    }

                    if (!hasPlayerStruct && v != 0u)
                    {
                        BlokKokenDurumu origin = VoxelBitUtility.GetOriginState(v);
                        if (origin == BlokKokenDurumu.OyuncuItemBlogu || origin == BlokKokenDurumu.VoxelLockedAtomic)
                        {
                            hasPlayerStruct = true;
                        }
                    }
                }

                if (isUniform && (_subChunkMicroPools[subY] == null || _subChunkMicroPools[subY]!.ActiveChiseledCount == 0))
                {
                    uniformMask |= (ushort)(1 << subY);
                    _uniformSubChunkValues[subY] = first;
                }
            }

            UniformSubChunkMask = uniformMask;
            HasActiveStructures = hasPlayerStruct;
        }

        /// <summary>Tum aktif 4x4x4 mikro-voxel slotlarini .sregion serilestirmesi icin disa aktarir.</summary>
        public (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[] ExportMicroPoolEntries()
        {
            List<(byte, ushort, ulong)>? all = null;
            for (byte subY = 0; subY < StratocraftConstants.SubChunksPerColumn; subY++)
            {
                MicroVoxelPool4096? pool = _subChunkMicroPools[subY];
                if (pool == null || pool.ActiveChiseledCount == 0)
                {
                    continue;
                }

                all ??= new List<(byte, ushort, ulong)>();
                List<(ushort SlotIndex12, ulong Mask64)> entries = pool.ExportActiveEntries();
                for (int i = 0; i < entries.Count; i++)
                {
                    all.Add((subY, entries[i].SlotIndex12, entries[i].Mask64));
                }
            }

            return all == null ? Array.Empty<(byte, ushort, ulong)>() : all.ToArray();
        }

        /// <summary>Diskten yuklenen 4x4x4 mikro-voxel slotlarini ilgili Sub-Chunk havuzlarina geri yukler.</summary>
        public void ImportMicroPoolEntries(ReadOnlySpan<(byte SubChunkY, ushort SlotIndex12, ulong Mask64)> entries)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                (byte subY, ushort slot12, ulong mask64) = entries[i];
                if (subY < StratocraftConstants.SubChunksPerColumn &&
                    slot12 < StratocraftConstants.MaxMicroVoxelsPerSubChunk &&
                    mask64 != 0UL)
                {
                    GetOrCreateMicroPool(subY).AllocateSlot(slot12, mask64);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < _subChunkMicroPools.Length; i++)
            {
                _subChunkMicroPools[i]?.Dispose();
                _subChunkMicroPools[i] = null;
            }
        }
    }
}
