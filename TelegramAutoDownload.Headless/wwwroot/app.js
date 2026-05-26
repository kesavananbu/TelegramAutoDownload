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
  if (!r.ok) {
    let msg = `HTTP ${r.status}`;
    try { msg = (await r.json()).error || msg; } catch {}
    throw new Error(msg);
  }
  return r.json();
}
function showError(el, msg) { el.textContent = msg || ''; el.classList.toggle('hidden', !msg); }
function fmtBytes(n) {
  if (!n) return '—';
  const u = ['B','KB','MB','GB','TB']; let i = 0;
  while (n >= 1024 && i < u.length-1) { n /= 1024; i++; }
  return `${n.toFixed(1)} ${u[i]}`;
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
    <td><button data-act="sync">⬇ Sync</button></td>
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

  tr.querySelector('[data-act="sync"]').onclick = async (e) => {
    if (!c.selected) { alert('Enable monitoring first.'); return; }
    e.target.disabled = true; e.target.textContent = '…';
    try { await api(`/api/chats/${c.id}/sync`, { method: 'POST' }); }
    catch (err) { alert(err.message); }
    finally { e.target.disabled = false; e.target.textContent = '⬇ Sync'; }
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
    const [stats, byChat, jobs, limits, failed] = await Promise.all([
      api('/api/queue/stats'),
      api('/api/queue/stats/by-chat'),
      api('/api/bootstrap/jobs'),
      api('/api/settings/limits'),
      api('/api/queue/items?status=failed&limit=50'),
    ]);

    renderQueueCards(stats);
    renderBootstrapJobs(jobs);
    renderQueueByChat(byChat);
    renderFailedItems(failed);

    $('#q-capacity').value = limits.scannerApiCapacity ?? 5;
    $('#q-refill').value   = limits.scannerApiRefillPerSecond ?? 1;
    $('#q-threads').value  = limits.downloadThreads ?? 3;
  } catch (e) { console.warn(e); }
}

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
      try { await api(`/api/chats/${r.chatId}/bootstrap`, { method: 'POST' }); refreshQueue(); }
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
  try {
    await api('/api/settings/limits', {
      method: 'POST',
      body: JSON.stringify({
        scannerApiCapacity: Number($('#q-capacity').value),
        scannerApiRefillPerSecond: Number($('#q-refill').value),
        downloadThreads: Number($('#q-threads').value),
      }),
    });
    alert('Limits saved — active scans pick them up immediately.');
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
  } catch (e) { console.warn(e); }
}
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
    const ls = await api('/api/login/status');
    $('#conn').className = ls.loggedIn ? 'ok' : 'bad';
    if (ls.loggedIn) unlockTabs(true);
    if ($('#tab-status').classList.contains('active')) refreshStatus();
    if ($('#tab-queue').classList.contains('active')) refreshQueue();
  } catch { $('#conn').className = 'bad'; }
}

refreshLoginStatus();
setInterval(poll, 2500);
