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
            int startY = Mathf.Clamp(Mathf.FloorToInt(referenceY) + 1, 1, 254);

            for (int y = startY; y >= 1; y--)
            {
                byte mat = VoxelBitUtility.GetMaterialId(voxels[(y << 8) | (lz << 4) | lx]);
                if (mat != MaterialIdCatalog.Air && mat != MaterialIdCatalog.FiniteWaterSurface && mat != MaterialIdCatalog.FiniteLavaSurface)
                {
                    groundSurfaceY = y + 1.0f;
                    topMaterialId = mat;
                    return true;
                }
            }

            return false;
        }

        public float SampleSurfaceHeightY(float worldX, float worldZ, float fallbackY)
        {
            return TrySampleGroundSurfaceY(worldX, worldZ, fallbackY + 8.0f, out float gy, out _) ? gy : fallbackY;
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

        private RigidStructurePiece[] _pieces = new RigidStructurePiece[128];
        private SoilEmbedProfile[] _soilProfiles = new SoilEmbedProfile[128];
        private CornerBond4Interface[] _bonds = new CornerBond4Interface[256];
        private GameObject?[] _pieceVisuals = new GameObject?[128];
        private Vector3[] _pieceHalfExtents = new Vector3[128];
        private byte[] _pieceMaterialIds = new byte[128];
        private float[] _prevVerticalVel = new float[128];
        private byte[] _pieceRemoved = new byte[128];
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
            out string feedback)
        {
            _soilSampler.WorldChunks = worldChunks;
            TipInEvaluation eval = TimberTipInAndJoineryLogic.EvaluateTipInHole(targetPos.x, targetPos.z, targetPos.y, _soilSampler);
            float realDiam = Mathf.Clamp(diameterM, 0.44f, 0.74f);
            float realLen = Mathf.Clamp(lengthM, 2.5f, 18.0f);
            float centerY = eval.PitBottomY + realLen * 0.5f;

            int islandId = _nextIslandId++;
            int idx = AddCylinderPieceInternal(
                RigidTimberPieceKind.Post,
                species,
                realLen,
                realDiam,
                new SysVec3(targetPos.x, centerY, targetPos.z),
                SysQuat.Identity,
                islandId,
                MaterialIdCatalog.OakWood,
                sharedVoxelMat);

            _soilProfiles[idx] = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[idx], _soilSampler);

            if (eval.Result == TipInPlacementResult.NoHoleFlatGroundTipsOver)
            {
                // Duz zeminde koca kutuk dik duramaz -> hemen devrilir!
                _pieces[idx].State = RigidTimberState.DynamicActive;
                _pieces[idx].AngularVelocity = new SysVec3(0.45f, 0f, 0.25f);
                _hasDynamicPieces = true;
                feedback = $"⚠️ Çukur kazmadan {realLen:0.1}m kütük dik duramaz (çukur derinliği: {eval.HoleDepthMeters:0.2}m)! Önce kürekle 1.5m çukur kazın.";
                return true;
            }

            if (eval.Result == TipInPlacementResult.TemporaryHoleHeldNeedsBackfill)
            {
                // Cukur duvarlari gecici olarak dik tutuyor; oyuncu [V] ile toprak doldurunca kalici kilitlenecek!
                _pieces[idx].Flags |= 2; // Gecici cukur tutusu
                feedback = $"🪵 {realLen:0.1}m kütük çukura dikildi (derinlik {eval.HoleDepthMeters:0.2}m)! Şimdi [V] ile etrafını toprakla doldurup sıkıştırarak kalıcı kilitleyin!";
                return true;
            }

            _needsStaticReeval = true;
            ReevaluateStaticEquilibrium(worldChunks);
            feedback = $"🪵 {realLen:0.1}m kütük derin çukura ({eval.HoleDepthMeters:0.2}m) oturtuldu! Etrafını [V] toprak dolgusuyla sıkıştırarak tam taşıyıcı hale getirin.";
            return true;
        }

        /// <summary>
        /// Asama 1.2: Kendi kestigin kutugu yatay duvar sirasi veya iki sutun arasina kiris olarak yerlestirir.
        /// Baslangicta FrictionOnly (kertiksiz) olarak oturur -> baltayla fiziksel vurunca SaddleNotch/Dovetail kilitlenir!
        /// </summary>
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
            _soilSampler.WorldChunks = worldChunks;
            float realLen = Mathf.Clamp(lengthM, 2.0f, 12.0f);
            float realDiam = Mathf.Clamp(diameterM, 0.36f, 0.62f);

            // Yakindaki destek sutun/duvar parcasini bul (akilli soket yakalama)
            int supportA = -1, supportB = -1;
            int snapIsland = -1;
            Vector3 placePos = targetPos;
            for (int i = 0; i < _pieceCount; i++)
            {
                if (_pieceRemoved[i] != 0 || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                Vector3 p = ToUnityVec3(_pieces[i].Position);
                float horizDist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(targetPos.x, targetPos.z));
                if (horizDist <= realLen * 0.65f + 0.8f)
                {
                    if (supportA < 0)
                    {
                        supportA = i;
                        snapIsland = _pieces[i].IslandId;
                    }
                    else if (supportB < 0 && _pieces[i].IslandId == snapIsland)
                    {
                        supportB = i;
                        break;
                    }
                }
            }

            int islandId = snapIsland > 0 ? snapIsland : _nextIslandId++;
            Quaternion qHoriz = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.Euler(0f, 0f, 90f);
            if (supportA < 0)
            {
                float gy = _soilSampler.SampleSurfaceHeightY(targetPos.x, targetPos.z, targetPos.y);
                placePos.y = gy + realDiam * 0.5f;
            }

            int idx = AddCylinderPieceInternal(
                RigidTimberPieceKind.Log,
                species,
                realLen,
                realDiam,
                ToSysVec3(placePos),
                ToSysQuat(qHoriz),
                islandId,
                MaterialIdCatalog.ChinkedLogBlock,
                sharedVoxelMat);

            if (supportA >= 0)
            {
                AddBondInternal(supportA, idx, TimberJoineryKind.FrictionOnly, realDiam, ToSysVec3(placePos));
            }
            if (supportB >= 0)
            {
                AddBondInternal(supportB, idx, TimberJoineryKind.FrictionOnly, realDiam, ToSysVec3(placePos));
            }

            _needsStaticReeval = true;
            ReevaluateStaticEquilibrium(worldChunks);
            feedback = supportA >= 0
                ? $"🪵 Yatay kütük ({realLen:0.1}m) KERTİKSİZ (FrictionOnly) yerleştirildi! Baltayla fiziksel vurup Eyer/Kırlangıç kertiği açarak kilitleyin!"
                : $"🪵 Yatay kütük ({realLen:0.1}m) zemine bırakıldı (üst üste dizip baltayla kertik açabilirsiniz).";
            return true;
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

            feedback = "🏠 Gerçek 3 Katlı Tek-Parça Ahşap Ev (7.6×6.4m, kalın kütük duvarlı, döşemeli, merdivenli ve çatılı) kuruldu! İçine yürüyüp 3. kata çıkabilir, [Alt+L] ile parça kesebilirsiniz!";
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

                    // 1) Ust yuzeyde yurume & merdiven basamagi cikma (Step-Up <= 0.46m)
                    bool insideXZ = Mathf.Abs(feetLocal.x) <= half.x + playerRadius * 0.45f &&
                                    Mathf.Abs(feetLocal.z) <= half.z + playerRadius * 0.45f;
                    if (insideXZ)
                    {
                        float topYLocal = half.y;
                        float distFromTop = feetLocal.y - topYLocal;
                        if (distFromTop >= -0.46f && distFromTop <= 0.14f && verticalVelocity <= 0.65f)
                        {
                            Vector3 worldUp = rot * Vector3.up;
                            if (worldUp.y >= 0.72f)
                            {
                                Vector3 snappedLocal = new Vector3(feetLocal.x, topYLocal + 0.01f, feetLocal.z);
                                Vector3 snappedWorld = center + (rot * snappedLocal);
                                if (snappedWorld.y >= pPos.y - 0.15f)
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

                    // 2) Yan duvar / sutun kati carpismasi (hem alt govde hem ust govde testi!)
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
                feedback = $"⚠️ Bu parça elle cebe sığmaz ({piece.LengthM:0.1}m, {piece.MassKg:0} kg)! Baltayla/kazmayla vurup parçalayın veya Kütük Arabasıyla ([K]) taşıyın!";
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

            RigidStructurePiece piece = _pieces[bestIdx];
            bool isLarge = piece.LengthM > 2.2f || (_pieceHalfExtents[bestIdx].x * 2f > 2.2f) || (_pieceHalfExtents[bestIdx].z * 2f > 2.2f);
            if (isLarge && TrySplitPieceManuallyInHalf(bestIdx))
            {
                _hasDynamicPieces = true;
                feedback = $"🪓 Ağır parça ({piece.LengthM:0.1}m) baltayla ikiye bölündü! Küçülen parçaları [E] ile toplayabilir veya tekrar bölebilirsiniz.";
                return true;
            }

            // Zaten kucuk parca: baltayla kirilip odun/kalas kaynagina donusur
            salvagedMaterialId = piece.Kind == RigidTimberPieceKind.Plank ? MaterialIdCatalog.WoodPlank : MaterialIdCatalog.OakWood;
            salvagedCount = 1;
            _pieceRemoved[bestIdx] = 1;
            if (_pieceVisuals[bestIdx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[bestIdx]);
                _pieceVisuals[bestIdx] = null;
            }
            string itemName = salvagedMaterialId == MaterialIdCatalog.WoodPlank ? "Ahşap Kalas" : "Meşe Odunu";
            feedback = $"🪓 Parça kırılıp toplandı (+1 {itemName}).";
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

            // A) AYAKTAKI YAPI PARCASINA FIZIKSEL BALTA DARBESI!
            if (_pieces[bestIdx].State == RigidTimberState.StaticAnchored)
            {
                uint hitPieceId = _pieces[bestIdx].PieceId;
                // Eger bu parcanin kertiksiz (FrictionOnly) bir bagi varsa -> ilk vurusla KERTIK ACIP KILITLE!
                for (int b = 0; b < _bondCount; b++)
                {
                    if (_bonds[b].IsActive != 0 &&
                        (_bonds[b].PieceA == hitPieceId || _bonds[b].PieceB == hitPieceId) &&
                        _bonds[b].Joinery == TimberJoineryKind.FrictionOnly)
                    {
                        if (TimberTipInAndJoineryLogic.UpgradeJoineryWithAxeNotch(ref _bonds[b], out TimberJoineryKind upgraded))
                        {
                            feedback = $"🪓 Balta vuruşuyla {upgraded} KERTİĞİ açılıp kilitlendi! (Kesmek için tekrar vurun)";
                            return true;
                        }
                    }
                }

                // Aksi halde ayaktaki sutun/duvar/kiris hasar alir -> 3 darbede kesilir ve ev gercek zamanli yikilir!
                _pieces[bestIdx].Damage01 += 0.36f;
                if (_pieces[bestIdx].Damage01 < 1.0f)
                {
                    int pct = Mathf.Clamp(Mathf.RoundToInt(_pieces[bestIdx].Damage01 * 100f), 1, 99);
                    feedback = $"🪓 {_pieces[bestIdx].Kind} baltayla kesiliyor (Hasar: %{pct}) — kırılınca yapı çöker!";
                    return true;
                }

                // 3. darbe -> parca kesildi!
                RigidTimberPieceKind cutKind = _pieces[bestIdx].Kind;
                _pieceRemoved[bestIdx] = 1;
                _pieces[bestIdx].State = RigidTimberState.StaticLooseEntity;
                if (_pieceVisuals[bestIdx] != null)
                {
                    UnityEngine.Object.Destroy(_pieceVisuals[bestIdx]);
                    _pieceVisuals[bestIdx] = null;
                }
                for (int b = 0; b < _bondCount; b++)
                {
                    if (_bonds[b].PieceA == hitPieceId || _bonds[b].PieceB == hitPieceId)
                    {
                        _bonds[b].IsActive = 0;
                    }
                }
                _needsStaticReeval = true;
                ReevaluateStaticEquilibrium(worldChunks);
                feedback = $"💥 {cutKind} fiziksel balta darbesiyle KESİLDİ! Yapısal denge yeniden çözülüyor!";
                return true;
            }

            // B) YERDE YATAN YIKINTI PARCASINA FIZIKSEL BALTA DARBESI!
            RigidStructurePiece piece = _pieces[bestIdx];
            bool isLarge = piece.LengthM > 2.2f || (_pieceHalfExtents[bestIdx].x * 2f > 2.2f) || (_pieceHalfExtents[bestIdx].z * 2f > 2.2f);
            if (isLarge && TrySplitPieceManuallyInHalf(bestIdx))
            {
                _hasDynamicPieces = true;
                feedback = $"🪓 Fiziksel balta darbesiyle parça ({piece.LengthM:0.1}m) ikiye bölündü!";
                return true;
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
            feedback = $"🪓 Parça fiziksel darbeyle kırılıp toplandı (+1 {itemName}).";
            return true;
        }

        private bool TrySplitPieceManuallyInHalf(int idx)
        {
            if (idx < 0 || idx >= _pieceCount || _pieceRemoved[idx] != 0) return false;
            if (_pieceCount >= MaxActivePiecesCap + 40) return false;
            RigidStructurePiece orig = _pieces[idx];

            byte matId = _pieceMaterialIds[idx] != 0 ? _pieceMaterialIds[idx] : MaterialIdCatalog.OakWood;
            Vector3 halfExt = _pieceHalfExtents[idx];
            bool isBox = halfExt.x > orig.DiameterM * 0.65f || halfExt.z > orig.DiameterM * 0.65f;

            _pieceRemoved[idx] = 1;
            if (_pieceVisuals[idx] != null)
            {
                UnityEngine.Object.Destroy(_pieceVisuals[idx]);
                _pieceVisuals[idx] = null;
            }

            Quaternion uRot = ToUnityQuat(orig.Rotation);
            bool splitAlongX = isBox && halfExt.x >= halfExt.z;
            Vector3 splitAxis = isBox ? (uRot * (splitAlongX ? Vector3.right : Vector3.forward)) : (uRot * Vector3.up);
            float offset = isBox ? ((splitAlongX ? halfExt.x : halfExt.z) * 0.52f) : (orig.LengthM * 0.26f);

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 childPos = ToUnityVec3(orig.Position) + splitAxis * (s * offset) + Vector3.up * 0.12f;
                int cIdx;
                if (isBox)
                {
                    Vector3 childSize = splitAlongX
                        ? new Vector3(halfExt.x * 0.96f, halfExt.y * 2f, halfExt.z * 2f)
                        : new Vector3(halfExt.x * 2f, halfExt.y * 2f, halfExt.z * 0.96f);
                    cIdx = AddBoxPieceInternal(orig.Kind, orig.Species, childSize, ToSysVec3(childPos), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
                }
                else
                {
                    cIdx = AddCylinderPieceInternal(orig.Kind, orig.Species, orig.LengthM * 0.48f, orig.DiameterM, ToSysVec3(childPos), orig.Rotation, orig.IslandId, matId, _lastSharedVoxelMat);
                }
                _pieces[cIdx].SplitParentId = orig.PieceId;
                _pieces[cIdx].State = RigidTimberState.DynamicActive;
                _pieces[cIdx].LinearVelocity = ToSysVec3(splitAxis * (s * 0.65f) + Vector3.up * 0.35f);
            }
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
                    if (_pieceRemoved[i] != 0 || _pieces[i].Kind != RigidTimberPieceKind.Post || _pieces[i].State != RigidTimberState.StaticAnchored) continue;
                    float prevD = _soilProfiles[i].EmbedDepthM;
                    SoilEmbedProfile fresh = SoilEmbedmentEvaluator.SampleAndBuildProfile(in _pieces[i], _soilSampler, compactionFactor: 0.92f, hasStoneCollar: true);
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
            EmitLogPrism(_meshScratch, r, -halfL, halfL, matId);

            Mesh mesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_meshScratch, $"TimberMesh_{piece.PieceId}");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (sharedVoxelMat != null) mr.sharedMaterial = sharedVoxelMat;
            return go;
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

        private static void EmitLogPrism(List<FacetedVoxelTriangle> dst, float radius, float yMin, float yMax, byte matId)
        {
            const int sides = 8;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;
                float aMid = (a0 + a1) * 0.5f;
                Vector3 b0 = new Vector3(Mathf.Cos(a0) * radius, yMin, Mathf.Sin(a0) * radius);
                Vector3 b1 = new Vector3(Mathf.Cos(a1) * radius, yMin, Mathf.Sin(a1) * radius);
                Vector3 t0 = new Vector3(b0.x, yMax, b0.z);
                Vector3 t1 = new Vector3(b1.x, yMax, b1.z);
                Vector3 nOut = new Vector3(Mathf.Cos(aMid), 0f, Mathf.Sin(aMid));

                EmitQuad(dst, b0, t0, t1, b1, nOut, matId);
                EmitTri(dst, new Vector3(0f, yMax, 0f), t1, t0, Vector3.up, MaterialIdCatalog.WoodPlank);
                EmitTri(dst, new Vector3(0f, yMin, 0f), b0, b1, Vector3.down, MaterialIdCatalog.WoodPlank);
            }
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

        private static SysVec3 ToSysVec3(Vector3 v) => new SysVec3(v.x, v.y, v.z);
        private static Vector3 ToUnityVec3(SysVec3 v) => new Vector3(v.X, v.Y, v.Z);
        private static SysQuat ToSysQuat(Quaternion q) => new SysQuat(q.x, q.y, q.z, q.w);
        private static Quaternion ToUnityQuat(SysQuat q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
#endif
