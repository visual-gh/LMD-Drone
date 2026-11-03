using UnityEngine;

namespace LMDDrone.Visual;

/// <summary>
/// The drone, built here: the game has none. Low-poly and flat-shaded like everything around it: an X frame, four motors, a body with a small camera, two lights and four props,
/// solid when still and a pale disc when spinning. Dimensions are a real 5-inch quad's.
/// </summary>
internal sealed class DroneModel
{
    /// <summary>Arm centre to motor, metres. A 5-inch quad's motors sit about 11 cm from the middle.</summary>
    private const float Arm = 0.11f;
    private const float PropRadius = 0.065f;
    private const float PropHeight = 0.038f;
    private const int PropSides = 8;

    private static readonly Color Frame = new(0.13f, 0.14f, 0.16f);
    private static readonly Color Body = new(0.2f, 0.22f, 0.25f);
    private static readonly Color Motor = new(0.55f, 0.57f, 0.6f);
    private static readonly Color PropSolid = new(0.85f, 0.42f, 0.14f);
    private static readonly Color Lens = new(0.05f, 0.05f, 0.07f);
    private static readonly Color Front = new(0.2f, 1f, 0.3f);
    private static readonly Color Rear = new(1f, 0.15f, 0.1f);
    private static readonly Color DiscColor = new(0.6f, 0.45f, 0.35f, 0.1f);

    private GameObject _root;
    private readonly Transform[] _props = new Transform[4];
    private readonly MeshRenderer[] _blades = new MeshRenderer[4];
    private readonly MeshRenderer[] _discs = new MeshRenderer[4];
    private readonly List<MeshRenderer> _bodyParts = new();
    private readonly List<MeshRenderer> _solids = new();
    private readonly List<MeshRenderer> _glows = new();
    private float _spin;

    public bool Built => _root != null;

    public void Show(bool show)
    {
        if (_root != null)
        {
            _root.SetActive(show);
        }
    }

    /// <summary>Unseen by the camera but still throwing its shadow, for flying on board. Its shadow is the whole cost: about 30 small boxes in the shadow map.</summary>
    public void ShadowOnly(bool only)
    {
        var mode = only ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (var solid in _solids)
        {
            solid.shadowCastingMode = mode;
        }

        foreach (var glow in _glows)
        {
            glow.enabled = !only;
        }

        // A blurred disc throws none, so the blades stand in for it in the shadow.
        for (var i = 0; i < 4; i++)
        {
            _discs[i].enabled = !only;
            _blades[i].enabled = only;
        }
    }

    /// <summary>Only the props show, for a camera mounted on the frame: the rest would fill the picture.</summary>
    public void PropsOnly(bool only)
    {
        foreach (var part in _bodyParts)
        {
            part.enabled = !only;
        }
    }

    public void Place(Vector3 position, Quaternion rotation, float scale)
    {
        _root.transform.SetPositionAndRotation(position, rotation);
        _root.transform.localScale = Vector3.one * scale;
    }

    /// <summary>Turns the props: blades up to a speed, then the disc a spinning prop is to the eye.</summary>
    public void Spin(float thrust, float dt)
    {
        var fast = thrust > 0.04f;
        _spin = Mathf.Repeat(_spin + thrust * 6000f * dt, 360f);
        for (var i = 0; i < 4; i++)
        {
            _props[i].localRotation = Quaternion.Euler(0f, i % 2 == 0 ? _spin : -_spin, 0f);
            _blades[i].enabled = !fast;
            _discs[i].enabled = fast;
        }
    }

    public void Build(Material template)
    {
        _root = new GameObject("LMDDrone_Model");
        UnityEngine.Object.DontDestroyOnLoad(_root);

        Part("frame", Mesher.Box(new Vector3(0.05f, 0.012f, 0.11f)), Vector3.zero, Frame, template);
        for (var i = 0; i < 4; i++)
        {
            var x = i < 2 ? 1f : -1f;
            var z = i % 3 == 0 ? 1f : -1f;
            var at = new Vector3(x, 0f, z) * (Arm * 0.7071f);
            Part("arm", Mesher.Box(new Vector3(0.018f, 0.006f, Arm)), at * 0.5f, Frame, template).localRotation = Quaternion.Euler(0f, Mathf.Atan2(at.x, at.z) * Mathf.Rad2Deg, 0f);
            Part("motor", Mesher.Cylinder(0.014f, 0.02f, PropSides), at + Vector3.up * 0.016f, Motor, template);

            var hub = new GameObject("prop").transform;
            hub.SetParent(_root.transform, false);
            hub.localPosition = at + Vector3.up * PropHeight;
            _props[i] = hub;
            _blades[i] = Blade(hub, template);
            _solids.Add(_blades[i]);
            _discs[i] = Disc(hub, template);
            _discs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Part("body", Mesher.Box(new Vector3(0.042f, 0.032f, 0.09f)), Vector3.up * 0.024f, Body, template);
        Part("battery", Mesher.Box(new Vector3(0.036f, 0.022f, 0.066f)), Vector3.up * 0.052f, Motor * 0.6f, template);
        Part("camera", Mesher.Box(new Vector3(0.024f, 0.024f, 0.022f)), new Vector3(0f, 0.022f, 0.052f), Lens, template);
        Glow("front light", new Vector3(0f, 0.012f, 0.112f), Front);
        Glow("rear light", new Vector3(0f, 0.012f, -0.112f), Rear);
        _root.SetActive(false);
    }

    private Transform Part(string name, Mesh mesh, Vector3 at, Color color, Material template)
    {
        var part = Piece(name, mesh, _root.transform, at, color, template);
        _bodyParts.Add(part);
        _solids.Add(part);
        return part.transform;
    }

    private static MeshRenderer Piece(string name, Mesh mesh, Transform parent, Vector3 at, Color color, Material template)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Tint(template, color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return renderer;
    }

    private static Material Tint(Material template, Color color)
    {
        var material = new Material(template);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        material.mainTexture = null;
        return material;
    }

    private void Glow(string name, Vector3 at, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        go.transform.localPosition = at;
        go.AddComponent<MeshFilter>().sharedMesh = Mesher.Box(Vector3.one * 0.008f);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Unlit(color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _bodyParts.Add(renderer);
        _glows.Add(renderer);
    }

    private static MeshRenderer Blade(Transform hub, Material template) => Piece("blade", Mesher.Box(new Vector3(PropRadius * 2f, 0.002f, 0.014f)), hub, Vector3.zero, PropSolid, template);

    private static MeshRenderer Disc(Transform hub, Material template)
    {
        var renderer = Piece("disc", Mesher.Cylinder(PropRadius, 0.001f, 16), hub, Vector3.zero, PropSolid, template);
        var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        var material = new Material(shader != null ? shader : Shader.Find("UI/Default"));
        material.SetColor("_TintColor", DiscColor);
        material.color = DiscColor;
        renderer.sharedMaterial = material;
        return renderer;
    }

    /// <summary>The UI's shader: this build is sure to carry it, and it draws on a mesh like any other, unlit.</summary>
    private static Material Unlit(Color color)
    {
        var material = new Material(Shader.Find("UI/Default"));
        material.color = color;
        return material;
    }

    /// <summary>The meshes: hard edges, so each face has its own corners and takes the light as one flat colour.</summary>
    private static class Mesher
    {
        public static Mesh Box(Vector3 size)
        {
            var h = size * 0.5f;
            var corners = new[]
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            var faces = new[]
            {
                new[] { 0, 3, 2, 1 }, new[] { 5, 6, 7, 4 }, new[] { 4, 7, 3, 0 },
                new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 4, 0, 1, 5 },
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var face in faces)
            {
                var start = vertices.Count;
                foreach (var corner in face)
                {
                    vertices.Add(corners[corner]);
                }

                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            return Finish(vertices, triangles);
        }

        public static Mesh Cylinder(float radius, float height, int sides)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var top = height * 0.5f;
            for (var i = 0; i < sides; i++)
            {
                var a = Ring(i, sides, radius);
                var b = Ring(i + 1, sides, radius);
                var start = vertices.Count;
                vertices.AddRange(new[] { a - Vector3.up * top, a + Vector3.up * top, b + Vector3.up * top, b - Vector3.up * top });
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                start = vertices.Count;
                vertices.AddRange(new[] { Vector3.up * top, b + Vector3.up * top, a + Vector3.up * top });
                triangles.AddRange(new[] { start, start + 1, start + 2 });
                start = vertices.Count;
                vertices.AddRange(new[] { -Vector3.up * top, a - Vector3.up * top, b - Vector3.up * top });
                triangles.AddRange(new[] { start, start + 1, start + 2 });
            }

            return Finish(vertices, triangles);
        }

        private static Vector3 Ring(int i, int sides, float radius)
        {
            var angle = i * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }

        private static Mesh Finish(List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
