using TaskManager.Core.Data;

namespace TaskManager.Core.Services;

/// <summary>Una tarea que ha llegado de otro dispositivo del mismo usuario.</summary>
public sealed record ArrivedTask(Guid Id, string Title);

/// <summary>
/// Quien decide <b>cuando</b> se sincroniza. El <see cref="ISyncService"/> sabe hablar con el
/// servidor; esto sabe en que momentos hay que hacerlo.
/// </summary>
/// <remarks>
/// <para><b>Por que existe.</b> Antes la sincronizacion se lanzaba una sola vez, al arrancar
/// Windows, y en Android no se lanzaba nunca: por eso el mismo usuario veia listas distintas en
/// cada aparato aunque el servidor estuviera bien configurado. Los momentos en que hay que
/// sincronizar son los mismos en las dos plataformas, asi que la regla vive una sola vez, aqui.</para>
///
/// <para><b>Cuando sincroniza.</b> Al entrar (que es el primer instante en que hay con que hablar
/// con el servidor), al volver del segundo plano, poco despues de cada cambio local —para que una
/// tarea nueva llegue al otro aparato sin esperar— y cada pocos minutos mientras la aplicacion este
/// delante, que es lo que la mantiene al dia sin que nadie toque nada.</para>
///
/// <para><b>El retardo tras un cambio no es pereza.</b> Escribir una tarea a mano genera varios
/// cambios seguidos —titulo, lista, fecha—; subir en cada uno seria una peticion por tecla. Se
/// espera un momento a que la mano pare, y entonces sube todo junto.</para>
///
/// <para><b>Lo que NO hace, a proposito.</b> No despierta a la aplicacion cerrada. Haria falta una
/// notificacion enviada desde un servidor (FCM) con su proyecto de Firebase, y <b>se decidio no
/// hacerlo</b> (2026-09-01): no compensa montar esa pieza para adelantar unos minutos un aviso. Lo
/// que llega de otro dispositivo se ve al abrir, al volver a la aplicacion o al pulsar refrescar,
/// y entonces si se avisa.</para>
/// </remarks>
public sealed class SyncCoordinator : IDisposable
{
    /// <summary>Lo que se espera tras un cambio local antes de subir. Suficiente para agrupar.</summary>
    private static readonly TimeSpan AfterChangeDelay = TimeSpan.FromSeconds(4);

    /// <summary>Ronda de fondo con la aplicacion delante. Ni tan corta que moleste ni tan larga que se note.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    /// <summary>Lo que espera el boton de refrescar a que termine la sincronizacion que hubiera.</summary>
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(30);

    /// <summary>De quien es lo que ya se subio entero. Vacio = todavia de nadie.</summary>
    private const string KeyBackfilledFor = "sync.backfilled_for";

    /// <summary>De quien es lo que ya se ha vuelto a subir <b>cifrado</b>.</summary>
    private const string KeyEncryptedFor = "sync.encrypted_v1_for";

    private readonly ISyncService _sync;
    private readonly SupabaseAuthService _auth;
    private readonly TaskRepository _repository;
    private readonly SettingsService _settings;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Ha llegado otra peticion mientras se sincronizaba. Se atiende al terminar.</summary>
    private volatile bool _again;
    private CancellationTokenSource? _pending;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>Ids vistos en esta sesion, para no avisar dos veces de la misma tarea.</summary>
    private readonly HashSet<Guid> _announced = [];

    public SyncCoordinator(ISyncService sync, SupabaseAuthService auth, TaskRepository repository,
        SettingsService settings)
    {
        _sync = sync;
        _auth = auth;
        _repository = repository;
        _settings = settings;

        _auth.UserChanged += OnUserChanged;
        _sync.RemoteChanged += OnRemoteChanged;
        _repository.LocalChangeQueued += OnLocalChange;
    }

    /// <summary>
    /// Una tarea nueva de otro dispositivo del mismo usuario. Cada plataforma lo convierte en el
    /// aviso que sabe dar: un globo de bandeja en Windows, una notificacion en Android.
    /// </summary>
    public event EventHandler<ArrivedTask>? TaskArrived;

    /// <summary>Ha cambiado algo por debajo: la pantalla que este puesta deberia releer.</summary>
    public event EventHandler? Changed;

    /// <summary>Arranca la ronda de fondo y sincroniza ya. Se llama al abrir y al volver.</summary>
    public void Start()
    {
        _timer ??= new Timer(_ => _ = SyncNowAsync(), null, Interval, Interval);
        _ = SyncNowAsync();
    }

    /// <summary>Deja de rondar. Se llama al irse al segundo plano: ahi no hay nada que refrescar.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Algo ha cambiado aqui. Programa una subida en breve, y si ya habia una programada la
    /// sustituye: lo que cuenta es el ultimo cambio, no el primero.
    /// </summary>
    public void NotifyLocalChange()
    {
        if (_disposed || !_sync.IsConfigured)
        {
            return;
        }

        _pending?.Cancel();
        _pending?.Dispose();

        var cts = new CancellationTokenSource();
        _pending = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AfterChangeDelay, cts.Token).ConfigureAwait(false);
                await SyncNowAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ha llegado otro cambio antes de tiempo: sube aquel, no este.
            }
        });
    }

    /// <summary>
    /// Sube lo pendiente y baja lo nuevo. <b>Se traga los fallos</b>: quedarse sin cobertura no es
    /// un error que haya que enseñarle a nadie, y la cola de salida no se pierde.
    /// </summary>
    /// <remarks>
    /// <para><b>Una a la vez, pero no se pierde ninguna.</b> Si llega una peticion mientras hay
    /// otra en marcha, se apunta y se atiende al terminar, en vez de descartarla.</para>
    ///
    /// <para>Descartarla causaba un fallo muy feo al entrar por primera vez en un aparato nuevo:
    /// al entrar saltan <b>dos</b> avisos de usuario seguidos —uno con la identidad y otro cuando
    /// llega la sesion del servidor—, y el primero arranca una sincronizacion que todavia no tiene
    /// token y no puede bajar nada. La segunda, que sí lo tenia, se caia por el camino, y las
    /// tareas no aparecian hasta la ronda de tres minutos. Daba toda la impresion de que la cuenta
    /// no traia nada.</para>
    /// </remarks>
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_sync.IsConfigured || !_auth.IsSignedIn)
        {
            return;
        }

        // Una sola sincronizacion a la vez: dos a la vez subirian la misma cola dos veces.
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _again = true;
            return;
        }

        await RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sincronizacion <b>pedida por el usuario</b> (el boton de refrescar). Espera su turno.
    /// </summary>
    /// <remarks>
    /// <para>La diferencia con <see cref="SyncNowAsync"/> es lo que pasa cuando ya hay una en
    /// marcha. Ahi se apunta «hay que repetir» y se vuelve <b>en el acto</b>, que esta bien para una
    /// ronda automatica —da igual quien la haga— pero es justo lo contrario de lo que hace falta
    /// aqui: la pantalla repintaba los mismos datos de antes y parecia que el boton no hacia nada.
    /// Y era verdad que no hacia nada.</para>
    ///
    /// <para>Ahora espera a que termine la que hay (con un tope, para no dejar el boton colgado si
    /// algo se atasca) y entonces hace la suya. Al volver, lo que se pinte ya es lo nuevo.</para>
    /// </remarks>
    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_sync.IsConfigured || !_auth.IsSignedIn)
        {
            return;
        }

        if (!await _gate.WaitAsync(GateWait, cancellationToken).ConfigureAwait(false))
        {
            // La de antes sigue ahi. Se apunta para que se repita al acabar, que es lo unico que
            // queda por hacer sin dejar la pantalla esperando mas.
            _again = true;
            return;
        }

        await RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lo que hace una sincronizacion, con el turno ya tomado.</summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            do
            {
                _again = false;

                try
                {
                    await BackfillOnceAsync().ConfigureAwait(false);
                    await EncryptOnceAsync().ConfigureAwait(false);
                    await _sync.StartAsync(cancellationToken).ConfigureAwait(false);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Sync: {ex.Message}");
                }
            }
            while (_again && !_disposed);
        }
        finally
        {
            _gate.Release();
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// La primera vez que este usuario consigue hablar con el servidor, sube <b>todo lo que ya
    /// tenia aqui</b>. Sin esto, entrar con cuenta solo compartiria lo que se escriba a partir de
    /// ese momento y las tareas de antes se quedarian encerradas en este aparato.
    /// </summary>
    /// <remarks>
    /// Se apunta contra el usuario, no como un simple «ya esta»: si se entra con otra cuenta, lo de
    /// aqui tiene que volver a subir, ahora a nombre de esa.
    /// </remarks>
    private async Task BackfillOnceAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null || user.RemoteId.Length == 0)
        {
            return;   // Sin sesion en el servidor no hay a donde subir: ya se hara.
        }

        if (_settings.Get(KeyBackfilledFor) == user.Id)
        {
            return;
        }

        var queued = await _repository.QueueEverythingAsync().ConfigureAwait(false);
        await _settings.SetAsync(KeyBackfilledFor, user.Id).ConfigureAwait(false);

        System.Diagnostics.Debug.WriteLine($"Sync: {queued} filas locales encoladas para subir.");
    }

    /// <summary>
    /// Vuelve a subir todo <b>una vez</b>, ya cifrado, para que lo que quedo guardado en claro deje
    /// de estarlo.
    /// </summary>
    /// <remarks>
    /// <para>El cifrado nuevo solo alcanza a lo que se sube a partir de ahora; lo que ya estaba
    /// arriba se quedaria en claro para siempre, que es justo lo que no se queria. Como el
    /// <c>upsert</c> va por identificador, volver a subir cada fila sobrescribe el texto plano por
    /// el cifrado sin duplicar nada.</para>
    ///
    /// <para>No mueve <c>updated_at</c>: se sube tal cual estaba, asi que el otro dispositivo se
    /// baja las filas, ve que lo suyo es igual de nuevo y no cambia nada. La reescritura no se nota
    /// en ninguna pantalla.</para>
    ///
    /// <para>Se apunta contra el usuario, como el volcado inicial: entrar con otra cuenta vuelve a
    /// tener su propio texto arriba que cifrar.</para>
    /// </remarks>
    private async Task EncryptOnceAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null || user.RemoteId.Length == 0)
        {
            return;
        }

        if (_settings.Get(KeyEncryptedFor) == user.Id)
        {
            return;
        }

        if (!TextCipher.IsAvailable)
        {
            // Sin cifrado en esta plataforma no se marca nada: si algun dia lo hay, se hara
            // entonces en vez de dar por reescrito lo que se subio en claro.
            return;
        }

        var queued = await _repository.QueueEverythingAsync().ConfigureAwait(false);
        await _settings.SetAsync(KeyEncryptedFor, user.Id).ConfigureAwait(false);

        System.Diagnostics.Debug.WriteLine($"Sync: {queued} filas encoladas para volver a subir cifradas.");
    }

    private void OnLocalChange(object? sender, EventArgs e) => NotifyLocalChange();

    /// <summary>
    /// Entrar dispara la primera sincronizacion. Salta dos veces —al saberse quien es y al
    /// conseguir la sesion del servidor— y las dos cuentan: la segunda es la que de verdad puede
    /// bajar algo, y gracias al reintento de <see cref="SyncNowAsync"/> ya no se pierde aunque
    /// pille a la primera todavia en marcha.
    /// </summary>
    private void OnUserChanged(object? sender, AuthUser? user)
    {
        if (user is not null)
        {
            _ = SyncNowAsync();
        }
    }

    /// <summary>
    /// Avisa solo de lo que de verdad es <b>una tarea nueva de otro</b>: ni las ya hechas, ni las
    /// borradas, ni las que escribio este mismo aparato —que ya se vieron al escribirlas—, ni las
    /// que solo se han <b>modificado</b>.
    /// </summary>
    /// <remarks>
    /// Lo de las modificadas faltaba y se notaba: cambiarle el titulo a una tarea desde el movil
    /// sacaba un globo en Windows anunciandola como si acabara de nacer. Un cambio en algo que ya
    /// tienes delante no es una noticia; la pantalla se refresca y punto.
    /// </remarks>
    private async void OnRemoteChanged(object? sender, RemoteChange change)
    {
        // El repintado va siempre: modificada tambien hay que volver a pintarla.
        Changed?.Invoke(this, EventArgs.Empty);

        if (!change.IsNew || change.Entity != "tasks" || !Guid.TryParse(change.EntityId, out var id))
        {
            return;
        }

        try
        {
            var task = await _repository.GetTaskAsync(id).ConfigureAwait(false);
            if (task is null || task.Deleted || task.IsDone)
            {
                return;
            }

            lock (_announced)
            {
                if (!_announced.Add(id))
                {
                    return;
                }
            }

            TaskArrived?.Invoke(this, new ArrivedTask(id, task.Title));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Aviso de tarea remota: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _auth.UserChanged -= OnUserChanged;
        _sync.RemoteChanged -= OnRemoteChanged;
        _repository.LocalChangeQueued -= OnLocalChange;

        _pending?.Cancel();
        _pending?.Dispose();
        _timer?.Dispose();
        _gate.Dispose();
    }
}
