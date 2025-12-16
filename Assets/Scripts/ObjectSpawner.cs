using UnityEngine;

//Swapn Object
public class ObjectSpawner : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("생성할 프리팹")]
    [SerializeField] private GameObject prefabToSpawn;



    public void Spawn()
    {
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("프리팹이 설정되지 않았습니다.");
            return;
        }

        Instantiate(prefabToSpawn, transform.position + new Vector3(30, prefabToSpawn.transform.position.y, 0), transform.rotation);
    }
}