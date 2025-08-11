using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.Assertions;

public struct RoboInput {
    public bool cmd_C;          // super capacity
    public bool cmd_X;          // braking
    public bool cmd_R;          // rune-aiming mode
    public bool cmd_LShift;     // chassis spinning
    public bool cmd_LMouse;     // shooting
    public bool cmd_RMouse;     // auto aiming
    public float h;             // horizontal input
    public float v;             // vertical input
    public float mouseX;        // mouse X input
    public float mouseY;        // mouse Y input
}

/* infantry, hero and guard's controller */
public class RoboController : BasicController {
    enum RoboCtlType { Infantry, Hero, Guard };

    [Header("Kinematic")]
    public Vector3 centerOfMass;
    [Header("turret")]
    public Transform yaw;
    public Transform pitch;
    [Header("wheels")]
    public Transform[] wheels;
    [Header("order: FL-FR-BR-BL")]
    public WheelCollider[] wheelColliders;

    [Header("Weapon")]
    public Transform bullet_start;
    [Header("View")]
    public Transform view;

    [SyncVar]
    public float currcap = 0;

    const int maxcap = 500;
    [Header("robot params")]
    public float pitch_min = -30;   // down is minus, up is positive 
    public float pitch_max = 40;

    float pitch_ang = 0;
    Weapon wpn;

    bool playing => Cursor.lockState == CursorLockMode.Locked;
    bool isInfantry => robo_state.GetType() == typeof(InfantryState);
    bool isHero => robo_state.GetType() == typeof(HeroState);
    bool isGuard => robo_state.GetType() == typeof(GuardState);

    public override void OnStartClient() {
        base.OnStartClient();
        if (isOwned) {
            Transform tmp = Camera.main.transform;
            tmp.parent = view;
            tmp.localEulerAngles = Vector3.zero;
            tmp.localPosition = Vector3.zero;
        }
    }


    Transform virt_yaw;
    void Awake() {
        robo_state = GetComponent<RoboState>();
        wpn = GetComponent<Weapon>();
        /* create virtual yaw transform (independent of chassis's transform) */
        virt_yaw = new GameObject("virt_yaw-" + this.name).transform;
        virt_yaw.transform.SetPositionAndRotation(yaw.transform.position, yaw.transform.rotation);
        virt_yaw.parent = this.transform.parent;
    }


    void Start() {
        /* even if no authority, external reference should be inited */
        _rigid.centerOfMass = centerOfMass;

        if (isOwned) {
            BattleField.singleton.robo_local = this.robo_state;
            /* set dropdown of chassis and turret in preference_ui of bat_ui */
            BattleField.singleton.bat_ui.SetRoboPrefDrop(interactable: true);
            BattleField.singleton.bat_ui.drop_chas.ClearOptions();
            BattleField.singleton.bat_ui.drop_turr.ClearOptions();
            if (isInfantry) {
                BattleField.singleton.bat_ui.drop_chas.AddOptions(new List<string>() { "血量优先", "功率优先" });
                BattleField.singleton.bat_ui.drop_turr.AddOptions(new List<string>() { "爆发优先", "冷却优先", "弹速优先" });
            } else if (isHero) {
                BattleField.singleton.bat_ui.drop_chas.AddOptions(new List<string>() { "血量优先", "功率优先" });
                BattleField.singleton.bat_ui.drop_turr.AddOptions(new List<string>() { "爆发优先", "弹速优先" });
            }
        }
    }


    RoboInput _ri = new();
    void Update() {
        if (isGuard && NetworkServer.active && robo_state.survival) {
            // for guard, run without player input
            GuardLook();
            GuardMove();
            GuardShoot();
            return;
        }

        if (isOwned) {
            // for infantry and hero, collect input from player
            _ri.cmd_C = playing && Input.GetKeyDown(KeyCode.C);
            _ri.cmd_X = playing && Input.GetKey(KeyCode.X);
            _ri.cmd_R = playing && Input.GetKey(KeyCode.R);
            _ri.cmd_LShift = playing && Input.GetKey(KeyCode.LeftShift);
            _ri.cmd_LMouse = playing && Input.GetMouseButton(0);
            _ri.cmd_RMouse = playing && Input.GetMouseButton(1);
            _ri.h = playing ? Input.GetAxis("Horizontal") : 0;
            _ri.v = playing ? Input.GetAxis("Vertical") : 0;
            _ri.mouseX = playing ? 2 * Input.GetAxis("Mouse X") : 0;
            _ri.mouseY = playing ? 2 * Input.GetAxis("Mouse Y") : 0;
            // send to server PC to execute
            CmdInput(_ri);
            // update UI in client PC
            UpdateSelfUI();
        }

        if (NetworkServer.active) {
            if (robo_state.survival) {
                Look();
                Move();
                Shoot();
            } else
                StopMove();
        }
    }


    /// Goal state of the robot, affected by player input.
    [Command]
    void CmdInput(RoboInput ri) {
        // collect input
        discharging = ri.cmd_C ^ discharging; // toggle discharging state
        braking = ri.cmd_X;
        runeMode = ri.cmd_R;
        spinning = ri.cmd_LShift;
        firing = ri.cmd_LMouse;
        aiming = ri.cmd_RMouse;
        h += ri.h;
        v += ri.v;
        mouseX += ri.mouseX;
        mouseY += ri.mouseY;
    }


    RMUC_UI.BattleUI bat_ui => BattleField.singleton.bat_ui;    // alias
    void UpdateSelfUI() {
        bat_ui.rat_heat = wpn.heat_ratio;
        bat_ui.rat_cap = Mathf.Clamp01(currcap / maxcap);

        /* update buff display in UI */
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


    const int N_wheels = 4;
    const float efficiency = 0.5f;
    const float charge_coeff = 0.1f;          // how much of torque_avail will be used to charge capacity
    const float discharge_coeff = 1.8f;      // how fast capacity discharge 
    const float torque_drive = 20f;
    const float torque_spin = 20f;
    float torque_avail = 0;
    bool discharging, braking, spinning;
    float h, v;
    PIDController chas_ctl = new(1, 0, 0);
    void Move() {
        /* Manage Power */
        torque_avail = efficiency * robo_state.power;

        /* store energy in capacity */
        if (discharging && currcap > 1) {
            currcap -= (discharge_coeff - 1) * robo_state.power * Time.deltaTime;
            torque_avail *= discharge_coeff;
        } else {
            currcap += charge_coeff * torque_avail * Time.deltaTime;
            torque_avail *= 1 - charge_coeff;
        }
        currcap = Mathf.Min(maxcap, currcap);

        /* brake */
        if (braking) {
            for (int i = 0; i < N_wheels; i++) {
                wheelColliders[i].steerAngle = (45 + 90 * i) % 360 * Mathf.Deg2Rad;
                wheelColliders[i].motorTorque = 0;
                wheelColliders[i].brakeTorque = 10;
            }
            // Debug.Log("braking");
            currcap += torque_avail * Time.deltaTime;
            return;
        } else  // remove previous brake torque
            foreach (var wc in wheelColliders)
                wc.brakeTorque = 0;

        float chas2yaw = Vector3.SignedAngle(_rigid.transform.forward, virt_yaw.forward, _rigid.transform.up);

        // move the car and steer wheels
        float steer_ang = Mathf.Rad2Deg * Mathf.Atan2(h, v);
        steer_ang += chas2yaw;
        foreach (var wc in wheelColliders) {
            /* Note: steerAngle will CLAMP angle to [-360, 360]
                Get remainder, make sure steer_ang is in [-360, 360] */
            wc.steerAngle = steer_ang % 360;
            wc.motorTorque = torque_drive * Mathf.Sqrt(h * h + v * v);
        }
        // reset h, v
        h = v = 0;

        /* spin */
        float torque = 0;
        if (spinning)
            // spinning tip mode
            torque = torque_spin;
        else if (Mathf.Abs(chas2yaw) > 5)
            // make chassis follow turret(aka, yaw)
            torque = 0.2f * chas_ctl.PID(chas2yaw);

        /* get sum of force */
        float torque_now = 0;
        for (int i = 0; i < N_wheels; i++) {
            float ang1 = wheelColliders[i].steerAngle * Mathf.Deg2Rad;
            float ang2 = (45 + 90 * i) % 360 * Mathf.Deg2Rad;
            Vector2 f1 = wheelColliders[i].motorTorque * new Vector2(Mathf.Cos(ang1), Mathf.Sin(ang1));
            Vector2 f2 = torque * new Vector2(Mathf.Cos(ang2), Mathf.Sin(ang2));
            Vector2 f_all = f1 + f2;
            wheelColliders[i].steerAngle = (Mathf.Rad2Deg * Mathf.Atan2(f_all.y, f_all.x));
            wheelColliders[i].motorTorque = f_all.magnitude;
            torque_now += wheelColliders[i].motorTorque;
            /* rotate the visual model */
            if (wheels.Length > i)
                wheels[i].transform.localEulerAngles = new Vector3(0, wheelColliders[i].steerAngle, 0);
        }
        if (torque_now < 1) {   // counted as not moving
            foreach (var wc in wheelColliders)
                wc.brakeTorque = 0.1f;
            currcap += torque_avail * Time.deltaTime;
            return;
        } else
            for (int i = 0; i < N_wheels; i++)
                wheelColliders[i].motorTorque *= torque_avail / torque_now;

    }


    void StopMove() {
        foreach (var wc in wheelColliders) {
            wc.motorTorque = 0;
            wc.brakeTorque = 0.1f;
        }
    }


    float vel_guard_targ = 1;
    int move_guard_dir = 1;
    float last_t;
    PIDController pid_move = new PIDController(10, 0.08f, 0.5f);
    void GuardMove() {
        Vector3 vec_err = move_guard_dir * vel_guard_targ * Vector3.forward - _rigid.velocity;
        Vector3 f = Vector3.forward * pid_move.PID(vec_err.z);
        _rigid.AddForce(f, ForceMode.Acceleration);
        if (_rigid.transform.position.z < -1.2f)
            move_guard_dir = 1;
        else if (_rigid.transform.position.z > 1.2f)
            move_guard_dir = -1;
        // Debug.Log(_rigid.velocity.magnitude);
        if (BattleField.singleton.GetBattleTime() - last_t > 2) {
            vel_guard_targ = Random.Range(1, 3);
            last_t = BattleField.singleton.GetBattleTime();
        }
        return;
    }


    /* keep yaw.up coincides with _rigid.up */
    void CalibVirtYaw() {
        virt_yaw.transform.position = _rigid.transform.position;
        Vector3 axis = Vector3.Cross(virt_yaw.transform.up, _rigid.transform.up);
        float ang = Vector3.Angle(virt_yaw.transform.up, _rigid.transform.up);
        virt_yaw.transform.Rotate(axis, ang, Space.World);
    }


    /* Get look dir from user input */
    bool runeMode, aiming;
    float mouseX, mouseY;
    void Look() {
        CalibVirtYaw();
        /* correct yaw's transform, i.e., elimate attitude error caused by following movement */
        yaw.rotation = virt_yaw.rotation;
        if (!aiming || !AutoAim(bullet_start, runeMode)) {
            pitch_ang -= mouseY;
            pitch_ang = Mathf.Clamp(pitch_ang, -pitch_max, -pitch_min);
            /* Rotate Transform "pitch" by user input */
            pitch.localEulerAngles = new Vector3(pitch_ang, 0, 0);
            /* Rotate Transform "virt_yaw" by user input */
            virt_yaw.transform.Rotate(_rigid.transform.up, mouseX, Space.World);

            // reset mouse input
            mouseX = mouseY = 0;
            // reset last target to null
            last_target = null;
        }
        /* update yaw's transform, i.e., transform yaw to aim at target (store in virt yaw) */
        yaw.rotation = virt_yaw.rotation;
    }


    int pitch_guard_dir = -1;
    bool targ_avail = false;
    void GuardLook() {
        CalibVirtYaw();
        yaw.rotation = virt_yaw.rotation;
        targ_avail = AutoAim(bullet_start, runeMode: false);
        if (!targ_avail) {
            pitch_ang += pitch_guard_dir * Time.deltaTime * 120;
            if (pitch_ang < -pitch_max)
                pitch_guard_dir = 1;
            else if (pitch_ang > -pitch_min)
                pitch_guard_dir = -1;

            virt_yaw.transform.Rotate(_rigid.transform.up, 100 * Time.deltaTime, Space.World);
            pitch.localEulerAngles = new Vector3(pitch_ang, 0, 0);
            base.last_target = null;
        }
        yaw.rotation = virt_yaw.rotation;
    }


    protected override void AimAt(Vector3 target) {
        // Note: makes sure y and z axis of `pitch` coincides with those two axes of `bullet_start` 
        Vector3 d = target - pitch.transform.position;
        float d_pitch = BasicController.SignedAngleOnPlane(bullet_start.forward, d, pitch.transform.right);
        float d_yaw = BasicController.SignedAngleOnPlane(bullet_start.forward, d, virt_yaw.transform.up);

        d_pitch = dynCoeff * d_pitch;
        d_yaw = dynCoeff * d_yaw;

        d_pitch = Mathf.Clamp(pitch_ang + d_pitch, -pitch_max, -pitch_min) - Mathf.Clamp(pitch_ang, -pitch_max, -pitch_min);
        virt_yaw.transform.Rotate(virt_yaw.transform.up, d_yaw, Space.World);
        pitch.transform.Rotate(pitch.transform.right, d_pitch, Space.World);
        pitch_ang += d_pitch;       // update pitch_ang so that turret won't look around when switch off auto-aim
    }


    /* API for BattleUI.
        get ammunition supply at reborn spot */
    [Command]
    public void CmdSupply(string robot_s, int num) {
        // todo: add judge of money
        RoboState rs = BattleField.singleton.GetRobot(robot_s);
        RoboController rc = rs.GetComponent<RoboController>();
        bool in_supp_spot = rc.robo_state.robo_buff.FindIndex(i => i.tag == BuffType.rev) != -1;
        if (rc != null && in_supp_spot) {
            int money_req = rc.wpn.caliber == Caliber._17mm ? num : 15 * num;
            bool is_red = rs.GetComponent<RoboState>().armor_color == ArmorColor.Red;
            if (is_red) {
                if (money_req <= BattleField.singleton.money_red) {
                    rc.wpn.bullnum += num;
                    BattleField.singleton.money_red -= money_req;
                    Debug.Log("call supply");
                }
            } else if (money_req <= BattleField.singleton.money_blue) {
                rc.wpn.bullnum += num;
                BattleField.singleton.money_blue -= money_req;
                Debug.Log("call supply");
            }
        }
    }


    [Command]
    /* API for BattleField.
        when game starts, robo_local of every client PC send their preference choice */
    public void CmdUserPref(string robot_s, string pref_chas, string pref_turr) {
        RoboState rs = BattleField.singleton.GetRobot(robot_s);
        rs.GetUserPref(pref_chas, pref_turr);
        rs.Configure();
    }


    void GuardShoot() {
        if (!BattleField.singleton.game_started)
            return;

        if (targ_avail && BattleField.singleton.GetBattleTime() - last_fire > 0.3f) {
            ShootBull(bullet_start.position, robo_state.bullspd * bullet_start.forward + _rigid.velocity);
            last_fire = BattleField.singleton.GetBattleTime();
        }
    }


    float last_fire;
    bool firing;
    void Shoot() {
        if (!BattleField.singleton.game_started)
            return;

        if (firing && BattleField.singleton.GetBattleTime() - last_fire > 0.15f) {
            Vector3 pos = bullet_start.position;
            Vector3 vel = robo_state.bullspd * bullet_start.forward + _rigid.velocity;
            if (!NetworkClient.active)
                ShootBull(pos, vel);
            RpcShoot(pos, vel);
            last_fire = BattleField.singleton.GetBattleTime();
        }
    }


    [ClientRpc]
    void RpcShoot(Vector3 pos, Vector3 vel) {
        ShootBull(pos, vel);
    }
    void ShootBull(Vector3 pos, Vector3 vel) {
        GameObject bullet = wpn.GetBullet();
        if (bullet == null) {
            Debug.Log("no bullet");
            return;
        } else
            Debug.Log("shoot bullet.");
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        rb.position = pos;
        rb.velocity = vel;
        bullet.GetComponent<Bullet>().hitter = this.gameObject;
    }
}
