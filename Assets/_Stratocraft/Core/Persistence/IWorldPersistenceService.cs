#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Stratocraft.Core.Constants;
using Stratocraft.Core.Structures;

namespace Stratocraft.Core.Persistence
{
    /// <summary>
    /// Kapi ve pencere kepengi acik/surgulu/kilitli durum bitleri (.sregion V3 StructureGraphSection).
    /// </summary>
    [Flags]
    public enum DoorShutterStateFlags : byte
    {
        None = 0,
        IsOpen = 1 << 0,
        IsBolted = 1 << 1,
        IsLocked = 1 << 2,
        IsWindowShutter = 1 << 3
    }

    /// <summary>
    /// 8-byte kompakt kapi/kepenk durum kaydi (oyuncu yapilari ve prosedurel yerlesim kapi deltalari).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
    public struct DoorShutterStateRecord
    {
        public uint NodeId;
        public DoorShutterStateFlags StateFlags;
        public byte Reserved0;
        public ushort OwnerClanOrPlayerId16;

        public readonly bool IsOpen
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (StateFlags & DoorShutterStateFlags.IsOpen) != 0;
        }

        public readonly bool IsBolted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (StateFlags & DoorShutterStateFlags.IsBolted) != 0;
        }

        public readonly bool IsLocked
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (StateFlags & DoorShutterStateFlags.IsLocked) != 0;
        }

        public DoorShutterStateRecord(
            uint nodeId,
            DoorShutterStateFlags stateFlags,
            ushort ownerClanOrPlayerId16 = 0)
        {
            NodeId = nodeId;
            StateFlags = stateFlags;
            Reserved0 = 0;
            OwnerClanOrPlayerId16 = ownerClanOrPlayerId16;
        }
    }

    /// <summary>
    /// Prosedurel yerlesim (Koy/Kasaba/Sehir/Harabe) Seed-Delta degisiklik sinifi.
    /// Dokunulmamis yerlesim chunk'lari diskte 0 byte delta tutar.
    /// </summary>
    public enum StructuralSeedDeltaKind : byte
    {
        None = 0,
        DestroyedBaselineNode = 1,
        RepairedBaselineNode = 2,
        DamagedBaselineNode = 3,
        AddedPlayerNode = 4,
        DoorShutterToggled = 5
    }

    /// <summary>
    /// 12-byte kompakt yerlesim Seed-Delta yikim/onarim kaydi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 12)]
    public struct SettlementSeedDeltaRecord
    {
        public uint BaselineNodeId;
        public StructuralSeedDeltaKind Kind;
        public byte IntegrityByte;
        public byte CureRatioByte;
        public DoorShutterStateFlags DoorStateFlags;
        public uint AddedOrReplacementNodeId;

        public SettlementSeedDeltaRecord(
            uint baselineNodeId,
            StructuralSeedDeltaKind kind,
            byte integrityByte = 255,
            byte cureRatioByte = 255,
            DoorShutterStateFlags doorStateFlags = DoorShutterStateFlags.None,
            uint addedOrReplacementNodeId = 0u)
        {
            BaselineNodeId = baselineNodeId;
            Kind = kind;
            IntegrityByte = integrityByte;
            CureRatioByte = cureRatioByte;
            DoorStateFlags = doorStateFlags;
            AddedOrReplacementNodeId = addedOrReplacementNodeId;
        }
    }

    /// <summary>
    /// Chunk ici yerlestirilebilir zanaat istasyonu, santiye yigini ve yatak (SpawnPoint) sinifi.
    /// </summary>
    public enum StructureStationKind : byte
    {
        None = 0,
        BedSpawnPoint = 1,
        Workbench = 2,
        ForgeAnvil = 3,
        BloomeryFurnace = 4,
        LimeKiln = 5,
        MortarTrough = 6,
        ChestStorage = 7,
        SiteMaterialPile = 8,
        PulleyCrane = 9,
        HearthFireplace = 10
    }

    /// <summary>
    /// Chunk ici istasyon ve yatak durum bayraklari.
    /// </summary>
    [Flags]
    public enum StructureStationFlags : byte
    {
        None = 0,
        IsActiveSpawnPoint = 1 << 0,
        IsLit = 1 << 1,
        IsClaimed = 1 << 2,
        IsIndoors = 1 << 3
    }

    /// <summary>
    /// 24-byte kompakt chunk ici yerlestirilebilir istasyon/yatak kalicilik meta kaydi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 24)]
    public struct StructureStationMetaRecord
    {
        public uint StationId;
        public StructureStationKind Kind;
        public byte MaterialOrTier;
        public StructureStationFlags Flags;
        public byte Reserved0;
        public float PosX;
        public float PosY;
        public float PosZ;
        public ushort OwnerClanOrPlayerId16;
        public ushort StoredUnits;

        public StructureStationMetaRecord(
            uint stationId,
            StructureStationKind kind,
            float posX,
            float posY,
            float posZ,
            byte materialOrTier = 1,
            StructureStationFlags flags = StructureStationFlags.None,
            ushort ownerClanOrPlayerId16 = 0,
            ushort storedUnits = 0)
        {
            StationId = stationId;
            Kind = kind;
            MaterialOrTier = materialOrTier;
            Flags = flags;
            Reserved0 = 0;
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            OwnerClanOrPlayerId16 = ownerClanOrPlayerId16;
            StoredUnits = storedUnits;
        }
    }

    /// <summary>
    /// .sregion V3 opsiyonel yapi grafi blogu (ChunkDirtyFlags.Structures = 1 &lt;&lt; 7).
    /// Oyuncu yapilari (SerializedStructuralNode24[]), MortarCureTick, kapi/kepenk durum bitleri,
    /// prosedurel yerlesim Seed-Delta yikim/onarim maskesi ve istasyon/yatak kayitlarini tasir.
    /// Dokunulmamis prosedurel yerlesim chunk'larinda IsEmpty == true olur ve diskte 0 byte kaplar.
    /// </summary>
    public sealed class StructureGraphSection
    {
        /// <summary>Guncel V3 StructureGraphSection ikili bicim surumu.</summary>
        public const byte CurrentSectionFormatVersion = 1;

        /// <summary>Erken V3/V2 eski parca migrasyon ikili bicim surumu.</summary>
        public const byte LegacyInlineSectionFormatVersion = 0;

        /// <summary>24-byte kompakt yapisal dugum kayitlari.</summary>
        public SerializedStructuralNode24[] Nodes { get; }

        /// <summary>Harc kuruma zamanlayicisi son kayit tiki (ekran-disi kuruma yakalamasi icin).</summary>
        public ulong MortarCureTick { get; set; }

        /// <summary>Kapi ve pencere kepengi acik/surgulu/kilitli durum kayitlari.</summary>
        public DoorShutterStateRecord[] DoorShutterStates { get; }

        /// <summary>Prosedurel yerlesim taban tohum ozeti (0 = bagimsiz oyuncu yapisi).</summary>
        public int SettlementBaselineSeedHash { get; set; }

        /// <summary>Prosedurel yerlesim baz dugumlerinin yikim bit maskesi (her bit = 1 baz dugum indeksi).</summary>
        public ulong[] SettlementDestructionMaskWords { get; }

        /// <summary>Prosedurel yerlesim baz dugumlerinin onarim/guclendirme bit maskesi.</summary>
        public ulong[] SettlementRepairMaskWords { get; }

        /// <summary>Prosedurel yerlesim Seed-Delta ayrintili yikim/onarim kayitlari.</summary>
        public SettlementSeedDeltaRecord[] SettlementSeedDeltas { get; }

        /// <summary>Chunk icindeki yerlestirilmis zanaat istasyonlari, santiye yiginlari ve yataklar.</summary>
        public StructureStationMetaRecord[] StationsAndBeds { get; }

        /// <summary>Bu yapi bolumu eski V2 / erken V3 kaydindan otomatik migrasyonla mi olusturuldu?</summary>
        public bool MigratedFromLegacyFormat { get; set; }

        /// <summary>Hicbir oyuncu yapisi, delta maskesi, kapi durumu veya istasyon yoksa true (diskte 0 B yer kaplar).</summary>
        public bool IsEmpty =>
            Nodes.Length == 0 &&
            DoorShutterStates.Length == 0 &&
            SettlementSeedDeltas.Length == 0 &&
            StationsAndBeds.Length == 0 &&
            !HasAnyNonZeroWord(SettlementDestructionMaskWords) &&
            !HasAnyNonZeroWord(SettlementRepairMaskWords);

        /// <summary>PristineChunkDecay muafiyeti icin aktif yapi/istasyon/delta iceriyor mu?</summary>
        public bool HasActiveStructures => !IsEmpty;

        public StructureGraphSection(
            SerializedStructuralNode24[]? nodes = null,
            ulong mortarCureTick = 0UL,
            DoorShutterStateRecord[]? doorShutterStates = null,
            int settlementBaselineSeedHash = 0,
            ulong[]? settlementDestructionMaskWords = null,
            ulong[]? settlementRepairMaskWords = null,
            SettlementSeedDeltaRecord[]? settlementSeedDeltas = null,
            StructureStationMetaRecord[]? stationsAndBeds = null,
            bool migratedFromLegacyFormat = false)
        {
            Nodes = nodes ?? Array.Empty<SerializedStructuralNode24>();
            MortarCureTick = mortarCureTick;
            DoorShutterStates = doorShutterStates ?? Array.Empty<DoorShutterStateRecord>();
            SettlementBaselineSeedHash = settlementBaselineSeedHash;
            SettlementDestructionMaskWords = settlementDestructionMaskWords ?? Array.Empty<ulong>();
            SettlementRepairMaskWords = settlementRepairMaskWords ?? Array.Empty<ulong>();
            SettlementSeedDeltas = settlementSeedDeltas ?? Array.Empty<SettlementSeedDeltaRecord>();
            StationsAndBeds = stationsAndBeds ?? Array.Empty<StructureStationMetaRecord>();
            MigratedFromLegacyFormat = migratedFromLegacyFormat;
        }

        /// <summary>
        /// StructuralNode64 dizisini 24-byte kompakt forma paketler; kapi/pencere dugumlerinin
        /// acik/kilit durumlarini da DoorShutterStates listesine istege bagli olarak yansitir.
        /// </summary>
        public static StructureGraphSection PackFromNodes(
            ReadOnlySpan<StructuralNode64> nodes,
            ulong mortarCureTick = 0UL,
            DoorShutterStateRecord[]? explicitDoorShutterStates = null,
            int settlementBaselineSeedHash = 0,
            ulong[]? settlementDestructionMaskWords = null,
            ulong[]? settlementRepairMaskWords = null,
            SettlementSeedDeltaRecord[]? settlementSeedDeltas = null,
            StructureStationMetaRecord[]? stationsAndBeds = null)
        {
            var packed = nodes.Length > 0
                ? new SerializedStructuralNode24[nodes.Length]
                : Array.Empty<SerializedStructuralNode24>();

            List<DoorShutterStateRecord>? autoDoors = null;
            for (int i = 0; i < nodes.Length; i++)
            {
                SerializedStructuralNode24.PackFrom(in nodes[i], out packed[i]);
                if (explicitDoorShutterStates == null &&
                    (nodes[i].Role == StructuralElementRole.DoorFrameOpening ||
                     nodes[i].Role == StructuralElementRole.WindowFrameOpening))
                {
                    autoDoors ??= new List<DoorShutterStateRecord>(4);
                    DoorShutterStateFlags flags = DoorShutterStateFlags.None;
                    if (nodes[i].IsDoorOrWindowOpen) flags |= DoorShutterStateFlags.IsOpen;
                    if (nodes[i].IsLocked) flags |= DoorShutterStateFlags.IsLocked;
                    if (nodes[i].Role == StructuralElementRole.WindowFrameOpening) flags |= DoorShutterStateFlags.IsWindowShutter;
                    autoDoors.Add(new DoorShutterStateRecord(
                        nodes[i].NodeId,
                        flags,
                        (ushort)System.Math.Min(nodes[i].OwnerClanOrPlayerId, (uint)ushort.MaxValue)));
                }
            }

            return new StructureGraphSection(
                nodes: packed,
                mortarCureTick: mortarCureTick,
                doorShutterStates: explicitDoorShutterStates ?? (autoDoors != null ? autoDoors.ToArray() : Array.Empty<DoorShutterStateRecord>()),
                settlementBaselineSeedHash: settlementBaselineSeedHash,
                settlementDestructionMaskWords: settlementDestructionMaskWords,
                settlementRepairMaskWords: settlementRepairMaskWords,
                settlementSeedDeltas: settlementSeedDeltas,
                stationsAndBeds: stationsAndBeds);
        }

        /// <summary>
        /// 0-GC hedef tampona 24-byte dugumleri acar; kapi/kepenk durum kayitlarini esler ve
        /// istege bagli ekran-disi harc kuruma ilerlemesini (Karar A2) uygular.
        /// </summary>
        public int UnpackNodesInto(
            Span<StructuralNode64> destination,
            float elapsedOfflineSeconds = 0f,
            bool isCreativeMode = false)
        {
            int count = System.Math.Min(Nodes.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                Nodes[i].UnpackTo(out StructuralNode64 unpacked);
                if (elapsedOfflineSeconds > 0f && unpacked.Mortar != MortarType.None && unpacked.CureRatio01 < 1.0f)
                {
                    unpacked.CureRatio01 = MortarConstants.StepCureRatio(
                        unpacked.Mortar,
                        unpacked.CureRatio01,
                        elapsedOfflineSeconds,
                        isCreativeMode);
                }

                for (int d = 0; d < DoorShutterStates.Length; d++)
                {
                    if (DoorShutterStates[d].NodeId == unpacked.NodeId)
                    {
                        unpacked.IsDoorOrWindowOpen = DoorShutterStates[d].IsOpen;
                        unpacked.IsLocked = DoorShutterStates[d].IsLocked || DoorShutterStates[d].IsBolted;
                        break;
                    }
                }

                destination[i] = unpacked;
            }
            return count;
        }

        /// <summary>Verilen prosedurel yerlesim baz dugum indeksinin yikilmis olup olmadigini O(1) doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBaselineNodeDestroyed(int baselineIndex)
            => IsMaskBitSet(SettlementDestructionMaskWords, baselineIndex);

        /// <summary>Verilen prosedurel yerlesim baz dugum indeksinin onarilmis olup olmadigini O(1) doner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBaselineNodeRepaired(int baselineIndex)
            => IsMaskBitSet(SettlementRepairMaskWords, baselineIndex);

        /// <summary>
        /// Prosedurel yerlesim baz indekslerinden kompakt 64-bit yikim/onarim maskesi olusturur.
        /// Hicbir indeks degismemisse bos (0 uzunluklu) dizi doner -> 0 B disk deltasi!
        /// </summary>
        public static ulong[] BuildBitmaskFromIndices(int maxIndexExclusive, ReadOnlySpan<int> setIndices)
        {
            if (maxIndexExclusive <= 0 || setIndices.IsEmpty)
            {
                return Array.Empty<ulong>();
            }

            int wordCount = (maxIndexExclusive + 63) >> 6;
            ulong[] words = new ulong[wordCount];
            bool anySet = false;
            for (int i = 0; i < setIndices.Length; i++)
            {
                int idx = setIndices[i];
                if ((uint)idx < (uint)maxIndexExclusive)
                {
                    words[idx >> 6] |= 1UL << (idx & 63);
                    anySet = true;
                }
            }
            return anySet ? words : Array.Empty<ulong>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMaskBitSet(ulong[] words, int bitIndex)
        {
            if (bitIndex < 0)
            {
                return false;
            }
            int wordIdx = bitIndex >> 6;
            if (wordIdx >= words.Length)
            {
                return false;
            }
            return ((words[wordIdx] >> (bitIndex & 63)) & 1UL) != 0UL;
        }

        private static bool HasAnyNonZeroWord(ulong[] words)
        {
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i] != 0UL)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Eski V2 / erken V3 kayitlarindaki ModularPlacedPieceNode semasini Stratocraft.Core bagimliligi
    /// olmadan temsil eden tasinabilir migrasyon kaydi.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct LegacyModularPieceRecord
    {
        public int InstanceId;
        public byte PieceKindRaw;
        public byte MaterialKindRaw;
        public byte ThicknessKindRaw;
        public byte CompletedCourses;
        public byte RequiredCourses;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float YawDegrees;
        public float Health;
        public float MaxHealth;
        public bool IsGrounded;
        public bool IsBroken;
        public bool IsDoorOrWindowOpen;
        public bool IsLocked;
        public uint OwnerClanOrPlayerId;
    }

    /// <summary>
    /// Eski V2 ve erken V3 kayitlarini yeni UnifiedStructureTypes (StructuralNode64 / SerializedStructuralNode24)
    /// semasina 0 cokmeyle otomatik donusturen migrasyon yardimcisi (DOK7 §4.3).
    /// </summary>
    public static class LegacyPieceToUnifiedMigrator
    {
        /// <summary>
        /// Tek bir eski ModularPlacedPieceRecord kaydini StructuralNode64 dugumune donusturur.
        /// Bilinmeyen enum degerlerinde dahi asla istisna firlatmaz; guvenli varsayilan ekol/rol atar.
        /// </summary>
        public static StructuralNode64 MigrateSinglePiece(in LegacyModularPieceRecord legacy)
        {
            byte materialId = MapLegacyMaterialKind(legacy.MaterialKindRaw);
            ResolveSchoolAndRole(legacy.PieceKindRaw, legacy.MaterialKindRaw, out ArchitecturalSchool school, out StructuralElementRole role);
            StructureThicknessProfile thickness = MapLegacyThickness(legacy.ThicknessKindRaw);

            bool isMasonry = school == ArchitecturalSchool.StoneMasonry || school == ArchitecturalSchool.MonumentalArchDome;
            AnchorType anchor = legacy.IsGrounded
                ? (role == StructuralElementRole.StonePlinthFoundation || isMasonry
                    ? AnchorType.CompactedGravelOrTrench
                    : AnchorType.FirmDrySoilOrClay)
                : AnchorType.NoneFloating;

            MortarType mortar = isMasonry && role != StructuralElementRole.TrenchRubbleFooting
                ? MortarType.Lime
                : MortarType.None;

            float safeYaw = float.IsNaN(legacy.YawDegrees) || float.IsInfinity(legacy.YawDegrees) ? 0f : legacy.YawDegrees;
            byte yawQuarter = (byte)(((int)MathF.Round(safeYaw / 90f) % 4 + 4) & 0x03);
            uint safeNodeId = legacy.InstanceId > 0 ? (uint)legacy.InstanceId : 1u;

            StructuralNode64 node = StructuralNode64.Create(
                nodeId: safeNodeId,
                posX: float.IsNaN(legacy.PosX) || float.IsInfinity(legacy.PosX) ? 0f : legacy.PosX,
                posY: float.IsNaN(legacy.PosY) || float.IsInfinity(legacy.PosY) ? 0f : legacy.PosY,
                posZ: float.IsNaN(legacy.PosZ) || float.IsInfinity(legacy.PosZ) ? 0f : legacy.PosZ,
                school: school,
                role: role,
                materialId: materialId,
                anchor: anchor,
                mortar: mortar,
                yawQuarterTurns: yawQuarter,
                thickness: thickness,
                ownerClanOrPlayerId: legacy.OwnerClanOrPlayerId,
                instantCure: true);

            float maxHp = legacy.MaxHealth > 0.001f ? legacy.MaxHealth : 100f;
            float hp = float.IsNaN(legacy.Health) || float.IsInfinity(legacy.Health) ? maxHp : legacy.Health;
            node.Integrity01 = legacy.IsBroken ? 0f : System.Math.Clamp(hp / maxHp, 0f, 1f);
            node.CollapseState = legacy.IsBroken ? StructureCollapseState.Collapsed : StructureCollapseState.Stable;
            node.GeoSupportDist = legacy.IsBroken
                ? MortarConstants.DetachedGeodesicDistance
                : (legacy.IsGrounded ? (ushort)0 : (ushort)1);
            node.CompletedCourses = (byte)System.Math.Clamp(legacy.CompletedCourses == 0 ? 1 : legacy.CompletedCourses, 1, 15);
            node.IsDoorOrWindowOpen = legacy.IsDoorOrWindowOpen;
            node.IsLocked = legacy.IsLocked;
            return node;
        }

        /// <summary>
        /// Eski V2 / erken V3 moduler parca listesini tam bir V3 StructureGraphSection bloguna cevirir.
        /// </summary>
        public static StructureGraphSection MigrateBatchToSection(
            ReadOnlySpan<LegacyModularPieceRecord> legacyPieces,
            ulong mortarCureTick = 0UL)
        {
            if (legacyPieces.IsEmpty)
            {
                return new StructureGraphSection(migratedFromLegacyFormat: true);
            }

            var packed = new SerializedStructuralNode24[legacyPieces.Length];
            List<DoorShutterStateRecord>? doorStates = null;

            for (int i = 0; i < legacyPieces.Length; i++)
            {
                StructuralNode64 migrated = MigrateSinglePiece(in legacyPieces[i]);
                SerializedStructuralNode24.PackFrom(in migrated, out packed[i]);

                if (migrated.Role == StructuralElementRole.DoorFrameOpening ||
                    migrated.Role == StructuralElementRole.WindowFrameOpening)
                {
                    doorStates ??= new List<DoorShutterStateRecord>(4);
                    DoorShutterStateFlags flags = DoorShutterStateFlags.None;
                    if (migrated.IsDoorOrWindowOpen) flags |= DoorShutterStateFlags.IsOpen;
                    if (migrated.IsLocked) flags |= DoorShutterStateFlags.IsLocked;
                    if (migrated.Role == StructuralElementRole.WindowFrameOpening) flags |= DoorShutterStateFlags.IsWindowShutter;
                    doorStates.Add(new DoorShutterStateRecord(
                        migrated.NodeId,
                        flags,
                        (ushort)System.Math.Min(migrated.OwnerClanOrPlayerId, (uint)ushort.MaxValue)));
                }
            }

            return new StructureGraphSection(
                nodes: packed,
                mortarCureTick: mortarCureTick,
                doorShutterStates: doorStates != null ? doorStates.ToArray() : Array.Empty<DoorShutterStateRecord>(),
                migratedFromLegacyFormat: true);
        }

        /// <summary>
        /// V2 veya erken V3 kaydinda StructureGraphSection bulunmayan ancak HasActiveStructures == true olan
        /// chunk'lardaki islenmis oyuncu bloklarini cokmeden otomatik olarak V3 yapi dugumlerine yukseltir.
        /// </summary>
        public static StructureGraphSection? TryMigrateLegacyVoxelSnapshot(ChunkSerializationSnapshot legacySnapshot)
        {
            if (legacySnapshot == null || !legacySnapshot.HasActiveStructures)
            {
                return null;
            }

            if (legacySnapshot.StructureGraph != null && !legacySnapshot.StructureGraph.IsEmpty)
            {
                return legacySnapshot.StructureGraph;
            }

            uint[] voxels = legacySnapshot.VoxelCells;
            if (voxels == null || voxels.Length == 0)
            {
                return new StructureGraphSection(migratedFromLegacyFormat: true);
            }

            var migratedNodes = new List<SerializedStructuralNode24>(16);
            int baseWx = legacySnapshot.ChunkX << 4;
            int baseWz = legacySnapshot.ChunkZ << 4;
            uint nextNodeId = 1u;

            for (int idx = 0; idx < voxels.Length && migratedNodes.Count < 4096; idx++)
            {
                uint raw = voxels[idx];
                if (raw == 0u)
                {
                    continue;
                }

                byte mat = (byte)(raw & 0xFFu);
                if (mat == MaterialIdCatalog.Air)
                {
                    continue;
                }

                byte density = (byte)((raw >> 8) & 0x7Fu);
                byte shape = (byte)((raw >> 15) & 0x3Fu);
                if (shape == 0 && density < StratocraftConstants.SdfIsoLevel)
                {
                    continue;
                }

                // Bit [26..27]: BlokKokenDurumu (1 = OyuncuItemBlogu)
                uint originRaw = (raw >> 26) & 0x3u;
                bool isCrafted = MaterialIdCatalog.IsCraftedItemBlockMaterial(mat);
                if (originRaw != 1u && !isCrafted)
                {
                    continue;
                }

                int wy = idx >> 8;
                int lz = (idx >> 4) & 0x0F;
                int lx = idx & 0x0F;
                byte dmg = (byte)((raw >> 28) & 0x0Fu);
                byte rot = (byte)(((raw >> 21) & 0x1Fu) & 0x03);

                ArchitecturalSchool school = ResolveSchoolFromMaterial(mat);
                StructuralElementRole role = ResolveRoleFromMaterial(mat, school);
                MortarType mortar = (school == ArchitecturalSchool.StoneMasonry || school == ArchitecturalSchool.MonumentalArchDome)
                    ? MortarType.Lime
                    : MortarType.None;

                StructuralNode64 node = StructuralNode64.Create(
                    nodeId: nextNodeId++,
                    posX: baseWx + lx + 0.5f,
                    posY: wy + 0.5f,
                    posZ: baseWz + lz + 0.5f,
                    school: school,
                    role: role,
                    materialId: mat,
                    anchor: wy <= 1 ? AnchorType.BedrockOrSanctum : AnchorType.FirmDrySoilOrClay,
                    mortar: mortar,
                    yawQuarterTurns: rot,
                    thickness: StructureThicknessProfile.Standard,
                    instantCure: true);

                node.Integrity01 = System.Math.Clamp(1.0f - (dmg / 15.0f), 0.05f, 1.0f);
                node.CompletedCourses = 1;
                migratedNodes.Add(SerializedStructuralNode24.PackFrom(in node));
            }

            return new StructureGraphSection(
                nodes: migratedNodes.Count > 0 ? migratedNodes.ToArray() : Array.Empty<SerializedStructuralNode24>(),
                mortarCureTick: 0UL,
                migratedFromLegacyFormat: true);
        }

        private static byte MapLegacyMaterialKind(byte materialKindRaw) => materialKindRaw switch
        {
            0 => MaterialIdCatalog.OakWood,
            1 => MaterialIdCatalog.Ironwood,
            2 => MaterialIdCatalog.Cobblestone,
            3 => MaterialIdCatalog.Basalt,
            4 => MaterialIdCatalog.DressedMarbleBlock,
            5 => MaterialIdCatalog.ReinforcedSteelBlock,
            _ => MaterialIdCatalog.OakWood
        };

        private static StructureThicknessProfile MapLegacyThickness(byte thicknessKindRaw) => thicknessKindRaw switch
        {
            1 => StructureThicknessProfile.Slender,
            2 => StructureThicknessProfile.HeavyBearing,
            _ => StructureThicknessProfile.Standard
        };

        private static void ResolveSchoolAndRole(
            byte pieceKindRaw,
            byte materialKindRaw,
            out ArchitecturalSchool school,
            out StructuralElementRole role)
        {
            bool isHeavyStoneOrMarble = materialKindRaw >= 2;
            bool isMonumentalMat = materialKindRaw >= 4;

            switch (pieceKindRaw)
            {
                case 0: // LogBeam2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.LogCabin;
                    role = isHeavyStoneOrMarble ? StructuralElementRole.StoneOrWoodLintel : StructuralElementRole.TimberMainGirder;
                    break;
                case 1: // LogPillar3m
                    school = isMonumentalMat ? ArchitecturalSchool.MonumentalArchDome : ArchitecturalSchool.TimberFrame;
                    role = isMonumentalMat ? StructuralElementRole.MonolithicColumnShaft : StructuralElementRole.TimberPost;
                    break;
                case 2: // PlankWall2x2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.LogCabin;
                    role = isHeavyStoneOrMarble
                        ? (materialKindRaw >= 3 ? StructuralElementRole.AshlarMasonryWall : StructuralElementRole.RubbleMasonryCourse)
                        : StructuralElementRole.NotchedWallLog;
                    break;
                case 3: // PlankDoorway2x2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame;
                    role = StructuralElementRole.DoorFrameOpening;
                    break;
                case 4: // PlankWindow2x2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame;
                    role = StructuralElementRole.WindowFrameOpening;
                    break;
                case 5: // PlankFloor2x2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame;
                    role = StructuralElementRole.FloorJoistDeck;
                    break;
                case 6: // StoneFoundation2x2m
                    school = ArchitecturalSchool.StoneMasonry;
                    role = StructuralElementRole.StonePlinthFoundation;
                    break;
                case 7: // StoneArchPillar3m
                    school = ArchitecturalSchool.MonumentalArchDome;
                    role = StructuralElementRole.MonolithicColumnShaft;
                    break;
                case 8: // RoofRafter26Deg2x2m
                case 9: // RoofRafter45Deg2x2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame;
                    role = StructuralElementRole.InclinedRoofRafter;
                    break;
                case 10: // RoofRidge2m
                    school = ArchitecturalSchool.TimberFrame;
                    role = StructuralElementRole.RoofRidgeBeam;
                    break;
                case 11: // DiagonalBrace2m
                    school = ArchitecturalSchool.TimberFrame;
                    role = StructuralElementRole.DiagonalWindBrace;
                    break;
                case 12: // GableWall45Deg2m
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.TimberFrame;
                    role = isHeavyStoneOrMarble ? StructuralElementRole.AshlarMasonryWall : StructuralElementRole.InfillPanelWattleAdobe;
                    break;
                default:
                    school = isHeavyStoneOrMarble ? ArchitecturalSchool.StoneMasonry : ArchitecturalSchool.LogCabin;
                    role = isHeavyStoneOrMarble ? StructuralElementRole.RubbleMasonryCourse : StructuralElementRole.NotchedWallLog;
                    break;
            }
        }

        private static ArchitecturalSchool ResolveSchoolFromMaterial(byte matId)
        {
            if (matId == MaterialIdCatalog.DressedMarbleBlock || matId == MaterialIdCatalog.Marble || matId == MaterialIdCatalog.IronTieBeamBlock)
            {
                return ArchitecturalSchool.MonumentalArchDome;
            }
            if (matId == MaterialIdCatalog.CutAshlarStone ||
                matId == MaterialIdCatalog.FiredClayBrick ||
                matId == MaterialIdCatalog.LimeMortarStoneWall ||
                matId == MaterialIdCatalog.StoneBrick ||
                matId == MaterialIdCatalog.Cobblestone ||
                matId == MaterialIdCatalog.Basalt ||
                matId == MaterialIdCatalog.Stone)
            {
                return ArchitecturalSchool.StoneMasonry;
            }
            if (matId == MaterialIdCatalog.WattleDaubPanelBlock ||
                matId == MaterialIdCatalog.AdobeBrickBlock ||
                matId == MaterialIdCatalog.WoodPlank ||
                matId == MaterialIdCatalog.TimberShingleRoof ||
                matId == MaterialIdCatalog.LeadedGlassPane)
            {
                return ArchitecturalSchool.TimberFrame;
            }
            if (matId == MaterialIdCatalog.ChinkedLogBlock ||
                matId == MaterialIdCatalog.OakWood ||
                matId == MaterialIdCatalog.Ironwood ||
                matId == MaterialIdCatalog.SpiritWood)
            {
                return ArchitecturalSchool.LogCabin;
            }
            return ArchitecturalSchool.RawNaturalCrystal;
        }

        private static StructuralElementRole ResolveRoleFromMaterial(byte matId, ArchitecturalSchool school)
        {
            if (matId == MaterialIdCatalog.IronTieBeamBlock) return StructuralElementRole.IronTieRod;
            if (matId == MaterialIdCatalog.LeadedGlassPane) return StructuralElementRole.WindowFrameOpening;
            if (matId == MaterialIdCatalog.RoofClayTile || matId == MaterialIdCatalog.TimberShingleRoof || matId == MaterialIdCatalog.ThatchTile)
                return StructuralElementRole.RoofCoveringCourse;
            if (matId == MaterialIdCatalog.WattleDaubPanelBlock || matId == MaterialIdCatalog.AdobeBrickBlock)
                return StructuralElementRole.InfillPanelWattleAdobe;

            return school switch
            {
                ArchitecturalSchool.MonumentalArchDome => StructuralElementRole.AshlarMasonryWall,
                ArchitecturalSchool.StoneMasonry => StructuralElementRole.RubbleMasonryCourse,
                ArchitecturalSchool.TimberFrame => StructuralElementRole.TimberPost,
                ArchitecturalSchool.LogCabin => StructuralElementRole.NotchedWallLog,
                _ => StructuralElementRole.RawCrystalMassSegment
            };
        }
    }

    /// <summary>
    /// Tek bir 16x256x16 chunk sutununun serilestirme, MicroVoxelPool4096, V3 StructureGraphSection ve Pristine Chunk Decay ust-verisi.
    /// </summary>
    public sealed class ChunkSerializationSnapshot
    {
        /// <summary>Chunk X koordinati.</summary>
        public int ChunkX { get; }

        /// <summary>Chunk Z koordinati.</summary>
        public int ChunkZ { get; }

        /// <summary>Son ziyaret edilen oyun gunu.</summary>
        public uint LastVisitedGameDay { get; set; }

        /// <summary>Son ziyaret edilen gercek UTC dakikası (Anayasa Bolum 2.6.B: 14 Gercek Gun = 20.160 UTC dakika).</summary>
        public uint LastVisitedUtcMinutes { get; set; }

        /// <summary>Chunk klan siniri (TerritoryBitmask) icinde mi?</summary>
        public bool IsInsideClan { get; set; }

        /// <summary>Chunk icinde aktif oyuncu yapisi (IS_ITEM_BLOCK == 1), sandik, yatak veya degistirilmis yerlesim yapisi var mi?</summary>
        public bool HasActiveStructures { get; set; }

        /// <summary>65.536 adet 32-bit VoxelCell dizisi.</summary>
        public uint[] VoxelCells { get; }

        /// <summary>65.536 adet 16-bit FluidCell16 dizisi.</summary>
        public ushort[] FluidCells { get; }

        /// <summary>Seyrek 4x4x4 Mikro-Voxel yontma kayitlari (SubChunkY, SlotIndex12, Mask64).</summary>
        public (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[] MicroPoolEntries { get; }

        /// <summary>Paket 4C-2: Chunk'in son simule edildigi oyun-ici saniye (ekran-disi yakalama/catch-up icin).</summary>
        public double LastSimulatedGameSeconds { get; set; }

        /// <summary>Paket 4C-2: Bu chunk'in hangi alt-sistemlerde degisiklik tasidigi.</summary>
        public ChunkDirtyFlags DirtyFlags { get; set; }

        /// <summary>Paket 4C-2: Seyrek toprak nemi kayitlari (FlatIndex, Moisture[1..255]); kuru chunk'larda 0 uzunluk.</summary>
        public (ushort FlatIndex, byte Moisture)[] SparseSoilMoisture { get; }

        /// <summary>Paket 4C-2: 16x16 kar SWE birimleri (256 B); karsiz chunk'larda null.</summary>
        public byte[]? SnowSwe256 { get; }

        /// <summary>Paket 4C-2: Ozel tuzluluk bitset'i (ulong[1024]); varsayilan tuzlulukta null.</summary>
        public ulong[]? SalinityBits1024 { get; }

        /// <summary>
        /// .sregion V3 opsiyonel yapi grafi blogu (ChunkDirtyFlags.Structures = 1 &lt;&lt; 7).
        /// Dokunulmamis prosedurel yerlesim chunk'larinda null veya IsEmpty == true olur (diskte 0 B ek yer).
        /// </summary>
        public StructureGraphSection? StructureGraph { get; set; }

        /// <summary>Bu chunk diskte kalici yapi grafi veya yerlesim deltasi tasiyor mu?</summary>
        public bool HasPersistentStructureGraph => StructureGraph != null && !StructureGraph.IsEmpty;

        /// <summary>Yeni bir chunk serilestirme goruntusu olusturur.</summary>
        public ChunkSerializationSnapshot(
            int chunkX,
            int chunkZ,
            uint[] voxelCells,
            ushort[] fluidCells,
            uint lastVisitedGameDay = 0,
            bool isInsideClan = false,
            bool hasActiveStructures = false,
            uint lastVisitedUtcMinutes = 0,
            (byte SubChunkY, ushort SlotIndex12, ulong Mask64)[]? microPoolEntries = null,
            double lastSimulatedGameSeconds = 0.0,
            ChunkDirtyFlags dirtyFlags = ChunkDirtyFlags.Terrain | ChunkDirtyFlags.Fluid,
            (ushort FlatIndex, byte Moisture)[]? sparseSoilMoisture = null,
            byte[]? snowSwe256 = null,
            ulong[]? salinityBits1024 = null,
            StructureGraphSection? structureGraph = null)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            VoxelCells = voxelCells ?? throw new ArgumentNullException(nameof(voxelCells));
            FluidCells = fluidCells ?? throw new ArgumentNullException(nameof(fluidCells));
            LastVisitedGameDay = lastVisitedGameDay;
            IsInsideClan = isInsideClan;
            bool hasStructGraph = structureGraph != null && !structureGraph.IsEmpty;
            HasActiveStructures = hasActiveStructures || hasStructGraph;
            LastVisitedUtcMinutes = lastVisitedUtcMinutes;
            MicroPoolEntries = microPoolEntries ?? Array.Empty<(byte, ushort, ulong)>();
            LastSimulatedGameSeconds = lastSimulatedGameSeconds;
            DirtyFlags = hasStructGraph ? (dirtyFlags | ChunkDirtyFlags.Structures) : dirtyFlags;
            SparseSoilMoisture = sparseSoilMoisture ?? Array.Empty<(ushort, byte)>();
            SnowSwe256 = snowSwe256;
            SalinityBits1024 = salinityBits1024;
            StructureGraph = structureGraph;
        }
    }

    /// <summary>
    /// Bolge (.sregion) dosyalari ve dunya kalicilik islemleri icin sozlesme.
    /// </summary>
    public interface IWorldPersistenceService
    {
        /// <summary>Kirli chunk listesini 32x32 bolge (.sregion) dosyalarina Seed-Delta XOR + RLE + Zstd + XXH64 ile atomik kaydeder.</summary>
        ValueTask SaveDirtyChunksBatchAsync(ReadOnlyMemory<ChunkSerializationSnapshot> chunks, CancellationToken ct = default);

        /// <summary>Verilen (chunkX, chunkZ) koordinatindaki chunk'i diskten veya bellekten yukler.</summary>
        ValueTask<ChunkSerializationSnapshot?> TryLoadChunkAsync(int chunkX, int chunkZ, CancellationToken ct = default);

        /// <summary>Verilen bolge dosyasinin XXH64 saglama toplamini dogrular.</summary>
        bool VerifyRegionChecksum(int regionX, int regionZ);

        /// <summary>
        /// Klan siniri disinda kalan (IsInsideClan == false), icinde aktif oyuncu yapisi olmayan ve
        /// 14 gun boyunca ziyaret edilmeyen sahipsiz kazilmis chunk'lari silerek orijinal Seed'e dondurur.
        /// </summary>
        int PruneAbandonedWildernessChunks(uint currentGameDay, uint decayThresholdDays = StratocraftConstants.PristineChunkDecayGameDays);

        /// <summary>
        /// Anayasa Bolum 2.6.B: Gercek UTC zamanina gore 14 gercek gun (20.160 dakika) ziyaret edilmeyen
        /// sahipsiz vahsi doga chunk'larini orijinal Seed'e dondurur.
        /// </summary>
        int PruneAbandonedWildernessChunksByUtcMinutes(
            uint currentUtcMinutes,
            uint decayThresholdRealMinutes = StratocraftConstants.PristineChunkDecayRealMinutes);
    }
}
