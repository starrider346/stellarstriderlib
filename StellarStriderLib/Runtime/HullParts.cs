using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Items.Model;
using Research.Interface.Values;
using Ships.Grid;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Ships.Interface.Values;
using Ships.Rooms;
using StellarStriderLib.Core;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    internal static class HullParts
    {
        private static readonly HashSet<string> Registered = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Entry<PartDefinition>> Pivoted = new(StringComparer.Ordinal);
        private static readonly Dictionary<HullPartSettings, (PartDefinition Definition, List<ItemDrop> Cost)>
            Profiles = new();

        internal static readonly Dictionary<string, HullPartSettings> Donors = new(StringComparer.Ordinal);
        internal static IEnumerable<Entry<PartDefinition>> Variants => Pivoted.Values;

        // Native availability checks receive a size without the selected part's settings.
        [ThreadStatic] private static PartDefinition _placingPart;
        private static FieldInfo _previewSettings;
        private static FieldInfo _previewRotation;
        private static FieldInfo _snapHit;
        private static FieldInfo _buildSettingsId;

        internal static void Validate(string key, PartDefinition definition)
        {
            if (definition.State != null || definition.Callbacks != null || definition.Sockets != null ||
                definition.InteractionShapes != null || definition.AllowMove)
            {
                throw new ArgumentException($"Hull {key} cannot define state, runtime callbacks, sockets, " +
                                            "interaction shapes, or pickup movement.");
            }

            if (definition.StretchHullSlope && (definition.Size is not { } slopeSize || slopeSize.x != 1 ||
                                               slopeSize.y < 2 || slopeSize.z != 1))
                throw new ArgumentException($"Stretched hull slope {key} requires a one-cell width and a run of at least two cells.");
            if (definition.Size is not { } size) return;
            if (!HullFootprint.IsValid(size))
                throw new ArgumentException($"Hull {key} size must use positive whole cell counts.");
            var hasModel = definition.Models != null && (definition.Models.Length > 0 || definition.Configure != null);
            if (size != Vector3.one && (!hasModel || definition.PhysicsShapes is not { Length: > 0 }))
            {
                throw new ArgumentException($"Multi-cell hull {key} requires models and collision boxes.");
            }
        }

        internal static void Register(string key) => Registered.Add(key);

        internal static void ResolveDonors(IEnumerable<PartSettings> native)
        {
            foreach (var key in Registered.OrderBy(key => key, StringComparer.Ordinal))
            {
                var entry = StriderRegistry.Parts[key];
                var name = entry.Definition.Donor;
                var donor = HullRegistration.RequireDonor(key, name, native.FirstOrDefault(part => part.name == name));
                if (entry.Definition.StretchHullSlope && donor.hullMeshSegmentType != HullMeshSegmentType.Slope)
                    throw new InvalidOperationException($"Stretched hull slope {key} requires a slope donor: {name}");
                Donors.Add(key, donor);
                if (donor.pivotedSettings == null) continue;

                var variant = HullRegistration.RequireDonor(key, donor.pivotedSettings.name, donor.pivotedSettings);
                var variantKey = key + ".pivoted";
                if (StriderRegistry.Parts.ContainsKey(variantKey))
                    throw new InvalidOperationException($"Hull {key} reserves the pivoted part key {variantKey}.");

                Donors.Add(variantKey, variant);
                Pivoted.Add(key, new Entry<PartDefinition>
                {
                    Owner = entry.Owner, Key = variantKey, Definition = entry.Definition
                });
            }
        }

        internal static void Bind(HullPartSettings settings, PartDefinition definition, List<ItemDrop> cost) =>
            Profiles.Add(settings, (definition, cost));

        internal static void LinkVariants()
        {
            foreach (var pair in Pivoted)
                ((HullPartSettings)Settings(pair.Key)).pivotedSettings = Settings(pair.Value.Key);
        }

        internal static void InheritVariantUnlocks(IEnumerable<ResearchSettings> nodes)
        {
            foreach (var pair in Pivoted)
            {
                var part = Settings(pair.Key);
                var variant = Settings(pair.Value.Key);
                foreach (var node in nodes)
                {
                    var unlocks = node.unlockedParts;
                    if (unlocks != null && unlocks.Contains(part) && !unlocks.Contains(variant))
                        node.unlockedParts = unlocks.Append(variant).ToArray();
                }
            }
        }

        private static PartSettings Settings(string key) =>
            ContentSession.PartSettings[ContentSession.Registry.Resolve(ContentKind.Part, key)];

        internal static BoxShape[] CollisionBoxes(HullPartSettings settings) =>
            Profiles.TryGetValue(settings, out var profile) ? profile.Definition.PhysicsShapes : null;

        internal static bool HasCustomVisual(HullPartSettings settings) =>
            settings.hasItsOwnMesh && Profiles.ContainsKey(settings);

        internal static void Reset()
        {
            HullObjects.Reset();
            Profiles.Clear();
            Donors.Clear();
            Pivoted.Clear();
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var tools = Assembly.Load("Tools");
            var preview = tools.GetType("Tools.Build.PartPreviewDetection", true);
            var state = tools.GetType("Tools.Build.Model.SnapDetectionState", true);
            _previewSettings = AccessTools.Field(state, "PartSettings") ??
                               throw new MissingFieldException(state.FullName, "PartSettings");
            _previewRotation = AccessTools.Field(state, "PartRotation");
            _snapHit = AccessTools.Field(tools.GetType("Tools.Build.Model.HitSnapDetectionProperties", true), "Hit");

            var assembly = Assembly.Load("Assembly-CSharp");
            var interaction = assembly.GetType("Managers.Server.Services.ShipInteractionService", true);
            var request = Assembly.Load("Managers.Interface").GetType("Managers.Interface.Model.PlaceHullPartNetworkRequest", true);
            _buildSettingsId = AccessTools.Field(request, "SettingsId") ??
                               throw new MissingFieldException(request.FullName, "SettingsId");
            var place = AccessTools.GetDeclaredMethods(interaction).Single(method =>
                method.Name.StartsWith("RpcLogic___PlaceHullPart_ServerRPC___", StringComparison.Ordinal) &&
                method.GetParameters().FirstOrDefault()?.ParameterType == request);

            Patch(harmony, AccessTools.DeclaredMethod(preview, "CalculateSnapDetectionUpdate"),
                nameof(BeginPreview), nameof(EndPlacement));
            harmony.Patch(AccessTools.DeclaredMethod(preview, "TryFindSnapInRayHit"),
                postfix: new HarmonyMethod(typeof(HullParts), nameof(FloorOnStretchedSlope)));
            var onFloor = tools.GetType("Tools.Build.SnapDetectors.Types.SlopeOnFloorSnapDetector", true);
            harmony.Patch(AccessTools.DeclaredMethod(onFloor, "DetectSnapPlacement"),
                postfix: new HarmonyMethod(typeof(HullParts), nameof(StretchedSlopeOnFloor)));
            Patch(harmony, place, nameof(BeginPlacement), nameof(EndPlacement));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipGrid), "IsVariableSizeHullAvailable"), nameof(Available));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(HullPart), nameof(HullPart.GetLargePartCoords)), nameof(Coordinates));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipRooms), "GetBlockedOutChunkVoxels"), nameof(BlockedVoxels));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(ShipRooms), "GetRoomScanVoxels"), nameof(ScanVoxels));
            Patch(harmony, AccessTools.PropertyGetter(typeof(HullPartSettings), nameof(HullPartSettings.BuildingCost)), nameof(Cost));
            Patch(harmony, AccessTools.PropertyGetter(typeof(HullPartSettings), nameof(HullPartSettings.Mass)), nameof(Mass));
            HullObjects.Install(harmony, tools);
        }

        private static void Patch(HarmonyLib.Harmony harmony, MethodBase method, string prefix, string finalizer = null)
        {
            if (method == null) throw new MissingMethodException("Hull registration hook unavailable: " + prefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(HullParts), prefix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(HullParts), finalizer));
        }

        private static PartDefinition Definition(PartSettings settings) => settings is HullPartSettings hull &&
            Profiles.TryGetValue(hull, out var profile) ? profile.Definition : null;

        private static void BeginPreview(object __0, out PartDefinition __state)
        {
            __state = _placingPart;
            _placingPart = Definition((PartSettings)_previewSettings.GetValue(__0));
        }

        private static void BeginPlacement(object __0, out PartDefinition __state)
        {
            __state = _placingPart;
            var id = (ushort)_buildSettingsId.GetValue(__0);
            _placingPart = ContentSession.PartSettings.TryGetValue(id, out var settings) ? Definition(settings) : null;
        }

        private static void EndPlacement(PartDefinition __state) => _placingPart = __state;

        private static void StretchedSlopeOnFloor(object __0, ref IPartPlacement __result)
        {
            if (_placingPart is not { StretchHullSlope: true, Size: { } size } ||
                __result is not HullPartPlacement slope) return;

            var hit = (PartHit)_snapHit.GetValue(__0);
            var rise = Mathf.Sign((slope.Rotation * Vector3.up).y);
            if ((hit.Pos.y - slope.LocalPosition.y) * rise <= .25f) return;

            var anchor = slope.Coord - HullFootprint.SlopeRun(slope.Rotation) * ((int)size.y - 1);
            __result = new HullPartPlacement(anchor, slope.Position.Type, slope.ForwardOrientation, slope.UpOrientation);
        }

        private static void FloorOnStretchedSlope(object __0, RaycastHit __1, ref IPartPlacement __3, ref bool __result)
        {
            if (((PartSettings)_previewSettings.GetValue(__0))?.snappingStyle != SnappingStyle.Floor) return;
            var collider = __1.collider;
            if (collider == null) return;
            var anchor = collider.GetComponentInParent<HullColliderAnchor>();
            if (anchor == null || Definition(anchor.Settings) is not { StretchHullSlope: true, Size: { } size }) return;

            var ship = collider.GetComponentInParent<IShipPartPlacements>();
            if (ship == null) return;
            var rotation = ship.ToLocalRotation((Quaternion)_previewRotation.GetValue(__0));
            var forward = PartOrientationHelper.SnapVerticalOrientation(rotation * Vector3.forward);
            var up = PartOrientationHelper.SnapFloorOrientation(rotation * Vector3.up);
            if (forward is not { } floorForward || up is not { } floorUp) return;

            var slope = anchor.Placement;
            var run = (int)size.y;
            var along = HullFootprint.SlopeRun(slope.Rotation);
            var rise = (slope.Rotation * Vector3.up).y >= 0;
            var hit = ship.ToLocalPos(__1.point) - slope.LocalPosition;
            var farEnd = Vector3.Dot(hit, (Vector3)along) >= (run - 1) * .5f;
            var coord = slope.Coord + (farEnd ? along * run : -along);
            if (farEnd == rise) coord += Vector3Int.up;
            var position = new HullPartPosition(coord, VoxelPartType.Floor);
            __result = ship.IsHullPartAvailable(position);
            __3 = __result ? new HullPartPlacement(position, floorForward, floorUp) : null;
        }

        private static bool Available(ShipGrid __instance, HullPartPlacement __0, Vector3 __1, ref bool __result)
        {
            if (_placingPart?.Size is not { } size || size != __1) return true;

            var type = __0.Position.Type;
            __result = Footprint(__0, size, _placingPart.StretchHullSlope)
                .All(cell => __instance.IsHullAvailable(new HullPartPosition(cell, type)));
            return false;
        }

        private static bool Coordinates(ref HullPart __instance, ref IReadOnlyList<Vector3Int> __result)
        {
            var definition = Definition(__instance.HullSettings);
            if (definition?.Size is not { } size) return true;

            // The native grid and room scanner both consume the placed hull's cell list.
            var placement = __instance.HullPlacement;
            __result = Footprint(placement, size, definition.StretchHullSlope);
            return false;
        }

        private static IReadOnlyList<Vector3Int> Footprint(HullPartPlacement placement, Vector3 size, bool stretch) =>
            stretch ? HullFootprint.SlopeCoordinates(placement.Coord, placement.Rotation, (int)size.y) :
                HullFootprint.Coordinates(placement.Coord, placement.Rotation, size);

        private static bool BlockedVoxels(HullPart __0, ref Vector3Int[] __result) =>
            RoomVoxels(__0, false, ref __result);

        private static bool ScanVoxels(HullPart __0, ref Vector3Int[] __result) =>
            RoomVoxels(__0, true, ref __result);

        private static bool RoomVoxels(HullPart part, bool scan, ref Vector3Int[] result)
        {
            if (Definition(part.HullSettings) is not { StretchHullSlope: true, Size: { } size }) return true;
            var placement = part.HullPlacement;
            result = HullFootprint.SlopeRoomVoxels(placement.Coord, placement.Rotation, (int)size.y,
                ShipRoomsConstants.RoomVoxelResolution, scan);
            return false;
        }

        private static bool Cost(HullPartSettings __instance, ref List<ItemDrop> __result)
        {
            if (!Profiles.TryGetValue(__instance, out var profile)) return true;
            __result = new List<ItemDrop>(profile.Cost);
            return false;
        }

        private static bool Mass(HullPartSettings __instance, ref float __result)
        {
            if (!Profiles.ContainsKey(__instance)) return true;
            __result = __instance.mass;
            return false;
        }
    }
}
