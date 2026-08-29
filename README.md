# Task Manager

Gestor de tareas diarias con **listas por grupo**, **desglose de objetivos con IA local** y
**celebración al completar**. Dos aplicaciones sobre un mismo núcleo: Android (.NET MAUI) y Windows
(WPF en la bandeja del sistema).

- Qué hace → [ESPECIFICACION.md](ESPECIFICACION.md)
- Cómo está hecho → [ARQUITECTURA.md](ARQUITECTURA.md)
- Backend → [supabase/README.md](supabase/README.md)

## Estado

Fases 1 a 3 hechas: documentación, esquema SQL con RLS, núcleo compartido y las dos aplicaciones
funcionando **contra la base de datos local**. De la fase 4 está hecha la **entrada con Google**
(código completo en las dos aplicaciones); falta subir la cola de cambios y el Realtime.

| Proyecto | Qué es | Compila |
|---|---|---|
| `TaskManager.Core` | Modelo, SQLite, XP y niveles, desglose con IA, entrada con Google, contrato de sincronización | sí |
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
- Escribir + Intro añade la tarea; la varita la desglosa en micro-pasos.
- `--tray` arranca escondido: es lo que usa el inicio con Windows.
- Datos en `%LOCALAPPDATA%\Socratic\TaskManager\taskmanager.db3`.

## Cuenta

El usuario entra con **Google a través de Supabase Auth** (PKCE): en Android por Chrome Custom Tabs,
en Windows por el navegador del sistema contra un servidor local. Los tokens se guardan en el
almacén seguro de Android y con DPAPI en Windows — nunca en claro.

Entrar es lo que guarda su usuario y le permite tener listas de **varios grupos** a la vez, además
de las privadas. Lo hecho antes de entrar (tareas, XP y rachas) se traspasa a la cuenta la primera
vez, así que no se pierde nada.

Hace falta configurar el proveedor y las dos redirecciones en Supabase y Google Cloud: los pasos
están en [supabase/README.md](supabase/README.md). Sin esa configuración la aplicación funciona en
local y lo dice, en vez de dejar un botón que falla.

## Pasos Mágicos (IA local)

El desglose intenta, por este orden:

1. Un servidor **local** compatible con la API de OpenAI — Ollama, `llama.cpp --server`, LM Studio —
   en la dirección de Ajustes (por defecto `http://localhost:11434`, modelo `qwen2.5:3b-instruct`,
   Apache-2.0).
2. Si no responde, **plantillas por dominio** dentro de la propia aplicación: sin red, sin modelo y
   sin espera.

En Android lo normal es apuntar al PC de la LAN; correr el modelo dentro del móvil no da la calidad
ni el tiempo que pide la especificación (ver [ARQUITECTURA.md § 5](ARQUITECTURA.md)).

## Lo que falta

- Fase 4: proyecto de Supabase con el proveedor de Google configurado, subida de la cola y Realtime.
- Fase 5: widget de Android, sonidos de celebración, temas desbloqueables y reacciones de grupo.
- Fase 6: ficha de Play Console, capturas y subida a `alpha`.

MIT · Copyright © 2026 Socratic
