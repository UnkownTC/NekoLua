using NekoLua.Api.Components;
using UnityEngine;

namespace NekoLua.Api;

internal static class LuaGameObject
{
    public static GameObject GameObject { get; private set; } = null;
    internal static void Create()
    {
        GameObject = new GameObject("NekoLua-Parent");
        UnityEngine.Object.DontDestroyOnLoad(GameObject);
        _ = GameObject.AddComponent<LuaComponent>();
    }
    internal static void Destroy()
    {
        if (GameObject != null)
            UnityEngine.Object.Destroy(GameObject);
        GameObject = null;
    }
}
