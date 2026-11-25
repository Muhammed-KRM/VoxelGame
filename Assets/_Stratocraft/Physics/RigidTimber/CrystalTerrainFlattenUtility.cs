#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// [C] Madde Kristali Astroneer-tarzi Yer Duzleme (Flatten) + [V] Oyuncu Eliyle Kristal-Bagisik Zemin Dolgu (Backfill)
    /// saf C# cekirdek mantigi (UnityEngine bagimsiz, 0 B GC).
    /// </summary>
    public static class CrystalTerrainFlattenUtility
    {
        /// <summary>
        /// Bir vokselin [C] Madde Kristali (vakum veya yer duzleme) tarafindan kirilip kirilamayacagini belirler.
        /// Oyuncu eliyle konulmus (BlokKokenDurumu.OyuncuItemBlogu) zemin/bloklar ASLA kristalle vakumlanamaz!
        /// </summary>
        public static bool IsProtectedFromCrystalVacuum(uint rawVoxel, bool isCreativeMode)
        {
            byte mat = VoxelBitUtility.GetMaterialId(rawVoxel);
            if (mat == MaterialIdCatalog.Air) return true;
            if (mat == MaterialIdCatalog.Bedrock) return true;

            // Oyuncu eliyle [V] voksel dolgu olarak konulan toprak/kil/kum/cakil
            // Yaratici Mod dahil hicbir durumda [C] Madde Kristali ile kirilamaz!
            if (VoxelBitUtility.GetOriginState(rawVoxel) == BlokKokenDurumu.OyuncuItemBlogu)
            {
                return true;
            }

            if (isCreativeMode) return false;

            if (MaterialIdCatalog.IsCraftedItemBlockMaterial(mat))
            {
                return true;
            }
            if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(mat))
            {
                return true;
            }
            if (mat >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Oyuncunun elindeki toprak/kil/kum/cakil topagini SurfaceNets araziyle puruzsuz birlesecek sekilde
        /// normal dogal gorunumlu voksel (Shape=0, Density=127) olarak yazar, ANCAK OriginState=OyuncuItemBlogu
        /// isaretledigi icin [C] Madde Kristali ile ASLA vakumlanip kirilamaz!
        /// </summary>
        public static uint PackCrystalImmuneBackfillSoilVoxel(byte soilMaterialId)
        {
            byte mat = soilMaterialId == MaterialIdCatalog.Air ? MaterialIdCatalog.Dirt : soilMaterialId;
            return VoxelBitUtility.PackStandard(
                mat,
                density: 127,
                shapeId: 0,
                rot24: 0,
                origin: BlokKokenDurumu.OyuncuItemBlogu,
                dmg4: 0);
        }

        public struct FlattenStepResult
        {
            public int ShavedVoxels;
            public int FilledVoxels;
            public int SkippedProtectedVoxels;
            public byte LastShavedMaterial;
        }

        /// <summary>
        /// Astroneer-tarzi Yer Duzleme (Flatten) adimi:
        /// - targetSurfaceY ustundeki dogal cikintilari traslar (depositCallback ile Kristal haznesine aktarir).
        /// - targetSurfaceY ve hemen altindaki cukurlari haznedeki malzemeyle doldurur (withdrawCallback).
        /// - Oyuncu eliyle konulmus (OyuncuItemBlogu) toprak/yapi bloklarina, cevherlere ve ana kayaya ASLA dokunmaz!
        /// </summary>
        public static FlattenStepResult ApplyFlattenBrush(
            int centerWx,
            int centerWz,
            int targetSurfaceY,
            float radiusMeters,
            int maxEditableY,
            byte fillMaterialId,
            bool isCreativeMode,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            Action<byte>? onShavedDeposit = null,
            Func<byte, bool>? onFillWithdraw = null)
        {
            var result = new FlattenStepResult();
            int rInt = Math.Clamp((int)MathF.Ceiling(radiusMeters), 1, 6);
            float rSq = radiusMeters * radiusMeters;

            int minY = Math.Max(2, targetSurfaceY - 2);
            int maxY = Math.Min(maxEditableY - 2, targetSurfaceY + 4);
            byte effectiveFillMat = fillMaterialId == MaterialIdCatalog.Air ? MaterialIdCatalog.Dirt : fillMaterialId;

            for (int dz = -rInt; dz <= rInt; dz++)
            {
                for (int dx = -rInt; dx <= rInt; dx++)
                {
                    if (dx * dx + dz * dz > rSq) continue;

                    int wx = centerWx + dx;
                    int wz = centerWz + dz;
                    int cx = wx >> 4;
                    int cz = wz >> 4;
                    if (!worldChunks.TryGetValue((cx, cz), out uint[]? voxels) || voxels == null) continue;

                    int lx = wx - (cx << 4);
                    int lz = wz - (cz << 4);
                    bool columnModified = false;

                    // 1) Hedef duzlemin ustundeki cikintilari trasla (yukaridan asagiya)
                    for (int wy = maxY; wy > targetSurfaceY; wy--)
                    {
                        int idx = (wy << 8) | (lz << 4) | lx;
                        uint curRaw = voxels[idx];
                        byte curMat = VoxelBitUtility.GetMaterialId(curRaw);
                        if (curMat == MaterialIdCatalog.Air ||
                            curMat == MaterialIdCatalog.FiniteWaterSurface ||
                            curMat == MaterialIdCatalog.FiniteLavaSurface)
                        {
                            continue;
                        }

                        if (IsProtectedFromCrystalVacuum(curRaw, isCreativeMode))
                        {
                            result.SkippedProtectedVoxels++;
                            continue;
                        }

                        voxels[idx] = 0u;
                        result.ShavedVoxels++;
                        result.LastShavedMaterial = curMat;
                        onShavedDeposit?.Invoke(curMat);
                        columnModified = true;
                    }

                    // 2) Hedef duzlemdeki ve ALTINDAKI BOSLUGU gercek kati zemine degene kadar (maks 14 blok) kesintisiz doldur!
                    // Boylece Astroneer duzleme platformunun alti ASLA bos/havada kabuk olmaz!
                    int fillBottomY = targetSurfaceY;
                    int lowestScanY = Math.Max(2, targetSurfaceY - 14);
                    for (int wy = targetSurfaceY; wy >= lowestScanY; wy--)
                    {
                        byte mCheck = VoxelBitUtility.GetMaterialId(voxels[(wy << 8) | (lz << 4) | lx]);
                        if (mCheck != MaterialIdCatalog.Air && mCheck != MaterialIdCatalog.FiniteWaterSurface)
                        {
                            fillBottomY = wy + 1;
                            break;
                        }
                        fillBottomY = wy;
                    }

                    for (int wy = fillBottomY; wy <= targetSurfaceY; wy++)
                    {
                        int idx = (wy << 8) | (lz << 4) | lx;
                        uint curRaw = voxels[idx];
                        byte curMat = VoxelBitUtility.GetMaterialId(curRaw);
                        if (curMat != MaterialIdCatalog.Air &&
                            curMat != MaterialIdCatalog.FiniteWaterSurface)
                        {
                            continue;
                        }

                        if (!isCreativeMode && onFillWithdraw != null && !onFillWithdraw(effectiveFillMat))
                        {
                            continue; // Haznede dolgu malzemesi bitti
                        }

                        voxels[idx] = VoxelBitUtility.PackStandard(
                            effectiveFillMat,
                            density: 127,
                            shapeId: 0,
                            rot24: 0,
                            origin: BlokKokenDurumu.DogalKutle,
                            dmg4: 0);
                        result.FilledVoxels++;
                        columnModified = true;
                    }

                    if (columnModified)
                    {
                        dirtyChunks.Add((cx, cz));
                        if (lx == 0) dirtyChunks.Add((cx - 1, cz));
                        if (lx == 15) dirtyChunks.Add((cx + 1, cz));
                        if (lz == 0) dirtyChunks.Add((cx, cz - 1));
                        if (lz == 15) dirtyChunks.Add((cx, cz + 1));
                    }
                }
            }

            return result;
        }
    }
}
