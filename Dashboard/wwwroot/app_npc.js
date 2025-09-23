'use strict';

(function(){
  const els = {
    dot: document.getElementById('health-dot'),
    text: document.getElementById('health-text'),
    photo: document.getElementById('npc-det-photo'),
    name: document.getElementById('npc-det-name'),
    surname: document.getElementById('npc-det-surname'),
    hpFill: document.getElementById('npc-det-hpfill'),
    title: document.getElementById('view-title')
  };

  const params = new URLSearchParams(location.search);
  const npcId = Number(params.get('id'));

  function hpPct(n){
    const cur = Number(n.hpCurrent)||0;
    const max = Number(n.hpMax)||0;
    if(max <= 0) return 0;
    const pct = Math.max(0, Math.min(100, (cur/max)*100));
    return pct.toFixed(0);
  }

  async function fetchHealth(){
    try{
      const res = await fetch('/api/health', { cache:'no-store' });
      if(!res.ok) throw 0;
      els.dot.style.background = '#11d67a';
      els.text.textContent = 'Online';
    }catch{
      els.dot.style.background = '#e05555';
      els.text.textContent = 'Offline';
    }
  }

  async function loadNpc(){
    if(!Number.isFinite(npcId)) return;
    try{
      const res = await fetch('/api/npcs', { cache:'no-store' });
      if(!res.ok) throw 0;
      const list = await res.json();
      const n = list.find(x => x.id === npcId);
      if(!n) return;
      
      // Update page title
      document.title = `${n.name || ''} ${n.surname || ''} - NPC Details`;
      if(els.title) els.title.textContent = `${n.name || ''} ${n.surname || ''}`;
      
      // Update details
      els.photo.src = n.photo || 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottQAAAABJRU5ErkJggg==';
      els.name.textContent = n.name || '—';
      els.surname.textContent = n.surname || '';
      els.hpFill.style.width = hpPct(n) + '%';
    }catch{}
  }

  // initial + polling
  fetchHealth();
  loadNpc();
  setInterval(fetchHealth, 5000);
  setInterval(loadNpc, 2000);
})();
