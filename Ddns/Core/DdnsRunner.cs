using System.Net;
using Ddns.Config;
using Ddns.Notifications;
using Microsoft.Extensions.Logging;

namespace Ddns.Core;

/// <summary>
/// Runs one DDNS cycle over every configured account and record. Shared by the
/// daemon and the one-shot <c>check</c> command.
/// </summary>
public sealed class DdnsRunner(
    IProviderRegistry providerRegistry,
    PublicIpService publicIpService,
    WebhookNotifier notifier,
    ILogger<DdnsRunner> logger)
{
    private readonly Dictionary<string, string> _appliedAddresses = new(StringComparer.Ordinal);

    public async Task<DdnsRunResult> RunOnceAsync(
        ConfigModel config,
        string? ipServiceUrlOverride = null,
        CancellationToken cancellationToken = default)
    {
        var ipServiceUrl = string.IsNullOrWhiteSpace(ipServiceUrlOverride)
            ? config.IpServiceUrl
            : ipServiceUrlOverride;

        IPAddress ipAddress;
        try
        {
            ipAddress = await publicIpService.GetPublicAddressAsync(ipServiceUrl, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to determine the public IP address");
            if (config.NotifyOnError)
                await notifier.SendAsync(
                    config.GlobalWebhook,
                    $"DDNS: failed to determine the public IP address: {exception.Message}",
                    cancellationToken);

            return new DdnsRunResult(null, 0, 0, 0);
        }

        logger.LogInformation("Public IP address: {IpAddress}", ipAddress);

        var updated = 0;
        var unchanged = 0;
        var failed = 0;

        foreach (var account in config.Accounts)
        {
            IDnsProvider provider;
            try
            {
                provider = providerRegistry.Create(account, config.DefaultApiVersion);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Skipping account {Account}", account.Name);
                failed += account.Records.Count;
                continue;
            }

            if (string.IsNullOrWhiteSpace(account.ApiKey))
            {
                logger.LogError("Skipping account {Account}: no api_key is configured", account.Name);
                failed += account.Records.Count;
                continue;
            }

            foreach (var record in account.Records)
                switch (await UpdateRecordAsync(config, provider, account, record, ipAddress, cancellationToken))
                {
                    case RecordOutcome.Updated:
                        updated++;
                        break;
                    case RecordOutcome.Unchanged:
                        unchanged++;
                        break;
                    default:
                        failed++;
                        break;
                }
        }

        logger.LogInformation(
            "DDNS cycle finished. Updated: {Updated}; Unchanged: {Unchanged}; Failed: {Failed}",
            updated,
            unchanged,
            failed);

        return new DdnsRunResult(ipAddress, updated, unchanged, failed);
    }

    /// <summary>Forgets which addresses were already applied, e.g. after a config reload.</summary>
    public void ResetState()
    {
        _appliedAddresses.Clear();
    }

    private async Task<RecordOutcome> UpdateRecordAsync(
        ConfigModel config,
        IDnsProvider provider,
        ConfigAccount account,
        ConfigRecord record,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var address = ipAddress.ToString();
        var cacheKey = string.Join(
            '\u001f',
            provider.Type,
            account.Name,
            record.Domain,
            record.ZoneId ?? string.Empty,
            record.RecordId ?? string.Empty);

        if (_appliedAddresses.TryGetValue(cacheKey, out var applied) && applied == address)
        {
            logger.LogDebug("{Provider}: {Domain} already handled with {IpAddress} in this process",
                provider.Type, record.Domain, address);
            return RecordOutcome.Unchanged;
        }

        var webhook = record.Webhook ?? account.Webhook ?? config.GlobalWebhook;

        try
        {
            var result = await provider.UpdateAsync(account, record, ipAddress, cancellationToken);
            _appliedAddresses[cacheKey] = address;

            if (!result.Changed)
            {
                logger.LogInformation("{Provider}: {Domain} is unchanged ({IpAddress})",
                    provider.Type, record.Domain, address);
                return RecordOutcome.Unchanged;
            }

            logger.LogInformation("{Provider}: {Domain} updated from {PreviousAddress} to {IpAddress}",
                provider.Type, record.Domain, result.PreviousAddress ?? "unknown", address);

            if (config.NotifyOnChange)
                await notifier.SendAsync(
                    webhook,
                    $"DDNS updated {record.Domain} ({provider.Type}): " +
                    $"{result.PreviousAddress ?? "unknown"} -> {address}",
                    cancellationToken);

            return RecordOutcome.Updated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Provider}: failed to update {Domain}", provider.Type, record.Domain);

            if (config.NotifyOnError)
                await notifier.SendAsync(
                    webhook,
                    $"DDNS error for {record.Domain} ({provider.Type}): {exception.Message}",
                    cancellationToken);

            return RecordOutcome.Failed;
        }
    }

    private enum RecordOutcome
    {
        Updated,
        Unchanged,
        Failed
    }
}

public sealed record DdnsRunResult(IPAddress? PublicAddress, int Updated, int Unchanged, int Failed);
