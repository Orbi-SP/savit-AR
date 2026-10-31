-- SAVIT: login (perfis) e base para a pontuação.
-- Como rodar: painel do Supabase > SQL Editor > New query > colar este arquivo > Run.
-- Pode rodar de novo sem problema (idempotente).
--
-- As contas são criadas no site do SAVIT com o e-mail do aluno; o app só faz login (e-mail + senha).
-- App e site acessam as tabelas com a chave pública + token do aluno; quem protege os dados é a RLS abaixo.
-- ATENÇÃO: rode também o 002_login_por_email.sql, que ajusta o perfil para e-mails reais.

-- ── Perfis ──────────────────────────────────────────────────────────────────
create table if not exists public.profiles (
  id         uuid primary key references auth.users (id) on delete cascade,
  username   text not null unique check (username ~ '^[a-z0-9_.-]{3,20}$'),
  created_at timestamptz not null default now()
);

alter table public.profiles enable row level security;

drop policy if exists "perfis visíveis para usuários logados" on public.profiles;
create policy "perfis visíveis para usuários logados" on public.profiles
  for select to authenticated using (true);

drop policy if exists "usuário edita o próprio perfil" on public.profiles;
create policy "usuário edita o próprio perfil" on public.profiles
  for update to authenticated using (auth.uid() = id) with check (auth.uid() = id);

-- Cria o perfil quando a conta é criada pelo site (o usuário vem em raw_user_meta_data.username;
-- sem ele, usa a parte do e-mail antes do @).
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  insert into public.profiles (id, username)
  values (new.id, lower(coalesce(new.raw_user_meta_data ->> 'username', split_part(new.email, '@', 1))));
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ── Pontuação (próxima etapa) ───────────────────────────────────────────────
-- Cada partida grava uma linha do próprio aluno; todos os logados podem ler (para ranking).
create table if not exists public.scores (
  id         bigint generated always as identity primary key,
  user_id    uuid not null default auth.uid() references public.profiles (id) on delete cascade,
  points     integer not null check (points >= 0),
  stage      text,
  created_at timestamptz not null default now()
);

create index if not exists scores_user_id_idx on public.scores (user_id);

alter table public.scores enable row level security;

drop policy if exists "aluno grava a própria pontuação" on public.scores;
create policy "aluno grava a própria pontuação" on public.scores
  for insert to authenticated with check (auth.uid() = user_id);

drop policy if exists "pontuações visíveis para usuários logados" on public.scores;
create policy "pontuações visíveis para usuários logados" on public.scores
  for select to authenticated using (true);
