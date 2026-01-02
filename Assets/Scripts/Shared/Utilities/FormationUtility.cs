using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 다중 유닛 분산 도착 위치 계산 유틸리티
    /// 여러 유닛이 같은 목표에 이동할 때 겹치지 않도록 분산 배치
    /// </summary>
    public static class FormationUtility
    {
        /// <summary>
        /// 분산 도착 위치 계산 (원형 배치)
        /// </summary>
        /// <param name="center">중심점 (우클릭 위치)</param>
        /// <param name="unitIndex">유닛 인덱스 (0부터 시작)</param>
        /// <param name="totalUnits">총 유닛 수</param>
        /// <param name="spacing">유닛 간 간격 (기본 1.25)</param>
        /// <returns>해당 유닛의 분산된 목표 위치</returns>
        public static float3 CalculateFormationPosition(
            float3 center,
            int unitIndex,
            int totalUnits,
            float spacing = 1.25f)
        {
            // 1명: 정확히 중앙
            if (totalUnits <= 1)
                return center;

            // 첫 번째 유닛은 중앙
            if (unitIndex == 0)
                return center;

            // 나머지 유닛은 원형 레이어에 배치
            int adjustedIndex = unitIndex - 1; // 중앙 유닛 제외
            int layer = 0;
            int unitsInPreviousLayers = 0;
            int unitsInCurrentLayer = 6; // 첫 번째 레이어는 6개 (육각형)

            // 어느 레이어에 속하는지 계산
            while (adjustedIndex >= unitsInCurrentLayer)
            {
                adjustedIndex -= unitsInCurrentLayer;
                unitsInPreviousLayers += unitsInCurrentLayer;
                layer++;
                unitsInCurrentLayer = 6 * (layer + 1); // 레이어마다 6개씩 증가
            }

            // 해당 레이어에서의 위치 계산
            float radius = spacing * (layer + 1);
            float angleStep = math.PI * 2f / unitsInCurrentLayer;
            float angle = angleStep * adjustedIndex;

            // 각 레이어마다 약간 회전시켜 자연스러운 배치
            float layerRotation = layer * 0.5f;
            angle += layerRotation;

            return new float3(
                center.x + math.cos(angle) * radius,
                center.y,
                center.z + math.sin(angle) * radius
            );
        }

        /// <summary>
        /// NavMesh 위의 유효한 위치인지 확인 후 보정된 위치 반환
        /// (NavMesh SamplePosition 래핑 - 필요 시 구현)
        /// </summary>
        public static float3 ValidatePositionOnNavMesh(float3 position, float maxDistance = 2f)
        {
            // NavMesh.SamplePosition은 메인 스레드에서만 호출 가능
            // 필요 시 PathfindingSystem에서 호출하도록 구현
            return position;
        }
    }
}
