using KiNexus.Core.Inference;

namespace KiNexus.Core.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void Build_PutsSystemPromptFirst_ThenHistory_ThenQuestion()
    {
        var history = new[]
        {
            new ChatMessage(ChatRole.User, "Hallo"),
            new ChatMessage(ChatRole.Assistant, "Grüezi!"),
        };

        var messages = PromptBuilder.Build("Sei nett.", history, "  Wie geht's?  ");

        Assert.Collection(messages,
            m => Assert.Equal(new ChatMessage(ChatRole.System, "Sei nett."), m),
            m => Assert.Equal(ChatRole.User, m.Role),
            m => Assert.Equal(ChatRole.Assistant, m.Role),
            m => Assert.Equal(new ChatMessage(ChatRole.User, "Wie geht's?"), m));
    }

    [Fact]
    public void Build_WithoutSystemPrompt_SendsNoSystemMessage()
    {
        var messages = PromptBuilder.Build("   ", [], "Hi");

        Assert.Single(messages);
        Assert.Equal(ChatRole.User, messages[0].Role);
    }

    [Fact]
    public void Build_DropsSystemMessagesFromHistory()
    {
        var history = new[] { new ChatMessage(ChatRole.System, "alter Prompt") };

        var messages = PromptBuilder.Build("neuer Prompt", history, "Hi");

        Assert.Single(messages, m => m.Role == ChatRole.System);
        Assert.Equal("neuer Prompt", messages[0].Content);
    }
}
