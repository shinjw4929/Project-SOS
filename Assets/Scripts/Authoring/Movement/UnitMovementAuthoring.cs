using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 유닛 전용 명령/상태 시스템을 위한 Authoring
    /// - MovementAuthoring과 함께 사용 (이동 관련 공용 컴포넌트는 MovementAuthoring에서 처리)
    /// - 유닛 전용 컴포넌트만 베이킹 (UnitIntentState, UnitActionState, UnitCommand)
    /// </summary>
    [RequireComponent(typeof(MovementAuthoring))]
    public class UnitMovementAuthoring : MonoBehaviour
    {
        public class Baker : Baker<UnitMovementAuthoring>
        {
            public override void Bake(UnitMovementAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // ==========================================================
                // 1. High Level Logic (유닛 의도)
                // ==========================================================
                // 유닛의 의도 (Move, Attack, Build...)
                AddComponent(entity, new UnitIntentState
                {
                    State = Intent.Idle,
                    TargetEntity = Entity.Null
                });

                // ==========================================================
                // 2. Low Level State (유닛 액션)
                // ==========================================================
                // 유닛 상태 (애니메이션용)
                AddComponent(entity, new UnitActionState
                {
                    State = Action.Idle
                });

                // ==========================================================
                // 3. Buffers (명령 버퍼)
                // ==========================================================
                // 입력 명령 버퍼 (ICommandData)
                // Baker에서 AddBuffer를 호출하면 GhostAuthoring이 감지하여 자동으로 Input Buffer로 등록함
                AddBuffer<UnitCommand>(entity);
            }
        }
    }
}
