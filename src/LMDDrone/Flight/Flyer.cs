using Il2CppInterop.Runtime;
using Il2CppMegagon.Downhill.Cameras;
using Il2CppMegagon.Downhill.Vehicle.Controller;
using LMDDrone.Audio;
using LMDDrone.Controls;
using LMDDrone.Game;
using LMDDrone.Visual;
using UnityEngine;

namespace LMDDrone.Flight;

internal enum Phase
{
    Grounded,

    /// <summary>Seen from the game's camera, rising off the rider. The bike is held.</summary>
    Lifting,

    /// <summary>The feed cutting in: the camera changes over behind the static.</summary>
    CuttingIn,

    Flying,

    /// <summary>Motors off, falling, the picture going.</summary>
    Crashing,

    /// <summary>The feed cutting out, on the way back.</summary>
    CuttingOut,

    /// <summary>The game's camera is back at the bike, with the drone coming down, and the bike waits for the ground under it.</summary>
    Returning,
}

/// <summary>Launch, the flight and the way back: the one place that knows what's taken from the game and gives it all back.</summary>
internal sealed class Flyer
{
    private const float StoppedSpeed = 0.5f;
    private const float StoppedFor = 0.5f;

    /// <summary>Physics steps per second. A step this short keeps a flip true at any frame rate.</summary>
    private const float StepRate = 240f;
    private const int MostSteps = 16;

    private const float LiftSeconds = 0.8f;
    private const float CutSeconds = 0.2f;
    private const float CrashSeconds = 0.6f;
    private const float ReturnSeconds = 0.5f;

    /// <summary>Static's full stays this long after the game's camera is back: it answers on physics steps, and takes two to settle.</summary>
    private const float StaticHeldSeconds = 0.05f;

    /// <summary>Where the rider carries it, from the bike's origin, and how far above it the lift goes.</summary>
    private const float BackHeight = 1.45f;
    private const float BackBehind = 0.25f;
    private const float LiftHeight = 5f;

    /// <summary>Where the picture starts: up and behind, looking back at the rider.</summary>
    private const float StartHeight = 6f;
    private const float StartBack = 5f;
    private const float StartLookDown = 35f;
    private const float StartClimb = 2f;

    /// <summary>The model is hard to read at the game camera's distance at its real size. Only in the shots from outside.</summary>
    private const float ShownScale = 2.5f;

    /// <summary>The shadow, over life size: under the sun's soft edge a thing this small at life size leaves nothing to see (measured: nothing at 1.5, a soft patch at 3).</summary>
    private const float ShadowScale = 3f;

    /// <summary>Frames the game's camera gets to bring the ground back, as a floor: it came in within 30 ms in every test.</summary>
    private const int ReturnFrames = 3;
    private const int ReturnFramesMost = 60;

    /// <summary>Frames for the mods set aside to have let go of the camera, which they do in their own late update.</summary>
    private const int HandOverFrames = 2;

    /// <summary>A hit harder than this ends an Advanced flight.</summary>
    private const float CrashSpeed = 8f;
    private const float AdvancedBounce = 0.35f;

    /// <summary>On its back this close to the ground and still this long, an Advanced quad is put upright.</summary>
    private const float UpsideDownClearance = 0.5f;
    private const float UpsideDownSeconds = 1.5f;

    /// <summary>How far ahead the drone looks for ground, for the haze and the ground effect.</summary>
    private const float GroundReach = 70f;

    private const float CutStaticPeak = 1f;
    private const float LimitStaticMost = 0.35f;

    /// <summary>The frame from the camera in Advanced: the camera up front, between and under the front props, as on a real quad.</summary>
    private static readonly Vector3 FrameFromCamera = new(0f, -0.012f, -0.125f);

    private readonly SimpleFlight _simple = new();
    private readonly AdvancedFlight _advanced = new();
    private readonly Picture _picture = new();
    private readonly Feed _feed = new();
    private readonly DroneModel _model = new();
    private readonly Motors _motors = new();

    private PlayCamera _play;
    private Camera _camera;
    private BikeLocomotion _bike;
    private Limits _limits;
    private bool _limitsKnown;
    private float _campHeight;
    private float _stoppedSince = -1f;
    private float _phaseTime;
    private float _stepsOwed;
    private int _returnFrames;
    private int _handOverFrames;
    private bool _taken;
    private bool _bikeFree;
    private float _baseFov;
    private float _baseNear;
    private Vector3 _position;
    private Vector3 _velocity;
    private Quaternion _view = Quaternion.identity;
    private Vector3 _from;
    private Vector3 _to;
    private float _clearance;
    private bool _wasPaused;
    private Camera.CameraCallback _preCull;
    private bool _preCullFailed;
    private bool _fromBreak;
    private float _upsideDownFor;

    public Phase Phase { get; private set; }

    public Vector3 Position => _position;
    public Vector3 Velocity => _velocity;
    public Quaternion View => _view;
    public FlightMode Mode => Config.FlightMode;

    /// <summary>How far into the signal limit it is, 0..1.</summary>
    public float Static { get; private set; }

    /// <summary>A launch would be taken now: a stopped bike in a ride, nothing else going on.</summary>
    public bool CanLaunch { get; private set; }

    public void Update()
    {
        if (!Find())
        {
            Abandon();
            return;
        }

        if (GameState.Riding)
        {
            Proxies.Tick(Phase == Phase.Flying);
        }

        // Pause does not stop time in this game: a screen up is the signal, and only the flight's own timers read it.
        var paused = GameState.MenuOpen;

        // The readout's toggle is for a clean picture: the hints go with it.
        Hud.ShowFlightHints(Phase == Phase.Flying && !paused && Config.Osd, _fromBreak);
        Hud.Update(Time.unscaledDeltaTime);
        if (Phase != Phase.Grounded && Phase != Phase.Returning)
        {
            Hud.HideGamePanels(true);
        }

        _motors.Pause(paused);
        if (paused)
        {
            _feed.Hide();
        }

        switch (Phase)
        {
            case Phase.Grounded:
                UpdateGrounded();
                break;
            case Phase.Lifting:
                Advance(paused, LiftSeconds, Lifted);
                break;
            case Phase.CuttingIn:
                UpdateCuttingIn(paused);
                break;
            case Phase.Flying:
                UpdateFlying(paused);
                break;
            case Phase.Crashing:
                Advance(paused, CrashSeconds, StartCutOut);
                break;
            case Phase.CuttingOut:
                Advance(paused, CutSeconds, GiveBackAndReturn);
                break;
            case Phase.Returning:
                UpdateReturning();
                break;
        }

        _wasPaused = paused;
    }

    public void LateUpdate()
    {
        switch (Phase)
        {
            case Phase.Lifting:
                _model.Place(Vector3.Lerp(_from, _to, Eased(_phaseTime / LiftSeconds)), Quaternion.Euler(0f, GameState.Heading(_bike), 0f), ShownScale);
                _model.Spin(Mathf.Lerp(0.2f, 0.7f, _phaseTime / LiftSeconds), Time.deltaTime);
                _motors.Update(_phaseTime / LiftSeconds * 0.7f, 0f, 0f, false, Time.deltaTime);
                break;
            case Phase.CuttingIn:
                _feed.Static(Mathf.Sin(Mathf.Clamp01(_phaseTime / CutSeconds) * Mathf.PI) * CutStaticPeak);
                if (_taken)
                {
                    HoldCamera();
                    Place();
                }

                break;
            case Phase.Flying:
                LateFlying();
                break;
            case Phase.Crashing:
                LateCrashing();
                break;
            case Phase.CuttingOut:
                _feed.Static(Mathf.Clamp01(_phaseTime / CutSeconds) * CutStaticPeak);
                if (_taken)
                {
                    HoldCamera();
                    Place();
                }

                break;
            case Phase.Returning:
                LateReturning();
                break;
        }
    }

    /// <summary>Back on the bike now, at once, from any phase: a restart, a quit or the mod going off.</summary>
    public void Land()
    {
        if (Phase == Phase.Grounded)
        {
            return;
        }

        if (_taken)
        {
            GiveBack();
        }

        OtherMods.HoldFreestyler(false);
        Hud.HideGamePanels(false);
        _feed.Hide();
        _model.Show(false);
        _motors.Stop();
        BikeHold.On = false;
        Phase = Phase.Grounded;
        _stoppedSince = -1f;
    }

    private void Advance(bool paused, float seconds, Action then)
    {
        if (paused)
        {
            return;
        }

        _phaseTime += Time.deltaTime;
        if (_phaseTime >= seconds)
        {
            _phaseTime = 0f;
            then();
        }
    }

    private void UpdateGrounded()
    {
        CanLaunch = false;
        Hud.NoteRide(GameState.Crashed(_bike), GameState.Speed(_bike));
        // At a resting place, the drone goes from the break; around it, before or after one, Y and the place are the game's.
        var resting = GameState.Resting;
        if (!Config.Enabled || !GameState.Riding || GameState.MenuOpen || GameState.Crashed(_bike) || GameState.Airborne(_bike) || GameState.Speed(_bike) >= StoppedSpeed || Hud.FinishShowing || OtherMods.HeadsetOn
            || (!resting && GameState.NearRest(_bike.transform.position)))
        {
            _stoppedSince = -1f;
            Hud.ShowHint(false);
            return;
        }

        if (_stoppedSince < 0f)
        {
            _stoppedSince = Time.time;
        }

        CanLaunch = Time.time - _stoppedSince >= StoppedFor;
        Hud.ShowHint(Time.time - _stoppedSince >= Hud.HintAfter && (Hud.Ridden || resting), resting);
        if (CanLaunch && Hud.LaunchPressed)
        {
            Launch();
        }
    }

    private void UpdateCuttingIn(bool paused)
    {
        if (_handOverFrames > 0)
        {
            _handOverFrames--;
            if (_handOverFrames == 0)
            {
                Take();
            }
        }

        Advance(paused, CutSeconds, () =>
        {
            _feed.Static(0f);
            FlyOnBoard();
            Phase = Phase.Flying;
        });
    }

    private void UpdateFlying(bool paused)
    {
        if (!Config.Enabled || !GameState.Riding)
        {
            Land();
            return;
        }

        if (paused)
        {
            return;
        }

        if (Hud.BackPressed)
        {
            StartCutOut();
            return;
        }

        // Out of a break taken back by the game: there's nothing left to fly for.
        if (_fromBreak && !GameState.Resting)
        {
            StartCutOut();
            return;
        }

        if (Hud.ModePressed)
        {
            SwitchMode();
        }

        if (Gamepad.Pressed(Gamepad.View) || Input.GetKeyDown(KeyCode.H))
        {
            Config.Osd = !Config.Osd;
            Config.Save();
        }

        if (Gamepad.Pressed(Gamepad.RightThumb) || Input.GetKeyDown(KeyCode.G))
        {
            (Config.FlightMode == FlightMode.Simple ? _simple.Tilt : _advanced.Tilt).Home();
        }

        // Nothing rights a quad on its back on the ground, so it rights itself after a moment.
        var onBack = Config.FlightMode == FlightMode.Advanced && (_advanced.Attitude * Vector3.up).y < 0f && _clearance < UpsideDownClearance && _velocity.sqrMagnitude < 1f;
        _upsideDownFor = onBack ? _upsideDownFor + Time.deltaTime : 0f;
        if (_upsideDownFor >= UpsideDownSeconds)
        {
            _advanced.Upright(ref _velocity);
            _upsideDownFor = 0f;
        }
    }

    private void SwitchMode()
    {
        Config.FlightMode = Config.FlightMode == FlightMode.Simple ? FlightMode.Advanced : FlightMode.Simple;
        Config.Save();
        EnterMode();
    }

    private void EnterMode()
    {
        if (Config.FlightMode == FlightMode.Simple)
        {
            _simple.Begin(_view, 0f);
        }
        else
        {
            _advanced.Begin(_view);
        }

        FlyOnBoard();
        ApplyLens();
    }

    /// <summary>On board, the model is its shadow. Or, by the setting, the props at the top of an Advanced picture.</summary>
    private void FlyOnBoard()
    {
        var props = Config.FlightMode == FlightMode.Advanced && Config.AdvancedPropsInView;
        _model.PropsOnly(props);
        _model.ShadowOnly(!props);
        _model.Show(true);
    }

    private void UpdateReturning()
    {
        _returnFrames++;
        if (_phaseTime >= StaticHeldSeconds)
        {
            _feed.Hide();
        }

        if (!_bikeFree && _returnFrames >= ReturnFrames)
        {
            // The ground is there or a few frames away. The bike only needs its rays to find it.
            if (Physics.Raycast(_bike.transform.position + Vector3.up, Vector3.down, 3f, Collision.Mask, QueryTriggerInteraction.Ignore) || _returnFrames > ReturnFramesMost)
            {
                BikeHold.On = false;
                OtherMods.HoldFreestyler(false);
                Hud.HideGamePanels(false);
                _bikeFree = true;
            }
        }

        _phaseTime += Time.deltaTime;
        if (_bikeFree && _phaseTime >= ReturnSeconds)
        {
            _model.Show(false);
            _motors.Stop();
            Phase = Phase.Grounded;

            // It never moved: it can go again at once.
            _stoppedSince = Time.time - StoppedFor;
        }
    }

    private void Launch()
    {
        if (!_limitsKnown)
        {
            _limits = new Limits(Level.FindTerrain(_bike.transform.position));
            _campHeight = Level.CampHeight(_bike.transform.position.y);
            _limitsKnown = true;
        }

        BuildVisuals();
        Hud.ShowHint(false);
        _fromBreak = GameState.Resting;
        Hud.HideGamePanels(true);
        BikeHold.On = true;
        OtherMods.HoldFreestyler(true);
        var heading = Quaternion.Euler(0f, GameState.Heading(_bike), 0f);
        var bike = _bike.transform.position;
        _from = bike + Vector3.up * BackHeight - heading * Vector3.forward * BackBehind;
        _to = _from + Vector3.up * LiftHeight;
        _position = bike + Vector3.up * StartHeight - heading * Vector3.forward * StartBack;
        _velocity = Vector3.up * StartClimb;
        _view = heading * Quaternion.Euler(StartLookDown, 0f, 0f);
        _stepsOwed = 0f;
        _phaseTime = 0f;
        Static = 0f;

        _model.ShadowOnly(false);
        _model.PropsOnly(false);
        _model.Place(_from, heading, ShownScale);
        _model.Show(true);
        _motors.Start();
        Phase = Phase.Lifting;
    }

    private void Lifted()
    {
        OtherMods.PauseCamera(true);
        _handOverFrames = HandOverFrames;
        Phase = Phase.CuttingIn;
    }

    private void StartCutOut()
    {
        _phaseTime = 0f;
        Phase = Phase.CuttingOut;
    }

    private void GiveBackAndReturn()
    {
        GiveBack();

        // The game's camera takes a frame or two to find its feet: they go by behind the static, at its full.
        _feed.Static(CutStaticPeak);
        var bike = _bike.transform.position;
        var heading = Quaternion.Euler(0f, GameState.Heading(_bike), 0f);
        _to = bike + Vector3.up * BackHeight - heading * Vector3.forward * BackBehind;
        _from = _to + Vector3.up * LiftHeight;
        _model.ShadowOnly(false);
        _model.PropsOnly(false);
        _model.Place(_from, heading, ShownScale);
        _model.Show(true);
        _phaseTime = 0f;
        _returnFrames = 0;
        _bikeFree = false;
        Phase = Phase.Returning;
    }

    private void LateFlying()
    {
        HoldCamera();
        var paused = GameState.MenuOpen;
        if (!paused)
        {
            Integrate(PilotInput.Read());
        }

        Place();
        var dt = Time.deltaTime;
        _picture.Update(_clearance, dt);
        if (paused)
        {
            return;
        }

        _feed.Static(Static * Static * LimitStaticMost);
        var advanced = Config.FlightMode == FlightMode.Advanced;
        if (advanced)
        {
            _motors.Update(_advanced.Thrust, Mathf.Clamp01(_advanced.Rate.magnitude / 600f), _velocity.magnitude, true, dt, _advanced.Armed);
            if (Config.AdvancedPropsInView)
            {
                _model.Place(_position + _advanced.Attitude * FrameFromCamera, _advanced.Attitude, 1f);
                _model.Spin(_advanced.Armed ? Mathf.Max(0.2f, _advanced.Thrust) : _advanced.Thrust, dt);
            }
            else
            {
                _model.Place(_position, _advanced.Attitude, ShadowScale);
            }
        }
        else
        {
            _model.Place(_position, Quaternion.Euler(0f, _simple.Yaw, 0f), ShadowScale);
            _motors.Update(Mathf.Clamp01(0.3f + _velocity.magnitude / 40f), 0f, _velocity.magnitude, false, dt);
        }

        _feed.ShowReadout(Config.Osd);
        if (Config.Osd)
        {
            var toBike = _bike.transform.position - _position;
            var ahead = _view * Vector3.forward;
            var angle = Mathf.DeltaAngle(Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg, Mathf.Atan2(toBike.x, toBike.z) * Mathf.Rad2Deg);
            _feed.Readout(Config.FlightMode, Config.AltitudeFrom == AltitudeFrom.Camp ? _position.y - _campHeight : -toBike.y, _velocity.magnitude, toBike.magnitude, angle);
        }
    }

    private void LateCrashing()
    {
        var dt = Time.deltaTime;
        if (!GameState.MenuOpen)
        {
            _advanced.Tumble(ref _velocity, dt);
            Collision.Move(ref _position, ref _velocity, dt, AdvancedBounce);
            _view = _advanced.View;
            _model.Place(_position, _advanced.Attitude, ShadowScale);
            _feed.Static(Mathf.Clamp01(_phaseTime / CrashSeconds) * 0.9f + 0.1f);
        }

        HoldCamera();
        Place();
    }

    private void LateReturning()
    {
        _model.Place(Vector3.Lerp(_from, _to, Eased(Mathf.Clamp01(_phaseTime / ReturnSeconds))), Quaternion.Euler(0f, GameState.Heading(_bike), 0f), ShownScale);
        _model.Spin(0.4f, Time.deltaTime);
    }

    private void Integrate(in PilotInput input)
    {
        _stepsOwed = Mathf.Min(_stepsOwed + Time.deltaTime * StepRate, MostSteps);
        var dt = 1f / StepRate;
        var advanced = Config.FlightMode == FlightMode.Advanced;
        _clearance = Collision.Clearance(_position, GroundReach);
        while (_stepsOwed >= 1f)
        {
            _stepsOwed -= 1f;
            if (advanced)
            {
                _advanced.Step(input, ref _velocity, _clearance, dt);
            }
            else
            {
                _simple.Step(input, _position, ref _velocity, dt);
            }

            var push = _limits.Push(_position, out var past);
            Static = past;
            _velocity += push * dt;
            var before = _velocity;
            var hit = Collision.Move(ref _position, ref _velocity, dt, advanced ? AdvancedBounce : 0f);
            if (advanced && hit > 0f)
            {
                var normal = (_velocity - before).normalized;
                if (hit > CrashSpeed && Config.AdvancedCrashes)
                {
                    Crash(hit);
                    return;
                }

                _advanced.Knock(normal, hit);
            }
        }

        _position = _limits.Clamp(Collision.PushOut(_position));
        _view = advanced ? _advanced.View : _simple.View;
    }

    private void Crash(float speed)
    {
        _advanced.Spin(speed);
        _motors.Stop();
        _phaseTime = 0f;
        Phase = Phase.Crashing;
    }

    private void Take()
    {
        _baseFov = _camera.fieldOfView;
        _baseNear = _camera.nearClipPlane;
        _taken = true;
        HoldCamera();
        _camera.nearClipPlane = 0.03f;
        _picture.Begin(_camera);
        _clearance = 0f;
        HookPreCull();
        if (Config.FlightMode == FlightMode.Simple)
        {
            _simple.Begin(_view, -StartLookDown);
        }
        else
        {
            _advanced.Begin(_view);
        }

        ApplyLens();
        Place();
    }

    private void ApplyLens() => _camera.fieldOfView = Config.FlightMode == FlightMode.Simple ? Config.SimpleFov : Config.AdvancedFov;

    /// <summary>Other mods give the camera back to the game in their own late update: this is the last word on whose it is.</summary>
    private void HoldCamera()
    {
        if (_play != null && _play.enabled)
        {
            _play.enabled = false;
        }
    }

    private void GiveBack()
    {
        _taken = false;
        _picture.End();
        if (_camera != null)
        {
            _camera.fieldOfView = _baseFov;
            _camera.nearClipPlane = _baseNear;
        }

        if (_play != null)
        {
            _play.enabled = true;
        }

        OtherMods.PauseCamera(false);
    }

    private void Place()
    {
        if (_camera != null)
        {
            _camera.transform.SetPositionAndRotation(_position, _view);
        }
    }

    /// <summary>Other scripts can still move the camera after the mods' late update, and a render shows the last word.</summary>
    private void HookPreCull()
    {
        if (_preCull != null || _preCullFailed)
        {
            return;
        }

        try
        {
            var callback = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(PreCull));
            Camera.onPreCull = Il2CppSystem.Delegate.Combine(Camera.onPreCull, callback).Cast<Camera.CameraCallback>();
            _preCull = callback;
        }
        catch (Exception e)
        {
            _preCullFailed = true;
            Mod.Log.Warning("the picture may run a frame behind: " + e.Message);
        }
    }

    /// <summary>Every camera comes through here, and a throw would go into the game's render.</summary>
    private void PreCull(Camera camera)
    {
        if (_taken && camera == _camera)
        {
            Place();
        }
    }

    private void BuildVisuals()
    {
        _feed.Build();
        if (!_model.Built)
        {
            _model.Build(ModelMaterial());
        }
    }

    /// <summary>A lit material of the game's, for the model to take its light and fog.</summary>
    private Material ModelMaterial()
    {
        var shader = Shader.Find("Standard");
        if (shader != null)
        {
            return new Material(shader);
        }

        var bike = _bike.GetComponentInChildren<SkinnedMeshRenderer>(true);
        return new Material(bike.sharedMaterial);
    }

    /// <summary>The objects of the current level. A level load replaces them.</summary>
    private bool Find()
    {
        if (_play == null)
        {
            var found = GameObject.Find("PlayCamera(Clone)");
            _play = found == null ? null : found.GetComponent<PlayCamera>();
            if (_play == null)
            {
                return false;
            }

            _camera = _play.GetComponent<Camera>();
        }

        if (_bike == null)
        {
            var found = GameObject.Find("Bike(Clone)");
            _bike = found == null ? null : found.GetComponent<BikeLocomotion>();
            if (_bike == null)
            {
                return false;
            }

            GameState.NewBike();
            Proxies.Reset();
            Hud.NewLevel();
            _limitsKnown = false;
        }

        return true;
    }

    /// <summary>The level has gone with the flight in it: nothing is left to give back but the hold and the other mods.</summary>
    private void Abandon()
    {
        if (Phase != Phase.Grounded)
        {
            _taken = false;
            _picture.End();
            _feed.Hide();
            _model.Show(false);
            _motors.Stop();
            OtherMods.PauseCamera(false);
            OtherMods.HoldFreestyler(false);
            BikeHold.On = false;
            Phase = Phase.Grounded;
        }

        CanLaunch = false;
    }

    private static float Eased(float t) => t * t * (3f - 2f * t);
}
