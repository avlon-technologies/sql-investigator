using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;


namespace SqlInvestigator.Testing;

/// <summary>
/// Inspects the credential vault independently of the code under test: it re-derives what a DPAPI CurrentUser blob and
/// a current-user-only file look like instead of calling the implementation's own helpers.
/// </summary>
public static class VaultInspector
{
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

    /// <summary>True when the file's DACL is protected (no inheritance) and grants access to the current user only.</summary>
    public static bool IsCurrentUserOnly(string path)
    {
        var me = WindowsIdentity.GetCurrent().User!;
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        if (!security.AreAccessRulesProtected)
        {
            return false;
        }

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        return rules.Count > 0 && rules.All(r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Equals(me));
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
