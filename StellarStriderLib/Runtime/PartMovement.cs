using System;
using System.Collections.Generic;
using System.Linq;
using Core.Services;
using FishNet.Connection;
using HarmonyLib;
using Managers.Interface.Model;
using Managers.Server.Services;
using Players.Interface.Model;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Parts.Common;
using Ships.Services;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal static class PartMovement
    {
        private static readonly System.Reflection.FieldInfo InteractionsField =
            AccessTools.Field(typeof(PartInteraction), "interactions");

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var client = AccessTools.Method(typeof(PartInteraction), "PickUpPart");
            var server = AccessTools.GetDeclaredMethods(typeof(ShipInteractionService)).SingleOrDefault(method =>
                method.Name.StartsWith("RpcLogic___PickUpPart_ServerRPC___", StringComparison.Ordinal) &&
                method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                    new[] { typeof(PartInteractNetworkRequest), typeof(NetworkConnection) }));
            if (client == null || server == null || InteractionsField == null)
            {
                throw new MissingMethodException("The native part pickup hooks are unavailable.");
            }

            harmony.Patch(client, prefix: new HarmonyMethod(typeof(PartMovement), nameof(ClientPickup)));
            harmony.Patch(server, prefix: new HarmonyMethod(typeof(PartMovement), nameof(ServerPickup)));
        }

        internal static void Configure(PartDefinition definition, GameObject prefab)
        {
            if (definition.AllowMove)
            {
                return;
            }

            foreach (var interaction in prefab.GetComponentsInChildren<PartInteraction>(true))
            {
                if (InteractionsField.GetValue(interaction) is List<PartInteractionConfig> actions)
                {
                    InteractionsField.SetValue(interaction, FilterInteractions(actions, false));
                }
            }
        }

        internal static List<PartInteractionConfig> FilterInteractions(IEnumerable<PartInteractionConfig> actions,
            bool allowMove) => actions.Where(action => allowMove || action.type != InteractionType.PickUp).ToList();

        internal static bool CanMove(ushort settingsId) =>
            !PartRuntime.TryDefinition(settingsId, out _, out var definition) || definition.AllowMove;

        private static bool ClientPickup(PartContext ___PartContext)
        {
            var ships = ServiceLocator.GetService<ShipsClientTracker>();
            return ships == null || !ships.TryGetTrackedShip(___PartContext.ShipId, out var ship) ||
                   CanMove(ship, ___PartContext.PartId);
        }

        private static bool ServerPickup(PartInteractNetworkRequest __0, ShipsServerTracker ____ships) =>
            CanMove(____ships?.GetShip(__0.ShipId), __0.PartId);

        private static bool CanMove(TrackedShip ship, ushort partId) =>
            ship == null || !ship.TryGetStatefulPart(partId, out var part) || CanMove(part.Settings.id);
    }
}
