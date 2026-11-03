'use strict';

// ============================================================
// AnyDownloader Popup — Video detection + send to desktop app
//
// States:
//   detected  → Video found on current page, ready to send
//   noVideo   → No video detected, manual URL input available
//   noApp     → Desktop app not installed/connected
// ============================================================

// ── Supported site metadata ───────────────────────────────────────────────
const SUPPORTED_SITES = [
  { pattern: /youtube\.com|youtu\.be/,   name: 'YouTube',     color: '#ff0000' },
  { pattern: /bilibili\.com/,            name: 'Bilibili',    color: '#00a1d6' },
  { pattern: /tiktok\.com/,              name: 'TikTok',      color: '#010101' },
  { pattern: /twitter\.com|x\.com/,      name: 'X / Twitter', color: '#1d9bf0' },
  { pattern: /twitch\.tv/,               name: 'Twitch',      color: '#9146ff' },
  { pattern: /vimeo\.com/,               name: 'Vimeo',       color: '#1ab7ea' },
  { pattern: /dailymotion\.com/,         name: 'Dailymotion', color: '#0066dc' },
  { pattern: /instagram\.com/,           name: 'Instagram',   color: '#e1306c' },
  { pattern: /facebook\.com|fb\.watch/,  name: 'Facebook',    color: '#1877f2' },
  { pattern: /nicovideo\.jp/,            name: 'NicoVideo',   color: '#e6384c' },
  { pattern: /weibo\.com/,               name: 'Weibo',       color: '#e6162d' },
];

// ── Helpers ───────────────────────────────────────────────────────────────
const $ = (id) => document.getElementById(id);

function detectSite(url) {
  return SUPPORTED_SITES.find(({ pattern }) => pattern.test(url)) ?? null;
}

function truncateUrl(url, maxLen = 50) {
  if (url.length <= maxLen) return url;
  return url.slice(0, maxLen - 3) + '…';
}

let toastTimer = null;
function showToast(msg, type = 'info') {
  const el = $('toast');
  el.textContent = msg;
  el.className   = `toast ${type}`;
  el.style.display = 'block';
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.style.display = 'none'; }, 3500);
}

// ── State management ─────────────────────────────────────────────────────
const STATES = ['stateDetected', 'stateNoVideo', 'stateNoApp'];

function setState(name) {
  STATES.forEach((id) => {
    const el = $(id);
    if (el) el.style.display = (id === name) ? '' : 'none';
  });
}

// ── App connection status ────────────────────────────────────────────────
let appConnected = false;

function setAppStatus(connected) {
  appConnected = connected;
  $('statusDot').className = 'status-dot ' + (connected ? 'online' : 'offline');
  // With deep-link handoff, "connected" just means the extension is ready
  // to invoke the OS protocol handler — the desktop app will launch on click.
  $('statusText').textContent = connected
    ? 'Ready — will open AnyDownloader app'
    : 'Extension not ready';
}

async function checkApp() {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage({ type: 'CHECK_APP' }, (response) => {
      if (chrome.runtime.lastError || !response) {
        setAppStatus(false);
        resolve(false);
        return;
      }
      setAppStatus(response.connected);
      resolve(response.connected);
    });
  });
}

// ── Send to desktop app ─────────────────────────────────────────────────
// The background worker builds an `anydownloader://download?url=...` deep link
// and opens it in a scratch tab; the OS routes it to AnyDownloader.app.
function sendToApp(url, title, quality) {
  chrome.runtime.sendMessage({
    type: 'SEND_TO_APP',
    url,
    title,
    quality,
  });

  // Show success feedback
  const msg = $('successMsg');
  if (msg) {
    msg.style.display = 'flex';
    setTimeout(() => { msg.style.display = 'none'; }, 3000);
  }
  showToast('Opening AnyDownloader…', 'success');
}

// ── Paste from clipboard ────────────────────────────────────────────────
async function handlePaste() {
  try {
    const text = await navigator.clipboard.readText();
    if (text && text.startsWith('http')) {
      $('urlInput').value = text;
      updateManualBtn();
    }
  } catch {
    showToast('Clipboard access denied — paste manually', 'error');
  }
}

function updateManualBtn() {
  $('manualSendBtn').disabled = !$('urlInput').value.trim();
}

// ── Listen for messages from background ─────────────────────────────────
chrome.runtime.onMessage.addListener((message) => {
  if (!message || typeof message.type !== 'string') return;

  // NATIVE_ERROR is fired when the background worker could not open the
  // deep-link tab (rare — typically only if the tabs API itself failed).
  if (message.type === 'NATIVE_ERROR') {
    showToast('Could not launch AnyDownloader. Install the desktop app first.', 'error');
    setAppStatus(false);
  }

  if (message.type === 'SENT_SUCCESS') {
    showToast('Opened AnyDownloader', 'success');
  }
});

// ── Init ─────────────────────────────────────────────────────────────────
async function init() {
  // 1. Check desktop app connection
  const connected = await checkApp();

  if (!connected) {
    setState('stateNoApp');
    // Still allow usage — the app might start later
  }

  // 2. Get current tab info
  let tabUrl = '';
  let tabTitle = '';
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab?.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('edge://')) {
      tabUrl = tab.url;
      tabTitle = tab.title || '';
    }
  } catch { /* ignore */ }

  // 3. Check for stored detection from content script
  const tabId = await getActiveTabId();
  const store = chrome.storage.session ?? chrome.storage.local;
  const key = tabId ? 'detected_' + tabId : null;
  const stored = key ? await store.get(key).catch(() => ({})) : {};
  const detected = key ? stored[key] : null;

  // Use detected info or fall back to tab URL if it matches a supported site
  const videoUrl = detected?.url || tabUrl;
  const videoTitle = detected?.title || tabTitle;
  const site = detectSite(videoUrl);

  if (site && videoUrl) {
    // Show detected state
    $('siteDot').style.background = site.color;
    $('siteName').textContent = site.name;
    $('videoTitle').textContent = videoTitle || 'Video detected';
    $('videoUrl').textContent = truncateUrl(videoUrl);

    if (connected) {
      setState('stateDetected');
    } else {
      // Show detected info but warn about app
      setState('stateNoApp');
    }
  } else if (connected) {
    setState('stateNoVideo');
  } else {
    setState('stateNoApp');
  }

  // 4. Clear badge
  if (tabId) {
    chrome.runtime.sendMessage({ type: 'CLEAR_BADGE', tabId }).catch(() => {});
  }

  // 5. Bind events
  $('sendBtn').addEventListener('click', () => {
    const quality = $('qualitySelect').value;
    sendToApp(videoUrl, videoTitle, quality);
  });

  $('urlInput').addEventListener('input', updateManualBtn);
  $('urlInput').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && $('urlInput').value.trim()) {
      sendToApp($('urlInput').value.trim(), '', 'best');
    }
  });

  $('pasteBtn').addEventListener('click', handlePaste);
  $('manualSendBtn').addEventListener('click', () => {
    const url = $('urlInput').value.trim();
    if (url) sendToApp(url, '', 'best');
  });
}

async function getActiveTabId() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    return tab?.id ?? null;
  } catch {
    return null;
  }
}

document.addEventListener('DOMContentLoaded', init);
