# SSLib (StellarStriderLib)

A MelonLoader content-registration library for **StellarDrive**, targeting
**netstandard2.1**.

SSLib provides build-menu categories, donor-based parts, items, fluids, gases,
research trees, embedded OBJ models and textures, loading wallpapers and weighted
mod loading steps, lifecycle
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

## Loading steps

Register a `LoadingStepDefinition` through `mod.RegisterLoadingStep(...)` during
initialization. Supply a mod-local `Id`, display `Name`, positive `Volume`, numeric
`Order`, and `Start` callback. Native loading has volume 100 and order 1; mod
defaults are volume 100 and order 10. Lower orders display first; all callbacks
start on the main thread at the beginning of each load with fresh
`LoadingStepContext` handles.

Report absolute progress with `step.Report(0..100)` and call `step.Release()` when
finished. Progress contributes immediately even while an earlier step's label is
displayed. Reporting 100 does not release the step. The overlay remains until
native loading and every mod step release. Once the local player is initialized,
SSLib pauses gameplay and physics through fade-out; networking and visual loading
continue. Use unscaled time or frame yields for loading coroutines. Original clock
rates are restored on completion, cancellation, or failure. Progress and release
calls support worker threads; Unity work must stay on the main thread. Use `step.CancellationToken` to stop abandoned work. Late
updates from previous loads are ignored. See the repository README for a coroutine
example and the complete registration contract.
