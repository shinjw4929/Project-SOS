using NUnit.Framework;
using Shared;
using Unity.Collections;
using Unity.Mathematics;

namespace Tests.EditMode.Utilities
{
    [TestFixture]
    public class FlowFieldCoreTests
    {
        const int GridSizeX = 10;
        const int GridSizeY = 10;
        const int CellCount = GridSizeX * GridSizeY;

        NativeArray<byte> _passMap;
        NativeArray<byte> _output;
        NativeArray<int2> _bfsQueue;
        NativeArray<byte> _visited;
        NativeArray<ushort> _costMap;

        [SetUp]
        public void SetUp()
        {
            _passMap = new NativeArray<byte>(CellCount, Allocator.Temp);
            _output = new NativeArray<byte>(CellCount, Allocator.Temp);
            _bfsQueue = new NativeArray<int2>(CellCount, Allocator.Temp);
            _visited = new NativeArray<byte>(CellCount, Allocator.Temp);
            _costMap = new NativeArray<ushort>(CellCount, Allocator.Temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_passMap.IsCreated) _passMap.Dispose();
            if (_output.IsCreated) _output.Dispose();
            if (_bfsQueue.IsCreated) _bfsQueue.Dispose();
            if (_visited.IsCreated) _visited.Dispose();
            if (_costMap.IsCreated) _costMap.Dispose();
        }

        void InitWorkerMemory()
        {
            for (int i = 0; i < CellCount; i++)
            {
                _output[i] = 255;
                _visited[i] = 0;
                _costMap[i] = ushort.MaxValue;
            }
        }

        #region ComputeField Tests

        [Test]
        public void ComputeField_EmptyGrid_AllCellsPointTowardDestination()
        {
            // 빈 그리드에서 목적지 (5,5)로 BFS
            InitWorkerMemory();
            var dest = new int2(5, 5);
            var gridSize = new int2(GridSizeX, GridSizeY);

            FlowFieldCore.ComputeField(_passMap, dest, gridSize, _output, _bfsQueue, _visited, _costMap);

            // 목적지 자체는 None
            Assert.AreEqual(FlowFieldCore.DirNone, _output[dest.y * GridSizeX + dest.x]);

            // 목적지 주변 셀은 유효한 방향 (255가 아님)
            Assert.AreNotEqual(FlowFieldCore.DirNone, _output[4 * GridSizeX + 5]); // S of dest
            Assert.AreNotEqual(FlowFieldCore.DirNone, _output[6 * GridSizeX + 5]); // N of dest

            // 모든 passable 셀에 방향이 할당됨 (빈 그리드이므로 전부 도달 가능)
            for (int i = 0; i < CellCount; i++)
            {
                byte dir = _output[i];
                Assert.IsTrue(dir <= 7 || dir == FlowFieldCore.DirNone,
                    $"Cell {i} has invalid direction {dir}");
            }
        }

        [Test]
        public void ComputeField_ObstacleBypass_CellsBehindWallHaveValidDirection()
        {
            // 장애물: y=5 행에 x=3~7 차단 (가운데 벽)
            for (int x = 3; x <= 7; x++)
                _passMap[5 * GridSizeX + x] = 1;

            InitWorkerMemory();
            var dest = new int2(5, 8); // 벽 위
            var gridSize = new int2(GridSizeX, GridSizeY);

            FlowFieldCore.ComputeField(_passMap, dest, gridSize, _output, _bfsQueue, _visited, _costMap);

            // 벽 아래 셀도 우회 경로로 도달 가능
            byte dirBelowWall = _output[2 * GridSizeX + 5]; // (5, 2) = 벽 아래
            Assert.AreNotEqual(FlowFieldCore.DirNone, dirBelowWall,
                "Cell below wall should be reachable via bypass");
        }

        [Test]
        public void ComputeField_DiagonalCornerBlocking_BlockedWhenAdjacentOrthogonalBlocked()
        {
            // (4,5)와 (5,4) 차단 → (4,4)에서 NE 대각 이동 불가
            _passMap[5 * GridSizeX + 4] = 1; // (4,5) blocked
            _passMap[4 * GridSizeX + 5] = 1; // (5,4) blocked

            InitWorkerMemory();
            var dest = new int2(5, 5);
            var gridSize = new int2(GridSizeX, GridSizeY);

            FlowFieldCore.ComputeField(_passMap, dest, gridSize, _output, _bfsQueue, _visited, _costMap);

            // (4,4) → 목적지(5,5) NE 대각은 차단, 다른 우회 경로만 가능
            byte dir = _output[4 * GridSizeX + 4];
            if (dir != FlowFieldCore.DirNone)
            {
                // NE(1)가 아닌 다른 방향이어야 함
                Assert.AreNotEqual(FlowFieldCore.DirNE, dir,
                    "Diagonal NE should be blocked when N and E are both blocked");
            }
        }

        [Test]
        public void ComputeField_UnreachableCell_DirectionIsNone()
        {
            // (5,5) 주변을 완전히 둘러싸서 도달 불가 영역 생성
            // 둘러싸기: (0,0)~(0,2) 사각형 차단
            for (int y = 0; y <= 2; y++)
                for (int x = 0; x <= 2; x++)
                    if (!(x == 1 && y == 1))
                        _passMap[y * GridSizeX + x] = 1;
            // (1,1)만 passable but 도달 불가

            InitWorkerMemory();
            var dest = new int2(5, 5);
            var gridSize = new int2(GridSizeX, GridSizeY);

            FlowFieldCore.ComputeField(_passMap, dest, gridSize, _output, _bfsQueue, _visited, _costMap);

            // (1,1)은 도달 불가
            Assert.AreEqual(FlowFieldCore.DirNone, _output[1 * GridSizeX + 1],
                "Completely surrounded cell should have direction None");
        }

        #endregion

        #region Direction Helpers

        [Test]
        public void GetReverseDirection_AllDirections_ReturnCorrectOpposite()
        {
            Assert.AreEqual(FlowFieldCore.DirS, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirN));
            Assert.AreEqual(FlowFieldCore.DirSW, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirNE));
            Assert.AreEqual(FlowFieldCore.DirW, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirE));
            Assert.AreEqual(FlowFieldCore.DirNW, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirSE));
            Assert.AreEqual(FlowFieldCore.DirN, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirS));
            Assert.AreEqual(FlowFieldCore.DirNE, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirSW));
            Assert.AreEqual(FlowFieldCore.DirE, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirW));
            Assert.AreEqual(FlowFieldCore.DirSE, FlowFieldCore.GetReverseDirection(FlowFieldCore.DirNW));
        }

        #endregion
    }
}
