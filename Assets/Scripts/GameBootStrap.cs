using Unity.NetCode;
using UnityEngine;

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
        
        return base.Initialize(defaultWorldName);
    }
}
