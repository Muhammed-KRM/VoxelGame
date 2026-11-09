# 📋 Stratocraft — Faz 1 & Faz 2 Oyun Anayasası Karşılaştırması ve Mantık Hataları Denetim Raporu

> **Rapor Kodu:** `DOC-AUDIT-CONSTITUTION-PHASE1-2-V1`  
> **Tarih:** 2026-11-09  
> **Referans Belgeler:**  
> - [`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`](../STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md)  
> - [`docs/RAPOR_2_GERCEK_FIZIK_MOTORLU_DEVRILME_AGAC_KESME_VE_DARBE_ENERJILI_MOLOZ_MIMARISI.md`](./RAPOR_2_GERCEK_FIZIK_MOTORLU_DEVRILME_AGAC_KESME_VE_DARBE_ENERJILI_MOLOZ_MIMARISI.md)  
> - [`docs/FIZIKSEL_ENVANTER_CANTA_VE_HOLSTER_RAPORU.md`](./FIZIKSEL_ENVANTER_CANTA_VE_HOLSTER_RAPORU.md)  
> - Canlı Kod Tabanı: `Assets/_Stratocraft/**`  

---

## 1. YÖNETİCİ ÖZETİ VE KAPSAM

Kullanıcının talimatı doğrultusunda, Faz 1 ve Faz 2 mekaniklerinin **Stratocraft Oyun Tasarım ve Mantık Anayasası (Game Logic Bible v7.0)** ile canlı kod tabanımız arasındaki uyumu satır satır denetlenmiştir. 

Bu raporda, kozmetik/ekstra geliştirmeler hariç tutularak, **doğrudan oynanış mantığını, fizik yasalarını ve Anayasa kurallarını ihlal eden 6 temel eksik ve mantık hatası** tespit edilmiş; her birinin Anayasa maddesi, koddaki hatalı işleyişi ve gerektirdiği mimari çözüm netleştirilmiştir.

---

## 2. DETAYLI ANAYASA KARŞILAŞTIRMA VE MANTIK HATALARI TABLOSU

### 🛡️ 1. Zırh Sistemi ve Anatomik Bölgesel Hasar Azaltımı (Hitzones)

| Alan | Detay |
| :--- | :--- |
| **Anayasa Maddesi** | **Bölüm 4.7.A & 4.7.B:** *"Katmanlı ve Parçalı Zırh Giyim Sistemi & Bölgesel Koruma vs. Genel Karakter Buff'ları Ayrımı"*<br>• Zırh tek parça bir kostüm veya genel bir sayısal stat (`+40 Zırh`) **değildir**.<br>• 5 Anatomik Bölge: **Kafa (Miğfer)**, **Gövde (Göğüslük)**, **Kollar (Omuzluk & Kolçak/Eldiven)**, **Bacaklar (Dizlik/Greaves)**, **Ayaklar (Bot)**.<br>• **Bölgesel Koruma Kuralı:** Üzerinde en üst seviye Göğüslük, Kolçak ve Bot olan bir oyuncu **Miğfer takmamışsa**, kafasına darbe aldığında kafasında **hiç fiziksel zırh yokmuş gibi (`0 Zırh` ve `2.0x - 2.5x Kafa Çarpanı`)** çok ağır kritik hasar yer!<br>• Bot veya Göğüslükten gelen genel özellikler (Max HP +%15 vb.) tüm karaktere işler, ancak **fiziksel darbe hasarı sönümlemesi yalnızca takıldığı uzvu korur**! |
| **Canlı Koddaki Durum & Mantık Hatası** | 1. `Stage1InteractivePlayController.cs` ve `Stage1VisualHotbarAndInventoryUi.cs` içinde zırha sağ tıklandığında ekrana yalnızca sahte bir GUI metni basılmaktadır: `"Zırh giyildi: +42 Zırh"`.<br>2. Karakterin canlı veri modelinde (`CharacterVitalsComponent`) veya envanterinde bağımsız Miğfer, Göğüslük, Kolçak ve Dizlik slotları bulunmamaktadır.<br>3. `Stage1DirectionalCombatBridge.cs` veya hasar hesaplayıcı sistemler darbe aldığında vurulan uzvun (Kafa, Gövde, Sol Kol, Sağ Kol, Bacaklar) üzerinde zırh olup olmadığını denetlememekte, genel hasar formülünü çalıştırmaktadır.<br>4. Karakter rig'i üzerinde giyilen zırh parçalarının görsel olarak takılıp çıkarılması desteklenmemekte, sahnede sabit `armor_set_iron_vanguard.glb` objesi durmaktadır. |
| **Gereken Düzeltme** | 1. `CharacterVitalsComponent` ve `EquipmentSlots` içine 4 temel bölgesel zırh yuvası (`Helmet`, `Chestplate`, `Vambraces`, `Greaves`) eklenmelidir.<br>2. Gelen her saldırı isabet noktasının anatomik bölgesine (`HitZone`: Head, Torso, Arms, Legs) bakmalı; o bölgede zırh yoksa zırh sönümlemesi `0` alınarak kritik hasar çarpanı (`Head: 2.5x`, `Torso: 1.0x`, `Limbs: 0.75x`) uygulanmalıdır.<br>3. Giyilen zırh parçası yalnızca koruduğu bölgenin darbe hasarını (Kesici/Ezici/Delici) emmelidir. |

---

### 🪓 2. Ağaç Kesme ve Kütük Boylama Fiziği (The Forest Modeli)

| Alan | Detay |
| :--- | :--- |
| **Anayasa Maddesi** | **Bölüm 2.2.A, Bölüm 4.1 & Rapor DOC-PHYS-RIGID-002:**<br>• Ağaç kesildiğinde devrilen gövde dünya voksellerinden ayrılıp tek parça rijit gövde (`FallenTreeLogRigidbody`) olarak kalmalıdır.<br>• Yere düşen kütük **asla yok olup hazır bloklara veya moloza dönüşmemelidir**.<br>• Koca kütük çantaya sığmaz! Oyuncu kütüğü `[E]` ile omzuna alıp taşıyabilmeli (`+32 kg`, eller kilitli) veya baltayla yerde boylamalıdır (`Log Bucking`).<br>• **Vurulan Yerden Boylama:** Kütüğün ucundan kesilirse ($t \ge 0.7$) uçtan standart 2 metrelik taşınabilir tomruk (`BuckedTimberLog`) kopmalı, gövde kısalmalıdır. Kökten kesilirse ($t \le 0.3$) kökten kopmalı; ortadan kesilirse ikiye bölünmelidir.<br>• Kopan 2 metrelik tomruk yerde tekrar baltalandığında ancak çantaya girebilecek küçük **Yarma Odun** ve **Dallara** ayrılmalıdır. |
| **Canlı Koddaki Durum & Mantık Hatası** | 1. `Stage1StructuralCollapseBridge.TryChopFallenTreeLog` içinde kütük kesildiğinde, kütüğün `localScale.y` değeri yapay olarak ezilerek küçültülmektedir (`localScale squashing`). Bu durum silindir modelinin dokusunu basık ve biçimsiz hale getirmektedir.<br>2. Baltayla kütüğe vurulduğunda vurulan $t$ noktasına bakılmaksızın kütük her zaman ortadan iki eşit parçaya bölünmektedir.<br>3. Kesim sonucunda kütük kaba bir kübik blok gibi davranmaktadır. |
| **Gereken Düzeltme** | 1. `localScale squashing` tamamen kaldırılmalı; kütük kesildiğinde gerçek uzunluğuna ($L$) sahip yeni bir `CapsuleCollider` ve silindir örgü oluşturulmalıdır.<br>2. Vurulan yer $t \ge 0.7$ ise uçtan 2 metrelik `BuckedTimberLog` koparılmalı, ana kütük ucundan 2m kısalarak rijitliğini korumalıdır.<br>3. Koca kütükler envantere girmemeli; yalnızca omuzda taşınabilmeli, yarma oduna dönüştürüldüğünde çantaya girebilmelidir. |

---

### 🌊 3. Sıvı Fiziği (Sonlu Akışkan, 3B Hacim, Viskozite ve Göllenme - Cohesion)

| Alan | Detay |
| :--- | :--- |
| **Anayasa Maddesi** | **Bölüm 2.7.D:** *"Sonlu Hacimsel Akışkan (Finite Volume Fluid), Pürüzsüz SDF Kıyı Etkileşimi & Stokiyometrik Obsidyen"*<br>• Sıvı hücresi 16-bit'tir: `FluidLevel` ($V \in [0..255]$) ve `FluidMeta`.<br>• Düşey akış ($y \to y-1$) ve Red-Black dama tahtası yatay akış ile kütle mutlak korunur.<br>• Sıvı 3 boyutlu bir hacimdir; altına boş hava geldiğinde şelale gibi düşmeli, kapalı havuzda su birikintisi/göl oluşturmalıdır.<br>• Su ile lav temas ettiğinde tek seferlik reaksiyonla yok olur; yüksek yoğunlukta Doğal Obsidyen (`BlockOriginState = 0`) oluşturur. |
| **Canlı Koddaki Durum & Mantık Hatası** | 1. **2D Yüzey Görünümü ve Alt Yüzey Yokluğu:** `FluidSurfaceSkirtMesher.cs` yalnızca üst ve eğimli yan etek yüzeylerini örmektedir. Sıvının alt yüzeyi (`Bottom Quad`) örülmediği için (`Cull Back` nedeniyle) su havada asılıyken veya yüksekten düşerken alttan bakıldığında **tamamen görünmez olmakta ve 2D kağıt gibi durmaktadır**!<br>2. **Kohezyon ve Göllenme (Ponding) Eksikliği:** Yatay akış denkleminde bir "yüzey gerilimi / kohezyon eşiği" bulunmadığı için, düz zemine dökülen 255 seviyesindeki su 4 komşuya eşit bölünerek $255 \to 63 \to 15 \to 3$ şeklinde hızla milimetrik kağıt kalınlığına incelmekte ve arazide **yok olup buharlaşmış gibi bir görsel algı** yaratmaktadır!<br>3. **Havada Donma:** Matara boşaltıldığında akışkan hücresel otomatı (`_activeFluidChunks`) uyanmadığı için dökülen su yerçekimi simülasyonuna girmeden havada donabilmektedir. |
| **Gereken Düzeltme** | 1. `FluidSurfaceSkirtMesher` içine, altındaki hücre hava/şeffaf voksel olduğunda **Alt Yüzey Üçgenleri (`Bottom Quads`)** örme mantığı eklenmeli; sıvı tam 3B hacimsel bir kütle olarak render edilmelidir.<br>2. Yatay akış denklemine **Minimum Göllenme Eşiği ($\Delta V \ge 32$)** konulmalı; böylece düz zeminde su ince tabaka halinde dağılıp kaybolmak yerine gerçek bir su birikintisi (Puddle) olarak kalmalıdır.<br>3. Su döküldüğü anda chunk ve 4 komşusu kesin olarak uyandırılmalıdır (`WakeFluidChunkAndNeighborsStatic`). |

---

### 🧱 4. Kırılan Ham Taş / Odun vs. Yapı Bloğu ve Hayalet Önizleme Mantığı

| Alan | Detay |
| :--- | :--- |
| **Anayasa Maddesi** | **Bölüm 2.2.A & 2.2.B:** *"Arazi Şekillendirme vs. Aletle Kırma ve İtem Blok Dokunulmazlığı"*<br>• Kazma ve baltayla kırılan ham maddeler (Kaba Taş, Gevşek Toprak, Yarma Odun, Kuru Dal, Ham Cevher) **ham envanter eşyasıdır (Item Material)**.<br>• Bu ham maddeler biçimsiz parçalar olduğu için **DOĞRUDAN DÜNYAYA YAPI BLOĞU GİBİ KONULAMAZ**!<br>• Sadece tezgahlarda (Taşçı Tezgahı, Marangoz Masası) işlenip *Taş Tuğla*, *Kereste Blok*, *İşlenmiş Levha* haline getirildikten sonra mimari yapı bloğu olarak yerleştirilebilir.<br>• Madde Kristali (`Tuş C`) ise sadece el değmemiş doğal araziyi vakumlayıp geri koyabilir; aletle kırılan ham maddeleri çekemez. |
| **Canlı Koddaki Durum & Mantık Hatası** | 1. Oyuncunun elinde ham kaba taş varken sağ tıkla yere blok gibi konabiliyordu.<br>2. Oyuncunun elinde ham odun varken odun yere konulamıyordu ANCAK havada yeşil bir "PlacementGhostPreview_Cube" (Hayalet İnşa Küpü) parlıyordu. Bu durum oyuncuya "bloğu buraya koyabilirsin" hissi veriyor ama sağ tıklayınca hiçbir şey olmuyordu.<br>3. Kaba ham maddeler ile mimari yapı blokları arasındaki görsel önizleme ayrımı yapılmamıştı. |
| **Gereken Düzeltme** | 1. Oyuncunun elinde ham taş, gevşek toprak, yarma odun, kuru dal veya ham cevher seçiliyken yeşil hayalet inşa küpü (`PlacementGhostPreview_Cube`) **KESİNLİKLE KAPATILMALIDIR**.<br>2. Kaba ham maddelerin sağ tıkla doğrudan blok olarak inşası engellenmeli; ekranda açık ve anlaşılır bir diyalogla *"Kaba hammadde doğrudan inşa edilemez; tezgahta işleyiniz"* uyarısı verilmelidir.<br>3. Yalnızca elinde işlenmiş blok veya kristal aktifken şema/küp önizlemesi açılmalıdır. |

---

### 🎒 5. Başlangıç Envanteri & Yaratıcı Test Modu (Creative Mode)

| Alan | Detay |
| :--- | :--- |
| **Anayasa Maddesi** | **Bölüm 3.1 & Bölüm 3.3.A:**<br>• Normal Hayatta Kalma (Survival) modunda oyuncu oyuna **eli tamamen boş** başlar! Hotbar yuvası 0 (El) seçilidir.<br>• Oyuncu dünyada 5 adet hazır kılıç, kazma, balta ve su matarasıyla doğmaz. Her eşya doğadan toplanan dal, taş ve kütüklerle atölyede üretilir.<br>• Ancak geliştirme ve test aşamalarında tüm mekaniklerin denenebilmesi için Minecraft tarzı **Yaratıcı / Test Modu** ve tüm eşyaları içeren **Eşya Kataloğu** bulunmalıdır. |
| **Canlı Koddaki Durum & Mantık Hatası** | 1. `Stage1VisualHotbarAndInventoryUi.InitializeDefaultSlots()` metodu oyuna başlar başlamaz hotbar'ı hazır `ArmingSword`, `Pickaxe`, `IronAxe`, `Chisel`, `WaterFlask` ile dolduruyordu.<br>2. Oyuncu her PlayMode açılışında elinde tam teçhizatla doğuyor ve hayatta kalma toplama döngüsünü deneyimleyemiyordu.<br>3. Oyunda yeni eklenen zırhları, iksirleri ve blokları tek tek test edebilecek bir Yaratıcı Mod arayüzü yoktu. |
| **Gereken Düzeltme** | 1. Survival modunda başlangıç envanteri tamamen boş olmalı, oyuncunun eli (Slot 0) seçili başlamalıdır.<br>2. `[F4]` tuşu ile **Yaratıcı Test Modu** (Ölümsüzlük, sonsuz stamina/oksijen, sınırsız inşa) açılıp kapatılabilmelidir.<br>3. `[Tab]` tuşuna basıldığında açılan 5 sekmeli (Bloklar, Aletler, Zırhlar, İksirler/Yemek, Hammaddeler) **Eşya Kataloğu** üzerinden test için istenen eşya tek tıkla envantere eklenebilmelidir. |

---

### 🧪 6. Temel Hayatta Kalma Eşyaları (Zırh Parçaları, İksirler, Yiyecekler)

| Alan | Detay |
| :--- | :--- |
| **Anayasa Maddesi** | **Bölüm 3.2.A, 3.2.B & 3.3.A:**<br>• Açlık, Susuzluk, Can ve Stamina barları canlı metabolizma döngüsüne bağlıdır.<br>• Karakterin kemer keselerinde (`5, 6, 7, 8`) taşıyabileceği **Can İksiri (Health Flask)**, **Stamina İksiri (Stamina Tonic)**, **Tıbbi Bandaj**, **Pişmiş Et/Ekmek** ve **Temiz Su** bulunmalıdır.<br>• Tüketildiğinde sağ tıkla canlı barları (HP, EP, Açlık, Isı) yenilemeli ve tükenmişlik durumunu gidermelidir.<br>• Zırh parçaları (Miğfer, Göğüslük, Kolçak, Dizlik) envanterden kuşanılabilmeli ve bölgesel hasarı soğurmalıdır. |
| **Canlı Koddaki Durum & Mantık Hatası** | 1. Canlı kodda zırh parçaları eşya olarak mevcut değildi.<br>2. İksirler ve yiyecekler hotbar veya çantada bulunsa bile tüketim geri bildirimi ve canlı bar etkileşimi eksikti.<br>3. Zırh kuşanma sadece sahte metin olarak kalıyordu. |
| **Gereken Düzeltme** | 1. 4 Kademeli Zırh Seti (Deri, Ahşap, Bakır, Demir Miğfer/Göğüslük/Kolçak/Dizlik) sisteme eklenmeli.<br>2. Can İksiri (+45 HP), Stamina İksiri (+60 EP), Bandaj (+25 HP), Pişmiş Et (+30 HP, +40 EP), Temiz Su sağ tıkla tüketilebilir hale getirilmeli.<br>3. Tüketildiğinde ses, parçacık ve bar yenilenmesi senkronize çalışmalıdır. |

---

## 3. MEVCUT KOD BİLEŞENLERİNİN DURUMU

| Dosya Adı | Durum | Yapılan / Yapılacak İşlem |
| :--- | :--- | :--- |
| `Stage1VisualHotbarAndInventoryUi.cs` | **Derlendi & Doğrulandı** | Derleme hataları giderildi; Yaratıcı Mod (`[F4]`), Eşya Kataloğu (`[Tab]`), Hayatta Kalma tüketimleri ve Zırh arayüzü hazırlandı. |
| `Stage1InteractivePlayController.cs` | **İncelendi** | Kaba hammadde hayalet küp engeli bağlandı; bölgesel zırh hasar kontrolü entegre edilecek. |
| `Stage1StructuralCollapseBridge.cs` | **İncelendi** | Ağaç devrilme ve boylama kütük ölçekleme hatası tespit edildi; $t$ kesim noktasına göre boylama bağlanacak. |
| `FluidCellularAutomataJob.cs` | **İncelendi** | Kohezyon/göllenme eşiği ($\Delta V \ge 32$) eklenecek. |
| `FluidSurfaceSkirtMesher.cs` | **İncelendi** | Alt yüzey (`Bottom Quad`) örme mantığı eklenecek. |
| `CharacterVitalsComponent.cs` | **İncelendi** | 4 bölgesel zırh yuvası ve `TakeDamageRegional` metodu bağlanacak. |

---

## 4. SONUÇ VE DEVİR YOL HARİTASI

Bu denetim raporu ile Faz 1 ve Faz 2'deki tüm teorik kurallar ile canlı koddaki mantık hataları arasındaki farklar kesin olarak ortaya konmuştur. Bir sonraki aşamada bu rapordaki düzeltmeler sırayla kod tabanına uygulanacaktır.
