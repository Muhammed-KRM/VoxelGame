#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// DOK4 §5.3, DOK6 §7 &amp; MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI Bolum 5.4 / Bolum 6:
    /// 48x48 chunk Metropol Sehir olceginde (2.304 chunk) 3-kademeli aktif pencere LOD seviyesi.
    /// </summary>
    public enum CityChunkLodTier : byte
    {
        Culled = 0,
        Lod2HorizonProxy = 1,   // 160 - 750 m+: Renkli catili / kubbeli dusuk ucgenli dis siluet proxy (24-64 ucgen/bina)
        Lod1CombinedShell = 2,  // 64 - 160 m: Kapi/pencere aciklikli birlesik dis cephe kabugu (ic mekan elenir)
        Lod0FullDetail = 3      // 0 - 64 m: Tam 65 .glb detay (mesh-gruplu instanced batch + donen kapi kanatlari)
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
        public byte DominantFacadeMaterialId;
        public byte DominantRoofMaterialId;
        public ArchitecturalSchool DominantSchool;
        public uint LastAccessTick;
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
    /// DOK4 §5.3, DOK6 §7 (AK-21 / T21) &amp; MIMARI_OZELLIKLERI_GELISTIRME_DOKUMANI Bolum 1 (KN-1, KN-2) + Bolum 5.4 + Bolum 6:
    /// - 48x48 chunk Metropol Sehir olceginde 0-GC LOD0 (0-64m) / LOD1 (64-160m) / LOD2 (160-750m+) aktif pencere gecis planlayicisi,
    /// - Ham birim kup (KN-1) ve bembeyaz HDR parlama (KN-2) hatalarini %100 ortadan kaldiran,
    ///   <see cref="StructureRoleGlbRegistry"/> uzerinden 65 gercek .glb mesh-gruplu 0-GC Graphics.DrawMeshInstanced cizicisi,
    /// - DoorFrameOpening dugumleri icin IsDoorOrWindowOpen durumuna gore 0° &lt;-&gt; 90° donen kapi kanadi (mod_hinged_door_leaf / mod_double_gate_leaf),
    /// - LRU uzak chunk tahliyesi (EvictDistantChunks / GetChunksPendingEviction / UnregisterChunkNodes) ve &lt;= 1.5 ms Stopwatch butcesi.
    /// </summary>
    public sealed class CityChunkLodBatchRenderer
    {
        public const float Lod0MaxDistanceMeters = 64.0f;
        public const float Lod1MaxDistanceMeters = 160.0f;
        public const float Lod2MaxDistanceMeters = 750.0f;
        public const double DefaultStreamingBudgetMs = 1.5;

        public const int DefaultMaxChunkCapacity = 512;
        public const int DefaultMaxNodeCapacity = 65536;

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
        private int _totalEvictedChunks;
        private uint _accessTickCounter = 1u;
        private double _lastStreamingElapsedMs;

        public int RegisteredChunkCount => _chunkCount;
        public int TotalRegisteredNodes => _nodeCount;
        public int PendingTransitionCount => _pendingCount;
        public int MaxChunkCapacity => _chunkSlots.Length;
        public int MaxNodeCapacity => _nodeBuffer.Length;
        public int TotalEvictedChunks => _totalEvictedChunks;

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
#if UNITY_5_3_OR_NEWER
            _chunkCombinedShellMeshes = new Mesh?[capChunks];
            _chunkMeshDirtyFlags = new bool[capChunks];
            _bucketedInstanceRefs = new uint[Math.Max(1024, capNodes + (capNodes >> 2))];
#endif
        }

        public void Clear()
        {
            _chunkCount = 0;
            _nodeCount = 0;
            _pendingCount = 0;
            _damagePromotionsTotal = 0;
            _lastTransitionsApplied = 0;
            _totalEvictedChunks = 0;
            _accessTickCounter = 1u;
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
        /// LOD0 icinde bireysel aktif fiziksel dugume yukseltilmesi gereken (kapi/pencere/merdiven/iskele/kalip/hasarli) elemanlar.
        /// Diger sabit duvar/subasman elemanlari tekil instanced batch olarak cizilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInteractiveOrDynamicInLod0(in StructuralNode64 node)
        {
            if (node.CollapseState != StructureCollapseState.Stable ||
                node.Integrity01 < 0.99f ||
                node.IsTemporarySupport)
            {
                return true;
            }

            return node.Role is
                StructuralElementRole.DoorFrameOpening or
                StructuralElementRole.WindowFrameOpening or
                StructuralElementRole.StairStringerFlight or
                StructuralElementRole.ModularScaffolding or
                StructuralElementRole.TemporaryCenteringScaffold or
                StructuralElementRole.ConcreteColumnFormwork or
                StructuralElementRole.TelescopicShoringProp or
                StructuralElementRole.SlabDeckFormwork or
                StructuralElementRole.SteelGuyWireBrace or
                StructuralElementRole.WoodenArchCentering or
                StructuralElementRole.SlidingEarthFormwork;
        }

        /// <summary>
        /// LOD1 (64-160 m) orta mesafede ic mekan elemesi: Kapi/pencere/duvar/kemer/kubbe/cati dis kabugu korunurken
        /// disaridan gorunmeyen doseme kirisleri, ic merdivenler, gecici kaliplar, iskele ve temel hendekleri elenir.
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
                StructuralElementRole.IronTieRod or
                StructuralElementRole.SaddleNotchedBaseLog or
                StructuralElementRole.HalfFlatStarterLog or
                StructuralElementRole.LogChinkingFill or
                StructuralElementRole.RebarFoundationCage or
                StructuralElementRole.RebarColumnCage or
                StructuralElementRole.ConcreteColumnFormwork or
                StructuralElementRole.TelescopicShoringProp or
                StructuralElementRole.SlabDeckFormwork or
                StructuralElementRole.SteelGuyWireBrace or
                StructuralElementRole.CorrugatedMetalDeck or
                StructuralElementRole.WoodenArchCentering or
                StructuralElementRole.SlidingEarthFormwork;
        }

        /// <summary>
        /// Bir (chunkX, chunkZ) chunk'ina ait yapi dugumlerini sifir heap tahsisiyle LOD sistemine kaydeder.
        /// Kapasite dolmussa en uzaktaki aktif olmayan chunk'lari LRU/mesafe sirasiyla otomatik tahliye eder.
        /// </summary>
        public bool RegisterChunkNodes(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<StructuralNode64> nodes,
            float initialViewerX = 0f,
            float initialViewerZ = 0f)
        {
            if (nodes.Length == 0 || nodes.Length > _nodeBuffer.Length) return false;

            int existingSlot = FindChunkSlotIndex(chunkX, chunkZ);
            if (existingSlot >= 0)
            {
                if (RefreshChunkSlot(existingSlot, nodes, initialViewerX, initialViewerZ))
                {
                    return true;
                }
                RemoveChunkSlotAt(existingSlot);
            }

            if (_chunkCount >= _chunkSlots.Length || _nodeCount + nodes.Length > _nodeBuffer.Length)
            {
                if (!EnsureCapacityByEvictingFarthest(initialViewerX, initialViewerZ, nodes.Length))
                {
                    return false;
                }
            }

            int startIdx = _nodeCount;
            nodes.CopyTo(_nodeBuffer.AsSpan(startIdx, nodes.Length));
            _nodeCount += nodes.Length;

            ComputeChunkLodMetrics(
                chunkX, chunkZ, startIdx, nodes.Length,
                initialViewerX, initialViewerZ,
                ++_accessTickCounter,
                out CityChunkLodSlot slot);

            _chunkSlots[_chunkCount++] = slot;
            return true;
        }

        /// <summary>
        /// Belirtilen (chunkX, chunkZ) chunk kaydini ve dugumlerini 0-GC ile LOD tamponundan kaldirir.
        /// </summary>
        public bool UnregisterChunkNodes(int chunkX, int chunkZ)
        {
            int idx = FindChunkSlotIndex(chunkX, chunkZ);
            if (idx < 0) return false;
            RemoveChunkSlotAt(idx);
            return true;
        }

        /// <summary>
        /// Oyuncudan <paramref name="evictionDistanceMeters"/> mesafeden daha uzakta kalan kayitli chunk'larin
        /// (ChunkX, ChunkZ) koordinatlarini en uzaktan baslayarak <paramref name="outChunks"/> tamponuna doldurur (0-GC).
        /// </summary>
        public int GetChunksPendingEviction(
            float viewerX,
            float viewerZ,
            float evictionDistanceMeters,
            Span<(int ChunkX, int ChunkZ)> outChunks)
        {
            if (_chunkCount == 0 || outChunks.Length == 0) return 0;

            float thresholdSq = evictionDistanceMeters * evictionDistanceMeters;
            int found = 0;

            for (int i = 0; i < _chunkCount && found < outChunks.Length; i++)
            {
                ref readonly CityChunkLodSlot s = ref _chunkSlots[i];
                if (s.ForcedLod0UntilSec > 0.0 && s.CurrentLod == CityChunkLodTier.Lod0FullDetail)
                {
                    continue;
                }

                float dx = s.CenterX - viewerX;
                float dz = s.CenterZ - viewerZ;
                float distSq = dx * dx + dz * dz;
                if (distSq > thresholdSq)
                {
                    outChunks[found++] = (s.ChunkX, s.ChunkZ);
                }
            }

            return found;
        }

        /// <summary>
        /// 48x48 chunk Metropol aktif pencere akisi icin <paramref name="maxKeepDistanceMeters"/> disinda kalan
        /// uzak chunk'lari en uzaktan baslayarak 0-GC ile tahliye eder.
        /// </summary>
        public int EvictDistantChunks(
            float viewerX,
            float viewerZ,
            float maxKeepDistanceMeters = Lod2MaxDistanceMeters,
            int maxEvictions = 16)
        {
            if (_chunkCount == 0 || maxEvictions <= 0) return 0;

            float maxKeepSq = maxKeepDistanceMeters * maxKeepDistanceMeters;
            int evicted = 0;

            while (evicted < maxEvictions && _chunkCount > 0)
            {
                int worstIdx = -1;
                float worstDistSq = maxKeepSq;

                for (int i = 0; i < _chunkCount; i++)
                {
                    ref readonly CityChunkLodSlot s = ref _chunkSlots[i];
                    float dx = s.CenterX - viewerX;
                    float dz = s.CenterZ - viewerZ;
                    float distSq = dx * dx + dz * dz;
                    if (distSq > worstDistSq)
                    {
                        worstDistSq = distSq;
                        worstIdx = i;
                    }
                }

                if (worstIdx < 0) break;
                RemoveChunkSlotAt(worstIdx);
                _totalEvictedChunks++;
                evicted++;
            }

            return evicted;
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

        /// <summary>
        /// Kayitli bir chunk'taki belirli bir dugumun <see cref="StructuralNode64.IsDoorOrWindowOpen"/> durumunu
        /// yerinde gunceller (kapi kanadinin 0° &lt;-&gt; 90° aninda donmesi icin, 0-GC).
        /// </summary>
        public bool TrySetNodeDoorOpenState(uint nodeId, bool isOpen)
        {
            if (nodeId == 0u) return false;
            for (int i = 0; i < _nodeCount; i++)
            {
                if (_nodeBuffer[i].NodeId == nodeId)
                {
                    _nodeBuffer[i].IsDoorOrWindowOpen = isOpen;
                    return true;
                }
            }
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
            slot.LastAccessTick = ++_accessTickCounter;
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
            uint tick = ++_accessTickCounter;

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

                if (desired == CityChunkLodTier.Lod0FullDetail)
                {
                    slot.LastAccessTick = tick;
                }

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
                slot.LastAccessTick = tick;
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

            // LOD0/LOD1: 65 .glb mesh-gruplu DrawMeshInstanced; LOD2: renkli catili/kubbeli ufuk siluetleri (1-4 draw call)
            int drawCalls = (lod0Chunks > 0 ? Math.Min(16, Math.Max(1, lod0Chunks)) : 0) +
                            (lod1Chunks > 0 ? Math.Min(12, Math.Max(1, (lod1Chunks + 1) / 2)) : 0) +
                            (lod2Chunks > 0 ? Math.Min(4, Math.Max(1, (lod2Chunks + 15) / 16)) : 0);

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

        private bool RefreshChunkSlot(
            int slotIdx,
            ReadOnlySpan<StructuralNode64> nodes,
            float viewerX,
            float viewerZ)
        {
            ref CityChunkLodSlot oldSlot = ref _chunkSlots[slotIdx];
            if (nodes.Length <= oldSlot.NodeCount)
            {
                nodes.CopyTo(_nodeBuffer.AsSpan(oldSlot.NodeStartIndex, nodes.Length));
                ComputeChunkLodMetrics(
                    oldSlot.ChunkX, oldSlot.ChunkZ,
                    oldSlot.NodeStartIndex, nodes.Length,
                    viewerX, viewerZ,
                    ++_accessTickCounter,
                    out CityChunkLodSlot updated);
                updated.CurrentLod = oldSlot.CurrentLod;
                updated.ForcedLod0UntilSec = oldSlot.ForcedLod0UntilSec;
                updated.IsDirty = true;
                oldSlot = updated;
                return true;
            }
            return false;
        }

        private void RemoveChunkSlotAt(int slotIdx)
        {
            if ((uint)slotIdx >= (uint)_chunkCount) return;

            CityChunkLodSlot removed = _chunkSlots[slotIdx];
            int removedStart = removed.NodeStartIndex;
            int removedLen = removed.NodeCount;

            // NodeBuffer icindeki boslugu kaydirarak kapat (0-GC in-place compaction)
            if (removedLen > 0 && removedStart + removedLen < _nodeCount)
            {
                int tailCount = _nodeCount - (removedStart + removedLen);
                _nodeBuffer.AsSpan(removedStart + removedLen, tailCount)
                    .CopyTo(_nodeBuffer.AsSpan(removedStart, tailCount));

                for (int i = 0; i < _chunkCount; i++)
                {
                    if (i != slotIdx && _chunkSlots[i].NodeStartIndex > removedStart)
                    {
                        _chunkSlots[i].NodeStartIndex -= removedLen;
                    }
                }
            }

            _nodeCount = Math.Max(0, _nodeCount - removedLen);
            _chunkCount--;

            if (slotIdx < _chunkCount)
            {
                _chunkSlots[slotIdx] = _chunkSlots[_chunkCount];
            }
            _chunkSlots[_chunkCount] = default;
            _pendingCount = 0;
        }

        private bool EnsureCapacityByEvictingFarthest(float viewerX, float viewerZ, int requiredNodes)
        {
            while ((_chunkCount >= _chunkSlots.Length || _nodeCount + requiredNodes > _nodeBuffer.Length) && _chunkCount > 0)
            {
                int worstIdx = -1;
                float worstScore = -1f;

                for (int i = 0; i < _chunkCount; i++)
                {
                    ref readonly CityChunkLodSlot s = ref _chunkSlots[i];
                    float dx = s.CenterX - viewerX;
                    float dz = s.CenterZ - viewerZ;
                    float distSq = dx * dx + dz * dz;
                    // En uzak ve en eski erisilen chunk oncelikle tahliye edilir
                    uint age = _accessTickCounter >= s.LastAccessTick ? (_accessTickCounter - s.LastAccessTick) : 0u;
                    float score = distSq + (age * 16.0f);
                    if (score > worstScore)
                    {
                        worstScore = score;
                        worstIdx = i;
                    }
                }

                if (worstIdx < 0) return false;
                RemoveChunkSlotAt(worstIdx);
                _totalEvictedChunks++;
            }

            return _chunkCount < _chunkSlots.Length && (_nodeCount + requiredNodes <= _nodeBuffer.Length);
        }

        private void ComputeChunkLodMetrics(
            int chunkX,
            int chunkZ,
            int startIdx,
            int count,
            float viewerX,
            float viewerZ,
            uint accessTick,
            out CityChunkLodSlot slot)
        {
            int activeCount = 0;
            int staticCount = 0;
            int shellCount = 0;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            bool hasDomeOrTower = false;
            byte facadeMat = MaterialIdCatalog.CutAshlarStone;
            byte roofMat = MaterialIdCatalog.RoofClayTile;
            ArchitecturalSchool dominantSchool = ArchitecturalSchool.StoneMasonry;

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
                    StructuralElementRole.DomeDrumTambour or
                    StructuralElementRole.PendentiveOrSquinch)
                {
                    hasDomeOrTower = true;
                }

                if (n.Role is StructuralElementRole.AshlarMasonryWall or
                    StructuralElementRole.RubbleMasonryCourse or
                    StructuralElementRole.NotchedWallLog or
                    StructuralElementRole.InfillPanelWattleAdobe or
                    StructuralElementRole.EnglishBondBrickWall or
                    StructuralElementRole.RammedEarthWall or
                    StructuralElementRole.CobCurvedWall or
                    StructuralElementRole.PlywoodSheathingPanel &&
                    n.MaterialId != 0)
                {
                    facadeMat = n.MaterialId;
                    dominantSchool = n.School;
                }
                else if (n.Role is StructuralElementRole.InclinedRoofRafter or
                         StructuralElementRole.RoofCoveringCourse or
                         StructuralElementRole.DomeRingCourse &&
                         n.MaterialId != 0)
                {
                    roofMat = n.MaterialId;
                }
            }

            if (minY > maxY) { minY = 0f; maxY = 0f; }
            float cx = chunkX * 16.0f + 8.0f;
            float cz = chunkZ * 16.0f + 8.0f;
            float dx = cx - viewerX;
            float dz = cz - viewerZ;
            CityChunkLodTier initialTier = EvaluateDistanceTier(MathF.Sqrt(dx * dx + dz * dz));

            // DOK4 §5.3: LOD2 bina adasi basina 24-64 ucgen renkli/catili/kubbeli siluet proxy
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
                HasDomedOrTowerSilhouette = hasDomeOrTower,
                DominantFacadeMaterialId = facadeMat,
                DominantRoofMaterialId = roofMat,
                DominantSchool = dominantSchool,
                LastAccessTick = accessTick
            };
        }

        /// <summary>
        /// Dugum kesit kalinlik profiline (Slender / Standard / HeavyBearing) gore yerel olcek carpanini cozer (0-GC).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResolveNodeThicknessScale(
            StructuralElementRole role,
            StructureThicknessProfile profile,
            out float scaleX,
            out float scaleY,
            out float scaleZ)
        {
            scaleX = 1.0f;
            scaleY = 1.0f;
            scaleZ = 1.0f;

            if (profile == StructureThicknessProfile.Standard)
            {
                return;
            }

            float factor = profile == StructureThicknessProfile.Slender ? 0.82f : 1.22f;
            switch (role)
            {
                case StructuralElementRole.AshlarMasonryWall:
                case StructuralElementRole.RubbleMasonryCourse:
                case StructuralElementRole.NotchedWallLog:
                case StructuralElementRole.InfillPanelWattleAdobe:
                case StructuralElementRole.RetainingWallCourse:
                case StructuralElementRole.PlatformStudWall:
                case StructuralElementRole.PlywoodSheathingPanel:
                case StructuralElementRole.RammedEarthWall:
                case StructuralElementRole.CobCurvedWall:
                case StructuralElementRole.EnglishBondBrickWall:
                    scaleZ = factor;
                    break;

                case StructuralElementRole.TimberPost:
                case StructuralElementRole.DrivenTimberPile:
                case StructuralElementRole.MonolithicColumnShaft:
                case StructuralElementRole.GroovedLogPost:
                case StructuralElementRole.SteelHBeamColumn:
                    scaleX = factor;
                    scaleZ = factor;
                    break;
            }
        }

#if UNITY_5_3_OR_NEWER
        private const int MaxInstancesPerDrawCall = 1023;
        private static readonly Matrix4x4[] _sharedInstanceMatrices = new Matrix4x4[MaxInstancesPerDrawCall];
        private static MaterialPropertyBlock? _sharedMpb;
        private static Material? _cachedSafeGlbMaterial;
        private static Material? _lastCheckedInputMaterial;

        // LOD2 Ufuk Silueti (catili bina blogu + kubbeli anitsal saray/tapinak) COLOR_0 mesh onbellegi
        private static readonly Dictionary<int, Mesh> _horizonSilhouetteMeshCache = new(32);

        private readonly Mesh?[] _chunkCombinedShellMeshes;
        private readonly bool[] _chunkMeshDirtyFlags;
        private readonly int[] _glbBucketCounts = new int[StructureRoleGlbRegistry.TotalGlbAssetCount];
        private readonly int[] _glbBucketOffsets = new int[StructureRoleGlbRegistry.TotalGlbAssetCount];
        private readonly int[] _glbBucketCursors = new int[StructureRoleGlbRegistry.TotalGlbAssetCount];
        private uint[] _bucketedInstanceRefs;

        /// <summary>
        /// KN-2 Cozumu: _UsePaletteLUT = 0 ve enableInstancing = true olarak yapilandirilmis
        /// .glb COLOR_0 uyumlu paylasilan materyal. Atanmazsa otomatik olarak
        /// <see cref="WorldLooseBuildSystem.ResolveModularVertexColorMaterial"/> uzerinden cozulur.
        /// </summary>
        public Material? GlbVertexColorMaterial { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticUnityCachesForDomainReloadDisabled()
        {
            Array.Clear(_sharedInstanceMatrices, 0, _sharedInstanceMatrices.Length);
            _sharedMpb = null;
            _cachedSafeGlbMaterial = null;
            _lastCheckedInputMaterial = null;
            _horizonSilhouetteMeshCache.Clear();
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
            Array.Clear(_glbBucketCounts, 0, _glbBucketCounts.Length);
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
        /// KN-2 Cozumu: Gonderilen materyal SharedVoxelMaterial (_UsePaletteLUT = 1) olsa bile
        /// Stefan-Boltzmann T^4 beyaz magma parlamasini %100 onlemek icin _UsePaletteLUT = 0 ve
        /// enableInstancing = true olan COLOR_0 materyaline donusturur.
        /// </summary>
        public static Material? ResolveSafeGlbVertexColorMaterial(Material? candidateMaterial)
        {
            if (candidateMaterial != null &&
                candidateMaterial.HasProperty("_UsePaletteLUT") &&
                candidateMaterial.GetFloat("_UsePaletteLUT") <= 0.01f)
            {
                if (!candidateMaterial.enableInstancing)
                {
                    candidateMaterial.enableInstancing = true;
                }
                return candidateMaterial;
            }

            if (_cachedSafeGlbMaterial != null && ReferenceEquals(_lastCheckedInputMaterial, candidateMaterial))
            {
                return _cachedSafeGlbMaterial;
            }

            _lastCheckedInputMaterial = candidateMaterial;
            Material? resolved = WorldLooseBuildSystem.ResolveModularVertexColorMaterial(candidateMaterial);
            if (resolved == null && candidateMaterial != null)
            {
                resolved = new Material(candidateMaterial)
                {
                    name = "M_Stratocraft_CityGlbVertexColor_Safe"
                };
            }

            if (resolved != null)
            {
                resolved.enableInstancing = true;
                if (resolved.HasProperty("_UsePaletteLUT"))
                {
                    resolved.SetFloat("_UsePaletteLUT", 0.0f);
                }
                _cachedSafeGlbMaterial = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Unity sahnesinde 0-GC GPU Instancing (Graphics.DrawMeshInstanced) ile LOD0/1/2 sehir
        /// yapilarini 65 gercek .glb modeli + donen kapi kanatlari + renkli ufuk siluetleriyle cizer.
        /// </summary>
        public void RenderVisibleCityBatches(
            Material? sharedMaterial,
            ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            RenderVisibleCityBatches(sharedMaterial, GlbVertexColorMaterial, shadows);
        }

        /// <summary>
        /// Acik <paramref name="glbVertexColorMaterial"/> parametresiyle 0-GC mesh-gruplu sehir LOD0/1/2 cizimi.
        /// </summary>
        public void RenderVisibleCityBatches(
            Material? sharedMaterial,
            Material? glbVertexColorMaterial,
            ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            if (_chunkCount == 0) return;

            Material? safeMat = ResolveSafeGlbVertexColorMaterial(glbVertexColorMaterial ?? GlbVertexColorMaterial ?? sharedMaterial);
            if (safeMat == null) return;

            _sharedMpb ??= new MaterialPropertyBlock();
            _sharedMpb.SetFloat("_UsePaletteLUT", 0.0f);

            // 1. Gecis: LOD0 ve LOD1 dugumlerini 65 .glb varlik indeksine gore say (0-GC)
            Array.Clear(_glbBucketCounts, 0, StructureRoleGlbRegistry.TotalGlbAssetCount);
            int totalInstancesToBucket = 0;

            for (int c = 0; c < _chunkCount; c++)
            {
                ref readonly CityChunkLodSlot slot = ref _chunkSlots[c];
                if (slot.CurrentLod is CityChunkLodTier.Culled or CityChunkLodTier.Lod2HorizonProxy)
                {
                    continue;
                }

                bool isLod1 = slot.CurrentLod == CityChunkLodTier.Lod1CombinedShell;
                int start = slot.NodeStartIndex;
                int end = start + slot.NodeCount;

                for (int idx = start; idx < end; idx++)
                {
                    ref readonly StructuralNode64 node = ref _nodeBuffer[idx];
                    if (node.CollapseState is StructureCollapseState.Collapsed or StructureCollapseState.DetachedInstantFall ||
                        node.Integrity01 <= 0.001f)
                    {
                        continue;
                    }
                    if (isLod1 && IsInteriorCulledInLod1(node.Role))
                    {
                        continue;
                    }

                    int assetIdx = StructureRoleGlbRegistry.ResolveGlbAssetIndex(in node);
                    _glbBucketCounts[assetIdx]++;
                    totalInstancesToBucket++;

                    // DoorFrameOpening: Kapi kasasina ek olarak 0° <-> 90° donen kapi kanadini (veya cift kanadi) kuyruga ekle!
                    if (node.Role == StructuralElementRole.DoorFrameOpening)
                    {
                        int leafAssetIdx = StructureRoleGlbRegistry.ResolveDoorLeafGlbAssetIndex(in node, out bool isDoubleLeaf);
                        _glbBucketCounts[leafAssetIdx]++;
                        totalInstancesToBucket++;
                        if (isDoubleLeaf)
                        {
                            _glbBucketCounts[leafAssetIdx]++;
                            totalInstancesToBucket++;
                        }
                    }
                }
            }

            // 2. Gecis: CSR prefix-sum ve dugum referanslarinin sabit tampona dagitimi (0-GC)
            if (totalInstancesToBucket > 0)
            {
                if (totalInstancesToBucket > _bucketedInstanceRefs.Length)
                {
                    _bucketedInstanceRefs = new uint[Math.Max(totalInstancesToBucket, _bucketedInstanceRefs.Length * 2)];
                }

                int runningOffset = 0;
                for (int a = 0; a < StructureRoleGlbRegistry.TotalGlbAssetCount; a++)
                {
                    _glbBucketOffsets[a] = runningOffset;
                    _glbBucketCursors[a] = runningOffset;
                    runningOffset += _glbBucketCounts[a];
                }

                for (int c = 0; c < _chunkCount; c++)
                {
                    ref readonly CityChunkLodSlot slot = ref _chunkSlots[c];
                    if (slot.CurrentLod is CityChunkLodTier.Culled or CityChunkLodTier.Lod2HorizonProxy)
                    {
                        continue;
                    }

                    bool isLod1 = slot.CurrentLod == CityChunkLodTier.Lod1CombinedShell;
                    int start = slot.NodeStartIndex;
                    int end = start + slot.NodeCount;

                    for (int idx = start; idx < end; idx++)
                    {
                        ref readonly StructuralNode64 node = ref _nodeBuffer[idx];
                        if (node.CollapseState is StructureCollapseState.Collapsed or StructureCollapseState.DetachedInstantFall ||
                            node.Integrity01 <= 0.001f)
                        {
                            continue;
                        }
                        if (isLod1 && IsInteriorCulledInLod1(node.Role))
                        {
                            continue;
                        }

                        int assetIdx = StructureRoleGlbRegistry.ResolveGlbAssetIndex(in node);
                        _bucketedInstanceRefs[_glbBucketCursors[assetIdx]++] = ((uint)idx << 2);

                        if (node.Role == StructuralElementRole.DoorFrameOpening)
                        {
                            int leafAssetIdx = StructureRoleGlbRegistry.ResolveDoorLeafGlbAssetIndex(in node, out bool isDoubleLeaf);
                            _bucketedInstanceRefs[_glbBucketCursors[leafAssetIdx]++] = ((uint)idx << 2) | 1u;
                            if (isDoubleLeaf)
                            {
                                _bucketedInstanceRefs[_glbBucketCursors[leafAssetIdx]++] = ((uint)idx << 2) | 2u;
                            }
                        }
                    }
                }

                // 3. Gecis: Her .glb varligi icin 1023'luk gruplar halinde Graphics.DrawMeshInstanced cagir (0-GC)
                for (int a = 0; a < StructureRoleGlbRegistry.TotalGlbAssetCount; a++)
                {
                    int count = _glbBucketCounts[a];
                    if (count <= 0) continue;

                    Mesh? glbMesh = StructureRoleGlbRegistry.LoadGlbMeshCached(a);
                    if (glbMesh == null) continue;

                    int startRef = _glbBucketOffsets[a];
                    int endRef = startRef + count;
                    int batchCount = 0;

                    for (int rIdx = startRef; rIdx < endRef; rIdx++)
                    {
                        uint packedRef = _bucketedInstanceRefs[rIdx];
                        int nodeIdx = (int)(packedRef >> 2);
                        uint subPart = packedRef & 3u;
                        ref readonly StructuralNode64 node = ref _nodeBuffer[nodeIdx];

                        _sharedInstanceMatrices[batchCount++] = BuildNodeOrSubPartMatrix(in node, subPart);
                        if (batchCount == MaxInstancesPerDrawCall)
                        {
                            Graphics.DrawMeshInstanced(glbMesh, 0, safeMat, _sharedInstanceMatrices, batchCount, _sharedMpb, shadows, true);
                            batchCount = 0;
                        }
                    }

                    if (batchCount > 0)
                    {
                        Graphics.DrawMeshInstanced(glbMesh, 0, safeMat, _sharedInstanceMatrices, batchCount, _sharedMpb, shadows, true);
                    }
                }
            }

            // 4. Gecis: LOD2 (160 - 750 m+) Renkli Catili & Kubbeli Ufuk Siluetleri (North Metropolis + Palace Dome)
            RenderLod2HorizonSilhouettes(safeMat);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Matrix4x4 BuildNodeOrSubPartMatrix(in StructuralNode64 node, uint subPart)
        {
            float baseYawDeg = (node.YawQuarterTurns & 3) * 90.0f;
            Quaternion baseRot = Quaternion.Euler(0f, baseYawDeg, 0f);
            Vector3 basePos = new Vector3(node.PosX, node.PosY, node.PosZ);

            if (subPart == 0u)
            {
                ResolveNodeThicknessScale(
                    node.Role,
                    (StructureThicknessProfile)node.ThicknessProfile,
                    out float sx,
                    out float sy,
                    out float sz);
                return Matrix4x4.TRS(basePos, baseRot, new Vector3(sx, sy, sz));
            }

            // subPart == 1 (tek kanat veya sol cift kanat) / subPart == 2 (sag cift kanat)
            StructureRoleGlbRegistry.ResolveDoorLeafGlbAssetIndex(in node, out bool isDoubleLeaf);
            bool isOpen = node.IsDoorOrWindowOpen;

            if (!isDoubleLeaf)
            {
                // Tek kanat menteseli kapi: -0.48m sol mentese ekseni etrafinda 0° <-> +90° iceri (+Z) doner
                Vector3 hingeLocal = new Vector3(-0.48f, 0.0f, 0.0f);
                Quaternion leafLocalRot = isOpen ? Quaternion.Euler(0f, -90.0f, 0f) : Quaternion.identity;
                Vector3 leafCenterFromHinge = new Vector3(0.48f, 0.0f, 0.0f);
                Vector3 localPos = hingeLocal + (leafLocalRot * leafCenterFromHinge);
                return Matrix4x4.TRS(basePos + (baseRot * localPos), baseRot * leafLocalRot, Vector3.one);
            }
            else
            {
                // Cift kanatli anitsal/saray kapisi: sol (-1.18m) ve sag (+1.18m) menteselerden 90° iceri acilir
                float sideSign = subPart == 1u ? -1.0f : 1.0f;
                Vector3 hingeLocal = new Vector3(sideSign * 1.18f, 0.0f, 0.0f);
                Quaternion leafLocalRot = isOpen
                    ? Quaternion.Euler(0f, sideSign * 90.0f, 0f)
                    : Quaternion.identity;
                Vector3 leafCenterFromHinge = new Vector3(-sideSign * 0.59f, 0.0f, 0.0f);
                Vector3 localPos = hingeLocal + (leafLocalRot * leafCenterFromHinge);
                return Matrix4x4.TRS(basePos + (baseRot * localPos), baseRot * leafLocalRot, new Vector3(0.50f, 1.0f, 1.0f));
            }
        }

        private void RenderLod2HorizonSilhouettes(Material safeMat)
        {
            // Ufukta (160-750m+) gorunen metropol chunk'larini (standart egimli catili blok vs kubbeli saray/tapinak) cizer
            Mesh? gableSilhouette = null;
            Mesh? domedSilhouette = null;

            int gableCount = 0;
            for (int c = 0; c < _chunkCount; c++)
            {
                ref readonly CityChunkLodSlot slot = ref _chunkSlots[c];
                if (slot.CurrentLod != CityChunkLodTier.Lod2HorizonProxy || slot.HasDomedOrTowerSilhouette)
                {
                    continue;
                }

                gableSilhouette ??= GetOrCreateHorizonSilhouetteMesh(
                    slot.DominantFacadeMaterialId,
                    slot.DominantRoofMaterialId,
                    hasDome: false);

                float h = MathF.Max(3.2f, slot.MaxY - slot.MinY);
                Vector3 pos = new Vector3(slot.CenterX, slot.MinY, slot.CenterZ);
                Vector3 scl = new Vector3(12.5f, h, 10.5f);
                _sharedInstanceMatrices[gableCount++] = Matrix4x4.TRS(pos, Quaternion.identity, scl);

                if (gableCount == MaxInstancesPerDrawCall)
                {
                    Graphics.DrawMeshInstanced(gableSilhouette, 0, safeMat, _sharedInstanceMatrices, gableCount, _sharedMpb, ShadowCastingMode.Off, true);
                    gableCount = 0;
                }
            }

            if (gableCount > 0 && gableSilhouette != null)
            {
                Graphics.DrawMeshInstanced(gableSilhouette, 0, safeMat, _sharedInstanceMatrices, gableCount, _sharedMpb, ShadowCastingMode.Off, true);
            }

            int domeCount = 0;
            for (int c = 0; c < _chunkCount; c++)
            {
                ref readonly CityChunkLodSlot slot = ref _chunkSlots[c];
                if (slot.CurrentLod != CityChunkLodTier.Lod2HorizonProxy || !slot.HasDomedOrTowerSilhouette)
                {
                    continue;
                }

                domedSilhouette ??= GetOrCreateHorizonSilhouetteMesh(
                    MaterialIdCatalog.DressedMarbleBlock,
                    MaterialIdCatalog.RoofClayTile,
                    hasDome: true);

                float h = MathF.Max(6.5f, slot.MaxY - slot.MinY);
                Vector3 pos = new Vector3(slot.CenterX, slot.MinY, slot.CenterZ);
                Vector3 scl = new Vector3(14.5f, h, 14.5f);
                _sharedInstanceMatrices[domeCount++] = Matrix4x4.TRS(pos, Quaternion.identity, scl);

                if (domeCount == MaxInstancesPerDrawCall)
                {
                    Graphics.DrawMeshInstanced(domedSilhouette, 0, safeMat, _sharedInstanceMatrices, domeCount, _sharedMpb, ShadowCastingMode.Off, true);
                    domeCount = 0;
                }
            }

            if (domeCount > 0 && domedSilhouette != null)
            {
                Graphics.DrawMeshInstanced(domedSilhouette, 0, safeMat, _sharedInstanceMatrices, domeCount, _sharedMpb, ShadowCastingMode.Off, true);
            }
        }

        /// <summary>
        /// LOD2 (160-750m+) icin MaterialIdCatalog.GetBaseColorRgb24 renklerini kullanan,
        /// BottomCenter_Zero pivotlu, egimli catili veya kubbeli mimari siluet mesh'i uretir (ASLA ham beyaz kup degil!).
        /// </summary>
        private static Mesh GetOrCreateHorizonSilhouetteMesh(byte facadeMaterialId, byte roofMaterialId, bool hasDome)
        {
            int key = ((hasDome ? 1 : 0) << 16) | (facadeMaterialId << 8) | roofMaterialId;
            if (_horizonSilhouetteMeshCache.TryGetValue(key, out Mesh? cached) && cached != null)
            {
                return cached;
            }

            MaterialIdCatalog.GetBaseColorRgb(
                facadeMaterialId != 0 ? facadeMaterialId : MaterialIdCatalog.CutAshlarStone,
                out byte fr, out byte fg, out byte fb);
            MaterialIdCatalog.GetBaseColorRgb(
                roofMaterialId != 0 ? roofMaterialId : MaterialIdCatalog.RoofClayTile,
                out byte rr, out byte rg, out byte rb);

            if (fr is 0 or 3 or 11) fr++;
            if (rr is 0 or 3 or 11) rr++;

            Color32 wallCol = new Color32(fr, fg, fb, 245);
            Color32 wallShadeCol = new Color32(
                (byte)Mathf.Max(6, fr * 84 / 100),
                (byte)Mathf.Max(6, fg * 84 / 100),
                (byte)Mathf.Max(6, fb * 84 / 100),
                245);
            if (wallShadeCol.r is 3 or 11) wallShadeCol.r++;

            Color32 roofCol = new Color32(rr, rg, rb, 248);

            var verts = new List<Vector3>(64);
            var norms = new List<Vector3>(64);
            var cols = new List<Color32>(64);
            var tris = new List<int>(96);

            static void AddQuad(
                List<Vector3> vL, List<Vector3> nL, List<Color32> cL, List<int> tL,
                Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
            {
                int idx = vL.Count;
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                vL.Add(a); vL.Add(b); vL.Add(c); vL.Add(d);
                for (int k = 0; k < 4; k++) { nL.Add(n); cL.Add(col); }
                tL.Add(idx + 0); tL.Add(idx + 1); tL.Add(idx + 2);
                tL.Add(idx + 0); tL.Add(idx + 2); tL.Add(idx + 3);
            }

            // Alt cephe govdesi: X,Z in [-0.5..+0.5], Y in [0.0..0.72]
            const float eaveY = 0.72f;
            AddQuad(verts, norms, cols, tris,
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, eaveY, -0.5f),
                new Vector3(0.5f, eaveY, -0.5f), new Vector3(0.5f, 0f, -0.5f), wallCol);
            AddQuad(verts, norms, cols, tris,
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, eaveY, 0.5f),
                new Vector3(-0.5f, eaveY, 0.5f), new Vector3(-0.5f, 0f, 0.5f), wallCol);
            AddQuad(verts, norms, cols, tris,
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(-0.5f, eaveY, 0.5f),
                new Vector3(-0.5f, eaveY, -0.5f), new Vector3(-0.5f, 0f, -0.5f), wallShadeCol);
            AddQuad(verts, norms, cols, tris,
                new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, eaveY, -0.5f),
                new Vector3(0.5f, eaveY, 0.5f), new Vector3(0.5f, 0f, 0.5f), wallShadeCol);

            if (!hasDome)
            {
                // Egimli kırma/besik cati: mahya Y = 1.00f
                AddQuad(verts, norms, cols, tris,
                    new Vector3(-0.54f, eaveY, -0.54f), new Vector3(-0.54f, 1.0f, 0f),
                    new Vector3(0.54f, 1.0f, 0f), new Vector3(0.54f, eaveY, -0.54f), roofCol);
                AddQuad(verts, norms, cols, tris,
                    new Vector3(0.54f, eaveY, 0.54f), new Vector3(0.54f, 1.0f, 0f),
                    new Vector3(-0.54f, 1.0f, 0f), new Vector3(-0.54f, eaveY, 0.54f), roofCol);
                // Alinlik kapaklari
                AddQuad(verts, norms, cols, tris,
                    new Vector3(-0.5f, eaveY, 0.5f), new Vector3(-0.5f, 1.0f, 0f),
                    new Vector3(-0.5f, 1.0f, 0f), new Vector3(-0.5f, eaveY, -0.5f), wallShadeCol);
                AddQuad(verts, norms, cols, tris,
                    new Vector3(0.5f, eaveY, -0.5f), new Vector3(0.5f, 1.0f, 0f),
                    new Vector3(0.5f, 1.0f, 0f), new Vector3(0.5f, eaveY, 0.5f), wallShadeCol);
            }
            else
            {
                // Anitsal Saray / Tapinak sekizgen kasnak + kubbe taci (Y in [0.72..1.18])
                const int segs = 8;
                const float rDrum = 0.38f;
                const float drumTopY = 0.88f;
                const float domeTopY = 1.18f;
                for (int s = 0; s < segs; s++)
                {
                    float a0 = (s / (float)segs) * Mathf.PI * 2f;
                    float a1 = ((s + 1) / (float)segs) * Mathf.PI * 2f;
                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * rDrum, eaveY, Mathf.Sin(a0) * rDrum);
                    Vector3 p1 = new Vector3(Mathf.Cos(a1) * rDrum, eaveY, Mathf.Sin(a1) * rDrum);
                    Vector3 d0 = new Vector3(Mathf.Cos(a0) * rDrum, drumTopY, Mathf.Sin(a0) * rDrum);
                    Vector3 d1 = new Vector3(Mathf.Cos(a1) * rDrum, drumTopY, Mathf.Sin(a1) * rDrum);
                    Vector3 apex = new Vector3(0f, domeTopY, 0f);
                    AddQuad(verts, norms, cols, tris, p0, d0, d1, p1, wallCol);
                    AddQuad(verts, norms, cols, tris, d0, apex, apex, d1, roofCol);
                }
            }

            Mesh mesh = new Mesh
            {
                name = hasDome ? $"CityLod2_DomedSilhouette_{key}" : $"CityLod2_PitchedSilhouette_{key}",
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            _horizonSilhouetteMeshCache[key] = mesh;
            return mesh;
        }
#endif
    }
}
