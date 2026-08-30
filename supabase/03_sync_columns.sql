-- ---------------------------------------------------------------------------
-- Columnas que le faltaban a "tasks" para poder sincronizar
--
-- El esquema inicial se escribio antes de las etiquetas, las tareas repetitivas,
-- la fecha de planificacion y el orden manual. Sin estas columnas la subida
-- fallaria con 400 (PostgREST rechaza cualquier campo que no exista) y el
-- dispositivo perderia justo lo que el usuario acaba de configurar.
--
-- Es idempotente: se puede ejecutar tantas veces como haga falta.
-- ---------------------------------------------------------------------------

alter table public.tasks add column if not exists tags        text not null default '';
alter table public.tasks add column if not exists recurrence_rule text not null default '';
alter table public.tasks add column if not exists planned_for date;
alter table public.tasks add column if not exists sort_order  int  not null default 0;
alter table public.tasks add column if not exists breakdown_rewarded boolean not null default false;

-- El orden manual se consulta siempre junto al estado, asi que van en el mismo indice.
create index if not exists tasks_order_idx on public.tasks (list_id, is_done, sort_order)
    where deleted = false;

-- Para que el "pull" incremental (updated_at > ultima vez) no acabe en un recorrido completo.
create index if not exists tasks_updated_idx      on public.tasks (updated_at);
create index if not exists task_lists_updated_idx on public.task_lists (updated_at);
create index if not exists task_steps_updated_idx on public.task_steps (updated_at);
