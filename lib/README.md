# lib/

Esta pasta fica **vazia no repositório**. O projeto não copia nem redistribui DLLs do jogo ou do BepInEx.

As referências de compilação vêm da sua instalação (via `GamePath`, veja `Directory.Build.props`):

| DLL | Origem | Por quê |
|---|---|---|
| `BepInEx.dll`, `0Harmony.dll` | `<jogo>/BepInEx/core/` | API de plugin, config, log e patches Harmony |
| `UnityEngine*.dll` (Core, IMGUI, InputLegacy, TextRendering) | `<jogo>/GraveyardKeeper2_Data/Managed/` | `MonoBehaviour`, `OnGUI`, `KeyboardShortcut` |
| `Assembly-CSharp.dll` | idem | Classes do jogo (`MainGame`, `PlayerController`, `WgoData`...) |
| `LazyBearTechnology.dll` | idem | Engine interna da Lazy Bear (`LazyInput`, `LazyUI`...) |
| `Newtonsoft.Json.dll`, `UniTask.dll` | idem | JSON do dump de descoberta / tipos usados em assinaturas do jogo |

Todas usam `<Private>false</Private>`: servem só para compilar e **nunca** vão para `bin/` nem para o zip de release.

Alternativa para CI: `dotnet build -p:UseNuGetRefs=true` usa os pacotes `BepInEx.Core` e
`UnityEngine.Modules` (6000.3.9) do NuGet para BepInEx/Unity. Os assemblies do jogo continuam
vindo da instalação local.

Se quiser usar uma cópia local em `lib/` (ex.: máquina sem o jogo), aponte `GamePath` para uma pasta com a
mesma estrutura. Arquivos `*.dll` são ignorados pelo `.gitignore`.
