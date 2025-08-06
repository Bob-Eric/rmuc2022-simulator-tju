using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CatchMine : MonoBehaviour {
    public const string mine_s = "mine";    // mark whether the collider is a mine
    public const string held_s = "held";    // mark whether the mine has been held (to prevent grab other team's mine)
    EngineerController ec;
    GameObject mine_holding;


    void Awake() {
        ec = GetComponentInParent<EngineerController>();
    }


    void LateUpdate() {
        if (mine_holding != null) {
            // Note: use rigidbody.transform.position instead of rigidbody.position to "teleport" the mine
            mine_holding.transform.rotation = transform.rotation;
            mine_holding.transform.position = transform.position;
        }
    }


    bool holding => ec.holding;
    void OnTriggerStay(Collider other) {
        if (!this.holding || !other.name.Contains(mine_s) || other.name.Contains(held_s))
            return;
        Hold(other.gameObject);
    }


    void Hold(GameObject mine) {
        if (mine_holding != null)
            return;
        mine.name = mine.name + held_s;
        mine.transform.parent = this.transform;
        Destroy(mine.GetComponent<Rigidbody>());
        mine.GetComponent<Collider>().enabled = false; // disable collider to avoid xchgspot detecting it incorrectly when claw not dropping the mine
        mine_holding = mine;
    }


    public void Release() {
        if (mine_holding == null)
            return;
        mine_holding.name = mine_holding.name.Replace(held_s, "");
        mine_holding.transform.parent = null;
        mine_holding.GetComponent<Collider>().enabled = true;
        mine_holding.AddComponent<Rigidbody>();
        mine_holding = null;
    }
}
