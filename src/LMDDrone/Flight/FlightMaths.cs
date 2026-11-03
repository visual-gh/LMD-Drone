using UnityEngine;

namespace LMDDrone.Flight;

/// <summary>The sums of flying, apart from the game, so SelfCheck can run them.</summary>
internal static class FlightMaths
{
    /// <summary>The stick's rate: the middle of the stick calm and the edge full. 0 expo is a straight line.</summary>
    public static float Rate(float stick, float top, float expo) => top * (expo * stick * stick * stick + (1f - expo) * stick);

    /// <summary>The thrust asked for, 0..1. Centred, the left stick holds a level quad up: up from there to full, down from there to nothing.</summary>
    public static float Throttle(float stick, float hover, bool centreIsHover)
    {
        if (!centreIsHover)
        {
            return (stick + 1f) * 0.5f;
        }

        return stick >= 0f ? Mathf.Lerp(hover, 1f, stick) : Mathf.Lerp(hover, 0f, -stick);
    }

    /// <summary>Drag per speed squared, so that full thrust at the steepest tilt that still holds height stops gaining speed at top speed.</summary>
    public static float DragFor(float thrustToWeight, float topSpeed)
    {
        var thrust = thrustToWeight * Physics.gravity.magnitude;
        var across = Mathf.Sqrt(Mathf.Max(0f, thrust * thrust - Physics.gravity.sqrMagnitude));
        return across / (topSpeed * topSpeed);
    }

    /// <summary>Towards a target through a lag of this many seconds, whatever the step.</summary>
    public static float Lag(float seconds, float dt) => seconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / seconds);

    /// <summary>Critically damped: the value and its speed both change smoothly, so no input ever snaps. Reaches about 98% in four times the time.</summary>
    public static float Smooth(float value, float target, ref float rate, float seconds, float dt)
    {
        var omega = 2f / Mathf.Max(0.001f, seconds);
        var x = omega * dt;
        var decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        var gap = value - target;
        var carry = (rate + omega * gap) * dt;
        rate = (rate - omega * carry) * decay;
        return target + (gap + carry) * decay;
    }

    public static Vector3 Smooth(Vector3 value, Vector3 target, ref Vector3 rate, float seconds, float dt) => new(
        Smooth(value.x, target.x, ref rate.x, seconds, dt),
        Smooth(value.y, target.y, ref rate.y, seconds, dt),
        Smooth(value.z, target.z, ref rate.z, seconds, dt));

    /// <summary>Turns an attitude by a body-axis rate in degrees per second. A small turn in the body's frame: no gimbal lock, and the same at any attitude.</summary>
    public static Quaternion Turn(Quaternion attitude, Vector3 rate, float dt)
    {
        var angle = rate.magnitude * dt;
        return angle <= 0f ? attitude : (attitude * Quaternion.AngleAxis(angle, rate.normalized)).normalized;
    }
}
