using Shared;
using Unity.Entities;
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
        public Vector2 mapBoundsMin = new Vector2(-100f, -100f);
        public Vector2 mapBoundsMax = new Vector2(100f, 100f);

        public class Baker : Baker<CameraSettingsAuthoring>
        {
            public override void Bake(CameraSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new CameraSettings
                {
                    // Hero Follow 설정
                    Offset = authoring.offset,
                    SmoothTime = authoring.smoothTime,
                    LockRotation = authoring.lockRotation,

                    // Edge Pan 설정
                    EdgePanSpeed = authoring.edgePanSpeed,
                    EdgeThreshold = authoring.edgeThreshold,
                    MapBoundsMin = authoring.mapBoundsMin,
                    MapBoundsMax = authoring.mapBoundsMax
                });
            }
        }
    }
}
