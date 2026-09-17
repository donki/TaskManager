using TaskManager.Core.Services;

namespace TaskManager.Mobile.Pages;

/// <summary>
/// Todas las etiquetas (tambien las que solo llevan tareas hechas), con cuantas tareas tiene cada
/// una y una papelera para borrarla de todas. Si la llevan tareas sin acabar, se pregunta.
/// </summary>
public sealed class TagsPage : ContentPage
{
    private readonly TaskService _tasks;
    private readonly VerticalStackLayout _rows = new() { Spacing = 0 };
    private readonly Label _empty;

    /// <summary>Se ha borrado alguna: quien abrio la pagina tiene que releer.</summary>
    public bool Changed { get; private set; }

    public TagsPage(TaskService tasks)
    {
        _tasks = tasks;
        var loc = Localization.Loc.Instance;
        Title = loc["TagsTitle"];

        _empty = new Label { Text = loc["TagsNone"], Style = (Style)Application.Current!.Resources["HintText"], IsVisible = false };
        var card = new Border { Style = (Style)Application.Current.Resources["Card"] };
        var inner = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 8 };
        inner.Add(new Label { Text = loc["TagsIntro"], Style = (Style)Application.Current.Resources["HintText"] });
        inner.Add(_empty);
        inner.Add(_rows);
        card.Content = inner;
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { card } } };
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var loc = Localization.Loc.Instance;
        _rows.Clear();
        var tags = await _tasks.Repository.GetTagsAsync(pendingOnly: false);
        _empty.IsVisible = tags.Count == 0;
        foreach (var tag in tags)
        {
            var (pending, total) = await _tasks.Repository.CountTagAsync(tag);
            var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], Padding = new Thickness(0, 6) };
            var text = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
            text.Add(new Label { Text = "#" + tag, Style = (Style)Application.Current!.Resources["ItemTitle"] });
            text.Add(new Label { Text = loc.Format("TagsCount", pending, total), Style = (Style)Application.Current.Resources["ItemSubtitle"] });
            grid.Add(text, 0);
            var delete = new ImageButton { Style = (Style)Application.Current.Resources["RowIconButton"], Source = "ic_delete_danger.png" };
            delete.Clicked += async (_, _) =>
            {
                var message = pending > 0 ? loc.Format("DeleteTagPending", tag, pending, total) : loc.Format("DeleteTagDone", tag, total);
                if (!await SocShared.ModernDialog.AlertAsync(this, loc["DeleteTag"], message, loc["Delete"], loc["Cancel"]))
                    return;
                await _tasks.Repository.DeleteTagAsync(tag);
                Changed = true;
                await ReloadAsync();
            };
            grid.Add(delete, 1);
            _rows.Add(grid);
            _rows.Add(new BoxView { HeightRequest = 1, Style = (Style)Application.Current.Resources["Separator"] });
        }
    }
}
