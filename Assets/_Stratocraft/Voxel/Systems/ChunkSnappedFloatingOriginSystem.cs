using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Birlesik (±256m Esikli, K*32m Adimli) Chunk-Snapped Floating Origin Sistemi (GAME LOGIC BIBLE v7.0 Bolum 2.6.A):
    /// 1) Gorsel &amp; Fizik Chunk Kaydirma (±256m Esigi, K*32m adim): Aktif chunk'larin kose tamponlarina dokunmadan
    ///    (0.0 ms yeniden meshleme!) yalnizca Transform konumlarini kaydirir ve 18.000m dis halkada bile
    ///    FP32 mantissa cozunurlugunu &lt;= 15.26 mikrometre (0.015 mm) duzeyinde tutar.
    /// 2) ShaderMacroPhaseOffset: UV-Free Biplanar UberShader'in kaydirma aninda sifir doku kaymasi (Zero Swim)
    ///    yasamasi icin _FloatingOriginMacroPhase mod-256m telafi vektorunu saglar.
    /// </summary>
    public sealed class ChunkSnappedFloatingOriginSystem
    {
        private const double MacroNoiseRepeatPeriodMeters = 256.0;

        /// <summary>Birikmis toplam gorsel orijin kaydirma ofseti (metre).</summary>
        public (double X, double Y, double Z) AccumulatedVisualShiftMeters { get; private set; }

        /// <summary>Aktif yerel fizik orijini (metre).</summary>
        public (double X, double Y, double Z) PhysicsOriginMeters { get; private set; }

        /// <summary>Shader _FloatingOriginMacroPhase icin [0, 256) araliginda sarilmis faz telafi ofseti.</summary>
        public (float X, float Y, float Z) ShaderMacroPhaseOffset { get; private set; }

        /// <summary>Floating origin kaydirmasi sirasinda tetiklenen mesh yeniden uretim sayisi (Her zaman 0 olmalidir!).</summary>
        public int MeshRebuildsTriggeredByShift { get; private set; }

        /// <summary>Toplam gerceklesen gorsel K*32m kaydirma sayisi.</summary>
        public int VisualShiftCount { get; private set; }

        /// <summary>Toplam gerceklesen ±256m fizik orijini kaydirma sayisi.</summary>
        public int PhysicsShiftCount { get; private set; }

        /// <summary>
        /// Kamera konumunu denetler; ±256m esigi asildiginda K*32m adimlarla sifir mesh yeniden uretimiyle
        /// (0 ms) koordinat kaydirmasini uygular.
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

            // 1. Fizik ve 240 Hz Kilic CCD Esigi (±256m -> 15.26 mikrometre FP32 hassasiyet garantisi)
            double worldCamX = AccumulatedVisualShiftMeters.X + localCameraPosition.X;
            double worldCamZ = AccumulatedVisualShiftMeters.Z + localCameraPosition.Z;
            double physDeltaX = worldCamX - PhysicsOriginMeters.X;
            double physDeltaZ = worldCamZ - PhysicsOriginMeters.Z;
            double physDistSq = physDeltaX * physDeltaX + physDeltaZ * physDeltaZ;

            double physThreshold = StratocraftConstants.PhysicsLocalOriginThresholdMeters;
            if (physDistSq >= physThreshold * physThreshold)
            {
                double snap = StratocraftConstants.VisualFloatingOriginSnapMeters;
                double shiftPhysX = Math.Floor(physDeltaX / snap) * snap;
                double shiftPhysZ = Math.Floor(physDeltaZ / snap) * snap;
                PhysicsOriginMeters = (
                    PhysicsOriginMeters.X + shiftPhysX,
                    PhysicsOriginMeters.Y,
                    PhysicsOriginMeters.Z + shiftPhysZ);
                PhysicsShiftCount++;
            }

            // 2. Gorsel Chunk-Snapped Floating Origin Esigi (±256m -> K * 32m kaydirma, 0 ms mesh rebuild)
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

                double nextShiftX = AccumulatedVisualShiftMeters.X + deltaShiftX;
                double nextShiftY = AccumulatedVisualShiftMeters.Y;
                double nextShiftZ = AccumulatedVisualShiftMeters.Z + deltaShiftZ;
                AccumulatedVisualShiftMeters = (nextShiftX, nextShiftY, nextShiftZ);

                ShaderMacroPhaseOffset = (
                    WrapModuloPositive(nextShiftX, MacroNoiseRepeatPeriodMeters),
                    WrapModuloPositive(nextShiftY, MacroNoiseRepeatPeriodMeters),
                    WrapModuloPositive(nextShiftZ, MacroNoiseRepeatPeriodMeters));

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

        private static float WrapModuloPositive(double value, double period)
        {
            double m = value % period;
            if (m < 0.0)
            {
                m += period;
            }
            return (float)m;
        }
    }
}
