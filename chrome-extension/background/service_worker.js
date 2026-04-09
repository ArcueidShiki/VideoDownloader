'use strict';

// ============================================================
// AnyDownloader — Background Service Worker (Manifest V3)
//
// Responsibilities:
//   1. Badge the toolbar icon when a downloadable page is detected
//   2. Register and handle the right-click context menu
//   3. Hand off download requests to the desktop app via `anydownloader://` deep link
//   4. Show desktop notifications for download status
//   5. Clean up stored state when tabs close
// ============================================================

const BADGE_COLOR_ACTIVE = '#7c3aed';
const CONTEXT_MENU_ID    = 'anydownloader-send-to-app';
// Custom URL scheme registered by the desktop app (see macOS Info.plist → CFBundleURLTypes,
// Windows installer → HKCR\anydownloader). Opening this URL hands control to the OS,
// which launches AnyDownloader.app with the embedded download request.
const DEEP_LINK_SCHEME   = 'anydownloader';

// ============================================================
// Installation — register context menu
// ============================================================
chrome.runtime.onInstalled.addListener(({ reason }) => {
  chrome.contextMenus.create({
    id:       CONTEXT_MENU_ID,
    title:    'Download with AnyDownloader',
    contexts: ['link', 'page'],
  });

  if (reason === chrome.runtime.OnInstalledReason.INSTALL) {
    chrome.storage.local.clear().catch(() => {});
  }
});

// ============================================================
// Context menu click → launch desktop app via deep link
// ============================================================
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== CONTEXT_MENU_ID) return;

  const targetUrl = info.linkUrl || info.pageUrl || '';
  if (!targetUrl) return;

  launchDesktopApp({
    url: targetUrl,
    title: tab?.title || '',
    quality: 'best',
  });
});

// ============================================================
// Deep link — hand off the download request to the desktop app
//
// Why a URL scheme instead of native messaging?
//   Native messaging requires installing a per-platform native host manifest
//   (~/.config or registry) that users never do manually. A URL scheme only
//   needs the desktop app installed; macOS Launch Services / Windows protocol
//   registration hands the request over automatically.
//
// Strategy: create a hidden tab pointed at the deep link. Chrome surfaces
// the OS protocol handler (which opens AnyDownloader.app), then we close
// the tab so the user never sees it.
// ============================================================
function launchDesktopApp({ url, title, quality }) {
  if (!url) return;

  const params = new URLSearchParams({
    url,
    title: title || '',
    quality: quality || 'best',
  });
  const deepLink = `${DEEP_LINK_SCHEME}://download?${params.toString()}`;

  chrome.tabs.create({ url: deepLink, active: false }, (tab) => {
    if (chrome.runtime.lastError || !tab) {
      broadcastToPopup({
        type: 'NATIVE_ERROR',
        error: chrome.runtime.lastError?.message
          || 'Could not open AnyDownloader. Is the desktop app installed?',
      });
      return;
    }

    // The OS protocol handler has been invoked — close the scratch tab.
    // 600ms is enough for Chrome to fire the external handoff but short
    // enough that the user barely notices the flash.
    setTimeout(() => {
      if (tab.id != null) chrome.tabs.remove(tab.id).catch(() => {});
    }, 600);

    showNotification('Sent to AnyDownloader', title || url);
    broadcastToPopup({ type: 'SENT_SUCCESS', url });
  });
}

function broadcastToPopup(message) {
  chrome.runtime.sendMessage(message).catch(() => {});
}

// ============================================================
// Message handler
// ============================================================
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || typeof message.type !== 'string') return;

  switch (message.type) {

    // Content script detected a supported page
    case 'DOWNLOAD_DETECTED':
      handleDownloadDetected(message, sender);
      sendResponse({ ok: true });
      break;

    // Popup cleared the badge after opening
    case 'CLEAR_BADGE': {
      const tabId = sender.tab?.id ?? message.tabId;
      if (tabId) clearBadge(tabId);
      sendResponse({ ok: true });
      break;
    }

    // Popup requests sending URL to desktop app
    case 'SEND_TO_APP':
      launchDesktopApp({
        url: message.url,
        title: message.title || '',
        quality: message.quality || 'best',
      });
      sendResponse({ ok: true });
      break;

    // Popup checks if desktop app is available.
    // With URL-scheme handoff there's no reliable probe (the OS doesn't
    // expose which protocol handlers are registered), so we optimistically
    // report "ready" and let the Send flow fall back gracefully if the
    // scheme is unhandled. This is the same model as mailto:/tel: links.
    case 'CHECK_APP':
      sendResponse({ connected: true });
      break;

    default:
      break;
  }

  return true;
});

// ============================================================
// Badge helpers
// ============================================================
function handleDownloadDetected(message, sender) {
  const tabId = sender.tab?.id;
  if (!tabId) return;

  chrome.action.setBadgeText({ text: '▼', tabId }).catch(() => {});
  chrome.action.setBadgeBackgroundColor({ color: BADGE_COLOR_ACTIVE, tabId }).catch(() => {});

  const key   = 'detected_' + tabId;
  const store = chrome.storage.session ?? chrome.storage.local;
  store.set({ [key]: { url: message.url || '', title: message.title || '' } }).catch(() => {});
}

function clearBadge(tabId) {
  chrome.action.setBadgeText({ text: '', tabId }).catch(() => {});
}

// ============================================================
// Notifications
// ============================================================
function showNotification(title, message) {
  chrome.notifications.create('anydownloader-' + Date.now(), {
    type:    'basic',
    iconUrl: '../icons/icon128.png',
    title,
    message,
  }).catch(() => {});
}

// ============================================================
// Tab cleanup
// ============================================================
chrome.tabs.onRemoved.addListener((tabId) => {
  clearBadge(tabId);
  const key   = 'detected_' + tabId;
  const store = chrome.storage.session ?? chrome.storage.local;
  store.remove(key).catch(() => {});
});
