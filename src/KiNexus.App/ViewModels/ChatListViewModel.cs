using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Chats;
using KiNexus.Core.Settings;

namespace KiNexus.App.ViewModels;

/// <summary>Ein Eintrag in der Seitenleiste.</summary>
public sealed partial class ChatListItem(ChatRecord chat) : ObservableObject
{
    public long Id => Chat.Id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(DateText))]
    private ChatRecord _chat = chat;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editTitle = "";

    public string Title => Chat.Title;

    /// <summary>Für Screenreader: Die Chat-Liste liest den Titel vor (statt des Klassennamens).</summary>
    public override string ToString() => Title;

    /// <summary>"14:03" für heute, "Gestern", sonst das Datum.</summary>
    public string DateText
    {
        get
        {
            var date = Chat.UpdatedAt.LocalDateTime;
            return date.Date == DateTime.Today ? date.ToString("HH:mm", CultureInfo.InvariantCulture)
                : date.Date == DateTime.Today.AddDays(-1) ? "Gestern"
                : date.ToString("dd.MM.yy", CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// Die einklappbare Seitenleiste mit allen Chats: neu, umbenennen, löschen, suchen (Bauplan 8.2).
/// </summary>
public sealed partial class ChatListViewModel : ObservableObject
{
    private readonly ChatService _chats;
    private readonly ChatSessionViewModel _session;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _searchDelay;
    private bool _selectingFromSession;

    public ChatListViewModel(ChatService chats, ChatSessionViewModel session, IDialogService dialogs, ISettingsService settings)
    {
        _chats = chats;
        _session = session;
        _dialogs = dialogs;
        _settings = settings;
        _isOpen = settings.Current.Chat.IsSidebarOpen;
        _session.ChatSaved += (_, chat) => OnChatSaved(chat);
    }

    public ObservableCollection<ChatListItem> Items { get; } = [];

    [ObservableProperty] private ChatListItem? _selectedItem;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isOpen;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Beim Start: Liste laden und den zuletzt offenen Chat öffnen.</summary>
    public async Task InitializeAsync()
    {
        await ReloadAsync();
        var last = Items.FirstOrDefault(i => i.Id == _settings.Current.Chat.LastChatId);
        if (last is not null)
        {
            SelectedItem = last;
        }
        else
        {
            _session.StartNewChat();
        }
    }

    [RelayCommand]
    private void NewChat()
    {
        _selectingFromSession = true;
        SelectedItem = null;
        _selectingFromSession = false;
        _session.StartNewChat();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsOpen = !IsOpen;

    [RelayCommand]
    private void StartRename(ChatListItem item)
    {
        foreach (var other in Items)
        {
            other.IsEditing = false;
        }

        item.EditTitle = item.Title;
        item.IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitRenameAsync(ChatListItem item)
    {
        if (!item.IsEditing)
        {
            return;
        }

        item.IsEditing = false;
        var title = item.EditTitle.Trim();
        if (title.Length == 0 || title == item.Title)
        {
            return;
        }

        var updated = await _chats.UpdateChatAsync(item.Chat with { Title = title.Length > ChatTitles.MaxLength ? title[..ChatTitles.MaxLength] : title }, CancellationToken.None);
        item.Chat = updated;
        if (_session.Chat?.Id == updated.Id)
        {
            _session.Chat = updated;
        }
    }

    [RelayCommand]
    private void CancelRename(ChatListItem item) => item.IsEditing = false;

    [RelayCommand]
    private async Task DeleteAsync(ChatListItem item)
    {
        if (_session.IsBusy && _session.Chat?.Id == item.Id)
        {
            return;
        }

        if (!_dialogs.Confirm("Chat löschen", $"Den Chat \"{item.Title}\" mit allen Nachrichten und Bildern löschen? Das geht nicht rückgängig.", "Löschen"))
        {
            return;
        }

        await _chats.DeleteChatAsync(item.Id, CancellationToken.None);
        Items.Remove(item);
        OnPropertyChanged(nameof(IsEmpty));
        if (_session.Chat?.Id == item.Id)
        {
            NewChat();
        }
    }

    partial void OnSelectedItemChanged(ChatListItem? value)
    {
        // Ein angefangenes Umbenennen wird übernommen, sobald man woanders hinklickt.
        foreach (var editing in Items.Where(i => i.IsEditing && i != value).ToList())
        {
            _ = CommitRenameAsync(editing);
        }

        if (value is not null && !_selectingFromSession)
        {
            _ = _session.OpenChatAsync(value.Id);
        }
    }

    /// <summary>Suche mit kurzer Verzögerung, damit nicht bei jedem Buchstaben gesucht wird.</summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchDelay?.Cancel();
        _searchDelay = new CancellationTokenSource();
        _ = SearchAfterDelayAsync(_searchDelay.Token);
    }

    partial void OnIsOpenChanged(bool value)
    {
        var settings = JsonDefaults.Clone(_settings.Current);
        settings.Chat.IsSidebarOpen = value;
        _ = _settings.SaveAsync(settings, CancellationToken.None);
    }

    private async Task SearchAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct);
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Es wurde weitergetippt.
        }
    }

    private async Task ReloadAsync()
    {
        var chats = await _chats.ListChatsAsync(SearchText, CancellationToken.None);
        var selectedId = SelectedItem?.Id ?? _session.Chat?.Id;
        _selectingFromSession = true;
        Items.Clear();
        foreach (var chat in chats)
        {
            Items.Add(new ChatListItem(chat));
        }

        SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId);
        _selectingFromSession = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Ein Chat wurde neu angelegt oder hat einen Titel bekommen: nach oben und markieren.</summary>
    private void OnChatSaved(ChatRecord chat)
    {
        _selectingFromSession = true;
        var item = Items.FirstOrDefault(i => i.Id == chat.Id);
        if (item is null)
        {
            item = new ChatListItem(chat);
            Items.Insert(0, item);
        }
        else
        {
            item.Chat = chat;
            var index = Items.IndexOf(item);
            if (index > 0)
            {
                Items.Move(index, 0);
            }
        }

        if (_session.Chat?.Id == chat.Id)
        {
            SelectedItem = item;
        }

        _selectingFromSession = false;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
