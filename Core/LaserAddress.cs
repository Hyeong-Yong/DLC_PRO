using System;

namespace DLC_PRO.Core;

public static class LaserAddress
{
    // Only parameter/command names are mapped. Values and raw console text are never rewritten.
    public static string Map(string name, int laserId)
    {
        if (laserId < 1 || laserId > 2) throw new ArgumentOutOfRangeException(nameof(laserId));
        return laserId != 1 && name.StartsWith("laser1:", StringComparison.Ordinal)
            ? "laser" + laserId + name.Substring(6) : name;
    }
}
