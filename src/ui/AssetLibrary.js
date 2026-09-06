/**
 * KIDCUT ASSET LIBRARY
 * Manages Media (with audio preservation), 100 Filters & Effects,
 * 8-Bit Text with animations, Pixel Stickers, Retro Transitions & Chiptune SFX.
 */

import { audioEngine } from '../engine/AudioEngine.js';
import { AudioExtractor } from '../engine/AudioExtractor.js';
import { 
  ALL_EFFECTS, 
  EFFECT_CATEGORIES, 
  ALL_TRANSITIONS, 
  TRANSITION_CATEGORIES, 
  ALL_AUDIO_EFFECTS, 
  AUDIO_EFFECT_CATEGORIES, 
  RetroTransitions, 
  TextAnimations 
} from '../engine/EffectsDatabase.js';
import { pickNativeFile, uploadMediaToDisk, getNativeApiBase } from '../engine/NativeBridge.js';

// Local store of imported media assets
const importedAssets = [];

export function setupAssetLibrary(timelineEngine) {
  const contentArea = document.getElementById('resource-content-area');
  const navTabs = document.querySelectorAll('.resource-nav .nav-tab');

  let activeTab = 'media';

  // Category and Search states
  let activeEffectCategory = 'all';
  let effectSearchQuery = '';

  let fxViewMode = 'effects'; // 'effects' | 'transitions'
  let activeTransitionCategory = 'all';
  let transitionSearchQuery = '';

  let activeAudioCategory = 'all';
  let audioSearchQuery = '';

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
        <button type="button" id="btn-native-picker" class="pixel-btn" style="margin-top: 10px; padding: 8px 12px; background: var(--color-gold); color: #000; font-family: 'Press Start 2P', monospace; font-size: 8px; border: 2px solid #fff; cursor: pointer; display: inline-flex; align-items: center; gap: 6px; box-shadow: 2px 2px 0px #000;">
          <span>📁</span>
          <span>EXPLORADOR NATIVO WINDOWS</span>
        </button>
        <button type="button" id="btn-native-picker-overlay" class="pixel-btn" style="margin-top: 6px; padding: 6px 10px; background: rgba(0, 240, 255, 0.15); color: var(--color-cyan); font-family: 'Press Start 2P', monospace; font-size: 7px; border: 2px solid var(--color-cyan); cursor: pointer; display: inline-flex; align-items: center; gap: 6px; box-shadow: 2px 2px 0px #000;" title="Importar medio y colocarlo directamente en la pista V2 de superposición en paralelo">
          <span>⚡</span>
          <span>IMPORTAR Y SUPERPONER (V2)</span>
        </button>
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
    const btnNative = wrap.querySelector('#btn-native-picker');
    const btnNativeOverlay = wrap.querySelector('#btn-native-picker-overlay');

    if (btnNative) {
      btnNative.addEventListener('click', async (e) => {
        e.stopPropagation();
        try {
          const data = await pickNativeFile();
          if (data && data.success && data.filePath) {
            await handleNativeFile(data.filePath, data.fileName, 'track-v1');
            return;
          }
          if (data && data.cancelled) return;
        } catch (_) {}
        fileInput.dataset.targetTrack = 'track-v1';
        fileInput.click();
      });
    }

    if (btnNativeOverlay) {
      btnNativeOverlay.addEventListener('click', async (e) => {
        e.stopPropagation();
        try {
          const data = await pickNativeFile();
          if (data && data.success && data.filePath) {
            await handleNativeFile(data.filePath, data.fileName, 'track-v2');
            return;
          }
          if (data && data.cancelled) return;
        } catch (_) {}
        fileInput.dataset.targetTrack = 'track-v2';
        fileInput.click();
      });
    }

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

  async function handleNativeFile(filePath, fileName, targetTrack = null) {
    const isVideo = /\.(mp4|webm|mov|mkv|avi)$/i.test(fileName);
    const isAudio = /\.(mp3|wav|ogg|aac|m4a|flac)$/i.test(fileName);
    const isImage = /\.(png|jpg|jpeg|gif|webp)$/i.test(fileName);
    const base = (await getNativeApiBase()) || '';

    const asset = {
      id: 'asset_' + Math.random().toString(36).substr(2, 9),
      name: fileName,
      type: isVideo ? 'video' : (isAudio ? 'audio' : 'image'),
      url: `${base}/api/file?path=${encodeURIComponent(filePath)}`,
      filePath: filePath,
      _nativePath: filePath,
      hasAudio: false,
      extractingAudio: false
    };

    if (isVideo) {
      const videoEl = document.createElement('video');
      videoEl.src = asset.url;
      videoEl.preload = 'auto';
      videoEl.onloadedmetadata = () => {
        asset.duration = videoEl.duration || 5.0;
        asset.mediaElement = videoEl;
        renderTabContent();
        if (targetTrack) {
          addAssetToTimeline(asset, targetTrack);
        }
      };
    } else if (isImage) {
      const imgEl = new Image();
      imgEl.src = asset.url;
      imgEl.onload = () => {
        asset.duration = 5.0;
        asset.mediaElement = imgEl;
        renderTabContent();
        if (targetTrack) {
          addAssetToTimeline(asset, targetTrack);
        }
      };
    } else if (isAudio) {
      asset.duration = 10.0;
      const audioEl = new Audio(asset.url);
      audioEl.onloadedmetadata = () => {
        asset.duration = audioEl.duration || 10.0;
        renderTabContent();
        if (targetTrack) {
          addAssetToTimeline(asset, targetTrack);
        }
      };
    }

    importedAssets.push(asset);
    renderTabContent();
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
        filePath: null,
        _nativePath: null,
        _uploadPromise: null,
        hasAudio: false,
        extractingAudio: false
      };

      // Immediately sync with local backend so FFmpeg has the file directly on disk
      asset._uploadPromise = uploadMediaToDisk(file, file.name)
        .then(data => {
          if (data && data.success && data.filePath) {
            asset.filePath = data.filePath;
            asset._nativePath = data.filePath;
            return data.filePath;
          }
          return null;
        })
        .catch(() => null);

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
      card.title = 'Haz doble clic o arrastra a la línea de tiempo';
      card.setAttribute('draggable', 'true');

      card.addEventListener('dragstart', (e) => {
        e.dataTransfer.setData('text/kidcut-asset-id', asset.id);
        e.dataTransfer.effectAllowed = 'copy';
      });

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
        <div class="asset-card-actions" style="display: flex; gap: 4px; margin-top: 6px; width: 100%;">
          <button type="button" class="btn-card-v1 retro-btn" style="flex: 1; font-size: 7px; padding: 3px 2px;" title="Añadir a Pista Principal (V1)">+ V1</button>
          ${asset.type !== 'audio' ? `
            <button type="button" class="btn-card-v2 retro-btn" style="flex: 1.2; font-size: 7px; padding: 3px 2px; color: var(--color-cyan); border-color: var(--color-cyan);" title="Superponer en Paralelo en Pista V2">⚡ SUPERPONER</button>
          ` : `
            <button type="button" class="btn-card-a2 retro-btn" style="flex: 1.2; font-size: 7px; padding: 3px 2px; color: var(--color-gold); border-color: var(--color-gold);" title="Añadir a Pista A2 (Efectos)">+ A2</button>
          `}
        </div>
      `;

      card.querySelector('.btn-card-v1').addEventListener('click', (e) => {
        e.stopPropagation();
        addAssetToTimeline(asset, asset.type === 'audio' ? 'track-a1' : 'track-v1');
      });

      const btnV2 = card.querySelector('.btn-card-v2');
      if (btnV2) {
        btnV2.addEventListener('click', (e) => {
          e.stopPropagation();
          addAssetToTimeline(asset, 'track-v2');
        });
      }

      const btnA2 = card.querySelector('.btn-card-a2');
      if (btnA2) {
        btnA2.addEventListener('click', (e) => {
          e.stopPropagation();
          addAssetToTimeline(asset, 'track-a2');
        });
      }

      card.addEventListener('dblclick', () => {
        // If there is already a clip on V1 at the playhead, automatically superpose onto V2 in parallel
        const v1Track = timelineEngine.tracks.find(t => t.id === 'track-v1');
        const hasV1ClipAtTime = v1Track && v1Track.clips.some(c => timelineEngine.currentTime >= c.start && timelineEngine.currentTime < c.start + c.duration);
        if (hasV1ClipAtTime && asset.type !== 'audio') {
          addAssetToTimeline(asset, 'track-v2');
        } else {
          addAssetToTimeline(asset, asset.type === 'audio' ? 'track-a1' : 'track-v1');
        }
      });

      grid.appendChild(card);
    });
  }

  // Global helper for drag-and-drop from TimelineView
  window.__kidcutGetAssetById = (id) => importedAssets.find(a => a.id === id);
  window.__kidcutAddAssetToTimeline = (asset, trackId, startTime) => addAssetToTimeline(asset, trackId, startTime);

  function addAssetToTimeline(asset, targetTrackId = 'track-v1', startTime = null) {
    const curTime = startTime !== null ? startTime : timelineEngine.currentTime;
    const dur = asset.duration || 5.0;

    if (asset.type === 'video') {
      const isOverlay = targetTrackId === 'track-v2';
      timelineEngine.pushState(`Añadir Video ${asset.name} (${isOverlay ? 'Overlay V2' : 'V1'})`);

      // 1. Add video clip to designated track (track-v1 or track-v2)
      const videoClip = timelineEngine.addClip(targetTrackId, {
        name: isOverlay ? `${asset.name} (OVERLAY)` : asset.name,
        type: 'video',
        start: curTime,
        duration: dur,
        mediaElement: asset.mediaElement,
        audioBuffer: asset.audioBuffer,
        file: asset.file,
        filePath: asset.filePath,
        _nativePath: asset.filePath || asset._nativePath,
        _assetRef: asset,
        _uploadPromise: asset._uploadPromise,
        scale: isOverlay ? 0.6 : 1.0,
        x: isOverlay ? 0.75 : 0.5,
        y: isOverlay ? 0.25 : 0.5
      }, true);

      // 2. Automatically link its original audio on Audio Track (A1 or A2) so user has full multitrack control
      if (asset.audioBuffer) {
        const audioTrackId = isOverlay ? 'track-a2' : 'track-a1';
        timelineEngine.addClip(audioTrackId, {
          name: `${asset.name} (AUDIO)`,
          type: 'audio',
          start: curTime,
          duration: dur,
          audioBuffer: asset.audioBuffer,
          file: asset.file,
          filePath: asset.filePath,
          _nativePath: asset.filePath || asset._nativePath,
          _assetRef: asset,
          _uploadPromise: asset._uploadPromise,
          sourceVideoClipId: videoClip ? videoClip.id : null
        }, true);
      }
    } else if (asset.type === 'image') {
      const isOverlay = targetTrackId === 'track-v2';
      timelineEngine.addClip(targetTrackId, {
        name: isOverlay ? `${asset.name} (OVERLAY)` : asset.name,
        type: 'image',
        start: curTime,
        duration: 5.0,
        mediaElement: asset.mediaElement,
        file: asset.file,
        filePath: asset.filePath,
        _nativePath: asset.filePath || asset._nativePath,
        _assetRef: asset,
        _uploadPromise: asset._uploadPromise,
        scale: isOverlay ? 0.6 : 1.0,
        x: isOverlay ? 0.75 : 0.5,
        y: isOverlay ? 0.25 : 0.5
      });
    } else if (asset.type === 'audio') {
      let targetTrack = timelineEngine.tracks.find(t => t.id === targetTrackId && t.type === 'audio') || timelineEngine.tracks.find(t => t.type === 'audio');
      if (!targetTrack) {
        targetTrack = timelineEngine.addTrack('audio');
      }
      timelineEngine.addClip(targetTrack.id, {
        name: asset.name,
        type: 'audio',
        start: curTime,
        duration: dur,
        audioBuffer: asset.audioBuffer,
        file: asset.file,
        filePath: asset.filePath,
        _nativePath: asset.filePath || asset._nativePath,
        _assetRef: asset,
        _uploadPromise: asset._uploadPromise
      });
    }

    audioEngine.playCoin();
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
  // 4. CHIPTUNE & 100 AUDIO DSP EFFECTS TAB
  // =========================================================================
  function renderChiptuneTab() {
    const wrap = document.createElement('div');
    wrap.className = 'tab-pane chiptune-pane';

    wrap.innerHTML = `
      <div class="asset-section-title">
        <span>SINTETIZADOR CHIPTUNE 8-BIT</span>
      </div>
      <div class="asset-grid" id="chiptune-grid"></div>

      <div class="asset-section-title" style="margin-top: 18px;">
        <span>CATÁLOGO DE 100 EFECTOS DE AUDIO & DSP</span>
      </div>

      <!-- Search Bar -->
      <div class="fx-search-wrap" style="margin-bottom: 8px;">
        <input type="text" id="audio-fx-search-input" class="pixel-input" placeholder="🔍 Buscar entre 100 efectos de audio..." value="${audioSearchQuery}" style="width: 100%;" />
      </div>

      <!-- Category Filter Pills -->
      <div class="fx-category-pills" id="audio-fx-category-pills"></div>

      <!-- Audio Effects Count & Grid -->
      <div class="fx-status-row" id="audio-fx-status-row" style="font-size: 8px; color: var(--text-dim); margin: 6px 0;"></div>
      <div class="asset-grid" id="audio-fx-cards-grid"></div>
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

    // 100 Audio Effects Category Pills
    const pillsContainer = wrap.querySelector('#audio-fx-category-pills');
    const searchInput = wrap.querySelector('#audio-fx-search-input');
    const statusRow = wrap.querySelector('#audio-fx-status-row');
    const cardsGrid = wrap.querySelector('#audio-fx-cards-grid');

    const allCatBtn = document.createElement('button');
    allCatBtn.className = `pixel-cat-pill ${activeAudioCategory === 'all' ? 'active' : ''}`;
    allCatBtn.textContent = '★ TODOS (100)';
    allCatBtn.addEventListener('click', () => {
      activeAudioCategory = 'all';
      renderChiptuneTab();
      audioEngine.playBeep(450, 'square', 0.03);
    });
    pillsContainer.appendChild(allCatBtn);

    AUDIO_EFFECT_CATEGORIES.forEach(cat => {
      const btn = document.createElement('button');
      btn.className = `pixel-cat-pill ${activeAudioCategory === cat.id ? 'active' : ''}`;
      btn.textContent = `${cat.icon} ${cat.name}`;
      btn.addEventListener('click', () => {
        activeAudioCategory = cat.id;
        renderChiptuneTab();
        audioEngine.playBeep(450, 'square', 0.03);
      });
      pillsContainer.appendChild(btn);
    });

    searchInput.addEventListener('input', (e) => {
      audioSearchQuery = e.target.value.toLowerCase().trim();
      filterAndRenderAudioCards(cardsGrid, statusRow);
    });

    filterAndRenderAudioCards(cardsGrid, statusRow);
  }

  function filterAndRenderAudioCards(grid, statusRow) {
    grid.innerHTML = '';
    const filtered = ALL_AUDIO_EFFECTS.filter(fx => {
      const matchesCat = activeAudioCategory === 'all' || fx.category === activeAudioCategory;
      const matchesSearch = !audioSearchQuery ||
        fx.label.toLowerCase().includes(audioSearchQuery) ||
        (fx.desc && fx.desc.toLowerCase().includes(audioSearchQuery)) ||
        fx.id.toLowerCase().includes(audioSearchQuery);
      return matchesCat && matchesSearch;
    });

    statusRow.textContent = `MOSTRANDO ${filtered.length} DE 100 EFECTOS DE AUDIO`;

    filtered.forEach(fx => {
      const card = document.createElement('div');
      card.className = 'asset-card fx-card';
      card.innerHTML = `
        <div class="asset-thumb" style="font-size: 20px;">${fx.icon}</div>
        <div class="asset-name" style="font-size: 8px;">${fx.label}</div>
        <div style="font-size: 7px; color: var(--text-dim); line-height: 1.2;">${fx.desc || ''}</div>
      `;

      card.addEventListener('click', () => {
        const selected = timelineEngine.getSelectedClip();
        if (selected) {
          selected.audioEffect = fx.id;
          timelineEngine.notify('clipupdated', { clip: selected });
          audioEngine.playCoin();
        } else {
          alert(`Selecciona un clip de audio o video en la línea de tiempo para asignarle el efecto "${fx.label}".`);
          audioEngine.playBeep(520, 'triangle', 0.08);
        }
      });

      grid.appendChild(card);
    });
  }

  // =========================================================================
  // 5. 100 VIDEO EFFECTS & 100 RETRO TRANSITIONS TAB
  // =========================================================================
  function renderFxTab() {
    const wrap = document.createElement('div');
    wrap.className = 'tab-pane fx-pane';

    wrap.innerHTML = `
      <!-- Sub-Nav Switcher between Video Effects and Transitions -->
      <div class="subnav-tabs">
        <button type="button" id="btn-subnav-effects" class="subnav-btn ${fxViewMode === 'effects' ? 'active' : ''}">
          ✨ 100 EFECTOS DE VIDEO
        </button>
        <button type="button" id="btn-subnav-transitions" class="subnav-btn ${fxViewMode === 'transitions' ? 'active' : ''}">
          🏁 100 TRANSICIONES RETRO
        </button>
      </div>

      <div class="asset-section-title">
        <span>${fxViewMode === 'effects' ? 'CATÁLOGO DE 100 EFECTOS Y FILTROS' : 'CATÁLOGO DE 100 TRANSICIONES PROCEDURALES'}</span>
      </div>

      <!-- Search Bar -->
      <div class="fx-search-wrap" style="margin-bottom: 8px;">
        <input type="text" id="fx-search-input" class="pixel-input" placeholder="${fxViewMode === 'effects' ? '🔍 Buscar entre 100 efectos...' : '🔍 Buscar entre 100 transiciones...'}" value="${fxViewMode === 'effects' ? effectSearchQuery : transitionSearchQuery}" style="width: 100%;" />
      </div>

      <!-- Category Filter Pills -->
      <div class="fx-category-pills" id="fx-category-pills"></div>

      <!-- Items Count & Grid -->
      <div class="fx-status-row" id="fx-status-row" style="font-size: 8px; color: var(--text-dim); margin: 6px 0;"></div>
      <div class="asset-grid" id="fx-cards-grid"></div>
    `;

    contentArea.appendChild(wrap);

    const btnSubnavEffects = wrap.querySelector('#btn-subnav-effects');
    const btnSubnavTrans = wrap.querySelector('#btn-subnav-transitions');
    const searchInput = wrap.querySelector('#fx-search-input');
    const pillsContainer = wrap.querySelector('#fx-category-pills');
    const statusRow = wrap.querySelector('#fx-status-row');
    const cardsGrid = wrap.querySelector('#fx-cards-grid');

    btnSubnavEffects.addEventListener('click', () => {
      fxViewMode = 'effects';
      renderFxTab();
      audioEngine.playBeep(420, 'square', 0.03);
    });

    btnSubnavTrans.addEventListener('click', () => {
      fxViewMode = 'transitions';
      renderFxTab();
      audioEngine.playBeep(520, 'square', 0.03);
    });

    if (fxViewMode === 'effects') {
      // Category Pills for Video Effects
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
        filterAndRenderVideoCards(cardsGrid, statusRow);
      });

      filterAndRenderVideoCards(cardsGrid, statusRow);
    } else {
      // Category Pills for Transitions
      const allCatBtn = document.createElement('button');
      allCatBtn.className = `pixel-cat-pill ${activeTransitionCategory === 'all' ? 'active' : ''}`;
      allCatBtn.textContent = '★ TODAS (100)';
      allCatBtn.addEventListener('click', () => {
        activeTransitionCategory = 'all';
        renderFxTab();
        audioEngine.playBeep(450, 'square', 0.03);
      });
      pillsContainer.appendChild(allCatBtn);

      TRANSITION_CATEGORIES.forEach(cat => {
        const btn = document.createElement('button');
        btn.className = `pixel-cat-pill ${activeTransitionCategory === cat.id ? 'active' : ''}`;
        btn.textContent = `${cat.icon} ${cat.name}`;
        btn.addEventListener('click', () => {
          activeTransitionCategory = cat.id;
          renderFxTab();
          audioEngine.playBeep(450, 'square', 0.03);
        });
        pillsContainer.appendChild(btn);
      });

      searchInput.addEventListener('input', (e) => {
        transitionSearchQuery = e.target.value.toLowerCase().trim();
        filterAndRenderTransitionCards(cardsGrid, statusRow);
      });

      filterAndRenderTransitionCards(cardsGrid, statusRow);
    }
  }

  function filterAndRenderVideoCards(grid, statusRow) {
    grid.innerHTML = '';

    const filtered = ALL_EFFECTS.filter(fx => {
      const matchesCat = activeEffectCategory === 'all' || fx.category === activeEffectCategory;
      const matchesSearch = !effectSearchQuery ||
        fx.name.toLowerCase().includes(effectSearchQuery) ||
        fx.desc.toLowerCase().includes(effectSearchQuery) ||
        fx.id.toLowerCase().includes(effectSearchQuery);
      return matchesCat && matchesSearch;
    });

    statusRow.textContent = `MOSTRANDO ${filtered.length} DE 100 EFECTOS DE VIDEO`;

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
          selected.filter = fx.id;
          selected.filterIntensity = fx.defaultIntensity || 50;
          timelineEngine.notify('clipupdated', { clip: selected });
          audioEngine.playLaser();
        } else {
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

  function filterAndRenderTransitionCards(grid, statusRow) {
    grid.innerHTML = '';

    const filtered = ALL_TRANSITIONS.filter(t => {
      const matchesCat = activeTransitionCategory === 'all' || t.category === activeTransitionCategory;
      const matchesSearch = !transitionSearchQuery ||
        t.name.toLowerCase().includes(transitionSearchQuery) ||
        t.id.toLowerCase().includes(transitionSearchQuery);
      return matchesCat && matchesSearch;
    });

    statusRow.textContent = `MOSTRANDO ${filtered.length} DE 100 TRANSICIONES RETRO`;

    filtered.forEach(t => {
      const card = document.createElement('div');
      card.className = 'asset-card';
      card.innerHTML = `
        <div class="asset-thumb">${t.icon}</div>
        <div class="asset-name">${t.name}</div>
        <div style="font-size: 7px; color: var(--color-cyan); margin-top: 3px;">CLIC: ASIGNAR A CLIP</div>
      `;

      card.addEventListener('click', () => {
        const selected = timelineEngine.getSelectedClip();
        if (selected) {
          selected.transition = t.id;
          selected.transitionDuration = 0.8;
          timelineEngine.notify('clipupdated', { clip: selected });
          audioEngine.playCoin();
        } else {
          alert(`Selecciona un clip en la línea de tiempo para asignarle la transición "${t.name}".`);
          audioEngine.playBeep(440, 'triangle', 0.08);
        }
      });

      grid.appendChild(card);
    });
  }

  // Initial render
  renderTabContent();
}
