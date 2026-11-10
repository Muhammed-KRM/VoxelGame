#if UNITY_5_3_OR_NEWER
#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Character.Diagnostics
{
    /// <summary>
    /// FAZ A2: URP uyumlu 3B Savurma Yörüngesi ve Çarpışma/Saplanma/Sekme Görselleştiricisi (Insert tuşu ile aç/kapa).
    /// </summary>
    public static class SwingTraceGizmoRenderer
    {
        private static Material? _lineMat;

        private static Material? EnsureLineMaterial()
        {
            if (_lineMat != null) return _lineMat;
            Shader? sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return null;
            _lineMat = new Material(sh)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull", (int)CullMode.Off);
            _lineMat.SetInt("_ZWrite", 0);
            _lineMat.SetInt("_ZTest", (int)CompareFunction.Always);
            return _lineMat;
        }

        public static void DrawTrails(SwingTraceRecorder? recorder)
        {
            if (recorder == null || !recorder.IsEnabled || !recorder.ShowVisualGizmos) return;
            Material? mat = EnsureLineMaterial();
            if (mat == null) return;

            CompletedSwingTrail[] trails = recorder.VisualTrails;
            mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            for (int t = 0; t < trails.Length; t++)
            {
                ref CompletedSwingTrail trail = ref trails[t];
                if (trail.SwingId < 0 || trail.PointCount < 2) continue;

                Color tipColor = trail.PeakPower01 < 0.35f
                    ? new Color(0.45f, 0.85f, 1.0f, 0.85f)   // Mikro kesik: Açık Mavi
                    : new Color(0.15f, 1.0f, 0.35f, 0.92f);  // Tam savurma: Parlak Yeşil
                Color headColor = new Color(0.20f, 0.65f, 1.0f, 0.80f);

                for (int i = 1; i < trail.PointCount; i++)
                {
                    GL.Color(tipColor);
                    GL.Vertex(trail.TipPoints[i - 1]);
                    GL.Vertex(trail.TipPoints[i]);

                    if (trail.HolsterSlot == 2 || trail.HolsterSlot == 3)
                    {
                        GL.Color(headColor);
                        GL.Vertex(trail.HeadPoints[i - 1]);
                        GL.Vertex(trail.HeadPoints[i]);
                    }
                }

                if (trail.HadContact)
                {
                    // Outcome: 2 = Ricochet (Sarı), 3 = Lodge (Mor), 1 = Cleave (Kırmızı)
                    Color markerCol = trail.Outcome == 3
                        ? new Color(0.85f, 0.20f, 1.0f, 1.0f)
                        : (trail.Outcome == 2
                            ? new Color(1.0f, 0.90f, 0.15f, 1.0f)
                            : new Color(1.0f, 0.20f, 0.20f, 1.0f));
                    DrawWireCross(trail.ContactPoint, 0.09f, markerCol);

                    if (trail.Outcome == 2 && trail.RicochetDir.sqrMagnitude > 0.001f)
                    {
                        GL.Color(new Color(1.0f, 0.90f, 0.15f, 1.0f));
                        GL.Vertex(trail.ContactPoint);
                        GL.Vertex(trail.ContactPoint + trail.RicochetDir.normalized * 0.42f);
                    }
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        private static void DrawWireCross(Vector3 center, float radius, Color c)
        {
            GL.Color(c);
            GL.Vertex(center - new Vector3(radius, 0f, 0f));
            GL.Vertex(center + new Vector3(radius, 0f, 0f));
            GL.Vertex(center - new Vector3(0f, radius, 0f));
            GL.Vertex(center + new Vector3(0f, radius, 0f));
            GL.Vertex(center - new Vector3(0f, 0f, radius));
            GL.Vertex(center + new Vector3(0f, 0f, radius));
        }
    }
}
#endif
