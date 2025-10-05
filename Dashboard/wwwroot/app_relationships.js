'use strict';

(function(){
  const canvas = document.getElementById('rel-canvas');
  const tooltip = document.getElementById('rel-tooltip');
  const viewTypeSelect = document.getElementById('rel-view-type');
  const npcIdInput = document.getElementById('rel-npc-id');
  const loadBtn = document.getElementById('rel-load');
  const showLabelsCheckbox = document.getElementById('rel-show-labels');
  const nodeCountEl = document.getElementById('rel-node-count');
  const edgeCountEl = document.getElementById('rel-edge-count');
  const statsEl = document.getElementById('rel-stats');

  if (!canvas) return;

  const ctx = canvas.getContext('2d');
  let nodes = [];
  let edges = [];
  let animationId = null;
  let isDragging = false;
  let selectedNode = null;
  let hoveredNode = null;
  let physicsEnabled = false; // Disable physics by default
  let nodeImages = {}; // Cache for loaded images

  function resizeCanvas(){
    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    canvas.style.width = rect.width + 'px';
    canvas.style.height = rect.height + 'px';
    ctx.scale(dpr, dpr);
  }

  function loadGraph(type, npcId){
    let url = '';
    if(type === 'city'){
      url = '/api/relationships/city?max=50';
    } else {
      if(!npcId) {
        alert('Please enter an NPC ID');
        return;
      }
      url = `/api/relationships/npc/${npcId}?depth=1`;
    }

    fetch(url, { cache: 'no-store' })
      .then(res => res.json())
      .then(data => {
        initGraph(data);
      })
      .catch(err => {
        console.error('Failed to load relationship graph:', err);
        alert('Failed to load graph');
      });
  }

  function initGraph(data){
    const rect = canvas.getBoundingClientRect();
    const centerX = rect.width / 2;
    const centerY = rect.height / 2;
    
    // Arrange nodes in a circle pattern for better initial layout
    const radius = Math.min(rect.width, rect.height) * 0.35;
    nodes = data.nodes.map((n, i) => {
      const angle = (i / data.nodes.length) * Math.PI * 2;
      const nodeRadius = Math.max(20, Math.min(30, 20 + n.connectionCount * 1.5));
      
      // Load image if available
      if(n.photo && !nodeImages[n.id]){
        const img = new Image();
        img.onload = () => {
          nodeImages[n.id] = img;
        };
        img.src = 'data:image/png;base64,' + n.photo;
      }
      
      return {
        id: n.id,
        name: n.name,
        occupation: n.occupation,
        connectionCount: n.connectionCount,
        photo: n.photo,
        x: centerX + Math.cos(angle) * radius,
        y: centerY + Math.sin(angle) * radius,
        radius: nodeRadius
      };
    });

    edges = data.edges.map(e => ({
      source: nodes.find(n => n.id === e.sourceId),
      target: nodes.find(n => n.id === e.targetId),
      strength: e.strength,
      type: e.type,
      mutual: e.mutual
    })).filter(e => e.source && e.target);

    if(nodeCountEl) nodeCountEl.textContent = nodes.length;
    if(edgeCountEl) edgeCountEl.textContent = edges.length;
    if(statsEl) statsEl.style.display = 'block';

    startSimulation();
  }

  function startSimulation(){
    if(animationId) cancelAnimationFrame(animationId);
    animate();
  }

  function animate(){
    if(physicsEnabled){
      updatePhysics();
    }
    render();
    animationId = requestAnimationFrame(animate);
  }

  function updatePhysics(){
    // Physics disabled - nodes are static
  }

  function render(){
    const rect = canvas.getBoundingClientRect();
    ctx.clearRect(0, 0, rect.width, rect.height);

    // Draw edges
    for(const edge of edges){
      const color = getEdgeColor(edge.type);
      const alpha = edge.mutual ? 0.6 : 0.3;
      ctx.strokeStyle = color.replace(')', `, ${alpha})`).replace('rgb', 'rgba');
      ctx.lineWidth = 1 + edge.strength * 2;
      ctx.beginPath();
      ctx.moveTo(edge.source.x, edge.source.y);
      ctx.lineTo(edge.target.x, edge.target.y);
      ctx.stroke();
    }

    // Draw nodes
    for(const node of nodes){
      const isHovered = node === hoveredNode;
      const isSelected = node === selectedNode;
      
      // Node shadow
      if(isHovered || isSelected){
        ctx.shadowBlur = 15;
        ctx.shadowColor = 'rgba(109, 197, 255, 0.8)';
      }

      // Draw photo if available
      const img = nodeImages[node.id];
      if(img && img.complete){
        ctx.save();
        ctx.beginPath();
        ctx.arc(node.x, node.y, node.radius, 0, Math.PI * 2);
        ctx.closePath();
        ctx.clip();
        
        // Draw image to fill the circle
        const size = node.radius * 2;
        ctx.drawImage(img, node.x - node.radius, node.y - node.radius, size, size);
        ctx.restore();
      } else {
        // Fallback: colored circle
        ctx.fillStyle = getNodeColor(node);
        ctx.beginPath();
        ctx.arc(node.x, node.y, node.radius, 0, Math.PI * 2);
        ctx.fill();
      }

      // Node border
      ctx.strokeStyle = isSelected ? '#6dc5ff' : isHovered ? '#4a9fd8' : 'rgba(255,255,255,0.3)';
      ctx.lineWidth = isSelected || isHovered ? 3 : 2;
      ctx.beginPath();
      ctx.arc(node.x, node.y, node.radius, 0, Math.PI * 2);
      ctx.stroke();

      ctx.shadowBlur = 0;

      // Labels
      if(showLabelsCheckbox?.checked){
        ctx.fillStyle = '#ffffff';
        ctx.font = '11px Inter, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(node.name, node.x, node.y + node.radius + 14);
      }
    }
  }

  function getNodeColor(node){
    const hue = (node.connectionCount * 30) % 360;
    return `hsl(${hue}, 70%, 50%)`;
  }

  function getEdgeColor(type){
    switch(type){
      case 'romantic': return 'rgb(255, 100, 150)';
      case 'roommate': return 'rgb(100, 200, 100)';
      case 'colleague': return 'rgb(100, 150, 255)';
      case 'friend': return 'rgb(255, 200, 100)';
      default: return 'rgb(150, 150, 150)';
    }
  }

  function getNodeAt(x, y){
    for(const node of nodes){
      const dx = x - node.x;
      const dy = y - node.y;
      if(dx * dx + dy * dy <= node.radius * node.radius){
        return node;
      }
    }
    return null;
  }

  function showTooltip(node, x, y){
    if(!tooltip) return;
    tooltip.innerHTML = `
      <div style="font-weight:600; margin-bottom:4px">${node.name}</div>
      <div style="font-size:11px; color:var(--muted)">${node.occupation}</div>
      <div style="font-size:11px; color:var(--muted); margin-top:4px">Connections: ${node.connectionCount}</div>
    `;
    tooltip.style.left = (x + 10) + 'px';
    tooltip.style.top = (y + 10) + 'px';
    tooltip.style.display = 'block';
  }

  function hideTooltip(){
    if(tooltip) tooltip.style.display = 'none';
  }

  // Event listeners
  canvas.addEventListener('mousedown', (e) => {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    selectedNode = getNodeAt(x, y);
    if(selectedNode){
      isDragging = true;
      canvas.style.cursor = 'grabbing';
    }
  });

  canvas.addEventListener('mousemove', (e) => {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    if(isDragging && selectedNode){
      selectedNode.x = x;
      selectedNode.y = y;
    } else {
      const node = getNodeAt(x, y);
      if(node !== hoveredNode){
        hoveredNode = node;
        if(node){
          canvas.style.cursor = 'pointer';
          showTooltip(node, e.clientX, e.clientY);
        } else {
          canvas.style.cursor = 'grab';
          hideTooltip();
        }
      } else if(hoveredNode){
        showTooltip(hoveredNode, e.clientX, e.clientY);
      }
    }
  });

  canvas.addEventListener('mouseup', () => {
    isDragging = false;
    selectedNode = null;
    canvas.style.cursor = hoveredNode ? 'pointer' : 'grab';
  });

  canvas.addEventListener('mouseleave', () => {
    isDragging = false;
    selectedNode = null;
    hoveredNode = null;
    hideTooltip();
    canvas.style.cursor = 'grab';
  });

  viewTypeSelect?.addEventListener('change', () => {
    if(viewTypeSelect.value === 'npc'){
      npcIdInput.style.display = 'block';
    } else {
      npcIdInput.style.display = 'none';
    }
  });

  loadBtn?.addEventListener('click', () => {
    loadGraph(viewTypeSelect.value, npcIdInput.value);
  });

  showLabelsCheckbox?.addEventListener('change', () => {
    // Render will pick up the checkbox state
  });

  window.addEventListener('resize', resizeCanvas);
  resizeCanvas();

  // Auto-load city graph on page load
  if(window.location.search.includes('view=relationships')){
    setTimeout(() => loadGraph('city'), 500);
  }

})();
