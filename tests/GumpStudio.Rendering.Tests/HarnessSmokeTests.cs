using SkiaSharp;

using Xunit;

namespace GumpStudio.Rendering.Tests;

public class HarnessSmokeTests
{
    /// <summary>
    /// SkiaSharp needs its native library present and loadable. Proving that here
    /// means a Phase 3 golden-image failure is a rendering bug, not a broken runtime.
    /// </summary>
    [Fact]
    public void SkiaNativeLibraryLoadsAndRenders()
    {
        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);

        canvas.Clear(SKColors.Magenta);
        canvas.Flush();

        Assert.Equal(SKColors.Magenta, bitmap.GetPixel(2, 2));
    }
}
