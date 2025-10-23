using System;
using Dario.Core.Abstraction.Card;
using Dario.Core.Abstraction.Card.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rayanparsi.Extensions.Translations.Abstractions;

namespace Dario.Core.Application.Card;

public static class CardServiceCollectionExtensions
{
    public static IServiceCollection AddDarioCardServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<ICardServices, CardServices>();
        services.Configure<CardServicesOptions>(configuration);
        services.PostConfigure<CardServicesOptions>(ApplyEnvironmentOverrides);
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
        return services;
    }

    private static void ApplyEnvironmentOverrides(CardServicesOptions options)
    {
        options.ConnectionString = OverrideIfSet(options.ConnectionString, "DB_CONNECTION_STRING");
        options.ConnectionStringQuery = OverrideIfSet(options.ConnectionStringQuery, "DB_QUERY_CONNECTION_STRING");
        options.EncryptionKey = OverrideIfSet(options.EncryptionKey, "CARD_ENCRYPTION_KEY");
    }

    private static string OverrideIfSet(string currentValue, string environmentVariable)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(environmentValue)
            ? currentValue
            : environmentValue;
    }
}
