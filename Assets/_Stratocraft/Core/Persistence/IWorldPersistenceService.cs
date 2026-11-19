#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Stratocraft.Core.Constants;

namespace Stratocraft.Core.Persistence
{
    /// <summary>
    /// Tek bir 16x256x16 chunk sutununun serilestirme, MicroVoxelPool4096 ve Pristine Chunk Decay ust-verisi.
    /// </summary>
    public sealed class ChunkSerializationSnapshot
    {
        /// <summary>Chunk X koordinati.</summary>
        public int ChunkX { get; }

        /// <summary>Chunk Z koordinati.</summary>
        public int ChunkZ { get; }

        /// <summary>Son ziyaret edilen oyun gunu.</summary>
        public uint LastVisitedGameDay { get; set; }

        /// <summary>Son ziyaret edilen gercek UTC dakikası (Anayasa Bolum 2.6.B: 14 Gercek Gun = 20.160 UTC dakika).</summary>
        public uint LastVisitedUtcMinutes { get; set; }

        /// <summary>Chunk klan siniri (TerritoryBitmask) icinde mi?</summary>
        public bool IsInsideClan { get; set; }

        /// <summary>Chunk icinde aktif oyuncu yapisi (IS_ITEM_BLOCK == 1), sandik veya batarya var mi?</summary>
        public bool HasActiveStructures { get; set; }

        /// <summary>65.536 adet 32-bit VoxelCell dizisi.</summary>
        public uint[] VoxelCells { get; }

        /// <summary>65.536 adet 16-bit FluidCell16 dizisi.</summary>
        public ushort[] FluidCells { get; }

        /// <summary>Seyrek 4x4x4 Mikro-Voxel yontma kayitlari (SubChunkY, SlotIndex12, Mask64).</summary>
        public (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[] MicroPoolEntries { get; }

        /// <summary>Paket 4C-2: Chunk'in son simule edildigi oyun-ici saniye (ekran-disi yakalama/catch-up icin).</summary>
        public double LastSimulatedGameSeconds { get; set; }

        /// <summary>Paket 4C-2: Bu chunk'in hangi alt-sistemlerde degisiklik tasidigi.</summary>
        public ChunkDirtyFlags DirtyFlags { get; set; }

        /// <summary>Paket 4C-2: Seyrek toprak nemi kayitlari (FlatIndex, Moisture[1..255]); kuru chunk'larda 0 uzunluk.</summary>
        public (ushort FlatIndex, byte Moisture)[] SparseSoilMoisture { get; }

        /// <summary>Paket 4C-2: 16x16 kar SWE birimleri (256 B); karsiz chunk'larda null.</summary>
        public byte[]? SnowSwe256 { get; }

        /// <summary>Paket 4C-2: Ozel tuzluluk bitset'i (ulong[1024]); varsayilan tuzlulukta null.</summary>
        public ulong[]? SalinityBits1024 { get; }

        /// <summary>Yeni bir chunk serilestirme goruntusu olusturur.</summary>
        public ChunkSerializationSnapshot(
            int chunkX,
            int chunkZ,
            uint[] voxelCells,
            ushort[] fluidCells,
            uint lastVisitedGameDay = 0,
            bool isInsideClan = false,
            bool hasActiveStructures = false,
            uint lastVisitedUtcMinutes = 0,
            (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[]? microPoolEntries = null,
            double lastSimulatedGameSeconds = 0.0,
            ChunkDirtyFlags dirtyFlags = ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Fluid,
            (ushort FlatIndex, byte Moisture)[]? sparseSoilMoisture = null,
            byte[]? snowSwe256 = null,
            ulong[]? salinityBits1024 = null)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            VoxelCells = voxelCells ?? throw new ArgumentNullException(nameof(voxelCells));
            FluidCells = fluidCells ?? throw new ArgumentNullException(nameof(fluidCells));
            LastVisitedGameDay = lastVisitedGameDay;
            IsInsideClan = isInsideClan;
            HasActiveStructures = hasActiveStructures;
            LastVisitedUtcMinutes = lastVisitedUtcMinutes;
            MicroPoolEntries = microPoolEntries ?? Array.Empty<(byte, ushort, ulong)>();
            LastSimulatedGameSeconds = lastSimulatedGameSeconds;
            DirtyFlags = dirtyFlags;
            SparseSoilMoisture = sparseSoilMoisture ?? Array.Empty<(ushort, byte)>();
            SnowSwe256 = snowSwe256;
            SalinityBits1024 = salinityBits1024;
        }
    }

    /// <summary>
    /// Bolge (.sregion) dosyalari ve dunya kalicilik islemleri icin sozlesme.
    /// </summary>
    public interface IWorldPersistenceService
    {
        /// <summary>Kirli chunk listesini 32x32 bolge (.sregion) dosyalarina Seed-Delta XOR + RLE + Zstd + XXH64 ile atomik kaydeder.</summary>
        ValueTask SaveDirtyChunksBatchAsync(ReadOnlyMemory<ChunkSerializationSnapshot> chunks, CancellationToken ct = default);

        /// <summary>Verilen (chunkX, chunkZ) koordinatindaki chunk'i diskten veya bellekten yukler.</summary>
        ValueTask<ChunkSerializationSnapshot?> TryLoadChunkAsync(int chunkX, int chunkZ, CancellationToken ct = default);

        /// <summary>Verilen bolge dosyasinin XXH64 saglama toplamini dogrular.</summary>
        bool VerifyRegionChecksum(int regionX, int regionZ);

        /// <summary>
        /// Klan siniri disinda kalan (IsInsideClan == false), icinde aktif oyuncu yapisi olmayan ve
        /// 14 gun boyunca ziyaret edilmeyen sahipsiz kazilmis chunk'lari silerek orijinal Seed'e dondurur.
        /// </summary>
        int PruneAbandonedWildernessChunks(uint currentGameDay, uint decayThresholdDays = StratocraftConstants.PristineChunkDecayGameDays);

        /// <summary>
        /// Anayasa Bolum 2.6.B: Gercek UTC zamanina gore 14 gercek gun (20.160 dakika) ziyaret edilmeyen
        /// sahipsiz vahsi doga chunk'larini orijinal Seed'e dondurur.
        /// </summary>
        int PruneAbandonedWildernessChunksByUtcMinutes(
            uint currentUtcMinutes,
            uint decayThresholdRealMinutes = StratocraftConstants.PristineChunkDecayRealMinutes);
    }
}
