#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Executable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $PSScriptRoot "dist\NetworkIconSpoofController.exe"
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
if ($assemblyName.Version -ne [Version]"1.1.0.0") {
    throw "Unexpected assembly version: $($assemblyName.Version)"
}

$bytes = [IO.File]::ReadAllBytes($Executable)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
if (-not $ascii.Contains("requireAdministrator")) {
    throw "The embedded requireAdministrator manifest was not found."
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

$assembly = [Reflection.Assembly]::LoadFile($Executable)
$systemChanges = $assembly.GetType("SystemChanges", $true)
$detectedState = $systemChanges.GetMethod("DetectState").Invoke($null, @()).ToString()
$connectivity = $systemChanges.GetMethod("GetConnectivitySummary").Invoke($null, @()).ToString()

[pscustomobject]@{
    Executable = $Executable
    Version = $assemblyName.Version.ToString()
    RequiresAdministrator = $true
    IconMatchesSource = $true
    IconSizes = ($sizes -join ", ")
    DetectedState = $detectedState
    Connectivity = $connectivity
    SHA256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
} | Format-List
