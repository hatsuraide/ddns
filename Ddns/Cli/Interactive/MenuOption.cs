namespace Ddns.Cli.Interactive;

public sealed record MenuOption<T>(T Value, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public enum MainMenuAction
{
    AddAccount,
    ImportPlainJson,
    Accounts,
    GlobalSettings,
    ReviewEffectiveConfig,
    ExportPlainJson,
    Save,
    Exit,
    ExitWithoutSaving
}

public enum AccountMenuAction
{
    AddAccount,
    EditAccount,
    Back
}

public enum AccountEditAction
{
    ReplaceApiKey,
    RenameAccount,
    SetApiVersionOverride,
    SetWebhookOverride,
    ManageRecords,
    RemoveAccount,
    Back
}

public enum RecordMenuAction
{
    DiscoverFromProvider,
    AddManually,
    EditRecord,
    RemoveRecord,
    Back
}

public enum GlobalSettingsAction
{
    ChangeInterval,
    ChangeDefaultApiVersion,
    ChangeIpServiceUrl,
    ChangeGlobalWebhook,
    ClearGlobalWebhook,
    ToggleChangeNotifications,
    ToggleErrorNotifications,
    Back
}

public enum ExportDestination
{
    Stdout,
    File,
    Back
}

public enum ApiVersionChoice
{
    GlobalDefault,
    V1,
    V2
}
