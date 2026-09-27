using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.DirectWrite;

namespace LimitTray.Native.Graphics;

/// <summary>
/// The Direct2D and DirectWrite factories. Created when something is about to be drawn
/// (popup opens, tray icon repaints) and released straight after: nothing graphical is
/// meant to survive in memory while the popup is closed.
/// </summary>
internal sealed unsafe class GraphicsFactory : IDisposable
{
    private GraphicsFactory(ID2D1Factory* d2d, IDWriteFactory* dwrite, string fontFamily)
    {
        D2D = d2d;
        DWrite = dwrite;
        FontFamily = fontFamily;
    }

    public ID2D1Factory* D2D { get; private set; }
    public IDWriteFactory* DWrite { get; private set; }

    /// <summary>"Segoe UI Variable Text" when installed, otherwise "Segoe UI" (same order as v0.3).</summary>
    public string FontFamily { get; }

    /// <summary>Throws when either factory cannot be created; the caller shows the failure in words.</summary>
    public static GraphicsFactory Create()
    {
        var d2dIid = ID2D1Factory.IID_Guid;
        ID2D1Factory* d2d;
        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2dIid, null, (void**)&d2d)
            .ThrowOnFailure();

        var dwIid = IDWriteFactory.IID_Guid;
        IDWriteFactory* dwrite;
        var hr = PInvoke.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &dwIid, (void**)&dwrite);
        if (hr.Failed)
        {
            d2d->Release();
            hr.ThrowOnFailure();
        }

        return new GraphicsFactory(d2d, dwrite, PickFamily(dwrite));
    }

    private static string PickFamily(IDWriteFactory* dwrite)
    {
        IDWriteFontCollection* fonts;
        dwrite->GetSystemFontCollection(&fonts, false);
        try
        {
            uint index;
            BOOL exists;
            fixed (char* name = "Segoe UI Variable Text")
                fonts->FindFamilyName(name, &index, &exists);
            return exists ? "Segoe UI Variable Text" : "Segoe UI";
        }
        finally
        {
            fonts->Release();
        }
    }

    public void Dispose()
    {
        if (DWrite != null) { DWrite->Release(); DWrite = null; }
        if (D2D != null) { D2D->Release(); D2D = null; }
    }
}
