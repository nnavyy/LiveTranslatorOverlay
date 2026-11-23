-- Login-optional guest donations (User#XXXXX), salted HardwareID anti-abuse,
-- and device-bound Sybil resistance for community rankings.

alter table community_private.usage_segments
    add column hardware_fingerprint_hash text check (
        hardware_fingerprint_hash is null or hardware_fingerprint_hash ~ '^[0-9a-f]{64}$'
    );

create index usage_segments_hwid_interval
    on community_private.usage_segments(hardware_fingerprint_hash, started_at, ended_at)
    where hardware_fingerprint_hash is not null;

alter table community_private.verified_support
    add column guest_tag text check (guest_tag is null or guest_tag ~ '^User#[0-9]{5}$'),
    add column hardware_fingerprint_hash text check (
        hardware_fingerprint_hash is null or hardware_fingerprint_hash ~ '^[0-9a-f]{64}$'
    );

alter table community_private.verified_support
    add constraint verified_support_identity_check check (
        user_id is not null or (offer = 'donation' and guest_tag is not null)
    );

create table community_private.guest_donation_requests (
    id uuid primary key default gen_random_uuid(),
    hardware_fingerprint_hash text not null check (hardware_fingerprint_hash ~ '^[0-9a-f]{64}$'),
    guest_tag text not null check (guest_tag ~ '^User#[0-9]{5}$'),
    cents integer not null check (cents > 0 and cents <= 10000000),
    note text not null default '' check (char_length(note) <= 200),
    status text not null default 'pending' check (status in ('pending', 'verified', 'cancelled')),
    created_at timestamptz not null default now()
);

create index guest_donation_requests_hwid_time
    on community_private.guest_donation_requests(hardware_fingerprint_hash, created_at desc);

alter table community_private.guest_donation_requests enable row level security;
revoke all on community_private.guest_donation_requests from public, anon, authenticated;

-- Derives User#10000..User#99999 identically to HardwareFingerprint.DeriveGuestDisplayTag in C#.
create function community_private.guest_tag_from_hwid(p_hwid_hash text)
returns text language plpgsql immutable strict set search_path = '' as $$
declare
    v_clean text := lower(trim(p_hwid_hash));
    v_prefix bigint;
    v_num integer;
begin
    if v_clean !~ '^[0-9a-f]{64}$' then
        raise exception using errcode = '22023', message = 'Expected 64-character lowercase SHA-256 hardware hash.';
    end if;
    -- Pad 32 zeros so values >= 0x80000000 convert as unsigned uint32 matching C# uint.Parse.
    v_prefix := ('x00000000' || left(v_clean, 8))::bit(64)::bigint;
    v_num := ((v_prefix % 90000) + 10000)::integer;
    return 'User#' || lpad(v_num::text, 5, '0');
end;
$$;

revoke all on function community_private.guest_tag_from_hwid(text) from public, anon, authenticated;

-- Guest donation request endpoint: requires NO login, tracks by salted HWID, rate-limited per device.
create function public.lt_submit_guest_donation(p_cents integer, p_hwid_hash text, p_note text default '')
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_hwid text := lower(trim(coalesce(p_hwid_hash, '')));
    v_tag text;
    v_id uuid;
    v_recent integer;
    v_note text := coalesce(left(trim(p_note), 200), '');
begin
    if v_hwid !~ '^[0-9a-f]{64}$' then
        raise exception using errcode = '22023', message = 'Invalid hardware fingerprint hash.';
    end if;
    if p_cents is null or p_cents <= 0 or p_cents > 10000000 then
        raise exception using errcode = '22023', message = 'Donation amount must be between 1 cent and US$100,000.';
    end if;

    select count(*)::integer into v_recent
    from community_private.guest_donation_requests
    where hardware_fingerprint_hash = v_hwid
      and status = 'pending'
      and created_at >= now() - interval '24 hours';

    if v_recent >= 5 then
        raise exception using errcode = '42501', message = 'Daily guest donation request limit reached for this device.';
    end if;

    v_tag := community_private.guest_tag_from_hwid(v_hwid);

    insert into community_private.guest_donation_requests(hardware_fingerprint_hash, guest_tag, cents, note, status)
    values (v_hwid, v_tag, p_cents, v_note, 'pending')
    returning id into v_id;

    return jsonb_build_object(
        'requestId', v_id,
        'guestTag', v_tag,
        'cents', p_cents,
        'status', 'pending',
        'thanks', 'Every license purchase or donation directly helps this solo developer expand language coverage.'
    );
end;
$$;

revoke all on function public.lt_submit_guest_donation(integer, text, text) from public, anon, authenticated;
grant execute on function public.lt_submit_guest_donation(integer, text, text) to anon, authenticated;

-- Device-aware usage submission blocking multi-account Sybil farming from one machine.
create function community_private.submit_usage_with_device(
    p_segment_id uuid,
    p_started_at timestamptz,
    p_ended_at timestamptz,
    p_hwid_hash text
) returns void language plpgsql security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_hwid text := lower(trim(coalesce(p_hwid_hash, '')));
    v_daily_secs double precision;
begin
    if not exists (select 1 from community_private.profiles where user_id = v_user and sync_usage) then
        raise exception using errcode = '42501', message = 'Usage sync is disabled.';
    end if;
    if v_hwid !~ '^[0-9a-f]{64}$' then
        raise exception using errcode = '22023', message = 'Invalid hardware fingerprint hash.';
    end if;
    if p_started_at is null or p_ended_at is null or p_segment_id is null
        or p_started_at < now() - interval '30 days' or p_ended_at > now() + interval '1 minute'
        or p_ended_at <= p_started_at or p_ended_at - p_started_at > interval '20 seconds' then
        raise exception using errcode = '22023', message = 'Invalid usage interval.';
    end if;

    -- Prevent two different accounts from farming simultaneous overlapping intervals on the same machine.
    if exists (
        select 1 from community_private.usage_segments u
        where u.hardware_fingerprint_hash = v_hwid
          and u.user_id <> v_user
          and tstzrange(u.started_at, u.ended_at, '[)') && tstzrange(p_started_at, p_ended_at, '[)')
    ) then
        raise exception using errcode = '42501', message = 'Overlapping usage from this device belongs to another account.';
    end if;

    select coalesce(sum(extract(epoch from (u.ended_at - u.started_at))), 0)
    into v_daily_secs
    from community_private.usage_segments u
    where u.hardware_fingerprint_hash = v_hwid
      and u.ended_at >= date_trunc('day', p_ended_at at time zone 'UTC') at time zone 'UTC';

    if v_daily_secs + extract(epoch from (p_ended_at - p_started_at)) > 86400 then
        raise exception using errcode = '42501', message = 'Daily device usage cap exceeded.';
    end if;

    insert into community_private.usage_segments(user_id, segment_id, started_at, ended_at, hardware_fingerprint_hash)
    values (v_user, p_segment_id, p_started_at, p_ended_at, v_hwid)
    on conflict (user_id, segment_id) do nothing;
end;
$$;

revoke all on function community_private.submit_usage_with_device(uuid, timestamptz, timestamptz, text) from public, anon, authenticated;

-- Update lt_community_board so verified guest donations render anonymously as User#XXXXX on the supporters board.
create or replace function public.lt_community_board(p_board text, p_search text default '', p_offset integer default 0)
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
        select p.nickname, p.nickname_key,
            floor(sum(extract(epoch from upper(i.period) - lower(i.period))))::bigint as score,
            p.user_id,
            p.public_support,
            p.show_medals
        from merged m
        cross join lateral unnest(m.intervals) i(period)
        join community_private.profiles p on p.user_id = m.user_id
        group by p.user_id, p.nickname, p.nickname_key, p.public_support, p.show_medals
    ), registered_support as (
        select p.nickname, p.nickname_key,
            sum(s.cents)::bigint as score,
            p.user_id,
            p.public_support,
            p.show_medals
        from community_private.net_support s
        join community_private.profiles p on p.user_id = s.user_id and p.public_support
        where p_board = 'supporters' and s.user_id is not null
        group by p.user_id, p.nickname, p.nickname_key, p.public_support, p.show_medals
    ), guest_support as (
        select v.guest_tag as nickname,
            lower(v.guest_tag) as nickname_key,
            sum(greatest(v.cents - coalesce(rev.rev_cents, 0), 0))::bigint as score,
            null::uuid as user_id,
            true as public_support,
            false as show_medals
        from community_private.verified_support v
        left join (
            select receipt_id, sum(cents)::bigint as rev_cents
            from community_private.support_reversals
            group by receipt_id
        ) rev on rev.receipt_id = v.receipt_id
        where p_board = 'supporters' and v.user_id is null and v.guest_tag is not null
        group by v.guest_tag
    ), paid_medals as (
        select s.user_id, array_agg(distinct case s.offer when 'supporter' then 'Supporter' else 'Insider' end order by
            case s.offer when 'supporter' then 'Supporter' else 'Insider' end) as medals
        from community_private.net_support s
        where s.cents > 0 and s.offer in ('supporter', 'insider') and s.user_id is not null
        group by s.user_id
    ), combined as (
        select * from usage_scores
        union all
        select * from registered_support
        union all
        select * from guest_support
    ), ranked as (
        select rank() over (order by c.score desc) as rank,
            c.nickname, c.nickname_key, c.score, c.user_id, c.public_support,
            case when c.show_medals then coalesce(m.medals, array[]::text[]) else array[]::text[] end as medals
        from combined c
        left join paid_medals m on m.user_id = c.user_id
        where c.score > 0
    ), page as (
        select * from ranked r
        where strpos(r.nickname_key, lower(trim(normalize(coalesce(p_search, ''), NFKC)))) > 0
        order by r.rank, r.nickname_key limit 50 offset p_offset
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'rank', r.rank,
        'nickname', r.nickname,
        'score', r.score,
        'medals', r.medals,
        'supportCents', case when r.user_id is null then r.score when r.public_support then coalesce(t.cents, 0) end
    ) order by r.rank, r.nickname_key), '[]'::jsonb)
    into v_entries
    from page r
    left join lateral (
        select sum(s.cents)::bigint as cents
        from community_private.net_support s
        where r.user_id is not null and r.public_support and s.user_id = r.user_id
    ) t on true;

    return jsonb_build_object('open', true, 'entries', v_entries);
end;
$$;
