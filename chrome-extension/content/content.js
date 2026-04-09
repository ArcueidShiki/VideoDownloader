'use strict';

// ============================================================
// AnyDownloader — Content Script
//
// Injected into every tab at document_idle.
// Detects whether the current page is a supported video page
// and notifies the background service worker to badge the icon.
//
// This script NEVER modifies the page — read-only observation only.
// ============================================================

(function () {
  // Patterns that identify a specific video page (not just the homepage)
  const SUPPORTED_PATTERNS = [
    /youtube\.com\/watch\?.*v=/,
    /youtu\.be\/[a-zA-Z0-9_-]{6,}/,
    /bilibili\.com\/video\//,
    /bilibili\.com\/bangumi\/play\//,
    /tiktok\.com\/@[^/]+\/video\/\d+/,
    /twitter\.com\/[^/]+\/status\/\d+/,
    /x\.com\/[^/]+\/status\/\d+/,
    /twitch\.tv\/videos\/\d+/,
    /twitch\.tv\/[^/]+\/clip\//,
    /vimeo\.com\/\d+/,
    /dailymotion\.com\/video\//,
    /instagram\.com\/(p|reel|tv)\//,
    /facebook\.com\/.*\/videos\//,
    /fb\.watch\//,
    /nicovideo\.jp\/watch\//,
    /weibo\.com\/tv\/show\//,
  ];

  function isSupportedUrl(url) {
    return SUPPORTED_PATTERNS.some((p) => p.test(url));
  }

  function sendDetected(url, title) {
    try {
      chrome.runtime.sendMessage(
        { type: 'DOWNLOAD_DETECTED', url, title },
        () => void chrome.runtime.lastError
      );
    } catch {
      // Extension context invalidated (e.g. extension reloaded) — ignore
    }
  }

  function runDetection() {
    const url   = window.location.href;
    const title = document.title;
    if (isSupportedUrl(url)) sendDetected(url, title);
  }

  // Run once after DOM is ready
  runDetection();

  // Re-run on SPA navigation (YouTube, Bilibili, TikTok update URL
  // via History API without a full page reload)
  let lastUrl = window.location.href;

  const observer = new MutationObserver(() => {
    const current = window.location.href;
    if (current !== lastUrl) {
      lastUrl = current;
      runDetection();
    }
  });

  // Watch <title> changes (title updates signal a completed SPA navigation)
  const titleEl = document.querySelector('title');
  if (titleEl) {
    observer.observe(titleEl, { childList: true, characterData: true, subtree: true });
  }

  // Shallow body watch for route changes
  observer.observe(document.body || document.documentElement, {
    childList: true,
    subtree:   false,
  });
})();
