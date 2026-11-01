using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.NativeBridge;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Stratocraft.UI
{
    /// <summary>
    /// Bolum 4.12 ve 5.1.3 Adim 5 geregi F3 tusuyla acilip kapanan canli teknik telemetri HUD ekrani:
    /// FPS, Frame ms (<= 15.0 ms hedefi), Toplam RAM MB (< 2.500 MB), Kaplama VRAM MB (< 16.0 MB),
    /// GC Tahsisati ve C++20 SIMD (AVX2) durumu.
    /// </summary>
    public sealed class F3TelemetryOverlayUi : MonoBehaviour
    {
        private bool _isVisible = true;
        private float _smoothedDeltaTime = 0.016f;
        private float _refreshTimer;
        private long _cachedTotalRamMb;
        private float _cachedTextureVramMb;
        private long _lastGcTotalBytes;
        private long _gcDeltaBytesPerFrame;
        private string _simdStatusLabel = "Checking...";
        private GUIStyle? _boxStyle;
        private GUIStyle? _labelStyle;

        private void Start()
        {
            try
            {
                _simdStatusLabel = StratocraftNativeBindings.GetSimdCapabilityLabel();
            }
            catch
            {
                _simdStatusLabel = "DLL Not Loaded";
            }

            _lastGcTotalBytes = GC.GetTotalMemory(forceFullCollection: false);
            SampleMemoryMetrics();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                _isVisible = !_isVisible;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f)
            {
                _smoothedDeltaTime += (dt - _smoothedDeltaTime) * 0.1f;
            }

            long currentGc = GC.GetTotalMemory(forceFullCollection: false);
            _gcDeltaBytesPerFrame = Math.Max(0L, currentGc - _lastGcTotalBytes);
            _lastGcTotalBytes = currentGc;

            _refreshTimer += dt;
            if (_refreshTimer >= 0.5f)
            {
                _refreshTimer = 0f;
                SampleMemoryMetrics();
            }
        }

        private void SampleMemoryMetrics()
        {
            long totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
            _cachedTotalRamMb = totalAllocated / (1024L * 1024L);

            long texBytes = Profiler.GetAllocatedMemoryForGraphicsDriver();
            float streamingMb = Texture.currentTextureMemory / (1024f * 1024f);
            _cachedTextureVramMb = streamingMb > 0.01f
                ? streamingMb
                : (texBytes / (1024f * 1024f));
        }

        private void OnGUI()
        {
            if (!_isVisible)
            {
                return;
            }

            EnsureStyles();

            float frameMs = _smoothedDeltaTime * 1000f;
            float fps = 1.0f / Mathf.Max(_smoothedDeltaTime, 0.0001f);

            Rect panelRect = new Rect(12f, 12f, 410f, 138f);
            GUI.Box(panelRect, GUIContent.none, _boxStyle!);

            GUILayout.BeginArea(new Rect(22f, 18f, 390f, 126f));
            GUILayout.Label($"<b>STRATOCRAFT TELEMETRY (F3)</b> | SIMD: {_simdStatusLabel}", _labelStyle!);
            GUILayout.Label($"FPS: {fps:0.0} | Frame: {frameMs:0.00} ms (Hedef <= {StratocraftConstants.TargetFrameBudgetMs:0.0} ms)", _labelStyle!);
            GUILayout.Label($"Oyun RAM: {_cachedTotalRamMb} MB / {StratocraftConstants.MaxGameRamBudgetMb} MB", _labelStyle!);
            GUILayout.Label($"Texture VRAM: {_cachedTextureVramMb:0.00} MB / {StratocraftConstants.MaxTextureVramBudgetMb:0.0} MB", _labelStyle!);
            GUILayout.Label($"GC Alloc/Frame: {_gcDeltaBytesPerFrame} B (Burst Hedef: 0 B)", _labelStyle!);
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null && _labelStyle != null)
            {
                return;
            }

            _boxStyle = new GUIStyle(GUI.skin.box);
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 13
            };
            _labelStyle.normal.textColor = new Color(0.88f, 0.96f, 0.90f, 1.0f);
        }
    }
}
