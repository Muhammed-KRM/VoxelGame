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
    public enum TimberSiteRigKind : byte
    {
        WoodScaffolding = 0,
        ParbuckleRamp2To1 = 1,
        GinPoleHoist = 2
    }

    /// <summary>
    /// Asama 1.3: Survival Şantiye Kaldırma & Erişim Araçları (0 yeni doku VRAM):
    /// - Ahşap İskele (2x2x2m yürünebilir platform + tırmanma merdiveni, %100 iadeli söküm)
    /// - Parbuckle 2:1 Eğik Kütük Rampası (2 sırık + 2 lif halat, geri kaymayan mandallı 3B yuvarlama)
    /// - Gin Pole Makaralı Vinç (A-çerçeve + makara çarkı + gerdirme ipi, 3. kata dikey kiriş çekme)
    /// </summary>
    public sealed class Stage1TimberSiteRigController
    {
        public sealed class SiteRigInstance
        {
            public uint RigId;
            public TimberSiteRigKind Kind;
            public Vector3 Position;
            public float YawDeg;
            public GameObject? RootGo;
            public GameObject? HoistedLogGo;
            public TimberHoistState HoistState;
            public bool HasLoadedLog;
            public float LoadedLogLengthM;
            public float LoadedLogDiamM;
            public byte LoadedWoodMat;
        }

        private readonly List<SiteRigInstance> _rigs = new(16);
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(256);
        private uint _nextRigId = 1;

        public int RigCount => _rigs.Count;

        /// <summary>
        /// Faz M4: Oyuncu 2. veya 3. katta dururken 12.5m yakinindaki Parbuckle Rampa veya Gin Pole Vincin
        /// tepesine cekilip mandallanmis gercek kutugu 1:1 onizleme ve oluga surme icin okur/tuketir!
        /// </summary>
        public bool TryPeekCompletedHoistLog(Vector3 playerPos, float maxRadius, out float lengthM, out float diameterM, out byte woodMat)
        {
            lengthM = 0f;
            diameterM = 0.42f;
            woodMat = MaterialIdCatalog.OakWood;
            float bestSq = maxRadius * maxRadius;
            int bestIdx = -1;
            for (int i = 0; i < _rigs.Count; i++)
            {
                SiteRigInstance r = _rigs[i];
                if (!r.HasLoadedLog || r.HoistState.IsCompleted == 0) continue;
                // Yukari cekilmis kutuk yalnizca ust katta / iskelede (rampa veya vincin teslimat kotunda) duran oyuncuya onizleme acar;
                // zemin katta veya bahcede Bos El ile dolasirken ekranda hayalet direk cikarmaz!
                if (playerPos.y < r.Position.y + 1.60f) continue;
                float dx = r.Position.x - playerPos.x;
                float dz = r.Position.z - playerPos.z;
                float dSq = dx * dx + dz * dz;
                if (dSq <= bestSq)
                {
                    bestSq = dSq;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return false;
            SiteRigInstance chosen = _rigs[bestIdx];
            lengthM = Mathf.Clamp(chosen.LoadedLogLengthM, 1.0f, 16.0f);
            diameterM = Mathf.Clamp(chosen.LoadedLogDiamM, 0.34f, 0.56f);
            woodMat = chosen.LoadedWoodMat != 0 ? chosen.LoadedWoodMat : MaterialIdCatalog.OakWood;
            return true;
        }

        public bool TryConsumeCompletedHoistLog(Vector3 playerPos, float maxRadius, out float lengthM, out float diameterM, out byte woodMat)
        {
            lengthM = 0f;
            diameterM = 0.42f;
            woodMat = MaterialIdCatalog.OakWood;
            float bestSq = maxRadius * maxRadius;
            int bestIdx = -1;
            for (int i = 0; i < _rigs.Count; i++)
            {
                SiteRigInstance r = _rigs[i];
                if (!r.HasLoadedLog || r.HoistState.IsCompleted == 0) continue;
                if (playerPos.y < r.Position.y + 1.60f) continue;
                float dx = r.Position.x - playerPos.x;
                float dz = r.Position.z - playerPos.z;
                float dSq = dx * dx + dz * dz;
                if (dSq <= bestSq)
                {
                    bestSq = dSq;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return false;
            SiteRigInstance chosen = _rigs[bestIdx];
            lengthM = Mathf.Clamp(chosen.LoadedLogLengthM, 1.0f, 16.0f);
            diameterM = Mathf.Clamp(chosen.LoadedLogDiamM, 0.34f, 0.56f);
            woodMat = chosen.LoadedWoodMat != 0 ? chosen.LoadedWoodMat : MaterialIdCatalog.OakWood;
            chosen.HasLoadedLog = false;
            if (chosen.HoistedLogGo != null)
            {
                UnityEngine.Object.Destroy(chosen.HoistedLogGo);
                chosen.HoistedLogGo = null;
            }
            return true;
        }

        public void DisposeAll()
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                if (_rigs[i].RootGo != null) UnityEngine.Object.Destroy(_rigs[i].RootGo);
            }
            _rigs.Clear();
        }

        /// <summary>
        /// Survival malzeme kuraliyla yeni santiye araci kurar (Yaratici Modda bedava, Survival'da gercek malzeme gerekir).
        /// </summary>
        public bool TryBuildSiteRig(
            TimberSiteRigKind kind,
            Vector3 worldPos,
            float yawDeg,
            bool isCreativeMode,
            Stage1HolsterAndBackpackController holsterCtrl,
            Material? sharedVoxelMat,
            out string feedback)
        {
            if (!isCreativeMode && !TryConsumeRigMaterials(kind, holsterCtrl, out string missingMsg))
            {
                feedback = missingMsg;
                return false;
            }

            uint id = _nextRigId++;
            var rig = new SiteRigInstance
            {
                RigId = id,
                Kind = kind,
                Position = worldPos,
                YawDeg = yawDeg
            };

            GameObject root = new GameObject($"SiteRig_{kind}_{id}");
            root.transform.position = worldPos;
            root.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            rig.RootGo = root;

            _meshScratch.Clear();
            switch (kind)
            {
                case TimberSiteRigKind.WoodScaffolding:
                    BuildScaffoldingMesh(_meshScratch);
                    feedback = "🪜 Ahşap İskele (2×2×2m) kuruldu! Üstüne yürüyüp çıkabilir, [K] ile %100 malzeme iadesiyle sökebilirsiniz.";
                    break;
                case TimberSiteRigKind.ParbuckleRamp2To1:
                    BuildParbuckleRampMesh(_meshScratch);
                    rig.HoistState = TimberHoistPhysics.CreateParbuckleRamp(id, 0, 180f, 3.2f, 32f);
                    feedback = "🪵 Parbuckle 2:1 Eğik Rampa kuruldu (2 sırık + halat)! [E] ile kütük yükleyip [E Basılı Tut] ile yukarı yuvarlayın (bırakınca mandal tutar)!";
                    break;
                case TimberSiteRigKind.GinPoleHoist:
                    BuildGinPoleMesh(_meshScratch);
                    rig.HoistState = TimberHoistPhysics.CreateGinPole(id, 0, 180f, 6.2f, 4);
                    feedback = "🏗️ Gin Pole Makaralı Vinç kuruldu! [E] ile kiriş bağlayıp [E Basılı Tut] ile 3. kata çekin!";
                    break;
                default:
                    feedback = string.Empty;
                    break;
            }

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_Rig_{kind}_{id}");
            root.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = root.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;

            _rigs.Add(rig);
            return true;
        }

        public bool TryDismantleNearestRigKeyK(
            Vector3 playerPos,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1MatterCrystalAndMiningController miningCtrl,
            out string feedback)
        {
            feedback = string.Empty;
            int idx = FindNearestRig(playerPos, 3.8f);
            if (idx < 0) return false;
            SiteRigInstance rig = _rigs[idx];
            RefundRigMaterials(rig.Kind, holsterCtrl, miningCtrl);
            if (rig.HoistedLogGo != null) UnityEngine.Object.Destroy(rig.HoistedLogGo);
            if (rig.RootGo != null) UnityEngine.Object.Destroy(rig.RootGo);
            _rigs.RemoveAt(idx);
            feedback = $"♻️ {rig.Kind} [K] ile %100 malzeme iadesiyle söküldü!";
            return true;
        }

        /// <summary>
        /// [E] ile Rampa veya Gin Pole'a kütük yükler / halat çekerek 3B yukarı kaldırır! (Söküm yalnızca bağımsız [K] tuşundadır — koşarken Shift+E ile yanlışlıkla sökülmez!)
        /// </summary>
        public bool TryInteractWithRig(
            Vector3 playerPos,
            bool shiftHeld,
            bool eHeldContinuous,
            float dt,
            Stage1HolsterAndBackpackController holsterCtrl,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Material? sharedVoxelMat,
            Stage1LogArchCartController? logCartCtrl,
            Stage1StructuralCollapseBridge? collapseBridge,
            Stage1RigidTimberBridge? rigidTimberBridge,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            _ = shiftHeld;
            feedback = string.Empty;
            int idx = FindNearestRig(playerPos, 3.8f);
            if (idx < 0) return false;

            SiteRigInstance rig = _rigs[idx];
            if (rig.Kind == TimberSiteRigKind.WoodScaffolding) return false;

            // Rampa veya Gin Pole'a GERCEK kutuk yukleme (havadan sahte kutuk yaratilmaz!)
            if (!rig.HasLoadedLog)
            {
                if (eHeldContinuous) return false;

                float realLen = 0f;
                float realDiam = 0.42f;
                byte realMat = MaterialIdCatalog.OakWood;
                bool acquiredRealLog = false;

                if (logCartCtrl != null && logCartCtrl.TryConsumeBoundLogForPlacement(out float cLen, out float cDiam, out byte cMat))
                {
                    realLen = cLen;
                    realDiam = cDiam;
                    realMat = cMat;
                    acquiredRealLog = true;
                }
                else if (collapseBridge != null && collapseBridge.TryPeekFallenTreeLogForCartExact(rig.Position, 6.0f, out int fIdx, out byte fMat, out _, out float fExactLen, out float fDiam, out _))
                {
                    collapseBridge.CommitDetachFallenTreeLogForCart(fIdx);
                    realLen = fExactLen;
                    realDiam = fDiam;
                    realMat = fMat;
                    acquiredRealLog = true;
                }
                else if (rigidTimberBridge != null && rigidTimberBridge.TryPeekFallenPieceForCartExact(rig.Position, 6.0f, out int rIdx, out byte rMat, out _, out float rExactLen, out float rDiam, out _))
                {
                    rigidTimberBridge.CommitDetachFallenPieceForCart(rIdx);
                    realLen = rExactLen;
                    realDiam = rDiam;
                    realMat = rMat;
                    acquiredRealLog = true;
                }
                else if (holsterCtrl.IsCreativeMode)
                {
                    realLen = 4.4f;
                    realDiam = 0.44f;
                    realMat = MaterialIdCatalog.OakWood;
                    acquiredRealLog = true;
                }

                if (!acquiredRealLog)
                {
                    feedback = "⚠️ Önce Kütük El Arabasıyla (veya devrik ağaçtan) rampanın/vincin 5m yakınına gerçek bir kütük getirin!";
                    return true;
                }

                rig.HasLoadedLog = true;
                rig.LoadedLogLengthM = Mathf.Clamp(realLen, 1.5f, 14.0f);
                rig.LoadedLogDiamM = Mathf.Clamp(realDiam, 0.34f, 0.56f);
                rig.LoadedWoodMat = realMat != 0 ? realMat : MaterialIdCatalog.OakWood;
                float estMassKg = rig.LoadedLogLengthM * 24f;
                rig.HoistState = rig.Kind == TimberSiteRigKind.ParbuckleRamp2To1
                    ? TimberHoistPhysics.CreateParbuckleRamp(rig.RigId, 0, estMassKg, 3.2f, 30f)
                    : TimberHoistPhysics.CreateGinPole(rig.RigId, 0, estMassKg, 6.2f, 4);
                SpawnHoistedLogVisual(rig, sharedVoxelMat);
                feedback = rig.Kind == TimberSiteRigKind.ParbuckleRamp2To1
                    ? $"🪢 Gerçek kütük ({rig.LoadedLogLengthM:F2}m) Parbuckle 2:1 rampasına sarıldı! [E Basılı Tut] ile 2. kata yuvarlayın (bırakınca mandal tutar)."
                    : $"🪢 Gerçek kütük ({rig.LoadedLogLengthM:F2}m) Gin Pole kancasına bağlandı! [E Basılı Tut] ile 3. kata çekin.";
                return true;
            }

            if (eHeldContinuous && rig.HoistState.IsCompleted == 0)
            {
                float prevProg = rig.HoistState.ProgressM;
                TimberHoistPhysics.StepHoist(ref rig.HoistState, pullInput01: 1.0f, dt: Mathf.Max(0.016f, dt));
                UpdateHoistedLogTransform(rig);
                if (rig.HoistState.IsCompleted == 0 && rig.HoistState.ProgressM <= prevProg + 1e-5f)
                {
                    feedback = $"⚠️ Bu kütük ({rig.LoadedLogLengthM:F1}m, {rig.HoistState.LogMassKg:0}kg) bu rampa için çok ağır! Daha kısa kesin veya 4 makaralı Gin Pole Vinç kullanın.";
                    return true;
                }
                float pct = Mathf.Clamp01(rig.HoistState.ProgressM / Mathf.Max(0.1f, rig.HoistState.RampLengthM)) * 100f;
                feedback = rig.HoistState.IsCompleted != 0
                    ? "✅ Kütük üst kata ulaştı ve mandalla kilitlendi! Üst kata çıkıp doğrudan oluğa sürebilir veya [E] ile döşemeye indirebilirsiniz."
                    : $"🪢 Halat çekiliyor... %{pct:0} ({rig.HoistState.ProgressM:F1}m / {rig.HoistState.RampLengthM:F1}m | Gerilim: {rig.HoistState.RopeTensionN:0} N)";
                return true;
            }

            if (rig.HoistState.IsCompleted != 0)
            {
                // Eger oyuncu tepede mandalli bekleyen kutugu [E] ile cozmek isterse ust kat dosemesinin ustune gercek kutuk olarak indirir!
                // (Veya oyuncu dogrudan 2./3. kata cikip iki oluklu direk arasina [Sol Tik] yaparsa dogrudan askidan alip oluga surer!)
                rig.HasLoadedLog = false;
                if (rig.HoistedLogGo != null)
                {
                    UnityEngine.Object.Destroy(rig.HoistedLogGo);
                    rig.HoistedLogGo = null;
                }

                Quaternion rot = Quaternion.Euler(0f, rig.YawDeg, 0f);
                float topY = rig.Kind == TimberSiteRigKind.ParbuckleRamp2To1 ? 3.35f : 6.45f;
                float fwdOff = rig.Kind == TimberSiteRigKind.ParbuckleRamp2To1 ? 1.85f : 1.35f;
                Vector3 deliverPos = rig.Position + rot * new Vector3(0f, topY, fwdOff);
                if (rigidTimberBridge != null)
                {
                    deliverPos.y = Mathf.Max(deliverPos.y, rigidTimberBridge.ResolveSupportingSurfaceY(deliverPos.x, deliverPos.z, deliverPos.y + 1.0f) + 0.35f);
                }

                if (collapseBridge != null)
                {
                    Vector3 logDir = rot * Vector3.right;
                    collapseBridge.SpawnExactLengthFallenLog(deliverPos, logDir, rig.LoadedWoodMat, rig.LoadedLogLengthM, rig.LoadedLogDiamM, sharedVoxelMat);
                    feedback = $"🏗️ Kütük ({rig.LoadedLogLengthM:F2}m) üst kat döşemesine indirildi! ([E] Omuzla veya doğrudan rampa tepesindeyken oluğa [Sol Tık] sürebilirsiniz)";
                }
                else
                {
                    holsterCtrl.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.LogDebarked, rig.LoadedWoodMat, 2, miningCtrl);
                    feedback = "🪵 Üst kata çıkarılan kiriş teslim alındı!";
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ahsap iskele platformlarinin ustunde yurume ve merdiveninden tirmanma cozumu (0 B GC).
        /// </summary>
        public void ResolvePlayerScaffoldCollision(
            Transform playerTransform,
            float playerRadius,
            ref float verticalVelocity,
            ref byte isGroundedFlag)
        {
            Vector3 pPos = playerTransform.position;
            bool modified = false;

            for (int i = 0; i < _rigs.Count; i++)
            {
                SiteRigInstance rig = _rigs[i];
                if (rig.Kind != TimberSiteRigKind.WoodScaffolding) continue;

                Quaternion invRot = Quaternion.Inverse(Quaternion.Euler(0f, rig.YawDeg, 0f));
                Vector3 local = invRot * (pPos - rig.Position);

                // 1) Ust platform (y = 2.0m, 2x2m)
                if (Mathf.Abs(local.x) <= 1.08f + playerRadius && Mathf.Abs(local.z) <= 1.08f + playerRadius)
                {
                    if (local.y >= 1.62f && local.y <= 2.18f && verticalVelocity <= 0.65f)
                    {
                        pPos.y = rig.Position.y + 2.04f;
                        verticalVelocity = 0f;
                        isGroundedFlag = 1;
                        modified = true;
                        continue;
                    }

                    // 2) On kenardaki entegre iskele merdiveni (z: -1.15..-0.75, y: 0..2.0m) -> W ile tirman!
                    if (local.z <= -0.68f && local.z >= -1.25f && local.y >= -0.1f && local.y < 2.02f && Input.GetKey(KeyCode.W))
                    {
                        pPos.y += 2.6f * Time.deltaTime;
                        verticalVelocity = 0f;
                        isGroundedFlag = 1;
                        modified = true;
                    }
                }
            }

            if (modified) playerTransform.position = pPos;
        }

        private int FindNearestRig(Vector3 pos, float maxDist)
        {
            int best = -1;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < _rigs.Count; i++)
            {
                float dSq = (_rigs[i].Position - pos).sqrMagnitude;
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    best = i;
                }
            }
            return best;
        }

        private void SpawnHoistedLogVisual(SiteRigInstance rig, Material? sharedMat)
        {
            if (rig.HoistedLogGo != null) UnityEngine.Object.Destroy(rig.HoistedLogGo);
            GameObject go = new GameObject($"HoistedLog_{rig.RigId}");
            if (rig.RootGo != null) go.transform.SetParent(rig.RootGo.transform, false);

            _meshScratch.Clear();
            EmitBox(_meshScratch, new Vector3(-2.1f, -0.22f, -0.22f), new Vector3(2.1f, 0.22f, 0.22f), MaterialIdCatalog.OakWood);
            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_HoistedLog_{rig.RigId}");
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;

            rig.HoistedLogGo = go;
            UpdateHoistedLogTransform(rig);
        }

        private static void UpdateHoistedLogTransform(SiteRigInstance rig)
        {
            if (rig.HoistedLogGo == null) return;
            float t = Mathf.Clamp01(rig.HoistState.ProgressM / Mathf.Max(0.1f, rig.HoistState.RampLengthM));
            if (rig.Kind == TimberSiteRigKind.ParbuckleRamp2To1)
            {
                // Egik 2 sirik uzerinde (z: -2.4 -> +1.8, y: 0.35 -> 3.35) yuvarlanarak cikar!
                float z = Mathf.Lerp(-2.4f, 1.8f, t);
                float y = Mathf.Lerp(0.38f, 3.40f, t);
                rig.HoistedLogGo.transform.localPosition = new Vector3(0f, y, z);
                rig.HoistedLogGo.transform.localRotation = Quaternion.Euler(t * 720f, 0f, 0f);
            }
            else
            {
                // Gin Pole: dikey olarak 0.4m -> 6.4m yukari ceker!
                rig.HoistedLogGo.transform.localPosition = new Vector3(0f, Mathf.Lerp(0.45f, 6.20f, t), 0.75f);
                rig.HoistedLogGo.transform.localRotation = Quaternion.identity;
            }
        }

        private static bool TryConsumeRigMaterials(TimberSiteRigKind kind, Stage1HolsterAndBackpackController holster, out string missing)
        {
            missing = string.Empty;
            var ui = holster.InventoryUi;
            if (kind == TimberSiteRigKind.WoodScaffolding)
            {
                if (!ui.TryConsumeItemFromInventory(InventoryItemKind.Stick, 0, 4))
                {
                    missing = "🪜 Ahşap İskele için en az 4x Dal/Sırık (+ 2x Kalas + 2x Lif) gerekir!";
                    return false;
                }
                ui.TryConsumeItemFromInventory(InventoryItemKind.PlantFiber, 0, 2);
                return true;
            }
            if (kind == TimberSiteRigKind.ParbuckleRamp2To1)
            {
                if (!ui.TryConsumeItemFromInventory(InventoryItemKind.Stick, 0, 2))
                {
                    missing = "🪢 Parbuckle 2:1 Rampa için en az 2x Uzun Sırık + 2x Lif Halat gerekir!";
                    return false;
                }
                ui.TryConsumeItemFromInventory(InventoryItemKind.PlantFiber, 0, 2);
                return true;
            }
            // Gin Pole
            if (!ui.TryConsumeItemFromInventory(InventoryItemKind.Stick, 0, 2) &&
                !ui.TryConsumeItemFromInventory(InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood, 2))
            {
                missing = "🏗️ Gin Pole Vinç için en az 2x Uzun Direk/Meşe + 4x Lif Halat gerekir!";
                return false;
            }
            ui.TryConsumeItemFromInventory(InventoryItemKind.PlantFiber, 0, 4);
            return true;
        }

        private static void RefundRigMaterials(TimberSiteRigKind kind, Stage1HolsterAndBackpackController holster, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            int sticks = kind == TimberSiteRigKind.WoodScaffolding ? 4 : 2;
            int fiber = kind == TimberSiteRigKind.GinPoleHoist ? 4 : 2;
            holster.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.Stick, 0, sticks, miningCtrl);
            holster.InventoryUi.TryAddHarvestedWorldItem(InventoryItemKind.PlantFiber, 0, fiber, miningCtrl);
        }

        private static void BuildScaffoldingMesh(List<FacetedVoxelTriangle> dst)
        {
            // 4 kose dikmesi (2.0m boy)
            EmitBox(dst, new Vector3(-0.98f, 0f, -0.98f), new Vector3(-0.86f, 2.85f, -0.86f), MaterialIdCatalog.OakWood);
            EmitBox(dst, new Vector3( 0.86f, 0f, -0.98f), new Vector3( 0.98f, 2.85f, -0.86f), MaterialIdCatalog.OakWood);
            EmitBox(dst, new Vector3(-0.98f, 0f,  0.86f), new Vector3(-0.86f, 2.85f,  0.98f), MaterialIdCatalog.OakWood);
            EmitBox(dst, new Vector3( 0.86f, 0f,  0.86f), new Vector3( 0.98f, 2.85f,  0.98f), MaterialIdCatalog.OakWood);
            // Ust yurunebilir kalas platformu (y = 2.0m) + korkuluklar
            EmitBox(dst, new Vector3(-1.02f, 1.92f, -1.02f), new Vector3(1.02f, 2.04f, 1.02f), MaterialIdCatalog.WoodPlank);
            EmitBox(dst, new Vector3(-1.00f, 2.75f,  0.88f), new Vector3(1.00f, 2.85f, 0.98f), MaterialIdCatalog.TreatedTimberBeam);
            EmitBox(dst, new Vector3(-1.00f, 2.75f, -0.98f), new Vector3(-0.90f, 2.85f, 0.98f), MaterialIdCatalog.TreatedTimberBeam);
            EmitBox(dst, new Vector3( 0.90f, 2.75f, -0.98f), new Vector3( 1.00f, 2.85f, 0.98f), MaterialIdCatalog.TreatedTimberBeam);
            // Entegre on merdiven basamaklari
            for (int r = 0; r < 6; r++)
            {
                float ry = 0.30f + r * 0.30f;
                EmitBox(dst, new Vector3(-0.40f, ry, -1.02f), new Vector3(0.40f, ry + 0.06f, -0.92f), MaterialIdCatalog.WoodPlank);
            }
        }

        private static void BuildParbuckleRampMesh(List<FacetedVoxelTriangle> dst)
        {
            // 2 paralel egik sirik (z: -2.5 -> +1.9, y: 0 -> 3.2m)
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 1.15f;
                for (int seg = 0; seg < 8; seg++)
                {
                    float t0 = seg / 8f;
                    float t1 = (seg + 1) / 8f;
                    Vector3 min = new Vector3(x - 0.10f, Mathf.Lerp(0.05f, 3.10f, t0), Mathf.Lerp(-2.5f, 1.9f, t0));
                    Vector3 max = new Vector3(x + 0.10f, Mathf.Lerp(0.05f, 3.10f, t1) + 0.16f, Mathf.Lerp(-2.5f, 1.9f, t1));
                    EmitBox(dst, min, max, MaterialIdCatalog.OakWood);
                }
            }
            // Ust mandal kirisi
            EmitBox(dst, new Vector3(-1.35f, 3.15f, 1.80f), new Vector3(1.35f, 3.32f, 1.98f), MaterialIdCatalog.TreatedTimberBeam);
        }

        private static void BuildGinPoleMesh(List<FacetedVoxelTriangle> dst)
        {
            // A-cerceve 2 uzun direk (6.8m boy)
            EmitBox(dst, new Vector3(-1.20f, 0f, -0.15f), new Vector3(-0.96f, 6.80f, 0.15f), MaterialIdCatalog.OakWood);
            EmitBox(dst, new Vector3( 0.96f, 0f, -0.15f), new Vector3( 1.20f, 6.80f, 0.15f), MaterialIdCatalog.OakWood);
            // Ust makara kirisi + ahşap makara carki
            EmitBox(dst, new Vector3(-1.30f, 6.55f, -0.18f), new Vector3(1.30f, 6.82f, 0.95f), MaterialIdCatalog.TreatedTimberBeam);
            EmitBox(dst, new Vector3(-0.18f, 6.15f,  0.55f), new Vector3(0.18f, 6.55f, 0.95f), MaterialIdCatalog.WoodPlank);
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
