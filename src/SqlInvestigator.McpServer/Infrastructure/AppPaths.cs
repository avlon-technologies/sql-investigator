namespace SqlInvestigator.Infrastructure;

/// <summary>
/// Where the four local stores live. One directory per user, current-user-only (see <see cref="SecureFiles"/>).
/// SQLINVESTIGATOR_HOME overrides the location (tests and acceptance scenarios use a disposable one).
/// </summary>
internal sealed class AppPaths
{
    public AppPaths(string home)
    {
        Home = Path.GetFullPath(home);
    }

    public string Home { get; }

    /// <summary>ConnectionCatalog: alias metadata and approved scope. Written only by ConnectionRegistry.</summary>
    public string CatalogFile => Path.Combine(Home, "catalog.json");

    /// <summary>InvestigationHistory: investigations, query evidence and the gateway's audit trail. Written only by QueryGateway.</summary>
    public string HistoryFile => Path.Combine(Home, "history.json");

    /// <summary>KnowledgeStore: schema snapshots and relationships. Written only by KnowledgeService.</summary>
    public string KnowledgeFile => Path.Combine(Home, "knowledge.json");

    /// <summary>CredentialVault on Windows: one DPAPI CurrentUser blob per alias (on macOS the items live in the login keychain). Written only by the local provisioning console.</summary>
    public string VaultDirectory => Path.Combine(Home, "vault");

    public static AppPaths FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("SQLINVESTIGATOR_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new AppPaths(configured);
        }

        return new AppPaths(Path.Combine(DefaultParent(), "SqlInvestigator", "data"));
    }

    /// <summary>%LOCALAPPDATA% on Windows; ~/Library/Application Support on macOS, stated rather than left to the runtime's mapping.</summary>
    private static string DefaultParent()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support");
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
}
