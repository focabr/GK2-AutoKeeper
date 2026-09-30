# lib/

This folder stays **empty in the repository**. The project does not copy or redistribute game or BepInEx DLLs.

The build references come from your installation (via `GamePath`, see `Directory.Build.props`):

| DLL | Source | Why |
|---|---|---|
| `BepInEx.dll`, `0Harmony.dll` | `<game>/BepInEx/core/` | Plugin API, config, logging and Harmony patches |
| `UnityEngine*.dll` (Core, IMGUI, InputLegacy, TextRendering) | `<game>/GraveyardKeeper2_Data/Managed/` | `MonoBehaviour`, `OnGUI`, `KeyboardShortcut` |
| `Assembly-CSharp.dll` | same | Game classes (`MainGame`, `PlayerController`, `WgoData`...) |
| `LazyBearTechnology.dll` | same | Lazy Bear's internal engine (`LazyInput`, `LazyUI`...) |
| `Newtonsoft.Json.dll`, `UniTask.dll` | same | JSON for the discovery dump / types used in game signatures |
| `AstarPathfindingProject.dll`, `Sirenix.Serialization.dll` | same | Types that appear in the signatures used (Seeker, serialized MonoBehaviours) |

All of them use `<Private>false</Private>`: they are only for compiling and **never** go into `bin/` or the release zip.

Alternative for CI: `dotnet build -p:UseNuGetRefs=true` uses the NuGet packages `BepInEx.Core` and
`UnityEngine.Modules` (6000.3.9) for BepInEx/Unity. The game assemblies still come from
the local installation.

If you want to use a local copy in `lib/` (e.g. a machine without the game), point `GamePath` to a folder with the
same structure. `*.dll` files are ignored by `.gitignore`.
