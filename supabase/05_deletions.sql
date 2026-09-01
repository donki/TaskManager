-- ---------------------------------------------------------------------------
-- "deletions": las bajas, para poder borrar de verdad
--
-- Hasta ahora un borrado era logico (deleted = true) y la fila se quedaba en la
-- base para siempre. El motivo era bueno: si se borrase la fila, el dispositivo
-- que estuviera desconectado no se enteraria nunca —no hay forma de sincronizar
-- una ausencia— y la lista reaparecería al volver.
--
-- Esta tabla resuelve las dos cosas a la vez: el contenido se BORRA de verdad y
-- lo unico que queda es un apunte diminuto de que aquello ya no esta. Los demas
-- dispositivos se bajan los apuntes y borran lo suyo.
--
-- Idempotente: se puede relanzar sin romper nada.
-- ---------------------------------------------------------------------------

create table if not exists public.deletions (
    entity     text        not null,
    entity_id  uuid        not null,
    owner_id   uuid        not null default auth.uid() references auth.users (id) on delete cascade,
    deleted_at timestamptz not null default now(),
    primary key (entity, entity_id)
);

-- La bajada pregunta "que se ha borrado desde la ultima vez".
create index if not exists deletions_at_idx on public.deletions (deleted_at);

alter table public.deletions enable row level security;

-- Cada uno ve y escribe sus propias bajas. Un apunte solo lleva un identificador,
-- pero saber QUE ha borrado alguien ya dice de mas.
drop policy if exists deletions_own on public.deletions;
create policy deletions_own on public.deletions
    for all
    using (owner_id = auth.uid())
    with check (owner_id = auth.uid());
