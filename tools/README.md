# tools/ (development only — nothing here goes into the package)

## Inspect — read the game code without a decompiler
Lists types/members and disassembles IL from `Assembly-CSharp.dll` (System.Reflection.Metadata; no NuGet).

```
dotnet build tools/Inspect -c Release -o tools/Inspect/out
dotnet tools/Inspect/out/Inspect.dll <Managed>/Assembly-CSharp.dll "^PlayerWorkComponent$" "FindNearestDockPoint" --il
dotnet tools/Inspect/out/Inspect.dll --refs <plugin>.dll GK2.Framework   # a DLL's references to another assembly
```
Arguments: `<dll> <type regex> [member regex] [--il]`.

## FrameworkStub — build the Mods menu bridge without GK2 Mod Framework
Stub with the SAME signatures the bridge uses (checked with `--refs` on the 0.3.12 bridge). **Never distribute it.**

```
dotnet build tools/FrameworkStub -c Release -o tools/FrameworkStub/out "-p:GamePath=<game>"
dotnet build src/AutoKeeper.FrameworkBridge -c Release "-p:GamePath=<game>" "-p:FrameworkDll=tools/FrameworkStub/out/GK2.Framework.dll"
```
