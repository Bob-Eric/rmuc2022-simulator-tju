using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class EngineerController : BasicController{
    [Header("Kinematic")]
    public Vector3 centerOfMass;
    [Header("turret")]
    public Transform pitch;
    [Header("order: FL-FR-BR-BL")]
    public WheelCollider[] wheelColliders;

    [Header("View")]
    public Transform robo_cam;

    [Header("Catching")]
    public Transform elev_1st;
    public Transform elev_2nd;
    public Transform arm;
    public Transform wrist;
    public Transform claw;

    [Header("Revive Card")]
    public Transform rev_card;

    private Rigidbody _rigid;
    private float pitch_ang = 0;
    private float pitch_min = -30;
    private float pitch_max = 40;
    private RoboState robo_state;


    /// <summary>
    /// non-API
    /// </summary>
    public override void OnStartClient() {
        base.OnStartClient();
        if (hasAuthority) {
            Transform tmp = Camera.main.transform;
            tmp.parent = robo_cam;
            tmp.localEulerAngles = Vector3.zero;
            tmp.localPosition = Vector3.zero;
        }
    }


    public override void OnStopClient() {
        base.OnStopClient();
        if (hasAuthority) {
            Camera.main.transform.parent = null;
            Cursor.lockState = CursorLockMode.None;
        }
    }


    void Start() {
        /* even if no authority, external reference should be inited */
        _rigid = GetComponent<Rigidbody>();
        _rigid.centerOfMass = centerOfMass;
        Cursor.lockState = CursorLockMode.Locked;
        robo_state = GetComponent<RoboState>();

        if (hasAuthority) {
            BattleField.singleton.robo_local = this.robo_state;
        }
    }


    void Update() {
        if (!hasAuthority)
            return;

        SetCursor();
        bool playing = Cursor.lockState == CursorLockMode.Locked;
        if (robo_state.survival && playing) {
            Move();
            Look();
            Catch();
            Save();
        } else {
            StopMove();
        }
        UpdateSelfUI();
    }


    RMUC_UI.BattleUI bat_ui => BattleField.singleton.bat_ui;    // alias
    void UpdateSelfUI() {
        bat_ui.indic_buf[0] = Mathf.Approximately(robo_state.B_atk, 0f) ? -1            // atk
            : Mathf.Approximately(robo_state.B_atk, 0.5f) ? 0 : 1;
        bat_ui.indic_buf[1] = Mathf.Approximately(robo_state.B_cd, 1f) ? -1             // cldn
            : Mathf.Approximately(robo_state.B_cd, 3f) ? 0 : 1;
        bat_ui.indic_buf[2] = Mathf.Approximately(robo_state.B_rev, 0f) ? -1            // rev
            : Mathf.Approximately(robo_state.B_rev, 0.02f) ? 0 : 1;
        bat_ui.indic_buf[3] = Mathf.Approximately(robo_state.B_dfc, 0f) ? -1            // dfc
            : Mathf.Approximately(robo_state.B_dfc, 0.5f) ? 0 : 1;
        HeroState hs = robo_state.GetComponent<HeroState>();
        bat_ui.indic_buf[4] = hs == null || !hs.sniping ? -1 : 0;
        bat_ui.indic_buf[5] = Mathf.Approximately(robo_state.B_pow, 0f) ? -1 : 0;       // lea
    }

    
    void SetCursor() {
        if (Input.GetKeyDown(KeyCode.Escape)) {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None
                : CursorLockMode.Locked;
        }
    }


    void StopMove() {
        foreach (var wc in wheelColliders) {
            wc.motorTorque = 0;
            wc.brakeTorque = 0.1f;
        }
    }


    const int wheel_num = 4;
    const float torque_drive = 8f;
    const float torque_spin = 2f;
    void Move() {
        bool catching = Input.GetKey(KeyCode.LeftShift);
        if (catching) {
            StopMove();
            return;
        }

        bool braking = Input.GetKey(KeyCode.X);
        bool spinning = Input.GetKey(KeyCode.E) ^ Input.GetKey(KeyCode.Q);  // exclusive or

        /* brake */
        if (braking) {
            for (int i = 0; i < wheel_num; i++) {
                wheelColliders[i].steerAngle = (45 + 90 * i) % 360 * Mathf.Deg2Rad;
                wheelColliders[i].brakeTorque = 5;
            }
            Debug.Log("braking");
            return;
        } else  // remove previous brake torque
            foreach (var wc in wheelColliders)
                wc.brakeTorque = 0;

        /* Transform */
        // Get move direction from user input
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // calc drive force
        float steer_ang = Mathf.Rad2Deg * Mathf.Atan2(h, v);
        float t1 = 0;
        if (Mathf.Abs(h) >= 1e-3 || Mathf.Abs(v) >= 1e-3)
            t1 = torque_drive;

        /* spin */
        float t2 = 0;
        if (spinning)
            t2 = (Input.GetKey(KeyCode.E) ? 1: -1) * torque_spin;

        /* get sum of force */
        float torque = 0;
        for (int i = 0; i < wheel_num; i++) {
            float ang1 = steer_ang * Mathf.Deg2Rad;
            float ang2 = (45 + 90 * i) % 360 * Mathf.Deg2Rad;
            Vector2 f1 = t1 * new Vector2(Mathf.Cos(ang1), Mathf.Sin(ang1));
            Vector2 f2 = t2 * new Vector2(Mathf.Cos(ang2), Mathf.Sin(ang2));
            Vector2 f_all = f1 + f2;
            wheelColliders[i].steerAngle = (Mathf.Rad2Deg * Mathf.Atan2(f_all.y, f_all.x));
            wheelColliders[i].motorTorque = f_all.magnitude;
            torque += wheelColliders[i].motorTorque;
        }
        if (torque < 1)   // counted as not moving
            foreach (var wc in wheelColliders)
                wc.brakeTorque = 0.1f;

        return;
    }


    void Look() {
        /* Get look dir from user input */
        float mouseY = 2 * Input.GetAxis("Mouse Y");
        pitch_ang -= mouseY;
        pitch_ang = Mathf.Clamp(pitch_ang, -pitch_max, -pitch_min);
        /* Rotate Transform "yaw" & "pitch" */
        pitch.localEulerAngles = new Vector3(pitch_ang, 0, 0);
    }


    readonly Vector3 elev_1st_start = new Vector3(0, 0, 0);
    readonly Vector3 elev_1st_end = new Vector3(0, 0, -0.24f);
    readonly Vector3 elev_2nd_start = new Vector3(0, 0, 0);
    readonly Vector3 elev_2nd_end = new Vector3(0, -0.2f, 0);
    readonly Vector3 arm_start = new Vector3(0, 0, 0);
    readonly Vector3 arm_end = new Vector3(-0.4f, 0, 0);
    readonly Vector3 wrist_fd = new Vector3(-90, 0, 0);
    readonly Vector3 wrist_bd = new Vector3(90, 0, 0);
    readonly Vector3 claw_lt = new Vector3(-0.13f, 0, 0);
    readonly Vector3 claw_rt = new Vector3(0.13f, 0, 0);
    float rat_arm = 0;
    float rat_claw = 0.5f;
    float st_wrist = 0.5f;
    float rat_wrist = 0.5f;
    float rat_elev = 0f;
    bool holding = false;
    void Catch() {
        wrist.localEulerAngles = Vector3.Lerp(wrist_fd, wrist_bd, rat_wrist); 

        bool catching = Input.GetKey(KeyCode.LeftShift);
        if (!catching)
            return ;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        /* elevate */
        bool cmd_up = Input.GetKey(KeyCode.E);
        bool cmd_dn = Input.GetKey(KeyCode.Q);
        if (cmd_up ^ cmd_dn)
            rat_elev = Mathf.Clamp01(rat_elev + (cmd_up ? Time.deltaTime : -Time.deltaTime));
        
        elev_1st.localPosition = Vector3.Lerp(elev_1st_start, elev_1st_end, rat_elev);
        elev_2nd.localPosition = Vector3.Lerp(elev_2nd_start, elev_2nd_end, rat_elev);
        /* move arm */
        rat_arm = Mathf.Clamp01(rat_arm + v*Time.deltaTime);
        arm.localPosition = Vector3.Lerp(arm_start, arm_end, rat_arm);
        /* move wrist */
        bool cmd_out = Input.GetKeyDown(KeyCode.C);
        bool cmd_in = Input.GetKeyDown(KeyCode.Z);
        if (cmd_in ^ cmd_out)
            st_wrist = Mathf.Clamp01(st_wrist + (cmd_out ? 0.5f : -0.5f));
        if (st_wrist > rat_wrist + 1e-3)
            rat_wrist += Time.deltaTime;
        else if (st_wrist < rat_wrist - 1e-3)
            rat_wrist -= Time.deltaTime;
        rat_wrist = Mathf.Clamp01(rat_wrist);
        /* move claw */
        rat_claw = Mathf.Clamp01(rat_claw + h*Time.deltaTime);
        claw.localPosition = Vector3.Lerp(claw_lt, claw_rt, rat_claw);
        
        holding ^= Input.GetKeyDown(KeyCode.R);
        // TODO
    }


    readonly Vector3 card_start = new Vector3(0, 0.084f, 0.22f);
    readonly Vector3 card_end = new Vector3(0, 0.084f, 0.50f);
    bool saving = false;
    float rat_rev = 0;
    public void Save() {
        if (Input.GetKeyDown(KeyCode.R)) {
            saving = !saving;
        }
        rat_rev = Mathf.Clamp01(rat_rev + (saving ? Time.deltaTime : -Time.deltaTime));
        rev_card.localPosition = Vector3.Lerp(card_start, card_end, rat_rev); 
    }

}