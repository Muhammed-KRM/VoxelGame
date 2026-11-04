# STRATOCRAFT — TÜM OYUN TUŞLARI VE FİZİKSEL MEKANİK KULLANIM REHBERİ

Bu rehber, **Stratocraft** içerisindeki tüm klavye/fare tuşlarını ve oyunun fizik tabanlı mekaniklerinin (**240 Hz Yönlü Kılıç Dövüşü, Blok/Siper & Saptırma, Dojo Stilleri, `[I]` 3B Kumaş Çanta Örtüsü, `[C]` Madde Kristali ve Materyal Seçimi, Parkur, Domino Yıkım ve Canlı Evcilleştirme**) adım adım nasıl kullanılacağını açıklar.

---

## 1. HAREKET, PARKUR, DURUŞ VE KAMERA KONTROLLERİ

| Tuş / Kombinasyon | Mekanik Adı | Nasıl Çalışır & Ne İşe Yarar? |
| :--- | :--- | :--- |
| **`W`, `A`, `S`, `D`** | **Yürüme (`4.0 m/s`)** | Karakteri 4 yönde hareket ettirir. Eğimli Smooth SurfaceNets arazilerinde (`<= 1.25m` basamak/yamaç) takılmadan veya zıplamadan pürüzsüzce yürür, çapraz duvar ve ağaç gövdelerinde eksen-ayrımlı olarak kayar (`Wall-Slide`). |
| **`Sol Shift` (Basılı Tut) + `WASD`** | **Depar / Sprint (`7.0 m/s`)** | Hızlı koşu sağlar (`-8.5 EP/sn` Stamina tüketir). Stamina `0 EP`'ye düşerse **Soluk Soluğa Yorgunluk Kilidi** devreye girer ve Stamina **`%15`'e (`15 EP`)** dolana kadar depar/zıplama kilitlenir (`1.45 m/s` hız sınırı). |
| **`Space`** | **Zıplama (`-6 EP`) veya `0.55sn` Kübik Bézier Mantle Tırmanışı (`-15 EP`)** | **1) Açık alanda:** Karakteri yukarı zıplatır (`-6 EP`).<br>**2) Önünüzde `1m–2.2m` yüksekliğinde bir duvar/çıkıntı (`Ledge`) varken:** Otomatik olarak **`0.55 sn` Kübik Bézier Mantle Tırmanışı** başlatır (`-15 EP`) ve karakteri çıkıntının üstüne pürüzsüzce çeker. |
| **`Sol Ctrl` (Basılı Tut)** | **Çömelme / Crouch (`2.0 m/s`, Boy: `1.15m`)** | Karakterin boyunu `1.80m`'den `1.15m`'ye indirir; alçak (`2 blok`) mağara geçitlerinden geçmeyi sağlar. |
| **`Sol Shift` + `Sol Ctrl` (Koşarken)** | **Eğim Duyarlı Kayma / Slide (`-15 EP`, `8.2 m/s` -> `~3.58m`)** | Depar atarken `Sol Ctrl`'ye basıldığında karakter `0.90m` profille ileri kayar. Düz zeminde `~3.58m` kayar; yokuş aşağı kayarken yerçekimiyle hızlanır. |
| **`Z` (Aç / Kapat)** | **Yüzüstü Sürünme / Prone (`1.0 m/s`, Boy: `0.45m`)** | `Z` tuşuna basıldığında karakter yüzüstü yere yatar (`0.45m` yükseklik, `0.20m` yarıçap) ve **`1x1m` dar tünellerden** geçebilir. Tekrar `Z`, `Space` veya `Sol Shift`'e basıldığında (eğer baş üstünde tavan engeli yoksa) ayağa kalkar. |
| **`Sol Alt` (veya `Mouse3` / `Mouse4`)** | **Fiziksel Yan-Adım / Dodge Kaçışı (`-18 EP`, `6.8 m/s`)** | Bastığınız hareket yönüne (veya hareket tuşuna basmıyorsanız geriye) `0.26 sn` boyunca `6.8 m/s` hızla ani kaçış hamlesi yapar. **Sahte ölümsüzlük (`i-frame`) yoktur**; saldırı hacminden gerçekten fiziksel olarak çekilmeniz gerekir. |
| **`F5`** | **Gerçek Birinci Şahıs (`FPS`) / Omuz Üstü (`TPS`) Kamera Geçişi** | `FPS` (Göz Hizası) ile `TPS` (Omuz Üstü 3B Görünüm) arasında anında geçiş yapar. `FPS` modunda kendi gövdeniz/kaskınız görüşünüzü kapatmaz, yere baktığınızda bacaklarınız ve bel kılıflarınız görünür. |

---

## 2. FİZİKSEL YÖNLÜ KILIÇ DÖVÜŞÜ NASIL KULLANILIR? (`[1]` KILIÇ SEÇİLİYKEN)

Stratocraft'ta hazır animasyonlu ("tuşa basınca otomatik vuran") bir dövüş sistemi **yoktur**. Kılıcınız, farenizin hareketiyle çalışan **240 Hz fiziksel bir yay-damper (`PD Servo`) sarkaçtır**.

### A. Kılıç Nasıl Savrulur? (Yönlü Kesik & Tam-Yay Vuruş)
1. **`[1]` tuşuna basarak** kılıcı elinize alın.
2. **Temel Yönlü Kesik (`Sol Tık Basılı Tut + Fareyi Hızla Savur`):**
   - **Sol Fare Tuşuna (`Sol Tık`) basılı tutarken** fareyi kesmek istediğiniz yöne (**Sağa, Sola, Yukarıdan Aşağıya, Aşağıdan Yukarıya veya Çapraz**) hızla çekin.
   - Kılıcınız farenizi takip ederek o yönde tam yay (`Full-Arc Slash`) çizer.
   - Kesiği tamamladıktan sonra kılıcınız kesik attığınız yönde **`~1.05 saniye`** boyunca duruşunu korur (böylece ardışık hızlı saldırılar akıcı şekilde zincirlenir); `1.05 sn` boyunca yeni hamle yapmazsanız kılıç otomatik olarak **Dik Şövalye Duruşuna (`Vertical Guard`)** geri döner.
3. **Ön-Kurumlu Güçlü Kesik (`Pre-Chambered Slash` — `1.28x` Hasar):**
   - Vurmadan hemen önce fareyi ters yöne çekip kılıcı geriye kurun, ardından **`Sol Tık` + Ters Yöne Hızlı Fare Savurması** yapın. Tam yay ivmesiyle **`1.28x` Ön-Kurumlu Kesik** vurursunuz.
4. **Havada Zıpla-Vur (`Aerial Jump-Strike` — `1.40x` Hasar & Geniş Düşey Menzil):**
   - **`Space`** ile zıplayıp havadayken **`Sol Tık` + Fareyi Yukarıdan Aşağıya** savurun. Yerçekimi momentumu kılıca eklenir ve **`1.40x` Zıpla-Vur** hasarı verir.

### B. Blok (`Parry`), Yan-Siper ve Usta Saptırma (`Master Deflect / Riposte`) Nasıl Yapılır?
1. **Yönlü Çapraz Blok / Siper (`Sağ Tık Basılı Tut + Fare Yönü`):**
   - **Sağ Fare Tuşuna (`Sağ Tık`) basılı tutun** ve fareyi saldırının geldiği tarafa (**Sol, Sağ, Üst veya Orta**) kaydırın.
   - Karakteriniz kılıcı o yönde **çapraz siper bariyeri** olarak kaldırır.
   - Düşman bu bariyere vurduğunda hasarın **`%85`'i emilir** (yalnızca `%15` sızıntı hasarı alırsınız).
   - **Dikkat (Gard Kırılması):** Çok hafif bir silahla (ör. `Rapier`) dev bir yaratığın veya ağır kılıcın darbesini sadece sabit `Sağ Tık` ile karşılamaya çalışırsanız, aşırı darbe torku bilek direncini aşar ve **Gard Kırılması (`Guard Break`)** yaşarsınız!
2. **Son Anda Usta Saptırma (`Master Deflect` & `1.85x` Riposte):**
   - Düşman pençesini/silahını savururken tam darbe anında (`<= 0.18 sn` penceresinde) **`Sağ Tık` basılıyken fareyi darbenin tersi yönüne savurun** (veya düşman pençesine doğrudan kılıcınızla karşı kesik atın).
   - **Kılıç-Pençe Çarpışması (`Active Clash / Master Deflect`)** gerçekleşir: **`0` hasar alırsınız**, düşman sersemler ve size `0.90 sn` boyunca **`1.85x` Usta Kontra (`Riposte`)** penceresi açılır!
3. **Siperden Hızlı Yukarı Kesik (`Guard-to-Slash` — `1.38x` Hız):**
   - **`Sağ Tık`** basılı tutarken fareyi aşağı çekerek kılıcı alt sipere kurun; ardından **`Sol Tık` basıp fareyi hızla yukarı çekin**. Kılıç alt siperden **`1.38x` ekstra hızla** yukarı doğru şimşek gibi keser!

### C. Saplama (`Thrust`), Aldatma (`Feint`), Dönüştürme (`Morph`) ve Tekme (`Kick`)
| Tuş / Kombinasyon | Dövüş Tekniği | Nasıl Yapılır? |
| :--- | :--- | :--- |
| **`Sol Tık` + `Sağ Tık` (İkisi Basılı) + `Fareyi İleri İt`** | **İleri Saplama (`Thrust`)** | İki fare tuşuna aynı anda basılı tutup fareyi ileri ittiğinizde kılıç düz bir çizgide ileri saplanır (`0.95m` erişim). Özellikle **Eskrim (`Rapier`)** stilinde **`2.0x` İğne Hamlesi** hasarı verir. |
| **`Q` (Savurma Başında)** | **Aldatma (`Feint` — `-6 EP`)** | Kılıcı savurmaya başladığınız ilk anda (**Windup** evresinde) **`Q`** tuşuna basarsanız savurma anında iptal olur ve rakibi yanıltıp hemen farklı yönden gerçek saldırıyı başlatabilirsiniz. |
| **`Fare Tekerleği İleri` (`Scroll Up`)** | **Kesiği Saplamaya Dönüştürme (`Slash-to-Thrust Morph` — `-8 EP`)** | Kılıç savrulurken tekerleği ileri ittiğinizde, yatay kesik anında ileri saplamaya (`Thrust`) dönüşür ve **`1.30x`** sürpriz hasarı verir. |
| **`F`** | **Ön Tekme (`Front Kick` — `-12 EP`)** | Yakındaki düşmanı veya gard almış rakibi tekmeleyerek geriye iter ve gardını sarsar (`Stagger`). |
| **`Sağ Tık` + `F`** | **Kabza Darbesi (`Pommel Bash` — `-10 EP`)** | Siper (`Sağ Tık`) tutarken `F` tuşuna basıldığında kılıcın kabzasıyla rakibin suratına hızlı bir sersemletme darbesi indirir. |

### D. Namlu Bölgeleri (`Forte`, `Mesne`, `Foible`) ve Mesafe Kontrolü
Kılıcınız kabzadan uca doğru 5 fiziksel temas noktasına ayrılır:
- **Kabza Dibinden Vuruş (`u <= 0.20` — `Hilt Bash`):** Düşmanın aşırı dibine (`< 1.0m`) girerseniz kılıcın keskin kısmı değil kabzası çarpar (`14` küt hasar verir).
- **Orta ve Uç Namlu (`Mesne u = 0.50` & `Foible u = 0.80..1.00`):** En yüksek açısal hıza sahip keskin bölgedir (**`1.22x` hasar çarpanı** ve **uzuv koparma** gücü). Canlılar artık sizden **`~2.65m–3.15m`** güvenli mesafede (`Stand-Off`) durduğu için bu mesafeden yaptığınız savurmalar doğrudan `Mesne/Foible` ile maksimum hasar verir!

### E. 4 Silah Arketipi (`[F8]`) ve 4 Dojo Dövüş Stili (`[G]`)
| Tuş | İşlev | Seçenekler ve Özel Yetenekler |
| :--- | :--- | :--- |
| **`F8`** | **Kılıç Arketipi Değiştir** | **1. Uzun Kılıç (`Longsword 1.85kg`):** Dengeli şövalye kılıcı.<br>**2. Ağır Kılıç (`Zweihänder 5.6kg`):** Yüksek momentumlu, geniş erişimli zırh/blok yarıcı.<br>**3. İnce Meç (`Rapier 1.05kg`):** Çok hızlı, saplama odaklı düello silahı.<br>**4. Katana (`1.35kg`):** Kavisli, şimşek kesik odaklı samuray kılıcı. |
| **`G`** | **Dojo Dövüş Stili Değiştir** | **1. Yüksek Şövalye (`High Guard`):** Baş üstü dikey duruş -> **Yetenek: Usta Kontra (`1.85x` Riposte)**.<br>**2. Çift El Yarma (`Zornhau`):** Geniş omuz duruşu -> **Yetenek: Kasırga Zinciri (Arka arkaya ters kesiklerde `0sn` bekleme, `3x` kombo) & Blok Yarma**.<br>**3. Iaido Kın Çekişi (`Battojutsu`):** Kında bekleme (`0.65sn` şarj) -> **Yetenek: Şimşek Kesiği (`1.60x` hız & hasar)**.<br>**4. Eskrim Düello (`Fencing Lunge`):** İleri uzatılmış uç -> **Yetenek: İğne Hamlesi (`2.0x` saplama hasarı & `+0.42m` erişim)**. |

> **Önemli (Silah x Stil Uyumu):** Ağır `Zweihänder` ile `Iaido` veya `Eskrim` yapmaya çalışırsanız hızınız **`0.45x`'e düşer**; ince `Rapier` ile `Çift El Yarma` yapmaya çalışırsanız hasarınız **`0.55x`'e düşer**. Silahınıza uygun Dojo stilini (`[G]`) seçin!

---

## 3. FİZİKSEL ENVANTER (`[I]`), BEL KILIFLARI (`[0..9]`) VE SANDIK/ÖRS/EVCİLLEŞTİRME (`[E]`)

| Tuş | Mekanik Adı | Nasıl Kullanılır? |
| :--- | :--- | :--- |
| **`I`** | **3B Fiziksel Kumaş Çanta Örtüsü (`The Forest` + Serbest 3B Sürükle-Bırak)** | - **`I` tuşuna bastığınızda** oyun duraklamaz (yürüme hızınız `1.2 m/s`'ye iner), önünüze **`1.68x` genişletilmiş 3B Kumaş Çanta Örtüsü** serilir ve **kamera bakışı kilitlenir** (böylece fareyi oynatırken ekran dönmez, eşyaları rahatça dizebilirsiniz).<br>- Her eşyanın sağ alt köşesinde **kaç adet olduğu (`x16`, `x8`)** ve aletlerin altında **dayanıklılık çubuğu** görünür.<br>- **Sol Tık Sürükle-Bırak (veya Tıkla-Taşı):** Çanta örtüsündeki (24 yuva) veya alt kemer/kılıf şeridindeki (9 yuva) herhangi bir 3B eşyayı fareyle tutup istediğiniz yuvaya serbestçe taşıyabilir, yerlerini değiştirebilirsiniz.<br>- **Sağ Tık:** Yuvadaki yığını **ikiye böler (`Split`)**.<br>- **`Shift` + Sol Tık:** Eşyayı çanta ile alt kılıf barı arasında **hızla aktarır**. |
| **`0`** | **Boş El (`Unarmed`)** | Eldeki silahı/aleti kılıfına sokar (`0.25 sn`). Boş elle yumuşak toprak/yaprak kazabilir veya `[Q]` ile el içi darbe vurabilirsiniz. |
| **`1` .. `9`** (veya **Fare Tekerleği**) | **Bel/Sırt Kılıfları ve Hızlı Yuvalar** | - **`[1]`:** Kılıç<br>- **`[2]`:** Fiziksel Kazma (`Pickaxe`)<br>- **`[3]`:** Fiziksel Balta (`Axe`)<br>- **`[4]`:** `[5]` tuşuyla da seçilen `4x4x4` Mikro-Keski (`Chisel`)<br>- **`[5]`:** `[6]` tuşuyla da seçilen Sıvı Matarası (`Fluid Flask`)<br>- **`[6]..[9]`:** Elde taşınan 3B İnşa Blokları (Ham Taş, Meşe Odunu, Kırıktaş, Bazalt vb.). |
| **`X`** | **Acil Alet/Silah Yere Bırakma (`0.0 sn Quick-Drop`)** | Elinizdeki ağır silahı/aleti `0.0 sn` içinde fiziksel olarak yere bırakır (ör. suya düştüğünüzde ağırlıktan kurtulmak için). Yerdeki eşyayı **`[E]`** ile geri alabilirsiniz. |
| **`E`** | **Çok Amaçlı Dünya Etkileşimi (Yerden Alma, Hazine Sandığı, Atölye Sandığı, Örs, Canlı Evcilleştirme & Binek)** | **`[E]` tuşu baktığınız/yakın olduğunuz nesneye göre şu 5 işlemi yapar:**<br>1. **Yerdeki Eşya / Ganimet (`<= 3.6m`):** Yere düşen silahları veya öldürdüğünüz yaratıklardan düşen ganimetleri (`Kül Postu`, ` Canavar Dişi`, `Mithril Cevheri`, `Anka Kalbi`) toplar.<br>2. **Köy / Tapınak / Harabe Hazine Sandıkları (`<= 4.8m`):** Dünyadaki yapıların içindeki hazine sandıklarını açar ve içindeki cevherleri/blokları çantanıza + Madde Kristalinize aktarır.<br>3. **Atölye 3B CAS Sandığı (`<= 3.8m`):** Sandık kapağını yay fiziğiyle açar ve sandıktaki 3B eşyayı alır (**`Shift + E`** elinizdeki eşyayı sandığa koyar).<br>4. **Atölye Örsü (`<= 3.6m`):** Elinizdeki aşınmış aleti/kılıcı `%100` tamir eder ve biler (**`Shift + E`** eldeki eşyayı külçeye geri dönüştürür).<br>5. **Yaralı Canlı Evcilleştirme (`<= 4.5m`, Canı `<= %38 HP`):** Canı `%38`'in altına düşmüş bir yaratığın yanına yaklaşıp **`[E]`**'ye basarsanız yaratığı evcilleştirirsiniz! Evcil **Savaş Atı (`#2`)** veya **Griffin (`#14`)** yanındayken tekrar **`[E]`**'ye basarak **bineğe binebilir / inebilirsiniz** (`Griffin` üzerinde `Space` ile uçabilirsiniz!). |

---

## 4. MADDE KRİSTALİ (`[C]`), MATERYAL SEÇİMİ, KAZMA VE İNŞA MEKANİKLERİ

### A. Madde Kristali (`[C]`) Nasıl Kullanılır ve İçindeki Materyal Nasıl Seçilir?
1. **`[C]` tuşuna basın:** Sol bileğinizdeki **Tier-3 Madde Kristali** aktifleşir ve ekranın alt-orta kısmında **8 kutulu `KRİSTAL MATERYAL SEÇİMİ` paneli** açılır:
   - `Taş`, `Toprak`, `Bazalt`, `Kırıktaş`, `Odun`, `Moloz`, `Tuğla`, `Mermer` (her birinin içinde kaç blok yüklü olduğu kutu üzerinde yazar).
2. **Kristal İçinden Koyacağınız Materyali Seçmek İçin:**
   - **Fare Tekerleğini (`Scroll Up / Down`) çevirin** VEYA **`[B]` tuşuna basın**!
   - Seçili materyal kutusu altın sarısı çerçeveyle vurgulanır.
3. **Kristalle Kazma / Kırma (`Sol Tık Basılı Tut`):**
   - Baktığınız doğal araziyi veya **daha önce koyduğunuz oyuncu bloklarını** DDA voksel ışınıyla (`11m` menzil) kazıp kristalin içine vakumlarsınız.
4. **Kristalle Blok Koyma / İnşa Etme (`Sağ Tık`):**
   - Seçtiğiniz materyalden (`> 0` stok varsa veya malzeme yuvasında varsa) baktığınız yüzeye blok inşa eder. Koyulan her blok anında fiziksel `MeshCollider` kazanır; üstüne çıkabilir veya tekrar `Sol Tık` ile kırabilirsiniz.
5. **Mimari Şekil, 24-Yön Döndürme ve Fırça Boyutu:**
   - **`[V]`:** 8 mimari şekil arasında geçiş yapar (`Tam Küp`, `45° Rampa`, `Köşe Rampa`, `İç Köşe`, `Yarım Plaka`, `İnce Dikme`, `Kemer`, `Silindir Kolon`).
   - **`[R]`:** Koyacağınız bloğu **24 farklı 3B yönde** döndürür (`Shift + R` eksen değiştirir).
   - **`Shift + Fare Tekerleği`** (veya **`[T]`**): Kristal fırça boyutunu `1x1x1` ile `5x5x5` arasında büyütüp küçültür.

### B. Fiziksel Kazma (`[2]`), Balta (`[3]`), Mikro-Keski (`[4]`/`[5]`) ve Matara (`[5]`/`[6]`)
| Alet / Yuva | Nasıl Kullanılır? |
| :--- | :--- |
| **`[2]` Fiziksel Kazma (`Pickaxe`)** | `[2]` ile kazmayı elinize alın. **`Sol Tık + Fareyi Hızla Savurarak`** kayalara/cevherlere (`m * v^2` kinetik enerjiyle) vurun. Taşıyıcı bir sütunun altını kırarsanız üstteki desteksiz kütle **Domino Yıkım (` FallingBlockCluster`)** ile devrilerek yıkılır! |
| **`[3]` Fiziksel Balta (`Axe`)** | `[3]` ile baltayı elinize alıp ağaç gövdelerinin tabanına savurarak vurun. Gövde kesildiğinde üstteki bütün ağaç ve yaprak tacı devrilerek odun/yaprak olarak iner. |
| **`[4]` (veya `[5]`) `4x4x4` Mikro-Keski (`Chisel`)** | **`Sol Tık`:** Baktığınız bloğu `0.25m` (`4x4x4` alt-voksel) hassasiyetinde oyar.<br>**`Sağ Tık`:** Seçili malzemeyle `0.25m` mikro-voksel ekler.<br>**`[B]`:** Oyma modunu (`Tekli 1x1x1` / `Yatay Kesim` / `Dikey Oluk`) değiştirir. |
| **`[5]` (veya `[6]`) Sıvı Matarası (`Fluid Flask`)** | **`Sol Tık`:** Önünüzdeki su/lav/asit kaynağından sıvıyı mataraya doldurur.<br>**`Sağ Tık`:** Mataradaki sıvıyı baktığınız yere döker (ör. **Su + Lav = Doğal Obsidyen + Buhar** oluşturur!). |
| **`[6]..[9]` Elde Taşınan 3B İnşa Blokları** | Madde Kristali kapalıyken bile `[6]..[9]` yuvalarından elinize bir blok alıp **`Sağ Tık`** ile doğrudan yere/duvara koyabilirsiniz. |

---

## 5. KAYIT, DÜNYA TOHUMU (`SEED`) VE TEST / KEŞİF KISAYOLLARI

| Tuş | İşlev | Açıklama |
| :--- | :--- | :--- |
| **`F3`** | **Ekran Bilgi & Geliştirici Yazılarını Aç / Kapat** | Üstteki 8 satırlık simülasyon/dünya bilgi kutusunu, alttaki kılıç duruş panelini ve sıcaklık uyarı yazılarını gizleyerek **tertemiz bir oyun ekranı** sunar (Minecraft'taki F3 mantığı). |
| **`F1`** | **Sinematik Mod (Tüm Arayüzü Gizle)** | Ekrana ait tüm yazıları, nişangahı ve alt eşya barını (Hotbar) tamamen gizleyip açar. |
| **`F6`** | **Hızlı Kaydet (`Quick-Save`)** | Tüm voksel değişikliklerinizi, envanterinizi ve kristal kütlenizi RLE + CRC32 korumalı `.scsave` dosyasına kaydeder. |
| **`Shift + F9`** | **Hızlı Yükle (`Quick-Load`)** | Kaydedilmiş `.scsave` dünyasını geri yükler. |
| **`F7`** | **Yeni Rastgele Dünya Tohumu (`Seed`) Üret** | Yeni bir `Seed` ile dağları, denizleri, adaları, su altı mağaralarını, volkanları ve köyleri baştan yaratır (`Shift + F7` varsayılan `Seed: 1453`'e döner). |
| **`F9`** | **Anatomik Uzuv Kopması Testi (`Limb Sever`)** | Oyuncunun sol kolunun kopması (`Missing Left Arm`) veya bacağının yaralanması durumundaki tek-el dövüş ve topallama fiziğini test eder. |
| **`F10`** | **Düellocu Kılıç Ustası NPC & Mekanik Kukla Çağır** | Tam önünüze yönlü blok/saldırı yapan **Kılıç Ustası Düellocu NPC** ve **Döner Mekanik Eğitim Kuklası** kurar. |
| **`F11`** | **İklim / Hava Durumu Döngüsü Testi** | Güneşli, Yağmurlu (`Islaklık`), Kar Fırtınası (`Hipotermi`) ve Volkanik Kül Fırtınası (`Hipertermi`) iklimlerini sırayla test eder. |
| **`F12`** | **Biyom, Deniz, Volkan ve Mağara Keşif Işınlayıcısı** | Sizi sırayla **Zümrüt Başlangıç Vadisi, Volkanik Kaldera, Buzul Zirveleri, Sarkıt Mağarası ve Okyanus Adaları** arasında ışınlar. |
