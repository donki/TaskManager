# Changelog — Task Manager

Formato de versión `AAAA.MM.DD.N` (constitución Mobile 3).

## 2026.09.11 — Borrar una serie entera, crear desde el tablero y el calendario, salir o borrar un grupo

Windows `2026.9.11.1` · Android `2026.09.11.1`

- **Borrar una tarea repetitiva pregunta si solo esa vuelta o la serie entera.** Antes se borraba
  la vuelta y las otras treinta seguían ahí. Con «toda la serie» se van todas las que se
  generaron, hechas incluidas: quien borra la serie quiere que no quede rastro. Al borrar varias
  a la vez, si alguna es de una serie se pregunta lo mismo para todas.
- **Se crean tareas desde el tablero** (caja arriba; en Windows, además, el botón de reproducir la
  crea ya «en curso») **y desde el calendario** (caja bajo el día elegido: la tarea nace planificada
  para ese día; en Windows, doble clic en un día lleva el foco a la caja). Como en «Mis tareas»,
  entran en la primera lista y se abren para rematarlas.
- **La papelera de un grupo pregunta: ¿salir o borrarlo para todos?** Salir quita la pertenencia en
  el servidor y los demás miembros lo conservan; borrarlo se lo lleva con sus listas y tareas para
  todos, y solo puede hacerlo quien lo creó (el servidor no deja a nadie más). Antes el botón solo
  lo escondía en este dispositivo, y en la siguiente bajada podía volver.
- **Un grupo que ya no está en el servidor** —lo borró el dueño, o a uno lo sacaron— **desaparece
  en la siguiente sincronización**. Los apuntes de baja son de quien borra y los demás miembros no
  los ven, así que se compara con la lista de grupos del servidor.

## 2026.09.09 — Las repeticiones se escriben, y Windows gana tablero y calendario

Windows `2026.9.9.4` · Android `2026.09.09.4`

> Antes de usar esta versión hay que ejecutar `supabase\12_en_curso_y_series.sql` en el proyecto de
> Supabase: la aplicación manda dos columnas nuevas en cada tarea que sube y, sin ellas, el servidor
> rechaza el lote entero.

- **Una tarea que se repite ya son todas sus vueltas, no una que reaparece.** Al guardarla se
  escriben de golpe: una tarea por cada día en que toca entre la fecha de planificación y la de
  finalización. «Cada martes de octubre a diciembre» son trece tareas que se ven en la lista, en el
  tablero y en el mes, y se puede mover o adelantar una suelta sin tocar las demás. Antes solo
  existía la vuelta de turno y la siguiente nacía al completar la anterior: el calendario enseñaba
  un único día y no había forma de ver lo que venía.
- **Por eso las dos fechas son obligatorias cuando hay repetición**: sin fecha de finalización,
  «todos los días» no tiene último día. Se avisa al guardar y se abren los dos campos.
- **Una serie se corta en 500 tareas.** Una repetición diaria a cinco años son 1826, y hay que
  sincronizarlas y mirarlas todos los días; al llegar al final se pone una fecha nueva.
- **Cambiar la repetición rehace las vueltas que quedan; cambiar una fecha mueve solo esa.** Lo
  hecho y lo ya pasado no se toca nunca: es el registro de lo que se hizo, con su XP y su racha.
- **Hay tablero.** Tres columnas —pendientes, en curso y hechas— y las tareas se arrastran de una a
  otra para cambiarles el estado. «En curso» es un estado nuevo: en la lista una tarea estaba hecha
  o no, y esa columna del medio es justo la que hace falta cuando hay varias cosas empezadas a la
  vez. Lleva los mismos filtros que «Mis tareas» y la misma fila de etiquetas. Lo que se crea nace
  en pendientes.
- **Y también en Android**, con las tres columnas a la vista *también con la tableta en vertical*:
  el ancho se reparte a partes iguales en vez de dar a cada columna una medida fija, que en vertical
  dejaría la tercera fuera de la pantalla.
- **Se ordena arrastrando**: las tarjetas dentro de su columna, y también las filas de «Mis tareas»
  en Android, que hasta ahora solo se podían reordenar dentro de una lista. El orden manual manda
  sobre el plazo, así que lo que se coloca a mano se queda donde se puso.
- **En Windows el tablero hace las dos cosas con el mismo gesto**: soltar una tarjeta en otra
  columna le cambia el estado, y soltarla en la suya la recoloca. En Android los dos serían la misma
  pulsación larga, así que allí el arrastre ordena y el estado se cambia tocando la tarjeta.
- **«En curso» está también en la ficha de la tarea**, en Windows y en Android, junto a «hecha» y
  «anclada»: en el móvil es la única forma de ponerlo, y en Windows evita tener que ir al tablero.
- **Windows tiene pestaña de calendario.** El mes, con las tareas donde están planificadas y las
  flechas para ir a los meses de antes y de después. Es el mismo control que la ventana suelta del
  menú de la bandeja —escrito una vez y enseñado en los dos sitios—, y ahora además vuelve a hoy de
  un botón y abre una tarea con doble clic.
- **El pie de las listas cuenta contra lo que queda.** Decía «se muestran 12 de 480» comparando con
  todo lo que existe, que con unos meses de uso es sobre todo archivo; ahora dice «se muestran 12 de
  34 pendientes · 62 % hechas». En Windows y en Android.
- **La fila de filtros solo enseña etiquetas con algo pendiente.** Filtrar por una etiqueta cuyas
  tareas están todas hechas devolvía una lista vacía, y la fila se iba convirtiendo en el archivo de
  todas las etiquetas que han existido. Al **etiquetar** una tarea siguen ofreciéndose todas, que es
  justo lo contrario: ahí se reutiliza la de siempre precisamente porque ya no queda nada vivo con
  ella.
- **Fuera el Gremio.** La pantalla de nivel, XP, racha y desbloqueados se ha quitado de Windows y de
  Android, con su entrada de menú y sus textos. La cuenta de XP y la racha **siguen por dentro**:
  son las que hacen saltar la celebración al completar una tarea, y quitarlas también habría sido
  arrancar el confeti y la tabla `xp_events` con él.
- **El tablero es la tercera pestaña**, detrás de «Mis tareas» y «Mis listas».
- **El calendario de los selectores de fecha ya no sale en chino.** Ese calendario lo pinta WPF, y
  para saber en qué idioma no mira la aplicación sino la propiedad `Language` del propio control —y
  el estilo que le pone HandyControl trae `zh-cn` escrito dentro. De ahí el «2026年9月» y el
  «一 二 三 四 五 六» en una ficha en castellano. Fijárselo a la ventana no basta (lo heredado pierde
  contra un estilo) y fijárselo al calendario cuando se carga llega tarde (ya ha pintado la fila de
  días). Lo que funciona, y es lo que hace ahora, es engancharse a la carga del **selector**, bajar
  al calendario de su desplegable y escribirle el idioma antes de que pinte nada.

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
