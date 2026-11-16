#nullable enable

namespace Stratocraft.Voxel.Data
{
    /// <summary>
    /// Stratocraft.Voxel katmaninin Stratocraft.Physics'e bagimlilik kurmadan
    /// aktif akiskan chunk kinematik yan-tamponunu (PrevMass ve Q7 yatay hizlar)
    /// okuyabilmesini saglayan sifir-GC veri sozlesmesi arayuzu.
    /// </summary>
    public interface IFluidKinematicsSlice
    {
        /// <summary>Bir onceki sabit akiskan adimi basindaki hucre kutlesi (0..255).</summary>
        byte[] PrevMass { get; }

        /// <summary>X ekseni yatay akis hizi (Q7 nicemlenmis [-127..+127] -> [-4..+4] m/s).</summary>
        sbyte[] VelX_Q7 { get; }

        /// <summary>Z ekseni yatay akis hizi (Q7 nicemlenmis [-127..+127] -> [-4..+4] m/s).</summary>
        sbyte[] VelZ_Q7 { get; }

        /// <summary>Yeni uyanan chunk'ta PrevMass dizisinin mevcut kutle ile esitlenip esitlenmedigi.</summary>
        bool IsInitializedFromCurrentMass { get; }
    }
}
