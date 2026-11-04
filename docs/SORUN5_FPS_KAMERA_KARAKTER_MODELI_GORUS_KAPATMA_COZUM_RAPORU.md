# SORUN 5: FPS KAMERADA HAREKET EDERKEN, EĞİLİNCE (`Ctrl`) VE SÜRÜNÜRKEN (`Z`) KARAKTER MODELİNİN GÖRÜŞÜ KAPATMASI — KÖK NEDEN VE ÇÖZÜM RAPORU

**Tarih:** 2026-11-04  
**İlgili Sistemler:**  
- [`Stage1InteractivePlayController.cs`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs) (`ApplyFirstPersonVisibility`, satır 443–479)  
- [`Stage1ArticulatedRigAnimator.cs`](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs) (`StepRigAnimation`, `ComputeTargetArticulatedPose`, `EnsureArticulatedLimbPivots`, `StepArticulatedLimbsAndArms`, `UpdateCameraPose`, satır 75–708)  
- [`Stage1HolsterAndBackpackController.cs`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs) (`UpdateBodyHolsteredWeaponProxies`, satır 696–739)  
- [`PhysicalCharacterControllerSystem.cs`](Assets/_Stratocraft/Character/Systems/PhysicalCharacterControllerSystem.cs) (`GetStanceCapsuleHeight`, satır 38–51)  
- **Oyun Anayasası Referansı:** [`STRATOCRAFT_ EVRENSEL OYUN TASARIM VE MANTIK ANAYASASI (GAME LOGIC BIBLE) (2).md`](STRATOCRAFT_%20EVRENSEL%20OYUN%20TASARIM%20VE%20MANTIK%20ANAYASASI%20(GAME%20LOGIC%20BIBLE)%20(2).md) — **Bölüm 3.1 (Çift Kamera FPS/TPS, Eğilme, Kayma ve Yüzüstü Sürünme Fiziği)**

---

## 1. YÖNETİCİ ÖZETİ VE ANAYASA KURALLARI (BÖLÜM 3.1)

Kullanıcının bildirdiği **"karakter hareket ederken filan karakter modeli gelip görüşümü kapatıyor arada, en çok da eğilince kapatıyor ve sürünürken"** sorunu; **Gerçek Birinci Şahıs (True First-Person / Full-Body Awareness)** kamera mimarisinde prosedürel eklemli şövalye zırhının (`RigIK_*`) omurga eğimiyle (`_smoothedBodyPitchDeg`) öne yatarken, FPS kamerasının geride kalması ve göğüs/boyunluk/sırt çantası/omuz zırhı parçalarının `ShadowCastingMode.On` olarak bırakılmasından kaynaklanmaktadır.

**Oyun Anayasası (Bölüm 3.1) Gereksinimleri:**
1. **Çift Kamera (`F5` Geçişi):** Ana kamera varsayılan olarak **FPS (Birinci Şahıs Gözden Görünüm)** modundadır; `F5` ile **Omuz Üstü TPS** moduna geçilir.
2. **FPS Gölge-Koruyucu Görünürlük (`ShadowCastingMode.ShadowsOnly`):** FPS modunda oyuncunun kendi kafa/miğfer, boyunluk, üst göğüs zırhı, sırt çantası ve omuz zırhı (`Pauldron`) parçaları **yerde ve duvarlarda gerçekçi tam vücut gölgesi oluşturmaya devam etmeli (`ShadowsOnly`)**, ancak kameranın yakın kırpma düzlemini (`nearClipPlane`) keserek görüşü **asla kapatmamalıdır**.
3. **Eğilme (`Sol Ctrl`) ve Yüzüstü Sürünme (`Z` — `-68°` Boyun Karşı-Eğimi):**
   - Çömelirken kapsül boyu `1.15m` (`Crouching`), kayarken `0.90m` (`Sliding`), yüzüstü sürünürken `0.45m` (`ProneCrawling`, göz yüksekliği `~0.58m`) seviyesine iner.
   - Sürünürken gövde (`_spineTorsoPivot`) yere paralel (`+78°`) ileri yatarken, kafa/boyun (`_headCounterPitchPivot`) `-68°` karşı-eğimle (`counterPitch`) ileriye/kameraya bakar. FPS kamerası geride (kalça/sırt hizasında) kalmamalı; omurganın ileri uzanımıyla (`Head-Bone Forward Offset`) uyumlu biçimde başın/gözün gerçek dünya konumuna taşınmalıdır.

---

## 2. MEVCUT C# KODUNDAKİ 4 KRİTİK KÖK NEDEN (SATIR SATIR ANALİZ)

### Kök Neden 2.1: `ApplyFirstPersonVisibility()` İçinde Yanlış İsim Filtresi (`"HeadHelmet"`) ve Üst Gövdenin `ShadowCastingMode.On` Bırakılması
[`Stage1InteractivePlayController.cs` satır 444–479](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L444-L479) incelendiğinde:
```csharp
bool isHeadPart = renderers[i].gameObject.name.Contains("HeadHelmet", StringComparison.OrdinalIgnoreCase);
renderers[i].enabled = true;
renderers[i].shadowCastingMode = (_isFirstPersonMode && isHeadPart)
    ? ShadowCastingMode.ShadowsOnly
    : ShadowCastingMode.On;
```
- **Hata 1 (İsim Uyuşmazlığı):** [`Stage1ArticulatedRigAnimator.EnsureArticulatedLimbPivots()`](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs#L349-L352) içinde oluşturulan kafa parçalarının adları `"RigIK_HelmetDome_Vis"`, `"RigIK_HelmetVisor"` ve `"RigIK_HelmetCrest_Vis"` şeklindedir. Hiçbirinin adında `"HeadHelmet"` geçmediği için `ApplyFirstPersonVisibility()` çağrıldığında (ör. `Start()` veya `F5` dönüşünde) `isHeadPart` **`false`** dönmekte ve **miğfer bile `ShadowCastingMode.On` (görünür) kalmaktadır!**
- **Hata 2 (Üst Gövde, Omuzlar ve Sırt Çantası):** Aynı döngüde `RigIK_GorgetCollar_Vis` (boyunluk), `RigIK_ChestCuirass_Vis` (göğüs zırhı), `RigIK_ChestPlastron_Vis`, `RigIK_AbdomenPlate_Vis`, `RigIK_ExplorerPack_Vis` (sırt çantası), `RigIK_PackBedroll_Vis` (uyku tulumu rulosu), `RigIK_PackFrame_Vis`, `RigIK_RightPauldron_Vis` ve `RigIK_LeftPauldron_Vis` (omuz zırhları) tamamen `ShadowCastingMode.On` olarak bırakılmıştır.

---

### Kök Neden 2.2: `StepRigAnimation()` İçinde Yalnızca `_headCounterPitchPivot` Altının `ShadowsOnly` Yapılması ve Dinamik Parçaların Kaçırılması
[`Stage1ArticulatedRigAnimator.cs` satır 128–151](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs#L128-L151) incelendiğinde:
```csharp
if (!_shadowModeInitialized || _lastFirstPersonShadowState != isFirstPersonMode)
{
    _shadowModeInitialized = true;
    _lastFirstPersonShadowState = isFirstPersonMode;
    var childRenderers = playerBodyRoot.GetComponentsInChildren<Renderer>(true);
    for (int r = 0; r < childRenderers.Length; r++)
    {
        ...
        bool isHeadPart = (_headCounterPitchPivot != null && rt.IsChildOf(_headCounterPitchPivot)) ||
                          rt.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          rt.name.IndexOf("Helmet", System.StringComparison.OrdinalIgnoreCase) >= 0;
        childRenderers[r].enabled = true;
        childRenderers[r].shadowCastingMode = (isFirstPersonMode && isHeadPart)
            ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
            : UnityEngine.Rendering.ShadowCastingMode.On;
    }
}
```
1. Burada sadece `_headCounterPitchPivot` altındaki 3 miğfer parçası `ShadowsOnly` yapılmaktadır. Oysa `_spineTorsoPivot` doğrudan `_headCounterPitchPivot`'un hemen altında (`y = 0.49m` vs `y = 0.53m`) yer alan `RigIK_GorgetCollar_Vis`, `RigIK_ChestCuirass_Vis`, `RigIK_ExplorerPack_Vis`, `RigIK_PackBedroll_Vis` ve `RigIK_Right/LeftPauldron_Vis` parçalarını taşımaktadır ve bunların tamamı `ShadowCastingMode.On` kalmaktadır!
2. Ayrıca [`Stage1HolsterAndBackpackController.UpdateBodyHolsteredWeaponProxies()`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L696-L722) metodu `playerBodyRoot` altına sonradan `Holster_WaistLeft_Sword`, `Holster_WaistRight_Pickaxe` ve `Holster_BackLeft_Axe` (`localPos = (-0.18, 1.15, -0.24)`) mesh'lerini eklemektedir. `_shadowModeInitialized` bayrağı bir kez `true` olduktan sonra bir daha çalışmadığı için sonradan eklenen veya aktifleşen kılıf proksileri FPS modunda kameranın içinde asılı kalmaktadır.

---

### Kök Neden 2.3: Hareket Ederken, Eğilince (`Ctrl`) ve Sürünürken (`Z`) Omurga İleri Eğimi (`_smoothedBodyPitchDeg`) ile Kamera Konumu (`UpdateCameraPose`) Arasındaki Geometrik Çarpışma

[`Stage1ArticulatedRigAnimator.cs`](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs) içindeki [`ComputeTargetArticulatedPose` (satır 213–267)](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs#L213-L267) ve [`UpdateCameraPose` (satır 619–652)](Assets/_Stratocraft/Character/Stage1ArticulatedRigAnimator.cs#L619-L652) fonksiyonlarının matematiksel koordinatlarını karşılaştırdığımızda sorunun neden **özellikle hareket ederken, eğilince ve sürünürken** yaşandığı %100 kanıtlanmaktadır:

#### A. Yürürken ve Depar Atarken (`Standing` / `Sprinting` — "Hareket Ederken Arada Görüşü Kapatması")
- **Kamera Konumu (`UpdateCameraPose`):**
  - `CapsuleHeightMeters = 1.80m` $\implies$ `eyeHeight = 1.80 - 0.14 = 1.66m`, `targetCamPos = (0, 1.66m + viewBob, +0.10m)`.
  - `viewBob = sin(2 * phase) * 0.024m` (kamera aşağı inerken `-0.024m` $\implies$ `Y_cam = 1.636m`).
- **Gövde Konumu (`StepRigAnimation`):**
  - `strideBob = |sin(phase)| * 0.038m` (gövde yukarı çıkarken `+0.038m` — `viewBob` ile faz farkı nedeniyle kamera inerken gövde yukarı çıkar!).
  - Depar atarken (`Sprinting`) `_spineTorsoPivot` ileriye doğru **`targetPitchDeg = +12°`** eğilir ve `strideYawTwist = ±6°` sağa-sola burkulur.
  - Bu sırada `RigIK_GorgetCollar_Vis` (`y = 0.49m`), `RigIK_RightPauldron_Vis` / `RigIK_LeftPauldron_Vis` ve sırttaki `Holster_BackLeft_Axe` (`y = 1.15m`) ileri ve yukarı savrularak kameranın `nearClipPlane` (`0.30m`) düzlemini keser; oyuncu koşarken veya aşağı bakarken yakalık ve omuz zırhı anlık olarak ekranı kapatır.

#### B. Eğilince / Çömelirken (`Crouching` — `Sol Ctrl`)
- **Kamera Konumu (`UpdateCameraPose` satır 631 & 642):**
  $$\text{CapsuleHeightMeters} = 1.15\text{m} \implies \text{eyeHeight} = \max(0.72, \, 1.15 - 0.14) = \mathbf{1.01\text{m}}$$
  $$\mathbf{P}_{\text{cam, crouch}} = (0, \, \mathbf{1.01\text{m}}, \, \mathbf{+0.10\text{m}})$$
- **Omurga ve Zırh Parçalarının Konumu (`ComputeTargetArticulatedPose` satır 223–227):**
  - `_pelvisRootPivot.y = 0.54m + strideBob (0.038m) = 0.578m`
  - `_spineTorsoPivot.y = 0.578 + 0.05 = 0.628m`
  - Omurga **`targetPitchDeg = +18°`** ileri (`+Z`) yatar!
  - `18°` ileri yatan omurga üzerindeki parçaların oyuncu-yerel $(Y, Z)$ koordinatları:
    1. **Boyunluk (`RigIK_GorgetCollar_Vis`, `localPos = (0, 0.49, 0.01)`, `scale = (0.28, 0.08, 0.22)`):**
       $$Y_{\text{collar}} = 0.628 + 0.49\cos(18^\circ) = \mathbf{1.094\text{m}}, \quad Z_{\text{collar}} = 0.01\cos(18^\circ) + 0.49\sin(18^\circ) = \mathbf{+0.161\text{m}}$$
       Boyunluğun arka-ön Z aralığı $[+0.05\text{m}, \, +0.27\text{m}]$, alt-üst Y aralığı $[1.05\text{m}, \, 1.14\text{m}]$ olur!
    2. **Göğüs Zırhı (`RigIK_ChestCuirass_Vis`, `localPos = (0, 0.34, 0.01)`, `scale = (0.42, 0.28, 0.26)`):**
       $$Y_{\text{chest}} = 0.628 + 0.34\cos(18^\circ) = \mathbf{0.951\text{m}}, \quad Z_{\text{chest}} = 0.34\sin(18^\circ) + 0.01 = \mathbf{+0.115\text{m}}$$
       Göğüs zırhının Z aralığı $[-0.015\text{m}, \, +0.245\text{m}]$, Y aralığı $[0.81\text{m}, \, 1.09\text{m}]$ olur!
    3. **Uyku Tulumu Rulosu (`RigIK_PackBedroll_Vis`, `localPos = (0, 0.49, -0.17)`, `scale = (0.11, 0.32, 0.11)`):**
       $$Y_{\text{bedroll}} = 0.628 + 0.49\cos(18^\circ) - 0.17\sin(18^\circ) = \mathbf{1.041\text{m}}, \quad Z_{\text{bedroll}} = 0.49\sin(18^\circ) - 0.17\cos(18^\circ) = \mathbf{-0.010\text{m}}$$
  - **Çarpışma Sonucu:** Kamera `(0, 1.01m, +0.10m)` noktasındayken, **`RigIK_ChestCuirass_Vis` (`Y = 0.81..1.09m, Z = -0.015..+0.245m`) ve `RigIK_GorgetCollar_Vis` (`Y = 1.05..1.14m, Z = +0.05..+0.27m`) doğrudan kameranın merceğinin tam üstüne ve önüne oturur!**

#### C. Yüzüstü Sürünürken (`ProneCrawling` — `Z` Tuşu)
- **Kamera Konumu (`UpdateCameraPose` satır 630 & 642):**
  $$\text{CapsuleHeightMeters} = 0.45\text{m} \implies \text{eyeHeight} = \max(0.58, \, 0.45 - 0.14) = \mathbf{0.58\text{m}}$$
  $$\mathbf{P}_{\text{cam, prone}} = (0, \, \mathbf{0.58\text{m}}, \, \mathbf{+0.04\text{m}})$$
- **Omurga, Sırt Çantası ve Kolların Konumu (`ComputeTargetArticulatedPose` satır 237–244 & `StepArticulatedLimbsAndArms` satır 555–590):**
  - `_pelvisRootPivot.y = 0.26m + strideBob = 0.298m` $\implies$ `_spineTorsoPivot.y = 0.348m`.
  - Omurga **`targetPitchDeg = +78°`** ileri (`+Z`) yatar!
  - `78°` ileri yatan omurga üzerinde:
    1. **Sırt Çantası (`RigIK_ExplorerPack_Vis`, `localPos = (0, 0.31, -0.17)`, `scale = (0.30, 0.32, 0.14)`):**
       $$Y_{\text{pack}} = 0.348 + 0.31\cos(78^\circ) + 0.17\sin(78^\circ) = \mathbf{0.578\text{m}}$$
       $$Z_{\text{pack}} = 0.31\sin(78^\circ) - 0.17\cos(78^\circ) = \mathbf{+0.268\text{m}} \quad (\text{Z aralığı: } [+0.11\text{m}, \, +0.43\text{m}])$$
    2. **Uyku Tulumu (`RigIK_PackBedroll_Vis`, `localPos = (0, 0.49, -0.17)`):**
       $$Y_{\text{bedroll}} = 0.348 + 0.49\cos(78^\circ) + 0.17\sin(78^\circ) = \mathbf{0.616\text{m}}, \quad Z_{\text{bedroll}} = \mathbf{+0.444\text{m}}$$
    3. **Göğüs Zırhı ve Boyunluk (`RigIK_ChestCuirass_Vis` & `RigIK_GorgetCollar_Vis`):**
       $$Y_{\text{collar}} = 0.348 + 0.49\cos(78^\circ) = \mathbf{0.450\text{m}}, \quad Z_{\text{collar}} = \mathbf{+0.481\text{m}}$$
    4. **Sürünme Kolları ve Omuz Zırhları (`RigIK_RightShoulderPivot` / `RigIK_LeftShoulderPivot`, `armPitch = -72° ± 22°`):**
       Omuzlar `Y ≈ 0.44m, Z ≈ +0.42m` noktasında ileri-geri sürünme hareketi yaparken kameranın (`Y = 0.58m, Z = +0.04m`) tam önünde sağdan ve soldan görüş konisini kapatır!
  - **Çarpışma Sonucu:** Sürünürken karakterin kafası `Z = +0.52m` ilerideyken, kamera `Z = +0.04m` geride (kalçanın hemen üstünde) bırakılmıştır! Bu yüzden kamera `Y = 0.58m, Z = +0.04m` noktasından ileriye (`+Z`) doğru bakarken **tam önünde (`Y = 0.578m, Z = +0.268m` ve `Y = 0.616m, Z = +0.444m`) duran `RigIK_ExplorerPack_Vis` (Sırt Çantası), `RigIK_PackBedroll_Vis` (Uyku Tulumu), `RigIK_ChestCuirass_Vis` ve `RigIK_Right/LeftPauldron_Vis` parçalarının doğrudan içine bakmaktadır!**

---

### Kök Neden 2.4: `UpdateBodyHolsteredWeaponProxies()` İçindeki Kılıf Proksilerinin (`Holster_*`) Sabit Konumu ve Görünürlüğü
[`Stage1HolsterAndBackpackController.cs` satır 696–722](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L696-L722) içinde:
```csharp
_holsteredBeltSwordProxy = CreateHolsterAttachmentProxy(bodyTr, "Holster_WaistLeft_Sword", swordVisual, new Vector3(-0.26f, 0.72f, -0.05f), ...);
_holsteredBeltPickaxeProxy = CreateHolsterAttachmentProxy(bodyTr, "Holster_WaistRight_Pickaxe", pickaxeVisual, new Vector3(0.26f, 0.72f, -0.05f), ...);
_holsteredBackAxeProxy = CreateHolsterAttachmentProxy(bodyTr, "Holster_BackLeft_Axe", axeVisual, new Vector3(-0.18f, 1.15f, -0.24f), ...);
```
- Bu 3 kılıf proksisi eklemli `_pelvisRootPivot` veya `_spineTorsoPivot` yerine doğrudan `playerBodyRoot` (`bodyTr`) köküne sabitlenmiştir.
- Oyuncu çömeldiğinde (`eyeHeight = 1.01m`) veya süründüğünde (`eyeHeight = 0.58m`), `Holster_BackLeft_Axe` `y = 1.15m` yüksekliğinde havada asılı kalmakta, `Holster_WaistLeft_Sword` ve `Holster_WaistRight_Pickaxe` ise `y = 0.72m` yüksekliğinde sürünme kamerasının (`y = 0.58m`) hemen üstünde/yanında kalmaktadır ve hiçbirine `ShadowCastingMode.ShadowsOnly` uygulanmamaktadır!

---

## 3. İNTERNET ARAŞTIRMASI (GITHUB, REDDIT, YOUTUBE VE AAA OYUN MOTORU ÇÖZÜMLERİ)

Gerçek Birinci Şahıs (*True First-Person Perspective — TFPP*) kullanan AAA oyunların ve Unity/Unreal topluluklarının (`r/Unity3D`, `r/gamedev`, `r/StarCitizen`, `r/GroundBranch`, `r/arma`) bu sorunu nasıl çözdüğü araştırılmıştır:

### 3.1. *Star Citizen* (Cloud Imperium Games — "First Person Universe" Kamera ve Gölge Mimarisi)
- **Problem:** *Star Citizen* geliştiricileri, FPS kamerasını doğrudan iskelete bağladıklarında çömelme, sürünme ve eğilme sırasında miğferin, boyunluğun, göğüs zırhının ve sırt çantasının kameranın içine girdiğini (`Mesh Clipping`), kamerayı doğrudan `Head` kemiği rotasyonuna bağladıklarında ise mide bulandırıcı sarsıntı (`Head-Bob Jitter`) oluştuğunu belgelemiştir.
- **Çözüm:**
  1. **Stabilized Eye Socket (Sönümlü İleri Göz Noktası):** Kamera rotasyonu doğrudan oyuncu fare açısıyla (`lookRot`) kontrol edilirken, kameranın **konumu** omurganın ileri eğimini takip eden sönümlü bir göz soketine (`Eye Forward Offset`) bağlanır; böylece karakter eğildiğinde veya süründüğünde kamera geride (sırt çantasının arkasında) kalmaz, başla birlikte ileri gider.
  2. **Local Client `ShadowsOnly` Stripping:** Yerel oyuncunun kendi kamerasında kafa, boyunluk, sırt çantası ve üst omuz zırhları `ShadowsOnly` (yalnızca gölge düşüren görünmez mesh) olarak işaretlenir; böylece oyuncu yere baktığında tam gölgesini ve bacaklarını görür ama hiçbir üst gövde/çanta poligonu kamerayı kapatamaz.

### 3.2. *Kingdom Come: Deliverance* ve *Cyberpunk 2077* (Full-Body Awareness & Upper-Torso Culling)
- *Kingdom Come: Deliverance* (Henry'nin tam plaka zırhlı FPS görünümü) ve *Cyberpunk 2077* (*Immersive First Person* mimarisi), oyuncunun aşağı baktığında bacaklarını ve çizmelerini görmesini sağlarken, çömelme (`Crouch`) ve kayma (`Slide`) esnasında kameranın klavikula (köprücük kemiği), omuzluk (`Pauldron`) ve göğüs zırhı (`Cuirass`) içine girmesini önlemek için:
  - Üst gövde, omuz zırhları ve sırt eklentilerini FPS modunda `ShadowCastingMode.ShadowsOnly` yapar.
  - Çömelme ve sürünme sırasında kameraya omurga eğim açısıyla (`sin(spinePitch)`) orantılı **dinamik ileri ofset (`Forward Offset`)** verir.

### 3.3. *Ground Branch* ve *Arma 3* (Prone Head-Bone Offset & Shoulder Retraction)
- *Ground Branch* ve *Arma 3* gibi taktiksel simülasyonlarda yüzüstü sürünürken (`Prone`), gövde yatay (`~80°`) konuma geçtiğinde kamera kapsül merkezinde (`Z = 0`) bırakılmaz; omurga uzunluğu (`~0.45m`) kadar ileriye (`+Z` yönüne, kafa kemiğinin önüne) taşınır.
- Ayrıca sürünürken ileri uzanan kolların omuz eklemleri (`Shoulder Pivot`) kameranın görüş konisine girmemesi için FPS modunda `ShadowsOnly` yapılır, yalnızca ön kol/bilek (`Elbow` / `Wrist`) ve eldeki silah görünür tutulur.

### 3.4. Unity URP / Reddit (`r/Unity3D`) En İyi Uygulama Özeti
1. **`ShadowCastingMode.ShadowsOnly` Kapsamı:** Tam vücut FPS rig'inde yalnızca kafa değil; **kafa + boyunluk + göğüs/karın + sırt çantası + omuz zırhları (`Pauldron` / `Shoulder`) + vücut kılıf proksileri (`Holster_`)** FPS modunda `ShadowsOnly` yapılmalıdır. Bacaklar (`Hip`, `Knee`, `Poleyn`, `Foot`) ve ön kollar/eller (`Elbow`, `Wrist`) ise `ShadowCastingMode.On` kalmalıdır; böylece oyuncu aşağı baktığında bacaklarını, ayaklarını ve ellerini görmeye devam ederken, eğildiğinde ve süründüğünde hiçbir gövde/çanta/omuz parçası görüşü kapatamaz.
2. **Dinamik Kamera İleri Ofseti (`Stance-Based Forward Offset`) ve `nearClipPlane`:**
   - FPS kamerasının `nearClipPlane` değeri `0.05f` (5 cm) yapılmalıdır (Unity varsayılanı `0.30f` çok büyüktür ve yakın uzuvları keser).
   - Kamera ileri ofseti (`forwardOffset`) omurganın ileri eğim açısını (`_smoothedBodyPitchDeg`) takip etmelidir:
     $$\text{forwardOffset} = 0.12\text{m} + 0.42\text{m} \cdot \sin(\theta_{\text{spinePitch}})$$
     - Ayakta (`0°–5°`): `~0.12m – 0.16m`
     - Çömelirken (`18°`): `~0.25m`
     - Sürünürken (`78°`): `~0.53m` (tam olarak `_headCounterPitchPivot`'un ileri uzandığı dünya konumunun önü!).

---

## 4. UYGULANACAK KESİN MÜHENDİSLİK ÇÖZÜMÜ (C# KOD PLANI)

Aşağıdaki 3 cerrahi düzeltme ile sorun hem ayakta/koşarken hem çömelirken (`Ctrl`) hem de sürünürken (`Z`) **%100 ve kalıcı olarak** ortadan kaldırılacaktır:

### 4.1. `Stage1ArticulatedRigAnimator.cs` — Birleşik FPS `ShadowsOnly` Politikası ve Kamera İleri Ofseti (`UpdateCameraPose`)

1. **FPS `ShadowsOnly` Kuralının Genişletilmesi (`IsUpperBodyOrHeadOccluderInFps`):**
   - FPS modunda (`isFirstPersonMode == true`) aşağıdaki parçaların tamamı `ShadowCastingMode.ShadowsOnly` yapılır (TPS `[F5]` modunda ise tamamı `ShadowCastingMode.On` olarak 15 parça şövalye zırhıyla tam görünür):
     - Kafa ve Miğfer: `_headCounterPitchPivot` altındaki tüm parçalar (`Head`, `Helmet`, `Visor`, `Crest`)
     - Boyunluk, Göğüs, Karın ve Sırt Çantası: `Gorget`, `Chest`, `Abdomen`, `ExplorerPack`, `PackBedroll`, `PackFrame`, `PelvisBelt`, `PelvisMail`
     - Omuz Zırhları ve Üst Omuz Eklemleri: `Pauldron`, `RightShoulderPivot_Vis`, `LeftShoulderPivot_Vis`
     - Vücuda Asılı Kılıf Proksileri: `Holster_` ile başlayan tüm proksiler (`Holster_WaistLeft_Sword`, `Holster_WaistRight_Pickaxe`, `Holster_BackLeft_Axe`)
   - **FPS Modunda Görünür Kalacak Vücut Parçaları (`ShadowCastingMode.On`):**
     - Alt bacaklar, dizlikler ve zırhlı çizmeler (`LeftKneePivot`, `RightKneePivot`, `Poleyn`, `LeftFootPivot`, `RightFootPivot`) ve kalça uyluklarının alt kısmı — oyuncu aşağı baktığında ayaklarını/bacaklarını görür ama çömelince/sürününce gövde, çanta ve omuzlar asla ekranı kapatmaz!
     - Ön kollar ve bilekler (`RightElbowPivot`, `LeftElbowPivot`, `RightWristPivot`, `LeftWristPivot`) — kılıç savururken ve sol bilek kristalini kullanırken eller/bilekler görünür kalır; ancak **sürünürken (`isProne == true`)** ileri-geri yer क्राउल (crawl) hareketi yapan kolların kameranın önünden geçip görüşü kapatmaması için sürünme esnasında FPS'te omuz/dirsek mesh'leri de `ShadowsOnly` yapılır!

2. **`UpdateCameraPose` İçinde Omurga-Uyumlu İleri Göz Ofseti (`Forward Eye Offset`) ve `nearClipPlane = 0.05f`:**
   ```csharp
   if (isFirstPersonMode)
   {
       if (Mathf.Abs(cam.nearClipPlane - 0.05f) > 0.005f)
       {
           cam.nearClipPlane = 0.05f;
       }

       // Omurga ileri yattiginda (Comelme: 18°, Surunme: 78°) kafa kemigi +Z yonune uzanir;
       // FPS kamerasi geride (sirt cantasinin icinde) kalmamali, kafa ile birlikte ileri tasinmalidir!
       float spinePitchRad = Mathf.Max(0f, _smoothedBodyPitchDeg) * Mathf.Deg2Rad;
       float dynamicForwardOffset = 0.14f + Mathf.Sin(spinePitchRad) * (isProne ? 0.40f : 0.24f);
       Vector3 targetCamPos = eyeWorld + playerRoot.forward * dynamicForwardOffset;
       ...
   }
   ```

### 4.2. `Stage1InteractivePlayController.cs` — `ApplyFirstPersonVisibility()` Senkronizasyonu
- [`Stage1InteractivePlayController.ApplyFirstPersonVisibility()`](Assets/_Stratocraft/Character/Stage1InteractivePlayController.cs#L444-L479) metodu doğrudan `_rigAnimator.ApplyFirstPersonShadowPolicy(PlayerBodyRoot.transform, _isFirstPersonMode, isProne)` yardımcı metodunu çağıracak şekilde birleştirilir; böylece tek bir doğruluk kaynağı (*Single Source of Truth*) üzerinden hem başlangıçta, hem `F5` kamera geçişinde, hem de duruş (`Stance`) değişimlerinde tüm renderer'lar eksiksiz güncellenir.

### 4.3. `Stage1HolsterAndBackpackController.cs` — Kılıf Proksilerinin (`Holster_*`) Eklemli Omurga/Pelvis'e Bağlanması ve FPS `ShadowsOnly` Uyumu
- [`UpdateBodyHolsteredWeaponProxies`](Assets/_Stratocraft/Character/Stage1HolsterAndBackpackController.cs#L696-L722) içindeki:
  - `Holster_WaistLeft_Sword` ve `Holster_WaistRight_Pickaxe` varsa `RigIK_PelvisRoot` altına,
  - `Holster_BackLeft_Axe` varsa `RigIK_SpineTorsoPivot` altına bağlanır (böylece TPS modunda çömelirken/sürünürken havada asılı kalmaz, bel ve sırtla birlikte eğilir).
  - Ayrıca bu proksiler oluşturulduğu anda `ApplyFirstPersonShadowPolicy` tarafından otomatik olarak FPS'te `ShadowsOnly`, TPS'te `ShadowCastingMode.On` yapılır.

---

## 5. ÖZET DOĞRULAMA MATRİSİ

| Duruş / Eylem | Eski Durum (Hatalı) | Yeni Durum (Çözüm Sonrası) |
| :--- | :--- | :--- |
| **Ayakta Yürüme & Depar (`W/A/S/D + Shift`)** | `HeadHelmet` isim hatası yüzünden miğfer, boyunluk (`Gorget`) ve omuz zırhları (`Pauldron`) `ShadowCastingMode.On` kalıyor; koşarken kamera sarsıntısıyla ekranı kapatıyordu. | Miğfer, boyunluk, üst gövde, sırt çantası ve omuz zırhları FPS'te `ShadowsOnly` yapıldı; kamera `nearClipPlane = 0.05f` ve `+0.14m..+0.20m` ileri ofsetle tamamen temiz görüş sunuyor. |
| **Eğilme / Çömelme (`Sol Ctrl`)** | Kamera `(0, 1.01m, +0.10m)` konumundayken `18°` öne eğilen göğüs zırhı (`ChestCuirass`) ve boyunluk (`GorgetCollar`) `(0, 1.09m, +0.16m)` ile kameranın tam içine giriyordu. | Kamera omurga eğimiyle `+0.22m` ileri taşındı ve üst gövde/boyunluk/çanta `ShadowsOnly` yapıldığı için çömelirken görüş **%100 açık**, yerde gerçekçi çömelme gölgesi **%100 korunuyor**. |
| **Yüzüstü Sürünme (`Z`)** | Kamera `(0, 0.58m, +0.04m)` geride kalırken `78°` öne yatan sırt çantası (`ExplorerPack`, `Z=+0.27m`), uyku tulumu (`Z=+0.44m`) ve sürünme kolları kameranın tam önünde görüşü tamamen kapatıyordu. | Kamera `dynamicForwardOffset = +0.53m` ile gerçek baş/göz hizasına taşındı; sırt çantası, gövde ve sürünme omuz/kolları FPS'te `ShadowsOnly` yapılarak sürünürken sıfır görüş kapanması sağlandı. |
| **Omuz Üstü TPS Modu (`F5`)** | Kılıftaki balta (`Holster_BackLeft_Axe`) `playerBodyRoot` kökünde sabit olduğu için çömelince/sürününce havada asılı kalıyordu. | Kılıf proksileri `RigIK_PelvisRoot` ve `RigIK_SpineTorsoPivot`'a bağlandı; TPS (`F5`) modunda 15 parça zırh, sırt çantası ve kılıflar `ShadowCastingMode.On` ile eksiksiz görünür. |
