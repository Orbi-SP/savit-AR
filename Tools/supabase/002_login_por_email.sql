-- SAVIT: perfil para login com o e-mail do próprio aluno (conta criada no site, login no app).
-- Rodar depois do 001: painel do Supabase > SQL Editor > New query > colar este arquivo > Run.
-- Pode rodar de novo sem problema (idempotente).
--
-- Por que: o 001 montava um "username" obrigatório e único a partir do e-mail, com formato rígido.
-- Com e-mails reais isso faria o cadastro no site falhar (ex.: joao+escola@gmail.com, ou dois "joao@"
-- de provedores diferentes). Agora o nome de exibição é opcional: o site pode mandá-lo em
-- options.data.username no signUp. O e-mail NÃO vai para profiles: ele já fica em auth.users e assim
-- um aluno logado não consegue listar o e-mail dos outros (profiles é legível por todos os logados).

alter table public.profiles drop constraint if exists profiles_username_check;
alter table public.profiles drop constraint if exists profiles_username_key;
alter table public.profiles alter column username drop not null;

comment on column public.profiles.username is
  'Nome de exibição (opcional), enviado pelo site em options.data.username. Usado no ranking.';

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  insert into public.profiles (id, username)
  values (new.id, nullif(trim(new.raw_user_meta_data ->> 'username'), ''));
  return new;
end;
$$;
