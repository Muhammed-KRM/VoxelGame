#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Diagnostics;
using Stratocraft.Physics.RigidTimber;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// KM-2.2 / DOK4 Bolum 5: Tam Boy Kutuk Tasiyan Kutuk El Arabasi (Log Arch) Kontrolcusu.
    /// - 2 donen buyuk ahsap tekerlek + dingil + A-kemer sasi + cekme kolu (0 yeni doku VRAM).
    /// - Sifir PhysX patlamasi: voksel zemin uzerinde clamped suspansiyon + Strapped Compound Mass CG dengesi.
    /// </summary>
    public sealed class Stage1LogArchCartController
    {
        public sealed class LogArchCartRuntime
        {
            public LogArchCartState State;
            public GameObject? RootGo;
            public Transform? ChassisTr;
            public Transform? LeftWheelTr;
            public Transform? RightWheelTr;
            public GameObject? BoundLogVisualGo;
            public byte BoundWoodMat;
            public int BoundSegments;
            public Vector3 Position;
            public float VerticalVelocity;
            public float YawDeg;
            public float WheelSpinDeg;
        }

        private readonly List<LogArchCartRuntime> _carts = new(4);
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(512);
        private uint _nextCartId = 1;
        private int _activePullingCartIndex = -1;

        public bool IsPullingCart => _activePullingCartIndex >= 0 && _activePullingCartIndex < _carts.Count;
        public int CartCount => _carts.Count;

        public bool TryPeekBoundLog(out float lengthM, out float diameterM, out byte woodMat)
        {
            lengthM = 0f;
            diameterM = 0.54f;
            woodMat = MaterialIdCatalog.OakWood;
            if (!IsPullingCart) return false;
            LogArchCartRuntime cart = _carts[_activePullingCartIndex];
            if (cart.State.HasBoundLog == 0) return false;
            lengthM = Mathf.Clamp(cart.State.BoundLogLengthM, 2.0f, 18.0f);
            diameterM = 0.54f;
            woodMat = cart.BoundWoodMat != 0 ? cart.BoundWoodMat : MaterialIdCatalog.OakWood;
            return true;
        }

        public bool TryConsumeBoundLogForPlacement(out float lengthM, out float diameterM, out byte woodMat)
        {
            if (!TryPeekBoundLog(out lengthM, out diameterM, out woodMat)) return false;
            LogArchCartRuntime cart = _carts[_activePullingCartIndex];
            cart.State = LogArchCartState.CreateEmpty(cart.State.CartId);
            cart.BoundSegments = 0;
            if (cart.BoundLogVisualGo != null)
            {
                UnityEngine.Object.Destroy(cart.BoundLogVisualGo);
                cart.BoundLogVisualGo = null;
            }
            return true;
        }

        public float ActiveSpeedMultiplier
        {
            get
            {
                if (!IsPullingCart) return 1.0f;
                return _carts[_activePullingCartIndex].State.PullSpeedMultiplier;
            }
        }

        public string ActiveHudStatus
        {
            get
            {
                if (!IsPullingCart) return string.Empty;
                ref readonly LogArchCartState st = ref _carts[_activePullingCartIndex].State;
                if (st.HasBoundLog != 0)
                {
                    return $"🪵 KÜTÜK ARABASI (ÇEKİLİYOR) | Yüklü: {st.BoundLogLengthM:0.0}m ({st.BoundLogMassKg:0} kg) | Dil: {st.TongueWeightKg:0.0} kg | [E] İndir | [Space] Fren | [Q] Arabayı Bırak";
                }
                return "🛞 KÜTÜK ARABASI (BOŞ) | Tam kütüğün yanına gelip [E] ile Bağla | [Q] Arabayı Bırak";
            }
        }

        public void DisposeAll()
        {
            for (int i = 0; i < _carts.Count; i++)
            {
                if (_carts[i].RootGo != null)
                {
                    UnityEngine.Object.Destroy(_carts[i].RootGo);
                }
            }
            _carts.Clear();
            _activePullingCartIndex = -1;
        }

        /// <summary>
        /// Yeni bir Kutuk El Arabasi (Log Arch) kurar (ilk surum: tekerlek + dingil + A-kemer sasi + cekme kolu).
        /// </summary>
        public LogArchCartRuntime SpawnLogArchCart(
            Vector3 spawnPos,
            float yawDeg,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            if (worldChunks != null)
            {
                spawnPos.y = SampleTerrainY(worldChunks, spawnPos.x, spawnPos.z, spawnPos.y);
            }
            uint id = _nextCartId++;
            var rt = new LogArchCartRuntime
            {
                State = LogArchCartState.CreateEmpty(id),
                Position = spawnPos,
                YawDeg = yawDeg
            };

            GameObject root = new GameObject($"LogArchCart_{id}");
            root.transform.position = spawnPos;
            root.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            GameObject chassis = new GameObject("ChassisArch");
            chassis.transform.SetParent(root.transform, false);
            BuildLogArchChassisVisual(chassis, sharedVoxelMat);

            GameObject leftWheel = CreateWheelVisual("Wheel_L", new Vector3(-0.78f, 0.48f, 0f), root.transform, sharedVoxelMat);
            GameObject rightWheel = CreateWheelVisual("Wheel_R", new Vector3(0.78f, 0.48f, 0f), root.transform, sharedVoxelMat);

            rt.RootGo = root;
            rt.ChassisTr = chassis.transform;
            rt.LeftWheelTr = leftWheel.transform;
            rt.RightWheelTr = rightWheel.transform;

            _carts.Add(rt);
            feedback = "🛞 Kütük El Arabası (Log Arch) kuruldu! Yaklaşıp [E] ile tutabilir, devrilmiş tam boy kütükleri kesmeden taşıyabilirsiniz.";
            return rt;
        }

        /// <summary>
        /// Oyuncu [E] bastiginda:
        /// - Araba cekiliyorsa: bos ise yakindaki tam boy kutugu baglar, dolu ise kutugu yere indirir.
        /// - Araba cekilmiyorsa: yakindaki kutuk arabasini tutar.
        /// </summary>
        public bool TryInteract(
            Vector3 playerPos,
            Vector3 playerForwardXZ,
            Stage1StructuralCollapseBridge collapseBridge,
            Material? sharedVoxelMat,
            out string feedback)
        {
            feedback = string.Empty;

            if (IsPullingCart)
            {
                LogArchCartRuntime cart = _carts[_activePullingCartIndex];
                if (cart.State.HasBoundLog != 0)
                {
                    Vector3 fwd = new Vector3(Mathf.Sin(cart.YawDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(cart.YawDeg * Mathf.Deg2Rad));
                    float logZOff = ComputeBoundLogLocalZOffset(cart.BoundSegments);
                    Vector3 dropCenter = cart.Position + fwd * logZOff;
                    dropCenter.y = cart.Position.y + 0.42f;
                    collapseBridge.SpawnFullLogFromCart(dropCenter, fwd, cart.BoundWoodMat, cart.BoundSegments);
                    if (cart.BoundLogVisualGo != null)
                    {
                        UnityEngine.Object.Destroy(cart.BoundLogVisualGo);
                        cart.BoundLogVisualGo = null;
                    }
                    LogArchCartPhysics.UnbindLog(ref cart.State);
                    feedback = $"🪵 Tam boy kütük ({cart.BoundSegments}m) indirildi.";
                    return true;
                }

                if (collapseBridge.TryDetachFallenTreeLogForCart(
                        cart.Position,
                        maxSearchRadius: 4.5f,
                        out byte woodMat,
                        out int segments,
                        out float diamM,
                        out uint pieceId))
                {
                    TimberWoodSpecies sp = woodMat == MaterialIdCatalog.Ironwood
                        ? TimberWoodSpecies.Ironwood
                        : (woodMat == MaterialIdCatalog.SpiritWood ? TimberWoodSpecies.Cedar : TimberWoodSpecies.Oak);
                    RigidStructurePiece piece = RigidStructurePiece.Create(
                        pieceId: pieceId,
                        kind: RigidTimberPieceKind.Log,
                        species: sp,
                        lengthM: segments * 1.0f,
                        diameterM: diamM,
                        position: System.Numerics.Vector3.Zero,
                        rotation: System.Numerics.Quaternion.Identity,
                        initialState: RigidTimberState.DynamicActive);

                    if (LogArchCartPhysics.TryBindFullLog(
                            ref cart.State,
                            in piece,
                            logCgOffsetFromAxleM: 0.14f,
                            currentTick: System.Diagnostics.Stopwatch.GetTimestamp(),
                            out feedback,
                            RigidTimberPhysicsTraceRecorder.Shared))
                    {
                        cart.BoundWoodMat = woodMat;
                        cart.BoundSegments = segments;
                        AttachBoundLogVisual(cart, woodMat, segments, sharedVoxelMat);
                        RigidTimberPhysicsTraceRecorder.Shared.FlushToJsonl(
                            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs"));
                        return true;
                    }
                }

                feedback = "Yakında bağlanacak devrilmiş tam boy kütük yok (4.5m içinde olmalı).";
                return true;
            }

            // Yakindaki kutuk arabasini tut (2B XZ mesafesi <= 3.8m, dikey <= 3.0m)
            int bestIdx = -1;
            float bestDistSq = 3.8f * 3.8f;
            for (int i = 0; i < _carts.Count; i++)
            {
                Vector3 diff = _carts[i].Position - playerPos;
                float dSqXZ = diff.x * diff.x + diff.z * diff.z;
                if (dSqXZ <= bestDistSq && Mathf.Abs(diff.y) <= 3.0f)
                {
                    bestDistSq = dSqXZ;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                _activePullingCartIndex = bestIdx;
                feedback = "🛞 Kütük El Arabasını tuttun! Tam kütüğün yanına sürüp [E] ile bağla, [Space] fren, [Q] bırak.";
                return true;
            }

            return false;
        }

        public bool TryReleaseCart(out string feedback)
        {
            feedback = string.Empty;
            if (!IsPullingCart) return false;
            _activePullingCartIndex = -1;
            feedback = "🛞 Kütük El Arabası bırakıldı (takozlandı).";
            return true;
        }

        public void TickIdleCartsGravity(float dt, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (_carts.Count == 0 || dt <= 1e-5f) return;
            for (int i = 0; i < _carts.Count; i++)
            {
                if (i == _activePullingCartIndex) continue;
                LogArchCartRuntime cart = _carts[i];
                float groundY = SampleTerrainY(worldChunks, cart.Position.x, cart.Position.z, cart.Position.y);
                if (cart.Position.y > groundY + 0.03f)
                {
                    cart.VerticalVelocity = Mathf.Max(-28f, cart.VerticalVelocity - 9.81f * dt);
                    cart.Position.y += cart.VerticalVelocity * dt;
                    if (cart.Position.y <= groundY)
                    {
                        cart.Position.y = groundY;
                        cart.VerticalVelocity = 0f;
                    }
                    if (cart.RootGo != null) cart.RootGo.transform.position = cart.Position;
                }
                else
                {
                    cart.Position.y = groundY;
                    cart.VerticalVelocity = 0f;
                    if (cart.RootGo != null) cart.RootGo.transform.position = cart.Position;
                }
            }
        }

        public void TickPulling(
            Vector3 playerPos,
            Vector3 playerForwardXZ,
            bool brakeHeld,
            float dt,
            Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            if (!IsPullingCart || dt <= 1e-5f) return;

            LogArchCartRuntime cart = _carts[_activePullingCartIndex];
            cart.State.BrakeEngaged = brakeHeld ? (byte)1 : (byte)0;

            Vector3 fwd = new Vector3(playerForwardXZ.x, 0f, playerForwardXZ.z);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            // Araba oyuncunun 2.45m arkasindan cekilir; oyuncu geri yururse araba icinden gecmez!
            Vector3 targetXZ = playerPos - fwd * 2.45f;
            Vector3 toTarget = new Vector3(targetXZ.x - cart.Position.x, 0f, targetXZ.z - cart.Position.z);
            float distToPlayerXZ = new Vector2(playerPos.x - cart.Position.x, playerPos.z - cart.Position.z).magnitude;
            float dist = toTarget.magnitude;

            float followRate = brakeHeld ? 1.5f : 7.0f;
            Vector3 nextPos = cart.Position;
            float nextYaw = cart.YawDeg;

            if (dist > 0.05f && distToPlayerXZ >= 2.25f)
            {
                Vector3 move = toTarget * Mathf.Clamp01(followRate * dt);
                Vector3 candPos = new Vector3(cart.Position.x + move.x, cart.Position.y, cart.Position.z + move.z);
                float desiredYaw = Mathf.Atan2(playerPos.x - candPos.x, playerPos.z - candPos.z) * Mathf.Rad2Deg;
                float candYaw = Mathf.LerpAngle(cart.YawDeg, desiredYaw, Mathf.Clamp01(6.0f * dt));

                // Agac govdesi / duvar / kaya engeli kontrolu: engel varsa tepesine cikmaz, carpip takilir!
                if (!IsCartBlockedByObstacle(worldChunks, candPos, candYaw, cart.State.HasBoundLog != 0 ? cart.BoundSegments : 0))
                {
                    nextPos = candPos;
                    nextYaw = candYaw;
                    float movedDist = move.magnitude;
                    cart.WheelSpinDeg = (cart.WheelSpinDeg + (movedDist / 0.50f) * Mathf.Rad2Deg) % 360f;
                }
                else
                {
                    // Sadece X veya sadece Z kayma denemesi (duvar/agac kenarindan takilarak kayma)
                    Vector3 slideX = new Vector3(candPos.x, cart.Position.y, cart.Position.z);
                    Vector3 slideZ = new Vector3(cart.Position.x, cart.Position.y, candPos.z);
                    if (!IsCartBlockedByObstacle(worldChunks, slideX, cart.YawDeg, cart.State.HasBoundLog != 0 ? cart.BoundSegments : 0))
                    {
                        nextPos = slideX;
                    }
                    else if (!IsCartBlockedByObstacle(worldChunks, slideZ, cart.YawDeg, cart.State.HasBoundLog != 0 ? cart.BoundSegments : 0))
                    {
                        nextPos = slideZ;
                    }
                }
            }

            // Yalnizca gercek zemin yuksekligini oku (agac govdesi / duvar ASLA zemin sayilmaz!)
            float prevY = cart.Position.y;
            float groundY = SampleTerrainY(worldChunks, nextPos.x, nextPos.z, prevY);
            // Tek karede dikey sicrama limiti (en fazla 1.1m egim -> asla agac tepesine cikmaz!)
            if (Mathf.Abs(groundY - prevY) <= 1.25f)
            {
                nextPos.y = Mathf.Lerp(prevY, groundY, Mathf.Clamp01(14f * dt));
            }

            float slopeSin = dist > 0.05f ? Mathf.Clamp((nextPos.y - prevY) / Mathf.Max(0.05f, dist), -0.6f, 0.6f) : 0f;
            LogArchCartPhysics.RecomputeCompoundMassAndBalance(ref cart.State, slopeSin);
            cart.Position = nextPos;
            cart.YawDeg = nextYaw;

            if (cart.RootGo != null)
            {
                cart.RootGo.transform.position = cart.Position;
                cart.RootGo.transform.rotation = Quaternion.Euler(0f, cart.YawDeg, 0f);
            }
            if (cart.ChassisTr != null)
            {
                cart.ChassisTr.localRotation = Quaternion.Euler(cart.State.PitchTiltDeg, 0f, 0f);
            }
            if (cart.LeftWheelTr != null)
            {
                cart.LeftWheelTr.localRotation = Quaternion.Euler(cart.WheelSpinDeg, 0f, 0f);
            }
            if (cart.RightWheelTr != null)
            {
                cart.RightWheelTr.localRotation = Quaternion.Euler(cart.WheelSpinDeg, 0f, 0f);
            }
        }

        private static bool IsTreeOrVerticalObstacleVoxel(uint raw)
        {
            if (!Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(raw)) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            byte shape = VoxelBitUtility.GetShapeId(raw);
            if (shape == ArchitecturalShapeCatalog.OctagonalColumn ||
                mat == MaterialIdCatalog.OakWood ||
                mat == MaterialIdCatalog.Ironwood ||
                mat == MaterialIdCatalog.SpiritWood)
            {
                return true;
            }
            return shape != 0;
        }

        private static bool IsCartBlockedByObstacle(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 cartPos,
            float yawDeg,
            int boundLogSegments)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            int checkY1 = Mathf.FloorToInt(cartPos.y + 0.65f);
            int checkY2 = Mathf.FloorToInt(cartPos.y + 1.35f);

            // Sasi sol/sag tekerlek ve merkez noktalarini kontrol et
            for (int side = -1; side <= 1; side++)
            {
                Vector3 p = cartPos + right * (side * 0.85f);
                if (IsSolidObstacleAt(worldChunks, p.x, checkY1, p.z) ||
                    IsSolidObstacleAt(worldChunks, p.x, checkY2, p.z))
                {
                    return true;
                }
            }

            // Bagli uzun kutugun on ve arka govdesini kontrol et (agaclara carpip takilsin!)
            if (boundLogSegments > 0)
            {
                float zOff = ComputeBoundLogLocalZOffset(boundLogSegments);
                float halfL = boundLogSegments * 0.5f;
                int samples = Mathf.Clamp(boundLogSegments, 2, 10);
                for (int i = 0; i <= samples; i++)
                {
                    float localZ = (zOff - halfL) + (boundLogSegments * (i / (float)samples));
                    Vector3 lp = cartPos + fwd * localZ;
                    if (IsSolidObstacleAt(worldChunks, lp.x, checkY1, lp.z) ||
                        IsSolidObstacleAt(worldChunks, lp.x, checkY2, lp.z))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSolidObstacleAt(Dictionary<(int cx, int cz), uint[]> worldChunks, float wx, int wy, float wz)
        {
            int ix = Mathf.FloorToInt(wx);
            int iz = Mathf.FloorToInt(wz);
            int cx = ix >= 0 ? (ix >> 4) : ((ix - 15) >> 4);
            int cz = iz >= 0 ? (iz >> 4) : ((iz - 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null) return false;
            int maxY = voxels.Length >> 8;
            if (wy < 1 || wy >= maxY) return false;
            int lx = ix - (cx << 4);
            int lz = iz - (cz << 4);
            uint raw = voxels[(wy << 8) | (lz << 4) | lx];
            return Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(raw);
        }

        private static float SampleTerrainY(Dictionary<(int cx, int cz), uint[]> worldChunks, float wx, float wz, float refY)
        {
            int ix = Mathf.FloorToInt(wx);
            int iz = Mathf.FloorToInt(wz);
            int cx = ix >= 0 ? (ix >> 4) : ((ix - 15) >> 4);
            int cz = iz >= 0 ? (iz >> 4) : ((iz - 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null) return refY;

            int lx = ix - (cx << 4);
            int lz = iz - (cz << 4);
            int col = (lz << 4) | lx;
            int maxY = voxels.Length >> 8;
            int startY = Mathf.Clamp(Mathf.RoundToInt(refY) + 2, 2, maxY - 2);
            for (int y = startY; y >= 1; y--)
            {
                uint raw = voxels[(y << 8) | col];
                if (!Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(raw)) continue;
                // Agac govdesi / dikey engel asla zemin sayilmaz!
                if (IsTreeOrVerticalObstacleVoxel(raw)) continue;

                uint above = (y + 1 < maxY) ? voxels[((y + 1) << 8) | col] : 0u;
                return Stage1PlayerMovementBridge.GetVoxelWalkableTopYPure(raw, above, y);
            }
            return refY;
        }

        private static float ComputeBoundLogLocalZOffset(int segments)
        {
            // Kutugun on ucu asla cekme kolu ucunun (+1.35m) onune gecmez -> oyuncunun icinden gecme %0!
            float halfL = segments * 0.5f;
            return Mathf.Min(0.0f, 1.30f - halfL);
        }

        private void BuildLogArchChassisVisual(GameObject chassisGo, Material? sharedMat)
        {
            _meshScratch.Clear();
            // Genis A-kemer dingil (1:1 kalin tam boy kutuk rahat sigsin: +/-0.95m, kemer yuksekligi 1.55m)
            AddColoredBox(_meshScratch, new Vector3(-0.96f, 0.48f, -0.07f), new Vector3(-0.62f, 0.58f, 0.07f), MaterialIdCatalog.IronTieBeamBlock);
            AddColoredBox(_meshScratch, new Vector3(0.62f, 0.48f, -0.07f), new Vector3(0.96f, 0.58f, 0.07f), MaterialIdCatalog.IronTieBeamBlock);
            AddColoredBox(_meshScratch, new Vector3(-0.72f, 0.52f, -0.08f), new Vector3(-0.56f, 1.54f, 0.08f), MaterialIdCatalog.TreatedTimberBeam);
            AddColoredBox(_meshScratch, new Vector3(0.56f, 0.52f, -0.08f), new Vector3(0.72f, 1.54f, 0.08f), MaterialIdCatalog.TreatedTimberBeam);
            AddColoredBox(_meshScratch, new Vector3(-0.74f, 1.40f, -0.09f), new Vector3(0.74f, 1.56f, 0.09f), MaterialIdCatalog.TreatedTimberBeam);
            // Cekme kolu (+Z oyuncuya dogru 2.15m)
            AddColoredBox(_meshScratch, new Vector3(-0.06f, 1.12f, 0.00f), new Vector3(0.06f, 1.24f, 2.10f), MaterialIdCatalog.WoodPlank);
            AddColoredBox(_meshScratch, new Vector3(-0.38f, 1.13f, 2.02f), new Vector3(0.38f, 1.23f, 2.14f), MaterialIdCatalog.TreatedTimberBeam);

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, "Mesh_LogArchChassis");
            chassisGo.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = chassisGo.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
        }

        private GameObject CreateWheelVisual(string name, Vector3 localPos, Transform parent, Material? sharedMat)
        {
            GameObject wheel = new GameObject(name);
            wheel.transform.SetParent(parent, false);
            float xSign = localPos.x < 0f ? -1f : 1f;
            wheel.transform.localPosition = new Vector3(xSign * 0.94f, 0.52f, 0f);

            _meshScratch.Clear();
            // 12 dilimli gercek dairesel ahsap tekerlek janti (r = 0.50m) + 6 parmaklik + demir gobek
            const int rimSegs = 12;
            const float rOuter = 0.50f;
            const float rInner = 0.40f;
            const float halfW = 0.065f;
            for (int i = 0; i < rimSegs; i++)
            {
                float a0 = (2f * Mathf.PI * i) / rimSegs;
                float a1 = (2f * Mathf.PI * (i + 1)) / rimSegs;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);

                Vector3 o0L = new Vector3(-halfW, s0 * rOuter, c0 * rOuter);
                Vector3 o1L = new Vector3(-halfW, s1 * rOuter, c1 * rOuter);
                Vector3 o0R = new Vector3(halfW, s0 * rOuter, c0 * rOuter);
                Vector3 o1R = new Vector3(halfW, s1 * rOuter, c1 * rOuter);
                Vector3 i0L = new Vector3(-halfW, s0 * rInner, c0 * rInner);
                Vector3 i1L = new Vector3(-halfW, s1 * rInner, c1 * rInner);
                Vector3 i0R = new Vector3(halfW, s0 * rInner, c0 * rInner);
                Vector3 i1R = new Vector3(halfW, s1 * rInner, c1 * rInner);

                Vector3 nOut = new Vector3(0f, (s0 + s1) * 0.5f, (c0 + c1) * 0.5f).normalized;
                EmitQuad(_meshScratch, o0L, o1L, o1R, o0R, nOut, MaterialIdCatalog.IronTieBeamBlock);
                EmitQuad(_meshScratch, i0R, i1R, i1L, i0L, -nOut, MaterialIdCatalog.WoodPlank);
                EmitQuad(_meshScratch, i0L, o0L, o1L, i1L, Vector3.left, MaterialIdCatalog.WoodPlank);
                EmitQuad(_meshScratch, o0R, i0R, i1R, o1R, Vector3.right, MaterialIdCatalog.WoodPlank);
            }
            // Parmakliklar + demir gobek
            AddColoredBox(_meshScratch, new Vector3(-0.04f, -0.40f, -0.045f), new Vector3(0.04f, 0.40f, 0.045f), MaterialIdCatalog.TreatedTimberBeam);
            AddColoredBox(_meshScratch, new Vector3(-0.04f, -0.045f, -0.40f), new Vector3(0.04f, 0.045f, 0.40f), MaterialIdCatalog.TreatedTimberBeam);
            AddColoredBox(_meshScratch, new Vector3(-0.09f, -0.11f, -0.11f), new Vector3(0.09f, 0.11f, 0.11f), MaterialIdCatalog.IronTieBeamBlock);

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_{name}");
            wheel.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = wheel.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
            return wheel;
        }

        private void AttachBoundLogVisual(LogArchCartRuntime cart, byte woodMat, int segments, Material? sharedMat)
        {
            if (cart.BoundLogVisualGo != null)
            {
                UnityEngine.Object.Destroy(cart.BoundLogVisualGo);
            }

            _meshScratch.Clear();
            for (int s = 0; s < segments; s++)
            {
                float offsetY = s - (segments - 1) * 0.5f;
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, offsetY - 0.5f, -0.5f),
                    woodMat,
                    ArchitecturalShapeCatalog.OctagonalColumn,
                    0,
                    1,
                    _meshScratch,
                    extendFoundationSkirt: false);
            }

            GameObject logVis = new GameObject($"BoundFullLog_{segments}m");
            if (cart.ChassisTr != null) logVis.transform.SetParent(cart.ChassisTr, false);
            // 1:1 GERCEK KALINLIK (asla kuculmez!) + on uc cekme kolunun gerisinde kalir (oyuncunun icinden gecmez!)
            float zOffset = ComputeBoundLogLocalZOffset(segments);
            logVis.transform.localPosition = new Vector3(0f, 0.72f, zOffset);
            logVis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            logVis.transform.localScale = Vector3.one;

            Mesh m = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"Mesh_BoundLog_{segments}m");
            logVis.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = logVis.AddComponent<MeshRenderer>();
            if (sharedMat != null) mr.sharedMaterial = sharedMat;
            cart.BoundLogVisualGo = logVis;
        }

        private static void AddColoredBox(
            List<FacetedVoxelTriangle> dst,
            Vector3 min,
            Vector3 max,
            byte matId)
        {
            EmitQuad(dst, new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, min.y, min.z), Vector3.back, matId);
            EmitQuad(dst, new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, min.y, max.z), Vector3.forward, matId);
            EmitQuad(dst, new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z), Vector3.left, matId);
            EmitQuad(dst, new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), Vector3.right, matId);
            EmitQuad(dst, new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), Vector3.up, matId);
            EmitQuad(dst, new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), Vector3.down, matId);
        }

        private static void EmitQuad(
            List<FacetedVoxelTriangle> dst,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3 n, byte matId)
        {
            var va = new System.Numerics.Vector3(a.x, a.y, a.z);
            var vb = new System.Numerics.Vector3(b.x, b.y, b.z);
            var vc = new System.Numerics.Vector3(c.x, c.y, c.z);
            var vd = new System.Numerics.Vector3(d.x, d.y, d.z);
            var vn = new System.Numerics.Vector3(n.x, n.y, n.z);
            dst.Add(new FacetedVoxelTriangle { V0 = va, V1 = vb, V2 = vc, Normal = vn, MaterialId = matId, Temperature = 0, FacetShade = 210, AmbientOcclusion = 255 });
            dst.Add(new FacetedVoxelTriangle { V0 = va, V1 = vc, V2 = vd, Normal = vn, MaterialId = matId, Temperature = 0, FacetShade = 210, AmbientOcclusion = 255 });
        }
    }
}
#endif
