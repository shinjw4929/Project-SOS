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

        public class Baker : Baker<GameSettingsAuthoring>
        {
            public override void Bake(GameSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new GameSettings
                {
                    InitialWallDecayTime = authoring.initialWallDecayTime
                });
            }
        }
    }
}
