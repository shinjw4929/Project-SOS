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

    [Header("오프셋(로컬 기준)  x=좌/우, y=위/아래, z=앞/뒤")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.0f, 0f);

    [Header("크기")]
    [SerializeField] private float uniformScale = 1.0f;

    private World _world;
    private EntityManager _em;

    private readonly Dictionary<Entity, TextMeshPro> _map = new();

    void Awake()
    {
        if (text3dPrefab == null)
        {
            Debug.LogError("EnemyHpTextManager: text3dPrefab(EnemyHPText3D 프리팹)을 넣으셔야 합니다.");
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
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _map.Clear();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam == null || cam != Camera.main) return;

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

                if (!_map.TryGetValue(e, out var tmp) || tmp == null)
                {
                    tmp = Instantiate(text3dPrefab);
                    if (tmp == null) continue;
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

                // LocalToWorld 행렬에서 축/위치 추출
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

                // "UI처럼 정면" = 카메라 회전 그대로
                tmp.transform.rotation = cam.transform.rotation;

                tmp.text = $"{hp.Current}/{hp.Max}";
            }

            // 사라진 엔티티 정리
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

    private bool EnsureClientWorldReady()
    {
        // 월드가 없거나 서버월드면 다시 잡기
        if (_world == null || !_world.IsCreated || ((_world.Flags & WorldFlags.GameServer) != 0))
        {
            TryBindClientWorld();
        }

        if (_world == null || !_world.IsCreated)
            return false;

        // EntityManager는 버전별 IsCreated 체크 없이 try/catch
        try { _em = _world.EntityManager; }
        catch { return false; }

        // 이 월드에 적 체력 데이터가 없으면 다시 찾기
        try
        {
            using var testQ = _em.CreateEntityQuery(ComponentType.ReadOnly<EnemyHealthData>());
            if (testQ.CalculateEntityCount() == 0)
            {
                TryBindClientWorld();
                if (_world == null || !_world.IsCreated) return false;
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
        if (_world != null && _world.IsCreated)
        {
            try { _em = _world.EntityManager; } catch { }
        }
    }

    // 핵심: "EnemyHealthData가 있는 월드" 중에서 반드시 GameClient를 우선 선택
    private static World FindClientWorldThatHasEnemyHealth()
    {
        // 1) GameClient 월드 중 EnemyHealthData 있는 곳
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

        // 2) 그래도 없으면 "Client" 이름 포함 월드
        foreach (var w in World.All)
        {
            if (!w.IsCreated) continue;
            if (w.Name == null || !w.Name.Contains("Client")) continue;

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

        // 3) 최후 fallback
        if (World.DefaultGameObjectInjectionWorld != null && World.DefaultGameObjectInjectionWorld.IsCreated)
            return World.DefaultGameObjectInjectionWorld;

        return null;
    }
}
