namespace Stratocraft.Core.Constants
{
    /// <summary>
    /// Stratocraft evrensel dunya, voxel, fizik, hareket, bellek ve kare butcesi sabitleri.
    /// GAME LOGIC BIBLE v7.0 ile birebir uyumlu tek dogruluk kaynagidir.
    /// </summary>
    public static class StratocraftConstants
    {
        // ---------- Voxel, Sub-Chunk ve Dikey Koordinat Boyutlari ----------
        /// <summary>Bir chunk sutununun X genisligi (blok).</summary>
        public const int ChunkSizeX = 16;

        /// <summary>Bir chunk sutununun Y yuksekligi (blok: arrayY 0..255 -> worldY -112..+143).</summary>
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

        /// <summary>Dizi Y indeksinden kanonik Dunya Y kotuna donusum ofseti (worldY = arrayY - 112).</summary>
        public const int WorldYOffsetFromArrayY = -112;

        /// <summary>Kanonik Deniz Seviyesi dizi Y indeksi (worldY = 0m -> arrayY = 112).</summary>
        public const int SeaLevelArrayY = 112;

        /// <summary>Anayasa Bolum 10.1: Kirilmaz Manto / Bedrock ust siniri (worldY = -105m -> arrayY = 7).</summary>
        public const int BedrockMantleMaxWorldY = -105;

        /// <summary>Kirilmaz Manto / Bedrock ust dizi Y indeksi (0..7).</summary>
        public const int BedrockMantleMaxArrayY = 7;

        /// <summary>Anayasa Bolum 10.1: Kesintisiz Kaynayan Lav Denizi alt dunya kotu (-100m -> arrayY = 12).</summary>
        public const int BoilingLavaSeaMinWorldY = -100;

        /// <summary>Anayasa Bolum 10.1: Kesintisiz Kaynayan Lav Denizi ust dunya kotu (-80m -> arrayY = 32).</summary>
        public const int BoilingLavaSeaMaxWorldY = -80;

        /// <summary>7-bit SDF yuzey gecis esigi (IsoLevel = 64 -> rho = 0.0).</summary>
        public const byte SdfIsoLevel = 64;

        /// <summary>Tam gomulu kati govde / Uc-eksenli kusatma SDF esigi (Density >= 96 -> rho >= +0.5).</summary>
        public const byte SdfConfinedSolidLevel = 96;

        // ---------- Kanonik 8 Es-Merkezli Zorluk Halkasi (Anayasa Bolum 10.1 - Metre) ----------
        /// <summary>Halka 0: Guvenli Merkez dis yaricapi (0 - 200m).</summary>
        public const float Ring0SafeRadiusMeters = 200f;

        /// <summary>Halka 1: Baslangic Ovalari dis yaricapi (200 - 1.000m).</summary>
        public const float Ring1RadiusMeters = 1000f;

        /// <summary>Halka 2: Vahsi Ormanlar ve Bozkir dis yaricapi (1.000 - 3.000m).</summary>
        public const float Ring2RadiusMeters = 3000f;

        /// <summary>Halka 3: Tehlikeli Daglar ve Felaket Kusagi dis yaricapi (3.000 - 5.000m).</summary>
        public const float Ring3RadiusMeters = 5000f;

        /// <summary>Halka 4: Corak ve Volkanik Topraklar dis yaricapi (5.000 - 10.000m).</summary>
        public const float Ring4RadiusMeters = 10000f;

        /// <summary>Halka 5: Kadim Yikintilar dis yaricapi (10.000 - 15.000m).</summary>
        public const float Ring5OuterRadiusMeters = 15000f;

        /// <summary>Halka 6: Abisal Sinir dis yaricapi (15.000 - 18.000m; >18.000m Halka 7 Boyutsal Yirtik).</summary>
        public const float Ring6AbyssalRadiusMeters = 18000f;

        /// <summary>Felaket Titanlarinin yuruyuse basladigi minimum kusak yaricapi (3.000m).</summary>
        public const float DisasterTitanSpawnRadiusMin = 3000f;

        /// <summary>Felaket Titanlarinin yuruyuse basladigi maksimum kusak yaricapi (3.600m).</summary>
        public const float DisasterTitanSpawnRadiusMax = 3600f;

        // ---------- Chunk-Snapped Floating Origin (Anayasa Bolum 2.6.A - Metre) ----------
        /// <summary>Birlesik gorsel + fizik K*32m Chunk-Snapped Floating Origin esigi (±256m -> 15.26 um FP32 hassasiyet).</summary>
        public const float VisualFloatingOriginThresholdMeters = 256f;

        /// <summary>Gorsel Floating Origin kaydirma adimi (K * 32m = 2 chunk kati).</summary>
        public const float VisualFloatingOriginSnapMeters = 32f;

        /// <summary>240 Hz kilic carpismasinda 15 mikrometre FP32 hassasiyeti koruyan yerel fizik esigi (±256m).</summary>
        public const float PhysicsLocalOriginThresholdMeters = 256f;

        // ---------- Fiziksel Karakter Hareket Hizlari ve Hitbox Boyutlari (Anayasa Bolum 2.7.E & 3.0) ----------
        /// <summary>Standart yurume hizi (m/sn).</summary>
        public const float WalkSpeedMps = 4.0f;

        /// <summary>Depar (Sprint) hizi (m/sn).</summary>
        public const float SprintSpeedMps = 7.0f;

        /// <summary>Egilerek yurume (Crouch - 1x1m maden tuneli) hizi (m/sn).</summary>
        public const float CrouchSpeedMps = 2.0f;

        /// <summary>Yerde surunme (Prone - 0.5m havalandirma yarigi) hizi (m/sn).</summary>
        public const float ProneSpeedMps = 1.0f;

        /// <summary>I tusuyla 3B kumas canta acildiginda uygulanan hiz carpani (-%70 ceza -> 1.2 m/sn).</summary>
        public const float BackpackOpenSpeedMultiplier = 0.30f;

        /// <summary>Ayakta karakter kapsul yuksekligi (1.80m).</summary>
        public const float StandingHeightMeters = 1.80f;

        /// <summary>Comelme karakter kapsul yuksekligi (0.90m).</summary>
        public const float CrouchHeightMeters = 0.90f;

        /// <summary>Surunme karakter kapsul yuksekligi (0.45m).</summary>
        public const float ProneHeightMeters = 0.45f;

        /// <summary>Ayakta/Comelmede kapsul yaricapi (0.28m -> 0.56m genislik).</summary>
        public const float StandingCapsuleRadiusMeters = 0.28f;

        /// <summary>Surunmede kapsul yaricapi (0.20m -> 0.40m genislik, 0.50m yariktan fiziki gecis garantisi).</summary>
        public const float ProneCapsuleRadiusMeters = 0.20f;

        // ---------- Zaman, Mevsim ve Pristine Chunk Decay Sabitleri (Anayasa Bolum 2.6.B & 10.3) ----------
        /// <summary>Tam 1 oyun gunu suresi: 60 gercek dakika = 3.600 saniye (40 dk Gunduz + 20 dk Gece).</summary>
        public const double SecondsPerInGameDay = 3600.0;

        /// <summary>Gunduz suresi (40 gercek dakika = 2.400 saniye).</summary>
        public const double DaylightDurationSeconds = 2400.0;

        /// <summary>Gece suresi (20 gercek dakika = 1.200 saniye).</summary>
        public const double NightDurationSeconds = 1200.0;

        /// <summary>1 mevsimdeki oyun gunu sayisi (24 oyun gunu = 24 gercek saat; 1 yil = 96 gercek saat).</summary>
        public const int DaysPerSeason = 24;

        /// <summary>Kanli Ay (Blood Moon) dongusu (her 3 gecede 1).</summary>
        public const int BloodMoonCycleDays = 3;

        /// <summary>Sahipsiz dogal chunk budama suresi (14 gercek gun = 20.160 UTC dakika).</summary>
        public const uint PristineChunkDecayRealMinutes = 14u * 24u * 60u;

        /// <summary>Geriye donuk test uyumlulugu icin 14 gunluk esik sabiti.</summary>
        public const uint PristineChunkDecayGameDays = 14u;

        // ---------- Donanim, Bellek ve Kare Butcesi Sinirlari ----------
        /// <summary>8 GB Sistem RAM hedefi icin maksimum toplam oyun RAM butcesi (MB).</summary>
        public const int MaxGameRamBudgetMb = 2500;

        /// <summary>UV-Free UberShader icin maksimum toplam doku VRAM butcesi (MB).</summary>
        public const float MaxTextureVramBudgetMb = 16.0f;

        /// <summary>Hedef 60+ FPS icin maksimum kare suresi butcesi (15.0 ms).</summary>
        public const float TargetFrameBudgetMs = 15.0f;

        /// <summary>Sonlu akiskan kare basi maksimum CPU butcesi (100 mikrosaniye).</summary>
        public const double FluidFrameBudgetMicroseconds = 100.0;

        /// <summary>240 Hz yakin dovus CCD sabit alt-adim suresi (1 / 240 sn).</summary>
        public const float MeleeCcdSubstepSeconds = 1.0f / 240.0f;

        /// <summary>Stokiyometrik Su + Lav -> Dogal Obsidyen reaksiyon esigi (128 / 255).</summary>
        public const byte StoichiometricFluidThreshold = 128;

        /// <summary>Solo zindan yaratik/Boss maksimum HP olcekleme tavani (1.30x).</summary>
        public const float SoloDungeonMaxHpMultiplier = 1.30f;
    }
}
