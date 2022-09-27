using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BasicState : MonoBehaviour {
    public abstract void TakeDamage(GameObject hitter, GameObject armor_hit, GameObject bullet);
    public void SetArmorColor(ArmorColor armor_color) {
        this.armor_color = armor_color;
        string other_color = armor_color == ArmorColor.Red ? "blue" : "red";
        Material new_mat = armor_color == ArmorColor.Red ? AssetManager.singleton.light_red 
            : AssetManager.singleton.light_blue;
        Renderer[] rens = GetComponentsInChildren<Renderer>();
        foreach (Renderer ren in rens) {
            if (ren.sharedMaterial.name.ToLower().Contains(other_color)) {
                ren.sharedMaterial = new_mat;
                Debug.Log("replace");
            }
        }
    }
    public ArmorColor armor_color;
    public float expval;
    protected Dictionary<GameObject, float> last_hit = new Dictionary<GameObject, float>();
    protected void Hit(GameObject hitter) {
        if (!last_hit.ContainsKey(hitter)) {
            last_hit.Add(hitter, Time.time);
        } else {
            last_hit[hitter] = Time.time;
        }
    }
    protected GameObject killer;
    protected bool killed = false;
    protected void DistribExp() {
        if (killed) {
            RoboState robot = killer.GetComponent<RoboState>();
            robot.currexp += this.expval;
            foreach (GameObject hitter in this.last_hit.Keys) {
                if (hitter != killer && Time.time - last_hit[hitter] < 10)
                    hitter.GetComponent<RoboState>().currexp += 0.25f * this.expval;
            }
        } else {
            RoboState[] robots = this.armor_color == ArmorColor.Red ? BattleField.singleton.robo_blue
                : BattleField.singleton.robo_red;
            List<RoboState> hero_infa = new List<RoboState>();
            foreach (RoboState robot in robots) {
                string robo_name = robot.gameObject.name.ToLower();
                bool heroOrinfa = robo_name.Contains("infantry") || robo_name.Contains("hero");
                if (robot.survival && heroOrinfa)
                    hero_infa.Add(robot);
            }
            float exp_average = this.expval / (float)hero_infa.Count;
            foreach (RoboState robot in hero_infa)
                robot.currexp += exp_average;
        }
    }

}
