using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Microsoft.Win32;
using SCSFix.Core.App;

namespace SCSFix.App;

/// <summary>What the app writes when it can't go on: <c>%LOCALAPPDATA%\SCSFix\crash.log</c>, with the Windows, runtime and CPU facts a
/// report needs. Several upstream reports (the app closing itself, not opening on Windows 10 21H2, an illegal-instruction crash on an
/// old AMD CPU) came with nothing to go on; a startup that fails shows a message too, instead of ending without a word.</summary>
public static class CrashLog
{
    public static string Path => System.IO.Path.Combine(AppStore.DefaultDir, "crash.log");

    /// <summary>Hooks the exceptions nothing catches. Once, first thing.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("an exception nothing handled" + (e.IsTerminating ? " (the app ends)" : ""), e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("a task's exception nobody observed", e.Exception);
    }

    public static void Write(string what, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {what}\r\n{Facts()}\r\n{e}\r\n\r\n");
        }
        catch (Exception) { }   // a log that can't be written must not make it worse
    }

    /// <summary>Facts that decide whether the app starts: the OS build, the runtime, the CPU's vector units, and whether
    /// User Account Control is on (WinUI apps don't start under every setting of it).</summary>
    public static string Facts()
    {
        string uac;
        try { uac = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", null)?.ToString() ?? "unset"; }
        catch (Exception) { uac = "unreadable"; }
        return $"  {typeof(CrashLog).Assembly.GetName().Version}; Windows {Environment.OSVersion.Version}; {RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture}; "
            + $"AVX2 {Avx2.IsSupported}, BMI2 {Bmi2.IsSupported}, SSE4.2 {Sse42.IsSupported}; EnableLUA {uac}; {Environment.ProcessorCount} threads";
    }

    /// <summary>A start that failed: logged and said.</summary>
    public static void StartFailed(Exception e)
    {
        Write("the app couldn't start", e);
        MessageBoxW(0, $"SCSFix couldn't start.\n\n{e.GetType().Name}: {e.Message}\n\nDetails are in\n{Path}", "SCSFix", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
