using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class EnemyHpTextManager : MonoBehaviour
{
    [SerializeField] private TextMeshPro text3dPrefab;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2f, 0f);
    [SerializeField] private float uniformScale = 1f;

    private World _world;
    private EntityManager _em;

    private readonly Dictionary<Entity, TextMeshPro> _map = new();

    void Awake()
    {
        if (text3dPrefab == null)
        {
            Debug.LogError("EnemyHpTextManager: text3dPrefab이 비어있습니다. EnemyHPText3D(TextMeshPro 3D) 프리팹을 넣으세요.");
            enabled = false;
        }
    }

    void Start()
    {
        TryBindWorld();
    }

    void LateUpdate()
    {
        if (!EnsureWorldReady())
            return;

        Camera cam = Camera.main;
        if (cam == null) return;

        EntityQuery q;
        try
        {
            q = _em.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<EnemyHealthData>()
            );
        }
        catch
        {
            return;
        }

        NativeArray<Entity> entities;
        try
        {
            entities = q.ToEntityArray(Allocator.Temp);
        }
        catch
        {
            return;
        }

        using (entities)
        {
            var alive = new HashSet<Entity>();

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                alive.Add(e);

                if (!_map.TryGetValue(e, out TextMeshPro tmp) || tmp == null)
                {
                    tmp = Instantiate(text3dPrefab);
                    if (tmp == null) continue;
                    _map[e] = tmp;
                }

                if (uniformScale > 0f)
                    tmp.transform.localScale = Vector3.one * uniformScale;

                LocalTransform lt;
                EnemyHealthData hp;

                try
                {
                    lt = _em.GetComponentData<LocalTransform>(e);
                    hp = _em.GetComponentData<EnemyHealthData>(e);
                }
                catch
                {
                    continue;
                }

                tmp.transform.position = (Vector3)lt.Position + worldOffset;
                tmp.transform.rotation = Quaternion.LookRotation(tmp.transform.position - cam.transform.position);

                tmp.text = $"{hp.Current}/{hp.Max}";
            }

            var toRemove = new List<Entity>();
            foreach (var kv in _map)
            {
                if (!alive.Contains(kv.Key))
                {
                    if (kv.Value != null) Destroy(kv.Value.gameObject);
                    toRemove.Add(kv.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
                _map.Remove(toRemove[i]);
        }
    }

    void OnDestroy()
    {
        foreach (var kv in _map)
        {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _map.Clear();
    }

    private bool EnsureWorldReady()
    {
        if (_world == null || !_world.IsCreated)
        {
            TryBindWorld();
        }

        if (_world == null || !_world.IsCreated)
            return false;

        try
        {
            _em = _world.EntityManager;
        }
        catch
        {
            return false;
        }

        // 적 체력 엔티티가 있는 월드가 아니면 다시 찾기
        try
        {
            using var testQ = _em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
            if (testQ.CalculateEntityCount() == 0)
            {
                TryBindWorld();
                if (_world == null || !_world.IsCreated) return false;
                _em = _world.EntityManager;

                using var testQ2 = _em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
                if (testQ2.CalculateEntityCount() == 0)
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private void TryBindWorld()
    {
        _world = FindWorldThatHasEnemyHealth();
        if (_world != null && _world.IsCreated)
        {
            _em = _world.EntityManager;
        }
    }

    private static World FindWorldThatHasEnemyHealth()
    {
        // 1) EnemyHealthData가 실제 존재하는 월드 우선
        foreach (var w in World.All)
        {
            if (!w.IsCreated) continue;

            EntityManager em;
            try { em = w.EntityManager; }
            catch { continue; }

            try
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
                if (q.CalculateEntityCount() > 0)
                    return w;
            }
            catch
            {
                continue;
            }
        }

        // 2) 그래도 없으면 GameClient 비슷한 월드(이름 기반)
        foreach (var w in World.All)
        {
            if (!w.IsCreated) continue;
            if (w.Name != null && w.Name.Contains("Client"))
                return w;
        }

        // 3) 최후
        if (World.DefaultGameObjectInjectionWorld != null && World.DefaultGameObjectInjectionWorld.IsCreated)
            return World.DefaultGameObjectInjectionWorld;

        return null;
    }
}
