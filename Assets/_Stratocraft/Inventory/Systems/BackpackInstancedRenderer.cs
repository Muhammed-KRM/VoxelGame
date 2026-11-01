using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// GAME LOGIC BIBLE v7.0 Bolum 4.2 &amp; 4.4 — 3B Kumas Canta ve Gorsel Sandiklar icin
    /// 90 Derece Capraz Kulce Piramidi (Cross-Stacked Ingot Pyramid) GPU Instanced Matris Hesaplayicisi
    /// ve Sifir-Collider (0 Physics Collider) Analitik Kay-Kajiya Slab Ray-AABB Esya Secicisi!
    /// </summary>
    public sealed class BackpackInstancedRenderer
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct InstanceTransformData
        {
            public float PosX, PosY, PosZ;
            public float YawRadians; // Katman bazli 0 ve 90 derece (PI/2) capraz kulce istiflemesi!
            public float Scale;
            public int SourceSlotIndex;
            public ushort ItemId;
            public byte PurityTier;
        }

        public int BuildGridPyramidInstances(
            ReadOnlySpan<StackableItemSlot> slots,
            Span<InstanceTransformData> outputInstances,
            int gridColumns = 6,
            float cellSpacingMeters = 0.08f)
        {
            int written = 0;
            int cols = Math.Max(1, gridColumns);

            for (int slotIdx = 0; slotIdx < slots.Length; slotIdx++)
            {
                StackableItemSlot slot = slots[slotIdx];
                if (slot.IsEmpty)
                {
                    continue;
                }

                int row = slotIdx / cols;
                int col = slotIdx % cols;
                float baseX = col * cellSpacingMeters;
                float baseZ = row * cellSpacingMeters;

                int visualLayers = slot.Count switch
                {
                    >= 48 => 4,
                    >= 24 => 3,
                    >= 8 => 2,
                    _ => 1
                };

                for (int layer = 0; layer < visualLayers; layer++)
                {
                    if (written >= outputInstances.Length)
                    {
                        return written;
                    }

                    // Tek katmanlar 0 derece, cift katmanlar 90 derece (PI/2) capraz kulce/kalas istifi!
                    float crossStackYaw = (layer & 1) == 1 ? (MathF.PI * 0.5f) : 0.0f;

                    outputInstances[written++] = new InstanceTransformData
                    {
                        PosX = baseX,
                        PosY = layer * (cellSpacingMeters * 0.45f),
                        PosZ = baseZ,
                        YawRadians = crossStackYaw,
                        Scale = 1.0f - (layer * 0.08f),
                        SourceSlotIndex = slotIdx,
                        ItemId = slot.ItemId,
                        PurityTier = slot.PurityTier
                    };
                }
            }

            return written;
        }

        /// <summary>
        /// Sifir Fizik Collider'li Kay-Kajiya Slab Ray-AABB Kesisim Testi:
        /// Canta veya sandik yerel uzayindaki fare isininin carptigi en yakin esya yuvasini O(N) (&lt; 2 us) icinde bulur!
        /// </summary>
        public static int RaycastPickSlotZeroCollider(
            Vector3 localRayOrigin,
            Vector3 localRayDir,
            ReadOnlySpan<StackableItemSlot> slots,
            out float hitDistance,
            int gridColumns = 6,
            float cellSpacingMeters = 0.08f)
        {
            hitDistance = float.MaxValue;
            int bestSlot = -1;
            int cols = Math.Max(1, gridColumns);
            float halfWidth = cellSpacingMeters * 0.42f;

            for (int slotIdx = 0; slotIdx < slots.Length; slotIdx++)
            {
                StackableItemSlot slot = slots[slotIdx];
                if (slot.IsEmpty)
                {
                    continue;
                }

                int row = slotIdx / cols;
                int col = slotIdx % cols;
                float cx = col * cellSpacingMeters;
                float cz = row * cellSpacingMeters;

                int layers = slot.Count >= 48 ? 4 : (slot.Count >= 24 ? 3 : (slot.Count >= 8 ? 2 : 1));
                float maxY = layers * (cellSpacingMeters * 0.45f) + halfWidth;

                Vector3 aabbMin = new Vector3(cx - halfWidth, -0.01f, cz - halfWidth);
                Vector3 aabbMax = new Vector3(cx + halfWidth, maxY, cz + halfWidth);

                if (IntersectRayAabbSlab(localRayOrigin, localRayDir, aabbMin, aabbMax, out float tNear) && tNear < hitDistance)
                {
                    hitDistance = tNear;
                    bestSlot = slotIdx;
                }
            }

            return bestSlot;
        }

        public static bool IntersectRayAabbSlab(
            Vector3 origin,
            Vector3 dir,
            Vector3 min,
            Vector3 max,
            out float tEnter)
        {
            tEnter = 0f;
            float tMin = 0.0f;
            float tMax = float.MaxValue;

            if (!UpdateSlabAxis(origin.X, dir.X, min.X, max.X, ref tMin, ref tMax)) return false;
            if (!UpdateSlabAxis(origin.Y, dir.Y, min.Y, max.Y, ref tMin, ref tMax)) return false;
            if (!UpdateSlabAxis(origin.Z, dir.Z, min.Z, max.Z, ref tMin, ref tMax)) return false;

            tEnter = tMin;
            return tMax >= tMin;
        }

        private static bool UpdateSlabAxis(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
        {
            if (MathF.Abs(dir) < 1e-7f)
            {
                return origin >= min && origin <= max;
            }

            float invD = 1.0f / dir;
            float t0 = (min - origin) * invD;
            float t1 = (max - origin) * invD;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            if (t0 > tMin) tMin = t0;
            if (t1 < tMax) tMax = t1;
            return tMax >= tMin;
        }
    }
}
