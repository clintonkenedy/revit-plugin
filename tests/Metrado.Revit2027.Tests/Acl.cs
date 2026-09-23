using System.Security.AccessControl;
using System.Security.Principal;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Denies the current user a right on a file or folder until disposed, so a
/// test meets the refusal a real share or a locked-down folder gives. A deny
/// entry binds the owner too; the owner keeps the right to change the entries,
/// which is how the refusal is lifted.
/// </summary>
internal static class Acl
{
    public static IDisposable Deny(string path, FileSystemRights rights)
    {
        FileSystemAccessRule rule = new(WindowsIdentity.GetCurrent().User!, rights, AccessControlType.Deny);
        FileSystemInfo target = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);

        Change(target, security => security.AddAccessRule(rule));
        return new Lift(() => Change(target, security => security.RemoveAccessRule(rule)));
    }

    private static void Change(FileSystemInfo target, Action<FileSystemSecurity> change)
    {
        switch (target)
        {
            case DirectoryInfo folder:
                DirectorySecurity folderSecurity = folder.GetAccessControl();
                change(folderSecurity);
                folder.SetAccessControl(folderSecurity);
                break;
            case FileInfo file:
                FileSecurity fileSecurity = file.GetAccessControl();
                change(fileSecurity);
                file.SetAccessControl(fileSecurity);
                break;
        }
    }

    private sealed class Lift(Action lift) : IDisposable
    {
        public void Dispose() => lift();
    }
}
