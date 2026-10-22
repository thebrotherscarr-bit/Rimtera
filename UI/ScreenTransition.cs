using Rimtera.World;

namespace Rimtera.UI;

public enum TransitionKind : byte
{
    None,
    Generating,
    Loading,
    Saving,
    Respawning,
    FadeIn
}

/// <summary>
/// Renderer-neutral state for the screens between menus and play: world generation with a
/// pass bar and an overall bar, loading, saving, the respawn countdown, and the fade into
/// the world. Visual progress eases toward the reported target so bars never jump.
/// </summary>
public sealed class ScreenTransition
{
    private const float FadeSeconds = 0.8f;

    public TransitionKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public float TargetPass { get; private set; }
    public float TargetTotal { get; private set; }
    public float VisualPass { get; private set; }
    public float VisualTotal { get; private set; }
    public float Elapsed { get; private set; }
    public float CountdownSeconds { get; private set; }

    public bool IsActive => Kind != TransitionKind.None;

    /// <summary>True while gameplay must stay frozen and input is ignored.</summary>
    public bool BlocksGameplay => Kind is TransitionKind.Generating or TransitionKind.Loading or TransitionKind.Saving
        or TransitionKind.Respawning || (Kind == TransitionKind.FadeIn && Elapsed < FadeSeconds * 0.5f);

    /// <summary>Black overlay opacity: full while working, fading out as the world appears.</summary>
    public float BlackoutAlpha => Kind switch
    {
        TransitionKind.None => 0f,
        TransitionKind.FadeIn => Math.Clamp(1f - Elapsed / FadeSeconds, 0f, 1f),
        TransitionKind.Respawning => 0.55f,
        _ => 1f
    };

    public bool ShowsProgress => Kind is TransitionKind.Generating or TransitionKind.Loading or TransitionKind.Saving;

    public void Begin(TransitionKind kind, string title, string message = "")
    {
        Kind = kind;
        Title = title;
        Message = message;
        TargetPass = TargetTotal = VisualPass = VisualTotal = 0f;
        Elapsed = 0f;
        CountdownSeconds = 0f;
    }

    public void BeginCountdown(string title, string message, float seconds)
    {
        Begin(TransitionKind.Respawning, title, message);
        CountdownSeconds = seconds;
    }

    public void Report(GenerationProgress progress)
    {
        if (Kind is not (TransitionKind.Generating or TransitionKind.Loading or TransitionKind.Saving))
            return;
        Message = progress.PassName;
        TargetPass = (float)Math.Clamp(progress.PassFraction, 0d, 1d);
        TargetTotal = Math.Max(TargetTotal, (float)Math.Clamp(progress.TotalFraction, 0d, 1d));
    }

    public void Complete()
    {
        TargetPass = TargetTotal = 1f;
        VisualPass = VisualTotal = 1f;
    }

    public void FadeIn()
    {
        Kind = TransitionKind.FadeIn;
        Elapsed = 0f;
    }

    public void Clear() => Kind = TransitionKind.None;

    /// <summary>Advances easing, fades and countdowns. Returns true when a countdown just reached zero.</summary>
    public bool Update(float deltaSeconds)
    {
        if (!IsActive)
            return false;
        Elapsed += deltaSeconds;
        var ease = 1f - MathF.Exp(-deltaSeconds * 8f);
        VisualPass += (TargetPass - VisualPass) * ease;
        VisualTotal += (TargetTotal - VisualTotal) * ease;
        // Keep the overall bar creeping forward slightly so long passes never look frozen.
        if (Kind == TransitionKind.Generating && VisualTotal < TargetTotal + 0.04f && VisualTotal < 0.97f)
            VisualTotal = Math.Min(VisualTotal + deltaSeconds * 0.01f, 0.97f);

        if (Kind == TransitionKind.FadeIn && Elapsed >= FadeSeconds)
            Clear();
        if (Kind == TransitionKind.Respawning && CountdownSeconds > 0f)
        {
            CountdownSeconds -= deltaSeconds;
            if (CountdownSeconds <= 0f)
            {
                CountdownSeconds = 0f;
                return true;
            }
        }
        return false;
    }
}
