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
    refreshBtn: document.getElementById('refresh-btn'),
    npcList: document.getElementById('npc-list'),
    npcSearch: document.getElementById('npc-search'),
    npcRefresh: document.getElementById('npc-refresh')
  };
  let npcCache = [];

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
    if(views.includes(v)) setActiveView(v);
  }));

  async function fetchHealth(){
    try{
      const res = await fetch('/api/health', { cache: 'no-store' });
      if(!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      els.healthDot.style.background = '#11d67a';
      els.healthText.textContent = 'Online';
      els.serverStatus.textContent = 'Online';
      els.serverPort.textContent = data.port ?? '—';
      els.serverTime.textContent = new Date(data.time || Date.now()).toLocaleString();
    }catch(err){
      els.healthDot.style.background = '#e05555';
      els.healthText.textContent = 'Offline';
      els.serverStatus.textContent = 'Offline';
    }
  }

  els.refreshBtn.addEventListener('click', () => fetchHealth());

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
      <div class="npc-card" title="${escapeHtml(n.name || '')}">
        <div class="npc-photo">
          <img class="npc-img" loading="lazy" src="${n.photo || 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottQAAAABJRU5ErkJggg=='}" alt="${escapeHtml(n.name || '')}"/>
        </div>
        <div class="npc-meta">
          <div class="npc-name">${escapeHtml(n.name || '')}</div>
          <div class="npc-hp"><div class="npc-hp-fill" style="width:${hpPct(n)}%"></div></div>
        </div>
      </div>
    `).join('');
    els.npcList.innerHTML = html;
  }

  function hpPct(n){
    const cur = Number(n.hpCurrent)||0;
    const max = Number(n.hpMax)||0;
    if(max <= 0) return 0;
    const pct = Math.max(0, Math.min(100, (cur/max)*100));
    return pct.toFixed(0);
  }

  function escapeHtml(s){
    return (s||'').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;'}[c]));
  }

  els.npcRefresh?.addEventListener('click', fetchNpcs);
  els.npcSearch?.addEventListener('input', () => renderNpcs());

  // initial
  setActiveView('home');
  fetchHealth();
  setInterval(fetchHealth, 5000);
  // Poll NPCs periodically when on NPCs view
  setInterval(() => {
    const isOnNpcs = document.getElementById('view-npcs')?.classList.contains('active');
    if(isOnNpcs) fetchNpcs();
  }, 2000);
})();
