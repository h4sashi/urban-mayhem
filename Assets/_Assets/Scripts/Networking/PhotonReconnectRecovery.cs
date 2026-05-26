using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PhotonReconnectRecovery : MonoBehaviourPunCallbacks
{
    public const float ReconnectTimeoutSeconds = 20f;
    public const int RoomPlayerTtlMilliseconds = 15000;
    public const int RoomEmptyTtlMilliseconds = 15000;

    private const string FallbackLobbySceneName = "Main";

    private static PhotonReconnectRecovery instance;

    private Coroutine reconnectRoutine;
    private string lastRoomName;
    private float reconnectDeadline;
    private float nextReconnectAttemptTime;
    private bool reconnecting;
    private bool directRoomReconnectAttempted;
    private bool intentionalRoomLeave;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject recoveryObject = new GameObject(nameof(PhotonReconnectRecovery));
        DontDestroyOnLoad(recoveryObject);
        instance = recoveryObject.AddComponent<PhotonReconnectRecovery>();
    }

    public static void MarkIntentionalRoomLeave()
    {
        if (instance != null)
        {
            instance.intentionalRoomLeave = true;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();

        lastRoomName = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : lastRoomName;
        intentionalRoomLeave = false;

        if (reconnecting)
        {
            FinishReconnect();
        }
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();

        if (intentionalRoomLeave || !IsGameplayScene(SceneManager.GetActiveScene().name))
        {
            lastRoomName = null;
            intentionalRoomLeave = false;
        }
    }

    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();

        if (!reconnecting || string.IsNullOrEmpty(lastRoomName) || PhotonNetwork.InRoom)
            return;

        TryRejoinLastRoom();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        base.OnJoinRoomFailed(returnCode, message);

        if (reconnecting)
        {
            Debug.LogWarning($"[Reconnect] Rejoin failed: ({returnCode}) {message}");
            nextReconnectAttemptTime = Time.realtimeSinceStartup + 1f;
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        base.OnDisconnected(cause);

        if (
            intentionalRoomLeave
            || cause == DisconnectCause.DisconnectByClientLogic
            || cause == DisconnectCause.ApplicationQuit
        )
        {
            intentionalRoomLeave = false;
            return;
        }

        if (!ShouldRecoverFromDisconnect())
            return;

        if (reconnectRoutine != null)
            StopCoroutine(reconnectRoutine);

        reconnectRoutine = StartCoroutine(ReconnectThenReturnToLobby(cause));
    }

    private IEnumerator ReconnectThenReturnToLobby(DisconnectCause cause)
    {
        reconnecting = true;
        directRoomReconnectAttempted = false;
        reconnectDeadline = Time.realtimeSinceStartup + ReconnectTimeoutSeconds;
        nextReconnectAttemptTime = 0f;

        Debug.LogWarning($"[Reconnect] Connection lost ({cause}). Trying to recover for {ReconnectTimeoutSeconds:0}s.");

        while (Time.realtimeSinceStartup < reconnectDeadline)
        {
            if (PhotonNetwork.InRoom)
            {
                FinishReconnect();
                yield break;
            }

            if (PhotonNetwork.NetworkClientState == ClientState.Disconnected)
            {
                TryStartReconnect();
            }
            else if (PhotonNetwork.IsConnectedAndReady && !string.IsNullOrEmpty(lastRoomName))
            {
                TryRejoinLastRoom();
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        Debug.LogWarning("[Reconnect] Could not recover connection in time. Returning to lobby.");
        reconnecting = false;
        reconnectRoutine = null;
        lastRoomName = null;
        intentionalRoomLeave = true;

        if (PhotonNetwork.NetworkClientState != ClientState.Disconnected)
        {
            PhotonNetwork.Disconnect();
        }

        SceneManager.LoadScene(FallbackLobbySceneName);
    }

    private bool ShouldRecoverFromDisconnect()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(lastRoomName) || IsGameplayScene(sceneName);
    }

    private void TryStartReconnect()
    {
        if (Time.realtimeSinceStartup < nextReconnectAttemptTime)
            return;

        nextReconnectAttemptTime = Time.realtimeSinceStartup + 1.5f;

        if (!directRoomReconnectAttempted)
        {
            directRoomReconnectAttempted = true;

            if (PhotonNetwork.ReconnectAndRejoin())
            {
                Debug.Log("[Reconnect] Attempting ReconnectAndRejoin.");
                return;
            }
        }

        if (PhotonNetwork.Reconnect())
        {
            Debug.Log("[Reconnect] Attempting reconnect to master.");
        }
    }

    private void TryRejoinLastRoom()
    {
        if (Time.realtimeSinceStartup < nextReconnectAttemptTime)
            return;

        nextReconnectAttemptTime = Time.realtimeSinceStartup + 1.5f;
        PhotonNetwork.RejoinRoom(lastRoomName);
        Debug.Log($"[Reconnect] Attempting to rejoin room '{lastRoomName}'.");
    }

    private void FinishReconnect()
    {
        Debug.Log("[Reconnect] Reconnected to the room.");

        if (reconnectRoutine != null)
        {
            StopCoroutine(reconnectRoutine);
            reconnectRoutine = null;
        }

        reconnecting = false;
        directRoomReconnectAttempted = false;
    }

    private static bool IsGameplayScene(string sceneName)
    {
        return sceneName == "Game" || sceneName == "Main 2" || sceneName == "GameOver";
    }

    private void OnGUI()
    {
        if (!reconnecting)
            return;

        int secondsRemaining = Mathf.CeilToInt(
            Mathf.Max(0f, reconnectDeadline - Time.realtimeSinceStartup)
        );

        Rect backgroundRect = new Rect(0f, 0f, Screen.width, Screen.height);
        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(backgroundRect, Texture2D.whiteTexture);
        GUI.color = previousColor;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontSize = Mathf.Clamp(Screen.height / 30, 18, 36);
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;

        GUI.Label(backgroundRect, $"Connection lost\nReconnecting... {secondsRemaining}s", style);
    }
}
