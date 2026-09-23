# Task Manager

Gestor de tareas diarias con **listas por grupo**, **micro-pasos** y
**celebración al completar**. Dos aplicaciones sobre un mismo núcleo: Android (.NET MAUI) y Windows
(WPF en la bandeja del sistema).

- Qué hace → [ESPECIFICACION.md](ESPECIFICACION.md)
- Cómo está hecho → [ARQUITECTURA.md](ARQUITECTURA.md)
- Backend → [supabase/README.md](supabase/README.md)

## Dónde conseguirla

- **Google Play:** https://play.google.com/store/apps/details?id=com.socratic.taskmanager
- **Microsoft Store:** https://apps.microsoft.com/search?query=%22sOC+Task+Manager%22 (enlace directo al producto en cuanto Partner Center dé el identificador)
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
