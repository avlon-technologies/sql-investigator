using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Connections;

/// <summary>
/// Reads the CredentialVault: one secret per alias, protected for the current operating-system user (ADR-007,
/// BindCredentialVault os-user-secret-store). Services only read it (ADL: no service writes a secret-store). A secret
/// read here is registered with <see cref="Redaction"/> before it is used, and it is never returned through MCP.
/// </summary>
internal sealed class CredentialVault(AppPaths paths)
{
    private readonly ISecretStore store = SecretStores.ForThisPlatform(paths);

    internal ISecretStore Store => store;

    /// <summary>Where the Windows store keeps an alias's DPAPI blob.</summary>
    public string FileFor(string alias) => Path.Combine(paths.VaultDirectory, ConnectionCatalogReader.NormalizeAlias(alias) + ".dpapi");

    /// <summary>The DPAPI entropy binds a blob to its alias, so a blob copied to another alias's file does not decrypt.</summary>
    public static byte[] EntropyFor(string alias) => Encoding.UTF8.GetBytes("SqlInvestigator.CredentialVault/1|" + ConnectionCatalogReader.NormalizeAlias(alias));

    public string ReadSecret(string alias)
    {
        var secret = store.Read(ConnectionCatalogReader.NormalizeAlias(alias));
        Redaction.Register(secret);
        return secret;
    }

    internal static InvestigatorException NotStored(string alias) => new(new InvestigatorError(
        "credential.unavailable",
        $"No credential is stored for alias \"{alias}\".",
        "Re-provision the alias at the local console: SqlInvestigator provision --alias " + alias));
}

/// <summary>
/// The local provisioning console's access to the vault: the only code that places or deletes a secret. It is
/// reached from <see cref="ProvisioningConsole"/> and from nothing an MCP tool can call.
/// </summary>
internal sealed class VaultProvisioner(AppPaths paths, CredentialVault vault)
{
    public void Store(string alias, string secret)
    {
        SecureFiles.EnsurePrivateDirectory(paths.Home);
        vault.Store.Write(ConnectionCatalogReader.NormalizeAlias(alias), secret);
    }

    public bool Delete(string alias) => vault.Store.Delete(ConnectionCatalogReader.NormalizeAlias(alias));
}

/// <summary>One operating system's per-user secret store. Aliases arrive normalized.</summary>
internal interface ISecretStore
{
    /// <summary>The mechanism, for diagnostics: dpapi-currentuser or keychain-login.</summary>
    string Mechanism { get; }

    /// <summary>The secret, or <c>credential.unavailable</c>. Never prompts and never waits on the user.</summary>
    string Read(string alias);

    /// <summary>Store or replace the secret. Local console only.</summary>
    void Write(string alias, string secret);

    /// <summary>Delete the secret; false when there was none. Local console only.</summary>
    bool Delete(string alias);
}

internal static class SecretStores
{
    public static ISecretStore ForThisPlatform(AppPaths paths)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretStore(paths);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new KeychainSecretStore(paths);
        }

        return new UnsupportedSecretStore();
    }

    private sealed class UnsupportedSecretStore : ISecretStore
    {
        public string Mechanism => "none";

        public string Read(string alias) => throw Unsupported();

        public void Write(string alias, string secret) => throw Unsupported();

        public bool Delete(string alias) => throw Unsupported();

        // Never a weaker fallback: with no per-user secret store there is nowhere a secret may be kept.
        private static InvestigatorException Unsupported() => new(new InvestigatorError(
            "platform.unsupported",
            "SQL Investigator protects credentials with the operating system's per-user secret store, available on Windows and macOS only.",
            "Run SQL Investigator on Windows x64 or macOS 15 or later."));
    }
}

/// <summary>Windows: one DPAPI CurrentUser blob per alias, with per-alias entropy, in a current-user-only file.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecretStore(AppPaths paths) : ISecretStore
{
    public string Mechanism => "dpapi-currentuser";

    private string FileFor(string alias) => Path.Combine(paths.VaultDirectory, alias + ".dpapi");

    public string Read(string alias)
    {
        var file = FileFor(alias);
        if (!File.Exists(file))
        {
            throw CredentialVault.NotStored(alias);
        }

        try
        {
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(file), CredentialVault.EntropyFor(alias), DataProtectionScope.CurrentUser);
            var secret = Encoding.UTF8.GetString(clear);
            CryptographicOperations.ZeroMemory(clear);
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

    public void Write(string alias, string secret)
    {
        SecureFiles.EnsurePrivateDirectory(paths.VaultDirectory);
        var clear = Encoding.UTF8.GetBytes(secret);
        try
        {
            var blob = ProtectedData.Protect(clear, CredentialVault.EntropyFor(alias), DataProtectionScope.CurrentUser);
            SecureFiles.AtomicWrite(FileFor(alias), blob);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public bool Delete(string alias)
    {
        var file = FileFor(alias);
        if (!File.Exists(file))
        {
            return false;
        }

        File.Delete(file);
        return true;
    }
}

/// <summary>
/// macOS: one generic-password item per alias in the user's default (login) keychain. The service names this data
/// home, so two homes (a test's and the developer's) never share an item; the account is the alias. The item's
/// access list trusts only the SqlInvestigator executable that added it (ADR-007: application-scoped). Reads never
/// prompt: a read that would need the user's approval, or a locked keychain, fails closed as credential.unavailable.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class KeychainSecretStore(AppPaths paths) : ISecretStore
{
    private static readonly TimeSpan TimeBox = TimeSpan.FromSeconds(15);

    public string Mechanism => "keychain-login";

    /// <summary>"SqlInvestigator/1 " + the first 16 hex digits of SHA-256 over the data home's full path.</summary>
    public static string ServiceFor(string home) =>
        "SqlInvestigator/1 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(home)))).ToLowerInvariant()[..16];

    public static string LabelFor(string alias) => "SQL Investigator: " + alias;

    private string Service => ServiceFor(paths.Home);

    public string Read(string alias)
    {
        var status = MacKeychain.Read(Service, alias, allowUi: false, TimeBox, out var data);
        if (status == MacKeychain.ItemNotFound)
        {
            throw CredentialVault.NotStored(alias);
        }

        if (status != MacKeychain.Success || data is null)
        {
            throw new InvestigatorException(new InvestigatorError(
                "credential.unavailable",
                $"The login keychain did not release the credential for alias \"{alias}\": {MacKeychain.Describe(status)}. " +
                "The keychain is locked, or this SqlInvestigator executable is not the one that stored the item (after an update or a rebuild the keychain sees a different application).",
                $"Unlock the login keychain, then re-provision at the local console with this executable: SqlInvestigator provision --alias {alias} --replace ... " +
                $"(or, in Keychain Access, allow this SqlInvestigator for the item \"{LabelFor(alias)}\")."));
        }

        try
        {
            return Encoding.UTF8.GetString(data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    public void Write(string alias, string secret)
    {
        var clear = Encoding.UTF8.GetBytes(secret);
        try
        {
            var status = MacKeychain.Write(Service, alias, LabelFor(alias), clear);
            if (status != MacKeychain.Success)
            {
                throw new InvestigatorException(new InvestigatorError(
                    "credential.store-failed",
                    $"The login keychain did not store the credential for alias \"{alias}\": {MacKeychain.Describe(status)}. Nothing was provisioned.",
                    $"Unlock the login keychain and retry. If an older item is in the way, delete \"{LabelFor(alias)}\" in Keychain Access, or run: SqlInvestigator remove --alias {alias}."));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    public bool Delete(string alias)
    {
        var status = MacKeychain.Delete(Service, alias);
        if (status == MacKeychain.ItemNotFound)
        {
            return false;
        }

        if (status != MacKeychain.Success)
        {
            throw new InvestigatorException(new InvestigatorError(
                "credential.store-failed",
                $"The login keychain did not delete the credential for alias \"{alias}\": {MacKeychain.Describe(status)}.",
                $"Unlock the login keychain and retry, or delete \"{LabelFor(alias)}\" in Keychain Access."));
        }

        return true;
    }
}
