using TaskManager.Core.Models;
using TaskManager.Core.Services;
using TaskManager.Mobile.Pages;
using TaskManager.Mobile.Services;
using TaskManager.Mobile.Tests.Infra;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace TaskManager.Mobile.Tests;

/// <summary>«Mis grupos»: crear, invitar, entrar (tecleando, por enlace o por QR), salir y borrar.</summary>
public class GroupsPageTests
{
    private static async Task<(GroupsPage Page, FakeSync Sync)> OpenAsync(TestApp app)
    {
        var sync = new FakeSync();
        var page = new GroupsPage(app.Tasks, sync);
        await page.Appear();
        return (page, sync);
    }

    private static List<GroupRow> Rows(GroupsPage page) => (List<GroupRow>)page.Named<CollectionView>("GroupsView").ItemsSource;

    [Fact]
    public void Enseña_los_grupos_con_sus_listas_y_lo_pendiente() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var group = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "FAM001" });
        var compra = await app.Repository.CreateListAsync("Compra", group.Id);
        await app.Repository.CreateListAsync("Vacia", group.Id);
        var hechas = await app.Repository.CreateListAsync("Hechas", group.Id);
        await app.Repository.AddTaskAsync(compra.Id, "Pan");
        var hecha = await app.Repository.AddTaskAsync(compra.Id, "Leche");
        await app.Tasks.CompleteTaskAsync(hecha);
        await app.Tasks.CompleteTaskAsync(await app.Repository.AddTaskAsync(hechas.Id, "Fruta"));

        var (page, _) = await OpenAsync(app);

        var row = Rows(page).Single();
        Assert.Equal("Familia", row.Name);
        Assert.Equal("Código FAM001 · 3 listas", row.Caption);
        var captions = row.Lists.ToDictionary(l => l.Name, l => l.Caption);
        Assert.Equal("1 de 2 pendientes", captions["Compra"]);
        Assert.Equal("Vacía", captions["Vacia"]);
        Assert.Equal("1 completadas", captions["Hechas"]);

        // Tocar una lista la abre; refrescar repinta.
        await page.Handler("OnListTapped", null, new TappedEventArgs(compra.Id));
        await page.Handler("OnListTapped", null, new TappedEventArgs(null));
        Assert.Equal($"{nameof(ListDetailPage)}?listId={compra.Id}", app.Ui.Routes.Single());
        await page.Handler("OnRefreshClicked");
        Assert.Single(Rows(page));
    });

    [Fact]
    public void Crear_un_grupo_le_pone_lista_y_enseña_el_QR() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (page, sync) = await OpenAsync(app);

        await page.Handler("OnNewGroupClicked");
        Assert.Empty(await app.Repository.GetGroupsAsync());

        app.Ui.Answer("  Piso  ");
        await page.Handler("OnNewGroupClicked");

        var group = (await app.Repository.GetGroupsAsync()).Single();
        Assert.Equal("Piso", group.Name);
        Assert.Equal("ABC123", group.JoinCode);
        Assert.Equal("General", (await app.Repository.GetGroupListsAsync(group.Id)).Single().Name);
        Assert.Contains("create:Piso", sync.Calls);
        Assert.Contains("ABC123", app.Ui.Clipboard.Single());

        var qr = Assert.IsType<GroupQrPage>(app.Ui.Pushed.Single());
        Assert.Equal("clave-compartida", qr.Named<Label>("KeyLabel").Text);
        Assert.Equal(app.Texts.Format("GroupCodeOnly", "ABC123"), qr.Named<Label>("CodeLabel").Text);
        Assert.NotNull(qr.Named<Image>("QrImage").Source);
    });

    [Fact]
    public void Si_el_servidor_falla_al_crear_el_grupo_no_queda_a_medias() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (page, sync) = await OpenAsync(app);
        sync.Fails = new HttpRequestException("sin red");

        app.Ui.Answer("Piso");
        await page.Handler("OnNewGroupClicked");

        Assert.Empty(await app.Repository.GetGroupsAsync());
        Assert.Equal("sin red", app.Ui.Dialogs.Last().Message);
        Assert.Empty(app.Ui.Pushed);
    });

    [Fact]
    public void Invitar_pide_una_clave_nueva_y_enseña_el_QR() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var group = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "FAM001" });
        var (page, sync) = await OpenAsync(app);

        await page.Handler("OnInviteClicked", new Button());
        await page.Handler("OnInviteClicked", PageDriver.RowButton(Guid.NewGuid()));
        Assert.Empty(sync.Calls);

        await page.Handler("OnInviteClicked", PageDriver.RowButton(group.Id));
        Assert.Equal(["renew:FAM001"], sync.Calls);
        Assert.IsType<GroupQrPage>(app.Ui.Pushed.Single());

        sync.Fails = new InvalidOperationException("no eres el dueño");
        await page.Handler("OnInviteClicked", PageDriver.RowButton(group.Id));
        Assert.Equal("no eres el dueño", app.Ui.Dialogs.Last().Message);
        Assert.Single(app.Ui.Pushed);
    });

    [Fact]
    public void Entrar_tecleando_codigo_y_clave() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (page, sync) = await OpenAsync(app);

        // Cancelar el codigo, o la clave: no se intenta.
        await page.Handler("OnJoinGroupClicked");
        app.Ui.Answer("ABC123");
        await page.Handler("OnJoinGroupClicked");
        Assert.Empty(sync.Calls);

        app.Ui.Answer(" ABC123 ", " clave ");
        await page.Handler("OnJoinGroupClicked");
        Assert.Equal(["join:ABC123:clave", "pull"], sync.Calls);
        Assert.Equal(app.Texts["JoinedTitle"], app.Ui.Dialogs.Last().Title);

        // Clave mal escrita: el servidor contesta con un error y se dice, sin cerrar nada.
        sync.Fails = new HttpRequestException("400");
        app.Ui.Answer("ABC123", "mala");
        await page.Handler("OnJoinGroupClicked");
        Assert.Equal("400", app.Ui.Dialogs.Last().Message);
    });

    [Fact]
    public void Una_invitacion_que_llega_de_fuera_pregunta_y_se_consume() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var invite = new GroupInvite("XYZ789", "secreto");

        // Algo que no es una invitacion se ignora.
        GroupInviteLinks.Anotar("no es una direccion");
        GroupInviteLinks.Anotar((string?)null);
        GroupInviteLinks.Anotar(new Uri("https://example.com"));
        Assert.False(GroupInviteLinks.Hay);

        // Dice que no: no entra, y no vuelve a preguntar.
        GroupInviteLinks.Anotar(GroupLink.For(invite).ToString());
        Assert.True(GroupInviteLinks.Hay);
        var (page, sync) = await OpenAsync(app);
        Assert.Equal(app.Texts.Format("JoinFromLinkMessage", "XYZ789"), app.Ui.Dialogs.Single().Message);
        Assert.Empty(sync.Calls);
        Assert.False(GroupInviteLinks.Hay);
        await page.Appear();
        Assert.Single(app.Ui.Dialogs);

        // Dice que si: entra.
        GroupInviteLinks.Anotar(GroupLink.For(invite));
        app.Ui.Answer(true);
        var (_, sync2) = await OpenAsync(app);
        Assert.Equal(["join:XYZ789:secreto", "pull"], sync2.Calls);
    });

    private static BarcodeDetectionEventArgs Read(string text) => new([new BarcodeResult { Value = text }]);

    private static T Blank<T>() => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public void Leer_el_QR_con_la_camara_y_entrar() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (page, sync) = await OpenAsync(app);
        var invite = new GroupInvite("QRQR01", "clave-qr");

        // La camara ve un QR que no es nuestro (se dice y sigue mirando) y luego el bueno. Esto
        // corre dentro del manejador del boton, que sigue esperando: por eso Invoke y Until.
        app.Ui.OnPush = async pushed =>
        {
            var scan = Assert.IsType<ScanQrPage>(pushed);
            var camara = scan.Named<CameraBarcodeReaderView>("Camara");
            Assert.Equal(BarcodeFormat.QrCode, camara.Options.Formats);

            await scan.Invoke("OnBarcodesDetected", null, new BarcodeDetectionEventArgs([]));
            await scan.Invoke("OnBarcodesDetected", null, Read("WIFI:S:casa;;"));
            await PageDriver.Until(() => app.Ui.Dialogs.Count == 1 && camara.IsDetecting);
            Assert.Equal(app.Texts["ScanTitle"], app.Ui.Dialogs[0].Title);
            Assert.EndsWith("WIFI:S:casa;;", app.Ui.Dialogs[0].Message);

            await scan.Invoke("OnBarcodesDetected", null, Read(GroupLink.For(invite).ToString()));

            // Un segundo aviso con el codigo delante no hace nada: ya se entrego.
            await scan.Invoke("OnBarcodesDetected", null, Read("otro"));
            await PageDriver.Until(() => app.Ui.Pops == 1);
            Assert.False(camara.IsDetecting);
        };

        app.Ui.Answer(null, true);   // el aviso del QR ajeno; despues, «entrar»
        await page.Handler("OnScanQrClicked");

        Assert.Equal(["join:QRQR01:clave-qr", "pull"], sync.Calls);
        Assert.Equal(1, app.Ui.Pops);
    });

    [Fact]
    public void Leer_el_QR_y_decir_que_no_o_cerrar_la_camara() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (page, sync) = await OpenAsync(app);

        // Lee uno bueno pero dice que no.
        app.Ui.OnPush = async pushed =>
        {
            await pushed.Invoke("OnBarcodesDetected", null, Read(GroupLink.For(new GroupInvite("AAA111", "k")).ToString()));
            await PageDriver.Until(() => app.Ui.Pops == 1);
        };
        await page.Handler("OnScanQrClicked");
        Assert.Empty(sync.Calls);
        Assert.Single(app.Ui.Dialogs);

        // Cierra la camara sin leer nada: se responde al salir y no pasa nada mas.
        app.Ui.OnPush = async pushed =>
        {
            await pushed.Invoke("OnCloseClicked", null, EventArgs.Empty);
            await PageDriver.Until(() => app.Ui.Pops == 2);
            await pushed.Invoke("OnNavigatedFrom", Blank<NavigatedFromEventArgs>());
        };
        await page.Handler("OnScanQrClicked");
        Assert.Empty(sync.Calls);
        Assert.Single(app.Ui.Dialogs);
    });

    [Fact]
    public void Sin_camara_se_dice_y_un_fallo_tambien() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var (page, _) = await OpenAsync(app);

        app.Ui.CameraAllowed = false;
        await page.Handler("OnScanQrClicked");
        Assert.Equal(app.Texts["ScanNoCamera"], app.Ui.Dialogs.Single().Message);
        Assert.Empty(app.Ui.Pushed);

        app.Ui.CameraAllowed = true;
        app.Ui.OnPush = _ => throw new InvalidOperationException("camara ocupada");
        await page.Handler("OnScanQrClicked");
        Assert.Equal("camara ocupada", app.Ui.Dialogs.Last().Message);
    });

    [Fact]
    public void La_camara_sin_imagen_lo_dice_y_con_imagen_no() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        ScanQrPage.Paciencia = TimeSpan.FromMilliseconds(5);
        try
        {
            var scan = (ScanQrPage)Activator.CreateInstance(typeof(ScanQrPage), nonPublic: true)!;
            await scan.Appear();
            await PageDriver.Until(() => scan.Named<Label>("HintLabel").Text == app.Texts["ScanNoFrames"]);

            // Con imagen llegando, el aviso no sale.
            scan.Named<Label>("HintLabel").Text = "mirando";
            await scan.Named<CameraBarcodeReaderView>("Camara").Raise("FrameReady", Blank<CameraFrameBufferEventArgs>());
            await scan.Appear();
            await Task.Delay(40);
            await UiThread.IdleAsync();
            Assert.Equal("mirando", scan.Named<Label>("HintLabel").Text);
        }
        finally
        {
            ScanQrPage.Paciencia = TimeSpan.FromSeconds(6);
        }
    });

    [Fact]
    public void Nueva_lista_del_grupo() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var group = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "FAM001" });
        var (page, _) = await OpenAsync(app);

        await page.Handler("OnNewListClicked", new Button());
        await page.Handler("OnNewListClicked", PageDriver.RowButton(group.Id));
        Assert.Empty(await app.Repository.GetGroupListsAsync(group.Id));

        app.Ui.Answer("Compras");
        await page.Handler("OnNewListClicked", PageDriver.RowButton(group.Id));
        Assert.Equal("Compras", (await app.Repository.GetGroupListsAsync(group.Id)).Single().Name);
        Assert.Single(Rows(page).Single().Lists);
    });

    [Fact]
    public void Salir_de_un_grupo_o_borrarlo_para_todos() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var group = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Familia", JoinCode = "FAM001" });
        var (page, sync) = await OpenAsync(app);
        var button = PageDriver.RowButton(group.Id);
        var leave = app.Texts["LeaveGroupOption"];
        var delete = app.Texts["DeleteGroupOption"];

        await page.Handler("OnDeleteGroupClicked", new Button());
        await page.Handler("OnDeleteGroupClicked", PageDriver.RowButton(Guid.NewGuid()));
        Assert.Empty(app.Ui.Dialogs);

        // Cancelar la pregunta, o la confirmacion de salir.
        await page.Handler("OnDeleteGroupClicked", button);
        app.Ui.Answer(leave, false);
        await page.Handler("OnDeleteGroupClicked", button);

        // Borrar sin ser el dueño: no se puede.
        sync.Owner = false;
        app.Ui.Answer(delete);
        await page.Handler("OnDeleteGroupClicked", button);
        Assert.Equal(app.Texts["GroupNotOwner"], app.Ui.Dialogs.Last().Message);

        // Dueño pero cancela la confirmacion.
        sync.Owner = true;
        app.Ui.Answer(delete, false);
        await page.Handler("OnDeleteGroupClicked", button);
        Assert.Empty(sync.Calls.Where(c => c is "leave" or "delete"));
        Assert.False((await app.Repository.GetGroupAsync(group.Id))!.Deleted);

        // Sin red no se sale: quitarlo solo de aqui lo traeria de vuelta.
        sync.Fails = new HttpRequestException("sin red");
        app.Ui.Answer(leave, true);
        await page.Handler("OnDeleteGroupClicked", button);
        Assert.Equal($"{app.Texts["GroupActionFailed"]}\nsin red", app.Ui.Dialogs.Last().Message);
        Assert.False((await app.Repository.GetGroupAsync(group.Id))!.Deleted);

        // Salir.
        sync.Fails = null;
        app.Ui.Answer(leave, true);
        await page.Handler("OnDeleteGroupClicked", button);
        Assert.Contains("leave", sync.Calls);
        Assert.True((await app.Repository.GetGroupAsync(group.Id))!.Deleted);

        // Borrar para todos (sin saber si es el dueño: se intenta y manda el servidor).
        var other = await app.Repository.SaveGroupAsync(new TaskGroup { Name = "Piso", JoinCode = "PIS001" });
        sync.Owner = null;
        app.Ui.Answer(delete, true);
        await page.Handler("OnDeleteGroupClicked", PageDriver.RowButton(other.Id));
        Assert.Contains("delete", sync.Calls);
        Assert.Empty(Rows(page));
    });

    [Fact]
    public void La_pagina_del_QR_comparte_copia_y_cierra() => UiThread.Run(async () =>
    {
        await using var app = await TestApp.StartAsync();
        var invite = new GroupInvite("ABC123", "clave");
        var page = new GroupQrPage("Familia", invite);

        await page.Handler("OnShareClicked");
        var shared = app.Ui.Shared.Single();
        Assert.Equal(GroupLink.Message(app.Texts, "Familia", invite), shared.Text);
        Assert.Equal(app.Texts["GroupInviteSubject"], shared.Subject);
        Assert.Equal(app.Texts["ShareTitle"], shared.Title);

        await page.Handler("OnCopyClicked");
        Assert.Equal(shared.Text, app.Ui.Clipboard.Single());
        Assert.Equal(app.Texts["GroupInviteSaved"], app.Ui.Dialogs.Single().Message);

        await page.Handler("OnCloseClicked");
        Assert.Equal(1, app.Ui.Pops);
    });
}
