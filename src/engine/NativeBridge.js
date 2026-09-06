/**
 * KIDCUT NATIVE BRIDGE
 * Connects the web UI directly to the high-performance C# / FFmpeg background engine.
 * Automatically discovers the active port in parallel, handles direct file selection,
 * and eliminates in-browser memory bloat.
 */

let cachedApiBase = null;

export async function getNativeApiBase() {
  // 1. Verify cached base if present
  if (cachedApiBase) {
    try {
      const res = await fetch(`${cachedApiBase}/api/ping`, { method: 'GET', signal: AbortSignal.timeout(2000) });
      if (res.ok) {
        const data = await res.json();
        if (data && data.status === 'ok') return cachedApiBase;
      }
    } catch (_) {
      cachedApiBase = null;
    }
  }

  // 2. Direct same-origin probe (fastest, zero CORS)
  try {
    const res = await fetch('/api/ping', { method: 'GET', signal: AbortSignal.timeout(2000) });
    if (res.ok) {
      const data = await res.json();
      if (data && data.status === 'ok') {
        cachedApiBase = window.location.origin;
        return cachedApiBase;
      }
    }
  } catch (_) {}

  // 3. Parallel probe across candidate local ports
  const candidates = [
    window.location.origin,
    'http://127.0.0.1:5173',
    'http://127.0.0.1:5174',
    'http://127.0.0.1:5175',
    'http://localhost:5173',
    'http://localhost:5174'
  ];

  // Try port file if available
  try {
    const portRes = await fetch('/kidcut_port.json?t=' + Date.now(), { signal: AbortSignal.timeout(1000) });
    if (portRes.ok) {
      const portData = await portRes.json();
      if (portData && portData.port) {
        candidates.unshift(`http://127.0.0.1:${portData.port}`);
      }
    }
  } catch (_) {}

  const uniqueCandidates = Array.from(new Set(candidates));

  const probePromises = uniqueCandidates.map(async (base) => {
    try {
      const res = await fetch(`${base}/api/ping`, { method: 'GET', signal: AbortSignal.timeout(2500) });
      if (res.ok) {
        const data = await res.json();
        if (data && data.status === 'ok') {
          return base;
        }
      }
    } catch (_) {}
    throw new Error('Unreachable');
  });

  try {
    const winner = await Promise.any(probePromises);
    cachedApiBase = winner;
    console.log(`[KidCut Native Bridge] Conectado al backend nativo en: ${winner}`);
    return winner;
  } catch (_) {
    return null;
  }
}

export async function nativeFetch(endpoint, options = {}) {
  const base = await getNativeApiBase();
  const fullUrl = base ? `${base}${endpoint}` : endpoint;
  return await fetch(fullUrl, options);
}

export async function checkNativeBackend() {
  // 1. First probe current origin directly
  try {
    const res = await fetch('/api/ping', { method: 'GET', signal: AbortSignal.timeout(2000) });
    if (res.ok) {
      const data = await res.json();
      if (data && data.status === 'ok') {
        cachedApiBase = window.location.origin;
        return data;
      }
    }
  } catch (_) {}

  // 2. Discover active base URL
  const base = await getNativeApiBase();
  if (!base) return null;

  try {
    const res = await fetch(`${base}/api/ping`, { method: 'GET', signal: AbortSignal.timeout(2500) });
    if (res.ok) {
      const data = await res.json();
      if (data && data.status === 'ok') return data;
    }
  } catch (_) {}
  return null;
}

export async function pickNativeFile() {
  try {
    const res = await nativeFetch('/api/pick-file');
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.warn('Error al llamar a /api/pick-file:', err);
  }
  return { success: false };
}

export async function uploadMediaToDisk(payload, fileName) {
  try {
    const encodedName = encodeURIComponent(fileName || `media_${Date.now()}.dat`);
    const res = await nativeFetch(`/api/upload-media?name=${encodedName}`, {
      method: 'POST',
      body: payload
    });
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.warn('Error al subir medio a disco:', err);
  }
  return { success: false };
}
