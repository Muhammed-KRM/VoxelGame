#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>Kertik/birlesim turune gore 4-kose kirilma parametreleri (DOK4 Bolum 4.1-4.2).</summary>
    public readonly struct JoineryStrengthSpec
    {
        public readonly float TensileStrengthPa;     // sigma_t (cekme dayanimi)
        public readonly float CohesionPa;            // c_joinery (kesme kohezyonu)
        public readonly float EffectiveFrictionMu;   // mu_eff (kertik acisi dahil etkin surtunme)
        public readonly float CompressionStrengthPa; // sigma_c (basinc ezilme)
        public readonly float ComplianceMPerN;       // XPBD uyumu alpha = 1/k

        public JoineryStrengthSpec(float sigmaT, float cohesion, float muEff, float sigmaC, float compliance)
        {
            TensileStrengthPa = sigmaT;
            CohesionPa = cohesion;
            EffectiveFrictionMu = muEff;
            CompressionStrengthPa = sigmaC;
            ComplianceMPerN = compliance;
        }
    }

    /// <summary>
    /// KM-4 / DOK4 Bolum 4: Reduced-AEM 4-Kose Kirilabilir Arayuz Degerlendiricisi (0 B GC).
    /// Tek merkez joint yerine 4 kose bag noktasiyla dogal egilme momenti ve kismi catlama->menteseleme->devrilme uretir.
    /// </summary>
    public static class ReducedAemBondEvaluator
    {
        // Kertiksiz duz yuvarlak kutuk: 0 cekme, 0 kohezyon, sadece 0.40 surtunme -> kolayca kayar/dagilir!
        private static readonly JoineryStrengthSpec FrictionOnlySpec = new(0f, 0f, 0.40f, 18e6f, 2.5e-6f);
        // Eyer Kertigi (Saddle-Notch): dusuk cekme ama yuksek etkin surtunme (mu_eff ≈ 1.65) -> ust yukle sikisir!
        private static readonly JoineryStrengthSpec SaddleNotchSpec = new(1.2e5f, 3.8e5f, 1.65f, 24e6f, 3.5e-7f);
        // Kirlangic Kuyrugu (Dovetail): yuksek geometrik cekme kilidi -> duvar disa acilamaz!
        private static readonly JoineryStrengthSpec DovetailSpec = new(1.4e6f, 1.8e6f, 2.35f, 28e6f, 1.8e-7f);
        // Ahsap Kavela / Pim (Pegged — kalkan duvar / payanda)
        private static readonly JoineryStrengthSpec PeggedSpec = new(6.5e5f, 9.5e5f, 1.35f, 24e6f, 2.8e-7f);
        // Doseme Kiris Cebi (JoistPocket — kat diyaframi kilidi)
        private static readonly JoineryStrengthSpec JoistPocketSpec = new(1.1e6f, 1.5e6f, 2.05f, 26e6f, 2.0e-7f);
        // Pièce-sur-Pièce Dikey Oluk + Zivana Dili (GroovedChannel — sifir ic ice gecmeli yanal kilit!)
        private static readonly JoineryStrengthSpec GroovedChannelSpec = new(1.6e6f, 2.1e6f, 2.45f, 30e6f, 1.5e-7f);
        // Kesintisiz Ana Direk Yan Zivana + Kavela (SideMortisePegged — 2./3. kat ana tasiyici kiris kilidi!)
        private static readonly JoineryStrengthSpec SideMortisePeggedSpec = new(2.2e6f, 2.6e6f, 2.60f, 32e6f, 1.2e-7f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static JoineryStrengthSpec GetJoinerySpec(TimberJoineryKind kind)
        {
            return kind switch
            {
                TimberJoineryKind.SaddleNotch => SaddleNotchSpec,
                TimberJoineryKind.Dovetail => DovetailSpec,
                TimberJoineryKind.Pegged => PeggedSpec,
                TimberJoineryKind.JoistPocket => JoistPocketSpec,
                TimberJoineryKind.GroovedChannel => GroovedChannelSpec,
                TimberJoineryKind.SideMortisePegged => SideMortisePeggedSpec,
                _ => FrictionOnlySpec
            };
        }

        /// <summary>
        /// Iki tek-parca eleman arasinda dunya temas merkezi ve yaricap/genisliginden 4-koseli Reduced-AEM arayuzu olusturur.
        /// </summary>
        public static CornerBond4Interface CreateInterface(
            uint bondId,
            in RigidStructurePiece pieceA,
            in RigidStructurePiece pieceB,
            Vector3 contactCenterWorld,
            Vector3 contactNormalWorld,
            float halfWidthA,
            float halfWidthB,
            TimberJoineryKind joinery)
        {
            Vector3 n = contactNormalWorld.LengthSquared() > 1e-6f ? Vector3.Normalize(contactNormalWorld) : Vector3.UnitY;
            Vector3 refAxis = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 t1 = Vector3.Normalize(Vector3.Cross(n, refAxis));
            Vector3 t2 = Vector3.Normalize(Vector3.Cross(n, t1));

            float hw1 = MathF.Max(0.08f, halfWidthA);
            float hw2 = MathF.Max(0.08f, halfWidthB);

            Vector3 c0W = contactCenterWorld + t1 * hw1 + t2 * hw2;
            Vector3 c1W = contactCenterWorld - t1 * hw1 + t2 * hw2;
            Vector3 c2W = contactCenterWorld - t1 * hw1 - t2 * hw2;
            Vector3 c3W = contactCenterWorld + t1 * hw1 - t2 * hw2;

            Quaternion invRotA = Quaternion.Conjugate(pieceA.Rotation);
            Quaternion invRotB = Quaternion.Conjugate(pieceB.Rotation);

            JoineryStrengthSpec spec = GetJoinerySpec(joinery);
            return new CornerBond4Interface
            {
                BondId = bondId,
                PieceA = pieceA.PieceId,
                PieceB = pieceB.PieceId,
                Joinery = joinery,
                CornerBrokenMask = 0,
                IsActive = 1,
                ContactAreaM2 = MathF.Max(0.02f, 4.0f * hw1 * hw2),
                ComplianceMPerN = spec.ComplianceMPerN,
                LastStressRatio01 = 0f,
                LocalA0 = Vector3.Transform(c0W - pieceA.Position, invRotA),
                LocalA1 = Vector3.Transform(c1W - pieceA.Position, invRotA),
                LocalA2 = Vector3.Transform(c2W - pieceA.Position, invRotA),
                LocalA3 = Vector3.Transform(c3W - pieceA.Position, invRotA),
                LocalB0 = Vector3.Transform(c0W - pieceB.Position, invRotB),
                LocalB1 = Vector3.Transform(c1W - pieceB.Position, invRotB),
                LocalB2 = Vector3.Transform(c2W - pieceB.Position, invRotB),
                LocalB3 = Vector3.Transform(c3W - pieceB.Position, invRotB)
            };
        }

        /// <summary>
        /// XPBD substep ortalamasindan gelen 4 kose kuvvetini (normal + teget) Mohr-Coulomb ve cekme/basinc
        /// kriterleriyle degerlendirir; kopan koseleri isaretler (1-2 kose = kismi catlama/mentese, 4 kose = tam kopma).
        /// </summary>
        public static bool EvaluateAndFractureCorners(
            ref CornerBond4Interface bond,
            float substepDt,
            int islandId,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink = null)
        {
            if (bond.IsActive == 0) return false;

            float invH2 = 1.0f / MathF.Max(1e-8f, substepDt * substepDt);
            JoineryStrengthSpec spec = GetJoinerySpec(bond.Joinery);
            float cornerArea = MathF.Max(0.005f, bond.ContactAreaM2 * 0.25f);

            float maxTensileN = spec.TensileStrengthPa * cornerArea;
            float maxCompN = spec.CompressionStrengthPa * cornerArea;
            float baseShearN = spec.CohesionPa * cornerArea;

            byte prevMask = bond.CornerBrokenMask;
            float worstRatio = 0f;

            for (int c = 0; c < 4; c++)
            {
                if ((bond.CornerBrokenMask & (1 << c)) != 0) continue;

                float lamN = GetLambdaN(in bond, c);
                float lamT = GetLambdaT(in bond, c);
                float fn = lamN * invH2;           // > 0: cekme, < 0: basinc
                float ft = MathF.Abs(lamT * invH2); // teget kesme kuvveti

                float rTensile = fn > 0f ? (maxTensileN > 1f ? fn / maxTensileN : (fn > 25f ? 2.0f : 0f)) : 0f;
                float rComp = fn < 0f ? (-fn / MathF.Max(1f, maxCompN)) : 0f;
                float shearLimit = baseShearN + spec.EffectiveFrictionMu * MathF.Max(0f, -fn);
                float rShear = shearLimit > 1f ? (ft / shearLimit) : (ft > 25f ? 2.0f : 0f);

                float cornerRatio = MathF.Max(rTensile, MathF.Max(rComp, rShear));
                if (cornerRatio > worstRatio) worstRatio = cornerRatio;

                if (cornerRatio >= 1.0f)
                {
                    bond.CornerBrokenMask |= (byte)(1 << c);
                }
            }

            bond.LastStressRatio01 = Math.Clamp(worstRatio, 0f, 2.0f);
            bool anyNewBreak = bond.CornerBrokenMask != prevMask;
            if (bond.CornerBrokenMask == 0x0F)
            {
                bond.IsActive = 0;
            }

            if (anyNewBreak && traceSink != null && traceSink.IsEnabled)
            {
                var ev = new RigidPhysicsTraceEvent
                {
                    Tick = currentTick,
                    PieceId = bond.PieceA,
                    IslandId = islandId,
                    EventKind = bond.IsActive == 0 ? RigidTimberTraceEventKind.BondFullBreak : RigidTimberTraceEventKind.BondPartialCrack,
                    State = RigidTimberState.DynamicActive,
                    SoilDecision = SoilEmbedDecision.ElasticBalanced,
                    BrokenMask = bond.CornerBrokenMask,
                    StressRatio01 = bond.LastStressRatio01,
                    DriveMomentNm = worstRatio
                };
                traceSink.Record(in ev, "ReducedAemBondEvaluator.EvaluateAndFractureCorners");
            }

            return anyNewBreak;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetLambdaN(in CornerBond4Interface b, int c) => c switch
        {
            0 => b.LambdaN0,
            1 => b.LambdaN1,
            2 => b.LambdaN2,
            _ => b.LambdaN3
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetLambdaT(in CornerBond4Interface b, int c) => c switch
        {
            0 => b.LambdaT0,
            1 => b.LambdaT1,
            2 => b.LambdaT2,
            _ => b.LambdaT3
        };
    }
}
