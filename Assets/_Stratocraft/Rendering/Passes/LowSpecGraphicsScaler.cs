using Stratocraft.Core.Constants;
using UnityEngine;

namespace Stratocraft.Rendering.Passes
{
    /// <summary>
    /// Bolum 1.6, 4.13.16 ve 5.1.0 Tablo 3.2 geregi 8 GB Sistem RAM veya dahili GPU (Intel Iris Xe / AMD Vega)
    /// tespit edildiginde otomatik olarak 1-Tap Dithered Biplanar ve 16 MB doku akis butcesini etkinlestirir.
    /// </summary>
    public sealed class LowSpecGraphicsScaler : MonoBehaviour
    {
        private const string LowSpecShaderKeyword = "_STRATOCRAFT_LOWSPEC_1TAP";
        private const int LowSpecSystemRamThresholdMb = 8400;
        private const int LowSpecVramThresholdMb = 2200;

        /// <summary>Sistemin dusuk donanim (8 GB RAM / iGPU) profilinde calisip calismadigi.</summary>
        public bool IsLowSpecModeActive { get; private set; }

        private void Awake()
        {
            ApplyHardwareAdaptiveBudget();
        }

        /// <summary>
        /// Sistem RAM ve GPU bellegini sorgulayarak UberShader 1-Tap anahtarini ve 16 MB VRAM sinirini uygular.
        /// </summary>
        public void ApplyHardwareAdaptiveBudget()
        {
            int sysRamMb = SystemInfo.systemMemorySize;
            int gpuVramMb = SystemInfo.graphicsMemorySize;

            bool shouldUseLowSpec = ShouldEnableLowSpecProfile(sysRamMb, gpuVramMb);
            IsLowSpecModeActive = shouldUseLowSpec;

            QualitySettings.streamingMipmapsActive = true;
            QualitySettings.streamingMipmapsMemoryBudget = StratocraftConstants.MaxTextureVramBudgetMb;

            if (shouldUseLowSpec)
            {
                Shader.EnableKeyword(LowSpecShaderKeyword);
            }
            else
            {
                Shader.DisableKeyword(LowSpecShaderKeyword);
            }
        }

        /// <summary>
        /// Saf matematiksel esik kontrolu (birim testlerinden Unity sahnesi olmadan da dogrulanabilir).
        /// </summary>
        public static bool ShouldEnableLowSpecProfile(int systemRamMb, int graphicsVramMb)
        {
            return systemRamMb <= LowSpecSystemRamThresholdMb || graphicsVramMb <= LowSpecVramThresholdMb;
        }
    }
}
