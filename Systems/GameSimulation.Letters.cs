namespace Rimtera.Systems;

/// <summary>What a letter is about, which sets its colour: red for threats, amber for bad news, green for good, grey otherwise.</summary>
public enum LetterKind : byte { Threat, BadNews, Good, Neutral }

/// <summary>
/// A letter (0.6.3, docs/VISION.md §4.3): the storyteller's word on something that matters, kept on the side of the screen
/// until it is read. A warning letter carries the hour its threat is due, and counts down to it.
/// </summary>
public sealed record Letter(long Id, LetterKind Kind, string Title, string Body, int Day, float Hour, float? DueHour = null, string? Tag = null);

/// <summary>
/// Letters (0.6.3): the storyteller writes when something matters (a raid on its way, an incident, a death, an arrival).
/// They stack on the side of the screen; a warning counts down to its threat. Reading one dismisses it; the oldest go first
/// when too many pile up, and ordinary letters fade after a day. Letters are saved with the run.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Most letters kept at once.</summary>
    public const int LetterCapacity = 8;
    /// <summary>Game hours an ordinary letter stays unread before it fades.</summary>
    public const float LetterLifeHours = 24f;

    private readonly List<Letter> _letters = new();
    private long _nextLetterId = 1;

    /// <summary>The letters waiting to be read, oldest first.</summary>
    public IReadOnlyList<Letter> Letters => _letters;

    /// <summary>Sends a letter; returns it. A letter with the same <paramref name="tag"/> replaces the older one.</summary>
    public Letter SendLetter(LetterKind kind, string title, string body, float? dueHour = null, string? tag = null)
    {
        if (tag is not null)
            _letters.RemoveAll(letter => letter.Tag == tag);
        var letter = new Letter(_nextLetterId++, kind, title, body, Day, ElapsedGameHours, dueHour, tag);
        _letters.Add(letter);
        while (_letters.Count > LetterCapacity)
            _letters.RemoveAt(0);
        return letter;
    }

    /// <summary>Puts a letter away (it was read). False when there is no such letter.</summary>
    public bool DismissLetter(long id) => _letters.RemoveAll(letter => letter.Id == id) > 0;

    /// <summary>Hours until a warning letter's threat is due (0 when due or not a warning).</summary>
    public float LetterHoursLeft(Letter letter) =>
        letter.DueHour is { } due ? Math.Max(0f, due - ElapsedGameHours) : 0f;

    /// <summary>A countdown in real minutes and seconds ("2:30"), as the HUD shows it.</summary>
    public static string Countdown(float gameHours)
    {
        var seconds = (int)MathF.Ceiling(Math.Max(0f, gameHours) / GameHoursPerRealSecond);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>Each step: ordinary letters fade after a day; a warning goes once its hour has passed.</summary>
    private void UpdateLetters()
    {
        if (_letters.Count == 0)
            return;
        _letters.RemoveAll(letter => letter.DueHour is { } due
            ? ElapsedGameHours > due + 0.25f
            : ElapsedGameHours - letter.Hour > LetterLifeHours);
    }

    void IStoryHost.Report(string incidentId, IncidentCategory category, string title, string summary)
    {
        PostMessage(summary);
        // A raid and an invasion write their own warning, with a countdown; an act's key event writes its own letter.
        if (incidentId is "raid" or "invasion" or "purpose")
            return;
        var kind = category switch
        {
            IncidentCategory.MajorThreat => LetterKind.Threat,
            IncidentCategory.MinorThreat => LetterKind.BadNews,
            _ => LetterKind.Good
        };
        SendLetter(kind, title, summary);
    }

    private Letter[]? CaptureLetters() => _letters.Count == 0 ? null : _letters.ToArray();

    private void RestoreLetters(Letter[]? letters)
    {
        _letters.Clear();
        foreach (var letter in (letters ?? []).TakeLast(LetterCapacity))
            if (letter is not null && Enum.IsDefined(letter.Kind) && !string.IsNullOrWhiteSpace(letter.Title) &&
                float.IsFinite(letter.Hour) && (letter.DueHour is null || float.IsFinite(letter.DueHour.Value)))
                _letters.Add(letter with { Body = letter.Body ?? string.Empty });
        _nextLetterId = _letters.Count == 0 ? 1 : _letters.Max(letter => letter.Id) + 1;
    }
}
