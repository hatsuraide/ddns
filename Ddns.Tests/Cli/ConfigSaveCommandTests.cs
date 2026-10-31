using System.Text.Json;
using Ddns.Config;
using Ddns.Core;

namespace Ddns.Tests.Cli;

[Collection(EnvironmentCollection.Name)]
public sealed class ConfigSaveCommandTests
{
    private const string Secret = "test-secret";

    [Fact]
    public async Task Save_EncryptsWhenSecretIsSetEvenIfTheImportIsPlain()
    {
        using var fixture = new ConfigSaveFixture();
        var source = fixture.WriteSource();

        var exitCode = await fixture.RunAsync("config", "save", "--file", source);

        Assert.Equal(0, exitCode);
        var saved = File.ReadAllText(fixture.TargetPath);
        Assert.Contains(ConfigStore.EncryptedFormat, saved);
        Assert.DoesNotContain("api-key", saved);

        var loaded = ConfigStore.LoadFromFile(fixture.TargetPath, Secret);
        Assert.Equal(ConfigFileFormat.Encrypted, loaded.Format);
        Assert.Equal("home.example.com", loaded.Config.Accounts[0].Records[0].Domain);
    }

    [Fact]
    public async Task Save_WritesPlainOnlyWhenNoSecretIsConfigured()
    {
        using var fixture = new ConfigSaveFixture(secret: null);
        var source = fixture.WriteSource();

        var exitCode = await fixture.RunAsync("config", "save", "--file", source);

        Assert.Equal(0, exitCode);
        var saved = File.ReadAllText(fixture.TargetPath);
        Assert.Contains("\"api_key\"", saved);
        Assert.DoesNotContain(ConfigStore.EncryptedFormat, saved);
    }

    [Fact]
    public async Task Save_PlainFlagOptsOutOfEncryption()
    {
        using var fixture = new ConfigSaveFixture();
        var source = fixture.WriteSource();

        var exitCode = await fixture.RunAsync("config", "save", "--file", source, "--plain");

        Assert.Equal(0, exitCode);
        var saved = File.ReadAllText(fixture.TargetPath);
        Assert.Contains("\"api_key\"", saved);
        Assert.DoesNotContain(ConfigStore.EncryptedFormat, saved);
    }

    [Fact]
    public async Task Save_PlainFlagRefusesToDecryptAnEncryptedImport()
    {
        using var fixture = new ConfigSaveFixture();
        var encryptedSource = Path.Combine(fixture.Directory, "encrypted-source.json");
        var config = CreateConfig();
        new ConfigStore(encryptedSource, Secret).SaveEncrypted(config);

        var exitCode = await fixture.RunAsync("config", "save", "--file", encryptedSource, "--plain");

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(fixture.TargetPath));
    }

    private static ConfigModel CreateConfig()
    {
        return new ConfigModel
        {
            Accounts =
            [
                new ConfigAccount
                {
                    Type = ProviderTypes.Cloudflare,
                    Name = "cf-main",
                    ApiKey = "api-key",
                    Records = [new ConfigRecord { Domain = "home.example.com" }]
                }
            ]
        };
    }

    private sealed class ConfigSaveFixture : IDisposable
    {
        private readonly string? _previousPath;
        private readonly string? _previousSecret;

        public ConfigSaveFixture(string? secret = Secret)
        {
            Directory = Path.Combine(Path.GetTempPath(), $"ddns-save-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            TargetPath = Path.Combine(Directory, "ddns.json");

            _previousPath = Environment.GetEnvironmentVariable("DDNS_CONFIG_PATH");
            _previousSecret = Environment.GetEnvironmentVariable("DDNS_SECRET");

            Environment.SetEnvironmentVariable("DDNS_CONFIG_PATH", TargetPath);
            Environment.SetEnvironmentVariable("DDNS_SECRET", secret);
        }

        public string Directory { get; }

        public string TargetPath { get; }

        public string WriteSource()
        {
            var path = Path.Combine(Directory, "legacy.json");
            ConfigStore.WriteAllTextAtomic(
                path, JsonSerializer.Serialize(CreateConfig(), ConfigStore.JsonOptions));
            return path;
        }

        public Task<int> RunAsync(params string[] args)
        {
            return CliApplication.Create().RunAsync(args);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("DDNS_CONFIG_PATH", _previousPath);
            Environment.SetEnvironmentVariable("DDNS_SECRET", _previousSecret);

            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
