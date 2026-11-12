# DOK1 — Hedef ve Gereksinim Dokümanı (Biz Ne Yapmak İstiyoruz?)

> **Doküman Navigasyonu (Ana 4'lü + Faz Bazlı Detaylı Geliştirme Belgeleri):**
> - **[DOK1 — Hedef ve Gereksinimler (Bu Doküman)](DOK1_HEDEF_VE_GEREKSINIMLER.md)**
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - [DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - [DOK4 — Ana Mimari ve Fazlı Geliştirme Yol Haritası](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
>   - [DOK4-FAZ1 — Doğal 3B Eşya DNA'sı, Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı](DOK4_FAZ1_DOGAL_ESYA_BIYOM_VE_YUMRUK.md)
>   - [DOK4-FAZ2 — Çömelmeli Fiziksel Taş Yontma (%Kalite) ve The Forest Tarzı 3B Çanta](DOK4_FAZ2_TAS_YONTMA_VE_THE_FOREST_CANTA.md)
>   - [DOK4-FAZ3 — Serbest Malzeme Dizme, [T] Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme](DOK4_FAZ3_ZIHIN_INSA_KAMP_ATESI_VE_YEMEK.md)
>   - [DOK4-FAZ4 — Gerçekçi Bloomery Demircilik (Kömür, Körük, Sünger Demir, Dövme) ve El Arabası](DOK4_FAZ4_BLOOMERY_DEMIRCILIK_VE_EL_ARABASI.md)

---

## 1. Temel Oyun Felsefesi: "Fiziksel Madde Gerçekçiliği vs. Madde Kristali"

> [!IMPORTANT]
> **Stratocraft'ın Değişmez Fiziksel Dünya Anayasası:**
> 1. **Doğal Eşyalar Küp Değildir:** Oyuncunun kazmayla kırdığı, ağaçtan düşürdüğü veya yerden topladığı hiçbir taş, dal, yaprak, toprak topağı veya kristal `1x1x1` metrelik yapay küp olamaz. Her doğal eşyanın kendine ait, doğada rastgele varyasyon gösteren gerçekçi bir **3B fiziksel şekli** vardır (dal ince-uzun/çatallı, taş yassı/köşeli, kuvars prizmatik vb.).
> 2. **Görsel ve Fiziksel Tutarlılık (`Yerde == Elde == Çantada == Geri Konduğunda`):** Bir eşya yerde nasıl görünüyorsa, karakter eline aldığında da, `[I]` çanta bezinin üstünde dururken de ve dünyaya geri bırakıldığında/dizildiğinde de **%100 birebir aynı 3B şekille** görünür ve yerleşir. İlerideki Yontma Tezgahında şekil verilmedikçe ham taş küp blok olarak konulamaz.
> 3. **Madde Kristalinin Varlık Sebebi:** Ham maddeyi pürüzsüz araziye (`SurfaceNets`) veya düzgün mimari bloklara dönüştürebilmek ve yüzlerce blokluk kütleyi taşıyabilmek **yalnızca Madde Kristaline (`[C]`)** özgü yüksek bir yetenektir.

---

## 2. İş Paketleri ve Beklenen Kullanıcı Deneyimi

### 2.1. Biyomlara Özel Doğal Kaynaklar, 3B Çalılar, Anında `[E]` Toplama ve Elle Avuçlama
- **12 Biyoma Duyarlı Doğa Dağılımı:** Dal, yaprak ve bitki lifi her yerde rastgele çıkmaz:
  - **Ağaçlı Biyomlar (`0: Zümrüt Vadi`, `1: Demir Ağacı Ormanı`, `10: Gökyüzü Adaları`):** Ağaç diplerinde **Kuru Yaprak Öbekleri** ve **Kuru Dallar**, zeminde kırılabilir **3B Çalılar** (elle koparıldığında `İnce Dal + Yaprak + Bitki Lifi` ve bazen `Yabani Meyve` verir), göl/dere kıyılarında **Kil Topağı** ve **Çakmaktaşı**.
  - **Bataklık (`3`):** Sazlık çalıları (bol `Bitki Lifi`), bol `Kil/Çamur`, çürük dallar; kuru çakmaktaşı çok nadir.
  - **Kayalık / Kristal Kanyon / Bazalt (`2`, `5`, `7`):** Ağaç, çalı ve yaprak **yoktur**. Bol `Kaba Taş`, `Çakmaktaşı` ve Biyom 5'te yüzey `Rezonans Kuvarsı` çıkıntıları bulunur.
  - **Çöl (`6`):** Yalnızca seyrek kuru dikenli çalı (`1x Kuru Dal`) ve bol `Kum`.
  - **Buzul (`4`), Volkanik (`8`), Küllü Çorak (`9`), Hiçlik (`11`):** Bitki örtüsü yoktur; biyoma özgü `Buz`, `Bazalt`, `Obsidyen Yongası`, `Kükürt` veya `Kömürleşmiş Odun` bulunur.
- **Anında `[E]` Toplama ve Görünmez Sandıkların Kaldırılması:** Yerdeki bir eşyaya bakıp `[E]` tuşuna tek kez basıldığında mıknatıs gecikmesi olmadan **anında** ele/çantaya alınır. Eski görünmez tapınak/harabe `[E]` ganimet hilesi tamamen kaldırılır.
- **Elle Az Miktarda Toprak / Kum / Kil Avuçlama:** Oyuncu boş elle yumuşak zemine (`Toprak`, `Kum`, `Kil`) bakıp **çömelerek `[E]`'ye basılı tuttuğunda** (`0.6 sn` avuçlama animasyonu), yerden `1 avuç Toprak/Kum/Kil Topağı` alır ve zemindeki SDF yüksekliği hafifçe çöker. Çok miktarda kazmak için kürek/kazma, çok miktarda taşımak için ise **El Arabası / Kızak** gerekir.

### 2.2. Çıplak El Kuralları ve Fiziksel Yumruk Mekaniği
- Boş elle (`Slot 0`) yere, taşa veya ağaca sol tıklandığında **blok kazılmaz ve enerji (Stamina) boşa gitmez** (yalnızca 3B çalılar elle kırılabilir/yolunabilir).
- Sol tık, sağ/sol kol nöbetleşe **gerçek bir Yumruk Savurma Animasyonu** oynatır:
  - Yumruk **yalnızca canlılara (yaratıklar / NPC'ler)** isabet edebilir.
  - İsabet ettiğinde düşük künt hasar verir, Stamina tüketir ve çıplak elle sert bir canlıya vurulduğu için oyuncunun kendi eli acıyarak küçük bir geri tepme hasarı (`1.5 HP`) alır.

### 2.3. Çömelmeli, Çizgili ve `%Kalite` Puanlı Fiziksel Taş Yontma (Stone Knapping)
1. Oyuncu eline **Çakmaktaşı (`Flint`)** alır, yerdeki normal bir **Kaba Taş**'a bakar ve **`[R]`** tuşuna basar.
2. Açılan minimal kalıp seçiminden üretmek istediği taş başlığı seçer:
   - `Taş Balta Başı`, `Taş Kazma Başı`, `Taş Bıçak Ağzı`, `Ağır Taş Çekiç/Tokmak Başı`.
3. **Fiziksel Odaklanma ve Çizgi Çizme:** Karakter taşın başına **çömelir**, kamera yumuşakça yukarıdan açılı olarak taşa yaklaşır; karakter elindeki çakmaktaşıyla taşın üst yüzeyine seçilen kalıbın **beyaz/tebeşir hayalet çizgisini çizer** (kısa çizim animasyonu).
4. **Kenarlardan Vurarak Yontma:** Taş `8x8` kırılabilir doğal kaya dilimlerinden oluşur. Oyuncu fare imleciyle çizginin **dışındaki** kenar fazlalıklarına nişan alıp `Sol Tık` ile çakmaktaşını vurur; her darbede kıvılcım ve küçük taş yongası fırlayarak o parça kopar.
5. **Esnek `%Kalite` Sistemi (`%30 – %100`):**
   - Dış fazlalıkların en az **%80'i** kırıldığında taş başlık sapa bağlanabilir hale gelir (`[E]` ile alınır, `[ESC]` ile vazgeçilir).
   - Oyuncu yanlışlıkla çizginin **içindeki** ana gövde parçalarını kırarsa taş hemen yok olmaz; bunun yerine başlığın **Kalite Puanı (`Quality01`)** düşer:
     - **Kusursuz Yontma (`%95–%100`):** Taş kazma/baltanın ulaşabileceği en yüksek dayanıklılık ve kırma gücü.
     - **Hatalı/Çentikli Yontma (`%35–%70`):** Yine sapa takılıp kazma/balta olur ama dayanıklılığı düşük ve vuruş gücü zayıftır.
     - **Aşırı Parçalama (İç gövdenin `>%50`'si kırılırsa):** Taş ortadan çatlayıp dağılır, geriye küçük çakıl parçaları kalır.
   - Yontulan başlık, oyuncunun kırdığı **gerçek 3B şekliyle** saklanır ve sapa takıldığında da o şekli korur!

### 2.4. `[I]` *The Forest* Tarzı Fiziksel Kumaş Çanta ve 3B Alet Bağlama
- Eski `6x4` kareli kutucuklar, `Girdi 1-2-3` padleri ve sağ üstteki 2B `ANATOMİK BÖLGESEL ZIRHLAR` penceresi **tamamen kaldırılır**.
- `[I]` açıldığında yere serilen kumaş örtünün üzerinde her malzeme kendi doğal bölgesinde (`Dallar`, `Taşlar`, `Çakmaktaşları`, `Yapraklar`, `Bitki Lifleri`, `Deriler`, `Cevherler`, `Yiyecekler`, `Aletler`), **kaç adet varsa o kadar fiziksel 3B nesne olarak ve her biri kendi rastgele şekliyle** dizili durur (gerçekçi taşıma sınırlarıyla: örn. en fazla `12 Dal`, `8 Kaba Taş`, `10 Çakmaktaşı`, `16 Yaprak`, `14 Lif`, `6 Toprak/Kil`).
- **Merkez Birleştirme Alanı:** Örtünün ortasındaki boş alana malzemeler tıklanıp/sürüklenip bırakılır (ör. `1x Yontulmuş Kazma Başı + 1x Kuru Dal + 1x Bitki Lifi`).
  - Ortaya tek bir malzeme konduğunda yanında diyajetik küçük bir ipucu (*"Dal + Yontulmuş Başlık + Bitki Lifi ➔ Taş Alet"*) belirir.
  - Geçerli kombinasyon tamamlandığında bağlama düğmesine tıklanır: **Bez üzerinde 3B bağlama animasyonu** (taş başlığın dala geçmesi ve lifin düğümlenerek sarılması) oynar; üretilen alet başlığın `%Kalite` değerini devralarak çantadaki yerini alır.

### 2.5. Serbest Fiziksel Dizme + `[T]` "Zihin / Yapı Tanımlama" Menüsü + Kamp Ateşi & Yemek Pişirme
- **Serbest Fiziksel Dizme (*Sons of the Forest* Yaklaşımı):**
  - Elde `Yaprak`, `Kuru Dal`, `Kaba Taş`, `Toprak/Kil Topağı` veya `Yarma Odun` varken zemine bakıp `Sağ Tık` yapıldığında eşya rastgele küp olarak değil; yüzey eğimine oturacak şekilde kendi 3B şekliyle yere dizilir (dal ise tıklayınca diz üstünde kırılıp çapraz yatırılır).
- **`[T]` Zihin Tuşu (Hayali Silüet + Toleranslı Yapı Tanıma):**
  - Oyuncu **`[T]`** tuşuna bastığında karakterin zihnindeki yapıları gösteren sade bir seçim halkası açılır (`Kamp Ateşi`, `Kömür Yığını`, `İlkel İzabe Ocağı`).
  - **Hayali Silüet İpucu:** Bir yapı seçildiğinde (veya üzerine gelindiğinde), oyuncunun baktığı zeminde **2.5–3 saniyeliğine soluk tebeşir/zihin silüeti** belirerek malzemelerin kabaca nasıl dizileceğini gösterir (otomatik `E` ile doldurma kesinlikle yoktur; oyuncu kendi malzemelerini eliyle dizer).
  - **Toleranslı Kural Doğrulama & Etiketleme:** Oyuncu dizdiği kümeye bakıp `[T]` menüsünden yapıyı onayladığında sistem `~1.8m` yarıçaplı alandaki fiziksel parçaları ve boşlukları kontrol eder. Eksik varsa net olarak söyler (*"Kamp ateşi için dalların altına biraz kuru yaprak/lif koymalısın"*). Uygunsa karakter kısa bir son dokunuş/sıvama animasyonu yapar; **oyuncunun dizdiği taşların/dalların şekli korunur** ve o küme oyun tarafından aktif yapı olarak etiketlenir!
- **Ateş Yakma ve 3B Yemek Pişirme:**
  - Ateşi yakmak için oyuncu eline **Çakmaktaşı** (diğer elinde/çantasında 2. Çakmaktaşı veya sert taş varken) alıp ateşe bakarak kıvılcım çakar (`2-3` çakışta tutuşur).
  - Elde `Çiğ Et` (veya pişirilebilir yiyecek) varken yanan kamp ateşine `Sağ Tık` yapıldığında et, ateşin üstüne 3B olarak yerleşir:
    - `Çiğ (Pembe-Kırmızı)` ➔ pişme süresince cızırdar ➔ `Pişmiş (Kızarmış Kahverengi, şekli hafif büzülür)`.
    - Piştiğinde `[E]` ile alınmazsa duman çıkararak **Yanmış Kömür** haline gelir ve besin değerini kaybeder!

### 2.6. `demircilik.txt` %100 Gerçekçi Bloomery Demirciliği ve Körük Sistemi
1. **Odun Kömürü Yığını (`Charcoal Pit`):** Kuru odun tek başına `~750 °C` verir ve cevher eritmeye yetmez. Yere dizilen `Yarma Odunlar` üstü `Toprak/Kil` ile örtülüp `[T]` ile **Kömür Yığını** yapılır; alttan ateşlenip için için tüttükten sonra açılarak saf **Odun Kömürü (`Charcoal`)** elde edilir.
2. **Cevher Kavurma ve Taşla Kırma (`Roasting & Crushing`):** Madenden çıkan **Ham Bakır/Demir Cevheri** önce ateşte kavrulup çatlatılır, ardından düz bir kaya üzerinde **Taş Çekiçle** vurularak ceviz büyüklüğünde **Kırılmış Cevher** parçalarına ayrılır.
3. **Baca İzabe Ocağı (`Bloomery`) + Kil Boru (`Tuyere`) + Körük (`Bellows`):**
   - `Kaba Taş + Kil/Toprak + Kum + Bitki Lifi` dizilip `[T]` ile `~1.2m` baca şeklinde **İlkel İzabe Ocağı** olarak sıvanır.
   - Alt deliğine **Kil Hava Borusu (`Tuyere`)** ve yanına avdan çıkan **Ham Deri + Ahşap + Lif** ile birleştirilen **Körük** takılmadan fırın `800 °C`'yi geçemez.
   - Üst bacadan kat kat `Odun Kömürü + Kırılmış Cevher` atılır. Oyuncu körüğü **ritmik olarak pompaladıkça** ocağın iç sıcaklığı ve kor rengi yükselir:
     - `~1100 °C (Parlak Turuncu)`: Kil pota içindeki **Bakır** eriyip saf bakır topağı/külçesi verir.
     - `~1250 °C (Akkor Sarı-Beyaz)`: Fırının altından erimiş taş artığı (**Cüruf / Slag**) lav gibi sızar; demir erimez, dipte kor halinde gözenekli **Sünger Demir (`Iron Bloom`)** kütlesi oluşur.
4. **Sünger Demirin Düz Kaya / Örs Üzerinde Katlanarak Dövülmesi (`Wrought Iron`):**
   - Fırının ön kapağı kırılıp çıkarılan kor halindeki **Sünger Demir**, soğumadan düz bir granit kaya örsün (veya atölye örsünün) üstüne konur ve **Ağır Taş Çekiç** (sonrasında Demir Çekiç) ile dövülür.
   - Her darbede gözeneklerden kıvılcımlı sıvı cüruf fışkırır. Kütle `< 700 °C` altına soğursa çatlamaması için tekrar kor ocağa sokulup ısıtılır. Cürufu tamamen atılıp katlandığında sağlam **Dövme Demir Lama (`Wrought Iron Bar`)** elde edilir ve bundan gerçek T2 Metal Kazma/Balta/Kılıç/Çekiç dövülür!

### 2.7. Madde Kristali (`[C]`) Uyanışı ve Fiziksel El Arabası / Yük Kızağı
- **Kuvars Yedirmeli `[C]` Uyanışı:** Oyun başında Madde Kristali uyuyan (`Kademe 0`) haldedir. Oyuncu eline **Rezonans Kuvarsı** alıp `[C]` veya `Sağ Tık` ile kristale tek tek enerji yedirir (`1/3 ➔ 2/3 ➔ 3/3` parlama ve rezonans sesi); 3. kuvarsta kristal **Kademe 1 (`500 Blok`)** olarak uyanır ve Doğal Arazi Serpme (`Şekil 0`) + vakumlama açılır. Kademe `2..5` evrimi örsten kaldırılıp **Yaratık / Boss Rezonans Kalplerine** bağlanır.
- **Fiziksel El Arabası / Ahşap Yük Kızağı:** Çantada az miktarda taşınabilen `Kum`, `Toprak`, `Kil`, `Kaba Taş`, `Odun Kütüğü` ve `Cevher Kayalarını` bol miktarda (`30–40 parça` gerçek 3B yığın görünümüyle!) taşımak için dallar ve yarma odunlarla inşa edilip `[E]` ile tutularak itilen/çekilen **Fiziksel El Arabası / Kızak** eklenir.

---

## 3. Performans, Kalite ve Bütçe Kısıtları

| Kriter | Zorunlu Sınır |
| :--- | :--- |
| **Kaplama (Texture) VRAM Bütçesi** | `< 16.0 MB` (**0 yeni kaplama dosyası** — tamamı UV'siz `StratocraftUberShader` tepe rengi + sıcaklık emisyonu ile üretilir) |
| **Kare Başına Çöp Tahsisi (GC Alloc)** | `0 B / kare` (tüm yontma, çanta ve fırın döngülerinde `struct` + önbellekli meş havuzu) |
| **Derleme ve Kalite Kapısı** | Unity 6 (`6000.6.7f1`) `0 Error / 0 Warning`, EditMode ve Standalone testler `%100 Geçer` |
| **Taşınabilirlik** | Hiçbir C#, test veya betikte sabit sürücü/kullanıcı yolu kullanılmaz |
