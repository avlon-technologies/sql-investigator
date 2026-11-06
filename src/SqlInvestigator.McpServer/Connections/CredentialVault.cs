using System.Security.Cryptography;
using System.Text;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Connections;

/// <summary>
/// Reads the CredentialVault: one DPAPI CurrentUser blob per alias in a current-user-only file. Services only read
/// it (ADL: no service writes a secret-store). A secret read here is registered with <see cref="Redaction"/> before
/// it is used, and it is never returned through MCP.
/// </summary>
internal sealed class CredentialVault(AppPaths paths)
{
    public string FileFor(string alias) => Path.Combine(paths.VaultDirectory, ConnectionCatalogReader.NormalizeAlias(alias) + ".dpapi");

    /// <summary>The DPAPI entropy binds a blob to its alias, so a blob copied to another alias's file does not decrypt.</summary>
    public static byte[] EntropyFor(string alias) => Encoding.UTF8.GetBytes("SqlInvestigator.CredentialVault/1|" + ConnectionCatalogReader.NormalizeAlias(alias));

    public string ReadSecret(string alias)
    {
        var file = FileFor(alias);
        if (!File.Exists(file))
        {
            throw new InvestigatorException(new InvestigatorError(
                "credential.unavailable",
                $"No credential is stored for alias \"{alias}\".",
                "Re-provision the alias at the local console: SqlInvestigator provision --alias " + alias));
        }

        try
        {
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(file), EntropyFor(alias), DataProtectionScope.CurrentUser);
            var secret = Encoding.UTF8.GetString(clear);
            CryptographicOperations.ZeroMemory(clear);
            Redaction.Register(secret);
            return secret;
        }
        catch (CryptographicException)
        {
            throw new InvestigatorException(new InvestigatorError(
                "credential.unavailable",
                $"The stored credential for alias \"{alias}\" cannot be decrypted by this Windows user.",
                "DPAPI CurrentUser secrets open only for the user who stored them. Re-provision the alias at the local console as yourself."));
        }
    }
}

/// <summary>
/// The local provisioning console's access to the vault: the only code that places or deletes a secret. It is
/// reached from <see cref="ProvisioningConsole"/> and from nothing an MCP tool can call.
/// </summary>
internal sealed class VaultProvisioner(AppPaths paths, CredentialVault vault)
{
    public void Store(string alias, string secret)
    {
        SecureFiles.EnsurePrivateDirectory(paths.VaultDirectory);
        var clear = Encoding.UTF8.GetBytes(secret);
        try
        {
            var blob = ProtectedData.Protect(clear, CredentialVault.EntropyFor(alias), DataProtectionScope.CurrentUser);
            SecureFiles.AtomicWrite(vault.FileFor(alias), blob);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public bool Delete(string alias)
    {
        var file = vault.FileFor(alias);
        if (!File.Exists(file))
        {
            return false;
        }

        File.Delete(file);
        return true;
    }
}
