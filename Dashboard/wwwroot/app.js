'use strict';

(function(){
  const views = ['home','console','npcs','player','map','stats','settings'];
  const els = {
    navItems: Array.from(document.querySelectorAll('.nav-item')),
    viewTitle: document.getElementById('view-title'),
    themeBlue: document.getElementById('theme-blue'),
    themeRed: document.getElementById('theme-red'),
    themeGreen: document.getElementById('theme-green'),
    themeYellow: document.getElementById('theme-yellow'),
    themePurple: document.getElementById('theme-purple'),
    themeCyan: document.getElementById('theme-cyan'),
    themePink: document.getElementById('theme-pink'),
    themeColorBlind: document.getElementById('theme-colorblind'),
    themeCustom: document.getElementById('theme-custom'),
    customColor: document.getElementById('custom-color'),
    // Console
    logsOutput: document.getElementById('logs-output'),
    logsRefresh: document.getElementById('logs-refresh'),
    logsLive: document.getElementById('logs-live'),
    logsTail: document.getElementById('logs-tail'),
    logsPath: document.getElementById('logs-path'),
    logsFilter: document.getElementById('logs-filter'),
    logsAutoscroll: document.getElementById('logs-autoscroll'),
    healthDot: document.getElementById('health-dot'),
    healthText: document.getElementById('health-text'),
    serverStatus: document.getElementById('server-status'),
    serverPort: document.getElementById('server-port'),
    serverTime: document.getElementById('server-time'),
    gameSave: document.getElementById('game-save'),
    gameCity: document.getElementById('game-city'),
    gameTime: document.getElementById('game-time'),
    gameMo: document.getElementById('game-mo'),
    refreshBtn: document.getElementById('refresh-btn'),
    npcList: document.getElementById('npc-list'),
    npcSearch: document.getElementById('npc-search'),
    npcRefresh: document.getElementById('npc-refresh'),
    npcDetails: document.getElementById('npc-details'),
    npcDetPhoto: document.getElementById('npc-det-photo'),
    npcDetName: document.getElementById('npc-det-name'),
    npcDetSurname: document.getElementById('npc-det-surname'),
    npcDetHpFill: document.getElementById('npc-det-hpfill'),
    // Player view
    playerSearch: document.getElementById('player-search'),
    playerPreset: document.getElementById('player-preset'),
    playerSpawn: document.getElementById('player-spawn'),
    playerStatus: document.getElementById('player-status'),
    // Player bars
    pHealthFill: document.getElementById('player-health-fill'),
    pHealthText: document.getElementById('player-health-text'),
    pHunFill: document.getElementById('player-hunger-fill'),
    pHunText: document.getElementById('player-hunger-text'),
    pThFill: document.getElementById('player-thirst-fill'),
    pThText: document.getElementById('player-thirst-text'),
    pTiFill: document.getElementById('player-tiredness-fill'),
    pTiText: document.getElementById('player-tiredness-text'),
    pEnFill: document.getElementById('player-energy-fill'),
    pEnText: document.getElementById('player-energy-text'),
    pSkFill: document.getElementById('player-stinky-fill'),
    pSkText: document.getElementById('player-stinky-text'),
    pCoFill: document.getElementById('player-cold-fill'),
    pCoText: document.getElementById('player-cold-text'),
    pWeFill: document.getElementById('player-wet-fill'),
    pWeText: document.getElementById('player-wet-text'),
    pHeFill: document.getElementById('player-headache-fill'),
    pHeText: document.getElementById('player-headache-text'),
    pBrFill: document.getElementById('player-bruised-fill'),
    pBrText: document.getElementById('player-bruised-text'),
    pBlFill: document.getElementById('player-bleeding-fill'),
    pBlText: document.getElementById('player-bleeding-text')
  };
  let playerPresetsLoaded = false;
  let playerPresetMaster = [];
  let npcCache = [];
  let selectedNpcId = null;
  let playerStatusTimer = null;
  let lastNpcJson = '';
  let logsTimer = null;
  let rawLogs = '';

  // Theme handling
  const THEME_KEY = 'sod_theme'; // 'blue' | 'red' | 'green' | 'yellow' | 'purple' | 'cyan' | 'pink' | 'colorblind' | 'custom'
  const THEME_CUSTOM_KEY = 'sod_theme_custom';
  const THEME_MAP = {
    blue: '',
    red: 'theme-red',
    green: 'theme-green',
    yellow: 'theme-yellow',
    purple: 'theme-purple',
    cyan: 'theme-cyan',
    pink: 'theme-pink',
    colorblind: 'theme-colorblind',
    custom: ''
  };
  function applyTheme(name){
    const root = document.documentElement;
    // Remove all theme classes, then add the selected one (if any)
    for(const cls of Object.values(THEME_MAP)){
      if(!cls) continue;
      root.classList.remove(cls);
    }
    const cls = THEME_MAP[name] || '';
    if(cls) root.classList.add(cls);
    // Reset any custom inline overrides when switching away from custom
    if(name !== 'custom'){
      clearCustomVars();
    } else {
      applyCustomFromStorage();
    }
  }
  function loadTheme(){
    const v = localStorage.getItem(THEME_KEY);
    return (v && (v in THEME_MAP)) ? v : 'blue';
  }
  function saveTheme(name){
    localStorage.setItem(THEME_KEY, name);
  }
  function hexToRgb(hex){
    const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex || '');
    if(!m) return {r:0,g:179,b:255};
    return { r: parseInt(m[1],16), g: parseInt(m[2],16), b: parseInt(m[3],16) };
  }
  function setCustomVars(hex){
    const {r,g,b} = hexToRgb(hex);
    const root = document.documentElement.style;
    const rgba12 = `rgba(${r}, ${g}, ${b}, 0.12)`;
    const rgba06 = `rgba(${r}, ${g}, ${b}, 0.06)`;
    root.setProperty('--neon-blue', hex);
    root.setProperty('--neon-blue-2', hex);
    root.setProperty('--accent', hex);
    root.setProperty('--accent-2', hex);
    root.setProperty('--bg-tint', rgba12);
    root.setProperty('--scan-tint', rgba06);
  }
  function clearCustomVars(){
    const root = document.documentElement.style;
    ['--neon-blue','--neon-blue-2','--accent','--accent-2','--bg-tint','--scan-tint'].forEach(v=>root.removeProperty(v));
  }
  function applyCustomFromStorage(){
    const hex = localStorage.getItem(THEME_CUSTOM_KEY) || '#00b3ff';
    if(els.customColor) els.customColor.value = hex;
    setCustomVars(hex);
  }
  // Initialize theme immediately
  const initialTheme = loadTheme();
  applyTheme(initialTheme);
  // Reflect in Settings radios if present
  if(els.themeBlue) els.themeBlue.checked = initialTheme === 'blue';
  els.themeRed && (els.themeRed.checked = initialTheme === 'red');
  els.themeGreen && (els.themeGreen.checked = initialTheme === 'green');
  els.themeYellow && (els.themeYellow.checked = initialTheme === 'yellow');
  els.themePurple && (els.themePurple.checked = initialTheme === 'purple');
  els.themeCyan && (els.themeCyan.checked = initialTheme === 'cyan');
  els.themePink && (els.themePink.checked = initialTheme === 'pink');
  els.themeColorBlind && (els.themeColorBlind.checked = initialTheme === 'colorblind');
  els.themeCustom && (els.themeCustom.checked = initialTheme === 'custom');
  if(initialTheme === 'custom') applyCustomFromStorage();
  // Wire events
  function wireTheme(id, name){
    const el = document.getElementById(id);
    el?.addEventListener('change', (e)=>{ if(e.target.checked){ applyTheme(name); saveTheme(name); }});
  }
  wireTheme('theme-blue','blue');
  wireTheme('theme-red','red');
  wireTheme('theme-green','green');
  wireTheme('theme-yellow','yellow');
  wireTheme('theme-purple','purple');
  wireTheme('theme-cyan','cyan');
  wireTheme('theme-pink','pink');
  wireTheme('theme-colorblind','colorblind');
  // Custom theme interactions
  document.getElementById('theme-custom')?.addEventListener('change', (e)=>{
    if(e.target.checked){
      const hex = (els.customColor?.value)||'#00b3ff';
      localStorage.setItem(THEME_CUSTOM_KEY, hex);
      saveTheme('custom');
      applyTheme('custom');
    }
  });
  els.customColor?.addEventListener('input', (e)=>{
    const hex = e.target.value;
    localStorage.setItem(THEME_CUSTOM_KEY, hex);
    if(document.getElementById('theme-custom')?.checked){
      setCustomVars(hex);
    }
  });

  // Noir background: update CSS vars based on mouse position (0..1)
  (function(){
    let rafPending = false;
    let mx = 0.5, my = 0.5;
    function applyVars(){
      document.documentElement.style.setProperty('--mouse-x', mx.toFixed(3));
      document.documentElement.style.setProperty('--mouse-y', my.toFixed(3));
    }
    window.addEventListener('mousemove', (e) => {
      const w = window.innerWidth || 1;
      const h = window.innerHeight || 1;
      mx = Math.max(0, Math.min(1, e.clientX / w));
      my = Math.max(0, Math.min(1, e.clientY / h));
      if (!rafPending) {
        rafPending = true;
        requestAnimationFrame(() => { rafPending = false; applyVars(); });
      }
    });
    window.addEventListener('mouseleave', () => { mx = 0.5; my = 0.5; applyVars(); });
    applyVars();
  })();

  function setActiveView(name){
    document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
    document.getElementById('view-' + name).classList.add('active');
    els.navItems.forEach(i => i.classList.toggle('active', i.getAttribute('data-view') === name));
    els.viewTitle.textContent = ({
      home: 'Overview',
      console: 'Console',
      npcs: 'NPCs',
      player: 'Player',
      map: 'Map',
      stats: 'Stats',
      settings: 'Settings'
    })[name] || 'Overview';

    if(name === 'npcs'){
      // lazy load when entering NPCs view
      if(npcCache.length === 0) fetchNpcs();
    }
    if(name === 'player'){
      if(!playerPresetsLoaded) fetchPlayerPresets();
      startPlayerStatusPolling();
    }
    if(name === 'console'){
      fetchLogs();
      startLogsPolling();
    } else {
      stopLogsPolling();
    }
  }

  els.navItems.forEach(i => i.addEventListener('click', e => {
    e.preventDefault();
    const v = i.getAttribute('data-view');
    if(views.includes(v)) {
      // Update URL without reloading the page
      const url = new URL(window.location);
      url.searchParams.set('view', v);
      window.history.pushState({}, '', url);
      setActiveView(v);
    }
  }));

  function setPlayerStatus(message, tone='info'){
    if(!els.playerStatus) return;
    els.playerStatus.textContent = message;
    els.playerStatus.classList.remove('dev-status-info','dev-status-ok','dev-status-error');
    const cls = tone === 'ok' ? 'dev-status-ok' : tone === 'error' ? 'dev-status-error' : 'dev-status-info';
    els.playerStatus.classList.add(cls);
  }

  async function fetchPlayerPresets(){
    try{
      setPlayerStatus('Loading presets…','info');
      let list = [];
      // Try live API first
      try{
        const r1 = await fetch('/api/player/presets', { cache: 'no-store' });
        if(r1.ok){
          const arr = await r1.json();
          if(Array.isArray(arr) && arr.length){
            list = arr;
          }
        }
      }catch{}
      // Fallback to local JSON
      if(!list.length){
        const r2 = await fetch('./presets.json', { cache: 'no-store' });
        if(r2.ok){
          const data = await r2.json();
          list = Array.isArray(data?.InteractablePreset) ? data.InteractablePreset : [];
        }
      }
      playerPresetMaster = list.slice();
      renderPlayerPresets();
      playerPresetsLoaded = true;
      setPlayerStatus(list.length ? 'Presets loaded.' : 'No presets found.', list.length ? 'ok' : 'error');
    }catch(err){
      setPlayerStatus('Failed to load presets.','error');
    }
  }

  // Render the presets dropdown filtered by the search query
  function renderPlayerPresets(){
    if(!els.playerPreset) return;
    const q = (els.playerSearch?.value || '').trim().toLowerCase();
    const filtered = q ? playerPresetMaster.filter(n => n.toLowerCase().includes(q)) : playerPresetMaster;
    const prev = els.playerPreset.value;
    els.playerPreset.innerHTML = '';
    for(const name of filtered){
      const opt = document.createElement('option');
      opt.value = name;
      opt.textContent = name;
      els.playerPreset.appendChild(opt);
    }
    // restore previous selection if still present, else first item
    if(prev && filtered.includes(prev)){
      els.playerPreset.value = prev;
    } else if(filtered.length){
      els.playerPreset.value = filtered[0];
    }
  }

  async function spawnSelectedPreset(){
    const name = els.playerPreset?.value;
    if(!name){ setPlayerStatus('Please select a preset.','error'); return; }
    try{
      setPlayerStatus('Spawning…','info');
      const res = await fetch('/api/player/spawn-item?preset=' + encodeURIComponent(name), { method:'POST', cache:'no-store' });
      const data = await res.json().catch(()=>({}));
      if(!res.ok || data.success === false){
        setPlayerStatus(data.message || ('Failed ('+res.status+')'), 'error');
      }else{
        setPlayerStatus(data.message || 'Spawned to inventory.', 'ok');
      }
    }catch(err){
      setPlayerStatus('Failed: ' + (err?.message || err), 'error');
    }
  }

  // spawnDefaultInventory removed (unsafe)

  function startPlayerStatusPolling(){
    stopPlayerStatusPolling();
    fetchPlayerStatus();
    playerStatusTimer = setInterval(() => {
      const active = document.getElementById('view-player')?.classList.contains('active');
      if(active) fetchPlayerStatus(); else stopPlayerStatusPolling();
    }, 1000);
  }

  function stopPlayerStatusPolling(){
    if(playerStatusTimer){ clearInterval(playerStatusTimer); playerStatusTimer = null; }
  }

  async function fetchPlayerStatus(){
    try{
      const res = await fetch('/api/player/status', { cache:'no-store' });
      if(!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      if(!data || data.ok === false) throw new Error(data?.message || 'Unavailable');
      renderPlayerStatus(data);
    }catch(err){
      // Do not spam status line; silently ignore transient errors
    }
  }

  function pct100(x){ return Math.max(0, Math.min(100, Math.round((x||0)*100))); }
  function setBar(fillEl, textEl, value, label){
    const p = pct100(value);
    if(fillEl) fillEl.style.width = p + '%';
    if(textEl) textEl.textContent = (p === 0 ? '' : label);
    const row = fillEl ? fillEl.closest('.form-row') : null;
    if(row) row.classList.toggle('is-zero', p === 0);
  }

  function renderPlayerStatus(d){
    const hCur = Number(d?.health?.current)||0;
    const hMax = Number(d?.health?.max)||0;
    const hpPct = hMax > 0 ? Math.max(0, Math.min(100, Math.round((hCur/hMax)*100))) : 0;
    setBar(els.pHealthFill, els.pHealthText, hpPct/100, `${Math.round(hCur*100)}/${Math.round(hMax*100)} HP (${hpPct}%)`);

    const needs = d?.needs||{};
    setBar(els.pHunFill, els.pHunText, needs.hunger||0, `${pct100(needs.hunger||0)}%`);
    setBar(els.pThFill, els.pThText, needs.thirst||0, `${pct100(needs.thirst||0)}%`);
    setBar(els.pTiFill, els.pTiText, needs.tiredness||0, `${pct100(needs.tiredness||0)}%`);
    setBar(els.pEnFill, els.pEnText, needs.energy||0, `${pct100(needs.energy||0)}%`);

    const st = d?.status||{};
    setBar(els.pSkFill, els.pSkText, st.stinky||0, `${pct100(st.stinky||0)}%`);
    setBar(els.pCoFill, els.pCoText, st.cold||0, `${pct100(st.cold||0)}%`);
    setBar(els.pWeFill, els.pWeText, st.wet||0, `${pct100(st.wet||0)}%`);
    setBar(els.pHeFill, els.pHeText, st.headache||0, `${pct100(st.headache||0)}%`);
    setBar(els.pBrFill, els.pBrText, st.bruised||0, `${pct100(st.bruised||0)}%`);
    setBar(els.pBlFill, els.pBlText, st.bleeding||0, `${pct100(st.bleeding||0)}%`);
  }

  async function fetchHealth(){
    try{
      const res = await fetch('/api/health', { cache: 'no-store' });
      if(!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      els.healthDot.style.background = '#11d67a';
      els.healthText.textContent = 'Online';
      els.healthText.className = 'status-text status-online';
      els.serverStatus.textContent = 'Online';
      els.serverStatus.className = 'v status-online';
      els.serverPort.textContent = data.port ?? '—';
      els.serverTime.textContent = new Date(data.time || Date.now()).toLocaleString();
    }catch(err){
      els.healthDot.style.background = '#e05555';
      els.healthText.textContent = 'Offline';
      els.healthText.className = 'status-text status-offline';
      els.serverStatus.textContent = 'Offline';
      els.serverStatus.className = 'v status-offline';
    }
  }

  // Fetch current save name
  async function fetchGame(){
    try{
      const res = await fetch('/api/game', { cache: 'no-store' });
      if(!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      if(els.gameSave) els.gameSave.textContent = data.saveName || '—';
      if(els.gameCity) els.gameCity.textContent = data.cityName || '—';
      if(els.gameTime) els.gameTime.textContent = data.timeText || '—';
      if(els.gameMo) els.gameMo.textContent = data.murderMO || '—';
    }catch(err){
      if(els.gameSave) els.gameSave.textContent = '—';
      if(els.gameCity) els.gameCity.textContent = '—';
      if(els.gameTime) els.gameTime.textContent = '—';
      if(els.gameMo) els.gameMo.textContent = '—';
    }
  }

  els.refreshBtn.addEventListener('click', () => { fetchHealth(); fetchGame(); });

  // NPCs
  async function fetchNpcs(){
    try{
      const res = await fetch('/api/npcs', { cache: 'no-store' });
      if(!res.ok) throw new Error('HTTP ' + res.status);
      const arr = await res.json();
      // Avoid unnecessary DOM re-renders if data hasn't changed
      const snapshot = JSON.stringify(arr);
      if (snapshot === lastNpcJson) return;
      lastNpcJson = snapshot;
      npcCache = arr;
      renderNpcs();
    }catch(err){
      if(els.npcList){
        els.npcList.innerHTML = '<div class="placeholder">Failed to load NPCs</div>';
      }
    }
  }

  function renderNpcs(){
    if(!els.npcList) return;
    const q = (els.npcSearch?.value || '').trim().toLowerCase();
    const data = npcCache.filter(n => !q || (n.name?.toLowerCase().includes(q) || n.surname?.toLowerCase().includes(q)));

    if(data.length === 0){
      els.npcList.innerHTML = '<div class="placeholder">No NPCs found.</div>';
      return;
    }

    const html = data.map(n => `
      <div class="npc-card clickable" data-id="${n.id}" title="${escapeHtml(n.name || '')}">
        <div class="npc-photo">
          <img class="npc-img" loading="lazy" src="${n.photo || 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottQAAAABJRU5ErkJggg=='}" alt="${escapeHtml(n.name || '')}"/>
        </div>
        <div class="npc-meta">
          <div class="npc-name">${escapeHtml(n.name || '')}</div>
          <div class="npc-hptext ${hpClass(n)}">${hpText(n)}</div>
          <div class="npc-hp"><div class="npc-hp-fill" style="width:${hpPct(n)}%"></div><div class="npc-hp-label">${n.isDead ? 'DEAD' : ''}</div></div>
          ${koMiniHtml(n)}
        </div>
      </div>
    `).join('');
    els.npcList.innerHTML = html;
    // Restore selection highlight after re-render
    if(selectedNpcId != null){
      const selCard = els.npcList.querySelector(`.npc-card[data-id="${selectedNpcId}"]`);
      if(selCard) selCard.classList.add('selected');
      updateDetailsIfSelected();
    }
  }

  function hpPct(n){
    if (n.isDead) return 0;
    const cur = Number(n.hpCurrent)||0;
    const max = Number(n.hpMax)||0;
    if(max <= 0) return 0;
    const pct = Math.max(0, Math.min(100, (cur/max)*100));
    return pct.toFixed(0);
  }
  
  function hpText(n){
    if (n.isDead) return 'Dead';
    const cur = Number(n.hpCurrent)||0;
    const max = Number(n.hpMax)||0;
    // Scale by 100 to show 0-100 range
    return `${Math.round(cur * 100)}/${Math.round(max * 100)} HP`;
  }
  
  function hpClass(n){
    if (n.isDead) return 'hp-dead';
    const p = Number(hpPct(n));
    return p >= 66 ? 'hp-high' : p >= 33 ? 'hp-med' : 'hp-low';
  }

  function koMiniHtml(n){
    if (!n.isKo) return '';
    const total = Number(n.koTotalSeconds)||0;
    const remain = Number(n.koRemainingSeconds)||0;
    if (total <= 0) return '';
    const pct = Math.max(0, Math.min(100, (remain/total)*100)).toFixed(0);
    return `<div class="npc-ko-mini"><div class="npc-ko-fill-mini" style="width:${pct}%"></div></div>`;
  }

  function escapeHtml(s){
    return (s||'').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;'}[c]));
  }

  els.npcRefresh?.addEventListener('click', fetchNpcs);
  els.npcSearch?.addEventListener('input', () => renderNpcs());
  els.playerSearch?.addEventListener('input', () => renderPlayerPresets());
  // Console events
  els.logsRefresh?.addEventListener('click', fetchLogs);
  els.logsLive?.addEventListener('change', () => {
    const isOnConsole = document.getElementById('view-console')?.classList.contains('active');
    if(isOnConsole){ els.logsLive.checked ? startLogsPolling() : stopLogsPolling(); }
  });
  els.logsTail?.addEventListener('change', () => fetchLogs());
  els.logsFilter?.addEventListener('input', () => renderLogs());
  els.logsAutoscroll?.addEventListener('change', () => {
    if(els.logsAutoscroll.checked){
      // If turning on, snap to bottom if on console
      const isOnConsole = document.getElementById('view-console')?.classList.contains('active');
      if(isOnConsole){
        els.logsOutput.scrollTop = els.logsOutput.scrollHeight;
      }
    }
  });
  els.logsOutput?.addEventListener('scroll', () => {
    // If user scrolls away from bottom, pause autoscroll automatically
    const nearBottom = (els.logsOutput.scrollHeight - els.logsOutput.scrollTop - els.logsOutput.clientHeight) < 20;
    if(!nearBottom && els.logsAutoscroll && els.logsAutoscroll.checked){
      els.logsAutoscroll.checked = false;
    }
  });

  // Player view actions
  els.playerSpawn?.addEventListener('click', spawnSelectedPreset);

  // Click to open details in a new page
  els.npcList?.addEventListener('click', (e) => {
    const card = e.target.closest('.npc-card');
    if(!card) return;
    const id = Number(card.dataset.id);
    if(!Number.isFinite(id)) return;
    // optional small pulse before navigation
    try{
      card.classList.add('clicked');
      setTimeout(() => card.classList.remove('clicked'), 220);
    }catch{}
    // Navigate to dedicated NPC page
    window.location.href = `./npc.html?id=${id}`;
  });

  // Check URL for view parameter
  function getInitialView() {
    const params = new URLSearchParams(window.location.search);
    const view = params.get('view');
    return views.includes(view) ? view : 'home';
  }
  
  // initial
  setActiveView(getInitialView());
  fetchHealth();
  fetchGame();
  setInterval(fetchHealth, 2000);
  setInterval(fetchGame, 2000);
  // Poll NPCs periodically when on NPCs view
  setInterval(() => {
    const isOnNpcs = document.getElementById('view-npcs')?.classList.contains('active');
    if(isOnNpcs) fetchNpcs();
  }, 2000);

  // Logs polling helpers
  async function fetchLogs(){
    if(!els.logsOutput) return;
    try{
      const tail = Number(els.logsTail?.value || 500) || 500;
      const res = await fetch(`/api/logs?tail=${tail}`, { cache: 'no-store' });
      if(!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      if(els.logsPath) els.logsPath.textContent = data.exists ? data.path : 'Log not found';
      rawLogs = data.content || '';
      renderLogs();
    }catch(err){
      if(els.logsOutput) els.logsOutput.textContent = 'Failed to read logs';
    }
  }

  function startLogsPolling(){
    stopLogsPolling();
    if(els.logsLive && els.logsLive.checked){
      logsTimer = setInterval(() => {
        const isOnConsole = document.getElementById('view-console')?.classList.contains('active');
        if(isOnConsole) fetchLogs();
      }, 1500);
    }
  }
  function stopLogsPolling(){
    if(logsTimer){ clearInterval(logsTimer); logsTimer = null; }
  }

  function escapeHtml(s){
    return (s||'').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;'}[c]));
  }

  function getFilterPredicate(){
    const q = (els.logsFilter?.value || '').trim();
    if(!q) return () => true;
    if(q.length >= 2 && q.startsWith('/') && q.endsWith('/')){
      try{ const re = new RegExp(q.slice(1,-1), 'i'); return (line) => re.test(line); }catch{ /* ignore */ }
    }
    const low = q.toLowerCase();
    return (line) => line.toLowerCase().includes(low);
  }

  const tsRegexes = [
    /^\[?(\d{4}[-\/]\d{2}[-\/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?)\]?\s?-?\s?/,
    /^\[?(\d{2}:\d{2}:\d{2})\]?\s?-?\s?/
  ];

  function parseLine(line){
    let ts = '';
    let rest = line;
    for(const re of tsRegexes){
      const m = rest.match(re);
      if(m){ ts = m[1]; rest = rest.slice(m[0].length); break; }
    }
    // Level detection (simple heuristics)
    const low = rest.toLowerCase();
    let level = 'info';
    if(low.includes('exception') || low.includes('error') || low.includes('fail')) level = 'error';
    else if(low.includes('warn')) level = 'warn';
    return { ts, level, msg: rest };
  }

  function renderLogs(){
    if(!els.logsOutput) return;
    const wasNearBottom = (els.logsOutput.scrollHeight - els.logsOutput.scrollTop - els.logsOutput.clientHeight) < 60;
    const pred = getFilterPredicate();
    const lines = (rawLogs || '').split('\n');
    const out = [];
    for(let i=0;i<lines.length;i++){
      const original = lines[i];
      if(!pred(original)) continue;
      const { ts, level, msg } = parseLine(original);
      const cls = level === 'error' ? 'level-error' : (level === 'warn' ? 'level-warn' : 'level-info');
      out.push(`<div class=\"log-line ${cls}\">`
        + `<span class=\"log-level\">${level}</span>`
        + `<span class=\"log-msg\">${escapeHtml(msg)}</span>`
        + (ts ? `<span class=\"log-ts\">${escapeHtml(ts)}</span>` : ``)
        + `</div>`);
    }
    els.logsOutput.innerHTML = out.length ? out.join('') : '<div class="log-line"><span class="log-msg">No log lines</span></div>';
    if(els.logsAutoscroll && els.logsAutoscroll.checked && wasNearBottom){
      els.logsOutput.scrollTop = els.logsOutput.scrollHeight;
    }
  }
})();
