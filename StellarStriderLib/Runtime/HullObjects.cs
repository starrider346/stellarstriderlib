using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Ships.Parts.Common.Utils;
using Ships.Physics.Collision;
using Ships.Visuals;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    /// <summary>Owns the custom objects attached to a native ship visual root or collider chunk.</summary>
    internal sealed class HullObjects : MonoBehaviour
    {
        private static readonly HashSet<HullObjects> Owners = new();
        private readonly Dictionary<HullPartPosition, GameObject> _parts = new();

        internal static void Install(HarmonyLib.Harmony harmony, Assembly tools)
        {
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipHullVisuals), nameof(ShipHullVisuals.AddHullPart)),
                nameof(AddVisual));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipHullVisuals), nameof(ShipHullVisuals.UpdateHullPart)),
                nameof(UpdateVisual));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipHullVisuals), nameof(ShipHullVisuals.RemoveHullPart)),
                nameof(RemoveVisual));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipHullColliderChunk), nameof(ShipHullColliderChunk.AddHullPart)),
                nameof(AddCollision));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipHullColliderChunk), nameof(ShipHullColliderChunk.RemoveHullPart)),
                nameof(RemoveCollision));
            var hover = tools.GetType("Tools.Common.Hover.HullHover", true);
            var constructor = AccessTools.DeclaredConstructor(hover, new[] { typeof(RaycastHit), typeof(Vector3) }) ??
                              throw new MissingMethodException(hover.FullName, ".ctor");
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(HullObjects), nameof(AnchorHover)));
        }

        private static void Patch(HarmonyLib.Harmony harmony, MethodBase method, string prefix)
        {
            if (method == null) throw new MissingMethodException("Hull object hook unavailable: " + prefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(HullObjects), prefix));
        }

        internal static void ConfigureBounds(ShipPartBounds target, Bounds fallback, BoxShape[] boxes)
        {
            Bounds? bounds = null;
            void Include(Matrix4x4 transform, Bounds shape)
            {
                var half = shape.extents;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var point = transform.MultiplyPoint3x4(shape.center + new Vector3(x * half.x, y * half.y, z * half.z));
                    var combined = bounds ?? new Bounds(point, Vector3.zero);
                    combined.Encapsulate(point);
                    bounds = combined;
                }
            }

            var inverse = target.transform.worldToLocalMatrix;
            foreach (var filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                    Include(inverse * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
            }

            foreach (var box in boxes ?? Array.Empty<BoxShape>())
                Include(Matrix4x4.TRS(box.Center, Quaternion.Euler(box.Rotation), Vector3.one), new Bounds(Vector3.zero, box.Size));

            var result = bounds ?? fallback;
            target.center = result.center;
            target.bounds = result.size;
        }

        private static HullObjects For(Component owner)
        {
            var objects = owner.GetComponent<HullObjects>() ?? owner.gameObject.AddComponent<HullObjects>();
            Owners.Add(objects);
            return objects;
        }

        private GameObject Track(HullPart part, GameObject instance)
        {
            var placement = part.HullPlacement;
            Remove(placement.Position);
            instance.transform.SetLocalPositionAndRotation(placement.LocalPosition, placement.Rotation);
            _parts.Add(placement.Position, instance);
            return instance;
        }

        internal static GameObject Visual(TrackedShipClient ship, HullPartPosition position)
        {
            var objects = ship.VisualObject.GetComponent<HullObjects>();
            return objects != null && objects._parts.TryGetValue(position, out var visual) ? visual : null;
        }

        private static bool AddVisual(ShipHullVisuals __instance, HullPart __0)
        {
            var settings = __0.HullSettings;
            if (!HullParts.HasCustomVisual(settings)) return true;

            For(__instance).Track(__0, Instantiate(settings.part, __instance.transform, false));
            return false;
        }

        private static bool RemoveVisual(ShipHullVisuals __instance, HullPart __0)
        {
            if (!HullParts.HasCustomVisual(__0.HullSettings)) return true;

            __instance.GetComponent<HullObjects>()?.Remove(__0.HullPlacement.Position);
            return false;
        }

        private static bool UpdateVisual(ShipHullVisuals __instance, HullPartUpdateEvent __0)
        {
            var part = __0.HullPart;
            if (!HullParts.HasCustomVisual(part.HullSettings)) return true;

            var objects = __instance.GetComponent<HullObjects>();
            var placement = part.HullPlacement;
            if (objects != null && objects._parts.TryGetValue(placement.Position, out var visual))
                visual.transform.SetLocalPositionAndRotation(placement.LocalPosition, placement.Rotation);
            return false;
        }

        private static bool AddCollision(ShipHullColliderChunk __instance, HullPart __0, BoxCollider ___colliderObjectPrefab)
        {
            var boxes = HullParts.CollisionBoxes(__0.HullSettings);
            if (boxes == null) return true;
            if (boxes.Length == 0) return false;

            var root = new GameObject("Registered hull colliders");
            root.transform.SetParent(__instance.transform, false);
            var placement = __0.HullPlacement;
            var anchor = root.AddComponent<HullColliderAnchor>();
            anchor.Placement = placement;
            anchor.Settings = __0.HullSettings;
            var tag = PartColliderUtils.GetPartColliderTag(placement.Position.Type, placement.ForwardOrientation);
            var layer = LayerMask.NameToLayer("Ship");
            For(__instance).Track(__0, root);
            foreach (var shape in boxes)
            {
                var collider = Instantiate(___colliderObjectPrefab, root.transform, false);
                collider.transform.SetLocalPositionAndRotation(shape.Center, Quaternion.Euler(shape.Rotation));
                collider.transform.localScale = Vector3.one;
                collider.center = Vector3.zero;
                collider.size = shape.Size;
                collider.gameObject.layer = layer;
                collider.tag = tag;
            }

            return false;
        }

        private static bool RemoveCollision(ShipHullColliderChunk __instance, HullPart __0)
        {
            if (HullParts.CollisionBoxes(__0.HullSettings) == null) return true;

            __instance.GetComponent<HullObjects>()?.Remove(__0.HullPlacement.Position);
            return false;
        }

        private static void AnchorHover(RaycastHit __0, Vector3 __1, ref HullPartPosition ____hullPartPosition,
            IShipPartTargeter ____currentShip, ref bool ____isFront)
        {
            var collider = __0.collider;
            if (collider == null) return;
            var anchor = collider.GetComponentInParent<HullColliderAnchor>();
            if (anchor == null) return;

            ____hullPartPosition = anchor.Placement.Position;
            ____isFront = Vector3.Dot(____currentShip.ConvertDirectionToLocalSpace(__1),
                PartOrientationHelper.GetVector3(anchor.Placement.ForwardOrientation)) < 0;
        }

        private void Remove(HullPartPosition position)
        {
            if (_parts.Remove(position, out var instance)) Release(instance);
        }

        private static void Release(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            Destroy(instance);
        }

        private void OnDestroy() => Owners.Remove(this);

        internal static void Reset()
        {
            foreach (var owner in Owners)
            {
                foreach (var instance in owner._parts.Values) Release(instance);
                owner._parts.Clear();
            }

            Owners.Clear();
        }
    }

    internal sealed class HullColliderAnchor : MonoBehaviour
    {
        internal HullPartPlacement Placement;
        internal HullPartSettings Settings;
    }
}
