#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Character.Data;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;
using Stratocraft.WorldGen.Generators;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 7 (SRP): Canli Yaratik Yapay Zekasi (<see cref="Creature21BehaviorSystem"/>), Devriye/Aggro/Takip/Saldiri
    /// durum makinesi, HP/Hasar, Uzuv Kopmasi (<c>ApplyLimbDismembermentPenalty</c> + <c>Socket_GoreCap_*</c>) ve
    /// <c>TARGET_SUPPORT_PILLARS</c> (Anti-Pillar-Cheese Domino tetikleme) Koprusu.
    /// </summary>
    public sealed class Stage1CreatureSimulationBridge
    {
        /// <summary>Canli yapay zeka durumlari.</summary>
        public enum CreatureAiState : byte { Patrol = 0, AggroChase = 1, MeleeAttack = 2, SupportPillarSiege = 3, Dead = 4 }

        private sealed class LiveCreatureAgent
        {
            public Transform Root = null!;
            public Vector3 AnchorPos;
            public float BaseYaw, PhaseOffset, MaxHp, CurrentHp, BaseSpeed, SpeedMultiplier = 1.0f, AttackCooldown;
            public bool IsFlyingBoss, LegSevered, ArmSevered, WingSevered;
            public string DisplayName = string.Empty;
            public AnatomyCapabilityFlags Capabilities;
            public CreatureAiState State = CreatureAiState.Patrol;
        }

        private readonly List<LiveCreatureAgent> _agents = new(8);
        private readonly List<FacetedVoxelTriangle> _limbMeshTris = new(64);

        /// <summary>Kayitli canli ajan sayisi.</summary>
        public int AgentCount => _agents.Count;

        /// <summary>Son yaratik etkilesim veya savas olayi bildirimi.</summary>
        public string LastCreatureEventText { get; private set; } =
            "Canlilar: Devriyede (Yaklasinca Aggro/Takip | Kilicla Uzuv Kesme | Yuksekteyken Sutun Yikma)";

        /// <summary>En yakin canlinin HUD ozet bilgisi.</summary>
        public string NearestCreatureHudSummary { get; private set; } = "Yakin Canli: Yok";

        /// <summary>Sahnedeki bir yaratigi canli AI, HP ve Anatomik Yetenek Matrisi ile kaydeder.</summary>
        public void RegisterCreature(Transform creatureRoot, Vector3 anchorPos, float phaseOffset, bool isFlyingBoss = false)
        {
            if (creatureRoot == null) return;

            CreatureArchetypeDefinition def = isFlyingBoss
                ? Creature21BehaviorSystem.GetById(16)
                : Creature21BehaviorSystem.GetById(4);

            _agents.Add(new LiveCreatureAgent
            {
                Root = creatureRoot,
                AnchorPos = anchorPos,
                BaseYaw = creatureRoot.eulerAngles.y,
                PhaseOffset = phaseOffset,
                IsFlyingBoss = isFlyingBoss,
                DisplayName = isFlyingBoss ? "Anka Kor Avatar (Boss)" : $"Ash Stalker #{_agents.Count + 1}",
                MaxHp = isFlyingBoss ? 680f : 240f,
                CurrentHp = isFlyingBoss ? 680f : 240f,
                BaseSpeed = isFlyingBoss ? 5.2f : 3.8f,
                SpeedMultiplier = 1.0f,
                Capabilities = def.InitialCapabilities | AnatomyCapabilityFlags.VoxelSiegeBreaker
            });
        }

        /// <summary>Tum canlilarin devriye, takip, saldiri ve sutun yikma (TARGET_SUPPORT_PILLARS) davranisini ilerletir.</summary>
        public void StepCreaturesAi(
            Vector3 playerPos, bool isPlayerParrying, ref CharacterVitalsComponent playerVitals,
            Dictionary<(int cx, int cz), uint[]> worldChunks, HashSet<(int cx, int cz)> dirtyChunks,
            Stage1StructuralCollapseBridge collapseBridge, float time, float deltaTime)
        {
            float nearestDist = float.MaxValue;
            LiveCreatureAgent? nearest = null;
            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead) continue;

                a.AttackCooldown = Mathf.Max(0f, a.AttackCooldown - deltaTime);
                Vector3 toPlayer = playerPos - a.Root.position;
                float horizDist = new Vector2(toPlayer.x, toPlayer.z).magnitude;
                if (horizDist < nearestDist) { nearestDist = horizDist; nearest = a; }

                float aggroRange = a.IsFlyingBoss ? 15.0f : (a.CurrentHp < a.MaxHp ? 14.0f : 6.5f);
                if (horizDist <= aggroRange)
                {
                    StepAggroAgent(a, playerPos, horizDist, toPlayer.y, isPlayerParrying, ref playerVitals, worldChunks, dirtyChunks, collapseBridge, time, deltaTime);
                }
                else
                {
                    StepPatrolAgent(a, time, worldChunks);
                }
            }
            if (nearest != null)
            {
                NearestCreatureHudSummary =
                    $"{nearest.DisplayName} [{nearest.State}] HP:{Mathf.CeilToInt(nearest.CurrentHp)}/{Mathf.CeilToInt(nearest.MaxHp)} " +
                    $"Hiz:%{Mathf.RoundToInt(nearest.SpeedMultiplier * 100f)} Mesafe:{nearestDist:F1}m";
            }
        }

        private void StepPatrolAgent(LiveCreatureAgent a, float time, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            a.State = CreatureAiState.Patrol;
            float t = time * 1.15f + a.PhaseOffset;
            if (a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0)
            {
                Vector3 offset = new Vector3(Mathf.Sin(t * 0.7f) * 2.8f, Mathf.Sin(t * 1.9f) * 0.65f, Mathf.Cos(t * 0.7f) * 2.4f);
                a.Root.position = a.AnchorPos + offset;
                a.Root.rotation = Quaternion.Euler(Mathf.Sin(t * 1.9f) * 8f, a.BaseYaw + Mathf.Sin(t * 0.7f) * 35f, Mathf.Cos(t * 1.9f) * 8f);
                return;
            }

            float radiusX = 2.4f * a.SpeedMultiplier;
            float radiusZ = 2.0f * a.SpeedMultiplier;
            Vector3 patrolOffset = new Vector3(Mathf.Sin(t) * radiusX, 0f, Mathf.Cos(t) * radiusZ);
            Vector3 nextPos = a.AnchorPos + patrolOffset;
            nextPos.y = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, nextPos.y, worldChunks);
            a.Root.position = nextPos;

            Vector3 moveDir = new Vector3(Mathf.Cos(t) * radiusX, 0f, -Mathf.Sin(t) * radiusZ);
            float moveYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float pitch = a.LegSevered ? 58f : Mathf.Abs(Mathf.Sin(t * 3.5f)) * 7f;
            float roll = a.LegSevered ? 12f : Mathf.Sin(t * 3.5f) * 4f;
            a.Root.rotation = Quaternion.Euler(pitch, moveYaw, roll);
        }

        private void StepAggroAgent(
            LiveCreatureAgent a,
            Vector3 playerPos,
            float horizDist,
            float vertDelta,
            bool isPlayerParrying,
            ref CharacterVitalsComponent playerVitals,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            Stage1StructuralCollapseBridge collapseBridge,
            float time,
            float deltaTime)
        {
            Vector3 pos = a.Root.position;
            Vector3 flatDir = new Vector3(playerPos.x - pos.x, 0f, playerPos.z - pos.z);
            if (flatDir.sqrMagnitude > 0.001f) flatDir.Normalize();

            if (horizDist > 2.05f)
            {
                a.State = CreatureAiState.AggroChase;
                bool flying = a.IsFlyingBoss && (a.Capabilities & AnatomyCapabilityFlags.WingedFlight) != 0;
                Vector3 nextPos = pos + flatDir * (a.BaseSpeed * a.SpeedMultiplier * deltaTime);

                if (flying)
                {
                    nextPos.y = Mathf.Lerp(pos.y, playerPos.y + 1.25f, deltaTime * 3.0f);
                    pos = nextPos;
                }
                else
                {
                    float targetY = SampleVoxelOrTerrainHeight(nextPos.x, nextPos.z, pos.y, worldChunks);
                    float stepDelta = targetY - pos.y;

                    if (stepDelta <= 1.25f)
                    {
                        nextPos.y = Mathf.MoveTowards(pos.y, targetY, deltaTime * 6.5f);
                        pos = nextPos;
                    }
                    else
                    {
                        Vector3 tangent = new Vector3(-flatDir.z, 0f, flatDir.x);
                        Vector3 detourPos = pos + tangent * (a.BaseSpeed * a.SpeedMultiplier * deltaTime * 0.75f);
                        detourPos.y = SampleVoxelOrTerrainHeight(detourPos.x, detourPos.z, pos.y, worldChunks);
                        pos = detourPos;
                    }
                }
                a.Root.position = pos;
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(a.LegSevered ? 64f : Mathf.Sin(time * 8f) * 6f, targetYaw, 0f);
            }
            else if (vertDelta >= 2.2f && (a.Capabilities & AnatomyCapabilityFlags.VoxelSiegeBreaker) != 0)
            {
                a.State = CreatureAiState.SupportPillarSiege;
                if (a.AttackCooldown <= 0f)
                {
                    a.AttackCooldown = 1.15f;
                    ExecuteSupportPillarSiegeStrike(a, playerPos, worldChunks, dirtyChunks, collapseBridge);
                }
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                a.Root.rotation = Quaternion.Euler(15f, targetYaw, 0f);
            }
            else
            {
                a.State = CreatureAiState.MeleeAttack;
                Vector3 tangent = new Vector3(-flatDir.z, 0f, flatDir.x);
                float strafe = Mathf.Sin(time * 2.8f + a.PhaseOffset) * 0.35f * deltaTime;
                Vector3 meleePos = pos + tangent * strafe;
                if (a.AttackCooldown > 0.85f)
                {
                    meleePos += flatDir * (1.2f * deltaTime);
                }
                meleePos.y = a.IsFlyingBoss
                    ? Mathf.Lerp(pos.y, playerPos.y + 0.9f, deltaTime * 4f)
                    : SampleVoxelOrTerrainHeight(meleePos.x, meleePos.z, pos.y, worldChunks);
                a.Root.position = meleePos;

                if (a.AttackCooldown <= 0f)
                {
                    a.AttackCooldown = 1.25f;
                    float rawDmg = (a.IsFlyingBoss ? 18f : 11f) * (a.ArmSevered ? 0.45f : 1.0f);
                    float finalDmg = isPlayerParrying ? rawDmg * 0.15f : rawDmg;
                    playerVitals.CurrentHp = Mathf.Max(1f, playerVitals.CurrentHp - finalDmg);
                    LastCreatureEventText = isPlayerParrying
                        ? $"PARRY BASARILI! {a.DisplayName} darbesi Forte ile saptirildi (-{finalDmg:F1} HP)."
                        : $"{a.DisplayName} saldirdi! (-{finalDmg:F1} HP | Kalan HP: {playerVitals.CurrentHp:F0})";
                }

                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                float combatPitch = a.AttackCooldown > 0.85f ? -14f : (a.LegSevered ? 55f : Mathf.Sin(time * 6f) * 8f);
                a.Root.rotation = Quaternion.Euler(combatPitch, targetYaw, 0f);
            }
        }

        private void ExecuteSupportPillarSiegeStrike(
            LiveCreatureAgent a,
            Vector3 playerPos,
            Dictionary<(int cx, int cz), uint[]> worldChunks,
            HashSet<(int cx, int cz)> dirtyChunks,
            Stage1StructuralCollapseBridge collapseBridge)
        {
            int wx = Mathf.FloorToInt(playerPos.x);
            int wz = Mathf.FloorToInt(playerPos.z);
            int baseY = Mathf.Clamp(Mathf.FloorToInt(a.Root.position.y) + 1, 2, Stage1InteractivePlayController.MaxEditableVoxelY - 1);

            for (int dy = 0; dy <= 2; dy++)
            {
                int vy = baseY + dy;
                uint raw = Stage1PlayerMovementBridge.ReadWorldVoxelRaw(worldChunks, wx, vy, wz);
                byte mat = VoxelBitUtility.GetMaterialId(raw);
                if (mat == MaterialIdCatalog.Air ||
                    mat == MaterialIdCatalog.Bedrock ||
                    VoxelBitUtility.GetOriginState(raw) != BlokKokenDurumu.OyuncuItemBlogu) continue;

                Stage1PlayerMovementBridge.WriteWorldVoxelRaw(worldChunks, dirtyChunks, wx, vy, wz, 0u);
                int fallen = collapseBridge.EvaluateCollapseAroundSeed(worldChunks, dirtyChunks, new Vector3Int(wx, vy, wz), 0f, out int rubbled);
                LastCreatureEventText = $"TARGET_SUPPORT_PILLARS! {a.DisplayName} oyuncu sutununu kirdi (Domino={fallen}, Moloz={rubbled})!";
                return;
            }
        }

        /// <summary>Oyuncunun serbest acili kilic darbesini menzildeki yaratiklara uygular ve uzuv kopmasini hesaplar.</summary>
        public bool TryApplyPlayerWeaponSlash(UnityEngine.Camera? cam, float slashAngleRad, float slashPower01, Material? sharedMat)
        {
            if (cam == null) return false;
            Vector3 origin = cam.transform.position;
            Vector3 forward = cam.transform.forward;

            for (int i = 0; i < _agents.Count; i++)
            {
                LiveCreatureAgent a = _agents[i];
                if (a.Root == null || a.State == CreatureAiState.Dead) continue;

                Vector3 toTarget = (a.Root.position + Vector3.up * 0.95f) - origin;
                if (toTarget.magnitude > 3.85f || Vector3.Dot(forward, toTarget.normalized) < 0.45f) continue;

                float damage = Mathf.Lerp(34f, 96f, slashPower01);
                a.CurrentHp = Mathf.Max(0f, a.CurrentHp - damage);
                string severNote = TrySeverLimbByAngle(a, slashAngleRad, slashPower01, sharedMat);

                if (a.CurrentHp <= 0f)
                {
                    a.State = CreatureAiState.Dead;
                    a.Root.localRotation = Quaternion.Euler(88f, a.Root.eulerAngles.y, 15f);
                    LastCreatureEventText = $"{a.DisplayName} ETKISIZ HALE GETIRILDI! ({damage:F0} Hasar){severNote}";
                }
                else
                {
                    LastCreatureEventText = $"{a.DisplayName} VURULDU (-{damage:F0} HP | Kalan: {a.CurrentHp:F0}/{a.MaxHp:F0}){severNote}";
                }
                return true;
            }
            return false;
        }

        private string TrySeverLimbByAngle(LiveCreatureAgent a, float slashAngleRad, float slashPower01, Material? sharedMat)
        {
            if (slashPower01 < 0.35f) return string.Empty;

            CreatureLimbSlot slot = a.IsFlyingBoss && !a.WingSevered
                ? CreatureLimbSlot.WingLeft
                : (Mathf.Sin(slashAngleRad) < -0.25f && !a.LegSevered ? CreatureLimbSlot.ThighLeft : CreatureLimbSlot.UpperArmRight);

            var (nextCaps, speedMult) = Creature21BehaviorSystem.ApplyLimbDismembermentPenalty(a.Capabilities, slot);
            a.Capabilities = nextCaps;
            if (speedMult < a.SpeedMultiplier) a.SpeedMultiplier = speedMult;

            if (slot == CreatureLimbSlot.ThighLeft && !a.LegSevered)
            {
                a.LegSevered = true;
                SpawnSeveredLimbVisual(a.Root.position, sharedMat, "Severed_CreatureLeg_Prop");
                return " [UZUV KOPTU: Bacak -> Surunme %35 Hiz!]";
            }
            if (slot == CreatureLimbSlot.WingLeft && !a.WingSevered)
            {
                a.WingSevered = true;
                SpawnSeveredLimbVisual(a.Root.position, sharedMat, "Severed_BossWing_Prop");
                return " [KANAT KOPTU: Yere Cakildi!]";
            }
            if (!a.ArmSevered)
            {
                a.ArmSevered = true;
                SpawnSeveredLimbVisual(a.Root.position, sharedMat, "Severed_CreatureArm_Prop");
                return " [UZUV KOPTU: Pence Kolu!]";
            }
            return string.Empty;
        }

        private void SpawnSeveredLimbVisual(Vector3 worldPos, Material? sharedMat, string name)
        {
            _limbMeshTris.Clear();
            HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f), MaterialIdCatalog.CreatureFurAsh, 2, 0, 8, _limbMeshTris);
            Mesh limbMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(_limbMeshTris, "Mesh_" + name);
            GameObject go = new GameObject(name);
            go.transform.position = worldPos + new Vector3(0.35f, 0.14f, -0.25f);
            go.transform.rotation = Quaternion.Euler(78f, 35f, 18f);
            go.transform.localScale = new Vector3(0.24f, 0.52f, 0.24f);
            go.AddComponent<MeshFilter>().sharedMesh = limbMesh;
            if (sharedMat != null) go.AddComponent<MeshRenderer>().sharedMaterial = sharedMat;
        }

        private static float SampleVoxelOrTerrainHeight(
            float worldX, float worldZ, float currentY, Dictionary<(int cx, int cz), uint[]> worldChunks)
        {
            int wx = Mathf.FloorToInt(worldX);
            int wz = Mathf.FloorToInt(worldZ);
            int cx = wx >= 0 ? (wx >> 4) : -(((-wx) + 15) >> 4);
            int cz = wz >= 0 ? (wz >> 4) : -(((-wz) + 15) >> 4);
            int lx = wx - cx * 16;
            int lz = wz - cz * 16;

            if (worldChunks.TryGetValue((cx, cz), out uint[]? chunk) && chunk != null)
            {
                int topY = Stage1InteractivePlayController.MaxEditableVoxelY - 2;
                for (int y = topY; y >= 1; y--)
                {
                    uint raw = chunk[(y << 8) | (lz << 4) | lx];
                    if (VoxelBitUtility.IsPhysicallySolid(raw))
                    {
                        return y + 1.0f;
                    }
                }
            }

            return StratocraftFloatingIslandWorldGen.SampleIslandTopSurfaceY(worldX, worldZ);
        }
    }
}
#endif
