#nullable enable
using System;
using System.Numerics;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// PT-05: Saf C# (0-GC, Unity-bagimsiz) 3B Moduler Yapi Kati Carpisma Cozucusu.
    /// Tum 13 moduler yapi parcasinin (duvar, pencere, kapi sovesi+lentosu, subasman, doseme,
    /// dikme, kiris, capraz payanda, 45 deg ucgen alinlik, 26/45 deg egimli cati ve mahya)
    /// tam yerel-uzay OBB / egimli duzlem geometrisini kullanarak:
    ///   - Yatay kapsul duvar engelleme (anti-tunneling alt-adimli),
    ///   - 2B MTV yatay depenetrasyon (ASLA Y isinlamasi yapmaz),
    ///   - Yurunebilir subasman / 1.-2. kat doseme / cati tabani yukseklik ornekleme,
    ///   - Tavan / kapi lentosu / cati alti bas carpma ve bas-ustu bosluk (headroom) denetimi saglar.
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

        private const float BroadPhaseHorizRadiusSq = 18.5f; // ~4.3m
        private const float BroadPhaseVertHalfSpan = 6.5f;
        private const float DefaultWallHalfThickness = 0.11f;
        private const float DoorwayClearHalfWidth = 0.46f;   // 0.92m acik kapi gecidi (oyuncu capi ~0.68m)
        private const float DoorwayLintelBottomY = 1.92f;    // PlankFloor (+0.08m) ustunden 1.84m net gecis (> 1.80m ayakta kapsul)
        private const float RoofSlabThickness = 0.22f;

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
                    // 45 deg capraz payanda: (+0.68, 0.06) -> (-0.68, 1.36) boyunca 3 siki kademe
                    outBoxes[0] = new LocalSolidBox(
                        new Vector3(0.22f, 0.00f, -0.10f),
                        new Vector3(0.76f, 0.52f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[1] = new LocalSolidBox(
                        new Vector3(-0.24f, 0.46f, -0.10f),
                        new Vector3(0.28f, 0.96f, 0.10f),
                        isWalkableTop: false);
                    outBoxes[2] = new LocalSolidBox(
                        new Vector3(-0.76f, 0.92f, -0.10f),
                        new Vector3(-0.20f, 1.44f, 0.10f),
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

            // Hizli kosu/dodge esnasinda ince duvarlardan tunelleme onlemi (0.12m uzeri adimlarda orta nokta da kontrol edilir)
            int subSteps = stepDistSq > (0.12f * 0.12f) ? 2 : 1;
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
            float bodyMinY = probeFeet.Y + 0.08f;
            float bodyMaxY = probeFeet.Y + MathF.Max(0.42f, capsuleHeight - 0.08f);
            float stepLimitY = curFeet.Y + maxStepUpMeters;
            float rSq = radius * radius;
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                if (!IsWithinBroadPhase(in piece, probeFeet.X, probeFeet.Y, probeFeet.Z)) continue;

                Quaternion invRot = Quaternion.Inverse(piece.Rotation);
                Vector3 localProbe = Vector3.Transform(probeFeet - piece.Position, invRot);
                float localBodyMinY = bodyMinY - piece.Position.Y;
                float localBodyMaxY = bodyMaxY - piece.Position.Y;
                float localStepLimitY = stepLimitY - piece.Position.Y;

                // 1) Egimli cati veya 45 deg ucgen alinlik ozel analitik denetimi
                if (IsSlopedRoof(piece.Kind, out _) || piece.Kind == ModularBuildPieceKind.GableWall45Deg2m)
                {
                    if (TryEvaluateLocalColumnSpan(piece.Kind, localProbe.X, localProbe.Z, radius, out float sMinY, out float sMaxY))
                    {
                        if (localBodyMaxY > sMinY && localBodyMinY < sMaxY)
                        {
                            // Yurunebilir alcak egim veya basamak mi?
                            bool walkableStep = IsSlopedRoof(piece.Kind, out _) && sMaxY <= localStepLimitY;
                            if (!walkableStep)
                            {
                                return true;
                            }
                        }
                    }
                    continue;
                }

                // 2) Standart ve coklu alt-kutulu (kapi sovesi+lentosu, duvar, pencere, dikme, temel, doseme) denetim
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

                    // Eger bu kutunun ust yuzeyi oyuncunun diz basamak sinirinin (1.08m) altindaysa (orn. StoneFoundation +0.32m
                    // veya PlankFloor +0.08m), engel degil ustune cikilabilir tabandir!
                    if (box.IsWalkableTop && box.Max.Y <= localStepLimitY)
                    {
                        continue;
                    }

                    return true;
                }
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
            float rSq = r * r;
            float h = Math.Clamp(capsuleHeightMeters, 0.45f, 1.90f);
            float bodyMinY = posY + 0.10f;
            float bodyMaxY = posY + MathF.Max(0.42f, h - 0.08f);
            float stepLimitY = posY + maxStepUpMeters;
            bool movedAny = false;
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            for (int iter = 0; iter < 3; iter++)
            {
                bool movedInPass = false;

                for (int i = 0; i < pieces.Length; i++)
                {
                    ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                    if (!IsWithinBroadPhase(in piece, posX, posY, posZ)) continue;

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
                            movedInPass = true;
                            movedAny = true;
                        }
                        continue;
                    }

                    if (IsSlopedRoof(piece.Kind, out _))
                    {
                        continue;
                    }

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

                        Vector3 localPush;
                        if (distSq > 1e-6f)
                        {
                            float dist = MathF.Sqrt(distSq);
                            float pushMag = MathF.Min(0.24f, (r - dist) + 0.004f);
                            localPush = new Vector3((ox / dist) * pushMag, 0f, (oz / dist) * pushMag);
                        }
                        else
                        {
                            // Kapsul merkezi kutunun tam icinde: en yakin kenardan disari it
                            float dNegX = MathF.Abs(localPos.X - box.Min.X);
                            float dPosX = MathF.Abs(box.Max.X - localPos.X);
                            float dNegZ = MathF.Abs(localPos.Z - box.Min.Z);
                            float dPosZ = MathF.Abs(box.Max.Z - localPos.Z);
                            float minEdge = MathF.Min(MathF.Min(dNegX, dPosX), MathF.Min(dNegZ, dPosZ));
                            if (minEdge == dNegZ) localPush = new Vector3(0f, 0f, -(dNegZ + r + 0.005f));
                            else if (minEdge == dPosZ) localPush = new Vector3(0f, 0f, dPosZ + r + 0.005f);
                            else if (minEdge == dNegX) localPush = new Vector3(-(dNegX + r + 0.005f), 0f, 0f);
                            else localPush = new Vector3(dPosX + r + 0.005f, 0f, 0f);

                            float len = localPush.Length();
                            if (len > 0.28f) localPush *= (0.28f / len);
                        }

                        Vector3 worldPush = Vector3.Transform(localPush, piece.Rotation);
                        posX += worldPush.X;
                        posZ += worldPush.Z;
                        localPos += localPush;
                        movedInPass = true;
                        movedAny = true;
                    }
                }

                if (!movedInPass) break;
            }

            return movedAny;
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
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                if (!IsWithinBroadPhase(in piece, posX, refFeetY, posZ)) continue;

                Quaternion invRot = Quaternion.Inverse(piece.Rotation);
                Vector3 localPos = Vector3.Transform(new Vector3(posX - piece.Position.X, 0f, posZ - piece.Position.Z), invRot);

                if (IsSlopedRoof(piece.Kind, out _))
                {
                    if (TryEvaluateLocalColumnSpan(piece.Kind, localPos.X, localPos.Z, r * 0.65f, out _, out float roofTopLocalY))
                    {
                        float worldTopY = piece.Position.Y + roofTopLocalY;
                        if (worldTopY <= maxReachY && worldTopY > bestY)
                        {
                            bestY = worldTopY;
                        }
                    }
                    continue;
                }

                if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m ||
                    piece.Kind == ModularBuildPieceKind.DiagonalBrace2m)
                {
                    continue;
                }

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
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            bool bonked = false;
            float lowestCeilingY = float.MaxValue;

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                if (!IsWithinBroadPhase(in piece, feetPos.X, feetPos.Y + effH * 0.5f, feetPos.Z)) continue;

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
                            float worldCeilingY = piece.Position.Y + sMinY;
                            if (worldCeilingY < lowestCeilingY)
                            {
                                lowestCeilingY = worldCeilingY;
                                bonked = true;
                            }
                        }
                    }
                    continue;
                }

                if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m) continue;

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
                            bonked = true;
                        }
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
            Span<LocalSolidBox> boxes = stackalloc LocalSolidBox[4];

            for (int i = 0; i < pieces.Length; i++)
            {
                ref readonly ModularPlacedPieceNode piece = ref pieces[i];
                if (!IsWithinBroadPhase(in piece, feetPos.X, feetPos.Y + 1.0f, feetPos.Z)) continue;

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
                    continue;
                }

                if (piece.Kind == ModularBuildPieceKind.GableWall45Deg2m) continue;

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

            if (!canCrouch115) canStand180 = false;
        }
    }
}
