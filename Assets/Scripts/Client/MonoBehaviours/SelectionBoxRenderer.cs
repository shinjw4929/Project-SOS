using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Shared;

public class SelectionBoxRenderer : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private RectTransform selectionBoxRect;
    
    // ECS 캐싱 변수
    private EntityManager _clientEntityManager;
    private EntityQuery _selectionBoxQuery;
    private bool _isInitialized = false;

    private void Start()
    {
        if (selectionBoxRect != null)
        {
            // 초기화: 박스 숨기기 및 피벗 설정
            selectionBoxRect.gameObject.SetActive(false);
            selectionBoxRect.pivot = Vector2.zero; // 좌하단 기준
            selectionBoxRect.anchorMin = Vector2.zero;
            selectionBoxRect.anchorMax = Vector2.zero;
        }
    }

    private void Update()
    {
        // 1. ECS 월드 연결 시도 (아직 연결 안 됐거나, 연결 끊긴 경우)
        if (!_isInitialized || !_clientEntityManager.World.IsCreated)
        {
            if (!TryInitClientWorld()) return;
        }

        // 2. 쿼리에 데이터(싱글톤)가 있는지 확인
        if (_selectionBoxQuery.IsEmptyIgnoreFilter)
        {
            if (selectionBoxRect.gameObject.activeSelf) selectionBoxRect.gameObject.SetActive(false);
            return;
        }

        // 3. 데이터 읽기
        var selectionBox = _selectionBoxQuery.GetSingleton<SelectionBox>();

        // 4. 드래그 상태에 따라 UI 갱신
        if (selectionBox.isDragging)
        {
            if (!selectionBoxRect.gameObject.activeSelf) selectionBoxRect.gameObject.SetActive(true);
            UpdateBoxRect(selectionBox.startScreenPos, selectionBox.currentScreenPos);
        }
        else
        {
            if (selectionBoxRect.gameObject.activeSelf) selectionBoxRect.gameObject.SetActive(false);
        }
    }

    // Netcode 환경에서 안전하게 Client World를 찾는 로직
    private bool TryInitClientWorld()
    {
        foreach (var world in World.All)
        {
            // ClientSystem 태그가 있거나, SelectionBox를 가진 월드를 탐색
            if (world == null || !world.IsCreated) continue;

            var query = world.EntityManager.CreateEntityQuery(typeof(SelectionBox));
            if (!query.IsEmptyIgnoreFilter)
            {
                _clientEntityManager = world.EntityManager;
                _selectionBoxQuery = query;
                _isInitialized = true;
                return true;
            }
        }
        return false;
    }

    private void UpdateBoxRect(float2 start, float2 current)
    {
        // 너비와 높이 계산
        float width = current.x - start.x;
        float height = current.y - start.y;

        // RectTransform은 음수 사이즈를 가질 수 없으므로 위치와 크기를 보정
        // 예: 오른쪽 위에서 왼쪽 아래로 드래그할 때 처리
        float x = start.x;
        float y = start.y;

        if (width < 0)
        {
            x = current.x;
            width = -width; // 절대값
        }

        if (height < 0)
        {
            y = current.y;
            height = -height; // 절대값
        }

        // UI에 적용
        selectionBoxRect.anchoredPosition = new Vector2(x, y);
        selectionBoxRect.sizeDelta = new Vector2(width, height);
    }
}