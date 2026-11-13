#nullable enable
using System;
using System.Numerics;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    public enum CharcoalPitPhase : byte
    {
        Unlit = 0,
        Smoldering = 1,
        ReadyCharcoal = 2,
        RuinedAsh = 3,
        Harvested = 4
    }

    public struct CharcoalPitState
    {
        public uint StationId;
        public Vector3 Center;
        public float BaseY;
        public int WoodUnits;
        public int CoverUnits;
        public bool HasEarthCover;
        public int SparkStrikeCount;
        public float ElapsedPyrolysisSec;
        public CharcoalPitPhase Phase;

        public const int RequiredSparkStrikes = 2;
        public const float PyrolysisDurationSec = 28.0f;
    }

    public struct OreCrushingSpotState
    {
        public bool IsActive;
        public byte OreMaterialId;
        public int HammerStrikeCount;
        public PhysicalItemShapeDna SourceDna;

        public const int RequiredHammerStrikes = 3;
    }

    public enum BloomeryOreChargeForm : byte
    {
        CrushedPowder = 0,
        RoastedChunk = 1,
        RawOre = 2
    }

    public enum BloomeryOreChargeResult : byte
    {
        Charged = 0,
        UnsupportedMaterial = 1,
        MixedMetalConflict = 2,
        FurnaceOreFull = 3
    }

    public struct BloomeryFurnaceState
    {
        public uint StationId;
        public Vector3 Center;
        public float BaseY;
        public bool HasClayTuyere;
        public bool HasLeatherBellows;
        public bool HasClayCrucible;
        public bool IsFrontClayPlugIntact;
        public bool IsLit;
        public int SparkStrikeCount;
        public int WoodFuelUnits;
        public int CharcoalUnits;
        public int CrushedOreUnits;
        public byte OreMaterialId;
        public float OreSmeltSpeedMultiplier;
        public float FuelBurnTimerSec;
        public float TemperatureCelsius;
        public float BellowsAirflow01;
        public float TimeSinceLastPumpSec;
        public float SmeltProgress01;
        public float SlagTappedVisual01;
        public bool HasReadyIronBloom;
        public bool HasMoltenCopperReady;
        public bool HasTappedSlagShard;

        public const int MaxCharcoalUnits = 12;
        public const int MaxCrushedOreUnits = 8;
        public const int MaxWoodUnits = 8;
        public const int RequiredSparkStrikes = 2;
        public const byte CopperMatId = 30;
        public const byte IronMatId = 32;
        public const float NaturalDraftCapCelsius = 795.0f;
        public const float CopperMeltThresholdCelsius = 1085.0f;
        public const float IronBloomThresholdCelsius = 1230.0f;
        public const float MaxFurnaceCelsius = 1315.0f;
        public const float CrushedOreSmeltSpeedMultiplier = 1.00f;
        public const float RoastedOreSmeltSpeedMultiplier = 0.85f;
        public const float RawOreSmeltSpeedMultiplier = 0.70f;
    }

    public struct HotForgeWorkpieceState
    {
        public bool IsOccupied;
        public bool IsCompletedWroughtBar;
        public float TemperatureCelsius;
        public int CompletedStrikes;
        public PhysicalItemShapeDna ShapeDna;

        public const float InitialBloomTempCelsius = 1180.0f;
        public const float AirCoolingPerSec = 16.0f;
        public const float MinHotForgeCelsius = 720.0f;
        public const int RequiredHotStrikes = 8;
    }

    public struct WheelbarrowCargoEntry
    {
        public byte ItemKindByte;
        public byte SubId;
        public PhysicalItemShapeDna ShapeDna;
    }

    /// <summary>
    /// 36 yuvali fiziksel El Arabasi kasa tamponu (LIFO yigin, her parcanin 16-byte ShapeDna verisini korur).
    /// </summary>
    public sealed class WheelbarrowCargoBuffer
    {
        public const int MaxCapacity = 36;
        private readonly WheelbarrowCargoEntry[] _entries = new WheelbarrowCargoEntry[MaxCapacity];

        public int Count { get; private set; }
        public bool IsFull => Count >= MaxCapacity;
        public bool IsEmpty => Count <= 0;

        public WheelbarrowCargoEntry GetAt(int index)
        {
            if ((uint)index >= (uint)Count) return default;
            return _entries[index];
        }

        public bool TryPush(byte itemKindByte, byte subId, in PhysicalItemShapeDna dna)
        {
            if (Count >= MaxCapacity) return false;
            _entries[Count++] = new WheelbarrowCargoEntry
            {
                ItemKindByte = itemKindByte,
                SubId = subId,
                ShapeDna = dna
            };
            return true;
        }

        public bool TryPop(out WheelbarrowCargoEntry entry)
        {
            if (Count <= 0)
            {
                entry = default;
                return false;
            }
            Count--;
            entry = _entries[Count];
            _entries[Count] = default;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = default;
            }
            Count = 0;
        }

        public static void ComputeBedSlotLocalPose(int slotIndex, out Vector3 localOffset, out float localYawDeg)
        {
            int idx = Math.Clamp(slotIndex, 0, MaxCapacity - 1);
            int layer = idx / 18;
            int inLayer = idx % 18;
            int colX = inLayer % 3;
            int rowZ = inLayer / 3;

            uint h = PhysicalItemShapeDna.MixHash((uint)(idx + 1) * 0x9E3779B9u);
            float jx = (((h & 0xFFu) / 255f) - 0.5f) * 0.036f;
            float jz = ((((h >> 8) & 0xFFu) / 255f) - 0.5f) * 0.036f;

            float x = -0.18f + colX * 0.18f + jx;
            float z = -0.34f + (rowZ / 5.0f) * 0.68f + jz;
            float y = (layer == 0 ? 0.14f : 0.27f) + (((h >> 16) & 0xFFu) / 255f) * 0.018f;

            localOffset = new Vector3(x, y, z);
            localYawDeg = (((h >> 24) & 0xFFu) / 255f) * 360.0f;
        }
    }

    /// <summary>
    /// Faz 2.4 saf C# (.NET 10 + Unity 6) Komur Ocagi pirolizi, Cevher Kirma, Bloomery 1250 C
    /// termodinamigi ve Sicak Orste Sunger Demir dovme durum makinesi.
    /// </summary>
    public static class BloomeryThermodynamicsSystem
    {
        public static CharcoalPitState CreateCharcoalPit(
            uint stationId,
            Vector3 center,
            float baseY,
            int woodUnits,
            int coverUnits)
        {
            return new CharcoalPitState
            {
                StationId = stationId,
                Center = center,
                BaseY = baseY,
                WoodUnits = Math.Max(0, woodUnits),
                CoverUnits = Math.Max(0, coverUnits),
                HasEarthCover = coverUnits >= 4,
                SparkStrikeCount = 0,
                ElapsedPyrolysisSec = 0f,
                Phase = CharcoalPitPhase.Unlit
            };
        }

        public static bool ApplyCharcoalPitFlintStrike(ref CharcoalPitState pit, out bool ignited)
        {
            ignited = false;
            if (pit.Phase != CharcoalPitPhase.Unlit) return false;

            pit.SparkStrikeCount++;
            if (pit.SparkStrikeCount >= CharcoalPitState.RequiredSparkStrikes)
            {
                pit.SparkStrikeCount = CharcoalPitState.RequiredSparkStrikes;
                if (!pit.HasEarthCover)
                {
                    pit.Phase = CharcoalPitPhase.RuinedAsh;
                    ignited = false;
                }
                else
                {
                    pit.Phase = CharcoalPitPhase.Smoldering;
                    pit.ElapsedPyrolysisSec = 0f;
                    ignited = true;
                }
            }
            return true;
        }

        public static void StepCharcoalPit(ref CharcoalPitState pit, float dt)
        {
            if (dt <= 0f || pit.Phase != CharcoalPitPhase.Smoldering) return;
            if (!pit.HasEarthCover)
            {
                pit.Phase = CharcoalPitPhase.RuinedAsh;
                return;
            }

            pit.ElapsedPyrolysisSec += dt;
            if (pit.ElapsedPyrolysisSec >= CharcoalPitState.PyrolysisDurationSec)
            {
                pit.ElapsedPyrolysisSec = CharcoalPitState.PyrolysisDurationSec;
                pit.Phase = CharcoalPitPhase.ReadyCharcoal;
            }
        }

        public static bool TryOpenOrHarvestCharcoalPit(
            ref CharcoalPitState pit,
            out int yieldedCharcoalCount,
            out bool ruinedByEarlyOpen)
        {
            yieldedCharcoalCount = 0;
            ruinedByEarlyOpen = false;

            if (pit.Phase == CharcoalPitPhase.Smoldering)
            {
                pit.Phase = CharcoalPitPhase.RuinedAsh;
                ruinedByEarlyOpen = true;
                return true;
            }

            if (pit.Phase == CharcoalPitPhase.ReadyCharcoal)
            {
                pit.Phase = CharcoalPitPhase.Harvested;
                yieldedCharcoalCount = Math.Clamp(6 + Math.Max(0, pit.WoodUnits - 4) / 2, 6, 8);
                return true;
            }

            return false;
        }

        public static bool ApplyOreCrushHammerStrike(
            ref OreCrushingSpotState spot,
            out bool shatteredIntoPowder)
        {
            shatteredIntoPowder = false;
            if (!spot.IsActive) return false;

            spot.HammerStrikeCount++;
            if (spot.HammerStrikeCount >= OreCrushingSpotState.RequiredHammerStrikes)
            {
                spot.HammerStrikeCount = OreCrushingSpotState.RequiredHammerStrikes;
                spot.IsActive = false;
                shatteredIntoPowder = true;
            }
            return true;
        }

        public static BloomeryFurnaceState CreateBloomeryFurnace(
            uint stationId,
            Vector3 center,
            float baseY,
            bool hasTuyere,
            bool hasBellows,
            bool hasCrucible)
        {
            return new BloomeryFurnaceState
            {
                StationId = stationId,
                Center = center,
                BaseY = baseY,
                HasClayTuyere = hasTuyere,
                HasLeatherBellows = hasBellows,
                HasClayCrucible = hasCrucible,
                IsFrontClayPlugIntact = true,
                OreSmeltSpeedMultiplier = BloomeryFurnaceState.CrushedOreSmeltSpeedMultiplier,
                TemperatureCelsius = 20.0f,
                TimeSinceLastPumpSec = 10.0f
            };
        }

        public static float GetOreChargeSpeedMultiplier(BloomeryOreChargeForm form) => form switch
        {
            BloomeryOreChargeForm.CrushedPowder => BloomeryFurnaceState.CrushedOreSmeltSpeedMultiplier,
            BloomeryOreChargeForm.RoastedChunk => BloomeryFurnaceState.RoastedOreSmeltSpeedMultiplier,
            _ => BloomeryFurnaceState.RawOreSmeltSpeedMultiplier
        };

        public static bool IsSupportedBloomeryOreMaterial(byte materialId)
        {
            return materialId == BloomeryFurnaceState.CopperMatId ||
                   materialId == BloomeryFurnaceState.IronMatId;
        }

        public static BloomeryOreChargeResult TryChargeOreUnit(
            ref BloomeryFurnaceState state,
            byte oreMaterialId,
            BloomeryOreChargeForm form)
        {
            if (!IsSupportedBloomeryOreMaterial(oreMaterialId))
            {
                return BloomeryOreChargeResult.UnsupportedMaterial;
            }

            if (state.CrushedOreUnits > 0 &&
                state.OreMaterialId != 0 &&
                state.OreMaterialId != oreMaterialId)
            {
                return BloomeryOreChargeResult.MixedMetalConflict;
            }

            if (state.CrushedOreUnits >= BloomeryFurnaceState.MaxCrushedOreUnits)
            {
                return BloomeryOreChargeResult.FurnaceOreFull;
            }

            float unitMul = GetOreChargeSpeedMultiplier(form);
            if (state.CrushedOreUnits <= 0)
            {
                state.OreSmeltSpeedMultiplier = unitMul;
                state.CrushedOreUnits = 1;
            }
            else
            {
                float prevMul = state.OreSmeltSpeedMultiplier <= 0.001f
                    ? BloomeryFurnaceState.CrushedOreSmeltSpeedMultiplier
                    : state.OreSmeltSpeedMultiplier;
                state.OreSmeltSpeedMultiplier =
                    (prevMul * state.CrushedOreUnits + unitMul) / (state.CrushedOreUnits + 1);
                state.CrushedOreUnits++;
            }

            state.OreMaterialId = oreMaterialId;
            return BloomeryOreChargeResult.Charged;
        }

        public const float LightRainThresholdMmPerHour = 0.5f;
        public const float HeavyRainExtinguishThresholdMmPerHour = 6.0f;

        public static bool ExtinguishBloomery(ref BloomeryFurnaceState state)
        {
            if (!state.IsLit) return false;
            state.IsLit = false;
            state.SparkStrikeCount = 0;
            state.BellowsAirflow01 = 0f;
            return true;
        }

        public static bool ApplyBloomeryFlintStrike(ref BloomeryFurnaceState state, out bool ignited) =>
            ApplyBloomeryFlintStrike(ref state, precipMmPerHour: 0f, hasOpenSkyAbove: false, out ignited);

        public static bool ApplyBloomeryFlintStrike(
            ref BloomeryFurnaceState state,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            out bool ignited)
        {
            ignited = false;
            if (state.IsLit || (state.CharcoalUnits <= 0 && state.WoodFuelUnits <= 0)) return false;

            if (hasOpenSkyAbove && precipMmPerHour >= HeavyRainExtinguishThresholdMmPerHour)
            {
                state.SparkStrikeCount = 0;
                return false;
            }

            int requiredStrikes = (hasOpenSkyAbove && precipMmPerHour > LightRainThresholdMmPerHour)
                ? BloomeryFurnaceState.RequiredSparkStrikes + 2
                : BloomeryFurnaceState.RequiredSparkStrikes;

            state.SparkStrikeCount++;
            if (state.SparkStrikeCount >= requiredStrikes)
            {
                state.SparkStrikeCount = BloomeryFurnaceState.RequiredSparkStrikes;
                state.IsLit = true;
                state.TemperatureCelsius = MathF.Max(state.TemperatureCelsius, 320.0f);
                ignited = true;
            }
            return true;
        }

        /// <summary>
        /// Paket 4A (Faz 4A.4 / AK-8) — Bloomery izabe firininin acik gokyuzu yagmur maruziyetini ilerletir.
        /// Ustu acikken (hasOpenSkyAbove == true) hafif yagmurda isitma yavaslar ve tutusturma zorlasir;
        /// siddetli yagmurda (&gt;= 6.0 mm/sa) firin soner (IsLit = false, komur/cevher/ilerleme korunur) ve sicaklik hizla ortama duser.
        /// Ustu cati/magara altindaysa (hasOpenSkyAbove == false) saganakta bile %100 yanmaya devam eder.
        /// </summary>
        public static bool StepRainExposure(
            ref BloomeryFurnaceState state,
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds,
            float ambientTempCelsius = 20.0f)
        {
            if (!hasOpenSkyAbove || precipMmPerHour <= LightRainThresholdMmPerHour || gameDeltaSeconds <= 0f)
            {
                return false;
            }

            float dt = MathF.Max(0f, gameDeltaSeconds);
            if (precipMmPerHour >= HeavyRainExtinguishThresholdMmPerHour)
            {
                bool wasLit = ExtinguishBloomery(ref state);
                float rapidDrop = MathF.Max(320.0f * dt, (state.TemperatureCelsius - ambientTempCelsius) * 0.85f);
                state.TemperatureCelsius = MathF.Max(ambientTempCelsius, state.TemperatureCelsius - rapidDrop);
                return wasLit;
            }

            if (!state.IsLit)
            {
                if (state.SparkStrikeCount > 0 && dt >= 0.5f)
                {
                    state.SparkStrikeCount = Math.Max(0, state.SparkStrikeCount - 1);
                }
            }
            else
            {
                state.BellowsAirflow01 = MathF.Max(0f, state.BellowsAirflow01 - 0.18f * dt);
                float dampCooling = precipMmPerHour * 22.0f * dt;
                state.TemperatureCelsius = MathF.Max(
                    MathF.Max(ambientTempCelsius, 180.0f),
                    state.TemperatureCelsius - dampCooling);
            }

            return false;
        }

        public static bool StepRainExposure(
            float precipMmPerHour,
            bool hasOpenSkyAbove,
            float gameDeltaSeconds,
            ref BloomeryFurnaceState state,
            float ambientTempCelsius = 20.0f) =>
            StepRainExposure(ref state, precipMmPerHour, hasOpenSkyAbove, gameDeltaSeconds, ambientTempCelsius);

        public static bool PumpBellows(ref BloomeryFurnaceState state)
        {
            if (!state.HasClayTuyere || !state.HasLeatherBellows) return false;

            float boost;
            if (state.TimeSinceLastPumpSec >= 0.55f && state.TimeSinceLastPumpSec <= 1.25f)
            {
                boost = 0.48f;
            }
            else if (state.TimeSinceLastPumpSec < 0.30f)
            {
                boost = 0.16f;
            }
            else
            {
                boost = 0.34f;
            }

            state.BellowsAirflow01 = MathF.Min(1.0f, state.BellowsAirflow01 + boost);
            state.TimeSinceLastPumpSec = 0f;
            return true;
        }

        public static void StepBloomery(ref BloomeryFurnaceState state, float dt)
        {
            if (dt <= 0f) return;

            state.TimeSinceLastPumpSec += dt;
            state.BellowsAirflow01 = MathF.Max(0f, state.BellowsAirflow01 - 0.14f * dt);

            if (!state.IsFrontClayPlugIntact && state.IsLit)
            {
                state.BellowsAirflow01 = 0f;
            }

            float targetTemp = 20.0f;
            if (!state.IsLit || (state.CharcoalUnits <= 0 && state.WoodFuelUnits <= 0))
            {
                if (state.IsLit)
                {
                    state.IsLit = false;
                    state.SparkStrikeCount = 0;
                }
                targetTemp = 20.0f;
            }
            else if (state.CharcoalUnits <= 0 && state.WoodFuelUnits > 0)
            {
                // Odun atesi koruklense bile Bakir (1085C) / Demir (1230C) derecesine cikamaz!
                targetTemp = 735.0f + 85.0f * state.BellowsAirflow01;
            }
            else
            {
                // Odun komuru var: tuyere + koruk + pompalama olmadan ~795 C sinirinda kalir
                if (!state.HasClayTuyere || !state.HasLeatherBellows || !state.IsFrontClayPlugIntact || state.BellowsAirflow01 <= 0.0001f)
                {
                    targetTemp = BloomeryFurnaceState.NaturalDraftCapCelsius;
                }
                else
                {
                    targetTemp = MathF.Min(
                        BloomeryFurnaceState.MaxFurnaceCelsius,
                        BloomeryFurnaceState.NaturalDraftCapCelsius + 550.0f * state.BellowsAirflow01);
                }
            }

            if (state.TemperatureCelsius < targetTemp)
            {
                state.TemperatureCelsius = MathF.Min(targetTemp, state.TemperatureCelsius + 145.0f * dt);
            }
            else if (state.TemperatureCelsius > targetTemp)
            {
                state.TemperatureCelsius = MathF.Max(targetTemp, state.TemperatureCelsius - 52.0f * dt);
            }

            if (state.IsLit)
            {
                state.FuelBurnTimerSec += dt * (1.0f + 0.55f * state.BellowsAirflow01);
                while (state.FuelBurnTimerSec >= 24.0f)
                {
                    state.FuelBurnTimerSec -= 24.0f;
                    if (state.CharcoalUnits > 0)
                    {
                        state.CharcoalUnits--;
                    }
                    else if (state.WoodFuelUnits > 0)
                    {
                        state.WoodFuelUnits--;
                    }

                    if (state.CharcoalUnits <= 0 && state.WoodFuelUnits <= 0)
                    {
                        state.IsLit = false;
                        state.SparkStrikeCount = 0;
                        state.FuelBurnTimerSec = 0f;
                        break;
                    }
                }
            }

            if (state.IsLit && state.CrushedOreUnits > 0 && state.CharcoalUnits > 0)
            {
                float smeltSpeedMul = state.OreSmeltSpeedMultiplier <= 0.001f
                    ? BloomeryFurnaceState.CrushedOreSmeltSpeedMultiplier
                    : Math.Clamp(state.OreSmeltSpeedMultiplier, 0.10f, 2.00f);

                if (state.OreMaterialId == BloomeryFurnaceState.CopperMatId &&
                    state.HasClayCrucible &&
                    !state.HasMoltenCopperReady)
                {
                    if (state.TemperatureCelsius >= BloomeryFurnaceState.CopperMeltThresholdCelsius)
                    {
                        state.SmeltProgress01 += (dt / 18.0f) * smeltSpeedMul;
                        if (state.SmeltProgress01 >= 1.0f)
                        {
                            state.SmeltProgress01 = 0f;
                            state.CrushedOreUnits -= Math.Min(state.CrushedOreUnits, 2);
                            if (state.CrushedOreUnits <= 0)
                            {
                                state.OreSmeltSpeedMultiplier = BloomeryFurnaceState.CrushedOreSmeltSpeedMultiplier;
                            }
                            state.CharcoalUnits -= Math.Min(state.CharcoalUnits, 2);
                            state.HasMoltenCopperReady = true;
                        }
                    }
                }
                else if (state.OreMaterialId == BloomeryFurnaceState.IronMatId &&
                         state.IsFrontClayPlugIntact &&
                         !state.HasReadyIronBloom)
                {
                    if (state.TemperatureCelsius >= BloomeryFurnaceState.IronBloomThresholdCelsius)
                    {
                        state.SmeltProgress01 += (dt / 25.0f) * smeltSpeedMul;
                        state.SlagTappedVisual01 = Math.Clamp(state.SmeltProgress01, 0f, 1f);
                        if (state.SmeltProgress01 >= 1.0f)
                        {
                            state.SmeltProgress01 = 0f;
                            state.SlagTappedVisual01 = 1.0f;
                            state.CrushedOreUnits -= Math.Min(state.CrushedOreUnits, 2);
                            if (state.CrushedOreUnits <= 0)
                            {
                                state.OreSmeltSpeedMultiplier = BloomeryFurnaceState.CrushedOreSmeltSpeedMultiplier;
                            }
                            state.CharcoalUnits -= Math.Min(state.CharcoalUnits, 2);
                            state.HasReadyIronBloom = true;
                            state.HasTappedSlagShard = true;
                        }
                    }
                }
            }
        }

        public static HotForgeWorkpieceState CreateHotBloomWorkpiece(
            in PhysicalItemShapeDna bloomDna,
            float initialTempC = HotForgeWorkpieceState.InitialBloomTempCelsius)
        {
            float clampedTemp = Math.Clamp(initialTempC, 20.0f, 1320.0f);
            return new HotForgeWorkpieceState
            {
                IsOccupied = true,
                IsCompletedWroughtBar = false,
                TemperatureCelsius = clampedTemp,
                CompletedStrikes = Math.Clamp((int)bloomDna.StageByte, 0, HotForgeWorkpieceState.RequiredHotStrikes - 1),
                ShapeDna = bloomDna.WithTemperature(TemperatureToUberByte(clampedTemp))
            };
        }

        public static void StepHotForgeWorkpiece(ref HotForgeWorkpieceState wp, float dt)
        {
            if (!wp.IsOccupied || dt <= 0f) return;
            wp.TemperatureCelsius = MathF.Max(20.0f, wp.TemperatureCelsius - HotForgeWorkpieceState.AirCoolingPerSec * dt);
            wp.ShapeDna = wp.ShapeDna.WithTemperature(TemperatureToUberByte(wp.TemperatureCelsius));
        }

        public static void ReheatWorkpiece(ref HotForgeWorkpieceState wp, float targetTempC = 1020.0f)
        {
            if (!wp.IsOccupied) return;
            wp.TemperatureCelsius = MathF.Max(wp.TemperatureCelsius, Math.Clamp(targetTempC, 20.0f, 1300.0f));
            wp.ShapeDna = wp.ShapeDna.WithTemperature(TemperatureToUberByte(wp.TemperatureCelsius));
        }

        public static bool ApplyHotForgeStrike(
            ref HotForgeWorkpieceState wp,
            out bool rejectedCold,
            out bool completedWroughtBar)
        {
            rejectedCold = false;
            completedWroughtBar = false;
            if (!wp.IsOccupied || wp.IsCompletedWroughtBar) return false;

            if (wp.TemperatureCelsius < HotForgeWorkpieceState.MinHotForgeCelsius)
            {
                rejectedCold = true;
                return false;
            }

            wp.CompletedStrikes++;
            byte nextStage = (byte)Math.Clamp(wp.CompletedStrikes, 0, 255);
            wp.ShapeDna = wp.ShapeDna.WithStage(nextStage).WithTemperature(TemperatureToUberByte(wp.TemperatureCelsius));

            if (wp.CompletedStrikes >= HotForgeWorkpieceState.RequiredHotStrikes)
            {
                wp.CompletedStrikes = HotForgeWorkpieceState.RequiredHotStrikes;
                wp.IsCompletedWroughtBar = true;
                completedWroughtBar = true;
            }

            return true;
        }

        public static byte TemperatureToUberByte(float tempC)
        {
            if (tempC <= 450.0f) return 0;
            float t = Math.Clamp((tempC - 450.0f) / 850.0f, 0f, 1f);
            return (byte)Math.Clamp((int)MathF.Round(t * 255.0f), 1, 255);
        }

        public static float UberByteToApproximateCelsius(byte uberTemp)
        {
            if (uberTemp == 0) return 20.0f;
            return 450.0f + (uberTemp / 255.0f) * 850.0f;
        }
    }
}
