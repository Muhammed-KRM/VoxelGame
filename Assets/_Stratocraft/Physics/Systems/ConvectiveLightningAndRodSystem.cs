#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Physics.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Systems
{
    /// <summary>
    /// Yildirim hedef adayi (0 B GC stack/span veri yapisi).
    /// </summary>
    public readonly struct LightningTargetCandidate
    {
        public readonly int WorldX;
        public readonly int WorldYTop;
        public readonly int WorldZ;
        public readonly bool IsLightningRod;
        public readonly bool IsWater;
        public readonly bool IsFlammable;
        public readonly bool HasOpenSkyAbove;

        public LightningTargetCandidate(
            int worldX,
            int worldYTop,
            int worldZ,
            bool isLightningRod,
            bool isWater = false,
            bool isFlammable = false,
            bool hasOpenSkyAbove = true)
        {
            WorldX = worldX;
            WorldYTop = worldYTop;
            WorldZ = worldZ;
            IsLightningRod = isLightningRod;
            IsWater = isWater;
            IsFlammable = isFlammable;
            HasOpenSkyAbove = hasOpenSkyAbove;
        }
    }

    /// <summary>
    /// Konvektif yildirim dusus sonucu ve gok gurultusu akustik gecikme cikisi.
    /// </summary>
    public readonly struct LightningStrikeResult
    {
        public readonly bool DidStrike;
        public readonly int TargetWorldX;
        public readonly int TargetWorldY;
        public readonly int TargetWorldZ;
        public readonly float SelectionScore;
        public readonly bool HitLightningRod;
        public readonly bool HarmlesslyGrounded;
        public readonly bool IgnitedTarget;
        public readonly float ThunderAudioDelaySeconds;

        public bool DivertedToLightningRod => HitLightningRod;

        public LightningStrikeResult(
            bool didStrike,
            int targetWorldX,
            int targetWorldY,
            int targetWorldZ,
            float selectionScore,
            bool hitLightningRod,
            bool harmlesslyGrounded,
            bool ignitedTarget,
            float thunderAudioDelaySeconds)
        {
            DidStrike = didStrike;
            TargetWorldX = targetWorldX;
            TargetWorldY = targetWorldY;
            TargetWorldZ = targetWorldZ;
            SelectionScore = selectionScore;
            HitLightningRod = hitLightningRod;
            HarmlesslyGrounded = harmlesslyGrounded;
            IgnitedTarget = ignitedTarget;
            ThunderAudioDelaySeconds = thunderAudioDelaySeconds;
        }
    }

    /// <summary>
    /// Paket 4A Faz 4A.4 (IP-6.4 / AK-9) — CAPE Konvektif Yildirim, Gok Gurultusu Gecikmesi ve Metal Paratoner Sistemi:
    /// - Yalnizca <c>capeJPerKg &gt; 1000</c> konvektif kararsizlik altinda yildirim uretir (0 rastgele sahte zamanlayici).
    /// - Hedef secim skoru: <c>S = yTop + 24 * isRod + 8 * isWater - 0.25 * horizDist</c>.
    /// - <c>24m</c> yatay koruma konisi icinde acik gokyuzu goren bir Metal Paratoner varsa yildirim %100 paratonere cekilir
    ///   ve hasarsiz/yanginsiz (<c>IgnitedTarget = false</c>) topraga iletilir.
    /// - Koruma disindaki en yuksek acik agac veya ahsap/saman cati vuruldugunda tutusur (<c>IgnitedTarget = true</c>).
    /// - Gok gurultusu sesi dinleyiciye olan 3B mesafeye gore <c>distance / 343.0f</c> saniye gecikmeyle ulasir.
    /// </summary>
    public sealed class ConvectiveLightningAndRodSystem
    {
        public const ushort MinConvectiveCapeJPerKg = 1000;
        public const float LightningRodProtectionRadiusMeters = 24.0f;
        public const float SpeedOfSoundMetersPerSecond = 343.0f;
        public const int MaxRegisteredRods = 256;

        private struct RegisteredRodEntry
        {
            public int WorldX;
            public int WorldYTop;
            public int WorldZ;
            public byte Active;
        }

        private readonly RegisteredRodEntry[] _registeredRods = new RegisteredRodEntry[MaxRegisteredRods];
        private int _rodCount;
        private float _convectiveChargeAccumulator;

        public int RegisteredRodCount => _rodCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConvectiveLightningActive(ushort capeJPerKg)
        {
            return capeJPerKg > MinConvectiveCapeJPerKg;
        }

        /// <summary>
        /// Kanonik yildirim hedef secim skoru:
        /// <c>S = yTop + 24 * isRod + 8 * isWater - 0.25 * horizDist</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeTargetScore(float yTop, bool isRod, bool isWater, float horizDist)
        {
            float score = yTop - 0.25f * MathF.Max(0.0f, horizDist);
            if (isRod) score += 24.0f;
            if (isWater) score += 8.0f;
            return score;
        }

        /// <summary>
        /// Yildirim dusus noktasi ile dinleyici arasindaki 3B mesafeden gok gurultusu ses gecikmesini (<c>d / 343.0f</c>) hesaplar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeThunderDelaySeconds(
            float strikeX, float strikeY, float strikeZ,
            float listenerX, float listenerY, float listenerZ)
        {
            float dx = strikeX - listenerX;
            float dy = strikeY - listenerY;
            float dz = strikeZ - listenerZ;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            return dist / SpeedOfSoundMetersPerSecond;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeThunderDelaySeconds(float distanceMeters)
        {
            return MathF.Max(0.0f, distanceMeters) / SpeedOfSoundMetersPerSecond;
        }

        /// <summary>
        /// Acik gokyuzu altindaki voksel malzemesinin yildirim dusunce tutusabilir (agac govdesi, kalas, saman cati) olup olmadigini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFlammableLightningTargetMaterial(byte materialId)
        {
            return materialId == MaterialIdCatalog.OakWood ||
                   materialId == MaterialIdCatalog.Ironwood ||
                   materialId == MaterialIdCatalog.SpiritWood ||
                   materialId == MaterialIdCatalog.YggdrasilBranch ||
                   materialId == MaterialIdCatalog.WoodPlank ||
                   materialId == MaterialIdCatalog.ThatchTile ||
                   materialId == MaterialIdCatalog.TacticalCloth;
        }

        public bool RegisterLightningRod(int worldX, int worldYTop, int worldZ)
        {
            for (int i = 0; i < _rodCount; i++)
            {
                ref RegisteredRodEntry cur = ref _registeredRods[i];
                if (cur.Active != 0 && cur.WorldX == worldX && cur.WorldZ == worldZ)
                {
                    cur.WorldYTop = worldYTop;
                    return true;
                }
            }

            if (_rodCount >= MaxRegisteredRods)
            {
                return false;
            }

            _registeredRods[_rodCount] = new RegisteredRodEntry
            {
                WorldX = worldX,
                WorldYTop = worldYTop,
                WorldZ = worldZ,
                Active = 1
            };
            _rodCount++;
            return true;
        }

        public bool UnregisterLightningRod(int worldX, int worldZ)
        {
            for (int i = 0; i < _rodCount; i++)
            {
                if (_registeredRods[i].Active != 0 &&
                    _registeredRods[i].WorldX == worldX &&
                    _registeredRods[i].WorldZ == worldZ)
                {
                    _rodCount--;
                    if (i < _rodCount)
                    {
                        _registeredRods[i] = _registeredRods[_rodCount];
                    }

                    _registeredRods[_rodCount] = default;
                    return true;
                }
            }

            return false;
        }

        public void ClearRegisteredRods()
        {
            Array.Clear(_registeredRods, 0, _rodCount);
            _rodCount = 0;
        }

        /// <summary>
        /// Verilen aday hedefler arasindan <c>capeJPerKg &gt; 1000</c> kosulunda yildirim hedefini secer.
        /// Eger secilen hedefin veya yildirim sutununun <c>24m</c> koruma konisi icinde acik bir Metal Paratoner varsa,
        /// yildirim %100 paratonere cekilir ve hasarsiz topraga iletilir.
        /// </summary>
        public static bool TryResolveLightningStrike(
            ushort capeJPerKg,
            float boltOriginX,
            float boltOriginZ,
            ReadOnlySpan<LightningTargetCandidate> candidates,
            float listenerX,
            float listenerY,
            float listenerZ,
            out LightningStrikeResult strikeResult)
        {
            strikeResult = default;
            if (!IsConvectiveLightningActive(capeJPerKg) || candidates.Length == 0)
            {
                return false;
            }

            int bestIdx = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref readonly LightningTargetCandidate c = ref candidates[i];
                if (!c.HasOpenSkyAbove)
                {
                    continue;
                }

                float dx = c.WorldX - boltOriginX;
                float dz = c.WorldZ - boltOriginZ;
                float horizDist = MathF.Sqrt(dx * dx + dz * dz);
                float s = ComputeTargetScore(c.WorldYTop, c.IsLightningRod, c.IsWater, horizDist);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                return false;
            }

            LightningTargetCandidate chosen = candidates[bestIdx];
            float chosenScore = bestScore;

            // 24m Koruma Konisi Kurali: Eger ham en yuksek aday paratoner degilse bile, bolge/aday etrafinda <= 24m icinde
            // acik gokyuzu goren bir Metal Paratoner varsa yildirim %100 o paratonere saptirilir!
            if (!chosen.IsLightningRod)
            {
                const float coneSq = LightningRodProtectionRadiusMeters * LightningRodProtectionRadiusMeters;
                int bestRodIdx = -1;
                float bestRodScore = float.NegativeInfinity;

                for (int i = 0; i < candidates.Length; i++)
                {
                    ref readonly LightningTargetCandidate rod = ref candidates[i];
                    if (!rod.IsLightningRod || !rod.HasOpenSkyAbove)
                    {
                        continue;
                    }

                    float dxTarget = rod.WorldX - chosen.WorldX;
                    float dzTarget = rod.WorldZ - chosen.WorldZ;
                    float distSqToTarget = dxTarget * dxTarget + dzTarget * dzTarget;

                    float dxBolt = rod.WorldX - boltOriginX;
                    float dzBolt = rod.WorldZ - boltOriginZ;
                    float distSqToBolt = dxBolt * dxBolt + dzBolt * dzBolt;

                    if (distSqToTarget <= coneSq || distSqToBolt <= coneSq)
                    {
                        float horizDist = MathF.Sqrt(distSqToBolt);
                        float rodScore = ComputeTargetScore(rod.WorldYTop, isRod: true, rod.IsWater, horizDist);
                        if (rodScore > bestRodScore)
                        {
                            bestRodScore = rodScore;
                            bestRodIdx = i;
                        }
                    }
                }

                if (bestRodIdx >= 0)
                {
                    chosen = candidates[bestRodIdx];
                    chosenScore = bestRodScore;
                }
            }

            bool hitRod = chosen.IsLightningRod;
            bool harmlesslyGrounded = hitRod;
            bool ignited = !hitRod && !chosen.IsWater && chosen.IsFlammable;
            float delaySec = ComputeThunderDelaySeconds(
                chosen.WorldX, chosen.WorldYTop, chosen.WorldZ,
                listenerX, listenerY, listenerZ);

            strikeResult = new LightningStrikeResult(
                didStrike: true,
                targetWorldX: chosen.WorldX,
                targetWorldY: chosen.WorldYTop,
                targetWorldZ: chosen.WorldZ,
                selectionScore: chosenScore,
                hitLightningRod: hitRod,
                harmlesslyGrounded: harmlesslyGrounded,
                ignitedTarget: ignited,
                thunderAudioDelaySeconds: delaySec);
            return true;
        }

        /// <summary>
        /// Voksel dunyasi chunk'larini ve kayitli Metal Paratoner'leri 0 B GC ile tarayarak yildirim dususunu cozer.
        /// </summary>
        public bool TryStrikeWorldAtStormColumn(
            ushort capeJPerKg,
            float boltOriginX,
            float boltOriginZ,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]>? fluidChunks,
            IReadOnlyDictionary<(int cx, int cz), ChunkSkyOcclusionMap256>? skyOcclusionMaps,
            float listenerX,
            float listenerY,
            float listenerZ,
            out LightningStrikeResult strikeResult,
            int searchRadiusBlocks = 24)
        {
            strikeResult = default;
            if (!IsConvectiveLightningActive(capeJPerKg) || worldChunks == null || worldChunks.Count == 0)
            {
                return false;
            }

            int radius = Math.Clamp(searchRadiusBlocks, 4, 32);
            int centerWx = (int)MathF.Floor(boltOriginX);
            int centerWz = (int)MathF.Floor(boltOriginZ);

            // Once 24m koruma konisi icindeki kayitli acik gokyuzulu paratonerleri kontrol et
            const float coneSq = LightningRodProtectionRadiusMeters * LightningRodProtectionRadiusMeters;
            int bestRodIdx = -1;
            float bestRodScore = float.NegativeInfinity;

            for (int i = 0; i < _rodCount; i++)
            {
                ref readonly RegisteredRodEntry rod = ref _registeredRods[i];
                if (rod.Active == 0)
                {
                    continue;
                }

                float dxBolt = rod.WorldX - boltOriginX;
                float dzBolt = rod.WorldZ - boltOriginZ;
                float distSq = dxBolt * dxBolt + dzBolt * dzBolt;
                if (distSq > coneSq)
                {
                    continue;
                }

                if (!IsWorldPositionOpenToSky(rod.WorldX, rod.WorldYTop, rod.WorldZ, worldChunks, skyOcclusionMaps))
                {
                    continue;
                }

                float s = ComputeTargetScore(rod.WorldYTop, isRod: true, isWater: false, MathF.Sqrt(distSq));
                if (s > bestRodScore)
                {
                    bestRodScore = s;
                    bestRodIdx = i;
                }
            }

            if (bestRodIdx >= 0)
            {
                RegisteredRodEntry winRod = _registeredRods[bestRodIdx];
                float rodDelay = ComputeThunderDelaySeconds(
                    winRod.WorldX, winRod.WorldYTop, winRod.WorldZ,
                    listenerX, listenerY, listenerZ);
                strikeResult = new LightningStrikeResult(
                    didStrike: true,
                    targetWorldX: winRod.WorldX,
                    targetWorldY: winRod.WorldYTop,
                    targetWorldZ: winRod.WorldZ,
                    selectionScore: bestRodScore,
                    hitLightningRod: true,
                    harmlesslyGrounded: true,
                    ignitedTarget: false,
                    thunderAudioDelaySeconds: rodDelay);
                return true;
            }

            // Paratoner yoksa yaricap icindeki en yuksek acik gokyuzu sutununu skorla
            bool foundTerrain = false;
            int bestWx = centerWx;
            int bestWy = 0;
            int bestWz = centerWz;
            bool bestFlammable = false;
            float bestTerrainScore = float.NegativeInfinity;

            int cachedCx = int.MinValue;
            int cachedCz = int.MinValue;
            uint[]? cachedVoxels = null;
            ushort[]? cachedFluids = null;

            for (int wz = centerWz - radius; wz <= centerWz + radius; wz++)
            {
                int cz = FloorDiv16(wz);
                int lz = wz - cz * 16;
                for (int wx = centerWx - radius; wx <= centerWx + radius; wx++)
                {
                    float dx = wx - boltOriginX;
                    float dz = wz - boltOriginZ;
                    float dSq = dx * dx + dz * dz;
                    if (dSq > radius * radius)
                    {
                        continue;
                    }

                    int cx = FloorDiv16(wx);
                    if (cx != cachedCx || cz != cachedCz)
                    {
                        cachedCx = cx;
                        cachedCz = cz;
                        worldChunks.TryGetValue((cx, cz), out cachedVoxels);
                        cachedFluids = null;
                        fluidChunks?.TryGetValue((cx, cz), out cachedFluids);
                    }

                    if (cachedVoxels == null)
                    {
                        continue;
                    }

                    int lx = wx - cx * 16;
                    int col = (lz << 4) | lx;
                    int maxY = (cachedVoxels.Length >> 8) - 1;

                    int topY = -1;
                    bool topIsWater = false;
                    byte topMat = MaterialIdCatalog.Air;

                    for (int y = maxY; y >= 1; y--)
                    {
                        int idx = (y << 8) | col;
                        if (cachedFluids != null && idx < cachedFluids.Length && cachedFluids[idx] != 0)
                        {
                            FluidCell16 fc = new FluidCell16(cachedFluids[idx]);
                            if (fc.Type == FluidType.Water && fc.Mass > 0)
                            {
                                topY = y;
                                topIsWater = true;
                                break;
                            }
                        }

                        uint raw = cachedVoxels[idx];
                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (mat != MaterialIdCatalog.Air && VoxelBitUtility.IsPhysicallySolid(raw))
                        {
                            topY = y;
                            topMat = mat;
                            break;
                        }
                    }

                    if (topY < 1)
                    {
                        continue;
                    }

                    float s = ComputeTargetScore(topY, isRod: false, topIsWater, MathF.Sqrt(dSq));
                    if (s > bestTerrainScore)
                    {
                        bestTerrainScore = s;
                        bestWx = wx;
                        bestWy = topY;
                        bestWz = wz;
                        bestFlammable = !topIsWater && IsFlammableLightningTargetMaterial(topMat);
                        foundTerrain = true;
                    }
                }
            }

            if (!foundTerrain)
            {
                return false;
            }

            // Secilen en yuksek hedefe <= 24m mesafede kayitli bir paratoner var mi son kez kontrol et
            for (int i = 0; i < _rodCount; i++)
            {
                ref readonly RegisteredRodEntry rod = ref _registeredRods[i];
                if (rod.Active == 0) continue;
                float rdx = rod.WorldX - bestWx;
                float rdz = rod.WorldZ - bestWz;
                if (rdx * rdx + rdz * rdz <= coneSq &&
                    IsWorldPositionOpenToSky(rod.WorldX, rod.WorldYTop, rod.WorldZ, worldChunks, skyOcclusionMaps))
                {
                    float boltDist = MathF.Sqrt((rod.WorldX - boltOriginX) * (rod.WorldX - boltOriginX) +
                                                (rod.WorldZ - boltOriginZ) * (rod.WorldZ - boltOriginZ));
                    float rodScore = ComputeTargetScore(rod.WorldYTop, isRod: true, isWater: false, boltDist);
                    float rodDelay = ComputeThunderDelaySeconds(
                        rod.WorldX, rod.WorldYTop, rod.WorldZ,
                        listenerX, listenerY, listenerZ);
                    strikeResult = new LightningStrikeResult(
                        didStrike: true,
                        targetWorldX: rod.WorldX,
                        targetWorldY: rod.WorldYTop,
                        targetWorldZ: rod.WorldZ,
                        selectionScore: rodScore,
                        hitLightningRod: true,
                        harmlesslyGrounded: true,
                        ignitedTarget: false,
                        thunderAudioDelaySeconds: rodDelay);
                    return true;
                }
            }

            float delay = ComputeThunderDelaySeconds(
                bestWx, bestWy, bestWz,
                listenerX, listenerY, listenerZ);

            strikeResult = new LightningStrikeResult(
                didStrike: true,
                targetWorldX: bestWx,
                targetWorldY: bestWy,
                targetWorldZ: bestWz,
                selectionScore: bestTerrainScore,
                hitLightningRod: false,
                harmlesslyGrounded: false,
                ignitedTarget: bestFlammable,
                thunderAudioDelaySeconds: delay);
            return true;
        }

        /// <summary>
        /// Deterministik konvektif sarj birikimi: yalnizca <c>capeJPerKg &gt; 1000</c> iken sarj doldurur,
        /// esik asildiginda yildirim cakmasini tetikler (<c>capeJPerKg &lt;= 1000</c> iken 0 sarj).
        /// </summary>
        public bool StepConvectiveCharge(ushort capeJPerKg, float precipMmPerHour, float gameDeltaSeconds)
        {
            if (!IsConvectiveLightningActive(capeJPerKg) || gameDeltaSeconds <= 0.0f)
            {
                _convectiveChargeAccumulator = MathF.Max(0.0f, _convectiveChargeAccumulator - 0.02f * MathF.Max(0.0f, gameDeltaSeconds));
                return false;
            }

            float excessCape = capeJPerKg - MinConvectiveCapeJPerKg;
            float precipBoost = 1.0f + Math.Clamp(precipMmPerHour / 20.0f, 0.0f, 1.5f);
            // CAPE = 2000 J/kg ve kuvetli yagista ~25-45 oyun saniyesinde 1 konvektif desarj
            float chargeRatePerSec = (0.008f + excessCape * 0.000018f) * precipBoost;
            _convectiveChargeAccumulator += chargeRatePerSec * gameDeltaSeconds;

            if (_convectiveChargeAccumulator >= 1.0f)
            {
                _convectiveChargeAccumulator -= MathF.Floor(_convectiveChargeAccumulator);
                return true;
            }

            return false;
        }

        private static bool IsWorldPositionOpenToSky(
            int wx,
            int wy,
            int wz,
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            IReadOnlyDictionary<(int cx, int cz), ChunkSkyOcclusionMap256>? skyOcclusionMaps)
        {
            int cx = FloorDiv16(wx);
            int cz = FloorDiv16(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;

            if (skyOcclusionMaps != null && skyOcclusionMaps.TryGetValue((cx, cz), out ChunkSkyOcclusionMap256? skyMap) && skyMap != null)
            {
                return skyMap.HasOpenSkyAbove(lx, wy, lz);
            }

            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null)
            {
                return true;
            }

            int maxY = (voxels.Length >> 8) - 1;
            int col = (lz << 4) | lx;
            for (int y = wy + 1; y <= maxY; y++)
            {
                if (ChunkSkyOcclusionMap256.BlocksSkyOxygenColumn(voxels[(y << 8) | col]))
                {
                    return false;
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv16(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);
    }
}
