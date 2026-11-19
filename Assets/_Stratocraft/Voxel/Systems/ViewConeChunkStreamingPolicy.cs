#nullable enable
using System;
using System.Collections.Generic;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Paket 4C-2: 3-Bolgeli Gorus Konisi (Frustum) Fiziksel Chunk Yukleme Konfigurasyonu.
    /// - Zone 1 (InnerCoreRadiusChunks=3, 7x7=49 chunk): 360 derece fizik/hareket/sivi guvenlik cekirdegi.
    /// - Zone 2 (View-Cone): Kamera yatay FOV + marj icindeki gorunur chunk'lar tam SurfaceNets mesh'lenir.
    /// - +1 Data Apron: SurfaceNets sinir catlagi (0 crack) icin yalnizca veri yuklenir, mesh URETILMEZ.
    /// </summary>
    public struct ViewConeConfig
    {
        public int InnerCoreRadiusChunks;
        public int ActiveOuterRadiusChunks;
        public float VerticalFovDegrees;
        public float AspectRatio;
        public float LoadHalfAngleMarginDeg;
        public float KeepHysteresisAngleDeg;
        public float GraceUnloadSeconds;
        public bool UseEuclideanOuterRadius;
        public bool IncludeDataApronRing;
        public bool ProtectOriginWorkshopCore;

        public static ViewConeConfig CreateDefault(
            int activeOuterRadiusChunks = 10,
            float verticalFovDegrees = 75.0f,
            float aspectRatio = 16.0f / 9.0f,
            bool protectOriginWorkshopCore = false)
        {
            return new ViewConeConfig
            {
                InnerCoreRadiusChunks = 3,
                ActiveOuterRadiusChunks = Math.Clamp(activeOuterRadiusChunks, 4, 16),
                VerticalFovDegrees = Math.Clamp(verticalFovDegrees, 45.0f, 120.0f),
                AspectRatio = Math.Clamp(aspectRatio, 1.0f, 3.0f),
                LoadHalfAngleMarginDeg = 12.0f,
                KeepHysteresisAngleDeg = 12.0f,
                GraceUnloadSeconds = 5.0f,
                UseEuclideanOuterRadius = true,
                IncludeDataApronRing = true,
                ProtectOriginWorkshopCore = protectOriginWorkshopCore
            };
        }
    }

    public enum ChunkConeMembership : byte
    {
        Outside = 0,
        DataApronOnly = 1,
        Zone2ViewCone = 2,
        Zone1InnerCore = 3
    }

    /// <summary>
    /// Saf C# (Unity bagimsiz, 0 B/kare GC) Gorus Konisi Chunk Yukleme &amp; Hiserezis Tahliye Politikasi.
    /// </summary>
    public sealed class ViewConeChunkStreamingPolicy
    {
        private const float DegToRad = MathF.PI / 180.0f;
        private const float RadToDeg = 180.0f / MathF.PI;

        private readonly Dictionary<(int cx, int cz), double> _outOfConeSinceSec = new(256);
        private readonly List<(int cx, int cz)> _reenteredScratch = new(64);
        private readonly List<((int cx, int cz) Key, float Score)> _candidateScratch = new(512);
        private readonly List<((int cx, int cz) Key, double Age)> _evictScratch = new(64);

        public int PendingGraceTimerCount => _outOfConeSinceSec.Count;

        public static float ComputeHorizontalHalfFovDegrees(float verticalFovDeg, float aspectRatio)
        {
            float halfVRad = Math.Clamp(verticalFovDeg, 30.0f, 150.0f) * 0.5f * DegToRad;
            float clampedAspect = Math.Clamp(aspectRatio, 1.0f, 3.0f);
            return MathF.Atan(MathF.Tan(halfVRad) * clampedAspect) * RadToDeg;
        }

        public static void ComputeCosThresholds(in ViewConeConfig cfg, out float cosLoad, out float cosKeep)
        {
            float halfHoriz = ComputeHorizontalHalfFovDegrees(cfg.VerticalFovDegrees, cfg.AspectRatio);
            float loadHalfDeg = Math.Clamp(halfHoriz + cfg.LoadHalfAngleMarginDeg, 58.0f, 76.0f);
            float keepHalfDeg = MathF.Min(88.0f, loadHalfDeg + MathF.Max(0.0f, cfg.KeepHysteresisAngleDeg));
            cosLoad = MathF.Cos(loadHalfDeg * DegToRad);
            cosKeep = MathF.Cos(keepHalfDeg * DegToRad);
        }

        public static bool IsProtectedOriginWorkshop(int cx, int cz)
        {
            return cx >= -2 && cx <= 1 && cz >= -2 && cz <= 1;
        }

        public static ChunkConeMembership ClassifyForLoad(
            int cx,
            int cz,
            int centerCx,
            int centerCz,
            float camFwdX,
            float camFwdZ,
            in ViewConeConfig cfg)
        {
            NormalizeForward2D(ref camFwdX, ref camFwdZ);
            ComputeCosThresholds(in cfg, out float cosLoad, out _);
            return ClassifyForLoadFast(cx, cz, centerCx, centerCz, camFwdX, camFwdZ, in cfg, cosLoad);
        }

        private static ChunkConeMembership ClassifyForLoadFast(
            int cx,
            int cz,
            int centerCx,
            int centerCz,
            float fwdX,
            float fwdZ,
            in ViewConeConfig cfg,
            float cosLoad)
        {
            if (IsInZone1OrZone2(cx, cz, centerCx, centerCz, fwdX, fwdZ, in cfg, cosLoad, outerPad: 0, out bool isInnerCore))
            {
                return isInnerCore ? ChunkConeMembership.Zone1InnerCore : ChunkConeMembership.Zone2ViewCone;
            }

            if (cfg.IncludeDataApronRing)
            {
                // +1 veri etegi: 8 komsudan herhangi biri Zone1 veya Zone2 icindeyse SurfaceNets catlaksizligi icin veri yuklenir (mesh URETILMEZ!)
                for (int oz = -1; oz <= 1; oz++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oz == 0) continue;
                        if (IsInZone1OrZone2(cx + ox, cz + oz, centerCx, centerCz, fwdX, fwdZ, in cfg, cosLoad, outerPad: 0, out _))
                        {
                            return ChunkConeMembership.DataApronOnly;
                        }
                    }
                }
            }

            return ChunkConeMembership.Outside;
        }

        public static bool ShouldKeepLoaded(
            int cx,
            int cz,
            int centerCx,
            int centerCz,
            float camFwdX,
            float camFwdZ,
            in ViewConeConfig cfg)
        {
            NormalizeForward2D(ref camFwdX, ref camFwdZ);
            ComputeCosThresholds(in cfg, out _, out float cosKeep);
            return ShouldKeepLoadedFast(cx, cz, centerCx, centerCz, camFwdX, camFwdZ, in cfg, cosKeep);
        }

        private static bool ShouldKeepLoadedFast(
            int cx,
            int cz,
            int centerCx,
            int centerCz,
            float fwdX,
            float fwdZ,
            in ViewConeConfig cfg,
            float cosKeep)
        {
            if (IsInZone1OrZone2(cx, cz, centerCx, centerCz, fwdX, fwdZ, in cfg, cosKeep, outerPad: 1, out _))
            {
                return true;
            }

            if (cfg.IncludeDataApronRing)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oz == 0) continue;
                        if (IsInZone1OrZone2(cx + ox, cz + oz, centerCx, centerCz, fwdX, fwdZ, in cfg, cosKeep, outerPad: 1, out _))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public void CollectPrioritizedSpawnCandidates(
            int centerCx,
            int centerCz,
            float camFwdX,
            float camFwdZ,
            in ViewConeConfig cfg,
            Func<int, int, bool> isAlreadyLoaded,
            List<(int cx, int cz)> outBatch,
            int maxBatchCount,
            int minChunkCoord = int.MinValue,
            int maxChunkCoord = int.MaxValue)
        {
            outBatch.Clear();
            if (maxBatchCount <= 0)
            {
                return;
            }

            NormalizeForward2D(ref camFwdX, ref camFwdZ);
            ComputeCosThresholds(in cfg, out float cosLoad, out _);

            _candidateScratch.Clear();
            int scanR = cfg.ActiveOuterRadiusChunks + (cfg.IncludeDataApronRing ? 1 : 0);

            for (int dz = -scanR; dz <= scanR; dz++)
            {
                int cz = centerCz + dz;
                if (cz < minChunkCoord || cz > maxChunkCoord) continue;
                for (int dx = -scanR; dx <= scanR; dx++)
                {
                    int cx = centerCx + dx;
                    if (cx < minChunkCoord || cx > maxChunkCoord) continue;
                    if (isAlreadyLoaded(cx, cz)) continue;

                    ChunkConeMembership m = ClassifyForLoadFast(cx, cz, centerCx, centerCz, camFwdX, camFwdZ, in cfg, cosLoad);
                    if (m == ChunkConeMembership.Outside)
                    {
                        continue;
                    }

                    // Oncelik puani (dusuk = once yukle): Zone1 cekirdek -> kamera merkezindeki yakin Zone2 -> kenar -> +1 apron
                    float distSq = dx * dx + dz * dz;
                    float dist = MathF.Sqrt(distSq);
                    float dot = dist > 1e-4f ? ((dx * camFwdX + dz * camFwdZ) / dist) : 1.0f;
                    float tierBias = m == ChunkConeMembership.Zone1InnerCore
                        ? 0.0f
                        : (m == ChunkConeMembership.Zone2ViewCone ? 100.0f : 350.0f);
                    float score = tierBias + dist * 4.0f - dot * 6.0f;
                    _candidateScratch.Add(((cx, cz), score));
                }
            }

            if (_candidateScratch.Count == 0)
            {
                return;
            }

            // Sifir-GC kismi secim (en dusuk maxBatchCount skoru al)
            int take = Math.Min(maxBatchCount, _candidateScratch.Count);
            for (int i = 0; i < take; i++)
            {
                int bestIdx = i;
                float bestScore = _candidateScratch[i].Score;
                for (int j = i + 1; j < _candidateScratch.Count; j++)
                {
                    if (_candidateScratch[j].Score < bestScore)
                    {
                        bestScore = _candidateScratch[j].Score;
                        bestIdx = j;
                    }
                }
                if (bestIdx != i)
                {
                    var tmp = _candidateScratch[i];
                    _candidateScratch[i] = _candidateScratch[bestIdx];
                    _candidateScratch[bestIdx] = tmp;
                }
                outBatch.Add(_candidateScratch[i].Key);
            }
        }

        public void EvaluateUnloadsWithGraceTimer(
            IEnumerable<(int cx, int cz)> loadedKeys,
            int centerCx,
            int centerCz,
            float camFwdX,
            float camFwdZ,
            in ViewConeConfig cfg,
            double nowSeconds,
            List<(int cx, int cz)> outReadyToUnload,
            int maxUnloadsThisFrame,
            Func<int, int, bool>? isBlockedByInFlightJob = null,
            int softActiveChunkCap = 380)
        {
            outReadyToUnload.Clear();
            if (maxUnloadsThisFrame <= 0)
            {
                return;
            }

            NormalizeForward2D(ref camFwdX, ref camFwdZ);
            ComputeCosThresholds(in cfg, out _, out float cosKeep);

            _reenteredScratch.Clear();
            _evictScratch.Clear();

            int loadedCount = 0;
            foreach (var key in loadedKeys)
            {
                loadedCount++;
                if (ShouldKeepLoadedFast(key.cx, key.cz, centerCx, centerCz, camFwdX, camFwdZ, in cfg, cosKeep))
                {
                    if (_outOfConeSinceSec.ContainsKey(key))
                    {
                        _reenteredScratch.Add(key);
                    }
                    continue;
                }

                if (!_outOfConeSinceSec.TryGetValue(key, out double leftAt))
                {
                    _outOfConeSinceSec[key] = nowSeconds;
                    continue;
                }

                double age = nowSeconds - leftAt;
                double requiredGrace = loadedCount > softActiveChunkCap
                    ? Math.Min(cfg.GraceUnloadSeconds, 1.0f)
                    : cfg.GraceUnloadSeconds;

                if (age >= requiredGrace)
                {
                    if (isBlockedByInFlightJob != null && isBlockedByInFlightJob(key.cx, key.cz))
                    {
                        continue;
                    }
                    _evictScratch.Add((key, age));
                }
            }

            for (int i = 0; i < _reenteredScratch.Count; i++)
            {
                _outOfConeSinceSec.Remove(_reenteredScratch[i]);
            }

            if (_evictScratch.Count == 0)
            {
                return;
            }

            // En uzun suredir koni disinda kalan chunk'lari once bosalt (0 GC kismi secim)
            int take = Math.Min(maxUnloadsThisFrame, _evictScratch.Count);
            for (int i = 0; i < take; i++)
            {
                int oldestIdx = i;
                double oldestAge = _evictScratch[i].Age;
                for (int j = i + 1; j < _evictScratch.Count; j++)
                {
                    if (_evictScratch[j].Age > oldestAge)
                    {
                        oldestAge = _evictScratch[j].Age;
                        oldestIdx = j;
                    }
                }
                if (oldestIdx != i)
                {
                    var tmp = _evictScratch[i];
                    _evictScratch[i] = _evictScratch[oldestIdx];
                    _evictScratch[oldestIdx] = tmp;
                }
                outReadyToUnload.Add(_evictScratch[i].Key);
            }
        }

        public void NotifyChunkUnloaded(int cx, int cz)
        {
            _outOfConeSinceSec.Remove((cx, cz));
        }

        public void ClearTimers()
        {
            _outOfConeSinceSec.Clear();
        }

        private static bool IsInZone1OrZone2(
            int cx,
            int cz,
            int centerCx,
            int centerCz,
            float fwdX,
            float fwdZ,
            in ViewConeConfig cfg,
            float cosHalfAngle,
            int outerPad,
            out bool isInnerCore)
        {
            int dx = cx - centerCx;
            int dz = cz - centerCz;
            int absDx = Math.Abs(dx);
            int absDz = Math.Abs(dz);
            int cheb = Math.Max(absDx, absDz);

            if (cheb <= cfg.InnerCoreRadiusChunks || (cfg.ProtectOriginWorkshopCore && IsProtectedOriginWorkshop(cx, cz)))
            {
                isInnerCore = true;
                return true;
            }

            isInnerCore = false;
            int outerR = cfg.ActiveOuterRadiusChunks + outerPad;
            if (cfg.UseEuclideanOuterRadius)
            {
                int distSq = dx * dx + dz * dz;
                int maxDistSq = outerR * outerR + outerR; // r <= outerR + ~0.5
                if (distSq > maxDistSq)
                {
                    return false;
                }
            }
            else if (cheb > outerR)
            {
                return false;
            }

            float invLen = 1.0f / MathF.Sqrt(dx * dx + dz * dz);
            float dot = (dx * fwdX + dz * fwdZ) * invLen;
            return dot >= cosHalfAngle;
        }

        private static void NormalizeForward2D(ref float fx, ref float fz)
        {
            float lenSq = fx * fx + fz * fz;
            if (lenSq < 1e-6f)
            {
                fx = 0.0f;
                fz = 1.0f;
                return;
            }
            float inv = 1.0f / MathF.Sqrt(lenSq);
            fx *= inv;
            fz *= inv;
        }
    }
}
