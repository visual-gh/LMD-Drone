using Il2CppTMPro;
using LMDDrone.Flight;
using UnityEngine;
using UnityEngine.UI;

namespace LMDDrone.Visual;

/// <summary>
/// What's drawn over the drone's camera: the static of a cut, of a crash and of the signal limit, and the readout. A canvas of its own: a child of the game's draws behind the 3D scene.
/// </summary>
internal sealed class Feed
{
    private const int NoiseWidth = 480;
    private const int NoiseHeight = 270;

    private GameObject _root;
    private RawImage _noise;
    private GameObject _osd;
    private TextMeshProUGUI _left;
    private TextMeshProUGUI _bikeText;
    private RawImage _pointer;

    /// <summary>0..1 of static.</summary>
    public void Static(float amount)
    {
        if (_root == null)
        {
            return;
        }

        var on = amount > 0.001f;
        _noise.gameObject.SetActive(on);
        if (on)
        {
            _noise.color = new Color(1f, 1f, 1f, Mathf.Clamp01(amount));
            _noise.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, 1f, 1f);
        }
    }

    public void Hide()
    {
        if (_root != null)
        {
            Static(0f);
            ShowReadout(false);
        }
    }

    public void ShowReadout(bool on)
    {
        if (_osd != null && _osd.activeSelf != on)
        {
            _osd.SetActive(on);
        }
    }

    /// <summary>Height, speed, mode, and the way back in the view's terms.</summary>
    public void Readout(FlightMode mode, float height, float speed, float bikeDistance, float bikeAngle)
    {
        _left.text = (mode == FlightMode.Simple ? "SIMPLE" : "ADVANCED") + "\n" + height.ToString("+0;-0;0") + " m\n" + (speed * 3.6f).ToString("0") + " km/h";
        _bikeText.text = bikeDistance.ToString("0") + " m";
        _pointer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -bikeAngle);
    }

    public void Build()
    {
        if (_root != null)
        {
            return;
        }

        _root = new GameObject("LMDDrone_Feed");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        _noise = Image(_root.transform, "static", NoiseTexture());
        Stretch(_noise.rectTransform);
        _noise.gameObject.SetActive(false);

        _osd = new GameObject("readout");
        _osd.AddComponent<RectTransform>();
        _osd.transform.SetParent(_root.transform, false);
        Stretch(_osd.GetComponent<RectTransform>());

        var font = GameFont();
        // At the top: the bottom corners are the hints'.
        _left = Text(_osd.transform, font, new Vector2(0f, 1f), new Vector2(48f, -48f), TextAlignmentOptions.TopLeft);
        _bikeText = Text(_osd.transform, font, new Vector2(1f, 1f), new Vector2(-48f, -48f), TextAlignmentOptions.TopRight);
        _pointer = Image(_osd.transform, "pointer", TriangleTexture());
        var rect = _pointer.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-84f, -120f);
        rect.sizeDelta = new Vector2(32f, 32f);
        _osd.SetActive(false);
    }

    private static RawImage Image(Transform parent, string name, Texture texture)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, Vector2 anchor, Vector2 at, TextAlignmentOptions alignment)
    {
        var go = new GameObject("text");
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            text.font = font;
        }

        text.fontSize = 28f;
        text.color = new Color(1f, 1f, 1f, 0.9f);
        text.alignment = alignment;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(320f, 140f);
        return text;
    }

    /// <summary>The HUD's own font, so it reads as part of the game.</summary>
    private static TMP_FontAsset GameFont()
    {
        var screen = GameObject.Find("PlayScreen(Clone)");
        var text = screen == null ? null : screen.GetComponentInChildren<TextMeshProUGUI>(true);
        return text == null ? null : text.font;
    }

    private static Texture2D NoiseTexture()
    {
        var texture = new Texture2D(NoiseWidth, NoiseHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var random = new System.Random(7);
        for (var y = 0; y < NoiseHeight; y++)
        {
            for (var x = 0; x < NoiseWidth; x++)
            {
                var v = (float)random.NextDouble();
                texture.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D TriangleTexture()
    {
        const int Size = 32;
        const int Tall = 24;
        // Clamped: repeating, the smoothing at the tip's edge reads the wide row at the far side of the texture, which shows as a line across the top.
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var bottom = (Size - Tall) / 2;
        for (var y = 0; y < Size; y++)
        {
            // A clean stair: a 2 pixel tip, then one more each side for every 2 rows down.
            var fromTop = bottom + Tall - 1 - y;
            var half = fromTop >= 0 && fromTop < Tall ? 1 + fromTop / 2 : 0;
            for (var x = 0; x < Size; x++)
            {
                var inside = x >= Size / 2 - half && x < Size / 2 + half;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 0.9f : 0f));
            }
        }

        texture.Apply();
        return texture;
    }
}
