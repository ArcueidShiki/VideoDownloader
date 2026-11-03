import { Router } from 'express';
import { fetchVideoInfo } from '../downloader.js';
import { validateUrl } from '../utils/validateUrl.js';

/**
 * Build the downloads router.
 * @param {import('../downloader.js').DownloadManager} manager
 * @returns {Router}
 */
export function createDownloadsRouter(manager) {
  const router = Router();

  // ---------------------------------------------------------------------------
  // GET /api/downloads/info?url=... — fetch video metadata via yt-dlp --dump-json
  // Must be registered before GET /:id to avoid "info" being treated as an id.
  // ---------------------------------------------------------------------------
  router.get('/info', async (req, res) => {
    const { url } = req.query;

    let validatedUrl;
    try {
      validatedUrl = validateUrl(url);
    } catch (err) {
      return res.status(400).json({ error: err.message });
    }

    try {
      const raw = await fetchVideoInfo(validatedUrl);

      // Extract a concise subset that the extension popup needs
      const formats = Array.isArray(raw.formats)
        ? raw.formats
            .filter((f) => f.format_id && (f.vcodec !== 'none' || f.acodec !== 'none'))
            .map((f) => ({
              formatId:  f.format_id,
              ext:       f.ext,
              quality:   f.format_note || f.resolution || f.format_id,
              hasVideo:  f.vcodec !== 'none',
              hasAudio:  f.acodec !== 'none',
              filesize:  f.filesize || f.filesize_approx || null,
            }))
        : [];

      return res.json({
        title:     raw.title     || null,
        thumbnail: raw.thumbnail || null,
        duration:  raw.duration  || null,
        uploader:  raw.uploader  || raw.channel || null,
        webpage_url: raw.webpage_url || url.trim(),
        formats,
      });
    } catch (err) {
      return res.status(500).json({ error: err.message });
    }
  });

  // ---------------------------------------------------------------------------
  // POST /api/downloads — add a new download
  // ---------------------------------------------------------------------------
  router.post('/', async (req, res) => {
    const { url, quality, format, outputDir } = req.body ?? {};

    let validatedUrl;
    try {
      validatedUrl = validateUrl(url);
    } catch (err) {
      return res.status(400).json({ error: err.message });
    }

    try {
      const item = await manager.add(validatedUrl, { quality, format, outputDir });
      return res.status(201).json(item);
    } catch (err) {
      return res.status(500).json({ error: err.message });
    }
  });

  // ---------------------------------------------------------------------------
  // GET /api/downloads — list all downloads
  // ---------------------------------------------------------------------------
  router.get('/', (_req, res) => {
    return res.json(manager.list());
  });

  // ---------------------------------------------------------------------------
  // GET /api/downloads/:id — get single download
  // ---------------------------------------------------------------------------
  router.get('/:id', (req, res) => {
    const item = manager.get(req.params.id);
    if (!item) return res.status(404).json({ error: 'Download not found' });
    return res.json(item);
  });

  // ---------------------------------------------------------------------------
  // DELETE /api/downloads/:id — remove a download
  // ---------------------------------------------------------------------------
  router.delete('/:id', (req, res) => {
    const item = manager.get(req.params.id);
    if (!item) return res.status(404).json({ error: 'Download not found' });
    manager.remove(req.params.id);
    return res.status(204).send();
  });

  // ---------------------------------------------------------------------------
  // POST /api/downloads/:id/pause
  // ---------------------------------------------------------------------------
  router.post('/:id/pause', (req, res) => {
    try {
      const item = manager.pause(req.params.id);
      return res.json(item);
    } catch (err) {
      const status = err.message.includes('not found') ? 404 : 400;
      return res.status(status).json({ error: err.message });
    }
  });

  // ---------------------------------------------------------------------------
  // POST /api/downloads/:id/resume
  // ---------------------------------------------------------------------------
  router.post('/:id/resume', async (req, res) => {
    try {
      const item = await manager.resume(req.params.id);
      return res.json(item);
    } catch (err) {
      const status = err.message.includes('not found') ? 404 : 400;
      return res.status(status).json({ error: err.message });
    }
  });

  // ---------------------------------------------------------------------------
  // POST /api/downloads/:id/restart
  // ---------------------------------------------------------------------------
  router.post('/:id/restart', async (req, res) => {
    try {
      const item = await manager.restart(req.params.id);
      return res.json(item);
    } catch (err) {
      const status = err.message.includes('not found') ? 404 : 400;
      return res.status(status).json({ error: err.message });
    }
  });

  // ---------------------------------------------------------------------------
  // POST /api/downloads/:id/start — start a queued download
  // ---------------------------------------------------------------------------
  router.post('/:id/start', async (req, res) => {
    const item = manager.get(req.params.id);
    if (!item) return res.status(404).json({ error: 'Download not found' });

    try {
      await manager.start(req.params.id);
      return res.json(manager.get(req.params.id));
    } catch (err) {
      return res.status(500).json({ error: err.message });
    }
  });

  return router;
}
