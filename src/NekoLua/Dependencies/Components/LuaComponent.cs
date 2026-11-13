using UnityEngine;

namespace NekoLua.Dependencies.Components;

internal class LuaComponent : MonoBehaviour
{
    public void Update()
    {
        LuaTimer.Tick(Time.deltaTime);
        LuaScheduler.Tick();
        LuaAsyncScheduler.Tick(Time.deltaTime);
        LuaMainThread.Tick();
    }
}
