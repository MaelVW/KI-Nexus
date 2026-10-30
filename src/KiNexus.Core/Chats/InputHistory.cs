namespace KiNexus.Core.Chats;

/// <summary>
/// Frühere Eingaben im Terminal, wie in einer echten Shell: Pfeil hoch = ältere, Pfeil runter = neuere.
/// </summary>
public sealed class InputHistory(int capacity = 100)
{
    private readonly List<string> _entries = [];
    private int _position;
    private string _draft = "";

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Merkt sich eine abgeschickte Eingabe. Doppelte direkt hintereinander zählen einmal.</summary>
    public void Add(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        if (_entries.Count == 0 || _entries[^1] != entry)
        {
            _entries.Add(entry);
            if (_entries.Count > capacity)
            {
                _entries.RemoveAt(0);
            }
        }

        ResetPosition();
    }

    /// <summary>Pfeil hoch. Beim ersten Mal wird der angefangene Text gemerkt.</summary>
    public string Previous(string current)
    {
        if (_entries.Count == 0)
        {
            return current;
        }

        if (_position == _entries.Count)
        {
            _draft = current;
        }

        _position = Math.Max(0, _position - 1);
        return _entries[_position];
    }

    /// <summary>Pfeil runter. Nach der neuesten Eingabe kommt wieder der angefangene Text.</summary>
    public string Next()
    {
        if (_position >= _entries.Count)
        {
            return _draft;
        }

        _position++;
        return _position == _entries.Count ? _draft : _entries[_position];
    }

    public void ResetPosition()
    {
        _position = _entries.Count;
        _draft = "";
    }
}
