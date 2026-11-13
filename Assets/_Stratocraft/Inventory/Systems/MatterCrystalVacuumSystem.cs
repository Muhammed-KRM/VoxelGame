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
        public const float SafeStarterZoneRadiusMeters = 75.0f;
        public const float Ring2OuterRadiusMeters = 180.0f;
        public const float Ring3OuterRadiusMeters = 320.0f;
        public const float Ring4OuterRadiusMeters = 500.0f;

        private readonly IGameLogService? _logger;
        private byte _crystalTier = 1;

        /// <summary>
        /// Aktif Madde Kristali kademesi (1..5 arasinda sinirlandirilir).
        /// </summary>
        public byte CrystalTier
        {
            get => _crystalTier;
            set => _crystalTier = (byte)Math.Clamp((int)value, 1, 5);
        }

        public float CurrentVacuumRadius => GetVacuumRadiusForTier(_crystalTier);
        public float CurrentPullSpeed => GetPullSpeedForTier(_crystalTier);
        public int CurrentBatchCapacity => GetBatchCapacityForTier(_crystalTier);
        public float CurrentBrushRadius => GetBrushRadiusForTier(_crystalTier);
        public float CurrentScatterRadiusXZ => GetNaturalScatterRadiusXZForTier(_crystalTier);

        public MatterCrystalVacuumSystem(IGameLogService? logger = null)
        {
            _logger = logger;
            _crystalTier = 1;
        }

        public static int ClampCrystalTier(int crystalTier) => Math.Clamp(crystalTier, 1, 5);

        /// <summary>
        /// Kademe bazli esya/kutle vakum cekim yaricapi (metre): T1=6.5m .. T5=13.0m.
        /// </summary>
        public static float GetVacuumRadiusForTier(int crystalTier) => Math.Clamp(crystalTier, 1, 5) switch
        {
            1 => 6.5f,
            2 => 8.0f,
            3 => 9.5f,
            4 => 11.0f,
            _ => 13.0f
        };

        /// <summary>
        /// Kademe bazli vakum cekim hizi (m/sn): T1=12.5 .. T5=26.0.
        /// </summary>
        public static float GetPullSpeedForTier(int crystalTier) => Math.Clamp(crystalTier, 1, 5) switch
        {
            1 => 12.5f,
            2 => 15.5f,
            3 => 18.5f,
            4 => 22.0f,
            _ => 26.0f
        };

        /// <summary>
        /// Kademe bazli tek adimda vakumlanabilen maksimum voksel parti kapasitesi: T1=1 .. T5=125.
        /// </summary>
        public static int GetBatchCapacityForTier(int crystalTier) => Math.Clamp(crystalTier, 1, 5) switch
        {
            1 => 1,
            2 => 8,
            3 => 27,
            4 => 64,
            _ => 125
        };

        /// <summary>
        /// Kademe bazli maksimum kazma/vakum kure firca yaricapi (metre): T1=0.50m .. T5=3.50m.
        /// </summary>
        public static float GetBrushRadiusForTier(int crystalTier) => Math.Clamp(crystalTier, 1, 5) switch
        {
            1 => 0.50f,
            2 => 1.15f,
            3 => 1.85f,
            4 => 2.65f,
            _ => 3.50f
        };

        /// <summary>
        /// Kademe bazli Dogal Arazi Sacilim Fircasi (ShapeId=0) yatay yaricapi (1.5m .. 3.5m).
        /// </summary>
        public static float GetNaturalScatterRadiusXZForTier(int crystalTier) => Math.Clamp(crystalTier, 1, 5) switch
        {
            1 => 1.50f,
            2 => 2.00f,
            3 => 2.50f,
            4 => 3.00f,
            _ => 3.50f
        };

        /// <summary>
        /// Kademe bazli Dogal Arazi Sacilim Fircasi dikey elipsoid yaricapi.
        /// </summary>
        public static float GetNaturalScatterRadiusYForTier(int crystalTier) =>
            MathF.Max(1.15f, 0.65f * GetNaturalScatterRadiusXZForTier(crystalTier));

        /// <summary>
        /// Rezonans Kristali (T2..T5) ile Madde Kristali kademesini yukseltir (1..5 clamp).
        /// </summary>
        public bool UpgradeTierWithResonance(int crystalTier, CrystalMassStorage? storage = null)
        {
            byte clampedTarget = (byte)Math.Clamp(crystalTier, 1, 5);
            byte current = storage != null ? storage.CrystalTier : _crystalTier;
            if (crystalTier < 2 || clampedTarget <= current)
            {
                _crystalTier = (byte)Math.Clamp((int)_crystalTier, 1, 5);
                return false;
            }

            _crystalTier = clampedTarget;
            storage?.SetTierAndCanonicalCapacity(clampedTarget);
            return true;
        }

        /// <summary>
        /// Dunya merkezinden (0,0) yatay mesafeye ve canlinin Boss olup olmamasina gore
        /// dusecek Rezonans Kristali kademesini (2..5) belirler.
        /// Guvenli baslangic vadisi icinde (r &lt; 75m) asla Rezonans Kristali dusmez (0 doner).
        /// </summary>
        public static byte DetermineResonanceCrystalDropTier(float distanceFromOriginMeters, bool isBoss = false)
        {
            if (float.IsNaN(distanceFromOriginMeters) || distanceFromOriginMeters < SafeStarterZoneRadiusMeters)
            {
                return 0;
            }

            int ringTier;
            if (distanceFromOriginMeters < Ring2OuterRadiusMeters)
            {
                ringTier = 2; // Ring 2 (75m .. 180m)
            }
            else if (distanceFromOriginMeters < Ring3OuterRadiusMeters)
            {
                ringTier = 3; // Ring 3 (180m .. 320m)
            }
            else if (distanceFromOriginMeters < Ring4OuterRadiusMeters)
            {
                ringTier = 4; // Ring 4 (320m .. 500m)
            }
            else
            {
                ringTier = 5; // Ring 5+ (>= 500m)
            }

            if (isBoss)
            {
                ringTier = Math.Min(5, ringTier + 1);
            }

            return (byte)Math.Clamp(ringTier, 2, 5);
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
        /// Paket 2 (IP-3) — Dogal Dairesel Arazi Serpistirme Fircasi (Natural Scatter Brush) yogunluk profili:
        /// Basik elipsoidal mesafe + deterministik 3B uzaysal gurultu ile merkezde 127, kenarlarda >= 72 (> SdfIsoLevel=64)
        /// yumusak gecisli SurfaceNets yogunlugu doner. Yaricap disindaki hucreler icin 0 doner.
        /// </summary>
        public static byte ComputeNaturalScatterTargetDensity(
            int wx,
            int wy,
            int wz,
            float centerX,
            float centerY,
            float centerZ,
            float radiusXZ,
            float radiusY)
        {
            if (radiusXZ <= 0.2f || radiusY <= 0.2f) return 0;

            float dx = (wx + 0.5f) - centerX;
            float dy = (wy + 0.5f) - centerY;
            float dz = (wz + 0.5f) - centerZ;

            float normDistSq = (dx * dx + dz * dz) / (radiusXZ * radiusXZ) + (dy * dy) / (radiusY * radiusY);
            if (normDistSq > 1.26f) return 0;

            float u = MathF.Sqrt(normDistSq);
            // Deterministik 3B hash gurultusu [-0.12 .. +0.12]: mukemmel yapay kure gorunumunu kirip dogal tepecik uretir
            uint h = (uint)(wx * 73856093) ^ (uint)(wy * 19349663) ^ (uint)(wz * 83492791);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            float organicJitter = ((h & 0xFFu) / 255.0f - 0.5f) * 0.24f;

            // Merkez cekirdekte (u < 0.35) gurultuyu bastir ki tam merkez her zaman dolgun kalsin
            float jitterWeight = Math.Clamp((u - 0.25f) / 0.65f, 0f, 1f);
            float uPerturbed = u + organicJitter * jitterWeight;
            if (uPerturbed > 1.0f) return 0;

            float uClamped = Math.Clamp(uPerturbed, 0f, 1f);
            float falloff = 1.0f - (uClamped * uClamped);
            int targetDens = (int)MathF.Round(72.0f + 55.0f * falloff);
            return (byte)Math.Clamp(targetDens, 72, 127);
        }

        /// <summary>
        /// Mevcut voksel yogunlugu ile Dogal Arazi Sacilim Fircasi hedef yogunlugunu yumusak gecisle birlestirir:
        /// Mevcut kati zemini asla inceltmez, bosluktan katiliga geciste SdfIsoLevel(64) ustu (>=72) sizdirmazlik garantiler.
        /// </summary>
        public static byte ComputeNaturalScatterSmoothedDensity(byte existingDensity, byte scatterTargetDensity)
        {
            if (scatterTargetDensity < StratocraftConstants.SdfIsoLevel)
            {
                return existingDensity;
            }
            if (existingDensity == 0)
            {
                return Math.Clamp(scatterTargetDensity, (byte)72, VoxelBitUtility.FullDensity);
            }
            if (existingDensity >= scatterTargetDensity)
            {
                int boosted = existingDensity + Math.Max(2, (127 - existingDensity) / 5);
                return (byte)Math.Clamp(boosted, (int)existingDensity, 127);
            }
            int blended = (scatterTargetDensity * 3 + existingDensity + 2) / 4;
            return (byte)Math.Clamp(Math.Max((int)scatterTargetDensity, blended), 72, 127);
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
