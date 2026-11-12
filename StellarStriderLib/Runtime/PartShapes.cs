using System;
using System.Linq;
using HarmonyLib;
using Ships.Interface.Settings;
using Ships.Parts.Common;
using Ships.Parts.Common.Model;
using Ships.Physics.Collision;
using System.Collections.Generic;
using Ships.Visuals;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StellarStriderLib.Runtime
{
    /// <summary>Validates custom box shapes and applies solid and interaction colliders to donor parts.</summary>
    internal static class PartShapes
    {
        internal static void Validate(PartDefinition partDefinition)
        {
            void Boxes(BoxShape[] boxes)
            {
                if (boxes == null || boxes.Length > 256)
                {
                    throw new ArgumentException("Shape arrays must contain at most 256 boxes.");
                }

                foreach (var box in boxes)
                {
                    if (box == null || !Finite(box.Center) || !Finite(box.Rotation) || !Finite(box.Size) ||
                        box.Size.x <= 0 || box.Size.y <= 0 || box.Size.z <= 0)
                    {
                        throw new ArgumentException("Boxes require finite transforms and positive size.");
                    }
                }
            }

            if (partDefinition.PhysicsShapes != null)
            {
                Boxes(partDefinition.PhysicsShapes);
            }

            if (partDefinition.InteractionShapes == null)
            {
                return;
            }

            if (partDefinition.InteractionShapes.Length > 32 ||
                partDefinition.InteractionShapes.Any(interaction => interaction == null) ||
                partDefinition.InteractionShapes.Select(interaction => interaction.Target ?? "").Distinct().Count() !=
                partDefinition.InteractionShapes.Length)
            {
                throw new ArgumentException("Interaction targets must be unique.");
            }

            foreach (var interaction in partDefinition.InteractionShapes)
            {
                Boxes(interaction.Boxes);
            }
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                                                     !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                                                     !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        internal static void Configure(PartDefinition partDefinition, GameObject prefab)
        {
            if (partDefinition.PhysicsShapes != null && prefab.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(c => c is Ships.Parts.Common.Model.Reactions.IShipPartDynamicCollider))
            {
                throw new InvalidOperationException(
                    "Static PhysicsShapes cannot override a dynamic donor collider. Choose a donor with static physics.");
            }

            foreach (var definition in partDefinition.InteractionShapes ?? Array.Empty<InteractionShapeDefinition>())
            {
                var matches = prefab.GetComponentsInChildren<PartInteraction>(true).Where(interaction =>
                    definition.Target == null || interaction.name == definition.Target).ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException("Interaction target must resolve exactly once: " +
                                                        (definition.Target ?? "<sole interaction>"));
                }

                var target = matches[0];
                foreach (var c in target.GetComponents<Collider>())
                {
                    Object.DestroyImmediate(c);
                }

                foreach (var shape in definition.Boxes)
                {
                    var child = new GameObject("Strider entry trigger")
                        { layer = target.gameObject.layer, tag = target.tag };
                    child.transform.SetParent(target.transform, false);
                    child.transform.position = prefab.transform.TransformPoint(shape.Center);
                    child.transform.rotation = prefab.transform.rotation * Quaternion.Euler(shape.Rotation);
                    var action = child.AddComponent<PartInteraction>();
                    foreach (var name in new[] { "isActive", "detectionDistance", "interactions" })
                    {
                        AccessTools.Field(typeof(PartInteraction), name).SetValue(action,
                            AccessTools.Field(typeof(PartInteraction), name).GetValue(target));
                    }

                    var box = child.AddComponent<BoxCollider>();
                    box.size = shape.Size;
                    box.isTrigger = true;
                }
            }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ShipStatefulColliders), "AddStatefulPart"),
                postfix: new HarmonyMethod(typeof(PartShapes), nameof(Identifiers)));
            harmony.Patch(AccessTools.Method(typeof(ShipPartInstancer), "InstantiateBoundsCollider"),
                prefix: new HarmonyMethod(typeof(PartShapes), nameof(Physics)));
        }

        private static void Identifiers(Ships.Interface.Model.Parts.StatefulPart __0,
            Dictionary<ushort, GameObject> ____statefulParts)
        {
            if (!PartRuntime.TryDefinition(__0.Settings.id, out _, out var definition) ||
                definition.PhysicsShapes == null || !____statefulParts.TryGetValue(__0.Id, out var root))
            {
                return;
            }

            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                var id = box.GetComponent<ShipPartIdentifier>() ?? box.gameObject.AddComponent<ShipPartIdentifier>();
                id.Id = __0.Id;
            }
        }

        private static bool Physics(PartSettings __0, Transform __1, ref Transform __result,
            PhysicsMaterial ___hullPhysicsMaterial)
        {
            if (!PartRuntime.TryDefinition(__0.id, out _, out var partDefinition) ||
                partDefinition.PhysicsShapes == null)
            {
                return true;
            }

            var root = new GameObject("Strider physics");
            root.transform.SetParent(__1, false);
            root.layer = LayerMask.NameToLayer("Ship");
            root.tag = "ShipFloatingPart";
            foreach (var shape in partDefinition.PhysicsShapes)
            {
                var child = new GameObject("Solid box") { layer = root.layer, tag = root.tag };
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = shape.Center;
                child.transform.localRotation = Quaternion.Euler(shape.Rotation);
                var box = child.AddComponent<BoxCollider>();
                box.size = shape.Size;
                box.sharedMaterial = ___hullPhysicsMaterial;
            }

            __result = root.transform;
            return false;
        }
    }
}
