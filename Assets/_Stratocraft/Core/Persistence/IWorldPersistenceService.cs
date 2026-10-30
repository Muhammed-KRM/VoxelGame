using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stratocraft.Core.Persistence
{
    /// <summary>
    /// Tek bir 16x256x16 chunk sutununun serilestirme ve Pristine Chunk Decay ust-verisi.
    /// </summary>
    public sealed class ChunkSerializationSnapshot
    {
        /// <summary>Chunk X koordinati.</summary>
        public int ChunkX { get; }

        /// <summary>Chunk Z koordinati.</summary>
        public int ChunkZ { get; }

        /// <summary>Son ziyaret edilen oyun gunu (Pristine Chunk Decay hesabi icin).</summary>
        public uint LastVisitedGameDay { get; set; }

        /// <summary>Chunk klan siniri (TerritoryBitmask) icinde mi?</summary>
        public bool IsInsideClan { get; set; }

        /// <summary>Chunk icinde aktif oyuncu yapisi, sandik veya batarya var mi?</summary>
        public bool HasActiveStructures { get; set; }

        /// <summary>65.536 adet 32-bit VoxelCell dizisi.</summary>
        public uint[] VoxelCells { get; }

        /// <summary>65.536 adet 16-bit FluidCell16 dizisi.</summary>
        public ushort[] FluidCells { get; }

        /// <summary>Yeni bir chunk serilestirme goruntusu olusturur.</summary>
        public ChunkSerializationSnapshot(
            int chunkX,
            int chunkZ,
            uint[] voxelCells,
            ushort[] fluidCells,
            uint lastVisitedGameDay = 0,
            bool isInsideClan = false,
            bool hasActiveStructures = false)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            VoxelCells = voxelCells ?? throw new ArgumentNullException(nameof(voxelCells));
            FluidCells = fluidCells ?? throw new ArgumentNullException(nameof(fluidCells));
            LastVisitedGameDay = lastVisitedGameDay;
            IsInsideClan = isInsideClan;
            HasActiveStructures = hasActiveStructures;
        }
    }

    /// <summary>
    /// Bolge (.sregion) dosyalari ve dunya kalicilik islemleri icin sozlesme.
    /// </summary>
    public interface IWorldPersistenceService
    {
        /// <summary>Kirli chunk listesini 32x32 bolge (.sregion) dosyalarina atomik olarak kaydeder.</summary>
        ValueTask SaveDirtyChunksBatchAsync(ReadOnlyMemory<ChunkSerializationSnapshot> chunks, CancellationToken ct = default);

        /// <summary>Verilen (chunkX, chunkZ) koordinatindaki chunk'i diskten veya bellekten yukler.</summary>
        ValueTask<ChunkSerializationSnapshot?> TryLoadChunkAsync(int chunkX, int chunkZ, CancellationToken ct = default);

        /// <summary>Verilen bolge dosyasinin XXH64 saglama toplamini dogrular.</summary>
        bool VerifyRegionChecksum(int regionX, int regionZ);

        /// <summary>
        /// Klan siniri disinda kalan (IsInsideClan == false), icinde aktif yapi olmayan ve
        /// 14 oyun gunu boyunca ziyaret edilmeyen sahipsiz kazilmis chunk'lari silerek orijinal Seed'e dondurur.
        /// </summary>
        int PruneAbandonedWildernessChunks(uint currentGameDay, uint decayThresholdDays = 14);
    }
}
