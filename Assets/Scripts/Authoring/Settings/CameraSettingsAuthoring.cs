using Shared;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Authoring
{
    public class CameraSettingsAuthoring : MonoBehaviour
    {
        [Header("Hero Follow Settings")]
        public Vector3 offset = new Vector3(0f, 20f, -10f);
        public float smoothTime = 0.12f;
        public bool lockRotation = true;

        [Header("Edge Pan Settings")]
        public float edgePanSpeed = 20f;
        public float edgeThreshold = 20f;
        public Transform groundTransform;
        public Vector2 mapBoundsMin = new Vector2(-100f, -100f);
        public Vector2 mapBoundsMax = new Vector2(100f, 100f);

        public class Baker : Baker<CameraSettingsAuthoring>
        {
            public override void Bake(CameraSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                float2 boundsMin, boundsMax;
                if (authoring.groundTransform != null)
                {
                    Vector3 pos = authoring.groundTransform.position;
                    Vector3 scale = authoring.groundTransform.localScale;
                    float halfX = scale.x * 10f / 2f;
                    float halfZ = scale.z * 10f / 2f;
                    boundsMin = new float2(pos.x - halfX, pos.z - halfZ);
                    boundsMax = new float2(pos.x + halfX, pos.z + halfZ);
                }
                else
                {
                    boundsMin = new float2(authoring.mapBoundsMin.x, authoring.mapBoundsMin.y);
                    boundsMax = new float2(authoring.mapBoundsMax.x, authoring.mapBoundsMax.y);
                }

                AddComponent(entity, new CameraSettings
                {
                    // Hero Follow 설정
                    Offset = authoring.offset,
                    SmoothTime = authoring.smoothTime,
                    LockRotation = authoring.lockRotation,

                    // Edge Pan 설정
                    EdgePanSpeed = authoring.edgePanSpeed,
                    EdgeThreshold = authoring.edgeThreshold,
                    MapBoundsMin = boundsMin,
                    MapBoundsMax = boundsMax
                });
            }
        }
    }
}
