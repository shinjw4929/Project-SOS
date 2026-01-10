using Unity.Entities;
using Unity.Mathematics;

namespace Server
{
    /// <summary>
    /// 병렬 검증 Job(Validate)에서 직렬 실행 Job(Execute)으로 데이터를 전달하기 위한 임시 구조체입니다.
    /// <para>
    /// 물리 연산과 그리드 검사 같은 무거운 작업은 병렬로 처리하고, 
    /// 자원 차감(Race Condition 방지)과 엔티티 생성은 직렬로 처리하기 위해 사용합니다.
    /// </para>
    /// </summary>
    public struct BuildActionRequest // : IComponentData는 NativeQueue에만 넣을 거라면 필요 없습니다.
    {
        /// <summary>요청을 보낸 RPC 엔티티 (처리 후 제거됨)</summary>
        public Entity RpcEntity;

        /// <summary>생성할 건물의 원본 프리팹 엔티티</summary>
        public Entity PrefabEntity;

        /// <summary>건설을 요청한 유저의 NetworkId (자원 차감 및 소유권 설정용)</summary>
        public int SourceNetworkId;

        /// <summary>요청을 보낸 연결 엔티티 (알림 RPC 전송용)</summary>
        public Entity SourceConnection;

        /// <summary>건설할 그리드 좌표 (논리적 위치)</summary>
        public int2 GridPosition;

        /// <summary>
        /// [최적화] Job 1에서 미리 계산된 월드 좌표 (높이 보정 포함).
        /// <br/>Job 2에서 다시 연산하지 않기 위해 캐싱하여 전달합니다.
        /// </summary>
        public float3 TargetWorldPos;

        /// <summary>건물 건설 비용 (자원 확인용)</summary>
        public int StructureCost;

        /// <summary>
        /// 물리 충돌 및 그리드 점유 검사 통과 여부.
        /// <br/>false일 경우 Job 2에서 자원 차감 없이 RPC만 제거합니다.
        /// </summary>
        public bool IsValidPhysics;
    }
}
