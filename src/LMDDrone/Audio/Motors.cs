using System.Runtime.InteropServices;
using Il2CppFMOD;
using Il2CppFMODUnity;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Task = System.Threading.Tasks.Task;

namespace LMDDrone.Audio;

/// <summary>
/// The motors, played through FMOD's core: four channels of the same loop started apart, panned a little and drifting against each other as four motors do, and one of wind for speed.
/// Any failure just means a silent drone, and is said in the log once.
/// </summary>
internal sealed class Motors
{
    /// <summary>Base pitch at no thrust and full, as a multiple of the loop's own pitch.</summary>
    private const float SimplePitchLow = 0.9f;
    private const float SimplePitchHigh = 1.25f;
    private const float AdvancedPitchLow = 0.8f;
    private const float AdvancedPitchHigh = 2.0f;

    /// <summary>Seconds for a prop to come up to a speed asked of it: motors have weight.</summary>
    private const float SpinUp = 0.12f;

    /// <summary>Seconds for disarmed motors to fall quiet.</summary>
    private const float SpinDown = 0.6f;

    /// <summary>Where the wind is full, and its share of the motors' level there.</summary>
    private const float WindFullAt = 30f;
    private const float WindShare = 0.9f;
    private const float WindEase = 0.25f;

    private static readonly float[] Pans = { -0.45f, 0.45f, 0.3f, -0.3f };
    private static readonly Task<(float[] motor, float[] wind)> Loops = Task.Run(() => (MotorSound.Motor(), MotorSound.Wind()));

    private readonly Channel[] _channels = new Channel[4];
    private Channel _windChannel;
    private Sound _motor;
    private Sound _wind;
    private bool _made;
    private bool _failed;
    private bool _playing;
    private float _time;
    private float _spin;
    private float _windNow;
    private float _armed;

    public void Start()
    {
        if (_failed || _playing)
        {
            return;
        }

        try
        {
            var system = RuntimeManager.CoreSystem;
            if (!_made)
            {
                Make(system);
            }

            var length = (uint)(MotorSound.Rate * MotorSound.LoopSeconds);
            for (var i = 0; i < _channels.Length; i++)
            {
                Check(system.playSound(_motor, default, true, out _channels[i]), "play");
                _channels[i].setPosition(length * (uint)i / (uint)_channels.Length, TIMEUNIT.PCM);
                _channels[i].setPan(Pans[i]);
                _channels[i].setVolume(0f);
                _channels[i].setPaused(false);
            }

            Check(system.playSound(_wind, default, false, out _windChannel), "play");
            _windChannel.setVolume(0f);
            _spin = 0f;
            _windNow = 0f;
            _armed = 1f;
            _playing = true;
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    /// <summary>Thrust 0..1, how hard it's turning 0..1, speed in metres per second, Advanced's wide range or Simple's narrow one, and disarmed falls quiet.</summary>
    public void Update(float thrust, float turning, float speed, bool advanced, float dt, bool armed = true)
    {
        if (!_playing)
        {
            return;
        }

        try
        {
            _time += dt;
            _spin = Mathf.Lerp(_spin, thrust, 1f - Mathf.Exp(-dt / SpinUp));
            var low = advanced ? AdvancedPitchLow : SimplePitchLow;
            var high = advanced ? AdvancedPitchHigh : SimplePitchHigh;
            var level = Config.Volume * (advanced ? 0.3f : 0.18f);
            _armed = armed ? 1f : Mathf.Lerp(_armed, 0f, 1f - Mathf.Exp(-dt / SpinDown));
            var volume = level * Mathf.Lerp(0.55f, 1f, _spin) * _armed;
            for (var i = 0; i < _channels.Length; i++)
            {
                var drift = Mathf.Sin(_time * (3.1f + i * 1.7f) + i) * 0.006f + (i % 2 == 0 ? turning : -turning) * 0.08f;
                _channels[i].setFrequency(MotorSound.Rate * Mathf.Lerp(low, high, _spin) * (1f + drift));
                _channels[i].setVolume(volume);
            }

            _windNow = Mathf.Lerp(_windNow, Mathf.Clamp01(speed / WindFullAt), 1f - Mathf.Exp(-dt / WindEase));
            _windChannel.setFrequency(MotorSound.Rate * Mathf.Lerp(0.8f, 1.4f, _windNow));
            _windChannel.setVolume(Config.Volume * WindShare * 0.3f * _windNow * _windNow);
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    public void Stop()
    {
        if (!_playing)
        {
            return;
        }

        _playing = false;
        try
        {
            foreach (var channel in _channels)
            {
                channel.stop();
            }

            _windChannel.stop();
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    public void Pause(bool paused)
    {
        if (!_playing)
        {
            return;
        }

        try
        {
            foreach (var channel in _channels)
            {
                channel.setPaused(paused);
            }

            _windChannel.setPaused(paused);
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    private void Make(Il2CppFMOD.System system)
    {
        var (motor, wind) = Loops.Result;
        _motor = Create(system, motor);
        _wind = Create(system, wind);
        _made = true;
    }

    private Sound Create(Il2CppFMOD.System system, float[] samples)
    {
        var bytes = new Il2CppStructArray<byte>(samples.Length * 2);
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = (short)(samples[i] * short.MaxValue);
            bytes[i * 2] = (byte)sample;
            bytes[i * 2 + 1] = (byte)(sample >> 8);
        }

        var info = new CREATESOUNDEXINFO
        {
            cbsize = Marshal.SizeOf<CreateSoundInfoShape>(),
            length = (uint)bytes.Length,
            numchannels = 1,
            defaultfrequency = MotorSound.Rate,
            format = SOUND_FORMAT.PCM16,
        };
        Check(system.createSound(bytes, MODE.OPENMEMORY | MODE.OPENRAW | MODE.LOOP_NORMAL | MODE._2D, ref info, out var sound), "create");
        return sound;
    }

    private void Check(RESULT result, string what)
    {
        if (result != RESULT.OK)
        {
            throw new InvalidOperationException(what + ": " + result);
        }
    }

    private void Fail(string message)
    {
        if (!_failed)
        {
            _failed = true;
            _playing = false;
            Mod.Log.Warning("the drone stays silent: " + message);
        }
    }

    /// <summary>FMOD 2.0's CREATESOUNDEXINFO as native code wants its size: interop's struct doesn't report it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CreateSoundInfoShape
    {
        public int cbsize;
        public uint length;
        public uint fileoffset;
        public int numchannels;
        public int defaultfrequency;
        public int format;
        public uint decodebuffersize;
        public int initialsubsound;
        public int numsubsounds;
        public IntPtr inclusionlist;
        public int inclusionlistnum;
        public IntPtr pcmreadcallback;
        public IntPtr pcmsetposcallback;
        public IntPtr nonblockcallback;
        public IntPtr dlsname;
        public IntPtr encryptionkey;
        public int maxpolyphony;
        public IntPtr userdata;
        public int suggestedsoundtype;
        public IntPtr fileuseropen;
        public IntPtr fileuserclose;
        public IntPtr fileuserread;
        public IntPtr fileuserseek;
        public IntPtr fileuserasyncread;
        public IntPtr fileuserasynccancel;
        public IntPtr fileuserdata;
        public int filebuffersize;
        public int channelorder;
        public IntPtr initialsoundgroup;
        public uint initialseekposition;
        public int initialseekpostype;
        public int ignoresetfilesystem;
        public uint audioqueuepolicy;
        public uint minmidigranularity;
        public int nonblockthreadid;
        public IntPtr fsbguid;
    }
}
