#nullable enable
using System;

namespace Stratocraft.Core.Persistence
{
    /// <summary>
    /// Paket 4C-2: Bir chunk'in dogal seed baslangicindan farkli olarak hangi alt-sistemlerde
    /// degisiklige ugradigini isaretleyen bit bayraklari. Temiz (None) chunk'lar unload edilirken
    /// disk veya bellek kaydi olusturmaz.
    /// </summary>
    [Flags]
    public enum ChunkDirtyFlags : byte
    {
        None = 0,
        Terrain = 1 << 0,
        Fluid = 1 << 1,
        Moisture = 1 << 2,
        Snow = 1 << 3,
        Salinity = 1 << 4,
        MicroPool = 1 << 5,
        PlayerFluid = 1 << 6
    }
}
