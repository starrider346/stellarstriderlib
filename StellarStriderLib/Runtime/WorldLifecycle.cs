using System;
using System.IO;
using StellarStriderLib.Core;
using HarmonyLib;
using Saving.Interface.Model;
using Saving.Services;

namespace StellarStriderLib.Runtime
{
    /// <summary>Binds registries and part state during native world loading and saves active ID mappings.</summary>
    internal static class WorldLifecycle
    {
        private static int _templateDepth;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(GameLoader), "LoadState"),
                prefix: new HarmonyMethod(typeof(WorldLifecycle), nameof(Load)));
            harmony.Patch(AccessTools.Method(typeof(Saving.Services.IO.SaveFileWriter), "WriteGameFile"),
                prefix: new HarmonyMethod(typeof(WorldLifecycle), nameof(Save)));
            var template = AccessTools.Method(typeof(Managers.Server.Saving.ServerStateLoader),
                "LoadWorldFromStreamingAssetSave");
            harmony.Patch(template, prefix: new HarmonyMethod(typeof(WorldLifecycle), nameof(Template)),
                finalizer: new HarmonyMethod(typeof(WorldLifecycle), nameof(TemplateFinished)));
        }

        private static void Template()
        {
            _templateDepth++;
        }

        private static void TemplateFinished()
        {
            _templateDepth--;
        }

        private static void Load(ISavedGameFile __0)
        {
            try
            {
                Plugin.ClearError();
                RegistryNetwork.WorldChanging();
                var directory = __0?.SaveFileInfo?.Directory?.FullName ??
                                throw new InvalidDataException("Save location is unavailable.");
                var path = Path.Combine(directory, WorldRegistry.FileName);
                var document = _templateDepth == 0 && File.Exists(path)
                    ? WorldRegistry.Parse(File.ReadAllText(path))
                    : new WorldRegistry();
                ContentSession.Prepare(document);
                PartStateSave.Load(directory, _templateDepth != 0);
                if (_templateDepth == 0)
                {
                    document.Write(directory);
                }
            }
            catch (Exception error)
            {
                Plugin.Report(error.Message);
                throw;
            }
        }

        private static void Save(string __1)
        {
            try
            {
                if (!ContentSession.Ready)
                {
                    throw new InvalidOperationException("Cannot save: StellarStriderLib registry is not ready.");
                }

                Directory.CreateDirectory(__1);
                ContentSession.Registry.Write(__1);
            }
            catch (Exception error)
            {
                Plugin.Report("Cannot save registry: " + error.Message, false);
                throw;
            }
        }
    }
}
