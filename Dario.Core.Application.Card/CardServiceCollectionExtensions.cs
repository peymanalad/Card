using Dario.Core.Abstraction.Card;
using Dario.Core.Abstraction.Card.Options;
using Dario.Core.Application.Card.Db;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dario.Core.Application.Card;

public static class CardServiceCollectionExtensions
{
    public static IServiceCollection AddCardApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CardServicesOptions>(
            configuration.GetSection("CardServices"));

        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        services.AddTransient<ICardServices, CardServices>();
        services.AddScoped<ICardBinStatsService>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CardServicesOptions>>().Value;
            var provider = Dario.Core.Abstraction.Card.Database.DatabaseProviderParser.Parse(options.Provider);
            return provider == Dario.Core.Abstraction.Card.Database.DatabaseProvider.SqlServer
                ? ActivatorUtilities.CreateInstance<SqlCardBinStatsService>(sp)
                : ActivatorUtilities.CreateInstance<OracleCardBinStatsService>(sp);
        });

        return services;
    }
}
