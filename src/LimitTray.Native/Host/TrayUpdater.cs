using LimitTray.Core.Presentation;

namespace LimitTray.Native.Host;

internal sealed class TrayUpdater
{
    private TrayIconModel? _model;
    private int _sizePx;
    private bool _dark;
    private bool _hasValue;

    public bool ShouldRedraw(TrayIconModel next, int iconSizePx, bool dark)
    {
        if (_hasValue && Equals(_model, next) && _sizePx == iconSizePx && _dark == dark)
            return false;

        _model = next;
        _sizePx = iconSizePx;
        _dark = dark;
        _hasValue = true;
        return true;
    }
}
