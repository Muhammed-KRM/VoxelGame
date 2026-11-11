#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stratocraft.Rendering
{
    /// <summary>
    /// Kamera su (veya lav/asit) yuzeyinin altina indiginde:
    /// 1. Beer-Lambert su alti mavi-turkuaz derinlik sisini (RenderSettings.fog),
    /// 2. URP Post-Processing ColorAdjustments mavi tonlamasini,
    /// 3. URP DepthOfField (Gaussian) hafif su alti kirilma bulanikligini
    /// 0 B doku VRAM ve 0 B/kare GC tahsisiyle uygular; kamera yuzeye ciktiginda sahne sisini birebir geri yukler.
    /// </summary>
    public sealed class UnderwaterCameraEffect : IDisposable
    {
        private bool _capturedBaselineFog;
        private bool _baseFogEnabled;
        private FogMode _baseFogMode;
        private Color _baseFogColor;
        private float _baseFogDensity;
        private float _baseFogStart;
        private float _baseFogEnd;

        private Camera? _trackedCamera;
        private UniversalAdditionalCameraData? _trackedCamData;
        private bool _baseRenderPostProcessing;

        private GameObject? _volumeHostGo;
        private Volume? _underwaterVolume;
        private VolumeProfile? _underwaterProfile;
        private ColorAdjustments? _colorAdjustments;
        private DepthOfField? _depthOfField;

        private float _blend01;
        private byte _activeFluidType;

        public float CurrentBlend01 => _blend01;
        public byte ActiveFluidType => _activeFluidType;

        public void Step(Camera? cam, float targetSubmersion01, byte fluidType, float deltaTime)
        {
            targetSubmersion01 = Mathf.Clamp01(targetSubmersion01);
            if (targetSubmersion01 > 0.001f && fluidType != 0)
            {
                _activeFluidType = fluidType;
            }

            float speed = targetSubmersion01 > _blend01 ? 10.5f : 7.5f;
            float t = 1f - Mathf.Exp(-speed * Mathf.Clamp(deltaTime, 0.0005f, 0.1f));
            _blend01 = Mathf.MoveTowards(Mathf.Lerp(_blend01, targetSubmersion01, t), targetSubmersion01, 0.002f);

            if (_blend01 <= 0.001f)
            {
                if (_capturedBaselineFog)
                {
                    RestoreBaselineState();
                }

                if (_underwaterVolume != null && _underwaterVolume.enabled)
                {
                    _underwaterVolume.weight = 0f;
                    _underwaterVolume.enabled = false;
                }

                _blend01 = 0f;
                return;
            }

            CaptureBaselineIfNeeded(cam);
            EnsureUnderwaterVolumeCreated();

            // 1. Su alti Beer-Lambert derinlik sisi
            Color targetFogCol;
            float targetFogDens;
            Color targetTintFilter;

            if (_activeFluidType == 2) // Lav
            {
                targetFogCol = new Color(0.68f, 0.18f, 0.04f, 1f);
                targetFogDens = 0.24f;
                targetTintFilter = new Color(1.00f, 0.50f, 0.22f, 1f);
            }
            else if (_activeFluidType == 3) // Asit
            {
                targetFogCol = new Color(0.12f, 0.48f, 0.12f, 1f);
                targetFogDens = 0.14f;
                targetTintFilter = new Color(0.62f, 0.98f, 0.55f, 1f);
            }
            else // Su (FluidType == 1)
            {
                targetFogCol = new Color(0.05f, 0.34f, 0.56f, 1f);
                targetFogDens = 0.085f;
                targetTintFilter = new Color(0.56f, 0.84f, 1.00f, 1f);
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            Color startCol = _baseFogEnabled ? _baseFogColor : new Color(0.55f, 0.76f, 0.92f, 1f);
            float startDens = _baseFogEnabled ? _baseFogDensity : 0.002f;
            RenderSettings.fogColor = Color.Lerp(startCol, targetFogCol, _blend01);
            RenderSettings.fogDensity = Mathf.Lerp(startDens, targetFogDens, _blend01);

            // 2. URP Post-Processing (Mavi Filtre + Hafif Su Alti Kirilma Bulanikligi)
            if (_underwaterVolume != null && _colorAdjustments != null && _depthOfField != null)
            {
                if (!_underwaterVolume.enabled)
                {
                    _underwaterVolume.enabled = true;
                }

                _underwaterVolume.weight = _blend01;
                _colorAdjustments.colorFilter.value = Color.Lerp(Color.white, targetTintFilter, _blend01);
                _colorAdjustments.postExposure.value = -0.18f * _blend01;
                _colorAdjustments.contrast.value = -8.0f * _blend01;
                _colorAdjustments.saturation.value = 8.0f * _blend01;

                // Yakindaki bloklari okunur tutup orta/uzak su icini yumusak bulaniklastiran Gaussian DoF
                _depthOfField.gaussianStart.value = Mathf.Lerp(12.0f, 1.35f, _blend01);
                _depthOfField.gaussianEnd.value = Mathf.Lerp(32.0f, 8.50f, _blend01);
                _depthOfField.gaussianMaxRadius.value = Mathf.Lerp(0.0f, 0.90f, _blend01);
            }
        }

        /// <summary>
        /// Post-Processing kapali kalite profillerinde dahi su altinda %100 gorunur mavi optik perde saglar (0 B doku VRAM).
        /// </summary>
        public void DrawUnderwaterScreenOverlayGui()
        {
            if (_blend01 <= 0.005f)
            {
                return;
            }

            Color prev = GUI.color;
            Color veilColor = _activeFluidType switch
            {
                2 => new Color(0.85f, 0.24f, 0.04f, 0.34f * _blend01),
                3 => new Color(0.14f, 0.62f, 0.16f, 0.24f * _blend01),
                _ => new Color(0.06f, 0.42f, 0.74f, 0.22f * _blend01)
            };

            GUI.color = veilColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void CaptureBaselineIfNeeded(Camera? cam)
        {
            if (!_capturedBaselineFog)
            {
                _baseFogEnabled = RenderSettings.fog;
                _baseFogMode = RenderSettings.fogMode;
                _baseFogColor = RenderSettings.fogColor;
                _baseFogDensity = RenderSettings.fogDensity;
                _baseFogStart = RenderSettings.fogStartDistance;
                _baseFogEnd = RenderSettings.fogEndDistance;
                _capturedBaselineFog = true;
            }

            if (cam != null && _trackedCamera != cam)
            {
                if (_trackedCamData != null)
                {
                    _trackedCamData.renderPostProcessing = _baseRenderPostProcessing;
                }

                _trackedCamera = cam;
                _trackedCamData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (_trackedCamData != null)
                {
                    _baseRenderPostProcessing = _trackedCamData.renderPostProcessing;
                    _trackedCamData.renderPostProcessing = true;
                }
            }
            else if (_trackedCamData != null && !_trackedCamData.renderPostProcessing)
            {
                _trackedCamData.renderPostProcessing = true;
            }
        }

        private void RestoreBaselineState()
        {
            if (_capturedBaselineFog)
            {
                RenderSettings.fog = _baseFogEnabled;
                RenderSettings.fogMode = _baseFogMode;
                RenderSettings.fogColor = _baseFogColor;
                RenderSettings.fogDensity = _baseFogDensity;
                RenderSettings.fogStartDistance = _baseFogStart;
                RenderSettings.fogEndDistance = _baseFogEnd;
                _capturedBaselineFog = false;
            }

            if (_trackedCamData != null)
            {
                _trackedCamData.renderPostProcessing = _baseRenderPostProcessing;
                _trackedCamData = null;
                _trackedCamera = null;
            }
        }

        private void EnsureUnderwaterVolumeCreated()
        {
            if (_underwaterVolume != null)
            {
                return;
            }

            _volumeHostGo = new GameObject("Stratocraft_UnderwaterPostVolume")
            {
                hideFlags = HideFlags.DontSave
            };

            _underwaterProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _underwaterProfile.name = "Stratocraft_UnderwaterProfile";
            _underwaterProfile.hideFlags = HideFlags.DontSave;

            _colorAdjustments = _underwaterProfile.Add<ColorAdjustments>(overrides: true);
            _colorAdjustments.active = true;

            _depthOfField = _underwaterProfile.Add<DepthOfField>(overrides: true);
            _depthOfField.active = true;
            _depthOfField.mode.overrideState = true;
            _depthOfField.mode.value = DepthOfFieldMode.Gaussian;

            _underwaterVolume = _volumeHostGo.AddComponent<Volume>();
            _underwaterVolume.isGlobal = true;
            _underwaterVolume.priority = 85f;
            _underwaterVolume.weight = 0f;
            _underwaterVolume.sharedProfile = _underwaterProfile;
        }

        public void Dispose()
        {
            RestoreBaselineState();

            if (_underwaterProfile != null)
            {
                UnityEngine.Object.Destroy(_underwaterProfile);
                _underwaterProfile = null;
            }

            if (_volumeHostGo != null)
            {
                UnityEngine.Object.Destroy(_volumeHostGo);
                _volumeHostGo = null;
            }

            _underwaterVolume = null;
            _colorAdjustments = null;
            _depthOfField = null;
        }
    }
}
