namespace Stratocraft.Core.Constants
{
    /// <summary>
    /// Stratocraft evrensel dunya, voxel, fizik, hareket, bellek ve kare butcesi sabitleri.
    /// Sihirli sayi yasagina (Bolum 2.2.1) uygun tek dogruluk kaynagidir.
    /// </summary>
    public static class StratocraftConstants
    {
        // ---------- Voxel ve Sub-Chunk Boyutlari ----------
        /// <summary>Bir chunk sutununun X genisligi (blok).</summary>
        public const int ChunkSizeX = 16;

        /// <summary>Bir chunk sutununun Y yuksekligi (blok).</summary>
        public const int ChunkSizeY = 256;

        /// <summary>Bir chunk sutununun Z derinligi (blok).</summary>
        public const int ChunkSizeZ = 16;

        /// <summary>Bir chunk sutunundaki toplam voxel sayisi (16 * 256 * 16 = 65.536).</summary>
        public const int VoxelsPerChunk = ChunkSizeX * ChunkSizeY * ChunkSizeZ;

        /// <summary>Sub-Chunk kenar uzunlugu (16x16x16 = 4.096 voxel).</summary>
        public const int SubChunkSize = 16;

        /// <summary>Sub-Chunk basina voxel ve kayipsiz mikro-havuz kapasitesi (%100 1:1 ortusme).</summary>
        public const int VoxelsPerSubChunk = 4096;

        /// <summary>Bir chunk sutunundaki dikey Sub-Chunk sayisi (256 / 16 = 16).</summary>
        public const int SubChunksPerColumn = 16;

        /// <summary>Sub-Chunk basina maksimum 4x4x4 (64-bit) mikro-voxel slot sayisi.</summary>
        public const int MaxMicroVoxelsPerSubChunk = 4096;

        // ---------- 6 Es-Merkezli Zorluk Halkasi ve Felaket Kusagi (Metre) ----------
        /// <summary>Halka 0 Guvenli Merkez dis yaricapi (200m).</summary>
        public const float Ring0SafeRadiusMeters = 200f;

        /// <summary>Halka 1 dis yaricapi (1.000m).</summary>
        public const float Ring1RadiusMeters = 1000f;

        /// <summary>Halka 2 dis yaricapi (3.000m).</summary>
        public const float Ring2RadiusMeters = 3000f;

        /// <summary>Halka 3 dis yaricapi (6.000m).</summary>
        public const float Ring3RadiusMeters = 6000f;

        /// <summary>Halka 4 dis yaricapi (10.000m).</summary>
        public const float Ring4RadiusMeters = 10000f;

        /// <summary>Halka 5 Hiclik Siniri dis yaricapi (18.000m).</summary>
        public const float Ring5OuterRadiusMeters = 18000f;

        /// <summary>Felaket Titanlarinin yuruyuse basladigi minimum kusak yaricapi (3.000m).</summary>
        public const float DisasterTitanSpawnRadiusMin = 3000f;

        /// <summary>Felaket Titanlarinin yuruyuse basladigi maksimum kusak yaricapi (3.600m).</summary>
        public const float DisasterTitanSpawnRadiusMax = 3600f;

        // ---------- Cift Esikli Floating Origin (Metre) ----------
        /// <summary>Gorsel dunya K*32m Chunk-Snapped Floating Origin esigi (±4.096m).</summary>
        public const float VisualFloatingOriginThresholdMeters = 4096f;

        /// <summary>Gorsel Floating Origin kaydirma adimi (32m).</summary>
        public const float VisualFloatingOriginSnapMeters = 32f;

        /// <summary>240 Hz kilic carpismasinda 15 mikrometre FP32 hassasiyeti koruyan yerel fizik esigi (±256m).</summary>
        public const float PhysicsLocalOriginThresholdMeters = 256f;

        // ---------- Fiziksel Karakter Hareket Hizlari ----------
        /// <summary>Standart yurume hizi (m/sn).</summary>
        public const float WalkSpeedMps = 4.0f;

        /// <summary>Depar (Sprint) hizi (m/sn).</summary>
        public const float SprintSpeedMps = 7.0f;

        /// <summary>Egilerek yurume (Crouch) hizi (m/sn).</summary>
        public const float CrouchSpeedMps = 2.0f;

        /// <summary>Yerde surunme (Prone - 1x1m gecis) hizi (m/sn).</summary>
        public const float ProneSpeedMps = 1.0f;

        /// <summary>I tusuyla 3B kumas canta acildiginda uygulanan hiz carpani (-%70 ceza).</summary>
        public const float BackpackOpenSpeedMultiplier = 0.30f;

        // ---------- Donanim, Bellek ve Kare Butcesi Sinirlari ----------
        /// <summary>8 GB Sistem RAM hedefi icin maksimum toplam oyun RAM butcesi (MB).</summary>
        public const int MaxGameRamBudgetMb = 2500;

        /// <summary>UV-Free UberShader icin maksimum toplam doku VRAM butcesi (MB).</summary>
        public const float MaxTextureVramBudgetMb = 16.0f;

        /// <summary>Hedef 60+ FPS icin maksimum kare suresi butcesi (15.0 ms).</summary>
        public const float TargetFrameBudgetMs = 15.0f;

        /// <summary>240 Hz yakin dovus CCD sabit alt-adim suresi (1 / 240 sn).</summary>
        public const float MeleeCcdSubstepSeconds = 1.0f / 240.0f;

        /// <summary>Stokiyometrik Su + Lav -> Dogal Obsidyen reaksiyon esigi (128 / 255).</summary>
        public const byte StoichiometricFluidThreshold = 128;

        /// <summary>Sahipsiz dogal chunk budama suresi (14 oyun gunu - Pristine Chunk Decay).</summary>
        public const uint PristineChunkDecayGameDays = 14u;

        /// <summary>Solo zindan yaratik/Boss maksimum HP olcekleme tavani (1.30x).</summary>
        public const float SoloDungeonMaxHpMultiplier = 1.30f;
    }
}
