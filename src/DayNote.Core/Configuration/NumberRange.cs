using System.Globalization;

namespace DayNote.Core.Configuration;

/// <summary>
/// The numbers a setting accepts: from <see cref="Min"/> to <see cref="Max"/>, and only whole ones when
/// <see cref="Whole"/>. The field that edits the setting reads typed text by it and its owner checks
/// stored values by it, so both accept the same numbers (number-field-conventions).
/// </summary>
public sealed record NumberRange(double Min, double Max, bool Whole)
{
    private const NumberStyles Typed =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public bool Accepts(double value) =>
        double.IsFinite(value) && value >= Min && value <= Max && (!Whole || value == Math.Floor(value));

    /// <summary>
    /// The number typed, or null when it is not one this range accepts. The interface language's decimal
    /// separator is read, and a point too, since no group separators are taken and a point is then never
    /// ambiguous.
    /// </summary>
    public double? Read(string text, CultureInfo culture)
    {
        if (!decimal.TryParse(text, Typed, culture, out var number)
            && !decimal.TryParse(text, Typed, CultureInfo.InvariantCulture, out number))
            return null;

        var value = (double)number;
        return Accepts(value) ? value : null;
    }

    /// <summary>The value <paramref name="by"/> away from <paramref name="from"/>, kept within the range.</summary>
    public double Step(double from, double by) =>
        Math.Clamp((double)((decimal)from + (decimal)by), Min, Max);

    /// <summary>A value as the field shows it and as its message names the bounds.</summary>
    public static string Format(double value, CultureInfo culture) => value.ToString("G", culture);
}
