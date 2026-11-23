#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Stratocraft.Networking
{
    /// <summary>
    /// Single-player PlayMode ECS bootstrap that overrides Unity.NetCode's <see cref="ClientServerBootstrap"/>
    /// (which implements <see cref="ICustomBootstrap"/>) so normal single-player sessions create only the
    /// standard local ECS world instead of idle Server/Client NetCode worlds that emit thousands of
    /// "Server Tick Batching" warnings.
    /// Automatically defers to <see cref="ClientServerBootstrap.Initialize(string)"/> when running inside
    /// NUnit/Unity Test Runner or when multiplayer NetCode bootstrapping is explicitly enabled.
    /// </summary>
    [Preserve]
    public sealed class SinglePlayerNetCodeBootstrap : ClientServerBootstrap
    {
        /// <summary>
        /// Set to <c>true</c> before entering multiplayer/dedicated-server mode to restore full
        /// Unity.NetCode Client/Server world creation.
        /// </summary>
        public static bool EnableMultiplayerNetCodeWorlds { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateForDomainReloadDisabled()
        {
            EnableMultiplayerNetCodeWorlds = false;
        }

        public override bool Initialize(string defaultWorldName)
        {
            if (ShouldUseNetCodeClientServerWorlds())
            {
                return base.Initialize(defaultWorldName);
            }

            // Initialize only the standard local ECS world (no idle NetCode Server/Client worlds).
            string worldName = string.IsNullOrWhiteSpace(defaultWorldName) ? "Default World" : defaultWorldName;
            CreateLocalWorld(worldName);
            return true;
        }

        public static bool ShouldUseNetCodeClientServerWorlds()
        {
            return SinglePlayerNetCodePolicy.ShouldUseNetCodeClientServerWorlds();
        }
    }

    /// <summary>
    /// Assembly-boundary-safe helper for querying whether NetCode Client/Server worlds should be active
    /// without requiring callers (such as <c>Stratocraft.Character</c>) to reference <c>Unity.NetCode</c>.
    /// </summary>
    public static class SinglePlayerNetCodePolicy
    {
        public static bool ShouldUseNetCodeClientServerWorlds()
        {
#if UNITY_SERVER
            return true;
#else
            if (SinglePlayerNetCodeBootstrap.EnableMultiplayerNetCodeWorlds)
            {
                return true;
            }

            if (Application.isBatchMode)
            {
                return true;
            }

            string[] cmdArgs = Environment.GetCommandLineArgs();
            for (int i = 0; i < cmdArgs.Length; i++)
            {
                string arg = cmdArgs[i];
                if (string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-multiplayer", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-dedicatedServer", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // If Unity Test Runner / NUnit is actively executing a test (real InitTestScene /
            // ActiveController or active TestExecutionContext.CurrentTest — ignoring orphan DontSave
            // runner GameObjects and stale TestContext.CurrentContext when Reload Domain is disabled),
            // preserve NetCode worlds.
            try
            {
                if (SceneManager.GetActiveScene().name.StartsWith("InitTestScene", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                int loadedScenes = SceneManager.sceneCount;
                for (int i = 0; i < loadedScenes; i++)
                {
                    if (SceneManager.GetSceneAt(i).name.StartsWith("InitTestScene", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                Type? ptcType = Type.GetType("UnityEngine.TestTools.TestRunner.PlaymodeTestsController, UnityEngine.TestRunner");
                object? activeCtrl = ptcType?.GetProperty("ActiveController")?.GetValue(null);
                if (activeCtrl is Component ctrlComp)
                {
                    string runnerSceneName = ctrlComp.gameObject.scene.name;
                    if (!string.IsNullOrEmpty(runnerSceneName) &&
                        runnerSceneName.StartsWith("InitTestScene", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    object? settingsObj = activeCtrl.GetType().GetField("settings")?.GetValue(activeCtrl)
                        ?? activeCtrl.GetType().GetProperty("settings")?.GetValue(activeCtrl);
                    string? bootstrapScene = settingsObj?.GetType().GetField("bootstrapScene")?.GetValue(settingsObj) as string
                        ?? settingsObj?.GetType().GetProperty("bootstrapScene")?.GetValue(settingsObj) as string;
                    if (!string.IsNullOrEmpty(bootstrapScene))
                    {
                        return true;
                    }
                }

                Type? execCtxType = Type.GetType("NUnit.Framework.Internal.TestExecutionContext, nunit.framework");
                if (execCtxType != null)
                {
                    object? execCtx = execCtxType.GetProperty("CurrentContext")?.GetValue(null);
                    object? currentTest = execCtx?.GetType().GetProperty("CurrentTest")?.GetValue(execCtx);
                    if (currentTest != null)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore reflection errors outside NUnit runtime.
            }

            return false;
#endif
        }
    }
}
#endif
