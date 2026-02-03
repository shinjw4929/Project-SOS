#pragma warning disable CS0618 // NavMeshQuery, PolygonId - deprecated without replacement in Unity 6
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Experimental.AI;

namespace Server
{
    public static class NavMeshPathUtils
    {
        /// <summary>
        /// Funnel 알고리즘(String-Pulling)으로 폴리곤 경로를 직선 웨이포인트로 변환한다.
        /// NavMeshQuery.GetPortalPoints를 사용하여 포탈 에지를 획득하고,
        /// XZ 평면 Cross Product로 funnel을 좁혀 최적 경로를 생성한다.
        /// </summary>
        /// <returns>생성된 웨이포인트 수. 0이면 실패.</returns>
        public static int FindStraightPath(
            ref NavMeshQuery query,
            NativeArray<PolygonId> polygonPath,
            int pathLength,
            float3 startPos,
            float3 endPos,
            NativeArray<float3> outWaypoints,
            int maxWaypoints)
        {
            if (pathLength <= 0 || maxWaypoints < 2)
                return 0;

            // 단일 폴리곤: start -> end 직선
            if (pathLength == 1)
            {
                outWaypoints[0] = startPos;
                outWaypoints[1] = endPos;
                return 2;
            }

            // Funnel 알고리즘
            float3 apex = startPos;
            float3 portalLeft = startPos;
            float3 portalRight = startPos;
            int apexIndex = 0;
            int leftIndex = 0;
            int rightIndex = 0;

            int waypointCount = 0;
            outWaypoints[waypointCount++] = startPos;

            for (int i = 1; i <= pathLength && waypointCount < maxWaypoints - 1; i++)
            {
                float3 left, right;

                if (i < pathLength)
                {
                    // 폴리곤 간 포탈 에지 획득
                    bool success = query.GetPortalPoints(
                        polygonPath[i - 1], polygonPath[i],
                        out var l, out var r);

                    if (!success)
                        return 0; // 포탈 획득 실패 -> 전체 경로 실패

                    left = l;
                    right = r;
                }
                else
                {
                    // 마지막 포탈은 endPos
                    left = endPos;
                    right = endPos;
                }

                // Right 벽 업데이트
                if (Cross2D(apex, portalRight, right) <= 0f)
                {
                    if (ApproxEqual(apex, portalRight) || Cross2D(apex, portalLeft, right) > 0f)
                    {
                        portalRight = right;
                        rightIndex = i;
                    }
                    else
                    {
                        // Right가 Left를 넘었음 -> Left가 새 apex
                        if (waypointCount < maxWaypoints - 1)
                        {
                            apex = portalLeft;
                            outWaypoints[waypointCount++] = apex;
                            apexIndex = leftIndex;

                            portalLeft = apex;
                            portalRight = apex;
                            leftIndex = apexIndex;
                            rightIndex = apexIndex;

                            i = apexIndex;
                            continue;
                        }
                    }
                }

                // Left 벽 업데이트
                if (Cross2D(apex, portalLeft, left) >= 0f)
                {
                    if (ApproxEqual(apex, portalLeft) || Cross2D(apex, portalRight, left) < 0f)
                    {
                        portalLeft = left;
                        leftIndex = i;
                    }
                    else
                    {
                        // Left가 Right를 넘었음 -> Right가 새 apex
                        if (waypointCount < maxWaypoints - 1)
                        {
                            apex = portalRight;
                            outWaypoints[waypointCount++] = apex;
                            apexIndex = rightIndex;

                            portalLeft = apex;
                            portalRight = apex;
                            leftIndex = apexIndex;
                            rightIndex = apexIndex;

                            i = apexIndex;
                            continue;
                        }
                    }
                }
            }

            // 마지막 웨이포인트: endPos
            if (waypointCount < maxWaypoints)
            {
                outWaypoints[waypointCount++] = endPos;
            }

            return waypointCount;
        }

        static float Cross2D(float3 origin, float3 a, float3 b)
        {
            return (b.x - origin.x) * (a.z - origin.z) - (a.x - origin.x) * (b.z - origin.z);
        }

        static bool ApproxEqual(float3 a, float3 b)
        {
            return math.lengthsq(a - b) < 1e-6f;
        }
    }
}
