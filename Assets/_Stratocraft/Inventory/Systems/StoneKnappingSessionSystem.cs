#nullable enable
using System;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Yerdeki kaba tasa cakmaktasi ile cizilebilecek 4 temel tas alet basligi kalibi.
    /// </summary>
    public enum KnappingTemplateId : byte
    {
        PickaxeHead = 0, // Kavisli cift uclu tas kazma basi
        AxeHead = 1,     // Genis agizli kama tas balta basi
        KnifeBlade = 2,  // Ince uzun keskin tas bicak agzi
        HammerHead = 3   // Kalin kut cevher kirma ve dovme cekic basi
    }

    /// <summary>
    /// Aktif bir 8x8 (64 hucreli) fiziksel tas yontma oturumunun anlik durumu.
    /// </summary>
    public struct StoneKnappingState
    {
        public KnappingTemplateId Template;
        public ulong CurrentMask64;
        public ulong TemplateMask64;
        public int StrikeCount;
        public bool IsShattered;
        public bool CanFinishHarvest;
        public float Quality01;
        public float ExcessRemainingRatio;
        public float CorePreservedRatio;

        public byte QualityPct => (byte)Math.Clamp((int)MathF.Round(Quality01 * 100f), 0, 100);
        public int RemainingExcessCells => StoneKnappingSessionSystem.PopCount64(CurrentMask64 & ~TemplateMask64);
        public int MissingCoreCells => StoneKnappingSessionSystem.PopCount64(TemplateMask64 & ~CurrentMask64);
    }

    /// <summary>
    /// Faz 2.2 (Asama 2.1): 8x8 bit-maskeli fiziksel tas yontma motoru.
    /// Hayalet kalip disindaki fazlaliklar kirildikca baslik sekillenir; cizgi icine vurulursa tas yok olmaz
    /// (cekirdek %50 ustunde kaldigi surece), ancak uretilecek aletin kalitesi (%35..%100) duser.
    /// </summary>
    public static class StoneKnappingSessionSystem
    {
        public const ulong FullSlabMask64 = 0xFFFF_FFFF_FFFF_FFFFUL;

        // Satir 0 (z=0 alt kenar) .. Satir 7 (z=7 ust kenar), her satir 8 bit (bit 0: x=0 .. bit 7: x=7)
        // 1) PickaxeHead: Kavisli cift uclu kazma basligi (26 cekirdek hucre, 38 dis fazlalik)
        private static readonly ulong PickaxeMask64 = PackRows(
            0b00000000,
            0b01000010,
            0b01100110,
            0b01111110,
            0b00111100,
            0b00111100,
            0b00011000,
            0b00000000);

        // 2) AxeHead: Genis kesici agizli kama balta basligi (30 cekirdek hucre, 34 dis fazlalik)
        private static readonly ulong AxeMask64 = PackRows(
            0b00000000,
            0b00111100,
            0b01111110,
            0b01111110,
            0b00111100,
            0b00111100,
            0b00011000,
            0b00000000);

        // 3) KnifeBlade: Ince uzun sivri cakmaktasi bicak agzi (20 cekirdek hucre, 44 dis fazlalik)
        private static readonly ulong KnifeMask64 = PackRows(
            0b00000000,
            0b00011000,
            0b00011100,
            0b00011100,
            0b00011100,
            0b00011100,
            0b00011000,
            0b00001000);

        // 4) HammerHead: Agir kut cevher kirma ve demir dovme cekic basligi (36 cekirdek hucre, 28 dis fazlalik)
        private static readonly ulong HammerMask64 = PackRows(
            0b00000000,
            0b00111100,
            0b01111110,
            0b01111110,
            0b01111110,
            0b01111110,
            0b00111100,
            0b00000000);

        private static ulong PackRows(byte r0, byte r1, byte r2, byte r3, byte r4, byte r5, byte r6, byte r7)
        {
            return ((ulong)r0)
                 | ((ulong)r1 << 8)
                 | ((ulong)r2 << 16)
                 | ((ulong)r3 << 24)
                 | ((ulong)r4 << 32)
                 | ((ulong)r5 << 40)
                 | ((ulong)r6 << 48)
                 | ((ulong)r7 << 56);
        }

        public static ulong GetTemplateMask(KnappingTemplateId template) => template switch
        {
            KnappingTemplateId.PickaxeHead => PickaxeMask64,
            KnappingTemplateId.AxeHead => AxeMask64,
            KnappingTemplateId.KnifeBlade => KnifeMask64,
            KnappingTemplateId.HammerHead => HammerMask64,
            _ => PickaxeMask64
        };

        public static string GetTemplateDisplayName(KnappingTemplateId template) => template switch
        {
            KnappingTemplateId.PickaxeHead => "Taş Kazma Başı",
            KnappingTemplateId.AxeHead => "Taş Balta Başı",
            KnappingTemplateId.KnifeBlade => "Keskin Taş Bıçak Ağzı",
            KnappingTemplateId.HammerHead => "Ağır Taş Çekiç Başı",
            _ => "Taş Başlık"
        };

        public static StoneKnappingState BeginSession(KnappingTemplateId template, ulong initialMask64 = FullSlabMask64)
        {
            ulong safeStart = initialMask64 == 0UL ? FullSlabMask64 : initialMask64;
            ulong tpl = GetTemplateMask(template);
            var state = new StoneKnappingState
            {
                Template = template,
                CurrentMask64 = safeStart,
                TemplateMask64 = tpl,
                StrikeCount = 0,
                IsShattered = false
            };
            RefreshMetrics(ref state);
            return state;
        }

        public static ulong CellBit(int cx, int cz) =>
            ((uint)cx < 8u && (uint)cz < 8u) ? (1UL << (cz * 8 + cx)) : 0UL;

        public static bool IsCellPresent(ulong mask, int cx, int cz)
        {
            if ((uint)cx >= 8u || (uint)cz >= 8u) return false;
            return (mask & (1UL << (cz * 8 + cx))) != 0UL;
        }

        public static bool IsCellExposedPerimeter(ulong mask, int cx, int cz)
        {
            if (!IsCellPresent(mask, cx, cz)) return false;
            if (cx == 0 || cx == 7 || cz == 0 || cz == 7) return true;
            return !IsCellPresent(mask, cx - 1, cz) ||
                   !IsCellPresent(mask, cx + 1, cz) ||
                   !IsCellPresent(mask, cx, cz - 1) ||
                   !IsCellPresent(mask, cx, cz + 1);
        }

        /// <summary>
        /// (cellX, cellZ) [0..7] noktasina cakmaktasi vurusu uygular.
        /// Dis kenardaki fazlalik hucrelerine vuruldugunda komsu 1-2 dis kenar fazlalik hucresi daha dogal yonga olarak kopar
        /// (asla hayalet cizgi icine tasmaz!). Cizgi icine vurulursa yalnizca vurulan tek hucre kirilir ve kalite duser.
        /// </summary>
        public static bool ApplyFlintStrike(ref StoneKnappingState state, int cellX, int cellZ, out ulong newlyChippedBits)
        {
            return ApplyFlintStrike(ref state, cellX, cellZ, out newlyChippedBits, out _, out _);
        }

        public static bool ApplyFlintStrike(
            ref StoneKnappingState state,
            int cellX,
            int cellZ,
            out ulong newlyChippedBits,
            out bool hitCoreCell,
            out bool shattered)
        {
            newlyChippedBits = 0UL;
            hitCoreCell = false;
            shattered = false;
            if (state.IsShattered || (uint)cellX >= 8u || (uint)cellZ >= 8u) return false;

            int bitIdx = cellZ * 8 + cellX;
            ulong targetBit = 1UL << bitIdx;
            if ((state.CurrentMask64 & targetBit) == 0UL) return false;

            bool isCoreTarget = (state.TemplateMask64 & targetBit) != 0UL;
            hitCoreCell = isCoreTarget;

            // Eger oyuncu en dis kenar yerine 1 icerideki bir dis fazlalik hucresine tikladiysa ve o hucre henuz acik kenar degilse,
            // en yakin dis kenardaki fazlalik hucresinden iceriye dogru kir ki dogal tas yontma hissi bozulmasin!
            if (!isCoreTarget && !IsCellExposedPerimeter(state.CurrentMask64, cellX, cellZ))
            {
                if (TryRedirectToNearestOuterEdgeCell(state.CurrentMask64, state.TemplateMask64, ref cellX, ref cellZ))
                {
                    bitIdx = cellZ * 8 + cellX;
                    targetBit = 1UL << bitIdx;
                }
            }

            state.StrikeCount++;
            newlyChippedBits |= targetBit;
            state.CurrentMask64 &= ~targetBit;

            if (!isCoreTarget)
            {
                // Dogal konkoidal yonga (conchoidal flake): Vurulan dis fazlalik hucresine komsu olan ve
                // YINE CIZGI DISINDA + ACIK KENARDA bulunan en fazla 2 komsu fazlalik hucresi birlikte kopar.
                // Bu sayede 34-44 dis hucre tek tek sikici tiklama yerine ~13-16 tatmin edici vurusla temizlenir
                // ama ASLA cizginin icine (TemplateMask64) tasmaz!
                ReadOnlySpan<int> dx = stackalloc int[4] { 1, -1, 0, 0 };
                ReadOnlySpan<int> dz = stackalloc int[4] { 0, 0, 1, -1 };
                int extraFlakes = 0;
                int startDir = (cellX * 3 + cellZ * 5 + state.StrikeCount) & 3;
                for (int k = 0; k < 4 && extraFlakes < 2; k++)
                {
                    int d = (startDir + k) & 3;
                    int nx = cellX + dx[d];
                    int nz = cellZ + dz[d];
                    if ((uint)nx >= 8u || (uint)nz >= 8u) continue;

                    ulong nBit = 1UL << (nz * 8 + nx);
                    if ((state.CurrentMask64 & nBit) == 0UL) continue;
                    if ((state.TemplateMask64 & nBit) != 0UL) continue; // Asla cekirdek kaliba dokunma!

                    if (IsCellExposedPerimeter(state.CurrentMask64, nx, nz))
                    {
                        state.CurrentMask64 &= ~nBit;
                        newlyChippedBits |= nBit;
                        extraFlakes++;
                    }
                }
            }

            RefreshMetrics(ref state);
            shattered = state.IsShattered;
            return true;
        }

        private static bool TryRedirectToNearestOuterEdgeCell(ulong currentMask, ulong templateMask, ref int cx, ref int cz)
        {
            int bestX = -1, bestZ = -1;
            int bestDistSq = int.MaxValue;
            for (int z = 0; z < 8; z++)
            {
                for (int x = 0; x < 8; x++)
                {
                    ulong b = 1UL << (z * 8 + x);
                    if ((currentMask & b) == 0UL || (templateMask & b) != 0UL) continue;
                    if (!IsCellExposedPerimeter(currentMask, x, z)) continue;
                    int dSq = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestX = x;
                        bestZ = z;
                    }
                }
            }

            if (bestX >= 0)
            {
                cx = bestX;
                cz = bestZ;
                return true;
            }
            return false;
        }

        public static void RefreshMetrics(ref StoneKnappingState state)
        {
            state.Quality01 = EvaluateQuality01(
                state.CurrentMask64,
                state.TemplateMask64,
                out float excessRatio,
                out float coreRatio);
            state.ExcessRemainingRatio = excessRatio;
            state.CorePreservedRatio = coreRatio;

            if (coreRatio < 0.50f)
            {
                state.IsShattered = true;
                state.CanFinishHarvest = false;
                state.Quality01 = 0f;
                return;
            }

            state.CanFinishHarvest = !state.IsShattered && excessRatio <= 0.22f && coreRatio >= 0.50f;
        }

        public static int PopCount64(ulong x)
        {
            x -= (x >> 1) & 0x5555_5555_5555_5555UL;
            x = (x & 0x3333_3333_3333_3333UL) + ((x >> 2) & 0x3333_3333_3333_3333UL);
            x = (x + (x >> 4)) & 0x0F0F_0F0F_0F0F_0F0FUL;
            return (int)((x * 0x0101_0101_0101_0101UL) >> 56);
        }

        public static float EvaluateQuality01(
            ulong currentMask,
            ulong templateMask,
            out float excessRemainingRatio,
            out float corePreservedRatio)
        {
            int totalCore = Math.Max(1, PopCount64(templateMask));
            int totalExcess = Math.Max(1, 64 - totalCore);

            int remainingCore = PopCount64(currentMask & templateMask);
            int remainingExcess = PopCount64(currentMask & ~templateMask);

            corePreservedRatio = remainingCore / (float)totalCore;
            excessRemainingRatio = remainingExcess / (float)totalExcess;

            if (corePreservedRatio < 0.50f)
            {
                return 0f;
            }

            // corePreservedRatio == 1.0 ve excessRemainingRatio == 0.0 -> tam 1.00 (%100 Mukemmel Usta Isi)
            // corePreservedRatio == 0.65 (cizgi icine epey girilmis) ve excess == 0.0 -> ~0.54 (%54 Zayif/Yamuk Ama Calisan Kazma!)
            // corePreservedRatio == 0.50 (kirilma sinirinda) -> 0.35 (%35 Cok Gucsuz Acemi Kazmasi)
            float coreFactor = Math.Clamp((corePreservedRatio - 0.50f) / 0.50f, 0f, 1f);
            float excessPenalty = Math.Clamp(excessRemainingRatio * 0.80f, 0f, 0.55f);
            float rawQuality = 0.35f + 0.65f * MathF.Pow(coreFactor, 0.90f) * (1.0f - excessPenalty);
            return Math.Clamp(rawQuality, 0.35f, 1.00f);
        }
    }
}
