#nullable enable
using System.Runtime.InteropServices;

namespace Stratocraft.Physics.Data
{
    /// <summary>
    /// Paket 4C-1: WorldGen deterministik arazi/biyom verisini Stratocraft.Physics icindeki
    /// RegionalAtmosphereSimulationSystem ve WorldHydrologyLedger'a 0-GC ile aktaran makro ornek.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct AtmosphereTerrainSample
    {
        public readonly float AltitudeAboveSeaMeters; // [0..5000] max(0, meanTopY - SeaLevelY)
        public readonly float OpenWaterFraction01;    // [0..1] acik deniz/okyanus orani
        public readonly float SurfaceAlbedo01;        // [0.04..0.85]
        public readonly float BiomeTempOffsetCelsius; // [-28..+26] iliman 18C bazina gore biyom sicaklik farki
        public readonly float SoilFraction01;         // [0..1] emici toprak orani
        public readonly float ImperviousFraction01;   // [0..1] gecirimsiz kaya/buz/kil kara orani
        public readonly float BasinConcavity01;       // [0..1] makro canak/vadi su tutma orani
        public readonly float MeanTopSurfaceWorldY;   // [0..255] ham dunya yuzey kotu
        public readonly byte DominantBiomeId;         // 0..11

        public AtmosphereTerrainSample(
            float altitudeAboveSeaMeters,
            float openWaterFraction01,
            float surfaceAlbedo01,
            float biomeTempOffsetCelsius,
            float soilFraction01,
            float imperviousFraction01,
            float basinConcavity01,
            float meanTopSurfaceWorldY,
            byte dominantBiomeId)
        {
            AltitudeAboveSeaMeters = altitudeAboveSeaMeters;
            OpenWaterFraction01 = openWaterFraction01;
            SurfaceAlbedo01 = surfaceAlbedo01;
            BiomeTempOffsetCelsius = biomeTempOffsetCelsius;
            SoilFraction01 = soilFraction01;
            ImperviousFraction01 = imperviousFraction01;
            BasinConcavity01 = basinConcavity01;
            MeanTopSurfaceWorldY = meanTopSurfaceWorldY;
            DominantBiomeId = dominantBiomeId;
        }
    }

    public delegate AtmosphereTerrainSample AtmosphereTerrainSamplerDelegate(
        float cellWorldCenterX,
        float cellWorldCenterZ,
        float cellSizeMeters);
}
