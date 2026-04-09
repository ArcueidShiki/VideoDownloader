/**
 * sessionManager.js
 *
 * Manages per-WebSocket sessions with:
 *  - One download slot per session
 *  - Global concurrency limit (MAX_CONCURRENT active downloads)
 *  - In-memory FIFO queue for excess requests
 *  - One-time token system for file delivery
 *  - Automatic cleanup (files + processes) on disconnect
 */

import { v4 as uuidv4 } from 'uuid';
import { rmSync, existsSync } from 'fs';

const MAX_CONCURRENT = parseInt(process.env.MAX_CONCURRENT ?? '20', 10);

// Session lifecycle:
//   idle → queued → downloading → completed
//                              → error
//   Any state → idle  (on cancel or after file served)

export class SessionManager {
  /** @type {Map<string, object>} id → session */
  #sessions = new Map();

  /** @type {Map<string, object>} token → { sessionId, filePath, filename } */
  #tokens = new Map();

  /** Number of sessions currently in 'downloading' state */
  #activeCount = 0;

  /** Ordered list of sessionIds waiting for a slot */
  #queue = [];

  // ---------------------------------------------------------------------------
  // Session lifecycle
  // ---------------------------------------------------------------------------

  /**
   * Create a new session linked to a WebSocket connection.
   * @param {import('ws').WebSocket} ws
   * @returns {string} sessionId
   */
  createSession(ws) {
    const id = uuidv4();
    this.#sessions.set(id, {
      id,
      ws,
      state: 'idle',    // idle | queued | downloading | completed | error
      outputDir: null,  // temp directory for this download
      filePath: null,   // completed output file path
      process: null,    // child_process handle
      downloadFn: null, // async (session) => void
    });
    return id;
  }

  /**
   * @param {string} id
   * @returns {object | undefined}
   */
  getSession(id) {
    return this.#sessions.get(id);
  }

  /**
   * Called when the WebSocket disconnects.
   * Kills any running process, deletes all temp files, removes session.
   * @param {string} sessionId
   */
  cleanupSession(sessionId) {
    const session = this.#sessions.get(sessionId);
    if (!session) return;

    const wasActive = session.state === 'downloading';

    this.#killAndClean(session);
    this.#queue = this.#queue.filter(id => id !== sessionId);

    // Revoke any pending download tokens for this session
    for (const [token, entry] of this.#tokens) {
      if (entry.sessionId === sessionId) {
        this.#tokens.delete(token);
        // File already cleaned up by #killAndClean above
      }
    }

    this.#sessions.delete(sessionId);

    if (wasActive) {
      this.#activeCount = Math.max(0, this.#activeCount - 1);
      this.#drain();
    } else {
      this.#broadcastQueuePositions();
    }
  }

  // ---------------------------------------------------------------------------
  // Download control
  // ---------------------------------------------------------------------------

  /**
   * Enqueue a download for the session.
   * Returns false if the session already has an active/queued download.
   *
   * @param {string} sessionId
   * @param {(session: object) => Promise<void>} downloadFn
   * @returns {boolean}
   */
  enqueueDownload(sessionId, downloadFn) {
    const session = this.#sessions.get(sessionId);
    if (!session) return false;
    if (session.state === 'queued' || session.state === 'downloading') return false;

    // Reset any previous download state
    this.#killAndClean(session);
    session.state = 'queued';
    session.downloadFn = downloadFn;
    this.#queue.push(sessionId);

    this.#broadcastQueuePositions();
    this.#drain();
    return true;
  }

  /**
   * Cancel the current download for a session and return it to idle.
   * @param {string} sessionId
   */
  cancelDownload(sessionId) {
    const session = this.#sessions.get(sessionId);
    if (!session) return;

    const wasActive = session.state === 'downloading';
    this.#killAndClean(session);
    this.#queue = this.#queue.filter(id => id !== sessionId);
    session.state = 'idle';

    this.#send(session, { type: 'cancelled' });

    if (wasActive) {
      this.#activeCount = Math.max(0, this.#activeCount - 1);
      this.#drain();
    } else {
      this.#broadcastQueuePositions();
    }
  }

  // ---------------------------------------------------------------------------
  // Download state notifications (called from downloader.js)
  // ---------------------------------------------------------------------------

  /**
   * Mark a download as completed and issue a one-time download token.
   * @param {string} sessionId
   * @param {string} filePath  absolute path to the output file
   */
  notifyCompleted(sessionId, filePath) {
    const session = this.#sessions.get(sessionId);
    if (!session || session.state !== 'downloading') return;

    const filename = filePath.split('/').pop();
    const token = uuidv4();
    this.#tokens.set(token, { sessionId, filePath, filename });

    session.state = 'completed';
    session.filePath = filePath;
    session.process = null;

    this.#send(session, { type: 'completed', filename, token });
    this.#activeCount = Math.max(0, this.#activeCount - 1);
    this.#drain();
  }

  /**
   * Mark a download as failed.
   * @param {string} sessionId
   * @param {string} errorMessage
   */
  notifyFailed(sessionId, errorMessage) {
    const session = this.#sessions.get(sessionId);
    // Guard: if session was cancelled or disconnected, state won't be 'downloading'.
    // In that case, the activeCount was already adjusted by cancelDownload/cleanupSession.
    if (!session || session.state !== 'downloading') return;

    session.state = 'error';
    session.process = null;
    this.#send(session, { type: 'error', message: errorMessage });
    this.#activeCount = Math.max(0, this.#activeCount - 1);
    this.#drain();
  }

  /**
   * Send an arbitrary state transition message to the session.
   * Used for sub-states like 'merging' that don't change the session's lifecycle state.
   * @param {string} sessionId
   * @param {string} state
   */
  notifyState(sessionId, state) {
    const session = this.#sessions.get(sessionId);
    if (session) this.#send(session, { type: 'state', state });
  }

  /**
   * Forward a progress update to the session's WebSocket.
   * @param {string} sessionId
   * @param {{ progress: number, speed: number, eta: number | null }} data
   */
  notifyProgress(sessionId, data) {
    const session = this.#sessions.get(sessionId);
    if (session) this.#send(session, { type: 'progress', ...data });
  }

  // ---------------------------------------------------------------------------
  // File delivery
  // ---------------------------------------------------------------------------

  /**
   * Consume a one-time download token.
   * Resets the session to idle and returns the file entry.
   *
   * @param {string} token
   * @returns {{ filePath: string, filename: string } | null}
   */
  consumeToken(token) {
    const entry = this.#tokens.get(token);
    if (!entry) return null;
    this.#tokens.delete(token);

    const session = this.#sessions.get(entry.sessionId);
    if (session) {
      session.state = 'idle';
      session.filePath = null;
    }
    return entry;
  }

  // ---------------------------------------------------------------------------
  // Private helpers
  // ---------------------------------------------------------------------------

  #drain() {
    while (this.#activeCount < MAX_CONCURRENT && this.#queue.length > 0) {
      const sessionId = this.#queue.shift();
      const session = this.#sessions.get(sessionId);
      if (!session || session.state !== 'queued') continue;

      this.#activeCount++;
      session.state = 'downloading';
      this.#send(session, { type: 'state', state: 'downloading' });

      Promise.resolve(session.downloadFn(session))
        .catch(err => this.notifyFailed(sessionId, err.message));
    }

    this.#broadcastQueuePositions();
  }

  #broadcastQueuePositions() {
    this.#queue.forEach((id, idx) => {
      const s = this.#sessions.get(id);
      if (s) this.#send(s, { type: 'queued', position: idx + 1, total: this.#queue.length });
    });
  }

  /** Kill process and delete output directory for a session. */
  #killAndClean(session) {
    if (session.process && !session.process.killed) {
      try { session.process.kill('SIGTERM'); } catch {}
    }
    session.process = null;

    if (session.outputDir && existsSync(session.outputDir)) {
      try { rmSync(session.outputDir, { recursive: true, force: true }); } catch {}
    }
    session.outputDir = null;
    session.filePath = null;
  }

  #send(session, data) {
    try {
      if (session.ws.readyState === 1 /* OPEN */) {
        session.ws.send(JSON.stringify(data));
      }
    } catch {}
  }
}
