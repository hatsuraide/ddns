using System.Text.Json;
using Ddns.Cli.Interactive;
using Ddns.Config;
using Ddns.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ddns.Cli.Commands;

public sealed class ConfigCommand : AsyncCommand<ConfigSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ConfigSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var workspace = new ConfigWorkspace(
                ConfigEnvironment.CreateStore(),
                new ProviderRegistry(httpClient),
                CliConsole.Output);
            return await workspace.RunAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
            return 1;
        }
    }
}

public sealed class ConfigExportCommand : Command<ConfigExportSettings>
{
    protected override int Execute(
        CommandContext context,
        ConfigExportSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var store = ConfigEnvironment.CreateStore();
            var loaded = store.LoadRequired();
            ConfigValidator.ThrowIfInvalid(loaded.Config);
            var json = JsonSerializer.Serialize(loaded.Config, ConfigStore.JsonOptions);

            if (string.IsNullOrWhiteSpace(settings.File))
            {
                Console.WriteLine(json);
                return 0;
            }

            ConfigStore.WriteAllTextAtomic(settings.File, json);
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
            return 1;
        }
    }
}

public sealed class ConfigSaveCommand : Command<ConfigSaveSettings>
{
    protected override ValidationResult Validate(CommandContext context, ConfigSaveSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.File)
            ? ValidationResult.Error("config save needs --file.")
            : ValidationResult.Success();
    }

    protected override int Execute(
        CommandContext context,
        ConfigSaveSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var store = ConfigEnvironment.CreateStore();
            var imported = ConfigStore.LoadFromFile(settings.File!, ConfigEnvironment.Secret);
            ConfigValidator.ThrowIfInvalid(imported.Config);

            if (imported.Format == ConfigFileFormat.Encrypted && !store.CanEncrypt)
                throw new InvalidOperationException(
                    "DDNS_SECRET is required to save an encrypted config file.");

            store.Save(imported.Config, imported.Format);
            CliConsole.Error.MarkupLineInterpolated($"Saved {store.ConfigPath}");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CliConsole.Error.MarkupLine(Markup.Escape(exception.Message));
            return 1;
        }
    }
}

public sealed class ConfigSettings : CommandSettings
{
}

public sealed class ConfigExportSettings : CommandSettings
{
    [CommandOption("--file <FILE>")] public string? File { get; init; }
}

public sealed class ConfigSaveSettings : CommandSettings
{
    [CommandOption("--file <FILE>")] public string? File { get; init; }
}
