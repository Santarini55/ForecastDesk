using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace ForecastDesk;

public sealed class RoundedPanel : Panel
{
    [DefaultValue(8)]
    public int Radius { get; set; } = 8;

    [DefaultValue(true)]
    public bool DrawBorder { get; set; } = true;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = ThemeManager.Palette.Card;
        ForeColor = ThemeManager.Palette.Text;
        Padding = new Padding(10);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        using var path = ThemeManager.RoundedRect(rect, Radius);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        if (DrawBorder)
        {
            using var border = new Pen(ThemeManager.Palette.Border);
            e.Graphics.DrawPath(border, path);
        }
    }
}

public sealed class ModernButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public bool Primary { get; set; }
    public bool Danger { get; set; }
    public bool Success { get; set; }
    public ModernButtonIcon Icon { get; set; } = ModernButtonIcon.None;
    public int Radius { get; set; } = 7;

    public ModernButton()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = UiFonts.Strong;
        Height = 30;
        Padding = new Padding(10, 0, 10, 0);
    }

    protected override bool ShowFocusCues => false;

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? ThemeManager.Palette.Background);
        pevent.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var palette = ThemeManager.Palette;
        var baseColor = Primary ? palette.Accent
            : Danger ? palette.Down
            : Success ? palette.Up
            : palette.CardAlt;
        var hoverColor = Primary ? palette.AccentHover
            : Danger ? ControlPaint.Light(palette.Down)
            : Success ? ControlPaint.Light(palette.Up)
            : palette.RowHover;
        var fillColor = _pressed ? ControlPaint.Dark(baseColor) : _hovered ? hoverColor : baseColor;
        var textColor = Primary || Danger || Success ? Color.White : palette.Text;

        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        using var path = ThemeManager.RoundedRect(rect, Radius);
        using var fill = new SolidBrush(fillColor);
        pevent.Graphics.FillPath(fill, path);

        if (!Primary && !Danger && !Success)
        {
            using var border = new Pen(palette.Border);
            pevent.Graphics.DrawPath(border, path);
        }

        if (Icon == ModernButtonIcon.None)
        {
            TextRenderer.DrawText(
                pevent.Graphics,
                Text,
                Font,
                rect,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        var textSize = TextRenderer.MeasureText(pevent.Graphics, Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
        const int iconSize = 14;
        const int gap = 7;
        var totalWidth = iconSize + gap + textSize.Width;
        var iconX = rect.Left + Math.Max(0, (rect.Width - totalWidth) / 2);
        var iconY = rect.Top + Math.Max(0, (rect.Height - iconSize) / 2);
        var iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);
        DrawIcon(pevent.Graphics, iconRect, textColor);

        var textX = iconRect.Right + gap;
        var textRect = new Rectangle(textX, rect.Top, Math.Max(0, rect.Right - textX - 4), rect.Height);
        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            textRect,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        using var path = ThemeManager.RoundedRect(rect, Math.Min(Radius, Math.Min(Width, Height) / 2));
        Region?.Dispose();
        Region = new Region(path);
    }

    private void DrawIcon(Graphics graphics, Rectangle bounds, Color color)
    {
        using var pen = new Pen(color, 1.6F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        if (Icon == ModernButtonIcon.PaperPlane)
        {
            var points = new[]
            {
                new Point(bounds.Left + 1, bounds.Top + 6),
                new Point(bounds.Right - 1, bounds.Top + 2),
                new Point(bounds.Right - 5, bounds.Bottom - 2),
                new Point(bounds.Left + 6, bounds.Top + 9)
            };
            graphics.DrawPolygon(pen, points);
            graphics.DrawLine(pen, bounds.Left + 6, bounds.Top + 9, bounds.Left + 5, bounds.Bottom - 2);
            graphics.DrawLine(pen, bounds.Left + 6, bounds.Top + 9, bounds.Right - 1, bounds.Top + 2);
            return;
        }

        if (Icon == ModernButtonIcon.Eye)
        {
            using var path = new GraphicsPath();
            path.AddBezier(
                bounds.Left + 1,
                bounds.Top + bounds.Height / 2,
                bounds.Left + 4,
                bounds.Top + 2,
                bounds.Right - 4,
                bounds.Top + 2,
                bounds.Right - 1,
                bounds.Top + bounds.Height / 2);
            path.AddBezier(
                bounds.Right - 1,
                bounds.Top + bounds.Height / 2,
                bounds.Right - 4,
                bounds.Bottom - 2,
                bounds.Left + 4,
                bounds.Bottom - 2,
                bounds.Left + 1,
                bounds.Top + bounds.Height / 2);
            graphics.DrawPath(pen, path);
            graphics.DrawEllipse(pen, bounds.Left + 5, bounds.Top + 5, 4, 4);
        }
    }
}

public enum ModernButtonIcon
{
    None,
    PaperPlane,
    Eye
}

public sealed class WindowControlButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public WindowControlButtonKind Kind { get; init; }

    public WindowControlButton()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        Cursor = Cursors.Hand;
        Size = new Size(24, 24);
        Margin = new Padding(5, 3, 0, 0);
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? ThemeManager.Palette.Background);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var palette = ThemeManager.Palette;
        var baseColor = Kind == WindowControlButtonKind.Close
            ? palette.Down
            : palette.Accent;
        var fillColor = _pressed
            ? ControlPaint.Dark(baseColor)
            : _hovered
                ? ControlPaint.Light(baseColor)
                : baseColor;

        var circle = new Rectangle(3, 3, Width - 7, Height - 7);
        using (var brush = new SolidBrush(fillColor))
        {
            e.Graphics.FillEllipse(brush, circle);
        }

        using var pen = new Pen(Color.White, 1.55F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var centerX = Width / 2;
        var centerY = Height / 2;
        if (Kind == WindowControlButtonKind.Minimize)
        {
            e.Graphics.DrawLine(pen, centerX - 4, centerY, centerX + 4, centerY);
            return;
        }

        if (Kind == WindowControlButtonKind.Maximize)
        {
            e.Graphics.DrawRectangle(pen, centerX - 4, centerY - 4, 8, 8);
            return;
        }

        e.Graphics.DrawLine(pen, centerX - 4, centerY - 4, centerX + 4, centerY + 4);
        e.Graphics.DrawLine(pen, centerX + 4, centerY - 4, centerX - 4, centerY + 4);
    }
}

public enum WindowControlButtonKind
{
    Minimize,
    Maximize,
    Close
}

public sealed class ModernComboBox : ComboBox
{
    private const int WmPaint = 0x000F;

    public ModernComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        BackColor = ThemeManager.Palette.Input;
        ForeColor = ThemeManager.Palette.Text;
        Font = UiFonts.Body;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmPaint)
        {
            DrawChrome();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        BackColor = ThemeManager.Palette.Input;
        ForeColor = Enabled ? ThemeManager.Palette.Text : ThemeManager.Palette.MutedText;
        base.OnEnabledChanged(e);
        Invalidate();
    }

    private void DrawChrome()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var graphics = Graphics.FromHwnd(Handle);
        var theme = ThemeManager.Palette;
        var arrowRect = new Rectangle(Math.Max(0, Width - 22), 1, 21, Math.Max(1, Height - 2));
        using var fill = new SolidBrush(theme.Input);
        graphics.FillRectangle(fill, arrowRect);
        using var border = new Pen(theme.InputBorder);
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        var centerX = arrowRect.Left + arrowRect.Width / 2;
        var centerY = arrowRect.Top + arrowRect.Height / 2 + 1;
        var points = new[]
        {
            new Point(centerX - 4, centerY - 2),
            new Point(centerX + 4, centerY - 2),
            new Point(centerX, centerY + 3)
        };
        using var arrowBrush = new SolidBrush(Enabled ? theme.MutedText : ControlPaint.Dark(theme.MutedText));
        graphics.FillPolygon(arrowBrush, points);
    }
}

public sealed class BadgeLabel : Label
{
    public Color BadgeBackColor { get; set; } = ThemeManager.Palette.CardAlt;
    public Color BadgeForeColor { get; set; } = ThemeManager.Palette.Text;
    public int Radius { get; set; } = 10;

    public BadgeLabel()
    {
        AutoSize = false;
        Height = 22;
        TextAlign = ContentAlignment.MiddleCenter;
        Font = UiFonts.Strong;
        Padding = new Padding(8, 0, 8, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        using var path = ThemeManager.RoundedRect(rect, Radius);
        using var fill = new SolidBrush(BadgeBackColor);
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            rect,
            BadgeForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

public sealed class DirectionSwitch : UserControl
{
    private readonly ModernButton _upButton = new() { Text = "UP", Success = true };
    private readonly ModernButton _downButton = new() { Text = "DOWN", Danger = true };
    private string _value = "UP";

    public event EventHandler? ValueChanged;

    public string Value
    {
        get => _value;
        set
        {
            _value = value.Equals("DOWN", StringComparison.OrdinalIgnoreCase) ? "DOWN" : "UP";
            UpdateVisuals();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DirectionSwitch()
    {
        Height = 30;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Controls.Add(layout);

        _upButton.Dock = DockStyle.Fill;
        _downButton.Dock = DockStyle.Fill;
        _upButton.Margin = new Padding(0, 0, 4, 0);
        _downButton.Margin = new Padding(4, 0, 0, 0);
        _upButton.Click += (_, _) => Value = "UP";
        _downButton.Click += (_, _) => Value = "DOWN";
        layout.Controls.Add(_upButton, 0, 0);
        layout.Controls.Add(_downButton, 1, 0);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        _upButton.Success = _value == "UP";
        _upButton.Primary = false;
        _downButton.Danger = _value == "DOWN";
        _upButton.Text = _value == "UP" ? "↑  UP" : "UP";
        _downButton.Text = _value == "DOWN" ? "↓  DOWN" : "DOWN";
        _upButton.Invalidate();
        _downButton.Invalidate();
    }
}

public sealed class ModernNumberBox : TextBox
{
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = decimal.MaxValue;

    public decimal Value
    {
        get
        {
            if (TryParse(Text, out var value))
            {
                return Clamp(value);
            }

            return Clamp(Minimum);
        }
        set => Text = Format(Clamp(value));
    }

    public ModernNumberBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = ThemeManager.Palette.Input;
        ForeColor = ThemeManager.Palette.Text;
        Font = UiFonts.Body;
        TextAlign = HorizontalAlignment.Left;
        Width = 92;
        Dock = DockStyle.Left;
        Margin = Padding.Empty;
    }

    protected override void OnLeave(EventArgs e)
    {
        Value = Value;
        base.OnLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        ApplyTheme();
        base.OnEnabledChanged(e);
    }

    private void ApplyTheme()
    {
        BackColor = ThemeManager.Palette.Input;
        ForeColor = Enabled ? ThemeManager.Palette.Text : ThemeManager.Palette.MutedText;
    }

    private decimal Clamp(decimal value)
    {
        return Math.Min(Math.Max(value, Minimum), Maximum);
    }

    private static bool TryParse(string text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out value);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

public sealed class ModernDateBox : TextBox
{
    private DateTime _value = DateTime.Today;

    public DateTime Value
    {
        get
        {
            if (DateTime.TryParseExact(Text, "dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var parsed))
            {
                _value = parsed.Date;
            }
            else if (DateTime.TryParse(Text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out parsed))
            {
                _value = parsed.Date;
            }

            return _value.Date;
        }
        set
        {
            _value = value.Date;
            Text = _value.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"));
        }
    }

    public ModernDateBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = ThemeManager.Palette.Input;
        ForeColor = ThemeManager.Palette.Text;
        Font = UiFonts.Body;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
    }

    protected override void OnLeave(EventArgs e)
    {
        Value = Value;
        base.OnLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        BackColor = ThemeManager.Palette.Input;
        ForeColor = Enabled ? ThemeManager.Palette.Text : ThemeManager.Palette.MutedText;
        base.OnEnabledChanged(e);
    }
}

public sealed class ModernTimeBox : TextBox
{
    private DateTime _value = DateTime.Today;

    public DateTime Value
    {
        get
        {
            if (DateTime.TryParseExact(Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                _value = DateTime.Today.AddHours(parsed.Hour).AddMinutes(parsed.Minute);
            }
            else if (TimeSpan.TryParse(Text, CultureInfo.InvariantCulture, out var time))
            {
                _value = DateTime.Today.Add(time);
            }

            return _value;
        }
        set
        {
            _value = DateTime.Today.AddHours(value.Hour).AddMinutes(value.Minute);
            Text = _value.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }

    public ModernTimeBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = ThemeManager.Palette.Input;
        ForeColor = ThemeManager.Palette.Text;
        Font = UiFonts.Body;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
    }

    protected override void OnLeave(EventArgs e)
    {
        Value = Value;
        base.OnLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        BackColor = ThemeManager.Palette.Input;
        ForeColor = Enabled ? ThemeManager.Palette.Text : ThemeManager.Palette.MutedText;
        base.OnEnabledChanged(e);
    }
}
