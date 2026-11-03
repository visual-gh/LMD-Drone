using LMDDrone.Controls;
using UnityEngine;

namespace LMDDrone.Flight;

/// <summary>
/// A camera drone. It holds its height, levels itself and stops when let go. The picture's horizon stays level but for a little lean, so it feels flown and not slid.
/// Nothing snaps: all of it goes through critically damped smoothing.
/// </summary>
internal sealed class SimpleFlight
{
    private const float ClimbSpeed = 8f;
    private const float FastestSpeed = 26f;
    private const float CineSpeed = 4f;
    private const float TurnRate = 90f;
    private const float CineTurnRate = 40f;

    /// <summary>Time constants, in the sense of FlightMaths.Smooth: about 0.8 s to full speed and 0.6 s to a stop.</summary>
    private const float Pickup = 0.2f;
    private const float Settle = 0.15f;
    private const float TurnEase = 0.12f;
    private const float CineTurnEase = 0.3f;
    private const float LeanEase = 0.2f;

    private const float BankAtFull = 10f;
    private const float LeanAtFull = 5f;

    /// <summary>The height it holds over what's below, and how firmly.</summary>
    private const float HoverHeight = 1.5f;
    private const float HoverLift = 5f;

    private Vector3 _velocityRate;
    private float _yawRate;
    private float _yawRateRate;
    private float _lean;
    private float _leanRate;
    private float _bank;
    private float _bankRate;

    public float Yaw { get; private set; }

    /// <summary>From wherever it was: what it had as a heading and how it looked.</summary>
    public void Begin(Quaternion attitude, float cameraTilt)
    {
        var forward = attitude * Vector3.forward;
        Yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        _yawRate = _yawRateRate = 0f;
        Tilt.Begin(cameraTilt, 0f);
        _velocityRate = Vector3.zero;
        _lean = _leanRate = _bank = _bankRate = 0f;
    }

    /// <summary>The picture's rotation.</summary>
    public Quaternion View => Quaternion.Euler(-Tilt.Shown + _lean, Yaw, _bank);

    /// <summary>Home is level.</summary>
    public CameraTilt Tilt { get; } = new();

    public void Step(in PilotInput input, Vector3 position, ref Vector3 velocity, float dt)
    {
        var slow = input.Slow * (1f - input.Fast);
        var cruise = Config.SimpleSpeed;
        var speed = Mathf.Lerp(Mathf.Lerp(cruise, FastestSpeed, input.Fast), CineSpeed, slow);
        var turn = Mathf.Lerp(TurnRate, CineTurnRate, slow);
        var turnEase = Mathf.Lerp(TurnEase, CineTurnEase, slow);

        _yawRate = FlightMaths.Smooth(_yawRate, input.Turn * turn, ref _yawRateRate, turnEase, dt);
        Yaw = Mathf.Repeat(Yaw + _yawRate * dt, 360f);
        var heading = Quaternion.Euler(0f, Yaw, 0f);

        var climb = ClimbSpeed * speed / cruise;
        var wanted = heading * new Vector3(input.Side * speed, 0f, input.Forward * speed);
        wanted.y = input.Climb * climb;

        // Only what's asked of it holds it up: a pilot can still set it down as low as the hover height.
        var clearance = Collision.Clearance(position, HoverHeight + 1f);
        if (clearance < HoverHeight)
        {
            wanted.y = Mathf.Max(wanted.y, (HoverHeight - clearance) * HoverLift);
        }

        var asked = input.Side != 0f || input.Forward != 0f || input.Climb != 0f;
        velocity = FlightMaths.Smooth(velocity, wanted, ref _velocityRate, asked ? Pickup : Settle, dt);

        Tilt.Step(Config.InvertTilt ? -input.Tilt : input.Tilt, dt);

        var local = Quaternion.Inverse(heading) * velocity;
        _lean = FlightMaths.Smooth(_lean, Mathf.Clamp(local.z / FastestSpeed, -1f, 1f) * LeanAtFull, ref _leanRate, LeanEase, dt);
        _bank = FlightMaths.Smooth(_bank, Mathf.Clamp((local.x + _yawRate * 0.05f) / FastestSpeed * 1.5f, -1f, 1f) * -BankAtFull, ref _bankRate, LeanEase, dt);
    }
}
