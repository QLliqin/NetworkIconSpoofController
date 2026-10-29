using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;

// Opt-in desktop integration test. Reads the native glyph through Explorer's
// UI Automation provider, independently of the component's acknowledgement.
// No overlay, screenshots, registry edits, network changes or Explorer restart.
internal static class NativeNetworkIconTests
{
    [MTAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--crash-child")
            {
                // Deliberately kept alive without Dispose for the parent to
                // terminate; this exercises the native owner-process watchdog.
                var childController = new NativeNetworkIconController();
                string childError;
                ConversionRegion childRegion = (ConversionRegion)int.Parse(args[1]);
                Require(childController.TryApply(childRegion, out childError), childError);
                Console.WriteLine("READY"); Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
                GC.KeepAlive(childController);
                return 0;
            }
            HashSet<char> original = ReadGlyphs();
            Require(original.Count > 0, "No native network glyph could be read before testing.");
            Console.WriteLine("Before: " + Format(original));
            using (var controller = new NativeNetworkIconController())
            {
                string error;
                Require(controller.TryApply(ConversionRegion.Default, out error), error);
                ConversionRegion[] modes = { ConversionRegion.UnitedStates, ConversionRegion.China, ConversionRegion.UnitedKingdom };
                char[] expected = { '\uF384', '\uE839', '\uEC3F' };
                for (int i = 0; i < modes.Length; i++)
                {
                    Require(controller.TryApply(modes[i], out error), error + " | " + controller.DescribeState());
                    Require(WaitFor(new HashSet<char> { expected[i] }), "Explorer UIA glyph did not match " + modes[i] + "; actual=" + Format(ReadGlyphs()));
                    Console.WriteLine("PASS " + modes[i] + " native Explorer glyph " + Format(ReadGlyphs()) + "; " + controller.DescribeState());
                }
                Require(controller.TryApply(ConversionRegion.Default, out error), error);
                Require(WaitFor(original), "Default did not restore Explorer's original glyph.");
                Console.WriteLine("PASS Default native restoration: " + Format(ReadGlyphs()));
                Require(controller.TryApply(ConversionRegion.China, out error), error);
                Require(WaitFor(new HashSet<char> { '\uE839' }), "Reapplication failed before disposal.");
            }
            Require(WaitFor(original), "Disposal did not restore original glyph.");
            Console.WriteLine("PASS disposal restoration. No overlay window was created.");
            TestOwnerExit(original);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void TestOwnerExit(HashSet<char> original)
    {
        bool originallyOffline = original.SetEquals(new HashSet<char> { '\uF384' });
        ConversionRegion testRegion = originallyOffline ? ConversionRegion.China : ConversionRegion.UnitedStates;
        var changed = new HashSet<char> { originallyOffline ? '\uE839' : '\uF384' };
        Require(!original.SetEquals(changed), "Watchdog test must change the original glyph.");
        using (var ready = new ManualResetEvent(false))
        using (var child = new Process())
        {
            child.StartInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--crash-child " + (int)testRegion)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            child.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == "READY") ready.Set(); };
            child.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) Console.Error.WriteLine(e.Data); };
            Require(child.Start(), "Could not start watchdog test child.");
            try
            {
                child.BeginOutputReadLine(); child.BeginErrorReadLine();
                Require(ready.WaitOne(15000), "Watchdog test child never confirmed a native glyph.");
                Require(WaitFor(changed), "Watchdog child did not change the native glyph.");
                child.Kill();
                Require(child.WaitForExit(4000), "Watchdog child did not exit.");
                Require(WaitFor(original), "Native glyph remained modified after the owner was forcibly terminated.");
                Console.WriteLine("PASS native restoration after forced owner-process termination.");
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(4000); }
            }
        }
    }

    private static bool WaitFor(HashSet<char> expected)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 4000)
        {
            if (expected.SetEquals(ReadGlyphs())) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private static HashSet<char> ReadGlyphs()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        uint pid;
        GetWindowThreadProcessId(taskbar, out pid);
        Require(taskbar != IntPtr.Zero, "Taskbar unavailable.");
        AutomationElement root = AutomationElement.FromHandle(taskbar);
        var found = new HashSet<char>();
        var stack = new Stack<Tuple<AutomationElement, bool>>();
        stack.Push(Tuple.Create(root, false));
        int visited = 0;
        while (stack.Count != 0 && ++visited < 6000)
        {
            var next = stack.Pop();
            var info = next.Item1.Current;
            string name = info.Name ?? string.Empty;
            bool network = next.Item2 || (info.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal)
                && info.AutomationId == "SystemTrayIcon"
                && (name.StartsWith("网络", StringComparison.Ordinal) || name.StartsWith("Network", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Wi-Fi", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Ethernet", StringComparison.OrdinalIgnoreCase)));
            if (network && info.AutomationId == "InnerTextBlock" && !info.IsOffscreen && name.Length == 1 && name[0] >= '\uE000')
            {
                Require(info.ProcessId == (int)pid, "The glyph is not owned by Explorer.");
                found.Add(name[0]);
            }
            AutomationElement child = TreeWalker.RawViewWalker.GetFirstChild(next.Item1);
            while (child != null)
            {
                stack.Push(Tuple.Create(child, network));
                child = TreeWalker.RawViewWalker.GetNextSibling(child);
            }
        }
        return found;
    }

    private static string Format(IEnumerable<char> glyphs)
    {
        var values = new List<string>();
        foreach (char glyph in glyphs) values.Add("U+" + ((int)glyph).ToString("X4"));
        return string.Join(",", values.ToArray());
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
