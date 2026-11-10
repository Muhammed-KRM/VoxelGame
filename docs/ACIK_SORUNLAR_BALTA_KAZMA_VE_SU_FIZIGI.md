# Açık Sorunlar Raporu: Balta/Kazma Yönlü Savurma ve Su Fiziği / 3B Görünürlük Sorunları

**Tarih:** `2026-11-10`  
**İlgili Devir:** `Devir #043`  
**Durum:** Açık (Sonraki oturumda çözülecek sorunların tespiti)

---

## 1. Balta (`Slot 3`) ve Kazma (`Slot 2`) Savurma Sorunları

### 1.1. Fareyi Sağa Götürünce Baltanın/Kazmanın Kendi Kendine Sağ-Sol Yapıp Hızlıca Vurması
- **Gözlemlenen Sorun:**
  - Ağaç oyuncunun **solunda** dururken oyuncu fareyi **sola** çektiğinde balta ağaca düzgünce vuruyor ve saplanıyor (bu kısım doğru çalışıyor).
  - Ancak oyuncu baltayı geri çekmek veya kurmak için fareyi **sağa** götürdüğünde, baltanın sadece sağ tarafa geçip beklemesi gerekirken garip bir döngüye giriyor; balta kendi kendine sürekli **sağ-sol** yapmaya başlıyor ve ağaca ardı ardına çok hızlı vurup ağacı saniyeler içinde kesiyor.
  - Aynı sorun **Kazma (`Slot 2`)** kullanırken de birebir yaşanıyor.
- **Beklenen Davranış:**
  - Fare **sağa** götürüldüğünde balta/kazma yalnızca sağ tarafa (kurulma/hazırlık konumuna) geçmeli, ağaca vurmamalı.
  - Yalnızca fare **hedefe doğru (örneğin soldaki ağaca doğru sola)** savrulduğunda vuruş yapmalı ve saplanmalı.
- **Log Kayıtlarındaki Kanıt (`Logs/swing_summary.log`):**
  - `17:43:46 – 17:44:15` (`Slot=2` Kazma, `SwingId=1..50`) ve `17:44:22 – 17:45:20` (`Slot=3` Balta, `SwingId=51..104`) kayıtlarında saniyede 2–3 kez ardışık otomatik savurma (`Outcome=3 Lodge` ve `Outcome=1 Ricochet`) tetiklendiği görülmektedir.
  - `Stage1DirectionalCombatBridge.cs` içinde alet slotları (`isToolSlot`) için fare savurma eşiğinin kaldırılıp her `LMB` basılı karede sabit `ToolChopArcProfile` yayının (`StartAnglesRad -> EndAnglesRad`) fare yönünden bağımsız tetiklenmesi, oyuncunun fareyi sağa çekmesini de yeni bir saldırı sayıp aleti sürekli sağ-sol sallamaktadır.

---

## 2. Su Fiziği ve Görselleştirme Sorunları

### 2.1. Suyun Yokuş Aşağı Gidişinin Görülmemesi ve Anında Aşağıda Belirmesi
- **Gözlemlenen Sorun:**
  - Oyuncu matarayla yere su bıraktığında su önce oyuncunun hemen önünde oluşuyor, ardından aradaki yokuştan akıp gidişi hiç görülmeden **anında yokuşun aşağısında** beliriyor.
  - Suyun yamaç boyunca akış süresi/yolculuğu takip edilemiyor; su sanki yerin altına girip kayboluyor ve aşağıda tekrar çıkıyor gibi davranıyor.

### 2.2. Suyun Hacminin Çok Ufak / İnce Olması (3B Blok Hissiyatının Eksikliği)
- **Gözlemlenen Sorun:**
  - Dökülen suyun hacmi ve görsel kalınlığı çok ufak/ince duruyor; bu yüzden hem arazide zor seçiliyor hem de akarken kayboluyormuş gibi görünüyor.
  - Suyun **Minecraft benzeri daha belirgin, dolgun ve 3 boyutlu (3D) bir hacme** sahip olması gerekiyor ki hem koyulduğu anda hem de akarken net biçimde görülebilsin.

### 2.3. Suyun Yerle Temas Etmiyor / Havada Asılı Duruyor Gibi Görünmesi
- **Gözlemlenen Sorun:**
  - Yamaçta veya çukurda duran su kütlesi zeminle (`SurfaceNets` çim araziyle) tam temas etmiyor; yerin üstünde **havada asılı duran** kopuk mavi bir yüzey gibi görünüyor (`media_1794322047915_a9d36867.jpg` ve `media_1794322114226_ff2e6783.jpg`).

### 2.4. Farklı Açılardan Yere Bakıp Su Döküldüğünde Suyun Koyulmaması / Anlaşılamaması
- **Gözlemlenen Sorun:**
  - Oyuncu elinde su matarasıyla yere farklı açılardan bakarak sağ tıkladığında çoğu açıda su dünyaya koyulmuyor ya da koyulup koyulmadığı hiç anlaşılmıyor.
  - Yere bakıp su koyma etkileşiminin (hedef voksel tespiti, görsel hacim ve geri bildirim) her bakış açısından tutarlı ve net olması gerekiyor.
