using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

using LobbyUI;

public class BattleNetworkManager : NetworkManager {
    public new static BattleNetworkManager singleton;
    public NetLobby net_lob;
    public MainMenu mainmenu;
    [Scene]
    public string scn_field;
    [Scene]
    public string scn_lobby;
    /* used to transfer data when scene loads */
    public List<PlayerSync> playerSyncs;


    public override void Awake() {
        if (singleton == null) {
            singleton = this;
        } else
            Destroy(this.gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }


    bool last_cli_connected;
    public override void Update() {
        base.Update();
        last_cli_connected = NetworkClient.isConnected;
    }


    bool ScnCmp(string scn_runtime, string scn_asset) {
        return scn_asset.Contains(scn_runtime);
    }

    public bool isScnLobby() => ScnCmp(SceneManager.GetActiveScene().name, scn_lobby);
    public bool isScnField() => ScnCmp(SceneManager.GetActiveScene().name, scn_field);

    public override void OnStartServer() {
        base.OnStartServer();

        /* clear playerSyncs, otherwise, previous items are there */
        this.playerSyncs = new List<PlayerSync>();

        NetworkServer.RegisterHandler<NetLobby.AvaOwnMessage>(OnRecAvaOwnMesWrapUp);
        NetworkServer.RegisterHandler<NetLobby.AvaReadyMessage>(OnRecAvaReadyMesWrapUp);
        NetworkServer.RegisterHandler<NetLobby.StartGameMessage>(OnRecStartGameMesWrapUp);
    }

    public override void OnStopServer() {
        base.OnStopServer();
        if (mainmenu != null)
            mainmenu.SetPlayerMode();
    }

    /* called on server when a client is connected to server */
    public override void OnServerConnect(NetworkConnectionToClient conn) {
        base.OnServerConnect(conn);
        Debug.Log("Hey, there! A client connects. ConnId: " + conn.connectionId);
        if (net_lob != null && !net_lob.allow_join) {
            conn.Disconnect();
            return;
        }
        if (isScnLobby()) {
            conn.Send<NetLobby.ClientIdMessage>(new NetLobby.ClientIdMessage(conn.connectionId));
        }
    }


    /* called on server when a client is disconnected */
    public override void OnServerDisconnect(NetworkConnectionToClient conn) {
        base.OnServerDisconnect(conn);
        if (isScnLobby())
            net_lob.OnPlayerLeave(conn);
        else if (isScnField()) {
            int idx = this.playerSyncs.FindIndex(i => i.connId == conn.connectionId);
            if (idx != -1)
                this.playerSyncs.RemoveAt(idx);
        }
    }


    public override void OnServerSceneChanged(string sceneName) {
        base.OnServerSceneChanged(sceneName);
        Debug.Log(sceneName + " has been loaded");
        if (isScnField()) {
            string log = "Player <=> Robot: \n";
            /* BattleField having been loaded, assign robot instance to avatar owner */
            foreach (RoboState robot in BattleField.singleton.robo_all) {
                int syncIdx = playerSyncs.FindIndex(i => i.ava_tag == robot.name);
                if (syncIdx == -1)
                    continue;
                NetworkConnectionToClient connToClient = NetworkServer.connections[playerSyncs[syncIdx].connId];
                robot.GetComponent<NetworkIdentity>().AssignClientAuthority(connToClient);
                log += playerSyncs[syncIdx].player_name + " takes " + robot.name + "\n";
            }
            Debug.Log(log);
        }
    }


    public override void OnClientConnect() {
        base.OnClientConnect();
        if (mainmenu == null || net_lob == null)
            return;
        mainmenu.SetPlayerLobby();
        NetworkClient.RegisterHandler<NetLobby.ClientIdMessage>(OnRecCliIdMesWrapUp);
        NetworkClient.RegisterHandler<NetLobby.SceneTransMessage>(OnRecScnTransMesWrapUp);

        /* when first joining, 1. send fake AvaMes to register
            2. init RoboTabs by playerSyncs that pulled from server PC */
        NetworkClient.Send<NetLobby.AvaOwnMessage>(new NetLobby.AvaOwnMessage(NetLobby.NULLAVA, mainmenu.input_info.text));
    }


    /* called on that client when a client is stopped (disconnected included) */
    public override void OnStopClient() {
        base.OnStopClient();
        if (NetworkClient.localPlayer != null)
            Destroy(NetworkClient.localPlayer.gameObject);

        if (last_cli_connected) {
            // clear what OnClientConnect() has done:
            if (isScnField())
                SceneManager.LoadScene(scn_lobby);
            else if (isScnLobby())
                mainmenu.SetPlayerMode();
        } else {
            // mainmenu's attempting to connect but fail
            // call mainmenu to do finishing touches 
            mainmenu.OnCancelJoin();
        }

        Debug.Log("client PC: stop client");
    }


    // since BattleNetworkManager singleton won't be destroyed when scene changes 
    // but this.net_lob and this.mainmenu will be destroyed
    // so every time new scene is loaded, singleton should find net_lob and mainmenu that is newly added
    void OnSceneLoaded(Scene scn, LoadSceneMode mode) {
        this.net_lob = FindObjectOfType<NetLobby>(includeInactive: true);
        this.mainmenu = FindObjectOfType<MainMenu>(includeInactive: true);
    }


    // Wrap-up function for handler and callback:
    // net_lob will be destroyed together with Lobby Scene. RegisterHandler(net_lob.xxx) is illegal when switch back to 
    // Lobby because `net_lob` is different and the former one that is registered in handler is gone. However, 
    // `BattleNetworkManager.singleton` is static and marked as `DontDestroyOnLoad`. Therefore, wrap up `net_lob.xxx` by `singleton`
    void OnRecCliIdMesWrapUp(NetLobby.ClientIdMessage mes) => net_lob.OnRecCliIdMes(mes);
    void OnRecScnTransMesWrapUp(NetLobby.SceneTransMessage mes) => net_lob.OnRecScnTransMes(mes);
    void OnRecAvaOwnMesWrapUp(NetworkConnectionToClient conn, NetLobby.AvaOwnMessage mes)
        => net_lob.OnRecAvaOwnMes(conn, mes);
    void OnRecAvaReadyMesWrapUp(NetworkConnectionToClient conn, NetLobby.AvaReadyMessage mes)
        => net_lob.OnRecAvaReadyMes(conn, mes);
    void OnRecStartGameMesWrapUp(NetworkConnectionToClient conn, NetLobby.StartGameMessage mes)
        => net_lob.OnRecStartGameMes(conn, mes);
}