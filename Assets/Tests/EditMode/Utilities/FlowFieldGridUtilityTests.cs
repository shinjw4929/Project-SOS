using NUnit.Framework;
using Shared;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    /// <summary>
    /// Flow Field 관련 GridUtility 메서드 테스트
    /// CellCenterToWorld, IsPassable, IsPassableForSize, BuildPassabilityMap, MarkPathBlocked/UnmarkPathBlocked
    /// </summary>
    [TestFixture]
    public class FlowFieldGridUtilityTests
    {
        GridSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new GridSettings
            {
                CellSize = 1.0f,
                GridOrigin = float2.zero,
                GridSize = new int2(200, 200)
            };
        }

        /// <summary>
        /// DynamicBuffer는 Length 설정 시 메모리를 0으로 초기화하지 않으므로 수동 초기화 필요
        /// </summary>
        static void ZeroBuffer(DynamicBuffer<GridCell> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = default;
        }

        #region CellCenterToWorld

        [Test]
        public void CellCenterToWorld_Cell00_ReturnsOriginPlusHalfCell()
        {
            float3 result = GridUtility.CellCenterToWorld(new int2(0, 0), _settings);

            // GridOrigin(0,0) + (0 + 0.5) * 1.0 = 0.5
            Assert.AreEqual(0.5f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
            Assert.AreEqual(0.5f, result.z, 0.001f);
        }

        [Test]
        public void CellCenterToWorld_RoundTrip_PreservesCell()
        {
            var cell = new int2(50, 75);
            float3 worldPos = GridUtility.CellCenterToWorld(cell, _settings);
            int2 backToCell = GridUtility.WorldToGrid(worldPos, _settings);

            Assert.AreEqual(cell.x, backToCell.x);
            Assert.AreEqual(cell.y, backToCell.y);
        }

        #endregion

        #region IsPassable

        [Test]
        public void IsPassable_PassableCell_ReturnsTrue()
        {
            var map = new NativeArray<byte>(100, Allocator.Temp);
            Assert.IsTrue(GridUtility.IsPassable(map, 5, 5, 10, 10));
            map.Dispose();
        }

        [Test]
        public void IsPassable_BlockedCell_ReturnsFalse()
        {
            var map = new NativeArray<byte>(100, Allocator.Temp);
            map[5 * 10 + 5] = 1;
            Assert.IsFalse(GridUtility.IsPassable(map, 5, 5, 10, 10));
            map.Dispose();
        }

        [Test]
        public void IsPassable_OutOfBounds_ReturnsFalse()
        {
            var map = new NativeArray<byte>(100, Allocator.Temp);
            Assert.IsFalse(GridUtility.IsPassable(map, -1, 0, 10, 10));
            Assert.IsFalse(GridUtility.IsPassable(map, 0, -1, 10, 10));
            Assert.IsFalse(GridUtility.IsPassable(map, 10, 0, 10, 10));
            Assert.IsFalse(GridUtility.IsPassable(map, 0, 10, 10, 10));
            map.Dispose();
        }

        #endregion

        #region IsPassableForSize

        [Test]
        public void IsPassableForSize_Padding0_ChecksSelfOnly()
        {
            var map = new NativeArray<byte>(100, Allocator.Temp);
            // (5,5) passable, 주변에 장애물 있어도 padding=0이면 통과
            map[4 * 10 + 5] = 1; // (5,4) blocked

            Assert.IsTrue(GridUtility.IsPassableForSize(map, 5, 5, 10, 10, 0));
            map.Dispose();
        }

        [Test]
        public void IsPassableForSize_Padding1_ChecksNeighbors()
        {
            var map = new NativeArray<byte>(100, Allocator.Temp);
            map[4 * 10 + 5] = 1; // (5,4) blocked — (5,5)의 남쪽 이웃

            // padding=1이면 주변 1칸 포함 → (5,4) blocked → 불통과
            Assert.IsFalse(GridUtility.IsPassableForSize(map, 5, 5, 10, 10, 1));
            map.Dispose();
        }

        [Test]
        public void IsPassableForSize_EdgeCell_Padding1_ReturnsFalse()
        {
            var map = new NativeArray<byte>(100, Allocator.Temp);
            // (0,0)은 padding=1일 때 (-1,-1)~(1,1) 범위 → 경계 밖 = blocked
            Assert.IsFalse(GridUtility.IsPassableForSize(map, 0, 0, 10, 10, 1));
            map.Dispose();
        }

        #endregion

        #region BuildPassabilityMap

        [Test]
        public void BuildPassabilityMap_AllClear_AllZero()
        {
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 25; // 5x5
            ZeroBuffer(buffer);

            var output = new NativeArray<byte>(25, Allocator.Temp);
            GridUtility.BuildPassabilityMap(buffer, new int2(5, 5), 0, output);

            for (int i = 0; i < 25; i++)
                Assert.AreEqual(0, output[i], $"Cell {i} should be passable");

            output.Dispose();
            world.Dispose();
        }

        [Test]
        public void BuildPassabilityMap_BlockedCell_OnlyThatCellBlocked()
        {
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 25;
            ZeroBuffer(buffer);

            var cell = buffer[12]; // (2,2) in 5x5
            cell.IsPathBlocked = 1;
            buffer[12] = cell;

            var output = new NativeArray<byte>(25, Allocator.Temp);
            GridUtility.BuildPassabilityMap(buffer, new int2(5, 5), 0, output);

            Assert.AreEqual(1, output[12]);
            Assert.AreEqual(0, output[11]); // 인접 셀은 passable
            Assert.AreEqual(0, output[13]);

            output.Dispose();
            world.Dispose();
        }

        [Test]
        public void BuildPassabilityMap_Padding1_ExpandsBlockedArea()
        {
            // 10x10 그리드 사용 (5x5는 padding=1 시 가장자리 전부 blocked)
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 100; // 10x10
            ZeroBuffer(buffer);

            // (5,5) blocked
            var cell = buffer[5 * 10 + 5];
            cell.IsPathBlocked = 1;
            buffer[5 * 10 + 5] = cell;

            var output = new NativeArray<byte>(100, Allocator.Temp);
            GridUtility.BuildPassabilityMap(buffer, new int2(10, 10), 1, output);

            // (5,5) blocked → padding=1이면 (4,4)~(6,6) 전부 blocked
            Assert.AreEqual(1, output[5 * 10 + 5]); // (5,5)
            Assert.AreEqual(1, output[4 * 10 + 4]); // (4,4)
            Assert.AreEqual(1, output[4 * 10 + 5]); // (5,4)
            Assert.AreEqual(1, output[4 * 10 + 6]); // (6,4)
            Assert.AreEqual(1, output[5 * 10 + 4]); // (4,5)
            Assert.AreEqual(1, output[5 * 10 + 6]); // (6,5)
            Assert.AreEqual(1, output[6 * 10 + 4]); // (4,6)
            Assert.AreEqual(1, output[6 * 10 + 5]); // (5,6)
            Assert.AreEqual(1, output[6 * 10 + 6]); // (6,6)

            // 확장 범위 밖 내부 셀은 passable
            Assert.AreEqual(0, output[3 * 10 + 3]); // (3,3) — 장애물에서 2칸 떨어짐
            Assert.AreEqual(0, output[7 * 10 + 7]); // (7,7)
            Assert.AreEqual(0, output[5 * 10 + 3]); // (3,5)

            output.Dispose();
            world.Dispose();
        }

        [Test]
        public void BuildPassabilityMap_IsOccupiedButNotBlocked_Passable()
        {
            // 핵심: IsOccupied=1이지만 IsPathBlocked=0이면 passability 맵에서 통과 가능
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 25;
            ZeroBuffer(buffer);

            var cell = buffer[12];
            cell.IsOccupied = 1;      // 배치 점유
            cell.IsPathBlocked = 0;   // 경로탐색 비차단
            buffer[12] = cell;

            var output = new NativeArray<byte>(25, Allocator.Temp);
            GridUtility.BuildPassabilityMap(buffer, new int2(5, 5), 0, output);

            Assert.AreEqual(0, output[12], "IsOccupied but not IsPathBlocked should be passable");

            output.Dispose();
            world.Dispose();
        }

        #endregion

        #region MarkPathBlocked / UnmarkPathBlocked

        [Test]
        public void MarkPathBlocked_Wall4x4_Path2x2_OnlyCenterBlocked()
        {
            // Wall: 배치 4x4, 경로탐색 2x2 중앙
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 100; // 10x10
            ZeroBuffer(buffer);

            GridUtility.MarkPathBlocked(buffer, 0, 0, 4, 4, 2, 2, 10);

            // offset = (4-2)/2 = 1 → 경로탐색 영역: (1,1)~(2,2)
            Assert.AreEqual(1, buffer[1 * 10 + 1].IsPathBlocked); // (1,1)
            Assert.AreEqual(1, buffer[1 * 10 + 2].IsPathBlocked); // (2,1)
            Assert.AreEqual(1, buffer[2 * 10 + 1].IsPathBlocked); // (1,2)
            Assert.AreEqual(1, buffer[2 * 10 + 2].IsPathBlocked); // (2,2)

            // 배치 영역이지만 경로탐색 영역 밖은 비차단
            Assert.AreEqual(0, buffer[0 * 10 + 0].IsPathBlocked); // (0,0)
            Assert.AreEqual(0, buffer[0 * 10 + 1].IsPathBlocked); // (1,0)
            Assert.AreEqual(0, buffer[3 * 10 + 3].IsPathBlocked); // (3,3)

            world.Dispose();
        }

        [Test]
        public void MarkPathBlocked_Barracks6x6_Path6x6_AllBlocked()
        {
            // Barracks: 배치 = 경로탐색 (PathWidth = Width)
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 100;
            ZeroBuffer(buffer);

            GridUtility.MarkPathBlocked(buffer, 0, 0, 6, 6, 6, 6, 10);

            // offset=0, 전체 6x6 blocked
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    Assert.AreEqual(1, buffer[y * 10 + x].IsPathBlocked, $"({x},{y}) should be blocked");

            // 범위 밖은 비차단
            Assert.AreEqual(0, buffer[0 * 10 + 6].IsPathBlocked); // (6,0)

            world.Dispose();
        }

        [Test]
        public void UnmarkPathBlocked_RestoresPassable()
        {
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 100;
            ZeroBuffer(buffer);

            GridUtility.MarkPathBlocked(buffer, 0, 0, 4, 4, 2, 2, 10);
            Assert.AreEqual(1, buffer[1 * 10 + 1].IsPathBlocked);

            GridUtility.UnmarkPathBlocked(buffer, 0, 0, 4, 4, 2, 2, 10);
            Assert.AreEqual(0, buffer[1 * 10 + 1].IsPathBlocked);
            Assert.AreEqual(0, buffer[1 * 10 + 2].IsPathBlocked);
            Assert.AreEqual(0, buffer[2 * 10 + 1].IsPathBlocked);
            Assert.AreEqual(0, buffer[2 * 10 + 2].IsPathBlocked);

            world.Dispose();
        }

        [Test]
        public void MarkPathBlocked_Idempotent_DoubleMarkNoChange()
        {
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 100;
            ZeroBuffer(buffer);

            GridUtility.MarkPathBlocked(buffer, 0, 0, 4, 4, 2, 2, 10);
            GridUtility.MarkPathBlocked(buffer, 0, 0, 4, 4, 2, 2, 10); // 2번째 마킹

            Assert.AreEqual(1, buffer[1 * 10 + 1].IsPathBlocked); // 여전히 1

            world.Dispose();
        }

        [Test]
        public void MarkPathBlocked_AdjacentBuildings_CorrectAreas()
        {
            var world = new World("Test");
            var em = world.EntityManager;
            var entity = em.CreateEntity(typeof(GridCell));
            var buffer = em.AddBuffer<GridCell>(entity);
            buffer.Length = 100;
            ZeroBuffer(buffer);

            // 건물 A: 배치 (0,0), 경로탐색 (1,1)-(2,2)
            GridUtility.MarkPathBlocked(buffer, 0, 0, 4, 4, 2, 2, 10);
            // 건물 B: 배치 (5,0), 경로탐색 (6,1)-(7,2)
            GridUtility.MarkPathBlocked(buffer, 5, 0, 4, 4, 2, 2, 10);

            // 건물 A 영역
            Assert.AreEqual(1, buffer[1 * 10 + 1].IsPathBlocked);
            Assert.AreEqual(1, buffer[2 * 10 + 2].IsPathBlocked);

            // 건물 B 영역
            Assert.AreEqual(1, buffer[1 * 10 + 6].IsPathBlocked);
            Assert.AreEqual(1, buffer[2 * 10 + 7].IsPathBlocked);

            // 사이 갭은 비차단
            Assert.AreEqual(0, buffer[1 * 10 + 3].IsPathBlocked); // (3,1)
            Assert.AreEqual(0, buffer[1 * 10 + 4].IsPathBlocked); // (4,1)
            Assert.AreEqual(0, buffer[1 * 10 + 5].IsPathBlocked); // (5,1)

            world.Dispose();
        }

        #endregion
    }
}
