#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $quotedPath = '"' + $PSCommandPath + '"'
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File $quotedPath -Elevated"
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait
    exit
}

$ncsi = "HKLM:\SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet"
$policy = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator"
$hostsPath = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
$hostsBegin = "# WiFiSpoofer BEGIN"
$hostsEnd = "# WiFiSpoofer END"

$defaults = [ordered]@{
    EnableActiveProbing = 1
    ActiveWebProbeHost = "www.msftconnecttest.com"
    ActiveWebProbePath = "connecttest.txt"
    ActiveWebProbeContent = "Microsoft Connect Test"
    ActiveWebProbeHostV6 = "ipv6.msftconnecttest.com"
    ActiveWebProbePathV6 = "connecttest.txt"
    ActiveWebProbeContentV6 = "Microsoft Connect Test"
    ActiveDnsProbeHost = "dns.msftncsi.com"
    ActiveDnsProbeContent = "131.107.255.255"
    ActiveDnsProbeHostV6 = "dns.msftncsi.com"
    ActiveDnsProbeContentV6 = "fd3e:4f5a:5b81::1"
    WebTimeout = 35
    StaleThreshold = 30
    PassivePollPeriod = 15
}

foreach ($entry in $defaults.GetEnumerator()) {
    $kind = if ($entry.Value -is [int]) { "DWord" } else { "String" }
    New-ItemProperty -Path $ncsi -Name $entry.Key -Value $entry.Value -PropertyType $kind -Force | Out-Null
}

foreach ($name in @("NoActiveProbe", "MinimumInternetHopCount")) {
    Remove-ItemProperty -Path $ncsi -Name $name -ErrorAction SilentlyContinue
}
foreach ($name in @("NoActiveProbe", "DisablePassivePolling", "MaxActiveProbes")) {
    Remove-ItemProperty -Path $policy -Name $name -ErrorAction SilentlyContinue
}

Get-NetFirewallRule -ErrorAction SilentlyContinue |
    Where-Object {
        $_.DisplayName -like "WiFiSpoofer_NCSI_Block*" -or
        $_.DisplayName -like "NetworkIconSpoof_NCSI_Block*"
    } |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $hostsPath) {
    $kept = New-Object Collections.Generic.List[string]
    $inside = $false
    foreach ($line in [IO.File]::ReadAllLines($hostsPath)) {
        if ($line.Trim().Equals($hostsBegin, [StringComparison]::OrdinalIgnoreCase)) {
            $inside = $true
            continue
        }
        if ($line.Trim().Equals($hostsEnd, [StringComparison]::OrdinalIgnoreCase)) {
            $inside = $false
            continue
        }
        if (-not $inside) {
            $kept.Add($line)
        }
    }
    [IO.File]::WriteAllLines($hostsPath, $kept, (New-Object Text.UTF8Encoding($false)))
}

& ipconfig.exe /flushdns | Out-Null
& netsh.exe interface ip delete arpcache | Out-Null
Restart-Service NlaSvc -Force -ErrorAction SilentlyContinue
Restart-Service netprofm -Force -ErrorAction SilentlyContinue
Start-Service NlaSvc -ErrorAction SilentlyContinue
Start-Service netprofm -ErrorAction SilentlyContinue

Write-Host "Network icon spoof settings have been restored." -ForegroundColor Green
Get-NetConnectionProfile -ErrorAction SilentlyContinue |
    Select-Object InterfaceAlias, IPv4Connectivity, IPv6Connectivity |
    Format-Table -AutoSize
