# Alt Ajan Araştırma Raporu: Unity 6 URP 17 Forward+ Bootstrap, SRP Batcher ve StratocraftUberShader Mimarisi

- **Tarih:** 2026-10-30
- **Alt Ajan ID:** 499c87dc-7c2f-4037-9523-b76e912a7510
- **Ana Rapor:** [../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md](../FAZ1_FAZ2_SORUN_VE_COZUM_MIMARI_RAPORU.md)

---

# URP Render Tanı Raporu (Stratocraft, Unity 6000.6.7f1 / URP 17.6.0 / Entities Graphics 6.6.0)

## 0. Yerelde bulunan KRİTİK sorunlar (koddan doğrulandı)
1. **Hiç URP Asset yok**: `GraphicsSettings.m_CustomRenderPipeline {fileID:0}` ve 6 Quality seviyesinin hepsinde `customRenderPipeline {fileID:0}` → proje fiilen **Built-in RP** ile çalışıyor. Sadece `Assets/UniversalRenderPipelineGlobalSettings.asset` var (GraphicsSettings `m_RenderPipelineGlobalSettingsMap`'te kayıtlı, guid e08753f9...) — bu tek başına SRP'yi etkinleştirmez.
2. **AYNI İSİMLİ İKİ SHADER**: `StratocraftVertexColorLit.shader` (CGPROGRAM, UnityCG.cginc, LightMode etiketi yok, SABİT ışık yönü `float3(0.45,0.82,-0.35)`) ile `StratocraftVertexColorUberLit.shader` İKİSİ DE `Shader "Stratocraft/VertexColorUberLit"` adını taşıyor. `Shader.Find` hangisini döneceği belirsiz. Built-in RP'de URP-etiketli olan geçersiz → CG olan kullanılıyor → sabit ışık + gölge yok = "düz/unlit" görünümün ana sebebi. URP'ye geçince bile CG olan LightMode'suz pass'ı "SRPDefaultUnlit" olarak çizilir. **Çözüm: StratocraftVertexColorLit.shader'ı sil veya `Stratocraft/Legacy/VertexColorLitBuiltin` olarak yeniden adlandır.**
3. `Stratocraft.Editor.asmdef` referanslarında `Unity.RenderPipelines.Universal.Runtime`, `Unity.RenderPipelines.Core.Runtime` (ve isteğe bağlı `Unity.RenderPipelines.Universal.Editor`) YOK → URP API kullanan editör betiği derlenmez; eklenmeli.
4. **SRP Batcher uyumsuzluğu**: Her iki URP shader'ında ShadowCaster/DepthOnly pass'ları `LitInput.hlsl` include ediyor; bu, ForwardLit'teki `UnityPerMaterial` ile FARKLI bir CBUFFER tanımlar → "UnityPerMaterial CBuffer inconsistent size" → SRP Batcher dışı (Entities Graphics için ölümcül). `ShadowCasterPass.hlsl`, `DepthOnlyPass.hlsl`, `DepthNormalsPass.hlsl` yalnızca `_ALPHATEST_ON` tanımlıysa `_BaseMap/_BaseColor/_Cutoff` ister (URP 17.6 kaynağında doğrulandı) → LitInput.hlsl YERİNE kendi CBUFFER'ını içeren ortak bir `StratocraftInput.hlsl` include et.
5. Eksikler: `_ADDITIONAL_LIGHTS`, `_CLUSTER_LIGHT_LOOP` (Forward+), fog, `_SCREEN_SPACE_OCCLUSION`, DepthNormals pass, ShadowCaster'da `_CASTING_PUNCTUAL_LIGHT_SHADOW`. VertexColorUberLit'te `DOTS_INSTANCING_ON` yok (UberShader_UVFree'de var). Uber shader `GetMainLight()` gölgesiz çağırıyor.
6. **URP 17.x (Unity 6.1+) değişikliği**: `_FORWARD_PLUS` keyword'ü **kaldırıldı/deprecated**, yerine `_CLUSTER_LIGHT_LOOP` (Core.hlsl:8 ve ForwardPlusKeyword.deprecated.hlsl'de doğrulandı). DOTS için URP'nin kendi yolu: `#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"` (içinde `multi_compile _ DOTS_INSTANCING_ON` + `target 4.5 DOTS_INSTANCING_ON`).

## 1. URP Asset'i tamamen editör betiğinden oluşturma (URP 17.6 API, paket kaynağından doğrulandı)
- `public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null)` — public. İçeride `ResourceReloader.ReloadAllNullIn` çağırır.
- `CreateRendererAsset(...)` **internal**; onun yerine `CreateInstance<UniversalRendererData>()` + `postProcessData = PostProcessData.GetDefaultPostProcessData()` (public) yap.
- `UniversalRendererData.renderingMode` public setter (`RenderingMode.ForwardPlus = 2`).
- Public setter'lar: `shadowDistance, shadowCascadeCount (1..4, aralık dışı exception), cascade2Split/3Split/4Split, cascadeBorder, shadowDepthBias, shadowNormalBias, mainLightShadowmapResolution, supportsHDR, supportsCameraDepthTexture, supportsCameraOpaqueTexture, useSRPBatcher, msaaSampleCount, renderScale, gpuResidentDrawerMode`.
- **INTERNAL setter'lar** (SerializedObject ile yazılmalı): `supportsMainLightShadows`→`m_MainLightShadowsSupported`, `supportsSoftShadows`→`m_SoftShadowsSupported`, `softShadowQuality`→`m_SoftShadowQuality`, `additionalLightsRenderingMode`→`m_AdditionalLightsRenderingMode` (PerPixel=1), `supportsAdditionalLightShadows`→`m_AdditionalLightShadowsSupported`.

```csharp
using UnityEditor; using UnityEngine; using UnityEngine.Rendering; using UnityEngine.Rendering.Universal;
public static class StratocraftUrpBootstrap {
  const string Dir = "Assets/_Stratocraft/Rendering/Settings";
  [MenuItem("Stratocraft/Render - URP Kur ve Ata")]
  public static void Ensure() {
    if (AssetDatabase.IsAssetImportWorkerProcess()) return;
    string pPath = Dir + "/Stratocraft_URP.asset", rPath = Dir + "/Stratocraft_URP_Renderer.asset";
    var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pPath);
    if (urp == null) {
      if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/_Stratocraft/Rendering","Settings"); }
      var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
      rd.postProcessData = PostProcessData.GetDefaultPostProcessData();
      rd.renderingMode = RenderingMode.ForwardPlus;          // Entities Graphics icin zorunlu
      AssetDatabase.CreateAsset(rd, rPath);                  // ONCE renderer kaydedilmeli
      urp = UniversalRenderPipelineAsset.Create(rd);
      AssetDatabase.CreateAsset(urp, pPath);
    }
    urp.useSRPBatcher = true; urp.supportsHDR = true; urp.supportsCameraDepthTexture = true;
    urp.shadowDistance = 120f; urp.shadowCascadeCount = 4; urp.mainLightShadowmapResolution = 2048;
    urp.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled; // DOTS/BRG ile cakismasin; Entities Graphics kendi BRG'sini kullanir
    var so = new SerializedObject(urp);
    so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
    so.FindProperty("m_SoftShadowsSupported").boolValue = true;
    so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
    so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
    so.ApplyModifiedPropertiesWithoutUndo();
    EditorUtility.SetDirty(urp);
    GraphicsSettings.defaultRenderPipeline = urp;
    int cur = QualitySettings.GetQualityLevel();
    for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }
    QualitySettings.SetQualityLevel(cur, true);
    AssetDatabase.SaveAssets();
  }
}
```
Tuzaklar: (a) Renderer data'yı `AssetDatabase.CreateAsset` ile URP asset'ten ÖNCE diske yaz, yoksa referans "missing" kalır. (b) `QualitySettings.renderPipeline` sadece AKTİF seviyeyi set eder → döngüde `SetQualityLevel(i,false)` gerekli (alternatif: `SerializedObject(QualitySettings asset) "m_QualitySettings.Array.data[i].customRenderPipeline"`). Quality'lere boş bırakmak da olur (defaultRenderPipeline'a düşer) ama açık atama daha güvenli. (c) `GraphicsSettings.renderPipelineAsset` eski/obsolete; `defaultRenderPipeline` kullan. (d) `[InitializeOnLoad]` + `delayCall` içinde çağırırken import worker kontrolü şart (Enforcer zaten bu kalıbı kullanıyor). (e) Atamadan sonra Entities Graphics sistemi sadece **OnCreate**'te kontrol eder → editörü/domain'i yeniden yükle (Play Mode'da domain reload kapalı olduğundan bir kez script recompile veya editör restart).
- **SSAO renderer feature** (public sınıf `ScreenSpaceAmbientOcclusion`): `var f = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>(); f.name="SSAO"; AssetDatabase.AddObjectToAsset(f, rd); rd.rendererFeatures.Add(f); rd.SetDirty();` Tuzak: `m_RendererFeatureMap` (localFileId listesi) de güncellenmeli — SerializedObject ile `m_RendererFeatureMap`'e `AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id)` ekle; aksi halde inspector "missing feature" uyarısı verebilir.
- MCP `script_eval` / `script_run`: yukarıdaki `StratocraftUrpBootstrap.Ensure()` çağrısı ya da `EditorApplication.ExecuteMenuItem("Stratocraft/Render - URP Kur ve Ata")` doğrudan çalıştırılabilir. Doğrulama: `GraphicsSettings.currentRenderPipeline?.GetType().Name` → "UniversalRenderPipelineAsset".

## 2. Entities Graphics neden SRP ister
Kaynak (`EntitiesGraphicsUtils.IsEntitiesGraphicsSupportedOnSystem`): `IsScriptableRenderPipelineUsed()` false ise, `GraphicsDeviceType.Null`, compute shader yoksa veya eski GL ise → false; `EntitiesGraphicsSystem.OnCreate` bu durumda `Enabled=false` yapıp tam sizin gördüğünüz mesajı loglar. Sebep: BatchRendererGroup + DOTS instancing (unity_ObjectToWorld vb. GPU buffer'dan `UNITY_ACCESS_DOTS_INSTANCED_PROP`) yalnız SRP Batcher'lı SRP shader'larıyla çalışır. Ayrıca `ValidateUsingURPForwardPlus()` URP renderer'ı reflection ile okur ve **Forward+ değilse uyarı** verir (Forward'da per-object ışık listesi BRG ile çalışmaz). Sonuç: tüm ECS-render materyalleri SRP-Batcher uyumlu + `DOTS_INSTANCING_ON` varyantlı olmalı; aksi halde pembe/"loading" materyal. Docs: https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest (Requirements: URP/HDRP, URP'de yalnız Forward+).

## 3. Minimal doğru pass listesi (URP 17.6, SRP Batcher + DOTS uyumlu)
Ortak `StratocraftInput.hlsl`: `CBUFFER_START(UnityPerMaterial) ... CBUFFER_END` + (DOTS override istenirse) `#ifdef UNITY_DOTS_INSTANCING_ENABLED UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata) ... UNITY_DOTS_INSTANCING_END #endif`. TÜM pass'lar bunu include eder (aynı CBUFFER düzeni = SRP Batcher).
```hlsl
SubShader { Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
 Pass { Name "ForwardLit" Tags{"LightMode"="UniversalForward"}
  HLSLPROGRAM
  #pragma target 4.5
  #pragma vertex Vert
  #pragma fragment Frag
  #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
  #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
  #pragma multi_compile _ _CLUSTER_LIGHT_LOOP          // eski _FORWARD_PLUS
  #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
  #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
  #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
  #pragma multi_compile_fog                              // veya include_with_pragmas Fog.hlsl
  #pragma multi_compile_instancing
  #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
  #include "StratocraftInput.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  ENDHLSL }
 Pass { Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ColorMask 0
  HLSLPROGRAM #pragma target 4.5
  #pragma vertex ShadowPassVertex
  #pragma fragment ShadowPassFragment
  #pragma multi_compile_instancing
  #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
  #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
  #include "StratocraftInput.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
  ENDHLSL }
 Pass { Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ZWrite On ColorMask R  ... DepthOnlyPass.hlsl (DepthOnlyVertex/DepthOnlyFragment) }
 Pass { Name "DepthNormals" Tags{"LightMode"="DepthNormals"} ZWrite On ... DepthNormalsPass.hlsl (DepthNormalsVertex/DepthNormalsFragment) } // SSAO icin zorunlu
 // Meta: yalniz lightmap bake edilecekse gerekli; voxel dinamik dunyada atlanabilir. Deferred kullanilmayacaksa GBuffer gerekmez.
}
```
Fragment'ta: `float4 sc = TransformWorldToShadowCoord(posWS); Light L = GetMainLight(sc);` + ek ışıklar için `LIGHT_LOOP_BEGIN(GetAdditionalLightsCount()) Light l = GetAdditionalLight(lightIndex, posWS); ... LIGHT_LOOP_END` (Forward+'ta `InputData inputData; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS); inputData.positionWS=posWS;` tanımlanmalı — makro inputData'yı kullanır). AO: `AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);`. Fog: vertex'te `ComputeFogFactor(positionCS.z)`, fragment'ta `MixFog(color, fogFactor)`. Ambient: hardcoded hemi yerine `SampleSH(normalWS)` (RenderSettings Trilight'ı okur).
**Faset normaller**: (a) ddx/ddy `normalize(cross(ddy(posWS), ddx(posWS)))` — bedava ama 2x2 quad'da kenarlarda hafif titreme, FP hassasiyeti büyük koordinatlarda bozulur (floating origin var, sorun değil), ShadowCaster/DepthNormals'da tutarsız (DepthNormals'ta da aynı türevi kullan ki SSAO uysun). (b) Mesher'da üçgen başına vertex çoğaltma — 3x vertex ama en doğru, gölge bias'ı ve SSAO tutarlı. Voxel Surface Nets için öneri: ddx/ddy + DepthNormals'ta da ddx/ddy; mevcut `_SmoothFacetBlend` iyi fikir. Cross sırası D3D/GL'de ve render-texture flip'inde ters dönebilir → mevcut `dot(geo, vtx)<0` düzeltmesi doğru.

## 4. DRG / Astroneer görünümü ve ucuz URP karşılıkları
- **DRG**: prosedürel mağara mesh'i, sert faset (flat) gölgeleme, triplanar kaya dokuları, çok koyu ortam + oyuncu kaynaklı nokta ışıklar (flare) ve emisyonlu mineraller, yoğun mesafe sisi, bloom. URP: Forward+ (çok sayıda point light), `_ADDITIONAL_LIGHTS`, düşük ambient, Exponential fog, Bloom + ACES/Neutral tonemapping Volume.
- **Astroneer**: marching-cubes yumuşak arazi, dokusuz pastel renk/gradyan paleti (vertex rengi/yükseklik gradyanı), güçlü fresnel/rim, yumuşak AO, renk gradlama. URP: palet LUT (sizin 256x16 LUT), yükseklik gradyanı, rim `pow(1-NdotV,3)`, SSAO renderer feature (Depth Normals kaynağı, After Opaque kapalı), Color Adjustments/Lift-Gamma-Gain.
- **MatCap**: view-space normal.xy*0.5+0.5 → tek fetch, metal/kristal için ışıktan bağımsız parıltı (UberShader'da zaten var).
- **Biplanar (IQ)**: 3 yerine 2 fetch, https://iquilezles.org/articles/biplanar/ (kodunuz IQ'nun ma/mi/me yöntemine sadık; IQ ayrıca ddx/ddy'yi orijinal p'den alıp `textureGrad` kullanır → eksen değişiminde mip dikiş artefaktını önler: `SAMPLE_TEXTURE2D_ARRAY_GRAD`).
- **Height fog**: fragment'ta `fogH = exp(-max(posWS.y - h0,0)*k)` ile URP fog faktörünü çarp; ya da Volume'suz basit `lerp(col, fogColor, saturate(dist*density)*fogH)`.

## 5. Color32 paketleme (R=MatID, G=Temp, B=Sharp, A=AO) + LUT
- `COLOR` semantiği varsayılan **lineer interpolasyonludur**. Bir üçgende farklı MatID'li vertexler varsa ara değerler (ör. 12 ile 40 arası 26) yanlış materyal → bantlar. Ayrıca Gamma/Linear: vertex color'lar **dönüştürülmez** (Linear color space'te bile ham 0..1 gelir), bu iyi; ama `Mesh.colors` (float Color) yerine `colors32` / `SetVertexBufferParams` ile `VertexAttributeFormat.UNorm8 x4` kullan.
- Doğru yol: MatID'yi **nointerpolation** ile ayrı TEXCOORD'a taşı:
```hlsl
struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;
  nointerpolation uint matId:TEXCOORD2;   // provoking vertex (D3D: ilk vertex) degeri
  float3 tsa:TEXCOORD3;                   // Temp/Sharp/AO interpolasyonlu kalabilir
  float fog:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
o.matId = (uint)round(input.color.r * 255.0); o.tsa = input.color.gba;
// Frag: float3 alb = LOAD_TEXTURE2D(_MaterialPaletteLUT, uint2(i.matId, row)).rgb;  // point, mip yok, filtre yok
```
`LOAD_TEXTURE2D` (texel fetch) `SampleMaterialLutExact`'ten daha temiz; LUT import: Filter=Point, sRGB=açık (albedo satırları) veya ayrı lineer LUT (roughness/metal), mip yok, sıkıştırma yok. Alternatif: mesher, vertex'e `UInt8` TEXCOORD1 (`VertexAttributeFormat.UInt8, 4`) koyup shader'da `uint4` olarak oku (normalize olmadan). Not: nointerpolation + Surface Nets'te üçgen sınırında "hangi vertex kazanır" provoking-vertex'e bağlı → materyal sınırları üçgen çizgisini izler (stilize görünüme uygun). Yumuşak geçiş isteniyorsa 2 MatID + blend ağırlığı taşı.

## 6. Açık kaynak referanslar
- Unity resmi URP shader kaynakları (Unity Companion License): `Library/PackageCache/com.unity.render-pipelines.universal@ffe395a3a1d1/Shaders/SimpleLit.shader` — en iyi şablon (pass/keyword listesi buradan alındı).
- Cyanilux "Writing Shader Code for URP" rehberi: https://www.cyanilux.com/tutorials/urp-shader-code/
- IQ Biplanar: https://iquilezles.org/articles/biplanar/ ; Ryan Dowling Soka triplanar/biplanar yazısı: https://ryandowlingsoka.com
- ColinLeung-NiloCat/UnityURPToonLitShaderExample (MIT): https://github.com/ColinLeung-NiloCat/UnityURPToonLitShaderExample
- Unity-Technologies/Graphics (URP kaynak, SSAO/DepthNormals): https://github.com/Unity-Technologies/Graphics
- Hextant Studios flat shading notları: https://hextantstudios.com
- Entities Graphics docs: https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest/index.html ; URP Forward+: https://docs.unity3d.com/6000.0/Documentation/Manual/urp/rendering/forward-plus-rendering-path.html

## Düzeltme Adımları
- **Adım 1**: `StratocraftVertexColorLit.shader`'ı sil ya da adını `Stratocraft/Legacy/VertexColorLitBuiltin` yap (isim çakışması).
- **Adım 2**: `Stratocraft.Editor.asmdef`'e `Unity.RenderPipelines.Universal.Runtime`, `Unity.RenderPipelines.Core.Runtime` referanslarını ekle (Rendering runtime asmdef'ine de gerekirse).
- **Adım 3**: `StratocraftUrpBootstrap.Ensure()` (yukarıdaki kod) ekle; Enforcer'ın `EnforceAllSettings()` içinden çağır. Asset yolu: `Assets/_Stratocraft/Rendering/Settings/`. Forward+, SRP Batcher açık, 4 kaskad, soft shadows, PerPixel ek ışık, depth texture açık.
- **Adım 4**: Renderer'a SSAO feature ekle (Depth Normals kaynağı) + sahneye global Volume (Bloom, Tonemapping Neutral/ACES, Color Adjustments) — script ile `VolumeProfile` oluştur.
- **Adım 5**: `StratocraftInput.hlsl` oluştur (tek UnityPerMaterial CBUFFER + DOTS bloğu); her iki URP shader'da `LitInput.hlsl` include'unu kaldır, yerine bunu koy.
- **Adım 6**: Pass/keyword listesini §3'e göre güncelle: `_CLUSTER_LIGHT_LOOP`, `_ADDITIONAL_LIGHTS`, `_SCREEN_SPACE_OCCLUSION`, fog, `DOTS.hlsl` include_with_pragmas, `_CASTING_PUNCTUAL_LIGHT_SHADOW`, DepthNormals pass. Hardcoded hemi ambient yerine `SampleSH`.
- **Adım 7**: MatID'yi `nointerpolation uint` TEXCOORD ile taşı, LUT'u `LOAD_TEXTURE2D` ile oku; LUT import ayarlarını Point/no-mip/uncompressed yap (AssetPostprocessor'da).
- **Adım 8**: SceneBuilder'daki fallback zincirini koru ama `Shader.Find` sonucu null ise `Debug.LogError` ver (sessiz fallback hatayı gizliyor); Directional light `shadows = Soft` zaten var.
- **Adım 9**: Editörü yeniden başlat (Entities Graphics yalnızca OnCreate'te kontrol eder; domain reload kapalı), Console'da "Entities Graphics package disabled" mesajının kaybolduğunu ve Frame Debugger'da "SRP Batch" görüldüğünü doğrula; materyal inspector'ında "SRP Batcher: compatible" kontrol et.
- **Adım 10**: `quality-gate.ps1` ile 0 Error/0 Warning ve shader varyant sayısını (keyword patlaması) kontrol et; gerekirse `_SHADOWS_SOFT_LOW/MEDIUM/HIGH` ve `_ADDITIONAL_LIGHTS_VERTEX` çıkarılarak varyant azaltılabilir.

Not: Hiçbir dosya değiştirilmedi. Kontrol edilen dosyalar: d:\oyun\Assets\_Stratocraft\Rendering\Shaders\*.shader/.hlsl, d:\oyun\Assets\_Stratocraft\Editor\StratocraftProjectSettingsEnforcer.cs, StratocraftStage1SceneBuilder.cs (satır 845-897), ProjectSettings\GraphicsSettings.asset, QualitySettings.asset, ve URP/Entities Graphics paket kaynakları (Library\PackageCache).
</SYSTEM_MESSAGE>
