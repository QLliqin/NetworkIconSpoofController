#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ToolchainDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "native\NetworkIconNative.cpp"
$exports = Join-Path $root "native\NetworkIconNative.def"
$outputDirectory = Join-Path $root "obj\native"
$output = Join-Path $outputDirectory "NetworkIconNative.dll"
$pinnedPackage = "llvm-mingw-20261020-ucrt-x86_64"

if (-not (Test-Path -LiteralPath $source)) {
    throw "Native source is missing: $source"
}
if (-not (Test-Path -LiteralPath $exports)) {
    throw "Native export definitions are missing: $exports"
}
if ([string]::IsNullOrWhiteSpace($ToolchainDirectory)) {
    $ToolchainDirectory = $env:LLVM_MINGW_ROOT
}
if ([string]::IsNullOrWhiteSpace($ToolchainDirectory)) {
    $ToolchainDirectory = Join-Path $root "obj\toolchain\$pinnedPackage"
}
$ToolchainDirectory = [IO.Path]::GetFullPath($ToolchainDirectory)
$compiler = Join-Path $ToolchainDirectory "bin\x86_64-w64-mingw32-clang++.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    throw @"
LLVM-MinGW x64 compiler was not found: $compiler
Download the pinned $pinnedPackage.zip release from
https://github.com/mstorsjo/llvm-mingw/releases/tag/20261020
and extract it into obj\toolchain, or pass -ToolchainDirectory / set LLVM_MINGW_ROOT.
The build does not download or install tools automatically.
"@
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @(
    "-std=c++17",
    "-O2",
    "-shared",
    "-DUNICODE",
    "-D_UNICODE",
    "-DWINVER=0x0A00",
    "-D_WIN32_WINNT=0x0A00",
    "-static-libgcc",
    "-static-libstdc++",
    "-Wl,--no-insert-timestamp",
    $source,
    $exports,
    "-o", $output,
    "-lole32",
    "-loleaut32",
    "-lruntimeobject",
    "-luser32",
    "-luuid"
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Native compilation failed with exit code $LASTEXITCODE."
}

$bytes = [IO.File]::ReadAllBytes($output)
if ($bytes.Length -lt 64) {
    throw "Native compiler produced an invalid PE image."
}
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ($peOffset -lt 64 -or $peOffset -gt ($bytes.Length - 26) -or
    [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550 -or
    [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664 -or
    [BitConverter]::ToUInt16($bytes, $peOffset + 24) -ne 0x20b -or
    ([BitConverter]::ToUInt16($bytes, $peOffset + 22) -band 0x2000) -eq 0) {
    throw "Native compiler output must be an x64 PE32+ DLL."
}
Write-Host "Native x64 library: $output ($($bytes.Length) bytes)"
