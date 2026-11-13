#nullable enable
using System;
using Stratocraft.Inventory.Systems;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 2 [B] Hayatta Kalma Tarif ve Malzeme Rehber Kitabi — Fiziksel Acik Iki Sayfali Deri Kitap IMGUI Arayuzu.
    /// Tum GUIStyle, GUIContent ve Color nesneleri EnsureStyles() icinde tek sefer onbelleklenir;
    /// DrawOnGui icinde GUI.color daima Color.white tutulur ve tum dikdortgenler DrawSolidRect ile cizilerek
    /// kare basina 0 B GC tahsisi ve tam okunabilir yuksek kontrastli tipografi saglanir.
    /// </summary>
    public sealed class Stage1SurvivalRecipeBookUi
    {
        public const int EntriesPerPage = 2;
        public const int EntriesPerSpread = EntriesPerPage * 2;
        private const int MaxCachedPageNumbers = 64;

        public bool IsOpen { get; private set; }
        public int SelectedTabIndex { get; private set; }
        public int ActiveTabIndex => SelectedTabIndex;
        public int CurrentSpreadIndex { get; private set; }

        public SurvivalRecipeBookTab ActiveTab =>
            (SurvivalRecipeBookTab)SurvivalRecipeBookCatalog.ClampTabIndex(SelectedTabIndex);

        public int TotalSpreadsInActiveTab
        {
            get
            {
                int activeTab = SurvivalRecipeBookCatalog.ClampTabIndex(SelectedTabIndex);
                int entryCount = SurvivalRecipeBookCatalog.GetEntriesForTab(activeTab).Length;
                return Math.Max(1, (entryCount + EntriesPerSpread - 1) / EntriesPerSpread);
            }
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close(restoreCursorLock: true);
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            IsOpen = true;
            CurrentSpreadIndex = Math.Clamp(CurrentSpreadIndex, 0, TotalSpreadsInActiveTab - 1);
#if UNITY_5_3_OR_NEWER
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
#endif
        }

        public void Close() => Close(restoreCursorLock: true);

        public void Close(bool restoreCursorLock)
        {
            if (!IsOpen) return;
            IsOpen = false;
#if UNITY_5_3_OR_NEWER
            if (restoreCursorLock)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
#endif
        }

        public void SelectTab(int tabIndex)
        {
            int clamped = SurvivalRecipeBookCatalog.ClampTabIndex(tabIndex);
            SelectedTabIndex = clamped;
            CurrentSpreadIndex = 0;
#if UNITY_5_3_OR_NEWER
            _leftPageScroll = Vector2.zero;
            _rightPageScroll = Vector2.zero;
#endif
        }

        public void SelectTab(SurvivalRecipeBookTab tab) => SelectTab((int)tab);

        public void NextSpread()
        {
            int maxSpread = Math.Max(0, TotalSpreadsInActiveTab - 1);
            if (CurrentSpreadIndex < maxSpread)
            {
                CurrentSpreadIndex++;
#if UNITY_5_3_OR_NEWER
                _leftPageScroll = Vector2.zero;
                _rightPageScroll = Vector2.zero;
#endif
            }
        }

        public void PreviousSpread()
        {
            if (CurrentSpreadIndex > 0)
            {
                CurrentSpreadIndex--;
#if UNITY_5_3_OR_NEWER
                _leftPageScroll = Vector2.zero;
                _rightPageScroll = Vector2.zero;
#endif
            }
        }

#if UNITY_5_3_OR_NEWER
        private sealed class CachedEntryGuiContent
        {
            public readonly string Id;
            public readonly GUIContent BadgeContent;
            public readonly GUIContent TitleContent;
            public readonly GUIContent SourceLabelContent;
            public readonly GUIContent ToolKeyContent;
            public readonly GUIContent GuideBodyContent;
            public readonly float FallbackCardHeight;
            public GUIContent[]? IngredientCountContents;

            public CachedEntryGuiContent(in SurvivalRecipeBookEntry entry)
            {
                Id = entry.Id;
                BadgeContent = new GUIContent(entry.CategoryBadge);
                TitleContent = new GUIContent(entry.Title);
                SourceLabelContent = new GUIContent($"Kaynak / Gereksinim:  {entry.IngredientsOrSource}");
                ToolKeyContent = new GUIContent($"Alet / Tuş & Yöntem:  {entry.ToolOrKeybind}");
                GuideBodyContent = new GUIContent(entry.StepByStepGuide);

                int guideLines = Mathf.Max(3, Mathf.CeilToInt(entry.StepByStepGuide.Length / 68f));
                FallbackCardHeight = 104f + guideLines * 18f;
            }
        }

        // 1) Arka plan perdesi ve dis deri kapak renk paleti
        private static readonly Color ColBackdropDim = new(0f, 0f, 0f, 0.58f);
        private static readonly Color ColLeatherOuterBorder = new(0x1D / 255f, 0x10 / 255f, 0x08 / 255f, 1f); // #1D1008
        private static readonly Color ColLeatherFill = new(0x38 / 255f, 0x21 / 255f, 0x12 / 255f, 1f);        // #382112
        private static readonly Color ColLeatherStitch = new(0x6B / 255f, 0x47 / 255f, 0x28 / 255f, 1f);      // #6B4728
        private static readonly Color ColBrassCorner = new(0xC8 / 255f, 0x9B / 255f, 0x4F / 255f, 1f);        // #C89B4F
        private static readonly Color ColBrassCornerShadow = new(0x7A / 255f, 0x5A / 255f, 0x26 / 255f, 1f);

        // 2) 3 kademeli kagit yigini derinligi ve parsomen yapraklar
        private static readonly Color ColPageStack1 = new(0xBC / 255f, 0xA7 / 255f, 0x82 / 255f, 1f);         // #BCA782
        private static readonly Color ColPageStack2 = new(0xD6 / 255f, 0xC4 / 255f, 0xA0 / 255f, 1f);         // #D6C4A0
        private static readonly Color ColPageStack3 = new(0xEF / 255f, 0xE3 / 255f, 0xC6 / 255f, 1f);         // #EFE3C6
        private static readonly Color ColParchmentPage = new(0xF4 / 255f, 0xEA / 255f, 0xD2 / 255f, 1f);      // #F4EAD2
        private static readonly Color ColPageRuledLine = new(0xD8 / 255f, 0xC7 / 255f, 0xA6 / 255f, 1f);      // #D8C7A6

        // 3) Orta dikey cilt/sirt olugu ve kirmizi ipek ayirac kurdelesi
        private static readonly Color ColSpineShadowOuter = new(0xB9 / 255f, 0xA3 / 255f, 0x7E / 255f, 1f);   // #B9A37E
        private static readonly Color ColSpineCreaseInner = new(0x8A / 255f, 0x71 / 255f, 0x50 / 255f, 1f);   // #8A7150
        private static readonly Color ColSilkBookmarkRibbon = new(0x8F / 255f, 0x1D / 255f, 0x1D / 255f, 1f); // #8F1D1D
        private static readonly Color ColSilkRibbonHighlight = new(0xB8 / 255f, 0x32 / 255f, 0x32 / 255f, 1f);

        // 4) Ustten disari tasan 4 bolum ayiraci sekmesi
        private static readonly Color ColTabActiveFill = new(0xF8 / 255f, 0xEE / 255f, 0xD8 / 255f, 1f);      // #F8EED8
        private static readonly Color ColTabActiveGoldTop = new(0xD4 / 255f, 0x9B / 255f, 0x35 / 255f, 1f);   // #D49B35
        private static readonly Color ColTabActiveText = new(0x1A / 255f, 0x11 / 255f, 0x0A / 255f, 1f);      // #1A110A
        private static readonly Color ColTabInactiveFill = new(0xD8 / 255f, 0xC3 / 255f, 0x9E / 255f, 1f);    // #D8C39E
        private static readonly Color ColTabInactiveBorder = new(0x6E / 255f, 0x55 / 255f, 0x36 / 255f, 1f);  // #6E5536
        private static readonly Color ColTabInactiveText = new(0x24 / 255f, 0x17 / 255f, 0x0D / 255f, 1f);    // #24170D

        // 5) Sag ust kirmizi kurdele Kapat butonu
        private static readonly Color ColCloseRibbonFill = new(0x8E / 255f, 0x1F / 255f, 0x1F / 255f, 1f);    // #8E1F1F
        private static readonly Color ColCloseRibbonBorder = new(0xDF / 255f, 0xB1 / 255f, 0x5B / 255f, 1f);  // #DFB15B
        private static readonly Color ColCloseRibbonText = new(0xFF / 255f, 0xF8 / 255f, 0xEB / 255f, 1f);    // #FFF8EB

        // 6) Kartlar ve yuksek kontrast tipografi
        private static readonly Color ColCardFill = new(0xFA / 255f, 0xF2 / 255f, 0xDF / 255f, 1f);           // #FAF2DF
        private static readonly Color ColCardBorder = new(0xC9 / 255f, 0xB5 / 255f, 0x90 / 255f, 1f);         // #C9B590
        private static readonly Color ColBadgeFill = new(0x6A / 255f, 0x24 / 255f, 0x16 / 255f, 1f);          // #6A2416
        private static readonly Color ColBadgeText = new(0xFF / 255f, 0xF7 / 255f, 0xE8 / 255f, 1f);          // #FFF7E8
        private static readonly Color ColEntryTitleText = new(0x18 / 255f, 0x10 / 255f, 0x09 / 255f, 1f);     // #181009
        private static readonly Color ColSourceText = new(0x14 / 255f, 0x47 / 255f, 0x26 / 255f, 1f);         // #144726
        private static readonly Color ColToolKeyText = new(0x15 / 255f, 0x34 / 255f, 0x62 / 255f, 1f);        // #153462
        private static readonly Color ColGuideBodyText = new(0x2A / 255f, 0x1E / 255f, 0x14 / 255f, 1f);      // #2A1E14

        // 7) Sayfa baslik, altlik ve yaprak cevirme buton renkleri
        private static readonly Color ColPageHeaderRunningText = new(0x5C / 255f, 0x3D / 255f, 0x22 / 255f, 1f);
        private static readonly Color ColPageSectionTitleText = new(0x1A / 255f, 0x11 / 255f, 0x0A / 255f, 1f);
        private static readonly Color ColPageSubtitleText = new(0x3B / 255f, 0x28 / 255f, 0x18 / 255f, 1f);
        private static readonly Color ColPageNumberText = new(0x4A / 255f, 0x33 / 255f, 0x1E / 255f, 1f);
        private static readonly Color ColNavBtnActiveFill = new(0xE6 / 255f, 0xD4 / 255f, 0xB3 / 255f, 1f);
        private static readonly Color ColNavBtnActiveBorder = new(0x7D / 255f, 0x5E / 255f, 0x3B / 255f, 1f);
        private static readonly Color ColNavBtnDisabledFill = new(0xEC / 255f, 0xE0 / 255f, 0xC8 / 255f, 1f);
        private static readonly Color ColNavBtnDisabledBorder = new(0xC9 / 255f, 0xB8 / 255f, 0x9A / 255f, 1f);
        private static readonly Color ColNavBtnDisabledText = new(0x8C / 255f, 0x78 / 255f, 0x5E / 255f, 1f);

        private Vector2 _leftPageScroll;
        private Vector2 _rightPageScroll;
        private bool _stylesInitialized;

        private GUIStyle? _runningHeaderStyle;
        private GUIStyle? _pageTitleStyle;
        private GUIStyle? _subtitleStyle;
        private GUIStyle? _activeTabLabelStyle;
        private GUIStyle? _inactiveTabLabelStyle;
        private GUIStyle? _closeButtonLabelStyle;
        private GUIStyle? _badgeStyle;
        private GUIStyle? _entryTitleStyle;
        private GUIStyle? _sourceLineStyle;
        private GUIStyle? _keybindLineStyle;
        private GUIStyle? _guideBodyStyle;
        private GUIStyle? _pageNumberLeftStyle;
        private GUIStyle? _pageNumberRightStyle;
        private GUIStyle? _navButtonActiveStyle;
        private GUIStyle? _navButtonDisabledStyle;
        private GUIStyle? _footerHintStyle;

        private GUIContent? _bookRunningHeaderContent;
        private GUIContent? _closeFullButtonContent;
        private GUIContent? _prevSpreadButtonContent;
        private GUIContent? _nextSpreadButtonContent;
        private GUIContent? _footerHintContent;
        private GUIContent[]? _tabButtonContents;
        private GUIContent[]? _tabTitleContents;
        private GUIContent[]? _tabSubtitleContents;
        private GUIContent[]? _cachedPageNumberContents;
        private CachedEntryGuiContent[][]? _cachedTabEntries;

        /// <summary>
        /// Yalnizca duz [B] tusu (!ctrlHeld &amp;&amp; !shiftHeld &amp;&amp; !altHeld) ile kitabi acar/kapatir;
        /// boylece [Shift+B] kristal malzeme secimiyle veya Unity Editor derleme kisayoluyla asla cakismaz.
        /// Kitap acikken [1..4] sekme degistirir, [A/D/Sol/Sag Ok] ve fare tekerlegi yaprak cevirir, [ESC] kapatir.
        /// </summary>
        public bool StepHotkeys(bool externalUiBlocked = false)
        {
            if (externalUiBlocked)
            {
                if (IsOpen)
                {
                    Close(restoreCursorLock: false);
                }
                return false;
            }

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (!ctrlHeld && !shiftHeld && !altHeld && Input.GetKeyDown(KeyCode.B))
            {
                Toggle();
                return true;
            }

            if (!IsOpen) return false;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close(restoreCursorLock: true);
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) { SelectTab(0); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { SelectTab(1); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { SelectTab(2); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha4)) { SelectTab(3); return true; }
            if (Input.GetKeyDown(KeyCode.Alpha5)) { SelectTab(4); return true; }

            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                PreviousSpread();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                NextSpread();
                return true;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0.02f)
            {
                PreviousSpread();
                return true;
            }
            if (scroll < -0.02f)
            {
                NextSpread();
                return true;
            }

            return false;
        }

        private static void DrawSolidRect(Rect r, Color col)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, col, 0f, 0f);
        }

        private static void DrawFramedRect(Rect r, Color fillCol, Color borderCol, float borderPx = 1f)
        {
            DrawSolidRect(r, borderCol);
            DrawSolidRect(
                new Rect(
                    r.x + borderPx,
                    r.y + borderPx,
                    Mathf.Max(0f, r.width - borderPx * 2f),
                    Mathf.Max(0f, r.height - borderPx * 2f)),
                fillCol);
        }

        private static void DrawBrassCorner(float x, float y, float size, bool leftSide, bool topSide)
        {
            Rect plateRect = new Rect(x, y, size, size);
            DrawFramedRect(plateRect, ColBrassCorner, ColBrassCornerShadow, 1f);

            float rivetX = leftSide ? x + 4f : x + size - 7f;
            float rivetY = topSide ? y + 4f : y + size - 7f;
            DrawSolidRect(new Rect(rivetX, rivetY, 3f, 3f), ColLeatherOuterBorder);
        }

        public void DrawOnGui() => DrawOnGui(Screen.width, Screen.height);

        public void DrawOnGui(float screenW, float screenH)
        {
            if (!IsOpen) return;
            EnsureStyles();

            Color prevColor = GUI.color;
            GUI.color = Color.white;

            // Fare tekerlegi ile yaprak cevirme (IMGUI EventType.ScrollWheel destegi)
            Event? ev = Event.current;
            if (ev != null && ev.type == EventType.ScrollWheel)
            {
                if (ev.delta.y > 0.05f)
                {
                    NextSpread();
                    ev.Use();
                }
                else if (ev.delta.y < -0.05f)
                {
                    PreviousSpread();
                    ev.Use();
                }
            }

            // 1) Arka plan yari saydam koyu perde
            DrawSolidRect(new Rect(0f, 0f, screenW, screenH), ColBackdropDim);

            // 2) Acik Fiziksel Deri Kitap Ana Boyutlari
            float bookW = Mathf.Clamp(screenW - 56f, 760f, 1180f);
            float bookH = Mathf.Clamp(screenH - 76f, 500f, 780f);
            float bookX = Mathf.Round((screenW - bookW) * 0.5f);
            float bookY = Mathf.Round((screenH - bookH) * 0.5f + 14f);

            int activeTab = SurvivalRecipeBookCatalog.ClampTabIndex(SelectedTabIndex);
            int totalSpreads = TotalSpreadsInActiveTab;
            if (CurrentSpreadIndex >= totalSpreads)
            {
                CurrentSpreadIndex = Math.Max(0, totalSpreads - 1);
            }
            if (CurrentSpreadIndex < 0)
            {
                CurrentSpreadIndex = 0;
            }

            // 3) Ust kenardan disari tasan 5 fiziksel bolum ayiraci sekmesi ([1]..[5])
            float tabsStartX = bookX + 24f;
            float tabsTotalW = bookW - 48f;
            float tabGap = 6f;
            float singleTabW = (tabsTotalW - tabGap * (SurvivalRecipeBookCatalog.TabCount - 1)) / SurvivalRecipeBookCatalog.TabCount;

            for (int t = 0; t < SurvivalRecipeBookCatalog.TabCount; t++)
            {
                bool isSelected = t == activeTab;
                float tabH = isSelected ? 36f : 30f;
                float tabY = isSelected ? (bookY - 31f) : (bookY - 25f);
                Rect tabRect = new Rect(tabsStartX + t * (singleTabW + tabGap), tabY, singleTabW, tabH);

                if (isSelected)
                {
                    DrawFramedRect(tabRect, ColTabActiveFill, ColTabInactiveBorder, 2f);
                    DrawSolidRect(new Rect(tabRect.x + 2f, tabRect.y + 2f, tabRect.width - 4f, 5f), ColTabActiveGoldTop);
                    GUI.Label(
                        new Rect(tabRect.x + 6f, tabRect.y + 6f, tabRect.width - 12f, tabRect.height - 8f),
                        _tabButtonContents![t],
                        _activeTabLabelStyle!);
                }
                else
                {
                    DrawFramedRect(tabRect, ColTabInactiveFill, ColTabInactiveBorder, 1.5f);
                    GUI.Label(
                        new Rect(tabRect.x + 6f, tabRect.y + 3f, tabRect.width - 12f, tabRect.height - 6f),
                        _tabButtonContents![t],
                        _inactiveTabLabelStyle!);
                }

                if (GUI.Button(tabRect, GUIContent.none, GUIStyle.none))
                {
                    SelectTab(t);
                    activeTab = SurvivalRecipeBookCatalog.ClampTabIndex(SelectedTabIndex);
                    totalSpreads = TotalSpreadsInActiveTab;
                }
            }

            // 4) En Dis Katman: Dikisli Koyu Kahverengi Deri Kapak + 4 Eskitilmis Pirinc Kosebent
            Rect leatherRect = new Rect(bookX, bookY, bookW, bookH);
            DrawFramedRect(leatherRect, ColLeatherFill, ColLeatherOuterBorder, 3f);

            // Ic dikis cercevesi (#6B4728)
            Rect stitchOuter = new Rect(bookX + 6f, bookY + 6f, bookW - 12f, bookH - 12f);
            DrawFramedRect(stitchOuter, ColLeatherFill, ColLeatherStitch, 1.5f);

            // 4 kosede pirinc kosebent (#C89B4F)
            const float cornerSize = 18f;
            DrawBrassCorner(bookX + 3f, bookY + 3f, cornerSize, leftSide: true, topSide: true);
            DrawBrassCorner(bookX + bookW - cornerSize - 3f, bookY + 3f, cornerSize, leftSide: false, topSide: true);
            DrawBrassCorner(bookX + 3f, bookY + bookH - cornerSize - 3f, cornerSize, leftSide: true, topSide: false);
            DrawBrassCorner(bookX + bookW - cornerSize - 3f, bookY + bookH - cornerSize - 3f, cornerSize, leftSide: false, topSide: false);

            // 5) Kalin Kagit Yigini Derinligi (Sol, Sag ve Alt kenarlarda 3 kademeli krem sayfa kalinligi)
            Rect stackRect1 = new Rect(bookX + 12f, bookY + 10f, bookW - 24f, bookH - 22f);
            DrawSolidRect(stackRect1, ColPageStack1);

            Rect stackRect2 = new Rect(stackRect1.x + 4f, stackRect1.y + 2f, stackRect1.width - 8f, stackRect1.height - 6f);
            DrawSolidRect(stackRect2, ColPageStack2);

            Rect stackRect3 = new Rect(stackRect2.x + 4f, stackRect2.y + 2f, stackRect2.width - 8f, stackRect2.height - 6f);
            DrawSolidRect(stackRect3, ColPageStack3);

            // 6) Acik Iki Sayfa (Sol Sayfa + Sag Sayfa) Parsomen Alani (#F4EAD2)
            Rect spreadRect = new Rect(stackRect3.x + 4f, stackRect3.y + 2f, stackRect3.width - 8f, stackRect3.height - 6f);
            DrawSolidRect(spreadRect, ColParchmentPage);

            // 7) Ortada tam merkezden gecen dikey cilt/sirt olugu (20px) + Kirmizi ipek ayirac kurdelesi (#8F1D1D)
            const float spineW = 20f;
            float spineX = Mathf.Round(spreadRect.x + (spreadRect.width - spineW) * 0.5f);
            Rect spineOuterRect = new Rect(spineX, spreadRect.y, spineW, spreadRect.height);
            DrawSolidRect(spineOuterRect, ColSpineShadowOuter);

            Rect spineInnerRect = new Rect(spineX + 6f, spreadRect.y, spineW - 12f, spreadRect.height);
            DrawSolidRect(spineInnerRect, ColSpineCreaseInner);

            // Kirmizi ipek ayirac kurdelesi + alt kapaktan disari sarkan V-ucu
            float ribbonW = 8f;
            float ribbonX = Mathf.Round(spineX + (spineW - ribbonW) * 0.5f);
            float ribbonBodyH = (bookY + bookH) - spreadRect.y + 12f;
            DrawSolidRect(new Rect(ribbonX, spreadRect.y, ribbonW, ribbonBodyH), ColSilkBookmarkRibbon);
            DrawSolidRect(new Rect(ribbonX + 2f, spreadRect.y, 2f, ribbonBodyH - 2f), ColSilkRibbonHighlight);
            // V-ucu catal (sol ve sag uc)
            float ribbonTailY = spreadRect.y + ribbonBodyH;
            DrawSolidRect(new Rect(ribbonX, ribbonTailY, 3f, 6f), ColSilkBookmarkRibbon);
            DrawSolidRect(new Rect(ribbonX + ribbonW - 3f, ribbonTailY, 3f, 6f), ColSilkBookmarkRibbon);

            // 8) Sol ve Sag Yaprak Ic Cerceveleri ve Cetvel Cizgileri (#D8C7A6)
            float singlePageW = Mathf.Floor((spreadRect.width - spineW) * 0.5f);
            Rect leftPageRect = new Rect(spreadRect.x, spreadRect.y, singlePageW, spreadRect.height);
            Rect rightPageRect = new Rect(spineX + spineW, spreadRect.y, spreadRect.xMax - (spineX + spineW), spreadRect.height);

            Rect leftInnerFrame = new Rect(leftPageRect.x + 10f, leftPageRect.y + 8f, leftPageRect.width - 16f, leftPageRect.height - 16f);
            Rect rightInnerFrame = new Rect(rightPageRect.x + 6f, rightPageRect.y + 8f, rightPageRect.width - 16f, rightPageRect.height - 16f);

            DrawFramedRect(leftInnerFrame, ColParchmentPage, ColPageRuledLine, 1f);
            DrawFramedRect(rightInnerFrame, ColParchmentPage, ColPageRuledLine, 1f);

            // 9) Sol Sayfa Ust Baslik (Kitap Ust Basligi + Bolum Adi)
            Rect leftHeaderArea = new Rect(leftInnerFrame.x + 12f, leftInnerFrame.y + 6f, leftInnerFrame.width - 24f, 46f);
            GUI.Label(
                new Rect(leftHeaderArea.x, leftHeaderArea.y, leftHeaderArea.width, 18f),
                _bookRunningHeaderContent!,
                _runningHeaderStyle!);
            GUI.Label(
                new Rect(leftHeaderArea.x, leftHeaderArea.y + 18f, leftHeaderArea.width, 26f),
                _tabTitleContents![activeTab],
                _pageTitleStyle!);
            DrawSolidRect(new Rect(leftInnerFrame.x + 10f, leftInnerFrame.y + 54f, leftInnerFrame.width - 20f, 1.5f), ColPageRuledLine);

            // 10) Sag Sayfa Ust Baslik (Alt Baslik + Sag Ust Kirmizi Kurdele Kapat Butonu)
            const float closeBtnW = 166f;
            const float closeBtnH = 28f;
            Rect closeBtnRect = new Rect(rightInnerFrame.xMax - closeBtnW - 10f, rightInnerFrame.y + 8f, closeBtnW, closeBtnH);
            DrawFramedRect(closeBtnRect, ColCloseRibbonFill, ColCloseRibbonBorder, 2f);
            GUI.Label(closeBtnRect, _closeFullButtonContent!, _closeButtonLabelStyle!);
            if (GUI.Button(closeBtnRect, GUIContent.none, GUIStyle.none))
            {
                GUI.color = prevColor;
                Close(restoreCursorLock: true);
                return;
            }

            Rect rightSubtitleRect = new Rect(
                rightInnerFrame.x + 12f,
                rightInnerFrame.y + 6f,
                Mathf.Max(120f, closeBtnRect.x - (rightInnerFrame.x + 20f)),
                46f);
            GUI.Label(rightSubtitleRect, _tabSubtitleContents![activeTab], _subtitleStyle!);
            DrawSolidRect(new Rect(rightInnerFrame.x + 10f, rightInnerFrame.y + 54f, rightInnerFrame.width - 20f, 1.5f), ColPageRuledLine);

            // 11) Sol ve Sag Sayfa Govde Alanlari (Her Sayfada 2 Kart -> Toplam 4 Girdi / Yaprak Cifti)
            const float headerZoneH = 60f;
            const float footerZoneH = 54f;
            Rect leftBodyRect = new Rect(
                leftInnerFrame.x + 8f,
                leftInnerFrame.y + headerZoneH,
                leftInnerFrame.width - 16f,
                Mathf.Max(120f, leftInnerFrame.height - headerZoneH - footerZoneH));
            Rect rightBodyRect = new Rect(
                rightInnerFrame.x + 8f,
                rightInnerFrame.y + headerZoneH,
                rightInnerFrame.width - 16f,
                Mathf.Max(120f, rightInnerFrame.height - headerZoneH - footerZoneH));

            CachedEntryGuiContent[] entries = _cachedTabEntries![activeTab];
            int baseEntryIdx = CurrentSpreadIndex * EntriesPerSpread;

            DrawPageCards(
                leftBodyRect,
                entries,
                startIndex: baseEntryIdx,
                count: EntriesPerPage,
                ref _leftPageScroll);

            DrawPageCards(
                rightBodyRect,
                entries,
                startIndex: baseEntryIdx + EntriesPerPage,
                count: EntriesPerPage,
                ref _rightPageScroll);

            // 12) Alt Cetvel Cizgisi + Sayfa Numaralari (0 B GC) + Yaprak Cevirme Butonlari + Kisayol Ipucu
            float leftFooterY = leftInnerFrame.yMax - footerZoneH;
            float rightFooterY = rightInnerFrame.yMax - footerZoneH;
            DrawSolidRect(new Rect(leftInnerFrame.x + 10f, leftFooterY, leftInnerFrame.width - 20f, 1.5f), ColPageRuledLine);
            DrawSolidRect(new Rect(rightInnerFrame.x + 10f, rightFooterY, rightInnerFrame.width - 20f, 1.5f), ColPageRuledLine);

            int leftPageNumberZeroBased = Mathf.Clamp(CurrentSpreadIndex * 2, 0, MaxCachedPageNumbers - 1);
            int rightPageNumberZeroBased = Mathf.Clamp(CurrentSpreadIndex * 2 + 1, 0, MaxCachedPageNumbers - 1);

            // Sol sayfa numarasi (Sol alt kose)
            Rect leftPageNumRect = new Rect(leftInnerFrame.x + 12f, leftFooterY + 6f, 92f, 26f);
            GUI.Label(leftPageNumRect, _cachedPageNumberContents![leftPageNumberZeroBased], _pageNumberLeftStyle!);

            // Sol sayfa "[◀ Onceki Yaprak (A / Sol Ok)]" butonu
            bool canGoPrev = CurrentSpreadIndex > 0;
            Rect prevBtnRect = new Rect(leftInnerFrame.xMax - 238f, leftFooterY + 5f, 226f, 26f);
            DrawFramedRect(
                prevBtnRect,
                canGoPrev ? ColNavBtnActiveFill : ColNavBtnDisabledFill,
                canGoPrev ? ColNavBtnActiveBorder : ColNavBtnDisabledBorder,
                1.5f);
            GUI.Label(
                prevBtnRect,
                _prevSpreadButtonContent!,
                canGoPrev ? _navButtonActiveStyle! : _navButtonDisabledStyle!);
            if (canGoPrev && GUI.Button(prevBtnRect, GUIContent.none, GUIStyle.none))
            {
                PreviousSpread();
            }

            // Sag sayfa "[Sonraki Yaprak (D / Sag Ok) ▶]" butonu
            bool canGoNext = CurrentSpreadIndex < totalSpreads - 1;
            Rect nextBtnRect = new Rect(rightInnerFrame.x + 12f, rightFooterY + 5f, 226f, 26f);
            DrawFramedRect(
                nextBtnRect,
                canGoNext ? ColNavBtnActiveFill : ColNavBtnDisabledFill,
                canGoNext ? ColNavBtnActiveBorder : ColNavBtnDisabledBorder,
                1.5f);
            GUI.Label(
                nextBtnRect,
                _nextSpreadButtonContent!,
                canGoNext ? _navButtonActiveStyle! : _navButtonDisabledStyle!);
            if (canGoNext && GUI.Button(nextBtnRect, GUIContent.none, GUIStyle.none))
            {
                NextSpread();
            }

            // Sag sayfa numarasi (Sag alt kose)
            Rect rightPageNumRect = new Rect(rightInnerFrame.xMax - 104f, rightFooterY + 6f, 92f, 26f);
            GUI.Label(rightPageNumRect, _cachedPageNumberContents![rightPageNumberZeroBased], _pageNumberRightStyle!);

            // Alt kisayol seridi (Sol ve sag sayfa altinda)
            Rect leftHintRect = new Rect(leftInnerFrame.x + 10f, leftFooterY + 32f, leftInnerFrame.width - 20f, 18f);
            Rect rightHintRect = new Rect(rightInnerFrame.x + 10f, rightFooterY + 32f, rightInnerFrame.width - 20f, 18f);
            GUI.Label(leftHintRect, _footerHintContent!, _footerHintStyle!);
            GUI.Label(rightHintRect, _footerHintContent!, _footerHintStyle!);

            GUI.color = prevColor;
        }

        private void DrawPageCards(
            Rect pageBodyRect,
            CachedEntryGuiContent[] entries,
            int startIndex,
            int count,
            ref Vector2 pageScroll)
        {
            const float cardGap = 10f;
            float availW = pageBodyRect.width;

            // Once bu sayfadaki (en fazla 2) kartin toplam dinamik yuksekligini hesapla
            float measureCardW = availW - 4f;
            float totalNeededH = 4f;
            int validCards = 0;

            for (int c = 0; c < count; c++)
            {
                int idx = startIndex + c;
                if (idx < 0 || idx >= entries.Length) break;
                validCards++;
                totalNeededH += MeasureCardHeight(entries[idx], measureCardW) + cardGap;
            }

            if (validCards == 0)
            {
                return;
            }

            bool needsScroll = totalNeededH > pageBodyRect.height + 2f;
            float cardW = needsScroll ? (availW - 18f) : (availW - 2f);
            if (needsScroll)
            {
                totalNeededH = 4f;
                for (int c = 0; c < count; c++)
                {
                    int idx = startIndex + c;
                    if (idx < 0 || idx >= entries.Length) break;
                    totalNeededH += MeasureCardHeight(entries[idx], cardW) + cardGap;
                }

                Rect viewRect = new Rect(0f, 0f, cardW, Mathf.Max(pageBodyRect.height, totalNeededH));
                pageScroll = GUI.BeginScrollView(pageBodyRect, pageScroll, viewRect, false, true);

                float curY = 2f;
                for (int c = 0; c < count; c++)
                {
                    int idx = startIndex + c;
                    if (idx < 0 || idx >= entries.Length) break;
                    float cardH = MeasureCardHeight(entries[idx], cardW);
                    DrawSingleCard(new Rect(0f, curY, cardW, cardH), entries[idx]);
                    curY += cardH + cardGap;
                }

                GUI.EndScrollView();
            }
            else
            {
                float curY = pageBodyRect.y + 2f;
                for (int c = 0; c < count; c++)
                {
                    int idx = startIndex + c;
                    if (idx < 0 || idx >= entries.Length) break;
                    float cardH = MeasureCardHeight(entries[idx], cardW);
                    DrawSingleCard(new Rect(pageBodyRect.x + 1f, curY, cardW, cardH), entries[idx]);
                    curY += cardH + cardGap;
                }
            }
        }

        private float MeasureCardHeight(CachedEntryGuiContent card, float cardWidth)
        {
            float innerW = Mathf.Max(140f, cardWidth - 20f);
            float titleW = Mathf.Max(90f, innerW - 156f);

            float titleH = Mathf.Max(24f, _entryTitleStyle!.CalcHeight(card.TitleContent, titleW));
            float headerRowH = Mathf.Max(24f, titleH);
            float sourceH = Mathf.Max(20f, _sourceLineStyle!.CalcHeight(card.SourceLabelContent, innerW));
            float keybindH = Mathf.Max(20f, _keybindLineStyle!.CalcHeight(card.ToolKeyContent, innerW));
            float bodyH = Mathf.Max(36f, _guideBodyStyle!.CalcHeight(card.GuideBodyContent, innerW));

            float computed = 10f + headerRowH + 6f + MeasureFigureHeight(card) + sourceH + 4f + keybindH + 6f + bodyH + 10f;
            return Mathf.Max(computed, 132f);
        }

        // ------------------------------------------------------------------
        // Gorsel sekil alani: solda gercek 3B model onizlemesi, sagda "Gerekenler" malzeme ikon seridi
        // ------------------------------------------------------------------
        private const float FigureHeight = 128f;
        private static readonly Color ColFigureFrame = new(0x8A / 255f, 0x71 / 255f, 0x50 / 255f, 1f);
        private static readonly Color ColFigureFill = new(0.90f, 0.85f, 0.74f, 1f);
        private static readonly Color ColIngredientSlot = new(0xEF / 255f, 0xE3 / 255f, 0xC6 / 255f, 1f);
        private GUIStyle? _figureCaptionStyle;
        private GUIStyle? _ingredientCountStyle;
        private GUIStyle? _ingredientNameStyle;
        private GUIContent? _ingredientsHeaderContent;
        private GUIContent? _previewLoadingContent;

        /// <summary>Tarif kitabi 3B onizleme kaynagi (Stage1InteractivePlayController baglar).</summary>
        public Stage1RecipeBookPreviewRenderer? PreviewRenderer { get; set; }

        private float MeasureFigureHeight(CachedEntryGuiContent card)
        {
            if (PreviewRenderer == null) return 0f;
            return PreviewRenderer.HasVisual(card.Id) ? FigureHeight + 8f : 0f;
        }

        private float DrawFigure(float innerX, float curY, float innerW, CachedEntryGuiContent card)
        {
            if (PreviewRenderer == null || !PreviewRenderer.HasVisual(card.Id)) return curY;

            float x = innerX;
            bool hasModel = PreviewRenderer.HasPreviewModel(card.Id);
            if (hasModel)
            {
                Rect frame = new Rect(x, curY, FigureHeight, FigureHeight);
                DrawFramedRect(frame, ColFigureFill, ColFigureFrame, 2f);
                Rect imgRect = new Rect(frame.x + 2f, frame.y + 2f, frame.width - 4f, frame.height - 4f);
                Texture? tex = PreviewRenderer.GetPreview(card.Id);
                if (tex != null)
                {
                    GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit, false);
                }
                else
                {
                    GUI.Label(imgRect, _previewLoadingContent!, _figureCaptionStyle!);
                }
                x = frame.xMax + 10f;
            }

            Stage1RecipeBookPreviewRenderer.IngredientIcon[]? ingredients = PreviewRenderer.GetIngredients(card.Id);
            if (ingredients != null && ingredients.Length > 0)
            {
                if (card.IngredientCountContents == null)
                {
                    card.IngredientCountContents = new GUIContent[ingredients.Length * 2];
                    for (int i = 0; i < ingredients.Length; i++)
                    {
                        card.IngredientCountContents[i * 2] = new GUIContent($"x{ingredients[i].Count}");
                        card.IngredientCountContents[i * 2 + 1] = new GUIContent(ingredients[i].Name);
                    }
                }

                float areaW = innerX + innerW - x;
                GUI.Label(new Rect(x, curY, areaW, 18f), _ingredientsHeaderContent!, _sourceLineStyle!);

                const float slotW = 68f;
                const float slotH = 50f;
                int cols = Mathf.Max(1, Mathf.FloorToInt(areaW / (slotW + 4f)));
                for (int i = 0; i < ingredients.Length; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    if (row > 1) break; // en fazla 2 satir (figur yuksekligi icinde)
                    Rect slot = new Rect(x + col * (slotW + 4f), curY + 22f + row * (slotH + 4f), slotW, slotH);
                    DrawFramedRect(slot, ColIngredientSlot, ColCardBorder, 1f);
                    Texture2D icon = Stage1VisualHotbarAndInventoryUi.GetStaticItemIcon(ingredients[i].Kind, ingredients[i].SubId);
                    GUI.DrawTexture(new Rect(slot.x + (slotW - 32f) * 0.5f, slot.y + 2f, 32f, 32f), icon, ScaleMode.ScaleToFit, true);
                    GUI.Label(new Rect(slot.x + 2f, slot.y + 2f, slotW - 4f, 16f), card.IngredientCountContents[i * 2], _ingredientCountStyle!);
                    GUI.Label(new Rect(slot.x + 1f, slot.y + slotH - 16f, slotW - 2f, 15f), card.IngredientCountContents[i * 2 + 1], _ingredientNameStyle!);
                }
            }

            return curY + FigureHeight + 8f;
        }

        private void DrawSingleCard(Rect cardRect, CachedEntryGuiContent card)
        {
            DrawFramedRect(cardRect, ColCardFill, ColCardBorder, 1.5f);

            float innerX = cardRect.x + 10f;
            float innerW = Mathf.Max(140f, cardRect.width - 20f);
            float curY = cardRect.y + 8f;

            // Kategori rozeti (#6A2416 uzerinde #FFF7E8 yazi) + Baslik (#181009, 15px Bold)
            const float badgeW = 148f;
            const float badgeH = 22f;
            Rect badgeRect = new Rect(innerX, curY + 1f, badgeW, badgeH);
            DrawSolidRect(badgeRect, ColBadgeFill);
            GUI.Label(badgeRect, card.BadgeContent, _badgeStyle!);

            float titleX = badgeRect.xMax + 8f;
            float titleW = Mathf.Max(90f, cardRect.xMax - 10f - titleX);
            float titleH = Mathf.Max(24f, _entryTitleStyle!.CalcHeight(card.TitleContent, titleW));
            GUI.Label(new Rect(titleX, curY, titleW, titleH), card.TitleContent, _entryTitleStyle!);

            curY += Mathf.Max(badgeH, titleH) + 5f;
            DrawSolidRect(new Rect(innerX, curY - 2f, innerW, 1f), ColPageRuledLine);

            // Gorsel: gercek 3B model onizlemesi + gereken malzeme ikonlari
            curY = DrawFigure(innerX, curY + 2f, innerW, card) - 2f;

            // Kaynak / Gereksinim (#144726, 12px Bold, wordWrap = true)
            float sourceH = Mathf.Max(20f, _sourceLineStyle!.CalcHeight(card.SourceLabelContent, innerW));
            GUI.Label(new Rect(innerX, curY, innerW, sourceH), card.SourceLabelContent, _sourceLineStyle!);
            curY += sourceH + 4f;

            // Alet / Tus & Yontem (#153462, 12px Bold, wordWrap = true)
            float keybindH = Mathf.Max(20f, _keybindLineStyle!.CalcHeight(card.ToolKeyContent, innerW));
            GUI.Label(new Rect(innerX, curY, innerW, keybindH), card.ToolKeyContent, _keybindLineStyle!);
            curY += keybindH + 5f;

            // Adim Adim Aciklama (#2A1E14, 12px Normal, wordWrap = true)
            float remainingH = Mathf.Max(32f, cardRect.yMax - 6f - curY);
            GUI.Label(new Rect(innerX, curY, innerW, remainingH), card.GuideBodyContent, _guideBodyStyle!);
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized && _entryTitleStyle != null) return;
            _stylesInitialized = true;

            _runningHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColPageHeaderRunningText }
            };

            _pageTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColPageSectionTitleText }
            };

            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColPageSubtitleText }
            };

            _activeTabLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColTabActiveText }
            };

            _inactiveTabLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColTabInactiveText }
            };

            _closeButtonLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColCloseRibbonText }
            };

            _badgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColBadgeText }
            };

            _entryTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColEntryTitleText }
            };

            _sourceLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColSourceText }
            };

            _keybindLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColToolKeyText }
            };

            _guideBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColGuideBodyText }
            };

            _pageNumberLeftStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColPageNumberText }
            };

            _pageNumberRightStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColPageNumberText }
            };

            _navButtonActiveStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColTabActiveText }
            };

            _navButtonDisabledStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColNavBtnDisabledText }
            };

            _footerHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColPageHeaderRunningText }
            };

            _figureCaptionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = ColPageSubtitleText }
            };

            _ingredientCountStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperRight,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = ColBadgeFill }
            };

            _ingredientNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = ColEntryTitleText }
            };

            _ingredientsHeaderContent = new GUIContent("Gerekenler:");
            _previewLoadingContent = new GUIContent("Görsel hazırlanıyor…");

            _bookRunningHeaderContent = new GUIContent("STRATOCRAFT HAYATTA KALMA TARİF & MALZEME EL KİTABI");
            _closeFullButtonContent = new GUIContent("[X] Kapat [B / ESC]");
            _prevSpreadButtonContent = new GUIContent("◀ Önceki Yaprak (A / Sol Ok)");
            _nextSpreadButtonContent = new GUIContent("Sonraki Yaprak (D / Sağ Ok) ▶");
            _footerHintContent = new GUIContent(
                "[1..5] Bölüm  |  [A/D / Ok / Tekerlek] Yaprak Çevir  |  [I] Çanta  |  [R] Yontma  |  [T] Zihin İnşası  |  [Shift+E] Söndür  |  [C]/[V]/[Shift+B] Kristal");

            _cachedPageNumberContents = new GUIContent[MaxCachedPageNumbers];
            for (int p = 0; p < MaxCachedPageNumbers; p++)
            {
                _cachedPageNumberContents[p] = new GUIContent($"Sayfa {p + 1}");
            }

            _tabButtonContents = new GUIContent[SurvivalRecipeBookCatalog.TabCount];
            _tabTitleContents = new GUIContent[SurvivalRecipeBookCatalog.TabCount];
            _tabSubtitleContents = new GUIContent[SurvivalRecipeBookCatalog.TabCount];
            _cachedTabEntries = new CachedEntryGuiContent[SurvivalRecipeBookCatalog.TabCount][];

            for (int t = 0; t < SurvivalRecipeBookCatalog.TabCount; t++)
            {
                _tabButtonContents[t] = new GUIContent(SurvivalRecipeBookCatalog.GetShortTabLabel(t));
                _tabTitleContents[t] = new GUIContent(SurvivalRecipeBookCatalog.GetTabTitle(t));
                _tabSubtitleContents[t] = new GUIContent(SurvivalRecipeBookCatalog.GetTabSubtitle(t));

                SurvivalRecipeBookEntry[] rawEntries = SurvivalRecipeBookCatalog.GetEntriesArrayForTab(t);
                var cachedList = new CachedEntryGuiContent[rawEntries.Length];
                for (int i = 0; i < rawEntries.Length; i++)
                {
                    cachedList[i] = new CachedEntryGuiContent(in rawEntries[i]);
                }
                _cachedTabEntries[t] = cachedList;
            }
        }
#else
        public bool StepHotkeys(bool externalUiBlocked = false)
        {
            if (externalUiBlocked && IsOpen)
            {
                Close(restoreCursorLock: false);
            }
            return false;
        }

        public void DrawOnGui() { }

        public void DrawOnGui(float screenW, float screenH) { }
#endif
    }
}
