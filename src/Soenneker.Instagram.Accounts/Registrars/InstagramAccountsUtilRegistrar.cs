using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Instagram.Accounts.Abstract;
using Soenneker.Instagram.OpenApiClientUtil.Registrars;

namespace Soenneker.Instagram.Accounts.Registrars;

/// <summary>
/// An Instagram account utility for publishing images, carousels, and reels.
/// </summary>
public static class InstagramAccountsUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IInstagramAccountsUtil"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddInstagramAccountsUtilAsSingleton(this IServiceCollection services)
    {
        services.AddInstagramOpenApiClientUtilAsSingleton().TryAddSingleton<IInstagramAccountsUtil, InstagramAccountsUtil>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IInstagramAccountsUtil"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddInstagramAccountsUtilAsScoped(this IServiceCollection services)
    {
        services.AddInstagramOpenApiClientUtilAsScoped().TryAddScoped<IInstagramAccountsUtil, InstagramAccountsUtil>();

        return services;
    }
}
