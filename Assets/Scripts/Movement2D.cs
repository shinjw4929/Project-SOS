using UnityEngine;

public class Movement2D : MonoBehaviour
{
    [SerializeField]
    public float moveSpeed = 5f;

    public Vector3 MoveDirection { get; set; } = Vector3.zero;

    private void Update()
    {
        transform.position += MoveDirection * moveSpeed * Time.deltaTime;
    }
}
