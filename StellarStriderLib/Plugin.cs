using System;
using System.Threading;
using MelonLoader;
using StellarStriderLib.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(StellarStriderLib.Plugin), "StellarStriderLib", "1.7.0", "StriderDev")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: MelonPriority(-1000)]

namespace StellarStriderLib
{
    public sealed class Plugin : MelonMod
    {
        private static int _mainThreadId;
        private static bool _stopRequested;
        private bool _initialized;
        private static bool _returnToTitle;
        private Vector2 _errorScroll;

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
            if (!_returnToTitle) LastError = message;
            _returnToTitle |= stop;
            _stopRequested |= stop;
            MelonLogger.Error(message);
        }

        internal static void ClearError()
        {
            LastError = "";
            _stopRequested = false;
            _returnToTitle = false;
        }

        public override void OnInitializeMelon()
        {
            CheckThread();
            ModOptionsMenu.Install(HarmonyInstance);
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
                ThermalRuntime.Install(HarmonyInstance);
                ThermalNetwork.Install();
                ThermalVisuals.Install(HarmonyInstance);
                _initialized = true;
                LoggerInstance.Msg("Registration API ready; waiting for a world registry.");
            }
            catch (Exception error)
            {
                Report("StellarStriderLib initialization failed: " + error);
            }
        }

        public override void OnLateInitializeMelon() => StriderConfig.SaveDefaults();

        public override void OnUpdate()
        {
            RecoverWorldLoad();
            if (!_initialized) return;

            PerformanceProfiler.Update();
            try
            {
                LoadingScreen.Update();
                RegistryNetwork.Update();
                PartStateNetwork.Update();
                ThermalRuntime.UpdateClock();
                ThermalNetwork.Pump();
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

        private static void RecoverWorldLoad()
        {
            if (!_returnToTitle) return;
            _returnToTitle = false;
            _stopRequested = false;
            try
            {
                var network = FishNet.InstanceFinder.NetworkManager;
                if (network != null)
                {
                    if (network.IsClientStarted) network.ClientManager.StopConnection();
                    if (network.IsServerStarted) network.ServerManager.StopConnection(true);
                }
            }
            catch (Exception error)
            {
                MelonLogger.Error("Could not stop failed world connection: " + error);
            }
            finally
            {
                ContentSession.End();
                try
                {
                    if (SceneManager.GetActiveScene().buildIndex != 0) SceneManager.LoadScene(0);
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                catch (Exception error)
                {
                    MelonLogger.Error("Could not return to title screen: " + error);
                }
            }
        }

        public override void OnGUI()
        {
            if (LastError.Length == 0 || _returnToTitle)
            {
                PerformanceProfiler.Draw();
                return;
            }
            var previousColor = GUI.color;
            var previousDepth = GUI.depth;
            GUI.depth = -32000;
            GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var width = Mathf.Min(680, Screen.width - 32);
            var height = Mathf.Min(380, Screen.height - 32);
            var rect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            GUI.color = new Color(0.08f, 0.12f, 0.18f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.8f, 1);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var heading = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = true };
            var body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 20, rect.width - 48, rect.height - 40));
            GUILayout.Label("Unable to load world", heading);
            GUILayout.Label("World loading stopped for the following reason:", body);
            GUILayout.Space(12);
            _errorScroll = GUILayout.BeginScrollView(_errorScroll);
            GUILayout.Label(LastError, body);
            GUILayout.EndScrollView();
            GUILayout.Space(16);
            if (GUILayout.Button("Dismiss", GUILayout.Height(42))) ClearError();
            GUILayout.EndArea();
            if (Event.current.isMouse) Event.current.Use();
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        public override void OnDeinitializeMelon()
        {
            PerformanceProfiler.Close();
            LoadingScreen.Shutdown();
            if (!_initialized)
            {
                return;
            }

            PartStateNetwork.Shutdown();
            ThermalNetwork.Shutdown();
            RegistryNetwork.Shutdown();
            ContentSession.End();
            ThermalVisuals.Shutdown();
            foreach (var mod in StriderRegistry.Mods.Values)
            {
                mod.Assets.Release();
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) =>
            LoadingScreen.SceneInitialized(buildIndex, sceneName);
    }
}
