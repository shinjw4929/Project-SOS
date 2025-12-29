using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Client;

/// <summary>
/// 드래그 선택 박스 UI 렌더링
/// - SelectionState의 Phase가 Dragging일 때 박스 표시
/// </summary>
public class SelectionBoxRenderer : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private RectTransform selectionBoxRect;
    [SerializeField] private Canvas parentCanvas;

    // ECS 캐싱
    private EntityManager _entityManager;
    private EntityQuery _selectionStateQuery;
    private bool _isInitialized;

    // Canvas 좌표 변환용
    private RectTransform _canvasRect;

    private void Start()
    {
        if (selectionBoxRect != null)
        {
            selectionBoxRect.gameObject.SetActive(false);
            selectionBoxRect.pivot = Vector2.zero;
            selectionBoxRect.anchorMin = Vector2.zero;
            selectionBoxRect.anchorMax = Vector2.zero;
        }

        if (parentCanvas != null)
        {
            _canvasRect = parentCanvas.GetComponent<RectTransform>();
        }
    }

    private void Update()
    {
        if (!_isInitialized || !_entityManager.World.IsCreated)
        {
            if (!TryInitClientWorld()) return;
        }

        if (_selectionStateQuery.IsEmptyIgnoreFilter)
        {
            HideBox();
            return;
        }

        var selectionState = _selectionStateQuery.GetSingleton<SelectionState>();

        // Dragging 상태일 때만 박스 표시
        if (selectionState.Phase == SelectionPhase.Dragging)
        {
            ShowBox();
            UpdateBoxRect(selectionState.StartScreenPos, selectionState.CurrentScreenPos);
        }
        else
        {
            HideBox();
        }
    }

    private bool TryInitClientWorld()
    {
        foreach (var world in World.All)
        {
            if (world == null || !world.IsCreated) continue;

            var query = world.EntityManager.CreateEntityQuery(typeof(SelectionState));
            if (!query.IsEmptyIgnoreFilter)
            {
                _entityManager = world.EntityManager;
                _selectionStateQuery = query;
                _isInitialized = true;
                return true;
            }
        }
        return false;
    }

    private void ShowBox()
    {
        if (!selectionBoxRect.gameObject.activeSelf)
            selectionBoxRect.gameObject.SetActive(true);
    }

    private void HideBox()
    {
        if (selectionBoxRect.gameObject.activeSelf)
            selectionBoxRect.gameObject.SetActive(false);
    }

    private void UpdateBoxRect(float2 start, float2 current)
    {
        Vector2 startLocal = ScreenToCanvasPosition(new Vector2(start.x, start.y));
        Vector2 currentLocal = ScreenToCanvasPosition(new Vector2(current.x, current.y));

        float width = currentLocal.x - startLocal.x;
        float height = currentLocal.y - startLocal.y;
        float x = startLocal.x;
        float y = startLocal.y;

        if (width < 0)
        {
            x = currentLocal.x;
            width = -width;
        }

        if (height < 0)
        {
            y = currentLocal.y;
            height = -height;
        }

        selectionBoxRect.anchoredPosition = new Vector2(x, y);
        selectionBoxRect.sizeDelta = new Vector2(width, height);
    }

    private Vector2 ScreenToCanvasPosition(Vector2 screenPos)
    {
        if (_canvasRect == null) return screenPos;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            screenPos,
            parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera,
            out Vector2 localPoint);

        // Canvas pivot(0.5, 0.5)에 맞춰 조절 
        Vector2 canvasSize = _canvasRect.rect.size;
        Vector2 pivotOffset = _canvasRect.pivot * canvasSize;
        return localPoint + pivotOffset;
    }
}