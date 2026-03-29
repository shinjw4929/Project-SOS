using System;
using System.IO;
using Unity.Logging;
using Unity.Logging.Sinks;
using Unity.NetCode;
using UnityEngine;
using Shared;

[UnityEngine.Scripting.Preserve]
public class GameBootStrap : ClientServerBootstrap {
    const int LogRetentionDays = 30;

    public override bool Initialize(string defaultWorldName)
    {
        // 백그라운드에서 멈추지 않도록 설정
        Application.runInBackground = true;
        // timeScale 강제 복원 (이전 GameStartPanelController가 0으로 설정한 값이 씬에 잔존할 수 있음)
        Time.timeScale = 1f;
        // 서버 리슨 + 클라이언트 자동 접속에 사용되는 포트
        // 클라이언트 자동 접속은 발생하지만, GoInGameClientSystem이 토큰 없으면 게임 진입을 차단
        AutoConnectPort = 7979;

        // vSync 끄기
        QualitySettings.vSyncCount = 0;

        // --- 로깅 초기화 ---
        string logRoot = Path.Combine(Application.persistentDataPath, "Logs");
        string todayDir = Path.Combine(logRoot, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(todayDir);

        string logPath = Path.Combine(todayDir, $"game_{DateTime.UtcNow:HHmmss}.log");

        var config = new LoggerConfig()
            .MinimumLevel.Set(LogLevel.Info)
            .WriteTo.File(logPath, minLevel: LogLevel.Debug, maxFileSizeBytes: 10 * 1024 * 1024, maxRoll: 5)
            .WriteTo.UnityDebugLog(minLevel: LogLevel.Warning);

        Log.Logger = new Unity.Logging.Logger(config);

        CleanOldLogs(logRoot);

        Application.quitting += () =>
        {
            Log.FlushAll();
            Log.Logger?.Dispose();
        };
        // --- 로깅 초기화 끝 ---

        return base.Initialize(defaultWorldName);
    }

    static void CleanOldLogs(string logRoot)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-LogRetentionDays);
            foreach (var dir in Directory.GetDirectories(logRoot))
            {
                string folderName = Path.GetFileName(dir);
                if (DateTime.TryParse(folderName, out DateTime folderDate) && folderDate < cutoff)
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to clean old logs: {e.Message}");
        }
    }
}
