#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Executable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $PSScriptRoot "dist\TimestampConverter.exe"
}
$Executable = [IO.Path]::GetFullPath($Executable)
$iconPath = Join-Path $PSScriptRoot "assets\app-icon.ico"

if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Executable not found: $Executable"
}
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "Icon not found: $iconPath"
}

$assemblyName = [Reflection.AssemblyName]::GetAssemblyName($Executable)
if ($assemblyName.Version -ne [Version]"3.0.0.0") {
    throw "Unexpected assembly version: $($assemblyName.Version)"
}

$bytes = [IO.File]::ReadAllBytes($Executable)
function Assert-Pe64([byte[]]$Image, [string]$Label, [bool]$RequireDll = $false) {
    if ($Image.Length -lt 64 -or $Image[0] -ne 0x4d -or $Image[1] -ne 0x5a) {
        throw "$Label is not a valid PE image."
    }
    $peOffset = [BitConverter]::ToInt32($Image, 0x3c)
    if ($peOffset -lt 64 -or $peOffset -gt ($Image.Length - 26) -or
        [BitConverter]::ToUInt32($Image, $peOffset) -ne 0x4550 -or
        [BitConverter]::ToUInt16($Image, $peOffset + 4) -ne 0x8664 -or
        [BitConverter]::ToUInt16($Image, $peOffset + 24) -ne 0x20b) {
        throw "$Label must be an x64 PE32+ image."
    }
    if ($RequireDll -and (([BitConverter]::ToUInt16($Image, $peOffset + 22) -band 0x2000) -eq 0)) {
        throw "$Label must have the PE DLL characteristic."
    }
}
Assert-Pe64 $bytes "TimestampConverter.exe"

# Reading assembly resources does not run the entry point or static constructors,
# load the native DLL, or initialize any Explorer/XAML connection. LoadFile also
# works in PowerShell 7, where ReflectionOnlyLoadFrom is not supported.
$assembly = [Reflection.Assembly]::LoadFile($Executable)
$nativeResourceName = "TimestampConverter.Native.dll"
if ($assembly.GetManifestResourceNames() -notcontains $nativeResourceName) {
    throw "The embedded native library resource is missing: $nativeResourceName"
}
$nativeStream = $assembly.GetManifestResourceStream($nativeResourceName)
if ($null -eq $nativeStream) {
    throw "The embedded native library resource could not be read."
}
try {
    $nativeMemory = New-Object IO.MemoryStream
    try {
        $nativeStream.CopyTo($nativeMemory)
        $nativeBytes = $nativeMemory.ToArray()
    }
    finally {
        $nativeMemory.Dispose()
    }
}
finally {
    $nativeStream.Dispose()
}
Assert-Pe64 $nativeBytes $nativeResourceName $true

$ascii = [Text.Encoding]::ASCII.GetString($bytes)
if (-not $ascii.Contains('level="asInvoker"') -or $ascii.Contains('level="requireAdministrator"')) {
    throw "The executable must embed an asInvoker manifest without administrator elevation."
}
if (-not $ascii.Contains("dpiAware") -or -not $ascii.Contains("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a")) {
    throw "DPI awareness or the Windows 10/11 compatibility declaration is missing."
}

$stream = [IO.File]::OpenRead($iconPath)
try {
    $reader = New-Object IO.BinaryReader($stream)
    $reserved = $reader.ReadUInt16()
    $type = $reader.ReadUInt16()
    $count = $reader.ReadUInt16()
    if ($reserved -ne 0 -or $type -ne 1 -or $count -lt 9) {
        throw "The ICO directory is invalid or incomplete."
    }

    $sizes = New-Object Collections.Generic.List[string]
    $source32Offset = 0
    $source32Length = 0
    for ($index = 0; $index -lt $count; $index++) {
        $width = [int]$reader.ReadByte()
        $height = [int]$reader.ReadByte()
        [void]$reader.ReadByte()
        [void]$reader.ReadByte()
        [void]$reader.ReadUInt16()
        [void]$reader.ReadUInt16()
        $imageLength = [int]$reader.ReadUInt32()
        $imageOffset = [int]$reader.ReadUInt32()
        if ($width -eq 0) { $width = 256 }
        if ($height -eq 0) { $height = 256 }
        $sizes.Add(("{0}x{1}" -f $width, $height))
        if ($width -eq 32 -and $height -eq 32) {
            $source32Offset = $imageOffset
            $source32Length = $imageLength
        }
    }
}
finally {
    $stream.Dispose()
}

if ($source32Length -le 0) {
    throw "The source ICO does not contain a 32x32 frame."
}

Add-Type -AssemblyName System.Drawing
$embeddedIcon = [Drawing.Icon]::ExtractAssociatedIcon($Executable)
if ($null -eq $embeddedIcon) {
    throw "No icon could be extracted from the executable."
}

function Get-BitmapHash([Drawing.Bitmap]$Bitmap) {
    $pixels = New-Object byte[] ($Bitmap.Width * $Bitmap.Height * 4)
    $offset = 0
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $color = $Bitmap.GetPixel($x, $y)
            $pixels[$offset] = $color.A
            $pixels[$offset + 1] = $color.R
            $pixels[$offset + 2] = $color.G
            $pixels[$offset + 3] = $color.B
            $offset += 4
        }
    }

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha256.ComputeHash($pixels))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}

$icoBytes = [IO.File]::ReadAllBytes($iconPath)
$source32Bytes = New-Object byte[] $source32Length
[Array]::Copy($icoBytes, $source32Offset, $source32Bytes, 0, $source32Length)
$source32Stream = New-Object IO.MemoryStream(, $source32Bytes)
$source32Bitmap = [Drawing.Bitmap]::FromStream($source32Stream)
$embeddedBitmap = $embeddedIcon.ToBitmap()
try {
    $embeddedIconHash = Get-BitmapHash $embeddedBitmap
    $expectedIconHash = Get-BitmapHash $source32Bitmap
}
finally {
    $embeddedBitmap.Dispose()
    $source32Bitmap.Dispose()
    $source32Stream.Dispose()
    $embeddedIcon.Dispose()
}
if ($embeddedIconHash -ne $expectedIconHash) {
    throw "The executable icon does not match assets/app-icon.ico."
}

# Exercise the pure conversion logic. Never launch the GUI or apply a region here;
# verification also runs on CI hosts without an interactive Explorer taskbar.
$candidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw ".NET Framework 4.x C# compiler was not found."
}
$testDirectory = Join-Path $PSScriptRoot "obj\verification"
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testExecutable = Join-Path $testDirectory "TimestampConverterTests.exe"
$testArguments = @(
    "/nologo",
    "/target:exe",
    "/optimize+",
    "/main:TimestampConverterTests",
    "/out:$testExecutable",
    (Join-Path $PSScriptRoot "src\TimestampConverter.cs"),
    (Join-Path $PSScriptRoot "tests\TimestampConverterTests.cs")
)
& $csc @testArguments
if ($LASTEXITCODE -ne 0) {
    throw "Timestamp conversion test compilation failed with exit code $LASTEXITCODE."
}
& $testExecutable
if ($LASTEXITCODE -ne 0) {
    throw "Timestamp conversion tests failed with exit code $LASTEXITCODE."
}

[pscustomobject]@{
    Executable = $Executable
    Version = $assemblyName.Version.ToString()
    Architecture = "x64"
    RequiresAdministrator = $false
    EmbeddedNativeLibrary = $nativeResourceName
    EmbeddedNativeLibraryIsPe64 = $true
    IconMatchesSource = $true
    IconSizes = ($sizes -join ", ")
    ConversionTestsPassed = $true
    SHA256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
} | Format-List
