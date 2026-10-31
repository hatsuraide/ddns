using Ddns.Cli.Interactive;
using Ddns.Config;
using Ddns.Core;
using Ddns.Notifications;
using Spectre.Console;
using Spectre.Console.Cli;
using Microsoft.Extensions.Logging;

namespace Ddns.Cli.Commands;

public sealed class CheckCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        try
        {
            var store = ConfigEnvironment.CreateStore();
            var loaded = store.LoadRequired();
            ConfigValidator.ThrowIfInvalid(loaded.Config);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                });
                builder.SetMinimumLevel(LogLevel.Information);
            });

            var runner = new DdnsRunner(
                new ProviderRegistry(httpClient),
                new PublicIpService(httpClient),
                new WebhookNotifier(httpClient, loggerFactory.CreateLogger<WebhookNotifier>()),
                loggerFactory.CreateLogger<DdnsRunner>());

            var result = await runner.RunOnceAsync(
                loaded.Config, ConfigEnvironment.IpServiceUrl, cancellationToken);

            return result.PublicAddress is null ? 1 : 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
            return 1;
        }
    }
}
