using UnityEngine;

public class Projectile : MonoBehaviour
{
    public float speed = 40f;
    public float maxDistance = 500f;

    Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;

        if (Vector3.Distance(startPos, transform.position) >= maxDistance)
        {
            Destroy(gameObject);
        }
    }
}
