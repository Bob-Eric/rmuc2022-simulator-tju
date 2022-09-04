using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace RMUC_UI {
    public struct PlayerSync {
        public int connId;          // connId of the player
        public string player_name;  // the nickname that player inputs before enterring lobby
        public bool owning_ava;   // whether taking a robot
        public string ava_tag;   // name of robot taken by the player, must be one of mainmenu.robo_names;
        public bool ready;          // whether ready to start after taking robot
    }
    /// <summary>
    /// @Orientation: define visual state and sync them; also do RPC
    /// @Style: Event/Call style
    /// </summary>
    public class NetLobby : NetworkBehaviour {
        /** Tip: nested struct declaration => Netlobby.AvatarMessage instead of AvatarMessage.
            direction: client -> server
         */
        public struct AvatarMessage : NetworkMessage {
            public string robot_s;
            public string player_name;
            /* construct function with params */
            public AvatarMessage(string robot_s, string player_name) {
                this.robot_s = robot_s;
                this.player_name = player_name;
            }
        }

        public struct AvaStateMessage : NetworkMessage {
            public bool ready;
        }

        /// <summary>
        /// Network Variables:
        public readonly SyncList<PlayerSync> playerSyncs = new SyncList<PlayerSync>();
        [SyncVar]
        public int owner_connId; // lobby owner
        [SyncVar]
        public bool allow_join;
        /// </summary> 

        /// <summary>
        /// Local Variables
        public MainMenu mainmenu;
        /// </summary>

        public override void OnStartClient() {
            base.OnStartClient();
            playerSyncs.Callback += OnPlayerSyncChanged;
        }
        public override void OnStartServer() {
            base.OnStartServer();
            playerSyncs.Reset();
            /** ApplyAvatar => Action<AvatarMessage>, 
                which tells me that usage of ApplyAvatar should be declared as ApplyAvatar(AvatarMessage mes) */
            NetworkServer.RegisterHandler<AvatarMessage>(OnApplyAvatar);
            NetworkServer.RegisterHandler<AvaStateMessage>(OnInvAvaReady);
        }

        [Server]
        public void OnApplyAvatar(NetworkConnectionToClient conn, AvatarMessage mes) {
            bool is_avatar_taken = (-1 != playerSyncs.FindIndex(i => i.owning_ava && i.ava_tag == mes.robot_s));
            if (is_avatar_taken)
                Debug.Log("server: the robot is taken!");
            else {
                /* make a new PlayerStateSync */
                PlayerSync tmp = new PlayerSync();
                tmp.connId = conn.connectionId;
                tmp.player_name = mes.player_name;
                tmp.owning_ava = true;
                tmp.ready = true;
                tmp.ava_tag = mes.robot_s;
                /* find player info => 
                    case1: the player just join and hasn't been added to player_sync_all.
                    case2: the player has been added.
                */
                Debug.Log("start to find index");
                int idx = playerSyncs.FindIndex(i => i.connId == conn.connectionId);
                if (idx == -1) {
                    playerSyncs.Add(tmp);
                } else
                    playerSyncs[idx] = tmp;
            }
        }

        [Server]
        public void OnInvAvaReady(NetworkConnectionToClient conn, AvaStateMessage mes) {
            int id_cli = conn.connectionId;
            int syncIdx = playerSyncs.FindIndex(i => i.connId == id_cli);
            if (syncIdx == -1) {
                Debug.Log("A client PC owning no avatar trys to change avatar state");
                return;
            } else {
                PlayerSync ps = new PlayerSync();
                ps = playerSyncs[syncIdx];
                ps.ready = mes.ready;
                playerSyncs[syncIdx] = ps;
            }
            return;
        }

        [Client]
        void OnPlayerSyncChanged(SyncList<PlayerSync>.Operation op, int index, PlayerSync oldval, PlayerSync newval) {
            /* if a new value is added to synclist, then 
                @oldval: new PlayerSync()
                @newval: newly-added object (PlayerSync) 
                @index: newval's index in synclist
            */
            /* step 1: reset old avatar */
            if (oldval.ava_tag != null) {
                int avaIdx = mainmenu.ava_tags.FindIndex(tag => tag == oldval.ava_tag);
                mainmenu.avatars[avaIdx].ResetAvatarTab();
            }
            /* step 2: set corresponding AvatarTab according to newval  */
            if (newval.ava_tag != null) {
                int avaIdx = mainmenu.ava_tags.FindIndex(tag => tag == newval.ava_tag);
                mainmenu.avatars[avaIdx].SetAvatarTab(newval);
            }
            int syncIdx = this.playerSyncs.FindIndex(p => p.connId == NetworkClient.connection.connectionId);
            if (syncIdx != -1) {
                mainmenu.owning_ava = playerSyncs[syncIdx].owning_ava;
                mainmenu.ava_ready = playerSyncs[syncIdx].ready;
                mainmenu.SetButtonReady();
            }
            /* log in every client */
            Debug.Log("avatar change!");
        }
    }
}