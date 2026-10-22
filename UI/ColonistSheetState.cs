using Rimtera.AI;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// Read-only colonist dossier. The same panel serves character creation previews
/// and the live in-game roster opened with the Colonist binding.
/// </summary>
public sealed class ColonistSheetState : UiState
{
    private readonly CharacterProfile _profile;
    private readonly GameSimulation? _game;
    private readonly Action _close;
    private readonly UiPanelElement _panel;
    private int _page;
    private int _subject;

    public ColonistSheetState(CharacterProfile profile, GameSimulation? game, Action close)
        : base("colonist-sheet", style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) })
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _game = game;
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _panel = new UiPanelElement("colonist-sheet-panel", new UiStyle
        {
            Width = UiLength.Pct(0.92f),
            Height = UiLength.Mixed(-96f, 0.92f),
            MinHeight = 330f,
            MaxHeight = 620f,
            MaxWidth = 1100f,
            HAlign = 0.5f,
            VAlign = 0.5f,
            Padding = UiThickness.Uniform(18f),
            OverflowHidden = true
        });
        Add(_panel);
        Build();
    }

    public override void OnEnter() => Build();

    private void Build()
    {
        _panel.ClearChildren();
        _panel.Add(new UiTextElement(
            _profile.Name,
            "colonist-sheet-title",
            new UiStyle
            {
                Width = UiLength.Pct(1f),
                Height = UiLength.Px(34f),
                Top = UiLength.Px(0f),
                HAlign = 0.5f
            })
        { Color = UiColor.HeaderBlue });

        AddPageButton("Overview", 0, 0.00f);
        AddPageButton("Skills", 1, 0.125f);
        AddPageButton("Health", 3, 0.25f);
        AddPageButton("Mood", 4, 0.375f);
        AddPageButton("Social", 5, 0.50f);
        AddPageButton("Crew", 2, 0.625f);
        if (_game is not null && _page is 3 or 4 or 5)
            AddSubjectButton(0.75f);
        AddPageButton("Close", -1, 0.875f, close: true);

        switch (_page)
        {
            case 0: BuildOverview(); break;
            case 1: BuildSkillsPage(); break;
            case 3: BuildHealthPage(); break;
            case 4: BuildMoodPage(); break;
            case 5: BuildSocialPage(); break;
            default: BuildCrewPage(); break;
        }
    }

    private void AddPageButton(string label, int page, float left, bool close = false)
    {
        var button = new UiButtonElement(label, () =>
        {
            if (close)
            {
                _close();
                return;
            }
            _page = page;
            Build();
        }, $"colonist-sheet-{label.ToLowerInvariant()}", new UiStyle
        {
            Left = UiLength.Pct(left),
            Width = UiLength.Pct(0.12f),
            Height = UiLength.Px(30f),
            Top = UiLength.Mixed(-36f, 1f),
            HAlign = 0f,
            Padding = new UiThickness(6f, 3f, 6f, 3f)
        })
        {
            Color = !close && _page == page ? UiColor.ButtonActive : UiColor.ButtonDefault
        };
        _panel.Add(button);
    }

    /// <summary>A line of a sheet column: a section caption, a name with its value on the right, a note, or a gap.</summary>
    private readonly record struct Line(string Label, string? Value = null, bool Header = false, bool Note = false)
    {
        public static Line Head(string text) => new(text, Header: true);
        public static Line Row(string label, string value) => new(label, value);
        public static Line Text(string text) => new(text, Note: true);
        public static readonly Line Gap = new(string.Empty);
    }

    private static string Percent(float value) => $"{MathF.Round(value * 100f):0}%";
    private static string Times(float value) => $"{value:0.00}×";

    private void BuildOverview()
    {
        var stats = _game?.Player.ColonistStats ?? _profile.Stats;
        var needs = _game?.Player.Needs ?? PreviewNeeds();
        var health = _game?.Player.Health;
        var hitPoints = _game?.Player.HitPoints ?? _profile.HitPoints;
        var maximumHitPoints = _game?.Player.MaximumHitPoints ?? _profile.EffectiveMaximumHealth;
        var mood = _game?.Player.Mood ?? ColonistMoodCalculator.Calculate(needs, stats, health);
        var injured = health?.GetPartStatuses().Count(part => part.Missing || part.Health < part.MaxHealth - 0.5f) ?? 0;

        AddColumn(0, new List<Line>
        {
            Line.Head("About"),
            Line.Row("Gender", GenderName(_profile.Gender.ToString())),
            Line.Row("Age", $"{_profile.Age}"),
            Line.Row("Backstory", _profile.BackstoryName),
            Line.Row("Traits", _profile.TraitSummary),
            Line.Gap,
            Line.Head("Needs"),
            Line.Row("Mood", $"{Percent(mood)}  {ColonistMoodCalculator.Label(mood)}"),
            Line.Row("Food", Percent(needs.FoodReserve)),
            Line.Row("Rest", Percent(needs.RestReserve)),
            Line.Row("Joy", Percent(needs.JoyReserve)),
            Line.Row("Comfort", Percent(needs.ComfortReserve)),
            Line.Gap,
            Line.Head("Health"),
            Line.Row("Life", $"{hitPoints:0} / {maximumHitPoints:0}"),
            Line.Row("Injuries", health is null ? "None" : injured == 0 ? "None" : $"{injured} (see Health)"),
            Line.Row("Pain", Percent(health?.PainLevel ?? 0f)),
            Line.Row("Consciousness", Percent(health?.Consciousness ?? 1f)),
            Line.Row("Moving", Percent(Capacity(health, BodyPartFunction.MovingLimbCore))),
            Line.Row("Manipulation", Percent(Capacity(health, BodyPartFunction.ManipulationLimbCore)))
        });

        var right = new List<Line> { Line.Head("Top skills") };
        right.AddRange(TopSkills(stats, 6));
        right.Add(Line.Gap);
        right.Add(Line.Head("Combat"));
        right.Add(Line.Row("Melee hit / dodge", $"{Percent(stats.MeleeHitChance)} / {Percent(stats.MeleeDodgeChance)}"));
        right.Add(Line.Row("Melee damage", Times(stats.MeleeDamageMultiplier)));
        right.Add(Line.Row("Shooting accuracy", Percent(stats.ShootingAccuracy)));
        right.Add(Line.Row("Shooting damage", Times(stats.ShootingDamageMultiplier)));
        right.Add(Line.Gap);
        right.Add(Line.Head("Work"));
        right.Add(Line.Row("Move speed", Times(stats.MoveSpeedMultiplier)));
        right.Add(Line.Row("Work speed", Times(stats.GlobalWorkSpeed)));
        right.Add(Line.Row("Mining", Times(stats.MiningSpeed)));
        right.Add(Line.Row("Construction", Times(stats.ConstructionSpeed)));
        right.Add(Line.Row("Crafting", Times(stats.CraftingSpeed)));
        AddColumn(1, right);
    }

    private void BuildSkillsPage()
    {
        var stats = _game?.Player.ColonistStats ?? _profile.Stats;
        var skills = new List<Line> { Line.Head("Skills") };
        foreach (var skill in Enum.GetValues<ColonistSkill>())
            skills.Add(Line.Row(ColonistCatalog.SkillName(skill), SkillValue(stats.Passion(skill), $"{stats.Level(skill)} / 20")));
        skills.Add(Line.Gap);
        skills.Add(Line.Text("▲ interested and ▲▲ burning passions learn faster."));
        AddColumn(0, skills);

        AddColumn(1, new List<Line>
        {
            Line.Head("Work speed"),
            Line.Row("Mining", Times(stats.MiningSpeed)),
            Line.Row("Construction", Times(stats.ConstructionSpeed)),
            Line.Row("Plants", Times(stats.PlantWorkSpeed)),
            Line.Row("Cooking", Times(stats.CookingSpeed)),
            Line.Row("Crafting", Times(stats.CraftingSpeed)),
            Line.Row("Research", Times(stats.ResearchSpeed)),
            Line.Row("Medical tending", Percent(stats.MedicalTendQuality)),
            Line.Row("Social impact", Percent(stats.SocialImpact)),
            Line.Gap,
            Line.Head("Combat"),
            Line.Row("Melee hit", Percent(stats.MeleeHitChance)),
            Line.Row("Dodge", Percent(stats.MeleeDodgeChance)),
            Line.Row("Shooting accuracy", Percent(stats.ShootingAccuracy)),
            Line.Row("Melee damage", Times(stats.MeleeDamageMultiplier)),
            Line.Row("Shooting damage", Times(stats.ShootingDamageMultiplier))
        });
    }

    private void BuildCrewPage()
    {
        if (_game is null)
        {
            AddColumn(0, new List<Line>
            {
                Line.Head("Crew"),
                Line.Text("Crew members join once a world is running and there is housing for them."),
                Line.Text("Open this sheet during play to see each of them.")
            }, wide: true);
            return;
        }

        var columns = new[] { new List<Line>(), new List<Line>() };
        if (_game.Colonists.Count == 0)
            columns[0].AddRange([Line.Head("Crew"), Line.Text("No one has joined yet. Build houses and they will come.")]);
        var index = 0;
        foreach (var agent in _game.Colonists)
        {
            var column = columns[index++ % 2];
            if (column.Count > 0)
                column.Add(Line.Gap);
            column.Add(Line.Head($"{agent.Profile.Name}  ·  {agent.Profile.BackstoryName}"));
            column.Add(Line.Row("Life", $"{agent.HitPoints:0} / {agent.MaximumHitPoints:0}"));
            column.Add(Line.Row("Mood", $"{Percent(agent.Mood)}  {agent.MoodLabel}"));
            column.Add(Line.Row("Food / rest", $"{Percent(agent.Needs.FoodReserve)} / {Percent(agent.Needs.RestReserve)}"));
            column.Add(Line.Row("Traits", agent.Profile.TraitSummary));
            column.Add(Line.Row("Best at", string.Join(", ", TopSkills(agent.ColonistStats, 2).Select(line => line.Label))));
            column.Add(Line.Row("Doing", agent.IsAlive ? $"{agent.Activity}" : "Down"));
        }
        AddColumn(0, columns[0]);
        AddColumn(1, columns[1]);
    }

    /// <summary>Colony member the Health, Mood and Social pages describe (player first).</summary>
    private IColonyPawn? Subject()
    {
        if (_game is null)
            return null;
        var pawns = _game.ColonyPawns;
        return pawns.Count == 0 ? null : pawns[Math.Clamp(_subject, 0, pawns.Count - 1)];
    }

    private void AddSubjectButton(float left)
    {
        var name = Subject()?.Name ?? "—";
        _panel.Add(new UiButtonElement($"{name}  ›", () =>
        {
            var count = _game?.ColonyPawns.Count ?? 1;
            _subject = (_subject + 1) % Math.Max(1, count);
            Build();
        }, "colonist-sheet-subject", new UiStyle
        {
            Left = UiLength.Pct(left),
            Width = UiLength.Pct(0.12f),
            Height = UiLength.Px(30f),
            Top = UiLength.Mixed(-36f, 1f),
            HAlign = 0f,
            Padding = new UiThickness(6f, 3f, 6f, 3f)
        }) { Color = UiColor.ButtonDefault });
    }

    /// <summary>Every body part with its health and conditions, plus capacities and vital signs.</summary>
    private void BuildHealthPage()
    {
        if (Subject() is not { } pawn)
        {
            AddColumn(0, [Line.Head("Health"), Line.Text("Body parts, wounds and capacities appear once a world is running.")], wide: true);
            return;
        }
        var health = pawn.Health;
        var parts = new List<Line> { Line.Head($"Body  ·  {pawn.Name}") };
        foreach (var part in health.GetPartStatuses())
        {
            var indent = part.ParentName is null ? string.Empty : "    ";
            parts.Add(Line.Row($"{indent}{DisplayName(part.Name)}", part.Missing ? "Missing" : $"{part.Health:0} / {part.MaxHealth:0}"));
            foreach (var condition in part.Conditions)
            {
                var notes = new List<string> { $"{condition.Severity:0}" };
                if (condition.BleedRate > 0.001f) notes.Add("bleeding");
                if (condition.Tended) notes.Add($"tended {Percent(condition.TendQuality)}");
                if (condition.Permanent) notes.Add("permanent");
                parts.Add(Line.Text($"{indent}      {condition.Label} ({string.Join(", ", notes)})"));
            }
        }
        AddColumn(0, parts.Take(28).ToList());

        var vitals = new List<Line> { Line.Head("Capacities") };
        foreach (var capacity in Enum.GetValues<BodyCapacity>())
            vitals.Add(Line.Row(SplitWords(capacity.ToString()), Percent(health.Capacities.TryGetValue(capacity, out var level) ? level : 1f)));
        vitals.Add(Line.Gap);
        vitals.Add(Line.Head("Vitals"));
        vitals.Add(Line.Row("Pain", Percent(health.PainLevel) + (health.InPainShock ? "  (shock)" : string.Empty)));
        vitals.Add(Line.Row("Bleeding", $"{health.BleedRate:0.00} / h"));
        vitals.Add(Line.Row("Blood loss", Percent(health.BloodLoss)));
        vitals.Add(Line.Row("State", pawn.IsDowned ? $"Downed: {health.DownedReason}" : pawn.IsAlive ? "Conscious and mobile" : "Dead"));
        vitals.Add(Line.Gap);
        vitals.Add(Line.Text("Crew with Medical skill tend nearby wounds."));
        vitals.Add(Line.Text("Tended wounds stop bleeding and heal faster."));
        AddColumn(1, vitals);
    }

    /// <summary>Mood, break risk, needs, and every thought with its mood effect.</summary>
    private void BuildMoodPage()
    {
        if (Subject() is not { } pawn)
        {
            AddColumn(0, [Line.Head("Mood"), Line.Text("Thoughts and mental state appear once a world is running.")], wide: true);
            return;
        }
        var mood = pawn.Mind.Mood;
        AddColumn(0, new List<Line>
        {
            Line.Head($"Mood  ·  {pawn.Name}"),
            Line.Row("Mood", $"{Percent(mood.Current)}  {ColonistMoodCalculator.Label(mood.Current)}"),
            Line.Row("Heading to", Percent(mood.Target)),
            Line.Row("From needs", Percent(mood.Baseline)),
            Line.Row("From thoughts", $"{mood.ThoughtOffset * 100f:+0;-0;0}%"),
            Line.Row("State", pawn.Mind.Breaker.StateLabel),
            Line.Gap,
            Line.Head("Needs"),
            Line.Row("Food", Percent(pawn.Needs.FoodReserve)),
            Line.Row("Rest", Percent(pawn.Needs.RestReserve)),
            Line.Row("Joy", Percent(pawn.Needs.JoyReserve)),
            Line.Row("Comfort", Percent(pawn.Needs.ComfortReserve)),
            Line.Gap,
            Line.Text("A break comes when mood stays low for a while."),
            Line.Text("Iron-willed colonists hold out longer, nervous ones less.")
        });

        var thoughts = new List<Line> { Line.Head("Thoughts") };
        var current = pawn.Mind.CurrentThoughts;
        if (current.Count == 0)
            thoughts.Add(Line.Text("Nothing on their mind."));
        foreach (var thought in current.Take(24))
        {
            var count = thought.Count > 1 ? $" ×{thought.Count}" : string.Empty;
            var about = thought.OtherName is null ? string.Empty : $" ({thought.OtherName})";
            var time = thought.RemainingHours is { } hours ? $"   {hours:0} h" : thought.Situational ? "   now" : string.Empty;
            thoughts.Add(Line.Row($"{thought.Label}{about}{count}", $"{thought.MoodOffset * 100f:+0;-0;0}%{time}"));
        }
        AddColumn(1, thoughts);
    }

    /// <summary>Opinion of every other colony member, with relation and compatibility.</summary>
    private void BuildSocialPage()
    {
        if (_game is null || Subject() is not { } pawn)
        {
            AddColumn(0, [Line.Head("Social"), Line.Text("Relationships form once the crew has landed.")], wide: true);
            return;
        }
        var lines = new List<Line> { Line.Head($"Opinions  ·  {pawn.Name}") };
        var opinions = _game.GetOpinions(pawn);
        if (opinions.Count == 0)
            lines.Add(Line.Text("No one else in the colony yet."));
        foreach (var opinion in opinions.OrderByDescending(view => view.Opinion))
        {
            var name = opinion.OtherAlive ? opinion.OtherName : $"{opinion.OtherName} †";
            lines.Add(Line.Row(name, $"{opinion.Opinion:+0;-0;0}   {opinion.RelationLabel}   compatibility {opinion.Compatibility:+0.00;-0.00;0.00}"));
        }
        lines.Add(Line.Gap);
        lines.Add(Line.Text("Opinion is compatibility plus fading memories of chats, kind words, slights and insults."));
        lines.Add(Line.Text("High opinion makes friends and lovers. Low opinion makes rivals and can end in a fight."));
        AddColumn(0, lines, wide: true);
    }

    /// <summary>
    /// Lays out one column (left or right half, or the full width): captions in amber, a name on the left with its
    /// value on the right, notes in quiet text.
    /// </summary>
    private void AddColumn(int column, IReadOnlyList<Line> lines, bool wide = false)
    {
        var left = wide ? 0f : column == 0 ? 0f : 0.53f;
        var width = wide ? 1f : 0.47f;
        var top = 46f;
        var index = 0;
        foreach (var line in lines)
        {
            var id = $"colonist-{_page}-{column}-{index++}";
            var height = line.Header ? 22f : 18f;
            if (line.Header)
                top += 2f;
            var box = new UiStyle { Left = UiLength.Pct(left), Top = UiLength.Px(top), Width = UiLength.Pct(width), Height = UiLength.Px(height) };
            if (line.Header)
                _panel.Add(new UiTextElement(line.Label.ToUpperInvariant(), id, box) { Color = new UiColor(242, 176, 64) });
            else if (line.Note)
                _panel.Add(new UiTextElement(line.Label, id, box) { Color = new UiColor(150, 160, 172) });
            else if (line.Label.Length > 0)
            {
                _panel.Add(new UiTextElement(line.Label, id, box) { Color = new UiColor(226, 230, 238) });
                if (line.Value is { } value)
                    _panel.Add(new UiTextElement(value, id + "-value", box) { Color = new UiColor(242, 196, 110), Align = UiTextAlign.Right });
            }
            top += height;
        }
    }

    private static string SkillValue(PassionLevel passion, string level) =>
        passion == PassionLevel.None ? level : $"{ColonistCatalog.PassionMarker(passion)}  {level}";

    private static string GenderName(string gender) => gender == "NonBinary" ? "Non-binary" : gender;

    private static string SplitWords(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    private static SurvivalNeeds PreviewNeeds() => new(0.82f, 0.90f, 0.72f, 0.70f);

    private static float Capacity(HealthComponent? health, BodyPartFunction function) =>
        health?.GetTaggedCapacity(function) ?? 1f;

    private static string DisplayName(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static IEnumerable<Line> TopSkills(ColonistStats stats, int count) =>
        stats.SkillLevels
            .Select((level, index) => (Level: level, Skill: (ColonistSkill)index))
            .OrderByDescending(item => item.Level)
            .ThenBy(item => item.Skill)
            .Take(count)
            .Select(item => Line.Row(ColonistCatalog.SkillName(item.Skill), SkillValue(stats.Passion(item.Skill), $"{item.Level}")));
}
