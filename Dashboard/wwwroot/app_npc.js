'use strict';

(function(){
  const els = {
    dot: document.getElementById('health-dot'),
    text: document.getElementById('health-text'),
    photo: document.getElementById('npc-det-photo'),
    name: document.getElementById('npc-det-name'),
    hpText: document.getElementById('npc-det-hptext'),
    hpFill: document.getElementById('npc-det-hpfill'),
    title: document.getElementById('view-title'),
    employer: document.getElementById('npc-employer'),
    job: document.getElementById('npc-job'),
    salary: document.getElementById('npc-salary'),
    address: document.getElementById('npc-address')
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
      
      // Update page title - only use name
      document.title = `${n.name || 'NPC'} - NPC Details`;
      if(els.title) els.title.textContent = n.name || 'NPC';
      
      // Update details
      els.photo.src = n.photo || 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottQAAAABJRU5ErkJggg==';
      els.name.textContent = n.name || '—';
      
      // HP bar and text with color coding
      const cur = Number(n.hpCurrent)||0;
      const max = Number(n.hpMax)||0;
      const pct = Number(hpPct(n));
      els.hpFill.style.width = pct + '%';
      
      // HP text with color level
      if (els.hpText) {
        // Scale by 100 to show 0-100 range
        els.hpText.textContent = `${Math.round(cur * 100)}/${Math.round(max * 100)} HP`;
        els.hpText.classList.remove('hp-high','hp-med','hp-low');
        els.hpText.classList.add(pct >= 66 ? 'hp-high' : pct >= 33 ? 'hp-med' : 'hp-low');
      }
      
      // Job information
      if (els.employer) els.employer.textContent = n.employer || '—';
      if (els.job) els.job.textContent = n.jobTitle || '—';
      if (els.salary) els.salary.textContent = n.salary || '—';
      
      // Home address
      if (els.address) els.address.textContent = n.homeAddress || '—';
    }catch{}
  }

  // initial + polling
  fetchHealth();
  loadNpc();
  setInterval(fetchHealth, 5000);
  setInterval(loadNpc, 2000);
})();
