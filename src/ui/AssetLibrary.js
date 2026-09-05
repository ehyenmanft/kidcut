/**
 * KIDCUT ASSET LIBRARY
 * Manages Media (with audio preservation), 100 Filters & Effects,
 * 8-Bit Text with animations, Pixel Stickers, Retro Transitions & Chiptune SFX.
 */

import { audioEngine } from '../engine/AudioEngine.js';
import { AudioExtractor } from '../engine/AudioExtractor.js';
import { ALL_EFFECTS, EFFECT_CATEGORIES, RetroTransitions, TextAnimations } from '../engine/EffectsDatabase.js';

// Local store of imported media assets
const importedAssets = [];

export function setupAssetLibrary(timelineEngine) {
  const contentArea = document.getElementById('resource-content-area');
  const navTabs = document.querySelectorAll('.resource-nav .nav-tab');

  let activeTab = 'media';

  // Navigation tab switching
  navTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      navTabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');
      activeTab = tab.dataset.tab;
      renderTabContent();
      audioEngine.playBeep(480, 'square', 0.04);
    });
  });

  function renderTabContent() {
    contentArea.innerHTML = '';
    switch (activeTab) {
      case 'media':
        contentArea.appendChild(renderMediaTab());
        break;
      case 'text':
        contentArea.appendChild(renderTextTab());
        break;
      case 'stickers':
        contentArea.appendChild(renderStickersTab());
        break;
      case 'chiptune':
        contentArea.appendChild(renderChiptuneTab());
        break;
      case 'fx':
        contentArea.appendChild(renderFxTab());
        break;
    }
  }

  // =========================================================================
  // 1. MEDIA TAB (LOCAL FILE IMPORTER WITH AUDIO EXTRACTION & PRESERVATION)
  // =========================================================================
  function renderMediaTab() {
    const wrap = document.createElement('div');
    wrap.className = 'tab-pane media-pane';

    wrap.innerHTML = `
      <div class="asset-section-title">
        <span>ARCHIVOS LOCALES</span>
        <span class="badge-count">${importedAssets.length}</span>
      </div>

      <div class="upload-dropzone" id="media-dropzone">
        <div class="dropzone-icon">📥</div>
        <div class="dropzone-text">IMPORTAR ARCHIVOS</div>
        <div class="dropzone-sub">Haz clic o arrastra videos (MP4, WebM), audios (MP3, WAV) o fotos</div>
        <div class="dropzone-sub" style="color: var(--color-gold); margin-top: 4px;">★ Preserva 100% el audio original sincronizado</div>
        <input type="file" id="media-file-input" multiple accept="video/*,audio/*,image/*" style="display: none;" />
      </div>

      <div class="asset-section-title" style="margin-top: 15px;">
        <span>BIBLIOTECA DE MEDIOS</span>
      </div>
      <div class="media-grid" id="media-grid"></div>
    `;

    const dropzone = wrap.querySelector('#media-dropzone');
    const fileInput = wrap.querySelector('#media-file-input');
    const mediaGrid = wrap.querySelector('#media-grid');

    dropzone.addEventListener('click', () => fileInput.click());

    dropzone.addEventListener('dragover', (e) => {
      e.preventDefault();
      dropzone.style.background = 'rgba(0, 240, 255, 0.2)';
    });

    dropzone.addEventListener('dragleave', () => {
      dropzone.style.background = 'rgba(0, 240, 255, 0.05)';
    });

    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.style.background = 'rgba(0, 240, 255, 0.05)';
      if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        handleFiles(e.dataTransfer.files);
      }
    });

    fileInput.addEventListener('change', (e) => {
      if (e.target.files && e.target.files.length > 0) {
        handleFiles(e.target.files);
      }
    });

    renderMediaGrid(mediaGrid);
    return wrap;
  }

  function handleFiles(fileList) {
    for (const file of fileList) {
      const isVideo = file.type.startsWith('video/') || /\.(mp4|webm|mov|mkv|avi)$/i.test(file.name);
      const isAudio = file.type.startsWith('audio/') || /\.(mp3|wav|ogg|aac|m4a|flac)$/i.test(file.name);
      const isImage = file.type.startsWith('image/') || /\.(png|jpg|jpeg|gif|webp)$/i.test(file.name);

      const blobUrl = URL.createObjectURL(file);
      const asset = {
        id: 'asset_' + Math.random().toString(36).substr(2, 9),
        name: file.name,
        type: isVideo ? 'video' : (isAudio ? 'audio' : 'image'),
        url: blobUrl,
        file,
        hasAudio: false,
        extractingAudio: false
      };

      if (isVideo) {
        const videoEl = document.createElement('video');
        videoEl.src = blobUrl;
        videoEl.preload = 'auto';
        videoEl.onloadedmetadata = () => {
          asset.duration = videoEl.duration || 5.0;
          asset.mediaElement = videoEl;
          renderTabContent();
        };

        // Extract and preserve original audio from video file
        asset.extractingAudio = true;
        AudioExtractor.extractAudio(file, blobUrl).then(buffer => {
          asset.extractingAudio = false;
          if (buffer) {
            asset.audioBuffer = buffer;
            asset.hasAudio = true;
          }
          renderTabContent();
        }).catch(() => {
          asset.extractingAudio = false;
          renderTabContent();
        });

      } else if (isImage) {
        const imgEl = new Image();
        imgEl.src = blobUrl;
        imgEl.onload = () => {
          asset.duration = 5.0;
          asset.mediaElement = imgEl;
          renderTabContent();
        };
      } else if (isAudio) {
        asset.duration = 10.0;
        asset.extractingAudio = true;
        AudioExtractor.extractAudio(file, blobUrl).then(buffer => {
          asset.extractingAudio = false;
          if (buffer) {
            asset.audioBuffer = buffer;
            asset.duration = buffer.duration;
            asset.hasAudio = true;
          }
          renderTabContent();
        }).catch(() => {
          asset.extractingAudio = false;
          renderTabContent();
        });
      }

      importedAssets.push(asset);
    }

    audioEngine.playCoin();
    renderTabContent();
  }

  function renderMediaGrid(grid) {
    grid.innerHTML = '';
    if (importedAssets.length === 0) {
      grid.innerHTML = `<div style="grid-column: span 2; font-size: 8px; color: var(--text-dim); text-align: center; padding: 20px;">NO HAY ARCHIVOS AÚN. HAZ CLIC ARRIBA PARA CARGAR UNO.</div>`;
      return;
    }

    importedAssets.forEach(asset => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.title = 'Haz doble clic para añadir a la línea de tiempo';

      let icon = '🎬';
      if (asset.type === 'audio') icon = '🎵';
      if (asset.type === 'image') icon = '🖼️';

      let audioBadge = '';
      if (asset.hasAudio) {
        audioBadge = '<span style="color: var(--color-cyan); font-size: 7px; font-weight: bold;">[🔊 AUDIO]</span>';
      } else if (asset.extractingAudio) {
        audioBadge = '<span style="color: var(--color-gold); font-size: 7px;">[⏳ AUDIO...]</span>';
      }

      card.innerHTML = `
        <div class="asset-thumb">${icon}</div>
        <div class="asset-name">${asset.name} ${audioBadge}</div>
      `;

      card.addEventListener('dblclick', () => {
        addAssetToTimeline(asset);
      });

      grid.appendChild(card);
    });
  }

  function addAssetToTimeline(asset) {
    const curTime = timelineEngine.currentTime;
    const dur = asset.duration || 5.0;

    if (asset.type === 'video') {
      timelineEngine.pushState(`Añadir Video ${asset.name}`);

      // 1. Add video clip to Track V1
      const videoClip = timelineEngine.addClip('track-v1', {
        name: asset.name,
        type: 'video',
        start: curTime,
        duration: dur,
        mediaElement: asset.mediaElement,
        audioBuffer: asset.audioBuffer,
        scale: 1.0
      }, true);

      // 2. Automatically link its original audio on Audio Track A1 so user has full multitrack control
      if (asset.audioBuffer) {
        timelineEngine.addClip('track-a1', {
          name: `${asset.name} (AUDIO)`,
          type: 'audio',
          start: curTime,
          duration: dur,
          audioBuffer: asset.audioBuffer,
          sourceVideoClipId: videoClip ? videoClip.id : null
        }, true);
      }
    } else if (asset.type === 'image') {
      timelineEngine.addClip('track-v1', {
        name: asset.name,
        type: 'image',
        start: curTime,
        duration: 5.0,
        mediaElement: asset.mediaElement,
        scale: 1.0
      });
    } else if (asset.type === 'audio') {
      timelineEngine.addClip('track-a1', {
        name: asset.name,
        type: 'audio',
        start: curTime,
        duration: dur,
        audioBuffer: asset.audioBuffer
      });
    }

    audioEngine.playBeep(650, 'square', 0.08);
  }

  // =========================================================================
  // 2. TEXT TAB (WITH 8-BIT ANIMATIONS & TRANSITIONS)
  // =========================================================================
  function renderTextTab() {
    const wrap = document.createElement('div');
    wrap.innerHTML = `
      <div class="asset-section-title">
        <span>TEXTO ARCADE Y ANIMACIONES</span>
      </div>

      <div class="text-presets-container">
        <div class="preset-category-title">ESTILOS CLÁSICOS</div>
        <div class="asset-grid" id="text-style-grid"></div>

        <div class="preset-category-title" style="margin-top: 14px;">ANIMACIONES RETRO (8-BIT)</div>
        <div class="asset-grid" id="text-anim-grid"></div>
      </div>
    `;

    contentArea.appendChild(wrap);

    const styleGrid = wrap.querySelector('#text-style-grid');
    const animGrid = wrap.querySelector('#text-anim-grid');

    const styles = [
      { text: '★ KIDCUT ★', font: "'Press Start 2P', monospace", color: '#ffd200', stroke: '#000000', anim: 'none' },
      { text: 'GAME OVER', font: "'Press Start 2P', monospace", color: '#ff3344', stroke: '#000000', anim: 'arcade_blink' },
      { text: 'PLAYER 1 READY', font: "'Silkscreen', cursive", color: '#00f0ff', stroke: '#000000', anim: 'pop_scale' },
      { text: 'LEVEL UP!', font: "'Press Start 2P', monospace", color: '#39ff14', stroke: '#000000', anim: 'wave_float' },
      { text: 'BOSS FIGHT', font: "'Press Start 2P', monospace", color: '#ff0055', stroke: '#000000', anim: 'glitch_shake' },
      { text: 'INSERT COIN', font: "'Press Start 2P', monospace", color: '#ffd200', stroke: '#000000', anim: 'arcade_blink' }
    ];

    styles.forEach(item => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.innerHTML = `
        <div class="text-preset-sample" style="font-family: ${item.font}; color: ${item.color};">${item.text}</div>
        <div class="asset-name">AÑADIR TEXTO</div>
      `;
      card.addEventListener('click', () => {
        timelineEngine.addClip('track-v2', {
          name: item.text,
          type: 'text',
          text: item.text,
          textColor: item.color,
          strokeColor: item.stroke,
          fontFamily: item.font,
          textAnimation: item.anim,
          fontSize: 48,
          duration: 3.5,
          y: 0.5
        });
        audioEngine.playLaser();
      });
      styleGrid.appendChild(card);
    });

    const animPresets = [
      { id: 'arcade_blink', name: 'Arcade Blink', icon: '✨', desc: 'Parpadeo clásico de máquina recreativa' },
      { id: 'typewriter', name: 'Typewriter', icon: '⌨️', desc: 'Aparición letra por letra retro' },
      { id: 'wave_float', name: 'Wave Float', icon: '〰️', desc: 'Flotación senoidal suave' },
      { id: 'glitch_shake', name: 'Glitch Shake', icon: '⚡', desc: 'Vibración errática de glitch' },
      { id: 'rainbow_cycle', name: 'Rainbow Cycle', icon: '🌈', desc: 'Ciclo cromático de colores arcade' },
      { id: 'pop_scale', name: 'Pop Bounce', icon: '💥', desc: 'Aparición con rebote elástico' }
    ];

    animPresets.forEach(anim => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.innerHTML = `
        <div class="asset-thumb">${anim.icon}</div>
        <div class="asset-name">${anim.name}</div>
      `;
      card.title = anim.desc;
      card.addEventListener('click', () => {
        const selected = timelineEngine.getSelectedClip();
        if (selected && selected.type === 'text') {
          selected.textAnimation = anim.id;
          timelineEngine.notify('clipupdated', { clip: selected });
          audioEngine.playBeep(520, 'square', 0.05);
        } else {
          // Add new animated text clip
          timelineEngine.addClip('track-v2', {
            name: anim.name.toUpperCase(),
            type: 'text',
            text: anim.name.toUpperCase(),
            textColor: '#00f0ff',
            textAnimation: anim.id,
            fontSize: 44,
            duration: 3.5,
            y: 0.5
          });
          audioEngine.playLaser();
        }
      });
      animGrid.appendChild(card);
    });
  }

  // =========================================================================
  // 3. STICKERS TAB (8-BIT PROCEDURAL & SPRITE ICONS)
  // =========================================================================
  function renderStickersTab() {
    const wrap = document.createElement('div');
    wrap.innerHTML = `
      <div class="asset-section-title">
        <span>STICKERS PIXEL ART (ARRÁSTRALOS EN PANTALLA)</span>
      </div>
      <div class="asset-grid" id="stickers-grid"></div>
    `;

    contentArea.appendChild(wrap);
    const grid = wrap.querySelector('#stickers-grid');

    const stickers = [
      { emoji: '👾', name: 'Marciano 8B' },
      { emoji: '❤️', name: 'Vida Retro' },
      { emoji: '🪙', name: 'Moneda Oro' },
      { emoji: '🔥', name: 'Fuego Pixel' },
      { emoji: '⚡', name: 'Rayo Neón' },
      { emoji: '🕹️', name: 'Arcade Joy' },
      { emoji: '💣', name: 'Bomba 8-Bit' },
      { emoji: '🍄', name: 'Power Seta' },
      { emoji: '⭐', name: 'Estrella Pro' },
      { emoji: '💀', name: 'Calavera' },
      { emoji: '🚀', name: 'Cohete 8B' },
      { emoji: '🗡️', name: 'Espada Pixel' }
    ];

    stickers.forEach(s => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.innerHTML = `
        <div class="asset-thumb" style="font-size: 28px;">${s.emoji}</div>
        <div class="asset-name">${s.name}</div>
      `;
      card.addEventListener('click', () => {
        timelineEngine.addClip('track-v2', {
          name: `Sticker ${s.emoji}`,
          type: 'sticker',
          emoji: s.emoji,
          scale: 1.5,
          x: 0.5,
          y: 0.5,
          duration: 3.0
        });
        audioEngine.playJump();
      });
      grid.appendChild(card);
    });
  }

  // =========================================================================
  // 4. CHIPTUNE TAB (8-BIT SOUND SYNTHESIZER)
  // =========================================================================
  function renderChiptuneTab() {
    const wrap = document.createElement('div');
    wrap.innerHTML = `
      <div class="asset-section-title">
        <span>EFECTOS DE SONIDO CHIPTUNE 8-BIT</span>
      </div>
      <div class="asset-grid" id="chiptune-grid"></div>
    `;

    contentArea.appendChild(wrap);
    const grid = wrap.querySelector('#chiptune-grid');

    const sfxList = [
      { id: 'coin', name: 'Moneda Arcade', icon: '🪙' },
      { id: 'laser', name: 'Disparo Láser', icon: '⚡' },
      { id: 'jump', name: 'Salto 8-Bit', icon: '🦘' },
      { id: 'powerup', name: 'Power Up!', icon: '🍄' },
      { id: 'explosion', name: 'Explosión Retro', icon: '💥' },
      { id: 'hit', name: 'Golpe Arcade', icon: '🥊' },
      { id: '1up', name: 'Vida Extra 1UP', icon: '💚' },
      { id: 'gameover', name: 'Game Over', icon: '💀' }
    ];

    sfxList.forEach(sfx => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.innerHTML = `
        <div class="asset-thumb">${sfx.icon}</div>
        <div class="asset-name">${sfx.name}</div>
        <div style="font-size: 7px; color: var(--color-cyan); margin-top: 3px;">CLIC: PROBAR / DOBLE CLIC: AÑADIR</div>
      `;

      card.addEventListener('click', () => {
        if (sfx.id === 'coin') audioEngine.playCoin();
        else if (sfx.id === 'laser') audioEngine.playLaser();
        else if (sfx.id === 'jump') audioEngine.playJump();
        else if (sfx.id === 'powerup') audioEngine.playPowerup();
        else if (sfx.id === 'explosion') audioEngine.playExplosion();
        else audioEngine.playBeep(440, 'square', 0.1);
      });

      card.addEventListener('dblclick', async () => {
        const buffer = await audioEngine.generateSfxBuffer(sfx.id);
        timelineEngine.addClip('track-a2', {
          name: `SFX ${sfx.name.toUpperCase()}`,
          type: 'audio',
          audioBuffer: buffer,
          duration: buffer.duration
        });
        audioEngine.playCoin();
      });

      grid.appendChild(card);
    });
  }

  // =========================================================================
  // 5. 100 EFFECTS & FILTERS TAB (WITH SEARCH, CATEGORIES & TRANSITIONS)
  // =========================================================================
  function renderFxTab() {
    const wrap = document.createElement('div');
    wrap.innerHTML = `
      <div class="asset-section-title">
        <span>CATÁLOGO DE 100 EFECTOS Y FILTROS</span>
      </div>

      <!-- Search Bar -->
      <div class="fx-search-wrap" style="margin-bottom: 8px;">
        <input type="text" id="fx-search-input" class="pixel-input" placeholder="🔍 Buscar entre 100 efectos..." value="${effectSearchQuery}" style="width: 100%;" />
      </div>

      <!-- Category Filter Pills -->
      <div class="fx-category-pills" id="fx-category-pills"></div>

      <!-- Effects Count & Grid -->
      <div class="fx-status-row" id="fx-status-row" style="font-size: 8px; color: var(--text-dim); margin: 6px 0;"></div>
      <div class="asset-grid" id="fx-cards-grid"></div>

      <!-- Retro Transitions Section -->
      <div class="asset-section-title" style="margin-top: 20px;">
        <span>TRANSICIONES RETRO 8-BIT</span>
      </div>
      <div class="asset-grid" id="transitions-grid"></div>
    `;

    contentArea.appendChild(wrap);

    const searchInput = wrap.querySelector('#fx-search-input');
    const pillsContainer = wrap.querySelector('#fx-category-pills');
    const statusRow = wrap.querySelector('#fx-status-row');
    const cardsGrid = wrap.querySelector('#fx-cards-grid');
    const transGrid = wrap.querySelector('#transitions-grid');

    // Render Category Pills
    EFFECT_CATEGORIES.forEach(cat => {
      const btn = document.createElement('button');
      btn.className = `pixel-cat-pill ${activeEffectCategory === cat.id ? 'active' : ''}`;
      btn.textContent = `${cat.icon} ${cat.name}`;
      btn.addEventListener('click', () => {
        activeEffectCategory = cat.id;
        renderFxTab();
        audioEngine.playBeep(450, 'square', 0.03);
      });
      pillsContainer.appendChild(btn);
    });

    searchInput.addEventListener('input', (e) => {
      effectSearchQuery = e.target.value.toLowerCase().trim();
      filterAndRenderCards(cardsGrid, statusRow);
    });

    filterAndRenderCards(cardsGrid, statusRow);

    // Render Transitions
    const transitionsList = [
      { id: RetroTransitions.PIXEL_DISSOLVE, name: 'Disolvencia Pixel', icon: '🏁', desc: 'Disolución por bloques aleatorios' },
      { id: RetroTransitions.WIPE_HORIZONTAL, name: 'Cortinilla H', icon: '↔️', desc: 'Barrido lateral dentado 8-bit' },
      { id: RetroTransitions.WIPE_VERTICAL, name: 'Cortinilla V', icon: '↕️', desc: 'Barrido vertical hacia abajo' },
      { id: RetroTransitions.IRIS_CIRCLE, name: 'Círculo Iris', icon: '⭕', desc: 'Apertura circular estilo arcade' },
      { id: RetroTransitions.BLINDS, name: 'Persianas Pixel', icon: '📶', desc: 'Láminas horizontales retro' },
      { id: RetroTransitions.GLITCH_WIPE, name: 'Glitch Wipe', icon: '⚡', desc: 'Corte con aberración cromática' },
      { id: RetroTransitions.TV_TURNOFF, name: 'TV Turn-Off', icon: '📺', desc: 'Colapso de tubo catódico' }
    ];

    transitionsList.forEach(t => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.innerHTML = `
        <div class="asset-thumb">${t.icon}</div>
        <div class="asset-name">${t.name}</div>
        <div style="font-size: 7px; color: var(--text-dim);">${t.desc}</div>
      `;
      card.addEventListener('click', () => {
        const selected = timelineEngine.getSelectedClip();
        if (selected) {
          selected.transition = t.id;
          selected.transitionDuration = 0.8;
          timelineEngine.notify('clipupdated', { clip: selected });
          audioEngine.playCoin();
        } else {
          alert('Selecciona un clip en la línea de tiempo para asignarle esta transición.');
        }
      });
      transGrid.appendChild(card);
    });
  }

  function filterAndRenderCards(grid, statusRow) {
    grid.innerHTML = '';

    const filtered = ALL_EFFECTS.filter(fx => {
      const matchesCat = activeEffectCategory === 'all' || fx.category === activeEffectCategory;
      const matchesSearch = !effectSearchQuery ||
        fx.name.toLowerCase().includes(effectSearchQuery) ||
        fx.desc.toLowerCase().includes(effectSearchQuery) ||
        fx.id.toLowerCase().includes(effectSearchQuery);
      return matchesCat && matchesSearch;
    });

    statusRow.textContent = `MOSTRANDO ${filtered.length} DE 100 EFECTOS`;

    filtered.forEach(fx => {
      const card = document.createElement('div');
      card.className = 'asset-card fx-card';
      card.innerHTML = `
        <div class="asset-thumb" style="font-size: 22px;">${fx.icon}</div>
        <div class="asset-name" style="font-size: 8px;">${fx.name}</div>
        <div style="font-size: 7px; color: var(--text-dim); line-height: 1.2;">${fx.desc}</div>
      `;

      card.addEventListener('click', () => {
        const selected = timelineEngine.getSelectedClip();
        if (selected) {
          // Apply directly to selected clip
          selected.filter = fx.id;
          selected.filterIntensity = fx.defaultIntensity || 50;
          timelineEngine.notify('clipupdated', { clip: selected });
          audioEngine.playLaser();
        } else {
          // Add as full color overlay clip on V2
          timelineEngine.addClip('track-v2', {
            name: `FX: ${fx.name}`,
            type: 'color',
            color: 'transparent',
            filter: fx.id,
            filterIntensity: fx.defaultIntensity || 50,
            duration: 4.0
          });
          audioEngine.playPowerup();
        }
      });

      grid.appendChild(card);
    });
  }

  // Initial render
  renderTabContent();
}
