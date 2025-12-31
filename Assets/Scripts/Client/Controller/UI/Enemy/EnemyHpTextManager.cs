using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public class EnemyHpTextManager : MonoBehaviour
{
    [SerializeField] private TextMeshPro text3dPrefab;

    [Header("������(���� ����)  x=��/��, y=��/�Ʒ�, z=��/��")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.0f, 0f);

    [Header("ũ��")]
    [SerializeField] private float uniformScale = 1.0f;

    private World _world;
    private EntityManager _em;

    private readonly Dictionary<Entity, TextMeshPro> _map = new();

    void Awake()
    {
        if (!text3dPrefab) // Unity Object는 implicit bool 사용
        {
            Debug.LogError("EnemyHpTextManager: text3dPrefab 프리팹을 할당해야 합니다.");
            enabled = false;
        }
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        TryBindClientWorld();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    void OnDestroy()
    {
        foreach (var kv in _map)
        {
            if (kv.Value) Destroy(kv.Value.gameObject); // Unity Object는 implicit bool 사용
        }
        _map.Clear();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!cam || cam != Camera.main) return; // Unity Object는 implicit bool 사용

        if (!EnsureClientWorldReady())
            return;

        EntityQuery q;
        try
        {
            q = _em.CreateEntityQuery(
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<EnemyHealthData>()
            );
        }
        catch { return; }

        NativeArray<Entity> entities;
        try { entities = q.ToEntityArray(Allocator.Temp); }
        catch { return; }

        using (entities)
        {
            var alive = new HashSet<Entity>();

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                alive.Add(e);

                if (!_map.TryGetValue(e, out var tmp) || !tmp) // Unity Object는 implicit bool 사용
                {
                    tmp = Instantiate(text3dPrefab);
                    if (!tmp) continue; // Unity Object는 implicit bool 사용
                    _map[e] = tmp;
                }

                tmp.transform.localScale = Vector3.one * uniformScale;

                LocalToWorld ltw;
                EnemyHealthData hp;

                try
                {
                    ltw = _em.GetComponentData<LocalToWorld>(e);
                    hp = _em.GetComponentData<EnemyHealthData>(e);
                }
                catch { continue; }

                // LocalToWorld ��Ŀ��� ��/��ġ ����
                float4x4 m = ltw.Value;
                float3 pos = m.c3.xyz;
                float3 right = math.normalizesafe(m.c0.xyz, new float3(1, 0, 0));
                float3 up = math.normalizesafe(m.c1.xyz, new float3(0, 1, 0));
                float3 forward = math.normalizesafe(m.c2.xyz, new float3(0, 0, 1));

                float3 wpos =
                    pos +
                    right * localOffset.x +
                    up * localOffset.y +
                    forward * localOffset.z;

                tmp.transform.position = (Vector3)wpos;

                // "UIó�� ����" = ī�޶� ȸ�� �״��
                tmp.transform.rotation = cam.transform.rotation;

                tmp.text = $"{hp.Current}/{hp.Max}";
            }

            // ����� ��ƼƼ ����
            var toRemove = new List<Entity>();
            foreach (var kv in _map)
            {
                if (!alive.Contains(kv.Key))
                {
                    if (kv.Value) Destroy(kv.Value.gameObject); // Unity Object는 implicit bool 사용
                    toRemove.Add(kv.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
                _map.Remove(toRemove[i]);
        }
    }

    private bool EnsureClientWorldReady()
    {
        // 월드가 없거나 유효하지 않으면 다시 바인딩
        if (!_world.IsCreated || ((_world.Flags & WorldFlags.GameServer) != 0))
        {
            TryBindClientWorld();
        }

        if (!_world.IsCreated)
            return false;

        // EntityManager�� ������ IsCreated üũ ���� try/catch
        try { _em = _world.EntityManager; }
        catch { return false; }

        // �� ���忡 �� ü�� �����Ͱ� ������ �ٽ� ã��
        try
        {
            using var testQ = _em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
            if (testQ.CalculateEntityCount() == 0)
            {
                TryBindClientWorld();
                if (!_world.IsCreated) return false;
                _em = _world.EntityManager;

                using var testQ2 = _em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
                if (testQ2.CalculateEntityCount() == 0)
                    return false;
            }
        }
        catch { return false; }

        return true;
    }

    private void TryBindClientWorld()
    {
        _world = FindClientWorldThatHasEnemyHealth();
        if (_world.IsCreated)
        {
            try { _em = _world.EntityManager; } catch { }
        }
    }

    // �ٽ�: "EnemyHealthData�� �ִ� ����" �߿��� �ݵ�� GameClient�� �켱 ����
    private static World FindClientWorldThatHasEnemyHealth()
    {
        // 1) GameClient ���� �� EnemyHealthData �ִ� ��
        foreach (var w in World.All)
        {
            if (!w.IsCreated) continue;
            if ((w.Flags & WorldFlags.GameClient) == 0) continue;

            EntityManager em;
            try { em = w.EntityManager; }
            catch { continue; }

            try
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
                if (q.CalculateEntityCount() > 0)
                    return w;
            }
            catch { }
        }

        // 2) �׷��� ������ "Client" �̸� ���� ����
        foreach (var w in World.All)
        {
            if (!w.IsCreated) continue;
            if (string.IsNullOrEmpty(w.Name) || !w.Name.Contains("Client")) continue;

            EntityManager em;
            try { em = w.EntityManager; }
            catch { continue; }

            try
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
                if (q.CalculateEntityCount() > 0)
                    return w;
            }
            catch { }
        }

        // 3) 최종 fallback
        if (World.DefaultGameObjectInjectionWorld.IsCreated)
            return World.DefaultGameObjectInjectionWorld;

        return default;
    }
}
