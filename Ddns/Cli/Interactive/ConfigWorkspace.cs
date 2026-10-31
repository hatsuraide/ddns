using System.Net;
using System.Text.Json;
using Ddns.Config;
using Ddns.Core;
using Spectre.Console;

namespace Ddns.Cli.Interactive;

public sealed class ConfigWorkspace
{
    private readonly IAnsiConsole _console;
    private readonly ProviderRegistry _providerRegistry;
    private readonly ConfigStore _store;

    public ConfigWorkspace(ConfigStore store, ProviderRegistry providerRegistry, IAnsiConsole console)
    {
        _store = store;
        _providerRegistry = providerRegistry;
        _console = console;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var config = _store.Load() ?? new ConfigModel();
        var dirty = false;

        while (true)
        {
            _console.WriteLine();
            WriteSummary(config, dirty);

            switch (PromptMainMenu(config))
            {
                case MainMenuAction.AddAccount:
                {
                    dirty = await AddAccountAsync(config, cancellationToken) || dirty;
                    break;
                }
                case MainMenuAction.ImportPlainJson:
                {
                    var imported = ImportPlainJson();
                    if (imported is not null)
                    {
                        config = imported;
                        dirty = true;
                    }

                    break;
                }
                case MainMenuAction.Accounts:
                {
                    dirty = await RunAccountsMenuAsync(config, cancellationToken) || dirty;
                    break;
                }
                case MainMenuAction.GlobalSettings:
                {
                    dirty = RunGlobalSettings(config) || dirty;
                    break;
                }
                case MainMenuAction.ReviewEffectiveConfig:
                {
                    WriteEffectiveConfig(config);
                    PromptToContinue();
                    break;
                }
                case MainMenuAction.ExportPlainJson:
                {
                    ExportPlainJsonFromWorkspace(config);
                    break;
                }
                case MainMenuAction.Save:
                {
                    if (Save(config)) dirty = false;
                    break;
                }
                case MainMenuAction.Exit:
                case MainMenuAction.ExitWithoutSaving:
                {
                    if (!dirty || _console.Confirm("Exit without saving changes?", false)) return 0;
                    break;
                }
            }
        }
    }

    private MainMenuAction PromptMainMenu(ConfigModel config)
    {
        var choices = config.Accounts.Count == 0
            ? new[]
            {
                MainMenuAction.AddAccount,
                MainMenuAction.ImportPlainJson,
                MainMenuAction.GlobalSettings,
                MainMenuAction.Exit
            }
            : new[]
            {
                MainMenuAction.Accounts,
                MainMenuAction.GlobalSettings,
                MainMenuAction.ReviewEffectiveConfig,
                MainMenuAction.ExportPlainJson,
                MainMenuAction.Save,
                MainMenuAction.ExitWithoutSaving
            };

        return _console.Prompt(
            new SelectionPrompt<MainMenuAction>()
                .Title("Action")
                .AddChoices(choices)
                .UseConverter(PromptLabels.MainMenuLabel));
    }

    private async Task<bool> RunAccountsMenuAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        while (true)
        {
            _console.WriteLine();
            _console.MarkupLine("[bold]Accounts[/]");

            var choices = new List<MenuOption<AccountMenuSelection>>
            {
                new(new AccountMenuSelection(AccountMenuAction.AddAccount),
                    PromptLabels.AccountMenuLabel(AccountMenuAction.AddAccount))
            };
            choices.AddRange(config.Accounts.Select(account =>
                new MenuOption<AccountMenuSelection>(
                    new AccountMenuSelection(AccountMenuAction.EditAccount, account),
                    $"{account.Name} ({PromptLabels.ProviderLabel(account.Type)}, {account.Records.Count} records)")));
            choices.Add(new MenuOption<AccountMenuSelection>(
                new AccountMenuSelection(AccountMenuAction.Back),
                PromptLabels.AccountMenuLabel(AccountMenuAction.Back)));

            var selected = _console.Prompt(
                new SelectionPrompt<MenuOption<AccountMenuSelection>>()
                    .Title("Account")
                    .AddChoices(choices));

            if (selected.Value.Action == AccountMenuAction.AddAccount)
            {
                if (await AddAccountAsync(config, cancellationToken)) return true;
            }
            else if (selected.Value.Action == AccountMenuAction.Back)
            {
                return false;
            }
            else if (selected.Value.Account is not null
                     && await EditAccountAsync(config, selected.Value.Account, cancellationToken))
            {
                return true;
            }
        }
    }

    private async Task<bool> AddAccountAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _console.MarkupLine("[bold]Add provider account[/]");

        var type = PromptProviderType();
        var name = PromptRequired(type == ProviderTypes.Mittwald
            ? "Account label / mittwald account name:"
            : "Account label:");

        if (config.Accounts.Any(account =>
                string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _console.MarkupLine("[red]Account name must be unique.[/]");
            return false;
        }

        var account = new ConfigAccount
        {
            Type = type,
            Name = name,
            ApiKey = PromptSecret(type == ProviderTypes.Cloudflare ? "Cloudflare API token:" : "Mittwald API key:")
        };

        if (type == ProviderTypes.Mittwald)
            account.ApiVersion = PromptApiVersionOverride("API version (global default when unset)");

        IDnsProvider provider;
        try
        {
            provider = _providerRegistry.Create(account, config.DefaultApiVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _console.WriteLine($"Cannot create provider: {exception.Message}");
            return false;
        }

        if (!await ValidateCredentialsAsync(config, provider, account, cancellationToken)) return false;

        account.Records = (await DiscoverWithFallbackAsync(provider, account, null, cancellationToken)).ToList();
        account.Webhook = EmptyToNull(PromptOptionalUrl("Webhook override (empty for global):"));

        config.Accounts.Add(account);
        return true;
    }

    private async Task<bool> EditAccountAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            _console.WriteLine();
            WriteAccountSummary(config, account);

            var selected = _console.Prompt(
                new SelectionPrompt<AccountEditAction>()
                    .Title("Account action")
                    .AddChoices(
                        AccountEditAction.ReplaceApiKey,
                        AccountEditAction.RenameAccount,
                        AccountEditAction.SetApiVersionOverride,
                        AccountEditAction.SetWebhookOverride,
                        AccountEditAction.ManageRecords,
                        AccountEditAction.RemoveAccount,
                        AccountEditAction.Back)
                    .UseConverter(PromptLabels.AccountEditLabel));

            switch (selected)
            {
                case AccountEditAction.ReplaceApiKey:
                    if (await ReplaceApiKeyAsync(config, account, cancellationToken)) return true;

                    break;
                case AccountEditAction.RenameAccount:
                    return RenameAccount(config, account);
                case AccountEditAction.SetApiVersionOverride:
                    account.ApiVersion = PromptApiVersionOverride("API version override");
                    return true;
                case AccountEditAction.SetWebhookOverride:
                    account.Webhook = EmptyToNull(PromptOptionalUrl("Webhook override (empty for global):"));
                    return true;
                case AccountEditAction.ManageRecords:
                    if (await ManageRecordsAsync(config, account, cancellationToken)) return true;

                    break;
                case AccountEditAction.RemoveAccount:
                    if (_console.Confirm($"Remove account '{Markup.Escape(account.Name)}' and all records?", false))
                    {
                        config.Accounts.Remove(account);
                        return true;
                    }

                    break;
                case AccountEditAction.Back:
                    return false;
            }
        }
    }

    private async Task<bool> ReplaceApiKeyAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var apiKey = PromptSecret("New API key:");
        var draft = new ConfigAccount
        {
            Type = account.Type,
            Name = account.Name,
            ApiKey = apiKey,
            ApiVersion = account.ApiVersion,
            Webhook = account.Webhook,
            Records = account.Records
        };

        IDnsProvider provider;
        try
        {
            provider = _providerRegistry.Create(draft, config.DefaultApiVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _console.WriteLine($"Cannot create provider: {exception.Message}");
            return false;
        }

        if (!await ValidateCredentialsAsync(config, provider, draft, cancellationToken)) return false;

        account.ApiKey = apiKey;
        _console.MarkupLine("[green]API key replaced and validated.[/]");

        if (_console.Confirm("Rediscover records now?", false))
            account.Records = (await DiscoverWithFallbackAsync(provider, account, account.Records, cancellationToken))
                .ToList();

        return true;
    }

    private bool RenameAccount(ConfigModel config, ConfigAccount account)
    {
        var newName = PromptRequired("New account name:");
        if (config.Accounts.Any(candidate =>
                !ReferenceEquals(candidate, account)
                && string.Equals(candidate.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            _console.MarkupLine("[red]Account name must be unique.[/]");
            return false;
        }

        account.Name = newName;
        return true;
    }

    private async Task<bool> ManageRecordsAsync(
        ConfigModel config,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            _console.WriteLine();
            _console.WriteLine($"Records for account: {account.Name}");
            WriteRecords(account.Records);

            var selected = _console.Prompt(
                new SelectionPrompt<RecordMenuAction>()
                    .Title("Record action")
                    .AddChoices(
                        RecordMenuAction.DiscoverFromProvider,
                        RecordMenuAction.AddManually,
                        RecordMenuAction.EditRecord,
                        RecordMenuAction.RemoveRecord,
                        RecordMenuAction.Back)
                    .UseConverter(PromptLabels.RecordMenuLabel));

            switch (selected)
            {
                case RecordMenuAction.DiscoverFromProvider:
                {
                    IDnsProvider provider;
                    try
                    {
                        provider = _providerRegistry.Create(account, config.DefaultApiVersion);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _console.WriteLine($"Cannot create provider: {exception.Message}");
                        return false;
                    }

                    account.Records = (await DiscoverWithFallbackAsync(
                            provider, account, account.Records, cancellationToken))
                        .ToList();
                    return true;
                }
                case RecordMenuAction.AddManually:
                    account.Records.Add(ReadManualRecord(account.Type));
                    return true;
                case RecordMenuAction.EditRecord:
                    return EditRecord(account);
                case RecordMenuAction.RemoveRecord:
                    return RemoveRecord(account);
                case RecordMenuAction.Back:
                    return false;
            }
        }
    }

    private ConfigRecord ReadManualRecord(string providerType)
    {
        _console.MarkupLine("[bold]Manual record add[/]");
        _console.MarkupLine("The record will be accepted without a live provider lookup.");

        var record = new ConfigRecord
        {
            Domain = PromptRequired("Domain name:")
        };

        if (string.Equals(ProviderTypes.Normalize(providerType), ProviderTypes.Cloudflare, StringComparison.Ordinal))
        {
            record.ZoneId = EmptyToNull(PromptOptional("Zone ID (optional, resolved from the domain when empty):"));
            record.RecordId = EmptyToNull(PromptOptional("Record ID (optional, resolved by name when empty):"));
        }
        else
        {
            record.ZoneId = EmptyToNull(PromptOptional("DNS zone ID (optional, mittwald v2):"));
            record.ProjectId = EmptyToNull(PromptOptional("Project ID (optional, mittwald v2):"));
        }

        return record;
    }

    private bool EditRecord(ConfigAccount account)
    {
        if (account.Records.Count == 0)
        {
            _console.MarkupLine("[yellow]No records configured.[/]");
            return false;
        }

        var record = PromptRecord(account.Records, "Record");
        var domain = PromptOptional($"Domain [{record.Domain}]:");
        var zoneId = PromptOptional($"Zone ID [{record.ZoneId}]:");
        var recordId = PromptOptional($"Record ID [{record.RecordId}]:");
        var projectId = PromptOptional($"Project ID [{record.ProjectId}]:");
        var webhook = PromptOptionalUrl("Webhook override (empty to keep current):");

        record.Domain = string.IsNullOrWhiteSpace(domain) ? record.Domain : domain;
        record.ZoneId = string.IsNullOrWhiteSpace(zoneId) ? record.ZoneId : zoneId;
        record.RecordId = string.IsNullOrWhiteSpace(recordId) ? record.RecordId : recordId;
        record.ProjectId = string.IsNullOrWhiteSpace(projectId) ? record.ProjectId : projectId;
        if (!string.IsNullOrWhiteSpace(webhook)) record.Webhook = webhook;
        return true;
    }

    private bool RemoveRecord(ConfigAccount account)
    {
        if (account.Records.Count == 0)
        {
            _console.MarkupLine("[yellow]No records configured.[/]");
            return false;
        }

        var record = PromptRecord(account.Records, "Record to remove");
        if (!_console.Confirm($"Remove record '{Markup.Escape(record.Domain)}'?", false)) return false;

        account.Records.Remove(record);
        return true;
    }

    private async Task<bool> ValidateCredentialsAsync(
        ConfigModel config,
        IDnsProvider provider,
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await provider.ValidateAsync(account, cancellationToken);
                return true;
            }
            catch (HttpRequestException exception) when (
                provider.Type == ProviderTypes.Mittwald
                && (account.ApiVersion ?? config.DefaultApiVersion) == 1
                && exception.StatusCode == HttpStatusCode.Forbidden)
            {
                _console.WriteLine(
                    $"Mittwald API v1 authenticated the API key but denied access to account '{account.Name}'. " +
                    "Enter the mittwald account name or numeric UID, not a local label or API-token UUID.");

                if (!_console.Confirm("Retry with another v1 account identifier?", false)) return false;

                account.Name = PromptRequired("Mittwald account name or UID:");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _console.WriteLine($"Provider validation failed: {exception.Message}");
                if (!_console.Confirm("Retry credential validation?", false)) return false;

                account.ApiKey = PromptSecret("API key:");
            }
        }
    }

    private async Task<List<ConfigRecord>> DiscoverWithFallbackAsync(
        IDnsProvider provider,
        ConfigAccount account,
        IReadOnlyList<ConfigRecord>? currentRecords,
        CancellationToken cancellationToken)
    {
        try
        {
            var discovered = await provider.DiscoverAsync(account, cancellationToken);
            return SelectRecords(discovered, currentRecords);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _console.WriteLine($"Record discovery failed: {exception.Message}");
            if (_console.Confirm("Add a record manually?", false))
                return [.. currentRecords ?? [], ReadManualRecord(account.Type)];

            return currentRecords?.ToList() ?? [];
        }
    }

    private List<ConfigRecord> SelectRecords(
        IReadOnlyList<DiscoveredTarget> discovered,
        IReadOnlyList<ConfigRecord>? currentRecords)
    {
        if (discovered.Count == 0)
        {
            _console.MarkupLine("[yellow]No records found.[/]");
            return currentRecords?.ToList() ?? [];
        }

        var existingByDomain = (currentRecords ?? [])
            .GroupBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var preselected = discovered
            .Where(target => existingByDomain.Count == 0 || existingByDomain.ContainsKey(target.Domain))
            .ToList();

        var prompt = new MultiSelectionPrompt<DiscoveredTarget>()
            .Title("Select records to manage")
            .NotRequired()
            .InstructionsText("[grey](Press space to toggle, enter to accept)[/]")
            .AddChoices(discovered)
            .UseConverter(target => Markup.Escape(target.ToString()));

        foreach (var target in preselected) prompt.Select(target);

        var selected = _console.Prompt(prompt);

        return selected.Select(target =>
            existingByDomain.TryGetValue(target.Domain, out var existing)
                ? existing
                : new ConfigRecord
                {
                    Domain = target.Domain,
                    ZoneId = target.ZoneId,
                    RecordId = target.RecordId,
                    ProjectId = target.ProjectId
                }).ToList();
    }

    private bool RunGlobalSettings(ConfigModel config)
    {
        _console.WriteLine();
        WriteGlobalSettings(config);

        var selected = _console.Prompt(
            new SelectionPrompt<GlobalSettingsAction>()
                .Title("Global setting")
                .AddChoices(
                    GlobalSettingsAction.ChangeInterval,
                    GlobalSettingsAction.ChangeDefaultApiVersion,
                    GlobalSettingsAction.ChangeIpServiceUrl,
                    GlobalSettingsAction.ChangeGlobalWebhook,
                    GlobalSettingsAction.ClearGlobalWebhook,
                    GlobalSettingsAction.ToggleChangeNotifications,
                    GlobalSettingsAction.ToggleErrorNotifications,
                    GlobalSettingsAction.Back)
                .UseConverter(PromptLabels.GlobalSettingsLabel));

        switch (selected)
        {
            case GlobalSettingsAction.ChangeInterval:
                config.Timeout = PromptTimeSpan("Interval:");
                return true;
            case GlobalSettingsAction.ChangeDefaultApiVersion:
                config.DefaultApiVersion = PromptApiVersionValue("Default mittwald API version:");
                return true;
            case GlobalSettingsAction.ChangeIpServiceUrl:
            {
                var url = PromptOptionalUrl($"IP service URL [{config.IpServiceUrl}]:");
                if (!string.IsNullOrWhiteSpace(url)) config.IpServiceUrl = url;
                return true;
            }
            case GlobalSettingsAction.ChangeGlobalWebhook:
                config.GlobalWebhook = EmptyToNull(PromptOptionalUrl("Global webhook:"));
                return true;
            case GlobalSettingsAction.ClearGlobalWebhook:
                config.GlobalWebhook = null;
                return true;
            case GlobalSettingsAction.ToggleChangeNotifications:
                config.NotifyOnChange = !config.NotifyOnChange;
                return true;
            case GlobalSettingsAction.ToggleErrorNotifications:
                config.NotifyOnError = !config.NotifyOnError;
                return true;
            case GlobalSettingsAction.Back:
            default:
                return false;
        }
    }

    private ConfigModel? ImportPlainJson()
    {
        var file = PromptRequired("Plain JSON file:");
        var loaded = ConfigStore.LoadFromFile(file, null);
        if (loaded.Format != ConfigFileFormat.Plain)
        {
            _console.MarkupLine("[red]Interactive import expects plain JSON.[/]");
            return null;
        }

        var errors = ConfigValidator.Validate(loaded.Config);
        if (errors.Count > 0)
        {
            WriteValidationErrors(errors);
            return null;
        }

        return loaded.Config;
    }

    private void ExportPlainJsonFromWorkspace(ConfigModel config)
    {
        var selected = _console.Prompt(
            new SelectionPrompt<ExportDestination>()
                .Title("Export destination")
                .AddChoices(ExportDestination.Stdout, ExportDestination.File, ExportDestination.Back)
                .UseConverter(PromptLabels.ExportDestinationLabel));
        if (selected == ExportDestination.Back) return;

        var json = JsonSerializer.Serialize(config, ConfigStore.JsonOptions);
        if (selected == ExportDestination.Stdout)
        {
            Console.WriteLine(json);
            return;
        }

        ConfigStore.WriteAllTextAtomic(PromptRequired("File:"), json);
    }

    private bool Save(ConfigModel config)
    {
        ConfigValidator.ThrowIfInvalid(config);

        if (_store.CanEncrypt)
        {
            _store.SaveEncrypted(config);
            _console.MarkupLine($"[green]Saved encrypted config to {Markup.Escape(_store.ConfigPath)}[/]");
            return true;
        }

        if (!_console.Confirm(
                "DDNS_SECRET is not set. Save the config as plain JSON?", false))
            return false;

        _store.SavePlain(config);
        _console.MarkupLine($"[yellow]Saved plain JSON config to {Markup.Escape(_store.ConfigPath)}[/]");
        return true;
    }

    private void WriteSummary(ConfigModel config, bool dirty)
    {
        _console.MarkupLine("[bold]DDNS Config[/]");
        _console.WriteLine();

        WriteGlobalSettings(config);
        WriteAccounts(config);
        _console.WriteLine();
        _console.MarkupLineInterpolated($"Unsaved changes: {(dirty ? "yes" : "no")}");
    }

    private void WriteGlobalSettings(ConfigModel config)
    {
        var table = new Table()
            .Title("Global")
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Interval", config.Timeout.ToString());
        table.AddRow("Default mittwald API version", config.DefaultApiVersion.ToString());
        table.AddRow("IP service", Markup.Escape(config.IpServiceUrl));
        table.AddRow("Global webhook", string.IsNullOrWhiteSpace(config.GlobalWebhook) ? "unset" : "configured");
        table.AddRow("Notify on change", config.NotifyOnChange ? "yes" : "no");
        table.AddRow("Notify on error", config.NotifyOnError ? "yes" : "no");
        _console.Write(table);
    }

    private void WriteAccounts(ConfigModel config)
    {
        var table = new Table()
            .Title("Accounts")
            .AddColumn("Name")
            .AddColumn("Provider")
            .AddColumn("Records")
            .AddColumn("Webhook")
            .AddColumn("API");

        if (config.Accounts.Count == 0)
            table.AddRow("No accounts configured yet.", string.Empty, string.Empty, string.Empty, string.Empty);
        else
            foreach (var account in config.Accounts)
                table.AddRow(
                    Markup.Escape(account.Name),
                    PromptLabels.ProviderLabel(account.Type),
                    account.Records.Count.ToString(),
                    string.IsNullOrWhiteSpace(account.Webhook) ? "global" : "custom",
                    account.ApiVersion is null ? "global" : $"v{account.ApiVersion}");

        _console.Write(table);
    }

    private void WriteAccountSummary(ConfigModel config, ConfigAccount account)
    {
        var table = new Table()
            .Title($"Account: {Markup.Escape(account.Name)}")
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Provider", PromptLabels.ProviderLabel(account.Type));
        table.AddRow("API key", "********");
        table.AddRow("API version", account.ApiVersion is null ? "global default" : $"v{account.ApiVersion}");
        table.AddRow("Webhook", string.IsNullOrWhiteSpace(account.Webhook) ? "global default" : "custom");
        table.AddRow("Records", account.Records.Count.ToString());
        _console.Write(table);
    }

    private void WriteRecords(IEnumerable<ConfigRecord> records)
    {
        var table = new Table()
            .AddColumn("Domain")
            .AddColumn("Zone")
            .AddColumn("Record")
            .AddColumn("Webhook");

        var ordered = records.OrderBy(record => record.Domain, StringComparer.OrdinalIgnoreCase).ToList();
        if (ordered.Count == 0)
        {
            table.AddRow("No records configured.", string.Empty, string.Empty, string.Empty);
        }
        else
        {
            foreach (var record in ordered)
                table.AddRow(
                    Markup.Escape(record.Domain),
                    string.IsNullOrWhiteSpace(record.ZoneId) ? string.Empty : Markup.Escape(record.ZoneId),
                    string.IsNullOrWhiteSpace(record.RecordId) ? string.Empty : Markup.Escape(record.RecordId),
                    string.IsNullOrWhiteSpace(record.Webhook) ? "default" : "custom");
        }

        _console.Write(table);
    }

    private void WriteEffectiveConfig(ConfigModel config)
    {
        _console.WriteLine();
        var table = new Table()
            .Title("Effective config")
            .AddColumn("Domain")
            .AddColumn("Account")
            .AddColumn("Provider")
            .AddColumn("Webhook");

        foreach (var account in config.Accounts)
        foreach (var record in account.Records)
            table.AddRow(
                Markup.Escape(record.Domain),
                Markup.Escape(account.Name),
                PromptLabels.ProviderLabel(account.Type),
                WebhookSource(config, account, record));

        _console.Write(table);
    }

    private void WriteValidationErrors(IReadOnlyList<string> errors)
    {
        var table = new Table()
            .Title("Imported config is invalid")
            .AddColumn("Error");

        foreach (var error in errors) table.AddRow(Markup.Escape(error));

        _console.Write(table);
    }

    private string PromptProviderType()
    {
        return _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Provider")
                .AddChoices(ProviderTypes.All)
                .UseConverter(PromptLabels.ProviderLabel));
    }

    private int? PromptApiVersionOverride(string title)
    {
        var selected = _console.Prompt(
            new SelectionPrompt<ApiVersionChoice>()
                .Title(title)
                .AddChoices(
                    ApiVersionChoice.GlobalDefault,
                    ApiVersionChoice.V1,
                    ApiVersionChoice.V2)
                .UseConverter(PromptLabels.ApiVersionLabel));

        return selected switch
        {
            ApiVersionChoice.V1 => 1,
            ApiVersionChoice.V2 => 2,
            _ => null
        };
    }

    private int PromptApiVersionValue(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<int>(prompt)
                .Validate(PromptValidators.ApiVersion));
    }

    private TimeSpan PromptTimeSpan(string prompt)
    {
        var input = _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Interval));

        PromptValidators.TryParseTimeSpan(input, out var timeSpan);
        return timeSpan;
    }

    private string PromptRequired(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Required));
    }

    private string PromptOptional(string prompt)
    {
        return _console.Prompt(new TextPrompt<string>(prompt).AllowEmpty());
    }

    private string PromptOptionalUrl(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .AllowEmpty()
                .Validate(PromptValidators.OptionalUrl));
    }

    private string PromptSecret(string prompt)
    {
        return _console.Prompt(
            new TextPrompt<string>(prompt)
                .Secret()
                .Validate(PromptValidators.Required));
    }

    private ConfigRecord PromptRecord(IReadOnlyList<ConfigRecord> records, string title)
    {
        return _console.Prompt(
            new SelectionPrompt<ConfigRecord>()
                .Title(title)
                .AddChoices(records)
                .UseConverter(record => Markup.Escape(record.Domain)));
    }

    private void PromptToContinue()
    {
        _console.Prompt(new TextPrompt<string>("Press enter to continue.").AllowEmpty());
    }

    private static string WebhookSource(ConfigModel config, ConfigAccount account, ConfigRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Webhook)) return "record override";

        if (!string.IsNullOrWhiteSpace(account.Webhook)) return "account override";

        if (!string.IsNullOrWhiteSpace(config.GlobalWebhook)) return "global";

        return "unset";
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record AccountMenuSelection(AccountMenuAction Action, ConfigAccount? Account = null);
}
