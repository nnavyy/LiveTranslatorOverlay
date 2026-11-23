import { createClient } from 'https://esm.sh/@supabase/supabase-js@2';

const VALID_RUNTIMES = new Set(['win-x64', 'osx-arm64', 'osx-x64']);
const SIGNED_URL_TTL_SECONDS = 600;

function jsonResponse(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
    },
  });
}

Deno.serve(async (req: Request): Promise<Response> => {
  if (req.method === 'OPTIONS') {
    return new Response(null, {
      status: 204,
      headers: {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Headers': 'authorization, x-client-info, apikey, content-type',
        'Access-Control-Allow-Methods': 'POST, OPTIONS',
      },
    });
  }

  if (req.method !== 'POST') {
    return jsonResponse({ error: 'Method not allowed.' }, 405);
  }

  const authHeader = req.headers.get('Authorization') ?? '';
  if (!authHeader.startsWith('Bearer ') || authHeader.length < 24) {
    return jsonResponse({ error: 'Sign in with a developer-verified US$100 Lifetime Insider account.' }, 401);
  }

  const supabaseUrl = Deno.env.get('SUPABASE_URL') ?? '';
  const supabaseAnonKey = Deno.env.get('SUPABASE_ANON_KEY') ?? '';
  const serviceRoleKey = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') ?? '';
  if (!supabaseUrl || !supabaseAnonKey || !serviceRoleKey) {
    return jsonResponse({ error: 'Server configuration incomplete.' }, 500);
  }

  let runtime = 'win-x64';
  let hwidHash: string | null = null;
  try {
    const payload = await req.json();
    if (typeof payload?.runtime === 'string') {
      runtime = payload.runtime.trim().toLowerCase();
    }
    if (typeof payload?.hwidHash === 'string' && payload.hwidHash.trim().length > 0) {
      hwidHash = payload.hwidHash.trim().toLowerCase();
    }
  } catch {
    return jsonResponse({ error: 'Invalid JSON request body.' }, 400);
  }

  if (!VALID_RUNTIMES.has(runtime)) {
    return jsonResponse({ error: 'Unsupported target runtime.' }, 400);
  }

  const userClient = createClient(supabaseUrl, supabaseAnonKey, {
    global: { headers: { Authorization: authHeader } },
    auth: { persistSession: false, autoRefreshToken: false },
  });

  const { data: meta, error: rpcError } = await userClient.rpc('lt_get_beta_release_metadata', {
    p_runtime: runtime,
    p_hwid_hash: hwidHash,
  });

  if (rpcError) {
    const isForbidden = rpcError.code === '42501' || /insider grant|sign-in|jwt|auth/i.test(rpcError.message);
    return jsonResponse(
      { error: rpcError.message || 'Beta access denied.' },
      isForbidden ? 403 : 400,
    );
  }

  if (!meta || meta.available !== true) {
    return jsonResponse({ error: 'No beta release published for this platform yet.' }, 404);
  }

  const adminClient = createClient(supabaseUrl, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  const { data: signedData, error: signError } = await adminClient.storage
    .from(meta.storageBucket)
    .createSignedUrl(meta.storagePath, SIGNED_URL_TTL_SECONDS);

  if (signError || !signedData?.signedUrl) {
    return jsonResponse({ error: 'Could not issue time-limited beta download URL.' }, 500);
  }

  return jsonResponse(
    {
      line: 'ml',
      channel: 'beta',
      version: meta.version,
      packages: {
        [runtime]: {
          url: signedData.signedUrl,
          size: meta.size,
          sha256: meta.sha256,
        },
      },
    },
    200,
  );
});
