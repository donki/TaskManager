-- ---------------------------------------------------------------------------
-- "task_attachments": enlaces y ficheros colgados de una tarea
--
-- Los ficheros van DENTRO de la fila (columna `data`, bytea). Lo habitual seria
-- Supabase Storage y guardar aqui solo la referencia, pero entonces harian falta
-- dos caminos distintos —uno para las filas y otro para los ficheros—, con sus
-- dos juegos de permisos y sus dos formas de fallar. Metiendo los bytes, el
-- adjunto viaja por la misma sincronizacion que todo lo demas.
--
-- El precio es el tamaño: el cliente no deja pasar de 5 MB por adjunto
-- (TaskAttachment.MaxFileBytes) porque cada uno viaja entero en cada bajada.
--
-- Idempotente: se puede relanzar sin romper nada.
-- ---------------------------------------------------------------------------

create table if not exists public.task_attachments (
    id         uuid        primary key default gen_random_uuid(),
    task_id    uuid        not null references public.tasks (id) on delete cascade,
    kind       text        not null default 'url',
    name       text        not null default '',
    url        text        not null default '',
    data       bytea,
    sort_order int         not null default 0,
    updated_at timestamptz not null default now(),
    synced_at  timestamptz not null default now(),
    deleted    boolean     not null default false
);

create index if not exists task_attachments_task_idx   on public.task_attachments (task_id);
create index if not exists task_attachments_synced_idx on public.task_attachments (synced_at);

-- Igual que las demas: la bajada pregunta por cuando llego la fila, no por cuando
-- la toco el usuario (ver 04_synced_at.sql).
drop trigger if exists task_attachments_touch_synced on public.task_attachments;
create trigger task_attachments_touch_synced
    before insert or update on public.task_attachments
    for each row execute function public.touch_synced_at();

alter table public.task_attachments enable row level security;

-- Manda la tarea: quien puede usar la tarea puede usar sus adjuntos.
drop policy if exists task_attachments_all on public.task_attachments;
create policy task_attachments_all on public.task_attachments
    for all
    using (public.can_use_task(task_id))
    with check (public.can_use_task(task_id));
