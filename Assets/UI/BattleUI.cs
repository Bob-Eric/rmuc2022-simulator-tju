using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

namespace RMUC_UI {
    public class BattleUI : MonoBehaviour {
        [Header("Battle Status")]
        public Notepad notepad;
        public RoboTab[] robotabs;

        public BloodBar baseStat_red;
        public BloodBar baseStat_blue;

        public RoboTab otptStat_red;
        public RoboTab otptStat_blue;

        [Header("Settings UI")]
        /* supply UI */
        public GameObject supp_ui;
        /* setting UI */
        public GameObject pref_ui;

        [Header("Owned robot profile")]
        public HeatRing hr;
        public Image overheat_bg;
        public float rat_heat = 0;  // updated by robocontroller every frame
        public TMP_Text txt_bullspd;
        public TMP_Text txt_bullnum;

        public Image img_cap;
        public Image img_exp;
        public TMP_Text txt_cap;
        public TMP_Text txt_exp;
        public float rat_cap;

        /* atk, cldn, rev, dfc, snp, lea */
        public int[] indic_buf = new int[6];
        public Image[] imgs_buf;

        RMUC_UI.RoboTab my_robotab;
        RoboState myrobot => BattleField.singleton.robo_local;



        void Start() {
            /* init imgs_buf */
            for (int i = 0; i < indic_buf.Length; i++)
                indic_buf[i] = -1;
        }


        void Update() {
            if (Input.GetKeyDown(KeyCode.Escape)) {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None
                    : CursorLockMode.Locked;
            }

            if (Input.GetKeyDown(KeyCode.O)) {
                this.supp_ui.SetActive(!this.supp_ui.activeSelf);
                Cursor.lockState = this.supp_ui.activeSelf ? CursorLockMode.None : CursorLockMode.Locked;
            }

            if (Input.GetKeyDown(KeyCode.BackQuote)) {
                /* preference UI */
                this.pref_ui.SetActive(!this.pref_ui.activeSelf);
                Cursor.lockState = this.pref_ui.activeSelf ? CursorLockMode.None : CursorLockMode.Locked;
            }
        }


        public void Push(UISync uisync) {
            SetNotePad(uisync.bat_sync);

            for (int i = 0; i < robotabs.Length; i++) {
                robotabs[i].Push(uisync.robots[i]);
            }
            SetBase(baseStat_red, uisync.bs_r);
            SetBase(baseStat_blue, uisync.bs_b);

            otptStat_red.Push(uisync.os_r);
            otptStat_blue.Push(uisync.os_b);

            SetMyUI();
        }


        int last_money_red;
        int last_money_red_max;
        int last_money_blue;
        int last_money_blue_max;
        void SetNotePad(BatSync bs) {
            // todo: set score
            notepad.DispTime(7 * 60 - bs.time_bat);
            if (bs.money_red != last_money_red || bs.money_blue != last_money_blue
                || bs.money_red_max != last_money_red_max || bs.money_blue_max != last_money_blue_max) {
                notepad.DispMoney();
                last_money_red = bs.money_red;
                last_money_red_max = bs.money_red_max;
                last_money_blue = bs.money_blue;
                last_money_blue_max = bs.money_blue_max;
            }
        }


        void SetBase(BloodBar baseStat, BaseSync bs) {
            baseStat.SetInvulState(bs.invul);
            baseStat.SetBlood(bs.currblood / 5000f);
            baseStat.SetShield(bs.shield / 500f);
        }


        // called every frame
        bool init = false;
        void SetMyUI() {
            if (myrobot == null)
                return;
            if (!init) {
                string color = myrobot.armor_color == ArmorColor.Red ? "red" : "blue";
                foreach (RoboTab rt in GetComponentsInChildren<RoboTab>(includeInactive: true)) {
                    if (rt.name.ToLower().Contains("my")) {
                        // Debug.Log("rt.name: " + rt.name);
                        if (!rt.name.ToLower().Contains(color)) {
                            rt.gameObject.SetActive(false);
                            continue;
                        }
                        rt.gameObject.SetActive(true);
                        my_robotab = rt;    // store reference

                        // get robotab's img_ava's index
                        int idx = 0; var tp = myrobot.GetType();
                        if (tp == typeof(HeroState)) idx = 0;
                        else if (tp == typeof(EngineerState)) {
                            txt_cap.gameObject.SetActive(false);
                            img_cap.gameObject.SetActive(false);
                            txt_exp.gameObject.SetActive(false);
                            img_exp.gameObject.SetActive(false);
                            idx = 1;
                        } else if (tp == typeof(InfantryState)) idx = 2;
                        else Debug.Log("wrong type of myrobot: " + tp);
                        // set robotab's img_ava
                        my_robotab.img_ava.sprite = my_robotab.imgs_team[idx];
                    }
                }
                int my_roboidx = BattleField.singleton.robo_all.FindIndex(i => i == myrobot);
                foreach (TMP_Text txt in my_robotab.GetComponentsInChildren<TMP_Text>())
                    if (txt.gameObject.name.ToLower().Contains("idx")) {
                        txt.text = (my_roboidx % 5 + 1).ToString();
                        // Debug.Log("set my robot index");
                    }
                init = true;
            }
            /* update heat, bullnum and speed */
            hr.SetHeat(rat_heat);
            if (rat_heat > 1)
                overheat_bg.gameObject.SetActive(true);
            else
                overheat_bg.gameObject.SetActive(false);
            Weapon weap;
            if (myrobot.TryGetComponent<Weapon>(out weap)) {
                txt_bullnum.text = weap.bullnum.ToString();
                txt_bullspd.text = weap.bullspd.ToString();
            }
            /* update robotab */
            my_robotab.Push(myrobot.Pull());
            my_robotab.bld_bar.DispBldTxt(myrobot.currblood, myrobot.maxblood);
            SetMyBuff();
            if (myrobot.GetComponent<RoboController>() != null) {
                if (myrobot.maxexp != int.MaxValue) {
                    txt_exp.text = string.Format("{0} / {1} Exp.", myrobot.currexp, myrobot.maxexp);
                    img_exp.fillAmount = (float)myrobot.currexp / myrobot.maxexp;
                } else {
                    txt_exp.text = string.Format("Max Exp");
                    img_exp.fillAmount = 1;
                }
                txt_cap.text = string.Format("{0:N1}% Cap.", rat_cap * 100);
                img_cap.fillAmount = rat_cap;
            }
        }


        void SetMyBuff() {
            SetMyBuffAt(0, AssetManager.singleton.img_atk);
            SetMyBuffAt(1, AssetManager.singleton.img_cldn);
            SetMyBuffAt(2, AssetManager.singleton.img_rev);
            SetMyBuffAt(3, AssetManager.singleton.img_dfc);
            SetMyBuffAt(4, null);
            SetMyBuffAt(5, null);
        }
        void SetMyBuffAt(int idx, Sprite[] spr) {
            imgs_buf[idx].gameObject.SetActive(indic_buf[idx] != -1);
            if (spr != null && indic_buf[idx] >= 0 && indic_buf[idx] < spr.Length)
                imgs_buf[idx].sprite = spr[indic_buf[idx]];
        }


        public void SetBullSupply(int num) {
            TMP_InputField tif = supp_ui.GetComponentInChildren<TMP_InputField>();
            tif.text = num.ToString();
        }


        public void CallBullSupply() {
            if (myrobot == null)
                return;
            RoboController rc = myrobot.GetComponent<RoboController>();
            int bull_num;
            if (rc != null && int.TryParse(supp_ui.GetComponentInChildren<TMP_InputField>().text, out bull_num)) {
                /* position check */
                bool in_supp_spot = rc.robo_state.robo_buff.FindIndex(i => i.tag == BuffType.rev) != -1;
                if (!in_supp_spot) {
                    Debug.Log("Cannot call supply: not in supply spot");
                    return;
                }
                /* money check */
                int money_req = bull_num * (rc.GetComponent<Weapon>().caliber == Caliber._17mm ? 1 : 15);
                int money_now = rc.GetComponent<RoboState>().armor_color == ArmorColor.Red ?
                    BattleField.singleton.money_red : BattleField.singleton.money_blue;
                if (money_now < money_req) {
                    Debug.Log("no sufficient money");
                    return;
                }
                rc.CmdSupply(rc.gameObject.name, bull_num);
                supp_ui.SetActive(false);
                Cursor.lockState = CursorLockMode.Locked;
                Debug.Log(rc.gameObject.name + " calls supply: " + bull_num);
            }
        }


        public void ReturnLobby() {
            NetworkManager net_man = GameObject.FindObjectOfType<NetworkManager>();
            if (NetworkServer.active && NetworkClient.active)
                net_man.StopHost();
            else if (NetworkClient.active)
                net_man.StopClient();
            else if (NetworkServer.active)
                net_man.StopServer();
        }
    }
}