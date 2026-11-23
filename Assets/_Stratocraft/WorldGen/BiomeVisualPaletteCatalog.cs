#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.WorldGen.Generators;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// PT-10: Stratocraft Cok-Biyomlu Sanat Anayasasi 6 Bolge Renk Paleti kimlikleri.
    /// </summary>
    public enum BiomePaletteRegionId : byte
    {
        EmeraldSunForest = 0,      // 1. Zumrut Gunesi Ormani
        GoldenTwilightPlateau = 1, // 2. Altin ve Alacakaranlik Yaylasi
        SapphireLagoonCoast = 2,   // 3. Safir Lagun ve Prusya Kiyilari
        CrimsonAmethystCanyon = 3, // 4. Kizil Kor ve Ametist Kanyonlari
        SunlitGlacialPeaks = 4,    // 5. Gunesli Buzul Zirveleri
        ObsidianVoidCoast = 5      // 6. Obsidyen Hiclik Kiyisi
    }

    /// <summary>
    /// Saf C# 0-GC dogrusal-uyumlu sRGB [0..1] renk deger turu (.NET 10 ve Unity 6 ortak).
    /// </summary>
    public readonly struct RgbColor
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RgbColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RgbColor FromHex(uint rgbHex)
        {
            float r = ((rgbHex >> 16) & 0xFFu) * (1.0f / 255.0f);
            float g = ((rgbHex >> 8) & 0xFFu) * (1.0f / 255.0f);
            float b = (rgbHex & 0xFFu) * (1.0f / 255.0f);
            return new RgbColor(r, g, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ToHex24()
        {
            uint r = (uint)Math.Clamp((int)MathF.Round(R * 255.0f), 0, 255);
            uint g = (uint)Math.Clamp((int)MathF.Round(G * 255.0f), 0, 255);
            uint b = (uint)Math.Clamp((int)MathF.Round(B * 255.0f), 0, 255);
            return (r << 16) | (g << 8) | b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RgbColor Lerp(RgbColor a, RgbColor b, float t)
        {
            float ct = t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
            return new RgbColor(
                a.R + (b.R - a.R) * ct,
                a.G + (b.G - a.G) * ct,
                a.B + (b.B - a.B) * ct);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(RgbColor a, RgbColor b)
        {
            float dr = a.R - b.R;
            float dg = a.G - b.G;
            float db = a.B - b.B;
            return MathF.Sqrt(dr * dr + dg * dg + db * db);
        }

#if UNITY_5_3_OR_NEWER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color ToUnityColor(float alpha = 1.0f) => new Color(R, G, B, alpha);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 ToUnityVector4(float w = 1.0f) => new Vector4(R, G, B, w);
#endif
    }

    /// <summary>
    /// PT-10: Her biyomu 6 temel gorsel parametreyle (GrassPrimary, GrassSecondary, CliffStoneColor,
    /// ColoredShadowTint, FogAndSkyTint, NeonPair [NeonColorA, NeonColorB + uclu NeonColorC]) tanimlayan 0-GC yapi.
    /// </summary>
    public readonly struct BiomeVisualProfile
    {
        public readonly BiomePaletteRegionId RegionId;
        public readonly RgbColor GrassPrimary;
        public readonly RgbColor GrassSecondary;
        public readonly RgbColor CliffStoneColor;
        public readonly RgbColor ColoredShadowTint;
        public readonly RgbColor FogAndSkyTint;
        public readonly RgbColor NeonColorA;
        public readonly RgbColor NeonColorB;
        public readonly RgbColor NeonColorC;

        public BiomeVisualProfile(
            BiomePaletteRegionId regionId,
            RgbColor grassPrimary,
            RgbColor grassSecondary,
            RgbColor cliffStoneColor,
            RgbColor coloredShadowTint,
            RgbColor fogAndSkyTint,
            RgbColor neonColorA,
            RgbColor neonColorB,
            RgbColor? neonColorC = null)
        {
            RegionId = regionId;
            GrassPrimary = grassPrimary;
            GrassSecondary = grassSecondary;
            CliffStoneColor = cliffStoneColor;
            ColoredShadowTint = coloredShadowTint;
            FogAndSkyTint = fogAndSkyTint;
            NeonColorA = neonColorA;
            NeonColorB = neonColorB;
            NeonColorC = neonColorC ?? neonColorA;
        }

        public static BiomeVisualProfile Lerp(in BiomeVisualProfile a, in BiomeVisualProfile b, float t)
        {
            float ct = t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
            return new BiomeVisualProfile(
                ct < 0.5f ? a.RegionId : b.RegionId,
                RgbColor.Lerp(a.GrassPrimary, b.GrassPrimary, ct),
                RgbColor.Lerp(a.GrassSecondary, b.GrassSecondary, ct),
                RgbColor.Lerp(a.CliffStoneColor, b.CliffStoneColor, ct),
                RgbColor.Lerp(a.ColoredShadowTint, b.ColoredShadowTint, ct),
                RgbColor.Lerp(a.FogAndSkyTint, b.FogAndSkyTint, ct),
                RgbColor.Lerp(a.NeonColorA, b.NeonColorA, ct),
                RgbColor.Lerp(a.NeonColorB, b.NeonColorB, ct),
                RgbColor.Lerp(a.NeonColorC, b.NeonColorC, ct));
        }

        /// <summary>
        /// Biyomlar arasi atmosfer/gokyuzu/renkli-golge gecisini yaklasik 3 saniyede puruzsuzce harmanlar.
        /// </summary>
        public static BiomeVisualProfile SmoothTransitionStep(
            in BiomeVisualProfile current,
            in BiomeVisualProfile target,
            float deltaTimeSeconds,
            float transitionDurationSeconds = 3.0f)
        {
            if (deltaTimeSeconds <= 0.0f || !float.IsFinite(deltaTimeSeconds))
            {
                return current;
            }

            float tau = MathF.Max(0.15f, transitionDurationSeconds) * (1.0f / 3.0f); // 3*tau'da %95 yakinsama
            float alpha = 1.0f - MathF.Exp(-deltaTimeSeconds / tau);
            return Lerp(in current, in target, alpha);
        }
    }

    /// <summary>
    /// PT-10: 6 Bolge Sanat Anayasasi renk paleti katalogu, 40-80m gurultulu yumusak biyom harmanlama
    /// ve GPU global uniform (0.0 MB VRAM, 0 B/kare GC) koprusu.
    /// </summary>
    public static class BiomeVisualPaletteCatalog
    {
        public const int RegionCount = 6;
        public const float DefaultBiomeBlendWidthMeters = 60.0f;
        public const float MinBiomeBlendWidthMeters = 40.0f;
        public const float MaxBiomeBlendWidthMeters = 80.0f;

        // 1. Zumrut Gunesi Ormani (#1EA855 / #74D944, kaya #ECE7D9, safir golge #0E3B59, neon #00F5D4 / #FF3D9A)
        public static readonly BiomeVisualProfile EmeraldSunForestProfile = new(
            BiomePaletteRegionId.EmeraldSunForest,
            grassPrimary: RgbColor.FromHex(0x1EA855u),
            grassSecondary: RgbColor.FromHex(0x74D944u),
            cliffStoneColor: RgbColor.FromHex(0xECE7D9u),
            coloredShadowTint: RgbColor.FromHex(0x0E3B59u),
            fogAndSkyTint: RgbColor.FromHex(0x88D4E8u),
            neonColorA: RgbColor.FromHex(0x00F5D4u),
            neonColorB: RgbColor.FromHex(0xFF3D9Au));

        // 2. Altin ve Alacakaranlik Yaylasi (#FFB81C / #0096B4, kaya #FFF9ED, murdum golge #5B21B6, neon #00F5D4 / #FF1E8A)
        public static readonly BiomeVisualProfile GoldenTwilightPlateauProfile = new(
            BiomePaletteRegionId.GoldenTwilightPlateau,
            grassPrimary: RgbColor.FromHex(0xFFB81Cu),
            grassSecondary: RgbColor.FromHex(0x0096B4u),
            cliffStoneColor: RgbColor.FromHex(0xFFF9EDu),
            coloredShadowTint: RgbColor.FromHex(0x5B21B6u),
            fogAndSkyTint: RgbColor.FromHex(0xD8B4E2u),
            neonColorA: RgbColor.FromHex(0x00F5D4u),
            neonColorB: RgbColor.FromHex(0xFF1E8Au));

        // 3. Safir Lagun ve Prusya Kiyilari (#00B4A2 / #0284C7, beyaz falez #F8FAFC, golge #0B2B4C, mercan neon #FF5436 / #2CF7E0)
        public static readonly BiomeVisualProfile SapphireLagoonCoastProfile = new(
            BiomePaletteRegionId.SapphireLagoonCoast,
            grassPrimary: RgbColor.FromHex(0x00B4A2u),
            grassSecondary: RgbColor.FromHex(0x0284C7u),
            cliffStoneColor: RgbColor.FromHex(0xF8FAFCu),
            coloredShadowTint: RgbColor.FromHex(0x0B2B4Cu),
            fogAndSkyTint: RgbColor.FromHex(0x7CE6F7u),
            neonColorA: RgbColor.FromHex(0xFF5436u),
            neonColorB: RgbColor.FromHex(0x2CF7E0u));

        // 4. Kizil Kor ve Ametist Kanyonlari (#D9383A / #F4C469, ametist kaya #7E22CE, ametist golge #2A1154, neon #FF7700 / #E81CFF)
        public static readonly BiomeVisualProfile CrimsonAmethystCanyonProfile = new(
            BiomePaletteRegionId.CrimsonAmethystCanyon,
            grassPrimary: RgbColor.FromHex(0xD9383Au),
            grassSecondary: RgbColor.FromHex(0xF4C469u),
            cliffStoneColor: RgbColor.FromHex(0x7E22CEu),
            coloredShadowTint: RgbColor.FromHex(0x2A1154u),
            fogAndSkyTint: RgbColor.FromHex(0xE89D7Eu),
            neonColorA: RgbColor.FromHex(0xFF7700u),
            neonColorB: RgbColor.FromHex(0xE81CFFu));

        // 5. Gunesli Buzul Zirveleri (#FFFFFF / #7DD3FC, kaya #475569, kobalt golge #1E3A8A, neon #00F5D4 / #FB7185)
        public static readonly BiomeVisualProfile SunlitGlacialPeaksProfile = new(
            BiomePaletteRegionId.SunlitGlacialPeaks,
            grassPrimary: RgbColor.FromHex(0xFFFFFFu),
            grassSecondary: RgbColor.FromHex(0x7DD3FCu),
            cliffStoneColor: RgbColor.FromHex(0x475569u),
            coloredShadowTint: RgbColor.FromHex(0x1E3A8Au),
            fogAndSkyTint: RgbColor.FromHex(0xBAE6FDu),
            neonColorA: RgbColor.FromHex(0x00F5D4u),
            neonColorB: RgbColor.FromHex(0xFB7185u));

        // 6. Obsidyen Hiclik Kiyisi (#1E1B4B / #D99B26, kaya #312E81, golge #0F0A24, uclu neon #00F5D4 / #FF1E8A / #FFB81C)
        public static readonly BiomeVisualProfile ObsidianVoidCoastProfile = new(
            BiomePaletteRegionId.ObsidianVoidCoast,
            grassPrimary: RgbColor.FromHex(0x1E1B4Bu),
            grassSecondary: RgbColor.FromHex(0xD99B26u),
            cliffStoneColor: RgbColor.FromHex(0x312E81u),
            coloredShadowTint: RgbColor.FromHex(0x0F0A24u),
            fogAndSkyTint: RgbColor.FromHex(0x4F46A8u),
            neonColorA: RgbColor.FromHex(0x00F5D4u),
            neonColorB: RgbColor.FromHex(0xFF1E8Au),
            neonColorC: RgbColor.FromHex(0xFFB81Cu));

        public static BiomeVisualProfile GetRegionProfile(BiomePaletteRegionId region) => region switch
        {
            BiomePaletteRegionId.EmeraldSunForest => EmeraldSunForestProfile,
            BiomePaletteRegionId.GoldenTwilightPlateau => GoldenTwilightPlateauProfile,
            BiomePaletteRegionId.SapphireLagoonCoast => SapphireLagoonCoastProfile,
            BiomePaletteRegionId.CrimsonAmethystCanyon => CrimsonAmethystCanyonProfile,
            BiomePaletteRegionId.SunlitGlacialPeaks => SunlitGlacialPeaksProfile,
            BiomePaletteRegionId.ObsidianVoidCoast => ObsidianVoidCoastProfile,
            _ => EmeraldSunForestProfile
        };

        public static string GetRegionDisplayNameTurkish(BiomePaletteRegionId region) => region switch
        {
            BiomePaletteRegionId.EmeraldSunForest => "Zümrüt Güneşi Ormanı",
            BiomePaletteRegionId.GoldenTwilightPlateau => "Altın ve Alacakaranlık Yaylası",
            BiomePaletteRegionId.SapphireLagoonCoast => "Safir Lagün ve Prusya Kıyıları",
            BiomePaletteRegionId.CrimsonAmethystCanyon => "Kızıl Kor ve Ametist Kanyonları",
            BiomePaletteRegionId.SunlitGlacialPeaks => "Güneşli Buzul Zirveleri",
            BiomePaletteRegionId.ObsidianVoidCoast => "Obsidyen Hiçlik Kıyısı",
            _ => "Zümrüt Güneşi Ormanı"
        };

        /// <summary>
        /// Mevcut 12 biyomu (0..11) koruyarak yeni eklenen 2 bolgeyle (12..13) birlikte tum 14 biyomu
        /// 6 kanonik sanat bolgesine esler.
        /// </summary>
        public static BiomePaletteRegionId MapBiomeIdToRegion(int biomeId) => biomeId switch
        {
            0 or 1 or 3 => BiomePaletteRegionId.EmeraldSunForest,
            12 or 5 => BiomePaletteRegionId.GoldenTwilightPlateau,
            13 or 2 or 10 => BiomePaletteRegionId.SapphireLagoonCoast,
            6 or 8 or 9 => BiomePaletteRegionId.CrimsonAmethystCanyon,
            4 => BiomePaletteRegionId.SunlitGlacialPeaks,
            11 or 7 => BiomePaletteRegionId.ObsidianVoidCoast,
            _ => BiomePaletteRegionId.EmeraldSunForest
        };

        public static BiomeVisualProfile GetProfileForBiomeId(int biomeId)
        {
            return GetRegionProfile(MapBiomeIdToRegion(biomeId));
        }

        /// <summary>
        /// Deterministik 2B Simplex-tarzi gradyan gurultusu ile biyom sinirlarinda 40-80m'lik organik parmak
        /// gecisleri icin dunya uzayi ofseti uretir (0 B GC).
        /// </summary>
        public static void SampleNoisyBiomeWarp2D(float worldX, float worldZ, float warpAmplitudeMeters, out float offsetX, out float offsetZ)
        {
            float freq1 = 0.023f;
            float freq2 = 0.051f;
            float n1x = MathF.Sin(worldX * freq1 + MathF.Cos(worldZ * (freq1 * 1.17f)) * 1.4f);
            float n1z = MathF.Cos(worldZ * freq1 - MathF.Sin(worldX * (freq1 * 1.13f)) * 1.4f);
            float n2x = MathF.Sin((worldX + worldZ) * freq2 * 0.71f + 1.3f);
            float n2z = MathF.Cos((worldX - worldZ) * freq2 * 0.71f - 0.9f);

            offsetX = (n1x * 0.68f + n2x * 0.32f) * warpAmplitudeMeters;
            offsetZ = (n1z * 0.68f + n2z * 0.32f) * warpAmplitudeMeters;
        }

        /// <summary>
        /// PT-10: Biyom sinirlarinda 40-80m gurultulu yumusak gecis (Biome Blending) uygular (0 B GC).
        /// 5-noktali organik gurultu-carpitmali cekirdek ile zemin, kaya, renkli golge, sis ve neon ciftini harmanlar.
        /// </summary>
        public static BiomeVisualProfile SampleBlendedBiomeVisualProfile(
            float worldX,
            float worldZ,
            float blendWidthMeters = DefaultBiomeBlendWidthMeters)
        {
            float clampedWidth = Math.Clamp(
                float.IsFinite(blendWidthMeters) ? blendWidthMeters : DefaultBiomeBlendWidthMeters,
                MinBiomeBlendWidthMeters,
                MaxBiomeBlendWidthMeters);

            float radius = clampedWidth * 0.5f; // 60m genislik -> 30m ornekleme yaricapi
            SampleNoisyBiomeWarp2D(worldX, worldZ, radius * 0.45f, out float warpX, out float warpZ);
            float cx = worldX + warpX;
            float cz = worldZ + warpZ;

            BiomeVisualProfile pCenter = GetProfileForBiomeId(StratocraftFloatingIslandWorldGen.GetBiomeId(cx, cz));
            BiomeVisualProfile pNorth = GetProfileForBiomeId(StratocraftFloatingIslandWorldGen.GetBiomeId(cx, cz + radius));
            BiomeVisualProfile pSouth = GetProfileForBiomeId(StratocraftFloatingIslandWorldGen.GetBiomeId(cx, cz - radius));
            BiomeVisualProfile pEast = GetProfileForBiomeId(StratocraftFloatingIslandWorldGen.GetBiomeId(cx + radius, cz));
            BiomeVisualProfile pWest = GetProfileForBiomeId(StratocraftFloatingIslandWorldGen.GetBiomeId(cx - radius, cz));

            // Eger tum 5 ornek ayni bolgedeyse sifir ek islemle dogrudan don
            if (pCenter.RegionId == pNorth.RegionId &&
                pCenter.RegionId == pSouth.RegionId &&
                pCenter.RegionId == pEast.RegionId &&
                pCenter.RegionId == pWest.RegionId)
            {
                return pCenter;
            }

            // Agirliklar: merkez %36, 4 ana yon her biri %16 -> toplam %100 puruzsuz 40-80m harmanlama
            const float wC = 0.36f;
            const float wN = 0.16f;

            static RgbColor Blend5(RgbColor c, RgbColor n, RgbColor s, RgbColor e, RgbColor w)
            {
                return new RgbColor(
                    c.R * wC + (n.R + s.R + e.R + w.R) * wN,
                    c.G * wC + (n.G + s.G + e.G + w.G) * wN,
                    c.B * wC + (n.B + s.B + e.B + w.B) * wN);
            }

            return new BiomeVisualProfile(
                pCenter.RegionId,
                Blend5(pCenter.GrassPrimary, pNorth.GrassPrimary, pSouth.GrassPrimary, pEast.GrassPrimary, pWest.GrassPrimary),
                Blend5(pCenter.GrassSecondary, pNorth.GrassSecondary, pSouth.GrassSecondary, pEast.GrassSecondary, pWest.GrassSecondary),
                Blend5(pCenter.CliffStoneColor, pNorth.CliffStoneColor, pSouth.CliffStoneColor, pEast.CliffStoneColor, pWest.CliffStoneColor),
                Blend5(pCenter.ColoredShadowTint, pNorth.ColoredShadowTint, pSouth.ColoredShadowTint, pEast.ColoredShadowTint, pWest.ColoredShadowTint),
                Blend5(pCenter.FogAndSkyTint, pNorth.FogAndSkyTint, pSouth.FogAndSkyTint, pEast.FogAndSkyTint, pWest.FogAndSkyTint),
                Blend5(pCenter.NeonColorA, pNorth.NeonColorA, pSouth.NeonColorA, pEast.NeonColorA, pWest.NeonColorA),
                Blend5(pCenter.NeonColorB, pNorth.NeonColorB, pSouth.NeonColorB, pEast.NeonColorB, pWest.NeonColorB),
                Blend5(pCenter.NeonColorC, pNorth.NeonColorC, pSouth.NeonColorC, pEast.NeonColorC, pWest.NeonColorC));
        }

#if UNITY_5_3_OR_NEWER
        private static readonly int s_biomeGrassPrimaryId = Shader.PropertyToID("_StratoBiomeGrassPrimary");
        private static readonly int s_biomeGrassSecondaryId = Shader.PropertyToID("_StratoBiomeGrassSecondary");
        private static readonly int s_biomeCliffStoneId = Shader.PropertyToID("_StratoBiomeCliffStone");
        private static readonly int s_biomeShadowTintId = Shader.PropertyToID("_StratoBiomeShadowTint");
        private static readonly int s_biomeFogTintId = Shader.PropertyToID("_StratoBiomeFogTint");
        private static readonly int s_biomeNeonAId = Shader.PropertyToID("_StratoBiomeNeonA");
        private static readonly int s_biomeNeonBId = Shader.PropertyToID("_StratoBiomeNeonB");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetGlobalBiomePaletteUniforms()
        {
            Shader.SetGlobalVector(s_biomeGrassPrimaryId, Vector4.zero);
            Shader.SetGlobalVector(s_biomeGrassSecondaryId, Vector4.zero);
            Shader.SetGlobalVector(s_biomeCliffStoneId, Vector4.zero);
            Shader.SetGlobalVector(s_biomeShadowTintId, Vector4.zero);
            Shader.SetGlobalVector(s_biomeFogTintId, Vector4.zero);
            Shader.SetGlobalVector(s_biomeNeonAId, Vector4.zero);
            Shader.SetGlobalVector(s_biomeNeonBId, Vector4.zero);
        }

        public static void ApplyGlobalBiomePaletteUniforms(in BiomeVisualProfile profile, float blendActive01 = 1.0f)
        {
            float active = Mathf.Clamp01(blendActive01);
            Shader.SetGlobalVector(s_biomeGrassPrimaryId, profile.GrassPrimary.ToUnityVector4(active));
            Shader.SetGlobalVector(s_biomeGrassSecondaryId, profile.GrassSecondary.ToUnityVector4(active));
            Shader.SetGlobalVector(s_biomeCliffStoneId, profile.CliffStoneColor.ToUnityVector4(active));
            Shader.SetGlobalVector(s_biomeShadowTintId, profile.ColoredShadowTint.ToUnityVector4(active));
            Shader.SetGlobalVector(s_biomeFogTintId, profile.FogAndSkyTint.ToUnityVector4(active));
            Shader.SetGlobalVector(s_biomeNeonAId, profile.NeonColorA.ToUnityVector4(active));
            Shader.SetGlobalVector(s_biomeNeonBId, profile.NeonColorB.ToUnityVector4(active));
        }
#endif
    }
}
