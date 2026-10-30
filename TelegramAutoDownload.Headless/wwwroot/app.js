'use strict';
const $  = (q, r=document) => r.querySelector(q);
const $$ = (q, r=document) => Array.from(r.querySelectorAll(q));

// ─── Web gate + Telegram auth ────────────────────────────────────────────────
let webAuthenticated = false;
let loggedIn = false;
let mustChangeWebPassword = false;

function applyDefaultPasswordUi(isDefault) {
  const onLogin = $('#web-default-warn');
  if (onLogin) onLogin.classList.toggle('hidden', !isDefault);
  const inSettings = $('#settings-pwd-warn');
  if (inSettings) inSettings.classList.toggle('hidden', !isDefault);
}

function showPasswordChangeModal(show) {
  mustChangeWebPassword = show;
  $('#password-change-modal')?.classList.toggle('hidden', !show);
  if (show) {
    $('#telegram-auth-screen').classList.add('hidden');
    $('#app-screen').classList.add('hidden');
    showError($('#pwd-change-error'));
    $('#pwd-new')?.focus();
  }
}

async function submitPasswordChange(current, newPwd, confirm, errEl) {
  showError(errEl);
  if (newPwd !== confirm) { showError(errEl, 'New password and confirmation do not match.'); return false; }
  try {
    const res = await api('/api/web-auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword: current, newPassword: newPwd, confirmPassword: confirm }),
    });
    applyDefaultPasswordUi(!!res.mustChangePassword);
    showPasswordChangeModal(false);
    $('#pwd-current').value = '';
    $('#pwd-new').value = '';
    $('#pwd-confirm').value = '';
    await refreshLoginStatus();
    return true;
  } catch (e) {
    showError(errEl, e.message);
    return false;
  }
}

function showWebLogin(show) {
  $('#web-login-screen').classList.toggle('hidden', !show);
  if (show) {
    $('#telegram-auth-screen').classList.add('hidden');
    $('#app-screen').classList.add('hidden');
  }
}

function setTelegramAuthState(isLoggedIn, userId) {
  loggedIn = isLoggedIn;
  $('#telegram-auth-screen').classList.toggle('hidden', isLoggedIn);
  $('#app-screen').classList.toggle('hidden', !isLoggedIn);
  const userEl = $('#header-user');
  if (isLoggedIn && userId) {
    userEl.textContent = `User ${userId}`;
    userEl.classList.remove('hidden');
    $('#s-user-id').textContent = userId;
  } else {
    userEl.classList.add('hidden');
    userEl.textContent = '';
    $('#s-user-id').textContent = '—';
  }
}

function enterApp(userId) {
  setTelegramAuthState(true, userId);
  const hashTab = location.hash.replace(/^#/, '');
  const prefsTab = loadUiPrefs().lastTab;
  const tab = VALID_TABS.has(hashTab) ? hashTab : (VALID_TABS.has(prefsTab) ? prefsTab : 'chats');
  switchTab(tab, { fromHash: VALID_TABS.has(hashTab) });
  refreshGlobalDownloadsPaused();
}

function leaveTelegramApp() {
  setTelegramAuthState(false);
  showStep('phone');
  $('#code').value = '';
  $('#password').value = '';
}

async function checkWebAuth() {
  const ws = await fetch('/api/web-auth/status', { credentials: 'include' }).then(r => r.json());
  applyDefaultPasswordUi(!!ws.mustChangePassword);
  if (!ws.enabled) {
    webAuthenticated = true;
    showWebLogin(false);
    return true;
  }
  webAuthenticated = !!ws.authenticated;
  if (!webAuthenticated) {
    showWebLogin(true);
    return false;
  }
  showWebLogin(false);
  if (ws.mustChangePassword) {
    showPasswordChangeModal(true);
    return true;
  }
  showPasswordChangeModal(false);
  return true;
}

async function initApp() {
  try {
    if (!(await checkWebAuth())) return;
    if (!mustChangeWebPassword) await refreshLoginStatus();
  } catch (e) {
    showError($('#web-login-error'), e.message);
    showWebLogin(true);
  }
}

// ─── Tabs ────────────────────────────────────────────────────────────────────
const VALID_TABS = new Set(['chats', 'queue', 'status', 'settings', 'logs']);

function switchTab(name, { fromHash = false } = {}) {
  if (!VALID_TABS.has(name)) name = 'chats';
  $$('header nav button').forEach(x => {
    const on = x.dataset.tab === name;
    x.classList.toggle('active', on);
    x.setAttribute('aria-selected', on ? 'true' : 'false');
  });
  $$('#app-screen .tab').forEach(t => t.classList.toggle('active', t.id === `tab-${name}`));
  if (!fromHash && loggedIn) {
    location.hash = name;
    saveUiPrefs({ lastTab: name });
  }
  if (name === 'chats')    loadChats();
  if (name === 'queue')    refreshQueue();
  if (name === 'status')   refreshStatus();
  if (name === 'settings') loadSettings();
  if (name === 'logs')     openLogsTab();
}

$$('header nav button').forEach(b => b.addEventListener('click', () => switchTab(b.dataset.tab)));
window.addEventListener('hashchange', () => {
  const tab = location.hash.replace(/^#/, '');
  if (VALID_TABS.has(tab)) switchTab(tab, { fromHash: true });
});

// ─── Helpers ────────────────────────────────────────────────────────────────
async function api(path, opts = {}) {
  const r = await fetch(path, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    ...opts,
  });
  let data = {};
  try { data = await r.json(); } catch {}
  if (r.status === 401 && data.webAuthRequired) {
    webAuthenticated = false;
    loggedIn = false;
    showWebLogin(true);
    throw new Error('Session expired — sign in again.');
  }
  if (r.status === 403 && data.error === 'passwordChangeRequired') {
    showPasswordChangeModal(true);
    throw new Error('Change your dashboard password to continue.');
  }
  if (!r.ok) {
    const err = new Error(data.error || `HTTP ${r.status}`);
    err.status = r.status;
    err.data = data;
    throw err;
  }
  return data;
}
function showError(el, msg) { el.textContent = msg || ''; el.classList.toggle('hidden', !msg); }
function fmtBytes(n) {
  if (!n) return '—';
  const u = ['B','KB','MB','GB','TB']; let i = 0;
  while (n >= 1024 && i < u.length-1) { n /= 1024; i++; }
  return `${n.toFixed(1)} ${u[i]}`;
}

function fmtTime(iso) {
  if (!iso) return '';
  try { return new Date(iso).toLocaleTimeString(); } catch { return iso; }
}

function renderFloodBanner(flood) {
  const el = $('#flood-banner');
  if (!flood?.active) {
    el.classList.add('hidden');
    el.textContent = '';
    return;
  }
  el.classList.remove('hidden');
  el.className = 'banner flood';
  el.textContent = `⚠ Telegram FLOOD_WAIT — API paused ~${flood.remainingSeconds}s (until ${fmtTime(flood.pausedUntil)})` +
    (flood.source ? ` · ${flood.source}` : '');
}

function applyDownloadsPausedUi(paused, requeuedInProgress) {
  const banner = $('#downloads-paused-banner');
  const btn = $('#btn-toggle-downloads');
  if (!banner || !btn) return;
  if (paused) {
    banner.classList.remove('hidden');
    banner.classList.add('danger');
    banner.textContent = '⏸ Downloads paused — queued items stay in the database until you resume.' +
      (requeuedInProgress ? ` (${requeuedInProgress} in-progress item(s) moved back to queued.)` : '');
    btn.textContent = 'Resume downloads';
    btn.classList.add('primary');
  } else {
    banner.classList.add('hidden');
    banner.textContent = '';
    btn.textContent = 'Pause downloads';
    btn.classList.remove('primary');
  }
}

async function setDownloadsPaused(paused) {
  const res = await api('/api/queue/downloads', {
    method: 'POST',
    body: JSON.stringify({ paused }),
  });
  applyDownloadsPausedUi(res.paused, res.requeuedInProgress);
  return res;
}

let LIMIT_THRESHOLDS = {
  scannerCapacity: { max: 100, warn: 20, danger: 50 },
  scannerRefill:   { max: 50,  warn: 5,  danger: 10 },
  downloadThreads: { max: 10,  warn: 6,  danger: 8 },
};
let limitsDefaults = null;

function limitLevel(value, t) {
  if (value >= t.max || value >= t.danger) return 'danger';
  if (value >= t.warn) return 'warn';
  return 'ok';
}

function applyLimitWarnings(capacity, refill, threads) {
  const box = $('#limits-warnings');
  const msgs = [];
  const capLvl = limitLevel(capacity, LIMIT_THRESHOLDS.scannerCapacity);
  const refLvl = limitLevel(refill, LIMIT_THRESHOLDS.scannerRefill);
  const thrLvl = limitLevel(threads, LIMIT_THRESHOLDS.downloadThreads);

  const setLabel = (id, lvl) => {
    const lbl = $(id);
    lbl.classList.remove('input-warn', 'input-danger');
    if (lvl === 'warn') lbl.classList.add('input-warn');
    if (lvl === 'danger') lbl.classList.add('input-danger');
  };
  setLabel('#lbl-capacity', capLvl);
  setLabel('#lbl-refill', refLvl);
  setLabel('#lbl-threads', thrLvl);

  if (capLvl === 'danger') msgs.push('Scanner burst at maximum — high ban risk.');
  else if (capLvl === 'warn') msgs.push('Scanner burst is high — consider lowering for huge channels.');

  if (refLvl === 'danger') msgs.push('Scanner refill at maximum — Telegram may FLOOD_WAIT or restrict your account.');
  else if (refLvl === 'warn') msgs.push('Scanner refill is aggressive — 1 req/sec is recommended.');

  if (thrLvl === 'danger') msgs.push('Download threads at maximum — may overload disk/network.');
  else if (thrLvl === 'warn') msgs.push('Download threads above default (3) — OK if your connection handles it.');

  if (!msgs.length) { box.classList.add('hidden'); box.textContent = ''; return; }
  box.classList.remove('hidden', 'danger');
  if (capLvl === 'danger' || refLvl === 'danger') box.classList.add('danger');
  box.innerHTML = '⚠ ' + msgs.join(' ');
}

async function startBootstrap(chatId, overrideParallel = false) {
  try {
    return await api(`/api/chats/${chatId}/bootstrap`, {
      method: 'POST',
      body: JSON.stringify({ overrideParallel }),
    });
  } catch (e) {
    if (e.status === 409 && e.data?.canOverride && !overrideParallel) {
      const blocker = e.data.blockingChat || 'another chat';
      if (await confirmDialog({
        title: 'Bootstrap conflict',
        message: `${e.message}\n\nStart anyway (one-time override)? Parallel scans share the same rate limiter but increase ban risk.`,
        confirmText: 'Start anyway',
        danger: true,
      })) return startBootstrap(chatId, true);
    }
    throw e;
  }
}

function applyParallelBootstrapUi(limits) {
  const on = !!limits.allowParallelBootstrap;
  $('#q-parallel').checked = on;
  $('#q-max-parallel').value = limits.maxParallelBootstraps ?? 3;
  $('#lbl-max-parallel').classList.toggle('hidden', !on);

  const pbox = $('#parallel-warn');
  const active = limits.activeBootstrapJobs ?? 0;
  const msgs = [];
  if (on) msgs.push(`Parallel bootstraps enabled (max ${limits.maxParallelBootstraps ?? 3}).`);
  else msgs.push('Single bootstrap globally (safest). Use override on conflict to force parallel.');
  if (active > 0) msgs.push(`${active} bootstrap job(s) running.`);
  pbox.classList.remove('hidden');
  pbox.classList.toggle('danger', on);
  pbox.textContent = 'ℹ ' + msgs.join(' ');
}

$('#q-parallel')?.addEventListener('change', () => {
  $('#lbl-max-parallel').classList.toggle('hidden', !$('#q-parallel').checked);
  if ($('#q-parallel').checked) {
    $('#parallel-warn').classList.remove('hidden');
    $('#parallel-warn').classList.add('danger');
    $('#parallel-warn').textContent = '⚠ Parallel bootstraps increase ban risk — all scans still share one API rate limiter.';
  }
});

function applySettingsThreadWarning(threads) {
  const box = $('#settings-threads-warn');
  const lvl = limitLevel(threads, LIMIT_THRESHOLDS.downloadThreads);
  if (lvl === 'ok') { box.classList.add('hidden'); return; }
  box.classList.remove('hidden', 'danger');
  if (lvl === 'danger') box.classList.add('danger');
  box.textContent = lvl === 'danger'
    ? '⚠ Maximum parallel downloads — may stress your connection.'
    : '⚠ Above default (3) — increase only if needed.';
}

// ─── Login ───────────────────────────────────────────────────────────────────
const loginErr = $('#login-error');
const steps = { creds: $('#step-creds'), phone: $('#step-phone'), code: $('#step-code'),
                password: $('#step-password') };
function showStep(name) {
  Object.entries(steps).forEach(([n, el]) => el.classList.toggle('hidden', n !== name));
}

async function refreshLoginStatus() {
  try {
    const [ls, st] = await Promise.all([api('/api/login/status'), api('/api/settings')]);
    $('#conn').className = ls.loggedIn ? 'ok' : 'bad';
    if (ls.loggedIn) {
      if (!loggedIn) enterApp(ls.userId);
      else setTelegramAuthState(true, ls.userId);
      return;
    }
    leaveTelegramApp();
    if (!st.appIdConfigured) { showStep('creds'); return; }
    if (ls.stage === 'AwaitingCode')          showStep('code');
    else if (ls.stage === 'AwaitingPassword') showStep('password');
    else                                      showStep('phone');
  } catch (e) { showError($('#login-error'), e.message); }
}

$('#btn-web-login').onclick = async () => {
  const errEl = $('#web-login-error');
  showError(errEl);
  try {
    const res = await fetch('/api/web-auth/login', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: $('#web-username').value.trim(),
        password: $('#web-password').value,
      }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) { showError(errEl, data.error || 'Sign in failed'); return; }
    webAuthenticated = true;
    $('#web-password').value = '';
    showWebLogin(false);
    applyDefaultPasswordUi(!!data.mustChangePassword);
    if (data.mustChangePassword) {
      showPasswordChangeModal(true);
      return;
    }
    await refreshLoginStatus();
  } catch (e) { showError(errEl, e.message); }
};

$('#web-password')?.addEventListener('keydown', e => {
  if (e.key === 'Enter') $('#btn-web-login').click();
});

$('#btn-pwd-change').onclick = async () => {
  await submitPasswordChange(
    $('#pwd-current').value,
    $('#pwd-new').value,
    $('#pwd-confirm').value,
    $('#pwd-change-error'),
  );
};

$('#pwd-confirm')?.addEventListener('keydown', e => {
  if (e.key === 'Enter') $('#btn-pwd-change').click();
});

$('#btn-change-web-password').onclick = async () => {
  const ok = await submitPasswordChange(
    $('#s-pwd-current').value,
    $('#s-pwd-new').value,
    $('#s-pwd-confirm').value,
    $('#settings-pwd-error'),
  );
  if (ok) {
    $('#s-pwd-current').value = '';
    $('#s-pwd-new').value = '';
    $('#s-pwd-confirm').value = '';
    toast('Dashboard password updated.');
  }
};

$('#btn-web-logout').onclick = async () => {
  await api('/api/web-auth/logout', { method: 'POST' });
  webAuthenticated = false;
  loggedIn = false;
  $('#web-password').value = '';
  showWebLogin(true);
};

$('#btn-save-creds').onclick = async () => {
  showError(loginErr);
  try {
    await api('/api/settings/credentials', {
      method: 'POST',
      body: JSON.stringify({ appId: Number($('#appId').value), apiHash: $('#apiHash').value.trim() }),
    });
    showStep('phone');
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-phone').onclick = async () => {
  showError(loginErr);
  try {
    const res = await api('/api/login/phone', { method: 'POST', body: JSON.stringify({ phone: $('#phone').value.trim() }) });
    if (res.error) { showError(loginErr, res.error); return; }
    if (res.stage === 'LoggedIn')              await refreshLoginStatus();
    else if (res.stage === 'AwaitingPassword') showStep('password');
    else                                       showStep('code');
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-code').onclick = async () => {
  showError(loginErr);
  try {
    const res = await api('/api/login/code', { method: 'POST', body: JSON.stringify({ code: $('#code').value.trim() }) });
    if (res.error) { showError(loginErr, res.error); return; }
    if (res.stage === 'LoggedIn')              await refreshLoginStatus();
    else if (res.stage === 'AwaitingPassword') showStep('password');
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-password').onclick = async () => {
  showError(loginErr);
  try {
    const res = await api('/api/login/password', { method: 'POST', body: JSON.stringify({ password: $('#password').value }) });
    if (res.error) { showError(loginErr, res.error); return; }
    if (res.stage === 'LoggedIn') await refreshLoginStatus();
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-logout').onclick = async () => {
  if (!await confirmDialog({
    title: 'Sign out of Telegram',
    message: 'Sign out of Telegram on this server?\n\nDownloads and queue data are kept, but the session is removed and you will need to verify your phone again.',
    confirmText: 'Sign out',
    danger: true,
  })) return;
  await api('/api/login/logout', { method: 'POST' });
  await refreshLoginStatus();
};

// ─── Chats ──────────────────────────────────────────────────────────────────
let allChats = [];
let queueByChat = {};
let activeJobsByChat = {};

function formatLastSync(row) {
  if (!row?.lastScannedDate) {
    return { text: 'Never', title: 'No bootstrap scan recorded — run Bootstrap to backfill history', stale: true };
  }
  const d = new Date(row.lastScannedDate.includes('T') ? row.lastScannedDate : row.lastScannedDate.replace(' ', 'T') + 'Z');
  const ageMs = Date.now() - d.getTime();
  const stale = ageMs > 24 * 3600 * 1000;
  const msgId = row.lastScannedMsgId ? ` · msg #${row.lastScannedMsgId}` : '';
  const complete = row.bootstrapComplete ? ' · complete' : ' · partial';
  return {
    text: d.toLocaleString() + msgId,
    title: `Last history scan${complete}. Run Bootstrap again to queue posts added since then.`,
    stale,
  };
}

async function loadChats() {
  const sk = $('#chats-loading');
  sk?.classList.remove('hidden');
  try {
    allChats = await api('/api/chats');
    markLoaderOk('chats');
    await renderChats();
  } catch (e) {
    markLoaderFail('chats', e);
    toastError(e.message);
  } finally {
    sk?.classList.add('hidden');
  }
}

$('#btn-refresh-chats').onclick = async () => {
  const btn = $('#btn-refresh-chats');
  btn.disabled = true; btn.textContent = '⏳ Loading…';
  try { allChats = await api('/api/chats/refresh', { method: 'POST' }); markLoaderOk('chats'); await renderChats(); toast('Chat list refreshed from Telegram.'); }
  catch (e) { markLoaderFail('chats', e); toastError(e.message); }
  finally { btn.disabled = false; btn.textContent = '🔄 Refresh from Telegram'; }
};

let chatSearchTimer = null;
$('#chat-search').oninput = () => {
  saveUiPrefs({ chatFilter: $('#chat-search').value });
  clearTimeout(chatSearchTimer);
  chatSearchTimer = setTimeout(() => renderChats(), 300);
};

async function renderChats() {
  if (tableHasFocusedInput('#chat-table')) return;
  const q = ($('#chat-search').value || loadUiPrefs().chatFilter || '').trim().toLowerCase();
  try {
    const [byChat, jobs] = await Promise.all([api('/api/queue/stats/by-chat'), api('/api/bootstrap/jobs')]);
    queueByChat = Object.fromEntries(byChat.map(r => [r.chatId, r]));
    activeJobsByChat = Object.fromEntries(jobs.filter(j => !j.done).map(j => [j.chatId, j]));
    markLoaderOk('queueByChat');
  } catch (e) {
    markLoaderFail('queueByChat', e);
  }
  const rows = allChats
    .filter(c => !q || (c.name + ' ' + c.username).toLowerCase().includes(q))
    .sort((a, b) => Number(b.selected) - Number(a.selected) || a.name.localeCompare(b.name));
  $('#chat-count').textContent = `${rows.filter(r => r.selected).length} / ${allChats.length} monitored`;
  $('#chats-empty')?.classList.toggle('hidden', rows.length > 0);
  const tbody = $('#chat-table tbody');
  tbody.innerHTML = '';
  rows.forEach(c => tbody.appendChild(chatRow(c)));
  applyHiddenColumns();
  labelTableCells($('#chat-table'));
}

async function startHistoryJob(chatId, selected) {
  if (selected) {
    return api(`/api/chats/${chatId}/sync`, { method: 'POST', body: JSON.stringify({ overrideParallel: false }) });
  }
  return startBootstrap(chatId);
}

function revertChatControl(t, c) {
  if (t.dataset.k) {
    const k = t.dataset.k;
    if (t.type === 'checkbox') t.checked = !!c[k];
    else if (t.type === 'number') t.value = c.downloadFromSize;
    else t.value = c[k] ?? '';
  } else if (t.dataset.p) {
    t.checked = !!(c.plugins && c.plugins[t.dataset.p]);
  }
}

function chatRow(c) {
  const tr = document.createElement('tr');
  if (c.selected) tr.classList.add('selected');
  const sync = formatLastSync(queueByChat[c.id]);
  tr.innerHTML = `
    <td data-col="monitor"><input type="checkbox" data-k="selected" aria-label="Monitor ${escape(c.name)}" ${c.selected ? 'checked' : ''}></td>
    <td data-col="name">${escape(c.name)}<br><small class="muted">${escape(c.username || '')}</small>${c.muted ? '<br><small class="muted">Telegram muted</small>' : ''}</td>
    <td data-col="type">${escape(c.type)}</td>
    <td data-col="members">${c.membersCount || ''}</td>
    <td data-col="minmb"><input type="number" data-k="downloadFromSize" aria-label="Min MB for ${escape(c.name)}" value="${c.downloadFromSize}" min="0" class="input-narrow"></td>
    <td data-col="media"><div class="media-checks">
      <label><input type="checkbox" data-k="videos" ${c.videos ? 'checked' : ''}> Video</label>
      <label><input type="checkbox" data-k="photos" ${c.photos ? 'checked' : ''}> Photo</label>
      <label><input type="checkbox" data-k="music" ${c.music ? 'checked' : ''}> Music</label>
      <label><input type="checkbox" data-k="files" ${c.files ? 'checked' : ''}> File</label>
    </div></td>
    <td data-col="plugins"><div class="plugin-checks">
      <label title="YouTube links"><input type="checkbox" data-p="YouTube" ${c.plugins?.YouTube ? 'checked' : ''}> YouTube</label>
      <label title="Social media links"><input type="checkbox" data-p="SocialMedia" ${c.plugins?.SocialMedia ? 'checked' : ''}> Social</label>
      <label title="Direct file links"><input type="checkbox" data-p="Other" ${c.plugins?.Other ? 'checked' : ''}> Direct</label>
      <label title="Torrent / magnet links"><input type="checkbox" data-p="Torrent" ${c.plugins?.Torrent ? 'checked' : ''}> Torrent</label>
    </div></td>
    <td data-col="filter"><input type="text" data-k="filter" value="${escape(c.filter || '')}" placeholder="regex; pattern" aria-label="Filter for ${escape(c.name)}"></td>
    <td data-col="folder">
      <input type="text" data-k="folderTemplate" value="${escape(c.folderTemplate || '')}" placeholder="{Type}/{ChatName}" aria-label="Folder template for ${escape(c.name)}">
      <details class="provider-folders">
        <summary>Per-provider folders</summary>
        <label>Social <input type="text" data-f="socialDownloadFolderTemplate" value="${escape(c.socialDownloadFolderTemplate || '')}"></label>
        <label>YouTube <input type="text" data-f="youtubeDownloadFolderTemplate" value="${escape(c.youtubeDownloadFolderTemplate || '')}"></label>
        <label>Direct <input type="text" data-f="otherDownloadFolderTemplate" value="${escape(c.otherDownloadFolderTemplate || '')}"></label>
        <label>Torrent <input type="text" data-f="torrentDownloadFolderTemplate" value="${escape(c.torrentDownloadFolderTemplate || '')}"></label>
      </details>
    </td>
    <td data-col="mute"><input type="checkbox" data-mute ${c.muted ? 'checked' : ''} aria-label="Mute ${escape(c.name)} on Telegram"></td>
    <td data-col="reactions"><div class="reaction-inputs">
      <input type="text" data-r="reactionIcon" value="${escape(c.reactionIcon || '')}" placeholder="Done" title="Reaction when download completes" aria-label="Done reaction for ${escape(c.name)}">
      <input type="text" data-r="downloadStartIcon" value="${escape(c.downloadStartIcon || '')}" placeholder="Start" title="Reaction when download starts" aria-label="Start reaction for ${escape(c.name)}">
      <input type="text" data-r="historyIcon" value="${escape(c.historyIcon || '')}" placeholder="History" title="Reaction when history saved" aria-label="History reaction for ${escape(c.name)}">
      <button type="button" data-act="load-reactions" class="inline">Load emoji</button>
    </div></td>
    <td data-col="history"><input type="checkbox" data-k="saveHistory" aria-label="Save history for ${escape(c.name)}" ${c.saveHistory ? 'checked' : ''}></td>
    <td data-col="sync" class="${sync.stale ? 'sync-stale' : 'sync-ok'}" title="${escape(sync.title)}" data-bootstrap-status>${escape(sync.text)}</td>
    <td data-col="bootstrap"><button data-act="bootstrap" title="Scan history for new media since last sync">${c.selected ? '⬇ Sync' : '⬇ Bootstrap'}</button></td>
    <td data-col="test"><button data-act="test-download" title="Download newest 10 media as probe">🧪 Test 10</button></td>
  `;

  tr.addEventListener('change', async (e) => {
    const t = e.target;
    if (t.dataset.mute !== undefined) {
      const want = t.checked;
      const prev = !!c.muted;
      try {
        await api(`/api/chats/${c.id}/mute`, { method: 'POST', body: JSON.stringify({ muted: want }) });
        c.muted = want;
        toast(want ? 'Chat muted on Telegram.' : 'Chat unmuted on Telegram.');
      } catch (err) {
        t.checked = prev;
        toastError(err.message);
      }
      return;
    }
    if (!t.dataset.k && !t.dataset.p) return;
    let body = {};
    if (t.dataset.k) {
      const k = t.dataset.k;
      body[k] = t.type === 'checkbox' ? t.checked : t.type === 'number' ? Number(t.value) : t.value;
    } else {
      body.plugins = { ...(c.plugins || {}), [t.dataset.p]: t.checked };
    }
    try {
      await api(`/api/chats/${c.id}`, { method: 'PATCH', body: JSON.stringify(body) });
      if (t.dataset.k) c[t.dataset.k] = body[t.dataset.k];
      else c.plugins = body.plugins;
      if ('selected' in body) tr.classList.toggle('selected', body.selected);
    } catch (err) {
      revertChatControl(t, c);
      toastError(err.message);
    }
  });

  tr.querySelectorAll('input[data-f]').forEach(inp => {
    inp.addEventListener('change', async () => {
      const key = inp.dataset.f;
      const prev = c[key] || '';
      const apiBody = {
        socialDownloadFolderTemplate: inp.dataset.f === 'socialDownloadFolderTemplate' ? inp.value : undefined,
        youtubeDownloadFolderTemplate: inp.dataset.f === 'youtubeDownloadFolderTemplate' ? inp.value : undefined,
        otherDownloadFolderTemplate: inp.dataset.f === 'otherDownloadFolderTemplate' ? inp.value : undefined,
        torrentDownloadFolderTemplate: inp.dataset.f === 'torrentDownloadFolderTemplate' ? inp.value : undefined,
      };
      Object.keys(apiBody).forEach(k => { if (apiBody[k] === undefined) delete apiBody[k]; });
      try {
        await api(`/api/chats/${c.id}/features`, { method: 'PATCH', body: JSON.stringify(apiBody) });
        c[key] = inp.value;
      } catch (err) {
        inp.value = prev;
        toastError(err.message);
      }
    });
  });

  tr.querySelectorAll('input[data-r]').forEach(inp => {
    inp.addEventListener('change', async () => {
      const key = inp.dataset.r;
      const prev = c[key] || '';
      const body = { [key]: inp.value };
      try {
        await api(`/api/chats/${c.id}/features`, { method: 'PATCH', body: JSON.stringify(body) });
        c[key] = inp.value;
      } catch (err) {
        inp.value = prev;
        toastError(err.message);
      }
    });
  });

  tr.querySelector('[data-act="load-reactions"]')?.addEventListener('click', async (e) => {
    e.target.disabled = true;
    try {
      const res = await api(`/api/chats/${c.id}/reactions`);
      if (!res.reactions?.length) { toast('No reactions available for this chat.'); return; }
      toast(`Available: ${res.reactions.slice(0, 12).join(' ')}${res.reactions.length > 12 ? '…' : ''}`);
    } catch (err) { toastError(err.message); }
    finally { e.target.disabled = false; }
  });

  tr.querySelector('[data-act="bootstrap"]').onclick = async (e) => {
    const label = c.selected ? 'Sync' : 'Bootstrap';
    if (!await confirmDialog({ title: `${label} history`, message: `${label} "${c.name}"?\n\nScans Telegram history (newest first), queues new media, then downloads gradually. Monitor captures live messages between scans.` }))
      return;
    e.target.disabled = true; e.target.textContent = '…';
    try {
      const res = await startHistoryJob(c.id, c.selected);
      toast(res.message || `${label} started — watch the Queue tab.`);
      refreshBootstrapStatusCell(tr, c.id);
    } catch (err) { toastError(err.message); }
    finally { e.target.disabled = false; e.target.textContent = c.selected ? '⬇ Sync' : '⬇ Bootstrap'; }
  };

  tr.querySelector('[data-act="test-download"]').onclick = async (e) => {
    if (!await confirmDialog({ title: 'Test download', message: `Test download for "${c.name}"?\n\nFinds the newest 10 media messages and attempts a real download for each. Nothing is written to the queue database.` }))
      return;
    e.target.disabled = true; e.target.textContent = '…';
    try {
      const report = await api(`/api/chats/${c.id}/test-download?limit=10`, { method: 'POST' });
      showTestDownloadReport(report);
    } catch (err) { toastError(err.message); }
    finally { e.target.disabled = false; e.target.textContent = '🧪 Test 10'; }
  };

  showBootstrapJob(tr, activeJobsByChat[c.id]);
  return tr;
}

function showBootstrapJob(tr, job) {
  const cell = tr.querySelector('[data-bootstrap-status]');
  if (!cell || !job || job.done) return;
  cell.textContent = `${job.status} · ${job.discovered}/${job.inserted}`;
  cell.classList.add('sync-stale');
}

async function refreshBootstrapStatusCell(tr, chatId) {
  try { showBootstrapJob(tr, await api(`/api/chats/${chatId}/bootstrap`)); }
  catch { /* no active job */ }
}

function showTestDownloadReport(report) {
  const panel = $('#test-download-panel');
  const summary = $('#test-download-summary');
  const detail = $('#test-download-detail');
  if (!panel || !summary || !detail) return;

  panel.classList.remove('hidden');
  panel.classList.toggle('danger-card', !report.readyForBootstrap);
  summary.innerHTML = `<strong>${escape(report.chatName)}</strong> — ${escape(report.summary)}<br>` +
    `Samples: ${report.samplesFound}/${report.requestedSamples} · ` +
    `✓ ${report.succeeded} · skip ${report.skipped} · ✗ ${report.failed} · ` +
    (report.readyForBootstrap
      ? '<span class="text-ok">Ready for bootstrap</span>'
      : '<span class="text-bad">Fix issues before bootstrap</span>');

  const lines = [];
  lines.push('=== SETUP ===');
  for (const s of report.setupLogs || [])
    lines.push(`${s.ok ? 'OK' : 'FAIL'}  [${s.phase}] ${s.detail}`);

  lines.push('', '=== ITEMS ===');
  for (const it of report.items || []) {
    lines.push(`--- msg ${it.messageId} · ${it.kind} · ${it.outcome} ---`);
    if (it.fileName) lines.push(`    file: ${it.fileName} (${it.sizeBytes} bytes)`);
    for (const s of it.steps || [])
      lines.push(`  ${s.ok ? 'OK' : 'FAIL'}  [${s.phase}] ${s.detail}`);
    if (it.error) lines.push(`  => ${it.error}`);
  }

  detail.textContent = lines.join('\n');
  panel.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

$('#btn-test-download-dismiss')?.addEventListener('click', () => {
  $('#test-download-panel')?.classList.add('hidden');
});

function escape(s) { return String(s ?? '').replace(/[&<>"]/g, ch => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;' }[ch])); }

function statusCount(byStatus, name) {
  const row = (byStatus || []).find(x => x.status === name);
  return row ? row.count : 0;
}

function chatName(chatId) {
  const c = allChats.find(x => x.id === chatId);
  return c ? c.name : `#${chatId}`;
}

async function retryAllFailed(chatId = null) {
  const stats = await api('/api/queue/stats');
  const count = chatId
    ? statusCount(
        (await api('/api/queue/stats/by-chat')).find(r => r.chatId === chatId)?.byStatus,
        'failed')
    : statusCount(stats.byStatus, 'failed');
  if (!count) {
    toast('No failed items to retry.');
    return;
  }
  const scope = chatId ? `"${chatName(chatId)}"` : 'all chats';
  if (!await confirmDialog({ message: `Re-queue ${count} failed item(s) for ${scope}?\n\nThe download orchestrator will pick them up automatically.` }))
    return;
  const url = chatId
    ? `/api/queue/retry-failed?chatId=${chatId}`
    : '/api/queue/retry-failed';
  const res = await api(url, { method: 'POST' });
  toast(`Re-queued ${res.requeued} item(s).`);
  refreshQueue();
}

async function retryAllSkipped(chatId = null) {
  const stats = await api('/api/queue/stats');
  const count = chatId
    ? statusCount(
        (await api('/api/queue/stats/by-chat')).find(r => r.chatId === chatId)?.byStatus,
        'skipped')
    : statusCount(stats.byStatus, 'skipped');
  if (!count) {
    toast('No skipped items to retry.');
    return;
  }
  const scope = chatId ? `"${chatName(chatId)}"` : 'all chats';
  if (!await confirmDialog({ message: `Re-queue ${count} skipped item(s) for ${scope}?\n\nItems may skip again if they are duplicates, stickers/voice, or blocked by filters (V/P/M/F, Min MB, regex).` }))
    return;
  const url = chatId
    ? `/api/queue/retry-skipped?chatId=${chatId}`
    : '/api/queue/retry-skipped';
  const res = await api(url, { method: 'POST' });
  toast(`Re-queued ${res.requeued} item(s).`);
  refreshQueue();
}

async function deleteAllFailed(chatId = null) {
  const stats = await api('/api/queue/stats');
  const count = chatId
    ? statusCount(
        (await api('/api/queue/stats/by-chat')).find(r => r.chatId === chatId)?.byStatus,
        'failed')
    : statusCount(stats.byStatus, 'failed');
  if (!count) {
    toast('No failed items to delete.');
    return;
  }
  const scope = chatId ? `"${chatName(chatId)}"` : 'all chats';
  if (!await confirmDialog({ message: `Permanently delete ${count} failed record(s) for ${scope}?\n\nThis removes them from the queue database only — files already on disk are not deleted.`, confirmText: 'Delete', danger: true }))
    return;
  const url = chatId
    ? `/api/queue/delete-failed?chatId=${chatId}`
    : '/api/queue/delete-failed';
  const res = await api(url, { method: 'POST' });
  toast(`Deleted ${res.deleted} failed record(s).`);
  refreshQueue();
}

async function clearChatQueue(chatId) {
  const name = chatName(chatId);
  const stats = (await api('/api/queue/stats/by-chat')).find(r => r.chatId === chatId);
  const total = stats?.total ?? 0;
  if (!total) {
    toast(`No queue records for "${name}".`);
    return;
  }
  if (!await confirmDialog({ message: `Clear all ${total} queue record(s) for "${name}"?\n\nThis removes pending/queued/done/failed rows from the database. Downloaded files on disk are NOT deleted. Bootstrap history watermark is reset.`, confirmText: 'Clear queue', danger: true }))
    return;
  const res = await api(`/api/queue/clear?chatId=${chatId}`, { method: 'POST' });
  toast(`Cleared ${res.deleted} record(s) for "${name}".`);
  refreshQueue();
}

// ─── Queue ────────────────────────────────────────────────────────────────────
async function refreshQueue() {
  try {
    if (!allChats.length) {
      try { allChats = await api('/api/chats'); markLoaderOk('chats'); } catch (e) { markLoaderFail('chats', e); }
    }
    const [stats, byChat, jobs, limits, failed, skipped, flood, dlState] = await Promise.all([
      api('/api/queue/stats'),
      api('/api/queue/stats/by-chat'),
      api('/api/bootstrap/jobs'),
      api('/api/settings/limits'),
      api('/api/queue/items?status=failed&limit=50'),
      api('/api/queue/items?status=skipped&limit=100'),
      api('/api/flood-wait'),
      api('/api/queue/downloads'),
    ]);

    queueByChat = Object.fromEntries(byChat.map(r => [r.chatId, r]));
    markLoaderOk('queue');

    renderFloodBanner(flood);
    if (limits.thresholds) LIMIT_THRESHOLDS = { ...limits.thresholds };
    if (limits.defaults) limitsDefaults = limits.defaults;

    renderQueueCards(stats);
    if (!tableHasFocusedInput('#bootstrap-table')) renderBootstrapJobs(jobs);
    if (!tableHasFocusedInput('#queue-chat-table')) renderQueueByChat(byChat);
    if (!tableHasFocusedInput('#skipped-table')) renderSkippedItems(skipped);
    if (!tableHasFocusedInput('#failed-table')) renderFailedItems(failed);
    ['#bootstrap-table', '#queue-chat-table', '#skipped-table', '#failed-table'].forEach(s => labelTableCells($(s)));

    if (!tableHasFocusedInput('.limits-grid')) {
      $('#q-capacity').value = limits.scannerApiCapacity ?? 5;
      $('#q-refill').value   = limits.scannerApiRefillPerSecond ?? 1;
      $('#q-threads').value  = limits.downloadThreads ?? 3;
    }
    applyLimitWarnings(
      Number($('#q-capacity').value),
      Number($('#q-refill').value),
      Number($('#q-threads').value));
    applyParallelBootstrapUi(limits);
    applyDownloadsPausedUi(!!limits.downloadsPaused);
    applyGlobalDownloadsPaused(!!dlState.paused, 0);
  } catch (e) {
    markLoaderFail('queue', e);
    toastError(e.message);
  }
}

['#q-capacity', '#q-refill', '#q-threads'].forEach(sel => {
  $(sel)?.addEventListener('input', () => applyLimitWarnings(
    Number($('#q-capacity').value),
    Number($('#q-refill').value),
    Number($('#q-threads').value)));
});

function renderQueueCards(stats) {
  const by = Object.fromEntries((stats.byStatus || []).map(x => [x.status, x]));
  const cards = [
    ['Total', stats.total, ''],
    ['Pending', by.pending?.count || 0, 'pending'],
    ['Queued', by.queued?.count || 0, 'queued'],
    ['In progress', by.in_progress?.count || 0, 'in_progress'],
    ['Done', by.done?.count || 0, 'done'],
    ['Failed', by.failed?.count || 0, 'failed'],
    ['Skipped', by.skipped?.count || 0, 'skipped'],
    ['Storage', fmtBytes(stats.totalBytes), ''],
  ];
  const el = $('#queue-cards');
  el.innerHTML = cards.map(([label, val]) =>
    `<div class="metric"><span class="muted">${escape(label)}</span><strong>${escape(String(val))}</strong></div>`
  ).join('');
  if (stats.legacyDedupCount) {
    el.innerHTML += `<div class="metric"><span class="muted">Legacy dedup</span><strong>${stats.legacyDedupCount}</strong></div>`;
  }
}

function renderBootstrapJobs(jobs) {
  const body = $('#bootstrap-table tbody');
  body.innerHTML = '';
  if (!jobs.length) {
    body.innerHTML = '<tr><td colspan="6" class="muted">No bootstrap jobs running.</td></tr>';
    return;
  }
  jobs.forEach(j => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escape(j.chatName || chatName(j.chatId))}</td>
      <td>${escape(j.status)}${j.error ? `<br><small class="error-cell">${escape(j.error)}</small>` : ''}</td>
      <td>${j.discovered}</td>
      <td>${j.inserted}</td>
      <td><small class="muted">${new Date(j.startedAt).toLocaleString()}</small></td>
      <td>${j.done ? '' : `<button data-act="cancel" data-id="${j.chatId}">Cancel</button>`}</td>`;
    tr.querySelector('[data-act="cancel"]')?.addEventListener('click', async (e) => {
      e.target.disabled = true;
      try { await api(`/api/chats/${j.chatId}/bootstrap`, { method: 'DELETE' }); refreshQueue(); }
      catch (err) { toastError(err.message); e.target.disabled = false; }
    });
    body.appendChild(tr);
  });
}

function renderQueueByChat(rows) {
  const body = $('#queue-chat-table tbody');
  body.innerHTML = '';
  const sorted = [...rows].sort((a, b) => b.total - a.total);
  if (!sorted.length) {
    body.innerHTML = '<tr><td colspan="11" class="muted">No tracked media yet — bootstrap a chat to populate the queue.</td></tr>';
    return;
  }
  sorted.forEach(r => {
    const tr = document.createElement('tr');
    const skippedN = statusCount(r.byStatus, 'skipped');
    const sync = formatLastSync(r);
    tr.innerHTML = `
      <td>${escape(chatName(r.chatId))}</td>
      <td>${r.total}</td>
      <td>${statusCount(r.byStatus, 'pending')}</td>
      <td>${statusCount(r.byStatus, 'queued')}</td>
      <td>${statusCount(r.byStatus, 'in_progress')}</td>
      <td>${statusCount(r.byStatus, 'done')}</td>
      <td>${statusCount(r.byStatus, 'failed')}</td>
      <td class="skip-count" title="${skippedN ? 'See Skipped items table below for reasons' : ''}">${skippedN}</td>
      <td class="${sync.stale ? 'sync-stale' : 'sync-ok'}" title="${escape(sync.title)}">${escape(sync.text)}</td>
      <td>${fmtBytes(r.bytes)}</td>
      <td class="actions-cell">
        <button data-act="bootstrap" data-id="${r.chatId}">Bootstrap</button>
        ${r.total ? `<button data-act="clear-queue" data-id="${r.chatId}">Clear queue</button>` : ''}
        ${statusCount(r.byStatus, 'failed') ? `<button data-act="retry-failed" data-id="${r.chatId}">Retry failed</button>` : ''}
        ${statusCount(r.byStatus, 'skipped') ? `<button data-act="retry-skipped" data-id="${r.chatId}">Retry skipped</button>` : ''}
      </td>`;
    tr.querySelector('[data-act="bootstrap"]').onclick = async (e) => {
      e.target.disabled = true; e.target.textContent = '…';
      try { await startBootstrap(r.chatId); refreshQueue(); }
      catch (err) { toastError(err.message); }
      finally { e.target.disabled = false; e.target.textContent = 'Bootstrap'; }
    };
    tr.querySelector('[data-act="clear-queue"]')?.addEventListener('click', async (e) => {
      e.target.disabled = true;
      try { await clearChatQueue(r.chatId); }
      catch (err) { toastError(err.message); }
      finally { e.target.disabled = false; }
    });
    tr.querySelector('[data-act="retry-failed"]')?.addEventListener('click', async (e) => {
      e.target.disabled = true;
      try { await retryAllFailed(r.chatId); }
      catch (err) { toastError(err.message); }
      finally { e.target.disabled = false; }
    });
    tr.querySelector('[data-act="retry-skipped"]')?.addEventListener('click', async (e) => {
      e.target.disabled = true;
      try { await retryAllSkipped(r.chatId); }
      catch (err) { toastError(err.message); }
      finally { e.target.disabled = false; }
    });
    body.appendChild(tr);
  });
}

function renderSkippedItems(items) {
  const body = $('#skipped-table tbody');
  body.innerHTML = '';
  if (!items.length) {
    body.innerHTML = '<tr><td colspan="7" class="muted">No skipped items in the latest batch.</td></tr>';
    return;
  }
  items.forEach(it => {
    const tr = document.createElement('tr');
    const reason = it.lastError || '(no reason recorded — re-bootstrap after update to capture reasons)';
    tr.innerHTML = `
      <td>${escape(chatName(it.chatId))}</td>
      <td>${it.messageId}</td>
      <td>${escape(it.fileName || '—')}</td>
      <td>${escape(it.kind || '')}</td>
      <td>${fmtBytes(it.sizeBytes)}</td>
      <td class="error-cell" title="${escape(reason)}">${escape(reason)}</td>
      <td><button data-act="retry">Retry</button></td>`;
    tr.querySelector('[data-act="retry"]').onclick = async (e) => {
      e.target.disabled = true;
      try {
        await api(`/api/queue/${it.chatId}/${it.messageId}/retry`, { method: 'POST' });
        refreshQueue();
      } catch (err) { toastError(err.message); e.target.disabled = false; }
    };
    body.appendChild(tr);
  });
}

function renderFailedItems(items) {
  const body = $('#failed-table tbody');
  body.innerHTML = '';
  if (!items.length) {
    body.innerHTML = '<tr><td colspan="8" class="muted">No failed items.</td></tr>';
    return;
  }
  items.forEach(it => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escape(chatName(it.chatId))}</td>
      <td>${it.messageId}</td>
      <td>${escape(it.fileName || '—')}</td>
      <td>${escape(it.kind || '')}</td>
      <td>${fmtBytes(it.sizeBytes)}</td>
      <td>${it.attempts ?? 0}</td>
      <td class="error-cell">${escape(it.lastError || '')}</td>
      <td><button data-act="retry">Retry</button></td>`;
    tr.querySelector('[data-act="retry"]').onclick = async (e) => {
      e.target.disabled = true;
      try {
        await api(`/api/queue/${it.chatId}/${it.messageId}/retry`, { method: 'POST' });
        refreshQueue();
      } catch (err) { toastError(err.message); e.target.disabled = false; }
    };
    body.appendChild(tr);
  });
}

$('#btn-save-limits').onclick = async () => {
  const capacity = Number($('#q-capacity').value);
  const refill   = Number($('#q-refill').value);
  const threads  = Number($('#q-threads').value);
  applyLimitWarnings(capacity, refill, threads);
  const danger = limitLevel(capacity, LIMIT_THRESHOLDS.scannerCapacity) === 'danger'
    || limitLevel(refill, LIMIT_THRESHOLDS.scannerRefill) === 'danger';
  if (danger && !await confirmDialog({ message: 'These scanner settings are at or near maximum and may trigger Telegram rate limits or account restrictions. Save anyway?', danger: true }))
    return;
  const wantParallel = $('#q-parallel').checked;
  if (wantParallel && !await confirmDialog({
    title: 'Enable parallel bootstrap',
    message: 'Parallel bootstrap runs multiple history scans at once (shared rate limit). Enable it?',
    confirmText: 'Enable parallel',
    danger: true,
  })) {
    $('#q-parallel').checked = false;
    $('#lbl-max-parallel').classList.add('hidden');
  }
  try {
    await api('/api/settings/limits', {
      method: 'POST',
      body: JSON.stringify({
        scannerApiCapacity: capacity,
        scannerApiRefillPerSecond: refill,
        downloadThreads: threads,
        allowParallelBootstrap: $('#q-parallel').checked,
        maxParallelBootstraps: Number($('#q-max-parallel').value) || 3,
      }),
    });
    toast('Limits saved — active scans pick them up immediately.');
    refreshQueue();
  } catch (e) { toastError(e.message); }
};

$('#btn-refresh-failed').onclick = () => refreshQueue();
$('#btn-refresh-skipped').onclick = () => refreshQueue();
$('#btn-retry-all-skipped').onclick = async () => {
  const btn = $('#btn-retry-all-skipped');
  btn.disabled = true;
  try { await retryAllSkipped(); }
  catch (err) { toastError(err.message); }
  finally { btn.disabled = false; }
};
$('#btn-retry-all-failed').onclick = async () => {
  const btn = $('#btn-retry-all-failed');
  btn.disabled = true;
  try { await retryAllFailed(); }
  catch (err) { toastError(err.message); }
  finally { btn.disabled = false; }
};
$('#btn-delete-all-failed').onclick = async () => {
  const btn = $('#btn-delete-all-failed');
  btn.disabled = true;
  try { await deleteAllFailed(); }
  catch (err) { toastError(err.message); }
  finally { btn.disabled = false; }
};
$('#btn-toggle-downloads').onclick = async () => {
  const btn = $('#btn-toggle-downloads');
  const paused = btn.textContent.startsWith('Pause');
  if (paused && !await confirmDialog({ message: 'Pause downloads?\n\nQueued items stay in the database. In-progress items move back to queued.' }))
    return;
  btn.disabled = true;
  try {
    await setDownloadsPaused(paused);
    refreshQueue();
  } catch (err) { toastError(err.message); }
  finally { btn.disabled = false; }
};

// ─── Status ─────────────────────────────────────────────────────────────────
async function refreshStatus() {
  try {
    const [s, downloads] = await Promise.all([api('/api/status'), api('/api/downloads')]);
    markLoaderOk('status');
    $('#m-monitored').textContent = `${s.monitoredChats} / ${s.totalChats}`;
    $('#m-active').textContent = s.activeDownloads;
    $('#m-folder').textContent = s.downloadFolder || '—';
    $('#m-downloads-state').textContent = s.downloadsPaused ? 'Paused' : 'Running';
    if (s.floodWait?.active) {
      $('#m-flood').textContent = `~${s.floodWait.remainingSeconds}s`;
    } else {
      $('#m-flood').textContent = 'Clear';
    }
    applyGlobalDownloadsPaused(!!s.downloadsPaused, 0);
    renderFloodBanner(s.floodWait);
    if (tableHasFocusedInput('#dl-table')) return;
    const body = $('#dl-table tbody');
    body.innerHTML = '';
    $('#dl-empty')?.classList.toggle('hidden', downloads.length > 0);
    downloads.forEach(d => {
      const st = fmtStaleness(d.at);
      const pct = Math.max(0, Math.min(100, Number(d.percent) || 0));
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${escape(d.chat)}</td>
        <td>${escape(d.file)}</td>
        <td>${escape(d.plugin || '')}</td>
        <td><span class="pill ${escape(String(d.status).toLowerCase())}">${escape(d.status)}</span></td>
        <td><span class="bar"><span></span></span> ${pct}%</td>
        <td>${fmtBytes(d.bytesDone)} / ${fmtBytes(d.bytesTotal)}</td>
        <td class="${st.stale ? 'sync-stale' : ''}" title="${escape(st.title || '')}">${escape(st.text)}</td>
        <td class="error-cell">${escape(d.error || '')}</td>`;
      // CSP forbids inline style attributes; CSSOM writes are allowed.
      tr.querySelector('.bar > span').style.width = `${pct}%`;
      body.appendChild(tr);
    });
    labelTableCells($('#dl-table'));
  } catch (e) {
    markLoaderFail('status', e);
    toastError(e.message);
  }
}

// ─── Settings ───────────────────────────────────────────────────────────────
async function loadSettings() {
  renderColumnPicker();
  try {
    const [s, ws] = await Promise.all([api('/api/settings'), api('/api/web-auth/status')]);
    markLoaderOk('settings');
    applyDefaultPasswordUi(!!ws.mustChangePassword);
    $('#s-folder').value = s.downloadFolder || '';
    if ($('#s-folder-layout')) $('#s-folder-layout').value = s.folderLayout || 'TypeFirst';
    $('#s-threads').value = s.downloadThreads || 3;
    applySettingsThreadWarning(Number($('#s-threads').value));
    const p = loadUiPrefs();
    if ($('#ui-theme')) $('#ui-theme').value = p.theme || 'system';
    if ($('#ui-accent') && p.accent) $('#ui-accent').value = p.accent;
    if ($('#ui-density')) $('#ui-density').value = p.density || 'comfortable';
  } catch (e) {
    markLoaderFail('settings', e);
    toastError(e.message);
  }
}

const DEFAULT_ACCENT = '#2563eb';
const ALWAYS_VISIBLE_COLUMNS = new Set(['monitor', 'name']);

function renderColumnPicker() {
  const box = $('#ui-columns');
  if (!box) return;
  const hidden = new Set(loadUiPrefs().hiddenColumns || []);
  box.innerHTML = '';
  $$('#chat-table thead th[data-col]').forEach(th => {
    const col = th.dataset.col;
    if (ALWAYS_VISIBLE_COLUMNS.has(col)) return;
    const label = document.createElement('label');
    label.innerHTML = `<input type="checkbox" data-col-toggle="${escape(col)}" ${hidden.has(col) ? '' : 'checked'}> ${escape(th.textContent.trim())}`;
    box.appendChild(label);
  });
}

function saveAppearanceFromForm() {
  const hiddenColumns = $$('#ui-columns input[data-col-toggle]').filter(i => !i.checked).map(i => i.dataset.colToggle);
  saveUiPrefs({
    theme: $('#ui-theme')?.value || 'system',
    accent: $('#ui-accent')?.value || DEFAULT_ACCENT,
    density: $('#ui-density')?.value || 'comfortable',
    hiddenColumns,
  });
  applyThemeFromPrefs();
}

['#ui-theme', '#ui-density'].forEach(sel => $(sel)?.addEventListener('change', saveAppearanceFromForm));
$('#ui-accent')?.addEventListener('input', saveAppearanceFromForm);
$('#ui-columns')?.addEventListener('change', saveAppearanceFromForm);

$('#btn-reset-appearance')?.addEventListener('click', () => {
  saveUiPrefs({ theme: 'system', accent: null, density: 'comfortable', hiddenColumns: [] });
  applyThemeFromPrefs();
  $('#ui-theme').value = 'system';
  $('#ui-accent').value = DEFAULT_ACCENT;
  $('#ui-density').value = 'comfortable';
  renderColumnPicker();
  toast('Appearance reset to defaults.');
});

$('#btn-reset-limits')?.addEventListener('click', async () => {
  if (!limitsDefaults) {
    try {
      const limits = await api('/api/settings/limits');
      limitsDefaults = limits.defaults;
      if (limits.thresholds) LIMIT_THRESHOLDS = { ...limits.thresholds };
    } catch (e) { toastError(e.message); return; }
  }
  if (!limitsDefaults) { toast('Defaults not loaded yet — open Queue tab first.'); return; }
  $('#q-capacity').value = limitsDefaults.scannerApiCapacity;
  $('#q-refill').value = limitsDefaults.scannerApiRefillPerSecond;
  $('#q-threads').value = limitsDefaults.downloadThreads;
  applyLimitWarnings(Number($('#q-capacity').value), Number($('#q-refill').value), Number($('#q-threads').value));
  toast('Limits reset to server defaults — click Save limits to apply.');
});
$('#s-threads')?.addEventListener('input', () => applySettingsThreadWarning(Number($('#s-threads').value)));
$('#btn-save-folder').onclick = async () => {
  try { await api('/api/settings/download-folder', { method: 'POST', body: JSON.stringify({ folder: $('#s-folder').value }) }); toast('Saved.'); }
  catch (e) { toastError(e.message); }
};
$('#btn-save-folder-layout')?.addEventListener('click', async () => {
  try {
    await api('/api/settings/folder-layout', { method: 'POST', body: JSON.stringify({ layout: $('#s-folder-layout').value }) });
    toast('Folder layout saved. New downloads use the selected structure.');
  } catch (e) { toastError(e.message); }
});
$('#btn-save-threads').onclick = async () => {
  try { await api('/api/settings/threads', { method: 'POST', body: JSON.stringify({ threads: Number($('#s-threads').value) }) }); toast('Saved.'); }
  catch (e) { toastError(e.message); }
};

// ─── Poller ─────────────────────────────────────────────────────────────────
async function poll() {
  if (!webAuthenticated || mustChangeWebPassword) return;
  try {
    const [ls, flood] = await Promise.all([api('/api/login/status'), api('/api/flood-wait')]);
    renderFloodBanner(flood);
    $('#conn').className = ls.loggedIn ? 'ok' : 'bad';
    if (ls.loggedIn && !loggedIn) await refreshLoginStatus();
    else if (!ls.loggedIn && loggedIn) await refreshLoginStatus();
    if (loggedIn) await refreshGlobalDownloadsPaused();
    if (loggedIn && $('#tab-status').classList.contains('active')) await refreshStatus();
    if (loggedIn && $('#tab-queue').classList.contains('active')) await refreshQueue();
  } catch {
    if ($('#conn')) $('#conn').className = 'bad';
    throw new Error('Poll failed');
  }
}

initApp();
schedulePoll(poll);
const _chatFilterPref = loadUiPrefs().chatFilter;
if (_chatFilterPref && $('#chat-search')) $('#chat-search').value = _chatFilterPref;

// ─── Logs (tail + SSE, client ring buffer) ───────────────────────────────────
const LOG_BUFFER_MAX = 5000;
const LOG_RENDER_MAX = 2000;
let logBuffer = [];
let logStream = null;
let logSearchMode = false;

function escapeHtml(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function renderLogView() {
  const slice = logBuffer.slice(-LOG_RENDER_MAX);
  $('#log-view').innerHTML = slice.map(l => {
    const cls = l.level ? `lv-${l.level}` : '';
    return `<span class="${cls}">${escapeHtml(l.text)}</span>`;
  }).join('\n');
  const el = $('#log-view');
  if ($('#log-follow').checked)
    el.scrollTop = el.scrollHeight;
}

function pushLogLines(lines) {
  for (const l of lines) {
    logBuffer.push({ text: l.text, level: l.level });
    if (logBuffer.length > LOG_BUFFER_MAX)
      logBuffer.shift();
  }
  renderLogView();
}

function stopLogStream() {
  if (logStream) { logStream.close(); logStream = null; }
}

function startLogStream(file) {
  stopLogStream();
  if (!$('#log-follow').checked) return;
  const url = `/api/logs/stream?file=${encodeURIComponent(file)}`;
  logStream = new EventSource(url);
  logStream.onmessage = (ev) => {
    try {
      const row = JSON.parse(ev.data);
      if (row.text) pushLogLines([row]);
    } catch {}
  };
  logStream.onerror = () => { /* EventSource auto-reconnects */ };
}

async function loadLogFiles() {
  const files = await api('/api/logs/files');
  const sel = $('#log-file');
  const prev = sel.value;
  sel.innerHTML = files.map(f =>
    `<option value="${escapeHtml(f.name)}">${escapeHtml(f.name)} (${fmtBytes(f.sizeBytes)})</option>`
  ).join('');
  if (prev && files.some(f => f.name === prev)) sel.value = prev;
  else if (files.length) sel.value = files[0].name;
  return files;
}

async function refreshLogTail() {
  const file = $('#log-file').value;
  if (!file) return;
  logSearchMode = false;
  const level = $('#log-level').value;
  const search = $('#log-filter').value.trim();
  const q = new URLSearchParams({ file, lines: '500' });
  if (level) q.set('level', level);
  if (search) q.set('search', search);
  const tail = await api(`/api/logs/tail?${q}`);
  logBuffer = tail.lines.map(l => ({ text: l.text, level: l.level }));
  $('#log-meta').textContent = [
    tail.message,
    `${tail.lines.length} line(s) · file ${fmtBytes(tail.fileSizeBytes)}`,
  ].filter(Boolean).join(' · ');
  renderLogView();
  startLogStream(file);
}

async function searchLogs() {
  const file = $('#log-file').value;
  const q = $('#log-filter').value.trim();
  if (!q) { toast('Enter filter text to search the full file (server-side scan).'); return; }
  stopLogStream();
  logSearchMode = true;
  $('#log-meta').textContent = 'Searching…';
  const res = await api(`/api/logs/search?file=${encodeURIComponent(file)}&q=${encodeURIComponent(q)}&limit=200`);
  logBuffer = res.lines.map(l => ({ text: `[${l.lineNumber}] ${l.text}`, level: l.level }));
  $('#log-meta').textContent = `Search "${res.query}" — ${res.returned} hit(s)${res.hasMore ? ' (more available — refine query)' : ''}`;
  renderLogView();
}

async function openLogsTab() {
  try {
    await loadLogFiles();
    await refreshLogTail();
    markLoaderOk('logs');
  } catch (e) {
    markLoaderFail('logs', e);
    toastError(e.message);
    if ($('#log-meta')) $('#log-meta').textContent = e.message;
  }
}

$('#btn-log-refresh').onclick = () => refreshLogTail().catch(e => toastError(e.message));
$('#btn-log-search').onclick = () => searchLogs().catch(e => toastError(e.message));
$('#log-file').onchange = () => refreshLogTail().catch(e => toastError(e.message));
$('#log-level').onchange = () => refreshLogTail().catch(e => toastError(e.message));
$('#log-filter').onkeydown = (e) => { if (e.key === 'Enter') refreshLogTail().catch(er => toastError(er.message)); };
$('#log-follow').onchange = () => {
  if ($('#log-follow').checked && !logSearchMode) startLogStream($('#log-file').value);
  else stopLogStream();
};
$('#btn-log-clear').onclick = () => { logBuffer = []; renderLogView(); };
