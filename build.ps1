#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root "src\Program.cs"
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "assets\app-icon.ico"
$dist = Join-Path $root "dist"
$output = Join-Path $dist "NetworkIconSpoofController.exe"

foreach ($required in @($source, $manifest, $icon)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required build input is missing: $required"
    }
}

if ($Clean -and (Test-Path -LiteralPath $dist)) {
    Get-ChildItem -LiteralPath $dist -File |
        Where-Object { $_.Name -ne ".gitkeep" } |
        Remove-Item -Force
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$candidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw ".NET Framework 4.x C# compiler was not found."
}

$compilerArguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/debug-",
    "/win32manifest:$manifest",
    "/win32icon:$icon",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/out:$output",
    $source
)

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
