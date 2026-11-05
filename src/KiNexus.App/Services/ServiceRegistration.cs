using System.IO;
using KiNexus.App.ViewModels;
using KiNexus.App.Views;
using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using KiNexus.Core.IO;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Processes;
using KiNexus.Core.Resources;
using KiNexus.Core.Settings;
using KiNexus.Core.Video;
using KiNexus.Engines;
using KiNexus.Engines.HuggingFace;
using KiNexus.Engines.LlamaCpp;
using KiNexus.Engines.Ollama;
using KiNexus.Engines.OpenWebUi;
using KiNexus.Engines.Training;
using KiNexus.Engines.Video;
using KiNexus.Engines.Voice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KiNexus.App.Services;

/// <summary>
/// Meldet alle Dienste, ViewModels und Fenster an (Dependency Injection).
/// So muss keine Klasse ihre Abhängigkeiten selbst erstellen.
/// </summary>
/// <remarks>
/// Dienste, die Pfade oder Ports aus den Einstellungen brauchen, werden erst beim ersten Gebrauch erstellt.
/// Dann sind die Einstellungen schon geladen. Änderungen an Pfaden und Ports wirken darum erst nach einem Neustart.
/// </remarks>
public static class ServiceRegistration
{
    public static ServiceProvider Build(AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ClearProviders().AddSerilog(dispose: false));
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ISettingsService>(provider =>
            new SettingsService(paths.SettingsFile, provider.GetRequiredService<ILogger<SettingsService>>()));
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IShellService, ShellService>();

        AddEngines(services, paths);
        AddChats(services, paths);
        foreach (var module in Modules.AppModules.Loaded)
        {
            module.ConfigureServices(services, paths);
        }

        services.AddSingleton<CreateHubViewModel>();
        services.AddSingleton<KiEditorServices>();
        services.AddSingleton<TuningViewModel>();
        services.AddSingleton<SystemPromptViewModel>();
        services.AddSingleton<TrainingViewModel>();
        services.AddSingleton(provider => new KiNexus.Core.Training.CustomTrainingModels(Path.Combine(paths.DataDirectory, "training", "eigene-modelle.json"),
            provider.GetRequiredService<ILogger<KiNexus.Core.Training.CustomTrainingModels>>()));
        services.AddSingleton<VideoViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void AddEngines(ServiceCollection services, AppPaths paths)
    {
        services.AddSingleton<IKiRegistry>(provider => new KiRegistry(paths.KisDirectory, provider.GetRequiredService<ILogger<KiRegistry>>()));
        services.AddSingleton(provider => new ModelLibrary(
            paths.Resolve(Settings(provider).Paths.Models), provider.GetRequiredService<ILogger<ModelLibrary>>()));
        services.AddSingleton<IResourceMonitor, ResourceMonitor>();
        services.AddSingleton<ProcessSupervisor>();

        services.AddSingleton(_ => new OllamaModelStore(OllamaModelStore.DefaultDirectory()));
        services.AddSingleton<OllamaImporter>();
        services.AddSingleton<ModelFactsProvider>();

        // Aus Ollamas Ordnern wird nie verschoben (Bauplan: Ollama-Dateien nie verändern).
        services.AddSingleton(provider => new ModelImporter(
            provider.GetRequiredService<ModelLibrary>(),
            [provider.GetRequiredService<OllamaModelStore>().ModelsDirectory,
             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama")],
            provider.GetRequiredService<ILogger<ModelImporter>>()));

        // Hugging Face: Token verschlüsselt (DPAPI) in data/hf-token.bin.
        services.AddSingleton(_ => new SecretStore(Path.Combine(paths.DataDirectory, "hf-token.bin")));
        services.AddSingleton(provider =>
        {
            var tokens = provider.GetRequiredService<SecretStore>();
            return new HuggingFaceClient(tokens.Load);
        });
        services.AddSingleton<ModelDownloader>();

        // Sprache (M4): Python-Worker für Mikrofon, Whisper und Piper, Wiedergabe mit NAudio.
        services.AddSingleton<VoiceWorker>();
        services.AddSingleton<PcmPlayer>();

        // Training (M8): Python-Worker mit PyTorch, Log in data/logs/training.log.
        services.AddSingleton(provider => new TrainingWorker(
            provider.GetRequiredService<ProcessSupervisor>(), provider.GetRequiredService<ILogger<TrainingWorker>>(),
            Path.Combine(paths.LogsDirectory, "training.log")));

        // Alternate Launch (M5): Open WebUI im Hintergrund, mit eigenem Job Object. Log in data/logs/open-webui.log.
        services.AddSingleton(provider => new OpenWebUiLauncher(
            provider.GetRequiredService<ProcessSupervisor>(), provider.GetRequiredService<ILogger<OpenWebUiLauncher>>(),
            Path.Combine(paths.LogsDirectory, "open-webui.log"), Path.Combine(paths.DataDirectory, "open-webui.processes")));

        // Video (Bauplan Video, V1): ComfyUI im Hintergrund, erst beim ersten Video gestartet. Log in data/logs/video.log.
        services.AddSingleton(provider =>
        {
            var settings = Settings(provider);
            var options = new ComfyOptions(
                VideoModelsDirectory: Path.Combine(paths.Resolve(settings.Paths.Models), "video"),
                OutputDirectory: Path.Combine(paths.DataDirectory, "videos", "roh"),
                WorkDirectory: Path.Combine(paths.DataDirectory, "comfyui"),
                LogFile: Path.Combine(paths.LogsDirectory, "video.log"),
                ImageModelsDirectory: Path.Combine(paths.Resolve(settings.Paths.Models), "image"),
                Port: settings.Ports.ComfyUi);
            return new ComfyVideoBackend(provider.GetRequiredService<ProcessSupervisor>(), provider.GetRequiredService<ILogger<ComfyVideoBackend>>(),
                ComfyInstallation.For(paths.Resolve(settings.Paths.Engines)), options);
        });
        services.AddSingleton<IVideoBackend>(provider => provider.GetRequiredService<ComfyVideoBackend>());
        services.AddSingleton(provider => new VideoLibrary(Path.Combine(paths.DataDirectory, "videos"), provider.GetRequiredService<ILogger<VideoLibrary>>()));
        services.AddSingleton(provider => new ImageLibrary(Path.Combine(paths.DataDirectory, "images"), provider.GetRequiredService<ILogger<ImageLibrary>>()));

        services.AddSingleton(provider =>
        {
            var settings = Settings(provider);
            var options = new LlamaCppOptions(
                ServerExe: Path.Combine(paths.Resolve(settings.Paths.Engines), "llama.cpp", "llama-server.exe"),
                Port: settings.Ports.LlamaServer,
                LogFile: Path.Combine(paths.LogsDirectory, "llama-server.log"));
            return new LlamaCppBackend(options, paths, provider.GetRequiredService<ProcessSupervisor>(),
                provider.GetRequiredService<ModelLibrary>(), provider.GetRequiredService<ILogger<LlamaCppBackend>>());
        });
        services.AddSingleton(provider => new OllamaBackend(Settings(provider).Ports.Ollama, provider.GetRequiredService<ILogger<OllamaBackend>>()));
        services.AddSingleton<IInferenceBackend>(provider => provider.GetRequiredService<LlamaCppBackend>());
        services.AddSingleton<IInferenceBackend>(provider => provider.GetRequiredService<OllamaBackend>());
        services.AddSingleton<EngineManager>();
    }

    private static void AddChats(ServiceCollection services, AppPaths paths)
    {
        services.AddSingleton<IChatRepository>(provider => new ChatRepository(paths.ChatDatabaseFile, provider.GetRequiredService<ILogger<ChatRepository>>()));
        services.AddSingleton(_ => new AttachmentStore(paths.AttachmentsDirectory));
        services.AddSingleton<ChatService>();
        services.AddSingleton<SystemPromptService>();
        services.AddSingleton(provider => new PromptTemplateStore(Path.Combine(paths.DataDirectory, "prompt-vorlagen.json"),
            provider.GetRequiredService<ILogger<PromptTemplateStore>>()));
        services.AddSingleton<KiReadinessViewModel>();
        services.AddSingleton<ChatSessionViewModel>();
        services.AddSingleton<ChatListViewModel>();
        services.AddSingleton<OpenWebUiViewModel>();
    }

    private static AppSettings Settings(IServiceProvider provider) => provider.GetRequiredService<ISettingsService>().Current;
}
