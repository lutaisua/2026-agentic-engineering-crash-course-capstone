using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Console.AnalyticsET.Cli;

namespace Core.AnalyticsET.Authentication;

internal sealed class WindowsGenericCredentialVault : ICredentialVault
{
    public VaultCredential Read(string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InputException("Application credentials currently require Windows Credential Manager.");
        }

        return ReadWindowsCredential(target);
    }

    [SupportedOSPlatform("windows")]
    private static VaultCredential ReadWindowsCredential(string target)
    {
        if (!CredRead(target, 1, 0, out IntPtr buffer))
        {
            throw new InputException("Windows generic credential is unavailable. Check its target and the current operator's logon session.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(buffer);
            if (credential.Type != 1 || credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize is 0 or > 2560 || credential.CredentialBlobSize % 2 != 0)
            {
                throw new InputException("Windows generic credential must contain a Unicode application secret value.");
            }

            // The generic credential username (often a Secret ID) is never used for authentication.
            string value = Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2))!
                .TrimEnd('\0');
            long version = ((long)(uint)credential.LastWritten.dwHighDateTime << 32) | (uint)credential.LastWritten.dwLowDateTime;
            return new(value, version.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            CredFree(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);
}
