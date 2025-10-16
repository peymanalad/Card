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
        return services;
    }
    public static IServiceCollection AddDarioCardServices(this IServiceCollection services, IConfiguration configuration, string sectionName)
    {
        services.AddDarioCardServices(configuration.GetSection(sectionName));
        return services;
    }

    public static IServiceCollection AddDarioCardServices(this IServiceCollection services, Action<CardServicesOptions> setupAction)
    {
        services.AddTransient<ICardServices, CardServices>();
        services.Configure(setupAction);
        return services;
    }
}
