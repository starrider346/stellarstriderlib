using System;
using System.Collections.Generic;
using System.Linq;
using Core.Services;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Settings;
using Ships.Interface.Values;
using Ships.Parts.Common.Hull;
using Ships.Parts.Common.Model;
using Ships.Parts.Common.Model.Reactions;
using Ships.Services;
using StellarStriderLib.Models;
using UnityEngine;

namespace StellarStriderLib.Showcase
{
    public static class ShowcaseContent
    {
        public const string ModId = "strider.showcase";
        internal const string PanelTexture = "Showcase.panel.png";
        private const string CubeResource = "Showcase.cube.obj";

        public static void RegisterContent(Texture categoryIcon)
        {
            var content = StriderRegistry.Create(ModId, typeof(Plugin).Assembly);
            content.RegisterCategory(new CategoryDefinition
            {
                Id = "parts",
                Name = "Strider Showcase",
                Icon = categoryIcon,
                SortOrder = 100
            });

            RegisterItems(content);
            RegisterConsole(content);
            RegisterWhiteCube(content);
            RegisterLongIronSlope(content);
            RegisterResearch(content);
        }

        private static void RegisterItems(ModRegistry content)
        {
            content.RegisterItem(new ItemDefinition
            {
                Id = "chip",
                Name = "Showcase Chip",
                Mass = .1f,
                MaxStackSize = 100,
                Models = new[]
                {
                    new MeshDefinition
                    {
                        Resource = CubeResource,
                        Scale = new Vector3(.15f, .03f, .1f),

                        Material = new MaterialDefinition { Kind = MaterialKind.Native, NativeName = "LogicGates" }
                    }
                },
                Configure = context =>
                {
                    context.Prefab.name = "Showcase chip inventory model";
                    _ = context.Assets.Mesh(CubeResource);
                }
            });

            content.RegisterFluid(new FluidDefinition
            {
                Id = "coolant", Name = "Showcase Coolant", Mass = 1, Color = Color.cyan
            });
            content.RegisterGas(new FluidDefinition
            {
                Id = "gas", Name = "Showcase Gas", Mass = .01f, Color = Color.magenta
            });
        }

        private static void RegisterConsole(ModRegistry content)
        {
            content.RegisterPart(new PartDefinition
            {
                Id = "console",
                Name = "Showcase Console",
                Description = "A native Constant with a custom body, socket and saved, replicated timer.",
                Donor = "Constant",
                Category = "parts",
                Mass = 2,
                Size = new Vector3(.5f, .5f, .5f),
                PhysicsShapes = new[]
                {
                    new BoxShape { Center = new Vector3(0, .025f, 0), Size = new Vector3(.5f, .05f, .5f) }
                },
                InteractionShapes = new[]
                {
                    new InteractionShapeDefinition
                    {
                        Target = null,
                        Boxes = new[]
                        {
                            new BoxShape { Center = new Vector3(0, .25f, 0), Size = new Vector3(.52f, .52f, .52f) }
                        }
                    }
                },
                Sockets = new[]
                {
                    new SocketDefinition
                    {
                        Id = 0,
                        Type = CableType.Signal,
                        Position = new Vector3(0, .07f, -.375f),
                        Rotation = Vector3.zero
                    }
                },
                BuildingCost = new[]
                {
                    new ItemCost { Item = ItemRef.Native(Items.Model.ItemsConstants.IronItemId), Quantity = 1 }
                },
                Models = null,
                Configure = ConfigureConsole,
                State = ConsoleState.CreateDefinition(),
                Callbacks = ConsoleState.CreateCallbacks()
            });
        }

        private static void ConfigureConsole(PartBuildContext context)
        {
            var visual = context.Prefab.GetComponentInChildren<IShipPartVisual>(true);
            if (visual is not IPartVisualWritable)
            {
                throw new InvalidOperationException("The Constant donor must retain its writable visual component.");
            }

            var root = visual.GameObject;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<Canvas>(true) != null ||
                    renderer.GetComponentInParent<SocketObject>(true) != null)
                {
                    continue;
                }

                var meshFilter = renderer.GetComponent<MeshFilter>();
                UnityEngine.Object.DestroyImmediate(renderer);
                if (meshFilter != null)
                {
                    UnityEngine.Object.DestroyImmediate(meshFilter);
                }
            }

            var models = new[]
            {
                new MeshDefinition
                {
                    Resource = CubeResource,
                    Position = new Vector3(0, .025f, 0),
                    Scale = new Vector3(.5f, .05f, .5f),
                    Material = new MaterialDefinition
                    {
                        Kind = MaterialKind.Solid, Tint = Color.cyan, TextureResource = PanelTexture
                    }
                },
                new MeshDefinition
                {
                    Resource = CubeResource,
                    Position = new Vector3(.16f, .09f, .16f),
                    Rotation = new Vector3(0, 45, 0),
                    Scale = new Vector3(.04f, .04f, .04f),
                    Material = new MaterialDefinition { Kind = MaterialKind.Unlit, Tint = Color.yellow }
                }
            };
            foreach (var mesh in models)
            {
                var child = context.Assets.AddMesh(root.transform, mesh);
                ConfigureMesh(context, child, mesh);
            }
        }

        private static void ConfigureMesh(PartBuildContext context, GameObject child, MeshDefinition mesh)
        {
            child.GetComponent<Renderer>().receiveShadows = false;
            child.name = $"Showcase visual {context.Settings.id}: {mesh.Resource}";
        }

        private static void RegisterWhiteCube(ModRegistry content)
        {
            content.RegisterPart(new PartDefinition
            {
                Id = "white_cube",
                Name = "Showcase White Cube",
                Description = "A free block that demonstrates empty sockets and donor collision bounds.",
                Donor = "Constant",
                AllowMove = true,
                Category = content.Key("parts"),
                Size = Vector3.one,
                Mass = 1,
                BuildingCost = Array.Empty<ItemCost>(),
                Sockets = Array.Empty<SocketDefinition>(),
                PhysicsShapes = null,
                InteractionShapes = null,
                ConfigureMesh = ConfigureMesh,
                Models = new[]
                {
                    new MeshDefinition
                    {
                        Resource = CubeResource,
                        Material = new MaterialDefinition { Kind = MaterialKind.Solid, Tint = Color.white }
                    }
                }
            });
        }

        private static void RegisterLongIronSlope(ModRegistry content)
        {
            const int length = 2;
            var slopeLength = Mathf.Sqrt(length * length + 1f);
            var collision = new BoxShape { Size = new Vector3(1, slopeLength, .1f) };
            var meshes = new List<Mesh>();
            content.WorldUnbinding += () =>
            {
                foreach (var mesh in meshes) UnityEngine.Object.Destroy(mesh);
                meshes.Clear();
            };

            content.RegisterHullPart(new PartDefinition
            {
                Id = "wide_iron_slope",
                Name = "Iron Slope (2×1)",
                Description = "An airtight iron slope that rises one cell over a two-cell horizontal run.",
                Donor = "IronSlope",
                Category = "parts",
                Size = new Vector3(1, length, 1),
                StretchHullSlope = true,
                Mass = HullConstants.MetalMassPerSquareMeter * slopeLength,
                ThermalCapacityKelvin = 2000,
                ThermalConductivity = .25f,
                BuildingCost = new[]
                {
                    new ItemCost { Item = ItemRef.Native(Items.Model.ItemsConstants.IronItemId), Quantity = 2 * length }
                },
                Models = Array.Empty<MeshDefinition>(),
                Configure = context => ConfigureLongIronSlope(context, length, collision, meshes),
                PhysicsShapes = new[] { collision }
            });
        }

        private static void ConfigureLongIronSlope(PartBuildContext context, int length, BoxShape collision, List<Mesh> meshes)
        {
            var donor = Resources.FindObjectsOfTypeAll<HullPartSettings>().First(hull => hull.name == "IronSlope");
            var segments = Resources.FindObjectsOfTypeAll<HullMeshSegmentsSettings>().First();
            var builder = ServiceLocator.GetService<HullMeshBuilder>();
            var anchor = new HullPartPlacement(new Vector3Int(4, 4, 4), VoxelPartType.Interior,
                donor.defaultForward, donor.defaultUp);
            var toLocal = Quaternion.Inverse(anchor.Rotation);
            var up = anchor.Rotation * Vector3.up;
            var run = (Vector3)Vector3Int.RoundToInt(Vector3.ProjectOnPlane(up, Vector3.up).normalized);
            var rise = Vector3.up * Mathf.Sign(up.y);
            var offset = run * ((length - 1) * .5f);
            var size = Vector3.one + (length - 1) * new Vector3(Mathf.Abs(run.x), 0, Mathf.Abs(run.z));
            var tilt = Quaternion.FromToRotation(Vector3.up, toLocal * (length * run + rise));
            collision.Center = toLocal * offset;
            collision.Rotation = tilt.eulerAngles;
            var bounds = context.Prefab.GetComponent<ShipPartBounds>();
            bounds.center = collision.Center;
            var height = tilt * (Vector3.up * collision.Size.y);
            var thickness = tilt * (Vector3.forward * collision.Size.z);
            bounds.bounds = new Vector3(collision.Size.x, Mathf.Abs(height.y) + Mathf.Abs(thickness.y),
                Mathf.Abs(height.z) + Mathf.Abs(thickness.z));

            var model = new GameObject("Iron slope").transform;
            model.SetParent(context.Prefab.transform, false);
            model.SetLocalPositionAndRotation(toLocal * offset, toLocal);
            model.localScale = size;

            foreach (var slice in builder.BuildChunkMesh(Vector3Int.zero, new[] { new HullPart(donor, anchor) },
                         Array.Empty<HullPart>()))
            {
                var mesh = slice.Value.Mesh;
                meshes.Add(mesh);
                var surface = new GameObject(slice.Key.Type.ToString());
                surface.transform.SetParent(model, false);
                surface.transform.localPosition = -anchor.LocalPosition;
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                surface.AddComponent<MeshRenderer>().sharedMaterial = segments.GetSliceMaterial(slice.Key);
            }
        }

        private static void RegisterResearch(ModRegistry content)
        {
            content.RegisterResearchTree(new ResearchTreeDefinition
            {
                Id = "technology",
                Nodes = new[]
                {
                    new ResearchNodeDefinition
                    {
                        Id = "start", Name = "Showcase Basics",
                        UnlockedParts = new[]
                        {
                            PartRef.Mod(content.Key("white_cube")),
                            PartRef.Mod(content.Key("wide_iron_slope"))
                        },
                        AvailableInDemo = true, ExclusiveToDemo = false
                    },
                    new ResearchNodeDefinition
                    {
                        Id = "console_research", Name = "Showcase Console",
                        Prerequisites = new[] { ResearchRef.Mod(content.Key("start")) },
                        Costs = new[]
                        {
                            new ItemCost { Item = ItemRef.Native(Items.Model.ItemsConstants.IronItemId), Quantity = 1 }
                        },
                        UnlockedParts = new[] { PartRef.Mod(content.Key("console")) }
                    },
                    new ResearchNodeDefinition
                    {
                        Id = "chip_research", Name = "Showcase Custom Item Cost",
                        Prerequisites = new[] { ResearchRef.Mod(content.Key("start")) },
                        Costs = new[] { new ItemCost { Item = ItemRef.Mod(content.Key("chip")), Quantity = 1 } }
                    }
                }
            });
        }
    }
}
