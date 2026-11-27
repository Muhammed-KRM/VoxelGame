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
using SysVec3 = System.Numerics.Vector3;
using SysQuat = System.Numerics.Quaternion;

namespace Stratocraft.Character
{
    /// <summary>
    /// KM-6.2: _worldChunks voksel haritasindan sifir PhysX bagimliligi ile zemin malzemesi ve yuzey yuksekligi okuyan ornekleyici.
    /// </summary>
    public sealed class WorldChunkVoxelSoilSampler : IVoxelSoilSampler
    {
        public Dictionary<(int cx, int cz), uint[]>? WorldChunks;

        public bool TrySampleSolidMaterialAt(float worldX, float worldY, float worldZ, out byte materialId)
        {
            materialId = MaterialIdCatalog.Air;
            if (WorldChunks == null) return false;

            int wx = Mathf.FloorToInt(worldX);
            int wy = Mathf.FloorToInt(worldY);
            int wz = Mathf.FloorToInt(worldZ);
            if (wy < 1 || wy >= 256) return false;

            int cx = wx >> 4;
            int cz = wz >> 4;
            if (!WorldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null) return false;

            int lx = wx - (cx << 4);
            int lz = wz - (cz << 4);
            byte mat = VoxelBitUtility.GetMaterialId(voxels[(wy << 8) | (lz << 4) | lx]);
            if (mat == MaterialIdCatalog.Air || mat == MaterialIdCatalog.FiniteWaterSurface || mat == MaterialIdCatalog.FiniteLavaSurface)
            {
                return false;
            }

            materialId = mat;
            return true;
        }

        public bool TrySampleGroundSurfaceY(float worldX, float worldZ, float referenceY, out float groundSurfaceY, out byte topMaterialId)
        {
            groundSurfaceY = referenceY;
            topMaterialId = MaterialIdCatalog.Dirt;
            if (WorldChunks == null) return false;

            int wx = Mathf.FloorToInt(worldX);
            int wz = Mathf.FloorToInt(worldZ);
            int cx = wx >> 4;
            int cz = wz >> 4;
            if (!WorldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null) return false;

            int lx = wx - (cx << 4);
            int lz = wz - (cz << 4);
            int col = (lz << 4) | lx;
            int maxY = voxels.Length >> 8;
            int startY = Mathf.Clamp(Mathf.RoundToInt(referenceY) + 2, 2, maxY - 2);

            for (int y = startY; y >= 1; y--)
            {
                uint raw = voxels[(y << 8) | col];
                if (!Stage1PlayerMovementBridge.IsHardCollisionVoxelPure(raw)) continue;
                // Agac govdesi, dal veya cali ASLA zemin/cukur sayilmaz!
                if (Stage1LogArchCartController.IsTreeOrVerticalObstacleVoxel(raw)) continue;

                uint above = (y + 1 < maxY) ? voxels[((y + 1) << 8) | col] : 0u;
                groundSurfaceY = Stage1PlayerMovementBridge.GetVoxelWalkableTopYPure(raw, above, y);
                topMaterialId = VoxelBitUtility.GetMaterialId(raw);
                return true;
            }

            return false;
        }

        public float SampleSurfaceHeightY(float worldX, float worldZ, float fallbackY)
        {
            if (Stage1LogArchCartController.TryRaycastRenderedTerrainY(worldX, worldZ, fallbackY, out float meshY))
                return meshY;
            return TrySampleGroundSurfaceY(worldX, worldZ, fallbackY + 3.5f, out float gy, out _) ? gy : fallbackY;
        }
    }

    /// <summary>
    /// KM-6.2 / DOK4: Tek-Parca Rijit/Elastik Ahsap Yapi Koprusu (Gercek 3 Katli Ahsap Ev Pilotu).
    /// - Gercek agac govdesi kalinligi (0.50m-0.62m), dogru SharedVoxelMaterial LUT ahsap renkleri (0 yeni doku VRAM).
    /// - Katı oyuncu carpismasi + 1./2./3. kat yurunebilir ahsap dosemeler ve merdivenler.
    /// - Her sutun, duvar kutugu, kiris, doseme ve cati paneli bagimsiz tek-parca RigidStructurePiece olarak yasar.
    /// </summary>
    public sealed class Stage1RigidTimberBridge
    {
        private readonly WorldChunkVoxelSoilSampler _soilSampler = new();
        private readonly TimberStaticEquilibriumSolver _staticSolver = new();
        private readonly XpbdRigidTimberSolver _xpbdSolver = new();
        private readonly List<FacetedVoxelTriangle> _meshScratch = new(512);

        private struct SwingInVisualAnim
        {
            public bool Active;
            public int PieceIndex;
            public Vector3 StartPos;
            public Vector3 EndPos;
            public Quaternion StartRot;
            public Quaternion EndRot;
            public float Elapsed;
            public float Duration;
        }

        private RigidStructurePiece[] _pieces = new RigidStructurePiece[128];
        private SoilEmbedProfile[] _soilProfiles = new SoilEmbedProfile[128];
        private PieceCarpentryProfile[] _carpentryProfiles = new PieceCarpentryProfile[128];
        private CornerBond4Interface[] _bonds = new CornerBond4Interface[256];
        private GameObject?[] _pieceVisuals = new GameObject?[128];
        private Vector3[] _pieceHalfExtents = new Vector3[128];
        private byte[] _pieceMaterialIds = new byte[128];
        private float[] _prevVerticalVel = new float[128];
        private byte[] _pieceRemoved = new byte[128];
        private readonly SwingInVisualAnim[] _swingInAnims = new SwingInVisualAnim[8];
        private Material? _lastSharedVoxelMat;

        private int _pieceCount;
        private int _bondCount;
        private uint _nextPieceId = 10000;
        private uint _nextBondId = 1;
        private int _nextIslandId = 1;
        private long _simTick;
        private bool _hasDynamicPieces;
        private bool _needsStaticReeval;
        private float _revalCooldown;
        private float _groundWatchdogTimer;
        private const int MaxActivePiecesCap = 260;

        public int ActivePieceCount => _pieceCount;
        public bool HasDynamicPieces => _hasDynamicPieces;
        public float LastSpawnedCabinGroundY { get; private set; }

        public void DisposeAll()
        {
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceVisuals[i] != null)
                {
                    UnityEngine.Object.Destroy(_pieceVisuals[i]);
                    _pieceVisuals[i] = null;
                }
            }
            _pieceCount = 0;
            _bondCount = 0;
            _hasDynamicPieces = false;
            _needsStaticReeval = false;
        }

        private void EnsurePieceCapacity(int required)
        {
            if (required <= _pieces.Length) return;
            int newCap = Math.Max(_pieces.Length * 2, required);
            Array.Resize(ref _pieces, newCap);
            Array.Resize(ref _soilProfiles, newCap);
            Array.Resize(ref _carpentryProfiles, newCap);
            Array.Resize(ref _pieceVisuals, newCap);
            Array.Resize(ref _pieceHalfExtents, newCap);
            Array.Resize(ref _pieceMaterialIds, newCap);
            Array.Resize(ref _prevVerticalVel, newCap);
            Array.Resize(ref _pieceRemoved, newCap);
        }

        private void EnsureBondCapacity(int required)
        {
            if (required <= _bonds.Length) return;
            int newCap = Math.Max(_bonds.Length * 2, required);
            Array.Resize(ref _bonds, newCap);
        }

        /// <summary>
        /// Asama 1.1 (Survival Tip-In): Kutuk El Arabasindaki veya eldeki gercek boy kutugu kazilmis cukurun dibine diker.
        /// - Cukur kazilmissa (>= 0.75m, >= 3 kati duvar): cukur duvarlari kutugu gecici dik tutar, [V] toprak dolgusuyla kilitlenir!
        /// - Duz zeminse: kutuk dik duramaz, hemen DynamicActive uyanip devrilir!
        /// </summary>
        public bool TryTipInLogIntoHole(
            Vector3 targetPos,
            float lengthM,
            float diameterM,
            TimberWoodSpecies species,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback,
            bool lockExactXZ = false)
        {
            _soilSampler.WorldChunks = worldChunks;
            // 3x3 cevre taramasiyla kazilmis cukurun GERCEK tabanini bul; ANCAK altindaki magaraya acilan delik varsa
            // ASLA 20m yerin altina isinlama (targetPos.y - 2.8m alt sinir korumasi)!
            // ONEMLI: lockExactXZ=true (Plan diregine kilitliyken) X/Z koordinati ASLA yana kaydirilmaz — sadece taban derinligi (bestPitY) olculur!
            float minAllowedPitY = targetPos.y - 2.8f;
            float bestX = targetPos.x;
            float bestZ = targetPos.z;
            float centerSampleY = _soilSampler.SampleSurfaceHeightY(targetPos.x, targetPos.z, targetPos.y + 1.5f);
            float bestPitY = centerSampleY >= minAllowedPitY ? centerSampleY : targetPos.y;

            int baseIx = Mathf.FloorToInt(targetPos.x);
            int baseIz = Mathf.FloorToInt(targetPos.z);
            float scanMaxR = lockExactXZ ? 0.95f : 0.72f;
            for (int ox = -1; ox <= 1; ox++)
            for (int oz = -1; oz <= 1; oz++)
            {
                float sx = baseIx + ox + 0.5f;
                float sz = baseIz + oz + 0.5f;
                if (Vector2.Distance(new Vector2(sx, sz), new Vector2(targetPos.x, targetPos.z)) > scanMaxR) continue;
                float sy = _soilSampler.SampleSurfaceHeightY(sx, sz, targetPos.y + 1.5f);
                if (sy >= minAllowedPitY && sy < bestPitY - 0.08f)
                {
                    bestPitY = sy;
                    if (!lockExactXZ)
                    {
                        bestX = sx;
                        bestZ = sz;
                    }
                }
            }

            TipInEvaluation eval = TimberTipInAndJoineryLogic.EvaluateTipInHole(bestX, bestZ, bestPitY + 0.5f, _soilSampler);
            // Eger tam merkezde magara deligi yuzunden EvaluateTipInHole cok derini olctuyse targetPos cevresindeki kenar yuksekligiyle dogrula:
            if (eval.PitBottomY < minAllowedPitY)
            {
                eval.PitBottomY = bestPitY;
                float rimMax = bestPitY;
                for (int d = 0; d < 4; d++)
                {
                    float rx = bestX + (d == 0 ? 1.6f : (d == 1 ? -1.6f : 0f));
                    float rz = bestZ + (d == 2 ? 1.6f : (d == 3 ? -1.6f : 0f));
                    float ry = _soilSampler.SampleSurfaceHeightY(rx, rz, targetPos.y + 3.0f);
                    if (ry > rimMax) rimMax = ry;
                }
                eval.HoleDepthMeters = Mathf.Max(0f, rimMax - bestPitY);
                if (eval.HoleDepthMeters >= TimberTipInAndJoineryLogic.MinTipInHoleDepthM)
                {
                    eval.Result = TipInPlacementResult.TemporaryHoleHeldNeedsBackfill;
                }
            }

            float realDiam = Mathf.Clamp(diameterM, 0.38f, 0.62f);
            float realLen = Mathf.Clamp(lengthM, 2.5f, 18.0f);
            float pitBottomY = Mathf.Clamp(eval.PitBottomY > 0.5f ? eval.PitBottomY : bestPitY, minAllowedPitY, targetPos.y + 1.0f);
            float centerY = pitBottomY + realLen * 0.5f;

            int islandId = _nextIslandId++;
            int idx = AddCylinderPieceInternal(
                RigidTimberPieceKind.Post,
                species,
                realLen,
                realDiam,
                new SysVec3(bestX, centerY, bestZ),
                SysQuat.Identity,
                islandId,
                MaterialIdCatalog.OakWood,
                sharedVoxelMat);

            _soilProfiles[idx] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[idx], _soilSampler);

            if (eval.Result == TipInPlacementResult.NoHoleFlatGroundTipsOver)
            {
                // Duz zeminde koca kutuk dik duramaz -> hemen devrilir!
                _pieces[idx].State = RigidTimberState.DynamicActive;
                _pieces[idx].AngularVelocity = new SysVec3(0.55f, 0f, 0.30f);
                _hasDynamicPieces = true;
                feedback = $"⚠️ Çukur kazmadan {realLen:F1}m kütük dik duramaz (çukur derinliği: {eval.HoleDepthMeters:F2}m)! Önce kazmayla çukur kazın — düşen kütüğü Arabayla [E] geri yükleyebilirsiniz!";
                return true;
            }

            // Cukur duvarlari (HoleDepth >= 0.55m) kutugu dik tutar! SoilEmbedmentEvaluator'in okudugu 4 yonlu ResistMoment alanlarini da doldur!
            _pieces[idx].Flags |= 2;
            SoilEmbedProfile holeSupport = _soilProfiles[idx];
            holeSupport.EmbedDepthM = Mathf.Max(holeSupport.EmbedDepthM, Mathf.Max(1.10f, eval.HoleDepthMeters));
            holeSupport.ResistMomentPosX = Mathf.Max(holeSupport.ResistMomentPosX, 85000f);
            holeSupport.ResistMomentNegX = Mathf.Max(holeSupport.ResistMomentNegX, 85000f);
            holeSupport.ResistMomentPosZ = Mathf.Max(holeSupport.ResistMomentPosZ, 85000f);
            holeSupport.ResistMomentNegZ = Mathf.Max(holeSupport.ResistMomentNegZ, 85000f);
            holeSupport.RotStiffnessPosX = Mathf.Max(holeSupport.RotStiffnessPosX, 250000f);
            holeSupport.RotStiffnessNegX = Mathf.Max(holeSupport.RotStiffnessNegX, 250000f);
            holeSupport.RotStiffnessPosZ = Mathf.Max(holeSupport.RotStiffnessPosZ, 250000f);
            holeSupport.RotStiffnessNegZ = Mathf.Max(holeSupport.RotStiffnessNegZ, 250000f);
            holeSupport.LastEffectiveResistMomentNm = 85000f;
            holeSupport.LastDecision = SoilEmbedDecision.ElasticBalanced;
            _soilProfiles[idx] = holeSupport;

            if (eval.Result == TipInPlacementResult.TemporaryHoleHeldNeedsBackfill)
            {
                feedback = $"🪵 {realLen:F1}m kütük çukurun dibine DİK OLARAK oturtuldu (derinlik {eval.HoleDepthMeters:F2}m)! Elinde Toprak veya Ham Taş varken [E], [Sol Tık] veya [Sağ Tık] ile çukuru doldurup kilitleyin!";
                return true;
            }

            feedback = $"🪵 {realLen:F1}m kütük derin çukura ({eval.HoleDepthMeters:F2}m) dikildi! Elinde Toprak/Taş ile [E] veya [Tık] basıp sıkıştırın.";
            return true;
        }

        public bool HasUnwedgedHolePostNear(Vector3 playerPos, float maxRadius = 3.5f)
        {
            float maxSq = maxRadius * maxRadius;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].Kind != RigidTimberPieceKind.Post || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                if ((_pieces[i].Flags & 2) == 0 && _soilProfiles[i].EmbedDepthM >= 1.35f) continue;
                float dx = _pieces[i].Position.X - playerPos.x;
                float dz = _pieces[i].Position.Z - playerPos.z;
                if (dx * dx + dz * dz <= maxSq) return true;
            }
            return false;
        }

        /// <summary>
        /// Kutuk El Arabasi ([E]) ile arabadan dusmus/devrilmis (veya henuz cukurda gecici duran) kutugu tekrar arabaya yukler!
        /// </summary>
        /// <summary>
        /// Cukurdaki gecici sutunun (Flags &amp; 2) dibine Tas Kama (RoughStoneChunk) veya Toprak Topagi (LooseDirtClod) ile
        /// vuruldugunda/sikistirildiginda sutunu %100 kalici tasiyici temel sutununa donusturur!
        /// </summary>
        public bool TryWedgeOrTampNearestHolePost(
            Vector3 playerPos,
            bool isStoneWedge,
            out string feedback,
            Dictionary<(int cx, int cz), uint[]>? worldChunks = null,
            HashSet<(int cx, int cz)>? dirtyChunks = null)
        {
            feedback = string.Empty;
            int bestIdx = -1;
            float bestDistSq = 3.5f * 3.5f;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].Kind != RigidTimberPieceKind.Post || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                if ((_pieces[i].Flags & 2) == 0 && _soilProfiles[i].EmbedDepthM >= 1.35f) continue;

                float dx = _pieces[i].Position.X - playerPos.x;
                float dz = _pieces[i].Position.Z - playerPos.z;
                float dSq = dx * dx + dz * dz;
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) return false;

            // Gercek fiziksel voksel dolgusu: Cukurun icindeki bos vokselleri direk tabanindan zemin dudagina (maks 1.65m) kadar
            // gercek sikistirilmis cakil/toprak vokseliyle doldur ki daha sonra yakinda yuzey kazildiginda direk boslukta sanilip yikilmasin!
            if (worldChunks != null)
            {
                _soilSampler.WorldChunks = worldChunks;
                float px = _pieces[bestIdx].Position.X;
                float pz = _pieces[bestIdx].Position.Z;
                float footY = _pieces[bestIdx].Position.Y - _pieces[bestIdx].LengthM * 0.5f;
                float rimY = SampleSurroundingRimSurfaceY(px, pz, footY + 3.0f);
                int minWy = Mathf.Max(2, Mathf.FloorToInt(footY));
                int maxWy = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(rimY - 0.01f, footY + 2.25f)), minWy, 250);
                byte fillMat = isStoneWedge ? MaterialIdCatalog.Gravel : MaterialIdCatalog.Dirt;
                uint packedFill = CrystalTerrainFlattenUtility.PackCrystalImmuneBackfillSoilVoxel(fillMat);

                int centerWx = Mathf.FloorToInt(px);
                int centerWz = Mathf.FloorToInt(pz);
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        int wx = centerWx + ox;
                        int wz = centerWz + oz;
                        int cx = wx >> 4;
                        int cz = wz >> 4;
                        if (!worldChunks.TryGetValue((cx, cz), out uint[]? vox) || vox == null) continue;
                        int lx = wx - (cx << 4);
                        int lz = wz - (cz << 4);
                        bool touched = false;
                        for (int wy = minWy; wy <= maxWy; wy++)
                        {
                            int vIdx = (wy << 8) | (lz << 4) | lx;
                            byte ex = VoxelBitUtility.GetMaterialId(vox[vIdx]);
                            if (ex == MaterialIdCatalog.Air)
                            {
                                vox[vIdx] = packedFill;
                                touched = true;
                            }
                        }
                        if (touched && dirtyChunks != null)
                        {
                            dirtyChunks.Add((cx, cz));
                            if (lx == 0) dirtyChunks.Add((cx - 1, cz));
                            if (lx == 15) dirtyChunks.Add((cx + 1, cz));
                            if (lz == 0) dirtyChunks.Add((cx, cz - 1));
                            if (lz == 15) dirtyChunks.Add((cx, cz + 1));
                        }
                    }
                }
            }

            _pieces[bestIdx].Flags &= unchecked((byte)~2);
            SoilEmbedProfile locked = worldChunks != null
                ? SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[bestIdx], _soilSampler, compactionFactor: 0.96f, hasStoneCollar: isStoneWedge)
                : _soilProfiles[bestIdx];
            float lockM = isStoneWedge ? 165000f : 125000f;
            locked.EmbedDepthM = Mathf.Max(locked.EmbedDepthM, isStoneWedge ? 1.55f : 1.40f);
            locked.HasStoneCollar = isStoneWedge ? (byte)1 : locked.HasStoneCollar;
            locked.CompactionFactor = 0.98f;
            locked.ResistMomentPosX = Mathf.Max(locked.ResistMomentPosX, lockM);
            locked.ResistMomentNegX = Mathf.Max(locked.ResistMomentNegX, lockM);
            locked.ResistMomentPosZ = Mathf.Max(locked.ResistMomentPosZ, lockM);
            locked.ResistMomentNegZ = Mathf.Max(locked.ResistMomentNegZ, lockM);
            locked.RotStiffnessPosX = Mathf.Max(locked.RotStiffnessPosX, 380000f);
            locked.RotStiffnessNegX = Mathf.Max(locked.RotStiffnessNegX, 380000f);
            locked.RotStiffnessPosZ = Mathf.Max(locked.RotStiffnessPosZ, 380000f);
            locked.RotStiffnessNegZ = Mathf.Max(locked.RotStiffnessNegZ, 380000f);
            locked.LastEffectiveResistMomentNm = lockM;
            locked.LastDecision = SoilEmbedDecision.ElasticBalanced;
            _soilProfiles[bestIdx] = locked;

            feedback = isStoneWedge
                ? "🪨 Çukur taş kamalar ve sıkıştırılmış çakılla doldurulup %100 kalıcı temel sütunu olarak kilitlendi!"
                : "🧱 Çukur toprakla doldurulup sıkıştırıldı — sütun %100 kalıcı olarak kilitlendi!";
            return true;
        }

        public bool TryPeekFallenPieceForCart(
            Vector3 cartPos,
            float maxSearchRadius,
            out int foundIndex,
            out byte woodMat,
            out int segments,
            out float diameterM,
            out uint pieceId)
            => TryPeekFallenPieceForCartExact(cartPos, maxSearchRadius, out foundIndex, out woodMat, out segments, out _, out diameterM, out pieceId);

        public bool TryPeekFallenPieceForCartExact(
            Vector3 cartPos,
            float maxSearchRadius,
            out int foundIndex,
            out byte woodMat,
            out int segments,
            out float exactLengthM,
            out float diameterM,
            out uint pieceId)
        {
            foundIndex = -1;
            woodMat = MaterialIdCatalog.OakWood;
            segments = 0;
            exactLengthM = 0f;
            diameterM = 0.42f;
            pieceId = 0;

            int bestIdx = -1;
            float bestDistSq = maxSearchRadius * maxSearchRadius;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0) continue;
                bool canReload = _pieces[i].State != RigidTimberState.StaticAnchored || (_pieces[i].Flags & 2) != 0;
                if (!canReload) continue;

                Vector3 p = ToUnityVec3(_pieces[i].Position);
                float dSqXZ = (p.x - cartPos.x) * (p.x - cartPos.x) + (p.z - cartPos.z) * (p.z - cartPos.z);
                if (dSqXZ <= bestDistSq && Mathf.Abs(p.y - cartPos.y) <= 6.5f)
                {
                    bestDistSq = dSqXZ;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            RigidStructurePiece chosen = _pieces[bestIdx];
            foundIndex = bestIdx;
            woodMat = _pieceMaterialIds[bestIdx] != 0 ? _pieceMaterialIds[bestIdx] : MaterialIdCatalog.OakWood;
            exactLengthM = Mathf.Clamp(chosen.LengthM, 1.0f, 18.0f);
            segments = Mathf.Clamp(Mathf.RoundToInt(exactLengthM), 1, 18);
            diameterM = Mathf.Clamp(chosen.DiameterM, 0.36f, 0.52f);
            pieceId = chosen.PieceId;
            return true;
        }

        public void CommitDetachFallenPieceForCart(int index)
        {
            if ((uint)index >= (uint)_pieceCount) return;
            _pieceRemoved[index] = 1;
            if (_pieceVisuals[index] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[index]);
                _pieceVisuals[index] = null;
            }
        }

        public bool TryDetachFallenPieceForCart(
            Vector3 cartPos,
            float maxSearchRadius,
            out byte woodMat,
            out int segments,
            out float diameterM,
            out uint pieceId)
        {
            if (!TryPeekFallenPieceForCart(cartPos, maxSearchRadius, out int idx, out woodMat, out segments, out diameterM, out pieceId))
                return false;
            CommitDetachFallenPieceForCart(idx);
            return true;
        }

        /// <summary>
        /// [K] ile oyuncunun yanlis diktigi veya yerdeki herhangi bir kutuk/diregi GERCEK KUTUK olarak geri almasini saglar!
        /// </summary>
        public bool TryRetrieveAimedTimberPiece(
            Vector3 rayOrigin, Vector3 rayDir, float maxRange,
            out float retrievedLengthM, out float retrievedDiamM, out byte retrievedWoodMat, out string feedback)
        {
            retrievedLengthM = 0f;
            retrievedDiamM = 0.36f;
            retrievedWoodMat = MaterialIdCatalog.OakWood;
            feedback = string.Empty;

            if (!TryRaycastAnyPiece(rayOrigin, rayDir, maxRange, out int bestIdx, out _, out _, out _))
                return false;

            ref RigidStructurePiece p = ref _pieces[bestIdx];
            retrievedLengthM = p.LengthM;
            retrievedDiamM = p.DiameterM;
            retrievedWoodMat = p.Species == TimberWoodSpecies.Ironwood ? MaterialIdCatalog.Ironwood : MaterialIdCatalog.OakWood;

            uint pid = p.PieceId;
            for (int b = 0; b < _bondCount; b++)
            {
                if (_bonds[b].PieceA == pid || _bonds[b].PieceB == pid)
                    _bonds[b].IsActive = 0;
            }

            _pieceRemoved[bestIdx] = 1;
            if (_pieceVisuals[bestIdx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[bestIdx]);
                _pieceVisuals[bestIdx] = null;
            }
            _needsStaticReeval = true;
            feedback = $"♻️ {p.Kind} ({retrievedLengthM:F2}m) sökülüp bütün kütük olarak geri alındı!";
            return true;
        }

        public enum HorizontalSnapMode : byte
        {
            FreeGroundSill = 0,          // Mod A: Duz toprak veya [C] tas zemin ustune taban kutugu
            SaddleNotchCrossStack = 1,   // Mod A: Baltayla 3B Eyer Kertigi acilmis alt kutuk uclarina 90° capraz bindirme
            GroovedPostInfill = 2,       // Mod B: Oluklu iki dikey direk arasina Swing-In duvar dolgusu (0 clipping!)
            SideMortiseMainGirder = 3,   // Mod B: Iki dikey diregin yan zivana yuvalarina oturan Ana Kat Kirisi (0 clipping!)
            BlockedMissingGroove = 4,    // Iki direk arasinda oluk henuz baltayla acilmamis -> Kirmizi Engelli!
            BlockedTooShort = 5          // Eldeki kutuk iki direk net acikligina kisa geliyor -> Kirmizi Engelli!
        }

        public struct HorizontalPlacementEval
        {
            public HorizontalSnapMode Mode;
            public Vector3 CenterPos;
            public float YawDeg;
            public float ClearBodyLengthM;
            public float RequiredTotalCutM;
            public float LeftoverLengthM;
            public int PostA;
            public int PostB;
            public int UnderCourseIdx;
            public int SaddleSupportA;
            public int SaddleSupportB;
            public int CourseNumber;
            public bool CanPlace => Mode != HorizontalSnapMode.BlockedMissingGroove && Mode != HorizontalSnapMode.BlockedTooShort;
        }

        public static int WorldDirToLocalOctantSector(Quaternion pieceRot, Vector3 worldDirXZ)
        {
            Vector3 localDir = Quaternion.Inverse(pieceRot) * new Vector3(worldDirXZ.x, 0f, worldDirXZ.z);
            float ang = Mathf.Atan2(localDir.z, localDir.x);
            if (ang < 0f) ang += Mathf.PI * 2f;
            // Duz sekizgen yuzey merkezleri tam 0° (+X Dogu), 45° (KD), 90° (+Z Kuzey), 135° (KB), 180° (-X Bati), 225° (GB), 270° (-Z Guney), 315° (GD) hizasindadir!
            int sec = Mathf.RoundToInt((ang / (Mathf.PI * 2f)) * 8f) & 7;
            return sec;
        }

        public static Vector3 LocalOctantSectorToWorldDir(Quaternion pieceRot, int sector)
        {
            float aMid = ((sector & 7) / 8.0f) * Mathf.PI * 2f;
            Vector3 localOut = new Vector3(Mathf.Cos(aMid), 0f, Mathf.Sin(aMid));
            Vector3 w = pieceRot * localOut;
            w.y = 0f;
            return w.sqrMagnitude > 1e-6f ? w.normalized : Vector3.right;
        }

        public static bool HasGrooveFacingWorldDir(in PieceCarpentryProfile carp, Quaternion pieceRot, Vector2 targetDirXZ, float minDot = 0.50f)
        {
            if (carp.GrooveSectorMask == 0 || targetDirXZ.sqrMagnitude < 1e-6f) return false;
            Vector2 dirN = targetDirXZ.normalized;
            for (int s = 0; s < 8; s++)
            {
                if ((carp.GrooveSectorMask & (1 << s)) == 0) continue;
                Vector3 w = LocalOctantSectorToWorldDir(pieceRot, s);
                if (Vector2.Dot(new Vector2(w.x, w.z), dirN) >= minDot) return true;
            }
            return false;
        }

        public static bool TryFindMortiseFacingWorldDir(in PieceCarpentryProfile carp, Quaternion pieceRot, Vector2 targetDirXZ, float localY, float heightTol, out float snappedLocalY, float minDot = 0.50f)
        {
            snappedLocalY = localY;
            if (carp.SideMortiseCount == 0 || targetDirXZ.sqrMagnitude < 1e-6f) return false;
            Vector2 dirN = targetDirXZ.normalized;
            float bestDot = minDot;
            bool found = false;
            for (int i = 0; i < carp.SideMortiseCount; i++)
            {
                byte ms = carp.GetMortiseSector(i);
                float my = carp.GetMortiseLocalY(i);
                if (Mathf.Abs(my - localY) > heightTol) continue;
                Vector3 w = LocalOctantSectorToWorldDir(pieceRot, ms);
                float dot = Vector2.Dot(new Vector2(w.x, w.z), dirN);
                if (dot >= bestDot)
                {
                    bestDot = dot;
                    snappedLocalY = my;
                    found = true;
                }
            }
            return found;
        }

        public static string GetSectorCompassLabel(Vector3 worldDirXZ)
        {
            float a = Mathf.Atan2(worldDirXZ.x, worldDirXZ.z) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            int idx = Mathf.RoundToInt(a / 45f) & 7;
            return idx switch
            {
                0 => "Kuzey",
                1 => "Kuzeydoğu",
                2 => "Doğu",
                3 => "Güneydoğu",
                4 => "Güney",
                5 => "Güneybatı",
                6 => "Batı",
                _ => "Kuzeybatı"
            };
        }

        /// <summary>
        /// Faz M3 &amp; M4: Hem 1:1 onizleme hem de [Sol Tik] icin SIFIR IC ICE GECMELI yatay yerlestirme degerlendiricisi!
        /// </summary>
        public HorizontalPlacementEval EvaluateSmartHorizontalPlacement(
            Vector3 targetPos,
            float manualYawDeg,
            float heldLengthM,
            float diameterM,
            Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            _soilSampler.WorldChunks = worldChunks;
            float realHeldLen = Mathf.Clamp(heldLengthM, 0.85f, 18.0f);
            float realDiam = Mathf.Clamp(diameterM, 0.36f, 0.62f);

            var eval = new HorizontalPlacementEval
            {
                Mode = HorizontalSnapMode.FreeGroundSill,
                PostA = -1,
                PostB = -1,
                UnderCourseIdx = -1,
                SaddleSupportA = -1,
                SaddleSupportB = -1,
                CourseNumber = 1
            };

            // 1) Yakindaki iki dikey ana direk (Post) cifitini bul (nisangah dogru parcasina <= 1.95m ise yakala!)
            int bestA = -1, bestB = -1;
            float bestPairScore = float.MaxValue;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored || _pieces[i].Kind != RigidTimberPieceKind.Post) continue;
                Vector3 pA = ToUnityVec3(_pieces[i].Position);
                Vector2 a2 = new Vector2(pA.x, pA.z);
                if (Vector2.Distance(a2, new Vector2(targetPos.x, targetPos.z)) > 9.5f) continue;

                for (int j = i + 1; j < _pieceCount; j++)
                {
                    if (_pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post) continue;
                    Vector3 pB = ToUnityVec3(_pieces[j].Position);
                    Vector2 b2 = new Vector2(pB.x, pB.z);
                    float span = Vector2.Distance(a2, b2);
                    if (span < 1.15f || span > 14.5f) continue;

                    Vector2 ab = (b2 - a2) / span;
                    float proj = Vector2.Dot(new Vector2(targetPos.x, targetPos.z) - a2, ab);
                    if (proj < -0.45f || proj > span + 0.45f) continue;

                    // KRITIK KURAL: Iki direk (i, j) arasinda ayni dogru uzerinde baska bir dikili ara direk (k) varsa
                    // kutuk ASLA o ara diregin icinden gecip kapi/pencere boslugunu kapatamaz!
                    bool blockedByIntermediatePost = false;
                    for (int k = 0; k < _pieceCount; k++)
                    {
                        if (k == i || k == j || _pieceRemoved[k] != 0 || _pieces[k].State != RigidTimberState.StaticAnchored || _pieces[k].Kind != RigidTimberPieceKind.Post)
                            continue;
                        Vector3 pK = ToUnityVec3(_pieces[k].Position);
                        if (Mathf.Abs(targetPos.y - pK.y) > _pieces[k].LengthM * 0.5f - 0.05f) continue;
                        Vector2 k2 = new Vector2(pK.x, pK.z);
                        float projK = Vector2.Dot(k2 - a2, ab);
                        if (projK > 0.35f && projK < span - 0.35f)
                        {
                            float perpK = Vector2.Distance(a2 + ab * projK, k2);
                            if (perpK <= (_pieces[k].DiameterM * 0.5f + realDiam * 0.5f + 0.20f))
                            {
                                blockedByIntermediatePost = true;
                                break;
                            }
                        }
                    }
                    if (blockedByIntermediatePost) continue;

                    float clProj = Mathf.Clamp(proj, 0f, span);
                    float perp = Vector2.Distance(a2 + ab * clProj, new Vector2(targetPos.x, targetPos.z));
                    float pairScore = perp + span * 0.03f;
                    if (perp <= 1.95f && pairScore < bestPairScore)
                    {
                        bestPairScore = pairScore;
                        bestA = i;
                        bestB = j;
                    }
                }
            }

            if (bestA >= 0 && bestB >= 0)
            {
                Vector3 pA = ToUnityVec3(_pieces[bestA].Position);
                Vector3 pB = ToUnityVec3(_pieces[bestB].Position);
                Vector2 a2 = new Vector2(pA.x, pA.z);
                Vector2 b2 = new Vector2(pB.x, pB.z);
                float span = Vector2.Distance(a2, b2);
                Vector2 ab = (b2 - a2) / span;

                float rA = _pieces[bestA].DiameterM * 0.5f;
                float rB = _pieces[bestB].DiameterM * 0.5f;
                // SIFIR IC ICE GECME: Dolu govde yalnizca kabuktan kabuga (L_clear) uzanir; yalnizca uctaki zivana dili oluk icine girer!
                float clearLen = Mathf.Max(0.55f, span - (rA + rB));
                float requiredCut = clearLen + 0.14f;

                Vector2 startSurface = a2 + ab * rA;
                Vector2 endSurface = b2 - ab * rB;
                Vector2 midXZ = (startSurface + endSurface) * 0.5f;
                float alignYaw = Mathf.Atan2(ab.y, -ab.x) * Mathf.Rad2Deg;

                Quaternion rotA = ToUnityQuat(_pieces[bestA].Rotation);
                Quaternion rotB = ToUnityQuat(_pieces[bestB].Rotation);
                int secA = WorldDirToLocalOctantSector(rotA, new Vector3(ab.x, 0f, ab.y));
                int secB = WorldDirToLocalOctantSector(rotB, new Vector3(-ab.x, 0f, -ab.y));

                float groundY = SampleMaxSurfaceAlongSpan(startSurface, endSurface, targetPos.y + 0.75f);

                // A) Once bu yukseklikte iki direkte de YAN KIRIS ZIVANASI (SideMortise) acilmis ve oyuncu dogrudan o yuvaya (+-0.45m) nisan almis mi kontrol et!
                // DIKKAT: Eger bu zivana yuvasina daha once bir Ana Kat Kirisi (Beam) oturtulduysa ayni yuvaya ikinci kez ust uste kiris koyma!
                float localYA = targetPos.y - pA.y;
                float localYB = targetPos.y - pB.y;
                bool hasMortiseA = TryFindMortiseFacingWorldDir(in _carpentryProfiles[bestA], rotA, ab, localYA, 0.45f, out float snapLocYA) ||
                                   _carpentryProfiles[bestA].TryFindMortiseNear(secA, localYA, 0.45f, out snapLocYA);
                bool hasMortiseB = TryFindMortiseFacingWorldDir(in _carpentryProfiles[bestB], rotB, -ab, localYB, 0.45f, out float snapLocYB) ||
                                   _carpentryProfiles[bestB].TryFindMortiseNear(secB, localYB, 0.45f, out snapLocYB);
                if (hasMortiseA && hasMortiseB && Mathf.Abs((pA.y + snapLocYA) - (pB.y + snapLocYB)) <= 0.55f)
                {
                    float beamY = ((pA.y + snapLocYA) + (pB.y + snapLocYB)) * 0.5f;
                    bool mortiseAlreadyOccupied = false;
                    for (int m = 0; m < _pieceCount; m++)
                    {
                        if (_pieceRemoved[m] != 0 || _pieces[m].State != RigidTimberState.StaticAnchored || _pieces[m].Kind != RigidTimberPieceKind.Beam) continue;
                        Vector3 pm = ToUnityVec3(_pieces[m].Position);
                        if (Vector2.Distance(new Vector2(pm.x, pm.z), midXZ) <= 0.45f && Mathf.Abs(pm.y - beamY) <= 0.28f)
                        {
                            mortiseAlreadyOccupied = true;
                            break;
                        }
                    }

                    if (!mortiseAlreadyOccupied)
                    {
                        eval.PostA = bestA;
                        eval.PostB = bestB;
                        eval.CenterPos = new Vector3(midXZ.x, beamY, midXZ.y);
                        eval.YawDeg = alignYaw;
                        eval.ClearBodyLengthM = clearLen;
                        eval.RequiredTotalCutM = requiredCut;
                        eval.LeftoverLengthM = Mathf.Max(0f, realHeldLen - requiredCut);
                        eval.Mode = realHeldLen + 0.05f < requiredCut
                            ? HorizontalSnapMode.BlockedTooShort
                            : HorizontalSnapMode.SideMortiseMainGirder;
                        return eval;
                    }
                }

                // B) Ayni duvar acikliginda ve hedef kat kotunun altinda (pk.y <= targetPos.y + 0.85f) mevcut yatay sira var mi?
                float stackedY = groundY + realDiam * 0.48f;
                int underIdx = -1;
                int courseCount = 1;
                float highestY = float.MinValue;
                for (int k = 0; k < _pieceCount; k++)
                {
                    if (_pieceRemoved[k] != 0 || _pieces[k].State != RigidTimberState.StaticAnchored) continue;
                    if (_pieces[k].Kind == RigidTimberPieceKind.Post || _pieces[k].Kind == RigidTimberPieceKind.Plank || _pieces[k].Kind == RigidTimberPieceKind.Rafter) continue;
                    Vector3 pk = ToUnityVec3(_pieces[k].Position);
                    if (pk.y > targetPos.y + 0.85f) continue;
                    if (Vector2.Distance(new Vector2(pk.x, pk.z), midXZ) <= 0.75f && Mathf.Abs(_pieces[k].LengthM - clearLen) <= 0.85f)
                    {
                        courseCount++;
                        if (pk.y > highestY)
                        {
                            highestY = pk.y;
                            underIdx = k;
                        }
                    }
                }
                if (underIdx >= 0)
                {
                    float underDiam = Mathf.Clamp(_pieces[underIdx].DiameterM, 0.36f, 0.62f);
                    // Scribed duzlestirilmis yuzey -> tam teget oturur, asla ic ice gomulmez!
                    stackedY = Mathf.Max(stackedY, highestY + (underDiam + realDiam) * 0.48f);
                }

                float finalY = targetPos.y > stackedY + 0.55f ? targetPos.y : stackedY;
                bool hasGrooveA = HasGrooveFacingWorldDir(in _carpentryProfiles[bestA], rotA, ab, 0.50f) ||
                                  _carpentryProfiles[bestA].HasGrooveNearSector(secA);
                bool hasGrooveB = HasGrooveFacingWorldDir(in _carpentryProfiles[bestB], rotB, -ab, 0.50f) ||
                                  _carpentryProfiles[bestB].HasGrooveNearSector(secB);

                eval.PostA = bestA;
                eval.PostB = bestB;
                eval.UnderCourseIdx = underIdx;
                eval.CourseNumber = courseCount;
                eval.CenterPos = new Vector3(midXZ.x, finalY, midXZ.y);
                eval.YawDeg = alignYaw;
                eval.ClearBodyLengthM = clearLen;
                eval.RequiredTotalCutM = requiredCut;
                eval.LeftoverLengthM = Mathf.Max(0f, realHeldLen - requiredCut);

                if (!hasGrooveA || !hasGrooveB)
                {
                    eval.Mode = HorizontalSnapMode.BlockedMissingGroove;
                }
                else if (realHeldLen + 0.05f < requiredCut)
                {
                    eval.Mode = HorizontalSnapMode.BlockedTooShort;
                }
                else
                {
                    eval.Mode = HorizontalSnapMode.GroovedPostInfill;
                }
                return eval;
            }

            // 2) Mod A (Direksiz Yigma / Canti): Uclarinda 3B Eyer Kertigi (EndSaddleNotchMask) acilmis alt kutuk var mi?
            int bestNotchLogA = -1, bestNotchLogB = -1;
            Vector3 notchEndA = Vector3.zero, notchEndB = Vector3.zero;
            float bestNotchDist = 1.65f;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored || _pieces[i].Kind != RigidTimberPieceKind.Log) continue;
                byte maskA = _carpentryProfiles[i].EndSaddleNotchMask;
                if (maskA == 0) continue;

                Vector3 cA = ToUnityVec3(_pieces[i].Position);
                Vector3 axisA = ToUnityQuat(_pieces[i].Rotation) * Vector3.up;
                float halfLA = _pieces[i].LengthM * 0.5f;

                for (int endBit = 0; endBit < 2; endBit++)
                {
                    if ((maskA & (1 << endBit)) == 0) continue;
                    Vector3 ePos = cA + axisA * ((endBit == 0 ? -1f : 1f) * Mathf.Max(0.15f, halfLA - 0.34f));
                    float d = Vector2.Distance(new Vector2(ePos.x, ePos.z), new Vector2(targetPos.x, targetPos.z));
                    if (d <= bestNotchDist)
                    {
                        bestNotchDist = d;
                        bestNotchLogA = i;
                        notchEndA = ePos;
                    }
                }
            }

            if (bestNotchLogA >= 0)
            {
                Vector3 axisA = ToUnityQuat(_pieces[bestNotchLogA].Rotation) * Vector3.up;
                Vector2 perpDir = new Vector2(-axisA.z, axisA.x).normalized;
                // Karsi paralel kertikli kutuk ucunu ara
                for (int j = 0; j < _pieceCount; j++)
                {
                    if (j == bestNotchLogA || _pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Log) continue;
                    byte maskB = _carpentryProfiles[j].EndSaddleNotchMask;
                    if (maskB == 0) continue;

                    Vector3 cB = ToUnityVec3(_pieces[j].Position);
                    Vector3 axisB = ToUnityQuat(_pieces[j].Rotation) * Vector3.up;
                    float halfLB = _pieces[j].LengthM * 0.5f;
                    for (int endBit = 0; endBit < 2; endBit++)
                    {
                        if ((maskB & (1 << endBit)) == 0) continue;
                        Vector3 candB = cB + axisB * ((endBit == 0 ? -1f : 1f) * Mathf.Max(0.15f, halfLB - 0.34f));
                        Vector2 diff = new Vector2(candB.x - notchEndA.x, candB.z - notchEndA.z);
                        float distAB = diff.magnitude;
                        if (distAB >= 1.5f && distAB <= realHeldLen + 0.2f && Mathf.Abs(Vector2.Dot(diff / distAB, perpDir)) >= 0.90f && Mathf.Abs(candB.y - notchEndA.y) <= 0.35f)
                        {
                            bestNotchLogB = j;
                            notchEndB = candB;
                            break;
                        }
                    }
                    if (bestNotchLogB >= 0) break;
                }

                float underR = _pieces[bestNotchLogA].DiameterM * 0.5f;
                float sitY = notchEndA.y + underR * 0.48f + realDiam * 0.42f;
                if (bestNotchLogB >= 0)
                {
                    Vector2 a2 = new Vector2(notchEndA.x, notchEndA.z);
                    Vector2 b2 = new Vector2(notchEndB.x, notchEndB.z);
                    float span = Vector2.Distance(a2, b2);
                    Vector2 ab = (b2 - a2) / span;
                    // Kose uclari 0.30m disari tasar (Log Crown!)
                    float crownLen = Mathf.Min(realHeldLen, span + 0.60f);
                    eval.Mode = HorizontalSnapMode.SaddleNotchCrossStack;
                    eval.SaddleSupportA = bestNotchLogA;
                    eval.SaddleSupportB = bestNotchLogB;
                    eval.CenterPos = new Vector3((a2.x + b2.x) * 0.5f, sitY, (a2.y + b2.y) * 0.5f);
                    eval.YawDeg = Mathf.Atan2(ab.y, -ab.x) * Mathf.Rad2Deg;
                    eval.ClearBodyLengthM = crownLen;
                    eval.RequiredTotalCutM = crownLen;
                    eval.LeftoverLengthM = Mathf.Max(0f, realHeldLen - crownLen);
                    return eval;
                }
                else
                {
                    // Tek kertikli koseye 90° dik oturtma
                    float crossYaw = Mathf.Atan2(perpDir.y, -perpDir.x) * Mathf.Rad2Deg;
                    float shift = realHeldLen * 0.5f - 0.32f;
                    Vector2 signDir = Vector2.Dot(new Vector2(targetPos.x - notchEndA.x, targetPos.z - notchEndA.z), perpDir) >= 0f ? perpDir : -perpDir;
                    Vector2 c2 = new Vector2(notchEndA.x, notchEndA.z) + signDir * shift;
                    eval.Mode = HorizontalSnapMode.SaddleNotchCrossStack;
                    eval.SaddleSupportA = bestNotchLogA;
                    eval.CenterPos = new Vector3(c2.x, sitY, c2.y);
                    eval.YawDeg = crossYaw;
                    eval.ClearBodyLengthM = realHeldLen;
                    eval.RequiredTotalCutM = realHeldLen;
                    eval.LeftoverLengthM = 0f;
                    return eval;
                }
            }

            // 3) Serbest Taban Kutugu (Sill Log — duz toprak, [C] tas temel veya ahsap kat dosemesi ustune!)
            float freeSurfY = ResolveSupportingSurfaceY(targetPos.x, targetPos.z, targetPos.y + 1.5f);
            eval.Mode = HorizontalSnapMode.FreeGroundSill;
            eval.CenterPos = new Vector3(targetPos.x, freeSurfY + realDiam * 0.5f, targetPos.z);
            eval.YawDeg = manualYawDeg;
            eval.ClearBodyLengthM = realHeldLen;
            eval.RequiredTotalCutM = realHeldLen;
            eval.LeftoverLengthM = 0f;
            return eval;
        }

        public void ComputeSmartHorizontalLogPose(
            Vector3 targetPos,
            float manualYawDeg,
            float lengthM,
            float diameterM,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out Vector3 outCenterPos,
            out float outYawDeg,
            out int outSupportA,
            out int outSupportB,
            out int outUnderCourseIdx)
        {
            HorizontalPlacementEval ev = EvaluateSmartHorizontalPlacement(targetPos, manualYawDeg, lengthM, diameterM, worldChunks);
            outCenterPos = ev.CenterPos;
            outYawDeg = ev.YawDeg;
            outSupportA = ev.PostA;
            outSupportB = ev.PostB;
            outUnderCourseIdx = ev.UnderCourseIdx;
        }

        /// <summary>
        /// Faz M3 &amp; M4: Sifir ic ice gecmeli yatay yerlestirme + Swing-In takma animasyonu + artan kutuk boyunu dondurme!
        /// </summary>
        public bool TryPlaceSmartHorizontalTimber(
            Vector3 targetPos,
            float manualYawDeg,
            float heldLengthM,
            float diameterM,
            TimberWoodSpecies species,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out float leftoverLengthM,
            out string feedback)
        {
            leftoverLengthM = 0f;
            HorizontalPlacementEval ev = EvaluateSmartHorizontalPlacement(targetPos, manualYawDeg, heldLengthM, diameterM, worldChunks);

            if (ev.Mode == HorizontalSnapMode.BlockedMissingGroove)
            {
                feedback = "⚠️ Direklerin birbirine bakan iç yüzünde henüz OLUK YOK! Önce [1] Balta ile iki direğin iç yüzüne 2'şer kez vurup 3B Dikey Oluk açın (iç içe geçme yasak!).";
                return false;
            }
            if (ev.Mode == HorizontalSnapMode.BlockedTooShort)
            {
                feedback = $"⚠️ Elindeki kütük ({heldLengthM:F2}m) bu iki direk açıklığına ({ev.RequiredTotalCutM:F2}m) kısa geliyor! Daha uzun bir kütük getirin.";
                return false;
            }

            float realDiam = Mathf.Clamp(diameterM, 0.36f, 0.62f);
            Quaternion qHoriz = Quaternion.Euler(0f, ev.YawDeg, 0f) * Quaternion.Euler(0f, 0f, 90f);

            int snapIsland = -1;
            if (ev.PostA >= 0) snapIsland = _pieces[ev.PostA].IslandId;
            else if (ev.UnderCourseIdx >= 0) snapIsland = _pieces[ev.UnderCourseIdx].IslandId;
            else if (ev.SaddleSupportA >= 0) snapIsland = _pieces[ev.SaddleSupportA].IslandId;
            int islandId = snapIsland > 0 ? snapIsland : _nextIslandId++;

            RigidTimberPieceKind pieceKind = ev.Mode == HorizontalSnapMode.SideMortiseMainGirder
                ? RigidTimberPieceKind.Beam
                : RigidTimberPieceKind.Log;
            byte matId = pieceKind == RigidTimberPieceKind.Beam
                ? MaterialIdCatalog.TreatedTimberBeam
                : MaterialIdCatalog.ChinkedLogBlock;

            int idx = AddCylinderPieceInternal(
                pieceKind,
                species,
                ev.ClearBodyLengthM,
                realDiam,
                ToSysVec3(ev.CenterPos),
                ToSysQuat(qHoriz),
                islandId,
                matId,
                sharedVoxelMat);

            if (ev.Mode is HorizontalSnapMode.GroovedPostInfill or HorizontalSnapMode.SideMortiseMainGirder)
            {
                _carpentryProfiles[idx].TenonEndMask = 3; // Her iki ucta 3B zivana dili oluk/yuva icine girer!
                RebuildPieceMeshVisual(idx);

                TimberJoineryKind jKind = ev.Mode == HorizontalSnapMode.SideMortiseMainGirder
                    ? TimberJoineryKind.SideMortisePegged
                    : TimberJoineryKind.GroovedChannel;

                Vector3 pA = ToUnityVec3(_pieces[ev.PostA].Position);
                Vector3 pB = ToUnityVec3(_pieces[ev.PostB].Position);
                AddBondInternal(ev.PostA, idx, jKind, realDiam, ToSysVec3(new Vector3(pA.x, ev.CenterPos.y, pA.z)));
                AddBondInternal(ev.PostB, idx, jKind, realDiam, ToSysVec3(new Vector3(pB.x, ev.CenterPos.y, pB.z)));
                if (ev.UnderCourseIdx >= 0)
                {
                    AddBondInternal(ev.UnderCourseIdx, idx, TimberJoineryKind.GroovedChannel, realDiam, ToSysVec3(ev.CenterPos));
                }

                // 0.38 sn Derin-Sig Oluk (Swing-In) takma animasyonu: sol ucu derin oluga girip sag ucu cevirerek yerine oturur!
                StartSwingInVisualAnimation(idx, ev.CenterPos, qHoriz);
            }
            else if (ev.Mode == HorizontalSnapMode.SaddleNotchCrossStack)
            {
                if (ev.SaddleSupportA >= 0)
                    AddBondInternal(ev.SaddleSupportA, idx, TimberJoineryKind.SaddleNotch, realDiam, ToSysVec3(ev.CenterPos));
                if (ev.SaddleSupportB >= 0)
                    AddBondInternal(ev.SaddleSupportB, idx, TimberJoineryKind.SaddleNotch, realDiam, ToSysVec3(ev.CenterPos));
                StartSwingInVisualAnimation(idx, ev.CenterPos, qHoriz);
            }
            else if (ev.Mode == HorizontalSnapMode.FreeGroundSill)
            {
                // Dogrudan saglam zemine oturan taban kutugu (altindaki toprak kazilirsa Ground Watchdog dusurur!)
                _pieces[idx].Flags |= 2;
            }

            _needsStaticReeval = true;
            ReevaluateStaticEquilibrium(worldChunks);

            leftoverLengthM = ev.LeftoverLengthM;
            string leftoverNote = leftoverLengthM >= 0.85f
                ? $" | Artan {leftoverLengthM:F2}m sağlam kütük yanına yere bırakıldı!"
                : string.Empty;

            feedback = ev.Mode switch
            {
                HorizontalSnapMode.SideMortiseMainGirder =>
                    $"🏗️ Ana Kat Kirişi ({ev.ClearBodyLengthM:F2}m) yan zıvana yuvalarına SIFIR İÇ İÇE GEÇMEYLE oturdu!{leftoverNote}",
                HorizontalSnapMode.GroovedPostInfill =>
                    $"🪵 {ev.CourseNumber}. Sıra Duvar Kütüğü ({ev.ClearBodyLengthM:F2}m) oluklara Swing-In ile kilitlendi!{leftoverNote}",
                HorizontalSnapMode.SaddleNotchCrossStack =>
                    $"🪵 Çapraz Yığma Kütük ({ev.ClearBodyLengthM:F2}m) 3B Eyer Kertiklerine tam oturdu!{leftoverNote}",
                _ => $"🪵 Taban Kütüğü ({ev.ClearBodyLengthM:F2}m) yatırıldı! Uçlarına [1] Balta ile vurarak 3B Eyer Kertiği açabilirsiniz."
            };
            return true;
        }

        public bool PlaceHorizontalLogOrBeam(
            Vector3 targetPos,
            float yawDeg,
            float lengthM,
            float diameterM,
            TimberWoodSpecies species,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            if (TryPlaceSmartHorizontalTimber(targetPos, yawDeg, lengthM, diameterM, species, sharedVoxelMat, worldChunks, out _, out feedback))
                return true;

            // Rampa vb. otomatik teslimatta oluk henuz acilmamissa serbest yatay kiris olarak ust kata birakir
            float realLen = Mathf.Clamp(lengthM, 1.5f, 16.0f);
            float realDiam = Mathf.Clamp(diameterM, 0.36f, 0.62f);
            Quaternion qHoriz = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(0f, 0f, 90f);
            AddCylinderPieceInternal(RigidTimberPieceKind.Log, species, realLen, realDiam, ToSysVec3(targetPos), ToSysQuat(qHoriz), _nextIslandId++, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
            feedback = $"🪵 Kütük ({realLen:F1}m) bırakıldı.";
            return true;
        }

        private void StartSwingInVisualAnimation(int pieceIdx, Vector3 finalPos, Quaternion finalRot)
        {
            if ((uint)pieceIdx >= (uint)_pieceCount || _pieceVisuals[pieceIdx] == null) return;
            Vector3 axis = finalRot * Vector3.up;
            Vector3 side = Vector3.Cross(Vector3.up, axis).normalized;
            Vector3 startPos = finalPos - axis * 0.10f + side * 0.28f + Vector3.up * 0.26f;
            Quaternion startRot = Quaternion.Euler(0f, 11f, 0f) * finalRot;

            for (int i = 0; i < _swingInAnims.Length; i++)
            {
                if (!_swingInAnims[i].Active || _swingInAnims[i].PieceIndex == pieceIdx)
                {
                    _swingInAnims[i] = new SwingInVisualAnim
                    {
                        Active = true,
                        PieceIndex = pieceIdx,
                        StartPos = startPos,
                        EndPos = finalPos,
                        StartRot = startRot,
                        EndRot = finalRot,
                        Elapsed = 0f,
                        Duration = 0.36f
                    };
                    _pieceVisuals[pieceIdx]!.transform.position = startPos;
                    _pieceVisuals[pieceIdx]!.transform.rotation = startRot;
                    return;
                }
            }
        }

        /// <summary>
        /// Kazilmis cukurun icine dikili direklerde cevre cimen dudagini (rim) bulur; cukur dibini baz alip yanlis yukseklik olcmeyi onler!
        /// </summary>
        public float SampleSurroundingRimSurfaceY(float wx, float wz, float refY)
        {
            float cY = ResolveSupportingSurfaceY(wx, wz, refY);
            float eY = ResolveSupportingSurfaceY(wx + 1.65f, wz, refY);
            float wY = ResolveSupportingSurfaceY(wx - 1.65f, wz, refY);
            float nY = ResolveSupportingSurfaceY(wx, wz + 1.65f, refY);
            float sY = ResolveSupportingSurfaceY(wx, wz - 1.65f, refY);
            return Mathf.Max(cY, Mathf.Max(Mathf.Max(eY, wY), Mathf.Max(nY, sY)));
        }

        public float SampleMaxSurfaceAlongSpan(Vector2 startXZ, Vector2 endXZ, float refY)
        {
            float maxY = Mathf.Max(
                SampleSurroundingRimSurfaceY(startXZ.x, startXZ.y, refY),
                SampleSurroundingRimSurfaceY(endXZ.x, endXZ.y, refY));
            for (int s = 1; s <= 5; s++)
            {
                float t = s / 6.0f;
                Vector2 pt = Vector2.Lerp(startXZ, endXZ, t);
                float h = ResolveSupportingSurfaceY(pt.x, pt.y, refY);
                if (h > maxY) maxY = h;
            }
            return maxY;
        }

        /// <summary>
        /// Faz M4 &amp; M5: Verilen (wx, wz) noktasinda hem voksel zemini hem de yurunebilir RigidTimber kat dosemelerini (Plank) ornekler!
        /// </summary>
        public float ResolveSupportingSurfaceY(float wx, float wz, float refY)
        {
            float bestY = _soilSampler.SampleSurfaceHeightY(wx, wz, refY);
            if (TrySampleSupportingFloorTopY(wx, wz, refY + 1.2f, out float plankTopY) && plankTopY > bestY)
            {
                bestY = plankTopY;
            }
            return bestY;
        }

        public bool TrySampleSupportingFloorTopY(float wx, float wz, float maxAllowedY, out float topY)
        {
            topY = float.MinValue;
            bool found = false;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                if (_pieces[i].Kind != RigidTimberPieceKind.Plank) continue;

                Vector3 c = ToUnityVec3(_pieces[i].Position);
                Quaternion inv = Quaternion.Inverse(ToUnityQuat(_pieces[i].Rotation));
                Vector3 loc = inv * new Vector3(wx - c.x, 0f, wz - c.z);
                Vector3 half = _pieceHalfExtents[i];
                if (Mathf.Abs(loc.x) <= half.x + 0.08f && Mathf.Abs(loc.z) <= half.z + 0.08f)
                {
                    float candidateTop = c.y + half.y;
                    if (candidateTop <= maxAllowedY && candidateTop > topY)
                    {
                        topY = candidateTop;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Faz M4: Elde TimberPlank (Kalas) varken iki kiris arasina Kat Dosemesi (mode=0), Kat Merdiveni (mode=1) veya Cati Mertegi+Kaplamasi (mode=2) kurar!
        /// </summary>
        public bool TryEvaluateAndPlacePlankCarpentry(
            int mode, // 0=FloorDeck, 1=StairFlight, 2=RoofSlope
            Vector3 targetPos,
            float manualYawDeg,
            bool executePlace,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out Vector3 previewPos,
            out Quaternion previewRot,
            out Vector3 previewSize,
            out string statusHint)
        {
            _soilSampler.WorldChunks = worldChunks;
            previewPos = targetPos;
            previewRot = Quaternion.Euler(0f, manualYawDeg, 0f);
            previewSize = new Vector3(2.2f, 0.20f, 2.2f);
            statusHint = string.Empty;

            if (mode == 0)
            {
                // Iki paralel ANA KAT KIRISI (Beam / TieBeam) arasina tam acida ve tam aciklikta SIFIR IC ICE GECMELI Kat Dosemesi!
                // (Duvar dolgusu olan alt Log siralariyla ASLA eslesmez -> gogus hizasina yanlis doseme atmaz!)
                int beamA = -1, beamB = -1;
                float bestScore = float.MaxValue;
                for (int i = 0; i < _pieceCount; i++)
                {
                    if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                    if (_pieces[i].Kind is not (RigidTimberPieceKind.Beam or RigidTimberPieceKind.TieBeam)) continue;
                    Vector3 pA = ToUnityVec3(_pieces[i].Position);
                    if (Mathf.Abs(pA.y - targetPos.y) > 1.35f) continue;

                    Vector3 axisA = (ToUnityQuat(_pieces[i].Rotation) * Vector3.up).normalized;
                    for (int j = i + 1; j < _pieceCount; j++)
                    {
                        if (_pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored) continue;
                        if (_pieces[j].Kind is not (RigidTimberPieceKind.Beam or RigidTimberPieceKind.TieBeam)) continue;
                        Vector3 pB = ToUnityVec3(_pieces[j].Position);
                        if (Mathf.Abs(pA.y - pB.y) > 0.30f) continue;

                        Vector3 axisB = (ToUnityQuat(_pieces[j].Rotation) * Vector3.up).normalized;
                        if (Mathf.Abs(Vector3.Dot(axisA, axisB)) < 0.90f) continue;

                        float span = Vector2.Distance(new Vector2(pA.x, pA.z), new Vector2(pB.x, pB.z));
                        if (span < 1.2f || span > 9.5f) continue;

                        Vector3 mid = (pA + pB) * 0.5f;
                        float dTarget = Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(targetPos.x, targetPos.z)) + Mathf.Abs(pA.y - targetPos.y) * 0.5f;
                        if (dTarget <= span * 0.85f + 1.8f && dTarget < bestScore)
                        {
                            bestScore = dTarget;
                            beamA = i;
                            beamB = j;
                        }
                    }
                }

                if (beamA >= 0 && beamB >= 0)
                {
                    Vector3 pA = ToUnityVec3(_pieces[beamA].Position);
                    Vector3 pB = ToUnityVec3(_pieces[beamB].Position);
                    Vector3 axisAlong = (ToUnityQuat(_pieces[beamA].Rotation) * Vector3.up);
                    axisAlong.y = 0f;
                    if (axisAlong.sqrMagnitude < 1e-4f) axisAlong = Vector3.right;
                    axisAlong.Normalize();

                    Vector3 across = new Vector3(pB.x - pA.x, 0f, pB.z - pA.z);
                    float spanAcross = across.magnitude;
                    Vector3 acrossDir = spanAcross > 0.05f ? (across / spanAcross) : Vector3.forward;

                    // Merdiven boslugu birakabilmesi icin boyuna 2.2m dilimler halinde doseme serer!
                    float maxAlong = Mathf.Min(_pieces[beamA].LengthM, _pieces[beamB].LengthM);
                    float stripWidthAlong = Mathf.Min(2.20f, maxAlong);
                    Vector3 midBase = (pA + pB) * 0.5f;
                    float projAlong = Mathf.Clamp(Vector3.Dot(targetPos - midBase, axisAlong), -maxAlong * 0.5f + stripWidthAlong * 0.5f, maxAlong * 0.5f - stripWidthAlong * 0.5f);

                    float topBeamY = Mathf.Max(pA.y + _pieces[beamA].DiameterM * 0.5f, pB.y + _pieces[beamB].DiameterM * 0.5f);
                    const float plankThick = 0.20f;
                    previewPos = new Vector3(midBase.x + axisAlong.x * projAlong, topBeamY + plankThick * 0.5f, midBase.z + axisAlong.z * projAlong);
                    previewRot = Quaternion.LookRotation(acrossDir, Vector3.up);
                    previewSize = new Vector3(stripWidthAlong, plankThick, spanAcross + 0.16f);
                    statusHint = $"🟫 Kat Döşeme Paneli ({stripWidthAlong:F1}×{spanAcross:F1}m) — Kirişlerin ÜST YÜZEYİNE Sıfır İç İçe Geçmeyle Oturur | [V] Mod Değiştir | [Sol Tık] Döşe";

                    if (executePlace)
                    {
                        int isl = _pieces[beamA].IslandId > 0 ? _pieces[beamA].IslandId : _nextIslandId++;
                        int dIdx = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, previewSize, ToSysVec3(previewPos), ToSysQuat(previewRot), isl, MaterialIdCatalog.WoodPlank, sharedVoxelMat);
                        AddBondInternal(beamA, dIdx, TimberJoineryKind.JoistPocket, 0.25f, ToSysVec3(new Vector3(pA.x, topBeamY, pA.z)));
                        AddBondInternal(beamB, dIdx, TimberJoineryKind.JoistPocket, 0.25f, ToSysVec3(new Vector3(pB.x, topBeamY, pB.z)));
                        _needsStaticReeval = true;
                        ReevaluateStaticEquilibrium(worldChunks);
                        statusHint = "🟫 Ahşap Kat Döşemesi kirişlerin üst yüzeyine kavelalandı! (Merdiven çıkışı için 1 dilimi boş bırakabilirsiniz)";
                    }
                    return true;
                }

                // Yakinda 2 kiris yoksa zemin/mevcut doseme hizasinda serbest doseme
                float surfY = ResolveSupportingSurfaceY(targetPos.x, targetPos.z, targetPos.y + 1.0f);
                previewPos = new Vector3(targetPos.x, surfY + 0.11f, targetPos.z);
                previewSize = new Vector3(2.2f, 0.20f, 2.2f);
                statusHint = "🟫 Zemin Ahşap Döşeme Paneli (2.2×2.2m) | [V] Merdiven/Çatı Modu | [R] 15° Döndür | [Sol Tık] Döşe";
                if (executePlace)
                {
                    int gIdx = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, previewSize, ToSysVec3(previewPos), ToSysQuat(previewRot), _nextIslandId++, MaterialIdCatalog.WoodPlank, sharedVoxelMat);
                    _pieces[gIdx].Flags |= 2; // Dogrudan zemine oturan doseme (altindaki toprak kazilirsa Ground Watchdog dusurur!)
                    statusHint = "🟫 Ahşap döşeme yerleştirildi.";
                }
                return true;
            }

            if (mode == 1)
            {
                // Kat Merdiveni (13 basamakli, <= 0.24m alcak rihtli puruzsuz yurunebilir merdiven!)
                float baseY = ResolveSupportingSurfaceY(targetPos.x, targetPos.z, targetPos.y + 0.5f);
                float storyRise = 3.20f;
                int nearBeam = -1;
                float bestBDist = 5.2f;
                for (int i = 0; i < _pieceCount; i++)
                {
                    if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                    if (_pieces[i].Kind is not (RigidTimberPieceKind.Beam or RigidTimberPieceKind.Plank)) continue;
                    Vector3 bp = ToUnityVec3(_pieces[i].Position);
                    float rise = bp.y - baseY;
                    if (rise >= 1.8f && rise <= 4.4f)
                    {
                        float d = Vector2.Distance(new Vector2(bp.x, bp.z), new Vector2(targetPos.x, targetPos.z));
                        if (d < bestBDist)
                        {
                            bestBDist = d;
                            nearBeam = i;
                            storyRise = rise + 0.14f;
                        }
                    }
                }

                const int steps = 13;
                const float stepRunZ = 0.29f;
                float totalRun = (steps - 1) * stepRunZ;
                Vector3 fwd = previewRot * Vector3.forward;
                previewPos = new Vector3(targetPos.x, baseY + storyRise * 0.5f, targetPos.z) + fwd * (totalRun * 0.5f);
                previewSize = new Vector3(1.50f, 0.20f, totalRun + 0.36f);
                previewRot = previewRot * Quaternion.Euler(-Mathf.Atan2(storyRise, totalRun) * Mathf.Rad2Deg, 0f, 0f);
                statusHint = $"🪜 Çıkılabilir Ahşap Kat Merdiveni (Yükseklik: {storyRise:F2}m, {steps} Basamak) | [R] Yön Döndür | [V] Çatı Modu | [Sol Tık] Kur";

                if (executePlace)
                {
                    int isl = nearBeam >= 0 && _pieces[nearBeam].IslandId > 0 ? _pieces[nearBeam].IslandId : _nextIslandId++;
                    Quaternion flatRot = Quaternion.Euler(0f, manualYawDeg, 0f);
                    float riser = storyRise / steps;
                    for (int s = 0; s < steps; s++)
                    {
                        float topStepY = baseY + (s + 1) * riser;
                        float sy = topStepY - 0.09f;
                        float sz = s * stepRunZ;
                        Vector3 sp = new Vector3(targetPos.x, sy, targetPos.z) + (flatRot * new Vector3(0f, 0f, sz));
                        int stepIdx = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Oak, new Vector3(1.50f, 0.18f, 0.36f), ToSysVec3(sp), ToSysQuat(flatRot), isl, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);
                        if (nearBeam >= 0)
                        {
                            AddBondInternal(nearBeam, stepIdx, TimberJoineryKind.Pegged, 0.24f, ToSysVec3(sp));
                        }
                    }
                    _needsStaticReeval = true;
                    ReevaluateStaticEquilibrium(worldChunks);
                    statusHint = $"🪜 {steps} basamaklı Çıkılabilir Kat Merdiveni kuruldu! Zıplamadan üst kata yürüyebilirsiniz.";
                }
                return true;
            }

            // mode == 2: Egimli Besik Cati Mertegi + Zona Kaplama (Yalnizca PARALEL Sacak Kirisi <-> Mahya Kirisi arasina simetrik egimle!)
            int lowBeam = -1, highRidge = -1;
            float bestRoofScore = float.MaxValue;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                if (_pieces[i].Kind is not (RigidTimberPieceKind.Beam or RigidTimberPieceKind.TieBeam)) continue;
                Vector3 pLow = ToUnityVec3(_pieces[i].Position);
                Vector3 lowAxis = ToUnityQuat(_pieces[i].Rotation) * Vector3.up;
                lowAxis.y = 0f;
                if (lowAxis.sqrMagnitude < 1e-4f) continue;
                lowAxis.Normalize();

                for (int j = 0; j < _pieceCount; j++)
                {
                    if (i == j || _pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored) continue;
                    if (_pieces[j].Kind is not (RigidTimberPieceKind.Beam or RigidTimberPieceKind.RidgeBeam)) continue;
                    Vector3 pHigh = ToUnityVec3(_pieces[j].Position);
                    float rise = pHigh.y - pLow.y;
                    if (rise < 0.85f || rise > 4.5f) continue;

                    Vector3 highAxis = ToUnityQuat(_pieces[j].Rotation) * Vector3.up;
                    highAxis.y = 0f;
                    if (highAxis.sqrMagnitude < 1e-4f) continue;
                    highAxis.Normalize();

                    // Sacak ve Mahya kirisleri KESINLIKLE birbirine paralel olmalidir (dik duvar kirisleriyle capraz cati kurulamaz!)
                    if (Mathf.Abs(Vector3.Dot(lowAxis, highAxis)) < 0.92f) continue;

                    // Mahya eksenine DIK yatay acikligi (perpXZ) olc: yan duvarda ara dikme oldugu icin yari-boy sacak kirisi kullanilsa bile
                    // hem sol hem sag cati kanadi %100 simetrik yakalanir!
                    Vector3 rawDiffXZ = new Vector3(pHigh.x - pLow.x, 0f, pHigh.z - pLow.z);
                    float alongCenterOffset = Vector3.Dot(rawDiffXZ, highAxis);
                    float maxOverlapOffset = (_pieces[i].LengthM + _pieces[j].LengthM) * 0.5f - 0.35f;
                    if (Mathf.Abs(alongCenterOffset) > maxOverlapOffset) continue;

                    Vector3 perpXZ = rawDiffXZ - highAxis * alongCenterOffset;
                    float runXZ = perpXZ.magnitude;
                    if (runXZ < 1.1f || runXZ > 7.5f) continue;

                    Vector3 pLowAligned = pLow + highAxis * Vector3.Dot(pHigh - pLow, highAxis);
                    Vector3 mid = (pLowAligned + pHigh) * 0.5f;
                    float halfRidgeL = _pieces[j].LengthM * 0.5f + 0.85f;
                    float alongProj = Mathf.Clamp(Vector3.Dot(targetPos - mid, highAxis), -halfRidgeL, halfRidgeL);
                    Vector3 sheetPt = mid + highAxis * alongProj;
                    float d = Vector3.Distance(sheetPt, targetPos);
                    if (d <= 6.2f && d < bestRoofScore)
                    {
                        bestRoofScore = d;
                        lowBeam = i;
                        highRidge = j;
                    }
                }
            }

            if (lowBeam >= 0 && highRidge >= 0)
            {
                Vector3 pL = ToUnityVec3(_pieces[lowBeam].Position);
                Vector3 pH = ToUnityVec3(_pieces[highRidge].Position);
                Vector3 ridgeAxis = (ToUnityQuat(_pieces[highRidge].Rotation) * Vector3.up);
                ridgeAxis.y = 0f;
                if (ridgeAxis.sqrMagnitude < 1e-4f) ridgeAxis = Vector3.forward;
                ridgeAxis.Normalize();

                Vector3 pLAligned = pL + ridgeAxis * Vector3.Dot(pH - pL, ridgeAxis);
                Vector3 slopeVec = pH - pLAligned;
                Vector3 horizSlope = new Vector3(slopeVec.x, 0f, slopeVec.z);
                float run = horizSlope.magnitude;
                float rise = pH.y - pL.y;
                float pitchRad = Mathf.Atan2(rise, Mathf.Max(0.2f, run));
                float pitchDeg = pitchRad * Mathf.Rad2Deg;
                // Yan sacak tasmasi (+0.85m disari) + mahya tepesinde tam kavusma (+0.18m) -> disaridan net gorunen gercek dag evi besik catisi!
                float eaveOverhang = 0.85f;
                float ridgeOverlap = 0.18f;
                float slopeLen = (run + eaveOverhang + ridgeOverlap) / Mathf.Max(0.35f, Mathf.Cos(pitchRad));

                Vector3 upSlopeDir = horizSlope.normalized;
                Vector3 slopeUnit3D = (upSlopeDir * Mathf.Cos(pitchRad) + Vector3.up * Mathf.Sin(pitchRad)).normalized;
                Quaternion baseYawRot = Quaternion.LookRotation(upSlopeDir, Vector3.up);
                previewRot = baseYawRot * Quaternion.Euler(-pitchDeg, 0f, 0f);

                // On ve arka kalkan duvarlarin da disina +0.85m tasan (Gable Overhang) tam boy besik cati!
                float totalRidgeCover = _pieces[highRidge].LengthM + 2.24f;
                float widthAlongRidge = Mathf.Max(2.45f, totalRidgeCover / 3.0f + 0.08f);
                Vector3 mid = (pLAligned + pH) * 0.5f;
                float halfCover = totalRidgeCover * 0.5f;
                float proj = Mathf.Clamp(Vector3.Dot(targetPos - mid, ridgeAxis), -halfCover + widthAlongRidge * 0.5f, halfCover - widthAlongRidge * 0.5f);
                float shiftAlongSlope = (ridgeOverlap - eaveOverhang) * 0.5f;
                previewPos = mid + ridgeAxis * proj + slopeUnit3D * shiftAlongSlope + Vector3.up * 0.26f;
                previewSize = new Vector3(widthAlongRidge, 0.22f, slopeLen);
                statusHint = $"🏠 Eğimli Çatı Mertek & Zona Paneli ({pitchDeg:0}° eğim, Saçak Taşmalı) | [V] Döşeme Modu | [Sol Tık] Kapla";

                if (executePlace)
                {
                    int isl = _pieces[highRidge].IslandId > 0 ? _pieces[highRidge].IslandId : _nextIslandId++;
                    int rIdx = AddBoxPieceInternal(RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, previewSize, ToSysVec3(previewPos), ToSysQuat(previewRot), isl, MaterialIdCatalog.TimberShingleRoof, sharedVoxelMat);
                    AddBondInternal(lowBeam, rIdx, TimberJoineryKind.Pegged, 0.28f, ToSysVec3(pL));
                    AddBondInternal(highRidge, rIdx, TimberJoineryKind.Pegged, 0.28f, ToSysVec3(pH));
                    _needsStaticReeval = true;
                    ReevaluateStaticEquilibrium(worldChunks);
                    statusHint = "🏠 Eğimli mertek ve ahşap çatı kaplaması Mahya ile Saçak kirişine kilitlendi!";
                }
                return true;
            }

            float fallbackY = ResolveSupportingSurfaceY(targetPos.x, targetPos.z, targetPos.y + 1.5f) + 1.2f;
            previewPos = new Vector3(targetPos.x, fallbackY, targetPos.z);
            previewRot = Quaternion.Euler(-35f, manualYawDeg, 0f);
            previewSize = new Vector3(2.2f, 0.20f, 2.8f);
            statusHint = "🏠 Çatı Paneli (İpucu: Önce dış duvar kirişi ve daha yüksek bir Mahya Kirişi koyarsanız aralarına otomatik kilitlenir!) | [R] Döndür | [Sol Tık] Kur";
            if (executePlace)
            {
                AddBoxPieceInternal(RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, previewSize, ToSysVec3(previewPos), ToSysQuat(previewRot), _nextIslandId++, MaterialIdCatalog.TimberShingleRoof, sharedVoxelMat);
                statusHint = "🏠 Eğimli çatı paneli yerleştirildi.";
            }
            return true;
        }

        /// <summary>
        /// Faz M5: Kapi kurulurken iki dikey kapi diregi arasindaki net ic yuzey acikligini ve ust lento altina kadar net kapi boyunu olcer.
        /// Arasi kutuk duvarla kapali olan direk ciftlerini reddeder!
        /// </summary>
        public bool TryMeasureDoorwayClearSpan(Vector3 worldPos, out float clearSpanM, out Vector3 hingeWorldPos, out float alignedYawDeg)
            => TryMeasureDoorwayClearSpanAndHeight(worldPos, out clearSpanM, out _, out hingeWorldPos, out alignedYawDeg);

        public bool TryMeasureDoorwayClearSpanAndHeight(Vector3 worldPos, out float clearSpanM, out float clearHeightM, out Vector3 hingeWorldPos, out float alignedYawDeg)
        {
            clearSpanM = 1.90f;
            clearHeightM = 2.20f;
            hingeWorldPos = worldPos;
            alignedYawDeg = 0f;

            int bestA = -1, bestB = -1;
            float bestScore = 3.2f;
            float bestLintelBottomY = float.MaxValue;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored || _pieces[i].Kind != RigidTimberPieceKind.Post) continue;
                Vector3 pA = ToUnityVec3(_pieces[i].Position);
                for (int j = i + 1; j < _pieceCount; j++)
                {
                    if (_pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post) continue;
                    Vector3 pB = ToUnityVec3(_pieces[j].Position);
                    float centerSpan = Vector2.Distance(new Vector2(pA.x, pA.z), new Vector2(pB.x, pB.z));
                    float netClear = centerSpan - (_pieces[i].DiameterM + _pieces[j].DiameterM) * 0.5f;
                    if (netClear < 1.05f || netClear > 2.85f) continue;

                    Vector2 mid = new Vector2((pA.x + pB.x) * 0.5f, (pA.z + pB.z) * 0.5f);
                    float d = Vector2.Distance(mid, new Vector2(worldPos.x, worldPos.z));
                    if (d >= bestScore) continue;

                    // Bu kapi acikligi yatay duvar kutukleriyle orulup kapatilmis mi kontrol et!
                    bool blockedByWallLogs = false;
                    float lowestLintelBot = float.MaxValue;
                    for (int k = 0; k < _pieceCount; k++)
                    {
                        if (_pieceRemoved[k] != 0 || _pieces[k].State != RigidTimberState.StaticAnchored) continue;
                        if (_pieces[k].Kind is not (RigidTimberPieceKind.Log or RigidTimberPieceKind.Beam)) continue;
                        Vector3 pk = ToUnityVec3(_pieces[k].Position);
                        if (Vector2.Distance(new Vector2(pk.x, pk.z), mid) > 0.75f) continue;
                        float botY = pk.y - _pieces[k].DiameterM * 0.5f;
                        if (botY >= worldPos.y - 0.1f && botY < worldPos.y + 1.75f)
                        {
                            blockedByWallLogs = true;
                            break;
                        }
                        if (botY >= worldPos.y + 1.75f && botY < lowestLintelBot)
                        {
                            lowestLintelBot = botY;
                        }
                    }
                    if (blockedByWallLogs) continue;

                    bestScore = d;
                    bestA = i;
                    bestB = j;
                    bestLintelBottomY = lowestLintelBot;
                }
            }

            if (bestA < 0 || bestB < 0) return false;
            Vector3 a = ToUnityVec3(_pieces[bestA].Position);
            Vector3 b = ToUnityVec3(_pieces[bestB].Position);
            Vector2 dir = new Vector2(b.x - a.x, b.z - a.z);
            float dist = dir.magnitude;
            dir /= Mathf.Max(0.01f, dist);
            float rA = _pieces[bestA].DiameterM * 0.5f;
            float rB = _pieces[bestB].DiameterM * 0.5f;
            clearSpanM = Mathf.Clamp(dist - (rA + rB), 1.05f, 2.80f);
            if (bestLintelBottomY < float.MaxValue * 0.5f)
            {
                clearHeightM = Mathf.Clamp(bestLintelBottomY - worldPos.y, 1.80f, 2.65f);
            }
            Vector2 hingeXZ = new Vector2(a.x, a.z) + dir * rA;
            hingeWorldPos = new Vector3(hingeXZ.x, worldPos.y, hingeXZ.y);
            alignedYawDeg = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// Faz M5: Pencere kepengi (WindowShutter) kurulurken iki direk arasindaki net ic acikligi, alt denizlik (sill) ustunu
        /// ve ust lento altini otomatik olcer; boylece pencere boslugu sifira sifir kapanir!
        /// </summary>
        public bool TryMeasureWindowClearSpanAndHeight(Vector3 worldPos, out float clearSpanM, out float clearHeightM, out Vector3 hingeWorldPos, out float alignedYawDeg)
        {
            clearSpanM = 1.70f;
            clearHeightM = 1.05f;
            hingeWorldPos = worldPos;
            alignedYawDeg = 0f;

            int bestA = -1, bestB = -1;
            float bestScore = 3.2f;
            float bestSillTopY = float.MinValue;
            float bestLintelBotY = float.MaxValue;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored || _pieces[i].Kind != RigidTimberPieceKind.Post) continue;
                Vector3 pA = ToUnityVec3(_pieces[i].Position);
                for (int j = i + 1; j < _pieceCount; j++)
                {
                    if (_pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post) continue;
                    Vector3 pB = ToUnityVec3(_pieces[j].Position);
                    float centerSpan = Vector2.Distance(new Vector2(pA.x, pA.z), new Vector2(pB.x, pB.z));
                    float netClear = centerSpan - (_pieces[i].DiameterM + _pieces[j].DiameterM) * 0.5f;
                    if (netClear < 0.85f || netClear > 3.00f) continue;

                    Vector2 mid = new Vector2((pA.x + pB.x) * 0.5f, (pA.z + pB.z) * 0.5f);
                    float d = Vector2.Distance(mid, new Vector2(worldPos.x, worldPos.z));
                    if (d >= bestScore) continue;

                    float sillTop = float.MinValue;
                    float lintelBot = float.MaxValue;
                    bool blockedAtCenter = false;
                    for (int k = 0; k < _pieceCount; k++)
                    {
                        if (_pieceRemoved[k] != 0 || _pieces[k].State != RigidTimberState.StaticAnchored) continue;
                        if (_pieces[k].Kind is not (RigidTimberPieceKind.Log or RigidTimberPieceKind.Beam)) continue;
                        Vector3 pk = ToUnityVec3(_pieces[k].Position);
                        if (Vector2.Distance(new Vector2(pk.x, pk.z), mid) > 0.75f) continue;
                        float topY = pk.y + _pieces[k].DiameterM * 0.48f;
                        float botY = pk.y - _pieces[k].DiameterM * 0.48f;
                        if (worldPos.y > botY + 0.08f && worldPos.y < topY - 0.08f)
                        {
                            blockedAtCenter = true;
                            break;
                        }
                        if (topY <= worldPos.y + 0.15f && topY > worldPos.y - 1.50f && topY > sillTop) sillTop = topY;
                        if (botY >= worldPos.y - 0.15f && botY < worldPos.y + 1.65f && botY < lintelBot) lintelBot = botY;
                    }
                    if (blockedAtCenter || sillTop < -1e6f) continue;

                    bestScore = d;
                    bestA = i;
                    bestB = j;
                    bestSillTopY = sillTop;
                    bestLintelBotY = lintelBot;
                }
            }

            if (bestA < 0 || bestB < 0) return false;
            Vector3 a = ToUnityVec3(_pieces[bestA].Position);
            Vector3 b = ToUnityVec3(_pieces[bestB].Position);
            Vector2 dir = new Vector2(b.x - a.x, b.z - a.z);
            float dist = dir.magnitude;
            dir /= Mathf.Max(0.01f, dist);
            float rA = _pieces[bestA].DiameterM * 0.5f;
            float rB = _pieces[bestB].DiameterM * 0.5f;
            clearSpanM = Mathf.Clamp(dist - (rA + rB), 0.85f, 2.95f);
            float baseWinY = bestSillTopY;
            if (bestLintelBotY < float.MaxValue * 0.5f && bestLintelBotY > baseWinY + 0.45f)
            {
                clearHeightM = Mathf.Clamp(bestLintelBotY - baseWinY, 0.65f, 1.80f);
            }
            Vector2 hingeXZ = new Vector2(a.x, a.z) + dir * rA;
            hingeWorldPos = new Vector3(hingeXZ.x, baseWinY, hingeXZ.y);
            alignedYawDeg = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// Faz M5: Tas Somine bacasinin evin en ust catisini 0.65m asmasi icin ustteki en yuksek cati noktasini olcer.
        /// </summary>
        public float SampleHighestRoofTopYNear(float wx, float wz, float baseFloorY)
        {
            float maxRoofY = baseFloorY + 3.4f;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                Vector3 p = ToUnityVec3(_pieces[i].Position);
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(wx, wz)) <= 5.5f)
                {
                    float top = p.y + _pieceHalfExtents[i].y;
                    if (top > maxRoofY) maxRoofY = top;
                }
            }
            return maxRoofY;
        }

        /// <summary>
        /// Faz M5: Gercek 3B Chinking (kil/lif derz harci) uygular ve etraftaki gercek duvar/cati sarmalini olcer!
        /// </summary>
        public int ApplyVisualChinkingNear(Vector3 playerPos, float radius, out int totalWallLogsNear, out int chinkedLogsNear)
        {
            int newlyChinked = 0;
            totalWallLogsNear = 0;
            chinkedLogsNear = 0;
            float rSq = radius * radius;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored || _pieces[i].Kind != RigidTimberPieceKind.Log) continue;
                Vector3 p = ToUnityVec3(_pieces[i].Position);
                if ((p - playerPos).sqrMagnitude > rSq) continue;

                totalWallLogsNear++;
                if (_carpentryProfiles[i].ChinkedSeamMask == 0 && newlyChinked < 4)
                {
                    _carpentryProfiles[i].ChinkedSeamMask = 1;
                    RebuildPieceMeshVisual(i);
                    newlyChinked++;
                }
                if (_carpentryProfiles[i].ChinkedSeamMask != 0)
                {
                    chinkedLogsNear++;
                }
            }
            return newlyChinked;
        }

        public void EvaluateRealCabinEnclosure(Vector3 playerPos, out int wallLogCount, out int chinkedLogCount, out bool hasRoofOverhead, out int enclosedDirectionsCount)
        {
            wallLogCount = 0;
            chinkedLogCount = 0;
            hasRoofOverhead = false;
            byte dirMask = 0;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                Vector3 p = ToUnityVec3(_pieces[i].Position);
                Vector3 d = p - playerPos;
                float distXZ = new Vector2(d.x, d.z).magnitude;

                if ((_pieces[i].Kind == RigidTimberPieceKind.Plank || _pieces[i].Kind == RigidTimberPieceKind.Rafter) &&
                    d.y >= 1.2f && d.y <= 11.5f && distXZ <= 4.5f)
                {
                    hasRoofOverhead = true;
                }

                if (_pieces[i].Kind == RigidTimberPieceKind.Log && distXZ <= 7.5f && Mathf.Abs(d.y) <= 4.0f)
                {
                    wallLogCount++;
                    if (_carpentryProfiles[i].ChinkedSeamMask != 0) chinkedLogCount++;
                    if (Mathf.Abs(d.x) >= Mathf.Abs(d.z))
                        dirMask |= (byte)(d.x > 0f ? 1 : 2);
                    else
                        dirMask |= (byte)(d.z > 0f ? 4 : 8);
                }
            }

            enclosedDirectionsCount = ((dirMask & 1) != 0 ? 1 : 0) +
                                      ((dirMask & 2) != 0 ? 1 : 0) +
                                      ((dirMask & 4) != 0 ? 1 : 0) +
                                      ((dirMask & 8) != 0 ? 1 : 0);
        }

        /// <summary>
        /// [L] Oyuncunun hedefledigi yere gercek agac kalinliginda (0.60m) tek parca dikey sutun yerlestirir.
        /// </summary>
        public bool PlaceVerticalTimberPost(
            Vector3 targetSurfacePos,
            float lengthM,
            float diameterM,
            TimberWoodSpecies species,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            _soilSampler.WorldChunks = worldChunks;
            float baseGroundY = _soilSampler.SampleSurfaceHeightY(targetSurfacePos.x, targetSurfacePos.z, targetSurfacePos.y);
            float realDiam = Mathf.Max(0.56f, diameterM);
            float centerY = baseGroundY + lengthM * 0.5f;

            int islandId = _nextIslandId++;
            int idx = AddCylinderPieceInternal(
                RigidTimberPieceKind.Post,
                species,
                lengthM,
                realDiam,
                new SysVec3(targetSurfacePos.x, centerY, targetSurfacePos.z),
                SysQuat.Identity,
                islandId,
                MaterialIdCatalog.OakWood,
                sharedVoxelMat);

            _soilProfiles[idx] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[idx], _soilSampler);
            _needsStaticReeval = true;
            ReevaluateStaticEquilibrium(worldChunks);

            float embedD = _soilProfiles[idx].EmbedDepthM;
            feedback = embedD >= 0.50f
                ? $"🪵 Tek-parça sütun ({lengthM:0.0}m, Ø{realDiam:0.00}m) dikildi! Gömülme: {embedD:0.00}m | Direnç: {_soilProfiles[idx].LastEffectiveResistMomentNm / 1000f:0.0} kN·m"
                : $"⚠️ Tek-parça sütun ({lengthM:0.0}m, Ø{realDiam:0.00}m) yüzeyde (gömülme {embedD:0.00}m)! Çukur kazıp [V] toprak dolgusuyla gömün!";
            return true;
        }

        /// <summary>
        /// [Shift+L] Gercek 3 Katli Tek-Parca Ahsap Ev Pilotu (7.6m x 6.4m taban, 3 kat = 9.0m + 2.6m besik cati):
        /// - 0.62m kalin mese kose sutunlari + 0.50m kalin gercek kutuk duvar siralari (kapi & pencere aciklikli)
        /// - 3 kat tam yurunebilir ahsap doseme + katlar arasi ahsap merdivenler
        /// - Mahya, gergi kirisleri, mertekler ve tam kapali egimli ahsap cati kaplamasi
        /// </summary>
        public bool SpawnThreeStoryLogCabinPilot(
            Vector3 originPos,
            float yawDeg,
            Material? sharedVoxelMat,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            _soilSampler.WorldChunks = worldChunks;
            float groundY = _soilSampler.SampleSurfaceHeightY(originPos.x, originPos.z, originPos.y);
            LastSpawnedCabinGroundY = groundY;

            int islandId = _nextIslandId++;
            Quaternion rotY = Quaternion.Euler(0f, yawDeg, 0f);
            SysQuat qYaw = ToSysQuat(rotY);

            const float spanX = 7.6f;
            const float spanZ = 6.4f;
            const float halfX = spanX * 0.5f;   // 3.8m
            const float halfZ = spanZ * 0.5f;   // 3.2m
            const float embedDepth = 2.0f;      // 3 katli agir yapi icin 2.0m gercek derin gomulme!
            const float storyH = 3.25f;         // Rahat tavan & merdiven bas mesafesi
            const float postH = embedDepth + storyH * 3.0f + 0.35f; // 12.10m
            const float postDiam = 0.62f;       // Gercek kalin mese agaci govdesi
            const float logWallDiam = 0.50f;    // Gercek kalin duvar kutugu
            const float beamDiam = 0.42f;       // Ana tasiyici kiris

            Vector3 ToWorld(Vector3 local) => new Vector3(originPos.x, groundY, originPos.z) + (rotY * local);

            // 1) 4 Kalin Kose Sutunu (1.5m topraga gomulu)
            int[] postIndices = new int[4];
            Vector3[] cornerLocal =
            {
                new Vector3(-halfX, postH * 0.5f - embedDepth, -halfZ),
                new Vector3( halfX, postH * 0.5f - embedDepth, -halfZ),
                new Vector3( halfX, postH * 0.5f - embedDepth,  halfZ),
                new Vector3(-halfX, postH * 0.5f - embedDepth,  halfZ)
            };

            for (int c = 0; c < 4; c++)
            {
                Vector3 wp = ToWorld(cornerLocal[c]);
                // Her kose sutununu gercek zemin derinligine (1.5m) oturtmak icin o kosenin altindaki topragi dogrula
                float cornerGroundY = _soilSampler.SampleSurfaceHeightY(wp.x, wp.z, groundY + 4.0f);
                if (cornerGroundY < groundY - 0.5f && worldChunks != null)
                {
                    // Egimli yamacta bosluk kalan kose altini gercek toprakla (DogalKutle) destekle ki kose sutunu gercekten gomulu olsun!
                    int gx = Mathf.FloorToInt(wp.x), gz = Mathf.FloorToInt(wp.z);
                    int gcx = gx >> 4, gcz = gz >> 4;
                    if (worldChunks.TryGetValue((gcx, gcz), out uint[]? gVox) && gVox != null)
                    {
                        int glx = gx - (gcx << 4), glz = gz - (gcz << 4);
                        int topFill = Mathf.Clamp(Mathf.FloorToInt(groundY), 2, 250);
                        int botFill = Mathf.Clamp(Mathf.FloorToInt(cornerGroundY), 2, topFill);
                        for (int fy = botFill; fy <= topFill; fy++)
                        {
                            gVox[(fy << 8) | (glz << 4) | glx] = VoxelBitUtility.PackStandard(MaterialIdCatalog.Dirt, 127, 0, 0, BlokKokenDurumu.DogalKutle, 0);
                        }
                    }
                }
                int pIdx = AddCylinderPieceInternal(
                    RigidTimberPieceKind.Post,
                    TimberWoodSpecies.Oak,
                    postH,
                    postDiam,
                    ToSysVec3(wp),
                    qYaw,
                    islandId,
                    MaterialIdCatalog.OakWood,
                    sharedVoxelMat);
                postIndices[c] = pIdx;
                // Gercek voksel zemin gomulmesini oku (sahte override YOK — altindaki toprak giderse aninda coker!)
                _soilProfiles[pIdx] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[pIdx], _soilSampler, compactionFactor: 0.92f, hasStoneCollar: true);
            }

            SysQuat qAxisX = ToSysQuat(rotY * Quaternion.Euler(0f, 0f, 90f));
            SysQuat qAxisZ = ToSysQuat(rotY * Quaternion.Euler(90f, 0f, 0f));

            // 2) 3 Kat: Ana Cerceve Kirisleri + Gercek Kalin Kutuk Duvar Siralari (Kapi & Pencere Aciklikli) + Doseme + Merdiven
            int[] topWallBeams = new int[4];
            for (int story = 0; story < 3; story++)
            {
                float floorY = story == 0 ? 0.28f : (story * storyH + 0.14f);
                float topBeamY = (story + 1) * storyH;

                // Kat ust cerceve kirisleri (0.42m kalinlik)
                int bFront = AddCylinderPieceInternal(RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, spanX + 0.4f, beamDiam, ToSysVec3(ToWorld(new Vector3(0f, topBeamY, -halfZ))), qAxisX, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);
                int bBack = AddCylinderPieceInternal(RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, spanX + 0.4f, beamDiam, ToSysVec3(ToWorld(new Vector3(0f, topBeamY, halfZ))), qAxisX, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);
                int bLeft = AddCylinderPieceInternal(RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, spanZ + 0.4f, beamDiam, ToSysVec3(ToWorld(new Vector3(-halfX, topBeamY, 0f))), qAxisZ, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);
                int bRight = AddCylinderPieceInternal(RigidTimberPieceKind.Beam, TimberWoodSpecies.Oak, spanZ + 0.4f, beamDiam, ToSysVec3(ToWorld(new Vector3(halfX, topBeamY, 0f))), qAxisZ, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);

                AddBondInternal(postIndices[0], bFront, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(-halfX, topBeamY, -halfZ))));
                AddBondInternal(postIndices[1], bFront, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(halfX, topBeamY, -halfZ))));
                AddBondInternal(postIndices[3], bBack, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(-halfX, topBeamY, halfZ))));
                AddBondInternal(postIndices[2], bBack, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(halfX, topBeamY, halfZ))));
                AddBondInternal(postIndices[0], bLeft, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(-halfX, topBeamY, -halfZ))));
                AddBondInternal(postIndices[3], bLeft, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(-halfX, topBeamY, halfZ))));
                AddBondInternal(postIndices[1], bRight, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(halfX, topBeamY, -halfZ))));
                AddBondInternal(postIndices[2], bRight, TimberJoineryKind.SaddleNotch, beamDiam, ToSysVec3(ToWorld(new Vector3(halfX, topBeamY, halfZ))));

                // Gercek 0.50m kalinliginda 5 sira kutuk duvar (her katta)
                for (int row = 0; row < 5; row++)
                {
                    float ly = story * storyH + 0.30f + row * 0.50f;
                    bool isDoorRow = (story == 0 && row <= 3);          // 1. kat on cephede 2.0m x 2.3m kapi acikligi
                    bool isWindowRow = (row == 2 || row == 3);          // Pencere acikligi satirlari

                    // ON CEPHE (-Z)
                    if (isDoorRow || (story > 0 && isWindowRow))
                    {
                        float segLen = (spanX - 2.0f) * 0.5f; // 2.8m sol & sag
                        int fl = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLen, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX + segLen * 0.5f, ly, -halfZ))), qAxisX, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        int fr = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLen, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX - segLen * 0.5f, ly, -halfZ))), qAxisX, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        AddBondInternal(postIndices[0], fl, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, -halfZ))));
                        AddBondInternal(postIndices[1], fr, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, -halfZ))));
                    }
                    else
                    {
                        int fFull = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, spanX + 0.3f, logWallDiam, ToSysVec3(ToWorld(new Vector3(0f, ly, -halfZ))), qAxisX, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        AddBondInternal(postIndices[0], fFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, -halfZ))));
                        AddBondInternal(postIndices[1], fFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, -halfZ))));
                    }

                    // ARKA CEPHE (+Z)
                    if (story > 0 && isWindowRow)
                    {
                        float segLen = (spanX - 2.0f) * 0.5f;
                        int bl = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLen, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX + segLen * 0.5f, ly, halfZ))), qAxisX, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        int br = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLen, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX - segLen * 0.5f, ly, halfZ))), qAxisX, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        AddBondInternal(postIndices[3], bl, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, halfZ))));
                        AddBondInternal(postIndices[2], br, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, halfZ))));
                    }
                    else
                    {
                        int bFull = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, spanX + 0.3f, logWallDiam, ToSysVec3(ToWorld(new Vector3(0f, ly, halfZ))), qAxisX, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        AddBondInternal(postIndices[3], bFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, halfZ))));
                        AddBondInternal(postIndices[2], bFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, halfZ))));
                    }

                    // SOL (-X) ve SAG (+X) CEPHELER
                    if (isWindowRow)
                    {
                        float segLenZ = (spanZ - 1.8f) * 0.5f; // 2.3m on & arka
                        int l1 = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLenZ, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, -halfZ + segLenZ * 0.5f))), qAxisZ, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        int l2 = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLenZ, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, halfZ - segLenZ * 0.5f))), qAxisZ, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        int r1 = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLenZ, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, -halfZ + segLenZ * 0.5f))), qAxisZ, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        int r2 = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, segLenZ, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, halfZ - segLenZ * 0.5f))), qAxisZ, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        AddBondInternal(postIndices[0], l1, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, -halfZ))));
                        AddBondInternal(postIndices[3], l2, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, halfZ))));
                        AddBondInternal(postIndices[1], r1, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, -halfZ))));
                        AddBondInternal(postIndices[2], r2, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, halfZ))));
                    }
                    else
                    {
                        int lFull = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, spanZ + 0.3f, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, 0f))), qAxisZ, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        int rFull = AddCylinderPieceInternal(RigidTimberPieceKind.Log, TimberWoodSpecies.Oak, spanZ + 0.3f, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, 0f))), qAxisZ, islandId, MaterialIdCatalog.ChinkedLogBlock, sharedVoxelMat);
                        AddBondInternal(postIndices[0], lFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, -halfZ))));
                        AddBondInternal(postIndices[3], lFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(-halfX, ly, halfZ))));
                        AddBondInternal(postIndices[1], rFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, -halfZ))));
                        AddBondInternal(postIndices[2], rFull, TimberJoineryKind.SaddleNotch, logWallDiam, ToSysVec3(ToWorld(new Vector3(halfX, ly, halfZ))));
                    }
                }

                // Yurunebilir Ahsap Doseme Panelleri (WoodPlank)
                if (story == 0)
                {
                    int fDeck = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, new Vector3(spanX - 0.4f, 0.28f, spanZ - 0.4f), ToSysVec3(ToWorld(new Vector3(0f, floorY, 0f))), qYaw, islandId, MaterialIdCatalog.WoodPlank, sharedVoxelMat);
                    AddBondInternal(postIndices[0], fDeck, TimberJoineryKind.JoistPocket, 0.30f, ToSysVec3(ToWorld(new Vector3(-halfX, floorY, -halfZ))));
                    AddBondInternal(postIndices[2], fDeck, TimberJoineryKind.JoistPocket, 0.30f, ToSysVec3(ToWorld(new Vector3(halfX, floorY, halfZ))));
                }
                else
                {
                    // Ust katlarda merdiven boslugu (x: +1.65..+3.55, z: -1.85..+1.65) TAM ACIK,
                    // merdiven bitisinde arka ust sahanlik (z: +1.65..+3.00) ve on sahanlik (z: -3.00..-1.85) var!
                    int mainDeck = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, new Vector3(5.25f, 0.24f, spanZ - 0.4f), ToSysVec3(ToWorld(new Vector3(-0.98f, floorY, 0f))), qYaw, islandId, MaterialIdCatalog.WoodPlank, sharedVoxelMat);
                    int frontLand = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, new Vector3(1.90f, 0.24f, 1.15f), ToSysVec3(ToWorld(new Vector3(2.60f, floorY, -2.42f))), qYaw, islandId, MaterialIdCatalog.WoodPlank, sharedVoxelMat);
                    int rearLand = AddBoxPieceInternal(RigidTimberPieceKind.Plank, TimberWoodSpecies.Pine, new Vector3(1.90f, 0.24f, 1.35f), ToSysVec3(ToWorld(new Vector3(2.60f, floorY, +2.32f))), qYaw, islandId, MaterialIdCatalog.WoodPlank, sharedVoxelMat);
                    AddBondInternal(postIndices[0], mainDeck, TimberJoineryKind.JoistPocket, 0.30f, ToSysVec3(ToWorld(new Vector3(-halfX, floorY, -halfZ))));
                    AddBondInternal(postIndices[3], mainDeck, TimberJoineryKind.JoistPocket, 0.30f, ToSysVec3(ToWorld(new Vector3(-halfX, floorY, halfZ))));
                    AddBondInternal(postIndices[1], frontLand, TimberJoineryKind.JoistPocket, 0.30f, ToSysVec3(ToWorld(new Vector3(halfX, floorY, -halfZ))));
                    AddBondInternal(postIndices[2], rearLand, TimberJoineryKind.JoistPocket, 0.30f, ToSysVec3(ToWorld(new Vector3(halfX, floorY, halfZ))));
                }

                // Katlar arasi yurunebilir ahsap merdiven basamaklari (1->2 ve 2->3: 10 adet 0.30m basamak -> arka sahanliga cikar!)
                if (story < 2)
                {
                    const int stepCount = 10;
                    for (int s = 0; s < stepCount; s++)
                    {
                        float stepY = story * storyH + 0.42f + s * (storyH / stepCount);
                        float stepZ = -1.60f + s * 0.33f;
                        int stepPiece = AddBoxPieceInternal(
                            RigidTimberPieceKind.Plank,
                            TimberWoodSpecies.Oak,
                            new Vector3(1.70f, 0.22f, 0.40f),
                            ToSysVec3(ToWorld(new Vector3(2.60f, stepY, stepZ))),
                            qYaw,
                            islandId,
                            MaterialIdCatalog.TreatedTimberBeam,
                            sharedVoxelMat);
                        AddBondInternal(postIndices[1], stepPiece, TimberJoineryKind.Pegged, 0.24f, ToSysVec3(ToWorld(new Vector3(halfX, stepY, stepZ))));
                    }
                }

                if (story == 2)
                {
                    topWallBeams[0] = bFront;
                    topWallBeams[1] = bBack;
                    topWallBeams[2] = bLeft;
                    topWallBeams[3] = bRight;
                }
            }

            // 3) 3. Kat Cati: Gergi Kirisleri + Kalin Mahya + Mertekler + Tam Kapali Egimli Ahsap Cati Panelleri
            float eaveY = 3.0f * storyH;
            float ridgeRise = 2.65f;
            float ridgeY = eaveY + ridgeRise;
            float roofOverhangX = 0.65f;
            float slopeSpanX = halfX + roofOverhangX;
            float rafterLen = Mathf.Sqrt(slopeSpanX * slopeSpanX + ridgeRise * ridgeRise);
            float pitchDeg = Mathf.Atan2(ridgeRise, halfX) * Mathf.Rad2Deg;

            // 2 Gergi kirisi (TieBeam)
            float[] tieZs = { -1.3f, 1.3f };
            for (int t = 0; t < tieZs.Length; t++)
            {
                int tieBeam = AddCylinderPieceInternal(RigidTimberPieceKind.TieBeam, TimberWoodSpecies.Oak, spanX, 0.38f, ToSysVec3(ToWorld(new Vector3(0f, eaveY + 0.10f, tieZs[t]))), qAxisX, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);
                AddBondInternal(topWallBeams[2], tieBeam, TimberJoineryKind.Dovetail, 0.38f, ToSysVec3(ToWorld(new Vector3(-halfX, eaveY + 0.10f, tieZs[t]))));
                AddBondInternal(topWallBeams[3], tieBeam, TimberJoineryKind.Dovetail, 0.38f, ToSysVec3(ToWorld(new Vector3(halfX, eaveY + 0.10f, tieZs[t]))));
            }

            // Mahya (RidgeBeam)
            int ridgeBeam = AddCylinderPieceInternal(RigidTimberPieceKind.RidgeBeam, TimberWoodSpecies.Oak, spanZ + 1.2f, 0.42f, ToSysVec3(ToWorld(new Vector3(0f, ridgeY, 0f))), qAxisZ, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);

            // 4 cift cati mertegi
            float[] rafterZs = { -halfZ, -1.1f, 1.1f, halfZ };
            for (int r = 0; r < rafterZs.Length; r++)
            {
                float rz = rafterZs[r];
                SysQuat qLeftRafter = ToSysQuat(rotY * Quaternion.Euler(0f, 0f, -(90f - pitchDeg)));
                SysQuat qRightRafter = ToSysQuat(rotY * Quaternion.Euler(0f, 0f, +(90f - pitchDeg)));

                int rLeft = AddCylinderPieceInternal(RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, rafterLen, 0.30f, ToSysVec3(ToWorld(new Vector3(-halfX * 0.5f, (eaveY + ridgeY) * 0.5f, rz))), qLeftRafter, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);
                int rRight = AddCylinderPieceInternal(RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, rafterLen, 0.30f, ToSysVec3(ToWorld(new Vector3(halfX * 0.5f, (eaveY + ridgeY) * 0.5f, rz))), qRightRafter, islandId, MaterialIdCatalog.TreatedTimberBeam, sharedVoxelMat);

                AddBondInternal(topWallBeams[2], rLeft, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(-halfX, eaveY, rz))));
                AddBondInternal(topWallBeams[3], rRight, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(halfX, eaveY, rz))));
                AddBondInternal(ridgeBeam, rLeft, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(0f, ridgeY, rz))));
                AddBondInternal(ridgeBeam, rRight, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(0f, ridgeY, rz))));
            }

            // Sol & Sag Tam Kapali Egimli Ahsap Zona Cati Panelleri (TimberShingleRoof)
            SysQuat qRoofLeft = ToSysQuat(rotY * Quaternion.Euler(0f, 0f, +pitchDeg));
            SysQuat qRoofRight = ToSysQuat(rotY * Quaternion.Euler(0f, 0f, -pitchDeg));
            int roofL = AddBoxPieceInternal(RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, new Vector3(rafterLen + 0.35f, 0.22f, spanZ + 1.3f), ToSysVec3(ToWorld(new Vector3(-halfX * 0.5f - 0.15f, (eaveY + ridgeY) * 0.5f + 0.22f, 0f))), qRoofLeft, islandId, MaterialIdCatalog.TimberShingleRoof, sharedVoxelMat);
            int roofR = AddBoxPieceInternal(RigidTimberPieceKind.Rafter, TimberWoodSpecies.Pine, new Vector3(rafterLen + 0.35f, 0.22f, spanZ + 1.3f), ToSysVec3(ToWorld(new Vector3(halfX * 0.5f + 0.15f, (eaveY + ridgeY) * 0.5f + 0.22f, 0f))), qRoofRight, islandId, MaterialIdCatalog.TimberShingleRoof, sharedVoxelMat);
            AddBondInternal(topWallBeams[2], roofL, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(-halfX, eaveY, 0f))));
            AddBondInternal(topWallBeams[3], roofR, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(halfX, eaveY, 0f))));
            AddBondInternal(ridgeBeam, roofL, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(0f, ridgeY, 0f))));
            AddBondInternal(ridgeBeam, roofR, TimberJoineryKind.Pegged, 0.30f, ToSysVec3(ToWorld(new Vector3(0f, ridgeY, 0f))));

            _needsStaticReeval = true;
            ReevaluateStaticEquilibrium(worldChunks);

            feedback = "🏠 Gerçek 3 Katlı Tek-Parça Ahşap Ev (7.6×6.4m, kalın kütük duvarlı, döşemeli, merdivenli ve çatılı) kuruldu! İçine yürüyüp 3. kata çıkabilir, Balta [V] Kip 4 ile parça kesebilir veya [K] ile sökebilirsiniz!";
            return true;
        }

        /// <summary>
        /// Oyuncu kapsulunu tum aktif tek-parca kutuk, duvar, doseme ve merdivenlerle carpisma/yurume icin cozer (0 B GC).
        /// - Duvar/sutunlarin icinden gecilmez.
        /// - Dosemelerin ve merdiven basamaklarinin ustunde yurunur (otomatik basamak cikma dahil).
        /// </summary>
        public void ResolvePlayerCollision(
            Transform playerTransform,
            float playerRadius,
            float playerHeight,
            ref float verticalVelocity,
            ref byte isGroundedFlag)
        {
            if (_pieceCount == 0) return;

            Vector3 pPos = playerTransform.position;
            bool modified = false;

            // 3 iterasyonlu siki OBB cozumu: oyuncu zorlasa bile duvar/sutun icinden gecemez!
            for (int iter = 0; iter < 3; iter++)
            {
                bool anyThisIter = false;
                for (int i = 0; i < _pieceCount; i++)
                {
                    if (_pieceRemoved[i] != 0) continue;
                    Vector3 center = ToUnityVec3(_pieces[i].Position);
                    Vector3 half = _pieceHalfExtents[i];

                    float maxR = half.magnitude + playerRadius + playerHeight;
                    Vector3 delta = pPos - center;
                    if (delta.sqrMagnitude > maxR * maxR) continue;

                    Quaternion rot = ToUnityQuat(_pieces[i].Rotation);
                    Quaternion invRot = Quaternion.Inverse(rot);

                    Vector3 feetLocal = invRot * (pPos - center);
                    Vector3 lowBodyLocal = invRot * (pPos + new Vector3(0f, 0.45f, 0f) - center);
                    Vector3 highBodyLocal = invRot * (pPos + new Vector3(0f, playerHeight - 0.25f, 0f) - center);

                    // 1) Ust yuzeyde yurume & merdiven basamagi cikma (Step-Up <= 0.48m)
                    bool insideXZ = Mathf.Abs(feetLocal.x) <= half.x + playerRadius * 0.72f &&
                                    Mathf.Abs(feetLocal.z) <= half.z + playerRadius * 0.72f;
                    if (insideXZ)
                    {
                        float topYLocal = half.y;
                        float distFromTop = feetLocal.y - topYLocal;
                        if (distFromTop >= -0.48f && distFromTop <= 0.16f && verticalVelocity <= 0.75f)
                        {
                            Vector3 worldUp = rot * Vector3.up;
                            if (worldUp.y >= 0.72f)
                            {
                                Vector3 snappedLocal = new Vector3(feetLocal.x, topYLocal + 0.01f, feetLocal.z);
                                Vector3 snappedWorld = center + (rot * snappedLocal);
                                if (snappedWorld.y >= pPos.y - 0.16f)
                                {
                                    pPos.y = snappedWorld.y;
                                    verticalVelocity = 0f;
                                    isGroundedFlag = 1;
                                    modified = true;
                                    anyThisIter = true;
                                    continue;
                                }
                            }
                        }
                    }

                    // 2) Yan duvar / sutun kati carpismasi (merdiven basamaklarinda 1-2 basamak ilerideki basamak burnunun dizi geri itmesini engelle!)
                    if (_pieces[i].Kind == RigidTimberPieceKind.Plank && (center.y + half.y - pPos.y) <= 0.68f && (center.y + half.y - pPos.y) >= -0.15f)
                    {
                        continue;
                    }

                    float ex = half.x + playerRadius;
                    float ez = half.z + playerRadius;
                    float ey = half.y + 0.38f;

                    bool hitLow = Mathf.Abs(lowBodyLocal.x) < ex && Mathf.Abs(lowBodyLocal.y) < ey && Mathf.Abs(lowBodyLocal.z) < ez;
                    bool hitHigh = Mathf.Abs(highBodyLocal.x) < ex && Mathf.Abs(highBodyLocal.y) < ey && Mathf.Abs(highBodyLocal.z) < ez;
                    if (hitLow || hitHigh)
                    {
                        Vector3 testPt = hitLow ? lowBodyLocal : highBodyLocal;
                        if (feetLocal.y >= half.y - 0.12f) continue;

                        float penX = ex - Mathf.Abs(testPt.x);
                        float penZ = ez - Mathf.Abs(testPt.z);
                        Vector3 pushLocal = penX < penZ
                            ? new Vector3(testPt.x >= 0f ? penX : -penX, 0f, 0f)
                            : new Vector3(0f, 0f, testPt.z >= 0f ? penZ : -penZ);

                        Vector3 pushWorld = rot * pushLocal;
                        pPos.x += pushWorld.x;
                        pPos.z += pushWorld.z;
                        modified = true;
                        anyThisIter = true;
                    }
                }
                if (!anyThisIter) break;
            }

            if (modified)
            {
                playerTransform.position = pPos;
            }
        }

        /// <summary>
        /// [Alt+L] Oyuncunun baktigi tek-parca sutun/kiris/duvar kutugunu keser ve yapisal dengeyi yeniden cozer.
        /// </summary>
        public bool TryCutAimedPiece(
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            feedback = string.Empty;
            if (_pieceCount == 0) return false;

            int bestIdx = -1;
            float bestDist = maxDist;
            Vector3 dirNorm = rayDir.normalized;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0) continue;
                Vector3 p = ToUnityVec3(_pieces[i].Position);
                Vector3 toP = p - rayOrigin;
                float proj = Vector3.Dot(toP, dirNorm);
                if (proj < 0.2f || proj > bestDist) continue;
                Vector3 closest = rayOrigin + dirNorm * proj;
                float perp = Vector3.Distance(closest, p);
                float hitRadius = Mathf.Max(0.55f, _pieceHalfExtents[i].magnitude * 0.55f);
                if (perp <= hitRadius)
                {
                    bestDist = proj;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                feedback = "🪓 Kesilecek tek-parça yapı elemanı nişangâhta değil (bir sütun veya kirişe bakıp [Alt+L] basın).";
                return false;
            }

            uint removedId = _pieces[bestIdx].PieceId;
            RigidTimberPieceKind kind = _pieces[bestIdx].Kind;
            _pieceRemoved[bestIdx] = 1;
            _pieces[bestIdx].State = RigidTimberState.StaticLooseEntity;
            if (_pieceVisuals[bestIdx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[bestIdx]);
                _pieceVisuals[bestIdx] = null;
            }

            for (int b = 0; b < _bondCount; b++)
            {
                if (_bonds[b].PieceA == removedId || _bonds[b].PieceB == removedId)
                {
                    _bonds[b].IsActive = 0;
                }
            }

            _needsStaticReeval = true;
            ReevaluateStaticEquilibrium(worldChunks);

            feedback = $"🪓 Tek-parça {kind} (#{removedId}) kesildi! Kalan yapı gerçek zamanlı statik/XPBD analizinden geçirildi.";
            return true;
        }

        /// <summary>
        /// [E] Yerde yatan yikinti/enkaz parcasini (StaticLooseEntity) sokup odun/kalas olarak toplar.
        /// </summary>
        public bool TrySalvageAimedDebris(
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;
            if (_pieceCount == 0) return false;

            int bestIdx = -1;
            float bestDist = maxDist;
            Vector3 dirNorm = rayDir.normalized;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticLooseEntity) continue;
                if (TryRaycastPieceObb(i, rayOrigin, dirNorm, bestDist, out float hitDist))
                {
                    bestDist = hitDist;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            RigidStructurePiece piece = _pieces[bestIdx];
            // Gercekci agirlik/boyut kurali: Koca kutuk/sutun/doseme [E] ile cebe GIRMEZ!
            // Yalnizca kucuk/hafif parcalar (<= 2.2m ve <= 55 kg) elle toplanabilir; buyukler baltayla parcalanir veya arabayla tasinir!
            bool isTooBigForHand = piece.LengthM > 2.2f || piece.MassKg > 55f ||
                                   (_pieceHalfExtents[bestIdx].x * 2f > 2.2f) ||
                                   (_pieceHalfExtents[bestIdx].z * 2f > 2.2f);
            if (isTooBigForHand)
            {
                feedback = $"⚠️ Bu parça elle cebe sığmaz ({piece.LengthM:F1}m, {piece.MassKg:0} kg)! Baltayla/kazmayla vurup parçalayın veya Kütük Arabasıyla ([K]) taşıyın!";
                return true; // Etkilesim tuketildi ama esya verilmedi (salvagedCount = 0)
            }

            salvagedMaterialId = piece.Kind == RigidTimberPieceKind.Plank ? MaterialIdCatalog.WoodPlank : MaterialIdCatalog.OakWood;
            salvagedCount = 1;

            _pieceRemoved[bestIdx] = 1;
            if (_pieceVisuals[bestIdx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[bestIdx]);
                _pieceVisuals[bestIdx] = null;
            }

            string itemName = salvagedMaterialId == MaterialIdCatalog.WoodPlank ? "Ahşap Kalas" : "Meşe Odunu";
            feedback = $"🪵 Küçük parça elle toplandı (+1 {itemName}).";
            return true;
        }

        /// <summary>
        /// Balta / Kazma ile Sol Tik vuruldugunda yerde yatan agir yikinti parcasini 2'ye boler (kuculunce toplanabilir olur).
        /// </summary>
        public bool TryChopAimedDebrisWithTool(
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;
            if (_pieceCount == 0) return false;

            int bestIdx = -1;
            float bestDist = maxDist;
            Vector3 dirNorm = rayDir.normalized;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State == RigidTimberState.StaticAnchored) continue;
                if (TryRaycastPieceObb(i, rayOrigin, dirNorm, bestDist, out float hitDist))
                {
                    bestDist = hitDist;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) return false;

            // [E] ile yerdeki dusmus/serbest kutugu dogrudan Yarma Odun / Kalas olarak geri al!
            RigidStructurePiece piece = _pieces[bestIdx];
            salvagedMaterialId = piece.Kind == RigidTimberPieceKind.Plank ? MaterialIdCatalog.WoodPlank : MaterialIdCatalog.OakWood;
            salvagedCount = Mathf.Clamp(Mathf.RoundToInt(piece.LengthM), 1, 5);
            _pieceRemoved[bestIdx] = 1;
            if (_pieceVisuals[bestIdx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[bestIdx]);
                _pieceVisuals[bestIdx] = null;
            }
            string itemName = salvagedMaterialId == MaterialIdCatalog.WoodPlank ? "Ahşap Kalas" : "Yarma Odun";
            feedback = $"🪵 Yerdeki kütük ({piece.LengthM:F1}m) geri alındı (+{salvagedCount}x {itemName}).";
            return true;
        }

        /// <summary>
        /// Gercek 240 Hz fiziksel balta/kazma savurmasi sirasinda bicak ucunun (bladeMid..bladeTip)
        /// HEM yerdeki yikinti parcalarina HEM DE ayaktaki yapi elemanlarina (kertik acma veya 3 darbede kesme!) temasini cozer.
        /// </summary>
        public bool TryPhysicalBladeSwingHitDebris(
            Vector3 bladeMidWorld,
            Vector3 bladeTipWorld,
            float bladeHitRadius,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;
            if (_pieceCount == 0) return false;

            int bestIdx = -1;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0) continue;
                Vector3 center = ToUnityVec3(_pieces[i].Position);
                Quaternion invRot = Quaternion.Inverse(ToUnityQuat(_pieces[i].Rotation));
                Vector3 half = _pieceHalfExtents[i] + new Vector3(bladeHitRadius, bladeHitRadius, bladeHitRadius);

                Vector3 tipL = invRot * (bladeTipWorld - center);
                Vector3 midL = invRot * (bladeMidWorld - center);
                bool hitTip = Mathf.Abs(tipL.x) <= half.x && Mathf.Abs(tipL.y) <= half.y && Mathf.Abs(tipL.z) <= half.z;
                bool hitMid = Mathf.Abs(midL.x) <= half.x && Mathf.Abs(midL.y) <= half.y && Mathf.Abs(midL.z) <= half.z;
                if (hitTip || hitMid)
                {
                    float dSq = (bladeTipWorld - center).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestIdx = i;
                    }
                }
            }

            if (bestIdx < 0) return false;

            // DIKKAT (KULLANICI GERI BILDIRIMI DUZELTMESI):
            // 240 Hz fiziksel balta bicagi savurmasi ASLA dikili ana direkleri, duvarlari veya catilari (StaticAnchored)
            // yanlislikla kesip ikiye bolmez! Ayaktaki yapi marangozlugu ve budamasi YALNIZCA secili Balta Kipi ([V]) uzerinden yapilir.
            if (_pieces[bestIdx].State == RigidTimberState.StaticAnchored)
            {
                return false;
            }

            // YERDE YATAN SERBEST / YIKINTI PARCASINA FIZIKSEL BALTA DARBESI!
            return TrySplitPieceAtWorldPoint(bestIdx, bladeTipWorld, worldChunks, out salvagedMaterialId, out salvagedCount, out feedback);
        }

        /// <summary>
        /// Kütüğü 50/50 değil TAM BALTANIN VURDUĞU NOKTADAN (`hitWorldPos`) ikiye keser!
        /// - Kesilen üst/desteksiz parça ANINDA DynamicActive olup yerçekimiyle yere düşer (asla havada kalmaz!).
        /// - Parça çok küçükse (&lt;= 1.25m) veya ikiye ayrılamayacak uçtaysa doğrudan Yarma Odun itemi olarak düşer!
        /// </summary>
        private bool TrySplitPieceAtWorldPoint(
            int idx,
            Vector3 hitWorldPos,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;
            if (idx < 0 || idx >= _pieceCount || _pieceRemoved[idx] != 0) return false;

            RigidStructurePiece orig = _pieces[idx];
            byte matId = _pieceMaterialIds[idx] != 0 ? _pieceMaterialIds[idx] : MaterialIdCatalog.OakWood;
            Vector3 halfExt = _pieceHalfExtents[idx];
            bool isBox = halfExt.x > orig.DiameterM * 0.65f || halfExt.z > orig.DiameterM * 0.65f;
            Quaternion uRot = ToUnityQuat(orig.Rotation);
            bool splitAlongX = isBox && halfExt.x >= halfExt.z;
            Vector3 splitAxis = isBox ? (uRot * (splitAlongX ? Vector3.right : Vector3.forward)) : (uRot * Vector3.up);
            float totalLen = isBox ? ((splitAlongX ? halfExt.x : halfExt.z) * 2f) : orig.LengthM;
            float halfL = totalLen * 0.5f;

            Vector3 origCenter = ToUnityVec3(orig.Position);
            float tAlong = Vector3.Dot(hitWorldPos - origCenter, splitAxis);
            // Uca çok yakın vurulduysa güvenli marjla böl ya da küçükse doğrudan odun itemine çevir
            tAlong = Mathf.Clamp(tAlong, -halfL + 0.15f, halfL - 0.15f);
            float lenA = halfL + tAlong; // Alt / Sol parça
            float lenB = halfL - tAlong; // Üst / Sağ parça

            // Çok küçükse (<= 1.25m) veya ikiye bölünemeyecek kadar kısa kalacaksa -> doğrudan odun itemi olarak düş!
            if (totalLen <= 1.25f || lenA < 0.45f || lenB < 0.45f || _pieceCount >= MaxActivePiecesCap + 38)
            {
                salvagedMaterialId = orig.Kind == RigidTimberPieceKind.Plank ? MaterialIdCatalog.WoodPlank : MaterialIdCatalog.OakWood;
                salvagedCount = Mathf.Clamp(Mathf.RoundToInt(totalLen * 1.5f), 1, 3);
                _pieceRemoved[idx] = 1;
                if (_pieceVisuals[idx] != null)
                {
                    UnityEngine.Object.Destroy(_pieceVisuals[idx]);
                    _pieceVisuals[idx] = null;
                }
                string itemName = salvagedMaterialId == MaterialIdCatalog.WoodPlank ? "Ahşap Kalas" : "Yarma Odun";
                feedback = $"🪓 Kısa parça ({totalLen:F1}m) baltayla oduna ayrıldı (+{salvagedCount}x {itemName})!";
                return true;
            }

            // Orijinal parçayı kaldır ve TAM VURULAN NOKTADAN iki yeni parça (lenA + lenB) üret!
            _pieceRemoved[idx] = 1;
            if (_pieceVisuals[idx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[idx]);
                _pieceVisuals[idx] = null;
            }

            // 0.10m temiz balta kesim boslugu (kerf) birak ki XPBD ilk karede ust parcayi firlatmasin!
            float kerfHalf = 0.05f;
            float spawnLenA = Mathf.Max(0.40f, lenA - kerfHalf);
            float spawnLenB = Mathf.Max(0.40f, lenB - kerfHalf);
            Vector3 posA = origCenter + splitAxis * (-halfL + spawnLenA * 0.5f);
            Vector3 posB = origCenter + splitAxis * (halfL - spawnLenB * 0.5f + 0.06f);

            int idxA, idxB;
            if (isBox)
            {
                Vector3 sizeA = splitAlongX ? new Vector3(spawnLenA, halfExt.y * 2f, halfExt.z * 2f) : new Vector3(halfExt.x * 2f, halfExt.y * 2f, spawnLenA);
                Vector3 sizeB = splitAlongX ? new Vector3(spawnLenB, halfExt.y * 2f, halfExt.z * 2f) : new Vector3(halfExt.x * 2f, halfExt.y * 2f, spawnLenB);
                idxA = AddBoxPieceInternal(orig.Kind, orig.Species, sizeA, ToSysVec3(posA), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
                idxB = AddBoxPieceInternal(orig.Kind, orig.Species, sizeB, ToSysVec3(posB), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
            }
            else
            {
                idxA = AddCylinderPieceInternal(orig.Kind, orig.Species, spawnLenA, orig.DiameterM, ToSysVec3(posA), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
                idxB = AddCylinderPieceInternal(orig.Kind, orig.Species, spawnLenB, orig.DiameterM, ToSysVec3(posB), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
            }

            _pieces[idxA].SplitParentId = orig.PieceId;
            _pieces[idxB].SplitParentId = orig.PieceId;

            // Alt parça (A) gerçekten çukurda toprağa gömülüyse dik kalabilir; değilse o da yere düşer!
            if (worldChunks != null) _soilSampler.WorldChunks = worldChunks;
            _soilProfiles[idxA] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[idxA], _soilSampler);
            bool aEmbedded = orig.Kind == RigidTimberPieceKind.Post && _soilProfiles[idxA].EmbedDepthM >= 0.35f;
            if (!aEmbedded)
            {
                _pieces[idxA].State = RigidTimberState.DynamicActive;
                _pieces[idxA].LinearVelocity = ToSysVec3(-splitAxis * 0.35f + Vector3.up * 0.15f);
            }

            // Üst/kesilen parça (B) ASLA havada kalamaz -> anında DynamicActive uyanıp yerçekimiyle yere devrilir!
            _pieces[idxB].State = RigidTimberState.DynamicActive;
            _pieces[idxB].LinearVelocity = ToSysVec3(splitAxis * 0.45f + Vector3.right * 0.35f + Vector3.up * 0.25f);
            _pieces[idxB].AngularVelocity = new SysVec3(0.55f, 0.15f, -0.45f);
            _hasDynamicPieces = true;

            feedback = $"🪓 Kütük tam vurduğun yerden ikiye kesildi ({lenA:F1}m + {lenB:F1}m)! Kesilen parça yere düşüyor.";
            return true;
        }

        /// <summary>
        /// [Ctrl+Shift+L] Sahnedeki tum yikilmis/daginik enkaz parcalarini (StaticLooseEntity) temizler.
        /// </summary>
        public int ClearAllLooseDebris(out string feedback)
        {
            int cleared = 0;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0) continue;
                if (_pieces[i].State == RigidTimberState.StaticLooseEntity || _pieces[i].State == RigidTimberState.DynamicActive)
                {
                    _pieceRemoved[i] = 1;
                    if (_pieceVisuals[i] != null)
                    {
                        UnityEngine.Object.Destroy(_pieceVisuals[i]);
                        _pieceVisuals[i] = null;
                    }
                    cleared++;
                }
            }
            _hasDynamicPieces = false;
            feedback = cleared > 0
                ? $"🧹 {cleared} adet yıkıntı/enkaz parçası temizlendi!"
                : "🧹 Temizlenecek yıkıntı parçası yok.";
            return cleared;
        }

        public void OnTerrainVoxelChanged(int wx, int wy, int wz, Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (_pieceCount == 0) return;
            _soilSampler.WorldChunks = worldChunks;
            bool anyAffected = false;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].Kind != RigidTimberPieceKind.Post) continue;
                float dx = _pieces[i].Position.X - (wx + 0.5f);
                float dz = _pieces[i].Position.Z - (wz + 0.5f);
                if (dx * dx + dz * dz <= 9.0f) // 3.0m yaricap
                {
                    _soilProfiles[i] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[i], _soilSampler);
                    anyAffected = true;
                }
            }

            if (anyAffected)
            {
                _needsStaticReeval = true;
                ReevaluateStaticEquilibrium(worldChunks);
            }
        }

        public void Tick(float dt, Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            if (_pieceCount == 0) return;
            _simTick++;

            // 0.36 sn Derin-Sig Oluk (Swing-In) gorsel yerlesim animasyonu (0 B GC)
            for (int a = 0; a < _swingInAnims.Length; a++)
            {
                if (!_swingInAnims[a].Active) continue;
                ref SwingInVisualAnim anim = ref _swingInAnims[a];
                int pIdx = anim.PieceIndex;
                if ((uint)pIdx >= (uint)_pieceCount || _pieceRemoved[pIdx] != 0 || _pieceVisuals[pIdx] == null || _pieces[pIdx].State != RigidTimberState.StaticAnchored)
                {
                    anim.Active = false;
                    continue;
                }
                anim.Elapsed += dt;
                float u = Mathf.Clamp01(anim.Elapsed / Mathf.Max(0.05f, anim.Duration));
                float smooth = 1f - (1f - u) * (1f - u);
                Transform vTr = _pieceVisuals[pIdx]!.transform;
                vTr.position = Vector3.Lerp(anim.StartPos, anim.EndPos, smooth);
                vTr.rotation = Quaternion.Slerp(anim.StartRot, anim.EndRot, smooth);
                if (u >= 1.0f)
                {
                    vTr.position = anim.EndPos;
                    vTr.rotation = anim.EndRot;
                    anim.Active = false;
                }
            }

            if (_needsStaticReeval)
            {
                _revalCooldown -= dt;
                if (_revalCooldown <= 0f)
                {
                    ReevaluateStaticEquilibrium(worldChunks);
                    _revalCooldown = 0.08f;
                }
            }

            // 4 Hz Hafif Zemin Bekcisi (Ground Watchdog): Domino cokusu, patlama veya baska bir sistem
            // bir sutunun altindaki topragi yok ettiginde en gec 0.25 sn icinde algilayip evi yikar (ASLA havada kalmaz!)
            _groundWatchdogTimer += dt;
            if (_groundWatchdogTimer >= 0.25f && worldChunks != null)
            {
                _groundWatchdogTimer = 0f;
                _soilSampler.WorldChunks = worldChunks;
                bool anyPostLostSoil = false;
                for (int i = 0; i < _pieceCount; i++)
                {
                    if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                    if (_pieces[i].Kind != RigidTimberPieceKind.Post)
                    {
                        if ((_pieces[i].Flags & 2) != 0)
                        {
                            float botY = _pieces[i].Position.Y - _pieceHalfExtents[i].y;
                            float gY = _soilSampler.SampleSurfaceHeightY(_pieces[i].Position.X, _pieces[i].Position.Z, _pieces[i].Position.Y + 0.5f);
                            if (gY < botY - 0.35f)
                            {
                                _pieces[i].Flags &= unchecked((byte)~2);
                                anyPostLostSoil = true;
                            }
                        }
                        continue;
                    }
                    float prevD = _soilProfiles[i].EmbedDepthM;
                    SoilEmbedProfile fresh = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[i], _soilSampler, compactionFactor: 0.92f, hasStoneCollar: true);
                    if ((_pieces[i].Flags & 2) != 0)
                    {
                        // Oyuncu etrafina toprak doldurduysa kalici gomulu moda yukselt; henuz doldurmadiysa cukur tabani durdugu surece dik tut!
                        if (fresh.EmbedDepthM >= 0.45f)
                        {
                            _pieces[i].Flags &= unchecked((byte)~2);
                            _soilProfiles[i] = fresh;
                            anyPostLostSoil = true;
                        }
                        else
                        {
                            float footY = _pieces[i].Position.Y - _pieces[i].LengthM * 0.5f;
                            TipInEvaluation holeCheck = TimberTipInAndJoineryLogic.EvaluateTipInHole(_pieces[i].Position.X, _pieces[i].Position.Z, footY + 0.5f, _soilSampler);
                            if (holeCheck.Result == TipInPlacementResult.NoHoleFlatGroundTipsOver)
                            {
                                _pieces[i].Flags &= unchecked((byte)~2);
                                _soilProfiles[i] = fresh;
                                anyPostLostSoil = true;
                            }
                        }
                        continue;
                    }
                    if (MathF.Abs(fresh.EmbedDepthM - prevD) >= 0.20f)
                    {
                        _soilProfiles[i] = fresh;
                        anyPostLostSoil = true;
                    }
                }
                if (anyPostLostSoil)
                {
                    ReevaluateStaticEquilibrium(worldChunks);
                }
            }

            if (!_hasDynamicPieces) return; // 0.00 ms uyku durumu!

            _soilSampler.WorldChunks = worldChunks;
            for (int i = 0; i < _pieceCount; i++)
            {
                _prevVerticalVel[i] = _pieces[i].LinearVelocity.Y;
            }

            int activeDyn = _xpbdSolver.StepActiveDynamics(
                _pieces.AsSpan(0, _pieceCount),
                _bonds.AsSpan(0, _bondCount),
                _soilSampler,
                Mathf.Clamp(dt, 0.001f, 0.033f),
                _simTick,
                RigidTimberPhysicsTraceRecorder.Shared);

            // Yukaridan dusen parcalarin kinetik carpma hasari + alttaki parcalara zincirleme enerji aktarimi + 2'ye kirilma!
            // Donma korumasi: kare basina maks 4 bolunme ve toplam aktif parca tavani (MaxActivePiecesCap)
            Span<int> fracturedScratch = stackalloc int[8];
            int countBeforeSplit = _pieceCount;
            int splitsThisFrame = 0;
            for (int i = 0; i < countBeforeSplit && splitsThisFrame < 4; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.DynamicActive) continue;
                float prevVy = _prevVerticalVel[i];
                float curVy = _pieces[i].LinearVelocity.Y;
                float impactDecel = MathF.Max(0f, (-prevVy) - MathF.Max(0f, -curVy));
                if (impactDecel >= TimberImpactFractureSystem.MinDamagingImpactSpeedMps)
                {
                    int fracCount = TimberImpactFractureSystem.EvaluateImpactAndCascadeEnergy(
                        i, impactDecel, _pieces.AsSpan(0, countBeforeSplit), fracturedScratch);
                    for (int f = 0; f < fracCount && splitsThisFrame < 4; f++)
                    {
                        if (TrySplitFracturedPieceInHalf(fracturedScratch[f]))
                        {
                            splitsThisFrame++;
                        }
                    }
                }
            }

            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieceVisuals[i] == null) continue;
                Transform tr = _pieceVisuals[i]!.transform;
                tr.position = ToUnityVec3(_pieces[i].Position);
                tr.rotation = ToUnityQuat(_pieces[i].Rotation);
            }

            if (activeDyn == 0)
            {
                _hasDynamicPieces = false;
                RigidTimberPhysicsTraceRecorder.Shared.FlushToJsonl(System.IO.Path.Combine(Application.dataPath, "..", "Logs"));
            }
        }

        private void ReevaluateStaticEquilibrium(Dictionary<(int cx, int cz), uint[]>? worldChunks)
        {
            _needsStaticReeval = false;
            if (_pieceCount == 0) return;

            _soilSampler.WorldChunks = worldChunks;
            int totalWoken = 0;
            for (int isl = 1; isl < _nextIslandId; isl++)
            {
                totalWoken += _staticSolver.EvaluateDirtyIsland(
                    isl,
                    _pieces.AsSpan(0, _pieceCount),
                    _soilProfiles.AsSpan(0, _pieceCount),
                    _bonds.AsSpan(0, _bondCount),
                    SysVec3.Zero,
                    _simTick,
                    RigidTimberPhysicsTraceRecorder.Shared);
            }

            if (totalWoken > 0)
            {
                _hasDynamicPieces = true;
                RigidTimberPhysicsTraceRecorder.Shared.FlushToJsonl(System.IO.Path.Combine(Application.dataPath, "..", "Logs"));
            }
        }

        private int AddCylinderPieceInternal(
            RigidTimberPieceKind kind,
            TimberWoodSpecies species,
            float lengthM,
            float diameterM,
            SysVec3 pos,
            SysQuat rot,
            int islandId,
            byte materialId,
            Material? sharedVoxelMat)
        {
            EnsurePieceCapacity(_pieceCount + 1);
            int idx = _pieceCount++;
            uint id = _nextPieceId++;

            RigidStructurePiece piece = RigidStructurePiece.Create(id, kind, species, lengthM, diameterM, pos, rot);
            piece.IslandId = islandId;
            _pieces[idx] = piece;
            _soilProfiles[idx] = default;
            _carpentryProfiles[idx] = default;
            _pieceRemoved[idx] = 0;
            _pieceMaterialIds[idx] = materialId;
            if (sharedVoxelMat != null) _lastSharedVoxelMat = sharedVoxelMat;
            float r = diameterM * 0.5f;
            _pieceHalfExtents[idx] = new Vector3(r, lengthM * 0.5f, r);
            _pieceVisuals[idx] = BuildCylinderVisual(in piece, materialId, sharedVoxelMat);
            return idx;
        }

        private int AddBoxPieceInternal(
            RigidTimberPieceKind kind,
            TimberWoodSpecies species,
            Vector3 fullSize,
            SysVec3 pos,
            SysQuat rot,
            int islandId,
            byte materialId,
            Material? sharedVoxelMat)
        {
            EnsurePieceCapacity(_pieceCount + 1);
            int idx = _pieceCount++;
            uint id = _nextPieceId++;

            float eqLen = Mathf.Max(fullSize.x, fullSize.z);
            float eqDiam = Mathf.Max(0.20f, fullSize.y);
            RigidStructurePiece piece = RigidStructurePiece.Create(id, kind, species, eqLen, eqDiam, pos, rot);
            piece.IslandId = islandId;
            piece.HalfExtents = new SysVec3(fullSize.x * 0.5f, fullSize.y * 0.5f, fullSize.z * 0.5f);
            _pieces[idx] = piece;
            _soilProfiles[idx] = default;
            _carpentryProfiles[idx] = default;
            _pieceRemoved[idx] = 0;
            _pieceMaterialIds[idx] = materialId;
            if (sharedVoxelMat != null) _lastSharedVoxelMat = sharedVoxelMat;
            _pieceHalfExtents[idx] = fullSize * 0.5f;
            _pieceVisuals[idx] = BuildBoxVisual(id, kind, pos, rot, fullSize * 0.5f, materialId, sharedVoxelMat);
            return idx;
        }

        private bool TrySplitFracturedPieceInHalf(int idx)
        {
            if (idx < 0 || idx >= _pieceCount || _pieceRemoved[idx] != 0) return false;
            if (_pieceCount >= MaxActivePiecesCap) return false;
            RigidStructurePiece orig = _pieces[idx];
            if (orig.SplitParentId != 0 || orig.LengthM < 1.8f) return false; // En fazla 1 kez bolunur -> asla ustel patlama olmaz!

            byte matId = _pieceMaterialIds[idx] != 0 ? _pieceMaterialIds[idx] : MaterialIdCatalog.OakWood;
            Vector3 halfExt = _pieceHalfExtents[idx];
            bool isBox = halfExt.x > orig.DiameterM * 0.65f || halfExt.z > orig.DiameterM * 0.65f;

            // Orijinal parcayi kaldir ve yerine 2 yari parca olustur (birbirinden ayrilarak duserler)
            _pieceRemoved[idx] = 1;
            if (_pieceVisuals[idx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[idx]);
                _pieceVisuals[idx] = null;
            }
            for (int b = 0; b < _bondCount; b++)
            {
                if (_bonds[b].PieceA == orig.PieceId || _bonds[b].PieceB == orig.PieceId)
                {
                    _bonds[b].IsActive = 0;
                }
            }

            Quaternion uRot = ToUnityQuat(orig.Rotation);
            Vector3 splitAxis = isBox ? (uRot * Vector3.right) : (uRot * Vector3.up);
            float offset = isBox ? (halfExt.x * 0.52f) : (orig.LengthM * 0.26f);

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 childPos = ToUnityVec3(orig.Position) + splitAxis * (s * offset);
                int cIdx;
                if (isBox)
                {
                    Vector3 childSize = new Vector3(halfExt.x * 0.96f, halfExt.y * 2f, halfExt.z * 2f);
                    cIdx = AddBoxPieceInternal(orig.Kind, orig.Species, childSize, ToSysVec3(childPos), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
                }
                else
                {
                    cIdx = AddCylinderPieceInternal(orig.Kind, orig.Species, orig.LengthM * 0.48f, orig.DiameterM, ToSysVec3(childPos), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
                }
                _pieces[cIdx].SplitParentId = orig.PieceId;
                _pieces[cIdx].State = RigidTimberState.DynamicActive;
                _pieces[cIdx].LinearVelocity = orig.LinearVelocity + ToSysVec3(splitAxis * (s * 0.85f));
                _pieces[cIdx].AngularVelocity = orig.AngularVelocity;
            }
            return true;
        }

        private void AddBondInternal(int idxA, int idxB, TimberJoineryKind joinery, float diam, SysVec3 worldPos)
        {
            int islA = _pieces[idxA].IslandId;
            int islB = _pieces[idxB].IslandId;
            if (islA != islB && islA > 0 && islB > 0)
            {
                int targetIsl = Math.Min(islA, islB);
                int oldIsl = Math.Max(islA, islB);
                for (int k = 0; k < _pieceCount; k++)
                {
                    if (_pieces[k].IslandId == oldIsl)
                    {
                        _pieces[k].IslandId = targetIsl;
                    }
                }
            }

            EnsureBondCapacity(_bondCount + 1);
            float half = diam * 0.5f;
            _bonds[_bondCount++] = ReducedAemBondEvaluator.CreateInterface(
                _nextBondId++,
                in _pieces[idxA],
                in _pieces[idxB],
                worldPos,
                SysVec3.UnitY,
                half,
                half,
                joinery);
        }

        private GameObject BuildCylinderVisual(in RigidStructurePiece piece, byte matId, Material? sharedVoxelMat)
        {
            GameObject go = new GameObject($"RigidTimber_{piece.Kind}_{piece.PieceId}");
            go.transform.position = ToUnityVec3(piece.Position);
            go.transform.rotation = ToUnityQuat(piece.Rotation);

            _meshScratch.Clear();
            float r = piece.DiameterM * 0.5f;
            float halfL = piece.LengthM * 0.5f;
            PieceCarpentryProfile emptyCarp = default;
            EmitCarvedLogPrism(_meshScratch, r, -halfL, halfL, matId, in emptyCarp);

            Mesh mesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"TimberMesh_{piece.PieceId}");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;
            return go;
        }

        public void RebuildPieceMeshVisual(int idx)
        {
            if ((uint)idx >= (uint)_pieceCount || _pieceRemoved[idx] != 0 || _pieceVisuals[idx] == null) return;
            var mf = _pieceVisuals[idx]!.GetComponent<MeshFilter>();
            if (mf == null) return;

            _meshScratch.Clear();
            ref readonly RigidStructurePiece piece = ref _pieces[idx];
            float r = piece.DiameterM * 0.5f;
            float halfL = piece.LengthM * 0.5f;
            byte matId = _pieceMaterialIds[idx] != 0 ? _pieceMaterialIds[idx] : MaterialIdCatalog.OakWood;
            EmitCarvedLogPrism(_meshScratch, r, -halfL, halfL, matId, in _carpentryProfiles[idx]);
            mf.sharedMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"TimberMesh_{piece.PieceId}_Carved");
        }

        private GameObject BuildBoxVisual(uint id, RigidTimberPieceKind kind, SysVec3 pos, SysQuat rot, Vector3 half, byte matId, Material? sharedVoxelMat)
        {
            GameObject go = new GameObject($"RigidTimber_{kind}_{id}");
            go.transform.position = ToUnityVec3(pos);
            go.transform.rotation = ToUnityQuat(rot);

            _meshScratch.Clear();
            EmitBox(_meshScratch, -half, half, matId);

            Mesh mesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"TimberBox_{id}");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;
            return go;
        }

        /// <summary>
        /// Faz M2: Gercek 3B Oyuklu Kutuk/Direk Geometrisi (0 yeni doku VRAM, 0 B GC):
        /// - GrooveSectorMask: Oyulmus sekizgen yuzeyde 10cm derin U-kanal (ic yanaklar ve taban WoodPlank yontulmus odun rengi!),
        /// - SideMortise: Kesintisiz ana direkte kat kirisi yan omuz/zivana cepleri,
        /// - EndSaddleNotchMask: Yatay kutuk uclarinda 3B yarim ay Eyer Kertigi oyuklari,
        /// - TenonEndMask: Iki ucta oluk icine giren inceltilmis zivana dilleri,
        /// - ChinkedSeamMask: Ust/alt derz boyunca acik kil/kirec harc seridi.
        /// </summary>
        private static void EmitCarvedLogPrism(
            List<FacetedVoxelTriangle> dst,
            float radius,
            float yMin,
            float yMax,
            byte matId,
            in PieceCarpentryProfile carp)
        {
            const int sides = 8;
            byte heartwoodMat = MaterialIdCatalog.WoodPlank;
            float grooveDepth = Mathf.Min(0.11f, radius * 0.42f);
            float innerR = Mathf.Max(0.06f, radius - grooveDepth);

            const float halfStep = Mathf.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                // Duz yuzey merkezleri tam 0° (+X), 45°, 90° (+Z), 135°, 180° (-X), 225°, 270° (-Z), 315° hizasina oturur!
                float aMid = (i / (float)sides) * Mathf.PI * 2f;
                float a0 = aMid - halfStep;
                float a1 = aMid + halfStep;
                Vector3 nOut = new Vector3(Mathf.Cos(aMid), 0f, Mathf.Sin(aMid));
                Vector2 dir0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dir1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                Vector3 b0 = new Vector3(dir0.x * radius, yMin, dir0.y * radius);
                Vector3 b1 = new Vector3(dir1.x * radius, yMin, dir1.y * radius);
                Vector3 t0 = new Vector3(dir0.x * radius, yMax, dir0.y * radius);
                Vector3 t1 = new Vector3(dir1.x * radius, yMax, dir1.y * radius);

                bool hasGroove = (carp.GrooveSectorMask & (1 << i)) != 0;
                if (hasGroove)
                {
                    // Yuzeyi Sol Dudak (0..0.22) + 3B Girintili U-Oluk (0.22..0.78) + Sag Dudak (0.78..1.0) olarak bol!
                    Vector2 pL = Vector2.Lerp(dir0, dir1, 0.22f) * radius;
                    Vector2 pR = Vector2.Lerp(dir0, dir1, 0.78f) * radius;
                    Vector2 iL = Vector2.Lerp(dir0, dir1, 0.25f) * innerR;
                    Vector2 iR = Vector2.Lerp(dir0, dir1, 0.75f) * innerR;
                    Vector3 nSide = new Vector3(-(dir1.y - dir0.y), 0f, dir1.x - dir0.x).normalized;

                    // Sol ve sag kabuk dudaklari
                    EmitQuad(dst, b0, t0, new Vector3(pL.x, yMax, pL.y), new Vector3(pL.x, yMin, pL.y), nOut, matId);
                    EmitQuad(dst, new Vector3(pR.x, yMin, pR.y), new Vector3(pR.x, yMax, pR.y), t1, b1, nOut, matId);

                    // Oluk ic sol yanak, ic taban ve ic sag yanak (acik sari yontulmus ic odun rengi!)
                    EmitQuad(dst, new Vector3(pL.x, yMin, pL.y), new Vector3(pL.x, yMax, pL.y), new Vector3(iL.x, yMax, iL.y), new Vector3(iL.x, yMin, iL.y), nSide, heartwoodMat);
                    EmitQuad(dst, new Vector3(iL.x, yMin, iL.y), new Vector3(iL.x, yMax, iL.y), new Vector3(iR.x, yMax, iR.y), new Vector3(iR.x, yMin, iR.y), nOut, heartwoodMat);
                    EmitQuad(dst, new Vector3(iR.x, yMin, iR.y), new Vector3(iR.x, yMax, iR.y), new Vector3(pR.x, yMax, pR.y), new Vector3(pR.x, yMin, pR.y), -nSide, heartwoodMat);

                    // Ayni yuzde ek olarak Yan Kiris Zivanasi (SideMortise) da acildiysa tasiyici omuz rafini goster!
                    for (int m = 0; m < carp.SideMortiseCount; m++)
                    {
                        if (carp.GetMortiseSector(m) != i) continue;
                        float my = Mathf.Clamp(carp.GetMortiseLocalY(m), yMin + 0.30f, yMax - 0.30f);
                        Vector2 sh0 = dir0 * (radius + 0.045f), sh1 = dir1 * (radius + 0.045f);
                        EmitQuad(dst, new Vector3(iL.x, my - 0.22f, iL.y), new Vector3(sh0.x, my - 0.22f, sh0.y), new Vector3(sh1.x, my - 0.22f, sh1.y), new Vector3(iR.x, my - 0.22f, iR.y), Vector3.up, MaterialIdCatalog.TreatedTimberBeam);
                        EmitQuad(dst, new Vector3(sh0.x, my - 0.30f, sh0.y), new Vector3(sh0.x, my - 0.22f, sh0.y), new Vector3(sh1.x, my - 0.22f, sh1.y), new Vector3(sh1.x, my - 0.30f, sh1.y), nOut, MaterialIdCatalog.TreatedTimberBeam);
                    }

                    // Kapaklar
                    EmitTri(dst, new Vector3(0f, yMax, 0f), new Vector3(pL.x, yMax, pL.y), t0, Vector3.up, heartwoodMat);
                    EmitTri(dst, new Vector3(0f, yMax, 0f), new Vector3(iR.x, yMax, iR.y), new Vector3(iL.x, yMax, iL.y), Vector3.up, heartwoodMat);
                    EmitTri(dst, new Vector3(0f, yMax, 0f), t1, new Vector3(pR.x, yMax, pR.y), Vector3.up, heartwoodMat);
                    EmitTri(dst, new Vector3(0f, yMin, 0f), b0, new Vector3(pL.x, yMin, pL.y), Vector3.down, heartwoodMat);
                    EmitTri(dst, new Vector3(0f, yMin, 0f), new Vector3(iL.x, yMin, iL.y), new Vector3(iR.x, yMin, iR.y), Vector3.down, heartwoodMat);
                    EmitTri(dst, new Vector3(0f, yMin, 0f), new Vector3(pR.x, yMin, pR.y), b1, Vector3.down, heartwoodMat);
                }
                else
                {
                    // Bu dilimde Yan Kiris Zivanasi (SideMortise) veya Uctaki Eyer Kertigi (EndSaddleNotch) var mi?
                    bool emittedMortise = false;
                    for (int m = 0; m < carp.SideMortiseCount; m++)
                    {
                        if (carp.GetMortiseSector(m) != i) continue;
                        float my = Mathf.Clamp(carp.GetMortiseLocalY(m), yMin + 0.30f, yMax - 0.30f);
                        float mBot = my - 0.20f;
                        float mTop = my + 0.20f;

                        // Alt kabuk + ust kabuk + ortada 3B oyulmus zivana cebi + tasiyici omuz cikintisi!
                        EmitQuad(dst, b0, new Vector3(b0.x, mBot, b0.z), new Vector3(b1.x, mBot, b1.z), b1, nOut, matId);
                        EmitQuad(dst, new Vector3(b0.x, mTop, b0.z), t0, t1, new Vector3(b1.x, mTop, b1.z), nOut, matId);

                        Vector2 mi0 = dir0 * innerR;
                        Vector2 mi1 = dir1 * innerR;
                        EmitQuad(dst, new Vector3(mi0.x, mBot, mi0.y), new Vector3(mi0.x, mTop, mi0.y), new Vector3(mi1.x, mTop, mi1.y), new Vector3(mi1.x, mBot, mi1.y), nOut, heartwoodMat);
                        EmitQuad(dst, new Vector3(b0.x, mBot, b0.z), new Vector3(mi0.x, mBot, mi0.y), new Vector3(mi1.x, mBot, mi1.y), new Vector3(b1.x, mBot, b1.z), Vector3.up, heartwoodMat);
                        EmitQuad(dst, new Vector3(mi0.x, mTop, mi0.y), new Vector3(b0.x, mTop, b0.z), new Vector3(b1.x, mTop, b1.z), new Vector3(mi1.x, mTop, mi1.y), Vector3.down, heartwoodMat);
                        emittedMortise = true;
                        break;
                    }

                    if (!emittedMortise)
                    {
                        // Mod A Eyer Kertigi (Saddle Notch): Yatay kutukte ust yuzeye denk gelen dilimlerde (-X / +X yonu) uc oyugu
                        bool isUpperSaddleFace = (carp.EndSaddleNotchMask != 0) && (i == 3 || i == 4 || i == 0 || i == 7);
                        if (isUpperSaddleFace && (yMax - yMin) >= 1.6f)
                        {
                            float curY0 = yMin;
                            if ((carp.EndSaddleNotchMask & 1) != 0)
                            {
                                float n0 = yMin + 0.14f, n1 = yMin + 0.54f;
                                EmitQuad(dst, new Vector3(b0.x, curY0, b0.z), new Vector3(b0.x, n0, b0.z), new Vector3(b1.x, n0, b1.z), new Vector3(b1.x, curY0, b1.z), nOut, matId);
                                Vector2 si0 = dir0 * (radius * 0.55f), si1 = dir1 * (radius * 0.55f);
                                EmitQuad(dst, new Vector3(si0.x, n0, si0.y), new Vector3(si0.x, n1, si0.y), new Vector3(si1.x, n1, si1.y), new Vector3(si1.x, n0, si1.y), nOut, heartwoodMat);
                                EmitQuad(dst, new Vector3(b0.x, n0, b0.z), new Vector3(si0.x, n0, si0.y), new Vector3(si1.x, n0, si1.y), new Vector3(b1.x, n0, b1.z), Vector3.up, heartwoodMat);
                                EmitQuad(dst, new Vector3(si0.x, n1, si0.y), new Vector3(b0.x, n1, b0.z), new Vector3(b1.x, n1, b1.z), new Vector3(si1.x, n1, si1.y), Vector3.down, heartwoodMat);
                                curY0 = n1;
                            }
                            float endY = ((carp.EndSaddleNotchMask & 2) != 0) ? (yMax - 0.54f) : yMax;
                            EmitQuad(dst, new Vector3(b0.x, curY0, b0.z), new Vector3(b0.x, endY, b0.z), new Vector3(b1.x, endY, b1.z), new Vector3(b1.x, curY0, b1.z), nOut, matId);
                            if ((carp.EndSaddleNotchMask & 2) != 0)
                            {
                                float n0 = yMax - 0.54f, n1 = yMax - 0.14f;
                                Vector2 si0 = dir0 * (radius * 0.55f), si1 = dir1 * (radius * 0.55f);
                                EmitQuad(dst, new Vector3(si0.x, n0, si0.y), new Vector3(si0.x, n1, si0.y), new Vector3(si1.x, n1, si1.y), new Vector3(si1.x, n0, si1.y), nOut, heartwoodMat);
                                EmitQuad(dst, new Vector3(b0.x, n0, b0.z), new Vector3(si0.x, n0, si0.y), new Vector3(si1.x, n0, si1.y), new Vector3(b1.x, n0, b1.z), Vector3.up, heartwoodMat);
                                EmitQuad(dst, new Vector3(si0.x, n1, si0.y), new Vector3(b0.x, n1, b0.z), new Vector3(b1.x, n1, b1.z), new Vector3(si1.x, n1, si1.y), Vector3.down, heartwoodMat);
                                EmitQuad(dst, new Vector3(b0.x, n1, b0.z), t0, t1, new Vector3(b1.x, n1, b1.z), nOut, matId);
                            }
                        }
                        else
                        {
                            EmitQuad(dst, b0, t0, t1, b1, nOut, matId);
                        }
                    }

                    EmitTri(dst, new Vector3(0f, yMax, 0f), t1, t0, Vector3.up, heartwoodMat);
                    EmitTri(dst, new Vector3(0f, yMin, 0f), b0, b1, Vector3.down, heartwoodMat);
                }
            }

            // Sifir ic ice gecmeli uctaki inceltilmis Zivana Dilleri (Tenon — yalnizca bu dil oluk icine girer!)
            if ((carp.TenonEndMask & 1) != 0)
            {
                EmitBox(dst, new Vector3(-0.07f, yMin - 0.085f, -0.07f), new Vector3(0.07f, yMin, 0.07f), heartwoodMat);
            }
            if ((carp.TenonEndMask & 2) != 0)
            {
                EmitBox(dst, new Vector3(-0.07f, yMax, -0.07f), new Vector3(0.07f, yMax + 0.085f, 0.07f), heartwoodMat);
            }

            // Gercek 3B Chinking (Kil/Kirec Derz Yalitimi Seridi)
            if (carp.ChinkedSeamMask != 0)
            {
                float seamOff = radius * 0.86f;
                EmitBox(dst, new Vector3(-seamOff - 0.04f, yMin + 0.02f, -0.09f), new Vector3(-seamOff + 0.04f, yMax - 0.02f, 0.09f), MaterialIdCatalog.Clay);
                EmitBox(dst, new Vector3(seamOff - 0.04f, yMin + 0.02f, -0.09f), new Vector3(seamOff + 0.04f, yMax - 0.02f, 0.09f), MaterialIdCatalog.Clay);
            }
        }

        /// <summary>
        /// Faz M1: Voksel raycast'inin goremedigi tum RigidTimber parcalarini (Post, Log, Beam, Plank, Rafter) OBB uzerinden milimetrik yakalar!
        /// </summary>
        public bool TryRaycastAnyPiece(
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            out int pieceIdx,
            out Vector3 hitWorldPoint,
            out Vector3 hitWorldNormal,
            out float hitDist)
        {
            pieceIdx = -1;
            hitWorldPoint = Vector3.zero;
            hitWorldNormal = Vector3.up;
            hitDist = maxDist;
            if (_pieceCount == 0 || rayDir.sqrMagnitude < 1e-6f) return false;

            Vector3 dirNorm = rayDir.normalized;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0) continue;
                if (TryRaycastPieceObbDetailed(i, rayOrigin, dirNorm, hitDist, out float d, out Vector3 wPt, out Vector3 wNorm))
                {
                    hitDist = d;
                    pieceIdx = i;
                    hitWorldPoint = wPt;
                    hitWorldNormal = wNorm;
                }
            }
            return pieceIdx >= 0;
        }

        private bool TryRaycastPieceObbDetailed(
            int idx,
            Vector3 rayOrigin,
            Vector3 rayDirNorm,
            float maxDist,
            out float hitDist,
            out Vector3 hitWorldPoint,
            out Vector3 hitWorldNormal)
        {
            hitDist = maxDist;
            hitWorldPoint = Vector3.zero;
            hitWorldNormal = Vector3.up;

            Vector3 center = ToUnityVec3(_pieces[idx].Position);
            Quaternion rot = ToUnityQuat(_pieces[idx].Rotation);
            Quaternion invRot = Quaternion.Inverse(rot);
            Vector3 origL = invRot * (rayOrigin - center);
            Vector3 dirL = invRot * rayDirNorm;
            Vector3 half = _pieceHalfExtents[idx] + new Vector3(0.06f, 0.06f, 0.06f);

            float tMin = 0.04f;
            float tMax = maxDist;
            int hitAxis = 1;
            float hitSign = 1f;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = origL[axis];
                float d = dirL[axis];
                float h = half[axis];
                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (o < -h || o > h) return false;
                }
                else
                {
                    float invD = 1.0f / d;
                    float t1 = (-h - o) * invD;
                    float t2 = (h - o) * invD;
                    float s1 = -1f;
                    if (t1 > t2) { (t1, t2) = (t2, t1); s1 = 1f; }
                    if (t1 > tMin) { tMin = t1; hitAxis = axis; hitSign = s1; }
                    if (t2 < tMax) tMax = t2;
                    if (tMin > tMax) return false;
                }
            }

            hitDist = tMin;
            hitWorldPoint = rayOrigin + rayDirNorm * tMin;
            Vector3 localNormal = Vector3.zero;
            localNormal[hitAxis] = hitSign;
            hitWorldNormal = rot * localNormal;
            return true;
        }

        public enum AxeCarpentryToolMode : byte
        {
            TreeAndLooseChop = 0,     // Varsayilan: Yalnizca agac ve yerdeki serbest kutugu keser (ayaktaki yapi %100 korumali!)
            VerticalWallGroove = 1,   // Yalnizca dikey duvar olugu acar ([R] 45° yon cevirir, karsi direkle otomatik hizalar!)
            SideMortiseAndNotch = 2,  // Yalnizca yan kiris zivanasi (Post) veya eyer kertigi (Log) acar!
            StructuralTrimAndCut = 3  // Yalnizca yapi kesimi & cati ustu fazla direk ucu sifira sifir budama!
        }

        public struct AxeCarpentryPreviewState
        {
            public bool HasTarget;
            public int PieceIdx;
            public RigidTimberPieceKind PieceKind;
            public AxeCarpentryToolMode Mode;
            public byte Sector;
            public bool AlreadyCarved;
            public bool IsSafePostTopTrim;
            public Vector3 PieceCenter;
            public Quaternion PieceRot;
            public float PieceLengthM;
            public float PieceRadiusM;
            public Vector3 HitWorldPt;
            public Vector3 FaceDirWorld; // Secili oluk/zivana dis yonu (XZ birim vektoru)
            public int PairedPostIdx;    // Hizadaki karsi direk (-1 ise yok)
            public Vector3 PairedPostFacePt;
            public float PairedSpanM;
            public string HudText;
        }

        public int FindAlignedOppositePost(int postIdx, Vector3 postCenter, Vector3 dirWorldXZ, out float spanM)
        {
            spanM = 0f;
            Vector2 dir2 = new Vector2(dirWorldXZ.x, dirWorldXZ.z);
            if (dir2.sqrMagnitude < 1e-5f) return -1;
            dir2.Normalize();

            int bestJ = -1;
            float bestScore = -1f;
            for (int j = 0; j < _pieceCount; j++)
            {
                if (j == postIdx || _pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post)
                    continue;
                Vector2 diff = new Vector2(_pieces[j].Position.X - postCenter.x, _pieces[j].Position.Z - postCenter.z);
                float dist = diff.magnitude;
                if (dist < 1.05f || dist > 14.5f) continue;
                Vector2 toJ = diff / dist;
                float dot = Vector2.Dot(dir2, toJ);
                if (dot < 0.85f) continue; // +-31 derece koni icindeki karsi direk

                // Arada baska bir dikili direk varsa atla
                bool blocked = false;
                for (int k = 0; k < _pieceCount; k++)
                {
                    if (k == postIdx || k == j || _pieceRemoved[k] != 0 || _pieces[k].State != RigidTimberState.StaticAnchored || _pieces[k].Kind != RigidTimberPieceKind.Post)
                        continue;
                    Vector2 kDiff = new Vector2(_pieces[k].Position.X - postCenter.x, _pieces[k].Position.Z - postCenter.z);
                    float projK = Vector2.Dot(kDiff, toJ);
                    if (projK > 0.35f && projK < dist - 0.35f)
                    {
                        float perpK = Vector2.Distance(toJ * projK, kDiff);
                        if (perpK <= 0.65f) { blocked = true; break; }
                    }
                }
                if (blocked) continue;

                float score = dot - dist * 0.015f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestJ = j;
                    spanM = dist;
                }
            }
            return bestJ;
        }

        public bool TryEvaluateAxeCarpentryPreview(
            AxeCarpentryToolMode mode,
            int manualSectorOffset,
            bool pairBothPosts,
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out AxeCarpentryPreviewState state)
        {
            state = default;
            state.PairedPostIdx = -1;
            state.Mode = mode;

            if (!TryRaycastAnyPiece(rayOrigin, rayDir, maxDist, out int idx, out Vector3 hitPt, out _, out _))
                return false;

            _soilSampler.WorldChunks = worldChunks;
            ref readonly RigidStructurePiece p = ref _pieces[idx];
            ref readonly PieceCarpentryProfile carp = ref _carpentryProfiles[idx];
            Quaternion rot = ToUnityQuat(p.Rotation);
            Vector3 center = ToUnityVec3(p.Position);

            state.HasTarget = true;
            state.PieceIdx = idx;
            state.PieceKind = p.Kind;
            state.PieceCenter = center;
            state.PieceRot = rot;
            state.PieceLengthM = p.LengthM;
            state.PieceRadiusM = p.DiameterM * 0.5f;
            state.HitWorldPt = hitPt;

            // Kip 0: Agac & Yerdeki Kutuk Kesimi (Ayaktaki yapi %100 korumali!)
            if (mode == AxeCarpentryToolMode.TreeAndLooseChop)
            {
                if (p.State == RigidTimberState.StaticAnchored)
                {
                    state.HudText = $"🛡️ YAPI KORUMASI AKTİF ({p.Kind}): Yanlışlıkla kesilmez! | [V] Oluk / Zıvana / Budama Kipi | [K] Sök";
                    return true;
                }
                Vector3 splitAxis = rot * Vector3.up;
                float halfL = p.LengthM * 0.5f;
                float tAlong = Mathf.Clamp(Vector3.Dot(hitPt - center, splitAxis), -halfL, halfL);
                float leftM = halfL + tAlong;
                float rightM = p.LengthM - leftM;
                state.FaceDirWorld = splitAxis;
                state.HudText = $"📏 SERBEST KÜTÜK KESİMİ: {leftM:F2}m | {rightM:F2}m (Toplam {p.LengthM:F2}m) — [Sol Tık] Kes | [V] Oluk Kipi";
                return true;
            }

            // Kip 1: Dikey Duvar Olugu Acma (Yalnizca dikili Post uzerinde!)
            if (mode == AxeCarpentryToolMode.VerticalWallGroove)
            {
                if (p.Kind != RigidTimberPieceKind.Post || p.State != RigidTimberState.StaticAnchored)
                {
                    state.HudText = "🪵 [Kip 2/4: DİKEY OLUK] Dikili bir Ana Direğe (Post) bakın! | [V] Sonraki Kip";
                    return true;
                }

                Vector3 aimRadial = new Vector3(hitPt.x - center.x, 0f, hitPt.z - center.z);
                if (aimRadial.sqrMagnitude < 1e-4f) aimRadial = new Vector3(-rayDir.x, 0f, -rayDir.z);

                // Eger oyuncu [R] ile elle cevirmemisse (offset == 0): ister iki diregin arasinda dursun (aim2),
                // ister disarida durup bu diregin uzerinden karsi direge baksin (camFwd2), o yondeki dikili direge otomatik kilitlen!
                if ((manualSectorOffset & 7) == 0)
                {
                    Vector2 aim2 = new Vector2(aimRadial.x, aimRadial.z).normalized;
                    Vector2 camFwd2 = new Vector2(rayDir.x, rayDir.z).normalized;
                    int bestAutoPost = -1;
                    float bestAutoScore = -1f;
                    for (int j = 0; j < _pieceCount; j++)
                    {
                        if (j == idx || _pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post)
                            continue;
                        Vector2 diff = new Vector2(_pieces[j].Position.X - center.x, _pieces[j].Position.Z - center.z);
                        float dist = diff.magnitude;
                        if (dist < 1.05f || dist > 14.5f) continue;
                        Vector2 toJ = diff / dist;
                        float dot = Mathf.Max(Vector2.Dot(aim2, toJ), Vector2.Dot(camFwd2, toJ));
                        if (dot < 0.42f) continue;
                        bool thisAlreadyFacesJ = HasGrooveFacingWorldDir(in carp, rot, toJ, 0.75f);
                        Quaternion rotJ = ToUnityQuat(_pieces[j].Rotation);
                        bool jFacesBack = HasGrooveFacingWorldDir(in _carpentryProfiles[j], rotJ, -toJ, 0.50f);
                        float sc = dot + (!thisAlreadyFacesJ ? 0.35f : 0f) + (jFacesBack ? 0.35f : 0.10f) - dist * 0.015f;
                        if (sc > bestAutoScore)
                        {
                            bestAutoScore = sc;
                            bestAutoPost = j;
                        }
                    }
                    if (bestAutoPost >= 0)
                    {
                        aimRadial = new Vector3(_pieces[bestAutoPost].Position.X - center.x, 0f, _pieces[bestAutoPost].Position.Z - center.z).normalized;
                    }
                }

                int baseSec = WorldDirToLocalOctantSector(rot, aimRadial);
                byte finalSec = (byte)((baseSec + (manualSectorOffset & 7)) & 7);
                Vector3 faceDir = LocalOctantSectorToWorldDir(rot, finalSec);
                string dirLbl = GetSectorCompassLabel(faceDir);
                bool hasGroove = (carp.GrooveSectorMask & (1 << finalSec)) != 0;

                state.Sector = finalSec;
                state.FaceDirWorld = faceDir;
                state.AlreadyCarved = hasGroove;

                int oppIdx = pairBothPosts ? FindAlignedOppositePost(idx, center, faceDir, out float oppSpan) : -1;
                float actualOppSpan = oppIdx >= 0 ? Vector2.Distance(new Vector2(center.x, center.z), new Vector2(_pieces[oppIdx].Position.X, _pieces[oppIdx].Position.Z)) : 0f;
                if (oppIdx >= 0)
                {
                    Vector3 oppC = ToUnityVec3(_pieces[oppIdx].Position);
                    state.PairedPostIdx = oppIdx;
                    state.PairedSpanM = actualOppSpan;
                    state.PairedPostFacePt = new Vector3(oppC.x, Mathf.Clamp(hitPt.y, oppC.y - _pieces[oppIdx].LengthM * 0.45f, oppC.y + _pieces[oppIdx].LengthM * 0.45f), oppC.z)
                                             - faceDir * (_pieces[oppIdx].DiameterM * 0.5f);
                }

                if (oppIdx >= 0)
                {
                    state.HudText = $"🪵 [DİKEY OLUK — {dirLbl}] ↔ Karşı Direkle Birlikte ({actualOppSpan:F2}m) | [Sol Tık] Aç | [R] 45° Yön | [J] Tek/Çift | [K] Kapat";
                }
                else
                {
                    string modeLbl = pairBothPosts ? "[J] Tek Direk Modu" : "Tek Direk Modu ([J] Çift)";
                    state.HudText = hasGroove
                        ? $"✅ [DİKEY OLUK — {dirLbl}] Bu yüz oluklu! | [R] 45° Başka Yön | [K] Bu Oluğu Kapat | [V] Sonraki Kip"
                        : $"🪵 [DİKEY OLUK — {dirLbl}] [Sol Tık] Oy | [R] 45° Yön Çevir | {modeLbl} | [V] Kip";
                }
                return true;
            }

            // Kip 2: Yan Kiris Zivanasi (Post) & Eyer Kertigi (Log)
            if (mode == AxeCarpentryToolMode.SideMortiseAndNotch)
            {
                if (p.Kind == RigidTimberPieceKind.Post && p.State == RigidTimberState.StaticAnchored)
                {
                    Vector3 aimRadial = new Vector3(hitPt.x - center.x, 0f, hitPt.z - center.z);
                    if (aimRadial.sqrMagnitude < 1e-4f) aimRadial = new Vector3(-rayDir.x, 0f, -rayDir.z);
                    if ((manualSectorOffset & 7) == 0)
                    {
                        Vector2 aim2 = new Vector2(aimRadial.x, aimRadial.z).normalized;
                        Vector2 camFwd2 = new Vector2(rayDir.x, rayDir.z).normalized;
                        int bestMPost = -1;
                        float bestMSc = -1f;
                        for (int j = 0; j < _pieceCount; j++)
                        {
                            if (j == idx || _pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post)
                                continue;
                            Vector2 diff = new Vector2(_pieces[j].Position.X - center.x, _pieces[j].Position.Z - center.z);
                            float dist = diff.magnitude;
                            if (dist < 1.05f || dist > 14.5f) continue;
                            Vector2 toJ = diff / dist;
                            float dot = Mathf.Max(Vector2.Dot(aim2, toJ), Vector2.Dot(camFwd2, toJ));
                            if (dot >= 0.42f && dot - dist * 0.015f > bestMSc)
                            {
                                bestMSc = dot - dist * 0.015f;
                                bestMPost = j;
                            }
                        }
                        if (bestMPost >= 0)
                        {
                            aimRadial = new Vector3(_pieces[bestMPost].Position.X - center.x, 0f, _pieces[bestMPost].Position.Z - center.z).normalized;
                        }
                    }
                    int baseSec = WorldDirToLocalOctantSector(rot, aimRadial);
                    byte finalSec = (byte)((baseSec + (manualSectorOffset & 7)) & 7);
                    Vector3 faceDir = LocalOctantSectorToWorldDir(rot, finalSec);
                    string dirLbl = GetSectorCompassLabel(faceDir);
                    float surfY = SampleSurroundingRimSurfaceY(center.x, center.z, hitPt.y + 1.5f);
                    float hAbove = hitPt.y - surfY;
                    Vector3 localHit = Quaternion.Inverse(rot) * (hitPt - center);
                    bool hasMort = carp.TryFindMortiseNear(finalSec, localHit.y, 0.35f, out _);

                    state.Sector = finalSec;
                    state.FaceDirWorld = faceDir;
                    state.AlreadyCarved = hasMort;

                    float oppSpan = 0f;
                    int oppIdx = pairBothPosts ? FindAlignedOppositePost(idx, center, faceDir, out oppSpan) : -1;
                    if (oppIdx >= 0)
                    {
                        Vector3 oppC = ToUnityVec3(_pieces[oppIdx].Position);
                        state.PairedPostIdx = oppIdx;
                        state.PairedSpanM = oppSpan;
                        state.PairedPostFacePt = new Vector3(oppC.x, hitPt.y, oppC.z) - faceDir * (_pieces[oppIdx].DiameterM * 0.5f);
                    }

                    string pairNote = oppIdx >= 0 ? $" ↔ Karşı Direkle +{hAbove:F2}m" : " (Tek Direk)";
                    state.HudText = $"🏗️ [YAN ZIVANA — {dirLbl}, +{hAbove:F2}m]{pairNote} | [Sol Tık] Oy | [R] 45° Yön | [J] Tek/Çift | [V] Kip";
                    return true;
                }

                if (p.Kind == RigidTimberPieceKind.Log && p.State == RigidTimberState.StaticAnchored && p.LengthM >= 1.6f)
                {
                    Vector3 localHit = Quaternion.Inverse(rot) * (hitPt - center);
                    bool isNegEnd = localHit.y < 0f;
                    byte bit = isNegEnd ? (byte)1 : (byte)2;
                    state.Sector = bit;
                    state.FaceDirWorld = rot * (isNegEnd ? Vector3.down : Vector3.up);
                    state.AlreadyCarved = (carp.EndSaddleNotchMask & bit) != 0;
                    state.HudText = state.AlreadyCarved
                        ? "✅ [EYER KERTİĞİ] Bu uç oyulmuş! | Kapatmak için [K] | [V] Kip Değiştir"
                        : "🪵 [EYER KERTİĞİ — SADDLE NOTCH] Kütük ucuna 90° oyuk aç — [Sol Tık] Oy | [V] Kip";
                    return true;
                }

                state.HudText = "🏗️ [Kip 3/4: ZIVANA & KERTİK] Dikili direğe (Yan Kiriş Yuvası) veya yatay kütük ucuna (Eyer Kertiği) bakın! | [V] Kip";
                return true;
            }

            // Kip 3: Yapi Kesim & Cati Ustu Fazla Direk Ucu Budama!
            if (p.Kind == RigidTimberPieceKind.Post && p.State == RigidTimberState.StaticAnchored)
            {
                float footY = center.y - p.LengthM * 0.5f;
                float topY = center.y + p.LengthM * 0.5f;
                uint pid = p.PieceId;
                float highestBondY = footY + 1.5f;
                for (int b = 0; b < _bondCount; b++)
                {
                    if (_bonds[b].IsActive == 0) continue;
                    if (_bonds[b].PieceA == pid)
                    {
                        float by = center.y + (_bonds[b].LocalA0.Y + _bonds[b].LocalA2.Y) * 0.5f;
                        if (by > highestBondY) highestBondY = by;
                    }
                    else if (_bonds[b].PieceB == pid)
                    {
                        float by = center.y + (_bonds[b].LocalB0.Y + _bonds[b].LocalB2.Y) * 0.5f;
                        if (by > highestBondY) highestBondY = by;
                    }
                }
                bool isSafeTrim = hitPt.y >= highestBondY + 0.10f && hitPt.y > footY + 1.8f && hitPt.y < topY - 0.12f;
                state.IsSafePostTopTrim = isSafeTrim;
                float keepLen = Mathf.Max(0f, hitPt.y - footY);
                float trimLen = Mathf.Max(0f, topY - hitPt.y);
                state.HudText = isSafeTrim
                    ? $"🪚 [ÇATI ÜSTÜ FAZLA UÇ BUDAMA] Altta Kalan: {keepLen:F2}m | Kesilecek Üst: {trimLen:F2}m (Alt bağlar kopmaz!) — [Sol Tık] Kes"
                    : $"⚠️ [ANA DİREK YIKIM KESİMİ] Dikkat: Bağlantı altından ({keepLen:F2}m) kesmek direği devirir! — [Sol Tık x3] Kes | [V] Korumalı Kip";
                return true;
            }

            state.HudText = $"⚠️ [YAPI ELEMANI KESİMİ — {p.Kind}] Tam mavi/kırmızı halkadan kesmek için [Sol Tık x2] | [V] Korumalı Kip";
            return true;
        }

        public bool TryUndoAxeCarpentryKeyK(
            AxeCarpentryToolMode mode,
            int manualSectorOffset,
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string feedback)
        {
            feedback = string.Empty;
            if (!TryEvaluateAxeCarpentryPreview(mode, manualSectorOffset, pairBothPosts: false, rayOrigin, rayDir, maxDist, worldChunks, out AxeCarpentryPreviewState st) || !st.HasTarget)
                return false;

            int idx = st.PieceIdx;
            ref RigidStructurePiece p = ref _pieces[idx];
            ref PieceCarpentryProfile carp = ref _carpentryProfiles[idx];

            if (mode == AxeCarpentryToolMode.VerticalWallGroove && p.Kind == RigidTimberPieceKind.Post)
            {
                if ((carp.GrooveSectorMask & (1 << st.Sector)) == 0)
                {
                    feedback = "ℹ️ Bu yüzde kapatılacak açık bir oluk yok ([R] ile oluklu yüze çevirebilirsiniz).";
                    return true;
                }
                carp.GrooveSectorMask &= unchecked((byte)~(1 << st.Sector));
                RebuildPieceMeshVisual(idx);
                feedback = $"♻️ Ana Direğin {GetSectorCompassLabel(st.FaceDirWorld)} yüzündeki oluk [K] ile kapatıldı!";
                return true;
            }

            if (mode == AxeCarpentryToolMode.SideMortiseAndNotch && p.Kind == RigidTimberPieceKind.Log)
            {
                if ((carp.EndSaddleNotchMask & st.Sector) == 0)
                {
                    feedback = "ℹ️ Bu kütük ucunda kapatılacak açık bir Eyer Kertiği yok.";
                    return true;
                }
                carp.EndSaddleNotchMask &= unchecked((byte)~st.Sector);
                RebuildPieceMeshVisual(idx);
                feedback = "♻️ Kütük ucundaki Eyer Kertiği [K] ile kapatıldı!";
                return true;
            }

            return false;
        }

        public bool TryExecuteAxeCarpentryAction(
            AxeCarpentryToolMode mode,
            int manualSectorOffset,
            bool pairBothPosts,
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;

            if (!TryEvaluateAxeCarpentryPreview(mode, manualSectorOffset, pairBothPosts, rayOrigin, rayDir, maxDist, worldChunks, out AxeCarpentryPreviewState st) || !st.HasTarget)
                return false;

            int idx = st.PieceIdx;
            ref RigidStructurePiece p = ref _pieces[idx];
            ref PieceCarpentryProfile carp = ref _carpentryProfiles[idx];

            // Kip 0: Ayaktaki yapiyi ASLA kesmez; yalnizca yerdeki serbest kutugu keser!
            if (mode == AxeCarpentryToolMode.TreeAndLooseChop)
            {
                if (p.State == RigidTimberState.StaticAnchored)
                {
                    feedback = "🛡️ Yapı Koruması Aktif: Yanlış diktiysen [K] ile bütün kütüğü geri alabilir, kesmek için [V] ile Kip 4'e geçebilirsin!";
                    return true;
                }
                return TrySplitPieceAtWorldPoint(idx, st.HitWorldPt, worldChunks, out salvagedMaterialId, out salvagedCount, out feedback);
            }

            // Kip 1: Dikey Duvar Olugu Acma (Tek tikta net oyma + karsi direk eslestirme!)
            if (mode == AxeCarpentryToolMode.VerticalWallGroove)
            {
                if (p.Kind != RigidTimberPieceKind.Post || p.State != RigidTimberState.StaticAnchored)
                {
                    feedback = "🪵 Dikey oluk yalnızca dikili Ana Direklere (Post) açılır! Direğe bakıp [Sol Tık] yapın.";
                    return true;
                }

                byte sec = st.Sector;
                string dirLbl = GetSectorCompassLabel(st.FaceDirWorld);
                carp.GrooveSectorMask |= (byte)(1 << sec);
                carp.PendingHits = 0;
                carp.PendingActionKind = 0;
                RebuildPieceMeshVisual(idx);

                if (pairBothPosts && st.PairedPostIdx >= 0)
                {
                    int opp = st.PairedPostIdx;
                    Quaternion oppRot = ToUnityQuat(_pieces[opp].Rotation);
                    Vector3 toFirst = (st.PieceCenter - ToUnityVec3(_pieces[opp].Position));
                    toFirst.y = 0f;
                    byte oppSec = (byte)WorldDirToLocalOctantSector(oppRot, toFirst);
                    _carpentryProfiles[opp].GrooveSectorMask |= (byte)(1 << oppSec);
                    RebuildPieceMeshVisual(opp);
                    feedback = $"✅ {dirLbl} yüzü ve karşı direğin bakan iç yüzünde ({st.PairedSpanM:F2}m) karşılıklı 3B DİKEY OLUKLAR açıldı! Artık araya doğrudan duvar kütüğü sürebilirsiniz!";
                    return true;
                }

                feedback = $"🪵 Ana Direğin {dirLbl} yüzünde 3B DİKEY OLUK açıldı! ([R] 45° Yön | [J] Tek/Çift Direk | [K] Oluğu Kapat)";
                return true;
            }

            // Kip 2: Yan Kiris Zivanasi & Eyer Kertigi
            if (mode == AxeCarpentryToolMode.SideMortiseAndNotch)
            {
                if (p.Kind == RigidTimberPieceKind.Post && p.State == RigidTimberState.StaticAnchored)
                {
                    Vector3 localHit = Quaternion.Inverse(st.PieceRot) * (st.HitWorldPt - st.PieceCenter);
                    carp.AddOrUpdateMortise(st.Sector, localHit.y);
                    RebuildPieceMeshVisual(idx);
                    float surfY = SampleSurroundingRimSurfaceY(st.PieceCenter.x, st.PieceCenter.z, st.HitWorldPt.y + 1.5f);
                    float hAbove = st.HitWorldPt.y - surfY;
                    string dirLbl = GetSectorCompassLabel(st.FaceDirWorld);

                    if (pairBothPosts && st.PairedPostIdx >= 0)
                    {
                        int opp = st.PairedPostIdx;
                        Quaternion oppRot = ToUnityQuat(_pieces[opp].Rotation);
                        Vector3 oppCenter = ToUnityVec3(_pieces[opp].Position);
                        byte oppSec = (byte)WorldDirToLocalOctantSector(oppRot, st.PieceCenter - oppCenter);
                        float oppLocalY = st.HitWorldPt.y - oppCenter.y;
                        _carpentryProfiles[opp].AddOrUpdateMortise(oppSec, oppLocalY);
                        RebuildPieceMeshVisual(opp);
                        feedback = $"🏗️ İki direk arasında (+{hAbove:F2}m terazi kotunda) karşılıklı 3B YAN KİRİŞ ZIVANALARI açıldı! Ana kirişi oturtabilirsiniz.";
                        return true;
                    }

                    feedback = $"🏗️ Ana Direğin {dirLbl} yüzünde (+{hAbove:F2}m) 3B YAN KİRİŞ ZIVANASI açıldı! ([J] Tek/Çift Direk)";
                    return true;
                }

                if (p.Kind == RigidTimberPieceKind.Log && p.State == RigidTimberState.StaticAnchored && p.LengthM >= 1.6f)
                {
                    carp.EndSaddleNotchMask |= st.Sector;
                    RebuildPieceMeshVisual(idx);
                    feedback = "🪵 Yatay kütük ucunda 3B EYER KERTİĞİ (Saddle Notch) açıldı! 90° çapraz kütük bindirebilirsiniz.";
                    return true;
                }

                feedback = "🏗️ Yan Kiriş Yuvası için dikili direğe, Eyer Kertiği için yatay kütük ucuna bakın!";
                return true;
            }

            // Kip 3: Yapi Kesim & Cati Ustu Fazla Direk Ucu Budama
            if (st.IsSafePostTopTrim && TryTrimStandingPostExcessTop(idx, st.HitWorldPt.y, out salvagedMaterialId, out salvagedCount, out feedback))
            {
                return true;
            }

            if (p.State == RigidTimberState.StaticAnchored)
            {
                float dmgStep = p.Kind == RigidTimberPieceKind.Post ? 0.36f : 0.52f;
                p.Damage01 += dmgStep;
                if (p.Damage01 < 1.0f)
                {
                    int pct = Mathf.Clamp(Mathf.RoundToInt(p.Damage01 * 100f), 1, 99);
                    feedback = $"🪚 {p.Kind} kesiliyor (%{pct}) — devam etmek için aynı halkaya tekrar vurun!";
                    return true;
                }

                uint hitId = p.PieceId;
                for (int b = 0; b < _bondCount; b++)
                {
                    if (_bonds[b].PieceA == hitId || _bonds[b].PieceB == hitId) _bonds[b].IsActive = 0;
                }
                bool ok = TrySplitPieceAtWorldPoint(idx, st.HitWorldPt, worldChunks, out salvagedMaterialId, out salvagedCount, out feedback);
                _needsStaticReeval = true;
                ReevaluateStaticEquilibrium(worldChunks);
                return ok;
            }

            return TrySplitPieceAtWorldPoint(idx, st.HitWorldPt, worldChunks, out salvagedMaterialId, out salvagedCount, out feedback);
        }

        private byte ResolvePostSectorSmart(int postIdx, Vector3 postCenter, Quaternion postRot, ref Vector3 radialWorld, bool preferUngrooved, byte grooveMask)
        {
            Vector2 aimDir2 = new Vector2(radialWorld.x, radialWorld.z);
            if (aimDir2.sqrMagnitude < 1e-5f) aimDir2 = Vector2.up;
            aimDir2.Normalize();

            int bestNeighbor = -1;
            float bestScore = -1f;
            for (int j = 0; j < _pieceCount; j++)
            {
                if (j == postIdx || _pieceRemoved[j] != 0 || _pieces[j].State != RigidTimberState.StaticAnchored || _pieces[j].Kind != RigidTimberPieceKind.Post)
                    continue;
                Vector2 diff = new Vector2(_pieces[j].Position.X - postCenter.x, _pieces[j].Position.Z - postCenter.z);
                float dist = diff.magnitude;
                if (dist < 1.05f || dist > 14.5f) continue;
                Vector2 dirToN = diff / dist;
                float dot = Vector2.Dot(aimDir2, dirToN);
                if (dot < 0.45f) continue; // ~63 derece komsu direk yakalama konisi

                byte candSec = (byte)WorldDirToLocalOctantSector(postRot, new Vector3(dirToN.x, 0f, dirToN.y));
                bool isUngrooved = (grooveMask & (1 << candSec)) == 0;
                float score = dot + (preferUngrooved && isUngrooved ? 0.30f : 0f) - dist * 0.01f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestNeighbor = j;
                }
            }

            if (bestNeighbor >= 0)
            {
                radialWorld = new Vector3(_pieces[bestNeighbor].Position.X - postCenter.x, 0f, _pieces[bestNeighbor].Position.Z - postCenter.z).normalized;
            }
            return (byte)WorldDirToLocalOctantSector(postRot, radialWorld);
        }

        /// <summary>
        /// Faz M1 &amp; M2: Oyuncu elinde Balta varken bir direge/kutuge baktiginda (daha vurmadan!) tam ne olacagini HUD'da gosterir!
        /// </summary>
        public bool TryInspectAxeCarpentryTarget(
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            bool shiftHeld,
            bool altHeld,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out string hudHint)
        {
            hudHint = string.Empty;
            if (!TryRaycastAnyPiece(rayOrigin, rayDir, maxDist, out int idx, out Vector3 hitPt, out _, out _))
                return false;

            _soilSampler.WorldChunks = worldChunks;
            ref readonly RigidStructurePiece p = ref _pieces[idx];
            ref readonly PieceCarpentryProfile carp = ref _carpentryProfiles[idx];
            Quaternion rot = ToUnityQuat(p.Rotation);
            Vector3 center = ToUnityVec3(p.Position);
            Vector3 localHit = Quaternion.Inverse(rot) * (hitPt - center);

            if (p.Kind == RigidTimberPieceKind.Post && p.State == RigidTimberState.StaticAnchored && !altHeld)
            {
                Vector3 radialWorld = new Vector3(hitPt.x - center.x, 0f, hitPt.z - center.z);
                if (radialWorld.sqrMagnitude < 1e-4f) radialWorld = -rayDir;
                float surfY = SampleSurroundingRimSurfaceY(center.x, center.z, hitPt.y + 1.5f);
                float hAbove = hitPt.y - surfY;
                bool wantMortiseHint = shiftHeld || hAbove >= 2.45f;
                int sector = ResolvePostSectorSmart(idx, center, rot, ref radialWorld, preferUngrooved: !wantMortiseHint, carp.GrooveSectorMask);
                string dirLbl = GetSectorCompassLabel(radialWorld);

                bool alreadyGrooved = (carp.GrooveSectorMask & (1 << sector)) != 0;
                bool wantMortise = shiftHeld || (alreadyGrooved && hAbove >= 2.15f) || hAbove >= 2.45f;

                if (!wantMortise && !alreadyGrooved)
                {
                    int prog = (carp.PendingActionKind == 1 && carp.PendingSector == sector) ? carp.PendingHits : 0;
                    hudHint = $"🪓 ANA DİREK ({dirLbl} Yüzü): 3B Dikey Oluk Aç [{prog}/2] — [Sol Tık] Baltayla Oy | [Shift+Sol Tık] Yan Kiriş Yuvası";
                    return true;
                }
                if (wantMortise && !carp.TryFindMortiseNear(sector, localHit.y, 0.35f, out _))
                {
                    int prog = (carp.PendingActionKind == 2 && carp.PendingSector == sector) ? carp.PendingHits : 0;
                    hudHint = $"🪓 KİRİŞ HİZASI ({dirLbl} Yüzü, Yükseklik +{hAbove:F2}m): 3B Yan Kiriş Zıvanası Aç [{prog}/2] — [Sol Tık] Baltayla Oy";
                    return true;
                }

                float cutLow = (p.LengthM * 0.5f) + localHit.y;
                float cutHigh = (p.LengthM * 0.5f) - localHit.y;
                hudHint = $"✅ ANA DİREK ({dirLbl} Yüzü): Oluk/Zıvana Hazır! | Fazla üst ucu ({cutHigh:F2}m) kesmek için [Alt + Sol Tık x3]";
                return true;
            }

            if (p.Kind == RigidTimberPieceKind.Log)
            {
                float halfL = p.LengthM * 0.5f;
                if (!altHeld && p.State == RigidTimberState.StaticAnchored && Mathf.Abs(localHit.y) >= halfL - 0.60f && p.LengthM >= 1.8f)
                {
                    bool isNegEnd = localHit.y < 0f;
                    byte bit = isNegEnd ? (byte)1 : (byte)2;
                    if ((carp.EndSaddleNotchMask & bit) == 0)
                    {
                        byte actKind = isNegEnd ? (byte)3 : (byte)4;
                        int prog = carp.PendingActionKind == actKind ? carp.PendingHits : 0;
                        hudHint = $"🪓 YIĞMA KÜTÜK UCU: 3B Eyer Kertiği (Saddle Notch) Oy [{prog}/2] — [Sol Tık] Baltayla Oy | [V] Kip Değiştir";
                        return true;
                    }
                }
                float leftM = Mathf.Clamp(halfL + localHit.y, 0f, p.LengthM);
                float rightM = Mathf.Max(0f, p.LengthM - leftM);
                hudHint = $"📏 KÜTÜK KESİM CETVELİ: {leftM:F2}m | {rightM:F2}m (Toplam {p.LengthM:F2}m) — [Sol Tık x3] Tam Buradan Kes";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Faz M2: Kamera nisangahi (<= 3.15m) uzerinden dikili direge 3B Dikey Oluk, Yan Kiris Zivanasi,
        /// yatan kutuge 3B Eyer Kertigi oyar veya 3 darbede tam bakilan metreden keser!
        /// </summary>
        public bool TryStrikeCarpentryWithAimedAxe(
            Vector3 rayOrigin,
            Vector3 rayDir,
            float maxDist,
            bool shiftHeld,
            bool altHeld,
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;

            if (!TryRaycastAnyPiece(rayOrigin, rayDir, maxDist, out int idx, out Vector3 hitPt, out _, out _))
                return false;

            _soilSampler.WorldChunks = worldChunks;
            ref RigidStructurePiece p = ref _pieces[idx];
            ref PieceCarpentryProfile carp = ref _carpentryProfiles[idx];
            Quaternion rot = ToUnityQuat(p.Rotation);
            Vector3 center = ToUnityVec3(p.Position);
            Vector3 localHit = Quaternion.Inverse(rot) * (hitPt - center);

            // 1) Dikili Ana Direk (Post): 3B Dikey Oluk (Groove) veya Yan Kiris Zivanasi (Side Mortise)!
            // DIKKAT: Normal [Sol Tik] (!altHeld) ASLA dikili ana tasiyici kolonu yanlislikla ikiye kesip binayi yikmaz!
            if (p.Kind == RigidTimberPieceKind.Post && p.State == RigidTimberState.StaticAnchored && !altHeld)
            {
                Vector3 radialWorld = new Vector3(hitPt.x - center.x, 0f, hitPt.z - center.z);
                if (radialWorld.sqrMagnitude < 1e-4f) radialWorld = -rayDir;
                float surfY = SampleSurroundingRimSurfaceY(center.x, center.z, hitPt.y + 1.5f);
                float hAbove = hitPt.y - surfY;
                bool wantMortiseCheck = shiftHeld || hAbove >= 2.45f;
                byte sector = ResolvePostSectorSmart(idx, center, rot, ref radialWorld, preferUngrooved: !wantMortiseCheck, carp.GrooveSectorMask);
                string dirLbl = GetSectorCompassLabel(radialWorld);

                bool alreadyGrooved = (carp.GrooveSectorMask & (1 << sector)) != 0;
                bool wantMortise = shiftHeld || (alreadyGrooved && hAbove >= 2.15f) || hAbove >= 2.45f;

                if (!wantMortise && !alreadyGrooved)
                {
                    if (carp.PendingActionKind != 1 || carp.PendingSector != sector)
                    {
                        carp.PendingActionKind = 1;
                        carp.PendingSector = sector;
                        carp.PendingHits = 1;
                        feedback = $"🪓 {dirLbl} yüzüne dikey oluk çizildi [1/2]! Aynı yüze 1 kez daha vurup oluğu açın.";
                        return true;
                    }
                    carp.GrooveSectorMask |= (byte)(1 << sector);
                    carp.PendingHits = 0;
                    carp.PendingActionKind = 0;
                    RebuildPieceMeshVisual(idx);
                    feedback = $"🪵 Ana Direğin {dirLbl} yüzünde GERÇEK 3B DİKEY OLUK (Groove) oyuldu! Karşı direğin bakan yüzüne de oluk açıp kütük sürebilirsiniz.";
                    return true;
                }

                if (wantMortise && !carp.TryFindMortiseNear(sector, localHit.y, 0.35f, out _))
                {
                    if (carp.PendingActionKind != 2 || carp.PendingSector != sector)
                    {
                        carp.PendingActionKind = 2;
                        carp.PendingSector = sector;
                        carp.PendingHits = 1;
                        feedback = $"🪓 {dirLbl} yüzünde (+{hAbove:F2}m) Yan Kiriş Yuvası işaretlendi [1/2]! 1 vuruş daha yapın.";
                        return true;
                    }
                    carp.AddOrUpdateMortise(sector, localHit.y);
                    carp.PendingHits = 0;
                    carp.PendingActionKind = 0;
                    RebuildPieceMeshVisual(idx);
                    feedback = $"🏗️ Ana Direğin {dirLbl} yüzünde (+{hAbove:F2}m) 3B YAN KİRİŞ ZIVANASI açıldı! Karşı direğe de aynı hizada açıp Ana Kiriş oturtabilirsiniz.";
                    return true;
                }

                feedback = $"✅ Ana Direğin {dirLbl} yüzünde 3B Oluk/Zıvana zaten hazır! (Direği kesmek veya kısaltmak isterseniz [Alt + Sol Tık] kullanın)";
                return true;
            }

            // 2) Yatay Kutuk (Log): Mod A Canti icin ucta 3B Eyer Kertigi (Saddle Notch)!
            if (p.Kind == RigidTimberPieceKind.Log && p.State == RigidTimberState.StaticAnchored && !altHeld && p.LengthM >= 1.8f)
            {
                float halfL = p.LengthM * 0.5f;
                if (Mathf.Abs(localHit.y) >= halfL - 0.60f)
                {
                    bool isNegEnd = localHit.y < 0f;
                    byte bit = isNegEnd ? (byte)1 : (byte)2;
                    if ((carp.EndSaddleNotchMask & bit) == 0)
                    {
                        byte actKind = isNegEnd ? (byte)3 : (byte)4;
                        if (carp.PendingActionKind != actKind)
                        {
                            carp.PendingActionKind = actKind;
                            carp.PendingHits = 1;
                            feedback = "🪓 Kütük ucuna Eyer Kertiği işaretlendi [1/2]! 1 kez daha vurup 3B yarım ay oyuğu açın.";
                            return true;
                        }
                        carp.EndSaddleNotchMask |= bit;
                        carp.PendingHits = 0;
                        carp.PendingActionKind = 0;
                        RebuildPieceMeshVisual(idx);
                        feedback = "🪵 Kütük ucunda GERÇEK 3B EYER KERTİĞİ (Saddle Notch) açıldı! Üst sırayı 90° çapraz olarak bu oyuğa oturtabilirsiniz.";
                        return true;
                    }
                }
            }

            // 3) Kalan durumlarda ([Alt] basiliyken fazla direk ucu kesme veya kolon/duvar kesme): 3 darbede tam bakilan noktadan kes!
            if (p.State == RigidTimberState.StaticAnchored)
            {
                p.Damage01 += 0.36f;
                if (p.Damage01 < 1.0f)
                {
                    int pct = Mathf.Clamp(Mathf.RoundToInt(p.Damage01 * 100f), 1, 99);
                    feedback = $"🪓 {p.Kind} baltayla kesiliyor (%{pct}) — 3. darbede tam baktığın metreden ayrılır!";
                    return true;
                }

                // Eger dikili bir Ana Diregin (Post) EN UST baglantisinin uzerindeki fazla cati ustu ucu kesiliyorsa,
                // alt katlarin oluk/zivana baglarini KOPARMADAN yalnizca ust fazlaligi kesip dusur!
                if (p.Kind == RigidTimberPieceKind.Post && TryTrimStandingPostExcessTop(idx, hitPt.y, out salvagedMaterialId, out salvagedCount, out feedback))
                {
                    return true;
                }

                uint hitId = p.PieceId;
                for (int b = 0; b < _bondCount; b++)
                {
                    if (_bonds[b].PieceA == hitId || _bonds[b].PieceB == hitId) _bonds[b].IsActive = 0;
                }
                bool ok = TrySplitPieceAtWorldPoint(idx, hitPt, worldChunks, out salvagedMaterialId, out salvagedCount, out feedback);
                _needsStaticReeval = true;
                ReevaluateStaticEquilibrium(worldChunks);
                return ok;
            }

            return TrySplitPieceAtWorldPoint(idx, hitPt, worldChunks, out salvagedMaterialId, out salvagedCount, out feedback);
        }

        public bool TryTrimStandingPostExcessTop(
            int postIdx,
            float cutWorldY,
            out byte salvagedMaterialId,
            out int salvagedCount,
            out string feedback)
        {
            salvagedMaterialId = MaterialIdCatalog.OakWood;
            salvagedCount = 0;
            feedback = string.Empty;
            if ((uint)postIdx >= (uint)_pieceCount || _pieceRemoved[postIdx] != 0) return false;
            ref RigidStructurePiece p = ref _pieces[postIdx];
            if (p.Kind != RigidTimberPieceKind.Post || p.State != RigidTimberState.StaticAnchored) return false;

            float oldCenterY = p.Position.Y;
            float oldHalfL = p.LengthM * 0.5f;
            float footY = oldCenterY - oldHalfL;
            float topY = oldCenterY + oldHalfL;
            if (cutWorldY <= footY + 1.8f || cutWorldY >= topY - 0.15f) return false;

            // En yuksek aktif baglanti noktasini bul: eger kesim noktasi bir kiris/duvar baglantisinin ALTINDAYSA bu ust budama degil, kolon yikimidir!
            uint pid = p.PieceId;
            float highestBondWorldY = footY + 1.5f;
            for (int b = 0; b < _bondCount; b++)
            {
                if (_bonds[b].IsActive == 0) continue;
                if (_bonds[b].PieceA == pid)
                {
                    float by = oldCenterY + (_bonds[b].LocalA0.Y + _bonds[b].LocalA2.Y) * 0.5f;
                    if (by > highestBondWorldY) highestBondWorldY = by;
                }
                else if (_bonds[b].PieceB == pid)
                {
                    float by = oldCenterY + (_bonds[b].LocalB0.Y + _bonds[b].LocalB2.Y) * 0.5f;
                    if (by > highestBondWorldY) highestBondWorldY = by;
                }
            }
            if (cutWorldY < highestBondWorldY + 0.10f) return false;

            float newLen = cutWorldY - footY;
            float excessLen = topY - cutWorldY;
            float newCenterY = footY + newLen * 0.5f;
            float deltaCenterY = newCenterY - oldCenterY; // negatif kayma

            // Mevcut tum baglarin bu direk uzerindeki yerel Y koordinatlarini yeni merkeze gore kaydir (0 bag kopmasi!)
            SysVec3 shift = new SysVec3(0f, -deltaCenterY, 0f);
            for (int b = 0; b < _bondCount; b++)
            {
                if (_bonds[b].IsActive == 0) continue;
                if (_bonds[b].PieceA == pid)
                {
                    _bonds[b].LocalA0 += shift;
                    _bonds[b].LocalA1 += shift;
                    _bonds[b].LocalA2 += shift;
                    _bonds[b].LocalA3 += shift;
                }
                else if (_bonds[b].PieceB == pid)
                {
                    _bonds[b].LocalB0 += shift;
                    _bonds[b].LocalB1 += shift;
                    _bonds[b].LocalB2 += shift;
                    _bonds[b].LocalB3 += shift;
                }
            }

            ref PieceCarpentryProfile carp = ref _carpentryProfiles[postIdx];
            carp.ShiftAllMortisesLocalY(-deltaCenterY);

            p.LengthM = newLen;
            p.MassKg = p.MassKg * (newLen / Mathf.Max(0.5f, newLen + excessLen));
            p.Position = new SysVec3(p.Position.X, newCenterY, p.Position.Z);
            p.Damage01 = 0f;
            _pieceHalfExtents[postIdx] = new Vector3(p.DiameterM * 0.5f, newLen * 0.5f, p.DiameterM * 0.5f);
            if (_pieceVisuals[postIdx] != null)
            {
                _pieceVisuals[postIdx]!.transform.position = ToUnityVec3(p.Position);
            }
            RebuildPieceMeshVisual(postIdx);

            salvagedCount = Mathf.Clamp(Mathf.RoundToInt(excessLen), 1, 4);
            feedback = $"🪚 Ana direğin çatı üstü fazlalığı ({excessLen:F2}m) çatı hizasından sıfıra sıfır kesildi (+{salvagedCount}x Odun)! Alt taşıyıcı bağlar %100 korundu.";
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
            EmitTri(dst, a, b, c, n, matId);
            EmitTri(dst, a, c, d, n, matId);
        }

        private static void EmitTri(List<FacetedVoxelTriangle> dst, Vector3 a, Vector3 b, Vector3 c, Vector3 n, byte matId)
        {
            dst.Add(new FacetedVoxelTriangle
            {
                V0 = new SysVec3(a.x, a.y, a.z),
                V1 = new SysVec3(b.x, b.y, b.z),
                V2 = new SysVec3(c.x, c.y, c.z),
                Normal = new SysVec3(n.x, n.y, n.z),
                MaterialId = matId,
                Temperature = 0,
                FacetShade = 215,
                AmbientOcclusion = 255
            });
        }

        private bool TryRaycastPieceObb(int idx, Vector3 rayOrigin, Vector3 rayDirNorm, float maxDist, out float hitDist)
        {
            hitDist = maxDist;
            Vector3 center = ToUnityVec3(_pieces[idx].Position);
            Quaternion invRot = Quaternion.Inverse(ToUnityQuat(_pieces[idx].Rotation));
            Vector3 origL = invRot * (rayOrigin - center);
            Vector3 dirL = invRot * rayDirNorm;
            Vector3 half = _pieceHalfExtents[idx] + new Vector3(0.14f, 0.14f, 0.14f);

            float tMin = 0.05f;
            float tMax = maxDist;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = origL[axis];
                float d = dirL[axis];
                float h = half[axis];
                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (o < -h || o > h) return false;
                }
                else
                {
                    float invD = 1.0f / d;
                    float t1 = (-h - o) * invD;
                    float t2 = (h - o) * invD;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    if (t1 > tMin) tMin = t1;
                    if (t2 < tMax) tMax = t2;
                    if (tMin > tMax) return false;
                }
            }

            hitDist = tMin;
            return true;
        }

        public bool HasStaticPieceMatchingBlueprint(Vector3 blueprintCenter, bool expectPost, float toleranceM = 0.65f)
        {
            float tolSq = toleranceM * toleranceM;
            for (int i = 0; i < _pieceCount; i++)
            {
                ref readonly RigidStructurePiece p = ref _pieces[i];
                if (p.State != RigidTimberState.StaticAnchored) continue;
                bool isPost = p.Kind == RigidTimberPieceKind.Post;
                if (expectPost != isPost) continue;

                Vector3 pos = ToUnityVec3(p.Position);
                if (expectPost)
                {
                    float dx = pos.x - blueprintCenter.x;
                    float dz = pos.z - blueprintCenter.z;
                    if (dx * dx + dz * dz <= tolSq) return true;
                }
                else
                {
                    if ((pos - blueprintCenter).sqrMagnitude <= tolSq * 1.4f) return true;
                }
            }
            return false;
        }

        private static SysVec3 ToSysVec3(Vector3 v) => new SysVec3(v.x, v.y, v.z);
        private static Vector3 ToUnityVec3(SysVec3 v) => new Vector3(v.X, v.Y, v.Z);
        private static SysQuat ToSysQuat(Quaternion q) => new SysQuat(q.x, q.y, q.z, q.w);
        private static Quaternion ToUnityQuat(SysQuat q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
#endif
