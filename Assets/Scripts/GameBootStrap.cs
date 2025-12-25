using Unity.NetCode;
using UnityEngine;

[UnityEngine.Scripting.Preserve]
public class GameBootStrap : ClientServerBootstrap {
    public override bool Initialize(string defaultWorldName)
    {
        Application.runInBackground = true;
        AutoConnectPort = 7979;
        return base.Initialize(defaultWorldName);
    }
}
