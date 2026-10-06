using TaskManager.Core.Data;
using TaskManager.Core.Models;

namespace TaskManager.Tests;

/// <summary>
/// El filtro por varias etiquetas (Ctrl+clic en las pastillas): como se guarda, como se marca y
/// que tareas deja pasar.
/// </summary>
public class TagFilterTests
{
    [Fact]
    public void Click_SinCtrl_DejaSoloEsa_ConCtrl_SumaYQuita()
    {
        Assert.Equal("obra", TagFilter.Click("casa", "obra", ctrl: false));
        Assert.Equal("casa,obra", TagFilter.Click("casa", "obra", ctrl: true));
        Assert.Equal("obra", TagFilter.Click("casa,obra", "CASA", ctrl: true));
        Assert.Null(TagFilter.Click("obra", "obra", ctrl: true));

        // «Todas» (null) borra el filtro con Ctrl o sin el.
        Assert.Null(TagFilter.Click("casa,obra", null, ctrl: true));
        Assert.Null(TagFilter.Click("casa,obra", null, ctrl: false));

        Assert.Equal($"casa,{TaskRepository.NoTag}", TagFilter.Click("casa", TaskRepository.NoTag, ctrl: true));
    }

    [Fact]
    public void Has_EnciendeLasMarcadas_YTodasSoloSinFiltro()
    {
        Assert.True(TagFilter.Has(null, null));
        Assert.False(TagFilter.Has("casa", null));
        Assert.True(TagFilter.Has("casa,obra", "Obra"));
        Assert.False(TagFilter.Has("casa,obra", "coche"));
        Assert.False(TagFilter.Has(null, "casa"));
    }

    [Fact]
    public void Lo_Guardado_Antes_Una_Etiqueta_Se_Sigue_Leyendo()
    {
        Assert.Equal(["casa"], TagFilter.Parse("casa"));
        Assert.Equal([TaskRepository.NoTag], TagFilter.Parse(TaskRepository.NoTag));
        Assert.Empty(TagFilter.Parse(null));
        Assert.Empty(TagFilter.Parse(""));
        Assert.Equal(["casa", "obra"], TagFilter.Parse("casa,obra,Casa,"));
    }

    [Fact]
    public void Prune_Y_Without_QuitanSoloLasQueSobran()
    {
        Assert.Equal($"casa,{TaskRepository.NoTag}",
            TagFilter.Prune($"casa,borrada,{TaskRepository.NoTag}", ["Casa", "obra"]));
        Assert.Null(TagFilter.Prune("borrada", ["casa"]));
        Assert.Null(TagFilter.Prune(null, ["casa"]));

        Assert.Equal("obra", TagFilter.Without("casa,obra", "CASA"));
        Assert.Null(TagFilter.Without("casa", "casa"));
    }

    [Fact]
    public void Matches_EntraLaQueLleveCualquiera()
    {
        Assert.True(TagFilter.Matches(null, ["x"]));
        Assert.True(TagFilter.Matches("casa,obra", ["Obra", "x"]));
        Assert.False(TagFilter.Matches("casa,obra", ["x"]));
        Assert.False(TagFilter.Matches("casa,obra", []));
        Assert.True(TagFilter.Matches($"casa,{TaskRepository.NoTag}", []));
        Assert.False(TagFilter.Matches(TaskRepository.NoTag, ["x"]));
    }

    [Fact]
    public void Describe_LasNombraTodas()
    {
        Assert.Equal("#casa · Sin etiqueta", TagFilter.Describe($"casa,{TaskRepository.NoTag}", "Sin etiqueta"));
        Assert.Equal(string.Empty, TagFilter.Describe(null, "Sin etiqueta"));
    }

    [Fact]
    public async Task Repositorio_FiltraPorVariasEtiquetas()
    {
        await using var s = await TestStore.CreateAsync();
        var repo = s.Repository;
        var list = await repo.CreateListAsync("L");
        var a = await repo.AddTaskAsync(list.Id, "a");
        var b = await repo.AddTaskAsync(list.Id, "b");
        var c = await repo.AddTaskAsync(list.Id, "c");
        var d = await repo.AddTaskAsync(list.Id, "d");
        await repo.AddTagAsync([a.Id], "casa");
        await repo.AddTagAsync([b.Id], "obra");
        await repo.AddTagAsync([c.Id], "coche");

        static IEnumerable<string> Titles(IEnumerable<TaskItem> t) => t.Select(x => x.Title).Order();

        Assert.Equal(["a", "b"], Titles(await repo.GetAllTasksAsync(TaskFilter.All, "casa,obra")));
        Assert.Equal(["a", "d"], Titles(await repo.GetAllTasksAsync(TaskFilter.All, $"casa,{TaskRepository.NoTag}")));
        Assert.Equal(["b"], Titles(await repo.GetTasksAsync(list.Id, tag: "OBRA")));
    }
}
