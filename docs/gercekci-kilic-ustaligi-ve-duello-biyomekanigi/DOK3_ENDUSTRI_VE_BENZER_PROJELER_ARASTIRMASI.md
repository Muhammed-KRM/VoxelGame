# DOK3: Gerçek Hayat (HEMA / Kenjutsu / Eskrim) ve Gerçekçi Kılıç Düellosu Oyunları Araştırması

> **Doküman Seti Navigasyonu:**
> - **DOK1 (Hedef & Gereksinimler):** [DOK1_HEDEF_VE_GEREKSINIMLER.md](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - **DOK2 (Mevcut Kod Debug & Akış):** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **DOK3 (Bu Doküman):** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)
> - **DOK4 (Adım Adım Geliştirme Planı):** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## BÖLÜM A: Gerçek Hayatta Kılıç Ustalığı ve Biyomekanik (HEMA, Kenjutsu & Eskrim)

### 1. Bir İnsan Kılıcı Savururken Adım Adım Ne Yapar? (7 Fazlı Kinetik Zincir)
Gerçek kılıç sanatında (Liechtenauer, Fiore dei Liberi, Joachim Meyer, Kenjutsu) bir darbe **proksimalden distale (ayak/yer -> kalça -> gövde -> omuz -> dirsek -> bilek -> kabza/pommel kaldıracı -> uç)** akar. Uzman bir dövüşçüde toplam temas süresi **350–550 ms**, garda dönüş dahil **700–950 ms**'dir:

| Faz | Zaman | Eklem & Kas Kinematigi | Ölçümlü Fiziksel Metrik |
| :--- | :--- | :--- | :--- |
| **1. Hazırlık (APA & Chamber)** | `0–80 ms` | Ağırlık Merkezi (CoM) `-4..-8 cm` çöker; arka ayak bileği yay gibi yüklenir. Eller telgrafı önlemek için geriye değil doğrudan hedef hattına açılır. | CoM düşüşü: `4–8 cm`; APA penceresi: `80–120 ms` |
| **2. Ayak İşi Başlangıcı (GRF)** | `80–160 ms` | Arka bacak yer reaksiyon kuvveti (GRF) üretir: kalça -> diz -> ayak bileği itişi ile *Passing Step* veya *Lunge* başlar. | Arka bacak GRF: `1.4–2.2 × Vücut Ağırlığı`; eklem hızı `400–550 °/s` |
| **3. Kalça-Gövde Burulması (X-Factor)** | `140–260 ms` | Pelvis `35°–55°` hedefe döner; göğüs kafesi elastik ataletle `15°–25°` geride kalıp (*X-Factor*) yay gibi boşalır. | Pelvis `350–500 °/s`; Toraks `550–750 °/s`; Omuz `700–950 °/s` |
| **4. Kol Uzatımı ("Hands First")** | `240–360 ms` | Tarihi altın kural: **"Önce eller uzanır, sonra ön ayak yere basar."** Dirsekler kilitlenmeden `155°–165°` açılır. | El doğrusal hızı: `4.0–6.5 m/s` |
| **5. Bilek Kırbacı & Ağız Hizalama (*Tenouchi*)** | `340–430 ms` | 2H'de üst el öne iterken alt el pommel'i geri çeker (**Push-Pull Couple**). Ağız kesme düzlemine `< ±3°` kilitlenir. | Açısal hız $\omega$: `20–35 rad/s`; Uç hızı $v = r\omega$: **`18–28 m/s`**; ön ayak teması `±50 ms` senkron |
| **6. Temas & Devam (Impact)** | `430–520 ms` | Ön ayak yere kilitlidir; vuruş **Center of Percussion (Sweet Spot)** bölgesinde gerçekleşir. | Longsword kinetik enerjisi: **`110–180 J`**; sert çarpışma tepe kuvveti `5.000–13.000 N` |
| **7. Garda Akış (Recovery)** | `520–850 ms` | Kılıç sert frenlenmez, karşı alt garda (*Pflug/Alber/Gedan*) akıp merkez hatta toplanır. | Recovery: Rapier `150–240 ms`, Katana `180–260 ms`, Longsword `220–320 ms`, Greatsword `350–480 ms` |

### 2. Saplama (Thrust / Lunge) ve Ayak İşleri (Footwork) Mesafeleri
- **Saplama Biyomekaniği (Bottoms et al., 2013):** `0–100 ms` APA -> `80–180 ms` kol öne çıkışı (`2.5–3.5 m/s`) -> `120–320 ms` arka bacak tam itişi (`> 500 °/s`) -> `320–450 ms` temas (**uç hızı `3.5–5.5 m/s`**). Adımsız kısa saplama `160–250 ms`.
- **Doğal Yer Değiştirme Tablosu:**
  - **Geçiş Adımı (*Durchtreten / Passing Step*):** `0.75–1.15 m` ileri (kesmelerde tipik `0.60–1.00 m`) — pelvis `45°–60°` doğal döner, kesmenin ana motorudur.
  - **Toplama Adımı (*Zusammenschritt / Okuri-ashi*):** `0.35–0.65 m` — yakın mesafe (*Krieg*) ince ayarı.
  - **İleri Hamle (*Lunge / Ausfall*):** `1.00–1.35 m` (`1.1–1.3 × bacak boyu`, CoM `-15..-25 cm`) — saplamanın ana uzanma mekaniği.
  - **Yan Kaçış (*Ausweichen / Voiding Step*):** `0.45–0.80 m` merkez hattan `30°–45°` çapraz yana — saldırı çizgisini boşaltıp rakibin açık yanına geçer.

### 3. Tek El (1H) vs. Çift El (2H) Kaldıraç Fiziği
- **2H (Longsword, Katana, Greatsword):** Üst el (balçak yanı) iter, alt el (pommel) çeker -> $\tau = F \cdot d_{grip}$ kuvvet çifti (`d_grip`: Longsword `18–25 cm`, Katana `22–28 cm`, Montante `32–45 cm`). Dönme ekseni iki elin ortasına kayar; ani frenleme ve havada yön değiştirme (*Winden*) son derece çeviktir.
- **1H (Rapier, Arming Sword):** Tek pivot noktası (Sınıf-3 kaldıraç). Aynı ağırlıktaki kılıcı durdurmak veya yönünü kırmak 2H'ye kıyasla **`2.2x – 3.0x`** fazla bilek torku ister; bu yüzden 1H kılıçlarda ağırlık merkezi (PoB) ele çok yakındır (`7–12 cm`).

### 4. Tarihi Gardlar (*Vier Leger* + *Kamae*)
- **Vom Tag (*Jodan*):** Eller omuz/alın hizasında, namlu `20°–45°` geriye yatık — maksimum aşağı çapraz kesme gücü.
- **Ochs (Öküz):** Eller şakak hizasında yüksek, uç doğrudan rakibin yüzüne/boğazına (`-5°..-15°`) — üst hattı kapatan yapısal çatı + yüksek saplama.
- **Pflug (*Chudan*):** Eller kaburga/göbek önünde, uç `+15°..+35°` rakibin göğsüne/boğazına — en dengeli savunma ve en hızlı saplama.
- **Alber (*Gedan*):** Eller kasık önünde, uç `-30°..-45°` yere yakın — üst hattı bilerek açan davet gardı ve yukarı karşı kesik (*Absetzen*).
- **Hangetort:** Asılı kama gardı — ağır kesmeleri eğik rampa gibi kenara kaydırır.

### 5. Blok, Saptırma, Geri Savrulma ve Bağlanma (*Bind / Winden*)
- **Sert / Dik Blok (90° veya Foible teması):** Momentum (`15–30 N·s`, Greatsword'da `35–55 N·s`) neredeyse tamamen savunmacıya geçer -> arka ayak **`15–40 cm`** (ağır darbede `55 cm`) geriye atılır, dizler `10°–20°` çöker, yüksek Posture/Stamina kaybı + `120–250 ms` gard sarsılması yaşanır.
- **Forte-on-Foible Saptırma (`30°–50°` açılı):** Savunmacının güçlü alt kısmı (`r_def ≈ 0.25 m`) saldıranın zayıf ucunu (`r_att ≈ 0.80 m`) karşıladığında **`3.2 : 1` mekanik kaldıraç avantajı** doğar. Geri savrulma **`0–5 cm`**, stamina maliyeti `%25`'e düşer, uç rakibin üstünde kalır -> anında **Riposte / Winden**!
- **Ağız vs. Düz Yüzey:** Ağız-omurga ekseni bir **I-kiriş** gibi en yüksek bükülme direncini verir ve *Bind* kurar; düz yüzey (*Flat*) statik ağır blokta kamçılanıp çöker ancak dinamik açılı kaydırmada (*Glietzhaw*) enerjiyi yana saptırır.
- **Bind, Fühlen & Winden:** Temas anında rakip sert basıyorsa (*Hart*) baskıya karşı itilmez, altından/üstünden geçilir (*Durchwechseln*); yumuşaksa (*Weich*) üzerinden bastırıp saplanır. **5 Meisterhäue** (*Zornhau, Zwerchhau, Schielhau, Krumphau, Scheitelhau*) tek tempoda hem blok hem saldırı yapar.

### 6. Dar Alan, Duvar ve Silah Arketipleri
- **Mesafe Kuşakları (*Maß*):** `> 2.2m` *Zufechten* (adım şart) | `1.3–2.2m` tam yay kesme | `0.55–1.3m` *Krieg* (geniş kesik boğulur -> *Halbschwert*, *Zwerchhau*, kısa saplama) | `< 0.55m` *Ringen* (**Pommel / *Knaufschlag***, omuz/kalkan itişi, hançer).
- **Duvar Çarpma Fiziği:**
  - *Taş / Metal:* Restitüsyon $e \approx 0.35\text{–}0.55$, ani geri sekme (`4–10 m/s`), `200–400 ms` bilek şoku/stagger, kıvılcım ve keskinlik aşınması.
  - *Ahşap:* $e \approx 0.10\text{–}0.20$, düzgün ağızda `1–4 cm` saplanma (*Lodge*) -> kurtarmak `180–350 ms` çekme işi ister.
- **5 Arketip Özeti:** Longsword (`1.45 kg`, 2H dengeli *Winden*) | Rapier (`1.10 kg`, 1H uzun Lunge, dar alanda zayıf) | Katana (`1.15 kg`, 2H hızlı *Tenouchi* çekme-kesme, `20–28 m/s`) | Greatsword/Montante (`2.80 kg`, `220–380 J` momentum, dar alanda zorunlu *Ricasso/Half-Sword*) | 1H Kılıç + Kalkan/Buckler (dar alanda en verimli, MS I.33 bağımsız el koruması).

---

## BÖLÜM B: Gerçekçi Kılıç Düellosu Oyunları Kıyaslamalı Analizi

| Oyun | FPS/TPS Eller & Tutuş | Ayak İşi Lunge & Blok Geri İtmesi | Dar Alan & Duvar Çözümü | Stratocraft İçin Çıkarım |
| :--- | :--- | :--- | :--- | :--- |
| **Half Sword** | Active Ragdoll elleri doğrudan kabzaya ve dar alanda orta namluya (*Half-Sword*) kenetlenir. | Tam fiziksel kütle sürüklenmesi; dengesiz darbede tökezleme. | Duvara fiziksel takılma; dar alanda oyuncu otomatik/elle *Half-Sword* veya saplamaya geçer. | Dar alanda *Halbschwert* soketi ve fiziksel ağırlık hissi; ancak saf ragdoll gecikmesinden kaçın! |
| **Hellish Quart** | Mocap + fizik hibrit bıçak; eller her zaman kabza soketinde. | İleri adım kesiğe gerçek ivme katar; blok/isabette organik geri adım. | Çok yakında uzun kesik otomatik iptal -> kısa saplama, kabza veya el itmesi (*Hand Push*). | **Long Guard -> Bind & Winden** ve çok yakında otomatik yakın-mesafe aksiyonu. |
| **Exanima** | Tam prosedürel omuz-dirsek-bilek zinciri. | Saldırı yönüne adım atmak gücü ciddi artırır; geri kaçarak vurmak gücü düşürür. | Koridorda geniş yatay kesik duvara çarpıp momentumu sıfırlar -> saplama/dikey kesik zorunlu. | Yerden yukarı kinetik bağ ve bağıl hız çarpanı. |
| **Kingdom Come: Deliverance 1 & 2** | **True First-Person + 2-Kemik IK** kabza soketleri; ağır blokta kol rigi geri teper. | **Stamina = Kalkan**, sıfırlanınca darbe HP'ye işler; yakın mesafede otomatik **Clinch**. | Engel açıklık kontrolü; dar alanda kısa silah avantajı. | **2-Kemik Analitik IK** ile kabzaya kilitli kollar + kamera çökme/recoil hissi. |
| **Mordhau & Chivalry 2** | Ayrı ölçekli FPS Viewmodel kollar (silah ölçeğinden bağımsız). | `Release` ilk %45'te sinüsoidal **Lunge** + `< 1.15m` **Anti-Overshoot**; blokta kontrollü geri kayma + Kick/Jab. | Windup'ta esnek, Release'de anında temiz **Wall Clank & Interrupt** (sıfır çırpınma). | **Turn Cap**, **Anti-Overshoot Lunge**, **4-Adım Süpürme Duvar Sekmesi** ve `Jab/Pommel`. |
| **Mount & Blade II: Bannerlord** | 4-yönlü kamera + net kol silueti. | **Bağıl Hız** (`Dot(v_rel, hitDir)`) + **Sweet Spot** (dış 1/3) + **Hilt-Hit Glance** (`u < 0.28`). | Yakın temas/duvarda anında **Glance/Bounce** (boğuk ses, düşük hasar). | **Bağıl Hız + Sweet Spot + Hilt-Hit Glance** formülü Stratocraft hasar motoruna birebir uyar! |

---

## BÖLÜM C: Stratocraft İçin Altın Sentez

Saf fizik (Half Sword/Exanima) bazen "lastik/sarhoş kol" hissi yaratır; saf animasyon (klasik RPG) ise ağırlıksız kalır. **Stratocraft'ın 240 Hz Symplectic PD + Durum Makinesi + 2-Kemik Analitik IK hibriti** iki dünyanın en iyisini birleştirir:
1. **0 ms Oyuncu Niyeti** (jest ve blok anında algılanır) + **Silah Bazlı Turn Cap** (ağırlık disiplini).
2. **Kabza Soketlerine 2-Kemik IK ile Kilitli Eller** (duvar itmesinde, blokta, saplanmada ve *Half-Sword*'da eller kabzadan asla kopmaz, kılıç ölçeği elleri deforme etmez).
3. **3 Bölgeli Histerezisli Duvar Kontrolü** (`< 0.62m` pürüzsüz katlama & geniş kesik kilidi | orta mesafe 4-adım süpürme Wall Clank | açık alan tam serbestlik).
4. **Tam Kinetik Zincir & Denge:** Passing Step/Lunge (`Anti-Overshoot`) + Yaratık & NPC Blok Geri Savrulması (`0-kare gecikme`) + `Posture` + `Bind & Winden` + `Sweet Spot / Hilt-Hit / Bağıl Hız`.
