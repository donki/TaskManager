using TaskManager.Core.Gamification;

namespace TaskManager.Mobile.Controls;

/// <summary>
/// Celebracion al completar (especificacion 4.A): confeti, destello dorado e indicador flotante de
/// XP. Se pone encima del contenido de la pagina y no intercepta el toque, asi que la lista se
/// sigue pudiendo usar mientras cae el confeti.
/// </summary>
public sealed class CelebrationView : Grid
{
    private readonly ConfettiDrawable _confetti = new();
    private readonly GraphicsView _canvas;
    private readonly Label _xpLabel;
    private readonly Border _xpBadge;
    private readonly IDispatcherTimer _timer;

    public CelebrationView()
    {
        InputTransparent = true;
        CascadeInputTransparent = true;

        _canvas = new GraphicsView { Drawable = _confetti, InputTransparent = true };

        _xpLabel = new Label
        {
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        _xpBadge = new Border
        {
            BackgroundColor = Color.FromArgb("#3525CD"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(18, 8),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 60, 0, 0),
            Opacity = 0,
            Content = _xpLabel,
            InputTransparent = true,
        };

        Add(_canvas);
        Add(_xpBadge);

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) =>
        {
            if (!_confetti.Advance())
            {
                _timer.Stop();
            }

            _canvas.Invalidate();
        };
    }

    /// <summary>Sonido y vibracion son configurables (especificacion 4.A); los lee la pagina.</summary>
    public bool HapticsEnabled { get; set; } = true;

    public async void Celebrate(Celebration celebration)
    {
        var width = Width > 0 ? Width : 360;
        var height = Height > 0 ? Height : 640;

        // Mas combo, mas confeti: es el "efecto visual incremental" de la especificacion.
        _confetti.Burst(new PointF((float)(width / 2), (float)(height * 0.35)), celebration.Combo);
        if (!_timer.IsRunning)
        {
            _timer.Start();
        }

        if (HapticsEnabled)
        {
            TryHaptic(celebration.LeveledUp);
        }

        _xpLabel.Text = celebration.LeveledUp
            ? $"¡Nivel {celebration.Level}!  +{celebration.Xp} XP"
            : celebration.IsCombo
                ? $"+{celebration.Xp} XP  ·  ¡Racha x{celebration.Combo:0.#}!"
                : $"+{celebration.Xp} XP";

        _xpBadge.Opacity = 0;
        _xpBadge.TranslationY = 10;
        await Task.WhenAll(_xpBadge.FadeToAsync(1, 120), _xpBadge.TranslateToAsync(0, 0, 160, Easing.CubicOut));
        await Task.Delay(1300);
        await _xpBadge.FadeToAsync(0, 350);
    }

    private static void TryHaptic(bool strong)
    {
        try
        {
            HapticFeedback.Default.Perform(strong ? HapticFeedbackType.LongPress : HapticFeedbackType.Click);
        }
        catch (FeatureNotSupportedException)
        {
            // Dispositivo sin vibrador: la celebracion visual se basta.
        }
    }

    /// <summary>
    /// Confeti dibujado a mano sobre el lienzo de MAUI Graphics: sin dependencias externas y sin
    /// imagenes, que es lo que permite que reaccione al combo.
    /// </summary>
    private sealed class ConfettiDrawable : IDrawable
    {
        private static readonly Color[] Palette =
        [
            Color.FromArgb("#3525CD"),
            Color.FromArgb("#635BF2"),
            Color.FromArgb("#F5C542"),
            Color.FromArgb("#27AE60"),
            Color.FromArgb("#E86A92"),
        ];

        private sealed class Piece
        {
            public float X;
            public float Y;
            public float VelocityX;
            public float VelocityY;
            public float Size;
            public float Angle;
            public float Spin;
            public float Life = 1f;
            public Color Color = Colors.White;
        }

        private readonly List<Piece> _pieces = [];

        public void Burst(PointF origin, double intensity)
        {
            var count = (int)Math.Clamp(24 * intensity, 16, 80);
            for (var i = 0; i < count; i++)
            {
                var angle = Random.Shared.NextDouble() * Math.PI * 2;
                var speed = (2.0 + Random.Shared.NextDouble() * 4.0) * intensity;

                _pieces.Add(new Piece
                {
                    X = origin.X,
                    Y = origin.Y,
                    VelocityX = (float)(Math.Cos(angle) * speed),
                    VelocityY = (float)(Math.Sin(angle) * speed) - 3f,
                    Size = Random.Shared.Next(5, 11),
                    Spin = (float)((Random.Shared.NextDouble() - 0.5) * 20),
                    Color = Palette[Random.Shared.Next(Palette.Length)],
                });
            }
        }

        /// <summary>Avanza un fotograma. Devuelve false cuando ya no queda nada que pintar.</summary>
        public bool Advance()
        {
            for (var i = _pieces.Count - 1; i >= 0; i--)
            {
                var p = _pieces[i];
                p.VelocityY += 0.32f;
                p.VelocityX *= 0.99f;
                p.X += p.VelocityX;
                p.Y += p.VelocityY;
                p.Angle += p.Spin;
                p.Life -= 0.011f;

                if (p.Life <= 0)
                {
                    _pieces.RemoveAt(i);
                }
            }

            return _pieces.Count > 0;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            foreach (var p in _pieces)
            {
                canvas.SaveState();
                canvas.Alpha = Math.Clamp(p.Life, 0f, 1f);
                canvas.FillColor = p.Color;
                canvas.Rotate(p.Angle, p.X, p.Y);
                canvas.FillRoundedRectangle(p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size * 1.6f, 1.5f);
                canvas.RestoreState();
            }
        }
    }
}
