namespace NekoLua.Api;

internal class LuaExtendMath
{
    public static double Clamp(double value, double min, double max)
    {
        if (min > max)
            (min, max) = (max, min);
        return value < min ? min : value > max ? max : value;
    }
}
