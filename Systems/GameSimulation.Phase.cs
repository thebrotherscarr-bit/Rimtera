using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The phase pool: a deep-cavern lake of <see cref="LiquidKind.Phase"/>. Items dropped into it shift into a related
/// material (a small original table), and a player who bathes in it is <see cref="StatusId.Phased"/> for a few
/// seconds, sinking through solid floors whenever they stand on one.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Real seconds of phasing after leaving the pool.</summary>
    public const float PhasedSeconds = 3f;
    private const float PhaseDropInterval = 0.3f;
    private float _phaseItemTimer;
    private float _phaseDropTimer;
    private readonly HashSet<long> _phaseShifted = new();

    /// <summary>What an item becomes after soaking in the phase pool (null when it is unaffected).</summary>
    public static ItemId? PhaseTransmute(ItemId item) => item switch
    {
        ItemId.SoilBlock => ItemId.MudBlock,
        ItemId.MudBlock => ItemId.SoilBlock,
        ItemId.SandBlock => ItemId.GlassBlock,
        ItemId.RockBlock => ItemId.EnergyCrystal,
        ItemId.SnowBlock => ItemId.ThinIceBlock,
        ItemId.CopperOre => ItemId.ScrapIronOre,
        ItemId.ScrapIronOre => ItemId.CopperOre,
        ItemId.Timber => ItemId.LivingWood,
        ItemId.Berry => ItemId.GlowSpore,
        ItemId.CoralShard => ItemId.CausticShard,
        ItemId.ObsidianShard => ItemId.AshBlock,
        ItemId.DyePetals => ItemId.PrismShard,
        // Gems turn round a ring: amethyst, sapphire, emerald, topaz, ruby and back; diamonds split light into prism shards.
        ItemId.AmethystShard => ItemId.SapphireShard,
        ItemId.SapphireShard => ItemId.EmeraldShard,
        ItemId.EmeraldShard => ItemId.TopazShard,
        ItemId.TopazShard => ItemId.RubyShard,
        ItemId.RubyShard => ItemId.AmethystShard,
        ItemId.DiamondShard => ItemId.PrismShard,
        _ => null
    };

    /// <summary>True while the player is phased (falls through floors).</summary>
    public bool PlayerPhased => Player.Statuses.IsActive(StatusId.Phased);

    private void UpdatePhase(float deltaSeconds)
    {
        // Bathing in the pool keeps the phasing topped up.
        if (!Player.IsDead && LiquidAtBody() == LiquidKind.Phase)
        {
            if (!PlayerPhased)
                PostMessage("The phase pool hums through you. Solid ground feels thin.");
            Player.Statuses.Apply(StatusId.Phased, PhasedSeconds * VitalHoursPerRealSecond);
        }
        if (PlayerPhased && Player.Motion.Grounded)
        {
            _phaseDropTimer += deltaSeconds;
            if (_phaseDropTimer >= PhaseDropInterval)
            {
                _phaseDropTimer = 0f;
                PhaseThroughFloor();
            }
        }
        else
            _phaseDropTimer = 0f;

        _phaseItemTimer += deltaSeconds;
        if (_phaseItemTimer < 0.25f || _groundItems.Count == 0)
            return;
        _phaseItemTimer = 0f;
        foreach (var item in _groundItems.ToArray())
        {
            if (_phaseShifted.Contains(item.Id) || PhaseTransmute(item.ItemId) is not { } result)
                continue;
            var cell = ClampCell(new GridPoint((int)MathF.Floor(item.Position.X), (int)MathF.Floor(item.Position.Y)));
            var data = World.GetCell(cell);
            if (data.LiquidKind != LiquidKind.Phase || data.LiquidDepth < 0.3f)
                continue;
            _groundItems.Remove(item);
            var shifted = SpawnGroundItem(result, item.Count, item.Position, item.Calibration, pickupDelay: 0.5f);
            if (shifted is not null)
                _phaseShifted.Add(shifted.Id);
            Flags.Increment("phase.transmutes");
        }
        if (_phaseShifted.Count > 512)
            _phaseShifted.RemoveWhere(id => _groundItems.All(item => item.Id != id));
    }

    /// <summary>Sinks the player through the floor to the next open space below (never through bedrock).</summary>
    private bool PhaseThroughFloor()
    {
        var motion = Player.Motion;
        var size = motion.Size;
        var feetRow = (int)MathF.Floor(motion.Position.Y + size.Y + 0.05f);
        for (var drop = 1; drop <= 28; drop++)
        {
            var row = feetRow + drop - 1;
            if (!World.Contains((int)MathF.Floor(motion.Position.X), row) ||
                World.GetCell((int)MathF.Floor(motion.Position.X + size.X / 2f), row).TileId == TileId.Bedrock)
                return false;
            var candidate = new Vector2(motion.Position.X, motion.Position.Y + drop);
            if (IsSolidActorPosition(candidate, size))
                continue;
            motion.Position = candidate;
            motion.Velocity = new Vector2(motion.Velocity.X, 0f);
            motion.FallPeakY = candidate.Y;
            Flags.Increment("phase.drops");
            return true;
        }
        return false;
    }
}
