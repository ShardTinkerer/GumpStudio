using Microsoft.Win32.SafeHandles;

namespace GumpStudio.Uo.Files;

/// <summary>
/// A read-only file handle with offset-based reads.
/// </summary>
/// <remarks>
/// Uses <see cref="RandomAccess"/> rather than a seekable <see cref="FileStream"/>
/// so reads carry their own offset and are safe to issue concurrently. The old
/// SDK shared one seeking stream across all callers, which meant two decoders
/// running at once would read each other's bytes.
/// </remarks>
public sealed class SafeFileHandleOwner : IDisposable
{
    private readonly SafeFileHandle _handle;

    private SafeFileHandleOwner(SafeFileHandle handle, long length, string path)
    {
        _handle = handle;
        Length = length;
        Path = path;
    }

    /// <summary>Length of the file in bytes, captured at open time.</summary>
    public long Length { get; }

    /// <summary>Full path, kept for diagnostics.</summary>
    public string Path { get; }

    public static SafeFileHandleOwner OpenRead(string path)
    {
        SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.RandomAccess);

        try
        {
            return new SafeFileHandleOwner(handle, RandomAccess.GetLength(handle), path);
        }
        catch
        {
            handle.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Reads into <paramref name="destination"/>, clamping to the end of the file.
    /// </summary>
    /// <returns>Bytes actually read, which is less than requested at end of file.</returns>
    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset >= Length || destination.IsEmpty)
        {
            return 0;
        }

        // Clamping here is what stops a corrupt index entry claiming a length
        // that runs past the end of the file.
        long available = Length - offset;

        if (destination.Length > available)
        {
            destination = destination[..(int)available];
        }

        int total = 0;

        while (total < destination.Length)
        {
            int read = RandomAccess.Read(_handle, destination[total..], offset + total);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public void Dispose() => _handle.Dispose();
}
