#Requires -Version 5.1
# Explicit opt-in desktop test: temporarily changes Explorer's network glyph.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$testDirectory = Join-Path $root 'obj\native-tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$output = Join-Path $testDirectory 'NativeNetworkIconTests.exe'
$native = Join-Path $root 'obj\native\NetworkIconNative.dll'
if (-not (Test-Path -LiteralPath $native)) { throw 'Run build.ps1 first.' }
$arguments = @('/nologo', '/target:exe', '/platform:x64', '/main:NativeNetworkIconTests',
    "/out:$output", "/win32manifest:$(Join-Path $root 'src\app.manifest')",
    "/resource:$native,TimestampConverter.Native.dll", '/reference:System.dll', '/reference:System.Core.dll',
    "/reference:$(Join-Path $framework 'WPF\UIAutomationClient.dll')",
    "/reference:$(Join-Path $framework 'WPF\UIAutomationTypes.dll')",
    "/reference:$(Join-Path $framework 'WPF\WindowsBase.dll')",
    (Join-Path $root 'src\NativeNetworkIconController.cs'),
    (Join-Path $root 'src\TimestampConverter.cs'),
    (Join-Path $root 'tests\NativeNetworkIconTests.cs'))
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native integration test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Native integration test failed; check the reported restoration status.' }
