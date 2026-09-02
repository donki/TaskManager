# Task Manager en la Microsoft Store

Estado: **el paquete está construido y listo para subir.** Falta enviarlo desde Partner Center,
que la primera vez no se puede hacer por API.

- **Identidad** (Partner Center → Product management → Product identity), ya puesta por defecto en
  `tools/empaquetar-msix.ps1`:
  - `Package/Identity/Name` — `sOCratic.sOCTaskManager`
  - `Package/Identity/Publisher` — `CN=2FC3763A-58D5-473A-840E-D47726B23FE3`
  - `PublisherDisplayName` — `sOCratic`
  - Nombre del paquete (`DisplayName`) — **`sOC Task Manager`**. La Store lo comprueba contra la
    lista de nombres reservados de la aplicación: si el reservado fuera otro, se regenera con
    `-DisplayName "<el que sea>"`.
- **Id. de Store**: `9PHJK2391727` · **PFN**: `sOCratic.sOCTaskManager_6c84vmrh3mfca`
- **Paquete**: `C:\ID\OneDrive\TaskManager\sOCTaskManager-2026.9.24.0.msix` (76 MB, **sin
  firmar**, que es como lo quiere la Store: la firma la pone ella).

## La API no sirve para la primera vez

Partner Center **sí tiene API** (Microsoft Store submission API, y la `msstore` CLI que la envuelve),
con autenticación de Microsoft Entra ID por *client credentials* — tenant ID, client ID y client
secret, ámbito `https://api.store.microsoft.com/.default`, token de 60 minutos. Pero la
documentación es explícita en dos puntos:

1. La aplicación **no se puede crear por API**; tiene que existir ya en Partner Center. Hecho.
2. **La primera *submission* hay que crearla a mano**, con el cuestionario de clasificación por
   edades incluido. A partir de ahí, y solo a partir de ahí, la API puede crear envíos, subir
   paquetes, tocar la ficha y publicar.

O sea que este primer envío va desde el navegador aunque haya credenciales. Para automatizar los
siguientes hace falta registrar una aplicación de Entra ID asociada a la cuenta (y ser
**administrador global** de ese directorio para poder asociarla), y entrar con credenciales de Entra
ID, **no** con la cuenta Microsoft personal.

## Lo que ya está hecho

- **Paquete MSIX que se construye y se firma**, comprobado (`tools/empaquetar-msix.ps1`). Envuelve
  el ejecutable autocontenido tal cual; no hace falta instalador.
- **Manifiesto** (`TaskManager.Desktop/Package/AppxManifest.xml`) con la única capacidad que
  necesita una aplicación de escritorio, `runFullTrust`, y nada más.
- **Iconos de la tienda** generados del mismo icono del ejecutable
  (`TaskManager.Desktop/Package/Images/`): 44, 71, 150, 310, 310x150 y el logo de 50.
- **Versión**: se saca del csproj. MSIX exige cuatro números y reserva el cuarto para la Store, así
  que `2026.9.2.4` se convierte en `2026.9.24.0`.
- **Probarlo aquí**: `.\tools\empaquetar-msix.ps1 -Autofirmar` genera un certificado temporal y firma
  el paquete. Para instalarlo hay que confiar antes en `bin\pruebas.cer`. Ese paquete es **solo para
  probar**.

## La ficha, ya redactada

Todo el material está en `store/microsoft/`:

- `ficha-es-ES.md` y `ficha-en-US.md`: nombre, descripción, características y qué va en cada campo,
  en bloques para copiar y pegar.
- `capturas/`: cuatro capturas de 1586x893 o mayor, hechas con una **compilación de demostración**
  (base aparte, sin cuenta, tareas inventadas). Ni un dato real.
- `logos/`: póster 9:16, caja 1:1, iconos 300/150/71 y arte de héroe 16:9.

## Lo que hay que rellenar en la ficha

### Descripción (es-ES)

> Task Manager es una lista de tareas que de verdad está en todos tus aparatos.
>
> Escribes una tarea en el móvil y aparece en el ordenador. Cada tarea puede llevar lista,
> etiquetas, fecha de inicio y de vencimiento, repetición, pasos, enlaces y ficheros. Las que no
> pueden esperar se anclan y se quedan arriba del todo.
>
> • Listas y etiquetas para ordenar lo tuyo
> • Pasos dentro de una tarea, y se arrastran para ordenarlos
> • Repetición diaria, semanal, mensual o anual
> • Buscador que mira en todo el texto
> • Avisos de lo que queda pendiente
> • Modo claro y oscuro
> • En castellano y en inglés
>
> **Privacidad:** el texto que escribes se cifra en tu dispositivo antes de subir y se guarda
> cifrado. Sin anuncios, sin rastreadores y sin analítica.

### Description (en-US)

> Task Manager is a to-do list that really is on all your devices.
>
> Write a task on your phone and it shows up on your computer. Every task can have a list, tags, a
> start and a due date, repetition, steps, links and files. The ones that cannot wait get pinned and
> stay at the top.
>
> • Lists and tags to keep things in order
> • Steps inside a task, dragged into the order you want
> • Daily, weekly, monthly or yearly repetition
> • Search across all the text
> • Reminders for what is still pending
> • Light and dark mode
> • Spanish and English
>
> **Privacy:** the text you write is encrypted on your device before it goes up, and stored
> encrypted. No ads, no trackers, no analytics.

### Lo demás

- **Categoría:** Productividad.
- **Edad:** apta para todos los públicos. En el cuestionario: no hay contenido generado por
  usuarios que se comparta en público, no hay compras, no hay publicidad.
- **Precio:** gratis.
- **Idiomas:** es-ES y en-US.
- **Capturas:** hacen falta al menos una de 1366x768 o mayor. Sirven las de la ventana principal, el
  detalle de una tarea y el panel rápido.
- **Política de privacidad (obligatoria):** la URL del sitio.

> ⚠️ **Antes de enviar nada**: la política publicada tiene que ser la nueva, la que dice que la
> aplicación entra con cuenta y guarda las tareas en un servidor cifradas. Está reescrita en
> `Web/socraticweb/`, pero **falta pegarla en Google Sites**. Enviar la ficha con la política vieja
> —la que dice que no hay cuentas ni servidor— sería declarar algo falso.

## «¿Por qué necesita runFullTrust y cómo se usará en el producto?»

Es lo que pregunta Partner Center por declarar una capacidad restringida.

> **El campo admite 500 caracteres**, comprobado a las malas: la versión larga se cortaba a mitad de
> frase. Usa la corta.

### Corta, para el formulario (493 caracteres)

```
Aplicación de escritorio Windows (WPF, .NET 10) empaquetada en MSIX: runFullTrust es lo que necesita el punto de entrada Windows.FullTrustApplication para arrancar, no para obtener privilegios. Se usa para la interfaz WPF, el icono de bandeja con su atajo global, la base SQLite local, cifrar los tokens con DPAPI, el inicio de sesión OAuth (navegador y 127.0.0.1) y abrir los adjuntos que elige el usuario. Sin elevación, sin servicios ni controladores, sin tocar datos de otras aplicaciones.
```

### Corta en inglés (491 caracteres)

```
sOC Task Manager is a Windows desktop app (WPF, .NET 10) packaged as MSIX: runFullTrust is what the Windows.FullTrustApplication entry point needs in order to start, not a way to gain privileges. It is used for the WPF interface, the tray icon and its global shortcut, the local SQLite database, encrypting session tokens with DPAPI, OAuth sign-in (system browser and 127.0.0.1) and opening the attachments the user picks. No elevation, no services or drivers, no access to other apps' data.
```

### Larga, por si hace falta ampliarla en una respuesta a certificación

### Español

```
sOC Task Manager es una aplicación de escritorio clásica de Windows (.NET 10 con WPF) empaquetada en
MSIX. runFullTrust es la capacidad que necesita el punto de entrada Windows.FullTrustApplication
para poder ejecutarse: sin ella el paquete no arranca. No se declara para obtener privilegios
adicionales. La aplicación no solicita elevación, no instala servicios ni controladores, no accede a
los datos de otras aplicaciones y no recorre el sistema de archivos.

En el producto se usa para:

- Presentar la interfaz de escritorio (WPF), con icono en el área de notificación y un atajo de
  teclado global (RegisterHotKey) que abre el panel rápido sin necesidad de la ventana principal.
- Guardar las tareas en una base de datos SQLite local, en la carpeta de datos del propio usuario,
  a través de una biblioteca nativa (e_sqlite3).
- Cifrar en disco los tokens de la sesión con DPAPI
  (System.Security.Cryptography.ProtectedData), para no dejarlos en claro.
- Iniciar sesión con Google o Microsoft: abre el navegador del sistema y escucha una única respuesta
  en 127.0.0.1, que es el flujo que exige OAuth 2.0 con PKCE en aplicaciones de escritorio.
- Adjuntar archivos que el usuario elige en el diálogo estándar de apertura, y abrir un enlace o un
  archivo adjunto con la aplicación predeterminada del sistema.
- Ajustar el color de la barra de título al tema claro u oscuro (DwmSetWindowAttribute).

Todo ello se ejecuta en el contexto del usuario que abre la aplicación. No se leen ni se modifican
archivos que el usuario no haya elegido expresamente.
```

### English

```
sOC Task Manager is a classic Windows desktop application (.NET 10 with WPF) packaged as MSIX.
runFullTrust is the capability required by the Windows.FullTrustApplication entry point: without it
the package does not start. It is not declared to gain additional privileges. The app never requests
elevation, installs no services or drivers, does not access other applications' data and does not
scan the file system.

In the product it is used to:

- Show the desktop (WPF) user interface, with a notification-area icon and a global keyboard
  shortcut (RegisterHotKey) that opens the quick panel without the main window.
- Store tasks in a local SQLite database inside the user's own data folder, through a native library
  (e_sqlite3).
- Encrypt the session tokens on disk with DPAPI
  (System.Security.Cryptography.ProtectedData) instead of leaving them readable.
- Sign in with Google or Microsoft: it opens the system browser and listens for a single response on
  127.0.0.1, which is the flow OAuth 2.0 with PKCE requires for desktop apps.
- Attach files the user picks in the standard open dialog, and open a link or an attachment with the
  system's default application.
- Match the title bar colour to the light or dark theme (DwmSetWindowAttribute).

All of it runs in the context of the user who opens the app. No file is read or modified unless the
user chose it.
```

### Un cabo suelto que deja el empaquetado

El ajuste «iniciar con Windows» escribe hoy en la clave `Run` del usuario en el registro
(`AutoStart.cs`). **Dentro de un MSIX el registro está virtualizado**, así que esa entrada puede no
sobrevivir. La forma correcta en un paquete es la extensión `windows.startupTask` del manifiesto. No
bloquea el envío —la aplicación funciona igual— pero conviene comprobarlo al instalar el paquete y,
si no arranca sola, cambiarlo por la extensión.

## Cuenta obligatoria: lo que pregunta la revisión

La aplicación exige entrar con Google o Microsoft. La revisión de la Store pide, cuando hay cuenta
obligatoria, **credenciales de prueba** para poder revisarla. Hay que darles una cuenta de Google o
de Microsoft de prueba en las notas para la certificación, o no podrán pasar de la primera pantalla.
