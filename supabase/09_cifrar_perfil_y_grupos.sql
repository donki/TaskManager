-- ---------------------------------------------------------------------------
-- Que el texto de `profiles`, `groups` y `group_members` tampoco quede en claro
--
-- El cifrado (ver TextCipher) lo hace el cliente: cifra al subir y descifra al
-- bajar. Estas tres tablas se le escapaban porque NO las escribe el cliente,
-- las escribe el propio servidor —un disparador y dos funciones—, y ahi no hay
-- forma de cifrar con el mismo esquema sin reescribir AES-GCM y PBKDF2 en
-- PL/pgSQL. Asi que se le quita al servidor la parte que escribe texto y se le
-- pasa al cliente, que ya sabe cifrar.
-- ---------------------------------------------------------------------------


-- ---------------------------------------------------------------------------
-- profiles: el disparador crea la fila, ya no la rellena
-- ---------------------------------------------------------------------------
--
-- `handle_new_user` copiaba el nombre, el correo y la foto de lo que devuelve
-- Google, en claro, y ademas lo REESCRIBIA en cada entrada — asi que aunque el
-- cliente los cifrara, la siguiente sesion los devolvia a texto plano.
--
-- Ahora solo se asegura de que la fila exista (las claves ajenas de otras
-- tablas cuelgan de ella). El contenido lo sube el cliente cifrado.

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    insert into public.profiles (id)
    values (new.id)
    on conflict (id) do nothing;

    return new;
end;
$$;


-- ---------------------------------------------------------------------------
-- groups: el nombre se cifra con el identificador del grupo
-- ---------------------------------------------------------------------------
--
-- Con el del grupo y no con el de quien lo crea: si no, los demas miembros no
-- podrian leer el nombre del grupo al que pertenecen.
--
-- De ahi el `p_id`: el identificador tiene que existir ANTES de cifrar, y antes
-- lo generaba el propio insert (`gen_random_uuid()`). Ahora lo trae el cliente,
-- que con el cifra el nombre y de paso guarda el grupo local con el mismo
-- identificador que el del servidor — antes eran dos distintos.
--
-- Lo que NO se cifra:
--   * `join_code`, porque es por donde se busca el grupo al entrar (y no es
--     texto del usuario: lo genera el servidor).
--   * `key_hash`, que es un bcrypt y tiene que poder compararse con crypt().

alter table public.groups drop constraint if exists groups_name_check;
alter table public.groups
    add constraint groups_name_check check (length(btrim(name)) >= 1);

drop function if exists public.create_group(text, text, text);

create or replace function public.create_group(
    p_id           uuid,
    p_name         text,
    p_key          text,
    p_display_name text default '')
returns table (group_id uuid, join_code text)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_code text;
begin
    if auth.uid() is null then
        raise exception 'auth required';
    end if;
    if p_key is null or length(p_key) < 6 then
        raise exception 'weak key';
    end if;
    if p_id is null then
        raise exception 'group id required';
    end if;

    -- Codigo de 6 caracteres sin ambiguos (sin O/0/I/1), reintentando ante colision.
    loop
        v_code := (
            select string_agg(substr('ABCDEFGHJKLMNPQRSTUVWXYZ23456789',
                                     1 + floor(random() * 32)::int, 1), '')
            from generate_series(1, 6)
        );
        exit when not exists (select 1 from public.groups g where g.join_code = v_code);
    end loop;

    insert into public.groups (id, name, join_code, key_hash, owner_id)
    values (p_id, p_name, v_code, crypt(p_key, gen_salt('bf')), auth.uid());

    -- Sin apodo por defecto: lo que llegue vacio lo rellena el cliente cifrado.
    insert into public.group_members (group_id, user_id, display_name, role)
    values (p_id, auth.uid(), coalesce(p_display_name, ''), 'owner');

    return query select p_id, v_code;
end;
$$;


-- ---------------------------------------------------------------------------
-- group_members: el apodo lo escribe el cliente, ya cifrado
-- ---------------------------------------------------------------------------
--
-- Al entrar solo se conoce el codigo, no el identificador del grupo, asi que no
-- se puede cifrar el apodo antes de la llamada: `join_group` mete la fila con el
-- apodo vacio y el cliente lo escribe cifrado en cuanto sabe a que grupo ha
-- entrado (la politica `group_members_update` ya deja a cada uno tocar su fila).
--
-- `role` se queda como esta: es 'owner' o 'member', no lo escribe nadie, y hay
-- un CHECK y politicas que lo miran.

create or replace function public.join_group(p_code text, p_key text, p_display_name text default '')
returns uuid
language plpgsql
security definer
set search_path = public
as $$
declare
    v_group public.groups%rowtype;
begin
    if auth.uid() is null then
        raise exception 'auth required';
    end if;

    select * into v_group from public.groups where join_code = upper(trim(p_code));

    -- El crypt se ejecuta siempre (aunque no exista el grupo) para no filtrar por tiempo
    -- que codigos existen.
    if v_group.id is null then
        perform crypt(coalesce(p_key, ''), gen_salt('bf'));
        raise exception 'invalid code or key';
    end if;

    if v_group.key_hash <> crypt(coalesce(p_key, ''), v_group.key_hash) then
        raise exception 'invalid code or key';
    end if;

    insert into public.group_members (group_id, user_id, display_name, role)
    values (v_group.id, auth.uid(), coalesce(p_display_name, ''), 'member')
    on conflict (group_id, user_id) do nothing;

    return v_group.id;
end;
$$;


revoke all    on function public.create_group(uuid, text, text, text) from public;
grant execute on function public.create_group(uuid, text, text, text) to authenticated;
