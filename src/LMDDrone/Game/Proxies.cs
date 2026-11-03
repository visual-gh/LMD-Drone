using UnityEngine;

namespace LMDDrone.Game;

/// <summary>
/// Collision for what the world only draws. A tree has none of its own shape, and few rocks do, so each gets a mesh collider of its own. They sit on an empty layer with its
/// physics off: only the drone's rays see them, never the game's or the bike. Only what's switched on counts: a level holds the props of all its trails, most of them off, and a
/// proxy of one of those is a wall nobody can see. The level spawns its trees as the camera comes near, so it keeps looking during a flight, and a proxy goes when what it stands
/// for is switched off or moved. The look costs a millisecond or two, and the rest goes a little per frame, so it never shows.
/// </summary>
internal static class Proxies
{
    /// <summary>An empty layer. Only the drone's rays look at it.</summary>
    public const int Layer = 30;

    /// <summary>Time per frame for all of it, in seconds.</summary>
    private const float BudgetPerFrame = 0.002f;

    /// <summary>Seconds between looks for what's newly spawned, while flying.</summary>
    private const float LookGap = 0.5f;

    /// <summary>Smaller than this across, it's a pebble: fly through it.</summary>
    private const float SmallestSize = 0.8f;

    /// <summary>The detail level to use: the first after the full one keeps a tree's real shape in about a third of the triangles, where the lowest is one bare cone.</summary>
    private const int WantedDetail = 1;

    /// <summary>Metres a source can have moved and still be what its proxy stands for.</summary>
    private const float MovedBy = 0.1f;

    private sealed class Proxy
    {
        public MeshFilter Source;
        public GameObject Collider;
        public Vector3Int Spot;
        public Vector3 At;
    }

    private static readonly HashSet<Vector3Int> Spots = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly List<MeshFilter> Todo = new();
    private static readonly List<Proxy> Standing = new();
    private static readonly Dictionary<Vector3Int, (MeshFilter filter, int score)> Best = new();

    private static GameObject _root;
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<MeshFilter> _scan;
    private static int _scanned;
    private static int _ground;
    private static int _obstacle;
    private static int _other;
    private static int _next;
    private static int _checked;
    private static int _seenFilters;
    private static float _lookAt;
    private static bool _layerReady;

    public static int Built { get; private set; }

    /// <summary>A new level: the old ones went with it.</summary>
    public static void Reset()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
        }

        _root = null;
        Spots.Clear();
        Seen.Clear();
        Todo.Clear();
        Standing.Clear();
        Best.Clear();
        _scan = null;
        _next = 0;
        _checked = 0;
        _seenFilters = 0;
        _lookAt = 0f;
        Built = 0;
    }

    /// <summary>Per frame in a ride. Riding, it looks once and builds that; flying, it keeps looking and keeps what stands true.</summary>
    public static void Tick(bool flying)
    {
        if (_root == null || (flying && Time.unscaledTime >= _lookAt))
        {
            _lookAt = Time.unscaledTime + LookGap;
            Look();
        }

        var until = Time.realtimeSinceStartup + BudgetPerFrame;
        Scan(until);
        while (_next < Todo.Count && Time.realtimeSinceStartup < until)
        {
            Build(Todo[_next++]);
        }

        if (_next >= Todo.Count && _next > 0)
        {
            Todo.Clear();
            _next = 0;
        }

        if (flying)
        {
            Check(until);
        }
    }

    /// <summary>Starts a scan of everything switched on, if that's changed. The scan itself goes a little per frame: over a whole level it would be a visible hitch (measured: 80 ms).</summary>
    private static void Look()
    {
        if (!_layerReady)
        {
            _layerReady = true;
            _ground = LayerMask.NameToLayer("Ground");
            _obstacle = LayerMask.NameToLayer("Obstacle");
            _other = LayerMask.NameToLayer("EnvironmentOther");
            for (var i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(Layer, i, true);
            }
        }

        if (_root == null)
        {
            _root = new GameObject("LMDDrone_Proxies") { layer = Layer };
        }

        if (_scan != null)
        {
            return;
        }

        // Only what's active in the hierarchy comes back from this one.
        var filters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
        if (filters.Count != _seenFilters)
        {
            _seenFilters = filters.Count;
            _scan = filters;
            _scanned = 0;
            Best.Clear();
        }
    }

    /// <summary>For each thing drawn at several levels of detail, the one wanted: they sit at one spot. Trees and rocks only, without a solid collider of their own.</summary>
    private static void Scan(float until)
    {
        if (_scan == null)
        {
            return;
        }

        while (_scanned < _scan.Count && Time.realtimeSinceStartup < until)
        {
            var filter = _scan[_scanned++];
            if (filter != null && Seen.Add(filter.Pointer))
            {
                Consider(filter);
            }
        }

        if (_scanned >= _scan.Count)
        {
            foreach (var (spot, found) in Best)
            {
                Spots.Add(spot);
                Todo.Add(found.filter);
            }

            _scan = null;
            Best.Clear();
        }
    }

    private static void Consider(MeshFilter filter)
    {
        var layer = filter.gameObject.layer;
        if ((layer != _ground && layer != _obstacle && layer != _other) || filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.name == "terrain_chunk")
        {
            return;
        }

        var spot = Vector3Int.RoundToInt(filter.transform.position * 5f);
        if (Spots.Contains(spot))
        {
            return;
        }

        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || renderer.bounds.size.magnitude < SmallestSize)
        {
            return;
        }

        // A capsule is the bike's idea of a tree, and the drone's rays pass it by: see Collision.Ray.
        var solid = filter.GetComponent<Collider>();
        if (solid != null && !solid.isTrigger && solid.TryCast<CapsuleCollider>() == null)
        {
            return;
        }

        var score = -Mathf.Abs(DetailLevel(filter.name) - WantedDetail);
        if (!Best.TryGetValue(spot, out var held) || score > held.score)
        {
            Best[spot] = (filter, score);
        }
    }

    private static int DetailLevel(string name)
    {
        var at = name.LastIndexOf("LOD", StringComparison.Ordinal);
        return at >= 0 && at + 3 < name.Length && char.IsDigit(name[at + 3]) ? name[at + 3] - '0' : 0;
    }

    private static void Build(MeshFilter filter)
    {
        // It may have gone with a chunk of the level since the look.
        if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy)
        {
            return;
        }

        var collider = new GameObject("p") { layer = Layer };
        var transform = collider.transform;
        transform.SetParent(_root.transform, false);
        var source = filter.transform;
        transform.SetPositionAndRotation(source.position, source.rotation);
        transform.localScale = source.lossyScale;
        collider.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        Standing.Add(new Proxy { Source = filter, Collider = collider, Spot = Vector3Int.RoundToInt(source.position * 5f), At = source.position });
        Built++;
    }

    /// <summary>A proxy goes when what it stands for has, and the spot and the filter are free to be looked at again.</summary>
    private static void Check(float until)
    {
        while (Standing.Count > 0 && Time.realtimeSinceStartup < until)
        {
            _checked = _checked >= Standing.Count ? 0 : _checked;
            var proxy = Standing[_checked];
            if (proxy.Source != null && proxy.Source.gameObject.activeInHierarchy && (proxy.Source.transform.position - proxy.At).sqrMagnitude < MovedBy * MovedBy)
            {
                _checked++;
                if (_checked >= Standing.Count)
                {
                    _checked = 0;
                    return;
                }

                continue;
            }

            UnityEngine.Object.Destroy(proxy.Collider);
            Spots.Remove(proxy.Spot);
            if (proxy.Source != null)
            {
                Seen.Remove(proxy.Source.Pointer);
            }

            Standing[_checked] = Standing[^1];
            Standing.RemoveAt(Standing.Count - 1);

            // What comes back is new to the count too, or in the same count as what went.
            _seenFilters = -1;
            Built--;
        }
    }
}
