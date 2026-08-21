using System.Threading.Tasks;
using DalSoft.RestClient.DependencyInjection;
using DalSoft.RestClient.Test.Integration.TestModels;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace DalSoft.RestClient.Test.Integration
{
    [TestFixture]
    public class TypedClientTests
    {
        private const string BaseUri = "http://jsonplaceholder.typicode.com";

        public class JsonPlaceholderClient
        {
            private readonly IRestClient _restClient;

            public JsonPlaceholderClient(IRestClient restClient)
            {
                _restClient = restClient;
            }

            public Task<User> GetUser(int id) => _restClient.Resource("users").Resource(id.ToString()).Get<User>();
        }

        [Test]
        public async Task AddRestClientTClient_ResolvedFromServiceProvider_CallsRealApi()
        {
            var services = new ServiceCollection();
            services.AddRestClient<JsonPlaceholderClient>(BaseUri, new Headers(new { UserAgent = "DalSoft.RestClient.Test.Integration" }));

            var client = services.BuildServiceProvider().GetRequiredService<JsonPlaceholderClient>();

            var user = await client.GetUser(1);

            Assert.That(user.id, Is.EqualTo(1));
            Assert.That(user.username, Is.Not.Empty);
        }
    }
}
