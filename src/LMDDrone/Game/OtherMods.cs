using System.Reflection;
using MelonLoader;

namespace LMDDrone.Game;

/// <summary>
/// Sets the other mods that read the pad or move the camera aside during a flight, the way LMD VR does it: by melon name and reflection, with no compile-time reference, so this
/// mod runs the same without them. Any failure means that mod just isn't set aside, and is said in the log.
/// </summary>
internal static class OtherMods
{
    private static object _altCameraState;
    private static MethodInfo _altCameraSuspend;
    private static MethodInfo _altCameraResume;
    private static PropertyInfo _altCameraSuspended;
    private static PropertyInfo _freestylerSetAside;
    private static bool _resolved;
    private static bool _pausedByUs;

    /// <summary>Something else has put Alt Camera aside: that's LMD VR with the headset on.</summary>
    public static bool HeadsetOn
    {
        get
        {
            Resolve();
            try
            {
                return !_pausedByUs && _altCameraSuspended != null && (bool)_altCameraSuspended.GetValue(_altCameraState);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Freestyler reads the pad itself, from its first press of a stick.</summary>
    public static void HoldFreestyler(bool hold)
    {
        Resolve();
        try
        {
            _freestylerSetAside?.SetValue(null, hold);
        }
        catch (Exception e)
        {
            Mod.Log.Warning("LMD Freestyler can't be set aside: " + e.GetBaseException().Message);
        }
    }

    public static void PauseCamera(bool pause)
    {
        Resolve();
        _pausedByUs = pause;
        try
        {
            (pause ? _altCameraSuspend : _altCameraResume)?.Invoke(_altCameraState, null);
        }
        catch (Exception e)
        {
            Mod.Log.Warning("LMD Alt Camera can't be set aside: " + e.GetBaseException().Message);
        }
    }

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        try
        {
            var camera = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name.StartsWith("Alternative Camera") || m.Info.Name == "LMD Alt Camera");
            if (camera != null)
            {
                _altCameraState = camera.GetType().GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(camera);
                _altCameraSuspend = _altCameraState?.GetType().GetMethod("SuspendOperation");
                _altCameraResume = _altCameraState?.GetType().GetMethod("ResumeOperation");
                _altCameraSuspended = _altCameraState?.GetType().GetProperty("Suspended");
                if (_altCameraSuspend == null || _altCameraResume == null)
                {
                    Mod.Log.Warning("Found LMD Alt Camera, but it can't be set aside.");
                }
            }

            var freestyler = MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == "LMD Freestyler");
            if (freestyler != null)
            {
                _freestylerSetAside = freestyler.GetType().Assembly.GetType("LMDFreestyler.Game.GameState")?.GetProperty("SetAside", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_freestylerSetAside == null)
                {
                    Mod.Log.Warning("Found LMD Freestyler, but it can't be set aside: it needs its latest version.");
                }
            }
        }
        catch (Exception e)
        {
            Mod.Log.Warning("Couldn't look for the other mods: " + e.GetBaseException().Message);
        }
    }
}
