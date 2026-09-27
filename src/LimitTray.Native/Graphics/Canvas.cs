using System.Numerics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;

namespace LimitTray.Native.Graphics;

/// <summary>
/// A thin drawing layer over a software Direct2D DC render target bound to a
/// <see cref="Surface"/>. Coordinates are DIPs; the target's DPI carries the scale, so
/// layout code never multiplies by it. Software rendering keeps the GPU driver out of
/// the process: on Intel Iris Xe that driver was what made WPF hold ~160 MB.
/// </summary>
internal sealed unsafe class Canvas : IDisposable, Ui.ITextMeasure
{
    private readonly GraphicsFactory _factory;
    private readonly TextEngine _text;
    private ID2D1DCRenderTarget* _target;
    private ID2D1SolidColorBrush* _brush;
    private ID2D1StrokeStyle* _round;
    private readonly Stack<(nint Layer, nint Geometry)> _layers = new();

    public Canvas(GraphicsFactory factory, TextEngine text, Surface surface, float scale)
    {
        _factory = factory;
        _text = text;
        Scale = scale;

        var props = new D2D1_RENDER_TARGET_PROPERTIES
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_SOFTWARE,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
            },
            dpiX = 96 * scale,
            dpiY = 96 * scale,
        };
        ID2D1DCRenderTarget* target;
        factory.D2D->CreateDCRenderTarget(&props, &target);
        _target = target;

        var bounds = new RECT { left = 0, top = 0, right = surface.Width, bottom = surface.Height };
        _target->BindDC(surface.Dc, &bounds);
        // ClearType does not survive per-pixel alpha; v0.3 was grayscale too.
        _target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        _target->SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);

        var white = ToD2D(Colour.White);
        ID2D1SolidColorBrush* brush;
        _target->CreateSolidColorBrush(&white, null, &brush);
        _brush = brush;

        var strokeProps = new D2D1_STROKE_STYLE_PROPERTIES
        {
            startCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
            endCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
            dashCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
            lineJoin = D2D1_LINE_JOIN.D2D1_LINE_JOIN_ROUND,
            miterLimit = 10,
        };
        ID2D1StrokeStyle* round;
        factory.D2D->CreateStrokeStyle(&strokeProps, null, 0, &round);
        _round = round;
    }

    public float Scale { get; }

    public void Begin()
    {
        _target->BeginDraw();
        ResetTransform();
    }

    /// <summary>Ends a drawing pass so the pixels can be read or blurred.</summary>
    public void End() => _target->EndDraw(null, null);

    public void Clear()
    {
        var clear = ToD2D(Colour.Transparent);
        _target->Clear(&clear);
    }

    private Matrix3x2 _transform = Matrix3x2.Identity;

    /// <summary>The current transform; pages set a translation, the refresh icon adds a rotation.</summary>
    public Matrix3x2 Transform
    {
        get => _transform;
        set
        {
            _transform = value;
            var m = value;
            _target->SetTransform((D2D_MATRIX_3X2_F*)&m);
        }
    }

    public void Translate(float dx, float dy) => Transform = Matrix3x2.CreateTranslation(dx, dy);

    public void ResetTransform() => Transform = Matrix3x2.Identity;

    public SizeF Measure(string text, TextStyle style, float maxWidth = float.MaxValue) =>
        _text.Measure(text, style, maxWidth);

    public float LineHeight(TextStyle style) => _text.LineHeight(style);

    // ---- shapes -------------------------------------------------------------

    public void FillRect(RectF r, Colour c)
    {
        UseColour(c);
        var rect = ToD2D(r);
        _target->FillRectangle(&rect, (ID2D1Brush*)_brush);
    }

    public void FillRoundedRect(RectF r, float radius, Colour c)
    {
        UseColour(c);
        var rr = Rounded(r, radius);
        _target->FillRoundedRectangle(&rr, (ID2D1Brush*)_brush);
    }

    /// <summary>A border drawn inside the rectangle, like WPF's Border.</summary>
    public void StrokeRoundedRect(RectF r, float radius, Colour c, float width)
    {
        UseColour(c);
        var half = width / 2;
        var rr = Rounded(r.Inflate(-half, -half), Math.Max(0, radius - half));
        _target->DrawRoundedRectangle(&rr, (ID2D1Brush*)_brush, width, null);
    }

    /// <summary>Left-to-right gradient in sRGB space, WPF's default interpolation.</summary>
    public void FillRoundedRectHorizontalGradient(RectF r, float radius, Colour from, Colour to)
    {
        if (r.W <= 0 || r.H <= 0) return;
        var stops = stackalloc D2D1_GRADIENT_STOP[2];
        stops[0] = new D2D1_GRADIENT_STOP { position = 0, color = ToD2D(from) };
        stops[1] = new D2D1_GRADIENT_STOP { position = 1, color = ToD2D(to) };
        ID2D1GradientStopCollection* collection;
        _target->CreateGradientStopCollection(stops, 2, D2D1_GAMMA.D2D1_GAMMA_2_2,
            D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP, &collection);
        var props = new D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES
        {
            startPoint = new D2D_POINT_2F { x = r.X, y = r.CentreY },
            endPoint = new D2D_POINT_2F { x = r.Right, y = r.CentreY },
        };
        ID2D1LinearGradientBrush* brush;
        _target->CreateLinearGradientBrush(&props, null, collection, &brush);
        var rr = Rounded(r, radius);
        _target->FillRoundedRectangle(&rr, (ID2D1Brush*)brush);
        brush->Release();
        collection->Release();
    }

    /// <summary>An ellipse filled with a radial gradient from the centre colour to the edge colour.</summary>
    public void FillRadialEllipse(RectF bounds, Colour centre, Colour edge)
    {
        var stops = stackalloc D2D1_GRADIENT_STOP[2];
        stops[0] = new D2D1_GRADIENT_STOP { position = 0, color = ToD2D(centre) };
        stops[1] = new D2D1_GRADIENT_STOP { position = 1, color = ToD2D(edge) };
        ID2D1GradientStopCollection* collection;
        _target->CreateGradientStopCollection(stops, 2, D2D1_GAMMA.D2D1_GAMMA_2_2,
            D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP, &collection);
        var props = new D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES
        {
            center = new D2D_POINT_2F { x = bounds.CentreX, y = bounds.CentreY },
            radiusX = bounds.W / 2,
            radiusY = bounds.H / 2,
        };
        ID2D1RadialGradientBrush* brush;
        _target->CreateRadialGradientBrush(&props, null, collection, &brush);
        var ellipse = new D2D1_ELLIPSE
        {
            point = new D2D_POINT_2F { x = bounds.CentreX, y = bounds.CentreY },
            radiusX = bounds.W / 2,
            radiusY = bounds.H / 2,
        };
        _target->FillEllipse(&ellipse, (ID2D1Brush*)brush);
        brush->Release();
        collection->Release();
    }

    public void FillEllipse(PointF centre, float radiusX, float radiusY, Colour c)
    {
        UseColour(c);
        var ellipse = new D2D1_ELLIPSE { point = ToD2D(centre), radiusX = radiusX, radiusY = radiusY };
        _target->FillEllipse(&ellipse, (ID2D1Brush*)_brush);
    }

    public void StrokeEllipse(PointF centre, float radiusX, float radiusY, Colour c, float width)
    {
        UseColour(c);
        var ellipse = new D2D1_ELLIPSE { point = ToD2D(centre), radiusX = radiusX, radiusY = radiusY };
        _target->DrawEllipse(&ellipse, (ID2D1Brush*)_brush, width, null);
    }

    /// <summary>
    /// A clockwise arc starting at <paramref name="startDegrees"/> (0 = east, -90 = north)
    /// with round caps, the shape of v0.3's ring gauge.
    /// </summary>
    public void Arc(PointF centre, float radius, float startDegrees, float sweepDegrees, Colour c, float width)
    {
        if (sweepDegrees <= 0) return;
        if (sweepDegrees >= 359.9f)
        {
            StrokeEllipse(centre, radius, radius, c, width);
            return;
        }

        var start = OnCircle(centre, radius, startDegrees);
        var end = OnCircle(centre, radius, startDegrees + sweepDegrees);
        ID2D1PathGeometry* path;
        _factory.D2D->CreatePathGeometry(&path);
        ID2D1GeometrySink* sink;
        path->Open(&sink);
        sink->BeginFigure(ToD2D(start), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_HOLLOW);
        var arc = new D2D1_ARC_SEGMENT
        {
            point = ToD2D(end),
            size = new D2D_SIZE_F { width = radius, height = radius },
            rotationAngle = 0,
            sweepDirection = D2D1_SWEEP_DIRECTION.D2D1_SWEEP_DIRECTION_CLOCKWISE,
            arcSize = sweepDegrees > 180 ? D2D1_ARC_SIZE.D2D1_ARC_SIZE_LARGE : D2D1_ARC_SIZE.D2D1_ARC_SIZE_SMALL,
        };
        sink->AddArc(&arc);
        sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
        sink->Close();
        sink->Release();

        UseColour(c);
        _target->DrawGeometry((ID2D1Geometry*)path, (ID2D1Brush*)_brush, width, _round);
        path->Release();
    }

    /// <summary>Connected line segments with round joins and caps.</summary>
    public void Polyline(ReadOnlySpan<PointF> points, Colour c, float width)
    {
        if (points.Length < 2) return;
        ID2D1PathGeometry* path;
        _factory.D2D->CreatePathGeometry(&path);
        ID2D1GeometrySink* sink;
        path->Open(&sink);
        sink->BeginFigure(ToD2D(points[0]), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_HOLLOW);
        for (var i = 1; i < points.Length; i++) sink->AddLine(ToD2D(points[i]));
        sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
        sink->Close();
        sink->Release();

        UseColour(c);
        _target->DrawGeometry((ID2D1Geometry*)path, (ID2D1Brush*)_brush, width, _round);
        path->Release();
    }

    // ---- text ---------------------------------------------------------------

    /// <summary>
    /// Draws text inside <paramref name="r"/>, aligned horizontally as asked and centred
    /// vertically when <paramref name="centreVertically"/> is set (WPF's VerticalAlignment=Center).
    /// </summary>
    public void Text(string text, TextStyle style, RectF r, Colour c, TextAlign align = TextAlign.Leading,
        bool centreVertically = false)
    {
        if (text.Length == 0 || c.A == 0) return;
        var layout = _text.CreateLayout(text, style, Math.Max(1, r.W), Math.Max(1, r.H));
        layout->SetTextAlignment(align switch
        {
            TextAlign.Centre => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
            TextAlign.Trailing => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
        });
        if (centreVertically)
            layout->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

        UseColour(c);
        _target->DrawTextLayout(new D2D_POINT_2F { x = r.X, y = r.Y }, layout, (ID2D1Brush*)_brush,
            D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        layout->Release();
    }

    // ---- layers -------------------------------------------------------------

    /// <summary>Clips to a rounded rectangle and applies an opacity until <see cref="PopLayer"/>.</summary>
    public void PushLayer(RectF? roundedClip, float radius, float opacity)
    {
        ID2D1RoundedRectangleGeometry* geometry = null;
        if (roundedClip is { } clip)
        {
            var rr = Rounded(clip, radius);
            _factory.D2D->CreateRoundedRectangleGeometry(&rr, &geometry);
        }

        ID2D1Layer* layer;
        _target->CreateLayer(null, &layer);
        var identity = Matrix3x2.Identity;
        var parameters = new D2D1_LAYER_PARAMETERS
        {
            contentBounds = new D2D_RECT_F { left = -float.MaxValue, top = -float.MaxValue, right = float.MaxValue, bottom = float.MaxValue },
            geometricMask = (ID2D1Geometry*)geometry,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = *(D2D_MATRIX_3X2_F*)&identity,
            opacity = opacity,
            opacityBrush = null,
            layerOptions = D2D1_LAYER_OPTIONS.D2D1_LAYER_OPTIONS_NONE,
        };
        _target->PushLayer(&parameters, layer);
        _layers.Push(((nint)layer, (nint)geometry));
    }

    public void PopLayer()
    {
        _target->PopLayer();
        var (layer, geometry) = _layers.Pop();
        ((ID2D1Layer*)layer)->Release();
        if (geometry != 0) ((ID2D1RoundedRectangleGeometry*)geometry)->Release();
    }

    public void PushClip(RectF r)
    {
        var rect = ToD2D(r);
        _target->PushAxisAlignedClip(&rect, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    }

    public void PopClip() => _target->PopAxisAlignedClip();

    // ---- plumbing -----------------------------------------------------------

    private void UseColour(Colour c)
    {
        var colour = ToD2D(c);
        _brush->SetColor(&colour);
    }

    private static PointF OnCircle(PointF centre, float radius, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        return new PointF(centre.X + radius * MathF.Cos(radians), centre.Y + radius * MathF.Sin(radians));
    }

    private static D2D1_COLOR_F ToD2D(Colour c) =>
        new() { r = c.R / 255f, g = c.G / 255f, b = c.B / 255f, a = c.A / 255f };

    private static D2D_RECT_F ToD2D(RectF r) =>
        new() { left = r.X, top = r.Y, right = r.Right, bottom = r.Bottom };

    private static D2D_POINT_2F ToD2D(PointF p) => new() { x = p.X, y = p.Y };

    private static D2D1_ROUNDED_RECT Rounded(RectF r, float radius) =>
        new() { rect = ToD2D(r), radiusX = radius, radiusY = radius };

    public void Dispose()
    {
        if (_round != null) { _round->Release(); _round = null; }
        if (_brush != null) { _brush->Release(); _brush = null; }
        if (_target != null) { _target->Release(); _target = null; }
    }
}
