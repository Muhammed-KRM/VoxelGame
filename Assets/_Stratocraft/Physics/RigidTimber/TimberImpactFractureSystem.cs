#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// Kinetik Carpma Hasari, Birikimli Kirilma ve Alttaki Parcalara Zincirleme Enerji Aktarimi (0 B GC).
    /// - Yukaridan dusen parcanin E_k = 0.5 * m * v^2 enerjisi malzeme/kesit kirilma esigiyle karsilastirilir.
    /// - Alttaki parca ilk dususte kirilmasa bile hasar (Damage01) birikir; >= 1.0 olunca parca kirilir/bolunur.
    /// - Bir parcanin ustune agir bir sey dustugunde, carpma enerjisinin iletim orani (eta) altindaki destek parcalarina aktarilir!
    /// </summary>
    public static class TimberImpactFractureSystem
    {
        /// <summary>Carpma hasari baslatacak minimum bagil carpma hizi (m/s).</summary>
        public const float MinDamagingImpactSpeedMps = 3.2f;

        /// <summary>Ustteki parcaya carpan enerjinin altindaki destek parcasina aktarim katsayisi (0..1).</summary>
        public const float DownwardEnergyTransferRatio = 0.65f;

        /// <summary>
        /// Agac turu ve kesit kalinligina gore parcanin kirilma enerjisi esigini (Joule) hesaplar.
        /// Daha kalin ve sert agaclar (Oak / Ironwood) cok daha fazla darbe emer; ince/yumusak kalaslar daha cabuk kirilir.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeFractureEnergyJoules(in RigidStructurePiece piece)
        {
            WoodSpeciesSpec spec = WoodSpeciesPhysicsLut.Get(piece.Species);
            float strengthFactor = spec.BendingStrengthPa / 28e6f; // Pine = 1.0, Oak = 1.5, Ironwood = 2.32
            float sectionScale = MathF.Max(0.18f, piece.DiameterM);
            float kindMult = piece.Kind switch
            {
                RigidTimberPieceKind.Post => 1.65f,
                RigidTimberPieceKind.Beam or RigidTimberPieceKind.TieBeam or RigidTimberPieceKind.RidgeBeam => 1.25f,
                RigidTimberPieceKind.Log => 1.15f,
                RigidTimberPieceKind.Rafter => 0.75f,
                RigidTimberPieceKind.Plank => 0.55f,
                _ => 0.85f
            };
            // Ornek: 0.24m kalinliginda Pine doseme kalasi -> ~2600 J; 0.62m Oak kose sutunu -> ~18500 J
            return 8500.0f * strengthFactor * (sectionScale * sectionScale / 0.16f) * kindMult;
        }

        /// <summary>
        /// Dinamik olarak dusen bir parcanin yere veya alttaki baska bir parcaya carpmasini degerlendirir.
        /// - Düşen parçaya kendi çarpma hasarını ekler.
        /// - Altındaki parçalara (ve onların altındaki destek zincirine!) enerji aktarır.
        /// - Hasarı >= 1.0 olan parçaların indekslerini fracturedIndicesOut içine yazar ve sayısını döner.
        /// </summary>
        public static int EvaluateImpactAndCascadeEnergy(
            int fallingIdx,
            float impactSpeedMps,
            Span<RigidStructurePiece> pieces,
            Span<int> fracturedIndicesOut)
        {
            if (fallingIdx < 0 || fallingIdx >= pieces.Length) return 0;
            if (impactSpeedMps < MinDamagingImpactSpeedMps) return 0;

            ref RigidStructurePiece hitter = ref pieces[fallingIdx];
            float effectiveSpeed = impactSpeedMps - (MinDamagingImpactSpeedMps * 0.5f);
            float kineticEnergyJ = 0.5f * MathF.Max(5f, hitter.MassKg) * effectiveSpeed * effectiveSpeed;

            int fracturedCount = 0;

            // 1) Dusen parcanin kendi carpma hasari (yer veya alt parca tepkisi)
            float selfThreshold = ComputeFractureEnergyJoules(in hitter);
            float selfDmg = (kineticEnergyJ * 0.45f) / MathF.Max(200f, selfThreshold);
            if (selfDmg > 0.02f)
            {
                bool wasIntact = hitter.Damage01 < 1.0f;
                hitter.Damage01 = MathF.Min(2.0f, hitter.Damage01 + selfDmg);
                if (wasIntact && hitter.Damage01 >= 1.0f && fracturedCount < fracturedIndicesOut.Length)
                {
                    fracturedIndicesOut[fracturedCount++] = fallingIdx;
                }
            }

            // 2) Hemen altindaki dogrudan carptigi parcalari bul ve enerjiyi asagiya zincirleme aktar!
            // Donma korumasi: en fazla 10 komsuya ve maks 3 derinlige yayilir (O(N), asla O(N^4) olmaz!)
            int visitBudget = 10;
            PropagateEnergyDownwardRecursive(
                sourceIdx: fallingIdx,
                incomingEnergyJ: kineticEnergyJ * 0.85f,
                depth: 0,
                pieces: pieces,
                fracturedIndicesOut: fracturedIndicesOut,
                fracturedCount: ref fracturedCount,
                visitBudget: ref visitBudget);

            return fracturedCount;
        }

        private static void PropagateEnergyDownwardRecursive(
            int sourceIdx,
            float incomingEnergyJ,
            int depth,
            Span<RigidStructurePiece> pieces,
            Span<int> fracturedIndicesOut,
            ref int fracturedCount,
            ref int visitBudget)
        {
            if (depth >= 3 || incomingEnergyJ < 150.0f || visitBudget <= 0) return;

            ref readonly RigidStructurePiece src = ref pieces[sourceIdx];
            float srcBottomY = src.Position.Y - MathF.Max(src.HalfExtents.Y, src.DiameterM * 0.5f);
            float srcReachXZ = MathF.Max(src.HalfExtents.X, MathF.Max(src.HalfExtents.Z, src.LengthM * 0.45f)) + 0.35f;

            for (int j = 0; j < pieces.Length && visitBudget > 0; j++)
            {
                if (j == sourceIdx) continue;
                ref RigidStructurePiece target = ref pieces[j];

                // Hedef parca, kaynak parcanin altinda ve yatay (XZ) temas izdusumu icinde mi?
                float targetTopY = target.Position.Y + MathF.Max(target.HalfExtents.Y, target.DiameterM * 0.5f);
                float verticalGap = srcBottomY - targetTopY;
                if (verticalGap < -0.95f || verticalGap > 0.90f) continue;

                float dx = MathF.Abs(src.Position.X - target.Position.X);
                float dz = MathF.Abs(src.Position.Z - target.Position.Z);
                float targetReachXZ = MathF.Max(target.HalfExtents.X, MathF.Max(target.HalfExtents.Z, target.LengthM * 0.45f));
                if (dx > srcReachXZ + targetReachXZ || dz > srcReachXZ + targetReachXZ) continue;

                visitBudget--;

                // Alttaki hedef parcaya hasar biriktir (tek seferde kirilmasa bile hasar birikir!)
                float targetThreshold = ComputeFractureEnergyJoules(in target);
                float dmgDelta = incomingEnergyJ / MathF.Max(200f, targetThreshold);
                if (dmgDelta > 0.01f)
                {
                    bool wasIntact = target.Damage01 < 1.0f;
                    target.Damage01 = MathF.Min(2.0f, target.Damage01 + dmgDelta);
                    if (wasIntact && target.Damage01 >= 1.0f)
                    {
                        if (target.State == RigidTimberState.StaticAnchored)
                        {
                            target.State = RigidTimberState.DynamicActive;
                        }
                        if (fracturedCount < fracturedIndicesOut.Length)
                        {
                            fracturedIndicesOut[fracturedCount++] = j;
                        }
                    }
                }

                // ZINCIRLEME ENERJI AKTARIMI: Ustteki nesne kirilmasa bile, altindaki nesneye darbe enerjisini aktarir!
                float transmittedEnergyJ = incomingEnergyJ * DownwardEnergyTransferRatio;
                PropagateEnergyDownwardRecursive(
                    sourceIdx: j,
                    incomingEnergyJ: transmittedEnergyJ,
                    depth: depth + 1,
                    pieces: pieces,
                    fracturedIndicesOut: fracturedIndicesOut,
                    fracturedCount: ref fracturedCount,
                    visitBudget: ref visitBudget);
            }
        }
    }
}
