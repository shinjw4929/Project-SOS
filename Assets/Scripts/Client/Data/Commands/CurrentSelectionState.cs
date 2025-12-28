using Unity.Entities;

namespace Client
{
    /// <summary>
    /// 현재 선택 상태 싱글톤
    /// - UI 표시용 (선택된 엔티티 정보)
    /// </summary>
    public struct CurrentSelectionState : IComponentData
    {
        // 대표 선택 엔티티 (UI에서 정보 표시용, BuilderTag 등 체크에 사용)
        public Entity PrimaryEntity;

        // 선택된 엔티티 수
        public int SelectedCount;

        // 선택 타입 (유닛/건물/혼합)
        public SelectionCategory Category;

        // 내 소유 엔티티만 선택되었는지
        public bool IsOwnedSelection;
    }

    /// <summary>
    /// 선택된 엔티티의 카테고리
    /// </summary>
    public enum SelectionCategory : byte
    {
        None = 0,       // 선택 없음
        Units = 1,      // 유닛만 선택
        Structure = 2,  // 건물만 선택
    }
}
