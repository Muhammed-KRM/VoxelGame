#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Oyun içi canlı ayarlar, zorluk çarpanları, çevre etkileri anahtarı ve değiştirilebilir kontrol tuşları.
    /// ESC menüsü açıldığında dış dünya (Time.timeScale = 1.0f) ASLA durmaz; canlılar, sıvılar ve fizik akmaya devam eder.
    /// </summary>
    public static class Stage1GameSettings
    {
        private static bool _loadedFromPrefs;

        // 1) Fare, Kamera ve Ses Ayarları
        public static float MouseSensitivity = 1.00f;
        public static bool InvertMouseY;
        public static float CameraFieldOfView = 75.0f;
        public static int RenderDistanceChunks = 10;
        public static float MasterVolume = 0.85f;
        public static float CombatSfxVolume = 0.90f;
        public static float AmbientVolume = 0.75f;

        // 2) Çevre Etkileri (Üşüme / Sıcaklama / Boğulma / Lav-Asit Hasarı) Kapatma Ayarı
        public static bool EnableEnvironmentalEffects = true;

        // 3) Oyun Zorluk Seviyesi (0: Barışçıl, 1: Kolay, 2: Normal, 3: Zor, 4: Kabus, 5: Özel)
        public static int DifficultyPreset = 2;
        public static float CreatureAggressionMultiplier = 1.00f;
        public static float CreatureHealthMultiplier = 1.00f;
        public static float CreatureDamageMultiplier = 1.00f;

        // 4) Değiştirilebilir Oyun Mekanik & Fizik Çarpanları
        public static float PlayerCombatDamageMultiplier = 1.00f;
        public static float MiningSpeedMultiplier = 1.00f;
        public static float PlayerMoveSpeedMultiplier = 1.00f;
        public static float PlayerJumpMultiplier = 1.00f;
        public static float StaminaDrainMultiplier = 1.00f;
        public static float ToolDurabilityLossMultiplier = 1.00f;

        // 4B) Performans, Chunk LOD (Distant Horizons) & Sürü Optimizasyon Ayarları (Varsayılan: Tümü AÇIK)
        public static bool EnableSubChunkPartialMeshing = true;
        public static bool EnableUndergroundCaveCulling = true;
        public static bool EnableDistantHorizonLod = true;
        public static bool EnableBurstAndAsyncBakeMesh = true;
        public static bool EnableFlowFieldAndSpatialBoids = true;

        // 5) Değiştirilebilir Kontrol Tuşları
        public static KeyCode KeyBackpack = KeyCode.I;
        public static KeyCode KeyMatterCrystal = KeyCode.C;
        public static KeyCode KeyInteract = KeyCode.E;
        public static KeyCode KeyMartialStance = KeyCode.G;
        public static KeyCode KeyKickOrBash = KeyCode.F;
        public static KeyCode KeyFeintOrSheathe = KeyCode.Q;
        public static KeyCode KeyQuickDrop = KeyCode.X;
        public static KeyCode KeyProne = KeyCode.Z;
        public static KeyCode KeyDodge = KeyCode.LeftAlt;
        public static KeyCode KeyCameraToggle = KeyCode.F5;

        public static void EnsureLoaded()
        {
            if (_loadedFromPrefs) return;
            _loadedFromPrefs = true;

            MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("SC_MouseSens", 1.00f), 0.15f, 3.00f);
            InvertMouseY = PlayerPrefs.GetInt("SC_InvertY", 0) != 0;
            CameraFieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat("SC_CamFov", 75.0f), 60.0f, 105.0f);
            RenderDistanceChunks = Mathf.Clamp(PlayerPrefs.GetInt("SC_RenderDist", 10), 6, 24);
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("SC_MasterVol", 0.85f));
            CombatSfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("SC_CombatVol", 0.90f));
            AmbientVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("SC_AmbientVol", 0.75f));

            EnableEnvironmentalEffects = PlayerPrefs.GetInt("SC_EnvEffects", 1) != 0;
            DifficultyPreset = Mathf.Clamp(PlayerPrefs.GetInt("SC_DiffPreset", 2), 0, 5);
            CreatureAggressionMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_MobAggro", 1.00f), 0.00f, 3.00f);
            CreatureHealthMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_MobHp", 1.00f), 0.25f, 4.00f);
            CreatureDamageMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_MobDmg", 1.00f), 0.00f, 4.00f);

            PlayerCombatDamageMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_PlrDmg", 1.00f), 0.50f, 5.00f);
            MiningSpeedMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_MineSpd", 1.00f), 0.50f, 5.00f);
            PlayerMoveSpeedMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_MoveSpd", 1.00f), 0.50f, 2.50f);
            PlayerJumpMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_JumpMult", 1.00f), 0.50f, 2.50f);
            StaminaDrainMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_StamDrain", 1.00f), 0.00f, 2.00f);
            ToolDurabilityLossMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("SC_ToolLoss", 1.00f), 0.00f, 2.00f);

            KeyBackpack = LoadKey("SC_Key_Backpack", KeyCode.I);
            KeyMatterCrystal = LoadKey("SC_Key_Crystal", KeyCode.C);
            KeyInteract = LoadKey("SC_Key_Interact", KeyCode.E);
            KeyMartialStance = LoadKey("SC_Key_Stance", KeyCode.G);
            KeyKickOrBash = LoadKey("SC_Key_Kick", KeyCode.F);
            KeyFeintOrSheathe = LoadKey("SC_Key_Feint", KeyCode.Q);
            KeyQuickDrop = LoadKey("SC_Key_Drop", KeyCode.X);
            KeyProne = LoadKey("SC_Key_Prone", KeyCode.Z);
            KeyDodge = LoadKey("SC_Key_Dodge", KeyCode.LeftAlt);
            KeyCameraToggle = LoadKey("SC_Key_Cam", KeyCode.F5);

            AudioListener.volume = MasterVolume;
        }

        private static KeyCode LoadKey(string prefKey, KeyCode defaultKey)
        {
            int raw = PlayerPrefs.GetInt(prefKey, (int)defaultKey);
            return Enum.IsDefined(typeof(KeyCode), raw) ? (KeyCode)raw : defaultKey;
        }

        public static void SaveToPrefs()
        {
            PlayerPrefs.SetFloat("SC_MouseSens", MouseSensitivity);
            PlayerPrefs.SetInt("SC_InvertY", InvertMouseY ? 1 : 0);
            PlayerPrefs.SetFloat("SC_CamFov", CameraFieldOfView);
            PlayerPrefs.SetInt("SC_RenderDist", RenderDistanceChunks);
            PlayerPrefs.SetFloat("SC_MasterVol", MasterVolume);
            PlayerPrefs.SetFloat("SC_CombatVol", CombatSfxVolume);
            PlayerPrefs.SetFloat("SC_AmbientVol", AmbientVolume);

            PlayerPrefs.SetInt("SC_EnvEffects", EnableEnvironmentalEffects ? 1 : 0);
            PlayerPrefs.SetInt("SC_DiffPreset", DifficultyPreset);
            PlayerPrefs.SetFloat("SC_MobAggro", CreatureAggressionMultiplier);
            PlayerPrefs.SetFloat("SC_MobHp", CreatureHealthMultiplier);
            PlayerPrefs.SetFloat("SC_MobDmg", CreatureDamageMultiplier);

            PlayerPrefs.SetFloat("SC_PlrDmg", PlayerCombatDamageMultiplier);
            PlayerPrefs.SetFloat("SC_MineSpd", MiningSpeedMultiplier);
            PlayerPrefs.SetFloat("SC_MoveSpd", PlayerMoveSpeedMultiplier);
            PlayerPrefs.SetFloat("SC_JumpMult", PlayerJumpMultiplier);
            PlayerPrefs.SetFloat("SC_StamDrain", StaminaDrainMultiplier);
            PlayerPrefs.SetFloat("SC_ToolLoss", ToolDurabilityLossMultiplier);

            PlayerPrefs.SetInt("SC_Key_Backpack", (int)KeyBackpack);
            PlayerPrefs.SetInt("SC_Key_Crystal", (int)KeyMatterCrystal);
            PlayerPrefs.SetInt("SC_Key_Interact", (int)KeyInteract);
            PlayerPrefs.SetInt("SC_Key_Stance", (int)KeyMartialStance);
            PlayerPrefs.SetInt("SC_Key_Kick", (int)KeyKickOrBash);
            PlayerPrefs.SetInt("SC_Key_Feint", (int)KeyFeintOrSheathe);
            PlayerPrefs.SetInt("SC_Key_Drop", (int)KeyQuickDrop);
            PlayerPrefs.SetInt("SC_Key_Prone", (int)KeyProne);
            PlayerPrefs.SetInt("SC_Key_Dodge", (int)KeyDodge);
            PlayerPrefs.SetInt("SC_Key_Cam", (int)KeyCameraToggle);
            PlayerPrefs.Save();
        }

        public static void ApplyDifficultyPreset(int preset)
        {
            DifficultyPreset = Mathf.Clamp(preset, 0, 4);
            switch (DifficultyPreset)
            {
                case 0: // Barışçıl (Peaceful)
                    CreatureAggressionMultiplier = 0.00f;
                    CreatureHealthMultiplier = 0.65f;
                    CreatureDamageMultiplier = 0.00f;
                    break;
                case 1: // Kolay (Easy)
                    CreatureAggressionMultiplier = 0.70f;
                    CreatureHealthMultiplier = 0.75f;
                    CreatureDamageMultiplier = 0.60f;
                    break;
                case 2: // Normal
                    CreatureAggressionMultiplier = 1.00f;
                    CreatureHealthMultiplier = 1.00f;
                    CreatureDamageMultiplier = 1.00f;
                    break;
                case 3: // Zor (Hard)
                    CreatureAggressionMultiplier = 1.45f;
                    CreatureHealthMultiplier = 1.50f;
                    CreatureDamageMultiplier = 1.50f;
                    break;
                case 4: // Kabus (Nightmare)
                    CreatureAggressionMultiplier = 2.00f;
                    CreatureHealthMultiplier = 2.25f;
                    CreatureDamageMultiplier = 2.20f;
                    break;
            }
            SaveToPrefs();
        }

        public static void ResetMultipliersToDefault()
        {
            ApplyDifficultyPreset(2);
            EnableEnvironmentalEffects = true;
            PlayerCombatDamageMultiplier = 1.00f;
            MiningSpeedMultiplier = 1.00f;
            PlayerMoveSpeedMultiplier = 1.00f;
            PlayerJumpMultiplier = 1.00f;
            StaminaDrainMultiplier = 1.00f;
            ToolDurabilityLossMultiplier = 1.00f;
            SaveToPrefs();
        }

        public static void ResetKeybindsToDefault()
        {
            KeyBackpack = KeyCode.I;
            KeyMatterCrystal = KeyCode.C;
            KeyInteract = KeyCode.E;
            KeyMartialStance = KeyCode.G;
            KeyKickOrBash = KeyCode.F;
            KeyFeintOrSheathe = KeyCode.Q;
            KeyQuickDrop = KeyCode.X;
            KeyProne = KeyCode.Z;
            KeyDodge = KeyCode.LeftAlt;
            KeyCameraToggle = KeyCode.F5;
            SaveToPrefs();
        }

        public static string GetDifficultyDisplayName(int preset) => preset switch
        {
            0 => "BARIŞÇIL (Canlılar Saldırmaz)",
            1 => "KOLAY (0.7x Agresiflik & 0.6x Hasar)",
            2 => "NORMAL (Standart Denge 1.0x)",
            3 => "ZOR (1.45x Agresiflik & 1.5x Can/Hasar)",
            4 => "KABUS (2.0x Agresiflik & 2.25x Can/Hasar)",
            _ => "ÖZEL (Kullanıcı Çarpanları)"
        };
    }

    /// <summary>
    /// [ESC] tuşu ile açılan, dış dünyayı DURDURMAYAN (Canlı Dünya) Sistem &amp; Ayarlar Menüsü.
    /// - Ana Menü: Oyuna Devam Et, Ayarlar, Dünyayı Kaydet (.sregion), Oyundan Çık.
    /// - Ayarlar Sekmeleri:
    ///   1) Fare Hassasiyeti, Kamera FOV &amp; Ses Seviyeleri
    ///   2) Zorluk Seviyesi (Canlı Agresifliği/Canı/Hasarı) &amp; Çevre Etkilerini (Üşüme/Sıcaklama) Kapatma
    ///   3) Değiştirilebilir Oyun &amp; Fizik Çarpanları (Kazı Hızı, Oyuncu Hasarı, Koşu/Zıplama, Stamina, Alet Ömrü)
    ///   4) Değiştirilebilir Kontrol Tuşları + Tüm Oyun Kontrolleri Tam Rehberi
    /// </summary>
    public sealed class Stage1EscapeMenuAndSettingsController
    {
        public bool IsMenuOpen { get; private set; }
        public bool IsInSettingsSubMenu { get; private set; }
        public int ActiveSettingsTab { get; private set; } // 0: Fare & Ses, 1: Zorluk & Çevre, 2: Çarpanlar, 3: Kontroller
        public bool IsWaitingForKeyRebind => IsMenuOpen && _waitingForRebindSlot >= 0;
        private int _waitingForRebindSlot = -1;
        private Vector2 _controlsScrollPos;

        private GUIStyle? _windowBoxStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _subHeaderStyle;
        private GUIStyle? _buttonStyle;
        private GUIStyle? _activeTabButtonStyle;
        private GUIStyle? _dangerButtonStyle;
        private GUIStyle? _labelStyle;
        private GUIStyle? _valueLabelStyle;
        private GUIStyle? _controlRowStyle;
        private Texture2D? _darkOverlayTex;
        private Texture2D? _panelBgTex;

        public void StepEscapeKeyAndRebind(
            ref bool isBackpackOpen,
            Action closeBackpackAction,
            Stage1CreatureSimulationBridge creatureBridge,
            UnityEngine.Camera? playerCamera)
        {
            Stage1GameSettings.EnsureLoaded();

            if (playerCamera != null && Mathf.Abs(playerCamera.fieldOfView - Stage1GameSettings.CameraFieldOfView) > 0.05f)
            {
                playerCamera.fieldOfView = Stage1GameSettings.CameraFieldOfView;
            }

            // Tuş atama (Rebind) bekleniyorsa basılan ilk geçerli tuşu yakala
            if (IsMenuOpen && _waitingForRebindSlot >= 0)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _waitingForRebindSlot = -1;
                    return;
                }

                foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
                {
                    if (kc == KeyCode.None || kc == KeyCode.Escape ||
                        kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1)
                    {
                        continue;
                    }
                    if (Input.GetKeyDown(kc))
                    {
                        AssignReboundKey(_waitingForRebindSlot, kc);
                        _waitingForRebindSlot = -1;
                        Stage1GameSettings.SaveToPrefs();
                        return;
                    }
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Eğer [I] 3B Çanta açıksa önce çantayı kapat, tekrar ESC basıldığında menüyü aç/kapat
                if (isBackpackOpen && !IsMenuOpen)
                {
                    closeBackpackAction();
                    return;
                }

                ToggleMenu();
                if (!IsMenuOpen)
                {
                    creatureBridge.ApplyLiveDifficultyMultipliers();
                }
            }
        }

        public void ToggleMenu()
        {
            IsMenuOpen = !IsMenuOpen;
            _waitingForRebindSlot = -1;
            // Dış dünya ASLA durmaz: Time.timeScale daima 1.0f kalır!
            Time.timeScale = 1.0f;

            if (IsMenuOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                IsInSettingsSubMenu = false;
                Stage1GameSettings.SaveToPrefs();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void CloseMenuAndResume(Stage1CreatureSimulationBridge creatureBridge)
        {
            IsMenuOpen = false;
            IsInSettingsSubMenu = false;
            _waitingForRebindSlot = -1;
            Time.timeScale = 1.0f;
            Stage1GameSettings.SaveToPrefs();
            creatureBridge.ApplyLiveDifficultyMultipliers();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void AssignReboundKey(int slot, KeyCode newKey)
        {
            switch (slot)
            {
                case 0: Stage1GameSettings.KeyBackpack = newKey; break;
                case 1: Stage1GameSettings.KeyMatterCrystal = newKey; break;
                case 2: Stage1GameSettings.KeyInteract = newKey; break;
                case 3: Stage1GameSettings.KeyMartialStance = newKey; break;
                case 4: Stage1GameSettings.KeyKickOrBash = newKey; break;
                case 5: Stage1GameSettings.KeyFeintOrSheathe = newKey; break;
                case 6: Stage1GameSettings.KeyQuickDrop = newKey; break;
                case 7: Stage1GameSettings.KeyProne = newKey; break;
                case 8: Stage1GameSettings.KeyDodge = newKey; break;
                case 9: Stage1GameSettings.KeyCameraToggle = newKey; break;
            }
        }

        private void EnsureStyles()
        {
            if (_darkOverlayTex == null)
            {
                _darkOverlayTex = new Texture2D(1, 1);
                _darkOverlayTex.SetPixel(0, 0, new Color(0.03f, 0.05f, 0.08f, 0.62f));
                _darkOverlayTex.Apply();
            }
            if (_panelBgTex == null)
            {
                _panelBgTex = new Texture2D(1, 1);
                _panelBgTex.SetPixel(0, 0, new Color(0.08f, 0.11f, 0.16f, 0.94f));
                _panelBgTex.Apply();
            }

            _windowBoxStyle ??= new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelBgTex, textColor = Color.white },
                padding = new RectOffset(16, 16, 14, 14)
            };
            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.88f, 0.55f, 1f) }
            };
            _subHeaderStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.55f, 0.92f, 1.0f, 1f) }
            };
            _buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _activeTabButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1.0f, 0.92f, 0.35f, 1f) }
            };
            _dangerButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1.0f, 0.55f, 0.55f, 1f) }
            };
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.90f, 0.94f, 0.98f, 1f) }
            };
            _valueLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1.0f, 0.90f, 0.48f, 1f) }
            };
            _controlRowStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.88f, 0.95f, 1.0f, 1f) }
            };
        }

        public void DrawEscapeMenuOnGui(
            Stage1CreatureSimulationBridge creatureBridge,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action saveWorldAction,
            Action loadWorldAction)
        {
            if (!IsMenuOpen) return;
            EnsureStyles();

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _darkOverlayTex);

            if (!IsInSettingsSubMenu)
            {
                DrawMainEscapeWindow(creatureBridge, miningCtrl, saveWorldAction, loadWorldAction);
            }
            else
            {
                DrawSettingsSubMenuWindow(creatureBridge, miningCtrl);
            }
        }

        private void DrawMainEscapeWindow(
            Stage1CreatureSimulationBridge creatureBridge,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action saveWorldAction,
            Action loadWorldAction)
        {
            float w = 480f, h = 430f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.Box(new Rect(x, y, w, h), string.Empty, _windowBoxStyle!);
            GUI.Label(new Rect(x + 20f, y + 16f, w - 40f, 30f), "⚔️ STRATOCRAFT — CANLI SİSTEM MENÜSÜ", _titleStyle!);

            string liveNote = "● Dış Dünya Canlı Devam Ediyor (Simülasyon & AI Durmaz)";
            GUIStyle liveStyle = new GUIStyle(_labelStyle!) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.50f, 0.98f, 0.68f, 1f) } };
            GUI.Label(new Rect(x + 20f, y + 46f, w - 40f, 22f), liveNote, liveStyle);

            string diffSummary = $"Zorluk: {Stage1GameSettings.GetDifficultyDisplayName(Stage1GameSettings.DifficultyPreset)} | " +
                                 $"Çevre Etkileri: {(Stage1GameSettings.EnableEnvironmentalEffects ? "AÇIK" : "KAPALI")}";
            GUIStyle subInfoStyle = new GUIStyle(_labelStyle!) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = new Color(0.82f, 0.88f, 0.96f, 1f) } };
            GUI.Label(new Rect(x + 16f, y + 70f, w - 32f, 22f), diffSummary, subInfoStyle);

            float btnX = x + 45f, btnW = w - 90f, btnH = 44f;
            float curY = y + 104f;

            if (GUI.Button(new Rect(btnX, curY, btnW, btnH), "▶  OYUNA DEVAM ET  [ESC]", _buttonStyle!))
            {
                CloseMenuAndResume(creatureBridge);
                return;
            }
            curY += 56f;

            if (GUI.Button(new Rect(btnX, curY, btnW, btnH), "⚙️  AYARLAR  (Fare, Ses, Zorluk, Çevre, Çarpanlar & Kontroller)", _buttonStyle!))
            {
                IsInSettingsSubMenu = true;
                return;
            }
            curY += 56f;

            // Hızlı Çevre Etkisi & Zorluk Butonları (Ana menüden de tek tıkla erişilebilir)
            string envQuickLabel = Stage1GameSettings.EnableEnvironmentalEffects
                ? "🌡️ Çevre Etkileri (Üşüme/Sıcaklama): AÇIK  (Kapatmak İçin Tıkla)"
                : "🛡️ Çevre Etkileri (Üşüme/Sıcaklama): KAPALI  (Açmak İçin Tıkla)";
            if (GUI.Button(new Rect(btnX, curY, btnW, 38f), envQuickLabel, _buttonStyle!))
            {
                Stage1GameSettings.EnableEnvironmentalEffects = !Stage1GameSettings.EnableEnvironmentalEffects;
                Stage1GameSettings.SaveToPrefs();
                miningCtrl.LastFeedbackText = Stage1GameSettings.EnableEnvironmentalEffects
                    ? "Çevre Etkileri (Üşüme / Sıcaklama / Oksijen) AÇILDI."
                    : "Çevre Etkileri (Üşüme / Sıcaklama / Oksijen) KAPATILDI (Sabit 37.0°C & %100 Oksijen).";
            }
            curY += 48f;

            float halfW = (btnW - 12f) * 0.5f;
            if (GUI.Button(new Rect(btnX, curY, halfW, 38f), "💾 Dünyayı Kaydet [F6]", _buttonStyle!))
            {
                saveWorldAction();
            }
            if (GUI.Button(new Rect(btnX + halfW + 12f, curY, halfW, 38f), "📂 Kaydı Yükle [Shift+F9]", _buttonStyle!))
            {
                loadWorldAction();
            }
            curY += 52f;

            if (GUI.Button(new Rect(btnX, curY, btnW, btnH), "🚪  OYUNDAN ÇIK  (Quit Game)", _dangerButtonStyle!))
            {
                Stage1GameSettings.SaveToPrefs();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        private void DrawSettingsSubMenuWindow(
            Stage1CreatureSimulationBridge creatureBridge,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            float w = Mathf.Min(860f, Screen.width - 40f);
            float h = Mathf.Min(610f, Screen.height - 40f);
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.Box(new Rect(x, y, w, h), string.Empty, _windowBoxStyle!);
            GUI.Label(new Rect(x + 20f, y + 12f, w - 40f, 28f), "⚙️ OYUN AYARLARI, ZORLUK, ÇARPANLAR VE KONTROLLER", _titleStyle!);

            // Üst Sekme (Tab) Çubuğu
            float tabY = y + 46f;
            float tabW = (w - 56f) * 0.25f;
            string[] tabNames =
            {
                "1. Fare, Kamera & Ses",
                "2. Zorluk & Çevre Etkileri",
                "3. Oyun Çarpanları",
                "4. Kontroller & Tuşlar"
            };

            for (int t = 0; t < tabNames.Length; t++)
            {
                GUIStyle st = ActiveSettingsTab == t ? _activeTabButtonStyle! : _buttonStyle!;
                if (GUI.Button(new Rect(x + 16f + t * (tabW + 8f), tabY, tabW, 34f), tabNames[t], st))
                {
                    ActiveSettingsTab = t;
                    _waitingForRebindSlot = -1;
                }
            }

            Rect contentRect = new Rect(x + 22f, y + 92f, w - 44f, h - 150f);
            switch (ActiveSettingsTab)
            {
                case 0:
                    DrawMouseAndAudioTab(contentRect);
                    break;
                case 1:
                    DrawDifficultyAndEnvironmentTab(contentRect, creatureBridge, miningCtrl);
                    break;
                case 2:
                    DrawGameplayMultipliersTab(contentRect, creatureBridge);
                    break;
                case 3:
                    DrawControlsAndKeybindsTab(contentRect);
                    break;
            }

            // Alt Geri & Oyuna Dön Çubuğu
            float bottomY = y + h - 50f;
            if (GUI.Button(new Rect(x + 22f, bottomY, 220f, 38f), "⬅  Ana ESC Menüsüne Dön", _buttonStyle!))
            {
                IsInSettingsSubMenu = false;
                _waitingForRebindSlot = -1;
                Stage1GameSettings.SaveToPrefs();
                creatureBridge.ApplyLiveDifficultyMultipliers();
            }

            if (GUI.Button(new Rect(x + w - 242f, bottomY, 220f, 38f), "▶  Kaydet ve Oyuna Dön [ESC]", _activeTabButtonStyle!))
            {
                CloseMenuAndResume(creatureBridge);
            }
        }

        private void DrawMouseAndAudioTab(Rect area)
        {
            float cy = area.y;
            GUI.Label(new Rect(area.x, cy, area.width, 24f), "🖱️ FARE VE KAMERA AYARLARI", _subHeaderStyle!);
            cy += 30f;

            Stage1GameSettings.MouseSensitivity = DrawSliderRow(
                area.x, ref cy, area.width,
                "Fare Hassasiyeti (Mouse Sensitivity):",
                Stage1GameSettings.MouseSensitivity, 0.15f, 3.00f,
                $"{Stage1GameSettings.MouseSensitivity:0.00}x");

            Stage1GameSettings.CameraFieldOfView = DrawSliderRow(
                area.x, ref cy, area.width,
                "Kamera Görüş Açısı (FOV):",
                Stage1GameSettings.CameraFieldOfView, 60.0f, 105.0f,
                $"{Stage1GameSettings.CameraFieldOfView:0}°");

            Stage1GameSettings.RenderDistanceChunks = Mathf.RoundToInt(DrawSliderRow(
                area.x, ref cy, area.width,
                "Görüş Mesafesi (Render Distance):",
                Stage1GameSettings.RenderDistanceChunks, 6f, 24f,
                $"{Stage1GameSettings.RenderDistanceChunks} Chunk ({Stage1GameSettings.RenderDistanceChunks * 16}m)"));

            bool nextInvert = GUI.Toggle(
                new Rect(area.x, cy, 380f, 26f),
                Stage1GameSettings.InvertMouseY,
                "  Dikey Fare Eksenini Ters Çevir (Invert Mouse Y)");
            if (nextInvert != Stage1GameSettings.InvertMouseY)
            {
                Stage1GameSettings.InvertMouseY = nextInvert;
            }
            cy += 38f;

            GUI.Label(new Rect(area.x, cy, area.width, 24f), "🔊 SES SEVİYELERİ (AUDIO MIXER)", _subHeaderStyle!);
            cy += 30f;

            float prevMaster = Stage1GameSettings.MasterVolume;
            Stage1GameSettings.MasterVolume = DrawSliderRow(
                area.x, ref cy, area.width,
                "Ana Ses Seviyesi (Master Volume):",
                Stage1GameSettings.MasterVolume, 0.0f, 1.0f,
                $"%{Mathf.RoundToInt(Stage1GameSettings.MasterVolume * 100f)}");
            if (Mathf.Abs(prevMaster - Stage1GameSettings.MasterVolume) > 0.001f)
            {
                AudioListener.volume = Stage1GameSettings.MasterVolume;
            }

            Stage1GameSettings.CombatSfxVolume = DrawSliderRow(
                area.x, ref cy, area.width,
                "Savaş, Kılıç & Alet Sesleri (SFX Volume):",
                Stage1GameSettings.CombatSfxVolume, 0.0f, 1.0f,
                $"%{Mathf.RoundToInt(Stage1GameSettings.CombatSfxVolume * 100f)}");

            Stage1GameSettings.AmbientVolume = DrawSliderRow(
                area.x, ref cy, area.width,
                "Ortam, Rüzgar & Doğa Sesleri (Ambient Volume):",
                Stage1GameSettings.AmbientVolume, 0.0f, 1.0f,
                $"%{Mathf.RoundToInt(Stage1GameSettings.AmbientVolume * 100f)}");

            cy += 8f;
            GUI.Label(new Rect(area.x, cy, area.width, 22f), "🚀 PERFORMANS, CHUNK LOD & SÜRÜ OPTİMİZASYON SİSTEMLERİ", _subHeaderStyle!);
            cy += 26f;

            Stage1GameSettings.EnableSubChunkPartialMeshing = GUI.Toggle(
                new Rect(area.x, cy, 400f, 22f),
                Stage1GameSettings.EnableSubChunkPartialMeshing,
                "  16x16x16 Kübik Alt-Chunk Kısmi Örme (10x Hızlı Kazı)");
            Stage1GameSettings.EnableUndergroundCaveCulling = GUI.Toggle(
                new Rect(area.x + 410f, cy, 400f, 22f),
                Stage1GameSettings.EnableUndergroundCaveCulling,
                "  Yeraltı Mağara Gizleme (Yüzeydeyken Uzak Mağaraları Gizle)");
            cy += 24f;

            Stage1GameSettings.EnableDistantHorizonLod = GUI.Toggle(
                new Rect(area.x, cy, 400f, 22f),
                Stage1GameSettings.EnableDistantHorizonLod,
                "  Uzak Kıta LOD Kabuğu (Distant Horizons 64-Bit Sütun Özeti)");
            Stage1GameSettings.EnableBurstAndAsyncBakeMesh = GUI.Toggle(
                new Rect(area.x + 410f, cy, 400f, 22f),
                Stage1GameSettings.EnableBurstAndAsyncBakeMesh,
                "  Burst SIMD + PhysX BakeMesh & İndeksli Köşe Paylaşımı");
            cy += 24f;

            Stage1GameSettings.EnableFlowFieldAndSpatialBoids = GUI.Toggle(
                new Rect(area.x, cy, 520f, 22f),
                Stage1GameSettings.EnableFlowFieldAndSpatialBoids,
                "  Paylaşımlı 3B Akış Alanı (Flow Field) & O(1) Spatial Hash Sürü AI");
            cy += 28f;

            if (GUI.Button(new Rect(area.x, cy, 280f, 32f), "↺ Fare, Ses & Performans Sıfırla", _buttonStyle!))
            {
                Stage1GameSettings.MouseSensitivity = 1.00f;
                Stage1GameSettings.InvertMouseY = false;
                Stage1GameSettings.CameraFieldOfView = 75.0f;
                Stage1GameSettings.MasterVolume = 0.85f;
                Stage1GameSettings.CombatSfxVolume = 0.90f;
                Stage1GameSettings.AmbientVolume = 0.75f;
                Stage1GameSettings.EnableSubChunkPartialMeshing = true;
                Stage1GameSettings.EnableUndergroundCaveCulling = true;
                Stage1GameSettings.EnableDistantHorizonLod = true;
                Stage1GameSettings.EnableBurstAndAsyncBakeMesh = true;
                Stage1GameSettings.EnableFlowFieldAndSpatialBoids = true;
                AudioListener.volume = Stage1GameSettings.MasterVolume;
                Stage1GameSettings.SaveToPrefs();
            }
        }

        private void DrawDifficultyAndEnvironmentTab(
            Rect area,
            Stage1CreatureSimulationBridge creatureBridge,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            float cy = area.y;
            GUI.Label(new Rect(area.x, cy, area.width, 24f), "🌡️ ÇEVRE VE İKLİM ETKİLERİ (ÜŞÜME / SICAKLAMA / OKSİJEN)", _subHeaderStyle!);
            cy += 28f;

            string envStateText = Stage1GameSettings.EnableEnvironmentalEffects
                ? "✅ ÇEVRE ETKİLERİ: AÇIK (Biyom Sıcaklığı, Üşüme/Sıcaklama ve Oksijen Aktif — Karakter Asla Yere Düşüp Kilitlenmez)"
                : "🛡️ ÇEVRE ETKİLERİ: KAPALI (Üşüme, Sıcaklama, Boğulma ve Lav/Asit Hasarı Tamamen Devre Dışı)";
            if (GUI.Button(new Rect(area.x, cy, area.width, 40f), envStateText,
                    Stage1GameSettings.EnableEnvironmentalEffects ? _buttonStyle! : _activeTabButtonStyle!))
            {
                Stage1GameSettings.EnableEnvironmentalEffects = !Stage1GameSettings.EnableEnvironmentalEffects;
                Stage1GameSettings.SaveToPrefs();
                miningCtrl.LastFeedbackText = Stage1GameSettings.EnableEnvironmentalEffects
                    ? "Çevre Etkileri AÇILDI."
                    : "Çevre Etkileri KAPATILDI (Üşüme ve sıcaklama devre dışı).";
            }
            cy += 52f;

            GUI.Label(new Rect(area.x, cy, area.width, 24f),
                "⚔️ OYUN ZORLUK SEVİYESİ (Zorluk Arttıkça Canlıların Agresifliği, Canları ve Hasarları Artar)", _subHeaderStyle!);
            cy += 28f;

            string[] diffLabels =
            {
                "0: BARIŞÇIL\n(Saldırmaz)",
                "1: KOLAY\n(0.7x Güç)",
                "2: NORMAL\n(1.0x Standart)",
                "3: ZOR\n(1.5x Güç)",
                "4: KABUS\n(2.2x Güç)"
            };
            float btnW = (area.width - 32f) / 5f;
            for (int d = 0; d < 5; d++)
            {
                GUIStyle st = Stage1GameSettings.DifficultyPreset == d ? _activeTabButtonStyle! : _buttonStyle!;
                if (GUI.Button(new Rect(area.x + d * (btnW + 8f), cy, btnW, 44f), diffLabels[d], st))
                {
                    Stage1GameSettings.ApplyDifficultyPreset(d);
                    creatureBridge.ApplyLiveDifficultyMultipliers();
                    miningCtrl.LastFeedbackText = $"Zorluk Seviyesi Değiştirildi: {Stage1GameSettings.GetDifficultyDisplayName(d)}";
                }
            }
            cy += 58f;

            GUI.Label(new Rect(area.x, cy, area.width, 22f), "🎚️ CANLI DAVRANIŞ VE ZORLUK DETAY ÇARPANLARI (Anında Sahnedeki Canlılara Uygulanır)", _subHeaderStyle!);
            cy += 28f;

            float prevAggro = Stage1GameSettings.CreatureAggressionMultiplier;
            float prevHp = Stage1GameSettings.CreatureHealthMultiplier;
            float prevDmg = Stage1GameSettings.CreatureDamageMultiplier;

            Stage1GameSettings.CreatureAggressionMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Canlı Agresifliği (Algılama Menzili, Takip & Saldırı Hızı):",
                Stage1GameSettings.CreatureAggressionMultiplier, 0.00f, 3.00f,
                Stage1GameSettings.CreatureAggressionMultiplier <= 0.01f
                    ? "0.00x (BARIŞÇIL)"
                    : $"{Stage1GameSettings.CreatureAggressionMultiplier:0.00}x");

            Stage1GameSettings.CreatureHealthMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Canlıların Can (Max HP) Çarpanı:",
                Stage1GameSettings.CreatureHealthMultiplier, 0.25f, 4.00f,
                $"{Stage1GameSettings.CreatureHealthMultiplier:0.00}x");

            Stage1GameSettings.CreatureDamageMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Canlıların Saldırı Hasarı Çarpanı:",
                Stage1GameSettings.CreatureDamageMultiplier, 0.00f, 4.00f,
                $"{Stage1GameSettings.CreatureDamageMultiplier:0.00}x");

            if (Mathf.Abs(prevAggro - Stage1GameSettings.CreatureAggressionMultiplier) > 0.005f ||
                Mathf.Abs(prevHp - Stage1GameSettings.CreatureHealthMultiplier) > 0.005f ||
                Mathf.Abs(prevDmg - Stage1GameSettings.CreatureDamageMultiplier) > 0.005f)
            {
                Stage1GameSettings.DifficultyPreset = 5; // Özel
                creatureBridge.ApplyLiveDifficultyMultipliers();
            }

            cy += 8f;
            GUI.Box(new Rect(area.x, cy, area.width, 44f),
                "ℹ️ Not: Oyuncu açlık/susuzluk tüketimi bu aşamada pasif tutulmuştur. Üşüme veya sıcaklama oluştuğunda karakter asla yere düşüp kilitlenmez.",
                _controlRowStyle!);
        }

        private void DrawGameplayMultipliersTab(Rect area, Stage1CreatureSimulationBridge creatureBridge)
        {
            float cy = area.y;
            GUI.Label(new Rect(area.x, cy, area.width, 24f), "⚡ OYUN MEKANİK, SAVAŞ, KAZI VE FİZİK ÇARPANLARI", _subHeaderStyle!);
            cy += 28f;

            Stage1GameSettings.PlayerCombatDamageMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Oyuncu Kılıç / Silah Hasar Çarpanı:",
                Stage1GameSettings.PlayerCombatDamageMultiplier, 0.50f, 5.00f,
                $"{Stage1GameSettings.PlayerCombatDamageMultiplier:0.00}x");

            Stage1GameSettings.MiningSpeedMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Kazma / Balta / Kristal Kazı & Kırma Hızı Çarpanı:",
                Stage1GameSettings.MiningSpeedMultiplier, 0.50f, 5.00f,
                $"{Stage1GameSettings.MiningSpeedMultiplier:0.00}x");

            Stage1GameSettings.PlayerMoveSpeedMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Oyuncu Yürüme & Koşu Hızı Çarpanı:",
                Stage1GameSettings.PlayerMoveSpeedMultiplier, 0.50f, 2.50f,
                $"{Stage1GameSettings.PlayerMoveSpeedMultiplier:0.00}x");

            Stage1GameSettings.PlayerJumpMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Oyuncu Zıplama Yüksekliği Çarpanı:",
                Stage1GameSettings.PlayerJumpMultiplier, 0.50f, 2.50f,
                $"{Stage1GameSettings.PlayerJumpMultiplier:0.00}x");

            Stage1GameSettings.StaminaDrainMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Dayanıklılık (Stamina) Tüketim Çarpanı (0 = Sınırsız):",
                Stage1GameSettings.StaminaDrainMultiplier, 0.00f, 2.00f,
                Stage1GameSettings.StaminaDrainMultiplier <= 0.01f
                    ? "0.00x (SINIRSIZ STAMINA)"
                    : $"{Stage1GameSettings.StaminaDrainMultiplier:0.00}x");

            Stage1GameSettings.ToolDurabilityLossMultiplier = DrawSliderRow(
                area.x, ref cy, area.width,
                "Kazma / Balta Aşınma Çarpanı (0 = Kırılmaz Alet):",
                Stage1GameSettings.ToolDurabilityLossMultiplier, 0.00f, 2.00f,
                Stage1GameSettings.ToolDurabilityLossMultiplier <= 0.01f
                    ? "0.00x (KIRILMAZ ALET)"
                    : $"{Stage1GameSettings.ToolDurabilityLossMultiplier:0.00}x");

            cy += 12f;
            if (GUI.Button(new Rect(area.x, cy, 320f, 36f), "↺ Tüm Çarpanları Varsayılana (1.00x) Sıfırla", _buttonStyle!))
            {
                Stage1GameSettings.ResetMultipliersToDefault();
                creatureBridge.ApplyLiveDifficultyMultipliers();
            }
        }

        private void DrawControlsAndKeybindsTab(Rect area)
        {
            float cy = area.y;
            GUI.Label(new Rect(area.x, cy, area.width - 240f, 24f),
                "⌨️ DEĞİŞTİRİLEBİLİR KONTROL TUŞLARI (Değiştirmek İçin Butona Tıklayıp Yeni Tuşa Basın)", _subHeaderStyle!);

            if (GUI.Button(new Rect(area.x + area.width - 230f, cy - 2f, 230f, 28f), "↺ Varsayılan Tuşlara Sıfırla", _buttonStyle!))
            {
                _waitingForRebindSlot = -1;
                Stage1GameSettings.ResetKeybindsToDefault();
            }
            cy += 30f;

            // 2 sütunlu değiştirilebilir tuş dizilimi (10 adet temel aksiyon tuşu)
            float colW = (area.width - 18f) * 0.5f;
            DrawKeybindButton(area.x, cy, colW, 0, "3B Kumaş Çanta / Envanter:", Stage1GameSettings.KeyBackpack);
            DrawKeybindButton(area.x + colW + 18f, cy, colW, 1, "Madde Kristali (Vakum Modu):", Stage1GameSettings.KeyMatterCrystal);
            cy += 32f;

            DrawKeybindButton(area.x, cy, colW, 2, "Etkileşim (Sandık/Örs/Evcilleştir):", Stage1GameSettings.KeyInteract);
            DrawKeybindButton(area.x + colW + 18f, cy, colW, 3, "Dövüş Sanatı / Dojo Stili Değiştir:", Stage1GameSettings.KeyMartialStance);
            cy += 32f;

            DrawKeybindButton(area.x, cy, colW, 4, "Fiziksel Tekme / Kabza Darbesi:", Stage1GameSettings.KeyKickOrBash);
            DrawKeybindButton(area.x + colW + 18f, cy, colW, 5, "Aldatma İptali (Feint) / Boş El:", Stage1GameSettings.KeyFeintOrSheathe);
            cy += 32f;

            DrawKeybindButton(area.x, cy, colW, 6, "Silahı Yere At (0.0s Quick-Drop):", Stage1GameSettings.KeyQuickDrop);
            DrawKeybindButton(area.x + colW + 18f, cy, colW, 7, "Yüzüstü Sürünme (Prone):", Stage1GameSettings.KeyProne);
            cy += 32f;

            DrawKeybindButton(area.x, cy, colW, 8, "Atik Yan-Adım / Kaçış (Dodge):", Stage1GameSettings.KeyDodge);
            DrawKeybindButton(area.x + colW + 18f, cy, colW, 9, "Kamera Açısı (FPS / Omuz TPS):", Stage1GameSettings.KeyCameraToggle);
            cy += 38f;

            GUI.Label(new Rect(area.x, cy, area.width, 22f), "📖 TÜM OYUN KONTROLLERİ TAM REHBERİ", _subHeaderStyle!);
            cy += 24f;

            Rect scrollViewOuter = new Rect(area.x, cy, area.width, area.yMax - cy);
            string[] allControlLines =
            {
                "• [W / A / S / D] : Fiziksel Karakter Yürüme (4.0 m/s)  |  [Left Shift + WASD] : Depar / Koşu (7.0 m/s)",
                "• [Left Ctrl] : Çömelme (Crouch 1.15m)  |  [Depar + Left Ctrl] : Eğim Duyarlı Kayma (Slide 3.58m)",
                $"• [{Stage1GameSettings.KeyProne}] : Yüzüstü Sürünme (Prone 0.45m — 1x1m dar tünellerden geçiş)  |  [{Stage1GameSettings.KeyDodge}] : Yan-Adım Dodge Kaçışı",
                "• [Space] : Zıplama & 2.2m Çıkıntıya Tutunup Tırmanma (0.55s Kübik Bezier Mantle) & Suda Yüzme",
                "• [Sol Tık (LMB) Basılı Tut + Fareyi Savur] : Kılıç / Kazma [2] / Balta [3] Tam-Yay Balistik Savurma Vuruşu",
                "• [Sağ Tık (RMB) Basılı Tut + Fareyi Yönlendir] : Kılıç Yönlü Guard / Siper Blok & Son Anda Fare Savurarak Saptırma (Deflect)",
                "• [Fare Tekerleği İleri (ScrollUp)] : Savurma Başlangıcını Zırh Delici İleri Saplamaya (Thrust Morph) Dönüştür",
                $"• [{Stage1GameSettings.KeyFeintOrSheathe}] : Savurmayı İlk %35'te İptal Et (Feint) veya Silahı Kınına Koyup Boş Ele Geç",
                $"• [{Stage1GameSettings.KeyKickOrBash}] : Fiziksel Tekme (Düşman Bloğunu Yarar)  |  [Sağ Tık + {Stage1GameSettings.KeyKickOrBash}] : Kabza Darbesi (Pommel Bash)",
                $"• [1] (Tekrar Basış) veya [F8] : 4 Kılıç Arketipi Değiştir (Longsword, Greatsword, Rapier, Katana)",
                $"• [{Stage1GameSettings.KeyMartialStance}] : 4 Dövüş Sanatı / Dojo Stili Değiştir (Şövalye VomTag, Iaido, Eskrim Punta, Çift-El Juggernaut)",
                "• [1..9] veya [Fare Tekerleği] : Kemer/Sırt Kılıfı Seçimi (1:Kılıç, 2:Kazma, 3:Balta, 4:İnşa Bloğu, 5:Mikro-Keski, 6:Sıvı Matarası)",
                "• [0] veya [H] : Elleri Boşalt (Boş El Modu)",
                $"• [{Stage1GameSettings.KeyBackpack}] veya [Tab] : 3B Kumaş Çanta Örtüsü & 24+9 Sürükle-Bırak Envanter Aç/Kapat",
                $"• [{Stage1GameSettings.KeyMatterCrystal}] : Sol Bilek Madde Kristali Vakum/İnşa Modu Aç/Kapat (Sol Tık: Vakum Kazı, Sağ Tık: Blok İnşa)",
                "• [R] : İnşa Bloğu 24-Yön Döndürme  |  [B] : İnşa Malzemesi Seçimi  |  [Shift + Tekerlek] : Kazı Fırça Çapı",
                $"• [{Stage1GameSettings.KeyInteract}] : Yerdeki Eşyayı Al / Örs Onarımı / 3B CAS Sandık Aç / Köy-Tapınak Hazinesi / Yaralı Canlı Evcilleştir & Binek",
                $"• [{Stage1GameSettings.KeyQuickDrop}] veya [Shift + G] : Eldeki Silahı/Aleti 0.0sn Sürede Yere At (Quick-Drop)",
                $"• [{Stage1GameSettings.KeyCameraToggle}] : 1. Şahıs (FPS) <-> 3. Şahıs Omuz Üstü (TPS) Kamera Geçişi",
                "• [F1] : Tüm Arayüzü Gizle (Sinematik Mod)  |  [F3] : Geliştirici & Simülasyon Bilgi Yazılarını Gizle/Aç",
                "• [F6] : Dünyayı Kaydet (.sregion)  |  [Shift + F9] : Dünyayı Yükle (.sregion)  |  [F7] : Yeni Rastgele Harita Tohumu (Seed)",
                "• [F9] : Oyuncu Uzuv Kopması Testi  |  [F10] : Düellocu NPC & Kukla Çağır  |  [F11] : İklim Testi  |  [F12] : Biyom/Mağara Keşif Işınlanma"
            };

            float contentH = allControlLines.Length * 24f + 8f;
            _controlsScrollPos = GUI.BeginScrollView(scrollViewOuter, _controlsScrollPos, new Rect(0f, 0f, area.width - 24f, contentH));
            for (int i = 0; i < allControlLines.Length; i++)
            {
                GUI.Box(new Rect(0f, i * 24f, area.width - 24f, 22f), allControlLines[i], _controlRowStyle!);
            }
            GUI.EndScrollView();
        }

        private void DrawKeybindButton(float x, float y, float width, int slotIndex, string actionLabel, KeyCode currentKey)
        {
            float labelW = width * 0.64f;
            float btnW = width - labelW - 4f;
            GUI.Label(new Rect(x, y, labelW, 28f), actionLabel, _labelStyle!);

            bool waiting = _waitingForRebindSlot == slotIndex;
            string btnText = waiting ? ">> TUŞA BASIN... [ESC: İptal] <<" : $"[{currentKey}]";
            GUIStyle st = waiting ? _activeTabButtonStyle! : _buttonStyle!;
            if (GUI.Button(new Rect(x + labelW + 4f, y, btnW, 28f), btnText, st))
            {
                _waitingForRebindSlot = waiting ? -1 : slotIndex;
            }
        }

        private float DrawSliderRow(
            float x, ref float y, float totalWidth, string label, float value, float min, float max, string formattedValue)
        {
            float labelW = totalWidth * 0.48f;
            float valW = 135f;
            float sliderW = totalWidth - labelW - valW - 16f;

            GUI.Label(new Rect(x, y, labelW, 24f), label, _labelStyle!);
            float nextVal = GUI.HorizontalSlider(new Rect(x + labelW + 8f, y + 6f, sliderW, 18f), value, min, max);
            GUI.Label(new Rect(x + labelW + sliderW + 14f, y, valW, 24f), formattedValue, _valueLabelStyle!);
            y += 30f;
            return nextVal;
        }
    }
}
#endif
