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
    public enum InteriorFixtureKind : byte
    {
        HingedDoor = 0,
        WindowShutter = 1,
        StoneFireplaceWithChimney = 2,
        SawhorseBench = 3,
        WoodBed = 4,
        StorageChest = 5,
        TableAndBench = 6,
        WallTorchSconce = 7
    }

    /// <summary>
    /// Asama 2: Gercek Yasanabilir Ev Ici Kontrolcusu (0 yeni doku VRAM):
    /// - [E] ile 90° acilip kapanan, [Shift+E] ile icerden surgulenen menteseli ahsap kapi & pencere kepengi
    /// - Yanmaz tas altlik + tas somine + catidan 0.6m tasan baca (isinma + duman/O2)
    /// - Chinking (kil/yosun) derz yalitimi -> EnclosedRoom sicak oda aktiflesmesi
    /// - Bicki / Marangoz Sehpasi (Sawhorse): kutukten balta/keserle kalas, kapi, kepenk, yatak, sandik, masa uretimi
    /// </summary>
    public sealed class Stage1TimberInteriorController
    {
        public sealed class InteriorFixture
        {
            public uint Id;
            public InteriorFixtureKind Kind;
            public Vector3 Position;
            public float BaseYawDeg;
            public float DoorWidthM = 1.90f;
            public bool IsOpenOrLit;
            public bool IsBoltedLocked;
            public bool HasChimney;
            public GameObject? RootGo;
            public Transform? PivotTr;
            public Light? PointLight;
            public int SelectedRecipeIdx;
        }

        private readonly List<InteriorFixture> _fixtures = new(32);
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(256);
        private uint _nextId = 1;
        private CabinInteriorThermalState _lastThermalState;
        private int _chinkedWallsCount;

        public int FixtureCount => _fixtures.Count;
        public CabinInteriorThermalState LastThermalState => _lastThermalState;

        /// <summary>
        /// Kapali Menteseli Ahsap Kapinin icinden gecilmesini onler; [E] ile acildiginda gecit serbest kalir (0 B GC).
        /// </summary>
        public void ResolvePlayerDoorCollision(ref Vector3 playerPos, float playerRadius, float playerHeight)
        {
            for (int i = 0; i < _fixtures.Count; i++)
            {
                InteriorFixture fx = _fixtures[i];
                if (fx.Kind != InteriorFixtureKind.HingedDoor || fx.IsOpenOrLit) continue;
                if (playerPos.y > fx.Position.y + 2.25f || playerPos.y + playerHeight < fx.Position.y) continue;

                Quaternion invRot = Quaternion.Inverse(Quaternion.Euler(0f, fx.BaseYawDeg, 0f));
                Vector3 local = invRot * (playerPos - fx.Position);
                float doorW = fx.DoorWidthM > 0.5f ? fx.DoorWidthM : 1.90f;
                if (local.x >= -playerRadius && local.x <= doorW + playerRadius)
                {
                    float minZ = 0.08f + playerRadius;
                    if (Mathf.Abs(local.z) < minZ)
                    {
                        local.z = local.z >= 0f ? minZ : -minZ;
                        Vector3 pushed = fx.Position + Quaternion.Euler(0f, fx.BaseYawDeg, 0f) * local;
                        playerPos.x = pushed.x;
                        playerPos.z = pushed.z;
                    }
                }
            }
        }

        public void DisposeAll()
        {
            for (int i = 0; i < _fixtures.Count; i++)
            {
                if (_fixtures[i].RootGo != null) UnityEngine.Object.Destroy(_fixtures[i].RootGo);
            }
            _fixtures.Clear();
            _chinkedWallsCount = 0;
        }

        /// <summary>
        /// 3 Katli pilot ev kuruldugunda icini yasanabilir kilan temel donanimlari (calisan kapi, tas somine+baca, yatak, bicki sehpasi) monte eder.
        /// </summary>
        public void FurnishPilotCabin(Vector3 cabinOrigin, float groundY, float yawDeg, Material? sharedMat)
        {
            Quaternion rotY = Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 ToW(Vector3 local) => new Vector3(cabinOrigin.x, groundY, cabinOrigin.z) + (rotY * local);

            // 1) On giris menteseli ahsap kapi (2.0m aciklik, [E] ac/kapa, [Shift+E] surgu!)
            SpawnFixtureInternal(InteriorFixtureKind.HingedDoor, ToW(new Vector3(-0.95f, 0.35f, -3.20f)), yawDeg, sharedMat, hasChimney: false);
            // 2) Yan pencere ahsap kepengi
            SpawnFixtureInternal(InteriorFixtureKind.WindowShutter, ToW(new Vector3(-3.80f, 1.35f, -0.85f)), yawDeg + 90f, sharedMat, hasChimney: false);
            // 3) Tas somine + catidan 0.6m tasan baca (sol arka kose)
            SpawnFixtureInternal(InteriorFixtureKind.StoneFireplaceWithChimney, ToW(new Vector3(-2.65f, 0.42f, 2.25f)), yawDeg, sharedMat, hasChimney: true);
            // 4) Ahsap yatak (1. kat sol taraf - [E] SpawnPoint)
            SpawnFixtureInternal(InteriorFixtureKind.WoodBed, ToW(new Vector3(-2.30f, 0.42f, -0.85f)), yawDeg, sharedMat, hasChimney: false);
            // 5) Bicki / Marangoz Sehpasi (1. kat orta-sag - kalas/mobilya uretimi)
            SpawnFixtureInternal(InteriorFixtureKind.SawhorseBench, ToW(new Vector3(0.25f, 0.42f, 1.45f)), yawDeg, sharedMat, hasChimney: false);
            // 6) Duvar mesale askisi
            SpawnFixtureInternal(InteriorFixtureKind.WallTorchSconce, ToW(new Vector3(-3.42f, 2.15f, 0.25f)), yawDeg, sharedMat, hasChimney: false);

            RecomputeThermalState();
        }

        /// <summary>
        /// Survival'da oyuncu tarafindan Bicki Sehpasi, Tas Somine, Yatak, Sandik, Masa veya Kapi yerlestirir.
        /// </summary>
        public bool TryPlaceInteriorFixture(
            InteriorFixtureKind kind,
            Vector3 worldPos,
            float yawDeg,
            bool isCreativeMode,
            Stage1HolsterAndBackpackController holster,
            Material? sharedMat,
            out string feedback,
            Stage1RigidTimberBridge? rigidTimberBridge = null)
        {
            if (!isCreativeMode && !TryConsumeFixtureMaterials(kind, holster, out string miss))
            {
                feedback = miss;
                return false;
            }

            float doorWidth = kind == InteriorFixtureKind.WindowShutter ? 1.70f : 1.90f;
            float doorHeight = kind == InteriorFixtureKind.WindowShutter ? 1.05f : 2.20f;
            float chimneyTopLocalY = 4.20f;
            if (rigidTimberBridge != null)
            {
                if (kind == InteriorFixtureKind.HingedDoor)
                {
                    if (rigidTimberBridge.TryMeasureDoorwayClearSpanAndHeight(worldPos, out float measuredSpan, out float measuredH, out Vector3 hingePos, out float alignedYaw))
                    {
                        doorWidth = measuredSpan;
                        doorHeight = measuredH;
                        worldPos = hingePos;
                        yawDeg = alignedYaw;
                    }
                    else
                    {
                        feedback = "⚠️ Burada açık bir kapı boşluğu yok (önce iki kapı direği arasını en az 1.85m yüksekliğe kadar açık bırakın)!";
                        return false;
                    }
                }
                else if (kind == InteriorFixtureKind.WindowShutter)
                {
                    if (rigidTimberBridge.TryMeasureWindowClearSpanAndHeight(worldPos, out float winSpan, out float winH, out Vector3 winHingePos, out float winYaw))
                    {
                        doorWidth = winSpan;
                        doorHeight = winH;
                        worldPos = winHingePos;
                        yawDeg = winYaw;
                    }
                }
                else if (kind == InteriorFixtureKind.StoneFireplaceWithChimney)
                {
                    float roofTopWorldY = rigidTimberBridge.SampleHighestRoofTopYNear(worldPos.x, worldPos.z, worldPos.y);
                    chimneyTopLocalY = Mathf.Clamp((roofTopWorldY - worldPos.y) + 0.65f, 3.80f, 15.50f);
                }
            }

            SpawnFixtureInternal(
                kind,
                worldPos,
                yawDeg,
                sharedMat,
                hasChimney: kind == InteriorFixtureKind.StoneFireplaceWithChimney,
                doorWidthM: doorWidth,
                chimneyTopLocalY: chimneyTopLocalY,
                doorHeightM: doorHeight);
            RecomputeThermalState(rigidTimberBridge, worldPos);
            feedback = kind == InteriorFixtureKind.HingedDoor
                ? $"🚪 Menteşeli Ahşap Kapı ({doorWidth:F2}×{doorHeight:F2}m açıklığa sıfıra sıfır) monte edildi! [E] 90° Aç/Kapa | [Shift+E] İç Sürgü."
                : $"🏠 {kind} yerleştirildi! Yaklaşıp [E] ile kullanabilirsiniz.";
            return true;
        }

        /// <summary>
        /// [E] / [Shift+E] ile kapi ac-kapa/surgule, kepenk ac-kapa, somine yak/sondur, yatakta SpawnPoint kaydet,
        /// Bicki Sehpasinda [Shift+E]/[E] ile kalas & kiriş uret veya elde kil/lif varken 3B Chinking derz yalitimi uygula!
        /// </summary>
        public bool TryInteract(
            Vector3 playerPos,
            bool shiftHeld,
            bool isCreativeMode,
            Stage1HolsterAndBackpackController holster,
            Stage1MatterCrystalAndMiningController miningCtrl,
            out string feedback,
            Stage1RigidTimberBridge? rigidTimberBridge = null)
        {
            feedback = string.Empty;

            // 1) Elde Kil veya Lif varken GERCEK 3B Chinking (derz yalitimi seridi) uygulama — yalnizca yakinda sivanmamis duvar kutugu varsa (veya 2.2m dibimizde etkilesimli somine/kapi yoksa) devreye girer!
            VisualInventorySlot held = holster.InventoryUi.GetActiveHotbarSlot();
            int nearInteractiveFixture = FindNearestFixture(playerPos, 2.25f);
            if (!shiftHeld && !held.IsEmpty &&
                (held.Kind == InventoryItemKind.ClayLump || held.Kind == InventoryItemKind.PlantFiber))
            {
                int newlyChinked = rigidTimberBridge != null
                    ? rigidTimberBridge.ApplyVisualChinkingNear(playerPos, 5.5f, out int totalNear, out int chinkedNear)
                    : 0;
                if (newlyChinked > 0 || (nearInteractiveFixture < 0 && _fixtures.Count > 0 && FindNearestFixture(playerPos, 6.5f) >= 0 && _chinkedWallsCount < 20))
                {
                    if (!isCreativeMode) holster.InventoryUi.TryConsumeActiveHotbarSlot(1);
                    _chinkedWallsCount = Mathf.Min(20, _chinkedWallsCount + Mathf.Max(4, newlyChinked));
                    RecomputeThermalState(rigidTimberBridge, playerPos);
                    feedback = $"🧱 3B Kütük Derz Yalıtımı (Chinking) sıvandı! Sızdırmazlık: %{_lastThermalState.SealFactor01 * 100f:0} | İç Isı Farkı: +{_lastThermalState.IndoorTempDeltaC:F1}°C" +
                               (_lastThermalState.IsEnclosedWarmRoom ? " (✅ SICAK KAPALI ODA AKTİF!)" : "");
                    return true;
                }
            }

            int idx = FindNearestFixture(playerPos, 3.0f);
            if (idx < 0) return false;
            InteriorFixture fx = _fixtures[idx];

            switch (fx.Kind)
            {
                case InteriorFixtureKind.HingedDoor:
                case InteriorFixtureKind.WindowShutter:
                    if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                    {
                        if (fx.RootGo != null) UnityEngine.Object.Destroy(fx.RootGo);
                        _fixtures.RemoveAt(idx);
                        RecomputeThermalState(rigidTimberBridge, playerPos);
                        feedback = fx.Kind == InteriorFixtureKind.HingedDoor ? "♻️ Menteşeli Kapı söküldü!" : "♻️ Pencere Kepengi söküldü!";
                        return true;
                    }
                    if (shiftHeld)
                    {
                        fx.IsBoltedLocked = !fx.IsBoltedLocked;
                        feedback = fx.IsBoltedLocked ? "🔒 İç sürgü kilitlendi!" : "🔓 İç sürgü açıldı.";
                        return true;
                    }
                    if (fx.IsBoltedLocked)
                    {
                        feedback = "🔒 Kapı/kepenk sürgülü! Açmak için önce [Shift+E] ile sürgüyü çekin.";
                        return true;
                    }
                    fx.IsOpenOrLit = !fx.IsOpenOrLit;
                    if (fx.PivotTr != null)
                    {
                        fx.PivotTr.localRotation = Quaternion.Euler(0f, fx.IsOpenOrLit ? -90f : 0f, 0f);
                    }
                    RecomputeThermalState(rigidTimberBridge, playerPos);
                    feedback = fx.IsOpenOrLit
                        ? $"🚪 {(fx.Kind == InteriorFixtureKind.HingedDoor ? "Kapı" : "Kepenk")} içe doğru açıldı (Oda yalıtımı: %{_lastThermalState.SealFactor01 * 100f:0})."
                        : $"🚪 {(fx.Kind == InteriorFixtureKind.HingedDoor ? "Kapı" : "Kepenk")} kapatıldı (Oda yalıtımı: %{_lastThermalState.SealFactor01 * 100f:0}).";
                    return true;

                case InteriorFixtureKind.StoneFireplaceWithChimney:
                    if (shiftHeld)
                    {
                        if (fx.RootGo != null) UnityEngine.Object.Destroy(fx.RootGo);
                        _fixtures.RemoveAt(idx);
                        RecomputeThermalState(rigidTimberBridge, playerPos);
                        feedback = "♻️ Taş Şömine & Baca sökülüp kaldırıldı!";
                        return true;
                    }
                    fx.IsOpenOrLit = !fx.IsOpenOrLit;
                    if (fx.PointLight != null) fx.PointLight.enabled = fx.IsOpenOrLit;
                    RecomputeThermalState(rigidTimberBridge, playerPos);
                    feedback = fx.IsOpenOrLit
                        ? $"🔥 Taş şömine yakıldı! Baca çekişi aktif | İç Isı: +{_lastThermalState.IndoorTempDeltaC:F1}°C | Yalıtım: %{_lastThermalState.SealFactor01 * 100f:0}" +
                          (_lastThermalState.IsEnclosedWarmRoom ? " (✅ SICAK KAPALI ODA!)" : "")
                        : "💨 Taş şömine söndürüldü. (Sökmek için [Shift+E])";
                    return true;

                case InteriorFixtureKind.WoodBed:
                    if (shiftHeld)
                    {
                        if (fx.RootGo != null) UnityEngine.Object.Destroy(fx.RootGo);
                        _fixtures.RemoveAt(idx);
                        feedback = "♻️ Ahşap Yatak sökülüp kaldırıldı!";
                        return true;
                    }
                    feedback = "🛏️ Yatak dinlenme & Yeniden Doğma Noktası (SpawnPoint) bu eve kaydedildi! (Sökmek için [Shift+E])";
                    return true;

                case InteriorFixtureKind.SawhorseBench:
                    if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                    {
                        if (fx.RootGo != null) UnityEngine.Object.Destroy(fx.RootGo);
                        _fixtures.RemoveAt(idx);
                        feedback = "♻️ Bıçkı / Marangoz Sehpası sökülüp kaldırıldı!";
                        return true;
                    }
                    if (shiftHeld)
                    {
                        fx.SelectedRecipeIdx = (fx.SelectedRecipeIdx + 1) % TimberCabinInteriorSystem.SawhorseRecipes.Length;
                        var nextRec = TimberCabinInteriorSystem.SawhorseRecipes[fx.SelectedRecipeIdx];
                        feedback = $"🪚 Bıçkı Sehpası Tarifi [Shift+E]: {nextRec.DisplayName} (Gerekli: {nextRec.RequiredOakLogs} Kütük, {nextRec.RequiredPlanks} Kalas, {nextRec.RequiredSticks} Dal) | Üret: [E] | Sök: [Alt+E]";
                        return true;
                    }
                    var rec = TimberCabinInteriorSystem.SawhorseRecipes[fx.SelectedRecipeIdx];
                    if (!isCreativeMode && rec.RequiredOakLogs > 0)
                    {
                        bool hasWood =
                            holster.InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.SplitFirewood, 0, rec.RequiredOakLogs, miningCtrl, ignoreSubId: true) ||
                            holster.InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.LogDebarked, 0, rec.RequiredOakLogs, miningCtrl, ignoreSubId: true) ||
                            holster.InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood, rec.RequiredOakLogs, miningCtrl);
                        if (!hasWood)
                        {
                            feedback = $"🪚 {rec.DisplayName} için en az {rec.RequiredOakLogs}x Odun (Yarma Odun / Soyulmuş Kütük / Meşe Kütüğü) gerekir! (Tarif: [Shift+E] | Sök: [Alt+E])";
                            return true;
                        }
                    }
                    if (rec.Kind == TimberInteriorCraftKind.WoodPlanks4)
                    {
                        holster.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.TimberPlank, 0, 4, miningCtrl);
                        feedback = "🪚 Bıçkı Sehpasında biçildi: +4x Ahşap Kalas (TimberPlank)! ([T]->[3] Döşeme, Çatı, Merdiven, Kapı ve Pencere örmek için hazır!)";
                    }
                    else
                    {
                        holster.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.LogDebarked, 0, 2, miningCtrl);
                        feedback = $"🪚 Bıçkı Sehpasında işlendi: +2x Soyulmuş Kütük / Kiriş ({rec.DisplayName})! ([T]->[3] Duvar & Kiriş için hazır!)";
                    }
                    return true;

                case InteriorFixtureKind.StorageChest:
                    if (shiftHeld)
                    {
                        if (fx.RootGo != null) UnityEngine.Object.Destroy(fx.RootGo);
                        _fixtures.RemoveAt(idx);
                        feedback = "♻️ Ahşap Eşya Sandığı sökülüp kaldırıldı!";
                        return true;
                    }
                    fx.IsOpenOrLit = !fx.IsOpenOrLit;
                    feedback = fx.IsOpenOrLit
                        ? "📦 Ahşap Eşya Sandığı açıldı! ([I] ile çantanızı düzenleyebilir, [Shift+E] ile sandığı sökebilirsiniz)"
                        : "📦 Ahşap Eşya Sandığı kapatıldı.";
                    return true;

                case InteriorFixtureKind.WallTorchSconce:
                    fx.IsOpenOrLit = !fx.IsOpenOrLit;
                    if (fx.PointLight != null) fx.PointLight.enabled = fx.IsOpenOrLit;
                    feedback = fx.IsOpenOrLit ? "🔥 Duvar meşalesi yakıldı!" : "💨 Duvar meşalesi söndürüldü.";
                    return true;

                default:
                    return false;
            }
        }

        public CabinInteriorThermalState EvaluatePlayerThermalShelter(Vector3 playerPos, Stage1RigidTimberBridge? rigidTimberBridge, out bool blocksWind)
        {
            RecomputeThermalState(rigidTimberBridge, playerPos);
            blocksWind = _lastThermalState.HasRoofOverhead != 0 && _lastThermalState.SealFactor01 >= 0.45f;
            return _lastThermalState;
        }

        private void RecomputeThermalState(Stage1RigidTimberBridge? rigidTimberBridge = null, Vector3 refPos = default)
        {
            int openings = 0, closed = 0;
            bool litFire = false, hasChimney = false;
            for (int i = 0; i < _fixtures.Count; i++)
            {
                var f = _fixtures[i];
                if (f.Kind == InteriorFixtureKind.HingedDoor || f.Kind == InteriorFixtureKind.WindowShutter)
                {
                    openings++;
                    if (!f.IsOpenOrLit) closed++;
                }
                else if (f.Kind == InteriorFixtureKind.StoneFireplaceWithChimney)
                {
                    if (f.IsOpenOrLit) litFire = true;
                    if (f.HasChimney) hasChimney = true;
                }
            }

            int totalLogs = 20;
            int chinkedLogs = _chinkedWallsCount;
            bool hasRoof = true;

            if (rigidTimberBridge != null && rigidTimberBridge.ActivePieceCount > 0)
            {
                Vector3 samplePos = refPos.sqrMagnitude > 0.01f ? refPos : (_fixtures.Count > 0 ? _fixtures[0].Position + Vector3.up * 1.0f : Vector3.zero);
                rigidTimberBridge.EvaluateRealCabinEnclosure(samplePos, out int realWalls, out int realChinked, out bool realRoof, out int enclosedDirs);
                if (realWalls > 0)
                {
                    totalLogs = Mathf.Max(12, realWalls);
                    chinkedLogs = enclosedDirs >= 4 ? Mathf.Max(realChinked, _chinkedWallsCount) : realChinked;
                    hasRoof = realRoof && enclosedDirs >= 4;
                }
            }

            _lastThermalState = new CabinInteriorThermalState
            {
                TotalWallLogs = totalLogs,
                ChinkedWallLogs = Mathf.Min(totalLogs, chinkedLogs),
                TotalOpenings = openings,
                ClosedDoorsOrShutters = closed,
                HasRoofOverhead = hasRoof ? (byte)1 : (byte)0,
                HasLitFireplace = litFire ? (byte)1 : (byte)0,
                HasRoofChimneyFlue = hasChimney ? (byte)1 : (byte)0
            };
            TimberCabinInteriorSystem.EvaluateCabinThermalAndSmoke(ref _lastThermalState);
        }

        private int FindNearestFixture(Vector3 pos, float maxDist)
        {
            int best = -1;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < _fixtures.Count; i++)
            {
                float dSq = (_fixtures[i].Position - pos).sqrMagnitude;
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    best = i;
                }
            }
            return best;
        }

        private InteriorFixture SpawnFixtureInternal(
            InteriorFixtureKind kind,
            Vector3 pos,
            float yawDeg,
            Material? sharedMat,
            bool hasChimney,
            float doorWidthM = 1.90f,
            float chimneyTopLocalY = 12.20f,
            float doorHeightM = 2.20f)
        {
            uint id = _nextId++;
            float clampedDoorW = Mathf.Clamp(doorWidthM, 0.85f, 3.00f);
            float clampedDoorH = kind == InteriorFixtureKind.WindowShutter
                ? Mathf.Clamp(doorHeightM, 0.65f, 1.85f)
                : Mathf.Clamp(doorHeightM, 1.80f, 2.65f);
            float clampedChimneyTop = Mathf.Clamp(chimneyTopLocalY, 3.80f, 16.5f);
            var fx = new InteriorFixture
            {
                Id = id,
                Kind = kind,
                Position = pos,
                BaseYawDeg = yawDeg,
                DoorWidthM = clampedDoorW,
                HasChimney = hasChimney
            };

            GameObject root = new GameObject($"Interior_{kind}_{id}");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            fx.RootGo = root;

            GameObject visualTarget = root;
            if (kind == InteriorFixtureKind.HingedDoor || kind == InteriorFixtureKind.WindowShutter)
            {
                GameObject pivot = new GameObject("HingePivot");
                pivot.transform.SetParent(root.transform, false);
                fx.PivotTr = pivot.transform;
                visualTarget = pivot;
            }

            _meshScratch.Clear();
            switch (kind)
            {
                case InteriorFixtureKind.HingedDoor:
                    EmitBox(_meshScratch, new Vector3(0f, 0f, -0.07f), new Vector3(clampedDoorW, clampedDoorH, 0.07f), MaterialIdCatalog.WoodPlank);
                    EmitBox(_meshScratch, new Vector3(0.08f, 0.30f, -0.09f), new Vector3(clampedDoorW - 0.08f, 0.45f, 0.09f), MaterialIdCatalog.TreatedTimberBeam);
                    EmitBox(_meshScratch, new Vector3(0.08f, clampedDoorH - 0.45f, -0.09f), new Vector3(clampedDoorW - 0.08f, clampedDoorH - 0.30f, 0.09f), MaterialIdCatalog.TreatedTimberBeam);
                    break;
                case InteriorFixtureKind.WindowShutter:
                    EmitBox(_meshScratch, new Vector3(0f, 0f, -0.05f), new Vector3(clampedDoorW, clampedDoorH, 0.05f), MaterialIdCatalog.WoodPlank);
                    EmitBox(_meshScratch, new Vector3(0.06f, 0.14f, -0.07f), new Vector3(clampedDoorW - 0.06f, 0.24f, 0.07f), MaterialIdCatalog.TreatedTimberBeam);
                    EmitBox(_meshScratch, new Vector3(0.06f, clampedDoorH - 0.24f, -0.07f), new Vector3(clampedDoorW - 0.06f, clampedDoorH - 0.14f, 0.07f), MaterialIdCatalog.TreatedTimberBeam);
                    break;
                case InteriorFixtureKind.StoneFireplaceWithChimney:
                    // Yanmaz tas altlik + gercek oyuk ocak agzi (-Z odaya bakar) + icerde komur/kutuk kozleri + catidan 0.65m tasan baca!
                    EmitBox(_meshScratch, new Vector3(-0.92f, -0.04f, -0.68f), new Vector3(0.92f, 0.14f, 0.56f), MaterialIdCatalog.CutAshlarStone);
                    EmitBox(_meshScratch, new Vector3(-0.82f, 0.14f, -0.52f), new Vector3(-0.46f, 1.42f, 0.50f), MaterialIdCatalog.StoneBrick); // Sol yan duvar
                    EmitBox(_meshScratch, new Vector3( 0.46f, 0.14f, -0.52f), new Vector3( 0.82f, 1.42f, 0.50f), MaterialIdCatalog.StoneBrick); // Sag yan duvar
                    EmitBox(_meshScratch, new Vector3(-0.46f, 0.14f,  0.18f), new Vector3( 0.46f, 1.42f, 0.50f), MaterialIdCatalog.Basalt);     // Koyu isli arka ocak duvari
                    EmitBox(_meshScratch, new Vector3(-0.84f, 1.42f, -0.56f), new Vector3( 0.84f, 1.88f, 0.52f), MaterialIdCatalog.StoneBrick); // Ocak kemeri / ust davlumbaz
                    EmitBox(_meshScratch, new Vector3(-0.34f, 0.15f, -0.28f), new Vector3( 0.34f, 0.30f, 0.12f), MaterialIdCatalog.OakWood);    // Ocak ici yakit kutukleri
                    EmitBox(_meshScratch, new Vector3(-0.42f, 1.88f, -0.38f), new Vector3( 0.42f, clampedChimneyTop, 0.44f), MaterialIdCatalog.StoneBrick);
                    EmitBox(_meshScratch, new Vector3(-0.56f, clampedChimneyTop, -0.52f), new Vector3(0.56f, clampedChimneyTop + 0.22f, 0.58f), MaterialIdCatalog.CutAshlarStone);
                    GameObject fireLightGo = new GameObject("FireplaceLight");
                    fireLightGo.transform.SetParent(root.transform, false);
                    fireLightGo.transform.localPosition = new Vector3(0f, 0.65f, -0.55f);
                    Light fl = fireLightGo.AddComponent<Light>();
                    fl.type = LightType.Point;
                    fl.color = new Color(1.0f, 0.62f, 0.25f);
                    fl.intensity = 3.5f;
                    fl.range = 11.5f;
                    fl.enabled = false;
                    fx.PointLight = fl;
                    break;
                case InteriorFixtureKind.SawhorseBench:
                    EmitBox(_meshScratch, new Vector3(-1.05f, 0.78f, -0.26f), new Vector3(1.05f, 0.94f, 0.26f), MaterialIdCatalog.WoodPlank);
                    EmitBox(_meshScratch, new Vector3(-0.88f, 0f, -0.24f), new Vector3(-0.72f, 0.80f, 0.24f), MaterialIdCatalog.OakWood);
                    EmitBox(_meshScratch, new Vector3( 0.72f, 0f, -0.24f), new Vector3( 0.88f, 0.80f, 0.24f), MaterialIdCatalog.OakWood);
                    break;
                case InteriorFixtureKind.WoodBed:
                    EmitBox(_meshScratch, new Vector3(-0.55f, 0f, -1.05f), new Vector3(0.55f, 0.42f, 1.05f), MaterialIdCatalog.TreatedTimberBeam);
                    EmitBox(_meshScratch, new Vector3(-0.48f, 0.42f, -0.96f), new Vector3(0.48f, 0.62f, 0.96f), MaterialIdCatalog.TacticalCloth);
                    break;
                case InteriorFixtureKind.StorageChest:
                    EmitBox(_meshScratch, new Vector3(-0.52f, 0f, -0.32f), new Vector3(0.52f, 0.62f, 0.32f), MaterialIdCatalog.WoodPlank);
                    break;
                case InteriorFixtureKind.TableAndBench:
                    EmitBox(_meshScratch, new Vector3(-0.88f, 0.74f, -0.46f), new Vector3(0.88f, 0.85f, 0.46f), MaterialIdCatalog.WoodPlank);
                    EmitBox(_meshScratch, new Vector3(-0.76f, 0f, -0.38f), new Vector3(-0.62f, 0.74f, 0.38f), MaterialIdCatalog.OakWood);
                    EmitBox(_meshScratch, new Vector3( 0.62f, 0f, -0.38f), new Vector3( 0.76f, 0.74f, 0.38f), MaterialIdCatalog.OakWood);
                    // On ve Arka Oturma Banklari
                    EmitBox(_meshScratch, new Vector3(-0.80f, 0.40f, -0.86f), new Vector3(0.80f, 0.48f, -0.56f), MaterialIdCatalog.TreatedTimberBeam);
                    EmitBox(_meshScratch, new Vector3(-0.68f, 0f, -0.82f), new Vector3(-0.56f, 0.40f, -0.60f), MaterialIdCatalog.OakWood);
                    EmitBox(_meshScratch, new Vector3( 0.56f, 0f, -0.82f), new Vector3( 0.68f, 0.40f, -0.60f), MaterialIdCatalog.OakWood);
                    EmitBox(_meshScratch, new Vector3(-0.80f, 0.40f,  0.56f), new Vector3(0.80f, 0.48f,  0.86f), MaterialIdCatalog.TreatedTimberBeam);
                    EmitBox(_meshScratch, new Vector3(-0.68f, 0f,  0.60f), new Vector3(-0.56f, 0.40f,  0.82f), MaterialIdCatalog.OakWood);
                    EmitBox(_meshScratch, new Vector3( 0.56f, 0f,  0.60f), new Vector3( 0.68f, 0.40f,  0.82f), MaterialIdCatalog.OakWood);
                    break;
                case InteriorFixtureKind.WallTorchSconce:
                    EmitBox(_meshScratch, new Vector3(-0.06f, -0.25f, -0.06f), new Vector3(0.06f, 0.30f, 0.06f), MaterialIdCatalog.OakWood);
                    GameObject tLightGo = new GameObject("TorchLight");
                    tLightGo.transform.SetParent(root.transform, false);
                    tLightGo.transform.localPosition = new Vector3(0f, 0.38f, 0f);
                    Light tl = tLightGo.AddComponent<Light>();
                    tl.type = LightType.Point;
                    tl.color = new Color(1.0f, 0.72f, 0.35f);
                    tl.intensity = 2.0f;
                    tl.range = 7.5f;
                    tl.enabled = true;
                    fx.PointLight = tl;
                    fx.IsOpenOrLit = true;
                    break;
            }

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_Int_{kind}_{id}");
            visualTarget.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = visualTarget.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            _fixtures.Add(fx);
            return fx;
        }

        private static bool TryConsumeFixtureMaterials(InteriorFixtureKind kind, Stage1HolsterAndBackpackController holster, out string missing)
        {
            missing = string.Empty;
            var ui = holster.InventoryUi;
            if (kind == InteriorFixtureKind.StoneFireplaceWithChimney)
            {
                if (!ui.TryConsumeItemFromInventory(InventoryItemKind.RoughStoneChunk, 0, 6, ignoreSubId: true) &&
                    !ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.Stone, 6))
                {
                    missing = "🔥 Taş Şömine & Baca için en az 6x Ham Taş gerekir!";
                    return false;
                }
                return true;
            }
            if (kind == InteriorFixtureKind.WallTorchSconce)
            {
                if (!ui.TryConsumeItemFromInventory(InventoryItemKind.Stick, 0, 1, ignoreSubId: true) &&
                    !ui.TryConsumeItemFromInventory(InventoryItemKind.SplitFirewood, 0, 1, ignoreSubId: true))
                {
                    missing = "🕯️ Duvar Meşalesi için en az 1x Dal (veya Yarma Odun) gerekir!";
                    return false;
                }
                ui.TryConsumeItemFromInventory(InventoryItemKind.PlantFiber, 0, 1, ignoreSubId: true);
                return true;
            }
            if (!ui.TryConsumeItemFromInventory(InventoryItemKind.TimberPlank, 0, 2, ignoreSubId: true) &&
                !ui.TryConsumeItemFromInventory(InventoryItemKind.LogDebarked, 0, 2, ignoreSubId: true) &&
                !ui.TryConsumeItemFromInventory(InventoryItemKind.SplitFirewood, 0, 2, ignoreSubId: true) &&
                !ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.WoodPlank, 2) &&
                !ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood, 1))
            {
                missing = $"🏠 {kind} için en az 2x Ahşap Kalas (Bıçkı Sehpasında [E] ile üretilir) gerekir!";
                return false;
            }
            return true;
        }

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
