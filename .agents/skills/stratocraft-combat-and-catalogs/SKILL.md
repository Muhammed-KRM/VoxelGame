---
name: stratocraft-combat-and-catalogs
description: >-
  Canonical gameplay rules and catalog invariants for Stratocraft: 240 Hz directional
  melee (Camera-Clutch + 1-Euro Filter + Stable PD, Forte/Foible), 15-piece 3-layer
  armor, 16x32x4 Anvil + 4 Harmonizer catalysts, 3D Fence BFS Clans (KGP/Crystal HP/3-way
  IsSiegeable), 75 Physical Batteries (Vertical Pole Lock), 25 Mines + 6 Fueled Trap Cores,
  13 Bosses (Phoenix Ember Egg), 21 Mobs, and 12 Global Disasters. Activate before coding
  Faz 3 through Faz 9 gameplay systems.
---

# Stratocraft Savas Fizigi ve Kanonik Veri Katalogu Sabitleri

Kodlama sirasinda eski sabit uretmemek icin `STRATOCRAFT_GELISTIRICI_DOKUMANI.md` **Bolum 4.14** tablolarini tek kaynak al ve su degismez kurallari koru:

## 1. Klan, KGP, Kristal HP ve Kusatma (`Faz 3`)
- **Kristal HP (`Lv1..Lv5`):** `5.000 -> 10.000 -> 20.000 -> 40.000 -> 80.000 HP`.
- **KGP Esikleri (`Lv1..Lv5`):** `Lv1: 500`, `Lv2: 5.000`, `Lv3: 20.000`, `Lv4: 60.000`, `Lv5: 150.000+` (`24h High-Water Mark` + `IsAffiliatedMule_C` katir hesap korumasi).
- **Fiziksel Ritueller:** `5x5` Soyulmus Kutuk / Tas Tugla cercevesini **Cakmakla atesleme**, Surgun icin **Ruh Atesine parsomen atma**, Tac Gecisi icin **Altin Migfer**, Ittifak icin **Elmas**.
- **3-Yollu `IsSiegeable`:** `%35` aktif uye VEYA `%35` aktif KGP/SGP VEYA `1 Komutan + 3+ uye` cevrimici.
- **Anti-Obsidian Entombment (Bogulma Cezasi):** Kristal tamamen kapatilirsa aldigi **kusatma hasari `5.0x` katlanir**.

## 2. 240 Hz Savas, 15 Parca Zirh ve Ruh Tapinagi (`Faz 4`)
- **Ana Fizik `60 Hz` + Yakın Dovus `4x` Alt-Adim (`240 Hz` / `4.16 ms`):** `Camera-Clutch` (`LMB/RMB`), `1-Euro Filter`, `Stable PD` kas torku, `Forte (u in [0, 0.35])` vs `Foible (u in [0.65, 1.0])` kaldirac carpismasi.
- **Offline Sleeper & Ruh Tapinagi:** Cikis yapan oyuncu uyuyan beden birakir; Ruh Tapinagi diriltmesi kendi yataginin yaninda aninda dirilme ve zayiflik cezasini silme saglar.

## 3. 75 Batarya, 25 Mayin ve 6 Tuzak Cekirdegi (`Faz 5`)
- **75 Batarya (`Bolum 4.14.5`):** Merkezde `[Batarya Cekirdegi]` + **Dikey Kutup Kilidi**: Her bataryanin merkez cekirdeginin **`(Ust [+Y], Alt [-Y])` komsu blok cifti benzersizdir** (alt-kume cakismasini onler). Tum bloklar `BlockOriginState == 1` (`OyuncuItemBlogu`) olmalidir.
- **6 Tuzak Cekirdegi (`Bolum 4.14.6`):** Tek bir `3x3` Magma cercevesine konulan **6 farkli yakitla** (*Komur, Paketlenmis Buz, Barut, Zehir Ozu, Lav Kovasi, Magma/Yildirim/Karanlik Madde*) calisir.
- **25 Fiziksel Mayin:** `5 Seviye (Alt Blok + Basinc Plakasi) x 5 Sutun Tetikleyici Oz` matrisi (`#1 Patlama .. #25`).

## 4. 13 Boss, 12 Felaket ve 4 Harmonizer Katalizoru (`Faz 6 - Faz 7`)
- **Phoenix (`Anka Kusu`) Kurali:** `0 HP` oldugunda yerde **`10 sn` boyunca `2.500 HP` Kor Yumurtaya** donusur; `10 sn` icinde fiziksel hasarla kirilmazsa **`%60 HP` (`6.000 HP`) ve `1.5x` hasarla** yeniden dogar.
- **4 Kanonik Harmonizer Katalizoru:** `1. Saf Simya Civasi & Tuz Kristali`, `2. Titan Golem Cekirdegi / Boyut Kristali`, `3. Karanlik Madde`, `4. Yildiz Cekirdegi & Khaos Kristali`.
