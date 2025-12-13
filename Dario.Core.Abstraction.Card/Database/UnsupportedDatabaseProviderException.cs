namespace Dario.Core.Abstraction.Card.Database;

public sealed class UnsupportedDatabaseProviderException : Exception
{
    public string ProviderRaw { get; }

    public UnsupportedDatabaseProviderException(string providerRaw)
        : base($"Unsupported database provider '{providerRaw}'. Only 'Oracle' or 'SqlServer' are allowed.")
    {
        ProviderRaw = providerRaw;
    }
}