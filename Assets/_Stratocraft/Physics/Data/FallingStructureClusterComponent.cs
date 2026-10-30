using System.Runtime.InteropServices;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Domino yikiminda kopan N voxelin GreedyBoxFractureJob tarafindan birlestirildigi
    /// maksimum 12 konveks BoxCollider kumesinden (FallingStructureCluster) her birinin fizik bileseni.
    /// Bkz. MATEMATIKSEL SIMULASYON RAPORU Bolum 2.3 ve GELISTIRICI DOKUMANI Bolum 4.13.5.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FallingStructureClusterComponent
    {
        /// <summary>Kumenin minimum lokal koordinati (MinX, MinY, MinZ - dahil).</summary>
        public (int X, int Y, int Z) MinBounds;

        /// <summary>Kumenin maksimum lokal koordinati (MaxX, MaxY, MaxZ - dahil).</summary>
        public (int X, int Y, int Z) MaxBounds;

        /// <summary>Kume icindeki toplam voxel sayisi.</summary>
        public int VoxelCount;

        /// <summary>Kumenin toplam kutlesi (kg).</summary>
        public float TotalMassKg;

        /// <summary>Kumenin kutle merkezi (Center of Mass - metre).</summary>
        public (float X, float Y, float Z) CenterOfMass;

        /// <summary>Kopma noktasindaki (pivot) devrilme torku tau = sum(m_i * (r_i - r_pivot) x g) (N*m).</summary>
        public (float X, float Y, float Z) OverturningTorqueNm;

        /// <summary>Baskın materyal kimligi.</summary>
        public byte DominantMaterialId;
    }
}
