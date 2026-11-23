import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';

const require = createRequire(import.meta.url);
const dependencyDirectory = process.argv[2];
assert(dependencyDirectory, 'Pass a directory with @electric-sql/pglite installed.');
const { PGlite } = await import(pathToFileURL(require.resolve('@electric-sql/pglite', { paths: [dependencyDirectory] })).href);
const db = new PGlite();
const alice = '00000000-0000-4000-8000-000000000001';
const bob = '00000000-0000-4000-8000-000000000002';
const hidden = '00000000-0000-4000-8000-000000000003';
const unverified = '00000000-0000-4000-8000-000000000004';

try {
  await db.exec(`
    create role anon; create role authenticated;
    create schema auth;
    create table auth.users (id uuid primary key, email text, email_confirmed_at timestamptz);
    create function auth.uid() returns uuid language sql stable
      as $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
  `);
  await db.exec(await readFile(new URL('../../supabase/migrations/20261110152000_community_rankings.sql', import.meta.url), 'utf8'));
  await db.exec(await readFile(new URL('../../supabase/migrations/20261111100000_manual_purchase_preview.sql', import.meta.url), 'utf8'));
  await db.exec(await readFile(new URL('../../supabase/migrations/20261111210000_public_profile_support.sql', import.meta.url), 'utf8'));
  await db.exec(await readFile(new URL('../../supabase/migrations/20261123120000_account_login_donations_and_tiers.sql', import.meta.url), 'utf8'));
  await db.exec(await readFile(new URL('../../supabase/migrations/20261123180000_guest_donations_hwid_and_antiabuse.sql', import.meta.url), 'utf8'));
  await db.exec(await readFile(new URL('../../supabase/migrations/20261123210000_owner_grants_and_private_beta.sql', import.meta.url), 'utf8'));
  const board = async (kind, search = '', offset = 0) =>
    (await db.query('select public.lt_community_board($1, $2, $3) as board', [kind, search, offset])).rows[0].board;

  await db.exec('set role anon');
  assert.deepEqual(await board('all'), { open: false, entries: [] });
  await assert.rejects(db.query('select * from community_private.profiles'), { code: '42501' });
  await assert.rejects(db.query("select community_private.save_profile('Guest', false, false, false, false)"), { code: '42501' });
  await db.exec('reset role');

  for (const [id, confirm] of [[alice, true], [bob, true], [hidden, true], [unverified, false]]) {
    await db.query('insert into auth.users values ($1, $2, case when $3 then now() end)', [id, `${id}@example.test`, confirm]);
  }
  const setUser = (id) => db.query("select set_config('request.jwt.claim.sub', $1, false)", [id]);
  const save = (name, usage, support, medals) =>
    db.query('select community_private.save_profile($1, true, $2, $3, $4)', [name, usage, support, medals]);
  await setUser(alice);
  await save('Alice', true, true, true);
  await setUser(bob);
  await assert.rejects(save('ＡＬＩＣＥ', true, true, true), { code: '23505' });
  await save('Bob', true, true, true);
  await setUser(hidden);
  await save('Hidden', false, false, false);
  const upload = (seconds) => db.query(`select community_private.submit_usage(
    '00000000-0000-4000-8000-000000000015', now() - make_interval(secs => $1), now())`, [seconds]);
  await upload(15);
  await upload(15);
  assert.equal((await db.query('select count(*)::integer as n from community_private.usage_segments where user_id = $1', [hidden])).rows[0].n, 1);
  await assert.rejects(upload(21), { code: '22023' });
  await db.query("select community_private.save_profile('Hidden', false, false, false, false)");
  await assert.rejects(upload(15), { code: '42501' });
  await setUser(unverified);
  await assert.rejects(save('Unverified', true, true, true), { code: '42501' });

  const write = (user, segment, start, end) => db.query(`
    with m as (select date_trunc('month', now() at time zone 'UTC') at time zone 'UTC' as midnight)
    insert into community_private.usage_segments
    select $1, $2, midnight + make_interval(secs => $3), midnight + make_interval(secs => $4) from m
  `, [user, segment, start, end]);
  await write(alice, '00000000-0000-4000-8000-000000000011', -10, 5);
  await write(alice, '00000000-0000-4000-8000-000000000012', 1, 11);
  await write(bob, '00000000-0000-4000-8000-000000000013', 0, 11);
  await write(hidden, '00000000-0000-4000-8000-000000000014', 0, 20);

  await db.query(`insert into community_private.verified_support (receipt_id, user_id, offer, cents) values
    ('00000000-0000-4000-8000-000000000021',$1,'supporter',1999),
    ('00000000-0000-4000-8000-000000000022',$1,'insider',10000),
    ('00000000-0000-4000-8000-000000000023',$2,'donation',10000),
    ('00000000-0000-4000-8000-000000000024',$3,'donation',20000)`, [alice, bob, hidden]);
  await db.exec(`insert into community_private.support_reversals (reversal_id, receipt_id, cents)
    values ('00000000-0000-4000-8000-000000000031','00000000-0000-4000-8000-000000000021',1999)`);
  await db.exec('update community_private.settings set rankings_open = true');

  await db.exec('set role anon');
  assert.deepEqual((await board('all')).entries, [
    { rank: 1, nickname: 'Alice', score: 21, medals: ['Insider'], supportCents: 10000 },
    { rank: 2, nickname: 'Bob', score: 11, medals: [], supportCents: 10000 }
  ]);
  assert.deepEqual((await board('month')).entries.map(row => [row.rank, row.nickname, row.score]), [[1, 'Alice', 11], [1, 'Bob', 11]]);
  assert.deepEqual((await board('all', 'BO')).entries.map(row => [row.rank, row.nickname]), [[2, 'Bob']]);
  assert.deepEqual((await board('all', '', 1)).entries.map(row => row.nickname), ['Bob']);
  assert.deepEqual((await board('supporters')).entries, [
    { rank: 1, nickname: 'Alice', score: 10000, medals: ['Insider'], supportCents: 10000 },
    { rank: 1, nickname: 'Bob', score: 10000, medals: [], supportCents: 10000 }
  ]);
  await assert.rejects(db.query('select * from community_private.verified_support'), { code: '42501' });
  const requestPurchase = async (offer) => (await db.query('select public.lt_request_manual_purchase($1) as id', [offer])).rows[0].id;
  await assert.rejects(requestPurchase('supporter'), { code: '42501' });
  await db.exec('reset role');
  await setUser(alice);
  await db.exec('set role authenticated');
  await assert.rejects(requestPurchase('supporter'), { code: '42501' });
  await db.exec('reset role');
  await db.exec('update community_private.settings set manual_purchase_preview_open = true');
  await setUser(unverified);
  await db.exec('set role authenticated');
  await assert.rejects(requestPurchase('supporter'), { code: '42501' });
  await setUser(alice);
  await assert.rejects(requestPurchase('donation'), { code: '22023' });
  const firstRequest = await requestPurchase('supporter');
  assert.match(firstRequest, /^[\da-f-]{36}$/);
  assert.equal(await requestPurchase('supporter'), firstRequest);
  assert.notEqual(await requestPurchase('insider'), firstRequest);
  await assert.rejects(db.query('select * from community_private.manual_purchase_requests'), { code: '42501' });
  await setUser(bob);
  assert.notEqual(await requestPurchase('supporter'), firstRequest);
  await db.exec('reset role');
  await setUser(alice);
  await save('Alice', true, false, true);
  await db.exec('set role anon');
  assert.deepEqual((await board('all', 'alice')).entries[0],
    { rank: 1, nickname: 'Alice', score: 21, medals: ['Insider'], supportCents: null });
  assert.equal((await board('supporters', 'alice')).entries.length, 0);
  await db.exec('reset role');
  assert.equal((await db.query('select count(*)::integer as count from community_private.manual_purchase_requests')).rows[0].count, 3);
  assert.equal((await db.query('select count(*)::integer as count from community_private.verified_support')).rows[0].count, 4);

  // Account summary, selectable licenses, and custom donation tracking checks
  await db.exec('set role anon');
  await assert.rejects(db.query("select public.lt_submit_support_request('donation', 500, '')"), { code: '42501' });
  await assert.rejects(db.query('select public.lt_my_account_summary()'), { code: '42501' });
  await db.exec('reset role');

  await setUser(unverified);
  await db.exec('set role authenticated');
  await assert.rejects(db.query("select public.lt_submit_support_request('supporter', 1999, '')"), { code: '42501' });
  await assert.rejects(db.query('select public.lt_my_account_summary()'), { code: '42501' });

  await setUser(alice);
  await assert.rejects(db.query("select public.lt_submit_support_request('supporter', 500, '')"), { code: '22023' });
  await assert.rejects(db.query("select public.lt_submit_support_request('donation', 0, '')"), { code: '22023' });
  const reqRow = (await db.query("select public.lt_submit_support_request('donation', 1500, 'keep building!') as res")).rows[0].res;
  assert.equal(reqRow.offer, 'donation');
  assert.equal(reqRow.cents, 1500);
  assert.equal(reqRow.status, 'pending');
  await assert.rejects(db.query('select * from community_private.support_requests'), { code: '42501' });

  const aliceSummary = (await db.query('select public.lt_my_account_summary() as res')).rows[0].res;
  assert.equal(aliceSummary.activeTier, 'insider');
  assert.equal(aliceSummary.verifiedTotalCents, 10000);
  assert.equal(aliceSummary.pendingTotalCents, 1500);
  assert.equal(aliceSummary.betaAccessUnlocked, true);
  assert(aliceSummary.history.length >= 2);
  await db.exec('reset role');

  // Guest donations (no login required -> User#XXXXX) & HWID anti-abuse checks
  const hwidHighBit = 'deadbeef' + '0'.repeat(56); // >= 0x80000000 unsigned parity check: 0xdeadbeef % 90000 + 10000 = 85999 + 10000 = 95999 -> Wait let Node verify exact uint32!
  const expectedTag = 'User#' + String(((parseInt(hwidHighBit.slice(0, 8), 16) >>> 0) % 90000) + 10000).padStart(5, '0');
  await db.exec('set role anon');
  for (let i = 0; i < 5; i++) {
    const g = (await db.query('select public.lt_submit_guest_donation($1, $2, $3) as res', [500, hwidHighBit, 'anon coffee'])).rows[0].res;
    assert.equal(g.guestTag, expectedTag);
    assert.equal(g.cents, 500);
    assert.equal(g.status, 'pending');
  }
  await assert.rejects(db.query("select public.lt_submit_guest_donation(500, $1, 'spam')", [hwidHighBit]), { code: '42501' });
  await assert.rejects(db.query("select * from community_private.guest_donation_requests"), { code: '42501' });
  await db.exec('reset role');

  // Verify guest donation in verified_support renders anonymously as User#XXXXX on supporters board
  await db.query(`insert into community_private.verified_support (receipt_id, user_id, offer, cents, guest_tag, hardware_fingerprint_hash) values
    ('00000000-0000-4000-8000-000000000091', null, 'donation', 5000, $1, $2)`, [expectedTag, hwidHighBit]);
  await db.exec('set role anon');
  const fullSupporters = (await board('supporters', '')).entries;
  assert.deepEqual(fullSupporters.map((e) => [e.rank, e.nickname, e.supportCents]), [
    [1, 'Bob', 10000],
    [2, expectedTag, 5000]
  ]);
  const guestBoard = (await board('supporters', 'user#')).entries;
  assert.equal(guestBoard.length, 1);
  assert.deepEqual(guestBoard[0], { rank: 2, nickname: expectedTag, score: 5000, medals: [], supportCents: 5000 });
  await db.exec('reset role');

  // Verify HWID Sybil protection blocks overlapping usage segments across distinct accounts
  const hwid2 = 'cafebabe' + '1'.repeat(56);
  const segT0 = new Date(Date.now() - 60000).toISOString();
  const segT1 = new Date(Date.now() - 50000).toISOString();
  await setUser(alice);
  await db.query('select community_private.submit_usage_with_device($1, $2, $3, $4)',
    ['00000000-0000-4000-8000-000000000051', segT0, segT1, hwid2]);
  await setUser(bob);
  await assert.rejects(
    db.query('select community_private.submit_usage_with_device($1, $2, $3, $4)',
      ['00000000-0000-4000-8000-000000000052', segT0, segT1, hwid2]),
    { code: '42501' });
  await db.exec('reset role');

  // Owner 1-by-1 grants & authenticated private beta gating checks
  const charlie = '00000000-0000-4000-8000-000000000005';
  await db.query('insert into auth.users values ($1, $2, now())', [charlie, `${charlie}@example.test`]);

  await db.exec('set role anon');
  await assert.rejects(db.query("select public.lt_get_beta_release_metadata('win-x64')"), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_list_requests('all', 10)"), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_grant_support($1)", [charlie]), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_revoke_support($1)", [charlie]), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_grant_guest_donation($1)", [charlie]), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_set_beta_access($1, true)", [charlie]), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_register_beta_release('1.1.2', 'win-x64', 'v1.1.2/win-x64/LiveTranslatorOverlay-Windows-x64.zip', 123, repeat('a', 64), '')"), { code: '42501' });
  await db.exec('reset role');

  await setUser(charlie);
  await db.exec('set role authenticated');
  await assert.rejects(db.query("select public.lt_get_beta_release_metadata('win-x64')"), { code: '42501' });
  await assert.rejects(db.query("select public.lt_admin_list_requests('all', 10)"), { code: '42501' });
  const charlieReq = (await db.query("select public.lt_submit_support_request('insider', 10000, 'Beta access please') as res")).rows[0].res;
  const charlieReqId = charlieReq.requestId;
  await db.exec('reset role');

  await db.query("select public.lt_admin_register_beta_release('1.1.2', 'win-x64', 'v1.1.2/win-x64/LiveTranslatorOverlay-Windows-x64.zip', 654321, $1, 'Private beta build')", ['a'.repeat(64)]);
  const adminPending = (await db.query("select public.lt_admin_list_requests('pending', 50) as res")).rows[0].res;
  assert(adminPending.accountRequests.some((r) => r.requestId === charlieReqId && r.email === `${charlie}@example.test` && r.offer === 'insider'));

  const granted = (await db.query("select public.lt_admin_grant_support($1, null, 'Verified payment') as res", [charlieReqId])).rows[0].res;
  assert.equal(granted.status, 'verified');
  assert.equal(granted.betaAccessUnlocked, true);

  await setUser(charlie);
  await db.exec('set role authenticated');
  const charlieSummary = (await db.query('select public.lt_my_account_summary() as res')).rows[0].res;
  assert.equal(charlieSummary.activeTier, 'insider');
  assert.equal(charlieSummary.verifiedTotalCents, 10000);
  assert.equal(charlieSummary.betaAccessUnlocked, true);
  const betaMeta = (await db.query("select public.lt_get_beta_release_metadata('win-x64') as res")).rows[0].res;
  assert.equal(betaMeta.available, true);
  assert.equal(betaMeta.version, '1.1.2');
  assert.equal(betaMeta.size, 654321);
  assert.equal(betaMeta.sha256, 'A'.repeat(64));
  await db.exec('reset role');

  const revoked = (await db.query("select public.lt_admin_revoke_support($1, 'Refund issued') as res", [charlieReqId])).rows[0].res;
  assert.equal(revoked.status, 'cancelled');
  assert.equal(revoked.betaAccessUnlocked, false);
  await setUser(charlie);
  await db.exec('set role authenticated');
  await assert.rejects(db.query("select public.lt_get_beta_release_metadata('win-x64')"), { code: '42501' });
  assert.equal((await db.query('select public.lt_my_account_summary() as res')).rows[0].res.betaAccessUnlocked, false);
  await db.exec('reset role');

  await db.query("select public.lt_admin_set_beta_access($1, true, 'Invited beta tester')", [charlie]);
  await setUser(charlie);
  await db.exec('set role authenticated');
  assert.equal((await db.query("select public.lt_get_beta_release_metadata('win-x64') as res")).rows[0].res.available, true);
  await db.exec('reset role');
  await db.query("select public.lt_admin_set_beta_access($1, false, 'Removed')", [charlie]);

  const hwidGuest3 = 'feedface' + '2'.repeat(56);
  const expectedGuest3Tag = 'User#' + String(((parseInt(hwidGuest3.slice(0, 8), 16) >>> 0) % 90000) + 10000).padStart(5, '0');
  await db.exec('set role anon');
  const guest3Req = (await db.query("select public.lt_submit_guest_donation(2500, $1, 'Guest donation') as res", [hwidGuest3])).rows[0].res;
  await db.exec('reset role');
  const grantedGuest = (await db.query("select public.lt_admin_grant_guest_donation($1, 'Confirmed') as res", [guest3Req.requestId])).rows[0].res;
  assert.equal(grantedGuest.status, 'verified');
  assert.equal(grantedGuest.guestTag, expectedGuest3Tag);

  console.log('Community SQL passed: consent, closed defaults, overlap, UTC boundaries, ties, pagination, refunds, and private access.');
  console.log('Manual purchase preview & account tracking passed: verified accounts, tier/donation validation, private ledger, and summary projection.');
  console.log('Guest donations & HWID anti-abuse passed: User#XXXXX parity, 5/24h anon rate limit, supporter rank ordering, and cross-account Sybil guard.');
  console.log('Owner 1-by-1 grants & private beta metadata passed: admin RLS, support+beta unlock, revocation, and guest verification.');
} finally { await db.close(); }
