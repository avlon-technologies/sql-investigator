using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SqlInvestigator.Infrastructure;

/// <summary>
/// Current-user-only files (BindCredentialVault acl: current-user-only; atomic-json-files: access-controlled) and
/// atomic replacement (atomic-json-files: atomic-replace-single-writer).
/// </summary>
internal static class SecureFiles
{
    private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("the current Windows user has no security identifier");

    /// <summary>
    /// Create the directory if needed and give it a protected DACL: the current user, full control, inherited by
    /// every file and directory created beneath it. Nothing is inherited from the parent.
    /// </summary>
    public static void EnsurePrivateDirectory(string path)
    {
        var dir = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(CurrentUser);
        security.AddAccessRule(new FileSystemAccessRule(
            CurrentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        dir.SetAccessControl(security);
    }

    /// <summary>Give one file an explicit protected DACL: the current user, full control, and nobody else.</summary>
    public static void RestrictFile(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(CurrentUser);
        security.AddAccessRule(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    /// <summary>
    /// True when every access rule on the file grants the current user and the rules are not inherited.
    /// Used by tests and by the console's `doctor` verb.
    /// </summary>
    public static bool IsCurrentUserOnly(string path)
    {
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
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
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
    /// A machine-wide-for-this-session lock per store file, so two server processes (Claude Code may start more
    /// than one) never interleave a read-modify-write. Held only around synchronous file work.
    /// </summary>
    public static T WithFileLock<T>(string path, Func<T> work)
    {
        var name = "Local\\SqlInvestigator." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..32];
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
