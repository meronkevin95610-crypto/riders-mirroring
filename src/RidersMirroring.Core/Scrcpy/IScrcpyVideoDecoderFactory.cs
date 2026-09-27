namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Constructs a fresh <see cref="IScrcpyVideoDecoder"/> for each new
/// <see cref="ScrcpySession"/>.
///
/// <para>
/// A factory is the cleanest seam between the session lifecycle and the
/// decoder implementation: the Core layer cannot reference
/// <c>ScrcpyVideoDecoder</c> directly (the FFmpeg types leak through),
/// and unit tests can substitute a factory that returns a fake.
/// </para>
/// </summary>
public interface IScrcpyVideoDecoderFactory
{
    /// <summary>
    /// Build a new decoder. The returned instance is owned by the
    /// caller and must be disposed when the session ends.
    /// </summary>
    IScrcpyVideoDecoder Create();
}
