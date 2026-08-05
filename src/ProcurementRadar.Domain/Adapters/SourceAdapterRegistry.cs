namespace ProcurementRadar.Domain.Adapters;

public interface ISourceAdapterRegistry
{
    /// <summary>The adapter for a <c>source.adapter_key</c>, or null if nothing
    /// claims that key.</summary>
    ISourceAdapter? For(string adapterKey);

    IReadOnlyCollection<string> KnownKeys { get; }
}

public sealed class SourceAdapterRegistry : ISourceAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ISourceAdapter> _byKey;

    public SourceAdapterRegistry(IEnumerable<ISourceAdapter> adapters)
    {
        _byKey = adapters.ToDictionary(adapter => adapter.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> KnownKeys => (IReadOnlyCollection<string>)_byKey.Keys;

    public ISourceAdapter? For(string adapterKey)
        => _byKey.GetValueOrDefault(adapterKey);
}
