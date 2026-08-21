using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using DalSoft.RestClient.Handlers;
using DalSoft.RestClient.Handlers.Mcp;
using DalSoft.RestClient.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;

namespace DalSoft.RestClient.DependencyInjection
{
    public static class RestClientFactoryRestClientFactoryConfigExtensions
    {
        public static RestClientFactoryConfig SetJsonSerializerOptions(this RestClientFactoryConfig config, JsonSerializerOptions jsonSerializerOptions)
        {
            config.JsonSerializer = new SystemTextJsonSerializer(jsonSerializerOptions);
            return config;
        }

        public static RestClientFactoryConfig UseNewtonsoftJson(this RestClientFactoryConfig config, JsonSerializerSettings jsonSerializerSettings = null)
        {
            config.JsonSerializer = new NewtonsoftJsonSerializer(jsonSerializerSettings);
            return config;
        }
        
        public static RestClientFactoryConfig UseNoDefaultHandlers(this RestClientFactoryConfig config)
        {
            config.UseDefaultHandlers = false;
            return config;
        }

        public static RestClientFactoryConfig UseHandler(this RestClientFactoryConfig config, Func<DelegatingHandler> handler)
        {
            config.HttpClientBuilder.AddHttpMessageHandler(handler);

            return config;
        }
        
        public static RestClientFactoryConfig UseHandler(this RestClientFactoryConfig config, Func<HttpRequestMessage, CancellationToken, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>, Task<HttpResponseMessage>> handler)
        {
            DelegatingHandler HandlerFactory() => new DelegatingHandlerWrapper(handler);

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        [Obsolete("Use UseHttpClientHandler(RestClientFactoryConfig, Action<HttpClientHandler>) overload instead")]
        public static RestClientFactoryConfig UseHttpClientHandler(this RestClientFactoryConfig config, Func<HttpClientHandler> handler)
        {
            config.HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(handler);

            return config;
        }

        public static RestClientFactoryConfig UseHttpClientHandler(this RestClientFactoryConfig config, Action<HttpClientHandler> httpClientHandlerOptions)
        {
            HttpClientHandler ConfigureHttpClientHandler(IServiceProvider provider) // Delegate called at Factory creation time
            {
                var clientHandler = provider.GetRequiredService<HttpClientHandlerWrapper>();
                httpClientHandlerOptions(clientHandler);

                return clientHandler;
            }

            config.HttpClientBuilder.Services.TryAddTransient<HttpClientHandlerWrapper>();
            config.HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(ConfigureHttpClientHandler);

            return config;
        }

        public static RestClientFactoryConfig UseUnitTestHandler(this RestClientFactoryConfig config)
        {
            DelegatingHandler HandlerFactory() => new UnitTestHandler();

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseUnitTestHandler(this RestClientFactoryConfig config, Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            DelegatingHandler HandlerFactory() => new UnitTestHandler(handler);

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseUnitTestHandler(this RestClientFactoryConfig config, Action<HttpRequestMessage> handler)
        {
            DelegatingHandler HandlerFactory() => new UnitTestHandler(handler);

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseFormUrlEncodedHandler(this RestClientFactoryConfig config)
        {
            DelegatingHandler HandlerFactory() => new FormUrlEncodedHandler();

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseMultipartFormDataHandler(this RestClientFactoryConfig config)
        {
            DelegatingHandler HandlerFactory() => new MultipartFormDataHandler();

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseRetryHandler(this RestClientFactoryConfig config)
        {
            DelegatingHandler HandlerFactory() => new RetryHandler();

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseRetryHandler(this RestClientFactoryConfig config, int maxRetries, double waitToRetryInSeconds, double maxWaitToRetryInSeconds, RetryHandler.BackOffStrategy backOffStrategy)
        {
            DelegatingHandler HandlerFactory() => new RetryHandler(maxRetries, waitToRetryInSeconds, maxWaitToRetryInSeconds, backOffStrategy);

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        /// <summary>Talk to an MCP server over the Streamable HTTP transport, the session is shared so it survives IHttpClientFactory handler rotation</summary>
        public static RestClientFactoryConfig UseMcpHandler(this RestClientFactoryConfig config, McpHandlerOptions options = null, McpSession session = null)
        {
            session = session ?? new McpSession(); // One session per registration, not per handler instance
            DelegatingHandler HandlerFactory() => new McpHandler(options, session);

            config.HttpClientBuilder.AddHttpMessageHandler(HandlerFactory);

            return config;
        }

        public static RestClientFactoryConfig UseCookieHandler(this RestClientFactoryConfig config)
        {
            return UseCookieHandler(config, new CookieContainer());
        }

        public static RestClientFactoryConfig UseCookieHandler(this RestClientFactoryConfig config, CookieContainer cookieContainer)
        {
            config.UseHttpClientHandler(httpClientHandler => { httpClientHandler.CookieContainer = cookieContainer; });

            DelegatingHandler HandlerFactory() => new CookieHandler();
            config.UseHandler(HandlerFactory);

            return config;
        }
    }
}
