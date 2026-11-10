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
    { rank: 1, nickname: 'Alice', score: 21, medals: ['Insider'] },
    { rank: 2, nickname: 'Bob', score: 11, medals: [] }
  ]);
  assert.deepEqual((await board('month')).entries.map(row => [row.rank, row.nickname, row.score]), [[1, 'Alice', 11], [1, 'Bob', 11]]);
  assert.deepEqual((await board('all', 'BO')).entries.map(row => [row.rank, row.nickname]), [[2, 'Bob']]);
  assert.deepEqual((await board('all', '', 1)).entries.map(row => row.nickname), ['Bob']);
  assert.deepEqual((await board('supporters')).entries, [
    { rank: 1, nickname: 'Alice', score: 10000, medals: ['Insider'] },
    { rank: 1, nickname: 'Bob', score: 10000, medals: [] }
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
  assert.equal((await db.query('select count(*)::integer as count from community_private.manual_purchase_requests')).rows[0].count, 3);
  assert.equal((await db.query('select count(*)::integer as count from community_private.verified_support')).rows[0].count, 4);
  console.log('Community SQL passed: consent, closed defaults, overlap, UTC boundaries, ties, pagination, refunds, and private access.');
  console.log('Manual purchase preview passed: closed by default, verified accounts only, private requests, idempotent retry, and no credit.');
} finally { await db.close(); }
