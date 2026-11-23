namespace Stratocraft.Core.Constants
{
    /// <summary>
    /// 0..255 araligindaki tum blok, 14 yeralti cevheri, mucevher, mimari blok, alasim ve ahsap MaterialID katalogu.
    /// GAME LOGIC BIBLE v7.0, C++20 StratocraftNative ve Python bake_material_lut.py ile birebir senkrondur.
    /// </summary>
    public static class MaterialIdCatalog
    {
        // ---------- Temel Dogal Bloklar (0..29) ----------
        /// <summary>Bosluk / Hava (0).</summary>
        public const byte Air = 0;

        /// <summary>Ham Tas (1).</summary>
        public const byte Stone = 1;

        /// <summary>Toprak (2).</summary>
        public const byte Dirt = 2;

        /// <summary>Cimenli Toprak (3).</summary>
        public const byte Grass = 3;

        /// <summary>Kum (4).</summary>
        public const byte Sand = 4;

        /// <summary>Kirilmaz Ana Kaya / Manto Bedrock (5 - Dunya tabani -105m).</summary>
        public const byte Bedrock = 5;

        /// <summary>Stokiyometrik Dogal Obsidyen (6 - Seviye 4 Madde Kristali ile vakumlanabilir).</summary>
        public const byte NaturalObsidian = 6;

        /// <summary>Bazalt (7).</summary>
        public const byte Basalt = 7;

        /// <summary>Kiriktas / Cobblestone (8).</summary>
        public const byte Cobblestone = 8;

        /// <summary>Cakil / Gravel (9).</summary>
        public const byte Gravel = 9;

        /// <summary>Paketlenmis Buz (10).</summary>
        public const byte PackedIce = 10;

        /// <summary>Magma Blogu (11).</summary>
        public const byte MagmaBlock = 11;

        /// <summary>Sonlu Akiskan Su Yuzeyi Gorsel Malzemesi (12).</summary>
        public const byte FiniteWaterSurface = 12;

        /// <summary>Sonlu Akiskan Erimis Lav Yuzeyi Gorsel Malzemesi (13).</summary>
        public const byte FiniteLavaSurface = 13;

        /// <summary>Dogal Mermer (14).</summary>
        public const byte Marble = 14;

        /// <summary>Yikim Sonrasi Gevsek Moloz / Rubble Voxel (15).</summary>
        public const byte LooseRubble = 15;

        /// <summary>Dogal Kil / Tortul Okyanus Muhru (16).</summary>
        public const byte Clay = 16;

        /// <summary>Magara Tasiyici Dikit-Sarkit Sutunu / Stalagnate (17).</summary>
        public const byte Stalagnate = 17;

        /// <summary>Gokyuzu Adasi Ucan Tas / Floatstone (18 - Yerçekimi bagimsiz SkyAnchor).</summary>
        public const byte Floatstone = 18;

        /// <summary>Sonlu Akiskan Korozyon Asidi Gorsel Malzemesi (19).</summary>
        public const byte FiniteAcidSurface = 19;

        /// <summary>Dogal Karstik Kirectasi / Limestone (20 - Kademe 2 Madde Kristali ile vakumlanabilir, kirec harci hammaddesi).</summary>
        public const byte Limestone = 20;

        // ---------- 14 Yeralti Cevheri (Madde Kristali Vakumundan Kesin Muaf: 30..43) ----------
        /// <summary>1. Bakir Cevheri (30).</summary>
        public const byte CopperOre = 30;

        /// <summary>2. Kalay Cevheri (31).</summary>
        public const byte TinOre = 31;

        /// <summary>3. Demir Cevheri (32).</summary>
        public const byte IronOre = 32;

        /// <summary>4. Boksit / Aluminyum Cevheri (33).</summary>
        public const byte BauxiteAluminumOre = 33;

        /// <summary>5. Gumus Cevheri (34).</summary>
        public const byte SilverOre = 34;

        /// <summary>6. Altin Cevheri (35).</summary>
        public const byte GoldOre = 35;

        /// <summary>7. Titanyum Cevheri (36 - Yalnizca -40m altinda).</summary>
        public const byte TitaniumOre = 36;

        /// <summary>8. Mithril Cevheri (37).</summary>
        public const byte MithrilOre = 37;

        /// <summary>9. Orichalcum Cevheri (38).</summary>
        public const byte OrichalcumOre = 38;

        /// <summary>10. Cehennem Demiri Cevheri (39).</summary>
        public const byte HellIronOre = 39;

        /// <summary>11. Ilahi Metal Cevheri (40).</summary>
        public const byte DivineMetalOre = 40;

        /// <summary>12. Astral Cevher / Boyut Kristali (41).</summary>
        public const byte AstralOre = 41;

        /// <summary>13. Adamantite Cevheri (42).</summary>
        public const byte AdamantiteOre = 42;

        /// <summary>14. Hiclik Metali / Void Metal Cevheri (43).</summary>
        public const byte VoidMetalOre = 43;

        // ---------- Yakit, Mucevher Damarlari ve Kozmik Maddeler (Vakumdan Kesin Muaf: 44..49 ve 52..59) ----------
        /// <summary>Komur Damari (44 - Anayasa Bolum 4.1 uyarinca Madde Kristali ile VAKUMLANAMAZ).</summary>
        public const byte Coal = 44;

        /// <summary>Karanlik Madde (45).</summary>
        public const byte DarkMatter = 45;

        /// <summary>Yildiz Cekirdegi (46).</summary>
        public const byte StarCore = 46;

        /// <summary>Mucevher Damari Baslangici (47).</summary>
        public const byte GemVeinStart = 47;

        /// <summary>Safir Kristal Damari (48).</summary>
        public const byte CrystalQuartzVein = 48;

        /// <summary>Mucevher Damari Bitisi (49).</summary>
        public const byte GemVeinEnd = 49;

        // ---------- Islenmis Metaller, Alasimlar ve Kanonik Mucevher Damarlari (50..69) ----------
        /// <summary>Bronz (50).</summary>
        public const byte Bronze = 50;

        /// <summary>Celik (51).</summary>
        public const byte Steel = 51;

        /// <summary>Kukurt Cevheri / Sulfur (52).</summary>
        public const byte SulfurOre = 52;

        /// <summary>Kuvars Kristali (53).</summary>
        public const byte QuartzGem = 53;

        /// <summary>Ametist Damari (54).</summary>
        public const byte AmethystGem = 54;

        /// <summary>Safir Damari (55).</summary>
        public const byte SapphireGem = 55;

        /// <summary>Zumrut Damari (56).</summary>
        public const byte EmeraldGem = 56;

        /// <summary>Yakut Damari / Ruby (57 - Yalnizca -45m altinda).</summary>
        public const byte RubyGem = 57;

        /// <summary>Elmas Damari (58).</summary>
        public const byte DiamondGem = 58;

        /// <summary>Kizil Elmas Damari / Red Diamond (59 - Yalnizca -50m altinda).</summary>
        public const byte RedDiamondGem = 59;

        /// <summary>3B Prosedurel Sam Celigi / Damascus Steel (60).</summary>
        public const byte DamascusSteel = 60;

        // ---------- Ahsaplar ve Organik Zanaat Malzemeleri (70..89) ----------
        /// <summary>Mese Odunu (70).</summary>
        public const byte OakWood = 70;

        /// <summary>Demir Agaci / Ironwood (71).</summary>
        public const byte Ironwood = 71;

        /// <summary>Ruh Agaci / SpiritWood (72).</summary>
        public const byte SpiritWood = 72;

        /// <summary>Yggdrasil Dali (73).</summary>
        public const byte YggdrasilBranch = 73;

        /// <summary>Taktiksel Kumas Canta Ortusu (74).</summary>
        public const byte TacticalCloth = 74;

        /// <summary>Ic Katman Deri / Dolgulu Gambeson (75).</summary>
        public const byte LeatherPadding = 75;

        /// <summary>Kemik / Dis / Boynuz (76).</summary>
        public const byte BoneIvory = 76;

        /// <summary>Kul Kurdu / Yaratik Postu (77).</summary>
        public const byte CreatureFurAsh = 77;

        /// <summary>Anka Kusu Altin-Kor Tuyu (78).</summary>
        public const byte PhoenixFeatherGold = 78;

        // ---------- Oyuncu Yapimi Mimari Yapi Bloklari (IS_ITEM_BLOCK: 120..199) ----------
        /// <summary>Oyuncu Yapimi Yontma Tas Tugla (120 - Basinc dayanimi 2.500 birim).</summary>
        public const byte StoneBrick = 120;

        /// <summary>Islenmis Ahsap Kalas Blogu (121 - Basinc dayanimi 400 birim).</summary>
        public const byte WoodPlank = 121;

        /// <summary>Guclendirilmis Celik Kiriş Blogu (122 - Basinc dayanimi 12.000 birim).</summary>
        public const byte ReinforcedSteelBlock = 122;

        /// <summary>Islenmis Obsidyen Tugla (123 - Basinc dayanimi 25.000 birim).</summary>
        public const byte ObsidianBrick = 123;

        /// <summary>Saman Cati / Thatch Blogu (124 - Basinc dayanimi 120 birim).</summary>
        public const byte ThatchTile = 124;

        /// <summary>Demir Havalandirma Mazgali / Grating (125 - Oksijen ve sivi gecirgen).</summary>
        public const byte IronGrating = 125;

        /// <summary>Gonyeli Kesme Tas / Ashlar Blogu (126 - Ekol 3/4 islenmis mimari blok, Madde Kristali vakumuna %100 bagisik).</summary>
        public const byte CutAshlarStone = 126;

        /// <summary>Islenmis Anitsal Mermer Blogu (127 - Ekol 4 sutun/kemer/kubbe mermeri, vakum bagisik).</summary>
        public const byte DressedMarbleBlock = 127;

        /// <summary>Firinlanmis Kirmizi Kil Tugla (128 - Ekol 3/4 tonoz/kubbe/baca tuglasi, vakum bagisik).</summary>
        public const byte FiredClayBrick = 128;

        /// <summary>Terracotta Pismis Kiremit Cati Blogu (129 - Su gecirmez yanmaz cati ortusu, vakum bagisik).</summary>
        public const byte RoofClayTile = 129;

        /// <summary>Guneste Kurutulmus Kerpic Tugla Blogu (130 - Ekol 1/2 yuksek isil kutleli duvar, vakum bagisik).</summary>
        public const byte AdobeBrickBlock = 130;

        /// <summary>Dal Orgu ve Camur Siva Dolgu Paneli / Wattle &amp; Daub (131 - Ekol 2 karkas dolgusu, vakum bagisik).</summary>
        public const byte WattleDaubPanelBlock = 131;

        /// <summary>Kirec Harcli Tas Yigma Duvar Blogu (132 - Ekol 3/4 harcli kagir duvar, vakum bagisik).</summary>
        public const byte LimeMortarStoneWall = 132;

        /// <summary>Soyulmus Kertikli ve Yosun/Kil Derz Dolgulu Kutuk Blogu (133 - Ekol 1 yalitimli kutuk duvar, vakum bagisik).</summary>
        public const byte ChinkedLogBlock = 133;

        /// <summary>Dovme Demir Gergi Kirisi / Kenet Blogu (134 - Ekol 4 kemer/kubbe yanal itme gergisi, vakum bagisik).</summary>
        public const byte IronTieBeamBlock = 134;

        /// <summary>Kursun Kafesli Mimari Pencere Cami (135 - Ekol 4 isik gecirgen tam ruzgar/isi yalitimi, vakum bagisik).</summary>
        public const byte LeadedGlassPane = 135;

        /// <summary>Yarma Ahsap Zona / Pedavra Cati Kaplamasi (136 - Ekol 1/2 egimli cati ortusu, vakum bagisik).</summary>
        public const byte TimberShingleRoof = 136;

        /// <summary>Kusaklama Batarya Cekirdegi (160).</summary>
        public const byte BatteryCore = 160;

        /// <summary>Parlayan Magara Yosunu (165 - Kapali magarada +O2 uretir).</summary>
        public const byte GlowingMoss = 165;

        /// <summary>Kamp Atesi / Mesale Blogu (166 - Isitir ancak kapali odada O2 tuketir).</summary>
        public const byte CampfireBlock = 166;

        /// <summary>Ilkel Tas Firin / Ocak Blogu (167 - Bakir cevheri izabesi ve kil pisirme + cevre isitma).</summary>
        public const byte PrimitiveStoneKilnBlock = 167;

        /// <summary>Kurulabilir Yatak / Yeniden Dogma Noktasi Blogu (168 - [E] ile SpawnPoint kaydeder).</summary>
        public const byte BedBlock = 168;

        /// <summary>Boss Trofe Yapi Bloklari Baslangici (180..195).</summary>
        public const byte BossTrophyBlockStart = 180;

        /// <summary>Boss Trofe Yapi Bloklari Bitisi (195).</summary>
        public const byte BossTrophyBlockEnd = 195;

        /// <summary>Zindan Kutsal Koruma Bloklari Baslangici (240..255 - OriginState=2 KaliciYapi).</summary>
        public const byte DungeonSanctumImmuneStart = 240;

        /// <summary>Zindan Kutsal Koruma Bloklari Bitisi (255).</summary>
        public const byte DungeonSanctumImmuneEnd = 255;

        /// <summary>
        /// Verilen MaterialID'nin yuksek rezonansli cevher, komur veya mucevher olup olmadigini doner.
        /// True donen bloklar Madde Kristali ile vakumlanamaz (ERR_ORE_RESONANCE_IMMUNE).
        /// Anayasa Bolum 4.1 uyarinca Komur (44) ve tum maden/mucevher damarlari dahildir.
        /// </summary>
        public static bool IsVacuumImmuneResonanceOreOrGem(byte materialId)
        {
            return (materialId >= CopperOre && materialId <= GemVeinEnd)
                || (materialId >= SulfurOre && materialId <= RedDiamondGem);
        }

        /// <summary>
        /// Verilen MaterialID'nin dogustan uretilmis esya-blok (IS_ITEM_BLOCK) kategorisinde olup olmadigini doner.
        /// </summary>
        public static bool IsCraftedItemBlockMaterial(byte materialId)
        {
            if (materialId == Bronze || materialId == Steel || materialId == DamascusSteel)
            {
                return true;
            }

            if (materialId >= TacticalCloth && materialId <= PhoenixFeatherGold)
            {
                return true;
            }

            return materialId >= StoneBrick && materialId < DungeonSanctumImmuneStart && materialId != GlowingMoss;
        }

        /// <summary>
        /// <see cref="IsCraftedItemBlockMaterial"/> icin kisa yardimci esanlamli metot.
        /// </summary>
        public static bool IsCraftedItemBlock(byte materialId) => IsCraftedItemBlockMaterial(materialId);

        /// <summary>
        /// Karar A3 uyarinca verilen MaterialID'nin [C] Madde Kristali vakumuna %100 bagisik olup olmadigini doner.
        /// Tum islenmis esya-bloklar (120..199, 126..136 dahil), cevher/mucevher damarlari, Bedrock ve Zindan bloklari bagisiktir.
        /// </summary>
        public static bool IsCrystalVacuumImmune(byte materialId)
        {
            return GetRequiredCrystalTierForTerrain(materialId) == 0;
        }

        /// <summary>
        /// Ham arazi blogunu Madde Kristali ile vakumlamak icin gereken minimum Kristal Kademesini (1..4) doner.
        /// Vakumlanamayan (Hava, Bedrock, Cevher, Komur, Mucevher, Esya-Blok, Zindan) malzemeler icin 0 doner.
        /// Dogal Obsidyen (6) Anayasa Bolum 2.7.D.2 ve 4.1 uyarinca Kademe 4 Kristal gerektirir.
        /// </summary>
        public static byte GetRequiredCrystalTierForTerrain(byte materialId)
        {
            if (materialId == Air ||
                materialId == Bedrock ||
                materialId == FiniteWaterSurface ||
                materialId == FiniteLavaSurface ||
                materialId == FiniteAcidSurface ||
                materialId >= DungeonSanctumImmuneStart ||
                IsVacuumImmuneResonanceOreOrGem(materialId) ||
                IsCraftedItemBlockMaterial(materialId))
            {
                return 0;
            }

            return materialId switch
            {
                Dirt or Grass or Sand or Gravel or Clay or LooseRubble or GlowingMoss => 1,
                Stone or Cobblestone or Stalagnate or Limestone or OakWood or Ironwood or SpiritWood or YggdrasilBranch => 2,
                PackedIce or Marble or Floatstone => 3,
                Basalt or MagmaBlock or NaturalObsidian => 4,
                _ => 2
            };
        }

        /// <summary>
        /// Verilen MaterialID'nin elektriksel ve isil olarak iletken bir metal, alasim veya metalik cevher olup olmadigini doner.
        /// Paket 4B (IP-6 Yildirim hedef agirligi ve IP-8 Metal Termal Kopru) tarafindan kullanilir.
        /// </summary>
        public static bool IsMetallicConductive(byte materialId)
        {
            return materialId == CopperOre ||
                   materialId == TinOre ||
                   materialId == IronOre ||
                   materialId == BauxiteAluminumOre ||
                   materialId == SilverOre ||
                   materialId == GoldOre ||
                   materialId == TitaniumOre ||
                   materialId == MithrilOre ||
                   materialId == OrichalcumOre ||
                   materialId == HellIronOre ||
                   materialId == DivineMetalOre ||
                   materialId == AdamantiteOre ||
                   materialId == VoidMetalOre ||
                   materialId == Bronze ||
                   materialId == Steel ||
                   materialId == DamascusSteel ||
                   materialId == ReinforcedSteelBlock ||
                   materialId == IronGrating ||
                   materialId == IronTieBeamBlock ||
                   materialId == BatteryCore;
        }

        /// <summary>
        /// Verilen MaterialID'nin dogustan yuksek sicaklikli akkor (Stefan-Boltzmann T^4 Blackbody >= 170) kaynagi olup olmadigini doner.
        /// </summary>
        public static bool IsIntrinsicHotEmissiveMaterial(byte materialId)
        {
            return materialId == FiniteLavaSurface ||
                   materialId == MagmaBlock ||
                   materialId == CampfireBlock ||
                   materialId == PrimitiveStoneKilnBlock ||
                   materialId == StarCore;
        }

        /// <summary>
        /// Verilen MaterialID'nin kis giysisi veya metal zirh ic astari olarak kullanilabilen yalitim malzemesi olup olmadigini doner.
        /// </summary>
        public static bool IsThermalInsulatingLinerMaterial(byte materialId)
        {
            return materialId == TacticalCloth ||
                   materialId == LeatherPadding ||
                   materialId == CreatureFurAsh ||
                   materialId == PhoenixFeatherGold;
        }

        /// <summary>
        /// Verilen MaterialID icin tahsissiz (0-GC string literal) okunabilir Turkce malzeme adini doner.
        /// </summary>
        public static string GetDisplayName(byte materialId) => materialId switch
        {
            Air => "Hava",
            Stone => "Ham Taş",
            Dirt => "Toprak",
            Grass => "Çimenli Toprak",
            Sand => "Kum",
            Bedrock => "Ana Kaya (Bedrock)",
            NaturalObsidian => "Doğal Obsidyen",
            Basalt => "Bazalt",
            Cobblestone => "Kırıktaş",
            Gravel => "Çakıl",
            PackedIce => "Paketlenmiş Buz",
            MagmaBlock => "Magma Bloğu",
            FiniteWaterSurface => "Su",
            FiniteLavaSurface => "Lav",
            Marble => "Doğal Mermer",
            LooseRubble => "Gevşek Moloz",
            Clay => "Doğal Kil",
            Stalagnate => "Dikit-Sarkıt Sütunu",
            Floatstone => "Uçantaş (Floatstone)",
            FiniteAcidSurface => "Korozyon Asidi",
            Limestone => "Kireçtaşı",
            CopperOre => "Bakır Cevheri",
            TinOre => "Kalay Cevheri",
            IronOre => "Demir Cevheri",
            BauxiteAluminumOre => "Boksit Cevheri",
            SilverOre => "Gümüş Cevheri",
            GoldOre => "Altın Cevheri",
            TitaniumOre => "Titanyum Cevheri",
            MithrilOre => "Mithril Cevheri",
            OrichalcumOre => "Orichalcum Cevheri",
            HellIronOre => "Cehennem Demiri Cevheri",
            DivineMetalOre => "İlahi Metal Cevheri",
            AstralOre => "Astral Cevher",
            AdamantiteOre => "Adamantite Cevheri",
            VoidMetalOre => "Hiçlik Metali Cevheri",
            Coal => "Kömür Damarı",
            DarkMatter => "Karanlık Madde",
            StarCore => "Yıldız Çekirdeği",
            GemVeinStart => "Mücevher Damarı",
            CrystalQuartzVein => "Kristal Kuvars Damarı",
            GemVeinEnd => "Derin Mücevher Damarı",
            Bronze => "Bronz",
            Steel => "Çelik",
            SulfurOre => "Kükürt Cevheri",
            QuartzGem => "Kuvars Kristali",
            AmethystGem => "Ametist",
            SapphireGem => "Safir",
            EmeraldGem => "Zümrüt",
            RubyGem => "Yakut",
            DiamondGem => "Elmas",
            RedDiamondGem => "Kızıl Elmas",
            DamascusSteel => "Şam Çeliği",
            OakWood => "Meşe Odunu",
            Ironwood => "Demir Ağacı",
            SpiritWood => "Ruh Ağacı",
            YggdrasilBranch => "Yggdrasil Dalı",
            TacticalCloth => "Dokuma Kumaş",
            LeatherPadding => "Dolgulu Deri",
            BoneIvory => "Kemik / Fildişi",
            CreatureFurAsh => "Yaratık Postu",
            PhoenixFeatherGold => "Anka Tüyü",
            StoneBrick => "Yontma Taş Tuğla",
            WoodPlank => "Ahşap Kalas",
            ReinforcedSteelBlock => "Güçlendirilmiş Çelik Kiriş",
            ObsidianBrick => "İşlenmiş Obsidyen Tuğla",
            ThatchTile => "Saz Çatı",
            IronGrating => "Demir Mazgal",
            CutAshlarStone => "Kesme Taş (Ashlar)",
            DressedMarbleBlock => "İşlenmiş Mermer Blok",
            FiredClayBrick => "Pişmiş Tuğla",
            RoofClayTile => "Terracotta Kiremit",
            AdobeBrickBlock => "Kerpiç Tuğla",
            WattleDaubPanelBlock => "Dal Örgü ve Çamur Sıva",
            LimeMortarStoneWall => "Kireç Harçlı Taş Duvar",
            ChinkedLogBlock => "Derz Dolgulu Kertikli Kütük",
            IronTieBeamBlock => "Demir Gergi Kirişi",
            LeadedGlassPane => "Kurşunlu Mimari Cam",
            TimberShingleRoof => "Ahşap Zona Çatı",
            BatteryCore => "Batarya Çekirdeği",
            GlowingMoss => "Parlayan Mağara Yosunu",
            CampfireBlock => "Kamp Ateşi",
            PrimitiveStoneKilnBlock => "İlkel Taş Fırın",
            BedBlock => "Yatak",
            >= BossTrophyBlockStart and <= BossTrophyBlockEnd => "Boss Trofe Bloğu",
            >= DungeonSanctumImmuneStart => "Kadim Zindan Taşı",
            _ => "Bilinmeyen Malzeme"
        };
    }
}
