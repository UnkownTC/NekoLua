using MoonSharp.Interpreter;
using System.Runtime.CompilerServices;

namespace NekoLua.Loader;

/// <summary>
/// 表示 Lua 模组的运行时实例，包含名称、模组目录、入口文件、关联脚本以及可禁用标志。
/// </summary>
/// <remarks>Name、ModDir、EntryFile 和 Script 为 init-only 属性，通常在对象初始化时设置后只读。ModDir 与 EntryFile 可为相对或绝对路径；EntryFile
/// 为空或 null 表示未指定。Disabled 指示该实例是否被禁用。相等性比较使用引用相等（ReferenceEquals），GetHashCode 使用
/// RuntimeHelpers.GetHashCode，以与引用相等保持一致。</remarks>
public class LuaModInstance
{
    /// <summary>
    /// 表示对象的名称。
    /// </summary>
    /// <remarks>仅可在对象初始化时设置（init-only），之后为只读。</remarks>
    public string Name { get; init; }
    /// <summary>
    /// 模组所在目录的路径。
    /// </summary>
    /// <remarks>为 init-only 属性，通常在对象初始化时设置；路径可为相对或绝对。</remarks>
    public string ModDir { get; init; }
    /// <summary>
    /// 应用或包的入口文件路径。
    /// </summary>
    /// <remarks>初始化时设置后不可更改。路径可为相对或绝对；为空或 null 表示未指定。</remarks>
    public string EntryFile { get; init; }
    /// <summary>
    /// 与实体关联的脚本对象。
    /// </summary>
    /// <remarks>在对象初始化期间设置；之后为只读（init-only）。</remarks>
    public Script Script { get; init; }
    /// <summary>
    /// 获取或设置一个值，指示该功能或控件是否处于禁用状态。
    /// </summary>
    /// <remarks>禁用时应使其不可交互并在处理或呈现时被忽略。默认值为 false。</remarks>
    public bool Disabled { get; set; }
    /// <summary>
    /// 确定两个 LuaModInstance? 引用是否引用相同的对象。
    /// </summary>
    /// <remarks>使用引用相等（ReferenceEquals）比较；不会调用类型的 Equals 重载。</remarks>
    /// <param name="a">第一个可为 null 的 LuaModInstance 引用。</param>
    /// <param name="b">第二个可为 null 的 LuaModInstance 引用。</param>
    /// <returns>如果两个引用引用同一对象或均为 null，则为 true；否则为 false。</returns>
    public static bool operator ==(LuaModInstance? a, LuaModInstance? b)
    {
        return ReferenceEquals(a, b);
    }
    /// <summary>
    /// 指示两个 LuaModInstance 引用是否不相等。
    /// </summary>
    /// <param name="a">要比较的第一个 LuaModInstance 引用。</param>
    /// <param name="b">要比较的第二个 LuaModInstance 引用。</param>
    /// <returns>如果 a 与 b 不引用同一对象，则为 true；否则为 false。</returns>
    public static bool operator !=(LuaModInstance? a, LuaModInstance? b)
    {
        return !ReferenceEquals(a, b);
    }
    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj);
    }
    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }
    /// <inheritdoc/>
    public override string ToString()
    {
        return base.ToString();
    }
}
