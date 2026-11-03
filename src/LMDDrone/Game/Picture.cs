using Il2CppInterop.Runtime;
using Il2CppMegagon.Framework.Graphics.PostEffects;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace LMDDrone.Game;

/// <summary>
/// The picture for a camera that goes where the game's never does, all of it given back on landing. Depth of field is off: it's set for a camera far above the bike. The flat
/// backdrop quads are off: their straight edge shows from up here, and without them it's a sea of mist. The game's haze thins with height, so the trail still reads from far up.
/// </summary>
internal sealed class Picture
{
    /// <summary>Height above ground where the haze starts to thin, and where it's as thin as it gets.</summary>
    private const float ThinFrom = 15f;
    private const float ThinFull = 65f;

    /// <summary>What's left of the game's fog when thinned the most.</summary>
    private const float FogLeft = 0.5f;
    private const float HeightFogLeft = 0.4f;
    private const float FogDistanceTimes = 2f;

    /// <summary>Seconds to follow a change: the ground can come and go under it.</summary>
    private const float Ease = 0.8f;

    private readonly List<Renderer> _quads = new();
    private readonly List<(Transform transform, Vector3 scale)> _hud = new();

    private PostProcessVolume _volume;
    private PostProcessProfile _profile;
    private DepthOfField _depth;
    private bool _depthWas;
    private bool _depthChanged;
    private float _thin;

    public void Begin(Camera camera)
    {
        _thin = 0f;
        Try("depth of field", () => DepthOff(camera));
        Try("backdrop", HideQuads);
        Try("HUD", HideHud);
        Try("haze", () => MakeHaze(camera));
    }

    /// <summary>Every frame of the flight.</summary>
    public void Update(float clearance, float dt)
    {
        // Options' Apply writes depth of field again, under the flight: off again, and on the way back as the game had it just then.
        if (_depth != null && _depthChanged && _depth.enabled.GetValue<bool>())
        {
            _depthWas = true;
            _depth.enabled.Override(false);
        }

        if (_volume == null)
        {
            return;
        }

        var target = Mathf.InverseLerp(ThinFrom, ThinFull, clearance);
        _thin = Mathf.Lerp(_thin, target, 1f - Mathf.Exp(-dt / Ease));
        _volume.weight = _thin;
    }

    public void End()
    {
        Try("haze", () =>
        {
            if (_volume != null)
            {
                _volume.weight = 0f;
                _volume.enabled = false;
            }
        });
        Try("depth of field", () =>
        {
            if (_depth != null && _depthChanged)
            {
                _depth.enabled.Override(_depthWas);
                _depthChanged = false;
            }
        });
        foreach (var quad in _quads)
        {
            if (quad != null)
            {
                quad.enabled = true;
            }
        }

        _quads.Clear();
        foreach (var (transform, scale) in _hud)
        {
            if (transform != null)
            {
                transform.localScale = scale;
            }
        }

        _hud.Clear();
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Mod.Log.Warning(what + " stays as it is: " + e.Message);
        }
    }

    private void DepthOff(Camera camera)
    {
        _depth = null;
        _depthChanged = false;
        var volume = camera.transform.Find("DefaultPostProcessing")?.GetComponent<PostProcessVolume>();
        var profile = volume == null ? null : volume.sharedProfile;
        if (profile == null)
        {
            return;
        }

        foreach (var setting in profile.settings)
        {
            var depth = setting?.TryCast<DepthOfField>();
            if (depth != null)
            {
                _depth = depth;
                _depthWas = depth.enabled.GetValue<bool>();
                depth.enabled.Override(false);
                _depthChanged = true;
                return;
            }
        }
    }

    private void HideQuads()
    {
        var background = LayerMask.NameToLayer("Background");
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
        {
            if (renderer.enabled && renderer.gameObject.layer == background && renderer.name.StartsWith("Quad"))
            {
                renderer.enabled = false;
                _quads.Add(renderer);
            }
        }
    }

    /// <summary>Scaled away, not switched off: other mods and the game take an active PlayScreen to mean there's no menu.</summary>
    private void HideHud()
    {
        var wrapper = GameObject.Find("Wrapper");
        if (wrapper == null)
        {
            return;
        }

        for (var i = 0; i < wrapper.transform.childCount; i++)
        {
            var child = wrapper.transform.GetChild(i);
            if (child.name.StartsWith("PlayScreen") || child.name.StartsWith("BlackBorder"))
            {
                _hud.Add((child, child.localScale));
                child.localScale = Vector3.zero;
            }
        }
    }

    /// <summary>A volume of our own over the game's, whose weight is how thin the haze is. It overrides only the fog, so the route's colours go on working.</summary>
    private void MakeHaze(Camera camera)
    {
        if (_volume == null)
        {
            var game = camera.transform.Find("DefaultPostProcessing");
            var host = new GameObject("LMDDrone_Haze") { layer = game != null ? game.gameObject.layer : 0 };
            host.transform.SetParent(camera.transform, false);
            _volume = host.AddComponent<PostProcessVolume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;
            _profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            var light = _profile.AddSettings(Il2CppType.Of<EnvironmentLight>()).Cast<EnvironmentLight>();
            var from = GameLight(game);
            var fog = from == null ? 1f : from.fogIntensity.GetValue<float>();
            var distance = from == null ? 100f : from.fogDistance.GetValue<float>();
            var height = from == null ? 1f : from.heightFogIntensity.GetValue<float>();
            light.fogIntensity.Override(fog * FogLeft);
            light.fogDistance.Override(distance * FogDistanceTimes);
            light.heightFogIntensity.Override(height * HeightFogLeft);
            _volume.sharedProfile = _profile;
        }

        _volume.enabled = true;
        _volume.weight = 0f;
    }

    private static EnvironmentLight GameLight(Transform game)
    {
        var profile = game == null ? null : game.GetComponent<PostProcessVolume>()?.sharedProfile;
        if (profile == null)
        {
            return null;
        }

        foreach (var setting in profile.settings)
        {
            var light = setting?.TryCast<EnvironmentLight>();
            if (light != null)
            {
                return light;
            }
        }

        return null;
    }
}
