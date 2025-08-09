using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;


public struct DroneInput {
    public bool cmd_E;
    public bool cmd_Q;
    public bool cmd_R;
    public bool cmd_LMouse;
    public bool cmd_RMouse;
    public float v;
    public float h;
    public float mouseX;
    public float mouseY;
}


public class DroneController : BasicController {
    [Header("Kinematic")]
    public Vector3 centerOfMass;
    [Header("turret")]
    public Transform yaw;
    public Transform pitch;
    public GameObject[] paddles;

    [Header("Weapon")]
    public Transform bullet_start;
    [Header("View")]
    public Transform view;      // control visual effect of leaning to make flying realistic

    public const float speed = 0.5f;

    float last_fire = 0;
    float pitch_ang = 0;
    float pitch_min = -45;
    float pitch_max = 15;
    Weapon wpn;


    bool playing => Cursor.lockState == CursorLockMode.Locked;

    bool cmd_E, cmd_Q, cmd_R;
    bool cmd_LMouse, cmd_RMouse;
    float mouseX, mouseY;
    float h, v;

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
            BattleField.singleton.bat_ui.robo_prof.SetActive(false);
            BattleField.singleton.bat_ui.drone_prof.SetActive(true);
        }
    }


    DroneInput _di = new();
    void Update() {
        if (!BattleField.singleton.started_game)
            return;

        if (isOwned) {
            _di.cmd_E = playing && Input.GetKey(KeyCode.E);
            _di.cmd_Q = playing && Input.GetKey(KeyCode.Q);
            _di.cmd_R = playing && Input.GetKeyDown(KeyCode.R);
            _di.cmd_LMouse = playing && Input.GetMouseButton(0);
            _di.cmd_RMouse = playing && Input.GetMouseButton(1);
            _di.h = playing ? Input.GetAxis("Horizontal") : 0;
            _di.v = playing ? Input.GetAxis("Vertical") : 0;
            _di.mouseX = playing ? Input.GetAxis("Mouse X") : 0;
            _di.mouseY = playing ? Input.GetAxis("Mouse Y") : 0;
            CmdInput(_di);
            UpdateSelfUI();
        }
        if (NetworkServer.active) {
            Look();
            Shoot();
            Attack();
        }
    }


    [Command]
    void CmdInput(DroneInput di) {
        cmd_E = di.cmd_E;
        cmd_Q = di.cmd_Q;
        cmd_R |= di.cmd_R;
        cmd_LMouse = di.cmd_LMouse;
        cmd_RMouse = di.cmd_RMouse;
        h += di.h;
        v += di.v;
        mouseX += di.mouseX;
        mouseY += di.mouseY;
    }


    public override void FixedUpdate() {
        base.FixedUpdate();

        if (!BattleField.singleton.started_game)
            return;
        PaddleSpin();

        if (NetworkServer.active)
            Move();
    }


    void PaddleSpin() {
        for (int i = 0; i < paddles.Length; i++) {
            paddles[i].transform.Rotate(Vector3.up, (i % 2 == 0 ? 1 : -1) * 15 * 360 * Time.fixedDeltaTime, Space.Self);
        }
    }


    RMUC_UI.BattleUI bat_ui => BattleField.singleton.bat_ui;    // alias
    void UpdateSelfUI() {
        if (t_bat - last_atk >= 30) {
            bat_ui.img_droneTimer.fillAmount = 0;
            wpn.bullnum = 0;
        } else
            bat_ui.img_droneTimer.fillAmount = 1 - (t_bat - last_atk) / 30;

        /* update buff display in UI */
        bat_ui.indic_buf[0] = Mathf.Approximately(robo_state.B_atk, 0f) ? -1            // atk
            : Mathf.Approximately(robo_state.B_atk, 0.5f) ? 0 : 1;
        bat_ui.indic_buf[1] = Mathf.Approximately(robo_state.B_cd, 1f) ? -1             // cd
            : Mathf.Approximately(robo_state.B_cd, 3f) ? 0 : 1;
        bat_ui.indic_buf[2] = Mathf.Approximately(robo_state.B_rev, 0f) ? -1            // rev
            : Mathf.Approximately(robo_state.B_rev, 0.02f) ? 0 : 1;
        bat_ui.indic_buf[3] = Mathf.Approximately(robo_state.B_dfc, 0f) ? -1            // dfc
            : Mathf.Approximately(robo_state.B_dfc, 0.5f) ? 0 : 1;
        HeroState hs = robo_state.GetComponent<HeroState>();
        bat_ui.indic_buf[4] = hs == null || !hs.sniping ? -1 : 0;
        bat_ui.indic_buf[5] = Mathf.Approximately(robo_state.B_pow, 0f) ? -1 : 0;       // lea
    }


    PIDController pid_yaw = new PIDController(5f, 0f, 10f);
    PIDController pid_throttle = new PIDController(6f, 0.2f, 0.1f);
    PIDController pid_sim_v = new PIDController(2f, 0.01f, 0f);                // control force forward
    PIDController pid_sim_h = new PIDController(2f, 0.01f, 0f);                // control force right
    PIDController pid_pitch = new PIDController(7f, 0.01f, 25f);
    PIDController pid_roll = new PIDController(7f, 0.01f, 25f);
    void Move() {
        // ascend and descend
        float vel_set = speed * ((cmd_E ? 1 : 0) - (cmd_Q ? 1 : 0));
        float f_thro = Mathf.Clamp(pid_throttle.PID(vel_set - _rigid.velocity.y) * 30 * Time.fixedDeltaTime, -5, 5);
        _rigid.AddForce(f_thro * Vector3.up, ForceMode.Acceleration);

        // fly horizontally
        Vector3 vec_set;
        if (Mathf.Abs(v) > 1e-3 || Mathf.Abs(h) > 1e-3) {
            Vector3 vec_v = Vector3.ProjectOnPlane(virt_yaw.forward, Vector3.up).normalized;
            Vector3 vec_h = Vector3.ProjectOnPlane(virt_yaw.right, Vector3.up).normalized;
            vec_set = (h * vec_h + v * vec_v).normalized;
        } else
            vec_set = Vector3.zero;
        h = v = 0;
        float tmp_v = Mathf.Clamp(pid_sim_v.PID(vec_set.z - _rigid.velocity.z) * 30 * Time.fixedDeltaTime, -5, 5);
        float tmp_h = Mathf.Clamp(pid_sim_h.PID(vec_set.x - _rigid.velocity.x) * 30 * Time.fixedDeltaTime, -5, 5);
        _rigid.AddForce(tmp_v * Vector3.forward + tmp_h * Vector3.right, ForceMode.Acceleration);

        // set visual effect of leaning
        Vector3 error = 0.15f * vec_set - Vector3.ProjectOnPlane(_rigid.transform.up, Vector3.up);
        float lean_v = Mathf.Clamp(pid_pitch.PID(error.z) * 30 * Time.fixedDeltaTime, -3, 3);
        float lean_h = Mathf.Clamp(pid_roll.PID(error.x) * 30 * Time.fixedDeltaTime, -3, 3);
        _rigid.AddTorque(lean_v * Vector3.right + lean_h * Vector3.back, ForceMode.Acceleration);

        // wings follow turret
        float wing2yaw = Vector3.SignedAngle(_rigid.transform.forward, virt_yaw.forward, _rigid.transform.up);
        float f_fol = Mathf.Clamp(pid_yaw.PID(wing2yaw) * 30 * Time.fixedDeltaTime, -30, 30);
        _rigid.AddTorque(f_fol * _rigid.transform.up, ForceMode.Acceleration);

        // yaw rotates with _rigid, hence calibration is needed
        CalibYaw();
    }


    /* keep yaw.up coincides with _rigid.up */
    void CalibVirtYaw() {
        virt_yaw.position = _rigid.transform.position;
        Vector3 axis = Vector3.Cross(virt_yaw.transform.up, _rigid.transform.up);
        float ang = Vector3.Angle(virt_yaw.transform.up, _rigid.transform.up);
        virt_yaw.transform.Rotate(axis, ang, Space.World);
    }


    /** align yaw's rotation to virt_yaw's rotation, should be called when yaw is 'dirty'
        (when yaw ain't aligned with virt_yaw and transform of yaw or any child is to use)
    */
    void CalibYaw() {
        yaw.rotation = virt_yaw.rotation;
    }

    /* Get look dir from user input */
    void Look() {
        CalibVirtYaw();
        // must align yaw to virt_yaw now because 'bullet_start', which is child of yaw is to use 
        CalibYaw();
        if (!cmd_RMouse || !base.AutoAim(bullet_start, runeMode: false)) {
            pitch_ang -= mouseY;
            pitch_ang = Mathf.Clamp(pitch_ang, -pitch_max, -pitch_min);
            /* Rotate Transform "pitch" by user input */
            pitch.localEulerAngles = new Vector3(pitch_ang, 0, 0);
            /* Rotate Transform "virt_yaw" by user input */
            virt_yaw.transform.Rotate(_rigid.transform.up, mouseX, Space.World);

            mouseX = mouseY = 0; // reset mouse input

            last_target = null;
        }
        /* update yaw's transform, i.e., transform yaw to aim at target (store in virt yaw) */
        CalibYaw();
    }


    protected override void AimAt(Vector3 target) {
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


    [SyncVar] float last_atk = -30;
    const int money_req = 300;
    int money_team => robo_state.armor_color == ArmorColor.Red ? BattleField.singleton.money_red : BattleField.singleton.money_blue;
    float t_bat => BattleField.singleton.GetBattleTime();
    void Attack() {
        /* player calls drone attack and money suffices */
        if (cmd_R && money_team >= 300 && t_bat - last_atk >= 30) {
            if (robo_state.armor_color == ArmorColor.Red)
                BattleField.singleton.money_red -= money_req;
            else
                BattleField.singleton.money_blue -= money_req;
            wpn.bullnum = 400;
            last_atk = t_bat;
        }
        cmd_R = false;
    }


    void Shoot() {
        if (t_bat - last_atk >= 30)
            return;
        if (cmd_LMouse && t_bat - last_fire > 0.05f) {
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
            // Debug.Log("no bullet");
            return;
        }
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        rb.position = pos;
        rb.velocity = vel;
        bullet.GetComponent<Bullet>().hitter = this.gameObject;
    }
}
