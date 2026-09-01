-- ---------------------------------------------------------------------------
-- "synced_at": cuando llego la fila AL SERVIDOR
--
-- Hacia falta porque la bajada incremental preguntaba por updated_at, que es
-- "cuando lo toco el usuario". Al subir por primera vez lo que ya existia en un
-- dispositivo, esas filas viajan con su fecha original —de hace dias—, mas
-- antigua que el ultimo corte del otro dispositivo: se insertaban en el servidor
-- y el otro no las veia NUNCA. El trigger de updated_at no arreglaba esto porque
-- solo actuaba en UPDATE, y aquello eran INSERT.
--
-- Son dos preguntas distintas y ahora hay una columna para cada una:
--   updated_at -> cuando lo cambio el usuario. Decide quien gana en un conflicto.
--   synced_at  -> cuando llego aqui. Decide que hay que bajarse.
--
-- Idempotente: se puede relanzar sin romper nada.
-- ---------------------------------------------------------------------------

alter table public.task_lists add column if not exists synced_at timestamptz not null default now();
alter table public.tasks      add column if not exists synced_at timestamptz not null default now();
alter table public.task_steps add column if not exists synced_at timestamptz not null default now();

create or replace function public.touch_synced_at()
returns trigger
language plpgsql
as $$
begin
    new.synced_at := now();
    return new;
end;
$$;

-- En INSERT tambien, que es justo lo que faltaba.
do $$
declare
    t text;
begin
    foreach t in array array['task_lists', 'tasks', 'task_steps'] loop
        execute format('drop trigger if exists %I_touch_synced on public.%I', t, t);
        execute format(
            'create trigger %I_touch_synced before insert or update on public.%I
             for each row execute function public.touch_synced_at()', t, t);
    end loop;
end;
$$;

-- La bajada filtra por aqui: sin indice seria un recorrido completo en cada vuelta.
create index if not exists task_lists_synced_idx on public.task_lists (synced_at);
create index if not exists tasks_synced_idx      on public.tasks      (synced_at);
create index if not exists task_steps_synced_idx on public.task_steps (synced_at);

-- Lo que ya estaba subido antes de esta columna se marca como recien llegado, para
-- que los dispositivos que se lo perdieron se lo bajen en la proxima vuelta.
update public.task_lists set synced_at = now();
update public.tasks       set synced_at = now();
update public.task_steps  set synced_at = now();
