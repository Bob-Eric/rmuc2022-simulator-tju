using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class XchgSpot : MonoBehaviour {
    public ArmorColor armor_color;
    public GameObject det_light;
    public GameObject coll_light;

    readonly Vector3 detCent = new Vector3(0, 0, 0.02f);            // offset from this.position to center of detection region 
    readonly Vector3 detSize = new Vector3(0.2f, 0.2f, 0.03f);      // detection region's (a box area) extent 
    readonly Vector3 collCent = new Vector3(-0.6f, 0, -0.3f);      // offset from this.position to center of collection region 
    readonly Vector3 collSize = new Vector3(0.8f, 1f, 0.5f);        // detection region's (a box area) extent 


    List<Material> det_materials;
    List<Renderer> coll_lightbars;
    void Awake() {
        det_materials = new(det_light.GetComponentsInChildren<Renderer>().Select(ren => ren.material));
        coll_lightbars = new(coll_light.GetComponentsInChildren<Renderer>());
    }

    const string used_s = "used";
    float t_last = -3f;
    void FixedUpdate() {
        if (!BattleField.singleton.game_started)
            return;
        // calling Physics.Overlapxxx is efficient to detect collide
        Collider[] cols = Physics.OverlapBox(transform.TransformPoint(detCent), 0.5f * detSize, transform.rotation);
        // Debug.DrawRay(transform.TransformPoint(detCent), Vector3.up, Color.red);
        foreach (Collider col in cols) {
            if (col.name.Contains(CatchMine.mine_s) && !col.name.Contains(used_s)) {
                col.name += used_s;
                t_last = BattleField.singleton.GetBattleTime();
                Debug.Log("detect a mine");
                StartCoroutine(DetLightBlink());
            }
        }

        cols = Physics.OverlapBox(transform.TransformPoint(collCent), 0.5f * collSize, transform.rotation);
        // Debug.DrawRay(transform.TransformPoint(collCent), Vector3.up, Color.green);
        foreach (Collider col in cols) {
            if (col.name.Contains(CatchMine.mine_s)) {
                if (BattleField.singleton.GetBattleTime() - t_last < 3f) {
                    BattleField.singleton.XchgMine(armor_color, col.name.Contains("gold"));
                    Debug.Log($"xchg a {(col.name.Contains("gold") ? "gold" : "normal")} mine successfully.");
                    StartCoroutine(CollLightBlink());
                } else
                    Debug.Log("xchg too late");
                Destroy(col);
            }
        }
    }

    readonly WaitForSeconds _wait = new(0.1f);
    IEnumerator DetLightBlink() {
        for (int i = 0; i < 15; i++) {
            det_materials.ForEach(mat => mat.DisableKeyword("_EMISSION"));
            yield return _wait;
            det_materials.ForEach(mat => mat.EnableKeyword("_EMISSION"));
            yield return _wait;
        }
    }


    IEnumerator CollLightBlink() {
        for (int i = 0; i < 10; i++) {
            coll_lightbars.ForEach(ren => ren.material = AssetManager.singleton.light_off);
            yield return _wait;
            coll_lightbars.ForEach(ren => ren.material = AssetManager.singleton.light_white);
            yield return _wait;
        }
    }
}