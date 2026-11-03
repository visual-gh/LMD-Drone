using UnityEngine;

namespace LMDDrone.Controls;

/// <summary>Which stick does what.</summary>
internal enum StickLayout
{
    /// <summary>Left stick climbs and turns, right stick flies.</summary>
    Normal,

    /// <summary>Left stick flies, right stick climbs and turns: closest to moving and looking in a game.</summary>
    Alternate,
}

/// <summary>What the pilot is asking for this frame, from the pad and the keyboard: the same for both flight modes, which read it their own way.</summary>
internal struct PilotInput
{
    /// <summary>-1..1, yaw.</summary>
    public float Turn;

    /// <summary>-1..1, climb or throttle.</summary>
    public float Climb;

    /// <summary>-1..1, sideways or roll.</summary>
    public float Side;

    /// <summary>-1..1, forward or pitch.</summary>
    public float Forward;

    /// <summary>0..1, Simple's faster.</summary>
    public float Fast;

    /// <summary>0..1, Simple's cine.</summary>
    public float Slow;

    /// <summary>-1..1, the camera's tilt.</summary>
    public float Tilt;

    public static PilotInput Read()
    {
        var input = FromSticks(Gamepad.LeftStick, Gamepad.RightStick, Config.StickLayout);
        input.Fast = Gamepad.RightTrigger;
        input.Slow = Gamepad.LeftTrigger;
        input.Tilt = (Gamepad.Held(Gamepad.DpadUp) ? 1f : 0f) - (Gamepad.Held(Gamepad.DpadDown) ? 1f : 0f);

        input.Turn = Key(input.Turn, KeyCode.E, KeyCode.Q);
        input.Climb = Key(input.Climb, KeyCode.Space, KeyCode.LeftShift);
        input.Side = Key(input.Side, KeyCode.D, KeyCode.A);
        input.Forward = Key(input.Forward, KeyCode.W, KeyCode.S);
        input.Tilt = Key(input.Tilt, KeyCode.R, KeyCode.F);
        if (Input.GetKey(KeyCode.C))
        {
            input.Fast = 1f;
        }

        if (Input.GetKey(KeyCode.V))
        {
            input.Slow = 1f;
        }

        return input;
    }

    public static PilotInput FromSticks(Vector2 left, Vector2 right, StickLayout layout)
    {
        var (climbTurn, flight) = layout == StickLayout.Alternate ? (right, left) : (left, right);
        return new PilotInput { Turn = climbTurn.x, Climb = climbTurn.y, Side = flight.x, Forward = flight.y };
    }

    private static float Key(float value, KeyCode plus, KeyCode minus)
    {
        var keys = (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f);
        return keys != 0f ? keys : value;
    }
}
