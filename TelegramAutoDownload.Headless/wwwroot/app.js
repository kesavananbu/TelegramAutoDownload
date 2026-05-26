'use strict';
const $  = (q, r=document) => r.querySelector(q);
const $$ = (q, r=document) => Array.from(r.querySelectorAll(q));

// ─── Tabs ────────────────────────────────────────────────────────────────────
$$('header nav button').forEach(b => b.addEventListener('click', () => {
  if (b.disabled) return;
  $$('header nav button').forEach(x => x.classList.toggle('active', x === b));
  $$('.tab').forEach(t => t.classList.toggle('active', t.id === `tab-${b.dataset.tab}`));
  if (b.dataset.tab === 'chats')    loadChats();
  if (b.dataset.tab === 'queue')    refreshQueue();
  if (b.dataset.tab === 'status')   refreshStatus();
  if (b.dataset.tab === 'settings') loadSettings();
}));

function unlockTabs(on) {
  $$('header nav button').forEach(b => {
    if (b.dataset.tab !== 'login') b.disabled = !on;
  });
}

// ─── Helpers ────────────────────────────────────────────────────────────────
async function api(path, opts = {}) {
  const r = await fetch(path, { headers: { 'Content-Type': 'application/json' }, ...opts });
  let data = {};
  try { data = await r.json(); } catch {}
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

const LIMIT_THRESHOLDS = {
  scannerCapacity: { max: 100, warn: 20, danger: 50 },
  scannerRefill:   { max: 50,  warn: 5,  danger: 10 },
  downloadThreads: { max: 10,  warn: 6,  danger: 8 },
};

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
      if (confirm(`${e.message}\n\nStart anyway (one-time override)?\nParallel scans share the same rate limiter but increase ban risk.`))
        return startBootstrap(chatId, true);
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
                password: $('#step-password'), done: $('#step-done') };
function showStep(name) {
  Object.entries(steps).forEach(([n, el]) => el.classList.toggle('hidden', n !== name));
}

async function refreshLoginStatus() {
  try {
    const [ls, st] = await Promise.all([api('/api/login/status'), api('/api/settings')]);
    if (ls.loggedIn) {
      showStep('done'); unlockTabs(true); return;
    }
    if (!st.appIdConfigured) { showStep('creds'); return; }
    if (ls.stage === 'AwaitingCode')     showStep('code');
    else if (ls.stage === 'AwaitingPassword') showStep('password');
    else                                  showStep('phone');
  } catch (e) { showError(loginErr, e.message); }
}

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
    if (res.stage === 'LoggedIn')         { showStep('done'); unlockTabs(true); }
    else if (res.stage === 'AwaitingPassword') showStep('password');
    else                                       showStep('code');
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-code').onclick = async () => {
  showError(loginErr);
  try {
    const res = await api('/api/login/code', { method: 'POST', body: JSON.stringify({ code: $('#code').value.trim() }) });
    if (res.error) { showError(loginErr, res.error); return; }
    if (res.stage === 'LoggedIn')         { showStep('done'); unlockTabs(true); }
    else if (res.stage === 'AwaitingPassword') showStep('password');
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-password').onclick = async () => {
  showError(loginErr);
  try {
    const res = await api('/api/login/password', { method: 'POST', body: JSON.stringify({ password: $('#password').value }) });
    if (res.error) { showError(loginErr, res.error); return; }
    if (res.stage === 'LoggedIn') { showStep('done'); unlockTabs(true); }
  } catch (e) { showError(loginErr, e.message); }
};

$('#btn-logout').onclick = async () => {
  await api('/api/login/logout', { method: 'POST' });
  unlockTabs(false); showStep('phone');
};

// ─── Chats ──────────────────────────────────────────────────────────────────
let allChats = [];

async function loadChats() {
  try { allChats = await api('/api/chats'); renderChats(); }
  catch (e) { console.warn(e); }
}

$('#btn-refresh-chats').onclick = async () => {
  const btn = $('#btn-refresh-chats');
  btn.disabled = true; btn.textContent = '⏳ Loading…';
  try { allChats = await api('/api/chats/refresh', { method: 'POST' }); renderChats(); }
  catch (e) { alert(e.message); }
  finally { btn.disabled = false; btn.textContent = '🔄 Refresh from Telegram'; }
};

$('#chat-search').oninput = () => renderChats();

function renderChats() {
  const q = $('#chat-search').value.trim().toLowerCase();
  const rows = allChats
    .filter(c => !q || (c.name + ' ' + c.username).toLowerCase().includes(q))
    .sort((a, b) => Number(b.selected) - Number(a.selected) || a.name.localeCompare(b.name));
  $('#chat-count').textContent = `${rows.filter(r => r.selected).length} / ${allChats.length} monitored`;
  const tbody = $('#chat-table tbody');
  tbody.innerHTML = '';
  rows.forEach(c => tbody.appendChild(chatRow(c)));
}

function chatRow(c) {
  const tr = document.createElement('tr');
  if (c.selected) tr.classList.add('selected');
  tr.innerHTML = `
    <td><input type="checkbox" data-k="selected" ${c.selected ? 'checked' : ''}></td>
    <td>${escape(c.name)}<br><small class="muted">${escape(c.username || '')}</small></td>
    <td>${escape(c.type)}</td>
    <td>${c.membersCount || ''}</td>
    <td><input type="number" data-k="downloadFromSize" value="${c.downloadFromSize}" min="0" style="width:60px"></td>
    <td>
      <input type="checkbox" data-k="videos" ${c.videos ? 'checked' : ''} title="Videos">
      <input type="checkbox" data-k="photos" ${c.photos ? 'checked' : ''} title="Photos">
      <input type="checkbox" data-k="music"  ${c.music  ? 'checked' : ''} title="Music">
      <input type="checkbox" data-k="files"  ${c.files  ? 'checked' : ''} title="Files">
    </td>
    <td>
      <input type="checkbox" data-p="YouTube"     ${c.plugins?.YouTube     ? 'checked' : ''} title="YouTube">
      <input type="checkbox" data-p="SocialMedia" ${c.plugins?.SocialMedia ? 'checked' : ''} title="SocialMedia">
      <input type="checkbox" data-p="Other"       ${c.plugins?.Other       ? 'checked' : ''} title="Direct">
      <input type="checkbox" data-p="Torrent"     ${c.plugins?.Torrent     ? 'checked' : ''} title="Torrent">
    </td>
    <td><input type="text" data-k="filter" value="${escape(c.filter || '')}" placeholder="regex; pattern"></td>
    <td><input type="text" data-k="folderTemplate" value="${escape(c.folderTemplate || '')}" placeholder="{Type}/{ChatName}"></td>
    <td><input type="checkbox" data-k="saveHistory" ${c.saveHistory ? 'checked' : ''}></td>
    <td><button data-act="bootstrap" title="Rate-limited history scan → queue">⬇ Bootstrap</button></td>
  `;

  const patch = {};
  tr.addEventListener('change', async (e) => {
    const t = e.target;
    if (t.dataset.k) {
      patch[t.dataset.k] = t.type === 'checkbox' ? t.checked
                          : t.type === 'number' ? Number(t.value)
                          : t.value;
    } else if (t.dataset.p) {
      patch.plugins = { ...(patch.plugins || c.plugins || {}), [t.dataset.p]: t.checked };
    }
    try { await api(`/api/chats/${c.id}`, { method: 'PATCH', body: JSON.stringify(patch) }); }
    catch (err) { alert(err.message); return; }
    Object.assign(c, patch);
    if ('selected' in patch) tr.classList.toggle('selected', patch.selected);
  });

  tr.querySelector('[data-act="bootstrap"]').onclick = async (e) => {
    if (!c.selected) {
      alert('Enable monitoring first.\n\nChecking the box only captures NEW messages going forward — it does not scan old history.\nUse Bootstrap to backfill through the rate-limited queue.');
      return;
    }
    if (!confirm(`Bootstrap "${c.name}"?\n\nThis scans history rate-limited, queues media in the database, then downloads gradually. Safe for huge chats — may take hours/days.`)) return;
    e.target.disabled = true; e.target.textContent = '…';
    try {
      const res = await startBootstrap(c.id);
      alert(res.message || 'Bootstrap started — watch the Queue tab.');
    }
    catch (err) { alert(err.message); }
    finally { e.target.disabled = false; e.target.textContent = '⬇ Bootstrap'; }
  };

  return tr;
}

function escape(s) { return String(s ?? '').replace(/[&<>"]/g, ch => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;' }[ch])); }

function statusCount(byStatus, name) {
  const row = (byStatus || []).find(x => x.status === name);
  return row ? row.count : 0;
}

function chatName(chatId) {
  const c = allChats.find(x => x.id === chatId);
  return c ? c.name : `#${chatId}`;
}

// ─── Queue ────────────────────────────────────────────────────────────────────
async function refreshQueue() {
  try {
    if (!allChats.length) {
      try { allChats = await api('/api/chats'); } catch {}
    }
    const [stats, byChat, jobs, limits, failed, flood] = await Promise.all([
      api('/api/queue/stats'),
      api('/api/queue/stats/by-chat'),
      api('/api/bootstrap/jobs'),
      api('/api/settings/limits'),
      api('/api/queue/items?status=failed&limit=50'),
      api('/api/flood-wait'),
    ]);

    renderFloodBanner(flood);
    if (limits.thresholds) {
      LIMIT_THRESHOLDS.scannerCapacity = limits.thresholds.scannerCapacity;
      LIMIT_THRESHOLDS.scannerRefill   = limits.thresholds.scannerRefill;
      LIMIT_THRESHOLDS.downloadThreads = limits.thresholds.downloadThreads;
    }

    renderQueueCards(stats);
    renderBootstrapJobs(jobs);
    renderQueueByChat(byChat);
    renderFailedItems(failed);

    $('#q-capacity').value = limits.scannerApiCapacity ?? 5;
    $('#q-refill').value   = limits.scannerApiRefillPerSecond ?? 1;
    $('#q-threads').value  = limits.downloadThreads ?? 3;
    applyLimitWarnings(
      Number($('#q-capacity').value),
      Number($('#q-refill').value),
      Number($('#q-threads').value));
    applyParallelBootstrapUi(limits);
  } catch (e) { console.warn(e); }
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
      catch (err) { alert(err.message); e.target.disabled = false; }
    });
    body.appendChild(tr);
  });
}

function renderQueueByChat(rows) {
  const body = $('#queue-chat-table tbody');
  body.innerHTML = '';
  const sorted = [...rows].sort((a, b) => b.total - a.total);
  if (!sorted.length) {
    body.innerHTML = '<tr><td colspan="10" class="muted">No tracked media yet — bootstrap a chat to populate the queue.</td></tr>';
    return;
  }
  sorted.forEach(r => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escape(chatName(r.chatId))}</td>
      <td>${r.total}</td>
      <td>${statusCount(r.byStatus, 'pending')}</td>
      <td>${statusCount(r.byStatus, 'queued')}</td>
      <td>${statusCount(r.byStatus, 'in_progress')}</td>
      <td>${statusCount(r.byStatus, 'done')}</td>
      <td>${statusCount(r.byStatus, 'failed')}</td>
      <td>${statusCount(r.byStatus, 'skipped')}</td>
      <td>${fmtBytes(r.bytes)}</td>
      <td><button data-act="bootstrap" data-id="${r.chatId}">Bootstrap</button></td>`;
    tr.querySelector('[data-act="bootstrap"]').onclick = async (e) => {
      e.target.disabled = true; e.target.textContent = '…';
      try { await startBootstrap(r.chatId); refreshQueue(); }
      catch (err) { alert(err.message); }
      finally { e.target.disabled = false; e.target.textContent = 'Bootstrap'; }
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
      } catch (err) { alert(err.message); e.target.disabled = false; }
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
  if (danger && !confirm('These scanner settings are at or near maximum and may trigger Telegram rate limits or account restrictions. Save anyway?')) return;
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
    if ($('#q-parallel').checked && !confirm('Parallel bootstrap is now enabled. Multiple history scans can run at once (shared rate limit). Keep this on?')) {
      await api('/api/settings/limits', {
        method: 'POST',
        body: JSON.stringify({
          scannerApiCapacity: capacity,
          scannerApiRefillPerSecond: refill,
          downloadThreads: threads,
          allowParallelBootstrap: false,
          maxParallelBootstraps: Number($('#q-max-parallel').value) || 3,
        }),
      });
      $('#q-parallel').checked = false;
      $('#lbl-max-parallel').classList.add('hidden');
    }
    alert('Limits saved — active scans pick them up immediately.');
    refreshQueue();
  } catch (e) { alert(e.message); }
};

$('#btn-refresh-failed').onclick = () => refreshQueue();

// ─── Status ─────────────────────────────────────────────────────────────────
async function refreshStatus() {
  try {
    const [s, downloads] = await Promise.all([api('/api/status'), api('/api/downloads')]);
    $('#m-monitored').textContent = `${s.monitoredChats} / ${s.totalChats}`;
    $('#m-active'   ).textContent = s.activeDownloads;
    $('#m-folder'   ).textContent = s.downloadFolder || '—';
    const body = $('#dl-table tbody');
    body.innerHTML = '';
    downloads.forEach(d => {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${escape(d.chat)}</td>
        <td>${escape(d.file)}</td>
        <td>${escape(d.plugin || '')}</td>
        <td><span class="pill ${d.status.toLowerCase()}">${d.status}</span></td>
        <td><span class="bar"><span style="width:${d.percent}%"></span></span> ${d.percent}%</td>
        <td>${fmtBytes(d.bytesDone)} / ${fmtBytes(d.bytesTotal)}</td>
        <td class="error-cell">${escape(d.error || '')}</td>`;
      body.appendChild(tr);
    });
  } catch (e) { console.warn(e); }
}

// ─── Settings ───────────────────────────────────────────────────────────────
async function loadSettings() {
  try {
    const s = await api('/api/settings');
    $('#s-folder').value = s.downloadFolder || '';
    $('#s-threads').value = s.downloadThreads || 3;
    applySettingsThreadWarning(Number($('#s-threads').value));
  } catch (e) { console.warn(e); }
}
$('#s-threads')?.addEventListener('input', () => applySettingsThreadWarning(Number($('#s-threads').value)));
$('#btn-save-folder').onclick = async () => {
  try { await api('/api/settings/download-folder', { method: 'POST', body: JSON.stringify({ folder: $('#s-folder').value }) }); alert('Saved.'); }
  catch (e) { alert(e.message); }
};
$('#btn-save-threads').onclick = async () => {
  try { await api('/api/settings/threads', { method: 'POST', body: JSON.stringify({ threads: Number($('#s-threads').value) }) }); alert('Saved.'); }
  catch (e) { alert(e.message); }
};

// ─── Poller ─────────────────────────────────────────────────────────────────
async function poll() {
  try {
    const [ls, flood] = await Promise.all([api('/api/login/status'), api('/api/flood-wait')]);
    renderFloodBanner(flood);
    $('#conn').className = ls.loggedIn ? 'ok' : 'bad';
    if (ls.loggedIn) unlockTabs(true);
    if ($('#tab-status').classList.contains('active')) refreshStatus();
    if ($('#tab-queue').classList.contains('active')) refreshQueue();
  } catch { $('#conn').className = 'bad'; }
}

refreshLoginStatus();
setInterval(poll, 2500);
