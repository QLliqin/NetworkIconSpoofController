using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("网络图标伪装控制器")]
[assembly: AssemblyDescription("控制 Windows NCSI 网络图标伪装")]
[assembly: AssemblyCompany("Local Tool")]
[assembly: AssemblyProduct("网络图标伪装控制器")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal enum SpoofState
{
    Disabled,
    Enabled,
    Partial
}

internal sealed class RegistryTarget
{
    public readonly string SubKey;
    public readonly string Name;
    public readonly RegistryValueKind Kind;
    public readonly object Value;

    public RegistryTarget(string subKey, string name, RegistryValueKind kind, object value)
    {
        SubKey = subKey;
        Name = name;
        Kind = kind;
        Value = value;
    }
}

internal sealed class RegistryBackup
{
    public string SubKey;
    public string Name;
    public bool Exists;
    public RegistryValueKind Kind;
    public string SerializedValue;
}

internal sealed class ProcessResult
{
    public int ExitCode;
    public string Output;
}

internal static class SystemChanges
{
    private const string NcsiKey = @"SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet";
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\NetworkConnectivityStatusIndicator";
    private const string HostsBegin = "# WiFiSpoofer BEGIN";
    private const string HostsEnd = "# WiFiSpoofer END";
    private const string FirewallPrefix = "WiFiSpoofer_NCSI_Block";
    private const string MismatchContent = "WiFiSpoofer-Do-Not-Match";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly object LogLock = new object();
    private static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkIconSpoofController");
    private static readonly string BackupFile = Path.Combine(DataDirectory, "registry-backup.txt");
    private static readonly string LogFile = Path.Combine(DataDirectory, "controller.log");
    private static readonly string HostsFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    private static readonly RegistryTarget[] SpoofTargets =
    {
        new RegistryTarget(NcsiKey, "EnableActiveProbing", RegistryValueKind.DWord, 1),
        new RegistryTarget(NcsiKey, "NoActiveProbe", RegistryValueKind.DWord, 0),
        new RegistryTarget(NcsiKey, "ActiveWebProbeHost", RegistryValueKind.String, "localhost"),
        new RegistryTarget(NcsiKey, "ActiveWebProbePath", RegistryValueKind.String, "wifi-spoofer-connecttest.txt"),
        new RegistryTarget(NcsiKey, "ActiveWebProbeContent", RegistryValueKind.String, MismatchContent),
        new RegistryTarget(NcsiKey, "ActiveWebProbeHostV6", RegistryValueKind.String, "localhost"),
        new RegistryTarget(NcsiKey, "ActiveWebProbePathV6", RegistryValueKind.String, "wifi-spoofer-connecttest.txt"),
        new RegistryTarget(NcsiKey, "ActiveWebProbeContentV6", RegistryValueKind.String, MismatchContent),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeHost", RegistryValueKind.String, "localhost"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeContent", RegistryValueKind.String, "203.0.113.254"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeHostV6", RegistryValueKind.String, "localhost"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeContentV6", RegistryValueKind.String, "2001:db8::ffff"),
        new RegistryTarget(NcsiKey, "WebTimeout", RegistryValueKind.DWord, 3),
        new RegistryTarget(NcsiKey, "StaleThreshold", RegistryValueKind.DWord, 1),
        new RegistryTarget(NcsiKey, "PassivePollPeriod", RegistryValueKind.DWord, 1),
        new RegistryTarget(NcsiKey, "MinimumInternetHopCount", RegistryValueKind.DWord, 255),
        new RegistryTarget(PolicyKey, "NoActiveProbe", RegistryValueKind.DWord, 0),
        new RegistryTarget(PolicyKey, "DisablePassivePolling", RegistryValueKind.DWord, 1)
    };

    // These values match this machine before ForceNoInternetIcon was applied.
    private static readonly RegistryTarget[] KnownCleanTargets =
    {
        new RegistryTarget(NcsiKey, "EnableActiveProbing", RegistryValueKind.DWord, 1),
        new RegistryTarget(NcsiKey, "NoActiveProbe", RegistryValueKind.DWord, null),
        new RegistryTarget(NcsiKey, "ActiveWebProbeHost", RegistryValueKind.String, "www.msftconnecttest.com"),
        new RegistryTarget(NcsiKey, "ActiveWebProbePath", RegistryValueKind.String, "connecttest.txt"),
        new RegistryTarget(NcsiKey, "ActiveWebProbeContent", RegistryValueKind.String, "Microsoft Connect Test"),
        new RegistryTarget(NcsiKey, "ActiveWebProbeHostV6", RegistryValueKind.String, "ipv6.msftconnecttest.com"),
        new RegistryTarget(NcsiKey, "ActiveWebProbePathV6", RegistryValueKind.String, "connecttest.txt"),
        new RegistryTarget(NcsiKey, "ActiveWebProbeContentV6", RegistryValueKind.String, "Microsoft Connect Test"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeHost", RegistryValueKind.String, "dns.msftncsi.com"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeContent", RegistryValueKind.String, "131.107.255.255"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeHostV6", RegistryValueKind.String, "dns.msftncsi.com"),
        new RegistryTarget(NcsiKey, "ActiveDnsProbeContentV6", RegistryValueKind.String, "fd3e:4f5a:5b81::1"),
        new RegistryTarget(NcsiKey, "WebTimeout", RegistryValueKind.DWord, 35),
        new RegistryTarget(NcsiKey, "StaleThreshold", RegistryValueKind.DWord, 30),
        new RegistryTarget(NcsiKey, "PassivePollPeriod", RegistryValueKind.DWord, 15),
        new RegistryTarget(NcsiKey, "MinimumInternetHopCount", RegistryValueKind.DWord, null),
        new RegistryTarget(PolicyKey, "NoActiveProbe", RegistryValueKind.DWord, null),
        new RegistryTarget(PolicyKey, "DisablePassivePolling", RegistryValueKind.DWord, null)
    };

    public static string LogPath
    {
        get { return LogFile; }
    }

    public static void EnableSpoof()
    {
        AppendLog("ENABLE started");
        EnsureBackupBeforeEnable();

        // Resolve the real probe addresses before the hosts block is installed.
        RemoveHostsBlock();
        List<string> probeAddresses = ResolveProbeAddresses();

        foreach (RegistryTarget target in SpoofTargets)
        {
            WriteRegistryTarget(target);
        }

        RemoveFirewallRules();
        InstallFirewallRules(probeAddresses);
        EnsureHostsBlock();
        RefreshNetworkState();
        AppendLog("ENABLE completed");
    }

    public static void DisableSpoof()
    {
        AppendLog("DISABLE started");
        RestoreBackupOrKnownCleanValues();
        RemoveFirewallRules();
        RemoveHostsBlock();
        RefreshNetworkState();
        AppendLog("DISABLE completed");
    }

    public static void RefreshNetworkState()
    {
        RunProcess("ipconfig.exe", "/flushdns", 15000);
        RunProcess("netsh.exe", "interface ip delete arpcache", 15000);

        string script =
            "Restart-Service NlaSvc -Force -ErrorAction SilentlyContinue; " +
            "Restart-Service netprofm -Force -ErrorAction SilentlyContinue; " +
            "Start-Service NlaSvc -ErrorAction SilentlyContinue; " +
            "Start-Service netprofm -ErrorAction SilentlyContinue";
        RunPowerShell(script, 45000);

        IntPtr ignored;
        NativeMethods.SendMessageTimeout(
            new IntPtr(0xffff),
            0x001A,
            IntPtr.Zero,
            @"SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet",
            0x0002,
            5000,
            out ignored);
    }

    public static SpoofState DetectState()
    {
        int matches = 0;
        int expected = 6;

        if (RegistryStringEquals(NcsiKey, "ActiveWebProbeHost", "localhost")) matches++;
        if (RegistryStringEquals(NcsiKey, "ActiveWebProbeContent", MismatchContent)) matches++;
        if (RegistryStringEquals(NcsiKey, "ActiveDnsProbeHost", "localhost")) matches++;
        if (RegistryDwordEquals(NcsiKey, "MinimumInternetHopCount", 255)) matches++;
        if (RegistryDwordEquals(PolicyKey, "DisablePassivePolling", 1)) matches++;
        if (HostsBlockExists()) matches++;

        if (matches == expected)
        {
            return SpoofState.Enabled;
        }

        if (matches == 0)
        {
            return SpoofState.Disabled;
        }

        return SpoofState.Partial;
    }

    public static string GetConnectivitySummary()
    {
        string script =
            "$items = Get-NetConnectionProfile -ErrorAction SilentlyContinue; " +
            "if (-not $items) { '未检测到活动网络配置' } else { " +
            "$items | ForEach-Object { $_.InterfaceAlias + ': IPv4=' + $_.IPv4Connectivity + ', IPv6=' + $_.IPv6Connectivity } }";
        ProcessResult result = RunPowerShell(script, 20000);
        string value = (result.Output ?? string.Empty).Trim();
        int cliXml = value.IndexOf("#< CLIXML", StringComparison.Ordinal);
        if (cliXml >= 0)
        {
            value = value.Substring(0, cliXml).Trim();
        }
        if (value.Length == 0)
        {
            return "Windows 网络判断：读取失败";
        }

        value = value.Replace("\r\n", "；").Replace("\n", "；");
        return "Windows 网络判断：" + value;
    }

    private static void EnsureBackupBeforeEnable()
    {
        Directory.CreateDirectory(DataDirectory);
        if (File.Exists(BackupFile))
        {
            return;
        }

        var lines = new List<string>();
        if (HasToolFingerprint())
        {
            // A legacy one-shot tool was already active, so its spoof values are not a valid backup.
            lines.Add("MODE|KNOWN_CLEAN");
        }
        else
        {
            lines.Add("MODE|REGISTRY");
            foreach (RegistryTarget target in SpoofTargets)
            {
                lines.Add(SerializeBackup(ReadRegistryBackup(target.SubKey, target.Name)));
            }
        }

        File.WriteAllLines(BackupFile, lines.ToArray(), Utf8NoBom);
        AppendLog("Backup created: " + lines[0]);
    }

    private static void RestoreBackupOrKnownCleanValues()
    {
        bool restoredRegistryBackup = false;

        if (File.Exists(BackupFile))
        {
            string[] lines = File.ReadAllLines(BackupFile, Utf8NoBom);
            if (lines.Length > 0 && lines[0].Equals("MODE|REGISTRY", StringComparison.Ordinal))
            {
                foreach (string line in lines)
                {
                    RegistryBackup backup;
                    if (TryParseBackup(line, out backup))
                    {
                        RestoreRegistryBackup(backup);
                    }
                }
                restoredRegistryBackup = true;
            }
        }

        if (!restoredRegistryBackup)
        {
            foreach (RegistryTarget target in KnownCleanTargets)
            {
                WriteRegistryTarget(target);
            }
        }

        try
        {
            if (File.Exists(BackupFile))
            {
                File.Delete(BackupFile);
            }
        }
        catch (Exception ex)
        {
            AppendLog("Backup cleanup failed: " + ex.Message);
        }
    }

    private static bool HasToolFingerprint()
    {
        return HostsBlockExists()
            || RegistryStringEquals(NcsiKey, "ActiveWebProbeHost", "localhost")
            || RegistryStringEquals(NcsiKey, "ActiveWebProbeContent", MismatchContent)
            || RegistryDwordEquals(PolicyKey, "DisablePassivePolling", 1);
    }

    private static RegistryBackup ReadRegistryBackup(string subKey, string name)
    {
        var backup = new RegistryBackup
        {
            SubKey = subKey,
            Name = name,
            Exists = false,
            Kind = RegistryValueKind.String,
            SerializedValue = string.Empty
        };

        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, false))
        {
            if (key == null)
            {
                return backup;
            }

            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
            {
                return backup;
            }

            backup.Exists = true;
            backup.Kind = key.GetValueKind(name);
            backup.SerializedValue = SerializeRegistryValue(value, backup.Kind);
        }

        return backup;
    }

    private static string SerializeBackup(RegistryBackup backup)
    {
        return "REG|"
            + Encode(backup.SubKey) + "|"
            + Encode(backup.Name) + "|"
            + backup.Exists.ToString(CultureInfo.InvariantCulture) + "|"
            + backup.Kind.ToString() + "|"
            + Encode(backup.SerializedValue ?? string.Empty);
    }

    private static bool TryParseBackup(string line, out RegistryBackup backup)
    {
        backup = null;
        string[] parts = line.Split('|');
        if (parts.Length != 6 || !parts[0].Equals("REG", StringComparison.Ordinal))
        {
            return false;
        }

        RegistryValueKind kind;
        if (!Enum.TryParse(parts[4], out kind))
        {
            return false;
        }

        bool exists;
        if (!bool.TryParse(parts[3], out exists))
        {
            return false;
        }

        backup = new RegistryBackup
        {
            SubKey = Decode(parts[1]),
            Name = Decode(parts[2]),
            Exists = exists,
            Kind = kind,
            SerializedValue = Decode(parts[5])
        };
        return true;
    }

    private static string SerializeRegistryValue(object value, RegistryValueKind kind)
    {
        if (kind == RegistryValueKind.MultiString)
        {
            string[] values = value as string[];
            return values == null ? string.Empty : string.Join("\u001f", values);
        }
        if (kind == RegistryValueKind.Binary)
        {
            byte[] values = value as byte[];
            return values == null ? string.Empty : Convert.ToBase64String(values);
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static object DeserializeRegistryValue(string value, RegistryValueKind kind)
    {
        switch (kind)
        {
            case RegistryValueKind.DWord:
                return int.Parse(value, CultureInfo.InvariantCulture);
            case RegistryValueKind.QWord:
                return long.Parse(value, CultureInfo.InvariantCulture);
            case RegistryValueKind.MultiString:
                return value.Split(new[] { '\u001f' }, StringSplitOptions.None);
            case RegistryValueKind.Binary:
                return Convert.FromBase64String(value);
            default:
                return value;
        }
    }

    private static void RestoreRegistryBackup(RegistryBackup backup)
    {
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(backup.SubKey, true)
               ?? Registry.LocalMachine.CreateSubKey(backup.SubKey))
        {
            if (backup.Exists)
            {
                key.SetValue(
                    backup.Name,
                    DeserializeRegistryValue(backup.SerializedValue, backup.Kind),
                    backup.Kind);
            }
            else
            {
                key.DeleteValue(backup.Name, false);
            }
        }
    }

    private static void WriteRegistryTarget(RegistryTarget target)
    {
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(target.SubKey, true)
               ?? Registry.LocalMachine.CreateSubKey(target.SubKey))
        {
            if (target.Value == null)
            {
                key.DeleteValue(target.Name, false);
            }
            else
            {
                key.SetValue(target.Name, target.Value, target.Kind);
            }
        }
    }

    private static bool RegistryStringEquals(string subKey, string name, string expected)
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, false))
            {
                object value = key == null ? null : key.GetValue(name, null);
                return value != null
                    && string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), expected, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool RegistryDwordEquals(string subKey, string name, int expected)
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, false))
            {
                object value = key == null ? null : key.GetValue(name, null);
                return value != null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == expected;
            }
        }
        catch
        {
            return false;
        }
    }

    private static List<string> ResolveProbeAddresses()
    {
        var addresses = new List<string>();
        string[] hosts =
        {
            "www.msftconnecttest.com",
            "ipv6.msftconnecttest.com",
            "www.msftncsi.com",
            "ipv6.msftncsi.com"
        };

        foreach (string host in hosts)
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(host))
                {
                    if (IPAddress.IsLoopback(address))
                    {
                        continue;
                    }

                    string value = address.ToString();
                    if (!addresses.Contains(value))
                    {
                        addresses.Add(value);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("DNS lookup failed for " + host + ": " + ex.Message);
            }
        }

        AppendLog("Resolved probe addresses: " + addresses.Count.ToString(CultureInfo.InvariantCulture));
        return addresses;
    }

    private static void InstallFirewallRules(IEnumerable<string> addresses)
    {
        int index = 0;
        foreach (string address in addresses)
        {
            string ruleName = FirewallPrefix + "_" + index.ToString(CultureInfo.InvariantCulture);
            string args =
                "advfirewall firewall add rule name=\"" + ruleName +
                "\" dir=out action=block enable=yes profile=any protocol=TCP remoteport=80 remoteip=" + address;
            ProcessResult result = RunProcess("netsh.exe", args, 15000);
            if (result.ExitCode != 0)
            {
                AppendLog("Firewall rule failed: " + ruleName + " " + result.Output);
            }
            index++;
        }
    }

    private static void RemoveFirewallRules()
    {
        string script =
            "Get-NetFirewallRule -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.DisplayName -like 'WiFiSpoofer_NCSI_Block*' -or " +
            "$_.DisplayName -like 'NetworkIconSpoof_NCSI_Block*' } | Remove-NetFirewallRule -ErrorAction SilentlyContinue";
        RunPowerShell(script, 30000);
    }

    private static void EnsureHostsBlock()
    {
        RemoveHostsBlock();
        var lines = new List<string>();
        if (File.Exists(HostsFile))
        {
            lines.AddRange(File.ReadAllLines(HostsFile));
        }

        if (lines.Count > 0 && lines[lines.Count - 1].Length != 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add(HostsBegin);
        lines.Add("127.0.0.1 www.msftconnecttest.com");
        lines.Add("127.0.0.1 ipv6.msftconnecttest.com");
        lines.Add("127.0.0.1 www.msftncsi.com");
        lines.Add("127.0.0.1 ipv6.msftncsi.com");
        lines.Add("127.0.0.1 dns.msftncsi.com");
        lines.Add("::1 www.msftconnecttest.com");
        lines.Add("::1 ipv6.msftconnecttest.com");
        lines.Add("::1 www.msftncsi.com");
        lines.Add("::1 ipv6.msftncsi.com");
        lines.Add("::1 dns.msftncsi.com");
        lines.Add(HostsEnd);
        File.WriteAllLines(HostsFile, lines.ToArray(), Utf8NoBom);
    }

    private static void RemoveHostsBlock()
    {
        if (!File.Exists(HostsFile))
        {
            return;
        }

        string[] lines = File.ReadAllLines(HostsFile);
        var kept = new List<string>();
        bool inside = false;
        foreach (string line in lines)
        {
            if (line.Trim().Equals(HostsBegin, StringComparison.OrdinalIgnoreCase))
            {
                inside = true;
                continue;
            }
            if (line.Trim().Equals(HostsEnd, StringComparison.OrdinalIgnoreCase))
            {
                inside = false;
                continue;
            }
            if (!inside)
            {
                kept.Add(line);
            }
        }

        while (kept.Count > 0 && kept[kept.Count - 1].Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }
        File.WriteAllLines(HostsFile, kept.ToArray(), Utf8NoBom);
    }

    private static bool HostsBlockExists()
    {
        try
        {
            if (!File.Exists(HostsFile))
            {
                return false;
            }
            foreach (string line in File.ReadLines(HostsFile))
            {
                if (line.Trim().Equals(HostsBegin, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static ProcessResult RunPowerShell(string script, int timeoutMs)
    {
        string quietScript = "$ProgressPreference='SilentlyContinue'; " + script;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(quietScript));
        return RunProcess(
            "powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            timeoutMs);
    }

    private static ProcessResult RunProcess(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return new ProcessResult { ExitCode = -1, Output = "Process.Start returned null" };
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return new ProcessResult { ExitCode = -2, Output = output + error + " [timeout]" };
                }

                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = (output + error).Trim()
                };
            }
        }
        catch (Exception ex)
        {
            AppendLog(fileName + " failed: " + ex);
            return new ProcessResult { ExitCode = -1, Output = ex.Message };
        }
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static void AppendLog(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(
                    LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
                    Utf8NoBom);
            }
        }
        catch
        {
        }
    }
}

internal sealed class ControllerForm : Form
{
    private readonly Label stateLabel;
    private readonly Label detailLabel;
    private readonly Label networkLabel;
    private readonly Label operationLabel;
    private readonly Panel stateIndicator;
    private readonly Button enableButton;
    private readonly Button disableButton;
    private readonly Button refreshButton;
    private readonly Button hideButton;
    private readonly NotifyIcon trayIcon;
    private readonly Icon appIcon;
    private bool allowExit;
    private bool busy;
    private bool balloonShown;

    public ControllerForm()
    {
        Text = "网络图标伪装控制器";
        ClientSize = new Size(560, 388);
        MinimumSize = new Size(576, 427);
        MaximumSize = new Size(576, 427);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        BackColor = Color.FromArgb(247, 248, 250);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        appIcon = LoadApplicationIcon();
        Icon = appIcon;

        var header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(560, 72),
            BackColor = Color.FromArgb(42, 45, 49)
        };
        Controls.Add(header);

        var titleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(28, 15),
            Text = "网络图标伪装控制器",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Point)
        };
        header.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(30, 45),
            Text = "控制 Windows 的“无 Internet”地球图标判断",
            ForeColor = Color.FromArgb(205, 210, 215)
        };
        header.Controls.Add(subtitleLabel);

        stateIndicator = new Panel
        {
            Location = new Point(30, 99),
            Size = new Size(16, 16),
            BackColor = Color.Gray
        };
        Controls.Add(stateIndicator);

        stateLabel = new Label
        {
            AutoSize = false,
            Location = new Point(58, 88),
            Size = new Size(470, 34),
            Text = "正在读取当前状态...",
            ForeColor = Color.FromArgb(35, 38, 42),
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point)
        };
        Controls.Add(stateLabel);

        detailLabel = new Label
        {
            AutoSize = false,
            Location = new Point(58, 123),
            Size = new Size(470, 24),
            Text = string.Empty,
            ForeColor = Color.FromArgb(90, 96, 102)
        };
        Controls.Add(detailLabel);

        networkLabel = new Label
        {
            AutoSize = false,
            Location = new Point(30, 158),
            Size = new Size(500, 42),
            Text = "Windows 网络判断：正在读取...",
            ForeColor = Color.FromArgb(70, 75, 80)
        };
        Controls.Add(networkLabel);

        var separator = new Panel
        {
            Location = new Point(30, 207),
            Size = new Size(500, 1),
            BackColor = Color.FromArgb(218, 221, 225)
        };
        Controls.Add(separator);

        enableButton = CreateActionButton(
            "开启伪装",
            new Point(30, 229),
            Color.FromArgb(23, 122, 82),
            Color.White);
        enableButton.Click += delegate { RunEnable(); };
        Controls.Add(enableButton);

        disableButton = CreateActionButton(
            "关闭伪装",
            new Point(205, 229),
            Color.FromArgb(177, 58, 58),
            Color.White);
        disableButton.Click += delegate { RunDisable(false); };
        Controls.Add(disableButton);

        refreshButton = CreateActionButton(
            "刷新状态",
            new Point(380, 229),
            Color.FromArgb(225, 228, 232),
            Color.FromArgb(35, 38, 42));
        refreshButton.Click += delegate { RefreshStatus(); };
        Controls.Add(refreshButton);

        operationLabel = new Label
        {
            AutoSize = false,
            Location = new Point(30, 294),
            Size = new Size(335, 48),
            Text = "关闭窗口时程序会隐藏到托盘；伪装状态只由上方按钮改变。",
            ForeColor = Color.FromArgb(90, 96, 102)
        };
        Controls.Add(operationLabel);

        hideButton = new Button
        {
            Location = new Point(380, 294),
            Size = new Size(150, 38),
            Text = "隐藏到托盘",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(45, 49, 54),
            Cursor = Cursors.Hand
        };
        hideButton.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 201);
        hideButton.Click += delegate { HideToTray(); };
        Controls.Add(hideButton);

        var footer = new Label
        {
            AutoSize = false,
            Location = new Point(30, 354),
            Size = new Size(500, 22),
            Text = "管理员模式  ·  v1.1.0",
            ForeColor = Color.FromArgb(125, 130, 136),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(footer);

        trayIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = "网络图标伪装控制器",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        trayIcon.DoubleClick += delegate { ShowMainWindow(); };

        Resize += delegate
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        };

        Shown += delegate { RefreshStatus(); };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ControllerApp.ShowWindowMessage)
        {
            ShowMainWindow();
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        trayIcon.Visible = false;
        trayIcon.Dispose();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (trayIcon != null)
            {
                trayIcon.Dispose();
            }
            if (appIcon != null)
            {
                appIcon.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private static Button CreateActionButton(string text, Point location, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Location = location,
            Size = new Size(150, 46),
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("网络图标伪装控制器") { Enabled = false };
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());

        var show = new ToolStripMenuItem("打开控制器");
        show.Click += delegate { ShowMainWindow(); };
        menu.Items.Add(show);

        var enable = new ToolStripMenuItem("开启伪装");
        enable.Click += delegate { RunEnable(); };
        menu.Items.Add(enable);

        var disable = new ToolStripMenuItem("关闭伪装");
        disable.Click += delegate { RunDisable(false); };
        menu.Items.Add(disable);

        var refresh = new ToolStripMenuItem("刷新状态");
        refresh.Click += delegate { ShowMainWindow(); RefreshStatus(); };
        menu.Items.Add(refresh);

        menu.Items.Add(new ToolStripSeparator());

        var disableAndExit = new ToolStripMenuItem("关闭伪装并退出");
        disableAndExit.Click += delegate { RunDisable(true); };
        menu.Items.Add(disableAndExit);

        var exit = new ToolStripMenuItem("退出控制器（保留当前状态）");
        exit.Click += delegate { ExitKeepingState(); };
        menu.Items.Add(exit);
        return menu;
    }

    private void RunEnable()
    {
        RunOperation(
            "正在开启伪装，请稍候...",
            SystemChanges.EnableSpoof,
            "伪装已开启。Windows 通常会在 5-30 秒内显示地球图标。",
            false);
    }

    private void RunDisable(bool exitAfter)
    {
        RunOperation(
            "正在关闭伪装，请稍候...",
            SystemChanges.DisableSpoof,
            "伪装已关闭。Windows 通常会在 5-30 秒内恢复正常网络图标。",
            exitAfter);
    }

    private void RunOperation(string progressText, Action action, string successText, bool exitAfter)
    {
        if (busy)
        {
            return;
        }

        ShowMainWindow();
        SetBusy(true, progressText);
        ThreadPool.QueueUserWorkItem(delegate
        {
            Exception error = null;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            try
            {
                BeginInvoke((Action)delegate
                {
                    SetBusy(false, string.Empty);
                    RefreshStatus();

                    if (error != null)
                    {
                        MessageBox.Show(
                            this,
                            "操作失败：\n" + error.Message + "\n\n日志：" + SystemChanges.LogPath,
                            "网络图标伪装控制器",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    if (exitAfter)
                    {
                        allowExit = true;
                        Close();
                        return;
                    }

                    MessageBox.Show(
                        this,
                        successText,
                        "网络图标伪装控制器",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                });
            }
            catch
            {
            }
        });
    }

    private void RefreshStatus()
    {
        SpoofState state = SystemChanges.DetectState();
        if (state == SpoofState.Enabled)
        {
            stateIndicator.BackColor = Color.FromArgb(23, 122, 82);
            stateLabel.Text = "伪装已开启";
            detailLabel.Text = "系统配置完整，当前设置会在控制器退出后继续保留。";
            trayIcon.Text = "网络图标伪装：已开启";
        }
        else if (state == SpoofState.Disabled)
        {
            stateIndicator.BackColor = Color.FromArgb(105, 112, 120);
            stateLabel.Text = "伪装已关闭";
            detailLabel.Text = "Windows 使用正常的联网状态检测。";
            trayIcon.Text = "网络图标伪装：已关闭";
        }
        else
        {
            stateIndicator.BackColor = Color.FromArgb(202, 132, 28);
            stateLabel.Text = "检测到不完整配置";
            detailLabel.Text = "请点击“开启伪装”重新应用，或点击“关闭伪装”完整恢复。";
            trayIcon.Text = "网络图标伪装：配置不完整";
        }

        networkLabel.Text = "Windows 网络判断：正在读取...";
        ThreadPool.QueueUserWorkItem(delegate
        {
            string summary = SystemChanges.GetConnectivitySummary();
            try
            {
                BeginInvoke((Action)delegate { networkLabel.Text = summary; });
            }
            catch
            {
            }
        });
    }

    private void SetBusy(bool value, string text)
    {
        busy = value;
        enableButton.Enabled = !value;
        disableButton.Enabled = !value;
        refreshButton.Enabled = !value;
        hideButton.Enabled = !value;
        UseWaitCursor = value;
        operationLabel.Text = value
            ? text
            : "关闭窗口时程序会隐藏到托盘；伪装状态只由上方按钮改变。";
    }

    private void HideToTray()
    {
        if (busy)
        {
            return;
        }
        Hide();
        ShowInTaskbar = false;
        if (!balloonShown)
        {
            trayIcon.BalloonTipTitle = "网络图标伪装控制器";
            trayIcon.BalloonTipText = "控制器仍在运行。双击托盘图标可重新打开。";
            trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            trayIcon.ShowBalloonTip(4000);
            balloonShown = true;
        }
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        BringToFront();
    }

    private void ExitKeepingState()
    {
        if (busy)
        {
            return;
        }
        allowExit = true;
        Close();
    }

    private static Icon LoadApplicationIcon()
    {
        Icon extracted = null;
        try
        {
            extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted != null)
            {
                return (Icon)extracted.Clone();
            }
        }
        finally
        {
            if (extracted != null)
            {
                extracted.Dispose();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int message,
        IntPtr wParam,
        string lParam,
        int flags,
        int timeout,
        out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern int RegisterWindowMessage(string messageName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

internal static class ControllerApp
{
    private const string MutexName = @"Global\NetworkIconSpoofController_v1";
    private const string ShowMessageName = "NetworkIconSpoofController_ShowWindow_v1";
    private static readonly IntPtr BroadcastWindow = new IntPtr(0xffff);

    public static readonly int ShowWindowMessage = NativeMethods.RegisterWindowMessage(ShowMessageName);

    [STAThread]
    public static void Main()
    {
        if (!IsAdministrator())
        {
            MessageBox.Show(
                "此程序需要管理员权限。请在 UAC 提示中选择“是”。",
                "网络图标伪装控制器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        bool createdNew;
        using (var mutex = new Mutex(true, MutexName, out createdNew))
        {
            if (!createdNew)
            {
                NativeMethods.PostMessage(BroadcastWindow, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ControllerForm());
            GC.KeepAlive(mutex);
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
