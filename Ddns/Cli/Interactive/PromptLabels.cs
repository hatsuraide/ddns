using Ddns.Core;

namespace Ddns.Cli.Interactive;

public static class PromptLabels
{
    public static string MainMenuLabel(MainMenuAction action)
    {
        return action switch
        {
            MainMenuAction.AddAccount => "Add provider account",
            MainMenuAction.ImportPlainJson => "Import plain JSON",
            MainMenuAction.Accounts => "Accounts",
            MainMenuAction.GlobalSettings => "Global settings",
            MainMenuAction.ReviewEffectiveConfig => "Review effective config",
            MainMenuAction.ExportPlainJson => "Export plain JSON",
            MainMenuAction.Save => "Save",
            MainMenuAction.Exit => "Exit",
            MainMenuAction.ExitWithoutSaving => "Exit without saving",
            _ => action.ToString()
        };
    }

    public static string AccountMenuLabel(AccountMenuAction action)
    {
        return action switch
        {
            AccountMenuAction.AddAccount => "Add provider account",
            AccountMenuAction.EditAccount => "Edit account",
            AccountMenuAction.Back => "Back",
            _ => action.ToString()
        };
    }

    public static string RecordMenuLabel(RecordMenuAction action)
    {
        return action switch
        {
            RecordMenuAction.DiscoverFromProvider => "Discover from provider",
            RecordMenuAction.AddManually => "Add manually",
            RecordMenuAction.EditRecord => "Edit record",
            RecordMenuAction.RemoveRecord => "Remove record",
            RecordMenuAction.Back => "Back",
            _ => action.ToString()
        };
    }

    public static string GlobalSettingsLabel(GlobalSettingsAction action)
    {
        return action switch
        {
            GlobalSettingsAction.ChangeInterval => "Change interval",
            GlobalSettingsAction.ChangeDefaultApiVersion => "Change default mittwald API version",
            GlobalSettingsAction.ChangeIpServiceUrl => "Change IP service URL",
            GlobalSettingsAction.ChangeGlobalWebhook => "Change global webhook",
            GlobalSettingsAction.ClearGlobalWebhook => "Clear global webhook",
            GlobalSettingsAction.ToggleChangeNotifications => "Toggle change notifications",
            GlobalSettingsAction.ToggleErrorNotifications => "Toggle error notifications",
            GlobalSettingsAction.Back => "Back",
            _ => action.ToString()
        };
    }

    public static string ExportDestinationLabel(ExportDestination destination)
    {
        return destination switch
        {
            ExportDestination.Stdout => "Plain JSON to stdout",
            ExportDestination.File => "Plain JSON to file",
            ExportDestination.Back => "Back",
            _ => destination.ToString()
        };
    }

    public static string ApiVersionLabel(ApiVersionChoice choice)
    {
        return choice switch
        {
            ApiVersionChoice.GlobalDefault => "Global default",
            ApiVersionChoice.V1 => "API v1",
            ApiVersionChoice.V2 => "API v2",
            _ => choice.ToString()
        };
    }

    public static string ProviderLabel(string type)
    {
        return ProviderTypes.Normalize(type) switch
        {
            ProviderTypes.Cloudflare => "Cloudflare",
            ProviderTypes.Mittwald => "Mittwald",
            _ => type
        };
    }

    public static string AccountEditLabel(AccountEditAction action)
    {
        return action switch
        {
            AccountEditAction.ReplaceApiKey => "Replace API key",
            AccountEditAction.RenameAccount => "Rename account",
            AccountEditAction.SetApiVersionOverride => "Set API version override",
            AccountEditAction.SetWebhookOverride => "Set webhook override",
            AccountEditAction.ManageRecords => "Manage records",
            AccountEditAction.RemoveAccount => "Remove account",
            AccountEditAction.Back => "Back",
            _ => action.ToString()
        };
    }
}
