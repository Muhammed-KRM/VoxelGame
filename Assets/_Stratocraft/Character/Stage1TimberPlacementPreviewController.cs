#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.RigidTimber;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec3 = System.Numerics.Vector3;

namespace Stratocraft.Character
{
    public enum TimberBuildPreviewKind : byte
    {
        None = 0,
        RealLogFromCart = 1,
        LogArchCart = 2,
        WoodScaffolding = 3,
        ParbuckleRamp2To1 = 4,
        GinPoleHoist = 5,
        SawhorseBench = 6,
        StoneFireplace = 7
    }

    /// <summary>
    /// Gercekci 1:1 3B Hayalet Onizleme + [T] Menusu Tek-Parca Insaat Paneli (Sifir gizli kisayol!).
    /// - Gercek kestigin agaci Kutuk El Arabasina yuklediginde baktigin yerde 1:1 kütük önizlemesi cikar:
    ///   [V] Dikey (Cukura Dik) / Yatay (Duvar-Kiris) | [R] 45° Dondur | [Sol Tik] Yerlestir!
    /// - [T] Insa Menusu acikken 6 santiye/ev-ici aracindan birine tiklayinca 1:1 3B onizleme acilir:
    ///   [R] Dondur | [Sol Tik] Kur | [Sag Tik / ESC] Iptal!
    /// </summary>
    public sealed class Stage1TimberPlacementPreviewController
    {
        private TimberBuildPreviewKind _activeKind = TimberBuildPreviewKind.None;
        private bool _logVerticalMode = true;
        private float _previewYawDeg;
        private float _lastBuiltLen;
        private TimberBuildPreviewKind _lastBuiltKind;
        private bool _lastBuiltVertical;

        private GameObject? _ghostGo;
        private Material? _ghostMat;
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(128);

        public bool HasActiveToolPreview => _activeKind != TimberBuildPreviewKind.None && _activeKind != TimberBuildPreviewKind.RealLogFromCart;

        public void Dispose()
        {
            if (_ghostGo != null)
            {
                UnityEngine.Object.Destroy(_ghostGo);
                _ghostGo = null;
            }
            if (_ghostMat != null)
            {
                UnityEngine.Object.Destroy(_ghostMat);
                _ghostMat = null;
            }
            _activeKind = TimberBuildPreviewKind.None;
        }

        public void CancelPreview()
        {
            _activeKind = TimberBuildPreviewKind.None;
            if (_ghostGo != null) _ghostGo.SetActive(false);
        }

        public void StartToolPreview(TimberBuildPreviewKind kind, float initialYawDeg, out string feedback)
        {
            _activeKind = kind;
            _previewYawDeg = Mathf.Round(initialYawDeg / 45f) * 45f;
            feedback = $"📐 Önizleme Aktif ({GetKindLabel(kind)}) | [R] 45° Döndür | [Sol Tık] Tam Oraya Kur | [Sağ Tık / ESC] İptal";
        }

        /// <summary>
        /// [T] Insa Menusu acikken ekranin saginda temiz "Tek-Parca Insaat & Santiye" panelini cizer.
        /// Bir butona tiklanirsa menuyu kapatip 1:1 3B onizleme baslatir.
        /// </summary>
        public bool DrawMindMenuConstructionPanel(
            float playerYawDeg,
            bool isCreativeMode,
            out string feedback)
        {
            feedback = string.Empty;
            float panelW = 290f;
            float panelH = 315f;
            float x = Screen.width - panelW - 22f;
            float y = 84f;

            GUI.Box(new Rect(x, y, panelW, panelH), "🪵 TEK-PARÇA ŞANTİYE & EV İÇİ");

            float btnY = y + 30f;
            const float btnH = 34f;
            const float gap = 6f;

            if (GUI.Button(new Rect(x + 12f, btnY, panelW - 24f, btnH), isCreativeMode ? "🛞 Kütük El Arabası (Log Arch)" : "🛞 Kütük El Arabası (2 Dal + 2 Lif)"))
            {
                StartToolPreview(TimberBuildPreviewKind.LogArchCart, playerYawDeg, out feedback);
                return true;
            }
            btnY += btnH + gap;

            if (GUI.Button(new Rect(x + 12f, btnY, panelW - 24f, btnH), isCreativeMode ? "🪜 Ahşap İskele (2×2×2m)" : "🪜 Ahşap İskele (4 Dal + 2 Lif)"))
            {
                StartToolPreview(TimberBuildPreviewKind.WoodScaffolding, playerYawDeg, out feedback);
                return true;
            }
            btnY += btnH + gap;

            if (GUI.Button(new Rect(x + 12f, btnY, panelW - 24f, btnH), isCreativeMode ? "🪢 Parbuckle 2:1 Eğik Rampa" : "🪢 Parbuckle Rampa (2 Dal + 2 Lif)"))
            {
                StartToolPreview(TimberBuildPreviewKind.ParbuckleRamp2To1, playerYawDeg, out feedback);
                return true;
            }
            btnY += btnH + gap;

            if (GUI.Button(new Rect(x + 12f, btnY, panelW - 24f, btnH), isCreativeMode ? "🏗️ Gin Pole Makaralı Vinç" : "🏗️ Gin Pole Vinç (2 Direk + 4 Lif)"))
            {
                StartToolPreview(TimberBuildPreviewKind.GinPoleHoist, playerYawDeg, out feedback);
                return true;
            }
            btnY += btnH + gap;

            if (GUI.Button(new Rect(x + 12f, btnY, panelW - 24f, btnH), isCreativeMode ? "🪚 Bıçkı / Marangoz Sehpası" : "🪚 Bıçkı Sehpası (2 Kalas / 1 Meşe)"))
            {
                StartToolPreview(TimberBuildPreviewKind.SawhorseBench, playerYawDeg, out feedback);
                return true;
            }
            btnY += btnH + gap;

            if (GUI.Button(new Rect(x + 12f, btnY, panelW - 24f, btnH), isCreativeMode ? "🔥 Taş Şömine & Baca" : "🔥 Taş Şömine & Baca (6 Ham Taş)"))
            {
                StartToolPreview(TimberBuildPreviewKind.StoneFireplace, playerYawDeg, out feedback);
                return true;
            }
            btnY += btnH + 8f;

            GUI.Label(new Rect(x + 12f, btnY, panelW - 24f, 44f),
                "💡 Kestiğin gerçek ağacı arabaya [E] ile bağladığında otomatik 1:1 önizleme açılır: [V] Dik/Yatay | [R] Döndür | [Sol Tık] Koy!");

            return false;
        }

        /// <summary>
        /// Her karede 1:1 3B hayalet önizlemeyi günceller; [V], [R], [Sol Tık] ve [Sağ Tık/ESC] girdilerini işler.
        /// </summary>
        public bool StepPreviewAndPlacement(
            bool menuOpen,
            UnityEngine.Camera? cam,
            Transform playerTr,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1LogArchCartController logCartCtrl,
            Stage1RigidTimberBridge rigidTimberBridge,
            Stage1TimberSiteRigController siteRigCtrl,
            Stage1TimberInteriorController interiorCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            out string hudHint)
        {
            hudHint = string.Empty;
            if (menuOpen || miningCtrl.IsCrystalActive)
            {
                if (_ghostGo != null) _ghostGo.SetActive(false);
                return false;
            }

            // Arabada yuklu gercek kutuk varsa ve baska bir arac onizlemesi acik degilse -> OTOMATIK 1:1 KUTUK ONIZLEMESI!
            bool hasCartLog = logCartCtrl.TryPeekBoundLog(out float cartLogLen, out float cartLogDiam, out byte cartWoodMat);
            TimberBuildPreviewKind effectiveKind = HasActiveToolPreview
                ? _activeKind
                : (hasCartLog ? TimberBuildPreviewKind.RealLogFromCart : TimberBuildPreviewKind.None);

            if (effectiveKind == TimberBuildPreviewKind.None)
            {
                if (_ghostGo != null) _ghostGo.SetActive(false);
                return false;
            }

            // Iptal ([Sag Tik] / [ESC] / [Q]) — sadece arac onizlemesi icin
            if (HasActiveToolPreview && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)))
            {
                CancelPreview();
                miningCtrl.LastFeedbackText = "Önizleme iptal edildi.";
                return true;
            }

            // [R]: 45 derece dondur
            if (Input.GetKeyDown(KeyCode.R))
            {
                _previewYawDeg = (_previewYawDeg + 45f) % 360f;
            }

            // [V]: Gercek kutuk icin Dikey (Cukura Dik) <-> Yatay (Duvar/Kiris) gecisi!
            if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart && Input.GetKeyDown(KeyCode.V))
            {
                _logVerticalMode = !_logVerticalMode;
            }

            // Hedef nokta (nisangah veya 3.2m on)
            Vector3 fwdFlat = cam != null ? new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z).normalized : playerTr.forward;
            if (fwdFlat.sqrMagnitude < 1e-4f) fwdFlat = Vector3.forward;
            Vector3 targetPos = miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 7.5f
                ? miningCtrl.AimHit.point
                : playerTr.position + fwdFlat * 3.2f;

            // 1:1 Hayalet gorselini olustur/guncelle
            EnsureGhostVisual(effectiveKind, hasCartLog ? cartLogLen : 4.5f, _logVerticalMode);
            if (_ghostGo != null)
            {
                _ghostGo.SetActive(true);
                if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart)
                {
                    if (_logVerticalMode)
                    {
                        _ghostGo.transform.position = targetPos + Vector3.up * (cartLogLen * 0.5f);
                        _ghostGo.transform.rotation = Quaternion.Euler(0f, _previewYawDeg, 0f);
                    }
                    else
                    {
                        _ghostGo.transform.position = targetPos + Vector3.up * (cartLogDiam * 0.5f);
                        _ghostGo.transform.rotation = Quaternion.Euler(0f, _previewYawDeg, 0f) * Quaternion.Euler(0f, 0f, 90f);
                    }
                }
                else
                {
                    _ghostGo.transform.position = targetPos;
                    _ghostGo.transform.rotation = Quaternion.Euler(0f, _previewYawDeg, 0f);
                }
            }

            hudHint = effectiveKind == TimberBuildPreviewKind.RealLogFromCart
                ? $"🪵 GERÇEK KÜTÜK YERLEŞTİRME ({cartLogLen:0.1}m | {(_logVerticalMode ? "DİKEY — Çukura Dik" : "YATAY — Duvar/Kiriş")}) | [V] Dik/Yatay | [R] Döndür ({_previewYawDeg:0}°) | [Sol Tık] Koy | [E] Yere Bırak"
                : $"📐 {GetKindLabel(effectiveKind)} | [R] Döndür ({_previewYawDeg:0}°) | [Sol Tık] Kur | [Sağ Tık / ESC] İptal";

            // [Sol Tik]: Tam hayaletin oldugu yere kur/yerlestir!
            if (Input.GetMouseButtonDown(0))
            {
                bool isCreative = holsterCtrl.IsCreativeMode;
                switch (effectiveKind)
                {
                    case TimberBuildPreviewKind.RealLogFromCart:
                        if (logCartCtrl.TryConsumeBoundLogForPlacement(out float len, out float diam, out byte wMat))
                        {
                            TimberWoodSpecies sp = wMat == MaterialIdCatalog.Ironwood ? TimberWoodSpecies.Ironwood : TimberWoodSpecies.Oak;
                            if (_logVerticalMode)
                            {
                                rigidTimberBridge.TryTipInLogIntoHole(targetPos, len, diam, sp, sharedVoxelMat, worldChunks, out string tipMsg);
                                miningCtrl.LastFeedbackText = tipMsg;
                            }
                            else
                            {
                                rigidTimberBridge.PlaceHorizontalLogOrBeam(targetPos + Vector3.up * (diam * 0.5f), _previewYawDeg, len, diam, sp, sharedVoxelMat, worldChunks, out string hMsg);
                                miningCtrl.LastFeedbackText = hMsg;
                            }
                        }
                        return true;

                    case TimberBuildPreviewKind.LogArchCart:
                        bool canCart = isCreative ||
                            (holsterCtrl.InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.Stick, 0, 2) &&
                             holsterCtrl.InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.PlantFiber, 0, 2));
                        if (!canCart)
                        {
                            miningCtrl.LastFeedbackText = "🛞 Kütük El Arabası için 2x Dal + 2x Bitki Lifi gerekir!";
                        }
                        else
                        {
                            logCartCtrl.SpawnLogArchCart(targetPos, _previewYawDeg, sharedVoxelMat, worldChunks, out string cMsg);
                            miningCtrl.LastFeedbackText = cMsg;
                            CancelPreview();
                        }
                        return true;

                    case TimberBuildPreviewKind.WoodScaffolding:
                        if (siteRigCtrl.TryBuildSiteRig(TimberSiteRigKind.WoodScaffolding, targetPos, _previewYawDeg, isCreative, holsterCtrl, sharedVoxelMat, out string sMsg))
                            CancelPreview();
                        miningCtrl.LastFeedbackText = sMsg;
                        return true;

                    case TimberBuildPreviewKind.ParbuckleRamp2To1:
                        if (siteRigCtrl.TryBuildSiteRig(TimberSiteRigKind.ParbuckleRamp2To1, targetPos, _previewYawDeg, isCreative, holsterCtrl, sharedVoxelMat, out string rMsg))
                            CancelPreview();
                        miningCtrl.LastFeedbackText = rMsg;
                        return true;

                    case TimberBuildPreviewKind.GinPoleHoist:
                        if (siteRigCtrl.TryBuildSiteRig(TimberSiteRigKind.GinPoleHoist, targetPos, _previewYawDeg, isCreative, holsterCtrl, sharedVoxelMat, out string gMsg))
                            CancelPreview();
                        miningCtrl.LastFeedbackText = gMsg;
                        return true;

                    case TimberBuildPreviewKind.SawhorseBench:
                        if (interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.SawhorseBench, targetPos, _previewYawDeg, isCreative, holsterCtrl, sharedVoxelMat, out string swMsg))
                            CancelPreview();
                        miningCtrl.LastFeedbackText = swMsg;
                        return true;

                    case TimberBuildPreviewKind.StoneFireplace:
                        if (interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.StoneFireplaceWithChimney, targetPos, _previewYawDeg, isCreative, holsterCtrl, sharedVoxelMat, out string fpMsg))
                            CancelPreview();
                        miningCtrl.LastFeedbackText = fpMsg;
                        return true;
                }
            }

            return HasActiveToolPreview;
        }

        private void EnsureGhostVisual(TimberBuildPreviewKind kind, float logLen, bool vertical)
        {
            if (_ghostGo != null && _lastBuiltKind == kind && Mathf.Abs(_lastBuiltLen - logLen) < 0.1f && _lastBuiltVertical == vertical)
            {
                return;
            }

            if (_ghostGo != null) UnityEngine.Object.Destroy(_ghostGo);
            _ghostGo = new GameObject($"TimberPreviewGhost_{kind}");
            _lastBuiltKind = kind;
            _lastBuiltLen = logLen;
            _lastBuiltVertical = vertical;

            if (_ghostMat == null)
            {
                Shader? sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                _ghostMat = new Material(sh);
                _ghostMat.color = new Color(0.28f, 0.88f, 0.45f, 0.48f);
                if (_ghostMat.HasProperty("_Surface")) _ghostMat.SetFloat("_Surface", 1f);
                if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", new Color(0.28f, 0.88f, 0.45f, 0.48f));
            }

            _meshScratch.Clear();
            switch (kind)
            {
                case TimberBuildPreviewKind.RealLogFromCart:
                    EmitBox(_meshScratch, new Vector3(-0.27f, -logLen * 0.5f, -0.27f), new Vector3(0.27f, logLen * 0.5f, 0.27f), MaterialIdCatalog.OakWood);
                    break;
                case TimberBuildPreviewKind.LogArchCart:
                    EmitBox(_meshScratch, new Vector3(-0.80f, 0.05f, -0.50f), new Vector3(0.80f, 1.45f, 2.00f), MaterialIdCatalog.OakWood);
                    break;
                case TimberBuildPreviewKind.WoodScaffolding:
                    EmitBox(_meshScratch, new Vector3(-1.00f, 0f, -1.00f), new Vector3(1.00f, 2.05f, 1.00f), MaterialIdCatalog.WoodPlank);
                    break;
                case TimberBuildPreviewKind.ParbuckleRamp2To1:
                    EmitBox(_meshScratch, new Vector3(-1.25f, 0.10f, -2.50f), new Vector3(1.25f, 3.20f, 1.90f), MaterialIdCatalog.OakWood);
                    break;
                case TimberBuildPreviewKind.GinPoleHoist:
                    EmitBox(_meshScratch, new Vector3(-1.20f, 0f, -0.25f), new Vector3(1.20f, 6.80f, 0.95f), MaterialIdCatalog.OakWood);
                    break;
                case TimberBuildPreviewKind.SawhorseBench:
                    EmitBox(_meshScratch, new Vector3(-1.05f, 0f, -0.28f), new Vector3(1.05f, 0.94f, 0.28f), MaterialIdCatalog.WoodPlank);
                    break;
                case TimberBuildPreviewKind.StoneFireplace:
                    EmitBox(_meshScratch, new Vector3(-0.90f, 0f, -0.65f), new Vector3(0.90f, 2.00f, 0.65f), MaterialIdCatalog.StoneBrick);
                    break;
            }

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_Ghost_{kind}");
            _ghostGo.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = _ghostGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ghostMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static string GetKindLabel(TimberBuildPreviewKind kind) => kind switch
        {
            TimberBuildPreviewKind.LogArchCart => "Kütük El Arabası",
            TimberBuildPreviewKind.WoodScaffolding => "Ahşap İskele (2×2×2m)",
            TimberBuildPreviewKind.ParbuckleRamp2To1 => "Parbuckle 2:1 Rampa",
            TimberBuildPreviewKind.GinPoleHoist => "Gin Pole Vinç",
            TimberBuildPreviewKind.SawhorseBench => "Bıçkı Sehpası",
            TimberBuildPreviewKind.StoneFireplace => "Taş Şömine & Baca",
            _ => "Kütük"
        };

        private static void EmitBox(List<FacetedVoxelTriangle> dst, Vector3 min, Vector3 max, byte matId)
        {
            EmitQuad(dst, new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, min.y, min.z), Vector3.back, matId);
            EmitQuad(dst, new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, min.y, max.z), Vector3.forward, matId);
            EmitQuad(dst, new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z), Vector3.left, matId);
            EmitQuad(dst, new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), Vector3.right, matId);
            EmitQuad(dst, new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), Vector3.up, matId);
            EmitQuad(dst, new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), Vector3.down, matId);
        }

        private static void EmitQuad(List<FacetedVoxelTriangle> dst, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, byte matId)
        {
            var va = new SysVec3(a.x, a.y, a.z);
            var vb = new SysVec3(b.x, b.y, b.z);
            var vc = new SysVec3(c.x, c.y, c.z);
            var vd = new SysVec3(d.x, d.y, d.z);
            var vn = new SysVec3(n.x, n.y, n.z);
            dst.Add(new FacetedVoxelTriangle { V0 = va, V1 = vb, V2 = vc, Normal = vn, MaterialId = matId, FacetShade = 215, AmbientOcclusion = 255 });
            dst.Add(new FacetedVoxelTriangle { V0 = va, V1 = vc, V2 = vd, Normal = vn, MaterialId = matId, FacetShade = 215, AmbientOcclusion = 255 });
        }
    }
}
#endif
