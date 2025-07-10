using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.Assertions;

public class Bullet : MonoBehaviour {
    [HideInInspector]
    public GameObject hitter;

    Rigidbody rb;
    int cnt_static;
    bool f_col; // if bullet has been collided with something
    void Awake() {
        Reset();
    }

    void Reset() {
        rb = GetComponent<Rigidbody>();
        cnt_static = 0;
        f_col = false;
    }

    void FixedUpdate() {
        Assert.IsTrue(rb != null);

        // simulate friction to make bullet stop
        if (f_col) {
            rb.velocity *= 0.99f;
            rb.angularVelocity *= 0.99f;
        }

        if (rb.velocity.magnitude < 0.01f && rb.angularVelocity.magnitude < 0.01f)
            cnt_static ++;
        else
            cnt_static = 0;

        if (cnt_static >= 5 || !BattleField.singleton.OnField(gameObject)) {
            BulletPool.singleton.RemoveBullet(this.gameObject);
            Reset();
        }
    }

    /* requirement: bullet need to be continous dynamic */
    void OnCollisionEnter(Collision collision) {
        /* Note: if bullet hasn't been spawned, isServer returns false even if the code is executed in server */
        ArmorController ac = collision.collider.GetComponent<ArmorController>();
        if (ac != null) {
            if (gameObject.name.ToLower().Contains("17mm"))
                AssetManager.singleton.PlayClipAtPoint(AssetManager.singleton.hit_17mm, transform.position);
            else
                AssetManager.singleton.PlayClipAtPoint(AssetManager.singleton.hit_42mm, transform.position);

            if (NetworkServer.active)
                ac.TakeHit(collision, this.gameObject);
        }
        f_col = true;
    }
}
