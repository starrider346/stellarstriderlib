using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Transporting;
using HarmonyLib;
using MelonLoader;
using Managers.Interface;
using StellarStriderLib.Core;
using UI.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    internal static class LoadingScreen
    {
        private static readonly LoadingProgress Progress = new();
        private static readonly Dictionary<CanvasGroup, float> Hidden = new();
        private static readonly HashSet<CanvasGroup> AddedGroups = new();
        private static readonly HashSet<string> InvalidWallpapers = new(StringComparer.Ordinal);
        private static readonly System.Random Random = new();
        private static LoadingScreenView _view;
        private static bool _failed, _bypassTransition, _transitionQueued, _wallpaperSelected;
        private static int _generation, _completeFrame = -1;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                Patch(harmony, typeof(LoadingScreenDisabler), "Awake", nameof(NativeAwake));
                harmony.Patch(AccessTools.Method(typeof(LoadingScreenDisabler), "DisableLoadingScreen"),
                    postfix: new HarmonyMethod(typeof(LoadingScreen), nameof(PlayerReady)));
                harmony.Patch(AccessTools.Method(typeof(SceneManager), "LoadScene",
                        new[] { typeof(string), typeof(LoadSceneMode) }),
                    prefix: new HarmonyMethod(typeof(LoadingScreen), nameof(SceneTransition)));
                Patch(harmony, typeof(Managers.Common.Network.FishnetConnectionErrorHandler),
                    "OnClientConnectionChanged", nameof(ClientConnectionChanged));
                Patch(harmony, typeof(Managers.Common.Network.FishnetConnectionErrorHandler),
                    "OnServerConnectionChanged", nameof(ServerConnectionChanged));
                foreach (var method in new[]
                         {
                             AccessTools.Method(typeof(Managers.Common.MainGameSceneInitializer), "Start"),
                             AccessTools.Method(typeof(Saving.Services.GameLoader), "LoadState"),
                             AccessTools.Method(typeof(Managers.Server.Saving.ServerStateLoader),
                                 "LoadWorldFromStreamingAssetSave")
                         })
                {
                    harmony.Patch(method, finalizer: new HarmonyMethod(typeof(LoadingScreen), nameof(LoadFailed)));
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, string handler)
        {
            var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(LoadingScreen), handler));
        }

        internal static void SceneInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "MainGame")
            {
                if (!Progress.Active) BeginWorld();
            }
            else if (buildIndex == 0)
            {
                Cancel();
            }
        }

        private static bool BeginWorld()
        {
            if (_failed) return false;
            if (Progress.Active && !Progress.FadingOut) return true;
            try
            {
                Cancel();
                _view ??= new LoadingScreenView();
                _view.Show();
                if (MainGameSceneLoadData.Mode != MainGameSceneLoadData.GameMode.JoinGameSteam &&
                    MainGameSceneLoadData.Mode != MainGameSceneLoadData.GameMode.JoinGameIp &&
                    !string.IsNullOrWhiteSpace(MainGameSceneLoadData.SaveFileFullName))
                {
                    _view.SetWorldName(new FileInfo(MainGameSceneLoadData.SaveFileFullName).Directory?.Name);
                }

                Progress.Begin(Time.realtimeSinceStartupAsDouble);
                return true;
            }
            catch (Exception error)
            {
                Fail(error);
                return false;
            }
        }

        private static bool SceneTransition(string __0, LoadSceneMode __1)
        {
            if (__0 != "MainGame" || _bypassTransition || _failed) return true;
            if (_transitionQueued) return false;
            if (!BeginWorld()) return true;
            try
            {
                _transitionQueued = true;
                MelonCoroutines.Start(LoadAfterFade(__0, __1, _generation));
                return false;
            }
            catch (Exception error)
            {
                Fail(error);
                return true;
            }
        }

        private static IEnumerator LoadAfterFade(string scene, LoadSceneMode mode, int generation)
        {
            while (generation == _generation && Progress.Opacity < 1)
                yield return null;
            yield return null;
            if (generation != _generation) yield break;
            try
            {
                _bypassTransition = true;
                SceneManager.LoadScene(scene, mode);
            }
            catch (Exception error)
            {
                Plugin.Report("Game scene loading failed: " + error.GetBaseException().Message);
            }
            finally
            {
                _bypassTransition = false;
                _transitionQueued = false;
            }
        }

        private static void NativeAwake(LoadingScreenDisabler __instance)
        {
            if (__instance.gameObject.scene.name != "MainGame" || !BeginWorld()) return;
            try
            {
                var group = __instance.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = __instance.gameObject.AddComponent<CanvasGroup>();
                    AddedGroups.Add(group);
                }

                if (!Hidden.ContainsKey(group)) Hidden.Add(group, group.alpha);
                group.alpha = 0;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static void PlayerReady(LoadingScreenDisabler __instance)
        {
            if (__instance.gameObject.scene.name == "MainGame") Progress.MarkReady(Time.frameCount);
        }

        private static void ClientConnectionChanged(ClientConnectionStateArgs __0)
        {
            if (Progress.Active && __0.ConnectionState == LocalConnectionState.Stopped)
                Plugin.Report("The connection stopped before the world finished loading.");
        }

        private static void ServerConnectionChanged(ServerConnectionStateArgs __0)
        {
            if (Progress.Active && __0.ConnectionState == LocalConnectionState.Stopped)
                Plugin.Report("The connection stopped before the world finished loading.");
        }

        private static Exception LoadFailed(Exception __exception)
        {
            if (__exception != null)
                Plugin.Report("World loading failed: " + __exception.GetBaseException().Message);
            return __exception;
        }

        internal static void Update()
        {
            if (!Progress.Active) return;
            try
            {
                if (_completeFrame >= 0 && Time.frameCount > _completeFrame)
                {
                    Progress.FadeOut();
                }

                if (!_wallpaperSelected)
                {
                    _wallpaperSelected = true;
                    SelectWallpaper();
                }

                Progress.Update(Time.realtimeSinceStartupAsDouble, Time.unscaledDeltaTime, Time.frameCount);
                _view.ResizeWallpaper();
                _view.SetProgress(Progress.Value);
                _view.SetOpacity(Progress.Opacity);
                if (Progress.FadingOut && Progress.Opacity <= 0)
                {
                    Cancel();
                    return;
                }

                if (Progress.Value >= 1 && Progress.Opacity >= 1 && _completeFrame < 0)
                    _completeFrame = Time.frameCount;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static void SelectWallpaper()
        {
            var candidates = StriderRegistry.Wallpapers.Values
                .Where(entry => !InvalidWallpapers.Contains(entry.Key)).ToList();
            while (candidates.Count > 0)
            {
                var index = Random.Next(candidates.Count);
                var entry = candidates[index];
                candidates.RemoveAt(index);
                try
                {
                    _view.SetWallpaper(entry.Owner.Assets.Texture(entry.Definition.TextureResource));
                    return;
                }
                catch (Exception error)
                {
                    InvalidWallpapers.Add(entry.Key);
                    MelonLogger.Warning("Skipping loading wallpaper " + entry.Key + ": " + error.Message);
                }
            }
        }

        internal static void Cancel()
        {
            _generation++;
            Progress.Cancel();
            _completeFrame = -1;
            _transitionQueued = false;
            _wallpaperSelected = false;
            _view?.Hide();
            foreach (var pair in Hidden)
                if (pair.Key != null)
                    pair.Key.alpha = pair.Value;
            foreach (var group in AddedGroups)
                if (group != null)
                    Object.Destroy(group);
            Hidden.Clear();
            AddedGroups.Clear();
        }

        private static void Fail(Exception error)
        {
            _failed = true;
            Cancel();
            _view?.Dispose();
            _view = null;
            MelonLogger.Warning("SSLib custom loading screen disabled; using vanilla visuals: " + error);
        }

        internal static void Shutdown()
        {
            Cancel();
            _view?.Dispose();
            _view = null;
        }
    }
}
