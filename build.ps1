<#
.SYNOPSIS
    Compila o GK2 AutoKeeper, instala no jogo e/ou gera o zip de release no formato Thunderstore.

.DESCRIPTION
    GamePath é resolvido nesta ordem: parâmetro -GamePath > variável GK2_GAME_PATH > arquivo GamePath.props.
    O deploy só escreve em <jogo>\BepInEx\plugins\AutoKeeper\. Nenhum arquivo do jogo é alterado.

.EXAMPLE
    .\build.ps1                      # build Release + copia para BepInEx\plugins\AutoKeeper
.EXAMPLE
    .\build.ps1 -Package             # idem + gera dist\GK2_AutoKeeper-x.y.z.zip
.EXAMPLE
    .\build.ps1 -NoDeploy -GamePath "D:\SteamLibrary\steamapps\common\Graveyard Keeper 2"

.NOTES
    A ponte opcional do menu "Mods" (AutoKeeper.FrameworkBridge.dll) só é compilada se o GK2.Framework.dll
    for encontrado (padrão: <jogo>\BepInEx\plugins\GK2.Framework.dll, ou -FrameworkDll caminho).
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

# ---------------------------------------------------------------- caminho do jogo
if (-not $GamePath) { $GamePath = $env:GK2_GAME_PATH }
if (-not $GamePath) {
    $propsFile = Join-Path $root 'GamePath.props'
    if (Test-Path $propsFile) {
        [xml]$props = Get-Content $propsFile -Raw
        $GamePath = @($props.Project.PropertyGroup | ForEach-Object { $_.GamePath } | Where-Object { $_ })[0]
    }
}
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath 'GraveyardKeeper2.exe'))) {
    throw "GamePath inválido ou não definido ('$GamePath'). Use -GamePath, a variável GK2_GAME_PATH ou crie GamePath.props (veja GamePath.props.example)."
}
$GamePath = (Resolve-Path $GamePath).Path

# ---------------------------------------------------------------- versão (fonte única: Directory.Build.props)
[xml]$dbp = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = @($dbp.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'Version não encontrada em Directory.Build.props.' }

$pluginCs = Get-Content (Join-Path $root 'src\AutoKeeper\Plugin.cs') -Raw
if ($pluginCs -notmatch "Version\s*=\s*`"$([regex]::Escape($version))`"") {
    throw "Plugin.Version em Plugin.cs não bate com Directory.Build.props ($version). Atualize os dois."
}

Write-Host "== GK2 AutoKeeper $version ($Configuration)" -ForegroundColor Cyan
Write-Host "   Jogo: $GamePath"

# ---------------------------------------------------------------- build
$buildArgs = @('build', $project, '-c', $Configuration, "-p:GamePath=$GamePath", '-nologo')
if ($UseNuGetRefs) { $buildArgs += '-p:UseNuGetRefs=true' }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou (código $LASTEXITCODE)." }

$dll = Join-Path $root "src\AutoKeeper\bin\$Configuration\AutoKeeper.dll"
if (-not (Test-Path $dll)) { throw "DLL não encontrada: $dll" }

# ---------------------------------------------------------------- ponte opcional (menu Mods do GK2 Mod Framework)
if (-not $FrameworkDll) { $FrameworkDll = Join-Path $GamePath 'BepInEx\plugins\GK2.Framework.dll' }
$bridgeDll = $null
if (Test-Path $FrameworkDll) {
    $bridgeArgs = @('build', $bridgeProject, '-c', $Configuration, "-p:GamePath=$GamePath", "-p:FrameworkDll=$FrameworkDll", '-nologo')
    & dotnet @bridgeArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build da ponte falhou (código $LASTEXITCODE)." }
    $bridgeDll = Join-Path $root "src\AutoKeeper.FrameworkBridge\bin\$Configuration\AutoKeeper.FrameworkBridge.dll"
}
else {
    Write-Warning "GK2.Framework.dll não encontrado ($FrameworkDll): ponte do menu Mods não compilada (é opcional)."
}

# ---------------------------------------------------------------- deploy
if (-not $NoDeploy) {
    if (Get-Process -Name 'GraveyardKeeper2' -ErrorAction SilentlyContinue) {
        Write-Warning 'O jogo está aberto: a DLL em uso não pode ser substituída. Feche o jogo e rode de novo.'
    }
    else {
        $dest = Join-Path $GamePath "BepInEx\plugins\$pluginFolderName"
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item $dll -Destination $dest -Force
        if ($bridgeDll) { Copy-Item $bridgeDll -Destination $dest -Force }
        Write-Host "   Instalado em: $dest" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------- pacote Thunderstore
if ($Package) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    # manifest.json com a versão atual (o nome do pacote no Thunderstore também dá nome ao zip)
    $manifest = Get-Content (Join-Path $root 'thunderstore\manifest.json') -Raw | ConvertFrom-Json
    $manifest.version_number = $version
    $manifestJson = $manifest | ConvertTo-Json -Depth 5

    $zipPath = Join-Path $dist "$($manifest.name)-$version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    # Entradas do zip SEMPRE com '/', para funcionar em mod managers (Compress-Archive do PS 5.1 usa '\').
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
            if (-not (Test-Path $src)) { throw "Arquivo do pacote não encontrado: $src" }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $src, $name) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host "   Pacote: $zipPath" -ForegroundColor Green
}
