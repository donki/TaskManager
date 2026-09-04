# Changelog — Task Manager

Formato de versión `AAAA.MM.DD.N` (constitución Mobile 3).

## 2026.09.04 — Entrar con Microsoft, y cada cuenta con sus listas

Windows `2026.9.4.3` · Android `2026.09.04.3`

- **La entrada con Microsoft ya se ofrece.** El flujo estaba escrito desde el 2026-08-31 pero oculto
  (`AuthOptions.MicrosoftSignInEnabled`). Lo que faltaba no era el flujo: era lo de debajo.
- **Cada cuenta tiene sus listas en el mismo aparato.** La base local era de un solo usuario, así que
  entrar con la segunda cuenta enseñaba las tareas de la primera mezcladas con las que bajaban de su
  servidor. Ahora cada lista, cada tarea y cada grupo llevan escrito de quién son
  (`AccountId`), y el repositorio filtra por la cuenta que está dentro: si se olvidara el filtro en
  una pantalla se verían las tareas de la otra sin que nada chirriara, así que no lo decide ninguna
  pantalla.
- **Se cambia de cuenta desde los ajustes**, con un botón por proveedor, en Windows y en Android.
  Cambiar no borra ni mueve nada: volver a la anterior lo devuelve todo donde estaba.
- **Nada de una cuenta sube a nombre de la otra.** La sesión del servidor se tira antes de poner la
  identidad nueva —un canje fallido dejaba el token de la cuenta anterior junto al usuario nuevo— y
  la cola de subida es por cuenta: lo escrito sin cobertura con una espera a que vuelva la suya en
  vez de subirse a la que entre después. El corte de la última bajada también es de cada cuenta.
- **Al actualizar no se pierde nada**: lo que ya había no es de ninguna cuenta todavía y se lo queda
  la que esté dentro, con su autoría, su XP y sus rachas.
- **Actualizar ahora se ve.** El botón habla con el servidor y espera a que termine, y hasta ahora
  no cambiaba nada en pantalla: con la red lenta parecía que no hacía nada y se pulsaba otra vez.
  En Android sale una pastilla flotante con la rueda («Actualizando…») y en Windows gira el propio
  icono. Tirar hacia abajo ya tenía rueda, pero se quedaba girando para siempre si fallaba la red.
- **Se recuerda el filtro.** «Mis tareas» vuelve a abrirse con el filtro y la etiqueta que se
  dejaron puestos, en Windows y en Android; el panel rápido guarda su etiqueta aparte. El buscador
  no se guarda: reabrir con la búsqueda de ayer parece que se han perdido tareas.
- **Volver a la aplicación sin terminar en el navegador ya cancela la entrada.** Cuando el proveedor
  rechaza la petición enseña *su* página de error y no redirige nunca a la loopback, así que la
  espera no terminaba jamás: en Android la rueda se quedaba girando para siempre y solo se salía
  matando la aplicación. Ahora se corta al volver (y hay un tope de tres minutos, como en Windows).
- **El registro de Entra se rehízo** (`tools\Registrar-Entra.ps1`). El que había no existía en
  ningún directorio —de ahí el `unauthorized_client: The client does not exist`— y el script lo
  creaba con dos defectos: solo cuentas de organización, y `http://localhost` como redirección
  cuando la aplicación vuelve a `http://127.0.0.1:<puerto>/auth/`. Ahora nace admitiendo **cuentas
  personales y de empresa** y con la ruta correcta.

## 2026.08.31 — Entrada obligatoria, sincronización de verdad y una sola interfaz

Windows `2026.8.31.3` · Android `2026.08.31.6`

### Entrada

- **La entrada es obligatoria** y se hace **directamente con el proveedor**, no a través de Supabase.
  Antes se abría `/auth/v1/authorize?provider=google` y, con el proveedor sin dar de alta en el
  proyecto, el navegador se plantaba en una página de Supabase con
  `Unsupported provider: provider is not enabled`. La entrada no puede depender de un ajuste del
  servidor.
- **Google y Microsoft**, los dos con PKCE. La identidad es el `sub` de Google o el `oid` de
  Microsoft: el mismo identificador en Windows y en Android.
- **El nombre de la cuenta es el nombre de la aplicación.**
- Pantalla de entrada en Windows, que hasta ahora no tenía ninguna: la cuenta se pedía escondida en
  los ajustes.
- **Android:** el cliente OAuth de tipo Android rechazaba la petición con `Error 400:
  invalid_request` (valida paquete y huella SHA-1). Se usa el mismo cliente de escritorio que
  Windows, recogiendo la vuelta en un servidor local dentro de la aplicación.
- La sesión guardada vale desde el primer momento, sin esperar a la red: un equipo sin conexión no
  puede dejar al usuario fuera de sus propias tareas.

### Sincronización

- **En Android no se sincronizaba nunca**: el servicio estaba registrado y nadie lo llamaba. Ahora
  hay un coordinador único (`SyncCoordinator`) que decide cuándo: al entrar, al volver del segundo
  plano, unos segundos después de cada cambio y cada pocos minutos.
- **Lo que ya había en local sube.** La cola solo recogía cambios a partir de su creación, así que
  lo escrito antes se quedaba encerrado en el aparato donde se escribió.
- Tres fallos que se comían la sincronización en silencio, y que ahora se registran:
  - `PGRST102: All object keys must match` — se omitían los campos nulos, así que dos tareas con
    distinto relleno viajaban con distinta forma. **Solo se veía con más de una tarea.**
  - `PGRST204` por la columna `context`, que el cliente mandaba y el servidor no tenía.
  - La bajada preguntaba por `updated_at` (cuándo lo tocó el usuario) en vez de por cuándo llegó al
    servidor: lo que un dispositivo subía por primera vez caía por detrás del corte del otro y no se
    veía nunca. Nueva columna `synced_at` (`supabase/04_synced_at.sql`).
- Aviso en el otro dispositivo cuando llega una tarea nueva de la misma cuenta.

### Interfaz

- **Windows y Android enseñan lo mismo y se parecen.** Windows tenía otro sistema visual y ni
  siquiera una pantalla de detalle: se podía crear una tarea y marcarla hecha, y nada más.
  - **Mis tareas**: todas las tareas con los mismos ocho filtros en las dos (pendientes, acabadas,
    todas, caducadas, y por fecha de inicio y de caducidad antes / desde hoy). El criterio se define
    una sola vez, en `TaskFilters`.
  - **Detalle de tarea en Windows** con los mismos campos que en Android, y **pasos que se pueden
    añadir a mano** en las dos.
  - Windows adopta la paleta y los estilos del móvil (tarjetas, pastillas, botones de icono).
- Fuera **Mi Día**, **los grupos** y **el gremio**; el **correo** queda oculto
  (`FeatureOptions.MailEnabled`) y **Azure DevOps** se ha quitado del todo.
- «Mis listas privadas» pasa a llamarse **«Mis listas»**.
- Se quita el **contexto** de las tareas: eran dos cajas de texto libre pidiendo casi lo mismo. El
  desglose parte ahora de las notas.

## 2026.08.29.2 — Entrada con Google

- **Cuenta de usuario con Google** a través de Supabase Auth, con PKCE, en las dos aplicaciones:
  Chrome Custom Tabs en Android y navegador del sistema contra `127.0.0.1` en Windows.
- Tokens en el almacén seguro de Android y cifrados con DPAPI en Windows; renovación automática con
  el token de refresco.
- Tabla `profiles` con disparador `handle_new_user`: el nombre y la foto de Google quedan guardados
  y visibles para los compañeros de grupo, con RLS que solo deja verlos a ellos.
- Pantalla de entrada en Android (hace de puerta al arrancar y se aparta sola si ya hay sesión o si
  se eligió seguir sin cuenta) y sección *Tu cuenta* en los ajustes de las dos aplicaciones.
- Al entrar por primera vez, las tareas, la autoría y el XP conseguidos sin cuenta pasan a la cuenta
  (`TaskService.AdoptAccountAsync`): entrar no cuesta el nivel ni la racha.
- Los pasos de alta en Google Cloud y Supabase, en `supabase/README.md`.

## 2026.08.29.1 — Primer esqueleto funcional

Arranque del proyecto a partir de la especificación funcional.

**Documentación**
- `ESPECIFICACION.md` con la especificación funcional completa.
- `ARQUITECTURA.md`: reparto en tres proyectos, modelo de datos, seguridad, IA y fases.
- `supabase/01_schema.sql` y `supabase/02_rls.sql`: esquema PostgreSQL, RLS por pertenencia y las
  funciones `create_group`, `join_group` y `rotate_group_key`.

**TaskManager.Core**
- Modelo (grupos, listas, tareas, micro-pasos, XP) sobre SQLite, con cola de salida para sincronizar.
- "Mi Día" resuelto como fecha en la tarea, no como lista: la vista se vacía sola a medianoche sin
  perder nada.
- Gamificación: 50 XP por tarea, 10 por micro-paso, 15 por desglose; combos hasta x3 en 90 s;
  niveles con curva cuadrática; racha que perdona un día de descanso y nunca resta XP.
- Desglose "Pasos Mágicos" con modelo local (API de OpenAI) y plantillas de reserva sin red.

**TaskManager.Mobile (Android)**
- Mi Día, Mis listas privadas, Mis grupos, El Tablón del Gremio, Ajustes y Acerca de.
- Celebración con confeti dibujado en `GraphicsView`, indicador flotante de XP y vibración.
- Alta de grupo con clave compartida y varias listas por grupo.

**TaskManager.Desktop (Windows)**
- Icono de bandeja con el número de tareas pendientes de Mi Día, dibujado en memoria.
- Panel flotante con captura rápida, atajo global `Ctrl+Alt+T` y mini-confeti al completar.
- Inicio con Windows mediante la clave `Run` del usuario.

**Pendiente**: sincronización real con Supabase (fase 4), widget y sonidos (fase 5), ficha de Play
Console (fase 6).
