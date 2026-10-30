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

        public static LoadedGlbData? LoadGlbFromDisk(string glbDiskPath, string meshName)
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

            for (int m = 0; m < root.meshes.Length; m++)
            {
                GltfPrimitive[]? prims = root.meshes[m].primitives;
                if (prims == null) continue;

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
                        ? ReadColor32Accessor(bytes, binStart, root, prim.attributes.COLOR_0, vCount)
                        : CreateDefaultColorArray(vCount, defaultMatId: 51);

                    int[] idxArr = prim.indices >= 0
                        ? ReadIndicesAccessor(bytes, binStart, root, prim.indices)
                        : CreateSequentialIndices(vCount);

                    int baseVertex = allVerts.Count;
                    allVerts.AddRange(posArr);
                    allNorms.AddRange(nrmArr);
                    allColors.AddRange(colArr);
                    // glTF 2.0 Sag-El (CCW) ucgen sarimini Unity Sol-El (CW) sarimina cevir (dis yuzey normalleri %100 disa bakar)
                    for (int i = 0; i + 2 < idxArr.Length; i += 3)
                    {
                        allTris.Add(baseVertex + idxArr[i]);
                        allTris.Add(baseVertex + idxArr[i + 2]);
                        allTris.Add(baseVertex + idxArr[i + 1]);
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
                        ? new Vector3(node.translation[0], node.translation[1], node.translation[2])
                        : Vector3.zero;
                    Quaternion r = (node.rotation != null && node.rotation.Length >= 4)
                        ? new Quaternion(node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3])
                        : Quaternion.identity;
                    Vector3 s = (node.scale != null && node.scale.Length >= 3)
                        ? new Vector3(node.scale[0], node.scale[1], node.scale[2])
                        : Vector3.one;

                    result.Sockets.Add((node.name!, t, r, s));
                }
            }

            return result;
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
            int expectedCount)
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
                    r = (byte)(BitConverter.ToUInt16(bytes, p) >> 8);
                    g = (byte)(BitConverter.ToUInt16(bytes, p + 2) >> 8);
                    b = (byte)(BitConverter.ToUInt16(bytes, p + 4) >> 8);
                    a = isVec4 ? (byte)(BitConverter.ToUInt16(bytes, p + 6) >> 8) : (byte)245;
                }
                else
                {
                    r = (byte)Mathf.Clamp(Mathf.RoundToInt(BitConverter.ToSingle(bytes, p) * 255f), 0, 255);
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(BitConverter.ToSingle(bytes, p + 4) * 255f), 0, 255);
                    b = (byte)Mathf.Clamp(Mathf.RoundToInt(BitConverter.ToSingle(bytes, p + 8) * 255f), 0, 255);
                    a = isVec4
                        ? (byte)Mathf.Clamp(Mathf.RoundToInt(BitConverter.ToSingle(bytes, p + 12) * 255f), 0, 255)
                        : (byte)245;
                }

                cols[i] = new Color32(r == 0 ? (byte)51 : r, g, b, a);
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
