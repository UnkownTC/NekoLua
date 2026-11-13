namespace NekoLua.Dependencies;

public class LuaExtendMath
{
    public static double Clamp(double value, double min, double max)
    {
        if (min > max)
            (min, max) = (max, min);
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
