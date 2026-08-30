using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace TaskManager.Desktop.Services;

/// <summary>
/// Icono de bandeja. El icono se dibuja en memoria (nada de ficheros .ico sueltos) para poder
/// pintar encima el numero de tareas pendientes de "Mi Dia", que es lo que pide la
/// especificacion 6.A.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private static readonly Color Indigo = Color.FromArgb(0x35, 0x25, 0xCD);
    private static readonly Color Badge = Color.FromArgb(0xE5, 0x3E, 0x3E);

    private readonly WinForms.NotifyIcon _icon;
    private Icon? _current;

    public TrayIconHost()
    {
        _icon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "Task Manager",
            ContextMenuStrip = BuildMenu(),
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                Activated?.Invoke(this, EventArgs.Empty);
            }
        };

        SetPending(0);
    }

    /// <summary>Clic izquierdo: desplegar el panel rapido.</summary>
    public event EventHandler? Activated;

    public event EventHandler? ExitRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? MainRequested;

    /// <summary>Ultimo recuento pintado, para poder rehacer el icono sin volver a consultarlo.</summary>
    public int Pending { get; private set; }

    /// <summary>Vuelve a montar el menu, que es lo que hace falta al cambiar de idioma.</summary>
    public void RebuildMenu()
    {
        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = BuildMenu();
        old?.Dispose();
    }

    public void SetPending(int pending)
    {
        Pending = pending;
        var previous = _current;
        _current = Render(pending);
        _icon.Icon = _current;
        _icon.Text = pending switch
        {
            0 => TaskManager.Desktop.Localization.Loc.Get("TrayUpToDate"),
            1 => TaskManager.Desktop.Localization.Loc.Get("TrayOnePending"),
            _ => TaskManager.Desktop.Localization.Loc.Format("TrayManyPending", pending),
        };

        previous?.Dispose();
    }

    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(3000, title, message, WinForms.ToolTipIcon.None);

    /// <summary>
    /// El mismo icono que en Android —tick blanco sobre el indigo de marca— para la ventana y la
    /// barra de tareas. Se dibuja aqui en vez de arrastrar un .ico: asi hay un unico sitio donde
    /// esta definido el icono de la aplicacion, y las dos plataformas ensenan lo mismo.
    /// </summary>
    public static System.Windows.Media.ImageSource CreateWindowIcon()
    {
        using var icon = Render(0);
        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();

        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    private WinForms.ContextMenuStrip BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        var open = new WinForms.ToolStripMenuItem(TaskManager.Desktop.Localization.Loc.Get("TrayOpen"));
        open.Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);

        var main = new WinForms.ToolStripMenuItem(TaskManager.Desktop.Localization.Loc.Get("OpenMainWindow"));
        main.Click += (_, _) => MainRequested?.Invoke(this, EventArgs.Empty);

        var settings = new WinForms.ToolStripMenuItem(TaskManager.Desktop.Localization.Loc.Get("MenuSettings"));
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var exit = new WinForms.ToolStripMenuItem(TaskManager.Desktop.Localization.Loc.Get("TrayExit"));
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(open);
        menu.Items.Add(main);
        menu.Items.Add(settings);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    /// <summary>
    /// Cuadrado indigo redondeado con un tick blanco y, si hay pendientes, un globo rojo con el
    /// numero en la esquina.
    /// </summary>
    private static Icon Render(int pending)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var background = new SolidBrush(Indigo);
            using var path = RoundedRect(new Rectangle(1, 1, 30, 30), 8);
            g.FillPath(background, path);

            using var pen = new Pen(Color.White, 3.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(pen, [new PointF(9, 16.5f), new PointF(14, 21.5f), new PointF(23, 11)]);

            if (pending > 0)
            {
                using var badge = new SolidBrush(Badge);
                g.FillEllipse(badge, 16, 0, 16, 16);

                var text = pending > 9 ? "9+" : pending.ToString();
                using var font = new Font("Segoe UI", pending > 9 ? 6.5f : 8f, FontStyle.Bold, GraphicsUnit.Point);
                using var white = new SolidBrush(Color.White);
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, white, 24 - size.Width / 2, 8 - size.Height / 2);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Clonar para poder destruir el HICON: Icon.FromHandle no toma la propiedad del recurso.
            using var shared = Icon.FromHandle(handle);
            return (Icon)shared.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
