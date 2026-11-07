using System.Runtime.InteropServices;
using System.Text;

namespace TimesGateMonitor.App.Infrastructure;

internal static class LocalTokenCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const string TargetName = "TGm/TimesGateMonitor/LocalToken";

    public static bool TryRead(out string token)
    {
        token = string.Empty;
        if (!NativeMethods.CredRead(TargetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return false;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            token = Decode(bytes);
            return !string.IsNullOrWhiteSpace(token);
        }
        finally
        {
            NativeMethods.CredFree(credentialPointer);
        }
    }

    public static void Save(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var bytes = Encode(token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeMethods.Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Windows Credential Manager rejected LocalToken (error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    internal static byte[] Encode(string token) => Encoding.Unicode.GetBytes(token);

    internal static string Decode(byte[] bytes) => Encoding.Unicode.GetString(bytes).TrimEnd('\0');

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Credential
        {
            internal int Flags;
            internal int Type;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string? TargetName;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string? Comment;

            internal long LastWritten;
            internal int CredentialBlobSize;
            internal IntPtr CredentialBlob;
            internal int Persist;
            internal int AttributeCount;
            internal IntPtr Attributes;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string? TargetAlias;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string? UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string target,
            int type,
            int flags,
            out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr buffer);
    }
}
