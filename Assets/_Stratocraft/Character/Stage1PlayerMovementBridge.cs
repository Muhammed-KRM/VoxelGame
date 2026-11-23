#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Character.Systems;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.Inventory.Systems;
using Stratocraft.Physics.Data;
using Stratocraft.Physics.Systems;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 1 — Anti-Gomulme (Depenetration), Kapsul-Arazi Carpismasi ve Fiziksel Karakter Hareket Koprusu.
    /// <see cref="PhysicalCharacterControllerSystem"/> motorunu sahnedeki oyuncu transformuna baglar;
    /// oyuncunun kendi icine blok koyup dunyadan dusmesini (%100 Anti-Burial CheckCapsule + Depenetration) onler.
    /// </summary>
    public sealed class Stage1PlayerMovementBridge
    {
        /// <summary>
        /// Saf C# (.NET 10 &amp; Unity 6) kapsul-blok kesisim kontrolu.
        /// Oyuncunun hedef voksel hucresine (wx, wy, wz) blok koymadan once kendi kapsulu ile
        /// kesisip kesismedigini kontrol eder.
        /// </summary>
        public static bool CanPlaceBlockOutsidePlayerCapsule(
            int wx, int wy, int wz, float playerX, float playerY, float playerZ, float capsuleHeight, float brushRadius = 0.5f)
        {
            float radius = 0.36f;
            float height = MathF.Max(0.45f, capsuleHeight);
            float halfExtent = MathF.Max(0.48f, brushRadius * 0.85f);
            float bx = wx + 0.5f, by = wy + 0.5f, bz = wz + 0.5f;

            bool overlapsX = (bx + halfExtent) > (playerX - radius) && (bx - halfExtent) < (playerX + radius);
            bool overlapsY = (by + halfExtent) > (playerY + 0.05f) && (by - halfExtent) < (playerY + height);
            bool overlapsZ = (bz + halfExtent) > (playerZ - radius) && (bz - halfExtent) < (playerZ + radius);
            return !(overlapsX && overlapsY && overlapsZ);
        }

        /// <summary>
        /// Oyuncu suya girdiginde veya hizla yuzdugunde merkez hucredeki suyun kucuk bir kismini (maxPushUnits)
        /// komsu acik hucrelere esit dagitarak %100 kutle korunumlu (net degisim = 0) fiziksel calkanti dalgasi olusturur.
        /// </summary>
        public static int ApplyMassConservingSwimmerSurge(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            HashSet<(int cx, int cz)>? activeFluidChunks,
            int wx,
            int wy,
            int wz,
            int maxPushUnits = 16)
        {
            if (wy < 1 || wy >= 254 || maxPushUnits <= 0)
            {
                return 0;
            }

            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? centerFluids) || centerFluids == null)
            {
                return 0;
            }

            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            int centerIdx = (wy << 8) | (lz << 4) | lx;
            if ((uint)centerIdx >= (uint)centerFluids.Length)
            {
                return 0;
            }

            FluidCell16 src = new FluidCell16(centerFluids[centerIdx]);
            if (src.Type != FluidType.Water || src.Mass < 24)
            {
                return 0;
            }

            int budget = Math.Min(maxPushUnits, src.Mass - 12);
            if (budget <= 0)
            {
                return 0;
            }

            ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };
            int perDir = Math.Max(2, budget / 4);
            int totalMoved = 0;

            for (int d = 0; d < 4 && totalMoved < budget; d++)
            {
                int nwx = wx + dx[d];
                int nwz = wz + dz[d];
                int ncx = nwx >= 0 ? (nwx >> 4) : -(((-nwx) + 15) >> 4);
                int ncz = nwz >= 0 ? (nwz >> 4) : -(((-nwz) + 15) >> 4);
                if (!fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nFluids) || nFluids == null ||
                    !worldChunks.TryGetValue((ncx, ncz), out uint[]? nSolids) || nSolids == null)
                {
                    continue;
                }

                int nlx = nwx - ncx * 16;
                int nlz = nwz - ncz * 16;
                int nIdx = (wy << 8) | (nlz << 4) | nlx;
                if ((uint)nIdx >= (uint)nFluids.Length || (uint)nIdx >= (uint)nSolids.Length)
                {
                    continue;
                }

                byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(nSolids[nIdx]);
                if (cap == 0)
                {
                    continue;
                }

                FluidCell16 dst = new FluidCell16(nFluids[nIdx]);
                if (dst.Type != FluidType.None && dst.Type != FluidType.Water)
                {
                    continue;
                }

                int space = cap - dst.Mass;
                if (space <= 0)
                {
                    continue;
                }

                int move = Math.Min(perDir, Math.Min(budget - totalMoved, space));
                if (move <= 0)
                {
                    continue;
                }

                nFluids[nIdx] = FluidCell16.Pack(FluidType.Water, (byte)(dst.Mass + move), isSettled: false, dst.PressureHead).RawValue;
                totalMoved += move;
                dirtyFluidChunks?.Add((ncx, ncz));
                activeFluidChunks?.Add((ncx, ncz));
            }

            if (totalMoved > 0)
            {
                centerFluids[centerIdx] = FluidCell16.Pack(FluidType.Water, (byte)(src.Mass - totalMoved), isSettled: false, src.PressureHead).RawValue;
                dirtyFluidChunks?.Add((cx, cz));
                activeFluidChunks?.Add((cx, cz));
            }

            return totalMoved;
        }

        public static void ComputeEffectiveFluidSurfaceAndBed(
            ReadOnlySpan<ushort> fluidCells,
            ReadOnlySpan<uint> solidVoxels,
            int col,
            int fluidWy,
            out float bedY,
            out float surfY)
        {
            int curIdx = (fluidWy << 8) | col;
            int belowIdx = ((fluidWy - 1) << 8) | col;
            FluidCell16 curCell = new FluidCell16(fluidCells[curIdx]);
            uint belowRaw = fluidWy > 0 && belowIdx < solidVoxels.Length ? solidVoxels[belowIdx] : 0u;
            uint curRaw = curIdx < solidVoxels.Length ? solidVoxels[curIdx] : 0u;

            // Alt hucre kati degil ama su/sivi ile doluysa (derin gol/deniz sutunu), ustteki hucrenin tabani tam fluidWy kotudur!
            if (fluidWy > 0 && !VoxelBitUtility.IsPhysicallySolid(belowRaw) && belowIdx < fluidCells.Length)
            {
                FluidCell16 belowFluid = new FluidCell16(fluidCells[belowIdx]);
                if (belowFluid.Type != FluidType.None && belowFluid.Mass >= 16)
                {
                    bedY = fluidWy;
                    surfY = MathF.Min(fluidWy + 1.0f, bedY + FluidSurfaceSkirtMesher.MassToFluidThickness(curCell.Mass));
                    return;
                }
            }

            int bedUnits = FluidPorosityUtility.ComputeCellBedElevationUnits255(belowRaw, curRaw, fluidWy);
            byte cap = FluidPorosityUtility.ComputeCellFluidCapacity(curRaw);
            bedY = bedUnits * (1.0f / 255.0f);
            surfY = FluidPorosityUtility.ComputeCellHydrostaticHeadUnits255(bedUnits, fluidWy, curCell.Mass, cap) * (1.0f / 255.0f);
        }

        /// <summary>
        /// Paket 2 İP-4 (#16 Kafa Su Üstündeyken Oksijen Hatası Çözümü):
        /// Tam sayı FloorToInt(pos.y + 1.45f) yerine karakterin ağız/burun kotunu (playerFeetY + 1.52f),
        /// alt-voksel gerçek görsel sıvı yüzeyi kotu (bedY + ComputeVisualFluidThickness) ve 4cm histerezis bandı
        /// ile karşılaştırır. Kafa su yüzeyinin üstündeyken headSubmerged=false ve submergedDepthMeters=0 döner.
        /// </summary>
        public static void ComputeBreathSubmersionState(
            ReadOnlySpan<ushort> fluidCells,
            ReadOnlySpan<uint> solidVoxels,
            float playerFeetY,
            int lx,
            int lz,
            int chunkHeight,
            bool previouslyHeadSubmerged,
            out bool headSubmerged,
            out float submergedDepthMeters,
            out int breathAirWy)
        {
            float breathY = playerFeetY + 1.52f;
            int maxValidY = Math.Max(2, chunkHeight - 2);
            int wy = Math.Clamp((int)MathF.Floor(breathY), 1, maxValidY);
            breathAirWy = wy;
            headSubmerged = false;
            submergedDepthMeters = 0f;

            if ((uint)lx >= 16u || (uint)lz >= 16u) return;
            int col = (lz << 4) | lx;
            int curIdx = (wy << 8) | col;
            int aboveIdx = ((wy + 1) << 8) | col;
            if (aboveIdx >= fluidCells.Length) return;

            FluidCell16 curCell = new FluidCell16(fluidCells[curIdx]);
            FluidCell16 aboveCell = new FluidCell16(fluidCells[aboveIdx]);

            if (curCell.Type == FluidType.None || curCell.Mass == 0)
            {
                // SurfaceNets kiyi tabaninda (bedY < wy+1) ust hucredeki (wy+1) su katmani alt hucre kotuna sarkabilir
                if (aboveCell.Type != FluidType.None && aboveCell.Mass > 0)
                {
                    ComputeEffectiveFluidSurfaceAndBed(fluidCells, solidVoxels, col, wy + 1, out float aboveBedY, out float aboveSurfY);
                    float aboveThresholdY = previouslyHeadSubmerged ? (aboveSurfY + 0.015f) : (aboveSurfY - 0.025f);
                    if (breathY >= aboveBedY - 0.05f && breathY < aboveThresholdY)
                    {
                        headSubmerged = true;
                        submergedDepthMeters = MathF.Max(0.12f, MathF.Max(aboveSurfY - breathY, aboveCell.PressureHead * 2.0f));
                    }
                }
                return;
            }

            if (aboveCell.Type != FluidType.None && aboveCell.Mass >= 16)
            {
                headSubmerged = true;
                int topFluidWy = wy + 1;
                int maxScanY = Math.Min(maxValidY, wy + 60);
                for (int sy = wy + 1; sy <= maxScanY; sy++)
                {
                    FluidCell16 c = new FluidCell16(fluidCells[(sy << 8) | col]);
                    if (c.Type == FluidType.None || c.Mass < 16) break;
                    topFluidWy = sy;
                }

                ComputeEffectiveFluidSurfaceAndBed(fluidCells, solidVoxels, col, topFluidWy, out _, out float topSurfY);
                float columnDepth = MathF.Max(0.20f, topSurfY - breathY);
                submergedDepthMeters = MathF.Max(columnDepth, curCell.PressureHead * 2.0f);
                return;
            }

            ComputeEffectiveFluidSurfaceAndBed(fluidCells, solidVoxels, col, wy, out _, out float surfY);
            float thresholdY = previouslyHeadSubmerged ? (surfY + 0.015f) : (surfY - 0.025f);

            if (breathY < thresholdY)
            {
                headSubmerged = true;
                submergedDepthMeters = MathF.Max(0.12f, MathF.Max(surfY - breathY, curCell.PressureHead * 2.0f));
            }
            else
            {
                headSubmerged = false;
                submergedDepthMeters = 0f;
                // Ağız/burun su yüzeyinin üstündeyken wy hücresinin tabanında kısmi su varsa,
                // EnclosedOxygenFloodFillSystem'in üstteki açık havayı ölçmesi için wy+1 hücresini hedefle.
                if (curCell.Mass >= 48 && aboveIdx < solidVoxels.Length && !VoxelBitUtility.IsPhysicallySolid(solidVoxels[aboveIdx]))
                {
                    breathAirWy = wy + 1;
                }
            }
        }

        /// <summary>Kati kor MagmaBlock (11) uzerine basildiginda veya govde temasinda saniyelik yanik hasari (HP/s).</summary>
        public const float MagmaBlockContactDamagePerSecond = 9.0f;

        /// <summary>Kati kor MagmaBlock (11) temasinda saniyelik vucut cekirdek isi artisi (C/s).</summary>
        public const float MagmaBlockCoreHeatGainCelsiusPerSecond = 2.8f;

        /// <summary>Kati blok icinde sikisma / toprak altinda bogulma saniyelik ezilme hasari (HP/s).</summary>
        public const float SolidBlockSuffocationDamagePerSecond = 6.0f;

        /// <summary>Kati blok icinde sikisma / gomulme sirasinda akciger oksijen kaybi (%/s).</summary>
        public const float SolidBlockSuffocationOxygenDrainPerSecond = 28.0f;

        /// <summary>Saf C# kati voksel carpismasi kontrolu (Air ve GlowingMoss haric tum fiziksel kati vokseller).</summary>
        public static bool IsHardCollisionVoxelPure(uint raw)
        {
            if (!VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            return mat != MaterialIdCatalog.Air && mat != MaterialIdCatalog.GlowingMoss;
        }

        /// <summary>
        /// HybridSurfaceNetsAndShapeMesher (IsoLevel = 64) ile birebir ayni dikey kenar kesisimini saf C# olarak hesaplar.
        /// </summary>
        public static float GetVoxelWalkableTopYPure(uint raw, uint aboveRaw, int wy)
        {
            if (VoxelBitUtility.GetShapeId(raw) != 0)
            {
                return wy + 1.0f;
            }

            int d = VoxelBitUtility.GetDensityOrLiquid(raw);
            int dAbove = (VoxelBitUtility.GetMaterialId(aboveRaw) != MaterialIdCatalog.Air &&
                          VoxelBitUtility.GetShapeId(aboveRaw) == 0)
                ? VoxelBitUtility.GetDensityOrLiquid(aboveRaw)
                : 0;
            float denom = MathF.Max(1f, d - dAbove);
            float t = Math.Clamp((d - VoxelBitUtility.IsoDensity) / denom, 0.08f, 0.92f);
            return wy + t;
        }

        /// <summary>
        /// Dogal SurfaceNets (ShapeId == 0) arazi voksellerinin [wx, wy, wz] kose izgara yogunluk alanini
        /// surekli 3B Trilinear SDF olarak ornekler; analitik gradyani ve baskin kati MaterialID'yi doner (0 B GC).
        /// </summary>
        public static void SampleTrilinearSolidSdfPure(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float px,
            float py,
            float pz,
            out float rho,
            out float gradX,
            out float gradY,
            out float gradZ,
            out byte dominantMaterialId)
        {
            int x0 = (int)MathF.Floor(px);
            int y0 = (int)MathF.Floor(py);
            int z0 = (int)MathF.Floor(pz);

            float tx = Math.Clamp(px - x0, 0f, 1f);
            float ty = Math.Clamp(py - y0, 0f, 1f);
            float tz = Math.Clamp(pz - z0, 0f, 1f);

            dominantMaterialId = MaterialIdCatalog.Air;
            float bestCornerWeight = 0f;

            float SampleCorner(int dx, int dy, int dz, float w, ref byte bestMat, ref float bestW)
            {
                uint raw = ReadWorldVoxelRaw(worldChunks, x0 + dx, y0 + dy, z0 + dz);
                if (!IsHardCollisionVoxelPure(raw) || VoxelBitUtility.GetShapeId(raw) != 0)
                {
                    return 0f;
                }

                float d = VoxelBitUtility.GetDensityOrLiquid(raw);
                float weighted = d * w;
                if (weighted > bestW)
                {
                    bestW = weighted;
                    bestMat = VoxelBitUtility.GetMaterialId(raw);
                }
                return d;
            }

            float ox = 1f - tx, oy = 1f - ty, oz = 1f - tz;
            float c000 = SampleCorner(0, 0, 0, ox * oy * oz, ref dominantMaterialId, ref bestCornerWeight);
            float c100 = SampleCorner(1, 0, 0, tx * oy * oz, ref dominantMaterialId, ref bestCornerWeight);
            float c010 = SampleCorner(0, 1, 0, ox * ty * oz, ref dominantMaterialId, ref bestCornerWeight);
            float c110 = SampleCorner(1, 1, 0, tx * ty * oz, ref dominantMaterialId, ref bestCornerWeight);
            float c001 = SampleCorner(0, 0, 1, ox * oy * tz, ref dominantMaterialId, ref bestCornerWeight);
            float c101 = SampleCorner(1, 0, 1, tx * oy * tz, ref dominantMaterialId, ref bestCornerWeight);
            float c011 = SampleCorner(0, 1, 1, ox * ty * tz, ref dominantMaterialId, ref bestCornerWeight);
            float c111 = SampleCorner(1, 1, 1, tx * ty * tz, ref dominantMaterialId, ref bestCornerWeight);

            float c00 = c000 * ox + c100 * tx;
            float c10 = c010 * ox + c110 * tx;
            float c01 = c001 * ox + c101 * tx;
            float c11 = c011 * ox + c111 * tx;

            float c0 = c00 * oy + c10 * ty;
            float c1 = c01 * oy + c11 * ty;

            rho = c0 * oz + c1 * tz;
            gradX = ((c100 - c000) * oy + (c110 - c010) * ty) * oz +
                    ((c101 - c001) * oy + (c111 - c011) * ty) * tz;
            gradY = (c10 - c00) * oz + (c11 - c01) * tz;
            gradZ = c1 - c0;
        }

        /// <summary>
        /// Verilen dunya noktasinin (kafa/goz veya kamera) kati bir blok icinde olup olmadigini denetler.
        /// Ucgen yuzey (rho = 64 veya kubik yuzey) gecilmeden hemen once <paramref name="opaqueAlpha01"/> = 1.0f uretir;
        /// boylece blok icinde bosluk (X-Ray) gorulmesi %100 imkansizdir ve tam o blogun malzemesi gosterilir.
        /// </summary>
        public static bool EvaluateSolidBlockContainmentAtPoint(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float px,
            float py,
            float pz,
            float cubicSkinMeters,
            float sdfFullOpaqueRho,
            out byte blockMaterialId,
            out float opaqueAlpha01)
        {
            blockMaterialId = MaterialIdCatalog.Air;
            opaqueAlpha01 = 0f;
            if (worldChunks == null || worldChunks.Count == 0 || py < 0.5f || py >= 255f)
            {
                return false;
            }

            // 1) Kubik / sekilli mimari vokseller (ShapeId != 0 -> [wx .. wx+1] x [wy .. wy+1] x [wz .. wz+1])
            int baseWx = (int)MathF.Floor(px);
            int baseWy = (int)MathF.Floor(py);
            int baseWz = (int)MathF.Floor(pz);
            float skin = MathF.Max(0f, cubicSkinMeters);

            for (int dy = -1; dy <= 1; dy++)
            {
                int wy = baseWy + dy;
                if (wy < 1 || wy >= 255) continue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int wz = baseWz + dz;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int wx = baseWx + dx;
                        uint raw = ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                        if (!IsHardCollisionVoxelPure(raw) || VoxelBitUtility.GetShapeId(raw) == 0)
                        {
                            continue;
                        }

                        if (px >= wx - skin && px <= wx + 1f + skin &&
                            py >= wy - skin && py <= wy + 1f + skin &&
                            pz >= wz - skin && pz <= wz + 1f + skin)
                        {
                            blockMaterialId = VoxelBitUtility.GetMaterialId(raw);
                            opaqueAlpha01 = 1.0f;
                            return true;
                        }
                    }
                }
            }

            // 2) Dogal SurfaceNets (ShapeId == 0) surekli 3B Trilinear SDF alani (gorsel yuzey rho = 64.0f)
            SampleTrilinearSolidSdfPure(
                worldChunks, px, py, pz, out float rho, out _, out _, out _, out byte sdfMat);
            float startRho = sdfFullOpaqueRho - 3.5f;
            if (rho >= startRho && sdfMat != MaterialIdCatalog.Air)
            {
                blockMaterialId = sdfMat;
                opaqueAlpha01 = rho >= sdfFullOpaqueRho
                    ? 1.0f
                    : Math.Clamp((rho - startRho) / MathF.Max(0.5f, sdfFullOpaqueRho - startRho), 0f, 1f);
                return true;
            }

            // 3) Tam gomulu kati sutun emniyeti (merkez, alt ve ust vokselin ucu de kati ise %100 opak)
            uint centerRaw = ReadWorldVoxelRaw(worldChunks, baseWx, baseWy, baseWz);
            if (IsHardCollisionVoxelPure(centerRaw) &&
                IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, baseWx, baseWy + 1, baseWz)) &&
                IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, baseWx, Math.Max(1, baseWy - 1), baseWz)))
            {
                blockMaterialId = VoxelBitUtility.GetMaterialId(centerRaw);
                opaqueAlpha01 = 1.0f;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Minecraft tarzi kati blok icinde sikisma / gomulme hasari (-6 HP/sn) ve tam ekran opak malzeme tespiti.
        /// </summary>
        public static bool StepSolidBlockSuffocationHazard(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float headX,
            float headY,
            float headZ,
            float dt,
            ref CharacterVitalsComponent vitals,
            out byte blockMaterialId,
            out float overlayAlpha01,
            out float appliedDamageHp)
        {
            appliedDamageHp = 0f;
            bool contained = EvaluateSolidBlockContainmentAtPoint(
                worldChunks,
                headX,
                headY,
                headZ,
                cubicSkinMeters: 0.04f,
                sdfFullOpaqueRho: 53.0f,
                out blockMaterialId,
                out overlayAlpha01);

            if (!contained || overlayAlpha01 < 0.35f)
            {
                return false;
            }

            if (dt > 0f)
            {
                vitals.LungOxygenPercent = MathF.Max(0f, vitals.LungOxygenPercent - SolidBlockSuffocationOxygenDrainPerSecond * dt);
                float prevHp = vitals.CurrentHp;
                float loss = SolidBlockSuffocationDamagePerSecond * dt;
                vitals.CurrentHp = MathF.Max(0.0f, prevHp - loss);
                appliedDamageHp = MathF.Max(0f, prevHp - vitals.CurrentHp);
            }

            return true;
        }

        /// <summary>
        /// Iki kati fiziksel cismin ic ice girmesini onleyen gercek 3B yatay kapsul depenetrasyonu (MTV + SDF gradyani).
        /// ASLA Y koordinatini yukari firlatmaz (magara tavani isinlamasi %100 imkansizdir); yalnizca acik hava yonune yatay iter!
        /// </summary>
        public static bool ResolveHorizontalSolidCapsuleDepenetrationPure(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeightMeters = 1.8f,
            float capsuleRadiusMeters = 0.34f)
        {
            float r = Math.Clamp(capsuleRadiusMeters, 0.20f, 0.34f);
            bool movedAny = SotfStructureCollisionRegistry.ResolveHorizontalDepenetration(
                ref posX, posY, ref posZ, capsuleHeightMeters, r, 1.08f);
            if (worldChunks == null || worldChunks.Count == 0) return movedAny;

            float rSq = r * r;
            float waistY = posY + Math.Clamp(capsuleHeightMeters * 0.42f, 0.30f, 0.72f);
            float headY = posY + Math.Clamp(capsuleHeightMeters - 0.16f, 0.42f, 1.56f);

            ReadOnlySpan<float> probeYs = stackalloc float[2] { waistY, headY };

            for (int iter = 0; iter < 2; iter++)
            {
                for (int p = 0; p < 2; p++)
                {
                    float py = probeYs[p];
                    int wy = (int)MathF.Floor(py);
                    if (wy < 1 || wy >= 254) continue;

                    // A) Kubik (ShapeId != 0) duvar bloklarindan 2B daire-kutu MTV itmesi
                    int baseWx = (int)MathF.Floor(posX);
                    int baseWz = (int)MathF.Floor(posZ);
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int wz = baseWz + dz;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int wx = baseWx + dx;
                            uint raw = ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                            if (!IsHardCollisionVoxelPure(raw) || VoxelBitUtility.GetShapeId(raw) == 0)
                            {
                                continue;
                            }

                            bool isWall = IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, wx, wy + 1, wz)) ||
                                          ((wy + 1.0f) - posY) > 1.08f;
                            if (!isWall) continue;

                            float closestX = Math.Clamp(posX, (float)wx, wx + 1.0f);
                            float closestZ = Math.Clamp(posZ, (float)wz, wz + 1.0f);
                            float ox = posX - closestX;
                            float oz = posZ - closestZ;
                            float distSq = ox * ox + oz * oz;
                            if (distSq >= rSq) continue;

                            float candX = posX;
                            float candZ = posZ;
                            if (distSq > 1e-6f)
                            {
                                float dist = MathF.Sqrt(distSq);
                                float push = MathF.Min(0.22f, r - dist + 0.004f);
                                candX += (ox / dist) * push;
                                candZ += (oz / dist) * push;
                            }
                            else
                            {
                                // Merkez tam kubik blogun icindeyse 4 komsudan acik olana dogru cikar
                                float dWest = posX - wx;
                                float dEast = (wx + 1.0f) - posX;
                                float dSouth = posZ - wz;
                                float dNorth = (wz + 1.0f) - posZ;
                                bool westOpen = !IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, wx - 1, wy, wz));
                                bool eastOpen = !IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, wx + 1, wy, wz));
                                bool southOpen = !IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, wx, wy, wz - 1));
                                bool northOpen = !IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, wx, wy, wz + 1));

                                float bestDist = float.MaxValue;
                                if (westOpen && dWest < bestDist) { bestDist = dWest; candX = wx - r - 0.01f; candZ = posZ; }
                                if (eastOpen && dEast < bestDist) { bestDist = dEast; candX = wx + 1.0f + r + 0.01f; candZ = posZ; }
                                if (southOpen && dSouth < bestDist) { bestDist = dSouth; candX = posX; candZ = wz - r - 0.01f; }
                                if (northOpen && dNorth < bestDist) { bestDist = dNorth; candX = posX; candZ = wz + 1.0f + r + 0.01f; }
                                if (bestDist == float.MaxValue) continue;
                            }

                            int dstWx = (int)MathF.Floor(candX);
                            int dstWz = (int)MathF.Floor(candZ);
                            if (!IsHardCollisionVoxelPure(ReadWorldVoxelRaw(worldChunks, dstWx, wy, dstWz)))
                            {
                                posX = candX;
                                posZ = candZ;
                                movedAny = true;
                            }
                        }
                    }

                    // B) Dogal SurfaceNets (ShapeId == 0) falez / magara duvarlarindan surekli SDF gradyani ile itme:
                    //    Yurunebilir dogal yokuslari ASLA yana itmemek icin ust hizada (posY + 1.25m) duvar devamliligini dogrula!
                    SampleTrilinearSolidSdfPure(worldChunks, posX, posY + 1.25f, posZ, out float upperWallRho, out _, out _, out _, out _);
                    SampleTrilinearSolidSdfPure(worldChunks, posX, py, posZ, out float centerRho, out float gx, out float gy, out float gz, out _);

                    float horizGradSq = gx * gx + gz * gz;
                    bool isSteepWall = upperWallRho >= 46.0f || (horizGradSq > 1.0f && MathF.Sqrt(horizGradSq) > MathF.Abs(gy) * 0.90f);
                    if (isSteepWall && centerRho > 44.0f && horizGradSq > 1.0f)
                    {
                        float horizGrad = MathF.Sqrt(horizGradSq);
                        float pushMeters = Math.Clamp((centerRho - 42.0f) / horizGrad, 0.005f, 0.20f);
                        float candX = posX - (gx / horizGrad) * pushMeters;
                        float candZ = posZ - (gz / horizGrad) * pushMeters;

                        SampleTrilinearSolidSdfPure(worldChunks, candX, py, candZ, out float afterRho, out _, out _, out _, out _);
                        if (afterRho + 0.25f < centerRho)
                        {
                            posX = candX;
                            posZ = candZ;
                            movedAny = true;
                        }
                    }
                }
            }

            if (SotfStructureCollisionRegistry.ResolveHorizontalDepenetration(
                    ref posX, posY, ref posZ, capsuleHeightMeters, r, 1.08f))
            {
                movedAny = true;
            }

            return movedAny;
        }

        /// <summary>
        /// PT-05: Voksel + 3B Moduler Yapi parcalari icin birlesik saf C# yatay kapsul depenetrasyonu (0 B GC).
        /// </summary>
        public static bool ResolveHorizontalSolidCapsuleDepenetrationPure(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            ReadOnlySpan<ModularPlacedPieceNode> modularPieces,
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeightMeters = 1.8f,
            float capsuleRadiusMeters = 0.34f)
        {
            bool movedVoxel = ResolveHorizontalSolidCapsuleDepenetrationPure(
                worldChunks, ref posX, posY, ref posZ, capsuleHeightMeters, capsuleRadiusMeters);
            bool movedModular = modularPieces.Length > 0 &&
                ModularStructureCollisionSolver.ResolveHorizontalCapsuleDepenetration(
                    modularPieces, ref posX, posY, ref posZ, capsuleHeightMeters, capsuleRadiusMeters, 1.08f);
            return movedVoxel || movedModular;
        }

        /// <summary>
        /// PT-05: Voksel + 3B Moduler Yapi tabanlari icin birlesik kirilmaz kati taban kilidi (0 B GC).
        /// </summary>
        public static float ComputeUnbreakableSolidFloorStopY(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            ReadOnlySpan<ModularPlacedPieceNode> modularPieces,
            float posX,
            float posZ,
            float prevFeetY,
            float candidateFeetY,
            float footRadiusMeters = 0.20f)
        {
            float stopY = ComputeUnbreakableSolidFloorStopY(
                worldChunks, posX, posZ, prevFeetY, candidateFeetY, footRadiusMeters);
            if (modularPieces.Length > 0 && candidateFeetY <= prevFeetY + 0.001f)
            {
                float modTopY = ModularStructureCollisionSolver.SampleSupportGroundY(
                    modularPieces, posX, posZ, prevFeetY, maxStepUpMeters: 0.32f, footRadiusMeters: MathF.Max(0.20f, footRadiusMeters));
                if (modTopY > -20f && prevFeetY >= modTopY - 0.32f && modTopY > stopY)
                {
                    stopY = modTopY;
                }
            }
            return stopY;
        }

        /// <summary>
        /// Kirilmaz Kati Taban Kilidi: Oyuncu bir duvar kosesine sikissa veya basindan asagi kum/toprak dokulup
        /// tamamen kati blok icine gomulse dahi (SampleSingleColumnGroundY -30f dondugu anlarda bile!)
        /// yercekiminin oyuncuyu altindaki kati vokselin icinden asagi dusurmesini %100 imkansiz kilar.
        /// ASLA magara tavanina veya ada tepesine yukari isinlamaz!
        /// </summary>
        public static float ComputeUnbreakableSolidFloorStopY(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float posX,
            float posZ,
            float prevFeetY,
            float candidateFeetY,
            float footRadiusMeters = 0.20f)
        {
            if (candidateFeetY > prevFeetY + 0.001f)
            {
                return candidateFeetY;
            }

            float minAllowedY = candidateFeetY;
            float r = Math.Clamp(footRadiusMeters, 0.12f, 0.26f);
            float structStopY = SotfStructureCollisionRegistry.SampleWalkableTopY(posX, prevFeetY, posZ, r, 1.08f);
            if (structStopY > -900f && prevFeetY >= structStopY - 0.30f && structStopY > minAllowedY)
            {
                minAllowedY = structStopY;
            }

            if (worldChunks == null || worldChunks.Count == 0)
            {
                return minAllowedY;
            }
            ReadOnlySpan<float> ox = stackalloc float[5] { 0f, r, -r, 0f, 0f };
            ReadOnlySpan<float> oz = stackalloc float[5] { 0f, 0f, 0f, r, -r };

            int scanTopWy = Math.Clamp((int)MathF.Floor(prevFeetY + 0.05f), 1, 253);
            int scanBottomWy = Math.Clamp((int)MathF.Floor(candidateFeetY - 0.02f), 1, scanTopWy);

            // 1) Ustunde hava olan tum yurunebilir kati basamak/tabanlari 5 noktali ayak diskinde tara (duvar kosesi siyirmalarini yakalar)
            for (int i = 0; i < 5; i++)
            {
                int wx = (int)MathF.Floor(posX + ox[i]);
                int wz = (int)MathF.Floor(posZ + oz[i]);
                for (int wy = scanTopWy; wy >= scanBottomWy; wy--)
                {
                    uint raw = ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                    if (!IsHardCollisionVoxelPure(raw)) continue;

                    uint aboveRaw = ReadWorldVoxelRaw(worldChunks, wx, wy + 1, wz);
                    if (!IsHardCollisionVoxelPure(aboveRaw))
                    {
                        float topY = GetVoxelWalkableTopYPure(raw, aboveRaw, wy);
                        if (prevFeetY >= topY - 0.28f && topY > minAllowedY)
                        {
                            minAllowedY = topY;
                        }
                        break;
                    }
                }
            }

            // 2) Eger oyuncunun TAM MERKEZ omurgasinin altindaki hucre kati bloksa (orn. 3x3 toprak mezar / duvar ici sikisma),
            //    ustteki voksel de kati olsa bile kati maddenin icinden daha asagi dusmesi fiziksel olarak YASAKTIR!
            int centerWx = (int)MathF.Floor(posX);
            int centerWz = (int)MathF.Floor(posZ);
            int belowSoleWy = Math.Clamp((int)MathF.Floor(candidateFeetY - 0.02f), 1, 253);
            uint centerBelowRaw = ReadWorldVoxelRaw(worldChunks, centerWx, belowSoleWy, centerWz);
            if (IsHardCollisionVoxelPure(centerBelowRaw))
            {
                uint centerAboveRaw = ReadWorldVoxelRaw(worldChunks, centerWx, belowSoleWy + 1, centerWz);
                if (!IsHardCollisionVoxelPure(centerAboveRaw))
                {
                    float topY = GetVoxelWalkableTopYPure(centerBelowRaw, centerAboveRaw, belowSoleWy);
                    if (prevFeetY >= topY - 0.32f && topY > minAllowedY)
                    {
                        minAllowedY = topY;
                    }
                }
                else if (candidateFeetY < prevFeetY)
                {
                    // Gomulu kati sutun: Yukari firlatma YOK (magara guvenli!), asagi dusme YOK (prevFeetY korunur)!
                    if (prevFeetY > minAllowedY)
                    {
                        minAllowedY = prevFeetY;
                    }
                }
            }

            // 3) Saglam (!IsBroken) 3B agac dallari uzerine iniste veya yururken dalin icinden asagi dusmeyi onle:
            OrganicTreeCanopyVisualSystem? treeSys = OrganicTreeCanopyVisualSystem.ActiveInstance;
            if (treeSys != null &&
                treeSys.TrySampleWalkableBranchSurfaceAt(
                    posX,
                    posZ,
                    prevFeetY,
                    r,
                    maxStepUpMeters: 0.45f,
                    out float branchTopY,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                if (prevFeetY >= branchTopY - 0.32f && branchTopY > minAllowedY)
                {
                    minAllowedY = branchTopY;
                }
            }

            return minAllowedY;
        }

        /// <summary>
        /// Saf C# (.NET 10 ve Unity 6) zemin + 3B agac dali yurunebilir yuzey yuksekligi ornekleyicisi (0 B GC).
        /// Hem voksel araziyi hem de <see cref="OrganicTreeCanopyVisualSystem"/> icindeki saglam (!IsBroken)
        /// kalin/ince agac dallarini tarayarak oyuncunun basabilecegi en yuksek yuzeyi doner.
        /// </summary>
        public static float SampleWalkableGroundOrBranchHeightPure(
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            OrganicTreeCanopyVisualSystem? treeSystem,
            float worldX,
            float worldZ,
            float refFeetY,
            float footRadiusMeters,
            float maxStepUpMeters,
            out bool isOnTreeBranch,
            out int supportingTreeInstanceId,
            out int supportingBranchId,
            out float branchNormalizedT01,
            out OrganicTreeBranchTier supportingBranchTier)
        {
            isOnTreeBranch = false;
            supportingTreeInstanceId = 0;
            supportingBranchId = 0;
            branchNormalizedT01 = 0f;
            supportingBranchTier = OrganicTreeBranchTier.ThickPrimaryCarrier;

            float terrainTopY = -30f;
            if (worldChunks != null && worldChunks.Count > 0)
            {
                int wx = (int)MathF.Floor(worldX);
                int wz = (int)MathF.Floor(worldZ);
                int scanTopWy = Math.Clamp((int)MathF.Floor(refFeetY + Math.Max(0.45f, maxStepUpMeters)), 1, 253);
                for (int wy = scanTopWy; wy >= 1; wy--)
                {
                    uint raw = ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
                    if (!IsHardCollisionVoxelPure(raw)) continue;
                    uint aboveRaw = ReadWorldVoxelRaw(worldChunks, wx, wy + 1, wz);
                    if (IsHardCollisionVoxelPure(aboveRaw)) continue;
                    terrainTopY = GetVoxelWalkableTopYPure(raw, aboveRaw, wy);
                    break;
                }
            }

            OrganicTreeCanopyVisualSystem? activeTrees = treeSystem ?? OrganicTreeCanopyVisualSystem.ActiveInstance;
            if (activeTrees != null &&
                activeTrees.TrySampleWalkableBranchSurfaceAt(
                    worldX,
                    worldZ,
                    refFeetY,
                    footRadiusMeters,
                    maxStepUpMeters,
                    out float branchTopY,
                    out int hitTreeId,
                    out int hitBranchId,
                    out float hitNormT,
                    out OrganicTreeBranchTier hitTier))
            {
                if (branchTopY >= terrainTopY - 0.02f)
                {
                    isOnTreeBranch = true;
                    supportingTreeInstanceId = hitTreeId;
                    supportingBranchId = hitBranchId;
                    branchNormalizedT01 = hitNormT;
                    supportingBranchTier = hitTier;
                    return branchTopY;
                }
            }

            return terrainTopY;
        }

        /// <summary>
        /// Saf C# (.NET 10 ve Unity 6) oyuncu-dal etkilesim adimi:
        /// Oyuncu bir agac dalinin uzerine bastiginda oyuncunun toplam kutlesini (totalPlayerMassKg) ve inis darbesini
        /// o dala uygular; dal kalinligina/agirliga gore esner (deflection) ve oyuncu dal ile birlikte alcalir.
        /// Eger ince dalda veya asiri yukte kirilma esigi asilirsa dal fiziksel olarak kirilir, odun parcasi duser
        /// (<paramref name="brokenBranchDrop"/>) ve oyuncu destegini kaybederek asagi duser!
        /// </summary>
        public static bool StepPlayerOnTreeBranchPhysicsPure(
            Dictionary<(int cx, int cz), uint[]>? worldChunks,
            OrganicTreeCanopyVisualSystem treeSystem,
            float playerX,
            ref float playerFeetY,
            float playerZ,
            ref float verticalVelocity,
            ref byte isGrounded,
            float totalPlayerMassKg,
            float dt,
            out int activeBranchId,
            out float branchDeflectionMeters,
            out bool branchBrokeThisStep,
            out BrokenTreeBranchDropEvent brokenBranchDrop,
            float footRadiusMeters = 0.22f)
        {
            activeBranchId = 0;
            branchDeflectionMeters = 0f;
            branchBrokeThisStep = false;
            brokenBranchDrop = default;

            if (treeSystem == null || dt <= 0f)
            {
                return false;
            }

            float supportY = SampleWalkableGroundOrBranchHeightPure(
                worldChunks,
                treeSystem,
                playerX,
                playerZ,
                playerFeetY,
                footRadiusMeters,
                maxStepUpMeters: 1.08f,
                out bool onBranch,
                out _,
                out int hitBranchId,
                out float hitNormT01,
                out _);

            float snapTol = isGrounded != 0 ? 0.65f : 0.28f;
            bool touchingSupport = supportY > -20f && playerFeetY <= supportY + snapTol && verticalVelocity <= 0.25f;

            if (onBranch && touchingSupport && hitBranchId > 0)
            {
                float impactSpeed = MathF.Max(0f, -verticalVelocity);
                treeSystem.ApplyLoadAndStepBranchPhysics(
                    hitBranchId,
                    MathF.Max(0f, totalPlayerMassKg),
                    hitNormT01,
                    dt,
                    out float tipDeflection,
                    out float deflectedSurfaceY,
                    out bool broke,
                    out BrokenTreeBranchDropEvent dropEvt,
                    impactSpeed);

                activeBranchId = hitBranchId;
                branchDeflectionMeters = tipDeflection;

                if (broke)
                {
                    branchBrokeThisStep = true;
                    brokenBranchDrop = dropEvt;
                    isGrounded = 0;
                    verticalVelocity = MathF.Min(verticalVelocity, -1.35f) - StratocraftConstants.GravityAcceleration * dt;
                    playerFeetY += verticalVelocity * dt;

                    // Altta daha alcak bir dal veya arazi zemini varsa onun altina gecme:
                    float lowerFloorY = SampleWalkableGroundOrBranchHeightPure(
                        worldChunks, treeSystem, playerX, playerZ, playerFeetY, footRadiusMeters, 0.45f,
                        out _, out _, out _, out _, out _);
                    if (lowerFloorY > -20f && playerFeetY <= lowerFloorY)
                    {
                        playerFeetY = lowerFloorY;
                        verticalVelocity = 0f;
                        isGrounded = 1;
                    }
                    return true;
                }

                // Dal kirilmadi, elastik olarak esnedi: Oyuncu ayagi bukulmus dal yuzeyini takip eder!
                if (deflectedSurfaceY > -20f)
                {
                    playerFeetY = deflectedSurfaceY;
                }
                else
                {
                    playerFeetY = supportY;
                }
                verticalVelocity = 0f;
                isGrounded = 1;
                return true;
            }

            // Oyuncu dal ustunde degil: Yuku kalkmis dallarin elastik geri yaylanmasini ilerlet ve normal yercekimi uygula
            treeSystem.StepAllTreeBranchesPhysics(dt);
            if (touchingSupport && supportY > -20f)
            {
                playerFeetY = supportY;
                verticalVelocity = 0f;
                isGrounded = 1;
            }
            else
            {
                isGrounded = 0;
                verticalVelocity -= StratocraftConstants.GravityAcceleration * dt;
                playerFeetY += verticalVelocity * dt;
                if (supportY > -20f && playerFeetY <= supportY)
                {
                    playerFeetY = supportY;
                    verticalVelocity = 0f;
                    isGrounded = 1;
                }
            }

            return false;
        }

        /// <summary>Dunya (wx, wy, wz) koordinatindaki 32-bit vokseli okur.</summary>
        public static uint ReadWorldVoxelRaw(Dictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wy, int wz)
        {
            if (worldChunks == null || wy < 0 || wy >= 256) return 0u;
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? v) || v == null) return 0u;
            int idx = (wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16);
            return (uint)idx < (uint)v.Length ? v[idx] : 0u;
        }

        /// <summary>
        /// Oyuncunun bastigi taban vokseli (feetWy - 1) veya ayak/bel kapsulu kati <see cref="MaterialIdCatalog.MagmaBlock"/> (11)
        /// ile temas halinde mi kontrol eder (.NET 10 ve Unity 6 uyumlu, 0 B GC).
        /// </summary>
        public static bool IsTouchingSolidMagmaBlock(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float playerX,
            float playerFeetY,
            float playerZ)
        {
            if (worldChunks == null || worldChunks.Count == 0)
            {
                return false;
            }

            int wx = (int)MathF.Floor(playerX);
            int wz = (int)MathF.Floor(playerZ);
            int feetWy = Math.Clamp((int)MathF.Floor(playerFeetY + 0.25f), 1, 254);
            int waistWy = Math.Clamp((int)MathF.Floor(playerFeetY + 0.85f), 1, 254);
            int soleWy = feetWy - 1;

            if (IsSolidMagmaVoxelAt(worldChunks, wx, feetWy, wz) ||
                IsSolidMagmaVoxelAt(worldChunks, wx, waistWy, wz))
            {
                return true;
            }

            if (soleWy >= 0 &&
                playerFeetY <= soleWy + 1.18f &&
                IsSolidMagmaVoxelAt(worldChunks, wx, soleWy, wz))
            {
                return true;
            }

            const float capsuleProbeRadius = 0.34f;
            ReadOnlySpan<float> ox = stackalloc float[4] { capsuleProbeRadius, -capsuleProbeRadius, 0f, 0f };
            ReadOnlySpan<float> oz = stackalloc float[4] { 0f, 0f, capsuleProbeRadius, -capsuleProbeRadius };

            for (int i = 0; i < 4; i++)
            {
                int px = (int)MathF.Floor(playerX + ox[i]);
                int pz = (int)MathF.Floor(playerZ + oz[i]);
                if (IsSolidMagmaVoxelAt(worldChunks, px, feetWy, pz) ||
                    IsSolidMagmaVoxelAt(worldChunks, px, waistWy, pz))
                {
                    return true;
                }

                if (soleWy >= 0 &&
                    playerFeetY <= soleWy + 1.12f &&
                    IsSolidMagmaVoxelAt(worldChunks, px, soleWy, pz))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Kati <see cref="MaterialIdCatalog.MagmaBlock"/> (11) temas yanigini (-9.0 HP/s ve +2.8 C/s cekirdek isi artisi)
        /// saf C# olarak <paramref name="vitals"/> uzerine uygular.
        /// </summary>
        public static bool StepSolidMagmaBlockContactHazard(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float playerX,
            float playerFeetY,
            float playerZ,
            float dt,
            bool environmentalEffectsEnabled,
            ref CharacterVitalsComponent vitals,
            out float appliedDamageHp,
            out float appliedCoreHeatDeltaCelsius)
        {
            appliedDamageHp = 0f;
            appliedCoreHeatDeltaCelsius = 0f;

            if (dt <= 0f || !IsTouchingSolidMagmaBlock(worldChunks, playerX, playerFeetY, playerZ))
            {
                return false;
            }

            if (environmentalEffectsEnabled)
            {
                float hpLoss = MagmaBlockContactDamagePerSecond * dt;
                float heatGain = MagmaBlockCoreHeatGainCelsiusPerSecond * dt;
                float prevHp = vitals.CurrentHp;
                float prevTemp = vitals.CoreBodyTempCelsius;

                vitals.CurrentHp = MathF.Max(0.0f, prevHp - hpLoss);
                vitals.CoreBodyTempCelsius = MathF.Min(43.5f, prevTemp + heatGain);
                vitals.Wetness01 = MathF.Max(0f, vitals.Wetness01 - 0.35f * dt);

                if (vitals.CoreBodyTempCelsius > ThermalAndBiologySystem.HyperthermiaThresholdCelsius)
                {
                    vitals.ActiveDiseaseFlags |= (byte)BiologicalAfflictionFlags.Hyperthermia;
                }

                appliedDamageHp = MathF.Max(0f, prevHp - vitals.CurrentHp);
                appliedCoreHeatDeltaCelsius = MathF.Max(0f, vitals.CoreBodyTempCelsius - prevTemp);
            }

            return true;
        }

        private static bool IsSolidMagmaVoxelAt(Dictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wy, int wz)
        {
            uint raw = ReadWorldVoxelRaw(worldChunks, wx, wy, wz);
            return VoxelBitUtility.GetMaterialId(raw) == MaterialIdCatalog.MagmaBlock &&
                   VoxelBitUtility.IsPhysicallySolid(raw);
        }

#if UNITY_5_3_OR_NEWER
        private const float VoidRescueThresholdY = -14.0f;
        private const float MaxStepUpHeightMeters = 0.65f;
        private const float BaseBodyMassKg = 75.0f;
        private const float DodgeStaminaCostEp = 18.0f;
        private const float DodgeDurationSeconds = 0.26f;
        private const float DodgeSpeedMetersPerSec = 6.8f;
        private const float DodgeCooldownSeconds = 0.65f;
        private const float ExhaustedWalkSpeedCapMps = 1.45f;

        private readonly PhysicalCharacterControllerSystem _controllerSystem = new();
        private CharacterMovementState _movementState = CharacterMovementState.CreateDefault();
        private CharacterVitalsComponent _vitals = CharacterVitalsComponent.CreateDefault();
        private float _verticalVelocity, _coyoteGroundedTimer = 0.15f, _jumpLaunchTimer;
        private Vector3 _safeSpawnPosition = new Vector3(-1.4f, 11.5f, -5.2f);
        private bool _isMantling;
        private float _mantleElapsedSeconds;
        private System.Numerics.Vector3 _mantleStartPos;
        private System.Numerics.Vector3 _mantleTargetPos;
        private float _dodgeRemainingSec;
        private float _dodgeCooldownSec;
        private Vector3 _dodgeDirectionWorld;
        private float _lowCeilingDebounceSec;
        private bool _wasTouchingWaterLastFrame;
        private bool _wasHeadSubmergedLastFrame;
        private float _swimmerSurgeCooldownSec;
        private float _pendingMagmaBurnHp;
        private float _pendingMagmaCoreHeatCelsius;

        /// <summary>Son karede hesaplanan nefes alma (agiz/burun ustu) hava voksel Y kotu.</summary>
        public int LastBreathAirVoxelY { get; private set; } = -1;

        /// <summary>Oyuncu su anda kati kor MagmaBlock (11) uzerine basiyor veya govdesiyle temas ediyor mu?</summary>
        public bool IsTouchingHotMagmaBlock { get; private set; }

        /// <summary>Oyuncunun kesintisiz kati MagmaBlock (11) temasinda kaldigi sure (sn; temas yoksa 0).</summary>
        public float LastMagmaContactSeconds { get; private set; }

        /// <summary>Oyuncunun kafasi su anda kati bir blok icinde sikismis / gomulmus durumda mi?</summary>
        public bool IsHeadSuffocatingInSolidBlock { get; private set; }

        /// <summary>Kafanin veya kameranin icinde bulundugu kati blogun MaterialID degeri (Toprak, Tas, Kum vb.).</summary>
        public byte SuffocatingBlockMaterialId { get; private set; }

        /// <summary>Minecraft tarzi tam ekran kati blok kaplama opakligi (0..1; 1.0 = %100 opak, sifir X-Ray!).</summary>
        public float SuffocationOverlayAlpha01 { get; private set; }

        /// <summary>Son fizik adiminda blok icinde sikisma nedeniyle kesilen can miktari (HP).</summary>
        public float LastSuffocationDamageHp { get; private set; }

        /// <summary>
        /// Kamera konumu (StepRigAnimation sonrasi) hala kati bir yuzeyin/blogun icinde sikismissa
        /// ucgen yuzey (X-Ray) gorulmeden once tam ekran opak blok dokusunu zorlar.
        /// </summary>
        public void EvaluateCameraSolidBlockShield(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 cameraWorldPos)
        {
            if (EvaluateSolidBlockContainmentAtPoint(
                    worldChunks,
                    cameraWorldPos.x,
                    cameraWorldPos.y,
                    cameraWorldPos.z,
                    cubicSkinMeters: 0.055f,
                    sdfFullOpaqueRho: 50.0f,
                    out byte camMat,
                    out float camAlpha) &&
                camAlpha > SuffocationOverlayAlpha01)
            {
                SuffocationOverlayAlpha01 = camAlpha;
                if (camMat != MaterialIdCatalog.Air)
                {
                    SuffocatingBlockMaterialId = camMat;
                }
            }
        }

        /// <summary>Biriken kati MagmaBlock yanik telemetrisini okur ve sifirlar.</summary>
        public bool ConsumePendingMagmaBurn(out float burnDamageHp, out float coreHeatDeltaCelsius)
        {
            burnDamageHp = _pendingMagmaBurnHp;
            coreHeatDeltaCelsius = _pendingMagmaCoreHeatCelsius;
            _pendingMagmaBurnHp = 0f;
            _pendingMagmaCoreHeatCelsius = 0f;
            return burnDamageHp > 0f || coreHeatDeltaCelsius > 0f;
        }

        /// <summary>Oyuncu-su etkilesimi dunya konumu (_StratocraftSwimmerPos.xyz).</summary>
        public Vector3 WaterInteractionWorldPos { get; private set; }
        /// <summary>Oyuncu-su dalga etkilesim siddeti 0..1 (_StratocraftSwimmerPos.w).</summary>
        public float WaterInteractionStrength01 { get; private set; }
        /// <summary>Suya atlama / hizli giris sicrama darbesi 0..1 (_StratocraftSwimmerVel.w).</summary>
        public float WaterSplashPulse01 { get; private set; }

        /// <summary>Anlik fiziksel karakter hareket durumu (Stance, Hiz, Kapsul Boyutu, Stamina Kilidi).</summary>
        public ref CharacterMovementState MovementState => ref _movementState;
        /// <summary>Anlik biyolojik hayatta kalma gostergeleri (HP, Stamina, Oksijen, Sicaklik).</summary>
        public ref CharacterVitalsComponent Vitals => ref _vitals;
        /// <summary>Dusey yercekimi / ziplama hizi (m/s).</summary>
        public float VerticalVelocity { get => _verticalVelocity; set => _verticalVelocity = value; }
        /// <summary>0.55s Kubik Bezier Mantle tirmanisi aktif mi?</summary>
        public bool IsMantling => _isMantling;
        /// <summary>Fiziksel yan-adim / Dodge kacisi aktif mi?</summary>
        public bool IsDodging => _dodgeRemainingSec > 0f;
        /// <summary>[F4] Yaratici Modda cift Space ile acilan Minecraft tarzi ucus modu aktif mi?</summary>
        public bool IsCreativeFlying { get; set; }

        private float _creativeFlightClockSec;
        private float _lastCreativeSpaceTapTime = -10f;
        private float _creativeFlyTakeoffGraceSec;
        /// <summary>PT-05: 3B Moduler yapilarla kati duvar/zemin/cati carpismasi icin bagli WorldLooseBuildSystem referansi.</summary>
        public WorldLooseBuildSystem? BoundLooseBuildSystem { get; set; }

        private ReadOnlySpan<ModularPlacedPieceNode> GetActiveModularPiecesSpan() =>
            BoundLooseBuildSystem != null ? BoundLooseBuildSystem.ActiveModularPiecesSpan : ReadOnlySpan<ModularPlacedPieceNode>.Empty;

        /// <summary>Bosluga dusme durumunda geri isinlanilacak guvenli ada koordinatini ayarlar.</summary>
        public void SetSafeSpawnPosition(Vector3 safeWorldPosition) => _safeSpawnPosition = safeWorldPosition;
        /// <summary>Unity Vector3 tabanli kapsul-blok kesisim kontrolu.</summary>
        public static bool CanPlaceBlockOutsidePlayerCapsule(int wx, int wy, int wz, Vector3 playerPos, float capsuleHeight, float brushRadius = 0.5f)
            => CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerPos.x, playerPos.y, playerPos.z, capsuleHeight, brushRadius);
        /// <summary>Ornek bazli kapsul-blok kesisim kontrolu.</summary>
        public bool CanPlaceBlockOutsidePlayerCapsule(Transform playerTransform, int wx, int wy, int wz, float brushRadius = 0.5f)
            => CanPlaceBlockOutsidePlayerCapsule(wx, wy, wz, playerTransform.position, _movementState.CapsuleHeightMeters, brushRadius);

        /// <summary>Dunya (wx, wy, wz) koordinatina 32-bit voksel yazar ve komsu chunk sinirlarini kirli isaretler.</summary>
        public static void WriteWorldVoxelRaw(
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks, int wx, int wy, int wz, uint rawValue)
        {
            if (wy < 1 || wy >= 255) return;
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels)) return;
            int lx = wx - cx * 16, lz = wz - cz * 16;
            voxels[(wy << 8) | (lz << 4) | lx] = rawValue;
            dirtyChunks.Add((cx, cz));
            if (lx <= 1 && worldChunks.ContainsKey((cx - 1, cz))) dirtyChunks.Add((cx - 1, cz));
            if (lx >= 14 && worldChunks.ContainsKey((cx + 1, cz))) dirtyChunks.Add((cx + 1, cz));
            if (lz <= 1 && worldChunks.ContainsKey((cx, cz - 1))) dirtyChunks.Add((cx, cz - 1));
            if (lz >= 14 && worldChunks.ContainsKey((cx, cz + 1))) dirtyChunks.Add((cx, cz + 1));
            Stage1InteractivePlayController.NotifyWorldVoxelEdited(cx, cz, wy, lx, lz, includeNeighbors: true);
        }

        /// <summary>WASD, kosu, kayma, surunme, 2.2m mantle, ziplama, sivi kaldirma/lav hasari ve kapsul-arazi depenetration cozumunu ilerletir.</summary>
        public void StepCharacterPhysics(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, Vector2 moveInput,
            bool wantSprint, bool wantCrouch, bool wantProne, bool jumpOrMantlePressed, bool isBackpackOpen,
            float equippedMassKg, float deltaTime, out string statusFeedback,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks = null,
            SoilMoistureAndMudSystem? soilMoistureSystem = null,
            HashSet<(int cx, int cz)>? dirtyFluidChunks = null,
            HashSet<(int cx, int cz)>? activeFluidChunks = null,
            bool isCreativeMode = false,
            bool ascendFlyHeld = false,
            bool descendFlyHeld = false,
            bool suppressAltDodge = false)
        {
            statusFeedback = string.Empty;
            _movementState.EquippedMassKg = equippedMassKg;
            _movementState.IsBackpackOverlayOpen = isBackpackOpen ? (byte)1 : (byte)0;
            _creativeFlightClockSec += deltaTime;
            if (_creativeFlyTakeoffGraceSec > 0f)
            {
                _creativeFlyTakeoffGraceSec = Mathf.Max(0f, _creativeFlyTakeoffGraceSec - deltaTime);
            }

            if (!isCreativeMode)
            {
                if (IsCreativeFlying)
                {
                    IsCreativeFlying = false;
                    _verticalVelocity = 0f;
                }
                _lastCreativeSpaceTapTime = -10f;
            }
            else
            {
                // [F4] Yaratici Mod: Sinirsiz Can + Sinirsiz Stamina + Sinirsiz Nefes!
                _vitals.CurrentHp = _vitals.MaxHp;
                _vitals.Stamina = _vitals.MaxStamina;
                _vitals.LungOxygenPercent = 100f;
                _movementState.IsExhaustedStaminaLock = 0;

                if (jumpOrMantlePressed && !isBackpackOpen)
                {
                    if ((_creativeFlightClockSec - _lastCreativeSpaceTapTime) <= 0.38f)
                    {
                        IsCreativeFlying = !IsCreativeFlying;
                        _lastCreativeSpaceTapTime = -10f;
                        _isMantling = false;
                        _jumpLaunchTimer = 0f;
                        jumpOrMantlePressed = false;

                        if (IsCreativeFlying)
                        {
                            _verticalVelocity = 0f;
                            _creativeFlyTakeoffGraceSec = 0.25f;
                            Vector3 pLift = playerTransform.position;
                            if (_movementState.IsGrounded != 0)
                            {
                                pLift.y += 0.14f;
                                playerTransform.position = pLift;
                            }
                            _movementState.IsGrounded = 0;
                            statusFeedback = "🕊️ [YARATICI UÇUŞ AÇIK]: [Space] Yüksel | [Ctrl] Alçal | [Shift] Hızlı Uç | Çift [Space] Bırak & Düş";
                        }
                        else
                        {
                            // Cift Space ile ucusu kapatinca yercekimiyle hemen dusmeye basla (Minecraft gibi)
                            _verticalVelocity = 0f;
                            _movementState.IsGrounded = 0;
                            _coyoteGroundedTimer = 0f;
                            statusFeedback = "🦶 [YARATICI UÇUŞ KAPANDI]: Serbest düşüş / normal yürüyüş aktif.";
                        }
                    }
                    else
                    {
                        _lastCreativeSpaceTapTime = _creativeFlightClockSec;
                    }
                }
            }

            // Faz A.4 & A.5: Hayatta Kalma modunda gercek 0 HP -> BleedOutCrawl ve aktif aclik/susuzluk!
            // Yalnizca [F4] Yaratici Modda tum vitaller %100 kilitli kalir.
            if (isCreativeMode)
            {
                _vitals.CurrentHp = _vitals.MaxHp;
                _vitals.Stamina = _vitals.MaxStamina;
                _vitals.HungerKcal = 2500f;
                _vitals.ThirstHydration = 100f;
                _vitals.BleedOutTimerSeconds = 30.0f;
                if (_movementState.Stance == CharacterStance.BleedOutCrawl)
                {
                    _movementState.Stance = CharacterStance.Standing;
                }
            }
            else if (_vitals.CurrentHp <= 0.0f)
            {
                _vitals.CurrentHp = 0.0f;
                _movementState.Stance = CharacterStance.BleedOutCrawl;
            }
            else if (_movementState.Stance == CharacterStance.BleedOutCrawl && _vitals.CurrentHp > 0.0f)
            {
                _movementState.Stance = CharacterStance.Standing;
            }

            Vector3 pos = playerTransform.position;
            if (pos.y < VoidRescueThresholdY)
            {
                RescueFromVoid(playerTransform, worldChunks);
                statusFeedback = "Guvenlik Kurtarmasi: Oyuncu ada yuzeyine geri tasindi";
                return;
            }
            if (_jumpLaunchTimer > 0f) _jumpLaunchTimer = Mathf.Max(0f, _jumpLaunchTimer - deltaTime);
            if (_dodgeCooldownSec > 0f) _dodgeCooldownSec = Mathf.Max(0f, _dodgeCooldownSec - deltaTime);

            if (isCreativeMode && IsCreativeFlying)
            {
                StepCreativeFlightPhysics(
                    playerTransform,
                    worldChunks,
                    isBackpackOpen ? Vector2.zero : moveInput,
                    wantSprint,
                    !isBackpackOpen && ascendFlyHeld,
                    !isBackpackOpen && descendFlyHeld,
                    deltaTime,
                    ref statusFeedback);
                EvaluatePlayerHeadSuffocationInSolidBlock(playerTransform, worldChunks, deltaTime, ref statusFeedback, isCreativeMode: true);
                _vitals.CurrentHp = _vitals.MaxHp;
                _vitals.Stamina = _vitals.MaxStamina;
                _vitals.LungOxygenPercent = 100f;
                _movementState.IsExhaustedStaminaLock = 0;
                return;
            }

            // Aktif 0.55sn Kubik Bezier Mantle tirmanisi devam ediyorsa yorunge boyunca puruzsuz ilerlet
            if (_isMantling)
            {
                _mantleElapsedSeconds += deltaTime;
                float progress01 = Mathf.Clamp01(_mantleElapsedSeconds / PhysicalCharacterControllerSystem.MantleDurationSeconds);
                _movementState.MantleProgress01 = progress01;
                _movementState.Stance = CharacterStance.Climbing;
                _movementState.CapsuleHeightMeters = PhysicalCharacterControllerSystem.GetStanceCapsuleHeight(CharacterStance.Climbing);
                _movementState.CapsuleRadiusMeters = PhysicalCharacterControllerSystem.GetStanceCapsuleRadius(CharacterStance.Climbing);
                System.Numerics.Vector3 bPos = PhysicalCharacterControllerSystem.EvaluateCubicBezierMantlePosition(
                    _mantleStartPos, _mantleTargetPos, progress01);
                playerTransform.position = new Vector3(bPos.X, bPos.Y, bPos.Z);
                _verticalVelocity = 0f;

                if (progress01 >= 1.0f)
                {
                    _isMantling = false;
                    _movementState.MantleProgress01 = 0f;
                    _movementState.Stance = CharacterStance.Standing;
                    _movementState.IsGrounded = 1;
                    _coyoteGroundedTimer = 0.16f;
                }
                return;
            }

            // Kılıç savurma momentumu veya önceki karedeki eğim geçişinden kalan gömülmeyi tavan ölçümünden ÖNCE çöz
            ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
            pos = playerTransform.position;

            float groundY = SampleSupportGroundHeightWithModular(worldChunks, pos);
            float groundSnapTolerance = _movementState.IsGrounded != 0 ? 0.65f : 0.24f;
            bool grounded = _jumpLaunchTimer <= 0.001f && pos.y <= groundY + groundSnapTolerance && _verticalVelocity <= 0.25f;
            _coyoteGroundedTimer = grounded ? 0.16f : Mathf.Max(0f, _coyoteGroundedTimer - deltaTime);
            _movementState.IsGrounded = grounded ? (byte)1 : (byte)0;

            bool hasLedgeAhead = DetectClimbableLedgeAhead(playerTransform, worldChunks, moveInput, out float ledgeTopY);
            Vector3 forwardFlat = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            Vector3 rightFlat = Vector3.ProjectOnPlane(playerTransform.right, Vector3.up).normalized;
            Vector3 wishDir = forwardFlat * moveInput.y + rightFlat * moveInput.x;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

            bool prevSliding = _movementState.Stance == CharacterStance.Sliding;
            if (!prevSliding && wantSprint && wantCrouch && wishDir.sqrMagnitude > 0.04f)
                PhysicalCharacterControllerSystem.TryInitiateSlide(ref _movementState, ref _vitals, (wishDir.x, wishDir.z));

            // Gercek voksel + 3B moduler tavan yuksekligi (Headroom Clearance) kontrolu: egimli zemini tavan sanmamak icin acik hava boslugunu olc
            MeasureHeadroomClearanceWithModular(worldChunks, pos, groundY, out bool rawCanCrouch115, out bool rawCanStand180);
            bool alreadyLowStance = _movementState.Stance == CharacterStance.Crouching || _movementState.Stance == CharacterStance.ProneCrawling;
            if (!rawCanCrouch115 || !rawCanStand180)
            {
                _lowCeilingDebounceSec = Mathf.Min(0.25f, _lowCeilingDebounceSec + deltaTime);
            }
            else
            {
                _lowCeilingDebounceSec = 0f;
            }

            bool enforceCeilingLock = alreadyLowStance || _lowCeilingDebounceSec >= 0.08f;
            bool canCrouch115 = !enforceCeilingLock || rawCanCrouch115;
            bool canStand180 = !enforceCeilingLock || rawCanStand180;

            CharacterStance reqStance = _movementState.Stance == CharacterStance.Sliding
                ? CharacterStance.Sliding
                : (wantProne ? CharacterStance.ProneCrawling : (wantCrouch ? CharacterStance.Crouching : (wantSprint ? CharacterStance.Sprinting : CharacterStance.Standing)));

            int headroomBlocks;
            if (!canCrouch115)
            {
                // 1x1m (0.60m) alcak tunel icinde ayaga kalkmak veya comelmek imkansizdir!
                if (!wantProne && _movementState.Stance != CharacterStance.Sliding)
                {
                    statusFeedback = "Tavan Alcak (1x1m Tunel Kilidi): Ayaga kalkmak veya comelmek icin bas ustu bosluk yok!";
                }
                reqStance = CharacterStance.ProneCrawling;
                headroomBlocks = 1;
            }
            else if (!canStand180)
            {
                // ~1.2m - 1.7m arasi boslukta comelinebilir (1.15m) ancak tam ayaga kalkilamaz (1.80m)!
                if (!wantProne && !wantCrouch && _movementState.Stance != CharacterStance.Sliding)
                {
                    statusFeedback = "Tavan Alcak (1.2m Bosluk): Ayaga kalkilamaz, Comelme (Crouch) durusunda kalindi.";
                }
                if (reqStance == CharacterStance.Standing || reqStance == CharacterStance.Sprinting)
                {
                    reqStance = CharacterStance.Crouching;
                }
                headroomBlocks = 2;
            }
            else
            {
                headroomBlocks = 3;
            }

            float staminaBeforeMove = _vitals.Stamina;
            _controllerSystem.StepMovement(ref _movementState, ref _vitals, reqStance, (wishDir.x, wishDir.z), headroomBlocks, deltaTime);
            float staminaDrainMult = Stage1GameSettings.StaminaDrainMultiplier;
            if (staminaDrainMult <= 0.01f)
            {
                _vitals.Stamina = _vitals.MaxStamina;
                _movementState.IsExhaustedStaminaLock = 0;
            }
            else if (_vitals.Stamina < staminaBeforeMove && Mathf.Abs(staminaDrainMult - 1.0f) > 0.01f)
            {
                float lost = staminaBeforeMove - _vitals.Stamina;
                _vitals.Stamina = Mathf.Clamp(staminaBeforeMove - lost * staminaDrainMult, 0f, _vitals.MaxStamina);
                if (_vitals.Stamina > 0.05f && staminaBeforeMove > 0.05f) _movementState.IsExhaustedStaminaLock = 0;
            }
            if (!prevSliding && _movementState.Stance == CharacterStance.Sliding)
                statusFeedback = "Kayma (Slide) Basladi (-15 EP -> 3.58m)";

            // Fiziksel Yan-Adim / Dodge Kacisi (LeftAlt veya Mouse3/Mouse4: 18 EP, 0.26sn 6.8 m/s patlama, i-frame yok!)
            // PT-04: [C] Madde Kristali (Alt = 3x6 Palet / Alt+Tekerlek) veya aktif insa sirasinda Alt tusu Dodge tetiklemez!
            float effectiveDodgeCost = DodgeStaminaCostEp * staminaDrainMult;
            bool allowAltDodge = Stage1InputConflictPolicy.CanTriggerCombatDodgeOnAlt(new GameplayInputContextSnapshot
            {
                IsCrystalActive = suppressAltDodge,
                IsBackpackOpen = isBackpackOpen
            });
            bool customDodgePressed = Stage1GameSettings.KeyDodge != KeyCode.LeftAlt &&
                                      Stage1GameSettings.KeyDodge != KeyCode.RightAlt &&
                                      Input.GetKeyDown(Stage1GameSettings.KeyDodge);
            bool altDodgePressed = allowAltDodge &&
                                   (Input.GetKeyDown(KeyCode.LeftAlt) ||
                                    (Stage1GameSettings.KeyDodge == KeyCode.LeftAlt && Input.GetKeyDown(Stage1GameSettings.KeyDodge)));
            bool dodgeTriggered = customDodgePressed ||
                                  altDodgePressed ||
                                  Input.GetKeyDown(KeyCode.Mouse3) ||
                                  Input.GetKeyDown(KeyCode.Mouse4);
            if (dodgeTriggered &&
                _dodgeRemainingSec <= 0f &&
                _dodgeCooldownSec <= 0f &&
                grounded &&
                _movementState.IsSubmergedInFluid == 0 &&
                _movementState.IsBackpackOverlayOpen == 0 &&
                _movementState.IsExhaustedStaminaLock == 0 &&
                canCrouch115 &&
                _vitals.Stamina >= effectiveDodgeCost)
            {
                if (staminaDrainMult > 0.01f) PhysicalCharacterControllerSystem.ConsumeStamina(ref _vitals, ref _movementState, effectiveDodgeCost);
                _dodgeDirectionWorld = wishDir.sqrMagnitude > 0.01f ? wishDir.normalized : -forwardFlat;
                _dodgeRemainingSec = DodgeDurationSeconds;
                _dodgeCooldownSec = DodgeCooldownSeconds;
                statusFeedback = "Fiziksel Yan-Adim / Dodge Kacisi (-18 EP | 6.8 m/s | i-Frame Yok!)";
            }

            // 0 EP Stamina Tukenme Kilidi (IsExhaustedStaminaLock != 0 -> %15 dolana kadar max 1.45 m/s yurume)
            // ve Hipotermi (-%20) / Aclik-Susuzluk (-%15) biyolojik hiz carpanlari:
            ApplyExhaustionAndBiologicalSpeedModifiers(ref statusFeedback);

            if (_dodgeRemainingSec > 0f)
            {
                float dodgeSpd = DodgeSpeedMetersPerSec * Stage1GameSettings.PlayerMoveSpeedMultiplier;
                _dodgeRemainingSec = Mathf.Max(0f, _dodgeRemainingSec - deltaTime);
                _movementState.CurrentSpeedMetersPerSec = dodgeSpd;
                _movementState.VelocityX = _dodgeDirectionWorld.x * dodgeSpd;
                _movementState.VelocityZ = _dodgeDirectionWorld.z * dodgeSpd;
            }

            ApplyJumpOrMantleVerticalImpulse(
                playerTransform, forwardFlat, jumpOrMantlePressed, grounded || _coyoteGroundedTimer > 0f, hasLedgeAhead, ledgeTopY, ref statusFeedback);
            StepFluidBuoyancyAndHazards(
                playerTransform, worldChunks, fluidChunks, dirtyFluidChunks, activeFluidChunks, deltaTime, ref statusFeedback);

            // WP-N1: Doygunlasmis islak camur zeminde (%15 - %35) gercekci hareket yavaslamasi
            if (grounded && _movementState.IsSubmergedInFluid == 0 && _dodgeRemainingSec <= 0f && soilMoistureSystem != null)
            {
                float mudMult = soilMoistureSystem.SampleGroundMudSlowdownMultiplier(
                    worldChunks, pos.x, pos.y, pos.z, out byte mudMoisture);
                if (mudMult < 0.995f)
                {
                    _movementState.CurrentSpeedMetersPerSec *= mudMult;
                    _movementState.VelocityX *= mudMult;
                    _movementState.VelocityZ *= mudMult;
                    if (string.IsNullOrEmpty(statusFeedback) && wishDir.sqrMagnitude > 0.02f)
                    {
                        int slowPct = Mathf.RoundToInt((1f - mudMult) * 100f);
                        statusFeedback = $"Camurlu Toprak (Nem %{(mudMoisture * 100) / 255}): Adimlar %{slowPct} Agirlasti";
                    }
                }
            }

            IntegrateHorizontalAndVerticalMotion(playerTransform, worldChunks, _dodgeRemainingSec > 0f ? _dodgeDirectionWorld : wishDir, deltaTime);
            StepActiveTreeBranchLoadAndBreakage(playerTransform, worldChunks, equippedMassKg, deltaTime, ref statusFeedback);
            ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
            EvaluatePlayerHeadSuffocationInSolidBlock(playerTransform, worldChunks, deltaTime, ref statusFeedback, isCreativeMode);
            if (isCreativeMode)
            {
                _vitals.CurrentHp = _vitals.MaxHp;
                _vitals.Stamina = _vitals.MaxStamina;
                _vitals.LungOxygenPercent = 100f;
                _movementState.IsExhaustedStaminaLock = 0;
            }
        }

        /// <summary>Oyuncunun su anda uzerinde durdugu 3B agac dalinin kimligi (dal ustunde degilse 0).</summary>
        public int CurrentSupportingTreeBranchId { get; private set; }

        /// <summary>Oyuncunun uzerinde durdugu agac dalinin anlik uc esneme/bukulme miktari (metre).</summary>
        public float CurrentTreeBranchDeflectionMeters { get; private set; }

        /// <summary>Oyuncunun uzerinde durdugu agac dalinin katmani (ThickPrimaryCarrier veya ThinOuterTerminal).</summary>
        public OrganicTreeBranchTier CurrentSupportingBranchTier { get; private set; }

        /// <summary>Son fizik adiminda oyuncunun bastigi agac dali asiri yukten/incelikten dolayi kirildi mi?</summary>
        public bool DidTreeBranchBreakLastStep { get; private set; }

        private void StepActiveTreeBranchLoadAndBreakage(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float equippedMassKg,
            float deltaTime,
            ref string statusFeedback)
        {
            DidTreeBranchBreakLastStep = false;
            OrganicTreeCanopyVisualSystem? treeSys = OrganicTreeCanopyVisualSystem.ActiveInstance;
            if (treeSys == null || treeSys.ActiveTreeCount <= 0)
            {
                CurrentSupportingTreeBranchId = 0;
                CurrentTreeBranchDeflectionMeters = 0f;
                return;
            }

            Vector3 pos = playerTransform.position;
            float footR = Mathf.Clamp(_movementState.CapsuleRadiusMeters * 0.68f, 0.16f, 0.26f);

            if (_jumpLaunchTimer <= 0.001f &&
                _verticalVelocity <= 0.25f &&
                treeSys.TrySampleWalkableBranchSurfaceAt(
                    pos.x,
                    pos.z,
                    pos.y,
                    footR,
                    MaxKneeStepUpMeters,
                    out float branchTopY,
                    out _,
                    out int hitBranchId,
                    out float normT01,
                    out OrganicTreeBranchTier hitTier))
            {
                float terrainY = SampleBilinearVoxelGroundHeight(worldChunks, pos.x, pos.z, pos.y);
                bool onBranchAboveTerrain = branchTopY >= terrainY - 0.04f && Mathf.Abs(pos.y - branchTopY) <= 0.48f;
                if (onBranchAboveTerrain)
                {
                    float totalMassKg = BaseBodyMassKg + Mathf.Max(0f, equippedMassKg);
                    float landingSpeed = Mathf.Max(0f, -_verticalVelocity);
                    treeSys.ApplyLoadAndStepBranchPhysics(
                        hitBranchId,
                        totalMassKg,
                        normT01,
                        deltaTime,
                        out float tipDeflection,
                        out float deflectedSurfaceY,
                        out bool branchBroke,
                        out BrokenTreeBranchDropEvent brokenDrop,
                        landingSpeed);

                    if (branchBroke)
                    {
                        CurrentSupportingTreeBranchId = 0;
                        CurrentTreeBranchDeflectionMeters = 0f;
                        DidTreeBranchBreakLastStep = true;
                        _movementState.IsGrounded = 0;
                        _coyoteGroundedTimer = 0f;
                        _verticalVelocity = Mathf.Min(_verticalVelocity, -1.65f);
                        pos.y += _verticalVelocity * deltaTime;
                        playerTransform.position = pos;
                        statusFeedback =
                            $"💥 AĞAÇ DALI KIRILDI! ({brokenDrop.Species} {brokenDrop.Tier} dalı {totalMassKg:F0} kg yüke dayanamadı -> +{brokenDrop.DroppedWoodCount} Odun düştü!)";
                        return;
                    }

                    CurrentSupportingTreeBranchId = hitBranchId;
                    CurrentTreeBranchDeflectionMeters = tipDeflection;
                    CurrentSupportingBranchTier = hitTier;
                    if (deflectedSurfaceY > -20f && Mathf.Abs(deflectedSurfaceY - pos.y) <= 0.45f)
                    {
                        pos.y = Mathf.MoveTowards(pos.y, deflectedSurfaceY, deltaTime * 22f);
                        playerTransform.position = pos;
                    }
                    _movementState.IsGrounded = 1;
                    _coyoteGroundedTimer = 0.16f;
                    return;
                }
            }

            CurrentSupportingTreeBranchId = 0;
            CurrentTreeBranchDeflectionMeters = 0f;
            treeSys.StepAllTreeBranchesPhysics(deltaTime);
        }

        private void StepCreativeFlightPhysics(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector2 moveInput,
            bool wantSprint,
            bool ascendHeld,
            bool descendHeld,
            float deltaTime,
            ref string statusFeedback)
        {
            _movementState.Stance = wantSprint ? CharacterStance.Sprinting : CharacterStance.Standing;
            _movementState.CapsuleHeightMeters = PhysicalCharacterControllerSystem.GetStanceCapsuleHeight(CharacterStance.Standing);
            _movementState.CapsuleRadiusMeters = PhysicalCharacterControllerSystem.GetStanceCapsuleRadius(CharacterStance.Standing);
            _movementState.IsGrounded = 0;
            _movementState.IsSubmergedInFluid = 0;

            Vector3 forwardFlat = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            Vector3 rightFlat = Vector3.ProjectOnPlane(playerTransform.right, Vector3.up).normalized;
            Vector3 wishDir = forwardFlat * moveInput.y + rightFlat * moveInput.x;
            if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

            float flyHorizSpeed = (wantSprint ? 21.6f : 10.9f) * Stage1GameSettings.PlayerMoveSpeedMultiplier;
            Vector3 targetHorizVel = wishDir * flyHorizSpeed;
            Vector3 curHorizVel = new Vector3(_movementState.VelocityX, 0f, _movementState.VelocityZ);
            float horizAccel = wishDir.sqrMagnitude > 0.01f ? 75f : 36f;
            curHorizVel = Vector3.MoveTowards(curHorizVel, targetHorizVel, deltaTime * horizAccel);

            _movementState.VelocityX = curHorizVel.x;
            _movementState.VelocityZ = curHorizVel.z;
            _movementState.CurrentSpeedMetersPerSec = curHorizVel.magnitude;

            float targetVertVel = 0f;
            if (ascendHeld && !descendHeld)
            {
                targetVertVel = wantSprint ? 14.0f : 8.5f;
            }
            else if (descendHeld && !ascendHeld)
            {
                targetVertVel = wantSprint ? -14.0f : -8.5f;
            }
            _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, targetVertVel, deltaTime * 90f);

            Vector3 pos = playerTransform.position;
            float prevFeetY = pos.y;
            float capHeight = _movementState.CapsuleHeightMeters;
            float capRadius = _movementState.CapsuleRadiusMeters;

            Vector3 stepDelta = curHorizVel * deltaTime;
            Vector3 candidatePos = new Vector3(pos.x + stepDelta.x, pos.y, pos.z + stepDelta.z);
            if (!IsHorizontalStepBlockedWithModular(worldChunks, pos, candidatePos, capHeight, capRadius))
            {
                pos.x = candidatePos.x;
                pos.z = candidatePos.z;
            }
            else
            {
                Vector3 candidateX = new Vector3(pos.x + stepDelta.x, pos.y, pos.z);
                if (Mathf.Abs(stepDelta.x) > 0.0001f && !IsHorizontalStepBlockedWithModular(worldChunks, pos, candidateX, capHeight, capRadius))
                {
                    pos.x = candidateX.x;
                }
                Vector3 candidateZ = new Vector3(pos.x, pos.y, pos.z + stepDelta.z);
                if (Mathf.Abs(stepDelta.z) > 0.0001f && !IsHorizontalStepBlockedWithModular(worldChunks, pos, candidateZ, capHeight, capRadius))
                {
                    pos.z = candidateZ.z;
                }
            }

            float hx = pos.x, hz = pos.z;
            if (ResolveHorizontalSolidCapsuleDepenetrationPure(worldChunks, GetActiveModularPiecesSpan(), ref hx, pos.y, ref hz, capHeight, capRadius))
            {
                pos.x = hx;
                pos.z = hz;
            }

            pos.y = Mathf.Clamp(pos.y + _verticalVelocity * deltaTime, 1.5f, Stage1InteractivePlayController.MaxEditableVoxelY - 3.0f);
            if (_verticalVelocity > 0f && TryResolveCeilingHeadBonkWithModular(worldChunks, ref pos, capHeight, capRadius))
            {
                _verticalVelocity = 0f;
            }

            if (_verticalVelocity <= 0f)
            {
                float nextGroundY = SampleSupportGroundHeightWithModular(worldChunks, pos);
                float unbreakableFloorY = ComputeUnbreakableSolidFloorStopY(worldChunks, GetActiveModularPiecesSpan(), pos.x, pos.z, prevFeetY, pos.y, capRadius * 0.62f);
                bool hitUnbreakableFloor = unbreakableFloorY > pos.y + 0.0001f;
                bool hitSurfaceGround = nextGroundY > -20f && pos.y <= nextGroundY + 0.05f;

                if (hitUnbreakableFloor || hitSurfaceGround)
                {
                    float floorY = hitUnbreakableFloor && hitSurfaceGround
                        ? Mathf.Max(nextGroundY, unbreakableFloorY)
                        : (hitUnbreakableFloor ? unbreakableFloorY : nextGroundY);
                    pos.y = floorY;
                    _verticalVelocity = 0f;
                    // Minecraft gibi zemine dokunuldugunda ucusu otomatik kapatip yere bas!
                    if (_creativeFlyTakeoffGraceSec <= 0f && !ascendHeld)
                    {
                        IsCreativeFlying = false;
                        _movementState.IsGrounded = 1;
                        _coyoteGroundedTimer = 0.16f;
                        statusFeedback = "🦶 Zemine inildi — Yaratıcı uçuş kapandı (Çift [Space] ile tekrar uçabilirsiniz).";
                    }
                }
            }

            playerTransform.position = pos;
        }

        private void EvaluatePlayerHeadSuffocationInSolidBlock(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float deltaTime,
            ref string statusFeedback,
            bool isCreativeMode = false)
        {
            float eyeOffset = Mathf.Clamp(_movementState.CapsuleHeightMeters * 0.84f, 0.38f, 1.52f);
            Vector3 headPos = playerTransform.position + Vector3.up * eyeOffset;
            bool suffocating = StepSolidBlockSuffocationHazard(
                worldChunks,
                headPos.x,
                headPos.y,
                headPos.z,
                isCreativeMode ? 0f : deltaTime,
                ref _vitals,
                out byte blockMat,
                out float alpha01,
                out float dmgHp);

            IsHeadSuffocatingInSolidBlock = suffocating && !isCreativeMode;
            SuffocatingBlockMaterialId = blockMat;
            SuffocationOverlayAlpha01 = suffocating ? Mathf.Max(0.96f, alpha01) : alpha01;
            LastSuffocationDamageHp = dmgHp;

            if (suffocating && !isCreativeMode)
            {
                statusFeedback = $"⚠ BLOK İÇİNDE SIKIŞMA / BOĞULMA! (-{SolidBlockSuffocationDamagePerSecond:0} CAN/sn) Kazarak veya hareket ederek çıkın!";
            }
        }

        private static bool IsHardCollisionVoxel(uint raw)
        {
            if (!VoxelBitUtility.IsPhysicallySolid(raw)) return false;
            byte mat = VoxelBitUtility.GetMaterialId(raw);
            return mat != MaterialIdCatalog.Air && mat != MaterialIdCatalog.GlowingMoss;
        }

        private static void MeasureHeadroomClearance(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 feetPos,
            float supportGroundY,
            out bool canCrouch115,
            out bool canStand180)
        {
            int wx = Mathf.FloorToInt(feetPos.x);
            int wz = Mathf.FloorToInt(feetPos.z);
            float refFeetY = supportGroundY > -20f ? Mathf.Max(feetPos.y, supportGroundY) : feetPos.y;
            int airBaseWy = Mathf.FloorToInt(refFeetY + 0.25f);

            // Smooth SurfaceNets egimli arazide zemin kolonu (wx, wz) kesirli refFeetY'den 1-2 voksel yukari uzanabilir;
            // alttaki kesintisiz zemin bloklarini gecerek oyuncunun ayak/govdesinin bulundugu GERCEK HAVA hucresini bul!
            int maxGroundSkipWy = Mathf.FloorToInt(refFeetY) + 2;
            while (airBaseWy <= maxGroundSkipWy && IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy, wz)))
            {
                airBaseWy++;
            }

            // Eger ayak hizasinda acik hava hucresi bulunamadiysa (gomulme cozumu devredeyse) sahte tavan kilidi uygulama
            if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy, wz)))
            {
                canCrouch115 = true;
                canStand180 = true;
                return;
            }

            bool blockedCrouchHead = IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy + 1, wz));
            bool blockedStandHead = IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, airBaseWy + 2, wz));

            canCrouch115 = !blockedCrouchHead;
            canStand180 = canCrouch115 && !blockedStandHead;
        }

        private void ApplyExhaustionAndBiologicalSpeedModifiers(ref string statusFeedback)
        {
            if (_movementState.Stance == CharacterStance.Sliding) return;

            float speed = _movementState.CurrentSpeedMetersPerSec;
            if (_movementState.IsExhaustedStaminaLock != 0)
            {
                speed = Mathf.Min(speed, ExhaustedWalkSpeedCapMps);
                if (string.IsNullOrEmpty(statusFeedback))
                {
                    statusFeedback = $"SOLUK SOLUĞA YORGUNLUK KILIDI! (Stamina %{_vitals.Stamina:F0} < %15 -> Ziplama/Depar Kilitli, Hiz 1.45 m/s)";
                }
            }

            if (Stage1GameSettings.EnableEnvironmentalEffects &&
                _vitals.CoreBodyTempCelsius < ThermalAndBiologySystem.HypothermiaThresholdCelsius)
            {
                speed *= 0.80f; // Hipotermi: -%20 hareket hizi cezasi
            }

            if (_vitals.HungerKcal <= 25.0f || _vitals.ThirstHydration <= 25.0f)
            {
                speed *= 0.85f; // Agir aclik veya susuzluk: -%15 hareket hizi cezasi
            }

            speed *= Stage1GameSettings.PlayerMoveSpeedMultiplier;
            _movementState.CurrentSpeedMetersPerSec = speed;
            Vector2 horiz = new Vector2(_movementState.VelocityX, _movementState.VelocityZ);
            if (horiz.sqrMagnitude > 0.0001f)
            {
                Vector2 dir = horiz.normalized;
                _movementState.VelocityX = dir.x * speed;
                _movementState.VelocityZ = dir.y * speed;
            }
        }

        private void StepFluidBuoyancyAndHazards(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]>? fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            HashSet<(int cx, int cz)>? activeFluidChunks,
            float dt,
            ref string statusFeedback)
        {
            WaterSplashPulse01 = Mathf.Max(0f, WaterSplashPulse01 - dt * 1.65f);
            if (_swimmerSurgeCooldownSec > 0f)
            {
                _swimmerSurgeCooldownSec = Mathf.Max(0f, _swimmerSurgeCooldownSec - dt);
            }

            Vector3 pos = playerTransform.position;
            if (fluidChunks == null)
            {
                _vitals.SubmergedDepthMeters = 0f;
                _movementState.IsSubmergedInFluid = 0;
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, 0f, dt * 3.0f);
                _wasTouchingWaterLastFrame = false;
                _wasHeadSubmergedLastFrame = false;
                LastBreathAirVoxelY = -1;
                ApplySolidMagmaContactIfNeeded(worldChunks, pos, dt, inLiquidLava: false, ref statusFeedback);
                return;
            }
            int wx = Mathf.FloorToInt(pos.x), wz = Mathf.FloorToInt(pos.z);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4), cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? f) || f == null)
            {
                _vitals.SubmergedDepthMeters = 0f;
                _movementState.IsSubmergedInFluid = 0;
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, 0f, dt * 3.0f);
                _wasTouchingWaterLastFrame = false;
                _wasHeadSubmergedLastFrame = false;
                LastBreathAirVoxelY = -1;
                ApplySolidMagmaContactIfNeeded(worldChunks, pos, dt, inLiquidLava: false, ref statusFeedback);
                return;
            }

            int lx = wx - cx * 16, lz = wz - cz * 16;
            int feetWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 0.25f), 1, 254);
            int waistWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + 0.85f), 1, 254);
            FluidCell16 feetCell = new FluidCell16(f[(feetWy << 8) | (lz << 4) | lx]);
            FluidCell16 waistCell = new FluidCell16(f[(waistWy << 8) | (lz << 4) | lx]);

            bool inLava = (feetCell.Type == FluidType.Lava && feetCell.Mass >= 36) || (waistCell.Type == FluidType.Lava && waistCell.Mass >= 24);
            bool inAcid = (feetCell.Type == FluidType.Acid && feetCell.Mass >= 36) || (waistCell.Type == FluidType.Acid && waistCell.Mass >= 24);
            bool inWater = waistCell.Type == FluidType.Water && waistCell.Mass >= 40;
            bool wadingWater = feetCell.Type == FluidType.Water && feetCell.Mass >= 14;
            bool touchingWater = inWater || wadingWater;

            worldChunks.TryGetValue((cx, cz), out uint[]? solidVox);
            ReadOnlySpan<uint> solidSpan = solidVox != null ? solidVox.AsSpan() : ReadOnlySpan<uint>.Empty;
            ComputeBreathSubmersionState(
                f.AsSpan(), solidSpan, pos.y, lx, lz, 256, _wasHeadSubmergedLastFrame,
                out bool headSubmerged, out float calcSubmergedDepth, out int breathAirWy);
            _wasHeadSubmergedLastFrame = headSubmerged;
            LastBreathAirVoxelY = breathAirWy;
            _vitals.SubmergedDepthMeters = calcSubmergedDepth;

            _movementState.IsSubmergedInFluid = (inWater || inLava || inAcid) ? (byte)1 : (byte)0;

            int surfaceWy = inWater ? waistWy : feetWy;
            float waterTopSurfaceY = surfaceWy + 0.55f;
            if (touchingWater)
            {
                for (int sy = surfaceWy; sy <= Mathf.Min(253, surfaceWy + 12); sy++)
                {
                    FluidCell16 c = new FluidCell16(f[(sy << 8) | (lz << 4) | lx]);
                    if (c.Type == FluidType.Water && c.Mass > 0)
                    {
                        surfaceWy = sy;
                    }
                    else
                    {
                        break;
                    }
                }

                int col = (lz << 4) | lx;
                ComputeEffectiveFluidSurfaceAndBed(f.AsSpan(), solidSpan, col, surfaceWy, out _, out waterTopSurfaceY);

                float horizSpd = Mathf.Sqrt(_movementState.VelocityX * _movementState.VelocityX + _movementState.VelocityZ * _movementState.VelocityZ);
                WaterInteractionWorldPos = new Vector3(pos.x, waterTopSurfaceY, pos.z);
                float targetStrength = Mathf.Clamp01((inWater ? 0.52f : 0.30f) + horizSpd * 0.14f + Mathf.Abs(_verticalVelocity) * 0.06f);
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, targetStrength, dt * 6.5f);

                if (!_wasTouchingWaterLastFrame)
                {
                    float impactSpeed = Mathf.Max(horizSpd * 0.5f, -_verticalVelocity);
                    WaterSplashPulse01 = Mathf.Clamp01(0.45f + impactSpeed * 0.11f);
                    if (inWater && _verticalVelocity < -3.0f)
                    {
                        // Suya yukaridan atlayinca su direnci dususu yumusatir
                        _verticalVelocity *= 0.45f;
                    }

                    // Yalnizca sig birikintiye yuksek hizla atlandiginda tek seferlik fiziksel sicrama uygula;
                    // derin deniz/gol icinde yuzerken her 0.28s'de voksel sokup kare dalgalanma URETME (GPU halkasi cizer)!
                    if (!inWater && impactSpeed >= 4.5f)
                    {
                        int splashUnits = Mathf.Clamp(Mathf.RoundToInt(4f + impactSpeed * 1.2f), 4, 12);
                        ApplyMassConservingSwimmerSurge(
                            worldChunks, fluidChunks, dirtyFluidChunks, activeFluidChunks, wx, surfaceWy, wz, splashUnits);
                    }
                    _swimmerSurgeCooldownSec = 0.50f;
                }
            }
            else
            {
                WaterInteractionStrength01 = Mathf.MoveTowards(WaterInteractionStrength01, 0f, dt * 2.4f);
            }
            _wasTouchingWaterLastFrame = touchingWater;

            if (inLava)
            {
                _movementState.CurrentSpeedMetersPerSec *= 0.42f;
                if (Stage1GameSettings.EnableEnvironmentalEffects)
                {
                    _vitals.CurrentHp = Mathf.Max(0f, _vitals.CurrentHp - 18.0f * dt);
                    _vitals.CoreBodyTempCelsius = Mathf.Min(43.0f, _vitals.CoreBodyTempCelsius + 4.0f * dt);
                    statusFeedback = "VOLKANIK LAV YANIGI! (-18 HP/s) Hemen lav kalderasindan cikin!";
                }
            }
            else if (inAcid)
            {
                _movementState.CurrentSpeedMetersPerSec *= 0.52f;
                if (Stage1GameSettings.EnableEnvironmentalEffects)
                {
                    _vitals.CurrentHp = Mathf.Max(0f, _vitals.CurrentHp - 12.0f * dt);
                    statusFeedback = "KORROZYON ASIDI YANIGI! (-12 HP/s) Kimyasal asit havuzundan hemen cikin!";
                }
            }
            else if (inWater)
            {
                _movementState.Stance = CharacterStance.Swimming;
                _movementState.CurrentSpeedMetersPerSec *= 0.62f;
                _vitals.Wetness01 = 1.0f;

                float totalMassKg = BaseBodyMassKg + _movementState.EquippedMassKg;
                if (totalMassKg > 115.0f)
                {
                    // Agir zirh/yuk (>115kg toplam kutle) Arsimet kaldirma kuvvetini asar ve oyuncuyu dibe batirir!
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, -2.4f, dt * 6.5f);
                    statusFeedback = $"AGIR ZIRH BATISI! (Toplam Kutle={totalMassKg:F0}kg > 115kg -> Suya Batiyorsun! [X] ile Agir Silahi Birak!)";
                }
                else if (Input.GetKey(KeyCode.Space))
                {
                    _verticalVelocity = Mathf.Max(_verticalVelocity, 3.6f);
                    _jumpLaunchTimer = 0.12f;
                    _movementState.IsGrounded = 0;
                    statusFeedback = "Suda Yuzme (Arsimet Kaldirma Kuvveti + Space)";
                }
                else if (Input.GetKey(KeyCode.LeftControl))
                {
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, -3.0f, dt * 10.0f);
                }
                else
                {
                    // Dogal Arsimet yuzey dengesi: Agiz/burun kotunu (pos.y + 1.52) su yuzeyinin ~18cm ustunde tutacak sekilde yumusak yuzdur!
                    float targetFloatFeetY = waterTopSurfaceY - 1.34f;
                    float depthBelowFloat = targetFloatFeetY - pos.y;
                    if (depthBelowFloat > -0.15f)
                    {
                        float targetVel = Mathf.Clamp(depthBelowFloat * 3.8f, -0.8f, 2.6f);
                        _verticalVelocity = Mathf.MoveTowards(
                            _verticalVelocity,
                            targetVel + StratocraftConstants.GravityAcceleration * dt,
                            dt * 26.0f);
                    }
                }
            }
            else
            {
                _vitals.Wetness01 = Mathf.Max(0f, _vitals.Wetness01 - 0.08f * dt);
            }

            ApplySolidMagmaContactIfNeeded(worldChunks, pos, dt, inLiquidLava: inLava, ref statusFeedback);
        }

        private void ApplySolidMagmaContactIfNeeded(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 playerPos,
            float dt,
            bool inLiquidLava,
            ref string statusFeedback)
        {
            if (inLiquidLava)
            {
                IsTouchingHotMagmaBlock = false;
                LastMagmaContactSeconds = 0f;
                return;
            }

            bool touchingMagma = StepSolidMagmaBlockContactHazard(
                worldChunks,
                playerPos.x,
                playerPos.y,
                playerPos.z,
                dt,
                Stage1GameSettings.EnableEnvironmentalEffects,
                ref _vitals,
                out float dmgHp,
                out float heatDeltaC);

            if (touchingMagma)
            {
                IsTouchingHotMagmaBlock = true;
                LastMagmaContactSeconds += dt;
                _pendingMagmaBurnHp += dmgHp;
                _pendingMagmaCoreHeatCelsius += heatDeltaC;
                if (Stage1GameSettings.EnableEnvironmentalEffects)
                {
                    statusFeedback = "SICAK VOLKANIK KAYA YANIGI! (-9 HP/s) Kor MagmaBlogu ustunden inin!";
                }
            }
            else
            {
                IsTouchingHotMagmaBlock = false;
                LastMagmaContactSeconds = 0f;
            }
        }

        private void ApplyJumpOrMantleVerticalImpulse(
            Transform playerTransform, Vector3 forwardFlat, bool jumpOrMantlePressed, bool canJumpFromGround,
            bool hasLedgeAhead, float ledgeTopY, ref string statusFeedback)
        {
            if (!jumpOrMantlePressed) return;
            if (_movementState.IsExhaustedStaminaLock != 0)
            {
                statusFeedback = "YORGUNLUK KILIDI (Stamina < %15): Ziplamak veya tirmanmak icin nefeslenin!";
                return;
            }

            float drainMult = Stage1GameSettings.StaminaDrainMultiplier;
            float mantleCost = 15.0f * drainMult;
            if (hasLedgeAhead && !_isMantling && _vitals.Stamina >= mantleCost)
            {
                if (drainMult > 0.01f) PhysicalCharacterControllerSystem.ConsumeStamina(ref _vitals, ref _movementState, mantleCost);

                Vector3 pos = playerTransform.position;
                Vector3 targetPos = pos + forwardFlat * 0.68f;
                targetPos.y = Mathf.Max(pos.y + 0.45f, Mathf.Min(ledgeTopY + 0.04f, pos.y + 2.25f));

                _isMantling = true;
                _mantleElapsedSeconds = 0f;
                _movementState.MantleProgress01 = 0f;
                _movementState.Stance = CharacterStance.Climbing;
                _movementState.IsGrounded = 0;
                _coyoteGroundedTimer = 0f;
                _verticalVelocity = 0f;
                _mantleStartPos = new System.Numerics.Vector3(pos.x, pos.y, pos.z);
                _mantleTargetPos = new System.Numerics.Vector3(targetPos.x, targetPos.y, targetPos.z);
                statusFeedback = "0.55s Kubik Bezier Mantle Tirmanisi (-15 EP)";
                return;
            }

            float jumpCost = 8.0f * drainMult;
            if (canJumpFromGround && _vitals.Stamina >= jumpCost)
            {
                if (drainMult > 0.01f) PhysicalCharacterControllerSystem.ConsumeStamina(ref _vitals, ref _movementState, jumpCost);
                _verticalVelocity = 6.45f * Stage1GameSettings.PlayerJumpMultiplier;
                _jumpLaunchTimer = 0.20f;
                _coyoteGroundedTimer = 0f;
                _movementState.IsGrounded = 0;
                statusFeedback = "Ziplama (-8 EP)";
            }
        }

        private void IntegrateHorizontalAndVerticalMotion(
            Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 wishDir, float deltaTime)
        {
            Vector3 pos = playerTransform.position;
            float prevFeetY = pos.y;
            Vector3 horizVel = new Vector3(_movementState.VelocityX, 0f, _movementState.VelocityZ);
            if (_movementState.Stance == CharacterStance.Sliding && horizVel.sqrMagnitude < 0.01f)
            {
                horizVel = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized * _movementState.CurrentSpeedMetersPerSec;
            }
            else if (wishDir.sqrMagnitude > 0.001f)
            {
                horizVel = wishDir * _movementState.CurrentSpeedMetersPerSec;
            }

            float capHeight = _movementState.CapsuleHeightMeters;
            float capRadius = _movementState.CapsuleRadiusMeters;
            Vector3 stepDelta = horizVel * deltaTime;
            Vector3 candidatePos = new Vector3(pos.x + stepDelta.x, pos.y, pos.z + stepDelta.z);
            if (!IsHorizontalStepBlockedWithModular(worldChunks, pos, candidatePos, capHeight, capRadius))
            {
                pos.x = candidatePos.x;
                pos.z = candidatePos.z;
            }
            else
            {
                // Capraz duvar/agac temasinda oyuncunun tamamen takilip durmasini onlemek icin X ve Z eksenlerinde ayri kayma (Wall-Slide)
                Vector3 candidateX = new Vector3(pos.x + stepDelta.x, pos.y, pos.z);
                if (Mathf.Abs(stepDelta.x) > 0.0001f &&
                    !IsHorizontalStepBlockedWithModular(worldChunks, pos, candidateX, capHeight, capRadius))
                {
                    pos.x = candidateX.x;
                }

                Vector3 candidateZ = new Vector3(pos.x, pos.y, pos.z + stepDelta.z);
                if (Mathf.Abs(stepDelta.z) > 0.0001f &&
                    !IsHorizontalStepBlockedWithModular(worldChunks, pos, candidateZ, capHeight, capRadius))
                {
                    pos.z = candidateZ.z;
                }
            }

            float nextGroundY = SampleSupportGroundHeightWithModular(worldChunks, pos);
            bool stickToSlope = _jumpLaunchTimer <= 0.001f &&
                                (_movementState.IsGrounded != 0 || _coyoteGroundedTimer > 0.01f) &&
                                _verticalVelocity <= 0.10f &&
                                nextGroundY > -20f &&
                                (nextGroundY - pos.y) <= MaxKneeStepUpMeters &&
                                (pos.y - nextGroundY) <= 0.65f;
            if (stickToSlope)
            {
                float diff = nextGroundY - pos.y;
                pos.y = Mathf.Abs(diff) < 0.02f ? nextGroundY : Mathf.MoveTowards(pos.y, nextGroundY, deltaTime * 28f);
                _verticalVelocity = 0f;
                _movementState.IsGrounded = 1;
                _coyoteGroundedTimer = 0.16f;
            }
            else
            {
                _verticalVelocity -= StratocraftConstants.GravityAcceleration * deltaTime;
                pos.y += _verticalVelocity * deltaTime;

                // Yukari ziplama esnasinda magara tavani, agac dali veya moduler yapi tavan/cati carpismasi
                if (_verticalVelocity > 0f &&
                    TryResolveCeilingHeadBonkWithModular(worldChunks, ref pos, capHeight, capRadius))
                {
                    _verticalVelocity = 0f;
                    _jumpLaunchTimer = 0f;
                }

                // Kirilmaz Kati Taban Kilidi: Duvar kosesine sikisilsa veya kati blok icine gomulunse bile
                // altindaki kati maddenin icinden asagi dusmek %100 imkansizdir (magarada yukari isinlama YOK!).
                // NOT: ComputeUnbreakableSolidFloorStopY havada zemin bulamadiginda candidateFeetY (pos.y) doner;
                // yalnizca gercek temas (unbreakableFloorY > pos.y + 0.0001f || hitSurfaceGround) aninda zemine oturt!
                float unbreakableFloorY = ComputeUnbreakableSolidFloorStopY(
                    worldChunks, GetActiveModularPiecesSpan(), pos.x, pos.z, prevFeetY, pos.y, capRadius * 0.62f);
                bool hitUnbreakableFloor = unbreakableFloorY > pos.y + 0.0001f;
                bool hitSurfaceGround = nextGroundY > -20f && pos.y <= nextGroundY;

                if (_jumpLaunchTimer <= 0.001f && _verticalVelocity <= 0.05f && (hitUnbreakableFloor || hitSurfaceGround))
                {
                    float effectiveGroundStopY = hitUnbreakableFloor && hitSurfaceGround
                        ? Mathf.Max(nextGroundY, unbreakableFloorY)
                        : (hitUnbreakableFloor ? unbreakableFloorY : nextGroundY);
                    pos.y = effectiveGroundStopY;
                    _verticalVelocity = 0f;
                    _movementState.IsGrounded = 1;
                    _coyoteGroundedTimer = 0.16f;
                }
            }
            playerTransform.position = pos;
        }

        private Vector3 _combatFootworkImpulseVel;
        private float _combatFootworkDecayRate = 12.0f;
        private float _combatVerticalDipMeters;

        /// <summary>Faz K.3: Blok geri savrulmasinda olusan diz/kamera dikey cokmesi (m, <= 0).</summary>
        public float CurrentCombatVerticalDipMeters => _combatVerticalDipMeters;

        /// <summary>
        /// Faz K.3: Savas ayak isi (Lunge, Passing Step, MasterDeflect sidestep veya 0-kare blok geri savrulmasi)
        /// hiz itkisini kuyruga ekler.
        /// </summary>
        public void AddCombatFootworkImpulse(
            Vector3 worldVel,
            float decayRate = 12.0f,
            bool isKnockback = false,
            float verticalDipMeters = 0f)
        {
            worldVel.y = 0f;
            if (isKnockback)
            {
                _combatFootworkImpulseVel = worldVel;
                _combatFootworkDecayRate = Mathf.Clamp(decayRate, 4.0f, 24.0f);
            }
            else
            {
                _combatFootworkImpulseVel += worldVel;
                _combatFootworkImpulseVel = Vector3.ClampMagnitude(_combatFootworkImpulseVel, 9.5f);
                _combatFootworkDecayRate = Mathf.Clamp(decayRate, 6.0f, 28.0f);
            }

            if (verticalDipMeters < _combatVerticalDipMeters)
            {
                _combatVerticalDipMeters = Mathf.Clamp(verticalDipMeters, -0.08f, 0f);
            }
        }

        /// <summary>
        /// Faz K.3: Kuyruktaki savas ayak isi / blok geri savrulma itkisini ayni karede (0-kare gecikme)
        /// voksel Wall-Slide korumali olarak uygular ve ustel olarak sonumler.
        /// </summary>
        public void FlushCombatFootworkImpulses(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            float deltaTime)
        {
            float dt = Mathf.Clamp(deltaTime, 0.0001f, 0.05f);
            if (_combatVerticalDipMeters < -0.0001f)
            {
                _combatVerticalDipMeters = Mathf.MoveTowards(_combatVerticalDipMeters, 0f, dt * 0.24f);
            }
            else
            {
                _combatVerticalDipMeters = 0f;
            }

            if (_combatFootworkImpulseVel.sqrMagnitude < 0.0004f)
            {
                _combatFootworkImpulseVel = Vector3.zero;
                return;
            }

            Vector3 stepDelta = _combatFootworkImpulseVel * dt;
            ApplyWeaponMomentumHorizontalStep(playerTransform, worldChunks, stepDelta, dt);
            float decay = Mathf.Exp(-_combatFootworkDecayRate * dt);
            _combatFootworkImpulseVel *= decay;
            if (_combatFootworkImpulseVel.sqrMagnitude < 0.0004f)
            {
                _combatFootworkImpulseVel = Vector3.zero;
            }
        }

        /// <summary>
        /// Kılıcın ağırlığına göre savurma veya ağır siper taşıma sırasında oluşan yatay eylemsizlik çekişini
        /// voksel duvar (X/Z ayrı Wall-Slide) ve zemin çarpışması korumalı olarak oyuncu gövdesine uygular.
        /// </summary>
        public void ApplyWeaponMomentumHorizontalStep(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 worldHorizontalDelta,
            float deltaTime)
        {
            if (playerTransform == null) return;
            worldHorizontalDelta.y = 0f;
            if (worldHorizontalDelta.sqrMagnitude < 0.000001f) return;

            Vector3 pos = playerTransform.position;
            float capH = _movementState.CapsuleHeightMeters;
            float capR = _movementState.CapsuleRadiusMeters;
            Vector3 candidatePos = pos + worldHorizontalDelta;
            bool moved = false;

            if (!IsHorizontalStepBlockedWithModular(worldChunks, pos, candidatePos, capH, capR))
            {
                float nextGroundY = SampleSupportGroundHeightWithModular(worldChunks, candidatePos);
                if (nextGroundY - pos.y <= MaxKneeStepUpMeters)
                {
                    pos.x = candidatePos.x;
                    pos.z = candidatePos.z;
                    moved = true;
                }
            }
            else
            {
                // Faz K.3: X/Z ayri Wall-Slide (duvara capraz lunge/pushback carptiginda kilitlenmek yerine kayar)
                if (Mathf.Abs(worldHorizontalDelta.x) > 0.0001f)
                {
                    Vector3 candX = new Vector3(pos.x + worldHorizontalDelta.x, pos.y, pos.z);
                    if (!IsHorizontalStepBlockedWithModular(worldChunks, pos, candX, capH, capR) &&
                        SampleSupportGroundHeightWithModular(worldChunks, candX) - pos.y <= MaxKneeStepUpMeters)
                    {
                        pos.x = candX.x;
                        moved = true;
                    }
                }
                if (Mathf.Abs(worldHorizontalDelta.z) > 0.0001f)
                {
                    Vector3 candZ = new Vector3(pos.x, pos.y, pos.z + worldHorizontalDelta.z);
                    if (!IsHorizontalStepBlockedWithModular(worldChunks, pos, candZ, capH, capR) &&
                        SampleSupportGroundHeightWithModular(worldChunks, candZ) - pos.y <= MaxKneeStepUpMeters)
                    {
                        pos.z = candZ.z;
                        moved = true;
                    }
                }
            }

            if (moved)
            {
                float finalGroundY = SampleSupportGroundHeightWithModular(worldChunks, pos);
                if (_jumpLaunchTimer <= 0.001f && _movementState.IsGrounded != 0 && _verticalVelocity <= 0.05f && finalGroundY > -20f)
                {
                    float diff = finalGroundY - pos.y;
                    pos.y = diff > 0f ? finalGroundY : (Mathf.Abs(diff) < 0.05f ? finalGroundY : Mathf.MoveTowards(pos.y, finalGroundY, deltaTime * 24f));
                }
                playerTransform.position = pos;
                ResolveVoxelAndColliderDepenetration(playerTransform, worldChunks);
            }
        }

        /// <summary>
        /// HybridSurfaceNetsAndShapeMesher (IsoLevel = 64) ile birebir ayni dikey kenar kesisimini (y + (d - 64) / (d - dAbove)) hesaplar.
        /// Boylece fiziksel ayak tabani gorsel cimen/toprak ucgenlerinin tam uzerine oturur; ne havada durma ne de 20-30 Hz ziplatma yasanir.
        /// </summary>
        public static float GetVoxelWalkableTopY(uint raw, uint aboveRaw, int wy)
            => GetVoxelWalkableTopYPure(raw, aboveRaw, wy);

        /// <summary>Oyuncu kapsulunun kati voksel icine girmesini tespit edip guvenli acik yone cikarir (magara tavani ve agaclarda ASLA yukari isinlamaz ve puruzsuz arazide ASLA +1m ziplatmaz!).</summary>
        public void ResolveVoxelAndColliderDepenetration(Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            Vector3 pos = playerTransform.position;

            // 1) Gercek 3B Yatay Kati Kapsul Depenetrasyonu (voksel + 3B moduler yapi duvarlari; Y'ye ASLA dokunmaz!)
            float hx = pos.x, hz = pos.z;
            if (ResolveHorizontalSolidCapsuleDepenetrationPure(
                    worldChunks, GetActiveModularPiecesSpan(), ref hx, pos.y, ref hz, _movementState.CapsuleHeightMeters, _movementState.CapsuleRadiusMeters))
            {
                pos.x = hx;
                pos.z = hz;
                playerTransform.position = pos;
            }

            // Yukari ziplarken asla dikey zemin yakalama calistirma (magara tavanina tutunup yukari cekmeyi %100 engeller)
            if (_jumpLaunchTimer > 0.001f || _verticalVelocity > 0.05f) return;

            bool wasGroundedOrCoyote = _movementState.IsGrounded != 0 || _coyoteGroundedTimer > 0.01f;
            if (wasGroundedOrCoyote)
            {
                float supportY = SampleSupportGroundHeightWithModular(worldChunks, pos);
                float maxSafeRecovery = supportY > -20f && !IsHardCollisionVoxel(ReadWorldVoxelRaw(
                        worldChunks, Mathf.FloorToInt(pos.x), Mathf.FloorToInt(supportY + 1.55f), Mathf.FloorToInt(pos.z)))
                    ? 1.72f
                    : MaxKneeStepUpMeters;
                if (supportY > -20f && pos.y < supportY - 0.005f && (supportY - pos.y) <= maxSafeRecovery)
                {
                    float burialDepth = supportY - pos.y;
                    pos.y = burialDepth <= 0.24f
                        ? supportY
                        : Mathf.MoveTowards(pos.y, supportY, 0.42f);
                    if (_verticalVelocity < 0f) _verticalVelocity = 0f;
                    _movementState.IsGrounded = 1;
                    _coyoteGroundedTimer = 0.16f;
                    playerTransform.position = pos;
                    return;
                }
            }

            // Yalnizca oyuncunun ayak bilegi kati blok icinde gercekten gomulduyse (orn. altina blok koyma / kum dusmesi)
            // ve ustteki 2 voksel tamamen acik havaysa tam izo-yuzey yuksekligine (exactTopY) cikar;
            // ShapeId == 0 cimen vokselinde footWy + 1.0f YASAKTIR (stickToSlope ile kavga edip titreme yaratmasini %100 onler)!
            int footWx = Mathf.FloorToInt(pos.x), footWz = Mathf.FloorToInt(pos.z);
            int footWy = Mathf.FloorToInt(pos.y + 0.16f);
            if (footWy >= 1 && footWy < Stage1InteractivePlayController.MaxEditableVoxelY - 3)
            {
                uint footRaw = ReadWorldVoxelRaw(worldChunks, footWx, footWy, footWz);
                uint aboveFootRaw = ReadWorldVoxelRaw(worldChunks, footWx, footWy + 1, footWz);
                if (IsHardCollisionVoxel(footRaw) &&
                    !IsHardCollisionVoxel(aboveFootRaw) &&
                    !IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, footWx, footWy + 2, footWz)))
                {
                    float exactTopY = GetVoxelWalkableTopY(footRaw, aboveFootRaw, footWy);
                    if (pos.y < exactTopY - 0.005f)
                    {
                        pos.y = exactTopY;
                        if (_verticalVelocity < 0f) _verticalVelocity = 0f;
                        _movementState.IsGrounded = 1;
                        _coyoteGroundedTimer = 0.16f;
                        playerTransform.position = pos;
                    }
                }
            }
        }

        public static bool TryResolveCeilingHeadBonk(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            ref Vector3 pos,
            float capsuleHeightMeters,
            float capsuleRadiusMeters)
        {
            float effH = Mathf.Clamp(capsuleHeightMeters, 0.45f, 1.85f);
            float effR = Mathf.Clamp(capsuleRadiusMeters * 0.80f, 0.16f, 0.28f);
            int crownWy = Mathf.FloorToInt(pos.y + effH + 0.03f);
            int chestWy = Mathf.FloorToInt(pos.y + effH * 0.45f);
            if (crownWy <= chestWy || crownWy < 2 || crownWy >= Stage1InteractivePlayController.MaxEditableVoxelY)
            {
                return false;
            }

            ReadOnlySpan<float> ox = stackalloc float[5] { 0f, effR, -effR, 0f, 0f };
            ReadOnlySpan<float> oz = stackalloc float[5] { 0f, 0f, 0f, effR, -effR };
            for (int i = 0; i < 5; i++)
            {
                int wx = Mathf.FloorToInt(pos.x + ox[i]);
                int wz = Mathf.FloorToInt(pos.z + oz[i]);
                if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, crownWy, wz)) &&
                    !IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, wx, chestWy, wz)))
                {
                    float maxAllowedFeetY = crownWy - effH - 0.025f;
                    if (pos.y > maxAllowedFeetY)
                    {
                        pos.y = Mathf.Max(pos.y - 0.35f, maxAllowedFeetY);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Oyuncunun ziplamadan diz boyu dogal tumsek ve 1 blokluk basamaklara rahatca cikabilecegi maksimum yukseklik (metre).</summary>
        public const float MaxKneeStepUpMeters = 1.08f;

        public static bool IsHorizontalStepBlocked(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 cur,
            Vector3 next,
            float capsuleHeightMeters = 1.8f,
            float capsuleRadiusMeters = 0.34f)
        {
            Vector2 step2D = new Vector2(next.x - cur.x, next.z - cur.z);
            if (step2D.sqrMagnitude < 1e-7f) return false;

            float r = Mathf.Clamp(capsuleRadiusMeters, 0.20f, 0.34f);
            if (SotfStructureCollisionRegistry.IsCapsuleStepBlocked(
                    new System.Numerics.Vector3(cur.x, cur.y, cur.z),
                    new System.Numerics.Vector3(next.x, next.y, next.z),
                    capsuleHeightMeters,
                    r,
                    MaxKneeStepUpMeters))
            {
                return true;
            }

            const float diag = 0.70710678f;
            float d = r * diag;

            float topOffset = Mathf.Clamp(capsuleHeightMeters - 0.12f, 0.45f, 1.68f);
            int minBodyWy = Mathf.Max(1, Mathf.FloorToInt(cur.y + 0.16f));
            int maxBodyWy = Mathf.Min(Stage1InteractivePlayController.MaxEditableVoxelY - 3, Mathf.FloorToInt(cur.y + topOffset));

            // A) Simetrik 3B Trilinear SDF On-Kenar Duvar Korumasi:
            //    SurfaceNets (ShapeId == 0) izo-yuzeyi [wx - 0.50 .. wx + 0.50] araligindadir; FloorToInt tek basina
            //    -X/-Z duvarlarinda 0.50m gecikme yaratir. Ust govde/bas hizasinda SDF yogunlugu rho >= 56.0f duvara girisi simetrik engeller!
            Vector2 stepDir = step2D.normalized;
            float leadR = r * 0.92f;
            float chestY = cur.y + Mathf.Clamp(capsuleHeightMeters * 0.62f, 0.40f, 1.18f);
            float headY = cur.y + topOffset;
            ReadOnlySpan<float> sdfProbeYs = stackalloc float[2] { chestY, headY };
            for (int p = 0; p < 2; p++)
            {
                float py = sdfProbeYs[p];
                float curPx = cur.x + stepDir.x * leadR;
                float curPz = cur.z + stepDir.y * leadR;
                float nextPx = next.x + stepDir.x * leadR;
                float nextPz = next.z + stepDir.y * leadR;

                SampleTrilinearSolidSdfPure(worldChunks, curPx, py, curPz, out float curRho, out _, out _, out _, out _);
                SampleTrilinearSolidSdfPure(worldChunks, nextPx, py, nextPz, out float nextRho, out float gx, out float gy, out float gz, out _);
                if (nextRho >= 56.0f && nextRho > curRho + 0.05f)
                {
                    float horizGrad = Mathf.Sqrt(gx * gx + gz * gz);
                    SampleTrilinearSolidSdfPure(worldChunks, nextPx, cur.y + MaxKneeStepUpMeters + 0.22f, nextPz, out float aboveKneeRho, out _, out _, out _, out _);
                    if (aboveKneeRho >= 48.0f || horizGrad > Mathf.Abs(gy) * 0.85f)
                    {
                        return true;
                    }
                }
            }

            // B) 360° Simetrik 8-Noktali Kapsul Cevre Kontrolu:
            ReadOnlySpan<float> ringX = stackalloc float[8] { r, -r, 0f, 0f, d, -d, d, -d };
            ReadOnlySpan<float> ringZ = stackalloc float[8] { 0f, 0f, r, -r, d, d, -d, -d };

            for (int i = 0; i < 8; i++)
            {
                int nextWx = Mathf.FloorToInt(next.x + ringX[i]);
                int nextWz = Mathf.FloorToInt(next.z + ringZ[i]);
                int curWx = Mathf.FloorToInt(cur.x + ringX[i]);
                int curWz = Mathf.FloorToInt(cur.z + ringZ[i]);

                if (nextWx == curWx && nextWz == curWz) continue;

                int topSolidWy = -1;
                uint topSolidRaw = 0u;
                for (int wy = minBodyWy; wy <= maxBodyWy; wy++)
                {
                    uint nextRaw = ReadWorldVoxelRaw(worldChunks, nextWx, wy, nextWz);
                    if (!IsHardCollisionVoxel(nextRaw)) continue;

                    // DIKKAT: Yalnizca ayni sutunda degilken YENI bir kati sutuna (nextWx, nextWz) gecis yapiliyor!
                    // Eger next sutununda ustu kapali bir duvar (wy + 1 dolu) varsa ASLA atlama; yalnizca acik basamaksa gecise izin ver.
                    bool nextIsMultiBlockWall = IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, nextWx, wy + 1, nextWz));
                    if (!nextIsMultiBlockWall && IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, curWx, wy, curWz)))
                    {
                        continue;
                    }

                    topSolidWy = wy;
                    topSolidRaw = nextRaw;
                }

                if (topSolidWy < 0) continue;

                uint aboveTopRaw = ReadWorldVoxelRaw(worldChunks, nextWx, topSolidWy + 1, nextWz);
                if (IsHardCollisionVoxel(aboveTopRaw))
                {
                    return true;
                }

                float columnStepTopY = GetVoxelWalkableTopY(topSolidRaw, aboveTopRaw, topSolidWy);

                // Sutundaki en ust kati vokselin yuzeyi diz hizasi basamak sinirini (>1.08m) asiyorsa gercek duvar / agac / alcak tavandir!
                if ((columnStepTopY - cur.y) > MaxKneeStepUpMeters)
                {
                    return true;
                }

                // Diz hizasi veya daha alcak tumsek/yamac/basamagin uzerine cikildiginda ustunde kapsul boyu kadar acik hava olmali!
                int headCheckMinWy = topSolidWy + 1;
                int headCheckMaxWy = Mathf.Min(
                    Stage1InteractivePlayController.MaxEditableVoxelY - 2,
                    Mathf.FloorToInt(columnStepTopY + topOffset));
                for (int hy = headCheckMinWy; hy <= headCheckMaxWy; hy++)
                {
                    if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, nextWx, hy, nextWz)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool DetectClimbableLedgeAhead(
            Transform playerTransform,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector2 moveInput,
            out float ledgeTopY)
        {
            Vector3 pos = playerTransform.position;
            ledgeTopY = pos.y;

            // Oldugu yerde, yana veya geri giderken Space basildiginda ASLA tırmanma (Mantle) devreye girip normal ziplamayi ezmez!
            if (moveInput.y <= 0.15f)
            {
                return false;
            }

            // Oyuncunun kendi basinin hemen ustunde alcak tavan varsa yukari tirmanamaz
            if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y + 1.95f), Mathf.FloorToInt(pos.z))))
            {
                return false;
            }

            Vector3 fwd = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            int minWy = Mathf.Max(1, Mathf.FloorToInt(pos.y + 0.95f));
            int maxWy = Mathf.Min(Stage1InteractivePlayController.MaxEditableVoxelY - 4, Mathf.FloorToInt(pos.y + 2.30f));

            for (int stepIdx = 0; stepIdx < 2; stepIdx++)
            {
                float probeDist = stepIdx == 0 ? 0.55f : 0.85f;
                Vector3 probe = pos + fwd * probeDist;
                int px = Mathf.FloorToInt(probe.x);
                int pz = Mathf.FloorToInt(probe.z);

                for (int y = maxWy; y >= minWy; y--)
                {
                    uint raw = ReadWorldVoxelRaw(worldChunks, px, y, pz);
                    if (!IsHardCollisionVoxel(raw)) continue;

                    uint aboveRaw = ReadWorldVoxelRaw(worldChunks, px, y + 1, pz);
                    if (IsHardCollisionVoxel(aboveRaw) ||
                        IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, px, y + 2, pz)))
                    {
                        // Ustunde 2 blok acik hava yoksa tirmanilabilir cikinti degil, yuksek duvar / agac govdesidir
                        return false;
                    }

                    float candidateTopY = GetVoxelWalkableTopY(raw, aboveRaw, y);
                    float backGroundY = SampleBilinearVoxelGroundHeight(
                        worldChunks, pos.x - fwd.x * 0.45f, pos.z - fwd.z * 0.45f, pos.y);
                    float refFootY = (backGroundY > -20f && backGroundY < pos.y && (pos.y - backGroundY) <= 0.60f)
                        ? backGroundY
                        : pos.y;
                    float heightDiff = candidateTopY - refFootY;
                    // MaxKneeStepUpMeters (1.08m) ve altindaki zemin/tumsek/basamaklarda daima normal yuruyus veya tam ziplama calisir;
                    // Mantle yalnizca bel-bas hizasi [1.10m .. 2.25m] cikintilar icindir!
                    if (heightDiff >= 1.10f && heightDiff <= 2.25f)
                    {
                        ledgeTopY = candidateTopY;
                        return true;
                    }
                    break;
                }

                // Ayni ileri mesafede [1.10m .. 2.25m] yukseklikte saglam bir kalin tasiyici agac dali varsa ona da tirmanilabilir (Mantle)!
                OrganicTreeCanopyVisualSystem? treeSys = OrganicTreeCanopyVisualSystem.ActiveInstance;
                if (treeSys != null &&
                    treeSys.TrySampleWalkableBranchSurfaceAt(
                        probe.x,
                        probe.z,
                        pos.y + 1.15f,
                        footRadiusMeters: 0.26f,
                        maxStepUpMeters: 1.15f,
                        out float branchLedgeY,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    float branchHeightDiff = branchLedgeY - pos.y;
                    if (branchHeightDiff >= 1.10f && branchHeightDiff <= 2.25f)
                    {
                        ledgeTopY = branchLedgeY;
                        return true;
                    }
                }
            }

            return false;
        }

        public static float SampleSingleColumnGroundY(
            Dictionary<(int cx, int cz), uint[]> worldChunks, int wx, int wz, int startY)
        {
            int clampedStart = Mathf.Clamp(startY, 1, Stage1InteractivePlayController.MaxEditableVoxelY - 3);
            for (int y = clampedStart; y >= 1; y--)
            {
                uint raw = ReadWorldVoxelRaw(worldChunks, wx, y, wz);
                if (!IsHardCollisionVoxel(raw)) continue;

                // KRITIK FIZIK INVARYANTI (Anti-Asansor):
                // Ustunde acik hava boslugu olmayan (y + 1 dolu olan) hicbir voksel "yurunebilir zemin" degildir;
                // bir magara tavaninin, duvarin veya agac govdesinin ic kesitidir!
                uint aboveRaw = ReadWorldVoxelRaw(worldChunks, wx, y + 1, wz);
                if (IsHardCollisionVoxel(aboveRaw)) continue;

                return GetVoxelWalkableTopY(raw, aboveRaw, y);
            }
            return -30f;
        }

        public static float SampleBilinearVoxelGroundHeight(
            Dictionary<(int cx, int cz), uint[]> worldChunks, float worldX, float worldZ, float refY)
        {
            int centerWx = Mathf.FloorToInt(worldX), centerWz = Mathf.FloorToInt(worldZ);
            int startY = Mathf.Clamp(Mathf.FloorToInt(refY + 1.45f), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 3);

            // Tamamen duz (3x3 komsuda ayni Y ve ayni yogunluklu) SurfaceNets cimen ovalarinda estetik faset titresiminden (+/-1.2 cm)
            // kaynaklanan dikey kamera mikro-salinimini sifirla (0.00000m puruzsuz kosu!):
            if (TrySampleUniformFlatSdfPlaneY(worldChunks, centerWx, centerWz, startY, out float flatPlaneY))
            {
                return flatPlaneY;
            }

            // Analitik 3B SurfaceNets ucgen sorgusu: Gorsel GPU mesh'inin X/Y/Z kenar ucgenlerini ve rampa bloklarini
            // 0.000m farkla yakalar; magara tavanini (ceilingCapY) dislayarak asansor hatasina izin vermez.
            if (VoxelSurfaceNetsGroundSampler.TrySampleSurfaceHeightAndNormal(
                    worldX, worldZ, refY, 1.92f, worldChunks, out float exactCenterY, out Vector3 centerNormal))
            {
                float slopeSoleClearance = centerNormal.y < 0.94f
                    ? Mathf.Clamp((1f - centerNormal.y) * 0.05f, 0f, 0.03f)
                    : 0f;
                return exactCenterY + slopeSoleClearance;
            }

            return SampleSingleColumnGroundY(worldChunks, centerWx, centerWz, startY);
        }

        private static bool TrySampleUniformFlatSdfPlaneY(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            int centerWx,
            int centerWz,
            int startY,
            out float flatPlaneY)
        {
            flatPlaneY = -30f;
            int refTopWy = -1;
            byte refDensity = 0;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int qx = centerWx + dx;
                    int qz = centerWz + dz;
                    int colTop = -1;
                    uint colRaw = 0u;
                    for (int y = startY; y >= 1; y--)
                    {
                        uint raw = ReadWorldVoxelRaw(worldChunks, qx, y, qz);
                        if (!IsHardCollisionVoxel(raw)) continue;
                        if (IsHardCollisionVoxel(ReadWorldVoxelRaw(worldChunks, qx, y + 1, qz))) continue;
                        colTop = y;
                        colRaw = raw;
                        break;
                    }

                    if (colTop < 0 || VoxelBitUtility.GetShapeId(colRaw) != 0)
                    {
                        return false;
                    }

                    byte dens = VoxelBitUtility.GetDensityOrLiquid(colRaw);
                    if (dens < VoxelBitUtility.IsoDensity) return false;

                    if (refTopWy < 0)
                    {
                        refTopWy = colTop;
                        refDensity = dens;
                    }
                    else if (colTop != refTopWy || dens != refDensity)
                    {
                        return false;
                    }
                }
            }

            if (refTopWy < 0) return false;
            float t = Mathf.Clamp((refDensity - VoxelBitUtility.IsoDensity) / Mathf.Max(1f, (float)refDensity), 0.08f, 0.92f);
            flatPlaneY = refTopWy + Mathf.Round(t * 256f) * (1f / 256f);
            return true;
        }

        private bool IsHorizontalStepBlockedWithModular(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 cur,
            Vector3 next,
            float capsuleHeightMeters = 1.8f,
            float capsuleRadiusMeters = 0.34f)
        {
            if (IsHorizontalStepBlocked(worldChunks, cur, next, capsuleHeightMeters, capsuleRadiusMeters))
            {
                return true;
            }

            ReadOnlySpan<ModularPlacedPieceNode> pieces = GetActiveModularPiecesSpan();
            if (pieces.Length == 0) return false;

            return ModularStructureCollisionSolver.IsHorizontalCapsuleStepBlocked(
                pieces,
                new System.Numerics.Vector3(cur.x, cur.y, cur.z),
                new System.Numerics.Vector3(next.x, next.y, next.z),
                capsuleHeightMeters,
                capsuleRadiusMeters,
                MaxKneeStepUpMeters);
        }

        private float SampleSupportGroundHeightWithModular(Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 pos)
        {
            float voxelY = SampleSupportGroundHeight(worldChunks, pos);
            ReadOnlySpan<ModularPlacedPieceNode> pieces = GetActiveModularPiecesSpan();
            if (pieces.Length == 0) return voxelY;

            float modY = ModularStructureCollisionSolver.SampleSupportGroundY(
                pieces, pos.x, pos.z, pos.y, MaxKneeStepUpMeters, footRadiusMeters: 0.24f);
            return modY > voxelY ? modY : voxelY;
        }

        private bool TryResolveCeilingHeadBonkWithModular(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            ref Vector3 pos,
            float capsuleHeightMeters,
            float capsuleRadiusMeters)
        {
            bool voxelBonk = TryResolveCeilingHeadBonk(worldChunks, ref pos, capsuleHeightMeters, capsuleRadiusMeters);
            ReadOnlySpan<ModularPlacedPieceNode> pieces = GetActiveModularPiecesSpan();
            if (pieces.Length == 0) return voxelBonk;

            System.Numerics.Vector3 pNum = new System.Numerics.Vector3(pos.x, pos.y, pos.z);
            bool modBonk = ModularStructureCollisionSolver.TryResolveCeilingHeadBonk(
                pieces, ref pNum, capsuleHeightMeters, capsuleRadiusMeters);
            if (modBonk)
            {
                pos.y = pNum.Y;
            }
            return voxelBonk || modBonk;
        }

        private void MeasureHeadroomClearanceWithModular(
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            Vector3 feetPos,
            float supportGroundY,
            out bool canCrouch115,
            out bool canStand180)
        {
            MeasureHeadroomClearance(worldChunks, feetPos, supportGroundY, out canCrouch115, out canStand180);
            ReadOnlySpan<ModularPlacedPieceNode> pieces = GetActiveModularPiecesSpan();
            if (pieces.Length == 0) return;

            float refY = supportGroundY > -20f ? Mathf.Max(feetPos.y, supportGroundY) : feetPos.y;
            ModularStructureCollisionSolver.EvaluateHeadroomClearance(
                pieces,
                new System.Numerics.Vector3(feetPos.x, refY, feetPos.z),
                out bool modCrouch,
                out bool modStand);
            canCrouch115 &= modCrouch;
            canStand180 &= modStand;
            if (!canCrouch115) canStand180 = false;
        }

        private static float SampleSupportGroundHeight(Dictionary<(int cx, int cz), uint[]> worldChunks, Vector3 pos)
        {
            float structTopY = SotfStructureCollisionRegistry.SampleWalkableTopY(pos.x, pos.y, pos.z, 0.22f, MaxKneeStepUpMeters);
            float bestTerrainY = SampleBilinearVoxelGroundHeight(worldChunks, pos.x, pos.z, pos.y);

            if (bestTerrainY <= -20f)
            {
                // Merkez nokta 1cm duvar sutununa (aboveRaw kati) denk gelip -30f donduyse ayak diskinin 4 acik kenarini tara:
                const float footR = 0.22f;
                int startWy = Mathf.Clamp(Mathf.FloorToInt(pos.y + MaxKneeStepUpMeters), 1, Stage1InteractivePlayController.MaxEditableVoxelY - 3);
                ReadOnlySpan<float> ox = stackalloc float[4] { footR, -footR, 0f, 0f };
                ReadOnlySpan<float> oz = stackalloc float[4] { 0f, 0f, footR, -footR };
                float bestFootY = -30f;
                for (int i = 0; i < 4; i++)
                {
                    float py = SampleSingleColumnGroundY(
                        worldChunks,
                        Mathf.FloorToInt(pos.x + ox[i]),
                        Mathf.FloorToInt(pos.z + oz[i]),
                        startWy);
                    if (py > bestFootY && py <= pos.y + MaxKneeStepUpMeters)
                    {
                        bestFootY = py;
                    }
                }
                if (bestFootY > -20f)
                {
                    bestTerrainY = bestFootY;
                }
            }

            // 3B Organik Agac Dallari uzerinde yurume/basma destegi:
            OrganicTreeCanopyVisualSystem? treeSys = OrganicTreeCanopyVisualSystem.ActiveInstance;
            if (treeSys != null &&
                treeSys.TrySampleWalkableBranchSurfaceAt(
                    pos.x,
                    pos.z,
                    pos.y,
                    footRadiusMeters: 0.22f,
                    maxStepUpMeters: MaxKneeStepUpMeters,
                    out float branchTopY,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                if (branchTopY > bestTerrainY)
                {
                    bestTerrainY = branchTopY;
                }
            }

            if (structTopY > -900f && structTopY > bestTerrainY)
            {
                bestTerrainY = structTopY;
            }

            if (bestTerrainY > -20f)
            {
                return bestTerrainY;
            }

            int terrainMask = LayerMask.GetMask("VoxelTerrain");
            int castMask = terrainMask != 0 ? terrainMask : UnityEngine.Physics.DefaultRaycastLayers;

            if (UnityEngine.Physics.Raycast(new Vector3(pos.x, pos.y + 0.95f, pos.z), Vector3.down,
                out RaycastHit hit, 16f, castMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }
            return bestTerrainY;
        }

        private void RescueFromVoid(Transform playerTransform, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            int sx = Mathf.FloorToInt(_safeSpawnPosition.x), sz = Mathf.FloorToInt(_safeSpawnPosition.z);
            float rescueY = _safeSpawnPosition.y;
            for (int y = Stage1InteractivePlayController.MaxEditableVoxelY - 2; y >= 1; y--)
            {
                uint raw = ReadWorldVoxelRaw(worldChunks, sx, y, sz);
                if (VoxelBitUtility.IsPhysicallySolid(raw))
                {
                    rescueY = GetVoxelWalkableTopY(raw, ReadWorldVoxelRaw(worldChunks, sx, y + 1, sz), y) + 0.05f;
                    break;
                }
            }
            playerTransform.position = new Vector3(_safeSpawnPosition.x, rescueY, _safeSpawnPosition.z);
            _verticalVelocity = 0f;
            _movementState.IsGrounded = 1;
        }
#endif
    }
}
