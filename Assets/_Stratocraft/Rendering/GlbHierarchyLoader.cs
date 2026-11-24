#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratocraft.Rendering
{
    /// <summary>
    /// Standartlara %100 uyumlu glTF 2.0 Binary (.glb) Hiyerarsi ve Color32 Mesh Yukleyicisi:
    /// - Regex yerine yapilandirilmis JSON semasi (nodes, meshes, primitives, accessors, bufferViews) okur.
    /// - COLOR_0 (UNSIGNED_BYTE 5121 / UNSIGNED_SHORT 5123 / FLOAT 5126) verisini dogrudan
    ///   StratocraftUberShader Color32 (R=MaterialID, G=Temperature, B=Sharpness, A=CavityAO) olarak paketler.
    /// - Tum Socket_* alt dugumlerini (Transform) gercek konumlariyla kurar; Socket_GoreCap_* kapaklarini gizler.
    /// </summary>
    public static class GlbHierarchyLoader
    {
        [Serializable]
        private sealed class GltfRoot
        {
            public GltfNode[]? nodes;
            public GltfMesh[]? meshes;
            public GltfBufferView[]? bufferViews;
            public GltfAccessor[]? accessors;
        }

        [Serializable]
        private sealed class GltfNode
        {
            public string? name;
            public int mesh = -1;
            public int[]? children;
            public float[]? matrix;
            public float[]? translation;
            public float[]? rotation;
            public float[]? scale;
        }

        [Serializable]
        private sealed class GltfMesh
        {
            public string? name;
            public GltfPrimitive[]? primitives;
        }

        [Serializable]
        private sealed class GltfPrimitive
        {
            public GltfAttributes? attributes;
            public int indices = -1;
        }

        [Serializable]
        private sealed class GltfAttributes
        {
            public int POSITION = -1;
            public int NORMAL = -1;
            public int COLOR_0 = -1;
        }

        [Serializable]
        private sealed class GltfBufferView
        {
            public int byteOffset;
            public int byteLength;
            public int byteStride;
        }

        [Serializable]
        private sealed class GltfAccessor
        {
            public int bufferView = -1;
            public int byteOffset;
            public int componentType;
            public int count;
            public string? type;
        }

        public sealed class LoadedGlbData
        {
            public Mesh CombinedMesh = null!;
            public readonly List<(string Name, Vector3 LocalPos, Quaternion LocalRot, Vector3 LocalScale)> Sockets = new();
        }

        private struct ArmorSlotCacheEntry
        {
            public string ResolvedPath;
            public long LastWriteTicks;
            public Mesh? LoadedMesh;
        }

        private static readonly Dictionary<string, (long LastWriteTicks, Dictionary<string, Mesh> Parts)> _articulatedPartsCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ArmorSlotCacheEntry> _armorSlotMeshCache =
            new(32, StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCachesForDomainReloadDisabled()
        {
            _articulatedPartsCache.Clear();
            _armorSlotMeshCache.Clear();
        }

        /// <summary>
        /// Vucut bolgesi indeksini (0=Head, 1=Torso, 2=Arms, 3=Legs, 4=Hands, 5=Pauldrons) ve zirh kademesini (1..5+)
        /// Assets/Art/Generated/Armors_15Piece altindaki gercek 15 parca 3 katmanli (layer1/layer2/layer3) .glb dosya adina esler.
        /// Sifir GC tahsisi (statik sabite donus) ile calisir.
        /// </summary>
        public static string ResolveArmorSlotGlbFileName(int hitZoneIndex, byte tier)
        {
            if (tier == 0)
            {
                return string.Empty;
            }

            int layer = tier == 1 ? 1 : (tier <= 3 ? 2 : 3);
            return hitZoneIndex switch
            {
                0 => layer switch
                {
                    1 => "armor_slot_head_layer1_padded.glb",
                    2 => "armor_slot_head_layer2_mail.glb",
                    _ => "armor_slot_head_layer3_plate.glb"
                },
                1 => layer switch
                {
                    1 => "armor_slot_torso_layer1_gambeson.glb",
                    2 => "armor_slot_torso_layer2_hauberk.glb",
                    _ => "armor_slot_torso_layer3_cuirass.glb"
                },
                2 => layer switch
                {
                    1 => "armor_slot_arms_layer1_sleeve.glb",
                    2 => "armor_slot_arms_layer2_vambrace_mail.glb",
                    _ => "armor_slot_arms_layer3_pauldron_plate.glb"
                },
                3 => layer switch
                {
                    1 => "armor_slot_legs_layer1_chausses_cloth.glb",
                    2 => "armor_slot_legs_layer2_chausses_mail.glb",
                    _ => "armor_slot_legs_layer3_greaves_plate.glb"
                },
                4 => layer switch
                {
                    1 => "armor_slot_hands_layer1_liner.glb",
                    2 => "armor_slot_hands_layer2_mail_glove.glb",
                    _ => "armor_slot_hands_layer3_gauntlet.glb"
                },
                5 => layer switch
                {
                    1 => "armor_slot_arms_layer1_sleeve.glb",
                    2 => "armor_slot_arms_layer2_vambrace_mail.glb",
                    _ => "armor_slot_arms_layer3_pauldron_plate.glb"
                },
                _ => string.Empty
            };
        }

        /// <summary>
        /// Assets/Art/Generated/Armors_15Piece altindaki 15 parca zirh .glb mesh'ini goreli yol ve dosya degisim zamaniyla
        /// onbellege alarak yukler. PlayMode kararli durumda 0 B/kare tahsis uretir.
        /// </summary>
        public static Mesh? LoadArmorSlotMeshCached(string slotGlbFileName)
        {
            if (string.IsNullOrEmpty(slotGlbFileName))
            {
                return null;
            }

            if (_armorSlotMeshCache.TryGetValue(slotGlbFileName, out ArmorSlotCacheEntry cached))
            {
                if (Application.isPlaying)
                {
                    if (cached.LoadedMesh != null || cached.LastWriteTicks < 0L)
                    {
                        return cached.LoadedMesh;
                    }
                }
                else
                {
                    long currentTicks = !string.IsNullOrEmpty(cached.ResolvedPath) && File.Exists(cached.ResolvedPath)
                        ? File.GetLastWriteTimeUtc(cached.ResolvedPath).Ticks
                        : -1L;
                    if (currentTicks == cached.LastWriteTicks && (currentTicks < 0L || cached.LoadedMesh != null))
                    {
                        return cached.LoadedMesh;
                    }
                }
            }

            string resolvedPath = ResolveArmorSlotGlbRelativeDiskPath(slotGlbFileName, out string cleanFileName);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                _armorSlotMeshCache[slotGlbFileName] = new ArmorSlotCacheEntry
                {
                    ResolvedPath = resolvedPath,
                    LastWriteTicks = -1L,
                    LoadedMesh = null
                };
                return null;
            }

            long diskTicks = File.GetLastWriteTimeUtc(resolvedPath).Ticks;
            string meshAssetName = $"ArmorSlot_{Path.GetFileNameWithoutExtension(cleanFileName)}";
            LoadedGlbData? loaded = LoadGlbFromDisk(resolvedPath, meshAssetName);
            Mesh? mesh = loaded != null ? loaded.CombinedMesh : null;

            _armorSlotMeshCache[slotGlbFileName] = new ArmorSlotCacheEntry
            {
                ResolvedPath = resolvedPath,
                LastWriteTicks = diskTicks,
                LoadedMesh = mesh
            };
            return mesh;
        }

        /// <summary>
        /// Stage1ArticulatedRigAnimator.RuntimeArmorMeshResolver (Func&lt;int, byte, Mesh?&gt;) ile sifir lambda/kapanis tahsisiyle
        /// dogrudan baglanabilen yardimci: ResolveArmorSlotGlbFileName + LoadArmorSlotMeshCached zincirini calistirir.
        /// </summary>
        public static Mesh? LoadArmorSlotMeshForZoneAndTier(int hitZoneIndex, byte tier)
        {
            string slotFileName = ResolveArmorSlotGlbFileName(hitZoneIndex, tier);
            return string.IsNullOrEmpty(slotFileName) ? null : LoadArmorSlotMeshCached(slotFileName);
        }

        public static void ClearArmorSlotMeshCache()
        {
            _armorSlotMeshCache.Clear();
        }

        private static string ResolveArmorSlotGlbRelativeDiskPath(string slotGlbFileName, out string cleanFileName)
        {
            string normalized = slotGlbFileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                ? slotGlbFileName
                : $"{slotGlbFileName}.glb";

            cleanFileName = Path.GetFileName(normalized);
            if (File.Exists(normalized))
            {
                return normalized;
            }

            string dataPath = Application.dataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                string viaDataPath = Path.Combine(dataPath, "Art", "Generated", "Armors_15Piece", cleanFileName);
                if (File.Exists(viaDataPath))
                {
                    return viaDataPath;
                }
            }

            string viaRelativeCwd = Path.Combine("Assets", "Art", "Generated", "Armors_15Piece", cleanFileName);
            if (File.Exists(viaRelativeCwd))
            {
                return viaRelativeCwd;
            }

            return !string.IsNullOrEmpty(dataPath)
                ? Path.Combine(dataPath, "Art", "Generated", "Armors_15Piece", cleanFileName)
                : viaRelativeCwd;
        }

        /// <summary>
        /// Cok-dugumlu eklemli bir .glb dosyasindaki her parca mesh'ini (orn. Human_Head, Human_Torso, Human_Hand_R)
        /// kendi yerel eklem pivotunda, sol-el Unity koordinatlarina (-X, Y, Z) ve dogrudan RGB Color32 renklerine
        /// cevrilmis ayri bir Mesh olarak yukler ve bellek icinde onbellege alir.
        /// </summary>
        public static Dictionary<string, Mesh>? LoadArticulatedPartsFromDisk(string glbDiskPath)
        {
            if (string.IsNullOrEmpty(glbDiskPath) || !File.Exists(glbDiskPath)) return null;
            long diskTicks = File.GetLastWriteTimeUtc(glbDiskPath).Ticks;
            if (_articulatedPartsCache.TryGetValue(glbDiskPath, out var cachedEntry) &&
                cachedEntry.LastWriteTicks == diskTicks &&
                cachedEntry.Parts != null)
            {
                bool allValid = cachedEntry.Parts.Count > 0;
                foreach (var kv in cachedEntry.Parts)
                {
                    if (kv.Value == null) { allValid = false; break; }
                }
                if (allValid) return cachedEntry.Parts;
            }

            byte[] bytes = File.ReadAllBytes(glbDiskPath);
            if (bytes.Length < 28 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67u) return null;

            int offset = 12;
            string? jsonText = null;
            int binStart = -1;
            int binLength = 0;
            while (offset + 8 <= bytes.Length)
            {
                int chunkLen = BitConverter.ToInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (chunkLen < 0 || dataStart + chunkLen > bytes.Length) break;
                if (chunkType == 0x4E4F534Au) jsonText = Encoding.UTF8.GetString(bytes, dataStart, chunkLen);
                else if (chunkType == 0x004E4942u) { binStart = dataStart; binLength = chunkLen; }
                offset = dataStart + chunkLen;
            }
            if (string.IsNullOrEmpty(jsonText) || binStart < 0 || binLength <= 0) return null;

            GltfRoot? root = JsonUtility.FromJson<GltfRoot>(jsonText);
            if (root?.meshes == null || root.meshes.Length == 0 || root.accessors == null || root.bufferViews == null) return null;

            // Node -> mesh eslesmesinden parca adlarini cozumle
            var meshIndexToName = new Dictionary<int, string>();
            if (root.nodes != null)
            {
                for (int n = 0; n < root.nodes.Length; n++)
                {
                    GltfNode node = root.nodes[n];
                    if (node.mesh >= 0 && !string.IsNullOrEmpty(node.name) && !meshIndexToName.ContainsKey(node.mesh))
                    {
                        meshIndexToName[node.mesh] = node.name!;
                    }
                }
            }

            var result = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
            for (int m = 0; m < root.meshes.Length; m++)
            {
                GltfMesh gMesh = root.meshes[m];
                if (gMesh.primitives == null || gMesh.primitives.Length == 0) continue;

                string partName = meshIndexToName.TryGetValue(m, out string? nName)
                    ? nName
                    : (!string.IsNullOrEmpty(gMesh.name) ? gMesh.name! : $"Part_{m}");

                var verts = new List<Vector3>(256);
                var norms = new List<Vector3>(256);
                var cols = new List<Color32>(256);
                var tris = new List<int>(512);

                for (int p = 0; p < gMesh.primitives.Length; p++)
                {
                    GltfPrimitive prim = gMesh.primitives[p];
                    if (prim.attributes == null || prim.attributes.POSITION < 0) continue;

                    Vector3[] posArr = ReadVec3Accessor(bytes, binStart, root, prim.attributes.POSITION);
                    int vCount = posArr.Length;
                    if (vCount == 0) continue;

                    Vector3[] nrmArr = prim.attributes.NORMAL >= 0
                        ? ReadVec3Accessor(bytes, binStart, root, prim.attributes.NORMAL)
                        : new Vector3[vCount];
                    if (nrmArr.Length != vCount) Array.Resize(ref nrmArr, vCount);

                    Color32[] colArr = prim.attributes.COLOR_0 >= 0
                        ? ReadColor32Accessor(bytes, binStart, root, prim.attributes.COLOR_0, vCount, preserveZeroRed: true)
                        : CreateDefaultColorArray(vCount, defaultMatId: 200);

                    int[] idxArr = prim.indices >= 0
                        ? ReadIndicesAccessor(bytes, binStart, root, prim.indices)
                        : CreateSequentialIndices(vCount);

                    int baseVertex = verts.Count;
                    for (int i = 0; i < vCount; i++)
                    {
                        // glTF (+Y up, +Z front, right-handed) -> Unity (+Y up, +Z front, left-handed): X eksenini yansit + ucgen sarimini cevir
                        verts.Add(new Vector3(-posArr[i].x, posArr[i].y, posArr[i].z));
                        norms.Add(new Vector3(-nrmArr[i].x, nrmArr[i].y, nrmArr[i].z));
                        cols.Add(colArr[i]);
                    }
                    for (int i = 0; i + 2 < idxArr.Length; i += 3)
                    {
                        tris.Add(baseVertex + idxArr[i]);
                        tris.Add(baseVertex + idxArr[i + 2]);
                        tris.Add(baseVertex + idxArr[i + 1]);
                    }
                }

                if (verts.Count == 0 || tris.Count == 0) continue;
                Mesh mesh = new Mesh
                {
                    name = $"Articulated_{partName}",
                    indexFormat = verts.Count > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetColors(cols);
                mesh.SetTriangles(tris, 0, calculateBounds: true);
                if (norms[0].sqrMagnitude < 1e-4f) mesh.RecalculateNormals();
                result[partName] = mesh;
            }

            if (result.Count > 0)
            {
                _articulatedPartsCache[glbDiskPath] = (diskTicks, result);
                return result;
            }
            return null;
        }

        public static LoadedGlbData? LoadGlbFromDisk(
            string glbDiskPath,
            string meshName,
            bool preserveLinearVertexRgb = false)
        {
            if (!File.Exists(glbDiskPath))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(glbDiskPath);
            if (bytes.Length < 28)
            {
                return null;
            }

            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic != 0x46546C67u) // 'glTF'
            {
                return null;
            }

            int offset = 12;
            string? jsonText = null;
            int binStart = -1;
            int binLength = 0;

            while (offset + 8 <= bytes.Length)
            {
                int chunkLen = BitConverter.ToInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                int dataStart = offset + 8;
                if (chunkLen < 0 || dataStart + chunkLen > bytes.Length)
                {
                    break;
                }

                if (chunkType == 0x4E4F534Au) // 'JSON'
                {
                    jsonText = Encoding.UTF8.GetString(bytes, dataStart, chunkLen);
                }
                else if (chunkType == 0x004E4942u) // 'BIN\0'
                {
                    binStart = dataStart;
                    binLength = chunkLen;
                }

                offset = dataStart + chunkLen;
            }

            if (string.IsNullOrEmpty(jsonText) || binStart < 0 || binLength <= 0)
            {
                return null;
            }

            GltfRoot? root = JsonUtility.FromJson<GltfRoot>(jsonText);
            if (root?.meshes == null || root.meshes.Length == 0 || root.accessors == null || root.bufferViews == null)
            {
                return null;
            }

            var allVerts = new List<Vector3>(1024);
            var allNorms = new List<Vector3>(1024);
            var allColors = new List<Color32>(1024);
            var allTris = new List<int>(2048);

            // Dugum hiyerarsisinden (parent -> child TRS / matrix) tum mesh orneklerinin dunya matrislerini hesapla
            var meshInstances = new List<(int MeshIndex, Matrix4x4 WorldMatrix, bool IsIdentity)>(root.meshes.Length);
            if (root.nodes != null && root.nodes.Length > 0)
            {
                int nodeCount = root.nodes.Length;
                bool[] hasParent = new bool[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    int[]? ch = root.nodes[n].children;
                    if (ch == null) continue;
                    for (int c = 0; c < ch.Length; c++)
                    {
                        if ((uint)ch[c] < (uint)nodeCount) hasParent[ch[c]] = true;
                    }
                }

                for (int n = 0; n < nodeCount; n++)
                {
                    if (!hasParent[n])
                    {
                        CollectNodeMeshInstances(root.nodes, n, Matrix4x4.identity, parentIsIdentity: true, meshInstances, depth: 0);
                    }
                }
            }

            if (meshInstances.Count == 0)
            {
                for (int m = 0; m < root.meshes.Length; m++)
                {
                    meshInstances.Add((m, Matrix4x4.identity, true));
                }
            }

            for (int inst = 0; inst < meshInstances.Count; inst++)
            {
                var (mIdx, worldMat, isIdentity) = meshInstances[inst];
                if ((uint)mIdx >= (uint)root.meshes.Length) continue;
                GltfPrimitive[]? prims = root.meshes[mIdx].primitives;
                if (prims == null) continue;

                Matrix4x4 normalMat = isIdentity ? Matrix4x4.identity : worldMat.inverse.transpose;
                bool hasNegativeDet = !isIdentity && worldMat.determinant < 0f;

                for (int p = 0; p < prims.Length; p++)
                {
                    GltfPrimitive prim = prims[p];
                    if (prim.attributes == null || prim.attributes.POSITION < 0) continue;

                    Vector3[] posArr = ReadVec3Accessor(bytes, binStart, root, prim.attributes.POSITION);
                    int vCount = posArr.Length;
                    if (vCount == 0) continue;

                    Vector3[] nrmArr = prim.attributes.NORMAL >= 0
                        ? ReadVec3Accessor(bytes, binStart, root, prim.attributes.NORMAL)
                        : new Vector3[vCount];
                    if (nrmArr.Length != vCount)
                    {
                        Array.Resize(ref nrmArr, vCount);
                    }

                    Color32[] colArr = prim.attributes.COLOR_0 >= 0
                        ? ReadColor32Accessor(bytes, binStart, root, prim.attributes.COLOR_0, vCount, preserveZeroRed: preserveLinearVertexRgb)
                        : CreateDefaultColorArray(vCount, defaultMatId: 51);

                    int[] idxArr = prim.indices >= 0
                        ? ReadIndicesAccessor(bytes, binStart, root, prim.indices)
                        : CreateSequentialIndices(vCount);

                    int baseVertex = allVerts.Count;
                    for (int i = 0; i < vCount; i++)
                    {
                        Vector3 vp = isIdentity ? posArr[i] : worldMat.MultiplyPoint3x4(posArr[i]);
                        Vector3 np = isIdentity ? nrmArr[i] : normalMat.MultiplyVector(nrmArr[i]);
                        if (np.sqrMagnitude > 1e-6f) np.Normalize();

                        // glTF 2.0 Sag-El (RH, +Y up) -> Unity Sol-El (LH, +Y up): X ekseni yansit + ucgen sarimini cevir
                        allVerts.Add(new Vector3(-vp.x, vp.y, vp.z));
                        allNorms.Add(new Vector3(-np.x, np.y, np.z));

                        Color32 c = colArr[i];
                        if (preserveLinearVertexRgb && (c.r == 0 || c.r == 3 || c.r == 11))
                        {
                            c.r = (byte)(c.r + 1);
                        }
                        allColors.Add(c);
                    }

                    bool flipWinding = !hasNegativeDet;
                    for (int i = 0; i + 2 < idxArr.Length; i += 3)
                    {
                        if (flipWinding)
                        {
                            allTris.Add(baseVertex + idxArr[i]);
                            allTris.Add(baseVertex + idxArr[i + 2]);
                            allTris.Add(baseVertex + idxArr[i + 1]);
                        }
                        else
                        {
                            allTris.Add(baseVertex + idxArr[i]);
                            allTris.Add(baseVertex + idxArr[i + 1]);
                            allTris.Add(baseVertex + idxArr[i + 2]);
                        }
                    }
                }
            }

            if (allVerts.Count == 0 || allTris.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = allVerts.Count > 65532 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(allVerts);
            mesh.SetNormals(allNorms);
            mesh.SetColors(allColors);
            mesh.SetTriangles(allTris, 0, calculateBounds: true);

            if (allNorms[0].sqrMagnitude < 1e-4f)
            {
                mesh.RecalculateNormals();
            }

            LoadedGlbData result = new LoadedGlbData { CombinedMesh = mesh };
            if (root.nodes != null)
            {
                for (int n = 0; n < root.nodes.Length; n++)
                {
                    GltfNode node = root.nodes[n];
                    if (node.mesh >= 0 || string.IsNullOrEmpty(node.name))
                    {
                        continue;
                    }

                    Vector3 t = (node.translation != null && node.translation.Length >= 3)
                        ? new Vector3(-node.translation[0], node.translation[1], node.translation[2])
                        : Vector3.zero;
                    Quaternion r = (node.rotation != null && node.rotation.Length >= 4)
                        ? new Quaternion(node.rotation[0], -node.rotation[1], -node.rotation[2], node.rotation[3])
                        : Quaternion.identity;
                    Vector3 s = (node.scale != null && node.scale.Length >= 3)
                        ? new Vector3(node.scale[0], node.scale[1], node.scale[2])
                        : Vector3.one;

                    result.Sockets.Add((node.name!, t, r, s));
                }
            }

            return result;
        }

        private static void CollectNodeMeshInstances(
            GltfNode[] nodes,
            int nodeIdx,
            Matrix4x4 parentWorld,
            bool parentIsIdentity,
            List<(int MeshIndex, Matrix4x4 WorldMatrix, bool IsIdentity)> outInstances,
            int depth)
        {
            if ((uint)nodeIdx >= (uint)nodes.Length || depth > 32) return;
            GltfNode node = nodes[nodeIdx];

            Matrix4x4 local = BuildGltfNodeLocalMatrix(node, out bool localIsIdentity);
            bool worldIsIdentity = parentIsIdentity && localIsIdentity;
            Matrix4x4 world = worldIsIdentity ? Matrix4x4.identity : (parentIsIdentity ? local : (localIsIdentity ? parentWorld : parentWorld * local));

            if (node.mesh >= 0)
            {
                outInstances.Add((node.mesh, world, worldIsIdentity));
            }

            if (node.children != null)
            {
                for (int i = 0; i < node.children.Length; i++)
                {
                    CollectNodeMeshInstances(nodes, node.children[i], world, worldIsIdentity, outInstances, depth + 1);
                }
            }
        }

        private static Matrix4x4 BuildGltfNodeLocalMatrix(GltfNode node, out bool isIdentity)
        {
            if (node.matrix != null && node.matrix.Length == 16)
            {
                isIdentity = false;
                var m = new Matrix4x4();
                m.SetColumn(0, new Vector4(node.matrix[0], node.matrix[1], node.matrix[2], node.matrix[3]));
                m.SetColumn(1, new Vector4(node.matrix[4], node.matrix[5], node.matrix[6], node.matrix[7]));
                m.SetColumn(2, new Vector4(node.matrix[8], node.matrix[9], node.matrix[10], node.matrix[11]));
                m.SetColumn(3, new Vector4(node.matrix[12], node.matrix[13], node.matrix[14], node.matrix[15]));
                return m;
            }

            bool hasT = node.translation != null && node.translation.Length >= 3 &&
                        (Mathf.Abs(node.translation[0]) > 1e-5f || Mathf.Abs(node.translation[1]) > 1e-5f || Mathf.Abs(node.translation[2]) > 1e-5f);
            bool hasR = node.rotation != null && node.rotation.Length >= 4 &&
                        (Mathf.Abs(node.rotation[0]) > 1e-5f || Mathf.Abs(node.rotation[1]) > 1e-5f || Mathf.Abs(node.rotation[2]) > 1e-5f || Mathf.Abs(node.rotation[3] - 1f) > 1e-5f);
            bool hasS = node.scale != null && node.scale.Length >= 3 &&
                        (Mathf.Abs(node.scale[0] - 1f) > 1e-5f || Mathf.Abs(node.scale[1] - 1f) > 1e-5f || Mathf.Abs(node.scale[2] - 1f) > 1e-5f);

            if (!hasT && !hasR && !hasS)
            {
                isIdentity = true;
                return Matrix4x4.identity;
            }

            isIdentity = false;
            Vector3 t = hasT ? new Vector3(node.translation![0], node.translation[1], node.translation[2]) : Vector3.zero;
            Quaternion r = hasR ? new Quaternion(node.rotation![0], node.rotation[1], node.rotation[2], node.rotation[3]) : Quaternion.identity;
            Vector3 s = hasS ? new Vector3(node.scale![0], node.scale[1], node.scale[2]) : Vector3.one;
            return Matrix4x4.TRS(t, r, s);
        }

        /// <summary>
        /// Yuklenen GLB modelinin tum Socket_* dugumlerini GameObject altina Transform olarak ekler;
        /// Socket_GoreCap_* dugumlerini uzuv kopana kadar otomatik gizler.
        /// </summary>
        public static void AttachSocketHierarchy(
            Transform modelRoot,
            List<(string Name, Vector3 LocalPos, Quaternion LocalRot, Vector3 LocalScale)> sockets)
        {
            for (int i = 0; i < sockets.Count; i++)
            {
                var (sName, sPos, sRot, sScale) = sockets[i];
                GameObject sockGo = new GameObject(sName);
                sockGo.transform.SetParent(modelRoot, false);
                sockGo.transform.localPosition = sPos;
                sockGo.transform.localRotation = sRot;
                sockGo.transform.localScale = sScale;

                if (sName.StartsWith("Socket_GoreCap_", StringComparison.OrdinalIgnoreCase))
                {
                    sockGo.SetActive(false);
                }
            }
        }

        private static Vector3[] ReadVec3Accessor(byte[] bytes, int binStart, GltfRoot root, int accessorIdx)
        {
            GltfAccessor acc = root.accessors![accessorIdx];
            GltfBufferView bv = root.bufferViews![acc.bufferView];
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

        private static Color32[] ReadColor32Accessor(
            byte[] bytes,
            int binStart,
            GltfRoot root,
            int accessorIdx,
            int expectedCount,
            bool preserveZeroRed = false)
        {
            GltfAccessor acc = root.accessors![accessorIdx];
            GltfBufferView bv = root.bufferViews![acc.bufferView];
            bool isVec4 = string.Equals(acc.type, "VEC4", StringComparison.OrdinalIgnoreCase);
            int compCount = isVec4 ? 4 : 3;
            int count = Mathf.Min(acc.count, expectedCount);
            Color32[] cols = new Color32[expectedCount];

            int elemBytes = acc.componentType switch
            {
                5121 => 1, // UNSIGNED_BYTE
                5123 => 2, // UNSIGNED_SHORT
                5126 => 4, // FLOAT
                _ => 1
            };
            int stride = bv.byteStride > 0 ? bv.byteStride : (compCount * elemBytes);
            int basePtr = binStart + bv.byteOffset + acc.byteOffset;

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
                    // glTF COLOR_0 linear uint16 -> sRGB byte (preserveZeroRed = gercek RGB insan dokusu)
                    float rf = BitConverter.ToUInt16(bytes, p) / 65535f;
                    float gf = BitConverter.ToUInt16(bytes, p + 2) / 65535f;
                    float bf = BitConverter.ToUInt16(bytes, p + 4) / 65535f;
                    if (preserveZeroRed)
                    {
                        rf = Mathf.LinearToGammaSpace(rf);
                        gf = Mathf.LinearToGammaSpace(gf);
                        bf = Mathf.LinearToGammaSpace(bf);
                    }
                    r = (byte)Mathf.Clamp(Mathf.RoundToInt(rf * 255f), 0, 255);
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(gf * 255f), 0, 255);
                    b = (byte)Mathf.Clamp(Mathf.RoundToInt(bf * 255f), 0, 255);
                    a = isVec4 ? (byte)(BitConverter.ToUInt16(bytes, p + 6) >> 8) : (byte)245;
                }
                else
                {
                    float rf = BitConverter.ToSingle(bytes, p);
                    float gf = BitConverter.ToSingle(bytes, p + 4);
                    float bf = BitConverter.ToSingle(bytes, p + 8);
                    if (preserveZeroRed)
                    {
                        rf = Mathf.LinearToGammaSpace(rf);
                        gf = Mathf.LinearToGammaSpace(gf);
                        bf = Mathf.LinearToGammaSpace(bf);
                    }
                    r = (byte)Mathf.Clamp(Mathf.RoundToInt(rf * 255f), 0, 255);
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(gf * 255f), 0, 255);
                    b = (byte)Mathf.Clamp(Mathf.RoundToInt(bf * 255f), 0, 255);
                    a = isVec4
                        ? (byte)Mathf.Clamp(Mathf.RoundToInt(BitConverter.ToSingle(bytes, p + 12) * 255f), 0, 255)
                        : (byte)245;
                }

                cols[i] = new Color32((!preserveZeroRed && r == 0) ? (byte)51 : r, g, b, a);
            }

            return cols;
        }

        private static int[] ReadIndicesAccessor(byte[] bytes, int binStart, GltfRoot root, int accessorIdx)
        {
            GltfAccessor acc = root.accessors![accessorIdx];
            GltfBufferView bv = root.bufferViews![acc.bufferView];
            int basePtr = binStart + bv.byteOffset + acc.byteOffset;
            int[] idx = new int[acc.count];

            if (acc.componentType == 5125) // UNSIGNED_INT
            {
                for (int i = 0; i < acc.count; i++)
                {
                    idx[i] = (int)BitConverter.ToUInt32(bytes, basePtr + i * 4);
                }
            }
            else if (acc.componentType == 5123) // UNSIGNED_SHORT
            {
                for (int i = 0; i < acc.count; i++)
                {
                    idx[i] = BitConverter.ToUInt16(bytes, basePtr + i * 2);
                }
            }
            else // UNSIGNED_BYTE (5121)
            {
                for (int i = 0; i < acc.count; i++)
                {
                    idx[i] = bytes[basePtr + i];
                }
            }

            return idx;
        }

        private static Color32[] CreateDefaultColorArray(int count, byte defaultMatId)
        {
            Color32[] arr = new Color32[count];
            Color32 c = new Color32(defaultMatId, 0, 200, 245);
            Array.Fill(arr, c);
            return arr;
        }

        private static int[] CreateSequentialIndices(int count)
        {
            int[] arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = i;
            return arr;
        }
    }
}
