using System.Text;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// A text view of a running game for an AI player (docs/AGENT_PLAY.md). It only reads the simulation: no UI, no clock,
/// no randomness, so it is safe in the deterministic core. Columns and rows are tiles; the header says which tile is top-left.
/// </summary>
public static class AgentObserver
{
    // Glyphs the console renderer already used, then a pool for every other tile so each kind in view is told apart.
    private static readonly Dictionary<TileId, char> KnownGlyphs = new()
    {
        [TileId.Soil] = 's', [TileId.Rock] = '#', [TileId.CopperOre] = 'c', [TileId.Grass] = 'g', [TileId.Sand] = ':',
        [TileId.Wood] = 'w', [TileId.Bedrock] = 'X', [TileId.Workbench] = '+', [TileId.Furnace] = 'F', [TileId.Anvil] = 'A',
        [TileId.CobaltOre] = 'k', [TileId.ViridiumOre] = 'v', [TileId.EnergyCrystal] = '%', [TileId.MiniTurret] = 'T',
        [TileId.HydroponicsTray] = 'h', [TileId.SolarPanel] = '=', [TileId.OxygenUnit] = 'O'
    };

    private const string GlyphPool = "abdefijlmnpqruyzBCDEHIJKLMNPQRSUVWYZ0123456789";

    public static string Describe(GameSimulation game, int halfWidth = 32, int halfHeight = 14)
    {
        halfWidth = Math.Clamp(halfWidth, 8, 60);
        halfHeight = Math.Clamp(halfHeight, 5, 30);
        var player = game.Player;
        var world = game.World;
        var centerX = (int)MathF.Floor(player.Motion.Position.X + player.Motion.Size.X / 2f);
        var centerY = (int)MathF.Floor(player.Motion.Position.Y + player.Motion.Size.Y / 2f);
        var width = Math.Min(halfWidth * 2 + 1, world.Width);
        var height = Math.Min(halfHeight * 2 + 1, world.Height);
        var startX = Math.Clamp(centerX - width / 2, 0, Math.Max(0, world.Width - width));
        var startY = Math.Clamp(centerY - height / 2, 0, Math.Max(0, world.Height - height));

        var rows = new char[height][];
        var legend = new SortedDictionary<char, string>();
        var assigned = new Dictionary<TileId, char>(KnownGlyphs);
        var poolIndex = 0;
        for (var row = 0; row < height; row++)
        {
            rows[row] = new char[width];
            for (var column = 0; column < width; column++)
            {
                var cell = world.GetCell(new GridPoint(startX + column, startY + row));
                char glyph;
                if (cell.LiquidDepth > 0.1f)
                    glyph = '~';
                else if (cell.TileId == TileId.Air)
                    glyph = cell.WallId == 0 ? ' ' : '.';
                else
                {
                    if (!assigned.TryGetValue(cell.TileId, out glyph))
                    {
                        glyph = poolIndex < GlyphPool.Length ? GlyphPool[poolIndex++] : '?';
                        assigned[cell.TileId] = glyph;
                    }
                    legend[glyph] = cell.TileId.ToString();
                }
                rows[row][column] = glyph;
            }
        }
        legend['~'] = "liquid";
        legend['.'] = "background wall (open air)";

        void Put(float worldX, float worldY, char glyph)
        {
            var column = (int)MathF.Floor(worldX) - startX;
            var row = (int)MathF.Floor(worldY) - startY;
            if ((uint)column < (uint)width && (uint)row < (uint)height)
                rows[row][column] = glyph;
        }

        var nearby = new List<string>();
        foreach (var enemy in game.Enemies)
        {
            if (!enemy.IsAlive)
                continue;
            var ex = enemy.Motion.Position.X + enemy.Motion.Size.X / 2f;
            var ey = enemy.Motion.Position.Y + enemy.Motion.Size.Y / 2f;
            Put(ex, ey, enemy.IsPassive ? '&' : '!');
            var tx = (int)MathF.Floor(ex);
            var ty = (int)MathF.Floor(ey);
            if (Math.Abs(tx - centerX) <= halfWidth + 4 && Math.Abs(ty - centerY) <= halfHeight + 4 && nearby.Count < 12)
                nearby.Add($"{enemy.DisplayName} at ({tx},{ty}) hp {enemy.HitPoints:0}/{enemy.MaximumHitPoints:0} {(enemy.IsPassive ? "passive" : "hostile")} {enemy.State}{(enemy.IsBoss ? " BOSS" : "")}");
        }
        Put(game.GuidePosition.X, game.GuidePosition.Y, 'G');
        Put(player.Motion.Position.X + player.Motion.Size.X / 2f, player.Motion.Position.Y + player.Motion.Size.Y / 2f, '@');
        legend['@'] = "you";
        legend['!'] = "hostile creature";
        legend['&'] = "passive creature";
        legend['G'] = "station AI / guide";

        var hour = (int)MathF.Floor(game.ClockHour);
        var minute = (int)MathF.Floor((game.ClockHour - hour) * 60f);
        var text = new StringBuilder();
        // "act:" (not a bare "act") so a small model does not mistake the run's act for the JSON "action" field.
        text.AppendLine($"tick {game.TickNumber}  day {game.Day}  {hour:00}:{minute:00}  act: {game.Act}");
        text.AppendLine($"you: tile ({centerX},{centerY})  hp {player.HitPoints:0}/{player.MaximumHitPoints:0}");
        text.AppendLine($"message: {game.LastMessage}");
        var inventory = player.Inventory;
        var selected = inventory.SelectedStack;
        text.AppendLine($"selected slot {inventory.SelectedSlot}: {(selected.IsEmpty ? "(empty)" : $"{selected.ItemId} x{selected.Count}")}");
        var held = new List<string>();
        for (var slot = 0; slot < inventory.Slots.Count; slot++)
            if (!inventory.Slots[slot].IsEmpty)
                held.Add($"{slot}:{inventory.Slots[slot].ItemId} x{inventory.Slots[slot].Count}");
        text.AppendLine("inventory: " + (held.Count == 0 ? "(empty)" : string.Join(", ", held)));
        var craftable = game.GetCraftableRecipes();
        text.AppendLine("craftable here: " + (craftable.Count == 0 ? "(nothing)" : string.Join(", ", craftable.Take(15).Select(recipe => $"{recipe.Id}->{recipe.Output}x{recipe.OutputCount}"))));
        if (nearby.Count > 0)
            text.AppendLine("creatures: " + string.Join("; ", nearby));
        if (game.IsOver)
            text.AppendLine("*** you are dead (run over) ***");
        text.AppendLine($"map: column 0 = tile x {startX}, row 0 = tile y {startY}; x grows right, y grows DOWN; {width}x{height} tiles");
        text.AppendLine("legend: " + string.Join("  ", legend.Select(pair => $"{pair.Key}={pair.Value}")));
        foreach (var row in rows)
            text.AppendLine(new string(row));
        return text.ToString();
    }
}
