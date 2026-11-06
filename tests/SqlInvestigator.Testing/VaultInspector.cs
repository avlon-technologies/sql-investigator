using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;


namespace SqlInvestigator.Testing;

/// <summary>
/// Inspects the credential vault independently of the code under test: it re-derives what a DPAPI CurrentUser blob, a
/// keychain item only SqlInvestigator may read, and a current-user-only file look like instead of calling the
/// implementation's own helpers.
/// </summary>
public static class VaultInspector
{
    /// <summary>What the acceptance step credential.user-protected checks, on this operating system.</summary>
    public sealed record Finding(string Mechanism, bool Present, bool ProtectedForUser, bool BoundToAlias, bool StoreCurrentUserOnly, string? Detail);

    /// <summary>
    /// Windows: the alias's DPAPI blob exists, is protected for the current user (not the machine), decrypts to the
    /// expected secret only with its alias entropy, and the file, the vault and the data home are current-user-only.
    /// macOS: an item exists under the data home's service and the alias's account; this process, another
    /// application of the same user, cannot read its data without the user's approval; the data home is 0700.
    /// </summary>
    public static Finding Check(string home, string alias, string expected)
    {
        if (OperatingSystem.IsWindows())
        {
            var file = Path.Combine(home, "vault", alias + ".dpapi");
            if (!File.Exists(file))
            {
                return new("dpapi-currentuser", false, false, false, false, "no vault file " + file);
            }

            var blob = File.ReadAllBytes(file);
            return new("dpapi-currentuser", true, IsDpapiCurrentUserBlob(blob), DecryptsForAlias(blob, alias, expected),
                IsCurrentUserOnly(file) && IsCurrentUserOnly(Path.GetDirectoryName(file)!) && IsCurrentUserOnly(home), null);
        }

        if (OperatingSystem.IsMacOS())
        {
            var lookup = KeychainProbe.Lookup(home, alias);
            if (lookup != KeychainProbe.Success)
            {
                return new("keychain-login", false, false, false, false, $"no keychain item (OSStatus {lookup})");
            }

            var read = KeychainProbe.ReadAsOtherApplication(home, alias, out var value);
            var protectedForUser = KeychainProbe.NeedsApproval(read);
            var detail = read == KeychainProbe.Success
                ? "another application read the item without approval" + (value == expected ? " (and it holds the secret)" : string.Empty)
                : $"another application's read answered OSStatus {read}";
            return new("keychain-login", true, protectedForUser, true, IsCurrentUserOnly(home), detail);
        }

        throw new PlatformNotSupportedException("the vault is inspected on Windows and macOS only");
    }

    private static readonly Guid DpapiProvider = new("df9d8cd0-1501-11d1-8c7a-00c04fc297eb");
    private const int LocalMachineFlag = 0x4; // CRYPTPROTECT_LOCAL_MACHINE

    /// <summary>True when the bytes are a DPAPI blob protected for the current user (not the machine).</summary>
    public static bool IsDpapiCurrentUserBlob(byte[] blob)
    {
        if (blob.Length < 44 || BitConverter.ToInt32(blob, 0) != 1 || new Guid(blob.AsSpan(4, 16)) != DpapiProvider)
        {
            return false;
        }

        var flags = BitConverter.ToInt32(blob, 40);
        return (flags & LocalMachineFlag) == 0;
    }

    /// <summary>True when the blob decrypts, as the current user, with the alias's entropy and not without it.</summary>
    [SupportedOSPlatform("windows")]
    public static bool DecryptsForAlias(byte[] blob, string alias, string expected)
    {
        var entropy = Encoding.UTF8.GetBytes("SqlInvestigator.CredentialVault/1|" + alias.ToLowerInvariant());
        try
        {
            var clear = ProtectedData.Unprotect(blob, entropy, DataProtectionScope.CurrentUser);
            if (Encoding.UTF8.GetString(clear) != expected)
            {
                return false;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }

        try
        {
            ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
            return false; // decrypting without the entropy should fail
        }
        catch (CryptographicException)
        {
            return true;
        }
    }

    /// <summary>
    /// Windows: the DACL is protected (no inheritance) and grants access to the current user only. macOS: the path
    /// belongs to the current user and has no group or other permission bit, as stat(1) reports it.
    /// </summary>
    public static bool IsCurrentUserOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var stat = Run("stat", "-f", "%u %Lp", path).Split(' ');
            var uid = Run("id", "-u");
            var mode = Convert.ToInt32(stat[1], 8);
            return stat[0] == uid && (mode & 0b000_111_111) == 0;
        }

        var me = WindowsIdentity.GetCurrent().User!;
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        if (!security.AreAccessRulesProtected)
        {
            return false;
        }

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        foreach (var r in rules)
        {
            if (r.AccessControlType != AccessControlType.Allow || !r.IdentityReference.Equals(me))
            {
                return false;
            }
        }

        return rules.Count > 0;
    }

    private static string Run(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }

    /// <summary>Every file under <paramref name="root"/> whose bytes contain <paramref name="value"/> as UTF-8 or UTF-16.</summary>
    public static IReadOnlyList<string> FilesContaining(string root, string value)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var utf8 = Encoding.UTF8.GetBytes(value);
        var utf16 = Encoding.Unicode.GetBytes(value);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var bytes = File.ReadAllBytes(f);
                return bytes.AsSpan().IndexOf(utf8) >= 0 || bytes.AsSpan().IndexOf(utf16) >= 0;
            })
            .ToList();
    }
}
