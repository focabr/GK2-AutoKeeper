<#
.SYNOPSIS
    Builds GK2 AutoKeeper, installs it into the game and/or creates the release zip in Thunderstore format.

.DESCRIPTION
    GamePath is resolved in this order: -GamePath parameter > GK2_GAME_PATH variable > GamePath.props file.
    Deploy only writes to <game>\BepInEx\plugins\AutoKeeper\. No game file is changed.

.EXAMPLE
    .\build.ps1                      # Release build + copy to BepInEx\plugins\AutoKeeper
.EXAMPLE
    .\build.ps1 -Package             # same + creates dist\GK2_AutoKeeper-x.y.z.zip
.EXAMPLE
    .\build.ps1 -NoDeploy -GamePath "D:\SteamLibrary\steamapps\common\Graveyard Keeper 2"

.NOTES
    The optional "Mods" menu bridge (AutoKeeper.FrameworkBridge.dll) is only built if GK2.Framework.dll
    is found (default: <game>\BepInEx\plugins\GK2.Framework.dll, or -FrameworkDll <path>).
#>
[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$NoDeploy,
    [switch]$Package,
    [switch]$UseNuGetRefs,
    [string]$FrameworkDll
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\AutoKeeper\AutoKeeper.csproj'
$bridgeProject = Join-Path $root 'src\AutoKeeper.FrameworkBridge\AutoKeeper.FrameworkBridge.csproj'
$pluginFolderName = 'AutoKeeper'

# ---------------------------------------------------------------- game path
if (-not $GamePath) { $GamePath = $env:GK2_GAME_PATH }
if (-not $GamePath) {
    $propsFile = Join-Path $root 'GamePath.props'
    if (Test-Path $propsFile) {
        [xml]$props = Get-Content $propsFile -Raw
        $GamePath = @($props.Project.PropertyGroup | ForEach-Object { $_.GamePath } | Where-Object { $_ })[0]
    }
}
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath 'GraveyardKeeper2.exe'))) {
    throw "GamePath is invalid or not set ('$GamePath'). Use -GamePath, the GK2_GAME_PATH variable or create GamePath.props (see GamePath.props.example)."
}
$GamePath = (Resolve-Path $GamePath).Path

# ---------------------------------------------------------------- version (single source: Directory.Build.props)
[xml]$dbp = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = @($dbp.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'Version not found in Directory.Build.props.' }

$pluginCs = Get-Content (Join-Path $root 'src\AutoKeeper\Plugin.cs') -Raw
if ($pluginCs -notmatch "Version\s*=\s*`"$([regex]::Escape($version))`"") {
    throw "Plugin.Version in Plugin.cs does not match Directory.Build.props ($version). Update both."
}

Write-Host "== GK2 AutoKeeper $version ($Configuration)" -ForegroundColor Cyan
Write-Host "   Game: $GamePath"

# ---------------------------------------------------------------- build
$buildArgs = @('build', $project, '-c', $Configuration, "-p:GamePath=$GamePath", '-nologo')
if ($UseNuGetRefs) { $buildArgs += '-p:UseNuGetRefs=true' }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit code $LASTEXITCODE)." }

$dll = Join-Path $root "src\AutoKeeper\bin\$Configuration\AutoKeeper.dll"
if (-not (Test-Path $dll)) { throw "DLL not found: $dll" }

# ---------------------------------------------------------------- optional bridge (GK2 Mod Framework Mods menu)
if (-not $FrameworkDll) { $FrameworkDll = Join-Path $GamePath 'BepInEx\plugins\GK2.Framework.dll' }
$bridgeDll = $null
if (Test-Path $FrameworkDll) {
    $bridgeArgs = @('build', $bridgeProject, '-c', $Configuration, "-p:GamePath=$GamePath", "-p:FrameworkDll=$FrameworkDll", '-nologo')
    & dotnet @bridgeArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build of the bridge failed (exit code $LASTEXITCODE)." }
    $bridgeDll = Join-Path $root "src\AutoKeeper.FrameworkBridge\bin\$Configuration\AutoKeeper.FrameworkBridge.dll"
}
else {
    Write-Warning "GK2.Framework.dll not found ($FrameworkDll): Mods menu bridge not built (it is optional)."
}

# ---------------------------------------------------------------- deploy
if (-not $NoDeploy) {
    if (Get-Process -Name 'GraveyardKeeper2' -ErrorAction SilentlyContinue) {
        Write-Warning 'The game is running: the DLL in use cannot be replaced. Close the game and run again.'
    }
    else {
        $dest = Join-Path $GamePath "BepInEx\plugins\$pluginFolderName"
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item $dll -Destination $dest -Force
        if ($bridgeDll) { Copy-Item $bridgeDll -Destination $dest -Force }
        Write-Host "   Installed to: $dest" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------- Thunderstore package
if ($Package) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    # manifest.json with the current version (the Thunderstore package name also names the zip)
    $manifest = Get-Content (Join-Path $root 'thunderstore\manifest.json') -Raw | ConvertFrom-Json
    $manifest.version_number = $version
    $manifestJson = $manifest | ConvertTo-Json -Depth 5

    $zipPath = Join-Path $dist "$($manifest.name)-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # Zip entries ALWAYS use '/', so they work in mod managers (PS 5.1 Compress-Archive uses '\').
    $entries = [ordered]@{
        'icon.png'                              = Join-Path $root 'thunderstore\icon.png'
        'README.md'                             = Join-Path $root 'thunderstore\README.md'
        'CHANGELOG.md'                          = Join-Path $root 'CHANGELOG.md'
        'LICENSE'                               = Join-Path $root 'LICENSE'
        "plugins/$pluginFolderName/AutoKeeper.dll" = $dll
    }
    if ($bridgeDll) { $entries["plugins/$pluginFolderName/AutoKeeper.FrameworkBridge.dll"] = $bridgeDll }

    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $zip.CreateEntry('manifest.json')
        $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
        try { $writer.Write($manifestJson) } finally { $writer.Dispose() }

        foreach ($name in $entries.Keys) {
            $src = $entries[$name]
            if (-not (Test-Path $src)) { throw "Package file not found: $src" }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $src, $name) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host "   Package: $zipPath" -ForegroundColor Green
}
