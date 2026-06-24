using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using rancher_devops_operator.Services;

using Xunit;

namespace rancher_devops_operator.Tests;

public class RancherApiServicePrincipalTests
{
    [Fact]
    public async Task GetPrincipalByNameAsync_MatchesLoginName()
    {
        var principalPayload = new
        {
            type = "collection",
            data = new[]
            {
                new
                {
                    id = "local://u-zdcml",
                    name = "claw",
                    loginName = "claw",
                    principalType = "user",
                },
            },
        };
        var json = JsonSerializer.Serialize(principalPayload);
        var parsed = JsonSerializer.Deserialize(json, rancher_devops_operator.RancherJsonSerializerContext.Default.RancherPrincipalList);
        Assert.NotNull(parsed);
        Assert.Single(parsed!.Data);
        Assert.Equal("claw", parsed.Data[0].LoginName);

        var handler = new StubHttpMessageHandler(json);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://rancher.example") };
        var factory = new ServiceCollection()
            .AddSingleton<IHttpClientFactory>(new SingletonHttpClientFactory(client))
            .BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();
        var service = new RancherApiService(factory, new NoopAuthService(), NullLogger<RancherApiService>.Instance);

        var principal = await service.GetPrincipalByNameAsync("claw", CancellationToken.None);
        Assert.True(handler.Called);

        Assert.NotNull(principal);
        Assert.Equal("local://u-zdcml", principal!.Id);
        Assert.Equal("claw", principal.LoginName);
    }

    private sealed class NoopAuthService : IRancherAuthService
    {
        public Task<System.Net.Http.Headers.AuthenticationHeaderValue> GetAuthorizationHeaderAsync(CancellationToken cancellationToken)
            => Task.FromResult(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test"));

        public void ConfigureHttpClient(HttpClient httpClient)
        {
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public bool Called { get; private set; }

        public StubHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SingletonHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingletonHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }
}
