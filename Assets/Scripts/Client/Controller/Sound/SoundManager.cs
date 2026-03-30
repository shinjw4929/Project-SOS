using System;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// ECS SoundEvent 버퍼를 소비하여 AudioSource 풀로 사운드를 재생하는 MonoBehaviour.
    /// 씬에 빈 GameObject를 만들고 이 컴포넌트를 추가하여 사용.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        [Header("MeleeHit (아군 근접 타격)")]
        [SerializeField] private AudioClip meleeHitClip;
        [SerializeField, Range(0f, 1f)] private float meleeHitVolume = 1f;

        [Header("RangedShot (아군 원거리 발사)")]
        [SerializeField] private AudioClip rangedShotClip;
        [SerializeField, Range(0f, 1f)] private float rangedShotVolume = 1f;

        [Header("UnitDeath (아군 사망)")]
        [SerializeField] private AudioClip unitDeathClip;
        [SerializeField, Range(0f, 1f)] private float unitDeathVolume = 1f;

        [Header("EnemyDeath (적 사망)")]
        [SerializeField] private AudioClip enemyDeathClip;
        [SerializeField, Range(0f, 1f)] private float enemyDeathVolume = 1f;

        [Header("EnemyMeleeHit (적 근접 타격)")]
        [SerializeField] private AudioClip enemyMeleeHitClip;
        [SerializeField, Range(0f, 1f)] private float enemyMeleeHitVolume = 1f;

        [Header("EnemyRangedShot (적 원거리 발사)")]
        [SerializeField] private AudioClip enemyRangedShotClip;
        [SerializeField, Range(0f, 1f)] private float enemyRangedShotVolume = 1f;

        [Header("UnitSpawn (유닛 스폰)")]
        [SerializeField] private AudioClip unitSpawnClip;
        [SerializeField, Range(0f, 1f)] private float unitSpawnVolume = 1f;

        [Header("WorkerGather (채집)")]
        [SerializeField] private AudioClip workerGatherClip;
        [SerializeField, Range(0f, 1f)] private float workerGatherVolume = 1f;

        [Header("BuildingPlace (건설 배치)")]
        [SerializeField] private AudioClip buildingPlaceClip;
        [SerializeField, Range(0f, 1f)] private float buildingPlaceVolume = 1f;

        [Header("BuildingComplete (건설 완료)")]
        [SerializeField] private AudioClip buildingCompleteClip;
        [SerializeField, Range(0f, 1f)] private float buildingCompleteVolume = 1f;

        [Header("Settings")]
        [SerializeField] private int poolSize = 32;
        [SerializeField] private float cullingDistance = 80f;
        [SerializeField] private int maxConcurrentPerType = 3;

        [Header("3D Audio")]
        [SerializeField] private float minDistance = 5f;
        [SerializeField] private float maxDistance = 80f;

        private AudioSource[] audioPool;
        private int poolIndex;
        private World clientWorld;
        private Entity soundEventEntity;
        private EntityQuery soundEventQuery;

        // 동시 재생 카운트 (SoundType.byte 값 기준)
        private int[] concurrentCounts;

        private void Awake()
        {
            audioPool = new AudioSource[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"AudioSource_{i}");
                go.transform.SetParent(transform);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1.0f;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.rolloffMode = AudioRolloffMode.Linear;
                audioPool[i] = source;
            }

            concurrentCounts = new int[256];
        }

        private void Update()
        {
            if (clientWorld == null || !clientWorld.IsCreated)
            {
                FindClientWorld();
                if (clientWorld == null) return;
            }

            var entityManager = clientWorld.EntityManager;

            if (!entityManager.Exists(soundEventEntity) ||
                !entityManager.HasBuffer<SoundEvent>(soundEventEntity))
            {
                FindSoundEventEntity(entityManager);
                if (soundEventEntity == Entity.Null) return;
            }

            var buffer = entityManager.GetBuffer<SoundEvent>(soundEventEntity);
            if (buffer.Length == 0) return;

            var cameraPos = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero;

            // 동시 재생 카운트 리셋
            Array.Clear(concurrentCounts, 0, concurrentCounts.Length);

            // 현재 재생 중인 AudioSource의 타입 카운트
            for (int i = 0; i < audioPool.Length; i++)
            {
                if (audioPool[i].isPlaying)
                    concurrentCounts[(int)GetSoundTypeForSource(i)]++;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                var evt = buffer[i];

                // 거리 컬링
                float distance = math.distance(cameraPos, evt.Position);
                if (distance > cullingDistance) continue;

                // 동시 재생 제한
                int typeIndex = (int)evt.Type;
                if (concurrentCounts[typeIndex] >= maxConcurrentPerType) continue;

                // AudioClip 매핑
                var clip = GetClip(evt.Type);
                if (clip == null) continue;

                // 풀에서 AudioSource 할당 (라운드로빈)
                var source = audioPool[poolIndex];
                source.transform.position = new Vector3(evt.Position.x, evt.Position.y, evt.Position.z);
                source.clip = clip;
                source.volume = evt.Volume * GetTypeVolume(evt.Type);
                source.Play();

                concurrentCounts[typeIndex]++;
                poolIndex = (poolIndex + 1) % poolSize;
            }

            buffer.Clear();
        }

        private void OnDestroy()
        {
            if (audioPool == null) return;
            for (int i = 0; i < audioPool.Length; i++)
            {
                if (audioPool[i] != null)
                    audioPool[i].Stop();
            }
        }

        private void FindClientWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    clientWorld = world;
                    soundEventQuery = world.EntityManager.CreateEntityQuery(typeof(SoundEventState));
                    FindSoundEventEntity(world.EntityManager);
                    return;
                }
            }
        }

        private void FindSoundEventEntity(EntityManager entityManager)
        {
            soundEventEntity = Entity.Null;
            if (!soundEventQuery.IsEmptyIgnoreFilter)
                soundEventEntity = soundEventQuery.GetSingletonEntity();
        }

        private AudioClip GetClip(SoundType type) => type switch
        {
            SoundType.MeleeHit         => meleeHitClip,
            SoundType.RangedShot       => rangedShotClip,
            SoundType.UnitDeath        => unitDeathClip,
            SoundType.EnemyDeath       => enemyDeathClip,
            SoundType.EnemyMeleeHit    => enemyMeleeHitClip,
            SoundType.EnemyRangedShot  => enemyRangedShotClip,
            SoundType.UnitSpawn        => unitSpawnClip,
            SoundType.WorkerGather     => workerGatherClip,
            SoundType.BuildingPlace    => buildingPlaceClip,
            SoundType.BuildingComplete => buildingCompleteClip,
            _                          => null,
        };

        private float GetTypeVolume(SoundType type) => type switch
        {
            SoundType.MeleeHit         => meleeHitVolume,
            SoundType.RangedShot       => rangedShotVolume,
            SoundType.UnitDeath        => unitDeathVolume,
            SoundType.EnemyDeath       => enemyDeathVolume,
            SoundType.EnemyMeleeHit    => enemyMeleeHitVolume,
            SoundType.EnemyRangedShot  => enemyRangedShotVolume,
            SoundType.UnitSpawn        => unitSpawnVolume,
            SoundType.WorkerGather     => workerGatherVolume,
            SoundType.BuildingPlace    => buildingPlaceVolume,
            SoundType.BuildingComplete => buildingCompleteVolume,
            _                          => 1f,
        };

        // 풀 인덱스 기반 타입 추적은 하지 않으므로, 재생 중인 소스의 clip으로 역매핑
        private SoundType GetSoundTypeForSource(int index)
        {
            var source = audioPool[index];
            if (source.clip == null) return SoundType.None;

            if (source.clip == meleeHitClip) return SoundType.MeleeHit;
            if (source.clip == rangedShotClip) return SoundType.RangedShot;
            if (source.clip == unitDeathClip) return SoundType.UnitDeath;
            if (source.clip == enemyDeathClip) return SoundType.EnemyDeath;
            if (source.clip == enemyMeleeHitClip) return SoundType.EnemyMeleeHit;
            if (source.clip == enemyRangedShotClip) return SoundType.EnemyRangedShot;
            if (source.clip == unitSpawnClip) return SoundType.UnitSpawn;
            if (source.clip == workerGatherClip) return SoundType.WorkerGather;
            if (source.clip == buildingPlaceClip) return SoundType.BuildingPlace;
            if (source.clip == buildingCompleteClip) return SoundType.BuildingComplete;

            return SoundType.None;
        }
    }
}
