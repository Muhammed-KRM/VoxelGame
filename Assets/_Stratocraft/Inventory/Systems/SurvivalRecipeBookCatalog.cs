#nullable enable
using System;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// [B] Hayatta Kalma Tarif ve Malzeme Rehber Kitabindaki 4 ana kategori sekmesi.
    /// </summary>
    public enum SurvivalRecipeBookTab : byte
    {
        MaterialsAndResources = 0,
        ClothAndKnappingRecipes = 1,
        MindBuildingStructures = 2,
        SmithingCookingAndCrystal = 3
    }

    /// <summary>
    /// Rehber kitabindaki tek bir malzeme, uretim tarifi, zihin insasi yapisi veya adim adim islem kaydi.
    /// </summary>
    public readonly struct SurvivalRecipeBookEntry
    {
        public readonly string Id;
        public readonly SurvivalRecipeBookTab Tab;
        public readonly string CategoryBadge;
        public readonly string Title;
        public readonly string IngredientsOrSource;
        public readonly string ToolOrKeybind;
        public readonly string StepByStepGuide;

        public string Name => Title;
        public string Description => StepByStepGuide;
        public string RecipeOrLocation => IngredientsOrSource;
        public string KeybindHint => ToolOrKeybind;

        public SurvivalRecipeBookEntry(
            string id,
            SurvivalRecipeBookTab tab,
            string categoryBadge,
            string title,
            string ingredientsOrSource,
            string toolOrKeybind,
            string stepByStepGuide)
        {
            Id = id;
            Tab = tab;
            CategoryBadge = categoryBadge;
            Title = title;
            IngredientsOrSource = ingredientsOrSource;
            ToolOrKeybind = toolOrKeybind;
            StepByStepGuide = stepByStepGuide;
        }
    }

    /// <summary>
    /// Rehber kitabindaki bir sekmenin baslik, aciklama ve giris listesi tanimi.
    /// </summary>
    public readonly struct SurvivalRecipeBookTabDefinition
    {
        public readonly SurvivalRecipeBookTab Tab;
        public readonly int TabIndex;
        public readonly string Title;
        public readonly string ShortTabLabel;
        public readonly string Subtitle;
        public readonly SurvivalRecipeBookEntry[] Entries;

        public SurvivalRecipeBookTabDefinition(
            SurvivalRecipeBookTab tab,
            int tabIndex,
            string title,
            string shortTabLabel,
            string subtitle,
            SurvivalRecipeBookEntry[] entries)
        {
            Tab = tab;
            TabIndex = tabIndex;
            Title = title;
            ShortTabLabel = shortTabLabel;
            Subtitle = subtitle;
            Entries = entries;
        }
    }

    /// <summary>
    /// Paket 2 [B] Hayatta Kalma Rehber Kitabi — 4 sekmeli, saf C# (.NET 10 ve Unity 6 uyumlu)
    /// sifir-GC Turkce malzeme, [I]/[R] craft tarifleri, [T] zihin insasi yapilari ve demircilik/pisirme/kristal katalogu.
    /// </summary>
    public static class SurvivalRecipeBookCatalog
    {
        public const int TabCount = 4;

        private static readonly string[] CachedTabTitles =
        {
            "Malzemeler ve Kaynaklar",
            "[I] / [R] Craft Tarifleri",
            "[T] Zihin İnşası Yapıları",
            "Demircilik, Pişirme & Kristal"
        };

        private static readonly string[] CachedShortTabLabels =
        {
            "[1] Malzemeler ve Kaynaklar",
            "[2] [I] / [R] Craft Tarifleri",
            "[3] [T] Zihin İnşası Yapıları",
            "[4] Demircilik, Pişirme & Kristal"
        };

        private static readonly string[] CachedTabSubtitles =
        {
            "Doğadaki temel hammaddelerin bulunduğu biyom/mesafe kuralları ve hangi alet veya tuşla çıkarılacağı.",
            "[R] ile yerdeki taşa Çakmaktaşıyla 4 taş başlık yontma ve [I] Kumaş Çanta Bezi üzerindeki tüm birleştirme tarifleri.",
            "Madde Kristali kapalıyken [T] Zihin İnşası Menüsü ile yerdeki gerçek 3B malzeme kümelerini doğrulama ve mühürleme.",
            "Kamp ateşinde pişirme/kavurma, 28 sn kömür ocağı pirolizi, 1250 °C Bloomery izabe fırını, örste dövme, [Shift+E] söndürme ve [C]/[V] Kristal."
        };

        // =====================================================================
        // SEKME 1: MALZEMELER VE KAYNAKLAR
        // =====================================================================
        private static readonly SurvivalRecipeBookEntry[] Tab0MaterialsEntries =
        {
            new(
                id: "mat_clay_lump",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Su Kıyısı Kaynağı",
                title: "Kil Topağı (ClayLump)",
                ingredientsOrSource: "Göl veya deniz kıyısına <= 4m yatay (ve <= 2m dikey) mesafedeki ıslak kum, toprak veya çim zeminler.",
                toolOrKeybind: "Sol Ctrl + [E] (Çömelip Avuçla Kazma) veya Kazma / Kürek [Sol Tık]",
                stepByStepGuide: "Suya <= 4m mesafedeki ıslak sahil/kıyı zemininde eğilip Sol Ctrl + [E] basılı tutarak veya kazma/kürekle kırarak sınırsız çıkarılır. Çiğ Kil Hava Borusu (2 Kil + 1 Kum), Çiğ Kil Pota (3 Kil + 1 Kum), [T]->[2] Kömür Ocağı örtüsü ve [T]->[3] İzabe Fırını (8 Taş + 6 Kil) yapımında kullanılır."),

            new(
                id: "mat_sand_pile",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Kuru Sahil & Çöl",
                title: "Kum Öbeği (SandPile)",
                ingredientsOrSource: "Sudan > 4m uzaktaki kuru sahil şeritleri, kumsallar ve çöl biyomu zeminleri.",
                toolOrKeybind: "Sol Ctrl + [E] (Avuçla Kazma) veya Kazma / Kürek [Sol Tık]",
                stepByStepGuide: "Sudan uzak kuru kum zeminlerde Sol Ctrl + [E] basılı tutularak veya kazma/kürekle kazılarak elde edilir. [I] Çanta Bezi ortasında Çiğ Kil Hava Borusu (2 Kil + 1 Kum) ve Çiğ Kil Pota (3 Kil + 1 Kum) harcına ısıl dayanım kazandırmak için katılır."),

            new(
                id: "mat_dirt_clod",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Yüzey Zemini",
                title: "Toprak Topağı (LooseDirtClod)",
                ingredientsOrSource: "Çim (Grass) ve toprak (Dirt) kaplı tüm yüzey zeminleri.",
                toolOrKeybind: "Sol Ctrl + [E] (Çömelip Avuçla Kazma) veya Kazma / Kürek [Sol Tık]",
                stepByStepGuide: "Çıplak elle Sol Ctrl + [E] basılı tutarak 1.15 sn'de avuçlanır (enerji tüketmez). [T]->[2] Odun Kömürü Ocağının hava almaması için en az 4 adet Toprak veya Kil Topağı ile odun yığınının üzerini örtmekte kullanılır."),

            new(
                id: "mat_rough_stone",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Kaya & Yüzey",
                title: "Kaba Taş Parçası (RoughStoneChunk)",
                ingredientsOrSource: "Yeryüzündeki gevşek taş döküntüleri ve ana kaya (Stone / Cobblestone) katmanları.",
                toolOrKeybind: "Yerden [E] ile Toplama veya T1+ Kazma ile [Sol Tık]",
                stepByStepGuide: "Doğada yerde hazır bulunur ([E]) veya taş blokların kırılmasıyla düşer. Yerdeki Kaba Taşa Çakmaktaşı ile bakıp [R] tuşuna basılarak 4 farklı taş alet başlığı yontulur; ayrıca [T]->[1] Kamp Ateşi taş çemberi (>= 4 taş) ve [T]->[3] İzabe Fırını gövdesi (>= 8 taş) için temel yapı taşıdır."),

            new(
                id: "mat_flint_stone",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Ateşleme & Yontma",
                title: "Çakmaktaşı (FlintStone)",
                ingredientsOrSource: "Yeryüzünde koyu gri sivri taş parçaları olarak doğal bulunur veya çakıl/kaya kazısından düşer.",
                toolOrKeybind: "Yerden [E] ile Toplama | Yontma: [R] | Kıvılcım: [Sol/Sağ Tık]",
                stepByStepGuide: "1) Yerdeki bir Kaba Taşa bakıp [R] tuşuna basarak 8x8 taş yontma oturumunu başlatır. 2) Kamp Ateşi (3 vuruş), Kömür Ocağı (2 vuruş) ve İzabe Fırınını (2 vuruş) kıvılcımla tutuşturur (kıvılcım çakmak için envanterde ikinci bir Çakmaktaşı veya Kaba Taş bulunmalıdır)."),

            new(
                id: "mat_natural_quartz",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Kristal Uyanışı",
                title: "Doğal Kuvars / Rezonans Kuvarsı (QuartzGem)",
                ingredientsOrSource: "Yüzey kayaçlarında ve sığ mağara duvarlarında parlak beyaz-camgöbeği kristal damarları olarak bulunur.",
                toolOrKeybind: "T1+ Taş Kazma ile [Sol Tık] Kazı -> [C] ile Kristale Yedirme",
                stepByStepGuide: "Bilekteki uyuyan Madde Kristalini (Tier 0 -> Tier 1, 500 Blok kapasite) uyandırmak için 3 adet Doğal Kuvars kazılır; elde veya çantadayken [C] (veya [Sağ Tık]) tuşuna 3 kez basılarak kristale yedirilir."),

            new(
                id: "mat_resonance_crystal",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "T2..T5 Boss/Canlı Ganimeti",
                title: "Rezonans Kristali (ResonanceCrystal T2..T5)",
                ingredientsOrSource: "Başlangıç vadisi (r < 75m güvenli bölge) dışındaki Halka 2..5 (Ring 2..5) vahşi canlılarından ve Boss'lardan düşer.",
                toolOrKeybind: "Canlı/Boss Avlama -> [E] ile Al -> Kristal Aktifken [C] ile Güçlendir",
                stepByStepGuide: "Madde Kristali [C] ile aktifken envanterde bir üst kademeye uygun T2..T5 Rezonans Kristali varsa [C] tuşuna basıldığında tüketilir; kristalin vakum yarıçapını, çekim hızını, kapasitesini (T2: 1.500 -> T5: 50.000 Blok) ve [V] Doğal Arazi Fırçası yarıçapını (1.5m -> 3.5m) yükseltir."),

            new(
                id: "mat_stick_and_firewood",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Orman & Ağaç",
                title: "Kuru Dal (Stick) & Yarılmış Odun (SplitFirewood)",
                ingredientsOrSource: "Kuru Dal: Ağaç altlarında yerde ve çalılarda. Yarılmış Odun: Balta ile devrilen ağaç kütüklerinin parçalanmasıyla.",
                toolOrKeybind: "Kuru Dal: [E] veya Çalıya [Sol Tık] | Yarılmış Odun: Baltayla Kütüğe [Sol Tık]",
                stepByStepGuide: "Kuru Dal alet sapı, çadır tipi kamp ateşi kurulumu ve yakıt (+60 sn) olarak kullanılır. Yarılmış Odun ise yüksek kalorili yakıt (+140 sn), Ahşap Su Kovası (2 Odun + 2 Lif), Ahşap Tekerlek (3-4 Odun/Dal + 2 Lif), [T]->[2] Kömür Ocağı ve [T]->[4] El Arabası yapımında kullanılır."),

            new(
                id: "mat_leaf_and_fiber",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Çalı & Bitki",
                title: "Yaprak Öbeği (LeafPile) & Bitki Lifi (PlantFiber)",
                ingredientsOrSource: "Orman çalıları, bodur bitkiler ve ağaç yaprak kümeleri.",
                toolOrKeybind: "Çalılara [E] veya [Sol Tık] (veya [I] Çantada 2 Yaprak -> 1 Bitki Lifi)",
                stepByStepGuide: "Yaprak Öbeği ve Bitki Lifi [T]->[1] Kamp Ateşinin en altına kav (tutuşturucu) olarak konur. Bitki Lifi ayrıca [I] Çanta Bezi ortasında taş/demir alet başlıklarını sapa bağlamak, Deri Körük dikmek ve Sargı Bezi (2 Yaprak + 2 Lif) sarmak için zorunludur."),

            new(
                id: "mat_hide_and_raw_meat",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Avcılık Ganimeti",
                title: "Ham Deri (RawHide) & Çiğ Et (FoodRawMeat)",
                ingredientsOrSource: "Yeryüzü ve mağara canlılarının taş/demir bıçak, balta veya kılıçla avlanmasıyla elde edilir.",
                toolOrKeybind: "Silahla [Sol Tık] Avlama -> [E] ile Toplama",
                stepByStepGuide: "Çiğ Et çiğ yenmez; yanan Kamp Ateşine [Sağ Tık] ile konulup 12 sn pişirilerek Pişmiş Et (+35 Can, +40 Enerji, +0.8 °C ısı) yapılır. Ham Deri ise [I] çanta ortasında Deri Körük (2 Ham Deri + 2 Dal/Odun + 2 Lif) üretiminde kullanılır."),

            new(
                id: "mat_charcoal",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Piroliz Yakıtı",
                title: "Odun Kömürü (CharcoalChunk)",
                ingredientsOrSource: "[T]->[2] Odun Kömürü Ocağında (>= 4 Odun/Dal + >= 4 Toprak/Kil) 28 sn havasız piroliz ile üretilir.",
                toolOrKeybind: "Çakmaktaşı [Sol/Sağ Tık] Tutuştur -> 28 sn İnce Mavi Duman -> [E] Hasat",
                stepByStepGuide: "Normal odun ateşi ~735-820 °C'yi geçemez. Bakır (1085 °C) ve Demir (1230-1250 °C) izabesi için Kömür Ocağından elde edilen 6-8x Odun Kömürü İzabe Fırınına [Sağ Tık] ile üstten doldurulmalıdır."),

            new(
                id: "mat_ores_all",
                tab: SurvivalRecipeBookTab.MaterialsAndResources,
                categoryBadge: "Maden & Cevher Damarları",
                title: "Bakır, Demir, Altın, Titanyum ve Mithril Cevherleri",
                ingredientsOrSource: "Yüzeydeki mostra (Outcrop) kaya izlerinin altında ve yeraltı mağara katmanlarındaki 3B rölyefli parıltılı damarlarda bulunur.",
                toolOrKeybind: "Bakır: T1 Taş Kazma | Demir & Altın: T2 Demir Kazma | Titanyum & Mithril: T3+ Kazma",
                stepByStepGuide: "Cevher damarları Madde Kristali ile vakumlanamaz; uygun kademedeki kazma ile [Sol Tık] savrularak kırılır. Bakır ve Demir cevherleri Kamp Ateşinde kavrulup (10 sn) Taş Çekiçle (3 vuruş) kırılarak veya doğrudan İzabe Fırınına [Sağ Tık] ile yüklenerek metale dönüştürülür.")
        };

        // =====================================================================
        // SEKME 2: [I] ÇANTA BEZİ & [R] TAŞ YONTMA TARİFLERİ
        // =====================================================================
        private static readonly SurvivalRecipeBookEntry[] Tab1CraftingEntries =
        {
            new(
                id: "craft_knapping_4_heads",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[R] Fiziksel Taş Yontma",
                title: "4 Taş Alet Başlığı: Kazma, Balta, Bıçak ve Çekiç Başı",
                ingredientsOrSource: "Yerde 1x Kaba Taş Parçası (veya kaya bloğu) + Envanterde/Elde 1x Çakmaktaşı (FlintStone).",
                toolOrKeybind: "Yerdeki Taşa Bak (<= 3.2m) + [R] -> Şablon [1..4] Seç -> [Sol Tık] Yont -> [E] Al",
                stepByStepGuide: "Yerdeki taşa bakıp [R] tuşuna basarak 4 şablondan birini seç: [1] Taş Kazma Başı, [2] Taş Balta Başı, [3] Keskin Taş Bıçak Ağzı, [4] Ağır Taş Çekiç Başı. Çizgi dışındaki fazlalık hücrelere [Sol Tık] ile vurup kır; çizgi içine vurmamaya dikkat ederek (%35..%100 kalite) tamamlanınca [E] ile başlığı al."),

            new(
                id: "craft_primitive_pickaxe",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Çanta Ortası (T1 Alet)",
                title: "İlkel Taş Kazma (StonePickaxe — T1)",
                ingredientsOrSource: "1x Yontulmuş Taş Kazma Başı + 1x Kuru Dal + 1x Bitki Lifi",
                toolOrKeybind: "[I] Çantayı Aç -> Parçaları Ortaya [Sol Tık] Koy -> [R / İple Bağla]",
                stepByStepGuide: "Yontulmuş kazma başlığının %Kalite ve 64-bit kırık izi DNA'sı üretilen kazmaya birebir geçer. Kaya katmanlarını, Doğal Kuvarsı ve Bakır Cevheri damarlarını kazmak için kullanılır."),

            new(
                id: "craft_primitive_axe",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Çanta Ortası (T1 Alet)",
                title: "İlkel Taş Balta (StoneAxe — T1)",
                ingredientsOrSource: "1x Yontulmuş Taş Balta Başı + 1x Kuru Dal + 1x Bitki Lifi",
                toolOrKeybind: "[I] Çantayı Aç -> Parçaları Ortaya [Sol Tık] Koy -> [R / İple Bağla]",
                stepByStepGuide: "Ağaçları kesip devirmek ve yerde yatan devrilmiş ağaç kütüklerini [Sol Tık] ile Yarılmış Odun (SplitFirewood) parçalarına ayırmak için kullanılır."),

            new(
                id: "craft_primitive_knife_spear_shovel",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Çanta Ortası (Silah & Kazı)",
                title: "İlkel Taş Bıçak / Mızrak & Kürek İşlevi",
                ingredientsOrSource: "1x Yontulmuş Taş Bıçak Ağzı + 1x Kuru Dal + 1x Bitki Lifi (Lif: 2 Yaprak -> 1 Lif veya Çalıdan [E])",
                toolOrKeybind: "[I] Çanta Ortasında [R / İple Bağla] | Yumuşak Zemin Kazısı: Sol Ctrl + [E] veya Kazma/Kürek",
                stepByStepGuide: "Keskin taş ağız Kuru Dal ve Bitki Lifi ile bağlanarak yakın dövüş, mızrak/bıçak saplama ve canlı avlama (Ham Deri & Çiğ Et) silahı üretilir. Toprak, ıslak kil ve kuru kum kazısı ise Sol Ctrl + [E] avuçla kazma veya kazma/kürek darbesiyle yapılır."),

            new(
                id: "craft_stone_hammer",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Çanta Ortası (Demircilik)",
                title: "Taş Çekiç (ToolStoneHammer)",
                ingredientsOrSource: "1x Kaba Taş (Yontulmuş Taş Çekiç Başı) + 1x Kuru Dal + 1-2x Bitki Lifi",
                toolOrKeybind: "[R] Şablon 4 (Çekiç Başı) -> [I] Çanta Ortasında Dal + Lif ile [R / İple Bağla]",
                stepByStepGuide: "Demir Çağına geçişin kilit aletidir: 1) Kavrulmuş cevheri düz kayada 3 vuruşta Cevher Tozuna kırar, 2) İzabe Fırını ön kil tıkacını [Sol Tık] ile kırar, 3) 1180 °C Akkor Sünger Demiri örste/düz taşta 8 darbede Dövme Demir Külçesine döver."),

            new(
                id: "craft_clay_tuyere",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Seramik Kalıplama",
                title: "Çiğ Kil Hava Borusu (ClayTuyerePipe)",
                ingredientsOrSource: "2x Kil Topağı (ClayLump) + 1x Kum Öbeği (SandPile)",
                toolOrKeybind: "[I] Çanta Ortasına 2 Kil + 1 Kum Koy -> [R / İple Bağla] -> Kamp Ateşinde 12 sn Pişir",
                stepByStepGuide: "Çanta ortasında çiğ olarak şekillendirilir. İzabe Fırınının yan deliğine [Sağ Tık] ile takılabilmesi için önce yanan Kamp Ateşine [Sağ Tık] ile konulup 12 saniye pişirilerek seramikleştirilmelidir!"),

            new(
                id: "craft_clay_crucible",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Seramik Kalıplama",
                title: "Çiğ Kil Pota (ClayCruciblePot)",
                ingredientsOrSource: "3x Kil Topağı (ClayLump) + 1x Kum Öbeği (SandPile) (veya 2x Kil + 2x Kum)",
                toolOrKeybind: "[I] Çanta Ortasına 3 Kil + 1 Kum Koy -> [R / İple Bağla] -> Kamp Ateşinde 12 sn Pişir",
                stepByStepGuide: "Kamp Ateşinde 12 sn pişirildikten sonra İzabe Fırınına [Sağ Tık] ile yerleştirilir. Fırın içinde 1085 °C'de Kırılmış Bakır Cevherini eritip 2x Saf Bakır Külçe dökmek için kullanılır."),

            new(
                id: "craft_leather_bellows",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] İzabe Donanımı",
                title: "Deri Körük (LeatherBellows)",
                ingredientsOrSource: "2x Ham Deri (RawHide) + 2x Kuru Dal / Yarılmış Odun + 2x Bitki Lifi",
                toolOrKeybind: "[I] Çanta Ortasına 2 Deri + 2 Dal/Odun + 2 Lif Koy -> [R / İple Bağla]",
                stepByStepGuide: "İzabe Fırınına Pişmiş Kil Hava Borusu (Tuyere) takıldıktan sonra üzerine [Sağ Tık] ile monte edilir. Fırın yanarken [E] ile ritmik pompalanarak sıcaklığı 795 °C doğal çekiş sınırından 1250+ °C'ye çıkarır."),

            new(
                id: "craft_wooden_wheel_and_bucket",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] Lojistik & Sıvı",
                title: "Ahşap Tekerlek (WoodenWheel) & İlkel Ahşap Su Kovası",
                ingredientsOrSource: "Ahşap Tekerlek = 3-4x Odun/Dal + 2x Bitki Lifi | Ahşap Su Kovası = 2x Yarılmış Odun + 2x Bitki Lifi",
                toolOrKeybind: "[I] Çanta Ortasına Odun + Lif Koy -> [R / İple Bağla]",
                stepByStepGuide: "Ahşap Tekerlek [T]->[4] 36 Yuvalı Ahşap El Arabasının ön aksı için kullanılır. İlkel Ahşap Su Kovası (SubId=1 su doluyken) gölden [Sol Tık] ile su alır, [Sağ Tık] ile suyu döker veya yanan Kamp Ateşi / İzabe Fırınını anında söndürür."),

            new(
                id: "craft_t2_iron_tools",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] T2 Demir Çağı Aletleri",
                title: "T2 Dövme Demir Kazma / Balta / Çekiç / Uzun Kılıç",
                ingredientsOrSource: "1..3x Dövme Demir Külçesi (WroughtIronBar) + 1x Dal/Odun + 1x Bitki Lifi (Kazma: 1 Külçe, Balta/Çekiç: 2 Külçe, Kılıç: 3 Külçe)",
                toolOrKeybind: "[I] Çanta Ortasına Dövme Demir Külçesi + Dal/Odun + Lif Koy -> [R / İple Bağla]",
                stepByStepGuide: "Örste 8 darbede dövülen Dövme Demir Külçesi (Lama) çanta ortasında sap ve lifle bağlanarak T2 Demir Kazma (1 Külçe + 1 Dal/Odun + 1 Lif), T2 Demir Balta (2 Külçe + 1 Dal + 1 Lif), T2 Demir Çekiç (2 Külçe + 1 Odun + 1 Lif) ve T2 Demir Kılıç (3 Külçe + 1 Dal/Odun + 1 Lif) üretilir."),

            new(
                id: "craft_bandage_and_kits",
                tab: SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                categoryBadge: "[I] İlk Yardım & Kitler",
                title: "Bitkisel Sargı Bezi & Taşınabilir İstasyon Kitleri",
                ingredientsOrSource: "Sargı Bezi = 2x Yaprak + 2x Lif | Kamp Ateşi Kiti = 3x Dal + 2x Çakmaktaşı + 1x Lif | Taş Ocak Kiti = 5x Taş + 2x Dal + 1x Toprak",
                toolOrKeybind: "[I] Çanta Ortası veya 3'lü Birleştirme Yuvası -> [R / İple Bağla]",
                stepByStepGuide: "Bitkisel Sargı Bezi kanamayı durdurur ve can yeniler. İstasyon kitleri ise acil durumda çantada hazır taşınabilen alternatif kurulum paketleridir.")
        };

        // =====================================================================
        // SEKME 3: [T] ZİHİN İNŞASI YAPILARI
        // =====================================================================
        private static readonly SurvivalRecipeBookEntry[] Tab2MindStructuresEntries =
        {
            new(
                id: "mind_campfire",
                tab: SurvivalRecipeBookTab.MindBuildingStructures,
                categoryBadge: "[T] -> [1] Isınma & Pişirme",
                title: "[T]->[1] Kamp Ateşi (Campfire)",
                ingredientsOrSource: "En alta >= 1x Yaprak Öbeği / Bitki Lifi (Kav) + Üzerine >= 3x Kuru Dal / Yarılmış Odun (Opsiyonel: Çevresine >= 4x Kaba Taş).",
                toolOrKeybind: "Yere [Sağ Tık] ile Diz -> [T] Zihin İnşası -> [1] Kamp Ateşi (veya [Sağ Tık / G] Silüet İpucu)",
                stepByStepGuide: "1) Eline Yaprak Öbeği veya Bitki Lifi alıp zemine [Sağ Tık] ile kav bırak. 2) Kavın üzerine/yanına (<= 0.85m) en az 3 adet Kuru Dal veya Odun [Sağ Tık] ile koy (dallar otomatik çadır eğimi alır). 3) İstersen çevresine >= 4 Kaba Taş dizerek +%15 yakıt/rüzgâr koruması kazan. 4) [T] -> [1] ile kümeni doğrula ve Çakmaktaşı ile 3 kez vurarak yak!"),

            new(
                id: "mind_charcoal_pit",
                tab: SurvivalRecipeBookTab.MindBuildingStructures,
                categoryBadge: "[T] -> [2] Piroliz Ocağı",
                title: "[T]->[2] Odun Kömürü Ocağı (CharcoalPit)",
                ingredientsOrSource: "En az 4x Kuru Dal / Yarılmış Odun + En az 4x Toprak Topağı / Kil Topağı kümesi.",
                toolOrKeybind: "Yere [Sağ Tık] ile Diz -> [T] Zihin İnşası -> [2] Odun Kömürü Ocağı",
                stepByStepGuide: "1) Yere (~1.85m yarıçap içine) [Sağ Tık] ile en az 4 adet Yarılmış Odun veya Kuru Dal yığ. 2) Odunların yanıp kül olmaması için üzerini/çevresini en az 4 adet Toprak Topağı veya Kil Topağı ile ört. 3) [T] -> [2] bastığında dağınık yığın temizlenip tabanından kütük uçları görünen, tepe baca delikli 3B kubbeli toprak kömür ocağına dönüşür."),

            new(
                id: "mind_bloomery_furnace",
                tab: SurvivalRecipeBookTab.MindBuildingStructures,
                categoryBadge: "[T] -> [3] 1250 °C Metalürji",
                title: "[T]->[3] Bloomery İzabe Fırını (BloomeryFurnace)",
                ingredientsOrSource: "En az 8x Kaba Taş Parçası + En az 6x Kil Topağı doğal kümesi (kule dikme şartı yoktur!).",
                toolOrKeybind: "Yere [Sağ Tık] ile Doğal Küme Halinde Ser -> [T] Zihin İnşası -> [3] İlkel İzabe Fırını",
                stepByStepGuide: "1) Yere (~1.85m yarıçap içine) [Sağ Tık] ile en az 8 adet Kaba Taş ve 6 adet Kil Topağı doğal şekilde bırak (üst üste kule dikmek gerekmez). 2) [T] -> [3] seçildiğinde küme mühürlenerek önü oyuncuya bakan taş kemerli kaideli, konik silindirik kil bacalı ve yan üfleme yuvalı gerçek 3B Bloomery İzabe Fırınına dönüşür."),

            new(
                id: "mind_wooden_wheelbarrow",
                tab: SurvivalRecipeBookTab.MindBuildingStructures,
                categoryBadge: "[T] -> [4] 36 Yuvalı Lojistik",
                title: "[T]->[4] 36 Yuvalı Ahşap El Arabası (WoodenWheelbarrow)",
                ingredientsOrSource: "1x Ahşap Tekerlek + En az 4-6x Odun/Dal (en az 2 Yarılmış Odun + 2 Kuru Dal) + 2x Bitki Lifi.",
                toolOrKeybind: "Yere [Sağ Tık] ile Diz -> [T] Zihin İnşası -> [4] Ahşap El Arabası",
                stepByStepGuide: "1) Önce [I] çantada 1x Ahşap Tekerlek üret ve [Sağ Tık] ile yere koy. 2) Arkasına tekne ve tutma kolları için Yarılmış Odun + Kuru Dal ve bağlamak için 2x Bitki Lifi diz. 3) [T] -> [4] ile mühürlendiğinde 36 yuvalı gerçek 3B Ahşap El Arabası oluşur; kasasına [Sağ Tık] ile yük koyup [E] ile geri alabilir, arka kollarına bakıp [E] ile sürebilirsin.")
        };

        // =====================================================================
        // SEKME 4: DEMİRCİLİK, PİŞİRME & KRİSTAL (ADIM ADIM REHBER)
        // =====================================================================
        private static readonly SurvivalRecipeBookEntry[] Tab3GuideEntries =
        {
            new(
                id: "guide_cooking_meat",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "Kamp Ateşi (12 sn)",
                title: "1. Et Pişirme (12 sn) ve Kamp Ateşi Yakıt Yönetimi",
                ingredientsOrSource: "Yanan Kamp Ateşi + Çiğ Et (FoodRawMeat) | Yakıt: Kuru Dal (+60 sn), Yarılmış Odun (+140 sn).",
                toolOrKeybind: "Çakmaktaşı ile 3x [Sol/Sağ Tık] Yak -> Çiğ Et ile [Sağ Tık] Koy -> 12 sn Sonra [E] Al",
                stepByStepGuide: "Kamp Ateşini Çakmaktaşıyla 3 vuruşta yak (180 sn başlangıç yakıtı; taş çember varsa +%15 bonus). Çiğ Eti [Sağ Tık] ile közlere koy: 12 sn sonunda altın-kahverengi Pişmiş Et olur ([E] ile al, +35 Can, +40 Enerji). DİKKAT: 26 sn'yi geçerse kömürleşip yanar!"),

            new(
                id: "guide_clay_firing",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "Seramik Pişirme (12 sn)",
                title: "2. Çiğ Kil Hava Borusu ve Kil Pota Pişirme (12 sn)",
                ingredientsOrSource: "Yanan Kamp Ateşi + Çiğ Kil Hava Borusu (2 Kil + 1 Kum) veya Çiğ Kil Pota (3 Kil + 1 Kum).",
                toolOrKeybind: "Yanan Kamp Ateşine [Sağ Tık] ile Koy -> 12 sn Bekle -> [E] ile Pişmiş Seramik Al",
                stepByStepGuide: "[I] çantada şekillendirilen kil boru ve pota başlangıçta çiğdir (FlagUnfiredClay) ve doğrudan fırına takılamaz. Yanan Kamp Ateşine [Sağ Tık] ile koyup 12 sn pişirerek dayanıklı seramiğe dönüştürdükten sonra [E] ile al."),

            new(
                id: "guide_ore_roast_and_crush",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "Cevher Hazırlama",
                title: "3. Cevher Kavurma (10 sn) ve Taş Çekiçle 3 Vuruşta Kırma",
                ingredientsOrSource: "Ham Bakır / Demir Cevheri + Yanan Kamp Ateşi + Düz Kaya/Örs + Taş Çekiç.",
                toolOrKeybind: "Ateşe [Sağ Tık] (10 sn) -> [E] Al -> Düz Kayaya [Sağ Tık] Koy -> Taş Çekiçle 3x [Sol Tık]",
                stepByStepGuide: "Ham cevheri Kamp Ateşinde 10 sn kavurup [E] ile Kavrulmuş Cevher olarak al. Düz bir kayaya veya Örse [Sağ Tık] ile yerleştirip Taş Çekiçle 3 kez [Sol Tık] vurduğunda 2x Kırılmış Cevher Tozu (CrushedOrePowder) verir ve fırında en yüksek hızda (1.00x) erir."),

            new(
                id: "guide_charcoal_pyrolysis",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "Piroliz (28 sn)",
                title: "4. Kömür Ocağı Pirolizi (28 sn — Erken Açma Uyarısı!)",
                ingredientsOrSource: "[T]->[2] Kubbeli Odun Kömürü Ocağı (>= 4 Odun/Dal + >= 4 Toprak/Kil) + Çakmaktaşı.",
                toolOrKeybind: "Çakmaktaşı ile 2x [Sol/Sağ Tık] Tutuştur -> 28 sn Bekle (Mavi Duman) -> [E] Hasat Et",
                stepByStepGuide: "Çakmaktaşı ile 2 vuruşta tutuşturulan ocak 28 sn boyunca kalın gri duman çıkararak için için yanar. KRİTİK: 28 sn dolmadan [E] ile erken açarsan içeri oksijen girer ve tüm odunlar KÜL olur! Duman ince maviye döndüğünde [E] ile açarak 6-8x Odun Kömürü topla."),

            new(
                id: "guide_bloomery_charging",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "İzabe Fırını Yükleme",
                title: "5. İzabe Fırınına Boru + Körük + Kömür + Cevher (Kırılmış/Kavrulmuş/Ham/Blok) Yükleme",
                ingredientsOrSource: "Pişmiş Kil Hava Borusu + Deri Körük (+ Bakır için Pişmiş Kil Pota) + Odun Kömürü + Bakır/Demir Cevheri.",
                toolOrKeybind: "Fırına [Sağ Tık] ile Donanım, Odun Kömürü ve Cevher Yükle",
                stepByStepGuide: "Fırına sırasıyla [Sağ Tık] ile Pişmiş Kil Hava Borusu ve Deri Körük (Bakır için ayrıca Pişmiş Kil Pota) tak. Üstten [Sağ Tık] ile Odun Kömürü (maks 12) ve Bakır/Demir Cevheri (maks 8) doldur. Fırın hem Kırılmış Cevher Tozu (1.00x hız), hem Kavrulmuş Cevher (0.85x hız), hem de doğrudan Ham Cevher veya Cevher Bloğu (0.70x hız) kabul eder!"),

            new(
                id: "guide_bellows_and_plug",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "1250 °C İzabe & Tıkaç",
                title: "6. [E] Ritmik Körük Pompalama, 1250 °C Sünger Demir ve Ön Kil Tıkaç Kırma/Sıvama",
                ingredientsOrSource: "Yanan İzabe Fırını + [E] Körük Pompalama + Taş Çekiç/Kazma + 1x Kil Topağı (Yeniden Mühürleme).",
                toolOrKeybind: "[E / Boş El Sol Tık] Körük (~0.8 sn ritim) -> Çekiç/Kazma [Sol Tık] Ön Tıkacı Kır -> [E] Sünger Demir Al",
                stepByStepGuide: "Fırını Çakmaktaşıyla yakıp ~0.8 sn ritimle [E] basarak körüğü pompala (-1.8 EP). 1085 °C'de Bakır erir ([E] ile 2x Bakır Külçe al); 1230-1250 °C'de Demir indirgenir ve cüruf akar ([E] ile Cüruf al). İçeride Sünger Demir oluştuğunda Çekiç/Kazma ile ön alt kil tıkaca [Sol Tık] vurup kır ve [E] ile 1180 °C Akkor Sünger Demiri çıkar! Sonraki izabe için ön deliği 1x Kil Topağı + [Sağ Tık] ile yeniden sıva."),

            new(
                id: "guide_anvil_forging",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "Sıcak Demircilik (8 Darbe)",
                title: "7. Örste veya Düz Kayada 8 Darbede Dövme Demir Külçesi",
                ingredientsOrSource: "1180 °C Akkor Sünger Demir (>= 720 °C) + Örs / Düz Kaya + Taş Çekiç.",
                toolOrKeybind: "Örse/Düz Kayaya [Sağ Tık] Koy -> Taş Çekiç ile 8x [Sol Tık] Döv -> [E] Dövme Demir Külçesi Al",
                stepByStepGuide: "Akkor Sünger Demiri soğumadan (>= 720 °C üstündeyken) Örse veya düz kayaya [Sağ Tık] ile koyup Taş Çekiçle 8 kez [Sol Tık] vurarak cürufu temizle -> Dövme Demir Külçesi (WroughtIronBar) hazır! Eğer 720 °C altına soğursa [E] ile alıp Kamp Ateşinde (8 sn) veya İzabe Fırınında (>= 850 °C) [Sağ Tık] ile yeniden ısıt."),

            new(
                id: "guide_extinguish_fire",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "Yakıt Koruma",
                title: "8. [Shift+E] veya Su Dolu Kap ile Ateşi / Fırını Söndürme",
                ingredientsOrSource: "Yanan Kamp Ateşi veya Yanan İzabe Fırını | İsteğe Bağlı: Su Dolu Kap (SubId == 1).",
                toolOrKeybind: "Ateşe/Fırına Bakarken [Shift + E] Bas VEYA Su Dolu Kap ile [Sağ Tık] Yap",
                stepByStepGuide: "Yanmakta olan Kamp Ateşine veya İzabe Fırınına bakarken [Shift + E] tuşuna bastığında ya da elinde su dolu kap (Su Kovası / Matara / Kazan, SubId == 1) ile [Sağ Tık] yaptığında ateş anında söner (IsLit = false). Kalan yakıt/kömür ve pişirme/eritme ilerlemesi tamamen korunur!"),

            new(
                id: "guide_crystal_and_scatter",
                tab: SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                categoryBadge: "[C] Kristal & [V] Fırça",
                title: "9. [C] Kristal Seviye Yükseltme (T0->T5), [V] Doğal Arazi Saçılımı ve [Ctrl+B] Malzeme",
                ingredientsOrSource: "3x Doğal Kuvars (T0 -> T1) | T2..T5 Rezonans Kristali (T1 -> T2..T5).",
                toolOrKeybind: "[C] Kristal Aç/Yükselt | [V] Doğal Arazi Fırçası <-> Mimari Blok | [Shift+V] Şekil Gez | [Ctrl+B] Malzeme",
                stepByStepGuide: "3x Kuvarsı [C] ile yedirerek kristali T1'e uyandır; kristal aktifken envanterdeki T2..T5 Rezonans Kristalini [C] ile yedirerek kademesini yükselt. [V] tuşu ShapeId = 0 (Doğal Arazi Saçılım Fırçası, 1.5m-3.5m yumuşak tepecik/dolgu) ile son kullanılan mimari blok şekli arasında anlık geçiş yapar; [Shift+V] mimari şekilleri döner, [Ctrl+B] inşa malzemesini değiştirir.")
        };

        private static readonly SurvivalRecipeBookTabDefinition[] CachedTabs =
        {
            new(
                SurvivalRecipeBookTab.MaterialsAndResources,
                0,
                CachedTabTitles[0],
                CachedShortTabLabels[0],
                CachedTabSubtitles[0],
                Tab0MaterialsEntries),
            new(
                SurvivalRecipeBookTab.ClothAndKnappingRecipes,
                1,
                CachedTabTitles[1],
                CachedShortTabLabels[1],
                CachedTabSubtitles[1],
                Tab1CraftingEntries),
            new(
                SurvivalRecipeBookTab.MindBuildingStructures,
                2,
                CachedTabTitles[2],
                CachedShortTabLabels[2],
                CachedTabSubtitles[2],
                Tab2MindStructuresEntries),
            new(
                SurvivalRecipeBookTab.SmithingCookingAndCrystal,
                3,
                CachedTabTitles[3],
                CachedShortTabLabels[3],
                CachedTabSubtitles[3],
                Tab3GuideEntries)
        };

        private static readonly SurvivalRecipeBookEntry[] CachedAllEntries = BuildAllEntries();

        private static SurvivalRecipeBookEntry[] BuildAllEntries()
        {
            int total = Tab0MaterialsEntries.Length +
                        Tab1CraftingEntries.Length +
                        Tab2MindStructuresEntries.Length +
                        Tab3GuideEntries.Length;
            var all = new SurvivalRecipeBookEntry[total];
            int dst = 0;
            for (int t = 0; t < CachedTabs.Length; t++)
            {
                SurvivalRecipeBookEntry[] src = CachedTabs[t].Entries;
                Array.Copy(src, 0, all, dst, src.Length);
                dst += src.Length;
            }
            return all;
        }

        public static ReadOnlySpan<string> TabTitles => CachedTabTitles;
        public static ReadOnlySpan<string> ShortTabLabels => CachedShortTabLabels;
        public static ReadOnlySpan<SurvivalRecipeBookTabDefinition> Tabs => CachedTabs;
        public static ReadOnlySpan<SurvivalRecipeBookEntry> AllEntries => CachedAllEntries;
        public static int TotalEntryCount => CachedAllEntries.Length;

        public static int ClampTabIndex(int tabIndex) => Math.Clamp(tabIndex, 0, TabCount - 1);

        public static string GetTabTitle(int tabIndex) => CachedTabTitles[ClampTabIndex(tabIndex)];
        public static string GetTabTitle(SurvivalRecipeBookTab tab) => GetTabTitle((int)tab);

        public static string GetShortTabLabel(int tabIndex) => CachedShortTabLabels[ClampTabIndex(tabIndex)];
        public static string GetShortTabLabel(SurvivalRecipeBookTab tab) => GetShortTabLabel((int)tab);

        public static string GetTabSubtitle(int tabIndex) => CachedTabSubtitles[ClampTabIndex(tabIndex)];
        public static string GetTabSubtitle(SurvivalRecipeBookTab tab) => GetTabSubtitle((int)tab);

        public static SurvivalRecipeBookTabDefinition GetTabDefinition(int tabIndex) => CachedTabs[ClampTabIndex(tabIndex)];
        public static SurvivalRecipeBookTabDefinition GetTabDefinition(SurvivalRecipeBookTab tab) => GetTabDefinition((int)tab);

        public static ReadOnlySpan<SurvivalRecipeBookEntry> GetEntriesForTab(int tabIndex) => CachedTabs[ClampTabIndex(tabIndex)].Entries;
        public static ReadOnlySpan<SurvivalRecipeBookEntry> GetEntriesForTab(SurvivalRecipeBookTab tab) => GetEntriesForTab((int)tab);

        /// <summary>
        /// IMGUI ve 0-GC dongulerinin dogrudan indeksleyebilmesi icin onceden tahsis edilmis statik diziyi dondurur.
        /// </summary>
        public static SurvivalRecipeBookEntry[] GetEntriesArrayForTab(int tabIndex) => CachedTabs[ClampTabIndex(tabIndex)].Entries;
        public static SurvivalRecipeBookEntry[] GetEntriesArrayForTab(SurvivalRecipeBookTab tab) => GetEntriesArrayForTab((int)tab);

        public static bool TryFindEntryById(string id, out SurvivalRecipeBookEntry entry)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < CachedAllEntries.Length; i++)
                {
                    if (string.Equals(CachedAllEntries[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = CachedAllEntries[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }
    }
}
