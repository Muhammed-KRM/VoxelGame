#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// KM-4 / DOK4 Bolum 4.2: Pure C# XPBD Rigid Body Cozucusu (Müller et al. 2020 PBDBodies Algorithm 1+2).
    /// - IStructuralDynamicsSolver uygular (ileride AVBD 6x6 ile tak-cikar degistirilebilir — DIP/OCP).
    /// - N_sub = 12, N_iter = 1, dusuk hizda e = 0 (sifir mikro ziplama), 0 B GC.
    /// - Dogrudan IVoxelSoilSampler ile zemin carpismasi (NearPhysicsColliderRadius=-1 iken bile zeminden dusme %0!).
    /// - 0.6s uyuma histerezisi -> StaticLooseEntity (0.00 ms, voksele birlesmez!).
    /// </summary>
    public sealed class XpbdRigidTimberSolver : IStructuralDynamicsSolver
    {
        public const int DefaultSubsteps = 12;
        public const float SleepLinearThresholdMps = 0.10f;
        public const float SleepAngularThresholdRadPerSec = 0.15f;
        public const float SleepHoldDurationSec = 0.60f;
        public const float GroundDynamicFrictionMu = 0.55f;

        public string SolverName => "XPBD-RigidTimber-Sub12";
        public int Substeps { get; set; } = DefaultSubsteps;

        public int StepActiveDynamics(
            Span<RigidStructurePiece> pieces,
            Span<CornerBond4Interface> bonds,
            IVoxelSoilSampler soilSampler,
            float deltaTimeSec,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink = null)
        {
            if (pieces.Length == 0 || deltaTimeSec <= 1e-6f)
            {
                return 0;
            }

            float dt = Math.Clamp(deltaTimeSec, 1e-4f, 0.05f);
            int substeps = Math.Clamp(Substeps, 4, 20);
            float h = dt / substeps;
            Vector3 gravity = new Vector3(0f, -WoodSpeciesPhysicsLut.GravityMps2, 0f);

            int activeCount = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i].State == RigidTimberState.DynamicActive)
                {
                    activeCount++;
                }
            }
            if (activeCount == 0)
            {
                return 0;
            }

            // Stackalloc / yerel tampon (0 B GC — tipik aktif ada <= 256 parca)
            int n = pieces.Length;
            Vector3[] prevPos = GetScratchVec3(n, 0);
            Quaternion[] prevRot = GetScratchQuat(n);

            for (int step = 0; step < substeps; step++)
            {
                // 1) Explicit Prediction (Algorithm 1)
                for (int i = 0; i < n; i++)
                {
                    ref RigidStructurePiece p = ref pieces[i];
                    if (p.State != RigidTimberState.DynamicActive || p.InvMass <= 0f) continue;

                    prevPos[i] = p.Position;
                    prevRot[i] = p.Rotation;

                    p.LinearVelocity += gravity * h;
                    p.Position += p.LinearVelocity * h;

                    // Gyroscopic + kuaterniyon entegrasyonu
                    Quaternion wq = new Quaternion(p.AngularVelocity.X, p.AngularVelocity.Y, p.AngularVelocity.Z, 0f);
                    Quaternion dq = wq * p.Rotation;
                    p.Rotation = Quaternion.Normalize(new Quaternion(
                        p.Rotation.X + 0.5f * h * dq.X,
                        p.Rotation.Y + 0.5f * h * dq.Y,
                        p.Rotation.Z + 0.5f * h * dq.Z,
                        p.Rotation.W + 0.5f * h * dq.W));
                }

                // 2) 4-Kose Reduced-AEM Bag Kisitlari (N_iter = 1)
                for (int bIdx = 0; bIdx < bonds.Length; bIdx++)
                {
                    ref CornerBond4Interface bond = ref bonds[bIdx];
                    if (bond.IsActive == 0) continue;

                    int idxA = FindPieceIndex(pieces, bond.PieceA);
                    int idxB = FindPieceIndex(pieces, bond.PieceB);
                    if (idxA < 0 || idxB < 0) continue;
                    if (pieces[idxA].State != RigidTimberState.DynamicActive &&
                        pieces[idxB].State != RigidTimberState.DynamicActive)
                    {
                        continue;
                    }

                    SolveCornerBond4(ref pieces[idxA], ref pieces[idxB], ref bond, h);
                }

                // 2b) Hafif Parca-Parca Carpismasi (Yikilan odunlarin birbirinin icinden gecmesini onler, 0 B GC)
                if (n <= 180)
                {
                    SolvePieceToPieceContacts(pieces);
                }

                // 3) Voxel Zemin Carpismasi (Her aktif silindirin 2 ucu + orta noktasi)
                // ve 4) Velocity Derivation + Dusuk Hizda e=0 & Ziplama/Firlama Kalkanı (Algorithm 2)
                float invH = 1.0f / h;
                for (int i = 0; i < n; i++)
                {
                    ref RigidStructurePiece p = ref pieces[i];
                    if (p.State != RigidTimberState.DynamicActive || p.InvMass <= 0f) continue;

                    bool hadGroundHit = SolveGroundContactsForCylinder(ref p, soilSampler, h);

                    Vector3 linV = (p.Position - prevPos[i]) * invH;
                    Quaternion deltaQ = p.Rotation * Quaternion.Conjugate(prevRot[i]);
                    float sign = deltaQ.W < 0f ? -1f : 1f;
                    Vector3 angV = new Vector3(deltaQ.X, deltaQ.Y, deltaQ.Z) * (2.0f * sign * invH);

                    // Zemin temasinda sifir mikro-ziplama (e = 0) + eksenel yuvarlanma & yatay surtunme direnci
                    if (hadGroundHit)
                    {
                        if (linV.Y > 0.0f) linV.Y = 0.0f;
                        bool isBoxPanel = p.HalfExtents.X > p.DiameterM * 0.65f || p.HalfExtents.Z > p.DiameterM * 0.65f;
                        if (isBoxPanel)
                        {
                            if (MathF.Abs(linV.Y) < 0.45f) linV.Y = 0.0f;
                            if (linV.LengthSquared() < 0.25f) linV *= 0.75f;
                            angV *= (angV.LengthSquared() < 1.0f) ? 0.72f : 0.90f;
                        }
                        else
                        {
                            Vector3 axisWorld = Vector3.Transform(Vector3.UnitY, p.Rotation);
                            float wAxial = Vector3.Dot(angV, axisWorld);
                            angV -= axisWorld * (wAxial * 0.06f);
                            angV.Y *= 0.965f;
                        }
                    }

                    // Firlama / Baumgarte hiz tavani (gomulu sutunun uzaya firlamasini kesin engeller)
                    float linSpd = linV.Length();
                    if (linSpd > 16.0f) linV *= (16.0f / linSpd);
                    float angSpd = angV.Length();
                    if (angSpd > 7.0f) angV *= (7.0f / angSpd);

                    p.LinearVelocity = linV * 0.996f;
                    p.AngularVelocity = angV * 0.990f;
                }
            }

            // 5) Substep Sonu 4-Kose Kirilma Kontrolu + 0.6s Uyuma Histerezisi -> StaticLooseEntity
            for (int bIdx = 0; bIdx < bonds.Length; bIdx++)
            {
                ref CornerBond4Interface bond = ref bonds[bIdx];
                if (bond.IsActive == 0) continue;
                ReducedAemBondEvaluator.EvaluateAndFractureCorners(ref bond, h, islandId: 0, currentTick, traceSink);
            }

            int stillActive = 0;
            for (int i = 0; i < n; i++)
            {
                ref RigidStructurePiece p = ref pieces[i];
                if (p.State != RigidTimberState.DynamicActive) continue;

                // NaN / Inf guvenlik korumasi
                if (!float.IsFinite(p.Position.X) || !float.IsFinite(p.Position.Y) || !float.IsFinite(p.Position.Z))
                {
                    p.LinearVelocity = Vector3.Zero;
                    p.AngularVelocity = Vector3.Zero;
                    p.State = RigidTimberState.StaticLooseEntity;
                    continue;
                }

                float vMag = p.LinearVelocity.Length();
                float wMag = p.AngularVelocity.Length();
                float bottomClearance = MathF.Min(p.HalfExtents.Y, p.DiameterM * 0.55f);
                if (bottomClearance < 0.05f) bottomClearance = p.DiameterM * 0.55f;
                bool touchingGround = soilSampler.TrySampleGroundSurfaceY(p.Position.X, p.Position.Z, p.Position.Y + 1.0f, out float gy, out _)
                                      && (p.Position.Y - bottomClearance <= gy + 0.45f);

                if (touchingGround && vMag < SleepLinearThresholdMps && wMag < SleepAngularThresholdRadPerSec)
                {
                    p.SleepTimerSec += dt;
                    if (p.SleepTimerSec >= SleepHoldDurationSec)
                    {
                        p.LinearVelocity = Vector3.Zero;
                        p.AngularVelocity = Vector3.Zero;
                        p.State = RigidTimberState.StaticLooseEntity;
                        p.InvMass = 0f;

                        if (traceSink != null && traceSink.IsEnabled)
                        {
                            var ev = new RigidPhysicsTraceEvent
                            {
                                Tick = currentTick,
                                PieceId = p.PieceId,
                                IslandId = p.IslandId,
                                EventKind = RigidTimberTraceEventKind.PieceSleptLoose,
                                State = RigidTimberState.StaticLooseEntity,
                                DirX = p.Position.X,
                                DirY = p.Position.Y,
                                DirZ = p.Position.Z
                            };
                            traceSink.Record(in ev, "XpbdRigidTimberSolver.SleepPiece");
                        }
                        continue;
                    }
                }
                else
                {
                    p.SleepTimerSec = 0f;
                }

                stillActive++;
            }

            return stillActive;
        }

        private static void SolveCornerBond4(
            ref RigidStructurePiece a,
            ref RigidStructurePiece b,
            ref CornerBond4Interface bond,
            float h)
        {
            float alphaTilde = bond.ComplianceMPerN / MathF.Max(1e-8f, h * h);

            for (int c = 0; c < 4; c++)
            {
                if ((bond.CornerBrokenMask & (1 << c)) != 0) continue;

                Vector3 localA = c switch { 0 => bond.LocalA0, 1 => bond.LocalA1, 2 => bond.LocalA2, _ => bond.LocalA3 };
                Vector3 localB = c switch { 0 => bond.LocalB0, 1 => bond.LocalB1, 2 => bond.LocalB2, _ => bond.LocalB3 };

                Vector3 rA = Vector3.Transform(localA, a.Rotation);
                Vector3 rB = Vector3.Transform(localB, b.Rotation);
                Vector3 pA = a.Position + rA;
                Vector3 pB = b.Position + rB;

                Vector3 diff = pA - pB;
                float dist = diff.Length();
                if (dist < 1e-6f) continue;

                Vector3 n = diff / dist;
                float wA = ComputeGeneralizedInvMass(in a, rA, n);
                float wB = ComputeGeneralizedInvMass(in b, rB, n);
                float wSum = wA + wB + alphaTilde;
                if (wSum < 1e-10f) continue;

                float dLambda = -dist / wSum;
                Vector3 impulse = n * dLambda;

                ApplyPositionalCorrection(ref a, rA, impulse);
                ApplyPositionalCorrection(ref b, rB, -impulse);

                // Normal (dikey/ayrilma) ve teget bileseni Lagrange carpanina kaydet
                float lamN = dLambda * MathF.Abs(n.Y);
                float lamT = dLambda * MathF.Sqrt(MathF.Max(0f, n.X * n.X + n.Z * n.Z));
                SetLambdas(ref bond, c, -lamN, MathF.Abs(lamT));
            }
        }

        private static void SolvePieceToPieceContacts(Span<RigidStructurePiece> pieces)
        {
            int n = pieces.Length;
            for (int i = 0; i < n; i++)
            {
                if (pieces[i].State != RigidTimberState.DynamicActive || pieces[i].InvMass <= 0f) continue;
                int contactsForI = 0;
                Vector3 pi = pieces[i].Position;
                float ri = MathF.Max(0.14f, MathF.Min(pieces[i].DiameterM * 0.5f, 0.35f));

                for (int j = i + 1; j < n && contactsForI < 3; j++)
                {
                    if (pieces[j].State == RigidTimberState.StaticAnchored) continue;
                    Vector3 pj = pieces[j].Position;
                    float rj = MathF.Max(0.14f, MathF.Min(pieces[j].DiameterM * 0.5f, 0.35f));
                    float minDist = ri + rj;

                    Vector3 diff = pi - pj;
                    float distSq = diff.LengthSquared();
                    if (distSq <= 1e-6f || distSq >= minDist * minDist) continue;

                    float dist = MathF.Sqrt(distSq);
                    float pen = MathF.Min(minDist - dist, 0.045f);
                    Vector3 dir = diff / dist;
                    // Ustteki parcayi yukari iterek dogal yigilma olustur
                    if (dir.Y < 0.15f && MathF.Abs(pi.Y - pj.Y) < minDist * 0.6f)
                    {
                        dir = Vector3.Normalize(new Vector3(dir.X, 0.35f, dir.Z));
                    }

                    float wSum = pieces[i].InvMass + pieces[j].InvMass;
                    if (wSum <= 1e-8f) continue;

                    float shareI = pieces[i].InvMass / wSum;
                    float shareJ = pieces[j].InvMass / wSum;
                    pieces[i].Position += dir * (pen * shareI);
                    if (pieces[j].State == RigidTimberState.DynamicActive && shareJ > 0f)
                    {
                        pieces[j].Position -= dir * (pen * shareJ);
                    }
                    pi = pieces[i].Position;
                    contactsForI++;
                }
            }
        }

        private static bool SolveGroundContactsForCylinder(
            ref RigidStructurePiece p,
            IVoxelSoilSampler soilSampler,
            float h)
        {
            bool anyHit = false;
            bool isBoxPanel = p.HalfExtents.X > p.DiameterM * 0.65f || p.HalfExtents.Z > p.DiameterM * 0.65f;

            if (isBoxPanel)
            {
                // Duz levha / doseme / cati paneli: gercek 8 OBB kosesi ile SIMETRIK (Jacobi) zemin carpismasi
                // (4 alt kose eszamanli temas ettiginde net tork tam 0 olur -> sifir sallanma, sifir havada kalma!)
                float hx = MathF.Max(0.08f, p.HalfExtents.X);
                float hy = MathF.Max(0.06f, p.HalfExtents.Y);
                float hz = MathF.Max(0.08f, p.HalfExtents.Z);

                int hitCount = 0;
                Vector3 sumLinImpulse = Vector3.Zero;
                Vector3 sumAngImpulse = Vector3.Zero;

                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 localPt = new Vector3(sx * hx, sy * hy, sz * hz);
                    Vector3 rArm = Vector3.Transform(localPt, p.Rotation);
                    Vector3 pt = p.Position + rArm;

                    if (!soilSampler.TrySampleGroundSurfaceY(pt.X, pt.Z, pt.Y + 0.45f, out float groundY, out _))
                    {
                        continue;
                    }

                    float rawPen = groundY - pt.Y;
                    if (rawPen <= 0f) continue;

                    float penetration = MathF.Min(rawPen, 0.12f);
                    Vector3 n = Vector3.UnitY;
                    float w = ComputeGeneralizedInvMass(in p, rArm, n);
                    if (w < 1e-10f) continue;

                    float dLambda = penetration / w;
                    Vector3 imp = n * dLambda;
                    sumLinImpulse += imp;
                    sumAngImpulse += Vector3.Cross(rArm, imp);

                    Vector3 velAtPt = p.LinearVelocity + Vector3.Cross(p.AngularVelocity, rArm);
                    Vector3 velTan = new Vector3(velAtPt.X, 0f, velAtPt.Z);
                    float tanSpeed = velTan.Length();
                    if (tanSpeed > 1e-4f)
                    {
                        float maxFriction = GroundDynamicFrictionMu * dLambda;
                        float corr = MathF.Min(tanSpeed * h, maxFriction);
                        Vector3 fImp = -(velTan / tanSpeed) * (corr * 0.45f);
                        sumLinImpulse += fImp;
                        sumAngImpulse += Vector3.Cross(rArm, fImp);
                    }
                    hitCount++;
                }

                if (hitCount > 0)
                {
                    float invHits = 1.0f / hitCount;
                    if (p.InvMass > 0f)
                    {
                        p.Position += (sumLinImpulse * invHits) * p.InvMass;
                        Vector3 dTheta = ApplyWorldInvInertia(in p, sumAngImpulse * invHits);
                        Quaternion dq = new Quaternion(dTheta.X, dTheta.Y, dTheta.Z, 0f) * p.Rotation;
                        p.Rotation = Quaternion.Normalize(new Quaternion(
                            p.Rotation.X + 0.5f * dq.X,
                            p.Rotation.Y + 0.5f * dq.Y,
                            p.Rotation.Z + 0.5f * dq.Z,
                            p.Rotation.W + 0.5f * dq.W));
                    }
                    return true;
                }
                return false;
            }

            Vector3 axisWorld = Vector3.Transform(Vector3.UnitY, p.Rotation);
            float halfL = p.LengthM * 0.5f;
            float r = p.DiameterM * 0.5f;

            // Silindir / kutuk icin 3 temas noktasi: alt uc (-halfL), merkez (0), ust uc (+halfL)
            ReadOnlySpan<float> offsets = stackalloc float[3] { -halfL, 0f, halfL };
            for (int k = 0; k < 3; k++)
            {
                Vector3 pt = p.Position + axisWorld * offsets[k];
                if (!soilSampler.TrySampleGroundSurfaceY(pt.X, pt.Z, pt.Y + 0.40f, out float groundY, out _))
                {
                    continue;
                }

                float rawPenetration = (groundY + r) - pt.Y;
                if (rawPenetration <= 0f) continue;

                // Alt-adim basina penetrasyon duzeltmesini yumusak tut (gomulu sutunun uzaya firlamasini onler)
                float penetration = MathF.Min(rawPenetration, 0.12f);
                anyHit = true;

                Vector3 rArm = pt - p.Position;
                Vector3 n = Vector3.UnitY;
                float w = ComputeGeneralizedInvMass(in p, rArm, n);
                if (w < 1e-10f) continue;

                float dLambda = penetration / w;
                ApplyPositionalCorrection(ref p, rArm, n * dLambda);

                // Zemin yatay surtunmesi (kaymayi ve sonsuz yuvarlanmayi keser)
                Vector3 velAtPt = p.LinearVelocity + Vector3.Cross(p.AngularVelocity, rArm);
                Vector3 velTan = new Vector3(velAtPt.X, 0f, velAtPt.Z);
                float tanSpeed = velTan.Length();
                if (tanSpeed > 1e-4f)
                {
                    float maxFriction = GroundDynamicFrictionMu * dLambda;
                    float corr = MathF.Min(tanSpeed * h, maxFriction);
                    Vector3 fDir = -(velTan / tanSpeed);
                    ApplyPositionalCorrection(ref p, rArm, fDir * (corr * 0.5f));
                }
            }
            return anyHit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ComputeGeneralizedInvMass(in RigidStructurePiece p, Vector3 rWorld, Vector3 n)
        {
            if (p.InvMass <= 0f) return 0f;
            Vector3 rn = Vector3.Cross(rWorld, n);
            Vector3 invIRn = ApplyWorldInvInertia(in p, rn);
            return p.InvMass + Vector3.Dot(rn, invIRn);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyPositionalCorrection(ref RigidStructurePiece p, Vector3 rWorld, Vector3 impulse)
        {
            if (p.InvMass <= 0f) return;
            p.Position += impulse * p.InvMass;
            Vector3 dTheta = ApplyWorldInvInertia(in p, Vector3.Cross(rWorld, impulse));
            Quaternion dq = new Quaternion(dTheta.X, dTheta.Y, dTheta.Z, 0f) * p.Rotation;
            p.Rotation = Quaternion.Normalize(new Quaternion(
                p.Rotation.X + 0.5f * dq.X,
                p.Rotation.Y + 0.5f * dq.Y,
                p.Rotation.Z + 0.5f * dq.Z,
                p.Rotation.W + 0.5f * dq.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ApplyWorldInvInertia(in RigidStructurePiece p, Vector3 vWorld)
        {
            Vector3 vLocal = Vector3.Transform(vWorld, Quaternion.Conjugate(p.Rotation));
            Vector3 scaled = new Vector3(
                vLocal.X * p.InvInertiaLocal.X,
                vLocal.Y * p.InvInertiaLocal.Y,
                vLocal.Z * p.InvInertiaLocal.Z);
            return Vector3.Transform(scaled, p.Rotation);
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
        private static void SetLambdas(ref CornerBond4Interface b, int c, float lamN, float lamT)
        {
            switch (c)
            {
                case 0: b.LambdaN0 = lamN; b.LambdaT0 = lamT; break;
                case 1: b.LambdaN1 = lamN; b.LambdaT1 = lamT; break;
                case 2: b.LambdaN2 = lamN; b.LambdaT2 = lamT; break;
                default: b.LambdaN3 = lamN; b.LambdaT3 = lamT; break;
            }
        }

        [ThreadStatic] private static Vector3[]? _tlsPrevPos;
        [ThreadStatic] private static Quaternion[]? _tlsPrevRot;

        private static Vector3[] GetScratchVec3(int n, int _)
        {
            if (_tlsPrevPos == null || _tlsPrevPos.Length < n) _tlsPrevPos = new Vector3[Math.Max(n, 256)];
            return _tlsPrevPos;
        }

        private static Quaternion[] GetScratchQuat(int n)
        {
            if (_tlsPrevRot == null || _tlsPrevRot.Length < n) _tlsPrevRot = new Quaternion[Math.Max(n, 256)];
            return _tlsPrevRot;
        }
    }
}
