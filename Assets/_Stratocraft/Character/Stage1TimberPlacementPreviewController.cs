#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Systems;
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
        StoneFireplace = 7,
        WoodBed = 8,
        HingedDoor = 9,
        WindowShutter = 10,
        StorageChest = 11,
        PlankCarpentry = 12,
        TableAndBench = 13,
        WallTorchSconce = 14
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
        private sealed class PinnedBlueprint
        {
            public TimberBuildPreviewKind Kind;
            public Vector3 Position;
            public float YawDeg;
            public GameObject? RootGo;
        }

        private readonly List<PinnedBlueprint> _pinnedBlueprints = new(16);
        private TimberBuildPreviewKind _activeKind = TimberBuildPreviewKind.None;
        private int _ignorePlaceUntilFrame;
        private bool _logVerticalMode = true;
        private int _plankCarpentryMode; // 0=FloorDeck, 1=StairFlight, 2=RoofSlope
        private float _previewYawDeg;
        private float _lastBuiltLen;
        private Vector3 _lastBuiltPlankSize;
        private TimberBuildPreviewKind _lastBuiltKind;
        private bool _lastBuiltVertical;

        private GameObject? _ghostGo;
        private Material? _ghostMat;
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(256);

        public bool HasActiveToolPreview =>
            _activeKind != TimberBuildPreviewKind.None &&
            _activeKind != TimberBuildPreviewKind.RealLogFromCart &&
            _activeKind != TimberBuildPreviewKind.PlankCarpentry;
        public int PinnedBlueprintCount => _pinnedBlueprints.Count;

        public void Dispose()
        {
            if (_ghostGo != null)
            {
                UnityEngine.Object.Destroy(_ghostGo);
                _ghostGo = null;
            }
            for (int i = 0; i < _pinnedBlueprints.Count; i++)
            {
                if (_pinnedBlueprints[i].RootGo != null)
                {
                    UnityEngine.Object.Destroy(_pinnedBlueprints[i].RootGo);
                }
            }
            _pinnedBlueprints.Clear();
            if (_ghostMat != null)
            {
                UnityEngine.Object.Destroy(_ghostMat);
                _ghostMat = null;
            }
            _activeKind = TimberBuildPreviewKind.None;
        }

        public bool TryFindAimedBlueprint(UnityEngine.Camera? cam, Transform playerTr, out int index, out string label)
        {
            index = -1;
            label = string.Empty;
            if (_pinnedBlueprints.Count == 0) return false;

            Vector3 origin = cam != null ? cam.transform.position : playerTr.position + Vector3.up * 1.5f;
            Vector3 dir = cam != null ? cam.transform.forward : playerTr.forward;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _pinnedBlueprints.Count; i++)
            {
                Vector3 center = _pinnedBlueprints[i].Position + Vector3.up * 0.9f;
                Vector3 to = center - origin;
                float dist = to.magnitude;
                if (dist > 8.5f) continue;

                float dot = dist > 0.05f ? Vector3.Dot(dir, to / dist) : 1f;
                if (dot < 0.72f && dist > 2.8f) continue;

                float score = dist * (1.25f - Mathf.Clamp01(dot));
                if (score < bestScore)
                {
                    bestScore = score;
                    index = i;
                    label = GetKindLabel(_pinnedBlueprints[i].Kind);
                }
            }
            return index >= 0;
        }

        public bool TryGetBlueprintKindAt(int index, out TimberBuildPreviewKind kind)
        {
            kind = TimberBuildPreviewKind.None;
            if (index < 0 || index >= _pinnedBlueprints.Count) return false;
            kind = _pinnedBlueprints[index].Kind;
            return true;
        }

        public bool RemoveBlueprintAt(int index, out string feedback)
        {
            feedback = string.Empty;
            if (index < 0 || index >= _pinnedBlueprints.Count) return false;
            PinnedBlueprint bp = _pinnedBlueprints[index];
            if (bp.RootGo != null) UnityEngine.Object.Destroy(bp.RootGo);
            string lbl = GetKindLabel(bp.Kind);
            _pinnedBlueprints.RemoveAt(index);
            feedback = $"🗑️ {lbl} proje izi (planı) kaldırıldı.";
            return true;
        }

        public bool ClearAllBlueprints(out string feedback)
        {
            int cnt = _pinnedBlueprints.Count;
            if (cnt == 0)
            {
                feedback = string.Empty;
                return false;
            }
            for (int i = 0; i < _pinnedBlueprints.Count; i++)
            {
                if (_pinnedBlueprints[i].RootGo != null) UnityEngine.Object.Destroy(_pinnedBlueprints[i].RootGo);
            }
            _pinnedBlueprints.Clear();
            feedback = $"🗑️ Tüm proje izleri ({cnt} adet) temizlendi.";
            return true;
        }

        private struct PendingHandAssembly
        {
            public bool Active;
            public TimberBuildPreviewKind Kind;
            public Vector3 Position;
            public float YawDeg;
            public float ElapsedSec;
            public float DurationSec;
            public int BlueprintIndex;
            public int[] MatchedLooseIndices;
            public int MatchedCount;
        }

        private PendingHandAssembly _assembly;
        public bool IsHandAssembling => _assembly.Active;
        public float HandAssemblyProgress01 => _assembly.Active && _assembly.DurationSec > 0.01f
            ? Mathf.Clamp01(_assembly.ElapsedSec / _assembly.DurationSec)
            : 0f;
        public string HandAssemblyLabel => _assembly.Active ? GetKindLabel(_assembly.Kind) : string.Empty;

        public bool TryRemoveAimedBlueprint(UnityEngine.Camera? cam, Transform playerTr, out string feedback)
        {
            feedback = string.Empty;
            if (!TryFindAimedBlueprint(cam, playerTr, out int idx, out _)) return false;
            return RemoveBlueprintAt(idx, out feedback);
        }

        private enum RequiredSlotMaterial : byte
        {
            Wood = 0,
            Fiber = 1,
            Stone = 2,
            Clay = 3,
            WoodenWheel = 4
        }

        private readonly struct BlueprintSlotSpec
        {
            public readonly string Label;
            public readonly Vector3 LocalOffset;
            public readonly Vector3 HalfSize;
            public readonly RequiredSlotMaterial Material;
            public readonly float ToleranceM;

            public BlueprintSlotSpec(string label, Vector3 localOffset, Vector3 halfSize, RequiredSlotMaterial material, float toleranceM = 0.95f)
            {
                Label = label;
                LocalOffset = localOffset;
                HalfSize = halfSize;
                Material = material;
                ToleranceM = toleranceM;
            }
        }

        private static readonly BlueprintSlotSpec[] LogArchCartSlots =
        {
            new("Sol Teker (Ahşap Teker)", new Vector3(-0.78f, 0.45f, 0.00f), new Vector3(0.09f, 0.44f, 0.44f), RequiredSlotMaterial.WoodenWheel, 0.95f),
            new("Sağ Teker (Ahşap Teker)", new Vector3(0.78f, 0.45f, 0.00f), new Vector3(0.09f, 0.44f, 0.44f), RequiredSlotMaterial.WoodenWheel, 0.95f),
            new("Dingil Odunu (Dal/Odun)", new Vector3(0.00f, 0.16f, 0.00f), new Vector3(0.74f, 0.10f, 0.10f), RequiredSlotMaterial.Wood, 0.95f),
            new("Çekme Kolu (Dal/Odun)", new Vector3(0.00f, 0.16f, 1.02f), new Vector3(0.10f, 0.10f, 0.92f), RequiredSlotMaterial.Wood, 1.05f),
            new("Merkez Bağlama Lifi", new Vector3(0.00f, 0.14f, 0.22f), new Vector3(0.18f, 0.12f, 0.18f), RequiredSlotMaterial.Fiber, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] ScaffoldingSlots =
        {
            new("Sol-Ön Direk (Dal/Odun)", new Vector3(-0.85f, 0.48f, 0.85f), new Vector3(0.10f, 0.48f, 0.10f), RequiredSlotMaterial.Wood, 0.95f),
            new("Sağ-Ön Direk (Dal/Odun)", new Vector3(0.85f, 0.48f, 0.85f), new Vector3(0.10f, 0.48f, 0.10f), RequiredSlotMaterial.Wood, 0.95f),
            new("Sol-Arka Direk (Dal/Odun)", new Vector3(-0.85f, 0.48f, -0.85f), new Vector3(0.10f, 0.48f, 0.10f), RequiredSlotMaterial.Wood, 0.95f),
            new("Sağ-Arka Direk (Dal/Odun)", new Vector3(0.85f, 0.48f, -0.85f), new Vector3(0.10f, 0.48f, 0.10f), RequiredSlotMaterial.Wood, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] ParbuckleRampSlots =
        {
            new("Sol Sırık (Dal/Odun)", new Vector3(-0.72f, 0.16f, 0.00f), new Vector3(0.11f, 0.11f, 1.15f), RequiredSlotMaterial.Wood, 1.05f),
            new("Sağ Sırık (Dal/Odun)", new Vector3(0.72f, 0.16f, 0.00f), new Vector3(0.11f, 0.11f, 1.15f), RequiredSlotMaterial.Wood, 1.05f),
            new("Üst Halat Lifi", new Vector3(0.00f, 0.15f, 0.95f), new Vector3(0.20f, 0.13f, 0.20f), RequiredSlotMaterial.Fiber, 1.00f)
        };

        private static readonly BlueprintSlotSpec[] GinPoleSlots =
        {
            new("Ana Direk (Dal/Odun)", new Vector3(0.00f, 0.55f, 0.00f), new Vector3(0.12f, 0.55f, 0.12f), RequiredSlotMaterial.Wood, 1.00f),
            new("Üst Bom (Dal/Odun)", new Vector3(0.00f, 0.16f, 1.05f), new Vector3(0.11f, 0.11f, 0.95f), RequiredSlotMaterial.Wood, 1.05f),
            new("Sol Gergi Lifi", new Vector3(-0.92f, 0.14f, -0.35f), new Vector3(0.18f, 0.12f, 0.18f), RequiredSlotMaterial.Fiber, 1.00f),
            new("Sağ Gergi Lifi", new Vector3(0.92f, 0.14f, -0.35f), new Vector3(0.18f, 0.12f, 0.18f), RequiredSlotMaterial.Fiber, 1.00f)
        };

        private static readonly BlueprintSlotSpec[] SawhorseSlots =
        {
            new("Sol Ayak/Kiriş (Dal/Odun)", new Vector3(-0.68f, 0.16f, 0.00f), new Vector3(0.11f, 0.11f, 0.85f), RequiredSlotMaterial.Wood, 1.00f),
            new("Sağ Ayak/Kiriş (Dal/Odun)", new Vector3(0.68f, 0.16f, 0.00f), new Vector3(0.11f, 0.11f, 0.85f), RequiredSlotMaterial.Wood, 1.00f)
        };

        private static readonly BlueprintSlotSpec[] FireplaceSlots =
        {
            new("Sol Ocak Taşı", new Vector3(-0.68f, 0.20f, 0.00f), new Vector3(0.22f, 0.20f, 0.35f), RequiredSlotMaterial.Stone, 0.90f),
            new("Sağ Ocak Taşı", new Vector3(0.68f, 0.20f, 0.00f), new Vector3(0.22f, 0.20f, 0.35f), RequiredSlotMaterial.Stone, 0.90f),
            new("Arka Ocak Taşı", new Vector3(0.00f, 0.20f, -0.58f), new Vector3(0.42f, 0.20f, 0.22f), RequiredSlotMaterial.Stone, 0.90f),
            new("Baca Temel Taşı", new Vector3(0.00f, 0.56f, -0.50f), new Vector3(0.32f, 0.18f, 0.22f), RequiredSlotMaterial.Stone, 0.95f),
            new("Harç Kili (Merkez)", new Vector3(0.00f, 0.15f, 0.12f), new Vector3(0.22f, 0.14f, 0.22f), RequiredSlotMaterial.Clay, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] WoodBedSlots =
        {
            new("Sol Yatak Çerçevesi (Dal/Odun)", new Vector3(-0.48f, 0.16f, 0.00f), new Vector3(0.10f, 0.10f, 0.95f), RequiredSlotMaterial.Wood, 0.95f),
            new("Sağ Yatak Çerçevesi (Dal/Odun)", new Vector3(0.48f, 0.16f, 0.00f), new Vector3(0.10f, 0.10f, 0.95f), RequiredSlotMaterial.Wood, 0.95f),
            new("Baş Döşeği (Yaprak/Lif)", new Vector3(0.00f, 0.18f, 0.45f), new Vector3(0.32f, 0.12f, 0.32f), RequiredSlotMaterial.Fiber, 0.95f),
            new("Ayak Döşeği (Yaprak/Lif)", new Vector3(0.00f, 0.18f, -0.45f), new Vector3(0.32f, 0.12f, 0.32f), RequiredSlotMaterial.Fiber, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] HingedDoorSlots =
        {
            new("Sol Kapı Kanadı (Odun/Kalas)", new Vector3(0.35f, 0.16f, 0.00f), new Vector3(0.14f, 0.10f, 0.85f), RequiredSlotMaterial.Wood, 0.95f),
            new("Orta Kapı Kanadı (Odun/Kalas)", new Vector3(0.95f, 0.16f, 0.00f), new Vector3(0.14f, 0.10f, 0.85f), RequiredSlotMaterial.Wood, 0.95f),
            new("Sağ Kapı Kanadı (Odun/Kalas)", new Vector3(1.55f, 0.16f, 0.00f), new Vector3(0.14f, 0.10f, 0.85f), RequiredSlotMaterial.Wood, 0.95f),
            new("Menteşe Bağı (Lif)", new Vector3(0.08f, 0.15f, 0.00f), new Vector3(0.16f, 0.12f, 0.16f), RequiredSlotMaterial.Fiber, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] WindowShutterSlots =
        {
            new("Sol Kepenk Kanadı (Odun/Kalas)", new Vector3(0.42f, 0.16f, 0.00f), new Vector3(0.14f, 0.10f, 0.50f), RequiredSlotMaterial.Wood, 0.95f),
            new("Sağ Kepenk Kanadı (Odun/Kalas)", new Vector3(1.25f, 0.16f, 0.00f), new Vector3(0.14f, 0.10f, 0.50f), RequiredSlotMaterial.Wood, 0.95f),
            new("Kepenk Bağı (Lif)", new Vector3(0.08f, 0.15f, 0.00f), new Vector3(0.15f, 0.11f, 0.15f), RequiredSlotMaterial.Fiber, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] StorageChestSlots =
        {
            new("Sol Sandık Yanı (Odun/Kalas)", new Vector3(-0.42f, 0.16f, 0.00f), new Vector3(0.10f, 0.14f, 0.30f), RequiredSlotMaterial.Wood, 0.90f),
            new("Sağ Sandık Yanı (Odun/Kalas)", new Vector3(0.42f, 0.16f, 0.00f), new Vector3(0.10f, 0.14f, 0.30f), RequiredSlotMaterial.Wood, 0.90f),
            new("Üst Kapak (Odun/Kalas)", new Vector3(0.00f, 0.32f, 0.00f), new Vector3(0.38f, 0.08f, 0.28f), RequiredSlotMaterial.Wood, 0.90f),
            new("Kapak Menteşe Lifi", new Vector3(0.00f, 0.16f, -0.25f), new Vector3(0.15f, 0.11f, 0.15f), RequiredSlotMaterial.Fiber, 0.90f)
        };

        private static readonly BlueprintSlotSpec[] TableAndBenchSlots =
        {
            new("Masa Tablası (Kalas/Odun)", new Vector3(0.00f, 0.75f, 0.00f), new Vector3(0.85f, 0.08f, 0.45f), RequiredSlotMaterial.Wood, 0.95f),
            new("Ön Bank (Kalas/Odun)", new Vector3(0.00f, 0.42f, -0.72f), new Vector3(0.75f, 0.08f, 0.20f), RequiredSlotMaterial.Wood, 0.95f),
            new("Arka Bank (Kalas/Odun)", new Vector3(0.00f, 0.42f, 0.72f), new Vector3(0.75f, 0.08f, 0.20f), RequiredSlotMaterial.Wood, 0.95f)
        };

        private static readonly BlueprintSlotSpec[] WallTorchSlots =
        {
            new("Meşale Gövdesi (Dal + Lif)", new Vector3(0.00f, 0.05f, 0.00f), new Vector3(0.10f, 0.32f, 0.10f), RequiredSlotMaterial.Wood, 0.85f)
        };

        private static BlueprintSlotSpec[] GetSlotsForKind(TimberBuildPreviewKind kind) => kind switch
        {
            TimberBuildPreviewKind.LogArchCart => LogArchCartSlots,
            TimberBuildPreviewKind.WoodScaffolding => ScaffoldingSlots,
            TimberBuildPreviewKind.ParbuckleRamp2To1 => ParbuckleRampSlots,
            TimberBuildPreviewKind.GinPoleHoist => GinPoleSlots,
            TimberBuildPreviewKind.SawhorseBench => SawhorseSlots,
            TimberBuildPreviewKind.StoneFireplace => FireplaceSlots,
            TimberBuildPreviewKind.WoodBed => WoodBedSlots,
            TimberBuildPreviewKind.HingedDoor => HingedDoorSlots,
            TimberBuildPreviewKind.WindowShutter => WindowShutterSlots,
            TimberBuildPreviewKind.StorageChest => StorageChestSlots,
            TimberBuildPreviewKind.TableAndBench => TableAndBenchSlots,
            TimberBuildPreviewKind.WallTorchSconce => WallTorchSlots,
            _ => Array.Empty<BlueprintSlotSpec>()
        };

        private static bool MatchesSlotMaterial(LooseBuildPartKind partKind, RequiredSlotMaterial req) => req switch
        {
            RequiredSlotMaterial.WoodenWheel => partKind == LooseBuildPartKind.WoodenWheel,
            RequiredSlotMaterial.Wood => partKind == LooseBuildPartKind.Stick || partKind == LooseBuildPartKind.SplitFirewood || (byte)partKind >= 16,
            RequiredSlotMaterial.Fiber => partKind == LooseBuildPartKind.PlantFiber || partKind == LooseBuildPartKind.LeafPile,
            RequiredSlotMaterial.Stone => partKind == LooseBuildPartKind.RoughStoneChunk,
            RequiredSlotMaterial.Clay => partKind == LooseBuildPartKind.ClayLump || partKind == LooseBuildPartKind.LooseDirtClod,
            _ => false
        };

        private readonly bool[] _slotFilledScratch = new bool[8];
        private readonly int[] _slotMatchedIdxScratch = new int[8];

        private int EvaluateSpatialSlots(
            TimberBuildPreviewKind kind,
            Vector3 bpPos,
            float bpYawDeg,
            ReadOnlySpan<LoosePartPoint> cloud,
            bool[] filledOut,
            int[] matchedPartIndicesOut)
        {
            BlueprintSlotSpec[] slots = GetSlotsForKind(kind);
            Quaternion rot = Quaternion.Euler(0f, bpYawDeg, 0f);
            int filledCount = 0;

            for (int s = 0; s < slots.Length; s++)
            {
                filledOut[s] = false;
                matchedPartIndicesOut[s] = -1;
                Vector3 slotWorld = bpPos + rot * slots[s].LocalOffset;
                float tolSq = slots[s].ToleranceM * slots[s].ToleranceM;
                int bestIdx = -1;
                float bestDistSq = tolSq;

                for (int i = 0; i < cloud.Length; i++)
                {
                    ref readonly LoosePartPoint pt = ref cloud[i];
                    if (pt.BoundStationId != 0u) continue;
                    if (!MatchesSlotMaterial(pt.Kind, slots[s].Material)) continue;

                    // Ayni fiziksel parca iki farkli yuvaya sayilamaz!
                    bool alreadyUsed = false;
                    for (int prev = 0; prev < s; prev++)
                    {
                        if (matchedPartIndicesOut[prev] == i) { alreadyUsed = true; break; }
                    }
                    if (alreadyUsed) continue;

                    float dx = pt.Position.X - slotWorld.x;
                    float dz = pt.Position.Z - slotWorld.z;
                    float dSq = dx * dx + dz * dz;
                    if (dSq <= bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestIdx = i;
                    }
                }

                if (bestIdx >= 0)
                {
                    filledOut[s] = true;
                    matchedPartIndicesOut[s] = bestIdx;
                    filledCount++;
                }
            }
            return filledCount;
        }

        /// <summary>
        /// Gercekci "Yere Materyal Yuvalarina Diz -> [T] Sag Tikla / Dogrula -> El Animasyonuyla Insa Et" dogrulayicisi.
        /// Her parcanin (orn. Sol Teker, Sag Teker, Dingil Odunu, Cekme Kolu, Merkez Lif) yaklasik dogru yuvada olmasini sart kosar!
        /// </summary>
        public bool TryValidateGroundClusterAndStartAssembly(
            TimberBuildPreviewKind kind,
            UnityEngine.Camera? cam,
            Transform playerTr,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1MindBuildingController mindCtrl,
            out string feedback)
        {
            feedback = string.Empty;
            if (_assembly.Active)
            {
                feedback = $"🛠️ Zaten {GetKindLabel(_assembly.Kind)} ellerle monte ediliyor...";
                return false;
            }

            int bpIdx = -1;
            Vector3 center = playerTr.position + (cam != null ? new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z).normalized : playerTr.forward) * 2.8f;
            float yawDeg = Mathf.Round((cam != null ? cam.transform.eulerAngles.y : playerTr.eulerAngles.y) / 45f) * 45f;
            if (TryFindAimedBlueprint(cam, playerTr, out int aimedIdx, out _))
            {
                PinnedBlueprint bp = _pinnedBlueprints[aimedIdx];
                if (bp.Kind == kind)
                {
                    bpIdx = aimedIdx;
                    center = bp.Position;
                    yawDeg = bp.YawDeg;
                }
            }

            BlueprintSlotSpec[] slots = GetSlotsForKind(kind);
            ReadOnlySpan<LoosePartPoint> cloud = looseBuildSystem.SnapshotPointCloud();
            int filledCount = EvaluateSpatialSlots(kind, center, yawDeg, cloud, _slotFilledScratch, _slotMatchedIdxScratch);

            if (filledCount < slots.Length)
            {
                string missing = string.Empty;
                bool missingWheel = false;
                for (int s = 0; s < slots.Length; s++)
                {
                    if (!_slotFilledScratch[s])
                    {
                        missing += (missing.Length > 0 ? ", " : "") + slots[s].Label;
                        if (slots[s].Material == RequiredSlotMaterial.WoodenWheel) missingWheel = true;
                    }
                }
                string wheelTip = missingWheel ? " (İpucu: Ahşap Tekerleği [I] Bez Örtüde odun+dal ile üretebilirsin!)" : string.Empty;
                feedback = $"⚠️ Dizilim eksik/yanlış yerde ({filledCount}/{slots.Length})! Eksik yuvalar: {missing}{wheelTip}";
                return false;
            }

            int[] matched = new int[slots.Length];
            for (int s = 0; s < slots.Length; s++) matched[s] = _slotMatchedIdxScratch[s];

            _assembly = new PendingHandAssembly
            {
                Active = true,
                Kind = kind,
                Position = center,
                YawDeg = yawDeg,
                ElapsedSec = 0f,
                DurationSec = 1.6f,
                BlueprintIndex = bpIdx,
                MatchedLooseIndices = matched,
                MatchedCount = slots.Length
            };

            mindCtrl.TriggerFinishingTouchAnimation(1.6f);
            feedback = $"🛠️ Tüm yuvalar doğru dizildi ({slots.Length}/{slots.Length})! Elleriyle {GetKindLabel(kind)} monte ediliyor...";
            return true;
        }

        public bool StepHandAssembly(
            float dt,
            WorldLooseBuildSystem looseBuildSystem,
            Stage1LogArchCartController logCartCtrl,
            Stage1TimberSiteRigController siteRigCtrl,
            Stage1TimberInteriorController interiorCtrl,
            Stage1RigidTimberBridge rigidTimberBridge,
            Stage1HolsterAndBackpackController holsterCtrl,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            Material? sharedVoxelMat,
            out string completedFeedback)
        {
            completedFeedback = string.Empty;
            if (!_assembly.Active) return false;

            _assembly.ElapsedSec += dt;
            if (_assembly.ElapsedSec < _assembly.DurationSec)
            {
                // 1.6 sn montaj sirasinda kilavuzun zeminde hafifce titreyip yukselerek kurulum animasyonu vermesi
                if (_assembly.BlueprintIndex >= 0 && _assembly.BlueprintIndex < _pinnedBlueprints.Count)
                {
                    GameObject? bpGo = _pinnedBlueprints[_assembly.BlueprintIndex].RootGo;
                    if (bpGo != null)
                    {
                        float p01 = Mathf.Clamp01(_assembly.ElapsedSec / Mathf.Max(0.01f, _assembly.DurationSec));
                        float pulse = 1.0f + 0.04f * Mathf.Sin(p01 * Mathf.PI * 10f);
                        bpGo.transform.localScale = new Vector3(pulse, Mathf.Lerp(0.35f, 1.05f, p01), pulse);
                    }
                }
                return false;
            }

            TimberBuildPreviewKind kind = _assembly.Kind;
            Vector3 pos = _assembly.Position;
            float yaw = _assembly.YawDeg;
            int bpIdx = _assembly.BlueprintIndex;
            int[]? matched = _assembly.MatchedLooseIndices;
            int matchCnt = _assembly.MatchedCount;
            _assembly = default;

            // Yerdeki fiziksel parcalari (dal/teker/lif/tas/kil) GERCEKTEN tuketip yok et!
            if (matched != null && matchCnt > 0)
            {
                looseBuildSystem.ConsumeAndDestroyLooseParts(new ReadOnlySpan<int>(matched, 0, matchCnt));
            }

            // Varsa gorsel kilavuz planini kaldir
            if (bpIdx >= 0 && bpIdx < _pinnedBlueprints.Count)
            {
                if (_pinnedBlueprints[bpIdx].RootGo != null) UnityEngine.Object.Destroy(_pinnedBlueprints[bpIdx].RootGo);
                _pinnedBlueprints.RemoveAt(bpIdx);
            }

            // Gercek fiziksel nesneyi kur (malzemeler zaten yerden dogrulanip tuketildigi icin isCreative: true ile kurar)
            switch (kind)
            {
                case TimberBuildPreviewKind.LogArchCart:
                    logCartCtrl.SpawnLogArchCart(pos, yaw, sharedVoxelMat, worldChunks, out completedFeedback);
                    break;
                case TimberBuildPreviewKind.WoodScaffolding:
                    siteRigCtrl.TryBuildSiteRig(TimberSiteRigKind.WoodScaffolding, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback);
                    break;
                case TimberBuildPreviewKind.ParbuckleRamp2To1:
                    siteRigCtrl.TryBuildSiteRig(TimberSiteRigKind.ParbuckleRamp2To1, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback);
                    break;
                case TimberBuildPreviewKind.GinPoleHoist:
                    siteRigCtrl.TryBuildSiteRig(TimberSiteRigKind.GinPoleHoist, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback);
                    break;
                case TimberBuildPreviewKind.SawhorseBench:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.SawhorseBench, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.StoneFireplace:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.StoneFireplaceWithChimney, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.WoodBed:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.WoodBed, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.HingedDoor:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.HingedDoor, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.WindowShutter:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.WindowShutter, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.StorageChest:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.StorageChest, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.TableAndBench:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.TableAndBench, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
                case TimberBuildPreviewKind.WallTorchSconce:
                    interiorCtrl.TryPlaceInteriorFixture(InteriorFixtureKind.WallTorchSconce, pos, yaw, isCreativeMode: true, holsterCtrl, sharedVoxelMat, out completedFeedback, rigidTimberBridge);
                    break;
            }
            return true;
        }

        private static void GetRequiredGroundMaterials(TimberBuildPreviewKind kind, out int wood, out int fiber, out int stone)
        {
            wood = 0; fiber = 0; stone = 0;
            switch (kind)
            {
                case TimberBuildPreviewKind.LogArchCart: wood = 2; fiber = 1; break;
                case TimberBuildPreviewKind.WoodScaffolding: wood = 4; break;
                case TimberBuildPreviewKind.ParbuckleRamp2To1: wood = 2; fiber = 1; break;
                case TimberBuildPreviewKind.GinPoleHoist: wood = 2; fiber = 2; break;
                case TimberBuildPreviewKind.SawhorseBench: wood = 2; break;
                case TimberBuildPreviewKind.StoneFireplace: stone = 4; break;
            }
        }

        private static string BuildRequirementText(int wood, int fiber, int stone)
        {
            string s = string.Empty;
            if (wood > 0) s += $"{wood}x Dal/Odun";
            if (fiber > 0) s += (s.Length > 0 ? " + " : "") + $"{fiber}x Bitki Lifi";
            if (stone > 0) s += (s.Length > 0 ? " + " : "") + $"{stone}x Taş";
            return s;
        }

        public void CancelPreview()
        {
            _activeKind = TimberBuildPreviewKind.None;
            if (_ghostGo != null) _ghostGo.SetActive(false);
        }

        public void StartToolPreview(TimberBuildPreviewKind kind, float initialYawDeg, out string feedback)
        {
            _activeKind = kind;
            _ignorePlaceUntilFrame = Time.frameCount + 2;
            _previewYawDeg = Mathf.Round(initialYawDeg / 45f) * 45f;
            feedback = $"📐 Önizleme Aktif ({GetKindLabel(kind)}) | [R] 45° Döndür | [Sol Tık] Dış Çizgi Planı Sabitle | [Sağ Tık / ESC] İptal";
        }

        /// <summary>
        /// [T] Insa Menusu acikken ekranin ustunde (en ust katmanda, ana menuyu kapatmadan!) 6 butonluk yatay serit cizer.
        /// </summary>
        public bool DrawMindMenuConstructionPanel(
            float playerYawDeg,
            bool isCreativeMode,
            out string feedback)
        {
            feedback = string.Empty;
            float panelW = Mathf.Min(Screen.width - 24f, 1140f);
            float panelH = 104f;
            float x = (Screen.width - panelW) * 0.5f;
            float y = 6f;

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.14f, 0.11f, 0.08f, 0.96f);
            GUI.Box(new Rect(x, y, panelW, panelH), "🪵 TEK-PARÇA ŞANTİYE, KAPI/PENCERE & EV İÇİ MOBİLYA (Tıkla -> 1:1 3B Önizleme | [R] Döndür | [Sol Tık] Kur)");

            float pad = 7f;
            float btnW = (panelW - pad * 7f) / 6f;
            float row1Y = y + 24f;
            float row2Y = y + 63f;
            float btnH = 33f;

            // 1. Satir: Santiye & Atolye
            GUI.backgroundColor = new Color(0.32f, 0.52f, 0.28f, 1.0f);
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 0, row1Y, btnW, btnH), isCreativeMode ? "🛞 Kütük Arabası" : "🛞 Kütük Arabası (2D+2L)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.LogArchCart, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 1, row1Y, btnW, btnH), isCreativeMode ? "🪜 Ahşap İskele" : "🪜 İskele (4D+2L)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.WoodScaffolding, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 2, row1Y, btnW, btnH), isCreativeMode ? "🪢 Parbuckle Rampa" : "🪢 Rampa (2D+2L)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.ParbuckleRamp2To1, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 3, row1Y, btnW, btnH), isCreativeMode ? "🏗️ Gin Pole Vinç" : "🏗️ Vinç (2M+4L)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.GinPoleHoist, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 4, row1Y, btnW, btnH), isCreativeMode ? "🪚 Bıçkı Sehpası" : "🪚 Sehpa (2 Odun)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.SawhorseBench, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 5, row1Y, btnW, btnH), isCreativeMode ? "🔥 Taş Şömine & Baca" : "🔥 Şömine (6 Taş)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.StoneFireplace, playerYawDeg, out feedback);
                return true;
            }

            // 2. Satir: Kapi, Kepenk ve Ev Ici Yasam Mobilyalari
            GUI.backgroundColor = new Color(0.55f, 0.40f, 0.24f, 1.0f);
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 0, row2Y, btnW, btnH), isCreativeMode ? "🚪 Menteşeli Kapı" : "🚪 Kapı (2 Kalas)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.HingedDoor, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 1, row2Y, btnW, btnH), isCreativeMode ? "🪟 Pencere Kepengi" : "🪟 Kepenk (2 Kalas)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.WindowShutter, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 2, row2Y, btnW, btnH), isCreativeMode ? "🛏️ Ahşap Yatak" : "🛏️ Yatak (2 Kalas+Lif)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.WoodBed, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 3, row2Y, btnW, btnH), isCreativeMode ? "📦 Eşya Sandığı" : "📦 Sandık (2 Kalas)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.StorageChest, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 4, row2Y, btnW, btnH), isCreativeMode ? "🪑 Masa & Bank" : "🪑 Masa & Bank (2 Kalas)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.TableAndBench, playerYawDeg, out feedback);
                return true;
            }
            if (GUI.Button(new Rect(x + pad + (btnW + pad) * 5, row2Y, btnW, btnH), isCreativeMode ? "🕯️ Duvar Meşalesi" : "🕯️ Meşale (1 Dal+Lif)"))
            {
                GUI.backgroundColor = prevBg;
                StartToolPreview(TimberBuildPreviewKind.WallTorchSconce, playerYawDeg, out feedback);
                return true;
            }

            GUI.backgroundColor = prevBg;
            return false;
        }

        private static bool TryPeekAvailableRealLog(
            Transform playerTr,
            Stage1LogArchCartController logCartCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1TimberSiteRigController siteRigCtrl,
            out int sourceKind, // 1=Cart, 2=Shoulder, 3=HoistRig
            out float lengthM,
            out float diamM,
            out byte woodMat)
        {
            if (logCartCtrl.TryPeekBoundLog(out lengthM, out diamM, out woodMat))
            {
                sourceKind = 1;
                return true;
            }
            if (holsterCtrl.IsCarryingShoulderLog)
            {
                sourceKind = 2;
                lengthM = Mathf.Clamp(holsterCtrl.ShoulderLogExactLengthM, 1.0f, 5.0f);
                diamM = Mathf.Clamp(holsterCtrl.ShoulderLogDiameterM, 0.36f, 0.56f);
                woodMat = holsterCtrl.ShoulderLogMaterialId != 0 ? holsterCtrl.ShoulderLogMaterialId : MaterialIdCatalog.OakWood;
                return true;
            }
            if (siteRigCtrl.TryPeekCompletedHoistLog(playerTr.position, 6.8f, out lengthM, out diamM, out woodMat))
            {
                sourceKind = 3;
                return true;
            }
            sourceKind = 0;
            lengthM = 0f;
            diamM = 0.42f;
            woodMat = MaterialIdCatalog.OakWood;
            return false;
        }

        private static bool TryConsumeAvailableRealLog(
            int sourceKind,
            Transform playerTr,
            Stage1LogArchCartController logCartCtrl,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1TimberSiteRigController siteRigCtrl,
            out float lengthM,
            out float diamM,
            out byte woodMat)
        {
            if (sourceKind == 1) return logCartCtrl.TryConsumeBoundLogForPlacement(out lengthM, out diamM, out woodMat);
            if (sourceKind == 2 && holsterCtrl.IsCarryingShoulderLog)
            {
                lengthM = Mathf.Clamp(holsterCtrl.ShoulderLogExactLengthM, 1.0f, 5.0f);
                diamM = Mathf.Clamp(holsterCtrl.ShoulderLogDiameterM, 0.36f, 0.56f);
                woodMat = holsterCtrl.ShoulderLogMaterialId != 0 ? holsterCtrl.ShoulderLogMaterialId : MaterialIdCatalog.OakWood;
                holsterCtrl.ClearCarriedShoulderLog();
                return true;
            }
            if (sourceKind == 3) return siteRigCtrl.TryConsumeCompletedHoistLog(playerTr.position, 6.8f, out lengthM, out diamM, out woodMat);

            lengthM = 0f;
            diamM = 0.42f;
            woodMat = MaterialIdCatalog.OakWood;
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
            WorldLooseBuildSystem looseBuildSystem,
            Stage1LogArchCartController logCartCtrl,
            Stage1RigidTimberBridge rigidTimberBridge,
            Stage1StructuralCollapseBridge collapseBridge,
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

            // 3 Kaynakli Gercek Kutuk Kontrolu: 1) El Arabasi, 2) Omuzda Kutuk (<=4.25m), 3) Rampa/Vinc Tepe Askisi!
            bool hasRealLog = TryPeekAvailableRealLog(
                playerTr, logCartCtrl, holsterCtrl, siteRigCtrl,
                out int logSourceKind, out float availLogLen, out float availLogDiam, out byte availWoodMat);

            VisualInventorySlot activeSlot = holsterCtrl.InventoryUi.GetActiveHotbarSlot();
            bool holdingPlank = !activeSlot.IsEmpty && activeSlot.Kind == InventoryItemKind.TimberPlank;
            bool holdingAxe = !activeSlot.IsEmpty && activeSlot.Kind == InventoryItemKind.ToolAxe;

            TimberBuildPreviewKind effectiveKind = HasActiveToolPreview
                ? _activeKind
                : (hasRealLog
                    ? TimberBuildPreviewKind.RealLogFromCart
                    : (holdingPlank ? TimberBuildPreviewKind.PlankCarpentry : TimberBuildPreviewKind.None));

            if (_assembly.Active)
            {
                if (_ghostGo != null) _ghostGo.SetActive(false);
                int pct = Mathf.RoundToInt(HandAssemblyProgress01 * 100f);
                hudHint = $"🛠️ Elleriyle {GetKindLabel(_assembly.Kind)} monte ediliyor... %{pct}";
                return false;
            }

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (effectiveKind == TimberBuildPreviewKind.None)
            {
                if (_ghostGo != null) _ghostGo.SetActive(false);
                if (TryFindAimedBlueprint(cam, playerTr, out int aimedIdx, out string aimedBpLbl))
                {
                    PinnedBlueprint bp = _pinnedBlueprints[aimedIdx];
                    BlueprintSlotSpec[] slots = GetSlotsForKind(bp.Kind);
                    int filled = EvaluateSpatialSlots(bp.Kind, bp.Position, bp.YawDeg, looseBuildSystem.SnapshotPointCloud(), _slotFilledScratch, _slotMatchedIdxScratch);
                    string checklist = string.Empty;
                    for (int s = 0; s < slots.Length; s++)
                    {
                        checklist += (s > 0 ? " | " : "") + (_slotFilledScratch[s] ? "✅" : "❌") + slots[s].Label;
                    }
                    hudHint = $"📐 {aimedBpLbl} [{filled}/{slots.Length}]: {checklist}  ([T] Doğrula/Kaldır)";
                    return false;
                }

                // Faz M1 & M2: Elde Balta varken direk olugu, yan zivana, eyer kertigi veya kesim metre cetvelini canli goster!
                if (holdingAxe && cam != null)
                {
                    if (rigidTimberBridge.TryInspectAxeCarpentryTarget(
                            cam.transform.position, cam.transform.forward, 3.35f, shiftHeld, altHeld, worldChunks, out string carpHint))
                    {
                        hudHint = carpHint;
                    }
                    else if (collapseBridge.TryInspectAimedFallenLogCutRuler(
                                 cam.transform.position, cam.transform.forward, 3.35f, out float lM, out float rM, out float totM))
                    {
                        hudHint = $"📏 AĞAÇ KESİM CETVELİ: Sol {lM:F2}m | Sağ {rM:F2}m (Toplam {totM:F2}m) — [Sol Tık] Baltayla Kes";
                    }
                }
                return false;
            }

            // Iptal ([Sag Tik] / [ESC] / [Q]) — sadece arac onizlemesi icin
            if (HasActiveToolPreview && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)))
            {
                CancelPreview();
                miningCtrl.LastFeedbackText = "Önizleme iptal edildi.";
                return true;
            }

            // [R]: 15 derece dondur ([Shift+R] 5 derece hassas dondurme!)
            if (Input.GetKeyDown(KeyCode.R))
            {
                float stepDeg = shiftHeld ? 5f : 15f;
                _previewYawDeg = (_previewYawDeg + stepDeg) % 360f;
            }

            // [V]: Gercek kutuk icin Dikey <-> Yatay; TimberPlank icin Doseme -> Merdiven -> Cati gecisi!
            if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart && Input.GetKeyDown(KeyCode.V))
            {
                _logVerticalMode = !_logVerticalMode;
            }
            else if (effectiveKind == TimberBuildPreviewKind.PlankCarpentry && Input.GetKeyDown(KeyCode.V))
            {
                _plankCarpentryMode = (_plankCarpentryMode + 1) % 3;
            }

            // BIRLESIK NISANGAH RAYCAST (Faz M1): Hem voksel zemini hem de RigidTimber direk/kiris/dosemeleri yakalar!
            Vector3 fwdFlat = cam != null ? new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z).normalized : playerTr.forward;
            if (fwdFlat.sqrMagnitude < 1e-4f) fwdFlat = Vector3.forward;

            float bestDist = 7.5f;
            Vector3 targetPos = playerTr.position + fwdFlat * 3.2f;
            bool hitTimber = false;
            int hitTimberIdx = -1;

            if (miningCtrl.HasAimHit && miningCtrl.AimHit.distance <= bestDist)
            {
                bestDist = miningCtrl.AimHit.distance;
                targetPos = miningCtrl.AimHit.point;
            }
            if (cam != null && rigidTimberBridge.TryRaycastAnyPiece(
                    cam.transform.position, cam.transform.forward, bestDist, out int tIdx, out Vector3 tHitPt, out _, out float tDist))
            {
                bestDist = tDist;
                targetPos = tHitPt;
                hitTimber = true;
                hitTimberIdx = tIdx;
            }
            _ = hitTimberIdx;

            // A) TIMBER PLANK (KALAS) DOSEME / MERDIVEN / CATI MODU (Faz M4)
            if (effectiveKind == TimberBuildPreviewKind.PlankCarpentry)
            {
                rigidTimberBridge.TryEvaluateAndPlacePlankCarpentry(
                    _plankCarpentryMode,
                    targetPos,
                    _previewYawDeg,
                    executePlace: false,
                    sharedVoxelMat,
                    worldChunks,
                    out Vector3 pPos,
                    out Quaternion pRot,
                    out Vector3 pSize,
                    out hudHint);

                EnsurePlankGhostVisual(pSize);
                SetGhostTintColor(new Color(0.38f, 0.92f, 0.52f, 0.56f));
                if (_ghostGo != null)
                {
                    _ghostGo.SetActive(true);
                    _ghostGo.transform.position = pPos;
                    _ghostGo.transform.rotation = pRot;
                }

                if (Time.frameCount >= _ignorePlaceUntilFrame && Input.GetMouseButtonDown(0))
                {
                    if (!holsterCtrl.IsCreativeMode && !holsterCtrl.InventoryUi.TryConsumeActiveHotbarSlot(1))
                    {
                        miningCtrl.LastFeedbackText = "⚠️ Kalas kalmadı! Bıçkı Sehpasında [E] ile yeni kalas biçin.";
                        return true;
                    }
                    rigidTimberBridge.TryEvaluateAndPlacePlankCarpentry(
                        _plankCarpentryMode, targetPos, _previewYawDeg, executePlace: true, sharedVoxelMat, worldChunks,
                        out _, out _, out _, out string placedPlankMsg);
                    holsterCtrl.ApplySelectedInventoryHotbarSlot(miningCtrl);
                    miningCtrl.LastFeedbackText = placedPlankMsg;
                    return true;
                }
                return false;
            }

            // B) [T] -> [7] SANTIYE VE EV-ICI PLANLARI (2./3. kat dosemesini ve pencere yuksekligini tanir — Faz M5)
            if (effectiveKind != TimberBuildPreviewKind.RealLogFromCart)
            {
                if (effectiveKind == TimberBuildPreviewKind.WindowShutter && hitTimber)
                {
                    targetPos.y = Mathf.Max(targetPos.y, playerTr.position.y + 0.9f);
                }
                else if (effectiveKind == TimberBuildPreviewKind.WallTorchSconce && hitTimber)
                {
                    targetPos.y = Mathf.Max(targetPos.y, playerTr.position.y + 1.45f);
                }
                else if (worldChunks != null)
                {
                    float refY = Mathf.Max(playerTr.position.y, targetPos.y) + 2.5f;
                    float yC = Stage1LogArchCartController.SampleTerrainY(worldChunks, targetPos.x, targetPos.z, refY);
                    float y1 = Stage1LogArchCartController.SampleTerrainY(worldChunks, targetPos.x + 0.65f, targetPos.z, refY);
                    float y2 = Stage1LogArchCartController.SampleTerrainY(worldChunks, targetPos.x - 0.65f, targetPos.z, refY);
                    float y3 = Stage1LogArchCartController.SampleTerrainY(worldChunks, targetPos.x, targetPos.z + 0.65f, refY);
                    float y4 = Stage1LogArchCartController.SampleTerrainY(worldChunks, targetPos.x, targetPos.z - 0.65f, refY);
                    float terrainMax = Mathf.Max(yC, Mathf.Max(Mathf.Max(y1, y2), Mathf.Max(y3, y4)));
                    if (rigidTimberBridge.TrySampleSupportingFloorTopY(targetPos.x, targetPos.z, refY, out float deckTopY) && deckTopY > terrainMax + 0.2f)
                    {
                        targetPos.y = deckTopY;
                    }
                    else
                    {
                        targetPos.y = terrainMax;
                    }
                }
                else
                {
                    targetPos.y = playerTr.position.y;
                }
            }

            // C) GERCEK KUTUK YERLESTIRME (SIFIR IC ICE GECME + OLUK KONTROLU + ARTAN KUTUK DUSURME — Faz M3 & M4)
            // Rampa / Gin Pole Vinc askisindan alinan ust kat kutukleri varsayilan olarak YATAY (duvar/kiris) modunda acilir!
            bool effectiveVertical = logSourceKind == 3 ? false : _logVerticalMode;
            Stage1RigidTimberBridge.HorizontalPlacementEval horizEval = default;
            float ghostLen = hasRealLog ? availLogLen : 4.5f;
            Color ghostTint = new Color(0.96f, 0.84f, 0.42f, 0.58f);

            if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart && !effectiveVertical)
            {
                horizEval = rigidTimberBridge.EvaluateSmartHorizontalPlacement(targetPos, _previewYawDeg, availLogLen, availLogDiam, worldChunks);
                ghostLen = Mathf.Max(0.6f, horizEval.ClearBodyLengthM);
                ghostTint = !horizEval.CanPlace
                    ? new Color(0.96f, 0.22f, 0.18f, 0.70f) // Kirmizi: Oluk acilmamis veya boy kisa!
                    : (horizEval.Mode == Stage1RigidTimberBridge.HorizontalSnapMode.FreeGroundSill
                        ? new Color(0.96f, 0.84f, 0.42f, 0.58f)
                        : new Color(0.30f, 0.94f, 0.48f, 0.64f)); // Yesil: Oluklara / Yuvaya / Kertige tam kilitlendi!
            }

            EnsureGhostVisual(effectiveKind, ghostLen, effectiveVertical);
            SetGhostTintColor(ghostTint);
            if (_ghostGo != null)
            {
                _ghostGo.SetActive(true);
                if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart)
                {
                    if (effectiveVertical)
                    {
                        _ghostGo.transform.position = targetPos + Vector3.up * (availLogLen * 0.5f);
                        _ghostGo.transform.rotation = Quaternion.Euler(0f, _previewYawDeg, 0f);
                    }
                    else
                    {
                        _ghostGo.transform.position = horizEval.CenterPos;
                        _ghostGo.transform.rotation = Quaternion.Euler(0f, horizEval.YawDeg, 0f) * Quaternion.Euler(0f, 0f, 90f);
                    }
                }
                else
                {
                    _ghostGo.transform.position = targetPos;
                    _ghostGo.transform.rotation = Quaternion.Euler(0f, _previewYawDeg, 0f);
                }
            }

            string srcTag = logSourceKind == 2 ? "Omuzdan" : (logSourceKind == 3 ? "Rampa/Vinç Askısından" : "Arabadan");
            if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart)
            {
                if (effectiveVertical)
                {
                    hudHint = $"🪵 ANA DİREK DİKME ({availLogLen:F2}m — {srcTag}) | [V] Yatay Mod | [Sol Tık] Çukura Dik";
                }
                else
                {
                    hudHint = horizEval.Mode switch
                    {
                        Stage1RigidTimberBridge.HorizontalSnapMode.BlockedMissingGroove =>
                            "⛔ OLUK AÇILMAMIŞ! İç içe geçme yasak — önce [1] Balta ile iki direğin birbirine bakan iç yüzüne 2'şer kez vurup 3B Dikey Oluk açın!",
                        Stage1RigidTimberBridge.HorizontalSnapMode.BlockedTooShort =>
                            $"⛔ KÜTÜK KISA GELİYOR! Eldeki: {availLogLen:F2}m < Gerekli: {horizEval.RequiredTotalCutM:F2}m — daha uzun kütük getirin!",
                        Stage1RigidTimberBridge.HorizontalSnapMode.GroovedPostInfill =>
                            $"✅ {horizEval.CourseNumber}. SIRA OLUKLU DUVAR ({srcTag}): {horizEval.ClearBodyLengthM:F2}m takılacak | {horizEval.LeftoverLengthM:F2}m artıp yere düşecek | [Sol Tık] Oluğa Sür",
                        Stage1RigidTimberBridge.HorizontalSnapMode.SideMortiseMainGirder =>
                            $"✅ YAN ZIVANALI ANA KAT KİRİŞİ ({srcTag}): {horizEval.ClearBodyLengthM:F2}m oturacak | {horizEval.LeftoverLengthM:F2}m artacak | [Sol Tık] Yuvaya Oturt",
                        Stage1RigidTimberBridge.HorizontalSnapMode.SaddleNotchCrossStack =>
                            $"✅ 90° EYER KERTİĞİ YIĞMA ({srcTag}): {horizEval.ClearBodyLengthM:F2}m oyuğa oturacak | [Sol Tık] Bindir",
                        _ =>
                            $"🪵 TABAN KÜTÜĞÜ ({availLogLen:F2}m — {srcTag}) | [V] Dikey | [R] 15° / [Shift+R] 5° ({_previewYawDeg:0}°) | [Sol Tık] Yatır"
                    };
                }
            }
            else
            {
                hudHint = $"📐 {GetKindLabel(effectiveKind)} PROJE PLANI | [R] 15° Döndür ({_previewYawDeg:0}°) | [Sol Tık] Planı Sabitle | [Sağ Tık / ESC] İptal";
            }

            if (Time.frameCount >= _ignorePlaceUntilFrame && Input.GetMouseButtonDown(0))
            {
                if (effectiveKind == TimberBuildPreviewKind.RealLogFromCart)
                {
                    if (!effectiveVertical && !horizEval.CanPlace)
                    {
                        miningCtrl.LastFeedbackText = hudHint;
                        return true;
                    }

                    if (TryConsumeAvailableRealLog(logSourceKind, playerTr, logCartCtrl, holsterCtrl, siteRigCtrl, out float len, out float diam, out byte wMat))
                    {
                        TimberWoodSpecies sp = wMat == MaterialIdCatalog.Ironwood ? TimberWoodSpecies.Ironwood : TimberWoodSpecies.Oak;
                        if (effectiveVertical)
                        {
                            rigidTimberBridge.TryTipInLogIntoHole(targetPos, len, diam, sp, sharedVoxelMat, worldChunks, out string tipMsg);
                            miningCtrl.LastFeedbackText = tipMsg;
                        }
                        else
                        {
                            if (rigidTimberBridge.TryPlaceSmartHorizontalTimber(
                                    targetPos, _previewYawDeg, len, diam, sp, sharedVoxelMat, worldChunks, out float leftoverLen, out string hMsg))
                            {
                                // Artan kutuk >= 0.85m ise kaybolmaz -> bulundugun kat dosemesinin veya zeminin ustune gercek kutuk olarak duser!
                                if (leftoverLen >= 0.85f)
                                {
                                    Vector3 rightFlat = new Vector3(fwdFlat.z, 0f, -fwdFlat.x);
                                    Vector3 dropPos = playerTr.position + rightFlat * 1.20f + fwdFlat * 0.85f;
                                    dropPos.y = rigidTimberBridge.ResolveSupportingSurfaceY(dropPos.x, dropPos.z, playerTr.position.y + 1.2f) + 0.38f;
                                    collapseBridge.SpawnExactLengthFallenLog(dropPos, fwdFlat, wMat, leftoverLen, diam, sharedVoxelMat);
                                }
                            }
                            miningCtrl.LastFeedbackText = hMsg;
                        }
                    }
                    return true;
                }

                // Ev ici mobilya / kapi / kepenk / atolye secimlerinde oyuncunun envanterinde Kalas/Tas varsa DOGRUDAN kurar (pencere/duvar yuksekligine de takilabilir!);
                // envanterinde yoksa yere 3B proje kilavuz plani sabitler!
                InteriorFixtureKind? directFx = effectiveKind switch
                {
                    TimberBuildPreviewKind.SawhorseBench => InteriorFixtureKind.SawhorseBench,
                    TimberBuildPreviewKind.StoneFireplace => InteriorFixtureKind.StoneFireplaceWithChimney,
                    TimberBuildPreviewKind.WoodBed => InteriorFixtureKind.WoodBed,
                    TimberBuildPreviewKind.HingedDoor => InteriorFixtureKind.HingedDoor,
                    TimberBuildPreviewKind.WindowShutter => InteriorFixtureKind.WindowShutter,
                    TimberBuildPreviewKind.StorageChest => InteriorFixtureKind.StorageChest,
                    TimberBuildPreviewKind.TableAndBench => InteriorFixtureKind.TableAndBench,
                    TimberBuildPreviewKind.WallTorchSconce => InteriorFixtureKind.WallTorchSconce,
                    _ => null
                };
                if (directFx.HasValue && interiorCtrl.TryPlaceInteriorFixture(
                        directFx.Value, targetPos, _previewYawDeg, holsterCtrl.IsCreativeMode, holsterCtrl, sharedVoxelMat, out string placedFxMsg, rigidTimberBridge))
                {
                    miningCtrl.LastFeedbackText = placedFxMsg;
                    CancelPreview();
                    return true;
                }

                // Santiye araclari (veya envanterde hazir kalas/tas yoksa) icin dis cizgili yari-seffaf 3B Proje Izi (Kilavuz) yerlestirir!
                PinWireframeBlueprint(effectiveKind, targetPos, _previewYawDeg);
                miningCtrl.LastFeedbackText =
                    $"📐 {GetKindLabel(effectiveKind)} görsel kılavuz planı yerleştirildi! Malzemeleri içine yere diz, sonra [T] → Kart/Plan Üstünde [Sağ Tık / Doğrula] ile el ile inşa et!";
                CancelPreview();
                return true;
            }

            return HasActiveToolPreview;
        }

        private void PinWireframeBlueprint(TimberBuildPreviewKind kind, Vector3 worldPos, float yawDeg)
        {
            EnsureGhostMaterial();
            GameObject bpGo = new GameObject($"PinnedBlueprint_{kind}_{_pinnedBlueprints.Count}");
            bpGo.transform.position = worldPos;
            bpGo.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            _meshScratch.Clear();
            BuildWireframeSilhouette(_meshScratch, kind, 4.5f);
            Mesh mesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_Blueprint_{kind}");
            bpGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = bpGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ghostMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _pinnedBlueprints.Add(new PinnedBlueprint
            {
                Kind = kind,
                Position = worldPos,
                YawDeg = yawDeg,
                RootGo = bpGo
            });
        }

        private void EnsureGhostMaterial()
        {
            if (_ghostMat != null) return;
            Shader? sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _ghostMat = new Material(sh);
            Color outlineCol = new Color(0.96f, 0.84f, 0.42f, 0.58f); // Yari-seffaf Da Vinci altin-parsomen dis cizgi!
            _ghostMat.color = outlineCol;
            if (_ghostMat.HasProperty("_Surface")) _ghostMat.SetFloat("_Surface", 1f);
            if (_ghostMat.HasProperty("_Blend")) _ghostMat.SetFloat("_Blend", 0f);
            if (_ghostMat.HasProperty("_SrcBlend")) _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_ghostMat.HasProperty("_DstBlend")) _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_ghostMat.HasProperty("_ZWrite")) _ghostMat.SetInt("_ZWrite", 0);
            _ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _ghostMat.renderQueue = 3250; // Bulutlarin (3000..3100) ustunde, opak zemin/agac derinlik testi (ZTest LEqual) korunarak cizilir!
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", outlineCol);
        }

        private void SetGhostTintColor(Color col)
        {
            EnsureGhostMaterial();
            if (_ghostMat == null) return;
            _ghostMat.color = col;
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", col);
        }

        private void EnsurePlankGhostVisual(Vector3 fullSize)
        {
            if (_ghostGo != null && _lastBuiltKind == TimberBuildPreviewKind.PlankCarpentry &&
                Vector3.Distance(_lastBuiltPlankSize, fullSize) < 0.05f)
            {
                return;
            }
            if (_ghostGo != null) UnityEngine.Object.Destroy(_ghostGo);
            _ghostGo = new GameObject("TimberPreviewGhost_PlankCarpentry");
            _lastBuiltKind = TimberBuildPreviewKind.PlankCarpentry;
            _lastBuiltPlankSize = fullSize;

            EnsureGhostMaterial();
            _meshScratch.Clear();
            Vector3 h = fullSize * 0.5f;
            EmitWireframeBox(_meshScratch, -h, h, 0.028f, MaterialIdCatalog.WoodPlank);
            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, "Mesh_Ghost_PlankCarpentry");
            _ghostGo.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = _ghostGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ghostMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
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

            EnsureGhostMaterial();

            _meshScratch.Clear();
            BuildWireframeSilhouette(_meshScratch, kind, logLen);

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_Ghost_{kind}");
            _ghostGo.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = _ghostGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ghostMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void BuildWireframeSilhouette(List<FacetedVoxelTriangle> dst, TimberBuildPreviewKind kind, float logLen)
        {
            if (kind == TimberBuildPreviewKind.RealLogFromCart)
            {
                EmitWireframeBox(dst, new Vector3(-0.24f, -logLen * 0.5f, -0.24f), new Vector3(0.24f, logLen * 0.5f, 0.24f), 0.030f, MaterialIdCatalog.OakWood);
                // Uzun kutuklerde (orn. 14-16m) goz hizasinda da net gorunmesi icin her 1.5 metrede bir ara kusak halkasi ciz!
                for (float y = -logLen * 0.5f + 1.5f; y < logLen * 0.5f - 0.5f; y += 1.5f)
                {
                    EmitWireframeBox(dst, new Vector3(-0.25f, y - 0.03f, -0.25f), new Vector3(0.25f, y + 0.03f, 0.25f), 0.025f, MaterialIdCatalog.OakWood);
                }
                return;
            }

            // Bitmis arac seklinde DEGIL -> Oyuncunun yere tek tek dizecegi HER BIR MATERYAL YUVASININ kendi sekli (Tekerlek halkasi, dal/odun cubugu, lif dugumu, tas blok)!
            BlueprintSlotSpec[] slots = GetSlotsForKind(kind);
            for (int s = 0; s < slots.Length; s++)
            {
                ref readonly BlueprintSlotSpec slot = ref slots[s];
                Vector3 c = slot.LocalOffset;
                Vector3 h = slot.HalfSize;
                if (slot.Material == RequiredSlotMaterial.WoodenWheel)
                {
                    EmitWireframeWheel(dst, c, radius: Mathf.Max(h.y, h.z), halfThick: h.x, lineT: 0.024f, MaterialIdCatalog.OakWood);
                }
                else
                {
                    byte matId = slot.Material switch
                    {
                        RequiredSlotMaterial.Stone => MaterialIdCatalog.StoneBrick,
                        RequiredSlotMaterial.Clay => MaterialIdCatalog.Clay,
                        RequiredSlotMaterial.Fiber => MaterialIdCatalog.WoodPlank,
                        _ => MaterialIdCatalog.OakWood
                    };
                    EmitWireframeBox(dst, c - h, c + h, 0.022f, matId);
                }
            }
        }

        private static void EmitWireframeWheel(List<FacetedVoxelTriangle> dst, Vector3 center, float radius, float halfThick, float lineT, byte matId)
        {
            // 8 kenarli tekerlek cemberi + 2 capraz parmaklik (oyuncu bunun Ahsap Tekerlek yuvasi oldugunu aninda anlar!)
            const int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i / (float)segs) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segs) * Mathf.PI * 2f;
                Vector3 p0 = center + new Vector3(0f, Mathf.Sin(a0) * radius, Mathf.Cos(a0) * radius);
                Vector3 p1 = center + new Vector3(0f, Mathf.Sin(a1) * radius, Mathf.Cos(a1) * radius);
                Vector3 min = Vector3.Min(p0, p1) - new Vector3(halfThick, lineT, lineT);
                Vector3 max = Vector3.Max(p0, p1) + new Vector3(halfThick, lineT, lineT);
                EmitBox(dst, min, max, matId);
            }
            // Merkez gobek + dikey/yatay parmaklik
            EmitBox(dst, center - new Vector3(halfThick, radius * 0.92f, lineT), center + new Vector3(halfThick, radius * 0.92f, lineT), matId);
            EmitBox(dst, center - new Vector3(halfThick, lineT, radius * 0.92f), center + new Vector3(halfThick, lineT, radius * 0.92f), matId);
        }

        private static string GetKindLabel(TimberBuildPreviewKind kind) => kind switch
        {
            TimberBuildPreviewKind.LogArchCart => "Kütük El Arabası",
            TimberBuildPreviewKind.WoodScaffolding => "Ahşap İskele (2×2×2m)",
            TimberBuildPreviewKind.ParbuckleRamp2To1 => "Parbuckle 2:1 Rampa",
            TimberBuildPreviewKind.GinPoleHoist => "Gin Pole Vinç",
            TimberBuildPreviewKind.SawhorseBench => "Bıçkı Sehpası",
            TimberBuildPreviewKind.StoneFireplace => "Taş Şömine & Baca",
            TimberBuildPreviewKind.WoodBed => "Ahşap Yatak",
            TimberBuildPreviewKind.HingedDoor => "Menteşeli Ahşap Kapı",
            TimberBuildPreviewKind.WindowShutter => "Pencere Kepengi",
            TimberBuildPreviewKind.StorageChest => "Ahşap Eşya Sandığı",
            TimberBuildPreviewKind.TableAndBench => "Ahşap Masa & Bank",
            TimberBuildPreviewKind.WallTorchSconce => "Duvar Meşalesi",
            _ => "Kütük"
        };

        private static void EmitWireframeBox(List<FacetedVoxelTriangle> dst, Vector3 min, Vector3 max, float t, byte matId)
        {
            // 4 dikey kose kenari
            EmitBox(dst, new Vector3(min.x, min.y, min.z), new Vector3(min.x + t, max.y, min.z + t), matId);
            EmitBox(dst, new Vector3(max.x - t, min.y, min.z), new Vector3(max.x, max.y, min.z + t), matId);
            EmitBox(dst, new Vector3(min.x, min.y, max.z - t), new Vector3(min.x + t, max.y, max.z), matId);
            EmitBox(dst, new Vector3(max.x - t, min.y, max.z - t), new Vector3(max.x, max.y, max.z), matId);
            // 4 alt yatay kenar
            EmitBox(dst, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y + t, min.z + t), matId);
            EmitBox(dst, new Vector3(min.x, min.y, max.z - t), new Vector3(max.x, min.y + t, max.z), matId);
            EmitBox(dst, new Vector3(min.x, min.y, min.z), new Vector3(min.x + t, min.y + t, max.z), matId);
            EmitBox(dst, new Vector3(max.x - t, min.y, min.z), new Vector3(max.x, min.y + t, max.z), matId);
            // 4 ust yatay kenar
            EmitBox(dst, new Vector3(min.x, max.y - t, min.z), new Vector3(max.x, max.y, min.z + t), matId);
            EmitBox(dst, new Vector3(min.x, max.y - t, max.z - t), new Vector3(max.x, max.y, max.z), matId);
            EmitBox(dst, new Vector3(min.x, max.y - t, min.z), new Vector3(min.x + t, max.y, max.z), matId);
            EmitBox(dst, new Vector3(max.x - t, max.y - t, min.z), new Vector3(max.x, max.y, max.z), matId);
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
