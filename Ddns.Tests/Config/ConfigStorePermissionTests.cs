using Ddns.Config;

namespace Ddns.Tests.Config;

public sealed class ConfigStorePermissionTests
{
    [Fact]
    public void DescribePermissionFailure_ExplainsContainerAndHostFixes()
    {
        var message = ConfigStore.DescribePermissionFailure(
            "/config/ddns.json", "/config");

        Assert.Contains("/config/ddns.json", message);
        Assert.Contains("/config", message);
        Assert.Contains("permission denied", message);
        Assert.Contains("PUID/PGID", message);
        Assert.Contains("--user", message);
        Assert.Contains("chown", message);
    }

    [Fact]
    public void WriteAllTextAtomic_LeavesNoTempFilesBehind()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ddns-perm-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "ddns.json");

        try
        {
            ConfigStore.WriteAllTextAtomic(path, "{}");

            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_ReportsPermissionFailureForUnwritableDirectory()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;

        var directory = Path.Combine(Path.GetTempPath(), $"ddns-ro-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var exception = Assert.Throws<UnauthorizedAccessException>(
                () => ConfigStore.WriteAllTextAtomic(Path.Combine(directory, "ddns.json"), "{}"));

            Assert.Contains("permission denied", exception.Message);
            Assert.Contains("PUID/PGID", exception.Message);
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(directory, true);
        }
    }
}
