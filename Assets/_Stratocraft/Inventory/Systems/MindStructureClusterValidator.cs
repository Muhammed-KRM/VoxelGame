#nullable enable
using System;
using System.Numerics;
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
}
