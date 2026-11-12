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
        private readonly Mesh?[] _silhouetteMeshes = new Mesh?[4];

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
            Stage1WheelbarrowController? wheelbarrowCtrl = null)
        {
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

            // Madde Kristali KAPALIYKEN [T] tusu Zihin Insa Menusunu acar/kapatir!
            if (!miningCtrl.IsCrystalActive && Input.GetKeyDown(KeyCode.T))
            {
                if (IsMindMenuOpen)
                {
                    CloseMindMenu(restoreCursorLock: true);
                }
                else
                {
                    OpenMindMenu();
                }
                return;
            }

            if (!IsMindMenuOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseMindMenu(restoreCursorLock: true);
                return;
            }

            // Hizli klavye kisayollari: [1..4] dogrulama, [G] secili kartin 2.8 sn dusunce silueti ipucu
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.Campfire, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.CharcoalPit, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.BloomeryFurnace, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                ExecuteValidateAndBind(MindStructureBlueprintId.WoodenWheelbarrow, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl);
                return;
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                MindStructureBlueprintId bp = (MindStructureBlueprintId)Mathf.Clamp(_hoveredBlueprintIndex, 0, 3);
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
            Stage1WheelbarrowController? wheelbarrowCtrl = null)
        {
            EnsureGuiStyles();

            if (showHud && _toastRemainingSec > 0.01f && !string.IsNullOrEmpty(_toastMessage))
            {
                DrawToastOverlay();
            }

            if (!IsMindMenuOpen) return;

            float panelW = Mathf.Min(700f, Screen.width - 40f);
            float panelH = 408f;
            float px = (Screen.width - panelW) * 0.5f;
            float py = (Screen.height - panelH) * 0.5f;

            Color prevColor = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.11f, 0.94f);
            GUI.Box(new Rect(px, py, panelW, panelH), GUIContent.none, _panelStyle!);
            GUI.color = prevColor;

            GUI.Label(new Rect(px + 18f, py + 12f, panelW - 36f, 26f),
                "🧠 ZİHİN İNŞA FİKİRLERİ  ([T] / [ESC] Kapat)", _titleStyle!);
            GUI.Label(new Rect(px + 18f, py + 38f, panelW - 36f, 22f),
                "Önce malzemeleri yere [Sağ Tık] ile dizin -> Ardından buradan [Sol Tık / 1..4] ile kümenizi doğrulayın!  |  [Sağ Tık / G]: 2.8 sn Zihin Silüeti İpucu",
                _subTitleStyle!);

            float cardStartY = py + 68f;
            float cardH = 74f;
            float cardGap = 8f;
            Vector2 mousePos = Event.current.mousePosition;

            for (int i = 0; i < 4; i++)
            {
                MindStructureBlueprintId bp = (MindStructureBlueprintId)i;
                Rect cardRect = new Rect(px + 18f, cardStartY + i * (cardH + cardGap), panelW - 36f, cardH);
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
                    _ => "🛞"
                };

                string header = $"[{i + 1}] {icon} {MindStructureClusterValidator.GetDisplayName(bp)}";
                string desc = MindStructureClusterValidator.GetShortDescription(bp);

                GUI.Label(new Rect(cardRect.x + 14f, cardRect.y + 8f, cardRect.width - 190f, 24f), header, _cardTitleStyle!);
                GUI.Label(new Rect(cardRect.x + 14f, cardRect.y + 32f, cardRect.width - 28f, 36f), desc, _cardDescStyle!);

                GUI.Label(new Rect(cardRect.xMax - 180f, cardRect.y + 8f, 168f, 22f),
                    hovered ? "[Sol Tık] Doğrula | [Sağ Tık] İpucu" : $"[{i + 1}] Seç",
                    _subTitleStyle!);

                if (hovered && Event.current.type == EventType.MouseDown)
                {
                    if (Event.current.button == 0)
                    {
                        Event.current.Use();
                        ExecuteValidateAndBind(bp, cam, playerTransform, miningCtrl, looseBuildSystem, campfireCtrl, worldChunks, sharedVoxelMat, bloomeryCtrl, wheelbarrowCtrl);
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
            Stage1WheelbarrowController? wheelbarrowCtrl = null)
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
            byte chalkBlue = MaterialIdCatalog.AstralOre;
            byte chalkGlow = MaterialIdCatalog.QuartzGem;

            switch (blueprint)
            {
                case MindStructureBlueprintId.Campfire:
                    // Alt kav halkasi + 5 konik dal cizgisi + dis tas cember noktalari
                    EmitRingStrips(new SysVec3(0f, 0.02f, 0f), 0.16f, 8, 0.010f, chalkGlow, 35, _silhouetteTris);
                    for (int s = 0; s < 5; s++)
                    {
                        float a = (s / 5f) * MathF.PI * 2f;
                        SysVec3 p0 = new SysVec3(MathF.Cos(a) * 0.24f, 0.02f, MathF.Sin(a) * 0.24f);
                        SysVec3 p1 = new SysVec3(MathF.Cos(a) * 0.03f, 0.34f, MathF.Sin(a) * 0.03f);
                        EmitThinLineBar(p0, p1, 0.012f, chalkBlue, 20, _silhouetteTris);
                    }
                    EmitRingStrips(new SysVec3(0f, 0.04f, 0f), 0.42f, 8, 0.014f, chalkBlue, 15, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.CharcoalPit:
                    // Ic odun yigini + dis kubbe toprak kaplama halkalari
                    EmitRingStrips(new SysVec3(0f, 0.05f, 0f), 0.48f, 10, 0.014f, chalkBlue, 20, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.24f, 0f), 0.38f, 8, 0.014f, chalkBlue, 20, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.42f, 0f), 0.22f, 6, 0.014f, chalkGlow, 30, _silhouetteTris);
                    break;

                case MindStructureBlueprintId.BloomeryFurnace:
                    // Silindirik 1m baca + alt yan tuyere hava borusu cizgisi
                    EmitRingStrips(new SysVec3(0f, 0.06f, 0f), 0.38f, 10, 0.015f, chalkBlue, 20, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.48f, 0f), 0.34f, 10, 0.015f, chalkBlue, 20, _silhouetteTris);
                    EmitRingStrips(new SysVec3(0f, 0.92f, 0f), 0.29f, 10, 0.015f, chalkGlow, 35, _silhouetteTris);
                    for (int v = 0; v < 4; v++)
                    {
                        float a = (v / 4f) * MathF.PI * 2f;
                        EmitThinLineBar(
                            new SysVec3(MathF.Cos(a) * 0.38f, 0.06f, MathF.Sin(a) * 0.38f),
                            new SysVec3(MathF.Cos(a) * 0.29f, 0.92f, MathF.Sin(a) * 0.29f),
                            0.012f, chalkBlue, 20, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(0.32f, 0.14f, 0f), new SysVec3(0.58f, 0.14f, 0f), 0.022f, chalkGlow, 45, _silhouetteTris);
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
                            0.014f, chalkGlow, 35, _silhouetteTris);
                    }
                    EmitThinLineBar(new SysVec3(-0.08f, 0.22f, 0.46f), new SysVec3(-0.30f, 0.46f, -0.55f), 0.014f, chalkBlue, 20, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(0.08f, 0.22f, 0.46f), new SysVec3(0.30f, 0.46f, -0.55f), 0.014f, chalkBlue, 20, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-0.24f, 0.36f, 0.22f), new SysVec3(0.24f, 0.36f, 0.22f), 0.014f, chalkBlue, 20, _silhouetteTris);
                    EmitThinLineBar(new SysVec3(-0.26f, 0.40f, -0.25f), new SysVec3(0.26f, 0.40f, -0.25f), 0.014f, chalkBlue, 20, _silhouetteTris);
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
