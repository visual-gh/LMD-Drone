using LMDDrone.Controls;
using UnityEngine;

namespace LMDDrone.Flight;

/// <summary>
/// A simulated 5-inch FPV quad in rate mode. The sticks set how fast it turns and nothing levels it: thrust goes along its own up axis, and gravity and drag do the rest. It keeps
/// turning at zero throttle, as air mode does on a real one.
/// </summary>
internal sealed class AdvancedFlight
{
    /// <summary>Seconds for the motors to answer, and for the quad to reach the rate asked of it.</summary>
    private const float MotorLag = 0.05f;
    private const float RateLag = 0.04f;

    /// <summary>Disarming, as a real quad lands: the throttle held at the bottom this long, this close to the ground.</summary>
    private const float DisarmStick = -0.9f;
    private const float DisarmClearance = 0.3f;
    private const float DisarmSeconds = 0.5f;

    /// <summary>Extra lift near the ground, as a share of the thrust at this height and below, fading out by twice it.</summary>
    private const float GroundEffect = 0.15f;
    private const float GroundEffectHeight = 0.5f;

    /// <summary>The rate that shakes it when dropping through its own air, at this speed and more.</summary>
    private const float PropwashRate = 40f;
    private const float PropwashFrom = 4f;
    private const float PropwashFull = 12f;

    private Vector3 _rate;
    private float _thrust;
    private float _time;
    private float _lowFor;

    public Quaternion Attitude { get; private set; } = Quaternion.identity;

    /// <summary>0..1, what the motors are giving: for the sound.</summary>
    public float Thrust => _thrust;

    /// <summary>The rate now in degrees per second: for the sound.</summary>
    public Vector3 Rate => _rate;

    /// <summary>Off once landed, until the throttle goes above centre.</summary>
    public bool Armed { get; private set; } = true;

    /// <summary>Home is the setting's uptilt.</summary>
    public CameraTilt Tilt { get; } = new();

    /// <summary>The picture's rotation: the camera sits tilted up from the quad's nose.</summary>
    public Quaternion View => Attitude * Quaternion.Euler(-Tilt.Shown, 0f, 0f);

    /// <summary>From wherever it was: level, on its heading, holding itself up. What's in the velocity carries on.</summary>
    public void Begin(Quaternion attitude)
    {
        Level(attitude);
        Armed = true;
        Tilt.Begin(Config.AdvancedUptilt, Config.AdvancedUptilt);
    }

    /// <summary>Upright where it is, nothing moving: for a quad stuck on its back. The camera and the motors stay as they were.</summary>
    public void Upright(ref Vector3 velocity)
    {
        Level(Attitude);
        velocity = Vector3.zero;
    }

    private void Level(Quaternion attitude)
    {
        var forward = attitude * Vector3.forward;
        Attitude = Quaternion.Euler(0f, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
        _rate = Vector3.zero;
        _thrust = Armed ? Hover : 0f;
        _lowFor = 0f;
    }

    private static float Hover => 1f / Mathf.Max(1.1f, Config.AdvancedThrust);

    public void Step(in PilotInput input, ref Vector3 velocity, float clearance, float dt)
    {
        _time += dt;
        Tilt.Step(Config.InvertTilt ? -input.Tilt : input.Tilt, dt);
        Arm(input.Climb, clearance, dt);

        // Unity's axes: +x tips the nose down, +y turns right, +z rolls left. Disarmed, the sticks do nothing.
        var wanted = !Armed ? Vector3.zero : new Vector3(
            FlightMaths.Rate(input.Forward, Config.AdvancedRate, Config.AdvancedExpo),
            FlightMaths.Rate(input.Turn, Config.AdvancedYawRate, Config.AdvancedExpo),
            FlightMaths.Rate(-input.Side, Config.AdvancedRate, Config.AdvancedExpo));
        _rate = Vector3.Lerp(_rate, wanted, FlightMaths.Lag(RateLag, dt));

        var up = Attitude * Vector3.up;
        var falling = Mathf.InverseLerp(PropwashFrom, PropwashFull, -velocity.y) * Mathf.Clamp01(Vector3.Dot(up, Vector3.up));
        var shake = new Vector3(Mathf.PerlinNoise(_time * 11f, 0.3f) - 0.5f, 0f, Mathf.PerlinNoise(0.7f, _time * 13f) - 0.5f) * (PropwashRate * 2f * falling);
        Attitude = FlightMaths.Turn(Attitude, _rate + shake, dt);

        var asked = Armed ? FlightMaths.Throttle(input.Climb, Hover, Config.ThrottleCentre) : 0f;
        _thrust = Mathf.Lerp(_thrust, asked, FlightMaths.Lag(MotorLag, dt));

        var thrustToWeight = Config.AdvancedThrust;
        var gravity = Physics.gravity;
        var lift = 1f + GroundEffect * Mathf.InverseLerp(GroundEffectHeight * 2f, GroundEffectHeight, clearance);
        var acceleration = up * (_thrust * thrustToWeight * gravity.magnitude * lift) + gravity;
        acceleration -= velocity * (velocity.magnitude * FlightMaths.DragFor(thrustToWeight, Config.AdvancedTopSpeed));
        velocity += acceleration * dt;
    }

    /// <summary>Full down near the ground for a moment disarms; in the air it's only a dive. Any throttle above centre arms it again.</summary>
    public void Arm(float stick, float clearance, float dt)
    {
        if (!Armed)
        {
            Armed = stick > 0f;
            return;
        }

        _lowFor = stick < DisarmStick && clearance < DisarmClearance ? _lowFor + dt : 0f;
        if (_lowFor >= DisarmSeconds)
        {
            Armed = false;
            _lowFor = 0f;
        }
    }

    /// <summary>What a hit does to its attitude: a knock, in proportion.</summary>
    public void Knock(Vector3 normal, float speed)
    {
        var local = Quaternion.Inverse(Attitude) * normal;
        _rate += new Vector3(local.z, 0f, -local.x) * (speed * 30f);
    }

    /// <summary>The fall of a quad with its motors off, for the moment before the picture goes.</summary>
    public void Tumble(ref Vector3 velocity, float dt)
    {
        _thrust = 0f;
        Attitude = FlightMaths.Turn(Attitude, _rate, dt);
        velocity += Physics.gravity * dt;
    }

    /// <summary>Spins it up as a crash does.</summary>
    public void Spin(float speed)
    {
        var amount = Mathf.Clamp(speed * 60f, 300f, 1200f);
        _rate = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-1f, 1f)).normalized * amount;
    }
}
