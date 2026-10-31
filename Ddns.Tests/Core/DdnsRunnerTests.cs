using System.Net;
using System.Text;
using System.Text.Json;
using Ddns.Config;
using Ddns.Core;
using Ddns.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ddns.Tests.Core;

public sealed class DdnsRunnerTests
{
    [Fact]
    public async Task RunOnce_UpdatesNotifiesAndSkipsTheSameAddressOnTheNextRun()
    {
        var provider = new FakeProvider();
        var webhooks = new List<string>();
        var runner = CreateRunner(provider, webhooks);
        var config = CreateConfig();

        var first = await runner.RunOnceAsync(config);
        Assert.Equal(1, first.Updated);
        Assert.Equal(1, provider.UpdateCalls);
        Assert.Contains("home.example.com", Assert.Single(webhooks));

        var second = await runner.RunOnceAsync(config);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(1, provider.UpdateCalls);
        Assert.Single(webhooks);
    }

    [Fact]
    public async Task RunOnce_DoesNotNotifyWhenProviderReportsUnchanged()
    {
        var provider = new FakeProvider(DnsUpdateResult.Unchanged("203.0.113.10"));
        var webhooks = new List<string>();
        var runner = CreateRunner(provider, webhooks);

        var result = await runner.RunOnceAsync(CreateConfig());

        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Unchanged);
        Assert.Empty(webhooks);
    }

    [Fact]
    public async Task RunOnce_NotifiesOnProviderFailure()
    {
        var provider = new FakeProvider(exception: new InvalidOperationException("boom"));
        var webhooks = new List<string>();
        var runner = CreateRunner(provider, webhooks);

        var result = await runner.RunOnceAsync(CreateConfig());

        Assert.Equal(1, result.Failed);
        Assert.Contains("boom", Assert.Single(webhooks));
    }

    [Fact]
    public async Task RunOnce_NotifiesGlobalWebhookWhenIpLookupFails()
    {
        var provider = new FakeProvider();
        var webhooks = new List<string>();
        var runner = CreateRunner(
            provider,
            webhooks,
            ipResponse: new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await runner.RunOnceAsync(CreateConfig());

        Assert.Null(result.PublicAddress);
        Assert.Equal(0, provider.UpdateCalls);
        Assert.Contains("public IP", Assert.Single(webhooks));
    }

    private static DdnsRunner CreateRunner(
        IDnsProvider provider,
        List<string> webhookBodies,
        HttpResponseMessage? ipResponse = null)
    {
        var ipClient = new HttpClient(new StubHandler(_ => Task.FromResult(
            ipResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("203.0.113.10")
            })));

        var webhookClient = new HttpClient(new StubHandler(async request =>
        {
            webhookBodies.Add(await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        return new DdnsRunner(
            new FakeRegistry(provider),
            new PublicIpService(ipClient),
            new WebhookNotifier(webhookClient, NullLogger<WebhookNotifier>.Instance),
            NullLogger<DdnsRunner>.Instance);
    }

    private static ConfigModel CreateConfig()
    {
        return new ConfigModel
        {
            GlobalWebhook = "https://example.com/hook",
            Timeout = TimeSpan.FromMinutes(5),
            Accounts =
            [
                new ConfigAccount
                {
                    Type = "fake",
                    Name = "test",
                    ApiKey = "key",
                    Records = [new ConfigRecord { Domain = "home.example.com" }]
                }
            ]
        };
    }

    private sealed class FakeProvider(
        DnsUpdateResult? result = null,
        Exception? exception = null) : IDnsProvider
    {
        public int UpdateCalls { get; private set; }

        public string Type => "fake";

        public Task ValidateAsync(ConfigAccount account, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiscoveredTarget>> DiscoverAsync(
            ConfigAccount account,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DiscoveredTarget>>([]);
        }

        public Task<DnsUpdateResult> UpdateAsync(
            ConfigAccount account,
            ConfigRecord record,
            IPAddress ipAddress,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            if (exception is not null) throw exception;

            return Task.FromResult(result ?? DnsUpdateResult.Updated("203.0.113.1"));
        }
    }

    private sealed class FakeRegistry(IDnsProvider provider) : IProviderRegistry
    {
        public IReadOnlyList<string> SupportedTypes => ["fake"];

        public IDnsProvider Create(ConfigAccount account, int defaultApiVersion)
        {
            return provider;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }
}
