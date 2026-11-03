namespace OfacMatch;

/// <summary>Argument checks shared by the public API. netstandard2.0 has no ThrowIfNull helpers.</summary>
internal static class Guard
{
    public static T NotNull<T>(T? value, string paramName)
        where T : class =>
        value ?? throw new ArgumentNullException(paramName);

    public static double InRange(double value, double min, double max, string paramName)
    {
        // NaN fails both comparisons, so test for the valid range rather than the invalid one.
        if (!(value >= min && value <= max))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Must be between {min} and {max}.");
        }

        return value;
    }

    public static int InRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Must be between {min} and {max}.");
        }

        return value;
    }
}
