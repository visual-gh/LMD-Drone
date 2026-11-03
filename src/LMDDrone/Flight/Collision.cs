using LMDDrone.Game;
using UnityEngine;

namespace LMDDrone.Flight;

/// <summary>
/// A small sphere moved through the world's colliders and the drone's own proxies for what has none. Moves, slides and reports the hardest hit. It's built of rays: this game's
/// build only carries the Raycast family, and the SphereCast that MelonLoader restores answers yes with an empty hit to everything (measured).
/// </summary>
internal static class Collision
{
    /// <summary>Prop tip to prop tip, a 5-inch quad is about 0.34 m across.</summary>
    public const float Radius = 0.17f;

    private const float Skin = 0.02f;
    private const int MostSlides = 3;

    /// <summary>Rays around the sphere's middle, at right angles to the way it's going.</summary>
    private const int RimRays = 8;

    /// <summary>Capsules a ray can pass through on its way to what's behind them.</summary>
    private const int MostCapsules = 4;
    private const float PastCapsule = 0.01f;

    /// <summary>All round the sphere, what it's pushed out of.</summary>
    private static readonly Vector3[] Around = BuildAround();

    private static int _mask;

    /// <summary>What counts as solid. Found once: layer numbers don't change.</summary>
    public static int Mask
    {
        get
        {
            if (_mask == 0)
            {
                _mask = LayerMask.GetMask("Ground", "Obstacle", "EnvironmentOther", "Terrain", "Lava", "Water") | (1 << Proxies.Layer);
            }

            return _mask;
        }
    }

    /// <summary>
    /// The first thing solid along a ray. A capsule isn't: the game wraps its trees in them for the bike, as wide at the top as at the trunk (measured: a metre or more around a
    /// tree's thin tip). A ray that starts inside a collider doesn't see it, so the ray just goes on from there, and the tree's own shape in the proxies is what it meets.
    /// </summary>
    public static bool Ray(Vector3 from, Vector3 direction, out RaycastHit hit, float reach)
    {
        var travelled = 0f;
        for (var i = 0; i < MostCapsules; i++)
        {
            if (!Physics.Raycast(from, direction, out hit, reach - travelled, Mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.collider.TryCast<CapsuleCollider>() == null)
            {
                hit.distance += travelled;
                return true;
            }

            travelled += hit.distance + PastCapsule;
            from = hit.point + direction * PastCapsule;
            if (travelled >= reach)
            {
                return false;
            }
        }

        hit = default;
        return false;
    }

    /// <summary>Moves the sphere along the velocity for this step. Returns the speed into the hardest thing it hit, 0 for none. The velocity loses what went into it, plus the bounce.</summary>
    public static float Move(ref Vector3 position, ref Vector3 velocity, float dt, float bounce)
    {
        var hardest = 0f;
        var left = velocity * dt;
        for (var i = 0; i < MostSlides && left.sqrMagnitude > 1e-8f; i++)
        {
            var length = left.magnitude;
            var direction = left / length;
            if (!Sweep(position, direction, length, out var travel, out var normal))
            {
                position += left;
                break;
            }

            position += direction * travel;
            var into = -Vector3.Dot(velocity, normal);
            if (into > 0f)
            {
                hardest = Mathf.Max(hardest, into);
                velocity += normal * (into * (1f + bounce));
            }

            left -= direction * travel;
            left -= normal * Vector3.Dot(left, normal);
        }

        return hardest;
    }

    /// <summary>Out of what the sphere has ended up too close to. Then out from under the ground: a level's mesh is solid from one side only, so a slip through it shows as nothing below and a surface above.</summary>
    public static Vector3 PushOut(Vector3 position)
    {
        var push = Vector3.zero;
        foreach (var direction in Around)
        {
            if (Ray(position, direction, out var hit, Radius))
            {
                push += hit.normal * (Radius - hit.distance);
            }
        }

        position += Vector3.ClampMagnitude(push, Radius);
        if (!Ray(position, Vector3.down, out _, 2000f) && Ray(position + Vector3.up * 200f, Vector3.down, out var surface, 200f))
        {
            position = surface.point + Vector3.up * (Radius + Skin);
        }

        return position;
    }

    /// <summary>Metres of clear air below the middle, up to the given reach.</summary>
    public static float Clearance(Vector3 position, float reach) => Ray(position, Vector3.down, out var hit, reach) ? hit.distance : reach;

    private static bool Sweep(Vector3 position, Vector3 direction, float length, out float travel, out Vector3 normal)
    {
        travel = float.MaxValue;
        normal = Vector3.zero;
        if (Ray(position, direction, out var hit, length + Radius + Skin))
        {
            travel = hit.distance - Radius - Skin;
            normal = hit.normal;
        }

        var side = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var other = Vector3.Cross(direction, side);
        for (var i = 0; i < RimRays; i++)
        {
            var angle = i * Mathf.PI * 2f / RimRays;
            var rim = position + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * Radius;
            if (Ray(rim, direction, out hit, length + Skin) && hit.distance - Skin < travel)
            {
                travel = hit.distance - Skin;
                normal = hit.normal;
            }
        }

        if (travel == float.MaxValue)
        {
            return false;
        }

        travel = Mathf.Clamp(travel, 0f, length);
        return true;
    }

    private static Vector3[] BuildAround()
    {
        var directions = new List<Vector3>();
        foreach (var x in new[] { -1f, 0f, 1f })
        {
            foreach (var y in new[] { -1f, 0f, 1f })
            {
                foreach (var z in new[] { -1f, 0f, 1f })
                {
                    if (x != 0f || y != 0f || z != 0f)
                    {
                        directions.Add(new Vector3(x, y, z).normalized);
                    }
                }
            }
        }

        return directions.ToArray();
    }
}
