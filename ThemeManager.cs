using System.Drawing.Drawing2D;

namespace ForecastDesk;

public enum AppTheme
{
    Dark,
    Light
}

public sealed record ThemePalette(
    Color Background,
    Color Panel,
    Color Card,
    Color CardAlt,
    Color Border,
    Color Text,
    Color MutedText,
    Color Input,
    Color InputBorder,
    Color Accent,
    Color AccentHover,
    Color Up,
    Color Down,
    Color Warning,
    Color Success,
    Color RowHover);

public static class ThemeManager
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public static ThemePalette Palette { get; private set; } = null!;

    public static readonly ThemePalette Dark = new(
        Background: Color.FromArgb(5, 14, 24),
        Panel: Color.FromArgb(8, 18, 31),
        Card: Color.FromArgb(10, 23, 38),
        CardAlt: Color.FromArgb(13, 28, 46),
        Border: Color.FromArgb(29, 43, 62),
        Text: Color.FromArgb(231, 238, 248),
        MutedText: Color.FromArgb(137, 154, 176),
        Input: Color.FromArgb(12, 25, 42),
        InputBorder: Color.FromArgb(34, 50, 72),
        Accent: Color.FromArgb(37, 99, 235),
        AccentHover: Color.FromArgb(52, 119, 255),
        Up: Color.FromArgb(0, 181, 126),
        Down: Color.FromArgb(244, 63, 94),
        Warning: Color.FromArgb(245, 178, 35),
        Success: Color.FromArgb(0, 200, 118),
        RowHover: Color.FromArgb(18, 35, 56));

    public static readonly ThemePalette Light = new(
        Background: Color.FromArgb(245, 247, 250),
        Panel: Color.White,
        Card: Color.White,
        CardAlt: Color.FromArgb(250, 252, 255),
        Border: Color.FromArgb(221, 227, 234),
        Text: Color.FromArgb(19, 26, 37),
        MutedText: Color.FromArgb(99, 113, 134),
        Input: Color.White,
        InputBorder: Color.FromArgb(205, 215, 228),
        Accent: Color.FromArgb(37, 99, 235),
        AccentHover: Color.FromArgb(29, 78, 216),
        Up: Color.FromArgb(0, 166, 118),
        Down: Color.FromArgb(220, 38, 38),
        Warning: Color.FromArgb(217, 119, 6),
        Success: Color.FromArgb(22, 163, 74),
        RowHover: Color.FromArgb(238, 244, 255));

    static ThemeManager()
    {
        Palette = Dark;
    }

    public static void Use(AppTheme theme)
    {
        CurrentTheme = theme;
        Palette = theme == AppTheme.Dark ? Dark : Light;
    }

    public static void StyleInput(Control control)
    {
        control.BackColor = Palette.Input;
        control.ForeColor = Palette.Text;
        control.Font = UiFonts.Body;
    }

    public static void StyleLabel(Label label, bool muted = false, bool strong = false)
    {
        label.ForeColor = muted ? Palette.MutedText : Palette.Text;
        label.Font = strong ? UiFonts.Strong : UiFonts.Body;
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        radius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        if (radius == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public static class UiFonts
{
    public static readonly Font Body = new("Segoe UI", 9F);
    public static readonly Font Strong = new("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8.25F);
    public static readonly Font Title = new("Segoe UI Semibold", 10F, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 8.5F);
}
