using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using KiNexus.Core.Tests;
using KiNexus.Engines.Voice;

namespace KiNexus.Engines.Tests;

public class VoiceProtocolTests
{
    [Fact]
    public void Configure_ContainsPathsDeviceAndPause()
    {
        var json = VoiceProtocol.Configure(new VoiceWorkerOptions(@"C:\m\whisper-small", @"C:\m\piper\de.onnx", "cpu", null, 800)).ToJsonString();

        Assert.Contains("\"type\":\"configure\"", json, StringComparison.Ordinal);
        Assert.Contains("\"device\":\"cpu\"", json, StringComparison.Ordinal);
        Assert.Contains("\"silenceMs\":800", json, StringComparison.Ordinal);
        Assert.Contains("\"microphone\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Commands_HaveTheRightShape()
    {
        Assert.Equal("""{"type":"listen","mode":"handsFree"}""", VoiceProtocol.Listen("handsFree").ToJsonString());
        Assert.Equal("""{"type":"stop_listening","transcribe":true}""", VoiceProtocol.StopListening(true).ToJsonString());
        Assert.Equal("""{"type":"speak","id":7,"text":"Hallo"}""", VoiceProtocol.Speak(7, "Hallo").ToJsonString());
        Assert.Equal("""{"type":"resume"}""", VoiceProtocol.Resume().ToJsonString());
        Assert.Equal("""{"type":"watch"}""", VoiceProtocol.Watch().ToJsonString());
        Assert.Equal("""{"type":"voice","voicePath":"C:\\v.onnx","speakerId":110,"lengthScale":1.1}""",
            VoiceProtocol.ChangeVoice(@"C:\v.onnx", 110, 1.1).ToJsonString());
    }

    [Fact]
    public void Configure_SendsSpeakerAndTempo()
    {
        var json = VoiceProtocol.Configure(new VoiceWorkerOptions("w", "v", "cpu", null, 800, SpeakerId: 120, LengthScale: 1.25)).ToJsonString();

        Assert.Contains("\"speakerId\":120", json, StringComparison.Ordinal);
        Assert.Contains("\"lengthScale\":1.25", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_BargeInAndVoiceChanged()
    {
        Assert.IsType<VoiceBargeIn>(VoiceProtocol.Parse("""{"type":"barge_in"}"""));
        Assert.Equal(new VoiceChanged(16000, 950), VoiceProtocol.Parse("""{"type":"voice_changed","sampleRate":16000,"loadMs":950}"""));
    }

    [Fact]
    public void Parse_ReadsAllEvents()
    {
        Assert.Equal(new VoiceReady("cpu", 22050, 7464), VoiceProtocol.Parse("""{"type":"ready","device":"cpu","sampleRate":22050,"loadMs":7464}"""));
        Assert.Equal(new VoiceTranscript("Bern ist schön.", 2900, 3074, null),
            VoiceProtocol.Parse("""{"type":"transcript","text":"Bern ist schön.","audioMs":2900,"tookMs":3074}"""));
        Assert.Equal(new VoiceTranscript("", 120, 0, "zu kurz"),
            VoiceProtocol.Parse("""{"type":"transcript","text":"","audioMs":120,"tookMs":0,"reason":"zu kurz"}"""));
        Assert.Equal(new VoiceListening("pushToTalk", "Microphone Array"), VoiceProtocol.Parse("""{"type":"listening","mode":"pushToTalk","microphone":"Microphone Array"}"""));
        Assert.Equal(new VoiceLevel(0.25), VoiceProtocol.Parse("""{"type":"level","value":0.25}"""));
        Assert.Equal(new VoiceSpeakDone(3, true), VoiceProtocol.Parse("""{"type":"speak_done","id":3,"cancelled":true}"""));
        Assert.Equal(new VoiceError("Kaputt", true), VoiceProtocol.Parse("""{"type":"error","message":"Kaputt","fatal":true}"""));
        Assert.IsType<VoiceSpeechStart>(VoiceProtocol.Parse("""{"type":"speech_start"}"""));
        Assert.Equal(new VoiceUnknown("neu"), VoiceProtocol.Parse("""{"type":"neu"}"""));
        Assert.Null(VoiceProtocol.Parse(" "));
    }

    [Fact]
    public void Parse_Audio_DecodesBase64()
    {
        var audio = Assert.IsType<VoiceAudio>(VoiceProtocol.Parse("""{"type":"audio","id":2,"pcm":"AQID"}"""));

        Assert.Equal(2, audio.Id);
        Assert.Equal(new byte[] { 1, 2, 3 }, audio.Pcm);
    }

    [Fact]
    public void Parse_Microphones()
    {
        var microphones = Assert.IsType<VoiceMicrophones>(VoiceProtocol.Parse(
            """{"type":"microphones","items":["Microphone Array (Realtek(R) Audio)","Kopfhörer (JBL Tune 520BT)"],"default":"Microphone Array (Realtek(R) Audio)"}"""));

        Assert.Equal(2, microphones.Items.Count);
        Assert.Equal("Kopfhörer (JBL Tune 520BT)", microphones.Items[1]);
        Assert.Equal("Microphone Array (Realtek(R) Audio)", microphones.Default);
    }
}

public class VoiceInstallationTests
{
    [Fact]
    public void NothingInstalled_ExplainsTheSetupScript()
    {
        using var root = new TempDirectory();
        var installation = VoiceInstallation.For(new AppPaths(root.Path), root.Combine("models"));

        Assert.Contains("setup-voice.ps1", installation.Problem(new VoiceSettings()), StringComparison.Ordinal);
    }

    [Fact]
    public void MultiSpeakerVoice_OffersChosenSpeakers()
    {
        using var root = new TempDirectory();
        var installation = Install(root, whisper: ["small"], voices: ["de_DE-mls-medium", "de_DE-ramona-low"]);

        Assert.Equal(["de_DE-mls-medium#110", "de_DE-mls-medium#120", "de_DE-ramona-low"], installation.Voices());
        Assert.Equal("de_DE-mls-medium#110", installation.ChooseVoice("de_DE-mls-medium#110"));
        Assert.EndsWith(Path.Combine("piper", "de_DE-mls-medium.onnx"), installation.VoicePath("de_DE-mls-medium#110"), StringComparison.Ordinal);
        Assert.Equal(110, VoiceInstallation.SpeakerId("de_DE-mls-medium#110"));
        Assert.Null(VoiceInstallation.SpeakerId("de_DE-ramona-low"));
        Assert.Equal("Ramona (weiblich, jung)", VoiceInstallation.DisplayName("de_DE-ramona-low"));
        Assert.Equal("de_DE-neu-low", VoiceInstallation.DisplayName("de_DE-neu-low"));
    }

    [Fact]
    public void FindsModelsAndVoices_AndPrefersThorsten()
    {
        using var root = new TempDirectory();
        var installation = Install(root, whisper: ["large-v3-turbo", "small"], voices: ["de_DE-kerstin-low", "de_DE-thorsten-high"]);

        Assert.Equal(["large-v3-turbo", "small"], installation.WhisperModels());
        Assert.Equal(["de_DE-kerstin-low", "de_DE-thorsten-high"], installation.Voices());
        Assert.Equal("de_DE-thorsten-high", installation.ChooseVoice(null));
        Assert.Equal("de_DE-kerstin-low", installation.ChooseVoice("de_DE-kerstin-low"));
        Assert.Equal("de_DE-thorsten-high", installation.ChooseVoice("gibt-es-nicht"));
        Assert.Null(installation.Problem(new VoiceSettings()));
    }

    [Fact]
    public void MissingWhisperModel_NamesTheCommand()
    {
        using var root = new TempDirectory();
        var installation = Install(root, whisper: ["small"], voices: ["de_DE-thorsten-high"]);

        var problem = installation.Problem(new VoiceSettings { WhisperModel = "large-v3-turbo" });

        Assert.Contains("-Whisper large-v3-turbo", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void VoiceWithoutJson_DoesNotCount()
    {
        using var root = new TempDirectory();
        var installation = Install(root, whisper: ["small"], voices: []);
        File.WriteAllText(Path.Combine(installation.VoiceModelsDirectory, "piper", "halb.onnx"), "");

        Assert.Empty(installation.Voices());
        Assert.Contains("-Voice", installation.Problem(new VoiceSettings { WhisperModel = "small" }), StringComparison.Ordinal);
    }

    private static VoiceInstallation Install(TempDirectory root, string[] whisper, string[] voices)
    {
        var installation = VoiceInstallation.For(new AppPaths(root.Path), root.Combine("models"));
        Directory.CreateDirectory(Path.GetDirectoryName(installation.PythonExe)!);
        File.WriteAllText(installation.PythonExe, "");
        Directory.CreateDirectory(Path.GetDirectoryName(installation.WorkerScript)!);
        File.WriteAllText(installation.WorkerScript, "");
        foreach (var model in whisper)
        {
            Directory.CreateDirectory(installation.WhisperPath(model));
            File.WriteAllText(Path.Combine(installation.WhisperPath(model), "model.bin"), "");
        }

        Directory.CreateDirectory(Path.Combine(installation.VoiceModelsDirectory, "piper"));
        foreach (var voice in voices)
        {
            File.WriteAllText(installation.VoicePath(voice), "");
            File.WriteAllText(installation.VoicePath(voice) + ".json", "{}");
        }

        return installation;
    }
}
