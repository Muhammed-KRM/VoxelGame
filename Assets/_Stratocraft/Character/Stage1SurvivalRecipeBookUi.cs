#nullable enable
using System;
using Stratocraft.Inventory.Systems;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 2 [B] Hayatta Kalma Tarif ve Malzeme Rehber Kitabi IMGUI Arayuzu.
    /// Tum GUIStyle, GUIContent ve Color nesneleri EnsureStyles() icinde tek sefer onbelleklenir;
    /// pencere acikken DrawOnGui(screenW, screenH) kare basina 0 B GC tahsisi yapar.
    /// </summary>
    public sealed class Stage1SurvivalRecipeBookUi
    {
        public bool IsOpen { get; private set; }
        public int SelectedTabIndex { get; private set; }

        public SurvivalRecipeBookTab ActiveTab =>
            (SurvivalRecipeBookTab)SurvivalRecipeBookCatalog.ClampTabIndex(SelectedTabIndex);

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
            if (SelectedTabIndex != clamped)
            {
                SelectedTabIndex = clamped;
#if UNITY_5_3_OR_NEWER
                _scrollPos = Vector2.zero;
#endif
            }
        }

        public void SelectTab(SurvivalRecipeBookTab tab) => SelectTab((int)tab);

#if UNITY_5_3_OR_NEWER
        private sealed class CachedEntryGuiContent
        {
            public readonly GUIContent BadgeContent;
            public readonly GUIContent TitleContent;
            public readonly GUIContent SourceLabelContent;
            public readonly GUIContent ToolKeyContent;
            public readonly GUIContent GuideBodyContent;
            public readonly float CardHeight;

            public CachedEntryGuiContent(in SurvivalRecipeBookEntry entry)
            {
                BadgeContent = new GUIContent(entry.CategoryBadge);
                TitleContent = new GUIContent(entry.Title);
                SourceLabelContent = new GUIContent($"📦 Kaynak / Gereksinim:  {entry.IngredientsOrSource}");
                ToolKeyContent = new GUIContent($"⌨️ Alet / Tuş & Yöntem:  {entry.ToolOrKeybind}");
                GuideBodyContent = new GUIContent(entry.StepByStepGuide);

                int guideLines = Mathf.Max(2, Mathf.CeilToInt(entry.StepByStepGuide.Length / 98f));
                CardHeight = 96f + guideLines * 19f;
            }
        }

        private static readonly Color ColBackdropTint = new(0.02f, 0.03f, 0.05f, 0.68f);
        private static readonly Color ColWindowBg = new(0.07f, 0.09f, 0.13f, 0.96f);
        private static readonly Color ColHeaderBg = new(0.10f, 0.14f, 0.20f, 0.98f);
        private static readonly Color ColCardBgEven = new(0.11f, 0.14f, 0.19f, 0.94f);
        private static readonly Color ColCardBgOdd = new(0.13f, 0.17f, 0.23f, 0.94f);
        private static readonly Color ColBadgeBg = new(0.18f, 0.34f, 0.48f, 0.95f);
        private static readonly Color ColActiveTabBg = new(0.22f, 0.46f, 0.68f, 0.98f);
        private static readonly Color ColInactiveTabBg = new(0.12f, 0.16f, 0.22f, 0.92f);
        private static readonly Color ColCloseBtnBg = new(0.56f, 0.18f, 0.18f, 0.95f);
        private static readonly Color ColFooterBg = new(0.09f, 0.12f, 0.17f, 0.95f);

        private static readonly Color ColTitleText = new(0.98f, 0.93f, 0.76f, 1.0f);
        private static readonly Color ColSubtitleText = new(0.76f, 0.86f, 0.96f, 1.0f);
        private static readonly Color ColBadgeText = new(0.88f, 0.96f, 1.0f, 1.0f);
        private static readonly Color ColEntryTitleText = new(1.0f, 0.92f, 0.66f, 1.0f);
        private static readonly Color ColSourceText = new(0.78f, 0.95f, 0.82f, 1.0f);
        private static readonly Color ColKeybindText = new(0.68f, 0.88f, 1.0f, 1.0f);
        private static readonly Color ColBodyText = new(0.90f, 0.92f, 0.95f, 1.0f);

        private Vector2 _scrollPos;
        private bool _stylesInitialized;

        private GUIStyle? _solidBoxStyle;
        private GUIStyle? _headerTitleStyle;
        private GUIStyle? _subtitleStyle;
        private GUIStyle? _tabButtonStyle;
        private GUIStyle? _activeTabButtonStyle;
        private GUIStyle? _closeButtonStyle;
        private GUIStyle? _badgeStyle;
        private GUIStyle? _entryTitleStyle;
        private GUIStyle? _sourceLineStyle;
        private GUIStyle? _keybindLineStyle;
        private GUIStyle? _guideBodyStyle;
        private GUIStyle? _footerHintStyle;

        private GUIContent? _windowHeaderContent;
        private GUIContent? _closeXContent;
        private GUIContent? _closeFullButtonContent;
        private GUIContent? _footerHintContent;
        private GUIContent[]? _tabButtonContents;
        private GUIContent[]? _tabSubtitleContents;
        private CachedEntryGuiContent[][]? _cachedTabEntries;
        private float[]? _cachedTabTotalContentHeights;

        /// <summary>
        /// Duz [B] tusu (Ctrl basili degilken) ile acma/kapama, acikken [1..4] sekme gecisi ve [ESC] kapama kisayollarini isler.
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
            if (!ctrlHeld && Input.GetKeyDown(KeyCode.B))
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

            return false;
        }

        public void DrawOnGui() => DrawOnGui(Screen.width, Screen.height);

        public void DrawOnGui(float screenW, float screenH)
        {
            if (!IsOpen) return;
            EnsureStyles();

            Color prevColor = GUI.color;

            // 1) Tam ekran koyu yari saydam arka plan perdesi
            GUI.color = ColBackdropTint;
            GUI.Box(new Rect(0f, 0f, screenW, screenH), GUIContent.none, _solidBoxStyle!);

            // 2) Ana Rehber Kitabi penceresi
            float panelW = Mathf.Clamp(screenW - 64f, 680f, 1020f);
            float panelH = Mathf.Clamp(screenH - 64f, 480f, 760f);
            float px = (screenW - panelW) * 0.5f;
            float py = (screenH - panelH) * 0.5f;

            GUI.color = ColWindowBg;
            GUI.Box(new Rect(px, py, panelW, panelH), GUIContent.none, _solidBoxStyle!);

            // 3) Ust Baslik Cubugu + Kapat Butonlari
            Rect headerRect = new Rect(px + 10f, py + 10f, panelW - 20f, 44f);
            GUI.color = ColHeaderBg;
            GUI.Box(headerRect, GUIContent.none, _solidBoxStyle!);
            GUI.color = prevColor;

            GUI.Label(new Rect(headerRect.x + 14f, headerRect.y + 8f, headerRect.width - 250f, 28f),
                _windowHeaderContent!, _headerTitleStyle!);

            Rect closeTextBtnRect = new Rect(headerRect.xMax - 224f, headerRect.y + 7f, 176f, 30f);
            Rect closeXBtnRect = new Rect(headerRect.xMax - 42f, headerRect.y + 7f, 34f, 30f);

            GUI.color = ColCloseBtnBg;
            bool clickedCloseText = GUI.Button(closeTextBtnRect, _closeFullButtonContent!, _closeButtonStyle!);
            bool clickedCloseX = GUI.Button(closeXBtnRect, _closeXContent!, _closeButtonStyle!);
            GUI.color = prevColor;

            if (clickedCloseText || clickedCloseX)
            {
                Close(restoreCursorLock: true);
                return;
            }

            // 4) 4 Sekme Butonu Cubugu
            float tabBarY = py + 62f;
            float tabGap = 8f;
            float totalTabsW = panelW - 20f;
            float singleTabW = (totalTabsW - tabGap * (SurvivalRecipeBookCatalog.TabCount - 1)) / SurvivalRecipeBookCatalog.TabCount;

            for (int t = 0; t < SurvivalRecipeBookCatalog.TabCount; t++)
            {
                Rect tabRect = new Rect(px + 10f + t * (singleTabW + tabGap), tabBarY, singleTabW, 36f);
                bool isSelected = t == SelectedTabIndex;
                GUI.color = isSelected ? ColActiveTabBg : ColInactiveTabBg;
                if (GUI.Button(tabRect, _tabButtonContents![t], isSelected ? _activeTabButtonStyle! : _tabButtonStyle!))
                {
                    SelectTab(t);
                }
            }
            GUI.color = prevColor;

            // 5) Sekme Alt Aciklama Bandi
            int activeTab = SurvivalRecipeBookCatalog.ClampTabIndex(SelectedTabIndex);
            Rect subBannerRect = new Rect(px + 14f, py + 104f, panelW - 28f, 24f);
            GUI.Label(subBannerRect, _tabSubtitleContents![activeTab], _subtitleStyle!);

            // 6) Alt Kisimda Kaydirilabilir (ScrollView) Kart Listesi
            float scrollAreaY = py + 132f;
            float footerH = 34f;
            float scrollAreaH = panelH - 132f - footerH - 12f;
            Rect scrollOuterRect = new Rect(px + 12f, scrollAreaY, panelW - 24f, scrollAreaH);

            CachedEntryGuiContent[] entries = _cachedTabEntries![activeTab];
            float contentW = scrollOuterRect.width - 22f;
            float contentH = Mathf.Max(scrollAreaH, _cachedTabTotalContentHeights![activeTab]);
            Rect scrollViewRect = new Rect(0f, 0f, contentW, contentH);

            _scrollPos = GUI.BeginScrollView(scrollOuterRect, _scrollPos, scrollViewRect, false, true);

            float curY = 4f;
            const float cardGap = 8f;
            for (int i = 0; i < entries.Length; i++)
            {
                CachedEntryGuiContent card = entries[i];
                float cardH = card.CardHeight;
                Rect cardRect = new Rect(2f, curY, contentW - 4f, cardH);

                GUI.color = (i & 1) == 0 ? ColCardBgEven : ColCardBgOdd;
                GUI.Box(cardRect, GUIContent.none, _solidBoxStyle!);

                // Sol ust kategori rozeti + baslik
                Rect badgeRect = new Rect(cardRect.x + 10f, cardRect.y + 8f, 186f, 22f);
                GUI.color = ColBadgeBg;
                GUI.Box(badgeRect, GUIContent.none, _solidBoxStyle!);
                GUI.color = prevColor;
                GUI.Label(badgeRect, card.BadgeContent, _badgeStyle!);

                GUI.Label(new Rect(badgeRect.xMax + 10f, cardRect.y + 7f, cardRect.width - 216f, 24f),
                    card.TitleContent, _entryTitleStyle!);

                // Kaynak / Malzeme satiri
                GUI.Label(new Rect(cardRect.x + 12f, cardRect.y + 34f, cardRect.width - 24f, 22f),
                    card.SourceLabelContent, _sourceLineStyle!);

                // Alet / Tus ve Yontem satiri
                GUI.Label(new Rect(cardRect.x + 12f, cardRect.y + 56f, cardRect.width - 24f, 22f),
                    card.ToolKeyContent, _keybindLineStyle!);

                // Adim adim aciklama govdesi
                GUI.Label(new Rect(cardRect.x + 12f, cardRect.y + 80f, cardRect.width - 24f, cardH - 86f),
                    card.GuideBodyContent, _guideBodyStyle!);

                curY += cardH + cardGap;
            }

            GUI.EndScrollView();

            // 7) Alt Kisayol Bilgi Cubugu
            Rect footerRect = new Rect(px + 10f, py + panelH - footerH - 6f, panelW - 20f, footerH);
            GUI.color = ColFooterBg;
            GUI.Box(footerRect, GUIContent.none, _solidBoxStyle!);
            GUI.color = prevColor;
            GUI.Label(footerRect, _footerHintContent!, _footerHintStyle!);
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized && _solidBoxStyle != null) return;
            _stylesInitialized = true;

            _solidBoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.whiteTexture },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(6, 6, 6, 6)
            };

            _headerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = ColTitleText }
            };

            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = ColSubtitleText }
            };

            _tabButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = Texture2D.whiteTexture, textColor = new Color(0.84f, 0.88f, 0.94f, 1.0f) },
                hover = { background = Texture2D.whiteTexture, textColor = Color.white },
                active = { background = Texture2D.whiteTexture, textColor = ColTitleText }
            };

            _activeTabButtonStyle = new GUIStyle(_tabButtonStyle)
            {
                fontSize = 13,
                normal = { background = Texture2D.whiteTexture, textColor = ColTitleText },
                hover = { background = Texture2D.whiteTexture, textColor = Color.white }
            };

            _closeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = Texture2D.whiteTexture, textColor = Color.white },
                hover = { background = Texture2D.whiteTexture, textColor = ColTitleText },
                active = { background = Texture2D.whiteTexture, textColor = Color.white }
            };

            _badgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ColBadgeText }
            };

            _entryTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = ColEntryTitleText }
            };

            _sourceLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = ColSourceText }
            };

            _keybindLineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = ColKeybindText }
            };

            _guideBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = ColBodyText }
            };

            _footerHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ColSubtitleText }
            };

            _windowHeaderContent = new GUIContent("📖 STRATOCRAFT HAYATTA KALMA TARİF & MALZEME REHBERİ");
            _closeXContent = new GUIContent("X");
            _closeFullButtonContent = new GUIContent("[B] veya [ESC] ile Kapat");
            _footerHintContent = new GUIContent(
                "Kısayollar: [1..4] Sekme Değiştir  |  [I] Çanta Üretimi  |  [R] Taş Yontma  |  [T] Zihin İnşası  |  [Shift+E] Ateşi Söndür  |  [C]/[V]/[Ctrl+B] Madde Kristali");

            _tabButtonContents = new GUIContent[SurvivalRecipeBookCatalog.TabCount];
            _tabSubtitleContents = new GUIContent[SurvivalRecipeBookCatalog.TabCount];
            _cachedTabEntries = new CachedEntryGuiContent[SurvivalRecipeBookCatalog.TabCount][];
            _cachedTabTotalContentHeights = new float[SurvivalRecipeBookCatalog.TabCount];

            for (int t = 0; t < SurvivalRecipeBookCatalog.TabCount; t++)
            {
                _tabButtonContents[t] = new GUIContent(SurvivalRecipeBookCatalog.GetShortTabLabel(t));
                _tabSubtitleContents[t] = new GUIContent(SurvivalRecipeBookCatalog.GetTabSubtitle(t));

                SurvivalRecipeBookEntry[] rawEntries = SurvivalRecipeBookCatalog.GetEntriesArrayForTab(t);
                var cachedList = new CachedEntryGuiContent[rawEntries.Length];
                float totalH = 12f;
                for (int i = 0; i < rawEntries.Length; i++)
                {
                    cachedList[i] = new CachedEntryGuiContent(in rawEntries[i]);
                    totalH += cachedList[i].CardHeight + 8f;
                }
                _cachedTabEntries[t] = cachedList;
                _cachedTabTotalContentHeights[t] = totalH;
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
