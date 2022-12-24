using UnityEngine;
using Mirror;

public enum RuneBuff { None, Junior, Senior };

/* Motion control and light control */
public class Rune : MonoBehaviour {
    /* external reference */
    public Transform rotator_rune;
    public RuneState rune_state_red;
    public RuneState rune_state_blue;
    public Transform[] mines_gold;
    public Transform[] mines_silv;
    public Activation activ;
    public ArmorColor rune_color;  // only works when rune's activated
    public bool disabled;       // rune'll be disabled for 30sec

    private const int jun_sta = 1;    // rune_junior starts
    private const int jun_end = 2;   // rune_junior ends
    private const int sen_sta = 10;   // rune_senior starts
    private const int sen_end = 420;   // rune_senior ends
    private const int mine_1_3 = 15;
    private const int mine_0_4 = 180;
    private const int mine_2 = mine_0_4 + 5;
    private RuneBuff rune_buff;
    private float a, w, t;
    private int sgn;


    /** use Init() instead of Start() to init instance of Rune, 
        so that instances of Rune, RuneState and RuneBlade are initialized in certain and correct order.
        
        Otherwise, maybe RuneBlade is initialized before RuneState, thereby causing null sharedMaterial of Renderer
     */
    public void Init() {
        rune_state_red.Init();
        rune_state_blue.Init();
        activ = Activation.Idle;
        SetRnSta();
        sgn = Random.Range(0f, 1f) > 0.5 ? 1 : -1;
        Reset();
        this.disabled = false;

        // for (int i = 0; i < mines_gold.Length; i++)
        // mines_gold[i].GetComponent<Rigidbody>().useGravity = false;
        for (int i = 0; i < mines_silv.Length; i++)
            mines_silv[i].transform.parent = null;
    }


    // Set rune state's activation by this.activ
    void SetRnSta() {
        rune_state_red.SetActiveState(this.activ);
        rune_state_blue.SetActiveState(this.activ);
    }


    bool[] dropped = new bool[] {false, false, false};
    void Update() {
        float t_bat = BattleField.singleton.GetBattleTime();
        if (t_bat > mine_1_3 && !dropped[0]) {
            DropMine(1);
            DropMine(3);
            dropped[0] = true;
        } else if (t_bat > mine_0_4 && !dropped[1]) {
            DropMine(0);
            DropMine(4);
            dropped[1] = true;
        } else if (t_bat > mine_2 && !dropped[2]) {
            DropMine(2);
            dropped[2] = true;
        }

        /* only server PC needs to calc rotation and activ state; client PC only syncs */
        if (!NetworkServer.active)
            return;

        /* rune has been activated => no spinning */
        if (this.activ == Activation.Activated || disabled)
            return;

        /* time for rune_junior */
        if (t_bat >= jun_sta && t_bat <= jun_end) {
            this.rune_buff = RuneBuff.Junior;
            this.activ = Activation.Ready;
            RuneSpin();
        }
        /* time for rune_senior */
        else if (t_bat >= sen_sta && t_bat <= sen_end) {
            this.rune_buff = RuneBuff.Senior;
            this.activ = Activation.Ready;
            RuneSpin();
        }
        /* rune is not available => set light to idle, no spinning */
        else {
            this.activ = Activation.Idle;
        }
        SetRnSta();
    }


    void DropMine(int mineIdx) {
        mines_gold[mineIdx].parent = null;
        mines_gold[mineIdx].gameObject.AddComponent<Rigidbody>();
    }


    public void ActivateRune(ArmorColor armor_color) {
        StartCoroutine(BattleField.singleton.ActivateRune(armor_color, rune_buff));
        if (armor_color == ArmorColor.Red) {
            /* rune_state_red's all blade has been turned on during hitting */
            rune_state_blue.SetActiveState(Activation.Idle);
        } else {
            rune_state_red.SetActiveState(Activation.Idle);
        }
    }


    void RuneSpin() {
        float spd = this.rune_buff == RuneBuff.Junior ? 60 : (this.a * Mathf.Sin(this.w * t) + 2.09f - this.a) * Mathf.Rad2Deg;
        rotator_rune.localEulerAngles += sgn * new Vector3(0, 0, spd * Time.deltaTime);
        this.t += Time.deltaTime;
    }


    /* Reset Activation State to false and spin params to a new set of values */
    public void Reset() {
        this.activ = Activation.Idle;
        SetRnSta();
        this.t = 0;
        this.a = Random.Range(0.78f, 1.045f);
        this.w = Random.Range(1.884f, 2f);
    }


    /* target spinning with rune, return its position after a given interval */
    public Vector3 PredPos(Vector3 target, float interval) {
        Vector3 center = rotator_rune.transform.position;
        Vector3 offset = target - center;
        float ang = 0;
        if (rune_buff == RuneBuff.Junior) {
            ang = sgn * 60 * interval;
        } else if (rune_buff == RuneBuff.Senior) {
            ang = sgn * Mathf.Rad2Deg * (this.a / this.w * (Mathf.Cos(this.w * this.t) - Mathf.Cos(this.w * (this.t + interval)))
                + (2.09f - this.a) * interval);
        } else {
            Debug.Log("rune's not spinning, no prediction");
            return target;
        }
        Vector3 axis = rotator_rune.transform.forward;
        Vector3 offset_new = Quaternion.AngleAxis(ang, axis) * offset;
        return center + offset_new;
    }
}
