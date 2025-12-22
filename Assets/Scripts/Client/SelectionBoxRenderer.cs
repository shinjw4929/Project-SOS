using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Client
{
    public class SelectionBoxRenderer : MonoBehaviour
    {
        private Texture2D _whiteTexture;
        
        // 캐싱용 변수
        private EntityManager _clientEntityManager;
        private EntityQuery _selectionBoxQuery;
        private bool _isInitialized = false;

        // 렌더링 데이터 (Update -> OnGUI 전달용)
        private bool _shouldDraw;
        private Rect _drawRect;

        private void Start()
        {
            // 텍스처 미리 생성 (메모리 할당 최적화)
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        private void Update()
        {
            // 1. 초기화가 안 되어 있거나, 월드가 파괴되었으면 다시 찾기
            if (!_isInitialized || !_clientEntityManager.World.IsCreated)
            {
                if (!TryInitClientWorld()) return;
            }

            // 2. 데이터가 있는지 확인 (쿼리가 비었으면 스킵)
            if (_selectionBoxQuery.IsEmptyIgnoreFilter)
            {
                _shouldDraw = false;
                return;
            }

            // 3. 데이터 가져오기 (여기서만 계산 수행)
            var selectionBox = _selectionBoxQuery.GetSingleton<SelectionBox>();
            _shouldDraw = selectionBox.isDragging;

            if (_shouldDraw)
            {
                // 화면 좌표 계산 (Y축 반전 처리)
                float2 start = new float2(selectionBox.startScreenPos.x, Screen.height - selectionBox.startScreenPos.y);
                float2 current = new float2(selectionBox.currentScreenPos.x, Screen.height - selectionBox.currentScreenPos.y);
                
                // 그릴 사각형 정보 업데이트
                _drawRect = GetScreenRect(start, current);
            }
        }

        private void OnGUI()
        {
            // 그릴 필요가 없으면 바로 종료 (함수 호출 비용 절약)
            if (!_shouldDraw) return;

            // --- 그리기 로직 (계산 없이 그리기만 수행) ---
            
            // 1. 내부 채우기 (반투명)
            GUI.color = new Color(0f, 1f, 0f, 0.15f);
            GUI.DrawTexture(_drawRect, _whiteTexture);

            // 2. 테두리 그리기
            GUI.color = Color.green;
            DrawBorder(_drawRect, 2);
            
            // 색상 복구
            GUI.color = Color.white;
        }

        // Client World를 찾아 캐싱하는 함수
        private bool TryInitClientWorld()
        {
            foreach (var world in World.All)
            {
                if (world == null || !world.IsCreated) continue;

                // ClientSystem 플래그가 있는 월드인지 확인하거나, 
                // 단순히 SelectionBox 쿼리가 유효한 월드를 찾음
                var query = world.EntityManager.CreateEntityQuery(typeof(SelectionBox));
                if (!query.IsEmptyIgnoreFilter) // 데이터가 존재하면 당첨!
                {
                    _clientEntityManager = world.EntityManager;
                    _selectionBoxQuery = query;
                    _isInitialized = true;
                    return true;
                }
            }
            return false;
        }

        private Rect GetScreenRect(float2 pos1, float2 pos2)
        {
            float x = math.min(pos1.x, pos2.x);
            float y = math.min(pos1.y, pos2.y);
            float width = math.abs(pos1.x - pos2.x);
            float height = math.abs(pos1.y - pos2.y);

            return new Rect(x, y, width, height);
        }

        private void DrawBorder(Rect rect, int thickness)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _whiteTexture); // 상
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _whiteTexture); // 하
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _whiteTexture); // 좌
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _whiteTexture); // 우
        }

        private void OnDestroy()
        {
            if (_whiteTexture != null) Destroy(_whiteTexture);
        }
    }
}