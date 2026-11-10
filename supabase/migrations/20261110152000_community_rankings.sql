-- Desktop clients can only access these records through the explicit RPCs below.
create schema community_private;
revoke all on schema community_private from public, anon, authenticated;

create table community_private.settings (
    singleton boolean primary key default true check (singleton),
    rankings_open boolean not null default false
);
insert into community_private.settings default values;

create table community_private.profiles (
    user_id uuid primary key references auth.users(id) on delete cascade,
    nickname text not null check (char_length(nickname) between 3 and 24),
    nickname_key text not null unique,
    sync_usage boolean not null default false,
    public_usage boolean not null default false,
    public_support boolean not null default false,
    show_medals boolean not null default false
);

create table community_private.usage_segments (
    user_id uuid not null references community_private.profiles(user_id) on delete cascade,
    segment_id uuid not null,
    started_at timestamptz not null,
    ended_at timestamptz not null,
    primary key (user_id, segment_id),
    check (ended_at > started_at and ended_at - started_at <= interval '20 seconds')
);

-- Only verified, settled transactions belong in this ledger. No client can create them.
create table community_private.verified_support (
    receipt_id uuid primary key,
    user_id uuid references community_private.profiles(user_id) on delete set null,
    offer text not null check (offer in ('supporter', 'insider', 'donation')),
    cents integer not null check (cents > 0),
    confirmed_at timestamptz not null default now(),
    check ((offer = 'supporter' and cents = 1999) or (offer = 'insider' and cents = 10000) or offer = 'donation')
);
create index verified_support_user on community_private.verified_support(user_id);

create table community_private.support_reversals (
    reversal_id uuid primary key,
    receipt_id uuid not null references community_private.verified_support(receipt_id),
    cents integer not null check (cents > 0),
    reversed_at timestamptz not null default now()
);
create index support_reversals_receipt on community_private.support_reversals(receipt_id);

alter table community_private.settings enable row level security;
alter table community_private.profiles enable row level security;
alter table community_private.usage_segments enable row level security;
alter table community_private.verified_support enable row level security;
alter table community_private.support_reversals enable row level security;
revoke all on all tables in schema community_private from public, anon, authenticated;

create view community_private.net_support as
select v.user_id, v.offer, greatest(v.cents - coalesce(sum(r.cents), 0), 0)::bigint as cents
from community_private.verified_support v
left join community_private.support_reversals r on v.receipt_id = r.receipt_id
group by v.receipt_id;
revoke all on community_private.net_support from public, anon, authenticated;

-- Profile and upload RPCs will receive a separate grant when the desktop account flow,
-- versioned consent and account deletion are deployed. Keep them private for now.
create function community_private.save_profile(
    p_nickname text, p_sync_usage boolean, p_public_usage boolean, p_public_support boolean, p_show_medals boolean
) returns void language plpgsql security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_name text := trim(normalize(p_nickname, NFKC));
    v_key text := lower(v_name);
begin
    if v_user is null or not exists (
        select 1 from auth.users where id = v_user and email is not null and email_confirmed_at is not null
    ) then
        raise exception using errcode = '42501', message = 'Verified email is required.';
    end if;
    if v_name is null or char_length(v_name) not between 3 and 24 or v_name !~ '^[[:alnum:]][[:alnum:] _-]*[[:alnum:]]$'
        or v_key in ('admin', 'administrator', 'developer', 'live translator', 'live translator overlay', 'livetranslator', 'support', 'moderator', 'nnavyy') then
        raise exception using errcode = '22023', message = 'Choose a different nickname.';
    end if;
    insert into community_private.profiles(user_id, nickname, nickname_key, sync_usage, public_usage, public_support, show_medals)
    values (v_user, v_name, v_key, p_sync_usage, p_public_usage, p_public_support, p_show_medals)
    on conflict (user_id) do update set nickname = excluded.nickname, nickname_key = excluded.nickname_key,
        sync_usage = excluded.sync_usage, public_usage = excluded.public_usage, public_support = excluded.public_support,
        show_medals = excluded.show_medals;
end;
$$;

create function community_private.submit_usage(p_segment_id uuid, p_started_at timestamptz, p_ended_at timestamptz)
returns void language plpgsql security definer set search_path = '' as $$
declare v_user uuid := auth.uid();
begin
    if not exists (select 1 from community_private.profiles where user_id = v_user and sync_usage) then
        raise exception using errcode = '42501', message = 'Usage sync is disabled.';
    end if;
    if p_started_at is null or p_ended_at is null or p_segment_id is null
        or p_started_at < now() - interval '30 days' or p_ended_at > now() + interval '1 minute'
        or p_ended_at <= p_started_at or p_ended_at - p_started_at > interval '20 seconds' then
        raise exception using errcode = '22023', message = 'Invalid usage interval.';
    end if;
    insert into community_private.usage_segments(user_id, segment_id, started_at, ended_at)
    values (v_user, p_segment_id, p_started_at, p_ended_at) on conflict (user_id, segment_id) do nothing;
end;
$$;
revoke all on function community_private.save_profile(text, boolean, boolean, boolean, boolean) from public, anon, authenticated;
revoke all on function community_private.submit_usage(uuid, timestamptz, timestamptz) from public, anon, authenticated;

create function public.lt_community_board(p_board text, p_search text default '', p_offset integer default 0)
returns jsonb language plpgsql stable security definer set search_path = '' as $$
declare
    v_month_start timestamptz := date_trunc('month', statement_timestamp() at time zone 'UTC') at time zone 'UTC';
    v_month_end timestamptz := (date_trunc('month', statement_timestamp() at time zone 'UTC') + interval '1 month') at time zone 'UTC';
    v_entries jsonb;
begin
    if p_board is null or p_board not in ('all', 'month', 'supporters') or p_offset is null or p_offset < 0
        or char_length(coalesce(p_search, '')) > 24 then
        raise exception using errcode = '22023', message = 'Invalid ranking query.';
    end if;
    if not exists (select 1 from community_private.settings where rankings_open) then
        return jsonb_build_object('open', false, 'entries', '[]'::jsonb);
    end if;

    with merged as (
        select u.user_id, range_agg(case when p_board = 'month'
            then tstzrange(greatest(u.started_at, v_month_start), least(u.ended_at, v_month_end), '[)')
            else tstzrange(u.started_at, u.ended_at, '[)') end) as intervals
        from community_private.usage_segments u
        join community_private.profiles p on p.user_id = u.user_id and p.public_usage
        where p_board <> 'supporters' and (p_board <> 'month' or (u.started_at < v_month_end and u.ended_at > v_month_start))
        group by u.user_id
    ), usage_scores as (
        select m.user_id, floor(sum(extract(epoch from upper(i.period) - lower(i.period))))::bigint as score
        from merged m cross join lateral unnest(m.intervals) i(period) group by m.user_id
    ), support_scores as (
        select s.user_id, sum(s.cents)::bigint as score
        from community_private.net_support s join community_private.profiles p on p.user_id = s.user_id and p.public_support
        where p_board = 'supporters' group by s.user_id
    ), paid_medals as (
        select s.user_id, array_agg(distinct case s.offer when 'supporter' then 'Supporter' else 'Insider' end order by
            case s.offer when 'supporter' then 'Supporter' else 'Insider' end) as medals
        from community_private.net_support s where s.cents > 0 and s.offer in ('supporter', 'insider')
        group by s.user_id
    ), scores as (
        select * from usage_scores union all select * from support_scores
    ), ranked as (
        select rank() over (order by s.score desc) as rank, p.nickname, p.nickname_key, s.score,
            case when p.show_medals then coalesce(m.medals, array[]::text[]) else array[]::text[] end as medals
        from scores s join community_private.profiles p on p.user_id = s.user_id
        left join paid_medals m on m.user_id = s.user_id where s.score > 0
    ), page as (
        select * from ranked r where strpos(r.nickname_key, lower(trim(normalize(coalesce(p_search, ''), NFKC)))) > 0
        order by r.rank, r.nickname_key limit 50 offset p_offset
    )
    select coalesce(jsonb_agg(jsonb_build_object('rank', r.rank, 'nickname', r.nickname, 'score', r.score, 'medals', r.medals)
        order by r.rank, r.nickname_key), '[]'::jsonb) into v_entries from page r;

    return jsonb_build_object('open', true, 'entries', v_entries);
end;
$$;

revoke all on function public.lt_community_board(text, text, integer) from public, anon, authenticated;
grant execute on function public.lt_community_board(text, text, integer) to anon, authenticated;
