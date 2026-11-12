#nullable enable
using System;
using System.Numerics;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// [T] Zihin Insa Menusundeki 4 temel fiziksel yapi fikri (Paket 2 — Faz 2.3 &amp; Faz 2.4).
    /// Bu menuden secim yapmak dunyaya hazir prefab KOYMAZ; oyuncunun yere elleriyle dizdigi
    /// gercek 3B parca kumesini dogrulayip islevsel istasyon olarak muhurler.
    /// </summary>
    public enum MindStructureBlueprintId : byte
    {
        Campfire = 0,          // Kamp Atesi
        CharcoalPit = 1,       // Odun Komuru Ocagi
        BloomeryFurnace = 2,   // Ilkel Izabe Firini (Bloomery)
        WoodenWheelbarrow = 3  // Ahsap El Arabasi
    }

    /// <summary>
    /// Saf C# kume dogrulayicisinin Unity bagimsiz calismasini saglayan dogal parca sinifi.
    /// </summary>
    public enum LooseBuildPartKind : byte
    {
        None = 0,
        LeafPile = 1,
        PlantFiber = 2,
        Stick = 3,
        SplitFirewood = 4,
        RoughStoneChunk = 5,
        LooseDirtClod = 6,
        ClayLump = 7,
        SandPile = 8,
        WoodenWheel = 9,
        ClayTuyerePipe = 10,
        LeatherBellows = 11,
        ClayCruciblePot = 12,
        CharcoalChunk = 13,
        RoastedOreChunk = 14,
        CrushedOrePowder = 15,
        IronBloomSponge = 16
    }

    /// <summary>
    /// Dunyaya serbest yerlestirilmis tek bir fiziksel parcanin matematiksel nokta bulutu temsili.
    /// </summary>
    public struct LoosePartPoint
    {
        public int InstanceId;
        public LooseBuildPartKind Kind;
        public Vector3 Position;
        public Quaternion Rotation;
        public PhysicalItemShapeDna ShapeDna;
        public uint BoundStationId;
    }

    /// <summary>
    /// Esnek nokta bulutu (Point-Cloud Cluster) dogrulama ciktisi.
    /// </summary>
    public struct MindValidationResult
    {
        public bool IsValid;
        public MindStructureBlueprintId Blueprint;
        public Vector3 ClusterCenter;
        public float ClusterBaseY;
        public float BonusEfficiency01;
        public int MatchedPartCount;
        public bool HasStoneRing;
        public string FeedbackMessage;
    }

    /// <summary>
    /// Faz 2.3 (Asama 3.3): Milimetrik slot dayatmayan, yalnizca fiziksel mantik kurallarini
    /// (~1.85m yaricap icinde altta kav var mi, ustunde dallar var mi, baca yukselmis mi vb.)
    /// sifir GC tahsisiyle denetleyen Zihin Kume Dogrulayicisi.
    /// </summary>
    public static class MindStructureClusterValidator
    {
        public const float DefaultScanRadiusMeters = 1.85f;

        public static string GetDisplayName(MindStructureBlueprintId blueprint) => blueprint switch
        {
            MindStructureBlueprintId.Campfire => "Kamp Ateşi",
            MindStructureBlueprintId.CharcoalPit => "Odun Kömürü Ocağı",
            MindStructureBlueprintId.BloomeryFurnace => "İlkel İzabe Fırını (Bloomery)",
            MindStructureBlueprintId.WoodenWheelbarrow => "Ahşap El Arabası",
            _ => "Fiziksel Yapı"
        };

        public static string GetShortDescription(MindStructureBlueprintId blueprint) => blueprint switch
        {
            MindStructureBlueprintId.Campfire =>
                "En alta kuru yaprak veya bitki lifi (kav), üzerine çadır gibi en az 3 dal diz; çevresine taş dizersen rüzgârdan korunur.",
            MindStructureBlueprintId.CharcoalPit =>
                "En alta kav koyup üzerine en az 4 odun/dal yığ ve hava almaması için etrafını en az 4 toprak veya kil topağıyla kapat.",
            MindStructureBlueprintId.BloomeryFurnace =>
                "En az 8 taş ve 6 kil topağını üst üste (~0.4m+) baca gibi ör; kil hava borusunu önceden koyabilir veya sonradan takabilirsin.",
            MindStructureBlueprintId.WoodenWheelbarrow =>
                "Öne 1 ahşap tekerlek koy, arkasına en az 2 yarılmış odun + 2 dal (toplam 6 ahşap) ve 2 bitki lifi diz.",
            _ => string.Empty
        };

        public static MindValidationResult ValidateCluster(
            MindStructureBlueprintId blueprint,
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius = DefaultScanRadiusMeters)
        {
            return blueprint switch
            {
                MindStructureBlueprintId.Campfire => ValidateCampfire(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.CharcoalPit => ValidateCharcoalPit(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.BloomeryFurnace => ValidateBloomeryFurnace(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.WoodenWheelbarrow => ValidateWheelbarrow(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                _ => new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = blueprint,
                    FeedbackMessage = "Bilinmeyen yapı şablonu."
                }
            };
        }

        private static MindValidationResult ValidateCampfire(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float radSq = scanRadius * scanRadius;
            float minY = float.MaxValue;
            int unboundInRadius = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;
                if (Vector3.DistanceSquared(p.Position, aimOrigin) > radSq) continue;

                unboundInRadius++;
                if (p.Position.Y < minY) minY = p.Position.Y;
            }

            if (unboundInRadius == 0)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Campfire,
                    FeedbackMessage = "Önünde henüz dizilmiş doğal malzeme yok! Önce yere [Sağ Tık] ile kav (yaprak/lif) ve üzerine en az 3 dal diz."
                };
            }

            // 1) En altta (>= 1) Kav (LeafPile veya PlantFiber) kontrolu
            Vector3 tinderSum = Vector3.Zero;
            int bottomTinderCount = 0;
            int anyTinderCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u) continue;
                if (Vector3.DistanceSquared(p.Position, aimOrigin) > radSq) continue;

                if (p.Kind == LooseBuildPartKind.LeafPile || p.Kind == LooseBuildPartKind.PlantFiber)
                {
                    anyTinderCount++;
                    if (p.Position.Y <= minY + 0.35f)
                    {
                        tinderSum += p.Position;
                        bottomTinderCount++;
                    }
                }
            }

            if (bottomTinderCount < 1)
            {
                string msg = anyTinderCount > 0
                    ? "Kav (kuru yaprak veya bitki lifi) dalların üstünde kalmış! Önce en alta zemine kav koymalısın."
                    : "Kamp ateşi için önce en alta kuru yaprak demeti veya bitki lifi (kav) koymalısın!";
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Campfire,
                    FeedbackMessage = msg
                };
            }

            Vector3 tinderCenter = tinderSum / bottomTinderCount;

            // 2) Kavin uzerinde / cevresinde (XZ <= 0.85m) en az 3 Dal veya Yarilmis Odun kontrolu
            int woodCount = 0;
            int stoneRingCount = 0;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u) continue;
                if (Vector3.DistanceSquared(p.Position, aimOrigin) > radSq) continue;

                float dx = p.Position.X - tinderCenter.X;
                float dz = p.Position.Z - tinderCenter.Z;
                float horizDist = MathF.Sqrt(dx * dx + dz * dz);
                float relY = p.Position.Y - minY;

                bool includePart = false;
                if ((p.Kind == LooseBuildPartKind.LeafPile || p.Kind == LooseBuildPartKind.PlantFiber) && horizDist <= 0.65f)
                {
                    includePart = true;
                }
                else if ((p.Kind == LooseBuildPartKind.Stick || p.Kind == LooseBuildPartKind.SplitFirewood) &&
                         horizDist <= 0.85f && relY >= -0.15f && relY <= 1.15f)
                {
                    woodCount++;
                    includePart = true;
                }
                else if (p.Kind == LooseBuildPartKind.RoughStoneChunk && horizDist <= 1.15f && relY <= 0.65f)
                {
                    if (horizDist >= 0.14f)
                    {
                        stoneRingCount++;
                    }
                    includePart = true;
                }

                if (includePart && writeCount < matchedIndicesOut.Length)
                {
                    matchedIndicesOut[writeCount++] = i;
                }
            }

            if (woodCount < 3)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Campfire,
                    ClusterCenter = tinderCenter,
                    ClusterBaseY = minY,
                    FeedbackMessage = $"Kuru yaprağın üzerine çadır gibi en az 3 Kuru Dal (veya Yarılmış Odun) dizmelisin! (Şu an: {woodCount}/3)"
                };
            }

            bool hasStoneRing = stoneRingCount >= 4;
            float bonus = hasStoneRing ? 0.15f : 0f;
            string okNote = hasStoneRing
                ? $"🔥 Taş Çemberli Kamp Ateşi kümesi hazır! (+%15 Rüzgâr & Yakıt Koruması) — Çakmaktaşı ile kıvılcım çakarak tutuştur!"
                : "🔥 Kamp Ateşi kümesi hazır! (Sönük — Çakmaktaşı ile 3 kez kıvılcım çakarak tutuştur!)";

            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.Campfire,
                ClusterCenter = new Vector3(tinderCenter.X, minY, tinderCenter.Z),
                ClusterBaseY = minY,
                BonusEfficiency01 = bonus,
                MatchedPartCount = writeCount,
                HasStoneRing = hasStoneRing,
                FeedbackMessage = okNote
            };
        }

        private static MindValidationResult ValidateCharcoalPit(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float radSq = scanRadius * scanRadius;
            float minY = float.MaxValue;
            int tinderCount = 0;
            int woodCount = 0;
            int coverCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || Vector3.DistanceSquared(p.Position, aimOrigin) > radSq) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.LeafPile || p.Kind == LooseBuildPartKind.PlantFiber)
                {
                    tinderCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.SplitFirewood || p.Kind == LooseBuildPartKind.Stick)
                {
                    woodCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.LooseDirtClod || p.Kind == LooseBuildPartKind.ClayLump)
                {
                    coverCount++;
                    relevant = true;
                }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (woodCount < 4)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.CharcoalPit,
                    FeedbackMessage = $"Odun Kömürü Ocağı için en az 4 Yarılmış Odun / Dal yığmalısın! (Şu an: {woodCount}/4)"
                };
            }
            if (coverCount < 4)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.CharcoalPit,
                    FeedbackMessage = $"Odunların yanıp kül olmaması için üzerini en az 4 Toprak veya Kil Topağıyla kapatmalısın! (Şu an: {coverCount}/4)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.CharcoalPit,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                MatchedPartCount = writeCount,
                FeedbackMessage = "🪵 Odun Kömürü Ocağı kümesi mühürlendi! Çakmaktaşı ile alttan tutuşturarak 28 sn için için kömürleştirebilirsin."
            };
        }

        private static MindValidationResult ValidateBloomeryFurnace(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float radSq = scanRadius * scanRadius;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            int stoneCount = 0;
            int clayCount = 0;
            int tuyereCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || Vector3.DistanceSquared(p.Position, aimOrigin) > radSq) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.RoughStoneChunk)
                {
                    stoneCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.ClayLump)
                {
                    clayCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.ClayTuyerePipe ||
                         p.Kind == LooseBuildPartKind.LeatherBellows ||
                         p.Kind == LooseBuildPartKind.ClayCruciblePot ||
                         p.Kind == LooseBuildPartKind.SandPile ||
                         p.Kind == LooseBuildPartKind.PlantFiber)
                {
                    if (p.Kind == LooseBuildPartKind.ClayTuyerePipe) tuyereCount++;
                    relevant = true;
                }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    if (p.Position.Y > maxY) maxY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (stoneCount < 8)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.BloomeryFurnace,
                    FeedbackMessage = $"İlkel İzabe Fırını gövdesi için en az 8 Kaba Taş Parçası örmelisin! (Şu an: {stoneCount}/8)"
                };
            }
            if (clayCount < 6)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.BloomeryFurnace,
                    FeedbackMessage = $"Fırın duvarlarının ısıyı tutması için taşların arasına en az 6 Kil Topağı sıvamalısın! (Şu an: {clayCount}/6)"
                };
            }
            float heightSpan = maxY - minY;
            if (heightSpan < 0.32f)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.BloomeryFurnace,
                    FeedbackMessage = $"Taş ve killeri düz yaymak yerine üst üste baca gibi (~0.4m+) yükseltmelisin! (Yükseklik: {heightSpan:0.2}m)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            string tuyereNote = tuyereCount > 0
                ? "Kil Hava Borusu takılı!"
                : "Şimdi yan alt deliğe [Sağ Tık] ile Kil Hava Borusu ve Deri Körük takabilirsin.";
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.BloomeryFurnace,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                MatchedPartCount = writeCount,
                FeedbackMessage = $"🏺 İlkel İzabe Fırını (Bloomery) doğrulandı! {tuyereNote}"
            };
        }

        private static MindValidationResult ValidateWheelbarrow(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float radSq = scanRadius * scanRadius;
            float minY = float.MaxValue;
            int wheelCount = 0;
            int plankCount = 0;
            int stickCount = 0;
            int fiberCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || Vector3.DistanceSquared(p.Position, aimOrigin) > radSq) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.WoodenWheel) { wheelCount++; relevant = true; }
                else if (p.Kind == LooseBuildPartKind.SplitFirewood) { plankCount++; relevant = true; }
                else if (p.Kind == LooseBuildPartKind.Stick) { stickCount++; relevant = true; }
                else if (p.Kind == LooseBuildPartKind.PlantFiber) { fiberCount++; relevant = true; }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (wheelCount < 1)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.WoodenWheelbarrow,
                    FeedbackMessage = "Ahşap El Arabası için en öne 1 adet Ahşap Tekerlek yerleştirmelisin!"
                };
            }
            if (plankCount < 2)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.WoodenWheelbarrow,
                    FeedbackMessage = $"El arabası teknesi için en az 2 Yarılmış Odun dizmelisin! (Şu an: {plankCount}/2)"
                };
            }
            if (stickCount < 2)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.WoodenWheelbarrow,
                    FeedbackMessage = $"El arabasının iki tutma kolu için en az 2 Kuru Dal uzatmalısın! (Şu an: {stickCount}/2)"
                };
            }
            if (plankCount + stickCount < 6)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.WoodenWheelbarrow,
                    FeedbackMessage = $"El arabası gövdesi için toplam en az 6 ahşap (Yarılmış Odun + Dal) dizmelisin! (Şu an: {plankCount + stickCount}/6)"
                };
            }
            if (fiberCount < 2)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.WoodenWheelbarrow,
                    FeedbackMessage = $"Gövdeyi ve aksı bağlamak için kümede en az 2 Bitki Lifi bulunmalı! (Şu an: {fiberCount}/2)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.WoodenWheelbarrow,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                MatchedPartCount = writeCount,
                FeedbackMessage = "🛞 Ahşap El Arabası çatıldı ve bağlandı! Ağır kütük ve cevher taşımaya hazır."
            };
        }
    }
}
