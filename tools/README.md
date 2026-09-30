# tools/ (só desenvolvimento — nada daqui vai para o pacote)

## Inspect — ler o código do jogo sem decompilador
Lista tipos/membros e desmonta IL de `Assembly-CSharp.dll` (System.Reflection.Metadata; sem NuGet).

```
dotnet build tools/Inspect -c Release -o tools/Inspect/out
dotnet tools/Inspect/out/inspect.dll <Managed>/Assembly-CSharp.dll "^PlayerWorkComponent$" "FindNearestDockPoint" --il
dotnet tools/Inspect/out/inspect.dll --refs <plugin>.dll GK2.Framework   # referências de uma DLL a outro assembly
```
Argumentos: `<dll> <regex do tipo> [regex do membro] [--il]`.

## FrameworkStub — compilar a ponte do menu Mods sem o GK2 Mod Framework
Stub com as MESMAS assinaturas que a ponte usa (conferidas com `--refs` na ponte 0.3.12). **Nunca distribuir.**

```
dotnet build tools/FrameworkStub -c Release -o tools/FrameworkStub/out "-p:GamePath=<jogo>"
dotnet build src/AutoKeeper.FrameworkBridge -c Release "-p:GamePath=<jogo>" "-p:FrameworkDll=tools/FrameworkStub/out/GK2.Framework.dll"
```
