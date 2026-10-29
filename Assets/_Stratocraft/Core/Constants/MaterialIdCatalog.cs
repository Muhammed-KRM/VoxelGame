namespace Stratocraft.Core.Constants
{
    /// <summary>
    /// 0..255 araligindaki tum blok, 14 yeralti cevheri, alasim ve ahsap MaterialID katalogu.
    /// C++20 StratocraftNative ve Python bake_material_lut.py ile birebir senkrondur.
    /// </summary>
    public static class MaterialIdCatalog
    {
        // ---------- Temel Dogal Bloklar ----------
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

        /// <summary>Kirilmaz Ana Kaya / Bedrock (5).</summary>
        public const byte Bedrock = 5;

        /// <summary>Stokiyometrik Dogal Obsidyen (6).</summary>
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

        // ---------- 14 Yeralti Cevheri (Madde Kristali Vakumundan Muaf: 30..43) ----------
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

        /// <summary>7. Titanyum Cevheri (36).</summary>
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

        // ---------- Yakit, Mucevher ve Kozmik Maddeler ----------
        /// <summary>Komur Damari (44).</summary>
        public const byte Coal = 44;

        /// <summary>Karanlik Madde (45).</summary>
        public const byte DarkMatter = 45;

        /// <summary>Yildiz Cekirdegi (46).</summary>
        public const byte StarCore = 46;

        /// <summary>Kizil Elmas / Mucevher Damari Baslangici (47).</summary>
        public const byte GemVeinStart = 47;

        /// <summary>Mucevher Damari Bitisi (49).</summary>
        public const byte GemVeinEnd = 49;

        // ---------- Islenmis Metaller, Alasimlar ve Ahsaplar ----------
        /// <summary>Bronz (50).</summary>
        public const byte Bronze = 50;

        /// <summary>Celik (51).</summary>
        public const byte Steel = 51;

        /// <summary>3B Prosedurel Sam Celigi / Damascus Steel (60).</summary>
        public const byte DamascusSteel = 60;

        /// <summary>Mese Odunu (70).</summary>
        public const byte OakWood = 70;

        /// <summary>Demir Agaci / Ironwood (71).</summary>
        public const byte Ironwood = 71;

        /// <summary>Ruh Agaci / SpiritWood (72).</summary>
        public const byte SpiritWood = 72;

        /// <summary>Yggdrasil Dali (73).</summary>
        public const byte YggdrasilBranch = 73;

        /// <summary>Zindan Kutsal Koruma Bloklari Baslangici (240..255).</summary>
        public const byte DungeonSanctumImmuneStart = 240;

        /// <summary>
        /// Verilen MaterialID'nin yuksek rezonansli cevher veya mucevher olup olmadigini doner.
        /// True donen bloklar Madde Kristali ile vakumlanamaz (ERR_ORE_RESONANCE_IMMUNE).
        /// </summary>
        public static bool IsVacuumImmuneResonanceOreOrGem(byte materialId)
        {
            return (materialId >= CopperOre && materialId <= VoidMetalOre)
                || (materialId >= DarkMatter && materialId <= GemVeinEnd);
        }
    }
}
