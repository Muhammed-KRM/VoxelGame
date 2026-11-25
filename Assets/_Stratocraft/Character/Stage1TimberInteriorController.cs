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
            out string feedback)
        {
            if (!isCreativeMode && !TryConsumeFixtureMaterials(kind, holster, out string miss))
            {
                feedback = miss;
                return false;
            }

            SpawnFixtureInternal(kind, worldPos, yawDeg, sharedMat, hasChimney: kind == InteriorFixtureKind.StoneFireplaceWithChimney);
            RecomputeThermalState();
            feedback = $"🏠 {kind} yerleştirildi! Yaklaşıp [E] ile kullanabilirsiniz.";
            return true;
        }

        /// <summary>
        /// [E] / [Shift+E] ile kapi ac-kapa/surgule, kepenk ac-kapa, somine yak/sondur, yatakta SpawnPoint kaydet,
        /// Bicki Sehpasinda [V]/[E] ile kalas & mobilya uret veya elde kil/yosun varken Chinking derz yalitimi uygula!
        /// </summary>
        public bool TryInteract(
            Vector3 playerPos,
            bool shiftHeld,
            bool isCreativeMode,
            Stage1HolsterAndBackpackController holster,
            Stage1MatterCrystalAndMiningController miningCtrl,
            out string feedback)
        {
            feedback = string.Empty;

            // 1) Elde Kil veya Lif varken Chinking (derz yalitimi) uygulama
            VisualInventorySlot held = holster.InventoryUi.GetActiveHotbarSlot();
            if (!shiftHeld && !held.IsEmpty &&
                (held.Kind == InventoryItemKind.ClayLump || held.Kind == InventoryItemKind.PlantFiber) &&
                _fixtures.Count > 0 && FindNearestFixture(playerPos, 6.5f) >= 0)
            {
                if (!isCreativeMode) holster.InventoryUi.TryConsumeActiveHotbarSlot(1);
                _chinkedWallsCount = Mathf.Min(20, _chinkedWallsCount + 4);
                RecomputeThermalState();
                feedback = $"🧱 Kütük derz yalıtımı (Chinking) uygulandı! Sızdırmazlık: %{_lastThermalState.SealFactor01 * 100f:0} | İç Isı Farkı: +{_lastThermalState.IndoorTempDeltaC:0.0}°C" +
                           (_lastThermalState.IsEnclosedWarmRoom ? " (✅ SICAK KAPALI ODA AKTİF!)" : "");
                return true;
            }

            int idx = FindNearestFixture(playerPos, 3.0f);
            if (idx < 0) return false;
            InteriorFixture fx = _fixtures[idx];

            switch (fx.Kind)
            {
                case InteriorFixtureKind.HingedDoor:
                case InteriorFixtureKind.WindowShutter:
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
                        fx.PivotTr.localRotation = Quaternion.Euler(0f, fx.IsOpenOrLit ? 90f : 0f, 0f);
                    }
                    RecomputeThermalState();
                    feedback = fx.IsOpenOrLit
                        ? $"🚪 {(fx.Kind == InteriorFixtureKind.HingedDoor ? "Kapı" : "Kepenk")} açıldı (Oda yalıtımı: %{_lastThermalState.SealFactor01 * 100f:0})."
                        : $"🚪 {(fx.Kind == InteriorFixtureKind.HingedDoor ? "Kapı" : "Kepenk")} kapatıldı (Oda yalıtımı: %{_lastThermalState.SealFactor01 * 100f:0}).";
                    return true;

                case InteriorFixtureKind.StoneFireplaceWithChimney:
                    fx.IsOpenOrLit = !fx.IsOpenOrLit;
                    if (fx.PointLight != null) fx.PointLight.enabled = fx.IsOpenOrLit;
                    RecomputeThermalState();
                    feedback = fx.IsOpenOrLit
                        ? $"🔥 Taş şömine yakıldı! Baca çekişi aktif | İç Isı: +{_lastThermalState.IndoorTempDeltaC:0.0}°C | Yalıtım: %{_lastThermalState.SealFactor01 * 100f:0}"
                        : "💨 Taş şömine söndürüldü.";
                    return true;

                case InteriorFixtureKind.WoodBed:
                    feedback = "🛏️ Yatak dinlenme & Yeniden Doğma Noktası (SpawnPoint) bu eve kaydedildi!";
                    return true;

                case InteriorFixtureKind.SawhorseBench:
                    if (shiftHeld)
                    {
                        fx.SelectedRecipeIdx = (fx.SelectedRecipeIdx + 1) % TimberCabinInteriorSystem.SawhorseRecipes.Length;
                        var nextRec = TimberCabinInteriorSystem.SawhorseRecipes[fx.SelectedRecipeIdx];
                        feedback = $"🪚 Bıçkı Sehpası Tarifi [Shift+E]: {nextRec.DisplayName} (Gerekli: {nextRec.RequiredOakLogs} Kütük, {nextRec.RequiredPlanks} Kalas, {nextRec.RequiredSticks} Dal) | Üretmek için [E]";
                        return true;
                    }
                    var rec = TimberCabinInteriorSystem.SawhorseRecipes[fx.SelectedRecipeIdx];
                    if (!isCreativeMode && rec.RequiredOakLogs > 0 &&
                        !holster.InventoryUi.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood, rec.RequiredOakLogs))
                    {
                        feedback = $"🪚 {rec.DisplayName} için en az {rec.RequiredOakLogs}x Meşe Kütüğü gerekir! (Tarif değiştir: [Shift+E])";
                        return true;
                    }
                    byte outMat = rec.Kind == TimberInteriorCraftKind.WoodPlanks4 ? MaterialIdCatalog.WoodPlank : MaterialIdCatalog.TreatedTimberBeam;
                    int outCnt = rec.Kind == TimberInteriorCraftKind.WoodPlanks4 ? 4 : 1;
                    holster.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.VoxelBlock, outMat, outCnt, miningCtrl);
                    feedback = $"🪚 Bıçkı Sehpasında üretildi: {rec.DisplayName} (+{outCnt})! (Tarif değiştir: [Shift+E])";
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

        private void RecomputeThermalState()
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

            _lastThermalState = new CabinInteriorThermalState
            {
                TotalWallLogs = 20,
                ChinkedWallLogs = _chinkedWallsCount,
                TotalOpenings = openings,
                ClosedDoorsOrShutters = closed,
                HasRoofOverhead = 1,
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

        private InteriorFixture SpawnFixtureInternal(InteriorFixtureKind kind, Vector3 pos, float yawDeg, Material? sharedMat, bool hasChimney)
        {
            uint id = _nextId++;
            var fx = new InteriorFixture
            {
                Id = id,
                Kind = kind,
                Position = pos,
                BaseYawDeg = yawDeg,
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
                    EmitBox(_meshScratch, new Vector3(0f, 0f, -0.07f), new Vector3(1.90f, 2.25f, 0.07f), MaterialIdCatalog.WoodPlank);
                    EmitBox(_meshScratch, new Vector3(0.08f, 0.35f, -0.09f), new Vector3(1.82f, 0.50f, 0.09f), MaterialIdCatalog.TreatedTimberBeam);
                    EmitBox(_meshScratch, new Vector3(0.08f, 1.75f, -0.09f), new Vector3(1.82f, 1.90f, 0.09f), MaterialIdCatalog.TreatedTimberBeam);
                    break;
                case InteriorFixtureKind.WindowShutter:
                    EmitBox(_meshScratch, new Vector3(0f, 0f, -0.05f), new Vector3(1.70f, 1.05f, 0.05f), MaterialIdCatalog.WoodPlank);
                    break;
                case InteriorFixtureKind.StoneFireplaceWithChimney:
                    // Yanmaz tas altlik + tas somine ocagi + catidan 0.6m tasan 11.5m baca safti
                    EmitBox(_meshScratch, new Vector3(-0.90f, -0.05f, -0.65f), new Vector3(0.90f, 0.14f, 0.65f), MaterialIdCatalog.CutAshlarStone);
                    EmitBox(_meshScratch, new Vector3(-0.78f, 0.14f, -0.50f), new Vector3(0.78f, 1.85f, 0.50f), MaterialIdCatalog.StoneBrick);
                    EmitBox(_meshScratch, new Vector3(-0.42f, 1.85f, -0.42f), new Vector3(0.42f, 12.20f, 0.42f), MaterialIdCatalog.StoneBrick);
                    EmitBox(_meshScratch, new Vector3(-0.56f, 12.20f, -0.56f), new Vector3(0.56f, 12.42f, 0.56f), MaterialIdCatalog.CutAshlarStone);
                    GameObject fireLightGo = new GameObject("FireplaceLight");
                    fireLightGo.transform.SetParent(root.transform, false);
                    fireLightGo.transform.localPosition = new Vector3(0f, 0.75f, -0.65f);
                    Light fl = fireLightGo.AddComponent<Light>();
                    fl.type = LightType.Point;
                    fl.color = new Color(1.0f, 0.62f, 0.25f);
                    fl.intensity = 3.2f;
                    fl.range = 10.5f;
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
                    EmitBox(_meshScratch, new Vector3(-0.85f, 0.72f, -0.48f), new Vector3(0.85f, 0.84f, 0.48f), MaterialIdCatalog.WoodPlank);
                    EmitBox(_meshScratch, new Vector3(-0.76f, 0f, -0.40f), new Vector3(-0.62f, 0.72f, 0.40f), MaterialIdCatalog.OakWood);
                    EmitBox(_meshScratch, new Vector3( 0.62f, 0f, -0.40f), new Vector3( 0.76f, 0.72f, 0.40f), MaterialIdCatalog.OakWood);
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
                if (!ui.TryConsumeItemFromInventory(InventoryItemKind.RoughStoneChunk, 0, 6) &&
                    !ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.Stone, 6))
                {
                    missing = "🔥 Taş Şömine için en az 6x Ham Taş gerekir!";
                    return false;
                }
                return true;
            }
            if (!ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.WoodPlank, 2) &&
                !ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood, 1))
            {
                missing = $"🏠 {kind} için en az 2x Ahşap Kalas (veya 1x Meşe) gerekir!";
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
