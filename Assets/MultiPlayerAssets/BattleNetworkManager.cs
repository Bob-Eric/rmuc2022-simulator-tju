using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class BattleNetworkManager : NetworkManager {
    public RMUC_UI.NetLobby net_lob;
    public RMUC_UI.MainMenu mainmenu;
    [Scene]
    public string bat_field;

    /* used to transfer data when scene loads */
    [HideInInspector]
    public List<RMUC_UI.PlayerSync> playerSyncs = new List<RMUC_UI.PlayerSync>();

    public override void OnStartServer() {
        base.OnStartServer();
        NetworkServer.RegisterHandler<RMUC_UI.NetLobby.AvatarMessage>(net_lob.OnApplyAvatar);
        NetworkServer.RegisterHandler<RMUC_UI.NetLobby.AvaStateMessage>(net_lob.OnInvAvaReady);
        NetworkServer.RegisterHandler<RMUC_UI.NetLobby.StartGameMessage>(net_lob.OnStartGame);
        /* clear playerSyncs, otherwise, previous items are there */
        net_lob.playerSyncs.Reset();
    }
   
    /* called on server when a client is connected to server */
    /// <summary>
    /// here is to verify
    /// </summary>
    public override void OnServerConnect(NetworkConnectionToClient conn) {
        base.OnServerConnect(conn);
        Debug.Log("Hey, there! A client connects. ConnId: " + conn.connectionId);
        conn.Send<RMUC_UI.NetLobby.ClientIdMessage>(new RMUC_UI.NetLobby.ClientIdMessage(conn.connectionId));
    }

    /* called on server when a client is disconnected */
    public override void OnServerDisconnect(NetworkConnectionToClient conn) {
        base.OnServerDisconnect(conn);
        Debug.Log("Hey, there! A client disconnects. ConnId: " + conn.connectionId);
        net_lob.OnPlayerLeave(conn);
    }
 
    public override void OnClientConnect() {
        base.OnClientConnect();
        net_lob.playerSyncs.Callback += net_lob.OnPlayerSyncChanged;
        NetworkClient.RegisterHandler<RMUC_UI.NetLobby.ClientIdMessage>(net_lob.OnReceiveConnId);
        Debug.Log("register handler in net_man");
    }

    /* called on that client when a client is disconnected */
    public override void OnClientDisconnect() {
        base.OnClientDisconnect();
        if (SceneManager.GetActiveScene().name == bat_field) {
            net_lob.playerSyncs.Callback -= net_lob.OnPlayerSyncChanged;
            mainmenu.SetPlayerOpt();
        }
    }

    public override void OnServerSceneChanged(string sceneName) {
        base.OnServerSceneChanged(sceneName);
        Debug.Log(sceneName + " has been loaded");

        /* BattleField having been loaded, assign robot instance to avatar owner */
        foreach (RobotState robot in BattleField.singleton.robo_red) {
            int syncIdx = playerSyncs.FindIndex(i => i.ava_tag == robot.name);
            if (syncIdx == -1)
                Debug.Log("no player takes " + robot.name);
            else {
                NetworkConnectionToClient connToClient = NetworkServer.connections[playerSyncs[syncIdx].connId];
                robot.GetComponent<NetworkIdentity>().AssignClientAuthority(connToClient);
                Debug.Log(playerSyncs[syncIdx].player_name + " takes " + robot.name);
            }
        }
        foreach (RobotState robot in BattleField.singleton.robo_blue) {
            int syncIdx = playerSyncs.FindIndex(i => i.ava_tag == robot.name);
            if (syncIdx == -1)
                Debug.Log("no player takes " + robot.name);
            else {
                NetworkConnectionToClient connToClient = NetworkServer.connections[playerSyncs[syncIdx].connId];
                robot.GetComponent<NetworkIdentity>().AssignClientAuthority(connToClient);
                Debug.Log(playerSyncs[syncIdx].player_name + " takes " + robot.name);
            }
        }
    }

    public override void OnClientSceneChanged() {
        base.OnClientSceneChanged();
        
    }

}