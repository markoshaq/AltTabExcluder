using System;
using System.Runtime.InteropServices;

namespace AltTabExcluder.Services;

/// <summary>
/// Local P/Invoke for process/token elevation queries. CsWin32 generates
/// SafeHandle overloads and types (TOKEN_ACCESS_MASK, PROCESS_ACCESS_RIGHTS)
/// that are cumbersome for our simple elevation check; these raw signatures
/// are simpler and self-contained.
/// </summary>
internal static class NativeElevationInterop
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20; // TOKEN_INFORMATION_CLASS.TokenElevation

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, ref TOKEN_ELEVATION TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_ELEVATION
    {
        public uint TokenIsElevated;
    }
}
