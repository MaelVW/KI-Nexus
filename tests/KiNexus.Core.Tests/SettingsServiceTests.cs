using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

public class SettingsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_WithoutFile_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);

        var settings = await service.LoadAsync(Ct);

        Assert.Equal(8090, settings.Ports.LlamaServer);
        Assert.Equal(11434, settings.Ports.Ollama);
        Assert.Equal(8080, settings.Ports.OpenWebUi);
        Assert.Equal("#00E5FF", settings.AccentColor);
        Assert.True(settings.OpenWebUi.StopOnExit);
        Assert.Same(settings, service.Current);
    }

    [Fact]
    public async Task SaveThenLoad_KeepsAllValues()
    {
        using var temp = new TempDirectory();
        var settings = new AppSettings
        {
            AccentColor = "#B388FF",
            DefaultChatStyle = ChatStyle.Terminal,
            EngineOrder = EngineOrder.OllamaFirst,
        };
        settings.Ports.LlamaServer = 9001;
        settings.Paths.Models = @"D:\Modelle";
        settings.BackgroundTasks.GenerateChatTitles = false;
        settings.Window.LastTab = "createHub";

        await CreateService(temp).SaveAsync(settings, Ct);
        var loaded = await CreateService(temp).LoadAsync(Ct);

        Assert.Equal("#B388FF", loaded.AccentColor);
        Assert.Equal(ChatStyle.Terminal, loaded.DefaultChatStyle);
        Assert.Equal(EngineOrder.OllamaFirst, loaded.EngineOrder);
        Assert.Equal(9001, loaded.Ports.LlamaServer);
        Assert.Equal(@"D:\Modelle", loaded.Paths.Models);
        Assert.False(loaded.BackgroundTasks.GenerateChatTitles);
        Assert.Equal("createHub", loaded.Window.LastTab);
    }

    [Fact]
    public async Task Save_WritesReadableJson_WithEnglishCamelCaseKeys()
    {
        using var temp = new TempDirectory();

        await CreateService(temp).SaveAsync(new AppSettings(), Ct);
        var json = await File.ReadAllTextAsync(SettingsFile(temp), Ct);

        Assert.Contains("\"llamaServer\": 8090", json);
        Assert.Contains("\"defaultChatStyle\": \"simple\"", json);
        Assert.Contains("\"engineOrder\": \"llamaCppFirst\"", json);
        Assert.False(File.Exists(SettingsFile(temp) + ".tmp"));
    }

    [Fact]
    public async Task Save_RaisesSavedEvent()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        AppSettings? received = null;
        service.Saved += (_, s) => received = s;
        var settings = new AppSettings();

        await service.SaveAsync(settings, Ct);

        Assert.Same(settings, received);
    }

    [Fact]
    public async Task Load_WithBrokenJson_UsesDefaultsAndKeepsBackup()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(SettingsFile(temp), "{ das ist kein JSON", Ct);

        var settings = await CreateService(temp).LoadAsync(Ct);

        Assert.Equal(8090, settings.Ports.LlamaServer);
        Assert.False(File.Exists(SettingsFile(temp)));
        Assert.Single(Directory.GetFiles(temp.Path, "settings.json.kaputt-*"));
    }

    [Fact]
    public async Task Load_WithPartialJson_FillsMissingValuesWithDefaults()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(SettingsFile(temp), """{ "ports": { "ollama": 12000 } }""", Ct);

        var settings = await CreateService(temp).LoadAsync(Ct);

        Assert.Equal(12000, settings.Ports.Ollama);
        Assert.Equal(8090, settings.Ports.LlamaServer);
        Assert.Equal("models", settings.Paths.Models);
    }

    [Fact]
    public async Task Load_WithInvalidValues_ReplacesThemWithDefaults()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(SettingsFile(temp), """
            {
              "accentColor": "blau",
              "ports": { "llamaServer": 80, "openWebUi": 99999 },
              "paths": null,
              "window": { "width": 200 }
            }
            """, Ct);

        var settings = await CreateService(temp).LoadAsync(Ct);

        Assert.Equal("#00E5FF", settings.AccentColor);
        Assert.Equal(8090, settings.Ports.LlamaServer);
        Assert.Equal(8080, settings.Ports.OpenWebUi);
        Assert.Equal("models", settings.Paths.Models);
        Assert.Equal(1280, settings.Window.Width);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new AppSettings();

        var copy = SettingsJson.Clone(original);
        copy.Ports.LlamaServer = 9999;

        Assert.Equal(8090, original.Ports.LlamaServer);
    }

    private static SettingsService CreateService(TempDirectory temp) =>
        new(SettingsFile(temp), NullLogger<SettingsService>.Instance);

    private static string SettingsFile(TempDirectory temp) => temp.Combine("settings.json");
}
