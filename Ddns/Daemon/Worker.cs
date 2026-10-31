using Ddns.Config;
using Ddns.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ddns.Daemon;

public sealed class Worker(ILogger<Worker> logger, DdnsRunner runner) : BackgroundService
{
    private readonly Lock _configLock = new();
    private ConfigModel _activeConfig = new();
    private FileSystemWatcher? _configWatcher;
    private CancellationTokenSource? _reloadDebounce;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configStore = ConfigEnvironment.CreateStore();
        var loadedConfig = configStore.LoadRequired().Config;
        ConfigValidator.ThrowIfInvalid(loadedConfig);
        SetActiveConfig(loadedConfig);
        StartConfigWatcher(configStore, stoppingToken);

        logger.LogInformation(
            "DDNS worker started. ConfigPath: {ConfigPath}; Records: {RecordCount}; Interval: {Interval}",
            configStore.ConfigPath,
            CountRecords(loadedConfig),
            loadedConfig.Timeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = GetActiveConfig();
            var delay = config.Timeout > TimeSpan.Zero ? config.Timeout : TimeSpan.FromMinutes(5);

            logger.LogInformation("DDNS check started at {Time}", DateTimeOffset.Now);

            try
            {
                await runner.RunOnceAsync(config, ConfigEnvironment.IpServiceUrl, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "DDNS check failed");
            }

            logger.LogInformation("DDNS check finished at {Time}", DateTimeOffset.Now);

            await Task.Delay(delay, stoppingToken);
        }
    }

    private void StartConfigWatcher(ConfigStore configStore, CancellationToken stoppingToken)
    {
        var directory = Path.GetDirectoryName(configStore.ConfigPath);
        if (string.IsNullOrWhiteSpace(directory)) directory = Directory.GetCurrentDirectory();

        Directory.CreateDirectory(directory);

        _configWatcher = new FileSystemWatcher(directory, Path.GetFileName(configStore.ConfigPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _configWatcher.Changed += (_, _) => QueueConfigReload(configStore, stoppingToken);
        _configWatcher.Created += (_, _) => QueueConfigReload(configStore, stoppingToken);
        _configWatcher.Renamed += (_, _) => QueueConfigReload(configStore, stoppingToken);
    }

    private void QueueConfigReload(ConfigStore configStore, CancellationToken stoppingToken)
    {
        var previousDebounce = Interlocked.Exchange(
            ref _reloadDebounce,
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
        previousDebounce?.Cancel();
        previousDebounce?.Dispose();

        var debounce = _reloadDebounce;
        if (debounce is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), debounce.Token);
                var loadedConfig = configStore.LoadRequired().Config;
                ConfigValidator.ThrowIfInvalid(loadedConfig);
                SetActiveConfig(loadedConfig);
                runner.ResetState();

                logger.LogInformation(
                    "Reloaded config. Records: {RecordCount}; Interval: {Interval}",
                    CountRecords(loadedConfig),
                    loadedConfig.Timeout);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Config reload failed. Keeping previous valid config.");
            }
        }, CancellationToken.None);
    }

    private ConfigModel GetActiveConfig()
    {
        lock (_configLock)
        {
            return _activeConfig;
        }
    }

    private void SetActiveConfig(ConfigModel config)
    {
        lock (_configLock)
        {
            _activeConfig = config;
        }
    }

    private static int CountRecords(ConfigModel config)
    {
        return config.Accounts.Sum(account => account.Records.Count);
    }

    public override void Dispose()
    {
        _configWatcher?.Dispose();
        _reloadDebounce?.Dispose();
        base.Dispose();
    }
}
