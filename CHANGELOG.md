# Changelog — Task Manager

Formato de versión `AAAA.MM.DD.NN` (constitución Mobile 3): el contador del día a dos cifras, el mismo que cierra el versionCode.

## 2026.10.07.00 — Pegar una imagen en Android, y que se note

Windows `2026.10.7.0` · Android `2026.10.07.00`

**Castellano**

- Corregido (Android): «pegar una imagen no funciona». Probado en el Xiaomi con la 2026.10.06.00, la
  imagen sí se guardaba (copiada en Chrome, Edge, Google Fotos o en otra aplicación, y pegada con el
  botón, con «Pegar» en las notas o el título, o desde el portapapeles de Gboard), pero como adjunto
  en «Enlaces y ficheros», más abajo y tapado por el teclado, y sin decir nada: parecía que no hacía
  nada. Ahora sale arriba del detalle el aviso «Imagen añadida a Enlaces y ficheros» durante tres
  segundos. No es un Toast de Android: el sistema ya saca el suyo («… pasted from your clipboard») y
  el nuestro esperaba en cola detrás, unos cuatro segundos tarde.
- También se pueden pegar imágenes en las etiquetas y en el paso nuevo, y se reconocen las que vienen
  dentro de un texto copiado con formato (`data:image/…;base64` en el HTML de un correo, unas notas o
  un chat). Las que van por dirección (`https://…`) no se bajan.
- Pruebas: lógica de las imágenes dentro de HTML y del aviso (sale, se va solo, se reinicia con otra
  imagen y no sale si la imagen es demasiado grande), y la prueba de interfaz T10 con un ayudante
  nuevo, `TaskManager.UITests.Portapapeles`, que deja una imagen de verdad en el portapapeles como
  lo hace otra aplicación (Appium solo sabe poner texto).

**English**

- Fixed (Android): "pasting an image doesn't work". Tested on the Xiaomi with 2026.10.06.00: the
  image was in fact saved (copied in Chrome, Edge, Google Photos or another app, and pasted with the
  button, with "Paste" in the notes or the title, or from Gboard's clipboard), but as an attachment
  in "Links and files", further down and hidden by the keyboard, with no word about it, so it looked
  like nothing happened. Now the detail shows "Image added to Links and files" at the top for three
  seconds. It is not an Android Toast: the system already shows its own ("… pasted from your
  clipboard") and ours was queued behind it, about four seconds late.
- Images can also be pasted in the tags and in the new step, and images inside copied formatted text
  (`data:image/…;base64` in the HTML of a mail, notes or a chat) are recognised. Images given by
  address (`https://…`) are not downloaded.
- Tests: logic of images inside HTML and of the notice (it shows, goes away by itself, restarts with
  another image and does not show if the image is too big), and UI test T10 with a new helper,
  `TaskManager.UITests.Portapapeles`, that puts a real image on the clipboard as another app does
  (Appium can only set text).

## 2026.10.06.00 — Etiquetas a la vista, varias a la vez, e imágenes pegadas en Android

Windows `2026.10.6.0` · Android `2026.10.06.00`

**Castellano**

- Detalle de la tarea: las etiquetas que ya existen salen en filas que saltan de línea, todas a la
  vista, en vez de en una tira que había que desplazar de lado (en el móvil y en Windows).
- Filtro por etiquetas: Ctrl+clic en una pastilla la suma o la quita de las marcadas, y la lista
  enseña las tareas que llevan **cualquiera** de ellas (con «Sin etiqueta» marcada, también las que
  no llevan ninguna). Un clic normal sigue dejando solo esa, y «Todas» lo borra. Vale en «Mis
  tareas», el tablero y el panel rápido de Windows, y en «Mis tareas» y el tablero del móvil con
  teclado. La selección se guarda como antes la etiqueta suelta (el mismo ajuste, separadas por
  comas), y el rótulo de la lista las nombra todas.
- Corregido (Android): no se podían pegar imágenes. Ahora «Pegar» con una imagen copiada en el título
  o en las notas del detalle, y las imágenes que manda el teclado (Gboard…), se guardan como adjunto
  de la tarea. El botón de pegar adjunto lee el portapapeles desde el hilo de la pantalla (como exige
  Android 10+) y reconoce la imagen por sus bytes cuando quien la copió no dice el tipo.
- Pruebas: lógica del filtro de varias etiquetas, del reconocimiento de imágenes pegadas y de las
  pantallas, y tres pruebas de interfaz nuevas con Appium (T07–T09, ver `TaskManager.UITests`).

**English**

- Task detail: the existing tags wrap over several rows, all in view, instead of a strip you had to
  scroll sideways (on the phone and on Windows).
- Tag filter: Ctrl+click on a chip adds it to or removes it from the selected ones, and the list shows
  the tasks that have **any** of them (with "No tag" selected, also those with none). A plain click
  still leaves just that one, and "All tags" clears it. It works in "My tasks", the board and the
  Windows quick panel, and in "My tasks" and the board on the phone with a keyboard. The selection is
  saved where the single tag was (same setting, comma-separated), and the list caption names them all.
- Fixed (Android): images could not be pasted. Now "Paste" with a copied image in the title or the
  notes of the detail, and images sent by the keyboard (Gboard…), are saved as attachments of the
  task. The paste-attachment button reads the clipboard on the UI thread (as Android 10+ requires)
  and recognises the image by its bytes when whoever copied it does not say the type.
- Tests: logic of the multi-tag filter, of recognising pasted images and of the screens, plus three
  new Appium UI tests (T07–T09, see `TaskManager.UITests`).

## 2026.10.03.00 — Las pantallas, también probadas

Windows `2026.10.3.0` · Android `2026.10.03.00`

**Castellano**

- Corregido: la racha («¡Racha x1,5!») y la subida de nivel de la celebración salían en castellano
  con la aplicación en inglés (en el móvil y en el panel de Windows).
- Corregido (Windows): la ventana principal podía enseñar tareas, listas y grupos repetidos cuando dos
  recargas se cruzaban (elegir una lista dispara otra).
- Corregido (Windows): la barra de marcar varias actuaba sobre la lista que se acababa de vaciar y
  sus botones no hacían nada.
- Corregido (Windows): si la entrada con la cuenta se quedaba sin respuesta, salía «Cannot access a
  disposed object» en vez de «entrada cancelada».
- Pruebas: además del núcleo, ahora se prueban las pantallas del móvil y las ventanas de Windows
  (427 pruebas, `dotnet test TaskManager.Pruebas.slnx`). La cobertura sobre toda la aplicación pasa
  del 36,8 % al **96,1 %**, contada de una forma nueva (solo sentencias; ver el README). Para poder
  probarlas, la lógica que vivía en las pantallas pasa a clases propias y lo que toca el sistema va
  detrás de interfaces; lo que se ve y lo que hace la aplicación no cambia.

**English**

- Fixed: the streak ("Streak x1.5!") and level-up messages of the celebration appeared in Spanish
  with the app in English (on the phone and in the Windows panel).
- Fixed (Windows): the main window could show duplicated tasks, lists and groups when two reloads
  overlapped (picking a list triggers another one).
- Fixed (Windows): the multi-select bar acted on the list that had just been emptied and its buttons
  did nothing.
- Fixed (Windows): when signing in timed out, it showed "Cannot access a disposed object" instead
  of "sign-in cancelled".
- Tests: besides the core, the phone screens and the Windows windows are now tested (427 tests,
  `dotnet test TaskManager.Pruebas.slnx`). Line coverage over the whole app goes from 36.8 % to
  **96.1 %**, counted in a new way (statements only; see the README). To test them, the logic that
  lived in the screens moved to its own classes and what touches the system sits behind interfaces;
  what the app shows and does is unchanged.

## 2026.09.30.00 — Una subida por tarea, no una por cambio

Windows `2026.9.30.0` · Android `2026.09.30.00`

**Castellano**

- Corregido: de una tarea (o lista, paso o adjunto) tocada varias veces sin conexión solo salía de
  la cola de sincronización la última entrada, y la misma fila se volvía a subir en cada vuelta
  siguiente, una por cada cambio que quedaba atrás. Ahora sube una vez y la cola queda vacía. Lo
  encontró el banco de pruebas nuevo (`TaskManager.Tests`).

**English**

- Fixed: for a task (or list, step or attachment) changed several times while offline, only the
  last entry left the sync queue, and the same row was uploaded again on every following round.
  Now it goes up once and the queue is left empty. Found by the new test suite
  (`TaskManager.Tests`).

## 2026.09.29.01 — En la Microsoft Store

Windows `2026.9.29.1` · Android `2026.09.29.01`

**Castellano**

- Task Manager para Windows está publicada en la Microsoft Store
  (https://apps.microsoft.com/detail/9PHJK2391727). En el móvil, **Acerca de › También disponible
  en** dice «Aplicación para Windows en Microsoft Store» y abre su ficha (antes, las releases de
  GitHub, que siguen teniendo el EXE y el MSIX de cada versión).

**English**

- Task Manager for Windows is published on the Microsoft Store. On the phone, **About › Also
  available on** says "Windows app on Microsoft Store" and opens its page (it used to open the
  GitHub releases, which still have the EXE and MSIX of every version).

## 2026.09.29.00 — Pantalla de Novedades

Windows `2026.9.29.0` · Android `2026.09.29.00`

**Castellano**

- **Novedades** (constitución General 6.7): lo que cambió en las cinco últimas versiones, de la más
  nueva a la más antigua, en tu idioma. Sale sola la primera vez que se abre una versión nueva (en
  el móvil, al llegar a «Mis tareas»; en Windows, al arrancar) y después se abre cuando quieras: en
  el móvil desde el menú lateral y desde Acerca de; en Windows desde el menú del icono de la bandeja
  y desde Acerca de. El contenido vive en el núcleo (`TaskManager.Core/whatsnew.json`), el mismo
  para las dos.
- Textos: el texto de privacidad dice «dispositivos» (no «aparatos»); la ayuda del correo (oculto
  todavía) ya no nombra proveedores ajenos, y un rechazo de la sesión habla de «el servidor».

**English**

- **What's new** screen: the changes of the last five versions, newest first, in your language. It
  opens by itself the first time a new version starts (on the phone, on My tasks; on Windows, at
  start-up) and any time later from the side menu and About (phone) or the tray icon menu and About
  (Windows).
- Texts: the privacy text says "devices"; the (still hidden) mail hint no longer names other
  providers, and a rejected session mentions "the server".

## 2026.09.27.01 — Atrás vuelve atrás y un error ya no la cierra

Windows `2026.09.27.01` · Android `2026.09.27.01`

**Castellano**

- **Botón de atrás del móvil**: en cualquier pantalla vuelve a la anterior (del detalle de una tarea
  a su lista, de una lista a «Listas», de Etiquetas o del QR a donde estabas); desde Calendario,
  Listas, Tablero, Grupos, Ajustes o Acerca de vuelve a «Mis tareas», y en «Mis tareas» la
  aplicación se oculta sin cerrarse. Si hay algo abierto encima —el menú lateral, un diálogo, el modo
  de marcar varias o texto en el buscador— atrás cierra primero eso. En Android 16 atrás cerraba la
  aplicación desde cualquier pantalla; ya no.
- **No se pierde lo escrito en una tarea**: si cambias algo en el detalle y pulsas atrás (o la
  flecha de arriba) sin guardar, pregunta si guardar, descartar o seguir editando.
- **Un error inesperado ya no cierra la aplicación** (Android y Windows): se avisa en tu idioma, la
  aplicación sigue funcionando y los detalles quedan en el registro de errores (`crash.log`).

**English**

- **Phone back button**: on any screen it goes back to the previous one (from a task to its list,
  from a list to Lists, from Tags or the QR to where you were); from Calendar, Lists, Board, Groups,
  Settings or About it returns to My tasks, and on My tasks the app is hidden without closing. If
  something is open on top —the side menu, a dialog, multi-select mode or text in the search box—
  back closes that first. On Android 16 back used to close the app from any screen; not any more.
- **Nothing you type in a task is lost**: if you change something in the task details and press back
  (or the top arrow) without saving, it asks whether to save, discard or keep editing.
- **An unexpected error no longer closes the app** (Android and Windows): you get a notice in your
  language, the app keeps working and the details go to the error log (`crash.log`).

## 2026.09.27 — Cada aviso con su texto

Windows `2026.09.27.00` · Android `2026.09.27.00`

- Las opciones «Avisarme de las tareas pendientes» (Windows) y «Avisarme de lo que queda pendiente»
  (Android) compartían la misma clave de traducción, así que en inglés las dos salían como «Remind me
  what is left». Ahora cada una tiene la suya: en Windows, «Remind me about pending tasks»; en
  Android, «Let me know what is still left to do».
- Fuera dos claves repetidas más en los diccionarios de castellano e inglés («Todas» del filtro, que
  estaba dos veces con el mismo texto, y el nombre de la lista por defecto, del que solo contaba
  «Tareas»/«Tasks»). Nada cambia a la vista.

## 2026.09.23 — Fuera la varita

- **Se ha quitado el desglose con IA («Pasos Mágicos»)** y todo lo que lo acompañaba: el botón de la
  varita —que en el móvil ya estaba oculto y en Windows seguía a la vista—, el servicio que hablaba
  con un modelo local, las plantillas de reserva, los ajustes del servidor y del modelo, los textos y
  la documentación. Los pasos se escriben a mano, como se venía haciendo.
- **Nada de lo guardado se toca**: los pasos que en su día vinieron de la IA siguen ahí, y la columna
  del premio y el tipo de XP antiguo se conservan para no romper las partidas ni la sincronización;
  simplemente ya no se otorgan.

## 2026.09.18 — El botón de etiquetas, a la vista

Windows `2026.09.18.00` · Android `2026.09.18.01`

- Android `2026.09.18.01`: el icono del botón de etiquetas salía negro con el tema oscuro; ahora
  cambia con el tema.

- El botón de la **pantalla de etiquetas** estaba al final de la fila de chips, que se desplaza en
  horizontal: con unas cuantas etiquetas quedaba fuera de la vista. Ahora va fijo a la izquierda de
  la fila, fuera del desplazamiento. Y la fila se queda mientras exista alguna etiqueta, aunque solo
  la lleven tareas hechas: si no, no había por dónde llegar a borrarlas.

## 2026.09.17 — Pantalla de etiquetas y enlaces a la otra versión

Windows `2026.09.17.00` · Android `2026.09.17.00`

- **Pantalla de etiquetas** desde el botón de etiqueta al final de la fila de filtros: todas las
  etiquetas (también las que solo llevan tareas hechas), cuántas tareas tiene cada una y una
  papelera para borrarla de todas; si la llevan tareas sin acabar, pregunta. El botón derecho
  (Windows) y la pulsación larga (Android) sobre un chip siguen funcionando.
- **Acerca de**: en Windows, enlaces a la aplicación de Android (Google Play) y a todas las
  versiones en GitHub; en Android, a la aplicación de Windows (exe / MSIX).

## 2026.09.16 — Pegar imágenes como adjunto y borrar etiquetas

Windows `2026.09.16.00` · Android `2026.09.16.00`

- **Pegar del portapapeles como adjunto.** En Windows, el botón «pegar» de Adjuntos (o Ctrl+V con el
  foco fuera de un cuadro de texto) mete una imagen copiada (captura, recorte, imagen de una web)
  como PNG con la fecha en el nombre, o los ficheros copiados en el Explorador. En Android, el botón
  «pegar» adjunta la imagen que haya en el portapapeles (MAUI solo lee texto: se saca del
  ClipboardManager). Mismo tope de tamaño que «Añadir fichero».
- **Borrar etiquetas.** Botón derecho sobre una etiqueta de la fila de filtros (Windows) o
  pulsación larga (Android) → «Borrar etiqueta»: se quita de todas las tareas que la llevan, hechas
  o no. Si la llevan tareas sin acabar se pregunta diciendo cuántas; si solo la llevan tareas
  hechas, se confirma sin más.

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
