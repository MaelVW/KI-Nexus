using KiNexus.Core.Chats;
using KiNexus.Core.Kis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KiNexus.Core.Tests;

public class SystemPromptServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PromptVersions_KeepNewestTen_NewestFirst()
    {
        using var temp = new TempDirectory();
        var repo = await CreateRepositoryAsync(temp);

        for (var i = 1; i <= 12; i++)
        {
            await repo.AddPromptVersionAsync("mael-ki", $"Fassung {i}", SystemPromptService.MaxVersions, Ct);
        }

        await repo.AddPromptVersionAsync("andere-ki", "Nicht mitzählen", SystemPromptService.MaxVersions, Ct);
        var versions = await repo.GetPromptVersionsAsync("mael-ki", Ct);

        Assert.Equal(10, versions.Count);
        Assert.Equal("Fassung 12", versions[0].Content);
        Assert.Equal("Fassung 3", versions[^1].Content);
        Assert.All(versions, v => Assert.Equal("mael-ki", v.KiId));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Save_FirstTime_KeepsOldPromptAsVersion_AndWritesKiFile()
    {
        using var temp = new TempDirectory();
        var (service, kis, repo) = await CreateServiceAsync(temp);
        var ki = new KiProfile { Id = "mael-ki", DisplayName = "Mael-KI", SystemPrompt = "Alt" };
        await kis.SaveAsync(ki, Ct);

        var updated = await service.SaveAsync(ki, "Neu\n\n", Ct);
        var versions = await repo.GetPromptVersionsAsync("mael-ki", Ct);

        Assert.Equal("Neu", updated.SystemPrompt);
        Assert.Equal("Alt", ki.SystemPrompt); // Das Original bleibt unverändert, gespeichert wird eine Kopie.
        Assert.Equal(["Neu", "Alt"], versions.Select(v => v.Content));
        var reloaded = await new KiRegistry(temp.Combine("kis"), NullLogger<KiRegistry>.Instance).LoadAsync(Ct);
        Assert.Equal("Neu", Assert.Single(reloaded).SystemPrompt);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Save_SameTextAgain_AddsNoDuplicate()
    {
        using var temp = new TempDirectory();
        var (service, kis, repo) = await CreateServiceAsync(temp);
        var ki = new KiProfile { Id = "mael-ki", DisplayName = "Mael-KI", SystemPrompt = "Alt" };
        await kis.SaveAsync(ki, Ct);

        var first = await service.SaveAsync(ki, "Neu", Ct);
        await service.SaveAsync(first, "Neu", Ct);
        var restored = await service.SaveAsync(first, "Alt", Ct);

        Assert.Equal(["Alt", "Neu", "Alt"], (await repo.GetPromptVersionsAsync("mael-ki", Ct)).Select(v => v.Content));
        Assert.Equal("Alt", restored.SystemPrompt);
        SqliteConnection.ClearAllPools();
    }

    [Theory]
    [InlineData(null, "Du bist nett.\n- Kurz.", "Du bist nett.")]
    [InlineData("Du bist nett.\n- Kurz.", "Du bist nett.\n- Kurz.\n- Sag Grüezi.", "+ Sag Grüezi.")]
    [InlineData("Du bist nett.\n- Kurz.\n- Sag Grüezi.", "Du bist nett.\n- Kurz.", "− Sag Grüezi.")]
    [InlineData("Gleich.", "Gleich.\n\n", "Gleich.")]
    public void DescribeChange_ShowsWhatIsNew(string? older, string newer, string expected) =>
        Assert.Equal(expected, SystemPromptService.DescribeChange(older, newer));

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void EstimateTokens_EmptyPrompt_IsZero(string prompt, int expected) =>
        Assert.Equal(expected, SystemPromptService.EstimateTokens(prompt));

    [Fact]
    public void EstimateTokens_LongerPrompt_NeedsMore()
    {
        var shortPrompt = SystemPromptService.EstimateTokens("Du bist ein Assistent.");
        var longPrompt = SystemPromptService.EstimateTokens(string.Concat(Enumerable.Repeat("Du bist ein hilfsbereiter Assistent. ", 20)));

        Assert.True(shortPrompt > 0);
        Assert.True(longPrompt > shortPrompt * 10);
    }

    private static async Task<ChatRepository> CreateRepositoryAsync(TempDirectory temp)
    {
        var repo = new ChatRepository(temp.Combine("chats.db"), NullLogger<ChatRepository>.Instance);
        await repo.InitializeAsync(Ct);
        return repo;
    }

    private static async Task<(SystemPromptService Service, KiRegistry Kis, ChatRepository Repo)> CreateServiceAsync(TempDirectory temp)
    {
        var repo = await CreateRepositoryAsync(temp);
        var kis = new KiRegistry(temp.Combine("kis"), NullLogger<KiRegistry>.Instance);
        return (new SystemPromptService(kis, repo, NullLogger<SystemPromptService>.Instance), kis, repo);
    }
}
