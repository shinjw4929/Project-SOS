using System;
using System.IO;
using Unity.Logging;
using Unity.Logging.Sinks;
using Unity.NetCode;
using UnityEngine;
using Shared;

[UnityEngine.Scripting.Preserve]
public class GameBootStrap : ClientServerBootstrap {
    public override bool Initialize(string defaultWorldName)
    {
        // 백그라운드에서 멈추지 않도록 설정
        Application.runInBackground = true;
        // 포트 설정
        AutoConnectPort = 7979;

        // vSync 끄기
        QualitySettings.vSyncCount = 0;

        // --- 로깅 초기화 ---
        string logDir = Path.Combine(Application.persistentDataPath, "Logs");
        Directory.CreateDirectory(logDir);
        string logPath = Path.Combine(logDir, $"game_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");

        var config = new LoggerConfig()
            .MinimumLevel.Set(LogLevel.Info)
            .WriteTo.File(logPath, minLevel: LogLevel.Debug, maxFileSizeBytes: 10 * 1024 * 1024, maxRoll: 5)
            .WriteTo.UnityDebugLog(minLevel: LogLevel.Warning);

        Log.Logger = new Unity.Logging.Logger(config);

        Application.quitting += () =>
        {
            Log.FlushAll();
            Log.Logger?.Dispose();
        };
        // --- 로깅 초기화 끝 ---

        return base.Initialize(defaultWorldName);
    }
}
