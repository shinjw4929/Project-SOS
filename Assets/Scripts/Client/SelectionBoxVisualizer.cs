using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Client; // SelectionBox가 정의된 namespace

public class SelectionBoxVisualizer : MonoBehaviour
{
    [SerializeField] private RectTransform boxRect; // 인스펙터에서 본인(Image) 연결
    private EntityManager _entityManager;
    private EntityQuery _selectionBoxQuery;

    private void Start()
    {
        // ECS World 가져오기
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null)
        {
            _entityManager = world.EntityManager;
            // SelectionBox 컴포넌트가 있는 엔티티를 찾기 위한 쿼리 생성
            _selectionBoxQuery = _entityManager.CreateEntityQuery(typeof(SelectionBox));
        }

        if (boxRect == null) boxRect = GetComponent<RectTransform>();
        
        // 마우스 좌표계와 UI 좌표계 일치를 위해 Pivot 설정 (좌측 하단)
        boxRect.pivot = new Vector2(0, 0);
        boxRect.anchorMin = Vector2.zero;
        boxRect.anchorMax = Vector2.zero;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        // 1. ECS 월드나 쿼리가 유효한지, 데이터가 생성되었는지 확인
        if (_entityManager == default || !_selectionBoxQuery.HasSingleton<SelectionBox>())
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        // 2. 싱글톤 데이터 읽어오기
        var selectionBox = _selectionBoxQuery.GetSingleton<SelectionBox>();

        // 3. 드래그 중일 때만 표시
        if (selectionBox.isDragging)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            UpdateBoxSize(selectionBox.startScreenPos, selectionBox.currentScreenPos);
        }
        else
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }

    private void UpdateBoxSize(float2 startPos, float2 currentPos)
    {
        // 시작점과 현재점 사이의 크기 계산
        float width = currentPos.x - startPos.x;
        float height = currentPos.y - startPos.y;

        // 음수 크기(역방향 드래그) 처리
        // RectTransform은 너비/높이가 양수여야 하므로 위치를 조정해야 함
        float x = startPos.x;
        float y = startPos.y;

        if (width < 0)
        {
            x = currentPos.x;
            width = Mathf.Abs(width);
        }

        if (height < 0)
        {
            y = currentPos.y;
            height = Mathf.Abs(height);
        }

        // UI 위치 및 크기 적용
        boxRect.anchoredPosition = new Vector2(x, y);
        boxRect.sizeDelta = new Vector2(width, height);
    }
}