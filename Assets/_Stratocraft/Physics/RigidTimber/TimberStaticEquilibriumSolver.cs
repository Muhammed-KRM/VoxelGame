#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// KM-5 / DOK4 Bolum 2, 3 &amp; 4: Olay-Tabanli Statik Denge, Kiris Sehimi ve Cati Disa Itmesi Cozucusu (0 B GC).
    /// - Uyuyan yapi: 0.00 ms/kare.
    /// - Degisimde: Ground-Anchor BFS -> double yuk/moment/sehim + Cati Outward Thrust -> Broms/Winkler sutun testi -> Secici XPBD uyanisi.
    /// </summary>
    public sealed class TimberStaticEquilibriumSolver : IRigidTimberIslandSolver
    {
        public const float DefaultRoofSlopeRad = 0.785398f; // 45 derece
        public const float OverturnKickAngularSpeedRadPerSec = 0.85f;

        [ThreadStatic] private static int[]? _tlsQueue;
        [ThreadStatic] private static byte[]? _tlsVisited;
        [ThreadStatic] private static double[]? _tlsAccumLoadKg;
        [ThreadStatic] private static Vector3[]? _tlsAccumLateralN;

        public static double ComputeBeamMidpointSagMeters(
            TimberWoodSpecies species,
            float spanLengthM,
            float diameterM,
            double distributedLoadNPerM,
            double centerPointLoadN)
        {
            WoodSpeciesSpec spec = WoodSpeciesPhysicsLut.Get(species);
            double E = spec.ElasticModulusPa;
            double I = WoodSpeciesPhysicsLut.ComputeSecondMomentOfArea(diameterM);
            double ei = Math.Max(1.0, E * I);
            double L = Math.Max(0.25, spanLengthM);
            double L3 = L * L * L;
            double L4 = L3 * L;
            return (5.0 * distributedLoadNPerM * L4) / (384.0 * ei) + (centerPointLoadN * L3) / (48.0 * ei);
        }

        public static double ComputeRoofOutwardThrustN(double roofWeightN, float roofPitchRad = DefaultRoofSlopeRad)
        {
            double tanTheta = Math.Max(0.25, Math.Tan(Math.Clamp(roofPitchRad, 0.25f, 1.30f)));
            return roofWeightN / (2.0 * tanTheta);
        }

        public int EvaluateDirtyIsland(
            int islandId,
            Span<RigidStructurePiece> pieces,
            Span<SoilEmbedProfile> soilProfiles,
            Span<CornerBond4Interface> bonds,
            Vector3 externalWindAccel,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink = null)
        {
            int n = pieces.Length;
            if (n == 0) return 0;

            EnsureScratchBuffers(n);
            int[] queue = _tlsQueue!;
            byte[] visited = _tlsVisited!;
            double[] accumLoadKg = _tlsAccumLoadKg!;
            Vector3[] accumLatN = _tlsAccumLateralN!;

            int totalWoken = 0;
            Span<Vector2> postXZ = stackalloc Vector2[8];

            // Kaskad dongusu (maks 4 tur: bir sutun/kiris kirilinca ustundeki parcalar da ayni olayda uyanir!)
            for (int cascade = 0; cascade < 4; cascade++)
            {
                Array.Clear(visited, 0, n);
                Array.Clear(accumLoadKg, 0, n);
                Array.Clear(accumLatN, 0, n);

                // 1) Adada RidgeBeam veya TieBeam (gergi kirisi) var mi? (Cati disa itme kontrolu!)
                bool hasRoofTieOrRidge = false;
                double totalRoofMassKg = 0.0;
                int topWallSupportCount = 0;

                for (int i = 0; i < n; i++)
                {
                    ref readonly RigidStructurePiece p = ref pieces[i];
                    if (p.IslandId != islandId || p.State != RigidTimberState.StaticAnchored) continue;
                    if (p.Kind == RigidTimberPieceKind.RidgeBeam || p.Kind == RigidTimberPieceKind.TieBeam)
                    {
                        hasRoofTieOrRidge = true;
                    }
                    else if (p.Kind == RigidTimberPieceKind.Rafter)
                    {
                        totalRoofMassKg += p.MassKg;
                    }
                    else if (p.Kind == RigidTimberPieceKind.Post)
                    {
                        topWallSupportCount++;
                    }
                }

                // 2) Ground-Anchor BFS: Saglam gomulu kok sutunlardan basla
                int qHead = 0, qTail = 0;
                for (int i = 0; i < n; i++)
                {
                    ref readonly RigidStructurePiece p = ref pieces[i];
                    if (p.IslandId != islandId || p.State != RigidTimberState.StaticAnchored) continue;

                    bool isGroundAnchored = (p.Flags & 2) != 0;
                    int soilIdx = FindSoilProfileIndex(soilProfiles, p.PieceId);
                    if (soilIdx >= 0)
                    {
                        SoilEmbedDecision dec = soilProfiles[soilIdx].LastDecision;
                        isGroundAnchored = soilProfiles[soilIdx].EmbedDepthM > 0.08f &&
                                           dec != SoilEmbedDecision.GeotechnicalOverturn &&
                                           dec != SoilEmbedDecision.WoodShaftFracture;
                    }

                    if (isGroundAnchored)
                    {
                        visited[i] = 1;
                        queue[qTail++] = i;
                    }
                }

                while (qHead < qTail)
                {
                    int curIdx = queue[qHead++];
                    uint curId = pieces[curIdx].PieceId;

                    for (int b = 0; b < bonds.Length; b++)
                    {
                        ref readonly CornerBond4Interface bond = ref bonds[b];
                        if (bond.IsActive == 0 || bond.IntactCornerCount() == 0) continue;

                        uint otherId = 0;
                        if (bond.PieceA == curId) otherId = bond.PieceB;
                        else if (bond.PieceB == curId) otherId = bond.PieceA;
                        else continue;

                        int otherIdx = FindPieceIndex(pieces, otherId);
                        if (otherIdx >= 0 &&
                            visited[otherIdx] == 0 &&
                            pieces[otherIdx].IslandId == islandId &&
                            pieces[otherIdx].State == RigidTimberState.StaticAnchored)
                        {
                            visited[otherIdx] = 1;
                            queue[qTail++] = otherIdx;
                        }
                    }
                }

                // 3) Zemine yolu kesilen parcalari aninda DynamicActive uyandir!
                int wokenThisRound = 0;
                for (int i = 0; i < n; i++)
                {
                    ref RigidStructurePiece p = ref pieces[i];
                    if (p.IslandId != islandId || p.State != RigidTimberState.StaticAnchored) continue;
                    if (visited[i] == 0)
                    {
                        WakePieceDynamic(ref p, Vector3.Zero, currentTick, traceSink);
                        wokenThisRound++;
                    }
                }

                // 3b) Gercek Destek Cokgeni & Konsol Devrilmesi:
                // - 2 saglam direk arasindaki oluklu duvar (perpDist ≈ 0) veya 3 direkli L-kose (CoM ucgen icinde) ASLA kendi kendine yikilmaz!
                // - Yalnizca 4 koseli yuksek bir binanin 1 kose sutunu kesildiginde ve ust yapi agirligi kalan destek dogrusundan/ucgeninden
                //   disariya (konsol / overhang > 1.35m) tasarsa kesilen koseye dogru devrilir!
                int visitedCount = 0;
                int anchoredPostCount = 0;
                float minY = float.MaxValue, maxY = float.MinValue;
                double totalMass = 0.0, comX = 0.0, comZ = 0.0;
                double superMass = 0.0, superComX = 0.0, superComZ = 0.0;

                for (int i = 0; i < n; i++)
                {
                    if (visited[i] == 0) continue;
                    ref readonly RigidStructurePiece p = ref pieces[i];
                    visitedCount++;
                    if (p.Position.Y < minY) minY = p.Position.Y;
                    if (p.Position.Y > maxY) maxY = p.Position.Y;
                    totalMass += p.MassKg;
                    comX += p.Position.X * p.MassKg;
                    comZ += p.Position.Z * p.MassKg;
                    if (p.Kind == RigidTimberPieceKind.Post)
                    {
                        if (anchoredPostCount < postXZ.Length)
                        {
                            postXZ[anchoredPostCount] = new Vector2(p.Position.X, p.Position.Z);
                        }
                        anchoredPostCount++;
                    }
                    else
                    {
                        superMass += p.MassKg;
                        superComX += p.Position.X * p.MassKg;
                        superComZ += p.Position.Z * p.MassKg;
                    }
                }

                if (visitedCount >= 10 && (maxY - minY) >= 4.5f && anchoredPostCount >= 1 && anchoredPostCount <= 3 && totalMass > 1.0)
                {
                    Vector2 com = superMass > 1.0
                        ? new Vector2((float)(superComX / superMass), (float)(superComZ / superMass))
                        : new Vector2((float)(comX / totalMass), (float)(comZ / totalMass));
                    Vector2 nearestSupport = postXZ[0];
                    Vector2 tipVec = Vector2.Zero;
                    bool shouldOverturn = false;

                    if (anchoredPostCount == 1)
                    {
                        nearestSupport = postXZ[0];
                        tipVec = com - nearestSupport;
                        shouldOverturn = tipVec.Length() >= 1.35f;
                    }
                    else if (anchoredPostCount == 2)
                    {
                        Vector2 a = postXZ[0], b = postXZ[1];
                        Vector2 ab = b - a;
                        float abLenSq = ab.LengthSquared();
                        float t = abLenSq > 1e-4f ? Math.Clamp(Vector2.Dot(com - a, ab) / abLenSq, 0f, 1f) : 0f;
                        nearestSupport = a + ab * t;
                        tipVec = com - nearestSupport;
                        // Iki direk arasindaki duz duvarin CoM'u dogru uzerindedir (mesafe ~0m -> yikilmaz);
                        // yalnizca yan tarafa desteksiz > 1.45m konsol tasarsa devrilir!
                        shouldOverturn = tipVec.Length() >= 1.45f;
                    }
                    else if (anchoredPostCount == 3)
                    {
                        Vector2 p0 = postXZ[0], p1 = postXZ[1], p2 = postXZ[2];
                        nearestSupport = ClosestPointOnTriangle2D(com, p0, p1, p2);
                        float outDist = Vector2.Distance(com, nearestSupport);
                        Vector2 postCentroid = (p0 + p1 + p2) / 3.0f;

                        // En uzun kenara (kesilen 4. kosenin karsisindaki diyagonal hipotenuse) gore ic guvenlik payini olc:
                        // - Gercek 3 direkli L-duvarda superCom hipotenusun > 0.85m ICINDEDIR -> ASLA yikilmaz!
                        // - 4 koseli 3 katli bir binanin 1 kose sutunu kesildiginde ust yapi CoM'u diyagonal sinirin ustune/disina (inwardMargin <= 0.25m) duser -> kesilen koseye devrilir!
                        Vector2 eA = p0, eB = p1, opp = p2;
                        float d01 = Vector2.DistanceSquared(p0, p1);
                        float d12 = Vector2.DistanceSquared(p1, p2);
                        float d20 = Vector2.DistanceSquared(p2, p0);
                        if (d12 >= d01 && d12 >= d20) { eA = p1; eB = p2; opp = p0; }
                        else if (d20 >= d01 && d20 >= d12) { eA = p2; eB = p0; opp = p1; }

                        Vector2 edgeDir = Vector2.Normalize(eB - eA);
                        Vector2 inwardNormal = new Vector2(-edgeDir.Y, edgeDir.X);
                        if (Vector2.Dot(opp - eA, inwardNormal) < 0f) inwardNormal = -inwardNormal;
                        float inwardMargin = Vector2.Dot(com - eA, inwardNormal);

                        if (outDist >= 0.35f)
                        {
                            shouldOverturn = true;
                            tipVec = com - nearestSupport;
                        }
                        else if (inwardMargin <= 0.25f)
                        {
                            shouldOverturn = true;
                            tipVec = com - postCentroid;
                            if (tipVec.LengthSquared() < 1e-4f) tipVec = -inwardNormal;
                        }
                    }

                    if (shouldOverturn && tipVec.LengthSquared() > 1e-4f)
                    {
                        Vector3 tipDir = Vector3.Normalize(new Vector3(tipVec.X, 0f, tipVec.Y));
                        for (int b = 0; b < bonds.Length; b++)
                        {
                            if (bonds[b].IsActive == 0) continue;
                            int ia = FindPieceIndex(pieces, bonds[b].PieceA);
                            int ib = FindPieceIndex(pieces, bonds[b].PieceB);
                            if ((ia >= 0 && pieces[ia].IslandId == islandId) || (ib >= 0 && pieces[ib].IslandId == islandId))
                            {
                                bonds[b].CornerBrokenMask = 0x0F;
                                bonds[b].IsActive = 0;
                            }
                        }

                        for (int i = 0; i < n; i++)
                        {
                            if (visited[i] == 0) continue;
                            ref RigidStructurePiece p = ref pieces[i];
                            if (p.State != RigidTimberState.StaticAnchored) continue;
                            float heightFactor = Math.Clamp((p.Position.Y - minY) / MathF.Max(1.0f, maxY - minY), 0.1f, 1.0f);
                            WakePieceDynamic(ref p, tipDir * (1.25f * heightFactor), currentTick, traceSink);
                            p.LinearVelocity += tipDir * (1.1f * heightFactor);
                            wokenThisRound++;
                        }
                    }
                }

                // 4) Bagli kalan parcalarda yuk birikimi + Kiris sehimi + Kertiksiz kayma + Cati Outward Thrust + Sutun Broms testi
                double roofThrustPerSupportN = (!hasRoofTieOrRidge && totalRoofMassKg > 1.0 && topWallSupportCount > 0)
                    ? ComputeRoofOutwardThrustN(totalRoofMassKg * WoodSpeciesPhysicsLut.GravityMps2) / topWallSupportCount
                    : 0.0;

                // Yukari -> Asagi basit yuk aktarimi (Y yuksekligine gore ustteki parcalarin yuku alt komsulara iner)
                for (int i = 0; i < n; i++)
                {
                    if (visited[i] == 0) continue;
                    ref readonly RigidStructurePiece p = ref pieces[i];
                    accumLoadKg[i] += p.MassKg;
                    accumLatN[i] += externalWindAccel * p.MassKg;
                }

                for (int b = 0; b < bonds.Length; b++)
                {
                    ref CornerBond4Interface bond = ref bonds[b];
                    if (bond.IsActive == 0) continue;
                    int idxA = FindPieceIndex(pieces, bond.PieceA);
                    int idxB = FindPieceIndex(pieces, bond.PieceB);
                    if (idxA < 0 || idxB < 0 || visited[idxA] == 0 || visited[idxB] == 0) continue;

                    int upper = pieces[idxA].Position.Y >= pieces[idxB].Position.Y ? idxA : idxB;
                    int lower = upper == idxA ? idxB : idxA;
                    accumLoadKg[lower] += accumLoadKg[upper] * 0.55;
                    accumLatN[lower] += accumLatN[upper] * 0.55f;

                    // Kertiksiz (FrictionOnly) baglantida yanal kuvvet > mu * N ise kayip dagilir!
                    if (bond.Joinery == TimberJoineryKind.FrictionOnly)
                    {
                        JoineryStrengthSpec jSpec = ReducedAemBondEvaluator.GetJoinerySpec(TimberJoineryKind.FrictionOnly);
                        float normalN = (float)(accumLoadKg[upper] * WoodSpeciesPhysicsLut.GravityMps2);
                        float lateralN = new Vector2(accumLatN[upper].X, accumLatN[upper].Z).Length();
                        if (lateralN > jSpec.EffectiveFrictionMu * normalN + 15f)
                        {
                            bond.CornerBrokenMask = 0x0F;
                            bond.IsActive = 0;
                            WakePieceDynamic(ref pieces[upper], new Vector3(accumLatN[upper].X, 0f, accumLatN[upper].Z), currentTick, traceSink);
                            wokenThisRound++;
                        }
                    }
                }

                // Kiris egilme/sehim & kirilma kontrolu + Gomulu sutun Broms/Winkler kontrolu
                for (int i = 0; i < n; i++)
                {
                    if (visited[i] == 0) continue;
                    ref RigidStructurePiece p = ref pieces[i];
                    if (p.State != RigidTimberState.StaticAnchored) continue;

                    if (p.Kind is RigidTimberPieceKind.Beam or RigidTimberPieceKind.Plank or RigidTimberPieceKind.Rafter)
                    {
                        double wDistNPerM = (p.MassKg * WoodSpeciesPhysicsLut.GravityMps2) / Math.Max(0.5, p.LengthM);
                        double extraCenterN = Math.Max(0.0, (accumLoadKg[i] - p.MassKg) * WoodSpeciesPhysicsLut.GravityMps2);
                        double mMax = (wDistNPerM * p.LengthM * p.LengthM) / 8.0 + (extraCenterN * p.LengthM) / 4.0;
                        // Genis doseme/cati panelleri (Plank/Rafter) tek bir ince cubuk degil, tam genislikli tabliyedir!
                        double panelWidthFactor = 1.0;
                        if (p.HalfExtents.X > p.DiameterM * 0.65f || p.HalfExtents.Z > p.DiameterM * 0.65f)
                        {
                            double panelW = Math.Max(p.HalfExtents.X * 2.0, p.HalfExtents.Z * 2.0);
                            panelWidthFactor = Math.Max(1.0, (panelW / Math.Max(0.15, p.DiameterM)) * 1.4);
                        }
                        double mYield = WoodSpeciesPhysicsLut.ComputeYieldMomentNm(p.Species, p.DiameterM) * panelWidthFactor;
                        double sag = ComputeBeamMidpointSagMeters(p.Species, p.LengthM, p.DiameterM, wDistNPerM, extraCenterN);

                        p.VisualSagMeters = (float)Math.Clamp(sag * 25.0, 0.0, 0.18); // Gozle gorulur elastik sehim
                        if (mMax >= mYield)
                        {
                            p.Damage01 = 1.0f;
                            BreakAllBondsForPiece(bonds, p.PieceId);
                            WakePieceDynamic(ref p, Vector3.Zero, currentTick, traceSink);
                            wokenThisRound++;
                        }
                    }
                    else if (p.Kind == RigidTimberPieceKind.Post)
                    {
                        int soilIdx = FindSoilProfileIndex(soilProfiles, p.PieceId);
                        if (soilIdx >= 0)
                        {
                            Vector3 lat = accumLatN[i];
                            if (roofThrustPerSupportN > 1.0)
                            {
                                // Cati disa itmesi: gergi kirisi yoksa sutunu disa iter!
                                lat.X += (float)roofThrustPerSupportN;
                            }

                            SoilEmbedDecision dec = SoilEmbedmentEvaluator.EvaluatePostStability(
                                ref p,
                                ref soilProfiles[soilIdx],
                                topLoadKg: (float)Math.Max(0.0, accumLoadKg[i] - p.MassKg),
                                lateralForceWorldN: lat,
                                lateralHeightAboveGroundM: MathF.Max(1.0f, p.LengthM - soilProfiles[soilIdx].EmbedDepthM),
                                topLoadEccentricityXZ: Vector2.Zero,
                                currentTick: currentTick,
                                traceSink: traceSink);

                            if (dec == SoilEmbedDecision.GeotechnicalOverturn || dec == SoilEmbedDecision.WoodShaftFracture)
                            {
                                Vector3 tiltDir = new Vector3(soilProfiles[soilIdx].OverturnDirX, 0f, soilProfiles[soilIdx].OverturnDirZ);
                                BreakAllBondsForPiece(bonds, p.PieceId);
                                WakePieceDynamic(ref p, tiltDir, currentTick, traceSink);
                                wokenThisRound++;
                            }
                        }
                    }
                }

                totalWoken += wokenThisRound;
                if (wokenThisRound == 0)
                {
                    break;
                }
            }

            if (traceSink != null && traceSink.IsEnabled)
            {
                var ev = new RigidPhysicsTraceEvent
                {
                    Tick = currentTick,
                    PieceId = 0,
                    IslandId = islandId,
                    EventKind = RigidTimberTraceEventKind.StaticSolved,
                    State = totalWoken > 0 ? RigidTimberState.DynamicActive : RigidTimberState.StaticAnchored,
                    DriveMomentNm = totalWoken
                };
                traceSink.Record(in ev, "TimberStaticEquilibriumSolver.EvaluateDirtyIsland");
            }

            return totalWoken;
        }

        private static void WakePieceDynamic(
            ref RigidStructurePiece p,
            Vector3 overturnDirXZ,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink)
        {
            p.State = RigidTimberState.DynamicActive;
            p.InvMass = 1.0f / MathF.Max(1.0f, p.MassKg);
            p.SleepTimerSec = 0f;

            if (overturnDirXZ.LengthSquared() > 1e-4f)
            {
                Vector3 u = Vector3.Normalize(new Vector3(overturnDirXZ.X, 0f, overturnDirXZ.Z));
                // u yonune devrilme ekseni: Y x u
                Vector3 rotAxis = Vector3.Cross(Vector3.UnitY, u);
                if (rotAxis.LengthSquared() > 1e-4f)
                {
                    p.AngularVelocity += Vector3.Normalize(rotAxis) * OverturnKickAngularSpeedRadPerSec;
                }
                p.LinearVelocity += u * 0.45f;
            }

            if (traceSink != null && traceSink.IsEnabled)
            {
                var ev = new RigidPhysicsTraceEvent
                {
                    Tick = currentTick,
                    PieceId = p.PieceId,
                    IslandId = p.IslandId,
                    EventKind = RigidTimberTraceEventKind.IslandWakeDynamic,
                    State = RigidTimberState.DynamicActive,
                    DirX = overturnDirXZ.X,
                    DirY = overturnDirXZ.Y,
                    DirZ = overturnDirXZ.Z
                };
                traceSink.Record(in ev, "TimberStaticEquilibriumSolver.WakePieceDynamic");
            }
        }

        private static void BreakAllBondsForPiece(Span<CornerBond4Interface> bonds, uint pieceId)
        {
            for (int i = 0; i < bonds.Length; i++)
            {
                if (bonds[i].PieceA == pieceId || bonds[i].PieceB == pieceId)
                {
                    bonds[i].CornerBrokenMask = 0x0F;
                    bonds[i].IsActive = 0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindPieceIndex(Span<RigidStructurePiece> pieces, uint pieceId)
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i].PieceId == pieceId) return i;
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindSoilProfileIndex(Span<SoilEmbedProfile> profiles, uint pieceId)
        {
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i].PieceId == pieceId) return i;
            }
            return -1;
        }

        private static Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-6f) return a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
            return a + ab * t;
        }

        private static Vector2 ClosestPointOnTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // Barycentric ic bolge kontrolu
            Vector2 v0 = c - a, v1 = b - a, v2 = p - a;
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);
            float denom = dot00 * dot11 - dot01 * dot01;
            if (MathF.Abs(denom) > 1e-6f)
            {
                float inv = 1f / denom;
                float u = (dot11 * dot02 - dot01 * dot12) * inv;
                float v = (dot00 * dot12 - dot01 * dot02) * inv;
                if (u >= -0.01f && v >= -0.01f && (u + v) <= 1.02f) return p;
            }
            Vector2 pAB = ClosestPointOnSegment2D(p, a, b);
            Vector2 pBC = ClosestPointOnSegment2D(p, b, c);
            Vector2 pCA = ClosestPointOnSegment2D(p, c, a);
            float dAB = Vector2.DistanceSquared(p, pAB);
            float dBC = Vector2.DistanceSquared(p, pBC);
            float dCA = Vector2.DistanceSquared(p, pCA);
            if (dAB <= dBC && dAB <= dCA) return pAB;
            return dBC <= dCA ? pBC : pCA;
        }

        private static void EnsureScratchBuffers(int n)
        {
            if (_tlsQueue == null || _tlsQueue.Length < n) _tlsQueue = new int[Math.Max(n, 256)];
            if (_tlsVisited == null || _tlsVisited.Length < n) _tlsVisited = new byte[Math.Max(n, 256)];
            if (_tlsAccumLoadKg == null || _tlsAccumLoadKg.Length < n) _tlsAccumLoadKg = new double[Math.Max(n, 256)];
            if (_tlsAccumLateralN == null || _tlsAccumLateralN.Length < n) _tlsAccumLateralN = new Vector3[Math.Max(n, 256)];
        }
    }
}
