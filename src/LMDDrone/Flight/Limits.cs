using UnityEngine;

namespace LMDDrone.Flight;

/// <summary>Where the drone may go: the level's terrain, and a margin. It's a signal limit, so it pushes back softly and the picture says why.</summary>
internal readonly struct Limits
{
    /// <summary>Metres outside the terrain where the push and the static start, and where it stops altogether.</summary>
    public const float SoftMargin = 40f;
    public const float HardMargin = 80f;
    public const float Ceiling = 200f;

    /// <summary>The push at the hard limit, in metres per second per second.</summary>
    private const float PushAtLimit = 60f;

    private readonly Bounds _terrain;

    public Limits(Bounds terrain) => _terrain = terrain;

    /// <summary>The acceleration back in, and how far past the soft limit it is, 0..1: what the static shows.</summary>
    public Vector3 Push(Vector3 position, out float past)
    {
        var over = new Vector3(
            Past(position.x, _terrain.min.x, _terrain.max.x, HardMargin),
            Past(position.y, _terrain.min.y, _terrain.max.y, Ceiling),
            Past(position.z, _terrain.min.z, _terrain.max.z, HardMargin));
        past = Mathf.Clamp01(Mathf.Max(Mathf.Abs(over.x), Mathf.Max(Mathf.Abs(over.y), Mathf.Abs(over.z))));
        return -over * PushAtLimit;
    }

    public Vector3 Clamp(Vector3 position) => new(
        Mathf.Clamp(position.x, _terrain.min.x - HardMargin, _terrain.max.x + HardMargin),
        Mathf.Clamp(position.y, _terrain.min.y, _terrain.max.y + Ceiling),
        Mathf.Clamp(position.z, _terrain.min.z - HardMargin, _terrain.max.z + HardMargin));

    /// <summary>-1..1: how far into the last stretch before the limit, 0 inside. Half of the margin is free, except that the ground needs none.</summary>
    private static float Past(float value, float min, float max, float margin)
    {
        var free = margin * 0.5f;
        if (value > max + free)
        {
            return Mathf.Min(1f, (value - max - free) / free);
        }

        return value < min - free ? Mathf.Max(-1f, (value - min + free) / free) : 0f;
    }
}
