using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Cift Esikli (Dual-Threshold) Chunk-Snapped Floating Origin Sistemi (F-03, F-07).
    /// 1) Gorsel Chunk Kaydirma (±4.096m Esigi, K*32m adim): Aktif chunk'larin kose tamponlarina dokunmadan
    ///    (0.0 ms yeniden meshleme!) yalnizca Transform konumlarini kaydirir.
    /// 2) Fizik ve 240 Hz Kilic CCD Kaydirma (±256m Esigi): Yerel fizik orijinini 16 chunk'ta bir guncelleyerek
    ///    18.000m dis halkada bile FP32 mantissa cozunurlugunu ~15 mikrometre (0.015 mm) duzeyinde tutar.
    /// Bkz. GAME LOGIC BIBLE Bolum 2.6.A ve GELISTIRICI DOKUMANI Bolum 4.13.5.
    /// </summary>
    public sealed class ChunkSnappedFloatingOriginSystem
    {
        /// <summary>Birikmis toplam gorsel orijin kaydirma ofseti (metre).</summary>
        public (double X, double Y, double Z) AccumulatedVisualShiftMeters { get; private set; }

        /// <summary>Aktif yerel fizik orijini (metre).</summary>
        public (double X, double Y, double Z) PhysicsOriginMeters { get; private set; }

        /// <summary>Floating origin kaydirmasi sirasinda tetiklenen mesh yeniden uretim sayisi (Her zaman 0 olmalidir!).</summary>
        public int MeshRebuildsTriggeredByShift { get; private set; }

        /// <summary>Toplam gerceklesen gorsel K*32m kaydirma sayisi.</summary>
        public int VisualShiftCount { get; private set; }

        /// <summary>Toplam gerceklesen ±256m fizik orijini kaydirma sayisi.</summary>
        public int PhysicsShiftCount { get; private set; }

        /// <summary>
        /// Kamera konumunu denetler; ±4.096m gorsel esigi veya ±256m fizik esigi asildiginda
        /// sifir mesh yeniden uretimiyle (0 ms) koordinat kaydirmasini uygular.
        /// </summary>
        public bool UpdateFloatingOrigin(
            ref (float X, float Y, float Z) localCameraPosition,
            IReadOnlyList<VoxelChunkComponent> activeChunks)
        {
            if (activeChunks == null)
            {
                throw new ArgumentNullException(nameof(activeChunks));
            }

            bool shifted = false;

            // 1. Fizik ve 240 Hz Kilic CCD Esigi (±256m -> 15 mikrometre FP32 hassasiyet garantisi)
            double worldCamX = AccumulatedVisualShiftMeters.X + localCameraPosition.X;
            double worldCamZ = AccumulatedVisualShiftMeters.Z + localCameraPosition.Z;
            double physDeltaX = worldCamX - PhysicsOriginMeters.X;
            double physDeltaZ = worldCamZ - PhysicsOriginMeters.Z;
            double physDistSq = physDeltaX * physDeltaX + physDeltaZ * physDeltaZ;

            double physThreshold = StratocraftConstants.PhysicsLocalOriginThresholdMeters;
            if (physDistSq >= physThreshold * physThreshold)
            {
                double snap = StratocraftConstants.ChunkSizeX;
                double shiftPhysX = Math.Floor(physDeltaX / snap) * snap;
                double shiftPhysZ = Math.Floor(physDeltaZ / snap) * snap;
                PhysicsOriginMeters = (
                    PhysicsOriginMeters.X + shiftPhysX,
                    PhysicsOriginMeters.Y,
                    PhysicsOriginMeters.Z + shiftPhysZ);
                PhysicsShiftCount++;
            }

            // 2. Gorsel Chunk-Snapped Floating Origin Esigi (±4.096m -> K * 32m kaydirma, 0 ms mesh rebuild)
            float camDistXZSq = localCameraPosition.X * localCameraPosition.X
                              + localCameraPosition.Z * localCameraPosition.Z;
            float visualThreshold = StratocraftConstants.VisualFloatingOriginThresholdMeters;

            if (camDistXZSq >= visualThreshold * visualThreshold)
            {
                float snapMeters = StratocraftConstants.VisualFloatingOriginSnapMeters; // 32m
                float deltaShiftX = (float)(Math.Floor(localCameraPosition.X / snapMeters) * snapMeters);
                float deltaShiftZ = (float)(Math.Floor(localCameraPosition.Z / snapMeters) * snapMeters);

                localCameraPosition = (
                    localCameraPosition.X - deltaShiftX,
                    localCameraPosition.Y,
                    localCameraPosition.Z - deltaShiftZ);

                AccumulatedVisualShiftMeters = (
                    AccumulatedVisualShiftMeters.X + deltaShiftX,
                    AccumulatedVisualShiftMeters.Y,
                    AccumulatedVisualShiftMeters.Z + deltaShiftZ);

                for (int i = 0; i < activeChunks.Count; i++)
                {
                    VoxelChunkComponent chunk = activeChunks[i];
                    var pos = chunk.VisualWorldPosition;
                    // Yalnizca Transform konumu kaydirilir; DirtySubChunkMask DEGISTIRILMEZ (0 ms mesh rebuild!)
                    chunk.VisualWorldPosition = (pos.X - deltaShiftX, pos.Y, pos.Z - deltaShiftZ);
                }

                VisualShiftCount++;
                shifted = true;
            }

            return shifted;
        }
    }
}
