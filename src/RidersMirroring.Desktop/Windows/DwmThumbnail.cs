using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Desktop.Windows;

/// <summary>
/// Renders a foreign top-level window (e.g. <c>scrcpy.exe</c> or UxPlay's
/// GStreamer output window) inside a WPF <see cref="System.Windows.Controls.Image"/>
/// via the Windows Desktop Window Manager (DWM) thumbnail API.
///
/// DWM thumbnails are GPU-composited: zero CPU cycles, no
/// BitBlt/PrintWindow hacks, and the embedded window stays interactive
/// (mouse clicks / keyboard) because the events are forwarded by the OS.
/// </summary>
/// <remarks>
/// Requires Windows Vista or later. The native call returns
/// <c>DWM_E_COMPOSITIONDISABLED</c> if DWM is off (rare); we fall back to
/// hiding the host <see cref="System.Windows.Controls.Image"/> so the UI
/// stays usable.
/// </remarks>
public sealed class DwmThumbnail : IDisposable
{
    private IntPtr _thumbnailId = IntPtr.Zero;
    private IntPtr _hostHwnd = IntPtr.Zero;
    private IntPtr _sourceHwnd = IntPtr.Zero;
    private bool _disposed;

    public bool IsAttached => _thumbnailId != IntPtr.Zero;

    public DwmThumbnail(UIElement hostControl)
    {
        // hostControl is the WPF Image (or Border) we want the foreign
        // window rendered inside. We need its HWND, which only exists
        // after the control is added to the visual tree.
        _hostHwnd = new WindowInteropHelper(Application.Current.MainWindow!).Handle;
    }

    /// <summary>
    /// Attach to <paramref name="sourceHwnd"/> and render it inside
    /// the destination rectangle <paramref name="destinationRect"/>
    /// expressed in host-window client coordinates.
    /// </summary>
    public bool Attach(IntPtr sourceHwnd, RECT destinationRect)
    {
        _sourceHwnd = sourceHwnd;

        if (!IsCompositionEnabled())
        {
            RidersLogger.Create<DwmThumbnail>().LogWarning("DWM composition is disabled; thumbnail cannot attach.");
            return false;
        }

        // If the host HWND changed (e.g. window was recreated), rebuild.
        Detach();

        var hr = DwmRegisterThumbnail(_hostHwnd, sourceHwnd, out _thumbnailId);
        if (hr != 0 || _thumbnailId == IntPtr.Zero)
        {
            RidersLogger.Create<DwmThumbnail>().LogWarning("DwmRegisterThumbnail failed: 0x{hr:X}", hr);
            return false;
        }

        // Force the source to be visible so DWM has something to render.
        ShowWindow(sourceHwnd, ShowWindowCommand.SW_SHOWNOACTIVATE);

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            fVisible = true,
            fSourceClientAreaOnly = true,
            opacity = 255,
            rcDestination = destinationRect,
        };
        hr = DwmUpdateThumbnailProperties(_thumbnailId, props);
        if (hr != 0)
        {
            RidersLogger.Create<DwmThumbnail>().LogWarning("DwmUpdateThumbnailProperties failed: 0x{hr:X}", hr);
            Detach();
            return false;
        }
        return true;
    }

    /// <summary>Move/resize the embedded window without re-registering.</summary>
    public bool UpdateDestination(RECT destinationRect)
    {
        if (_thumbnailId == IntPtr.Zero) return false;
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION,
            rcDestination = destinationRect,
        };
        return DwmUpdateThumbnailProperties(_thumbnailId, props) == 0;
    }

    public void Detach()
    {
        if (_thumbnailId != IntPtr.Zero)
        {
            DwmUnregisterThumbnail(_thumbnailId);
            _thumbnailId = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }

    // ---- native interop ----

    private const int DWM_TNP_VISIBLE = 0x1;
    private const int DWM_TNP_OPACITY = 0x4;
    private const int DWM_TNP_RECTDESTINATION = 0x2;
    private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x8;

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, DWM_THUMBNAIL_PROPERTIES ptnProperties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

    private static bool IsCompositionEnabled()
    {
        try
        {
            return DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public bool fVisible;
        public bool fSourceClientAreaOnly;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private enum ShowWindowCommand
    {
        SW_SHOWNOACTIVATE = 4,
    }
}

/// <summary>Locates the scrcpy / UxPlay window by enumerating top-level
/// windows and matching process names. Used so we can hand its HWND to
/// <see cref="DwmThumbnail"/>.</summary>
public static class ExternalWindowFinder
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern System.IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern System.IntPtr GetModuleFileName(IntPtr hModule, System.Text.StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Find the HWND of a process whose image-name matches <paramref name="fileName"/>.</summary>
    public static IntPtr? FindByProcessFileName(string fileName)
    {
        IntPtr? result = null;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) return true;
            var hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return true;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                if (GetModuleFileName(IntPtr.Zero, sb, sb.Capacity) != 0
                    && sb.ToString().EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    result = hWnd;
                    return false; // stop
                }
            }
            finally
            {
                CloseHandle(hProc);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Find a window whose title starts with <paramref name="titlePrefix"/>.</summary>
    public static IntPtr? FindByTitlePrefix(string titlePrefix)
    {
        IntPtr? result = null;
        EnumWindows((hWnd, _) =>
        {
            var len = GetWindowTextLength(hWnd);
            if (len == 0) return true;
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase))
            {
                result = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}