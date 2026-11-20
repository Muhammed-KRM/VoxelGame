# Stratocraft — Dalga A (P0 Kritik Savaş, AI, Biyoloji, Ölüm/Yatak ve Mimari Şekiller) Kalite Kapısı Raporu

> **Tarih:** `2026-11-20`
> **Kapsam:** `Faz A.1` .. `Faz A.6` + `WaveADeepDiagnosticProbe` (`[Home]`) + 4'lü Dokümantasyon Seti (`docs/dalga-a-p0-savas-ai-biyoloji-ve-mimari/DOK1..DOK4`)

---

## 1. Uygulanan Fazlar ve Çıktılar

| Faz | Kapsam | Yeni / Güncellenen Ana Dosyalar |
| :--- | :--- | :--- |
| **Probe** | Sıfır-GC Dalga A Derin Teşhis Loglayıcısı (`[Home]` + kapanışta otomatik kayıt) | `Assets/_Stratocraft/Core/Logging/WaveADeepDiagnosticProbe.cs` |
| **Faz A.1** | Mimari Şekiller (`0..14 + 63`): su geçirmez dışa dönük `EmitArchVoussoir`, `EmitRoofCorner`, `EmitSlopedRamp`, tek-parça L-prizma `EmitStairSteps`, `EmitTrapezoidalKeystone`, 24-yönlü kapalı `EmitFacetedCrystalCluster`, `±0.50f` snap, döndürülmüş etek koruması, doğru Türkçe etiketler, `[R]`/`[Shift+R]` takası, `[C]` sürekli hayalet, `ShapeId 63` `PackChiseled` eşleşmesi | `ArchitecturalShapeCatalog.cs`, `HybridSurfaceNetsAndShapeMesher.cs`, `Stage1MatterCrystalAndMiningController.cs` |
| **Faz A.2** | Yönlü Kılıç Biyomekaniği: sabit 240 Hz yarı-örtük Euler PD (`Lerp` ezmesi sonu), canlı fare yön saptırma ve hayalet yaw kayması temizliği, hareket/duruş + 1H/2H + silah ağırlığı ivmesi, sert açılı kamera/ayak çekme sınırı, anatomik kol erişim küresi, ayrık tek-vuruş `Thrust` (`~0.35s`), `dt`-bağımsız ağız hizalaması, `UpdateCameraPose` önce (`0` kare gecikme), çift stamina temizliği, ayrık normalli parlak kılıç izi, FPS sağda yüzen kahverengi önkol kutusu temizliği ve Katana 90° mesh onarımı | `MeleeBiomechanicsSolver.cs`, `Stage1DirectionalCombatBridge.cs`, `Stage1CombatFeedbackAndArenaBridge.cs`, `Stage1ArticulatedRigAnimator.cs`, `Stage1InteractivePlayController.cs` |
| **Faz A.3** | 3 Aşamalı Adaptif Canlı AI: **2x çift HP düşüşü temizliği** (vahşi canlı + Düellocu NPC), `AttackWindup -> AttackActive (ileri koni hitbox) -> AttackRecovery` + ileri doğru animasyon + ıskalama (`Whiff`) + %100 `RMB` blok & Riposte, türlere özel çok yönlü saldırı repertuarı, 8-kayıtlı `CreaturePatternMemory` ile yönlü blok/kaçınma öğrenmesi, mesafeye göre güç/zeka ölçekleme, Pasif/Nötr/Agresif + sürü alarmı, `< 48m` ve savaşta DES tenzil kilidi ile ışınlanma/kaybolma onarımı | `CreatureCombatAiProfile.cs`, `CreaturePatternMemory.cs`, `Stage1CreatureSimulationBridge.cs`, `Stage1CombatFeedbackAndArenaBridge.cs` |
| **Faz A.4** | Gerçek `0 HP` Ölüm Akışı & Kurulabilir Yatak: 16 ölümsüzlük kilidi temizliği, `0 HP -> 30 sn BleedOutCrawl` (Bandajla kalkma veya `[R]` pes etme) -> Ölüm Ekranı & Yeniden Doğma, `BedBlock (168)` + `[E]` `SpawnPoint` kaydı (yatak kırılırsa başlangıca dönüş) | `BedSpawnPointService.cs`, `MaterialIdCatalog.cs`, `CharacterVitalsComponent.cs`, `Stage1PlayerMovementBridge.cs`, `Stage1StructuralCollapseBridge.cs`, `Stage1InteractivePlayController.cs` |
| **Faz A.5** | Aktif Açlık/Susuzluk & Tüm Slotlardan Tüketim: 3 zorla `2000/100` sıfırlama temizliği (~35 dk açlık / ~23 dk susuzluk), **`ActiveHolsterSlot == 8` kilidi kaldırıldı** (Hotbar `0..9` tüm slotlardan yeme/içme/bandajlama), su dolu Mataradan (`[E]`) ve doğal tatlı sudan (`[E]`) içme, Bandajın `HeavyBleeding` temizlemesi, kompakt alt HUD Amber Açlık & Mavi Susuzluk çubukları | `ThermalAndBiologySystem.cs`, `Stage1VisualHotbarAndInventoryUi.cs`, `Stage1InteractivePlayController.cs` |
| **Faz A.6** | Ekran Görüntüsü HUD & İklim Onarımı: tüm `:0.1 -> :0.0` C# biçim hataları (`211°C / 371°C / 10131 hPa -> 21.0°C / 37.0°C / 1013.0 hPa`), üst HUD çakışması giderimi ve sıcak çölde `Snow` fazı engellemesi | `Stage1InteractivePlayController.cs` |

---

## 2. Otomatik Test ve Derleme Doğrulaması

* **`.NET 10` (`Release`, `TreatWarningsAsErrors=true`):** **243 / 243 PASS** (`0 Error / 0 Warning`)
  * `WaveAArchitecturalShapesTests` (`3` test — 16 mimari şekil × 24 SO(3) rotasyon × 2 etek modu = **768 kombinasyon su geçirmezlik / 0 ters normal / 0 açık kenar / 0 FullFaceMask yarığı / 0 etek taşması**)
  * `WaveAMeleeBiomechanicsTests` (`5` test — 240 Hz PD, **8 yönlü tam yay + sol-üst kabza erişim zarfı**, sert kamera çekme limiti, `0.35s` ayrık `Thrust`, `dt`-bağımsız `EdgeRoll`)
  * `WaveACreatureAndSurvivalTests` (`6` test — 1x tekil hasar & `0 HP` izni, koni ıskalama, alışkanlık öğrenme & yönlü blok, mesafe halkası ölçekleme, Yatak kırılma/doğma, dengeli açlık/susuzluk/bandaj/30s `BleedOut`)
* **Unity 6 (`6000.6.7f1`):** `compilationFailed = false`, `consoleErrors = 0`, `consoleWarnings = 0`.
* **C++20 Native & Python Asset Factory:** `quality-gate.ps1 -Phase 4` ile doğrulandı (`VRAM < 16 MB`, `HardcodedPathHits = 0`).
