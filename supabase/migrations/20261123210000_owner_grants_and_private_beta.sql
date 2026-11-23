-- Owner 1-by-1 grant management, audit log, and authenticated private beta release metadata.

create table community_private.beta_grants (
    user_id uuid primary key references auth.users(id) on delete cascade,
    active boolean not null default true,
    note text not null default '' check (char_length(note) <= 300),
    granted_at timestamptz not null default now(),
    revoked_at timestamptz
);

create table community_private.beta_releases (
    id uuid primary key default gen_random_uuid(),
    version text not null check (version ~ '^[0-9]+\.[0-9]+\.[0-9]+$'),
    runtime text not null check (runtime in ('win-x64', 'osx-arm64', 'osx-x64')),
    storage_bucket text not null default 'beta-releases',
    storage_path text not null check (char_length(storage_path) between 3 and 500),
    size bigint not null check (size > 0),
    sha256 text not null check (sha256 ~ '^[0-9a-fA-F]{64}$'),
    notes text not null default '',
    active boolean not null default true,
    published_at timestamptz not null default now()
);
create unique index beta_releases_version_runtime on community_private.beta_releases(version, runtime);
create index beta_releases_active_runtime on community_private.beta_releases(runtime, active, published_at desc);

create table community_private.admin_grant_audit (
    id uuid primary key default gen_random_uuid(),
    action text not null check (action in ('grant_support', 'revoke_support', 'grant_guest_donation', 'set_beta_access', 'publish_beta_release')),
    target_request_id uuid,
    target_user_id uuid,
    target_guest_tag text,
    offer text,
    cents integer,
    grant_beta boolean,
    note text not null default '',
    acted_at timestamptz not null default now()
);

alter table community_private.beta_grants enable row level security;
alter table community_private.beta_releases enable row level security;
alter table community_private.admin_grant_audit enable row level security;
revoke all on community_private.beta_grants from public, anon, authenticated;
revoke all on community_private.beta_releases from public, anon, authenticated;
revoke all on community_private.admin_grant_audit from public, anon, authenticated;

-- Include explicit beta_grants in authenticated account summary while preserving existing fields.
create or replace function public.lt_my_account_summary()
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_email text;
    v_verified bigint;
    v_has_insider boolean;
    v_has_supporter boolean;
    v_explicit_beta boolean;
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

    select exists(
        select 1 from community_private.beta_grants g
        where g.user_id = v_user and g.active
    ) into v_explicit_beta;

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
        'betaAccessUnlocked', (v_has_insider or v_verified >= 10000 or v_explicit_beta),
        'explicitBetaGrant', v_explicit_beta,
        'history', v_history
    );
end;
$$;

-- Authenticated endpoint called by Supabase Edge Function `beta-release` before minting a signed URL.
create function public.lt_get_beta_release_metadata(p_runtime text, p_hwid_hash text default null)
returns jsonb language plpgsql stable security definer set search_path = '' as $$
declare
    v_user uuid := auth.uid();
    v_runtime text := lower(trim(coalesce(p_runtime, '')));
    v_hwid text := nullif(lower(trim(coalesce(p_hwid_hash, ''))), '');
    v_allowed boolean;
    v_release record;
begin
    if v_user is null or not exists (
        select 1 from auth.users where id = v_user and email is not null and email_confirmed_at is not null
    ) then
        raise exception using errcode = '42501', message = 'Verified email sign-in is required for Beta access.';
    end if;

    if v_runtime not in ('win-x64', 'osx-arm64', 'osx-x64') then
        raise exception using errcode = '22023', message = 'Invalid target runtime.';
    end if;

    if v_hwid is not null and v_hwid !~ '^[0-9a-f]{64}$' then
        raise exception using errcode = '22023', message = 'Invalid hardware fingerprint hash.';
    end if;

    select (
        coalesce((
            select (bool_or(ns.offer = 'insider' and ns.cents > 0) or coalesce(sum(ns.cents), 0) >= 10000)
            from community_private.net_support ns
            where ns.user_id = v_user
        ), false)
        or exists (
            select 1 from community_private.beta_grants g
            where g.user_id = v_user and g.active
        )
    ) into v_allowed;

    if not coalesce(v_allowed, false) then
        raise exception using errcode = '42501', message = 'Beta access requires a developer-verified US$100 Lifetime Insider grant.';
    end if;

    select br.version, br.runtime, br.storage_bucket, br.storage_path, br.size, br.sha256, br.notes, br.published_at
    into v_release
    from community_private.beta_releases br
    where br.runtime = v_runtime and br.active
    order by br.published_at desc
    limit 1;

    if not found then
        return jsonb_build_object('available', false);
    end if;

    return jsonb_build_object(
        'available', true,
        'version', v_release.version,
        'runtime', v_release.runtime,
        'storageBucket', v_release.storage_bucket,
        'storagePath', v_release.storage_path,
        'size', v_release.size,
        'sha256', upper(v_release.sha256),
        'notes', v_release.notes,
        'publishedAt', v_release.published_at
    );
end;
$$;

revoke all on function public.lt_get_beta_release_metadata(text, text) from public, anon, authenticated;
grant execute on function public.lt_get_beta_release_metadata(text, text) to authenticated;

-- Owner-only inspection & 1-by-1 grant RPCs (strictly ungranted to anon/authenticated; granted only to service_role).
create function public.lt_admin_list_requests(p_status text default 'all', p_limit integer default 100)
returns jsonb language plpgsql stable security definer set search_path = '' as $$
declare
    v_status text := lower(trim(coalesce(p_status, 'all')));
    v_limit integer := least(greatest(coalesce(p_limit, 100), 1), 500);
    v_account_requests jsonb;
    v_guest_donations jsonb;
    v_insiders jsonb;
begin
    if v_status not in ('all', 'pending', 'verified', 'cancelled') then
        raise exception using errcode = '22023', message = 'Status filter must be all, pending, verified, or cancelled.';
    end if;

    select coalesce(jsonb_agg(row_item), '[]'::jsonb)
    into v_account_requests
    from (
        select jsonb_build_object(
            'requestId', sr.id,
            'userId', sr.user_id,
            'email', u.email,
            'offer', sr.offer,
            'cents', sr.cents,
            'note', sr.note,
            'status', sr.status,
            'createdAt', sr.created_at,
            'explicitBetaActive', coalesce(bg.active, false)
        ) as row_item
        from community_private.support_requests sr
        join auth.users u on u.id = sr.user_id
        left join community_private.beta_grants bg on bg.user_id = sr.user_id
        where v_status = 'all' or sr.status = v_status
        order by sr.created_at desc
        limit v_limit
    ) q;

    select coalesce(jsonb_agg(row_item), '[]'::jsonb)
    into v_guest_donations
    from (
        select jsonb_build_object(
            'requestId', gr.id,
            'guestTag', gr.guest_tag,
            'cents', gr.cents,
            'note', gr.note,
            'status', gr.status,
            'hardwareFingerprintHash', gr.hardware_fingerprint_hash,
            'createdAt', gr.created_at
        ) as row_item
        from community_private.guest_donation_requests gr
        where v_status = 'all' or gr.status = v_status
        order by gr.created_at desc
        limit v_limit
    ) q;

    select coalesce(jsonb_agg(row_item), '[]'::jsonb)
    into v_insiders
    from (
        select jsonb_build_object(
            'userId', u.id,
            'email', u.email,
            'verifiedTotalCents', coalesce(sum(ns.cents), 0)::bigint,
            'explicitBetaActive', coalesce(bool_or(bg.active), false),
            'betaGrantedAt', max(bg.granted_at),
            'betaNote', coalesce(max(bg.note), '')
        ) as row_item
        from auth.users u
        left join community_private.net_support ns on ns.user_id = u.id
        left join community_private.beta_grants bg on bg.user_id = u.id and bg.active
        group by u.id, u.email
        having coalesce(bool_or(ns.offer = 'insider' and ns.cents > 0), false)
            or coalesce(sum(ns.cents), 0) >= 10000
            or coalesce(bool_or(bg.active), false)
        order by u.email asc
    ) q;

    return jsonb_build_object(
        'accountRequests', v_account_requests,
        'guestDonations', v_guest_donations,
        'verifiedInsiders', v_insiders
    );
end;
$$;

create function public.lt_admin_grant_support(p_request_id uuid, p_grant_beta boolean default null, p_admin_note text default '')
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_req record;
    v_email text;
    v_unlock_beta boolean;
    v_note text := coalesce(left(trim(p_admin_note), 300), '');
begin
    select sr.*, u.email into v_req
    from community_private.support_requests sr
    join auth.users u on u.id = sr.user_id
    where sr.id = p_request_id;

    if not found then
        raise exception using errcode = '22023', message = 'Support request not found.';
    end if;

    v_email := v_req.email;

    -- Ensure private profile row exists before inserting into verified_support (FK to profiles.user_id).
    insert into community_private.profiles(user_id, nickname, nickname_key, sync_usage, public_usage, public_support, show_medals)
    values (
        v_req.user_id,
        'User-' || left(replace(v_req.user_id::text, '-', ''), 8),
        'user-' || left(replace(v_req.user_id::text, '-', ''), 8),
        false, false, false, false
    )
    on conflict (user_id) do nothing;

    insert into community_private.verified_support(receipt_id, user_id, offer, cents)
    values (p_request_id, v_req.user_id, v_req.offer, v_req.cents)
    on conflict (receipt_id) do nothing;

    update community_private.support_requests
    set status = 'verified'
    where id = p_request_id;

    v_unlock_beta := coalesce(p_grant_beta, v_req.offer = 'insider');
    if v_unlock_beta then
        insert into community_private.beta_grants(user_id, active, note, granted_at, revoked_at)
        values (v_req.user_id, true, v_note, now(), null)
        on conflict (user_id) do update
            set active = true,
                note = case when excluded.note = '' then community_private.beta_grants.note else excluded.note end,
                granted_at = now(),
                revoked_at = null;
    end if;

    insert into community_private.admin_grant_audit(action, target_request_id, target_user_id, offer, cents, grant_beta, note)
    values ('grant_support', p_request_id, v_req.user_id, v_req.offer, v_req.cents, v_unlock_beta, v_note);

    return jsonb_build_object(
        'requestId', p_request_id,
        'userId', v_req.user_id,
        'email', v_email,
        'offer', v_req.offer,
        'cents', v_req.cents,
        'status', 'verified',
        'betaAccessUnlocked', (v_unlock_beta or exists(select 1 from community_private.beta_grants where user_id = v_req.user_id and active))
    );
end;
$$;

create function public.lt_admin_revoke_support(p_request_id uuid, p_reason text default '')
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_req record;
    v_remaining integer := 0;
    v_reason text := coalesce(left(trim(p_reason), 300), '');
begin
    select sr.*, u.email into v_req
    from community_private.support_requests sr
    join auth.users u on u.id = sr.user_id
    where sr.id = p_request_id;

    if not found then
        raise exception using errcode = '22023', message = 'Support request not found.';
    end if;

    select greatest(
        v.cents - coalesce((select sum(r.cents) from community_private.support_reversals r where r.receipt_id = v.receipt_id), 0),
        0
    )::integer
    into v_remaining
    from community_private.verified_support v
    where v.receipt_id = p_request_id;

    if coalesce(v_remaining, 0) > 0 then
        insert into community_private.support_reversals(reversal_id, receipt_id, cents)
        values (gen_random_uuid(), p_request_id, v_remaining);
    end if;

    update community_private.support_requests
    set status = 'cancelled'
    where id = p_request_id;

    update community_private.beta_grants
    set active = false,
        revoked_at = now(),
        note = case when v_reason = '' then note else v_reason end
    where user_id = v_req.user_id;

    insert into community_private.admin_grant_audit(action, target_request_id, target_user_id, offer, cents, grant_beta, note)
    values ('revoke_support', p_request_id, v_req.user_id, v_req.offer, coalesce(v_remaining, 0), false, v_reason);

    return jsonb_build_object(
        'requestId', p_request_id,
        'userId', v_req.user_id,
        'email', v_req.email,
        'status', 'cancelled',
        'reversedCents', coalesce(v_remaining, 0),
        'betaAccessUnlocked', false
    );
end;
$$;

create function public.lt_admin_grant_guest_donation(p_request_id uuid, p_admin_note text default '')
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_req record;
    v_note text := coalesce(left(trim(p_admin_note), 300), '');
begin
    select * into v_req
    from community_private.guest_donation_requests
    where id = p_request_id;

    if not found then
        raise exception using errcode = '22023', message = 'Guest donation request not found.';
    end if;

    insert into community_private.verified_support(receipt_id, user_id, offer, cents, guest_tag, hardware_fingerprint_hash)
    values (p_request_id, null, 'donation', v_req.cents, v_req.guest_tag, v_req.hardware_fingerprint_hash)
    on conflict (receipt_id) do nothing;

    update community_private.guest_donation_requests
    set status = 'verified'
    where id = p_request_id;

    insert into community_private.admin_grant_audit(action, target_request_id, target_guest_tag, offer, cents, grant_beta, note)
    values ('grant_guest_donation', p_request_id, v_req.guest_tag, 'donation', v_req.cents, false, v_note);

    return jsonb_build_object(
        'requestId', p_request_id,
        'guestTag', v_req.guest_tag,
        'cents', v_req.cents,
        'status', 'verified'
    );
end;
$$;

create function public.lt_admin_set_beta_access(p_user_id uuid, p_active boolean, p_note text default '')
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_email text;
    v_note text := coalesce(left(trim(p_note), 300), '');
begin
    select email into v_email from auth.users where id = p_user_id;
    if v_email is null then
        raise exception using errcode = '22023', message = 'User ID not found in auth.users.';
    end if;

    insert into community_private.beta_grants(user_id, active, note, granted_at, revoked_at)
    values (
        p_user_id,
        coalesce(p_active, false),
        v_note,
        now(),
        case when coalesce(p_active, false) then null else now() end
    )
    on conflict (user_id) do update
        set active = coalesce(p_active, false),
            note = case when excluded.note = '' then community_private.beta_grants.note else excluded.note end,
            granted_at = case when coalesce(p_active, false) then now() else community_private.beta_grants.granted_at end,
            revoked_at = case when coalesce(p_active, false) then null else now() end;

    insert into community_private.admin_grant_audit(action, target_user_id, grant_beta, note)
    values ('set_beta_access', p_user_id, coalesce(p_active, false), v_note);

    return jsonb_build_object(
        'userId', p_user_id,
        'email', v_email,
        'explicitBetaActive', coalesce(p_active, false)
    );
end;
$$;

create function public.lt_admin_register_beta_release(
    p_version text,
    p_runtime text,
    p_storage_path text,
    p_size bigint,
    p_sha256 text,
    p_notes text default ''
) returns jsonb language plpgsql security definer set search_path = '' as $$
declare
    v_version text := trim(coalesce(p_version, ''));
    v_runtime text := lower(trim(coalesce(p_runtime, '')));
    v_path text := trim(coalesce(p_storage_path, ''));
    v_hash text := upper(trim(coalesce(p_sha256, '')));
    v_notes text := coalesce(p_notes, '');
    v_id uuid;
begin
    if v_version !~ '^[0-9]+\.[0-9]+\.[0-9]+$' then
        raise exception using errcode = '22023', message = 'Version must match X.Y.Z.';
    end if;
    if v_runtime not in ('win-x64', 'osx-arm64', 'osx-x64') then
        raise exception using errcode = '22023', message = 'Invalid target runtime.';
    end if;
    if p_size is null or p_size <= 0 or v_hash !~ '^[0-9A-F]{64}$' or char_length(v_path) < 3 then
        raise exception using errcode = '22023', message = 'Invalid package path, size, or SHA-256 checksum.';
    end if;

    update community_private.beta_releases
    set active = false
    where runtime = v_runtime and active;

    insert into community_private.beta_releases(version, runtime, storage_bucket, storage_path, size, sha256, notes, active, published_at)
    values (v_version, v_runtime, 'beta-releases', v_path, p_size, v_hash, v_notes, true, now())
    on conflict (version, runtime) do update
        set storage_path = excluded.storage_path,
            size = excluded.size,
            sha256 = excluded.sha256,
            notes = excluded.notes,
            active = true,
            published_at = now()
    returning id into v_id;

    insert into community_private.admin_grant_audit(action, target_request_id, note)
    values ('publish_beta_release', v_id, v_version || ' (' || v_runtime || ')');

    return jsonb_build_object(
        'releaseId', v_id,
        'version', v_version,
        'runtime', v_runtime,
        'storageBucket', 'beta-releases',
        'storagePath', v_path,
        'size', p_size,
        'sha256', v_hash
    );
end;
$$;

revoke all on function public.lt_admin_list_requests(text, integer) from public, anon, authenticated;
revoke all on function public.lt_admin_grant_support(uuid, boolean, text) from public, anon, authenticated;
revoke all on function public.lt_admin_revoke_support(uuid, text) from public, anon, authenticated;
revoke all on function public.lt_admin_grant_guest_donation(uuid, text) from public, anon, authenticated;
revoke all on function public.lt_admin_set_beta_access(uuid, boolean, text) from public, anon, authenticated;
revoke all on function public.lt_admin_register_beta_release(text, text, text, bigint, text, text) from public, anon, authenticated;

do $$
begin
    if exists (select 1 from pg_roles where rolname = 'service_role') then
        grant execute on function public.lt_admin_list_requests(text, integer) to service_role;
        grant execute on function public.lt_admin_grant_support(uuid, boolean, text) to service_role;
        grant execute on function public.lt_admin_revoke_support(uuid, text) to service_role;
        grant execute on function public.lt_admin_grant_guest_donation(uuid, text) to service_role;
        grant execute on function public.lt_admin_set_beta_access(uuid, boolean, text) to service_role;
        grant execute on function public.lt_admin_register_beta_release(text, text, text, bigint, text, text) to service_role;
    end if;
end $$;
