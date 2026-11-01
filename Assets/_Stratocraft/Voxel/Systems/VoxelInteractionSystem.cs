#nullable enable
using System;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Fiziksel kazma/alet ile blok kirma, Smooth Surface Nets 1x1m tunel yumusatma (Soft-Bevel Mining Clearance)
    /// ve OyuncuItemBlogu (OriginState = 1) yerlestirme sistemi.
    /// - Kirilan BataryaUretimi / KaliciYapi (OriginState = 2) bloklar %0 esya dusurur (Sifir Drop Somuru Korumasi).
    /// - Kazilan 1x1m tunellerde komsu dogal SDF bloklarinin asiri siskin yogunlugu (&gt; 92 -&gt; 88) tiraslanarak
    ///   R_prone = 0.20m surunme kapsulunun pürüzsüz gecmesi saglanir!
    /// </summary>
    public sealed class VoxelInteractionSystem
    {
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
            if (!VoxelBitUtility.IsPhysicallySolid(cell.RawValue))
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

            // Blok kirildi -> Hava yap ve 1x1m tunel gecisi icin komsu dogal SDF siskinligini yumusat (Soft-Bevel)
            chunk.SetVoxel(lx, y, lz, VoxelCell.Air);
            ApplySoftBevelTunnelClearance(chunk, lx, y, lz);

            // BataryaUretimi / KaliciYapi (OriginState == 2) bloklar kirildiginda %0 drop verir
            if (cell.OriginState == BlokKokenDurumu.BataryaUretimi)
            {
                return (true, MaterialIdCatalog.Air, 0);
            }

            byte dropMat = cell.MaterialId == MaterialIdCatalog.Stone ? MaterialIdCatalog.Cobblestone : cell.MaterialId;
            return (true, dropMat, 1);
        }

        /// <summary>
        /// Kazilan hucrenin 4 yatay komsusundaki dogal SDF (ShapeID == 0) bloklarin yogunlugunu
        /// katiligini bozmadan (&gt;= 64 kalacak sekilde) max 88'e yumusatir; 1x1m surunme tuneli bogaz daralmasini onler.
        /// </summary>
        public static void ApplySoftBevelTunnelClearance(VoxelChunkComponent chunk, int lx, int y, int lz)
        {
            ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
            ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };

            for (int d = 0; d < 4; d++)
            {
                int nx = lx + dx[d];
                int nz = lz + dz[d];
                if ((uint)nx >= StratocraftConstants.ChunkSizeX || (uint)nz >= StratocraftConstants.ChunkSizeZ)
                {
                    continue;
                }

                uint nRaw = chunk.GetVoxel(nx, y, nz).RawValue;
                byte nMat = VoxelBitUtility.GetMaterialId(nRaw);
                if (nMat == MaterialIdCatalog.Air ||
                    nMat == MaterialIdCatalog.Bedrock ||
                    nMat >= MaterialIdCatalog.DungeonSanctumImmuneStart ||
                    VoxelBitUtility.GetShapeId(nRaw) != 0)
                {
                    continue;
                }

                byte dens = VoxelBitUtility.GetDensityOrLiquid(nRaw);
                if (dens > 88)
                {
                    uint beveled = VoxelBitUtility.PackStandard(
                        nMat,
                        density: 88,
                        shapeId: 0,
                        rot24: VoxelBitUtility.GetRotationState(nRaw),
                        origin: VoxelBitUtility.GetOriginState(nRaw),
                        dmg4: VoxelBitUtility.GetDamageState(nRaw));
                    chunk.SetVoxel(nx, y, nz, new VoxelCell(beveled));
                }
            }
        }

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
            if (VoxelBitUtility.IsPhysicallySolid(existing.RawValue))
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
