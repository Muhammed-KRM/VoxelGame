#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Stratocraft.Core.Constants;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>6 zemin sinifi icin geoteknik parametreler (DOK3 Bolum 3.1 / DOK4 Bolum 3.2).</summary>
    public readonly struct SoilGeotechSpec
    {
        public readonly bool IsCohesive;
        public readonly float UnitWeightNPerM3; // gamma (N/m^3)
        public readonly float Kp;               // Rankine pasif basinc katsayisi
        public readonly float Ka;               // Rankine aktif basinc katsayisi
        public readonly float CuPa;             // Drenajsiz kayma dayanimi c_u (Pa)
        public readonly float NhNPerM3;         // Kohezyonsuz yatak katsayisi artisi n_h (N/m^3)
        public readonly float KhPaPerM;         // Kohezyonlu yatak katsayisi k_h (N/m^3)

        public SoilGeotechSpec(bool isCohesive, float gammaKnM3, float phiDeg, float cuKpa, float nhKnM3, float khMnM3)
        {
            IsCohesive = isCohesive;
            UnitWeightNPerM3 = gammaKnM3 * 1000.0f;
            float phiRad = phiDeg * (MathF.PI / 180.0f);
            float tP = MathF.Tan((MathF.PI * 0.25f) + phiRad * 0.5f);
            float tA = MathF.Tan((MathF.PI * 0.25f) - phiRad * 0.5f);
            Kp = tP * tP;
            Ka = tA * tA;
            CuPa = cuKpa * 1000.0f;
            NhNPerM3 = nhKnM3 * 1000.0f;
            KhPaPerM = khMnM3 * 1_000_000.0f;
        }
    }

    /// <summary>
    /// KM-3 / DOK4 Bolum 3: Gomulu Dikey Sutun / Kazik Zemin Mekanigi Cozucusu (0 B GC, O(1)/sutun).
    /// Broms (1964) + IBC 1807.3 + Winkler Hiperbolik p-y + Asimetrik Kazi Aktif Toprak Itkisi.
    /// </summary>
    public static class SoilEmbedmentEvaluator
    {
        public const float SliceHeightM = 0.25f;
        public const int MaxEmbedSlices = 10; // Maks 2.50m derinlik taramasi

        private static readonly SoilGeotechSpec MudSpec = new(isCohesive: true, gammaKnM3: 13f, phiDeg: 5f, cuKpa: 10f, nhKnM3: 1500f, khMnM3: 1.2f);
        private static readonly SoilGeotechSpec SandSpec = new(isCohesive: false, gammaKnM3: 15.5f, phiDeg: 29f, cuKpa: 0f, nhKnM3: 5500f, khMnM3: 4f);
        private static readonly SoilGeotechSpec DirtLoamSpec = new(isCohesive: false, gammaKnM3: 18f, phiDeg: 30f, cuKpa: 15f, nhKnM3: 14000f, khMnM3: 12f);
        private static readonly SoilGeotechSpec StiffClaySpec = new(isCohesive: true, gammaKnM3: 19.5f, phiDeg: 20f, cuKpa: 95f, nhKnM3: 18000f, khMnM3: 32f);
        private static readonly SoilGeotechSpec GravelStoneSpec = new(isCohesive: false, gammaKnM3: 20.5f, phiDeg: 40f, cuKpa: 0f, nhKnM3: 42000f, khMnM3: 65f);
        private static readonly SoilGeotechSpec BedrockSpec = new(isCohesive: true, gammaKnM3: 26f, phiDeg: 48f, cuKpa: 12000f, nhKnM3: 250000f, khMnM3: 600f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SoilGeotechSpec ResolveSoilSpec(byte materialId)
        {
            return materialId switch
            {
                MaterialIdCatalog.PackedIce or MaterialIdCatalog.WetPortlandConcrete => MudSpec,
                MaterialIdCatalog.Sand => SandSpec,
                MaterialIdCatalog.Dirt or MaterialIdCatalog.Grass or MaterialIdCatalog.GlowingMoss => DirtLoamSpec,
                MaterialIdCatalog.Clay or MaterialIdCatalog.AdobeBrickBlock or MaterialIdCatalog.CobMudBrick or MaterialIdCatalog.RammedEarthCompacted => StiffClaySpec,
                MaterialIdCatalog.Gravel or MaterialIdCatalog.Cobblestone or MaterialIdCatalog.LooseRubble or MaterialIdCatalog.CrushedLimeAggregate => GravelStoneSpec,
                MaterialIdCatalog.Stone or MaterialIdCatalog.Basalt or MaterialIdCatalog.Marble or MaterialIdCatalog.Limestone
                    or MaterialIdCatalog.Bedrock or MaterialIdCatalog.NaturalObsidian or MaterialIdCatalog.CutAshlarStone
                    or MaterialIdCatalog.StoneBrick or MaterialIdCatalog.DriedRammedEarth or MaterialIdCatalog.CuredReinforcedConcrete => BedrockSpec,
                _ => DirtLoamSpec
            };
        }

        /// <summary>
        /// Sutunun tabanindan yukari dogru 4 yonde (+X, -X, +Z, -Z) 0.25m dilimlerle voxel zemin profilini cikarir
        /// ve Broms/Winkler yon bagimli devrilme kapasitelerini hesaplar.
        /// </summary>
        public static SoilEmbedProfile SampleAndBuildProfile(
            in RigidStructurePiece post,
            IVoxelSoilSampler soilSampler,
            float compactionFactor = 0.85f,
            bool hasStoneCollar = false)
        {
            float kappa = Math.Clamp(compactionFactor, 0.30f, 1.0f);
            float b = MathF.Max(0.14f, post.DiameterM);
            float sampleRadius = MathF.Max(0.32f, b * 0.5f + 0.22f);
            float baseWorldY = post.Position.Y - post.LengthM * 0.5f;

            var prof = new SoilEmbedProfile
            {
                PieceId = post.PieceId,
                CompactionFactor = kappa,
                HasStoneCollar = hasStoneCollar ? (byte)1 : (byte)0,
                LastWoodYieldMomentNm = WoodSpeciesPhysicsLut.ComputeYieldMomentNm(post.Species, b)
            };

            Span<float> dirDepth = stackalloc float[4];
            Span<byte> dirMat = stackalloc byte[4];
            Span<float> dx = stackalloc float[4] { 1f, -1f, 0f, 0f };
            Span<float> dz = stackalloc float[4] { 0f, 0f, 1f, -1f };

            // 1) Her 4 yonde gomulme derinligini ve baskin zemin malzemesini tara
            for (int d = 0; d < 4; d++)
            {
                float sx = post.Position.X + dx[d] * sampleRadius;
                float sz = post.Position.Z + dz[d] * sampleRadius;
                int solidSlices = 0;
                byte topMat = MaterialIdCatalog.Air;

                for (int s = 0; s < MaxEmbedSlices; s++)
                {
                    float sy = baseWorldY + (s + 0.5f) * SliceHeightM;
                    if (soilSampler.TrySampleSolidMaterialAt(sx, sy, sz, out byte mat) && mat != MaterialIdCatalog.Air)
                    {
                        solidSlices = s + 1;
                        topMat = mat;
                    }
                }

                dirDepth[d] = solidSlices * SliceHeightM;
                dirMat[d] = topMat;
            }

            prof.DirDepthPosX = dirDepth[0];
            prof.DirDepthNegX = dirDepth[1];
            prof.DirDepthPosZ = dirDepth[2];
            prof.DirDepthNegZ = dirDepth[3];
            prof.DirSoilMatPosX = dirMat[0];
            prof.DirSoilMatNegX = dirMat[1];
            prof.DirSoilMatPosZ = dirMat[2];
            prof.DirSoilMatNegZ = dirMat[3];

            float maxD = MathF.Max(MathF.Max(dirDepth[0], dirDepth[1]), MathF.Max(dirDepth[2], dirDepth[3]));
            float avgD = 0.25f * (dirDepth[0] + dirDepth[1] + dirDepth[2] + dirDepth[3]);
            prof.EmbedDepthM = avgD;
            float zr = 0.70f * MathF.Max(0.10f, maxD);
            prof.PivotDepthZr = zr;

            // 2) Her 4 devrilme yonunde Broms + Winkler direnc momenti M_u_soil ve yay sertligi K_theta
            Span<float> resist = stackalloc float[4];
            Span<float> stiff = stackalloc float[4];
            Span<int> opp = stackalloc int[4] { 1, 0, 3, 2 };

            for (int d = 0; d < 4; d++)
            {
                int o = opp[d];
                float mUpper = 0f;
                float mLower = 0f;
                float kSum = 0f;

                int maxSlices = Math.Clamp((int)MathF.Ceiling(maxD / SliceHeightM), 0, MaxEmbedSlices);
                for (int s = 0; s < maxSlices; s++)
                {
                    // z: yerel yuzeyden asagi derinlik (0 = ust yuzey, maxD = taban)
                    float z = (maxSlices - s - 0.5f) * SliceHeightM;
                    bool isUpper = z < zr;
                    int sideIdx = isUpper ? d : o;
                    float sideD = dirDepth[sideIdx];
                    float localDepthFromTop = sideD - (s + 0.5f) * SliceHeightM;
                    if (localDepthFromTop <= 0f) continue; // Bu dilimde o tarafta toprak kazilmis / bos!

                    byte mat = dirMat[sideIdx];
                    if (hasStoneCollar && localDepthFromTop <= 0.35f)
                    {
                        mat = MaterialIdCatalog.Gravel; // IBC 1807.3.2.2 yuzey tas takoz kisiti
                    }

                    // 1m disarisi ucurum/hendek mi (Mezazigh & Levacher sev indirgemesi f_m)?
                    float outerX = post.Position.X + dx[sideIdx] * (sampleRadius + 0.85f);
                    float outerZ = post.Position.Z + dz[sideIdx] * (sampleRadius + 0.85f);
                    float sy = baseWorldY + (s + 0.5f) * SliceHeightM;
                    float fm = soilSampler.TrySampleSolidMaterialAt(outerX, sy, outerZ, out byte oMat) && oMat != MaterialIdCatalog.Air
                        ? 1.0f
                        : 0.55f;

                    SoilGeotechSpec spec = ResolveSoilSpec(mat);
                    float pu;
                    float k;
                    if (spec.IsCohesive)
                    {
                        pu = localDepthFromTop < 1.5f * b ? 0f : (9.0f * spec.CuPa * b * kappa * fm);
                        k = spec.KhPaPerM * b * kappa;
                    }
                    else
                    {
                        pu = 3.0f * spec.UnitWeightNPerM3 * localDepthFromTop * spec.Kp * b * kappa * fm;
                        k = spec.NhNPerM3 * localDepthFromTop * kappa;
                    }

                    float arm = MathF.Abs(zr - z);
                    float dM = pu * SliceHeightM * arm;
                    if (isUpper) mUpper += dM;
                    else mLower += dM;
                    kSum += k * SliceHeightM * arm * arm;
                }

                // Broms kuvvet cifti (force-couple): devrilme yonundeki ust pasif zemin kazildiysa
                // arka taraf tek basina sutunu dik tutamaz (en zayif halka sinirlar).
                float mSum = dirDepth[d] <= 0.05f ? 0f : MathF.Min(mUpper + mLower, MathF.Min(mUpper, mLower) * 2.2f + mUpper * 0.35f);
                resist[d] = mSum;
                stiff[d] = dirDepth[d] <= 0.05f ? 1500f : MathF.Max(1500f, kSum);
            }

            prof.ResistMomentPosX = resist[0];
            prof.ResistMomentNegX = resist[1];
            prof.ResistMomentPosZ = resist[2];
            prof.ResistMomentNegZ = resist[3];

            prof.RotStiffnessPosX = stiff[0];
            prof.RotStiffnessNegX = stiff[1];
            prof.RotStiffnessPosZ = stiff[2];
            prof.RotStiffnessNegZ = stiff[3];

            // 3) Asimetrik Kazi Aktif Toprak Itkisi: bir taraf kazildiysa dolu karsi taraf kazilan yone iter!
            prof.AsymmetricPushX = ComputeAsymmetricActiveMoment(dirDepth[1], dirMat[1], dirDepth[0], b)
                                 - ComputeAsymmetricActiveMoment(dirDepth[0], dirMat[0], dirDepth[1], b);
            prof.AsymmetricPushZ = ComputeAsymmetricActiveMoment(dirDepth[3], dirMat[3], dirDepth[2], b)
                                 - ComputeAsymmetricActiveMoment(dirDepth[2], dirMat[2], dirDepth[3], b);

            return prof;
        }

        /// <summary>
        /// Sutun uzerindeki yanal kuvvet, eksantrik ust yuk ve asimetrik kazi itkisini hesaplayip
        /// 4 durumlu geoteknik karari (Elastic / PlasticTilting / GeotechnicalOverturn / WoodShaftFracture) dondurur.
        /// </summary>
        public static SoilEmbedDecision EvaluatePostStability(
            ref RigidStructurePiece post,
            ref SoilEmbedProfile profile,
            float topLoadKg,
            Vector3 lateralForceWorldN,
            float lateralHeightAboveGroundM,
            Vector2 topLoadEccentricityXZ,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink = null)
        {
            float totalWeightN = (post.MassKg + MathF.Max(0f, topLoadKg)) * WoodSpeciesPhysicsLut.GravityMps2;
            float leverH = MathF.Max(0.25f, lateralHeightAboveGroundM) + profile.PivotDepthZr;

            // P-Delta etkisi (mevcut plastik egilme + ust yuk eksantrikligi + gercek yapi geometrik eksantrikligi e_min)
            // Eurocode 5 / IBC 1807.3: Uzun ve agir bir yapida kusursuz 0.000m eksantriklik yoktur;
            // yukseklik (H) ve ust agirlik arttikca minimum devirici moment artar -> az gomulen 3 katli yapi ayakta duramaz!
            Vector3 axisUp = Vector3.Transform(Vector3.UnitY, post.Rotation);
            float aboveGroundH = MathF.Max(0.5f, post.LengthM - profile.EmbedDepthM);
            float comHeight = aboveGroundH * 0.55f;
            float naturalEccentricity = topLoadKg > 250f ? MathF.Max(0.06f, aboveGroundH * 0.025f) : 0f;
            float pDeltaX = axisUp.X * comHeight + topLoadEccentricityXZ.X + naturalEccentricity;
            float pDeltaZ = axisUp.Z * comHeight + topLoadEccentricityXZ.Y;

            // Devirici moment bilesenleri (+X veya +Z yonune deviren momentler)
            float mxDrive = lateralForceWorldN.X * leverH + totalWeightN * pDeltaX + profile.AsymmetricPushX;
            float mzDrive = lateralForceWorldN.Z * leverH + totalWeightN * pDeltaZ + profile.AsymmetricPushZ;
            float mDrive = MathF.Sqrt(mxDrive * mxDrive + mzDrive * mzDrive);

            float ux = mDrive > 1e-3f ? (mxDrive / mDrive) : 1f;
            float uz = mDrive > 1e-3f ? (mzDrive / mDrive) : 0f;
            profile.OverturnDirX = ux;
            profile.OverturnDirZ = uz;

            // Devrilme yonunde (u_hat) etkin zemin direnci ve donme sertligi
            float rX = ux >= 0f ? profile.ResistMomentPosX : profile.ResistMomentNegX;
            float rZ = uz >= 0f ? profile.ResistMomentPosZ : profile.ResistMomentNegZ;
            float kX = ux >= 0f ? profile.RotStiffnessPosX : profile.RotStiffnessNegX;
            float kZ = uz >= 0f ? profile.RotStiffnessPosZ : profile.RotStiffnessNegZ;

            float wx2 = ux * ux;
            float wz2 = uz * uz;
            float mSoil = rX * wx2 + rZ * wz2;
            float kTheta = kX * wx2 + kZ * wz2;

            // Gomulmemis (yuzeyde) sutun: yalnizca kendi dairesel taban yaricapi (b/2) dengesi!
            if (profile.EmbedDepthM <= 0.05f)
            {
                mSoil = MathF.Max(mSoil, totalWeightN * (post.DiameterM * 0.5f));
                kTheta = MathF.Max(kTheta, 2500f);
            }

            float mWood = profile.LastWoodYieldMomentNm > 1f
                ? profile.LastWoodYieldMomentNm
                : WoodSpeciesPhysicsLut.ComputeYieldMomentNm(post.Species, post.DiameterM);

            float limitM = MathF.Max(1f, MathF.Min(mSoil, mWood));
            float ratio = mDrive / limitM;

            SoilEmbedDecision decision;
            if (ratio < 0.70f)
            {
                decision = SoilEmbedDecision.ElasticBalanced;
            }
            else if (ratio < 1.00f)
            {
                decision = SoilEmbedDecision.PlasticTilting;
            }
            else
            {
                decision = mSoil < mWood
                    ? SoilEmbedDecision.GeotechnicalOverturn
                    : SoilEmbedDecision.WoodShaftFracture;
            }

            // Hiperbolik p-y egilme acisi (radyan)
            float denom = MathF.Max(0.12f, 1.0f - 0.85f * Math.Clamp(ratio, 0f, 0.99f));
            float thetaEq = Math.Clamp(mDrive / MathF.Max(1000f, kTheta * denom), 0f, 0.45f);
            if (decision == SoilEmbedDecision.PlasticTilting)
            {
                post.PlasticTiltRad = MathF.Max(post.PlasticTiltRad, thetaEq * 0.55f);
            }

            profile.LastDecision = decision;
            profile.LastDriveMomentNm = mDrive;
            profile.LastEffectiveResistMomentNm = mSoil;
            profile.LastWoodYieldMomentNm = mWood;
            profile.LastTiltAngleRad = MathF.Max(thetaEq, post.PlasticTiltRad);

            if (traceSink != null && traceSink.IsEnabled)
            {
                var ev = new RigidPhysicsTraceEvent
                {
                    Tick = currentTick,
                    PieceId = post.PieceId,
                    IslandId = post.IslandId,
                    EventKind = RigidTimberTraceEventKind.SoilEvaluated,
                    State = post.State,
                    SoilDecision = decision,
                    EmbedDepthM = profile.EmbedDepthM,
                    DriveMomentNm = mDrive,
                    ResistMomentNm = mSoil,
                    WoodYieldMomentNm = mWood,
                    TiltDeg = profile.LastTiltAngleRad * (180.0f / MathF.PI),
                    StressRatio01 = Math.Clamp(ratio, 0f, 2.0f),
                    DirX = ux,
                    DirY = 0f,
                    DirZ = uz
                };
                traceSink.Record(in ev, "SoilEmbedmentEvaluator.EvaluatePostStability");
            }

            return decision;
        }

        private static float ComputeAsymmetricActiveMoment(float fullSideDepth, byte fullSideMat, float openSideDepth, float b)
        {
            float exposed = fullSideDepth - openSideDepth;
            if (exposed <= 0.15f || fullSideMat == MaterialIdCatalog.Air) return 0f;
            SoilGeotechSpec spec = ResolveSoilSpec(fullSideMat);
            // Aktif toprak basinci ucgeni: F_a = 0.5 * Ka * gamma * h^2 * b, moment kolu = h / 3
            float forceN = 0.5f * spec.Ka * spec.UnitWeightNPerM3 * exposed * exposed * b;
            return forceN * (exposed * 0.45f);
        }
    }
}
