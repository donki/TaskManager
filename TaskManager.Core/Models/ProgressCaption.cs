using TaskManager.Core.Services;

namespace TaskManager.Core.Models;

/// <summary>
/// El pie de las listas: cuantas se ven, cuantas quedan y cuanto se lleva hecho.
/// </summary>
/// <remarks>
/// <para>Vive en el nucleo, y no en cada aplicacion, por la misma razon que los filtros: el pie
/// dice lo mismo en Windows y en Android, y escrito dos veces acaba contando dos cosas distintas.</para>
///
/// <para><b>El total son las que quedan, no todas.</b> Antes se comparaba con el total de la
/// cuenta, y con unos meses de uso ese numero es sobre todo el archivo: «se muestran 12 de 480» no
/// dice lo que el filtro esconde, dice cuanto se ha terminado desde que se instalo. Contra lo
/// pendiente, el numero vuelve a significar algo. Y lo hecho no se pierde: va detras, en
/// porcentaje, que es donde se ve el avance sin que estorbe.</para>
/// </remarks>
public static class ProgressCaption
{
    /// <param name="shown">
    /// Las que se estan enseñando <b>sin hacer</b>, no las filas que hay pintadas.
    /// </param>
    /// <remarks>
    /// Los dos numeros tienen que ser de lo mismo. Contando filas, con el filtro en «todas» salia
    /// «se muestran 113 de 46 pendientes»: cierto por separado y absurdo junto. Asi, «46 de 46»
    /// dice lo que se pregunta —de lo que queda, cuanto estoy viendo— y con el filtro de acabadas
    /// dice «0 de 46», que tambien es la verdad.
    /// </remarks>
    /// <param name="pending">Las que quedan por hacer, sin filtrar.</param>
    /// <param name="done">Las que ya se hicieron, sin filtrar.</param>
    public static string Footer(int shown, int pending, int done, LocalizationService texts)
    {
        var total = pending + done;

        // Sin una sola tarea no hay avance del que hablar, y «0 %» en una lista recien creada solo
        // sirve para dar la bienvenida con un cero.
        if (total == 0)
        {
            return texts.Format("ShowingOfPendingNone", shown, pending);
        }

        var percent = (int)Math.Round(done * 100.0 / total, MidpointRounding.AwayFromZero);

        return texts.Format("ShowingOfPending", shown, pending, percent);
    }

    /// <summary>Lo mismo, tomando los dos numeros como los devuelve el repositorio.</summary>
    public static string Footer(int shown, (int Pending, int Done) counts, LocalizationService texts) =>
        Footer(shown, counts.Pending, counts.Done, texts);
}
