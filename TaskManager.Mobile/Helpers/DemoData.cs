#if DEMO
using TaskManager.Core.Data;
using TaskManager.Core.Services;

namespace TaskManager.Mobile.Helpers;

/// <summary>
/// Las tareas inventadas de la compilación de demostración, la que sirve para hacer las capturas de
/// las tiendas.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Las capturas de una ficha se ven en público, y la aplicación sin
/// cuenta no se puede ni abrir: capturarla de verdad significa enseñar las tareas de quien la
/// usa —su trabajo, sus recados, sus fechas—, que no tienen por qué acabar en una tienda. Con esto
/// se ve exactamente la misma aplicación, con datos que no son de nadie.</para>
///
/// <para><b>Solo existe en la compilación de demostración</b> (<c>-p:Demo=true</c>), que además
/// escribe en otra base de datos: la de verdad no se toca, ni siquiera si se instala encima. Fuera
/// de esa compilación este fichero no llega ni a compilar.</para>
///
/// <para>Las tareas están escritas para que la pantalla cuente lo que hace la aplicación: hay
/// ancladas, con etiqueta, con fecha, con pasos y hechas, y las fechas son relativas al día en que
/// se ejecuta para que las capturas no envejezcan.</para>
/// </remarks>
internal static class DemoData
{
    /// <summary>La cuenta de mentira con la que se marcan estas filas.</summary>
    private const string Cuenta = "demo";

    public static async Task SeedAsync(TaskRepository repository, SettingsService settings)
    {
        // El repositorio filtra por la cuenta que este dentro, asi que primero hay una.
        await settings.SetAsync(SettingsService.KeyGoogleSub, Cuenta);
        await settings.SetAsync(SettingsService.KeyAccountEmail, "demo@socratic.app");
        await settings.SetAsync(SettingsService.KeyAuthProvider, "demo");

        // El idioma se fija aqui y no a mano en cada captura: las dos fichas de la tienda —es-ES y
        // en-US— necesitan la aplicacion en su idioma, y asi basta con volver a compilar con
        // -p:DemoLang=en para tener el otro juego.
#if DEMO_EN
        await settings.SetAsync(SettingsService.KeyLanguage, "en");
#else
        await settings.SetAsync(SettingsService.KeyLanguage, "es");
#endif

        if ((await repository.GetPrivateListsAsync()).Count > 0)
        {
            return;   // Ya sembrado: no se duplica en cada arranque.
        }

        var hoy = DateTime.Today;

#if DEMO_EN
        var casa = await repository.CreateListAsync("Home");
        var trabajo = await repository.CreateListAsync("Work");
        var viaje = await repository.CreateListAsync("Trip to Lisbon");

        await Tarea(repository, casa.Id, "Buy bread and fruit", tags: "home,shopping",
            due: hoy, pinned: true, myDay: true);

        await Tarea(repository, casa.Id, "Change the air filter", tags: "home",
            due: hoy.AddDays(2), recurrence: "monthly:12");

        await Tarea(repository, trabajo.Id, "Get ready for Monday's meeting", tags: "work",
            due: hoy.AddDays(3), planned: hoy.AddDays(1), pinned: true, myDay: true,
            notes: "Bring the quarter's figures and the delivery calendar.",
            steps: ["Go over the figures", "Write the outline", "Book the room"]);

        await Tarea(repository, trabajo.Id, "Reply to Marta's email", tags: "work",
            due: hoy.AddDays(-1), myDay: true);

        await Tarea(repository, trabajo.Id, "Send September's invoice", tags: "work,invoices",
            due: hoy.AddDays(7), recurrence: "monthly:1");

        await Tarea(repository, viaje.Id, "Book the train", tags: "trip",
            due: hoy.AddDays(10),
            steps: ["Check the timetables", "Compare prices", "Buy both ways"]);

        await Tarea(repository, viaje.Id, "Book the hotel", tags: "trip", due: hoy.AddDays(12));

        await Tarea(repository, viaje.Id, "Renew the passport", tags: "trip", done: true);

        await Tarea(repository, casa.Id, "Water the plants", tags: "home",
            recurrence: "weekly:1,4", myDay: true);

        await Tarea(repository, casa.Id, "Call the plumber", tags: "home", done: true);
#else
        var casa = await repository.CreateListAsync("Casa");
        var trabajo = await repository.CreateListAsync("Trabajo");
        var viaje = await repository.CreateListAsync("Viaje a Lisboa");

        await Tarea(repository, casa.Id, "Comprar pan y fruta", tags: "casa,compra",
            due: hoy, pinned: true, myDay: true);

        await Tarea(repository, casa.Id, "Cambiar el filtro del aire", tags: "casa",
            due: hoy.AddDays(2), recurrence: "monthly:12");

        await Tarea(repository, trabajo.Id, "Preparar la reunión del lunes", tags: "trabajo",
            due: hoy.AddDays(3), planned: hoy.AddDays(1), pinned: true, myDay: true,
            notes: "Llevar los números del trimestre y el calendario de entregas.",
            steps: ["Repasar las cifras", "Escribir el guion", "Reservar la sala"]);

        await Tarea(repository, trabajo.Id, "Contestar el correo de Marta", tags: "trabajo",
            due: hoy.AddDays(-1), myDay: true);

        await Tarea(repository, trabajo.Id, "Enviar la factura de septiembre", tags: "trabajo,facturas",
            due: hoy.AddDays(7), recurrence: "monthly:1");

        await Tarea(repository, viaje.Id, "Sacar los billetes", tags: "viaje",
            due: hoy.AddDays(10),
            steps: ["Mirar horarios", "Comparar precios", "Comprar la ida y la vuelta"]);

        await Tarea(repository, viaje.Id, "Reservar el hotel", tags: "viaje", due: hoy.AddDays(12));

        await Tarea(repository, viaje.Id, "Renovar el pasaporte", tags: "viaje", done: true);

        await Tarea(repository, casa.Id, "Regar las plantas", tags: "casa",
            recurrence: "weekly:1,4", myDay: true);

        await Tarea(repository, casa.Id, "Llamar al fontanero", tags: "casa", done: true);
#endif
    }

    private static async Task Tarea(
        TaskRepository repository,
        Guid listId,
        string title,
        string tags = "",
        string notes = "",
        DateTime? due = null,
        DateTime? planned = null,
        bool pinned = false,
        bool done = false,
        bool myDay = false,
        string recurrence = "",
        IReadOnlyList<string>? steps = null)
    {
        var tarea = await repository.AddTaskAsync(listId, title, myDay);

        tarea.Tags = tags;
        tarea.Notes = notes;
        tarea.DueAt = due;
        tarea.PlannedFor = planned;
        tarea.IsPinned = pinned;
        tarea.IsDone = done;
        tarea.DoneAt = done ? DateTime.UtcNow : null;
        tarea.RecurrenceRule = recurrence;

        await repository.UpdateTaskAsync(tarea);

        if (steps is { Count: > 0 })
        {
            await repository.AddStepsAsync(tarea.Id, steps);
        }
    }
}
#endif
