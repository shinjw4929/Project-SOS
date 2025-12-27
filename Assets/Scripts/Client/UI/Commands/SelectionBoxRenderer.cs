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

    // ECS 캐싱
    private EntityManager _entityManager;
    private EntityQuery _selectionStateQuery;
    private bool _isInitialized;

    private void Start()
    {
        if (selectionBoxRect != null)
        {
            selectionBoxRect.gameObject.SetActive(false);
            selectionBoxRect.pivot = Vector2.zero;
            selectionBoxRect.anchorMin = Vector2.zero;
            selectionBoxRect.anchorMax = Vector2.zero;
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
        float width = current.x - start.x;
        float height = current.y - start.y;
        float x = start.x;
        float y = start.y;

        if (width < 0)
        {
            x = current.x;
            width = -width;
        }

        if (height < 0)
        {
            y = current.y;
            height = -height;
        }

        selectionBoxRect.anchoredPosition = new Vector2(x, y);
        selectionBoxRect.sizeDelta = new Vector2(width, height);
    }
}