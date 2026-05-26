'use strict';
const $  = (q, r=document) => r.querySelector(q);
const $$ = (q, r=document) => Array.from(r.querySelectorAll(q));

// ─── Tabs ────────────────────────────────────────────────────────────────────
$$('header nav button').forEach(b => b.addEventListener('click', () => {
  if (b.disabled) return;
  $$('header nav button').forEach(x => x.classList.toggle('active', x === b));
  $$('.tab').forEach(t => t.classList.toggle('active', t.id === `tab-${b.dataset.tab}`));
  if (b.dataset.tab === 'chats')    loadChats();
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
  } catch { $('#conn').className = 'bad'; }
}

refreshLoginStatus();
setInterval(poll, 2500);
