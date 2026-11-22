#nullable enable
#if UNITY_5_3_OR_NEWER
using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
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

        private static bool ShouldUseNetCodeClientServerWorlds()
        {
#if UNITY_SERVER
            return true;
#else
            if (EnableMultiplayerNetCodeWorlds)
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

            // If NUnit is actively executing a test context (EditMode/PlayMode test runner),
            // preserve standard NetCode bootstrap behavior for any NetCode-dependent tests.
            try
            {
                Type? testContextType = Type.GetType("NUnit.Framework.TestContext, nunit.framework");
                if (testContextType != null)
                {
                    object? currentContext = testContextType.GetProperty("CurrentContext")?.GetValue(null);
                    object? currentTest = currentContext?.GetType().GetProperty("Test")?.GetValue(currentContext);
                    string? testFullName = currentTest?.GetType().GetProperty("FullName")?.GetValue(currentTest) as string;
                    if (!string.IsNullOrEmpty(testFullName) &&
                        !string.Equals(testFullName, "AdhocContext", StringComparison.OrdinalIgnoreCase))
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
