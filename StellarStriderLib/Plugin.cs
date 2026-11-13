using System;
using System.Threading;
using MelonLoader;
using StellarStriderLib.Runtime;
using UnityEngine;

[assembly: MelonInfo(typeof(StellarStriderLib.Plugin), "StellarStriderLib", "1.2.0", "StriderDev")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: MelonPriority(-1000)]

namespace StellarStriderLib
{
    public sealed class Plugin : MelonMod
    {
        private static int _mainThreadId;
        private static bool _stopRequested;
        private bool _initialized;

        internal static string LastError { get; private set; } = "";

        internal static void CheckThread()
        {
            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == 0)
            {
                _mainThreadId = currentThreadId;
            }

            if (_mainThreadId != currentThreadId)
            {
                throw new InvalidOperationException("StellarStriderLib API must run on the Unity main thread.");
            }
        }

        internal static void Report(string message, bool stop = true)
        {
            LoadingScreen.Cancel();
            LastError = message;
            _stopRequested |= stop;
            MelonLogger.Error(message);
        }

        internal static void ClearError()
        {
            LastError = "";
            _stopRequested = false;
        }

        public override void OnInitializeMelon()
        {
            CheckThread();
            try
            {
                GameHooks.Install(HarmonyInstance);
                SocketGrid.Install(HarmonyInstance);
                WorldLifecycle.Install(HarmonyInstance);
                RegistryNetwork.Install(HarmonyInstance);
                PartShapes.Install(HarmonyInstance);
                PartRuntime.Install(HarmonyInstance);
                PartMovement.Install(HarmonyInstance);
                PartStateSave.Install(HarmonyInstance);
                PartStateNetwork.Install();
                LoadingScreen.Install(HarmonyInstance);
                _initialized = true;
                LoggerInstance.Msg("Registration API ready; waiting for a world registry.");
            }
            catch (Exception error)
            {
                Report("StellarStriderLib initialization failed: " + error);
            }
        }

        public override void OnUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                LoadingScreen.Update();
                RegistryNetwork.Update();
                PartStateNetwork.Update();
                if (!_stopRequested)
                {
                    return;
                }

                _stopRequested = false;
                var network = FishNet.InstanceFinder.NetworkManager;
                if (network == null)
                {
                    return;
                }

                if (network.IsClientStarted)
                {
                    network.ClientManager.StopConnection();
                }

                if (network.IsServerStarted)
                {
                    network.ServerManager.StopConnection(true);
                }
            }
            catch (Exception error)
            {
                Report("StellarStriderLib: " + error.Message);
            }
        }

        public override void OnGUI()
        {
            if (LastError.Length == 0)
            {
                return;
            }

            var errorRect = new Rect(Math.Max(20, (Screen.width - 620) / 2), 40, 620, 180);
            GUILayout.BeginArea(errorRect, GUI.skin.box);
            GUILayout.Label(LastError);
            if (GUILayout.Button("Dismiss"))
            {
                ClearError();
            }

            GUILayout.EndArea();
        }

        public override void OnDeinitializeMelon()
        {
            LoadingScreen.Shutdown();
            if (!_initialized)
            {
                return;
            }

            PartStateNetwork.Shutdown();
            RegistryNetwork.Shutdown();
            ContentSession.End();
            foreach (var mod in StriderRegistry.Mods.Values)
            {
                mod.Assets.Release();
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) =>
            LoadingScreen.SceneInitialized(buildIndex, sceneName);
    }
}
