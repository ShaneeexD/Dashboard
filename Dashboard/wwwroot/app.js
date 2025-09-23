'use strict';

(function(){
  const views = ['home','npcs','map','stats','settings'];
  const els = {
    navItems: Array.from(document.querySelectorAll('.nav-item')),
    viewTitle: document.getElementById('view-title'),
    healthDot: document.getElementById('health-dot'),
    healthText: document.getElementById('health-text'),
    serverStatus: document.getElementById('server-status'),
    serverPort: document.getElementById('server-port'),
    serverTime: document.getElementById('server-time'),
    gameSave: document.getElementById('game-save'),
    refreshBtn: document.getElementById('refresh-btn'),
    npcList: document.getElementById('npc-list'),
    npcSearch: document.getElementById('npc-search'),
    npcRefresh: document.getElementById('npc-refresh'),
    npcDetails: document.getElementById('npc-details'),
    npcDetPhoto: document.getElementById('npc-det-photo'),
    npcDetName: document.getElementById('npc-det-name'),
    npcDetSurname: document.getElementById('npc-det-surname'),
    npcDetHpFill: document.getElementById('npc-det-hpfill')
  };
  let npcCache = [];
  let selectedNpcId = null;

  function setActiveView(name){
    document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
    document.getElementById('view-' + name).classList.add('active');
    els.navItems.forEach(i => i.classList.toggle('active', i.getAttribute('data-view') === name));
    els.viewTitle.textContent = ({
      home: 'Overview',
      npcs: 'NPCs',
      map: 'Map',
      stats: 'Stats',
      settings: 'Settings'
    })[name] || 'Overview';

    if(name === 'npcs'){
      // lazy load when entering NPCs view
      if(npcCache.length === 0) fetchNpcs();
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
    }catch(err){
      if(els.gameSave) els.gameSave.textContent = '—';
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
          <div class="npc-hp"><div class="npc-hp-fill" style="width:${hpPct(n)}%"></div></div>
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
    const cur = Number(n.hpCurrent)||0;
    const max = Number(n.hpMax)||0;
    if(max <= 0) return 0;
    const pct = Math.max(0, Math.min(100, (cur/max)*100));
    return pct.toFixed(0);
  }
  
  function hpText(n){
    const cur = Number(n.hpCurrent)||0;
    const max = Number(n.hpMax)||0;
    // Scale by 100 to show 0-100 range
    return `${Math.round(cur * 100)}/${Math.round(max * 100)} HP`;
  }
  
  function hpClass(n){
    const p = Number(hpPct(n));
    return p >= 66 ? 'hp-high' : p >= 33 ? 'hp-med' : 'hp-low';
  }

  function escapeHtml(s){
    return (s||'').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;'}[c]));
  }

  els.npcRefresh?.addEventListener('click', fetchNpcs);
  els.npcSearch?.addEventListener('input', () => renderNpcs());

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
  setInterval(fetchHealth, 5000);
  setInterval(fetchGame, 5000);
  // Poll NPCs periodically when on NPCs view
  setInterval(() => {
    const isOnNpcs = document.getElementById('view-npcs')?.classList.contains('active');
    if(isOnNpcs) fetchNpcs();
  }, 2000);
})();
