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
using SysQuat = System.Numerics.Quaternion;

namespace Stratocraft.Character
{
    public enum BlueprintDrawTool : byte
    {
        WallLine = 0,         // The Forest tarzi: 1. Tik Baslangic -> Surukle -> 2. Tik Kilit & Zincirle! ([Ctrl] 90° Dik)
        RectangleRoom = 1,    // 1. Tik Kose A -> Surukle -> 2. Tik Kose B (Tam dikdortgen oda + 4 duvar + kiris + doseme)
        FloorDeckArea = 2,    // 1. Tik Kose A -> Surukle -> 2. Tik Kose B (Kat doseme tabliyesi)
        GableRoofArea = 3,    // 1. Tik Kose A -> Surukle -> 2. Tik Kose B (Besik cati + tepe mahya kirisi)
        DoorOrWindowCut = 4,  // Herhangi bir kattaki tek bir duvar bolumune Kapi (2.0m) veya Pencere (1.0m) boslugu ac!
        SingleSupportPost = 5 // Kirmizi/zayif bir kirisin altina tekli destek diregi ekle
    }

    /// <summary>
    /// [N] The Forest Tarzi Tıkla-Uzat 3B Çok Katlı Ev Planı & Canlı Yapısal Dayanıklılık Simülatörü.
    /// - [H] (veya PageUp/PageDown): 1. Kat (+0.0m) -> 2. Kat (+2.6m) -> 3. Kat (+5.2m) kat değiştirme!
    /// - [Ctrl] Basılı: 90° Tam Düz & Karşı Köşe Hizalı Dikdörtgen (bırakınca tamamen serbest açı!).
    /// - [V] Araç Değiştir: Duvar Hattı, Dikdörtgen Oda, Kat Döşemesi, Beşik Çatı, Kapı/Pencere Aç, Tekli Direk.
    /// - [J] Araç Seçeneği: Otomatik Ara Direk (Açık/Kapalı) veya Kapı/Pencere seçimi.
    /// </summary>
    public sealed class Stage1TimberBlueprintPlannerController
    {
        private struct PreviewNodeItem
        {
            public BlueprintNodeSpec Spec;
            public int WallBayId;
        }

        private sealed class PlacedBlueprintNode
        {
            public BlueprintNodeSpec Spec;
            public BlueprintNodeEvaluation Eval;
            public int WallBayId;
            public GameObject? RootGo;
            public Material? InstanceMat;
        }

        private sealed class OpeningMarkerVisual
        {
            public int WallBayId;
            public bool IsWindow;
            public Vector3 BayBase;
            public Quaternion BayRot;
            public float BaySpan;
            public GameObject? RootGo;
            public Material? Mat;
        }

        private sealed class BlueprintUndoSnapshot
        {
            public List<(BlueprintNodeSpec Spec, int WallBayId)> Nodes = new();
            public List<(int WallBayId, bool IsWindow, Vector3 BayBase, Quaternion BayRot, float BaySpan)> Openings = new();
            public bool HasRefAxis;
            public float RefAxisYawDeg;
        }

        private const float StoryHeightM = 2.60f;
        private const float GroundPostHeightM = 3.25f; // 0.65m gomulu + 2.60m 1. kat yuksekligi!
        private const float DefaultPostEmbedM = 0.65f;
        private const float MaxAutoBaySpanM = 3.20f;

        private readonly List<PlacedBlueprintNode> _nodes = new(160);
        private readonly List<OpeningMarkerVisual> _openingMarkers = new(16);
        private readonly List<BlueprintUndoSnapshot> _undoStack = new(24);
        private readonly List<PreviewNodeItem> _previewItems = new(64);
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(512);

        private uint _nextNodeId = 1;
        private int _nextWallBayId = 1;

        private BlueprintDrawTool _tool = BlueprintDrawTool.WallLine;
        private int _activeStoryIndex;              // [H]: 0 = 1. Kat (+0.0m), 1 = 2. Kat (+2.6m), 2 = 3. Kat (+5.2m)
        private bool _autoIntermediatePosts = true; // [J]: 3.2m ustu duvarlara otomatik ara direk koy
        private bool _cutoutIsWindow;               // [J]: DoorOrWindowCut icin false=Kapi, true=Pencere
        private float _refAxisYawDeg;               // Ilk duvar ekseni ([Ctrl] 90° dikdortgen kilidi)
        private bool _hasRefAxis;
        private float _manualYawDeg;

        private bool _hasAnchor;
        private Vector3 _anchorGroundPos;
        private float _lastDragLengthM;
        private float _lastDragWidthM;

        private int _lastSyncedRealPieceCount = -1;
        private float _nextSyncTime;

        private GameObject? _previewGo;
        private Material? _previewMat;

        public bool IsDrawingModeActive { get; private set; }
        public bool ConsumedClickThisFrame { get; private set; }
        public bool ConsumedKeyQThisFrame { get; private set; }
        public bool ConsumedKeyKThisFrame { get; private set; }
        public bool IsConsumingKeyR => IsDrawingModeActive;
        public int NodeCount => _nodes.Count;
        public int OpeningMarkerCount => _openingMarkers.Count;
        public int ActiveStoryIndex => _activeStoryIndex;
        public BlueprintDrawTool ActiveTool => _tool;
        public bool AutoIntermediatePosts => _autoIntermediatePosts;
        public bool HasActiveAnchor => _hasAnchor;
        public BlueprintPlanSummary LastSummary { get; private set; }
        public string LastHudHint { get; private set; } = string.Empty;

        public void Dispose()
        {
            ClearAllNodes();
            if (_previewGo != null)
            {
                UnityEngine.Object.Destroy(_previewGo);
                _previewGo = null;
            }
            if (_previewMat != null)
            {
                UnityEngine.Object.Destroy(_previewMat);
                _previewMat = null;
            }
            IsDrawingModeActive = false;
            _hasAnchor = false;
        }

        public void ClearAllNodes(bool keepUndoHistory = false)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].RootGo != null) UnityEngine.Object.Destroy(_nodes[i].RootGo);
                if (_nodes[i].InstanceMat != null) UnityEngine.Object.Destroy(_nodes[i].InstanceMat);
            }
            _nodes.Clear();
            for (int i = 0; i < _openingMarkers.Count; i++)
            {
                if (_openingMarkers[i].RootGo != null) UnityEngine.Object.Destroy(_openingMarkers[i].RootGo);
                if (_openingMarkers[i].Mat != null) UnityEngine.Object.Destroy(_openingMarkers[i].Mat);
            }
            _openingMarkers.Clear();
            LastSummary = default;
            _hasAnchor = false;
            _hasRefAxis = false;
            _activeStoryIndex = 0;
            _lastSyncedRealPieceCount = -1;
            if (!keepUndoHistory) _undoStack.Clear();
        }

        private void PushUndoSnapshot()
        {
            var snap = new BlueprintUndoSnapshot
            {
                HasRefAxis = _hasRefAxis,
                RefAxisYawDeg = _refAxisYawDeg
            };
            for (int i = 0; i < _nodes.Count; i++)
            {
                snap.Nodes.Add((_nodes[i].Spec, _nodes[i].WallBayId));
            }
            for (int i = 0; i < _openingMarkers.Count; i++)
            {
                var om = _openingMarkers[i];
                snap.Openings.Add((om.WallBayId, om.IsWindow, om.BayBase, om.BayRot, om.BaySpan));
            }
            _undoStack.Add(snap);
            if (_undoStack.Count > 24) _undoStack.RemoveAt(0);
        }

        public bool TryUndoLastAction(out string feedback)
        {
            feedback = string.Empty;
            if (_undoStack.Count == 0) return false;

            BlueprintUndoSnapshot snap = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            int keepStory = _activeStoryIndex;
            ClearAllNodes(keepUndoHistory: true);
            _activeStoryIndex = keepStory;
            _hasRefAxis = snap.HasRefAxis;
            _refAxisYawDeg = snap.RefAxisYawDeg;

            for (int i = 0; i < snap.Nodes.Count; i++)
            {
                var item = snap.Nodes[i];
                var node = new PlacedBlueprintNode { Spec = item.Spec, WallBayId = item.WallBayId };
                BuildPlacedNodeVisual(node);
                _nodes.Add(node);
            }
            for (int i = 0; i < snap.Openings.Count; i++)
            {
                var o = snap.Openings[i];
                SpawnPermanentOpeningMarker(o.WallBayId, o.BayBase, o.BayRot, o.BaySpan, o.IsWindow);
            }
            ReanalyzeAllPlacedNodes();
            feedback = $"↩️ Son plan adımı GERİ ALINDI! ({_nodes.Count} parça — {(LastSummary.WillStand ? $"✅ AYAKTA KALIR %{LastSummary.OverallStabilityPct}" : "🔴 ÇÖKME RİSKLİ")})";
            return true;
        }

        public void Step(
            bool menuOpen,
            UnityEngine.Camera? cam,
            Transform playerTr,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1RigidTimberBridge rigidTimberBridge,
            Stage1HolsterAndBackpackController holsterCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            ConsumedClickThisFrame = false;
            ConsumedKeyQThisFrame = false;
            ConsumedKeyKThisFrame = false;
            LastHudHint = string.Empty;

            // 1) [N]: 3B Ev Plani Cizim Modunu Ac / Kapat
            if (!menuOpen && !miningCtrl.IsCrystalActive && Input.GetKeyDown(KeyCode.N))
            {
                IsDrawingModeActive = !IsDrawingModeActive;
                _hasAnchor = false;
                if (IsDrawingModeActive)
                {
                    miningCtrl.LastFeedbackText =
                        "📐 3B EV PLANI ([N]): [Sol Tık] Başlat/Uzat | [Ctrl] 90° Dik | [H] Kat Değiştir (1/2/3) | [V] Araç (Duvar/Oda/Çatı/Kapı)";
                }
                else
                {
                    if (_previewGo != null) _previewGo.SetActive(false);
                    miningCtrl.LastFeedbackText = _nodes.Count > 0
                        ? $"📐 Ev planı hazır ({_nodes.Count} parça — {(LastSummary.WillStand ? $"✅ AYAKTA KALIR %{LastSummary.OverallStabilityPct}" : "🔴 ÇÖKME RİSKİ VAR")})!"
                        : "📐 Ev planı çizim modu kapatıldı.";
                }
            }

            // 2) Gercek insaat parcalariyla otomatik ilerleme senkronizasyonu
            if (_nodes.Count > 0 && (rigidTimberBridge.ActivePieceCount != _lastSyncedRealPieceCount || Time.unscaledTime >= _nextSyncTime))
            {
                _nextSyncTime = Time.unscaledTime + 0.35f;
                _lastSyncedRealPieceCount = rigidTimberBridge.ActivePieceCount;
                SyncBuiltNodesFromWorld(rigidTimberBridge);
            }

            if (menuOpen || miningCtrl.IsCrystalActive)
            {
                if (_previewGo != null) _previewGo.SetActive(false);
                return;
            }

            if (!IsDrawingModeActive)
            {
                if (_previewGo != null) _previewGo.SetActive(false);
                if (_nodes.Count > 0)
                {
                    VisualInventorySlot active = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
                    if (!active.IsEmpty && active.Kind == InventoryItemKind.ToolShovel &&
                        TryFindNearestUnbuiltPost(playerTr.position, 4.5f, out _, out float distM))
                    {
                        LastHudHint = $"⛏️ PLANLI DİREK ÇUKURU ({distM:F1}m yakında) — [Sol Tık] ile mavi çemberin içini kaz!";
                    }
                }
                return;
            }

            // 3) [Q] veya [ESC]: Cizim modundan cik
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Escape))
            {
                if (Input.GetKeyDown(KeyCode.Q)) ConsumedKeyQThisFrame = true;
                _hasAnchor = false;
                IsDrawingModeActive = false;
                if (_previewGo != null) _previewGo.SetActive(false);
                miningCtrl.LastFeedbackText = "📐 Plan çiziminden çıkıldı — çizili ev planı dünyada rehber olarak kalır.";
                return;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // [Sag Tik]: Aktif surukleme zincirini bitir
            if (_hasAnchor && Input.GetMouseButtonDown(1))
            {
                ConsumedClickThisFrame = true;
                _hasAnchor = false;
                miningCtrl.LastFeedbackText = "✏️ Çizim zinciri tamamlandı — [H] ile 2. kata çıkabilir veya [V] ile araç değiştirebilirsin.";
                return;
            }

            // [H] veya [PageUp]/[PageDown]: 1. Kat / 2. Kat / 3. Kat secimi (TUM ARACLARDA CALISIR!)
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.PageDown))
            {
                if (Input.GetKeyDown(KeyCode.PageDown) || (Input.GetKeyDown(KeyCode.H) && shiftHeld))
                    _activeStoryIndex = (_activeStoryIndex + 2) % 3;
                else
                    _activeStoryIndex = (_activeStoryIndex + 1) % 3;
                _hasAnchor = false;
                miningCtrl.LastFeedbackText = $"🏗️ ÇİZİM KATI DEĞİŞTİ [H]: {_activeStoryIndex + 1}. KAT (+{_activeStoryIndex * StoryHeightM:F1}m yükseklik)";
            }

            // [V]: Cizim Araci Degistir
            if (Input.GetKeyDown(KeyCode.V))
            {
                int count = 6;
                int next = shiftHeld ? (((int)_tool + count - 1) % count) : (((int)_tool + 1) % count);
                _tool = (BlueprintDrawTool)next;
                _hasAnchor = false;
                miningCtrl.LastFeedbackText = $"📐 Plan Aracı [V]: {GetToolLabel(_tool)} (Aktif Kat: {_activeStoryIndex + 1}. Kat)";
            }

            // [R]: Ana ekseni 15° (Shift+R: 5°) dondur
            if (Input.GetKeyDown(KeyCode.R))
            {
                float step = shiftHeld ? 5f : 15f;
                _manualYawDeg = (_manualYawDeg + step) % 360f;
                if (!_hasRefAxis) _refAxisYawDeg = _manualYawDeg;
            }

            // [J]: Araca ozel secenek
            if (Input.GetKeyDown(KeyCode.J))
            {
                if (_tool is BlueprintDrawTool.WallLine or BlueprintDrawTool.RectangleRoom)
                {
                    _autoIntermediatePosts = !_autoIntermediatePosts;
                    miningCtrl.LastFeedbackText = _autoIntermediatePosts
                        ? "🪵 Otomatik Ara Destek Direği [J]: AÇIK (3.2m üstü duvarlara otomatik destek direği koyar)"
                        : "⚠️ Otomatik Ara Destek Direği [J]: KAPALI (Uzun desteksiz açıklıklarda kırmızı çökme testi!)";
                }
                else if (_tool == BlueprintDrawTool.DoorOrWindowCut)
                {
                    _cutoutIsWindow = !_cutoutIsWindow;
                    miningCtrl.LastFeedbackText = _cutoutIsWindow
                        ? "🪟 Açıklık Türü [J]: PENCERE BOŞLUĞU (1.0m)"
                        : "🚪 Açıklık Türü [J]: KAPI BOŞLUĞU (2.0m)";
                }
            }

            // [Backspace] veya [Ctrl+Z]: Son plan islemini (ekleme / silme / kapi-pencere) aninda GERI AL!
            if (Input.GetKeyDown(KeyCode.Backspace) || (ctrlHeld && Input.GetKeyDown(KeyCode.Z)))
            {
                _hasAnchor = false;
                if (TryUndoLastAction(out string undoMsg))
                {
                    miningCtrl.LastFeedbackText = undoMsg;
                }
                else
                {
                    miningCtrl.LastFeedbackText = "ℹ️ Geri alınacak bir plan adımı yok.";
                }
                return;
            }

            // [K]: Baktigin plan bolumunu sil ([Shift+K]: Tum plani temizle)
            if (Input.GetKeyDown(KeyCode.K))
            {
                ConsumedKeyKThisFrame = true;
                if (shiftHeld)
                {
                    if (_nodes.Count > 0) PushUndoSnapshot();
                    ClearAllNodes(keepUndoHistory: true);
                    miningCtrl.LastFeedbackText = "🗑️ Tüm ev planı temizlendi ([Backspace] ile geri alabilirsin).";
                    return;
                }
                if (cam != null && TryRemoveAimedBlueprintNode(cam.transform.position, cam.transform.forward, 14.0f, out string delMsg))
                {
                    miningCtrl.LastFeedbackText = delMsg;
                    return;
                }
            }

            if (cam == null) return;

            // 4) KAPI / PENCERE ACIKLIGI ARACI (Canli 3B Altin/Camgobegi Cerceve Onizlemeli!)
            if (_tool == BlueprintDrawTool.DoorOrWindowCut)
            {
                int aimedBay = FindAimedWallBayId(cam.transform.position, cam.transform.forward, 12.0f, out Vector3 bayCenter, out Quaternion bayRot, out float baySpan);
                string cutLbl = _cutoutIsWindow ? "🪟 PENCERE BOŞLUĞU" : "🚪 KAPI BOŞLUĞU";

                if (aimedBay > 0)
                {
                    RenderOpeningHighlightPreview(bayCenter, bayRot, baySpan, _cutoutIsWindow);
                    LastHudHint = $"📐 [{cutLbl}] Duvar Bölümü #{aimedBay} Seçildi — [Sol Tık] Boşluk Aç | [J] Kapı/Pencere Değiştir | [V] Araç";
                    if (Input.GetMouseButtonDown(0))
                    {
                        ConsumedClickThisFrame = true;
                        if (TryCarveOpeningInWallBay(aimedBay, _cutoutIsWindow, out string cutMsg))
                        {
                            miningCtrl.LastFeedbackText = cutMsg;
                        }
                    }
                }
                else
                {
                    if (_previewGo != null) _previewGo.SetActive(false);
                    LastHudHint = $"📐 [{cutLbl}] Çizili bir duvar bölümüne bak (3B çerçeve belirir) ve [Sol Tık] yap | [J] Kapı/Pencere | [V] Araç";
                }
                return;
            }

            // 5) Zemin hedef noktasini bul (14m menzil)
            Vector3 rawAimPt = ResolveAimGroundPoint(cam, playerTr, miningCtrl, worldChunks);
            float storyOffsetY = _activeStoryIndex * StoryHeightM;

            // 6) Tekli Destek Diregi Araci (Duvar ucuna, kiris ortasina veya alt kat diregine MIKNATISLI KENETLENIR!)
            if (_tool == BlueprintDrawTool.SingleSupportPost)
            {
                _previewItems.Clear();
                bool snappedPost = TrySnapToAnyBlueprintAnchor(rawAimPt, 1.25f, out Vector3 postPt, out string postSnapTag, out _);
                Vector3 finalPostPt = snappedPost ? postPt : rawAimPt;
                finalPostPt.y = SampleGroundY(finalPostPt.x, finalPostPt.z, finalPostPt.y, worldChunks);

                _previewItems.Add(new PreviewNodeItem
                {
                    Spec = MakePostSpec(finalPostPt, _activeStoryIndex),
                    WallBayId = 0
                });
                BlueprintStabilityTier pTier = EvaluatePreviewBatch(_previewItems, out BlueprintFailureReason pReason, out int pPct);
                RenderPreviewBatch(_previewItems, pTier);
                string pSnapBadge = snappedPost ? $" [{postSnapTag}]" : "";
                LastHudHint = $"📐 [TEKLİ DİREK — {_activeStoryIndex + 1}. KAT]{pSnapBadge} {FormatTierShort(pTier, pPct, pReason)} | [Sol Tık] Koy | [Backspace] Geri Al";

                if (Input.GetMouseButtonDown(0))
                {
                    ConsumedClickThisFrame = true;
                    PushUndoSnapshot();
                    CommitPreviewBatch(_previewItems);
                    miningCtrl.LastFeedbackText = FormatCommitFeedback($"{_activeStoryIndex + 1}. Kat Destek Direği");
                }
                return;
            }

            // 7) Tıkla-Uzat (Point-to-Point) Araclari: Duvar Hatti, Dikdortgen Oda, Kat Dosemesi, Besik Cati
            _previewItems.Clear();

            if (!_hasAnchor)
            {
                bool snappedCorner = TrySnapToAnyBlueprintAnchor(rawAimPt, 1.25f, out Vector3 snappedA, out string snapTag, out float snappedAxisYaw);
                Vector3 startPt = snappedCorner ? snappedA : rawAimPt;
                startPt.y = SampleGroundY(startPt.x, startPt.z, startPt.y, worldChunks);

                _previewItems.Add(new PreviewNodeItem
                {
                    Spec = MakePostSpec(startPt, _activeStoryIndex),
                    WallBayId = 0
                });
                RenderPreviewBatch(_previewItems, _activeStoryIndex == 0 ? BlueprintStabilityTier.GroundedBlue : BlueprintStabilityTier.SafeGreen);

                string snapTxt = snappedCorner ? $" [{snapTag}]" : "";
                LastHudHint = $"📐 [{GetToolLabel(_tool)} — {_activeStoryIndex + 1}. KAT (+{storyOffsetY:F1}m)]{snapTxt} [Sol Tık] Başlat | [H] Kat | [Ctrl] 90° Dik | [Backspace] Geri Al";

                if (Input.GetMouseButtonDown(0))
                {
                    ConsumedClickThisFrame = true;
                    _anchorGroundPos = startPt;
                    _hasAnchor = true;
                    if (snappedCorner && !float.IsNaN(snappedAxisYaw) && !_hasRefAxis)
                    {
                        _refAxisYawDeg = snappedAxisYaw;
                        _hasRefAxis = true;
                    }
                    miningCtrl.LastFeedbackText = $"📍 {_activeStoryIndex + 1}. Kat başlangıç köşesi konuldu! Sürükleyip 2. köşeye [Sol Tık] yap ([Ctrl] = 90° Tam Düz | [Sağ Tık] = Bitir).";
                }
                return;
            }

            Vector3 endPt = ComputeSnappedEndPoint(_anchorGroundPos, rawAimPt, ctrlHeld, out bool closedLoopCorner, out bool alignedWithOtherCorner);
            endPt.y = SampleGroundY(endPt.x, endPt.z, _anchorGroundPos.y, worldChunks);

            BuildDragPreviewItems(_tool, _anchorGroundPos, endPt, _activeStoryIndex, worldChunks, _previewItems);
            BlueprintStabilityTier worstTier = EvaluatePreviewBatch(_previewItems, out BlueprintFailureReason failReason, out int estPct);
            RenderPreviewBatch(_previewItems, worstTier);

            string orthoBadge = ctrlHeld
                ? (alignedWithOtherCorner ? " [📐 90° DİK + KÖŞE HİZALI!]" : " [📐 90° TAM DÜZ]")
                : " [Serbest — Düz için Ctrl]";
            string sizeBadge = _tool == BlueprintDrawTool.WallLine
                ? $"{_lastDragLengthM:F2}m"
                : $"{_lastDragLengthM:F2}m × {_lastDragWidthM:F2}m";
            string loopBadge = closedLoopCorner ? " [🔗 MEVCUT KÖŞE/DUVARA KİLİTLİ]" : "";

            LastHudHint = $"📐 [{_activeStoryIndex + 1}. KAT {GetToolLabel(_tool)}: {sizeBadge}]{orthoBadge}{loopBadge} {FormatTierShort(worstTier, estPct, failReason)} | [Sol Tık] Onayla | [Sağ Tık] Bitir";

            if (Input.GetMouseButtonDown(0))
            {
                ConsumedClickThisFrame = true;
                if (_previewItems.Count > 0 && Vector2.Distance(new Vector2(_anchorGroundPos.x, _anchorGroundPos.z), new Vector2(endPt.x, endPt.z)) >= 0.85f)
                {
                    if (!_hasRefAxis)
                    {
                        if (_tool == BlueprintDrawTool.WallLine)
                        {
                            Vector2 d = new Vector2(endPt.x - _anchorGroundPos.x, endPt.z - _anchorGroundPos.z);
                            if (d.sqrMagnitude > 0.25f)
                            {
                                _refAxisYawDeg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                                _hasRefAxis = true;
                            }
                        }
                        else if (_tool == BlueprintDrawTool.RectangleRoom)
                        {
                            _refAxisYawDeg = _manualYawDeg;
                            _hasRefAxis = true;
                        }
                    }

                    PushUndoSnapshot();
                    CommitPreviewBatch(_previewItems);

                    if (_tool == BlueprintDrawTool.WallLine && !closedLoopCorner)
                    {
                        _anchorGroundPos = endPt;
                        _hasAnchor = true;
                        miningCtrl.LastFeedbackText = FormatCommitFeedback($"{_activeStoryIndex + 1}. Kat Duvarı ({_lastDragLengthM:F1}m)") + " — Devam et veya [Sağ Tık] bitir!";
                    }
                    else
                    {
                        _hasAnchor = false;
                        string doneName = closedLoopCorner ? $"{_activeStoryIndex + 1}. Kat Kapalı Oda" : $"{_activeStoryIndex + 1}. Kat {GetToolLabel(_tool)}";
                        miningCtrl.LastFeedbackText = FormatCommitFeedback(doneName);
                    }
                }
            }
        }

        // --- Test ve dis cagrilar icin temiz yardimcilar ---
        public BlueprintPlanSummary DrawWallSegmentBetween(
            Vector3 pointA, Vector3 pointB, bool ctrlSnap90, bool autoIntermediatePosts,
            Dictionary<(int cx, int cz), uint[]>? worldChunks, int storyIndex = 0)
        {
            _autoIntermediatePosts = autoIntermediatePosts;
            _activeStoryIndex = Mathf.Clamp(storyIndex, 0, 2);
            Vector3 a = new Vector3(pointA.x, SampleGroundY(pointA.x, pointA.z, pointA.y, worldChunks), pointA.z);
            Vector3 b = ComputeSnappedEndPoint(a, pointB, ctrlSnap90, out _, out _);
            b.y = SampleGroundY(b.x, b.z, a.y, worldChunks);
            _previewItems.Clear();
            BuildDragPreviewItems(BlueprintDrawTool.WallLine, a, b, _activeStoryIndex, worldChunks, _previewItems);
            CommitPreviewBatch(_previewItems);
            return LastSummary;
        }

        public BlueprintPlanSummary DrawRectangleRoomBetween(
            Vector3 cornerA, Vector3 cornerB, bool ctrlSnap90, bool autoIntermediatePosts,
            Dictionary<(int cx, int cz), uint[]>? worldChunks, int storyIndex = 0)
        {
            _autoIntermediatePosts = autoIntermediatePosts;
            _activeStoryIndex = Mathf.Clamp(storyIndex, 0, 2);
            Vector3 a = new Vector3(cornerA.x, SampleGroundY(cornerA.x, cornerA.z, cornerA.y, worldChunks), cornerA.z);
            Vector3 b = new Vector3(cornerB.x, SampleGroundY(cornerB.x, cornerB.z, a.y, worldChunks), cornerB.z);
            _previewItems.Clear();
            BuildDragPreviewItems(BlueprintDrawTool.RectangleRoom, a, b, _activeStoryIndex, worldChunks, _previewItems);
            CommitPreviewBatch(_previewItems);
            return LastSummary;
        }

        public BlueprintPlanSummary DrawGableRoofBetween(
            Vector3 cornerA, Vector3 cornerB, int storyIndex,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            _activeStoryIndex = Mathf.Clamp(storyIndex, 0, 2);
            Vector3 a = new Vector3(cornerA.x, SampleGroundY(cornerA.x, cornerA.z, cornerA.y, worldChunks), cornerA.z);
            Vector3 b = new Vector3(cornerB.x, SampleGroundY(cornerB.x, cornerB.z, a.y, worldChunks), cornerB.z);
            _previewItems.Clear();
            BuildDragPreviewItems(BlueprintDrawTool.GableRoofArea, a, b, _activeStoryIndex, worldChunks, _previewItems);
            CommitPreviewBatch(_previewItems);
            return LastSummary;
        }

        public bool TryCarveOpeningInWallBay(int wallBayId, bool isWindow, out string feedback)
        {
            feedback = string.Empty;
            var bayLogs = new List<int>(8);
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].WallBayId == wallBayId && _nodes[i].Spec.Kind == BlueprintPieceKind.WallLog)
                    bayLogs.Add(i);
            }
            if (bayLogs.Count == 0) return false;

            bayLogs.Sort((a, b) => _nodes[a].Spec.Position.Y.CompareTo(_nodes[b].Spec.Position.Y));
            Vector3 bayCenter = ToUnity(_nodes[bayLogs[0]].Spec.Position);
            Quaternion bayRot = ToUnity(_nodes[bayLogs[0]].Spec.Rotation);
            float baySpan = _nodes[bayLogs[0]].Spec.LengthM;
            float baseLogY = bayCenter.y - 0.32f;

            var toRemove = new List<int>(4);
            for (int k = 0; k < bayLogs.Count; k++)
            {
                bool cutThis = isWindow
                    ? (k == 1 || k == 2) // Pencere: alt sira (0) ve ust lento (3) kalir, orta 2 sira acilir!
                    : (k <= 2);          // Kapi: alt 3 sira acilir, ust lento (3) kalir!
                if (cutThis) toRemove.Add(bayLogs[k]);
            }
            toRemove.Sort((a, b) => b.CompareTo(a));
            if (toRemove.Count > 0)
            {
                PushUndoSnapshot();
                for (int r = 0; r < toRemove.Count; r++)
                {
                    int idx = toRemove[r];
                    if (_nodes[idx].RootGo != null) UnityEngine.Object.Destroy(_nodes[idx].RootGo);
                    if (_nodes[idx].InstanceMat != null) UnityEngine.Object.Destroy(_nodes[idx].InstanceMat);
                    _nodes.RemoveAt(idx);
                }
                SpawnPermanentOpeningMarker(wallBayId, new Vector3(bayCenter.x, baseLogY, bayCenter.z), bayRot, baySpan, isWindow);
                ReanalyzeAllPlacedNodes();
                feedback = isWindow
                    ? $"🪟 Duvar Bölümü #{wallBayId} içine 3B PENCERE BOŞLUĞU açıldı!"
                    : $"🚪 Duvar Bölümü #{wallBayId} içine 3B KAPI BOŞLUĞU açıldı!";
                return true;
            }
            return false;
        }

        public bool TryFindNearestUnbuiltPost(Vector3 worldPos, float maxDist, out Vector3 postBasePos, out float distM)
        {
            postBasePos = Vector3.zero;
            distM = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var nd = _nodes[i].Spec;
                if (nd.Kind != BlueprintPieceKind.Post || !nd.IsGrounded || nd.IsBuilt) continue;
                Vector3 b = new Vector3(nd.Position.X, nd.Position.Y - nd.LengthM * 0.5f + nd.PlannedEmbedDepthM, nd.Position.Z);
                float d = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(b.x, b.z));
                if (d <= maxDist && d < distM)
                {
                    distM = d;
                    postBasePos = b;
                }
            }
            return distM <= maxDist;
        }

        public void DrawBlueprintStabilityHud()
        {
            if (_nodes.Count == 0 && !IsDrawingModeActive) return;

            float w = 455f;
            float h = IsDrawingModeActive ? 190f : 114f;
            float x = Screen.width - w - 16f;
            float y = 156f;

            GUI.Box(new Rect(x, y, w, h), string.Empty);
            var sum = LastSummary;

            string statusLine;
            if (_nodes.Count == 0)
                statusLine = "📐 EV PLANI (BOŞ) — [Sol Tık] ile ilk köşeyi koy ve uzat!";
            else if (sum.WillStand)
                statusLine = sum.WarningCount > 0
                    ? $"🟡 PLAN AYAKTA KALIR (%{sum.OverallStabilityPct}) — {sum.WarningCount} Uyarı"
                    : $"✅ PLAN SAĞLAM & AYAKTA KALIR (%{sum.OverallStabilityPct})";
            else
                statusLine = $"🔴 PLAN ÇÖKER! ({sum.CollapseCount} Kritik Parça | %{sum.OverallStabilityPct})";

            GUI.Label(new Rect(x + 10f, y + 8f, w - 20f, 22f), statusLine);

            if (_nodes.Count > 0)
            {
                string diag = TimberBlueprintStructuralAnalyzer.GetReasonTextTurkish(sum.PrimaryFailure);
                GUI.Label(new Rect(x + 10f, y + 30f, w - 20f, 22f), $"🔍 {diag}");

                int posts = 0, walls = 0, beams = 0, decks = 0, roofs = 0;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    switch (_nodes[i].Spec.Kind)
                    {
                        case BlueprintPieceKind.Post: posts++; break;
                        case BlueprintPieceKind.WallLog: walls++; break;
                        case BlueprintPieceKind.FloorBeam:
                        case BlueprintPieceKind.RidgeOrTieBeam: beams++; break;
                        case BlueprintPieceKind.FloorDeck: decks++; break;
                        case BlueprintPieceKind.RoofSlope: roofs++; break;
                    }
                }
                GUI.Label(new Rect(x + 10f, y + 54f, w - 20f, 22f),
                    $"📊 İnşa: {sum.BuiltPieces}/{sum.TotalPieces} | Direk:{posts} Duvar:{walls} Kiriş:{beams} Döş:{decks} Çatı:{roofs}");
                GUI.Label(new Rect(x + 10f, y + 76f, w - 20f, 22f),
                    "🔵Kök  🟢Güvenli  🟡Sınırda  🔴Çöker  |  [N] Plan Modu");
            }

            if (IsDrawingModeActive)
            {
                string jOpt = _tool is BlueprintDrawTool.WallLine or BlueprintDrawTool.RectangleRoom
                    ? (_autoIntermediatePosts ? "Ara Direk: AÇIK" : "Ara Direk: KAPALI")
                    : (_tool == BlueprintDrawTool.DoorOrWindowCut
                        ? (_cutoutIsWindow ? "🪟 Pencere" : "🚪 Kapı")
                        : "Standart");
                GUI.Label(new Rect(x + 10f, y + 102f, w - 20f, 20f),
                    $"🏗️ AKTİF KAT [H / PgUp-Dn]: {_activeStoryIndex + 1}. KAT (+{_activeStoryIndex * StoryHeightM:F1}m)");
                GUI.Label(new Rect(x + 10f, y + 122f, w - 20f, 20f),
                    $"✏️ Araç [V]: {GetToolLabel(_tool)}  |  [J] {jOpt}");
                GUI.Label(new Rect(x + 10f, y + 142f, w - 20f, 20f),
                    "📐 [Ctrl Basılı]: 90° Tam Düz & Dikdörtgen Hizalama (Bırakınca Serbest)");
                GUI.Label(new Rect(x + 10f, y + 162f, w - 20f, 20f),
                    "[Sol Tık] Onayla | [Sağ Tık] Bitir | [K] Sil | [Backspace] Geri Al");
            }
        }

        private Vector3 ComputeSnappedEndPoint(
            Vector3 anchor, Vector3 rawAim, bool ctrlHeld,
            out bool snappedToExistingCorner, out bool alignedWithOtherCorner)
        {
            snappedToExistingCorner = false;
            alignedWithOtherCorner = false;

            if (TrySnapToAnyBlueprintAnchor(rawAim, 1.05f, out Vector3 existingCorner, out _, out _) &&
                Vector2.Distance(new Vector2(anchor.x, anchor.z), new Vector2(existingCorner.x, existingCorner.z)) >= 1.0f)
            {
                snappedToExistingCorner = true;
                return existingCorner;
            }

            Vector2 a2 = new Vector2(anchor.x, anchor.z);
            Vector2 b2 = new Vector2(rawAim.x, rawAim.z);
            Vector2 delta = b2 - a2;
            if (delta.magnitude < 0.25f) return rawAim;

            if (!ctrlHeld) return rawAim;

            float refRad = (_hasRefAxis ? _refAxisYawDeg : _manualYawDeg) * Mathf.Deg2Rad;
            Vector2 axisF = new Vector2(Mathf.Sin(refRad), Mathf.Cos(refRad));
            Vector2 axisR = new Vector2(axisF.y, -axisF.x);

            float projF = Vector2.Dot(delta, axisF);
            float projR = Vector2.Dot(delta, axisR);

            if (_tool == BlueprintDrawTool.WallLine)
            {
                Vector2 lockDir = Mathf.Abs(projF) >= Mathf.Abs(projR)
                    ? (projF >= 0f ? axisF : -axisF)
                    : (projR >= 0f ? axisR : -axisR);
                float lockLen = Mathf.Max(1.0f, Mathf.Round(Mathf.Max(Mathf.Abs(projF), Mathf.Abs(projR)) * 5f) / 5f);

                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].Spec.Kind != BlueprintPieceKind.Post) continue;
                    Vector2 p2 = new Vector2(_nodes[i].Spec.Position.X, _nodes[i].Spec.Position.Z);
                    if (Vector2.Distance(p2, a2) < 0.5f) continue;
                    float pProj = Vector2.Dot(p2 - a2, lockDir);
                    if (pProj >= 1.2f && Mathf.Abs(pProj - lockLen) <= 0.75f)
                    {
                        lockLen = pProj;
                        alignedWithOtherCorner = true;
                        break;
                    }
                }
                Vector2 res2 = a2 + lockDir * lockLen;
                return new Vector3(res2.x, rawAim.y, res2.y);
            }
            else
            {
                float snappedF = Mathf.Sign(projF == 0f ? 1f : projF) * Mathf.Max(1.6f, Mathf.Round(Mathf.Abs(projF) * 5f) / 5f);
                float snappedR = Mathf.Sign(projR == 0f ? 1f : projR) * Mathf.Max(1.6f, Mathf.Round(Mathf.Abs(projR) * 5f) / 5f);
                Vector2 res2 = a2 + axisF * snappedF + axisR * snappedR;
                return new Vector3(res2.x, rawAim.y, res2.y);
            }
        }

        private void BuildDragPreviewItems(
            BlueprintDrawTool tool, Vector3 a, Vector3 b, int storyIndex,
            Dictionary<(int cx, int cz), uint[]>? worldChunks, List<PreviewNodeItem> dst)
        {
            dst.Clear();
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            float totalDist = Vector2.Distance(a2, b2);
            if (totalDist < 0.8f) return;

            int tempBayId = _nextWallBayId;

            if (tool == BlueprintDrawTool.WallLine)
            {
                _lastDragLengthM = totalDist;
                _lastDragWidthM = 0.34f;
                EmitWallRunItems(a, b, storyIndex, worldChunks, dst, ref tempBayId);
                return;
            }

            float refRad = (_hasRefAxis ? _refAxisYawDeg : _manualYawDeg) * Mathf.Deg2Rad;
            Vector2 axisF = new Vector2(Mathf.Sin(refRad), Mathf.Cos(refRad));
            Vector2 axisR = new Vector2(axisF.y, -axisF.x);
            Vector2 d = b2 - a2;
            float lenF = Vector2.Dot(d, axisF);
            float lenR = Vector2.Dot(d, axisR);
            if (Mathf.Abs(lenF) < 1.2f) lenF = Mathf.Sign(lenF == 0f ? 1f : lenF) * 1.2f;
            if (Mathf.Abs(lenR) < 1.2f) lenR = Mathf.Sign(lenR == 0f ? 1f : lenR) * 1.2f;

            _lastDragLengthM = Mathf.Abs(lenF);
            _lastDragWidthM = Mathf.Abs(lenR);

            Vector2 c00 = a2;
            Vector2 c10 = a2 + axisF * lenF;
            Vector2 c11 = a2 + axisF * lenF + axisR * lenR;
            Vector2 c01 = a2 + axisR * lenR;

            float baseGroundY = SampleGroundY((c00.x + c11.x) * 0.5f, (c00.y + c11.y) * 0.5f, a.y, worldChunks);

            if (tool == BlueprintDrawTool.RectangleRoom)
            {
                Vector3 p00 = new Vector3(c00.x, SampleGroundY(c00.x, c00.y, baseGroundY, worldChunks), c00.y);
                Vector3 p10 = new Vector3(c10.x, SampleGroundY(c10.x, c10.y, baseGroundY, worldChunks), c10.y);
                Vector3 p11 = new Vector3(c11.x, SampleGroundY(c11.x, c11.y, baseGroundY, worldChunks), c11.y);
                Vector3 p01 = new Vector3(c01.x, SampleGroundY(c01.x, c01.y, baseGroundY, worldChunks), c01.y);

                EmitWallRunItems(p00, p10, storyIndex, worldChunks, dst, ref tempBayId);
                EmitWallRunItems(p10, p11, storyIndex, worldChunks, dst, ref tempBayId);
                EmitWallRunItems(p11, p01, storyIndex, worldChunks, dst, ref tempBayId);
                EmitWallRunItems(p01, p00, storyIndex, worldChunks, dst, ref tempBayId);

                float deckY = baseGroundY + (storyIndex + 1) * StoryHeightM;
                EmitFloorDeckItem(c00, c10, c11, c01, deckY, refRad * Mathf.Rad2Deg, dst);
                return;
            }

            if (tool == BlueprintDrawTool.FloorDeckArea)
            {
                // 1. Kat (storyIndex=0) -> Zemin Kat Ahsap Dosemesi (+0.08m); 2. Kat -> +2.68m; 3. Kat -> +5.28m!
                float deckY = baseGroundY + storyIndex * StoryHeightM + 0.08f;
                EmitFloorDeckItem(c00, c10, c11, c01, deckY, refRad * Mathf.Rad2Deg, dst, isGroundedDeck: storyIndex == 0);
                return;
            }

            if (tool == BlueprintDrawTool.GableRoofArea)
            {
                float eaveY = baseGroundY + (storyIndex + 1) * StoryHeightM;
                EmitGableRoofItems(c00, c10, c11, c01, eaveY, refRad * Mathf.Rad2Deg, dst);
            }
        }

        private void EmitWallRunItems(
            Vector3 a, Vector3 b, int storyIndex,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            List<PreviewNodeItem> dst, ref int bayIdCounter)
        {
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            float dist = Vector2.Distance(a2, b2);
            if (dist < 0.8f) return;

            int bays = _autoIntermediatePosts ? Mathf.Max(1, Mathf.CeilToInt(dist / MaxAutoBaySpanM)) : 1;
            float yaw = Mathf.Atan2(b2.x - a2.x, b2.y - a2.y) * Mathf.Rad2Deg;
            Quaternion qHoriz = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f);
            float storyBaseOffset = storyIndex * StoryHeightM;

            for (int i = 0; i <= bays; i++)
            {
                Vector2 p2 = Vector2.Lerp(a2, b2, (float)i / bays);
                float gY = SampleGroundY(p2.x, p2.y, a.y, worldChunks);
                BlueprintNodeSpec postSpec = MakePostSpec(new Vector3(p2.x, gY, p2.y), storyIndex);

                if (!HasSameStoryPostNear(dst, postSpec.Position, 0.45f) && !HasExistingSameStoryPostNear(postSpec.Position, 0.45f))
                {
                    dst.Add(new PreviewNodeItem { Spec = postSpec, WallBayId = 0 });
                }
            }

            float baySpan = dist / bays;
            float[] courseOffsets = { 0.32f, 0.92f, 1.52f, 2.12f };

            for (int i = 0; i < bays; i++)
            {
                int thisBayId = bayIdCounter++;
                Vector2 p0 = Vector2.Lerp(a2, b2, (float)i / bays);
                Vector2 p1 = Vector2.Lerp(a2, b2, (float)(i + 1) / bays);
                Vector2 mid = (p0 + p1) * 0.5f;
                float gY = SampleGroundY(mid.x, mid.y, a.y, worldChunks) + storyBaseOffset;

                for (int c = 0; c < courseOffsets.Length; c++)
                {
                    dst.Add(new PreviewNodeItem
                    {
                        WallBayId = thisBayId,
                        Spec = new BlueprintNodeSpec
                        {
                            Id = _nextNodeId,
                            Kind = BlueprintPieceKind.WallLog,
                            Position = ToSys(new Vector3(mid.x, gY + courseOffsets[c], mid.y)),
                            Rotation = ToSys(qHoriz),
                            LengthM = baySpan,
                            DiameterM = 0.30f,
                            HalfExtents = new SysVec3(0.15f, baySpan * 0.5f, 0.15f),
                            Species = TimberWoodSpecies.Oak,
                            IsGrounded = false
                        }
                    });
                }

                dst.Add(new PreviewNodeItem
                {
                    WallBayId = thisBayId,
                    Spec = new BlueprintNodeSpec
                    {
                        Id = _nextNodeId,
                        Kind = BlueprintPieceKind.FloorBeam,
                        Position = ToSys(new Vector3(mid.x, gY + StoryHeightM, mid.y)),
                        Rotation = ToSys(qHoriz),
                        LengthM = baySpan,
                        DiameterM = 0.30f,
                        HalfExtents = new SysVec3(0.15f, baySpan * 0.5f, 0.15f),
                        Species = TimberWoodSpecies.Oak,
                        IsGrounded = false
                    }
                });
            }
        }

        private void EmitFloorDeckItem(Vector2 c00, Vector2 c10, Vector2 c11, Vector2 c01, float deckY, float yawDeg, List<PreviewNodeItem> dst, bool isGroundedDeck = false)
        {
            Vector2 center = (c00 + c11) * 0.5f;
            float lenF = Vector2.Distance(c00, c10);
            float lenR = Vector2.Distance(c00, c01);
            Quaternion qDeck = Quaternion.Euler(0f, yawDeg, 0f);

            dst.Add(new PreviewNodeItem
            {
                WallBayId = 0,
                Spec = new BlueprintNodeSpec
                {
                    Id = _nextNodeId,
                    Kind = BlueprintPieceKind.FloorDeck,
                    Position = ToSys(new Vector3(center.x, deckY + 0.10f, center.y)),
                    Rotation = ToSys(qDeck),
                    LengthM = Mathf.Max(lenF, lenR),
                    DiameterM = 0.24f,
                    HalfExtents = new SysVec3(lenR * 0.5f, 0.09f, lenF * 0.5f),
                    Species = TimberWoodSpecies.Oak,
                    IsGrounded = isGroundedDeck
                }
            });
        }

        private void EmitGableRoofItems(Vector2 c00, Vector2 c10, Vector2 c11, Vector2 c01, float eaveY, float yawDeg, List<PreviewNodeItem> dst)
        {
            Vector2 center = (c00 + c11) * 0.5f;
            float lenF = Vector2.Distance(c00, c10);
            float lenR = Vector2.Distance(c00, c01);
            float halfW = lenR * 0.5f;
            float pitchRad = 30f * Mathf.Deg2Rad;
            float ridgeRise = halfW * Mathf.Tan(pitchRad);
            float slopeLen = halfW / Mathf.Cos(pitchRad) + 0.35f;

            float refRad = yawDeg * Mathf.Deg2Rad;
            Vector2 axisR = new Vector2(Mathf.Cos(refRad), -Mathf.Sin(refRad));
            if (Vector2.Dot(c01 - c00, axisR) < 0f) axisR = -axisR;

            Quaternion qRidge = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(90f, 0f, 0f);
            dst.Add(new PreviewNodeItem
            {
                WallBayId = 0,
                Spec = new BlueprintNodeSpec
                {
                    Id = _nextNodeId,
                    Kind = BlueprintPieceKind.RidgeOrTieBeam,
                    Position = ToSys(new Vector3(center.x, eaveY + ridgeRise, center.y)),
                    Rotation = ToSys(qRidge),
                    LengthM = lenF + 0.4f,
                    DiameterM = 0.30f,
                    HalfExtents = new SysVec3(0.15f, (lenF + 0.4f) * 0.5f, 0.15f),
                    Species = TimberWoodSpecies.Oak,
                    IsGrounded = false
                }
            });

            Vector2 leftC = center - axisR * (halfW * 0.5f);
            Vector2 rightC = center + axisR * (halfW * 0.5f);
            float midY = eaveY + ridgeRise * 0.5f;

            Quaternion qLeft = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(0f, 0f, 30f);
            Quaternion qRight = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(0f, 0f, -30f);

            dst.Add(new PreviewNodeItem
            {
                WallBayId = 0,
                Spec = new BlueprintNodeSpec
                {
                    Id = _nextNodeId,
                    Kind = BlueprintPieceKind.RoofSlope,
                    Position = ToSys(new Vector3(leftC.x, midY, leftC.y)),
                    Rotation = ToSys(qLeft),
                    LengthM = slopeLen,
                    DiameterM = 0.22f,
                    HalfExtents = new SysVec3(slopeLen * 0.5f, 0.08f, (lenF + 0.4f) * 0.5f),
                    Species = TimberWoodSpecies.Oak,
                    IsGrounded = false
                }
            });
            dst.Add(new PreviewNodeItem
            {
                WallBayId = 0,
                Spec = new BlueprintNodeSpec
                {
                    Id = _nextNodeId,
                    Kind = BlueprintPieceKind.RoofSlope,
                    Position = ToSys(new Vector3(rightC.x, midY, rightC.y)),
                    Rotation = ToSys(qRight),
                    LengthM = slopeLen,
                    DiameterM = 0.22f,
                    HalfExtents = new SysVec3(slopeLen * 0.5f, 0.08f, (lenF + 0.4f) * 0.5f),
                    Species = TimberWoodSpecies.Oak,
                    IsGrounded = false
                }
            });
        }

        private BlueprintNodeSpec MakePostSpec(Vector3 groundPos, int storyIndex)
        {
            if (storyIndex <= 0)
            {
                return new BlueprintNodeSpec
                {
                    Id = _nextNodeId,
                    Kind = BlueprintPieceKind.Post,
                    Position = ToSys(new Vector3(groundPos.x, groundPos.y - DefaultPostEmbedM + GroundPostHeightM * 0.5f, groundPos.z)),
                    Rotation = SysQuat.Identity,
                    LengthM = GroundPostHeightM,
                    DiameterM = 0.34f,
                    HalfExtents = new SysVec3(0.17f, GroundPostHeightM * 0.5f, 0.17f),
                    Species = TimberWoodSpecies.Oak,
                    IsGrounded = true,
                    PlannedEmbedDepthM = DefaultPostEmbedM
                };
            }

            // 2. ve 3. Kat direkleri alt kattaki direk/kiris ustune oturur (IsGrounded=false -> altta destek yoksa KIRMIZI COKER!)
            float baseY = groundPos.y + storyIndex * StoryHeightM;
            return new BlueprintNodeSpec
            {
                Id = _nextNodeId,
                Kind = BlueprintPieceKind.Post,
                Position = ToSys(new Vector3(groundPos.x, baseY + StoryHeightM * 0.5f, groundPos.z)),
                Rotation = SysQuat.Identity,
                LengthM = StoryHeightM,
                DiameterM = 0.32f,
                HalfExtents = new SysVec3(0.16f, StoryHeightM * 0.5f, 0.16f),
                Species = TimberWoodSpecies.Oak,
                IsGrounded = false,
                PlannedEmbedDepthM = 0f
            };
        }

        private static bool HasSameStoryPostNear(List<PreviewNodeItem> list, SysVec3 pos, float tol)
        {
            float tolSq = tol * tol;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Spec.Kind != BlueprintPieceKind.Post) continue;
                if (MathF.Abs(list[i].Spec.Position.Y - pos.Y) > 0.6f) continue;
                float dx = list[i].Spec.Position.X - pos.X;
                float dz = list[i].Spec.Position.Z - pos.Z;
                if (dx * dx + dz * dz <= tolSq) return true;
            }
            return false;
        }

        private bool HasExistingSameStoryPostNear(SysVec3 pos, float tol)
        {
            float tolSq = tol * tol;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Spec.Kind != BlueprintPieceKind.Post) continue;
                if (MathF.Abs(_nodes[i].Spec.Position.Y - pos.Y) > 0.6f) continue;
                float dx = _nodes[i].Spec.Position.X - pos.X;
                float dz = _nodes[i].Spec.Position.Z - pos.Z;
                if (dx * dx + dz * dz <= tolSq) return true;
            }
            return false;
        }

        /// <summary>
        /// Evrensel Mıknatıslı Kenetlenme:
        /// 1) Duvar/Kiriş Uçları (silinen köşe direğinin tam yerine yeni direk veya ek duvar oturtmak için!)
        /// 2) Mevcut Plan Direkleri / Alt Kat Köşeleri
        /// 3) Duvar/Kiriş Orta Noktası (kırmızı kirişin tam ortasına destek direği koymak için!)
        /// 4) Mevcut Duvar Hattına T-Birleşim (mevcut evin yanına tam bitişik yeni oda/bölme duvar eklemek için!)
        /// </summary>
        public bool TrySnapToAnyBlueprintAnchor(
            Vector3 aimPt, float maxDist,
            out Vector3 snappedGroundPos, out string snapTag, out float snappedWallAxisYawDeg)
        {
            snappedGroundPos = aimPt;
            snapTag = string.Empty;
            snappedWallAxisYawDeg = float.NaN;
            if (_nodes.Count == 0) return false;

            Vector2 a2 = new Vector2(aimPt.x, aimPt.z);
            float bestScore = maxDist;
            bool found = false;

            // 1) Mevcut Direkler (Yuksek oncelik: -0.15m bonus)
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Spec.Kind != BlueprintPieceKind.Post) continue;
                var sp = _nodes[i].Spec;
                Vector2 p2 = new Vector2(sp.Position.X, sp.Position.Z);
                float d = Vector2.Distance(a2, p2);
                if (d < maxDist && d - 0.15f < bestScore)
                {
                    bestScore = d - 0.15f;
                    float gY = sp.IsGrounded ? (sp.Position.Y - sp.LengthM * 0.5f + sp.PlannedEmbedDepthM) : aimPt.y;
                    snappedGroundPos = new Vector3(p2.x, gY, p2.y);
                    snapTag = "🔗 KÖŞE DİREĞİNE KİLİTLİ";
                    found = true;
                }
            }

            // 2) Duvar / Kiris Uclari, Orta Noktalari ve T-Birlesim Hattı
            for (int i = 0; i < _nodes.Count; i++)
            {
                var sp = _nodes[i].Spec;
                if (sp.Kind is not (BlueprintPieceKind.WallLog or BlueprintPieceKind.FloorBeam)) continue;

                Vector3 center = ToUnity(sp.Position);
                Quaternion rot = ToUnity(sp.Rotation);
                Vector3 dir = rot * Vector3.up;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) continue;
                dir.Normalize();

                float wallYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float halfL = sp.LengthM * 0.5f;
                Vector2 c2 = new Vector2(center.x, center.z);
                Vector2 d2 = new Vector2(dir.x, dir.z);

                Vector2 end0 = c2 - d2 * halfL;
                Vector2 end1 = c2 + d2 * halfL;

                // Duvar/Kiris Ucu #1 (Silinen sutunun tam yeri!)
                float dEnd0 = Vector2.Distance(a2, end0);
                if (dEnd0 < maxDist && dEnd0 - 0.12f < bestScore)
                {
                    bestScore = dEnd0 - 0.12f;
                    snappedGroundPos = new Vector3(end0.x, aimPt.y, end0.y);
                    snapTag = "🔗 DUVAR UCUNA TAM KİLİTLİ";
                    snappedWallAxisYawDeg = wallYaw;
                    found = true;
                }

                // Duvar/Kiris Ucu #2
                float dEnd1 = Vector2.Distance(a2, end1);
                if (dEnd1 < maxDist && dEnd1 - 0.12f < bestScore)
                {
                    bestScore = dEnd1 - 0.12f;
                    snappedGroundPos = new Vector3(end1.x, aimPt.y, end1.y);
                    snapTag = "🔗 DUVAR UCUNA TAM KİLİTLİ";
                    snappedWallAxisYawDeg = wallYaw;
                    found = true;
                }

                // Duvar/Kiris Orta Noktasi (Destek diregi veya T-duvar ortasi)
                float dMid = Vector2.Distance(a2, c2);
                if (dMid < maxDist * 0.85f && dMid < bestScore)
                {
                    bestScore = dMid;
                    snappedGroundPos = new Vector3(c2.x, aimPt.y, c2.y);
                    snapTag = "🔗 DUVAR ORTASINA KİLİTLİ";
                    snappedWallAxisYawDeg = wallYaw;
                    found = true;
                }

                // Duvar Hattina T-Birlesim Izdusumu
                float proj = Vector2.Dot(a2 - c2, d2);
                if (Mathf.Abs(proj) <= halfL - 0.25f)
                {
                    Vector2 onLine = c2 + d2 * proj;
                    float perp = Vector2.Distance(a2, onLine);
                    if (perp < 0.70f && perp + 0.18f < bestScore)
                    {
                        bestScore = perp + 0.18f;
                        snappedGroundPos = new Vector3(onLine.x, aimPt.y, onLine.y);
                        snapTag = "🔗 DUVAR HATTINA T-BİRLEŞİM";
                        snappedWallAxisYawDeg = wallYaw;
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool TrySnapToExistingPostXZ(Vector3 aimPt, float maxDist, out Vector3 snappedGroundPos)
        {
            snappedGroundPos = aimPt;
            float best = maxDist;
            bool found = false;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Spec.Kind != BlueprintPieceKind.Post) continue;
                var sp = _nodes[i].Spec;
                float gY = sp.IsGrounded ? (sp.Position.Y - sp.LengthM * 0.5f + sp.PlannedEmbedDepthM) : aimPt.y;
                Vector3 g = new Vector3(sp.Position.X, gY, sp.Position.Z);
                float d = Vector2.Distance(new Vector2(aimPt.x, aimPt.z), new Vector2(g.x, g.z));
                if (d < best)
                {
                    best = d;
                    snappedGroundPos = g;
                    found = true;
                }
            }
            return found;
        }

        private BlueprintStabilityTier EvaluatePreviewBatch(
            List<PreviewNodeItem> batch, out BlueprintFailureReason primaryReason, out int estStabilityPct)
        {
            primaryReason = BlueprintFailureReason.None;
            estStabilityPct = 100;
            if (batch.Count == 0) return BlueprintStabilityTier.SafeGreen;

            int existing = _nodes.Count;
            int total = existing + batch.Count;
            var specs = new BlueprintNodeSpec[total];
            for (int i = 0; i < existing; i++) specs[i] = _nodes[i].Spec;
            for (int i = 0; i < batch.Count; i++)
            {
                var s = batch[i].Spec;
                s.Id = _nextNodeId + (uint)i + 1000u;
                specs[existing + i] = s;
            }

            var evals = new BlueprintNodeEvaluation[total];
            BlueprintPlanSummary sum = TimberBlueprintStructuralAnalyzer.AnalyzePlan(specs, evals);
            estStabilityPct = sum.OverallStabilityPct;

            BlueprintStabilityTier worst = BlueprintStabilityTier.GroundedBlue;
            for (int i = existing; i < total; i++)
            {
                if (evals[i].Tier > worst && evals[i].Tier != BlueprintStabilityTier.BuiltSolid)
                {
                    worst = evals[i].Tier;
                    if (evals[i].Reason != BlueprintFailureReason.None) primaryReason = evals[i].Reason;
                }
            }
            if (primaryReason == BlueprintFailureReason.None) primaryReason = sum.PrimaryFailure;
            return worst;
        }

        private void CommitPreviewBatch(List<PreviewNodeItem> batch)
        {
            int maxBayUsed = _nextWallBayId - 1;
            for (int i = 0; i < batch.Count; i++)
            {
                BlueprintNodeSpec spec = batch[i].Spec;
                spec.Id = _nextNodeId++;
                int bayId = batch[i].WallBayId;
                if (bayId > maxBayUsed) maxBayUsed = bayId;
                var node = new PlacedBlueprintNode { Spec = spec, WallBayId = bayId };
                BuildPlacedNodeVisual(node);
                _nodes.Add(node);
            }
            _nextWallBayId = maxBayUsed + 1;
            ReanalyzeAllPlacedNodes();
        }

        private void ReanalyzeAllPlacedNodes()
        {
            int n = _nodes.Count;
            if (n == 0)
            {
                LastSummary = default;
                return;
            }

            var specs = new BlueprintNodeSpec[n];
            for (int i = 0; i < n; i++) specs[i] = _nodes[i].Spec;
            var evals = new BlueprintNodeEvaluation[n];
            LastSummary = TimberBlueprintStructuralAnalyzer.AnalyzePlan(specs, evals);

            for (int i = 0; i < n; i++)
            {
                _nodes[i].Eval = evals[i];
                ApplyTierColorToNode(_nodes[i]);
            }
        }

        private void SyncBuiltNodesFromWorld(Stage1RigidTimberBridge bridge)
        {
            bool anyChanged = false;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var nd = _nodes[i];
                bool matched = bridge.HasStaticPieceMatchingBlueprint(
                    ToUnity(nd.Spec.Position),
                    nd.Spec.Kind == BlueprintPieceKind.Post,
                    toleranceM: 0.65f);
                if (matched != nd.Spec.IsBuilt)
                {
                    nd.Spec.IsBuilt = matched;
                    anyChanged = true;
                }
            }
            if (anyChanged) ReanalyzeAllPlacedNodes();
        }

        private int FindAimedWallBayId(
            Vector3 rayOrigin, Vector3 rayDir, float maxDist,
            out Vector3 bayCenterBase, out Quaternion bayRot, out float baySpan)
        {
            int bestBay = -1;
            float bestDist = maxDist;
            bayCenterBase = Vector3.zero;
            bayRot = Quaternion.identity;
            baySpan = 2.6f;

            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Spec.Kind != BlueprintPieceKind.WallLog || _nodes[i].WallBayId <= 0) continue;
                Vector3 c = ToUnity(_nodes[i].Spec.Position);
                float proj = Vector3.Dot(c - rayOrigin, rayDir);
                if (proj < 0.3f || proj > bestDist) continue;
                Vector3 closest = rayOrigin + rayDir * proj;
                if (Vector3.Distance(closest, c) <= Mathf.Max(0.95f, _nodes[i].Spec.LengthM * 0.45f))
                {
                    bestDist = proj;
                    bestBay = _nodes[i].WallBayId;
                }
            }

            if (bestBay > 0)
            {
                float minY = float.MaxValue;
                Vector3 pos = Vector3.zero;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].WallBayId == bestBay && _nodes[i].Spec.Kind == BlueprintPieceKind.WallLog)
                    {
                        if (_nodes[i].Spec.Position.Y < minY)
                        {
                            minY = _nodes[i].Spec.Position.Y;
                            pos = ToUnity(_nodes[i].Spec.Position);
                            bayRot = ToUnity(_nodes[i].Spec.Rotation);
                            baySpan = _nodes[i].Spec.LengthM;
                        }
                    }
                }
                bayCenterBase = new Vector3(pos.x, minY - 0.32f, pos.z);
            }
            return bestBay;
        }

        private bool TryRemoveAimedBlueprintNode(Vector3 rayOrigin, Vector3 rayDir, float maxDist, out string feedback)
        {
            feedback = string.Empty;
            int bestIdx = -1;
            float bestDist = maxDist;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Vector3 c = ToUnity(_nodes[i].Spec.Position);
                float proj = Vector3.Dot(c - rayOrigin, rayDir);
                if (proj < 0.2f || proj > bestDist) continue;
                Vector3 closest = rayOrigin + rayDir * proj;
                float hitRad = Mathf.Max(0.60f, _nodes[i].Spec.LengthM * 0.38f);
                if (Vector3.Distance(closest, c) <= hitRad)
                {
                    bestDist = proj;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return false;

            PushUndoSnapshot();
            int bayId = _nodes[bestIdx].WallBayId;
            if (bayId > 0 && _nodes[bestIdx].Spec.Kind == BlueprintPieceKind.WallLog)
            {
                int removed = 0;
                for (int i = _nodes.Count - 1; i >= 0; i--)
                {
                    if (_nodes[i].WallBayId == bayId && _nodes[i].Spec.Kind != BlueprintPieceKind.Post)
                    {
                        if (_nodes[i].RootGo != null) UnityEngine.Object.Destroy(_nodes[i].RootGo);
                        if (_nodes[i].InstanceMat != null) UnityEngine.Object.Destroy(_nodes[i].InstanceMat);
                        _nodes.RemoveAt(i);
                        removed++;
                    }
                }
                ReanalyzeAllPlacedNodes();
                feedback = $"🗑️ Duvar bölümü #{bayId} ({removed} parça) plandan silindi!";
                return true;
            }

            string lbl = GetKindLabel(_nodes[bestIdx].Spec.Kind);
            if (_nodes[bestIdx].RootGo != null) UnityEngine.Object.Destroy(_nodes[bestIdx].RootGo);
            if (_nodes[bestIdx].InstanceMat != null) UnityEngine.Object.Destroy(_nodes[bestIdx].InstanceMat);
            _nodes.RemoveAt(bestIdx);
            ReanalyzeAllPlacedNodes();
            feedback = $"🗑️ Plandan {lbl} silindi! Kalan: {_nodes.Count} parça";
            return true;
        }

        private void RenderOpeningHighlightPreview(Vector3 bayBase, Quaternion bayLogRot, float baySpan, bool isWindow)
        {
            EnsurePreviewGo(new Color(0.15f, 0.92f, 1.0f, 0.85f));
            _previewGo!.SetActive(true);
            _previewGo.transform.position = Vector3.zero;
            _previewGo.transform.rotation = Quaternion.identity;

            _meshScratch.Clear();
            EmitOpeningFrameMesh(bayBase, bayLogRot, baySpan, isWindow, _meshScratch);
            var mf = _previewGo.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
            mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, "Mesh_BP_OpeningPreview");

            Color col = isWindow ? new Color(0.20f, 0.92f, 1.0f, 0.85f) : new Color(1.0f, 0.82f, 0.18f, 0.88f);
            if (_previewMat != null)
            {
                _previewMat.color = col;
                if (_previewMat.HasProperty("_BaseColor")) _previewMat.SetColor("_BaseColor", col);
            }
        }

        private void SpawnPermanentOpeningMarker(int wallBayId, Vector3 bayBase, Quaternion bayLogRot, float baySpan, bool isWindow)
        {
            GameObject go = new GameObject($"BlueprintOpening_{(isWindow ? "Window" : "Door")}_{wallBayId}");
            _meshScratch.Clear();
            EmitOpeningFrameMesh(bayBase, bayLogRot, baySpan, isWindow, _meshScratch);
            go.AddComponent<MeshFilter>().sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_Open_{wallBayId}");
            Color col = isWindow ? new Color(0.20f, 0.88f, 1.0f, 0.78f) : new Color(1.0f, 0.78f, 0.18f, 0.82f);
            Material mat = CreateTransparentBlueprintMat(col);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _openingMarkers.Add(new OpeningMarkerVisual
            {
                WallBayId = wallBayId,
                IsWindow = isWindow,
                BayBase = bayBase,
                BayRot = bayLogRot,
                BaySpan = baySpan,
                RootGo = go,
                Mat = mat
            });
        }

        private static void EmitOpeningFrameMesh(Vector3 bayBase, Quaternion bayLogRot, float baySpan, bool isWindow, List<FacetedVoxelTriangle> tris)
        {
            // WallLog'un +Y ekseni duvar boyunca uzanir; dikey eksen dunya +Y'dir.
            Vector3 wallDir = bayLogRot * Vector3.up;
            wallDir.y = 0f;
            if (wallDir.sqrMagnitude < 1e-4f) wallDir = Vector3.right;
            wallDir.Normalize();
            float yaw = Mathf.Atan2(wallDir.x, wallDir.z) * Mathf.Rad2Deg;
            Quaternion frameRot = Quaternion.Euler(0f, yaw, 0f);

            float openW = Mathf.Clamp(baySpan - 0.45f, 1.10f, isWindow ? 1.65f : 1.95f);
            float yBot = isWindow ? 0.80f : 0.05f;
            float yTop = isWindow ? 1.85f : 2.02f;
            float midY = (yBot + yTop) * 0.5f;
            float halfH = (yTop - yBot) * 0.5f;

            Matrix4x4 trs = Matrix4x4.TRS(bayBase + Vector3.up * midY, frameRot, Vector3.one);
            EmitWireBoxXform(tris, new Vector3(-0.18f, -halfH, -openW * 0.5f), new Vector3(0.18f, halfH, openW * 0.5f), 0.055f, trs);
        }

        private static Vector3 ResolveAimGroundPoint(
            UnityEngine.Camera cam, Transform playerTr,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 14.0f)
                return miningCtrl.AimHit.point;

            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;
            float groundY = SampleGroundY(playerTr.position.x, playerTr.position.z, playerTr.position.y, worldChunks);
            if (dir.y < -0.05f)
            {
                float t = Mathf.Clamp((groundY - origin.y) / dir.y, 1.5f, 14.0f);
                return origin + dir * t;
            }
            Vector3 fwdFlat = new Vector3(dir.x, 0f, dir.z);
            if (fwdFlat.sqrMagnitude < 1e-4f) fwdFlat = Vector3.forward;
            fwdFlat.Normalize();
            Vector3 p = playerTr.position + fwdFlat * 5.0f;
            p.y = SampleGroundY(p.x, p.z, groundY, worldChunks);
            return p;
        }

        private static float SampleGroundY(float wx, float wz, float refY, Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (worldChunks == null) return refY;
            int ix = Mathf.FloorToInt(wx);
            int iz = Mathf.FloorToInt(wz);
            int cx = ix >= 0 ? (ix >> 4) : -(((-ix) + 15) >> 4);
            int cz = iz >= 0 ? (iz >> 4) : -(((-iz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? vox) || vox == null) return refY;
            int lx = ix - (cx << 4);
            int lz = iz - (cz << 4);
            int maxY = Mathf.Clamp(Mathf.FloorToInt(refY) + 4, 2, 126);
            for (int y = maxY; y >= 1; y--)
            {
                uint cell = vox[(y << 8) | (lz << 4) | lx];
                if (VoxelBitUtility.GetMaterialId(cell) != MaterialIdCatalog.Air)
                    return y + 1.0f;
            }
            return refY;
        }

        private void BuildPlacedNodeVisual(PlacedBlueprintNode node)
        {
            GameObject go = new GameObject($"BlueprintPlan_{node.Spec.Kind}_{node.Spec.Id}");
            go.transform.position = ToUnity(node.Spec.Position);
            go.transform.rotation = ToUnity(node.Spec.Rotation);

            _meshScratch.Clear();
            EmitSpecWireframeLocal(in node.Spec, Matrix4x4.identity, _meshScratch);
            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_BP_{node.Spec.Id}");
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            Material mat = CreateTransparentBlueprintMat(GetColorForTier(BlueprintStabilityTier.SafeGreen));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            node.RootGo = go;
            node.InstanceMat = mat;
        }

        private void EnsurePreviewGo(Color initCol)
        {
            if (_previewGo != null) return;
            _previewGo = new GameObject("BlueprintDragPreviewRoot");
            _previewGo.AddComponent<MeshFilter>();
            _previewMat = CreateTransparentBlueprintMat(initCol);
            var mr = _previewGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _previewMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void RenderPreviewBatch(List<PreviewNodeItem> batch, BlueprintStabilityTier tier)
        {
            if (batch.Count == 0)
            {
                if (_previewGo != null) _previewGo.SetActive(false);
                return;
            }

            EnsurePreviewGo(GetColorForTier(tier));
            _previewGo!.SetActive(true);
            _previewGo.transform.position = Vector3.zero;
            _previewGo.transform.rotation = Quaternion.identity;

            _meshScratch.Clear();
            for (int i = 0; i < batch.Count; i++)
            {
                BlueprintNodeSpec sp = batch[i].Spec;
                Matrix4x4 trs = Matrix4x4.TRS(ToUnity(sp.Position), ToUnity(sp.Rotation), Vector3.one);
                EmitSpecWireframeLocal(in sp, trs, _meshScratch);
            }

            var mf = _previewGo.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
            mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, "Mesh_BP_DragPreview");

            Color col = GetColorForTier(tier);
            col.a = 0.78f;
            if (_previewMat != null)
            {
                _previewMat.color = col;
                if (_previewMat.HasProperty("_BaseColor")) _previewMat.SetColor("_BaseColor", col);
            }
        }

        private static void ApplyTierColorToNode(PlacedBlueprintNode node)
        {
            if (node.RootGo == null || node.InstanceMat == null) return;
            if (node.Eval.Tier == BlueprintStabilityTier.BuiltSolid)
            {
                node.RootGo.SetActive(false);
                return;
            }
            node.RootGo.SetActive(true);
            Color c = GetColorForTier(node.Eval.Tier);
            node.InstanceMat.color = c;
            if (node.InstanceMat.HasProperty("_BaseColor")) node.InstanceMat.SetColor("_BaseColor", c);
        }

        private static void EmitSpecWireframeLocal(in BlueprintNodeSpec spec, Matrix4x4 xform, List<FacetedVoxelTriangle> tris)
        {
            Vector3 he = new Vector3(spec.HalfExtents.X, spec.HalfExtents.Y, spec.HalfExtents.Z);
            if (he.sqrMagnitude < 1e-4f) he = new Vector3(spec.DiameterM * 0.5f, spec.LengthM * 0.5f, spec.DiameterM * 0.5f);
            EmitWireBoxXform(tris, -he, he, 0.032f, xform);

            if (spec.Kind == BlueprintPieceKind.Post && spec.IsGrounded)
            {
                float holeY = -spec.LengthM * 0.5f + spec.PlannedEmbedDepthM + 0.04f;
                EmitWireBoxXform(tris, new Vector3(-0.34f, holeY - 0.02f, -0.34f), new Vector3(0.34f, holeY + 0.04f, 0.34f), 0.038f, xform);
            }
        }

        private static void EmitWireBoxXform(List<FacetedVoxelTriangle> tris, Vector3 min, Vector3 max, float t, Matrix4x4 m)
        {
            float x0 = min.x, x1 = max.x, y0 = min.y, y1 = max.y, z0 = min.z, z1 = max.z;
            EmitBoxXform(tris, new Vector3(x0, y0, z0), new Vector3(x1, y0 + t, z0 + t), m);
            EmitBoxXform(tris, new Vector3(x0, y0, z1 - t), new Vector3(x1, y0 + t, z1), m);
            EmitBoxXform(tris, new Vector3(x0, y1 - t, z0), new Vector3(x1, y1, z0 + t), m);
            EmitBoxXform(tris, new Vector3(x0, y1 - t, z1 - t), new Vector3(x1, y1, z1), m);

            EmitBoxXform(tris, new Vector3(x0, y0, z0), new Vector3(x0 + t, y0 + t, z1), m);
            EmitBoxXform(tris, new Vector3(x1 - t, y0, z0), new Vector3(x1, y0 + t, z1), m);
            EmitBoxXform(tris, new Vector3(x0, y1 - t, z0), new Vector3(x0 + t, y1, z1), m);
            EmitBoxXform(tris, new Vector3(x1 - t, y1 - t, z0), new Vector3(x1, y1, z1), m);

            EmitBoxXform(tris, new Vector3(x0, y0, z0), new Vector3(x0 + t, y1, z0 + t), m);
            EmitBoxXform(tris, new Vector3(x1 - t, y0, z0), new Vector3(x1, y1, z0 + t), m);
            EmitBoxXform(tris, new Vector3(x0, y0, z1 - t), new Vector3(x0 + t, y1, z1), m);
            EmitBoxXform(tris, new Vector3(x1 - t, y0, z1 - t), new Vector3(x1, y1, z1), m);
        }

        private static void EmitBoxXform(List<FacetedVoxelTriangle> tris, Vector3 bMin, Vector3 bMax, Matrix4x4 m)
        {
            SysVec3 v000 = ToSys(m.MultiplyPoint3x4(new Vector3(bMin.x, bMin.y, bMin.z)));
            SysVec3 v100 = ToSys(m.MultiplyPoint3x4(new Vector3(bMax.x, bMin.y, bMin.z)));
            SysVec3 v110 = ToSys(m.MultiplyPoint3x4(new Vector3(bMax.x, bMax.y, bMin.z)));
            SysVec3 v010 = ToSys(m.MultiplyPoint3x4(new Vector3(bMin.x, bMax.y, bMin.z)));
            SysVec3 v001 = ToSys(m.MultiplyPoint3x4(new Vector3(bMin.x, bMin.y, bMax.z)));
            SysVec3 v101 = ToSys(m.MultiplyPoint3x4(new Vector3(bMax.x, bMin.y, bMax.z)));
            SysVec3 v111 = ToSys(m.MultiplyPoint3x4(new Vector3(bMax.x, bMax.y, bMax.z)));
            SysVec3 v011 = ToSys(m.MultiplyPoint3x4(new Vector3(bMin.x, bMax.y, bMax.z)));

            EmitQuad(tris, v001, v101, v111, v011, SysVec3.UnitZ);
            EmitQuad(tris, v100, v000, v010, v110, -SysVec3.UnitZ);
            EmitQuad(tris, v000, v001, v011, v010, -SysVec3.UnitX);
            EmitQuad(tris, v101, v100, v110, v111, SysVec3.UnitX);
            EmitQuad(tris, v011, v111, v110, v010, SysVec3.UnitY);
            EmitQuad(tris, v000, v100, v101, v001, -SysVec3.UnitY);
        }

        private static void EmitQuad(List<FacetedVoxelTriangle> tris, SysVec3 a, SysVec3 b, SysVec3 c, SysVec3 d, SysVec3 n)
        {
            tris.Add(new FacetedVoxelTriangle { V0 = a, V1 = b, V2 = c, Normal = n, MaterialId = MaterialIdCatalog.Marble });
            tris.Add(new FacetedVoxelTriangle { V0 = a, V1 = c, V2 = d, Normal = n, MaterialId = MaterialIdCatalog.Marble });
        }

        private static Material CreateTransparentBlueprintMat(Color col)
        {
            Shader? sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            mat.color = col;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3260;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            return mat;
        }

        private string FormatCommitFeedback(string what) => LastSummary.WillStand
            ? $"📐 {what} plana eklendi! ({_nodes.Count} parça — ✅ AYAKTA KALIR %{LastSummary.OverallStabilityPct})"
            : $"⚠️ {what} eklendi ANCAK ÇÖKME RİSKLİ: {TimberBlueprintStructuralAnalyzer.GetReasonTextTurkish(LastSummary.PrimaryFailure)}";

        private static string FormatTierShort(BlueprintStabilityTier tier, int pct, BlueprintFailureReason reason) => tier switch
        {
            BlueprintStabilityTier.GroundedBlue => "🔵 ZEMİNE GÖMÜLÜ (%100)",
            BlueprintStabilityTier.SafeGreen => $"🟢 AYAKTA KALIR (%{pct})",
            BlueprintStabilityTier.CautionYellow => $"🟡 SINIRDA (%{pct} — {TimberBlueprintStructuralAnalyzer.GetReasonTextTurkish(reason)})",
            _ => $"🔴 ÇÖKER! ({TimberBlueprintStructuralAnalyzer.GetReasonTextTurkish(reason)})"
        };

        private static Color GetColorForTier(BlueprintStabilityTier tier) => tier switch
        {
            BlueprintStabilityTier.GroundedBlue => new Color(0.20f, 0.65f, 1.00f, 0.62f),
            BlueprintStabilityTier.SafeGreen => new Color(0.22f, 0.92f, 0.44f, 0.56f),
            BlueprintStabilityTier.CautionYellow => new Color(0.98f, 0.78f, 0.16f, 0.64f),
            BlueprintStabilityTier.CriticalRed => new Color(0.96f, 0.20f, 0.18f, 0.74f),
            _ => new Color(0.60f, 0.90f, 0.70f, 0.30f)
        };

        public static string GetToolLabel(BlueprintDrawTool tool) => tool switch
        {
            BlueprintDrawTool.WallLine => "🧱 Duvar Hattı (Tıkla-Uzat)",
            BlueprintDrawTool.RectangleRoom => "📐 Dikdörtgen Oda / Ev",
            BlueprintDrawTool.FloorDeckArea => "🪵 Kat Döşemesi",
            BlueprintDrawTool.GableRoofArea => "⛺ Beşik Çatı & Mahya",
            BlueprintDrawTool.DoorOrWindowCut => "🚪 Kapı / Pencere Aç",
            BlueprintDrawTool.SingleSupportPost => "🪵 Tekli Destek Direği",
            _ => "Plan"
        };

        public static string GetKindLabel(BlueprintPieceKind kind) => kind switch
        {
            BlueprintPieceKind.Post => "Ana Direk",
            BlueprintPieceKind.WallLog => "Duvar Kütüğü",
            BlueprintPieceKind.FloorBeam => "Kat Kirişi",
            BlueprintPieceKind.FloorDeck => "Ahşap Döşeme",
            BlueprintPieceKind.RoofSlope => "Çatı Paneli",
            BlueprintPieceKind.RidgeOrTieBeam => "Mahya Kirişi",
            _ => "Parça"
        };

        private static SysVec3 ToSys(Vector3 v) => new(v.x, v.y, v.z);
        private static SysQuat ToSys(Quaternion q) => new(q.x, q.y, q.z, q.w);
        private static Vector3 ToUnity(SysVec3 v) => new(v.X, v.Y, v.Z);
        private static Quaternion ToUnity(SysQuat q) => new(q.X, q.Y, q.Z, q.W);
    }
}
#endif
