# Ficha de Google Play — Task Manager

`com.socratic.taskmanager` · pista de pruebas cerradas (`alpha`).

Los textos de aquí son los que sube `tools/publicar-ficha-play.ps1`; si se cambian, se vuelve a
ejecutar y quedan iguales en el repositorio y en la consola. Los límites de Play son duros: título
30 caracteres, descripción breve 80 y descripción completa 4000.

El nombre es **Task Manager**, el mismo que se ve en el aparato (`ApplicationTitle` del csproj). En
la Microsoft Store es «sOC Task Manager» porque ahí ese es el nombre reservado de la aplicación, y
la tienda lo comprueba; en Play no hay tal reserva y manda lo que dice el móvil.

---

## es-ES (idioma por defecto)

### Título

```
Task Manager
```

### Descripción breve

```
Tus tareas en el móvil y en el ordenador, con la misma cuenta
```

### Descripción completa

```
Task Manager es una lista de tareas que de verdad está en todos tus aparatos: escribes una tarea en el móvil y aparece en el ordenador, y al revés.

Cada tarea lleva lo que necesite: lista, etiquetas, fecha de inicio y de vencimiento, repetición, pasos, enlaces y ficheros adjuntos. Las que no pueden esperar se anclan y se quedan arriba del todo, con la fecha más cercana primero.

Los filtros ordenan por ti: todas, ancladas, pendientes, hechas, vencidas, las que empiezan antes o después de hoy, las que vencen antes o después de hoy, o las que no llevan ninguna etiqueta. El buscador mira en el título, las notas, las etiquetas, los pasos y los adjuntos, así que encuentras una tarea por lo que recuerdes de ella.

Una tarea se parte en pasos y los pasos se arrastran hasta dejarlos en el orden en que los vas a hacer. La repetición puede ser diaria, semanal en los días que elijas, mensual un día del mes o anual un mes y un día. Las fechas traen avisos de lo que queda pendiente, y un aviso se puede posponer.

Marca varias tareas a la vez y hazlas, devuélvelas a pendientes, ánclalas, etiquétalas, muévelas a otra lista o bórralas de una vez.

GRUPOS COMPARTIDOS
Un grupo es un espacio con sus propias listas que se comparte con otras personas. Se invita enseñando un código QR: el otro aparato lo escanea con la propia aplicación y entra, sin teclear códigos ni claves.

TU TEXTO NO SE GUARDA EN CLARO
Lo que escribes se cifra en tu dispositivo antes de salir y se guarda cifrado en el servidor. Sin anuncios, sin rastreadores y sin analítica.

POR QUÉ HACE FALTA UNA CUENTA
Se entra con una cuenta que ya tienes, de Google o de Microsoft. No creamos ninguna cuenta y nunca vemos tu contraseña. La cuenta es lo que hace posible la sincronización: sin ella no hay forma de saber que dos aparatos son de la misma persona.

También hay versión para Windows, con icono en la bandeja del sistema.

En castellano y en inglés, con modo claro y oscuro.
```

---

## en-US

### Título

```
Task Manager
```

### Descripción breve

```
Your tasks on your phone and on your PC, with the same account
```

### Descripción completa

```
Task Manager is a to-do list that really is on all your devices: write a task on your phone and it shows up on your computer, and the other way round.

Every task can carry whatever it needs: a list, tags, a start date and a due date, repetition, steps, links and attached files. Pin the ones that cannot wait and they stay at the top, with the closest due date first.

Filters do the sorting for you: everything, pinned, pending, done, overdue, starting before or after today, due before or after today, or tasks with no tag at all. Search reads titles, notes, tags, steps and attachments, so you can find a task by whatever you remember about it.

Break a task into steps and drag them into the order you will do them in. Repeats can be daily, weekly on the days you pick, monthly on a day of the month, or yearly on a month and a day. Dates come with reminders for what is pending, and a reminder can be put off for later.

Select several tasks at once and mark them done, send them back to pending, pin them, tag them, move them to another list or delete them in one go.

SHARED GROUPS
A group is a space with its own lists, shared with other people. You invite someone by showing a QR code: the other device scans it with the app itself and joins, with no codes or keys to type.

YOUR TEXT IS NOT STORED IN READABLE FORM
What you type is encrypted on your own device before it goes anywhere, and it is stored encrypted on the server. No ads, no trackers, no analytics.

WHY AN ACCOUNT IS REQUIRED
You sign in with an account you already have, from Google or Microsoft. We do not create an account for you and we never see your password. Signing in is what makes syncing work: without it, there is no way to tell that two devices belong to the same person.

There is a Windows version too, with a system tray icon.

In English and Spanish, with light and dark mode.
```

---

## Imágenes

| Qué | Fichero | Estado |
|---|---|---|
| Icono 512×512 | `icon_512.png` | Hecho. Dibujado con las mismas formas y colores que `appicon.svg`, no escalado de uno pequeño. |
| Gráfico destacado 1024×500 | `feature_graphic.png` | Hecho. |
| Capturas de teléfono | `capturas/es-ES/` y `capturas/en-US/` | Hechas: cinco por idioma. |

Las capturas salen de la **compilación de demostración** (`-p:Demo=true`, y `-p:DemoLang=en` para el
juego en inglés): entra sin cuenta, siembra tareas inventadas y usa otra base de datos, así que no
se enseña ni un dato real. Se suben en el orden del nombre del fichero.

## Lo que Play pide aparte de esto

Nada de lo de abajo se puede hacer por API; es el formulario de la consola:

- Clasificación del contenido (cuestionario).
- Seguridad de los datos: hay cuenta, hay sincronización y hay cifrado en tránsito y en reposo.
- Público objetivo y anuncios (no hay).
- Política de privacidad: la URL, **ya actualizada**, que diga que hay cuenta y servidor.
