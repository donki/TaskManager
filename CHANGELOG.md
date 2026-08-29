# Changelog — Task Manager

Formato de versión `AAAA.MM.DD.N` (constitución Mobile 3).

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
