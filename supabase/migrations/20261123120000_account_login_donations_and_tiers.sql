-- Account login summary, selectable license/donation tracking ledger, and beta entitlement projection.
create table community_private.support_requests (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users(id) on delete cascade,
    offer text not null check (offer in ('supporter', 'insider', 'donation')),
    cents integer not null check (cents > 0 and cents <= 10000000),
    note text not null default '' check (char_length(note) <= 200),
    status text not null default 'pending' check (status in ('pending', 'verified', 'cancelled')),
    created_at timestamptz not null default now(),
    check ((offer = 'supporter' and cents = 1999) or (offer = 'insider' and cents = 10000) or offer = 'donation')
);
create index support_requests_user on community_private.support_requests(user_id, created_at desc);

alter table community_private.support_requests enable row level security;
revoke all on community_private.support_requests from public, anon, authenticated;

create function public.lt_submit_support_request(p_offer text, p_cents integer, p_note text default '')
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_id uuid;
    v_note text := coalesce(left(trim(p_note), 200), '');
begin
    if v_user is null or not exists (
        select 1 from auth.users where id = v_user and email is not null and email_confirmed_at is not null
    ) then
        raise exception using errcode = '42501', message = 'Verified email is required.';
    end if;

    if p_offer is null or p_offer not in ('supporter', 'insider', 'donation') then
        raise exception using errcode = '22023', message = 'Choose a valid offer or donation.';
    end if;

    if p_cents is null or p_cents <= 0 or p_cents > 10000000 or
       (p_offer = 'supporter' and p_cents <> 1999) or
       (p_offer = 'insider' and p_cents <> 10000) then
        raise exception using errcode = '22023', message = 'Invalid support amount for selected tier.';
    end if;

    insert into community_private.support_requests(user_id, offer, cents, note, status)
    values (v_user, p_offer, p_cents, v_note, 'pending')
    returning id into v_id;

    return jsonb_build_object(
        'requestId', v_id,
        'offer', p_offer,
        'cents', p_cents,
        'status', 'pending',
        'thanks', 'Every license purchase or donation directly helps this solo developer expand language coverage.'
    );
end;
$$;

create function public.lt_my_account_summary()
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_email text;
    v_verified bigint;
    v_has_insider boolean;
    v_has_supporter boolean;
    v_pending bigint;
    v_history jsonb;
begin
    if v_user is null then
        raise exception using errcode = '42501', message = 'Sign in required.';
    end if;

    select email into v_email
    from auth.users
    where id = v_user and email is not null and email_confirmed_at is not null;

    if v_email is null then
        raise exception using errcode = '42501', message = 'Verified email is required.';
    end if;

    select
        coalesce(sum(cents), 0)::bigint,
        coalesce(bool_or(offer = 'insider' and cents > 0), false),
        coalesce(bool_or(offer = 'supporter' and cents > 0), false)
    into v_verified, v_has_insider, v_has_supporter
    from community_private.net_support
    where user_id = v_user;

    select coalesce(sum(cents), 0)::bigint
    into v_pending
    from community_private.support_requests
    where user_id = v_user and status = 'pending';

    select coalesce(jsonb_agg(item order by created_at desc), '[]'::jsonb)
    into v_history
    from (
        select
            r.receipt_id::text as id,
            r.offer,
            r.cents::bigint as cents,
            'verified'::text as status,
            r.confirmed_at as created_at,
            jsonb_build_object('id', r.receipt_id::text, 'offer', r.offer, 'cents', r.cents::bigint, 'status', 'verified', 'createdAt', r.confirmed_at) as item
        from community_private.verified_support r
        where r.user_id = v_user
        union all
        select
            sr.id::text as id,
            sr.offer,
            sr.cents::bigint as cents,
            sr.status,
            sr.created_at,
            jsonb_build_object('id', sr.id::text, 'offer', sr.offer, 'cents', sr.cents::bigint, 'status', sr.status, 'createdAt', sr.created_at) as item
        from community_private.support_requests sr
        where sr.user_id = v_user
        order by created_at desc
        limit 25
    ) combined;

    return jsonb_build_object(
        'email', v_email,
        'activeTier', case when v_has_insider then 'insider' when v_has_supporter then 'supporter' else 'free' end,
        'verifiedTotalCents', v_verified,
        'pendingTotalCents', v_pending,
        'betaAccessUnlocked', (v_has_insider or v_verified >= 10000),
        'history', v_history
    );
end;
$$;

revoke all on function public.lt_submit_support_request(text, integer, text) from public, anon, authenticated;
revoke all on function public.lt_my_account_summary() from public, anon, authenticated;
grant execute on function public.lt_submit_support_request(text, integer, text) to authenticated;
grant execute on function public.lt_my_account_summary() to authenticated;
