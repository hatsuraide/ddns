using Ddns.Cli.Interactive;
using Ddns.Cli.Tui;

namespace Ddns.Tests.Cli;

public sealed class MenuHelperTests
{
    [Fact]
    public void MenuChoice_KeepsValueIndependentFromDisplayLabel()
    {
        var choice = new MenuChoice<MainMenuAction>(MainMenuAction.Save, "Persist configuration");

        Assert.Equal(MainMenuAction.Save, choice.Value);
        Assert.Equal("Persist configuration", choice.ToString());
        Assert.Equal("Persist configuration", choice.ToItem().Label);
    }

    [Fact]
    public void MenuChoice_RendersDescriptionAndSubmenuMarker()
    {
        var choice = new MenuChoice<MainMenuAction>(
            MainMenuAction.Accounts, "Accounts", "2 accounts", IsSubmenu: true);

        Assert.Equal("Accounts - 2 accounts", choice.ToString());

        var item = choice.ToItem();
        Assert.Equal("2 accounts", item.Description);
        Assert.True(item.IsSubmenu);
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    public void ShortIntervals_ParseAsPositiveTimespans(string input, int seconds)
    {
        Assert.True(PromptValidators.TryParseShortTimeSpan(input, out var value));
        Assert.Equal(TimeSpan.FromSeconds(seconds), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0m")]
    [InlineData("-1h")]
    [InlineData("1d")]
    public void InvalidShortIntervals_AreRejected(string input)
    {
        Assert.False(PromptValidators.TryParseShortTimeSpan(input, out _));
    }

    [Fact]
    public void ApiVersionLabels_DoNotDefineValues()
    {
        Assert.Equal("API v1", PromptLabels.ApiVersionLabel(ApiVersionChoice.V1));
        Assert.Equal("API v2", PromptLabels.ApiVersionLabel(ApiVersionChoice.V2));
        Assert.Equal(ApiVersionChoice.V1, Enum.Parse<ApiVersionChoice>("V1"));
    }

    [Theory]
    [InlineData("cloudflare", "Cloudflare")]
    [InlineData("MITTWALD", "Mittwald")]
    [InlineData("mittwald", "Mittwald")]
    public void ProviderLabels_AreReadable(string type, string expected)
    {
        Assert.Equal(expected, PromptLabels.ProviderLabel(type));
    }
}
