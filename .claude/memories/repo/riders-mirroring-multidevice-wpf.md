# Riders Mirroring — Multi-device + WPF (oct. 2026)

## Architecture

```
ScrcpySessionViewModel (1 per device)
        ↓ owns
ScrcpySession (unsealed, StartAsync virtual)
        ↓ owns
ScrcpyServer (raw_stream mode)
ScrcpyClient
ScrcpyVideoStream
IScrcpyVideoDecoder (from factory)
─────────────────────────────
ScrcpySessionManager
  ConcurrentDictionary<string, ScrcpySession>
  Func<string, ScrcpySession>? _sessionFactory (test seam)
─────────────────────────────
MainViewModel
  ObservableCollection<ScrcpySessionViewModel> ActiveSessions
  ScrcpySessionManager? _sessionManager
```

## Test seam pattern (ScrcpySession)

Pour tester le manager sans toucher à ADB / jar :
- `ScrcpySession` est **`class`** (pas `sealed`)
- `StartAsync` est **`virtual`**
- Tests utilisent `Func<string, ScrcpySession> _sessionFactory` qui retourne un `FakeScrcpySession : ScrcpySession` qui override `StartAsync` à `Task.CompletedTask`
- Le manager : si `StartAsync` lance, on remove la session du dict (rollback)

## IScrcpyVideoDecoder API (FFmpeg 8.1.0)

```csharp
public interface IScrcpyVideoDecoder : IAsyncDisposable
{
    bool IsReady { get; }
    ScrcpyStreamHeader? Header { get; }
    event EventHandler<DecodedVideoFrame>? FrameDecoded;
    void Prime(ScrcpyStreamHeader header);                        // PAS ReadOnlySpan<byte>
    Task PushPacketAsync(ReadOnlyMemory<byte>, CancellationToken); // PAS bool TryDecode
    Task FlushAsync(CancellationToken);
}
```

`DecodedVideoFrame = record(int Width, int Height, int Stride, ReadOnlyMemory<byte> BgraPixels, TimeSpan PresentationTime)`.

## ScrcpySessionViewModel

- Constructeur : `(ScrcpySession session)` — 1 seul arg, **PAS** `(session, displayName)`
- Expose : `DeviceSerial` (lecture), `CurrentFrame`, `CurrentFps`, `StatusLabel` (ObservableProperty)
- `StartAsync()` délègue à `_session.StartAsync()`
- `StopAsync()` se désabonne + `await _session.DisposeAsync()`
- `Dispose()` synchrone (pour `using var`)

## ScrcpyVideoDecoderFactory

- Configure `FFmpeg.AutoGen.ffmpeg.RootPath` dans le ctor
- `DefaultFfmpegRoot()` cherche `AppContext.BaseDirectory/ffmpeg` puis `vendor/ffmpeg/bin` puis `cwd/vendor/ffmpeg/bin`
- **WATCH** : `Directory` (sans préfixe) lève CS0103 dans un fichier qui n'a pas `using System.IO;`. Utiliser `IO.Directory` après `using IO = System.IO;` ou importer System.IO en haut

## WriteableBitmap.WritePixels

- Signature WPF : `WritePixels(Int32Rect, Array, int stride, int offset)` — **PAS** ReadOnlyMemory<byte>
- Workaround : `frame.BgraPixels.ToArray()` (alloue, mais acceptable pour ~30fps @ 1080p = ~250 MB/s qu'on ne peut de toute façon pas inonder via dispatcher)
- Toujours entourer de `bitmap.Lock()` / `bitmap.Unlock()`

## Résultats finaux

- **156 Core tests + 96 Desktop tests = 252/252 verts** (Debug + Release)
- Build 0 erreur
- Frame réelle décodée (BMP 8.3 MB) depuis ASUS_X00TD Android 9
