using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Tracks;
using Il2CppMegagon.Downhill.Vehicle;
using Il2CppMegagon.Downhill.Vehicle.Controller;
using UnityEngine;

namespace LMDDrone.Game;

/// <summary>The game state this mod reads. The same mapping as LMD Alt Camera's and LMD Freestyler's, all of it seen live.</summary>
internal static class GameState
{
    private const int RidingState = 100;

    /// <summary>Seconds between searches on a level that shows no resting place yet: doubling, so a level with none doesn't pay for it all ride.</summary>
    private const float RestLookGap = 2f;
    private const float RestLookGapMost = 64f;

    private static TrackHiddenLocation[] _restPlaces;
    private static float _lookForRestAt;
    private static float _restLookGap = RestLookGap;
    private static Transform _wrapper;

    /// <summary>GameStateManager's state: 10 menu, 100 riding.</summary>
    public static bool Riding
    {
        get
        {
            try
            {
                return (int)GameStateManager.prop_EnumNPublicSealedva5vUnique_0 == RidingState;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>A screen other than the HUD is up: any active child of the UI's "Wrapper" that isn't one of those always there. A break's screen is the HUD of a break.</summary>
    public static bool MenuOpen
    {
        get
        {
            if (_wrapper == null)
            {
                var found = GameObject.Find("Wrapper");
                if (found == null)
                {
                    return false;
                }

                _wrapper = found.transform;
            }

            for (var i = 0; i < _wrapper.childCount; i++)
            {
                var child = _wrapper.GetChild(i).gameObject;
                if (child.activeSelf && !child.name.StartsWith("BlackBorder") && !child.name.StartsWith("SplashScreen") && !child.name.StartsWith("PlayScreen")
                    && !(child.name.StartsWith("CutsceneScreen") && Resting))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A new bike is a new level: look for its resting places afresh.</summary>
    public static void NewBike()
    {
        _restPlaces = null;
        _restLookGap = RestLookGap;
    }

    /// <summary>"Take a break" at a resting place: TrackHiddenLocation.field_Private_Boolean_0. The game has its own camera there.</summary>
    public static bool Resting
    {
        get
        {
            if (_restPlaces == null || (_restPlaces.Length == 0 && Time.unscaledTime > _lookForRestAt) || _restPlaces.Any(r => r == null))
            {
                _restPlaces = Resources.FindObjectsOfTypeAll<TrackHiddenLocation>().Where(r => r.gameObject.scene.IsValid()).ToArray();
                _lookForRestAt = Time.unscaledTime + _restLookGap;
                _restLookGap = Mathf.Min(_restLookGap * 2f, RestLookGapMost);
            }

            return _restPlaces.Any(r => r.field_Private_Boolean_0);
        }
    }

    /// <summary>Inside a resting place's area (its trigger): the place's Y is the game's there, also after a break, until the bike has left it.</summary>
    public static bool NearRest(Vector3 position)
    {
        if (_restPlaces == null)
        {
            return false;
        }

        foreach (var place in _restPlaces.Where(p => p != null))
        {
            foreach (var area in place.GetComponentsInChildren<Collider>(true))
            {
                if (area.isTrigger && area.bounds.Contains(position))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>field_Private_Boolean_2, or a reason in prop_CrashReason_0. Some crashes only set one of them.</summary>
    public static bool Crashed(BikeLocomotion bike) => bike.field_Private_Boolean_2 || bike.prop_CrashReason_0 != CrashReason.NoCrash;

    /// <summary>field_Private_Single_8: speed in metres per second.</summary>
    public static float Speed(BikeLocomotion bike) => bike.field_Private_Single_8;

    /// <summary>field_Private_Boolean_1: no wheel on the ground.</summary>
    public static bool Airborne(BikeLocomotion bike) => bike.field_Private_Boolean_1;

    /// <summary>field_Private_Single_5: heading in degrees.</summary>
    public static float Heading(BikeLocomotion bike) => bike.field_Private_Single_5;
}
