#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// Kapı kanadı açılma mekaniği varyantı (DOK5 §3.1).
    /// </summary>
    public enum StructureDoorKind : byte
    {
        HingedSingle = 0,
        Sliding = 1,
        DoubleLeafGate = 2
    }

    /// <summary>
    /// Merdiven geometrisi varyantı (DOK5 §3.3).
    /// </summary>
    public enum StructureStairKind : byte
    {
        Straight = 0,
        LTurn = 1,
        SpiralStone = 2
    }

    /// <summary>
    /// Işın/mermi çarpışma maskesi (ok/mermi vs görüş hattı vs tüm katılar).
    /// </summary>
    public enum StructureRayMask : byte
    {
        Projectile = 0,
        LineOfSight = 1,
        SolidAll = 2
    }

    /// <summary>
    /// 0-GC yapı ışın çarpışma sonucu.
    /// </summary>
    public readonly struct StructureRayHit
    {
        public readonly bool Hit;
        public readonly float DistanceMeters;
        public readonly Vector3 Point;
        public readonly uint NodeId;
        public readonly StructuralElementRole Role;

        public StructureRayHit(bool hit, float distanceMeters, Vector3 point, uint nodeId, StructuralElementRole role)
        {
            Hit = hit;
            DistanceMeters = distanceMeters;
            Point = point;
            NodeId = nodeId;
            Role = role;
        }
    }

    /// <summary>
    /// PT-05 &amp; DOK4 §5.4: Saf C# (0-GC, Unity-bağımsız) 3B Modüler ve Birleşik Yapı Katı Çarpışma Çözücüsü.
    /// Mevcut 13 modüler parçanın (duvar, pencere, kapı sövesi+lentosu, subasman, döşeme, dikme, kiriş,
    /// çapraz payanda, alınlık, 26/45 deg eğimli çatı, mahya) tam yerel-uzay OBB matematiğini korurken;
    /// <see cref="StructuralNode64"/> düğümleri için:
    ///   - Menteşeli / sürgülü / çift kanatlı açılır-kapanır ve kilitlenebilir kapı OBB'si,
    ///   - Pencere OBB'si (oyuncu kapsülünü tutar, açıkken/cam-mazgalken ok ve görüş ışınını geçirir),
    ///   - Düz / L-dönüşlü / taş sarmal merdiven rampa &amp; basamak yürüme yüzeyi + &gt;= 1.95 m StairwellTrimmerOpening tavan muafiyeti,
    ///   - Yürünebilir dış cephe ModularScaffolding (platform + entegre iç merdiven + 0.95 m korkuluk),
    ///   - Ekol 4 kemer ve tonoz altı eğrisel geçit (ArchIntradosClearance) analitik çözümlerini sunar.
    /// </summary>
    public static class ModularStructureCollisionSolver
    {
        public readonly struct LocalSolidBox
        {
            public readonly Vector3 Min;
            public readonly Vector3 Max;
            public readonly bool IsWalkableTop;

            public LocalSolidBox(Vector3 min, Vector3 max, bool isWalkableTop = true)
            {
                Min = min;
                Max = max;
                IsWalkableTop = isWalkableTop;
            }
        }

        public const float StairwellMinClearHeadroomMeters = 1.95f;
        public const float StairwellOpeningHalfExtent = 0.86f;
        public const float ScaffoldingDeckTopY = 2.00f;
        public const float ScaffoldingGuardrailHeight = 0.95f;
        public const float WindowSillTopY = 0.95f;
        public const float WindowLintelBottomY = 1.80f;
        public const float WindowApertureHalfWidth = 0.44f;

        private const float BroadPhaseHorizRadiusSq = 25.0f; // 5.0m (geniş kemer/çift kapı kapsama)
        private const float BroadPhaseVertHalfSpan = 6.5f;
        private const float DefaultWallHalfThickness = 0.11f;
        private const float DoorwayClearHalfWidth = 0.46f;   // 0.92m açık kapı geçidi (oyuncu çapı ~0.68m)
        private const float DoorwayLintelBottomY = 1.92f;    // PlankFloor (+0.08m) üstünden 1.84m net geçiş (> 1.80m ayakta kapsül)
        private const float RoofSlabThickness = 0.22f;
        private const float TwoPi = MathF.PI * 2.0f;

        // =====================================================================
        // MEVCUT MODULAR BUILD PIECE API (100% GERIYE DONUK UYUMLU)
        // =====================================================================

        /// <summary>
        /// Verilen moduler parca turunun yerel-uzay kati carpisma kutularini doldurur (0 B GC).
        /// PlankDoorway2x2m icin 3 ayri kutu uretilir: sol sove, sag sove ve ust lento (orta kapi aciktir).
        /// </summary>
        public static int PopulateLocalSolidBoxes(ModularBuildPieceKind kind, Span<LocalSolidBox> outBoxes)
        {
            switch (kind)
            {
                case ModularBuildPieceKind.PlankWall2x2m:
                case ModularBuildPieceKind.PlankWindow2x2m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -DefaultWallHalfThickness),
                        new Vector3(1.00f, 2.00f, DefaultWallHalfThickness));
                    return 1;

                case ModularBuildPieceKind.PlankDoorway2x2m:
                    // Sol kapi sovesi
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -DefaultWallHalfThickness),
                        new Vector3(-DoorwayClearHalfWidth, 2.00f, DefaultWallHalfThickness));
                    // Sag kapi sovesi
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(DoorwayClearHalfWidth, 0.00f, -DefaultWallHalfThickness),
                        new Vector3(1.00f, 2.00f, DefaultWallHalfThickness));
                    // Ust kapi lentosu (ayakta 1.80m yuruyuse tam acik, ziplamada kafa engeller)
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(-DoorwayClearHalfWidth, DoorwayLintelBottomY, -DefaultWallHalfThickness),
                        new Vector3(DoorwayClearHalfWidth, 2.04f, DefaultWallHalfThickness),
                        isWalkableTop: true);
                    return 3;

                case ModularBuildPieceKind.StoneFoundation2x2m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, -0.65f, -1.00f),
                        new Vector3(1.00f, 0.32f, 1.00f));
                    return 1;

                case ModularBuildPieceKind.PlankFloor2x2m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, -0.02f, -1.00f),
                        new Vector3(1.00f, 0.08f, 1.00f));
                    return 1;

                case ModularBuildPieceKind.LogPillar3m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.16f, 0.00f, -0.16f),
                        new Vector3(0.16f, 3.00f, 0.16f));
                    return 1;

                case ModularBuildPieceKind.StoneArchPillar3m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.26f, 0.00f, -0.22f),
                        new Vector3(0.26f, 3.00f, 0.22f));
                    return 1;

                case ModularBuildPieceKind.LogBeam2m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.04f, 0.00f, -0.14f),
                        new Vector3(1.04f, 0.26f, 0.14f));
                    return 1;

                case ModularBuildPieceKind.RoofRidge2m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.02f, -0.02f, -0.34f),
                        new Vector3(1.02f, 0.28f, 0.34f));
                    return 1;

                case ModularBuildPieceKind.DiagonalBrace2m:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.76f, 0.00f, -0.10f),
                        new Vector3(-0.22f, 0.52f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-0.28f, 0.46f, -0.10f),
                        new Vector3(0.24f, 0.96f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(0.20f, 0.92f, -0.10f),
                        new Vector3(0.76f, 1.44f, 0.10f),
                        isWalkableTop: false);
                    return 3;

                default:
                    return 0;
            }
        }

        private static bool IsSlopedRoof(ModularBuildPieceKind kind, out float riseHeight)
        {
            if (kind == ModularBuildPieceKind.RoofRafter26Deg2x2m)
            {
                riseHeight = 1.0f;
                return true;
            }
            if (kind == ModularBuildPieceKind.RoofRafter45Deg2x2m)
            {
                riseHeight = 2.0f;
                return true;
            }
            riseHeight = 0.0f;
            return false;
        }

        private static bool TryEvaluateLocalColumnSpan(
            ModularBuildPieceKind kind,
            float localX,
            float localZ,
            float radius,
            out float spanMinY,
            out float spanMaxY)
        {
            if (IsSlopedRoof(kind, out float rise))
            {
                if (localX < -1.00f - radius || localX > 1.00f + radius ||
                    localZ < -1.05f - radius || localZ > 1.02f + radius)
                {
                    spanMinY = 0f;
                    spanMaxY = 0f;
                    return false;
                }

                float clampedZ = Math.Clamp(localZ, -1.00f, 1.00f);
                float t = (clampedZ + 1.0f) * 0.5f;
                float deckY = t * rise;
                spanMinY = deckY - 0.04f;
                spanMaxY = deckY + RoofSlabThickness;
                return true;
            }

            if (kind == ModularBuildPieceKind.GableWall45Deg2m)
            {
                if (localX < -1.00f - radius || localX > 1.00f + radius ||
                    localZ < -DefaultWallHalfThickness - radius || localZ > DefaultWallHalfThickness + radius)
                {
                    spanMinY = 0f;
                    spanMaxY = 0f;
                    return false;
                }

                float clampedX = Math.Clamp(localX, -1.00f, 1.00f);
                float topY = Math.Clamp(1.05f + clampedX, 0.08f, 2.05f);
                spanMinY = 0.00f;
                spanMaxY = topY;
                return true;
            }

            spanMinY = 0f;
            spanMaxY = 0f;
            return false;
        }

        private static bool IsWithinBroadPhase(in ModularPlacedPieceNode piece, float px, float py, float pz)
        {
            if (piece.IsBroken || piece.IsFalling) return false;
            float dx = px - piece.Position.X;
            float dz = pz - piece.Position.Z;
            if ((dx * dx + dz * dz) > BroadPhaseHorizRadiusSq) return false;
            float dy = py - piece.Position.Y;
            return MathF.Abs(dy) <= BroadPhaseVertHalfSpan;
        }

        /// <summary>
        /// Yatay adimda oyuncu kapsulunun herhangi bir kati moduler duvar, pencere, kapi sovesi, dikme,
        /// kiris, cati veya yuksek temel ile carpisip carpismadigini denetler (anti-tunneling alt-adimli, 0 B GC).
        /// </summary>
        public static bool IsHorizontalCapsuleStepBlocked(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            Vector3 curFeet,
            Vector3 nextFeet,
            float capsuleHeightMeters = 1.80f,
            float capsuleRadiusMeters = 0.34f,
            float maxStepUpMeters = 1.08f)
        {
            if (pieces.Length == 0) return false;

            float dx = nextFeet.X - curFeet.X;
            float dz = nextFeet.Z - curFeet.Z;
            float stepDistSq = dx * dx + dz * dz;
            if (stepDistSq < 1e-8f) return false;

            float r = Math.Clamp(capsuleRadiusMeters, 0.20f, 0.36f);
            float h = Math.Clamp(capsuleHeightMeters, 0.45f, 1.90f);

            int subSteps = stepDistSq > (0.14f * 0.14f)
                ? Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(stepDistSq) / 0.14f), 2, 16)
                : 1;
            for (int s = 1; s <= subSteps; s++)
            {
                float frac = s / (float)subSteps;
                Vector3 probeFeet = new Vector3(
                    curFeet.X + dx * frac,
                    curFeet.Y,
                    curFeet.Z + dz * frac);

                if (IsCapsuleBlockedAtPosition(pieces, curFeet, probeFeet, h, r, maxStepUpMeters))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCapsuleBlockedAtPosition(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            Vector3 curFeet,
            Vector3 probeFeet,
            float capsuleHeight,
            float radius,
            float maxStepUpMeters)
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                if (IsModularPieceBlockingCapsule(in piece, curFeet, probeFeet, capsuleHeight, radius, maxStepUpMeters))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsModularPieceBlockingCapsule(
            in ModularPlacedPieceNode piece,
            Vector3 curFeet,
            Vector3 probeFeet,
            float capsuleHeight,
            float radius,
            float maxStepUpMeters)
        {
            if (!IsWithinBroadPhase(in piece, probeFeet.X, probeFeet.Y, probeFeet.Z)) return false;

            float bodyMinY = probeFeet.Y + 0.08f;
            float bodyMaxY = probeFeet.Y + MathF.Max(0.42f, capsuleHeight - 0.08f);
            float stepLimitY = curFeet.Y + maxStepUpMeters;
            float rSq = radius * radius;
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            Quaternion invRot = Quaternion.Inverse(piece.Rotation);
            Vector3 localProbe = Vector3.Transform(probeFeet - piece.Position, invRot);
            float localBodyMinY = bodyMinY - piece.Position.Y;
            float localBodyMaxY = bodyMaxY - piece.Position.Y;
            float localStepLimitY = stepLimitY - piece.Position.Y;

            if (IsSlopedRoof(piece.Kind, out _) || piece.Kind == ModularBuildPieceKind.GableWall45Deg2m)
            {
                if (TryEvaluateLocalColumnSpan(piece.Kind, localProbe.X, localProbe.Z, radius, out float sMinY, out float sMaxY))
                {
                    if (localBodyMaxY > sMinY && localBodyMinY < sMaxY)
                    {
                        bool walkableStep = IsSlopedRoof(piece.Kind, out _) && sMaxY <= localStepLimitY;
                        if (!walkableStep)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            int boxCount = PopulateLocalSolidBoxes(piece.Kind, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (localBodyMaxY <= box.Min.Y || localBodyMinY >= box.Max.Y)
                {
                    continue;
                }

                float closestX = Math.Clamp(localProbe.X, box.Min.X, box.Max.X);
                float closestZ = Math.Clamp(localProbe.Z, box.Min.Z, box.Max.Z);
                float ox = localProbe.X - closestX;
                float oz = localProbe.Z - closestZ;
                if ((ox * ox + oz * oz) >= rSq)
                {
                    continue;
                }

                if (box.IsWalkableTop && box.Max.Y <= localStepLimitY)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Oyuncu kapsulu herhangi bir kati moduler duvar/dikme/sove icine girmisse (orn. lunge, dodge, parca yerlestirme)
        /// en kisa yatay MTV ekseninde disari iter (ASLA Y isinlamasi yapmaz, 0 B GC).
        /// </summary>
        public static bool ResolveHorizontalCapsuleDepenetration(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeightMeters = 1.80f,
            float capsuleRadiusMeters = 0.34f,
            float maxStepUpMeters = 1.08f)
        {
            if (pieces.Length == 0) return false;

            float r = Math.Clamp(capsuleRadiusMeters, 0.20f, 0.36f);
            float h = Math.Clamp(capsuleHeightMeters, 0.45f, 1.90f);
            bool movedAny = false;

            for (int iter = 0; iter < 3; iter++)
            {
                bool movedInPass = false;

                for (int i = 0; i < pieces.Length; i++)
                {
                    ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                    if (ResolveModularPieceDepenetration(in piece, ref posX, posY, ref posZ, h, r, maxStepUpMeters))
                    {
                        movedInPass = true;
                        movedAny = true;
                    }
                }

                if (!movedInPass) break;
            }

            return movedAny;
        }

        internal static bool ResolveModularPieceDepenetration(
            in ModularPlacedPieceNode piece,
            ref float posX,
            float posY,
            ref float posZ,
            float h,
            float r,
            float maxStepUpMeters)
        {
            if (!IsWithinBroadPhase(in piece, posX, posY, posZ)) return false;

            float rSq = r * r;
            float bodyMinY = posY + 0.10f;
            float bodyMaxY = posY + MathF.Max(0.42f, h - 0.08f);
            float stepLimitY = posY + maxStepUpMeters;
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            Quaternion invRot = Quaternion.Inverse(piece.Rotation);
            Vector3 worldFeet = new Vector3(posX, posY, posZ);
            Vector3 localPos = Vector3.Transform(worldFeet - piece.Position, invRot);
            float localBodyMinY = bodyMinY - piece.Position.Y;
            float localBodyMaxY = bodyMaxY - piece.Position.Y;
            float localStepLimitY = stepLimitY - piece.Position.Y;

            if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m)
            {
                if (TryEvaluateLocalColumnSpan(piece.Kind, localPos.X, localPos.Z, r, out float gMinY, out float gMaxY) &&
                    localBodyMaxY > gMinY && localBodyMinY < gMaxY)
                {
                    float signZ = localPos.Z >= 0f ? 1f : -1f;
                    float targetZ = signZ * (DefaultWallHalfThickness + r + 0.005f);
                    float pushZ = Math.Clamp(targetZ - localPos.Z, -0.24f, 0.24f);
                    Vector3 worldPush = Vector3.Transform(new Vector3(0f, 0f, pushZ), piece.Rotation);
                    posX += worldPush.X;
                    posZ += worldPush.Z;
                    return true;
                }
                return false;
            }

            if (IsSlopedRoof(piece.Kind, out _))
            {
                return false;
            }

            bool moved = false;
            int boxCount = PopulateLocalSolidBoxes(piece.Kind, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (localBodyMaxY <= box.Min.Y || localBodyMinY >= box.Max.Y) continue;
                if (box.IsWalkableTop && box.Max.Y <= localStepLimitY) continue;

                float closestX = Math.Clamp(localPos.X, box.Min.X, box.Max.X);
                float closestZ = Math.Clamp(localPos.Z, box.Min.Z, box.Max.Z);
                float ox = localPos.X - closestX;
                float oz = localPos.Z - closestZ;
                float distSq = ox * ox + oz * oz;
                if (distSq >= rSq) continue;

                Vector3 localPush = ComputeHorizontalPushOut(localPos, in box, ox, oz, distSq, r);
                Vector3 worldPush = Vector3.Transform(localPush, piece.Rotation);
                posX += worldPush.X;
                posZ += worldPush.Z;
                localPos += localPush;
                moved = true;
            }

            return moved;
        }

        /// <summary>
        /// Oyuncunun ayagi altindaki en yuksek yurunebilir moduler taban kotunu (subasman, 1./2. kat doseme,
        /// cati egimi, kiris, sutun tepesi) anti-asansor kuraliyla (topY &lt;= refFeetY + maxStepUp) dondurur.
        /// Uygun yuzey yoksa -30f dondurur (0 B GC).
        /// </summary>
        public static float SampleSupportGroundY(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            float posX,
            float posZ,
            float refFeetY,
            float maxStepUpMeters = 1.08f,
            float footRadiusMeters = 0.24f)
        {
            if (pieces.Length == 0) return -30f;

            float bestY = -30f;
            float maxReachY = refFeetY + maxStepUpMeters;
            float r = Math.Clamp(footRadiusMeters, 0.14f, 0.30f);

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                float candidateY = SampleModularPieceSupportGroundY(in piece, posX, posZ, refFeetY, maxReachY, r);
                if (candidateY > bestY)
                {
                    bestY = candidateY;
                }
            }

            return bestY;
        }

        internal static float SampleModularPieceSupportGroundY(
            in ModularPlacedPieceNode piece,
            float posX,
            float posZ,
            float refFeetY,
            float maxReachY,
            float r)
        {
            if (!IsWithinBroadPhase(in piece, posX, refFeetY, posZ)) return -30f;

            Quaternion invRot = Quaternion.Inverse(piece.Rotation);
            Vector3 localPos = Vector3.Transform(new Vector3(posX - piece.Position.X, 0f, posZ - piece.Position.Z), invRot);

            if (IsSlopedRoof(piece.Kind, out _))
            {
                if (TryEvaluateLocalColumnSpan(piece.Kind, localPos.X, localPos.Z, r * 0.65f, out _, out float roofTopLocalY))
                {
                    float worldTopY = piece.Position.Y + roofTopLocalY;
                    if (worldTopY <= maxReachY) return worldTopY;
                }
                return -30f;
            }

            if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m ||
                piece.Kind == ModularBuildPieceKind.DiagonalBrace2m)
            {
                return -30f;
            }

            float bestY = -30f;
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];
            int boxCount = PopulateLocalSolidBoxes(piece.Kind, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (!box.IsWalkableTop) continue;

                if (localPos.X < box.Min.X - r || localPos.X > box.Max.X + r ||
                    localPos.Z < box.Min.Z - r || localPos.Z > box.Max.Z + r)
                {
                    continue;
                }

                float worldTopY = piece.Position.Y + box.Max.Y;
                if (worldTopY <= maxReachY && worldTopY > bestY)
                {
                    bestY = worldTopY;
                }
            }

            return bestY;
        }

        /// <summary>
        /// Ziplama veya ucma sirasinda ust kat dosemesi, kapi lentosu, kiris veya egimli cati altina
        /// kafa carpma durumunu tespit eder ve ayak kotunu guvenli tavana kenetler (0 B GC).
        /// </summary>
        public static bool TryResolveCeilingHeadBonk(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            ref Vector3 feetPos,
            float capsuleHeightMeters = 1.80f,
            float capsuleRadiusMeters = 0.30f)
        {
            if (pieces.Length == 0) return false;

            float effH = Math.Clamp(capsuleHeightMeters, 0.45f, 1.85f);
            float effR = Math.Clamp(capsuleRadiusMeters * 0.78f, 0.16f, 0.26f);
            float chestY = feetPos.Y + effH * 0.55f;
            float crownY = feetPos.Y + effH + 0.03f;

            bool bonked = false;
            float lowestCeilingY = float.MaxValue;

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                if (TrySampleModularPieceCeilingY(in piece, feetPos, effH, effR, chestY, crownY, out float worldCeilingY))
                {
                    if (worldCeilingY < lowestCeilingY)
                    {
                        lowestCeilingY = worldCeilingY;
                        bonked = true;
                    }
                }
            }

            if (bonked)
            {
                float maxAllowedFeetY = lowestCeilingY - effH - 0.025f;
                if (feetPos.Y > maxAllowedFeetY)
                {
                    feetPos.Y = MathF.Max(feetPos.Y - 0.35f, maxAllowedFeetY);
                }
            }

            return bonked;
        }

        internal static bool TrySampleModularPieceCeilingY(
            in ModularPlacedPieceNode piece,
            Vector3 feetPos,
            float effH,
            float effR,
            float chestY,
            float crownY,
            out float lowestCeilingY)
        {
            lowestCeilingY = float.MaxValue;
            if (!IsWithinBroadPhase(in piece, feetPos.X, feetPos.Y + effH * 0.5f, feetPos.Z)) return false;

            Quaternion invRot = Quaternion.Inverse(piece.Rotation);
            Vector3 localFeet = Vector3.Transform(feetPos - piece.Position, invRot);
            float localChestY = chestY - piece.Position.Y;
            float localCrownY = crownY - piece.Position.Y;

            if (IsSlopedRoof(piece.Kind, out _))
            {
                if (TryEvaluateLocalColumnSpan(piece.Kind, localFeet.X, localFeet.Z, effR, out float sMinY, out float sMaxY))
                {
                    if (localCrownY >= sMinY && localChestY < sMinY && localFeet.Y < sMaxY)
                    {
                        lowestCeilingY = piece.Position.Y + sMinY;
                        return true;
                    }
                }
                return false;
            }

            if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m) return false;

            bool found = false;
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];
            int boxCount = PopulateLocalSolidBoxes(piece.Kind, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (localFeet.X < box.Min.X - effR || localFeet.X > box.Max.X + effR ||
                    localFeet.Z < box.Min.Z - effR || localFeet.Z > box.Max.Z + effR)
                {
                    continue;
                }

                if (localCrownY >= box.Min.Y && localChestY < box.Min.Y)
                {
                    float worldCeilingY = piece.Position.Y + box.Min.Y;
                    if (worldCeilingY < lowestCeilingY)
                    {
                        lowestCeilingY = worldCeilingY;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Moduler tavan/cati altinda comelme (1.15m) ve ayaga kalkma (1.80m) bas-ustu boslugunu olcer (0 B GC).
        /// </summary>
        public static void EvaluateHeadroomClearance(
            ReadOnlySpan<ModularPlacedPieceNode> pieces,
            Vector3 feetPos,
            out bool canCrouch115,
            out bool canStand180)
        {
            canCrouch115 = true;
            canStand180 = true;
            if (pieces.Length == 0) return;

            const float probeR = 0.22f;
            float chestY = feetPos.Y + 0.55f;
            float crouchTopY = feetPos.Y + 1.15f;
            float standTopY = feetPos.Y + 1.78f;

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                EvaluateModularPieceHeadroom(in piece, feetPos, probeR, chestY, crouchTopY, standTopY, ref canCrouch115, ref canStand180);
            }

            if (!canCrouch115) canStand180 = false;
        }

        internal static void EvaluateModularPieceHeadroom(
            in ModularPlacedPieceNode piece,
            Vector3 feetPos,
            float probeR,
            float chestY,
            float crouchTopY,
            float standTopY,
            ref bool canCrouch115,
            ref bool canStand180)
        {
            if (!IsWithinBroadPhase(in piece, feetPos.X, feetPos.Y + 1.0f, feetPos.Z)) return;

            Quaternion invRot = Quaternion.Inverse(piece.Rotation);
            Vector3 localPos = Vector3.Transform(feetPos - piece.Position, invRot);
            float localChestY = chestY - piece.Position.Y;
            float localCrouchY = crouchTopY - piece.Position.Y;
            float localStandY = standTopY - piece.Position.Y;

            if (IsSlopedRoof(piece.Kind, out _))
            {
                if (TryEvaluateLocalColumnSpan(piece.Kind, localPos.X, localPos.Z, probeR, out float sMinY, out _))
                {
                    if (sMinY > localChestY)
                    {
                        if (sMinY < localCrouchY) canCrouch115 = false;
                        if (sMinY < localStandY) canStand180 = false;
                    }
                }
                return;
            }

            if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m) return;

            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];
            int boxCount = PopulateLocalSolidBoxes(piece.Kind, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (localPos.X < box.Min.X - probeR || localPos.X > box.Max.X + probeR ||
                    localPos.Z < box.Min.Z - probeR || localPos.Z > box.Max.Z + probeR)
                {
                    continue;
                }

                if (box.Min.Y > localChestY)
                {
                    if (box.Min.Y < localCrouchY) canCrouch115 = false;
                    if (box.Min.Y < localStandY) canStand180 = false;
                }
            }
        }

        // =====================================================================
        // BIRLESIK YAPI DUGUMU (StructuralNode64) — KAPI, PENCERE, MERDIVEN,
        // ISKELE, STAIRWELL TRIMMER VE EKOL 4 KEMER ANALITIK COZUCUSU (0-GC)
        // =====================================================================

        /// <summary>
        /// Kapı düğümünün varyantını (Menteşeli / Sürgülü / Çift Kanatlı) yapılandırır.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConfigureDoorVariant(
            ref StructuralNode64 node,
            StructureDoorKind doorKind,
            bool isOpen = false,
            bool isLocked = false)
        {
            node.Role = StructuralElementRole.DoorFrameOpening;
            node.CompletedCourses = (byte)((byte)doorKind + 1);
            if (doorKind == StructureDoorKind.DoubleLeafGate)
            {
                node.ThicknessProfile = (byte)StructureThicknessProfile.HeavyBearing;
            }
            node.IsLocked = isLocked;
            node.IsDoorOrWindowOpen = !isLocked && isOpen;
        }

        /// <summary>
        /// Kapı düğümünün türünü (Menteşeli / Sürgülü / Çift Kanatlı) çözer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructureDoorKind ResolveDoorKind(in StructuralNode64 node)
        {
            if (node.ThicknessProfile == (byte)StructureThicknessProfile.HeavyBearing || node.CompletedCourses >= 3)
            {
                return StructureDoorKind.DoubleLeafGate;
            }
            if (node.CompletedCourses == 2)
            {
                return StructureDoorKind.Sliding;
            }
            return StructureDoorKind.HingedSingle;
        }

        /// <summary>
        /// [E] etkileşimi: kapı veya pencere kepengini açıp kapatır.
        /// Kilitliyken (<see cref="StructuralNode64.IsLocked"/>) yetkisiz kullanıcı açamaz (false döner).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryInteractToggleDoorOrWindow(ref StructuralNode64 node, bool isAuthorized = false)
        {
            if (node.Role != StructuralElementRole.DoorFrameOpening &&
                node.Role != StructuralElementRole.WindowFrameOpening)
            {
                return false;
            }

            if (node.IsLocked && !isAuthorized)
            {
                return false;
            }

            node.IsDoorOrWindowOpen = !node.IsDoorOrWindowOpen;
            return true;
        }

        /// <summary>
        /// [Shift+E] etkileşimi: kapalı kapı/kepenk sürgüsünü içeriden veya yetkiyle kilitler/açar.
        /// Açık kapı doğrudan kilitlenemez (önce kapatılmalıdır).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryToggleDoorOrWindowLock(ref StructuralNode64 node, bool canOperateInteriorBolt)
        {
            if (!canOperateInteriorBolt) return false;
            if (node.Role != StructuralElementRole.DoorFrameOpening &&
                node.Role != StructuralElementRole.WindowFrameOpening)
            {
                return false;
            }

            if (node.IsDoorOrWindowOpen) return false;
            node.IsLocked = !node.IsLocked;
            return true;
        }

        /// <summary>
        /// Merdiven düğümünün türünü (Düz / L-Dönüşlü / Taş Sarmal) yapılandırır.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConfigureStairVariant(ref StructuralNode64 node, StructureStairKind stairKind)
        {
            node.Role = StructuralElementRole.StairStringerFlight;
            node.CompletedCourses = (byte)((byte)stairKind + 1);
        }

        /// <summary>
        /// Merdiven düğümünün geometrik tipini çözer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StructureStairKind ResolveStairKind(in StructuralNode64 node)
        {
            if (node.CompletedCourses >= 3) return StructureStairKind.SpiralStone;
            if (node.CompletedCourses == 2) return StructureStairKind.LTurn;
            return StructureStairKind.Straight;
        }

        /// <summary>
        /// Düğümün aktif katı çarpışma üretip üretmediğini denetler.
        /// Çökmüş veya anında düşen düğümler hayalet çarpışma bırakmaz.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNodeActiveForCollision(in StructuralNode64 node)
        {
            return node.Integrity01 > 0.001f &&
                   node.CollapseState != StructureCollapseState.Collapsed &&
                   node.CollapseState != StructureCollapseState.DetachedInstantFall;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WorldToNodeLocal(in StructuralNode64 node, float wx, float wy, float wz, out float lx, out float ly, out float lz)
        {
            float dx = wx - node.PosX;
            float dy = wy - node.PosY;
            float dz = wz - node.PosZ;
            ly = dy;
            switch (node.YawQuarterTurns & 0x03)
            {
                case 1: // +90 deg around Y: inv is -90 deg -> lx = -dz, lz = dx
                    lx = -dz;
                    lz = dx;
                    break;
                case 2: // 180 deg
                    lx = -dx;
                    lz = -dz;
                    break;
                case 3: // +270 deg around Y: inv is +90 deg -> lx = dz, lz = -dx
                    lx = dz;
                    lz = -dx;
                    break;
                default: // 0 deg
                    lx = dx;
                    lz = dz;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void NodeLocalVectorToWorld(in StructuralNode64 node, float lx, float lz, out float wx, out float wz)
        {
            switch (node.YawQuarterTurns & 0x03)
            {
                case 1: // +90 deg around Y
                    wx = lz;
                    wz = -lx;
                    break;
                case 2: // 180 deg
                    wx = -lx;
                    wz = -lz;
                    break;
                case 3: // +270 deg around Y
                    wx = -lz;
                    wz = lx;
                    break;
                default:
                    wx = lx;
                    wz = lz;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ResolveWallHalfThickness(in StructuralNode64 node)
        {
            return (StructureThicknessProfile)node.ThicknessProfile switch
            {
                StructureThicknessProfile.Slender => 0.09f,
                StructureThicknessProfile.HeavyBearing => 0.24f,
                _ => 0.14f
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsArchOrVaultRole(StructuralElementRole role)
        {
            return role == StructuralElementRole.ArchVoussoir ||
                   role == StructuralElementRole.ArchKeystone ||
                   role == StructuralElementRole.RelievingArch ||
                   role == StructuralElementRole.BarrelOrCrossVault;
        }

        /// <summary>
        /// DOK4 §5.4: Ekol 4 kemer ve tonoz altı parabolik karın yüksekliği
        /// y_karin(x) = y_uzengi + f * (1 - 4*x^2 / L^2).
        /// </summary>
        public static void GetArchGeometryParams(
            in StructuralNode64 node,
            out float clearSpanL,
            out float pierWidth,
            out float springHeightY,
            out float archRiseF,
            out float extradosTopY,
            out float halfDepthZ)
        {
            bool monumental = node.School == ArchitecturalSchool.MonumentalArchDome ||
                              node.ThicknessProfile == (byte)StructureThicknessProfile.HeavyBearing;
            if (monumental)
            {
                clearSpanL = 3.20f;
                pierWidth = 0.45f;
                springHeightY = 1.65f;
                archRiseF = 1.60f;
                extradosTopY = 3.65f;
                halfDepthZ = node.Role == StructuralElementRole.BarrelOrCrossVault ? 1.00f : 0.32f;
            }
            else
            {
                clearSpanL = 2.00f;
                pierWidth = 0.32f;
                springHeightY = 1.50f;
                archRiseF = 1.05f;
                extradosTopY = 2.85f;
                halfDepthZ = node.Role == StructuralElementRole.BarrelOrCrossVault ? 1.00f : 0.22f;
            }
        }

        /// <summary>
        /// Verilen yerel X koordinatında kemer karın yüksekliğini (yerel Y, metre) döner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateArchIntradosLocalY(in StructuralNode64 node, float localX)
        {
            GetArchGeometryParams(in node, out float spanL, out _, out float springY, out float riseF, out _, out _);
            float halfL = spanL * 0.5f;
            float clampedX = Math.Clamp(localX, -halfL, halfL);
            float ratio = (4.0f * clampedX * clampedX) / (spanL * spanL);
            return springY + riseF * MathF.Max(0.0f, 1.0f - ratio);
        }

        /// <summary>
        /// Bir <see cref="StructuralNode64"/> düğümünün yerel katı OBB kutularını doldurur (0 B GC).
        /// Kapı açık/kapalı durumunu, pencere parapet+söve+lentosunu, StairwellTrimmerOpening çevresini
        /// ve ModularScaffolding platform + korkuluk kutularını üretir.
        /// En az 8 elemanlı <paramref name="outBoxes"/> tamponu verilmelidir.
        /// </summary>
        public static int PopulateNodeLocalSolidBoxes(in StructuralNode64 node, Span<LocalSolidBox> outBoxes)
        {
            if (!IsNodeActiveForCollision(in node)) return 0;

            float halfT = ResolveWallHalfThickness(in node);

            switch (node.Role)
            {
                // --- A. Temel ve Zemin Altlıkları ---
                case StructuralElementRole.TrenchRubbleFooting:
                case StructuralElementRole.StonePlinthFoundation:
                case StructuralElementRole.MonumentalAshlarPedestal:
                case StructuralElementRole.SteppedSlopePlinth:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, -0.65f, -1.00f),
                        new Vector3(1.00f, 0.32f, 1.00f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.RetainingWallCourse:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -MathF.Max(0.24f, halfT)),
                        new Vector3(1.00f, 1.60f, MathF.Max(0.24f, halfT)),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.SillBeamOrLog:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.02f, 0.00f, -0.18f),
                        new Vector3(1.02f, 0.34f, 0.18f),
                        isWalkableTop: true);
                    return 1;

                // --- B. Düşey Taşıyıcılar ve Duvarlar ---
                case StructuralElementRole.NotchedWallLog:
                case StructuralElementRole.RubbleMasonryCourse:
                case StructuralElementRole.AshlarMasonryWall:
                case StructuralElementRole.InfillPanelWattleAdobe:
                case StructuralElementRole.RawCrystalMassSegment:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -halfT),
                        new Vector3(1.00f, 2.00f, halfT),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.TimberPost:
                case StructuralElementRole.DrivenTimberPile:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.16f, 0.00f, -0.16f),
                        new Vector3(0.16f, 3.00f, 0.16f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.MonolithicColumnShaft:
                case StructuralElementRole.ColumnCapitalImpost:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.28f, 0.00f, -0.28f),
                        new Vector3(0.28f, 3.20f, 0.28f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.ExteriorButtressPier:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.42f, 0.00f, -0.55f),
                        new Vector3(0.42f, 3.20f, 0.55f),
                        isWalkableTop: true);
                    return 1;

                // --- C. Yatay Açıklık, Döşeme, Kiriş, Payanda ---
                case StructuralElementRole.FloorJoistDeck:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, -0.04f, -1.00f),
                        new Vector3(1.00f, 0.08f, 1.00f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.TimberMainGirder:
                case StructuralElementRole.StoneOrWoodLintel:
                case StructuralElementRole.RoofPurlin:
                case StructuralElementRole.CollarTieOrTruss:
                case StructuralElementRole.CantileverCorbelBracket:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.04f, 0.00f, -0.15f),
                        new Vector3(1.04f, 0.28f, 0.15f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.IronTieRod:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.60f, 0.00f, -0.05f),
                        new Vector3(1.60f, 0.09f, 0.05f),
                        isWalkableTop: false);
                    return 1;

                case StructuralElementRole.RoofRidgeBeam:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.02f, -0.02f, -0.34f),
                        new Vector3(1.02f, 0.28f, 0.34f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.DiagonalWindBrace:
                case StructuralElementRole.FlyingButtressStrut:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.76f, 0.00f, -0.10f),
                        new Vector3(-0.22f, 0.52f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-0.28f, 0.46f, -0.10f),
                        new Vector3(0.24f, 0.96f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(0.20f, 0.92f, -0.10f),
                        new Vector3(0.76f, 1.44f, 0.10f),
                        isWalkableTop: false);
                    return 3;

                // --- D. Kubbe / Tambur (Zemin kat geçişi açık, üst kasnak katı) ---
                case StructuralElementRole.PendentiveOrSquinch:
                case StructuralElementRole.DomeRingCourse:
                case StructuralElementRole.DomeOculusCrown:
                case StructuralElementRole.DomeDrumTambour:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 2.20f, -1.00f),
                        new Vector3(1.00f, 2.55f, 1.00f),
                        isWalkableTop: true);
                    return 1;

                // --- E1. Kapı Kasası + Açılır-Kapanır Kanat OBB ---
                case StructuralElementRole.DoorFrameOpening:
                {
                    StructureDoorKind doorKind = ResolveDoorKind(in node);
                    if (doorKind == StructureDoorKind.DoubleLeafGate)
                    {
                        const float gateHalfOuter = 2.00f;
                        const float gateClearHalf = 1.22f; // 2.44m çift kanat geniş geçit (Kağnı & At geçer)
                        const float gateHeight = 3.20f;
                        const float gateLintelY = 2.95f;

                        // Sol & sağ kale sövesi + üst lento
                        outBoxes[0] = new LocalSolidBox(new Vector3(-gateHalfOuter, 0.00f, -halfT), new Vector3(-gateClearHalf, gateHeight, halfT));
                        outBoxes[1] = new LocalSolidBox(new Vector3(gateClearHalf, 0.00f, -halfT), new Vector3(gateHalfOuter, gateHeight, halfT));
                        outBoxes[2] = new LocalSolidBox(new Vector3(-gateClearHalf, gateLintelY, -halfT), new Vector3(gateClearHalf, gateHeight, halfT));

                        if (!node.IsDoorOrWindowOpen)
                        {
                            // Kapalı çift kanat: orta açıklığı tamamen bloke eder
                            outBoxes[3] = new LocalSolidBox(
                                new Vector3(-gateClearHalf, 0.00f, -0.07f),
                                new Vector3(gateClearHalf, gateLintelY, 0.07f),
                                isWalkableTop: false);
                            return 4;
                        }

                        // Açık çift kanat: her iki kanat 90 deg içeri (+Z) söve kenarına katlanır, merkez geçit tamamen açık!
                        outBoxes[3] = new LocalSolidBox(
                            new Vector3(-gateClearHalf - 0.04f, 0.00f, halfT),
                            new Vector3(-gateClearHalf + 0.08f, gateLintelY, halfT + 1.18f),
                            isWalkableTop: false);
                        outBoxes[4] = new LocalSolidBox(
                            new Vector3(gateClearHalf - 0.08f, 0.00f, halfT),
                            new Vector3(gateClearHalf + 0.04f, gateLintelY, halfT + 1.18f),
                            isWalkableTop: false);
                        return 5;
                    }

                    // Tek kanat menteşeli veya sürgülü kapı (2.0m x 2.04m, net 1.04m x 1.94m açıklık)
                    const float clearHalf = 0.52f;
                    const float lintelY = 1.94f;
                    outBoxes[0] = new LocalSolidBox(new Vector3(-1.00f, 0.00f, -halfT), new Vector3(-clearHalf, 2.04f, halfT));
                    outBoxes[1] = new LocalSolidBox(new Vector3(clearHalf, 0.00f, -halfT), new Vector3(1.00f, 2.04f, halfT));
                    outBoxes[2] = new LocalSolidBox(new Vector3(-clearHalf, lintelY, -halfT), new Vector3(clearHalf, 2.06f, halfT));

                    if (!node.IsDoorOrWindowOpen)
                    {
                        // Kapalı kanat: kapsülü ve AI'ı kesinlikle bloklar
                        outBoxes[3] = new LocalSolidBox(
                            new Vector3(-clearHalf, 0.00f, -0.05f),
                            new Vector3(clearHalf, lintelY, 0.05f),
                            isWalkableTop: false);
                        return 4;
                    }

                    if (doorKind == StructureDoorKind.Sliding)
                    {
                        // Sürgülü kanat: duvar yüzeyine paralel +X yönünde sağ sövenin dışına kayar (0 süpürme alanı)
                        outBoxes[3] = new LocalSolidBox(
                            new Vector3(clearHalf + 0.02f, 0.00f, halfT + 0.01f),
                            new Vector3(clearHalf + 1.04f, lintelY, halfT + 0.09f),
                            isWalkableTop: false);
                        return 4;
                    }

                    // Menteşeli tek kanat açık: -X sövesinden +Z yönüne 90 deg döner, merkez geçit tamamen açık!
                    outBoxes[3] = new LocalSolidBox(
                        new Vector3(-clearHalf - 0.06f, 0.00f, halfT),
                        new Vector3(-clearHalf + 0.02f, lintelY, halfT + 0.98f),
                        isWalkableTop: false);
                    return 4;
                }

                // --- E2. Pencere Kasası (Parapet + Söveler + Lento: Kapsülü tutar, orta boşluk ok/görüş geçirir) ---
                case StructuralElementRole.WindowFrameOpening:
                    // Alt parapet (0..0.95m, IsWalkableTop = false -> oyuncu pencereden yürüyüp geçemez!)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -halfT),
                        new Vector3(1.00f, WindowSillTopY, halfT),
                        isWalkableTop: false);
                    // Üst lento (1.80..2.00m)
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-1.00f, WindowLintelBottomY, -halfT),
                        new Vector3(1.00f, 2.00f, halfT),
                        isWalkableTop: true);
                    // Sol söve
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(-1.00f, WindowSillTopY, -halfT),
                        new Vector3(-WindowApertureHalfWidth, WindowLintelBottomY, halfT),
                        isWalkableTop: false);
                    // Sağ söve
                    outBoxes[3] = new LocalSolidBox(
                        new Vector3(WindowApertureHalfWidth, WindowSillTopY, -halfT),
                        new Vector3(1.00f, WindowLintelBottomY, halfT),
                        isWalkableTop: false);
                    return 4;

                // --- E3. Şömine & Baca ---
                case StructuralElementRole.MasonryHearthChimney:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.80f, 0.00f, -0.50f),
                        new Vector3(0.80f, 2.00f, 0.50f),
                        isWalkableTop: true);
                    return 1;

                // --- E4. Üst Kat Merdiven Boşluğu (StairwellTrimmerOpening: Yalnızca kenar Header/Trimmer çerçevesi katı!) ---
                case StructuralElementRole.StairwellTrimmerOpening:
                    // -X Trimmer kirişi
                    outBoxes[0] = new LocalSolidBox(new Vector3(-1.00f, -0.06f, -1.00f), new Vector3(-StairwellOpeningHalfExtent, 0.10f, 1.00f), isWalkableTop: true);
                    // +X Trimmer kirişi
                    outBoxes[1] = new LocalSolidBox(new Vector3(StairwellOpeningHalfExtent, -0.06f, -1.00f), new Vector3(1.00f, 0.10f, 1.00f), isWalkableTop: true);
                    // -Z Header kirişi
                    outBoxes[2] = new LocalSolidBox(new Vector3(-StairwellOpeningHalfExtent, -0.06f, -1.00f), new Vector3(StairwellOpeningHalfExtent, 0.10f, -StairwellOpeningHalfExtent), isWalkableTop: true);
                    // +Z Header kirişi
                    outBoxes[3] = new LocalSolidBox(new Vector3(-StairwellOpeningHalfExtent, -0.06f, StairwellOpeningHalfExtent), new Vector3(StairwellOpeningHalfExtent, 0.10f, 1.00f), isWalkableTop: true);
                    return 4;

                // --- E5. Yürünebilir Dış Cephe İskele (ModularScaffolding: Platform + Entegre Merdiven Boşluğu + 0.95m Korkuluk) ---
                case StructuralElementRole.ModularScaffolding:
                    // Ana yürünebilir kalas platformu (x in [-0.45, +1.00], z in [-1.00, +1.00], y = 2.00m)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.45f, ScaffoldingDeckTopY - 0.10f, -1.00f),
                        new Vector3(1.00f, ScaffoldingDeckTopY, 1.00f),
                        isWalkableTop: true);
                    // Entegre merdiven çıkış sahanlığı (x in [-1.00, -0.45], z in [+0.55, +1.00], y = 2.00m)
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-1.00f, ScaffoldingDeckTopY - 0.10f, 0.55f),
                        new Vector3(-0.45f, ScaffoldingDeckTopY, 1.00f),
                        isWalkableTop: true);
                    // Dış cephe bel hizası güvenlik korkuluğu (+Z kenarı, 0.95m yükseklik, düşmeyi engeller!)
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(-1.00f, ScaffoldingDeckTopY, 0.90f),
                        new Vector3(1.00f, ScaffoldingDeckTopY + ScaffoldingGuardrailHeight, 1.00f),
                        isWalkableTop: false);
                    // 4 ince köşe dikmesi (0.08m kesit)
                    outBoxes[3] = new LocalSolidBox(new Vector3(-1.00f, 0.00f, -1.00f), new Vector3(-0.92f, ScaffoldingDeckTopY, -0.92f), isWalkableTop: false);
                    outBoxes[4] = new LocalSolidBox(new Vector3(0.92f, 0.00f, -1.00f), new Vector3(1.00f, ScaffoldingDeckTopY, -0.92f), isWalkableTop: false);
                    outBoxes[5] = new LocalSolidBox(new Vector3(-1.00f, 0.00f, 0.92f), new Vector3(-0.92f, ScaffoldingDeckTopY + ScaffoldingGuardrailHeight, 1.00f), isWalkableTop: false);
                    outBoxes[6] = new LocalSolidBox(new Vector3(0.92f, 0.00f, 0.92f), new Vector3(1.00f, ScaffoldingDeckTopY + ScaffoldingGuardrailHeight, 1.00f), isWalkableTop: false);
                    return 7;

                case StructuralElementRole.TemporaryCenteringScaffold:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.76f, 0.00f, -0.10f),
                        new Vector3(-0.22f, 0.52f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-0.28f, 0.46f, -0.10f),
                        new Vector3(0.24f, 1.02f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(0.20f, 0.96f, -0.10f),
                        new Vector3(0.76f, 1.56f, 0.10f),
                        isWalkableTop: false);
                    return 3;

                // --- F. 9-Ekol Genişletilmiş Yapı ve Geçici Destek Rolleri (43..63 — 18 Yeni .glb 1:1 Pivot) ---
                case StructuralElementRole.GroovedLogPost:
                    // mod_grooved_log_post_3m.glb (3.0m oluklu dikey kütük dikmesi)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.18f, 0.00f, -0.18f),
                        new Vector3(0.18f, 3.00f, 0.18f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.SaddleNotchedBaseLog:
                case StructuralElementRole.HalfFlatStarterLog:
                    // mod_notched_log_saddle_2m.glb / mod_notched_log_dovetail_2m.glb
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.02f, 0.00f, -0.18f),
                        new Vector3(1.02f, 0.36f, 0.18f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.LogChinkingFill:
                    // mod_chinked_log_course_2m.glb
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -halfT),
                        new Vector3(1.00f, 2.00f, halfT),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.TimberRingBeam:
                    // mod_timber_ring_beam_2m.glb (2.0m çift kirişli ahşap hatıl kuşağı)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.02f, 0.00f, -0.22f),
                        new Vector3(1.02f, 0.22f, 0.22f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.PlatformStudWall:
                case StructuralElementRole.PlywoodSheathingPanel:
                {
                    // mod_platform_stud_frame_2x2m.glb / mod_osb_sheathing_panel_2x2m.glb
                    float studHalfT = MathF.Max(0.08f, halfT);
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -studHalfT),
                        new Vector3(1.00f, 2.00f, studHalfT),
                        isWalkableTop: true);
                    return 1;
                }

                case StructuralElementRole.RammedEarthWall:
                case StructuralElementRole.CobCurvedWall:
                {
                    // mod_rammed_earth_wall_2x2m.glb / mod_cob_curved_wall_2m.glb
                    float earthHalfT = MathF.Max(0.22f, halfT);
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -earthHalfT),
                        new Vector3(1.00f, 2.00f, earthHalfT),
                        isWalkableTop: true);
                    return 1;
                }

                case StructuralElementRole.EnglishBondBrickWall:
                {
                    // mod_english_bond_brick_2x2m.glb
                    float brickHalfT = MathF.Max(0.18f, halfT);
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -brickHalfT),
                        new Vector3(1.00f, 2.00f, brickHalfT),
                        isWalkableTop: true);
                    return 1;
                }

                case StructuralElementRole.RebarFoundationCage:
                    // mod_rebar_foundation_cage_2m.glb (2x2m grobeton + hasır + merkez L-filiz)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -1.00f),
                        new Vector3(1.00f, 0.32f, 1.00f),
                        isWalkableTop: true);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-0.18f, 0.32f, -0.18f),
                        new Vector3(0.18f, 0.95f, 0.18f),
                        isWalkableTop: true);
                    return 2;

                case StructuralElementRole.RebarColumnCage:
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.20f, 0.00f, -0.20f),
                        new Vector3(0.20f, 3.00f, 0.20f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.ConcreteColumnFormwork:
                    // mod_concrete_column_formwork_3m.glb (3.0m kuşaklı betonarme kolon kalıbı)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.32f, 0.00f, -0.32f),
                        new Vector3(0.32f, 3.00f, 0.32f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.TelescopicShoringProp:
                case StructuralElementRole.SlabDeckFormwork:
                    // mod_telescopic_shoring_2x3m.glb: Üst 2x2m tabliye kalıbı (Y=2.68..3.00m) + 4 köşe teleskopik çelik dikme (X,Z=±0.76m)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 2.68f, -1.00f),
                        new Vector3(1.00f, 3.00f, 1.00f),
                        isWalkableTop: true);
                    outBoxes[1] = new LocalSolidBox(new Vector3(-0.84f, 0.00f, -0.84f), new Vector3(-0.68f, 2.68f, -0.68f), isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(new Vector3(0.68f, 0.00f, -0.84f), new Vector3(0.84f, 2.68f, -0.68f), isWalkableTop: false);
                    outBoxes[3] = new LocalSolidBox(new Vector3(-0.84f, 0.00f, 0.68f), new Vector3(-0.68f, 2.68f, 0.84f), isWalkableTop: false);
                    outBoxes[4] = new LocalSolidBox(new Vector3(0.68f, 0.00f, 0.68f), new Vector3(0.84f, 2.68f, 0.84f), isWalkableTop: false);
                    return 5;

                case StructuralElementRole.SteelHBeamColumn:
                    // mod_steel_h_column_3m.glb (3.0m H-profil çelik kolon)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.24f, 0.00f, -0.24f),
                        new Vector3(0.24f, 3.00f, 0.24f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.SteelGuyWireBrace:
                    // mod_steel_x_brace_2x2m.glb (45° çelik gergi halatı)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.76f, 0.00f, -0.09f),
                        new Vector3(-0.22f, 0.56f, 0.09f),
                        isWalkableTop: false);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-0.28f, 0.50f, -0.09f),
                        new Vector3(0.28f, 1.12f, 0.09f),
                        isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(0.22f, 1.06f, -0.09f),
                        new Vector3(0.76f, 1.68f, 0.09f),
                        isWalkableTop: false);
                    return 3;

                case StructuralElementRole.SteelXBrace:
                    // mod_steel_x_brace_2x2m.glb (2x2m bayrak plakalı çelik X-çapraz perde)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.98f, 0.00f, -0.09f),
                        new Vector3(0.98f, 2.00f, 0.09f),
                        isWalkableTop: false);
                    return 1;

                case StructuralElementRole.CorrugatedMetalDeck:
                    // mod_metal_deck_slab_2x2m.glb (2x2x0.26m oluklu trapez sac + kompozit beton döşeme)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -1.00f),
                        new Vector3(1.00f, 0.26f, 1.00f),
                        isWalkableTop: true);
                    return 1;

                case StructuralElementRole.WoodenArchCentering:
                    // mod_wooden_arch_centering_2m.glb (2.0m ahşap kemer kalıbı + kama takozları)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-0.98f, 0.00f, -0.23f),
                        new Vector3(-0.74f, 0.90f, 0.23f),
                        isWalkableTop: false);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(0.74f, 0.00f, -0.23f),
                        new Vector3(0.98f, 0.90f, 0.23f),
                        isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(-0.98f, 0.12f, -0.23f),
                        new Vector3(0.98f, 1.15f, 0.23f),
                        isWalkableTop: false);
                    return 3;

                case StructuralElementRole.SlidingEarthFormwork:
                    // mod_sliding_earth_formwork_2m.glb (2.0m x 0.92m gergili kayar toprak kalıbı)
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(-1.00f, 0.00f, -0.28f),
                        new Vector3(1.00f, 0.92f, 0.28f),
                        isWalkableTop: true);
                    return 1;

                // Analitik eğimli/sarmal/kemer elemanlar özel değerlendiricilerle çözülür
                case StructuralElementRole.StairStringerFlight:
                case StructuralElementRole.InclinedRoofRafter:
                case StructuralElementRole.RoofCoveringCourse:
                case StructuralElementRole.ArchVoussoir:
                case StructuralElementRole.ArchKeystone:
                case StructuralElementRole.RelievingArch:
                case StructuralElementRole.BarrelOrCrossVault:
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Verilen <see cref="StructuralElementRole"/> (0..63) için BottomCenter_Zero yerel-uzay AABB sınırlarını döner (0-GC).
        /// </summary>
        public static void GetRoleLocalBounds(
            StructuralElementRole role,
            out Vector3 localMin,
            out Vector3 localMax,
            StructureThicknessProfile thickness = StructureThicknessProfile.Standard)
        {
            var probe = new StructuralNode64
            {
                Role = role,
                ThicknessProfile = (byte)thickness,
                Integrity01 = 1.0f,
                CollapseState = StructureCollapseState.Stable
            };

            if (IsArchOrVaultRole(role))
            {
                GetArchGeometryParams(in probe, out float spanL, out float pierW, out _, out _, out float topY, out float halfZ);
                float hx = (spanL * 0.5f) + pierW;
                localMin = new Vector3(-hx, 0.00f, -halfZ);
                localMax = new Vector3(hx, topY, halfZ);
                return;
            }

            if (role == StructuralElementRole.StairStringerFlight)
            {
                localMin = new Vector3(-0.80f, 0.00f, -1.25f);
                localMax = new Vector3(0.80f, thickness == StructureThicknessProfile.HeavyBearing ? 3.00f : 2.00f, 1.25f);
                return;
            }

            if (role is StructuralElementRole.InclinedRoofRafter or StructuralElementRole.RoofCoveringCourse)
            {
                float rise = thickness == StructureThicknessProfile.HeavyBearing ? 2.00f : 1.00f;
                localMin = new Vector3(-1.00f, 0.00f, -1.05f);
                localMax = new Vector3(1.00f, rise + RoofSlabThickness, 1.02f);
                return;
            }

            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[8];
            int count = PopulateNodeLocalSolidBoxes(in probe, boxes);
            if (count <= 0)
            {
                localMin = new Vector3(-0.50f, 0.00f, -0.50f);
                localMax = new Vector3(0.50f, 1.00f, 0.50f);
                return;
            }

            Vector3 minV = boxes[0].Min;
            Vector3 maxV = boxes[0].Max;
            for (int i = 1; i < count; i++)
            {
                minV = Vector3.Min(minV, boxes[i].Min);
                maxV = Vector3.Max(maxV, boxes[i].Max);
            }
            localMin = minV;
            localMax = maxV;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHorizontalFloorDeckRole(StructuralElementRole role)
        {
            return role is
                StructuralElementRole.FloorJoistDeck or
                StructuralElementRole.CorrugatedMetalDeck or
                StructuralElementRole.SlabDeckFormwork;
        }

        /// <summary>
        /// Verilen dünya konumu bir <see cref="StructuralElementRole.StairwellTrimmerOpening"/> iç oyuğu içinde mi?
        /// Üst kat döşemesinin tavan çarpması ve taban örneklenmesinden muaf tutulması (&gt;= 1.95 m kafa boşluğu) için kullanılır.
        /// </summary>
        internal static bool IsPointInsideStairwellOpening(
            ReadOnlySpan<StructuralNode64> nodes,
            float worldX,
            float slabWorldY,
            float worldZ)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 openNode = ref nodes[i];
                if (openNode.Role != StructuralElementRole.StairwellTrimmerOpening || !IsNodeActiveForCollision(in openNode))
                {
                    continue;
                }

                if (MathF.Abs(slabWorldY - openNode.PosY) > 0.45f) continue;

                WorldToNodeLocal(in openNode, worldX, slabWorldY, worldZ, out float lx, out _, out float lz);
                if (MathF.Abs(lx) <= 1.02f && MathF.Abs(lz) <= 1.02f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Merdiven (Düz / L / Taş Sarmal) veya iskele entegre merdiven rampa yüksekliğini yerel uzayda örnekler.
        /// </summary>
        internal static bool TrySampleWalkableRampOrStairLocalY(
            in StructuralNode64 node,
            float localX,
            float localZ,
            float localRefFeetY,
            float maxStepUpMeters,
            float footRadius,
            out float localWalkTopY,
            out float localUndersideY)
        {
            localWalkTopY = -30f;
            localUndersideY = 0f;

            if (node.Role == StructuralElementRole.ModularScaffolding)
            {
                // Entegre tırmanılabilir iç merdiven: x in [-0.95, -0.45], z in [-0.90, +0.55] -> y: 0.0 -> 2.00m
                if (localX >= -0.98f - footRadius && localX <= -0.42f + footRadius &&
                    localZ >= -0.95f - footRadius && localZ <= 0.60f + footRadius)
                {
                    float t = Math.Clamp((localZ + 0.90f) / 1.45f, 0.0f, 1.0f);
                    localWalkTopY = t * ScaffoldingDeckTopY;
                    localUndersideY = MathF.Max(0.0f, localWalkTopY - 0.14f);
                    return localWalkTopY <= localRefFeetY + maxStepUpMeters;
                }
                return false;
            }

            if (node.Role == StructuralElementRole.InclinedRoofRafter ||
                node.Role == StructuralElementRole.RoofCoveringCourse)
            {
                if (localX < -1.00f - footRadius || localX > 1.00f + footRadius ||
                    localZ < -1.02f - footRadius || localZ > 1.02f + footRadius)
                {
                    return false;
                }

                float rise = node.ThicknessProfile == (byte)StructureThicknessProfile.HeavyBearing ? 2.00f : 1.00f;
                float t = Math.Clamp((localZ + 1.00f) * 0.50f, 0.0f, 1.0f);
                localWalkTopY = t * rise + RoofSlabThickness;
                localUndersideY = t * rise - 0.04f;
                return true;
            }

            if (node.Role != StructuralElementRole.StairStringerFlight) return false;

            StructureStairKind stairKind = ResolveStairKind(in node);
            switch (stairKind)
            {
                case StructureStairKind.Straight:
                {
                    // Düz merdiven: genişlik 1.60m (x in [-0.80, +0.80]), yatay 2.50m (z in [-1.25, +1.25]), dikey 2.00m (Heavy: 3.00m)
                    const float halfW = 0.80f;
                    const float halfLen = 1.25f;
                    float totalRise = node.ThicknessProfile == (byte)StructureThicknessProfile.HeavyBearing ? 3.00f : 2.00f;

                    if (localX < -halfW - footRadius || localX > halfW + footRadius ||
                        localZ < -halfLen - footRadius || localZ > halfLen + footRadius)
                    {
                        return false;
                    }

                    float t = Math.Clamp((localZ + halfLen) / (2.0f * halfLen), 0.0f, 1.0f);
                    localWalkTopY = t * totalRise;
                    localUndersideY = MathF.Max(0.0f, localWalkTopY - 0.22f);
                    return true;
                }

                case StructureStairKind.LTurn:
                {
                    // L-dönüşlü sahanlıklı merdiven (2.0m x 2.0m ayak izi, 2.0m toplam kat yüksekliği)
                    if (localX < -1.00f - footRadius || localX > 1.00f + footRadius ||
                        localZ < -1.00f - footRadius || localZ > 1.00f + footRadius)
                    {
                        return false;
                    }

                    float cx = Math.Clamp(localX, -1.00f, 1.00f);
                    float cz = Math.Clamp(localZ, -1.00f, 1.00f);
                    if (cx <= 0.00f && cz <= 0.00f)
                    {
                        // Alt kol: z in [-1.0, 0.0] -> y: 0.0 .. 1.0m
                        localWalkTopY = Math.Clamp(cz + 1.00f, 0.0f, 1.0f);
                        localUndersideY = MathF.Max(0.0f, localWalkTopY - 0.20f);
                        return true;
                    }
                    if (cx <= 0.00f && cz > 0.00f)
                    {
                        // Ara sahanlık: y = 1.00m
                        localWalkTopY = 1.00f;
                        localUndersideY = 0.82f;
                        return true;
                    }
                    if (cx > 0.00f && cz >= -0.05f)
                    {
                        // Üst kol: x in [0.0, +1.0] -> y: 1.0 .. 2.0m
                        localWalkTopY = 1.00f + Math.Clamp(cx, 0.0f, 1.0f);
                        localUndersideY = MathF.Max(0.82f, localWalkTopY - 0.20f);
                        return true;
                    }
                    return false;
                }

                case StructureStairKind.SpiralStone:
                {
                    // Taş sarmal merdiven: merkez taş çekirdek r=0.24m, dış çap r=1.35m, 360 deg dönüşte 3.0m yükseklik
                    const float innerR = 0.24f;
                    const float outerR = 1.35f;
                    const float turnHeight = 3.00f;
                    float distSq = localX * localX + localZ * localZ;
                    float maxR = outerR + footRadius;
                    if (distSq > maxR * maxR || distSq < (innerR * 0.65f) * (innerR * 0.65f))
                    {
                        return false;
                    }

                    float angle = MathF.Atan2(localZ, localX);
                    if (angle < 0.0f) angle += TwoPi;
                    float baseHeight = (angle / TwoPi) * turnHeight;

                    // 0..3.0m sarmal turu ve tepe sahanlık devamı (tam dönüşte 3.0m'ye pürüzsüz geçiş)
                    float maxReach = localRefFeetY + maxStepUpMeters;
                    float chosenY = -30f;
                    if (baseHeight <= maxReach)
                    {
                        chosenY = baseHeight;
                    }
                    float nextTurnY = baseHeight + turnHeight;
                    if (nextTurnY <= turnHeight + 0.05f && nextTurnY <= maxReach && nextTurnY > chosenY)
                    {
                        chosenY = nextTurnY;
                    }

                    if (chosenY < -10f) return false;
                    localWalkTopY = chosenY;
                    localUndersideY = MathF.Max(0.0f, chosenY - 0.20f);
                    return true;
                }

                default:
                    return false;
            }
        }

        internal static bool IsNodeBlockingCapsule(
            in StructuralNode64 node,
            Vector3 curFeet,
            Vector3 probeFeet,
            float capsuleHeight,
            float radius,
            float maxStepUpMeters,
            bool isFloorExemptByStairwell)
        {
            if (!IsNodeActiveForCollision(in node)) return false;

            float dx = probeFeet.X - node.PosX;
            float dz = probeFeet.Z - node.PosZ;
            if ((dx * dx + dz * dz) > BroadPhaseHorizRadiusSq) return false;
            if (MathF.Abs(probeFeet.Y - node.PosY) > BroadPhaseVertHalfSpan) return false;

            if (isFloorExemptByStairwell && IsHorizontalFloorDeckRole(node.Role))
            {
                return false;
            }

            WorldToNodeLocal(in node, probeFeet.X, probeFeet.Y, probeFeet.Z, out float lx, out _, out float lz);
            float localBodyMinY = (probeFeet.Y + 0.08f) - node.PosY;
            float localBodyMaxY = (probeFeet.Y + MathF.Max(0.42f, capsuleHeight - 0.08f)) - node.PosY;
            float localStepLimitY = (curFeet.Y + maxStepUpMeters) - node.PosY;
            float rSq = radius * radius;

            // Merdiven boşluğu tavan/döşeme oyuğu (StairwellTrimmerOpening):
            // Merdiven kovası içinden üst kata tırmanırken veya üst kata adım atarken yatay geçişi bloke etmez!
            if (node.Role == StructuralElementRole.StairwellTrimmerOpening)
            {
                if (MathF.Abs(lx) <= 1.02f && MathF.Abs(lz) <= 1.02f)
                {
                    return false;
                }
            }

            // 1) Ekol 4 Kemer & Tonoz altı eğrisel geçit
            if (IsArchOrVaultRole(node.Role))
            {
                GetArchGeometryParams(in node, out float spanL, out float pierW, out _, out _, out float topY, out float halfZ);
                float totalHalfX = (spanL * 0.5f) + pierW;
                if (MathF.Abs(lz) <= halfZ + radius && MathF.Abs(lx) <= totalHalfX + radius)
                {
                    // Yan ayaklar (piers)
                    if (MathF.Abs(lx) + radius > spanL * 0.5f)
                    {
                        if (localBodyMaxY > 0.0f && localBodyMinY < topY && topY > localStepLimitY)
                        {
                            return true;
                        }
                    }

                    // Kemer karnı üstü spandrel (eğer kafa kemer karnını aşıyorsa engeller, altı tamamen açık!)
                    float probeLocalX = Math.Clamp(lx, -spanL * 0.5f, spanL * 0.5f);
                    float intradosY = EvaluateArchIntradosLocalY(in node, probeLocalX);
                    if (localBodyMaxY > intradosY && localBodyMinY < topY)
                    {
                        return true;
                    }
                }
                return false;
            }

            // 2) Merdiven (Düz / L / Sarmal)
            if (node.Role == StructuralElementRole.StairStringerFlight)
            {
                StructureStairKind stairKind = ResolveStairKind(in node);
                if (stairKind == StructureStairKind.SpiralStone)
                {
                    // Merkez taş çekirdek sütunu (r = 0.24m) her zaman katı engeldir
                    const float newelR = 0.24f;
                    float minDist = newelR + radius;
                    if ((lx * lx + lz * lz) < (minDist * minDist) &&
                        localBodyMaxY > 0.0f && localBodyMinY < 3.05f)
                    {
                        return true;
                    }
                    // Sarmal basamaklar yürünebilirdir; oyuncu basamak yüksekliğinden yürür
                    return false;
                }

                if (TrySampleWalkableRampOrStairLocalY(in node, lx, lz, curFeet.Y - node.PosY, maxStepUpMeters, radius, out float stairTopY, out float stairUnderY))
                {
                    if (localBodyMaxY > stairUnderY && localBodyMinY < stairTopY && stairTopY > localStepLimitY)
                    {
                        return true;
                    }
                }
                return false;
            }

            // 3) Eğimli çatı merteği
            if (node.Role == StructuralElementRole.InclinedRoofRafter ||
                node.Role == StructuralElementRole.RoofCoveringCourse)
            {
                if (TrySampleWalkableRampOrStairLocalY(in node, lx, lz, curFeet.Y - node.PosY, maxStepUpMeters, radius, out float roofTopY, out float roofUnderY))
                {
                    if (localBodyMaxY > roofUnderY && localBodyMinY < roofTopY && roofTopY > localStepLimitY)
                    {
                        return true;
                    }
                }
                return false;
            }

            // 4) Çoklu yerel katı OBB kutuları (Kapı, Pencere, İskele, StairwellTrimmerOpening, Duvar, Sütun, Temel, Döşeme)
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[8];
            int boxCount = PopulateNodeLocalSolidBoxes(in node, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (localBodyMaxY <= box.Min.Y || localBodyMinY >= box.Max.Y)
                {
                    continue;
                }

                float closestX = Math.Clamp(lx, box.Min.X, box.Max.X);
                float closestZ = Math.Clamp(lz, box.Min.Z, box.Max.Z);
                float ox = lx - closestX;
                float oz = lz - closestZ;
                if ((ox * ox + oz * oz) >= rSq)
                {
                    continue;
                }

                if (box.IsWalkableTop && box.Max.Y <= localStepLimitY)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        internal static bool ResolveNodeHorizontalDepenetration(
            in StructuralNode64 node,
            ref float posX,
            float posY,
            ref float posZ,
            float h,
            float r,
            float maxStepUpMeters,
            bool isFloorExemptByStairwell)
        {
            if (!IsNodeActiveForCollision(in node)) return false;
            if (isFloorExemptByStairwell && IsHorizontalFloorDeckRole(node.Role)) return false;

            float dx = posX - node.PosX;
            float dz = posZ - node.PosZ;
            if ((dx * dx + dz * dz) > BroadPhaseHorizRadiusSq) return false;
            if (MathF.Abs(posY - node.PosY) > BroadPhaseVertHalfSpan) return false;

            WorldToNodeLocal(in node, posX, posY, posZ, out float lx, out _, out float lz);
            float localBodyMinY = (posY + 0.10f) - node.PosY;
            float localBodyMaxY = (posY + MathF.Max(0.42f, h - 0.08f)) - node.PosY;
            float localStepLimitY = (posY + maxStepUpMeters) - node.PosY;
            float rSq = r * r;

            // Spiral stair central newel push-out
            if (node.Role == StructuralElementRole.StairStringerFlight && ResolveStairKind(in node) == StructureStairKind.SpiralStone)
            {
                const float newelR = 0.24f;
                float minDist = newelR + r;
                float distSq = lx * lx + lz * lz;
                if (distSq < minDist * minDist && localBodyMaxY > 0.0f && localBodyMinY < 3.05f)
                {
                    float dist = MathF.Sqrt(MathF.Max(1e-6f, distSq));
                    float push = MathF.Min(0.24f, (minDist - dist) + 0.004f);
                    float plx = distSq > 1e-6f ? (lx / dist) * push : push;
                    float plz = distSq > 1e-6f ? (lz / dist) * push : 0f;
                    NodeLocalVectorToWorld(in node, plx, plz, out float wx, out float wz);
                    posX += wx;
                    posZ += wz;
                    return true;
                }
                return false;
            }

            if (IsArchOrVaultRole(node.Role))
            {
                GetArchGeometryParams(in node, out float spanL, out float pierW, out _, out _, out float topY, out float halfZ);
                if (localBodyMaxY <= 0.0f || localBodyMinY >= topY) return false;
                float halfSpan = spanL * 0.5f;
                Span<LocalSolidBox> piers = stackalloc LocalSolidBox[2];
                piers[0] = new LocalSolidBox(new Vector3(-halfSpan - pierW, 0.0f, -halfZ), new Vector3(-halfSpan, topY, halfZ), false);
                piers[1] = new LocalSolidBox(new Vector3(halfSpan, 0.0f, -halfZ), new Vector3(halfSpan + pierW, topY, halfZ), false);
                bool movedArch = false;
                for (int b = 0; b < 2; b++)
                {
                    ref readonly LocalSolidBox box = ref piers[b];
                    float closestX = Math.Clamp(lx, box.Min.X, box.Max.X);
                    float closestZ = Math.Clamp(lz, box.Min.Z, box.Max.Z);
                    float ox = lx - closestX;
                    float oz = lz - closestZ;
                    float dSq = ox * ox + oz * oz;
                    if (dSq >= rSq) continue;
                    Vector3 lp = ComputeHorizontalPushOut(new Vector3(lx, 0f, lz), in box, ox, oz, dSq, r);
                    NodeLocalVectorToWorld(in node, lp.X, lp.Z, out float wx, out float wz);
                    posX += wx;
                    posZ += wz;
                    lx += lp.X;
                    lz += lp.Z;
                    movedArch = true;
                }
                return movedArch;
            }

            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[8];
            int boxCount = PopulateNodeLocalSolidBoxes(in node, boxes);
            bool moved = false;
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (localBodyMaxY <= box.Min.Y || localBodyMinY >= box.Max.Y) continue;
                if (box.IsWalkableTop && box.Max.Y <= localStepLimitY) continue;

                float closestX = Math.Clamp(lx, box.Min.X, box.Max.X);
                float closestZ = Math.Clamp(lz, box.Min.Z, box.Max.Z);
                float ox = lx - closestX;
                float oz = lz - closestZ;
                float distSq = ox * ox + oz * oz;
                if (distSq >= rSq) continue;

                Vector3 lp = ComputeHorizontalPushOut(new Vector3(lx, 0f, lz), in box, ox, oz, distSq, r);
                NodeLocalVectorToWorld(in node, lp.X, lp.Z, out float wx, out float wz);
                posX += wx;
                posZ += wz;
                lx += lp.X;
                lz += lp.Z;
                moved = true;
            }

            return moved;
        }

        internal static float SampleNodeSupportGroundY(
            in StructuralNode64 node,
            float posX,
            float posZ,
            float refFeetY,
            float maxStepUpMeters,
            float r,
            bool isFloorExemptByStairwell)
        {
            if (!IsNodeActiveForCollision(in node)) return -30f;
            if (isFloorExemptByStairwell && IsHorizontalFloorDeckRole(node.Role)) return -30f;

            float dx = posX - node.PosX;
            float dz = posZ - node.PosZ;
            if ((dx * dx + dz * dz) > BroadPhaseHorizRadiusSq) return -30f;
            if (MathF.Abs(refFeetY - node.PosY) > BroadPhaseVertHalfSpan) return -30f;

            float maxReachY = refFeetY + maxStepUpMeters;
            WorldToNodeLocal(in node, posX, refFeetY, posZ, out float lx, out _, out float lz);

            float bestY = -30f;

            // Merdiven, çatı ve iskele entegre merdiveni
            if (TrySampleWalkableRampOrStairLocalY(in node, lx, lz, refFeetY - node.PosY, maxStepUpMeters, r, out float rampTopY, out _))
            {
                float worldTopY = node.PosY + rampTopY;
                if (worldTopY <= maxReachY && worldTopY > bestY)
                {
                    bestY = worldTopY;
                }
            }

            // Kemer üstü yürünebilir tabla (köprü/sur yolu)
            if (IsArchOrVaultRole(node.Role))
            {
                GetArchGeometryParams(in node, out float spanL, out float pierW, out _, out _, out float topY, out float halfZ);
                float halfOuter = spanL * 0.5f + pierW;
                if (MathF.Abs(lx) <= halfOuter + r && MathF.Abs(lz) <= halfZ + r)
                {
                    float worldTopY = node.PosY + topY;
                    if (worldTopY <= maxReachY && worldTopY > bestY)
                    {
                        bestY = worldTopY;
                    }
                }
                return bestY;
            }

            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[8];
            int boxCount = PopulateNodeLocalSolidBoxes(in node, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (!box.IsWalkableTop) continue;

                if (lx < box.Min.X - r || lx > box.Max.X + r ||
                    lz < box.Min.Z - r || lz > box.Max.Z + r)
                {
                    continue;
                }

                float worldTopY = node.PosY + box.Max.Y;
                if (worldTopY <= maxReachY && worldTopY > bestY)
                {
                    bestY = worldTopY;
                }
            }

            return bestY;
        }

        internal static bool TrySampleNodeCeilingY(
            in StructuralNode64 node,
            Vector3 feetPos,
            float effH,
            float effR,
            float chestY,
            float crownY,
            bool isFloorExemptByStairwell,
            out float lowestCeilingY)
        {
            lowestCeilingY = float.MaxValue;
            if (!IsNodeActiveForCollision(in node)) return false;
            if (isFloorExemptByStairwell && IsHorizontalFloorDeckRole(node.Role)) return false;

            float dx = feetPos.X - node.PosX;
            float dz = feetPos.Z - node.PosZ;
            if ((dx * dx + dz * dz) > BroadPhaseHorizRadiusSq) return false;
            if (MathF.Abs((feetPos.Y + effH * 0.5f) - node.PosY) > BroadPhaseVertHalfSpan) return false;

            WorldToNodeLocal(in node, feetPos.X, feetPos.Y, feetPos.Z, out float lx, out float ly, out float lz);
            float localChestY = chestY - node.PosY;
            float localCrownY = crownY - node.PosY;

            // Merdiven boşluğu tavan geçiş oyuğu (StairwellTrimmerOpening):
            // Merdivenden üst kata çıkarken >= 1.95 m kafa boşluğu muafiyeti sağlar (çerçeve kenarına kafa takılmaz!)
            if (node.Role == StructuralElementRole.StairwellTrimmerOpening)
            {
                if (MathF.Abs(lx) <= 1.02f && MathF.Abs(lz) <= 1.02f &&
                    feetPos.Y >= node.PosY - StairwellMinClearHeadroomMeters)
                {
                    return false;
                }
            }

            // Ekol 4 kemer altı tavan karın eğrisi
            if (IsArchOrVaultRole(node.Role))
            {
                GetArchGeometryParams(in node, out float spanL, out _, out _, out _, out float topY, out float halfZ);
                if (MathF.Abs(lx) <= spanL * 0.5f + effR && MathF.Abs(lz) <= halfZ + effR)
                {
                    float intradosY = EvaluateArchIntradosLocalY(in node, lx);
                    if (localCrownY >= intradosY && localChestY < intradosY && ly < topY)
                    {
                        lowestCeilingY = node.PosY + intradosY;
                        return true;
                    }
                }
                return false;
            }

            // Eğimli çatı merteği
            if (node.Role == StructuralElementRole.InclinedRoofRafter ||
                node.Role == StructuralElementRole.RoofCoveringCourse)
            {
                if (TrySampleWalkableRampOrStairLocalY(in node, lx, lz, ly, 4.0f, effR, out float roofTopY, out float roofUnderY))
                {
                    if (localCrownY >= roofUnderY && localChestY < roofUnderY && ly < roofTopY)
                    {
                        lowestCeilingY = node.PosY + roofUnderY;
                        return true;
                    }
                }
                return false;
            }

            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[8];
            int boxCount = PopulateNodeLocalSolidBoxes(in node, boxes);
            bool found = false;
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (lx < box.Min.X - effR || lx > box.Max.X + effR ||
                    lz < box.Min.Z - effR || lz > box.Max.Z + effR)
                {
                    continue;
                }

                if (localCrownY >= box.Min.Y && localChestY < box.Min.Y)
                {
                    float worldCeilingY = node.PosY + box.Min.Y;
                    if (worldCeilingY < lowestCeilingY)
                    {
                        lowestCeilingY = worldCeilingY;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// <see cref="StructuralNode64"/> dizisi üzerinde yatay kapsül adımı engelleme testi (0-GC).
        /// </summary>
        public static bool IsHorizontalCapsuleStepBlocked(
            ReadOnlySpan<StructuralNode64> nodes,
            Vector3 curFeet,
            Vector3 nextFeet,
            float capsuleHeightMeters = 1.80f,
            float capsuleRadiusMeters = 0.34f,
            float maxStepUpMeters = 1.08f)
        {
            if (nodes.Length == 0) return false;

            float dx = nextFeet.X - curFeet.X;
            float dz = nextFeet.Z - curFeet.Z;
            float stepDistSq = dx * dx + dz * dz;
            if (stepDistSq < 1e-8f) return false;

            float r = Math.Clamp(capsuleRadiusMeters, 0.20f, 0.36f);
            float h = Math.Clamp(capsuleHeightMeters, 0.45f, 1.90f);

            int subSteps = stepDistSq > (0.14f * 0.14f)
                ? Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(stepDistSq) / 0.14f), 2, 16)
                : 1;
            for (int s = 1; s <= subSteps; s++)
            {
                float frac = s / (float)subSteps;
                Vector3 probeFeet = new Vector3(
                    curFeet.X + dx * frac,
                    curFeet.Y,
                    curFeet.Z + dz * frac);

                for (int i = 0; i < nodes.Length; i++)
                {
                    ref readonly StructuralNode64 node = ref nodes[i];
                    bool exempt = IsHorizontalFloorDeckRole(node.Role) &&
                                  IsPointInsideStairwellOpening(nodes, probeFeet.X, node.PosY, probeFeet.Z);
                    if (IsNodeBlockingCapsule(in node, curFeet, probeFeet, h, r, maxStepUpMeters, exempt))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// <see cref="StructuralNode64"/> dizisi üzerinde yürünebilir taban/merdiven/iskele kotu örnekleme (0-GC).
        /// StairwellTrimmerOpening içindeki üst döşemeler muaf tutulur.
        /// </summary>
        public static float SampleSupportGroundY(
            ReadOnlySpan<StructuralNode64> nodes,
            float posX,
            float posZ,
            float refFeetY,
            float maxStepUpMeters = 1.08f,
            float footRadiusMeters = 0.24f)
        {
            if (nodes.Length == 0) return -30f;

            float bestY = -30f;
            float r = Math.Clamp(footRadiusMeters, 0.14f, 0.30f);

            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 node = ref nodes[i];
                bool exempt = IsHorizontalFloorDeckRole(node.Role) &&
                              IsPointInsideStairwellOpening(nodes, posX, node.PosY, posZ);
                float candidateY = SampleNodeSupportGroundY(in node, posX, posZ, refFeetY, maxStepUpMeters, r, exempt);
                if (candidateY > bestY)
                {
                    bestY = candidateY;
                }
            }

            return bestY;
        }

        /// <summary>
        /// <see cref="StructuralNode64"/> dizisi üzerinde tavan kafa çarpma denetimi (0-GC).
        /// StairwellTrimmerOpening oyuğu altındaki üst kat döşemesi &gt;= 1.95 m kafa boşluğu için muaf tutulur.
        /// </summary>
        public static bool TryResolveCeilingHeadBonk(
            ReadOnlySpan<StructuralNode64> nodes,
            ref Vector3 feetPos,
            float capsuleHeightMeters = 1.80f,
            float capsuleRadiusMeters = 0.30f)
        {
            if (nodes.Length == 0) return false;

            float effH = Math.Clamp(capsuleHeightMeters, 0.45f, 1.85f);
            float effR = Math.Clamp(capsuleRadiusMeters * 0.78f, 0.16f, 0.26f);
            float chestY = feetPos.Y + effH * 0.55f;
            float crownY = feetPos.Y + effH + 0.03f;

            bool bonked = false;
            float lowestCeilingY = float.MaxValue;

            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 node = ref nodes[i];
                bool exempt = IsHorizontalFloorDeckRole(node.Role) &&
                              IsPointInsideStairwellOpening(nodes, feetPos.X, node.PosY, feetPos.Z);
                if (TrySampleNodeCeilingY(in node, feetPos, effH, effR, chestY, crownY, exempt, out float worldCeilingY))
                {
                    if (worldCeilingY < lowestCeilingY)
                    {
                        lowestCeilingY = worldCeilingY;
                        bonked = true;
                    }
                }
            }

            if (bonked)
            {
                float maxAllowedFeetY = lowestCeilingY - effH - 0.025f;
                if (feetPos.Y > maxAllowedFeetY)
                {
                    feetPos.Y = MathF.Max(feetPos.Y - 0.35f, maxAllowedFeetY);
                }
            }

            return bonked;
        }

        /// <summary>
        /// <see cref="StructuralNode64"/> dizisi üzerinde ok/mermi veya görüş hattı ışın çarpışması (0-GC).
        /// Pencere boşluğu (<see cref="StructuralElementRole.WindowFrameOpening"/>) açıkken veya kepenksizken
        /// ok ve görüş ışınını geçirir; kapalı kepenk/kapı/duvar ışını engeller.
        /// </summary>
        public static bool Raycast(
            ReadOnlySpan<StructuralNode64> nodes,
            Vector3 rayOrigin,
            Vector3 rayEnd,
            out StructureRayHit bestHit,
            StructureRayMask mask = StructureRayMask.Projectile)
        {
            bestHit = default;
            Vector3 delta = rayEnd - rayOrigin;
            float totalLen = delta.Length();
            if (totalLen < 1e-5f || nodes.Length == 0) return false;

            float bestT = float.MaxValue;
            uint hitNodeId = 0u;
            StructuralElementRole hitRole = default;
            bool hitAny = false;

            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly StructuralNode64 node = ref nodes[i];
                if (TryRaycastNode(in node, rayOrigin, rayEnd, mask, out float t01) && t01 < bestT)
                {
                    bestT = t01;
                    hitNodeId = node.NodeId;
                    hitRole = node.Role;
                    hitAny = true;
                }
            }

            if (!hitAny) return false;

            bestHit = new StructureRayHit(
                hit: true,
                distanceMeters: bestT * totalLen,
                point: rayOrigin + delta * bestT,
                nodeId: hitNodeId,
                role: hitRole);
            return true;
        }

        internal static bool TryRaycastNode(
            in StructuralNode64 node,
            Vector3 rayOrigin,
            Vector3 rayEnd,
            StructureRayMask mask,
            out float hitT01)
        {
            hitT01 = float.MaxValue;
            if (!IsNodeActiveForCollision(in node)) return false;

            WorldToNodeLocal(in node, rayOrigin.X, rayOrigin.Y, rayOrigin.Z, out float ox, out float oy, out float oz);
            WorldToNodeLocal(in node, rayEnd.X, rayEnd.Y, rayEnd.Z, out float ex, out float ey, out float ez);

            Vector3 localO = new Vector3(ox, oy, oz);
            Vector3 localD = new Vector3(ex - ox, ey - oy, ez - oz);
            bool hit = false;

            // Pencere kapalı kepenk/cam denetimi
            if (node.Role == StructuralElementRole.WindowFrameOpening)
            {
                bool hasClosedShutter = node.IsLocked && !node.IsDoorOrWindowOpen;
                bool hasClosedGlass = node.MaterialId == MaterialIdCatalog.LeadedGlassPane && !node.IsDoorOrWindowOpen;
                bool blocksCenterAperture = mask switch
                {
                    StructureRayMask.LineOfSight => hasClosedShutter, // Cam görüş hattını geçirir, kapalı kepenk keser
                    StructureRayMask.Projectile => hasClosedShutter || hasClosedGlass, // Açık pencere / mazgal oku geçirir!
                    _ => hasClosedShutter || hasClosedGlass
                };

                if (blocksCenterAperture)
                {
                    var paneBox = new LocalSolidBox(
                        new Vector3(-WindowApertureHalfWidth, WindowSillTopY, -0.04f),
                        new Vector3(WindowApertureHalfWidth, WindowLintelBottomY, 0.04f),
                        isWalkableTop: false);
                    if (TryIntersectSegmentAabb(localO, localD, paneBox.Min, paneBox.Max, out float paneT) && paneT < hitT01)
                    {
                        hitT01 = paneT;
                        hit = true;
                    }
                }
            }

            // Kemer ayakları ve üst spandrel
            if (IsArchOrVaultRole(node.Role))
            {
                GetArchGeometryParams(in node, out float spanL, out float pierW, out _, out _, out float topY, out float halfZ);
                float halfSpan = spanL * 0.5f;
                if (TryIntersectSegmentAabb(localO, localD, new Vector3(-halfSpan - pierW, 0f, -halfZ), new Vector3(-halfSpan, topY, halfZ), out float tL) && tL < hitT01)
                {
                    hitT01 = tL;
                    hit = true;
                }
                if (TryIntersectSegmentAabb(localO, localD, new Vector3(halfSpan, 0f, -halfZ), new Vector3(halfSpan + pierW, topY, halfZ), out float tR) && tR < hitT01)
                {
                    hitT01 = tR;
                    hit = true;
                }
                if (TryIntersectSegmentAabb(localO, localD, new Vector3(-halfSpan, 0f, -halfZ), new Vector3(halfSpan, topY, halfZ), out float tMid))
                {
                    Vector3 p = localO + localD * tMid;
                    float intradosY = EvaluateArchIntradosLocalY(in node, p.X);
                    if (p.Y >= intradosY && tMid < hitT01)
                    {
                        hitT01 = tMid;
                        hit = true;
                    }
                }
                return hit;
            }

            // Demir Mazgal (IronGrating) ok ve görüş hattını geçirir!
            if (node.MaterialId == MaterialIdCatalog.IronGrating &&
                (mask == StructureRayMask.Projectile || mask == StructureRayMask.LineOfSight))
            {
                return false;
            }

            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[8];
            int boxCount = PopulateNodeLocalSolidBoxes(in node, boxes);
            for (int b = 0; b < boxCount; b++)
            {
                ref readonly LocalSolidBox box = ref boxes[b];
                if (TryIntersectSegmentAabb(localO, localD, box.Min, box.Max, out float t) && t < hitT01)
                {
                    hitT01 = t;
                    hit = true;
                }
            }

            return hit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ComputeNodeWorldFootprintXZ(
            in StructuralNode64 node,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            float halfExtent = 1.25f;
            if (IsArchOrVaultRole(node.Role))
            {
                GetArchGeometryParams(in node, out float spanL, out float pierW, out _, out _, out _, out _);
                halfExtent = (spanL * 0.5f) + pierW + 0.15f;
            }
            else if (node.Role == StructuralElementRole.DoorFrameOpening &&
                     ResolveDoorKind(in node) == StructureDoorKind.DoubleLeafGate)
            {
                halfExtent = 2.15f;
            }
            else if (node.Role == StructuralElementRole.StairStringerFlight)
            {
                halfExtent = 1.50f;
            }
            else if (node.Role == StructuralElementRole.IronTieRod)
            {
                halfExtent = 1.75f;
            }

            minX = node.PosX - halfExtent;
            maxX = node.PosX + halfExtent;
            minZ = node.PosZ - halfExtent;
            maxZ = node.PosZ + halfExtent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryIntersectSegmentAabb(
            Vector3 origin,
            Vector3 delta,
            Vector3 boxMin,
            Vector3 boxMax,
            out float hitT01)
        {
            float tMin = 0.0f;
            float tMax = 1.0f;

            if (!ClipAxis(origin.X, delta.X, boxMin.X, boxMax.X, ref tMin, ref tMax) ||
                !ClipAxis(origin.Y, delta.Y, boxMin.Y, boxMax.Y, ref tMin, ref tMax) ||
                !ClipAxis(origin.Z, delta.Z, boxMin.Z, boxMax.Z, ref tMin, ref tMax))
            {
                hitT01 = float.MaxValue;
                return false;
            }

            hitT01 = tMin;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ClipAxis(float o, float d, float minB, float maxB, ref float tMin, ref float tMax)
        {
            if (MathF.Abs(d) < 1e-7f)
            {
                return o >= minB && o <= maxB;
            }

            float invD = 1.0f / d;
            float t1 = (minB - o) * invD;
            float t2 = (maxB - o) * invD;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ComputeHorizontalPushOut(
            Vector3 localPos,
            in LocalSolidBox box,
            float ox,
            float oz,
            float distSq,
            float r)
        {
            if (distSq > 1e-6f)
            {
                float dist = MathF.Sqrt(distSq);
                float pushMag = MathF.Min(0.24f, (r - dist) + 0.004f);
                return new Vector3((ox / dist) * pushMag, 0f, (oz / dist) * pushMag);
            }

            float dNegX = MathF.Abs(localPos.X - box.Min.X);
            float dPosX = MathF.Abs(box.Max.X - localPos.X);
            float dNegZ = MathF.Abs(localPos.Z - box.Min.Z);
            float dPosZ = MathF.Abs(box.Max.Z - localPos.Z);
            float minEdge = MathF.Min(MathF.Min(dNegX, dPosX), MathF.Min(dNegZ, dPosZ));
            Vector3 localPush;
            if (minEdge == dNegZ) localPush = new Vector3(0f, 0f, -(dNegZ + r + 0.005f));
            else if (minEdge == dPosZ) localPush = new Vector3(0f, 0f, dPosZ + r + 0.005f);
            else if (minEdge == dNegX) localPush = new Vector3(-(dNegX + r + 0.005f), 0f, 0f);
            else localPush = new Vector3(dPosX + r + 0.005f, 0f, 0f);

            float len = localPush.Length();
            if (len > 0.28f) localPush *= (0.28f / len);
            return localPush;
        }
    }

    /// <summary>
    /// DOK4 §5.4: 10.000+ parçalı şehir ölçeğinde O(1) kapsül ve ışın çarpışması sağlayan 0-GC düz dizili (CSR)
    /// 4m x 4m XZ mekânsal hash çarpışma çözücüsü.
    /// Hem <see cref="StructuralNode64"/> (Ekol 0..4 birleşik graf düğümleri) hem de <see cref="ModularPlacedPieceNode"/>
    /// parçalarını aynı 4m CSR ızgarasında indeksler ve kararlı-durum sorgularında 0 B/kare GC üretir.
    /// </summary>
    public sealed class SpatialHashedStructureCollisionSolver : ISolidStructureCollisionProvider
    {
        public const int CellShiftXZ = 2; // 2^2 = 4.0m hücre boyutu (Chunk başına 4x4 = 16 hücre)
        public const float CellSizeMeters = 4.0f;
        private const long EmptyCellKey = long.MinValue;

        private StructuralNode64[] _unifiedNodes;
        private int _unifiedCount;

        private ModularPlacedPieceNode[] _modularNodes;
        private int _modularCount;

        // Açık-adreslemeli hücre tablosu + CSR düz düğüm indeks dizisi
        private long[] _bucketKeys;
        private int[] _bucketOffsets;
        private int[] _bucketCounts;
        private int[] _bucketCursors;
        private int _bucketMask;
        private int _occupiedCellCount;

        // Pozitif: unified node indeksi (0.._unifiedCount-1); Negatif: ~modularIdx
        private int[] _csrNodeIndices;
        private int _csrTotalEntries;

        // 0-GC çoklu hücre tekrarı eleme damgası (Array.Clear maliyeti yok!)
        private uint[] _unifiedVisitStamps;
        private uint[] _modularVisitStamps;
        private uint _queryStamp = 1u;

        // O(1) NodeId -> indeks araması için açık-adreslemeli tablo (kapı aç/kapa için rebuild gerektirmez)
        private uint[] _idLookupKeys;
        private int[] _idLookupIndices;
        private int _idLookupMask;

        // Merdiven boşluğu hızlı indeksi (StairwellTrimmerOpening düğüm indeksleri)
        private int[] _stairwellOpeningIndices;
        private int _stairwellOpeningCount;

        private static SpatialHashedStructureCollisionSolver? s_sharedInstance;

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        public static void ResetSharedSolverOnSubsystemRegistration()
        {
            s_sharedInstance?.Clear();
            s_sharedInstance = null;
        }

        public static SpatialHashedStructureCollisionSolver Shared =>
            s_sharedInstance ??= new SpatialHashedStructureCollisionSolver();

        public int UnifiedNodeCount => _unifiedCount;
        public int ModularPieceCount => _modularCount;
        public int TotalIndexedNodeCount => _unifiedCount + _modularCount;
        public int OccupiedCellCount => _occupiedCellCount;
        public int LastQueryVisitedNodeCount { get; private set; }

        public SpatialHashedStructureCollisionSolver(int initialNodeCapacity = 256)
        {
            int cap = Math.Max(16, initialNodeCapacity);
            _unifiedNodes = new StructuralNode64[cap];
            _modularNodes = new ModularPlacedPieceNode[16];
            _unifiedVisitStamps = new uint[cap];
            _modularVisitStamps = new uint[16];
            _stairwellOpeningIndices = new int[16];

            int bucketCap = NextPowerOfTwo(Math.Max(64, cap));
            _bucketKeys = new long[bucketCap];
            _bucketOffsets = new int[bucketCap];
            _bucketCounts = new int[bucketCap];
            _bucketCursors = new int[bucketCap];
            _bucketMask = bucketCap - 1;
            Array.Fill(_bucketKeys, EmptyCellKey);

            _csrNodeIndices = new int[cap * 2];

            int idCap = NextPowerOfTwo(Math.Max(64, cap * 2));
            _idLookupKeys = new uint[idCap];
            _idLookupIndices = new int[idCap];
            _idLookupMask = idCap - 1;
            Array.Fill(_idLookupIndices, -1);
        }

        public void Clear()
        {
            _unifiedCount = 0;
            _modularCount = 0;
            _occupiedCellCount = 0;
            _csrTotalEntries = 0;
            _stairwellOpeningCount = 0;
            LastQueryVisitedNodeCount = 0;
            Array.Fill(_bucketKeys, EmptyCellKey);
            Array.Fill(_idLookupIndices, -1);
        }

        /// <summary>
        /// Yalnızca <see cref="StructuralNode64"/> düğümlerinden 4m x 4m CSR mekânsal hash ızgarasını yeniden kurar.
        /// Yeterli kapasite ısındıktan sonra 0 B GC ile çalışır.
        /// </summary>
        public void Rebuild(ReadOnlySpan<StructuralNode64> unifiedNodes)
        {
            Rebuild(unifiedNodes, ReadOnlySpan<ModularPlacedPieceNode>.Empty);
        }

        /// <summary>
        /// Yalnızca <see cref="ModularPlacedPieceNode"/> parçalarından 4m x 4m CSR mekânsal hash ızgarasını yeniden kurar.
        /// </summary>
        public void Rebuild(ReadOnlySpan<ModularPlacedPieceNode> modularPieces)
        {
            Rebuild(ReadOnlySpan<StructuralNode64>.Empty, modularPieces);
        }

        /// <summary>
        /// Hem <see cref="StructuralNode64"/> hem <see cref="ModularPlacedPieceNode"/> verilerini tek 4m x 4m CSR ızgarasında indeksler.
        /// </summary>
        public void Rebuild(
            ReadOnlySpan<StructuralNode64> unifiedNodes,
            ReadOnlySpan<ModularPlacedPieceNode> modularPieces)
        {
            _unifiedCount = unifiedNodes.Length;
            _modularCount = modularPieces.Length;
            _stairwellOpeningCount = 0;
            _occupiedCellCount = 0;
            _csrTotalEntries = 0;

            if (_unifiedCount > _unifiedNodes.Length)
            {
                int newCap = NextPowerOfTwo(_unifiedCount);
                _unifiedNodes = new StructuralNode64[newCap];
                _unifiedVisitStamps = new uint[newCap];
            }
            if (_modularCount > _modularNodes.Length)
            {
                int newCap = NextPowerOfTwo(_modularCount);
                _modularNodes = new ModularPlacedPieceNode[newCap];
                _modularVisitStamps = new uint[newCap];
            }

            if (_unifiedCount > 0) unifiedNodes.CopyTo(_unifiedNodes);
            if (_modularCount > 0) modularPieces.CopyTo(_modularNodes);

            int totalNodes = _unifiedCount + _modularCount;
            int requiredBuckets = NextPowerOfTwo(Math.Max(64, totalNodes * 3));
            if (requiredBuckets > _bucketKeys.Length)
            {
                _bucketKeys = new long[requiredBuckets];
                _bucketOffsets = new int[requiredBuckets];
                _bucketCounts = new int[requiredBuckets];
                _bucketCursors = new int[requiredBuckets];
                _bucketMask = requiredBuckets - 1;
            }
            Array.Fill(_bucketKeys, EmptyCellKey);

            int requiredIdSlots = NextPowerOfTwo(Math.Max(64, _unifiedCount * 2));
            if (requiredIdSlots > _idLookupKeys.Length)
            {
                _idLookupKeys = new uint[requiredIdSlots];
                _idLookupIndices = new int[requiredIdSlots];
                _idLookupMask = requiredIdSlots - 1;
            }
            Array.Fill(_idLookupIndices, -1);

            if (totalNodes == 0) return;

            // Geçiş 1: Hücre başına düşen düğüm sayılarını say + Stairwell açılımı & NodeId tablosu
            for (int i = 0; i < _unifiedCount; i++)
            {
                ref readonly StructuralNode64 n = ref _unifiedNodes[i];
                if (n.NodeId != 0u)
                {
                    InsertIdLookup(n.NodeId, i);
                }

                if (!ModularStructureCollisionSolver.IsNodeActiveForCollision(in n)) continue;

                if (n.Role == StructuralElementRole.StairwellTrimmerOpening)
                {
                    if (_stairwellOpeningCount >= _stairwellOpeningIndices.Length)
                    {
                        Array.Resize(ref _stairwellOpeningIndices, _stairwellOpeningIndices.Length * 2);
                    }
                    _stairwellOpeningIndices[_stairwellOpeningCount++] = i;
                }

                ModularStructureCollisionSolver.ComputeNodeWorldFootprintXZ(in n, out float minX, out float maxX, out float minZ, out float maxZ);
                CountFootprintCells(minX, maxX, minZ, maxZ);
            }

            for (int i = 0; i < _modularCount; i++)
            {
                ref readonly ModularPlacedPieceNode p = ref _modularNodes[i];
                if (p.IsBroken || p.IsFalling) continue;
                CountFootprintCells(p.Position.X - 1.35f, p.Position.X + 1.35f, p.Position.Z - 1.35f, p.Position.Z + 1.35f);
            }

            // CSR prefix-sum
            int runningOffset = 0;
            for (int b = 0; b < _bucketKeys.Length; b++)
            {
                if (_bucketKeys[b] == EmptyCellKey) continue;
                _bucketOffsets[b] = runningOffset;
                _bucketCursors[b] = runningOffset;
                runningOffset += _bucketCounts[b];
            }

            _csrTotalEntries = runningOffset;
            if (_csrTotalEntries > _csrNodeIndices.Length)
            {
                _csrNodeIndices = new int[NextPowerOfTwo(_csrTotalEntries)];
            }

            // Geçiş 2: Düz CSR dizisine düğüm indekslerini doldur
            for (int i = 0; i < _unifiedCount; i++)
            {
                ref readonly StructuralNode64 n = ref _unifiedNodes[i];
                if (!ModularStructureCollisionSolver.IsNodeActiveForCollision(in n)) continue;
                ModularStructureCollisionSolver.ComputeNodeWorldFootprintXZ(in n, out float minX, out float maxX, out float minZ, out float maxZ);
                ScatterFootprintCells(minX, maxX, minZ, maxZ, i);
            }

            for (int i = 0; i < _modularCount; i++)
            {
                ref readonly ModularPlacedPieceNode p = ref _modularNodes[i];
                if (p.IsBroken || p.IsFalling) continue;
                ScatterFootprintCells(p.Position.X - 1.35f, p.Position.X + 1.35f, p.Position.Z - 1.35f, p.Position.Z + 1.35f, ~i);
            }
        }

        /// <summary>
        /// İndekslenmiş bir kapı veya pencere düğümünü [E] ile açar/kapatır (O(1), 0-GC, yeniden inşa gerektirmez).
        /// </summary>
        public bool TryInteractToggleDoorOrWindow(uint nodeId, bool isAuthorized = false)
        {
            int idx = FindUnifiedNodeIndex(nodeId);
            if (idx < 0) return false;
            return ModularStructureCollisionSolver.TryInteractToggleDoorOrWindow(ref _unifiedNodes[idx], isAuthorized);
        }

        /// <summary>
        /// İndekslenmiş bir kapı veya pencere düğümünün kilidini [Shift+E] ile değiştirir (O(1), 0-GC).
        /// </summary>
        public bool TryToggleDoorOrWindowLock(uint nodeId, bool canOperateInteriorBolt)
        {
            int idx = FindUnifiedNodeIndex(nodeId);
            if (idx < 0) return false;
            return ModularStructureCollisionSolver.TryToggleDoorOrWindowLock(ref _unifiedNodes[idx], canOperateInteriorBolt);
        }

        /// <summary>
        /// İndekslenmiş düğümün anlık durumunu okur (O(1)).
        /// </summary>
        public bool TryGetUnifiedNode(uint nodeId, out StructuralNode64 node)
        {
            int idx = FindUnifiedNodeIndex(nodeId);
            if (idx < 0)
            {
                node = default;
                return false;
            }
            node = _unifiedNodes[idx];
            return true;
        }

        // =====================================================================
        // ISolidStructureCollisionProvider & GENISLETILMIS O(1) SORGULAR (0-GC)
        // =====================================================================

        public bool IsCapsuleStepBlockedByStructures(
            Vector3 curFeet,
            Vector3 nextFeet,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters)
        {
            LastQueryVisitedNodeCount = 0;
            if (_csrTotalEntries == 0) return false;

            float dx = nextFeet.X - curFeet.X;
            float dz = nextFeet.Z - curFeet.Z;
            float stepDistSq = dx * dx + dz * dz;
            if (stepDistSq < 1e-8f) return false;

            float r = Math.Clamp(capsuleRadius, 0.20f, 0.36f);
            float h = Math.Clamp(capsuleHeight, 0.45f, 1.90f);

            int subSteps = stepDistSq > (0.14f * 0.14f)
                ? Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(stepDistSq) / 0.14f), 2, 16)
                : 1;

            int visited = 0;
            for (int s = 1; s <= subSteps; s++)
            {
                float frac = s / (float)subSteps;
                Vector3 probeFeet = new Vector3(
                    curFeet.X + dx * frac,
                    curFeet.Y,
                    curFeet.Z + dz * frac);

                uint stamp = NextQueryStamp();
                GetCellRange(probeFeet.X - r, probeFeet.X + r, probeFeet.Z - r, probeFeet.Z + r,
                    out int minCx, out int maxCx, out int minCz, out int maxCz);

                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    for (int cx = minCx; cx <= maxCx; cx++)
                    {
                        int slot = FindBucketSlot(PackCellKey(cx, cz));
                        if (slot < 0) continue;

                        int start = _bucketOffsets[slot];
                        int end = start + _bucketCounts[slot];
                        for (int k = start; k < end; k++)
                        {
                            int encoded = _csrNodeIndices[k];
                            if (encoded >= 0)
                            {
                                if (_unifiedVisitStamps[encoded] == stamp) continue;
                                _unifiedVisitStamps[encoded] = stamp;
                                visited++;

                                ref readonly StructuralNode64 node = ref _unifiedNodes[encoded];
                                bool exempt = ModularStructureCollisionSolver.IsHorizontalFloorDeckRole(node.Role) &&
                                              IsPointInsideIndexedStairwell(probeFeet.X, node.PosY, probeFeet.Z);
                                if (ModularStructureCollisionSolver.IsNodeBlockingCapsule(in node, curFeet, probeFeet, h, r, maxStepUpMeters, exempt))
                                {
                                    LastQueryVisitedNodeCount = visited;
                                    return true;
                                }
                            }
                            else
                            {
                                int modIdx = ~encoded;
                                if (_modularVisitStamps[modIdx] == stamp) continue;
                                _modularVisitStamps[modIdx] = stamp;
                                visited++;

                                if (ModularStructureCollisionSolver.IsModularPieceBlockingCapsule(in _modularNodes[modIdx], curFeet, probeFeet, h, r, maxStepUpMeters))
                                {
                                    LastQueryVisitedNodeCount = visited;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            LastQueryVisitedNodeCount = visited;
            return false;
        }

        public bool ResolveHorizontalDepenetrationFromStructures(
            ref float posX,
            float posY,
            ref float posZ,
            float capsuleHeight,
            float capsuleRadius,
            float maxStepUpMeters)
        {
            LastQueryVisitedNodeCount = 0;
            if (_csrTotalEntries == 0) return false;

            float r = Math.Clamp(capsuleRadius, 0.20f, 0.36f);
            float h = Math.Clamp(capsuleHeight, 0.45f, 1.90f);
            bool movedAny = false;
            int visited = 0;

            for (int iter = 0; iter < 3; iter++)
            {
                bool movedInPass = false;
                uint stamp = NextQueryStamp();
                GetCellRange(posX - r, posX + r, posZ - r, posZ + r,
                    out int minCx, out int maxCx, out int minCz, out int maxCz);

                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    for (int cx = minCx; cx <= maxCx; cx++)
                    {
                        int slot = FindBucketSlot(PackCellKey(cx, cz));
                        if (slot < 0) continue;

                        int start = _bucketOffsets[slot];
                        int end = start + _bucketCounts[slot];
                        for (int k = start; k < end; k++)
                        {
                            int encoded = _csrNodeIndices[k];
                            if (encoded >= 0)
                            {
                                if (_unifiedVisitStamps[encoded] == stamp) continue;
                                _unifiedVisitStamps[encoded] = stamp;
                                visited++;

                                ref readonly StructuralNode64 node = ref _unifiedNodes[encoded];
                                bool exempt = ModularStructureCollisionSolver.IsHorizontalFloorDeckRole(node.Role) &&
                                              IsPointInsideIndexedStairwell(posX, node.PosY, posZ);
                                if (ModularStructureCollisionSolver.ResolveNodeHorizontalDepenetration(in node, ref posX, posY, ref posZ, h, r, maxStepUpMeters, exempt))
                                {
                                    movedInPass = true;
                                    movedAny = true;
                                }
                            }
                            else
                            {
                                int modIdx = ~encoded;
                                if (_modularVisitStamps[modIdx] == stamp) continue;
                                _modularVisitStamps[modIdx] = stamp;
                                visited++;

                                if (ModularStructureCollisionSolver.ResolveModularPieceDepenetration(in _modularNodes[modIdx], ref posX, posY, ref posZ, h, r, maxStepUpMeters))
                                {
                                    movedInPass = true;
                                    movedAny = true;
                                }
                            }
                        }
                    }
                }

                if (!movedInPass) break;
            }

            LastQueryVisitedNodeCount = visited;
            return movedAny;
        }

        public float SampleWalkableStructureTopY(
            float posX,
            float posY,
            float posZ,
            float footRadius,
            float maxStepUpMeters)
        {
            LastQueryVisitedNodeCount = 0;
            if (_csrTotalEntries == 0) return -30f;

            float bestY = -30f;
            float r = Math.Clamp(footRadius, 0.14f, 0.30f);
            float maxReachY = posY + maxStepUpMeters;
            uint stamp = NextQueryStamp();
            int visited = 0;

            GetCellRange(posX - r, posX + r, posZ - r, posZ + r,
                out int minCx, out int maxCx, out int minCz, out int maxCz);

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int slot = FindBucketSlot(PackCellKey(cx, cz));
                    if (slot < 0) continue;

                    int start = _bucketOffsets[slot];
                    int end = start + _bucketCounts[slot];
                    for (int k = start; k < end; k++)
                    {
                        int encoded = _csrNodeIndices[k];
                        if (encoded >= 0)
                        {
                            if (_unifiedVisitStamps[encoded] == stamp) continue;
                            _unifiedVisitStamps[encoded] = stamp;
                            visited++;

                            ref readonly StructuralNode64 node = ref _unifiedNodes[encoded];
                            bool exempt = ModularStructureCollisionSolver.IsHorizontalFloorDeckRole(node.Role) &&
                                          IsPointInsideIndexedStairwell(posX, node.PosY, posZ);
                            float candidateY = ModularStructureCollisionSolver.SampleNodeSupportGroundY(in node, posX, posZ, posY, maxStepUpMeters, r, exempt);
                            if (candidateY > bestY) bestY = candidateY;
                        }
                        else
                        {
                            int modIdx = ~encoded;
                            if (_modularVisitStamps[modIdx] == stamp) continue;
                            _modularVisitStamps[modIdx] = stamp;
                            visited++;

                            float candidateY = ModularStructureCollisionSolver.SampleModularPieceSupportGroundY(in _modularNodes[modIdx], posX, posZ, posY, maxReachY, r);
                            if (candidateY > bestY) bestY = candidateY;
                        }
                    }
                }
            }

            LastQueryVisitedNodeCount = visited;
            return bestY;
        }

        /// <summary>
        /// O(1) CSR sorgusuyla tavan kafa çarpmasını çözer (StairwellTrimmerOpening muafiyetli, 0-GC).
        /// </summary>
        public bool TryResolveCeilingHeadBonk(
            ref Vector3 feetPos,
            float capsuleHeightMeters = 1.80f,
            float capsuleRadiusMeters = 0.30f)
        {
            LastQueryVisitedNodeCount = 0;
            if (_csrTotalEntries == 0) return false;

            float effH = Math.Clamp(capsuleHeightMeters, 0.45f, 1.85f);
            float effR = Math.Clamp(capsuleRadiusMeters * 0.78f, 0.16f, 0.26f);
            float chestY = feetPos.Y + effH * 0.55f;
            float crownY = feetPos.Y + effH + 0.03f;

            bool bonked = false;
            float lowestCeilingY = float.MaxValue;
            uint stamp = NextQueryStamp();
            int visited = 0;

            GetCellRange(feetPos.X - effR, feetPos.X + effR, feetPos.Z - effR, feetPos.Z + effR,
                out int minCx, out int maxCx, out int minCz, out int maxCz);

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int slot = FindBucketSlot(PackCellKey(cx, cz));
                    if (slot < 0) continue;

                    int start = _bucketOffsets[slot];
                    int end = start + _bucketCounts[slot];
                    for (int k = start; k < end; k++)
                    {
                        int encoded = _csrNodeIndices[k];
                        if (encoded >= 0)
                        {
                            if (_unifiedVisitStamps[encoded] == stamp) continue;
                            _unifiedVisitStamps[encoded] = stamp;
                            visited++;

                            ref readonly StructuralNode64 node = ref _unifiedNodes[encoded];
                            bool exempt = ModularStructureCollisionSolver.IsHorizontalFloorDeckRole(node.Role) &&
                                          IsPointInsideIndexedStairwell(feetPos.X, node.PosY, feetPos.Z);
                            if (ModularStructureCollisionSolver.TrySampleNodeCeilingY(in node, feetPos, effH, effR, chestY, crownY, exempt, out float worldCeilingY) &&
                                worldCeilingY < lowestCeilingY)
                            {
                                lowestCeilingY = worldCeilingY;
                                bonked = true;
                            }
                        }
                        else
                        {
                            int modIdx = ~encoded;
                            if (_modularVisitStamps[modIdx] == stamp) continue;
                            _modularVisitStamps[modIdx] = stamp;
                            visited++;

                            if (ModularStructureCollisionSolver.TrySampleModularPieceCeilingY(in _modularNodes[modIdx], feetPos, effH, effR, chestY, crownY, out float worldCeilingY) &&
                                worldCeilingY < lowestCeilingY)
                            {
                                lowestCeilingY = worldCeilingY;
                                bonked = true;
                            }
                        }
                    }
                }
            }

            LastQueryVisitedNodeCount = visited;
            if (bonked)
            {
                float maxAllowedFeetY = lowestCeilingY - effH - 0.025f;
                if (feetPos.Y > maxAllowedFeetY)
                {
                    feetPos.Y = MathF.Max(feetPos.Y - 0.35f, maxAllowedFeetY);
                }
            }

            return bonked;
        }

        /// <summary>
        /// O(1) CSR ızgarası üzerinden ok / mermi veya görüş hattı ışın testi (0-GC).
        /// </summary>
        public bool Raycast(
            Vector3 rayOrigin,
            Vector3 rayEnd,
            out StructureRayHit bestHit,
            StructureRayMask mask = StructureRayMask.Projectile)
        {
            bestHit = default;
            LastQueryVisitedNodeCount = 0;
            Vector3 delta = rayEnd - rayOrigin;
            float totalLen = delta.Length();
            if (_csrTotalEntries == 0 || totalLen < 1e-5f) return false;

            float minX = MathF.Min(rayOrigin.X, rayEnd.X);
            float maxX = MathF.Max(rayOrigin.X, rayEnd.X);
            float minZ = MathF.Min(rayOrigin.Z, rayEnd.Z);
            float maxZ = MathF.Max(rayOrigin.Z, rayEnd.Z);

            GetCellRange(minX, maxX, minZ, maxZ, out int minCx, out int maxCx, out int minCz, out int maxCz);
            uint stamp = NextQueryStamp();
            int visited = 0;
            float bestT = float.MaxValue;
            uint hitNodeId = 0u;
            StructuralElementRole hitRole = default;
            bool hitAny = false;

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int slot = FindBucketSlot(PackCellKey(cx, cz));
                    if (slot < 0) continue;

                    int start = _bucketOffsets[slot];
                    int end = start + _bucketCounts[slot];
                    for (int k = start; k < end; k++)
                    {
                        int encoded = _csrNodeIndices[k];
                        if (encoded < 0) continue;
                        if (_unifiedVisitStamps[encoded] == stamp) continue;
                        _unifiedVisitStamps[encoded] = stamp;
                        visited++;

                        ref readonly StructuralNode64 node = ref _unifiedNodes[encoded];
                        if (ModularStructureCollisionSolver.TryRaycastNode(in node, rayOrigin, rayEnd, mask, out float t01) &&
                            t01 < bestT)
                        {
                            bestT = t01;
                            hitNodeId = node.NodeId;
                            hitRole = node.Role;
                            hitAny = true;
                        }
                    }
                }
            }

            LastQueryVisitedNodeCount = visited;
            if (!hitAny) return false;

            bestHit = new StructureRayHit(
                hit: true,
                distanceMeters: bestT * totalLen,
                point: rayOrigin + delta * bestT,
                nodeId: hitNodeId,
                role: hitRole);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsRayBlocked(Vector3 rayOrigin, Vector3 rayEnd, StructureRayMask mask = StructureRayMask.Projectile)
            => Raycast(rayOrigin, rayEnd, out _, mask);

        // =====================================================================
        // IC YARDIMCILAR (CSR & HASH TABLOSU)
        // =====================================================================

        private bool IsPointInsideIndexedStairwell(float worldX, float slabWorldY, float worldZ)
        {
            for (int i = 0; i < _stairwellOpeningCount; i++)
            {
                ref readonly StructuralNode64 openNode = ref _unifiedNodes[_stairwellOpeningIndices[i]];
                if (MathF.Abs(slabWorldY - openNode.PosY) > 0.45f) continue;

                ModularStructureCollisionSolver.WorldToNodeLocal(in openNode, worldX, slabWorldY, worldZ, out float lx, out _, out float lz);
                if (MathF.Abs(lx) <= 1.02f && MathF.Abs(lz) <= 1.02f)
                {
                    return true;
                }
            }

            return false;
        }

        private void CountFootprintCells(float minX, float maxX, float minZ, float maxZ)
        {
            GetCellRange(minX, maxX, minZ, maxZ, out int minCx, out int maxCx, out int minCz, out int maxCz);
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int slot = GetOrCreateBucketSlot(PackCellKey(cx, cz));
                    _bucketCounts[slot]++;
                }
            }
        }

        private void ScatterFootprintCells(float minX, float maxX, float minZ, float maxZ, int encodedIndex)
        {
            GetCellRange(minX, maxX, minZ, maxZ, out int minCx, out int maxCx, out int minCz, out int maxCz);
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int slot = FindBucketSlot(PackCellKey(cx, cz));
                    if (slot >= 0)
                    {
                        _csrNodeIndices[_bucketCursors[slot]++] = encodedIndex;
                    }
                }
            }
        }

        private int GetOrCreateBucketSlot(long cellKey)
        {
            // Yük faktörü %65'i geçerse yeniden boyutlandır
            if ((_occupiedCellCount + 1) * 3 >= _bucketKeys.Length * 2)
            {
                GrowBucketTable();
            }

            int idx = HashCellKey(cellKey) & _bucketMask;
            while (true)
            {
                long existing = _bucketKeys[idx];
                if (existing == cellKey) return idx;
                if (existing == EmptyCellKey)
                {
                    _bucketKeys[idx] = cellKey;
                    _bucketCounts[idx] = 0;
                    _bucketOffsets[idx] = 0;
                    _bucketCursors[idx] = 0;
                    _occupiedCellCount++;
                    return idx;
                }
                idx = (idx + 1) & _bucketMask;
            }
        }

        private void GrowBucketTable()
        {
            long[] oldKeys = _bucketKeys;
            int[] oldCounts = _bucketCounts;
            int newCap = oldKeys.Length * 2;
            _bucketKeys = new long[newCap];
            _bucketOffsets = new int[newCap];
            _bucketCounts = new int[newCap];
            _bucketCursors = new int[newCap];
            _bucketMask = newCap - 1;
            Array.Fill(_bucketKeys, EmptyCellKey);

            for (int i = 0; i < oldKeys.Length; i++)
            {
                long k = oldKeys[i];
                if (k == EmptyCellKey) continue;
                int slot = HashCellKey(k) & _bucketMask;
                while (_bucketKeys[slot] != EmptyCellKey)
                {
                    slot = (slot + 1) & _bucketMask;
                }
                _bucketKeys[slot] = k;
                _bucketCounts[slot] = oldCounts[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindBucketSlot(long cellKey)
        {
            int idx = HashCellKey(cellKey) & _bucketMask;
            while (true)
            {
                long existing = _bucketKeys[idx];
                if (existing == cellKey) return idx;
                if (existing == EmptyCellKey) return -1;
                idx = (idx + 1) & _bucketMask;
            }
        }

        private void InsertIdLookup(uint nodeId, int unifiedIdx)
        {
            int slot = (int)(MixHash32(nodeId) & (uint)_idLookupMask);
            while (_idLookupIndices[slot] != -1)
            {
                if (_idLookupKeys[slot] == nodeId)
                {
                    _idLookupIndices[slot] = unifiedIdx;
                    return;
                }
                slot = (slot + 1) & _idLookupMask;
            }
            _idLookupKeys[slot] = nodeId;
            _idLookupIndices[slot] = unifiedIdx;
        }

        private int FindUnifiedNodeIndex(uint nodeId)
        {
            if (nodeId == 0u || _unifiedCount == 0) return -1;
            int slot = (int)(MixHash32(nodeId) & (uint)_idLookupMask);
            while (true)
            {
                int idx = _idLookupIndices[slot];
                if (idx == -1) return -1;
                if (_idLookupKeys[slot] == nodeId) return idx;
                slot = (slot + 1) & _idLookupMask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint NextQueryStamp()
        {
            _queryStamp++;
            if (_queryStamp == 0u)
            {
                Array.Clear(_unifiedVisitStamps, 0, _unifiedVisitStamps.Length);
                Array.Clear(_modularVisitStamps, 0, _modularVisitStamps.Length);
                _queryStamp = 1u;
            }
            return _queryStamp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetCellRange(
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            out int minCx,
            out int maxCx,
            out int minCz,
            out int maxCz)
        {
            minCx = (int)MathF.Floor(minX) >> CellShiftXZ;
            maxCx = (int)MathF.Floor(maxX) >> CellShiftXZ;
            minCz = (int)MathF.Floor(minZ) >> CellShiftXZ;
            maxCz = (int)MathF.Floor(maxZ) >> CellShiftXZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long PackCellKey(int cx, int cz)
            => ((long)cx << 32) | (uint)cz;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashCellKey(long key)
        {
            ulong x = (ulong)key;
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb9fe1a85ec53UL;
            x ^= x >> 33;
            return (int)x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MixHash32(uint h)
        {
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return h;
        }

        private static int NextPowerOfTwo(int v)
        {
            int p = 16;
            while (p < v && p < (1 << 28)) p <<= 1;
            return p;
        }
    }
}
