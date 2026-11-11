#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Math;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 2.7.D.1 — Clamped SDF Kiyi Gozenekliligi (Porosity) ve
    /// ShapeID == 63 Mikro-Voxel Su Olugu / Boru Kapasitesi Hesaplayicisi:
    /// - Hava hucresi: V_max = 255
    /// - Demir Izgara (IronGrating = 125): V_max = 224 (su ve oksijen icinden gecer!)
    /// - Dogal SDF Kiyi Hucresi (ShapeID == 0, Density &lt; 96): Egimli SDF arazi kiyisinda suyun
    ///    yuzey altina girmesini saglayan kismi gozenek kapasitesi!
    /// - Yontulmus Mikro-Voxel (ShapeID == 63): (64 - PopCount(mask64)) * 255 / 64 ile 0.25m su arklarina izin verir!
    /// </summary>
    public static class FluidPorosityUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ComputeCellFluidCapacity(uint solidVoxelRaw, ulong optionalMicroMask64 = ulong.MaxValue)
        {
            byte matId = VoxelBitUtility.GetMaterialId(solidVoxelRaw);
            if (matId == MaterialIdCatalog.Air)
            {
                return 255;
            }

            if (matId == MaterialIdCatalog.IronGrating)
            {
                return 224;
            }

            byte shapeId = VoxelBitUtility.GetShapeId(solidVoxelRaw);
            if (shapeId == VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                int filledSubVoxels = BitOps.PopCount(optionalMicroMask64);
                int openSubVoxels = Math.Clamp(64 - filledSubVoxels, 0, 64);
                return (byte)((openSubVoxels * 255) / 64);
            }

            if (shapeId == 0)
            {
                byte density = VoxelBitUtility.GetDensityOrLiquid(solidVoxelRaw);
                if (density < StratocraftConstants.SdfIsoLevel)
                {
                    // Hava tarafindaki dar-bant SDF hucresi tam akiskan alabilir
                    return 255;
                }

                // SurfaceNets kati arazi vokselleri (Density >= 64) gecirimsiz tabandir;
                // su kati vokselin icine bolunmez, ustundeki hucrede alt-voksel taban kotu b(x,y,z) uzerine oturur.
                return 0;
            }

            // Mimari yarim levha / kiris sekillerinde kismi bosluk kapasitesi, tam kup (1) ise 0
            return shapeId switch
            {
                ArchitecturalShapeCatalog.HalfSlab => 128,
                ArchitecturalShapeCatalog.QuarterBeam => 192,
                ArchitecturalShapeCatalog.Ramp22Low => 160,
                ArchitecturalShapeCatalog.Ramp45 => 128,
                _ => 0
            };
        }

        /// <summary>
        /// Bir (x, y, z) sivi hucresinin oturdugu alt-voksel taban yuksekligini (b) [0..255] sabit noktali
        /// kutle esdegeri biriminde (255 birim = 1.0 metre) hesaplar.
        /// - Eger alt hucre (y - 1) bosluksa (selale / yamaç dususu): (y - 1) * 255 doner (kuvvetli yokus asagi cekim!).
        /// - Eger alt hucre (y - 1) ShapeId == 0 dogal SurfaceNets arazisiyse: izo-yuzey yuksekligi
        ///   b = (y - 1) + Clamp((d_below - 64) / (d_below - d_curr), 0.08, 0.92) metre olarak [0..255] olceginde doner.
        /// - Eger alt hucre tam mimari bloksa (ShapeId != 0): y * 255 doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeCellBedElevationUnits255(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            byte curMat = VoxelBitUtility.GetMaterialId(currentSolidRaw);
            byte curShape = VoxelBitUtility.GetShapeId(currentSolidRaw);
            if (curMat != MaterialIdCatalog.Air && curShape != 0 && curShape != VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                byte cap = ComputeCellFluidCapacity(currentSolidRaw);
                if (cap > 0 && cap < 255)
                {
                    return y * 255 + (255 - cap);
                }
            }

            if (y <= 0)
            {
                return 0;
            }

            if (!VoxelBitUtility.IsPhysicallySolid(belowSolidRaw))
            {
                // Alt hucre fiziksel olarak kati degil (yamaç kenari / bosluk): su buraya dogru hizla akar
                return (y - 1) * 255;
            }

            byte belowShape = VoxelBitUtility.GetShapeId(belowSolidRaw);
            if (belowShape == 0)
            {
                int dBelow = VoxelBitUtility.GetDensityOrLiquid(belowSolidRaw);
                int dCurr = (curMat != MaterialIdCatalog.Air && curShape == 0)
                    ? VoxelBitUtility.GetDensityOrLiquid(currentSolidRaw)
                    : 0;
                int denom = Math.Max(1, dBelow - dCurr);
                int isoOffset255 = Math.Clamp(((dBelow - StratocraftConstants.SdfIsoLevel) * 255) / denom, 20, 235);
                return (y - 1) * 255 + isoOffset255;
            }

            return y * 255;
        }

        /// <summary>
        /// Mesh uretimi icin (x, y, z) sutununda SurfaceNets arazi yuzeyinin tam metre cinsinden yuksekligini doner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeColumnSurfaceNetsBedMeters(uint belowSolidRaw, uint currentSolidRaw, int y)
        {
            return ComputeCellBedElevationUnits255(belowSolidRaw, currentSolidRaw, y) * (1.0f / 255.0f);
        }

        /// <summary>
        /// Bir hucreye kati blok konuldugunda veya Madde Kristali ile SDF kutlesi eklendiginde,
        /// azalan V_max kapasitesini asan siviyi yok etmek yerine komsu acik hucrelere (+Y, +-X, +-Z) koruyarak iter!
        /// </summary>
        public static int DisplaceExcessFluidOnSolidEdit(
            ReadOnlySpan<uint> solidVoxels,
            Span<ushort> fluids,
            int sizeX,
            int sizeY,
            int sizeZ,
            int editX,
            int editY,
            int editZ)
        {
            int strideY = sizeZ * sizeX;
            int idx = editY * strideY + editZ * sizeX + editX;
            FluidCell16 cell = new FluidCell16(fluids[idx]);
            if (cell.Mass == 0 || cell.Type == FluidType.None)
            {
                return 0;
            }

            byte newCap = ComputeCellFluidCapacity(solidVoxels[idx]);
            if (cell.Mass <= newCap)
            {
                fluids[idx] = FluidCell16.WakeRaw(fluids[idx]);
                return 0;
            }

            int excess = cell.Mass - newCap;
            FluidType fType = cell.Type;
            fluids[idx] = newCap > 0 ? FluidCell16.Pack(fType, newCap, isSettled: false).RawValue : (ushort)0;

            // Oncelikle yukari (+Y), sonra 4 yatay komsuya, en son asagiya (-Y) hacim korunumlu dagit
            ReadOnlySpan<int> dx = stackalloc int[6] { 0, 1, -1, 0, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[6] { 1, 0, 0, 0, 0, -1 };
            ReadOnlySpan<int> dz = stackalloc int[6] { 0, 0, 0, 1, -1, 0 };

            int displaced = 0;
            for (int d = 0; d < 6 && excess > 0; d++)
            {
                int nx = editX + dx[d];
                int ny = editY + dy[d];
                int nz = editZ + dz[d];
                if ((uint)nx >= (uint)sizeX || (uint)ny >= (uint)sizeY || (uint)nz >= (uint)sizeZ)
                {
                    continue;
                }

                int nIdx = ny * strideY + nz * sizeX + nx;
                byte nCap = ComputeCellFluidCapacity(solidVoxels[nIdx]);
                if (nCap == 0)
                {
                    continue;
                }

                FluidCell16 nCell = new FluidCell16(fluids[nIdx]);
                if (nCell.Type != FluidType.None && nCell.Type != fType)
                {
                    continue;
                }

                int space = nCap - nCell.Mass;
                if (space <= 0)
                {
                    continue;
                }

                int push = Math.Min(excess, space);
                fluids[nIdx] = FluidCell16.Pack(fType, (byte)(nCell.Mass + push), isSettled: false).RawValue;
                excess -= push;
                displaced += push;
            }

            return displaced;
        }

        /// <summary>
        /// Dik (85 deg), capraz (45 deg) ve sig/yatay (10 deg) tum bakis acilarinda su/lav matarasinin
        /// dokecegi gecerli 3B voksel hucresini cozumler. SurfaceNets egimli zeminde DDA yan yuz carpmasi
        /// nedeniyle dolu komsuya dusup sessizce iptal olma hatasini %100 ortadan kaldirir.
        /// </summary>
        public static bool TryResolveFluidPourCell(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            int maxY,
            FluidType pourType,
            int aimX,
            int aimY,
            int aimZ,
            int placeX,
            int placeY,
            int placeZ,
            float hitNormalX,
            float hitNormalY,
            float hitNormalZ,
            bool hasMeshSurfaceHit,
            float meshSurfacePointX,
            float meshSurfacePointY,
            float meshSurfacePointZ,
            out int outPourX,
            out int outPourY,
            out int outPourZ)
        {
            int safeMaxY = Math.Max(2, maxY - 2);

            // 1. Eger gercek SurfaceNets MeshCollider yuzey noktasi varsa, yuzey normali yonunde +0.22m ustteki hucreyi dene
            if (hasMeshSurfaceHit)
            {
                int mx = FloorToIntPure(meshSurfacePointX + hitNormalX * 0.22f);
                int my = Math.Clamp(FloorToIntPure(meshSurfacePointY + MathF.Max(0.18f, hitNormalY * 0.28f)), 1, safeMaxY);
                int mz = FloorToIntPure(meshSurfacePointZ + hitNormalZ * 0.22f);
                if (IsCellValidForPourOrReaction(worldChunks, fluidChunks, mx, my, mz, pourType))
                {
                    outPourX = mx; outPourY = my; outPourZ = mz;
                    return true;
                }
                if (my + 1 <= safeMaxY && IsCellValidForPourOrReaction(worldChunks, fluidChunks, mx, my + 1, mz, pourType))
                {
                    outPourX = mx; outPourY = my + 1; outPourZ = mz;
                    return true;
                }
            }

            // 2. Vurulan vokselin kendisi yari-gecirgen (SDF kiyi / ince cimen) ise dogrudan icine dok
            int clampedAimY = Math.Clamp(aimY, 1, safeMaxY);
            if (TryReadWorldCell(worldChunks, fluidChunks, aimX, clampedAimY, aimZ, out uint aimSolid, out ushort aimFluid))
            {
                byte aimCap = ComputeCellFluidCapacity(aimSolid);
                if (aimSolid != 0u && aimCap >= 96 && CanAcceptPourInCell(aimFluid, aimCap, pourType))
                {
                    outPourX = aimX; outPourY = clampedAimY; outPourZ = aimZ;
                    return true;
                }

                // 3. Vurulan voksel dogal arazi (ShapeId == 0) ise ve sig acidan yanina carpildiysa (hitNormalY < 0.5),
                //    oyuncunun gercekte baktigi yer o arazi kolonunun hemen ustu (aimY + 1) dir!
                bool isNaturalTerrain = VoxelBitUtility.IsPhysicallySolid(aimSolid) && VoxelBitUtility.GetShapeId(aimSolid) == 0;
                if (isNaturalTerrain && hitNormalY >= -0.25f && clampedAimY + 1 <= safeMaxY)
                {
                    if (IsCellValidForPourOrReaction(worldChunks, fluidChunks, aimX, clampedAimY + 1, aimZ, pourType))
                    {
                        outPourX = aimX; outPourY = clampedAimY + 1; outPourZ = aimZ;
                        return true;
                    }
                }
            }

            // 4. Standart DDA yerlestirme hucresi (PlacementVoxelCoord)
            int clampedPlaceY = Math.Clamp(placeY, 1, safeMaxY);
            if (IsCellValidForPourOrReaction(worldChunks, fluidChunks, placeX, clampedPlaceY, placeZ, pourType))
            {
                outPourX = placeX; outPourY = clampedPlaceY; outPourZ = placeZ;
                return true;
            }

            // 5. Eger hedef hucre doluysa (veya zaten 255 birim ayni siviyla agzina kadar doluysa):
            //    once (aimX, aimY + 1, aimZ), sonra (placeX, placeY + 1, placeZ), ardindan 4 yatay acik komsuyu dene!
            if (clampedAimY + 1 <= safeMaxY &&
                IsCellValidForPourOrReaction(worldChunks, fluidChunks, aimX, clampedAimY + 1, aimZ, pourType))
            {
                outPourX = aimX; outPourY = clampedAimY + 1; outPourZ = aimZ;
                return true;
            }

            if (clampedPlaceY + 1 <= safeMaxY &&
                IsCellValidForPourOrReaction(worldChunks, fluidChunks, placeX, clampedPlaceY + 1, placeZ, pourType))
            {
                outPourX = placeX; outPourY = clampedPlaceY + 1; outPourZ = placeZ;
                return true;
            }

            ReadOnlySpan<int> hDx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> hDz = stackalloc int[4] { 0, 0, 1, -1 };
            for (int i = 0; i < 4; i++)
            {
                int nx = placeX + hDx[i];
                int nz = placeZ + hDz[i];
                if (IsCellValidForPourOrReaction(worldChunks, fluidChunks, nx, clampedPlaceY, nz, pourType))
                {
                    outPourX = nx; outPourY = clampedPlaceY; outPourZ = nz;
                    return true;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                int nx = aimX + hDx[i];
                int nz = aimZ + hDz[i];
                int ny = Math.Min(safeMaxY, clampedAimY + 1);
                if (IsCellValidForPourOrReaction(worldChunks, fluidChunks, nx, ny, nz, pourType))
                {
                    outPourX = nx; outPourY = ny; outPourZ = nz;
                    return true;
                }
            }

            outPourX = placeX;
            outPourY = clampedPlaceY;
            outPourZ = placeZ;
            return false;
        }

        /// <summary>
        /// Tek bir matara (varsayilan 255 birim = 1 tam voksel) butcesini ASLA asmadan secilen hedef hucreye
        /// kontrollu su/lav doker. Eger hedef hucre kismen doluysa, yalnizca kalan butceyi acik yatay komsulara
        /// veya dar cukurda hemen ust hucreye (+Y) aktarir.
        /// </summary>
        public static int ApplySingleFlaskFluidPour(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int wx,
            int wy,
            int wz,
            int maxY,
            FluidType pourType,
            int maxPourUnits = 255)
        {
            if (maxPourUnits <= 0)
            {
                return 0;
            }

            int cx = FloorDiv16Pure(wx);
            int cz = FloorDiv16Pure(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? centerFluids) || centerFluids == null ||
                !worldChunks.TryGetValue((cx, cz), out uint[]? centerSolids) || centerSolids == null)
            {
                return 0;
            }

            int idx = (wy << 8) | (lz << 4) | lx;
            if ((uint)idx >= (uint)centerFluids.Length || (uint)idx >= (uint)centerSolids.Length)
            {
                return 0;
            }

            byte centerCap = ComputeCellFluidCapacity(centerSolids[idx]);
            if (centerCap == 0)
            {
                return 0;
            }

            int remaining = maxPourUnits;
            int addedTotal = 0;

            FluidCell16 prevCenter = new FluidCell16(centerFluids[idx]);
            if (prevCenter.Type == FluidType.None || prevCenter.Mass == 0 || prevCenter.Type == pourType)
            {
                int centerSpace = Math.Max(0, centerCap - prevCenter.Mass);
                int centerAdd = Math.Min(remaining, centerSpace);
                if (centerAdd > 0)
                {
                    byte newMass = (byte)(prevCenter.Mass + centerAdd);
                    centerFluids[idx] = FluidCell16.Pack(pourType, newMass, isSettled: false, pressureHead: 1).RawValue;
                    dirtyFluidChunks?.Add((cx, cz));
                    remaining -= centerAdd;
                    addedTotal += centerAdd;
                }
            }
            else
            {
                // Zit sivi (Su + Lav) uzerine dokulduyse reaksiyon icin hedefi guncelle
                byte pourMass = (byte)Math.Min((int)centerCap, remaining);
                centerFluids[idx] = FluidCell16.Pack(pourType, pourMass, isSettled: false, pressureHead: 1).RawValue;
                dirtyFluidChunks?.Add((cx, cz));
                return pourMass;
            }

            if (remaining <= 0)
            {
                return addedTotal;
            }

            // Kalan matara butcesini once 4 yatay komsuya, dar cukurdaysa hemen ust hucreye (+Y) dagit
            ReadOnlySpan<int> dx = stackalloc int[5] { 1, -1, 0, 0, 0 };
            ReadOnlySpan<int> dy = stackalloc int[5] { 0, 0, 0, 0, 1 };
            ReadOnlySpan<int> dz = stackalloc int[5] { 0, 0, 1, -1, 0 };
            int safeMaxY = Math.Max(2, maxY - 2);

            for (int i = 0; i < 5 && remaining > 0; i++)
            {
                int nwy = wy + dy[i];
                if (nwy < 1 || nwy > safeMaxY)
                {
                    continue;
                }

                int nwx = wx + dx[i];
                int nwz = wz + dz[i];
                int ncx = FloorDiv16Pure(nwx);
                int ncz = FloorDiv16Pure(nwz);
                int nlx = nwx - ncx * 16;
                int nlz = nwz - ncz * 16;

                if (!fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nFluids) || nFluids == null ||
                    !worldChunks.TryGetValue((ncx, ncz), out uint[]? nSolids) || nSolids == null)
                {
                    continue;
                }

                int nIdx = (nwy << 8) | (nlz << 4) | nlx;
                if ((uint)nIdx >= (uint)nFluids.Length || (uint)nIdx >= (uint)nSolids.Length)
                {
                    continue;
                }

                byte nCap = ComputeCellFluidCapacity(nSolids[nIdx]);
                if (nCap == 0)
                {
                    continue;
                }

                FluidCell16 nExisting = new FluidCell16(nFluids[nIdx]);
                if (nExisting.Type != FluidType.None && nExisting.Mass > 0 && nExisting.Type != pourType)
                {
                    continue;
                }

                int space = nCap - nExisting.Mass;
                if (space <= 0)
                {
                    continue;
                }

                int add = Math.Min(remaining, space);
                nFluids[nIdx] = FluidCell16.Pack(pourType, (byte)(nExisting.Mass + add), isSettled: false, pressureHead: 1).RawValue;
                dirtyFluidChunks?.Add((ncx, ncz));
                remaining -= add;
                addedTotal += add;
            }

            return addedTotal;
        }

        /// <summary>
        /// Kademeli sivi kaplari (Matara=380, Kova=1275, Kazan=3825) icin tek hucrede asiri basinc kulesi
        /// olusturmadan, once yumusak bir baslangic kubbesiyle cevreye hafifce yayip dar cukurlarda yukari (+Y)
        /// dolduran kutle-korunumlu dokus metodu. Dondurulen deger gercekte dunyaya aktarilan birim sayisidir.
        /// </summary>
        public static int ApplyContainerFluidPour(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int wx,
            int wy,
            int wz,
            int maxY,
            FluidType pourType,
            int requestedUnits)
        {
            if (requestedUnits <= 0 || pourType == FluidType.None) return 0;

            int safeMaxY = Math.Max(2, maxY - 2);
            wy = Math.Clamp(wy, 1, safeMaxY);

            if (!TryReadWorldCell(worldChunks, fluidChunks, wx, wy, wz, out uint centerSolid, out ushort centerFluidRaw))
            {
                return 0;
            }

            byte centerCap = ComputeCellFluidCapacity(centerSolid);
            if (centerCap == 0) return 0;

            FluidCell16 prevCenter = new FluidCell16(centerFluidRaw);
            if (prevCenter.Type != FluidType.None && prevCenter.Mass > 0 && prevCenter.Type != pourType)
            {
                int reactAdd = Math.Min(requestedUnits, (int)centerCap);
                WriteWorldFluidCell(fluidChunks, dirtyFluidChunks, wx, wy, wz, FluidCell16.Pack(pourType, (byte)reactAdd, isSettled: false, pressureHead: 1).RawValue);
                return reactAdd;
            }

            int remaining = requestedUnits;
            int addedTotal = 0;

            // 1. Asama: Acik arazide tek hucrede 255'lik sel kulesi patlamasini onlemek icin ilk geciste yumusak kubbe tavanlari uygula;
            // eger etraf kapali bir cukursa 2. Asama kalan butceyi tam kapasiteye (255) ve ust katmanlara (+Y) tamamlar!
            int maxRadius = requestedUnits > 1600 ? 2 : 1;
            int maxVerticalLayers = requestedUnits > 1600 ? 3 : 2;

            // Yumusak ilk yayilim gecisi (ayni veya daha alcak tabanli acik komsulara hafif kubbe dagilimi)
            int centerBed = SampleWorldBedElevation(worldChunks, wx, wy, wz);
            int softCenterTarget = requestedUnits <= 420 ? 168 : 220;
            addedTotal += TryAddFluidToWorldCell(worldChunks, fluidChunks, dirtyFluidChunks, wx, wy, wz, pourType, softCenterTarget, ref remaining);

            ReadOnlySpan<int> ring1Dx = stackalloc int[8] { 1, -1, 0, 0, 1, 1, -1, -1 };
            ReadOnlySpan<int> ring1Dz = stackalloc int[8] { 0, 0, 1, -1, 1, -1, 1, -1 };

            for (int r = 0; r < 8 && remaining > 0; r++)
            {
                if (r >= 4 && requestedUnits <= 420) break;
                int nwx = wx + ring1Dx[r];
                int nwz = wz + ring1Dz[r];
                // Eger komsu hucre kati duvarsa atla; yokus yukariysa sadece cok hafif islat, yokus asagiysa daha cok pay ver
                int nwy = ResolvePourNeighborY(worldChunks, nwx, wy, nwz, safeMaxY);
                if (nwy < 1) continue;
                int nBed = SampleWorldBedElevation(worldChunks, nwx, nwy, nwz);
                if (nBed > centerBed + 90) continue;

                int softCap = nBed <= centerBed
                    ? (r < 4 ? 140 : 96)
                    : (r < 4 ? 64 : 36);
                addedTotal += TryAddFluidToWorldCell(worldChunks, fluidChunks, dirtyFluidChunks, nwx, nwy, nwz, pourType, softCap, ref remaining);
            }

            // 2. Asama: Kalan su butcesini (ozellikle Kova=1275 ve Kazan=3825 veya kapali cukurlarda) katman katman tam doldur
            for (int dy = 0; dy < maxVerticalLayers && remaining > 0; dy++)
            {
                int layerY = wy + dy;
                if (layerY > safeMaxY) break;

                addedTotal += TryAddFluidToWorldCell(worldChunks, fluidChunks, dirtyFluidChunks, wx, layerY, wz, pourType, 255, ref remaining);

                for (int rad = 1; rad <= maxRadius && remaining > 0; rad++)
                {
                    for (int dz = -rad; dz <= rad && remaining > 0; dz++)
                    {
                        for (int dx = -rad; dx <= rad && remaining > 0; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != rad) continue;
                            int nwx = wx + dx;
                            int nwz = wz + dz;
                            int targetY = dy == 0 ? ResolvePourNeighborY(worldChunks, nwx, layerY, nwz, safeMaxY) : layerY;
                            if (targetY < 1) continue;
                            addedTotal += TryAddFluidToWorldCell(worldChunks, fluidChunks, dirtyFluidChunks, nwx, targetY, nwz, pourType, 255, ref remaining);
                        }
                    }
                }
            }

            return addedTotal;
        }

        /// <summary>
        /// Bos veya kismen dolu sivi kabina (Matara / Kova / Kazan) hedef havzadan kutle-korunumlu sivi ceker.
        /// Havzada ne kadar sivi varsa o kadar ceker (hicbir zaman yoktan sivi uretmez!).
        /// </summary>
        public static int ScoopFluidIntoContainer(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int aimWx,
            int aimWy,
            int aimWz,
            int maxY,
            FluidType requiredOrNoneType,
            int maxUnitsNeeded,
            out FluidType scoopedType)
        {
            scoopedType = requiredOrNoneType;
            if (maxUnitsNeeded <= 0) return 0;

            int safeMaxY = Math.Max(2, maxY - 2);
            int remainingNeed = maxUnitsNeeded;
            int totalScooped = 0;
            int searchRadius = maxUnitsNeeded > 1500 ? 3 : 2;

            for (int rad = 0; rad <= searchRadius && remainingNeed > 0; rad++)
            {
                for (int dy = 1; dy >= -2 && remainingNeed > 0; dy--)
                {
                    int wy = Math.Clamp(aimWy + dy, 1, safeMaxY);
                    for (int dz = -rad; dz <= rad && remainingNeed > 0; dz++)
                    {
                        for (int dx = -rad; dx <= rad && remainingNeed > 0; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != rad) continue;
                            int wx = aimWx + dx;
                            int wz = aimWz + dz;

                            int cx = FloorDiv16Pure(wx);
                            int cz = FloorDiv16Pure(wz);
                            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null) continue;
                            int lx = wx - cx * 16;
                            int lz = wz - cz * 16;
                            int idx = (wy << 8) | (lz << 4) | lx;
                            if ((uint)idx >= (uint)fluids.Length) continue;

                            FluidCell16 cell = new FluidCell16(fluids[idx]);
                            if (cell.Type == FluidType.None || cell.Mass == 0) continue;
                            if (scoopedType == FluidType.None)
                            {
                                if (cell.Mass < 12) continue;
                                scoopedType = cell.Type;
                            }
                            else if (cell.Type != scoopedType)
                            {
                                continue;
                            }

                            int take = Math.Min((int)cell.Mass, remainingNeed);
                            int left = cell.Mass - take;
                            fluids[idx] = left <= 0
                                ? (ushort)0
                                : FluidCell16.Pack(cell.Type, (byte)left, isSettled: false, pressureHead: 0).RawValue;

                            dirtyFluidChunks?.Add((cx, cz));
                            WakeWorldNeighborsSimple(fluidChunks, dirtyFluidChunks, wx, wy, wz, safeMaxY);
                            remainingNeed -= take;
                            totalScooped += take;
                        }
                    }
                }
            }

            return totalScooped;
        }

        private static int ResolvePourNeighborY(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            int wx, int baseWy, int wz, int safeMaxY)
        {
            if (TryReadWorldSolidOnly(worldChunks, wx, baseWy, wz, out uint curSolid) && ComputeCellFluidCapacity(curSolid) >= 48)
            {
                if (baseWy > 1 && TryReadWorldSolidOnly(worldChunks, wx, baseWy - 1, wz, out uint belowSolid) && ComputeCellFluidCapacity(belowSolid) >= 128)
                {
                    return baseWy - 1;
                }
                return baseWy;
            }
            if (baseWy + 1 <= safeMaxY && TryReadWorldSolidOnly(worldChunks, wx, baseWy + 1, wz, out uint upSolid) && ComputeCellFluidCapacity(upSolid) >= 64)
            {
                return baseWy + 1;
            }
            return -1;
        }

        private static int SampleWorldBedElevation(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            int wx, int wy, int wz)
        {
            TryReadWorldSolidOnly(worldChunks, wx, wy, wz, out uint curSolid);
            uint belowSolid = 0u;
            if (wy > 0) TryReadWorldSolidOnly(worldChunks, wx, wy - 1, wz, out belowSolid);
            return ComputeCellBedElevationUnits255(belowSolid, curSolid, wy);
        }

        private static int TryAddFluidToWorldCell(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int wx, int wy, int wz,
            FluidType pourType,
            int targetMaxInCell,
            ref int remainingBudget)
        {
            if (remainingBudget <= 0) return 0;
            int cx = FloorDiv16Pure(wx);
            int cz = FloorDiv16Pure(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null ||
                !worldChunks.TryGetValue((cx, cz), out uint[]? solids) || solids == null)
            {
                return 0;
            }

            int idx = (wy << 8) | (lz << 4) | lx;
            if ((uint)idx >= (uint)fluids.Length || (uint)idx >= (uint)solids.Length) return 0;

            byte cap = ComputeCellFluidCapacity(solids[idx]);
            if (cap == 0) return 0;

            FluidCell16 existing = new FluidCell16(fluids[idx]);
            if (existing.Type != FluidType.None && existing.Mass > 0 && existing.Type != pourType) return 0;

            int effectiveCap = Math.Min((int)cap, targetMaxInCell);
            int space = effectiveCap - existing.Mass;
            if (space <= 0) return 0;

            int add = Math.Min(remainingBudget, space);
            fluids[idx] = FluidCell16.Pack(pourType, (byte)(existing.Mass + add), isSettled: false, pressureHead: 1).RawValue;
            dirtyFluidChunks?.Add((cx, cz));
            remainingBudget -= add;
            return add;
        }

        private static void WriteWorldFluidCell(
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int wx, int wy, int wz, ushort raw)
        {
            int cx = FloorDiv16Pure(wx);
            int cz = FloorDiv16Pure(wz);
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null) return;
            int idx = (wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16);
            if ((uint)idx < (uint)fluids.Length)
            {
                fluids[idx] = raw;
                dirtyFluidChunks?.Add((cx, cz));
            }
        }

        private static void WakeWorldNeighborsSimple(
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int wx, int wy, int wz, int safeMaxY)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = wy + dy;
                if (ny < 1 || ny > safeMaxY) continue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nwx = wx + dx, nwz = wz + dz;
                        int ncx = FloorDiv16Pure(nwx), ncz = FloorDiv16Pure(nwz);
                        if (!fluidChunks.TryGetValue((ncx, ncz), out ushort[]? fl) || fl == null) continue;
                        int idx = (ny << 8) | ((nwz - ncz * 16) << 4) | (nwx - ncx * 16);
                        if ((uint)idx < (uint)fl.Length && fl[idx] != 0)
                        {
                            fl[idx] = FluidCell16.WakeRaw(fl[idx]);
                            dirtyFluidChunks?.Add((ncx, ncz));
                        }
                    }
                }
            }
        }

        private static bool TryReadWorldSolidOnly(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            int wx, int wy, int wz, out uint solidRaw)
        {
            solidRaw = 0u;
            int cx = FloorDiv16Pure(wx);
            int cz = FloorDiv16Pure(wz);
            int idx = (wy << 8) | ((wz - cz * 16) << 4) | (wx - cx * 16);
            if (!worldChunks.TryGetValue((cx, cz), out uint[]? solids) || solids == null || (uint)idx >= (uint)solids.Length)
            {
                return false;
            }
            solidRaw = solids[idx];
            return true;
        }

        /// <summary>
        /// Secilen hedef hucreye tam dolu (255) kaynak hucresi yerlestirir ve etrafindaki acik yatay komsulara
        /// baslangic 3B kohezyon beslemesi (~84 birim) uygulayarak suyun ilk karede kagit gibi degil dolgun 3B kutle
        /// olarak dogmasini saglar. Dondurulen deger eklenen toplam sivi birimidir.
        /// </summary>
        public static int ApplyCohesiveFluidPour(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            Dictionary<(int cx, int cz), ushort[]> fluidChunks,
            HashSet<(int cx, int cz)>? dirtyFluidChunks,
            int wx,
            int wy,
            int wz,
            int maxY,
            FluidType pourType)
        {
            int cx = FloorDiv16Pure(wx);
            int cz = FloorDiv16Pure(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            if (!fluidChunks.TryGetValue((cx, cz), out ushort[]? centerFluids) || centerFluids == null ||
                !worldChunks.TryGetValue((cx, cz), out uint[]? centerSolids) || centerSolids == null)
            {
                return 0;
            }

            int idx = (wy << 8) | (lz << 4) | lx;
            if ((uint)idx >= (uint)centerFluids.Length || (uint)idx >= (uint)centerSolids.Length)
            {
                return 0;
            }

            byte centerCap = ComputeCellFluidCapacity(centerSolids[idx]);
            if (centerCap == 0)
            {
                return 0;
            }

            FluidCell16 prevCenter = new FluidCell16(centerFluids[idx]);
            int addedTotal = Math.Max(0, centerCap - prevCenter.Mass);
            centerFluids[idx] = FluidCell16.Pack(pourType, centerCap, isSettled: false, pressureHead: 3).RawValue;
            dirtyFluidChunks?.Add((cx, cz));

            // Acik 4 yatay komsuya kohezyon beslemesi (su icin +84 birim, lav icin +48 birim):
            // boylece 1 blok su dokuldugunde aninda dolgun 3B su kutlesi olusur ve yamactan gozle gorulur akar.
            byte sideFeedTarget = pourType == FluidType.Lava ? (byte)48 : (byte)84;
            ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };

            for (int i = 0; i < 4; i++)
            {
                int nwx = wx + dx[i];
                int nwz = wz + dz[i];
                int ncx = FloorDiv16Pure(nwx);
                int ncz = FloorDiv16Pure(nwz);
                int nlx = nwx - ncx * 16;
                int nlz = nwz - ncz * 16;

                if (!fluidChunks.TryGetValue((ncx, ncz), out ushort[]? nFluids) || nFluids == null ||
                    !worldChunks.TryGetValue((ncx, ncz), out uint[]? nSolids) || nSolids == null)
                {
                    continue;
                }

                int nIdx = (wy << 8) | (nlz << 4) | nlx;
                if ((uint)nIdx >= (uint)nFluids.Length || (uint)nIdx >= (uint)nSolids.Length)
                {
                    continue;
                }

                byte nCap = ComputeCellFluidCapacity(nSolids[nIdx]);
                if (nCap < 96)
                {
                    continue;
                }

                FluidCell16 nExisting = new FluidCell16(nFluids[nIdx]);
                if (nExisting.Type != FluidType.None && nExisting.Type != pourType)
                {
                    continue;
                }

                byte targetMass = Math.Min(nCap, sideFeedTarget);
                if (nExisting.Mass < targetMass)
                {
                    addedTotal += (targetMass - nExisting.Mass);
                    nFluids[nIdx] = FluidCell16.Pack(pourType, targetMass, isSettled: false, pressureHead: 1).RawValue;
                    dirtyFluidChunks?.Add((ncx, ncz));
                }
            }

            return addedTotal;
        }

        private static bool IsCellValidForPourOrReaction(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            int wx,
            int wy,
            int wz,
            FluidType pourType)
        {
            if (!TryReadWorldCell(worldChunks, fluidChunks, wx, wy, wz, out uint solidRaw, out ushort fluidRaw))
            {
                return false;
            }

            byte cap = ComputeCellFluidCapacity(solidRaw);
            if (cap == 0)
            {
                return false;
            }

            return CanAcceptPourInCell(fluidRaw, cap, pourType);
        }

        private static bool CanAcceptPourInCell(ushort fluidRaw, byte cap, FluidType pourType)
        {
            FluidCell16 existing = new FluidCell16(fluidRaw);
            if (existing.Type == FluidType.None || existing.Mass == 0)
            {
                return true;
            }

            // Zit sivi varsa (Su + Lav) stokiyometrik reaksiyon icin gecerlidir
            if (existing.Type != pourType)
            {
                return true;
            }

            // Ayni tip sivi varsa en az 32 birim bosluk kalmissa kabul et; agzina kadar doluysa ust/yan komsuya tasir!
            return existing.Mass + 32 <= cap;
        }

        private static bool TryReadWorldCell(
            IReadOnlyDictionary<(int cx, int cz), uint[]> worldChunks,
            IReadOnlyDictionary<(int cx, int cz), ushort[]> fluidChunks,
            int wx,
            int wy,
            int wz,
            out uint solidRaw,
            out ushort fluidRaw)
        {
            solidRaw = 0u;
            fluidRaw = 0;
            int cx = FloorDiv16Pure(wx);
            int cz = FloorDiv16Pure(wz);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;
            int idx = (wy << 8) | (lz << 4) | lx;

            if (!worldChunks.TryGetValue((cx, cz), out uint[]? solids) || solids == null || (uint)idx >= (uint)solids.Length ||
                !fluidChunks.TryGetValue((cx, cz), out ushort[]? fluids) || fluids == null || (uint)idx >= (uint)fluids.Length)
            {
                return false;
            }

            solidRaw = solids[idx];
            fluidRaw = fluids[idx];
            return true;
        }

        private static int FloorDiv16Pure(int v) => v >= 0 ? (v >> 4) : -(((-v) + 15) >> 4);

        private static int FloorToIntPure(float v) => (int)MathF.Floor(v);
    }
}
