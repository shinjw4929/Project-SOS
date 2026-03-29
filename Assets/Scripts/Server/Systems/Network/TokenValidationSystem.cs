using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// GoInGameRequestRpc의 AuthToken을 룸 서버(:8081)에서 검증한다.
    /// 유효하면 TokenValidatedTag + RoomSessionInfo를 부착하고,
    /// 무효하면 클라이언트를 Disconnect한다.
    /// </summary>
    [UpdateBefore(typeof(GoInGameServerSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    partial class TokenValidationSystem : SystemBase
    {
        RoomTokenValidator validator;

        protected override void OnCreate()
        {
            validator = new RoomTokenValidator("127.0.0.1", 8081);
            RequireForUpdate<GoInGameRequestRpc>();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (rpc, request, entity) in
                SystemAPI.Query<RefRO<GoInGameRequestRpc>, RefRO<ReceiveRpcCommandRequest>>()
                    .WithNone<TokenValidatedTag>()
                    .WithEntityAccess())
            {
                var sourceConnection = request.ValueRO.SourceConnection;
                string token = rpc.ValueRO.AuthToken.ToString();

                try
                {
                    var result = validator.Validate(token);

                    if (result.Valid)
                    {
                        ecb.AddComponent<TokenValidatedTag>(entity);

                        if (EntityManager.Exists(sourceConnection))
                        {
                            ecb.AddComponent(sourceConnection, new RoomSessionInfo
                            {
                                SessionId = new FixedString64Bytes(result.SessionId),
                                UserId = new FixedString64Bytes(result.UserId)
                            });
                        }

                        FixedString128Bytes logMessage = "Token validated, userId=";
                        logMessage.Append(result.UserId);
                        GameLogger.Info(LogWorld.Server, LogCategory.Network, in logMessage);
                    }
                    else
                    {
                        if (EntityManager.Exists(sourceConnection))
                            ecb.AddComponent<NetworkStreamRequestDisconnect>(sourceConnection);
                        ecb.DestroyEntity(entity);

                        FixedString128Bytes logMessage =
                            "Token validation failed, disconnecting client";
                        GameLogger.Warning(LogWorld.Server, LogCategory.Network, in logMessage);
                    }
                }
                catch (Exception exception)
                {
                    if (EntityManager.Exists(sourceConnection))
                        ecb.AddComponent<NetworkStreamRequestDisconnect>(sourceConnection);
                    ecb.DestroyEntity(entity);

                    FixedString128Bytes logMessage = "Token validation error: ";
                    var truncated = exception.Message.Length > 100
                        ? exception.Message.Substring(0, 100)
                        : exception.Message;
                    logMessage.Append(truncated);
                    GameLogger.Error(LogWorld.Server, LogCategory.Network, in logMessage);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        protected override void OnDestroy()
        {
            validator?.Dispose();
        }
    }
}
