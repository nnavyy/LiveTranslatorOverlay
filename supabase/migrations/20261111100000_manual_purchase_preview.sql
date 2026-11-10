alter table community_private.settings
    add column manual_purchase_preview_open boolean not null default false;

create table community_private.manual_purchase_requests (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users(id) on delete cascade,
    offer text not null check (offer in ('supporter', 'insider')),
    requested_at timestamptz not null default now(),
    unique (user_id, offer)
);

alter table community_private.manual_purchase_requests enable row level security;
revoke all on community_private.manual_purchase_requests from public, anon, authenticated;

-- This preview records an intent only. It does not collect money or write to the verified ledger.
create function public.lt_request_manual_purchase(p_offer text)
returns uuid language plpgsql security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_id uuid;
begin
    if not exists (select 1 from community_private.settings where manual_purchase_preview_open) then
        raise exception using errcode = '42501', message = 'The purchase request preview is closed.';
    end if;
    if v_user is null or not exists (
        select 1 from auth.users where id = v_user and email is not null and email_confirmed_at is not null
    ) then
        raise exception using errcode = '42501', message = 'Verified email is required.';
    end if;
    if p_offer is null or p_offer not in ('supporter', 'insider') then
        raise exception using errcode = '22023', message = 'Choose a lifetime offer.';
    end if;
    insert into community_private.manual_purchase_requests(user_id, offer)
    values (v_user, p_offer) on conflict (user_id, offer) do nothing
    returning id into v_id;
    if v_id is null then
        select id into v_id from community_private.manual_purchase_requests where user_id = v_user and offer = p_offer;
    end if;
    return v_id;
end;
$$;

revoke all on function public.lt_request_manual_purchase(text) from public, anon, authenticated;
grant execute on function public.lt_request_manual_purchase(text) to authenticated;
