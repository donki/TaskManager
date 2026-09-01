# Changelog — Task Manager

Formato de versión `AAAA.MM.DD.N` (constitución Mobile 3).

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
