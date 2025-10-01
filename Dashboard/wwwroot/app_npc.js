'use strict';

(function(){
  // Theme handling (match app.js)
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
    // remove all
    for(const cls of Object.values(THEME_MAP)){
      if(!cls) continue; root.classList.remove(cls);
    }
    const cls = THEME_MAP[name] || '';
    if(cls) root.classList.add(cls);
    if(name !== 'custom') clearCustomVars(); else applyCustomFromStorage();
  }
  function loadTheme(){
    try{
      const v = localStorage.getItem(THEME_KEY);
      return (v && (v in THEME_MAP)) ? v : 'blue';
    }catch{ return 'blue'; }
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
    try{
      const hex = localStorage.getItem(THEME_CUSTOM_KEY) || '#00b3ff';
      setCustomVars(hex);
    }catch{}
  }
  // Initialize theme immediately and react to cross-tab changes
  const initialTheme = loadTheme();
  applyTheme(initialTheme);
  window.addEventListener('storage', (e)=>{
    if(e.key === THEME_KEY){ applyTheme(loadTheme()); }
    if(e.key === THEME_CUSTOM_KEY && loadTheme()==='custom'){ applyCustomFromStorage(); }
  });

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
    // Profile fields
    age: document.getElementById('npc-age'),
    gender: document.getElementById('npc-gender'),
    height: document.getElementById('npc-height'),
    buildTxt: document.getElementById('npc-build'),
    hair: document.getElementById('npc-hair'),
    eyes: document.getElementById('npc-eyes'),
    shoe: document.getElementById('npc-shoe'),
    glasses: document.getElementById('npc-glasses'),
    facialhair: document.getElementById('npc-facialhair'),
    dob: document.getElementById('npc-dob'),
    phone: document.getElementById('npc-phone'),
    // Employment extra
    workhours: document.getElementById('npc-workhours'),
    // Dev
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
      
      // Home address - make it clickable if we have an addressId
      if (els.address) {
        const addressText = n.homeAddress || '—';
        if (n.homeAddressId && n.homeAddressId > 0) {
          els.address.innerHTML = `<span class="clickable">${addressText}</span>`;
          els.address.style.cursor = 'pointer';
          els.address.onclick = () => window.location.href = `residence.html?id=${n.homeAddressId}`;
        } else {
          els.address.textContent = addressText;
          els.address.onclick = null;
          els.address.style.cursor = 'default';
        }
      }

      // Profile values
      if (els.age) els.age.textContent = (Number.isFinite(n.ageYears) && n.ageYears > 0) ? `${n.ageYears} (${n.ageGroup||''})` : (n.ageGroup || '—');
      if (els.gender) els.gender.textContent = n.gender || '—';
      if (els.height) {
        const hcm = Number(n.heightCm)||0;
        els.height.textContent = hcm > 0 ? `${hcm} cm (${n.heightCategory||'—'})` : (n.heightCategory || '—');
      }
      if (els.buildTxt) els.buildTxt.textContent = n.build || '—';
      if (els.hair) {
        const parts = [];
        if (n.hairType) parts.push(n.hairType);
        if (n.hairColor) parts.push(n.hairColor.toLowerCase ? n.hairColor.toLowerCase() : n.hairColor);
        els.hair.textContent = parts.join(', ') || '—';
      }
      if (els.eyes) els.eyes.textContent = n.eyes || '—';
      if (els.shoe) els.shoe.textContent = (Number(n.shoeSize)||0) > 0 ? String(n.shoeSize) : '—';
      if (els.glasses) els.glasses.textContent = n.glasses ? 'Yes' : 'No';
      if (els.facialhair) els.facialhair.textContent = n.facialHair ? 'Yes' : 'No';
      if (els.dob) els.dob.textContent = n.dateOfBirth || '—';
      if (els.phone) els.phone.textContent = n.telephoneNumber || '—';

      // Employment extras
      if (els.workhours) els.workhours.textContent = n.workHours || '—';
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
