#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.WorldGen.Generators;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace Stratocraft.WorldGen
{
    /// <summary>
    /// Assets/Art/Generated/Flora/*.glb altindaki 4 gercek 3B organik agac taci ve kok mahmuzu varligi.
    /// </summary>
    public enum OrganicFloraKind : byte
    {
        NordicPineCanopy = 0,
        SnowPineCanopy = 1,
        AncientOakCanopy = 2,
        TreeRootButtress = 3
    }

    /// <summary>
    /// Agac dallarinin yapisal katman hiyerarsisi:
    /// Katman 0 (ThickPrimaryCarrier): Govde bileziginden cikan kalin tasiyici ana dallar (yuksek EI, uzerinde guvenle yurunur ve esner).
    /// Katman 1 (ThinOuterTerminal): Ana dallardan veya ust tactan ayrilan ince uc dallar (dusuk EI, agirlik binince belirgin bukulur ve kirilir).
    /// </summary>
    public enum OrganicTreeBranchTier : byte
    {
        ThickPrimaryCarrier = 0,
        ThinOuterTerminal = 1
    }

    /// <summary>
    /// Biyom, irtifa, ahsap malzemesi ve koordinat tohumuna gore belirlenen 7 farkli organik agac dal mimarisi.
    /// </summary>
    public enum OrganicTreeSpeciesKind : byte
    {
        AncientOak = 0,
        NordicPine = 1,
        SnowPine = 2,
        IronwoodGiant = 3,
        WeepingWillow = 4,
        SilverBirch = 5,
        CoastalPalm = 6
    }

    /// <summary>
    /// StratocraftFloatingIslandWorldGen tarafindan chunk basina deterministik uretilen tekil 3B agac yerlesim tanimi (0-GC).
    /// </summary>
    public readonly struct OrganicTreePlacementSpec
    {
        public readonly float WorldX;
        public readonly float BaseY;
        public readonly float CrownBaseY;
        public readonly float WorldZ;
        public readonly int TrunkHeight;
        public readonly int BiomeId;
        public readonly bool IsPine;
        public readonly byte WoodMaterialId;
        public readonly OrganicTreeSpeciesKind SpeciesKind;

        public OrganicTreePlacementSpec(
            float worldX,
            float crownBaseY,
            float worldZ,
            int trunkHeight,
            int biomeId,
            bool isPine)
        {
            int clampedTrunk = Math.Max(1, trunkHeight);
            WorldX = worldX;
            CrownBaseY = crownBaseY;
            BaseY = crownBaseY - clampedTrunk;
            WorldZ = worldZ;
            TrunkHeight = clampedTrunk;
            BiomeId = biomeId;
            IsPine = isPine;
            WoodMaterialId = ResolveDefaultWoodMaterialForBiome(biomeId, isIronwoodHint: false);
            SpeciesKind = OrganicTreeCanopyVisualSystem.SelectSpeciesForTree(
                biomeId, isPine, crownBaseY, WoodMaterialId, (int)MathF.Floor(worldX), (int)MathF.Floor(worldZ));
        }

        public OrganicTreePlacementSpec(
            float worldX,
            float baseY,
            float crownBaseY,
            float worldZ,
            int trunkHeight,
            int biomeId,
            bool isPine,
            byte woodMaterialId = 0,
            OrganicTreeSpeciesKind? explicitSpecies = null)
        {
            WorldX = worldX;
            BaseY = baseY;
            CrownBaseY = crownBaseY;
            WorldZ = worldZ;
            TrunkHeight = Math.Max(1, trunkHeight);
            BiomeId = biomeId;
            IsPine = isPine;
            WoodMaterialId = woodMaterialId != 0
                ? woodMaterialId
                : ResolveDefaultWoodMaterialForBiome(biomeId, isIronwoodHint: false);
            SpeciesKind = explicitSpecies ?? OrganicTreeCanopyVisualSystem.SelectSpeciesForTree(
                biomeId, isPine, crownBaseY, WoodMaterialId, (int)MathF.Floor(worldX), (int)MathF.Floor(worldZ));
        }

        private static byte ResolveDefaultWoodMaterialForBiome(int biomeId, bool isIronwoodHint)
        {
            if (isIronwoodHint || biomeId == 1) return MaterialIdCatalog.Ironwood;
            if (biomeId == 3) return MaterialIdCatalog.SpiritWood;
            return MaterialIdCatalog.OakWood;
        }
    }

    /// <summary>
    /// Sahnede aktif olan tekil organik 3B agac taci + kok mahmuzu + 2-katmanli dal hiyerarsisi dugumu (0-GC deger turu).
    /// </summary>
    public struct ActiveOrganicTreeNode
    {
        public int InstanceId;
        public int ChunkOwnerKey;
        public int VoxelX;
        public int BaseVoxelY;
        public int CrownVoxelY;
        public int VoxelZ;
        public int TrunkHeight;
        public int BiomeId;
        public bool IsPine;
        public byte WoodMaterialId;
        public OrganicTreeSpeciesKind SpeciesKind;
        public OrganicFloraKind CanopyKind;
        public float WorldX;
        public float BaseWorldY;
        public float CrownWorldY;
        public float TrunkCollarWorldY;
        public float WorldZ;
        public float YawDegrees;
        public float UniformScale;
        public int FirstBranchIndex;
        public int BranchCount;
        public int ThickPrimaryBranchCount;
        public int ThinOuterBranchCount;
        public int IntactBranchCount;
        public bool IsCanopyActive;
        public bool IsRootActive;
    }

    /// <summary>
    /// Euler-Bernoulli konsol kiris (cantilever beam) esneklik/bukulme (deflection) ve kirilma fizigine sahip
    /// tekil 3B agac dali dugumu (0-GC deger turu).
    /// </summary>
    public struct OrganicTreeBranchNode
    {
        public int BranchId;
        public int TreeInstanceId;
        public int TreeSlotIndex;
        public int LocalBranchIndex;
        public int ParentBranchLocalIndex;
        public float ParentAttachT01;
        public OrganicTreeSpeciesKind Species;
        public OrganicTreeBranchTier Tier;
        public byte WoodMaterialId;
        public float BaseAnchorWorldX;
        public float BaseAnchorWorldY;
        public float BaseAnchorWorldZ;
        public float AnchorWorldX;
        public float AnchorWorldY;
        public float AnchorWorldZ;
        public float RestTipWorldX;
        public float RestTipWorldY;
        public float RestTipWorldZ;
        public float CurrentTipWorldX;
        public float CurrentTipWorldY;
        public float CurrentTipWorldZ;
        public float DirX;
        public float DirY;
        public float DirZ;
        public float HorizontalLengthMeters;
        public float LengthMeters;
        public float StartRadiusMeters;
        public float EndRadiusMeters;
        public float FlexuralRigidityEI;
        public float MaxSafeLoadKg;
        public float BreakMomentThresholdNm;
        public float MaxElasticDeflectionMeters;
        public float PreloadMassKg;
        public float CurrentAppliedLoadKg;
        public float CurrentLoadPosition01;
        public float CurrentBendingMomentNm;
        public float CurrentTipDeflectionMeters;
        public float TargetTipDeflectionMeters;
        public float DeflectionVelocityMps;
        public float CurrentDeflectionAngleDeg;
        public byte DroppedWoodUnits;
        public bool IsBroken;
    }

    /// <summary>
    /// Asiri yuk veya ince dalda kirilma esigi asildiginda dusen fiziksel dal/odun parcasi olayi (0-GC).
    /// </summary>
    public readonly struct BrokenTreeBranchDropEvent
    {
        public readonly int TreeInstanceId;
        public readonly int BranchId;
        public readonly OrganicTreeSpeciesKind Species;
        public readonly OrganicTreeBranchTier Tier;
        public readonly byte WoodMaterialId;
        public readonly byte DroppedWoodCount;
        public readonly float WorldX;
        public readonly float WorldY;
        public readonly float WorldZ;
        public readonly float BranchLengthMeters;
        public readonly float BranchRadiusMeters;

        public BrokenTreeBranchDropEvent(
            int treeInstanceId,
            int branchId,
            OrganicTreeSpeciesKind species,
            OrganicTreeBranchTier tier,
            byte woodMaterialId,
            byte droppedWoodCount,
            float worldX,
            float worldY,
            float worldZ,
            float branchLengthMeters,
            float branchRadiusMeters)
        {
            TreeInstanceId = treeInstanceId;
            BranchId = branchId;
            Species = species;
            Tier = tier;
            WoodMaterialId = woodMaterialId;
            DroppedWoodCount = droppedWoodCount;
            WorldX = worldX;
            WorldY = worldY;
            WorldZ = worldZ;
            BranchLengthMeters = branchLengthMeters;
            BranchRadiusMeters = branchRadiusMeters;
        }
    }

    /// <summary>
    /// Flora/*.glb dosyasinin ikili glTF 2.0 baslik, POSITION/NORMAL/COLOR_0 ve 0 gomulu doku denetim raporu.
    /// </summary>
    public readonly struct FloraGlbAuditReport
    {
        public readonly bool ExistsOnDisk;
        public readonly bool HasValidGlbHeader;
        public readonly bool HasPositionAttribute;
        public readonly bool HasNormalAttribute;
        public readonly bool HasColor0Attribute;
        public readonly int EmbeddedTextureCount;
        public readonly int EmbeddedImageCount;
        public readonly long FileSizeBytes;
        public readonly string ResolvedPath;

        public FloraGlbAuditReport(
            bool existsOnDisk,
            bool hasValidGlbHeader,
            bool hasPositionAttribute,
            bool hasNormalAttribute,
            bool hasColor0Attribute,
            int embeddedTextureCount,
            int embeddedImageCount,
            long fileSizeBytes,
            string resolvedPath)
        {
            ExistsOnDisk = existsOnDisk;
            HasValidGlbHeader = hasValidGlbHeader;
            HasPositionAttribute = hasPositionAttribute;
            HasNormalAttribute = hasNormalAttribute;
            HasColor0Attribute = hasColor0Attribute;
            EmbeddedTextureCount = embeddedTextureCount;
            EmbeddedImageCount = embeddedImageCount;
            FileSizeBytes = fileSizeBytes;
            ResolvedPath = resolvedPath;
        }
    }

    /// <summary>
    /// Minecraft tipi 1m³ kup agac yapraklarini gercek 3B katmanli Nordik cam, karli cam, cok-kumeli mese taclari,
    /// 7 farkli agac turu mimarisi (Mese, Nordik Cam, Kar Cami, Demir Agaci, Salkim Sogut, Gumus Hus, Kiyi Palmiyesi),
    /// 2 ayri katmanli (Kalin Tasiyici Ana Dal + Ince Uc Dal) fiziksel basilabilir/bukulebilir/kirilabilir dallar
    /// ve egim telafili kok mahmuzlariyla degistiren 0-GC havuzlu sistem.
    /// Hem .NET 10 saf birim testlerinde hem de Unity 6 sahnesinde calisir.
    /// </summary>
    public sealed class OrganicTreeCanopyVisualSystem
    {
        public const int FloraAssetCount = 4;
        public const int MaxBranchesPerTree = 12;
        public const float VoxelTrunkOctagonalRadiusMeters = 0.38f;
        public const float TrunkCollarAnchorRadiusMeters = 0.34f;
        private const int MaxBrokenDropRingCapacity = 64;

        private static OrganicTreeCanopyVisualSystem? _activeInstance;

        /// <summary>Sahnedeki veya aktif testteki son olusturulan/baglanan agac-dal sistemi ornegi.</summary>
        public static OrganicTreeCanopyVisualSystem? ActiveInstance
        {
            get => _activeInstance;
            set => _activeInstance = value;
        }

        public OrganicTreeCanopyVisualSystem()
        {
            _activeInstance = this;
        }

        public static int PackChunkTreeKey(int chunkX, int chunkZ) =>
            0x20000000 | (((chunkX & 0x7FFF) << 15) | (chunkZ & 0x7FFF));

        public static string GetFloraGlbFileName(OrganicFloraKind kind) => kind switch
        {
            OrganicFloraKind.NordicPineCanopy => "flora_nordic_pine_canopy.glb",
            OrganicFloraKind.SnowPineCanopy => "flora_snow_pine_canopy.glb",
            OrganicFloraKind.AncientOakCanopy => "flora_ancient_oak_canopy.glb",
            OrganicFloraKind.TreeRootButtress => "flora_tree_root_buttress.glb",
            _ => "flora_nordic_pine_canopy.glb"
        };

        public static OrganicFloraKind SelectCanopyKindForTree(int biomeId, bool isPine, float crownBaseY)
        {
            bool snowy = biomeId == 4 || biomeId == 11 || crownBaseY >= 168.0f;
            if (snowy)
            {
                return OrganicFloraKind.SnowPineCanopy;
            }
            if (isPine || biomeId == 5)
            {
                return OrganicFloraKind.NordicPineCanopy;
            }
            return OrganicFloraKind.AncientOakCanopy;
        }

        /// <summary>
        /// Biyom, irtifa, ahsap malzemesi ve dunya koordinatina gore 7 farkli agac turu mimarisinden birini secer.
        /// </summary>
        public static OrganicTreeSpeciesKind SelectSpeciesForTree(
            int biomeId,
            bool isPine,
            float crownBaseY,
            byte woodMaterialId,
            int worldX = 0,
            int worldZ = 0)
        {
            if (biomeId == 4 || biomeId == 11 || crownBaseY >= 168.0f)
            {
                return OrganicTreeSpeciesKind.SnowPine;
            }
            if (isPine || biomeId == 5)
            {
                return OrganicTreeSpeciesKind.NordicPine;
            }
            if (biomeId == 3 || woodMaterialId == MaterialIdCatalog.SpiritWood)
            {
                return OrganicTreeSpeciesKind.WeepingWillow;
            }
            if (biomeId == 1 || woodMaterialId == MaterialIdCatalog.Ironwood)
            {
                return OrganicTreeSpeciesKind.IronwoodGiant;
            }
            if (biomeId == 6 || biomeId == 10 || (crownBaseY <= StratocraftFloatingIslandWorldGen.SeaLevelY + 3.2f && (Math.Abs(worldX) + Math.Abs(worldZ)) > 36))
            {
                return OrganicTreeSpeciesKind.CoastalPalm;
            }

            uint hash = (uint)((worldX * 73856093) ^ (worldZ * 19349663) ^ (biomeId * 83492791));
            if (biomeId == 2 || (biomeId == 0 && (hash % 5u) == 3u))
            {
                return OrganicTreeSpeciesKind.SilverBirch;
            }
            return OrganicTreeSpeciesKind.AncientOak;
        }

        /// <summary>
        /// Euler-Bernoulli konsol kiris sekil fonksiyonu: phi(t) = (3*t^2 - t^3) / 2, t in [0..1].
        /// t = 0 (govde ankastre koku) -> 0.0; t = 1 (dal ucu) -> 1.0.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static float EvaluateCantileverDeflectionShape01(float t01)
        {
            float t = Math.Clamp(t01, 0f, 1f);
            return 0.5f * t * t * (3.0f - t);
        }

        /// <summary>
        /// Sabit kodlu mutlak yol kullanmadan goreli ve ust-dizin aramasiyla Flora/*.glb yolunu cozer.
        /// </summary>
        public static string ResolveFloraGlbDiskPath(OrganicFloraKind kind, string? unityDataPath = null)
        {
            string fileName = GetFloraGlbFileName(kind);
            if (!string.IsNullOrEmpty(unityDataPath))
            {
                string viaData = Path.Combine(unityDataPath, "Art", "Generated", "Flora", fileName);
                if (File.Exists(viaData)) return viaData;
            }

            string relFromCwd = Path.Combine("Assets", "Art", "Generated", "Flora", fileName);
            if (File.Exists(relFromCwd)) return Path.GetFullPath(relFromCwd);

            string? probeDir = Directory.GetCurrentDirectory();
            for (int depth = 0; depth < 7 && !string.IsNullOrEmpty(probeDir); depth++)
            {
                string candidate = Path.Combine(probeDir, "Assets", "Art", "Generated", "Flora", fileName);
                if (File.Exists(candidate)) return candidate;
                probeDir = Directory.GetParent(probeDir)?.FullName;
            }

            probeDir = AppContext.BaseDirectory;
            for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(probeDir); depth++)
            {
                string candidate = Path.Combine(probeDir, "Assets", "Art", "Generated", "Flora", fileName);
                if (File.Exists(candidate)) return candidate;
                probeDir = Directory.GetParent(probeDir)?.FullName;
            }

            return !string.IsNullOrEmpty(unityDataPath)
                ? Path.Combine(unityDataPath, "Art", "Generated", "Flora", fileName)
                : relFromCwd;
        }

        public static FloraGlbAuditReport AuditFloraGlbFile(OrganicFloraKind kind, string? unityDataPath = null)
        {
            string path = ResolveFloraGlbDiskPath(kind, unityDataPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new FloraGlbAuditReport(false, false, false, false, false, -1, -1, 0L, path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 28 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67u)
            {
                return new FloraGlbAuditReport(true, false, false, false, false, -1, -1, bytes.Length, path);
            }

            if (BitConverter.ToUInt32(bytes, 4) != 2u)
            {
                return new FloraGlbAuditReport(true, false, false, false, false, -1, -1, bytes.Length, path);
            }

            int offset = 12;
            string? jsonText = null;
            bool hasBin = false;
            while (offset + 8 <= bytes.Length)
            {
                int chunkLen = BitConverter.ToInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (chunkLen < 0 || dataStart + chunkLen > bytes.Length) break;

                if (chunkType == 0x4E4F534Au)
                {
                    jsonText = Encoding.UTF8.GetString(bytes, dataStart, chunkLen);
                }
                else if (chunkType == 0x004E4942u && chunkLen > 0)
                {
                    hasBin = true;
                }
                offset = dataStart + chunkLen;
            }

            if (string.IsNullOrEmpty(jsonText) || !hasBin)
            {
                return new FloraGlbAuditReport(true, false, false, false, false, -1, -1, bytes.Length, path);
            }

            bool hasPos = jsonText.Contains("\"POSITION\"", StringComparison.Ordinal);
            bool hasNrm = jsonText.Contains("\"NORMAL\"", StringComparison.Ordinal);
            bool hasCol0 = jsonText.Contains("\"COLOR_0\"", StringComparison.Ordinal);
            int texCount = CountJsonArrayElements(jsonText, "\"textures\"");
            int imgCount = CountJsonArrayElements(jsonText, "\"images\"");

            return new FloraGlbAuditReport(
                existsOnDisk: true,
                hasValidGlbHeader: true,
                hasPositionAttribute: hasPos,
                hasNormalAttribute: hasNrm,
                hasColor0Attribute: hasCol0,
                embeddedTextureCount: texCount,
                embeddedImageCount: imgCount,
                fileSizeBytes: bytes.Length,
                resolvedPath: path);
        }

        private static int CountJsonArrayElements(string json, string keyToken)
        {
            int idx = json.IndexOf(keyToken, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int openBracket = json.IndexOf('[', idx + keyToken.Length);
            if (openBracket < 0) return 0;

            int depth = 0;
            int objectCount = 0;
            for (int i = openBracket; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) break;
                }
                else if (c == '{' && depth == 1)
                {
                    objectCount++;
                }
            }
            return objectCount;
        }

        /// <summary>
        /// Canli voksel chunk'indan veya WorldGen deterministik agac yerlesim kuralindan chunk icindeki
        /// agaclarin (WorldX, BaseY, CrownBaseY, WorldZ, TrunkHeight, BiomeId, IsPine, WoodMaterialId, SpeciesKind) listesini cikarir (0 B GC).
        /// </summary>
        public static int ResolveDeterministicChunkTreeSpecs(
            int chunkX,
            int chunkZ,
            Span<OrganicTreePlacementSpec> outSpecs,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            if (outSpecs.Length == 0) return 0;

            if (worldChunks != null &&
                worldChunks.TryGetValue((chunkX, chunkZ), out uint[]? chunkVoxels) &&
                chunkVoxels != null)
            {
                return ExtractTreeSpecsFromChunkVoxels(chunkX, chunkZ, chunkVoxels, outSpecs);
            }

            int writeCount = 0;
            int seed = StratocraftFloatingIslandWorldGen.ActiveWorldSeed;
            int seedShiftX = seed == 1453 ? 0 : ((seed * 17) % 3) - 1;
            int seedShiftZ = seed == 1453 ? 0 : ((seed * 31) % 3) - 1;

            if (chunkX == -1 && chunkZ == -1)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(5 + seedShiftZ, 3, 7), 6, isIronwood: false, outSpecs, ref writeCount);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(11 + seedShiftZ, 10, 12), 7, isIronwood: false, outSpecs, ref writeCount);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(11 + seedShiftZ, 10, 12), 8, isIronwood: true, outSpecs, ref writeCount);
            }
            else if (chunkX == -1 && chunkZ == 0)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(3 + seedShiftZ, 3, 5), 9, isIronwood: true, outSpecs, ref writeCount);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 5), Math.Clamp(12 + seedShiftZ, 11, 12), 7, isIronwood: false, outSpecs, ref writeCount);
            }
            else if (chunkX == 1 && chunkZ == -1)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(12 + seedShiftZ, 10, 12), 7, isIronwood: false, outSpecs, ref writeCount);
            }
            else if (chunkX == 1 && chunkZ == 0)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(4 + seedShiftZ, 3, 6), 7, isIronwood: false, outSpecs, ref writeCount);
            }
            else
            {
                int h1 = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ seed);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, 3 + (h1 % 4), 3 + ((h1 >> 4) % 4), 6 + (h1 % 5), isIronwood: (h1 & 2) != 0, outSpecs, ref writeCount);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, 9 + ((h1 >> 8) % 4), 9 + ((h1 >> 12) % 4), 6 + ((h1 >> 3) % 5), isIronwood: (h1 & 4) != 0, outSpecs, ref writeCount);
            }

            return writeCount;
        }

        private static void TryAppendDeterministicCandidate(
            int chunkX,
            int chunkZ,
            int lx,
            int lz,
            int trunkHeight,
            bool isIronwood,
            Span<OrganicTreePlacementSpec> outSpecs,
            ref int writeCount)
        {
            if (writeCount >= outSpecs.Length) return;
            if (SettlementLayoutSystem.IsColumnReservedBySettlement((chunkX << 4) + lx, (chunkZ << 4) + lz)) return;

            if (!StratocraftFloatingIslandWorldGen.TryResolveNaturalTreeCandidate(
                    chunkX, chunkZ, lx, lz, trunkHeight, isIronwood,
                    out int wx, out int wz, out int baseY, out int effectiveTrunkH,
                    out int biomeId, out byte woodMat, out bool isPine))
            {
                return;
            }

            if (SettlementLayoutSystem.IsColumnReservedBySettlement(wx, wz)) return;

            outSpecs[writeCount++] = new OrganicTreePlacementSpec(
                worldX: wx + 0.5f,
                baseY: baseY,
                crownBaseY: baseY + effectiveTrunkH,
                worldZ: wz + 0.5f,
                trunkHeight: effectiveTrunkH,
                biomeId: biomeId,
                isPine: isPine,
                woodMaterialId: woodMat);
        }

        private static int ExtractTreeSpecsFromChunkVoxels(
            int chunkX,
            int chunkZ,
            uint[] voxels,
            Span<OrganicTreePlacementSpec> outSpecs)
        {
            int writeCount = 0;
            int maxY = Math.Min(StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 1, (voxels.Length >> 8) - 1);

            for (int lz = 2; lz <= 13 && writeCount < outSpecs.Length; lz++)
            {
                int colWz = (chunkZ << 4) + lz;
                for (int lx = 2; lx <= 13 && writeCount < outSpecs.Length; lx++)
                {
                    if (SettlementLayoutSystem.IsColumnReservedBySettlement((chunkX << 4) + lx, colWz))
                    {
                        continue;
                    }
                    int col = (lz << 4) | lx;
                    int minTrunkY = -1;
                    int maxTrunkY = -1;
                    byte trunkWoodMat = MaterialIdCatalog.OakWood;

                    for (int y = 12; y <= maxY; y++)
                    {
                        uint raw = voxels[(y << 8) | col];
                        if (raw == 0u) continue;
                        if (VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.DogalKutle) continue;
                        if (VoxelBitUtility.GetShapeId(raw) != 2) continue; // OctagonalColumn agac govdesi

                        byte mat = VoxelBitUtility.GetMaterialId(raw);
                        if (mat == MaterialIdCatalog.OakWood ||
                            mat == MaterialIdCatalog.Ironwood ||
                            mat == MaterialIdCatalog.SpiritWood)
                        {
                            if (minTrunkY < 0)
                            {
                                minTrunkY = y;
                                trunkWoodMat = mat;
                            }
                            maxTrunkY = y;
                        }
                    }

                    int spanH = maxTrunkY - minTrunkY;
                    if (minTrunkY < 0 || spanH < 4) continue;

                    int wx = chunkX * 16 + lx;
                    int wz = chunkZ * 16 + lz;
                    int biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
                    bool isPine = biomeId == 4 || biomeId == 5;

                    outSpecs[writeCount++] = new OrganicTreePlacementSpec(
                        worldX: wx + 0.5f,
                        baseY: minTrunkY,
                        crownBaseY: maxTrunkY,
                        worldZ: wz + 0.5f,
                        trunkHeight: spanH,
                        biomeId: biomeId,
                        isPine: isPine,
                        woodMaterialId: trunkWoodMat);
                }
            }

            return writeCount;
        }

        private static void ComputeDeterministicVariation(
            int wx,
            int wz,
            int trunkHeight,
            out float yawDeg,
            out float uniformScale)
        {
            uint h = (uint)((wx * 73856093) ^ (wz * 19349663) ^ (trunkHeight * 83492791));
            yawDeg = (h & 1023u) * (360.0f / 1024.0f);
            float trunkFactor = Math.Clamp(0.92f + (trunkHeight - 5) * 0.038f, 0.90f, 1.15f);
            float jitter = 0.96f + (((h >> 10) & 255u) / 255.0f) * 0.08f;
            uniformScale = trunkFactor * jitter;
        }

        /// <summary>
        /// Agac turune ve ahsap malzemesine gore 2 katmanli (ThickPrimaryCarrier + ThinOuterTerminal)
        /// fiziksel dal agini deterministik olarak uretir (0 B GC).
        /// Tum ana dallar (ThickPrimaryCarrier) govde bilezigine (r = 0.34m <= VoxelTrunkOctagonalRadius 0.38m)
        /// sifir boslukla kenetlenir; ince uc dallar (ThinOuterTerminal) ise ana dallarin orta/dis bogumlarindan ayrilir.
        /// </summary>
        public static int GenerateSpeciesBranchHierarchyForTree(
            in ActiveOrganicTreeNode tree,
            int treeSlotIndex,
            int firstGlobalBranchId,
            Span<OrganicTreeBranchNode> outBranches,
            out int thickPrimaryCount,
            out int thinOuterCount)
        {
            thickPrimaryCount = 0;
            thinOuterCount = 0;
            if (outBranches.Length < 4) return 0;

            OrganicTreeSpeciesKind species = tree.SpeciesKind;
            byte woodMat = tree.WoodMaterialId != 0 ? tree.WoodMaterialId : MaterialIdCatalog.OakWood;
            float scale = Math.Clamp(tree.UniformScale, 0.88f, 1.18f);
            float baseYawRad = tree.YawDegrees * (MathF.PI / 180.0f);

            // Ahsap turune gore elastisite modulu E ve kirilma dayanimi carpani
            float speciesStiffnessMult = species switch
            {
                OrganicTreeSpeciesKind.IronwoodGiant => 1.78f,
                OrganicTreeSpeciesKind.AncientOak => 1.15f,
                OrganicTreeSpeciesKind.NordicPine => 0.96f,
                OrganicTreeSpeciesKind.SnowPine => 0.92f,
                OrganicTreeSpeciesKind.SilverBirch => 0.86f,
                OrganicTreeSpeciesKind.CoastalPalm => 0.78f,
                OrganicTreeSpeciesKind.WeepingWillow => 0.64f,
                _ => 1.0f
            };
            if (woodMat == MaterialIdCatalog.Ironwood && speciesStiffnessMult < 1.55f)
            {
                speciesStiffnessMult = 1.65f;
            }
            else if (woodMat == MaterialIdCatalog.SpiritWood && species != OrganicTreeSpeciesKind.WeepingWillow)
            {
                speciesStiffnessMult *= 0.82f;
            }

            float speciesStrengthMult = species switch
            {
                OrganicTreeSpeciesKind.IronwoodGiant => 1.85f,
                OrganicTreeSpeciesKind.AncientOak => 1.18f,
                OrganicTreeSpeciesKind.NordicPine => 1.00f,
                OrganicTreeSpeciesKind.SnowPine => 0.88f, // Soguktan kirilgan
                OrganicTreeSpeciesKind.SilverBirch => 0.84f,
                OrganicTreeSpeciesKind.CoastalPalm => 0.86f,
                OrganicTreeSpeciesKind.WeepingWillow => 0.82f,
                _ => 1.0f
            };

            // Tur bazli ana dal (Katman 0 - ThickPrimaryCarrier) ve ince uc dal (Katman 1 - ThinOuterTerminal) mimari parametreleri
            int primaryTargetCount;
            float primaryBaseReach, primaryStartR, primaryEndR, primaryElevSin, primaryPreloadKg;
            float thinBaseLength, thinStartR, thinEndR, thinElevSin, thinSpreadAngleRad, thinPreloadKg;

            switch (species)
            {
                case OrganicTreeSpeciesKind.AncientOak:
                    primaryTargetCount = 5;
                    primaryBaseReach = 2.45f * scale;
                    primaryStartR = 0.225f;
                    primaryEndR = 0.140f;
                    primaryElevSin = 0.24f;
                    primaryPreloadKg = 8.0f;
                    thinBaseLength = 1.65f * scale;
                    thinStartR = 0.072f;
                    thinEndR = 0.034f;
                    thinElevSin = 0.14f;
                    thinSpreadAngleRad = 0.54f;
                    thinPreloadKg = 3.0f;
                    break;

                case OrganicTreeSpeciesKind.NordicPine:
                    primaryTargetCount = 5;
                    primaryBaseReach = 2.20f * scale;
                    primaryStartR = 0.178f;
                    primaryEndR = 0.112f;
                    primaryElevSin = -0.05f;
                    primaryPreloadKg = 6.5f;
                    thinBaseLength = 1.45f * scale;
                    thinStartR = 0.062f;
                    thinEndR = 0.028f;
                    thinElevSin = -0.14f;
                    thinSpreadAngleRad = 0.48f;
                    thinPreloadKg = 2.5f;
                    break;

                case OrganicTreeSpeciesKind.SnowPine:
                    primaryTargetCount = 5;
                    primaryBaseReach = 2.15f * scale;
                    primaryStartR = 0.184f;
                    primaryEndR = 0.116f;
                    primaryElevSin = -0.14f;
                    primaryPreloadKg = 22.0f; // Kar yuku on-egilmesi
                    thinBaseLength = 1.40f * scale;
                    thinStartR = 0.058f;
                    thinEndR = 0.026f;
                    thinElevSin = -0.22f;
                    thinSpreadAngleRad = 0.46f;
                    thinPreloadKg = 11.5f; // Uclarda biriken kar yuku
                    break;

                case OrganicTreeSpeciesKind.IronwoodGiant:
                    primaryTargetCount = 5;
                    primaryBaseReach = 2.65f * scale;
                    primaryStartR = 0.250f;
                    primaryEndR = 0.162f;
                    primaryElevSin = 0.36f;
                    primaryPreloadKg = 10.0f;
                    thinBaseLength = 1.75f * scale;
                    thinStartR = 0.080f;
                    thinEndR = 0.040f;
                    thinElevSin = 0.22f;
                    thinSpreadAngleRad = 0.50f;
                    thinPreloadKg = 3.5f;
                    break;

                case OrganicTreeSpeciesKind.WeepingWillow:
                    primaryTargetCount = 4;
                    primaryBaseReach = 2.50f * scale;
                    primaryStartR = 0.195f;
                    primaryEndR = 0.122f;
                    primaryElevSin = 0.18f;
                    primaryPreloadKg = 9.0f;
                    thinBaseLength = 2.05f * scale;
                    thinStartR = 0.054f;
                    thinEndR = 0.022f;
                    thinElevSin = -0.44f; // Sarkik salkim sogut uc dallari
                    thinSpreadAngleRad = 0.38f;
                    thinPreloadKg = 4.5f;
                    break;

                case OrganicTreeSpeciesKind.SilverBirch:
                    primaryTargetCount = 4;
                    primaryBaseReach = 2.05f * scale;
                    primaryStartR = 0.162f;
                    primaryEndR = 0.102f;
                    primaryElevSin = 0.42f;
                    primaryPreloadKg = 5.0f;
                    thinBaseLength = 1.45f * scale;
                    thinStartR = 0.054f;
                    thinEndR = 0.024f;
                    thinElevSin = 0.18f;
                    thinSpreadAngleRad = 0.44f;
                    thinPreloadKg = 2.0f;
                    break;

                case OrganicTreeSpeciesKind.CoastalPalm:
                default:
                    primaryTargetCount = 4;
                    primaryBaseReach = 2.25f * scale;
                    primaryStartR = 0.168f;
                    primaryEndR = 0.106f;
                    primaryElevSin = 0.12f;
                    primaryPreloadKg = 7.0f;
                    thinBaseLength = 1.75f * scale;
                    thinStartR = 0.056f;
                    thinEndR = 0.024f;
                    thinElevSin = -0.28f;
                    thinSpreadAngleRad = 0.32f;
                    thinPreloadKg = 3.0f;
                    break;
            }

            int totalWritten = 0;
            int maxCap = Math.Min(outBranches.Length, MaxBranchesPerTree);
            primaryTargetCount = Math.Min(primaryTargetCount, maxCap / 2);

            float trunkSpan = Math.Max(4.0f, tree.CrownWorldY - tree.BaseWorldY);

            // =========================================================================
            // KATMAN 1: Kalin Tasiyici Ana Dallar (OrganicTreeBranchTier.ThickPrimaryCarrier)
            // =========================================================================
            for (int p = 0; p < primaryTargetCount && totalWritten < maxCap; p++)
            {
                float goldenStep = species is OrganicTreeSpeciesKind.NordicPine or OrganicTreeSpeciesKind.SnowPine
                    ? p * 2.3999632f
                    : (2.0f * MathF.PI * p) / primaryTargetCount + ((p & 1) * 0.14f);
                float azimuth = baseYawRad + goldenStep;
                float dirXZ_X = MathF.Cos(azimuth);
                float dirXZ_Z = MathF.Sin(azimuth);

                // Ilk tasiyici ana dal agaca tirmanilabilmesi icin govdenin orta-ust kotundan,
                // diger ana dallar ise CrownWorldY govde-tac bileziginden (%100 voksel govde icinden) cikar!
                float attachY;
                if (species == OrganicTreeSpeciesKind.CoastalPalm)
                {
                    attachY = tree.CrownWorldY + 0.15f + (p * 0.16f);
                }
                else if (p == 0)
                {
                    attachY = tree.BaseWorldY + MathF.Max(1.85f, trunkSpan * 0.45f);
                }
                else if (p == 1)
                {
                    attachY = tree.BaseWorldY + MathF.Max(2.75f, trunkSpan * 0.68f);
                }
                else
                {
                    attachY = tree.CrownWorldY - 0.55f + ((p - 2) * 0.48f);
                }

                // Govde bilezigi kenetlenmesi: Anchor noktasini voksel govde yaricapi (0.38m) icinde r=0.34m'ye yerlestir
                float anchorX = tree.WorldX + dirXZ_X * TrunkCollarAnchorRadiusMeters;
                float anchorY = attachY;
                float anchorZ = tree.WorldZ + dirXZ_Z * TrunkCollarAnchorRadiusMeters;

                float reachVar = primaryBaseReach * (0.94f + 0.06f * (p % 3));
                float dy = reachVar * primaryElevSin;
                float tipX = anchorX + dirXZ_X * reachVar;
                float tipY = anchorY + dy;
                float tipZ = anchorZ + dirXZ_Z * reachVar;

                float r0 = primaryStartR * (1.0f - p * 0.035f);
                float r1 = primaryEndR * (1.0f - p * 0.035f);

                outBranches[totalWritten] = CreateBranchNode(
                    branchId: firstGlobalBranchId + totalWritten,
                    treeInstanceId: tree.InstanceId,
                    treeSlotIndex: treeSlotIndex,
                    localBranchIndex: totalWritten,
                    parentBranchLocalIndex: -1,
                    parentAttachT01: 0f,
                    species: species,
                    tier: OrganicTreeBranchTier.ThickPrimaryCarrier,
                    woodMat: woodMat,
                    anchorX: anchorX,
                    anchorY: anchorY,
                    anchorZ: anchorZ,
                    tipX: tipX,
                    tipY: tipY,
                    tipZ: tipZ,
                    startR: r0,
                    endR: r1,
                    stiffnessMult: speciesStiffnessMult,
                    strengthMult: speciesStrengthMult,
                    preloadKg: primaryPreloadKg);

                totalWritten++;
                thickPrimaryCount++;
            }

            // =========================================================================
            // KATMAN 2: Ince Catalli ve Uc Dallar (OrganicTreeBranchTier.ThinOuterTerminal)
            // =========================================================================
            int thinSlotsRemaining = maxCap - totalWritten;
            for (int tIdx = 0; tIdx < thinSlotsRemaining; tIdx++)
            {
                int parentIdx = tIdx % thickPrimaryCount;
                ref readonly OrganicTreeBranchNode parent = ref outBranches[parentIdx];

                float attachT = tIdx < thickPrimaryCount ? 0.72f : 0.88f;
                float sideSign = (tIdx % 2 == 0) ? 1.0f : -1.0f;
                float parentAzimuth = MathF.Atan2(parent.DirZ, parent.DirX);
                float childAzimuth = parentAzimuth + sideSign * thinSpreadAngleRad * (tIdx < thickPrimaryCount ? 1.0f : 0.65f);

                float cdx = MathF.Cos(childAzimuth);
                float cdz = MathF.Sin(childAzimuth);

                float pDeflectAtAttach = parent.CurrentTipDeflectionMeters * EvaluateCantileverDeflectionShape01(attachT);
                float anchorX = parent.AnchorWorldX + (parent.RestTipWorldX - parent.AnchorWorldX) * attachT;
                float anchorY = parent.AnchorWorldY + (parent.RestTipWorldY - parent.AnchorWorldY) * attachT - pDeflectAtAttach;
                float anchorZ = parent.AnchorWorldZ + (parent.RestTipWorldZ - parent.AnchorWorldZ) * attachT;

                float horizLen = thinBaseLength * (0.90f + 0.08f * (tIdx % 3));
                float cdy = horizLen * thinElevSin;
                float tipX = anchorX + cdx * horizLen;
                float tipY = anchorY + cdy;
                float tipZ = anchorZ + cdz * horizLen;

                float r0 = thinStartR * (tIdx < thickPrimaryCount ? 1.0f : 0.86f);
                float r1 = thinEndR * (tIdx < thickPrimaryCount ? 1.0f : 0.84f);

                outBranches[totalWritten] = CreateBranchNode(
                    branchId: firstGlobalBranchId + totalWritten,
                    treeInstanceId: tree.InstanceId,
                    treeSlotIndex: treeSlotIndex,
                    localBranchIndex: totalWritten,
                    parentBranchLocalIndex: parentIdx,
                    parentAttachT01: attachT,
                    species: species,
                    tier: OrganicTreeBranchTier.ThinOuterTerminal,
                    woodMat: woodMat,
                    anchorX: anchorX,
                    anchorY: anchorY,
                    anchorZ: anchorZ,
                    tipX: tipX,
                    tipY: tipY,
                    tipZ: tipZ,
                    startR: r0,
                    endR: r1,
                    stiffnessMult: speciesStiffnessMult,
                    strengthMult: speciesStrengthMult,
                    preloadKg: thinPreloadKg);

                totalWritten++;
                thinOuterCount++;
            }

            return totalWritten;
        }

        private static OrganicTreeBranchNode CreateBranchNode(
            int branchId,
            int treeInstanceId,
            int treeSlotIndex,
            int localBranchIndex,
            int parentBranchLocalIndex,
            float parentAttachT01,
            OrganicTreeSpeciesKind species,
            OrganicTreeBranchTier tier,
            byte woodMat,
            float anchorX,
            float anchorY,
            float anchorZ,
            float tipX,
            float tipY,
            float tipZ,
            float startR,
            float endR,
            float stiffnessMult,
            float strengthMult,
            float preloadKg)
        {
            float dx = tipX - anchorX;
            float dy = tipY - anchorY;
            float dz = tipZ - anchorZ;
            float horizLen = MathF.Max(0.25f, MathF.Sqrt(dx * dx + dz * dz));
            float fullLen = MathF.Max(0.35f, MathF.Sqrt(dx * dx + dy * dy + dz * dz));
            float invLen = 1.0f / fullLen;

            // Efektif kesit yaricapi ve atalet momenti I = (pi * r_eff^4) / 4
            float rEff = 0.65f * startR + 0.35f * endR;
            float areaMomentI = (MathF.PI * 0.25f) * (rEff * rEff * rEff * rEff);

            // Efektif Young Modulu (N/m^2): Kalin dallarda ~0.04m..0.22m, ince dallarda ~0.18m..0.55m esneme verir
            float effectiveYoungsModulus = 2.85e7f * stiffnessMult;
            float rigidityEI = MathF.Max(85.0f, effectiveYoungsModulus * areaMomentI);

            // Kirilma dayanimi: Kalin tasiyici ana dallar (185..420 kg), ince uc dallar (35..72 kg)
            float maxSafeLoadKg;
            float maxElasticDeflection;
            byte woodDrops;
            if (tier == OrganicTreeBranchTier.ThickPrimaryCarrier)
            {
                float radiusRatio = startR / 0.20f;
                maxSafeLoadKg = Math.Clamp(225.0f * (radiusRatio * radiusRatio) * strengthMult, 165.0f, 460.0f);
                maxElasticDeflection = Math.Clamp(fullLen * 0.24f, 0.34f, 0.72f);
                woodDrops = (byte)Math.Clamp((int)MathF.Round(fullLen * 1.15f), 2, 4);
            }
            else
            {
                float radiusRatio = startR / 0.065f;
                maxSafeLoadKg = Math.Clamp(52.0f * (radiusRatio * radiusRatio) * strengthMult, 32.0f, 74.0f);
                maxElasticDeflection = Math.Clamp(fullLen * 0.32f, 0.26f, 0.62f);
                woodDrops = (byte)Math.Clamp((int)MathF.Round(fullLen * 0.65f), 1, 2);
            }

            float refLeverArm = fullLen * 0.72f;
            float breakMomentNm = (maxSafeLoadKg + preloadKg) * 9.81f * refLeverArm;

            // Dogal yaprak/kar on-yuklemesinden kaynaklanan baslangic statik esnemesi
            float preloadForceN = preloadKg * 9.81f;
            float preloadArm = fullLen * 0.65f;
            float initDeflection = Math.Clamp(
                (preloadForceN * preloadArm * preloadArm * (3.0f * fullLen - preloadArm)) / (6.0f * rigidityEI),
                0.0f,
                maxElasticDeflection * 0.35f);
            float initAngleDeg = MathF.Atan2(initDeflection, fullLen) * (180.0f / MathF.PI);

            return new OrganicTreeBranchNode
            {
                BranchId = branchId,
                TreeInstanceId = treeInstanceId,
                TreeSlotIndex = treeSlotIndex,
                LocalBranchIndex = localBranchIndex,
                ParentBranchLocalIndex = parentBranchLocalIndex,
                ParentAttachT01 = parentAttachT01,
                Species = species,
                Tier = tier,
                WoodMaterialId = woodMat,
                BaseAnchorWorldX = anchorX,
                BaseAnchorWorldY = anchorY,
                BaseAnchorWorldZ = anchorZ,
                AnchorWorldX = anchorX,
                AnchorWorldY = anchorY,
                AnchorWorldZ = anchorZ,
                RestTipWorldX = tipX,
                RestTipWorldY = tipY,
                RestTipWorldZ = tipZ,
                CurrentTipWorldX = tipX,
                CurrentTipWorldY = tipY - initDeflection,
                CurrentTipWorldZ = tipZ,
                DirX = dx * invLen,
                DirY = dy * invLen,
                DirZ = dz * invLen,
                HorizontalLengthMeters = horizLen,
                LengthMeters = fullLen,
                StartRadiusMeters = startR,
                EndRadiusMeters = endR,
                FlexuralRigidityEI = rigidityEI,
                MaxSafeLoadKg = maxSafeLoadKg,
                BreakMomentThresholdNm = breakMomentNm,
                MaxElasticDeflectionMeters = maxElasticDeflection,
                PreloadMassKg = preloadKg,
                CurrentAppliedLoadKg = 0f,
                CurrentLoadPosition01 = 0f,
                CurrentBendingMomentNm = preloadForceN * preloadArm,
                CurrentTipDeflectionMeters = initDeflection,
                TargetTipDeflectionMeters = initDeflection,
                DeflectionVelocityMps = 0f,
                CurrentDeflectionAngleDeg = initAngleDeg,
                DroppedWoodUnits = woodDrops,
                IsBroken = false
            };
        }

        // =========================================================================================
        // ORTAK 0-GC AGAC + DAL FIZIK BELLEGI VE METOTLARI (.NET 10 & UNITY 6)
        // =========================================================================================

        private ActiveOrganicTreeNode[] _activeTrees = new ActiveOrganicTreeNode[256];
        private OrganicTreeBranchNode[] _activeBranches = new OrganicTreeBranchNode[256 * MaxBranchesPerTree];
        private readonly BrokenTreeBranchDropEvent[] _brokenBranchDrops = new BrokenTreeBranchDropEvent[MaxBrokenDropRingCapacity];
        private readonly OrganicTreePlacementSpec[] _chunkTreeScratch = new OrganicTreePlacementSpec[12];
        private readonly HashSet<int> _spawnedChunkKeys = new(128);
        private int _activeTreeCount;
        private int _nextInstanceId = 1;
        private int _nextBranchId = 1;
        private int _brokenDropReadIndex;
        private int _brokenDropWriteIndex;
        private int _brokenDropCount;
        private int _totalBrokenBranchesEver;

        public ReadOnlySpan<ActiveOrganicTreeNode> ActiveTreesSpan => _activeTrees.AsSpan(0, _activeTreeCount);
        public int ActiveTreeCount => _activeTreeCount;
        public int PendingBrokenBranchDropCount => _brokenDropCount;
        public int TotalBrokenBranchesCount => _totalBrokenBranchesEver;

        public int ActiveStandingCanopyCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < _activeTreeCount; i++)
                {
                    if (_activeTrees[i].IsCanopyActive) c++;
                }
                return c;
            }
        }

        public int ActiveIntactBranchCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < _activeTreeCount; i++)
                {
                    c += _activeTrees[i].IntactBranchCount;
                }
                return c;
            }
        }

        /// <summary>Verilen agac yuvasindaki (0 .. ActiveTreeCount-1) tum dallarin ReadOnlySpan dilimini doner (0 B GC).</summary>
        public ReadOnlySpan<OrganicTreeBranchNode> GetTreeBranchesSpan(int treeSlotIndex)
        {
            if ((uint)treeSlotIndex >= (uint)_activeTreeCount)
            {
                return ReadOnlySpan<OrganicTreeBranchNode>.Empty;
            }
            ref readonly ActiveOrganicTreeNode t = ref _activeTrees[treeSlotIndex];
            return _activeBranches.AsSpan(t.FirstBranchIndex, t.BranchCount);
        }

        /// <summary>BranchId ile aktif bir dali bulur ve kopyasini doner.</summary>
        public bool TryGetBranchById(int branchId, out OrganicTreeBranchNode branch)
        {
            for (int t = 0; t < _activeTreeCount; t++)
            {
                int start = _activeTrees[t].FirstBranchIndex;
                int count = _activeTrees[t].BranchCount;
                for (int b = 0; b < count; b++)
                {
                    ref readonly OrganicTreeBranchNode cand = ref _activeBranches[start + b];
                    if (cand.BranchId == branchId)
                    {
                        branch = cand;
                        return true;
                    }
                }
            }
            branch = default;
            return false;
        }

        /// <summary>Kirilan dallardan dusen odun/dal parcasi olayini kuyruktan 0-GC olarak tuketir.</summary>
        public bool TryConsumeBrokenBranchDrop(out BrokenTreeBranchDropEvent dropEvent)
        {
            if (_brokenDropCount <= 0)
            {
                dropEvent = default;
                return false;
            }

            dropEvent = _brokenBranchDrops[_brokenDropReadIndex];
            _brokenDropReadIndex = (_brokenDropReadIndex + 1) % MaxBrokenDropRingCapacity;
            _brokenDropCount--;
            return true;
        }

        private void EnqueueBrokenBranchDrop(in OrganicTreeBranchNode branch)
        {
            var evt = new BrokenTreeBranchDropEvent(
                treeInstanceId: branch.TreeInstanceId,
                branchId: branch.BranchId,
                species: branch.Species,
                tier: branch.Tier,
                woodMaterialId: branch.WoodMaterialId,
                droppedWoodCount: Math.Max((byte)1, branch.DroppedWoodUnits),
                worldX: 0.5f * (branch.AnchorWorldX + branch.CurrentTipWorldX),
                worldY: 0.5f * (branch.AnchorWorldY + branch.CurrentTipWorldY),
                worldZ: 0.5f * (branch.AnchorWorldZ + branch.CurrentTipWorldZ),
                branchLengthMeters: branch.LengthMeters,
                branchRadiusMeters: branch.StartRadiusMeters);

            _brokenBranchDrops[_brokenDropWriteIndex] = evt;
            _brokenDropWriteIndex = (_brokenDropWriteIndex + 1) % MaxBrokenDropRingCapacity;
            if (_brokenDropCount < MaxBrokenDropRingCapacity)
            {
                _brokenDropCount++;
            }
            else
            {
                _brokenDropReadIndex = (_brokenDropReadIndex + 1) % MaxBrokenDropRingCapacity;
            }
            _totalBrokenBranchesEver++;
        }

        /// <summary>
        /// Verilen dunya (worldX, worldZ) konumunda oyuncunun ayaklarinin basabilecegi en yuksek saglam (!IsBroken)
        /// 3B agac dali ust yuzey kotunu (branchTopSurfaceY), dal kimligini ve dal boyunca konum oranini (0..1) hesaplar (0 B GC).
        /// </summary>
        public bool TrySampleWalkableBranchSurfaceAt(
            float worldX,
            float worldZ,
            float refFeetY,
            float footRadiusMeters,
            float maxStepUpMeters,
            out float branchTopSurfaceY,
            out int hitTreeInstanceId,
            out int hitBranchId,
            out float normalizedDistanceAlongBranch01,
            out OrganicTreeBranchTier hitBranchTier)
        {
            branchTopSurfaceY = -30f;
            hitTreeInstanceId = 0;
            hitBranchId = 0;
            normalizedDistanceAlongBranch01 = 0f;
            hitBranchTier = OrganicTreeBranchTier.ThickPrimaryCarrier;

            float footR = Math.Clamp(footRadiusMeters, 0.08f, 0.36f);
            float maxAllowedTopY = refFeetY + Math.Max(0.15f, maxStepUpMeters);
            bool foundAny = false;

            for (int t = 0; t < _activeTreeCount; t++)
            {
                ref readonly ActiveOrganicTreeNode tree = ref _activeTrees[t];
                if (!tree.IsCanopyActive || tree.IntactBranchCount <= 0) continue;

                float dxTree = worldX - tree.WorldX;
                float dzTree = worldZ - tree.WorldZ;
                if (dxTree * dxTree + dzTree * dzTree > 36.0f) continue; // > 6.0m disinda dal olamaz

                int bStart = tree.FirstBranchIndex;
                int bCount = tree.BranchCount;
                for (int b = 0; b < bCount; b++)
                {
                    ref readonly OrganicTreeBranchNode br = ref _activeBranches[bStart + b];
                    if (br.IsBroken) continue;

                    float ux = br.CurrentTipWorldX - br.AnchorWorldX;
                    float uz = br.CurrentTipWorldZ - br.AnchorWorldZ;
                    float lenSqXZ = ux * ux + uz * uz;
                    if (lenSqXZ < 1e-4f) continue;

                    float projT = Math.Clamp(((worldX - br.AnchorWorldX) * ux + (worldZ - br.AnchorWorldZ) * uz) / lenSqXZ, 0f, 1f);
                    float cx = br.AnchorWorldX + projT * ux;
                    float cz = br.AnchorWorldZ + projT * uz;
                    float latDx = worldX - cx;
                    float latDz = worldZ - cz;
                    float latDistSq = latDx * latDx + latDz * latDz;

                    float radiusAtT = br.StartRadiusMeters + projT * (br.EndRadiusMeters - br.StartRadiusMeters);
                    float walkableHalfWidth = radiusAtT + footR;
                    if (latDistSq > walkableHalfWidth * walkableHalfWidth) continue;

                    float shape01 = EvaluateCantileverDeflectionShape01(projT);
                    float restCenterY = br.AnchorWorldY + projT * (br.RestTipWorldY - br.AnchorWorldY);
                    float deflectedCenterY = restCenterY - br.CurrentTipDeflectionMeters * shape01;

                    float latNorm01 = Math.Clamp(MathF.Sqrt(latDistSq) / MathF.Max(0.04f, walkableHalfWidth), 0f, 1f);
                    float cylinderCrownLift = radiusAtT * MathF.Sqrt(MathF.Max(0.25f, 1.0f - 0.38f * latNorm01 * latNorm01));
                    float candTopY = deflectedCenterY + cylinderCrownLift;

                    if (candTopY > maxAllowedTopY) continue;

                    if (!foundAny || candTopY > branchTopSurfaceY)
                    {
                        foundAny = true;
                        branchTopSurfaceY = candTopY;
                        hitTreeInstanceId = tree.InstanceId;
                        hitBranchId = br.BranchId;
                        normalizedDistanceAlongBranch01 = projT;
                        hitBranchTier = br.Tier;
                    }
                }
            }

            return foundAny;
        }

        /// <summary>
        /// Belirli bir dal uzerine (branchId) verilen konumda (normalizedPosition01: 0..1) dikey yuk (loadMassKg)
        /// ve inis darbe hizi (landingImpactSpeedMps) uygulayarak tum agac dallarinin esneme (deflection) ve
        /// kirilma (fracture) fizigini dt saniye ilerletir (0 B GC).
        /// </summary>
        public bool ApplyLoadAndStepBranchPhysics(
            int targetBranchId,
            float loadMassKg,
            float normalizedPosition01,
            float dt,
            out float resultingTipDeflectionMeters,
            out float resultingSurfaceYAtLoad,
            out bool targetBranchBroke,
            out BrokenTreeBranchDropEvent brokenDropEvent,
            float landingImpactSpeedMps = 0f)
        {
            resultingTipDeflectionMeters = 0f;
            resultingSurfaceYAtLoad = -30f;
            targetBranchBroke = false;
            brokenDropEvent = default;

            int targetTreeSlot = -1;
            int targetLocalIdx = -1;

            // 1) Onceki karedeki gecici yukleri temizle ve hedef dali bul
            for (int t = 0; t < _activeTreeCount; t++)
            {
                int bStart = _activeTrees[t].FirstBranchIndex;
                int bCount = _activeTrees[t].BranchCount;
                for (int b = 0; b < bCount; b++)
                {
                    ref OrganicTreeBranchNode br = ref _activeBranches[bStart + b];
                    br.CurrentAppliedLoadKg = 0f;
                    br.CurrentLoadPosition01 = 0f;
                    if (br.BranchId == targetBranchId)
                    {
                        targetTreeSlot = t;
                        targetLocalIdx = b;
                    }
                }
            }

            float clampedT = Math.Clamp(normalizedPosition01, 0.15f, 1.0f);
            float impactMult = 1.0f + Math.Clamp(MathF.Max(0f, landingImpactSpeedMps) * 0.16f, 0f, 1.35f);
            float effectiveAppliedKg = MathF.Max(0f, loadMassKg) * impactMult;

            if (targetTreeSlot >= 0 && targetLocalIdx >= 0 && effectiveAppliedKg > 0f)
            {
                int bStart = _activeTrees[targetTreeSlot].FirstBranchIndex;
                ref OrganicTreeBranchNode targetBr = ref _activeBranches[bStart + targetLocalIdx];
                if (!targetBr.IsBroken)
                {
                    targetBr.CurrentAppliedLoadKg = effectiveAppliedKg;
                    targetBr.CurrentLoadPosition01 = clampedT;

                    // Eger yuk ince bir uc daldaysa (ThinOuterTerminal), yukun %68'i bagli oldugu
                    // kalin tasiyici ana dala (ThickPrimaryCarrier) da aktarilir!
                    if (targetBr.ParentBranchLocalIndex >= 0 &&
                        targetBr.ParentBranchLocalIndex < _activeTrees[targetTreeSlot].BranchCount)
                    {
                        ref OrganicTreeBranchNode parentBr = ref _activeBranches[bStart + targetBr.ParentBranchLocalIndex];
                        if (!parentBr.IsBroken)
                        {
                            parentBr.CurrentAppliedLoadKg += effectiveAppliedKg * 0.68f;
                            parentBr.CurrentLoadPosition01 = Math.Clamp(targetBr.ParentAttachT01, 0.35f, 0.95f);
                        }
                    }
                }
            }

            // 2) Tum dallarin Euler-Bernoulli esneme ve kirilma adimini calistir
            StepAllTreeBranchesPhysics(dt);

            if (targetTreeSlot < 0 || targetLocalIdx < 0)
            {
                return false;
            }

            int slotBase = _activeTrees[targetTreeSlot].FirstBranchIndex;
            ref readonly OrganicTreeBranchNode updatedTarget = ref _activeBranches[slotBase + targetLocalIdx];
            resultingTipDeflectionMeters = updatedTarget.CurrentTipDeflectionMeters;

            if (updatedTarget.IsBroken)
            {
                targetBranchBroke = true;
                brokenDropEvent = new BrokenTreeBranchDropEvent(
                    treeInstanceId: updatedTarget.TreeInstanceId,
                    branchId: updatedTarget.BranchId,
                    species: updatedTarget.Species,
                    tier: updatedTarget.Tier,
                    woodMaterialId: updatedTarget.WoodMaterialId,
                    droppedWoodCount: Math.Max((byte)1, updatedTarget.DroppedWoodUnits),
                    worldX: 0.5f * (updatedTarget.AnchorWorldX + updatedTarget.CurrentTipWorldX),
                    worldY: 0.5f * (updatedTarget.AnchorWorldY + updatedTarget.CurrentTipWorldY),
                    worldZ: 0.5f * (updatedTarget.AnchorWorldZ + updatedTarget.CurrentTipWorldZ),
                    branchLengthMeters: updatedTarget.LengthMeters,
                    branchRadiusMeters: updatedTarget.StartRadiusMeters);
                return true;
            }

            float shapeAtLoad = EvaluateCantileverDeflectionShape01(clampedT);
            float restY = updatedTarget.AnchorWorldY + clampedT * (updatedTarget.RestTipWorldY - updatedTarget.AnchorWorldY);
            float radiusAtLoad = updatedTarget.StartRadiusMeters + clampedT * (updatedTarget.EndRadiusMeters - updatedTarget.StartRadiusMeters);
            resultingSurfaceYAtLoad = (restY - updatedTarget.CurrentTipDeflectionMeters * shapeAtLoad) + radiusAtLoad;
            return true;
        }

        /// <summary>
        /// Aktif tum agaclarin dallarini Euler-Bernoulli konsol kiris esnekligi (yay-sonumleyici dinamigi)
        /// ve kirilma esigi kurallarina gore dt saniye ilerletir (0 B GC).
        /// </summary>
        public int StepAllTreeBranchesPhysics(float dt)
        {
            float clampedDt = Math.Clamp(dt, 0.001f, 0.10f);
            int subSteps = clampedDt > 0.025f ? 3 : (clampedDt > 0.012f ? 2 : 1);
            float stepDt = clampedDt / subSteps;
            int brokenThisStep = 0;

            const float springOmega = 15.0f; // Dogal acisal frekans (rad/s)
            const float dampingZeta = 0.78f; // Sonum orani

            for (int t = 0; t < _activeTreeCount; t++)
            {
                ref ActiveOrganicTreeNode tree = ref _activeTrees[t];
                if (!tree.IsCanopyActive || tree.BranchCount <= 0) continue;

                int bStart = tree.FirstBranchIndex;
                int bCount = tree.BranchCount;

                for (int b = 0; b < bCount; b++)
                {
                    ref OrganicTreeBranchNode br = ref _activeBranches[bStart + b];
                    if (br.IsBroken) continue;

                    // Eger ebeveyn ana dal kirildiysa, ona bagli ince uc dal da aninda kopar!
                    if (br.ParentBranchLocalIndex >= 0 && br.ParentBranchLocalIndex < bCount)
                    {
                        ref readonly OrganicTreeBranchNode parentBr = ref _activeBranches[bStart + br.ParentBranchLocalIndex];
                        if (parentBr.IsBroken)
                        {
                            BreakSingleBranchInternal(ref tree, ref br);
                            brokenThisStep++;
                            continue;
                        }

                        // Ebeveyn dalin esnemesini cocuk dalin ankastre kokune (AnchorWorldY) yansit!
                        float parentDeflectAtAttach = parentBr.CurrentTipDeflectionMeters *
                                                      EvaluateCantileverDeflectionShape01(br.ParentAttachT01);
                        float anchorDrop = parentDeflectAtAttach;
                        float prevAnchorY = br.AnchorWorldY;
                        br.AnchorWorldY = br.BaseAnchorWorldY - anchorDrop;
                        br.RestTipWorldY += (br.AnchorWorldY - prevAnchorY);
                    }

                    float loadT = br.CurrentAppliedLoadKg > 0.01f
                        ? Math.Clamp(br.CurrentLoadPosition01, 0.18f, 1.0f)
                        : 0.65f;
                    float leverArm = br.LengthMeters * loadT;
                    float preloadArm = br.LengthMeters * 0.65f;

                    float preloadForceN = br.PreloadMassKg * 9.81f;
                    float appliedForceN = br.CurrentAppliedLoadKg * 9.81f;

                    float totalMomentNm = preloadForceN * preloadArm + appliedForceN * leverArm;
                    br.CurrentBendingMomentNm = totalMomentNm;

                    // Euler-Bernoulli konsol kiris uc cokmesi: delta = F * a^2 * (3L - a) / (6 * E * I)
                    float deltaPreload = (preloadForceN * preloadArm * preloadArm * (3.0f * br.LengthMeters - preloadArm)) /
                                         (6.0f * br.FlexuralRigidityEI);
                    float deltaApplied = (appliedForceN * leverArm * leverArm * (3.0f * br.LengthMeters - leverArm)) /
                                         (6.0f * br.FlexuralRigidityEI);
                    br.TargetTipDeflectionMeters = deltaPreload + deltaApplied;

                    for (int s = 0; s < subSteps; s++)
                    {
                        float dispError = br.TargetTipDeflectionMeters - br.CurrentTipDeflectionMeters;
                        float accel = (springOmega * springOmega) * dispError - (2.0f * dampingZeta * springOmega) * br.DeflectionVelocityMps;
                        br.DeflectionVelocityMps += accel * stepDt;
                        br.CurrentTipDeflectionMeters = MathF.Max(0f, br.CurrentTipDeflectionMeters + br.DeflectionVelocityMps * stepDt);
                    }

                    // Kirilma Esigi Denetimi:
                    // 1) Egilme momenti (CurrentBendingMomentNm) kirilma momentini (BreakMomentThresholdNm) astiysa, VEYA
                    // 2) Uygulanan yuk dalin o noktadaki guvenli tasima kapasitesini (MaxSafeLoadKg / Min(1, loadT / 0.72)) astiysa
                    float effectiveLoadLimitAtT = br.MaxSafeLoadKg / MathF.Max(0.45f, loadT / 0.72f);
                    bool overloaded = br.CurrentAppliedLoadKg > effectiveLoadLimitAtT ||
                                      br.CurrentBendingMomentNm >= br.BreakMomentThresholdNm ||
                                      (br.CurrentTipDeflectionMeters >= br.MaxElasticDeflectionMeters &&
                                       br.CurrentAppliedLoadKg >= br.MaxSafeLoadKg * 0.85f);

                    if (overloaded)
                    {
                        BreakSingleBranchInternal(ref tree, ref br);
                        brokenThisStep++;

                        // Eger kalin bir ana dal kirildiysa, ona bagli tum ince uc dallari da ayni adimda kir!
                        if (br.Tier == OrganicTreeBranchTier.ThickPrimaryCarrier)
                        {
                            int brokenLocal = br.LocalBranchIndex;
                            for (int c = 0; c < bCount; c++)
                            {
                                ref OrganicTreeBranchNode child = ref _activeBranches[bStart + c];
                                if (!child.IsBroken && child.ParentBranchLocalIndex == brokenLocal)
                                {
                                    BreakSingleBranchInternal(ref tree, ref child);
                                    brokenThisStep++;
                                }
                            }
                        }
                        continue;
                    }

                    // Bukulme acisi ve guncel 3B uc konumu (uzunluk korunumu ile hafif yatay iceri cekilme)
                    float clampedDeflect = Math.Clamp(br.CurrentTipDeflectionMeters, 0f, br.LengthMeters * 0.65f);
                    br.CurrentDeflectionAngleDeg = MathF.Atan2(clampedDeflect, br.LengthMeters) * (180.0f / MathF.PI);
                    float foreshorten = br.LengthMeters - MathF.Sqrt(MathF.Max(0.04f, br.LengthMeters * br.LengthMeters - clampedDeflect * clampedDeflect));
                    float horizUx = br.RestTipWorldX - br.AnchorWorldX;
                    float horizUz = br.RestTipWorldZ - br.AnchorWorldZ;
                    float horizMag = MathF.Max(0.01f, br.HorizontalLengthMeters);
                    br.CurrentTipWorldX = br.RestTipWorldX - (horizUx / horizMag) * foreshorten;
                    br.CurrentTipWorldY = br.RestTipWorldY - clampedDeflect;
                    br.CurrentTipWorldZ = br.RestTipWorldZ - (horizUz / horizMag) * foreshorten;

                    // Bu fizik adiminda tuketilen gecici yuku sifirla; oyuncu daldan ayrildiginda bir sonraki adimda dal geri yaylanir!
                    br.CurrentAppliedLoadKg = 0f;
                    br.CurrentLoadPosition01 = 0f;
                }

#if UNITY_5_3_OR_NEWER
                SyncUnityBranchVisualsForTree(t);
#endif
            }

            return brokenThisStep;
        }

        private void BreakSingleBranchInternal(ref ActiveOrganicTreeNode tree, ref OrganicTreeBranchNode branch)
        {
            if (branch.IsBroken) return;
            branch.IsBroken = true;
            branch.CurrentTipDeflectionMeters = branch.MaxElasticDeflectionMeters;
            branch.CurrentDeflectionAngleDeg = 48.0f;
            branch.CurrentTipWorldY = branch.RestTipWorldY - branch.MaxElasticDeflectionMeters;
            if (tree.IntactBranchCount > 0)
            {
                tree.IntactBranchCount--;
            }
            EnqueueBrokenBranchDrop(in branch);
        }

#if UNITY_5_3_OR_NEWER
        [Serializable]
        private sealed class GltfSimpleRoot
        {
            public GltfSimpleMesh[]? meshes;
            public GltfSimpleBufferView[]? bufferViews;
            public GltfSimpleAccessor[]? accessors;
        }

        [Serializable]
        private sealed class GltfSimpleMesh
        {
            public GltfSimplePrimitive[]? primitives;
        }

        [Serializable]
        private sealed class GltfSimplePrimitive
        {
            public GltfSimpleAttributes? attributes;
            public int indices = -1;
        }

        [Serializable]
        private sealed class GltfSimpleAttributes
        {
            public int POSITION = -1;
            public int NORMAL = -1;
            public int COLOR_0 = -1;
        }

        [Serializable]
        private sealed class GltfSimpleBufferView
        {
            public int byteOffset;
            public int byteLength;
            public int byteStride;
        }

        [Serializable]
        private sealed class GltfSimpleAccessor
        {
            public int bufferView = -1;
            public int byteOffset;
            public int componentType;
            public int count;
            public string? type;
        }

        private struct PooledTreeVisualSlot
        {
            public GameObject? CanopyGo;
            public Transform? CanopyTr;
            public MeshFilter? CanopyMf;
            public MeshRenderer? CanopyMr;
            public OrganicFloraKind AssignedCanopyKind;
            public GameObject? RootGo;
            public Transform? RootTr;
            public MeshFilter? RootMf;
            public MeshRenderer? RootMr;
            public GameObject?[]? BranchGos;
            public Transform?[]? BranchTrs;
            public MeshRenderer?[]? BranchMrs;
        }

        private static readonly Mesh?[] CachedFloraMeshes = new Mesh?[FloraAssetCount];
        private static readonly bool[] AttemptedFloraLoads = new bool[FloraAssetCount];
        private static Mesh? _cachedThickBranchUnitMesh;
        private static Mesh? _cachedThinBranchUnitMesh;
        private static Material? _sharedOrganicFoliageMaterial;
        private static bool _sharedOrganicFoliageMaterialConfigured;
        private static Material? _sharedOrganicFoliageLastFallback;
        private const int MaxVisualPoolCapacity = 512;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesOnSubsystemRegistration()
        {
            Array.Clear(CachedFloraMeshes, 0, CachedFloraMeshes.Length);
            Array.Clear(AttemptedFloraLoads, 0, AttemptedFloraLoads.Length);
            _cachedThickBranchUnitMesh = null;
            _cachedThinBranchUnitMesh = null;
            _sharedOrganicFoliageMaterial = null;
            _sharedOrganicFoliageMaterialConfigured = false;
            _sharedOrganicFoliageLastFallback = null;
            _activeInstance = null;
        }

        private PooledTreeVisualSlot[] _activeVisuals = new PooledTreeVisualSlot[256];
        private readonly Stack<PooledTreeVisualSlot> _visualPool = new(128);
        private Transform? _floraRootTransform;

        public int PooledTreeVisualCount => _visualPool.Count;

        private static Mesh GetOrCreateUnitTaperedBranchMesh(bool isThickPrimary)
        {
            if (isThickPrimary && _cachedThickBranchUnitMesh != null) return _cachedThickBranchUnitMesh;
            if (!isThickPrimary && _cachedThinBranchUnitMesh != null) return _cachedThinBranchUnitMesh!;

            int segs = isThickPrimary ? 6 : 4;
            float r0 = 1.0f;
            float r1 = isThickPrimary ? 0.62f : 0.45f;
            Color32 barkDark = new Color32(72, 50, 36, 224);
            Color32 barkRidge = new Color32(96, 68, 46, 234);

            var verts = new List<Vector3>((segs + 1) * 2 + 2);
            var norms = new List<Vector3>((segs + 1) * 2 + 2);
            var cols = new List<Color32>((segs + 1) * 2 + 2);
            var tris = new List<int>(segs * 12);

            for (int ring = 0; ring < 2; ring++)
            {
                float z = ring == 0 ? 0f : 1.0f;
                float r = ring == 0 ? r0 : r1;
                for (int s = 0; s < segs; s++)
                {
                    float ang = (2.0f * Mathf.PI * s) / segs;
                    float cx = Mathf.Cos(ang);
                    float cy = Mathf.Sin(ang);
                    verts.Add(new Vector3(cx * r, cy * r, z));
                    norms.Add(new Vector3(cx, cy, 0.15f).normalized);
                    cols.Add((s & 1) == 0 ? barkDark : barkRidge);
                }
            }

            for (int s = 0; s < segs; s++)
            {
                int sn = (s + 1) % segs;
                int b0 = s, b1 = sn;
                int t0 = segs + s, t1 = segs + sn;
                tris.Add(b0); tris.Add(t0); tris.Add(t1);
                tris.Add(b0); tris.Add(t1); tris.Add(b1);
            }

            int tipCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, 0f, 1.02f));
            norms.Add(Vector3.forward);
            cols.Add(barkRidge);
            for (int s = 0; s < segs; s++)
            {
                int sn = (s + 1) % segs;
                tris.Add(segs + s);
                tris.Add(tipCenterIdx);
                tris.Add(segs + sn);
            }

            Mesh m = new Mesh
            {
                name = isThickPrimary ? "Mesh_UnitThickPrimaryBranch" : "Mesh_UnitThinOuterBranch"
            };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetColors(cols);
            m.SetTriangles(tris, 0, calculateBounds: true);

            if (isThickPrimary) _cachedThickBranchUnitMesh = m;
            else _cachedThinBranchUnitMesh = m;
            return m;
        }

        public static Material? ResolveOrganicFoliageMaterial(Material? fallbackSharedMat = null)
        {
            if (_sharedOrganicFoliageMaterial == null)
            {
                Shader? sh = Shader.Find("Stratocraft/VertexColorUberLit");
                if (sh == null) return fallbackSharedMat;
                _sharedOrganicFoliageMaterial = new Material(sh)
                {
                    name = "M_Stratocraft_OrganicTreeFoliage_Matte"
                };
                _sharedOrganicFoliageMaterial.enableInstancing = true;
                _sharedOrganicFoliageMaterialConfigured = false;
                _sharedOrganicFoliageLastFallback = null;
            }

            if (_sharedOrganicFoliageMaterialConfigured && ReferenceEquals(_sharedOrganicFoliageLastFallback, fallbackSharedMat))
            {
                return _sharedOrganicFoliageMaterial;
            }

            _sharedOrganicFoliageMaterial.enableInstancing = true;
            if (fallbackSharedMat != null)
            {
                if (fallbackSharedMat.HasProperty("_MicroDetailArray"))
                {
                    Texture? microArr = fallbackSharedMat.GetTexture("_MicroDetailArray");
                    if (microArr != null) _sharedOrganicFoliageMaterial.SetTexture("_MicroDetailArray", microArr);
                }
                if (fallbackSharedMat.HasProperty("_MatCapAtlas16x"))
                {
                    Texture? mcTex = fallbackSharedMat.GetTexture("_MatCapAtlas16x");
                    if (mcTex != null) _sharedOrganicFoliageMaterial.SetTexture("_MatCapAtlas16x", mcTex);
                }
            }

            _sharedOrganicFoliageMaterial.SetFloat("_UsePaletteLUT", 0.0f);
            _sharedOrganicFoliageMaterial.SetFloat("_MatCapBlendWeight", 0.02f);
            _sharedOrganicFoliageMaterial.SetFloat("_RimLightStrength", 0.04f);
            _sharedOrganicFoliageMaterial.SetFloat("_MicroDetailStrength", 0.35f);
            _sharedOrganicFoliageMaterial.SetFloat("_SmoothFacetBlend", 0.38f);
            _sharedOrganicFoliageMaterial.SetFloat("_AmbientStrength", 0.68f);
            _sharedOrganicFoliageLastFallback = fallbackSharedMat;
            _sharedOrganicFoliageMaterialConfigured = true;
            return _sharedOrganicFoliageMaterial;
        }

        /// <summary>
        /// Kamera yeraltindayken veya uzak chunklardayken agac tac/kok/dal cizimini ve golge dokumunu budar (0 B GC).
        /// </summary>
        public void UpdateTreeVisibilityAndShadowCulling(
            Vector3 cameraWorldPos,
            bool isCameraUnderground,
            float maxCanopyDist = 144f,
            float maxCanopyShadowDist = 48f,
            float maxRootDist = 56f,
            float maxRootShadowDist = 28f)
        {
            float maxCanopySq = maxCanopyDist * maxCanopyDist;
            float maxCanopyShadowSq = maxCanopyShadowDist * maxCanopyShadowDist;
            float maxRootSq = maxRootDist * maxRootDist;
            float maxRootShadowSq = maxRootShadowDist * maxRootShadowDist;

            for (int i = 0; i < _activeTreeCount; i++)
            {
                ref readonly ActiveOrganicTreeNode node = ref _activeTrees[i];
                ref PooledTreeVisualSlot vis = ref _activeVisuals[i];

                if (isCameraUnderground)
                {
                    if (vis.CanopyMr != null && vis.CanopyMr.enabled) vis.CanopyMr.enabled = false;
                    if (vis.RootMr != null && vis.RootMr.enabled) vis.RootMr.enabled = false;
                    if (vis.BranchMrs != null)
                    {
                        for (int b = 0; b < vis.BranchMrs.Length; b++)
                        {
                            MeshRenderer? bmr = vis.BranchMrs[b];
                            if (bmr != null && bmr.enabled) bmr.enabled = false;
                        }
                    }
                    continue;
                }

                float dx = node.WorldX - cameraWorldPos.x;
                float dz = node.WorldZ - cameraWorldPos.z;
                float distSq = dx * dx + dz * dz;

                if (vis.CanopyMr != null)
                {
                    bool showCanopy = node.IsCanopyActive && distSq <= maxCanopySq;
                    if (vis.CanopyMr.enabled != showCanopy) vis.CanopyMr.enabled = showCanopy;
                    if (showCanopy)
                    {
                        ShadowCastingMode cm = distSq <= maxCanopyShadowSq ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        if (vis.CanopyMr.shadowCastingMode != cm) vis.CanopyMr.shadowCastingMode = cm;
                    }
                }

                if (vis.RootMr != null)
                {
                    bool showRoot = node.IsRootActive && distSq <= maxRootSq;
                    if (vis.RootMr.enabled != showRoot) vis.RootMr.enabled = showRoot;
                    if (showRoot)
                    {
                        ShadowCastingMode rm = distSq <= maxRootShadowSq ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        if (vis.RootMr.shadowCastingMode != rm) vis.RootMr.shadowCastingMode = rm;
                    }
                }

                if (vis.BranchMrs != null)
                {
                    bool inBranchVisRange = node.IsCanopyActive && distSq <= maxRootSq;
                    int bStart = node.FirstBranchIndex;
                    for (int b = 0; b < node.BranchCount && b < vis.BranchMrs.Length; b++)
                    {
                        MeshRenderer? bmr = vis.BranchMrs[b];
                        if (bmr == null) continue;
                        bool showBranch = inBranchVisRange && !_activeBranches[bStart + b].IsBroken;
                        if (bmr.enabled != showBranch) bmr.enabled = showBranch;
                    }
                }
            }
        }

        public static Mesh? LoadFloraMeshCached(OrganicFloraKind kind)
        {
            int idx = Mathf.Clamp((int)kind, 0, FloraAssetCount - 1);
            if (AttemptedFloraLoads[idx])
            {
                return CachedFloraMeshes[idx];
            }

            AttemptedFloraLoads[idx] = true;
            string diskPath = ResolveFloraGlbDiskPath(kind, Application.dataPath);
            Mesh? mesh = LoadSingleGlbMeshLeftHandedSrgb(diskPath, $"Flora3D_{kind}");
            CachedFloraMeshes[idx] = mesh;
            return mesh;
        }

        private static Mesh? LoadSingleGlbMeshLeftHandedSrgb(string glbPath, string meshName)
        {
            if (string.IsNullOrEmpty(glbPath) || !File.Exists(glbPath)) return null;
            byte[] bytes = File.ReadAllBytes(glbPath);
            if (bytes.Length < 28 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) return null;

            int offset = 12;
            string? jsonText = null;
            int binStart = -1;
            int binLen = 0;
            while (offset + 8 <= bytes.Length)
            {
                int chunkLen = BitConverter.ToInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (chunkLen < 0 || dataStart + chunkLen > bytes.Length) break;
                if (chunkType == 0x4E4F534Au) jsonText = Encoding.UTF8.GetString(bytes, dataStart, chunkLen);
                else if (chunkType == 0x004E4942u) { binStart = dataStart; binLen = chunkLen; }
                offset = dataStart + chunkLen;
            }
            if (string.IsNullOrEmpty(jsonText) || binStart < 0 || binLen <= 0) return null;

            GltfSimpleRoot? root = JsonUtility.FromJson<GltfSimpleRoot>(jsonText);
            if (root?.meshes == null || root.meshes.Length == 0 || root.accessors == null || root.bufferViews == null)
            {
                return null;
            }

            var verts = new List<Vector3>(1024);
            var norms = new List<Vector3>(1024);
            var cols = new List<Color32>(1024);
            var tris = new List<int>(2048);

            for (int m = 0; m < root.meshes.Length; m++)
            {
                GltfSimplePrimitive[]? prims = root.meshes[m].primitives;
                if (prims == null) continue;
                for (int p = 0; p < prims.Length; p++)
                {
                    GltfSimplePrimitive prim = prims[p];
                    if (prim.attributes == null || prim.attributes.POSITION < 0) continue;

                    Vector3[] posArr = ReadGlbVec3(bytes, binStart, root, prim.attributes.POSITION);
                    int vCount = posArr.Length;
                    if (vCount == 0) continue;

                    Vector3[] nrmArr = prim.attributes.NORMAL >= 0
                        ? ReadGlbVec3(bytes, binStart, root, prim.attributes.NORMAL)
                        : new Vector3[vCount];
                    if (nrmArr.Length != vCount) Array.Resize(ref nrmArr, vCount);

                    Color32[] colArr = prim.attributes.COLOR_0 >= 0
                        ? ReadGlbColor32Srgb(bytes, binStart, root, prim.attributes.COLOR_0, vCount)
                        : new Color32[vCount];

                    int[] idxArr = prim.indices >= 0
                        ? ReadGlbIndices(bytes, binStart, root, prim.indices)
                        : CreateSequentialIndices(vCount);

                    int baseV = verts.Count;
                    for (int i = 0; i < vCount; i++)
                    {
                        verts.Add(new Vector3(-posArr[i].x, posArr[i].y, posArr[i].z));
                        norms.Add(new Vector3(-nrmArr[i].x, nrmArr[i].y, nrmArr[i].z));
                        cols.Add(colArr[i]);
                    }
                    for (int i = 0; i + 2 < idxArr.Length; i += 3)
                    {
                        tris.Add(baseV + idxArr[i]);
                        tris.Add(baseV + idxArr[i + 2]);
                        tris.Add(baseV + idxArr[i + 1]);
                    }
                }
            }

            if (verts.Count == 0 || tris.Count == 0) return null;

            Mesh built = new Mesh
            {
                name = meshName,
                indexFormat = verts.Count > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            built.SetVertices(verts);
            built.SetNormals(norms);
            built.SetColors(cols);
            built.SetTriangles(tris, 0, calculateBounds: true);
            if (norms[0].sqrMagnitude < 1e-4f) built.RecalculateNormals();
            return built;
        }

        private static Vector3[] ReadGlbVec3(byte[] bytes, int binStart, GltfSimpleRoot root, int accIdx)
        {
            GltfSimpleAccessor acc = root.accessors![accIdx];
            GltfSimpleBufferView bv = root.bufferViews![acc.bufferView];
            int stride = bv.byteStride > 0 ? bv.byteStride : 12;
            int basePtr = binStart + bv.byteOffset + acc.byteOffset;
            Vector3[] arr = new Vector3[acc.count];
            for (int i = 0; i < acc.count; i++)
            {
                int p = basePtr + i * stride;
                arr[i] = new Vector3(
                    BitConverter.ToSingle(bytes, p),
                    BitConverter.ToSingle(bytes, p + 4),
                    BitConverter.ToSingle(bytes, p + 8));
            }
            return arr;
        }

        private static Color32[] ReadGlbColor32Srgb(
            byte[] bytes,
            int binStart,
            GltfSimpleRoot root,
            int accIdx,
            int expectedCount)
        {
            GltfSimpleAccessor acc = root.accessors![accIdx];
            GltfSimpleBufferView bv = root.bufferViews![acc.bufferView];
            bool isVec4 = string.Equals(acc.type, "VEC4", StringComparison.OrdinalIgnoreCase);
            int compCount = isVec4 ? 4 : 3;
            int elemBytes = acc.componentType switch { 5121 => 1, 5123 => 2, 5126 => 4, _ => 1 };
            int stride = bv.byteStride > 0 ? bv.byteStride : (compCount * elemBytes);
            int basePtr = binStart + bv.byteOffset + acc.byteOffset;
            int count = Mathf.Min(acc.count, expectedCount);
            Color32[] cols = new Color32[expectedCount];

            for (int i = 0; i < count; i++)
            {
                int p = basePtr + i * stride;
                byte r, g, b, a;
                if (acc.componentType == 5121)
                {
                    r = bytes[p];
                    g = bytes[p + 1];
                    b = bytes[p + 2];
                    a = isVec4 ? bytes[p + 3] : (byte)245;
                }
                else if (acc.componentType == 5123)
                {
                    float rf = Mathf.LinearToGammaSpace(BitConverter.ToUInt16(bytes, p) / 65535f);
                    float gf = Mathf.LinearToGammaSpace(BitConverter.ToUInt16(bytes, p + 2) / 65535f);
                    float bf = Mathf.LinearToGammaSpace(BitConverter.ToUInt16(bytes, p + 4) / 65535f);
                    r = (byte)Mathf.Clamp(Mathf.RoundToInt(rf * 255f), 0, 255);
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(gf * 255f), 0, 255);
                    b = (byte)Mathf.Clamp(Mathf.RoundToInt(bf * 255f), 0, 255);
                    a = isVec4 ? (byte)(BitConverter.ToUInt16(bytes, p + 6) >> 8) : (byte)245;
                }
                else
                {
                    float rf = Mathf.LinearToGammaSpace(BitConverter.ToSingle(bytes, p));
                    float gf = Mathf.LinearToGammaSpace(BitConverter.ToSingle(bytes, p + 4));
                    float bf = Mathf.LinearToGammaSpace(BitConverter.ToSingle(bytes, p + 8));
                    r = (byte)Mathf.Clamp(Mathf.RoundToInt(rf * 255f), 0, 255);
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(gf * 255f), 0, 255);
                    b = (byte)Mathf.Clamp(Mathf.RoundToInt(bf * 255f), 0, 255);
                    a = isVec4
                        ? (byte)Mathf.Clamp(Mathf.RoundToInt(BitConverter.ToSingle(bytes, p + 12) * 255f), 0, 255)
                        : (byte)245;
                }
                cols[i] = new Color32(r, g, b, a);
            }
            return cols;
        }

        private static int[] ReadGlbIndices(byte[] bytes, int binStart, GltfSimpleRoot root, int accIdx)
        {
            GltfSimpleAccessor acc = root.accessors![accIdx];
            GltfSimpleBufferView bv = root.bufferViews![acc.bufferView];
            int basePtr = binStart + bv.byteOffset + acc.byteOffset;
            int[] idx = new int[acc.count];
            if (acc.componentType == 5125)
            {
                for (int i = 0; i < acc.count; i++) idx[i] = (int)BitConverter.ToUInt32(bytes, basePtr + i * 4);
            }
            else if (acc.componentType == 5123)
            {
                for (int i = 0; i < acc.count; i++) idx[i] = BitConverter.ToUInt16(bytes, basePtr + i * 2);
            }
            else
            {
                for (int i = 0; i < acc.count; i++) idx[i] = bytes[basePtr + i];
            }
            return idx;
        }

        private static int[] CreateSequentialIndices(int count)
        {
            int[] idx = new int[count];
            for (int i = 0; i < count; i++) idx[i] = i;
            return idx;
        }

        private void EnsureFloraContainerRoot()
        {
            if (_floraRootTransform != null) return;
            GameObject existing = GameObject.Find("07_OrganicTreeFloraRoot");
            if (existing == null)
            {
                existing = new GameObject("07_OrganicTreeFloraRoot");
            }
            else if (Application.isPlaying)
            {
                for (int i = existing.transform.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(existing.transform.GetChild(i).gameObject);
                }
            }
            _floraRootTransform = existing.transform;
        }

        public int SpawnOrSyncChunkTrees(
            int chunkX,
            int chunkZ,
            Material? fallbackSharedMat = null,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            int chunkKey = PackChunkTreeKey(chunkX, chunkZ);
            if (_spawnedChunkKeys.Contains(chunkKey))
            {
                return CountTreesInChunk(chunkKey);
            }

            int specCount = ResolveDeterministicChunkTreeSpecs(chunkX, chunkZ, _chunkTreeScratch, worldChunks);
            return SpawnOrSyncChunkTrees(chunkX, chunkZ, _chunkTreeScratch.AsSpan(0, specCount), fallbackSharedMat);
        }

        public int SpawnOrSyncChunkTrees(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<(float WorldX, float CrownBaseY, float WorldZ, int TrunkHeight, int BiomeId, bool IsPine)> tupleSpecs,
            Material? fallbackSharedMat = null)
        {
            int count = Math.Min(tupleSpecs.Length, _chunkTreeScratch.Length);
            for (int i = 0; i < count; i++)
            {
                var t = tupleSpecs[i];
                _chunkTreeScratch[i] = new OrganicTreePlacementSpec(
                    t.WorldX, t.CrownBaseY, t.WorldZ, t.TrunkHeight, t.BiomeId, t.IsPine);
            }
            return SpawnOrSyncChunkTrees(chunkX, chunkZ, _chunkTreeScratch.AsSpan(0, count), fallbackSharedMat);
        }

        public int SpawnOrSyncChunkTrees(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<OrganicTreePlacementSpec> treeSpecs,
            Material? fallbackSharedMat = null)
        {
            _activeInstance = this;
            int chunkKey = PackChunkTreeKey(chunkX, chunkZ);
            if (!_spawnedChunkKeys.Add(chunkKey))
            {
                return CountTreesInChunk(chunkKey);
            }

            if (treeSpecs.Length == 0) return 0;

            EnsureFloraContainerRoot();
            Material? foliageMat = ResolveOrganicFoliageMaterial(fallbackSharedMat);

            for (int i = 0; i < treeSpecs.Length; i++)
            {
                AppendActiveTreeInstance(chunkKey, in treeSpecs[i], foliageMat);
            }

            return treeSpecs.Length;
        }

        public int ReleaseChunkTrees(int chunkX, int chunkZ)
        {
            int chunkKey = PackChunkTreeKey(chunkX, chunkZ);
            if (!_spawnedChunkKeys.Remove(chunkKey))
            {
                return 0;
            }

            int released = 0;
            for (int i = _activeTreeCount - 1; i >= 0; i--)
            {
                if (_activeTrees[i].ChunkOwnerKey != chunkKey) continue;
                ReleaseVisualSlotToPool(ref _activeVisuals[i]);
                RemoveTreeEntrySwapBack(i);
                released++;
            }
            return released;
        }

        /// <summary>
        /// Oyuncu baltayla veya yapisal cokusle agac govdesini kestiginde (wx, wy, wz) o agacin
        /// 3B yaprak tacini, bagli dallarini ve kok mahmuzunu aninda temizleyip 0-GC havuza iade eder!
        /// </summary>
        public int NotifyTreeTruncatedOrFelledAt(int wx, int wy, int wz)
        {
            int felledCount = 0;
            for (int i = _activeTreeCount - 1; i >= 0; i--)
            {
                ref ActiveOrganicTreeNode t = ref _activeTrees[i];
                if (Math.Abs(wx - t.VoxelX) > 1 || Math.Abs(wz - t.VoxelZ) > 1) continue;
                if (wy < t.BaseVoxelY - 1 || wy > t.CrownVoxelY + 2) continue;

                ReleaseVisualSlotToPool(ref _activeVisuals[i]);
                RemoveTreeEntrySwapBack(i);
                felledCount++;
            }
            return felledCount;
        }

        public void ClearAllTrees()
        {
            for (int i = _activeTreeCount - 1; i >= 0; i--)
            {
                ReleaseVisualSlotToPool(ref _activeVisuals[i]);
            }
            _activeTreeCount = 0;
            _spawnedChunkKeys.Clear();
            _brokenDropReadIndex = 0;
            _brokenDropWriteIndex = 0;
            _brokenDropCount = 0;
        }

        private int CountTreesInChunk(int chunkKey)
        {
            int c = 0;
            for (int i = 0; i < _activeTreeCount; i++)
            {
                if (_activeTrees[i].ChunkOwnerKey == chunkKey) c++;
            }
            return c;
        }

        private void AppendActiveTreeInstance(
            int chunkKey,
            in OrganicTreePlacementSpec spec,
            Material? foliageMat)
        {
            if (_activeTreeCount >= _activeTrees.Length)
            {
                int nextCap = _activeTrees.Length * 2;
                Array.Resize(ref _activeTrees, nextCap);
                Array.Resize(ref _activeVisuals, nextCap);
                Array.Resize(ref _activeBranches, nextCap * MaxBranchesPerTree);
            }

            int vx = Mathf.FloorToInt(spec.WorldX);
            int vz = Mathf.FloorToInt(spec.WorldZ);
            int baseVy = Mathf.RoundToInt(spec.BaseY);
            int crownVy = Mathf.RoundToInt(spec.CrownBaseY);
            OrganicFloraKind canopyKind = SelectCanopyKindForTree(spec.BiomeId, spec.IsPine, spec.CrownBaseY);
            ComputeDeterministicVariation(vx, vz, spec.TrunkHeight, out float yawDeg, out float scale);

            PooledTreeVisualSlot vis = RentOrCreateVisualSlot(canopyKind, foliageMat);
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);

            if (vis.CanopyTr != null)
            {
                vis.CanopyTr.SetPositionAndRotation(new Vector3(spec.WorldX, spec.CrownBaseY, spec.WorldZ), rot);
                vis.CanopyTr.localScale = Vector3.one * scale;
            }
            if (vis.CanopyGo != null && !vis.CanopyGo.activeSelf)
            {
                vis.CanopyGo.SetActive(true);
            }

            if (vis.RootTr != null)
            {
                vis.RootTr.SetPositionAndRotation(new Vector3(spec.WorldX, spec.BaseY, spec.WorldZ), rot);
                vis.RootTr.localScale = Vector3.one * Mathf.Lerp(0.95f, 1.12f, Mathf.InverseLerp(0.90f, 1.15f, scale));
            }
            if (vis.RootGo != null && !vis.RootGo.activeSelf)
            {
                vis.RootGo.SetActive(true);
            }

            int idx = _activeTreeCount++;
            int firstBranchIdx = idx * MaxBranchesPerTree;
            var node = new ActiveOrganicTreeNode
            {
                InstanceId = _nextInstanceId++,
                ChunkOwnerKey = chunkKey,
                VoxelX = vx,
                BaseVoxelY = baseVy,
                CrownVoxelY = crownVy,
                VoxelZ = vz,
                TrunkHeight = spec.TrunkHeight,
                BiomeId = spec.BiomeId,
                IsPine = spec.IsPine,
                WoodMaterialId = spec.WoodMaterialId,
                SpeciesKind = spec.SpeciesKind,
                CanopyKind = canopyKind,
                WorldX = spec.WorldX,
                BaseWorldY = spec.BaseY,
                CrownWorldY = spec.CrownBaseY,
                TrunkCollarWorldY = spec.CrownBaseY + 1.0f,
                WorldZ = spec.WorldZ,
                YawDegrees = yawDeg,
                UniformScale = scale,
                FirstBranchIndex = firstBranchIdx,
                IsCanopyActive = true,
                IsRootActive = true
            };

            int branchCount = GenerateSpeciesBranchHierarchyForTree(
                in node,
                idx,
                _nextBranchId,
                _activeBranches.AsSpan(firstBranchIdx, MaxBranchesPerTree),
                out int thickCount,
                out int thinCount);
            _nextBranchId += branchCount;

            node.BranchCount = branchCount;
            node.ThickPrimaryBranchCount = thickCount;
            node.ThinOuterBranchCount = thinCount;
            node.IntactBranchCount = branchCount;

            _activeTrees[idx] = node;
            _activeVisuals[idx] = vis;
            SyncUnityBranchVisualsForTree(idx);
        }

        private void SyncUnityBranchVisualsForTree(int treeSlotIdx)
        {
            if ((uint)treeSlotIdx >= (uint)_activeTreeCount) return;
            ref readonly ActiveOrganicTreeNode tree = ref _activeTrees[treeSlotIdx];
            ref PooledTreeVisualSlot vis = ref _activeVisuals[treeSlotIdx];
            if (vis.BranchGos == null || vis.BranchTrs == null) return;

            int bStart = tree.FirstBranchIndex;
            for (int b = 0; b < vis.BranchGos.Length; b++)
            {
                GameObject? bGo = vis.BranchGos[b];
                Transform? bTr = vis.BranchTrs[b];
                if (bGo == null || bTr == null) continue;

                if (b >= tree.BranchCount)
                {
                    if (bGo.activeSelf) bGo.SetActive(false);
                    continue;
                }

                ref readonly OrganicTreeBranchNode br = ref _activeBranches[bStart + b];
                if (br.IsBroken)
                {
                    if (bGo.activeSelf) bGo.SetActive(false);
                    continue;
                }

                Vector3 anchor = new Vector3(br.AnchorWorldX, br.AnchorWorldY, br.AnchorWorldZ);
                Vector3 tip = new Vector3(br.CurrentTipWorldX, br.CurrentTipWorldY, br.CurrentTipWorldZ);
                Vector3 delta = tip - anchor;
                float len = delta.magnitude;
                if (len < 0.05f)
                {
                    if (bGo.activeSelf) bGo.SetActive(false);
                    continue;
                }

                if (!bGo.activeSelf) bGo.SetActive(true);
                bTr.SetPositionAndRotation(anchor, Quaternion.LookRotation(delta / len, Vector3.up));
                bTr.localScale = new Vector3(br.StartRadiusMeters, br.StartRadiusMeters, len);
            }
        }

        private PooledTreeVisualSlot RentOrCreateVisualSlot(OrganicFloraKind canopyKind, Material? foliageMat)
        {
            PooledTreeVisualSlot slot;
            if (_visualPool.Count > 0)
            {
                slot = _visualPool.Pop();
            }
            else
            {
                GameObject canopyGo = new GameObject($"OrganicTreeCanopy_{canopyKind}");
                if (_floraRootTransform != null) canopyGo.transform.SetParent(_floraRootTransform, false);
                MeshFilter canopyMf = canopyGo.AddComponent<MeshFilter>();
                MeshRenderer canopyMr = canopyGo.AddComponent<MeshRenderer>();
                canopyMr.shadowCastingMode = ShadowCastingMode.On;
                canopyMr.receiveShadows = true;

                GameObject rootGo = new GameObject("OrganicTreeRootButtress");
                if (_floraRootTransform != null) rootGo.transform.SetParent(_floraRootTransform, false);
                MeshFilter rootMf = rootGo.AddComponent<MeshFilter>();
                rootMf.sharedMesh = LoadFloraMeshCached(OrganicFloraKind.TreeRootButtress);
                MeshRenderer rootMr = rootGo.AddComponent<MeshRenderer>();
                rootMr.shadowCastingMode = ShadowCastingMode.On;
                rootMr.receiveShadows = true;

                var branchGos = new GameObject?[MaxBranchesPerTree];
                var branchTrs = new Transform?[MaxBranchesPerTree];
                var branchMrs = new MeshRenderer?[MaxBranchesPerTree];
                Mesh thickMesh = GetOrCreateUnitTaperedBranchMesh(isThickPrimary: true);
                Mesh thinMesh = GetOrCreateUnitTaperedBranchMesh(isThickPrimary: false);

                for (int b = 0; b < MaxBranchesPerTree; b++)
                {
                    bool isPrimarySlot = b < 5;
                    GameObject bGo = new GameObject(isPrimarySlot ? $"TreeBranch_ThickPrimary_{b}" : $"TreeBranch_ThinOuter_{b}");
                    if (_floraRootTransform != null) bGo.transform.SetParent(_floraRootTransform, false);
                    MeshFilter bMf = bGo.AddComponent<MeshFilter>();
                    bMf.sharedMesh = isPrimarySlot ? thickMesh : thinMesh;
                    MeshRenderer bMr = bGo.AddComponent<MeshRenderer>();
                    bMr.shadowCastingMode = isPrimarySlot ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    bMr.receiveShadows = true;
                    bGo.SetActive(false);

                    branchGos[b] = bGo;
                    branchTrs[b] = bGo.transform;
                    branchMrs[b] = bMr;
                }

                slot = new PooledTreeVisualSlot
                {
                    CanopyGo = canopyGo,
                    CanopyTr = canopyGo.transform,
                    CanopyMf = canopyMf,
                    CanopyMr = canopyMr,
                    AssignedCanopyKind = (OrganicFloraKind)255,
                    RootGo = rootGo,
                    RootTr = rootGo.transform,
                    RootMf = rootMf,
                    RootMr = rootMr,
                    BranchGos = branchGos,
                    BranchTrs = branchTrs,
                    BranchMrs = branchMrs
                };
            }

            if (foliageMat != null)
            {
                if (slot.CanopyMr != null && slot.CanopyMr.sharedMaterial != foliageMat)
                {
                    slot.CanopyMr.sharedMaterial = foliageMat;
                }
                if (slot.RootMr != null && slot.RootMr.sharedMaterial != foliageMat)
                {
                    slot.RootMr.sharedMaterial = foliageMat;
                }
                if (slot.BranchMrs != null)
                {
                    for (int b = 0; b < slot.BranchMrs.Length; b++)
                    {
                        MeshRenderer? bMr = slot.BranchMrs[b];
                        if (bMr != null && bMr.sharedMaterial != foliageMat)
                        {
                            bMr.sharedMaterial = foliageMat;
                        }
                    }
                }
            }

            if (slot.AssignedCanopyKind != canopyKind)
            {
                slot.AssignedCanopyKind = canopyKind;
                if (slot.CanopyMf != null)
                {
                    slot.CanopyMf.sharedMesh = LoadFloraMeshCached(canopyKind);
                }
            }

            return slot;
        }

        private void ReleaseVisualSlotToPool(ref PooledTreeVisualSlot slot)
        {
            if (slot.CanopyGo != null) slot.CanopyGo.SetActive(false);
            if (slot.RootGo != null) slot.RootGo.SetActive(false);
            if (slot.BranchGos != null)
            {
                for (int b = 0; b < slot.BranchGos.Length; b++)
                {
                    GameObject? bGo = slot.BranchGos[b];
                    if (bGo != null && bGo.activeSelf) bGo.SetActive(false);
                }
            }

            if (slot.CanopyGo != null || slot.RootGo != null)
            {
                if (_visualPool.Count < MaxVisualPoolCapacity)
                {
                    _visualPool.Push(slot);
                }
                else
                {
                    if (slot.CanopyGo != null) UnityEngine.Object.Destroy(slot.CanopyGo);
                    if (slot.RootGo != null) UnityEngine.Object.Destroy(slot.RootGo);
                    if (slot.BranchGos != null)
                    {
                        for (int b = 0; b < slot.BranchGos.Length; b++)
                        {
                            if (slot.BranchGos[b] != null) UnityEngine.Object.Destroy(slot.BranchGos[b]);
                        }
                    }
                }
            }
            slot = default;
        }

        private void RemoveTreeEntrySwapBack(int index)
        {
            int last = _activeTreeCount - 1;
            if (index < last)
            {
                ActiveOrganicTreeNode movedTree = _activeTrees[last];
                int srcBranchBase = last * MaxBranchesPerTree;
                int dstBranchBase = index * MaxBranchesPerTree;
                movedTree.FirstBranchIndex = dstBranchBase;
                for (int b = 0; b < movedTree.BranchCount; b++)
                {
                    OrganicTreeBranchNode br = _activeBranches[srcBranchBase + b];
                    br.TreeSlotIndex = index;
                    _activeBranches[dstBranchBase + b] = br;
                    _activeBranches[srcBranchBase + b] = default;
                }
                _activeTrees[index] = movedTree;
                _activeVisuals[index] = _activeVisuals[last];
            }
            else
            {
                int lastBranchBase = last * MaxBranchesPerTree;
                int count = _activeTrees[last].BranchCount;
                for (int b = 0; b < count; b++)
                {
                    _activeBranches[lastBranchBase + b] = default;
                }
            }
            _activeTrees[last] = default;
            _activeVisuals[last] = default;
            _activeTreeCount = last;
        }
#else
        private int[] _visualPoolTokens = new int[256];
        private readonly Stack<int> _visualPool = new(128);
        private int _nextPoolTokenId = 1;

        public int PooledTreeVisualCount => _visualPool.Count;

        public int SpawnOrSyncChunkTrees(
            int chunkX,
            int chunkZ,
            IReadOnlyDictionary<(int cx, int cz), uint[]>? worldChunks = null)
        {
            int chunkKey = PackChunkTreeKey(chunkX, chunkZ);
            if (_spawnedChunkKeys.Contains(chunkKey))
            {
                return CountTreesInChunk(chunkKey);
            }

            int specCount = ResolveDeterministicChunkTreeSpecs(chunkX, chunkZ, _chunkTreeScratch, worldChunks);
            return SpawnOrSyncChunkTrees(chunkX, chunkZ, _chunkTreeScratch.AsSpan(0, specCount));
        }

        public int SpawnOrSyncChunkTrees(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<(float WorldX, float CrownBaseY, float WorldZ, int TrunkHeight, int BiomeId, bool IsPine)> tupleSpecs)
        {
            int count = Math.Min(tupleSpecs.Length, _chunkTreeScratch.Length);
            for (int i = 0; i < count; i++)
            {
                var t = tupleSpecs[i];
                _chunkTreeScratch[i] = new OrganicTreePlacementSpec(
                    t.WorldX, t.CrownBaseY, t.WorldZ, t.TrunkHeight, t.BiomeId, t.IsPine);
            }
            return SpawnOrSyncChunkTrees(chunkX, chunkZ, _chunkTreeScratch.AsSpan(0, count));
        }

        public int SpawnOrSyncChunkTrees(
            int chunkX,
            int chunkZ,
            ReadOnlySpan<OrganicTreePlacementSpec> treeSpecs)
        {
            _activeInstance = this;
            int chunkKey = PackChunkTreeKey(chunkX, chunkZ);
            if (!_spawnedChunkKeys.Add(chunkKey))
            {
                return CountTreesInChunk(chunkKey);
            }

            for (int i = 0; i < treeSpecs.Length; i++)
            {
                AppendPureTreeNode(chunkKey, in treeSpecs[i]);
            }
            return treeSpecs.Length;
        }

        public int ReleaseChunkTrees(int chunkX, int chunkZ)
        {
            int chunkKey = PackChunkTreeKey(chunkX, chunkZ);
            if (!_spawnedChunkKeys.Remove(chunkKey))
            {
                return 0;
            }

            int released = 0;
            for (int i = _activeTreeCount - 1; i >= 0; i--)
            {
                if (_activeTrees[i].ChunkOwnerKey != chunkKey) continue;
                _visualPool.Push(_visualPoolTokens[i]);
                RemovePureSwapBack(i);
                released++;
            }
            return released;
        }

        public int NotifyTreeTruncatedOrFelledAt(int wx, int wy, int wz)
        {
            int felledCount = 0;
            for (int i = _activeTreeCount - 1; i >= 0; i--)
            {
                ref ActiveOrganicTreeNode t = ref _activeTrees[i];
                if (Math.Abs(wx - t.VoxelX) > 1 || Math.Abs(wz - t.VoxelZ) > 1) continue;
                if (wy < t.BaseVoxelY - 1 || wy > t.CrownVoxelY + 2) continue;

                _visualPool.Push(_visualPoolTokens[i]);
                RemovePureSwapBack(i);
                felledCount++;
            }
            return felledCount;
        }

        public void ClearAllTrees()
        {
            for (int i = _activeTreeCount - 1; i >= 0; i--)
            {
                _visualPool.Push(_visualPoolTokens[i]);
            }
            _activeTreeCount = 0;
            _spawnedChunkKeys.Clear();
            _brokenDropReadIndex = 0;
            _brokenDropWriteIndex = 0;
            _brokenDropCount = 0;
        }

        private int CountTreesInChunk(int chunkKey)
        {
            int c = 0;
            for (int i = 0; i < _activeTreeCount; i++)
            {
                if (_activeTrees[i].ChunkOwnerKey == chunkKey) c++;
            }
            return c;
        }

        private void AppendPureTreeNode(int chunkKey, in OrganicTreePlacementSpec spec)
        {
            if (_activeTreeCount >= _activeTrees.Length)
            {
                int nextCap = _activeTrees.Length * 2;
                Array.Resize(ref _activeTrees, nextCap);
                Array.Resize(ref _visualPoolTokens, nextCap);
                Array.Resize(ref _activeBranches, nextCap * MaxBranchesPerTree);
            }

            int vx = (int)MathF.Floor(spec.WorldX);
            int vz = (int)MathF.Floor(spec.WorldZ);
            int baseVy = (int)MathF.Round(spec.BaseY);
            int crownVy = (int)MathF.Round(spec.CrownBaseY);
            OrganicFloraKind canopyKind = SelectCanopyKindForTree(spec.BiomeId, spec.IsPine, spec.CrownBaseY);
            ComputeDeterministicVariation(vx, vz, spec.TrunkHeight, out float yawDeg, out float scale);

            int token = _visualPool.Count > 0 ? _visualPool.Pop() : _nextPoolTokenId++;
            int idx = _activeTreeCount++;
            int firstBranchIdx = idx * MaxBranchesPerTree;

            var node = new ActiveOrganicTreeNode
            {
                InstanceId = _nextInstanceId++,
                ChunkOwnerKey = chunkKey,
                VoxelX = vx,
                BaseVoxelY = baseVy,
                CrownVoxelY = crownVy,
                VoxelZ = vz,
                TrunkHeight = spec.TrunkHeight,
                BiomeId = spec.BiomeId,
                IsPine = spec.IsPine,
                WoodMaterialId = spec.WoodMaterialId,
                SpeciesKind = spec.SpeciesKind,
                CanopyKind = canopyKind,
                WorldX = spec.WorldX,
                BaseWorldY = spec.BaseY,
                CrownWorldY = spec.CrownBaseY,
                TrunkCollarWorldY = spec.CrownBaseY + 1.0f,
                WorldZ = spec.WorldZ,
                YawDegrees = yawDeg,
                UniformScale = scale,
                FirstBranchIndex = firstBranchIdx,
                IsCanopyActive = true,
                IsRootActive = true
            };

            int branchCount = GenerateSpeciesBranchHierarchyForTree(
                in node,
                idx,
                _nextBranchId,
                _activeBranches.AsSpan(firstBranchIdx, MaxBranchesPerTree),
                out int thickCount,
                out int thinCount);
            _nextBranchId += branchCount;

            node.BranchCount = branchCount;
            node.ThickPrimaryBranchCount = thickCount;
            node.ThinOuterBranchCount = thinCount;
            node.IntactBranchCount = branchCount;

            _activeTrees[idx] = node;
            _visualPoolTokens[idx] = token;
        }

        private void RemovePureSwapBack(int index)
        {
            int last = _activeTreeCount - 1;
            if (index < last)
            {
                ActiveOrganicTreeNode movedTree = _activeTrees[last];
                int srcBranchBase = last * MaxBranchesPerTree;
                int dstBranchBase = index * MaxBranchesPerTree;
                movedTree.FirstBranchIndex = dstBranchBase;
                for (int b = 0; b < movedTree.BranchCount; b++)
                {
                    OrganicTreeBranchNode br = _activeBranches[srcBranchBase + b];
                    br.TreeSlotIndex = index;
                    _activeBranches[dstBranchBase + b] = br;
                    _activeBranches[srcBranchBase + b] = default;
                }
                _activeTrees[index] = movedTree;
                _visualPoolTokens[index] = _visualPoolTokens[last];
            }
            else
            {
                int lastBranchBase = last * MaxBranchesPerTree;
                int count = _activeTrees[last].BranchCount;
                for (int b = 0; b < count; b++)
                {
                    _activeBranches[lastBranchBase + b] = default;
                }
            }
            _activeTrees[last] = default;
            _visualPoolTokens[last] = 0;
            _activeTreeCount = last;
        }
#endif
    }
}
