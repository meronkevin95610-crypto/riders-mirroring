// Hand-transpiled from src/main/ws-server.ts — types stripped, runtime preserved.
// Sync contract: if ws-server.ts changes, re-transpile this file. The test
// runner (run-tests.mjs) loads this file directly via dynamic import().
//
// Differences from the .ts:
//   - `interface Session { ... }`     → plain const + JSDoc
//   - `interface IssuedToken { ... }` → const shape + JSDoc
//   - `interface SignalingEvents`     → const EVENT_NAMES
//   - `function reject(...)` signature stripped
//   - `private readonly`, `private readonly` → dropped
//   - generic `<K extends keyof ...>` → JSDoc-only
//   - `as const` / `satisfies` dropped
//
// Behavior is IDENTICAL — this file is meant to be a runtime mirror of the
// production logic for in-sandbox validation.

import { WebSocketServer, WebSocket } from 'ws';
import { randomUUID } from 'node:crypto';
import { EventEmitter } from 'node:events';

/** Per-session state. */
const Session = {
  token: 'string',
  socket: 'WebSocket|null',
  deviceType: "'android'|'ios'|null",
  issuedAt: 'number',
  lastDisconnectAt: 'number|null',
  rendererBound: 'boolean',
};

/** Token authority entry. */
const IssuedToken = {
  issuedAt: 'number',
  revokedAt: 'number|undefined',
};

const EVENT_NAMES = [
  'session:created',
  'session:reconnected',
  'session:expired',
  'session:renderer-bound',
  'session:client-signal',
  'session:closed',
];

function send(socket, msg) {
  if (!socket || socket.readyState !== WebSocket.OPEN) return;
  socket.send(JSON.stringify(msg));
}

function reject(socket, code, message, closeCode = 1008) {
  send(socket, { kind: 'error', code, message });
  try {
    socket.close(closeCode, message);
  } catch {
    /* socket may already be closing */
  }
}

export class SignalingServer extends EventEmitter {
  constructor(preferredPort = 0, opts = {}) {
    super();
    this.preferredPort = preferredPort;
    this.tokenTtlMs = opts.tokenTtlMs ?? 5 * 60 * 1000;
    this.wss = new WebSocketServer({ port: preferredPort });
    this.sessions = new Map();
    this.issuedTokens = new Map();
  }

  async listen() {
    await new Promise((resolve, reject) => {
      this.wss.once('listening', () => resolve());
      this.wss.once('error', reject);
    });

    this.wss.on('connection', (socket, req) => {
      const url = new URL(req.url ?? '/', 'http://localhost');
      const tokenFromUrl = url.searchParams.get('token');
      this.handleConnection(socket, tokenFromUrl);
    });
  }

  // ─── Token authority ──────────────────────────────────────

  registerToken() {
    const token = randomUUID();
    this.issuedTokens.set(token, { issuedAt: Date.now() });
    return token;
  }

  registerTokenRaw(token) {
    this.issuedTokens.set(token, { issuedAt: Date.now() });
  }

  revokeToken(token, reason = 'revoked') {
    const entry = this.issuedTokens.get(token);
    if (!entry) return false;
    entry.revokedAt = Date.now();
    return this.revokeSession(token, reason);
  }

  isWithinTtl(token, now = Date.now()) {
    const entry = this.issuedTokens.get(token);
    if (!entry) return false;
    return now - entry.issuedAt <= this.tokenTtlMs;
  }

  // ─── Connection lifecycle ─────────────────────────────────

  handleConnection(socket, tokenFromUrl) {
    if (!tokenFromUrl) {
      reject(socket, 'unauthorized', 'missing token query param');
      return;
    }

    const now = Date.now();
    const entry = this.issuedTokens.get(tokenFromUrl);
    if (!entry || entry.revokedAt !== undefined || !this.isWithinTtl(tokenFromUrl, now)) {
      const reason = !entry
        ? 'token not registered'
        : entry.revokedAt !== undefined
          ? 'token revoked'
          : 'token expired';
      reject(socket, 'token_expired', reason);

      const existing = this.sessions.get(tokenFromUrl);
      if (existing) {
        this.emit('session:expired', existing);
        this.emit('session:closed', tokenFromUrl, reason);
        this.sessions.delete(tokenFromUrl);
      }
      return;
    }

    let session = this.sessions.get(tokenFromUrl);
    if (!session) {
      session = {
        token: tokenFromUrl,
        socket: null,
        deviceType: null,
        issuedAt: entry.issuedAt,
        lastDisconnectAt: null,
        rendererBound: false,
      };
      this.sessions.set(tokenFromUrl, session);
      this.emit('session:created', session);
    } else if (session.socket && session.socket.readyState === WebSocket.OPEN) {
      // Reconnect within TTL but a previous socket is still hanging on —
      // close it to make room for the new one. We attach the new socket
      // synchronously so `session.socket` points to the new socket BEFORE
      // the close event of the stale one runs — that way the close handler
      // (which nulls `session.socket` only if it still points to the stale
      // socket) sees the new socket and is a no-op.
      const stale = session.socket;
      try {
        stale.close(1000, 'replaced by new socket');
      } catch {
        /* already closed */
      }
      this.attachSocket(session, socket);
    } else {
      this.emit('session:reconnected', session);
      this.attachSocket(session, socket);
    }
  }

  sendToClient(token, signal) {
    const s = this.sessions.get(token);
    if (!s) return false;
    send(s.socket, signal);
    return true;
  }

  bindRenderer(token) {
    const s = this.sessions.get(token);
    if (!s) return null;
    s.rendererBound = true;
    this.emit('session:renderer-bound', s);
    return s;
  }

  listSessions() {
    return [...this.sessions.values()];
  }

  revokeSession(token, reason = 'revoked') {
    const s = this.sessions.get(token);
    if (!s) return false;
    send(s.socket, { kind: 'bye', sessionToken: token, reason });
    try {
      s.socket?.close(1000, reason);
    } catch {
      /* already closed */
    }
    this.sessions.delete(token);
    this.emit('session:closed', token, reason);
    return true;
  }

  async close() {
    for (const s of this.sessions.values()) {
      send(s.socket, { kind: 'bye', sessionToken: s.token, reason: 'server shutting down' });
      try {
        s.socket?.close(1001, 'server shutting down');
      } catch {
        /* already closed */
      }
    }
    this.sessions.clear();
    this.issuedTokens.clear();
    await new Promise((resolve) => this.wss.close(() => resolve()));
  }

  get port() {
    const address = this.wss.address();
    return typeof address === 'object' && address ? address.port : this.preferredPort;
  }

  attachSocket(session, socket) {
    session.socket = socket;
    session.lastDisconnectAt = null;

    socket.on('message', (raw) => {
      let msg;
      try {
        msg = JSON.parse(raw.toString());
      } catch {
        reject(socket, 'invalid_payload', 'JSON parse failed');
        return;
      }

      if (msg.sessionToken !== session.token) {
        reject(socket, 'unauthorized', 'session_token mismatch');
        return;
      }

      switch (msg.kind) {
        case 'hello':
          session.deviceType = msg.device;
          send(socket, { kind: 'welcome', sessionToken: session.token });
          break;
        case 'sdp-offer':
        case 'sdp-answer':
        case 'ice-candidate':
        case 'bye':
          this.emit('session:client-signal', session, msg);
          break;
        default:
          // Unknown kind — log silently
          break;
      }
    });

    socket.on('close', (code, reason) => {
      if (session.socket === socket) {
        session.socket = null;
        session.lastDisconnectAt = Date.now();
        this.emit('session:closed', session.token, reason.toString() || `code ${code}`);
      }
    });

    socket.on('error', () => {
      // Swallow — the close handler will fire shortly after.
    });
  }
}

export async function startSignalingServer(preferredPort = 0, opts = {}) {
  const server = new SignalingServer(preferredPort, opts);
  await server.listen();
  return server;
}

// Re-export event names so the test runner can subscribe without typos.
export const SIGNALLING_EVENTS = EVENT_NAMES;
