using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KiNexus.App.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KiNexus.App.Avatar;

/// <summary>
/// Der Avatar (Erweiterung E3): ein kleines Browser-Feld (WebView2) mit dem Code aus dem Avatar-Testpanel (index.html?nurAvatar).
/// Sicherheit: nur die eigenen Dateien (https://avatar.kinexus) und der Ordner des Modells (https://modelle.kinexus),
/// alle anderen Anfragen werden blockiert, keine Navigation, keine neuen Fenster, keine DevTools, keine Kamera (erst E3.7).
/// Befehle gehen als JSON an die Seite (PostWebMessageAsJson), so wie im Testpanel beschrieben.
/// </summary>
public sealed class AvatarView : UserControl
{
    private const string AppHost = "avatar.kinexus";
    private const string ModelHost = "modelle.kinexus";

    public static readonly DependencyProperty AvatarFileProperty = DependencyProperty.Register(nameof(AvatarFile), typeof(string), typeof(AvatarView),
        new PropertyMetadata(null, (d, _) => ((AvatarView)d).OnAvatarFileChanged()));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(ConversationState), typeof(AvatarView),
        new PropertyMetadata(ConversationState.Off, (d, _) => ((AvatarView)d).SendState()));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(AvatarView),
        new PropertyMetadata(0.0)); // Der Takt (_mouthTimer) liest sie höchstens 20 Mal pro Sekunde.

    private readonly WebView2 _web = new() { DefaultBackgroundColor = System.Drawing.Color.Transparent };
    private readonly DispatcherTimer _mouthTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _ready;
    private double _lastMouth = -1;

    /// <summary>models/avatar im Projektordner: Nur hier liegen Avatare, nur diesen Ordner sieht die Seite.</summary>
    public static string AvatarFolder { get; } = Path.Combine(
        KiNexus.Core.Paths.AppPaths.Detect(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(KiNexus.Core.Paths.AppPaths.RootEnvironmentVariable)).Root,
        "models", "avatar");

    public AvatarView()
    {
        Content = _web;
        Loaded += async (_, _) => await InitializeAsync();
        Unloaded += (_, _) => _mouthTimer.Stop();
        _mouthTimer.Tick += (_, _) => SendMouth();
    }

    /// <summary>Volle Pfad zur VRM-Datei. Null = nichts laden.</summary>
    public string? AvatarFile
    {
        get => (string?)GetValue(AvatarFileProperty);
        set => SetValue(AvatarFileProperty, value);
    }

    public ConversationState State
    {
        get => (ConversationState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Lautstärke 0 bis 1. Beim Sprechen öffnet sie den Mund.</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Für die Abnahme und später E3.3: das letzte Ereignis der Seite (z.B. "loaded").</summary>
    public string LastEvent { get; private set; } = "";

    /// <summary>Zuhören und Verstehen heissen im Avatar "listening", Denken "thinking", Sprechen "speaking", der Rest "idle".</summary>
    public static string StateName(ConversationState state) => state switch
    {
        ConversationState.Listening or ConversationState.Hearing => "listening",
        ConversationState.Understanding or ConversationState.Thinking => "thinking",
        ConversationState.Speaking => "speaking",
        _ => "idle",
    };

    /// <summary>Mundöffnung aus der Lautstärke: leise = zu, ab ca. 0.5 ganz offen.</summary>
    public static double MouthFor(double level) => Math.Round(Math.Clamp((level - 0.03) * 2.2, 0, 1), 2);

    private async Task InitializeAsync()
    {
        if (_web.CoreWebView2 is not null)
        {
            return;
        }

        var appDir = AppContext.BaseDirectory;
        var web = Path.Combine(appDir, "Avatar", "web");
        var userData = Path.Combine(Path.GetTempPath(), "KI-Nexus-WebView2");
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or IOException)
        {
            Content = new TextBlock { Text = "Der Avatar braucht die WebView2-Runtime (bei Windows 11 dabei): " + ex.Message, TextWrapping = TextWrapping.Wrap };
            return;
        }

        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("WebView2 ist nicht bereit.");
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(AppHost, web, CoreWebView2HostResourceAccessKind.Deny);
        // Modelle nur aus models/avatar: Die Seite sieht keinen anderen Ordner. Vor dem Laden der Seite setzen.
        Directory.CreateDirectory(AvatarFolder);
        core.SetVirtualHostNameToFolderMapping(ModelHost, AvatarFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Host != AppHost)
            {
                e.Cancel = true;
            }
        };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

        // Jede Anfrage ausserhalb der beiden lokalen Adressen wird blockiert (kein CDN, kein Internet).
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "https" && uri.Host is AppHost or ModelHost)
            {
                return;
            }

            if (uri?.Scheme is "blob" or "data")
            {
                return;
            }

            Log("blockiert: " + e.Request.Uri);
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Gesperrt", "");
        };
        core.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson);
        // Fehler der Seite und blockierte Inhalte (CSP) kommen als Meldung zurück, fürs Protokoll.
        await core.AddScriptToExecuteOnDocumentCreatedAsync("""
            window.addEventListener('error', e => chrome.webview.postMessage({ type: 'jsError', message: String(e.message), source: String(e.filename) + ':' + e.lineno }));
            window.addEventListener('unhandledrejection', e => chrome.webview.postMessage({ type: 'jsError', message: String(e.reason && e.reason.message || e.reason) }));
            document.addEventListener('securitypolicyviolation', e => chrome.webview.postMessage({ type: 'cspBlocked', directive: e.violatedDirective, uri: String(e.blockedURI) }));
            """);
        core.NavigationCompleted += (_, _) =>
        {
            _ready = true;
            Post(new { type = "setLipSyncMode", mode = "aus" });
            OnAvatarFileChanged();
            SendState();
        };
        core.Navigate($"https://{AppHost}/index.html?nurAvatar");
    }

    /// <summary>Protokoll in data/logs/avatar.log (Meldungen der Seite, Fehler, blockierte Anfragen).</summary>
    private static void Log(string line)
    {
        try
        {
            var paths = KiNexus.Core.Paths.AppPaths.Detect(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(KiNexus.Core.Paths.AppPaths.RootEnvironmentVariable));
            Directory.CreateDirectory(paths.LogsDirectory);
            File.AppendAllText(Path.Combine(paths.LogsDirectory, "avatar.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Protokoll ist nicht wichtig genug, um den Avatar zu stören.
        }
    }

    private void OnMessage(string json)
    {
        Log("Seite: " + (json.Length > 400 ? json[..400] : json));
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("type", out var type))
            {
                LastEvent = type.GetString() ?? "";
                AutomationProperties_SetName();
            }
        }
        catch (JsonException)
        {
            // Unlesbare Meldung der Seite: ignorieren.
        }
    }

    /// <summary>Damit Tests (UI Automation) sehen, ob das Modell geladen ist.</summary>
    private void AutomationProperties_SetName() =>
        System.Windows.Automation.AutomationProperties.SetName(this, "Avatar: " + LastEvent);

    private void OnAvatarFileChanged()
    {
        if (!_ready || _web.CoreWebView2 is null)
        {
            return;
        }

        if (AvatarFile is not { } file || !File.Exists(file))
        {
            Post(new { type = "unload" });
            return;
        }

        var full = Path.GetFullPath(file);
        if (!full.StartsWith(AvatarFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Log("nicht in models/avatar: " + full);
            return;
        }

        var relative = string.Join('/', Path.GetRelativePath(AvatarFolder, full).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString));
        Post(new { type = "loadUrl", url = $"https://{ModelHost}/{relative}" });
    }

    private void SendState()
    {
        Post(new { type = "setState", state = StateName(State) });
        if (State == ConversationState.Speaking)
        {
            _mouthTimer.Start();
        }
        else
        {
            _mouthTimer.Stop();
            _lastMouth = -1;
            Post(new { type = "setMouth", value = 0.0 });
        }
    }

    private void SendMouth()
    {
        var mouth = MouthFor(Level);
        if (Math.Abs(mouth - _lastMouth) >= 0.03)
        {
            _lastMouth = mouth;
            Post(new { type = "setMouth", value = mouth });
        }
    }

    private void Post(object command)
    {
        if (_ready && _web.CoreWebView2 is { } core)
        {
            var json = JsonSerializer.Serialize(command);
            if (!json.Contains("setMouth", StringComparison.Ordinal))
            {
                Log("App: " + json);
            }

            core.PostWebMessageAsJson(json);
        }
    }
}
