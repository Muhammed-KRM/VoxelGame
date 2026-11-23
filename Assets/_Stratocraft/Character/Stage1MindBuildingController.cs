#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Systems;
using Stratocraft.Rendering;
using Stratocraft.Voxel.Jobs;
using UnityEngine;
using SysVec3 = System.Numerics.Vector3;

namespace Stratocraft.Character
{
    /// <summary>
    /// Faz 2.3 (Asama 3.2): [T] tusuyla acilan Zihin Insa Menusu (Mind Blueprint Selection),
    /// 2.8 saniyelik tebesir-mavisi gecici 3B dusunce silueti ipucu ve oyuncunun dizdigi
    /// gercek 3B parca kumesini silmeden istasyon olarak etiketleme (Tagging) denetleyicisi.
    /// </summary>
    public sealed class Stage1MindBuildingController
    {
        public struct RegisteredMindStationInfo
        {
            public uint StationId;
            public MindStructureBlueprintId Blueprint;
            public Vector3 Center;
            public float BaseY;
            public float BonusEfficiency01;
        }

        private const float ThoughtSilhouetteDurationSec = 2.80f;
        private const float FinishingTouchCrouchDurationSec = 0.75f;

        private readonly List<RegisteredMindStationInfo> _registeredNonCampfireStations = new(8);
        private readonly List<FacetedVoxelTriangle> _silhouetteTris = new(256);
        private readonly Mesh?[] _silhouetteMeshes = new Mesh?[MindStructureClusterValidator.BlueprintCount];

        private uint _nextStationId = 100u;
        private int _hoveredBlueprintIndex;
        private float _thoughtHintRemainingSec;
        private float _finishingCrouchTimer;
        private float _toastRemainingSec;
        private bool _toastIsSuccess;
        private string _toastMessage = string.Empty;

        private Transform? _silhouetteParent;
        private GameObject? _thoughtSilhouetteGo;
        private MeshFilter? _thoughtSilhouetteMf;
        private MeshRenderer? _thoughtSilhouetteMr;

        private GUIStyle? _panelStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _subTitleStyle;
        private GUIStyle? _cardTitleStyle;
        private GUIStyle? _cardDescStyle;
        private GUIStyle? _toastBoxStyle;
        private GUIStyle? _toastTextStyle;

        public bool IsMindMenuOpen { get; private set; }
        public bool ConsumedEscapeThisFrame { get; private set; }
        public bool ConsumedKeyGThisFrame { get; private set; }
        public bool ForcePlayerCrouch => _finishingCrouchTimer > 0.001f;
        public IReadOnlyList<RegisteredMindStationInfo> RegisteredNonCampfireStations => _registeredNonCampfireStations;

        public void Step(
            float dt,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            bool externalUiBlocked,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl = null,
            Stage1WheelbarrowController? wheelbarrowCtrl = null,
            Stage1WindmillController? windmillCtrl = null)
        {
            ConsumedEscapeThisFrame = false;
            ConsumedKeyGThisFrame = false;

            if (_finishingCrouchTimer > 0f)
            {
                _finishingCrouchTimer = Mathf.Max(0f, _finishingCrouchTimer - dt);
            }
            if (_toastRemainingSec > 0f)
            {
                _toastRemainingSec = Mathf.Max(0f, _toastRemainingSec - dt);
            }

            StepThoughtSilhouette(dt);

            if (externalUiBlocked)
            {
                if (IsMindMenuOpen)
                {
                    CloseMindMenu(restoreCursorLock: false);
                }
                return;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // [T] (yalın): Zihin İnşa ve SOTF Çok-Malzemeli Modüler Yapı Menüsünü açar/kapatır ([Shift+T] Kristal fırça içindir!)
            if (Input.GetKeyDown(KeyCode.T) &&
                Stage1InputConflictPolicy.ResolveKeyT(new GameplayInputContextSnapshot
                {
                    ShiftHeld = shiftHeld,
                    CtrlHeld = ctrlHeld,
                    AltHeld = altHeld,
                    IsCrystalActive = miningCtrl.IsCrystalActive
                }) == ResolvedGameplayKeyAction.OpenMindAndSotfBuildMenu)
            {
                if (IsMindMenuOpen)
                {
                    CloseMindMenu(restoreCursorLock: true);
                }
                else
                {
                    if (miningCtrl.IsCrystalActive)
                    {
                        miningCtrl.IsCrystalActive = false;
                    }
                    OpenMindMenu();
                }
                return;
            }

            if (!IsMindMenuOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ConsumedEscapeThisFrame = true;
                CloseMindMenu(restoreCursorLock: true);
                return;
            }

            // Hizli klavye kisayollari: [1..9] dogrulama, [G] secili kartin 2.8 sn dusunce silueti ipucu
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.Campfire, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.CharcoalPit, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.BloomeryFurnace, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.WoodenWheelbarrow, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.Windmill, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.LogWall, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.LogPalisade, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha8))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.LogBeam, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.LogCabin, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                return;
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                ConsumedKeyGThisFrame = true;
                MindStructureBlueprintId bp = (MindStructureBlueprintId)Mathf.Clamp(_hoveredBlueprintIndex, 0, MindStructureClusterValidator.BlueprintCount - 1);
                SpawnThoughtSilhouetteHint(bp, cam, playerTransform, miningCtrl, worldChunks, sharedVoxelMat);
                CloseMindMenu(restoreCursorLock: true);
            }
        }

        public void DrawGui(
            bool showHud,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl = null,
            Stage1WheelbarrowController? wheelbarrowCtrl = null,
            Stage1WindmillController? windmillCtrl = null)
        {
            EnsureGuiStyles();

            if (showHud && _toastRemainingSec > 0.01f && !string.IsNullOrEmpty(_toastMessage))
            {
                DrawToastOverlay();
            }

            if (showHud && !IsMindMenuOpen &&
                looseBuildSystem.HasValidGhostPlacement &&
                !string.IsNullOrEmpty(looseBuildSystem.CurrentModularStatusText))
            {
                DrawModularStabilityHudBadge(looseBuildSystem);
            }

            if (!IsMindMenuOpen) return;

            float panelW = Mathf.Min(948f, Screen.width - 28f);
            float panelH = Mathf.Min(676f, Screen.height - 20f);
            float px = (Screen.width - panelW) * 0.5f;
            float py = (Screen.height - panelH) * 0.5f;

            Color prevColor = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.11f, 0.94f);
            GUI.Box(new Rect(px, py, panelW, panelH), GUIContent.none, _panelStyle!);
            GUI.color = prevColor;

            GUI.Label(new Rect(px + 18f, py + 10f, panelW - 36f, 24f),
                "🧠 ZİHİN İNŞA & SOTF ÇOK-MALZEMELİ YAPI MENÜSÜ  ([T] / [ESC] Kapat)", _titleStyle!);
            GUI.Label(new Rect(px + 18f, py + 34f, panelW - 36f, 20f),
                "Malzemeleri veya 2m kütükleri [Sol Tık] ile dizin -> [Sol Tık / 1..9] ile kümenizi doğrulayın!  |  [Sağ Tık / G]: 2.8 sn Zihin Silüeti İpucu",
                _subTitleStyle!);

            float cardStartY = py + 58f;
            float cardH = 68f;
            float cardGap = 6f;
            float colGap = 14f;
            float colW = (panelW - 36f - colGap) * 0.5f;
            Vector2 mousePos = Event.current.mousePosition;

            for (int i = 0; i < MindStructureClusterValidator.BlueprintCount; i++)
            {
                MindStructureBlueprintId bp = (MindStructureBlueprintId)i;
                int col = i < 5 ? 0 : 1;
                int row = i < 5 ? i : (i - 5);
                float cardX = px + 18f + col * (colW + colGap);
                Rect cardRect = new Rect(cardX, cardStartY + row * (cardH + cardGap), colW, cardH);
                bool hovered = cardRect.Contains(mousePos);
                if (hovered) _hoveredBlueprintIndex = i;

                GUI.color = hovered
                    ? new Color(0.18f, 0.30f, 0.42f, 0.96f)
                    : new Color(0.11f, 0.14f, 0.19f, 0.92f);
                GUI.Box(cardRect, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;

                string icon = bp switch
                {
                    MindStructureBlueprintId.Campfire => "🔥",
                    MindStructureBlueprintId.CharcoalPit => "🪵",
                    MindStructureBlueprintId.BloomeryFurnace => "🏺",
                    MindStructureBlueprintId.WoodenWheelbarrow => "🛞",
                    MindStructureBlueprintId.Windmill => "🌬️",
                    MindStructureBlueprintId.LogWall => "🧱",
                    MindStructureBlueprintId.LogPalisade => "🛡️",
                    MindStructureBlueprintId.LogBeam => "🏗️",
                    MindStructureBlueprintId.LogCabin => "🏠",
                    _ => "🪵"
                };

                string header = $"[{i + 1}] {icon} {MindStructureClusterValidator.GetDisplayName(bp)}";
                string desc = MindStructureClusterValidator.GetShortDescription(bp);

                GUI.Label(new Rect(cardRect.x + 12f, cardRect.y + 4f, cardRect.width - 155f, 20f), header, _cardTitleStyle!);
                GUI.Label(new Rect(cardRect.x + 12f, cardRect.y + 24f, cardRect.width - 24f, 40f), desc, _cardDescStyle!);

                GUI.Label(new Rect(cardRect.xMax - 148f, cardRect.y + 4f, 140f, 20f),
                    hovered ? "[Sol] Doğrula | [Sağ] İpucu" : $"[{i + 1}] Seç",
                    _subTitleStyle!);

                if (hovered && Event.current.type == EventType.MouseDown)
                {
                    if (Event.current.button == 0)
                    {
                        Event.current.Use();
                        ExecuteValidateAndBind(bp, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl, windmillCtrl);
                        return;
                    }
                    if (Event.current.button == 1)
                    {
                        Event.current.Use();
                        SpawnThoughtSilhouetteHint(bp, cam, playerTransform, miningCtrl, worldChunks, sharedVoxelMat);
                        CloseMindMenu(restoreCursorLock: true);
                        return;
                    }
                }
            }

            // SOTF & Valheim Çok-Malzemeli + Statik Taşıyıcılı 3B Modüler Yapı Seti
            float modHeaderY = cardStartY + 5f * (cardH + cardGap) + 4f;
            GUI.Label(
                new Rect(px + 18f, modHeaderY, panelW - 205f, 22f),
                "🏗️ SOTF & VALHEIM YAPI SETİ  ([Sol Tık] Katman Diz | [V] Parça | [G] Malzeme | [R] Döndür | [Shift+R] Kalınlık | [Sağ Tık/ESC/Q] İptal)",
                _titleStyle!);

            if (looseBuildSystem.IsModularBuildModeActive)
            {
                Rect stopRect = new Rect(px + panelW - 186f, modHeaderY, 168f, 22f);
                if (GUI.Button(stopRect, "İnşa Modunu Bırak [Q]"))
                {
                    looseBuildSystem.CancelActiveBuildPlacement(
                        miningCtrl: miningCtrl,
                        emptyHotbarSelectionIfHammer: false,
                        customFeedback: "🔨 İnşa modu kapatıldı — el boşaltıldı.");
                    CloseMindMenu(restoreCursorLock: true);
                    return;
                }
            }

            // 1) 6 Yapı Malzemesi Seçici Satırı (Ahşap Kütük -> Demir Ağacı -> Örme Taş -> Bazalt -> Mermer -> Çelik)
            float matRowY = modHeaderY + 25f;
            float matGap = 6f;
            float matBtnW = (panelW - 36f - matGap * (SotfStructuralBuildMechanics.MaterialKindCount - 1)) / SotfStructuralBuildMechanics.MaterialKindCount;
            float matBtnH = 28f;
            BuildStructuralMaterialKind activeMat = looseBuildSystem.ActiveStructuralMaterial;

            for (int mi = 0; mi < SotfStructuralBuildMechanics.MaterialKindCount; mi++)
            {
                BuildStructuralMaterialKind matKind = (BuildStructuralMaterialKind)mi;
                Rect matRect = new Rect(px + 18f + mi * (matBtnW + matGap), matRowY, matBtnW, matBtnH);
                bool matHov = matRect.Contains(mousePos);
                bool matSel = activeMat == matKind;

                GUI.color = matSel
                    ? new Color(0.20f, 0.46f, 0.30f, 0.96f)
                    : (matHov ? new Color(0.24f, 0.34f, 0.46f, 0.96f) : new Color(0.12f, 0.16f, 0.22f, 0.92f));
                GUI.Box(matRect, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;

                StructuralMaterialProfile mp = SotfStructuralBuildMechanics.GetMaterialProfile(matKind);
                GUI.Label(
                    new Rect(matRect.x + 6f, matRect.y + 4f, matRect.width - 12f, 20f),
                    $"{SotfStructuralBuildMechanics.GetMaterialShortBadge(matKind)} ({mp.MaxUnsupportedSpanMeters:0.#}m)",
                    _cardDescStyle!);

                if (matHov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Event.current.Use();
                    looseBuildSystem.SelectStructuralMaterial(matKind);
                    return;
                }
            }

            // 2) 3 Kesit Kalınlığı Seçici Satırı + Seçili Malzeme Mühendislik Özeti
            float thickRowY = matRowY + matBtnH + 5f;
            float thickBtnW = 196f;
            float thickBtnH = 26f;
            BuildSectionThickness activeThick = looseBuildSystem.ActiveSectionThickness;

            for (int ti = 0; ti < 3; ti++)
            {
                BuildSectionThickness tKind = (BuildSectionThickness)ti;
                Rect tRect = new Rect(px + 18f + ti * (thickBtnW + 6f), thickRowY, thickBtnW, thickBtnH);
                bool tHov = tRect.Contains(mousePos);
                bool tSel = activeThick == tKind;

                GUI.color = tSel
                    ? new Color(0.26f, 0.42f, 0.18f, 0.96f)
                    : (tHov ? new Color(0.22f, 0.32f, 0.44f, 0.96f) : new Color(0.11f, 0.15f, 0.20f, 0.92f));
                GUI.Box(tRect, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;

                GUI.Label(
                    new Rect(tRect.x + 8f, tRect.y + 3f, tRect.width - 16f, 20f),
                    SotfStructuralBuildMechanics.GetThicknessTurkishName(tKind),
                    _cardDescStyle!);

                if (tHov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Event.current.Use();
                    looseBuildSystem.SelectSectionThickness(tKind);
                    return;
                }
            }

            float statInfoX = px + 18f + 3f * (thickBtnW + 6f) + 6f;
            float effSpan = SotfStructuralBuildMechanics.ComputeEffectiveMaxSpanMeters(
                ModularBuildPieceKind.PlankFloor2x2m, activeMat, activeThick, hasBeamUnderneath: false, hasDiagonalBrace: false);
            float effCapKn = SotfStructuralBuildMechanics.ComputePieceCompressionCapacityKn(
                ModularBuildPieceKind.LogPillar3m, activeMat, activeThick);
            GUI.Label(
                new Rect(statInfoX, thickRowY + 3f, px + panelW - 18f - statInfoX, 20f),
                $"Açıklık: {effSpan:0.1}m | Kolon Kapasite: {effCapKn:0} kN",
                _subTitleStyle!);

            // 3) 12 Modüler Yapı Parçası Izgarası
            float modGridY = thickRowY + thickBtnH + 6f;
            float modGapX = 8f;
            float modGapY = 5f;
            float modBtnW = (panelW - 36f - modGapX * 3f) / 4f;
            float modBtnH = 36f;

            for (int m = 0; m < ValheimSotfModularGraphSolver.ModularPieceCount; m++)
            {
                ModularBuildPieceKind mKind = (ModularBuildPieceKind)m;
                int mCol = m % 4;
                int mRow = m / 4;
                Rect mRect = new Rect(
                    px + 18f + mCol * (modBtnW + modGapX),
                    modGridY + mRow * (modBtnH + modGapY),
                    modBtnW,
                    modBtnH);
                bool mHov = mRect.Contains(mousePos);
                bool mSelected = looseBuildSystem.IsModularBuildModeActive && looseBuildSystem.ActiveModularPieceKind == mKind;

                GUI.color = mSelected
                    ? new Color(0.16f, 0.44f, 0.28f, 0.96f)
                    : (mHov ? new Color(0.22f, 0.34f, 0.48f, 0.96f) : new Color(0.11f, 0.15f, 0.21f, 0.92f));
                GUI.Box(mRect, GUIContent.none, _panelStyle!);
                GUI.color = prevColor;

                int reqCourses = SotfStructuralBuildMechanics.GetDefaultRequiredCourses(mKind);
                string courseTag = reqCourses > 1 ? $" ({reqCourses} Sıra)" : string.Empty;
                GUI.Label(
                    new Rect(mRect.x + 8f, mRect.y + 3f, mRect.width - 16f, mRect.height - 6f),
                    ValheimSotfModularGraphSolver.GetPieceTurkishDisplayName(mKind) + courseTag,
                    _cardDescStyle!);

                if (mHov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    Event.current.Use();
                    looseBuildSystem.SelectModularPieceForPlacement(mKind, enableModularMode: true);
                    string chosen = ValheimSotfModularGraphSolver.GetPieceTurkishDisplayName(mKind);
                    string matLabel = SotfStructuralBuildMechanics.GetMaterialTurkishName(looseBuildSystem.ActiveStructuralMaterial);
                    string msg = $"🏗️ {chosen} [{matLabel}] seçildi! ([Sol Tık] Katman Diz | [V] Parça | [G] Malzeme | [R] Döndür | [Sağ Tık/ESC/Q] Bırak)";
                    miningCtrl.LastFeedbackText = msg;
                    ShowToast(msg, isSuccess: true, durationSec: 3.2f);
                    CloseMindMenu(restoreCursorLock: true);
                    return;
                }
            }
        }

        public void ClearAll()
        {
            CloseMindMenu(restoreCursorLock: false);
            _registeredNonCampfireStations.Clear();
            _thoughtHintRemainingSec = 0f;
            if (_thoughtSilhouetteGo != null)
            {
                _thoughtSilhouetteGo.SetActive(false);
            }
        }

        private void OpenMindMenu()
        {
            IsMindMenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseMindMenu(bool restoreCursorLock)
        {
            IsMindMenuOpen = false;
            if (restoreCursorLock)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void ExecuteValidateAndBind(
            MindStructureBlueprintId blueprint,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1CampfireAndCookingController campfireCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            Stage1BloomeryAndForgingController? bloomeryCtrl = null,
            Stage1WheelbarrowController? wheelbarrowCtrl = null,
            Stage1WindmillController? windmillCtrl = null)
        {
            CloseMindMenu(restoreCursorLock: true);

            Vector3 aimOrigin = ResolveClusterAimOrigin(cam, playerTransform, miningCtrl, looseBuildSystem, worldChunks);
            ReadOnlySpan<LoosePartPoint> cloud = looseBuildSystem.SnapshotPointCloud();
            Span<int> matchedIndices = stackalloc int[64];

            MindValidationResult res = MindStructureClusterValidator.ValidateCluster(
                blueprint,
                new SysVec3(aimOrigin.x, aimOrigin.y, aimOrigin.z),
                cloud,
                matchedIndices);

            miningCtrl.LastFeedbackText = res.FeedbackMessage;
            ShowToast(res.FeedbackMessage, res.IsValid, res.IsValid ? 3.8f : 3.4f);

            if (!res.IsValid)
            {
                return;
            }

            uint newStationId = ++_nextStationId;
            int bindCount = Mathf.Min(res.MatchedPartCount, matchedIndices.Length);
            looseBuildSystem.BindPartsToStation(matchedIndices.Slice(0, bindCount), newStationId);

            // 0.75 sn egilip ellerle son duzeltme dokunusu! (Oyuncunun koydugu tas/dal mesh'leri OLDUGU GIBI kalir!)
            _finishingCrouchTimer = FinishingTouchCrouchDurationSec;

            if (blueprint == MindStructureBlueprintId.Campfire)
            {
                campfireCtrl.RegisterUnlitCampfire(newStationId, in res, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.CharcoalPit && bloomeryCtrl != null)
            {
                bloomeryCtrl.RegisterCharcoalPit(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.BloomeryFurnace && bloomeryCtrl != null)
            {
                bloomeryCtrl.RegisterBloomeryFurnace(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.WoodenWheelbarrow && wheelbarrowCtrl != null)
            {
                wheelbarrowCtrl.RegisterWheelbarrowStation(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else if (blueprint == MindStructureBlueprintId.Windmill && windmillCtrl != null)
            {
                windmillCtrl.RegisterWindmillStation(newStationId, in res, looseBuildSystem, sharedVoxelMat);
            }
            else
            {
                _registeredNonCampfireStations.Add(new RegisteredMindStationInfo
                {
                    StationId = newStationId,
                    Blueprint = blueprint,
                    Center = new Vector3(res.ClusterCenter.X, res.ClusterBaseY, res.ClusterCenter.Z),
                    BaseY = res.ClusterBaseY,
                    BonusEfficiency01 = res.BonusEfficiency01
                });
            }
        }

        private static Vector3 ResolveClusterAimOrigin(
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            WorldLooseBuildSystem looseBuildSystem,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            Vector3 playerPos = playerTransform.position;
            Vector3 rayOrigin = cam != null ? cam.transform.position : playerPos + new Vector3(0f, 1.52f, 0f);
            Vector3 rayDir = cam != null && cam.transform.forward.sqrMagnitude > 0.001f
                ? cam.transform.forward.normalized
                : playerTransform.forward;

            // 1) Kamera isini boyunca en yakin etiketlenmemis serbest parca varsa onun etrafini merkez al
            float bestScore = float.MaxValue;
            Vector3 bestPartPos = Vector3.zero;
            bool foundPartNearRay = false;

            var parts = looseBuildSystem.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.BoundStationId != 0u || p.VisualGo == null) continue;
                if (Vector3.Distance(playerPos + Vector3.up * 0.8f, p.Position) > 3.65f) continue;

                Vector3 toP = p.Position - rayOrigin;
                float along = Vector3.Dot(toP, rayDir);
                if (along < -0.25f || along > 4.10f) continue;

                Vector3 closest = rayOrigin + rayDir * Mathf.Max(0f, along);
                float perp = Vector3.Distance(closest, p.Position);
                if (perp > 1.35f) continue;

                float score = perp * 2.0f + Mathf.Max(0f, along) * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPartPos = p.Position;
                    foundPartNearRay = true;
                }
            }

            if (foundPartNearRay)
            {
                return bestPartPos;
            }

            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 3.65f)
            {
                return miningCtrl.AimHit.point;
            }

            Vector3 fwdFlat = playerTransform.forward;
            fwdFlat.y = 0f;
            fwdFlat = fwdFlat.sqrMagnitude > 0.001f ? fwdFlat.normalized : Vector3.forward;
            Vector3 fallback = playerPos + fwdFlat * 1.45f;
            if (worldChunks != null &&
                VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    fallback.x, fallback.z, playerPos.y, 1.9f, worldChunks, out float gy, out _))
            {
                fallback.y = gy;
            }
            return fallback;
        }

        private void SpawnThoughtSilhouetteHint(
            MindStructureBlueprintId blueprint,
            UnityEngine.Camera? cam,
            Transform playerTransform,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat)
        {
            EnsureSilhouetteContainerAndMesh(blueprint);

            Vector3 pos;
            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= 3.5f)
            {
                pos = miningCtrl.AimHit.point;
            }
            else
            {
                Vector3 fwd = cam != null ? cam.transform.forward : playerTransform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                pos = playerTransform.position + fwd * 1.55f;
                if (worldChunks != null &&
                    VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                        pos.x, pos.z, playerTransform.position.y, 1.9f, worldChunks, out float gy, out _))
                {
                    pos.y = gy;
                }
            }

            if (_thoughtSilhouetteGo == null)
            {
                _thoughtSilhouetteGo = new GameObject("MindThoughtHintSilhouette_Temporary");
                if (_silhouetteParent != null) _thoughtSilhouetteGo.transform.SetParent(_silhouetteParent, false);
                _thoughtSilhouetteMf = _thoughtSilhouetteGo.AddComponent<MeshFilter>();
                _thoughtSilhouetteMr = _thoughtSilhouetteGo.AddComponent<MeshRenderer>();
                if (_thoughtSilhouetteMr != null)
                {
                    _thoughtSilhouetteMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _thoughtSilhouetteMr.receiveShadows = false;
                }
            }

            if (_thoughtSilhouetteMr != null && sharedVoxelMat != null)
            {
                _thoughtSilhouetteMr.sharedMaterial = sharedVoxelMat;
            }
            if (_thoughtSilhouetteMf != null)
            {
                _thoughtSilhouetteMf.sharedMesh = _silhouetteMeshes[(int)blueprint];
            }

            Vector3 lookFwd = playerTransform.forward;
            lookFwd.y = 0f;
            if (lookFwd.sqrMagnitude < 0.001f) lookFwd = Vector3.forward;

            _thoughtSilhouetteGo.transform.position = pos + new Vector3(0f, 0.02f, 0f);
            _thoughtSilhouetteGo.transform.rotation = Quaternion.LookRotation(lookFwd.normalized, Vector3.up);
            _thoughtSilhouetteGo.transform.localScale = Vector3.one;
            _thoughtSilhouetteGo.SetActive(true);

            _thoughtHintRemainingSec = ThoughtSilhouetteDurationSec;
            string msg = $"💭 {MindStructureClusterValidator.GetDisplayName(blueprint)} zihin silüeti canlandırıldı (2.8 sn sonra kendiliğinden silinir — parçaları ellerinle diz!).";
            miningCtrl.LastFeedbackText = msg;
            ShowToast(msg, isSuccess: true, durationSec: 2.8f);
        }

        private void StepThoughtSilhouette(float dt)
        {
            if (_thoughtHintRemainingSec <= 0f || _thoughtSilhouetteGo == null) return;

            _thoughtHintRemainingSec -= dt;
            if (_thoughtHintRemainingSec <= 0f)
            {
                _thoughtHintRemainingSec = 0f;
                _thoughtSilhouetteGo.SetActive(false);
                return;
            }

            // Son 0.55 saniyede yumusakca kuculerek yok ol (0 kaplama VRAM, 0 ek shader!)
            float fadeScale = _thoughtHintRemainingSec < 0.55f
                ? Mathf.Clamp01(_thoughtHintRemainingSec / 0.55f)
                : 1.0f + 0.025f * Mathf.Sin(Time.time * 7.5f);
            _thoughtSilhouetteGo.transform.localScale = new Vector3(fadeScale, fadeScale, fadeScale);
        }

        private void EnsureSilhouetteContainerAndMesh(MindStructureBlueprintId blueprint)
        {
            if (_silhouetteParent == null)
            {
                GameObject existing = GameObject.Find("06_Faz23_WorldLooseBuildRoot");
                if (existing == null) existing = new GameObject("06_Faz23_WorldLooseBuildRoot");
                _silhouetteParent = existing.transform;
            }

            int idx = (int)blueprint;
            if (_silhouetteMeshes[idx] != null) return;

            _silhouetteTris.Clear();
            // DOK2 B3 & DOK5 §2.2: Mavi tebesir cizgisi yerine dogal kirectasi & zumrut yesili hacimli kilavuz silueti
            byte guidePrimary = MaterialIdCatalog.EmeraldGem;
            byte guideAccent = MaterialIdCatalog.Limestone;
            byte logGuideMat = MaterialIdCatalog.OakWood;

            switch (blueprint)
            {
                case MindStructureBlueprintId.Campfire:
                    // Alt kav halkasi + 5 konik dal cizgisi + dis tas cember noktalari
                    EmitRingStrips(new SysVec3(0f, 0.02f, 0f), 0.16f, 8, 0.022f, guideAccent, 0, _silhouetteTris);
                    for (int s = 0; s < 5; s++)
                    {
                        float a = (s / 5f) * MathF.PI * 2f;
                        SysVec3 p0 = new SysVec3(MathF.Cos(a) * 0.24f, 0.02f, MathF.Sin(a) * 0.24f);
                        SysVec3 p1 = new SysVec3(MathF.Cos(a) * 0.03f, 0.34f, MathF.Sin(a) * 0.03f);
                        EmitThinLineBar(p0, p1, 0.024f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitRingStrips(new SysVec3(0f, 0.04f, 0f), 0.42f, 8, 0.026f, guidePrimary, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.CharcoalPit:
                    // Ic odun yigini + dis kubbe toprak kaplama halkalari
                    EmitRingStrips(new SysVec3(0f, 0.05f, 0f), 0.48f, 10, 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.24f, 0f), 0.38f, 8, 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.42f, 0f), 0.22f, 6, 0.026f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.BloomeryFurnace:
                    // Silindirik 1m baca + alt yan tuyere hava borusu cizgisi
                    EmitRingStrips(new SysVec3(0f, 0.06f, 0f), 0.38f, 10, 0.028f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.48f, 0f), 0.34f, 10, 0.028f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.92f, 0f), 0.29f, 10, 0.028f, guideAccent, 0, _silhouetteTris);
                    for (int v = 0; v < 4; v++)
                    {
                        float a = (v / 4f) * MathF.PI * 2f;
                        EmitThinLineBar(
                            new SysVec3(MathF.Cos(a) * 0.38f, 0.06f, MathF.Sin(a) * 0.38f),
                            new SysVec3(MathF.Cos(a) * 0.29f, 0.92f, MathF.Sin(a) * 0.29f),
                            0.026f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(0.32f, 0.14f, 0f), new SysVec3(0.58f, 0.14f, 0f), 0.036f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.WoodenWheelbarrow:
                    // On tekerlek cemberi + V kollar + tekne cercevesi
                    for (int w = 0; w < 10; w++)
                    {
                        float a0 = (w / 10f) * MathF.PI * 2f;
                        float a1 = ((w + 1) / 10f) * MathF.PI * 2f;
                        EmitThinLineBar(
                            new SysVec3(0f, 0.22f + MathF.Sin(a0) * 0.20f, 0.46f + MathF.Cos(a0) * 0.20f),
                            new SysVec3(0f, 0.22f + MathF.Sin(a1) * 0.20f, 0.46f + MathF.Cos(a1) * 0.20f),
                            0.026f, guideAccent, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.08f, 0.22f, 0.46f), new SysVec3(-0.30f, 0.46f, -0.55f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.08f, 0.22f, 0.46f), new SysVec3(0.30f, 0.46f, -0.55f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-0.24f, 0.36f, 0.22f), new SysVec3(0.24f, 0.36f, 0.22f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-0.26f, 0.40f, -0.25f), new SysVec3(0.26f, 0.40f, -0.25f), 0.026f, guidePrimary, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.Windmill:
                    // Sekizgen tas kaide halkasi + 4 dikey kule diregi + ust 4 yelken kanat arti cizgisi
                    EmitRingStrips(new SysVec3(0f, 0.08f, 0f), 0.88f, 8, 0.030f, guidePrimary, 0, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.85f, 0f), 0.56f, 8, 0.028f, guideAccent, 0, _silhouetteTris);
                    for (int p = 0; p < 4; p++)
                    {
                        float a = (p / 4f) * MathF.PI * 2f + (MathF.PI * 0.25f);
                        EmitThinLineBar(
                            new SysVec3(MathF.Cos(a) * 0.78f, 0.08f, MathF.Sin(a) * 0.78f),
                            new SysVec3(MathF.Cos(a) * 0.36f, 2.80f, MathF.Sin(a) * 0.36f),
                            0.030f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-1.65f, 2.85f, 0.42f), new SysVec3(1.65f, 2.85f, 0.42f), 0.034f, guideAccent, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0f, 1.20f, 0.42f), new SysVec3(0f, 4.50f, 0.42f), 0.034f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogWall:
                    // 3 katmanli gercek hacimli 2m yatay kutuk duvar kilavuzu (0.42m sira adimi + iki ucta dikme)
                    for (int row = 0; row < 3; row++)
                    {
                        float y = 0.22f + row * SotfStructuralBuildMechanics.TimberLogCourseStepYMeters;
                        byte mat = row == 2 ? guideAccent : logGuideMat;
                        EmitThinLineBar(new SysVec3(-0.98f, y, 0f), new SysVec3(0.98f, y, 0f), 0.065f, mat, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.98f, 0.05f, 0f), new SysVec3(-0.98f, 1.45f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.98f, 0.05f, 0f), new SysVec3(0.98f, 1.45f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogPalisade:
                    // Yan yana 4 dikey 2m kazik cit silueti + yatay kusak
                    for (int s = 0; s < 4; s++)
                    {
                        float x = -0.60f + s * 0.40f;
                        EmitThinLineBar(new SysVec3(x, 0.05f, 0f), new SysVec3(x, 1.95f, 0f), 0.060f, logGuideMat, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.72f, 1.45f, 0f), new SysVec3(0.72f, 1.45f, 0f), 0.045f, guideAccent, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogBeam:
                    // 2 tasiyici dikme + ustte 2m yatay cati kirisi
                    EmitThinLineBar(new SysVec3(-0.88f, 0.05f, 0f), new SysVec3(-0.88f, 1.88f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.88f, 0.05f, 0f), new SysVec3(0.88f, 1.88f, 0f), 0.055f, guidePrimary, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-1.02f, 1.92f, 0f), new SysVec3(1.02f, 1.92f, 0f), 0.065f, logGuideMat, 0, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.LogCabin:
                    // 4 kose dikme + kutuk duvar cerceveleri + egimli besik cati kirisleri
                    for (int c = 0; c < 4; c++)
                    {
                        float cx = (c & 1) == 0 ? -1.0f : 1.0f;
                        float cz = (c & 2) == 0 ? -1.0f : 1.0f;
                        EmitThinLineBar(new SysVec3(cx, 0.05f, cz), new SysVec3(cx, 1.75f, cz), 0.055f, guidePrimary, 0, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-1.0f, 1.75f, -1.0f), new SysVec3(0f, 2.45f, -1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(1.0f, 1.75f, -1.0f), new SysVec3(0f, 2.45f, -1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-1.0f, 1.75f, 1.0f), new SysVec3(0f, 2.45f, 1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(1.0f, 1.75f, 1.0f), new SysVec3(0f, 2.45f, 1.0f), 0.050f, logGuideMat, 0, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0f, 2.45f, -1.05f), new SysVec3(0f, 2.45f, 1.05f), 0.060f, guideAccent, 0, _silhouetteTris);
                    break;
            }

            _silhouetteMeshes[idx] = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                _silhouetteTris, $"Mesh_MindThoughtSilhouette_{blueprint}");
        }

        private static void EmitRingStrips(
            SysVec3 center,
            float radius,
            int segments,
            float thickness,
            byte matId,
            byte tempByte,
            List<FacetedVoxelTriangle> outTris)
        {
            for (int i = 0; i < segments; i++)
            {
                if ((i & 1) == 1 && segments >= 8)
                {
                    // Kesikli tebesir cizgisi hissi icin ceyrek bosluk birak
                    float a0 = (i / (float)segments) * MathF.PI * 2f;
                    float aMid = ((i + 0.68f) / segments) * MathF.PI * 2f;
                    EmitThinLineBar(
                        center + new SysVec3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius),
                        center + new SysVec3(MathF.Cos(aMid) * radius, 0f, MathF.Sin(aMid) * radius),
                        thickness, matId, tempByte, outTris);
                }
                else
                {
                    float a0 = (i / (float)segments) * MathF.PI * 2f;
                    float a1 = ((i + 0.75f) / segments) * MathF.PI * 2f;
                    EmitThinLineBar(
                        center + new SysVec3(MathF.Cos(a0) * radius, 0f, MathF.Sin(a0) * radius),
                        center + new SysVec3(MathF.Cos(a1) * radius, 0f, MathF.Sin(a1) * radius),
                        thickness, matId, tempByte, outTris);
                }
            }
        }

        private static void EmitThinLineBar(
            SysVec3 p0,
            SysVec3 p1,
            float halfWidth,
            byte matId,
            byte tempByte,
            List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 dir = p1 - p0;
            float len = dir.Length();
            if (len < 0.001f) return;
            SysVec3 f = dir / len;
            SysVec3 side = SysVec3.Cross(f, MathF.Abs(f.Y) < 0.9f ? SysVec3.UnitY : SysVec3.UnitX);
            side = SysVec3.Normalize(side) * halfWidth;
            SysVec3 up = SysVec3.Normalize(SysVec3.Cross(side, f)) * halfWidth;

            SysVec3 a0 = p0 - side, b0 = p0 + side, c0 = p0 + up;
            SysVec3 a1 = p1 - side, b1 = p1 + side, c1 = p1 + up;

            AddQuad(a0, b0, b1, a1, -up, matId, tempByte, outTris);
            AddQuad(b0, c0, c1, b1, side + up, matId, tempByte, outTris);
            AddQuad(c0, a0, a1, c1, -side + up, matId, tempByte, outTris);
        }

        private static void AddQuad(
            SysVec3 v0, SysVec3 v1, SysVec3 v2, SysVec3 v3,
            SysVec3 approxNormal, byte matId, byte tempByte, List<FacetedVoxelTriangle> outTris)
        {
            SysVec3 n = approxNormal.LengthSquared() > 1e-6f ? SysVec3.Normalize(approxNormal) : SysVec3.UnitY;
            outTris.Add(new FacetedVoxelTriangle
            {
                V0 = v0, V1 = v1, V2 = v2, Normal = n,
                MaterialId = matId, Temperature = tempByte, FacetShade = 230, AmbientOcclusion = 0xE2
            });
            outTris.Add(new FacetedVoxelTriangle
            {
                V0 = v0, V1 = v2, V2 = v3, Normal = n,
                MaterialId = matId, Temperature = tempByte, FacetShade = 222, AmbientOcclusion = 0xE2
            });
        }

        private void ShowToast(string message, bool isSuccess, float durationSec)
        {
            _toastMessage = message;
            _toastIsSuccess = isSuccess;
            _toastRemainingSec = durationSec;
        }

        private void DrawToastOverlay()
        {
            float w = Mathf.Min(680f, Screen.width - 40f);
            float h = 44f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.16f;

            Color prev = GUI.color;
            GUI.color = _toastIsSuccess
                ? new Color(0.08f, 0.24f, 0.15f, 0.90f)
                : new Color(0.28f, 0.11f, 0.08f, 0.92f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _toastBoxStyle!);
            GUI.color = prev;

            GUI.Label(new Rect(x + 12f, y + 6f, w - 24f, h - 12f), _toastMessage, _toastTextStyle!);
        }

        private void DrawModularStabilityHudBadge(WorldLooseBuildSystem looseBuildSystem)
        {
            float w = Mathf.Min(720f, Screen.width - 40f);
            float h = 46f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - 132f;

            Color32 c32 = looseBuildSystem.CurrentGhostTintColor;
            Color prev = GUI.color;
            GUI.color = new Color(0.07f, 0.09f, 0.12f, 0.88f);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, _toastBoxStyle!);

            float stab01 = Mathf.Clamp01(looseBuildSystem.CurrentGhostStability01);
            GUI.color = new Color(c32.r / 255f, c32.g / 255f, c32.b / 255f, 0.92f);
            GUI.Box(new Rect(x + 6f, y + h - 9f, (w - 12f) * Mathf.Max(0.04f, stab01), 5f), GUIContent.none, _toastBoxStyle!);
            GUI.color = prev;

            GUI.Label(new Rect(x + 12f, y + 4f, w - 24f, h - 14f), looseBuildSystem.CurrentModularStatusText, _toastTextStyle!);
        }

        private void EnsureGuiStyles()
        {
            if (_panelStyle != null) return;
            _panelStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.88f, 0.96f, 1.0f) }
            };
            _subTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.72f, 0.85f, 0.95f) }
            };
            _cardTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.0f, 0.92f, 0.72f) }
            };
            _cardDescStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.90f, 0.93f, 0.96f) }
            };
            _toastBoxStyle = new GUIStyle(GUI.skin.box);
            _toastTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
        }
    }
}
#endif
