using ClosedXML.Excel;
using ClosedXML.Graphics;

namespace DigitalOps.API.Features.Members;

internal static class MemberWorkbookGraphics
{
    private static readonly object SyncRoot = new();
    private static bool _configured;

    public static void Configure()
    {
        lock (SyncRoot)
        {
            if (_configured)
            {
                return;
            }

            foreach (var fontPath in GetFallbackFontPaths())
            {
                try
                {
                    using var fontStream = File.OpenRead(fontPath);
                    LoadOptions.DefaultGraphicEngine =
                        DefaultGraphicEngine.CreateOnlyWithFonts(fontStream);
                    _configured = true;
                    return;
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException)
                {
                    // Try the next platform-specific font location.
                }
            }

            _configured = true;
        }
    }

    private static IEnumerable<string> GetFallbackFontPaths()
    {
        var systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrWhiteSpace(systemFonts))
        {
            yield return Path.Combine(systemFonts, "arial.ttf");
            yield return Path.Combine(systemFonts, "calibri.ttf");
        }

        yield return @"C:\Windows\Fonts\arial.ttf";
        yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        yield return "/usr/share/fonts/dejavu/DejaVuSans.ttf";
        yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
    }
}
