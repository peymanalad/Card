using Microsoft.Extensions.Configuration;

namespace Dario.Core.Abstraction.Card.Options;

public sealed class CardServicesOptions
{
    [ConfigurationKeyName("DatabaseProvider")]
    public string Provider { get; set; } = "Unknown";
    public string ConnectionString { get; set; } = string.Empty;
    public string? ConnectionStringQuery { get; set; }
    public string EncryptionKey { get; set; } = string.Empty;

    public void Validate()
    {
        if ((Provider.Equals("Oracle", StringComparison.OrdinalIgnoreCase) ||
             Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                $"CardServices:ConnectionString is required when Provider is '{Provider}'.");
        }
        if (string.IsNullOrWhiteSpace(EncryptionKey))
        {
            throw new InvalidOperationException("CardServices:EncryptionKey is required.");
        }
    }
}
