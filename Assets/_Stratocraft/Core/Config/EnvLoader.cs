using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Stratocraft.Core.Config
{
    /// <summary>
    /// Proje veya calisma dizinindeki .env dosyasini okuyarak GameRuntimeOptions nesnesine baglar.
    /// Hicbir dosyada sabit yol (D:\oyun vb.) kullanmaz; %100 goreli yol ile calisir.
    /// </summary>
    public static class EnvLoader
    {
        /// <summary>
        /// Verilen dosya yolundan (veya ortam degiskenlerinden) .env anahtar-deger ciftlerini cozumler.
        /// </summary>
        public static Dictionary<string, string> ParseDotEnvFile(string envFilePath)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(envFilePath) || !File.Exists(envFilePath))
            {
                return values;
            }

            string[] lines = File.ReadAllLines(envFilePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0)
                {
                    continue;
                }

                string key = trimmed.Substring(0, eqIdx).Trim();
                string rawVal = trimmed.Substring(eqIdx + 1).Trim();
                if (rawVal.Length >= 2 &&
                    ((rawVal[0] == '"' && rawVal[rawVal.Length - 1] == '"') ||
                     (rawVal[0] == '\'' && rawVal[rawVal.Length - 1] == '\'')))
                {
                    rawVal = rawVal.Substring(1, rawVal.Length - 2);
                }

                values[key] = rawVal;
            }

            return values;
        }

        /// <summary>
        /// .env dosyasini ve proses ortam degiskenlerini birlestirerek GameRuntimeOptions olusturur.
        /// </summary>
        public static GameRuntimeOptions LoadOptions(string? envFilePath = null)
        {
            string resolvedPath = string.IsNullOrWhiteSpace(envFilePath) ? ".env" : envFilePath!;
            Dictionary<string, string> map = ParseDotEnvFile(resolvedPath);
            var opts = new GameRuntimeOptions();

            opts.EnvironmentName = ReadString(map, "STRATOCRAFT_ENV", opts.EnvironmentName);
            opts.WorldSeed = ReadInt(map, "STRATOCRAFT_WORLD_SEED", opts.WorldSeed);
            opts.SaveDirectory = ReadString(map, "STRATOCRAFT_SAVE_DIR", opts.SaveDirectory);
            opts.LogLevel = ReadString(map, "STRATOCRAFT_LOG_LEVEL", opts.LogLevel);

            string modeStr = ReadString(map, "SERVER_MODE", "HostLoopback");
            if (Enum.TryParse(modeStr, ignoreCase: true, out ServerExecutionMode parsedMode))
            {
                opts.ServerMode = parsedMode;
            }

            opts.NetcodePort = ReadInt(map, "SERVER_PORT_NETCODE", opts.NetcodePort);
            opts.VoxelStreamPort = ReadInt(map, "SERVER_PORT_VOXEL_STREAM", opts.VoxelStreamPort);
            opts.MaxPlayers = ReadInt(map, "SERVER_MAX_PLAYERS", opts.MaxPlayers);
            opts.AdminApiKey = ReadString(map, "SERVER_ADMIN_API_KEY", opts.AdminApiKey);
            opts.ContractSigningSecret = ReadString(map, "CONTRACT_SIGNING_SECRET", opts.ContractSigningSecret);

            opts.MaxGameRamMb = ReadInt(map, "MAX_GAME_RAM_MB", opts.MaxGameRamMb);
            opts.MaxTextureVramMb = ReadInt(map, "MAX_TEXTURE_VRAM_MB", opts.MaxTextureVramMb);
            opts.ActiveChunkRadiusXz = ReadInt(map, "ACTIVE_CHUNK_RADIUS_XZ", opts.ActiveChunkRadiusXz);
            opts.SingleplayerDungeonHpCap = ReadFloat(map, "SINGLEPLAYER_DUNGEON_HP_CAP", opts.SingleplayerDungeonHpCap);

            return opts;
        }

        private static string ReadString(Dictionary<string, string> map, string key, string fallback)
        {
            string? envOverride = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(envOverride))
            {
                return envOverride;
            }

            return map.TryGetValue(key, out string? val) && !string.IsNullOrEmpty(val) ? val : fallback;
        }

        private static int ReadInt(Dictionary<string, string> map, string key, int fallback)
        {
            string raw = ReadString(map, key, string.Empty);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static float ReadFloat(Dictionary<string, string> map, string key, float fallback)
        {
            string raw = ReadString(map, key, string.Empty);
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }
    }
}
