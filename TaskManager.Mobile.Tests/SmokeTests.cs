using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Tests.Infra;

namespace TaskManager.Mobile.Tests;

public class SmokeTests
{
    [Fact]
    public void Todas_las_paginas_se_construyen_con_su_XAML() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();

        Page[] pages =
        [
            new LoginPage(), new MyTasksPage(), new CalendarPage(), new ListsPage(), new KanbanPage(),
            new ListDetailPage(), new TaskDetailPage(), new MailPage(), new GroupsPage(), new SettingsPage(),
            new AboutPage(), new WhatsNewPage(), new AppShell(),
        ];

        Assert.All(pages, p => Assert.NotNull(p));
    });
}
