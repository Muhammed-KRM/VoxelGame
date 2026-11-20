#nullable enable
using System;
using System.Numerics;
using Stratocraft.CreaturesAI.Data;

namespace Stratocraft.CreaturesAI.Systems
{
    public enum CreatureDefenseAction : byte
    {
        None = 0,
        DirectionalBlock = 1,
        SideDodge = 2,
        BackstepDodge = 3
    }

    /// <summary>
    /// Dalga A (Faz A.3): Sifir-GC sabit boyutlu (8 kayitlik) oyuncu aliskanlik hafizasi,
    /// yonlu blok / kacinma karar mekanizmasi ve 3B ileri koni fiziksel saldiri hitbox cozucusu.
    /// </summary>
    public struct CreaturePatternMemory
    {
        private byte _d0, _d1, _d2, _d3, _d4, _d5, _d6, _d7;
        private byte _count;
        private byte _cursor;

        public void RecordPlayerAttackDirection(CreatureAttackDirection dir)
        {
            byte v = (byte)dir;
            switch (_cursor)
            {
                case 0: _d0 = v; break;
                case 1: _d1 = v; break;
                case 2: _d2 = v; break;
                case 3: _d3 = v; break;
                case 4: _d4 = v; break;
                case 5: _d5 = v; break;
                case 6: _d6 = v; break;
                default: _d7 = v; break;
            }
            _cursor = (byte)((_cursor + 1) & 7);
            if (_count < 8) _count++;
        }

        public readonly float GetDirectionFrequency01(CreatureAttackDirection dir)
        {
            if (_count == 0) return 0f;
            byte target = (byte)dir;
            int hits = 0;
            if (_count > 0 && _d0 == target) hits++;
            if (_count > 1 && _d1 == target) hits++;
            if (_count > 2 && _d2 == target) hits++;
            if (_count > 3 && _d3 == target) hits++;
            if (_count > 4 && _d4 == target) hits++;
            if (_count > 5 && _d5 == target) hits++;
            if (_count > 6 && _d6 == target) hits++;
            if (_count > 7 && _d7 == target) hits++;
            return hits / (float)_count;
        }

        /// <summary>
        /// Oyuncu saldiri yonu + canlinin mesafe/tur zekasi + ogrenilmis tekrar sikligina gore
        /// fiziksel yonlu blok veya kacinma karari verir.
        /// Yonlu blokta canli yalnizca guardedDirection yonunu kapatir — karsi yon acik kalir!
        /// </summary>
        public readonly CreatureDefenseAction EvaluateDefenseReaction(
            in CreatureSpeciesAiProfile profile,
            CreatureAttackDirection incomingPlayerDir,
            float intelligenceBonus01,
            float roll01,
            out CreatureAttackDirection guardedDirection)
        {
            guardedDirection = incomingPlayerDir;
            float intel = Math.Clamp(profile.BaseIntelligence01 + intelligenceBonus01, 0.1f, 0.95f);
            float habitFreq = GetDirectionFrequency01(incomingPlayerDir);

            // Tekrarlanan saldiri yonu canlinin reaksiyon ihtimalini belirgin bicimde artirir!
            float adaptBoost = habitFreq * (0.35f + intel * 0.45f);

            if (profile.CanBlockDirectionally)
            {
                float blockProb = Math.Clamp(profile.BaseBlockChance + adaptBoost, 0f, 0.82f);
                if (roll01 < blockProb)
                {
                    guardedDirection = incomingPlayerDir;
                    return CreatureDefenseAction.DirectionalBlock;
                }
            }

            if (profile.CanDodge)
            {
                float dodgeProb = Math.Clamp(profile.BaseDodgeChance + adaptBoost * 0.65f, 0f, 0.58f);
                if (roll01 < dodgeProb)
                {
                    return incomingPlayerDir is CreatureAttackDirection.Left or CreatureAttackDirection.Right
                        ? CreatureDefenseAction.BackstepDodge
                        : CreatureDefenseAction.SideDodge;
                }
            }

            return CreatureDefenseAction.None;
        }

        /// <summary>
        /// Canlinin aktif gard yonu, oyuncunun gelen kesik yonunu fiziksel olarak karsiliyor mu?
        /// (Sagi bloklayan canliya soldan vurulursa blok asilir!)
        /// </summary>
        public static bool DoesCreatureGuardBlockAttack(
            CreatureAttackDirection creatureGuardDir,
            CreatureAttackDirection playerAttackDir)
        {
            if (playerAttackDir == CreatureAttackDirection.Lunge)
            {
                // Saplama merkezden gelir; yalnizca Overhead veya DualSide gard kismen kapatabilir
                return creatureGuardDir == CreatureAttackDirection.DualSide;
            }
            return creatureGuardDir == playerAttackDir || creatureGuardDir == CreatureAttackDirection.DualSide;
        }

        /// <summary>
        /// Fiziksel ileri koni + mesafe + dikey aralik hitbox kontrolu.
        /// Oyuncu yana veya geriye kacarsa darbe iskalayip 0 hasar verir!
        /// </summary>
        public static bool EvaluateForwardConeMeleeHit(
            Vector3 creaturePos,
            Vector3 creatureForwardXZ,
            Vector3 playerPos,
            float reachMeters,
            float halfConeDegrees,
            float maxVerticalDeltaMeters = 2.05f)
        {
            float dy = MathF.Abs(playerPos.Y - creaturePos.Y);
            if (dy > maxVerticalDeltaMeters) return false;

            float dx = playerPos.X - creaturePos.X;
            float dz = playerPos.Z - creaturePos.Z;
            float horizDistSq = dx * dx + dz * dz;
            if (horizDistSq > reachMeters * reachMeters) return false;
            if (horizDistSq < 0.04f) return true;

            float invD = 1.0f / MathF.Sqrt(horizDistSq);
            float dirX = dx * invD;
            float dirZ = dz * invD;

            float fLen = MathF.Sqrt(creatureForwardXZ.X * creatureForwardXZ.X + creatureForwardXZ.Z * creatureForwardXZ.Z);
            if (fLen < 1e-4f) return true;
            float fx = creatureForwardXZ.X / fLen;
            float fz = creatureForwardXZ.Z / fLen;

            float dot = dirX * fx + dirZ * fz;
            float cosHalfCone = MathF.Cos(Math.Clamp(halfConeDegrees, 10f, 175f) * (MathF.PI / 180f));
            return dot >= cosHalfCone;
        }

        public static CreatureAttackDirection ClassifyPlayerSlashDirection(float dirX, float dirY, bool isThrust)
        {
            if (isThrust) return CreatureAttackDirection.Lunge;
            if (MathF.Abs(dirY) >= MathF.Abs(dirX))
            {
                return dirY <= 0f ? CreatureAttackDirection.Overhead : CreatureAttackDirection.Underhand;
            }
            return dirX >= 0f ? CreatureAttackDirection.Left : CreatureAttackDirection.Right;
        }
    }
}
