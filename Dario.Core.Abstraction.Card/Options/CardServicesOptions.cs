namespace Dario.Core.Abstraction.Card.Options;
public class CardServicesOptions
{
    public string DatabaseProvider { get; set; } = string.Empty;
    public string ServiceIP { get; set; } = string.Empty;

    public int ServicePort = 10010;
    public string ConnectionString { get; set; } = string.Empty;
    public string ConnectionStringQuery { get; set; } = string.Empty;
    public string SqlConnectionString { get; set; } = string.Empty;
    public string SqlConnectionStringQuery { get; set; } = string.Empty;
    public string EncryptionKey { get; set; } = string.Empty;
}
