# 📌 Stratocraft Faz 1 & Faz 2: Acımasız Teknik Denetim ve Kök Neden Raporu

Bu rapor, Anayasa (`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI.md`) ve mevcut C# kod tabanının satır satır denetlenmesiyle hazırlanmıştır. Kullanıcının *"çoğu şey göstermelik çalışıyor"* tespiti teknik olarak **%100 doğrulanmış** olup, aşağıda her mekaniğin gerçek tamamlanma yüzdesi ve arka plandaki kök nedenleri tablo halinde sunulmuştur.

---

## 🌍 FAZ 1: DÜNYA, VOKSEL, AKIŞKAN VE YIKIM SİSTEMLERİ

| Mekanik Adı | Mevcut Durum | Tespit Edilen Buglar ve "Göstermelik" (Stub) Kısımlar | Gerçek Oyun İçin Yapılması Gerekenler |
| :--- | :--- | :--- | :--- |
| **Sıvılar (Su ve Lav)** | 🔴 **%30**<br>(Sadece Görsel) | - **Havada Doğuyor:** Zemin 8.88m'de biterken sıvılar 9.96m'de doğuyor.<br>- **Uykuda Başlıyor:** `isSettled = true` ile doğduğu için akmıyor.<br>- **Kazıya Tepkisiz:** `WakeFluidNeighborhood` metodu yazılmış ama kazı yapıldığında çağrılmıyor.<br>- **Etkileşim Yok:** Suya girince yüzme/kaldırma kuvveti, lava girince yanma hasarı yok. | Sıvıları çukurların içine göm ve `isSettled: false` ile başlat. Kazı yapıldığında komşu sıvıları uyandır. Oyuncu etkileşimini (Arşimet, Lav hasarı) canlı döngüye bağla. |
| **Dünya Üretimi & Biyomlar** | 🔴 **%35**<br>(Sabit Ada) | - **Seed Yok:** Dünya C++ SIMD ile sınırsız üretilebilir tasarlanmış ama sahneye tohumsuz, sabit 4x4 (64m) küçücük bir ada bağlanmış.<br>- **Ağaç/Bitki Yok:** Meşe ağacı, yosun veya zindanlar üretilmiyor.<br>- **Bedrock Yok:** Dünyanın altı kırılamaz Bedrock yerine Bazalt ile kapatılmış, delinebiliyor. | `worldSeed` parametresini bağla. C++ `ChunkStreamingSystem` ile sınırsız akışı aç. Gerçek 3B cevher damarları, ağaçlar ve en alta kırılamaz `Bedrock` ekle. |
| **Malzeme ve Şekiller** | 🟠 **%40**<br>(Sınırlı) | - **56 Malzemeden 8'i Seçilebilir:** Ahşap Kalas, Taş Tuğla, Mermer vb. seçilemiyor.<br>- **Cevher Hasadı Bozuk:** Demir kazınca Demir yerine "Taş" veriyor.<br>- **62 Şekilden 9'u Açık:** Mimari şekillerin çoğu çarkta yok. | `BuildableMaterials` listesini tüm malzemeleri kapsayacak şekilde genişlet. Kazılan cevherin kendi kimliğini (`matId`) düşürmesini sağla. |
| **Domino Yapısal Yıkım** | 🟠 **%55**<br>(Görseli Sahte) | - **Hileli Zemin:** Doğal zeminler gizlice `Kalıcı Yapı` yapılarak çökmesi engelleniyor.<br>- **Düşen Parça:** Çöken koca bina, tek bir sündürülmüş küp olarak düşüyor.<br>- **Pancake Yok:** Düşen kütle altındaki bloğa çarpınca kütlesi %35 yok oluyor, alt katları ezmiyor. | Zemin hilesini kaldır, çoklu-chunk (32x32x32) gerçek jeodezik hesabı çalıştır. Düşen salkımı küp yerine kendi gerçek modeliyle düşür ve alt katmanları ezen Pancake yıkımı bağla. |
| **Floating Origin & Oksijen & Kayıt** | 🟠 **%60**<br>(Kopuk) | - **Floating Origin:** Yaratıkları ve çöken binaları kaydırmayı unutuyor.<br>- **Oksijen BFS:** Su altına girince sistem hala açık havada sanıyor.<br>- **Kayıt (.sregion):** Sıvı verilerini (`_fluidChunks`) kaydetmiyor ve yüklemiyor. | Kaydırma sistemine tüm sahne unsurlarını dahil et. Su altı basınç/oksijen tüketimini bağla. Kayıt/Yükleme sistemine 16-bit sıvı dizisini entegre et. |

---

## ⚔️ FAZ 2: SAVAŞ, KRİSTAL, ALETLER VE ENVANTER

| Mekanik Adı | Mevcut Durum | Tespit Edilen Buglar ve "Göstermelik" (Stub) Kısımlar | Gerçek Oyun İçin Yapılması Gerekenler |
| :--- | :--- | :--- | :--- |
| **Kılıç Savurma & Oto-Destek** | 🔴 **%30**<br>(Animasyon) | - **Yatay Savurma Bozuk:** Kodda `dir.y` eksik! Fareyi nereye çekersen çek kılıç hep yukarıdan aşağıya (dikey) iniyor.<br>- **Serbestlik Yok:** Fiziksel kol yerine 0.32s sabit Sin/Cos animasyonu oynatılıyor.<br>- **Kamera Kilitli:** `Camera-Clutch` yumuşak boyun takibi hesaplıyor ama sahneye aktarılmayıp %100 dondurulmuş.<br>- **Oto-Destek:** Kılıcın ucuna değil 126 derecelik dev bir koniye alan hasarı vuruyor. | `cleavePitch` matematiğini düzelt (`dir.y` ekle). Sabit animasyonu kaldırıp oyuncunun faresiyle 360° serbest vektörel savurmayı aç. `1€ Filter` ve `Stable PD` torkunu tam bağla. |
| **Alet Çeşitliliği & Yerden Alma** | 🔴 **%20**<br>(Kısıtlı) | - **Elde Başlama:** Eşyalarla doğuluyor. Yere birden fazla eşya atılırsa `G` tuşu eskileri haritadan siliyor.<br>- **Eksik Aletler:** Kürek, Orak, Tırpan ve Kademe (Lv1-Lv5) farkları yok. | Oyuncuyu boş elle başlat, aletleri yerden/masadan fiziğiyle almasını sağla. Farklı tür ve kademedeki (Taş, Demir, Mithril) aletleri oyuna dahil et. |
| **Madencilik ve Kırma Süresi** | 🔴 **%25**<br>(Anında Kırılma) | - **0.45 Saniyede Her Şey Kırılıyor:** `Mathf.Max(3, ...)` zorlaması yüzünden en sert Obsidyen bile saniyesinde kırılıyor.<br>- **Uyumluluk Yok:** Kılıçla toprak, baltayla obsidyen kazılabiliyor.<br>- **Alan Kırma:** Fiziksel kazma tek vuruşta 5x5x5 alan yok ediyor. | Gerçek Anayasal kırma sürelerini bağla. Yanlış aletle veya düşük kademe aletle kazıldığında hasarı 0'a indir veya geri sektir. |
| **Madde Kristali (`C`)** | 🟠 **%40**<br>(Buglı) | - **Görünmez Önizleme:** Kılıç/Kazma eldeyken önizleme siliniyor, Nereye koyulacağı belli değil.<br>- **Vakum Efekti Yok:** Kazılan materyal kristale doğru parçacık olarak akmıyor.<br>- **1:1 Muhasebe Yok:** Başlangıçta bedava blok veriliyor, yumuşak tepecik çekilince 1 tam blok sayılıyor.<br>- **Hızlar Sabit:** Toprağı çekme ile Obsidyen çekme hızı tamamen aynı. İstatistik (stok) penceresi yok. | İnşa hayaletini sürekli göster. 3B rezonans vakum parçacıkları ekle. Başlangıç stoklarını sıfırla, gerçek 1:1 Q24.8 muhasebesi yap ve ekrana detaylı stok UI'si ekle. Sertliğe göre çekme/koyma hızını ayarla. |
| **Envanter ve Çanta (`I`)** | 🔴 **%10**<br>(Görsel UI Yok) | - **Hotbar Yok:** Ekranın altında hangi slotta ne olduğunu gösteren Kemer (Hotbar) arayüzü yok.<br>- **Çanta Çalışmıyor:** Çanta (`I`) açılıp bir eşyaya tıklanınca çantadan kemere aktarmıyor, sahte bir şekilde sadece "bakır sil, taş ver, çantayı kapat" yapıyor. | Ekran altına fiziksel Hotbar (Kemer/Sırt yuvaları) ekle. 3B Çanta örtüsü ile kemer slotları arasında gerçek eşya taşıma/kuşanma (3.0s gecikmeli) mantığını bağla. |
| **Craft (Üretim) Sistemleri** | ℹ️ **FAZ 3** | Örs başında şu an sadece sahte bir sayaç artıyor. Kapsamlı eritme, ahşap işleme ve anvil forging **Faz 3 ve Faz 7'nin** konusudur. | Faz 3'e geçildiğinde İlkel Zanaat ve Hologram İnşa sistemine başlanacak. |

---

## 🏃 ANIMASYON, RIG VE YAPAY ZEKA (AI)

| Mekanik Adı | Mevcut Durum | Tespit Edilen Buglar ve "Göstermelik" (Stub) Kısımlar | Gerçek Oyun İçin Yapılması Gerekenler |
| :--- | :--- | :--- | :--- |
| **Karakter & Yaratık Animasyonları** | 🔴 **%10**<br>(Heykel Bükme) | - **0 Kemik:** Modellerin içinde hareketli bacak/kol yok. Unity bunları tek parça (`CombinedMesh`) satranç taşı gibi okuyor.<br>- **Kalas Gibi Yatıyor:** Sürünürken (`Z`) karakter yüzüstü kalas gibi yere devriliyor. Çömelirken (`Ctrl`) diz kırmak yerine boyu eziliyor.<br>- **Kazma Donuk:** Kırma yaparken kılıç havada asılı, kazma ise hiç kıpırdamıyor. | Blender'da hiyerarşik (kalça, diz, omuz ayrılmış) `Node + Mesh` üretimi yap. `GlbHierarchyLoader` ile bu düğümleri parçalı yükle ve IK ile gerçek animasyon ver. |
| **21 Canlı AI ve Phoenix Boss** | 🔴 **%15**<br>(Stub AI) | - **Eksik Modeller:** 21 yaratıktan sadece 2'si sahnede var.<br>- **Yapay Zeka Yok:** A* yol bulma veya taşıyıcı sütun kırma çalışmıyor; körü körüne dümdüz yürüyorlar.<br>- **Kopan Uzuv Sahte:** Uzuv kopunca modelden kanlı parça düşmek yerine yere rastgele bir üçgen taş fırlatılıyor.<br>- **Kor Yumurta Yok:** Phoenix Boss 0 HP olunca yumurta olmak yerine direk yan yatıp ölüyor. Sürü/Kaçma mantığı yok. | 19 Yaratığı oyuna ekle. `DestructibleNavSpanAStar` yol bulmasını sahneye bağla. Uzuv koptuğunda o alt-meshi gizleyip kanlı kapak (`GoreCap`) açarak gerçek uzuv kopması yap. |

---

## 📝 SONUÇ VE İZLECEK YOL

Şu anki sahnede Faz 1 ve Faz 2 sistemlerinin büyük bir kısmı **arka planda (matematiksel düzeyde) yazılmış ancak sahneye bağlanmamış (kopuk)** veya **görsel/kolay yollara (stub/hack) başvurularak atlanmıştır**. 

Yeni geliştirmelere (Faz 3'e) geçmeden önce; **Sıvıların akmaması, kılıcın sadece dikey inmesi, animasyonların heykel bükme şeklinde olması, her şeyin 0.45 saniyede kırılması ve eşyaların havadan ele gelmesi** sorunlarının çözülerek oyunun "prototip" hissinden "gerçek bir PC oyunu" hissine çıkarılması gerekmektedir.