using UnityEngine;

namespace LMDDrone.Flight;

/// <summary>
/// The camera's tilt off the D-pad, the same in both modes: a tap is one step, a hold sweeps after a moment, and the shown angle eases there on LMD Alt Camera's zoom curve.
/// Degrees, up positive.
/// </summary>
internal sealed class CameraTilt
{
    public const float Lowest = -90f;
    public const float Highest = 90f;

    private const float StepSize = 10f;
    private const float HoldDelay = 0.25f;

    /// <summary>With the step and the delay, a hold sweeps the whole range in about 1.5 s.</summary>
    private const float HoldRate = 150f;

    /// <summary>Alt Camera's ViewMath.EaseZoom, its rate and where it lands.</summary>
    private const float Ease = 6f;
    private const float Landed = 0.001f;

    private float _held;
    private float _home;

    public float Target { get; private set; }

    public float Shown { get; private set; }

    public void Begin(float tilt, float home)
    {
        Target = Shown = Mathf.Clamp(tilt, Lowest, Highest);
        _home = home;
        _held = 0f;
    }

    /// <summary>Back to where the mode starts it, eased.</summary>
    public void Home() => Target = _home;

    public void Step(float stick, float dt)
    {
        if (stick == 0f)
        {
            _held = 0f;
        }
        else
        {
            if (_held == 0f)
            {
                Target += StepSize * Mathf.Sign(stick);
            }
            else if (_held >= HoldDelay)
            {
                Target += stick * HoldRate * dt;
            }

            _held += dt;
        }

        Target = Mathf.Clamp(Target, Lowest, Highest);
        var next = Mathf.Lerp(Shown, Target, 1f - Mathf.Exp(-dt * Ease));
        Shown = Mathf.Abs(next - Target) < Landed ? Target : next;
    }
}
