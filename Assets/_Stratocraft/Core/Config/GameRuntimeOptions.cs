namespace Stratocraft.Core.Config
{
    /// <summary>
    /// Sunucu calisma modu: Tek oyunculu yerel HostLoopback veya ayrilmis DedicatedServer.
    /// Her iki modda da %100 ayni otoriter sunucu sistemleri calisir.
    /// </summary>
    public enum ServerExecutionMode : byte
    {
        /// <summary>Tek oyunculu bellek-ici sifir gecikmeli yerel sunucu + istemci.</summary>
        HostLoopback = 0,

        /// <summary>Ayrilmis basliksiz (headless) cok oyunculu otoriter sunucu.</summary>
        DedicatedServer = 1
    }

    /// <summary>
    /// .env ve ServerConfig.json uzerinden yuklenen calisma zamani oyun ve butce secenekleri.
    /// </summary>
    public sealed class GameRuntimeOptions
    {
        /// <summary>Calisma ortami adi (Development / Production).</summary>
        public string EnvironmentName { get; set; } = "Development";

        /// <summary>Deterministik dunya uretim tohumu (Seed).</summary>
        public int WorldSeed { get; set; } = 14532026;

        /// <summary>Dunya kayit dizini (goreli yol).</summary>
        public string SaveDirectory { get; set; } = "./saves/default_world";

        /// <summary>Minimum log seviyesi.</summary>
        public string LogLevel { get; set; } = "Information";

        /// <summary>Sunucu calisma modu (HostLoopback veya DedicatedServer).</summary>
        public ServerExecutionMode ServerMode { get; set; } = ServerExecutionMode.HostLoopback;

        /// <summary>Netcode ana UDP portu.</summary>
        public int NetcodePort { get; set; } = 7777;

        /// <summary>LiteNetLib LZ4 voxel akis UDP portu.</summary>
        public int VoxelStreamPort { get; set; } = 7778;

        /// <summary>Maksimum eszamanli oyuncu sayisi.</summary>
        public int MaxPlayers { get; set; } = 64;

        /// <summary>Yonetici API gizli anahtari (Loglarda *** olarak maskelenir).</summary>
        public string AdminApiKey { get; set; } = string.Empty;

        /// <summary>Kan Imzali Kontrat Ed25519 sunucu muhur sirri (Loglarda *** olarak maskelenir).</summary>
        public string ContractSigningSecret { get; set; } = string.Empty;

        /// <summary>Maksimum oyun RAM butcesi (MB).</summary>
        public int MaxGameRamMb { get; set; } = 2400;

        /// <summary>Maksimum doku VRAM butcesi (MB).</summary>
        public int MaxTextureVramMb { get; set; } = 16;

        /// <summary>Aktif XZ chunk yaricapi.</summary>
        public int ActiveChunkRadiusXz { get; set; } = 4;

        /// <summary>Demirci orsunde serbest ozel silah tasarimina izin verilip verilmedigi.</summary>
        public bool AllowCustomItemDesign { get; set; } = true;

        /// <summary>Solo zindan yaratik/Boss maksimum HP carpani tavani.</summary>
        public float SingleplayerDungeonHpCap { get; set; } = 1.30f;
    }
}
