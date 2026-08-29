-- Task Manager — esquema PostgreSQL (Supabase)
-- Ejecutar antes que 02_rls.sql. Idempotente: se puede relanzar.

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------------------
-- Perfil del usuario (entrada con Google)
-- ---------------------------------------------------------------------------

-- auth.users guarda la identidad; aqui queda lo que ven los companeros de grupo.
create table if not exists public.profiles (
    id           uuid        primary key references auth.users (id) on delete cascade,
    display_name text        not null default '',
    email        text        not null default '',
    avatar_url   text        not null default '',
    updated_at   timestamptz not null default now()
);

-- Alta automatica al entrar por primera vez: el cliente no tiene que acordarse de crearla, y los
-- datos salen de lo que devuelve Google (nombre y foto van en raw_user_meta_data).
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    insert into public.profiles (id, display_name, email, avatar_url)
    values (
        new.id,
        coalesce(new.raw_user_meta_data ->> 'full_name', new.raw_user_meta_data ->> 'name', new.email, ''),
        coalesce(new.email, ''),
        coalesce(new.raw_user_meta_data ->> 'avatar_url', ''))
    on conflict (id) do update
        set display_name = excluded.display_name,
            email        = excluded.email,
            avatar_url   = excluded.avatar_url,
            updated_at   = now();

    return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
    after insert or update on auth.users
    for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------
-- Grupos y pertenencia
-- ---------------------------------------------------------------------------

create table if not exists public.groups (
    id          uuid primary key default gen_random_uuid(),
    name        text        not null check (length(trim(name)) between 1 and 60),
    -- Codigo publico, corto y comodo de dictar. No da acceso por si solo.
    join_code   text        not null unique check (join_code ~ '^[A-Z0-9]{6}$'),
    -- Clave compartida: solo el hash bcrypt. Nadie la lee (ver 02_rls.sql).
    key_hash    text        not null,
    owner_id    uuid        not null references auth.users (id) on delete cascade,
    created_at  timestamptz not null default now()
);

create table if not exists public.group_members (
    group_id     uuid        not null references public.groups (id) on delete cascade,
    user_id      uuid        not null references auth.users (id) on delete cascade,
    display_name text        not null default '',
    role         text        not null default 'member' check (role in ('owner', 'member')),
    joined_at    timestamptz not null default now(),
    primary key (group_id, user_id)
);

create index if not exists group_members_user_idx on public.group_members (user_id);

-- ---------------------------------------------------------------------------
-- Listas y tareas
-- ---------------------------------------------------------------------------

-- group_id nulo => lista privada del owner_id.
create table if not exists public.task_lists (
    id         uuid        primary key default gen_random_uuid(),
    group_id   uuid        references public.groups (id) on delete cascade,
    owner_id   uuid        not null references auth.users (id) on delete cascade,
    name       text        not null check (length(trim(name)) between 1 and 60),
    icon       text        not null default 'ic_list',
    sort_order int         not null default 0,
    updated_at timestamptz not null default now(),
    deleted    boolean     not null default false
);

create index if not exists task_lists_group_idx on public.task_lists (group_id);
create index if not exists task_lists_owner_idx on public.task_lists (owner_id);

create table if not exists public.tasks (
    id         uuid        primary key default gen_random_uuid(),
    list_id    uuid        not null references public.task_lists (id) on delete cascade,
    title      text        not null check (length(trim(title)) between 1 and 200),
    notes      text        not null default '',
    -- Contexto que acota el desglose de la tarea. Va cifrado como el resto del contenido.
    context    text        not null default '',
    is_done    boolean     not null default false,
    done_at    timestamptz,
    done_by    uuid        references auth.users (id) on delete set null,
    -- "Mi Dia" es una fecha, no una lista: la tarea esta en Mi Dia si my_day_on = current_date.
    my_day_on  date,
    due_at     timestamptz,
    created_by uuid        not null references auth.users (id) on delete cascade,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    deleted    boolean     not null default false
);

create index if not exists tasks_list_idx    on public.tasks (list_id);
create index if not exists tasks_my_day_idx  on public.tasks (my_day_on) where deleted = false;

create table if not exists public.task_steps (
    id         uuid        primary key default gen_random_uuid(),
    task_id    uuid        not null references public.tasks (id) on delete cascade,
    title      text        not null check (length(trim(title)) between 1 and 200),
    is_done    boolean     not null default false,
    sort_order int         not null default 0,
    source     text        not null default 'manual' check (source in ('manual', 'ai')),
    updated_at timestamptz not null default now(),
    deleted    boolean     not null default false
);

create index if not exists task_steps_task_idx on public.task_steps (task_id);

-- ---------------------------------------------------------------------------
-- Gamificacion
-- ---------------------------------------------------------------------------

create table if not exists public.xp_events (
    id         uuid        primary key default gen_random_uuid(),
    user_id    uuid        not null references auth.users (id) on delete cascade,
    group_id   uuid        references public.groups (id) on delete cascade,
    task_id    uuid        references public.tasks (id) on delete set null,
    amount     int         not null check (amount > 0),
    kind       text        not null check (kind in ('task', 'step', 'breakdown', 'bonus')),
    combo      numeric(3,1) not null default 1.0,
    created_at timestamptz not null default now()
);

create index if not exists xp_events_user_idx  on public.xp_events (user_id, created_at desc);
create index if not exists xp_events_group_idx on public.xp_events (group_id, created_at desc);

-- Reacciones rapidas a la tarea que acaba de completar un companero.
create table if not exists public.reactions (
    id         uuid        primary key default gen_random_uuid(),
    task_id    uuid        not null references public.tasks (id) on delete cascade,
    user_id    uuid        not null references auth.users (id) on delete cascade,
    emoji      text        not null check (length(emoji) between 1 and 8),
    created_at timestamptz not null default now(),
    unique (task_id, user_id, emoji)
);

-- ---------------------------------------------------------------------------
-- updated_at automatico (la sincronizacion resuelve conflictos por este campo)
-- ---------------------------------------------------------------------------

create or replace function public.touch_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at := now();
    return new;
end;
$$;

do $$
declare
    t text;
begin
    foreach t in array array['task_lists', 'tasks', 'task_steps'] loop
        execute format('drop trigger if exists %I_touch on public.%I', t, t);
        execute format(
            'create trigger %I_touch before update on public.%I
             for each row execute function public.touch_updated_at()', t, t);
    end loop;
end;
$$;

-- ---------------------------------------------------------------------------
-- Realtime: las listas compartidas se ven cambiar en directo
-- ---------------------------------------------------------------------------

do $$
begin
    if exists (select 1 from pg_publication where pubname = 'supabase_realtime') then
        alter publication supabase_realtime add table public.tasks;
        alter publication supabase_realtime add table public.task_steps;
        alter publication supabase_realtime add table public.task_lists;
        alter publication supabase_realtime add table public.reactions;
        alter publication supabase_realtime add table public.xp_events;
    end if;
exception
    when duplicate_object then null;
end;
$$;
