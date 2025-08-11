using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class BattleField : MonoBehaviour {
    public static BattleField singleton { get; private set; }

    /// <summary>
    /// Game Params
    /// </summary>
    public int money_red;
    public int money_red_max;
    public int money_blue;
    public int money_blue_max;
    public int score_red;
    public int score_blue;

    public const int length = 30;
    public const int width = 16;
    public const int height = 4;
    public bool had_first_blood;
    public bool game_started { get; private set; }

    /// <summary>
    /// External reference
    /// </summary>
    public RMUC_UI.BattleUI bat_ui;
    public OutpostState outpost_blue;
    public OutpostState outpost_red;
    public BaseState base_blue;
    public BaseState base_red;
    public GuardState guard_red;
    public GuardState guard_blue;
    public Rune rune;

    /* hero engineer infantry1 infantry2 */
    public RoboState[] robo_red;
    public RoboState[] robo_blue;
    public SyncNode sync_node;

    public List<Renderer> lightbars_blue_outpost;
    public List<Renderer> lightbars_blue_guard;
    public List<Renderer> lightbars_red_outpost;
    public List<Renderer> lightbars_red_guard;

    /// <summary>
    /// Automatically set
    /// </summary>
    public List<RoboState> robo_all = new();
    public List<BasicState> team_all = new();
    public RoboState robo_local;


    /* priority (with NetworkIdentity): Instantiate > Awake() > OnStartServer() (obviously, iff in server PC) 
        ----Spawn----> OnStartClient() (obviously, iff in client PC) > Start()    
    */
    void Awake() {
        if (singleton == null) {
            singleton = this;
        } else
            Destroy(gameObject);
        robo_all.AddRange(robo_red);
        robo_all.AddRange(robo_blue);

        team_all.AddRange(robo_all);
        team_all.Add(guard_red);
        team_all.Add(guard_blue);
        team_all.Add(outpost_red);
        team_all.Add(outpost_blue);
    }


    void Start() {
        StartCoroutine(StartGame());
    }


    void FixedUpdate() {
        t_bat += Time.fixedDeltaTime;
        if (GetBattleTime() > 420f || !base_blue.survival || !base_red.survival)
            EndGame();
    }


    // reset game params, such as money, started_game, etc; score won't be reset
    void ResetParam() {
        money_red = 0;
        money_red_max = 0;
        money_blue = 0;
        money_blue_max = 0;
        had_first_blood = false;
        game_started = false;
        game_ending = false;
    }


    IEnumerator StartGame() {
        // a short time for both team to prepare for the game. (change position, look around, etc.)
        ResetParam();
        t_bat = -GameSetting.singleton.prepare_sec - 6;

        yield return new WaitForSeconds(GameSetting.singleton.prepare_sec);

        AssetManager.singleton.StopClip(AssetManager.singleton.prepare);
        AssetManager.singleton.PlayClipAround(AssetManager.singleton.cntdown);

        yield return new WaitForSeconds(6);

        rune.Init();
        if (robo_local != null) {
            var rc = robo_local.GetComponent<RoboController>();
            if (rc != null) {
                /* when game starts, dropdowns become non-interactable and captions of dropdowns in pref_ui submit */
                bat_ui.SetRoboPrefDrop(false);
                string pref_chas = bat_ui.drop_chas.captionText.text;
                string pref_turr = bat_ui.drop_turr.captionText.text;
                robo_local.GetUserPref(pref_chas, pref_turr);
                robo_local.Configure();
                rc.CmdUserPref(rc.gameObject.name, pref_chas, pref_turr);
            }
        }
        AssetManager.singleton.PlayClipAround(AssetManager.singleton.gamebg, true, 0.3f);
        AllAddMoney(200);
        StartCoroutine(DistribMoney());
        game_started = true;
    }


    [Header("draw-redwin-bluewin")]
    [SerializeField] Animator[] anims_win;
    bool game_ending;
    public void EndGame() {
        if (game_ending)
            return;
        game_ending = true;

        int rlt = 0; // 0: draw; 1: red win; 2: blue win
        int[] blood_diff = new int[3] {outpost_red.currblood - outpost_blue.currblood,
            guard_red.currblood - guard_blue.currblood, // TODO: add guard state and put guard blood difference here
            base_red.currblood - base_blue.currblood};
        for (int i = 0; i < blood_diff.Length; i++) {
            if (blood_diff[i] != 0) {
                rlt = blood_diff[i] > 0 ? 1 : 2;
                break;
            }
        }

        if (anims_win != null && rlt < anims_win.Length) {
            anims_win[rlt].gameObject.SetActive(true);
        }

        AssetManager.singleton.StopClip(AssetManager.singleton.gamebg);
        AssetManager.singleton.PlayClipAround(AssetManager.singleton.gameend, false, 0.7f);

        net_man.StartCoroutine(EndGameTrans());
    }


    BattleNetworkManager net_man => BattleNetworkManager.singleton;
    IEnumerator EndGameTrans() {
        // wait for a while such that `anims_win` ends playing
        yield return new WaitForSeconds(10);
        SceneTransit.singleton.StartTransit();
        // wait for a while such that SceneTransit.singleton ends playing
        yield return new WaitForSeconds(5);
        // server PC change scene to `scn_lobby`
        net_man.ServerChangeScene(net_man.scn_lobby);
    }


    float t_bat = 0;
    public float GetBattleTime() => t_bat;


    public RoboState GetRobot(string robot_s) {
        RoboState tmp = robo_all.Find(i => i.name == robot_s);
        if (tmp == null) {
            // Debug.Log(robot_s + " : " + GameObject.Find(robot_s));
            tmp = GameObject.Find(robot_s).GetComponent<RoboState>();
        }
        return tmp;
    }


    public bool OnField(GameObject obj) {
        const int x_half_length = 16;
        const int y_half_length = 10;
        const int z_half_length = 10;
        Vector3 rel_pos = obj.transform.position - transform.position;
        return Mathf.Abs(rel_pos.x) < x_half_length && Mathf.Abs(rel_pos.y) < y_half_length
            && Mathf.Abs(rel_pos.z) < z_half_length;
    }


    Dictionary<BasicState, int> killnum = new();
    public void Kill(GameObject hitter, GameObject hittee) {
        Debug.Log(hitter.name + " slays " + hittee.name);

        BasicState hittee_state = hittee.GetComponent<BasicState>();

        /* a team's base is lost and game ends */
        if (hittee_state is BaseState) {
            EndGame();
            return;
        }

        bool is_red = hittee_state.armor_color == ArmorColor.Red;
        BaseState bs = is_red ? base_red : base_blue;
        GuardState gs = is_red ? guard_red : guard_blue;
        /* a team's outpost is lost and its base becomes vulnerable */
        if (hittee_state is OutpostState) {
            gs.invul = false;
            gs.SetInvulLight(false);
            bs.invul = false;
            bs.SetInvulLight(false);
            bs.shield = 500;
            foreach (Renderer ren in is_red ? lightbars_red_outpost : lightbars_blue_outpost)
                ren.material = AssetManager.singleton.light_off;
        }
        /* a team's guard is lost and its base opens shells */
        if (hittee_state is GuardState) {
            bs.GetComponent<Base>().OpenShells(true);
            bs.shield = 0;
            foreach (Renderer ren in is_red ? lightbars_red_guard : lightbars_blue_guard)
                ren.material = AssetManager.singleton.light_off;
        }

        BasicState hitter_state = hitter.GetComponent<BasicState>();
        AudioClip ac;
        // teammate's killed
        if (hittee_state.armor_color == robo_local.armor_color) {
            if (hittee_state == robo_local) {
                AssetManager.singleton.PlayClipAtPoint(AssetManager.singleton.robo_die, robo_local.transform.position);
                ac = AssetManager.singleton.self_die;
            } else
                ac = AssetManager.singleton.ally_die;
        }
        // enemy's killed but not by teammate
        else if (hitter_state.armor_color != robo_local.armor_color)
            ac = AssetManager.singleton.kill[0];
        // enemy's killed by teammate
        else {
            if (!killnum.ContainsKey(hitter_state))
                killnum.Add(hitter_state, 0);
            else
                killnum[hitter_state]++;
            int idx = killnum[hitter_state] % AssetManager.singleton.kill.Length;
            ac = AssetManager.singleton.kill[idx];
        }
        AssetManager.singleton.PlayClipAround(ac);
        bat_ui.brdcst.EnqueueKill(hitter, hittee);
    }


    public IEnumerator ActivateRune(ArmorColor armor_color, RuneBuff rune_buff) {
        if (NetworkServer.active) {
            sync_node.RpcActivateRune(armor_color, rune_buff);
        }
        AssetManager.singleton.PlayClipAround(AssetManager.singleton.rune_activ);
        if (rune_buff == RuneBuff.None)
            Debug.LogError("Error: activate RuneBuff.None");
        rune.activ = Activation.Activated;
        rune.rune_color = armor_color;
        AddRuneBuff(armor_color, rune_buff);
        yield return new WaitForSeconds(45);

        RmRuneBuff(armor_color, rune_buff);
        rune.Reset();
        /* reset rune.activ and motion params */
        rune.disabled = true;
        yield return new WaitForSeconds(30);
        rune.disabled = false;
    }


    public void XchgMine(ArmorColor armor_color, bool is_gold) {
        Debug.Log("team " + armor_color + " xchg mine");
        int d_mon = is_gold ? 300 : 100;
        if (armor_color == ArmorColor.Red) {
            money_red_max += d_mon;
            money_red += d_mon;
        } else {
            money_blue_max += d_mon;
            money_blue += d_mon;
        }

        bat_ui.notepad.DispXchgMine(armor_color, is_gold);
    }


    BatSync tmp = new BatSync();
    public BatSync Pull() {
        tmp.time_bat = GetBattleTime();
        tmp.money_red = money_red;
        tmp.money_red_max = money_red_max;
        tmp.money_blue = money_blue;
        tmp.money_blue_max = money_blue_max;
        tmp.score_red = score_red;
        tmp.score_blue = score_blue;
        return tmp;
    }


    public void Push(BatSync tmp) {
        t_bat = tmp.time_bat;
        money_red = tmp.money_red;
        money_red_max = tmp.money_red_max;
        money_blue = tmp.money_blue;
        money_blue_max = tmp.money_blue_max;
        score_red = tmp.score_red;
        score_blue = tmp.score_blue;
    }


    /// <summary>
    /// non-API 
    /// </summary>
    void RmRuneBuff(ArmorColor armor_color, RuneBuff rune_buff) {
        float atk_up = rune_buff == RuneBuff.Junior ? 0.5f : 1f;
        float dfc_up = rune_buff == RuneBuff.Junior ? 0 : 0.5f;
        RoboState[] targets = armor_color == ArmorColor.Red ? robo_red : robo_blue;
        foreach (RoboState robot in targets) {
            robot.li_B_atk.Remove(atk_up);
            robot.li_B_dfc.Remove(dfc_up);
            robot.UpdateBuff();
        }
    }


    void AddRuneBuff(ArmorColor armor_color, RuneBuff rune_buff) {
        AssetManager.singleton.PlayClipAround(AssetManager.singleton.rune_activ);
        float atk_up = rune_buff == RuneBuff.Junior ? 0.5f : 1f;
        float dfc_up = rune_buff == RuneBuff.Junior ? 0 : 0.5f;
        RoboState[] targets = armor_color == ArmorColor.Red ? robo_red : robo_blue;
        foreach (RoboState robot in targets) {
            robot.li_B_atk.Add(atk_up);
            robot.li_B_dfc.Add(dfc_up);
            robot.UpdateBuff();
        }
    }


    IEnumerator DistribMoney() {
        for (int i = 0; i < 5; i++) {
            yield return new WaitForSeconds(60);
            AllAddMoney(100);
        }
        yield return new WaitForSeconds(60);
        AllAddMoney(200);
        yield break;
    }


    void AllAddMoney(int number) {
        money_red_max += number;
        money_red += number;
        money_blue_max += number;
        money_blue += number;
    }
}
