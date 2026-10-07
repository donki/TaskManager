# Task Manager

Gestor de tareas diarias con **listas por grupo**, **micro-pasos** y
**celebración al completar**. Dos aplicaciones sobre un mismo núcleo: Android (.NET MAUI) y Windows
(WPF en la bandeja del sistema).

- Qué hace → [ESPECIFICACION.md](ESPECIFICACION.md)
- Cómo está hecho → [ARQUITECTURA.md](ARQUITECTURA.md)
- Backend → [supabase/README.md](supabase/README.md)

## Dónde conseguirla

- **Google Play:** https://play.google.com/store/apps/details?id=com.socratic.taskmanager
- **Microsoft Store:** https://apps.microsoft.com/detail/9PHJK2391727
- **Releases de GitHub** (APK / EXE / MSIX de cada versión): https://github.com/donki/TaskManager/releases

## Estado

Fases 1 a 3 hechas: documentación, esquema SQL con RLS, núcleo compartido y las dos aplicaciones
funcionando **contra la base de datos local**. De la fase 4 está hecha la **entrada con cuenta**
(código completo en las dos aplicaciones); falta subir la cola de cambios y el Realtime.

| Proyecto | Qué es | Compila |
|---|---|---|
| `TaskManager.Core` | Modelo, SQLite, XP y niveles, entrada con cuenta, contrato de sincronización | sí |
| `TaskManager.Mobile` | App Android (MAUI): Mi Día, listas, grupos, Tablón, Ajustes, Acerca de | sí |
| `TaskManager.Desktop` | App Windows (WPF): bandeja, panel flotante, atajo global | sí, probada en ejecución |

## Compilar

```
dotnet build TaskManager.slnx
```

Solo el escritorio:

```
dotnet run --project TaskManager.Desktop
```

El AAB firmado de Android sigue el procedimiento de la constitución (clave compartida, la misma que
File Manager; la contraseña se pasa por línea de comandos):

```
dotnet publish TaskManager.Mobile -c Release -f net10.0-android36.0 -p:AndroidPackageFormat=aab \
  -p:AndroidSigningStorePass=<pass> -p:AndroidSigningKeyPass=<pass>
```

## Pruebas

El banco entero se lanza con un solo `dotnet test TaskManager.Pruebas.slnx` (tres proyectos xUnit):

- `TaskManager.Tests` prueba el núcleo compartido `TaskManager.Core`: repeticiones y series,
  filtros, etiquetas, XP/niveles/rachas, repositorio SQLite (en ficheros temporales), `TaskService`,
  cifrado de texto y de grupo, invitaciones, traducciones (mismas claves y marcadores en es/en),
  novedades, ajustes, y la entrada con cuenta y la sincronización con Supabase contra un servidor
  HTTP falso (nada sale a la red).
- `TaskManager.Mobile.Tests` construye de verdad las páginas MAUI del móvil (el proyecto de la app
  compila también para `net10.0`, solo para esto) con un dispatcher de prueba y servicios reales sobre
  SQLite temporal, y pulsa sus botones: Mis tareas, detalle, listas, tablero, calendario, grupos,
  correo, ajustes, entrada, novedades, el Shell y el botón de atrás. Lo que toca el sistema
  (navegación, diálogos, portapapeles, avisos, navegador) va por interfaces con un doble.
- `TaskManager.Desktop.Tests` crea las ventanas WPF de Windows en un hilo STA, fuera de la pantalla y
  con la `App` real, y las maneja igual: la bandeja, el panel rápido, la ventana principal, el detalle,
  el calendario, ajustes, entrada y el arranque entero. No toca el registro, ni
  `%LOCALAPPDATA%`, ni el portapapeles, ni la bandeja de verdad, ni la app que esté abierta.

| Fecha | Pruebas | Cobertura de lo instrumentado | Cobertura sobre toda la app | Tiempo del banco |
|---|---|---|---|---|
| 2026-10-07 | 454 (pasan todas: 262 + 115 + 77) | 97,0 % (7972 de 8222 líneas) | **95,5 %** (7972 de 8349 líneas) | ≈3 min 30 s con cobertura (los tres a la vez; el de Windows marca el tiempo) |
| 2026-10-06 | 452 (pasan todas: 262 + 113 + 77) | 97,0 % (7943 de 8186 líneas) | **95,5 %** (7943 de 8313 líneas) | ≈3 min 40 s con cobertura (los tres a la vez; el de Windows marca el tiempo) |
| 2026-10-03 | 427 (pasan todas: 255 + 97 + 75) | 97,5 % (7878 de 8081 líneas) | **96,1 %** (7878 de 8199 líneas) | ≈2 min 20 s (los tres a la vez; el de Windows marca el tiempo) |
| 2026-09-30 | 254 (pasan todas) | 97,3 % (3919 de 4026 líneas) | ≈39 % (3919 de ≈10 000 líneas) | ≈11 s |

**Cómo se cuenta «toda la app»** (`tools/cobertura-app.py`, desde el 2026-10-03): todos los `.cs`
de `TaskManager.Core`, `TaskManager.Mobile` y `TaskManager.Desktop` (fuera `obj/`, `bin/`, `*.g.cs`,
`*.Designer.cs` y los proyectos de pruebas). Solo cuentan las líneas con **sentencias**, que es lo
que coverlet mide: no cuentan llaves sueltas, `using`, `namespace`, atributos, constantes, campos sin
valor, firmas de métodos ni el interior de interfaces y enum. De los ficheros que compila el banco se
toman las líneas que marca coverlet (sin excluir `CompilerGeneratedAttribute`, así que cuentan los
métodos `async` y las lambdas); los que el banco no compila (`Platforms/Android`) se cuentan con
esas reglas y entran enteros como **no cubiertos**, igual que lo que va bajo `#if ANDROID` en un
fichero común (el banco del móvil compila `net10.0`). En lo instrumentado, las reglas y coverlet
difieren en un 1 %. La cifra del 2026-09-30 contaba también llaves y declaraciones; con la
cuenta nueva aquel banco daba el 36,8 %.

Lo que queda sin cubrir (321 líneas): el código nativo de Android (`Platforms/Android`, ≈125: avisos,
sincronización de fondo, actividad), las llamadas finales a Windows de verdad (registro, navegador,
portapapeles, captura), el arranque de MAUI en el móvil y ramas defensivas que no se dan.

```
dotnet test TaskManager.Pruebas.slnx
dotnet test TaskManager.Pruebas.slnx --collect:"XPlat Code Coverage" --results-directory cobertura
python tools/cobertura-app.py cobertura --detalle
```

Las pruebas de interfaz en el dispositivo (Appium, `TaskManager.UITests`) van aparte: ver su README.

## La aplicación de escritorio

- Vive en la bandeja; el icono lleva un globo rojo con las tareas pendientes de Mi Día.
- Clic izquierdo o **Ctrl+Alt+T** (configurable) abre el panel flotante sobre cualquier ventana,
  incluido un juego a pantalla completa.
- Escribir + Intro añade la tarea; dentro se le añaden micro-pasos.
- `--tray` arranca escondido: es lo que usa el inicio con Windows.
- Datos en `%LOCALAPPDATA%\Socratic\TaskManager\taskmanager.db3`.

## Cuenta

Entrar es **obligatorio** y se puede hacer con **Google o con Microsoft**, hablando con el proveedor
**directamente** (PKCE), no a través de Supabase: la vuelta se recoge en un servidor local en
`127.0.0.1`, igual en Windows y en Android. Los tokens se guardan en el almacén seguro de Android y
con DPAPI en Windows — nunca en claro.

**Cada cuenta tiene sus listas** en el mismo aparato: se cambia de una a otra desde Ajustes, con un
botón por proveedor, y lo de la anterior se queda donde estaba. Lo escrito antes de que hubiera
cuenta (tareas, XP y rachas) se lo queda la primera que entra, así que al actualizar no se pierde
nada.

La sesión de Supabase es **aparte y opcional**: el `id_token` que firma el proveedor se canjea por
un JWT del proyecto, que es lo único que entiende la RLS. Si ese canje falla, se entra igual y la
aplicación funciona en local. Los pasos del servidor están en
[supabase/README.md](supabase/README.md).

## Lo que falta

- Fase 4: proyecto de Supabase con los proveedores dados de alta, subida de la cola y Realtime.
- Fase 5: widget de Android, sonidos de celebración, temas desbloqueables y reacciones de grupo.
- Fase 6: ficha de Play Console, capturas y subida a `alpha`.

MIT · Copyright © 2026 Socratic
