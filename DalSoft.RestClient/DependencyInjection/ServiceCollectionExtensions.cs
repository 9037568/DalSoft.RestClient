using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DalSoft.RestClient.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static RestClientFactoryConfig AddRestClient(this IServiceCollection services, string baseUri)
        {
            return AddRestClient(services, baseUri, (Headers) null);
        }
        
        public static RestClientFactoryConfig AddRestClient(this IServiceCollection services, string baseUri, Headers defaultRequestHeaders)
        {
            return  services.AddRestClient(RestClientFactory.DefaultClientName, baseUri, defaultRequestHeaders);
        }
        
        public static RestClientFactoryConfig AddRestClient(this IServiceCollection services, string name, string baseUri)
        {
            return services.AddRestClient(name, baseUri, null);
        }
        
        public static RestClientFactoryConfig AddRestClient(this IServiceCollection services, string name, string baseUri, Headers defaultRequestHeaders)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name cannot be empty", nameof(name));
            
            services.TryAddSingleton<IRestClientFactory, RestClientFactory>();

            var httpClientBuilder = services.AddHttpClient(name);
            var config = new RestClientFactoryConfig(httpClientBuilder); 
            
            services.AddSingleton(serviceProvider => new RestClientContainer(serviceProvider, name, baseUri, defaultRequestHeaders));

            return config;
        }

        /// <summary>Registers a typed client, TClient is resolved transiently with a configured IRestClient (or RestClient) injected into its constructor</summary>
        public static RestClientFactoryConfig AddRestClient<TClient>(this IServiceCollection services, string baseUri) where TClient : class
        {
            return services.AddRestClient<TClient, TClient>(baseUri, null);
        }

        public static RestClientFactoryConfig AddRestClient<TClient>(this IServiceCollection services, string baseUri, Headers defaultRequestHeaders) where TClient : class
        {
            return services.AddRestClient<TClient, TClient>(baseUri, defaultRequestHeaders);
        }

        public static RestClientFactoryConfig AddRestClient<TClient, TImplementation>(this IServiceCollection services, string baseUri) where TClient : class where TImplementation : class, TClient
        {
            return services.AddRestClient<TClient, TImplementation>(baseUri, null);
        }

        public static RestClientFactoryConfig AddRestClient<TClient, TImplementation>(this IServiceCollection services, string baseUri, Headers defaultRequestHeaders) where TClient : class where TImplementation : class, TClient
        {
            var name = GetTypedClientName<TClient>();
            var config = services.AddRestClient(name, baseUri, defaultRequestHeaders);

            services.AddTransient<TClient>(serviceProvider =>
            {
                var restClient = serviceProvider.GetRequiredService<IRestClientFactory>().CreateClient(name);
                return ActivatorUtilities.CreateInstance<TImplementation>(serviceProvider, restClient); // Satisfies both IRestClient and RestClient ctor parameters
            });

            return config;
        }

        internal static string GetTypedClientName<TClient>() => typeof(TClient).FullName;
    }
}
