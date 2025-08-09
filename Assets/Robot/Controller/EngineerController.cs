using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System;

public struct EngineerInput {
    public bool cmd_C;          // super capacity; claw rotated by -90 degrees
    public bool cmd_Z;          // ;claw rotated by 90 degrees
    public bool cmd_R;          // revive card; claw suction
    public bool cmd_LShift;     // chassis-claw mode switch
    public bool cmd_E;          // elevate claw
    public bool cmd_Q;          // drop claw
    public bool cmd_X;          // braking
    public float h;             // horizontal input
    public float v;             // vertical input
    public float mouseX;        // mouse X input
    public float mouseY;        // mouse Y input
    public float dt;            // delta time
};

public class EngineerController : BasicController {
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

    private CatchMine cm;


    bool playing => Cursor.lockState == CursorLockMode.Locked;


    public override void OnStartClient() {
        base.OnStartClient();
        if (isOwned) {
            Transform tmp = Camera.main.transform;
            tmp.parent = robo_cam;
            tmp.localEulerAngles = Vector3.zero;
            tmp.localPosition = Vector3.zero;
        }
    }


    void Awake() {
        robo_state = GetComponent<RoboState>();
        cm = GetComponentInChildren<CatchMine>();
    }


    void Start() {
        /* even if no authority, external reference should be inited */
        _rigid.centerOfMass = centerOfMass;
        yaw_ang = _rigid.transform.localEulerAngles.y;

        if (isOwned) {
            BattleField.singleton.robo_local = robo_state;
        }
    }


    /* NOTE: multiple CmdInput may be handled in one frame in server PC. 
        Hence, we use "h += ei.h" instead of "h = ei.h" to avoid overwriting.
    */
    [Command]
    void CmdInput(EngineerInput ei) {
        if (!ei.cmd_LShift) {
            /* chassis mode */
            saving ^= ei.cmd_R; // toggle saving state
            if (ei.cmd_E ^ ei.cmd_Q)
                yaw_ang += (ei.cmd_E ? 1 : -1) * 30 * ei.dt;
            h += ei.h;
            v += ei.v;
            yaw_ang += ei.mouseX;
        } else {
            /* claw mode */
            // set target rotation of wrist
            if (ei.cmd_C ^ ei.cmd_Z)
                st_wrist += ei.cmd_C ? 90 : -90;
            // hold
            hold_switch ^= ei.cmd_R;
            // elevate
            if (ei.cmd_E ^ ei.cmd_Q)
                ratio_elev = Mathf.Clamp01(ratio_elev + (ei.cmd_E ? ei.dt : -ei.dt));
            // move arm
            ratio_arm = Mathf.Clamp01(ratio_arm + ei.v * ei.dt);
            // move claw
            ratio_claw = Mathf.Clamp01(ratio_claw + ei.h * ei.dt);
        }
        pitch_ang = Mathf.Clamp(pitch_ang - ei.mouseY, -pitch_max, -pitch_min);
        braking = ei.cmd_X || ei.cmd_LShift;
    }


    EngineerInput _ei = new();
    void Update() {
        if (isOwned) {
            // collect input in client PC
            _ei.cmd_C = playing && Input.GetKeyDown(KeyCode.C);
            _ei.cmd_Z = playing && Input.GetKeyDown(KeyCode.Z);
            _ei.cmd_R = playing && Input.GetKeyDown(KeyCode.R);
            _ei.cmd_LShift = playing && Input.GetKey(KeyCode.LeftShift);
            _ei.cmd_E = playing && Input.GetKey(KeyCode.E);
            _ei.cmd_Q = playing && Input.GetKey(KeyCode.Q);
            _ei.cmd_X = playing && Input.GetKey(KeyCode.X);
            _ei.h = playing ? Input.GetAxis("Horizontal") : 0;
            _ei.v = playing ? Input.GetAxis("Vertical") : 0;
            _ei.mouseX = playing ? 2 * Input.GetAxis("Mouse X") : 0;
            _ei.mouseY = playing ? 2 * Input.GetAxis("Mouse Y") : 0;
            _ei.dt = Time.deltaTime;
            // send to server PC to execute
            if (NetworkClient.active)
                CmdInput(_ei);
            // update UI in client PC
            UpdateSelfUI();
        }

        if (NetworkServer.active)
            if (robo_state.survival) {
                Move();
                Look();
                MovClaw();
                Catch();
                Save();
            } else
                StopMove();
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


    void StopMove() {
        foreach (var wc in wheelColliders) {
            wc.motorTorque = 0;
            wc.brakeTorque = 0.1f;
        }
    }


    const int wheel_num = 4;
    const float torque_drive = 8f;
    PIDController chas_ctl = new PIDController(1, 0, 0);
    bool braking;
    float h, v;
    void Move() {
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

        // calc drive force
        float steer_ang = Mathf.Rad2Deg * Mathf.Atan2(h, v);
        float t1 = 0;
        if (Mathf.Abs(h) >= 1e-3 || Mathf.Abs(v) >= 1e-3)
            t1 = torque_drive;
        // reset h and v
        h = v = 0;

        /* spin */
        float t2 = 0;

        float d_ang = -Mathf.DeltaAngle(yaw_ang, _rigid.transform.eulerAngles.y);
        if (Mathf.Abs(d_ang) < 5) d_ang = 0;
        t2 = 0.2f * chas_ctl.PID(d_ang);


        /* get sum of force */
        float torque = 0;
        for (int i = 0; i < wheel_num; i++) {
            float ang1 = steer_ang * Mathf.Deg2Rad;
            float ang2 = (45 + 90 * i) % 360 * Mathf.Deg2Rad;
            Vector2 f1 = t1 * new Vector2(Mathf.Cos(ang1), Mathf.Sin(ang1));
            Vector2 f2 = t2 * new Vector2(Mathf.Cos(ang2), Mathf.Sin(ang2));
            Vector2 f_all = f1 + f2;
            wheelColliders[i].steerAngle = Mathf.Rad2Deg * Mathf.Atan2(f_all.y, f_all.x);
            wheelColliders[i].motorTorque = f_all.magnitude;
            torque += wheelColliders[i].motorTorque;
        }
        if (torque < 1)   // counted as not moving
            foreach (var wc in wheelColliders)
                wc.brakeTorque = 0.1f;

        return;
    }


    float pitch_ang = 0;
    float yaw_ang = 0;
    const float pitch_min = -30;
    const float pitch_max = 40;
    void Look() {
        /* Rotate Transform "yaw" & "pitch" */
        pitch.localEulerAngles = new Vector3(pitch_ang, 0, 0);
    }


    readonly Vector3 elev_1st_start = new Vector3(0, 0, 0);
    readonly Vector3 elev_1st_end = new Vector3(0, 0, -0.24f);
    readonly Vector3 elev_2nd_start = new Vector3(0, 0, 0);
    readonly Vector3 elev_2nd_end = new Vector3(0, -0.2f, 0);
    readonly Vector3 arm_start = new Vector3(0, 0, 0);
    readonly Vector3 arm_end = new Vector3(-0.4f, 0, 0);
    readonly Vector3 claw_lt = new Vector3(-0.32f, 0.178f, 0.054f);
    readonly Vector3 claw_rt = new Vector3(-0.08f, 0.178f, 0.054f);
    float ratio_arm = 0;
    float ratio_claw = 0.5f;
    int st_wrist = 0; // set wrist angle
    float ang = 0;
    float ratio_elev = 0f;
    void MovClaw() {
        elev_1st.localPosition = Vector3.Lerp(elev_1st_start, elev_1st_end, ratio_elev);
        elev_2nd.localPosition = Vector3.Lerp(elev_2nd_start, elev_2nd_end, ratio_elev);
        arm.localPosition = Vector3.Lerp(arm_start, arm_end, ratio_arm);
        ang -= 8 * Time.deltaTime * Mathf.DeltaAngle(st_wrist, ang);
        wrist.localEulerAngles = new Vector3(ang, 0, 0);
        claw.localPosition = Vector3.Lerp(claw_lt, claw_rt, ratio_claw);
    }


    bool hold_switch;
    public bool holding = false;
    void Catch() {
        if (!hold_switch)
            return;

        hold_switch = false;
        RpcCatch(to_hold: !holding);
        holding = !holding;
    }
    [ClientRpc]
    void RpcCatch(bool to_hold) {
        // flip holding state every time when called
        if (!to_hold) {
            cm.Release();
            cm.enabled = false;
        } else {
            cm.enabled = true;
        }
    }


    readonly Vector3 card_start = new Vector3(0, 0.084f, 0.22f);
    readonly Vector3 card_end = new Vector3(0, 0.084f, 0.50f);
    bool saving = false;
    float ratio_rev = 0; // reach-out ratio of revive_card
    public void Save() {
        ratio_rev = Mathf.Clamp01(ratio_rev + (saving ? Time.deltaTime : -Time.deltaTime));
        rev_card.localPosition = Vector3.Lerp(card_start, card_end, ratio_rev);
    }

}