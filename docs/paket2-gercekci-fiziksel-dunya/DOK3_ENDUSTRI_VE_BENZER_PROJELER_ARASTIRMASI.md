# DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Başkaları Nasıl Yapmış?)

> **Doküman Navigasyonu (Ana 4'lü + Faz Bazlı Detaylı Geliştirme Belgeleri):**
> - [DOK1 — Hedef ve Gereksinimler](DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **[DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Bu Doküman)](DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - [DOK4 — Ana Mimari ve Fazlı Geliştirme Yol Haritası](DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)
>   - [DOK4-FAZ1 — Doğal 3B Eşya DNA'sı, Biyom Florası, Elle Avuçlama, Yumruk ve Kristal Uyanışı](DOK4_FAZ1_DOGAL_ESYA_BIYOM_VE_YUMRUK.md)
>   - [DOK4-FAZ2 — Çömelmeli Fiziksel Taş Yontma (%Kalite) ve The Forest Tarzı 3B Çanta](DOK4_FAZ2_TAS_YONTMA_VE_THE_FOREST_CANTA.md)
>   - [DOK4-FAZ3 — Serbest Malzeme Dizme, [T] Zihin Menüsü, Ateş Yakma ve 3B Yemek Pişirme](DOK4_FAZ3_ZIHIN_INSA_KAMP_ATESI_VE_YEMEK.md)
>   - [DOK4-FAZ4 — Gerçekçi Bloomery Demircilik (Kömür, Körük, Sünger Demir, Dövme) ve El Arabası](DOK4_FAZ4_BLOOMERY_DEMIRCILIK_VE_EL_ARABASI.md)

---

## 1. Karşılaştırmalı Endüstri Analizi Tablosu

| Mekanik Alanı | *The Forest* & *Sons of the Forest* | *Vintage Story* (*TerraFirmaCraft* Mirası) | *Green Hell* & *Kingdom Come: Deliverance* | **Stratocraft İçin Özgün Sentezimiz** |
| :--- | :--- | :--- | :--- | :--- |
| **Doğal Eşya Geometrisi** | Sabit tekil 3B modeller (tüm dallar ve taşlar birbirinin kopyasıdır). | Voksel dünyasında küçük blok/mikro-voksel öğeler. | Sabit yüksek poligonlu tarama modelleri (yüksek kaplama VRAM maliyeti). | **Tohumlu (Seeded) Prosedürel 3B Eşya DNA'sı (`ShapeSeed`):** Her dal, taş, yaprak ve cevher `0 Byte` ek doku belleğiyle (`StratocraftUberShader`) 8–12 benzersiz 3B şekil varyantından üretilir; yerde, elde, bezde ve geri konduğunda **%100 aynı mesh** kullanılır. |
| **Fiziksel Taş Yontma (*Knapping*)** | Yok (doğrudan bezde sopa + taş bağlanır). | Yerdeki yassı taşın üstünde `16x16` mikro-voksel kırma; yanlış vuruşta kalıp bozulur ve taş tamamen çöp olur. | *Green Hell*: Basit menü/masa birleştirmesi, alt-parça kırma yok. | **Çömelmeli `8x8` Tebeşir İzi + `%Kalite` Sistemi (`%35..%100`):** Yerdeki taşa çömelinir, çakmaktaşıyla hayalet çizgi çizilir. Çizgi içi hatalı kırıklar taşı anında yok etmez; ortaya **düşük güçlü/çatlak kazma (`%35`)** çıkarır, kusursuz işçilik **maksimum güç (`%100`)** verir! |
| **3B Kumaş Çanta & Bağlama** | Yere serilen örtüde her eşya adedi kadar 3B yığın halinde durur; ortaya koyup çarka basınca bağlanır. | Klasik `2B` kare envanter ızgarası (`3x3` üretim). | *Green Hell*: Sırt çantası cepleri + 3B üretim taşı. | **Karesiz *The Forest* Bezi + Bireysel Varyasyon + Bağlama Animasyonu:** `6x4` kare pedler ve 2B pencereler kaldırılır; bezdeki 8 dalın 8'i de kendi eğriliğiyle durur; ortada başlık + dal + lif gerçek bir **3B sarma animasyonuyla** birleşir ve yontulan taş mesh'i aletin ucunda korunur. |
| **Fiziksel Yapı Kurma** | *TF1*: Yere hayalet plan koyup `[E]` basma (yapay). *SotF*: Eldeki dalı dizde kırıp yere çapraz dizme, etrafına taş koyma. | Voksel hücresine katman katman odun/kömür/kil dizme. | *Green Hell*: Hayalet iskelet içine malzeme takma. | **Serbest Dizme + `[T]` Zihin Menüsü (Esnek Nokta Bulutu Doğrulama):** Oyuncu yaprak, dal, taş ve kili kendi eliyle dizer. `[T]` tuşuyla 2.8 sn silik bir fikir silüeti görebilir; onayladığında sistem `1.8m` yarıçaptaki parçaları kontrol eder ve **oyuncunun dizdiği taşların/dalların şeklini bozmadan** yapıyı canlandırır! |
| **Kamp Ateşinde Yemek Pişirme** | Ateş üstüne et koyma; zamanla pişme ve unutulursa kömürleşme. | Tencere / şiş üzerinde sıcaklık tabanlı pişirme. | *Green Hell*: Ateş köz noktalarına 3B et bırakma, görsel renk/duman değişimi ve yanma. | **3B Köz Üstü Pişirme Durum Makinesi:** Sağ tıkla bırakılan `Çiğ Et` (`Pembe`) -> `Pişmiş Et` (`Kızarmış Kahve`, hafif büzülmüş mesh) -> `[E]` ile alınmazsa `Yanmış Kömür Et` (`Siyah`, yenmez). |
| **İlkel Metalürji & Demircilik** | Gerçek metalürji yok. | **Endüstri Altın Standardı:** Odun kömürü çukuru (`Charcoal Pit`) -> Cevher kırma -> `Bloomery` bacası -> `Iron Bloom` (Sünger Demir) -> Örste sıcak döverek cüruf (`Slag`) atma. | *KCD*: Körük çekerek demiri doğru kor rengine (`Sarı-Beyaz`) ısıtma, soğuyunca tekrar ocağa sokma ve çekiç ritmiyle dövme. | **[demircilik.txt](../../docs/demircilik.txt) Tam Uyarlaması (`Vintage Story` + `KCD`):** Havasız toprak altı kömür yığını + Cevher kavurma/taş çekiçle kırma + **Kil Hava Borusu (`Tuyere`) & Deri Körük** pompalanan İzabe Ocağı (`1100 °C` Bakır, `1250 °C` Cüruf akışı + **Sünger Demir**) + Yassı granitte sıcak döverek katlama! |
| **Yığın Yük Taşıma** | **Log Sled (Kütük Kızağı):** İki kolundan tutulan, içine kütük, taş ve dal yığılan fiziksel araba. | Sandık / küfe taşıma. | Elde kucaklama (yavaşlatır). | **Fiziksel El Arabası / Ahşap Yük Kızağı:** Kum, toprak, kil, taş, cevher ve odunu (`36` parçaya kadar) kasasında gerçek 3B yığın olarak taşıyan, arazi eğimine uyum sağlayan tekerlekli el arabası. |

---

## 2. Matematiksel Modeller ve Algoritmik Çıkarımlar

### 2.1. Tohumlu (Seeded) Prosedürel 3B Eşya DNA'sı (`0` Kaplama Belleği)
Kaplama VRAM bütçemiz `< 16.0 MB` (ve yeni kaplama hedefimiz `0 MB`) olduğu için *Green Hell* gibi fotogrametri kaplamaları yerine, mevcut `FacetedVoxelTriangle` + `StratocraftUberShader` (`Color32 + Roughness + Metallic + Temp`) altyapısını **Deterministik Geometri Varyasyonu** ile birleştiriyoruz:
- Her eşya örneği yalnızca `16 Byte` büyüklüğünde bir `PhysicalItemShapeDna` taşır:
  - `uint ShapeSeed`: `pcg_hash(chunkSeed, spawnIndex)` ile üretilen tohum. İmalatçı fabrika (`PhysicalItemMeshFactory`), her eşya sınıfı için (`Stick`, `RoughStone`, `FlintStone`, `LeafPile`, `DirtClod` vb.) `12` adet önceden hesaplanmış (`pre-baked`) varyant mesh önbellekte tutar (`ShapeSeed % 12` + `±%12` anizotropik ölçek varyasyonu). Böylece binlerce eşya olsa bile sahnede yalnızca birkaç düzine paylaşımlı `Mesh` örneği kullanılır (`0 B/kare` GC!).
  - `ulong KnapMask64`: Yontulmuş taş başlıklar için `8x8` bit maskesi (`1 = taş dolu`, `0 = kırılmış`). Oyuncunun kırdığı her benzersiz maske `Dictionary<ulong, Mesh>` içinde önbelleklenir; böylece yamuk yontulan bir kazma başı çantada da, elde sopaya bağlandığında da birebir o yamuk şekliyle görünür!

### 2.2. `8x8` Bit-Maskesi Üzerinde `%Kalite` (*Non-Punitive Knapping*) Matematiği
*Vintage Story*'de kalıp içinden tek bir mikro-voksel kırmak tüm taşı mahvedebilir; bu durum yeni oyuncular için sinir bozucudur. Kullanıcımızın tasarımı çok daha gerçekçi ve ödüllendiricidir:
- Taş levha `8x8 = 64` alt-hücreden (`ulong CurrentMask64 = 0xFFFFFFFFFFFFFFFFUL`) oluşur.
- Seçilen kalıp maskesi $M_{template}$ olsun:
  - **Kalıp İçi Çekirdek ($C_{in}$):** $\text{popcnt}(M_{template})$
  - **Atılması Gereken Dış Fazlalık ($E_{out}$):** $\text{popcnt}(\neg M_{template})$
- Anlık taş durumu $M_{cur}$ iken:
  - Kalan dış fazlalık oranı:
    $$R_{excess} = \frac{\text{popcnt}(M_{cur} \land \neg M_{template})}{E_{out}}$$
  - Korunan iç çekirdek oranı:
    $$R_{core} = \frac{\text{popcnt}(M_{cur} \land M_{template})}{C_{in}}$$
- **Tamamlanma ve Kalite Kuralları:**
  1. **Aşırı Parçalanma Sınırı:** Eğer $R_{core} < 0.50$ ise (çekirdeğin yarısından fazlası yanlışlıkla kırılmışsa), taş ortadan ikiye çatlar ve `2x Çakıl Taşı`na dönüşür.
  2. **Alınabilir Başlık Eşiği:** Dış fazlalığın en az `%80`'i temizlendiğinde ($R_{excess} \le 0.20$), başlık `[E]` ile alınabilir hale gelir.
  3. **Kalite Skoru (`Quality01` $\in [0.35, 1.00]$):**
     $$Q = \text{ clamp }\left(0.35 + 0.65 \cdot \left(\frac{R_{core} - 0.50}{0.50}\right)^{1.25} - 0.25 \cdot R_{excess},\; 0.35,\; 1.00\right)$$
     - Kusursuz yontma ($R_{core} = 1.0, R_{excess} = 0.0$) $\implies Q = 1.00$ (**%100 Güç & Dayanıklılık**).
     - Acemi/hatalı yontma ($R_{core} = 0.60, R_{excess} = 0.18$) $\implies Q \approx 0.42$ (**%42 Güçlü ama hala çalışan kaba taş kazma!**).

### 2.3. `[T]` Zihin Menüsü İçin Esnek 3B Nokta Bulutu (*Fuzzy Spatial Cluster*) Yapı Doğrulama
*The Forest 1*'deki gibi yere sabit "Press E to add rock" hayaleti koymak yerine, oyuncunun serbestçe dizdiği `WorldLoosePart` nesnelerini geometrik kurallarla tanıyan hafif bir uzamsal kümeleyici tasarlıyoruz:
1. Oyuncu `[T]` menüsünden hedef yapıyı (ör. `İlkel İzabe Ocağı`) seçip baktığı noktada onayladığında, bakış noktası $P_0$ etrafında $R_{scan} = 1.8\text{ m}$ yarıçaplı küredeki tüm serbest parçalar taranır (`Zero-GC` sabit dizili tarama).
2. Parçaların ağırlık merkezi (centroid) $C = \frac{1}{N}\sum_{i=1}^{N} p_i$ hesaplanır.
3. Yapı türüne göre **Malzeme Sayımı + Geometri İnvaryantı** denetlenir:
   - **Kamp Ateşi (`Campfire`):** En az `1` tutuşturucu (`Yaprak` veya `Bitki Lifi`, $y_i \le C_y + 0.20\text{ m}$) + en az `3` `Kuru Dal / Yarma Odun` ($r_{xz} \le 0.65\text{ m}$). Opsiyonel taş çemberi (`>= 4 Kaba Taş`, $0.30\text{ m} \le r_{xz} \le 0.85\text{ m}$) varsa rüzgar koruması ve ısı verimi `+%15` artar!
   - **Kömür Yığını (`CharcoalPit`):** Merkezde en az `4` `Yarma Odun` ($r_{xz} \le 0.55\text{ m}$) + dış/üst kabukta en az `4` `Toprak/Kil Topağı` (ortalama yarıçap veya yükseklik odun çekirdeğinin dışında/üstünde, havasız örtü).
   - **İlkel İzabe Ocağı (`BloomeryFurnace`):** En az `6` `Kaba Taş` + `3` `Kil/Toprak` + `1` `Kum` + `1` `Bitki Lifi`; taşların yatay merkeze uzaklığı $0.22\text{ m} \le \overline{r_{xz}} \le 0.85\text{ m}$ (ortası boş baca halkası!) ve tepe yüksekliği $\Delta y \ge 0.55\text{ m}$ olmalıdır.
4. **Şekli Bozmadan Etiketleme:** Doğrulama geçtiğinde oyuncunun koyduğu hiçbir taş veya dal silinip tek tip fabrikasyon modelle **değiştirilmez**! Oyuncunun dizdiği `GameObject` referansları bir `LiveCraftedStation` kapsayıcısına bağlanır, yalnızca taş aralarına ince bir harç/kül katmanı ve etkileşim noktaları (köz yuvası, körük deliği, cüruf ağzı) eklenir.

### 2.4. Körük Akışlı İzabe Termodinamiği (*Newton Heating + Airflow ODE*)
[demircilik.txt](../../docs/demircilik.txt) dosyasındaki tarihsel gerçekliği birebir modellemek için her ateş ve izabe ocağı tek bir sürekli sıcaklık diferansiyel denklemiyle çalışır:
$$\frac{dT}{dt} = \frac{T_{eq}(F, A) - T}{\tau_{heat}} - k_{loss} \cdot \max(0, T - T_{env})$$
Burada denge sıcaklığı $T_{eq}(F, A)$ yakıt türüne ($F$) ve körük hava debisine ($A \in [0, 1]$) bağlıdır:
- **Kuru Dal / Yarma Odun ($F = \text{Wood}$):** Körüksüz $T_{eq} = 680\,^\circ\text{C}$, körüklü maksimum $T_{eq} = 820\,^\circ\text{C}$ $\implies$ **Et pişirir, su kaynatır, cevher kavurur; ancak Bakırı (`1085 °C`) ve Demiri (`1250 °C` cüruf + sünger demir) ASLA eritemez!**
- **Odun Kömürü ($F = \text{Charcoal}$):**
  - Körüksüz doğal çekiş ($A = 0$): $T_{eq} = 800\,^\circ\text{C}$ (metal erimez).
  - Orta ritimli körük ($A \approx 0.65$): $T_{eq} \approx 1130\,^\circ\text{C}$ $\implies$ Kil potadaki **Bakır Cevheri** erir (`Sıvı Bakır`).
  - Tam ritimli körük ($A \ge 0.90$, `Tuyere` + `Deri Körük` takılı): $T_{eq} \approx 1280\,^\circ\text{C}$ $\implies$ Demir cevherindeki silika gangı eriyip alt delikten **Cüruf (`Slag`)** olarak akar; tabanda akkor **Sünger Demir (`Iron Bloom`)** birikir!
- Sıcaklık $T$, doğrudan `StratocraftUberShader` içindeki `UberMeshVertex.Temp = (byte)Clamp((T - 450) / 850 * 255, 0, 255)` kanalını sürer: `550 °C` sönük bordo $\rightarrow$ `850 °C` vişne kırmızısı $\rightarrow$ `1100 °C` parlak turuncu $\rightarrow$ `1280 °C` göz kamaştırıcı sarı-beyaz!
