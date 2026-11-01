using System.Runtime.InteropServices;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Insaat hayalet onizleme (Ghost Build Preview) 4-renk yapisal guvenlik skalasi.
    /// </summary>
    public enum StructuralPreviewColor : byte
    {
        /// <summary>Yesil: Tam guvenli (%0..50 yuk ve konsol siniri icinde).</summary>
        GreenSafe = 0,
        /// <summary>Sari: Konsol veya kemer gerilimi altinda (%50..75 kapasite).</summary>
        YellowCantilever = 1,
        /// <summary>Turuncu: Kritik sinira yakin (%75..99 kapasite, ek yukte catlayabilir).</summary>
        OrangeCritical = 2,
        /// <summary>Kirmizi: Aninda kopar veya tasiyici kolonu ezer (&gt;= %100 kapasite / desteksiz).</summary>
        RedWillCollapse = 3
    }

    /// <summary>
    /// Domino yikiminda 3B 6-bagli bilesen etiketleme (6-CCL) + GreedyBoxFractureJob tarafindan birlestirildigi
    /// maksimum 12 konveks BoxCollider kumesinden (FallingStructureCluster) her birinin fizik bileseni.
    /// Steiner Paralel Eksen Teoremi atalet tensoru (InertiaTensorDiag) dahil edilmistir.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FallingStructureClusterComponent
    {
        /// <summary>3B bagli bilesen kimligi (ayri dusen parcalarin tek kutuda birlesmesini onler).</summary>
        public int ConnectedComponentId;

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

        /// <summary>Kutle merkezi etrafinda Steiner Paralel Eksen Teoremi diyagonal atalet tensoru (Ixx, Iyy, Izz - kg*m^2).</summary>
        public (float Ixx, float Iyy, float Izz) InertiaTensorDiag;

        /// <summary>Baskin materyal kimligi.</summary>
        public byte DominantMaterialId;
    }
}
