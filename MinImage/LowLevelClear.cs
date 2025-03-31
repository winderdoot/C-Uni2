using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MinImage;

/// <summary>
/// I took this from https://stackoverflow.com/questions/3244976/is-is-possible-to-programmatically-clear-the-console-history in desperation
/// </summary>
public static partial class LowLevelClear
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleHistoryInfo(CONSOLE_HISTORY_INFO ConsoleHistoryInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleHistoryInfo(CONSOLE_HISTORY_INFO ConsoleHistoryInfo);

    [StructLayout(LayoutKind.Sequential)]
    private class CONSOLE_HISTORY_INFO
    {
        public uint cbSize;
        public uint BufferSize;
        public uint BufferCount;
        public uint TrimDuplicates;
    }

    public static void ClearConsoleHistory()
    {
        var chi = new CONSOLE_HISTORY_INFO();
        chi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(CONSOLE_HISTORY_INFO));

        if (!GetConsoleHistoryInfo(chi))
        {
            return;
        }

        var originalBufferSize = chi.BufferSize;
        chi.BufferSize = 0;

        if (!SetConsoleHistoryInfo(chi))
        {
            return;
        }

        chi.BufferSize = originalBufferSize;

        if (!SetConsoleHistoryInfo(chi))
        {
            return;
        }
    }
}