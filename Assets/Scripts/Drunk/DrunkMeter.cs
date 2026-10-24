using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 酔い度。右下の「飲む」ボタンを押すとたまり、ときどき画面のピントが合わなくなる。
/// 酔い度が高いほど、ぼけの波が強く・長く・頻繁に来る。
///
/// シーンに置く必要はない。起動時に自動で作られ、DontDestroyOnLoad でシーンをまたいで生き続ける。
/// ボタンは部屋に入っている間(ホスト/クライアントとして接続中)だけ出し、部屋を出たら 0 に戻す。
/// 今は自分の画面だけの演出で、他のプレイヤーには同期していない。
///
/// ぼかしは URP の被写界深度(Bokeh)。ピント位置をすぐ目の前に置き、それより奥を全部ぼかす。
/// Bokeh のぼけの大きさ(最大 14px 相当)は URP 側で決まっているので、ここで動かすのは
/// 「どれだけその最大に近づけるか」だけ。
/// </summary>
public class DrunkMeter : MonoBehaviour
{
    public static DrunkMeter Instance { get; private set; }

    [Header("酔い度 (0〜1)")]
    [Tooltip("1回飲むごとに増える量。0.1 なら10杯で最大")]
    [SerializeField] private float drinkAmount = 0.1f;
    [Tooltip("時間で醒める速さ(1秒あたり)。0 なら醒めない")]
    [SerializeField] private float soberPerSecond = 0f;

    [Header("ぼけ方")]
    [Tooltip("酔い度最大のときのぼけ具合(0〜1)。1 で URP の Bokeh の上限")]
    [Range(0f, 1f)]
    [SerializeField] private float maxBlur = 1f;
    [Tooltip("ピント位置(m)。目の前に置くと、それより奥が全部ぼける")]
    [SerializeField] private float focusDistance = 0.3f;

    [Header("ぼけの波")]
    [Tooltip("酔い度がほぼ 0 のときの、波と波の間隔(秒)")]
    [SerializeField] private float maxWaveInterval = 15f;
    [Tooltip("酔い度最大のときの、波と波の間隔(秒)")]
    [SerializeField] private float minWaveInterval = 3f;
    [Tooltip("酔い度がほぼ 0 のときの、1回の波の長さ(秒)")]
    [SerializeField] private float minWaveDuration = 2f;
    [Tooltip("酔い度最大のときの、1回の波の長さ(秒)")]
    [SerializeField] private float maxWaveDuration = 5f;
    [Tooltip("飲んでから最初の波が来るまでの最大待ち時間(秒)")]
    [SerializeField] private float waveDelayAfterDrink = 1.5f;

    /// <summary>現在の酔い度(0〜1)。</summary>
    public float Level { get; private set; }

    private float untilNextWave;      // 次の波までの残り時間
    private float waveElapsed = -1f;  // 波の経過時間。負なら波と波の間
    private float waveDuration;

    private const float Aperture = 1f;   // F値。最小にしておき、強さは焦点距離で調整する

    private DepthOfField dof;
    private GameObject uiRoot;

    // 自分がポストエフェクトを有効にしたカメラ。醒めたら元に戻す
    private readonly HashSet<UniversalAdditionalCameraData> enabledByMe = new HashSet<UniversalAdditionalCameraData>();
    private Camera[] cameraBuffer = new Camera[8];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStartup()
    {
        if (Instance != null) return;
        var go = new GameObject("~DrunkMeter");
        DontDestroyOnLoad(go);
        go.AddComponent<DrunkMeter>();
    }

    private void Awake()
    {
        Instance = this;
        BuildVolume();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>1杯飲む。「飲む」ボタンから呼ばれる。罰ゲームなどから呼んでもいい。</summary>
    public void Drink()
    {
        Level = Mathf.Clamp01(Level + drinkAmount);

        // 飲んだらすぐ効いてくるように、次の波を早める
        if (waveElapsed < 0f) untilNextWave = Mathf.Min(untilNextWave, waveDelayAfterDrink);
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        bool inRoom = nm != null && nm.IsClient;   // ホストも IsClient が true

        if (uiRoot.activeSelf != inRoom) uiRoot.SetActive(inRoom);

        if (!inRoom && Level > 0f)
        {
            // 部屋を出たら醒める。次に入ったときは 0 から
            Level = 0f;
        }

        if (soberPerSecond > 0f && Level > 0f)
        {
            Level = Mathf.Max(0f, Level - soberPerSecond * Time.deltaTime);
        }

        if (Level > 0f)
        {
            ApplyBlur(Level * UpdateWave());
        }
        else
        {
            waveElapsed = -1f;
            untilNextWave = 0f;
            ApplyBlur(0f);
        }
    }

    /// <summary>
    /// ぼけの波を進め、今の波の強さ(0〜1)を返す。波と波の間は 0。
    /// 1回の波は sin カーブでふわっとぼけて、ふわっと戻る。
    /// </summary>
    private float UpdateWave()
    {
        if (waveElapsed < 0f)
        {
            untilNextWave -= Time.deltaTime;
            if (untilNextWave > 0f) return 0f;

            waveElapsed = 0f;
            waveDuration = Mathf.Lerp(minWaveDuration, maxWaveDuration, Level);
        }

        waveElapsed += Time.deltaTime;
        float t = waveElapsed / waveDuration;
        if (t >= 1f)
        {
            waveElapsed = -1f;
            untilNextWave = Mathf.Lerp(maxWaveInterval, minWaveInterval, Level) * Random.Range(0.7f, 1.3f);
            return 0f;
        }
        return Mathf.Sin(t * Mathf.PI);
    }

    // ---- ぼかし ----

    private void BuildVolume()
    {
        var volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;   // シーン側の Global Volume より優先する

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        dof = profile.Add<DepthOfField>();
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.aperture.Override(Aperture);
        dof.focusDistance.Override(focusDistance);
        dof.focalLength.Override(1f);
        dof.active = false;
        volume.sharedProfile = profile;
    }

    private void ApplyBlur(float level)
    {
        bool on = level > 0.001f;
        dof.active = on;
        SetPostProcessing(on);
        if (!on) return;

        dof.focusDistance.value = focusDistance;
        dof.focalLength.value = FocalLengthFor(level * maxBlur, focusDistance);
    }

    /// <summary>
    /// 遠くの物のぼけ具合(URP の maxCoC)が blur になる焦点距離(mm)を返す。
    /// URP: maxCoC = (f/A * f/1000) / (P - f/1000) を f について解いたもの。
    /// こうしておくと、ぼけ具合が酔い度にほぼ比例する。
    /// </summary>
    private static float FocalLengthFor(float blur, float focusDist)
    {
        float k = blur * Aperture;
        float f = (-k + Mathf.Sqrt(k * k + 4f * k * 1000f * focusDist)) * 0.5f;
        return Mathf.Clamp(f, 1f, 300f);
    }

    /// <summary>
    /// カメラのポストエフェクトは既定でオフのものが多い(Player プレハブ、神経衰弱の固定カメラ)。
    /// 酔っている間だけオンにし、醒めたら自分がオンにしたものだけ元に戻す。
    /// カメラはシーン遷移やスポーンで入れ替わるので毎フレーム見る。
    /// </summary>
    private void SetPostProcessing(bool on)
    {
        if (on)
        {
            if (cameraBuffer.Length < Camera.allCamerasCount)
            {
                cameraBuffer = new Camera[Camera.allCamerasCount];
            }
            int count = Camera.GetAllCameras(cameraBuffer);
            for (int i = 0; i < count; i++)
            {
                var data = cameraBuffer[i].GetUniversalAdditionalCameraData();
                if (data != null && !data.renderPostProcessing)
                {
                    data.renderPostProcessing = true;
                    enabledByMe.Add(data);
                }
            }
        }
        else if (enabledByMe.Count > 0)
        {
            foreach (var data in enabledByMe)
            {
                if (data != null) data.renderPostProcessing = false;
            }
            enabledByMe.Clear();
        }
    }

    // ---- UI ----

    /// <summary>右下に「飲む」ボタンを作る。どのシーンでも同じ見た目になるようコードで組む。</summary>
    private void BuildUI()
    {
        uiRoot = new GameObject("DrunkCanvas");
        uiRoot.transform.SetParent(transform, false);

        var canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;   // シーン側の UI より手前

        // シーン側の Canvas と同じ基準解像度にして、見た目の大きさを揃える
        var scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;

        uiRoot.AddComponent<GraphicRaycaster>();

        // 飲むボタン
        var button = CreateRect("DrinkButton", uiRoot.transform, new Vector2(-40f, 40f), new Vector2(240f, 120f));
        var buttonImage = button.gameObject.AddComponent<Image>();
        buttonImage.color = new Color(0.95f, 0.65f, 0.2f);
        var btn = button.gameObject.AddComponent<Button>();
        btn.targetGraphic = buttonImage;
        btn.onClick.AddListener(Drink);

        var buttonText = CreateText("Label", button, "飲む", 60f);
        Stretch(buttonText.rectTransform);

        uiRoot.SetActive(false);
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        SetBottomRight(rect, position, size);
        return rect;
    }

    private static void SetBottomRight(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>フォントは TMP Settings の既定(日本語は NotoSansJP にフォールバック)を使う。</summary>
    private static TMP_Text CreateText(string name, Transform parent, string text, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }
}
