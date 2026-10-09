<#
.SYNOPSIS
    Builds the PC Health Monitor Windows executable (portable, self-contained, win-x64).

.DESCRIPTION
    1. Restores packages and runs the automated tests (unless -SkipTests).
    2. Publishes src/PCHealthMonitor.Desktop with the "win-x64" publish profile:
       a single self-contained PCHealthMonitor.exe that does not require .NET on the target machine.
    3. Copies it to dist/ as PCHealthMonitor-<version>-win-x64.exe and writes a SHA-256 checksum file.
    Optionally (-IncludeCli) also publishes the command-line tool as pchealth-<version>-win-x64.exe.

    Requirements: Windows 10/11 x64 and the .NET SDK 8 or newer. Internet access is needed the first
    time to download the .NET 8 runtime packs from NuGet.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/publish.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/publish.ps1 -Version 1.1.0 -IncludeCli
#>
[CmdletBinding()]
param(
    # Version stamped into the executable. Defaults to <Version> in Directory.Build.props.
    [string]$Version,
    [switch]$SkipTests,
    [switch]$IncludeCli
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host ""
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Title failed (exit code $LASTEXITCODE)." }
}

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (was '$Version')." }

Write-Host "PC Health Monitor build - version $Version" -ForegroundColor Green
& dotnet --version | ForEach-Object { Write-Host "dotnet SDK $_" }

$artifacts = Join-Path $root 'artifacts'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Invoke-Step 'Restore' { dotnet restore PCHealthMonitor.sln }

if (-not $SkipTests) {
    Invoke-Step 'Tests' { dotnet test PCHealthMonitor.sln --configuration Release --no-restore --nologo }
}

$desktopOut = Join-Path $artifacts "publish\desktop"
Invoke-Step 'Publish desktop app (self-contained single file, win-x64)' {
    dotnet publish src/PCHealthMonitor.Desktop/PCHealthMonitor.Desktop.csproj `
        -p:PublishProfile=win-x64 -p:Version=$Version -p:DebugType=embedded -o $desktopOut --nologo
}

$exeName = "PCHealthMonitor-$Version-win-x64.exe"
$exePath = Join-Path $dist $exeName
Copy-Item (Join-Path $desktopOut 'PCHealthMonitor.exe') $exePath -Force
$outputs = @($exePath)

if ($IncludeCli) {
    $cliOut = Join-Path $artifacts "publish\cli"
    Invoke-Step 'Publish command-line tool (self-contained single file, win-x64)' {
        dotnet publish src/PCHealthMonitor.Cli/PCHealthMonitor.Cli.csproj -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
            -p:DebugType=embedded -p:Version=$Version -o $cliOut --nologo
    }
    $cliPath = Join-Path $dist "pchealth-$Version-win-x64.exe"
    Copy-Item (Join-Path $cliOut 'pchealth.exe') $cliPath -Force
    $outputs += $cliPath
}

$checksums = foreach ($file in $outputs) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $file)"
}
$checksumFile = Join-Path $dist "SHA256SUMS-$Version.txt"
$checksums | Set-Content -Path $checksumFile -Encoding ascii

Write-Host ""
Write-Host "Done." -ForegroundColor Green
foreach ($file in $outputs) {
    $size = (Get-Item $file).Length / 1MB
    Write-Host ("  {0}  ({1:N1} MB)" -f $file, $size)
}
Write-Host "  $checksumFile"
