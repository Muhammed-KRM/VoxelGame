#nullable enable
using System;
using System.IO;
using System.Numerics;
using System.Text;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// [T] Zihin Insa Menusundeki fiziksel yapi fikirleri (Paket 2 — Faz 2.3, Faz 2.4, Paket 4B &amp; Kutuk Yapilari).
    /// Bu menuden secim yapmak dunyaya hazir prefab KOYMAZ; oyuncunun yere elleriyle dizdigi
    /// gercek 3B parca kumesini dogrulayip islevsel istasyon veya yapi olarak muhurler.
    /// </summary>
    public enum MindStructureBlueprintId : byte
    {
        Campfire = 0,          // Kamp Atesi
        CharcoalPit = 1,       // Odun Komuru Ocagi
        BloomeryFurnace = 2,   // Ilkel Izabe Firini (Bloomery)
        WoodenWheelbarrow = 3, // Ahsap El Arabasi
        Windmill = 4,          // Fiziksel Yel Degirmeni (Paket 4B Faz 4B.5)
        LogWall = 5,           // Kütük Duvar
        LogPalisade = 6,       // Kütük Çit
        LogBeam = 7,           // Kütük Kiriş
        LogCabin = 8           // Kütük Kulübe
    }

    /// <summary>
    /// Kesilmis 2m agac kutugunun (FallenTreeLog) [R] tusuyla degistirilen 3B yerlestirme yonelimi.
    /// </summary>
    public enum LogPlacementOrientation : byte
    {
        HorizontalWall = 0,   // Yatay Duvar Kütüğü (Horizontal Wall Log)
        VerticalPalisade = 1, // Dikey Çit Kazığı (Vertical Palisade Stake)
        OverheadBeam = 2      // Üst Çatı Kirişi (Overhead Roof Beam)
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
        IronBloomSponge = 16,
        WindmillSailPanel = 17,
        FallenTreeLog = 18
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
        public const int BlueprintCount = 9;

        public static LogPlacementOrientation CycleLogOrientation(LogPlacementOrientation current) =>
            (LogPlacementOrientation)(((int)current + 1) % 3);

        public static string GetLogOrientationDisplayName(LogPlacementOrientation orientation) => orientation switch
        {
            LogPlacementOrientation.HorizontalWall => "Yatay Duvar Kütüğü (Horizontal Wall Log)",
            LogPlacementOrientation.VerticalPalisade => "Dikey Çit Kazığı (Vertical Palisade Stake)",
            LogPlacementOrientation.OverheadBeam => "Üst Çatı Kirişi (Overhead Roof Beam)",
            _ => "Yatay Duvar Kütüğü (Horizontal Wall Log)"
        };

        /// <summary>
        /// Omuzda tasinan 2m kesilmis agac kutugunun (FallenTreeLog) hedef zemin noktasi, bakis acisi,
        /// secili [R] yonelimi (HorizontalWall, VerticalPalisade, OverheadBeam) ve etraftaki mevcut
        /// kutuklere gore konumunu, 3B donusunu ve ShapeDna bilgisini hesaplar (0 B GC).
        /// </summary>
        public static void ComputeTimberLogPlacementPose(
            LogPlacementOrientation orientation,
            in PhysicalItemShapeDna inputDna,
            Vector3 surfacePoint,
            Vector3 surfaceNormal,
            float userYawDegrees,
            ReadOnlySpan<LoosePartPoint> existingParts,
            out Vector3 finalPosition,
            out Quaternion finalRotation,
            out PhysicalItemShapeDna finalDna,
            out bool stackedOnExistingLog)
        {
            stackedOnExistingLog = false;
            finalDna = inputDna.WithStage((byte)orientation);

            Vector3 n = surfaceNormal.LengthSquared() > 0.0001f
                ? Vector3.Normalize(surfaceNormal)
                : Vector3.UnitY;

            float yawRad = userYawDegrees * (MathF.PI / 180f);
            Vector3 horizFwd = new Vector3(MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));

            if (orientation == LogPlacementOrientation.VerticalPalisade)
            {
                // 2m kutuk dikey kazik olarak dikilir (merkez Y = zemin + 0.98m)
                Vector3 resolvedXZ = new Vector3(surfacePoint.X, 0f, surfacePoint.Z);
                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint p = ref existingParts[i];
                    if (p.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                    float dx = resolvedXZ.X - p.Position.X;
                    float dz = resolvedXZ.Z - p.Position.Z;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    if (dist > 0.001f && dist < 0.36f && MathF.Abs(surfacePoint.Y + 0.98f - p.Position.Y) < 0.90f)
                    {
                        float push = 0.36f - dist;
                        resolvedXZ.X += (dx / dist) * push;
                        resolvedXZ.Z += (dz / dist) * push;
                    }
                }

                finalPosition = new Vector3(resolvedXZ.X, surfacePoint.Y + 0.98f, resolvedXZ.Z);
                finalRotation = WorldLoosePlacementMath.LookRotationSafe(horizFwd, Vector3.UnitY);
                return;
            }

            if (orientation == LogPlacementOrientation.OverheadBeam)
            {
                // Ust tavan/cati kirisi: etraftaki dikme veya duvar kutuklerinin ustune (min +1.88m) oturur
                float beamY = surfacePoint.Y + 1.92f;
                for (int i = 0; i < existingParts.Length; i++)
                {
                    ref readonly LoosePartPoint p = ref existingParts[i];
                    if (p.Kind != LooseBuildPartKind.FallenTreeLog && p.Kind != LooseBuildPartKind.RoughStoneChunk) continue;
                    float dx = surfacePoint.X - p.Position.X;
                    float dz = surfacePoint.Z - p.Position.Z;
                    if ((dx * dx + dz * dz) <= 1.45f * 1.45f)
                    {
                        bool isVerticalSupport = p.ShapeDna.StageByte == (byte)LogPlacementOrientation.VerticalPalisade;
                        float topSupportY = p.Position.Y + (isVerticalSupport ? 1.02f : 0.38f);
                        if (topSupportY >= surfacePoint.Y + 1.10f && topSupportY <= surfacePoint.Y + 3.20f && topSupportY > beamY - 0.35f)
                        {
                            beamY = MathF.Max(beamY, topSupportY);
                            stackedOnExistingLog = true;
                        }
                    }
                }

                finalPosition = new Vector3(surfacePoint.X, beamY, surfacePoint.Z);
                // Yerel +Y ekseni 2m kutuk boyu oldugu icin +Y eksenini horizFwd yonune yatir:
                finalRotation = WorldLoosePlacementMath.LookRotationSafe(Vector3.UnitY, horizFwd);
                return;
            }

            // HorizontalWall (Yatay Duvar Kutugu): zemine veya mevcut yatay kutugun uzerine (+0.38m) istiflenir
            float wallY = surfacePoint.Y + 0.20f;
            for (int i = 0; i < existingParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref existingParts[i];
                if (p.Kind != LooseBuildPartKind.FallenTreeLog) continue;
                float dx = surfacePoint.X - p.Position.X;
                float dz = surfacePoint.Z - p.Position.Z;
                if ((dx * dx + dz * dz) <= 0.62f * 0.62f)
                {
                    float candidateY = p.Position.Y + 0.38f;
                    if (candidateY > wallY && candidateY <= surfacePoint.Y + 2.40f)
                    {
                        wallY = candidateY;
                        stackedOnExistingLog = true;
                    }
                }
            }

            finalPosition = new Vector3(surfacePoint.X, wallY, surfacePoint.Z);
            Vector3 tangentFwd = horizFwd - n * Vector3.Dot(horizFwd, n);
            if (tangentFwd.LengthSquared() < 0.0001f)
            {
                tangentFwd = Vector3.Cross(n, Vector3.UnitX);
            }
            tangentFwd = Vector3.Normalize(tangentFwd);
            finalRotation = WorldLoosePlacementMath.LookRotationSafe(-n, tangentFwd);
        }

        public static string GetDisplayName(MindStructureBlueprintId blueprint) => blueprint switch
        {
            MindStructureBlueprintId.Campfire => "Kamp Ateşi",
            MindStructureBlueprintId.CharcoalPit => "Odun Kömürü Ocağı",
            MindStructureBlueprintId.BloomeryFurnace => "İlkel İzabe Fırını (Bloomery)",
            MindStructureBlueprintId.WoodenWheelbarrow => "Ahşap El Arabası",
            MindStructureBlueprintId.Windmill => "Fiziksel Yel Değirmeni",
            MindStructureBlueprintId.LogWall => "Kütük Duvar",
            MindStructureBlueprintId.LogPalisade => "Kütük Çit",
            MindStructureBlueprintId.LogBeam => "Kütük Kiriş",
            MindStructureBlueprintId.LogCabin => "Kütük Kulübe",
            _ => "Fiziksel Yapı"
        };

        public static string GetShortDescription(MindStructureBlueprintId blueprint) => blueprint switch
        {
            MindStructureBlueprintId.Campfire =>
                "En alta kuru yaprak veya bitki lifi (kav), üzerine çadır gibi en az 3 dal diz; çevresine taş dizersen rüzgârdan korunur.",
            MindStructureBlueprintId.CharcoalPit =>
                "Yere en az 4 odun/dal ve üzerini kapatacak en az 4 toprak veya kil topağı diz; mühürlenince kubbeli kömür ocağına dönüşür.",
            MindStructureBlueprintId.BloomeryFurnace =>
                "Yere en az 8 taş ve 6 kil topağı diz; mühürlenince taş tabanlı silindirik kil izabe fırınına dönüşür (hava borusu ve körük takılır).",
            MindStructureBlueprintId.WoodenWheelbarrow =>
                "Öne 1 ahşap tekerlek koy, arkasına en az 2 yarılmış odun + 2 dal (toplam 6 ahşap) ve 2 bitki lifi diz.",
            MindStructureBlueprintId.Windmill =>
                "Tabana en az 4 taş kaide, üzerine en az 4 ahşap direk/odun ve üst göbeğe 4 Yelken Kanat (WindmillSailPanel) diz; tahıl ve cevher öğütür.",
            MindStructureBlueprintId.LogWall =>
                "Kesilmiş 2m ağaç kütüklerini (FallenTreeLog) [R: Yatay Duvar] modunda üst üste/yan yana en az 3 adet diz; kütük siper duvarı olarak kenetlenir.",
            MindStructureBlueprintId.LogPalisade =>
                "Kesilmiş 2m ağaç kütüklerini [R: Dikey Çit Kazığı] modunda yan yana en az 3 adet dik; savunma kazıklı kütük çit olarak mühürlenir.",
            MindStructureBlueprintId.LogBeam =>
                "En az 2 adet 2m ağaç kütüğünü [R: Üst Çatı Kirişi] ve taşıyıcı dikme olarak yerleştir; tavan taşıyıcı kütük kiriş olarak mühürlenir.",
            MindStructureBlueprintId.LogCabin =>
                "Çevreye en az 6 adet 2m ağaç kütüğü (duvar + çatı kirişi) diz; fırtına ve soğuktan koruyan Kütük Kulübe barınağı olarak mühürlenir.",
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
                MindStructureBlueprintId.Windmill => ValidateWindmill(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.LogWall => ValidateLogWall(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.LogPalisade => ValidateLogPalisade(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.LogBeam => ValidateLogBeam(aimOrigin, allParts, matchedIndicesOut, scanRadius),
                MindStructureBlueprintId.LogCabin => ValidateLogCabin(aimOrigin, allParts, matchedIndicesOut, scanRadius),
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
                    FeedbackMessage = $"Fırın duvarlarının ısıyı tutması için taşların yanına/üstüne en az 6 Kil Topağı koymalısın! (Şu an: {clayCount}/6)"
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

        private static MindValidationResult ValidateWindmill(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float horizRad = MathF.Max(scanRadius, 2.65f);
            float horizRadSq = horizRad * horizRad;
            float minY = float.MaxValue;
            int candidateCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;

                float dx = p.Position.X - aimOrigin.X;
                float dz = p.Position.Z - aimOrigin.Z;
                if ((dx * dx) + (dz * dz) > horizRadSq) continue;
                if (MathF.Abs(p.Position.Y - aimOrigin.Y) > 4.60f) continue;

                candidateCount++;
                if (p.Position.Y < minY) minY = p.Position.Y;
            }

            if (candidateCount == 0)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Windmill,
                    FeedbackMessage = "Önünde yel değirmeni için dizilmiş parça yok! Tabana 4 Taş, üzerine 4 Ahşap Direk/Odun ve 4 Yelken Kanat diz."
                };
            }

            int stoneBaseCount = 0;
            int woodShaftCount = 0;
            int sailPanelCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;

                float dx = p.Position.X - aimOrigin.X;
                float dz = p.Position.Z - aimOrigin.Z;
                if ((dx * dx) + (dz * dz) > horizRadSq) continue;
                if (MathF.Abs(p.Position.Y - aimOrigin.Y) > 4.60f) continue;

                float relY = p.Position.Y - minY;
                bool relevant = false;

                if (p.Kind == LooseBuildPartKind.RoughStoneChunk && relY <= 1.50f)
                {
                    stoneBaseCount++;
                    relevant = true;
                }
                else if ((p.Kind == LooseBuildPartKind.SplitFirewood ||
                          p.Kind == LooseBuildPartKind.Stick ||
                          p.Kind == LooseBuildPartKind.WoodenWheel) &&
                         relY <= 3.80f)
                {
                    woodShaftCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.WindmillSailPanel && relY <= 4.20f)
                {
                    sailPanelCount++;
                    relevant = true;
                }

                if (relevant)
                {
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length)
                    {
                        matchedIndicesOut[writeCount++] = i;
                    }
                }
            }

            if (stoneBaseCount < 4)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Windmill,
                    FeedbackMessage = $"Yel Değirmeni kâgir kaidesi ve değirmen taşı yatağı için tabana en az 4 Taş Parçası dizmelisin! (Şu an: {stoneBaseCount}/4)"
                };
            }

            if (woodShaftCount < 4)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Windmill,
                    FeedbackMessage = $"Yel Değirmeni dikey kulesi ve ana mili için en az 4 Ahşap (Yarılmış Odun / Dal / Tekerlek) dizmelisin! (Şu an: {woodShaftCount}/4)"
                };
            }

            if (sailPanelCount < 4)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.Windmill,
                    FeedbackMessage = $"Yel Değirmeni 4 kanatlı Betz rotoru için en az 4 adet Yel Değirmeni Yelken Kanadı (WindmillSailPanel) yerleştirmelisin! (Şu an: {sailPanelCount}/4)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.Windmill,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                BonusEfficiency01 = 1.0f,
                MatchedPartCount = writeCount,
                HasStoneRing = true,
                FeedbackMessage = "🌬️ Fiziksel Yel Değirmeni kuruldu! ([Sağ Tık] Tahıl/Cevher Yükle | [E] Un/Toz Topla | [Shift+E] Fırtına Freni)"
            };
        }

        private static MindValidationResult ValidateLogWall(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float horizRad = MathF.Max(scanRadius, 2.85f);
            float horizRadSq = horizRad * horizRad;
            float minY = float.MaxValue;
            int logCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;
                float dx = p.Position.X - aimOrigin.X;
                float dz = p.Position.Z - aimOrigin.Z;
                if ((dx * dx + dz * dz) > horizRadSq || MathF.Abs(p.Position.Y - aimOrigin.Y) > 3.20f) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.FallenTreeLog)
                {
                    logCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.PlantFiber || p.Kind == LooseBuildPartKind.Stick || p.Kind == LooseBuildPartKind.RoughStoneChunk)
                {
                    relevant = true;
                }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (logCount < 3)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.LogWall,
                    FeedbackMessage = $"Kütük Duvar (LogWall) örmek için en az 3 adet 2m Ağaç Kütüğü (FallenTreeLog) yerleştirmelisin! (Şu an: {logCount}/3)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.LogWall,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                BonusEfficiency01 = Math.Clamp(0.25f + (logCount - 3) * 0.05f, 0.25f, 0.60f),
                MatchedPartCount = writeCount,
                FeedbackMessage = "🧱 Kütük Duvar (LogWall) kenetlendi ve mühürlendi! (+Siper & Rüzgâr Kalkanı)"
            };
        }

        private static MindValidationResult ValidateLogPalisade(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float horizRad = MathF.Max(scanRadius, 2.85f);
            float horizRadSq = horizRad * horizRad;
            float minY = float.MaxValue;
            int logCount = 0;
            int verticalStakeCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;
                float dx = p.Position.X - aimOrigin.X;
                float dz = p.Position.Z - aimOrigin.Z;
                if ((dx * dx + dz * dz) > horizRadSq || MathF.Abs(p.Position.Y - aimOrigin.Y) > 3.20f) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.FallenTreeLog)
                {
                    logCount++;
                    if (p.ShapeDna.StageByte == (byte)LogPlacementOrientation.VerticalPalisade)
                    {
                        verticalStakeCount++;
                    }
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.PlantFiber || p.Kind == LooseBuildPartKind.Stick)
                {
                    relevant = true;
                }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (logCount < 3)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.LogPalisade,
                    FeedbackMessage = $"Kütük Çit (LogPalisade) için [R: Dikey Çit Kazığı] modunda yan yana en az 3 adet 2m Ağaç Kütüğü dikmelisin! (Şu an: {logCount}/3)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            float bonus = verticalStakeCount >= 3 ? 0.35f : 0.20f;
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.LogPalisade,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                BonusEfficiency01 = bonus,
                MatchedPartCount = writeCount,
                FeedbackMessage = "🛡️ Kütük Çit (LogPalisade) kazıkları birbirine bağlandı ve mühürlendi!"
            };
        }

        private static MindValidationResult ValidateLogBeam(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float horizRad = MathF.Max(scanRadius, 2.85f);
            float horizRadSq = horizRad * horizRad;
            float minY = float.MaxValue;
            int logCount = 0;
            int overheadBeamCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;
                float dx = p.Position.X - aimOrigin.X;
                float dz = p.Position.Z - aimOrigin.Z;
                if ((dx * dx + dz * dz) > horizRadSq || MathF.Abs(p.Position.Y - aimOrigin.Y) > 3.60f) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.FallenTreeLog)
                {
                    logCount++;
                    if (p.ShapeDna.StageByte == (byte)LogPlacementOrientation.OverheadBeam)
                    {
                        overheadBeamCount++;
                    }
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.PlantFiber || p.Kind == LooseBuildPartKind.Stick || p.Kind == LooseBuildPartKind.RoughStoneChunk)
                {
                    relevant = true;
                }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (logCount < 2)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.LogBeam,
                    FeedbackMessage = $"Kütük Kiriş (LogBeam) için en az 2 adet 2m Ağaç Kütüğü ([R: Üst Çatı Kirişi] + taşıyıcı) yerleştirmelisin! (Şu an: {logCount}/2)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            float bonus = overheadBeamCount >= 1 ? 0.30f : 0.15f;
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.LogBeam,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                BonusEfficiency01 = bonus,
                MatchedPartCount = writeCount,
                FeedbackMessage = "🏗️ Kütük Kiriş (LogBeam) taşıyıcı tavan bağlantısı doğrulandı ve mühürlendi!"
            };
        }

        private static MindValidationResult ValidateLogCabin(
            Vector3 aimOrigin,
            ReadOnlySpan<LoosePartPoint> allParts,
            Span<int> matchedIndicesOut,
            float scanRadius)
        {
            float horizRad = MathF.Max(scanRadius, 3.65f);
            float horizRadSq = horizRad * horizRad;
            float minY = float.MaxValue;
            int logCount = 0;
            int stoneCount = 0;
            Vector3 centerSum = Vector3.Zero;
            int writeCount = 0;

            for (int i = 0; i < allParts.Length; i++)
            {
                ref readonly LoosePartPoint p = ref allParts[i];
                if (p.BoundStationId != 0u || p.Kind == LooseBuildPartKind.None) continue;
                float dx = p.Position.X - aimOrigin.X;
                float dz = p.Position.Z - aimOrigin.Z;
                if ((dx * dx + dz * dz) > horizRadSq || MathF.Abs(p.Position.Y - aimOrigin.Y) > 4.20f) continue;

                bool relevant = false;
                if (p.Kind == LooseBuildPartKind.FallenTreeLog)
                {
                    logCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.RoughStoneChunk)
                {
                    stoneCount++;
                    relevant = true;
                }
                else if (p.Kind == LooseBuildPartKind.SplitFirewood ||
                         p.Kind == LooseBuildPartKind.Stick ||
                         p.Kind == LooseBuildPartKind.PlantFiber ||
                         p.Kind == LooseBuildPartKind.ClayLump)
                {
                    relevant = true;
                }

                if (relevant)
                {
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    centerSum += p.Position;
                    if (writeCount < matchedIndicesOut.Length) matchedIndicesOut[writeCount++] = i;
                }
            }

            if (logCount < 6)
            {
                return new MindValidationResult
                {
                    IsValid = false,
                    Blueprint = MindStructureBlueprintId.LogCabin,
                    FeedbackMessage = $"Kütük Kulübe (LogCabin) inşası için en az 6 adet 2m Ağaç Kütüğü (FallenTreeLog) dizmelisin! (Şu an: {logCount}/6)"
                };
            }

            Vector3 center = centerSum / Math.Max(1, writeCount);
            return new MindValidationResult
            {
                IsValid = true,
                Blueprint = MindStructureBlueprintId.LogCabin,
                ClusterCenter = new Vector3(center.X, minY, center.Z),
                ClusterBaseY = minY,
                BonusEfficiency01 = 1.0f,
                MatchedPartCount = writeCount,
                HasStoneRing = stoneCount >= 4,
                FeedbackMessage = "🏠 Kütük Kulübe (LogCabin) çatıldı ve mühürlendi! (+Tam Fırtına & Soğuk Barınağı)"
            };
        }
    }

    /// <summary>
    /// Valheim &amp; Sons of the Forest 13 parcali gercek 3B moduler yapi kiti (Assets/Art/Generated/Structures_Modular/*.glb).
    /// </summary>
    public enum ModularBuildPieceKind : byte
    {
        LogBeam2m = 0,
        LogPillar3m = 1,
        PlankWall2x2m = 2,
        PlankDoorway2x2m = 3,
        PlankWindow2x2m = 4,
        PlankFloor2x2m = 5,
        StoneFoundation2x2m = 6,
        StoneArchPillar3m = 7,
        RoofRafter26Deg2x2m = 8,
        RoofRafter45Deg2x2m = 9,
        RoofRidge2m = 10,
        DiagonalBrace2m = 11,
        GableWall45Deg2m = 12
    }

    /// <summary>
    /// Valheim renk kodlu yapisal stabilite kusagi:
    /// Blue = 1.0 (Zemin / Tas Temel), Green &gt;= 0.75, Yellow &gt;= 0.45, Orange &gt;= 0.20, Red &lt; 0.20 (&lt; 0.12 cokme siniri).
    /// </summary>
    public enum ValheimStabilityBand : byte
    {
        Red = 0,
        Orange = 1,
        Yellow = 2,
        Green = 3,
        Blue = 4
    }

    /// <summary>
    /// Sahnedeki veya prosedurel yerleskedeki tekil 3B moduler yapi dugumu (0-GC deger turu).
    /// </summary>
    public struct ModularPlacedPieceNode
    {
        public int InstanceId;
        public ModularBuildPieceKind Kind;
        public BuildStructuralMaterialKind MaterialKind;
        public BuildSectionThickness ThicknessKind;
        public bool ExplicitMaterialAssigned;
        public byte CompletedCourses;
        public byte RequiredCourses;
        public Vector3 Position;
        public Quaternion Rotation;
        public float GroundFloorY;
        public float Health;
        public float MaxHealth;
        public float Stability01;
        public ValheimStabilityBand StabilityBand;
        public float AccumulatedAxialLoadKn;
        public float CompressionCapacityKn;
        public bool IsGrounded;
        public bool IsFalling;
        public bool IsBroken;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocityRadPerSec;
        public float FallenElapsedSec;
        public int ChunkOwnerKey;
    }

    /// <summary>
    /// Mevcut moduler parca uzerindeki yerel miknatis kenetlenme (snap) soketi.
    /// </summary>
    public readonly struct ModularSnapSocket
    {
        public readonly Vector3 LocalOffset;
        public readonly float RelativeYawDeg;

        public ModularSnapSocket(Vector3 localOffset, float relativeYawDeg = 0f)
        {
            LocalOffset = localOffset;
            RelativeYawDeg = relativeYawDeg;
        }
    }

    /// <summary>
    /// Moduler .glb dosyasinin baslik, COLOR_0 niteligi ve sifir-kaplama (0 texture) denetim raporu.
    /// </summary>
    public readonly struct ModularGlbAuditReport
    {
        public readonly bool ExistsOnDisk;
        public readonly bool HasValidGlbHeader;
        public readonly bool HasPositionAttribute;
        public readonly bool HasNormalAttribute;
        public readonly bool HasColor0Attribute;
        public readonly int EmbeddedTextureCount;
        public readonly int EmbeddedImageCount;
        public readonly long FileSizeBytes;
        public readonly string ResolvedPath;

        public ModularGlbAuditReport(
            bool existsOnDisk,
            bool hasValidGlbHeader,
            bool hasPositionAttribute,
            bool hasNormalAttribute,
            bool hasColor0Attribute,
            int embeddedTextureCount,
            int embeddedImageCount,
            long fileSizeBytes,
            string resolvedPath)
        {
            ExistsOnDisk = existsOnDisk;
            HasValidGlbHeader = hasValidGlbHeader;
            HasPositionAttribute = hasPositionAttribute;
            HasNormalAttribute = hasNormalAttribute;
            HasColor0Attribute = hasColor0Attribute;
            EmbeddedTextureCount = embeddedTextureCount;
            EmbeddedImageCount = embeddedImageCount;
            FileSizeBytes = fileSizeBytes;
            ResolvedPath = resolvedPath;
        }
    }

    /// <summary>
    /// Saf C# (0-GC, Unity-bagimsiz) Valheim Miknatisli Kenetlenme + Renk Kodlu Cizge Stabilitesi +
    /// Sons of the Forest 3B Yercekimi &amp; Acisal Tork Yikim Cozucusu.
    /// </summary>
    public static class ValheimSotfModularGraphSolver
    {
        public const int ModularPieceCount = 13;
        public const float CollapseStabilityThreshold = 0.12f;
        public const float OrangeStabilityThreshold = 0.20f;
        public const float YellowStabilityThreshold = 0.45f;
        public const float GreenStabilityThreshold = 0.75f;
        public const float BlueGroundedThreshold = 0.995f;
        public const float SnapCaptureRadiusMeters = 1.45f;
        public const float JointOverlapToleranceMeters = 0.26f;
        public const float GravityAccelMps2 = 9.81f;

        public static string ResolveModularGlbFileName(ModularBuildPieceKind kind) => GetPieceGlbFileName(kind);

        public static string GetPieceGlbFileName(ModularBuildPieceKind kind) => kind switch
        {
            ModularBuildPieceKind.LogBeam2m => "mod_log_beam_2m.glb",
            ModularBuildPieceKind.LogPillar3m => "mod_log_pillar_3m.glb",
            ModularBuildPieceKind.PlankWall2x2m => "mod_plank_wall_2x2m.glb",
            ModularBuildPieceKind.PlankDoorway2x2m => "mod_plank_doorway_2x2m.glb",
            ModularBuildPieceKind.PlankWindow2x2m => "mod_plank_window_2x2m.glb",
            ModularBuildPieceKind.PlankFloor2x2m => "mod_plank_floor_2x2m.glb",
            ModularBuildPieceKind.StoneFoundation2x2m => "mod_stone_foundation_2x2m.glb",
            ModularBuildPieceKind.StoneArchPillar3m => "mod_stone_arch_pillar_3m.glb",
            ModularBuildPieceKind.RoofRafter26Deg2x2m => "mod_roof_rafter_26deg_2x2m.glb",
            ModularBuildPieceKind.RoofRafter45Deg2x2m => "mod_roof_rafter_45deg_2x2m.glb",
            ModularBuildPieceKind.RoofRidge2m => "mod_roof_ridge_2m.glb",
            ModularBuildPieceKind.DiagonalBrace2m => "mod_diagonal_brace_2m.glb",
            ModularBuildPieceKind.GableWall45Deg2m => "mod_gable_wall_45deg_2m.glb",
            _ => "mod_log_beam_2m.glb"
        };

        public static string GetPieceTurkishDisplayName(ModularBuildPieceKind kind) => kind switch
        {
            ModularBuildPieceKind.LogBeam2m => "Soyulmuş Kütük Kiriş (2m)",
            ModularBuildPieceKind.LogPillar3m => "Soyulmuş Kütük Dikme (3m)",
            ModularBuildPieceKind.PlankWall2x2m => "Geçmeli Nordik Kütük Duvar (2x2m)",
            ModularBuildPieceKind.PlankDoorway2x2m => "Kemerli Kapılı Nordik Duvar (2x2m)",
            ModularBuildPieceKind.PlankWindow2x2m => "Ahşap Kepenkli Pencereli Duvar (2x2m)",
            ModularBuildPieceKind.PlankFloor2x2m => "Kirişli Ahşap Döşeme (2x2m)",
            ModularBuildPieceKind.StoneFoundation2x2m => "Etekli Harçlı Taş Subasman (2x2m)",
            ModularBuildPieceKind.StoneArchPillar3m => "Düz Abaküslü Taş Kemer Sütunu (3m)",
            ModularBuildPieceKind.RoofRafter26Deg2x2m => "26° Kademeli Zona Çatı (2x2m)",
            ModularBuildPieceKind.RoofRafter45Deg2x2m => "45° Dik Zona Kaplı Çatı (2x2m)",
            ModularBuildPieceKind.RoofRidge2m => "İskandinav Çatı Mahyası (2m)",
            ModularBuildPieceKind.DiagonalBrace2m => "45° Çapraz Göğüsleme Payandası (2m)",
            ModularBuildPieceKind.GableWall45Deg2m => "45° Üçgen Çatı Alınlık Duvarı (2m)",
            _ => "Modüler Yapı Parçası"
        };

        public static float GetPieceDefaultMaxHealth(ModularBuildPieceKind kind) => kind switch
        {
            ModularBuildPieceKind.StoneFoundation2x2m => 240.0f,
            ModularBuildPieceKind.StoneArchPillar3m => 190.0f,
            ModularBuildPieceKind.LogPillar3m => 135.0f,
            ModularBuildPieceKind.LogBeam2m => 120.0f,
            ModularBuildPieceKind.PlankWall2x2m => 95.0f,
            ModularBuildPieceKind.PlankDoorway2x2m => 90.0f,
            ModularBuildPieceKind.PlankWindow2x2m => 85.0f,
            ModularBuildPieceKind.PlankFloor2x2m => 90.0f,
            ModularBuildPieceKind.RoofRafter26Deg2x2m => 85.0f,
            ModularBuildPieceKind.RoofRafter45Deg2x2m => 85.0f,
            ModularBuildPieceKind.RoofRidge2m => 70.0f,
            ModularBuildPieceKind.DiagonalBrace2m => 80.0f,
            ModularBuildPieceKind.GableWall45Deg2m => 90.0f,
            _ => 90.0f
        };

        public static ModularBuildPieceKind CyclePieceKind(ModularBuildPieceKind current, int delta = 1)
        {
            int next = ((int)current + delta) % ModularPieceCount;
            if (next < 0) next += ModularPieceCount;
            return (ModularBuildPieceKind)next;
        }

        public static ValheimStabilityBand ClassifyStabilityBand(float stability01)
        {
            if (stability01 >= BlueGroundedThreshold) return ValheimStabilityBand.Blue;
            if (stability01 >= GreenStabilityThreshold) return ValheimStabilityBand.Green;
            if (stability01 >= YellowStabilityThreshold) return ValheimStabilityBand.Yellow;
            if (stability01 >= OrangeStabilityThreshold) return ValheimStabilityBand.Orange;
            return ValheimStabilityBand.Red;
        }

        public static bool CanPieceStand(float stability01) => stability01 >= CollapseStabilityThreshold;

        public static void GetStabilityBandRgba(ValheimStabilityBand band, out byte r, out byte g, out byte b, out byte a)
        {
            a = 245;
            switch (band)
            {
                case ValheimStabilityBand.Blue:
                    r = 62; g = 172; b = 255;
                    break;
                case ValheimStabilityBand.Green:
                    r = 68; g = 232; b = 98;
                    break;
                case ValheimStabilityBand.Yellow:
                    r = 248; g = 218; b = 58;
                    break;
                case ValheimStabilityBand.Orange:
                    r = 250; g = 136; b = 42;
                    break;
                default:
                    r = 246; g = 54; b = 54;
                    break;
            }
        }

        public static string GetStabilityBandTurkishName(ValheimStabilityBand band) => band switch
        {
            ValheimStabilityBand.Blue => "MAVİ (Tam Temel Desteği)",
            ValheimStabilityBand.Green => "YEŞİL (Sağlam Taşıyıcı)",
            ValheimStabilityBand.Yellow => "SARI (Orta Yük)",
            ValheimStabilityBand.Orange => "TURUNCU (Kritik Konsol)",
            _ => "KIRMIZI (Yetersiz Destek — Çöker!)"
        };

        public static void GetPieceLocalBounds(ModularBuildPieceKind kind, out Vector3 localMin, out Vector3 localMax)
        {
            switch (kind)
            {
                case ModularBuildPieceKind.LogBeam2m:
                    localMin = new Vector3(-1.04f, 0.00f, -0.13f);
                    localMax = new Vector3(1.04f, 0.26f, 0.13f);
                    break;
                case ModularBuildPieceKind.LogPillar3m:
                    localMin = new Vector3(-0.14f, 0.00f, -0.14f);
                    localMax = new Vector3(0.14f, 3.00f, 0.14f);
                    break;
                case ModularBuildPieceKind.PlankWall2x2m:
                case ModularBuildPieceKind.PlankDoorway2x2m:
                case ModularBuildPieceKind.PlankWindow2x2m:
                    localMin = new Vector3(-1.00f, 0.00f, -0.06f);
                    localMax = new Vector3(1.00f, 2.00f, 0.06f);
                    break;
                case ModularBuildPieceKind.PlankFloor2x2m:
                    localMin = new Vector3(-1.00f, 0.00f, -1.00f);
                    localMax = new Vector3(1.00f, 0.10f, 1.00f);
                    break;
                case ModularBuildPieceKind.StoneFoundation2x2m:
                    localMin = new Vector3(-1.00f, -0.65f, -1.00f);
                    localMax = new Vector3(1.00f, 0.32f, 1.00f);
                    break;
                case ModularBuildPieceKind.StoneArchPillar3m:
                    localMin = new Vector3(-0.26f, 0.00f, -0.22f);
                    localMax = new Vector3(0.26f, 3.00f, 0.22f);
                    break;
                case ModularBuildPieceKind.RoofRafter26Deg2x2m:
                    localMin = new Vector3(-1.00f, 0.00f, -1.05f);
                    localMax = new Vector3(1.00f, 1.14f, 1.02f);
                    break;
                case ModularBuildPieceKind.RoofRafter45Deg2x2m:
                    localMin = new Vector3(-1.00f, 0.00f, -1.05f);
                    localMax = new Vector3(1.00f, 2.14f, 1.02f);
                    break;
                case ModularBuildPieceKind.RoofRidge2m:
                    localMin = new Vector3(-1.02f, -0.02f, -0.34f);
                    localMax = new Vector3(1.02f, 0.28f, 0.34f);
                    break;
                case ModularBuildPieceKind.DiagonalBrace2m:
                    localMin = new Vector3(-0.76f, 0.00f, -0.09f);
                    localMax = new Vector3(0.76f, 1.44f, 0.09f);
                    break;
                case ModularBuildPieceKind.GableWall45Deg2m:
                    localMin = new Vector3(-1.00f, 0.00f, -0.07f);
                    localMax = new Vector3(1.00f, 2.05f, 0.07f);
                    break;
                default:
                    localMin = new Vector3(-0.5f, 0.0f, -0.5f);
                    localMax = new Vector3(0.5f, 1.0f, 0.5f);
                    break;
            }
        }

        public static void ComputeWorldAabb(
            ModularBuildPieceKind kind,
            Vector3 worldPos,
            Quaternion worldRot,
            out Vector3 worldMin,
            out Vector3 worldMax)
        {
            GetPieceLocalBounds(kind, out Vector3 lMin, out Vector3 lMax);
            Vector3 minV = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxV = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 corner = new Vector3(
                    (mask & 1) == 0 ? lMin.X : lMax.X,
                    (mask & 2) == 0 ? lMin.Y : lMax.Y,
                    (mask & 4) == 0 ? lMin.Z : lMax.Z);
                Vector3 w = worldPos + Vector3.Transform(corner, worldRot);
                minV = Vector3.Min(minV, w);
                maxV = Vector3.Max(maxV, w);
            }

            worldMin = minV;
            worldMax = maxV;
        }

        public static bool DoAabbsTouch(
            Vector3 minA, Vector3 maxA,
            Vector3 minB, Vector3 maxB,
            float tolerance = JointOverlapToleranceMeters)
        {
            return (minA.X - tolerance <= maxB.X && maxA.X + tolerance >= minB.X) &&
                   (minA.Y - tolerance <= maxB.Y && maxA.Y + tolerance >= minB.Y) &&
                   (minA.Z - tolerance <= maxB.Z && maxA.Z + tolerance >= minB.Z);
        }

        public static float DistancePointToAabb(Vector3 point, Vector3 boxMin, Vector3 boxMax)
        {
            float cx = Math.Clamp(point.X, boxMin.X, boxMax.X);
            float cy = Math.Clamp(point.Y, boxMin.Y, boxMax.Y);
            float cz = Math.Clamp(point.Z, boxMin.Z, boxMax.Z);
            float dx = point.X - cx;
            float dy = point.Y - cy;
            float dz = point.Z - cz;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Goreli yolla (Application.dataPath + CWD + ust dizin yuruyusu, 0 sabit kodlu mutlak yol) .glb yolunu cozer.
        /// </summary>
        public static string ResolveModularGlbDiskPath(ModularBuildPieceKind kind, string? unityDataPath = null)
        {
            string fileName = GetPieceGlbFileName(kind);
            if (!string.IsNullOrEmpty(unityDataPath))
            {
                string viaData = Path.Combine(unityDataPath, "Art", "Generated", "Structures_Modular", fileName);
                if (File.Exists(viaData)) return viaData;
            }

            string relFromCwd = Path.Combine("Assets", "Art", "Generated", "Structures_Modular", fileName);
            if (File.Exists(relFromCwd)) return Path.GetFullPath(relFromCwd);

            string? probeDir = Directory.GetCurrentDirectory();
            for (int depth = 0; depth < 7 && !string.IsNullOrEmpty(probeDir); depth++)
            {
                string candidate = Path.Combine(probeDir, "Assets", "Art", "Generated", "Structures_Modular", fileName);
                if (File.Exists(candidate)) return candidate;
                probeDir = Directory.GetParent(probeDir)?.FullName;
            }

            probeDir = AppContext.BaseDirectory;
            for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(probeDir); depth++)
            {
                string candidate = Path.Combine(probeDir, "Assets", "Art", "Generated", "Structures_Modular", fileName);
                if (File.Exists(candidate)) return candidate;
                probeDir = Directory.GetParent(probeDir)?.FullName;
            }

            return !string.IsNullOrEmpty(unityDataPath)
                ? Path.Combine(unityDataPath, "Art", "Generated", "Structures_Modular", fileName)
                : relFromCwd;
        }

        /// <summary>
        /// .glb ikili basligini ve JSON semasini okuyarak COLOR_0 varligini ve 0 gomulu kaplama kuralini dogrular.
        /// </summary>
        public static ModularGlbAuditReport AuditModularGlbFile(ModularBuildPieceKind kind, string? unityDataPath = null)
        {
            string path = ResolveModularGlbDiskPath(kind, unityDataPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new ModularGlbAuditReport(false, false, false, false, false, -1, -1, 0L, path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 28 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67u)
            {
                return new ModularGlbAuditReport(true, false, false, false, false, -1, -1, bytes.Length, path);
            }

            uint version = BitConverter.ToUInt32(bytes, 4);
            if (version != 2u)
            {
                return new ModularGlbAuditReport(true, false, false, false, false, -1, -1, bytes.Length, path);
            }

            int offset = 12;
            string? jsonText = null;
            bool hasBinChunk = false;
            while (offset + 8 <= bytes.Length)
            {
                int chunkLen = BitConverter.ToInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (chunkLen < 0 || dataStart + chunkLen > bytes.Length) break;

                if (chunkType == 0x4E4F534Au) // JSON
                {
                    jsonText = Encoding.UTF8.GetString(bytes, dataStart, chunkLen);
                }
                else if (chunkType == 0x004E4942u && chunkLen > 0) // BIN\0
                {
                    hasBinChunk = true;
                }
                offset = dataStart + chunkLen;
            }

            if (string.IsNullOrEmpty(jsonText) || !hasBinChunk)
            {
                return new ModularGlbAuditReport(true, false, false, false, false, -1, -1, bytes.Length, path);
            }

            bool hasPos = jsonText.Contains("\"POSITION\"", StringComparison.Ordinal);
            bool hasNrm = jsonText.Contains("\"NORMAL\"", StringComparison.Ordinal);
            bool hasCol0 = jsonText.Contains("\"COLOR_0\"", StringComparison.Ordinal);
            int texCount = CountJsonArrayObjects(jsonText, "\"textures\"");
            int imgCount = CountJsonArrayObjects(jsonText, "\"images\"");

            return new ModularGlbAuditReport(
                existsOnDisk: true,
                hasValidGlbHeader: true,
                hasPositionAttribute: hasPos,
                hasNormalAttribute: hasNrm,
                hasColor0Attribute: hasCol0,
                embeddedTextureCount: texCount,
                embeddedImageCount: imgCount,
                fileSizeBytes: bytes.Length,
                resolvedPath: path);
        }

        private static int CountJsonArrayObjects(string json, string keyToken)
        {
            int idx = json.IndexOf(keyToken, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int openBracket = json.IndexOf('[', idx + keyToken.Length);
            if (openBracket < 0) return 0;

            int depth = 0;
            int objectCount = 0;
            for (int i = openBracket; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (c == '{' && depth == 1)
                {
                    objectCount++;
                }
            }
            return objectCount;
        }

        /// <summary>
        /// Mevcut bir moduler parca uzerinde aday parcanin miknatisli kenetlenebilecegi yerel soketleri listeler (0 B GC).
        /// </summary>
        private static void AddSock(Span<ModularSnapSocket> outSockets, ref int n, float x, float y, float z, float relYaw = 0f)
        {
            if (n < outSockets.Length)
            {
                outSockets[n++] = new ModularSnapSocket(new Vector3(x, y, z), relYaw);
            }
        }

        public static int PopulateCompatibleSnapSockets(
            ModularBuildPieceKind hostKind,
            ModularBuildPieceKind candidateKind,
            Span<ModularSnapSocket> outSockets)
        {
            int n = 0;

            bool candIsWall = candidateKind == ModularBuildPieceKind.PlankWall2x2m ||
                              candidateKind == ModularBuildPieceKind.PlankDoorway2x2m ||
                              candidateKind == ModularBuildPieceKind.PlankWindow2x2m;
            bool candIsPillar = candidateKind == ModularBuildPieceKind.LogPillar3m ||
                                candidateKind == ModularBuildPieceKind.StoneArchPillar3m;
            bool candIsRoof = candidateKind == ModularBuildPieceKind.RoofRafter26Deg2x2m ||
                              candidateKind == ModularBuildPieceKind.RoofRafter45Deg2x2m;

            switch (hostKind)
            {
                case ModularBuildPieceKind.StoneFoundation2x2m:
                    if (candidateKind == ModularBuildPieceKind.StoneFoundation2x2m)
                    {
                        AddSock(outSockets, ref n, -2.0f, 0.0f, 0.0f); AddSock(outSockets, ref n, 2.0f, 0.0f, 0.0f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, -2.0f); AddSock(outSockets, ref n, 0.0f, 0.0f, 2.0f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.PlankFloor2x2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.32f, 0.0f);
                        AddSock(outSockets, ref n, -2.0f, 0.32f, 0.0f); AddSock(outSockets, ref n, 2.0f, 0.32f, 0.0f);
                        AddSock(outSockets, ref n, 0.0f, 0.32f, -2.0f); AddSock(outSockets, ref n, 0.0f, 0.32f, 2.0f);
                    }
                    else if (candIsWall || candidateKind == ModularBuildPieceKind.LogBeam2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.32f, -0.94f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 0.32f, 0.94f, 180f);
                        AddSock(outSockets, ref n, -0.94f, 0.32f, 0.0f, 90f);
                        AddSock(outSockets, ref n, 0.94f, 0.32f, 0.0f, 270f);
                    }
                    else if (candIsPillar)
                    {
                        AddSock(outSockets, ref n, -0.90f, 0.32f, -0.90f); AddSock(outSockets, ref n, 0.90f, 0.32f, -0.90f);
                        AddSock(outSockets, ref n, -0.90f, 0.32f, 0.90f); AddSock(outSockets, ref n, 0.90f, 0.32f, 0.90f);
                    }
                    break;

                case ModularBuildPieceKind.PlankFloor2x2m:
                    if (candidateKind == ModularBuildPieceKind.PlankFloor2x2m)
                    {
                        AddSock(outSockets, ref n, -2.0f, 0.0f, 0.0f); AddSock(outSockets, ref n, 2.0f, 0.0f, 0.0f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, -2.0f); AddSock(outSockets, ref n, 0.0f, 0.0f, 2.0f);
                    }
                    else if (candIsWall || candidateKind == ModularBuildPieceKind.LogBeam2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.10f, -0.94f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 0.10f, 0.94f, 180f);
                        AddSock(outSockets, ref n, -0.94f, 0.10f, 0.0f, 90f);
                        AddSock(outSockets, ref n, 0.94f, 0.10f, 0.0f, 270f);
                    }
                    else if (candIsPillar)
                    {
                        AddSock(outSockets, ref n, -0.90f, 0.10f, -0.90f); AddSock(outSockets, ref n, 0.90f, 0.10f, -0.90f);
                        AddSock(outSockets, ref n, -0.90f, 0.10f, 0.90f); AddSock(outSockets, ref n, 0.90f, 0.10f, 0.90f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.DiagonalBrace2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, -1.40f, 0.0f);
                    }
                    break;

                case ModularBuildPieceKind.PlankWall2x2m:
                case ModularBuildPieceKind.PlankDoorway2x2m:
                case ModularBuildPieceKind.PlankWindow2x2m:
                    if (candIsWall)
                    {
                        AddSock(outSockets, ref n, -2.0f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 2.0f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 2.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, -1.0f, 0.0f, 1.0f, 90f);
                        AddSock(outSockets, ref n, 1.0f, 0.0f, 1.0f, 270f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.GableWall45Deg2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 2.00f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 2.00f, 0.0f, 180f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.PlankFloor2x2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 1.95f, 1.00f);
                        AddSock(outSockets, ref n, 0.0f, 1.95f, -1.00f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, 1.00f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, -1.00f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.LogBeam2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 2.00f, 0.0f);
                    }
                    else if (candIsRoof)
                    {
                        AddSock(outSockets, ref n, 0.0f, 2.00f, 1.00f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 2.00f, -1.00f, 180f);
                    }
                    else if (candIsPillar)
                    {
                        AddSock(outSockets, ref n, -0.95f, 0.0f, 0.0f);
                        AddSock(outSockets, ref n, 0.95f, 0.0f, 0.0f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.DiagonalBrace2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.56f, 0.72f, 90f);
                        AddSock(outSockets, ref n, 0.0f, 0.56f, -0.72f, 270f);
                    }
                    break;

                case ModularBuildPieceKind.LogPillar3m:
                case ModularBuildPieceKind.StoneArchPillar3m:
                    if (candIsPillar)
                    {
                        AddSock(outSockets, ref n, 0.0f, 3.00f, 0.0f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.LogBeam2m)
                    {
                        AddSock(outSockets, ref n, 1.00f, 2.74f, 0.0f, 0f);
                        AddSock(outSockets, ref n, -1.00f, 2.74f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 2.74f, 1.00f, 90f);
                        AddSock(outSockets, ref n, 0.0f, 2.74f, -1.00f, 90f);
                    }
                    else if (candIsWall)
                    {
                        AddSock(outSockets, ref n, 1.00f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, -1.00f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, 1.00f, 90f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, -1.00f, 90f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.PlankFloor2x2m)
                    {
                        AddSock(outSockets, ref n, 1.00f, 2.90f, 1.00f);
                        AddSock(outSockets, ref n, -1.00f, 2.90f, 1.00f);
                        AddSock(outSockets, ref n, 1.00f, 2.90f, -1.00f);
                        AddSock(outSockets, ref n, -1.00f, 2.90f, -1.00f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.DiagonalBrace2m)
                    {
                        AddSock(outSockets, ref n, 0.70f, 1.56f, 0.0f, 0f);
                        AddSock(outSockets, ref n, -0.70f, 1.56f, 0.0f, 180f);
                        AddSock(outSockets, ref n, 0.0f, 1.56f, 0.70f, 270f);
                        AddSock(outSockets, ref n, 0.0f, 1.56f, -0.70f, 90f);
                    }
                    else if (candIsRoof)
                    {
                        AddSock(outSockets, ref n, 1.00f, 3.00f, 1.00f, 0f);
                        AddSock(outSockets, ref n, -1.00f, 3.00f, 1.00f, 0f);
                    }
                    break;

                case ModularBuildPieceKind.LogBeam2m:
                    if (candidateKind == ModularBuildPieceKind.LogBeam2m)
                    {
                        AddSock(outSockets, ref n, -2.0f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 2.0f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, -1.0f, 0.0f, 1.0f, 90f);
                        AddSock(outSockets, ref n, 1.0f, 0.0f, 1.0f, 90f);
                    }
                    else if (candIsRoof)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.24f, 1.00f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 0.24f, -1.00f, 180f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.PlankFloor2x2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.22f, 1.00f);
                        AddSock(outSockets, ref n, 0.0f, 0.22f, -1.00f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.DiagonalBrace2m)
                    {
                        AddSock(outSockets, ref n, -0.35f, -1.38f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 0.35f, -1.38f, 0.0f, 180f);
                    }
                    break;

                case ModularBuildPieceKind.RoofRafter26Deg2x2m:
                case ModularBuildPieceKind.RoofRafter45Deg2x2m:
                {
                    float rise = hostKind == ModularBuildPieceKind.RoofRafter45Deg2x2m ? 2.0f : 1.0f;
                    if (candIsRoof)
                    {
                        AddSock(outSockets, ref n, -2.0f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 2.0f, 0.0f, 0.0f, 0f);
                        AddSock(outSockets, ref n, 0.0f, rise, 2.0f, 0f);
                        AddSock(outSockets, ref n, 0.0f, 0.0f, 2.0f, 180f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.RoofRidge2m)
                    {
                        AddSock(outSockets, ref n, 0.0f, rise - 0.02f, 1.00f, 0f);
                    }
                    break;
                }

                case ModularBuildPieceKind.GableWall45Deg2m:
                    if (candidateKind == ModularBuildPieceKind.GableWall45Deg2m)
                    {
                        AddSock(outSockets, ref n, 2.0f, 0.0f, 0.0f, 180f);
                        AddSock(outSockets, ref n, -2.0f, 0.0f, 0.0f, 0f);
                    }
                    else if (candIsRoof)
                    {
                        AddSock(outSockets, ref n, 0.0f, 0.02f, 1.00f, 90f);
                        AddSock(outSockets, ref n, 0.0f, 0.02f, -1.00f, 90f);
                    }
                    else if (candidateKind == ModularBuildPieceKind.RoofRidge2m)
                    {
                        AddSock(outSockets, ref n, 1.00f, 1.98f, 1.00f, 90f);
                        AddSock(outSockets, ref n, 1.00f, 1.98f, -1.00f, 90f);
                    }
                    break;
            }

            return n;
        }

        /// <summary>
        /// Oyuncunun nisan noktasina ve mevcut moduler parcalara gore miknatisli kenetlenme (magnetic snap) konumunu,
        /// [R] donusunu ve aday parcanin canli Valheim cizge stabilitesini (Stability01 + Band) hesaplar (0 B GC).
        /// </summary>
        public static void ComputeMagneticPlacementPose(
            ModularBuildPieceKind candidateKind,
            Vector3 aimPoint,
            float userYawDeg,
            float groundSurfaceY,
            ReadOnlySpan<ModularPlacedPieceNode> existingNodes,
            out Vector3 finalPosition,
            out Quaternion finalRotation,
            out bool snappedToPiece,
            out bool isGrounded,
            out float previewStability01,
            out ValheimStabilityBand previewBand,
            BuildStructuralMaterialKind candidateMaterial = BuildStructuralMaterialKind.PineTimber,
            BuildSectionThickness candidateThickness = BuildSectionThickness.Standard,
            bool explicitMaterial = false)
        {
            snappedToPiece = false;
            float snappedYawDeg = MathF.Round(userYawDeg / 45.0f) * 45.0f;
            finalRotation = CreateYawQuaternion(snappedYawDeg);
            finalPosition = new Vector3(aimPoint.X, MathF.Max(aimPoint.Y, groundSurfaceY), aimPoint.Z);

            float bestDistSq = SnapCaptureRadiusMeters * SnapCaptureRadiusMeters;
            Span<ModularSnapSocket> sockets = stackalloc ModularSnapSocket[12];

            for (int i = 0; i < existingNodes.Length; i++)
            {
                ref readonly ModularPlacedPieceNode host = ref existingNodes[i];
                if (host.IsBroken || host.IsFalling) continue;
                if (Vector3.DistanceSquared(host.Position, aimPoint) > 5.2f * 5.2f) continue;

                float hostYaw = ExtractYawDegrees(host.Rotation);
                int sCount = PopulateCompatibleSnapSockets(host.Kind, candidateKind, sockets);
                for (int s = 0; s < sCount; s++)
                {
                    // Tamamlanmamis yarim duvarlarin ust soketlerini erken acma (once duvar katmanlari dizilmeli!)
                    if (!SotfStructuralBuildMechanics.IsPieceFullyConstructed(in host) && sockets[s].LocalOffset.Y > 0.55f)
                    {
                        continue;
                    }

                    Vector3 worldSock = host.Position + Vector3.Transform(sockets[s].LocalOffset, host.Rotation);
                    float dSq = Vector3.DistanceSquared(worldSock, aimPoint);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        finalPosition = worldSock;
                        finalRotation = CreateYawQuaternion(hostYaw + sockets[s].RelativeYawDeg + snappedYawDeg);
                        snappedToPiece = true;
                    }
                }
            }

            ComputeWorldAabb(candidateKind, finalPosition, finalRotation, out Vector3 candMin, out Vector3 candMax);
            isGrounded = candMin.Y <= groundSurfaceY + 0.22f;
            previewStability01 = EvaluateCandidateStability(
                candidateKind, finalPosition, finalRotation, candMin, candMax, isGrounded, existingNodes,
                candidateMaterial, candidateThickness, explicitMaterial);
            previewBand = ClassifyStabilityBand(previewStability01);
        }

        /// <summary>
        /// Nisan noktasinin yakininda henuz tum kutuk/tas katmanlari dizilmemis (CompletedCourses &lt; RequiredCourses)
        /// bir moduler yapi elemani varsa indeksini doner (-1 = yok).
        /// </summary>
        public static int FindAimedUnfinishedCourseNodeIndex(
            ReadOnlySpan<ModularPlacedPieceNode> nodes,
            Vector3 aimPoint,
            float maxCaptureDist = 1.45f)
        {
            int bestIdx = -1;
            float bestDist = maxCaptureDist;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref nodes[i];
                if (n.IsBroken || n.IsFalling) continue;
                if (SotfStructuralBuildMechanics.IsPieceFullyConstructed(in n)) continue;

                ComputeWorldAabb(n.Kind, n.Position, n.Rotation, out Vector3 bMin, out Vector3 bMax);
                float d = DistancePointToAabb(aimPoint, bMin, bMax);
                if (d <= bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        public static BuildStructuralMaterialKind ResolveEffectiveNodeMaterial(in ModularPlacedPieceNode node)
        {
            if (!node.ExplicitMaterialAssigned &&
                node.MaterialKind == BuildStructuralMaterialKind.PineTimber &&
                (node.Kind == ModularBuildPieceKind.StoneFoundation2x2m || node.Kind == ModularBuildPieceKind.StoneArchPillar3m))
            {
                return BuildStructuralMaterialKind.RubbleStone;
            }
            return node.MaterialKind;
        }

        public static float ComputePieceMaxHealth(
            ModularBuildPieceKind kind,
            BuildStructuralMaterialKind matKind,
            BuildSectionThickness thickness)
        {
            float baseHp = GetPieceDefaultMaxHealth(kind);
            StructuralMaterialProfile prof = SotfStructuralBuildMechanics.GetMaterialProfile(matKind);
            float matRatio = (kind == ModularBuildPieceKind.StoneFoundation2x2m || kind == ModularBuildPieceKind.StoneArchPillar3m)
                ? (prof.BaseMaxHealth / 240.0f)
                : (prof.BaseMaxHealth / 110.0f);
            float thickMult = thickness switch
            {
                BuildSectionThickness.Slender => 0.68f,
                BuildSectionThickness.HeavyBearing => 1.65f,
                _ => 1.00f
            };
            return MathF.Max(30.0f, baseHp * MathF.Max(0.55f, matRatio) * thickMult);
        }

        public static float EvaluateCandidateStability(
            ModularBuildPieceKind candidateKind,
            Vector3 candPos,
            Quaternion candRot,
            Vector3 candMin,
            Vector3 candMax,
            bool isGrounded,
            ReadOnlySpan<ModularPlacedPieceNode> existingNodes,
            BuildStructuralMaterialKind candidateMaterial = BuildStructuralMaterialKind.PineTimber,
            BuildSectionThickness candidateThickness = BuildSectionThickness.Standard,
            bool explicitMaterial = false)
        {
            _ = candPos;
            _ = candRot;
            BuildStructuralMaterialKind effCandMat = (!explicitMaterial && candidateMaterial == BuildStructuralMaterialKind.PineTimber &&
                (candidateKind == ModularBuildPieceKind.StoneFoundation2x2m || candidateKind == ModularBuildPieceKind.StoneArchPillar3m))
                ? BuildStructuralMaterialKind.RubbleStone
                : candidateMaterial;

            if (isGrounded)
            {
                return 1.0f;
            }

            Vector3 candCenter = 0.5f * (candMin + candMax);
            float bestStability = 0.0f;
            bool bracedByActiveStrut = false;
            float bestBraceSupport = 0.0f;
            float candWeightKn = SotfStructuralBuildMechanics.ComputePieceSelfWeightKn(candidateKind, effCandMat, candidateThickness);

            for (int i = 0; i < existingNodes.Length; i++)
            {
                ref readonly ModularPlacedPieceNode sup = ref existingNodes[i];
                if (sup.IsBroken || sup.IsFalling || sup.Stability01 <= 0.01f) continue;

                // Tamamlanmamis yarim duvarlarin (< %100 katman) ustune tavan/cati/ust duvar oturtulamaz (once duvar kutukleri tamamlanmali!)
                if (!SotfStructuralBuildMechanics.IsPieceFullyConstructed(in sup) &&
                    candMin.Y >= sup.Position.Y + 0.50f)
                {
                    continue;
                }

                ComputeWorldAabb(sup.Kind, sup.Position, sup.Rotation, out Vector3 supMin, out Vector3 supMax);
                if (!DoAabbsTouch(supMin, supMax, candMin, candMax, JointOverlapToleranceMeters))
                {
                    continue;
                }

                BuildStructuralMaterialKind supMat = ResolveEffectiveNodeMaterial(in sup);
                Vector3 supCenter = 0.5f * (supMin + supMax);
                float edgeLoss = ComputeDirectionalStabilityLoss(
                    sup.Kind, candidateKind, supCenter, candCenter, sup.IsGrounded,
                    supMat, sup.ThicknessKind, effCandMat, candidateThickness);

                // Ince/zayif destege cok agir tas/mermer ust katman koyulursa basinc asimi uyarisi:
                float supCapKn = SotfStructuralBuildMechanics.ComputePieceCompressionCapacityKn(sup.Kind, supMat, sup.ThicknessKind);
                float projectedLoadKn = sup.AccumulatedAxialLoadKn + candWeightKn;
                if (projectedLoadKn > supCapKn)
                {
                    edgeLoss += 0.95f;
                }
                else if (projectedLoadKn > supCapKn * 0.75f)
                {
                    edgeLoss += ((projectedLoadKn / supCapKn) - 0.75f) * 1.25f;
                }

                float candidateVal = Math.Clamp(sup.Stability01 - edgeLoss, 0.0f, 1.0f);
                if (candidateVal > bestStability)
                {
                    bestStability = candidateVal;
                }

                if (sup.Kind == ModularBuildPieceKind.DiagonalBrace2m && sup.Stability01 >= 0.20f)
                {
                    bracedByActiveStrut = true;
                    if (sup.Stability01 > bestBraceSupport) bestBraceSupport = sup.Stability01;
                }
            }

            if (bracedByActiveStrut)
            {
                bestStability = Math.Clamp(MathF.Max(bestStability + 0.22f, bestBraceSupport - 0.06f), 0.0f, 0.96f);
            }

            return bestStability;
        }

        private static float ComputeDirectionalStabilityLoss(
            ModularBuildPieceKind supportKind,
            ModularBuildPieceKind targetKind,
            Vector3 supportCenter,
            Vector3 targetCenter,
            bool supportIsGrounded,
            BuildStructuralMaterialKind supportMat = BuildStructuralMaterialKind.PineTimber,
            BuildSectionThickness supportThickness = BuildSectionThickness.Standard,
            BuildStructuralMaterialKind targetMat = BuildStructuralMaterialKind.PineTimber,
            BuildSectionThickness targetThickness = BuildSectionThickness.Standard)
        {
            float dy = targetCenter.Y - supportCenter.Y;
            float dx = targetCenter.X - supportCenter.X;
            float dz = targetCenter.Z - supportCenter.Z;
            float horizDist = MathF.Sqrt(dx * dx + dz * dz);

            float verticalLoss = targetKind switch
            {
                ModularBuildPieceKind.StoneFoundation2x2m => 0.06f,
                ModularBuildPieceKind.StoneArchPillar3m => 0.08f,
                ModularBuildPieceKind.LogPillar3m => 0.12f,
                ModularBuildPieceKind.LogBeam2m => 0.13f,
                ModularBuildPieceKind.DiagonalBrace2m => 0.10f,
                ModularBuildPieceKind.GableWall45Deg2m => 0.14f,
                _ => 0.17f
            };

            float horizontalLoss = targetKind switch
            {
                ModularBuildPieceKind.StoneFoundation2x2m => 0.12f,
                ModularBuildPieceKind.StoneArchPillar3m => 0.18f,
                ModularBuildPieceKind.DiagonalBrace2m => 0.10f,
                ModularBuildPieceKind.LogBeam2m => 0.20f,
                ModularBuildPieceKind.GableWall45Deg2m => 0.19f,
                ModularBuildPieceKind.RoofRafter26Deg2x2m => 0.21f,
                ModularBuildPieceKind.RoofRafter45Deg2x2m => 0.21f,
                ModularBuildPieceKind.RoofRidge2m => 0.18f,
                _ => 0.24f // PlankFloor2x2m / PlankWall2x2m: Standart Cam Ahsapta 1.00 -> 0.76 -> 0.52 -> 0.28 -> 0.04 (<0.12 coker!)
            };

            // Malzeme ve kesit kalinligi gercekci mukavemet olceklemesi:
            float vertMatScale = targetMat switch
            {
                BuildStructuralMaterialKind.IronwoodHeavy => 0.68f,
                BuildStructuralMaterialKind.RubbleStone => 0.52f,
                BuildStructuralMaterialKind.CutBasalt => 0.42f,
                BuildStructuralMaterialKind.CarvedMarble => 0.36f,
                BuildStructuralMaterialKind.ReinforcedSteelObsidian => 0.24f,
                _ => 1.00f
            };

            float horizMatScale = targetMat switch
            {
                BuildStructuralMaterialKind.IronwoodHeavy => 0.68f,
                // Tas/kagir basinca cok dayaniklidir ama kolonsuz/kirissiz boslukta yatay konsol aciklik gecemez (cekmeye zayif):
                BuildStructuralMaterialKind.RubbleStone => targetKind == ModularBuildPieceKind.StoneFoundation2x2m ? 1.00f : 1.48f,
                BuildStructuralMaterialKind.CutBasalt => targetKind == ModularBuildPieceKind.StoneFoundation2x2m ? 0.90f : 1.22f,
                BuildStructuralMaterialKind.CarvedMarble => 0.96f,
                BuildStructuralMaterialKind.ReinforcedSteelObsidian => 0.44f,
                _ => 1.00f
            };

            float targetThickScale = targetThickness switch
            {
                BuildSectionThickness.Slender => 1.46f,      // Ince kesit cabuk egilir / burkulur!
                BuildSectionThickness.HeavyBearing => 0.62f, // Kalin kiris/kolon cok daha genis aciklik gecer!
                _ => 1.00f
            };

            float supportThickBoost = supportThickness == BuildSectionThickness.HeavyBearing ? 0.88f :
                                      (supportThickness == BuildSectionThickness.Slender ? 1.16f : 1.00f);

            verticalLoss *= vertMatScale * targetThickScale * supportThickBoost;
            horizontalLoss *= horizMatScale * targetThickScale * supportThickBoost;

            // Ana Tasiyici Kiris (LogBeam2m) uzerine oturan doseme ve catilar yuku kirise aktararak daha genis aciklik gecer!
            if (supportKind == ModularBuildPieceKind.LogBeam2m &&
                (targetKind == ModularBuildPieceKind.PlankFloor2x2m ||
                 targetKind == ModularBuildPieceKind.RoofRafter26Deg2x2m ||
                 targetKind == ModularBuildPieceKind.RoofRafter45Deg2x2m))
            {
                horizontalLoss *= 0.72f;
            }

            float loss;
            if (dy >= 0.28f && horizDist <= 1.35f)
            {
                loss = verticalLoss;
            }
            else if (dy < -0.55f)
            {
                loss = horizontalLoss + 0.28f;
            }
            else
            {
                loss = horizontalLoss;
            }

            // Kagir temel / kolon / celik-demir agaci kaide takviyesi:
            bool strongMasonryOrColumnSupport =
                supportKind == ModularBuildPieceKind.StoneFoundation2x2m ||
                supportKind == ModularBuildPieceKind.StoneArchPillar3m ||
                supportMat == BuildStructuralMaterialKind.CarvedMarble ||
                supportMat == BuildStructuralMaterialKind.CutBasalt ||
                supportMat == BuildStructuralMaterialKind.ReinforcedSteelObsidian;
            if (strongMasonryOrColumnSupport)
            {
                float stoneBoost = supportIsGrounded ? 0.11f : 0.07f;
                loss = MathF.Max(0.03f, loss - stoneBoost);
            }

            return loss;
        }

        /// <summary>
        /// Tum aktif moduler parca cizgesinin Valheim stabilitesini (Stability01 in [0..1]) sifirdan hesaplar;
        /// zeminden kopan (0.0) veya esik altina (&lt; 0.12f) dusen tum parcalari SOTF 3B yercekimi + acisal tork
        /// devrilme moduna (IsFalling = true) gecirir ve kaskad dengeye ulasana kadar yineler (0 B GC).
        /// </summary>
        public static int EvaluateGraphAndTriggerCollapse(
            Span<ModularPlacedPieceNode> nodes,
            Vector3 fractureEpicenter,
            out int newlyCollapsedCount)
        {
            newlyCollapsedCount = 0;
            int count = nodes.Length;
            if (count == 0) return 0;

            Span<Vector3> minBuf = count <= 192 ? stackalloc Vector3[count] : new Vector3[count];
            Span<Vector3> maxBuf = count <= 192 ? stackalloc Vector3[count] : new Vector3[count];
            Span<Vector3> centerBuf = count <= 192 ? stackalloc Vector3[count] : new Vector3[count];
            Span<float> stabBuf = count <= 192 ? stackalloc float[count] : new float[count];
            Span<float> axialLoadBuf = count <= 192 ? stackalloc float[count] : new float[count];
            Span<float> compCapBuf = count <= 192 ? stackalloc float[count] : new float[count];
            Span<int> heightOrder = count <= 192 ? stackalloc int[count] : new int[count];

            for (int i = 0; i < count; i++)
            {
                if (nodes[i].IsBroken || nodes[i].IsFalling) continue;
                ComputeWorldAabb(nodes[i].Kind, nodes[i].Position, nodes[i].Rotation, out minBuf[i], out maxBuf[i]);
                centerBuf[i] = 0.5f * (minBuf[i] + maxBuf[i]);
            }

            bool detachedAnyInPass = true;
            int safetyCascadeGuard = 0;

            while (detachedAnyInPass && safetyCascadeGuard++ < count + 2)
            {
                detachedAnyInPass = false;

                for (int i = 0; i < count; i++)
                {
                    heightOrder[i] = i;
                    if (nodes[i].IsBroken || nodes[i].IsFalling)
                    {
                        stabBuf[i] = 0.0f;
                        axialLoadBuf[i] = 0.0f;
                        compCapBuf[i] = 1.0f;
                        continue;
                    }
                    bool grounded = nodes[i].IsGrounded || minBuf[i].Y <= nodes[i].GroundFloorY + 0.22f;
                    nodes[i].IsGrounded = grounded;
                    stabBuf[i] = grounded ? 1.0f : 0.0f;

                    BuildStructuralMaterialKind effMat = ResolveEffectiveNodeMaterial(in nodes[i]);
                    float compRatio = SotfStructuralBuildMechanics.GetCompletionRatio01(in nodes[i]);
                    axialLoadBuf[i] = SotfStructuralBuildMechanics.ComputePieceSelfWeightKn(
                        nodes[i].Kind, effMat, nodes[i].ThicknessKind, compRatio);
                    compCapBuf[i] = SotfStructuralBuildMechanics.ComputePieceCompressionCapacityKn(
                        nodes[i].Kind, effMat, nodes[i].ThicknessKind);
                }

                // 1) Zemin temasindan yukariya/yana Bellman-Ford yapisal yuk/aciklik gevsetmesi (malzeme + kalinlik duyarli)
                for (int pass = 0; pass < count; pass++)
                {
                    bool anyImproved = false;
                    for (int u = 0; u < count; u++)
                    {
                        float sU = stabBuf[u];
                        if (sU <= CollapseStabilityThreshold) continue;
                        if (nodes[u].IsBroken || nodes[u].IsFalling) continue;
                        BuildStructuralMaterialKind matU = ResolveEffectiveNodeMaterial(in nodes[u]);

                        for (int v = 0; v < count; v++)
                        {
                            if (u == v || nodes[v].IsBroken || nodes[v].IsFalling || nodes[v].IsGrounded) continue;
                            if (!DoAabbsTouch(minBuf[u], maxBuf[u], minBuf[v], maxBuf[v], JointOverlapToleranceMeters)) continue;

                            BuildStructuralMaterialKind matV = ResolveEffectiveNodeMaterial(in nodes[v]);
                            float loss = ComputeDirectionalStabilityLoss(
                                nodes[u].Kind, nodes[v].Kind, centerBuf[u], centerBuf[v], nodes[u].IsGrounded,
                                matU, nodes[u].ThicknessKind, matV, nodes[v].ThicknessKind);
                            float cand = sU - loss;
                            if (cand > stabBuf[v] + 1e-4f)
                            {
                                stabBuf[v] = Math.Clamp(cand, 0.0f, 1.0f);
                                anyImproved = true;
                            }
                        }
                    }

                    if (!anyImproved) break;
                }

                // 2) DiagonalBrace2m (45° Gogusleme Payandasi) ucgenleme rijitlik takviyesi + komsu yayilimi
                bool anyStrutBoost = false;
                for (int b = 0; b < count; b++)
                {
                    if (nodes[b].IsBroken || nodes[b].IsFalling) continue;
                    if (nodes[b].Kind != ModularBuildPieceKind.DiagonalBrace2m) continue;
                    if (stabBuf[b] < 0.20f) continue;

                    float bestAnchorStab = 0.0f;
                    int bestAnchorIdx = -1;
                    for (int a = 0; a < count; a++)
                    {
                        if (a == b || nodes[a].IsBroken || nodes[a].IsFalling) continue;
                        if (nodes[a].Kind == ModularBuildPieceKind.DiagonalBrace2m) continue;
                        if (!DoAabbsTouch(minBuf[b], maxBuf[b], minBuf[a], maxBuf[a], JointOverlapToleranceMeters)) continue;
                        if (stabBuf[a] > bestAnchorStab)
                        {
                            bestAnchorStab = stabBuf[a];
                            bestAnchorIdx = a;
                        }
                    }

                    if (bestAnchorIdx < 0 || bestAnchorStab < 0.30f) continue;

                    for (int t = 0; t < count; t++)
                    {
                        if (t == b || t == bestAnchorIdx || nodes[t].IsBroken || nodes[t].IsFalling || nodes[t].IsGrounded) continue;
                        if (!DoAabbsTouch(minBuf[b], maxBuf[b], minBuf[t], maxBuf[t], JointOverlapToleranceMeters)) continue;

                        float boosted = Math.Clamp(MathF.Max(stabBuf[t] + 0.24f, bestAnchorStab - 0.14f), 0.0f, 0.96f);
                        if (boosted > stabBuf[t] + 1e-4f)
                        {
                            stabBuf[t] = boosted;
                            anyStrutBoost = true;
                        }
                    }
                }

                if (anyStrutBoost)
                {
                    for (int extraPass = 0; extraPass < 4; extraPass++)
                    {
                        bool changed = false;
                        for (int u = 0; u < count; u++)
                        {
                            float sU = stabBuf[u];
                            if (sU <= CollapseStabilityThreshold || nodes[u].IsBroken || nodes[u].IsFalling) continue;
                            BuildStructuralMaterialKind matU = ResolveEffectiveNodeMaterial(in nodes[u]);
                            for (int v = 0; v < count; v++)
                            {
                                if (u == v || nodes[v].IsBroken || nodes[v].IsFalling || nodes[v].IsGrounded) continue;
                                if (!DoAabbsTouch(minBuf[u], maxBuf[u], minBuf[v], maxBuf[v], JointOverlapToleranceMeters)) continue;

                                BuildStructuralMaterialKind matV = ResolveEffectiveNodeMaterial(in nodes[v]);
                                float loss = ComputeDirectionalStabilityLoss(
                                    nodes[u].Kind, nodes[v].Kind, centerBuf[u], centerBuf[v], nodes[u].IsGrounded,
                                    matU, nodes[u].ThicknessKind, matV, nodes[v].ThicknessKind);
                                float cand = sU - loss;
                                if (cand > stabBuf[v] + 1e-4f)
                                {
                                    stabBuf[v] = Math.Clamp(cand, 0.0f, 1.0f);
                                    changed = true;
                                }
                            }
                        }
                        if (!changed) break;
                    }
                }

                // 2.5) Gercekci Yukaridan-Asagiya Tributary Yuk Aktarimi (ust kat/cati/tas agirliginin alt kolon ve duvarlara binmesi):
                //      Ince/zayif malzeme uzerine cok katli veya agir kagir yuk bindiginde alt tasiyici ezilip kirilir!
                for (int i = 1; i < count; i++)
                {
                    int key = heightOrder[i];
                    float keyY = centerBuf[key].Y;
                    int j = i - 1;
                    while (j >= 0 && centerBuf[heightOrder[j]].Y < keyY)
                    {
                        heightOrder[j + 1] = heightOrder[j];
                        j--;
                    }
                    heightOrder[j + 1] = key;
                }

                for (int ord = 0; ord < count; ord++)
                {
                    int u = heightOrder[ord];
                    if (nodes[u].IsBroken || nodes[u].IsFalling || nodes[u].IsGrounded) continue;
                    if (stabBuf[u] < CollapseStabilityThreshold) continue;

                    float weightToTransfer = axialLoadBuf[u];
                    float totalShare = 0.0f;

                    for (int s = 0; s < count; s++)
                    {
                        if (s == u || nodes[s].IsBroken || nodes[s].IsFalling || stabBuf[s] <= 0.01f) continue;
                        if (centerBuf[s].Y > centerBuf[u].Y + 0.12f) continue;
                        if (!DoAabbsTouch(minBuf[u], maxBuf[u], minBuf[s], maxBuf[s], JointOverlapToleranceMeters)) continue;

                        bool isBelow = centerBuf[s].Y < centerBuf[u].Y - 0.15f;
                        bool isColumn = SotfStructuralBuildMechanics.IsDedicatedColumnOrFoundation(nodes[s].Kind);
                        float share = isBelow ? (isColumn ? 3.2f : 2.0f) : (stabBuf[s] > stabBuf[u] ? 0.85f : 0.0f);
                        totalShare += share;
                    }

                    if (totalShare > 1e-4f)
                    {
                        for (int s = 0; s < count; s++)
                        {
                            if (s == u || nodes[s].IsBroken || nodes[s].IsFalling || stabBuf[s] <= 0.01f) continue;
                            if (centerBuf[s].Y > centerBuf[u].Y + 0.12f) continue;
                            if (!DoAabbsTouch(minBuf[u], maxBuf[u], minBuf[s], maxBuf[s], JointOverlapToleranceMeters)) continue;

                            bool isBelow = centerBuf[s].Y < centerBuf[u].Y - 0.15f;
                            bool isColumn = SotfStructuralBuildMechanics.IsDedicatedColumnOrFoundation(nodes[s].Kind);
                            float share = isBelow ? (isColumn ? 3.2f : 2.0f) : (stabBuf[s] > stabBuf[u] ? 0.85f : 0.0f);
                            if (share > 0.0f)
                            {
                                axialLoadBuf[s] += weightToTransfer * (share / totalShare);
                            }
                        }
                    }
                }

                // Basinc kapasitesi asilan (AccumulatedAxialLoadKn > CompressionCapacityKn) alt kolon/duvarlar ezilerek coker!
                for (int i = 0; i < count; i++)
                {
                    if (nodes[i].IsBroken || nodes[i].IsFalling) continue;
                    nodes[i].AccumulatedAxialLoadKn = axialLoadBuf[i];
                    nodes[i].CompressionCapacityKn = compCapBuf[i];

                    float util = axialLoadBuf[i] / MathF.Max(1.0f, compCapBuf[i]);
                    if (util > 1.00f)
                    {
                        stabBuf[i] = 0.0f;
                    }
                    else if (util > 0.72f)
                    {
                        float overloadPenalty = ((util - 0.72f) / 0.28f) * 0.65f;
                        stabBuf[i] = MathF.Max(0.0f, stabBuf[i] - overloadPenalty);
                    }
                }

                // 3) Stabilitesi < 0.12f (veya kopuk/ezilmis = 0.0f) olan parcalari koparip SOTF 3B tork ile dusur!
                for (int i = 0; i < count; i++)
                {
                    if (nodes[i].IsBroken || nodes[i].IsFalling) continue;

                    float s = stabBuf[i];
                    nodes[i].Stability01 = s;
                    nodes[i].StabilityBand = ClassifyStabilityBand(s);

                    if (s < CollapseStabilityThreshold)
                    {
                        TriggerPiecePhysicalCollapse(ref nodes[i], centerBuf[i], fractureEpicenter);
                        newlyCollapsedCount++;
                        detachedAnyInPass = true;
                    }
                }
            }

            int standingCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (!nodes[i].IsBroken && !nodes[i].IsFalling)
                {
                    standingCount++;
                }
            }
            return standingCount;
        }

        private static void TriggerPiecePhysicalCollapse(
            ref ModularPlacedPieceNode node,
            Vector3 worldCenter,
            Vector3 fractureEpicenter)
        {
            node.IsFalling = true;
            node.IsGrounded = false;
            node.FallenElapsedSec = 0.0f;

            Vector3 fromFracture = worldCenter - fractureEpicenter;
            Vector3 horizOffset = new Vector3(fromFracture.X, 0.0f, fromFracture.Z);
            float hLen = horizOffset.Length();
            uint h = (uint)(node.InstanceId * 747796405 + 2891336453u);
            float jitterAngle = ((h & 1023u) / 1024.0f) * MathF.PI * 2.0f;

            Vector3 outwardDir = hLen > 0.05f
                ? (horizOffset / hLen)
                : new Vector3(MathF.Cos(jitterAngle), 0.0f, MathF.Sin(jitterAngle));

            // 3B dogrusal devrilme itkisi + anlik asagi yercekimi baslangici
            float lateralKick = 0.85f + ((h >> 10) & 255u) / 255.0f * 0.95f;
            node.LinearVelocity = outwardDir * lateralKick + new Vector3(0.0f, -0.45f, 0.0f);

            // Gercek fiziksel devrilme torku: r x F_gravity = Cross(outwardDir, -UnitY) -> disariya dogru devrilme ekseni!
            Vector3 gravityTorqueAxis = Vector3.Cross(outwardDir, -Vector3.UnitY);
            if (gravityTorqueAxis.LengthSquared() < 1e-4f)
            {
                gravityTorqueAxis = Vector3.UnitX;
            }
            else
            {
                gravityTorqueAxis = Vector3.Normalize(gravityTorqueAxis);
            }

            float spinRate = 1.65f + ((h >> 18) & 255u) / 255.0f * 1.85f;
            float twistYaw = (((int)((h >> 24) & 63u) - 32) / 32.0f) * 0.95f;
            node.AngularVelocityRadPerSec = gravityTorqueAxis * spinRate + new Vector3(0.0f, twistYaw, 0.0f);
        }

        /// <summary>
        /// Belirtilen dunya noktasindaki moduler destek parcalarina kazma/balta/kusatma hasari uygular;
        /// kirilan parcalar (Health &lt;= 0) silinir ve cizge yeniden hesaplanarak ustteki parcalar SOTF fizigiyle coker!
        /// </summary>
        public static int ApplyDamageAtPoint(
            Span<ModularPlacedPieceNode> nodes,
            Vector3 hitWorldPoint,
            float hitRadius,
            float damage,
            out int brokenSupportCount,
            out int collapsedPieceCount)
        {
            brokenSupportCount = 0;
            collapsedPieceCount = 0;
            if (nodes.Length == 0 || damage <= 0.0f || hitRadius <= 0.0f) return 0;

            float minHitDist = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsBroken || nodes[i].IsFalling) continue;
                ComputeWorldAabb(nodes[i].Kind, nodes[i].Position, nodes[i].Rotation, out Vector3 bMin, out Vector3 bMax);
                float d = DistancePointToAabb(hitWorldPoint, bMin, bMax);
                if (d <= hitRadius && d < minHitDist)
                {
                    minHitDist = d;
                }
            }

            if (minHitDist == float.MaxValue)
            {
                return 0;
            }

            bool focusedToolSwing = hitRadius <= 1.65f;
            int hitCount = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsBroken || nodes[i].IsFalling) continue;
                ComputeWorldAabb(nodes[i].Kind, nodes[i].Position, nodes[i].Rotation, out Vector3 bMin, out Vector3 bMax);
                float d = DistancePointToAabb(hitWorldPoint, bMin, bMax);
                if (d > hitRadius) continue;
                if (focusedToolSwing && d > minHitDist + 0.30f) continue;

                float effectiveDamage = focusedToolSwing
                    ? damage
                    : damage * Math.Clamp(1.0f - 0.72f * (d / hitRadius), 0.20f, 1.0f);

                hitCount++;
                nodes[i].Health -= effectiveDamage;
                if (nodes[i].Health <= 0.0f)
                {
                    nodes[i].Health = 0.0f;
                    nodes[i].IsBroken = true;
                    nodes[i].Stability01 = 0.0f;
                    nodes[i].StabilityBand = ValheimStabilityBand.Red;
                    brokenSupportCount++;
                }
            }

            if (brokenSupportCount > 0)
            {
                EvaluateGraphAndTriggerCollapse(nodes, hitWorldPoint, out collapsedPieceCount);
            }

            return hitCount;
        }

        /// <summary>
        /// SOTF fiziksel yikim modundaki (IsFalling == true) tum parcalarin 3B yercekimi ivmesini,
        /// acisal tork donusunu ve zemin carpma sonumlenmesini ilerletir (0 B GC).
        /// </summary>
        public static int StepFallingPieces(Span<ModularPlacedPieceNode> nodes, float dt)
        {
            if (dt <= 0.0f || nodes.Length == 0) return 0;
            float clampedDt = MathF.Min(dt, 0.05f);
            int activeFalling = 0;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsBroken || !nodes[i].IsFalling) continue;
                activeFalling++;
                ref ModularPlacedPieceNode p = ref nodes[i];
                p.FallenElapsedSec += clampedDt;

                p.LinearVelocity.Y -= GravityAccelMps2 * clampedDt;
                p.Position += p.LinearVelocity * clampedDt;

                float omegaMag = p.AngularVelocityRadPerSec.Length();
                if (omegaMag > 1e-4f)
                {
                    Quaternion deltaRot = Quaternion.CreateFromAxisAngle(
                        p.AngularVelocityRadPerSec / omegaMag, omegaMag * clampedDt);
                    p.Rotation = Quaternion.Normalize(Quaternion.Concatenate(p.Rotation, deltaRot));
                }

                float floorLimitY = p.GroundFloorY + 0.08f;
                if (p.Position.Y < floorLimitY)
                {
                    p.Position.Y = floorLimitY;
                    if (p.LinearVelocity.Y < -0.6f)
                    {
                        p.LinearVelocity.Y = -p.LinearVelocity.Y * 0.18f;
                    }
                    else
                    {
                        p.LinearVelocity.Y = 0.0f;
                    }
                    p.LinearVelocity.X *= MathF.Max(0.0f, 1.0f - 5.5f * clampedDt);
                    p.LinearVelocity.Z *= MathF.Max(0.0f, 1.0f - 5.5f * clampedDt);
                    p.AngularVelocityRadPerSec *= MathF.Max(0.0f, 1.0f - 4.8f * clampedDt);
                }
            }

            return activeFalling;
        }

        private ref struct SitePieceEmitter
        {
            public Span<ModularPlacedPieceNode> OutPieces;
            public Vector3 WorldOrigin;
            public Quaternion SiteRot;
            public int ChunkOwnerKey;
            public int WriteCount;
            public int NextId;

            public void Emit(ModularBuildPieceKind kind, float lx, float ly, float lz, float localYawDeg, bool grounded = false)
            {
                if (WriteCount >= OutPieces.Length) return;
                Vector3 wPos = WorldOrigin + Vector3.Transform(new Vector3(lx, ly, lz), SiteRot);
                Quaternion wRot = Quaternion.Normalize(Quaternion.Concatenate(CreateYawQuaternion(localYawDeg), SiteRot));
                float maxHp = GetPieceDefaultMaxHealth(kind);
                OutPieces[WriteCount++] = new ModularPlacedPieceNode
                {
                    InstanceId = NextId++,
                    Kind = kind,
                    Position = wPos,
                    Rotation = wRot,
                    GroundFloorY = WorldOrigin.Y,
                    Health = maxHp,
                    MaxHealth = maxHp,
                    Stability01 = grounded ? 1.0f : 0.8f,
                    StabilityBand = grounded ? ValheimStabilityBand.Blue : ValheimStabilityBand.Green,
                    IsGrounded = grounded,
                    IsFalling = false,
                    IsBroken = false,
                    LinearVelocity = Vector3.Zero,
                    AngularVelocityRadPerSec = Vector3.Zero,
                    FallenElapsedSec = 0.0f,
                    ChunkOwnerKey = ChunkOwnerKey
                };
            }
        }

        /// <summary>
        /// Maden tahkimati sorgusu (0 B GC): Belirtilen dunya vokselinin (wx, wy, wz) altinda/etrafinda
        /// zemine bagli, yikilmamis tasiyici ahsap/tas moduler destek (LogPillar3m, LogBeam2m, DiagonalBrace2m vb.)
        /// bulunup bulunmadigini denetler.
        /// </summary>
        public static bool HasGroundedTimberSupportAtVoxel(
            ReadOnlySpan<ModularPlacedPieceNode> nodes,
            int wx,
            int wy,
            int wz)
        {
            if (nodes.Length == 0) return false;

            float vx = wx + 0.5f;
            float vy = wy + 0.5f;
            float vz = wz + 0.5f;
            const float horizShoringReach = 1.65f;

            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref nodes[i];
                if (n.IsBroken || n.IsFalling || n.Stability01 < CollapseStabilityThreshold) continue;

                bool isShoringSupport =
                    n.Kind == ModularBuildPieceKind.LogPillar3m ||
                    n.Kind == ModularBuildPieceKind.LogBeam2m ||
                    n.Kind == ModularBuildPieceKind.DiagonalBrace2m ||
                    n.Kind == ModularBuildPieceKind.StoneArchPillar3m ||
                    n.Kind == ModularBuildPieceKind.StoneFoundation2x2m;
                if (!isShoringSupport) continue;

                ComputeWorldAabb(n.Kind, n.Position, n.Rotation, out Vector3 bMin, out Vector3 bMax);
                float dx = MathF.Max(0.0f, MathF.Max(bMin.X - vx, vx - bMax.X));
                float dz = MathF.Max(0.0f, MathF.Max(bMin.Z - vz, vz - bMax.Z));
                if ((dx * dx + dz * dz) > horizShoringReach * horizShoringReach) continue;

                if (vy >= bMin.Y - 0.65f && wy <= bMax.Y + 2.50f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Pencere-tabanli maden tahkimati on-elemesi ve tek seferlik yerel maske rasterizasyonu (0 B GC):
        /// Solve penceresiyle kesisen hicbir tasiyici moduler parca yoksa outMask'a dokunmadan hemen false doner;
        /// kesisen parcalar varsa yalnizca o parcalarin 1.65m tahkimat etki hacmini outMask'a yazar ve true doner.
        /// </summary>
        public static bool TryRasterizeGroundedTimberSupportInWindow(
            ReadOnlySpan<ModularPlacedPieceNode> nodes,
            int originX,
            int originY,
            int originZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            Span<byte> outMask,
            bool isZMajorLocal)
        {
            if (nodes.Length == 0 || sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) return false;
            int requiredLen = sizeX * sizeY * sizeZ;
            if (outMask.Length < requiredLen) return false;

            const float horizShoringReach = 1.65f;
            const float reachSq = horizShoringReach * horizShoringReach;
            float winMinX = originX;
            float winMaxX = originX + sizeX;
            float winMinY = originY;
            float winMaxY = originY + sizeY;
            float winMinZ = originZ;
            float winMaxZ = originZ + sizeZ;

            bool anyWritten = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly ModularPlacedPieceNode n = ref nodes[i];
                if (n.IsBroken || n.IsFalling || n.Stability01 < CollapseStabilityThreshold) continue;

                bool isShoringSupport =
                    n.Kind == ModularBuildPieceKind.LogPillar3m ||
                    n.Kind == ModularBuildPieceKind.LogBeam2m ||
                    n.Kind == ModularBuildPieceKind.DiagonalBrace2m ||
                    n.Kind == ModularBuildPieceKind.StoneArchPillar3m ||
                    n.Kind == ModularBuildPieceKind.StoneFoundation2x2m;
                if (!isShoringSupport) continue;

                // Hizli merkez kaba elemesi (tum parcalar <= 3.5m yaricaptadir; pencereden uzaktaki yuzlerce parcada ComputeWorldAabb atlanir)
                if (n.Position.X + 4.5f < winMinX || n.Position.X - 4.5f > winMaxX ||
                    n.Position.Z + 4.5f < winMinZ || n.Position.Z - 4.5f > winMaxZ ||
                    n.Position.Y + 6.0f < winMinY || n.Position.Y - 4.0f > winMaxY)
                {
                    continue;
                }

                ComputeWorldAabb(n.Kind, n.Position, n.Rotation, out Vector3 bMin, out Vector3 bMax);
                if (bMax.X + horizShoringReach < winMinX || bMin.X - horizShoringReach > winMaxX ||
                    bMax.Z + horizShoringReach < winMinZ || bMin.Z - horizShoringReach > winMaxZ ||
                    bMax.Y + 2.50f < winMinY || bMin.Y - 0.65f > winMaxY)
                {
                    continue;
                }

                int minLx = Math.Max(0, (int)MathF.Floor(bMin.X - horizShoringReach - 0.5f) - originX);
                int maxLx = Math.Min(sizeX - 1, (int)MathF.Ceiling(bMax.X + horizShoringReach - 0.5f) - originX);
                int minLy = Math.Max(0, (int)MathF.Floor(bMin.Y - 0.65f - 0.5f) - originY);
                int maxLy = Math.Min(sizeY - 1, (int)MathF.Ceiling(bMax.Y + 2.50f) - originY);
                int minLz = Math.Max(0, (int)MathF.Floor(bMin.Z - horizShoringReach - 0.5f) - originZ);
                int maxLz = Math.Min(sizeZ - 1, (int)MathF.Ceiling(bMax.Z + horizShoringReach - 0.5f) - originZ);

                if (minLx > maxLx || minLy > maxLy || minLz > maxLz) continue;

                for (int lz = minLz; lz <= maxLz; lz++)
                {
                    float vz = originZ + lz + 0.5f;
                    float dz = MathF.Max(0.0f, MathF.Max(bMin.Z - vz, vz - bMax.Z));
                    float dzSq = dz * dz;
                    if (dzSq > reachSq) continue;

                    for (int lx = minLx; lx <= maxLx; lx++)
                    {
                        float vx = originX + lx + 0.5f;
                        float dx = MathF.Max(0.0f, MathF.Max(bMin.X - vx, vx - bMax.X));
                        if ((dx * dx + dzSq) > reachSq) continue;

                        for (int ly = minLy; ly <= maxLy; ly++)
                        {
                            int wy = originY + ly;
                            float vy = wy + 0.5f;
                            if (vy < bMin.Y - 0.65f || wy > bMax.Y + 2.50f) continue;

                            if (!anyWritten)
                            {
                                outMask.Slice(0, requiredLen).Clear();
                                anyWritten = true;
                            }

                            int idx = isZMajorLocal
                                ? (lx + sizeX * (ly + sizeY * lz))
                                : (((ly * sizeZ) + lz) * sizeX + lx);
                            outMask[idx] = 1;
                        }
                    }
                }
            }

            return anyWritten;
        }

        /// <summary>
        /// Prosedurel moduler yerleske (0=NordicTimberCabin, 1=MeadHallWatchtower, 2=CarvedMarbleShrine, 3=CollapsedTimberStoneRuin)
        /// parcalarini %100 kapali, sizdirmaz, on/arka GableWall45Deg2m alinlikli ve derin tas subasmanli olarak kurar (0 B GC).
        /// </summary>
        public static int BuildProceduralSitePieces(
            byte siteKindIndex,
            Vector3 worldOrigin,
            float siteYawDegrees,
            int chunkOwnerKey,
            Span<ModularPlacedPieceNode> outPieces,
            int startingInstanceId = 1)
        {
            if (siteKindIndex >= SettlementBuildingBlueprints.KindVillageWell &&
                siteKindIndex < SettlementBuildingBlueprints.TotalSiteKindCount)
            {
                return SettlementBuildingBlueprints.BuildExtendedSitePieces(
                    siteKindIndex, worldOrigin, siteYawDegrees, chunkOwnerKey, outPieces, startingInstanceId);
            }

            var em = new SitePieceEmitter
            {
                OutPieces = outPieces,
                WorldOrigin = worldOrigin,
                SiteRot = CreateYawQuaternion(siteYawDegrees),
                ChunkOwnerKey = chunkOwnerKey,
                WriteCount = 0,
                NextId = startingInstanceId
            };

            switch (siteKindIndex)
            {
                case 0: // NordicTimberCabin (%100 Kapali 4x4m Nordik Kutuk Kulube + On/Arka 45° GableWall + Dik Zona Cati)
                {
                    // Derin egim etekli harcli tas subasman (4x StoneFoundation2x2m) + ahsap doseme
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 1.0f, 0f, grounded: true);

                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, 1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, 1.0f, 0f);

                    // 4 Tam Cephe Duvari (kapi + kepenkli pencereler + gecmeli kutuk duvarlar)
                    em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.94f, 0.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 0.32f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 0.32f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.94f, 0.32f, 1.0f, 270f);

                    // Yan sacak asik kutukleri + on/arka alinlik merkez kral dikmeleri + gogusleme payandalari
                    em.Emit(ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogPillar3m, 0.0f, 1.28f, -1.88f, 0f);
                    em.Emit(ModularBuildPieceKind.LogPillar3m, 0.0f, 1.28f, 1.88f, 0f);
                    em.Emit(ModularBuildPieceKind.DiagonalBrace2m, -1.18f, 1.86f, -1.90f, 0f);
                    em.Emit(ModularBuildPieceKind.DiagonalBrace2m, 1.18f, 1.86f, -1.90f, 180f);

                    // On ve Arka 45° Ucgen Cati Alinlik Duvarlari (Λ tam sızdırmaz kapanis!)
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, -1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, 1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, 1.94f, 180f);

                    // Tam Iki Yan 45° Dik Zona Cati Egimi + Ust Mahya Kirisi
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, 1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, 1.0f, 90f);
                    break;
                }

                case 1: // MeadHallWatchtower (2 Tam Katli %100 Kapali Nordik Gozetleme Kulesi + On/Arka 45° GableWall + Dik Cati)
                {
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 1.0f, 0f, grounded: true);

                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, 1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, 1.0f, 0f);

                    // 1. Kat Tam Cevre Duvarlari + Kose Tasiyici Kutuk Dikmeler
                    em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 0.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.94f, 0.32f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.94f, 0.32f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 0.32f, 1.0f, 270f);

                    em.Emit(ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, -1.90f, 0f);
                    em.Emit(ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, -1.90f, 0f);
                    em.Emit(ModularBuildPieceKind.LogPillar3m, -1.90f, 0.32f, 1.90f, 0f);
                    em.Emit(ModularBuildPieceKind.LogPillar3m, 1.90f, 0.32f, 1.90f, 0f);

                    // 2. Kat Doseme + 2. Kat Tam Kepenkli Gozetleme Duvarlari + Payandalar
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 2.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 2.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 2.32f, 1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 2.32f, 1.0f, 0f);

                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 2.34f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 2.34f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 2.34f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.0f, 2.34f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.94f, 2.34f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 2.34f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 2.34f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.94f, 2.34f, 1.0f, 270f);

                    em.Emit(ModularBuildPieceKind.DiagonalBrace2m, -1.18f, 2.88f, -1.90f, 0f);
                    em.Emit(ModularBuildPieceKind.DiagonalBrace2m, 1.18f, 2.88f, -1.90f, 180f);

                    // On ve Arka 45° Ucgen Cati Alinliklari + Iki Yan 45° Cati + Mahya
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 4.34f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 4.34f, -1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 4.34f, 1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 4.34f, 1.94f, 180f);

                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 4.36f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 4.36f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 4.36f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 4.36f, 1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 6.34f, 1.0f, 90f);
                    break;
                }

                case 2: // CarvedMarbleShrine (%100 Kapali Tas Kemer Portalli & 45° GableWall Alinlikli Kadim Tapinak)
                {
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 1.0f, 0f, grounded: true);

                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, 1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, 1.0f, 0f);

                    // 4 Tam Kutsal Salon Duvari (cift giris portali + kepenkli pencereler)
                    em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.92f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, 1.0f, 0.32f, -1.92f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.0f, 0.32f, 1.92f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.92f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.92f, 0.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.92f, 0.32f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.92f, 0.32f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.92f, 0.32f, 1.0f, 270f);

                    // On ve arka portallarda cati egimi altina (3.32m < 3.48m) temiz oturan tas kemer sutunlari:
                    // Yan sacak LogBeam2m (X=±1.94m) ile X ekseninde >1.0m ayriktir; icinden ASLA kiris gecmez!
                    em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -0.84f, 0.32f, -2.02f, 0f);
                    em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 0.84f, 0.32f, -2.02f, 0f);
                    em.Emit(ModularBuildPieceKind.StoneArchPillar3m, -0.84f, 0.32f, 2.02f, 0f);
                    em.Emit(ModularBuildPieceKind.StoneArchPillar3m, 0.84f, 0.32f, 2.02f, 0f);

                    em.Emit(ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, 1.0f, 90f);

                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, -1.92f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, -1.92f, 180f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, 1.92f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, 1.92f, 180f);

                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, 1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, 1.0f, 90f);
                    break;
                }

                default: // 3 = Tahkimli Sınır Karakol Evi (%100 Kapali, 0 Kopuk Direk, 0 Ucan Yarim Cati!)
                {
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, -1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, -1.0f, 0.0f, 1.0f, 0f, grounded: true);
                    em.Emit(ModularBuildPieceKind.StoneFoundation2x2m, 1.0f, 0.0f, 1.0f, 0f, grounded: true);

                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, -1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, -1.0f, 0.32f, 1.0f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankFloor2x2m, 1.0f, 0.32f, 1.0f, 0f);

                    em.Emit(ModularBuildPieceKind.PlankDoorway2x2m, -1.0f, 0.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.0f, 0.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.0f, 0.32f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.0f, 0.32f, 1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, -1.94f, 0.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, -1.94f, 0.32f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.PlankWall2x2m, 1.94f, 0.32f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.PlankWindow2x2m, 1.94f, 0.32f, 1.0f, 270f);

                    em.Emit(ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, -1.94f, 2.28f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.LogBeam2m, 1.94f, 2.28f, 1.0f, 90f);

                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, -1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, -1.94f, 180f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, -1.0f, 2.32f, 1.94f, 0f);
                    em.Emit(ModularBuildPieceKind.GableWall45Deg2m, 1.0f, 2.32f, 1.94f, 180f);

                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, -1.0f, 2.34f, 1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, -1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRafter45Deg2x2m, 1.0f, 2.34f, 1.0f, 270f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, -1.0f, 90f);
                    em.Emit(ModularBuildPieceKind.RoofRidge2m, 0.0f, 4.32f, 1.0f, 90f);
                    break;
                }
            }

            Span<ModularPlacedPieceNode> slice = outPieces.Slice(0, em.WriteCount);
            EvaluateGraphAndTriggerCollapse(slice, worldOrigin, out _);
            return em.WriteCount;
        }

        public static Quaternion CreateYawQuaternion(float yawDegrees)
        {
            float rad = yawDegrees * (MathF.PI / 180.0f);
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, rad);
        }

        public static float ExtractYawDegrees(Quaternion q)
        {
            Vector3 fwd = Vector3.Transform(Vector3.UnitZ, q);
            if (fwd.X * fwd.X + fwd.Z * fwd.Z < 1e-5f) return 0.0f;
            float deg = MathF.Atan2(fwd.X, fwd.Z) * (180.0f / MathF.PI);
            if (deg < 0.0f) deg += 360.0f;
            return deg;
        }

        /// <summary>
        /// <see cref="ModularPlacedPieceNode"/> dugumunu 0-GC ile birlesik <see cref="StructuralNode64"/> graf dugumune donusturur.
        /// </summary>
        public static StructuralNode64 ConvertModularPieceToStructuralNode64(
            in ModularPlacedPieceNode piece,
            uint overrideNodeId = 0u,
            MortarType mortar = MortarType.None,
            bool instantCure = true,
            uint ownerClanOrPlayerId = 0u)
        {
            BuildStructuralMaterialKind effMat = ResolveEffectiveNodeMaterial(in piece);
            byte materialId = SotfStructuralBuildMechanics.GetMaterialProfile(effMat).VoxelMaterialId;
            if (materialId == 0) materialId = MaterialIdCatalog.OakWood;

            ArchitecturalSchool school = effMat switch
            {
                BuildStructuralMaterialKind.CarvedMarble or
                BuildStructuralMaterialKind.ReinforcedSteelObsidian => ArchitecturalSchool.MonumentalArchDome,
                BuildStructuralMaterialKind.RubbleStone or
                BuildStructuralMaterialKind.CutBasalt => ArchitecturalSchool.StoneMasonry,
                _ => ArchitecturalSchool.LogCabin
            };

            StructuralElementRole role = piece.Kind switch
            {
                ModularBuildPieceKind.StoneFoundation2x2m => StructuralElementRole.StonePlinthFoundation,
                ModularBuildPieceKind.LogPillar3m => StructuralElementRole.TimberPost,
                ModularBuildPieceKind.StoneArchPillar3m => StructuralElementRole.MonolithicColumnShaft,
                ModularBuildPieceKind.LogBeam2m => StructuralElementRole.TimberMainGirder,
                ModularBuildPieceKind.PlankFloor2x2m => StructuralElementRole.FloorJoistDeck,
                ModularBuildPieceKind.PlankDoorway2x2m => StructuralElementRole.DoorFrameOpening,
                ModularBuildPieceKind.PlankWindow2x2m => StructuralElementRole.WindowFrameOpening,
                ModularBuildPieceKind.RoofRafter26Deg2x2m or ModularBuildPieceKind.RoofRafter45Deg2x2m => StructuralElementRole.InclinedRoofRafter,
                ModularBuildPieceKind.RoofRidge2m => StructuralElementRole.RoofRidgeBeam,
                ModularBuildPieceKind.DiagonalBrace2m => StructuralElementRole.DiagonalWindBrace,
                ModularBuildPieceKind.GableWall45Deg2m => StructuralElementRole.InfillPanelWattleAdobe,
                _ => school == ArchitecturalSchool.StoneMasonry
                    ? StructuralElementRole.AshlarMasonryWall
                    : StructuralElementRole.NotchedWallLog
            };

            MortarType resolvedMortar = mortar != MortarType.None
                ? mortar
                : (school == ArchitecturalSchool.StoneMasonry && role != StructuralElementRole.StonePlinthFoundation
                    ? MortarType.Lime
                    : MortarType.None);

            AnchorType anchor = piece.IsGrounded
                ? (role == StructuralElementRole.StonePlinthFoundation || school == ArchitecturalSchool.StoneMasonry
                    ? AnchorType.CompactedGravelOrTrench
                    : AnchorType.FirmDrySoilOrClay)
                : AnchorType.NoneFloating;

            StructureThicknessProfile thickness = piece.ThicknessKind switch
            {
                BuildSectionThickness.Slender => StructureThicknessProfile.Slender,
                BuildSectionThickness.HeavyBearing => StructureThicknessProfile.HeavyBearing,
                _ => StructureThicknessProfile.Standard
            };

            float yawDeg = ExtractYawDegrees(piece.Rotation);
            byte yawQuarter = (byte)(((int)MathF.Round(yawDeg / 90f) % 4 + 4) & 0x03);
            uint id = overrideNodeId != 0u ? overrideNodeId : (uint)Math.Max(1, piece.InstanceId);

            StructuralNode64 node = StructuralNode64.Create(
                nodeId: id,
                posX: piece.Position.X,
                posY: piece.Position.Y,
                posZ: piece.Position.Z,
                school: school,
                role: role,
                materialId: materialId,
                anchor: anchor,
                mortar: resolvedMortar,
                yawQuarterTurns: yawQuarter,
                thickness: thickness,
                ownerClanOrPlayerId: ownerClanOrPlayerId,
                instantCure: instantCure);

            float maxHp = piece.MaxHealth > 0.01f ? piece.MaxHealth : ComputePieceMaxHealth(piece.Kind, effMat, piece.ThicknessKind);
            node.Integrity01 = piece.IsBroken ? 0f : Math.Clamp(piece.Health / maxHp, 0f, 1f);
            node.GeoSupportDist = piece.IsGrounded
                ? (ushort)0
                : (piece.IsFalling || piece.IsBroken ? MortarConstants.DetachedGeodesicDistance : (ushort)1);
            node.CollapseState = piece.IsBroken
                ? StructureCollapseState.Collapsed
                : (piece.IsFalling ? StructureCollapseState.DetachedInstantFall : StructureCollapseState.Stable);
            node.CompletedCourses = SotfStructuralBuildMechanics.GetEffectiveCompletedCourses(in piece);
            node.Chinking = school == ArchitecturalSchool.LogCabin || school == ArchitecturalSchool.TimberFrame
                ? ChinkingFlags.ClayDaubed | ChinkingFlags.WeatherSealed
                : ChinkingFlags.LimePlastered | ChinkingFlags.WeatherSealed;
            return node;
        }

        /// <summary>
        /// <see cref="StructuralNode64"/> dugumunu moduler yerlestirme eslenigine donusturur (0-GC).
        /// </summary>
        public static ModularPlacedPieceNode ConvertStructuralNode64ToModularPiece(
            in StructuralNode64 node,
            int chunkOwnerKey = 0)
        {
            ModularBuildPieceKind kind = node.Role switch
            {
                StructuralElementRole.StonePlinthFoundation or StructuralElementRole.TrenchRubbleFooting => ModularBuildPieceKind.StoneFoundation2x2m,
                StructuralElementRole.TimberPost => ModularBuildPieceKind.LogPillar3m,
                StructuralElementRole.MonolithicColumnShaft => ModularBuildPieceKind.StoneArchPillar3m,
                StructuralElementRole.TimberMainGirder => ModularBuildPieceKind.LogBeam2m,
                StructuralElementRole.FloorJoistDeck => ModularBuildPieceKind.PlankFloor2x2m,
                StructuralElementRole.DoorFrameOpening => ModularBuildPieceKind.PlankDoorway2x2m,
                StructuralElementRole.WindowFrameOpening => ModularBuildPieceKind.PlankWindow2x2m,
                StructuralElementRole.InclinedRoofRafter or StructuralElementRole.RoofCoveringCourse => ModularBuildPieceKind.RoofRafter45Deg2x2m,
                StructuralElementRole.RoofRidgeBeam => ModularBuildPieceKind.RoofRidge2m,
                StructuralElementRole.DiagonalWindBrace => ModularBuildPieceKind.DiagonalBrace2m,
                _ => ModularBuildPieceKind.PlankWall2x2m
            };

            BuildStructuralMaterialKind mat = node.School == ArchitecturalSchool.MonumentalArchDome
                ? BuildStructuralMaterialKind.CarvedMarble
                : (node.School == ArchitecturalSchool.StoneMasonry
                    ? BuildStructuralMaterialKind.RubbleStone
                    : BuildStructuralMaterialKind.PineTimber);

            BuildSectionThickness thick = (StructureThicknessProfile)node.ThicknessProfile switch
            {
                StructureThicknessProfile.Slender => BuildSectionThickness.Slender,
                StructureThicknessProfile.HeavyBearing => BuildSectionThickness.HeavyBearing,
                _ => BuildSectionThickness.Standard
            };

            bool isCollapsedOrDetached = node.CollapseState == StructureCollapseState.Collapsed ||
                                         node.CollapseState == StructureCollapseState.DetachedInstantFall;
            float maxHp = GetPieceDefaultMaxHealth(kind);
            return new ModularPlacedPieceNode
            {
                InstanceId = (int)node.NodeId,
                Kind = kind,
                MaterialKind = mat,
                ThicknessKind = thick,
                ExplicitMaterialAssigned = true,
                CompletedCourses = node.CompletedCourses == 0 ? (byte)1 : node.CompletedCourses,
                RequiredCourses = 1,
                Position = new Vector3(node.PosX, node.PosY, node.PosZ),
                Rotation = CreateYawQuaternion((node.YawQuarterTurns & 3) * 90f),
                GroundFloorY = node.PosY,
                Health = maxHp * Math.Clamp(node.Integrity01, 0f, 1f),
                MaxHealth = maxHp,
                Stability01 = isCollapsedOrDetached ? 0f : Math.Clamp(1.0f - node.StressRatio * 0.5f, 0.15f, 1.0f),
                StabilityBand = isCollapsedOrDetached ? ValheimStabilityBand.Red : ValheimStabilityBand.Green,
                IsGrounded = node.Anchor != AnchorType.NoneFloating || node.GeoSupportDist == 0,
                IsFalling = node.CollapseState == StructureCollapseState.DetachedInstantFall,
                IsBroken = node.CollapseState == StructureCollapseState.Collapsed,
                ChunkOwnerKey = chunkOwnerKey
            };
        }

        /// <summary>
        /// Aktif <see cref="ModularPlacedPieceNode"/> dugumlerini komsu baglantilariyla birlikte
        /// <see cref="UnifiedStructureGraphSolver"/> icine esitler (0-GC).
        /// </summary>
        public static int SyncModularNodesToUnifiedSolver(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            UnifiedStructureGraphSolver solver,
            uint nodeIdBaseOffset = 2000000u,
            float connectRadiusMeters = 2.65f,
            MortarType defaultMasonryMortar = MortarType.Lime,
            bool instantCure = true)
        {
            if (solver == null || pieces.IsEmpty) return 0;

            float maxDistSq = connectRadiusMeters * connectRadiusMeters;
            int synced = 0;

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode p = ref pieces[i];
                if (p.IsBroken || p.InstanceId <= 0) continue;

                uint nid = nodeIdBaseOffset + (uint)p.InstanceId;
                StructuralNode64 node = ConvertModularPieceToStructuralNode64(
                    in p,
                    overrideNodeId: nid,
                    mortar: defaultMasonryMortar,
                    instantCure: instantCure);
                solver.AddOrUpdateNode(in node);
                synced++;

                for (int j = 0; j < i; j++)
                {
                    ref readonly ModularPlacedPieceNode other = ref pieces[j];
                    if (other.IsBroken || other.InstanceId <= 0) continue;

                    float dx = p.Position.X - other.Position.X;
                    float dy = p.Position.Y - other.Position.Y;
                    float dz = p.Position.Z - other.Position.Z;
                    float distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq > maxDistSq || distSq < 0.01f) continue;

                    JointConnectionKind jKind = node.School == ArchitecturalSchool.StoneMasonry
                        ? (defaultMasonryMortar == MortarType.None ? JointConnectionKind.DryStoneInterlock : JointConnectionKind.LimeMortar)
                        : (node.School == ArchitecturalSchool.TimberFrame ? JointConnectionKind.MortiseTenonPeg : JointConnectionKind.SaddleNotch);

                    uint otherNid = nodeIdBaseOffset + (uint)other.InstanceId;
                    float cure01 = instantCure || node.Mortar == MortarType.None ? 1.0f : node.CureRatio01;
                    solver.ConnectNodes(nid, otherNid, jKind, cureRatio01: cure01);
                }
            }

            return synced;
        }
    }
}
