using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ddns.Config;

public sealed class ConfigStore
{
    public const string EncryptedFormat = "ddns.encrypted-config";
    public const string EncryptionAlgorithm = "AES-256-GCM";
    public const string DefaultConfigPath = "~/.config/ddns.json";

    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;

    private readonly string? _encryptionSecret;

    public ConfigStore(string configPath, string? encryptionSecret)
    {
        ConfigPath = ExpandPath(configPath);
        _encryptionSecret = encryptionSecret;
    }

    public string ConfigPath { get; }

    public bool CanEncrypt => !string.IsNullOrWhiteSpace(_encryptionSecret);

    public bool ExistsAndHasContent => File.Exists(ConfigPath) && new FileInfo(ConfigPath).Length > 0;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ConfigModel? Load()
    {
        if (!ExistsAndHasContent) return null;

        return LoadFromFile(ConfigPath, _encryptionSecret).Config;
    }

    public LoadedConfig LoadRequired()
    {
        if (!ExistsAndHasContent)
            throw new FileNotFoundException($"Config file does not exist or is empty: {ConfigPath}", ConfigPath);

        return LoadFromFile(ConfigPath, _encryptionSecret);
    }

    public void SaveEncrypted(ConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(_encryptionSecret))
            throw new InvalidOperationException("DDNS_SECRET is required to save encrypted config files.");

        var plainJson = JsonSerializer.Serialize(config, JsonOptions);
        var encryptedJson = JsonSerializer.Serialize(EncryptConfig(plainJson, _encryptionSecret), JsonOptions);
        WriteAllTextAtomic(ConfigPath, encryptedJson);
    }

    public void SavePlain(ConfigModel config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        WriteAllTextAtomic(ConfigPath, json);
    }

    public void Save(ConfigModel config, ConfigFileFormat format)
    {
        if (format == ConfigFileFormat.Encrypted)
        {
            SaveEncrypted(config);
            return;
        }

        SavePlain(config);
    }

    public static LoadedConfig LoadFromFile(string path, string? encryptionSecret)
    {
        path = ExpandPath(path);
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return new LoadedConfig(new ConfigModel(), ConfigFileFormat.Plain);

        var encryptedConfig = TryReadEncryptedConfig(json);
        if (encryptedConfig is not null)
        {
            if (string.IsNullOrWhiteSpace(encryptionSecret))
                throw new InvalidOperationException("DDNS_SECRET is required to decrypt the config file.");

            ValidateEncryptedHeader(encryptedConfig);
            var decryptedJson = DecryptConfig(encryptedConfig, encryptionSecret);
            return new LoadedConfig(ReadConfigModel(decryptedJson), ConfigFileFormat.Encrypted);
        }

        return new LoadedConfig(ReadConfigModel(json), ConfigFileFormat.Plain);
    }

    public static void WriteAllTextAtomic(string path, string text)
    {
        path = ExpandPath(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var targetDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;

        var tempPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, text);
            File.Move(tempPath, path, true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(DescribePermissionFailure(path, targetDirectory), exception);
        }
    }

    internal static string DescribePermissionFailure(string path, string directory)
    {
        return $"Cannot write '{path}': permission denied for directory '{directory}' " +
               $"(running as {DescribeCurrentUser()}). " +
               "In Docker the container user must own or be able to write the mounted config directory: " +
               "set PUID/PGID in .env to your host user ids, or run the container with " +
               "--user \"$(id -u):$(id -g)\". On the host, fix the ownership with " +
               $"sudo chown -R \"$(id -u):$(id -g)\" '{directory}'.";
    }

    private static string DescribeCurrentUser()
    {
        if (OperatingSystem.IsWindows()) return $"'{Environment.UserName}'";

        try
        {
            return $"'{Environment.UserName}' (uid {GetEffectiveUserId()})";
        }
        catch
        {
            return $"'{Environment.UserName}'";
        }
    }

    public static string ExpandPath(string path)
    {
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);

        return path;
    }

    private static ConfigModel ReadConfigModel(string json)
    {
        var migratedJson = ConfigMigration.Migrate(json);

        return JsonSerializer.Deserialize<ConfigModel>(migratedJson, JsonOptions)
               ?? throw new InvalidOperationException("Config file is empty or invalid.");
    }

    private static EncryptedConfig? TryReadEncryptedConfig(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            var hasEnvelope = document.RootElement.TryGetProperty("format", out var format)
                              && (string.Equals(format.GetString(), EncryptedFormat, StringComparison.Ordinal)
                                  || string.Equals(format.GetString(), "mittwald-ddns.encrypted-config",
                                      StringComparison.Ordinal));
            var hasLegacyEnvelope = document.RootElement.TryGetProperty("ciphertext", out _)
                                    || document.RootElement.TryGetProperty("nonce", out _)
                                    || document.RootElement.TryGetProperty("tag", out _);
            if (!hasEnvelope && !hasLegacyEnvelope) return null;

            return JsonSerializer.Deserialize<EncryptedConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateEncryptedHeader(EncryptedConfig encryptedConfig)
    {
        if (!string.Equals(encryptedConfig.Format, EncryptedFormat, StringComparison.Ordinal)
            && !string.Equals(encryptedConfig.Format, "mittwald-ddns.encrypted-config", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported encrypted config format.");

        if (encryptedConfig.Version != 1)
            throw new InvalidOperationException($"Unsupported encrypted config version: {encryptedConfig.Version}.");

        if (!string.Equals(encryptedConfig.Algorithm, EncryptionAlgorithm, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Unsupported encrypted config algorithm: {encryptedConfig.Algorithm}.");

        if (string.IsNullOrWhiteSpace(encryptedConfig.Ciphertext)
            || string.IsNullOrWhiteSpace(encryptedConfig.Nonce)
            || string.IsNullOrWhiteSpace(encryptedConfig.Tag))
            throw new InvalidOperationException("Encrypted config is missing ciphertext, nonce, or tag.");
    }

    private static EncryptedConfig EncryptConfig(string text, string secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(text);
        var ciphertext = new byte[plaintext.Length];
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var tag = new byte[TagSizeInBytes];

        using var aes = new AesGcm(DeriveKey(secret), TagSizeInBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new EncryptedConfig
        {
            Ciphertext = Convert.ToBase64String(ciphertext),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag)
        };
    }

    private static string DecryptConfig(EncryptedConfig encryptedConfig, string secret)
    {
        var ciphertext = Convert.FromBase64String(encryptedConfig.Ciphertext);
        var nonce = Convert.FromBase64String(encryptedConfig.Nonce);
        var tag = Convert.FromBase64String(encryptedConfig.Tag);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(DeriveKey(secret), TagSizeInBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}

public sealed record LoadedConfig(ConfigModel Config, ConfigFileFormat Format);

public enum ConfigFileFormat
{
    Plain,
    Encrypted
}
