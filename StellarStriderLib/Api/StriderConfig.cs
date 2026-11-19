using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib
{
    /// <summary>Opens local mod settings during MelonLoader initialization.</summary>
    public static class StriderConfig
    {
        private static readonly Dictionary<string, (Assembly Assembly, ModConfig Config)> Mods =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loads a mod's config file once and returns its in-memory store.</summary>
        public static ModConfig Create(string modId, Assembly assembly)
        {
            Plugin.CheckThread();
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            ConfigFile.CheckModId(modId);
            if (Mods.TryGetValue(modId, out var existing))
            {
                if (existing.Assembly == assembly && existing.Config.ModId == modId) return existing.Config;
                throw new ArgumentException("Config directory already registered: " + modId, nameof(modId));
            }

            var path = Path.Combine(Application.persistentDataPath, "config", modId, "config.json");
            var legacyPath = Path.Combine(MelonEnvironment.UserDataDirectory, "config", modId, "config.json");
            var config = new ModConfig(modId, path, message => MelonLogger.Warning(message), legacyPath);
            Mods.Add(modId, (assembly, config));
            return config;
        }

        internal static IEnumerable<ModOptionsCategory> Categories =>
            Mods.Values.SelectMany(mod => mod.Config.Categories).OrderBy(category => category.Order);

        internal static void SaveDefaults()
        {
            foreach (var mod in Mods.Values)
            {
                mod.Config.SaveDefaults();
            }
        }
    }
}
