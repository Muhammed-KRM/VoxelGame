#nullable enable
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
        public readonly uint TransferredMassQ24_8;

        public VacuumAttemptResult(bool success, string? errorCode, byte vacuumedMaterialId, uint transferredMassQ24_8 = 0u)
        {
            Success = success;
            ErrorCode = errorCode;
            VacuumedMaterialId = vacuumedMaterialId;
            TransferredMassQ24_8 = transferredMassQ24_8;
        }
    }

    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.1 — Madde Kristali (Matter Crystal) 5-Kapili Vakum,
    /// Post-Quantization Kayipsiz Q24.8 SDF Kure Fircasi ve 0.25m Mikro-Keski (Micro-Chisel) Sistemi:
    /// 1. OriginState != DogalKutle (0) olan tum bloklar (OyuncuItemBlogu=1, BataryaUretimi/KaliciYapi=2, Locked=3)
    ///    ERR_CRYSTAL_CRAFTED_BLOCK_FORBIDDEN ile kesin reddedilir!
    /// 2. 14 Yeralti Cevheri (30..43), Komur (Coal=44), Tum Mucevherler (45..49, 52..59), Bedrock (5) ve
    ///    Kadim Zindan Kutsal Bloklari (&gt;= 240) ERR_ORE_RESONANCE_IMMUNE ile kesin reddedilir!
    /// 3. Dogal Obsidyen (NaturalObsidian=6, OriginState==0) Seviye 1..3 kristallerde reddedilir,
    ///    yalnizca Seviye 4 (CrystalTier &gt;= 4) Madde Kristali ile vakumlanabilir!
    /// 4. Kristalden dunyaya puskurtulen veya sekillendirilen HER voksel/mikro-voksel
    ///    HER ZAMAN OriginState = DogalKutle (0) olarak yazilir (%0 esya kopyalama / batarya somurusu)!
    /// </summary>
    public sealed class MatterCrystalVacuumSystem
    {
        private readonly IGameLogService? _logger;

        public MatterCrystalVacuumSystem(IGameLogService? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// 5-Kapili Kanonik G_vac Dogrulayicisi.
        /// </summary>
        public VacuumAttemptResult ValidateVacuumEligibility(VoxelCell cell, byte crystalTier, int lx, int y, int lz, ulong traceId)
        {
            byte matId = cell.MaterialId;
            if (matId == MaterialIdCatalog.Air || !VoxelBitUtility.IsPhysicallySolid(cell.RawValue))
            {
                return new VacuumAttemptResult(false, null, MaterialIdCatalog.Air);
            }

            // Kapi 1: Yalnizca OriginState == DogalKutle (0) vakumlanabilir!
            if (cell.OriginState != BlokKokenDurumu.DogalKutle)
            {
                _logger?.LogFunctionError(
                    ErrorCodes.CrystalCraftedBlockForbidden,
                    null,
                    $"{{\"lx\":{lx},\"y\":{y},\"lz\":{lz},\"matId\":{matId},\"origin\":{(int)cell.OriginState}}}",
                    traceId);
                return new VacuumAttemptResult(false, ErrorCodes.CrystalCraftedBlockForbidden, matId);
            }

            // Kapi 2: 14 Cevher + Komur (44) + Mucevherler + Bedrock + Zindan Koruma Bagisikligi
            if (MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId) ||
                matId == MaterialIdCatalog.Bedrock ||
                matId >= MaterialIdCatalog.DungeonSanctumImmuneStart ||
                MaterialIdCatalog.IsCraftedItemBlockMaterial(matId))
            {
                _logger?.LogFunctionError(
                    ErrorCodes.CrystalOreResonanceImmune,
                    null,
                    $"{{\"lx\":{lx},\"y\":{y},\"lz\":{lz},\"matId\":{matId}}}",
                    traceId);
                return new VacuumAttemptResult(false, ErrorCodes.CrystalOreResonanceImmune, matId);
            }

            // Kapi 3: Kristal Kademesi Kontrolu (Toprak=Tier1, Tas=Tier2, Buz/Mermer=Tier3, Bazalt/Magma/NaturalObsidian=Tier4)
            byte requiredTier = MaterialIdCatalog.GetRequiredCrystalTierForTerrain(matId);
            if (requiredTier == 0 || requiredTier == byte.MaxValue || crystalTier < requiredTier)
            {
                _logger?.LogFunctionError(
                    ErrorCodes.CrystalOreResonanceImmune,
                    null,
                    $"{{\"lx\":{lx},\"y\":{y},\"lz\":{lz},\"matId\":{matId},\"reqTier\":{requiredTier},\"curTier\":{crystalTier}}}",
                    traceId);
                return new VacuumAttemptResult(false, ErrorCodes.CrystalOreResonanceImmune, matId);
            }

            return new VacuumAttemptResult(true, null, matId);
        }

        public VacuumAttemptResult TryVacuumVoxel(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            CrystalMassStorage storage,
            ulong traceId = 0)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            if (storage == null) throw new ArgumentNullException(nameof(storage));

            VoxelCell cell = chunk.GetVoxel(lx, y, lz);
            VacuumAttemptResult gate = ValidateVacuumEligibility(cell, storage.CrystalTier, lx, y, lz, traceId);
            if (!gate.Success)
            {
                return gate;
            }

            byte matId = cell.MaterialId;
            uint massQ24_8;
            if (cell.IsChiseledMicroVoxel)
            {
                MicroVoxelPool4096 pool = chunk.GetOrCreateMicroPool(y >> 4);
                ulong mask = pool.GetMask(cell.MicroPoolIndex);
                massQ24_8 = (uint)MicroVoxelPool4096.CountSolidMicroCubes(mask) * CrystalMassStorageData.OneMicroSubVoxelQ24_8;
            }
            else if (cell.ShapeId == 0)
            {
                massQ24_8 = CrystalMassStorage.MassQ24_8FromDensity7(cell.DensityOrLiquid);
            }
            else
            {
                massQ24_8 = CrystalMassStorageData.OneBlockQ24_8;
            }

            if (massQ24_8 == 0u || !storage.TryDepositMassQ24_8(matId, massQ24_8, cell.DamageState))
            {
                return new VacuumAttemptResult(false, null, matId);
            }

            if (cell.IsChiseledMicroVoxel)
            {
                MicroVoxelPool4096 pool = chunk.GetOrCreateMicroPool(y >> 4);
                pool.UpdateOrAutoCompact(cell, MicroVoxelPool4096.ComputeLocalIndex12(lx, y, lz), MicroVoxelPool4096.EmptyMask);
            }

            chunk.SetVoxel(lx, y, lz, VoxelCell.Air);
            return new VacuumAttemptResult(true, null, matId, massQ24_8);
        }

        /// <summary>
        /// Kristaldeki sikistirilmis dogal kutleyi dunyaya geri puskurtur.
        /// Cikan blok HER ZAMAN OriginState = DogalKutle (0) olarak kalir ve onceki hasar durumunu korur!
        /// </summary>
        public bool TryExtrudeNaturalBlock(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            byte materialId,
            CrystalMassStorage storage,
            byte shapeId = 1,
            byte rotationState = 0)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            if (storage == null) throw new ArgumentNullException(nameof(storage));

            if (VoxelBitUtility.IsPhysicallySolid(chunk.GetVoxel(lx, y, lz).RawValue))
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
                shapeId,
                rotationState,
                BlokKokenDurumu.DogalKutle, // KESIN KURAL: Kristal ciktisi DAIMA DogalKutle (0)!
                restoredDamage4Bit);

            chunk.SetVoxel(lx, y, lz, new VoxelCell(packed));
            return true;
        }

        /// <summary>
        /// Post-Quantization Kayipsiz Q24.8 SDF Heykeltraslik Fircasi:
        /// 7-bit (0..127) kuantizasyon SONRASI gercek kutle farkini (newQ - oldQ) hesaplayarak
        /// kristal deposu ile dunya arasinda 1 bit bile sapma olmadan puruzsuz Magara/Arazi sekillendirir!
        /// </summary>
        public int ApplyLosslessSdfSphereBrush(
            VoxelChunkComponent chunk,
            float centerX,
            float centerY,
            float centerZ,
            float radiusBlocks,
            int signedDensityDelta,
            byte extrudeMaterialId,
            CrystalMassStorage storage)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (signedDensityDelta == 0 || radiusBlocks <= 0.1f) return 0;

            int minX = Math.Max(0, (int)MathF.Floor(centerX - radiusBlocks));
            int maxX = Math.Min(StratocraftConstants.ChunkSizeX - 1, (int)MathF.Ceiling(centerX + radiusBlocks));
            int minY = Math.Max(1, (int)MathF.Floor(centerY - radiusBlocks));
            int maxY = Math.Min(StratocraftConstants.ChunkSizeY - 2, (int)MathF.Ceiling(centerY + radiusBlocks));
            int minZ = Math.Max(0, (int)MathF.Floor(centerZ - radiusBlocks));
            int maxZ = Math.Min(StratocraftConstants.ChunkSizeZ - 1, (int)MathF.Ceiling(centerZ + radiusBlocks));

            int modifiedCells = 0;
            float rSq = radiusBlocks * radiusBlocks;

            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = (x + 0.5f) - centerX;
                        float dy = (y + 0.5f) - centerY;
                        float dz = (z + 0.5f) - centerZ;
                        float distSq = dx * dx + dy * dy + dz * dz;
                        if (distSq > rSq)
                        {
                            continue;
                        }

                        float falloff = 1.0f - MathF.Sqrt(distSq) / radiusBlocks;
                        int stepDelta = (int)MathF.Round(signedDensityDelta * falloff);
                        if (stepDelta == 0)
                        {
                            continue;
                        }

                        VoxelCell cur = chunk.GetVoxel(x, y, z);
                        if (stepDelta < 0)
                        {
                            // Vakumlama (oyma): Yalnizca G_vac gecen dogal kutleler oyulabilir!
                            if (cur.MaterialId == MaterialIdCatalog.Air) continue;
                            if (!ValidateVacuumEligibility(cur, storage.CrystalTier, x, y, z, 0).Success) continue;
                            if (cur.ShapeId != 0 && cur.ShapeId != ArchitecturalShapeCatalog.FullMasonryCube) continue;

                            byte oldDens = cur.ShapeId == 0 ? cur.DensityOrLiquid : VoxelBitUtility.FullDensity;
                            byte newDens = (byte)Math.Clamp(oldDens + stepDelta, 0, 127);
                            if (newDens < 16) newDens = 0;

                            uint oldMass = CrystalMassStorage.MassQ24_8FromDensity7(oldDens);
                            uint newMass = CrystalMassStorage.MassQ24_8FromDensity7(newDens);
                            uint gainedMass = oldMass - newMass;
                            if (gainedMass == 0u || !storage.TryDepositMassQ24_8(cur.MaterialId, gainedMass, cur.DamageState))
                            {
                                continue;
                            }

                            VoxelCell nextCell = newDens == 0
                                ? VoxelCell.Air
                                : new VoxelCell(VoxelBitUtility.PackStandard(cur.MaterialId, newDens, 0, 0, BlokKokenDurumu.DogalKutle, cur.DamageState));
                            chunk.SetVoxel(x, y, z, nextCell);
                            modifiedCells++;
                        }
                        else
                        {
                            // Puskurtme (insa/dolgu): DAIMA OriginState = DogalKutle (0) yazar!
                            byte targetMat = cur.MaterialId == MaterialIdCatalog.Air ? extrudeMaterialId : cur.MaterialId;
                            if (targetMat == MaterialIdCatalog.Air) continue;
                            if (cur.MaterialId != MaterialIdCatalog.Air && cur.OriginState != BlokKokenDurumu.DogalKutle) continue;

                            byte oldDens = cur.MaterialId == MaterialIdCatalog.Air
                                ? (byte)0
                                : (cur.ShapeId == 0 ? cur.DensityOrLiquid : VoxelBitUtility.FullDensity);
                            byte newDens = (byte)Math.Clamp(oldDens + stepDelta, 0, 127);
                            if (oldDens == 0 && newDens < StratocraftConstants.SdfIsoLevel)
                            {
                                newDens = StratocraftConstants.SdfIsoLevel;
                            }

                            uint oldMass = CrystalMassStorage.MassQ24_8FromDensity7(oldDens);
                            uint newMass = CrystalMassStorage.MassQ24_8FromDensity7(newDens);
                            if (newMass <= oldMass) continue;

                            uint neededMass = newMass - oldMass;
                            if (!storage.TryWithdrawMassQ24_8(targetMat, neededMass, out byte dmg))
                            {
                                continue;
                            }

                            byte finalDmg = cur.MaterialId == MaterialIdCatalog.Air ? dmg : Math.Max(cur.DamageState, dmg);
                            uint nextRaw = VoxelBitUtility.PackStandard(targetMat, newDens, 0, 0, BlokKokenDurumu.DogalKutle, finalDmg);
                            chunk.SetVoxel(x, y, z, new VoxelCell(nextRaw));
                            modifiedCells++;
                        }
                    }
                }
            }

            return modifiedCells;
        }

        /// <summary>
        /// Madde Kristali 0.25m Mikro-Keski (4x4x4 = 64 alt-kup; her alt-kup tam 4 Q24.8 kutle birimi!):
        /// Dogal blogu 0.25m hassasiyetle oyar veya doldurur; OriginState DAIMA DogalKutle (0) kalir.
        /// </summary>
        public bool TryChiselNaturalMicroSubVoxel(
            VoxelChunkComponent chunk,
            int lx,
            int y,
            int lz,
            int subX,
            int subY,
            int subZ,
            bool carveOut,
            byte fillMaterialId,
            CrystalMassStorage storage)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if ((uint)subX >= 4u || (uint)subY >= 4u || (uint)subZ >= 4u) return false;

            int bitIndex = (subY << 4) | (subZ << 2) | subX;
            ulong bitMask = 1UL << bitIndex;
            ushort localIdx12 = MicroVoxelPool4096.ComputeLocalIndex12(lx, y, lz);
            MicroVoxelPool4096 pool = chunk.GetOrCreateMicroPool(y >> 4);
            VoxelCell cell = chunk.GetVoxel(lx, y, lz);

            if (carveOut)
            {
                if (!ValidateVacuumEligibility(cell, storage.CrystalTier, lx, y, lz, 0).Success)
                {
                    return false;
                }

                ulong currentMask = cell.IsChiseledMicroVoxel
                    ? pool.GetMask(cell.MicroPoolIndex)
                    : MicroVoxelPool4096.FullCubeMask;

                if ((currentMask & bitMask) == 0UL)
                {
                    return false;
                }

                if (!storage.TryDepositMassQ24_8(cell.MaterialId, CrystalMassStorageData.OneMicroSubVoxelQ24_8, cell.DamageState))
                {
                    return false;
                }

                ulong nextMask = currentMask & ~bitMask;
                VoxelCell baseNatural = new VoxelCell(VoxelBitUtility.PackStandard(
                    cell.MaterialId, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, cell.DamageState));
                VoxelCell updated = pool.UpdateOrAutoCompact(baseNatural, localIdx12, nextMask);
                chunk.SetVoxel(lx, y, lz, updated);
                return true;
            }
            else
            {
                byte mat = cell.MaterialId == MaterialIdCatalog.Air ? fillMaterialId : cell.MaterialId;
                if (mat == MaterialIdCatalog.Air) return false;
                if (cell.MaterialId != MaterialIdCatalog.Air && cell.OriginState != BlokKokenDurumu.DogalKutle) return false;

                ulong currentMask = cell.MaterialId == MaterialIdCatalog.Air
                    ? MicroVoxelPool4096.EmptyMask
                    : (cell.IsChiseledMicroVoxel ? pool.GetMask(cell.MicroPoolIndex) : MicroVoxelPool4096.FullCubeMask);

                if ((currentMask & bitMask) != 0UL)
                {
                    return false;
                }

                if (!storage.TryWithdrawMassQ24_8(mat, CrystalMassStorageData.OneMicroSubVoxelQ24_8, out byte dmg))
                {
                    return false;
                }

                ulong nextMask = currentMask | bitMask;
                VoxelCell baseNatural = new VoxelCell(VoxelBitUtility.PackStandard(
                    mat, VoxelBitUtility.FullDensity, 1, 0, BlokKokenDurumu.DogalKutle, dmg));
                VoxelCell updated = pool.UpdateOrAutoCompact(baseNatural, localIdx12, nextMask);
                chunk.SetVoxel(lx, y, lz, updated);
                return true;
            }
        }
    }
}
