using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FolderGlimpse.Preview;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiSmallIcon = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        internal nint Icon;
        internal int IconIndex;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    internal static BitmapSource? GetSmallIcon(string path)
    {
        if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiSmallIcon) == 0 || info.Icon == 0) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { DestroyIcon(info.Icon); }
    }
}
