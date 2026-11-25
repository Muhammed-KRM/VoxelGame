# DOK4 — Tam Akış Diyagramı ve Matematiksel Model (Tek-Parça Yapı Fiziği & 3 Katlı Kütük Ev)

> **Belge Seti Navigasyonu:**  
> [`DOK1 — Hedef ve Gereksinimler`](DOK1_HEDEF_VE_GEREKSINIMLER.md) | [`DOK2 — Mevcut Kod Debug ve Akış Raporu`](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) | [`DOK3 — Endüstri, Simülasyon, Açık Kaynak ve YouTube Araştırması`](DOK3_ENDUSTRI_SIMULASYON_OSS_YOUTUBE_ARASTIRMASI.md) | **[`DOK4 — Tam Akış Diyagramı ve Matematiksel Model`](DOK4_TAM_AKIS_DIYAGRAMI_VE_MATEMATIK_MODELI.md)** *(Bu belge)* | [`DOK5 — Adım Adım Geliştirme ve Doğrulama Planı`](DOK5_ADIM_ADIM_GELISTIRME_VE_DOGRULAMA_PLANI.md)

> [!IMPORTANT]
> **Ana Mantık Koruma Kuralı:** Geliştirme veya PlayTest sırasında bir hata çıktığında bu belgedeki ana fizik mantığı bypass edilmez (fizik susturulmaz). Akışta bir değişiklik gerekirse **ana mantığı bozmadan** önce bu belgedeki ilgili diyagram ve formül güncellenir, ardından kod düzeltilir.

---

## 1. Çekirdek Veri Yapıları (0 B GC, Blittable, Tek-Parça Kimlikli)

Tüm yapı elemanları (`[C]` kristali dışındaki kütük, sütun, kiriş, kalas, payanda, mertek) voksel dizisine gömülmez ve birbirine kaynaşmaz. Her parça aşağıdaki 4 hafif veri yapısıyla temsil edilir:

| Veri Yapısı | Temel Alanlar | Sorumluluğu |
| :--- | :--- | :--- |
| **`RigidStructurePiece`** | `PieceId (uint)`, `IslandId (int)`, `State (StaticAnchored / DynamicActive / StaticLooseEntity)`, `Kind (Log / Post / Beam / Plank / Rafter / Brace)`, `WoodSpecies`, `LengthM`, `DiameterM`, `MassKg`, `Pos (double3)`, `Rot (quaternion)`, `Vel`, `AngVel`, `InvInertiaLocal`, `NotchFlags (None / Saddle / Dovetail)`, `PlasticTiltRad`, `Damage01`, `SplitParentId` | Tek-parça cismin değişmez kimliği, boyutları, kütlesi, pozisyonu ve kalıcı kırılma geçmişi. |
| **`SoilEmbedProfile`** | `PieceId`, `EmbedDepthM (D)`, `DirDepth4 (N, S, E, W)`, `DirSoilMat4`, `CompactionFactor (kappa: 0.35..1.0)`, `HasStoneCollar (bool)`, `PivotDepthZr`, `ResistMoment4Dir (Mu)`, `RotStiffness4Dir (K_theta)`, `AsymmetricPushVec` | Gömülü dikey sütunun 4 yöndeki zemin derinliği, dolgu sıkılığı ve $O(1)$ devrilme/kırılma kapasitesi. |
| **`CornerBond4Interface`** | `BondId`, `PieceA`, `PieceB`, `JoineryKind (FrictionOnly / SaddleNotch / Dovetail / Pegged / JoistPocket)`, `CornerLocalA[4]`, `CornerLocalB[4]`, `CornerBrokenMask (4-bit)`, `LambdaN[4]`, `LambdaT[4]`, `LastStressRatio01` | İki tek-parça cisim arasındaki **Reduced-AEM 4-köşe** temas/kertik arayüzü. |
| **`RigidPhysicsTraceEvent`** | `Tick`, `PieceId`, `IslandId`, `Phase`, `EmbedD`, `DriveMomentNm`, `ResistMomentNm`, `WoodYieldNm`, `TiltDeg`, `StressRatio`, `DecisionCode`, `DirX/Y/Z` | Her kritik kararın 0-GC ring buffer + `telemetry_logs.db` kaydı. |

---

## 2. Ana Sistem Yaşam Döngüsü Akış Diyagramı (`StaticAnchored` ↔ `DynamicActive` ↔ `StaticLooseEntity`)

```mermaid
flowchart TD
    S0["Uyuyan Yapi: StaticAnchored (0.00 ms / kare)"] --> EV{"Olay Geldi mi? (Koyma / Kirma / Kazma / Ruzgar / Darbe)"}
    EV -- "Hayir" --> S0
    EV -- "Evet: Sadece ilgili IslandId Dirty" --> A1["1. Zemin Profili Guncelle (4-Yonlu Voxel SoilParamLUT)"]
    A1 --> A2["2. Ground-Anchor BFS (Zemine Bagli Adalari Bul)"]
    A2 --> B1{"Zemine Hic Baglantisi Olmayan Ada Var mi?"}
    B1 -- "Evet" --> D1["Kopan Adayi DynamicActive Durumuna Uyandir"]
    B1 -- "Hayir" --> A3["3. Olay-Tabanli Statik Denge & Stres Coz (double PCG + 4-Kose AEM + Broms)"]
    A3 --> B2{"Herhangi Bir Sutun/Kiris/Kertik Esigi Asti mi?"}
    B2 -- "Esik < 0.70 (Guvenli)" --> L1["Elastik Sehim Gorseli Uygula + Log Yaz"] --> S0
    B2 -- "0.70 <= Esik < 1.00 (Uyari)" --> L2["Plastik Egilme + Gicirti Sesi + Talas Tozu + Sari/Turuncu Renk + Log Yaz"] --> S0
    B2 -- "Esik >= 1.00 (Kirilma / Devrilme)" --> A4["Asiri Yuklu Kose Bagini Kopar veya Sutunu Zemin Kilidinden Cikar"]
    A4 --> A5["Kaskad Kontrolu (Maks 4 Tur): Adim 2 BFS'ye Don"]
    A5 --> D1
    D1 --> D2["4. Burst XPBD Rigid Body (N_sub=12, N_iter=1) + Inertial Relief + Voxel Zemin Carpismasi"]
    D2 --> D3{"||v|| < 0.05 m/s VE ||w|| < 0.05 rad/s (0.6 sn)?"}
    D3 -- "Hayir" --> D2
    D3 -- "Evet" --> L3["5. StaticLooseEntity (0.00 ms / kare): Tek Parca Kimligi + SpatialHash Kaydi (ASLA Voxel'e Birlesmez!)"]
    L3 --> W1{"Altindaki Toprak Kazildi mi veya Darbe Geldi mi?"}
    W1 -- "Evet" --> D1
    W1 -- "Hayir" --> L3
```

---

## 3. Gömülü Sütun / Kazık Karar Akışı ve Matematiksel Formülleri

### 3.1. Akış Diyagramı (Derinlik, Zemin Türü, Açı, Ağırlık Merkezi ve Devrilme Yönü)

```mermaid
flowchart TD
    P0["Dikey Kutuk / Sutun Konuldu veya Yanindaki Toprak Kazildi"] --> P1["4 Yonde (+X, -X, +Z, -Z) ve 0.25m Dilimlerde Voxel Arazi Tara"]
    P1 --> P2["Her Dilimde MaterialId -> SoilParamLUT (gamma, phi, Kp, cu, kh) x Compaction (kappa)"]
    P2 --> P3["Etkin Gomulme Derinligi D_eff ve Donme Merkezi z_r = 0.70 * D_eff Hesapla"]
    P3 --> P4["Toplam Agirlik (W_post + W_top), Agirlik Merkezi (CoM), Yanal Yuk (F_lat) ve Asimetrik Kazı Itkisi (M_asym) Topla"]
    P4 --> P5["Bileske Devirici Moment Vektoru M_drive ve Devrilme Yonu Birim Vektoru u_hat Bul"]
    P5 --> P6["u_hat Yonunde Zemin Direnci M_u_soil(u_hat) ve Donme Yay Sertligi K_theta(u_hat) Hesapla"]
    P6 --> P7["Ahsap Govde Kirilma Momenti M_y_wood Hesapla"]
    P7 --> C1{"D_eff <= 0.05 m (Gomulmemis / Yuzeyde)?"}
    C1 -- "Evet" --> C1A["Sadece Kendi Taban Poligonu Dengesi: M_u_soil = (W_post + W_top) * (b / 2)"]
    C1 -- "Hayir" --> C2{"M_drive < 0.70 * min(M_u_soil, M_y_wood)?"}
    C1A --> C2
    C2 -- "Evet" --> R1["DURUM 1 — ELASTIK DENGE: theta_eq = M_drive / K_theta (Yuk kalkinca duzelir)"]
    C2 -- "Hayir" --> C3{"M_drive < min(M_u_soil, M_y_wood)?"}
    C3 -- "Evet" --> R2["DURUM 2 — PLASTIK ZEMIN YAMULMASI: Kalici theta_plastic artar + Gicirti/Toprak Catlagi Uyarisi"]
    C3 -- "Hayir" --> C4{"M_u_soil < M_y_wood?"}
    C4 -- "Evet (Toprak/Kum/Kil Yenildi)" --> R3["DURUM 3 — GEOTEKNIK DEVRILME: Sutun Zemin Kilidinden Cikar, u_hat Yonunde XPBD ile Devrilir"]
    C4 -- "Hayir (Kaya / Tas Takoz Tuttu)" --> R4["DURUM 4 — GOVDEDEN KIRILMA: Kok Yerde Kalir, Sutun z=0 Hizasindan 2 Ayri Parcaya Bolunur"]
    R1 --> LOG["Trace Log Yaz: pieceId, D_eff, u_hat, M_drive, M_u_soil, M_y_wood, DecisionCode"]
    R2 --> LOG
    R3 --> LOG
    R4 --> LOG
```

### 3.2. Kesin Matematiksel Formüller (Broms 1964 + IBC 1807.3 + Winkler $p\text{-}y$)

1. **Tek Parça Kütük Kütlesi ve Ağırlığı:**
   $$V = \pi \left(\frac{b}{2}\right)^2 L, \qquad m_{\text{post}} = \rho_{\text{wood}} \cdot V, \qquad W_{\text{post}} = m_{\text{post}} \cdot g$$
   *(Örnek: Yaş Çam $\rho = 800\text{ kg/m}^3$, $L = 4.5\text{ m}$, $b = 0.30\text{ m} \implies m \approx 254\text{ kg}$; Meşe $\rho = 1000\text{ kg/m}^3 \implies m \approx 318\text{ kg}$.)*
2. **Dilim Bazlı Zemin Yanal Direnci ($p_{u,i}$) ve Yay Sertliği ($k_i$):**
   - **Kohezyonsuz (Kum, Çakıl, Normal Toprak):**
     $$K_p = \tan^2\!\left(45^\circ + \frac{\phi}{2}\right), \qquad p_{u,i} = 3\,\gamma\,z_i\,K_p\,b\,\kappa\,f_m, \qquad k_i = n_h\,z_i\,\kappa$$
   - **Kohezyonlu (Çamur, Kil):**
     $$p_{u,i} = \begin{cases} 0 & z_i < 1.5\,b \\ 9\,c_u\,b\,\kappa\,f_m & z_i \ge 1.5\,b \end{cases}, \qquad k_i = k_h\,b\,\kappa$$
   - *(Yüzeyde taş takoz halkası `HasStoneCollar == true` ise üst $0.35\text{ m}$ dilime çakıl/kaya sınıfı $p_{u}$ ve $K_\theta$ eklenir; yan tarafı kazılıp uçurum açıldıysa şev indirgeme çarpanı $f_m = 0.50$, tam bitişik kazıda o dilimde $p_{u,i} = 0$ olur.)*
3. **Yön Bağımlı Toplam Zemin Devrilme Kapasitesi ($M_{u,\text{soil}}$) ve Dönme Sertliği ($K_\theta$):**
   $$z_r = 0.70\,D_{\text{eff}}$$
   $$M_{u,\text{soil}}(\hat{u}) = \sum_{z_i < z_r} p_{u,i}(+\hat{u})\,\Delta z\,(z_r - z_i) + \sum_{z_i > z_r} p_{u,i}(-\hat{u})\,\Delta z\,(z_i - z_r)$$
   $$K_\theta(\hat{u}) = \sum_{z_i < z_r} k_i(+\hat{u})\,\Delta z\,(z_r - z_i)^2 + \sum_{z_i > z_r} k_i(-\hat{u})\,\Delta z\,(z_i - z_r)^2$$
   *(Eğer $D_{\text{eff}} \le 0.05\text{ m}$ yani gömülmemişse: $M_{u,\text{soil}} = (W_{\text{post}} + W_{\text{top}}) \cdot \frac{b}{2}$.)*
4. **Devirici Moment Vektörü ($\mathbf{M}_{\text{drive}}$, $P\text{-}\Delta$ ve Asimetrik Kazı Dahil):**
   $$\mathbf{M}_{\text{drive}} = \mathbf{r}_{\text{lat}} \times \mathbf{F}_{\text{lat}} + \mathbf{r}_{\text{CoM}} \times \big((W_{\text{post}} + W_{\text{top}})\,\hat{\mathbf{g}}\big) + \mathbf{M}_{\text{active\_asym}}$$
   - Burada $\mathbf{M}_{\text{active\_asym}}$: Bir tarafı kazılan çukurda karşı taraftaki dolu zeminin uyguladığı aktif itki ($p_a = K_a \gamma z b$, $K_a = \tan^2(45^\circ - \phi/2)$) momentidir; sütunu doğrudan **kazılan boşluk yönüne** iter.
   - Devrilme yönü birim vektörü: $\hat{u} = \frac{(\mathbf{M}_{\text{drive}} \times \hat{\mathbf{y}})_{xz}}{\|(\mathbf{M}_{\text{drive}} \times \hat{\mathbf{y}})_{xz}\|}$.
5. **Ahşap Gövde Kırılma Momenti ($M_{y,\text{wood}}$):**
   $$I = \frac{\pi b^4}{64}, \qquad M_{y,\text{wood}} = f_b \cdot \frac{\pi b^3}{32}$$
   *(Tipik eğilme dayanımı $f_b$: Çam `~28 MPa`, Meşe `~42 MPa`, Demir Odunu `~65 MPa`.)*
6. **Hiperbolik $p\text{-}y$ Eğilme ve Plastik Yamulma Açısı:**
   $$\theta_{\text{eq}} = \frac{M_{\text{drive}}}{K_\theta(\hat{u}) \cdot \left(1 - 0.85\,\frac{M_{\text{drive}}}{M_{u,\text{soil}}(\hat{u})}\right)}$$
   $M_{\text{drive}} / M_{u,\text{soil}} \ge 0.70$ olduğunda $\theta_{\text{plastic}} = \max(\theta_{\text{plastic}},\; 0.55\,\theta_{\text{eq}})$ kalıcı olarak kaydedilir.

---

## 4. Yatay Kütük / Kiriş, Kertik ve 4-Köşe Reduced-AEM Kırılma Akışı

### 4.1. Akış Diyagramı (Üst Üste Kütük Dizme, Kertik Kilidi ve Kısmi Çatlama → Devrilme)

```mermaid
flowchart TD
    K0["Yatay Kutuk / Kiris / Mertek Konuldu veya Ustune Yuk Bindi"] --> K1["Temas Eden Alt/Yan Parcalari Bul -> 4-Kose Arayuzu (CornerBond4) Olustur"]
    K1 --> K2{"Kose Kertigi / Baglanti Tipi Nedir?"}
    K2 -- "Kertiksiz (FrictionOnly)" --> M1["T_max = 0, mu_eff = 0.40 (Hafif yanal yukte kayar/yuvarlanir)"]
    K2 -- "Eyer Kertigi (SaddleNotch)" --> M2["T_max = Dusuk, mu_eff = tan(atan(0.40) + 38 deg) ≈ 1.65 (Ust yukle sikisir)"]
    K2 -- "Kirlangic Kuyrugu (Dovetail)" --> M3["T_max = Yuksek (Geometrik kilit), mu_eff >= 2.2 (Disa acilamaz)"]
    M1 --> K3["Statik Denge (double Block-Jacobi PCG) veya XPBD Lagrange Carpani F = lambda / h^2 Oku"]
    M2 --> K3
    M3 --> K3
    K3 --> K4["Her 4 Kose Bagi Icin Normal (F_n) ve Kesme (F_t) Kontrol Et"]
    K4 --> C5{"Cekme (F_n > F_t_max) veya Mohr-Coulomb (||F_t|| > c*A + mu_eff*max(0,-F_n)) veya Ezilme (-F_n > F_c_max)?"}
    C5 -- "4 Kose Saglam" --> OK["Dengede: Gorsel Sehim delta = 5*w*L^4 / (384*E*I) Uygula"]
    C5 -- "1-2 Kose Koptu (Kismi Catlama)" --> PART["Kopan Kose Bitini Isaretle -> Kalan Basinc Koseleri Mentese Gibi Tutar (Egilme & Sallanma)"]
    PART --> K5{"Agirlik Merkezi Destek Poligonundan Cikti mi veya 4 Kose de Koptu mu?"}
    K5 -- "Hayir" --> WARN["Kritik Uyari: Gicirti + Talas Tozu + Egik Kalma"]
    K5 -- "Evet" --> FALL["Ada Baglantisi Koptu -> XPBD DynamicActive Devrilme & Dusus"]
```

### 4.2. Kesin Formüller (4-Köşe Reduced-AEM, Kiriş Sehimi, Çatı İtmesi & XPBD)

1. **Mohr-Coulomb & Çekme/Basınç Kırılma Kriteri (4 Köşenin Her Biri İçin):**
   - Çekme kopması: $F_{n,c} > F_{t,\max} = \sigma_t \cdot \frac{A_{\text{contact}}}{4}$
   - Kesme kayması: $\|\mathbf{F}_{t,c}\| > c_{\text{joinery}} \cdot \frac{A_{\text{contact}}}{4} + \mu_{\text{eff}} \cdot \max(0, -F_{n,c})$
   - Basınç ezilmesi: $-F_{n,c} > F_{c,\max} = \sigma_c \cdot \frac{A_{\text{contact}}}{4}$
2. **Kiriş Açıklığı Eğilme (Görsel Sehim & Kırılma):**
   $$\delta_{\text{mid}} = \frac{5\,w\,L^4}{384\,E\,I} + \frac{P_{\text{point}}\,L^3}{48\,E\,I}, \qquad M_{\max} = \frac{w\,L^2}{8} + \frac{P_{\text{point}}\,L}{4}$$
   - $M_{\max} / M_{y,\text{wood}} \in [0.75, 1.00)$ → Kiriş görsel olarak aşağı eğilir + gıcırdama/talaş uyarısı verir.
   - $M_{\max} \ge M_{y,\text{wood}}$ → Kiriş orta noktasından **kalıcı olarak 2 ayrı `RigidStructurePiece` parçasına bölünür** (`SplitParentId` ile izlenir) ve XPBD ile düşer.
3. **Çatı Dışa İtmesi (*Outward Thrust*):**
   $$H_{\text{thrust}} = \frac{W_{\text{roof}}}{2\,\tan\theta_{\text{roof}}}$$
   - Yapısal Mahya Kirişi (`RidgeBeam`) veya Alt `1/3` Gergi Kirişi (`RafterTie`) varsa $H_{\text{thrust}}$ çekme kuvveti olarak gergi kirişine aktarılır; yoksa doğrudan 3. kat üst duvarına yanal kuvvet olarak binip duvarları dışa devirir!
4. **XPBD Rigid Body Konum/Kuaterniyon Adımı ($N_{\text{sub}} = 12, N_{\text{iter}} = 1, h = \Delta t / 12$):**
   $$w_i = m_i^{-1} + (\mathbf{r}_i \times \mathbf{n})^T \mathbf{I}_i^{-1} (\mathbf{r}_i \times \mathbf{n}), \qquad \Delta\lambda = \frac{-c - (\alpha / h^2)\lambda}{w_1 + w_2 + \alpha / h^2}$$
   $$\mathbf{F}_{\text{bond}} = \frac{\lambda}{h^2}\,\mathbf{n}, \qquad e = 0 \text{ (eger } |v_n| \le 2gh \text{ ise — sifir mikro-ziplama!)}$$
5. **d'Alembert Inertial Relief (Havada Düşen Adada Sahte Kırılmayı Önleme):**
   $$\mathbf{f}_i^{\text{eff}} = \mathbf{f}_i^{\text{ext}} - m_i \Big(\mathbf{a}_{\text{com}} + \boldsymbol{\alpha} \times \mathbf{r}_i + \boldsymbol{\omega} \times (\boldsymbol{\omega} \times \mathbf{r}_i)\Big)$$

---

## 5. Tam Boy Kütük Lojistiği Akışı (Kesim → Kütük El Arabası → 2.–3. Kat Çıkarma)

```mermaid
flowchart TD
    T0["Ormanda Farkli Boy Agac (2m..6m+) Sec & Kes"] --> T1["Agac Devrilir -> Budama: Tam Boy Tek-Parca Kutuk Kalir (Otomatik 2m Bolme YOK)"]
    T1 --> T2{"Kutuk Agirligi / Boyu?"}
    T2 -- "<= 2.5m (~50-100 kg)" --> T3A["Omuzda Tasinabilir (Yavaslar + Stamina)"]
    T2 -- "> 2.5m (~150-900 kg)" --> T3B["Kutuk El Arabasi / Log Arch Gerekir (Tekerlek + Dingil + Sasi)"]
    T3B --> T4["[E] Bagla -> Strapped Compound Mass: Kutuk RB Kapanir, Arabanin Toplam Kutle + CG Guncellenir"]
    T4 --> T5["Raycast Suspansiyon (0 PhysX Patlamasi) + Egimde [Space] Takoz Freni"]
    T5 --> T6{"Hedef Kat?"}
    T6 -- "Zemin / 1. Kat" --> T7A["Temel Cukuruna Dik veya Duvara Yerlestir"]
    T6 -- "2. Kat" --> T7B["Parbuckle Rampasi (2 Egik Kutuk + 2:1 Halat + Mandal Kilidi)"]
    T6 -- "3. Kat & Cati" --> T7C["Gin Pole Vinci (Tek Direk + 3 Oto-Gergi Halati + W/S Kaldir, A/D Salinim)"]
    T7A --> T8["Akis 2 / Akis 3 Fizik Dogrulamasi + Trace Log"]
    T7B --> T8
    T7C --> T8
```

---

## 6. Zorunlu 0-GC Debug & Telemetri Loglama Akışı

Her fonksiyonun ve değişkenin neden o kararı verdiğini canlı oyunda %100 görebilmek için iki kanallı loglama zorunludur:
1. **Yüksek Frekanslı Fizik Trace (`RigidTimberPhysicsTraceRecorder` — `0 B GC` Ring Buffer → `Logs/rigid_timber_trace.jsonl`):**
   - Her olayda (yerleştirme, gömme ölçümü, zemin kazısı, 4-köşe gerilme, sehim, devrilme, kırılma, uyuma) tek satır yapılandırılmış kayıt:
     `{tick, fn, pieceId, islandId, state, embedD, dirD:[N,S,E,W], soil:[N,S,E,W], kappa, mDrive, mSoil, mWood, tiltDeg, uHat:[x,z], brokenMask, reason}`
2. **Kritik Olay & Performans Telemetrisi (`IGameLogService` → `Logs/telemetry_logs.db`):**
   - `LogPipelineStart / End` (`RigidTimber.SolveIsland`, `RigidTimber.StepXpbd`, mikrosaniye süre + parça/bağ sayısı),
   - `LogFunctionError` (NaN/Inf koruması, bütçe aşımı `> 2.0 ms`, beklenmeyen durum geçişi).
