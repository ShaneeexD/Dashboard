'use strict';

(function(){
  const views = ['home','npcs','player','map','stats','settings'];
  const els = {
    navItems: Array.from(document.querySelectorAll('.nav-item')),
    viewTitle: document.getElementById('view-title'),
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

  function setActiveView(name){
    document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
    document.getElementById('view-' + name).classList.add('active');
    els.navItems.forEach(i => i.classList.toggle('active', i.getAttribute('data-view') === name));
    els.viewTitle.textContent = ({
      home: 'Overview',
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
      npcCache = await res.json();
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

  // Player view actions
  els.playerSpawn?.addEventListener('click', spawnSelectedPreset);

  // Click to open details in a new page
  els.npcList?.addEventListener('click', (e) => {
    const card = e.target.closest('.npc-card');
    if(!card) return;
    const id = Number(card.dataset.id);
    if(!Number.isFinite(id)) return;
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
})();
