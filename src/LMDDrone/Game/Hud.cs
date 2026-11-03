using HarmonyLib;
using Il2CppMegagon.Downhill;
using Il2CppMegagon.Downhill.Managers;
using Il2CppMegagon.Downhill.UI;
using Il2CppMegagon.Downhill.UI.Screens;
using Il2CppTMPro;
using LMDDrone.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LMDDrone.Game;

/// <summary>
/// The game's own prompts, and the buttons. On the trail the hint is a copy of "Take a break", in the middle where the game's own prompts go, on Y as that is, and gives way to them:
/// while theirs is up, Y is theirs. The bar's prompts are copies of the break's "Continue Ride", strip and all: in a break "Launch drone" mirrored on the left, on X; in flight
/// "Recall drone" on its right in its place and "Switch mode" mirrored on the left. So the looks, the place and a pad's glyph are the game's, and they fade as the game's do.
/// A break has no pause.
/// </summary>
internal static class Hud
{
    /// <summary>Seconds stopped before the hint shows.</summary>
    public const float HintAfter = 1f;

    /// <summary>As the game's prompts fade, measured on its panel: in over 0.5 s easing out (quadratic), out over 0.75 s easing out harder (cubic).</summary>
    private const float FadeInSeconds = 0.5f;
    private const float FadeOutSeconds = 0.75f;

    /// <summary>On screen and then gone, as "Continue Ride" is (measured: fading at 6 s, gone by 13).</summary>
    private const float HintSeconds = 6f;

    private const string LaunchKey = "LMDDRONE_LAUNCH";
    private const string ModeKey = "LMDDRONE_MODE";
    private const string BackKey = "LMDDRONE_BACK";
    private const string BackBreakKey = "LMDDRONE_BACKBREAK";

    /// <summary>The lines, and the keyboard's key for each where the action's own key isn't the drone's (null: the action's own).</summary>
    private static readonly Dictionary<string, (string Text, string Keyboard)> Lines = new()
    {
        [LaunchKey] = ("{0} Launch drone", "X"),
        [ModeKey] = ("{0} Switch mode", "Tab"),
        [BackKey] = ("{0} Recall drone", null),
        [BackBreakKey] = ("{0} Recall drone", "Backspace"),
    };

    /// <summary>
    /// Rewired's UI_ACTION_1 is X on a pad. B is SUICIDE on the trail, Backspace on the keyboard (UI_CANCEL is Start and Escape there, the pause), and UI_CANCEL in a break
    /// (Escape on the keyboard, which isn't the drone's): for the glyphs.
    /// </summary>
    private const int XAction = 17;
    private const int BAction = 44;
    private const int BBreakAction = 4;

    private const string ContinueText = "TextMeshPro Text_CancelCutscene";
    private const string BreakText = "TextMeshPro Text_HiddenLocation";

    /// <summary>A line that's in the game's table once it has loaded it: how a reloaded table for another language is noticed.</summary>
    private const string TableProbe = "MODIFIER_MIRRORMODE";

    private static readonly List<(Behaviour Part, bool Was)> Hidden = new();
    private static GameObject _canvas;
    private static Prompt _trail;
    private static Prompt _break;
    private static Prompt _mode;
    private static Prompt _back;
    private static Prompt _backBreak;
    private static bool _flying;
    private static bool _flyingFromBreak;
    private static float _flyingFor;
    private static GameObject _gameFinish;
    private static GameObject _gameLocation;
    private static GameObject _gameItem;

    /// <summary>The break's own panel, "Continue Ride" and its strip: on the break's screen, not the HUD's.</summary>
    private static GameObject _gameBreak;
    private static bool _triedPanel;
    private static bool _barFailed;
    private static bool _wanted;
    private static bool _inBreak;
    private static float _shownFor;
    private static bool _panelsHidden;

    /// <summary>The ride is under way: no hint at a spawn or a respawn.</summary>
    public static bool Ridden { get; private set; }

    /// <summary>"Show Results" is up, and X is the game's.</summary>
    public static bool FinishShowing => _gameFinish != null && _gameFinish.activeInHierarchy;

    /// <summary>"Take a break" or "Collect" is up, where the hint would go.</summary>
    private static bool GamePromptShowing => (_gameLocation != null && _gameLocation.activeInHierarchy) || (_gameItem != null && _gameItem.activeInHierarchy);

    /// <summary>On the trail Y on a pad, unless the game's prompt has it. In a break X, as B is the game's "Continue Ride". X on a keyboard.</summary>
    public static bool LaunchPressed => (GameState.Resting ? Gamepad.Pressed(Gamepad.X) : Gamepad.Pressed(Gamepad.Y) && !GamePromptShowing) || Input.GetKeyDown(KeyCode.X);

    /// <summary>B on a pad, Backspace on a keyboard: the game's own B and Backspace, the suicide, are shut off with the bike held.</summary>
    public static bool BackPressed => Gamepad.Pressed(Gamepad.B) || Input.GetKeyDown(KeyCode.Backspace);

    /// <summary>X on a pad, Tab on a keyboard.</summary>
    public static bool ModePressed => Gamepad.Pressed(Gamepad.X) || Input.GetKeyDown(KeyCode.Tab);

    /// <summary>The player last used a gamepad, by the game's input.</summary>
    private static bool UsingPad
    {
        get
        {
            try
            {
                var controller = Il2CppRewired.ReInput.players.GetPlayer(0).controllers.GetLastActiveController();
                return controller != null && controller.type == Il2CppRewired.ControllerType.Joystick;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The flight's hints, while flying.</summary>
    public static void ShowFlightHints(bool show, bool fromBreak) => (_flying, _flyingFromBreak) = (show, fromBreak);

    public static void NewLevel()
    {
        Ridden = false;
        Hidden.Clear();
        _panelsHidden = false;

        // A respawn is a new bike on the same screen: the copies would stay behind it.
        foreach (var prompt in new[] { _trail, _break, _mode, _back, _backBreak }.Where(p => p != null))
        {
            prompt.Destroy();
        }

        if (_canvas != null)
        {
            UnityEngine.Object.Destroy(_canvas);
        }

        _canvas = null;
        _trail = null;
        _break = null;
        _barFailed = false;
        _mode = null;
        _back = null;
        _backBreak = null;
        _gameBreak = null;
        _gameFinish = null;
        _gameLocation = null;
        _gameItem = null;
        _triedPanel = false;
        _wanted = false;
    }

    /// <summary>Per frame on the ground: crashing starts it over, and riding ends the wait.</summary>
    public static void NoteRide(bool crashed, float speed)
    {
        if (crashed)
        {
            Ridden = false;
        }
        else if (speed > 1f)
        {
            Ridden = true;
        }
    }

    /// <summary>Asked for, it shows and then fades by itself, until it's been let go of and asked for again.</summary>
    public static void ShowHint(bool show, bool inBreak = false)
    {
        if (!show || inBreak != _inBreak)
        {
            _shownFor = 0f;
        }

        _wanted = show;
        _inBreak = inBreak;
    }

    /// <summary>
    /// The game's "Take a break", "Collect" and a break's "Continue Ride" (on B and Escape) act wherever the camera is, and read as nonsense over a flight: out of sight and deaf while it lasts. Their
    /// graphics and inputs go off, not the panels: the game's fades write the panels' alpha (so a zeroed alpha came back through) and only run when the bike comes or goes. The
    /// bike hasn't moved, so each part comes back as it was.
    /// </summary>
    public static void HideGamePanels(bool hide)
    {
        if (hide == _panelsHidden)
        {
            return;
        }

        _panelsHidden = hide;
        if (!hide)
        {

            foreach (var (part, was) in Hidden.Where(h => h.Part != null))
            {
                part.enabled = was;
            }

            Hidden.Clear();
            return;
        }

        // shortcut: Escape (and likely Start) still ends a break under the drone, through a path not found yet (not the button, not Rewired's player, not the screen's
        // script); the flight then comes home by itself (Flyer). Block it when that path is known.

        foreach (var panel in new[] { _gameLocation, _gameItem, _gameBreak }.Where(p => p != null))
        {
            foreach (var part in panel.GetComponentsInChildren<InteractiveUIElement>(true).Select(e => (Behaviour)e).Concat(panel.GetComponentsInChildren<Graphic>(true)))
            {
                Hidden.Add((part, part.enabled));
                part.enabled = false;
            }
        }
    }

    /// <summary>Per frame: the fade, and the panel the first time it's asked for.</summary>
    public static void Update(float dt)
    {
        if (!_triedPanel)
        {
            FindPanels();
        }

        Publish();
        if (_trail == null)
        {
            return;
        }

        // The game's own prompt on the spot takes the place, and the hint's few seconds wait for it to go.
        var clear = _inBreak || !GamePromptShowing;
        if (_wanted && clear)
        {
            _shownFor += dt;
        }

        var up = _wanted && clear && _shownFor < HintSeconds;
        _trail.Fade(up && !_inBreak, dt);
        if (_back == null)
        {
            FindBar();
            return;
        }

        // In a break, with "Continue Ride": it comes and goes with the game's panel.
        var breakPanel = _gameBreak.activeInHierarchy ? _gameBreak.GetComponent<CanvasGroup>() : null;
        if (_inBreak && !_flying && breakPanel != null)
        {
            _break.Follow(breakPanel.alpha);
        }
        else
        {
            _break.Fade(false, dt);
        }

        // In flight a few seconds, then gone, as the game's hints are.
        _flyingFor = _flying ? _flyingFor + dt : 0f;
        var bar = _flying && _flyingFor < HintSeconds;
        _mode.Fade(bar, dt);
        _back.Fade(bar && !_flyingFromBreak, dt);
        _backBreak.Fade(bar && _flyingFromBreak, dt);
    }

    /// <summary>
    /// One copy of a game's prompt panel with one of its texts, that fades in and out as the game's do and says one of the lines. Without an action it keeps the source's button.
    /// The game's element works the text and the key out late, whenever its refresh finishes: into a hidden text of its own, or it would land over the line written here.
    /// </summary>
    private sealed class Prompt
    {
        private readonly GameObject _panel;
        private readonly CanvasGroup _group;
        private readonly InteractiveUIElement _input;
        private readonly TextMeshProUGUI _text;
        private readonly TextMeshProUGUI _worked;
        private readonly string _key;
        private readonly string _keyboard;
        private bool _keyApplied;
        private bool _pad;
        private bool _up;
        private float _from;
        private float _elapsed;

        public Prompt(GameObject source, Transform parent, string name, string text, string key, int? action, bool mirrored)
        {
            _key = key;
            _keyboard = Lines[key].Keyboard == null ? null : "<color=#e9ac50>[" + Lines[key].Keyboard + "]</color>";

            // Made under a switched-off holder: a live copy of the game's element and button signs up with the game's input and the UI's navigation as it wakes,
            // and took the pause menu's input away (locked in it). The copy only works out its text: element and button stay off.
            var holder = new GameObject("LMDDrone_Holder");
            holder.SetActive(false);
            _panel = UnityEngine.Object.Instantiate(source, holder.transform, false);
            _panel.name = name;
            foreach (var behaviour in _panel.GetComponentsInChildren<InteractiveUIElement>(true).Select(e => (Behaviour)e).Concat(_panel.GetComponentsInChildren<Selectable>(true)))
            {
                behaviour.enabled = false;
            }

            _panel.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(holder);
            if (parent == source.transform.parent)
            {
                _panel.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            }
            else
            {
                // Over the whole canvas, as the source is over its screen: a pixel past each edge, as the game's is, so the strip meets the edge.
                var rect = _panel.GetComponent<RectTransform>();
                var theirs = source.GetComponent<RectTransform>();
                rect.anchorMin = theirs.anchorMin;
                rect.anchorMax = theirs.anchorMax;
                rect.anchoredPosition = theirs.anchoredPosition;
                rect.sizeDelta = theirs.sizeDelta;
            }

            _group = _panel.GetComponent<CanvasGroup>() ?? _panel.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // The one text it says, and the parts that go with it: the bar's strip goes with "Continue Ride".
            foreach (var other in _panel.GetComponentsInChildren<InteractiveUIElement>(true).Where(e => e.name != text).ToList())
            {
                UnityEngine.Object.DestroyImmediate(other.gameObject);
            }

            _input = _panel.GetComponentInChildren<InteractiveUIElement>(true);
            if (action != null)
            {
                _input.m_rewiredActionID = action.Value;
            }

            _input.m_delegatesOnActivate = new EventTrigger.Entry();
            _text = _input.GetComponent<TextMeshProUGUI>();
            var worked = new GameObject("KeyText");
            worked.SetActive(false);
            worked.transform.SetParent(_text.transform, false);
            _worked = worked.AddComponent<TextMeshProUGUI>();
            _input.field_Private_TextMeshProUGUI_0 = _worked;
            if (mirrored)
            {
                foreach (var part in _panel.GetComponentsInChildren<RectTransform>(true).Where(r => r.parent == _panel.transform))
                {
                    Mirror(part, part != _text.rectTransform);
                }

                _text.horizontalAlignment = HorizontalAlignmentOptions.Left;
            }

            // The prompt is a button, and one the game hasn't switched on tints its text dim.
            var button = _input.GetComponent<Button>();
            if (button != null)
            {
                button.transition = Selectable.Transition.None;
            }

            _text.canvasRenderer.SetColor(Color.white);
            _panel.SetActive(false);
        }

        public void Destroy()
        {
            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel);
            }
        }

        /// <summary>At the game's panel's own alpha, so it comes and goes with it.</summary>
        public void Follow(float alpha)
        {
            _up = alpha > 0f;
            _group.alpha = alpha;
            if (_panel.activeSelf != alpha > 0f)
            {
                _panel.SetActive(alpha > 0f);
            }

            if (_panel.activeSelf)
            {
                WriteText();
            }
        }

        public void Fade(bool up, float dt)
        {
            if (up != _up)
            {
                _up = up;
                _from = _group.alpha;
                _elapsed = 0f;
            }

            _elapsed += dt;
            var shown = up
                ? _from + (1f - _from) * (1f - Mathf.Pow(1f - Mathf.Clamp01(_elapsed / FadeInSeconds), 2f))
                : _from * Mathf.Pow(1f - Mathf.Clamp01(_elapsed / FadeOutSeconds), 3f);
            _group.alpha = shown;
            if (_panel.activeSelf != shown > 0f)
            {
                _panel.SetActive(shown > 0f);
            }

            if (_panel.activeSelf)
            {
                WriteText();
            }
        }

        /// <summary>The line with the key the element worked out for the device in use, written here only: cheap when nothing changed.</summary>
        private void WriteText()
        {
            // The element is off, so it hears of no change of device: asked again on each one, as it would on its own.
            var pad = UsingPad;
            if (!_keyApplied || pad != _pad)
            {
                _keyApplied = true;
                _pad = pad;
                _input.Method_Public_Virtual_Final_New_Void_String_0(_key);
                _input.Method_Private_Void_Boolean_Boolean_0(true, true);
            }

            var format = _input.field_Private_String_0;
            var key = _input.field_Private_String_1;
            if (format != null && !format.Contains("{0}") && key != null && key.Contains("{0}"))
            {
                (format, key) = (key, format);
            }

            if (!string.IsNullOrEmpty(format) && format.Contains("{0}") && key != null)
            {
                // A keyboard's key comes in brackets; where the action's own isn't the drone's, the drone's goes in. A pad's is a glyph from the sprite asset the element picked.
                var text = format.Replace("{0}", _keyboard != null && key.Contains('[') ? _keyboard : key);
                if (_worked.spriteAsset != null && _text.spriteAsset != _worked.spriteAsset)
                {
                    _text.spriteAsset = _worked.spriteAsset;
                }

                if (_text.text != text)
                {
                    _text.text = text;
                }
            }
        }
    }

    /// <summary>The HUD's prompts live under its own group: the trail's hint stands there too, next to "Take a break".</summary>
    private static void FindPanels()
    {
        var screen = GameObject.Find("PlayScreen(Clone)");
        var group = screen == null ? null : screen.transform.Find("PlayScreen_Group");
        if (group == null)
        {
            return;
        }

        _triedPanel = true;
        _gameFinish = group.Find("FinishScreenPanel")?.gameObject;
        _gameLocation = group.Find("HiddenLocationPanel")?.gameObject;
        _gameItem = group.Find("HiddenItemPanel")?.gameObject;
        if (_gameFinish == null || _gameLocation == null)
        {
            return;
        }

        try
        {
            _trail = new Prompt(_gameLocation, group, "LMDDrone_Hint", BreakText, LaunchKey, null, false);
        }
        catch (Exception e)
        {
            _trail = null;
            Mod.Log.Warning("no hint on screen: " + e.Message);
        }
    }

    /// <summary>
    /// The bar's prompts are copies of the break's "Continue Ride", which lives on the break's screen. The game's screens draw behind the 3D scene while the drone has the
    /// camera, so the copies have a canvas of their own over everything, scaled as the game's screen is: a copy sits where the game's does. Looked for until the break's
    /// screen is there.
    /// </summary>
    private static void FindBar()
    {
        var wrapper = _gameLocation.transform.parent.parent.parent;
        var panel = _barFailed || wrapper == null ? null : wrapper.Find("CutsceneScreen(Clone)/HiddenLocationPanel");
        if (panel == null)
        {
            return;
        }

        try
        {
            _canvas = new GameObject("LMDDrone_HintCanvas");
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            // Scaled as the break's own screen, whose prompt this is (the HUD's is scaled otherwise: the copies came out bigger).
            var theirs = panel.parent.GetComponent<CanvasScaler>() ?? _gameLocation.transform.parent.parent.GetComponent<CanvasScaler>();
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = theirs.uiScaleMode;
            scaler.referenceResolution = theirs.referenceResolution;
            scaler.screenMatchMode = theirs.screenMatchMode;
            scaler.matchWidthOrHeight = theirs.matchWidthOrHeight;
            _gameBreak = panel.gameObject;
            _break = new Prompt(_gameBreak, _canvas.transform, "LMDDrone_BreakHint", ContinueText, LaunchKey, XAction, true);
            _mode = new Prompt(_gameBreak, _canvas.transform, "LMDDrone_ModeHint", ContinueText, ModeKey, XAction, true);
            _back = new Prompt(_gameBreak, _canvas.transform, "LMDDrone_BackHint", ContinueText, BackKey, BAction, false);
            _backBreak = new Prompt(_gameBreak, _canvas.transform, "LMDDrone_BackBreakHint", ContinueText, BackBreakKey, BBreakAction, false);
        }
        catch (Exception e)
        {
            _back = null;
            _barFailed = true;
            Mod.Log.Warning("no flight hints on screen: " + e.Message);
        }
    }

    /// <summary>The same place from the other side of its parent. A graphic is flipped too, about its pivot, so a fade runs the other way; text only moves.</summary>
    private static void Mirror(RectTransform rect, bool flip)
    {
        var (min, max) = (rect.anchorMin, rect.anchorMax);
        rect.anchorMin = new Vector2(1f - max.x, min.y);
        rect.anchorMax = new Vector2(1f - min.x, max.y);
        rect.anchoredPosition = new Vector2(-rect.anchoredPosition.x, rect.anchoredPosition.y);
        if (flip)
        {
            rect.localScale = new Vector3(-rect.localScale.x, rect.localScale.y, rect.localScale.z);
        }
        else
        {
            rect.pivot = new Vector2(1f - rect.pivot.x, rect.pivot.y);
        }
    }

    /// <summary>The game looks its lines up in tables, and reloads them for another language.</summary>
    private static void Publish()
    {
        var manager = Manager<LocalizationManager>.prop_T_0;
        if (manager == null)
        {
            return;
        }

        foreach (var table in new[] { manager.field_Private_Dictionary_2_String_String_0, manager.field_Private_Dictionary_2_String_String_1 })
        {
            if (table != null && table.ContainsKey(TableProbe) && !table.ContainsKey(LaunchKey))
            {
                foreach (var line in Lines)
                {
                    table[line.Key] = line.Value.Text;
                }
            }
        }
    }

    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.Method_Public_Static_String_String_0))]
    private static class Lookup
    {
        private static bool Prefix(string __0, ref string __result)
        {
            if (__0 == null || !Lines.TryGetValue(__0, out var line))
            {
                return true;
            }

            __result = line.Text;
            return false;
        }
    }

    /// <summary>A pause menu's way out of the ride ends the flight first, so what they do finds everything as it left it.</summary>
    [HarmonyPatch]
    private static class WaysOut
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PauseScreen), nameof(PauseScreen.OnRestartCheckpoint));
            yield return AccessTools.Method(typeof(PauseScreen), nameof(PauseScreen.OnRestartRun));
            yield return AccessTools.Method(typeof(PauseScreen), nameof(PauseScreen.OnBackToMenu));
            yield return AccessTools.Method(typeof(PauseScreen), nameof(PauseScreen.OnQuitToDesktop));
        }

        private static void Prefix() => Mod.Flyer?.Land();
    }
}
