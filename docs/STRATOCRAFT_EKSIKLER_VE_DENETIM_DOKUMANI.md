# Stratocraft — Kapsamlı Eksikler, Mantık Hataları ve Faz Denetim Dokümanı

> **Hazırlanma Tarihi:** 2026-11-19  
> **Son Güncelleme:** 2026-11-19 (v1.1 — Anayasa `v7.1` & Geliştirici Dokümanı `v1.3` Pragmatik Mühendislik Modernizasyonu ile Senkronize Edildi)  
> **Referans Sürüm:** `main` @ `b82aefd` + Yaratıcı Mod Uçuş (`Ctrl` alçalma & Çift `Space` serbest düşüş) düzeltmesi  
> **Denetlenen Kaynaklar:** `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` (`v1.3`), `STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md` (`v7.1`), `DURUM_VE_DEVIR.md`, `Tools/AutonomousAssetFactory/Stratocraft_Asset_Manifest.json`, `Assets/_Stratocraft/` kaynak kodu ve `NativeCpp/` modülleri.

---

## 1. Yönetici Özeti ve Faz Numaralandırma Gerçeği

Kod tabanı, Geliştirici Dokümanı ve Oyun Tasarım Anayasası birebir karşılaştırıldığında ortaya çıkan en önemli tespit şudur:

1. **Faz Numaralandırma Sapması (Etiket Kayması):**  
   Git etiketleri `v0.2.0-faz2` noktasında durmaktadır. Faz 2'den sonra proje geliştirme odağı **Paket 2 / 3 / 4A / 4B / 4B.2 / 4C** (erken dönem hayatta kalma, mağara/maden/lav, iklim/hidroloji/bulut, görüş-konisi ve ekran-dışı DES simülasyonu) üzerine kaymıştır. `docs/test-raporlari/faz3-raporu.md` ve `faz4*-raporu.md` dosyaları bu paketler için "Faz 3 / Faz 4" etiketini yeniden kullanmış; ancak **Anayasa Faz 3 (Klanlar/KGP/3B Çit BFS)** ve **Anayasa Faz 4 (15 Parça Zırh Delme/Kayma, Menzilli Balistik, 3 Ölüm Modu, Offline Sleeper)** henüz kodlanmamıştır.
2. **Faz 1 ve Faz 2'de "Sonradan Yapılır" Diye Ertelenmiş / Yarım Kalmış Borçlar Var:**  
   Çekirdek voksel motoru, sıvı fiziği, iklim/hidroloji, yapısal domino yıkımı ve sahne akışı güçlü çalışsa da; `Floating Origin` kaydırması, `C++20 SIMD gürültü` köprüsü, `RingDifficultyCalculator` halka bağlantısı, 3B varlık fabrikasındaki 21 canlı / 13 boss / 29 atölye açığı ve mimari şekil mesh hataları hâlâ açıktır.
3. **Mevcut Oynanışta Kritik Mantık Hataları ve Eksikler Bulundu:**  
   Kullanıcının PlayTest sırasında fark ettiği tüm sorunların (kılıç yörünge/savurma tuhaflığı, aşırı hızlı/anlaşılmaz canlı saldırıları, kaba modeller/animasyonlar, içi görünen rampa/kemer blokları, tek tip küp ağaçlar, halka-biyom kopukluğu, açlık/susuzluk yokluğu, `[1]` aynı slotta kılıç döngüsü, `[G]` parşömensiz stil değişimi, 5×5 küp yapılar, kütüklerle inşa yapılamaması) **birebir kod düzeyindeki kök nedenleri** ve bunlara ek olarak **6 gizli mantık hatası** (canlıların 2x çift hasar düşmesi, oyuncunun HP>=20 ölümsüzlük kilidi, saplama/thrust 60 vuruş/sn testere açığı, geri doğru oynayan saldırı animasyonu, hiç doğmayan çam ağaçları, `ShapeId` arayüz etiket kayması) tespit edilmiştir.

### Anayasa Faz 0 – Faz 9 Gerçeklik Matrisi

| Faz | Anayasa / Geliştirici Dokümanı Kapsamı | Kod Gerçekliği (%) | Durum Özeti |
| :--- | :--- | :---: | :--- |
| **Faz 0** | Proje iskeleti, URP Forward+, SQLite WAL telemetri, kalite kapısı | **%100** | Tamamlandı (`0 Error / 0 Warning`, VRAM `4.14 / 16 MB`). |
| **Faz 1** | 32-bit Voksel, Hibrit SurfaceNets+Şekiller, Domino Çöküşü, Sıvılar, 8 Halka, Floating Origin | **%82** | Voksel/domino/sıvı/iklim/DES çok güçlü ve Anayasa'daki eski yöntemden daha ileri; **eksikler:** `Floating Origin` devre dışı, `RingDifficultyCalculator` dünya üretimine bağlı değil, mimari şekil mesh'lerinde ters yüzey (`inverted winding`) hataları var. *(C++20 SIMD DLL zorunluluğu kaldırıldı -> C# Burst SIMD yeterli.)* |
| **Faz 2** | 3B Varlık Fabrikası (Yol A Blender/Blockbench: 21 canlı, 13 boss, 15 parça zırh, 29 atölye, 24 yapı, silahlar, Hibrit Skinned+VAT) | **%22** | Fabrika hattı çalışıyor ancak manifestte sadece **11 varlık** var (`1/21` canlı, `1/13` boss, `1/29` atölye, `1/24` yapı). PlayMode'da mevcut `.glb` modelleri dahi kapatılıp prosedürel küp/kutu rig'e düşülüyor. |
| **Faz 3** | Klanlar (3B Çit BFS, KGP, Kristal Canı, 3 Yönlü `IsSiegeable`, 6 Ritüel, Hafif SQLite WAL, Hayalet Hologram) | **%0** | `Assets/_Stratocraft/Clan/` klasörü ve tek bir C# sınıfı yok. |
| **Faz 4** | Temiz Kinematik Yönlü Kılıç (`120–240 Hz` CCD), `Master Strike`, 15 Parça 3 Katman Zırh (`O(1)` Kaplama/Stop-Rib), Menzilli Balistik, 3 Ölüm Modu, Offline Sleeper | **%25** | Yakın dövüş erken çekilip `Stage1DirectionalCombatBridge` içine yazıldı; ancak sabit akümülatörlü değil, `Master Strike`, 15 parça zırh fiziği, ok/arbalet/pilum, 3 ölüm modu ve Offline Sleeper **%0**. |
| **Faz 5** | 5 Antik Zindan, 75 Fiziksel Batarya (Dikey Direk Kilidi), 25 Mayın, 6 Yakıtlı Tuzak Çekirdeği | **%5** | Yalnızca `.svox` şablon dosyaları ve hiç çağrılmayan ölü `DungeonGeneratorSystem.cs` var; 75 batarya ve 31 tuzak **%0**. |
| **Faz 6** | 13 Ritüel Boss, SGP, 12 Küresel Felaket, 500 Canlı VAT/Instanced Gece Dalgaları, Supply Drop | **%5** | Yalnızca Anka Kuşu (Phoenix) kısmi simülasyonu var (şu an `(128, Y, -118)` konumunda parklı); diğer 12 boss, 12 felaket, SGP ve gece dalgaları **%0**. |
| **Faz 7** | `16×32×4` Örs (mevcut SurfaceNets ile), Harmonizer + 4 Katalizör, 6 Atölye / 29 Cihaz, Ustalık, 24 Klan Yapısı | **%5** | Yalnızca basit 8 vuruşlu Bloomery demir külçesi + 1 statik örs/fırın `.glb` var; dövme/harmonizer/ustalık **%0**. |
| **Faz 8** | Liebig NPK Tarım, Bozulma, `1★–5★` Genetik, Ayrık Maillard Yemek, 2B (2 Boyutlu) Faz Uzayı Simya, 13 Kafes Boss Evcilleştirme, `R` Islık, GOAP İşçiler, Hidrolik Piston | **%5** | Yalnızca yaralı At/Grifon'a `[E]` ile binme ve 12 sn kamp ateşi eti pişirme var; geri kalanı **%0**. |
| **Faz 9** | 5 Aşamalı DFA Kuşatma, Kervanlar, Kan Sözleşmesi, Takas Pazarı, Görev Loncası, Kanca/Planör, `JSON + MoonSharp Lua` Mod SDK, Listen-Server Co-op | **%3** | Yalnızca C++ `contract_crypto.cpp` (Ed25519) ve 1 mancınık `.glb` var; C# oynanış sistemleri **%0**. *(IL2CPP uyumsuz Roslyn kaldırıldı; MMO sharding `v1.0+` sonrasına ertelendi.)* |

---

## 1.5. Anayasa `v7.1` & Geliştirici Dokümanı `v1.3` Yöntem Modernizasyonu (Daha İyi Çözümle Değiştirilen 11 Madde)

Oynanış hedeflerinden hiç taviz vermeden, artık mantıklı olmayan veya bizim mevcut sistemimizin daha iyi olduğu 11 teknik yöntem her iki ana dokümanda güncellenmiştir:

1. **Arazi Meshleme:** GPU Compute / saf Binary Greedy yerine mevcut **`HybridSurfaceNetsAndShapeMesher` + `FluidSurfaceSkirtMesher`** (`100+ FPS`, `0 B` GC).
2. **Dünya Yükleme:** 3B Octree & agresif View-Cone boşaltma yerine mevcut **`360°` aktif halka + `8m×8m` `DistantHorizonLodSystem` + Ekran-Dışı DES (`ChunkDeltaLog`) + `<1 MB` `WorldHydrologyLedger`**.
3. **Çekirdek Mimari:** Zorunlu tam DOTS ECS yerine mevcut **Hibrit C# + `NativeArray` + Burst Jobs + ince `MonoBehaviour` köprüleri**.
4. **Yerel C++20 DLL:** Zorunlu C++20 genişletmesi kaldırıldı; **saf C# Burst SIMD + C# LZ4/Zstd + .NET Kripto** standart.
5. **Veritabanı & Kayıt:** IL2CPP kırıcı EF Core `WorldStateDbContext` kaldırıldı; **parametreli hafif SQLite WAL + `.sregion V3` (`RegionFileStore`) + `ChunkDeltaLog`**.
6. **3B Varlık Fabrikası:** Yerel 24 GB VRAM AI 3B (`TRELLIS`/`TripoSG`/`HY-Motion`) zorunluluğu kaldırıldı; **Yol A Headless Blender 4.2 (`bpy`) + Blockbench + `MidVoxIO` + 4 açılı PNG doğrulama** birincil hat.
7. **Animasyon:** Saf `VAT` yerine **Hibrit Animasyon** (`SkinnedMeshRenderer` + `Animator` + IK oyuncu/boss/yakın canlılar için; `VAT`/Instancing yalnızca `>40 m` 500 canlı gece dalgaları için).
8. **Yönlü Yakın Dövüş:** Aktif-ragdoll PD torku + aerodinamik kılıç sürtünmesi yerine **Temiz Kinematik Yönlü Kılıç + sabit `120–240 Hz` Burst Swept-Capsule CCD** (Camera-Clutch, `1€` filtre, silah ağırlığı, `Flat-Slap`, Forte/Foible, `Master Strike` %100 korunur).
9. **15 Parça Zırh:** UV-free shader ile çelişen `64×64` UV raycast kaldırıldı; `UniqueEquipmentInstance` (`128 B`) içinde **`O(1)` kaplama oranı + `HasStopRib` bayrakları**.
10. **Örs, Simya & Mod SDK:** Örs mevcut `HybridSurfaceNetsAndShapeMesher` motorunu paylaşır; 2B (2 Boyutlu) Simya ve Maillard pişirme ayrık vektör adımlamasıyla çalışır; **IL2CPP'de imkânsız olan `Roslyn` runtime C# derleyicisi kaldırılıp `JSON/TOML + Sandbox MoonSharp Lua` Mod SDK** benimsendi.
11. **Tek Oyunculu Öncelikli:** Faz 0'dan `Netcode for Entities` + `512×512m` MMO Sharding kaldırıldı; önce saf C# servislerle tek oyunculu çekirdek bitirilir, sonra tek transportlu **Listen-Server Co-op (2–16 oyuncu)** eklenir.

---

## 2. Faz 1 ve Faz 2'de Hâlâ Açık Olan Gerçek Teknik Borçlar

*(Not: C++20 `simd_world_noise.cpp` P/Invoke kopukluğu artık borç değildir; `v1.3` kararıyla C# Burst SIMD standart kabul edilmiştir.)*

1. **`ChunkSnappedFloatingOriginSystem` Çalışma Zamanında Devre Dışı:**  
   Devir #032 sırasında geçici olarak kapatılan `±4096 m` (veya `256 m`) merkez kaydırma (Floating Origin) yeniden açılmadı. Oyuncu merkezden kilometrelerce uzaklaştığında floating-point titremesi (jitter) riski devam ediyor.
2. **`RingDifficultyCalculator` Öksüz (Orphaned) Durumda:**  
   Anayasadaki eş merkezli zorluk halkaları matematiksel olarak `RingDifficultyCalculator` içinde tanımlı; ancak dünya üretimi, biyom seçimi, maden dağılımı ve canlı doğuşu bu sınıfı hiç kullanmıyor (yalnızca ölü `DungeonGeneratorSystem.cs` referans veriyor).
3. **`DungeonGeneratorSystem.StampAncientDungeonIfSelected` Ölü Kod:**  
   Zindan yerleştirme sistemi yazılmış ama `StratocraftFloatingIslandWorldGen.cs` içinden hiç çağrılmıyor.
4. **3B Varlık Fabrikası (`Assets/Art/Generated/`) Üretim Açığı:**  
   `Tools/AutonomousAssetFactory/Stratocraft_Asset_Manifest.json` toplamda sadece **11 varlık** tanımlıyor:
   - Canlılar: `1 / 21` (`mob_ash_stalker_vat500.glb`)
   - Bosslar: `1 / 13` (`boss_phoenix_ember_avatar.glb`)
   - Atölye cihazları: `1 / 29`
   - Klan yapıları: `1 / 24`
   - Kuşatma araçları: `1` (mancınık)
   - Silahlar: `4` kılıç kabzası, Zırh: `1` set (`15` slot `.glb` dosyası diskte mevcut ama oyunda kuşanılmıyor).

---

## 3. Mevcut Sistemlerdeki Oynanış, Fizik, AI, Görsel ve Mantık Hataları

Aşağıdaki 10 başlık, hem PlayTest'te doğrudan gözlemlenen sorunları hem de kod taramasında tespit edilen gizli mantık hatalarını dosya ve satır numaralarıyla özetler.

### 3.1. Kılıç Fiziği, Hedef Etrafından Dolaşma ve Savurma Anomalileri
*Dosya: `Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs`*

*(Not: Ayrı bir `DirectionalMelee240HzSystem.cs` dosyası yoktur; tüm yakın dövüş fiziği `Stage1DirectionalCombatBridge.cs` içindedir.)*

1. **Savurma Ortasında Yörünge / Daire Çizme (Orbiting) Kök Nedenleri:**
   - **Canlı Savurma Sırasında Hedef Kayması (`satır 2099–2125`):** `BallisticSlashPhase.ActiveFullArcSwing` aşamasındayken en ufak fare hareketi (`liveMouseDelta.sqrMagnitude > 0.04f`) sürekli olarak `_slashDirUnit` vektörünü (`satır 2102–2103`), `_slashEndAnglesRad` bitiş açısını (`satır 2104`) ve `perpArc` yay bombe vektörünü (`satır 2119`) yeniden hesaplıyor. Oyuncu vururken fareyi kıpırdattığında kılıç yay ucu hedefin etrafından dolanan bir döngüye giriyor.
   - **Kamera Çekme Geri Besleme Döngüsü (`satır 2135–2145`):** Aktif savurma `cameraYawDeg` ve `cameraPitchDeg` açılarını zorla sürüklüyor (`24.5°–32°`). FPS `weaponPivot` doğrudan `playerCamera` uzayında dönüştürüldüğü için (`satır 2543–2548`) kameranın çekilmesi kılıcın dünya yayını tekrar besleyip savurmayı saptırıyor.
   - **Omuz/Dirsek Kısıtsız Kamera Uzayı Yörüngesi (`satır 2420–2454, 2543–2548`):** Kabza konumu (`armArcX = gx * 0.56f`, `armArcY = gy * 0.46f`) ve uç yönü omuz/kol kemik kısıtı olmadan serbestçe dönüyor.
2. **Fiziksel PD Ataleti Doğrudan `Lerp` ile Eziliyor (`satır 2127–2133`):**  
   4 alt adımda `StepSecondOrderRotationalInertia()` çalıştırıldıktan hemen sonra `satır 2133` `_currentBladeAnglesRad = Vector2.Lerp(_currentBladeAnglesRad, _targetWristAnglesRad, 0.65f)` diyerek dönel atalet çözümünü eziyor; bu da kılıcın yapay şekilde sıçramasına (snap) yol açıyor.
3. **Gerçek 240 Hz Değil (`satır 2127–2132, 2202–2207`):**  
   Sabit 240 Hz akümülatör yerine değişken kare `dt` değerini 4'e bölüyor (`effectiveDt * 0.25f`). Sonuç olarak 30 FPS'te 120 Hz, 120 FPS'te 480 Hz çalışıyor.
4. **Gizli Hata — Saplama (Thrust) Kare-Başı Testere Açığı (`satır 1764–1774, 1870` + `Stage1CreatureSimulationBridge.cs:1904`):**  
   Sol+Sağ tık basılı tutulunca `_isThrustMode` açılıyor ve **her karede** `CurrentSwingId++` artırılıyor (`satır 1773`), `IsActiveAttackSwing` sürekli `true` kalıyor (`satır 1870`). Canlı hasar tekilleştirmesi `LastHitSwingId == CurrentSwingId` baktığı için saplama saniyede 60+ kez hasar veriyor ve geri çekilmiyor.
5. **Gizli Hata — `dt` ile Ölçeklenmeyen Kenar Yuvarlanması (`satır 2274–2278`):**  
   Savurma ortasında ters hareket yapıldığında `_edgeRollRad += 1.25f` kare süresine (`dt`) çarpılmadan ekleniyor; yüksek FPS'te kılıç kontrolsüzce kendi ekseninde dönüp "Flat-Blade Slap" moduna düşüyor.

---

### 3.2. Silah Envanteri (`[1]` Aynı Slotta Döngü) ve `[G]` Dövüş Stili Parşömen Eksikliği
*Dosyalar: `Assets/_Stratocraft/UI/Stage1VisualHotbarAndInventoryUi.cs`, `Assets/_Stratocraft/Inventory/Stage1HolsterAndBackpackController.cs`, `Assets/_Stratocraft/Character/Stage1DirectionalCombatBridge.cs`, `Assets/_Stratocraft/Core/Stage1InteractivePlayController.cs`*

1. **Neden `[1]` Tuşuna Basınca Aynı Slotta Kılıç Türü Değişiyor ve Yaratıcı Modda Ayrı Kılıçlar Alınamıyor?**
   - `InventoryItemKind` (`Stage1VisualHotbarAndInventoryUi.cs:16–19, 85`) içinde yalnızca tek bir jenerik `WeaponSword = 1` eşyası var (`SubId` sadece `0: Demir/Çelik T2` ve `1: İlkel Çakmaktaşı Bıçağı T1` ayırıyor).
   - `[Tab]` Yaratıcı Mod (`CreativeTools`, `satır 4309–4315`) sadece `(WeaponSword, 1)` ve `(WeaponSword, 0)` sunuyor; **Uzun Kılıç (Longsword), Katana, Büyük Kılıç (Greatsword) ve Meç (Rapier)** ayrı envanter eşyası olarak mevcut değil.
   - Seçili kılıç slotuna tekrar basıldığında (`Stage1HolsterAndBackpackController.cs:2195–2204`) kılıcı kınına sokmak yerine `combatBridge.CycleWeaponArchetype(swordPivot)` çağrılıyor (`F8` de aynısını yapıyor: `Stage1InteractivePlayController.cs:3213–3231`). Silah tipi eşyaya değil, savaş köprüsündeki global bir enum'a (`ActiveWeaponArchetype`, `satır 475–499`) bağlı.
2. **Neden `[G]` ile Tüm Dövüş Stilleri Baştan Açık ve Parşömenle Öğrenme Yok?**
   - `Stage1DirectionalCombatBridge.cs:178–179` içinde `AllDojoStancesUnlocked = true` ve `StanceMastery01 = 0.65f` sabit kodlanmış; üstelik bu iki özellik kod tabanında **hiçbir yerde okunmuyor**.
   - `Stage1InteractivePlayController.cs:3200–3207` -> `CycleMartialStance()` (`satır 436–454`) hiçbir kilit/öğrenme kontrolü yapmadan 4 stili doğrudan döndürüyor.
   - `InventoryItemKind` içinde dövüş stili parşömeni (scroll) eşyası, kullanım/okuma mekaniği, öğrenilen stil kayıt listesi veya ustalık (Mastery) ilerlemesi **hiç yok**.

---

### 3.3. Canlılar, Yapay Zekâ ve Aşırı Hızlı / Anlaşılmaz Saldırı Ritmi
*Dosyalar: `Assets/_Stratocraft/CreaturesAI/Stage1CreatureSimulationBridge.cs`, `Assets/_Stratocraft/CreaturesAI/Creature21BehaviorSystem.cs`, `Assets/_Stratocraft/Character/CharacterVitalsComponent.cs`*

1. **Neden Canlı Saldırıları Aşırı Hızlı, Anlaşılmaz ve Kaçınılamaz?**
   - **Kurma / Uyarı (Windup / Telegraph) Durumu Yok (`Stage1CreatureSimulationBridge.cs:1569–1632`):** `CreatureAiState` içinde saldırı hazırlık/savurma öncesi telgraf aşaması yok. `MeleeAttack` içinde `a.AttackCooldown <= 0f` olduğu anda (`satır 1599`) yön kontrolü, kılıç/pençe çarpışma kutusu (hitbox) veya kaçınma penceresi olmadan **anında hitscan hasar** uygulanıyor.
   - **Kısa Bekleme Süresi (`satır 1602`):** Saldırı aralığı yüksek agresiflikte `~0.96 sn`'ye kadar düşüyor.
   - **Gizli Hata — Saldırı Animasyonu ve Atılma Hasardan SONRA ve TERS Yönde Oynuyor (`satır 1578–1582, 1635, 2785–2786`):** Hasar uygulandıktan *sonra* `AttackCooldown` ~`1.30f`'ye kurulduğu için `isAttacking = a.AttackCooldown > 0.65f` ve `attackSwing01 = (a.AttackCooldown - 0.65f) / 0.65f` hasar zaten yendikten sonra `1.0 -> 0.0` yönünde **geriye doğru** oynuyor; canlı atılması (lunge) da hasar sonrasına kalıyor.
2. **Gizli Hata — Canlılar Tek Vuruşta 2x (Çift) Hasar Düşüyor:**  
   `CharacterVitalsComponent.TakeDamageRegional()` (`CharacterVitalsComponent.cs:148–153`) `CurrentHp` değerinden hasarı zaten düşüyor; hemen ardından `Stage1CreatureSimulationBridge.cs:1625–1626` aynı `actualDmg` değerini `playerVitals.CurrentHp`'den **ikinci kez** çıkarıyor.
3. **21 Canlı ve 13 Boss'un Gerçek Durumu:**
   - **13 Boss:** Yalnızca **1/13** (Anka Kuşu / `IsPhoenixBoss`, `satır 205, 232`) mevcut ve o da Storm Wyvern (`archId = 16`) şablonunu ödünç alıyor. Kalan **12 boss** C# kodunda veri taslağı olarak bile yok.
   - **21 Canlı:** `Creature21BehaviorSystem.cs:116–169` içinde 21 veri kaydı var ama:
     - Görsel olarak tüm 21 canlı + 5 mağara varyantı yalnızca **3 jenerik kutu şablonunu** (Uçan, İki Ayaklı, Dört Ayaklı — Örümcek ve Akrep `#9/#15` bile 4 bacaklı kutu!) paylaşıyor (`Stage1CreatureSimulationBridge.cs:2589–2700`).
     - Özel yetenek bayrakları (`WallClimbable`, `CapTrenchBallistic`, `CapStealthPhasing`, `CapBlinkTeleport`, `CapHarvestHands`, `CapSiegeAndShipCrew`, `Creature21BehaviorSystem.cs:19–28`) sadece veri etiketi; PlayMode'da **hiçbirinin davranışı çalışmıyor**.

---

### 3.4. Oyuncu ve Canlı 3B Modelleri, Eklem/Rig Kusurları ve Animasyon Kalitesi
*Dosyalar: `Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs`, `Assets/_Stratocraft/CreaturesAI/Stage1CreatureSimulationBridge.cs`, `Assets/_Stratocraft/Editor/StratocraftStage1SceneBuilder.cs`*

1. **Blender `.glb` Modelleri Neden PlayMode'da Kutu Gibi Görünüyor?**
   - **Oyuncu (`Stage1ArticulatedRigAnimator.cs:244–248, 429–527`):** `armor_set_iron_vanguard.glb` kemiksiz (unskinned) statik mesh olduğu için PlayMode başında `staticStatueRenderer.enabled = false` (`satır 431`) ile **kapatılıyor** ve yerine `EmitArchitecturalShape(shapeId: 7/6)` ile oluşturulan katı kutu parçaları (`satır 459–526`) takılıyor. Diskteki 15 adet `armor_slot_*.glb` dosyası karaktere hiç giydirilmiyor.
   - **Canlılar ve Boss (`Stage1CreatureSimulationBridge.cs:2594–2597`):** Kök `.glb` `MeshRenderer` kapatılıp (`staticRootMr.enabled = false`) `Articulated_*` kutu çocukları üretiliyor. Kopan uzuv `.glb` dosyaları da kullanılmıyor; `AttachGoreCapAndThrowLimb` (`satır 2426–2443`) sıradan bir voksel kutusu fırlatıyor.
   - **Kılıçlar (`Stage1DirectionalCombatBridge.cs:494–520`):** `weapon_hilt_ironwood_longsword.glb` mesh'i çalışma zamanında prosedürel `AddBox` mesh'i ile eziliyor.
2. **Eklem, IK ve Animasyon Eksikleri (`Stage1ArticulatedRigAnimator.cs`):**
   - Gerçek `SkinnedMeshRenderer`, `Animator` veya `AnimationClip` yok; ayrı katı kutular döndükçe eklem yerlerinde boşluklar ve kırılmalar oluşuyor (`satır 564–590`).
   - **Kol IK Yok (`satır 782–850`):** Silahlar `_rightWristPivot` kemiğine bağlı değil; TPS kolları yalnızca kaba Euler tahmini yapıyor (`satır 790–794`). Bu yüzden kılıç elden kopuk havada duruyor ve çift elli kılıçlarda sol el kabzayı hiç kavramıyor.
   - Kopan sol kol (`satır 828–830`) ön kolu gizlemek yerine tüm kolu `(0.45, 0.32, 0.45)` oranında küçültüyor.

---

### 3.5. Açlık, Susuzluk, Hastalıklar, 15 Parça Zırh ve Ölümsüzlük Kilidi
*Dosyalar: `Assets/_Stratocraft/Character/Stage1PlayerMovementBridge.cs`, `Assets/_Stratocraft/Core/Stage1InteractivePlayController.cs`, `Assets/_Stratocraft/Character/ThermalAndBiologySystem.cs`, `Assets/_Stratocraft/Character/CharacterVitalsComponent.cs`*

1. **Açlık ve Susuzluk Her Karede 3 Farklı Yerden Zorla Sıfırlanıyor (Kilitli):**
   - `Stage1PlayerMovementBridge.cs:1046–1047`: `_vitals.HungerKcal = 2000f; _vitals.ThirstHydration = 100f;`
   - `Stage1InteractivePlayController.cs:2522–2523`: `_movementBridge.Vitals.HungerKcal = 2000f; _movementBridge.Vitals.ThirstHydration = 100f;`
   - `Stage1InteractivePlayController.cs:3758–3759`: `_movementBridge.Vitals.HungerKcal = 2000f; _movementBridge.Vitals.ThirstHydration = 100f;`
   - *Çelişki:* `ThermalAndBiologySystem.cs:378–397` içinde açlık/susuzluk azalması, açlık hasarı ve tokken can yenilenmesi **zaten kodlanmış**; ancak yukarıdaki 3 sabit atama sistemi tamamen felç ediyor ve üstüne her karede bedava can yenilenmesi (`2000 >= 1600 && 100 >= 80`) tetikliyor.
2. **Gizli Hata — Oyuncu Ölümsüzlük Kilidi (`HP >= 20` ve `BleedOutCrawl` İptali):**
   - `Stage1InteractivePlayController.cs:3772`: `CurrentHp = Mathf.Max(20.0f, CurrentHp)` her karede canı 20'ye geri çekiyor.
   - `Stage1PlayerMovementBridge.cs:1045, 1048–1051`: `CurrentHp >= 1.0f` kilidi koyuyor ve `CharacterStance.BleedOutCrawl` durumunu anında `Standing`'e çeviriyor. Böylece 30 saniyelik kanama/ölüm sistemi (`ThermalAndBiologySystem.cs:424–426`) ulaşılamaz kalıyor.
3. **Hastalıklar ve 15 Parça Zırh Eksikleri:**
   - `FoodPoisoning` ve `SporeInfection` (`CharacterVitalsComponent.cs:13–14`) yalnızca enum adı; kodda sıfır referans var.
   - Zırh sistemi 15 parça / 3 katman (İç-Orta-Dış) değil, yalnızca **4 tek-katmanlı slot** (Miğfer, Göğüslük, Kolluk, Dizlik; eldiven/el slotu bile yok — `Stage1VisualHotbarAndInventoryUi.cs:220, 811–816`, `CharacterVitalsComponent.cs:50–53`).

---

### 3.6. Mimari Şekillerde Az Çeşitlilik, İçi Görünen Rampa/Kemer ve Etiket Kayması
*Dosyalar: `Assets/_Stratocraft/Voxel/HybridSurfaceNetsAndShapeMesher.cs`, `Assets/_Stratocraft/Voxel/ArchitecturalShapeCatalog.cs`, `Assets/_Stratocraft/Core/Stage1MatterCrystalAndMiningController.cs`*

1. **62 Şeklin Yalnızca 9'u Erişilebilir ve Çoğu Aynı Kutu:**
   - `Stage1MatterCrystalAndMiningController.cs:36` yalnızca 9 şekil (`0..7, 63`) açıyor; `8..62` arası erişilemez.
   - `HybridSurfaceNetsAndShapeMesher.cs:1057–1075` 62 şekil ID'sini sadece 14 switch koluna indirgiyor (ör. mazgal, payanda, siperlik hepsi aynı konik kutu).
2. **Gizli Hata — Arayüz Etiketi ile Gerçek `ShapeId` Uyuşmuyor:**  
   `GetShapeDisplayName` (`Stage1MatterCrystalAndMiningController.cs:3232`) ile `ArchitecturalShapeCatalog.cs:25–30` tamamen kayık:
   - `ShapeId 2`: UI *"45° Rampa"* diyor -> Gerçekte `OctagonalColumn` (Sekizgen Sütun)
   - `ShapeId 3`: UI *"İç Köşe"* diyor -> Gerçekte `Ramp45` (45° Rampa)
   - `ShapeId 4`: UI *"Dış Köşe"* diyor -> Gerçekte `ArchVoussoir` (Kemer Taşı)
   - `ShapeId 5`: UI *"Kemer"* diyor -> Gerçekte `TrapezoidalKeystone` (Kilit Taşı)
   - `ShapeId 6`: UI *"Kilit Taşı"* diyor -> Gerçekte `BattlementMerlon` (Siperlik)
   - `ShapeId 7`: UI *"Sekizgen Sütun"* diyor -> Gerçekte `FacetedCrystalSpire` (Kristal Kule)
3. **Neden Rampa, Kemer ve Çatı Bloklarının İçi Görünüyor?**
   - **Rampa (`EmitSlopedRamp`, `HybridSurfaceNetsAndShapeMesher.cs:1222–1247`, `ShapeId 3,10,11`):** Köşe koordinatları `±0.49f` (her kenarda 2 cm boşluk) üretirken `ArchitecturalShapeCatalog.cs:210` alt (`FaceNegY`) ve arka (`FacePosZ`) yüzleri %100 opak işaretliyor. `ComputeArchitecturalNeighborMask` bitişik küpün yüzünü silince, 2 cm'lik yarıktan komşu bloğun ve rampanın içi görünüyor.
   - **Kemer Taşı (`EmitArchVoussoir`, `satır 1285–1306`, `ShapeId 4` — kemerlerde ve tüm ağaç dal çıkıntılarında kullanılıyor!):** 5 yüzeyde (`satır 1297–1301, 1304`) **üçgen sarım yönü ters (inverted winding)**; blok tamamen **içi dışına dönük** çiziliyor.
   - **Çatı Köşesi (`EmitRoofCorner`, `satır 1262`, `ShapeId 13`):** `+Z` yüzeyi ters sarımlı (back-face culled).
   - **Kristal (`EmitFacetedCrystalCluster`, `satır 1314–1335`, `ShapeId 7`):** Alt taban kapağı (bottom cap) yok ve `rotationState` yok sayılıyor.

---

### 3.7. Ağaçlar (Küp Yapraklar, Tek Tip Düz Direk ve Ölü Çam Kodu)
*Dosya: `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs` (`satır 3991–4082`)*

1. **Tek Tip Düz Direk ve Küp Yapraklar:**  
   Tüm ağaçlar `5..10 m` yüksekliğinde tamamen düz `1×1` dikey `OctagonalColumn` gövde, 4 adet ters-yüzlü (`shapeId: 4`) güdük dal çıkıntısı ve aynı `5×5` Manhattan-elmas deseninde `shapeId: 1` (küp) + `shapeId: 6` (kale siperliği) bloklardan oluşan yaprak tacı kullanıyor.
2. **Ayrı Yaprak Materyali Yok:**  
   Yapraklar için ayrı bir `MaterialId` tanımlanmamış; zeminle aynı `MaterialIdCatalog.Grass` kullanılıyor.
3. **Gizli Hata — Çam Ağacı Kodu Ölü (Hiç Doğmuyor):**  
   `satır 4021` biyom `2, 4, 5, 6, 7, 8, 9, 11` için erken `return` yapıyor; oysa `satır 4051`'deki `isPine = biomeId == 4 || biomeId == 5` kontrolü bundan **sonra** geliyor. Yani çam ağaçları hiçbir zaman oluşmuyor; ağaç yalnızca biyom `0, 1, 3, 10` içinde tek tip üretiliyor.
4. **Tür ve Dallanma Eksikliği:**  
   Meşe, çam, huş, söğüt, akasya, palmiye, kristal/demir ağacı gibi gövde eğriliği, kök çıkıntısı, organik dallanma ve yaprak yoğunluğu çeşitliliği yok.

---

### 3.8. Biyom Çeşitliliği ve Merkezden Uzaklaştıkça Zorlaşan Halka Sistemi
*Dosyalar: `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs`, `Assets/_Stratocraft/WorldGen/RingDifficultyCalculator.cs`, `Assets/_Stratocraft/CreaturesAI/Stage1CreatureSimulationBridge.cs`*

1. **Periyodik Dalga ve Sabit Test Yamaları:**  
   12 biyom ID'si periyodik `Sin * Cos` girişim dalgalarıyla (`~95–175 m` ölçekte) dağılıyor; ayrıca `ActiveWorldSeed == 1453` iken (`satır 193–203`) sabit dikdörtgen test yamaları haritayı eziyor.
2. **Merkezden Uzaklaştıkça Zorluk (Ring 0..7) Kopukluğu:**  
   `RingDifficultyCalculator` (8 eş merkezli halka) ne dünya üretimine ne de maden dağılımına (`OreDistributionTable`) bağlı. `Stage1CreatureSimulationBridge` kendi içinde rastgele `<120 / <1800 / <9000` kovaları kullanıyor. Sonuç olarak buzul, asit bataklığı ve volkanik/kül biyomları başlangıç noktasının hemen dibinde (`r = 92 m+`) çıkabiliyor; merkezden dışa doğru katmanlı keşif ve zorluk hissi yok.

---

### 3.9. Dünya Yapıları (5×5 Minecraft Küpleri; Harabe, Köy, Kasaba, Şehir ve Zindan Eksikliği)
*Dosyalar: `Assets/_Stratocraft/WorldGen/StratocraftFloatingIslandWorldGen.cs` (`satır 3320–3476, 4100`), `Assets/_Stratocraft/WorldGen/DungeonGeneratorSystem.cs`*

1. **Sadece 3 Adet `5×5×4` Tek-Chunk Küp Kutusu:**  
   Dünya üretiminde yalnızca 3 minik yapı şablonu (`Köy Evi`, `Mermer Tapınak`, `Antik Harabe`) var; bunlar düz `WoodPlank`/`StoneBrick` küplerinden oluşuyor ve ganimet sandığı niyetine içeriye ham `GoldOre`/`MithrilOre` küpü (`satır 3418, 3450, 3474`) bırakılıyor.
2. **Gerçekçi Taş/Duvar İşçiliği Yok:**  
   Stratocraft'ın küp-küp olmayan, kavisli/pahlı/gerçekçi duvar ve mimari hedefinin aksine yapılar saf Minecraft blok küpleriyle örülüyor.
3. **Çöl/Buzulda Çimen Kaide Hatası (`satır 4100`):**  
   `EnsureSolidFoundationUnder` tüm yapıların altına biyomdan bağımsız olarak parlak yeşil `Grass` dolgu kaidesi koyuyor.
4. **Çok-Chunk Yerleşim ve Zindan Eksikliği:**  
   Gerçek harabe kompleksleri, yollarla bağlı köyler, kasabalar, şehirler, kaleler, köprüler ve yeraltı zindan girişleri yok (`DungeonGeneratorSystem` hiç çağrılmıyor).

---

### 3.10. Malzemelerle Yapı Bloğu Üretilememesi ve Kütüklerle İnşa Eksikliği
*Dosyalar: `Assets/_Stratocraft/Voxel/MaterialIdCatalog.cs`, `Assets/_Stratocraft/Physics/Stage1StructuralCollapseBridge.cs`, `Assets/_Stratocraft/Core/Stage1WorldLooseBuildController.cs`, `Assets/_Stratocraft/Core/Stage1MindBuildingController.cs`*

1. **Tanımlı Ama Kullanılamayan Yapı Malzemeleri:**  
   `MaterialIdCatalog` içinde `ReinforcedSteelBlock (122)`, `ObsidianBrick (123)`, `ThatchTile (124)`, `IronGrating (125)`, `SpiritWood (72)`, `YggdrasilBranch (73)`, `Bronze (50)`, `Steel (51)`, `DamascusSteel (60)`, `Floatstone (18)`, `Stalagnate (17)`, `BoneIvory (76)` tanımlı olmasına rağmen **hiçbirinin yapı bloğu tarifi veya yerleştirme akışı yok**. Ayrıca kumtaşı, kireçtaşı, pişmiş tuğla/kiremit, ahşap zona/çatı, cam ve yaprak gibi temel yapı materyalleri de eksik.
2. **Kesilen Ağaç Kütükleriyle İnşa Yapılamıyor:**  
   Devrilen kütükler (`Stage1StructuralCollapseBridge.cs:1172–1450`) yalnızca `2 m` parçalara bölünebiliyor, omuzda taşınabiliyor (`32 kg`), yere bırakılabiliyor veya yakacak oduna yarılabiliyor.
   - `Stage1WorldLooseBuildController.cs:128` omuzda kütük varken yerleştirmeyi açıkça **kapatıyor**.
   - `Stage1MindBuildingController` yalnızca 5 tezgâh/istasyon şablonuna sahip; **kütük duvar, çit/palisad, kiriş, çatı iskeleti veya kütük kulübe** inşa etme sistemi hiç yok.

---

## 4. Sıradaki Büyük Anayasa Sistemleri (Faz 3 – Faz 9 Kalanlar)

Mevcut eksikler ve hatalar kapatıldıktan sonra Anayasa'ya göre sıfırdan inşa edilmesi gereken ana fazlar şunlardır:

1. **Anayasa Faz 3 — Klanlar, Bölge ve Diplomasi:**  
   3B Çit BFS kapalı alan algılama, Klan Gelişim Puanı (KGP), Klan Kristali HP, 3 yönlü `IsSiegeable` kuşatma kuralı, 6 Klan Ritüeli, hafif SQLite WAL kalıcı dünya kaydı, Hayalet Hologram önizleme.
2. **Anayasa Faz 4 — Tam Savaş, Zırh ve Ölüm Hukuku:**  
   `Master Strike` (`PredictiveBlockAssistJob`), 15 parça 3 katmanlı zırh fiziği (`O(1)` kaplama oranı + `HasStopRib`), Menzilli Balistik (Yay, Arbalet, Pilum, rüzgâr/yerçekimi sapması), 3 Ölüm Ganimet Modu, `Offline Sleeper` (çevrimdışı uyuyan beden).
3. **Anayasa Faz 5 — Zindanlar ve Mühendislik Savunması:**  
   5 Antik Zindan (dünya üretimine bağlı), 75 Fiziksel Batarya (Dikey Direk Kilidi), 25 Mayın ve 6 Yakıtlı Tuzak Çekirdeği.
4. **Anayasa Faz 6 — Oyun Sonu Bossları ve Küresel Olaylar:**  
   Kalan 12 Ritüel Boss, SGP ilerlemesi, 12 Küresel Felaket, 500 Canlı VAT/Instanced Gece Dalgaları, İkmal Paketi (Supply Drop).
5. **Anayasa Faz 7 — İleri Zanaat ve Atölyeler:**  
   `16×32×4` voksel Örs dövme (mevcut `HybridSurfaceNetsAndShapeMesher` ile), Harmonizer + 4 Katalizör, 6 Atölye / 29 Cihaz, Zanaat Ustalık sistemi, 24 Klan Yapısı.
6. **Anayasa Faz 8 — Tarım, Genetik, Simya, Evcilleştirme ve Otomasyon:**  
   Liebig NPK toprak kimyası, gıda bozulması, `1★–5★` canlı/bitki genetiği, ayrık Maillard yemek pişirme, 2B (2 Boyutlu) faz uzayı simya, 13 kafesli boss evcilleştirme, `R` ıslık komutları, GOAP işçi NPC'ler, hidrolik piston çiftlikleri.
7. **Anayasa Faz 9 — Kuşatma, Ekonomi, Modlama ve Co-op:**  
   5 aşamalı DFA kuşatma makinesi mekaniği, kervanlar, Ed25519 Kan Sözleşmeleri, takas pazarı, görev loncası, kanca/planör, IL2CPP uyumlu **`JSON/TOML + Sandbox MoonSharp Lua` Mod SDK** ve tek transportlu **Listen-Server Co-op (2–16 oyuncu)**.

---

## 5. Önceliklendirilmiş Çözüm ve Yol Haritası Önerisi

Oyunu bir sonraki PlayTest'te bambaşka bir kalite seviyesine çıkarmak için sorunları 3 dalgada çözmek en sağlıklı sıradır:

### Dalga A — P0 Kritik Mantık ve Geometri Hataları (Hemen Düzeltilmeli)
1. **Savaş Hataları:** Savurma ortası hedef kaymasını/kamera geri beslemesini sabitle, `Lerp` ezmesini kaldırıp gerçek 240 Hz akümülatöre geç, saplama (`Thrust`) kare-başı `CurrentSwingId++` testere açığını ve `dt`'siz `edgeRoll` dönüşünü kapat.
2. **Canlı Saldırı & Hasar Hataları:** Canlılara gerçek `Windup -> ActiveHitbox -> Recovery` (telgraf + kaçınma penceresi) ekle, animasyonun hasardan sonra ters oynamasını düzelt, 2x çift hasar düşümünü ve oyuncu `HP >= 20` ölümsüzlük kilidini kaldır (ölümsüzlük sadece `[F4]` Yaratıcı Modda olsun).
3. **Mimari Şekil & Etiket Hataları:** `EmitArchVoussoir` (`ShapeId 4`) ve `EmitRoofCorner` (`ShapeId 13`) ters sarım (inverted winding) hatalarını düzelt, `EmitSlopedRamp` `±0.49f` yarığını `±0.50f` (veya doğru maskeleme) ile kapat, `GetShapeDisplayName` etiket kaymasını `ArchitecturalShapeCatalog` ile eşitle.
4. **Açlık & Susuzluk:** 3 yerdeki `HungerKcal = 2000f / ThirstHydration = 100f` zorunlu sıfırlamalarını kaldır (yalnızca `[F4]` Yaratıcı Modda sabit tut) ve `ThermalAndBiologySystem` biyolojisini aktif et.

### Dalga B — P1 Çekirdek Oynanış, Görsel Kalite ve Dünya Zenginliği
1. **Silah & Dövüş Stili Ayrımı:** Uzun Kılıç, Katana, Büyük Kılıç ve Meç'i ayrı envanter eşyalarına böl (`[1]` döngüsünü kaldır, `[Tab]` Yaratıcı Moda ekle); `[G]` stilleri için parşömen eşyası + öğrenme/kilit açma sistemi kur.
2. **İnşa, Malzeme ve Kütük Yapıları:** Kesilen kütüklerle doğrudan yerleştirilebilen kütük duvar, çit/palisad, kiriş ve kulübe şablonları ekle; `MaterialIdCatalog` içindeki atıl materyallere yapı bloğu tarifleri ve şekil çeşitliliği aç.
3. **Ağaçlar, Biyomlar ve Halkalar:** Ölü çam ağacı hatasını düzelt, ayrı yaprak materyali ve çoklu tür/dallanma (meşe, çam, huş, palmiye, kristal ağacı) ekle; `RingDifficultyCalculator` (Ring 0..7) sistemini biyom, maden ve canlı zorluğuna bağla; Seed 1453 test yamalarını kaldır.
4. **Gerçekçi Yapılar ve Yerleşimler:** 5×5 küp kutuları yerine pahlı/kemerli gerçekçi taş işçiliği kullanan çok-chunk harabeler, köyler, kasabalar ve aktif `DungeonGeneratorSystem` girişleri ekle; çimen kaide hatasını düzelt.
5. **Karakter & Canlı 3B Modelleri / Rig / Animasyon:** `Tools/AutonomousAssetFactory` üzerinden skinned (kemikli) karakter + 15 parça zırh + 21 canlı / 13 boss `.glb` üretimini genişlet; `Stage1ArticulatedRigAnimator` ve `Stage1CreatureSimulationBridge` içinde `.glb` kapatma geçici çözümünü kaldırıp gerçek eklem/IK ve el-kabza bağlantısı kur.

### Dalga C — P2 Büyük Anayasa Fazları (Faz 3 -> Faz 9)
- Dalga A ve B ile çekirdek dünya, savaş, hayatta kalma ve görsel kalite sağlamlaştıktan sonra sırasıyla **Anayasa Faz 3 (Klanlar/3B Çit BFS)** -> **Faz 4 (15 Parça Zırh + Balistik + Ölüm Modları)** -> **Faz 5 (Zindanlar + 75 Batarya + Tuzaklar)** -> **Faz 6–9** fazlarına geçilir.
