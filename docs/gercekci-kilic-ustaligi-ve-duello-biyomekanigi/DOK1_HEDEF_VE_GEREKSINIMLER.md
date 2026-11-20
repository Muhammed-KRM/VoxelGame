# DOK1: Gerçekçi Kılıç Ustalığı ve Düello Biyomekaniği — Hedef ve Gereksinimler

> **Doküman Seti Navigasyonu:**
> - **DOK1 (Bu Doküman):** [DOK1_HEDEF_VE_GEREKSINIMLER.md](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - **DOK2 (Mevcut Kod Debug & Akış):** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **DOK3 (Gerçek Hayat HEMA/Kenjutsu & Oyunlar Araştırması):** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - **DOK4 (Adım Adım Geliştirme Planı):** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. Amaç ve Temel Vizyon (Biz Ne Yapmak İstiyoruz?)

Stratocraft'ın 240 Hz yönlü kılıç sistemi temel savurma hissinde sağlam bir zemine oturdu; ancak kılıcın ekranda havada tek başına yüzmesi yerine **gerçek bir insan bedeninin kılıcı nasıl tuttuğunu, nasıl adım attığını, nasıl tork ürettiğini, darbe aldığında nasıl geri savrulduğunu ve dar alanda nasıl davrandığını** birebir yansıtan **Gerçekçi Kılıç Ustalığı & Düello Biyomekaniği** mimarisine yükseltmek istiyoruz.

**Temel Hedef İlkeleri:**
1. **Tam Vücut Kinetik Zinciri:** Kılıç yalnızca bilekten dönmez; güç yerden (ayak) başlar -> diz/kalça -> gövde rotasyonu -> omuz -> dirsek -> bilek -> iki elin it-çek kaldıraç mekaniği ile uca aktarılır.
2. **Görünür, Anatomik El-Kol Bağlantısı (FPS & TPS):** Oyuncu kendi zırhlı/eldivenli ellerini ve önkollarını kabzayı gerçekçi biçimde kavrarken görmelidir (1H'de tek el, 2H'de iki el, dar alanda yarı-kılıç tutuşu, sol kol kopuksa tek el!).
3. **Fiziksel Ayak İşi (Footwork) ve Geri Savrulma:**
   - İleri savurmada / saplamada doğal **geçiş adımı (Passing Step)** veya **hamle adımı (Lunge)** ile mikro ileri kayma (`0.35m – 0.90m`, düşmanın içine dalmayan *Anti-Overshoot* korumalı).
   - Ağır bir darbeyi bloklarken **gerçek momentum aktarımı**: zayıf/dik blokta savunmacı `0.20m – 0.55m` geri savrulur ve diz/kamera çökmesi hisseder; zamanlanmış **Forte-on-Foible** saptırmada geri savrulma sıfıra yakın olur ve yan kaçış adımıyla anında **Riposte** açılır.
4. **3 Bölgeli Dar Alan & Duvar Fiziği:**
   - Çok yakında (`< 0.62m`): geniş savurma fiziksel olarak imkânsızdır — kılıç baş aşağı sapıtmak yerine göğse dik/yarı-kılıç pozisyonuna toplanır; geniş kesik yerine **Kabza Darbesi (`[F]`)**, **Kısa Saplama** veya **Yarı-Kılıç (*Halbschwert*)** yapılır.
   - Orta mesafede (`0.62m – namlu+0.25m`): savurma başlar, namlu duvara değdiği anda kıvılcım ve metalik çınlama ile temiz **Duvar Sekmesi (Wall Clank)** veya ahşapta **Saplanma (Lodge)** oluşur.
5. **Silah Çeşitliliği ve Gerçek Ustalık Katmanı:** Longsword, Rapier, Katana ve Greatsword yalnızca hasar/hız çarpanı değil; bambaşka **el-kol kaldıraçları, adım mesafeleri, Turn Cap (dönüş limiti), Denge (Posture), Bind/Winden (kılıç bağlanması)** ve **Bağıl Hız / Sweet-Spot / Hilt-Hit** fiziği üretmelidir.

---

## 2. Bir Kılıç Ustası Savaşta Adım Adım Ne Yapar? (Eksiksiz Aksiyon Kataloğu)

Oyuna adım adım eklenecek her insan hareketi aşağıda başlangıçtan bitişe faz faz tanımlanmıştır:

### 2.1. Kesme Saldırısı (Slash — Örn. *Oberhau / Zornhau / Yatay Kesik*)
1. **Hazırlık / Kurulma (`0–80 ms`, Chamber & APA):** Ağırlık merkezi hafif çöker (`-4..-6 cm`), omuz ve gövde kesilecek yönün tersine `15°–25°` ön-burulma (anticipation) yapar.
2. **Önce Eller + Adım Başlangıcı (`80–160 ms`, Hands First & Passing Step):** Eller hedef hattına açılırken arka ayak yeri iterek gövdeyi ileri (`0.40–0.80 m`) ve hafif yana taşır.
3. **Gövde-Omuz-Bilek Kırbacı (`160–320 ms`, Kinetic Chain & Release):** Kalça -> göğüs -> omuz -> dirsek -> bilek sırayla hızlanır; çift elde üst el iterken alt el pommel'i gövdeye çeker (**Push-Pull Couple**).
4. **Temas ve Ağız Hizalaması (`320–420 ms`, Impact):**
   - **Sweet Spot (`u = 0.68..0.92`) + Düzgün Ağız (`< 35°`):** `1.25x` temiz kesik, kanama/uzuv zayıflatma.
   - **Hilt-Hit / Çok Yakın (`u < 0.28`):** Boğulmuş kabza teması (`0.35x` künt hasar, boğuk ses — Bannerlord/gerçek hayat kuralı).
   - **Flat-Blade Slap (`> 55°` ters bilek):** `0.35x` yassı çarpma.
   - **Bağıl Hız Bonusu:** İleri yürürken/adım atarken kesmek `+%10..%25` kinetik bonus; geri kaçarken kesmek `-%15..%20` ceza verir.
5. **Devam ve Doğal Garda Akış (`420–720 ms`, Follow-Through & Recovery):** Enerji yapay frenle kesilmez; kılıç karşı alt garda (*Pflug / Alber*) akar ve oradan seçili durağan garda toplanır.

### 2.2. Saplama Saldırısı (Thrust / Stich & Fencing Lunge)
1. **Hat Kilitleme (`0–90 ms`):** Uç doğrudan hedef merkezine hizalanır; dirsek gövdeye çekilir.
2. **Patlayıcı İleri Hamle (`90–220 ms`, Lunge):** Ön ayak `0.65–1.05 m` ileri atılır (Rapier `Fencing_Punta` duruşunda en uzun menzil), ağırlık merkezi alçalır (`-10..-18 cm`), kol düz uzanır. *Anti-Overshoot:* Düşman `< 1.15m` içindeyse ileri kayma sıfırlanır.
3. **Çekiş ve Garda Dönüş (`220–420 ms`):** Kol ve ön ayak senkronize geri çekilir; bu sürede savunma gardı açık kalır.

### 2.3. Blok, Saptırma, Geri Savrulma ve Kılıç Bağlanması (Parry, Deflect, Pushback & Bind/Winden)
1. **Sert / Dik Blok (Hard Block / Geç Blok):**
   - Gelen momentum (`kütle × hız`) savunmacıya aktarılır.
   - Savunmacı **`0.20–0.50 m` geriye kayar/adım atar** (Golem/Greatsword darbesinde `0.55–0.90 m`).
   - Kamera hafif çöker (`-3..-6 cm` diz amortisörü), **Posture (Denge)** ve Stamina düşer, `150–250 ms` gard sarsıntısı oluşur.
2. **Zamanlanmış Forte-on-Foible Saptırma (Master Deflect):**
   - Oyuncu kılıcının güçlü alt kısmını (*Forte*, `u < 0.40`) rakibin zayıf ucuna (*Foible*, `u > 0.60`) `30°–50°` açıyla yerleştirip doğru anda karşı hamle yaptığında:
   - Geri savrulma neredeyse **sıfırdır (`0–0.06 m`)**; bunun yerine savunmacı yana küçük bir **kaçış adımı (sidestep slip)** atar, Posture yenilenir ve **`0.35 s` Riposte** penceresi açılır!
3. **Kılıç Bağlanması ve Sarma (Bind & Winden):**
   - İki kılıç kafa kafaya geldiğinde anında sekip kopmak yerine `0.25–0.55 s` sürtünmeli temas (**Bind**) kurulabilir.
   - Bu pencere içinde oyuncu fareyi rakibin zayıf açısına kırarsa (**Winden / Sarma**), rakibin kılıcını yana düşürerek doğrudan iç hattan karşı saplama/kesik çıkarır.
4. **Denge Kırılması (Posture Break / Guard Crush):**
   - Sürekli sabit blok tutmak veya ağır darbeye uyumsuz tek-el stille yakalanmak **Posture** barını `0`'a düşürür -> `0.6–0.8 s` açık sendeleme + sert geri savrulma!

### 2.4. Dar Alan, Duvar ve Sıfır Mesafe (*Krieg* & *Ringen am Schwert*)
1. **Çok Yakın Mesafe (`< 0.62 m`):**
   - Geniş savurma **başlamadan engellenir**; kılıç göğse yakın dikey/yarı-kılıç pozisyonuna pürüzsüzce katlanır (asla baş aşağı dönmez!).
   - Geçerli aksiyonlar: **`[F]` Kabza Darbesi (Pommel Strike / Knaufschlag)**, **Tekme / Omuz İtmesi (mesafe açar)**, **Yarı-Kılıç (*Halbschwert*) Kısa Saplaması**.
2. **Orta Yakın Mesafe (`0.62 m .. namlu+0.25 m`):**
   - Geniş savurma başlar; 4 alt-adımlı süpürme sırasında taş/metal duvara temas ederse **Duvar Sekmesi** (kıvılcım + metalik çınlama + bilek şoku + `0.25s` recovery); ahşaba düzgün ağızla vurursa **Saplanma (Lodge)** oluşur.
   - Oyuncu isterse dar koridorlarda **Dikey Kesik (Overhead)** veya **Saplama (Thrust)** ile duvara değmeden dövüşebilir.

### 2.5. Tarihi Gard Duruşları ve Geçiş Akışı
Mevcut 4 stilin (`HighGuard_VomTag`, `Fencing_Punta`, `Iaido_QuickDraw`, `TwoHand_Juggernaut`) içinde fare konumu doğal tarihi gardlara akar:
- **Yukarı (`Vom Tag / Jodan`)**: Yüksek kesme gücü, açık alt hat.
- **Yüksek-İleri (`Ochs`)**: Baş koruması + yüksek saplama hattı.
- **Orta-Alt İleri (`Pflug / Chudan`)**: Dengeli saplama ve orta savunma.
- **Alçak (`Alber / Gedan`)**: Davet gardı + yukarı aparkat/karşı kesik.

---

## 3. İş Paketleri ve Kabul Kriterleri (Faz K.1 – K.6)

| Faz | İş Paketi | Kabul Kriteri (Ölçülebilir Hedef) |
| :--- | :--- | :--- |
| **K.1** | **2-Kemik Analitik IK FPS & TPS El-Kol Rigi** | Kabza soketlerine (`RightGrip`, `LeftGrip`, `HalfSwordGrip`) kilitli, kılıç ölçeğinden bağımsız 2-kemik IK kollar; 1H/2H/sol-kol-kopuk dinamik geçiş; `nearClipPlane` kırpılması yok; TPS'te sol el de 2H kabzayı tutar. |
| **K.2** | **Proaktif 5-Işın Dar Alan & 4-Adım Duvar Süpürmesi** | Histerezisli `crampedFold01` katlaması (0 Hz titreşim, 0 ters dönme); `< 0.62m` geniş kesik kilidi + dar alan *Halbschwert* kısa saplama; orta mesafede 4-alt-adımlı süpürme ile temiz taş sekmesi / ahşap saplanması. |
| **K.3** | **Ayak İşi Lunge, Anti-Overshoot & Blok Geri Savrulması** | Kesme ve saplamada gerçek ileri adım (`0.35–0.90m`), `< 1.15m` Anti-Overshoot; **hem Düellocu NPC hem tüm dünya yaratıklarında** blok/parry kütle-hız bazlı pushback + X/Z Wall-Slide + aynı karede (0-kare gecikme) uygulama + MasterDeflect yan kaçış adımı. |
| **K.4** | **Denge (Posture), Bağıl Hız, Sweet-Spot & Hilt-Hit Glance** | Oyuncu & düşman `Posture (0..100)` barı; ileri/geri bağıl hız hasar çarpanı (`-20% .. +25%`); `u < 0.28` Hilt-Hit Glance (`0.35x`) ve `u ∈ [0.68..0.92]` Sweet-Spot (`1.25x`); silaha özel **Turn Cap** (`115°–260°/s`). |
| **K.5** | **Bind & Winden (Kılıç Kilidi & Kaldıraç Kaydırma)** | Blok/Clash anında `0.25–0.55s` sürtünmeli kılıç teması (*Bind*); Forte-on-Foible kaldıraç okuması ve fareyle yön kırarak (*Winden*) rakip kılıcını düşürüp kontra saplama açma. |
| **K.6** | **Yarı-Kılıç (*Halbschwert*) Modu & Kinetik Zincir Cilası** | Dar alanda veya ağır zırhlı düşmana karşı `[X]` / yakın-mesafe **Half-Sword** tutuşu (sol el orta namluda); savurma başı mikro omuz ön-gerilmesi, diz çökme kamera amortisörü ve NUnit + PlayMode doğrulama paketi. |

---

## 4. Performans ve Kalite Kısıtları
- **0 B/kare GC Alloc** (tüm IK, 5-ışın duvar taraması, Posture ve Bind hesapları stack/struct tabanlı).
- **< 16.0 MB VRAM** (tüm eldiven/kolçak/zırh parçaları UV-Free `Color32` prosedürel veya mevcut GLB/SVOX shader ile).
- **0 Error / 0 Warning** (`.NET 10` + Unity 6 `6000.6.7f1`).
- **Tepkisellik Kuralı:** Fiziksel gerçekçilik asla "sarhoş/lastik kol" gecikmesi üretmemeli; oyuncu niyeti `0 ms` gecikmeyle okunur, ağırlık ve atalet 240 Hz PD + Turn Cap üzerinden hissedilir.
