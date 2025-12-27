using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    /// <summary>
    /// 건물 배치 프리뷰 상태 싱글톤
    /// - 현재 선택된 건물 타입
    /// - 마우스가 가리키는 그리드 좌표
    /// - 현재 위치에 배치 가능 여부 (BuildingPreviewUpdateSystem에서 업데이트)
    /// </summary>
    public struct StructurePreviewState : IComponentData
    {
        // 선택된 건물의 "원본 프리팹 엔티티
        public Entity SelectedPrefab;
        // 서버 전송용 인덱스
        public int SelectedPrefabIndex;
        // summary>마우스가 가리키는 그리드 좌표
        public int2 GridPosition;
        // <summary>현재 위치에 건물 배치 가능 여부 (충돌 체크 결과)
        public bool IsValidPlacement;
    }
}
