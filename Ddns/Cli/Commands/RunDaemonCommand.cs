using Ddns.Cli.Interactive;
using Ddns.Core;
using Ddns.Daemon;
using Ddns.Notifications;
using Spectre.Console.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ddns.Cli.Commands;

public sealed class RunDaemonCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<ProviderRegistry>();
        builder.Services.AddSingleton<IProviderRegistry>(services => services.GetRequiredService<ProviderRegistry>());
        builder.Services.AddSingleton<PublicIpService>();
        builder.Services.AddSingleton<WebhookNotifier>();
        builder.Services.AddSingleton<DdnsRunner>();
        builder.Services.AddHostedService<Worker>();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }
}
