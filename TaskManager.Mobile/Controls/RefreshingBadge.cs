namespace TaskManager.Mobile.Controls;

/// <summary>
/// Aviso flotante de «estoy actualizando»: una pastilla con la rueda girando, arriba y centrada.
/// </summary>
/// <remarks>
/// <para><b>Por que hace falta.</b> El boton de actualizar de la barra habla con el servidor y
/// espera a que termine. Mientras tanto no cambiaba nada en pantalla: con la red lenta parecia que
/// el boton no hacia nada y se pulsaba otra vez. Tirar hacia abajo si tenia rueda —la del
/// <c>RefreshView</c>—, asi que el mismo gesto daba respuesta por un camino y no por el otro.</para>
///
/// <para>Va <b>encima</b> del contenido y no intercepta el toque, como la celebracion
/// (<see cref="CelebrationView"/>): actualizar no bloquea, se puede seguir escribiendo mientras
/// llega lo de fuera. Y no ocupa sitio en la maqueta cuando esta apagado, asi que ninguna pantalla
/// se descoloca al aparecer.</para>
/// </remarks>
public sealed class RefreshingBadge : Border
{
    private readonly ActivityIndicator _spinner;

    public RefreshingBadge()
    {
        _spinner = new ActivityIndicator
        {
            WidthRequest = 18,
            HeightRequest = 18,
            Color = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        BackgroundColor = Color.FromArgb("#3525CD");
        StrokeThickness = 0;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 };
        Padding = new Thickness(16, 8);
        Margin = new Thickness(0, 12, 0, 0);
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Start;

        InputTransparent = true;
        IsVisible = false;

        Content = new HorizontalStackLayout
        {
            Spacing = 10,

            // El Border no propaga el «no me toques» a lo de dentro (CascadeInputTransparent es de
            // Layout, no de Border), asi que se dice tambien aqui.
            InputTransparent = true,
            CascadeInputTransparent = true,
            Children =
            {
                _spinner,
                new Label
                {
                    Text = Localization.Loc.Instance["Refreshing"],
                    FontSize = 13,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center,
                },
            },
        };
    }

    /// <summary>
    /// Enciende la pastilla, hace lo que se le pida y la apaga pase lo que pase.
    /// </summary>
    /// <remarks>
    /// Se envuelve el trabajo en vez de dejar dos llamadas sueltas —encender y apagar— porque un
    /// fallo de red entre las dos dejaria la rueda girando para siempre, que es peor que no tener
    /// ninguna.
    /// </remarks>
    public async Task WhileAsync(Func<Task> work)
    {
        IsVisible = true;
        _spinner.IsRunning = true;

        try
        {
            await work();
        }
        finally
        {
            _spinner.IsRunning = false;
            IsVisible = false;
        }
    }
}
