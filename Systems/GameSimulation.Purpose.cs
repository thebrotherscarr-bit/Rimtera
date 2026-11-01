using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Purposeful events (0.6.5, docs/VISION.md §4.3): each act has a key event that pushes the run toward its next step. Once an
/// act has gone on for <see cref="PurposeAfterHours"/> hours, the storyteller sends it, once: Landfall brings a survivor's
/// call and building supplies; the Descent a signal from the core, how far down it lies, and supplies for the dig; the Core
/// and the acts after it the summon for the boss that opens the way (and, in the Zealots' act, where the cult gathers).
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Game hours into an act before its key event can come.</summary>
    public const float PurposeAfterHours = 18f;

    private static string PurposeFlag(RunAct act) => $"purpose.{(int)act}";

    /// <summary>Whether the act's key event has been sent.</summary>
    public bool PurposeSent(RunAct act) => Flags.IsSet(PurposeFlag(act));

    private bool PurposeDue => Act < RunAct.Launch && !PurposeSent(Act) && HoursInAct >= PurposeAfterHours;

    string? IStoryHost.SendPurpose() => SendPurpose();

    /// <summary>Sends the current act's key event (once per act); returns what came, or null.</summary>
    public string? SendPurpose()
    {
        var act = Act;
        if (act >= RunAct.Launch || PurposeSent(act))
            return null;
        Flags.Set(PurposeFlag(act));
        var (title, body, gifts) = act switch
        {
            RunAct.Landfall => ("A survivor's call",
                "A survivor on the open band is looking for shelter. Build them a house: walls behind, a door, a light, a table and a chair, " +
                $"{HousingValidator.MinimumCells} cells or more, and they will come.",
                new (ItemId, int)[] { (ItemId.WoodBlock, 60), (ItemId.WallTorch, 3), (ItemId.WorkbenchBlock, 1) }),
            RunAct.Descent => ("A signal from the core",
                $"The station AI has traced a signal from the irradiated core, about {Math.Max(0, (int)(World.Height * 0.8f - PlayerCenter.Y))} tiles below you. " +
                "Dig down through the biomes to reach it.",
                new (ItemId, int)[] { (ItemId.WallTorch, 20), (ItemId.RopeCoil, 2), (ItemId.RestorativeTonic, 3) }),
            RunAct.Core => BossPurpose(CoreGateBoss, "The gate stirs", "Beating it fractures the crust and opens the hardmode ladder."),
            RunAct.Zealots when !_defeatedBosses.Contains(RitualBoss) => RitualPurpose(),
            _ => BossPurpose(NextBoss, "The way forward", "It stands between you and the launch.")
        };
        var given = new List<string>();
        foreach (var (item, count) in gifts)
        {
            var added = Player.Inventory.Add(item, count);
            if (added > 0)
                given.Add($"{added} {Items[item].Name}");
        }
        var text = given.Count == 0 ? body : $"{body} Dropped with it: {string.Join(", ", given)}.";
        SendLetter(LetterKind.Good, title, text, tag: "purpose");
        Record($"{title}: the storyteller pointed the way.");
        return $"{title}. {text}";
    }

    /// <summary>The summon for <paramref name="boss"/>, if you carry none.</summary>
    private (string Title, string Body, (ItemId, int)[] Gifts) BossPurpose(EnemyKind boss, string title, string why)
    {
        var name = EnemyArchetypes.Get(boss).DisplayName;
        var summon = Items.All.Where(item => item.SummonsBoss == boss).Select(item => item.Id).Cast<ItemId?>().FirstOrDefault();
        if (_defeatedBosses.Contains(boss) && boss == CoreGateBoss)
            return BossPurpose(NextBoss, "The way forward", "It stands between you and the launch.");
        if (summon is not { } item)
            return (title, $"The {name} waits. {why}", []);
        var carried = Player.Inventory.Count(item) > 0;
        return (title, $"Scavengers found a relic that calls the {name}. {why}" + (carried ? $" (You already carry {Items[item].Name}.)" : ""),
            carried ? [] : [(item, 1)]);
    }

    /// <summary>The Zealots' act: where the cult's ritual waits.</summary>
    private (string Title, string Body, (ItemId, int)[] Gifts) RitualPurpose()
    {
        var site = RitualSite;
        var dx = site.X - (int)PlayerCenter.X;
        var where = Math.Abs(dx) < 8 ? "right here" : $"{Math.Abs(dx)} tiles {(dx < 0 ? "west" : "east")} of you";
        return ("A defector's word",
            $"A cult defector says the Astro-God's zealots chant at the dungeon gate, {where}. Break their ritual: the Void Cantor guards it.",
            [(ItemId.RestorativeTonic, 3)]);
    }
}
