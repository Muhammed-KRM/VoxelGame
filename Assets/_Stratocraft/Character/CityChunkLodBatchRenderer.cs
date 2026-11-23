#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Structures;

#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// DOK4 §5.3 &amp; DOK6 §7: Sehir olcegi (9x9 chunk, 3.000-10.000+ dugum) 3-kademeli LOD seviyesi.
    /// </summary>
    public enum CityChunkLodTier : byte
    {
        Culled = 0,
        Lod2HorizonProxy = 1,   // 128 - 384 m+: Dusuk ucgenli dis siluet proxy (24-64 ucgen/bina)
        Lod1CombinedShell = 2,  // 48 - 128 m: Kapi/pencere aciklikli birlesik bina kabugu (ic mekan elenir)
        Lod0FullDetail = 3      // 0 - 48 m: Tam detay (statik cephe instanced batch + dinamik aktif dugumler)
    }

    /// <summary>
    /// Bir sehir chunk'inin kayitli LOD ozet kaydi (0-GC value type).
    /// </summary>
    public struct CityChunkLodSlot
    {
        public int ChunkX;
        public int ChunkZ;
        public int NodeStartIndex;
        public int NodeCount;
        public int Lod0ActiveNodeCount;
        public int Lod0StaticBatchNodeCount;
        public int Lod1ShellNodeCount;
        public int Lod2ProxyTriangles;
        public float CenterX;
        public float CenterZ;
        public float MinY;
        public float MaxY;
        public CityChunkLodTier CurrentLod;
        public CityChunkLodTier TargetLod;
        public double ForcedLod0UntilSec;
        public bool IsDirty;
        public bool HasDomedOrTowerSilhouette;
    }

    /// <summary>
    /// Tek karelik sehir LOD cizim ve akis istatistik ozeti (0-GC).
    /// </summary>
    public readonly struct CityLodRenderSummary
    {
        public readonly int RegisteredChunkCount;
        public readonly int TotalRegisteredNodes;
        public readonly int Lod0ChunkCount;
        public readonly int Lod1ChunkCount;
        public readonly int Lod2ChunkCount;
        public readonly int Lod0StaticElements;
        public readonly int Lod0ActiveElements;
        public readonly int Lod1ShellElements;
        public readonly int Lod2ProxyTriangles;
        public readonly int EstimatedDrawCalls;
        public readonly int TransitionsAppliedLastStep;
        public readonly int PendingTransitionCount;
        public readonly int DamagePromotionsTotal;
        public readonly double LastStreamingElapsedMs;

        public CityLodRenderSummary(
            int registeredChunkCount,
            int totalRegisteredNodes,
            int lod0ChunkCount,
            int lod1ChunkCount,
            int lod2ChunkCount,
            int lod0StaticElements,
            int lod0ActiveElements,
            int lod1ShellElements,
            int lod2ProxyTriangles,
            int estimatedDrawCalls,
            int transitionsAppliedLastStep,
            int pendingTransitionCount,
            int damagePromotionsTotal,
            double lastStreamingElapsedMs)
        {
            RegisteredChunkCount = registeredChunkCount;
            TotalRegisteredNodes = totalRegisteredNodes;
            Lod0ChunkCount = lod0ChunkCount;
            Lod1ChunkCount = lod1ChunkCount;
            Lod2ChunkCount = lod2ChunkCount;
            Lod0StaticElements = lod0StaticElements;
            Lod0ActiveElements = lod0ActiveElements;
            Lod1ShellElements = lod1ShellElements;
            Lod2ProxyTriangles = lod2ProxyTriangles;
            EstimatedDrawCalls = estimatedDrawCalls;
            TransitionsAppliedLastStep = transitionsAppliedLastStep;
            PendingTransitionCount = pendingTransitionCount;
            DamagePromotionsTotal = damagePromotionsTotal;
            LastStreamingElapsedMs = lastStreamingElapsedMs;
        }
    }

    /// <summary>
    /// DOK4 §5.3 &amp; DOK6 §7 (AK-21 / T21):
    /// 9x9 chunk Sehir olceginde 0-GC LOD0 (0-48m) / LOD1 (48-128m) / LOD2 (128-384m+) gecis planlayicisi,
    /// ic mekan elemesi, hasar aninda aninda LOD0 yukseltme ve &lt;= 1.5 ms Stopwatch akis butcesi denetleyicisi.
    /// </summary>
    public sealed class CityChunkLodBatchRenderer
    {
        public const float Lod0MaxDistanceMeters = 48.0f;
        public const float Lod1MaxDistanceMeters = 128.0f;
        public const float Lod2MaxDistanceMeters = 384.0f;
        public const double DefaultStreamingBudgetMs = 1.5;

        public const int DefaultMaxChunkCapacity = 256;
        public const int DefaultMaxNodeCapacity = 16384;

        private readonly CityChunkLodSlot[] _chunkSlots;
        private readonly StructuralNode64[] _nodeBuffer;
        private readonly int[] _pendingTransitionIndices;
        private readonly float[] _pendingDistancesSq;
        private readonly Stopwatch _streamingStopwatch = new();

        private int _chunkCount;
        private int _nodeCount;
        private int _pendingCount;
        private int _damagePromotionsTotal;
        private int _lastTransitionsApplied;
        private double _lastStreamingElapsedMs;

        public int RegisteredChunkCount => _chunkCount;
        public int TotalRegisteredNodes => _nodeCount;
        public int PendingTransitionCount => _pendingCount;

        public CityChunkLodBatchRenderer(
            int maxChunkCapacity = DefaultMaxChunkCapacity,
            int maxNodeCapacity = DefaultMaxNodeCapacity)
        {
            int capChunks = Math.Max(16, maxChunkCapacity);
            int capNodes = Math.Max(256, maxNodeCapacity);
            _chunkSlots = new CityChunkLodSlot[capChunks];
            _nodeBuffer = new StructuralNode64[capNodes];
            _pendingTransitionIndices = new int[capChunks];
            _pendingDistancesSq = new float[capChunks];
        }

        public void Clear()
        {
            _chunkCount = 0;
            _nodeCount = 0;
            _pendingCount = 0;
            _damagePromotionsTotal = 0;
            _lastTransitionsApplied = 0;
            _lastStreamingElapsedMs = 0.0;
#if UNITY_5_3_OR_NEWER
            ClearUnityInstanceState();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CityChunkLodTier EvaluateDistanceTier(float distanceMeters)
        {
            if (distanceMeters <= Lod0MaxDistanceMeters) return CityChunkLodTier.Lod0FullDetail;
            if (distanceMeters <= Lod1MaxDistanceMeters) return CityChunkLodTier.Lod1CombinedShell;
            if (distanceMeters <= Lod2MaxDistanceMeters) return CityChunkLodTier.Lod2HorizonProxy;
            return CityChunkLodTier.Culled;
        }

        /// <summary>
        /// LOD0 icinde bireysel aktif fiziksel dugume yukseltilmesi gereken (kapi/pencere/merdiven/iskele/hasarli) elemanlar.
        /// Diger sabit duvar/subasman elemanlari tekil instanced batch olarak cizilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInteractiveOrDynamicInLod0(in StructuralNode64 node)
        {
            if (node.CollapseState != StructureCollapseState.Stable || node.Integrity01 < 0.99f)
            {
                return true;
            }

            return node.Role is
                StructuralElementRole.DoorFrameOpening or
                StructuralElementRole.WindowFrameOpening or
                StructuralElementRole.StairStringerFlight or
                StructuralElementRole.ModularScaffolding or
                StructuralElementRole.TemporaryCenteringScaffold;
        }

        /// <summary>
        /// LOD1 (48-128 m) orta mesafede ic mekan elemesi: Kapi/pencere/duvar/kemer/kubbe/cati dis kabugu korunurken
        /// disaridan gorunmeyen doseme kirisleri, ic merdivenler, iskele ve temel hendekleri elenir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInteriorCulledInLod1(StructuralElementRole role)
        {
            return role is
                StructuralElementRole.TrenchRubbleFooting or
                StructuralElementRole.SillBeamOrLog or
                StructuralElementRole.FloorJoistDeck or
                StructuralElementRole.TimberMainGirder or
                StructuralElementRole.CollarTieOrTruss or
                StructuralElementRole.StairStringerFlight or
                StructuralElementRole.StairwellTrimmerOpening or
                StructuralElementRole.TemporaryCenteringScaffold or
                StructuralElementRole.ModularScaffolding or
                StructuralElementRole.CantileverCorbelBracket or
                StructuralElementRole.IronTieRod;
        }

        /// <summary>
        /// Bir (chunkX, chunkZ) chunk'ina ait yapi dugumlerini sifir heap tahsisiyle LOD sistemine kaydeder.
        /// </summary>
        public bool RegisterChunkNodes(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<StructuralNode64> nodes,
            float initialViewerX = 0f,
            float initialViewerZ = 0f)
        {
            if (nodes.Length == 0) return false;
            if (_nodeCount + nodes.Length > _nodeBuffer.Length) return false;

            int existingSlot = FindChunkSlotIndex(chunkX, chunkZ);
            if (existingSlot >= 0)
            {
                // Varolan chunk guncelleniyorsa yerinde yenile (ek kapasite varsa)
                return RefreshChunkSlot(existingSlot, nodes);
            }

            if (_chunkCount >= _chunkSlots.Length) return false;

            int startIdx = _nodeCount;
            nodes.CopyTo(_nodeBuffer.AsSpan(startIdx, nodes.Length));
            _nodeCount += nodes.Length;

            ComputeChunkLodMetrics(
                chunkX, chunkZ, startIdx, nodes.Length,
                initialViewerX, initialViewerZ,
                out CityChunkLodSlot slot);

            _chunkSlots[_chunkCount++] = slot;
            return true;
        }

        public bool TryGetChunkSlot(int chunkX, int chunkZ, out CityChunkLodSlot slot)
        {
            int idx = FindChunkSlotIndex(chunkX, chunkZ);
            if (idx >= 0)
            {
                slot = _chunkSlots[idx];
                return true;
            }
            slot = default;
            return false;
        }

        public ReadOnlySpan<StructuralNode64> GetChunkNodesSpan(in CityChunkLodSlot slot)
        {
            if (slot.NodeCount <= 0 || slot.NodeStartIndex + slot.NodeCount > _nodeCount)
            {
                return ReadOnlySpan<StructuralNode64>.Empty;
            }
            return _nodeBuffer.AsSpan(slot.NodeStartIndex, slot.NodeCount);
        }

        /// <summary>
        /// AK-21 / DOK6 §7: Uzaktaki LOD1 veya LOD2 sur/bina dugumune hasar geldiginde
        /// ilgili chunk'i ANINDA LOD0'a yukseltir (uzaktan kirilmayan duvar hatasini %100 onler).
        /// </summary>
        public bool NotifyChunkDamaged(int chunkX, int chunkZ, double nowSeconds, float holdLod0Seconds = 12.0f)
        {
            int idx = FindChunkSlotIndex(chunkX, chunkZ);
            if (idx < 0) return false;

            ref CityChunkLodSlot slot = ref _chunkSlots[idx];
            slot.ForcedLod0UntilSec = Math.Max(slot.ForcedLod0UntilSec, nowSeconds + holdLod0Seconds);
            slot.TargetLod = CityChunkLodTier.Lod0FullDetail;
            if (slot.CurrentLod != CityChunkLodTier.Lod0FullDetail)
            {
                slot.CurrentLod = CityChunkLodTier.Lod0FullDetail;
                slot.IsDirty = true;
                _damagePromotionsTotal++;
            }

#if UNITY_5_3_OR_NEWER
            MarkUnityChunkMeshDirty(idx);
#endif
            return true;
        }

        public int NotifyDamageAtWorldPos(float worldX, float worldZ, float radiusMeters, double nowSeconds, float holdLod0Seconds = 12.0f)
        {
            int minCx = (int)MathF.Floor((worldX - radiusMeters) / 16f);
            int maxCx = (int)MathF.Floor((worldX + radiusMeters) / 16f);
            int minCz = (int)MathF.Floor((worldZ - radiusMeters) / 16f);
            int maxCz = (int)MathF.Floor((worldZ + radiusMeters) / 16f);

            int promoted = 0;
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (NotifyChunkDamaged(cx, cz, nowSeconds, holdLod0Seconds))
                    {
                        promoted++;
                    }
                }
            }
            return promoted;
        }

        /// <summary>
        /// Oyuncu konumuna gore tum kayitli sehir chunk'larinin LOD2 &lt;-&gt; LOD1 &lt;-&gt; LOD0 gecislerini
        /// &lt;= 1.5 ms Stopwatch butcesi icinde 0-GC olarak yurutur.
        /// </summary>
        public int StepStreaming(
            float viewerX,
            float viewerZ,
            double nowSeconds,
            double budgetMs = DefaultStreamingBudgetMs,
            int maxTransitionsPerFrame = 16)
        {
            _streamingStopwatch.Restart();
            _pendingCount = 0;

            for (int i = 0; i < _chunkCount; i++)
            {
                ref CityChunkLodSlot slot = ref _chunkSlots[i];
                float dx = slot.CenterX - viewerX;
                float dz = slot.CenterZ - viewerZ;
                float distSq = dx * dx + dz * dz;
                float dist = MathF.Sqrt(distSq);

                CityChunkLodTier desired = slot.ForcedLod0UntilSec > nowSeconds
                    ? CityChunkLodTier.Lod0FullDetail
                    : EvaluateDistanceTier(dist);

                slot.TargetLod = desired;
                if (slot.CurrentLod != desired || slot.IsDirty)
                {
                    _pendingTransitionIndices[_pendingCount] = i;
                    _pendingDistancesSq[_pendingCount] = slot.ForcedLod0UntilSec > nowSeconds ? -1f : distSq;
                    _pendingCount++;
                }
            }

            int applied = 0;
            int maxLimit = Math.Max(1, maxTransitionsPerFrame);

            // En yakin / hasarli chunk'lari secerek <= budgetMs icinde isle (0-GC secim siralamasi)
            while (applied < maxLimit && _pendingCount > 0)
            {
                if (applied > 0 && _streamingStopwatch.Elapsed.TotalMilliseconds >= budgetMs)
                {
                    break;
                }

                int bestPos = 0;
                float bestDistSq = _pendingDistancesSq[0];
                for (int k = 1; k < _pendingCount; k++)
                {
                    if (_pendingDistancesSq[k] < bestDistSq)
                    {
                        bestDistSq = _pendingDistancesSq[k];
                        bestPos = k;
                    }
                }

                int slotIdx = _pendingTransitionIndices[bestPos];
                _pendingCount--;
                if (bestPos < _pendingCount)
                {
                    _pendingTransitionIndices[bestPos] = _pendingTransitionIndices[_pendingCount];
                    _pendingDistancesSq[bestPos] = _pendingDistancesSq[_pendingCount];
                }

                ref CityChunkLodSlot slot = ref _chunkSlots[slotIdx];
                slot.CurrentLod = slot.TargetLod;
                slot.IsDirty = false;
#if UNITY_5_3_OR_NEWER
                RebuildUnityChunkRenderState(slotIdx);
#endif
                applied++;
            }

            _streamingStopwatch.Stop();
            _lastTransitionsApplied = applied;
            _lastStreamingElapsedMs = _streamingStopwatch.Elapsed.TotalMilliseconds;
            return applied;
        }

        /// <summary>
        /// Tum kayitli sehir chunk'lari icin anlik LOD dagilimi, eleman sayisi ve draw call tahminini hesaplar (0-GC).
        /// </summary>
        public void GetRenderFrameSummary(out CityLodRenderSummary summary)
        {
            int lod0Chunks = 0, lod1Chunks = 0, lod2Chunks = 0;
            int lod0Static = 0, lod0Active = 0, lod1Shell = 0, lod2Tris = 0;

            for (int i = 0; i < _chunkCount; i++)
            {
                ref readonly CityChunkLodSlot s = ref _chunkSlots[i];
                switch (s.CurrentLod)
                {
                    case CityChunkLodTier.Lod0FullDetail:
                        lod0Chunks++;
                        lod0Static += s.Lod0StaticBatchNodeCount;
                        lod0Active += s.Lod0ActiveNodeCount;
                        break;
                    case CityChunkLodTier.Lod1CombinedShell:
                        lod1Chunks++;
                        lod1Shell += s.Lod1ShellNodeCount;
                        break;
                    case CityChunkLodTier.Lod2HorizonProxy:
                        lod2Chunks++;
                        lod2Tris += s.Lod2ProxyTriangles;
                        break;
                }
            }

            // LOD0: 1 instanced batch + dinamik aktif katman; LOD1: chunk basina 1 birlesik shell; LOD2: toplam 1-8 ufuk kabugu
            int drawCalls = (lod0Chunks > 0 ? Math.Min(12, Math.Max(1, lod0Chunks)) : 0) +
                            lod1Chunks +
                            (lod2Chunks > 0 ? Math.Min(8, Math.Max(1, (lod2Chunks + 7) / 8)) : 0);

            summary = new CityLodRenderSummary(
                _chunkCount,
                _nodeCount,
                lod0Chunks,
                lod1Chunks,
                lod2Chunks,
                lod0Static,
                lod0Active,
                lod1Shell,
                lod2Tris,
                drawCalls,
                _lastTransitionsApplied,
                _pendingCount,
                _damagePromotionsTotal,
                _lastStreamingElapsedMs);
        }

        private int FindChunkSlotIndex(int chunkX, int chunkZ)
        {
            for (int i = 0; i < _chunkCount; i++)
            {
                if (_chunkSlots[i].ChunkX == chunkX && _chunkSlots[i].ChunkZ == chunkZ)
                {
                    return i;
                }
            }
            return -1;
        }

        private bool RefreshChunkSlot(int slotIdx, ReadOnlySpan<StructuralNode64> nodes)
        {
            ref CityChunkLodSlot oldSlot = ref _chunkSlots[slotIdx];
            if (nodes.Length <= oldSlot.NodeCount)
            {
                nodes.CopyTo(_nodeBuffer.AsSpan(oldSlot.NodeStartIndex, nodes.Length));
                ComputeChunkLodMetrics(
                    oldSlot.ChunkX, oldSlot.ChunkZ,
                    oldSlot.NodeStartIndex, nodes.Length,
                    oldSlot.CenterX, oldSlot.CenterZ,
                    out CityChunkLodSlot updated);
                updated.CurrentLod = oldSlot.CurrentLod;
                updated.ForcedLod0UntilSec = oldSlot.ForcedLod0UntilSec;
                updated.IsDirty = true;
                oldSlot = updated;
                return true;
            }
            return false;
        }

        private void ComputeChunkLodMetrics(
            int chunkX,
            int chunkZ,
            int startIdx,
            int count,
            float viewerX,
            float viewerZ,
            out CityChunkLodSlot slot)
        {
            int activeCount = 0;
            int staticCount = 0;
            int shellCount = 0;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            bool hasDomeOrTower = false;

            for (int i = 0; i < count; i++)
            {
                ref readonly StructuralNode64 n = ref _nodeBuffer[startIdx + i];
                if (n.PosY < minY) minY = n.PosY;
                if (n.PosY > maxY) maxY = n.PosY;

                if (IsInteractiveOrDynamicInLod0(in n)) activeCount++;
                else staticCount++;

                if (!IsInteriorCulledInLod1(n.Role)) shellCount++;

                if (n.Role is StructuralElementRole.DomeRingCourse or
                    StructuralElementRole.DomeOculusCrown or
                    StructuralElementRole.DomeDrumTambour)
                {
                    hasDomeOrTower = true;
                }
            }

            if (minY > maxY) { minY = 0f; maxY = 0f; }
            float cx = chunkX * 16.0f + 8.0f;
            float cz = chunkZ * 16.0f + 8.0f;
            float dx = cx - viewerX;
            float dz = cz - viewerZ;
            CityChunkLodTier initialTier = EvaluateDistanceTier(MathF.Sqrt(dx * dx + dz * dz));

            // DOK4 §5.3: LOD2 bina adasi basina 24-64 ucgen siluet proxy
            int proxyTris = hasDomeOrTower ? 48 : ((maxY - minY) > 5.0f ? 36 : 24);

            slot = new CityChunkLodSlot
            {
                ChunkX = chunkX,
                ChunkZ = chunkZ,
                NodeStartIndex = startIdx,
                NodeCount = count,
                Lod0ActiveNodeCount = activeCount,
                Lod0StaticBatchNodeCount = staticCount,
                Lod1ShellNodeCount = shellCount,
                Lod2ProxyTriangles = proxyTris,
                CenterX = cx,
                CenterZ = cz,
                MinY = minY,
                MaxY = maxY,
                CurrentLod = initialTier,
                TargetLod = initialTier,
                ForcedLod0UntilSec = 0.0,
                IsDirty = true,
                HasDomedOrTowerSilhouette = hasDomeOrTower
            };
        }

#if UNITY_5_3_OR_NEWER
        private const int MaxInstancesPerDrawCall = 1023;
        private static readonly Matrix4x4[] _sharedInstanceMatrices = new Matrix4x4[MaxInstancesPerDrawCall];
        private static readonly Vector4[] _sharedInstanceColors = new Vector4[MaxInstancesPerDrawCall];
        private static readonly List<Mesh> _sharedUnitMeshPool = new(16);
        private static Mesh? _sharedUnitBoxMesh;
        private static MaterialPropertyBlock? _sharedMpb;

        private readonly Mesh?[] _chunkCombinedShellMeshes = new Mesh?[DefaultMaxChunkCapacity];
        private readonly bool[] _chunkMeshDirtyFlags = new bool[DefaultMaxChunkCapacity];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticUnityCachesForDomainReloadDisabled()
        {
            Array.Clear(_sharedInstanceMatrices, 0, _sharedInstanceMatrices.Length);
            Array.Clear(_sharedInstanceColors, 0, _sharedInstanceColors.Length);
            _sharedUnitMeshPool.Clear();
            _sharedUnitBoxMesh = null;
            _sharedMpb = null;
        }

        private void ClearUnityInstanceState()
        {
            for (int i = 0; i < _chunkCombinedShellMeshes.Length; i++)
            {
                if (_chunkCombinedShellMeshes[i] != null)
                {
                    _chunkCombinedShellMeshes[i]!.Clear();
                }
                _chunkMeshDirtyFlags[i] = false;
            }
        }

        private void MarkUnityChunkMeshDirty(int slotIdx)
        {
            if ((uint)slotIdx < (uint)_chunkMeshDirtyFlags.Length)
            {
                _chunkMeshDirtyFlags[slotIdx] = true;
            }
        }

        private void RebuildUnityChunkRenderState(int slotIdx)
        {
            if ((uint)slotIdx >= (uint)_chunkCombinedShellMeshes.Length) return;
            _chunkMeshDirtyFlags[slotIdx] = false;
        }

        /// <summary>
        /// Unity sahnesinde 0-GC GPU Instancing (Graphics.DrawMeshInstanced) ile LOD0/1/2 sehir
        /// bloklarini cizer. Sabit 1023 matris havuzu sayesinde kare basina 0 B GC alloc yapar.
        /// </summary>
        public void RenderVisibleCityBatches(Material? sharedMaterial, ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            if (sharedMaterial == null || _chunkCount == 0) return;

            Mesh unitMesh = GetOrCreateSharedUnitBoxMesh();
            _sharedMpb ??= new MaterialPropertyBlock();

            int batchCount = 0;
            for (int c = 0; c < _chunkCount; c++)
            {
                ref readonly CityChunkLodSlot slot = ref _chunkSlots[c];
                if (slot.CurrentLod == CityChunkLodTier.Culled) continue;

                if (slot.CurrentLod == CityChunkLodTier.Lod2HorizonProxy)
                {
                    float h = MathF.Max(2.5f, slot.MaxY - slot.MinY);
                    Vector3 pos = new Vector3(slot.CenterX, slot.MinY + h * 0.5f, slot.CenterZ);
                    Vector3 scl = new Vector3(12.0f, h, 12.0f);
                    _sharedInstanceMatrices[batchCount++] = Matrix4x4.TRS(pos, Quaternion.identity, scl);
                    if (batchCount == MaxInstancesPerDrawCall)
                    {
                        Graphics.DrawMeshInstanced(unitMesh, 0, sharedMaterial, _sharedInstanceMatrices, batchCount, _sharedMpb, shadows, true);
                        batchCount = 0;
                    }
                    continue;
                }

                bool isLod1 = slot.CurrentLod == CityChunkLodTier.Lod1CombinedShell;
                for (int i = 0; i < slot.NodeCount; i++)
                {
                    ref readonly StructuralNode64 node = ref _nodeBuffer[slot.NodeStartIndex + i];
                    if (node.CollapseState == StructureCollapseState.Collapsed) continue;
                    if (isLod1 && IsInteriorCulledInLod1(node.Role)) continue;

                    Vector3 pos = new Vector3(node.PosX, node.PosY, node.PosZ);
                    Quaternion rot = Quaternion.Euler(0f, (node.YawQuarterTurns & 3) * 90f, 0f);
                    Vector3 scl = ResolveNodeProxyScale(node.Role);
                    _sharedInstanceMatrices[batchCount++] = Matrix4x4.TRS(pos, rot, scl);

                    if (batchCount == MaxInstancesPerDrawCall)
                    {
                        Graphics.DrawMeshInstanced(unitMesh, 0, sharedMaterial, _sharedInstanceMatrices, batchCount, _sharedMpb, shadows, true);
                        batchCount = 0;
                    }
                }
            }

            if (batchCount > 0)
            {
                Graphics.DrawMeshInstanced(unitMesh, 0, sharedMaterial, _sharedInstanceMatrices, batchCount, _sharedMpb, shadows, true);
            }
        }

        private static Vector3 ResolveNodeProxyScale(StructuralElementRole role) => role switch
        {
            StructuralElementRole.AshlarMasonryWall or StructuralElementRole.RubbleMasonryCourse => new Vector3(2.0f, 1.4f, 0.45f),
            StructuralElementRole.NotchedWallLog => new Vector3(2.0f, 0.48f, 0.44f),
            StructuralElementRole.MonolithicColumnShaft or StructuralElementRole.TimberPost => new Vector3(0.45f, 2.8f, 0.45f),
            StructuralElementRole.RoofCoveringCourse or StructuralElementRole.FloorJoistDeck => new Vector3(2.0f, 0.25f, 2.0f),
            StructuralElementRole.DomeRingCourse or StructuralElementRole.DomeDrumTambour => new Vector3(3.2f, 1.0f, 3.2f),
            _ => new Vector3(1.0f, 1.0f, 1.0f)
        };

        private static Mesh GetOrCreateSharedUnitBoxMesh()
        {
            if (_sharedUnitBoxMesh != null) return _sharedUnitBoxMesh;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mf = go.GetComponent<MeshFilter>();
            _sharedUnitBoxMesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
            _sharedUnitBoxMesh.name = "CityChunkLod_UnitBoxMesh";
            UnityEngine.Object.DestroyImmediate(go);
            return _sharedUnitBoxMesh;
        }
#endif
    }
}
