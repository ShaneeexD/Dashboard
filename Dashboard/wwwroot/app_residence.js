'use strict';

(function(){
  // Theme handling (same as npc page)
  const THEME_KEY = 'sod_theme';
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
  const initialTheme = loadTheme();
  applyTheme(initialTheme);
  window.addEventListener('storage', (e)=>{
    if(e.key === THEME_KEY){ applyTheme(loadTheme()); }
    if(e.key === THEME_CUSTOM_KEY && loadTheme()==='custom'){ applyCustomFromStorage(); }
  });

  const els = {
    healthDot: document.getElementById('health-dot'),
    healthText: document.getElementById('health-text'),
    residenceName: document.getElementById('residence-name'),
    residenceType: document.getElementById('residence-type'),
    building: document.getElementById('res-building'),
    floor: document.getElementById('res-floor'),
    preset: document.getElementById('res-preset'),
    style: document.getElementById('res-style'),
    rooms: document.getElementById('res-rooms'),
    residentCount: document.getElementById('res-resident-count'),
    residentsList: document.getElementById('residents-list')
  };

  const params = new URLSearchParams(location.search);
  const addressId = Number(params.get('id'));

  async function fetchHealth(){
    try{
      const res = await fetch('/api/health', { cache:'no-store' });
      if(!res.ok) throw 0;
      els.healthDot.style.background = '#11d67a';
      els.healthText.textContent = 'Online';
    }catch{
      els.healthDot.style.background = '#e05555';
      els.healthText.textContent = 'Offline';
    }
  }

  async function loadResidence(){
    if(!Number.isFinite(addressId) || addressId < 0) {
      els.residenceName.textContent = 'Invalid address ID';
      return;
    }
    
    try{
      const res = await fetch(`/api/address/${addressId}`, { cache:'no-store' });
      if(!res.ok) {
        if(res.status === 404){
          els.residenceName.textContent = 'Address not found';
          els.residentsList.innerHTML = '<div class="loading-message">No data available</div>';
        }
        return;
      }
      
      const addr = await res.json();
      
      // Update page title and header
      document.title = `${addr.name || 'Residence'} - Residence Details`;
      els.residenceName.textContent = addr.name || '—';
      els.residenceType.textContent = addr.isResidence ? 'Residence' : 'Commercial/Other';
      
      // Location info
      els.building.textContent = addr.buildingName || '—';
      els.floor.textContent = addr.floor || (addr.floorNumber >= 0 ? `Floor ${addr.floorNumber}` : '—');
      els.preset.textContent = addr.addressPreset || '—';
      
      // Details
      els.style.textContent = addr.designStyle || '—';
      els.rooms.textContent = addr.roomCount || '0';
      els.residentCount.textContent = (addr.residents?.length || 0).toString();
      
      // Residents list
      if (addr.residents && addr.residents.length > 0) {
        let html = '';
        for (const resident of addr.residents) {
          const photoSrc = resident.photo || 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottQAAAABJRU5ErkJggg==';
          html += `
            <div class="resident-card">
              <img src="${photoSrc}" alt="${resident.name}" class="resident-photo"/>
              <div class="resident-info">
                <div class="resident-name"><a href="npc.html?id=${resident.id}">${resident.name} ${resident.surname}</a></div>
              </div>
            </div>
          `;
        }
        els.residentsList.innerHTML = html;
      } else {
        els.residentsList.innerHTML = '<div class="loading-message">No residents</div>';
      }
    }catch(err){
      console.error('Failed to load residence:', err);
      els.residenceName.textContent = 'Error loading data';
      els.residentsList.innerHTML = '<div class="loading-message">Error loading residents</div>';
    }
  }

  // Initial load and health polling
  fetchHealth();
  loadResidence();
  setInterval(fetchHealth, 2000);
  setInterval(loadResidence, 5000); // Poll every 5s for resident updates
})();
