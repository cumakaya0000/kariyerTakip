using System.Drawing;

namespace KariyerTakip.Common;

public static class AppBrand
{
    public static Icon CreateIcon()
    {
        using var stream = typeof(AppBrand).Assembly.GetManifestResourceStream("KariyerTakip.Assets.kt.ico")!;
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }
}
