'use strict';

/** Toast, confirm, loader health, prefs, theme — loaded before app.js (no build step). */

const UI_PREFS_KEY = 'tad.ui.v1';
const loaderHealth = {};
let pollTimer = null;
let pollIntervalMs = 2500;
let pollErrorStreak = 0;
let confirmTrapHandler = null;

function loadUiPrefs() {
  try { return JSON.parse(localStorage.getItem(UI_PREFS_KEY) || '{}'); }
  catch { return {}; }
}

function saveUiPrefs(patch) {
  const next = { ...loadUiPrefs(), ...patch };
  localStorage.setItem(UI_PREFS_KEY, JSON.stringify(next));
  return next;
}

function applyThemeFromPrefs() {
  const p = loadUiPrefs();
  const theme = p.theme || 'system';
  document.documentElement.dataset.theme = theme;
  if (p.accent) document.documentElement.style.setProperty('--accent', p.accent);
  else document.documentElement.style.removeProperty('--accent');
  document.documentElement.dataset.density = p.density || 'comfortable';
  applyHiddenColumns();
}

function applyHiddenColumns() {
  const hidden = new Set(Array.isArray(loadUiPrefs().hiddenColumns) ? loadUiPrefs().hiddenColumns : []);
  document.querySelectorAll('#chat-table [data-col]').forEach(el => {
    el.classList.toggle('col-hidden', hidden.has(el.dataset.col));
  });
}

/** Copies header text into each body cell's data-label so narrow screens can render rows as labelled cards. */
function labelTableCells(table) {
  if (!table) return;
  const heads = Array.from(table.querySelectorAll('thead th')).map(th => th.textContent.trim());
  table.querySelectorAll('tbody tr').forEach(tr => {
    const cells = Array.from(tr.children);
    if (cells.length === 1 && cells[0].colSpan > 1) return;
    cells.forEach((td, i) => { if (heads[i]) td.dataset.label = heads[i]; });
  });
}

function toast(message, { variant = 'info', duration = 5500 } = {}) {
  const region = document.getElementById('toast-region');
  if (!region || !message) return;
  const el = document.createElement('div');
  el.className = `toast toast-${variant}`;
  el.setAttribute('role', variant === 'error' ? 'alert' : 'status');
  el.textContent = message;
  region.appendChild(el);
  setTimeout(() => { el.classList.add('toast-out'); setTimeout(() => el.remove(), 300); }, duration);
}

function toastError(message) { toast(message, { variant: 'error', duration: 9000 }); }

function confirmDialog({ title = 'Confirm', message = '', confirmText = 'Continue', danger = false } = {}) {
  return new Promise((resolve) => {
    const dlg = document.getElementById('confirm-dialog');
    const ok = document.getElementById('confirm-ok');
    const cancel = document.getElementById('confirm-cancel');
    if (!dlg || !ok || !cancel) { resolve(window.confirm(message)); return; }

    document.getElementById('confirm-title').textContent = title;
    document.getElementById('confirm-message').textContent = message;
    ok.textContent = confirmText;
    ok.classList.toggle('btn-danger', danger);
    const returnFocus = document.activeElement;

    const finish = (val) => {
      dlg.classList.add('hidden');
      ok.onclick = null;
      cancel.onclick = null;
      document.removeEventListener('keydown', onKey);
      if (confirmTrapHandler) dlg.removeEventListener('keydown', confirmTrapHandler);
      confirmTrapHandler = null;
      if (returnFocus && typeof returnFocus.focus === 'function') returnFocus.focus();
      resolve(val);
    };

    const focusables = () => Array.from(dlg.querySelectorAll('button:not([disabled])'));
    confirmTrapHandler = (e) => {
      if (e.key !== 'Tab') return;
      const nodes = focusables();
      if (!nodes.length) return;
      const first = nodes[0];
      const last = nodes[nodes.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    };

    // Enter is left to the focused button; Cancel has initial focus, so Enter never confirms a destructive action by default.
    const onKey = (e) => {
      if (e.key === 'Escape') finish(false);
    };

    ok.onclick = () => finish(true);
    cancel.onclick = () => finish(false);
    dlg.classList.remove('hidden');
    dlg.addEventListener('keydown', confirmTrapHandler);
    document.addEventListener('keydown', onKey);
    cancel.focus();
  });
}

function markLoaderOk(key) {
  loaderHealth[key] = { ok: true, at: Date.now(), error: null };
  renderLoaderBanner();
  renderFreshnessBadge();
}

function markLoaderFail(key, err) {
  const msg = err?.message || String(err || 'Unknown error');
  loaderHealth[key] = { ok: false, at: Date.now(), error: msg };
  renderLoaderBanner();
  renderFreshnessBadge();
}

function renderLoaderBanner() {
  const el = document.getElementById('loader-banner');
  if (!el) return;
  const failed = Object.entries(loaderHealth).filter(([, v]) => v && !v.ok);
  if (!failed.length) { el.classList.add('hidden'); el.textContent = ''; return; }
  el.classList.remove('hidden');
  el.textContent = failed.map(([k, v]) => `${k}: ${v.error}`).join(' · ');
}

function renderFreshnessBadge() {
  const el = document.getElementById('data-freshness');
  if (!el) return;
  const times = Object.values(loaderHealth).filter(v => v?.ok).map(v => v.at);
  if (!times.length) { el.classList.add('hidden'); return; }
  const latest = Math.max(...times);
  const stale = Object.values(loaderHealth).some(v => v && !v.ok);
  el.classList.remove('hidden');
  el.textContent = stale ? `Stale · last OK ${new Date(latest).toLocaleTimeString()}` : `Updated ${new Date(latest).toLocaleTimeString()}`;
  el.classList.toggle('stale-badge', stale);
}

function tableHasFocusedInput(rootSel) {
  const root = document.querySelector(rootSel);
  const ae = document.activeElement;
  if (!root || !ae) return false;
  return root.contains(ae) && (ae.matches('input, textarea, select') || ae.isContentEditable);
}

function applyGlobalDownloadsPaused(paused, requeuedInProgress) {
  const banner = document.getElementById('global-downloads-paused-banner');
  if (!banner) return;
  if (!paused) { banner.classList.add('hidden'); banner.textContent = ''; return; }
  banner.classList.remove('hidden');
  banner.textContent = '⏸ Downloads paused globally' +
    (requeuedInProgress ? ` (${requeuedInProgress} in-progress re-queued)` : '') +
    ' — resume from the Queue tab.';
}

async function refreshGlobalDownloadsPaused() {
  try {
    const res = await fetch('/api/queue/downloads', { credentials: 'include' }).then(r => r.json());
    applyGlobalDownloadsPaused(!!res.paused, 0);
    markLoaderOk('downloads');
  } catch (e) {
    markLoaderFail('downloads', e);
  }
}

function schedulePoll(fn) {
  if (pollTimer) clearTimeout(pollTimer);
  pollTimer = setTimeout(async () => {
    if (document.hidden) { schedulePoll(fn); return; }
    try {
      await fn();
      pollErrorStreak = 0;
      pollIntervalMs = 2500;
    } catch {
      pollErrorStreak += 1;
      pollIntervalMs = Math.min(30000, 2500 * Math.pow(2, pollErrorStreak));
    }
    schedulePoll(fn);
  }, pollIntervalMs);
}

function fmtStaleness(iso) {
  if (!iso) return { text: '—', stale: false };
  const d = new Date(iso);
  const ageMs = Date.now() - d.getTime();
  const stale = ageMs > 120000;
  return { text: d.toLocaleTimeString(), stale, title: d.toLocaleString() };
}

applyThemeFromPrefs();
