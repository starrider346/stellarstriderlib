using System;
using System.Reflection;
using HarmonyLib;
using Ships.Interface.Model.Placement;
using Ships.Interface.Settings;
using UnityEngine;

namespace StellarStriderLib.Runtime
{
    /// <summary>Aligns fractional-size registered parts with the native socket lattice during precise placement.</summary>
    internal static class SocketGrid
    {
        [ThreadStatic] private static bool _placingSocketPart;
        private static FieldInfo _settingsField;
        private static FieldInfo _sizeField;
        private static FieldInfo _positionField;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var tools = Assembly.Load("Tools");
            var state = tools.GetType("Tools.Build.Model.SnapDetectionState", true);
            var properties = tools.GetType("Tools.Build.Model.HitSnapDetectionProperties", true);
            _settingsField = RequiredField(state, "PartSettings");
            _sizeField = RequiredField(properties, "Size");
            _positionField = RequiredField(properties, "Pos");
            var preview = AccessTools.Method(tools.GetType("Tools.Build.PartPreviewDetection", true),
                "CalculateSnapDetectionUpdate");
            var snap = AccessTools.Method(
                tools.GetType("Tools.Build.SnapDetectors.Common.PreciseSnappingDetection", true),
                "SnapInsideCoord", new[] { properties, typeof(PartOrientation), typeof(PartOrientation) });
            if (preview == null || snap == null || snap.ReturnType != typeof(Vector3))
            {
                throw new MissingMethodException("The precise socket placement hooks are unavailable.");
            }

            harmony.Patch(preview, prefix: new HarmonyMethod(typeof(SocketGrid), nameof(BeginPlacement)),
                finalizer: new HarmonyMethod(typeof(SocketGrid), nameof(EndPlacement)));
            harmony.Patch(snap, prefix: new HarmonyMethod(typeof(SocketGrid), nameof(Snap)));
        }

        private static FieldInfo RequiredField(Type type, string name) =>
            AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);

        private static void BeginPlacement(object __0, out bool __state)
        {
            __state = _placingSocketPart;
            var settings = (PartSettings)_settingsField.GetValue(__0);
            _placingSocketPart = ContentSession.Ready && settings != null &&
                                 PartRuntime.TryDefinition(settings.id, out _, out var definition) &&
                                 definition.Size.HasValue && definition.Sockets != null &&
                                 definition.Sockets.Length > 0;
        }

        private static void EndPlacement(bool __state) => _placingSocketPart = __state;

        private static bool Snap(object __0, PartOrientation __1, PartOrientation __2, ref Vector3 __result)
        {
            if (!_placingSocketPart)
            {
                return true;
            }

            __result = SnapPosition((Vector3)_positionField.GetValue(__0),
                (Vector3)_sizeField.GetValue(__0), __1, __2);
            return false;
        }

        internal static float Phase(float size) => size < 1 ? Mathf.Repeat(size * 2, 1) : 0;

        internal static Vector3 SnapPosition(Vector3 position, Vector3 size,
            PartOrientation upOrientation, PartOrientation forwardOrientation)
        {
            var up = PartOrientationHelper.GetVector3(upOrientation);
            var forward = PartOrientationHelper.GetVector3(forwardOrientation);
            var basis = Quaternion.LookRotation(
                PartOrientationHelper.GetVector3(PartOrientationHelper.GetPerpendicular(upOrientation)), up);
            var inverse = Quaternion.Inverse(basis);
            var local = inverse * position;
            var phase = inverse * Quaternion.LookRotation(forward, up) *
                        new Vector3(Phase(size.x), Phase(size.y), Phase(size.z));

            var snapped = new Vector3(SnapAxis(local.x, phase.x), 0, SnapAxis(local.z, phase.z));
            snapped.y = PartOrientationHelper.IsSquare(upOrientation)
                ? Mathf.Round(local.y)
                : Mathf.Round(local.y * Mathf.Sqrt(2)) / Mathf.Sqrt(2) + .5f;
            return basis * snapped - up * .46f;
        }

        private static float SnapAxis(float position, float phase) =>
            (Mathf.Round(position * 4 + phase) - phase) / 4;
    }
}
