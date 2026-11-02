using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace SpringCleaning;

internal sealed class VNavmeshIpc
{
    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> moveCloseTo;
    private readonly ICallGateSubscriber<bool> isMoving;
    private readonly ICallGateSubscriber<object> stop;

    public VNavmeshIpc(IDalamudPluginInterface pluginInterface)
    {
        isReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        moveCloseTo = pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>(
            "vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        isMoving = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        stop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool IsAvailable()
    {
        try
        {
            return isReady.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    public bool MoveTo(Vector3 position, float range)
    {
        try
        {
            return moveCloseTo.InvokeFunc(position, false, range);
        }
        catch
        {
            return false;
        }
    }

    public bool IsMoving()
    {
        try
        {
            return isMoving.InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            stop.InvokeAction();
        }
        catch
        {
            // vnavmesh is optional and may disappear while this plugin is loaded.
        }
    }
}
