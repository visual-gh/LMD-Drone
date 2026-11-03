using LMDDrone.Controls;
using LMDDrone.Flight;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LMDDrone.Mod), "LMD Drone", LMDDrone.Mod.Version, "Visual")]
[assembly: MelonGame("Megagon Industries", "Lonely Mountains: Downhill")]

namespace LMDDrone;

public class Mod : MelonMod
{
    public const string Version = "1.0.0";

    internal static MelonLogger.Instance Log { get; private set; }

    internal static Flyer Flyer { get; private set; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Config.Init();
        Flyer = new Flyer();
        Log.Msg("LMD Drone " + Version + ": stopped on a trail, Y launches. Settings: UserData\\LMDDrone.cfg");
    }

    public override void OnUpdate()
    {
        Gamepad.Poll();
        if (Input.GetKeyDown(KeyCode.F11))
        {
            Config.Reload();
        }

        Flyer.Update();
    }

    public override void OnLateUpdate() => Flyer.LateUpdate();
}
