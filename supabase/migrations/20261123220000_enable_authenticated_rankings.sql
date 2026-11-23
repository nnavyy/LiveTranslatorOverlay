-- Enable authenticated user profile management and device-bound usage submission
-- for live Community Rankings while keeping default rankings_open = false safe.

create function public.lt_my_profile()
returns jsonb language plpgsql stable security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_profile record;
begin
    if v_user is null or not exists (
        select 1 from auth.users where id = v_user and email is not null and email_confirmed_at is not null
    ) then
        raise exception using errcode = '42501', message = 'Verified email sign-in is required.';
    end if;

    select nickname, sync_usage, public_usage, public_support, show_medals
    into v_profile
    from community_private.profiles
    where user_id = v_user;

    if not found then
        return jsonb_build_object('configured', false);
    end if;

    return jsonb_build_object(
        'configured', true,
        'nickname', v_profile.nickname,
        'syncUsage', v_profile.sync_usage,
        'publicUsage', v_profile.public_usage,
        'publicSupport', v_profile.public_support,
        'showMedals', v_profile.show_medals
    );
end;
$$;

create function public.lt_save_profile(
    p_nickname text,
    p_sync_usage boolean,
    p_public_usage boolean,
    p_public_support boolean,
    p_show_medals boolean default false
) returns jsonb language plpgsql security definer set search_path = '' as $$
begin
    perform community_private.save_profile(
        p_nickname,
        coalesce(p_sync_usage, false),
        coalesce(p_public_usage, false),
        coalesce(p_public_support, false),
        coalesce(p_show_medals, false)
    );
    return public.lt_my_profile();
end;
$$;

create function public.lt_submit_usage_segment(
    p_segment_id uuid,
    p_started_at timestamptz,
    p_ended_at timestamptz,
    p_hwid_hash text
) returns void language plpgsql security definer set search_path = '' as $$
begin
    perform community_private.submit_usage_with_device(p_segment_id, p_started_at, p_ended_at, p_hwid_hash);
end;
$$;

create function public.lt_admin_set_rankings_open(p_open boolean)
returns jsonb language plpgsql security definer set search_path = '' as $$
begin
    update community_private.settings
    set rankings_open = coalesce(p_open, false);

    return jsonb_build_object('rankingsOpen', coalesce(p_open, false));
end;
$$;

revoke all on function public.lt_my_profile() from public, anon, authenticated;
revoke all on function public.lt_save_profile(text, boolean, boolean, boolean, boolean) from public, anon, authenticated;
revoke all on function public.lt_submit_usage_segment(uuid, timestamptz, timestamptz, text) from public, anon, authenticated;
revoke all on function public.lt_admin_set_rankings_open(boolean) from public, anon, authenticated;

grant execute on function public.lt_my_profile() to authenticated;
grant execute on function public.lt_save_profile(text, boolean, boolean, boolean, boolean) to authenticated;
grant execute on function public.lt_submit_usage_segment(uuid, timestamptz, timestamptz, text) to authenticated;

do $$
begin
    if exists (select 1 from pg_roles where rolname = 'service_role') then
        grant execute on function public.lt_admin_set_rankings_open(boolean) to service_role;
    end if;
end $$;
