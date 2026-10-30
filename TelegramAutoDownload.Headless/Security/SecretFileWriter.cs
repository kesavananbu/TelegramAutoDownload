using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TelegramAutoDownload.Headless.Security;

/// <summary>Atomic writes with restrictive permissions for secrets under DATA_DIR.</summary>
public static class SecretFileWriter
{
    public static void WriteSecret(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            ApplyDirectoryMode(directory);
        }

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, contents);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
            ApplyFileMode(path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static void ApplyDirectoryMode(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        TryRestrictWindows(directory, isDirectory: true);
    }

    public static void ApplyFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return;
        }

        TryRestrictWindows(path, isDirectory: false);
    }

    [SupportedOSPlatform("windows")]
    private static void TryRestrictWindows(string path, bool isDirectory)
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            if (identity?.User is null) return;

            if (isDirectory)
            {
                var dirInfo = new DirectoryInfo(path);
                var acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                acl.AddAccessRule(new FileSystemAccessRule(
                    identity.User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                dirInfo.SetAccessControl(acl);
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var acl = new FileSecurity();
                acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                acl.AddAccessRule(new FileSystemAccessRule(
                    identity.User, FileSystemRights.FullControl, AccessControlType.Allow));
                fileInfo.SetAccessControl(acl);
            }
        }
        catch
        {
            // Best-effort on Windows hosts without ACL support.
        }
    }
}
