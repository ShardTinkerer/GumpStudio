using GumpStudio.Core.Primitives;

namespace GumpStudio.Core.Editing;

/// <summary>
/// The design grid: spacing, whether it is drawn, and whether edits snap to it.
/// </summary>
/// <remarks>
/// A built-in feature rather than a plugin. In 1.8 this was <c>SnapToGrid.dll</c>,
/// which had to reach into the designer form through mouse and key hooks and
/// persist its own <c>BinaryFormatter</c> config file beside the executable. It
/// is core editing behaviour, so it lives in the editor.
/// </remarks>
public sealed class GridSettings
{
    private int _width = 5;
    private int _height = 5;

    /// <summary>Horizontal spacing in gump pixels. Always at least 1.</summary>
    public int Width
    {
        get => _width;
        set => _width = Math.Max(1, value);
    }

    /// <summary>Vertical spacing in gump pixels. Always at least 1.</summary>
    public int Height
    {
        get => _height;
        set => _height = Math.Max(1, value);
    }

    /// <summary>Whether the grid is drawn on the canvas.</summary>
    public bool Visible { get; set; }

    /// <summary>Whether moving and resizing snap to the grid.</summary>
    /// <remarks>
    /// Separate from <see cref="Visible"/> on purpose: seeing the grid and being
    /// constrained by it are independent choices, and the original conflated
    /// neither.
    /// </remarks>
    public bool SnapEnabled { get; set; }

    /// <summary>Rounds a horizontal coordinate to the nearest grid line.</summary>
    public int SnapX(int x) => Round(x, _width);

    /// <summary>Rounds a vertical coordinate to the nearest grid line.</summary>
    public int SnapY(int y) => Round(y, _height);

    /// <summary>Rounds a point to the nearest grid intersection.</summary>
    public GumpPoint Snap(GumpPoint point) => new(SnapX(point.X), SnapY(point.Y));

    /// <summary>
    /// Rounds a rectangle's edges outward-independently to the grid.
    /// </summary>
    /// <remarks>
    /// Each edge snaps on its own so a resize lands flush with the grid on the
    /// side being dragged, rather than snapping the origin and leaving the far
    /// edge off-grid.
    /// </remarks>
    public GumpRect Snap(GumpRect rect)
    {
        int left = SnapX(rect.Left);
        int top = SnapY(rect.Top);
        int right = SnapX(rect.Right);
        int bottom = SnapY(rect.Bottom);

        // Never collapse an element to nothing by snapping both edges together.
        return new GumpRect(
            left,
            top,
            Math.Max(_width, right - left),
            Math.Max(_height, bottom - top));
    }

    private static int Round(int value, int step)
    {
        // Integer division truncates toward zero, which would bias negative
        // coordinates the wrong way, so round on the floor instead.
        int floor = (int)Math.Floor(value / (double)step) * step;

        return value - floor >= (step + 1) / 2 ? floor + step : floor;
    }
}
