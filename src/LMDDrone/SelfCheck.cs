using System.Text;
using LMDDrone.Audio;
using LMDDrone.Controls;
using LMDDrone.Flight;
using UnityEngine;

namespace LMDDrone;

/// <summary>The flying's sums, against what they promise. Pure logic only: it runs on demand and touches no game state.</summary>
internal static class SelfCheck
{
    public static string Run()
    {
        var b = new StringBuilder();
        var passed = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok)
            {
                passed++;
            }
            else
            {
                b.AppendLine("FAILED " + name + " " + detail);
            }
        }

        // The stick's curve: the ends and the middle.
        Check("rate at full", Mathf.Approximately(FlightMaths.Rate(1f, 600f, 0.6f), 600f));
        Check("rate at nothing", FlightMaths.Rate(0f, 600f, 0.6f) == 0f);
        Check("rate is even", Mathf.Approximately(FlightMaths.Rate(-0.5f, 600f, 0.6f), -FlightMaths.Rate(0.5f, 600f, 0.6f)));
        Check("expo is calm in the middle", FlightMaths.Rate(0.5f, 600f, 0.6f) < 600f * 0.5f);
        Check("no expo is a line", Mathf.Approximately(FlightMaths.Rate(0.5f, 600f, 0f), 300f));

        // Throttle.
        Check("centre is hover", Mathf.Approximately(FlightMaths.Throttle(0f, 0.2f, true), 0.2f));
        Check("up is full", Mathf.Approximately(FlightMaths.Throttle(1f, 0.2f, true), 1f));
        Check("down is none", FlightMaths.Throttle(-1f, 0.2f, true) == 0f);
        Check("a radio's centre is half", Mathf.Approximately(FlightMaths.Throttle(0f, 0.2f, false), 0.5f));

        // Top speed: full thrust held at the tilt that holds height, from standstill, with the drag it asks for.
        var thrust = 5f;
        var gravity = 9.81f;
        var drag = FlightMaths.DragFor(thrust, 35f);
        var across = Mathf.Sqrt((thrust * gravity) * (thrust * gravity) - gravity * gravity);
        var speed = 0f;
        for (var i = 0; i < 240 * 20; i++)
        {
            speed += (across - drag * speed * speed) / 240f;
        }

        Check("top speed", Mathf.Abs(speed - 35f) < 0.5f, speed.ToString("0.0"));

        // Smoothing: gets there at any step, without passing it, and a change of step doesn't change where.
        foreach (var dt in new[] { 1f / 240f, 1f / 60f, 1f / 30f })
        {
            var value = 0f;
            var rate = 0f;
            var most = 0f;
            for (var t = 0f; t < 3f; t += dt)
            {
                value = FlightMaths.Smooth(value, 1f, ref rate, 0.2f, dt);
                most = Mathf.Max(most, value);
            }

            Check("smooth arrives", Mathf.Abs(value - 1f) < 0.001f, value.ToString());
            Check("smooth doesn't overshoot", most < 1.001f, most.ToString());
        }

        // Rotation stays a true rotation, and a full turn comes back.
        var attitude = Quaternion.identity;
        for (var i = 0; i < 240; i++)
        {
            attitude = FlightMaths.Turn(attitude, new Vector3(360f, 0f, 0f), 1f / 240f);
        }

        Check("a second at 360 is a full turn", Quaternion.Angle(attitude, Quaternion.identity) < 0.5f, attitude.ToString());
        attitude = Quaternion.identity;
        for (var i = 0; i < 240 * 60; i++)
        {
            attitude = FlightMaths.Turn(attitude, new Vector3(437f, 251f, -611f), 1f / 240f);
        }

        Check("a minute of tumbling stays unit", Mathf.Abs(Quaternion.Dot(attitude, attitude) - 1f) < 0.001f);

        // Body axes: what the sticks say is what the nose does.
        var up = FlightMaths.Turn(Quaternion.identity, new Vector3(90f, 0f, 0f), 1f) * Vector3.forward;
        Check("+x tips the nose down", up.y < -0.9f, up.ToString());

        // Camera tilt: a tap is one step, a 1.5 s hold sweeps the range, home eases back and lands.
        var tilt = new CameraTilt();
        tilt.Begin(0f, 0f);
        tilt.Step(1f, 1f / 60f);
        tilt.Step(0f, 1f / 60f);
        Check("tilt tap is one step", Mathf.Approximately(tilt.Target, 10f), tilt.Target.ToString());
        tilt.Begin(CameraTilt.Lowest, 0f);
        for (var t = 0f; t < 1.5f; t += 1f / 240f)
        {
            tilt.Step(1f, 1f / 240f);
        }

        Check("tilt hold sweeps the range", tilt.Target == CameraTilt.Highest, tilt.Target.ToString());
        tilt.Home();
        for (var t = 0f; t < 3f; t += 1f / 60f)
        {
            tilt.Step(0f, 1f / 60f);
        }

        Check("tilt home lands", tilt.Shown == 0f, tilt.Shown.ToString());

        // Landing: full down on the ground disarms, the same in the air doesn't, above centre arms.
        var quad = new AdvancedFlight();
        for (var t = 0f; t < 1f; t += 1f / 240f)
        {
            quad.Arm(-1f, 5f, 1f / 240f);
        }

        Check("a dive stays armed", quad.Armed);
        for (var t = 0f; t < 0.6f; t += 1f / 240f)
        {
            quad.Arm(-1f, 0.2f, 1f / 240f);
        }

        Check("landed disarms", !quad.Armed);
        quad.Arm(-0.5f, 0.2f, 1f / 240f);
        Check("stays off below centre", !quad.Armed);
        quad.Arm(0.3f, 0.2f, 1f / 240f);
        Check("above centre arms", quad.Armed);

        // The limits.
        var limits = new Limits(new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f)));
        Check("nothing inside", limits.Push(Vector3.zero, out var past) == Vector3.zero && past == 0f);
        var push = limits.Push(new Vector3(50f + Limits.HardMargin, 0f, 0f), out past);
        Check("pushed back from the hard edge", push.x < 0f && past == 1f, push.ToString());
        limits.Push(new Vector3(50f + Limits.SoftMargin * 0.5f, 0f, 0f), out past);
        Check("free inside the soft margin", past == 0f, past.ToString());
        Check("clamped", limits.Clamp(new Vector3(9999f, 9999f, -9999f)).x == 50f + Limits.HardMargin);

        // The stick's dead zone.
        Check("in the dead zone", Gamepad.Shape(new Vector2(0.05f, 0f), 0.1f) == Vector2.zero);
        Check("full stays full", Mathf.Approximately(Gamepad.Shape(Vector2.up, 0.1f).y, 1f));

        // The sticks: which one does what, in the two layouts.
        var normal = PilotInput.FromSticks(new Vector2(1f, 0.5f), new Vector2(0.25f, -1f), StickLayout.Normal);
        Check("Normal: left climbs and turns", normal.Turn == 1f && normal.Climb == 0.5f);
        Check("Normal: right flies", normal.Side == 0.25f && normal.Forward == -1f);
        var alternate = PilotInput.FromSticks(new Vector2(1f, 0.5f), new Vector2(0.25f, -1f), StickLayout.Alternate);
        Check("Alternate: left flies", alternate.Side == 1f && alternate.Forward == 0.5f);
        Check("Alternate: right climbs and turns", alternate.Turn == 0.25f && alternate.Climb == -1f);

        // The sound: in range, with no step where the loop joins up, and no peak held flat against the top.
        foreach (var (name, loop) in new[] { ("motor", MotorSound.Motor()), ("wind", MotorSound.Wind()) })
        {
            Check(name + " sound in range", loop.All(s => Mathf.Abs(s) <= 1f));
            var biggest = 0f;
            for (var i = 1; i < loop.Length; i++)
            {
                biggest = Mathf.Max(biggest, Mathf.Abs(loop[i] - loop[i - 1]));
            }

            Check(name + " sound joins up", Mathf.Abs(loop[0] - loop[^1]) <= biggest * 1.5f, Mathf.Abs(loop[0] - loop[^1]) + " against " + biggest);
            Check(name + " sound isn't clipped", loop.Max(Mathf.Abs) < 0.98f, loop.Max(Mathf.Abs).ToString());
        }

        return passed + " passed\n" + b;
    }
}
