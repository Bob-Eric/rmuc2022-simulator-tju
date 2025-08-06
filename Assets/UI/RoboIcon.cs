using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


namespace RMUC_UI {
    public class RoboIcon : MonoBehaviour {
        RoboState robo_conn;
        public GameObject arrow;
        public TMP_Text txt_idx;
        public Image img_bg;
        [Header("red-blue")]
        public Sprite[] spr_bg;
        public Sprite[] spr_arrow;
        public ArmorColor armor_color;

        Image img_arrow;


        public void Init(RoboState robo_conn) {
            if (robo_conn == null) {
                Destroy(this.gameObject);
                return;
            }
            this.robo_conn = robo_conn;
            SetColor(robo_conn.armor_color);
            string[] kw = robo_conn.name.Split('_');  // keywords contained in robot's name. Ex. infantry_red_4
            if (kw.Length != 3) {
                Destroy(this.gameObject);
                return;
            }
            txt_idx.text = kw[2];
        }


        void Awake() {
            img_arrow = arrow.GetComponentInChildren<Image>();
        }


        void Update() {
            if (this.robo_conn == null || this.robo_conn.rigid == null)
                return;

            SetArrow();
            SetIconPos();
        }

        float x => robo_conn.transform.position.x;
        float z => robo_conn.transform.position.z;
        List<float> X = new List<float>();
        List<float> Z = new List<float>();
        Vector2 vel;
        void FixedUpdate() {
            X.Add(x); Z.Add(z);
            if (X.Count < 5)
                return;
            vel = new Vector2(X[4] - X[0], Z[4] - Z[0]) / (4 * Time.fixedDeltaTime);
            X.RemoveAt(0); Z.RemoveAt(0);
        }


        void SetColor(ArmorColor armor_color) {
            this.armor_color = armor_color;
            int tmp = (int)armor_color;
            img_arrow.sprite = spr_arrow[tmp];
            img_bg.sprite = spr_bg[tmp];
        }


        Vector3 localrot = new Vector3(0, 0, 0);
        void SetArrow() {
            if (vel.magnitude < 0.1f) {
                arrow.SetActive(false);
                return;
            } else
                arrow.SetActive(true);
            float ang = Vector2.SignedAngle(Vector2.up, vel);
            localrot.z = ang + 180;
            arrow.transform.localEulerAngles = localrot;
        }


        
        Vector3 localpos = new Vector3(0, 0, 0);
        void SetIconPos() {
            /* pos_map = (pos_real - origin_real) * map_scale + origin_map.
                Here, origin_real and origin_map are both zero. */
            localpos.x = - Minimap.length / BattleField.length * robo_conn.rigid.transform.position.x;
            localpos.y = - Minimap.width / BattleField.width * robo_conn.rigid.transform.position.z;
            transform.localPosition = localpos;
        }
    }
} 