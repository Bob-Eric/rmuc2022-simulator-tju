using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.Assertions;

public class Bullet : MonoBehaviour {
    [HideInInspector]
    public GameObject hitter;

    Rigidbody rb;
    int cnt_inactive; // counter for static or out of field
    bool collided; // if bullet has been collided with something
    void Awake() {
        Reset();
    }

    void Reset() {
        rb = GetComponent<Rigidbody>();
        rb.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        cnt_inactive = 0;
        collided = false;
    }

    void FixedUpdate() {
        Assert.IsTrue(rb != null);

        // simulate friction to make bullet stop
        if (collided) {
            rb.velocity *= 0.99f;
            rb.angularVelocity *= 0.99f;
        }

        if ((rb.velocity.magnitude < 0.01f && rb.angularVelocity.magnitude < 0.01f) || !BattleField.singleton.OnField(gameObject))
            cnt_inactive++;
        else
            cnt_inactive = 0;

        if (cnt_inactive >= 5) {
            BulletPool.singleton.RemoveBullet(gameObject);
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
                ac.TakeHit(collision, gameObject);
        }
        collided = true;
    }
}
