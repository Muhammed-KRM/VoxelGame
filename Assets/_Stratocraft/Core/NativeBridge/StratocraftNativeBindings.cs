using System;
using System.Runtime.InteropServices;

namespace Stratocraft.Core.NativeBridge
{
    /// <summary>
    /// C++20 StratocraftNative.dll/.so SIMD, LZ4/Zstd/XXH64, Ed25519 ve SQLite WAL P/Invoke koprusu.
    /// Unsafe sifir-kopya isaretcilerle (pointer) hem Unity 6 Burst/IL2CPP hem de .NET 8 ile uyumludur.
    /// </summary>
    public static unsafe class StratocraftNativeBindings
    {
        /// <summary>Yerel kutuphane adi (uzantisiz; Windows'ta .dll, Linux'ta .so olarak cozulur).</summary>
        public const string NativeLibraryName = "StratocraftNative";

        /// <summary>StratocraftNative ABI surum numarasi (Faz 0 = 100).</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_GetAbiVersion")]
        public static extern int GetAbiVersion();

        /// <summary>CPU SIMD yetenek seviyesi (0=Scalar, 1=SSE4.2, 2=AVX2, 3=AVX-512).</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_GetSimdCapabilities")]
        public static extern int GetSimdCapabilities();

        /// <summary>SIMD yetenek seviyesini okunabilir ad olarak doner.</summary>
        public static string GetSimdCapabilityLabel()
        {
            int level = GetSimdCapabilities();
            switch (level)
            {
                case 3: return "AVX-512";
                case 2: return "AVX2 (Active)";
                case 1: return "SSE4.2";
                default: return "Scalar";
            }
        }

        /// <summary>
        /// 16x256x16 (65.536) chunk sutununu 6 zorluk halkasi, magara ve 14 cevher kurallarina gore doldurur.
        /// </summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Noise_GenerateChunkSIMD")]
        public static extern void GenerateChunkSimd(
            int worldSeed,
            int chunkX,
            int chunkZ,
            uint* outVoxelCells65536,
            ushort* outFluidCells65536);

        /// <summary>Belirtilen bellek tamponunun 64-bit XXH64 saglama toplamini hesaplar.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Codec_ComputeXXH64")]
        public static extern ulong ComputeXxh64(void* buffer, int size, ulong seed);

        /// <summary>Tamponu LZ4 ile sikistirir ve XXH64 saglama toplamini yazar.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Codec_CompressChunkLZ4")]
        public static extern int CompressChunkLz4(
            void* srcBuffer,
            int srcSize,
            void* dstBuffer,
            int dstCapacity,
            ulong* outXxh64);

        /// <summary>LZ4 verisini acar ve XXH64 saglama toplamini dogrular (bozuksa -2 doner).</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Codec_DecompressChunkLZ4")]
        public static extern int DecompressChunkLz4(
            void* srcBuffer,
            int srcSize,
            void* dstBuffer,
            int dstCapacity,
            ulong expectedXxh64);

        /// <summary>.sregion bolge verisini Zstandard (Zstd) ile sikistirir.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Codec_CompressRegionZstd")]
        public static extern int CompressRegionZstd(
            void* srcBuffer,
            int srcSize,
            void* dstBuffer,
            int dstCapacity,
            int compressionLevel);

        /// <summary>Zstandard ile sikistirilmis .sregion verisini acar.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Codec_DecompressRegionZstd")]
        public static extern int DecompressRegionZstd(
            void* srcBuffer,
            int srcSize,
            void* dstBuffer,
            int dstCapacity);

        /// <summary>32 bayt tohumdan Ed25519 acik (32B) ve gizli (64B) anahtar cifti turetir.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Crypto_DeriveKeyPairEd25519")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool DeriveKeyPairEd25519(
            byte* seed32,
            byte* outPublicKey32,
            byte* outSecretKey64);

        /// <summary>Kan Imzali Kontrat verisini Ed25519 ile imzalar.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Crypto_SignContractEd25519")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SignContractEd25519(
            byte* payload,
            int payloadLen,
            byte* secretKey64,
            byte* outSignature64);

        /// <summary>Kan Imzali Kontrat Ed25519 imzasini sabit zamanli dogrular.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Crypto_VerifyContractEd25519")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool VerifyContractEd25519(
            byte* payload,
            int payloadLen,
            byte* signature64,
            byte* publicKey32);

        /// <summary>SQLite WAL telemetri veritabanini acar ve 3 katmanli tablo semasini baslatir.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Telemetry_OpenWalDb")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool TelemetryOpenWalDb(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8DbPath,
            out IntPtr outDbHandle);

        /// <summary>SQLite WAL veritabanina UTF-8 kodlu toplu SQL islemi yazar.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Telemetry_ExecuteSql")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool TelemetryExecuteSql(
            IntPtr dbHandle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8SqlBatch);

        /// <summary>SQLite veritabanindan tekil 64-bit tamsayi sorgu sonucu doner.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Telemetry_QueryScalarInt64")]
        public static extern long TelemetryQueryScalarInt64(
            IntPtr dbHandle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8QuerySql);

        /// <summary>SQLite WAL veritabanini checkpoint alarak guvenli kapatir.</summary>
        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "StratocraftNative_Telemetry_CloseDb")]
        public static extern void TelemetryCloseDb(IntPtr dbHandle);
    }
}
