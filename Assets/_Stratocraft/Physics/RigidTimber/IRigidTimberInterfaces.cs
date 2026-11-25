#nullable enable
using System;
using System.Numerics;

namespace Stratocraft.Physics.RigidTimber
{
    /// <summary>
    /// Voxel araziden (1m veya 0.25m alt-voksel) zemin malzemesi ve kati yuzey yuksekligi okuyan SOLID arayuzu (DIP / ISP).
    /// Hem Unity 6 canli chunk sozlugunde hem saf .NET 10 birim testlerinde UnityEngine olmadan calisir.
    /// </summary>
    public interface IVoxelSoilSampler
    {
        /// <summary>Verilen dunya noktasinda kati zemin/kaya varsa true ve MaterialId doner.</summary>
        bool TrySampleSolidMaterialAt(float worldX, float worldY, float worldZ, out byte materialId);

        /// <summary>Verilen (worldX, worldZ) sutununda yuzey yuksekligini ve zemin MaterialId'sini doner.</summary>
        bool TrySampleGroundSurfaceY(float worldX, float worldZ, float referenceY, out float surfaceY, out byte materialId);
    }

    /// <summary>
    /// 0-GC fizik debug/telemetri olaylarini tasiyan ince arayuz (ISP).
    /// </summary>
    public interface IRigidPhysicsTraceSink
    {
        bool IsEnabled { get; }
        void Record(in RigidPhysicsTraceEvent traceEvent, string? functionName = null);
    }

    /// <summary>
    /// Dinamik devrilme, dusus ve 4-kose kirilabilir arayuz cozucu arayuzu (OCP / DIP).
    /// Pilot fazinda XpbdRigidTimberSolver baglanir; ileride agir tas kaleler icin AvbdRigidSolver tak-cikar degistirilebilir.
    /// </summary>
    public interface IStructuralDynamicsSolver
    {
        string SolverName { get; }

        /// <summary>
        /// Yalnizca DynamicActive parcalar ve aktif 4-kose baglar uzerinde bir fizik adimi (dt) ilerletir.
        /// Uyuyan (StaticAnchored / StaticLooseEntity) parcalara dokunmaz (0 ms).
        /// </summary>
        int StepActiveDynamics(
            Span<RigidStructurePiece> pieces,
            Span<CornerBond4Interface> bonds,
            IVoxelSoilSampler soilSampler,
            float deltaTimeSec,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink = null);
    }

    /// <summary>
    /// Olay-tabanli statik denge (Ground-Anchor BFS + double Block-Jacobi PCG + 4-kose AEM) cozucu arayuzu.
    /// </summary>
    public interface IRigidTimberIslandSolver
    {
        /// <summary>
        /// Sadece kirli (dirty) isaretlenen yapi adasini cozer; esigi asan baglari koparir ve kopan adalari DynamicActive'e uyandirir.
        /// </summary>
        int EvaluateDirtyIsland(
            int islandId,
            Span<RigidStructurePiece> pieces,
            Span<SoilEmbedProfile> soilProfiles,
            Span<CornerBond4Interface> bonds,
            Vector3 externalWindAccel,
            long currentTick,
            IRigidPhysicsTraceSink? traceSink = null);
    }
}
