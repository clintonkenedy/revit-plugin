using System.Security.AccessControl;
using System.Security.Principal;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Denies the current user rights on a file or folder until disposed, so a
/// test meets the refusal a real share or a locked-down folder gives. A deny
/// entry binds the owner too; the owner keeps the right to change the entries,
/// which is how the refusal is lifted.
/// </summary>
internal static class Acl
{
    public static IDisposable Deny(string path, FileSystemRights rights) => Deny(path, new DeniedRight(rights));

    /// <summary>Several entries at once, such as one on a folder and one its new files inherit.</summary>
    public static IDisposable Deny(string path, params DeniedRight[] denied)
    {
        FileSystemAccessRule[] rules =
        [
            .. denied.Select(entry => new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!, entry.Rights, entry.Inheritance, entry.Propagation, AccessControlType.Deny)),
        ];
        FileSystemInfo target = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);

        Change(target, security => Array.ForEach(rules, rule => security.AddAccessRule(rule)));
        return new Lift(() => Change(target, security => Array.ForEach(rules, rule => security.RemoveAccessRule(rule))));
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

/// <param name="Inheritance">Which children inherit the entry; none, by default, so it binds only the target itself.</param>
internal sealed record DeniedRight(
    FileSystemRights Rights,
    InheritanceFlags Inheritance = InheritanceFlags.None,
    PropagationFlags Propagation = PropagationFlags.None);
