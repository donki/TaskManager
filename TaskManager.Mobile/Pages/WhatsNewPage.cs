using System.Globalization;
using TaskManager.Core.Services;
using TaskManager.Mobile.Helpers;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Novedades de las cinco ultimas versiones, de la mas nueva a la mas antigua (General 6.7). Sale
/// sola la primera vez que se abre una version nueva (desde «Mis tareas») y se puede abrir desde el
/// menu y desde Acerca de. El contenido es el del nucleo (<see cref="WhatsNew"/>), el mismo que en
/// Windows.
/// </summary>
public sealed class WhatsNewPage : ContentPage
{
    private readonly VerticalStackLayout _list = new() { Padding = new Thickness(16), Spacing = 16 };

    public WhatsNewPage()
    {
        SetBinding(TitleProperty, new Binding("[MenuWhatsNew]", source: Localization.Loc.Instance));
        Content = new ScrollView { Content = _list };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var current = AppInfo.Current.VersionString;
        await ServiceHelper.GetRequiredService<SettingsService>().MarkVersionSeenAsync(current);

        Fill(current);
    }

    private void Fill(string current)
    {
        var loc = Localization.Loc.Instance;
        var resources = Application.Current!.Resources;
        var culture = new CultureInfo(loc.Language);

        _list.Clear();
        var releases = WhatsNew.Load(loc.Language);
        if (releases.Count == 0)
        {
            _list.Add(new Label { Text = loc["WhatsNewEmpty"], Style = (Style)resources["HintText"] });
            return;
        }

        foreach (var release in releases)
        {
            var stack = new VerticalStackLayout { Padding = new Thickness(20, 18), Spacing = 8 };
            stack.Add(new Label
            {
                Text = WhatsNew.SameVersion(release.Version, current) ? loc.Format("WhatsNewCurrent", release.Version) : release.Version,
                Style = (Style)resources["CardTitle"],
            });

            if (release.Date is { } date)
            {
                stack.Add(new Label { Text = date.ToString("D", culture), Style = (Style)resources["HintText"] });
            }

            foreach (var item in release.Items)
            {
                var row = new Grid
                {
                    ColumnDefinitions = [new ColumnDefinition(new GridLength(14)), new ColumnDefinition(GridLength.Star)],
                    ColumnSpacing = 8,
                };
                row.Add(new BoxView
                {
                    Color = (Color)resources["Primary"],
                    WidthRequest = 6,
                    HeightRequest = 6,
                    CornerRadius = 3,
                    VerticalOptions = LayoutOptions.Start,
                    Margin = new Thickness(0, 8, 0, 0),
                }, 0, 0);
                row.Add(new Label { Text = item, Style = (Style)resources["BodyText"] }, 1, 0);
                stack.Add(row);
            }

            _list.Add(new Border { Style = (Style)resources["Card"], Content = stack });
        }
    }
}
