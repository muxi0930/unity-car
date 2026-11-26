#if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX) && !PHOTON_CAPTURE
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif

public sealed class PhotonDesktop : MonoBehaviour
{
    [Serializable]
    public class Message
    {
        public string type, map, mode, address, data, message;
        public int session, frames;
        public float fov;
        public bool clearMarks, drifting;
        public Pose camera;
        public Pose[] cars;
        public Effect[] effects;
        public float[] marks;
    }

    [Serializable]
    public class Pose
    {
        public string id, vehicle, color;
        public bool visible;
        public int boost;
        public float x, y, z, qx, qy, qz, qw, scale;
        public Vector3 Position => new Vector3(x,y,z);
        public Quaternion Rotation => new Quaternion(qx,qy,qz,qw);
    }

    [Serializable]
    public class Effect
    {
        public string id, type;
        public float x, y, z, yaw, scale;
    }

    [Serializable]
    class NetworkEvent
    {
        public string type, data;
        public int session;
    }

    const string Library = "PhotonWebView";
    [DllImport(Library)] static extern void PhotonOpen(string path, int editor);
    [DllImport(Library)] static extern void PhotonPlace(int editor, double x, double y, double width, double height, int visible);
    [DllImport(Library)] static extern void PhotonEvaluate(string script);
    [DllImport(Library)] static extern int PhotonPoll([Out] byte[] buffer, int capacity);
    [DllImport(Library)] static extern void PhotonFullscreen();
    [DllImport(Library)] static extern void PhotonClose();

    readonly ConcurrentQueue<NetworkEvent> networking = new ConcurrentQueue<NetworkEvent>();
    byte[] buffer = new byte[1024*32];
    PhotonRoomConnection room;
    int currentSession;
    PhotonWorld world;
    bool opened, hasFrames;
#if UNITY_EDITOR
    EditorWindow gameView;
    const BindingFlags EditorMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo EditorParent = typeof(EditorWindow).GetField("m_Parent", EditorMembers);
    static readonly PropertyInfo ActiveDockTab = typeof(EditorWindow).Assembly
        .GetType("UnityEditor.HostView")?.GetProperty("actualView", EditorMembers);

    static bool IsVisible(EditorWindow window)
    {
        var parent = EditorParent?.GetValue(window);
        return parent != null && ActiveDockTab != null
            ? ReferenceEquals(ActiveDockTab.GetValue(parent),window)
            : IsGameView(EditorWindow.focusedWindow);
    }
    static bool IsGameView(EditorWindow window) => window != null && window.GetType().Name == "GameView";
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode) return;
        var original = FindFirstObjectByType<PhotonRace>();
        if (original == null) return;
        var client = new GameObject("Photon Fox Unity Client").AddComponent<PhotonDesktop>();
        client.Initialize(original);
    }

    void Initialize(PhotonRace original)
    {
        try
        {
            var file = Path.Combine(Application.streamingAssetsPath,"PhotonClient","index.html");
            if (!File.Exists(file)) throw new FileNotFoundException("Photon client interface missing", file);
#if UNITY_EDITOR
            gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            PhotonOpen(file,1);
#else
            PhotonOpen(file,0);
#endif
            opened = true;
            Application.runInBackground = true;
            world = gameObject.AddComponent<PhotonWorld>();
            world.Initialize(original);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Photon Fox 无法加载赛前界面：{ex.Message}");
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (!opened) return;
#if UNITY_EDITOR
        if (IsGameView(EditorWindow.focusedWindow)) gameView = EditorWindow.focusedWindow;
        if (gameView != null)
        {
            var rect = gameView.position;
            const int toolbar = 21;
            PhotonPlace(1,rect.x,rect.y+2*toolbar,rect.width,Math.Max(0,rect.height-toolbar),IsVisible(gameView)?1:0);
        }
#else
        PhotonPlace(0,0,0,0,0,1);
#endif
        int length;
        while ((length = PhotonPoll(buffer, buffer.Length)) != 0)
        {
            if (length < 0)
            {
                buffer = new byte[-length];
                continue;
            }
            var message = JsonUtility.FromJson<Message>(Encoding.UTF8.GetString(buffer,0,length));
            switch (message.type)
            {
                case "frame":
                    world.Apply(message);
                    if (!hasFrames && message.frames > 1)
                    {
                        hasFrames = true;
                        Debug.Log($"PHOTON CLIENT: Unity frame {Time.frameCount}, presentation frame {message.frames}, map {message.map}");
                    }
                    break;
                case "connect":
                    room?.Dispose();
                    currentSession = message.session;
                    if (Uri.TryCreate(message.address,UriKind.Absolute,out var address) &&
                        (address.Scheme == "ws" || address.Scheme == "wss"))
                        room = new PhotonRoomConnection(address,currentSession,Publish);
                    break;
                case "disconnect":
                    currentSession = 0;
                    room?.Dispose();
                    room = null;
                    break;
                case "send":
                    if (message.session == currentSession) room?.Send(message.data);
                    break;
                case "fullscreen":
#if UNITY_EDITOR
                    if (gameView != null)
                    {
                        gameView.maximized = !gameView.maximized;
                        PhotonEvaluate($"window.photonFullscreenChanged?.({gameView.maximized.ToString().ToLowerInvariant()})");
                    }
#else
                    PhotonFullscreen();
#endif
                    break;
                case "error": Debug.LogError($"Photon Fox 界面错误：{message.message}"); break;
            }
        }
        while (networking.TryDequeue(out var evt))
            if (evt.session == currentSession)
                PhotonEvaluate("(() => { const e = "+JsonUtility.ToJson(evt)+"; window.photonNetworkEvent?.(e.type,e.session,e.data); })()");
    }

    void Publish(string type,int session,string data) =>
        networking.Enqueue(new NetworkEvent{type=type,session=session,data=data});

    void OnDestroy()
    {
        room?.Dispose();
        if (opened) PhotonClose();
    }
}
#endif
