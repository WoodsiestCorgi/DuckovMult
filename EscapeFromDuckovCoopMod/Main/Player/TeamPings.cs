using Duckov.MiniMaps;
using Duckov.Scenes;
using TMPro;
using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

// [max] Team pings: the ping key marks the aim point for every player on the same map.
// Each player sees a marker with the name and distance on screen and an icon on the map.
// A new ping from the same player replaces the old one; pings fade out after a few seconds.
internal sealed class TeamPings : MonoBehaviour
{
    private const float Lifetime = 15f;
    private const float FadeTime = 2f;
    private const float Cooldown = 0.4f;
    private const float ScreenMargin = 36f;

    private static readonly Color[] Palette =
    {
        new(1f, 0.78f, 0.2f),
        new(0.35f, 0.8f, 1f),
        new(0.55f, 1f, 0.45f),
        new(1f, 0.45f, 0.75f),
        new(0.8f, 0.6f, 1f),
        new(1f, 0.55f, 0.3f)
    };

    private sealed class Ping
    {
        public string Id;
        public string Name;
        public Vector3 Position;
        public float Created;
        public Color Color;
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Label;
        public int ShownDistance = -1;
        public SimplePointOfInterest Poi;
    }

    internal static TeamPings Instance { get; private set; }

    private readonly Dictionary<string, Ping> _pings = new();
    private readonly List<string> _expired = new();
    private Canvas _canvas;
    private float _nextAllowed;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        try
        {
            HandleInput();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TeamPings] " + e.Message);
        }
    }

    private void HandleInput()
    {
        var key = CoopAISettings.ActiveGeneral?.PingKey ?? KeyCode.None;
        if (key == KeyCode.None || !Input.GetKeyDown(key)) return;
        if (Time.unscaledTime < _nextAllowed) return;

        var service = NetService.Instance;
        if (service == null || !service.networkStarted) return;
        if (MModUI.Instance != null && MModUI.Instance.IsChatTyping()) return;
        if (!LevelManager.LevelInited || GameManager.Paused) return;

        var main = CharacterMainControl.Main;
        if (!main || (main.Health && main.Health.IsDead)) return;

        var lpm = LocalPlayerManager.Instance;
        if (lpm == null || !lpm.ComputeIsInGame(out var sceneId)) return;

        _nextAllowed = Time.unscaledTime + Cooldown;

        var rpc = new TeamPingRpc
        {
            PlayerId = service.GetPlayerId(null),
            PlayerName = LocalName(service),
            SceneId = sceneId ?? string.Empty,
            SubSceneId = SafeActiveSubScene(),
            Position = main.GetCurrentAimPoint()
        };

        Show(in rpc);
        // Host: to every client. Client: to the host, who relays it to the others.
        CoopTool.SendRpc(in rpc);
    }

    internal static void Handle(RpcContext context, TeamPingRpc rpc)
    {
        if (context.IsServer)
            CoopTool.SendRpc(in rpc, context.Sender);

        if (Instance) Instance.Show(in rpc);
    }

    internal static string KeyName(KeyCode key)
    {
        return key switch
        {
            KeyCode.None => CoopLocalization.Get("ui.key.none"),
            KeyCode.Mouse2 => CoopLocalization.Get("ui.key.mouseMiddle"),
            KeyCode.Mouse3 => CoopLocalization.Get("ui.key.mouseSide", 4),
            KeyCode.Mouse4 => CoopLocalization.Get("ui.key.mouseSide", 5),
            _ => key.ToString()
        };
    }

    private void Show(in TeamPingRpc rpc)
    {
        var lpm = LocalPlayerManager.Instance;
        if (lpm == null || !lpm.ComputeIsInGame(out var mySceneId)) return;
        if (!string.IsNullOrEmpty(rpc.SceneId) && !Spectator.AreSameMap(mySceneId, rpc.SceneId)) return;

        EnsureCanvas();

        var id = !string.IsNullOrEmpty(rpc.PlayerId) ? rpc.PlayerId : rpc.PlayerName ?? "?";
        Remove(id);

        var ping = new Ping
        {
            Id = id,
            Name = string.IsNullOrEmpty(rpc.PlayerName) ? "?" : rpc.PlayerName,
            Position = rpc.Position,
            Created = Time.unscaledTime,
            Color = ColorFor(id)
        };
        BuildMarker(ping);
        ping.Poi = CreatePoi(ping, rpc.SubSceneId);
        _pings[id] = ping;

        try
        {
            Duckov.AudioManager.Post("UI/hover");
        }
        catch
        {
        }
    }

    private void LateUpdate()
    {
        if (_pings.Count == 0) return;

        var cam = Camera.main;
        var now = Time.unscaledTime;
        var hidden = GameManager.Paused;
        var hasListener = TryGetListenerPosition(out var listener);

        _expired.Clear();
        foreach (var ping in _pings.Values)
        {
            var age = now - ping.Created;
            if (age >= Lifetime || !ping.Root)
            {
                _expired.Add(ping.Id);
                continue;
            }

            if (hidden || !cam)
            {
                ping.Group.alpha = 0f;
                continue;
            }

            ping.Group.alpha = age > Lifetime - FadeTime ? Mathf.Clamp01((Lifetime - age) / FadeTime) : 1f;

            var screen = cam.WorldToScreenPoint(ping.Position + Vector3.up * 0.5f);
            if (screen.z < 0f)
            {
                screen.x = Screen.width - screen.x;
                screen.y = Screen.height - screen.y;
            }

            screen.x = Mathf.Clamp(screen.x, ScreenMargin, Screen.width - ScreenMargin);
            screen.y = Mathf.Clamp(screen.y, ScreenMargin + 26f, Screen.height - ScreenMargin);
            ping.Root.position = new Vector3(screen.x, screen.y, 0f);

            var distance = hasListener ? Mathf.RoundToInt(Vector3.Distance(listener, ping.Position)) : 0;
            if (distance != ping.ShownDistance)
            {
                ping.ShownDistance = distance;
                ping.Label.text = CoopLocalization.Get("ui.ping.label", ping.Name, distance);
            }
        }

        foreach (var id in _expired)
            Remove(id);
    }

    private void Remove(string id)
    {
        if (!_pings.TryGetValue(id, out var ping)) return;
        _pings.Remove(id);
        if (ping.Root) Destroy(ping.Root.gameObject);
        if (ping.Poi) Destroy(ping.Poi.gameObject);
    }

    private void EnsureCanvas()
    {
        if (_canvas) return;

        var root = new GameObject("TeamPingsCanvas", typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 31000;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void BuildMarker(Ping ping)
    {
        var root = new GameObject("Ping_" + ping.Id, typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(_canvas.transform, false);
        ping.Root = root.GetComponent<RectTransform>();
        ping.Root.sizeDelta = Vector2.zero;
        ping.Group = root.GetComponent<CanvasGroup>();
        ping.Group.blocksRaycasts = false;
        ping.Group.interactable = false;

        CreateDiamond(root.transform, "Shadow", 26f, new Color(0f, 0f, 0f, 0.65f));
        CreateDiamond(root.transform, "Diamond", 18f, ping.Color);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(root.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(320f, 30f);
        labelRect.anchoredPosition = new Vector2(0f, -28f);

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 19f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = ping.Color;
        label.richText = false;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        ping.Label = label;
    }

    private static void CreateDiamond(Transform parent, string name, float size, Color color)
    {
        var diamond = new GameObject(name, typeof(RectTransform), typeof(Image));
        diamond.transform.SetParent(parent, false);
        var rect = diamond.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(size, size);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var image = diamond.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static SimplePointOfInterest CreatePoi(Ping ping, string subSceneId)
    {
        try
        {
            if (MultiSceneCore.Instance == null || !MultiSceneCore.MainScene.HasValue) return null;

            Sprite icon = null;
            var icons = MapMarkerManager.Icons;
            if (icons != null && icons.Count > 0) icon = icons[0];

            var scene = string.IsNullOrEmpty(subSceneId) ? SafeActiveSubScene() : subSceneId;
            var poi = SimplePointOfInterest.Create(ping.Position, scene, ping.Name, icon);
            poi.Color = ping.Color;
            poi.ShadowColor = Color.black;
            poi.ShadowDistance = 0f;
            return poi;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetListenerPosition(out Vector3 position)
    {
        var spectator = Spectator.Instance;
        if (spectator != null && spectator.TryGetSpectateAnchor(out position)) return true;

        var main = CharacterMainControl.Main;
        if (main)
        {
            position = main.transform.position;
            return true;
        }

        position = default;
        return false;
    }

    private static string LocalName(NetService service)
    {
        var status = service.localPlayerStatus;
        if (status != null && !string.IsNullOrEmpty(status.SteamName)) return status.SteamName;
        if (status != null && !string.IsNullOrEmpty(status.PlayerName)) return status.PlayerName;
        return "Player";
    }

    private static string SafeActiveSubScene()
    {
        try
        {
            return MultiSceneCore.ActiveSubSceneID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Color ColorFor(string id)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in id ?? string.Empty) hash = hash * 31 + c;
            return Palette[(hash & 0x7fffffff) % Palette.Length];
        }
    }
}
