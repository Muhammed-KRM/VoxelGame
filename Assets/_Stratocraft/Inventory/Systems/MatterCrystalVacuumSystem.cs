using System;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Logging;
using Stratocraft.Inventory.Data;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Inventory.Systems
{
    public readonly struct VacuumAttemptResult
    {
        public readonly bool Success;
        public readonly string? ErrorCode;
        public readonly byte VacuumedMaterialId;

        public VacuumAttemptResult(bool success, string? errorCode, byte vacuumedMaterialId)
        {
            Success = success;
            ErrorCode = errorCode;
            VacuumedMaterialId = vacuumedMaterialId;
        }
    }

    /// <summary>
    /// Madde Kristali (Matter Crystal) Doğal Kütle Vakum ve Geri Püskürtme Sistemi.
    /// - Yalnızca OriginState == DogalKutle (0) olan sıradan doğal blokları (Taş, Toprak, Kum, Çakıl, Bazalt vb.) emer.
    /// - OriginState == OyuncuItemBlogu (1) olan blokları ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN ile reddeder.
    /// - 14 yeraltı cevherini (30..43) ve mücevher damarlarını (45..49) ERR_ORE_RESONANCE_IMMUNE ile reddeder.
    /// - Doğal Obsidyeni (6), Ana Kayayı (5) ve Zindan Koruma Bloklarını (240..255) vakumlamaz.
    /// - Vakumlanan bloğun DamageState (0..15 -> 1/256 kesirli) hasarını korur; geri yerleştirildiğinde
    ///   yine OriginState == DogalKutle (0) ve aynı hasar seviyesiyle koyar (Can Sıfırlama ve Batarya Sömürü Koruması).
    /// </summary>
    public sealed class MatterCrystalVacuumSystem
    {
        private readonly IGameLogService? _logger;

        public MatterCrystalVacuumSystem(IGameLogService? logger = null)
        {
            _logger = logger;
        }

        public VacuumAttemptResult TryVacuumVoxel(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            CrystalMassStorage storage,
            ulong traceId = 0)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            VoxelCell cell = chunk.GetVoxel(lx, y, lz);
            if (!cell.IsSolid)
            {
                return new VacuumAttemptResult(false, null, MaterialIdCatalog.Air);
            }

            byte matId = cell.MaterialId;

            // 1. Oyuncu yapımı gerçek blok (IS_ITEM_BLOCK == 1) kontrolü
            if (cell.OriginState == BlokKokenDurumu.OyuncuItemBlogu)
            {
                _logger?.LogFunctionError(
                    ErrorCodes.CrystalCraftedBlockForbidden,
                    null,
                    $"{{\"lx\":{lx},\"y\":{y},\"lz\":{lz},\"matId\":{matId}}}",
                    traceId);
                return new VacuumAttemptResult(false, ErrorCodes.CrystalCraftedBlockForbidden, matId);
            }

            // 2. 14 Yeraltı Cevheri ve Mücevher Rezonans Bağışıklığı (30..43, 45..49) + Doğal Obsidyen
            if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId) ||
                matId == MaterialIdCatalog.NaturalObsidian ||
                matId == MaterialIdCatalog.Bedrock ||
                matId >= MaterialIdCatalog.DungeonSanctumImmuneStart)
            {
                _logger?.LogFunctionError(
                    ErrorCodes.CrystalOreResonanceImmune,
                    null,
                    $"{{\"lx\":{lx},\"y\":{y},\"lz\":{lz},\"matId\":{matId}}}",
                    traceId);
                return new VacuumAttemptResult(false, ErrorCodes.CrystalOreResonanceImmune, matId);
            }

            // 3. Yalnızca DogalKutle (0) vakumlanabilir (BataryaUretimi veya VoxelLockedAtomic reddedilir)
            if (cell.OriginState != BlokKokenDurumu.DogalKutle)
            {
                return new VacuumAttemptResult(false, ErrorCodes.CrystalCraftedBlockForbidden, matId);
            }

            if (!storage.TryDepositNaturalBlock(matId, cell.DamageState))
            {
                return new VacuumAttemptResult(false, null, matId);
            }

            chunk.SetVoxel(lx, y, lz, VoxelCell.Air);
            return new VacuumAttemptResult(true, null, matId);
        }

        /// <summary>
        /// Kristaldeki sıkıştırılmış doğal kütleyi dünyaya geri püskürtür.
        /// Çıkan blok her zaman DogalKutle (OriginState = 0) olarak kalır ve önceki hasar durumunu korur.
        /// </summary>
        public bool TryExtrudeNaturalBlock(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            byte materialId,
            CrystalMassStorage storage)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (chunk.GetVoxel(lx, y, lz).IsSolid)
            {
                return false;
            }

            if (!storage.TryWithdrawNaturalBlock(materialId, out byte restoredDamage4Bit))
            {
                return false;
            }

            uint packed = VoxelBitUtility.PackStandard(
                materialId,
                VoxelBitUtility.FullDensity,
                1,
                0,
                BlokKokenDurumu.DogalKutle,
                restoredDamage4Bit);

            chunk.SetVoxel(lx, y, lz, new VoxelCell(packed));
            return true;
        }
    }
}
