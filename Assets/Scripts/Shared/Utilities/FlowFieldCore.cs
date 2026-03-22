using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// BFS 기반 Flow Field 코어 알고리즘
    /// IJob 내부에서 호출되므로 Job 레벨 Burst에 의해 자동 컴파일됨
    /// </summary>
    [BurstCompile]
    public struct FlowFieldCore
    {
        // 방향 인코딩 (byte)
        public const byte DirN  = 0;
        public const byte DirNE = 1;
        public const byte DirE  = 2;
        public const byte DirSE = 3;
        public const byte DirS  = 4;
        public const byte DirSW = 5;
        public const byte DirW  = 6;
        public const byte DirNW = 7;
        public const byte DirNone = 255;

        /// <summary>
        /// 방향 → 오프셋 변환 (Burst 호환)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 GetDirectionOffset(byte dir)
        {
            switch (dir)
            {
                case DirN:  return new int2(0, 1);
                case DirNE: return new int2(1, 1);
                case DirE:  return new int2(1, 0);
                case DirSE: return new int2(1, -1);
                case DirS:  return new int2(0, -1);
                case DirSW: return new int2(-1, -1);
                case DirW:  return new int2(-1, 0);
                case DirNW: return new int2(-1, 1);
                default:    return new int2(0, 0);
            }
        }

        /// <summary>
        /// 역방향 변환: BFS 확산 시 역방향을 셀에 기록
        /// N↔S, NE↔SW, E↔W, SE↔NW
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetReverseDirection(byte dir)
        {
            // (dir + 4) % 8 — 8방향에서 정확히 반대편
            return (byte)((dir + 4) & 7);
        }

        /// <summary>
        /// BFS 기반 Flow Field 계산
        /// 호출자 책임: outputField MemSet(255), visited MemSet(0), costMap MemSet(ushort.MaxValue)
        /// bfsQueue는 gridCellCount 이상 크기로 할당
        /// 목적지는 범위 내 유효 좌표
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeField(
            NativeArray<byte> passabilityMap,
            int2 destination,
            int2 gridSize,
            NativeArray<byte> outputField,
            NativeArray<int2> bfsQueue,
            NativeArray<byte> visited,
            NativeArray<ushort> costMap)
        {
            int gridSizeX = gridSize.x;
            int gridSizeY = gridSize.y;
            int destIndex = destination.y * gridSizeX + destination.x;

            // 목적지가 blocked이면 전체 필드가 255(None)으로 남음
            if (passabilityMap[destIndex] != 0)
                return;

            // 목적지 초기화
            outputField[destIndex] = DirNone; // 목적지 자체는 None
            visited[destIndex] = 1;
            costMap[destIndex] = 0;

            // Flat array FIFO queue (head/tail 방식)
            int head = 0;
            int tail = 0;
            bfsQueue[tail++] = destination;

            while (head < tail)
            {
                int2 current = bfsQueue[head++];
                int currentIndex = current.y * gridSizeX + current.x;
                ushort currentCost = costMap[currentIndex];

                // 8방향 탐색: 직교(0,2,4,6) 먼저, 대각(1,3,5,7) 나중에
                // 직교 우선 순서로 대각 방향 편향 방지
                for (int pass = 0; pass < 8; pass++)
                {
                    // pass 0-3: 직교 (N=0, E=2, S=4, W=6), pass 4-7: 대각 (NE=1, SE=3, SW=5, NW=7)
                    byte dir = pass < 4 ? (byte)(pass * 2) : (byte)(pass * 2 - 7);

                    int2 dirOffset = GetDirectionOffset(dir);
                    int nx = current.x + dirOffset.x;
                    int ny = current.y + dirOffset.y;

                    // 경계 체크
                    if (nx < 0 || ny < 0 || nx >= gridSizeX || ny >= gridSizeY)
                        continue;

                    int neighborIndex = ny * gridSizeX + nx;

                    // 이미 방문
                    if (visited[neighborIndex] != 0)
                        continue;

                    // passability 체크
                    if (passabilityMap[neighborIndex] != 0)
                        continue;

                    // 대각 이동 코너 차단: 인접 직교 셀이 모두 passable이어야 함
                    if ((dir & 1) != 0) // 홀수 dir = 대각 (NE, SE, SW, NW)
                    {
                        if (!IsDiagonalPassable(passabilityMap, current, dir, gridSizeX, gridSizeY))
                            continue;
                    }

                    visited[neighborIndex] = 1;
                    costMap[neighborIndex] = (ushort)(currentCost + 1);
                    outputField[neighborIndex] = GetReverseDirection(dir);
                    bfsQueue[tail++] = new int2(nx, ny);
                }
            }
        }

        /// <summary>
        /// 대각 이동 시 인접 직교 셀 passability 확인
        /// NE → N, E 모두 passable / SE → S, E / SW → S, W / NW → N, W
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDiagonalPassable(NativeArray<byte> passabilityMap, int2 current, byte dir, int gridSizeX, int gridSizeY)
        {
            int2 ortho1, ortho2;

            switch (dir)
            {
                case DirNE:
                    ortho1 = new int2(current.x, current.y + 1);     // N
                    ortho2 = new int2(current.x + 1, current.y);     // E
                    break;
                case DirSE:
                    ortho1 = new int2(current.x, current.y - 1);     // S
                    ortho2 = new int2(current.x + 1, current.y);     // E
                    break;
                case DirSW:
                    ortho1 = new int2(current.x, current.y - 1);     // S
                    ortho2 = new int2(current.x - 1, current.y);     // W
                    break;
                case DirNW:
                    ortho1 = new int2(current.x, current.y + 1);     // N
                    ortho2 = new int2(current.x - 1, current.y);     // W
                    break;
                default:
                    return true;
            }

            // 경계 체크 + passability 체크
            if (ortho1.x < 0 || ortho1.y < 0 || ortho1.x >= gridSizeX || ortho1.y >= gridSizeY)
                return false;
            if (ortho2.x < 0 || ortho2.y < 0 || ortho2.x >= gridSizeX || ortho2.y >= gridSizeY)
                return false;

            return passabilityMap[ortho1.y * gridSizeX + ortho1.x] == 0
                && passabilityMap[ortho2.y * gridSizeX + ortho2.x] == 0;
        }
    }
}
