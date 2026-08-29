-- Task Manager — Row Level Security y funciones de acceso por clave compartida.
-- Ejecutar despues de 01_schema.sql.
--
-- Modelo: la clave compartida NO viaja en cada peticion. Se canjea una vez con join_group()
-- por una fila en group_members, y a partir de ahi manda el JWT del usuario.
-- El razonamiento esta en ../ARQUITECTURA.md, seccion 4.

alter table public.profiles      enable row level security;
alter table public.groups        enable row level security;
alter table public.group_members enable row level security;
alter table public.task_lists    enable row level security;
alter table public.tasks         enable row level security;
alter table public.task_steps    enable row level security;
alter table public.xp_events     enable row level security;
alter table public.reactions     enable row level security;

-- ---------------------------------------------------------------------------
-- Ayudas. security definer para poder consultar group_members sin recursion de politicas.
-- ---------------------------------------------------------------------------

create or replace function public.is_member(p_group uuid)
returns boolean
language sql
security definer
set search_path = public
stable
as $$
    select exists (
        select 1 from public.group_members
        where group_id = p_group and user_id = auth.uid()
    );
$$;

-- Puede tocar la lista: es suya (privada) o es miembro del grupo.
create or replace function public.can_use_list(p_list uuid)
returns boolean
language sql
security definer
set search_path = public
stable
as $$
    select exists (
        select 1 from public.task_lists l
        where l.id = p_list
          and (
                (l.group_id is null and l.owner_id = auth.uid())
             or (l.group_id is not null and public.is_member(l.group_id))
          )
    );
$$;

create or replace function public.can_use_task(p_task uuid)
returns boolean
language sql
security definer
set search_path = public
stable
as $$
    select exists (
        select 1 from public.tasks t
        where t.id = p_task and public.can_use_list(t.list_id)
    );
$$;

-- ---------------------------------------------------------------------------
-- profiles: cada uno ve el suyo y el de sus companeros de grupo (el Tablon y la celebracion
-- grupal necesitan poner nombre y cara a quien completa una tarea). Solo se edita el propio.
-- ---------------------------------------------------------------------------

drop policy if exists profiles_select on public.profiles;
create policy profiles_select on public.profiles
    for select using (
        id = auth.uid()
        or exists (
            select 1
            from public.group_members mine
            join public.group_members theirs on theirs.group_id = mine.group_id
            where mine.user_id = auth.uid() and theirs.user_id = profiles.id
        )
    );

drop policy if exists profiles_update on public.profiles;
create policy profiles_update on public.profiles
    for update using (id = auth.uid()) with check (id = auth.uid());

drop policy if exists profiles_insert on public.profiles;
create policy profiles_insert on public.profiles
    for insert with check (id = auth.uid());

-- ---------------------------------------------------------------------------
-- groups: se ven los grupos de los que uno es miembro. key_hash no lo lee nadie:
-- ninguna politica da acceso a la tabla fuera de estas, y las funciones que la
-- consultan son security definer.
-- ---------------------------------------------------------------------------

drop policy if exists groups_select on public.groups;
create policy groups_select on public.groups
    for select using (public.is_member(id));

drop policy if exists groups_insert on public.groups;
create policy groups_insert on public.groups
    for insert with check (owner_id = auth.uid());

drop policy if exists groups_update on public.groups;
create policy groups_update on public.groups
    for update using (owner_id = auth.uid()) with check (owner_id = auth.uid());

drop policy if exists groups_delete on public.groups;
create policy groups_delete on public.groups
    for delete using (owner_id = auth.uid());

-- ---------------------------------------------------------------------------
-- group_members: cada uno ve a los miembros de sus grupos. Alta SOLO por join_group().
-- ---------------------------------------------------------------------------

drop policy if exists group_members_select on public.group_members;
create policy group_members_select on public.group_members
    for select using (public.is_member(group_id));

drop policy if exists group_members_update on public.group_members;
create policy group_members_update on public.group_members
    for update using (user_id = auth.uid()) with check (user_id = auth.uid());

-- Salirse del grupo, o el propietario echar a alguien.
drop policy if exists group_members_delete on public.group_members;
create policy group_members_delete on public.group_members
    for delete using (
        user_id = auth.uid()
        or exists (select 1 from public.groups g where g.id = group_id and g.owner_id = auth.uid())
    );

-- ---------------------------------------------------------------------------
-- task_lists / tasks / task_steps
-- ---------------------------------------------------------------------------

drop policy if exists task_lists_all on public.task_lists;
create policy task_lists_all on public.task_lists
    for all
    using (
        (group_id is null and owner_id = auth.uid())
        or (group_id is not null and public.is_member(group_id))
    )
    with check (
        (group_id is null and owner_id = auth.uid())
        or (group_id is not null and public.is_member(group_id))
    );

drop policy if exists tasks_all on public.tasks;
create policy tasks_all on public.tasks
    for all
    using (public.can_use_list(list_id))
    with check (public.can_use_list(list_id));

drop policy if exists task_steps_all on public.task_steps;
create policy task_steps_all on public.task_steps
    for all
    using (public.can_use_task(task_id))
    with check (public.can_use_task(task_id));

-- ---------------------------------------------------------------------------
-- xp_events: cada uno escribe los suyos; se leen los propios y los del grupo
-- (el Tablon del Gremio necesita ver el progreso de los companeros).
-- ---------------------------------------------------------------------------

drop policy if exists xp_events_select on public.xp_events;
create policy xp_events_select on public.xp_events
    for select using (
        user_id = auth.uid()
        or (group_id is not null and public.is_member(group_id))
    );

drop policy if exists xp_events_insert on public.xp_events;
create policy xp_events_insert on public.xp_events
    for insert with check (user_id = auth.uid());

-- ---------------------------------------------------------------------------
-- reactions: aplausos y emojis sobre tareas visibles.
-- ---------------------------------------------------------------------------

drop policy if exists reactions_select on public.reactions;
create policy reactions_select on public.reactions
    for select using (public.can_use_task(task_id));

drop policy if exists reactions_insert on public.reactions;
create policy reactions_insert on public.reactions
    for insert with check (user_id = auth.uid() and public.can_use_task(task_id));

drop policy if exists reactions_delete on public.reactions;
create policy reactions_delete on public.reactions
    for delete using (user_id = auth.uid());

-- ---------------------------------------------------------------------------
-- Alta y entrada a grupos
-- ---------------------------------------------------------------------------

-- Crea el grupo con su clave compartida y mete al creador como owner.
-- Devuelve el join_code para que se pueda dictar.
create or replace function public.create_group(p_name text, p_key text, p_display_name text default '')
returns table (group_id uuid, join_code text)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_id   uuid;
    v_code text;
begin
    if auth.uid() is null then
        raise exception 'auth required';
    end if;
    if p_key is null or length(p_key) < 6 then
        raise exception 'weak key';
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

    insert into public.groups (name, join_code, key_hash, owner_id)
    values (p_name, v_code, crypt(p_key, gen_salt('bf')), auth.uid())
    returning id into v_id;

    insert into public.group_members (group_id, user_id, display_name, role)
    values (v_id, auth.uid(), coalesce(nullif(p_display_name, ''), 'Yo'), 'owner');

    return query select v_id, v_code;
end;
$$;

-- Unico camino de entrada a un grupo: hay que acertar codigo + clave compartida.
-- Devuelve el id del grupo; error generico si algo no cuadra (no distingue codigo de clave).
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
    values (v_group.id, auth.uid(), coalesce(nullif(p_display_name, ''), 'Invitado'), 'member')
    on conflict (group_id, user_id) do nothing;

    return v_group.id;
end;
$$;

-- Rotar la clave compartida sin cambiar el join_code. Solo el propietario.
create or replace function public.rotate_group_key(p_group uuid, p_new_key text)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
    if not exists (select 1 from public.groups where id = p_group and owner_id = auth.uid()) then
        raise exception 'not the owner';
    end if;
    if p_new_key is null or length(p_new_key) < 6 then
        raise exception 'weak key';
    end if;

    update public.groups set key_hash = crypt(p_new_key, gen_salt('bf')) where id = p_group;
end;
$$;

revoke all on function public.create_group(text, text, text)      from public;
revoke all on function public.join_group(text, text, text)        from public;
revoke all on function public.rotate_group_key(uuid, text)        from public;
grant execute on function public.create_group(text, text, text)   to authenticated;
grant execute on function public.join_group(text, text, text)     to authenticated;
grant execute on function public.rotate_group_key(uuid, text)     to authenticated;
