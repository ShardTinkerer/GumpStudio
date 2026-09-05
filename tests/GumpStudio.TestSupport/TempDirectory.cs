namespace GumpStudio.TestSupport;

/// <summary>A scratch directory that deletes itself on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gumpstudio-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public override string ToString() => Path;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked scratch directory is not worth failing a test run over;
            // the OS reclaims it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
