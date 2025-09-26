'use strict';

(function(){
  // Theme: apply saved theme immediately
  try{
    const THEME_KEY = 'sod_theme';
    const t = localStorage.getItem(THEME_KEY);
    if(t === 'red') document.documentElement.classList.add('theme-red');
    else document.documentElement.classList.remove('theme-red');
  }catch{}

  const els = {
    dot: document.getElementById('health-dot'),
    text: document.getElementById('health-text'),
    photo: document.getElementById('npc-det-photo'),
    name: document.getElementById('npc-det-name'),
    hpText: document.getElementById('npc-det-hptext'),
    hpFill: document.getElementById('npc-det-hpfill'),
    hpBar: document.getElementById('npc-det-hpbar'),
    hpLabel: document.getElementById('npc-det-hplabel'),
    koBar: document.getElementById('npc-ko-bar'),
    koFill: document.getElementById('npc-ko-fill'),
    title: document.getElementById('view-title'),
    employer: document.getElementById('npc-employer'),
    job: document.getElementById('npc-job'),
    salary: document.getElementById('npc-salary'),
    address: document.getElementById('npc-address'),
    devTeleportPlayer: document.getElementById('dev-teleport-player'),
    devTeleportNpc: document.getElementById('dev-teleport-npc'),
    devStatus: document.getElementById('dev-action-status')
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

  function setDevStatus(message, tone = 'info'){
    if(!els.devStatus) return;
    els.devStatus.textContent = message;
    els.devStatus.classList.remove('dev-status-info','dev-status-ok','dev-status-error');
    const cls = tone === 'ok' ? 'dev-status-ok' : tone === 'error' ? 'dev-status-error' : 'dev-status-info';
    els.devStatus.classList.add(cls);
  }

  function setDevButtonsDisabled(disabled){
    if(els.devTeleportPlayer) els.devTeleportPlayer.disabled = disabled;
    if(els.devTeleportNpc) els.devTeleportNpc.disabled = disabled;
  }

  async function runNpcAction(action){
    if(!Number.isFinite(npcId)) return;
    if(!action) return;
    setDevStatus('Running action…','info');
    setDevButtonsDisabled(true);
    try{
      const res = await fetch(`/api/npc/${npcId}/${action}`, { method:'POST', cache:'no-store' });
      const data = await res.json().catch(() => ({}));
      if(!res.ok || data.success === false){
        const message = data.message || `Action failed (${res.status})`;
        setDevStatus(message, 'error');
      }else{
        setDevStatus(data.message || 'Action completed.', 'ok');
      }
    }catch(err){
      setDevStatus(`Action failed: ${err?.message || err}`, 'error');
    }finally{
      setDevButtonsDisabled(false);
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
      
      // HP bar and text with death handling
      const isDead = !!n.isDead;
      const cur = Number(n.hpCurrent)||0;
      const max = Number(n.hpMax)||0;
      const pct = Number(hpPct(n));
      
      if (isDead) {
        if (els.hpFill) els.hpFill.style.width = '0%';
        if (els.hpLabel) els.hpLabel.textContent = 'DEAD';
        if (els.hpText) {
          els.hpText.textContent = 'Dead';
          els.hpText.classList.remove('hp-high','hp-med','hp-low');
          els.hpText.classList.add('hp-dead');
        }
      } else {
        if (els.hpFill) els.hpFill.style.width = pct + '%';
        if (els.hpLabel) els.hpLabel.textContent = '';
        if (els.hpText) {
          els.hpText.textContent = `${Math.round(cur * 100)}/${Math.round(max * 100)} HP`;
          els.hpText.classList.remove('hp-high','hp-med','hp-low','hp-dead');
          els.hpText.classList.add(pct >= 66 ? 'hp-high' : pct >= 33 ? 'hp-med' : 'hp-low');
        }
      }

      // KO Bar (no interpolation - poll-based only)
      const isKo = !!n.isKo;
      const koTotal = Number(n.koTotalSeconds) || 0;
      const koRemain = Number(n.koRemainingSeconds) || 0;
      if (isKo && koTotal > 0 && koRemain >= 0) {
        const kpct = Math.max(0, Math.min(100, (koRemain / koTotal) * 100));
        if (els.koFill) els.koFill.style.width = kpct.toFixed(0) + '%';
        if (els.koBar) els.koBar.classList.remove('hidden');
      } else {
        if (els.koFill) els.koFill.style.width = '0%';
        if (els.koBar) els.koBar.classList.add('hidden');
      }
      
      // Job information
      if (els.employer) els.employer.textContent = n.employer || '—';
      if (els.job) els.job.textContent = n.jobTitle || '—';
      if (els.salary) els.salary.textContent = n.salary || '—';
      
      // Home address
      if (els.address) els.address.textContent = n.homeAddress || '—';
    }catch{}
  }

  // No interpolation loop

  if(els.devTeleportPlayer){
    els.devTeleportPlayer.addEventListener('click', () => runNpcAction('teleport-player'));
  }
  if(els.devTeleportNpc){
    els.devTeleportNpc.addEventListener('click', () => runNpcAction('teleport-npc'));
  }

  // initial + polling
  fetchHealth();
  loadNpc();
  setInterval(fetchHealth, 2000);
  setInterval(loadNpc, 2000);
})();
