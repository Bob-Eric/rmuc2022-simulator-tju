using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/* engineer:
    1. no limit of chassis power
    2. no level, maxblood is 500
 */
public class EngineerState : RoboState {
    public override RoboSync Pull() {
        RoboSync rs = base.Pull();
        // Debug.Log("actual survival: " + this.survival + " pull survival: " + rs.bat_stat);
        rs.has_blood = true;
        rs.has_wpn = false;
        rs.has_level = false;
        rs.maxblood = this.maxblood;
        return rs;
    }
    // Start is called before the first frame update
    public override void Start() {
        /* Make sure init maxblood first because in Start(), maxblood is assigned to currblood */
        this.maxblood = 500;
        this.expval = AssetManager.singleton.exp["engineer"]["have"].ToObject<int>(); 
        base.Start();
    }

    // Update is called once per frame
    public override void Update() {
        base.Update();
    }
}
