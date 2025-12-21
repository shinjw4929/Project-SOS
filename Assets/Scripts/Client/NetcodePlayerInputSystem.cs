using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine.InputSystem;


[UpdateInGroup(typeof(GhostInputSystemGroup))]
partial struct NetcodePlayerInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<NetcodePlayerInput>();
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (RefRW<NetcodePlayerInput> netcodePlayerInput
                 in SystemAPI.Query<RefRW<NetcodePlayerInput>>().WithAll<GhostOwnerIsLocal>())
        {
            float2 inputVector = new float2();
            if (Keyboard.current.wKey.isPressed) {
                inputVector.y = +1f;
            }
            if (Keyboard.current.sKey.isPressed) {
                inputVector.y = -1f;
            }
            if (Keyboard.current.aKey.isPressed) {
                inputVector.x = -1f;
            }
            if (Keyboard.current.dKey.isPressed) {
                inputVector.x = +1f;
            }
            netcodePlayerInput.ValueRW.inputVector = inputVector;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
