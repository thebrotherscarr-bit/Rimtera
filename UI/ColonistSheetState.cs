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
            $"COLONIST  •  {_profile.Name}",
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
            Color = !close && _page == page ? UiColor.HeaderBlue : UiColor.ButtonDefault
        };
        _panel.Add(button);
    }

    private void BuildOverview()
    {
        var stats = _game?.Player.ColonistStats ?? _profile.Stats;
        var needs = _game?.Player.Needs ?? PreviewNeeds();
        var health = _game?.Player.Health;
        var hitPoints = _game?.Player.HitPoints ?? _profile.HitPoints;
        var maximumHitPoints = _game?.Player.MaximumHitPoints ?? _profile.EffectiveMaximumHealth;
        var mood = _game?.Player.Mood ?? ColonistMoodCalculator.Calculate(needs, stats, health);

        var left = new List<string>
        {
            $"{_profile.Name}  •  {_profile.Gender}  •  age {_profile.Age}",
            $"Backstory: {_profile.BackstoryName}",
            $"Traits: {_profile.TraitSummary}",
            $"Mood: {mood:P0}  {ColonistMoodCalculator.Label(mood)}",
            $"Food {needs.FoodReserve:P0}   Rest {needs.RestReserve:P0}",
            $"Joy {needs.JoyReserve:P0}   Comfort {needs.ComfortReserve:P0}",
            $"Health {hitPoints:0}/{maximumHitPoints:0}   Pain {(health?.PainLevel ?? 0f):P0}",
            $"Consciousness {(health?.Consciousness ?? 1f):P0}   Moving {Capacity(health, BodyPartFunction.MovingLimbCore):P0}",
            $"Manipulation {Capacity(health, BodyPartFunction.ManipulationLimbCore):P0}   Sight {Capacity(health, BodyPartFunction.SightSource):P0}",
            $"Move {stats.MoveSpeedMultiplier:0.00}×   Work {stats.GlobalWorkSpeed:0.00}×",
            $"Melee {stats.MeleeHitChance:P0} hit / {stats.MeleeDodgeChance:P0} dodge   Shoot {stats.ShootingAccuracy:P0}",
            $"Melee dmg {stats.MeleeDamageMultiplier:0.00}×   Shoot dmg {stats.ShootingDamageMultiplier:0.00}×",
            $"Medical {stats.MedicalTendQuality:P0}   Research {stats.ResearchSpeed:0.00}×   Social {stats.SocialImpact:P0}",
            string.Empty,
            "WORK SPEEDS",
            $"Mining {stats.MiningSpeed:0.00}×   Construction {stats.ConstructionSpeed:0.00}×   Plants {stats.PlantWorkSpeed:0.00}×",
            $"Cooking {stats.CookingSpeed:0.00}×   Crafting {stats.CraftingSpeed:0.00}×   Research {stats.ResearchSpeed:0.00}×"
        };
        AddLines(left, 0f, 0.55f);

        var right = new List<string>
        {
            "TOP SKILLS"
        };
        right.AddRange(TopSkillLines(stats, 7));
        right.Add(string.Empty);
        right.Add("CURRENT WORLD");
        if (_game is null)
        {
            right.Add("Preview only — create a world to meet the crew.");
        }
        else
        {
            var selected = _game.Player.Inventory.SelectedItem?.Name ?? "empty hands";
            right.Add($"Carrying: {selected}");
            right.Add($"Colonists: {_game.Colonists.Count(agent => agent.IsAlive)} alive");
            right.Add("Damage scales with the matching combat skill.");
        }
        right.Add(string.Empty);
        right.Add("BODY");
        if (health is null)
            right.Add("Healthy. Open the Health page in a world for every part.");
        else
        {
            var damaged = health.GetPartStatuses().Where(part => part.Missing || part.Health < part.MaxHealth - 0.5f).ToArray();
            right.Add(damaged.Length == 0 ? "No injuries." : $"{damaged.Length} injured part(s) — see Health.");
            right.Add($"Bleeding {health.BleedRate:0.00}/h   Blood loss {health.BloodLoss:P0}");
        }
        AddLines(right, 0.56f, 0.43f);
    }

    private void BuildSkillsPage()
    {
        var stats = _game?.Player.ColonistStats ?? _profile.Stats;
        var lines = new List<string>();
        foreach (var skill in Enum.GetValues<ColonistSkill>())
        {
            var level = stats.Level(skill);
            lines.Add($"{ColonistCatalog.PassionMarker(stats.Passion(skill))} " +
                      $"{ColonistCatalog.SkillName(skill),-14} {level,2} / 20   {PassionName(stats.Passion(skill))}");
        }
        AddLines(lines, 0f, 0.56f);

        AddLines(new List<string>
        {
            "DERIVED WORK",
            $"Mining          {stats.MiningSpeed:0.00}×",
            $"Construction    {stats.ConstructionSpeed:0.00}×",
            $"Plants          {stats.PlantWorkSpeed:0.00}×",
            $"Cooking         {stats.CookingSpeed:0.00}×",
            $"Crafting        {stats.CraftingSpeed:0.00}×",
            $"Research        {stats.ResearchSpeed:0.00}×",
            $"Medical tend    {stats.MedicalTendQuality:P0}",
            $"Social impact   {stats.SocialImpact:P0}",
            string.Empty,
            "COMBAT",
            $"Melee hit       {stats.MeleeHitChance:P0}   dodge {stats.MeleeDodgeChance:P0}",
            $"Shooting        {stats.ShootingAccuracy:P0}",
            $"Melee damage    {stats.MeleeDamageMultiplier:0.00}×",
            $"Shooting damage {stats.ShootingDamageMultiplier:0.00}×"
        }, 0.56f, 0.42f);
    }

    private void BuildCrewPage()
    {
        if (_game is null)
        {
            AddLines(new List<string>
            {
                "CREW",
                string.Empty,
                "Crew profiles are generated when a world starts.",
                "Each colonist gets a deterministic name, backstory,",
                "traits, skills, appearance, needs, and mood.",
                string.Empty,
                "Open this sheet during play to inspect them."
            }, 0f, 0.9f);
            return;
        }

        var lines = new List<string> { "CREW" };
        if (_game.Colonists.Count == 0)
        {
            lines.Add("No colonist NPCs in this world.");
        }
        foreach (var agent in _game.Colonists)
        {
            lines.Add(string.Empty);
            lines.Add($"{agent.Profile.Name}  •  {agent.Profile.BackstoryName}  •  {agent.Profile.Age}");
            lines.Add($"HP {agent.HitPoints:0}/{agent.MaximumHitPoints:0}   Mood {agent.Mood:P0} {agent.MoodLabel}");
            lines.Add($"Food {agent.Needs.FoodReserve:P0}   Rest {agent.Needs.RestReserve:P0}   " +
                      $"Joy {agent.Needs.JoyReserve:P0}   Comfort {agent.Needs.ComfortReserve:P0}");
            lines.Add($"Traits: {agent.Profile.TraitSummary}");
            lines.Add($"Top: {string.Join(" · ", TopSkillLines(agent.ColonistStats, 4).Select(line => line.TrimStart('▲', '·', ' ')))}");
            lines.Add(agent.IsAlive ? $"Activity: {agent.Activity}" : "Activity: down");
        }
        AddLines(lines, 0f, 0.92f);
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
        _panel.Add(new UiButtonElement($"▸ {name}", () =>
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
            AddLines(["HEALTH", string.Empty, "Body parts, wounds and capacities appear once a world is running."], 0f, 0.9f);
            return;
        }
        var health = pawn.Health;
        var parts = new List<string> { $"HEALTH  •  {pawn.Name}" };
        foreach (var part in health.GetPartStatuses())
        {
            var indent = part.ParentName is null ? string.Empty : "  ";
            var state = part.Missing ? "missing" : $"{part.Health:0}/{part.MaxHealth:0}";
            var depth = part.Depth.ToString().ToLowerInvariant();
            parts.Add($"{indent}{DisplayName(part.Name),-16} {state,-8} {depth}");
            foreach (var condition in part.Conditions)
            {
                var notes = new List<string> { $"{condition.Severity:0}" };
                if (condition.BleedRate > 0.001f) notes.Add("bleeding");
                if (condition.Tended) notes.Add($"tended {condition.TendQuality:P0}");
                if (condition.Permanent) notes.Add("permanent");
                parts.Add($"{indent}    · {condition.Label} ({string.Join(", ", notes)})");
            }
        }
        AddLines(parts.Take(34), 0f, 0.55f);

        var vitals = new List<string> { "CAPACITIES" };
        foreach (var capacity in Enum.GetValues<BodyCapacity>())
            vitals.Add($"{capacity,-14} {(health.Capacities.TryGetValue(capacity, out var level) ? level : 1f):P0}");
        vitals.Add(string.Empty);
        vitals.Add("VITALS");
        vitals.Add($"Pain {health.PainLevel:P0}{(health.InPainShock ? "  (pain shock)" : string.Empty)}");
        vitals.Add($"Bleeding {health.BleedRate:0.00}/h   Blood loss {health.BloodLoss:P0}");
        vitals.Add(pawn.IsDowned ? $"DOWNED: {health.DownedReason}" : pawn.IsAlive ? "Conscious and mobile" : "Dead");
        vitals.Add(string.Empty);
        vitals.Add("Crew with Medical skill tend nearby wounds;");
        vitals.Add("tended wounds stop bleeding and heal faster.");
        AddLines(vitals, 0.58f, 0.40f);
    }

    /// <summary>Mood, break risk, needs, and every thought with its mood effect.</summary>
    private void BuildMoodPage()
    {
        if (Subject() is not { } pawn)
        {
            AddLines(["MOOD", string.Empty, "Thoughts and mental state appear once a world is running."], 0f, 0.9f);
            return;
        }
        var mood = pawn.Mind.Mood;
        var breaker = pawn.Mind.Breaker;
        var left = new List<string>
        {
            $"MOOD  •  {pawn.Name}",
            $"Mood {mood.Current:P0} (heading to {mood.Target:P0})   {ColonistMoodCalculator.Label(mood.Current)}",
            $"Needs baseline {mood.Baseline:P0}   Thoughts {mood.ThoughtOffset:+0%;-0%;0%}",
            $"State: {breaker.StateLabel}",
            string.Empty,
            "NEEDS",
            $"Food {pawn.Needs.FoodReserve:P0}   Rest {pawn.Needs.RestReserve:P0}",
            $"Joy {pawn.Needs.JoyReserve:P0}   Comfort {pawn.Needs.ComfortReserve:P0}",
            string.Empty,
            "Breaks happen when mood stays under a",
            "threshold for a while. Iron-willed colonists",
            "hold out longer; nervous ones break sooner."
        };
        AddLines(left, 0f, 0.42f);

        var thoughts = new List<string> { "THOUGHTS" };
        var current = pawn.Mind.CurrentThoughts;
        if (current.Count == 0)
            thoughts.Add("Nothing on their mind.");
        foreach (var thought in current.Take(30))
        {
            var count = thought.Count > 1 ? $" ×{thought.Count}" : string.Empty;
            var about = thought.OtherName is null ? string.Empty : $" ({thought.OtherName})";
            var time = thought.RemainingHours is { } hours ? $"  {hours:0}h" : thought.Situational ? "  now" : string.Empty;
            thoughts.Add($"{thought.MoodOffset,5:+0%;-0%;0%}  {thought.Label}{about}{count}{time}");
        }
        AddLines(thoughts, 0.44f, 0.55f);
    }

    /// <summary>Opinion of every other colony member, with relation and compatibility.</summary>
    private void BuildSocialPage()
    {
        if (_game is null || Subject() is not { } pawn)
        {
            AddLines(["SOCIAL", string.Empty, "Relationships form once the crew has landed."], 0f, 0.9f);
            return;
        }
        var lines = new List<string> { $"SOCIAL  •  {pawn.Name}", string.Empty, $"{"Colonist",-20} {"Opinion",8}  {"Relation",-14} Compat." };
        var opinions = _game.GetOpinions(pawn);
        if (opinions.Count == 0)
            lines.Add("No one else in the colony yet.");
        foreach (var opinion in opinions.OrderByDescending(view => view.Opinion))
        {
            var name = opinion.OtherAlive ? opinion.OtherName : $"{opinion.OtherName} †";
            lines.Add($"{name,-20} {opinion.Opinion,8:+0;-0;0}  {opinion.RelationLabel,-14} {opinion.Compatibility:+0.00;-0.00;0.00}");
        }
        lines.Add(string.Empty);
        lines.Add("Opinion = compatibility + fading memories of chats, deep talks,");
        lines.Add("kind words, slights and insults. High opinion makes friends and");
        lines.Add("lovers; low opinion makes rivals and can end in a social fight.");
        AddLines(lines, 0f, 0.92f);
    }

    private void AddLines(IEnumerable<string> lines, float left, float width)
    {
        var top = 38f;
        const float lineHeight = 15f;
        var index = 0;
        foreach (var text in lines)
        {
            _panel.Add(new UiTextElement(text, $"colonist-line-{_page}-{index++}", new UiStyle
            {
                Left = UiLength.Pct(left),
                Top = UiLength.Px(top),
                Width = UiLength.Pct(width),
                Height = UiLength.Px(lineHeight)
            }));
            top += lineHeight;
        }
    }

    private static SurvivalNeeds PreviewNeeds() => new(0.82f, 0.90f, 0.72f, 0.70f);

    private static float Capacity(HealthComponent? health, BodyPartFunction function) =>
        health?.GetTaggedCapacity(function) ?? 1f;

    private static string DisplayName(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static IEnumerable<string> TopSkillLines(ColonistStats stats, int count) =>
        stats.SkillLevels
            .Select((level, index) => (Level: level, Skill: (ColonistSkill)index))
            .OrderByDescending(item => item.Level)
            .ThenBy(item => item.Skill)
            .Take(count)
            .Select(item => $"{ColonistCatalog.PassionMarker(stats.Passion(item.Skill))} " +
                            $"{ColonistCatalog.SkillName(item.Skill),-14} {item.Level,2}");

    private static string PassionName(PassionLevel passion) => passion switch
    {
        PassionLevel.Burning => "burning",
        PassionLevel.Interested => "interested",
        _ => "none"
    };
}
