using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// The native component changes Explorer's own XAML TextBlock. This class owns
// its per-run command channel; it never creates a drawing/overlay window.
internal sealed class NativeNetworkIconController : IDisposable
{
    private const int MapSize = 384;
    private readonly object sync = new object();
    private MemoryMappedFile mapping;
    private MemoryMappedViewAccessor view;
    private string mappingName;
    private Timer heartbeatTimer;
    private IntPtr nativeModule;
    private AttachDelegate attach;
    private uint explorerPid;
    private int sequence;
    private bool disposed;
    private string lastError = string.Empty;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int AttachDelegate(uint processId, [MarshalAs(UnmanagedType.LPWStr)] string name);

    public string LastError { get { lock (sync) return lastError; } }

    // Called on a worker thread by the form so Explorer startup cannot freeze it.
    public bool TryApply(ConversionRegion region, out string error)
    {
        if (!Enum.IsDefined(typeof(ConversionRegion), region)) throw new ArgumentOutOfRangeException("region");
        int mode = region == ConversionRegion.Default ? 0 : (int)region + 1;
        try
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("NativeNetworkIconController");
                if (mode == 0 && view == null) { error = string.Empty; return true; }
                if (mode == 0)
                {
                    uint currentExplorer;
                    GetWindowThreadProcessId(FindWindow("Shell_TrayWnd", null), out currentExplorer);
                    if (currentExplorer != explorerPid)
                    {
                        WriteCommand(0);
                        lastError = string.Empty;
                        error = string.Empty;
                        return true; // The previous Explorer and its XAML state no longer exist.
                    }
                }
            }
            if (mode != 0) EnsureAttached();
            int request;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("NativeNetworkIconController");
                request = WriteCommand(mode);
            }
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 6500)
            {
                lock (sync)
                {
                    if (disposed) throw new ObjectDisposedException("NativeNetworkIconController");
                    int ack = view.ReadInt32(28);
                    int status = view.ReadInt32(32);
                    if (ack == request && status == (mode == 0 ? 1 : 2))
                    {
                        lastError = string.Empty;
                        error = string.Empty;
                        return true;
                    }
                    if (status < 0 && ack == request) throw new InvalidOperationException(ReadNativeError());
                }
                Thread.Sleep(50);
            }
            throw new TimeoutException("系统图标未确认更新。请检查是否有任务栏美化工具占用 XAML 诊断接口，或当前 Windows 版本不受支持。");
        }
        catch (Exception ex)
        {
            lock (sync)
            {
                // On failure request a real native restoration, never leave a
                // timed-out command armed to unexpectedly apply later.
                if (!disposed && view != null) WriteCommand(0);
                lastError = ex.Message;
                error = lastError;
            }
            return false;
        }
    }

    private void EnsureAttached()
    {
        if (!Environment.Is64BitProcess || Environment.OSVersion.Version.Build < 22000)
            throw new PlatformNotSupportedException("原生图标版本目前支持 Windows 11 x64 默认任务栏。");
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        uint processId;
        GetWindowThreadProcessId(taskbar, out processId);
        if (taskbar == IntPtr.Zero || processId == 0) throw new InvalidOperationException("未找到 Windows 任务栏，请等待资源管理器恢复后重试。");
        using (Process process = Process.GetProcessById((int)processId))
        {
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (!string.Equals(process.MainModule.FileName, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前任务栏并非 Windows Explorer，无法修改原生图标。");
        }

        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException("NativeNetworkIconController");
            if (view != null && explorerPid == processId && view.ReadUInt32(52) == processId && view.ReadInt32(32) != -2)
            {
                if (!IsStale(view.ReadUInt64(56))) return;
                throw new InvalidOperationException("原生图标组件没有响应；请先选择默认或重新打开程序。");
            }
            if (view != null)
            {
                WriteCommand(0);
                view.Dispose(); mapping.Dispose();
                view = null; mapping = null;
            }
            if (attach == null)
            {
                string libraryPath = ExtractNativeLibrary();
                nativeModule = LoadLibraryEx(libraryPath, IntPtr.Zero, 0x00000008);
                if (nativeModule == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法加载原生图标组件。");
                IntPtr entry = GetProcAddress(nativeModule, "NativeAttach");
                if (entry == IntPtr.Zero) throw new InvalidOperationException("原生组件缺少 NativeAttach 入口。");
                attach = (AttachDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(AttachDelegate));
            }
            mappingName = "Local\\TimestampConverter.Native." + Guid.NewGuid().ToString("N");
            mapping = MemoryMappedFile.CreateNew(mappingName, MapSize, MemoryMappedFileAccess.ReadWrite);
            view = mapping.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
            view.Write(0, 0x54494332U);
            view.Write(4, 1U);
            view.Write(8, (uint)Process.GetCurrentProcess().Id);
            view.Write(16, GetTickCount64());
            explorerPid = processId;
            sequence = 0;
            if (heartbeatTimer == null) heartbeatTimer = new Timer(Heartbeat, null, 0, 500);
        }

        // Must run outside sync: heartbeat and cancellation remain available
        // while Windows connects the diagnostics client to Explorer.
        int result = attach(processId, mappingName);
        if (result < 0) throw new InvalidOperationException("连接系统图标失败（0x" + result.ToString("X8", CultureInfo.InvariantCulture) + "）。");
    }

    private int WriteCommand(int mode)
    {
        view.Write(12, mode);
        view.Write(16, GetTickCount64());
        Thread.MemoryBarrier();
        sequence = unchecked(sequence + 1);
        view.Write(24, sequence);
        return sequence;
    }

    private void Heartbeat(object state)
    {
        lock (sync)
        {
            if (disposed || view == null) return;
            view.Write(16, GetTickCount64());
        }
    }

    public string CheckHealth()
    {
        lock (sync)
        {
            if (disposed || view == null) return string.Empty;
            bool restoring = view.ReadInt32(12) == 0;
            if (restoring)
            {
                uint currentExplorer;
                GetWindowThreadProcessId(FindWindow("Shell_TrayWnd", null), out currentExplorer);
                if (currentExplorer != explorerPid) return string.Empty;
            }
            int request = view.ReadInt32(24);
            int ack = view.ReadInt32(28);
            int status = view.ReadInt32(32);
            if (status < 0 && ack == request) return ReadNativeError();
            if (view.ReadUInt32(52) != 0 && IsStale(view.ReadUInt64(56)))
                return "原生图标组件暂时无响应，请重新选择地区。";
            if (restoring && view.ReadUInt32(52) != 0 && (ack != request || status != 1))
                return "正在等待系统确认恢复原图标；若此提示持续，请重新选择默认。";
            return string.Empty;
        }
    }

    public string DescribeState()
    {
        lock (sync)
        {
            if (view == null) return "Not attached";
            return string.Format(CultureInfo.InvariantCulture,
                "Explorer={0}; status={1}; matched={2}; glyph=U+{3:X4}; original=U+{4:X4}; request={5}; ack={6}; detail={7}",
                view.ReadUInt32(52), view.ReadInt32(32), view.ReadUInt32(40), view.ReadUInt32(44), view.ReadUInt32(48), view.ReadInt32(24), view.ReadInt32(28), ReadNativeError());
        }
    }

    private static bool IsStale(ulong timestamp)
    {
        ulong now = GetTickCount64();
        return timestamp == 0 || (now > timestamp && now - timestamp > 4000);
    }

    private string ReadNativeError()
    {
        byte[] bytes = new byte[320];
        view.ReadArray(64, bytes, 0, bytes.Length);
        string value = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(value) && view.ReadInt32(36) == 0) return "OK";
        return "原生图标操作失败：" + (string.IsNullOrWhiteSpace(value)
            ? "0x" + view.ReadInt32(36).ToString("X8", CultureInfo.InvariantCulture) : value);
    }

    private static string ExtractNativeLibrary()
    {
        byte[] bytes;
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("TimestampConverter.Native.dll"))
        {
            if (resource == null) throw new InvalidOperationException("程序缺少内嵌原生组件，请重新构建。");
            using (var memory = new MemoryStream()) { resource.CopyTo(memory); bytes = memory.ToArray(); }
        }
        string hash;
        using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TimestampConverter.Native", hash);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "NetworkIconNative.dll");
        if (!File.Exists(path))
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try { File.Move(temporary, path); }
                catch (IOException) { if (!File.Exists(path)) throw; }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Validate the final file even if another instance created it first.
        using (SHA256 sha = SHA256.Create())
        using (FileStream file = File.OpenRead(path))
        {
            if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != hash)
                throw new InvalidOperationException("原生组件校验失败，请移除损坏的缓存文件后重试。");
        }
        return path;
    }

    public void Dispose()
    {
        int restoreRequest = 0;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (heartbeatTimer != null) heartbeatTimer.Dispose();
            if (view != null) restoreRequest = WriteCommand(0);
        }
        // Give the native UI thread a bounded opportunity to restore now.
        // Owner-process liveness/heartbeat provides the independent fallback.
        for (int i = 0; i < 20; i++)
        {
            lock (sync)
            {
                if (view == null || view.ReadInt32(28) == restoreRequest) break;
            }
            Thread.Sleep(50);
        }
        lock (sync)
        {
            if (view != null) { view.Dispose(); view = null; }
            if (mapping != null) { mapping.Dispose(); mapping = null; }
            // Do not FreeLibrary while a diagnostics attachment could be running.
            // The controller's copy is released by process exit.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
