using System.Net;
using System.Text.Json;
using Ddns.Cli.Tui;
using Ddns.Config;
using Ddns.Core;
using Spectre.Console;

namespace Ddns.Cli.Interactive;

public sealed class ConfigWorkspace
{
    private readonly IAnsiConsole _console;
    private readonly ProviderRegistry _providerRegistry;
    private readonly ConfigStore _store;
    private readonly TuiSession _tui;

    public ConfigWorkspace(ConfigStore store, ProviderRegistry providerRegistry, IAnsiConsole console)
        : this(store, providerRegistry, console, new TuiSession(Console.Out, TuiSession.IsTerminalAvailable()))
    {
    }

    internal ConfigWorkspace(
        ConfigStore store,
        ProviderRegistry providerRegistry,
        IAnsiConsole console,
        TuiSession tui)
    {
        _store = store;
        _providerRegistry = providerRegistry;
        _console = console;
        _tui = tui;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var config = _store.Load() ?? new ConfigModel();
        var dirty = false;

        ConsoleCancelEventHandler? cancelHandler = null;
        if (_tui.Enabled)
        {
            cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                _tui.Dispose();
                Environment.Exit(130);
            };
            Console.CancelKeyPress += cancelHandler;
        }

        _tui.EnterAlternateScreen();
        try
        {
            while (true)
            {
                if (!_tui.IsActive)
                {
                    _console.WriteLine();
                    WriteSummary(config, dirty);
                }

                if (!PromptMainMenu(config, dirty, out var action)) return 0;

                switch (action)
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
                        if (!dirty || Confirm("Exit without saving changes?")) return 0;
                        break;
                    }
                }
            }
        }
        finally
        {
            if (cancelHandler is not null) Console.CancelKeyPress -= cancelHandler;
            _tui.Dispose();
        }
    }

    private bool PromptMainMenu(ConfigModel config, bool dirty, out MainMenuAction action)
    {
        var recordCount = config.Accounts.Sum(account => account.Records.Count);
        var choices = new List<MenuChoice<MainMenuAction>>();

        if (config.Accounts.Count == 0)
        {
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.AddAccount, "Add provider account", "mittwald or Cloudflare"));
            choices.Add(new MenuChoice<MainMenuAction>(MainMenuAction.ImportPlainJson, "Import plain JSON"));
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.GlobalSettings, "Global settings", config.Timeout.ToString(), IsSubmenu: true));
            choices.Add(new MenuChoice<MainMenuAction>(MainMenuAction.Exit, "Exit"));
        }
        else
        {
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.Accounts,
                "Accounts",
                $"{config.Accounts.Count} accounts, {recordCount} records",
                IsSubmenu: true));
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.GlobalSettings,
                "Global settings",
                $"every {config.Timeout}",
                IsSubmenu: true));
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.ReviewEffectiveConfig, "Review effective config"));
            choices.Add(new MenuChoice<MainMenuAction>(MainMenuAction.ExportPlainJson, "Export plain JSON"));
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.Save,
                "Save",
                dirty ? "unsaved changes" : "no pending changes"));
            choices.Add(new MenuChoice<MainMenuAction>(
                MainMenuAction.ExitWithoutSaving, "Exit without saving"));
        }

        if (Choose("DDNS Config", choices, isRoot: true, out action)) return true;

        action = MainMenuAction.ExitWithoutSaving;
        return false;
    }

    private async Task<bool> RunAccountsMenuAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!_tui.IsActive) _console.MarkupLine("[bold]Accounts[/]");

            var choices = new List<MenuChoice<AccountMenuSelection>>
            {
                new(
                    new AccountMenuSelection(AccountMenuAction.AddAccount),
                    PromptLabels.AccountMenuLabel(AccountMenuAction.AddAccount))
            };
            choices.AddRange(config.Accounts.Select(account =>
                new MenuChoice<AccountMenuSelection>(
                    new AccountMenuSelection(AccountMenuAction.EditAccount, account),
                    account.Name,
                    $"{PromptLabels.ProviderLabel(account.Type)} · {account.Records.Count} records",
                    IsSubmenu: true)));
            choices.Add(new MenuChoice<AccountMenuSelection>(
                new AccountMenuSelection(AccountMenuAction.Back),
                PromptLabels.AccountMenuLabel(AccountMenuAction.Back)));

            if (!Choose("Accounts", choices, isRoot: false, out var selected)) return false;

            if (selected.Action == AccountMenuAction.AddAccount)
            {
                if (await AddAccountAsync(config, cancellationToken)) return true;
            }
            else if (selected.Action == AccountMenuAction.Back)
            {
                return false;
            }
            else if (selected.Account is not null
                     && await EditAccountAsync(config, selected.Account, cancellationToken))
            {
                return true;
            }
        }
    }

    private async Task<bool> AddAccountAsync(ConfigModel config, CancellationToken cancellationToken)
    {
        if (!_tui.IsActive)
        {
            _console.WriteLine();
            _console.MarkupLine("[bold]Add provider account[/]");
        }

        var type = PromptProviderType();
        var name = PromptRequired(type == ProviderTypes.Mittwald
            ? "Account label / mittwald account name:"
            : "Account label:");

        if (config.Accounts.Any(account =>
                string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Notify("Account name must be unique.", error: true);
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
            Notify($"Cannot create provider: {exception.Message}", error: true);
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
            if (!_tui.IsActive) WriteAccountSummary(config, account);

            var choices = Enum.GetValues<AccountEditAction>()
                .Select(action => new MenuChoice<AccountEditAction>(
                    action,
                    PromptLabels.AccountEditLabel(action),
                    action == AccountEditAction.ManageRecords
                        ? $"{account.Records.Count} records"
                        : null,
                    IsSubmenu: action == AccountEditAction.ManageRecords))
                .ToList();

            if (!Choose($"Account: {account.Name}", choices, isRoot: false, out var selected)) return false;

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
                    if (Confirm($"Remove account '{account.Name}' and all records?"))
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
            Notify($"Cannot create provider: {exception.Message}", error: true);
            return false;
        }

        if (!await ValidateCredentialsAsync(config, provider, draft, cancellationToken)) return false;

        account.ApiKey = apiKey;
        Notify("API key replaced and validated.");

        if (Confirm("Rediscover records now?"))
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
            Notify("Account name must be unique.", error: true);
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
            if (!_tui.IsActive)
            {
                _console.WriteLine();
                _console.WriteLine($"Records for account: {account.Name}");
                WriteRecords(account.Records);
            }

            var choices = Enum.GetValues<RecordMenuAction>()
                .Select(action => new MenuChoice<RecordMenuAction>(
                    action,
                    PromptLabels.RecordMenuLabel(action),
                    IsSubmenu: false))
                .ToList();

            if (!Choose($"Records: {account.Name}", choices, isRoot: false, out var selected)) return false;

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
                        Notify($"Cannot create provider: {exception.Message}", error: true);
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
        if (!_tui.IsActive)
        {
            _console.MarkupLine("[bold]Manual record add[/]");
            _console.MarkupLine("The record will be accepted without a live provider lookup.");
        }

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
            Notify("No records configured.", error: true);
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
            Notify("No records configured.", error: true);
            return false;
        }

        var record = PromptRecord(account.Records, "Record to remove");
        if (!Confirm($"Remove record '{record.Domain}'?")) return false;

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
                Notify(
                    $"Mittwald API v1 authenticated the API key but denied access to account '{account.Name}'. " +
                    "Enter the mittwald account name or numeric UID, not a local label or API-token UUID.",
                    error: true);

                if (!Confirm("Retry with another v1 account identifier?")) return false;

                account.Name = PromptRequired("Mittwald account name or UID:");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Notify($"Provider validation failed: {exception.Message}", error: true);
                if (!Confirm("Retry credential validation?")) return false;

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
        {Notify($"Record discovery failed: {exception.Message}", error: true);
            if (Confirm("Add a record manually?"))
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
            Notify("No records found.", error: true);
            return currentRecords?.ToList() ?? [];
        }

        var existingByDomain = (currentRecords ?? [])
            .GroupBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var choices = discovered
            .Select(target =>
            {
                var record = existingByDomain.TryGetValue(target.Domain, out var existing)
                    ? existing
                    : new ConfigRecord
                    {
                        Domain = target.Domain,
                        ZoneId = target.ZoneId,
                        RecordId = target.RecordId,
                        ProjectId = target.ProjectId
                    };

                return new MenuChoice<ConfigRecord>(
                    record,
                    target.Domain,
                    string.IsNullOrWhiteSpace(target.ZoneId) ? null : $"zone {target.ZoneId}");
            })
            .ToList();

        var preselected = choices
            .Where(choice => existingByDomain.Count == 0 || existingByDomain.ContainsKey(choice.Value.Domain))
            .Select(choice => choice.Value)
            .ToList();

        var selected = ChooseMany("Select records to manage", choices, preselected);
        return selected?.ToList() ?? currentRecords?.ToList() ?? [];
    }

    private bool RunGlobalSettings(ConfigModel config)
    {
        if (!_tui.IsActive)
        {
            _console.WriteLine();
            WriteGlobalSettings(config);
        }

        var choices = new List<MenuChoice<GlobalSettingsAction>>
        {
            new(GlobalSettingsAction.ChangeInterval, PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ChangeInterval),
                $"every {config.Timeout}"),
            new(GlobalSettingsAction.ChangeDefaultApiVersion,
                PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ChangeDefaultApiVersion),
                $"v{config.DefaultApiVersion}"),
            new(GlobalSettingsAction.ChangeIpServiceUrl,
                PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ChangeIpServiceUrl),
                config.IpServiceUrl),
            new(GlobalSettingsAction.ChangeGlobalWebhook,
                PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ChangeGlobalWebhook),
                string.IsNullOrWhiteSpace(config.GlobalWebhook) ? "unset" : "configured"),
            new(GlobalSettingsAction.ClearGlobalWebhook,
                PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ClearGlobalWebhook)),
            new(GlobalSettingsAction.ToggleChangeNotifications,
                PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ToggleChangeNotifications),
                config.NotifyOnChange ? "on" : "off"),
            new(GlobalSettingsAction.ToggleErrorNotifications,
                PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.ToggleErrorNotifications),
                config.NotifyOnError ? "on" : "off"),
            new(GlobalSettingsAction.Back, PromptLabels.GlobalSettingsLabel(GlobalSettingsAction.Back))
        };

        if (!Choose("Global settings", choices, isRoot: false, out var selected)) return false;

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
            Notify("Interactive import expects plain JSON.", error: true);
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
        var choices = new List<MenuChoice<ExportDestination>>
        {
            new(ExportDestination.Stdout, PromptLabels.ExportDestinationLabel(ExportDestination.Stdout)),
            new(ExportDestination.File, PromptLabels.ExportDestinationLabel(ExportDestination.File)),
            new(ExportDestination.Back, PromptLabels.ExportDestinationLabel(ExportDestination.Back))
        };

        if (!Choose("Export", choices, isRoot: false, out var selected)) return;
        if (selected == ExportDestination.Back) return;

        var json = JsonSerializer.Serialize(config, ConfigStore.JsonOptions);
        if (selected == ExportDestination.Stdout)
        {
            WithConsole(() =>
            {
                Console.WriteLine(json);
                return true;
            });
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
            Notify($"Saved encrypted config to {_store.ConfigPath}");
            return true;
        }

        if (!Confirm("DDNS_SECRET is not set. Save the config as plain JSON?")) return false;

        _store.SavePlain(config);
        Notify($"Saved plain JSON config to {_store.ConfigPath}");
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
        WithConsole(() =>
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
            PromptToContinue();
            return true;
        });
    }

    private void WriteValidationErrors(IReadOnlyList<string> errors)
    {
        _tui.Suspend();
        try
        {
            var table = new Table()
                .Title("Imported config is invalid")
                .AddColumn("Error");

            foreach (var error in errors) table.AddRow(Markup.Escape(error));

            _console.Write(table);
        }
        finally
        {
            _tui.Resume();
        }
    }

    private string PromptProviderType()
    {
        var choices = ProviderTypes.All
            .Select(type => new MenuChoice<string>(
                type,
                PromptLabels.ProviderLabel(type),
                type == ProviderTypes.Cloudflare ? "API token + zone records" : "API key, v1 or v2"))
            .ToList();

        return Choose("Provider", choices, isRoot: false, out var type) ? type : ProviderTypes.Mittwald;
    }

    private int? PromptApiVersionOverride(string title)
    {
        var choices = new List<MenuChoice<ApiVersionChoice>>
        {
            new(ApiVersionChoice.GlobalDefault, PromptLabels.ApiVersionLabel(ApiVersionChoice.GlobalDefault)),
            new(ApiVersionChoice.V1, PromptLabels.ApiVersionLabel(ApiVersionChoice.V1)),
            new(ApiVersionChoice.V2, PromptLabels.ApiVersionLabel(ApiVersionChoice.V2))
        };

        if (!Choose(title, choices, isRoot: false, out var selected)) return null;

        return selected switch
        {
            ApiVersionChoice.V1 => 1,
            ApiVersionChoice.V2 => 2,
            _ => null
        };
    }

    private int PromptApiVersionValue(string prompt)
    {
        return WithConsole(() => _console.Prompt(
            new TextPrompt<int>(prompt)
                .Validate(PromptValidators.ApiVersion)));
    }

    private TimeSpan PromptTimeSpan(string prompt)
    {
        var input = WithConsole(() => _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Interval)));

        PromptValidators.TryParseTimeSpan(input, out var timeSpan);
        return timeSpan;
    }

    private string PromptRequired(string prompt)
    {
        return WithConsole(() => _console.Prompt(
            new TextPrompt<string>(prompt)
                .Validate(PromptValidators.Required)));
    }

    private string PromptOptional(string prompt)
    {
        return WithConsole(() => _console.Prompt(new TextPrompt<string>(prompt).AllowEmpty()));
    }

    private string PromptOptionalUrl(string prompt)
    {
        return WithConsole(() => _console.Prompt(
            new TextPrompt<string>(prompt)
                .AllowEmpty()
                .Validate(PromptValidators.OptionalUrl)));
    }

    private string PromptSecret(string prompt)
    {
        return WithConsole(() => _console.Prompt(
            new TextPrompt<string>(prompt)
                .Secret()
                .Validate(PromptValidators.Required)));
    }

    private ConfigRecord PromptRecord(IReadOnlyList<ConfigRecord> records, string title)
    {
        var choices = records
            .Select(record => new MenuChoice<ConfigRecord>(
                record,
                record.Domain,
                string.IsNullOrWhiteSpace(record.ZoneId) ? null : $"zone {record.ZoneId}"))
            .ToList();

        if (Choose(title, choices, isRoot: false, out var selected)) return selected;

        return records[0];
    }

    private void PromptToContinue()
    {
        WithConsole(() => _console.Prompt(new TextPrompt<string>("Press enter to continue.").AllowEmpty()));
    }

    // ── TUI / fallback plumbing ─────────────────────────────────────────────

    /// <summary>
    /// Single-select menu. Uses the LSAC-style TUI when a terminal is available
    /// and falls back to Spectre.Console prompts otherwise.
    /// </summary>
    private bool Choose<T>(string title, IReadOnlyList<MenuChoice<T>> choices, bool isRoot, out T value)
    {
        if (_tui.Enabled)
        {
            var items = choices.Select(choice => choice.ToItem()).ToList();
            var index = _tui.Select(title, items, isRoot);
            if (index is null)
            {
                value = default!;
                return false;
            }

            value = choices[index.Value].Value;
            return true;
        }

        if (choices.Count == 0)
        {
            value = default!;
            return false;
        }

        _console.WriteLine();
        value = WithConsole(() => _console.Prompt(
            new SelectionPrompt<MenuChoice<T>>()
                .Title(title)
                .PageSize(15)
                .AddChoices(choices)).Value);
        return true;
    }

    /// <summary>Multi-select menu with the same TUI/fallback split as <see cref="Choose{T}"/>.</summary>
    private IReadOnlyList<T>? ChooseMany<T>(
        string title,
        IReadOnlyList<MenuChoice<T>> choices,
        IReadOnlyCollection<T> preselected)
    {
        if (_tui.Enabled)
        {
            var items = choices.Select(choice => choice.ToItem()).ToList();
            var preselectedIndices = new List<int>();
            for (var i = 0; i < choices.Count; i++)
                if (preselected.Contains(choices[i].Value))
                    preselectedIndices.Add(i);

            var indices = _tui.SelectMany(title, items, preselectedIndices);
            return indices?.Select(index => choices[index].Value).ToList();
        }

        var prompt = new MultiSelectionPrompt<MenuChoice<T>>()
            .Title(title)
            .NotRequired()
            .InstructionsText("[grey](Press space to toggle, enter to accept)[/]")
            .AddChoices(choices);

        foreach (var choice in choices)
            if (preselected.Contains(choice.Value))
                prompt.Select(choice);

        return WithConsole(() => _console.Prompt(prompt).Select(choice => choice.Value).ToList());
    }

    private bool Confirm(string prompt)
    {
        return WithConsole(() => _console.Confirm(prompt, false));
    }

    private T WithConsole<T>(Func<T> action)
    {
        _tui.Suspend();
        try
        {
            return action();
        }
        finally
        {
            _tui.Resume();
        }
    }

    private void Notify(string message, bool error = false)
    {
        if (_tui.IsActive)
        {
            _tui.Alert(message, error);
            return;
        }

        if (error)
            _console.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        else
            _console.MarkupLine(Markup.Escape(message));
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
