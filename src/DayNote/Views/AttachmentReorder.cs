using System;

namespace DayNote.Views;

/// <summary>
/// The pure index math behind dragging an attachment row to reorder it, lifted out of the
/// pointer handler so the rounding and clamping are testable without a live list.
/// </summary>
public static class AttachmentReorder
{
    /// <summary>
    /// The slot a dragged row should land in: its start index shifted by however many whole
    /// rows the cursor has travelled (<paramref name="delta"/> pixels over a
    /// <paramref name="rowStep"/>-pixel row, rounding half away from zero), clamped into
    /// <c>[0, count - 1]</c>. A non-positive <paramref name="rowStep"/> (unmeasurable) or
    /// empty list means no movement.
    /// </summary>
    public static int TargetIndex(int startIndex, double delta, double rowStep, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var moved = rowStep > 0 ? (int)Math.Round(delta / rowStep, MidpointRounding.AwayFromZero) : 0;
        return Math.Clamp(startIndex + moved, 0, count - 1);
    }
}
