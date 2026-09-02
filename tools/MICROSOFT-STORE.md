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

## Cuenta obligatoria: lo que pregunta la revisión

La aplicación exige entrar con Google o Microsoft. La revisión de la Store pide, cuando hay cuenta
obligatoria, **credenciales de prueba** para poder revisarla. Hay que darles una cuenta de Google o
de Microsoft de prueba en las notas para la certificación, o no podrán pasar de la primera pantalla.
