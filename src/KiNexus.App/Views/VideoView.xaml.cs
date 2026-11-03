using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KiNexus.App.ViewModels;
using KiNexus.Core.Video;

namespace KiNexus.App.Views;

/// <summary>
/// Der Studio-Tab (Video und Bild). Den Player (MediaElement) steuert diese Datei: Er braucht Befehle wie Play und Pause,
/// die man nicht binden kann. Alles andere steht im <see cref="VideoViewModel"/>.
/// </summary>
public partial class VideoView : UserControl
{
    private VideoViewModel? _viewModel;
    private bool _isPlaying;

    /// <summary>Ein neues Video läuft sofort. Beim Öffnen des Tabs zeigt der Player nur das erste Bild.</summary>
    private bool _autoPlay;

    public VideoView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Open(_viewModel?.SelectedVideo, autoPlay: false);
        Unloaded += (_, _) => ClosePlayer();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = DataContext as VideoViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoViewModel.SelectedVideo) && IsLoaded)
        {
            Open(_viewModel?.SelectedVideo, autoPlay: true);
        }
    }

    /// <summary>Kein Video (oder ein Bild, das zeigt die Ansicht selbst): Der Player gibt die Datei frei (wichtig vor dem Löschen).</summary>
    private void Open(VideoEntry? video, bool autoPlay)
    {
        ClosePlayer();
        if (video is null or { IsImage: true })
        {
            return;
        }

        _autoPlay = autoPlay;
        Player.Source = new Uri(video.VideoPath);
        Player.Play(); // Öffnet die Datei. Ohne Autoplay hält MediaOpened gleich wieder an.
        SetPlaying(true);
    }

    private void ClosePlayer()
    {
        Player.Stop();
        Player.Close();
        Player.Source = null;
        PlayerError.Visibility = Visibility.Collapsed;
        SetPlaying(false);
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!_autoPlay)
        {
            Player.Pause();
            SetPlaying(false);
        }
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        // Zurück an den Anfang und anhalten. Ein Klick auf Abspielen startet es nochmals.
        Player.Stop();
        SetPlaying(false);
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        PlayerError.Text = "Das Video lässt sich hier nicht abspielen: " + e.ErrorException.Message
            + " Mit „Ordner öffnen“ kannst du es in einem anderen Player ansehen.";
        PlayerError.Visibility = Visibility.Visible;
        SetPlaying(false);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (Player.Source is null)
        {
            Open(_viewModel?.SelectedVideo, autoPlay: true);
            return;
        }

        if (_isPlaying)
        {
            Player.Pause();
        }
        else
        {
            _autoPlay = true;
            Player.Play();
        }

        SetPlaying(!_isPlaying);
    }

    private void SetPlaying(bool playing)
    {
        _isPlaying = playing;
        PlayPauseIcon.Text = playing ? Glyphs.Pause : Glyphs.Play;
        AutomationProperties.SetName(PlayPause, playing ? "Pause" : "Abspielen");
    }
}
