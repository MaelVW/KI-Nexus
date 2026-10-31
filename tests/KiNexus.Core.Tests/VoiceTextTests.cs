using KiNexus.Core.Chats;
using KiNexus.Core.Settings;
using KiNexus.Core.Voice;

namespace KiNexus.Core.Tests;

public class SpeechTextTests
{
    [Theory]
    [InlineData("**Fett** und *kursiv*", "Fett und kursiv")]
    [InlineData("## Überschrift", "Überschrift")]
    [InlineData("- erster Punkt", "erster Punkt")]
    [InlineData("> Ein Zitat", "Ein Zitat")]
    [InlineData("Nimm `dotnet build` dafür.", "Nimm dotnet build dafür.")]
    [InlineData("Siehe [die Doku](https://learn.microsoft.com) für mehr.", "Siehe die Doku für mehr.")]
    [InlineData("Mehr unter https://example.com hier.", "Mehr unter hier.")]
    [InlineData("Super gemacht! 🎉", "Super gemacht!")]
    [InlineData("1. Mehl kaufen", "1. Mehl kaufen")]
    public void ForSpeech_RemovesMarkdown(string markdown, string expected)
    {
        Assert.Equal(expected, SpeechText.ForSpeech(markdown));
    }

    [Fact]
    public void ForSpeech_CodeBlock_BecomesPlaceholder()
    {
        Assert.Equal(SpeechText.CodePlaceholder, SpeechText.ForSpeech("```csharp\nvar x = 1;\n```"));
    }

    [Fact]
    public void ForSpeech_Table_ReadsCellsWithCommas()
    {
        Assert.Equal("Name, Alter Mael, 18", SpeechText.ForSpeech("| Name | Alter |\n|---|---|\n| Mael | 18 |"));
    }

    [Theory]
    [InlineData("***")]
    [InlineData("  ")]
    [InlineData("---")]
    public void ForSpeech_NothingSpeakable_IsEmpty(string markdown)
    {
        Assert.Equal("", SpeechText.ForSpeech(markdown));
    }

    [Fact]
    public void NormalizeTranscript_UsesSwissSpelling()
    {
        Assert.Equal("Das ist eine grosse Strasse.", SpeechText.NormalizeTranscript("  Das ist eine große   Straße. "));
    }

    [Theory]
    [InlineData("Untertitel im Auftrag des ZDF für funk, 2017", true)]
    [InlineData("Untertitelung. BR 2018", true)]
    [InlineData("Vielen Dank fürs Zuschauen!", true)]
    [InlineData("Wie spät ist es?", false)]
    [InlineData("Vielen Dank!", false)]
    public void Hallucinations_AreDetected(string text, bool expected)
    {
        Assert.Equal(expected, SpeechText.IsLikelyHallucination(text));
    }
}

public class SentenceSplitterTests
{
    [Fact]
    public void SplitsWhileStreaming_AndWaitsForTheNextCharacter()
    {
        var splitter = new SentenceSplitter();

        Assert.Empty(splitter.Append("Hallo Mael"));
        Assert.Empty(splitter.Append("."));
        Assert.Equal(["Hallo Mael."], splitter.Append(" Wie geht"));
        Assert.Empty(splitter.Append("s dir?"));
        Assert.Equal(["Wie gehts dir?"], splitter.Flush());
    }

    [Fact]
    public void FirstSentence_IsReady_BeforeTheAnswerEnds()
    {
        var splitter = new SentenceSplitter();

        Assert.Equal(["Bern ist die Hauptstadt."], splitter.Append("Bern ist die Hauptstadt. Dort steht"));
    }

    [Theory]
    [InlineData("Das gilt z.B. für Bern. Und mehr.", "Das gilt z.B. für Bern.")]
    [InlineData("Es kostet ca. 5 Franken. Gut.", "Es kostet ca. 5 Franken.")]
    [InlineData("Am 1. August ist Feiertag. Ja.", "Am 1. August ist Feiertag.")]
    [InlineData("Version 3.5 ist neu. Ja.", "Version 3.5 ist neu.")]
    [InlineData("Wirklich?! Ja.", "Wirklich?!")]
    public void AbbreviationsAndNumbers_DoNotEndASentence(string text, string first)
    {
        var splitter = new SentenceSplitter();

        var sentences = splitter.Append(text).Concat(splitter.Flush()).ToList();

        Assert.Equal(first, sentences[0]);
        Assert.Equal(2, sentences.Count);
    }

    [Fact]
    public void LineBreaks_EndListItems()
    {
        var splitter = new SentenceSplitter();

        var sentences = splitter.Append("Du brauchst:\n- Mehl\n- Eier\n").Concat(splitter.Flush()).ToList();

        Assert.Equal(["Du brauchst:", "Mehl", "Eier"], sentences);
    }

    [Fact]
    public void CodeBlocks_AreNotRead_PlaceholderOnlyOnce()
    {
        var splitter = new SentenceSplitter();
        var parts = new[] { "So geht es:\n```cs", "harp\nvar x = 1;\n", "```\nUnd nochmals:\n```\nfoo();\n```\nFertig." };

        var sentences = parts.SelectMany(splitter.Append).ToList();
        sentences.AddRange(splitter.Flush());

        Assert.Equal(["So geht es:", SpeechText.CodePlaceholder, "Und nochmals:", "Fertig."], sentences);
    }

    [Fact]
    public void UnfinishedCodeBlock_IsNotRead_ButAnnounced()
    {
        var splitter = new SentenceSplitter();

        var sentences = splitter.Append("Hier:\n```\nabgebrochen").Concat(splitter.Flush()).ToList();

        Assert.Equal(["Hier:", SpeechText.CodePlaceholder], sentences);
    }

    [Fact]
    public void Markdown_IsRemovedFromSentences()
    {
        var splitter = new SentenceSplitter();

        var sentences = splitter.Append("**Wichtig:** Trink genug *Wasser*. ").Concat(splitter.Flush()).ToList();

        Assert.Equal(["Wichtig:", "Trink genug Wasser."], sentences);
    }

    [Fact]
    public void Reset_StartsFresh()
    {
        var splitter = new SentenceSplitter();
        splitter.Append("```\ncode");

        splitter.Reset();

        Assert.Equal(["Neu."], splitter.Append("Neu.").Concat(splitter.Flush()));
    }
}

public class ConversationPromptTests
{
    [Fact]
    public void Suffix_IsAddedWithBlankLine()
    {
        Assert.Equal("Du bist hilfsbereit.\n\nAntworte kurz und so, wie man spricht.",
            ChatService.WithSuffix("Du bist hilfsbereit.  ", SpeechText.ConversationHint));
    }

    [Fact]
    public void WithoutSuffix_PromptStaysTheSame()
    {
        Assert.Equal("Prompt", ChatService.WithSuffix("Prompt", null));
        Assert.Equal(SpeechText.ConversationHint, ChatService.WithSuffix("", SpeechText.ConversationHint));
    }

    [Fact]
    public void VoiceSettings_InvalidValues_AreRepaired()
    {
        var settings = new AppSettings();
        settings.Voice.SilenceMs = 50;
        settings.Voice.WhisperModel = " ";
        settings.Voice.PushToTalkKey = "";

        SettingsValidator.Normalize(settings);

        Assert.Equal(800, settings.Voice.SilenceMs);
        Assert.Equal("large-v3-turbo", settings.Voice.WhisperModel);
        Assert.Equal("Space", settings.Voice.PushToTalkKey);
    }
}
