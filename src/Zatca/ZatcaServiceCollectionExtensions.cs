using System;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers <see cref="Zatca.ZatcaClient"/> in the DI container.
    /// </summary>
    public static class ZatcaServiceCollectionExtensions
    {
        public static IServiceCollection AddZatca(
            this IServiceCollection services, Zatca.ZatcaClientOptions options)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            services.TryAddSingleton(options ?? throw new ArgumentNullException(nameof(options)));
            services.TryAddSingleton<Zatca.ZatcaClient>();
            return services;
        }

        public static IServiceCollection AddZatca(
            this IServiceCollection services, Action<Zatca.ZatcaClientOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var options = new Zatca.ZatcaClientOptions();
            configure(options);
            return services.AddZatca(options);
        }
    }
}
