using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 게임 운영 설정 Authoring.
    /// EntitiesSubScene에 빈 GameObject를 만들고 이 컴포넌트를 추가하여 사용.
    /// </summary>
    public class GameSettingsAuthoring : MonoBehaviour
    {
        [Header("Initial Wall Settings")]
        [Tooltip("초기 배치 벽이 자동 파괴되기까지 걸리는 시간 (초)")]
        [Min(0f)]
        public float initialWallDecayTime = 30f;

        [Header("Wave0 Settings")]
        [Tooltip("게임 시작 시 초기 스폰할 적 수")]
        [Min(0)]
        public int wave0InitialSpawnCount = 30;

        [Header("Wave Transition Conditions")]
        [Tooltip("Wave1 전환 경과 시간 (초)")]
        [Min(1f)]
        public float wave1TriggerTime = 60f;
        [Tooltip("Wave1 전환 처치 수")]
        [Min(1)]
        public int wave1TriggerKillCount = 15;
        [Tooltip("Wave2 전환 경과 시간 (초)")]
        [Min(1f)]
        public float wave2TriggerTime = 120f;
        [Tooltip("Wave2 전환 처치 수")]
        [Min(1)]
        public int wave2TriggerKillCount = 30;

        [Header("Enemy Limit")]
        [Tooltip("맵에 존재할 수 있는 최대 적 수")]
        [Min(1)]
        public int maxEnemyCount = 1200;

        [Header("Wave1+ Spawn Settings")]
        [Tooltip("Wave1 적 스폰 주기 (초)")]
        [Min(0.5f)]
        public float wave1SpawnInterval = 5f;
        [Tooltip("Wave1 1회 스폰 수")]
        [Min(1)]
        public int wave1SpawnCount = 3;
        [Tooltip("Wave2 적 스폰 주기 (초)")]
        [Min(0.5f)]
        public float wave2SpawnInterval = 4f;
        [Tooltip("Wave2 1회 스폰 수")]
        [Min(1)]
        public int wave2SpawnCount = 4;

        public class Baker : Baker<GameSettingsAuthoring>
        {
            public override void Bake(GameSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new GameSettings
                {
                    InitialWallDecayTime = authoring.initialWallDecayTime,
                    Wave0InitialSpawnCount = authoring.wave0InitialSpawnCount,
                    Wave1TriggerTime = authoring.wave1TriggerTime,
                    Wave1TriggerKillCount = authoring.wave1TriggerKillCount,
                    Wave2TriggerTime = authoring.wave2TriggerTime,
                    Wave2TriggerKillCount = authoring.wave2TriggerKillCount,
                    Wave1SpawnInterval = authoring.wave1SpawnInterval,
                    Wave1SpawnCount = authoring.wave1SpawnCount,
                    Wave2SpawnInterval = authoring.wave2SpawnInterval,
                    Wave2SpawnCount = authoring.wave2SpawnCount,
                    MaxEnemyCount = authoring.maxEnemyCount
                });
            }
        }
    }
}
