using Unity.Entities;
using Shared;

namespace Client
{
    /// <summary>
    /// 건물 배치 프리뷰 상태 싱글톤
    /// - 현재 선택된 건물 타입
    /// - 마우스가 가리키는 그리드 좌표
    /// - 현재 위치에 배치 가능 여부 (BuildingPreviewUpdateSystem에서 업데이트)
    /// </summary>
    public struct BuildingPreviewState : IComponentData
    {
        /// <summary>현재 선택된 건물 타입 (Wall, Barracks 등)</summary>
        public BuildingTypeEnum selectedType;

        /// <summary>마우스가 가리키는 그리드 X 좌표</summary>
        public int gridX;

        /// <summary>마우스가 가리키는 그리드 Y 좌표</summary>
        public int gridY;

        /// <summary>현재 위치에 건물 배치 가능 여부 (충돌 체크 결과)</summary>
        public bool isValidPlacement;
    }
}
