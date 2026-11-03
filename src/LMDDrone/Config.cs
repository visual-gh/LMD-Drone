using LMDDrone.Controls;
using LMDDrone.Flight;
using MelonLoader;
using MelonLoader.Preferences;
using MelonLoader.Utils;

namespace LMDDrone;

/// <summary>The mod's settings, in UserData\LMDDrone.cfg. F11 reloads them.</summary>
internal static class Config
{
    private static MelonPreferences_Category _category;
    private static MelonPreferences_Entry<bool> _enabled;
    private static MelonPreferences_Entry<FlightMode> _flightMode;
    private static MelonPreferences_Entry<StickLayout> _stickLayout;
    private static MelonPreferences_Entry<AltitudeFrom> _altitudeFrom;
    private static MelonPreferences_Entry<bool> _osd;
    private static MelonPreferences_Entry<float> _volume;
    private static MelonPreferences_Entry<bool> _invertTilt;
    private static MelonPreferences_Entry<float> _simpleSpeed;
    private static MelonPreferences_Entry<float> _simpleFov;
    private static MelonPreferences_Entry<float> _advancedRate;
    private static MelonPreferences_Entry<float> _advancedExpo;
    private static MelonPreferences_Entry<float> _advancedUptilt;
    private static MelonPreferences_Entry<float> _advancedFov;
    private static MelonPreferences_Entry<float> _advancedYawRate;
    private static MelonPreferences_Entry<float> _advancedThrust;
    private static MelonPreferences_Entry<float> _advancedTopSpeed;
    private static MelonPreferences_Entry<bool> _advancedCrashes;
    private static MelonPreferences_Entry<bool> _advancedPropsInView;
    private static MelonPreferences_Entry<bool> _throttleCentre;
    private static MelonPreferences_Entry<float> _deadzone;

    public static bool Enabled => _enabled.Value;

    public static FlightMode FlightMode
    {
        get => _flightMode.Value;
        set => _flightMode.Value = value;
    }

    public static StickLayout StickLayout => _stickLayout.Value;

    public static bool Osd
    {
        get => _osd.Value;
        set => _osd.Value = value;
    }

    public static AltitudeFrom AltitudeFrom => _altitudeFrom.Value;

    public static float Volume => _volume.Value;
    public static bool InvertTilt => _invertTilt.Value;
    public static float SimpleSpeed => _simpleSpeed.Value;
    public static float SimpleFov => _simpleFov.Value;
    public static float AdvancedRate => _advancedRate.Value;
    public static float AdvancedExpo => _advancedExpo.Value;
    public static float AdvancedUptilt => _advancedUptilt.Value;
    public static float AdvancedFov => _advancedFov.Value;
    public static float AdvancedYawRate => _advancedYawRate.Value;
    public static float AdvancedThrust => _advancedThrust.Value;
    public static float AdvancedTopSpeed => _advancedTopSpeed.Value;
    public static bool AdvancedCrashes => _advancedCrashes.Value;
    public static bool AdvancedPropsInView => _advancedPropsInView.Value;
    public static bool ThrottleCentre => _throttleCentre.Value;
    public static float Deadzone => _deadzone.Value;

    public static void Init()
    {
        _category = MelonPreferences.CreateCategory("LMDDrone", "LMD Drone");
        _category.SetFilePath(Path.Combine(MelonEnvironment.UserDataDirectory, "LMDDrone.cfg"));
        _enabled = _category.CreateEntry("Enabled", true, description: "The mod on or off.");
        _flightMode = _category.CreateEntry("FlightMode", FlightMode.Simple, "Flight Mode", "Simple or Advanced.");
        _stickLayout = _category.CreateEntry("StickLayout", StickLayout.Normal, "Stick Layout", "Normal: left stick climbs and turns, right stick flies. Alternate: left stick flies, right stick climbs and turns.");
        _osd = _category.CreateEntry("Osd", true, "On-screen Readout", "Height, speed, mode and a pointer back to the bike.");
        _altitudeFrom = _category.CreateEntry("AltitudeFrom", AltitudeFrom.Camp, "Altitude From", "What the readout's height is measured from: the mountain's finish camp, or the bike.");
        _volume = _category.CreateEntry("Volume", 0.5f, "Motor Volume", "Of the drone's motors, 0 to 1.", validator: new ValueRange<float>(0f, 1f));
        _invertTilt = _category.CreateEntry("InvertTilt", false, "Invert Camera Tilt", "Swaps what the D-pad's up and down do to the camera's tilt.");
        _simpleSpeed = _category.CreateEntry("SimpleSpeed", 14f, "Speed", "Simple: cruising speed in metres per second.", validator: new ValueRange<float>(6f, 20f));
        _simpleFov = _category.CreateEntry("SimpleFov", 80f, "Field of View", "Simple: field of view.", validator: new ValueRange<float>(50f, 110f));
        _advancedRate = _category.CreateEntry("AdvancedRate", 600f, "Rate", "Advanced: top pitch and roll rate, degrees per second.", validator: new ValueRange<float>(200f, 1000f));
        _advancedExpo = _category.CreateEntry("AdvancedExpo", 0.6f, "Expo", "Advanced: 0 is a straight line from the stick to the rate. Higher is calmer around the centre.", validator: new ValueRange<float>(0f, 0.9f), is_hidden: true);
        _advancedUptilt = _category.CreateEntry("AdvancedUptilt", 25f, "Camera Uptilt", "Advanced: degrees the camera sits above the quad's nose. More is for flying faster.", validator: new ValueRange<float>(0f, 45f));
        _advancedFov = _category.CreateEntry("AdvancedFov", 110f, "Field of View", "Advanced: field of view.", validator: new ValueRange<float>(70f, 130f));
        _advancedYawRate = _category.CreateEntry("AdvancedYawRate", 360f, description: "Advanced: top yaw rate, degrees per second.", is_hidden: true);
        _advancedThrust = _category.CreateEntry("AdvancedThrust", 5f, description: "Advanced: full throttle as a multiple of the quad's weight.", is_hidden: true);
        _advancedTopSpeed = _category.CreateEntry("AdvancedTopSpeed", 35f, description: "Advanced: top speed at full throttle in metres per second. Sets the drag.", is_hidden: true);
        _advancedCrashes = _category.CreateEntry("AdvancedCrashes", true, description: "Advanced: a hard hit ends the flight. Off, it only bounces.", is_hidden: true);
        _advancedPropsInView = _category.CreateEntry("AdvancedPropsInView", false, description: "Advanced: the front props at the top of the picture, as on a real FPV camera.", is_hidden: true);
        _throttleCentre = _category.CreateEntry("ThrottleCentre", true, description: "Advanced: the left stick's centre is hover. Off, its centre is half thrust and the bottom is none, as on a real radio.", is_hidden: true);
        _deadzone = _category.CreateEntry("Deadzone", 0.1f, description: "Stick travel that is ignored. Raise it if the drone drifts.", is_hidden: true);
        _category.SaveToFile(false);
    }

    public static void Reload() => _category.LoadFromFile(false);

    public static void Save() => _category.SaveToFile(false);
}
