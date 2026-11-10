#nullable enable
using System;

namespace Stratocraft.Character
{
    /// <summary>
    /// FAZ B3: Kılıç yönlü savurma jesti, mikro-kesik (Micro-Cut) ve sağ-sol spam (Reversal Tax) ayar parametreleri.
    /// Birimler: Input.GetAxis("Mouse X/Y") hassasiyetsiz ham birimidir (1.0 birim = 10 ham fare sayımı).
    /// </summary>
    public static class SwingGestureTuning
    {
        /// <summary>Jest birikim penceresi süresi (saniye).</summary>
        public static float WindowSeconds = 0.12f;

        /// <summary>Çoklu karede yapılan büyük savurmaların erken mikro-kesik olarak tetiklenmemesi için minimum birikim süresi (sn).</summary>
        public static float MinMultiFrameIntegrationSeconds = 0.045f;

        /// <summary>Bu mesafenin altındaki küçük kıpırtılar (örn. 3 sayım = 0.30) ASLA savurma başlatmaz, yalnızca gardı kaydırır.</summary>
        public static float DeadzonePathUnits = 0.40f;

        /// <summary>Mikro kesiğin (Micro-Cut) başladığı yol uzunluğu.</summary>
        public static float MicroCutStartUnits = 0.40f;

        /// <summary>Tam güçlü (%100) savurmaya ulaşılan yol uzunluğu.</summary>
        public static float FullPowerPathUnits = 1.15f;

        /// <summary>Power01 bu değerin altındaysa vuruş "Mikro Kesik" (Micro-Cut) sayılır (uzuv koparmaz, sersemletmez).</summary>
        public static float MicroCutPowerThreshold01 = 0.35f;

        /// <summary>Mikro kesikte (Power01 -> 0) savurma hızı çarpanı.</summary>
        public static float MicroCutMinSpeedMult = 0.38f;

        /// <summary>Mikro kesikte (Power01 -> 0) toplam darbe/hasar çarpanı (tam savurmanın <= %25'i).</summary>
        public static float MicroCutMinDamageMult = 0.16f;

        /// <summary>Ters yöne savurma sayılması için yön vektörleri nokta çarpımı eşiği.</summary>
        public static float ReversalDotThreshold = -0.35f;

        /// <summary>Savurma bittikten sonra ters yön vergisinin (Reversal Tax) geçerli olduğu süre (sn).</summary>
        public static float ReversalTaxWindowSeconds = 0.55f;

        /// <summary>Hızlı sağ-sol spam sırasında tam güç için gereken ek fare yolu çarpanı.</summary>
        public static float ReversalPathMultiplier = 1.65f;

        /// <summary>Hızlı sağ-sol spam sırasında eklenen ekstra toparlanma süresi (sn).</summary>
        public static float ReversalExtraRecoverySeconds = 0.16f;

        /// <summary>
        /// Savurma gücüne (0..1) ve mikro-kesik durumuna göre hasar çarpanını hesaplar.
        /// Mikro kesikte tam savurmanın en fazla %22..25'i kadar hasar verir.
        /// </summary>
        public static float ComputeDamageMultiplier(float power01, bool isMicroCut)
        {
            float p = Math.Clamp(power01, 0f, 1f);
            if (isMicroCut || p < MicroCutPowerThreshold01)
            {
                float t = MicroCutPowerThreshold01 > 0.001f ? Math.Clamp(p / MicroCutPowerThreshold01, 0f, 1f) : 0f;
                return MicroCutMinDamageMult + (0.24f - MicroCutMinDamageMult) * t;
            }
            return 0.25f + 0.75f * p;
        }
    }
}
