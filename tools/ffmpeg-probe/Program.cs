using System;
using System.Reflection;
using FFmpeg.AutoGen;

var ffmpegType = typeof(ffmpeg);
Console.WriteLine($"Assembly: {ffmpegType.Assembly.GetName()}");
Console.WriteLine($"RootPath default: {ffmpeg.RootPath}");
Console.WriteLine();

// Inspect the static fields used to initialize dynamically-loaded bindings.
foreach (var f in ffmpegType.Assembly.GetTypes())
{
    if (f.Name.Contains("Binding", StringComparison.Ordinal)
        || f.Name.Contains("Resolver", StringComparison.Ordinal)
        || f.Name.Contains("Loader", StringComparison.Ordinal)
        || f.Name.Contains("Libraries", StringComparison.Ordinal))
    {
        Console.WriteLine($"Type: {f.FullName}");
        foreach (var sf in f.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            Console.WriteLine($"  static {sf.FieldType.Name} {sf.Name} = {sf.GetValue(null)}");
        }
        foreach (var m in f.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (m.DeclaringType == f)
            {
                Console.WriteLine($"  method {m.Name}({string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))})");
            }
        }
    }
}

// Probe one binding to see what throws.
Console.WriteLine();
Console.WriteLine("Probing ffmpeg.LibraryVersionMap...");
try
{
    var mapField = ffmpegType.GetField("LibraryVersionMap", BindingFlags.Public | BindingFlags.Static);
    Console.WriteLine($"  mapField: {mapField}");
    if (mapField is not null)
    {
        var map = mapField.GetValue(null);
        Console.WriteLine($"  map: {map}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  EX: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("Probing avcodec_find_decoder directly...");
try
{
    var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
    Console.WriteLine($"  codec ptr: 0x{(long)codec:X}");
}
catch (Exception ex)
{
    Console.WriteLine($"  EX: {ex.GetType().FullName}: {ex.Message}");
    var inner = ex.InnerException;
    while (inner is not null)
    {
        Console.WriteLine($"    inner: {inner.GetType().FullName}: {inner.Message}");
        inner = inner.InnerException;
    }
}
