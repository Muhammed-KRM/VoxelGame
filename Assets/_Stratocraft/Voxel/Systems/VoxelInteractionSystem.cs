using System;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Fiziksel kazma/alet ile blok kirma, yontma ve OyuncuItemBlogu (OriginState = 1) yerlestirme sistemi.
    /// - Kirilan BataryaUretimi (OriginState = 2) bloklar %0 esya dusurur (Sifir Drop Somuru Korumasi).
    /// - Kazma/alet ile kirilan dogal bloklar veya cevherler gercek esya (IS_ITEM_BLOCK = 1) uretir.
    /// </summary>
    public sealed class VoxelInteractionSystem
    {
        /// <summary>
        /// Belirtilen koordinattaki bloga kazma hasari uygular. Blok kirilirsa dusen MaterialId ve miktari doner.
        /// </summary>
        public (bool Broken, byte DroppedMaterialId, int DroppedCount) ApplyMiningDamage(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            byte damageSteps,
            int toolTier = 3)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            VoxelCell cell = chunk.GetVoxel(lx, y, lz);
            if (!cell.IsSolid)
            {
                return (false, MaterialIdCatalog.Air, 0);
            }

            // Bedrock ve Zindan Kutsal Koruma bloklari kirilamaz
            if (cell.MaterialId == MaterialIdCatalog.Bedrock || cell.MaterialId >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                return (false, MaterialIdCatalog.Air, 0);
            }

            // Yuksek kademeli cevherler yetersiz kazma kademesiyle kirilamaz
            if (cell.MaterialId >= MaterialIdCatalog.MithrilOre && cell.MaterialId <= MaterialIdCatalog.VoidMetalOre && toolTier < 2)
            {
                return (false, MaterialIdCatalog.Air, 0);
            }

            int nextDamage = cell.DamageState + Math.Max((byte)1, damageSteps);
            if (nextDamage <= 15)
            {
                uint updatedRaw = VoxelBitUtility.WithDamageState(cell.RawValue, (byte)nextDamage);
                chunk.SetVoxel(lx, y, lz, new VoxelCell(updatedRaw));
                return (false, MaterialIdCatalog.Air, 0);
            }

            // Blok kirildi -> Hava yap
            chunk.SetVoxel(lx, y, lz, VoxelCell.Air);

            // BataryaUretimi (OriginState == 2) bloklar kirildiginda %0 drop verir
            if (cell.OriginState == BlokKokenDurumu.BataryaUretimi)
            {
                return (true, MaterialIdCatalog.Air, 0);
            }

            byte dropMat = cell.MaterialId == MaterialIdCatalog.Stone ? MaterialIdCatalog.Cobblestone : cell.MaterialId;
            return (true, dropMat, 1);
        }

        /// <summary>
        /// Oyuncunun envanterindeki gercek bir blok esyasini dunyaya OyuncuItemBlogu (OriginState = 1) olarak yerlestirir.
        /// </summary>
        public bool PlacePlayerItemBlock(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            byte materialId,
            byte shapeId = 1,
            byte rotationState = 0)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (materialId == MaterialIdCatalog.Air)
            {
                return false;
            }

            VoxelCell existing = chunk.GetVoxel(lx, y, lz);
            if (existing.IsSolid)
            {
                return false;
            }

            uint packed = VoxelBitUtility.PackStandard(
                materialId,
                VoxelBitUtility.FullDensity,
                shapeId,
                rotationState,
                BlokKokenDurumu.OyuncuItemBlogu,
                0);

            chunk.SetVoxel(lx, y, lz, new VoxelCell(packed));
            return true;
        }
    }
}
