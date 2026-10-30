#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Stratocraft.Core.Logging
{
    /// <summary>
    /// Bolum 2.4 Madde 4 geregi oturum anahtarlarini (SessionToken), Ed25519 gizli anahtarlarini,
    /// .env sirlarini ve uretim modundaki IPv4 adreslerini *** ile maskeleyen yardimci sinif.
    /// </summary>
    public static class SensitiveDataMasker
    {
        private static readonly Regex KeyValueSecretRegex = new Regex(
            @"(?i)\b(SERVER_ADMIN_API_KEY|ADMIN_API_KEY|CONTRACT_SIGNING_SECRET|SessionToken|SecretKey|Password|Authorization|Bearer)\s*([:=])\s*([^\s,"";\}]+)",
            RegexOptions.Compiled);

        private static readonly Regex Ipv4Regex = new Regex(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            RegexOptions.Compiled);

        private static readonly object SyncRoot = new object();
        private static readonly List<string> RegisteredLiteralSecrets = new List<string>();

        /// <summary>
        /// Calisma zamaninda yuklenen .env gizli degerlerini dogrudan metin eslesmesiyle maskelemek icin kaydeder.
        /// </summary>
        public static void RegisterSecretValue(string? secretValue)
        {
            if (string.IsNullOrWhiteSpace(secretValue) || secretValue!.Length < 6)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (!RegisteredLiteralSecrets.Contains(secretValue))
                {
                    RegisteredLiteralSecrets.Add(secretValue);
                }
            }
        }

        /// <summary>
        /// Kayitli gizli degerleri temizler (birim test izolasyonu icin).
        /// </summary>
        public static void ClearRegisteredSecrets()
        {
            lock (SyncRoot)
            {
                RegisteredLiteralSecrets.Clear();
            }
        }

        /// <summary>
        /// Girdi metnindeki tum gizli anahtar=deger ciftlerini, kayitli sirlari ve (istenirse) IP adreslerini *** ile maskeler.
        /// </summary>
        public static string Mask(string? rawText, bool maskIpAddresses = true)
        {
            if (string.IsNullOrEmpty(rawText))
            {
                return string.Empty;
            }

            string sanitized = rawText!;

            lock (SyncRoot)
            {
                for (int i = 0; i < RegisteredLiteralSecrets.Count; i++)
                {
                    string secret = RegisteredLiteralSecrets[i];
                    if (sanitized.IndexOf(secret, StringComparison.Ordinal) >= 0)
                    {
                        sanitized = sanitized.Replace(secret, "***");
                    }
                }
            }

            sanitized = KeyValueSecretRegex.Replace(sanitized, "$1$2***");

            if (maskIpAddresses)
            {
                sanitized = Ipv4Regex.Replace(sanitized, "***.***.***.***");
            }

            return sanitized;
        }
    }
}
