# Supabase — Task Manager

Una sola base de datos para todos los usuarios. Las migraciones están más abajo.

## Entrada con cuenta

La aplicación **habla directamente con Google y con Microsoft**, no a través de Supabase: abre el
navegador del sistema, hace PKCE contra el proveedor y de ahí saca la identidad y el nombre. Por eso
la entrada funciona con este proyecto en cualquier estado — antes, con el proveedor sin activar, el
navegador se quedaba en una página de Supabase que decía `Unsupported provider`.

Lo que sí necesita el proyecto es poder **firmar la sesión**, que es lo único que entiende la RLS. El
cliente canjea el `id_token` del proveedor:

```
POST /auth/v1/token?grant_type=id_token
{"provider": "google" | "azure", "id_token": "..."}
```

Para que ese canje funcione hay que activar cada proveedor en *Authentication › Sign In / Providers*
y pegar su **Client ID** (el de escritorio de Google y el de Entra). No hacen falta *Redirect URLs*:
con este flujo el navegador nunca vuelve a Supabase.

Si el canje falla, **la aplicación entra igual** y funciona en local, sin sincronizar; en cuanto el
proveedor quede activo, la sincronización arranca sola sin tocar el cliente.

El perfil (`profiles`) se crea y se actualiza solo con el disparador `handle_new_user` de
`01_schema.sql`.

## Migraciones

Desde el **SQL Editor**, en orden. Todas son idempotentes:

1. `01_schema.sql` — tablas, índices, `updated_at` automático y publicación de Realtime.
2. `02_rls.sql` — Row Level Security y las funciones de grupo.
3. `03_sync_columns.sql` — columnas que le faltaban a `tasks` para poder sincronizar.
4. `04_synced_at.sql` — `synced_at`: **cuándo llegó la fila al servidor**, que es distinto de cuándo
   la tocó el usuario. Sin ella, lo que un dispositivo sube por primera vez lleva su fecha original
   —de hace días—, cae por detrás del último corte del otro dispositivo y no se baja nunca.

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
