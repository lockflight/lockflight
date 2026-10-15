using DotNet.Testcontainers.Builders;

namespace Shop.Tests;

/// <summary>Needs a Docker daemon; probes container isolation limits.</summary>
public sealed class ContainerSmokeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Container_ServesHttp()
    {
        await using var container = new ContainerBuilder("nginx:1.27-alpine")
            .WithPortBinding(80, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(80)))
            .Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient();
        var response = await client.GetAsync(new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(80)}/"), TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }
}
