using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stratocraft.Inventory.Data;

namespace Stratocraft.Inventory.Systems
{
    /// <summary>
    /// 3B Kumaş Çanta ve Görsel Sandıklar için GPU Instanced Grid Piramit Hesaplayıcısı (&lt; 0.04 ms).
    /// Yığın miktarına (1..64) göre eşyaları 3B piramit/küp diziliminde tek DrawMeshInstanced çağrısıyla çizer.
    /// </summary>
    public sealed class BackpackInstancedRenderer
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct InstanceTransformData
        {
            public float PosX, PosY, PosZ;
            public float Scale;
            public ushort ItemId;
            public byte PurityTier;
        }

        /// <summary>
        /// Verilen eşya yuvalarını 3B çanta/sandık ızgarası üzerinde instanced dönüşüm matrislerine dönüştürür.
        /// </summary>
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

                // Yığın büyüklüğüne göre 1..4 görsel katman (Grid Piramit temsili)
                int visualSubInstances = slot.Count switch
                {
                    >= 48 => 4,
                    >= 24 => 3,
                    >= 8 => 2,
                    _ => 1
                };

                for (int layer = 0; layer < visualSubInstances; layer++)
                {
                    if (written >= outputInstances.Length)
                    {
                        return written;
                    }

                    outputInstances[written++] = new InstanceTransformData
                    {
                        PosX = baseX,
                        PosY = layer * (cellSpacingMeters * 0.45f),
                        PosZ = baseZ,
                        Scale = 1.0f - (layer * 0.08f),
                        ItemId = slot.ItemId,
                        PurityTier = slot.PurityTier
                    };
                }
            }

            return written;
        }
    }
}
