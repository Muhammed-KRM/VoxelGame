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
        }

        public OrganicTreePlacementSpec(
            float worldX,
            float baseY,
            float crownBaseY,
            float worldZ,
            int trunkHeight,
            int biomeId,
            bool isPine)
        {
            WorldX = worldX;
            BaseY = baseY;
            CrownBaseY = crownBaseY;
            WorldZ = worldZ;
            TrunkHeight = Math.Max(1, trunkHeight);
            BiomeId = biomeId;
            IsPine = isPine;
        }
    }

    /// <summary>
    /// Sahnede aktif olan tekil organik 3B agac taci + kok mahmuzu dugumu (0-GC deger turu).
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
        public OrganicFloraKind CanopyKind;
        public float WorldX;
        public float BaseWorldY;
        public float CrownWorldY;
        public float WorldZ;
        public float YawDegrees;
        public float UniformScale;
        public bool IsCanopyActive;
        public bool IsRootActive;
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
    /// Minecraft tipi 1m³ kup agac yapraklarini gercek 3B katmanli Nordik cam, karli cam, cok-kumeli mese taclari
    /// ve egim telafili kok mahmuzlariyla degistiren 0-GC havuzlu gorsel sistem.
    /// Hem .NET 10 saf birim testlerinde hem de Unity 6 sahnesinde calisir.
    /// </summary>
    public sealed class OrganicTreeCanopyVisualSystem
    {
        public const int FloraAssetCount = 4;

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
            bool snowy = biomeId == 4 || biomeId == 11 || crownBaseY >= 110.0f;
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
        /// agaclarin (WorldX, BaseY, CrownBaseY, WorldZ, TrunkHeight, BiomeId, IsPine) listesini cikarir (0 B GC).
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
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(5 + seedShiftZ, 3, 7), 6, outSpecs, ref writeCount);
            }
            else if (chunkX == 0 && chunkZ == 0)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(11 + seedShiftZ, 10, 12), 7, outSpecs, ref writeCount);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(11 + seedShiftZ, 10, 12), 8, outSpecs, ref writeCount);
            }
            else if (chunkX == -1 && chunkZ == 0)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(12 + seedShiftX, 11, 12), Math.Clamp(3 + seedShiftZ, 3, 5), 9, outSpecs, ref writeCount);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 5), Math.Clamp(12 + seedShiftZ, 11, 12), 7, outSpecs, ref writeCount);
            }
            else if (chunkX == 1 && chunkZ == -1)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(12 + seedShiftZ, 10, 12), 7, outSpecs, ref writeCount);
            }
            else if (chunkX == 1 && chunkZ == 0)
            {
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, Math.Clamp(3 + seedShiftX, 3, 4), Math.Clamp(4 + seedShiftZ, 3, 6), 7, outSpecs, ref writeCount);
            }
            else
            {
                int h1 = Math.Abs((chunkX * 73856093) ^ (chunkZ * 19349663) ^ seed);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, 3 + (h1 % 4), 3 + ((h1 >> 4) % 4), 6 + (h1 % 5), outSpecs, ref writeCount);
                TryAppendDeterministicCandidate(
                    chunkX, chunkZ, 9 + ((h1 >> 8) % 4), 9 + ((h1 >> 12) % 4), 6 + ((h1 >> 3) % 5), outSpecs, ref writeCount);
            }

            return writeCount;
        }

        private static void TryAppendDeterministicCandidate(
            int chunkX,
            int chunkZ,
            int lx,
            int lz,
            int trunkHeight,
            Span<OrganicTreePlacementSpec> outSpecs,
            ref int writeCount)
        {
            if (writeCount >= outSpecs.Length) return;
            lx = Math.Clamp(lx, 3, 12);
            lz = Math.Clamp(lz, 3, 12);

            if (StratocraftFloatingIslandWorldGen.TryGetChunkModularStructureSite(chunkX, chunkZ, out ProceduralModularSiteSpec modSite) &&
                Math.Abs(lx - modSite.LocalCenterX) <= modSite.TerraceHalfExtentBlocks &&
                Math.Abs(lz - modSite.LocalCenterZ) <= modSite.TerraceHalfExtentBlocks)
            {
                return;
            }

            int wx = chunkX * 16 + lx;
            int wz = chunkZ * 16 + lz;
            int biomeId = StratocraftFloatingIslandWorldGen.GetBiomeId(wx, wz);
            bool isCoreChunk = chunkX >= -1 && chunkX <= 1 && chunkZ >= -1 && chunkZ <= 0;
            if (!isCoreChunk && (biomeId == 2 || biomeId == 6 || biomeId == 7 || biomeId == 8 || biomeId == 9 || biomeId == 11))
            {
                return;
            }

            float surfTopY = StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(wx, wz);
            float r = MathF.Sqrt((float)wx * wx + (float)wz * wz);
            if (r > 24.0f && surfTopY < StratocraftFloatingIslandWorldGen.SeaLevelY + 1.2f)
            {
                return;
            }

            int baseY = Math.Clamp(
                (int)MathF.Round(surfTopY),
                12,
                StratocraftFloatingIslandWorldGen.MaxEditableVoxelY - 18);
            int effectiveTrunkH = Math.Clamp(trunkHeight, 5, 10);
            bool isPine = biomeId == 4 || biomeId == 5;

            outSpecs[writeCount++] = new OrganicTreePlacementSpec(
                worldX: wx + 0.5f,
                baseY: baseY,
                crownBaseY: baseY + effectiveTrunkH,
                worldZ: wz + 0.5f,
                trunkHeight: effectiveTrunkH,
                biomeId: biomeId,
                isPine: isPine);
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
                for (int lx = 2; lx <= 13 && writeCount < outSpecs.Length; lx++)
                {
                    int col = (lz << 4) | lx;
                    int minTrunkY = -1;
                    int maxTrunkY = -1;

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
                            if (minTrunkY < 0) minTrunkY = y;
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
                        isPine: isPine);
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
            float trunkFactor = Math.Clamp(0.88f + (trunkHeight - 5) * 0.045f, 0.85f, 1.18f);
            float jitter = 0.94f + (((h >> 10) & 255u) / 255.0f) * 0.12f;
            uniformScale = trunkFactor * jitter;
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
        }

        private static readonly Mesh?[] CachedFloraMeshes = new Mesh?[FloraAssetCount];
        private static readonly bool[] AttemptedFloraLoads = new bool[FloraAssetCount];
        private static Material? _sharedOrganicFoliageMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesOnSubsystemRegistration()
        {
            Array.Clear(CachedFloraMeshes, 0, CachedFloraMeshes.Length);
            Array.Clear(AttemptedFloraLoads, 0, AttemptedFloraLoads.Length);
            _sharedOrganicFoliageMaterial = null;
        }

        private ActiveOrganicTreeNode[] _activeTrees = new ActiveOrganicTreeNode[256];
        private PooledTreeVisualSlot[] _activeVisuals = new PooledTreeVisualSlot[256];
        private readonly OrganicTreePlacementSpec[] _chunkTreeScratch = new OrganicTreePlacementSpec[12];
        private readonly Stack<PooledTreeVisualSlot> _visualPool = new(128);
        private readonly HashSet<int> _spawnedChunkKeys = new(128);
        private Transform? _floraRootTransform;
        private int _activeTreeCount;
        private int _nextInstanceId = 1;

        public ReadOnlySpan<ActiveOrganicTreeNode> ActiveTreesSpan => _activeTrees.AsSpan(0, _activeTreeCount);
        public int ActiveTreeCount => _activeTreeCount;
        public int PooledTreeVisualCount => _visualPool.Count;

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
            }

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
            return _sharedOrganicFoliageMaterial;
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
        /// 3B yaprak tacini (ve kok kesildiyse kok mahmuzunu) aninda temizleyip 0-GC havuza iade eder!
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
                vis.RootTr.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.12f, Mathf.InverseLerp(0.85f, 1.18f, scale));
            }
            if (vis.RootGo != null && !vis.RootGo.activeSelf)
            {
                vis.RootGo.SetActive(true);
            }

            int idx = _activeTreeCount++;
            _activeTrees[idx] = new ActiveOrganicTreeNode
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
                CanopyKind = canopyKind,
                WorldX = spec.WorldX,
                BaseWorldY = spec.BaseY,
                CrownWorldY = spec.CrownBaseY,
                WorldZ = spec.WorldZ,
                YawDegrees = yawDeg,
                UniformScale = scale,
                IsCanopyActive = true,
                IsRootActive = true
            };
            _activeVisuals[idx] = vis;
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
                    RootMr = rootMr
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
            if (slot.CanopyGo != null || slot.RootGo != null)
            {
                _visualPool.Push(slot);
            }
            slot = default;
        }

        private void RemoveTreeEntrySwapBack(int index)
        {
            int last = _activeTreeCount - 1;
            if (index < last)
            {
                _activeTrees[index] = _activeTrees[last];
                _activeVisuals[index] = _activeVisuals[last];
            }
            _activeTrees[last] = default;
            _activeVisuals[last] = default;
            _activeTreeCount = last;
        }
#else
        private ActiveOrganicTreeNode[] _activeTrees = new ActiveOrganicTreeNode[256];
        private int[] _visualPoolTokens = new int[256];
        private readonly OrganicTreePlacementSpec[] _chunkTreeScratch = new OrganicTreePlacementSpec[12];
        private readonly Stack<int> _visualPool = new(128);
        private readonly HashSet<int> _spawnedChunkKeys = new(128);
        private int _activeTreeCount;
        private int _nextInstanceId = 1;
        private int _nextPoolTokenId = 1;

        public ReadOnlySpan<ActiveOrganicTreeNode> ActiveTreesSpan => _activeTrees.AsSpan(0, _activeTreeCount);
        public int ActiveTreeCount => _activeTreeCount;
        public int PooledTreeVisualCount => _visualPool.Count;

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
            }

            int vx = (int)MathF.Floor(spec.WorldX);
            int vz = (int)MathF.Floor(spec.WorldZ);
            int baseVy = (int)MathF.Round(spec.BaseY);
            int crownVy = (int)MathF.Round(spec.CrownBaseY);
            OrganicFloraKind canopyKind = SelectCanopyKindForTree(spec.BiomeId, spec.IsPine, spec.CrownBaseY);
            ComputeDeterministicVariation(vx, vz, spec.TrunkHeight, out float yawDeg, out float scale);

            int token = _visualPool.Count > 0 ? _visualPool.Pop() : _nextPoolTokenId++;
            int idx = _activeTreeCount++;
            _activeTrees[idx] = new ActiveOrganicTreeNode
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
                CanopyKind = canopyKind,
                WorldX = spec.WorldX,
                BaseWorldY = spec.BaseY,
                CrownWorldY = spec.CrownBaseY,
                WorldZ = spec.WorldZ,
                YawDegrees = yawDeg,
                UniformScale = scale,
                IsCanopyActive = true,
                IsRootActive = true
            };
            _visualPoolTokens[idx] = token;
        }

        private void RemovePureSwapBack(int index)
        {
            int last = _activeTreeCount - 1;
            if (index < last)
            {
                _activeTrees[index] = _activeTrees[last];
                _visualPoolTokens[index] = _visualPoolTokens[last];
            }
            _activeTrees[last] = default;
            _visualPoolTokens[last] = 0;
            _activeTreeCount = last;
        }
#endif
    }
}
