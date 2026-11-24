#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.WorldGen.Generators
{
    /// <summary>
    /// Faz D2 (DK-02): %100 Voksel 4x4x4 (0.25m cozunurluk, ShapeID = 63, MicroVoxelPool4096) ic mekan mobilyalari,
    /// pencereler, korkuluklar, kuyu cikrigi, tarla citi ve [E] ile acilip kapanabilen mentese kapilarin
    /// 64-bit kanonik alt-voksel maske sablonlari.
    ///
    /// Bit indeksleme (MicroVoxelPool4096.GetMicroSubIndex64 ile birebir ayni):
    ///   bit = ((my &amp; 3) &lt;&lt; 4) | ((mz &amp; 3) &lt;&lt; 2) | (mx &amp; 3)
    ///   - my = 0 (bit 0..15)  : Zemin temas katmani (0.00m .. 0.25m, masa/sandalye ayaklari ve tabanlar)
    ///   - my = 1 (bit 16..31) : Oturma / alt govde katmani (0.25m .. 0.50m)
    ///   - my = 2 (bit 32..47) : Orta govde / yatak / tezgah katmani (0.50m .. 0.75m)
    ///   - my = 3 (bit 48..63) : Masa tablası / sirtlik / ust kiris katmani (0.75m .. 1.00m)
    ///
    /// Her sablon hem 0 &lt; PopCount &lt; 64 kosulunu (Auto-Compact korumasi) hem de
    /// ComputeVerticalBottleneckCrossSection16 &gt;= 2 kosulunu (dikey darbogaz yuk tasima butunlugu) saglar.
    /// </summary>
    public static class VoxelMicroFurnitureTemplates
    {
        // =====================================================================
        // 1) IC MEKAN MOBILYALARI (%100 VOKSEL 4x4x4, ShapeID = 63)
        // =====================================================================

        /// <summary>Yemek Masasi: my=0..2 4 kose bacak (0x9009), my=3 tam 4x4 masa tablasi (0xFFFF).</summary>
        public const ulong DiningTable64 = 0xFFFF_9009_9009_9009UL;

        /// <summary>Calisma / Atolye Masasi: my=0 kose ayaklar, my=1 alt ara kayit (0x9999), my=2 cekmece kasasi (0xFF99), my=3 kalin tabla (0xFFFF).</summary>
        public const ulong WorkTable64 = 0xFFFF_FF99_9999_9009UL;

        /// <summary>Sirtlikli Sandalye (+Z sirtlik, -Z on): my=0 4 ayak (0x9009), my=1 oturak (0xFFFF), my=2..3 sirtlik dikmeleri ve kayiti (0xF000 / 0x9000).</summary>
        public const ulong ChairWithBack64 = 0xF000_9000_FFFF_9009UL;

        /// <summary>Tabure: my=0..1 4 merkez-ic ayak (0x0660 / 0x9009), my=2 genis oturak (0x6FF6), my=3 minder gobegi (0x0660).</summary>
        public const ulong Stool64 = 0x0660_6FF6_0660_9009UL;

        /// <summary>Yatak Basligi (+Z baslik duvar tarafi): my=0 4 karyola ayagi, my=1 tam dosek (0xFFFF), my=2 yastik + baslik (0xFF66), my=3 oymali baslik taci (0xF000).</summary>
        public const ulong BedHeadboard64 = 0xF000_FF66_FFFF_9009UL;

        /// <summary>Yatak Ayak Ucu (-Z ayak ucu): my=0 4 karyola ayagi, my=1 tam dosek/yorgan (0xFFFF), my=2 yorgan + ayak ucu tahtasi (0x660F), my=3 kose topuzlari (0x0009).</summary>
        public const ulong BedFoot64 = 0x0009_660F_FFFF_9009UL;

        /// <summary>Kitaplik / Raf (+Z sirt): my=0..3 arka sirt + yan dikmeler + raf katlari ve kitap siralari (52/64 dolu, on cephede girintili kitap raflari).</summary>
        public const ulong Bookshelf64 = 0xFFFF_F990_FFFF_FF99UL;

        /// <summary>Depo Sandigi: my=0 kizak ayaklar (0x9999), my=1..2 sandik govdesi (0xFFFF / 0x6FF6), my=3 bombeli kapak ve on demir toka (0x6FF6).</summary>
        public const ulong StorageChest64 = 0x0FF0_FFFF_6FF6_9009UL;

        /// <summary>Ahsap Fici / Varil: my=0 ve my=3 dairesel cember taban/kapak (0x6FF6), my=1..2 genis gobek (0xFFFF) -> 56/64 silindirik varil!</summary>
        public const ulong Barrel64 = 0x6FF6_FFFF_FFFF_6FF6UL;

        /// <summary>Mutfak Tezgahi: my=0 baza (0xFFF0), my=1..2 kapakli dolap govdesi (0xFFF9), my=3 tam tezgah mermeri/ahsap tablasi (0xFFFF).</summary>
        public const ulong KitchenCounter64 = 0xFFFF_FFF9_FFF9_FFF0UL;

        /// <summary>Somine / Pisirme Ocagi (-Z on acik ates agzi, +Z arka/yan tas duvarlar): my=0 tas taban (0xFFFF), my=1..2 U-bicimli ates haznesi (0xF999), my=3 duman davlumbazi lentosu (0xFFFF).</summary>
        public const ulong CookingHearth64 = 0xFFFF_F999_F999_FFFFUL;

        /// <summary>Kraliyet Tahti (+Z yuksek arkalik, yan kolcaklar): my=0 basamakli kaide (0xFFFF), my=1 genis taht oturagi (0xFFFF), my=2 yan kolcaklar + sirtlik (0xF990), my=3 tacli sirtlik (0xF600).</summary>
        public const ulong RoyalThrone64 = 0xF600_F990_FFFF_FFFFUL;

        /// <summary>Silahlik / Mizrak Standi: my=0 agir taban (0x9FF9), my=1..2 dikey mizrak/kilic saplari ve yan dikmeler (0x0BD0), my=3 ust baglanti barasi (0x0FF0).</summary>
        public const ulong WeaponRack64 = 0x0FF0_0BD0_0BD0_9FF9UL;

        /// <summary>Demirci Orsu ve Kutugu: my=0 genis kutuk tabani (0x6FF6), my=1 kutuk govdesi (0x0660), my=2 dar ors beli (0x0660), my=3 uzamis ors boynuzu ve tablasi (0x0FF6).</summary>
        public const ulong AnvilPedestal64 = 0x0FF6_0660_0660_6FF6UL;

        /// <summary>Marangoz Testere Sehpasi (SawhorseBench): my=0..1 capraz A-ayaklar (0x9009 / 0x0990), my=2 uzun calisma kirisi (0x0FF0), my=3 iskence takozlari (0x0900).</summary>
        public const ulong SawhorseBench64 = 0x0900_0FF0_0990_9009UL;

        // =====================================================================
        // 2) MIMARI MIKRO-DETAYLAR & MENTESE KAPILAR (ShapeID = 63)
        // =====================================================================

        /// <summary>Mentese Kapi Alt Kanat (Kapali, mz=0 on yuzde 0.25m kanat + mx=0,mz=1 mentese pimi + mx=3,mz=1 kapi kolu).</summary>
        public const ulong HingedDoorLowerClosed64 = 0x008F_000F_000F_001FUL;

        /// <summary>Mentese Kapi Ust Kanat (Kapali, mz=0 on yuzde gozetleme pencereli ust kanat + mx=0,mz=1 ust mentese pimi).</summary>
        public const ulong HingedDoorUpperClosed64 = 0x000F_0019_0009_000FUL;

        /// <summary>Mentese Kapi Alt Kanat (Acik, mx=0 mentese ekseni etrafinda 90 derece iceri acilmis kanat + mentese/kol pimi).</summary>
        public const ulong HingedDoorLowerOpen64 = 0x3111_1111_1111_1113UL;

        /// <summary>Mentese Kapi Ust Kanat (Acik, mx=0 mentese ekseni etrafinda 90 derece iceri acilmis pencereli ust kanat).</summary>
        public const ulong HingedDoorUpperOpen64 = 0x1111_1003_1001_1111UL;

        /// <summary>Kayitli Pencere Kasasi (MullionWindow64): dis cerceve + arti (+) orta kayit, 4 isik gozu acik.</summary>
        public const ulong MullionWindow64 = 0x0FF0_0990_0FF0_0FF0UL;

        /// <summary>Kepenkli Pencere (ShutterWindow64): ust/alt denizlik + iki yanda disa acilmis ahsap panjur kanatlari.</summary>
        public const ulong ShutterWindow64 = 0x0FF0_9999_9999_0FF0UL;

        /// <summary>Balkon / Galeri Korkulugu (BalconyRailing64): mz=0 kenarinda kose ve orta korkuluk babalari + ust kupeste.</summary>
        public const ulong BalconyRailing64 = 0x000F_0009_0009_000FUL;

        /// <summary>Tarla / Bahce Citi (FencePostAndRail64): orta dikme + yatay alt/ust cit kayitlari.</summary>
        public const ulong FencePostAndRail64 = 0x0060_0FF0_0060_0FF0UL;

        /// <summary>Kuyu Cikrigi Cercevesi (WellWindlassFrame64): iki yan tas/ahsap dikme + ust cikrik mili ve kolu.</summary>
        public const ulong WellWindlassFrame64 = 0x9FF9_0990_0990_9009UL;

        private static readonly ulong[] LowerClosedRotations =
        {
            HingedDoorLowerClosed64,
            RotateMaskY(HingedDoorLowerClosed64, 1),
            RotateMaskY(HingedDoorLowerClosed64, 2),
            RotateMaskY(HingedDoorLowerClosed64, 3)
        };

        private static readonly ulong[] UpperClosedRotations =
        {
            HingedDoorUpperClosed64,
            RotateMaskY(HingedDoorUpperClosed64, 1),
            RotateMaskY(HingedDoorUpperClosed64, 2),
            RotateMaskY(HingedDoorUpperClosed64, 3)
        };

        private static readonly ulong[] LowerOpenRotations =
        {
            HingedDoorLowerOpen64,
            RotateMaskY(HingedDoorLowerOpen64, 1),
            RotateMaskY(HingedDoorLowerOpen64, 2),
            RotateMaskY(HingedDoorLowerOpen64, 3)
        };

        private static readonly ulong[] UpperOpenRotations =
        {
            HingedDoorUpperOpen64,
            RotateMaskY(HingedDoorUpperOpen64, 1),
            RotateMaskY(HingedDoorUpperOpen64, 2),
            RotateMaskY(HingedDoorUpperOpen64, 3)
        };

        /// <summary>
        /// 4x4x4 alt-voksel maskesini Y ekseni etrafinda 90 derecelik adimlarla (quarterTurns = 0..3) dondurur.
        /// ArchitecturalShapeCatalog (rot 0..3) ile birebir ayni yonelim kuralini izler:
        /// quarterTurns=1 icin (mx, mz) -&gt; (mz, 3 - mx).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateMaskY(ulong mask, int quarterTurns)
        {
            int turns = quarterTurns & 3;
            if (turns == 0 || mask == 0UL || mask == MicroVoxelPool4096.FullCubeMask)
            {
                return mask;
            }

            ulong result = 0UL;
            for (int my = 0; my < 4; my++)
            {
                int yShift = my << 4;
                uint slice = (uint)((mask >> yShift) & 0xFFFFUL);
                if (slice == 0u)
                {
                    continue;
                }

                ulong rotSlice = 0UL;
                for (int mz = 0; mz < 4; mz++)
                {
                    for (int mx = 0; mx < 4; mx++)
                    {
                        int srcBit = (mz << 2) | mx;
                        if (((slice >> srcBit) & 1u) == 0u)
                        {
                            continue;
                        }

                        int nx;
                        int nz;
                        switch (turns)
                        {
                            case 1:
                                nx = mz;
                                nz = 3 - mx;
                                break;
                            case 2:
                                nx = 3 - mx;
                                nz = 3 - mz;
                                break;
                            default:
                                nx = 3 - mz;
                                nz = mx;
                                break;
                        }

                        rotSlice |= 1UL << ((nz << 2) | nx);
                    }
                }

                result |= rotSlice << yShift;
            }

            return result;
        }

        /// <summary>
        /// Verilen 64-bit maskenin kapali veya acik bir mentese kapi maskesi olup olmadigini cozer.
        /// </summary>
        public static bool TryClassifyDoorMask(
            ulong mask64,
            out bool isUpperHalf,
            out bool isOpen,
            out int quarterTurns)
        {
            for (int r = 0; r < 4; r++)
            {
                if (mask64 == LowerClosedRotations[r])
                {
                    isUpperHalf = false;
                    isOpen = false;
                    quarterTurns = r;
                    return true;
                }
                if (mask64 == UpperClosedRotations[r])
                {
                    isUpperHalf = true;
                    isOpen = false;
                    quarterTurns = r;
                    return true;
                }
                if (mask64 == LowerOpenRotations[r])
                {
                    isUpperHalf = false;
                    isOpen = true;
                    quarterTurns = r;
                    return true;
                }
                if (mask64 == UpperOpenRotations[r])
                {
                    isUpperHalf = true;
                    isOpen = true;
                    quarterTurns = r;
                    return true;
                }
            }

            isUpperHalf = false;
            isOpen = false;
            quarterTurns = 0;
            return false;
        }

        /// <summary>
        /// Verilen (localX, worldY, localZ) noktasindaki ShapeID == 63 mentese kapi mikro-vokselini (ve ust/alt esini)
        /// Kapali64 &lt;-&gt; Acik64 arasinda temizce degistirir (DK-02 sifir .glb kapi etkilesimi).
        /// </summary>
        public static bool TryToggleDoorMicroVoxel(
            uint[] voxels,
            MicroVoxelPool4096?[]? microPools,
            int localX,
            int worldY,
            int localZ,
            out bool isNowOpen)
        {
            isNowOpen = false;
            if (voxels == null ||
                microPools == null ||
                (uint)localX >= 16u ||
                (uint)localZ >= 16u ||
                worldY < 1)
            {
                return false;
            }

            int flatIdx = (worldY << 8) | (localZ << 4) | localX;
            if ((uint)flatIdx >= (uint)voxels.Length)
            {
                return false;
            }

            uint raw = voxels[flatIdx];
            if (VoxelBitUtility.GetMaterialId(raw) == MaterialIdCatalog.Air ||
                VoxelBitUtility.GetShapeId(raw) != VoxelBitUtility.ChiseledMicroVoxelShapeId)
            {
                return false;
            }

            int subY = worldY >> 4;
            if ((uint)subY >= (uint)microPools.Length)
            {
                return false;
            }

            MicroVoxelPool4096? pool = microPools[subY];
            if (pool == null || !pool.IsAllocated)
            {
                return false;
            }

            ushort slot12 = VoxelBitUtility.GetMicroPoolIndex(raw);
            ulong currentMask = pool.GetMask(slot12);
            if (!TryClassifyDoorMask(currentMask, out bool isUpperHalf, out bool wasOpen, out int quarterTurns))
            {
                return false;
            }

            bool targetOpen = !wasOpen;
            ulong nextSelfMask = isUpperHalf
                ? (targetOpen ? UpperOpenRotations[quarterTurns] : UpperClosedRotations[quarterTurns])
                : (targetOpen ? LowerOpenRotations[quarterTurns] : LowerClosedRotations[quarterTurns]);

            WriteMicroMaskToCell(voxels, microPools, localX, worldY, localZ, raw, nextSelfMask);

            // Dikey es kapi blogunu da (alt ise worldY + 1, ust ise worldY - 1) senkron cevir:
            int partnerY = isUpperHalf ? (worldY - 1) : (worldY + 1);
            if (partnerY >= 1)
            {
                int partnerFlat = (partnerY << 8) | (localZ << 4) | localX;
                if ((uint)partnerFlat < (uint)voxels.Length)
                {
                    uint partnerRaw = voxels[partnerFlat];
                    int partnerSubY = partnerY >> 4;
                    if (VoxelBitUtility.GetMaterialId(partnerRaw) != MaterialIdCatalog.Air &&
                        VoxelBitUtility.GetShapeId(partnerRaw) == VoxelBitUtility.ChiseledMicroVoxelShapeId &&
                        (uint)partnerSubY < (uint)microPools.Length &&
                        microPools[partnerSubY] != null)
                    {
                        ushort partnerSlot = VoxelBitUtility.GetMicroPoolIndex(partnerRaw);
                        ulong partnerMask = microPools[partnerSubY]!.GetMask(partnerSlot);
                        if (TryClassifyDoorMask(partnerMask, out bool partnerUpper, out _, out int partnerTurns) &&
                            partnerUpper != isUpperHalf)
                        {
                            ulong nextPartnerMask = partnerUpper
                                ? (targetOpen ? UpperOpenRotations[partnerTurns] : UpperClosedRotations[partnerTurns])
                                : (targetOpen ? LowerOpenRotations[partnerTurns] : LowerClosedRotations[partnerTurns]);
                            WriteMicroMaskToCell(voxels, microPools, localX, partnerY, localZ, partnerRaw, nextPartnerMask);
                        }
                    }
                }
            }

            isNowOpen = targetOpen;
            return true;
        }

        /// <summary>
        /// Verilen chunk icindeki (localX, worldY, localZ) hucresine 4x4x4 mikro-voksel maskesini yazar;
        /// microPools == null ise istenen mimari fallback ShapeID ile temiz kati blok yazar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StampMicroOrFallback(
            uint[] voxels,
            MicroVoxelPool4096?[]? microPools,
            int localX,
            int worldY,
            int localZ,
            byte materialId,
            ulong mask64,
            byte fallbackShapeId = ArchitecturalShapeCatalog.HalfSlab,
            byte fallbackRotation = 0,
            BlokKokenDurumu originState = BlokKokenDurumu.VoxelLockedAtomic)
        {
            if ((uint)localX >= 16u || (uint)localZ >= 16u || worldY < 1)
            {
                return;
            }

            int flatIdx = (worldY << 8) | (localZ << 4) | localX;
            if ((uint)flatIdx >= (uint)voxels.Length)
            {
                return;
            }

            if (materialId == MaterialIdCatalog.Air || mask64 == 0UL)
            {
                voxels[flatIdx] = 0u;
                return;
            }

            int subY = worldY >> 4;
            if (microPools != null && (uint)subY < (uint)microPools.Length && mask64 != MicroVoxelPool4096.FullCubeMask)
            {
                MicroVoxelPool4096 pool = microPools[subY] ??= new MicroVoxelPool4096();
                ushort slot12 = MicroVoxelPool4096.ComputeLocalIndex12(localX, worldY & 15, localZ);
                pool.AllocateSlot(slot12, mask64);
                voxels[flatIdx] = VoxelBitUtility.PackChiseled(materialId, slot12, originState, dmg4: 0);
                return;
            }

            byte resolvedShape = mask64 == MicroVoxelPool4096.FullCubeMask
                ? ArchitecturalShapeCatalog.FullMasonryCube
                : fallbackShapeId;
            voxels[flatIdx] = VoxelBitUtility.PackStandard(
                materialId,
                VoxelBitUtility.FullDensity,
                resolvedShape,
                fallbackRotation,
                originState,
                dmg4: 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteMicroMaskToCell(
            uint[] voxels,
            MicroVoxelPool4096?[] microPools,
            int localX,
            int worldY,
            int localZ,
            uint existingRaw,
            ulong newMask)
        {
            int subY = worldY >> 4;
            MicroVoxelPool4096 pool = microPools[subY] ??= new MicroVoxelPool4096();
            ushort slot12 = MicroVoxelPool4096.ComputeLocalIndex12(localX, worldY & 15, localZ);
            pool.AllocateSlot(slot12, newMask);
            voxels[(worldY << 8) | (localZ << 4) | localX] = VoxelBitUtility.PackChiseled(
                VoxelBitUtility.GetMaterialId(existingRaw),
                slot12,
                VoxelBitUtility.GetOriginState(existingRaw),
                VoxelBitUtility.GetDamageState(existingRaw));
        }
    }
}
