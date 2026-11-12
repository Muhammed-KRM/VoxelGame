#nullable enable
using System;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    public enum ClothCenterCraftOutput : byte
    {
        None = 0,
        StonePickaxe = 1,
        StoneAxe = 2,
        StoneHammer = 3,
        StoneKnife = 4,
        LeafBandage = 5,
        LeatherBellows = 6,
        WoodenWheel = 7,
        WoodenBucket = 8
    }

    public struct ClothCenterInputItem
    {
        public PhysicalItemArchetype Archetype;
        public int Count;
        public PhysicalItemShapeDna ShapeDna;
    }

    public struct ClothCenterCraftEvaluation
    {
        public ClothCenterCraftOutput Output;
        public PhysicalItemShapeDna InheritedOutputDna;
        public string OutputDisplayName;
        public string HintOrActionMessage;
        public bool IsReadyToBind;

        public bool IsCraftable => IsReadyToBind;
        public string ThoughtHint => HintOrActionMessage ?? string.Empty;

        public int ConsumePickaxeHead;
        public int ConsumeAxeHead;
        public int ConsumeHammerHead;
        public int ConsumeKnifeBlade;
        public int ConsumeStick;
        public int ConsumeFirewood;
        public int ConsumePlantFiber;
        public int ConsumeLeafPile;
        public int ConsumeRawHide;
    }

    /// <summary>
    /// Faz 2.2 (Asama 2.4): Kumas canta ortasina (Center Circle) konulan 3B parcalari degerlendirir,
    /// eksik baglama malzemesi varsa dusunce ipucu uretir, tam ise yontulmus basligin KnapMask64 + %Kalite
    /// DNA'sini uretilen kazma/balta/cekic/bicaga birebir miras aktarir!
    /// </summary>
    public static class ClothMatCraftingSystem
    {
        public static ClothCenterCraftEvaluation EvaluateCenterPile(ReadOnlySpan<ClothCenterInputItem> centerItems)
        {
            int pickHead = 0, axeHead = 0, hamHead = 0, knifeHead = 0;
            int sticks = 0, firewood = 0, fibers = 0, leaves = 0, hides = 0, otherCount = 0;

            PhysicalItemShapeDna headDna = PhysicalItemShapeDna.CreateNatural(0x51A70C22u, 100);
            bool capturedHeadDna = false;

            for (int i = 0; i < centerItems.Length; i++)
            {
                ClothCenterInputItem it = centerItems[i];
                if (it.Count <= 0 || it.Archetype == PhysicalItemArchetype.None) continue;

                switch (it.Archetype)
                {
                    case PhysicalItemArchetype.KnappedPickaxeHead:
                        pickHead += it.Count;
                        if (!capturedHeadDna) { headDna = it.ShapeDna; capturedHeadDna = true; }
                        break;
                    case PhysicalItemArchetype.KnappedAxeHead:
                        axeHead += it.Count;
                        if (!capturedHeadDna) { headDna = it.ShapeDna; capturedHeadDna = true; }
                        break;
                    case PhysicalItemArchetype.KnappedHammerHead:
                        hamHead += it.Count;
                        if (!capturedHeadDna) { headDna = it.ShapeDna; capturedHeadDna = true; }
                        break;
                    case PhysicalItemArchetype.KnappedKnifeBlade:
                        knifeHead += it.Count;
                        if (!capturedHeadDna) { headDna = it.ShapeDna; capturedHeadDna = true; }
                        break;
                    case PhysicalItemArchetype.NaturalStick:
                        sticks += it.Count;
                        break;
                    case PhysicalItemArchetype.SplitFirewood:
                        firewood += it.Count;
                        break;
                    case PhysicalItemArchetype.PlantFiber:
                        fibers += it.Count;
                        break;
                    case PhysicalItemArchetype.LeafPile:
                        leaves += it.Count;
                        break;
                    case PhysicalItemArchetype.RawHideRoll:
                        hides += it.Count;
                        break;
                    default:
                        otherCount += it.Count;
                        break;
                }
            }

            int totalHeads = pickHead + axeHead + hamHead + knifeHead;
            if (totalHeads == 0 && sticks == 0 && firewood == 0 && fibers == 0 && leaves == 0 && hides == 0 && otherCount == 0)
            {
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    OutputDisplayName = string.Empty,
                    HintOrActionMessage = "Bezin üzerindeki 3B yığınlara [Sol Tık] yaparak malzemeleri ortaya koyun ve iple bağlayın."
                };
            }

            if (otherCount > 0)
            {
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    OutputDisplayName = string.Empty,
                    HintOrActionMessage = "💭 Ortada çanta içi birleştirmeye uymayan fazladan malzeme var ([Sağ Tık] ile yığına geri çekin)."
                };
            }

            if (totalHeads > 1)
            {
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    OutputDisplayName = string.Empty,
                    HintOrActionMessage = "💭 Aynı anda tek bir yontulmuş taş başlık bağlanabilir ([Sağ Tık] ile fazla başlığı geri çekin)."
                };
            }

            // 1..4) Yontulmus Tas Baslik + Kuru Dal + Bitki Lifi -> Tas Aletler (%Kalite ve KnapMask64 mirasli!)
            if (totalHeads == 1 && firewood == 0 && leaves == 0 && hides == 0)
            {
                bool isHammer = hamHead == 1;
                int reqStick = 1;
                int reqFiber = isHammer ? 2 : 1;

                ClothCenterCraftOutput targetOut = pickHead == 1
                    ? ClothCenterCraftOutput.StonePickaxe
                    : (axeHead == 1
                        ? ClothCenterCraftOutput.StoneAxe
                        : (hamHead == 1 ? ClothCenterCraftOutput.StoneHammer : ClothCenterCraftOutput.StoneKnife));

                byte qualPct = headDna.EffectiveQualityPct;
                string toolName = targetOut switch
                {
                    ClothCenterCraftOutput.StonePickaxe => $"Taş Kazma (Kalite: %{qualPct})",
                    ClothCenterCraftOutput.StoneAxe => $"Taş Balta (Kalite: %{qualPct})",
                    ClothCenterCraftOutput.StoneHammer => $"Ağır Taş Çekiç (Kalite: %{qualPct})",
                    _ => $"Keskin Taş Bıçak (Kalite: %{qualPct})"
                };

                if (sticks >= reqStick && fibers >= reqFiber)
                {
                    return new ClothCenterCraftEvaluation
                    {
                        Output = targetOut,
                        InheritedOutputDna = headDna,
                        OutputDisplayName = toolName,
                        HintOrActionMessage = $"🛠️ HAZIR: {toolName} — [İple Bağla / R] ile üret!",
                        IsReadyToBind = true,
                        ConsumePickaxeHead = pickHead > 0 ? 1 : 0,
                        ConsumeAxeHead = axeHead > 0 ? 1 : 0,
                        ConsumeHammerHead = hamHead > 0 ? 1 : 0,
                        ConsumeKnifeBlade = knifeHead > 0 ? 1 : 0,
                        ConsumeStick = reqStick,
                        ConsumePlantFiber = reqFiber
                    };
                }

                int missStick = Math.Max(0, reqStick - sticks);
                int missFiber = Math.Max(0, reqFiber - fibers);
                string needMsg = (missStick > 0 && missFiber > 0)
                    ? $"💭 {toolName} için: +{missStick}x Kuru Dal ve +{missFiber}x Bitki Lifi gerekli"
                    : (missStick > 0
                        ? $"💭 Sap takmak için +{missStick}x Kuru Dal gerekli"
                        : $"💭 Sıkıca bağlamak için +{missFiber}x Bitki Lifi gerekli");

                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    OutputDisplayName = toolName,
                    HintOrActionMessage = needMsg
                };
            }

            // 5) Bitkisel Sargi Bezi: 2x Yaprak Demeti + 2x Bitki Lifi
            if (leaves > 0 && totalHeads == 0 && sticks == 0 && firewood == 0 && hides == 0)
            {
                if (leaves >= 2 && fibers >= 2)
                {
                    return new ClothCenterCraftEvaluation
                    {
                        Output = ClothCenterCraftOutput.LeafBandage,
                        InheritedOutputDna = PhysicalItemShapeDna.CreateNatural(0xBA4DA6E1u, 100),
                        OutputDisplayName = "Bitkisel Sargı Bezi",
                        HintOrActionMessage = "🛠️ HAZIR: Bitkisel Sargı Bezi — [İple Bağla / R] ile sar!",
                        IsReadyToBind = true,
                        ConsumeLeafPile = 2,
                        ConsumePlantFiber = 2
                    };
                }
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    HintOrActionMessage = $"💭 Sargı Bezi için: {Math.Max(0, 2 - leaves)}x Yaprak Demeti, {Math.Max(0, 2 - fibers)}x Bitki Lifi daha koyun"
                };
            }

            // 6) Deri Koruk (Faz 2.4): 2x Ham Deri + 2x (Yarma Odun veya Kuru Dal) + 2x Bitki Lifi
            if (hides > 0 && totalHeads == 0 && leaves == 0)
            {
                bool hasWood = firewood >= 2 || sticks >= 2;
                if (hides >= 2 && hasWood && fibers >= 2)
                {
                    return new ClothCenterCraftEvaluation
                    {
                        Output = ClothCenterCraftOutput.LeatherBellows,
                        InheritedOutputDna = PhysicalItemShapeDna.CreateNatural(0xBE11055u, 100),
                        OutputDisplayName = "El Yapımı Deri Körük",
                        HintOrActionMessage = "🛠️ HAZIR: El Yapımı Deri Körük — [İple Bağla / R] ile dik!",
                        IsReadyToBind = true,
                        ConsumeRawHide = 2,
                        ConsumeFirewood = firewood >= 2 ? 2 : 0,
                        ConsumeStick = firewood >= 2 ? 0 : 2,
                        ConsumePlantFiber = 2
                    };
                }
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    HintOrActionMessage = "💭 Deri Körük için: 2x Ham Deri + 2x Odun/Dal + 2x Bitki Lifi gerekli"
                };
            }

            // 7 & 8) Ahsap Tekerlek (3x Yarma Odun + 2x Lif) veya Ilkel Ahsap Kova (2x Yarma Odun + 2x Lif)
            if (firewood > 0 && totalHeads == 0 && sticks == 0 && leaves == 0 && hides == 0)
            {
                if (firewood >= 3 && fibers >= 2)
                {
                    return new ClothCenterCraftEvaluation
                    {
                        Output = ClothCenterCraftOutput.WoodenWheel,
                        InheritedOutputDna = PhysicalItemShapeDna.CreateNatural(0x77EE1100u, 100),
                        OutputDisplayName = "Ahşap El Arabası Tekerleği",
                        HintOrActionMessage = "🛠️ HAZIR: Ahşap Tekerlek — [İple Bağla / R] ile bağla!",
                        IsReadyToBind = true,
                        ConsumeFirewood = 3,
                        ConsumePlantFiber = 2
                    };
                }
                if (firewood == 2 && fibers >= 2)
                {
                    return new ClothCenterCraftEvaluation
                    {
                        Output = ClothCenterCraftOutput.WoodenBucket,
                        InheritedOutputDna = PhysicalItemShapeDna.CreateNatural(0xB0C4E701u, 100),
                        OutputDisplayName = "İlkel Ahşap Su Kovası",
                        HintOrActionMessage = "🛠️ HAZIR: İlkel Ahşap Kova — [İple Bağla / R] ile bağla! (Tekerlek için +1 Yarma Odun daha ekleyin)",
                        IsReadyToBind = true,
                        ConsumeFirewood = 2,
                        ConsumePlantFiber = 2
                    };
                }
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    HintOrActionMessage = "💭 Ahşap Kova (2x Odun + 2x Lif) veya El Arabası Tekerleği (3x Odun + 2x Lif) için malzeme ekleyin"
                };
            }

            if (sticks > 0 || fibers > 0)
            {
                return new ClothCenterCraftEvaluation
                {
                    Output = ClothCenterCraftOutput.None,
                    IsReadyToBind = false,
                    HintOrActionMessage = "💭 Alet bağlamak için ortaya Yontulmuş Taş Başlık + Kuru Dal + Bitki Lifi koyun"
                };
            }

            return default;
        }

        /// <summary>
        /// Basligin %35..%100 Kalite degerinden alet dayanikliligi (22..85) ve kazi hizi carpani (0.45x..1.15x) hesaplar.
        /// </summary>
        public static void ComputeStoneToolStatsFromQuality(
            byte qualityPct,
            out float maxDurability,
            out float miningPowerMultiplier)
        {
            float q01 = Math.Clamp(qualityPct == 0 ? 1.0f : (qualityPct * 0.01f), 0.35f, 1.00f);
            float t = Math.Clamp((q01 - 0.35f) / 0.65f, 0f, 1f);
            maxDurability = 22.0f + (85.0f - 22.0f) * t;
            miningPowerMultiplier = 0.45f + (1.15f - 0.45f) * t;
        }
    }
}
