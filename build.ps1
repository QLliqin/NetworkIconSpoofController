#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Clean,
    [string]$ToolchainDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root "src") -Filter "*.cs" -File | Sort-Object Name | ForEach-Object { $_.FullName })
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "assets\app-icon.ico"
$dist = Join-Path $root "dist"
$output = Join-Path $dist "TimestampConverter.exe"
$nativeLibrary = Join-Path $root "obj\native\NetworkIconNative.dll"

if ($sources.Count -eq 0) {
    throw "No C# source files were found in src."
}
foreach ($required in @($manifest, $icon)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required build input is missing: $required"
    }
}

if ($Clean -and (Test-Path -LiteralPath $output)) {
    # Clean only this build's output; preserve unrelated files and user packages.
    Remove-Item -LiteralPath $output -Force
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null

# Native builds are explicit and local. This script never downloads a compiler.
& (Join-Path $root "tools\build-native.ps1") -ToolchainDirectory $ToolchainDirectory
if (-not (Test-Path -LiteralPath $nativeLibrary)) {
    throw "The native build did not produce $nativeLibrary"
}

$candidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw ".NET Framework 4.x C# compiler was not found."
}

$frameworkDirectory = Split-Path -Parent $csc
$desktopReferences = @("UIAutomationClient.dll", "UIAutomationTypes.dll", "WindowsBase.dll") |
    ForEach-Object { Join-Path $frameworkDirectory "WPF\$_" }
foreach ($reference in $desktopReferences) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Required desktop framework assembly is missing: $reference"
    }
}

$compilerArguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/debug-",
    "/win32manifest:$manifest",
    "/win32icon:$icon",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/resource:$nativeLibrary,TimestampConverter.Native.dll",
    "/out:$output"
)
$compilerArguments += @($desktopReferences | ForEach-Object { "/reference:$_" })
$compilerArguments += $sources

& $csc @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName($output)
$hash = Get-FileHash -LiteralPath $output -Algorithm SHA256

[pscustomobject]@{
    Output = $output
    Version = $assembly.Version.ToString()
    Bytes = (Get-Item -LiteralPath $output).Length
    SHA256 = $hash.Hash
} | Format-List
