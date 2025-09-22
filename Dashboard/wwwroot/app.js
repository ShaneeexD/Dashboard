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
    refreshBtn: document.getElementById('refresh-btn')
  };

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

  // initial
  setActiveView('home');
  fetchHealth();
  setInterval(fetchHealth, 5000);
})();
