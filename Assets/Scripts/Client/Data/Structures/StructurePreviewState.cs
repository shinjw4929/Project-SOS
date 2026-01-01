using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    /// <summary>
    /// 프리뷰 배치 상태 (3단계)
    /// </summary>
    public enum PlacementStatus : byte
    {
        Invalid = 0,        // 건설 불가 (빨간색)
        ValidInRange = 1,   // 건설 가능 + 사거리 내 (초록색)
        ValidOutOfRange = 2 // 건설 가능 + 사거리 밖 (노란색)
    }

    /// <summary>
    /// 건물 배치 프리뷰 상태 싱글톤
    /// - 현재 선택된 건물 타입
    /// - 마우스가 가리키는 그리드 좌표
    /// - 현재 위치에 배치 가능 여부 (BuildingPreviewUpdateSystem에서 업데이트)
    /// </summary>
    public struct StructurePreviewState : IComponentData
    {
        // 선택된 건물의 원본 프리팹 엔티티
        public Entity SelectedPrefab;
        // 서버 전송용 인덱스
        public int SelectedPrefabIndex;
        // 마우스가 가리키는 그리드 좌표
        public int2 GridPosition;
        // 현재 위치에 건물 배치 가능 여부 (충돌 체크 결과) - 하위 호환용
        public bool IsValidPlacement;
        // 3단계 배치 상태 (Invalid/ValidInRange/ValidOutOfRange)
        public PlacementStatus Status;
        // 유닛과 건물 간 거리 (디버깅/UI용)
        public float DistanceToBuilder;
    }
}
