using System.Globalization;

namespace GumpStudio.Core.Primitives;

/// <summary>An integer point in gump coordinates.</summary>
/// <remarks>
/// A local type rather than <c>System.Drawing.Point</c>, which is banned across
/// the rewrite: it drags in a Windows-only dependency and it is the reason the
/// old code could not be tested headlessly.
/// </remarks>
public readonly record struct GumpPoint(int X, int Y)
{
    public static GumpPoint Origin { get; }

    public GumpPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    public static GumpPoint operator +(GumpPoint a, GumpPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static GumpPoint operator -(GumpPoint a, GumpPoint b) => new(a.X - b.X, a.Y - b.Y);

    public static GumpPoint Add(GumpPoint left, GumpPoint right) => left + right;

    public static GumpPoint Subtract(GumpPoint left, GumpPoint right) => left - right;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{X}, {Y}");
}

/// <summary>An integer size in gump coordinates.</summary>
public readonly record struct GumpSize(int Width, int Height)
{
    public static GumpSize Empty { get; }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Width} x {Height}");
}

/// <summary>An axis-aligned integer rectangle.</summary>
public readonly record struct GumpRect(int X, int Y, int Width, int Height)
{
    public GumpRect(GumpPoint location, GumpSize size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    public static GumpRect Empty { get; }

    public int Left => X;

    public int Top => Y;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public GumpPoint Location => new(X, Y);

    public GumpSize Size => new(Width, Height);

    /// <summary>The centre point, rounded down.</summary>
    /// <remarks>
    /// The original's alignment sorters computed <c>(Y + Height) / 2</c>, which is
    /// not a centre at all. Alignment commands use this instead.
    /// </remarks>
    public GumpPoint Center => new(X + (Width / 2), Y + (Height / 2));

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(GumpPoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public bool IntersectsWith(GumpRect other) =>
        other.X < Right && X < other.Right && other.Y < Bottom && Y < other.Bottom;

    /// <summary>Grows the rectangle by <paramref name="dx"/> and <paramref name="dy"/> on every side.</summary>
    public GumpRect Inflate(int dx, int dy) =>
        new(X - dx, Y - dy, Width + (dx * 2), Height + (dy * 2));

    /// <summary>The smallest rectangle containing both inputs.</summary>
    public static GumpRect Union(GumpRect a, GumpRect b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);

        return new GumpRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    /// <summary>Builds a rectangle from two opposite corners, in any order.</summary>
    /// <remarks>Used for marquee selection, where the drag can go in any direction.</remarks>
    public static GumpRect FromCorners(GumpPoint a, GumpPoint b)
    {
        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);

        return new GumpRect(left, top, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}) {Width} x {Height}");
}
