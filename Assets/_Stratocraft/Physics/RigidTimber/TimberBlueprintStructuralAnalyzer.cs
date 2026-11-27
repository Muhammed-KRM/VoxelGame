#nullable enable
using System;
using System.Numerics;

namespace Stratocraft.Physics.RigidTimber
{
    public enum BlueprintPieceKind : byte
    {
        Post = 0,
        WallLog = 1,
        FloorBeam = 2,
        FloorDeck = 3,
        RoofSlope = 4,
        RidgeOrTieBeam = 5
    }

    public enum BlueprintStabilityTier : byte
    {
        GroundedBlue = 0,
        SafeGreen = 1,
        CautionYellow = 2,
        CriticalRed = 3,
        BuiltSolid = 4
    }

    public enum BlueprintFailureReason : byte
    {
        None = 0,
        NoGroundPath = 1,
        UnsupportedSpanTooLong = 2,
        HighBendingStress = 3,
        MissingRoofTieBeam = 4,
        CantileverOverturn = 5,
        ShallowSoilEmbed = 6,
        PostOverloaded = 7
    }

    public struct BlueprintNodeSpec
    {
        public uint Id;
        public BlueprintPieceKind Kind;
        public Vector3 Position;
        public Quaternion Rotation;
        public float LengthM;
        public float DiameterM;
        public Vector3 HalfExtents;
        public TimberWoodSpecies Species;
        public bool IsGrounded;
        public float PlannedEmbedDepthM;
        public bool IsBuilt;
    }

    public struct BlueprintNodeEvaluation
    {
        public BlueprintStabilityTier Tier;
        public int StabilityPct;
        public float StressRatio01;
        public float PredictedSagMm;
        public bool WillCollapse;
        public BlueprintFailureReason Reason;
    }

    public struct BlueprintPlanSummary
    {
        public int TotalPieces;
        public int BuiltPieces;
        public int GroundedPosts;
        public int SafeCount;
        public int WarningCount;
        public int CollapseCount;
        public bool WillStand;
        public int OverallStabilityPct;
        public BlueprintFailureReason PrimaryFailure;
    }

    /// <summary>
    /// Insa oncesi 3B Mimari Plan (Blueprint) Yapisal Dayaniklilik Simulatörü.
    /// Gercek TimberStaticEquilibriumSolver + WoodSpeciesPhysicsLut + Ground-Anchor BFS uzerinden
    /// her planlanan parcanin ayakta kalip kalmayacagini, gerilme oranini ve sehimini (mm) hesaplar.
    /// </summary>
    public static class TimberBlueprintStructuralAnalyzer
    {
        public const float MinSafePostEmbedDepthM = 0.45f;
        public const float MaxSafeUnsupportedSpanM = 4.20f;
        public const float CautionUnsupportedSpanM = 3.40f;

        private static readonly TimberStaticEquilibriumSolver _solver = new();

        public static BlueprintPlanSummary AnalyzePlan(
            ReadOnlySpan<BlueprintNodeSpec> nodes,
            Span<BlueprintNodeEvaluation> outEvals)
        {
            int n = nodes.Length;
            BlueprintPlanSummary summary = default;
            summary.TotalPieces = n;
            if (n == 0 || outEvals.Length < n)
            {
                return summary;
            }

            // 1) Gecici gercek RigidStructurePiece + SoilEmbedProfile + CornerBond4Interface dizilerini kur
            var simPieces = new RigidStructurePiece[n];
            var soilList = new SoilEmbedProfile[n];
            int soilCount = 0;

            bool hasRidgeOrTie = false;
            int roofCount = 0;

            for (int i = 0; i < n; i++)
            {
                ref readonly BlueprintNodeSpec nd = ref nodes[i];
                if (nd.IsBuilt) summary.BuiltPieces++;
                if (nd.Kind == BlueprintPieceKind.RidgeOrTieBeam) hasRidgeOrTie = true;
                if (nd.Kind == BlueprintPieceKind.RoofSlope) roofCount++;

                RigidTimberPieceKind rKind = MapToRigidKind(nd.Kind);
                float diam = MathF.Max(0.16f, nd.DiameterM);
                float len = MathF.Max(0.50f, nd.LengthM);
                Vector3 he = nd.HalfExtents.LengthSquared() > 1e-4f
                    ? nd.HalfExtents
                    : new Vector3(diam * 0.5f, len * 0.5f, diam * 0.5f);

                WoodSpeciesSpec spec = WoodSpeciesPhysicsLut.Get(nd.Species);
                float massKg = rKind is RigidTimberPieceKind.Plank or RigidTimberPieceKind.Rafter
                    ? MathF.Max(14f, (he.X * 2f) * (he.Y * 2f) * (he.Z * 2f) * spec.DryDensityKgPerM3 * 0.65f)
                    : WoodSpeciesPhysicsLut.ComputeCylinderMassKg(nd.Species, len, diam);

                bool validGround =
                    (nd.Kind == BlueprintPieceKind.Post && nd.IsGrounded && nd.PlannedEmbedDepthM >= MinSafePostEmbedDepthM) ||
                    (nd.Kind == BlueprintPieceKind.FloorDeck && nd.IsGrounded);
                if (nd.Kind == BlueprintPieceKind.Post && validGround) summary.GroundedPosts++;

                simPieces[i] = new RigidStructurePiece
                {
                    PieceId = nd.Id != 0 ? nd.Id : (uint)(i + 1),
                    IslandId = 1,
                    Kind = rKind,
                    Species = nd.Species,
                    State = RigidTimberState.StaticAnchored,
                    Position = nd.Position,
                    Rotation = nd.Rotation,
                    LengthM = len,
                    DiameterM = diam,
                    HalfExtents = he,
                    MassKg = massKg,
                    Flags = validGround ? (byte)2 : (byte)0
                };

                if (nd.Kind == BlueprintPieceKind.Post && nd.IsGrounded)
                {
                    float emb = nd.PlannedEmbedDepthM;
                    float resistM = emb >= MinSafePostEmbedDepthM ? MathF.Max(6500f, 32000f * emb * emb) : 120f;
                    float stiffK = emb >= MinSafePostEmbedDepthM ? MathF.Max(18000f, 95000f * emb * emb) : 400f;
                    soilList[soilCount++] = new SoilEmbedProfile
                    {
                        PieceId = simPieces[i].PieceId,
                        EmbedDepthM = emb,
                        DirDepthPosX = emb,
                        DirDepthNegX = emb,
                        DirDepthPosZ = emb,
                        DirDepthNegZ = emb,
                        DirSoilMatPosX = 2,
                        DirSoilMatNegX = 2,
                        DirSoilMatPosZ = 2,
                        DirSoilMatNegZ = 2,
                        CompactionFactor = 0.85f,
                        HasStoneCollar = 1,
                        PivotDepthZr = 0.70f * emb,
                        ResistMomentPosX = resistM,
                        ResistMomentNegX = resistM,
                        ResistMomentPosZ = resistM,
                        ResistMomentNegZ = resistM,
                        RotStiffnessPosX = stiffK,
                        RotStiffnessNegX = stiffK,
                        RotStiffnessPosZ = stiffK,
                        RotStiffnessNegZ = stiffK,
                        LastDecision = emb >= MinSafePostEmbedDepthM
                            ? SoilEmbedDecision.ElasticBalanced
                            : SoilEmbedDecision.GeotechnicalOverturn
                    };
                }
            }

            // 2) Blueprint parcalari arasindaki fiziksel temas/baglantilari otomatik cikar
            int maxBonds = Math.Max(64, n * 24);
            var bondBuf = new CornerBond4Interface[maxBonds];
            int bondCount = 0;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (bondCount >= maxBonds) break;
                    if (!AreBlueprintPiecesConnected(in nodes[i], in nodes[j], out Vector3 contactPt, out TimberJoineryKind joinery))
                        continue;

                    float contactW = MathF.Min(simPieces[i].DiameterM, simPieces[j].DiameterM);
                    bondBuf[bondCount++] = new CornerBond4Interface
                    {
                        BondId = (uint)(bondCount + 1),
                        PieceA = simPieces[i].PieceId,
                        PieceB = simPieces[j].PieceId,
                        Joinery = joinery,
                        ContactAreaM2 = contactW * contactW,
                        LocalA0 = contactPt - simPieces[i].Position,
                        LocalA2 = contactPt - simPieces[i].Position,
                        LocalB0 = contactPt - simPieces[j].Position,
                        LocalB2 = contactPt - simPieces[j].Position,
                        CornerBrokenMask = 0,
                        IsActive = 1
                    };
                }
            }

            // 3) Ground-Anchor BFS (zeminden kac adimda destek aliyor?)
            int[] hopDist = new int[n];
            int[] queue = new int[n];
            for (int i = 0; i < n; i++) hopDist[i] = -1;
            int qHead = 0, qTail = 0;

            for (int i = 0; i < n; i++)
            {
                if ((simPieces[i].Flags & 2) != 0)
                {
                    hopDist[i] = 0;
                    queue[qTail++] = i;
                }
            }

            while (qHead < qTail)
            {
                int cur = queue[qHead++];
                uint curId = simPieces[cur].PieceId;
                int nextHop = hopDist[cur] + 1;

                for (int b = 0; b < bondCount; b++)
                {
                    uint otherId = 0;
                    if (bondBuf[b].PieceA == curId) otherId = bondBuf[b].PieceB;
                    else if (bondBuf[b].PieceB == curId) otherId = bondBuf[b].PieceA;
                    else continue;

                    for (int k = 0; k < n; k++)
                    {
                        if (simPieces[k].PieceId == otherId && hopDist[k] < 0)
                        {
                            hopDist[k] = nextHop;
                            queue[qTail++] = k;
                            break;
                        }
                    }
                }
            }

            // 4) Gercek TimberStaticEquilibriumSolver ile tum plan adasini simule et!
            var soilSpan = soilList.AsSpan(0, soilCount);
            var bondSpan = bondBuf.AsSpan(0, bondCount);
            _solver.EvaluateDirtyIsland(1, simPieces, soilSpan, bondSpan, Vector3.Zero, 1L, traceSink: null);

            // 5) Her parca icin Valheim + Muhendislik Gerilme Haritasi (Mavi/Yesil/Sari/Kirmizi) olustur
            long stabilitySum = 0;
            for (int i = 0; i < n; i++)
            {
                ref readonly BlueprintNodeSpec nd = ref nodes[i];
                ref readonly RigidStructurePiece sp = ref simPieces[i];
                var ev = new BlueprintNodeEvaluation
                {
                    Tier = BlueprintStabilityTier.SafeGreen,
                    StabilityPct = 90,
                    StressRatio01 = 0.15f,
                    PredictedSagMm = 0f,
                    WillCollapse = false,
                    Reason = BlueprintFailureReason.None
                };

                // A) Kök direk çukur derinliği kontrolü
                if (nd.Kind == BlueprintPieceKind.Post && nd.IsGrounded && nd.PlannedEmbedDepthM < MinSafePostEmbedDepthM)
                {
                    ev.Tier = BlueprintStabilityTier.CriticalRed;
                    ev.StabilityPct = 15;
                    ev.StressRatio01 = 1.0f;
                    ev.WillCollapse = true;
                    ev.Reason = BlueprintFailureReason.ShallowSoilEmbed;
                }
                // B) Zemine bağlantı yolu yoksa kesin çöker!
                else if (hopDist[i] < 0)
                {
                    ev.Tier = BlueprintStabilityTier.CriticalRed;
                    ev.StabilityPct = 0;
                    ev.StressRatio01 = 1.0f;
                    ev.WillCollapse = true;
                    ev.Reason = BlueprintFailureReason.NoGroundPath;
                }
                else
                {
                    // Yatay elemanlarda desteksiz açıklık ve eğilme momenti kontrolü
                    if (nd.Kind is BlueprintPieceKind.WallLog or BlueprintPieceKind.FloorBeam or BlueprintPieceKind.FloorDeck or BlueprintPieceKind.RoofSlope)
                    {
                        float clearSpan = EstimateEffectiveSpanMeters(i, nodes);
                        double wDist = (sp.MassKg * WoodSpeciesPhysicsLut.GravityMps2) / Math.Max(0.5, clearSpan);
                        double sagM = TimberStaticEquilibriumSolver.ComputeBeamMidpointSagMeters(
                            nd.Species, clearSpan, sp.DiameterM, wDist, centerPointLoadN: 0.0);
                        double mMax = (wDist * clearSpan * clearSpan) / 8.0;
                        double panelFactor = nd.Kind is BlueprintPieceKind.FloorDeck or BlueprintPieceKind.RoofSlope ? 2.2 : 1.0;
                        double mYield = Math.Max(1.0, WoodSpeciesPhysicsLut.ComputeYieldMomentNm(nd.Species, sp.DiameterM) * panelFactor);
                        float stress = (float)Math.Clamp(mMax / mYield, 0.0, 1.5);

                        ev.PredictedSagMm = (float)(sagM * 1000.0);
                        ev.StressRatio01 = Math.Clamp(stress, 0f, 1f);

                        if (clearSpan > MaxSafeUnsupportedSpanM)
                        {
                            ev.Tier = BlueprintStabilityTier.CriticalRed;
                            ev.StabilityPct = 10;
                            ev.WillCollapse = true;
                            ev.Reason = BlueprintFailureReason.UnsupportedSpanTooLong;
                        }
                        else if (stress >= 1.0f)
                        {
                            ev.Tier = BlueprintStabilityTier.CriticalRed;
                            ev.StabilityPct = 12;
                            ev.WillCollapse = true;
                            ev.Reason = BlueprintFailureReason.HighBendingStress;
                        }
                        else if (clearSpan > CautionUnsupportedSpanM || stress >= 0.70f)
                        {
                            ev.Tier = BlueprintStabilityTier.CautionYellow;
                            ev.StabilityPct = Math.Clamp((int)MathF.Round((1f - MathF.Max(stress, (clearSpan - 2.6f) / 2.2f)) * 100f), 38, 68);
                            ev.Reason = BlueprintFailureReason.UnsupportedSpanTooLong;
                        }
                        else
                        {
                            int hopPenalty = Math.Min(30, hopDist[i] * 6);
                            ev.Tier = BlueprintStabilityTier.SafeGreen;
                            ev.StabilityPct = Math.Clamp(96 - hopPenalty - (int)(stress * 25f), 72, 98);
                        }

                        // Çatı dışa itme kontrolü (mahya/gergi kirişi yoksa uyar)
                        if (!ev.WillCollapse && nd.Kind == BlueprintPieceKind.RoofSlope && roofCount >= 2 && !hasRidgeOrTie)
                        {
                            if (ev.Tier == BlueprintStabilityTier.SafeGreen)
                            {
                                ev.Tier = BlueprintStabilityTier.CautionYellow;
                                ev.StabilityPct = Math.Min(ev.StabilityPct, 58);
                                ev.Reason = BlueprintFailureReason.MissingRoofTieBeam;
                            }
                        }
                    }
                    else if (nd.Kind == BlueprintPieceKind.Post && hopDist[i] == 0)
                    {
                        ev.Tier = BlueprintStabilityTier.GroundedBlue;
                        ev.StabilityPct = 100;
                        ev.StressRatio01 = 0.08f;
                    }
                    else
                    {
                        int hopPenalty = Math.Min(28, hopDist[i] * 6);
                        ev.Tier = hopDist[i] <= 3 ? BlueprintStabilityTier.SafeGreen : BlueprintStabilityTier.CautionYellow;
                        ev.StabilityPct = Math.Clamp(96 - hopPenalty, 55, 96);
                    }

                    // Gerçek statik çözücü bu parçayı uyandırdıysa (yıkıldıysa) kesin Kırmızı!
                    if (sp.State != RigidTimberState.StaticAnchored)
                    {
                        ev.Tier = BlueprintStabilityTier.CriticalRed;
                        ev.StabilityPct = 0;
                        ev.StressRatio01 = 1.0f;
                        ev.WillCollapse = true;
                        if (ev.Reason == BlueprintFailureReason.None)
                        {
                            ev.Reason = nd.Kind == BlueprintPieceKind.Post
                                ? BlueprintFailureReason.PostOverloaded
                                : BlueprintFailureReason.CantileverOverturn;
                        }
                    }
                }

                if (nd.IsBuilt && !ev.WillCollapse)
                {
                    ev.Tier = BlueprintStabilityTier.BuiltSolid;
                }

                outEvals[i] = ev;
                stabilitySum += ev.StabilityPct;

                if (ev.WillCollapse)
                {
                    summary.CollapseCount++;
                    if (summary.PrimaryFailure == BlueprintFailureReason.None)
                        summary.PrimaryFailure = ev.Reason;
                }
                else if (ev.Tier == BlueprintStabilityTier.CautionYellow)
                {
                    summary.WarningCount++;
                    if (summary.PrimaryFailure == BlueprintFailureReason.None)
                        summary.PrimaryFailure = ev.Reason;
                }
                else
                {
                    summary.SafeCount++;
                }
            }

            summary.WillStand = n > 0 && summary.CollapseCount == 0 && summary.GroundedPosts > 0;
            int avgPct = (int)(stabilitySum / n);
            summary.OverallStabilityPct = summary.WillStand ? Math.Clamp(avgPct, 35, 100) : Math.Clamp(avgPct, 0, 30);
            return summary;
        }

        public static string GetReasonTextTurkish(BlueprintFailureReason reason) => reason switch
        {
            BlueprintFailureReason.NoGroundPath => "Zemine gömülü taşıyıcı direğe bağlı değil — altına direk ekle!",
            BlueprintFailureReason.UnsupportedSpanTooLong => "Desteksiz açıklık çok geniş (>4.2m) — ortaya destek direği ekle!",
            BlueprintFailureReason.HighBendingStress => "Kiriş eğilme sınırını aşıyor — orta direk veya daha kısa açıklık kullan!",
            BlueprintFailureReason.MissingRoofTieBeam => "Çatı dışa itiyor — tepeye Mahya / Gergi Kirişi ekle!",
            BlueprintFailureReason.CantileverOverturn => "Dengesiz tek taraflı çıkma (konsol) — karşı köşeye direk ekle!",
            BlueprintFailureReason.ShallowSoilEmbed => "Direk çukuru sığ (<0.45m) — kürekle en az 0.5m derin kaz!",
            BlueprintFailureReason.PostOverloaded => "Taşıyıcı direk aşırı yüklü — yanına ek direk koy!",
            _ => "Yapı dengeli ve ayakta kalır."
        };

        private static RigidTimberPieceKind MapToRigidKind(BlueprintPieceKind kind) => kind switch
        {
            BlueprintPieceKind.Post => RigidTimberPieceKind.Post,
            BlueprintPieceKind.WallLog => RigidTimberPieceKind.Log,
            BlueprintPieceKind.FloorBeam => RigidTimberPieceKind.Beam,
            BlueprintPieceKind.FloorDeck => RigidTimberPieceKind.Plank,
            BlueprintPieceKind.RoofSlope => RigidTimberPieceKind.Rafter,
            BlueprintPieceKind.RidgeOrTieBeam => RigidTimberPieceKind.RidgeBeam,
            _ => RigidTimberPieceKind.Log
        };

        private static float EstimateEffectiveSpanMeters(int idx, ReadOnlySpan<BlueprintNodeSpec> nodes)
        {
            ref readonly BlueprintNodeSpec cur = ref nodes[idx];
            float len = MathF.Max(0.6f, cur.LengthM);
            Vector3 axis = Vector3.Transform(Vector3.UnitY, cur.Rotation);
            if (cur.Kind is BlueprintPieceKind.FloorDeck or BlueprintPieceKind.RoofSlope)
            {
                // Tabliye ve cati panelleri kisa kenar boyunca desteklenir (2-yonlu destek kurali)
                float spanX = cur.HalfExtents.X * 2f;
                float spanZ = cur.HalfExtents.Z * 2f;
                if (spanX <= spanZ)
                {
                    axis = Vector3.Transform(Vector3.UnitX, cur.Rotation);
                    len = spanX;
                }
                else
                {
                    axis = Vector3.Transform(Vector3.UnitZ, cur.Rotation);
                    len = spanZ;
                }
                if (cur.Kind == BlueprintPieceKind.FloorDeck)
                {
                    // 4 taraftan duvar/kiris ile cevrilen doseme efektif acikligi 0.75 katsayisiyla paylasir
                    len *= 0.75f;
                }
            }
            if (axis.LengthSquared() < 1e-4f) axis = Vector3.UnitX;
            axis = Vector3.Normalize(axis);

            // Eger elemanin orta bolgesinin altinda ara direk varsa efektif aciklik yariya iner!
            float halfL = len * 0.5f;
            float bestMidSupportDist = float.MaxValue;
            for (int j = 0; j < nodes.Length; j++)
            {
                if (j == idx || nodes[j].Kind != BlueprintPieceKind.Post) continue;
                Vector3 d = nodes[j].Position - cur.Position;
                float proj = Vector3.Dot(d, axis);
                if (MathF.Abs(proj) >= halfL - 0.55f) continue; // Uç direk değil, orta destek arıyoruz
                Vector3 perp = d - axis * proj;
                float perpXZ = MathF.Sqrt(perp.X * perp.X + perp.Z * perp.Z);
                if (perpXZ <= 0.70f && nodes[j].Position.Y + nodes[j].LengthM * 0.5f >= cur.Position.Y - 0.60f)
                {
                    float splitSpan = MathF.Max(halfL + proj, halfL - proj);
                    if (splitSpan < bestMidSupportDist) bestMidSupportDist = splitSpan;
                }
            }

            return bestMidSupportDist < len ? bestMidSupportDist : len;
        }

        private static bool AreBlueprintPiecesConnected(
            in BlueprintNodeSpec a,
            in BlueprintNodeSpec b,
            out Vector3 contactPt,
            out TimberJoineryKind joinery)
        {
            contactPt = (a.Position + b.Position) * 0.5f;
            joinery = TimberJoineryKind.GroovedChannel;

            // İki parçanın yönelimli yarı-eksenlerinden yaklaşım mesafesi (basit ama güvenilir OBB/segment yaklaşımı)
            Vector3 pa = ClosestPointOnPieceSegment(in a, b.Position);
            Vector3 pb = ClosestPointOnPieceSegment(in b, pa);
            pa = ClosestPointOnPieceSegment(in a, pb);

            float dist = Vector3.Distance(pa, pb);
            float radA = a.Kind is BlueprintPieceKind.FloorDeck or BlueprintPieceKind.RoofSlope
                ? MathF.Max(a.HalfExtents.X, a.HalfExtents.Z) * 0.35f + 0.25f
                : MathF.Max(0.22f, a.DiameterM * 0.55f);
            float radB = b.Kind is BlueprintPieceKind.FloorDeck or BlueprintPieceKind.RoofSlope
                ? MathF.Max(b.HalfExtents.X, b.HalfExtents.Z) * 0.35f + 0.25f
                : MathF.Max(0.22f, b.DiameterM * 0.55f);

            float tol = radA + radB + 0.28f;
            if (dist > tol) return false;

            contactPt = (pa + pb) * 0.5f;
            joinery = (a.Kind == BlueprintPieceKind.Post || b.Kind == BlueprintPieceKind.Post)
                ? TimberJoineryKind.GroovedChannel
                : TimberJoineryKind.Pegged;
            return true;
        }

        private static Vector3 ClosestPointOnPieceSegment(in BlueprintNodeSpec nd, Vector3 target)
        {
            if (nd.Kind is BlueprintPieceKind.FloorDeck or BlueprintPieceKind.RoofSlope)
            {
                Quaternion inv = Quaternion.Inverse(nd.Rotation);
                Vector3 loc = Vector3.Transform(target - nd.Position, inv);
                Vector3 clamped = new Vector3(
                    Math.Clamp(loc.X, -nd.HalfExtents.X, nd.HalfExtents.X),
                    Math.Clamp(loc.Y, -nd.HalfExtents.Y, nd.HalfExtents.Y),
                    Math.Clamp(loc.Z, -nd.HalfExtents.Z, nd.HalfExtents.Z));
                return nd.Position + Vector3.Transform(clamped, nd.Rotation);
            }

            Vector3 axis = Vector3.Transform(Vector3.UnitY, nd.Rotation);
            if (axis.LengthSquared() < 1e-4f) axis = Vector3.UnitY;
            axis = Vector3.Normalize(axis);
            float halfL = MathF.Max(0.25f, nd.LengthM * 0.5f);
            float t = Math.Clamp(Vector3.Dot(target - nd.Position, axis), -halfL, halfL);
            return nd.Position + axis * t;
        }
    }
}
