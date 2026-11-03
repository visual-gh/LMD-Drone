using System.Runtime.InteropServices;
using UnityEngine;

namespace LMDDrone.Controls;

/// <summary>Reads Xbox-style controllers straight from XInput, as LMD VR and LMD Alt Camera do. Merges all connected ones.</summary>
internal static class Gamepad
{
    public const ushort DpadUp = 0x0001;
    public const ushort DpadDown = 0x0002;
    public const ushort DpadLeft = 0x0004;
    public const ushort DpadRight = 0x0008;
    public const ushort View = 0x0020;
    public const ushort RightThumb = 0x0080;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;

    private static ushort _previous;
    private static ushort _current;
    private static bool _unavailable;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    /// <summary>-1..1 per axis (right and up positive), with a round dead zone.</summary>
    public static Vector2 LeftStick { get; private set; }

    public static Vector2 RightStick { get; private set; }

    /// <summary>0..1.</summary>
    public static float LeftTrigger { get; private set; }

    public static float RightTrigger { get; private set; }

    /// <summary>Call once per frame.</summary>
    public static void Poll()
    {
        _previous = _current;
        _current = 0;
        var left = Vector2.zero;
        var right = Vector2.zero;
        byte leftTrigger = 0;
        byte rightTrigger = 0;
        if (!_unavailable)
        {
            try
            {
                for (uint i = 0; i < 4; i++)
                {
                    if (XInputGetState(i, out var state) == 0)
                    {
                        _current |= state.Buttons;
                        leftTrigger = Math.Max(leftTrigger, state.LeftTrigger);
                        rightTrigger = Math.Max(rightTrigger, state.RightTrigger);
                        var l = new Vector2(state.ThumbLX, state.ThumbLY) / 32767f;
                        var r = new Vector2(state.ThumbRX, state.ThumbRY) / 32767f;
                        if (l.sqrMagnitude > left.sqrMagnitude)
                        {
                            left = l;
                        }

                        if (r.sqrMagnitude > right.sqrMagnitude)
                        {
                            right = r;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _unavailable = true;
                Mod.Log.Warning("XInput not available, gamepad controls disabled");
            }
        }

        LeftStick = Shape(left, Config.Deadzone);
        RightStick = Shape(right, Config.Deadzone);
        LeftTrigger = leftTrigger / 255f;
        RightTrigger = rightTrigger / 255f;
    }

    /// <summary>Round dead zone, rescaled so the stick still reaches 0..1, and diagonals kept inside the unit circle.</summary>
    public static Vector2 Shape(Vector2 raw, float deadzone)
    {
        var magnitude = Mathf.Min(1f, raw.magnitude);
        return magnitude <= deadzone ? Vector2.zero : raw.normalized * ((magnitude - deadzone) / (1f - deadzone));
    }

    public static bool Held(ushort button) => (_current & button) != 0;

    public static bool Pressed(ushort button) => (_current & button) != 0 && (_previous & button) == 0;
}
