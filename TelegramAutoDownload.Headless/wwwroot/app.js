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
  if (b.dataset.tab === 'logs')    openLogsTab();
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
    <td><button data-act="test-download" title="Download newest 10 media as probe">🧪 Test 10</button></td>
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

  tr.querySelector('[data-act="test-download"]').onclick = async (e) => {
    if (!confirm(`Test download for "${c.name}"?\n\nFinds the newest 10 media messages, validates settings, and attempts a real download for each.\n\nNothing is written to the queue database. Check Logs tab for [TestDownload] lines.`))
      return;
    e.target.disabled = true; e.target.textContent = '…';
    try {
      const report = await api(`/api/chats/${c.id}/test-download?limit=10`, { method: 'POST' });
      showTestDownloadReport(report);
    } catch (err) { alert(err.message); }
    finally { e.target.disabled = false; e.target.textContent = '🧪 Test 10'; }
  };

  return tr;
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
      ? '<span style="color:#1a7f4c">Ready for bootstrap</span>'
      : '<span style="color:#a22">Fix issues before bootstrap</span>');

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
    alert('No failed items to retry.');
    return;
  }
  const scope = chatId ? `"${chatName(chatId)}"` : 'all chats';
  if (!confirm(`Re-queue ${count} failed item(s) for ${scope}?\n\nThe download orchestrator will pick them up automatically.`))
    return;
  const url = chatId
    ? `/api/queue/retry-failed?chatId=${chatId}`
    : '/api/queue/retry-failed';
  const res = await api(url, { method: 'POST' });
  alert(`Re-queued ${res.requeued} item(s).`);
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
    alert('No failed items to delete.');
    return;
  }
  const scope = chatId ? `"${chatName(chatId)}"` : 'all chats';
  if (!confirm(`Permanently delete ${count} failed record(s) for ${scope}?\n\nThis removes them from the queue database only — files already on disk are not deleted.`))
    return;
  const url = chatId
    ? `/api/queue/delete-failed?chatId=${chatId}`
    : '/api/queue/delete-failed';
  const res = await api(url, { method: 'POST' });
  alert(`Deleted ${res.deleted} failed record(s).`);
  refreshQueue();
}

async function clearChatQueue(chatId) {
  const name = chatName(chatId);
  const stats = (await api('/api/queue/stats/by-chat')).find(r => r.chatId === chatId);
  const total = stats?.total ?? 0;
  if (!total) {
    alert(`No queue records for "${name}".`);
    return;
  }
  if (!confirm(`Clear all ${total} queue record(s) for "${name}"?\n\nThis removes pending/queued/done/failed rows from the database. Downloaded files on disk are NOT deleted. Bootstrap history watermark is reset.`))
    return;
  const res = await api(`/api/queue/clear?chatId=${chatId}`, { method: 'POST' });
  alert(`Cleared ${res.deleted} record(s) for "${name}".`);
  refreshQueue();
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
    applyDownloadsPausedUi(!!limits.downloadsPaused);
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
      <td class="actions-cell">
        <button data-act="bootstrap" data-id="${r.chatId}">Bootstrap</button>
        ${r.total ? `<button data-act="clear-queue" data-id="${r.chatId}">Clear queue</button>` : ''}
        ${statusCount(r.byStatus, 'failed') ? `<button data-act="retry-failed" data-id="${r.chatId}">Retry failed</button>` : ''}
      </td>`;
    tr.querySelector('[data-act="bootstrap"]').onclick = async (e) => {
      e.target.disabled = true; e.target.textContent = '…';
      try { await startBootstrap(r.chatId); refreshQueue(); }
      catch (err) { alert(err.message); }
      finally { e.target.disabled = false; e.target.textContent = 'Bootstrap'; }
    };
    tr.querySelector('[data-act="clear-queue"]')?.addEventListener('click', async (e) => {
      e.target.disabled = true;
      try { await clearChatQueue(r.chatId); }
      catch (err) { alert(err.message); }
      finally { e.target.disabled = false; }
    });
    tr.querySelector('[data-act="retry-failed"]')?.addEventListener('click', async (e) => {
      e.target.disabled = true;
      try { await retryAllFailed(r.chatId); }
      catch (err) { alert(err.message); }
      finally { e.target.disabled = false; }
    });
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
$('#btn-retry-all-failed').onclick = async () => {
  const btn = $('#btn-retry-all-failed');
  btn.disabled = true;
  try { await retryAllFailed(); }
  catch (err) { alert(err.message); }
  finally { btn.disabled = false; }
};
$('#btn-delete-all-failed').onclick = async () => {
  const btn = $('#btn-delete-all-failed');
  btn.disabled = true;
  try { await deleteAllFailed(); }
  catch (err) { alert(err.message); }
  finally { btn.disabled = false; }
};
$('#btn-toggle-downloads').onclick = async () => {
  const btn = $('#btn-toggle-downloads');
  const paused = btn.textContent.startsWith('Pause');
  if (paused && !confirm('Pause downloads?\n\nQueued items stay in the database. In-progress items move back to queued.'))
    return;
  btn.disabled = true;
  try {
    await setDownloadsPaused(paused);
    refreshQueue();
  } catch (err) { alert(err.message); }
  finally { btn.disabled = false; }
};

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
    if ($('#s-folder-layout')) $('#s-folder-layout').value = s.folderLayout || 'TypeFirst';
    $('#s-threads').value = s.downloadThreads || 3;
    applySettingsThreadWarning(Number($('#s-threads').value));
  } catch (e) { console.warn(e); }
}
$('#s-threads')?.addEventListener('input', () => applySettingsThreadWarning(Number($('#s-threads').value)));
$('#btn-save-folder').onclick = async () => {
  try { await api('/api/settings/download-folder', { method: 'POST', body: JSON.stringify({ folder: $('#s-folder').value }) }); alert('Saved.'); }
  catch (e) { alert(e.message); }
};
$('#btn-save-folder-layout')?.addEventListener('click', async () => {
  try {
    await api('/api/settings/folder-layout', { method: 'POST', body: JSON.stringify({ layout: $('#s-folder-layout').value }) });
    alert('Folder layout saved. New downloads use the selected structure.');
  } catch (e) { alert(e.message); }
});
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
  if (!q) { alert('Enter filter text to search the full file (server-side scan).'); return; }
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
  } catch (e) { console.warn(e); $('#log-meta').textContent = e.message; }
}

$('#btn-log-refresh').onclick = () => refreshLogTail().catch(e => alert(e.message));
$('#btn-log-search').onclick = () => searchLogs().catch(e => alert(e.message));
$('#log-file').onchange = () => refreshLogTail().catch(e => alert(e.message));
$('#log-level').onchange = () => refreshLogTail().catch(e => alert(e.message));
$('#log-filter').onkeydown = (e) => { if (e.key === 'Enter') refreshLogTail().catch(er => alert(er.message)); };
$('#log-follow').onchange = () => {
  if ($('#log-follow').checked && !logSearchMode) startLogStream($('#log-file').value);
  else stopLogStream();
};
$('#btn-log-clear').onclick = () => { logBuffer = []; renderLogView(); };
