using System.Runtime.InteropServices;

namespace Rimtera.Systems;

/// <summary>
/// The project stays a console-subsystem executable so terminal modes keep
/// working. A normal game launch hides and releases the console only when it
/// belongs exclusively to Rimtera, leaving a single game window.
/// </summary>
internal static class ConsoleHost
{
    private const int SwHide = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    public static void HideForGraphicalMode()
    {
        var window = GetConsoleWindow();
        if (window == IntPtr.Zero)
            return;

        var processes = new uint[8];
        var count = GetConsoleProcessList(processes, (uint)processes.Length);
        if (count != 1 || processes[0] != (uint)Environment.ProcessId)
            return;

        ShowWindow(window, SwHide);
        FreeConsole();
    }
}
