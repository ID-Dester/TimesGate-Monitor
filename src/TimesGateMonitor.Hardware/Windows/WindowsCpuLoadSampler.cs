using System.Runtime.InteropServices;

namespace TimesGateMonitor.Hardware.Windows;

internal sealed class WindowsCpuLoadSampler
{
    private readonly object _sync = new();
    private SystemTimesSample? _previous;

    public double? Capture()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        var current = new SystemTimesSample(
            ToUInt64(idle),
            ToUInt64(kernel),
            ToUInt64(user));

        lock (_sync)
        {
            var previous = _previous;
            _previous = current;
            return previous.HasValue
                ? CalculateUsage(previous.Value, current)
                : null;
        }
    }

    internal static double? CalculateUsage(SystemTimesSample previous, SystemTimesSample current)
    {
        if (current.Idle < previous.Idle ||
            current.Kernel < previous.Kernel ||
            current.User < previous.User)
        {
            return null;
        }

        var idleDelta = current.Idle - previous.Idle;
        var kernelDelta = current.Kernel - previous.Kernel;
        var userDelta = current.User - previous.User;
        var totalDelta = kernelDelta + userDelta;

        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            return null;
        }

        return Math.Clamp(
            100d * (totalDelta - idleDelta) / totalDelta,
            0d,
            100d);
    }

    private static ulong ToUInt64(NativeMethods.FileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    internal readonly record struct SystemTimesSample(ulong Idle, ulong Kernel, ulong User);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            internal uint LowDateTime;
            internal uint HighDateTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(
            out FileTime idleTime,
            out FileTime kernelTime,
            out FileTime userTime);
    }
}
