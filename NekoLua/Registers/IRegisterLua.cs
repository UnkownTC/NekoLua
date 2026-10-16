using MoonSharp.Interpreter;

using NekoLua.Loader;

namespace NekoLua.Registers;

public interface IRegisterLua
{
    string Name { get; }
    Table Create(LuaModInstance mod);
}
