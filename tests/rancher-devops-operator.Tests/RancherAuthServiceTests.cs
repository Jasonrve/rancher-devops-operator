using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using rancher_devops_operator.Services;

using Xunit;

namespace rancher_devops_operator.Tests;

public class RancherAuthServiceTests
{
    [Fact]
    public async Task PassThroughAuthorizationHeader_WinsOverConfiguredToken()
    {
        var authContext = new RancherRequestAuthContext();
        var factory = new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rancher:Url"] = "https://rancher.example",
            ["Rancher:Token"] = "configured-token",
        }).Build();

        var service = new RancherAuthService(factory, config, authContext, NullLogger<RancherAuthService>.Instance);
        using var _ = authContext.Push("Bearer pass-through-token");

        var header = await service.GetAuthorizationHeaderAsync(CancellationToken.None);

        Assert.Equal("Bearer", header.Scheme);
        Assert.Equal("pass-through-token", header.Parameter);
    }

    [Fact]
    public async Task ConfiguredToken_IsUsedWhenNoPassThroughHeaderExists()
    {
        var authContext = new RancherRequestAuthContext();
        var factory = new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rancher:Url"] = "https://rancher.example",
            ["Rancher:Token"] = "configured-token",
        }).Build();

        var service = new RancherAuthService(factory, config, authContext, NullLogger<RancherAuthService>.Instance);

        var header = await service.GetAuthorizationHeaderAsync(CancellationToken.None);

        Assert.Equal("Bearer", header.Scheme);
        Assert.Equal("configured-token", header.Parameter);
    }
}
