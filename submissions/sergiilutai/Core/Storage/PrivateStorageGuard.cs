using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Console.AnalyticsET.Cli;

namespace Core.AnalyticsET.Storage;

internal interface IPrivateStorageGuard
{
    string PrepareDirectory(string path);
    void VerifyFile(string path);
}

internal sealed class WindowsPrivateStorageGuard : IPrivateStorageGuard
{
    public string PrepareDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        RejectSharedOrRedirectedPath(fullPath);
        if (!OperatingSystem.IsWindows())
        {
            throw new InputException("Protected local storage currently requires Windows.");
        }

        PrepareWindowsDirectory(fullPath);
        return fullPath;
    }

    public void VerifyFile(string path)
    {
        RejectSharedOrRedirectedPath(Path.GetFullPath(path));
        if (!OperatingSystem.IsWindows())
        {
            throw new InputException("Protected local storage currently requires Windows.");
        }

        FileSecurity security = new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        VerifyWindowsAccess(security, WindowsIdentity.GetCurrent().User!, requireProtected: false);
    }

    internal static void RejectSharedOrRedirectedPath(string path)
    {
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InputException("Private storage cannot use a redirected or symbolic-link path.");
        }

        DirectoryInfo? directory = entry is DirectoryInfo existing ? existing : ((FileInfo)entry).Directory;
        while (directory is not null)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InputException("Private storage cannot use a redirected or symbolic-link path.");
            }

            if (File.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.Exists(Path.Combine(directory.FullName, "openspec")))
            {
                throw new InputException("Choose a private storage directory outside repository and planning workspaces.");
            }

            directory = directory.Parent;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PrepareWindowsDirectory(string path)
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User ??
            throw new UnauthorizedAccessException("Windows operator identity is unavailable.");
        DirectoryInfo directory = new(path);
        if (!directory.Exists)
        {
            DirectorySecurity security = new();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (SecurityIdentifier identity in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            {
                security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }

            // New directories are created with their private ACL before any identifiers are written.
            Directory.CreateDirectory(directory.Parent!.FullName);
            directory.Create(security);
        }

        VerifyWindowsAccess(directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), user,
            requireProtected: true);
    }

    [SupportedOSPlatform("windows")]
    internal static void VerifyWindowsAccess(FileSystemSecurity security, SecurityIdentifier user, bool requireProtected)
    {
        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        if (!user.Equals(security.GetOwner(typeof(SecurityIdentifier))) ||
            (requireProtected && !security.AreAccessRulesProtected))
        {
            throw new InputException("Private storage must be owned by the current operator with protected directory permissions. Choose a new dedicated directory.");
        }

        bool userAllowed = false;
        bool systemAllowed = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                (!rule.IdentityReference.Equals(user) && !rule.IdentityReference.Equals(system)))
            {
                throw new InputException("Private storage permissions must grant access only to the operator and SYSTEM.");
            }

            bool fullAccess = (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
            userAllowed |= rule.IdentityReference.Equals(user) && fullAccess;
            systemAllowed |= rule.IdentityReference.Equals(system) && fullAccess;
        }

        if (!userAllowed || !systemAllowed)
        {
            throw new InputException("Private storage needs operator and SYSTEM access. Choose a new dedicated directory.");
        }
    }
}
