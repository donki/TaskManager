# Supabase — Task Manager

Dos ficheros, en este orden, desde el **SQL Editor** del proyecto de Supabase:

1. `01_schema.sql` — tablas, índices, `updated_at` automático y publicación de Realtime.
2. `02_rls.sql` — Row Level Security y las funciones `create_group`, `join_group` y
   `rotate_group_key`.

Los dos son idempotentes: se pueden relanzar sin romper nada.

## Entrada con Google (hay que configurarla a mano)

1. **Google Cloud** → *APIs y servicios › Credenciales* → **ID de cliente de OAuth 2.0** de tipo
   *Aplicación web*.
   - URI de redirección autorizado: `https://<proyecto>.supabase.co/auth/v1/callback`
   - Rellenar la *pantalla de consentimiento* (nombre, correo de soporte, dominio).
2. **Supabase** → *Authentication › Providers › Google*: activar y pegar el **Client ID** y el
   **Client Secret** de Google.
3. **Supabase** → *Authentication › URL Configuration › Redirect URLs*, las dos:

   ```
   com.socratic.taskmanager://auth
   http://127.0.0.1:*/auth/
   ```

   La primera es la de Android (coincide con el intent-filter de
   `WebAuthenticationCallbackActivity`); la segunda, la del escritorio, que levanta un servidor
   local para recoger la vuelta.
4. En la aplicación, *Ajustes* → pegar la **URL del proyecto** y la **anon key**. A partir de ahí el
   botón *Continuar con Google* funciona en las dos.

No hace falta ningún ID de cliente de Android: la app no habla con Google, habla con Supabase, y es
Supabase quien tiene registrada la aplicación en Google.

El perfil (`profiles`) se crea y se actualiza solo con el disparador `handle_new_user` de
`01_schema.sql`, con el nombre y la foto que devuelve Google.

## Cómo entra un usuario a un grupo

```
create_group('Familia', 'la-clave-de-casa')  ->  (group_id, join_code = 'K7QMDF')
join_group('K7QMDF', 'la-clave-de-casa')     ->  group_id   (inserta en group_members)
```

- El **join_code** es público y corto, pensado para dictarlo por teléfono. No da acceso por sí solo.
- La **clave compartida** solo se guarda como hash bcrypt y solo se comprueba dentro de
  `join_group`, que es `security definer`. El error no distingue entre código inexistente y clave
  incorrecta, y el `crypt` se ejecuta igualmente cuando el código no existe para no filtrar por
  tiempo qué grupos hay.
- A partir de ahí manda la pertenencia (`group_members`) más el JWT del usuario: las políticas de
  `task_lists`, `tasks`, `task_steps`, `xp_events` y `reactions` no vuelven a mirar la clave.

El porqué de este diseño, y por qué **no** se compara la clave dentro de cada política, está en
[../ARQUITECTURA.md § 4](../ARQUITECTURA.md).

## Configuración en la aplicación

La URL y la `anon key` del proyecto se guardan en los ajustes locales de cada app
(`supabase.url`, `supabase.anon_key`). Si están vacías, Task Manager funciona **entero en local**
contra SQLite — es el modo en el que está ahora mismo el esqueleto.

**La `service_role key` no se pone nunca en el cliente**: se salta toda la RLS.
