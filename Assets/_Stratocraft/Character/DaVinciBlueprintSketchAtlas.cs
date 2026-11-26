#if UNITY_5_3_OR_NEWER
#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Stratocraft.Character
{
    public enum DaVinciSketchKind : byte
    {
        GenericFrame = 0,
        LogArchCart = 1,
        Scaffolding = 2,
        ParbuckleRamp = 3,
        GinPoleCrane = 4,
        SawhorseBench = 5,
        StoneFireplace = 6,
        LogWall = 7,
        PillarBeam = 8,
        Stairs = 9,
        DoorWindow = 10,
        RoofTruss = 11,
        StoneArch = 12,
        KilnFurnace = 13
    }

    /// <summary>
    /// Leonardo da Vinci Codex Atlanticus tarzi sicak parsomen & sepya murekkepli prosedurel eskiz ikonlari.
    /// 0 disk dokusu, toplam VRAM < 0.08 MB, tek seferde uretilip cache'lenir (0 B/kare GC).
    /// </summary>
    public static class DaVinciBlueprintSketchAtlas
    {
        private const int Size = 56;
        private static readonly Dictionary<DaVinciSketchKind, Texture2D> Cache = new(16);
        private static Texture2D? _parchmentBg;
        private static Texture2D? _cardBg;
        private static Texture2D? _cardHoverBg;
        private static Texture2D? _tabActiveBg;

        private static readonly Color32 PaperLight = new(235, 221, 188, 250);
        private static readonly Color32 PaperCard = new(244, 234, 208, 248);
        private static readonly Color32 PaperHover = new(252, 244, 220, 252);
        private static readonly Color32 InkDark = new(54, 34, 20, 255);
        private static readonly Color32 InkMid = new(112, 78, 48, 255);
        private static readonly Color32 InkGrid = new(202, 184, 148, 255);
        private static readonly Color32 InkRed = new(146, 48, 34, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            _parchmentBg = null;
            _cardBg = null;
            _cardHoverBg = null;
            _tabActiveBg = null;
        }

        public static Texture2D GetParchmentPanelBg() => _parchmentBg != null ? _parchmentBg : (_parchmentBg = BuildBorderedTex(PaperLight, InkMid, 2));
        public static Texture2D GetParchmentCardBg() => _cardBg != null ? _cardBg : (_cardBg = BuildBorderedTex(PaperCard, InkMid, 1));
        public static Texture2D GetParchmentCardHoverBg() => _cardHoverBg != null ? _cardHoverBg : (_cardHoverBg = BuildBorderedTex(PaperHover, InkRed, 2));
        public static Texture2D GetParchmentTabActiveBg() => _tabActiveBg != null ? _tabActiveBg : (_tabActiveBg = BuildBorderedTex(new Color32(138, 56, 36, 245), InkDark, 1));

        public static DaVinciSketchKind InferSketchFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return DaVinciSketchKind.GenericFrame;
            string t = title.ToLowerInvariant();
            if (t.Contains("araba")) return DaVinciSketchKind.LogArchCart;
            if (t.Contains("iskele")) return DaVinciSketchKind.Scaffolding;
            if (t.Contains("rampa") || t.Contains("parbuckle")) return DaVinciSketchKind.ParbuckleRamp;
            if (t.Contains("vinç") || t.Contains("gin pole") || t.Contains("palanga")) return DaVinciSketchKind.GinPoleCrane;
            if (t.Contains("sehpa") || t.Contains("bıçkı") || t.Contains("tezgâh") || t.Contains("tezgah")) return DaVinciSketchKind.SawhorseBench;
            if (t.Contains("şömine") || t.Contains("baca") || t.Contains("fırın") || t.Contains("oca")) return DaVinciSketchKind.StoneFireplace;
            if (t.Contains("merdiven")) return DaVinciSketchKind.Stairs;
            if (t.Contains("kapı") || t.Contains("pencere") || t.Contains("kepenk")) return DaVinciSketchKind.DoorWindow;
            if (t.Contains("çatı") || t.Contains("beşik") || t.Contains("kubbe")) return DaVinciSketchKind.RoofTruss;
            if (t.Contains("kemer") || t.Contains("taş") || t.Contains("tuğla")) return DaVinciSketchKind.StoneArch;
            if (t.Contains("kütük") || t.Contains("duvar")) return DaVinciSketchKind.LogWall;
            if (t.Contains("kiriş") || t.Contains("dikme") || t.Contains("payanda") || t.Contains("kolon")) return DaVinciSketchKind.PillarBeam;
            return DaVinciSketchKind.GenericFrame;
        }

        public static Texture2D GetSketchTexture(DaVinciSketchKind kind)
        {
            if (Cache.TryGetValue(kind, out Texture2D? tex) && tex != null) return tex;

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool border = x == 0 || y == 0 || x == Size - 1 || y == Size - 1;
                bool grid = (x % 14 == 0) || (y % 14 == 0);
                pixels[y * Size + x] = border ? InkMid : (grid ? InkGrid : PaperCard);
            }

            // Eskiz cizimleri (sepya murekkep + kirmizi olcu cizgileri)
            switch (kind)
            {
                case DaVinciSketchKind.LogArchCart:
                    DrawCircle(pixels, 18, 16, 10, InkDark);
                    DrawCircle(pixels, 18, 16, 3, InkMid);
                    DrawLine(pixels, 18, 16, 18, 42, InkDark);
                    DrawLine(pixels, 18, 42, 48, 30, InkDark); // Cekme kolu
                    DrawLine(pixels, 8, 28, 36, 28, InkRed);   // Asili tam boy kutuk
                    DrawLine(pixels, 8, 32, 36, 32, InkRed);
                    break;
                case DaVinciSketchKind.Scaffolding:
                    DrawLine(pixels, 10, 8, 10, 48, InkDark);
                    DrawLine(pixels, 46, 8, 46, 48, InkDark);
                    DrawLine(pixels, 8, 28, 48, 28, InkDark);
                    DrawLine(pixels, 8, 44, 48, 44, InkDark);
                    DrawLine(pixels, 10, 8, 46, 28, InkMid);
                    DrawLine(pixels, 46, 8, 10, 28, InkMid);
                    DrawLine(pixels, 18, 8, 18, 44, InkRed); // Merdiven
                    break;
                case DaVinciSketchKind.ParbuckleRamp:
                    DrawLine(pixels, 8, 10, 46, 40, InkDark);
                    DrawLine(pixels, 12, 8, 50, 38, InkDark);
                    DrawCircle(pixels, 28, 28, 7, InkRed);   // Yuvarlanan kutuk
                    DrawLine(pixels, 28, 35, 48, 44, InkMid); // Halat
                    break;
                case DaVinciSketchKind.GinPoleCrane:
                    DrawLine(pixels, 12, 8, 28, 48, InkDark);
                    DrawLine(pixels, 40, 8, 28, 48, InkDark);
                    DrawCircle(pixels, 28, 44, 4, InkRed);   // Makara
                    DrawLine(pixels, 28, 40, 28, 16, InkMid); // Halat
                    DrawRect(pixels, 20, 10, 36, 16, InkDark); // Kiris
                    break;
                case DaVinciSketchKind.SawhorseBench:
                    DrawRect(pixels, 8, 30, 48, 36, InkDark);
                    DrawLine(pixels, 14, 30, 10, 10, InkDark);
                    DrawLine(pixels, 42, 30, 46, 10, InkDark);
                    DrawLine(pixels, 14, 38, 42, 38, InkRed);
                    break;
                case DaVinciSketchKind.StoneFireplace:
                    DrawRect(pixels, 10, 8, 46, 26, InkDark);
                    DrawRect(pixels, 20, 8, 36, 20, InkRed); // Ocak agzi
                    DrawRect(pixels, 21, 26, 35, 48, InkDark); // Baca
                    DrawLine(pixels, 28, 48, 32, 53, InkMid);
                    break;
                case DaVinciSketchKind.LogWall:
                    for (int row = 10; row <= 42; row += 8)
                    {
                        DrawRect(pixels, 8, row, 48, row + 6, InkDark);
                        DrawCircle(pixels, 12, row + 3, 3, InkRed);
                        DrawCircle(pixels, 44, row + 3, 3, InkRed);
                    }
                    break;
                case DaVinciSketchKind.PillarBeam:
                    DrawRect(pixels, 10, 8, 16, 46, InkDark);
                    DrawRect(pixels, 40, 8, 46, 46, InkDark);
                    DrawRect(pixels, 8, 40, 48, 46, InkDark);
                    DrawLine(pixels, 16, 28, 26, 40, InkRed); // 45 deg payanda
                    DrawLine(pixels, 40, 28, 30, 40, InkRed);
                    break;
                case DaVinciSketchKind.Stairs:
                    for (int s = 0; s < 5; s++)
                    {
                        int sx = 8 + s * 8;
                        int sy = 10 + s * 7;
                        DrawLine(pixels, sx, sy, sx + 8, sy, InkDark);
                        DrawLine(pixels, sx + 8, sy, sx + 8, sy + 7, InkDark);
                    }
                    DrawLine(pixels, 8, 8, 48, 44, InkRed);
                    break;
                case DaVinciSketchKind.DoorWindow:
                    DrawRect(pixels, 14, 8, 42, 48, InkDark);
                    DrawLine(pixels, 28, 8, 28, 48, InkMid);
                    DrawLine(pixels, 14, 28, 42, 28, InkMid);
                    DrawCircle(pixels, 36, 24, 2, InkRed);
                    break;
                case DaVinciSketchKind.RoofTruss:
                    DrawLine(pixels, 6, 18, 28, 46, InkDark);
                    DrawLine(pixels, 50, 18, 28, 46, InkDark);
                    DrawLine(pixels, 8, 18, 48, 18, InkRed); // Gergi kirisi
                    DrawLine(pixels, 28, 18, 28, 46, InkMid);
                    break;
                case DaVinciSketchKind.StoneArch:
                    DrawRect(pixels, 8, 8, 16, 30, InkDark);
                    DrawRect(pixels, 40, 8, 48, 30, InkDark);
                    DrawCircle(pixels, 28, 28, 16, InkDark);
                    DrawRect(pixels, 25, 41, 31, 49, InkRed); // Keystone
                    break;
                default:
                    DrawRect(pixels, 10, 10, 46, 46, InkDark);
                    DrawLine(pixels, 10, 10, 46, 46, InkMid);
                    DrawLine(pixels, 46, 10, 10, 46, InkMid);
                    DrawCircle(pixels, 28, 28, 12, InkRed);
                    break;
            }

            var t2d = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t2d.SetPixels32(pixels);
            t2d.Apply(false, true);
            Cache[kind] = t2d;
            return t2d;
        }

        private static Texture2D BuildBorderedTex(Color32 fill, Color32 border, int bWidth)
        {
            const int s = 16;
            var pix = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool isB = x < bWidth || y < bWidth || x >= s - bWidth || y >= s - bWidth;
                pix[y * s + x] = isB ? border : fill;
            }
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t.SetPixels32(pix);
            t.Apply(false, true);
            return t;
        }

        private static void SetPix(Color32[] pix, int x, int y, Color32 col)
        {
            if ((uint)x < Size && (uint)y < Size) pix[y * Size + x] = col;
        }

        private static void DrawLine(Color32[] pix, int x0, int y0, int x1, int y1, Color32 col)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPix(pix, x0, y0, col);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static void DrawRect(Color32[] pix, int x0, int y0, int x1, int y1, Color32 col)
        {
            DrawLine(pix, x0, y0, x1, y0, col);
            DrawLine(pix, x0, y1, x1, y1, col);
            DrawLine(pix, x0, y0, x0, y1, col);
            DrawLine(pix, x1, y0, x1, y1, col);
        }

        private static void DrawCircle(Color32[] pix, int cx, int cy, int r, Color32 col)
        {
            int x = r, y = 0, err = 1 - x;
            while (x >= y)
            {
                SetPix(pix, cx + x, cy + y, col); SetPix(pix, cx + y, cy + x, col);
                SetPix(pix, cx - y, cy + x, col); SetPix(pix, cx - x, cy + y, col);
                SetPix(pix, cx - x, cy - y, col); SetPix(pix, cx - y, cy - x, col);
                SetPix(pix, cx + y, cy - x, col); SetPix(pix, cx + x, cy - y, col);
                y++;
                if (err < 0) err += 2 * y + 1;
                else { x--; err += 2 * (y - x + 1); }
            }
        }
    }
}
#endif
