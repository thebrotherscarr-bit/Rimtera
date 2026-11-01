using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// Crafting guidance on the canvas, Terraria's crafting menu and Guide in Rimtera's terms: the crafting list (with
    /// recipes you're close to, have/needed counts and the station each needs), the station AI's recipe guide (every
    /// recipe that uses or makes an item, browsable ingredient by ingredient), the next-step hint line and the
    /// recipe lines on item tooltips.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private readonly List<(RectangleF Rect, ItemId Item)> _guideHits = new();
        private readonly List<(RectangleF Rect, ItemId Item)> _materialHits = new();
        private readonly HashSet<string> _almostIds = new(StringComparer.OrdinalIgnoreCase);
        private RectangleF _guideArea;
        private int _guideVisibleRows = 1;
        private readonly SolidBrush _guideTipBrush = new(Color.FromArgb(150, 205, 235));
        private readonly SolidBrush _haveBrush = new(Color.FromArgb(120, 222, 120));
        private readonly SolidBrush _missingBrush = new(Color.FromArgb(242, 100, 90));
        private readonly SolidBrush _almostVeil = new(Color.FromArgb(150, 12, 14, 20));
        private readonly SolidBrush _guideRowBrush = new(Color.FromArgb(200, 26, 30, 40));
        private readonly SolidBrush _textPanel = new(Color.FromArgb(185, 14, 17, 24));
        private readonly SolidBrush _hintBack = new(Color.FromArgb(170, 16, 20, 28));
        private readonly Pen _missingPen = new(Color.FromArgb(230, 222, 84, 72), 2f);

        private GameSimulation? _almostGame;
        private double _almostAt = double.NegativeInfinity;
        private IReadOnlyList<RecipeStatus> _almost = Array.Empty<RecipeStatus>();
        private GameSimulation? _tipsGame;
        private double _tipsAt = double.NegativeInfinity;
        private IReadOnlyList<GuideTip> _tips = Array.Empty<GuideTip>();
        private string? _dismissedTip;
        private GuideTip? _hintShown;
        private RectangleF _hintRect;
        private RectangleF _hintClose;

        /// <summary>The station AI's next steps, refreshed once a second (the base survey isn't free).</summary>
        public IReadOnlyList<GuideTip> GuideTips(GameSimulation game, bool fresh = false)
        {
            var now = FrameClock.Seconds;
            if (fresh || !ReferenceEquals(_tipsGame, game) || now - _tipsAt > 1.0)
            {
                _tips = game.GuideTips();
                _tipsGame = game;
                _tipsAt = now;
            }
            return _tips;
        }

        private IReadOnlyList<RecipeStatus> AlmostCraftable(GameSimulation game)
        {
            var now = FrameClock.Seconds;
            if (!ReferenceEquals(_almostGame, game) || now - _almostAt > 0.25)
            {
                _almost = game.GetAlmostCraftableRecipes();
                _almostGame = game;
                _almostAt = now;
            }
            return _almost;
        }

        /// <summary>A click on an item in the recipe guide or on a material of the selected recipe: the item to look up.</summary>
        public bool TryGuideClick(Point point, out ItemId item)
        {
            foreach (var (rect, hit) in _guideHits.Concat(_materialHits))
            {
                if (!rect.Contains(point))
                    continue;
                item = hit;
                Invalidate();
                return true;
            }
            item = default;
            return false;
        }

        /// <summary>The recipe whose result slot is under the point (hold-to-craft keeps crafting while it stays there).</summary>
        public string? RecipeAt(Point point) => _craftHits.FirstOrDefault(hit => hit.Rect.Contains(point)).RecipeId;

        /// <summary>The wheel over the crafting grid or the guide's list scrolls it a row at a time.</summary>
        public bool TryScrollCrafting(Point point, int delta, InventoryUiState state)
        {
            if (state.Guide is { } guide)
            {
                if (!_guideArea.Contains(point))
                    return false;
                guide.Scroll = Math.Max(0, guide.Scroll + (delta < 0 ? 1 : -1));
                Invalidate();
                return true;
            }
            if (!_craftArea.Contains(point))
                return false;
            _craftScrollRows = Math.Max(0, _craftScrollRows + (delta < 0 ? 1 : -1));
            Invalidate();
            return true;
        }

        /// <summary>
        /// Crafting under the inventory (and any open chest), Terraria 1.4's grid: everything craftable here as result
        /// slots, then (with "Close to" shown) greyed recipes you're close to. The selected one is outlined with its
        /// materials as have/needed and the station it needs on the rows below. Click to select, click again to craft,
        /// hold to keep crafting; click a material to look it up in the recipe guide; the wheel scrolls.
        /// </summary>
        private void DrawCraftingPanel(Graphics g, GameSimulation game, InventoryUiState inventory)
        {
            _craftHits.Clear();
            _materialHits.Clear();
            _guideHits.Clear();
            _guideArea = RectangleF.Empty;
            if (inventory.Guide is { } guide)
            {
                _craftArea = RectangleF.Empty;
                DrawGuidePanel(g, game, inventory, guide);
                return;
            }
            var craftable = game.GetCraftableRecipes();
            var almost = inventory.ShowAlmostCraftable ? AlmostCraftable(game) : Array.Empty<RecipeStatus>();
            var recipes = craftable.Concat(almost.Select(status => status.Recipe)).ToList();
            _almostIds.Clear();
            foreach (var status in almost)
                _almostIds.Add(status.Recipe.Id);
            var top = inventory.CraftingTop;
            var stride = InventoryLayout.Stride;
            var size = InventoryLayout.Slot;
            var left = InventoryLayout.Left;
            var width = InventoryLayout.Columns * stride;
            if (recipes.Count == 0)
            {
                _craftArea = RectangleF.Empty;
                DrawNothingToCraft(g, game, inventory, left, top, width);
                return;
            }
            var selectedIndex = recipes.FindIndex(recipe => recipe.Id == _craftSelected);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
                _craftSelected = recipes[0].Id;
            }
            // Rows that fit above the materials block and the bottom edge.
            var visibleRows = Math.Max(1, (int)((Height - top - 92) / stride));
            var totalRows = (recipes.Count + InventoryLayout.Columns - 1) / InventoryLayout.Columns;
            _craftScrollRows = Math.Clamp(_craftScrollRows, 0, Math.Max(0, totalRows - visibleRows));
            // The materials block sits right under the rows in use, as in Terraria, and counts as part of the panel.
            var shownRows = Math.Min(totalRows, visibleRows);
            _craftArea = new RectangleF(left, top, width, shownRows * stride + 80);
            for (var i = _craftScrollRows * InventoryLayout.Columns; i < recipes.Count && i < (_craftScrollRows + visibleRows) * InventoryLayout.Columns; i++)
            {
                var recipe = recipes[i];
                var row = i / InventoryLayout.Columns - _craftScrollRows;
                var rect = new RectangleF(left + i % InventoryLayout.Columns * stride, top + row * stride, size, size);
                var selected = i == selectedIndex;
                DrawSlot(g, rect, new ItemStack(recipe.Output, recipe.OutputCount), selected, null);
                if (_almostIds.Contains(recipe.Id))
                {
                    // Close, not ready: greyed, with a red corner mark.
                    g.FillRectangle(_almostVeil, rect);
                    g.FillRectangle(_missingBrush, rect.Right - 9, rect.Y + 3, 6, 6);
                }
                if (selected)
                    g.DrawRectangle(_menuFocusPen, rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                if (MousePosition is { } mouse && rect.Contains(mouse))
                    _hoveredSlotStack = new ItemStack(recipe.Output, recipe.OutputCount);
                _craftHits.Add((rect, recipe.Id));
            }
            var counts = almost.Count > 0 ? $"{craftable.Count} ready · {almost.Count} close" : $"{craftable.Count} ready";
            if (totalRows > visibleRows)
                counts += $" · rows {_craftScrollRows + 1}–{Math.Min(totalRows, _craftScrollRows + visibleRows)} of {totalRows}, wheel scrolls";
            DrawSelectedRecipe(g, game, game.RecipeStatusOf(recipes[selectedIndex]), left, top + shownRows * stride + 6, counts);
        }

        /// <summary>The selected recipe's materials (have/needed, short ones in red) and where it is crafted.</summary>
        private void DrawSelectedRecipe(Graphics g, GameSimulation game, RecipeStatus status, float left, float y, string counts)
        {
            var recipe = status.Recipe;
            var output = game.Items[recipe.Output];
            g.FillRectangle(_textPanel, left - 6, y - 3, InventoryLayout.Columns * InventoryLayout.Stride + 130f, 74f);
            var title = $"{output.Name}{(recipe.OutputCount > 1 ? $" ×{recipe.OutputCount}" : "")}";
            g.DrawString(title, _small, _amberBrush, left, y);
            var x = left + g.MeasureString(title, _small).Width + 6;
            DrawStationLine(g, status, x, y);
            var countsWidth = g.MeasureString(counts, _small).Width;
            g.DrawString(counts, _small, _dimTextBrush, left + InventoryLayout.Columns * InventoryLayout.Stride + 120f - countsWidth, y + 54f);
            x = left;
            foreach (var ingredient in status.Ingredients)
            {
                var rect = new RectangleF(x, y + 18, 34, 34);
                DrawSlot(g, rect, new ItemStack(ingredient.Item, 1), false, null);
                if (!ingredient.Met)
                    g.DrawRectangle(_missingPen, rect.X, rect.Y, rect.Width, rect.Height);
                var count = $"{ingredient.Have}/{ingredient.Need}";
                DrawCentered(g, count, new RectangleF(rect.X - 8, rect.Bottom, rect.Width + 16, 14), _small, ingredient.Met ? _haveBrush : _missingBrush);
                if (MousePosition is { } mouse && rect.Contains(mouse))
                    _hoveredSlotStack = new ItemStack(ingredient.Item, ingredient.Need);
                _materialHits.Add((rect, ingredient.Item));
                x += 46;
            }
            var help = status.Craftable
                ? "Click the outlined result again to craft · hold to craft more · click a material to look it up"
                : "Click a material to see where it comes from";
            g.DrawString(help, _small, _dimTextBrush, x + 6, y + 28);
        }

        /// <summary>"at a Power Forge" in green when it's in reach, the missing station (or lesson) in red when not.</summary>
        private void DrawStationLine(Graphics g, RecipeStatus status, float x, float y)
        {
            string text;
            Brush brush;
            if (status.Locked)
            {
                var material = status.Recipe.GuideUnlockMaterials?.FirstOrDefault();
                text = material is { } reveal && Game is { } game
                    ? $"· the station AI teaches this when you carry {game.Items[reveal].Name}"
                    : "· the station AI teaches this";
                brush = _missingBrush;
            }
            else if (status.MissingStations.Count > 0)
            {
                text = $"· needs {string.Join(" and ", status.MissingStations.Select(station => station.DisplayName()))} nearby";
                brush = _missingBrush;
            }
            else if (status.MissingConditions.Count > 0)
            {
                text = $"· must be crafted {string.Join(", ", status.MissingConditions)}";
                brush = _missingBrush;
            }
            else
            {
                text = status.Stations.Count == 0 ? "· by hand" : $"· {CraftingGuide.WhereCrafted(status.Recipe)}";
                brush = _haveBrush;
            }
            g.DrawString(text, _small, brush, x, y);
        }

        /// <summary>An empty crafting list still says what to do: the station AI's next step and how to see more.</summary>
        private void DrawNothingToCraft(Graphics g, GameSimulation game, InventoryUiState inventory, float left, float top, float width)
        {
            var tip = GuideTips(game).FirstOrDefault();
            var tipText = tip is null ? null : $"Station AI: {tip.Text}";
            var tipHeight = tipText is null ? 0f : Math.Min(64f, g.MeasureString(tipText, _small, (int)width).Height) + 8f;
            var more = inventory.ShowAlmostCraftable
                ? "Nothing close either. Gather materials, or open the Recipe guide to see what any item makes."
                : "\"Close to\" shows recipes you're nearly able to make. The Recipe guide shows what any item makes.";
            var moreHeight = g.MeasureString(more, _small, (int)width).Height;
            g.FillRectangle(_textPanel, left - 6, top, width + 12, 24f + tipHeight + moreHeight + 8f);
            g.DrawString("Nothing craftable here.", _small, _hudBrush, left, top + 4);
            var y = top + 24f;
            if (tipText is not null)
            {
                g.DrawString(tipText, _small, _guideTipBrush, new RectangleF(left, y, width, 64));
                y += tipHeight;
            }
            g.DrawString(more, _small, _dimTextBrush, new RectangleF(left, y, width, moreHeight + 2));
        }

        /// <summary>
        /// The station AI's recipe guide (Terraria's Guide): one row per recipe that uses (or makes) the looked-up item,
        /// with its result, every material as have/needed and the station it needs. Clicking any item looks it up.
        /// </summary>
        private void DrawGuidePanel(Graphics g, GameSimulation game, InventoryUiState inventory, GuideLookupState guide)
        {
            var top = inventory.CraftingTop;
            var left = InventoryLayout.Left;
            var width = InventoryLayout.Columns * InventoryLayout.Stride + 120f;
            if (guide.Item is not { } item)
            {
                g.FillRectangle(_textPanel, left - 6, top, width - 108f, 52f);
                g.DrawString("Nothing looked up yet. Click the slot above while holding an item, Shift-click an inventory item, " +
                             "or click a material in the crafting list.", _small, _dimTextBrush, new RectangleF(left, top + 4, width - 120f, 48));
                return;
            }
            var name = game.Items[item].Name;
            var recipes = guide.View == GuideView.UsedIn
                ? CraftingGuide.UsesOf(game.Crafting, game.Items, item)
                : CraftingGuide.MadeFrom(game.Crafting, game.Items, item);
            var nearby = game.NearbyCraftingStations();
            var environment = game.CurrentCraftingEnvironment;
            string header;
            if (guide.View == GuideView.UsedIn)
                header = recipes.Count == 0 ? $"Nothing is crafted from {name}." : $"{recipes.Count} recipe{(recipes.Count == 1 ? " uses" : "s use")} {name}:";
            else if (recipes.Count == 0)
            {
                var sources = CraftingGuide.SourcesOf(game.Items, game.Tiles, item);
                header = sources.Count == 0
                    ? $"{name} isn't crafted. Look for it out in the world: crates, caches and traders."
                    : $"{name} isn't crafted. Where to get it: {string.Join("; ", sources)}.";
            }
            else
                header = $"{recipes.Count} recipe{(recipes.Count == 1 ? " makes" : "s make")} {name}:";
            var headerHeight = Math.Min(32f, g.MeasureString(header, _small, (int)width).Height);
            g.FillRectangle(_textPanel, left - 6, top - 2, width + 8, headerHeight + 4);
            g.DrawString(header, _small, _amberBrush, new RectangleF(left, top, width, 32));

            const float rowHeight = 54f;
            var listTop = top + headerHeight + 8f;
            _guideVisibleRows = Math.Max(1, (int)((Height - listTop - 12) / rowHeight));
            guide.Scroll = Math.Clamp(guide.Scroll, 0, Math.Max(0, recipes.Count - _guideVisibleRows));
            _guideArea = new RectangleF(left, listTop, width, _guideVisibleRows * rowHeight);
            for (var i = guide.Scroll; i < recipes.Count && i < guide.Scroll + _guideVisibleRows; i++)
            {
                var status = CraftingGuide.Describe(game.Crafting, recipes[i], game.Player.Inventory, nearby, environment);
                var y = listTop + (i - guide.Scroll) * rowHeight;
                g.FillRectangle(_guideRowBrush, left - 4, y, width, rowHeight - 4);
                DrawGuideRow(g, game, status, item, left, y, width);
            }
            if (recipes.Count > _guideVisibleRows)
                g.DrawString($"{guide.Scroll + 1}–{Math.Min(recipes.Count, guide.Scroll + _guideVisibleRows)} of {recipes.Count} · wheel scrolls",
                    _small, _dimTextBrush, left + width - 220f, top);
        }

        private void DrawGuideRow(Graphics g, GameSimulation game, RecipeStatus status, ItemId lookedUp, float left, float y, float width)
        {
            var recipe = status.Recipe;
            var result = new RectangleF(left, y + 4, 40, 40);
            DrawSlot(g, result, new ItemStack(recipe.Output, recipe.OutputCount), recipe.Output == lookedUp, null);
            Track(result, recipe.Output, recipe.OutputCount);
            var output = game.Items[recipe.Output];
            using var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString($"{output.Name}{(recipe.OutputCount > 1 ? $" ×{recipe.OutputCount}" : "")}", _small,
                status.Craftable ? _haveBrush : _amberBrush, new RectangleF(left + 46, y + 6, 190, 16), clip);
            string where;
            Brush whereBrush;
            if (status.Locked)
            {
                where = "taught by the station AI";
                whereBrush = _missingBrush;
            }
            else if (status.MissingStations.Count > 0)
            {
                where = $"at {string.Join(" + ", status.Stations.Select(station => station.DisplayName()))}";
                whereBrush = _missingBrush;
            }
            else
            {
                where = status.Stations.Count == 0 ? "by hand" : $"at {string.Join(" + ", status.Stations.Select(station => station.DisplayName()))}";
                whereBrush = _dimTextBrush;
            }
            if (status.MissingConditions.Count > 0)
                where += $", {string.Join(", ", status.MissingConditions)}";
            g.DrawString(where, _small, whereBrush, new RectangleF(left + 46, y + 24, 190, 16), clip);

            var x = left + 244f;
            foreach (var ingredient in status.Ingredients)
            {
                if (x + 34 > left + width - 4)
                    break;
                var rect = new RectangleF(x, y + 3, 32, 32);
                DrawSlot(g, rect, new ItemStack(ingredient.Item, 1), ingredient.Item == lookedUp, null);
                if (!ingredient.Met)
                    g.DrawRectangle(_missingPen, rect.X, rect.Y, rect.Width, rect.Height);
                DrawCentered(g, $"{ingredient.Have}/{ingredient.Need}", new RectangleF(rect.X - 8, rect.Bottom, rect.Width + 16, 14), _small,
                    ingredient.Met ? _haveBrush : _missingBrush);
                Track(rect, ingredient.Item, ingredient.Need);
                x += 44f;
            }

            void Track(RectangleF rect, ItemId item, int count)
            {
                _guideHits.Add((rect, item));
                if (MousePosition is { } mouse && rect.Contains(mouse))
                    _hoveredSlotStack = new ItemStack(item, count);
            }
        }

        /// <summary>Tooltip lines from the recipe data: where the item is crafted and what it goes into.</summary>
        private IReadOnlyList<string> GuideTooltipLines(ItemId item) =>
            Game is { } game ? CraftingGuide.TooltipLines(game.Crafting, game.Items, item) : Array.Empty<string>();

        /// <summary>
        /// The station AI's next step as one quiet line at the top of the screen (Options → Interface → Guide hints).
        /// The × hides that tip until the next one comes up; clicking the text opens the recipe guide on it.
        /// </summary>
        private void DrawGuideHint(Graphics g, GameSimulation game)
        {
            _hintRect = RectangleF.Empty;
            _hintClose = RectangleF.Empty;
            _hintShown = null;
            if (!_menu.Preferences.GuideHints)
                return;
            var tip = GuideTips(game).FirstOrDefault();
            if (tip is null || tip.Id == _dismissedTip)
                return;
            var start = 10f + 10 * (HotbarSlot + 4) + 12f;
            var end = Width - MinimapWidth - 22f;
            var width = Math.Min(560f, end - start);
            if (width < 160f)
                return;
            var text = $"NEXT  {tip.Text}";
            var textWidth = width - 26f;
            var measured = g.MeasureString(text, _small, (int)textWidth);
            var height = Math.Min(48f, measured.Height) + 8f;
            var rect = new RectangleF(start + (end - start - width) / 2f, 6f, width, height);
            g.FillRectangle(_hintBack, rect);
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord };
            g.DrawString(text, _small, _guideTipBrush, new RectangleF(rect.X + 6, rect.Y + 4, textWidth, height - 6), format);
            _hintClose = new RectangleF(rect.Right - 18f, rect.Y + 2f, 16f, 16f);
            g.DrawString("×", _small, _dimTextBrush, _hintClose.X + 3f, _hintClose.Y);
            _hintRect = rect;
            _hintShown = tip;
        }

        /// <summary>A click on the hint line: the × dismisses the tip (true, no focus); the text returns its item to look up.</summary>
        /// <summary>True over the station AI's hint line (a HUD element: clicks there never reach the world).</summary>
        public bool GuideHintAt(Point point) => !MenuOpen && _hintShown is not null && _hintRect.Contains(point);

        public bool TryHintClick(Point point, out ItemId? focus)
        {
            focus = null;
            if (_hintShown is not { } tip || !_hintRect.Contains(point))
                return false;
            if (_hintClose.Contains(point))
            {
                _dismissedTip = tip.Id;
                Invalidate();
                return true;
            }
            focus = tip.Focus;
            return true;
        }
    }

    /// <summary>The host's side of crafting guidance: the station AI's advice screen, the recipe guide and hold-to-craft.</summary>
    private sealed partial class GameForm
    {
        private string? _craftHoldRecipe;
        private double _craftHoldNext;
        private double _craftHoldInterval;

        /// <summary>A click on a guide item or a material: looks it up (opening the recipe guide from the crafting list).</summary>
        private bool HandleGuideClick(InventoryUiState state, Point point)
        {
            if (!_canvas.TryGuideClick(point, out var item))
                return false;
            if (state.Guide is null)
                state.OpenGuide(item, GuideView.MadeFrom);
            else
                state.GuideLookUp(item);
            SyncCanvas();
            return true;
        }

        /// <summary>The hint line: × hides the tip, the text opens the recipe guide on the tip's item.</summary>
        private bool HandleGuideHintClick(Point point)
        {
            if (!_canvas.TryHintClick(point, out var focus))
                return false;
            if (focus is { } item)
                OpenRecipeGuide(item, GuideView.MadeFrom);
            return true;
        }

        /// <summary>Holding the mouse on a result keeps crafting it, slowly at first and then faster, as Terraria does.</summary>
        private void BeginCraftHold(string recipeId)
        {
            _craftHoldRecipe = recipeId;
            _craftHoldInterval = 0.22;
            _craftHoldNext = FrameClock.Seconds + 0.45;
        }

        private void EndCraftHold() => _craftHoldRecipe = null;

        private void UpdateCraftHold()
        {
            if (_craftHoldRecipe is not { } recipe)
                return;
            if (_game is null || !_menuOpen || _menu.Ui.CurrentState is not InventoryUiState state || !_mouse.InterfaceLeftDown ||
                _canvas.MousePosition is not { } mouse || _canvas.RecipeAt(mouse) != recipe)
            {
                EndCraftHold();
                return;
            }
            var now = FrameClock.Seconds;
            if (now < _craftHoldNext)
                return;
            var result = _game.TryCraft(recipe);
            state.ShowStatus(result.Message);
            if (!result.Success)
            {
                EndCraftHold();
                return;
            }
            _craftHoldInterval = Math.Max(0.05, _craftHoldInterval * 0.8);
            _craftHoldNext = now + _craftHoldInterval;
        }

        /// <summary>Opens the inventory with the recipe guide in place of crafting, looking up <paramref name="item"/>.</summary>
        private void OpenRecipeGuide(ItemId? item, GuideView? view = null)
        {
            if (_game is null)
                return;
            var game = _game;
            if (game.IsNearGuide(game.Player.Motion.Position + game.Player.Motion.Size / 2f))
                game.ConsultGuide(); // within earshot, the station AI also teaches what your materials unlock
            if (_menuOpen && _menu.Ui.CurrentState is InventoryUiState { ChestInventory: null } open)
            {
                open.OpenGuide(item, view);
                SyncCanvas();
                return;
            }
            var state = ShowStorage(null, null, "Chest", new GuideLookupState());
            if (item is { } lookup)
                state?.OpenGuide(lookup, view);
            SyncCanvas();
        }

        /// <summary>The housed station AI standing at <paramref name="position"/>, if it has moved in.</summary>
        private TownNpc? StationAiAt(Vector2 position) => _game?.TownNpcs.FirstOrDefault(npc =>
            npc.Kind == TownNpcKind.StationAi && !npc.IsBound && Vector2.Distance(npc.Motion.Position + npc.Motion.Size / 2f, position) < 2f);

        /// <summary>
        /// Talking to the station AI (Terraria's Guide): its advice on what to do next, read from your progress, with the
        /// recipe guide one click away. Talking within earshot also teaches the recipes your materials unlock.
        /// </summary>
        private void OpenStationAiScreen(TownNpc? npc, int tipIndex = 0, string? status = null, bool consult = false)
        {
            if (_game is null)
                return;
            var game = _game;
            var learnedLine = string.Empty;
            if (consult && game.IsNearGuide(game.Player.Motion.Position + game.Player.Motion.Size / 2f))
            {
                var learned = game.ConsultGuide();
                if (learned.Count > 0)
                    learnedLine = $"I've added {learned.Count} recipe{(learned.Count == 1 ? "" : "s")} to your crafting list: " +
                                  string.Join(", ", learned.Select(id => game.Crafting.FindById(id) is { } recipe ? game.Items[recipe.Output].Name : id)) + ". ";
            }
            var tips = _canvas.GuideTips(game, fresh: true);
            var index = ((tipIndex % tips.Count) + tips.Count) % tips.Count;
            var tip = tips[index];
            var entries = new List<MenuEntry>
            {
                new("Recipe guide", () => OpenRecipeGuide(tip.Focus, tip.Focus is null ? null : GuideView.MadeFrom),
                    Description: "Every recipe that uses or makes an item, with the station each needs."),
                new(tips.Count > 1 ? $"Another tip ({index + 1} of {tips.Count})" : "That's my best advice", () => OpenStationAiScreen(npc, index + 1),
                    Enabled: tips.Count > 1)
            };
            if (npc is not null)
            {
                // 0.3.8: the AI powers the machines; feeding it fuel refills the reserve and better fuel raises its tier.
                entries.Add(new MenuEntry($"Feed power ({game.StationPower}/{GameSimulation.MaxPower}, tier {game.StationPowerTier})",
                    () => OpenStationAiScreen(npc, index, game.FeedStationAi()),
                    Description: "Energy crystals, power cells, uranium, void essence or astral ore. Better fuel powers better machines."));
                entries.Add(new MenuEntry("Shop", () => OpenShopScreen(npc, string.Empty)));
                entries.Add(new MenuEntry("Sell held item", () =>
                {
                    var earned = game.Sell(npc, game.Player.Inventory.SelectedSlot);
                    OpenStationAiScreen(npc, index, earned > 0 ? $"Sold for {earned} credits." : "I can't use that.");
                }));
            }
            entries.Add(new MenuEntry("Goodbye", CloseMenu));
            var title = npc is null ? "Station AI" : $"{npc.Name} the {npc.Definition.Role}";
            var body = status ?? $"“{learnedLine}{tip.Text}”";
            var screen = new MenuScreen("station-ai", title, entries, "Enter  choose      ↑ ↓  move      Esc  leave", body: body);
            _down.Clear();
            _menu.ShowDirect(screen, CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }
    }
}
