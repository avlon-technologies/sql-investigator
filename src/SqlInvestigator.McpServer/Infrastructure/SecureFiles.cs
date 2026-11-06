using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SqlInvestigator.Infrastructure;

/// <summary>
/// Current-user-only files (BindCredentialVault acl: current-user-only; atomic-json-files: access-controlled) and
/// atomic replacement (atomic-json-files: atomic-replace-single-writer). Windows: protected NTFS DACLs granting the
/// current user only. macOS: owner-only modes, 0700 for directories and 0600 for files (ADR-007).
/// </summary>
internal static class SecureFiles
{
    private const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode GroupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("the current Windows user has no security identifier");

    /// <summary>
    /// Create the directory if needed and restrict it to the current user. Windows: a protected DACL, the current
    /// user with full control, inherited by every file and directory beneath it; nothing inherited from the parent.
    /// macOS: mode 0700. Only the owner (or root) may change a mode, so a successful chmod also shows the directory
    /// belongs to this user.
    /// </summary>
    public static void EnsurePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                Directory.CreateDirectory(path, OwnerDirectory);
                File.SetUnixFileMode(path, OwnerDirectory);
                if ((File.GetUnixFileMode(path) & GroupOrOther) != 0)
                {
                    throw new IOException("the mode did not change");
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                throw Inaccessible(path, e);
            }

            return;
        }

        try
        {
            var dir = Directory.CreateDirectory(path);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            SetOwnerIfDifferent(security, dir.GetAccessControl(AccessControlSections.Owner));
            security.AddAccessRule(new FileSystemAccessRule(
                CurrentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dir.SetAccessControl(security);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            throw Inaccessible(path, e);
        }
    }

    /// <summary>Restrict one file to the current user: an explicit protected DACL on Windows, mode 0600 on macOS.</summary>
    public static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerFile);
            return;
        }

        var file = new FileInfo(path);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        SetOwnerIfDifferent(security, file.GetAccessControl(AccessControlSections.Owner));
        security.AddAccessRule(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl, AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    /// <summary>
    /// Writing the owner needs WRITE_OWNER, which an ordinary user lacks in a folder that grants only Modify (for
    /// example a folder made directly under C:\). A directory or file this user just created is already theirs, so
    /// the owner is written only when it is someone else; the DACL alone needs only the owner's implicit WRITE_DAC.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void SetOwnerIfDifferent(FileSystemSecurity target, FileSystemSecurity current)
    {
        if (current.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !owner.Equals(CurrentUser))
        {
            target.SetOwner(CurrentUser);
        }
    }

    private static InvestigatorException Inaccessible(string path, Exception e) => OperatingSystem.IsWindows()
        ? new(new InvestigatorError(
            "store.inaccessible",
            $"SQL Investigator cannot create or restrict its data folder {path} to the current Windows user ({e.GetType().Name}).",
            "Use a folder you own with full control, such as the default under %LOCALAPPDATA%, or set SQLINVESTIGATOR_HOME to one."))
        : new(new InvestigatorError(
            "store.inaccessible",
            $"SQL Investigator cannot create its data folder {path} or restrict it to the current user, mode 0700 ({e.GetType().Name}).",
            "Use a folder you own, such as the default under ~/Library/Application Support/SqlInvestigator, or set SQLINVESTIGATOR_HOME to one."));

    /// <summary>
    /// True when only the current user can reach the path: on Windows every access rule grants the current user and
    /// none is inherited; on macOS no group or other permission bit is set.
    /// </summary>
    public static bool IsCurrentUserOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (File.GetUnixFileMode(path) & GroupOrOther) == 0;
        }

        var security = new FileInfo(path).GetAccessControl();
        if (!security.AreAccessRulesProtected)
        {
            return false;
        }

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        if (rules.Count == 0)
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in rules)
        {
            if (!rule.IdentityReference.Equals(CurrentUser))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Write the bytes to a temporary file in the same directory, flush them to disk, then move it over the target.
    /// A reader sees the old file or the new one, never a partial write.
    /// </summary>
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = OwnerFile; // never readable by others, not even before RestrictFile
            }

            using (var stream = new FileStream(temp, options))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            RestrictFile(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// A named lock per store file, so two server processes (Claude Code may start more than one) never interleave a
    /// read-modify-write. Held only around synchronous file work. Windows: per logon session (Local\). macOS: each
    /// shell is its own session there, so the lock is Global\, which .NET scopes to the current user.
    /// </summary>
    public static T WithFileLock<T>(string path, Func<T> work)
    {
        var prefix = OperatingSystem.IsWindows() ? "Local\\" : "Global\\";
        var name = prefix + "SqlInvestigator." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..32];
        using var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
            {
                throw new InvestigatorException(new InvestigatorError(
                    "store.busy",
                    $"The local store {Path.GetFileName(path)} is locked by another SqlInvestigator process.",
                    "Retry; if it persists, close other SqlInvestigator processes."));
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died mid-write. Atomic replacement means the file is the old or the new version,
            // never a partial one, so taking over the lock is safe.
        }

        try
        {
            return work();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
