#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Inventory.Data;
using Stratocraft.Inventory.Systems;
using Stratocraft.Voxel.Data;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Envanter ve Hotbar kare slotlarindaki esya sinifi.
    /// </summary>
    public enum InventoryItemKind : byte
    {
        Empty = 0,
        WeaponSword = 1,
        ToolPickaxe = 2,
        ToolAxe = 3,
        ToolMicroChisel = 4,
        ToolFluidFlask = 5,
        VoxelBlock = 6,
        OreChunk = 7,
        MetalIngot = 8,
        RoughStoneChunk = 9,
        LooseDirtClod = 10,
        SplitFirewood = 11,
        Stick = 12,
        ArmorHelmet = 13,
        ArmorChestplate = 14,
        ArmorVambrace = 15,
        ArmorGreaves = 16,
        PotionHealth = 17,
        PotionStamina = 18,
        MedicalBandage = 19,
        FoodCookedMeat = 20,
        FoodWildBerries = 21,
        FoodCleanWater = 22,
        PlaceableCampfire = 23
    }

    /// <summary>
    /// Kare slot icindeki tekil esya yigini verisi.
    /// </summary>
    public struct VisualInventorySlot
    {
        public InventoryItemKind Kind;
        public byte SubId; // VoxelBlock/OreChunk/RoughStone/Firewood -> MaterialId, ToolFluidFlask -> 0:Bos 1:Su 2:Lav, MetalIngot -> 1:Cu 2:Fe 3:Mi, Armor -> Tier
        public int Count;
        public float Durability01;

        public bool IsEmpty => Kind == InventoryItemKind.Empty || Count <= 0;

        public bool CanStackWith(in VisualInventorySlot other)
        {
            if (IsEmpty || other.IsEmpty) return false;
            if (Kind != other.Kind || SubId != other.SubId) return false;
            return Kind == InventoryItemKind.VoxelBlock ||
                   Kind == InventoryItemKind.OreChunk ||
                   Kind == InventoryItemKind.MetalIngot ||
                   Kind == InventoryItemKind.ToolFluidFlask ||
                   Kind == InventoryItemKind.RoughStoneChunk ||
                   Kind == InventoryItemKind.LooseDirtClod ||
                   Kind == InventoryItemKind.SplitFirewood ||
                   Kind == InventoryItemKind.Stick ||
                   Kind == InventoryItemKind.PotionHealth ||
                   Kind == InventoryItemKind.PotionStamina ||
                   Kind == InventoryItemKind.MedicalBandage ||
                   Kind == InventoryItemKind.FoodCookedMeat ||
                   Kind == InventoryItemKind.FoodWildBerries ||
                   Kind == InventoryItemKind.FoodCleanWater ||
                   Kind == InventoryItemKind.PlaceableCampfire;
        }
    }

    /// <summary>
    /// Alt Kemer/Kilif Durum Bari (0: Bos El, 1..9: Kilif Yuvalari) + [I] Tusu The Forest Tarzi
    /// 3B Fiziksel Kumas Canta Ortusu Uzerinde Serbest 3B Surukle-Birak (3D Raycast Plane Drag &amp; Drop)
    /// Veri, Kanal ve Diyajetik 3B Bilgi Etiketi Kontrolcusu. (2B Minecraft/IMGUI envanter/sandik penceresi YOKTUR!)
    /// </summary>
    public sealed class Stage1VisualHotbarAndInventoryUi
    {
        public const int HotbarSlotCount = 9;
        public const int BackpackSlotCount = 24;
        public const int ChestSlotCount = 16;
        public const int ArmorSlotCount = 4;

        public readonly VisualInventorySlot[] HotbarSlots = new VisualInventorySlot[HotbarSlotCount];
        public readonly VisualInventorySlot[] BackpackSlots = new VisualInventorySlot[BackpackSlotCount];
        public readonly VisualInventorySlot[] ChestSlots = new VisualInventorySlot[ChestSlotCount];
        public readonly VisualInventorySlot[] EquippedArmorSlots = new VisualInventorySlot[ArmorSlotCount];

        public VisualInventorySlot CursorHeldSlot;
        public bool DragOriginIsHotbar { get; private set; }
        public int DragOriginSlotIndex { get; private set; } = -1;

        public int SelectedHotbarIndex { get; private set; } = -1; // -1 = Bos El [0], 0..8 = Slot [1..9]
        public bool IsChestWindowOpen { get; set; }
        public bool IsCreativeCatalogOpen { get; set; }
        public int CreativeCategoryIndex { get; set; }
        public string HoveredSlotTooltip { get; private set; } = string.Empty;

        /// <summary>3B Kumas Canta Ortusu uzerinde fareyle uzerine gelinen 3B esyanin dunya konumu ve bilgi etiketi.</summary>
        public bool HasHovered3DMatSlot { get; set; }
        public Vector3 Hovered3DMatWorldPos { get; set; }
        public string Hovered3DMatTitle { get; set; } = string.Empty;
        public string Hovered3DMatDetail { get; set; } = string.Empty;
        public Transform? ActiveBackpackMatRoot { get; set; }

        public float ChannelRemainingSec { get; private set; }
        public float ChannelTotalSec { get; private set; }
        public string ChannelLabel { get; private set; } = string.Empty;
        public bool IsChannelActive => ChannelRemainingSec > 0.001f;

        private readonly Dictionary<int, Texture2D> _iconCache = new(64);
        private Texture2D? _slotBgTex;
        private Texture2D? _slotSelectedBgTex;
        private Texture2D? _slotHoverBgTex;
        private Texture2D? _windowBgTex;
        private Texture2D? _whitePixelTex;
        private GUIStyle? _countLabelStyle;
        private GUIStyle? _keyBadgeStyle;
        private GUIStyle? _windowTitleStyle;
        private GUIStyle? _slotTitleBannerStyle;
        private GUIStyle? _tooltipBoxStyle;
        private GUIStyle? _vitalsLabelStyle;
        private bool _iconsInitialized;

        private static readonly string[] CachedHotbarKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        private static readonly string[] CachedStackCountStrings = BuildCachedStackCountStrings();
        private int _lastHpCurInt = -1;
        private int _lastHpMaxInt = -1;
        private string _cachedHpLabel = string.Empty;
        private int _lastStamPctInt = -1;
        private bool _lastStamExhausted;
        private string _cachedStamLabel = string.Empty;
        private int _lastOxyPctInt = -1;
        private string _cachedOxyLabel = string.Empty;

        private static string[] BuildCachedStackCountStrings()
        {
            var arr = new string[257];
            for (int i = 0; i < arr.Length; i++) arr[i] = $"x{i}";
            return arr;
        }

        private static string GetCachedStackCountString(int count) =>
            (uint)count < (uint)CachedStackCountStrings.Length ? CachedStackCountStrings[count] : $"x{count}";

        private static readonly byte[] TrackedCrystalMaterials =
        {
            MaterialIdCatalog.Stone,
            MaterialIdCatalog.Dirt,
            MaterialIdCatalog.Sand,
            MaterialIdCatalog.Basalt,
            MaterialIdCatalog.NaturalObsidian,
            MaterialIdCatalog.Cobblestone,
            MaterialIdCatalog.OakWood,
            MaterialIdCatalog.Ironwood,
            MaterialIdCatalog.Marble,
            MaterialIdCatalog.StoneBrick,
            MaterialIdCatalog.LooseRubble,
            MaterialIdCatalog.CopperOre,
            MaterialIdCatalog.IronOre,
            MaterialIdCatalog.GoldOre,
            MaterialIdCatalog.MithrilOre
        };

        public Stage1VisualHotbarAndInventoryUi()
        {
            InitializeDefaultSlots();
        }

        public static int GetMaxStackCount(InventoryItemKind kind, byte subId = 0) => kind switch
        {
            InventoryItemKind.RoughStoneChunk => 6,
            InventoryItemKind.LooseDirtClod => 8,
            InventoryItemKind.SplitFirewood => 6,
            InventoryItemKind.Stick => 16,
            InventoryItemKind.OreChunk => 6,
            InventoryItemKind.MetalIngot => 16,
            InventoryItemKind.VoxelBlock => 8,
            InventoryItemKind.ToolFluidFlask => 16,
            InventoryItemKind.PotionHealth => 8,
            InventoryItemKind.PotionStamina => 8,
            InventoryItemKind.MedicalBandage => 12,
            InventoryItemKind.FoodCookedMeat => 12,
            InventoryItemKind.FoodWildBerries => 16,
            InventoryItemKind.FoodCleanWater => 8,
            InventoryItemKind.PlaceableCampfire => 4,
            _ => 1
        };

        private void InitializeDefaultSlots()
        {
            // Hayatta Kalma (Survival) Anayasası: Oyuncu dünyaya eli tamamen boş başlar.
            // Tüm alet ve zırhları kendisi üretir veya [F4] + [Tab] Yaratıcı Mod ile test eder!
            SelectedHotbarIndex = -1;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                HotbarSlots[i] = default;
            }

            for (int i = 0; i < BackpackSlotCount; i++)
            {
                BackpackSlots[i] = default;
            }

            for (int i = 0; i < ArmorSlotCount; i++)
            {
                EquippedArmorSlots[i] = default;
            }

            // Dunyadaki baslangic sandigi (CAS Sandik Kasasi) acil durum rezervi olarak bazi maddeleri icerir
            ChestSlots[0] = new VisualInventorySlot { Kind = InventoryItemKind.MetalIngot, SubId = 1, Count = 8, Durability01 = 1.0f };
            ChestSlots[1] = new VisualInventorySlot { Kind = InventoryItemKind.MetalIngot, SubId = 2, Count = 6, Durability01 = 1.0f };
            ChestSlots[2] = new VisualInventorySlot { Kind = InventoryItemKind.MetalIngot, SubId = 3, Count = 4, Durability01 = 1.0f };
            ChestSlots[3] = new VisualInventorySlot { Kind = InventoryItemKind.VoxelBlock, SubId = MaterialIdCatalog.StoneBrick, Count = 16, Durability01 = 1.0f };
            ChestSlots[4] = new VisualInventorySlot { Kind = InventoryItemKind.VoxelBlock, SubId = MaterialIdCatalog.Marble, Count = 12, Durability01 = 1.0f };
            ChestSlots[5] = new VisualInventorySlot { Kind = InventoryItemKind.VoxelBlock, SubId = MaterialIdCatalog.Ironwood, Count = 8, Durability01 = 1.0f };
            ChestSlots[6] = new VisualInventorySlot { Kind = InventoryItemKind.OreChunk, SubId = MaterialIdCatalog.GoldOre, Count = 4, Durability01 = 1.0f };
            ChestSlots[7] = new VisualInventorySlot { Kind = InventoryItemKind.OreChunk, SubId = MaterialIdCatalog.MithrilOre, Count = 2, Durability01 = 1.0f };
            for (int i = 8; i < ChestSlotCount; i++)
            {
                ChestSlots[i] = default;
            }
        }

        /// <summary>
        /// Yerden [E] ile toplanan veya kazidan/kesimden elde edilen esyalari cantaya (veya hotbara) fiziksel yigin siniriyla istifler.
        /// </summary>
        public bool TryAddHarvestedWorldItem(InventoryItemKind kind, byte subId, int count, out int addedCount)
        {
            addedCount = 0;
            if (count <= 0 || kind == InventoryItemKind.Empty) return false;

            int remaining = count;
            int maxStack = GetMaxStackCount(kind, subId);

            // 1) Once cantadaki mevcut ayni tur ve dolmamis yuvalara ekle
            for (int i = 0; i < BackpackSlotCount && remaining > 0; i++)
            {
                ref VisualInventorySlot slot = ref BackpackSlots[i];
                if (!slot.IsEmpty && slot.Kind == kind && slot.SubId == subId && slot.Count < maxStack)
                {
                    int space = maxStack - slot.Count;
                    int add = Mathf.Min(space, remaining);
                    slot.Count += add;
                    remaining -= add;
                    addedCount += add;
                }
            }

            // 2) Sonra hotbardaki mevcut ayni tur ve dolmamis yuvalara ekle
            for (int i = 0; i < HotbarSlotCount && remaining > 0; i++)
            {
                ref VisualInventorySlot slot = ref HotbarSlots[i];
                if (!slot.IsEmpty && slot.Kind == kind && slot.SubId == subId && slot.Count < maxStack)
                {
                    int space = maxStack - slot.Count;
                    int add = Mathf.Min(space, remaining);
                    slot.Count += add;
                    remaining -= add;
                    addedCount += add;
                }
            }

            // 3) Hala kaldiysa cantadaki ilk bos yuvaya koy
            for (int i = 0; i < BackpackSlotCount && remaining > 0; i++)
            {
                ref VisualInventorySlot slot = ref BackpackSlots[i];
                if (slot.IsEmpty)
                {
                    int add = Mathf.Min(maxStack, remaining);
                    slot = new VisualInventorySlot { Kind = kind, SubId = subId, Count = add, Durability01 = 1.0f };
                    remaining -= add;
                    addedCount += add;
                }
            }

            // 4) Cantada yer yoksa hotbardaki ilk bos yuvaya koy
            for (int i = 0; i < HotbarSlotCount && remaining > 0; i++)
            {
                ref VisualInventorySlot slot = ref HotbarSlots[i];
                if (slot.IsEmpty)
                {
                    int add = Mathf.Min(maxStack, remaining);
                    slot = new VisualInventorySlot { Kind = kind, SubId = subId, Count = add, Durability01 = 1.0f };
                    remaining -= add;
                    addedCount += add;
                }
            }

            return addedCount > 0;
        }

        /// <summary>Secili Hotbar slotundaki esyayi dondurur (Bos El ise Empty doner).</summary>
        public VisualInventorySlot GetActiveHotbarSlot() =>
            (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) ? default : HotbarSlots[SelectedHotbarIndex];

        /// <summary>Belirli bir Hotbar slotunu (0..8 -> [1..9], -1 -> Bos El [0]) secer.</summary>
        public void SelectHotbarSlot(int index) =>
            SelectedHotbarIndex = Mathf.Clamp(index, -1, HotbarSlotCount - 1);

        public void SetActiveHotbarSlot(VisualInventorySlot slot)
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount)
            {
                HotbarSlots[SelectedHotbarIndex] = slot;
            }
        }

        public static bool IsArmorKind(InventoryItemKind kind) =>
            kind == InventoryItemKind.ArmorHelmet ||
            kind == InventoryItemKind.ArmorChestplate ||
            kind == InventoryItemKind.ArmorVambrace ||
            kind == InventoryItemKind.ArmorGreaves;

        public static int GetArmorSlotIndexForItemKind(InventoryItemKind kind) => kind switch
        {
            InventoryItemKind.ArmorHelmet => 0,
            InventoryItemKind.ArmorChestplate => 1,
            InventoryItemKind.ArmorVambrace => 2,
            InventoryItemKind.ArmorGreaves => 3,
            _ => -1
        };

        public static string GetArmorSlotName(int slotIndex) => slotIndex switch
        {
            0 => "Kafa (Miğfer)",
            1 => "Gövde (Göğüslük)",
            2 => "Kollar (Kolçak)",
            3 => "Bacaklar (Dizlik)",
            _ => "Zırh"
        };

        public static string GetArmorTierName(byte tier) => tier switch
        {
            1 => "Deri/Kumaş (%30)",
            2 => "Ahşap (%45)",
            3 => "Bakır (%60)",
            4 => "Demir (%75)",
            5 => "Mithril (%90)",
            _ => "Zırhsız (%0)"
        };

        public void SyncEquippedArmorToVitals(ref Stratocraft.Character.Data.CharacterVitalsComponent vitals)
        {
            vitals.EquippedHelmetTier = (byte)(EquippedArmorSlots[0].Kind == InventoryItemKind.ArmorHelmet ? EquippedArmorSlots[0].SubId : 0);
            vitals.EquippedChestplateTier = (byte)(EquippedArmorSlots[1].Kind == InventoryItemKind.ArmorChestplate ? EquippedArmorSlots[1].SubId : 0);
            vitals.EquippedVambracesTier = (byte)(EquippedArmorSlots[2].Kind == InventoryItemKind.ArmorVambrace ? EquippedArmorSlots[2].SubId : 0);
            vitals.EquippedGreavesTier = (byte)(EquippedArmorSlots[3].Kind == InventoryItemKind.ArmorGreaves ? EquippedArmorSlots[3].SubId : 0);
        }

        public bool TryEquipArmor(ref VisualInventorySlot itemToEquip, out VisualInventorySlot unequippedOld)
        {
            unequippedOld = default;
            int targetSlot = GetArmorSlotIndexForItemKind(itemToEquip.Kind);
            if (targetSlot < 0) return false;

            unequippedOld = EquippedArmorSlots[targetSlot];
            EquippedArmorSlots[targetSlot] = itemToEquip;
            EquippedArmorSlots[targetSlot].Count = 1;
            itemToEquip = unequippedOld;
            return true;
        }

        public bool TryUnequipArmor(int slotIndex, out VisualInventorySlot unequipped)
        {
            unequipped = default;
            if ((uint)slotIndex >= (uint)EquippedArmorSlots.Length) return false;
            if (EquippedArmorSlots[slotIndex].IsEmpty) return false;

            unequipped = EquippedArmorSlots[slotIndex];
            EquippedArmorSlots[slotIndex] = default;
            return true;
        }

        public bool TryEquipActiveHotbarArmor(ref Stratocraft.Character.Data.CharacterVitalsComponent vitals, out string feedback)
        {
            feedback = string.Empty;
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot activeSlot = ref HotbarSlots[SelectedHotbarIndex];
            if (activeSlot.IsEmpty || !IsArmorKind(activeSlot.Kind)) return false;

            int targetSlot = GetArmorSlotIndexForItemKind(activeSlot.Kind);
            if (targetSlot < 0) return false;

            VisualInventorySlot oldArmor = EquippedArmorSlots[targetSlot];
            EquippedArmorSlots[targetSlot] = activeSlot;
            EquippedArmorSlots[targetSlot].Count = 1;
            activeSlot = oldArmor;

            SyncEquippedArmorToVitals(ref vitals);
            feedback = $"🛡️ Kuşanıldı: {GetArmorSlotName(targetSlot)} ({GetArmorTierName(EquippedArmorSlots[targetSlot].SubId)})";
            return true;
        }

        public void HandleArmorSlotClick(
            int slotIndex,
            int mouseButton,
            Stage1MatterCrystalAndMiningController? miningCtrl)
        {
            if ((uint)slotIndex >= (uint)EquippedArmorSlots.Length) return;

            if (!CursorHeldSlot.IsEmpty)
            {
                int targetIndex = GetArmorSlotIndexForItemKind(CursorHeldSlot.Kind);
                if (targetIndex == slotIndex)
                {
                    VisualInventorySlot old = EquippedArmorSlots[slotIndex];
                    EquippedArmorSlots[slotIndex] = CursorHeldSlot;
                    EquippedArmorSlots[slotIndex].Count = 1;
                    CursorHeldSlot = old;
                    if (miningCtrl != null)
                    {
                        miningCtrl.LastFeedbackText = $"🛡️ Kuşanıldı: {GetArmorSlotName(slotIndex)} ({GetArmorTierName(EquippedArmorSlots[slotIndex].SubId)})";
                    }
                    return;
                }
                else
                {
                    if (miningCtrl != null)
                    {
                        miningCtrl.LastFeedbackText = $"⚠️ Bu eşya {GetArmorSlotName(slotIndex)} yuvasına takılamaz!";
                    }
                    return;
                }
            }

            if (!EquippedArmorSlots[slotIndex].IsEmpty)
            {
                VisualInventorySlot unequipped = EquippedArmorSlots[slotIndex];
                EquippedArmorSlots[slotIndex] = default;

                if (mouseButton == 1)
                {
                    if (!TryMergeOrPlaceIntoArray(unequipped, BackpackSlots) && !TryMergeOrPlaceIntoArray(unequipped, HotbarSlots))
                    {
                        CursorHeldSlot = unequipped;
                    }
                }
                else
                {
                    CursorHeldSlot = unequipped;
                }

                if (miningCtrl != null)
                {
                    miningCtrl.LastFeedbackText = $"{GetArmorSlotName(slotIndex)} zırhı çıkarıldı.";
                }
            }
        }

        /// <summary>
        /// Secili Hotbar slotunda yalnizca insa edilebilir VoxelBlock veya MetalIngot varsa 1 adet tuketir ve MaterialId dondurur.
        /// (Kaba tas parcalari, gevsek toprak, odun ve dallar dogrudan blok olarak konulamaz!)
        /// </summary>
        public bool TryConsumeSelectedBuildableVoxel(out byte materialId)
        {
            return TryConsumeSelectedBuildableVoxel(out materialId, out _);
        }

        public bool TryConsumeSelectedBuildableVoxel(out byte materialId, out InventoryItemKind itemKind)
        {
            materialId = 0;
            itemKind = InventoryItemKind.Empty;
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot slot = ref HotbarSlots[SelectedHotbarIndex];
            if (slot.IsEmpty) return false;

            if (slot.Kind == InventoryItemKind.VoxelBlock ||
                slot.Kind == InventoryItemKind.MetalIngot)
            {
                byte resolvedMat = slot.Kind == InventoryItemKind.MetalIngot
                    ? (slot.SubId switch
                    {
                        1 => MaterialIdCatalog.CopperOre,
                        2 => MaterialIdCatalog.IronOre,
                        3 => MaterialIdCatalog.MithrilOre,
                        _ => slot.SubId != 0 ? slot.SubId : MaterialIdCatalog.Steel
                    })
                    : slot.SubId;

                if (resolvedMat == 0) return false;
                materialId = resolvedMat;
                itemKind = slot.Kind;
                slot.Count--;
                if (slot.Count <= 0)
                {
                    slot = default;
                }
                return true;
            }
            return false;
        }

        public bool TryConsumeActiveHotbarSlot(int count = 1)
        {
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return false;
            ref VisualInventorySlot slot = ref HotbarSlots[SelectedHotbarIndex];
            if (slot.IsEmpty || slot.Count < count) return false;
            slot.Count -= count;
            if (slot.Count <= 0)
            {
                slot = default;
            }
            return true;
        }

        /// <summary>Secili aletin dayaniklilik cubugunu (0..1) gunceller.</summary>
        public void UpdateSelectedToolDurability(float durability01)
        {
            float clamped = Mathf.Clamp01(durability01);
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount && !HotbarSlots[SelectedHotbarIndex].IsEmpty)
            {
                HotbarSlots[SelectedHotbarIndex].Durability01 = clamped;
                return;
            }
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (HotbarSlots[i].Kind == InventoryItemKind.ToolPickaxe || HotbarSlots[i].Kind == InventoryItemKind.ToolAxe)
                {
                    HotbarSlots[i].Durability01 = clamped;
                }
            }
        }

        /// <summary>Fiziksel kusanma (1.5s) veya Madde Kristali aktarim (3.0s) kanalini baslatir.</summary>
        public void StartEquipOrTransferChannel(float durationSec, string label)
        {
            ChannelTotalSec = Mathf.Max(0.1f, durationSec);
            ChannelRemainingSec = ChannelTotalSec;
            ChannelLabel = label;
        }

        /// <summary>Aktif kusanma/transfer kanalini ilerletir.</summary>
        public void StepChannel(float dt, Stage1MatterCrystalAndMiningController? miningCtrl = null)
        {
            if (ChannelRemainingSec <= 0f) return;
            ChannelRemainingSec = Mathf.Max(0f, ChannelRemainingSec - dt);
            if (ChannelRemainingSec <= 0f)
            {
                if (miningCtrl != null)
                {
                    miningCtrl.LastFeedbackText = $"TAMAMLANDI: {ChannelLabel}";
                }
                ChannelLabel = string.Empty;
            }
        }

        /// <summary>Hasar alindiginda veya canta kapatildiginda aktif kanali aninda iptal eder.</summary>
        public void CancelActiveChannel()
        {
            ChannelRemainingSec = 0f;
            ChannelTotalSec = 0f;
            ChannelLabel = string.Empty;
        }

        /// <summary>Secili matara slotunun sivi turunu dondurur (0=Bos, 1=Su, 2=Lav).</summary>
        public byte GetSelectedFlaskFluidType()
        {
            if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount &&
                HotbarSlots[SelectedHotbarIndex].Kind == InventoryItemKind.ToolFluidFlask)
            {
                return HotbarSlots[SelectedHotbarIndex].SubId;
            }
            return 0;
        }

        /// <summary>Secili matara slotunun sivi turunu ve kullanim adedini gunceller.</summary>
        public void SetSelectedFlaskFluidType(byte fluidSubId, int countDelta = 0)
        {
            if (SelectedHotbarIndex < 0 || SelectedHotbarIndex >= HotbarSlotCount) return;
            ref VisualInventorySlot slot = ref HotbarSlots[SelectedHotbarIndex];
            if (slot.Kind != InventoryItemKind.ToolFluidFlask) return;

            slot.SubId = fluidSubId;
            slot.Count = Mathf.Clamp(slot.Count + countDelta, 1, 16);
            if (slot.SubId == 0) slot.Count = 1;
        }

        /// <summary>Kilic slotunu bulup secer (F8/F10/G kisayollari icin).</summary>
        public void SelectSwordSlotOrFirst()
        {
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (HotbarSlots[i].Kind == InventoryItemKind.WeaponSword)
                {
                    SelectedHotbarIndex = i;
                    return;
                }
            }
            HotbarSlots[0] = new VisualInventorySlot { Kind = InventoryItemKind.WeaponSword, SubId = 0, Count = 1, Durability01 = 1.0f };
            SelectedHotbarIndex = 0;
        }

        /// <summary>Madde Kristali (CrystalStorage) ve Moloz sayaclari ile gorsel Hotbar/Canta slotlarini cift yonlu senkronize eder.</summary>
        public void SyncWithCrystalStorage(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            for (int m = 0; m < TrackedCrystalMaterials.Length; m++)
            {
                byte matId = TrackedCrystalMaterials[m];
                bool isOre = MaterialIdCatalog.IsVacuumImmuneResonanceOreOrGem(matId);
                InventoryItemKind targetKind = isOre ? InventoryItemKind.OreChunk : InventoryItemKind.VoxelBlock;

                int storageCount = matId == MaterialIdCatalog.LooseRubble
                    ? miningCtrl.LooseRubbleShardCount
                    : miningCtrl.CrystalStorage.GetCount(matId);

                int currentUiCount = CountMaterialInPlayerInventory(matId);
                if (storageCount != currentUiCount)
                {
                    SetMaterialTotalInPlayerInventory(matId, targetKind, storageCount);
                }
            }
        }

        /// <summary>3B Canta Ortusu uzerinde surukle-birak veya sandik transferi yapildiginda CrystalStorage miktarlarini gunceller.</summary>
        public void PushUiCountsToCrystalStorage(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            for (int m = 0; m < TrackedCrystalMaterials.Length; m++)
            {
                byte matId = TrackedCrystalMaterials[m];
                int uiCount = CountMaterialInPlayerInventory(matId);
                if (matId == MaterialIdCatalog.LooseRubble)
                {
                    miningCtrl.LooseRubbleShardCount = uiCount;
                }
                else
                {
                    int storageCount = miningCtrl.CrystalStorage.GetCount(matId);
                    int diff = uiCount - storageCount;
                    if (diff > 0)
                    {
                        miningCtrl.CrystalStorage.TryDepositMassQ24_8(matId, (uint)diff * CrystalMassStorageData.OneBlockQ24_8);
                    }
                    else if (diff < 0)
                    {
                        int toRemove = -diff;
                        for (int r = 0; r < toRemove; r++)
                        {
                            miningCtrl.CrystalStorage.TryWithdrawNaturalBlock(matId, out _);
                        }
                    }
                }
            }
        }

        private int CountMaterialInPlayerInventory(byte matId)
        {
            int sum = 0;
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (!HotbarSlots[i].IsEmpty &&
                    (HotbarSlots[i].Kind == InventoryItemKind.VoxelBlock || HotbarSlots[i].Kind == InventoryItemKind.OreChunk) &&
                    HotbarSlots[i].SubId == matId)
                {
                    sum += HotbarSlots[i].Count;
                }
            }
            for (int i = 0; i < BackpackSlotCount; i++)
            {
                if (!BackpackSlots[i].IsEmpty &&
                    (BackpackSlots[i].Kind == InventoryItemKind.VoxelBlock || BackpackSlots[i].Kind == InventoryItemKind.OreChunk) &&
                    BackpackSlots[i].SubId == matId)
                {
                    sum += BackpackSlots[i].Count;
                }
            }
            if (!CursorHeldSlot.IsEmpty &&
                (CursorHeldSlot.Kind == InventoryItemKind.VoxelBlock || CursorHeldSlot.Kind == InventoryItemKind.OreChunk) &&
                CursorHeldSlot.SubId == matId)
            {
                sum += CursorHeldSlot.Count;
            }
            return sum;
        }

        private void SetMaterialTotalInPlayerInventory(byte matId, InventoryItemKind kind, int targetTotal)
        {
            int current = CountMaterialInPlayerInventory(matId);
            int delta = targetTotal - current;
            if (delta == 0) return;

            if (delta > 0)
            {
                for (int i = 0; i < HotbarSlotCount; i++)
                {
                    if (!HotbarSlots[i].IsEmpty && HotbarSlots[i].Kind == kind && HotbarSlots[i].SubId == matId)
                    {
                        HotbarSlots[i].Count += delta;
                        return;
                    }
                }
                for (int i = 0; i < BackpackSlotCount; i++)
                {
                    if (!BackpackSlots[i].IsEmpty && BackpackSlots[i].Kind == kind && BackpackSlots[i].SubId == matId)
                    {
                        BackpackSlots[i].Count += delta;
                        return;
                    }
                }
                for (int i = 0; i < HotbarSlotCount; i++)
                {
                    if (HotbarSlots[i].IsEmpty)
                    {
                        HotbarSlots[i] = new VisualInventorySlot { Kind = kind, SubId = matId, Count = delta, Durability01 = 1.0f };
                        return;
                    }
                }
                for (int i = 0; i < BackpackSlotCount; i++)
                {
                    if (BackpackSlots[i].IsEmpty)
                    {
                        BackpackSlots[i] = new VisualInventorySlot { Kind = kind, SubId = matId, Count = delta, Durability01 = 1.0f };
                        return;
                    }
                }
            }
            else
            {
                int rem = -delta;
                if (SelectedHotbarIndex >= 0 && SelectedHotbarIndex < HotbarSlotCount)
                {
                    ref var sel = ref HotbarSlots[SelectedHotbarIndex];
                    if (!sel.IsEmpty && sel.Kind == kind && sel.SubId == matId)
                    {
                        int take = Mathf.Min(rem, sel.Count);
                        sel.Count -= take;
                        rem -= take;
                        if (sel.Count <= 0) sel = default;
                    }
                }
                for (int i = HotbarSlotCount - 1; i >= 0 && rem > 0; i--)
                {
                    if (!HotbarSlots[i].IsEmpty && HotbarSlots[i].Kind == kind && HotbarSlots[i].SubId == matId)
                    {
                        int take = Mathf.Min(rem, HotbarSlots[i].Count);
                        HotbarSlots[i].Count -= take;
                        rem -= take;
                        if (HotbarSlots[i].Count <= 0) HotbarSlots[i] = default;
                    }
                }
                for (int i = BackpackSlotCount - 1; i >= 0 && rem > 0; i--)
                {
                    if (!BackpackSlots[i].IsEmpty && BackpackSlots[i].Kind == kind && BackpackSlots[i].SubId == matId)
                    {
                        int take = Mathf.Min(rem, BackpackSlots[i].Count);
                        BackpackSlots[i].Count -= take;
                        rem -= take;
                        if (BackpackSlots[i].Count <= 0) BackpackSlots[i] = default;
                    }
                }
            }
        }

        /// <summary>
        /// 3B Kumas Canta Ortusu (The Forest Stili) uzerindeki bir 3B yuvadan esya suruklemeyi baslatir.
        /// </summary>
        public bool Begin3DMatSlotDrag(bool isHotbar, int slotIndex, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            VisualInventorySlot[] arr = isHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)slotIndex >= (uint)arr.Length || arr[slotIndex].IsEmpty) return false;

            CursorHeldSlot = arr[slotIndex];
            DragOriginIsHotbar = isHotbar;
            DragOriginSlotIndex = slotIndex;
            arr[slotIndex] = default;
            PushUiCountsToCrystalStorage(miningCtrl);
            return true;
        }

        /// <summary>
        /// 3B Kumas Canta Ortusu uzerinde suruklenen 3B esyayi hedef 3B yuvaya birakir, birlestirir veya yerini degistirir (Swap).
        /// </summary>
        public void Complete3DMatSlotDrop(
            bool targetIsHotbar,
            int targetSlotIndex,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action onActiveSlotChanged)
        {
            if (CursorHeldSlot.IsEmpty) return;

            VisualInventorySlot[] targetArr = targetIsHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)targetSlotIndex >= (uint)targetArr.Length)
            {
                ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
                onActiveSlotChanged();
                return;
            }

            ref VisualInventorySlot targetSlot = ref targetArr[targetSlotIndex];
            VisualInventorySlot placed = CursorHeldSlot;

            if (targetSlot.IsEmpty)
            {
                targetSlot = CursorHeldSlot;
                CursorHeldSlot = default;
            }
            else if (targetSlot.CanStackWith(in CursorHeldSlot))
            {
                targetSlot.Count += CursorHeldSlot.Count;
                CursorHeldSlot = default;
            }
            else
            {
                // 3B Kumas Ortu Uzerinde Serbest Yer Degistirme (Spatial 3D Swap):
                // Hedef yuvadaki esya suruklemenin basladigi kaynak yuvaya gonderilir!
                VisualInventorySlot displaced = targetSlot;
                targetSlot = CursorHeldSlot;
                CursorHeldSlot = default;

                VisualInventorySlot[] originArr = DragOriginIsHotbar ? HotbarSlots : BackpackSlots;
                if ((uint)DragOriginSlotIndex < (uint)originArr.Length && originArr[DragOriginSlotIndex].IsEmpty)
                {
                    originArr[DragOriginSlotIndex] = displaced;
                }
                else if (!TryMergeOrPlaceIntoArray(in displaced, BackpackSlots))
                {
                    TryMergeOrPlaceIntoArray(in displaced, HotbarSlots);
                }
            }

            if (targetIsHotbar)
            {
                SelectedHotbarIndex = targetSlotIndex;
            }
            if (targetIsHotbar != DragOriginIsHotbar)
            {
                TriggerChannelForSlotItem(in placed, miningCtrl);
            }

            DragOriginSlotIndex = -1;
            PushUiCountsToCrystalStorage(miningCtrl);
            onActiveSlotChanged();
        }

        /// <summary>
        /// 3B Kumas Canta Ortusu uzerindeki bir 3B yuvaya Sag Tik (Yariya Bolme / Tekli Birakma) veya Shift+Sol Tik (Hizli Aktarim) uygular.
        /// </summary>
        public void Handle3DMatSlotSecondaryOrQuickAction(
            bool isHotbar,
            int slotIndex,
            int mouseButton,
            bool isShift,
            Stage1MatterCrystalAndMiningController miningCtrl,
            VisualChestCasSystem chestCasSystem,
            Action onActiveSlotChanged)
        {
            VisualInventorySlot[] arr = isHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)slotIndex >= (uint)arr.Length) return;

            HandleSlotMouseClick(
                ref arr[slotIndex],
                slotIndex,
                isHotbar: isHotbar,
                isChest: false,
                mouseButton: mouseButton,
                isShift: isShift,
                miningCtrl: miningCtrl,
                chestCasSystem: chestCasSystem);
            onActiveSlotChanged();
        }

        /// <summary>
        /// Suruklenen 3B esya kumas ortunun disina birakildiginda esyayi kaybolmadan alindigi 3B yuvaya geri koyar.
        /// </summary>
        public void ReturnCursorHeldItemToOriginOrInventory(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (CursorHeldSlot.IsEmpty) return;
            VisualInventorySlot[] originArr = DragOriginIsHotbar ? HotbarSlots : BackpackSlots;
            if ((uint)DragOriginSlotIndex < (uint)originArr.Length && originArr[DragOriginSlotIndex].IsEmpty)
            {
                originArr[DragOriginSlotIndex] = CursorHeldSlot;
                CursorHeldSlot = default;
                DragOriginSlotIndex = -1;
                PushUiCountsToCrystalStorage(miningCtrl);
                return;
            }
            ReturnCursorHeldItemToInventory(miningCtrl);
        }

        /// <summary>
        /// Ekranin en altinda kompakt [0..9] Kemer/Kilif Durum Barini cizer ve [I] 3B Kumas Canta Ortusu acikken
        /// ortu uzerindeki 3B esyalarin diyajetik Dunya->Ekran (WorldToScreenPoint) 3B bilgi etiketini gosterir.
        /// (2B Minecraft envanter ve 2B sandik pencereleri tamamen kaldirilmistir!)
        /// </summary>
        public void DrawHotbarAndInventoryOnGui(
            bool isBackpackOpen,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge,
            VisualChestCasSystem chestCasSystem,
            Action onActiveSlotChanged,
            float currentHp = 100f,
            float maxHp = 100f,
            float stamina = 100f,
            float maxStamina = 100f,
            bool isExhaustedLock = false,
            float lungOxygenPercent = 100f,
            bool isSubmerged = false,
            float coreBodyTemp = 37.0f)
        {
            EnsureTexturesAndStyles();
            Event ev = Event.current;
            if (ev.type == EventType.Layout)
            {
                SyncWithCrystalStorage(miningCtrl);
            }
            HoveredSlotTooltip = string.Empty;

            if (!isBackpackOpen && IsChestWindowOpen) IsChestWindowOpen = false;
            if (!isBackpackOpen && !CursorHeldSlot.IsEmpty) ReturnCursorHeldItemToOriginOrInventory(miningCtrl);
            if (!isBackpackOpen) HasHovered3DMatSlot = false;

            float screenW = Screen.width;
            float screenH = Screen.height;
            Vector2 mousePos = ev.mousePosition;

            const float slotSize = 54f;
            const float slotGap = 6f;
            const int totalBarSlots = HotbarSlotCount + 1;
            float totalBarWidth = totalBarSlots * slotSize + (totalBarSlots - 1) * slotGap + 16f;
            float barX = (screenW - totalBarWidth) * 0.5f;
            float barY = screenH - slotSize - 12f;

            GUI.DrawTexture(new Rect(barX - 6f, barY - 6f, totalBarWidth + 12f, slotSize + 12f), _windowBgTex!);

            Rect emptyHandRect = new Rect(barX + 8f, barY, slotSize, slotSize);
            bool emptyHandSelected = SelectedHotbarIndex == -1 && !miningCtrl.IsCrystalActive;
            DrawSingleSlotBox(emptyHandRect, default, "0", emptyHandSelected, true, combatBridge, mousePos);

            if (ev.type == EventType.MouseDown && emptyHandRect.Contains(mousePos))
            {
                SelectedHotbarIndex = -1;
                miningCtrl.IsCrystalActive = false;
                onActiveSlotChanged();
                ev.Use();
            }

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                float sx = barX + 8f + (i + 1) * (slotSize + slotGap);
                float sy = (SelectedHotbarIndex == i && !miningCtrl.IsCrystalActive) ? barY - 4f : barY;
                Rect slotRect = new Rect(sx, sy, slotSize, slotSize);
                bool isSel = SelectedHotbarIndex == i && !miningCtrl.IsCrystalActive;

                DrawSingleSlotBox(slotRect, HotbarSlots[i], CachedHotbarKeyLabels[i], isSel, false, combatBridge, mousePos);

                if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseUp) && slotRect.Contains(mousePos))
                {
                    if (isBackpackOpen)
                    {
                        if (ev.type == EventType.MouseUp && ev.button == 0 && !CursorHeldSlot.IsEmpty)
                        {
                            Complete3DMatSlotDrop(targetIsHotbar: true, targetSlotIndex: i, miningCtrl, onActiveSlotChanged);
                        }
                        else if (ev.type == EventType.MouseDown)
                        {
                            HandleSlotMouseClick(ref HotbarSlots[i], i, isHotbar: true, isChest: false, ev.button, ev.shift, miningCtrl, chestCasSystem);
                            onActiveSlotChanged();
                        }
                    }
                    else if (ev.type == EventType.MouseDown)
                    {
                        SelectedHotbarIndex = i;
                        miningCtrl.IsCrystalActive = false;
                        onActiveSlotChanged();
                    }
                    ev.Use();
                }
            }

            bool showOxygen = isSubmerged || lungOxygenPercent < 98f;
            DrawVitalsHudBars(screenW, barY, currentHp, maxHp, stamina, maxStamina, isExhaustedLock, lungOxygenPercent, isSubmerged, coreBodyTemp);

            string activeTitle = FormatSelectedSlotBannerText(miningCtrl, combatBridge);
            float bannerY = showOxygen ? barY - 50f : barY - 34f;
            GUI.Box(new Rect(screenW * 0.5f - 285f, bannerY, 570f, 20f), activeTitle, _slotTitleBannerStyle!);

            // [C] Madde Kristali Aktifken: Kristal İçeriği & Materyal Seçim Paneli (Tekerlek / [B] / Tıklama ile malzeme seçimi)
            if (miningCtrl.IsCrystalActive)
            {
                DrawCrystalContentsAndMaterialPickerPanel(screenW, barY, miningCtrl, combatBridge, ev, mousePos);
            }

            if (isBackpackOpen)
            {
                // 3B Kumaş Çanta Örtüsü üzerindeki her dolu 3B yuvanın üstüne isim + adet rozeti (x16, x12 vb.) çiz
                Draw3DMatSlotStackBadges(screenW, screenH, combatBridge);

                // 🛡️ ANATOMİK BÖLGESEL ZIRHLAR Paneli (Kafa, Gövde, Kollar, Bacaklar)
                DrawEquippedArmorPanel(screenW, screenH, barY, ev, mousePos, miningCtrl, onActiveSlotChanged);

                // Ust ince diyajetik rehber seridi (3B Kumas Canta Ortusunun ustunu ASLA kapatmaz!)
                Rect matBannerRect = new Rect(screenW * 0.5f - 410f, 178f, 820f, 28f);
                GUI.DrawTexture(matBannerRect, _windowBgTex!);
                GUI.Label(
                    matBannerRect,
                    "✦ [I] 3B FİZİKSEL KUMAŞ ÇANTA ÖRTÜSÜ (Kamera Kilitli) — 3B Eşyaları Fareyle Tutup Örtüde İstediğin Bölmeye veya Kılıf [1..9] Şeridine Sürükle & Bırak!",
                    _slotTitleBannerStyle!);

                if (IsChannelActive)
                {
                    float prog01 = 1f - Mathf.Clamp01(ChannelRemainingSec / Mathf.Max(0.1f, ChannelTotalSec));
                    Rect chBg = new Rect(screenW * 0.5f - 290f, 214f, 580f, 24f);
                    Rect chFg = new Rect(chBg.x + 3f, chBg.y + 3f, (chBg.width - 6f) * prog01, 18f);
                    GUI.DrawTexture(chBg, _windowBgTex!);
                    Color prev = GUI.color;
                    GUI.color = new Color(0.20f, 0.85f, 0.55f, 0.85f);
                    GUI.DrawTexture(chFg, _whitePixelTex!);
                    GUI.color = prev;
                    GUI.Label(chBg, $"{ChannelLabel} — Kalan: {ChannelRemainingSec:0.1}sn (Hasar Alinirsa Iptal Olur!)", _slotTitleBannerStyle!);
                }

                // 3B Kumas Ortu Uzerindeki Hovered 3B Esya Icin WorldToScreenPoint Diyajetik Bilgi Karti
                if (HasHovered3DMatSlot && !string.IsNullOrEmpty(Hovered3DMatTitle))
                {
                    Vector2 tipPos = mousePos + new Vector2(18f, -58f);
                    UnityEngine.Camera? cam = UnityEngine.Camera.main;
                    if (cam != null)
                    {
                        Vector3 sp = cam.WorldToScreenPoint(Hovered3DMatWorldPos + Vector3.up * 0.14f);
                        if (sp.z > 0.05f)
                        {
                            tipPos = new Vector2(sp.x - 185f, (screenH - sp.y) - 66f);
                        }
                    }

                    const float tw = 380f;
                    const float th = 58f;
                    float tx = Mathf.Clamp(tipPos.x, 12f, screenW - tw - 12f);
                    float ty = Mathf.Clamp(tipPos.y, 216f, barY - th - 36f);
                    GUI.Box(new Rect(tx, ty, tw, th), $"{Hovered3DMatTitle}\n{Hovered3DMatDetail}", _tooltipBoxStyle!);
                }
                else if (!string.IsNullOrEmpty(HoveredSlotTooltip) && CursorHeldSlot.IsEmpty)
                {
                    float tw = 340f;
                    float tx = Mathf.Clamp(mousePos.x + 16f, 10f, screenW - tw - 10f);
                    float ty = Mathf.Clamp(mousePos.y - 64f, 10f, barY - 62f);
                    GUI.Box(new Rect(tx, ty, tw, 54f), HoveredSlotTooltip, _tooltipBoxStyle!);
                }
            }
        }

        private void DrawEquippedArmorPanel(
            float screenW,
            float screenH,
            float barY,
            Event ev,
            Vector2 mousePos,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Action onActiveSlotChanged)
        {
            const float panelW = 286f;
            const float panelH = 212f;
            float px = screenW - panelW - 18f;
            float py = 72f;

            Rect panelRect = new Rect(px, py, panelW, panelH);
            GUI.DrawTexture(panelRect, _windowBgTex!);

            string title = "🛡️ ANATOMİK BÖLGESEL ZIRHLAR";
            GUI.Label(new Rect(px + 8f, py + 4f, panelW - 16f, 22f), title, _slotTitleBannerStyle!);

            const float slotSize = 38f;
            const float rowH = 42f;

            for (int i = 0; i < ArmorSlotCount; i++)
            {
                float ry = py + 30f + i * rowH;
                Rect slotRect = new Rect(px + 10f, ry, slotSize, slotSize);
                ref VisualInventorySlot armorSlot = ref EquippedArmorSlots[i];
                bool isHovered = slotRect.Contains(mousePos);

                Texture2D bg = isHovered ? _slotHoverBgTex! : _slotBgTex!;
                GUI.DrawTexture(slotRect, bg);

                if (!armorSlot.IsEmpty)
                {
                    Texture2D icon = GetOrBuildIconForSlot(in armorSlot, CombatWeaponArchetype.Longsword);
                    GUI.DrawTexture(new Rect(slotRect.x + 3f, slotRect.y + 3f, slotSize - 6f, slotSize - 6f), icon);
                }

                string slotLabel = GetArmorSlotName(i);
                string statusText;
                Color statusColor;

                if (armorSlot.IsEmpty)
                {
                    if (i == 0)
                    {
                        statusText = "KORUMASIZ! ⚠️ (2.5x Kritik Kafa)";
                        statusColor = new Color(1.0f, 0.35f, 0.35f);
                    }
                    else
                    {
                        statusText = "Boş Yuva (%0 Koruma)";
                        statusColor = new Color(0.72f, 0.72f, 0.72f);
                    }
                }
                else
                {
                    string tierDesc = GetArmorTierName(armorSlot.SubId);
                    statusText = $"{GetSlotDisplayName(in armorSlot, null)} — {tierDesc}";
                    statusColor = new Color(0.40f, 1.0f, 0.65f);
                }

                Rect textRect = new Rect(px + slotSize + 16f, ry, panelW - slotSize - 22f, rowH - 4f);
                Color prevColor = GUI.color;
                GUI.Label(new Rect(textRect.x, textRect.y, textRect.width, 18f), slotLabel, _keyBadgeStyle!);
                GUI.color = statusColor;
                GUI.Label(new Rect(textRect.x, textRect.y + 18f, textRect.width, 18f), statusText, _vitalsLabelStyle!);
                GUI.color = prevColor;

                if (isHovered)
                {
                    HoveredSlotTooltip = $"{slotLabel}\n{statusText}\n[Sol/Sağ Tık]: Çıkar / Zırh Değiştir";
                    if (ev.type == EventType.MouseDown)
                    {
                        HandleArmorSlotClick(i, ev.button, miningCtrl);
                        onActiveSlotChanged();
                        ev.Use();
                    }
                }
            }
        }

        private void DrawCrystalContentsAndMaterialPickerPanel(
            float screenW,
            float barY,
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge,
            Event ev,
            Vector2 mousePos)
        {
            ReadOnlySpan<byte> mats = Stage1MatterCrystalAndMiningController.GetCrystalBuildableMaterials();
            const float cellW = 58f;
            const float cellH = 50f;
            const float gap = 4f;
            float panelW = mats.Length * cellW + (mats.Length - 1) * gap + 16f;
            float panelH = cellH + 30f;
            float px = (screenW - panelW) * 0.5f;
            float py = barY - panelH - 36f;

            Rect bgRect = new Rect(px, py, panelW, panelH);
            GUI.DrawTexture(bgRect, _windowBgTex!);
            string header =
                $"◆ MADDE KRİSTALİ İÇERİĞİ (Toplam: {miningCtrl.CrystalStorage.CurrentTotalBlocks} Blok) — [Fare Tekerleği / B]: Malzeme Seç | [V]: {Stage1MatterCrystalAndMiningController.GetShapeDisplayName(miningCtrl.SelectedShapeId)} | [T]: {miningCtrl.BrushAreaLabel}";
            GUI.Label(new Rect(px + 6f, py + 3f, panelW - 12f, 20f), header, _slotTitleBannerStyle!);

            byte selectedMat = miningCtrl.SelectedMaterialId;
            for (int i = 0; i < mats.Length; i++)
            {
                byte matId = mats[i];
                int count = miningCtrl.GetMaterialCountInCrystal(matId);
                float cx = px + 8f + i * (cellW + gap);
                float cy = py + 24f;
                Rect cellRect = new Rect(cx, cy, cellW, cellH);

                bool isSelected = matId == selectedMat;
                bool isHovered = cellRect.Contains(mousePos);
                GUI.DrawTexture(cellRect, isSelected ? _slotSelectedBgTex! : (isHovered ? _slotHoverBgTex! : _slotBgTex!));

                if (isSelected)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(0.25f, 0.95f, 1.0f, 0.98f);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, cellRect.width, 2f), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.yMax - 2f, cellRect.width, 2f), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y, 2f, cellRect.height), _whitePixelTex!);
                    GUI.DrawTexture(new Rect(cellRect.xMax - 2f, cellRect.y, 2f, cellRect.height), _whitePixelTex!);
                    GUI.color = prev;
                }

                var previewSlot = new VisualInventorySlot
                {
                    Kind = InventoryItemKind.VoxelBlock,
                    SubId = matId,
                    Count = count,
                    Durability01 = 1f
                };
                Texture2D icon = GetOrBuildIconForSlot(in previewSlot, combatBridge.ActiveWeaponArchetype);
                Color prevIconCol = GUI.color;
                if (count <= 0) GUI.color = new Color(0.55f, 0.55f, 0.55f, 0.55f);
                GUI.DrawTexture(new Rect(cellRect.x + 13f, cellRect.y + 3f, 32f, 30f), icon);
                GUI.color = prevIconCol;

                string shortName = GetShortMaterialBadgeName(matId);
                GUI.Label(new Rect(cellRect.x + 2f, cellRect.y + 1f, cellRect.width - 4f, 14f), shortName, _keyBadgeStyle!);
                GUI.Label(new Rect(cellRect.x + 2f, cellRect.y + cellH - 18f, cellRect.width - 5f, 16f), GetCachedStackCountString(count), _countLabelStyle!);

                if (ev.type == EventType.MouseDown && cellRect.Contains(mousePos))
                {
                    miningCtrl.SelectMaterial(matId);
                    miningCtrl.LastFeedbackText =
                        $"Kristal İnşa Malzemesi Seçildi: {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(matId)} (Stok: x{count}) -> [Sağ Tık] Dünyaya Koy";
                    ev.Use();
                }
            }
        }

        private void Draw3DMatSlotStackBadges(
            float screenW,
            float screenH,
            Stage1DirectionalCombatBridge combatBridge)
        {
            if (ActiveBackpackMatRoot == null) return;
            UnityEngine.Camera? cam = UnityEngine.Camera.main;
            if (cam == null) return;

            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (HotbarSlots[i].IsEmpty) continue;
                Vector3 localPos = Stage1HolsterAndBackpackController.GetHotbarMatSlotLocalPos(i);
                DrawSingle3DMatBadge(cam, ActiveBackpackMatRoot.TransformPoint(localPos + new Vector3(0f, 0.055f, 0f)), in HotbarSlots[i], i + 1, screenW, screenH, combatBridge);
            }

            for (int i = 0; i < BackpackSlotCount; i++)
            {
                if (BackpackSlots[i].IsEmpty) continue;
                Vector3 localPos = Stage1HolsterAndBackpackController.GetBackpackMatSlotLocalPos(i);
                DrawSingle3DMatBadge(cam, ActiveBackpackMatRoot.TransformPoint(localPos + new Vector3(0f, 0.055f, 0f)), in BackpackSlots[i], 0, screenW, screenH, combatBridge);
            }
        }

        private void DrawSingle3DMatBadge(
            UnityEngine.Camera cam,
            Vector3 worldPos,
            in VisualInventorySlot slot,
            int hotbarNumber,
            float screenW,
            float screenH,
            Stage1DirectionalCombatBridge combatBridge)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0.1f) return;

            float sx = sp.x;
            float sy = screenH - sp.y;
            if (sx < 20f || sx > screenW - 20f || sy < 190f || sy > screenH - 70f) return;

            string shortLabel = GetShortSlotBadgeLabel(in slot);
            string badgeText = slot.Count > 1 || slot.Kind == InventoryItemKind.VoxelBlock || slot.Kind == InventoryItemKind.OreChunk || slot.Kind == InventoryItemKind.MetalIngot
                ? $"{shortLabel} x{slot.Count}"
                : (hotbarNumber > 0 ? $"[{hotbarNumber}] {shortLabel}" : shortLabel);

            const float bw = 78f;
            const float bh = 20f;
            Rect r = new Rect(sx - bw * 0.5f, sy - bh * 0.5f, bw, bh);
            GUI.DrawTexture(r, _windowBgTex!);
            GUI.Label(r, badgeText, _keyBadgeStyle!);
        }

        private static string GetShortSlotBadgeLabel(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => "Kılıç",
            InventoryItemKind.ToolPickaxe => "Kazma",
            InventoryItemKind.ToolAxe => "Balta",
            InventoryItemKind.ToolMicroChisel => "Keski",
            InventoryItemKind.ToolFluidFlask => slot.SubId == 2 ? "LavMtr" : (slot.SubId == 1 ? "SuMtr" : "Matara"),
            InventoryItemKind.MetalIngot => slot.SubId == 1 ? "Bakır" : (slot.SubId == 2 ? "Demir" : "Mithril"),
            InventoryItemKind.OreChunk => GetShortMaterialBadgeName(slot.SubId),
            InventoryItemKind.VoxelBlock => GetShortMaterialBadgeName(slot.SubId),
            InventoryItemKind.RoughStoneChunk => "KabaTaş",
            InventoryItemKind.LooseDirtClod => "Toprak",
            InventoryItemKind.SplitFirewood => "Odun",
            InventoryItemKind.Stick => "Dal",
            InventoryItemKind.ArmorHelmet => "Miğfer",
            InventoryItemKind.ArmorChestplate => "Zırh",
            InventoryItemKind.ArmorVambrace => "Kolçak",
            InventoryItemKind.ArmorGreaves => "Dizlik",
            InventoryItemKind.PotionHealth => "Canİks",
            InventoryItemKind.PotionStamina => "Enrİks",
            InventoryItemKind.MedicalBandage => "Bandaj",
            InventoryItemKind.FoodCookedMeat => "Et",
            InventoryItemKind.FoodWildBerries => "Böğürt",
            InventoryItemKind.FoodCleanWater => "Su",
            InventoryItemKind.PlaceableCampfire => "Ateş",
            _ => "Eşya"
        };

        private static string GetShortMaterialBadgeName(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => "Taş",
            MaterialIdCatalog.Dirt => "Toprak",
            MaterialIdCatalog.Grass => "Çim",
            MaterialIdCatalog.Sand => "Kum",
            MaterialIdCatalog.Basalt => "Bazalt",
            MaterialIdCatalog.NaturalObsidian => "Obsid",
            MaterialIdCatalog.Cobblestone => "Kırıktaş",
            MaterialIdCatalog.OakWood => "Meşe",
            MaterialIdCatalog.Ironwood => "D.Ağaç",
            MaterialIdCatalog.Marble => "Mermer",
            MaterialIdCatalog.StoneBrick => "Tuğla",
            MaterialIdCatalog.LooseRubble => "Moloz",
            MaterialIdCatalog.WoodPlank => "Kalas",
            MaterialIdCatalog.CopperOre => "BakırCv",
            MaterialIdCatalog.IronOre => "DemirCv",
            MaterialIdCatalog.GoldOre => "AltınCv",
            MaterialIdCatalog.MithrilOre => "Mithril",
            _ => $"M#{matId}"
        };

        private void TriggerChannelForSlotItem(in VisualInventorySlot item, Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (item.IsEmpty) return;
            bool isCrystalMass = item.Kind == InventoryItemKind.VoxelBlock ||
                                 item.Kind == InventoryItemKind.OreChunk ||
                                 item.Kind == InventoryItemKind.MetalIngot;
            float dur = isCrystalMass ? 3.0f : 1.5f;
            string label = isCrystalMass
                ? "Canta <-> Madde Kristali Kutle Aktarim Kanali (3.0sn)"
                : "Canta -> Kilif Silah/Alet Kusanma Kanali (1.5sn)";
            StartEquipOrTransferChannel(dur, label);
            miningCtrl.LastFeedbackText = $"{label} baslatildi...";
        }

        private void HandleSlotMouseClick(
            ref VisualInventorySlot clickedSlot,
            int slotIndex,
            bool isHotbar,
            bool isChest,
            int mouseButton,
            bool isShift,
            Stage1MatterCrystalAndMiningController miningCtrl,
            VisualChestCasSystem chestCasSystem)
        {
            if (isShift && !clickedSlot.IsEmpty)
            {
                VisualInventorySlot moving = clickedSlot;
                bool moved;
                if (isChest)
                {
                    moved = TryMergeOrPlaceIntoArray(moving, BackpackSlots) || TryMergeOrPlaceIntoArray(moving, HotbarSlots);
                    if (moved)
                    {
                        chestCasSystem.TryWithdrawAtomic(slotIndex, (ushort)Mathf.Max(1, moving.Count), out _, out uint casVer);
                        miningCtrl.LastFeedbackText = $"CAS Sandiktan cekildi (Slot {slotIndex} | CAS Ver={casVer})";
                    }
                }
                else if (isHotbar)
                {
                    moved = TryMergeOrPlaceIntoArray(moving, BackpackSlots);
                }
                else
                {
                    moved = TryMergeOrPlaceIntoArray(moving, HotbarSlots);
                }

                if (moved)
                {
                    TriggerChannelForSlotItem(in moving, miningCtrl);
                    clickedSlot = default;
                    PushUiCountsToCrystalStorage(miningCtrl);
                }
                return;
            }

            if (mouseButton == 1)
            {
                if (CursorHeldSlot.IsEmpty && !clickedSlot.IsEmpty)
                {
                    if (IsArmorKind(clickedSlot.Kind))
                    {
                        int targetSlot = GetArmorSlotIndexForItemKind(clickedSlot.Kind);
                        if (targetSlot >= 0)
                        {
                            VisualInventorySlot old = EquippedArmorSlots[targetSlot];
                            EquippedArmorSlots[targetSlot] = clickedSlot;
                            EquippedArmorSlots[targetSlot].Count = 1;
                            clickedSlot = old;
                            if (isChest)
                            {
                                if (!old.IsEmpty) chestCasSystem.InitializeSlot(slotIndex, old.SubId, (ushort)old.Count);
                                else chestCasSystem.TryWithdrawAtomic(slotIndex, 1, out _, out _);
                            }
                            miningCtrl.LastFeedbackText = $"🛡️ Kuşanıldı: {GetArmorSlotName(targetSlot)} ({GetArmorTierName(EquippedArmorSlots[targetSlot].SubId)})";
                            PushUiCountsToCrystalStorage(miningCtrl);
                            return;
                        }
                    }

                    int takeHalf = Mathf.Max(1, (clickedSlot.Count + 1) / 2);
                    CursorHeldSlot = clickedSlot;
                    CursorHeldSlot.Count = takeHalf;
                    DragOriginIsHotbar = isHotbar;
                    DragOriginSlotIndex = slotIndex;
                    clickedSlot.Count -= takeHalf;
                    if (isChest) chestCasSystem.TryWithdrawAtomic(slotIndex, (ushort)takeHalf, out _, out _);
                    if (clickedSlot.Count <= 0) clickedSlot = default;
                }
                else if (!CursorHeldSlot.IsEmpty)
                {
                    if (clickedSlot.IsEmpty)
                    {
                        clickedSlot = CursorHeldSlot;
                        clickedSlot.Count = 1;
                        CursorHeldSlot.Count--;
                        if (CursorHeldSlot.Count <= 0) CursorHeldSlot = default;
                    }
                    else if (clickedSlot.CanStackWith(in CursorHeldSlot))
                    {
                        clickedSlot.Count++;
                        CursorHeldSlot.Count--;
                        if (CursorHeldSlot.Count <= 0) CursorHeldSlot = default;
                    }
                    if (isChest) chestCasSystem.InitializeSlot(slotIndex, clickedSlot.SubId, (ushort)clickedSlot.Count);
                    TriggerChannelForSlotItem(in clickedSlot, miningCtrl);
                }
                PushUiCountsToCrystalStorage(miningCtrl);
                return;
            }

            if (CursorHeldSlot.IsEmpty)
            {
                if (!clickedSlot.IsEmpty)
                {
                    CursorHeldSlot = clickedSlot;
                    DragOriginIsHotbar = isHotbar;
                    DragOriginSlotIndex = slotIndex;
                    if (isChest) chestCasSystem.TryWithdrawAtomic(slotIndex, (ushort)Mathf.Max(1, clickedSlot.Count), out _, out _);
                    clickedSlot = default;
                }
            }
            else
            {
                VisualInventorySlot placed = CursorHeldSlot;
                if (clickedSlot.IsEmpty)
                {
                    clickedSlot = CursorHeldSlot;
                    CursorHeldSlot = default;
                }
                else if (clickedSlot.CanStackWith(in CursorHeldSlot))
                {
                    clickedSlot.Count += CursorHeldSlot.Count;
                    CursorHeldSlot = default;
                }
                else
                {
                    VisualInventorySlot temp = clickedSlot;
                    clickedSlot = CursorHeldSlot;
                    CursorHeldSlot = temp;
                }
                if (isChest) chestCasSystem.InitializeSlot(slotIndex, clickedSlot.SubId, (ushort)clickedSlot.Count);
                TriggerChannelForSlotItem(in placed, miningCtrl);
            }

            PushUiCountsToCrystalStorage(miningCtrl);
        }

        private static bool TryMergeOrPlaceIntoArray(in VisualInventorySlot item, VisualInventorySlot[] targetArray)
        {
            for (int i = 0; i < targetArray.Length; i++)
            {
                if (targetArray[i].CanStackWith(in item))
                {
                    targetArray[i].Count += item.Count;
                    return true;
                }
            }
            for (int i = 0; i < targetArray.Length; i++)
            {
                if (targetArray[i].IsEmpty)
                {
                    targetArray[i] = item;
                    return true;
                }
            }
            return false;
        }

        private void ReturnCursorHeldItemToInventory(Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (CursorHeldSlot.IsEmpty) return;
            if (!TryMergeOrPlaceIntoArray(in CursorHeldSlot, BackpackSlots))
            {
                TryMergeOrPlaceIntoArray(in CursorHeldSlot, HotbarSlots);
            }
            CursorHeldSlot = default;
            DragOriginSlotIndex = -1;
            PushUiCountsToCrystalStorage(miningCtrl);
        }

        private void DrawSingleSlotBox(
            Rect rect,
            in VisualInventorySlot slot,
            string keyLabel,
            bool isSelected,
            bool isEmptyHandIndicator,
            Stage1DirectionalCombatBridge combatBridge,
            Vector2 mousePos)
        {
            bool hovered = rect.Contains(mousePos);
            Texture2D bg = isSelected ? _slotSelectedBgTex! : (hovered ? _slotHoverBgTex! : _slotBgTex!);
            GUI.DrawTexture(rect, bg);

            if (isSelected)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1.0f, 0.84f, 0.22f, 0.95f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), _whitePixelTex!);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), _whitePixelTex!);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), _whitePixelTex!);
                GUI.DrawTexture(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), _whitePixelTex!);
                GUI.color = prev;
            }

            if (isEmptyHandIndicator)
            {
                Texture2D handIcon = GetOrBuildIconForSlot(default, combatBridge.ActiveWeaponArchetype);
                GUI.DrawTexture(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f), handIcon);
            }
            else if (!slot.IsEmpty)
            {
                Texture2D icon = GetOrBuildIconForSlot(in slot, combatBridge.ActiveWeaponArchetype);
                GUI.DrawTexture(new Rect(rect.x + 7f, rect.y + 6f, rect.width - 14f, rect.height - 14f), icon);

                if (slot.Count > 1 || slot.Kind == InventoryItemKind.VoxelBlock || slot.Kind == InventoryItemKind.OreChunk || slot.Kind == InventoryItemKind.MetalIngot)
                {
                    GUI.Label(new Rect(rect.x + 4f, rect.y + rect.height - 20f, rect.width - 7f, 18f), GetCachedStackCountString(slot.Count), _countLabelStyle!);
                }

                if (slot.Kind == InventoryItemKind.WeaponSword ||
                    slot.Kind == InventoryItemKind.ToolPickaxe ||
                    slot.Kind == InventoryItemKind.ToolAxe ||
                    slot.Kind == InventoryItemKind.ToolMicroChisel)
                {
                    float dur = Mathf.Clamp01(slot.Durability01);
                    Rect barBg = new Rect(rect.x + 6f, rect.yMax - 6f, rect.width - 12f, 3f);
                    Rect barFg = new Rect(rect.x + 6f, rect.yMax - 6f, (rect.width - 12f) * dur, 3f);
                    Color prev = GUI.color;
                    GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
                    GUI.DrawTexture(barBg, _whitePixelTex!);
                    GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.15f), new Color(0.25f, 0.95f, 0.45f), dur);
                    GUI.DrawTexture(barFg, _whitePixelTex!);
                    GUI.color = prev;
                }
            }

            if (!string.IsNullOrEmpty(keyLabel))
            {
                GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 24f, 16f), keyLabel, _keyBadgeStyle!);
            }

            if (hovered && (!slot.IsEmpty || isEmptyHandIndicator))
            {
                HoveredSlotTooltip = FormatSlotTooltip(in slot, isEmptyHandIndicator, combatBridge);
            }
        }

        public string FormatSelectedSlotBannerText(
            Stage1MatterCrystalAndMiningController miningCtrl,
            Stage1DirectionalCombatBridge combatBridge)
        {
            if (miningCtrl.IsCrystalActive)
            {
                return $"[C] MADDE KRISTALI AKTIF (T{miningCtrl.CurrentCrystalTier} | {miningCtrl.BrushAreaLabel}) — [Sol Tik] Vakumla | [Sag Tik] Insa Et ({Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(miningCtrl.SelectedMaterialId)})";
            }

            if (SelectedHotbarIndex == -1)
            {
                return "[0] BOS EL — Yumusak Toprak/Yaprak Kazma & [E] Dunya/Sandik Etkilesimi ([1..9] veya Tekerlek ile Kilif Sec)";
            }

            VisualInventorySlot slot = GetActiveHotbarSlot();
            if (slot.IsEmpty)
            {
                return $"[{SelectedHotbarIndex + 1}] BOS KILIF YUVASI — [I] 3B Kumas Cantayi acip bu yuvaya 3B esya surukleyin!";
            }

            return $"[{SelectedHotbarIndex + 1}] {GetSlotDisplayName(in slot, combatBridge)} (Adet: {slot.Count}) — {GetSlotUsageHint(in slot)}";
        }

        public static string GetSlotDisplayName(in VisualInventorySlot slot, Stage1DirectionalCombatBridge? combatBridge = null) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => combatBridge != null ? combatBridge.GetWeaponDisplayName() : "Celik Uzun Kilic (1.85 kg)",
            InventoryItemKind.ToolPickaxe => "Demir Kazma (2.40 kg)",
            InventoryItemKind.ToolAxe => "Demir Balta (2.10 kg)",
            InventoryItemKind.ToolMicroChisel => "Mimari Mikro-Keski (4x4x4 Yontma)",
            InventoryItemKind.ToolFluidFlask => slot.SubId switch
            {
                1 => "Kristal Su Matarasi (Akiskan Su)",
                2 => "Volkanik Lav Matarasi (Akiskan Lav)",
                _ => "Bos Kristal Matara"
            },
            InventoryItemKind.VoxelBlock or InventoryItemKind.OreChunk => Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId),
            InventoryItemKind.MetalIngot => slot.SubId switch
            {
                1 => "Saf Bakir Kulce",
                2 => "Saf Demir Kulce",
                3 => "Saf Mithril Kulce",
                _ => "Metal Kulce"
            },
            InventoryItemKind.RoughStoneChunk => $"Kaba {Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId)} Parçası",
            InventoryItemKind.LooseDirtClod => "Gevşek Toprak Yığını",
            InventoryItemKind.SplitFirewood => $"{Stage1MatterCrystalAndMiningController.GetMaterialDisplayName(slot.SubId)} Yarma Odun",
            InventoryItemKind.Stick => "Kuru Dal (Çalı/Odun Parçası)",
            InventoryItemKind.ArmorHelmet => slot.SubId switch
            {
                1 => "Deri Miğfer (0.4 kg)",
                2 => "Ahşap Savaş Miğferi (0.8 kg)",
                3 => "Dövme Bakır Miğfer (1.4 kg)",
                _ => "Dövme Demir Miğfer (1.2 kg)"
            },
            InventoryItemKind.ArmorChestplate => slot.SubId switch
            {
                1 => "Deri Zırh Yeleği (1.8 kg)",
                2 => "Ahşap Plaka Göğüslük (2.8 kg)",
                3 => "Dövme Bakır Göğüslük (5.2 kg)",
                _ => "Dövme Demir Göğüslük (4.5 kg)"
            },
            InventoryItemKind.ArmorVambrace => slot.SubId switch
            {
                1 => "Deri Ön Kolçak (0.4 kg)",
                _ => "Demir Plaka Ön Kolçak (1.0 kg)"
            },
            InventoryItemKind.ArmorGreaves => slot.SubId switch
            {
                1 => "Deri Çizme / Dizlik (0.9 kg)",
                _ => "Demir Plaka Dizlik ve Çizme (2.2 kg)"
            },
            InventoryItemKind.PotionHealth => "Şifalı Can İksiri (+%45 Can)",
            InventoryItemKind.PotionStamina => "Dayanıklılık / Enerji İksiri (+%70 Enerji)",
            InventoryItemKind.MedicalBandage => "Tıbbi Bandaj (Kanamayı Durdurur & +%20 Can)",
            InventoryItemKind.FoodCookedMeat => "Közde Pişmiş Et (+%35 Can & +%50 Enerji)",
            InventoryItemKind.FoodWildBerries => "Yaban Orman Böğürtleni (+%10 Can & +%20 Enerji)",
            InventoryItemKind.FoodCleanWater => "Arıtılmış Temiz Su (+%40 Enerji & Ferahlama)",
            InventoryItemKind.PlaceableCampfire => "Kamp Ateşi Kiti (Isınma, Işık ve Pişirme)",
            _ => "Bos Yuva"
        };

        private static string GetSlotUsageHint(in VisualInventorySlot slot) => slot.Kind switch
        {
            InventoryItemKind.WeaponSword => "[Sol Tik+Fare] Yonsel Kesik | [Sag Tik+Fare] Capraz Blok & Saptirma | [G] Stil",
            InventoryItemKind.ToolPickaxe => "[Sol Tik+Fare] Kaya ve Cevher Damari Kirma",
            InventoryItemKind.ToolAxe => "[Sol Tik+Fare] Agac Govdesi Devirme & Ahsap Kirma",
            InventoryItemKind.ToolMicroChisel => "[Sol Tik] 4x4x4 Mikro-Voksel Yont | [Sag Tik] Mikro-Voksel Ekle",
            InventoryItemKind.ToolFluidFlask => "[Sol Tik] Su/Lav Doldur | [Sag Tik] Dunyaya Akiskan Su/Lav Dok",
            InventoryItemKind.VoxelBlock => "[Sag Tik] Dunyaya Blok Yerlestir | [V] Mimari Sekil Degistir | [R] 24-Yon Dondur",
            InventoryItemKind.OreChunk => "[Sag Tik] Dunyaya Cevher Koy veya Ors/Izabe Ocaginda Islet",
            InventoryItemKind.MetalIngot => "[Sag Tik] Dunyaya Kulce Koy veya 16x32x4 Ors Uzerinde Silah/Zirh Dov",
            InventoryItemKind.RoughStoneChunk => "Kaba Madencilik Hammaddesi (Doğrudan blok olarak konulamaz! Tezgahta işleyin)",
            InventoryItemKind.LooseDirtClod => "Gevşek Kazı Hammaddesi (Doğrudan blok olarak konulamaz)",
            InventoryItemKind.SplitFirewood => "Ateş / Kömür / Tezgah Hammaddesi (Doğrudan blok olarak konulamaz)",
            InventoryItemKind.Stick => "Alet Sapı / Meşale / Çerçeve Hammaddesi",
            InventoryItemKind.ArmorHelmet => "[Sağ Tık] Başa Kuşan / Çıkar (Kafa Hasar Koruması)",
            InventoryItemKind.ArmorChestplate => "[Sağ Tık] Göğse Kuşan / Çıkar (Gövde Darbe Direnci)",
            InventoryItemKind.ArmorVambrace => "[Sağ Tık] Kollara Kuşan / Çıkar (Kol Koruması)",
            InventoryItemKind.ArmorGreaves => "[Sağ Tık] Bacaklara Kuşan / Çıkar (Bacak ve Ayak Koruması)",
            InventoryItemKind.PotionHealth => "[Sol Tık] İç (+45 HP Can Yeniler)",
            InventoryItemKind.PotionStamina => "[Sol Tık] İç (+70 EP Enerji Yeniler)",
            InventoryItemKind.MedicalBandage => "[Sol Tık] Sar (Kanamayı Durdurur, +20 HP)",
            InventoryItemKind.FoodCookedMeat => "[Sol Tık] Ye (+35 HP, +50 EP, Tokluk Sağlar)",
            InventoryItemKind.FoodWildBerries => "[Sol Tık] Ye (+10 HP, +20 EP)",
            InventoryItemKind.FoodCleanWater => "[Sol Tık] İç (Susuzluğu Giderir, +40 EP)",
            InventoryItemKind.PlaceableCampfire => "[Sağ Tık] Yere Kur (Işık, Isınma ve Pişirme Alanı)",
            _ => string.Empty
        };

        private static string FormatSlotTooltip(
            in VisualInventorySlot slot,
            bool isEmptyHand,
            Stage1DirectionalCombatBridge combatBridge)
        {
            if (isEmptyHand)
            {
                return "[0] BOS EL\nCiplak elle yumusak toprak kazma, [E] ile yerden esya/sandik/ors etkilesimi.";
            }
            string name = GetSlotDisplayName(in slot, combatBridge);
            string hint = GetSlotUsageHint(in slot);
            return $"{name}  [Miktar: x{slot.Count}]\n{hint}";
        }

        private void EnsureTexturesAndStyles()
        {
            if (_iconsInitialized && _slotBgTex != null && _whitePixelTex != null) return;
            _iconsInitialized = true;

            _whitePixelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "ProcUI_White1x1" };
            _whitePixelTex.SetPixel(0, 0, Color.white);
            _whitePixelTex.Apply(false, false);

            _slotBgTex = BuildFramedBoxTexture(48, 48, new Color(0.11f, 0.13f, 0.17f, 0.90f), new Color(0.32f, 0.36f, 0.44f, 0.95f));
            _slotHoverBgTex = BuildFramedBoxTexture(48, 48, new Color(0.18f, 0.22f, 0.30f, 0.94f), new Color(0.65f, 0.78f, 0.95f, 1.0f));
            _slotSelectedBgTex = BuildFramedBoxTexture(48, 48, new Color(0.22f, 0.20f, 0.12f, 0.96f), new Color(1.0f, 0.82f, 0.22f, 1.0f));
            _windowBgTex = BuildFramedBoxTexture(32, 32, new Color(0.06f, 0.08f, 0.11f, 0.92f), new Color(0.28f, 0.34f, 0.42f, 0.95f));

            _countLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerRight,
                normal = { textColor = new Color(1.0f, 0.95f, 0.55f, 1.0f) }
            };
            _keyBadgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.72f, 0.82f, 0.94f, 0.90f) }
            };
            _windowTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.95f, 0.88f, 0.55f, 1.0f) }
            };
            _slotTitleBannerStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.88f, 0.96f, 1.0f, 1.0f) }
            };
            _tooltipBoxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = new Color(1.0f, 0.94f, 0.65f, 1.0f) }
            };
            _vitalsLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.96f, 0.98f, 1.0f, 1.0f) }
            };
        }

        private void DrawVitalsHudBars(
            float screenW, float barY,
            float currentHp, float maxHp,
            float stamina, float maxStamina,
            bool isExhaustedLock,
            float lungOxygenPercent, bool isSubmerged,
            float coreBodyTemp)
        {
            const float totalBarWidth = 580f;
            float startX = (screenW - totalBarWidth) * 0.5f;
            float currentY = barY - 17f;

            // 1. Oksijen Barı (Suya batınca veya oksijen <%98 olunca görünür)
            if (isSubmerged || lungOxygenPercent < 98f)
            {
                currentY -= 15f;
                Rect oxyRect = new Rect(startX, currentY, totalBarWidth, 12f);
                float oxy01 = Mathf.Clamp01(lungOxygenPercent / 100f);
                Color oxyFill = oxy01 < 0.25f ? new Color(0.95f, 0.22f, 0.22f, 1.0f) : new Color(0.18f, 0.78f, 0.95f, 1.0f);
                int oxyInt = Mathf.RoundToInt(lungOxygenPercent);
                if (oxyInt != _lastOxyPctInt)
                {
                    _lastOxyPctInt = oxyInt;
                    _cachedOxyLabel = $"🤿 OKSİJEN %{oxyInt}";
                }
                DrawHorizontalBar(oxyRect, oxy01, oxyFill, new Color(0.04f, 0.12f, 0.20f, 0.88f), new Color(0.25f, 0.50f, 0.70f, 0.95f),
                    _cachedOxyLabel);
                currentY += 15f;
            }

            // 2. Can (HP) ve Dayanıklılık / Yorgunluk (EP) Barları Yan Yana
            float halfWidth = (totalBarWidth - 8f) * 0.5f;

            // Sol Bar: Can (HP)
            Rect hpRect = new Rect(startX, currentY, halfWidth, 13f);
            float hp01 = maxHp > 0.1f ? Mathf.Clamp01(currentHp / maxHp) : 1f;
            Color hpFill = hp01 < 0.28f ? new Color(0.95f, 0.15f, 0.15f, 1.0f) : new Color(0.85f, 0.18f, 0.20f, 1.0f);
            int hpCurInt = Mathf.RoundToInt(currentHp);
            int hpMaxInt = Mathf.RoundToInt(maxHp);
            if (hpCurInt != _lastHpCurInt || hpMaxInt != _lastHpMaxInt)
            {
                _lastHpCurInt = hpCurInt;
                _lastHpMaxInt = hpMaxInt;
                _cachedHpLabel = $"❤️ CAN %{Mathf.RoundToInt(hp01 * 100f)} ({hpCurInt}/{hpMaxInt})";
            }
            DrawHorizontalBar(hpRect, hp01, hpFill, new Color(0.20f, 0.05f, 0.05f, 0.88f), new Color(0.55f, 0.18f, 0.18f, 0.95f),
                _cachedHpLabel);

            // Sağ Bar: Dayanıklılık / Yorgunluk (EP / Stamina)
            Rect stamRect = new Rect(startX + halfWidth + 8f, currentY, halfWidth, 13f);
            float stam01 = maxStamina > 0.1f ? Mathf.Clamp01(stamina / maxStamina) : 1f;
            int stamPctInt = Mathf.RoundToInt(stam01 * 100f);

            Color stamFill = isExhaustedLock
                ? new Color(0.98f, 0.58f, 0.12f, 1.0f)
                : (stam01 < 0.25f ? new Color(0.92f, 0.80f, 0.18f, 1.0f) : new Color(0.22f, 0.84f, 0.30f, 1.0f));
            if (stamPctInt != _lastStamPctInt || isExhaustedLock != _lastStamExhausted)
            {
                _lastStamPctInt = stamPctInt;
                _lastStamExhausted = isExhaustedLock;
                _cachedStamLabel = isExhaustedLock
                    ? $"⚡ NEFESSİZ! (DİNLENİYOR) %{stamPctInt}"
                    : $"⚡ ENERJİ (EP) %{stamPctInt}";
            }
            DrawHorizontalBar(stamRect, stam01, stamFill, new Color(0.06f, 0.16f, 0.08f, 0.88f), new Color(0.22f, 0.48f, 0.26f, 0.95f), _cachedStamLabel);
        }

        private void DrawHorizontalBar(Rect rect, float fill01, Color fillColor, Color bgColor, Color borderColor, string labelText)
        {
            if (_whitePixelTex == null) return;
            fill01 = Mathf.Clamp01(fill01);

            Color oldColor = GUI.color;

            // Kenarlık (Border)
            GUI.color = borderColor;
            GUI.DrawTexture(rect, _whitePixelTex);

            // Arka plan (Background)
            Rect bgRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            GUI.color = bgColor;
            GUI.DrawTexture(bgRect, _whitePixelTex);

            // Doluluk (Fill)
            if (fill01 > 0.005f)
            {
                Rect fillRect = new Rect(bgRect.x, bgRect.y, bgRect.width * fill01, bgRect.height);
                GUI.color = fillColor;
                GUI.DrawTexture(fillRect, _whitePixelTex);
            }

            GUI.color = oldColor;

            // Metin etiketi
            if (!string.IsNullOrEmpty(labelText) && _vitalsLabelStyle != null)
            {
                GUI.Label(rect, labelText, _vitalsLabelStyle);
            }
        }

        private static Texture2D BuildFramedBoxTexture(int w, int h, Color fill, Color border)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "ProcUI_SlotBox",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = x < 2 || x >= w - 2 || y < 2 || y >= h - 2;
                    bool isInnerShadow = !isBorder && (x == 2 || y == h - 3);
                    Color c = isBorder ? border : (isInnerShadow ? fill * 0.72f : fill);
                    c.a = isBorder ? border.a : fill.a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply(false, false);
            return tex;
        }

        private Texture2D GetOrBuildIconForSlot(in VisualInventorySlot slot, CombatWeaponArchetype activeSwordArchetype)
        {
            int key = ((int)slot.Kind << 16) | (slot.SubId << 8) | (slot.Kind == InventoryItemKind.WeaponSword ? (int)activeSwordArchetype : 0);
            if (_iconCache.TryGetValue(key, out Texture2D? existing) && existing != null)
            {
                return existing;
            }

            Texture2D created = BuildProceduralItemIcon(in slot, activeSwordArchetype);
            _iconCache[key] = created;
            return created;
        }

        private static Texture2D BuildProceduralItemIcon(in VisualInventorySlot slot, CombatWeaponArchetype swordArch)
        {
            const int S = 48;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                name = $"ProcIcon_{slot.Kind}_{slot.SubId}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    tex.SetPixel(x, y, clear);

            switch (slot.Kind)
            {
                case InventoryItemKind.Empty: DrawEmptyHandIcon(tex); break;
                case InventoryItemKind.WeaponSword: DrawSwordIcon(tex, swordArch); break;
                case InventoryItemKind.ToolPickaxe: DrawPickaxeIcon(tex); break;
                case InventoryItemKind.ToolAxe: DrawAxeIcon(tex); break;
                case InventoryItemKind.ToolMicroChisel: DrawChiselIcon(tex); break;
                case InventoryItemKind.ToolFluidFlask: DrawFluidFlaskIcon(tex, slot.SubId); break;
                case InventoryItemKind.VoxelBlock: DrawIsometricVoxelCubeIcon(tex, slot.SubId); break;
                case InventoryItemKind.OreChunk: DrawOreChunkIcon(tex, slot.SubId); break;
                case InventoryItemKind.MetalIngot: DrawIngotIcon(tex, slot.SubId); break;
                case InventoryItemKind.RoughStoneChunk: DrawRoughStoneIcon(tex, slot.SubId); break;
                case InventoryItemKind.LooseDirtClod: DrawLooseDirtIcon(tex); break;
                case InventoryItemKind.SplitFirewood: DrawFirewoodIcon(tex, slot.SubId); break;
                case InventoryItemKind.Stick: DrawStickIcon(tex); break;
                case InventoryItemKind.ArmorHelmet: DrawHelmetIcon(tex, slot.SubId); break;
                case InventoryItemKind.ArmorChestplate: DrawChestplateIcon(tex, slot.SubId); break;
                case InventoryItemKind.ArmorVambrace: DrawVambraceIcon(tex, slot.SubId); break;
                case InventoryItemKind.ArmorGreaves: DrawGreavesIcon(tex, slot.SubId); break;
                case InventoryItemKind.PotionHealth: DrawPotionBottleIcon(tex, new Color(0.88f, 0.18f, 0.18f, 1f)); break;
                case InventoryItemKind.PotionStamina: DrawPotionBottleIcon(tex, new Color(0.22f, 0.82f, 0.32f, 1f)); break;
                case InventoryItemKind.MedicalBandage: DrawBandageIcon(tex); break;
                case InventoryItemKind.FoodCookedMeat: DrawCookedMeatIcon(tex); break;
                case InventoryItemKind.FoodWildBerries: DrawBerriesIcon(tex); break;
                case InventoryItemKind.FoodCleanWater: DrawCleanWaterIcon(tex); break;
                case InventoryItemKind.PlaceableCampfire: DrawCampfireIcon(tex); break;
            }

            tex.Apply(false, false);
            return tex;
        }

        private static void DrawEmptyHandIcon(Texture2D tex)
        {
            Color skin = new Color(0.84f, 0.68f, 0.54f, 0.85f);
            Color leather = new Color(0.36f, 0.24f, 0.16f, 0.95f);
            FillRect(tex, 16, 10, 16, 16, skin);
            FillRect(tex, 15, 6, 18, 6, leather);
            FillRect(tex, 16, 26, 3, 12, skin);
            FillRect(tex, 20, 26, 3, 14, skin);
            FillRect(tex, 24, 26, 3, 13, skin);
            FillRect(tex, 28, 26, 3, 10, skin);
            FillRect(tex, 11, 16, 5, 8, skin);
        }

        private static void DrawSwordIcon(Texture2D tex, CombatWeaponArchetype arch)
        {
            Color bladeBright = arch == CombatWeaponArchetype.Katana ? new Color(0.85f, 0.95f, 1.0f, 1f) : new Color(0.86f, 0.89f, 0.94f, 1f);
            Color bladeEdge = new Color(0.55f, 0.62f, 0.72f, 1f);
            Color guardGold = arch == CombatWeaponArchetype.Rapier ? new Color(0.95f, 0.78f, 0.25f, 1f) : new Color(0.78f, 0.64f, 0.22f, 1f);
            Color gripLeather = new Color(0.42f, 0.22f, 0.14f, 1f);

            DrawThickLine(tex, 7, 7, 15, 15, 2, gripLeather);
            FillRect(tex, 5, 5, 4, 4, guardGold);

            if (arch == CombatWeaponArchetype.Rapier)
            {
                DrawThickLine(tex, 11, 19, 19, 11, 3, guardGold);
                DrawThickLine(tex, 13, 13, 18, 20, 2, guardGold);
            }
            else if (arch == CombatWeaponArchetype.Katana)
            {
                FillRect(tex, 13, 13, 6, 6, guardGold);
            }
            else
            {
                DrawThickLine(tex, 10, 21, 21, 10, 2, guardGold);
            }

            int thickness = arch == CombatWeaponArchetype.Greatsword ? 3 : (arch == CombatWeaponArchetype.Rapier ? 1 : 2);
            DrawThickLine(tex, 16, 16, 42, 42, thickness, bladeBright);
            DrawThickLine(tex, 17, 15, 42, 40, 1, bladeEdge);

            if (arch == CombatWeaponArchetype.Greatsword)
            {
                DrawThickLine(tex, 20, 24, 24, 20, 2, guardGold);
            }
        }

        private static void DrawPickaxeIcon(Texture2D tex)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            Color steel = new Color(0.74f, 0.80f, 0.88f, 1f);
            Color edge = new Color(0.92f, 0.96f, 1.0f, 1f);
            DrawThickLine(tex, 8, 8, 35, 35, 2, wood);
            DrawThickLine(tex, 22, 38, 36, 36, 3, steel);
            DrawThickLine(tex, 36, 36, 38, 22, 3, steel);
            DrawThickLine(tex, 18, 40, 23, 38, 2, edge);
            DrawThickLine(tex, 38, 23, 40, 18, 2, edge);
        }

        private static void DrawAxeIcon(Texture2D tex)
        {
            Color wood = new Color(0.58f, 0.38f, 0.20f, 1f);
            Color steel = new Color(0.72f, 0.78f, 0.86f, 1f);
            Color brightEdge = new Color(0.94f, 0.97f, 1.0f, 1f);
            DrawThickLine(tex, 8, 8, 36, 36, 2, wood);
            DrawThickLine(tex, 28, 32, 38, 24, 4, steel);
            DrawThickLine(tex, 26, 28, 36, 18, 4, steel);
            DrawThickLine(tex, 38, 26, 35, 16, 2, brightEdge);
        }

        private static void DrawChiselIcon(Texture2D tex)
        {
            Color handle = new Color(0.68f, 0.34f, 0.18f, 1f);
            Color ferrule = new Color(0.85f, 0.72f, 0.28f, 1f);
            Color steel = new Color(0.80f, 0.88f, 0.96f, 1f);
            DrawThickLine(tex, 9, 9, 22, 22, 3, handle);
            DrawThickLine(tex, 22, 22, 25, 25, 3, ferrule);
            DrawThickLine(tex, 25, 25, 40, 40, 2, steel);
            FillRect(tex, 8, 30, 5, 5, steel);
            FillRect(tex, 15, 30, 5, 5, ferrule);
            FillRect(tex, 8, 37, 5, 5, ferrule);
        }

        private static void DrawFluidFlaskIcon(Texture2D tex, byte subId)
        {
            Color glass = new Color(0.75f, 0.88f, 0.96f, 0.85f);
            Color cork = new Color(0.62f, 0.42f, 0.24f, 1f);
            Color liquid = subId == 1
                ? new Color(0.18f, 0.58f, 0.98f, 0.98f)
                : (subId == 2 ? new Color(1.0f, 0.42f, 0.08f, 0.98f) : new Color(0.25f, 0.30f, 0.36f, 0.4f));

            FillRect(tex, 20, 36, 8, 5, cork);
            FillRect(tex, 18, 30, 12, 6, glass);
            FillRect(tex, 12, 8, 24, 22, glass);
            if (subId != 0)
            {
                FillRect(tex, 14, 10, 20, 16, liquid);
                FillRect(tex, 16, 22, 6, 3, Color.Lerp(liquid, Color.white, 0.45f));
            }
        }

        private static void DrawIsometricVoxelCubeIcon(Texture2D tex, byte matId)
        {
            Color baseCol = GetMaterialIconColor(matId);
            Color topCol = Color.Lerp(baseCol, Color.white, 0.22f);
            Color leftCol = baseCol * 0.82f; leftCol.a = 1f;
            Color rightCol = baseCol * 0.60f; rightCol.a = 1f;

            for (int y = 6; y <= 42; y++)
            {
                for (int x = 8; x <= 40; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 24f;

                    if (dy >= Mathf.Abs(dx) * 0.5f && dy <= 16f - Mathf.Abs(dx) * 0.5f)
                    {
                        Color c = (matId == MaterialIdCatalog.Dirt) ? new Color(0.32f, 0.68f, 0.26f, 1f) : topCol;
                        if (((x + y) & 3) == 0) c *= 0.93f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                    else if (dx < 0f && dx >= -16f && dy < -dx * 0.5f && dy >= -16f - dx * 0.5f)
                    {
                        Color c = leftCol;
                        if (matId == MaterialIdCatalog.OakWood || matId == MaterialIdCatalog.Ironwood)
                        {
                            if ((x & 3) == 0) c *= 0.84f;
                        }
                        else if (((x * 3 + y * 5) & 7) == 0)
                        {
                            c *= 0.88f;
                        }
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                    else if (dx >= 0f && dx <= 16f && dy < dx * 0.5f && dy >= -16f + dx * 0.5f)
                    {
                        Color c = rightCol;
                        if (((x * 5 + y * 3) & 7) == 0) c *= 0.88f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static void DrawOreChunkIcon(Texture2D tex, byte matId)
        {
            Color rock = new Color(0.42f, 0.44f, 0.48f, 1f);
            Color vein = GetMaterialIconColor(matId);
            for (int y = 10; y <= 38; y++)
            {
                for (int x = 10; x <= 38; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(24f, 24f));
                    if (d <= 14.5f)
                    {
                        bool isVein = ((x * 7 + y * 13) % 11) < 3 && d < 11.5f;
                        tex.SetPixel(x, y, isVein ? vein : rock * (0.75f + (y - 10f) / 60f));
                    }
                }
            }
        }

        private static void DrawIngotIcon(Texture2D tex, byte ingotType)
        {
            Color col = ingotType switch
            {
                1 => new Color(0.92f, 0.52f, 0.24f, 1f),
                2 => new Color(0.78f, 0.82f, 0.88f, 1f),
                _ => new Color(0.35f, 0.88f, 0.98f, 1f)
            };
            FillRect(tex, 10, 14, 28, 12, col * 0.78f);
            FillRect(tex, 13, 20, 22, 8, col);
            FillRect(tex, 15, 24, 18, 3, Color.Lerp(col, Color.white, 0.45f));
        }

        private static void DrawRoughStoneIcon(Texture2D tex, byte matId)
        {
            Color stoneCol = GetMaterialIconColor(matId);
            Color darkChip = stoneCol * 0.65f; darkChip.a = 1f;
            Color lightFacet = Color.Lerp(stoneCol, Color.white, 0.28f);

            // Pürüzlü, köşeli kaba taş parçası
            for (int y = 10; y <= 36; y++)
            {
                for (int x = 10; x <= 38; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 22f;
                    // Düzensiz çokgen sınırı
                    float radius = 13.0f + Mathf.Sin(dx * 1.7f + dy * 0.9f) * 2.2f + Mathf.Cos(dy * 2.1f) * 1.5f;
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        Color c = stoneCol;
                        if (dy > dx * 0.4f + 2f) c = lightFacet;
                        else if (dy < -2f) c = darkChip;
                        if (((x * 5 + y * 7) & 7) == 0) c *= 0.88f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static void DrawLooseDirtIcon(Texture2D tex)
        {
            Color earthDark = new Color(0.38f, 0.25f, 0.15f, 1f);
            Color earthMid = new Color(0.52f, 0.36f, 0.22f, 1f);
            Color earthLight = new Color(0.64f, 0.48f, 0.30f, 1f);

            // Gevşek toprak yığını - tabanı geniş tepe
            for (int y = 10; y <= 32; y++)
            {
                int halfW = Mathf.RoundToInt((34 - y) * 0.65f) + 2;
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    if (x < 6 || x > 42) continue;
                    float n = Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                    Color c = n > 0.6f ? earthLight : (n > 0.35f ? earthMid : earthDark);
                    tex.SetPixel(x, y, c);
                }
            }
            // Üstüne ufak döküntü toprak parçacıkları
            FillRect(tex, 22, 33, 4, 3, earthMid);
            FillRect(tex, 17, 12, 3, 2, earthLight);
            FillRect(tex, 28, 14, 3, 2, earthDark);
        }

        private static void DrawFirewoodIcon(Texture2D tex, byte matId)
        {
            Color bark = matId == MaterialIdCatalog.Ironwood ? new Color(0.28f, 0.22f, 0.18f, 1f) : new Color(0.38f, 0.24f, 0.14f, 1f);
            Color splitWood = matId == MaterialIdCatalog.Ironwood ? new Color(0.55f, 0.45f, 0.36f, 1f) : new Color(0.72f, 0.54f, 0.32f, 1f);
            Color heartWood = Color.Lerp(splitWood, Color.white, 0.22f);

            // İki yarma odun parçası (çapraz ve paralel kütük dilimleri)
            // 1. Odun (sol yatık)
            DrawThickLine(tex, 10, 14, 34, 32, 4, bark);
            DrawThickLine(tex, 12, 17, 34, 32, 2, splitWood);
            DrawThickLine(tex, 13, 20, 32, 32, 1, heartWood);

            // 2. Odun (altına dayalı)
            DrawThickLine(tex, 14, 10, 38, 22, 3, bark);
            DrawThickLine(tex, 16, 12, 38, 22, 1, splitWood);
        }

        private static void DrawStickIcon(Texture2D tex)
        {
            Color wood = new Color(0.48f, 0.32f, 0.18f, 1f);
            Color highlight = new Color(0.62f, 0.44f, 0.26f, 1f);

            // Kuru dal / sopa: sol alttan sağ üste uzanan çatallı dal
            DrawThickLine(tex, 10, 8, 38, 38, 2, wood);
            DrawThickLine(tex, 11, 9, 37, 37, 1, highlight);
            // Yan dal budağı
            DrawThickLine(tex, 24, 23, 34, 18, 1, wood);
        }

        private static Color GetMaterialIconColor(byte matId) => matId switch
        {
            MaterialIdCatalog.Stone => new Color(0.56f, 0.58f, 0.62f, 1f),
            MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass => new Color(0.52f, 0.36f, 0.22f, 1f),
            MaterialIdCatalog.Sand => new Color(0.88f, 0.80f, 0.52f, 1f),
            MaterialIdCatalog.Basalt => new Color(0.26f, 0.28f, 0.32f, 1f),
            MaterialIdCatalog.NaturalObsidian => new Color(0.24f, 0.14f, 0.38f, 1f),
            MaterialIdCatalog.Cobblestone => new Color(0.48f, 0.50f, 0.52f, 1f),
            MaterialIdCatalog.StoneBrick => new Color(0.64f, 0.66f, 0.70f, 1f),
            MaterialIdCatalog.Marble => new Color(0.92f, 0.92f, 0.95f, 1f),
            MaterialIdCatalog.OakWood => new Color(0.60f, 0.40f, 0.22f, 1f),
            MaterialIdCatalog.Ironwood => new Color(0.38f, 0.30f, 0.26f, 1f),
            MaterialIdCatalog.LooseRubble => new Color(0.50f, 0.46f, 0.40f, 1f),
            MaterialIdCatalog.CopperOre => new Color(0.92f, 0.50f, 0.22f, 1f),
            MaterialIdCatalog.IronOre => new Color(0.82f, 0.64f, 0.52f, 1f),
            MaterialIdCatalog.GoldOre => new Color(1.00f, 0.84f, 0.20f, 1f),
            MaterialIdCatalog.MithrilOre => new Color(0.30f, 0.90f, 1.00f, 1f),
            _ => new Color(0.65f, 0.65f, 0.65f, 1f)
        };

        private static void FillRect(Texture2D tex, int x0, int y0, int w, int h, Color c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(tex.height, y0 + h); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(tex.width, x0 + w); x++)
                    tex.SetPixel(x, y, c);
        }

        private static void DrawThickLine(Texture2D tex, int x0, int y0, int x1, int y1, int radius, Color c)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                int cx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int cy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                FillRect(tex, cx - radius, cy - radius, radius * 2 + 1, radius * 2 + 1, c);
            }
        }

        private static void DrawHelmetIcon(Texture2D tex, byte tier)
        {
            Color baseCol = tier switch
            {
                1 => new Color(0.48f, 0.32f, 0.20f, 1f), // Deri
                2 => new Color(0.60f, 0.42f, 0.24f, 1f), // Ahşap
                3 => new Color(0.88f, 0.52f, 0.28f, 1f), // Bakır
                _ => new Color(0.72f, 0.75f, 0.80f, 1f)  // Demir
            };
            Color darkRim = baseCol * 0.65f; darkRim.a = 1f;
            Color visor = new Color(0.12f, 0.14f, 0.18f, 1f);

            for (int y = 14; y <= 36; y++)
            {
                for (int x = 12; x <= 36; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 24f;
                    if (dx * dx + dy * dy <= 12f * 12f && y >= 20)
                        tex.SetPixel(x, y, baseCol);
                    else if (y < 20 && Mathf.Abs(dx) <= 11f && y >= 14)
                        tex.SetPixel(x, y, baseCol);
                }
            }
            FillRect(tex, 16, 20, 16, 3, visor);
            DrawThickLine(tex, 14, 14, 34, 14, 1, darkRim);
            DrawThickLine(tex, 24, 32, 24, 38, 1, darkRim);
        }

        private static void DrawChestplateIcon(Texture2D tex, byte tier)
        {
            Color col = tier switch
            {
                1 => new Color(0.48f, 0.32f, 0.20f, 1f),
                2 => new Color(0.60f, 0.42f, 0.24f, 1f),
                3 => new Color(0.88f, 0.52f, 0.28f, 1f),
                _ => new Color(0.72f, 0.75f, 0.80f, 1f)
            };
            Color edge = col * 0.65f; edge.a = 1f;

            for (int y = 10; y <= 36; y++)
            {
                int halfW = y > 28 ? 14 : (y < 16 ? 9 : 12);
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    tex.SetPixel(x, y, col);
                }
            }
            FillRect(tex, 20, 32, 8, 6, new Color(0f, 0f, 0f, 0f));
            FillRect(tex, 8, 30, 5, 6, edge);
            FillRect(tex, 35, 30, 5, 6, edge);
        }

        private static void DrawVambraceIcon(Texture2D tex, byte tier)
        {
            Color col = tier == 1 ? new Color(0.48f, 0.32f, 0.20f, 1f) : new Color(0.72f, 0.75f, 0.80f, 1f);
            Color strap = new Color(0.28f, 0.18f, 0.10f, 1f);
            DrawThickLine(tex, 16, 12, 32, 34, 4, col);
            DrawThickLine(tex, 18, 18, 22, 14, 1, strap);
            DrawThickLine(tex, 24, 26, 28, 22, 1, strap);
        }

        private static void DrawGreavesIcon(Texture2D tex, byte tier)
        {
            Color col = tier == 1 ? new Color(0.48f, 0.32f, 0.20f, 1f) : new Color(0.72f, 0.75f, 0.80f, 1f);
            DrawThickLine(tex, 18, 10, 18, 36, 3, col);
            DrawThickLine(tex, 30, 10, 30, 36, 3, col);
            FillRect(tex, 14, 10, 8, 4, col * 0.8f);
            FillRect(tex, 26, 10, 8, 4, col * 0.8f);
        }

        private static void DrawPotionBottleIcon(Texture2D tex, Color liquidCol)
        {
            Color glass = new Color(0.85f, 0.92f, 0.98f, 0.75f);
            Color cork = new Color(0.62f, 0.42f, 0.22f, 1f);

            FillRect(tex, 21, 33, 6, 4, cork);
            FillRect(tex, 22, 27, 4, 6, glass);

            for (int y = 8; y <= 27; y++)
            {
                for (int x = 12; x <= 36; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 18f;
                    if (dx * dx + dy * dy <= 10.5f * 10.5f)
                    {
                        Color c = y <= 22 ? liquidCol : glass;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
            DrawThickLine(tex, 17, 21, 19, 23, 1, Color.white);
        }

        private static void DrawBandageIcon(Texture2D tex)
        {
            Color cloth = new Color(0.92f, 0.90f, 0.86f, 1f);
            Color shadow = new Color(0.74f, 0.72f, 0.68f, 1f);
            Color redCross = new Color(0.88f, 0.18f, 0.18f, 1f);

            for (int y = 12; y <= 34; y++)
            {
                for (int x = 12; x <= 36; x++)
                {
                    float dx = x - 24f;
                    float dy = y - 23f;
                    if (dx * dx + dy * dy <= 11f * 11f)
                    {
                        Color c = (x + y) % 6 == 0 ? shadow : cloth;
                        tex.SetPixel(x, y, c);
                    }
                }
            }
            FillRect(tex, 22, 17, 4, 12, redCross);
            FillRect(tex, 18, 21, 12, 4, redCross);
        }

        private static void DrawCookedMeatIcon(Texture2D tex)
        {
            Color meatCooked = new Color(0.62f, 0.32f, 0.18f, 1f);
            Color sear = new Color(0.24f, 0.12f, 0.08f, 1f);
            Color bone = new Color(0.92f, 0.90f, 0.84f, 1f);

            for (int y = 14; y <= 34; y++)
            {
                for (int x = 12; x <= 32; x++)
                {
                    float dx = x - 22f;
                    float dy = y - 24f;
                    if (dx * dx + dy * dy <= 9.5f * 9.5f)
                    {
                        tex.SetPixel(x, y, meatCooked);
                    }
                }
            }
            DrawThickLine(tex, 15, 20, 28, 28, 1, sear);
            DrawThickLine(tex, 18, 16, 30, 24, 1, sear);
            DrawThickLine(tex, 28, 18, 38, 12, 2, bone);
        }

        private static void DrawBerriesIcon(Texture2D tex)
        {
            Color berry1 = new Color(0.58f, 0.12f, 0.38f, 1f);
            Color berry2 = new Color(0.38f, 0.10f, 0.48f, 1f);
            Color leaf = new Color(0.28f, 0.62f, 0.22f, 1f);

            FillCircle(tex, 18, 18, 6, berry1);
            FillCircle(tex, 28, 18, 6, berry2);
            FillCircle(tex, 23, 26, 6, berry1);
            DrawThickLine(tex, 23, 28, 23, 34, 1, leaf);
            DrawThickLine(tex, 23, 33, 27, 36, 1, leaf);
        }

        private static void DrawCleanWaterIcon(Texture2D tex)
        {
            DrawPotionBottleIcon(tex, new Color(0.25f, 0.65f, 0.92f, 0.92f));
        }

        private static void DrawCampfireIcon(Texture2D tex)
        {
            Color wood = new Color(0.42f, 0.24f, 0.12f, 1f);
            Color flameRed = new Color(0.92f, 0.28f, 0.10f, 1f);
            Color flameYellow = new Color(1.00f, 0.82f, 0.15f, 1f);

            DrawThickLine(tex, 10, 12, 38, 18, 2, wood);
            DrawThickLine(tex, 12, 18, 36, 12, 2, wood);

            for (int y = 16; y <= 38; y++)
            {
                int halfW = Mathf.RoundToInt((38 - y) * 0.45f) + 1;
                for (int x = 24 - halfW; x <= 24 + halfW; x++)
                {
                    Color c = y < 28 && Mathf.Abs(x - 24) <= 2 ? flameYellow : flameRed;
                    tex.SetPixel(x, y, c);
                }
            }
        }

        private static void FillCircle(Texture2D tex, int cx, int cy, int radius, Color c)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                    {
                        if (x >= 0 && x < tex.width && y >= 0 && y < tex.height)
                            tex.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static readonly string[] CreativeCategoryNames =
        {
            "🧱 Bloklar (24)",
            "⚔️ Silah & Aletler (10)",
            "🛡️ Zırhlar (12)",
            "🧪 İksir & Tıp (3)",
            "🍖 Yemek & Yaşam (9)"
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeBlocks =
        {
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Stone),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Dirt),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Grass),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Sand),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Basalt),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.NaturalObsidian),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Cobblestone),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.StoneBrick),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Marble),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.OakWood),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.Ironwood),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.WoodPlank),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.GlowingMoss),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.LooseRubble),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.CopperOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.IronOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.GoldOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.MithrilOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.AstralOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.StarCore),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.MagmaBlock),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.TitaniumOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.OrichalcumOre),
            (InventoryItemKind.VoxelBlock, MaterialIdCatalog.ObsidianBrick)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeTools =
        {
            (InventoryItemKind.WeaponSword, 0),
            (InventoryItemKind.ToolPickaxe, 0),
            (InventoryItemKind.ToolAxe, 0),
            (InventoryItemKind.ToolMicroChisel, 0),
            (InventoryItemKind.ToolFluidFlask, 0),
            (InventoryItemKind.ToolFluidFlask, 1),
            (InventoryItemKind.ToolFluidFlask, 2),
            (InventoryItemKind.MetalIngot, 1),
            (InventoryItemKind.MetalIngot, 2),
            (InventoryItemKind.MetalIngot, 3)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeArmors =
        {
            (InventoryItemKind.ArmorHelmet, 1),
            (InventoryItemKind.ArmorHelmet, 2),
            (InventoryItemKind.ArmorHelmet, 3),
            (InventoryItemKind.ArmorHelmet, 4),
            (InventoryItemKind.ArmorChestplate, 1),
            (InventoryItemKind.ArmorChestplate, 2),
            (InventoryItemKind.ArmorChestplate, 3),
            (InventoryItemKind.ArmorChestplate, 4),
            (InventoryItemKind.ArmorVambrace, 1),
            (InventoryItemKind.ArmorVambrace, 4),
            (InventoryItemKind.ArmorGreaves, 1),
            (InventoryItemKind.ArmorGreaves, 4)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativePotions =
        {
            (InventoryItemKind.PotionHealth, 0),
            (InventoryItemKind.PotionStamina, 0),
            (InventoryItemKind.MedicalBandage, 0)
        };

        private static readonly (InventoryItemKind kind, byte subId)[] CreativeSurvival =
        {
            (InventoryItemKind.FoodCookedMeat, 0),
            (InventoryItemKind.FoodWildBerries, 0),
            (InventoryItemKind.FoodCleanWater, 0),
            (InventoryItemKind.PlaceableCampfire, 0),
            (InventoryItemKind.SplitFirewood, MaterialIdCatalog.OakWood),
            (InventoryItemKind.SplitFirewood, MaterialIdCatalog.Ironwood),
            (InventoryItemKind.Stick, 0),
            (InventoryItemKind.RoughStoneChunk, MaterialIdCatalog.Stone),
            (InventoryItemKind.LooseDirtClod, 0)
        };

        private bool TryAddOrMergeItem(InventoryItemKind kind, byte subId, int count, Stage1MatterCrystalAndMiningController? miningCtrl = null)
        {
            var slot = new VisualInventorySlot { Kind = kind, SubId = subId, Count = count, Durability01 = 1f };
            bool added = TryMergeOrPlaceIntoArray(in slot, HotbarSlots) || TryMergeOrPlaceIntoArray(in slot, BackpackSlots);
            if (added && miningCtrl != null)
            {
                PushUiCountsToCrystalStorage(miningCtrl);
            }
            return added;
        }

        public void DrawCreativeCatalogGUI(
            float screenW,
            float screenH,
            Stage1MatterCrystalAndMiningController miningCtrl)
        {
            if (!IsCreativeCatalogOpen) return;
            EnsureTexturesAndStyles();

            float winW = Mathf.Min(780f, screenW - 40f);
            float winH = Mathf.Min(460f, screenH - 60f);
            float winX = (screenW - winW) * 0.5f;
            float winY = (screenH - winH) * 0.5f;

            Rect winRect = new Rect(winX, winY, winW, winH);
            GUI.DrawTexture(winRect, _windowBgTex!);

            GUI.Label(new Rect(winX + 16f, winY + 12f, winW - 140f, 26f),
                "<b>[YARATICI TEST MODU — EŞYA VE BLOK KATALOĞU (F4 / TAB)]</b>", _windowTitleStyle!);

            if (GUI.Button(new Rect(winX + winW - 120f, winY + 10f, 106f, 26f), "[KAPAT - ESC]"))
            {
                IsCreativeCatalogOpen = false;
                return;
            }

            float tabX = winX + 16f;
            float tabY = winY + 44f;
            float tabW = (winW - 32f) / CreativeCategoryNames.Length;
            for (int c = 0; c < CreativeCategoryNames.Length; c++)
            {
                bool isSelected = CreativeCategoryIndex == c;
                Rect tabRect = new Rect(tabX + c * tabW, tabY, tabW - 4f, 28f);
                GUI.DrawTexture(tabRect, isSelected ? _slotSelectedBgTex! : _slotBgTex!);
                if (GUI.Button(tabRect, CreativeCategoryNames[c], _keyBadgeStyle!))
                {
                    CreativeCategoryIndex = c;
                }
            }

            var items = CreativeCategoryIndex switch
            {
                0 => CreativeBlocks,
                1 => CreativeTools,
                2 => CreativeArmors,
                3 => CreativePotions,
                _ => CreativeSurvival
            };

            float gridX = winX + 16f;
            float gridY = tabY + 38f;
            const float cellW = 120f;
            const float cellH = 50f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((winW - 32f) / cellW));

            for (int i = 0; i < items.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rect cellRect = new Rect(gridX + col * cellW, gridY + row * cellH, cellW - 6f, cellH - 6f);
                var entry = items[i];
                int maxStack = GetMaxStackCount(entry.kind, entry.subId);
                var testSlot = new VisualInventorySlot { Kind = entry.kind, SubId = entry.subId, Count = maxStack, Durability01 = 1f };

                bool isHovered = cellRect.Contains(Event.current.mousePosition);
                GUI.DrawTexture(cellRect, isHovered ? _slotHoverBgTex! : _slotBgTex!);

                Texture2D icon = GetOrBuildIconForSlot(in testSlot, CombatWeaponArchetype.Longsword);
                if (icon != null)
                {
                    GUI.DrawTexture(new Rect(cellRect.x + 4f, cellRect.y + 4f, 36f, 36f), icon);
                }

                string name = GetShortSlotBadgeLabel(in testSlot);
                GUI.Label(new Rect(cellRect.x + 42f, cellRect.y + 6f, cellRect.width - 44f, 18f), name, _keyBadgeStyle!);
                GUI.Label(new Rect(cellRect.x + 42f, cellRect.y + 24f, cellRect.width - 44f, 16f), $"x{maxStack}", _countLabelStyle!);

                if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none))
                {
                    if (TryAddOrMergeItem(entry.kind, entry.subId, maxStack, miningCtrl))
                    {
                        miningCtrl.LastFeedbackText = $"KREATİF KATALOG: +{maxStack}x {GetSlotDisplayName(in testSlot)} envantere eklendi!";
                    }
                    else
                    {
                        miningCtrl.LastFeedbackText = "ENVANTER DOLU: Eşya eklemek için önce bazı yuvaları boşaltın!";
                    }
                }
            }

            GUI.Label(new Rect(winX + 16f, winY + winH - 26f, winW - 32f, 20f),
                "İpucu: Bir eşyaya tıklayarak tam bir yığın halinde doğrudan çantanıza/hotbarınıza alabilirsiniz. ([TAB] veya [ESC] ile kapatın)", _keyBadgeStyle!);
        }
    }
}
#endif
