using NUnit.Framework;
using Shared;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    /// <summary>
    /// GridUtility 유닛 테스트
    /// 그리드 좌표 변환 및 충돌 검사 로직 검증
    /// </summary>
    [TestFixture]
    public class GridUtilityTests
    {
        private GridSettings _defaultSettings;

        [SetUp]
        public void SetUp()
        {
            // 기본 그리드 설정: 셀 크기 1, 원점 (0,0), 그리드 크기 100x100
            _defaultSettings = new GridSettings
            {
                CellSize = 1f,
                GridOrigin = float2.zero,
                GridSize = new int2(100, 100)
            };
        }

        #region WorldToGrid Tests

        [Test]
        public void WorldToGrid_OriginPosition_ReturnsZero()
        {
            // Arrange
            float3 worldPos = float3.zero;

            // Act
            int2 result = GridUtility.WorldToGrid(worldPos, _defaultSettings);

            // Assert
            Assert.AreEqual(new int2(0, 0), result);
        }

        [Test]
        public void WorldToGrid_PositiveOffset_ReturnsCorrectGrid()
        {
            // Arrange
            float3 worldPos = new float3(5.5f, 0f, 3.2f);

            // Act
            int2 result = GridUtility.WorldToGrid(worldPos, _defaultSettings);

            // Assert
            // floor(5.5) = 5, floor(3.2) = 3
            Assert.AreEqual(new int2(5, 3), result);
        }

        [Test]
        public void WorldToGrid_NegativePosition_ReturnsNegativeGrid()
        {
            // Arrange
            float3 worldPos = new float3(-2.5f, 0f, -1.2f);

            // Act
            int2 result = GridUtility.WorldToGrid(worldPos, _defaultSettings);

            // Assert
            // floor(-2.5) = -3, floor(-1.2) = -2
            Assert.AreEqual(new int2(-3, -2), result);
        }

        [Test]
        public void WorldToGrid_WithOffset_AccountsForOrigin()
        {
            // Arrange
            var settings = new GridSettings
            {
                CellSize = 1f,
                GridOrigin = new float2(10f, 10f),
                GridSize = new int2(100, 100)
            };
            float3 worldPos = new float3(15f, 0f, 12f);

            // Act
            int2 result = GridUtility.WorldToGrid(worldPos, settings);

            // Assert
            // (15 - 10) / 1 = 5, (12 - 10) / 1 = 2
            Assert.AreEqual(new int2(5, 2), result);
        }

        [Test]
        public void WorldToGrid_LargeCellSize_ScalesCorrectly()
        {
            // Arrange
            var settings = new GridSettings
            {
                CellSize = 2f,
                GridOrigin = float2.zero,
                GridSize = new int2(100, 100)
            };
            float3 worldPos = new float3(5f, 0f, 7f);

            // Act
            int2 result = GridUtility.WorldToGrid(worldPos, settings);

            // Assert
            // floor(5/2) = 2, floor(7/2) = 3
            Assert.AreEqual(new int2(2, 3), result);
        }

        #endregion

        #region WorldToGridForBuilding Tests

        [Test]
        public void WorldToGridForBuilding_1x1Building_ConvertsCorrectly()
        {
            // Arrange
            float3 centerPos = new float3(5.5f, 0f, 5.5f);
            int width = 1;
            int length = 1;

            // Act
            int2 result = GridUtility.WorldToGridForBuilding(centerPos, width, length, _defaultSettings);

            // Assert
            // 중심 5.5 - (1*1*0.5) = 5.0 → round(5.0) = 5
            Assert.AreEqual(new int2(5, 5), result);
        }

        [Test]
        public void WorldToGridForBuilding_2x2Building_ConvertsCorrectly()
        {
            // Arrange
            float3 centerPos = new float3(6f, 0f, 6f);  // 2x2 건물의 중심
            int width = 2;
            int length = 2;

            // Act
            int2 result = GridUtility.WorldToGridForBuilding(centerPos, width, length, _defaultSettings);

            // Assert
            // 중심 6 - (2*1*0.5) = 5 → 좌하단 (5, 5)
            Assert.AreEqual(new int2(5, 5), result);
        }

        [Test]
        public void WorldToGridForBuilding_3x3Building_ConvertsCorrectly()
        {
            // Arrange
            float3 centerPos = new float3(5f, 0f, 5f);  // 3x3 건물의 중심
            int width = 3;
            int length = 3;

            // Act
            int2 result = GridUtility.WorldToGridForBuilding(centerPos, width, length, _defaultSettings);

            // Assert
            // 중심 5 - (3*1*0.5) = 3.5 → round(3.5) = 4 (반올림)
            Assert.AreEqual(new int2(4, 4), result);
        }

        #endregion

        #region GridToWorld Tests

        [Test]
        public void GridToWorld_1x1Building_ReturnsCenter()
        {
            // Arrange
            int gridX = 5;
            int gridY = 5;
            int width = 1;
            int length = 1;

            // Act
            float3 result = GridUtility.GridToWorld(gridX, gridY, width, length, _defaultSettings);

            // Assert
            // (5 * 1) + (1 * 1 * 0.5) = 5.5
            Assert.AreEqual(5.5f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
            Assert.AreEqual(5.5f, result.z, 0.001f);
        }

        [Test]
        public void GridToWorld_2x2Building_ReturnsCenter()
        {
            // Arrange
            int gridX = 5;
            int gridY = 5;
            int width = 2;
            int length = 2;

            // Act
            float3 result = GridUtility.GridToWorld(gridX, gridY, width, length, _defaultSettings);

            // Assert
            // (5 * 1) + (2 * 1 * 0.5) = 6
            Assert.AreEqual(6f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
            Assert.AreEqual(6f, result.z, 0.001f);
        }

        [Test]
        public void GridToWorld_WithOrigin_AccountsForOffset()
        {
            // Arrange
            var settings = new GridSettings
            {
                CellSize = 1f,
                GridOrigin = new float2(10f, 20f),
                GridSize = new int2(100, 100)
            };
            int gridX = 5;
            int gridY = 5;
            int width = 1;
            int length = 1;

            // Act
            float3 result = GridUtility.GridToWorld(gridX, gridY, width, length, settings);

            // Assert
            // 10 + (5 * 1) + 0.5 = 15.5
            // 20 + (5 * 1) + 0.5 = 25.5
            Assert.AreEqual(15.5f, result.x, 0.001f);
            Assert.AreEqual(25.5f, result.z, 0.001f);
        }

        [Test]
        public void GridToWorld_RoundTrip_PreservesPosition()
        {
            // Arrange
            float3 originalWorld = new float3(10.5f, 0f, 15.5f);

            // Act
            int2 gridPos = GridUtility.WorldToGrid(originalWorld, _defaultSettings);
            float3 backToWorld = GridUtility.GridToWorld(gridPos.x, gridPos.y, 1, 1, _defaultSettings);

            // Assert
            // 1x1 건물의 경우 중심이 .5로 스냅됨
            Assert.AreEqual(originalWorld.x, backToWorld.x, 0.001f);
            Assert.AreEqual(originalWorld.z, backToWorld.z, 0.001f);
        }

        #endregion

        #region IsOverlapping Tests

        [Test]
        public void IsOverlapping_CompletelyOverlapping_ReturnsTrue()
        {
            // Arrange
            int2 pos1 = new int2(5, 5);
            int2 size1 = new int2(3, 3);
            int2 pos2 = new int2(5, 5);  // 동일한 위치
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void IsOverlapping_PartialOverlap_ReturnsTrue()
        {
            // Arrange
            int2 pos1 = new int2(5, 5);
            int2 size1 = new int2(3, 3);
            int2 pos2 = new int2(7, 7);  // 일부 겹침
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void IsOverlapping_EdgeTouching_ReturnsTrue()
        {
            // Arrange - 한 픽셀 겹침
            int2 pos1 = new int2(5, 5);
            int2 size1 = new int2(3, 3);  // 5~7 범위
            int2 pos2 = new int2(7, 5);   // 7에서 시작
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            // pos1.x (5) < pos2.x + size2.x (10) AND pos1.x + size1.x (8) > pos2.x (7)
            // 5 < 10 (true) AND 8 > 7 (true) → true
            Assert.IsTrue(result);
        }

        [Test]
        public void IsOverlapping_NotOverlapping_ReturnsFalse()
        {
            // Arrange
            int2 pos1 = new int2(0, 0);
            int2 size1 = new int2(3, 3);  // 0~2 범위
            int2 pos2 = new int2(10, 10); // 완전히 떨어짐
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void IsOverlapping_AdjacentNotTouching_ReturnsFalse()
        {
            // Arrange
            int2 pos1 = new int2(5, 5);
            int2 size1 = new int2(3, 3);  // 5~7 범위 (7 미포함, 즉 5,6,7)
            int2 pos2 = new int2(8, 5);   // 8에서 시작 (인접)
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            // pos1.x + size1.x (8) > pos2.x (8) → 8 > 8 = false
            Assert.IsFalse(result);
        }

        [Test]
        public void IsOverlapping_ContainedInside_ReturnsTrue()
        {
            // Arrange - 작은 건물이 큰 건물 안에 완전히 포함
            int2 pos1 = new int2(0, 0);
            int2 size1 = new int2(10, 10);
            int2 pos2 = new int2(3, 3);
            int2 size2 = new int2(2, 2);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void IsOverlapping_OnlyXOverlap_ReturnsFalse()
        {
            // Arrange - X축만 겹치고 Y축은 안 겹침
            int2 pos1 = new int2(5, 0);
            int2 size1 = new int2(3, 3);
            int2 pos2 = new int2(6, 10);  // X는 겹치지만 Y는 멀리
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void IsOverlapping_OnlyYOverlap_ReturnsFalse()
        {
            // Arrange - Y축만 겹치고 X축은 안 겹침
            int2 pos1 = new int2(0, 5);
            int2 size1 = new int2(3, 3);
            int2 pos2 = new int2(10, 6);  // Y는 겹치지만 X는 멀리
            int2 size2 = new int2(3, 3);

            // Act
            bool result = GridUtility.IsOverlapping(pos1, size1, pos2, size2);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region Constants Tests

        [Test]
        public void ResourceNodeExclusionDistance_HasExpectedValue()
        {
            // Assert
            Assert.AreEqual(9, GridUtility.ResourceNodeExclusionDistance);
        }

        #endregion
    }
}
