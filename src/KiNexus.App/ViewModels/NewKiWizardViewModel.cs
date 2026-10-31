using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Engines.Ollama;

namespace KiNexus.App.ViewModels;

/// <summary>Ein wählbares Grundmodell im Assistenten (aus models/ oder aus Ollama).</summary>
public sealed record ModelChoice(string Title, string Subtitle, EngineKind Engine, LocalModel? Local, string? OllamaName, long SizeBytes)
{
    public string EngineText => Engine == EngineKind.Ollama ? "Ollama" : "llama.cpp";

    public string Key => Local?.FolderName ?? "ollama:" + OllamaName;

    public override string ToString() => Title;
}

/// <summary>
/// Assistent "Neue KI" in 4 Schritten (Bauplan 8.3): Grundmodell, Name, Systemprompt, Parameter.
/// Speichert als kis/&lt;id&gt;.json und bietet danach einen Test-Chat an.
/// </summary>
public sealed partial class NewKiWizardViewModel(CreateHubViewModel hub, IKiRegistry kis, OllamaBackend ollama) : ObservableObject
{
    public const int StepCount = 4;

    private static readonly string[] Titles = ["Grundmodell wählen", "Name und Beschreibung", "Systemprompt", "Parameter"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepTitle), nameof(StepText), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsLastStep), nameof(IsNotLastStep))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    private int _step = 1;

    public string StepTitle => Titles[Step - 1];
    public string StepText => $"Schritt {Step} von {StepCount}";
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool IsLastStep => Step == StepCount;
    public bool IsNotLastStep => Step != StepCount;

    // ----------------------------------------------------------- Schritt 1: Grundmodell

    public ObservableCollection<ModelChoice> Models { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fit), nameof(HasModels))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private ModelChoice? _selectedModel;

    public bool HasModels => Models.Count > 0;

    [ObservableProperty] private string _ollamaHint = "";

    // ----------------------------------------------------------- Schritt 2: Name

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdPreview))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(SaveCommand))]
    private string _name = "";

    [ObservableProperty] private string _description = "";

    /// <summary>"Wird gespeichert als kis/mein-coach.json".</summary>
    public string IdPreview => Name.Trim().Length == 0 ? "" : $"Wird gespeichert als kis/{KiIds.FromName(Name, kis.All.Select(k => k.Id))}.json";

    // ----------------------------------------------------------- Schritt 3: Systemprompt

    public IReadOnlyList<PromptTemplate> PromptTemplates => KiTemplates.Prompts;

    [ObservableProperty] private string _systemPrompt = KiTemplates.Prompts[0].Text;

    [RelayCommand]
    private void ApplyTemplate(PromptTemplate template) => SystemPrompt = template.Text;

    // ----------------------------------------------------------- Schritt 4: Parameter

    public IReadOnlyList<ParameterPreset> Presets => KiTemplates.Parameters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetValuesText))]
    private ParameterPreset _selectedPreset = KiTemplates.Parameters[0];

    public IReadOnlyList<int> ContextLengths => KiTemplates.ContextLengths;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fit))]
    private int _contextLength = 8192;

    [ObservableProperty] private bool _thinkByDefault;

    /// <summary>Ampel für Modell + gewählte Kontextlänge.</summary>
    public FitEstimate? Fit => SelectedModel switch
    {
        { Local: { } local } => hub.Estimate(local, ContextLength),
        { SizeBytes: > 0 } ollamaModel => hub.Estimate(ollamaModel.SizeBytes, 0, ContextLength),
        _ => null,
    };

    /// <summary>Welche Werte die Vorlage setzt, z.B. "Temperatur 0.3 · Top-P 0.9 · Top-K 40".</summary>
    public string PresetValuesText
    {
        get
        {
            var (temperature, topP, topK) = EffectiveValues();
            return temperature is null && topP is null && topK is null
                ? "Keine eigenen Werte: Die Engine nimmt den Standard des Modells."
                : string.Join(" · ", new[]
                {
                    temperature is { } t ? $"Temperatur {t.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : null,
                    topP is { } p ? $"Top-P {p.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : null,
                    topK is { } k ? $"Top-K {k}" : null,
                }.OfType<string>());
        }
    }

    // ----------------------------------------------------------- Ergebnis

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    private KiProfile? _savedKi;

    public bool IsSaved => SavedKi is not null;

    [ObservableProperty] private string _error = "";

    /// <summary>Liest die Modelle neu. Mit <paramref name="model"/> oder <paramref name="ollamaName"/> ist eines schon gewählt.</summary>
    public async Task PrepareAsync(LocalModel? model, string? ollamaName, bool keepSelection = false)
    {
        var previous = keepSelection ? SelectedModel?.Key : null;
        if (!keepSelection)
        {
            Reset();
        }

        // Zuerst die Modelle aus models/ (geht sofort), dann die von Ollama. Läuft Ollama nicht,
        // dauert die Absage unter Windows ca. 2 s. Darauf soll der Assistent nicht warten.
        var wanted = model is not null ? model.FolderName : ollamaName is not null ? "ollama:" + ollamaName : previous;
        Models.Clear();
        // Bild- und Video-Modelle (z.B. für ComfyUI) taugen nicht für eine KI.
        foreach (var local in (await Task.Run(hub.LibraryScan)).Where(m => m.IsLanguageModel))
        {
            Models.Add(new ModelChoice(local.FolderName,
                $"{DisplayFormat.Bytes(local.Manifest.SizeBytes)} · {local.Manifest.Quantization ?? "?"} · Bilder {(local.SupportsImages ? "ja" : "nein")}",
                EngineKind.LlamaCpp, local, null, local.Manifest.SizeBytes));
        }

        SelectedModel = Models.FirstOrDefault(m => m.Key == wanted);
        OnPropertyChanged(nameof(HasModels));
        if (model is not null)
        {
            Step = 2;
        }

        OllamaHint = "Ollama-Modelle werden gesucht …";
        try
        {
            foreach (var info in await ollama.ListModelsAsync(CancellationToken.None))
            {
                Models.Add(new ModelChoice(info.Name, $"{DisplayFormat.Bytes(info.SizeBytes)} · in Ollama", EngineKind.Ollama, null, info.Name, info.SizeBytes));
            }

            OllamaHint = "";
        }
        catch (InferenceException)
        {
            OllamaHint = "Ollama läuft nicht. Darum fehlen hier die Ollama-Modelle.";
        }

        if (SelectedModel is null && Models.FirstOrDefault(m => m.Key == wanted) is { } ollamaChoice)
        {
            SelectedModel = ollamaChoice;
            if (ollamaName is not null)
            {
                Step = 2;
            }
        }

        OnPropertyChanged(nameof(HasModels));
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    private bool CanGoBack => Step > 1 && !IsSaved;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Step++;

    private bool CanGoNext => Step switch
    {
        1 => SelectedModel is not null,
        2 => Name.Trim().Length > 0,
        3 => true,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedModel is not { } choice)
        {
            return;
        }

        Error = "";
        var (temperature, topP, topK) = EffectiveValues();
        var ki = new KiProfile
        {
            Id = KiIds.FromName(Name, kis.All.Select(k => k.Id)),
            DisplayName = Name.Trim(),
            Description = Description.Trim(),
            Engine = choice.Engine,
            SystemPrompt = SystemPrompt.Trim(),
            Parameters =
            {
                Temperature = temperature,
                TopP = topP,
                TopK = topK,
                ContextLength = ContextLength,
                ThinkByDefault = ThinkByDefault,
            },
        };

        if (choice.Local is { } local)
        {
            ki.BaseModel.Path = hub.ToKiPath(local.ModelPath);
            ki.BaseModel.MmprojPath = local.MmprojPath is { } mmproj ? hub.ToKiPath(mmproj) : null;
        }
        else
        {
            ki.BaseModel.OllamaName = choice.OllamaName;
        }

        try
        {
            await kis.SaveAsync(ki, CancellationToken.None);
            SavedKi = ki;
            BackCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = "Speichern hat nicht geklappt: " + ex.Message;
        }
    }

    private bool CanSave => SelectedModel is not null && Name.Trim().Length > 0 && !IsSaved;

    [RelayCommand]
    private void StartTestChat()
    {
        if (SavedKi is { } ki)
        {
            hub.Shell?.OpenChatWith(ki);
        }
    }

    [RelayCommand]
    private void StartOver()
    {
        Reset();
        _ = PrepareAsync(null, null);
    }

    partial void OnSavedKiChanged(KiProfile? value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnSelectedModelChanged(ModelChoice? value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PresetValuesText));
    }

    /// <summary>"Standard" nimmt die Werte des Herstellers aus model.json (stammen aus der GGUF-Datei), falls vorhanden.</summary>
    private (double? Temperature, double? TopP, int? TopK) EffectiveValues()
    {
        if (SelectedPreset.Temperature is not null || SelectedPreset.TopP is not null || SelectedPreset.TopK is not null)
        {
            return (SelectedPreset.Temperature, SelectedPreset.TopP, SelectedPreset.TopK);
        }

        return SelectedModel?.Local?.Manifest is { } manifest ? (manifest.Temperature, manifest.TopP, manifest.TopK) : (null, null, null);
    }

    private void Reset()
    {
        Step = 1;
        SelectedModel = null;
        Name = "";
        Description = "";
        SystemPrompt = KiTemplates.Prompts[0].Text;
        SelectedPreset = KiTemplates.Parameters[0];
        ContextLength = 8192;
        ThinkByDefault = false;
        SavedKi = null;
        Error = "";
    }
}
