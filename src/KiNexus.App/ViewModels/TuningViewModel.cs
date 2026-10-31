using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Core;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Resources;
using KiNexus.Engines;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Eine Wahl bei "Geladen halten". 0 Minuten = bis ich entlade.</summary>
public sealed record KeepAliveChoice(int Minutes, string Label)
{
    public override string ToString() => Label;

    public static KeepAliveChoice For(int minutes) => minutes switch
    {
        <= 0 => new(0, "Bis ich entlade"),
        1 => new(1, "1 Minute"),
        60 => new(60, "1 Stunde"),
        _ => new(minutes, $"{minutes} Minuten"),
    };
}

/// <summary>
/// Der Feineinstellungen-Tab (Bauplan 8.5): Parameter pro KI, jeder mit einer kurzen Erklärung.
/// Schätzt den Speicherbedarf und warnt, bevor RAM oder VRAM knapp werden.
/// </summary>
public sealed partial class TuningViewModel : KiEditorTabViewModel
{
    /// <summary>Kontextlängen zur Auswahl, höchstens bis zum Kontext aus dem Training des Modells.</summary>
    private static readonly int[] ContextSteps = [2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144];

    /// <summary>Weiss man nicht, wie viel das Modell kann, gehen wir nicht höher.</summary>
    private const int FallbackMaxContext = 32768;

    /// <summary>Damit vergleichen wir: Wird es mit mehr Kontext schlechter als mit dem Standard?</summary>
    private const int BaselineContext = 8192;

    /// <summary>Ab diesem Anteil von VRAM + nutzbarem RAM warnen wir "wird knapp".</summary>
    private const double TightShare = 0.85;

    private static readonly int[] KeepAliveSteps = [1, 5, 15, 30, 60, 0];

    private readonly ModelFactsProvider _factsProvider;
    private readonly IResourceMonitor _resources;
    private readonly EngineManager _engines;
    private readonly Dictionary<string, ModelFacts> _factsCache = [];
    private ModelFacts _facts = ModelFacts.Unknown;
    private string _factsKey = "";
    private ResourceSnapshot? _machine;
    private string _savedSnapshot = "";
    private int _factsVersion;
    private bool _loading;

    public TuningViewModel(KiEditorServices services, ModelFactsProvider factsProvider, IResourceMonitor resources, EngineManager engines,
        ILogger<TuningViewModel> logger)
        : base("tuning", "Feineinstellungen", Glyphs.Tuning, services, logger)
    {
        _factsProvider = factsProvider;
        _resources = resources;
        _engines = engines;

        Temperature = SettingFieldViewModel.Decimal("Temperatur", "Wie kreativ bzw. zufällig die Antworten sind.",
            "Tief (z.B. 0.3) = genauer und gleichförmiger, gut für Fakten und Code. Hoch (z.B. 1.2) = abwechslungsreicher, "
            + "aber auch fehleranfälliger. 0 = immer das wahrscheinlichste Wort.", 0, 2, Changed);
        TopP = SettingFieldViewModel.Decimal("top_p", "Wie viele Wort-Kandidaten das Modell in Betracht zieht (nach Anteil).",
            "Das Modell nimmt nur die wahrscheinlichsten Wörter, bis sie zusammen diesen Anteil erreichen. 0.95 = die besten 95 %. 1 = aus.",
            0, 1, Changed);
        TopK = SettingFieldViewModel.Integer("top_k", "Wie viele Wort-Kandidaten das Modell höchstens anschaut.",
            "Nur die k wahrscheinlichsten Wörter kommen in Frage, z.B. 40 oder 64. 0 = aus.", 0, () => 1000, allowEmpty: true, Changed);
        MinP = SettingFieldViewModel.Decimal("min_p", "Wie unwahrscheinlich ein Wort-Kandidat höchstens sein darf.",
            "Wörter mit weniger als diesem Anteil der Wahrscheinlichkeit des besten Worts fallen weg. 0.05 = 5 %. 0 = aus.", 0, 1, Changed);
        RepeatPenalty = SettingFieldViewModel.Decimal("Wiederholungs-Strafe", "Wie stark das Modell Wiederholungen vermeidet.",
            "1 = aus. Etwas mehr (z.B. 1.1) hilft, wenn die KI sich wiederholt. Zu viel macht die Sprache holprig.", 0.5, 2, Changed);
        PresencePenalty = SettingFieldViewModel.Decimal("Themen-Strafe", "Wie stark das Modell schon genannte Wörter meidet (presence penalty).",
            "0 = aus. Positiv (z.B. 0.5) = die KI wiederholt weniger und wechselt eher das Thema. Manche Hersteller empfehlen 1.5 gegen Endlosschleifen.",
            -2, 2, Changed);
        MaxTokens = SettingFieldViewModel.Integer("Max. Antwortlänge", "Wie lang eine Antwort höchstens wird (in Token, ca. 3/4 Wort).",
            "Danach hört die KI auf zu schreiben. 2048 Token sind ca. 1500 Wörter. Muss kleiner sein als die Kontextlänge.",
            16, () => ContextLength - 256, allowEmpty: false, Changed);
        GpuLayers = SettingFieldViewModel.Custom("GPU-Layer", "Wie viel vom Modell auf der Grafikkarte liegt.",
            "Leer = automatisch: llama.cpp verteilt das Modell selbst auf Grafikkarte und RAM (empfohlen). "
            + "Eine Zahl = so viele Schichten auf die Grafikkarte, der Rest kommt in den RAM. \"all\" = alles auf die Grafikkarte.",
            ValidateGpuLayers, Changed);
        CpuMoe = SettingFieldViewModel.Integer("MoE-Experten auf CPU", "Nur bei MoE-Modellen: wie viele Experten-Schichten auf die CPU kommen.",
            "Leer = automatisch. Mehr Schichten auf der CPU = weniger VRAM, aber langsamer. Der Rest des Modells bleibt auf der Grafikkarte.",
            0, () => _facts.BlockCount ?? 999, allowEmpty: true, Changed);

        SamplingFields = [Temperature, TopP, TopK, MinP, RepeatPenalty, PresencePenalty];
        EngineFields = [GpuLayers, CpuMoe];
    }

    // ----------------------------------------------------------- Felder

    public SettingFieldViewModel Temperature { get; }
    public SettingFieldViewModel TopP { get; }
    public SettingFieldViewModel TopK { get; }
    public SettingFieldViewModel MinP { get; }
    public SettingFieldViewModel RepeatPenalty { get; }
    public SettingFieldViewModel PresencePenalty { get; }
    public SettingFieldViewModel MaxTokens { get; }
    public SettingFieldViewModel GpuLayers { get; }
    public SettingFieldViewModel CpuMoe { get; }

    public IReadOnlyList<SettingFieldViewModel> SamplingFields { get; }

    public IReadOnlyList<SettingFieldViewModel> EngineFields { get; }

    [ObservableProperty] private IReadOnlyList<int> _contextLengths = ContextSteps.Where(c => c <= FallbackMaxContext).ToList();

    [ObservableProperty] private int _contextLength = BaselineContext;

    [ObservableProperty] private bool _thinkByDefault;

    [ObservableProperty] private IReadOnlyList<KeepAliveChoice> _keepAliveChoices = KeepAliveSteps.Select(KeepAliveChoice.For).ToList();

    [ObservableProperty] private KeepAliveChoice? _keepAlive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DraftHint))]
    private bool _useDraft;

    // ----------------------------------------------------------- Anzeige

    [ObservableProperty] private string _modelSummary = "";

    [ObservableProperty] private string _defaultsSource = "";

    [ObservableProperty] private bool _isReadingModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DraftHint))]
    private bool _hasDraft;

    [ObservableProperty] private bool _isLlamaCpp = true;

    [ObservableProperty] private FitEstimate? _fit;

    [ObservableProperty] private string _memoryText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryWarning))]
    private string _memoryWarning = "";

    [ObservableProperty] private bool _memoryWarningIsSevere;

    public bool HasMemoryWarning => MemoryWarning.Length > 0;

    public string DraftHint => !HasDraft
        ? ""
        : $"Ein kleines Hilfsmodell schlägt Wörter vor, das grosse prüft sie. Das Draft-Modell ({GbText(_facts.DraftBytes)} GB) liegt "
            + "zusätzlich auf der Grafikkarte. Gemessen mit gemma4 in M1: ca. 41–55 statt 40 Token/s, ca. 0.5 GB mehr VRAM.";

    public string EngineHint => IsLlamaCpp
        ? "Kontextlänge, GPU-Layer, MoE und Draft wirken beim nächsten Laden. Ist die KI gerade geladen, lädt die App sie bei der nächsten Frage neu."
        : "Bei Ollama verteilt Ollama das Modell selbst auf Grafikkarte und RAM. Die Kontextlänge wirkt beim nächsten Laden.";

    public override bool IsDirty => !_loading && SelectedKi is not null && Snapshot() != _savedSnapshot;

    private bool CanSave => IsDirty && AllValid;

    /// <summary>Für den Knopf "Verwerfen" (das Toolkit braucht einen eindeutigen Namen).</summary>
    private bool HasChanges => IsDirty;

    private bool AllValid => SamplingFields.Concat(EngineFields).Append(MaxTokens).All(f => f.IsValid);

    partial void OnContextLengthChanged(int value)
    {
        MaxTokens.Revalidate();
        UpdateEstimate();
        Changed();
    }

    partial void OnThinkByDefaultChanged(bool value) => Changed();

    partial void OnKeepAliveChanged(KeepAliveChoice? value) => Changed();

    partial void OnUseDraftChanged(bool value)
    {
        UpdateEstimate();
        Changed();
    }

    partial void OnIsLlamaCppChanged(bool value) => OnPropertyChanged(nameof(EngineHint));

    // ----------------------------------------------------------- Laden

    protected override void Load(KiProfile? ki)
    {
        _loading = true;
        try
        {
            if (ki is null)
            {
                _facts = ModelFacts.Unknown;
                ModelSummary = "";
                Fit = null;
                MemoryText = "";
                MemoryWarning = "";
                _savedSnapshot = "";
                return;
            }

            // Andere Modell-Datei: Alte Angaben weg, bis die neuen gelesen sind (sonst stimmt z.B. der Draft-Pfad nicht).
            if (FactsKey(ki) != _factsKey)
            {
                _factsKey = FactsKey(ki);
                _facts = ModelFacts.Unknown;
                HasDraft = false;
                Fit = null;
                MemoryText = "";
                MemoryWarning = "";
                ModelSummary = "";
            }

            var p = ki.Parameters;
            Temperature.Text = ParameterText.Format(p.Temperature);
            TopP.Text = ParameterText.Format(p.TopP);
            TopK.Text = ParameterText.Format(p.TopK);
            MinP.Text = ParameterText.Format(p.MinP);
            RepeatPenalty.Text = ParameterText.Format(p.RepeatPenalty);
            PresencePenalty.Text = ParameterText.Format(p.PresencePenalty);
            ContextLengths = ContextChoices(_facts.MaxContext, p.ContextLength);
            ContextLength = p.ContextLength;
            MaxTokens.Text = ParameterText.Format(p.MaxTokens);
            ThinkByDefault = p.ThinkByDefault;

            var engine = ki.EngineOptions;
            IsLlamaCpp = ki.Engine == EngineKind.LlamaCpp;
            GpuLayers.Text = string.Equals(engine.GpuLayers, "auto", StringComparison.OrdinalIgnoreCase) ? "" : engine.GpuLayers;
            GpuLayers.IsVisible = IsLlamaCpp;
            CpuMoe.Text = ParameterText.Format(engine.CpuMoeLayers);
            CpuMoe.IsVisible = IsLlamaCpp && (engine.CpuMoeLayers is not null || _facts.IsMoe);
            var keepAlive = KeepAliveChoice.For(engine.KeepAliveMinutes);
            KeepAliveChoices = KeepAliveSteps.Select(KeepAliveChoice.For).Append(keepAlive).Distinct().OrderBy(c => c.Minutes <= 0 ? int.MaxValue : c.Minutes).ToList();
            KeepAlive = keepAlive;
            UseDraft = engine.UseDraftModel;

            _savedSnapshot = Snapshot();
        }
        finally
        {
            _loading = false;
        }

        Changed();
        _ = LoadFactsAsync(ki);
    }

    /// <summary>Modell-Datei lesen (Grösse, Kontext, Empfehlungen). Im Hintergrund, die GGUF-Datei ist gross.</summary>
    private async Task LoadFactsAsync(KiProfile ki)
    {
        var version = ++_factsVersion;
        var key = FactsKey(ki);
        if (!_factsCache.TryGetValue(key, out var facts))
        {
            IsReadingModel = true;
            ModelSummary = "Modell-Datei wird gelesen …";
            facts = await Task.Run(() => _factsProvider.Describe(ki));
            _factsCache[key] = facts;
        }

        if (version != _factsVersion)
        {
            return;
        }

        IsReadingModel = false;
        _facts = facts;
        _machine ??= await Task.Run(_resources.Read);

        var current = ContextLength;
        ContextLengths = ContextChoices(facts.MaxContext, current);
        OnPropertyChanged(nameof(ContextLength));
        CpuMoe.IsVisible = IsLlamaCpp && (facts.IsMoe || CpuMoe.Text.Length > 0);
        CpuMoe.Revalidate();
        HasDraft = IsLlamaCpp && facts.HasDraft;
        ModelSummary = Summary(ki, facts);
        DefaultsSource = facts.Defaults.Source;
        SetHints(facts.Defaults);
        UpdateEstimate();
    }

    // ----------------------------------------------------------- Befehle

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (Current is not { } saved)
        {
            return;
        }

        var updated = Build(saved);
        var loadChanged = updated.Parameters.ContextLength != saved.Parameters.ContextLength
            || updated.EngineOptions.GpuLayers != saved.EngineOptions.GpuLayers
            || updated.EngineOptions.CpuMoeLayers != saved.EngineOptions.CpuMoeLayers
            || updated.EngineOptions.UseDraftModel != saved.EngineOptions.UseDraftModel;
        try
        {
            await Registry.SaveAsync(updated, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogError(ex, "Feineinstellungen für {Id} nicht gespeichert.", saved.Id);
            ShowStatus($"Speichern fehlgeschlagen: {ex.Message}", isError: true);
            return;
        }

        var text = loadChanged && _engines.LoadedKi?.Id == updated.Id
            ? "Gespeichert. Die KI ist gerade geladen: Bei der nächsten Frage lädt die App sie mit den neuen Werten neu (dauert einige Sekunden)."
            : "Gespeichert. Gilt ab der nächsten Frage.";
        Reload();
        var (ollamaText, isError) = await SyncOllamaModelAsync(updated);
        ShowStatus((text + " " + ollamaText).Trim(), isError);
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Discard()
    {
        Reload();
        ShowStatus("Änderungen verworfen.");
    }

    /// <summary>Werte des Herstellers (GGUF-Datei oder Ollama) und die Standards der App. Wird erst mit "Speichern" übernommen.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = _facts.Defaults;
        Temperature.Text = ParameterText.Format(defaults.Temperature);
        TopP.Text = ParameterText.Format(defaults.TopP);
        TopK.Text = ParameterText.Format(defaults.TopK);
        MinP.Text = ParameterText.Format(defaults.MinP);
        RepeatPenalty.Text = ParameterText.Format(defaults.RepeatPenalty);
        PresencePenalty.Text = ParameterText.Format(defaults.PresencePenalty);
        if (!ContextLengths.Contains(BaselineContext))
        {
            ContextLengths = [.. ContextLengths.Append(BaselineContext).Order()];
        }

        ContextLength = BaselineContext;
        MaxTokens.Text = "2048";
        ThinkByDefault = false;
        GpuLayers.Text = "";
        CpuMoe.Text = "";
        KeepAlive = KeepAliveChoices.FirstOrDefault(c => c.Minutes == 5);
        UseDraft = false;
        ShowStatus($"Standardwerte eingesetzt ({defaults.Source}). Noch nicht gespeichert.");
    }

    // ----------------------------------------------------------- Hilfen

    /// <summary>Eine Kopie der gespeicherten KI mit den Werten aus den Feldern.</summary>
    private KiProfile Build(KiProfile saved)
    {
        var ki = JsonDefaults.Clone(saved);
        var p = ki.Parameters;
        p.Temperature = Temperature.DecimalValue;
        p.TopP = TopP.DecimalValue;
        p.TopK = TopK.IntegerValue;
        p.MinP = MinP.DecimalValue;
        p.RepeatPenalty = RepeatPenalty.DecimalValue;
        p.PresencePenalty = PresencePenalty.DecimalValue;
        p.ContextLength = ContextLength;
        p.MaxTokens = MaxTokens.IntegerValue ?? p.MaxTokens;
        p.ThinkByDefault = ThinkByDefault;

        var engine = ki.EngineOptions;
        engine.KeepAliveMinutes = KeepAlive?.Minutes ?? engine.KeepAliveMinutes;
        if (ki.Engine == EngineKind.LlamaCpp)
        {
            var layers = GpuLayers.Text.Trim().ToLowerInvariant();
            engine.GpuLayers = layers.Length == 0 ? "auto" : layers;
            if (CpuMoe.IsVisible)
            {
                engine.CpuMoeLayers = CpuMoe.IntegerValue;
            }

            // Ist die Modell-Datei (noch) nicht gelesen, bleibt der Draft-Schalter, wie er war.
            if (HasDraft)
            {
                engine.UseDraftModel = UseDraft;
                ki.BaseModel.DraftPath = _facts.DraftPath;
            }
        }

        return ki;
    }

    private string Snapshot() => string.Join("|",
        Temperature.Text.Trim(), TopP.Text.Trim(), TopK.Text.Trim(), MinP.Text.Trim(), RepeatPenalty.Text.Trim(), PresencePenalty.Text.Trim(),
        ContextLength, MaxTokens.Text.Trim(), ThinkByDefault, GpuLayers.Text.Trim().ToLowerInvariant(), CpuMoe.Text.Trim(), KeepAlive?.Minutes, UseDraft);

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    private string? ValidateGpuLayers(string text)
    {
        var value = text.Trim().ToLowerInvariant();
        if (value is "" or "auto" or "all")
        {
            return null;
        }

        var max = (_facts.BlockCount ?? 998) + 1;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layers) && layers >= 0 && layers <= max
            ? null
            : $"Leer, \"all\" oder eine Zahl von 0 bis {max}.";
    }

    private void SetHints(SamplingDefaults defaults)
    {
        Temperature.Hint = Recommended(ParameterText.Format(defaults.Temperature));
        TopP.Hint = Recommended(ParameterText.Format(defaults.TopP));
        TopK.Hint = Recommended(ParameterText.Format(defaults.TopK));
        MinP.Hint = Recommended(ParameterText.Format(defaults.MinP));
        RepeatPenalty.Hint = Recommended(ParameterText.Format(defaults.RepeatPenalty));
        PresencePenalty.Hint = Recommended(ParameterText.Format(defaults.PresencePenalty));
        MaxTokens.Hint = "Standard: 2048";
        GpuLayers.Hint = _facts.BlockCount is { } blocks ? $"leer = automatisch · Modell hat {blocks} Schichten" : "leer = automatisch";
        CpuMoe.Hint = "leer = automatisch";
    }

    private static string Recommended(string value) => value.Length > 0 ? $"Empfehlung: {value}" : "leer = Standard der Engine";

    /// <summary>
    /// Die Ampel und die Warnung für die gewählte Kontextlänge. Wie im Create-Hub ist es eine Schätzung:
    /// Datei + Puffer für den Kontext, verglichen mit VRAM und 60 % des RAM.
    /// </summary>
    private void UpdateEstimate()
    {
        if (SelectedKi is null || _facts.ModelBytes == 0 || _machine is null)
        {
            Fit = null;
            MemoryText = SelectedKi is null || IsReadingModel ? "" : "Keine Schätzung möglich: Die Modell-Datei ist nicht lesbar.";
            MemoryWarning = "";
            return;
        }

        var extra = _facts.ExtraBytes + (UseDraft && HasDraft ? _facts.DraftBytes : 0);
        var fit = FitEstimator.Estimate(_facts.ModelBytes, extra, ContextLength, _machine.VramTotal, _machine.RamTotal);
        var baseline = FitEstimator.Estimate(_facts.ModelBytes, extra, Math.Min(BaselineContext, ContextLength), _machine.VramTotal, _machine.RamTotal);
        var budget = fit.VramBudgetBytes + fit.RamBudgetBytes;

        Fit = fit;
        MemoryText = $"Der Kontext braucht ca. {GbText(FitEstimator.ContextBufferBytes(ContextLength))} GB. {fit.Explanation}";
        MemoryWarningIsSevere = fit.Level == FitLevel.Red;
        MemoryWarning = fit.Level == FitLevel.Red
            ? $"Zu wenig Speicher: Mit {ContextLength} Token braucht die KI ca. {GbText(fit.NeedBytes)} GB, nutzbar sind ca. {GbText(budget)} GB. "
                + "Das Laden kann scheitern oder Windows wird sehr langsam. Wähle eine kleinere Kontextlänge."
            : fit.NeedBytes > budget * TightShare
                ? $"Speicher wird knapp: ca. {GbText(fit.NeedBytes)} von {GbText(budget)} GB. Andere Programme (Browser, Spiele) haben dann wenig Platz."
                : fit.Level > baseline.Level
                    ? $"Mit {ContextLength} Token passt die KI nicht mehr ganz auf die Grafikkarte. Ein Teil kommt in den RAM, die Antworten werden langsamer."
                    : "";
    }

    private static string FactsKey(KiProfile ki) => string.Join("|", ki.Engine, ki.BaseModel.Path, ki.BaseModel.MmprojPath, ki.BaseModel.OllamaName);

    private static IReadOnlyList<int> ContextChoices(int? maxContext, int current)
    {
        var max = maxContext ?? FallbackMaxContext;
        return [.. ContextSteps.Where(c => c <= max).Append(current).Append(Math.Min(max, ContextSteps[^1])).Distinct().Order()];
    }

    private static string Summary(KiProfile ki, ModelFacts facts)
    {
        if (facts.ModelBytes == 0)
        {
            return "Modell-Datei nicht gefunden oder nicht lesbar.";
        }

        var name = ki.Engine == EngineKind.Ollama ? $"Ollama: {ki.BaseModel.OllamaName}" : Path.GetFileName(ki.BaseModel.Path ?? "");
        var parts = new List<string> { name, $"{GbText(facts.ModelBytes)} GB" };
        if (facts.IsMoe)
        {
            parts.Add("MoE-Modell");
        }

        if (facts.MaxContext is { } max)
        {
            parts.Add($"bis {max} Token Kontext");
        }

        if (facts.BlockCount is { } blocks)
        {
            parts.Add($"{blocks} Schichten");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>z.B. "15.9" (immer mit Punkt, wie im Rest der App).</summary>
    private static string GbText(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture);
}
