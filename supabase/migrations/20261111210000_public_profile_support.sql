-- Share the verified net total only for profiles that opt into public support.
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
        select rank() over (order by s.score desc) as rank, p.nickname, p.nickname_key, s.score, p.user_id, p.public_support,
            case when p.show_medals then coalesce(m.medals, array[]::text[]) else array[]::text[] end as medals
        from scores s join community_private.profiles p on p.user_id = s.user_id
        left join paid_medals m on m.user_id = s.user_id where s.score > 0
    ), page as (
        select * from ranked r where strpos(r.nickname_key, lower(trim(normalize(coalesce(p_search, ''), NFKC)))) > 0
        order by r.rank, r.nickname_key limit 50 offset p_offset
    )
    select coalesce(jsonb_agg(jsonb_build_object(
        'rank', r.rank, 'nickname', r.nickname, 'score', r.score, 'medals', r.medals,
        'supportCents', case when r.public_support then coalesce(t.cents, 0) end
    ) order by r.rank, r.nickname_key), '[]'::jsonb) into v_entries
    from page r
    left join lateral (
        select sum(s.cents)::bigint as cents from community_private.net_support s
        where r.public_support and s.user_id = r.user_id
    ) t on true;

    return jsonb_build_object('open', true, 'entries', v_entries);
end;
$$;
