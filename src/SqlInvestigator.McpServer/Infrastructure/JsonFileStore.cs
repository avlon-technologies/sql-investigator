using System.Text.Json;

namespace SqlInvestigator.Infrastructure;

/// <summary>
/// One local JSON store: System.Text.Json, atomic replacement, one writer. Each store has exactly one owning
/// service (CLAUDE.md), which is the only code that holds a writable instance; readers use <see cref="Read"/>.
/// </summary>
internal sealed class JsonFileStore<T>(string path) where T : class, new()
{
    public string Path { get; } = path;

    /// <summary>
    /// The current document; an empty one when the file does not exist yet. A file that exists but cannot be read
    /// is never treated as empty: that would silently drop a policy or a history. It fails closed.
    /// </summary>
    public T Read()
    {
        if (!File.Exists(Path))
        {
            return new T();
        }

        try
        {
            var bytes = File.ReadAllBytes(Path);
            return JsonSerializer.Deserialize<T>(bytes, Json.StoreOptions) ?? throw new JsonException("the document is null");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvestigatorException(new InvestigatorError(
                "store.unreadable",
                $"The local store {System.IO.Path.GetFileName(Path)} cannot be read ({e.GetType().Name}).",
                "Restore the file from a backup or remove it deliberately; nothing was assumed in its place."));
        }
    }

    /// <summary>Read, change and write back under the store's lock. Returns what the change returned.</summary>
    public TResult Update<TResult>(Func<T, TResult> change)
    {
        return SecureFiles.WithFileLock(Path, () =>
        {
            var document = Read();
            var result = change(document);
            SecureFiles.AtomicWrite(Path, JsonSerializer.SerializeToUtf8Bytes(document, Json.StoreOptions));
            return result;
        });
    }
}
