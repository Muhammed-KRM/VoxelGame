#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.CreaturesAI.CreaturesAndBosses;
using Stratocraft.Voxel.Jobs;
using UnityEngine;

namespace Stratocraft.Character
{
    /// <summary>
    /// Paket 3 Faz 3.4: Sifir harici ses dosyasi (0 Byte disk/asset), 0 MB doku VRAM ve 0 B/kare runtime GC
    /// ile calisan prosedurel PCM akustik telgraf ve tavan tas tozu dokulme kontrolcusu.
    /// - InitializeProceduralClipsOnce(): 22050 Hz tek kanalli (~0.55 sn) 5 sentetik PCM telgraf klibi uretir:
    ///   SpiderClick, BatScreech, CrystalChime, ArmorRattle, MagmaHiss.
    /// - 4 elemanli sabit 3B AudioSource havuzu (spatialBlend = 1.0f, minDistance = 2.5f, maxDistance = 18.0f).
    /// - 16 elemanli sabit mikro-tas/toz kup havuzu (FallingCaveDustPool) ile telgraf aninda tavandan toz/tas dusurur.
    /// </summary>
    public sealed class Stage1CaveAcousticTelegraphController
    {
        public const int SampleRateHz = 22050;
        public const float ClipDurationSeconds = 0.55f;
        public const int SampleCount = 12128; // ~0.55 sn @ 22050 Hz
        public const int AudioSourcePoolSize = 4;
        public const int FallingDustPoolSize = 16;

        public struct FallingDustSlot
        {
            public Transform? Transform;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 AngularVelocityDeg;
            public float RemainingLifetime;
            public byte Active;
        }

        private static bool s_clipsInitialized;
        private static AudioClip? s_spiderClickClip;
        private static AudioClip? s_batScreechClip;
        private static AudioClip? s_crystalChimeClip;
        private static AudioClip? s_armorRattleClip;
        private static AudioClip? s_magmaHissClip;
        private static readonly float[] s_pcmScratchBuffer = new float[SampleCount];
        private static Mesh? s_sharedDustPebbleMesh;
        private static readonly List<FacetedVoxelTriangle> s_meshBuildTris = new(24);

        private readonly AudioSource?[] _audioSourcePool = new AudioSource?[AudioSourcePoolSize];
        private readonly Transform?[] _audioSourceTransforms = new Transform?[AudioSourcePoolSize];
        private readonly FallingDustSlot[] _fallingCaveDustPool = new FallingDustSlot[FallingDustPoolSize];
        private GameObject? _poolContainerRoot;
        private int _audioRoundRobinCursor;
        private int _dustRoundRobinCursor;
        private int _lastPlayModeStepFrame = -1;

        public Material? SharedVoxelMaterial { get; set; }
        public bool AreClipsInitialized => s_clipsInitialized && s_spiderClickClip != null;
        public AudioClip? SpiderClickClip => s_spiderClickClip;
        public AudioClip? BatScreechClip => s_batScreechClip;
        public AudioClip? CrystalChimeClip => s_crystalChimeClip;
        public AudioClip? ArmorRattleClip => s_armorRattleClip;
        public AudioClip? MagmaHissClip => s_magmaHissClip;

        public int ActiveDustParticleCount { get; private set; }
        public int TotalTelegraphsTriggered { get; private set; }
        public CaveCreatureVariantKind LastTelegraphedVariant { get; private set; }
        public Vector3 LastTelegraphWorldPos { get; private set; }
        public string LastTelegraphStatusText { get; private set; } = string.Empty;

        public ReadOnlySpan<FallingDustSlot> FallingCaveDustPool => _fallingCaveDustPool;

        /// <summary>
        /// Baslangicta bir kez 22050 Hz mono PCM ornekleriyle 5 magara canlisi telgraf ses klibini sentezler.
        /// Birden fazla kez cagrilsa bile yalnizca ilk cagride uretim yapar (idempotent, 0 B runtime GC).
        /// </summary>
        public void InitializeProceduralClipsOnce()
        {
            if (s_clipsInitialized && s_spiderClickClip != null)
            {
                return;
            }

            s_spiderClickClip = BuildProceduralClip("PCM_CaveTelegraph_SpiderClick", CaveCreatureVariantKind.GiantCaveSpider);
            s_batScreechClip = BuildProceduralClip("PCM_CaveTelegraph_BatScreech", CaveCreatureVariantKind.CaveBatCrawler);
            s_crystalChimeClip = BuildProceduralClip("PCM_CaveTelegraph_CrystalChime", CaveCreatureVariantKind.CrystalCavernScorpion);
            s_armorRattleClip = BuildProceduralClip("PCM_CaveTelegraph_ArmorRattle", CaveCreatureVariantKind.AbyssalSkeletonKnight);
            s_magmaHissClip = BuildProceduralClip("PCM_CaveTelegraph_MagmaHiss", CaveCreatureVariantKind.MagmaSerpent);
            s_clipsInitialized = true;
        }

        public AudioClip? GetClipForVariant(CaveCreatureVariantKind variant)
        {
            InitializeProceduralClipsOnce();
            return variant switch
            {
                CaveCreatureVariantKind.GiantCaveSpider => s_spiderClickClip,
                CaveCreatureVariantKind.CaveBatCrawler => s_batScreechClip,
                CaveCreatureVariantKind.CrystalCavernScorpion => s_crystalChimeClip,
                CaveCreatureVariantKind.AbyssalSkeletonKnight => s_armorRattleClip,
                CaveCreatureVariantKind.MagmaSerpent => s_magmaHissClip,
                _ => s_spiderClickClip
            };
        }

        /// <summary>
        /// Verilen magara canlisi varyanti icin worldCeilingPos noktasinda 3B uzamsal ses telgrafi calar
        /// ve tavandan oyuncunun gorus alanina dogru mikro-tas/toz parcaciklari doker.
        /// </summary>
        public void TriggerTelegraph(CaveCreatureVariantKind variant, Vector3 worldCeilingPos, Vector3 playerPos)
        {
            if (variant == CaveCreatureVariantKind.None)
            {
                return;
            }

            InitializeProceduralClipsOnce();
            EnsurePoolsCreated();

            AudioClip? clip = GetClipForVariant(variant);
            int audioIdx = _audioRoundRobinCursor;
            _audioRoundRobinCursor = (_audioRoundRobinCursor + 1) & (AudioSourcePoolSize - 1);

            Transform? srcTr = _audioSourceTransforms[audioIdx];
            AudioSource? src = _audioSourcePool[audioIdx];
            if (srcTr != null && src != null && clip != null)
            {
                srcTr.position = worldCeilingPos;
                src.clip = clip;
                src.pitch = 0.94f + ((TotalTelegraphsTriggered & 3) * 0.04f);
                src.volume = 0.88f;
                src.Play();
            }

            // Tavandan oyuncunun onune dogru 4 adet mikro-tas/toz parcacigi birak (0 B GC sabit havuz)
            Vector3 toPlayer = playerPos - worldCeilingPos;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                toPlayer.Normalize();
            }

            Vector3 dropCenter = worldCeilingPos + (toPlayer * 0.65f);
            for (int k = 0; k < 4; k++)
            {
                int slotIdx = _dustRoundRobinCursor;
                _dustRoundRobinCursor = (_dustRoundRobinCursor + 1) & (FallingDustPoolSize - 1);

                ref FallingDustSlot slot = ref _fallingCaveDustPool[slotIdx];
                float angle = (k * 1.570796f) + (TotalTelegraphsTriggered * 0.65f);
                float radial = 0.18f + ((k & 1) * 0.16f);
                Vector3 spawnPos = new Vector3(
                    dropCenter.x + (Mathf.Cos(angle) * radial),
                    worldCeilingPos.y - (0.08f * k),
                    dropCenter.z + (Mathf.Sin(angle) * radial));

                Vector3 vel = new Vector3(
                    Mathf.Cos(angle) * 0.35f + toPlayer.x * 0.25f,
                    -1.45f - (k * 0.35f),
                    Mathf.Sin(angle) * 0.35f + toPlayer.z * 0.25f);

                if (slot.Active == 0)
                {
                    ActiveDustParticleCount++;
                }

                slot.Position = spawnPos;
                slot.Velocity = vel;
                slot.AngularVelocityDeg = new Vector3(210f + k * 35f, 160f, 280f - k * 40f);
                slot.RemainingLifetime = 1.35f;
                slot.Active = 1;

                if (slot.Transform != null)
                {
                    slot.Transform.position = spawnPos;
                    float scale = 0.065f + ((k & 1) * 0.025f);
                    slot.Transform.localScale = new Vector3(scale, scale, scale);
                    if (!slot.Transform.gameObject.activeSelf)
                    {
                        slot.Transform.gameObject.SetActive(true);
                    }
                }
            }

            TotalTelegraphsTriggered++;
            LastTelegraphedVariant = variant;
            LastTelegraphWorldPos = worldCeilingPos;
            LastTelegraphStatusText = GetTelegraphCaption(variant);
        }

        /// <summary>
        /// Tavandan dokulen tas tozu parcaciklarinin balistik dususunu ilerletir (0 B/kare GC).
        /// </summary>
        public void StepDustParticles(float dt)
        {
            if (dt <= 0f || ActiveDustParticleCount <= 0)
            {
                return;
            }

            if (Application.isPlaying)
            {
                int curFrame = Time.frameCount;
                if (_lastPlayModeStepFrame == curFrame && curFrame > 0)
                {
                    return;
                }
                _lastPlayModeStepFrame = curFrame;
            }

            int remainingActive = 0;
            for (int i = 0; i < FallingDustPoolSize; i++)
            {
                ref FallingDustSlot slot = ref _fallingCaveDustPool[i];
                if (slot.Active == 0)
                {
                    continue;
                }

                slot.RemainingLifetime -= dt;
                if (slot.RemainingLifetime <= 0f)
                {
                    slot.Active = 0;
                    slot.RemainingLifetime = 0f;
                    if (slot.Transform != null && slot.Transform.gameObject.activeSelf)
                    {
                        slot.Transform.gameObject.SetActive(false);
                    }
                    continue;
                }

                remainingActive++;
                slot.Velocity.y -= 9.81f * dt;
                slot.Position += slot.Velocity * dt;

                if (slot.Transform != null)
                {
                    slot.Transform.position = slot.Position;
                    slot.Transform.Rotate(slot.AngularVelocityDeg * dt, Space.Self);
                }
            }

            ActiveDustParticleCount = remainingActive;
        }

        private void EnsurePoolsCreated()
        {
            if (_poolContainerRoot != null)
            {
                return;
            }

            _poolContainerRoot = new GameObject("CaveAcousticTelegraph_Pools");
            if (s_sharedDustPebbleMesh == null)
            {
                s_meshBuildTris.Clear();
                HybridSurfaceNetsAndShapeMesher.EmitArchitecturalShape(
                    new System.Numerics.Vector3(-0.5f, -0.5f, -0.5f),
                    MaterialIdCatalog.Stone,
                    1,
                    0,
                    8,
                    s_meshBuildTris);
                s_sharedDustPebbleMesh = HybridSurfaceNetsAndShapeMesher.BuildUnityMeshZeroGc(
                    s_meshBuildTris, "Mesh_CaveFallingDustPebble_Shared");
            }

            for (int i = 0; i < AudioSourcePoolSize; i++)
            {
                GameObject audioGo = new GameObject($"CaveTelegraphAudio_{i}");
                audioGo.transform.SetParent(_poolContainerRoot.transform, false);
                AudioSource src = audioGo.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 1.0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2.5f;
                src.maxDistance = 18.0f;
                _audioSourcePool[i] = src;
                _audioSourceTransforms[i] = audioGo.transform;
            }

            for (int i = 0; i < FallingDustPoolSize; i++)
            {
                GameObject dustGo = new GameObject($"CaveFallingDust_{i}");
                dustGo.transform.SetParent(_poolContainerRoot.transform, false);
                dustGo.transform.localScale = new Vector3(0.07f, 0.07f, 0.07f);
                MeshFilter mf = dustGo.AddComponent<MeshFilter>();
                mf.sharedMesh = s_sharedDustPebbleMesh;
                MeshRenderer mr = dustGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (SharedVoxelMaterial != null)
                {
                    mr.sharedMaterial = SharedVoxelMaterial;
                }
                dustGo.SetActive(false);

                _fallingCaveDustPool[i] = new FallingDustSlot
                {
                    Transform = dustGo.transform,
                    Position = Vector3.zero,
                    Velocity = Vector3.zero,
                    AngularVelocityDeg = Vector3.zero,
                    RemainingLifetime = 0f,
                    Active = 0
                };
            }
        }

        private static AudioClip BuildProceduralClip(string clipName, CaveCreatureVariantKind variant)
        {
            FillSynthSamples(s_pcmScratchBuffer, variant);
            AudioClip clip = AudioClip.Create(clipName, SampleCount, 1, SampleRateHz, false);
            clip.SetData(s_pcmScratchBuffer, 0);
            return clip;
        }

        private static void FillSynthSamples(float[] buffer, CaveCreatureVariantKind variant)
        {
            const float invRate = 1.0f / SampleRateHz;
            const float twoPi = 6.2831853f;
            float lpState = 0f;
            uint rng = (uint)variant * 0x9E3779B9u + 0x13579BDFu;

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i * invRate;
                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                float white = ((rng & 0xFFFFu) / 32767.5f) - 1.0f;
                float sample = 0f;

                switch (variant)
                {
                    case CaveCreatureVariantKind.GiantCaveSpider:
                    {
                        // 4x ardisik kitin bacak tikirtisi (1.4 kHz - 3.2 kHz, 18 ms keskin zarf) + tas surtunmesi
                        for (int pulse = 0; pulse < 4; pulse++)
                        {
                            float tPulse = t - (pulse * 0.115f);
                            if (tPulse >= 0f && tPulse < 0.075f)
                            {
                                float env = Mathf.Exp(-tPulse * 62.0f);
                                float f1 = 1520f + (pulse * 340f);
                                float f2 = 2840f - (pulse * 180f);
                                sample += env * (0.62f * Mathf.Sin(twoPi * f1 * tPulse) +
                                                 0.38f * Mathf.Sin(twoPi * f2 * tPulse) +
                                                 0.25f * white);
                            }
                        }
                        break;
                    }

                    case CaveCreatureVariantKind.CaveBatCrawler:
                    {
                        // Tiz ekolokasyon asagi frekans supurmesi (7.5 kHz -> 3.8 kHz) + 28 Hz kanat cirpma AM
                        float chirpPhase = twoPi * ((7500f * t) - (3350f * t * t));
                        float env = Mathf.Sin(Mathf.Clamp01(t / ClipDurationSeconds) * Mathf.PI);
                        float wingFlutter = 0.55f + 0.45f * Mathf.Sin(twoPi * 28.0f * t);
                        sample = env * wingFlutter * (0.72f * Mathf.Sin(chirpPhase) + 0.18f * white);
                        break;
                    }

                    case CaveCreatureVariantKind.CrystalCavernScorpion:
                    {
                        // Uyumsuz harmonikli kristal can rezonansi (1.85 kHz + 2.77 kHz + 4.12 kHz) + kiskac darbesi
                        float env = Mathf.Exp(-t * 5.2f);
                        float snapEnv = t > 0.18f ? Mathf.Exp(-(t - 0.18f) * 48.0f) : 0f;
                        sample = env * (0.50f * Mathf.Sin(twoPi * 1850f * t) +
                                        0.32f * Mathf.Sin(twoPi * 2770f * t) +
                                        0.18f * Mathf.Sin(twoPi * 4120f * t)) +
                                 snapEnv * (0.55f * Mathf.Sin(twoPi * 1180f * t) + 0.30f * white);
                        break;
                    }

                    case CaveCreatureVariantKind.AbyssalSkeletonKnight:
                    {
                        // Metalik rezonansli pasli zirh gicirtisi ve kemik sakirtisi (420 Hz + 680 Hz sonumlu FM)
                        float burstTime = t % 0.26f;
                        float env = Mathf.Exp(-burstTime * 11.5f);
                        float fmMod = Mathf.Sin(twoPi * 96f * t) * 3.2f;
                        sample = env * (0.58f * Mathf.Sin(twoPi * 420f * t + fmMod) +
                                        0.32f * Mathf.Sin(twoPi * 680f * t - fmMod * 0.5f) +
                                        0.15f * white);
                        break;
                    }

                    case CaveCreatureVariantKind.MagmaSerpent:
                    {
                        // Alcak-geciren moduleli termal tislama (250 Hz - 1.6 kHz) + kabarcik patlamasi
                        lpState += 0.18f * (white - lpState);
                        float env = Mathf.Sin(Mathf.Clamp01(t / ClipDurationSeconds) * Mathf.PI);
                        float bubbleEnv = t > 0.22f ? Mathf.Exp(-(t - 0.22f) * 26.0f) : 0f;
                        sample = env * (0.65f * lpState + 0.25f * Mathf.Sin(twoPi * 310f * t)) +
                                 bubbleEnv * 0.52f * Mathf.Sin(twoPi * 135f * t);
                        break;
                    }
                }

                buffer[i] = Mathf.Clamp(sample, -0.95f, 0.95f);
            }
        }

        private static string GetTelegraphCaption(CaveCreatureVariantKind variant) => variant switch
        {
            CaveCreatureVariantKind.GiantCaveSpider => "TAVAN TIKIRTISI & TAS TOZU (Dev Magara Orumcegi Pususu!)",
            CaveCreatureVariantKind.CaveBatCrawler => "EKOLOKASYON CIGLIGI & KANAT SESI (Magara Yarasasi Dalisi!)",
            CaveCreatureVariantKind.CrystalCavernScorpion => "KRISTAL REZONANS CINLAMASI (Kristal Magara Akrebi!)",
            CaveCreatureVariantKind.AbyssalSkeletonKnight => "PASLI ZIRH & KEMIK GICIRTISI (Derinlik Iskelet Sovalyesi!)",
            CaveCreatureVariantKind.MagmaSerpent => "DERIN MAGMA TISLAMASI (Ates / Magma Yilani!)",
            _ => "MAGARA AKUSTIK TELGRAFI"
        };
    }
}
#endif
