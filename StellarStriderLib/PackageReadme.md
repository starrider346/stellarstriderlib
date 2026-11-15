# SSLib (StellarStriderLib)

A MelonLoader content-registration library for **StellarDrive**, targeting
**netstandard2.1**.

SSLib provides build-menu categories, donor-based parts, items, fluids, gases,
research trees, embedded OBJ models and textures, loading wallpapers, lifecycle
callbacks, and versioned part state with saving and multiplayer replication.

## Add it to your mod

```sh
dotnet add package StellarStriderLib
```

Target `netstandard2.1` in your consumer project. Reference the compatible
MelonLoader, Unity, and StellarDrive assemblies from your game installation
separately. These assemblies are not bundled or automatically referenced by this
package.

Declare the library dependency in your mod:

```csharp
using MelonLoader;

[assembly: MelonAdditionalDependencies("StellarStriderLib")]
```

Register content from `OnInitializeMelon` on Unity's main thread, before the first
world opens. Keep mod IDs and content keys stable across releases. Resolve
world-specific settings and numeric IDs after the registry's `WorldBound` event.