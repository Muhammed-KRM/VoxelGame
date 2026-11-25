#nullable enable
using System;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.RigidTimber
{
    public enum TimberInteriorCraftKind : byte
    {
        WoodPlanks4 = 0,
        HingedWoodDoor = 1,
        WindowShutter = 2,
        BedFrame = 3,
        StorageChest = 4,
        TableAndBench = 5,
        WallTorchSconce = 6,
        WeaponRack = 7,
        WoodenPulleyWheel = 8
    }

    public readonly struct TimberInteriorRecipe
    {
        public readonly TimberInteriorCraftKind Kind;
        public readonly string DisplayName;
        public readonly int RequiredOakLogs;
        public readonly int RequiredPlanks;
        public readonly int RequiredSticks;
        public readonly int RequiredFiber;
        public readonly int RequiredAxeStrikes;

        public TimberInteriorRecipe(
            TimberInteriorCraftKind kind,
            string displayName,
            int oakLogs,
            int planks,
            int sticks,
            int fiber,
            int strikes)
        {
            Kind = kind;
            DisplayName = displayName;
            RequiredOakLogs = oakLogs;
            RequiredPlanks = planks;
            RequiredSticks = sticks;
            RequiredFiber = fiber;
            RequiredAxeStrikes = strikes;
        }
    }

    /// <summary>
    /// Asama 2: Kütük ev içi yalıtım (Chinking), kapalı oda sızdırmazlığı (SealFactor),
    /// şömine/baca ısınması & duman/O2 kontrolü ve Bıçkı Sehpası (Sawhorse) basit marangozluk tarifleri.
    /// </summary>
    public struct CabinInteriorThermalState
    {
        public int TotalWallLogs;
        public int ChinkedWallLogs;
        public int TotalOpenings;
        public int ClosedDoorsOrShutters;
        public byte HasRoofOverhead;
        public byte HasLitFireplace;
        public byte HasRoofChimneyFlue;
        public float SealFactor01;          // 0.0 .. 1.0 (>= 0.70 -> EnclosedRoom!)
        public float IndoorTempDeltaC;      // Disaridan kac derece daha sicak
        public byte SmokeHazardWarning;     // 1 ise: kapali odada bacasiz ates -> CO/O2 uyarisi!
        public bool IsEnclosedWarmRoom => HasRoofOverhead != 0 && SealFactor01 >= 0.70f;
    }

    public static class TimberCabinInteriorSystem
    {
        public static readonly TimberInteriorRecipe[] SawhorseRecipes =
        {
            new(TimberInteriorCraftKind.WoodPlanks4,       "4x Ahşap Kalas (Biçme)",      oakLogs: 1, planks: 0, sticks: 0, fiber: 0, strikes: 2),
            new(TimberInteriorCraftKind.HingedWoodDoor,    "Menteşeli Ahşap Kapı",        oakLogs: 0, planks: 4, sticks: 2, fiber: 2, strikes: 2),
            new(TimberInteriorCraftKind.WindowShutter,     "Ahşap Pencere Kepengi",       oakLogs: 0, planks: 2, sticks: 1, fiber: 1, strikes: 1),
            new(TimberInteriorCraftKind.BedFrame,          "Ahşap Yatak (SpawnPoint)",    oakLogs: 0, planks: 4, sticks: 2, fiber: 4, strikes: 2),
            new(TimberInteriorCraftKind.StorageChest,      "Ahşap Sandık (16 Yuva)",      oakLogs: 0, planks: 6, sticks: 0, fiber: 2, strikes: 2),
            new(TimberInteriorCraftKind.TableAndBench,     "Ahşap Masa & Bank",           oakLogs: 1, planks: 3, sticks: 2, fiber: 0, strikes: 2),
            new(TimberInteriorCraftKind.WallTorchSconce,   "Duvar Meşale Askısı",         oakLogs: 0, planks: 0, sticks: 3, fiber: 1, strikes: 1),
            new(TimberInteriorCraftKind.WeaponRack,        "Duvar Alet / Silah Rafı",     oakLogs: 0, planks: 2, sticks: 2, fiber: 0, strikes: 1),
            new(TimberInteriorCraftKind.WoodenPulleyWheel, "Ahşap Makara Çarkı (Vinç)",   oakLogs: 1, planks: 0, sticks: 2, fiber: 2, strikes: 2)
        };

        /// <summary>
        /// DOK1 AK-05 & DOK5 Bolum 3.4:
        /// - Chinking (yosun/kil derz dolgusu) yapilmamissa kütük ev yalnizca yagmuru keser, ruzgar sizdirir (SealFactor < 0.70).
        /// - Chinking + kapali kapi/kepenk sonrasi SealFactor >= 0.70 olur ve sicak kapali oda (EnclosedRoom) aktiflesir.
        /// - Somine yakildiginda baca varsa +22 C isitir ve duman = 0; baca yoksa kapali odada duman/O2 uyarisi verir!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EvaluateCabinThermalAndSmoke(ref CabinInteriorThermalState state)
        {
            if (state.HasRoofOverhead == 0)
            {
                state.SealFactor01 = 0f;
                state.IndoorTempDeltaC = state.HasLitFireplace != 0 ? 4.0f : 0f;
                state.SmokeHazardWarning = 0;
                return;
            }

            float chinkRatio = state.TotalWallLogs > 0
                ? Math.Clamp((float)state.ChinkedWallLogs / state.TotalWallLogs, 0f, 1f)
                : 0f;
            float openingSeal = state.TotalOpenings > 0
                ? Math.Clamp((float)state.ClosedDoorsOrShutters / state.TotalOpenings, 0f, 1f)
                : 1f;

            // DOK1 AK-05: Chinking %0 ise SealFactor maks 0.25 (kapali sicak oda sayilmaz!)
            state.SealFactor01 = Math.Clamp(chinkRatio * 0.75f + openingSeal * 0.25f, 0f, 1f);

            if (state.HasLitFireplace != 0)
            {
                if (state.HasRoofChimneyFlue != 0)
                {
                    state.SmokeHazardWarning = 0;
                    state.IndoorTempDeltaC = 6.0f + 18.0f * state.SealFactor01; // Tam yalitimsiz +6 C, tam yalitimli +24 C!
                }
                else
                {
                    // Bacasiz ates: kapaliysa duman ve CO/O2 tehlikesi!
                    state.SmokeHazardWarning = state.SealFactor01 >= 0.55f ? (byte)1 : (byte)0;
                    state.IndoorTempDeltaC = 5.0f + 10.0f * state.SealFactor01;
                }
            }
            else
            {
                state.SmokeHazardWarning = 0;
                state.IndoorTempDeltaC = 4.5f * state.SealFactor01; // Ruzgar kesme (Wind Chill korumasi)
            }
        }
    }
}
