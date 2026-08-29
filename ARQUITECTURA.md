# Arquitectura — Task Manager

> El **qué** está en [ESPECIFICACION.md](ESPECIFICACION.md). Este documento fija el **cómo**:
> proyectos, modelo de datos, seguridad, sincronización, IA y fases de construcción.
>
> Cumple la [Constitución Mobile](../CONSTITUCION-MOBILE.md): carpeta propia dentro de `Mobile/`,
> firma compartida (`..\..\Shared\signing.props`), API objetivo 36, MIT y monetizable, botones con
> iconos y sistema de diseño índigo.

## 1. Reparto en proyectos

```
TaskManager/
├── TaskManager.Core/      net10.0                    — modelo, datos locales, XP, IA, sincronización
├── TaskManager.Mobile/    net10.0-android36.0 (MAUI) — app Android
└── TaskManager.Desktop/   net10.0-windows (WPF)      — bandeja, flyout y atajo global
```

**Por qué dos apps y no un MAUI único.** El escritorio de esta aplicación *es* un icono de bandeja
con una ventana flotante y un atajo global sobre pantalla completa. MAUI-Windows no da acceso
razonable a nada de eso (NotifyIcon, `RegisterHotKey`, ventana sin marco que se activa sin robar el
foco al juego de delante). WPF sí, sin dependencias externas — `NotifyIcon` de WinForms y P/Invoke —,
lo que además respeta la regla MIT/monetizable. Todo lo que no es pintar pantalla vive en
`TaskManager.Core` y lo comparten las dos.

## 2. Modelo de datos

Mismas entidades en SQLite (local) y PostgreSQL (Supabase); el esquema SQL está en
[supabase/01_schema.sql](supabase/01_schema.sql).

| Entidad | Campos relevantes |
|---|---|
| `profiles` | `id` (= `auth.users.id`), `display_name`, `email`, `avatar_url` |
| `groups` | `id`, `name`, `join_code` (público, 6 caracteres), `key_hash`, `owner_id`, `created_at` |
| `group_members` | `group_id`, `user_id`, `display_name`, `role`, `joined_at` |
| `task_lists` | `id`, `group_id` (nulo ⇒ lista **privada**), `owner_id`, `name`, `icon`, `sort_order` |
| `tasks` | `id`, `list_id`, `title`, `notes`, `is_done`, `done_at`, `done_by`, `my_day_on`, `due_at`, `created_by`, `updated_at`, `deleted` |
| `task_steps` | `id`, `task_id`, `title`, `is_done`, `sort_order`, `source` (`ai` / `manual`) |
| `xp_events` | `id`, `user_id`, `group_id`, `task_id`, `amount`, `kind`, `combo`, `created_at` |
| `reactions` | `id`, `task_id`, `user_id`, `emoji`, `created_at` |

Decisiones:

- **Una sola tabla `task_lists` para privadas y de grupo.** `group_id IS NULL` es la lista privada;
  evita duplicar toda la lógica de tareas por partida doble.
- **"Mi Día" es un campo, no una lista.** `tasks.my_day_on DATE`: una tarea está en Mi Día si
  `my_day_on = CURRENT_DATE`. El reinicio de medianoche no borra nada ni necesita un proceso
  programado — al día siguiente ya no cuadra la fecha, y la tarea sigue viva en su lista.
- **Borrado lógico** (`deleted`) porque hay que sincronizar bajas entre dispositivos offline.
- **Última escritura gana** por `updated_at` (UTC). Suficiente para listas de tareas y ahorra el
  coste de un CRDT: los conflictos reales son raros y de baja consecuencia.

## 3. Datos locales y sincronización

- **SQLite** (`sqlite-net-pcl`, MIT) es la fuente de verdad **en el dispositivo**. La interfaz
  escribe y lee siempre en local: el offline no es un modo aparte, es el funcionamiento normal.
- Cada cambio deja una fila en la **cola de salida** (`sync_queue`). `ISyncService` la vacía contra
  Supabase cuando hay red y aplica lo que llega (Realtime, o una lectura completa al arrancar) con la
  regla de `updated_at`.
- **Realtime** en las listas de grupo abiertas: es lo que hace visible la celebración grupal.
- Si Supabase no está configurado (URL vacía), la app funciona **entera en local**. Es lo que permite
  desarrollar y probar sin backend.

## 4. Cuenta: entrada con Google

La identidad la pone **Supabase Auth con proveedor Google**. La aplicación no habla con Google
directamente: pide a Supabase que abra el consentimiento y recoge la sesión de vuelta.

**Flujo (PKCE, el mismo en las dos aplicaciones):**

1. `SupabaseAuthService` genera un `code_verifier`, calcula su `code_challenge` (S256) y abre
   `/auth/v1/authorize?provider=google&redirect_to=…&code_challenge=…&code_challenge_method=s256`.
2. La ventana la abre el **navegador del sistema**, nunca un WebView incrustado: Google rechaza los
   WebView desde 2021. En Android es `WebAuthenticator` (Chrome Custom Tabs); en Windows, el
   navegador por defecto contra un `HttpListener` en `127.0.0.1`.
3. La vuelta trae `?code=…`. Se canjea en `/auth/v1/token?grant_type=pkce` junto con el verificador
   y se obtienen `access_token` y `refresh_token`.
4. El perfil se lee de `/auth/v1/user`; en el servidor, el disparador `handle_new_user` deja la fila
   de `profiles` al día con el nombre y la foto de Google.

**Por qué PKCE y no el flujo implícito.** En el implícito los tokens vuelven en el *fragmento* de la
URL, y el fragmento no se envía nunca al servidor: en Windows, donde la vuelta la recoge un servidor
local, no llegaría nada. Con PKCE el código viaja como parámetro de consulta y el mismo código de
núcleo sirve para el esquema propio de Android y para el `localhost` del escritorio.

**Dónde se guardan los tokens.** Nunca en la base de datos en claro si hay algo mejor:

| Plataforma | Almacén | Respaldo |
|---|---|---|
| Android | `SecureStorage` (Keystore) | tabla de ajustes, si el Keystore falla |
| Windows | DPAPI, atado a la cuenta de Windows (`tokens.dat`) | — |

**Redirecciones que hay que dar de alta** en *Authentication › URL Configuration › Redirect URLs*:

```
com.socratic.taskmanager://auth      (Android; coincide con el intent-filter)
http://127.0.0.1:*/auth/             (Windows; el puerto puede cambiar si el 53682 está ocupado)
```

**Sin cuenta.** La aplicación arranca igual y funciona en local con un identificador provisional.
Al entrar por primera vez, `TaskService.AdoptAccountAsync` reasigna tareas, autoría y XP de ese
identificador a la cuenta: entrar no puede costarle al usuario el nivel y las rachas que ya tenía.

**Un usuario, varios grupos.** La pertenencia es una fila por (grupo, usuario) en `group_members`,
sin límite: el mismo usuario tiene a la vez sus listas privadas y las listas de todos sus grupos, y
"Mi Día" las mezcla indicando de qué lista viene cada tarea.

## 5. Seguridad: RLS con clave compartida

La especificación pide que la clave compartida gobierne el acceso. **La clave no viaja en cada
petición**: se canjea una vez por pertenencia.

1. `join_group(p_code, p_key)` — función `SECURITY DEFINER`. Comprueba la clave contra `key_hash`
   (bcrypt, extensión `pgcrypto`) y, si cuadra, inserta `(auth.uid(), group_id)` en `group_members`.
   Es el único camino de entrada a un grupo.
2. Las políticas RLS de `task_lists`, `tasks`, `task_steps`, `xp_events` y `reactions` autorizan por
   **pertenencia** (`EXISTS (SELECT 1 FROM group_members ...)`) y, en las privadas, por
   `owner_id = auth.uid()`.
3. De `groups` solo se leen las filas de las que uno es miembro, y `key_hash` no lo lee nadie: solo
   lo toca la función `SECURITY DEFINER`.

**Por qué no comparar la clave dentro de la política.** Una política del tipo
`WHERE shared_key = current_setting('request.header.x-key')` obliga a que la clave en claro viaje en
cada petición y esté guardada en todos los dispositivos, y deja la puerta abierta a probar claves
contra la API con la `anon key`, que es pública por diseño. Canjeándola una vez, la clave solo se usa
al unirse, el intento pasa por una función donde se puede limitar la frecuencia, y a partir de ahí
manda el JWT del usuario. Se cumple lo pedido — sin clave no se entra — sin regalarle el grupo a
quien haga fuerza bruta.

`join_code` (visible, corto, cómodo de dictar) y clave compartida (secreta) van separados: así se
puede rotar la clave sin cambiar el código del grupo.

## 6. "Pasos Mágicos": desglose con IA local

`IBreakdownService`, dos implementaciones y una cascada:

1. **`LocalLlmBreakdownService`** — habla con un servidor local compatible con la API de OpenAI
   (Ollama, `llama.cpp --server`, LM Studio) en `http://localhost:11434`. Modelo recomendado:
   **Qwen2.5 3B Instruct** (Apache-2.0, compatible con la regla MIT/monetizable). Devuelve JSON con
   3-5 pasos. En Windows es la vía normal.
2. **`HeuristicBreakdownService`** — plantillas por dominio (mudanza, compra, limpieza, estudio,
   trámites, evento, avería…) más un desglose genérico. Sin red, sin modelo y sin latencia.

**Aviso honesto sobre Android.** Ejecutar un LLM *dentro* del móvil con la calidad y el < 1 s que
pide la especificación no es realista en gama media: los modelos que caben responden mal y tardan
segundos. Por eso en Android el orden es (a) el servidor local del PC si está accesible en la LAN
—dirección configurable en Ajustes— y, si no, (b) el desglose heurístico. La interfaz es la misma, así
que el día que se decida usar un modelo en la nube basta con añadir una tercera implementación.

## 7. Gamificación

- **XP**: tarea completada 50; micro-paso 10; desglosar con IA 15 (una vez por tarea).
- **Combos**: cada tarea completada dentro de los 90 s de la anterior sube el multiplicador
  (x1 → x1,5 → x2 → x3, tope x3).
- **Niveles**: curva cuadrática `XP(n) = 100·n·(n+1)/2`; el nivel del grupo sale de la suma de XP de
  sus miembros.
- **Rachas sin castigo**: la racha cuenta días con al menos una tarea completada y no se rompe por un
  día suelto (un día de descanso al mes se perdona). Nunca se resta XP.
- Todo el cálculo vive en `TaskManager.Core/Gamification`, sin dependencias de interfaz, para que
  móvil y escritorio celebren exactamente igual.

## 8. Fases

| Fase | Contenido | Estado |
|---|---|---|
| 1 | Documentación, esquema SQL + RLS, `TaskManager.Core` (modelo, SQLite, XP, IA) | hecho |
| 2 | App Android: Mi Día, listas privadas, grupos, tablón, celebración | esqueleto funcional |
| 3 | App Windows: bandeja, flyout, atajo global, captura rápida | esqueleto funcional |
| 4 | Supabase real: proyecto y migraciones, **entrada con Google (hecha)**, cola de sincronización y Realtime | a medias |
| 5 | Widget de Android, sonidos, temas desbloqueables, reacciones grupales | pendiente |
| 6 | Ficha de Play Console, iconos y capturas, subida a `alpha` | pendiente |

## 9. Lo que hay que decidir antes de la fase 4

- Crear el proyecto de Supabase (URL + `anon key`), activar el proveedor **Google** y dar de alta
  las dos redirecciones. Los pasos están en [supabase/README.md](supabase/README.md).
- Si el desglose por IA en Android se queda en heurístico o se acepta depender del PC o de la nube.
- Nombre de paquete definitivo: por ahora `com.socratic.taskmanager`.
