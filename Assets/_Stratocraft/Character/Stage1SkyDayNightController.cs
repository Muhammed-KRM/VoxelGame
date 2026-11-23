#if UNITY_5_3_OR_NEWER
#nullable enable
using Stratocraft.WorldGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 4A canli PlayMode duzeltmesi: Gunes efemerisini (SolarEphemState) gercekten gorunur kilan
    /// gokyuzu / gece-gunduz denetleyicisi.
    /// - Kamerayi duz renk (SolidColor) yerine prosedurel Skybox'a alir (gunes diski, ufuk, gun batimi kizilligi).
    /// - Gunduz yonlu isik = Gunes; gece ayni yonlu isik Ay'a doner (soluk mavi, dusuk siddet) -> Skybox'ta ay diski.
    /// - Trilight ortam isigi, sis rengi ve skybox pozlamasi gunes yuksekligi + bulut ortusu + firtina ile surulur.
    /// 0 ek doku VRAM: Unity'nin yerlesik Skybox/Procedural shader'i kullanilir.
    /// </summary>
    public sealed class Stage1SkyDayNightController
    {
        private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        private static readonly int SunSizeConvergenceId = Shader.PropertyToID("_SunSizeConvergence");
        private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int NightDimId = Shader.PropertyToID("_StratoNightDim01");
        private static readonly int HorizonTintId = Shader.PropertyToID("_HorizonTint");
        private static readonly int ZenithTintId = Shader.PropertyToID("_ZenithTint");
        private static readonly int BiomeSkyTintId = Shader.PropertyToID("_BiomeSkyTint");
        private static readonly int NightFactorId = Shader.PropertyToID("_NightFactor");
        private static readonly int CloudOvercastId = Shader.PropertyToID("_CloudOvercast");
        private static readonly int AuroraStrengthId = Shader.PropertyToID("_AuroraStrength");

        private static readonly Color DayAmbientSky = new(0.54f, 0.68f, 0.88f);
        private static readonly Color DayAmbientEquator = new(0.40f, 0.44f, 0.50f);
        private static readonly Color DayAmbientGround = new(0.22f, 0.19f, 0.17f);
        private static readonly Color NightAmbientSky = new(0.075f, 0.095f, 0.17f);
        private static readonly Color NightAmbientEquator = new(0.05f, 0.06f, 0.10f);
        private static readonly Color NightAmbientGround = new(0.03f, 0.03f, 0.04f);
        private static readonly Color SunsetAmbientSky = new(0.62f, 0.46f, 0.42f);
        private static readonly Color OvercastAmbientSky = new(0.46f, 0.49f, 0.53f);

        private static readonly Color DayFogColor = new(0.62f, 0.74f, 0.88f);
        private static readonly Color NightFogColor = new(0.035f, 0.045f, 0.085f);
        private static readonly Color SunsetFogColor = new(0.86f, 0.52f, 0.36f);
        private static readonly Color OvercastFogColor = new(0.55f, 0.58f, 0.62f);

        private static readonly Color MoonLightColor = new(0.58f, 0.68f, 0.96f);

        private Material? _skyMaterial;
        private Material? _originalSkybox;
        private bool _skyMaterialFailed;
        private bool _isSmoothCustomSky;
        private bool _hasCapturedBaseFogDensity;
        private float _baseFogDensity = 0.0022f;

        public float DayFactor01 { get; private set; } = 1.0f;
        public bool IsMoonLit { get; private set; }

        /// <summary>
        /// Saf matematik: gunes yuksekligine (sunDirY) gore yumusak gunduz agirligi [0..1].
        /// Ufkun 6 derece altinda (sivil alacakaranlik) tamamen gece, ~12 derece ustunde tam gunduz.
        /// </summary>
        public static float ComputeDayFactor01(float sunDirY) => SmoothStep01(-0.10f, 0.20f, sunDirY);

        /// <summary>Gun dogumu / batimi kizillik agirligi [0..1] (gunes ufka yakinken en yuksek).</summary>
        public static float ComputeTwilightFactor01(float sunDirY)
        {
            float a = Mathf.Abs(sunDirY + 0.02f);
            return Mathf.Clamp01(1.0f - a / 0.22f);
        }

        public void Apply(
            UnityEngine.Camera? cam,
            Light? sunLight,
            in SolarEphemState ephem,
            RayleighHorizonTint rayleighTint,
            float cloudCover01,
            float storm01,
            float lightningFlash01,
            bool allowFogColorWrite,
            BiomeVisualProfile? biomeProfile = null,
            float cloudImmersion01 = 0.0f)
        {
            float sunY = ephem.SunDirY;
            float day = ComputeDayFactor01(sunY);
            float twilight = ComputeTwilightFactor01(sunY);
            float cloud = Mathf.Clamp01(cloudCover01);
            float storm = Mathf.Clamp01(storm01);
            float immersion = Mathf.Clamp01(cloudImmersion01);
            DayFactor01 = day;

            // 1) Yonlu isik: Gunes (ufkun ustu) / Ay (ufkun alti). Gecis anindaki siddet ~0 oldugu icin pop olmaz.
            if (sunLight != null)
            {
                Vector3 sunDir = new Vector3(ephem.SunDirX, ephem.SunDirY, ephem.SunDirZ);
                if (sunDir.sqrMagnitude < 1e-4f) sunDir = Vector3.up;
                sunDir.Normalize();

                float flash = Mathf.Clamp01(lightningFlash01) * 1.65f;
                float cloudDim = 1.0f - 0.55f * cloud;
                float stormDim = 1.0f - 0.35f * storm;

                if (sunY >= -0.02f)
                {
                    IsMoonLit = false;
                    Vector3 lightForward = -sunDir;
                    lightForward.y = Mathf.Min(lightForward.y, -0.03f);
                    sunLight.transform.rotation = Quaternion.LookRotation(lightForward.normalized, Vector3.up);
                    float sunStrength = SmoothStep01(-0.02f, 0.22f, sunY);
                    sunLight.color = new Color(rayleighTint.R, rayleighTint.G, rayleighTint.B, 1.0f);
                    sunLight.intensity = 1.05f * sunStrength * cloudDim * stormDim + flash;
                }
                else
                {
                    IsMoonLit = true;
                    // Ay gunesin karsisinda: isik yonu gunes yonuyle ayni (ay -sunDir'de, isik ondan gelir)
                    Vector3 lightForward = sunDir;
                    lightForward.y = Mathf.Min(lightForward.y, -0.18f);
                    sunLight.transform.rotation = Quaternion.LookRotation(lightForward.normalized, Vector3.up);
                    float moonStrength = SmoothStep01(-0.02f, -0.20f, sunY);
                    sunLight.color = MoonLightColor;
                    sunLight.intensity = 0.24f * moonStrength * (1.0f - 0.6f * cloud) + flash;
                }

                if (RenderSettings.sun != sunLight)
                {
                    RenderSettings.sun = sunLight;
                }
            }

            // 2) Prosedurel Skybox (gunes/ay diski, Rayleigh ufuk, gun batimi)
            EnsureSkyMaterial();
            if (_skyMaterial != null)
            {
                if (RenderSettings.skybox != _skyMaterial)
                {
                    RenderSettings.skybox = _skyMaterial;
                }

                float overcastDim = Mathf.Lerp(1.0f, 0.70f, Mathf.Clamp01(cloud * 0.55f + storm * 0.45f));
                float exposure = Mathf.Lerp(0.06f, 1.25f, day) * overcastDim + lightningFlash01 * 1.2f;
                float thickness = Mathf.Lerp(0.85f, 1.65f, twilight) * Mathf.Lerp(1.0f, 0.85f, cloud);
                Color skyTint = Color.Lerp(new Color(0.50f, 0.50f, 0.50f), new Color(0.45f, 0.46f, 0.48f), cloud * storm);
                Color ground = Color.Lerp(new Color(0.05f, 0.05f, 0.07f), new Color(0.37f, 0.35f, 0.34f), day);

                _skyMaterial.SetFloat(ExposureId, exposure);
                _skyMaterial.SetFloat(AtmosphereThicknessId, thickness);
                _skyMaterial.SetColor(SkyTintId, skyTint);
                _skyMaterial.SetColor(GroundColorId, ground);
                _skyMaterial.SetFloat(SunSizeId, IsMoonLit ? 0.030f : Mathf.Lerp(0.045f, 0.020f, cloud));
                _skyMaterial.SetFloat(SunSizeConvergenceId, IsMoonLit ? 9.0f : 5.0f);

                if (_isSmoothCustomSky)
                {
                    Color dayZenith = new Color(0.18f, 0.44f, 0.84f, 1.0f);
                    Color nightZenith = new Color(0.025f, 0.040f, 0.095f, 1.0f);
                    Color horizonBase = Color.Lerp(new Color(0.05f, 0.065f, 0.13f, 1.0f), new Color(0.76f, 0.86f, 0.96f, 1.0f), day);
                    horizonBase = Color.Lerp(horizonBase, new Color(rayleighTint.R, rayleighTint.G, rayleighTint.B, 1.0f), twilight * 0.62f * Mathf.Max(day, 0.35f));

                    _skyMaterial.SetColor(ZenithTintId, Color.Lerp(nightZenith, dayZenith, day));
                    _skyMaterial.SetColor(HorizonTintId, horizonBase);
                    _skyMaterial.SetFloat(NightFactorId, 1.0f - day);
                    _skyMaterial.SetFloat(CloudOvercastId, Mathf.Clamp01(cloud * 0.72f + storm * 0.45f));

                    if (biomeProfile.HasValue)
                    {
                        BiomeVisualProfile bp = biomeProfile.Value;
                        _skyMaterial.SetColor(BiomeSkyTintId, bp.FogAndSkyTint.ToUnityColor(day * 0.85f));
                        float aurora = (bp.RegionId == BiomePaletteRegionId.SunlitGlacialPeaks ||
                                        bp.RegionId == BiomePaletteRegionId.ObsidianVoidCoast) ? 0.90f : 0.0f;
                        _skyMaterial.SetFloat(AuroraStrengthId, aurora);
                    }
                    else
                    {
                        _skyMaterial.SetColor(BiomeSkyTintId, new Color(0.54f, 0.82f, 0.92f, 0.0f));
                        _skyMaterial.SetFloat(AuroraStrengthId, 0.0f);
                    }
                }

                if (cam != null && cam.clearFlags != CameraClearFlags.Skybox)
                {
                    cam.clearFlags = CameraClearFlags.Skybox;
                }
            }

            // 3) Trilight ortam isigi + PT-10 biyom renkli golge harmanlamasi
            Color ambSky = Color.Lerp(NightAmbientSky, DayAmbientSky, day);
            ambSky = Color.Lerp(ambSky, SunsetAmbientSky, twilight * 0.45f * day);
            ambSky = Color.Lerp(ambSky, OvercastAmbientSky * Mathf.Lerp(0.18f, 1.0f, day), cloud * 0.55f);
            Color ambEq = Color.Lerp(NightAmbientEquator, DayAmbientEquator, day);
            Color ambGround = Color.Lerp(NightAmbientGround, DayAmbientGround, day);

            if (biomeProfile.HasValue)
            {
                BiomeVisualProfile bp = biomeProfile.Value;
                Color shadowCol = bp.ColoredShadowTint.ToUnityColor(1.0f);
                Color fogSkyCol = bp.FogAndSkyTint.ToUnityColor(1.0f);
                float dayBlend = Mathf.Lerp(0.22f, 0.55f, day);
                ambSky = Color.Lerp(ambSky, fogSkyCol * Mathf.Lerp(0.24f, 0.82f, day), dayBlend * 0.55f);
                ambEq = Color.Lerp(ambEq, shadowCol * Mathf.Lerp(0.32f, 0.95f, day), dayBlend * 0.65f);
                ambGround = Color.Lerp(ambGround, shadowCol * Mathf.Lerp(0.18f, 0.58f, day), dayBlend * 0.75f);
            }

            float stormAmbientDim = 1.0f - 0.30f * storm;

            if (RenderSettings.ambientMode != AmbientMode.Trilight)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
            }
            RenderSettings.ambientSkyColor = ambSky * stormAmbientDim;
            RenderSettings.ambientEquatorColor = ambEq * stormAmbientDim;
            RenderSettings.ambientGroundColor = ambGround * stormAmbientDim;
            // Uber shader'daki sabit yari-kure ortam tabanini gece/firtinada karart
            Shader.SetGlobalFloat(NightDimId, Mathf.Clamp01((1.0f - day) + storm * 0.25f * day));

            // 4) Sis rengi + PT-10 Biyom sis harmani + PT-09 Bulut ici yumusak sis hissiyati
            if (allowFogColorWrite)
            {
                if (!_hasCapturedBaseFogDensity)
                {
                    _baseFogDensity = RenderSettings.fogDensity > 0.0001f ? RenderSettings.fogDensity : 0.0022f;
                    _hasCapturedBaseFogDensity = true;
                }

                Color fog = Color.Lerp(NightFogColor, DayFogColor, day);
                if (biomeProfile.HasValue)
                {
                    Color biomeFog = biomeProfile.Value.FogAndSkyTint.ToUnityColor(1.0f) * Mathf.Lerp(0.16f, 0.94f, day);
                    fog = Color.Lerp(fog, biomeFog, 0.48f);
                }
                fog = Color.Lerp(fog, SunsetFogColor, twilight * 0.55f * Mathf.Max(day, 0.35f));
                fog = Color.Lerp(fog, OvercastFogColor * Mathf.Lerp(0.15f, 1.0f, day), cloud * 0.6f);
                fog *= 1.0f - 0.25f * storm;

                // PT-09: Bulut katmaninin icine ucunca yumusak aydinlik bulut sisi; ustune cikinca kristal berrak gokyuzu!
                if (immersion > 0.005f)
                {
                    Color cloudMistDay = Color.Lerp(new Color(0.93f, 0.96f, 0.99f, 1.0f), new Color(0.58f, 0.63f, 0.70f, 1.0f), storm * 0.75f);
                    Color cloudMistNight = new Color(0.12f, 0.15f, 0.22f, 1.0f);
                    Color cloudMist = Color.Lerp(cloudMistNight, cloudMistDay, day);
                    fog = Color.Lerp(fog, cloudMist, immersion * 0.88f);
                    RenderSettings.fogDensity = Mathf.Lerp(_baseFogDensity, 0.0185f, immersion);
                }
                else
                {
                    RenderSettings.fogDensity = _baseFogDensity;
                }

                fog.a = 1.0f;
                RenderSettings.fogColor = fog;
                if (cam != null)
                {
                    cam.backgroundColor = fog;
                }
            }
        }

        private void EnsureSkyMaterial()
        {
            if (_skyMaterial != null || _skyMaterialFailed) return;

            Material? source = RenderSettings.skybox;
            _originalSkybox = source;

            // PT-09: Kare-kare / 8-bit banding artefaktini kokunden gideren per-pixel IGN-dithered ozel gokyuzu shader'i
            Shader? smoothShader = Shader.Find("Stratocraft/SmoothProceduralSky");
            if (smoothShader != null)
            {
                _skyMaterial = new Material(smoothShader)
                {
                    name = "StratocraftSmoothSky_Runtime",
                    hideFlags = HideFlags.DontSave
                };
                _isSmoothCustomSky = true;
                return;
            }

            Shader? shader = source != null && source.shader != null && source.shader.name == "Skybox/Procedural"
                ? source.shader
                : Shader.Find("Skybox/Procedural");

            if (shader == null)
            {
                _skyMaterialFailed = true;
                Debug.LogWarning("[Stratocraft] Skybox shader bulunamadi; gokyuzu duz renkte kalacak.");
                return;
            }

            _skyMaterial = source != null && source.shader == shader
                ? new Material(source)
                : new Material(shader);
            _skyMaterial.name = "StratocraftDynamicSky_Runtime";
            _skyMaterial.hideFlags = HideFlags.DontSave;
            _skyMaterial.EnableKeyword("_SUNDISK_HIGH_QUALITY");
            _skyMaterial.DisableKeyword("_SUNDISK_NONE");
            _skyMaterial.DisableKeyword("_SUNDISK_SIMPLE");
            _isSmoothCustomSky = false;
        }

        public void Dispose()
        {
            Shader.SetGlobalFloat(NightDimId, 0.0f);
            if (_hasCapturedBaseFogDensity)
            {
                RenderSettings.fogDensity = _baseFogDensity;
            }
            if (_skyMaterial != null)
            {
                if (RenderSettings.skybox == _skyMaterial)
                {
                    RenderSettings.skybox = _originalSkybox;
                }
                Object.Destroy(_skyMaterial);
                _skyMaterial = null;
            }
        }

        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
#endif
