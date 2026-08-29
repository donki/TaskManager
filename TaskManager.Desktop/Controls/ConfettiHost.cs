using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskManager.Desktop.Controls;

/// <summary>
/// Confeti de escritorio (especificacion 6.B). Lienzo transparente que no intercepta el raton:
/// se dibuja encima del panel sin estorbar a lo que hay debajo.
/// </summary>
public sealed class ConfettiHost : Canvas
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x35, 0x25, 0xCD),
        Color.FromRgb(0x63, 0x5B, 0xF2),
        Color.FromRgb(0xF5, 0xC5, 0x42),
        Color.FromRgb(0x27, 0xAE, 0x60),
        Color.FromRgb(0xE8, 0x6A, 0x92),
    ];

    private readonly List<Particle> _particles = [];
    private bool _running;
    private TimeSpan _lastTick;

    public ConfettiHost()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    private sealed class Particle
    {
        public required Rectangle Shape { get; init; }

        public double X { get; set; }

        public double Y { get; set; }

        public double VelocityX { get; set; }

        public double VelocityY { get; set; }

        public double Spin { get; set; }

        public double Angle { get; set; }

        public double Life { get; set; }
    }

    /// <summary>
    /// <paramref name="intensity"/> viene del combo: una racha alta lanza mas piezas y mas lejos,
    /// que es el "efecto visual incremental" de la especificacion 4.A.
    /// </summary>
    public void Burst(Point origin, double intensity = 1.0)
    {
        var count = (int)Math.Clamp(18 * intensity, 12, 60);
        for (var i = 0; i < count; i++)
        {
            var color = Palette[Random.Shared.Next(Palette.Length)];
            var shape = new Rectangle
            {
                Width = Random.Shared.Next(4, 9),
                Height = Random.Shared.Next(6, 12),
                Fill = new SolidColorBrush(color),
                RadiusX = 1,
                RadiusY = 1,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var speed = (2.5 + Random.Shared.NextDouble() * 4.5) * intensity;

            var particle = new Particle
            {
                Shape = shape,
                X = origin.X,
                Y = origin.Y,
                VelocityX = Math.Cos(angle) * speed,
                VelocityY = Math.Sin(angle) * speed - 3,
                Spin = (Random.Shared.NextDouble() - 0.5) * 24,
                Life = 1.0,
            };

            Children.Add(shape);
            _particles.Add(particle);
        }

        Start();
    }

    private void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _lastTick = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // El delta real evita que el confeti vaya al doble de velocidad en pantallas de 120 Hz.
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        var delta = _lastTick == TimeSpan.Zero ? 1.0 : (now - _lastTick).TotalSeconds * 60.0;
        _lastTick = now;
        delta = Math.Clamp(delta, 0.5, 3.0);

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.VelocityY += 0.35 * delta;          // gravedad
            p.VelocityX *= Math.Pow(0.99, delta); // rozamiento
            p.X += p.VelocityX * delta;
            p.Y += p.VelocityY * delta;
            p.Angle += p.Spin * delta;
            p.Life -= 0.012 * delta;

            if (p.Life <= 0 || p.Y > ActualHeight + 40)
            {
                Children.Remove(p.Shape);
                _particles.RemoveAt(i);
                continue;
            }

            SetLeft(p.Shape, p.X);
            SetTop(p.Shape, p.Y);
            p.Shape.Opacity = Math.Clamp(p.Life, 0, 1);
            p.Shape.RenderTransform = new RotateTransform(p.Angle);
        }

        if (_particles.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
    }
}
