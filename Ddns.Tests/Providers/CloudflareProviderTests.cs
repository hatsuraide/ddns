using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ddns.Config;
using Ddns.Core;
using Ddns.Providers;

namespace Ddns.Tests.Providers;

public sealed class CloudflareProviderTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesChangedAddressAndKeepsRecordSettings()
    {
        string? updateBody = null;
        var httpClient = new HttpClient(new StubHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Put && path == "/client/v4/zones/zone-1/dns_records/rec-1")
            {
                updateBody = await request.Content!.ReadAsStringAsync();
                return JsonResponse(
                    """{"success":true,"errors":[],"result":{"id":"rec-1","name":"home.example.com","type":"A","content":"203.0.113.20","ttl":120,"proxied":true}}""");
            }

            if (request.Method == HttpMethod.Get && path == "/client/v4/zones/zone-1/dns_records/rec-1")
                return JsonResponse(
                    """{"success":true,"errors":[],"result":{"id":"rec-1","name":"home.example.com","type":"A","content":"203.0.113.10","ttl":120,"proxied":true}}""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var provider = new CloudflareProvider(httpClient);
        var account = CreateAccount();
        var record = new ConfigRecord
        {
            Domain = "home.example.com",
            ZoneId = "zone-1",
            RecordId = "rec-1"
        };

        var result = await provider.UpdateAsync(account, record, IPAddress.Parse("203.0.113.20"));

        Assert.True(result.Changed);
        Assert.Equal("203.0.113.10", result.PreviousAddress);

        using var document = JsonDocument.Parse(updateBody!);
        Assert.Equal("A", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("home.example.com", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("203.0.113.20", document.RootElement.GetProperty("content").GetString());
        Assert.Equal(120, document.RootElement.GetProperty("ttl").GetInt32());
        Assert.True(document.RootElement.GetProperty("proxied").GetBoolean());
    }

    [Fact]
    public async Task UpdateAsync_SkipsPutWhenAddressIsUnchanged()
    {
        var putCount = 0;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put) putCount++;

            if (request.Method == HttpMethod.Get && path == "/client/v4/zones/zone-1/dns_records/rec-1")
                return Task.FromResult(JsonResponse(
                    """{"success":true,"errors":[],"result":{"id":"rec-1","name":"home.example.com","type":"A","content":"203.0.113.10","ttl":1,"proxied":false}}"""));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }));

        var provider = new CloudflareProvider(httpClient);
        var result = await provider.UpdateAsync(
            CreateAccount(),
            new ConfigRecord { Domain = "home.example.com", ZoneId = "zone-1", RecordId = "rec-1" },
            IPAddress.Parse("203.0.113.10"));

        Assert.False(result.Changed);
        Assert.Equal(0, putCount);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsOnlyAddressRecords()
    {
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/client/v4/zones")
                return Task.FromResult(JsonResponse(
                    """{"success":true,"errors":[],"result":[{"id":"zone-1","name":"example.com"}],"result_info":{"page":1,"per_page":50,"count":1,"total_count":1,"total_pages":1}}"""));

            if (path == "/client/v4/zones/zone-1/dns_records")
                return Task.FromResult(JsonResponse(
                    """
                    {"success":true,"errors":[],"result":[
                      {"id":"rec-a","name":"home.example.com","type":"A","content":"203.0.113.10","ttl":1,"proxied":false},
                      {"id":"rec-txt","name":"example.com","type":"TXT","content":"hello","ttl":1,"proxied":false}
                    ],"result_info":{"page":1,"per_page":100,"count":2,"total_count":2,"total_pages":1}}
                    """));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }));

        var discovered = await new CloudflareProvider(httpClient).DiscoverAsync(CreateAccount());

        var target = Assert.Single(discovered);
        Assert.Equal("home.example.com", target.Domain);
        Assert.Equal("zone-1", target.ZoneId);
        Assert.Equal("rec-a", target.RecordId);
    }

    [Fact]
    public async Task UpdateAsync_ResolvesZoneFromLongestMatchingName()
    {
        string? matchedPath = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/client/v4/zones")
                return Task.FromResult(JsonResponse(
                    """
                    {"success":true,"errors":[],"result":[
                      {"id":"zone-root","name":"example.com"},
                      {"id":"zone-sub","name":"sub.example.com"}
                    ],"result_info":{"page":1,"per_page":50,"count":2,"total_count":2,"total_pages":1}}
                    """));

            if (path.StartsWith("/client/v4/zones/", StringComparison.Ordinal)
                && path.EndsWith("/dns_records/rec-1", StringComparison.Ordinal))
            {
                matchedPath = path;
                return Task.FromResult(JsonResponse(
                    """{"success":true,"errors":[],"result":{"id":"rec-1","name":"host.sub.example.com","type":"A","content":"203.0.113.10","ttl":1,"proxied":false}}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }));

        var result = await new CloudflareProvider(httpClient).UpdateAsync(
            CreateAccount(),
            new ConfigRecord { Domain = "host.sub.example.com", RecordId = "rec-1" },
            IPAddress.Parse("203.0.113.10"));

        Assert.False(result.Changed);
        Assert.Equal("/client/v4/zones/zone-sub/dns_records/rec-1", matchedPath);
    }

    [Fact]
    public async Task ValidateAsync_SendsBearerToken()
    {
        string? authorization = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(JsonResponse(
                """{"success":true,"errors":[],"result":[],"result_info":{"page":1,"per_page":1,"count":0,"total_count":0,"total_pages":1}}"""));
        }));

        await new CloudflareProvider(httpClient).ValidateAsync(CreateAccount());

        Assert.Equal("Bearer cf-token", authorization);
    }

    private static ConfigAccount CreateAccount()
    {
        return new ConfigAccount
        {
            Type = ProviderTypes.Cloudflare,
            Name = "cf-main",
            ApiKey = "cf-token"
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
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
