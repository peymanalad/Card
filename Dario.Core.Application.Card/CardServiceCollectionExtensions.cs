using Dario.Core.Abstraction.Card;
using Dario.Core.Abstraction.Card.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rayanparsi.Extensions.Translations.Abstractions;
using System;

namespace Dario.Core.Application.Card;

public static class CardServiceCollectionExtensions
{
    public static IServiceCollection AddDarioCardServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<ICardServices, CardServices>();
        services.Configure<CardServicesOptions>(configuration);
        services.PostConfigure<CardServicesOptions>(ApplyEnvironmentOverrides);
        RegisterCardBinStatsService(services);
        return services;
    }
    public static IServiceCollection AddDarioCardServices(this IServiceCollection services, IConfiguration configuration, string sectionName)
    {
        return services.AddDarioCardServices(configuration.GetSection(sectionName));
    }

    public static IServiceCollection AddDarioCardServices(this IServiceCollection services, Action<CardServicesOptions> setupAction)
    {
        services.AddTransient<ICardServices, CardServices>();
        services.Configure(setupAction);
        services.PostConfigure<CardServicesOptions>(ApplyEnvironmentOverrides);
        RegisterCardBinStatsService(services);
        return services;
    }

    private static void ApplyEnvironmentOverrides(CardServicesOptions options)
    {
        options.DatabaseProvider = OverrideIfSet(options.DatabaseProvider, "DB_PROVIDER");
        options.ConnectionString = OverrideIfSet(options.ConnectionString, "DB_CONNECTION_STRING");
        options.ConnectionStringQuery = OverrideIfSet(options.ConnectionStringQuery, "DB_QUERY_CONNECTION_STRING");
        options.SqlConnectionString = OverrideIfSet(options.SqlConnectionString, "DB_SQL_CONNECTION_STRING");
        options.SqlConnectionStringQuery = OverrideIfSet(options.SqlConnectionStringQuery, "DB_SQL_QUERY_CONNECTION_STRING");
        options.EncryptionKey = OverrideIfSet(options.EncryptionKey, "CARD_ENCRYPTION_KEY");
    }
    private static void RegisterCardBinStatsService(IServiceCollection services)
    {
        services.AddScoped<ICardBinStatsService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CardServicesOptions>>().Value;

            var shouldUseSqlServer = ShouldUseSqlServer(options);
            if (shouldUseSqlServer)
            {
                return ActivatorUtilities.CreateInstance<SqlCardBinStatsService>(sp);
            }

            return ActivatorUtilities.CreateInstance<OracleCardBinStatsService>(sp);
        });
    }

    private static bool ShouldUseSqlServer(CardServicesOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabaseProvider))
        {
            return options.DatabaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(options.SqlConnectionString);
    }

    private static string OverrideIfSet(string currentValue, string environmentVariable)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(environmentValue)
            ? currentValue
            : environmentValue;
    }
}
