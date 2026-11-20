#nullable enable
using System;
using System.Numerics;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Character.Systems
{
    /// <summary>
    /// Dalga A (Faz A.4): Kurulabilir Yatak (BedBlock) Yeniden Dogma Noktasi (SpawnPoint) servisi.
    /// Oyuncu yatakla [E] etkilesimine girince koordinati kaydeder; olum sonrasi yatak hala yerindeyse
    /// ve ustu engelsizse yatakta dogurur, yatak kirildiysa dunya baslangic noktasina doner.
    /// </summary>
    public sealed class BedSpawnPointService
    {
        public bool HasBedSpawn { get; private set; }
        public int BedX { get; private set; }
        public int BedY { get; private set; }
        public int BedZ { get; private set; }

        public void SetBedSpawnPoint(int wx, int wy, int wz)
        {
            HasBedSpawn = true;
            BedX = wx;
            BedY = wy;
            BedZ = wz;
        }

        public void ClearBedSpawnPoint()
        {
            HasBedSpawn = false;
            BedX = 0;
            BedY = 0;
            BedZ = 0;
        }

        public Vector3 ResolveRespawnPosition(
            Func<int, int, int, uint> readWorldVoxelRaw,
            Vector3 worldOriginRespawnPos,
            out bool usedBedSpawn,
            out string statusMessage)
        {
            if (!HasBedSpawn)
            {
                usedBedSpawn = false;
                statusMessage = "🏠 Başlangıç noktasında yeniden doğdunuz (Yatak kurup [E] ile doğma noktası belirleyebilirsiniz).";
                return worldOriginRespawnPos;
            }

            uint bedRaw = readWorldVoxelRaw(BedX, BedY, BedZ);
            bool bedIntact = VoxelBitUtility.GetMaterialId(bedRaw) == MaterialIdCatalog.BedBlock;
            if (!bedIntact)
            {
                ClearBedSpawnPoint();
                usedBedSpawn = false;
                statusMessage = "⚠️ Yatağınız kırılmış veya yok olmuş! Başlangıç noktasına yeniden doğdunuz.";
                return worldOriginRespawnPos;
            }

            uint above1 = readWorldVoxelRaw(BedX, BedY + 1, BedZ);
            uint above2 = readWorldVoxelRaw(BedX, BedY + 2, BedZ);
            bool headClear = !VoxelBitUtility.IsPhysicallySolid(above1) && !VoxelBitUtility.IsPhysicallySolid(above2);
            if (!headClear)
            {
                usedBedSpawn = false;
                statusMessage = "⚠️ Yatağınızın üstü kapalı/engelli! Güvenli başlangıç noktasına doğdunuz.";
                return worldOriginRespawnPos;
            }

            usedBedSpawn = true;
            statusMessage = $"🛏️ Yatağınızda yeniden doğdunuz ({BedX}, {BedY + 1}, {BedZ}).";
            return new Vector3(BedX + 0.5f, BedY + 1.15f, BedZ + 0.5f);
        }
    }
}
