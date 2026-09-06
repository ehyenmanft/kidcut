import { 
  ALL_EFFECTS, 
  EFFECT_CATEGORIES, 
  ALL_TRANSITIONS, 
  TRANSITION_CATEGORIES, 
  AUDIO_EFFECT_CATEGORIES, 
  TextAnimations 
} from '../engine/EffectsDatabase.js';
import { AUDIO_EFFECTS } from '../engine/AudioEngine.js';

export function setupInspectorView(timelineEngine) {
  const container = document.getElementById('inspector-content');
  const badge = document.getElementById('selected-clip-badge');

  function renderInspector(clip) {
    if (!clip) {
      badge.textContent = 'NINGUNO';
      container.innerHTML = `
        <div class="empty-inspector-msg">
          <div class="pixel-ghost">👾</div>
          <p>SELECCIONA UN CLIP EN LA LÍNEA DE TIEMPO O EN PANTALLA PARA EDITARLO</p>
        </div>
      `;
      return;
    }

    badge.textContent = clip.type.toUpperCase();

    // Group 100 Video Effects by category for select dropdown
    let effectOptionsHtml = `<option value="none">-- SIN FILTRO --</option>`;
    EFFECT_CATEGORIES.filter(c => c.id !== 'all').forEach(cat => {
      const catEffects = ALL_EFFECTS.filter(fx => fx.category === cat.id);
      if (catEffects.length > 0) {
        effectOptionsHtml += `<optgroup label="${cat.icon} ${cat.name}">`;
        catEffects.forEach(fx => {
          const isSelected = clip.filter === fx.id ? 'selected' : '';
          effectOptionsHtml += `<option value="${fx.id}" ${isSelected}>${fx.icon} ${fx.name}</option>`;
        });
        effectOptionsHtml += `</optgroup>`;
      }
    });

    // Group 100 Audio Effects by category for select dropdown
    let audioEffectOptionsHtml = `<option value="none">-- SIN EFECTO --</option>`;
    AUDIO_EFFECT_CATEGORIES.forEach(cat => {
      const catEffects = AUDIO_EFFECTS.filter(fx => fx.category === cat.id);
      if (catEffects.length > 0) {
        audioEffectOptionsHtml += `<optgroup label="${cat.icon} ${cat.name}">`;
        catEffects.forEach(fx => {
          const isSelected = (clip.audioEffect || 'none') === fx.id ? 'selected' : '';
          audioEffectOptionsHtml += `<option value="${fx.id}" ${isSelected}>${fx.icon} ${fx.label}</option>`;
        });
        audioEffectOptionsHtml += `</optgroup>`;
      }
    });

    // Text Animations dropdown
    let textAnimOptionsHtml = '';
    const anims = [
      { id: 'none', label: '-- NINGUNA --' },
      { id: 'arcade_blink', label: '✨ Arcade Blink' },
      { id: 'typewriter', label: '⌨️ Typewriter Retro' },
      { id: 'wave_float', label: '〰️ Wave Float' },
      { id: 'glitch_shake', label: '⚡ Glitch Shake' },
      { id: 'rainbow_cycle', label: '🌈 Rainbow Cycle' },
      { id: 'pop_scale', label: '💥 Pop Bounce' }
    ];
    anims.forEach(a => {
      const isSel = clip.textAnimation === a.id ? 'selected' : '';
      textAnimOptionsHtml += `<option value="${a.id}" ${isSel}>${a.label}</option>`;
    });

    // Group 100 Retro Transitions by category for select dropdown
    let transOptionsHtml = `<option value="none">-- NINGUNA --</option>`;
    TRANSITION_CATEGORIES.forEach(cat => {
      const catTrans = ALL_TRANSITIONS.filter(t => t.category === cat.id);
      if (catTrans.length > 0) {
        transOptionsHtml += `<optgroup label="${cat.icon} ${cat.name}">`;
        catTrans.forEach(t => {
          const isSel = clip.transition === t.id ? 'selected' : '';
          transOptionsHtml += `<option value="${t.id}" ${isSel}>${t.icon} ${t.name}</option>`;
        });
        transOptionsHtml += `</optgroup>`;
      }
    });

    const clipTrack = timelineEngine.tracks.find(t => t.clips.some(c => c.id === clip.id));
    const isV2 = clipTrack && clipTrack.id === 'track-v2';
    const isVisual = clip.type !== 'audio';

    container.innerHTML = `
      <!-- General Properties -->
      <div class="inspector-group">
        <div class="group-title">INFORMACIÓN DEL CLIP</div>
        <div class="prop-row">
          <span>NOMBRE:</span>
          <input type="text" id="prop-clip-name" class="prop-input" style="width: 140px; text-align: left;" value="${clip.name}" />
        </div>
        <div class="prop-row">
          <span>PISTA:</span>
          <span style="color: ${isV2 ? 'var(--color-cyan)' : 'var(--color-gold)'}; font-weight: bold; font-size: 8px;">${clipTrack ? clipTrack.name : 'V1 (PRINCIPAL)'}</span>
        </div>
        ${isVisual ? `
          <div class="prop-row" style="margin-top: 4px;">
            ${!isV2 ? `
              <button type="button" id="btn-inspector-toggle-track" class="retro-btn" style="width: 100%; font-size: 7px; padding: 4px; color: var(--color-cyan); border-color: var(--color-cyan); background: rgba(0, 240, 255, 0.1);" title="Mover clip a la pista V2 de superposición para reproducir en paralelo">
                ⚡ SUPERPONER A V2 (PARALELO)
              </button>
            ` : `
              <button type="button" id="btn-inspector-toggle-track" class="retro-btn" style="width: 100%; font-size: 7px; padding: 4px; color: var(--color-gold); border-color: var(--color-gold); background: rgba(255, 210, 0, 0.1);" title="Bajar clip a la pista principal V1">
                ⬇ BAJAR A V1 (PRINCIPAL)
              </button>
            `}
          </div>
        ` : ''}
        <div class="prop-row">
          <span>INICIO:</span>
          <span style="color: var(--color-gold); font-family: var(--font-digits); font-size: 16px;">${clip.start.toFixed(2)}s</span>
        </div>
        <div class="prop-row">
          <span>DURACIÓN:</span>
          <div style="display: flex; align-items: center; gap: 4px;">
            <button type="button" id="btn-inspector-dur-minus" class="pixel-tool-btn" style="padding: 1px 6px; font-size: 10px; min-width: 22px;" title="Reducir duración (-0.5s)">-</button>
            <span id="label-clip-duration" style="color: var(--color-cyan); font-family: var(--font-digits); font-size: 16px; min-width: 44px; text-align: center;">${clip.duration.toFixed(2)}s</span>
            <button type="button" id="btn-inspector-dur-plus" class="pixel-tool-btn" style="padding: 1px 6px; font-size: 10px; min-width: 22px;" title="Ampliar duración (+0.5s)">+</button>
          </div>
        </div>
      </div>

      <!-- Text Specific Properties -->
      ${clip.type === 'text' ? `
        <div class="inspector-group">
          <div class="group-title">TEXTO Y ANIMACIÓN 8-BIT</div>
          <div class="prop-row" style="flex-direction: column; align-items: flex-start; gap: 4px;">
            <span>CONTENIDO:</span>
            <input type="text" id="prop-text-content" class="prop-input" style="width: 100%; text-align: left;" value="${clip.text || ''}" />
          </div>
          <div class="prop-row">
            <span>ANIMACIÓN:</span>
            <select id="prop-text-anim" class="prop-select" style="width: 150px;">
              ${textAnimOptionsHtml}
            </select>
          </div>
          <div class="prop-row">
            <span>TAMAÑO:</span>
            <input type="range" id="prop-font-size" class="prop-slider" min="16" max="140" value="${clip.fontSize || 48}" />
            <span id="label-font-size" style="font-size: 8px;">${clip.fontSize || 48}px</span>
          </div>
          <div class="prop-row">
            <span>COLOR:</span>
            <input type="color" id="prop-text-color" value="${clip.textColor || '#ffd200'}" style="background: transparent; border: none; cursor: pointer;" />
          </div>
          <div class="prop-row">
            <span>BORDE:</span>
            <input type="color" id="prop-stroke-color" value="${clip.strokeColor || '#000000'}" style="background: transparent; border: none; cursor: pointer;" />
          </div>
        </div>
      ` : ''}

      <!-- Transform Controls (Visual Clips) -->
      ${clip.type !== 'audio' ? `
        <div class="inspector-group">
          <div class="group-title">TRANSFORMACIÓN EN PANTALLA</div>
          <div style="font-size: 7px; color: var(--color-cyan); margin-bottom: 6px;">★ Puedes mover y escalar arrastrando en la pantalla</div>
          <div class="prop-row">
            <span>POSICIÓN X:</span>
            <input type="range" id="prop-pos-x" class="prop-slider" min="-0.2" max="1.2" step="0.01" value="${clip.x !== undefined ? clip.x : 0.5}" />
            <span id="label-pos-x" style="font-size: 8px;">${Math.round((clip.x !== undefined ? clip.x : 0.5) * 100)}%</span>
          </div>
          <div class="prop-row">
            <span>POSICIÓN Y:</span>
            <input type="range" id="prop-pos-y" class="prop-slider" min="-0.2" max="1.2" step="0.01" value="${clip.y !== undefined ? clip.y : 0.5}" />
            <span id="label-pos-y" style="font-size: 8px;">${Math.round((clip.y !== undefined ? clip.y : 0.5) * 100)}%</span>
          </div>
          <div class="prop-row">
            <span>ESCALA:</span>
            <input type="range" id="prop-scale" class="prop-slider" min="0.1" max="4.0" step="0.05" value="${clip.scale !== undefined ? clip.scale : 1.0}" />
            <span id="label-scale" style="font-size: 8px;">${Math.round((clip.scale || 1.0) * 100)}%</span>
          </div>
          <div class="prop-row">
            <span>ROTACIÓN:</span>
            <input type="range" id="prop-rotation" class="prop-slider" min="-180" max="180" step="1" value="${clip.rotation || 0}" />
            <span id="label-rotation" style="font-size: 8px;">${clip.rotation || 0}°</span>
          </div>
          <div class="prop-row">
            <span>OPACIDAD:</span>
            <input type="range" id="prop-opacity" class="prop-slider" min="0" max="1" step="0.05" value="${clip.opacity !== undefined ? clip.opacity : 1.0}" />
            <span id="label-opacity" style="font-size: 8px;">${Math.round((clip.opacity !== undefined ? clip.opacity : 1.0) * 100)}%</span>
          </div>

          ${isV2 ? `
            <div class="prop-row" style="flex-direction: column; align-items: flex-start; gap: 4px; margin-top: 8px; padding-top: 6px; border-top: 1px dashed var(--border-color);">
              <span style="font-size: 7px; color: var(--color-cyan); font-weight: bold;">PRESETS PIP (SUPERPOSICIÓN):</span>
              <div style="display: flex; gap: 4px; width: 100%; margin-top: 2px;">
                <button type="button" class="retro-btn btn-pip-preset" data-pip="top-right" style="flex: 1; font-size: 6px; padding: 4px 1px;" title="Esquina Superior Derecha">↗ SUP DER</button>
                <button type="button" class="retro-btn btn-pip-preset" data-pip="bottom-right" style="flex: 1; font-size: 6px; padding: 4px 1px;" title="Esquina Inferior Derecha">↘ INF DER</button>
                <button type="button" class="retro-btn btn-pip-preset" data-pip="center-50" style="flex: 1; font-size: 6px; padding: 4px 1px;" title="50% Centrado">⊞ 50%</button>
                <button type="button" class="retro-btn btn-pip-preset" data-pip="full-100" style="flex: 1; font-size: 6px; padding: 4px 1px;" title="100% Pantalla Completa">⬜ 100%</button>
              </div>
            </div>
          ` : ''}
        </div>

        <!-- 100 Effects & Filters Selector -->
        <div class="inspector-group">
          <div class="group-title">FILTRO / EFECTO RETRO (100)</div>
          <div class="prop-row" style="flex-direction: column; align-items: flex-start; gap: 4px;">
            <span>SELECCIONAR EFECTO:</span>
            <select id="prop-filter-select" class="prop-select" style="width: 100%;">
              ${effectOptionsHtml}
            </select>
          </div>
          <div class="prop-row" id="row-filter-intensity" style="margin-top: 6px;">
            <span>INTENSIDAD:</span>
            <input type="range" id="prop-filter-intensity" class="prop-slider" min="5" max="100" value="${clip.filterIntensity || 50}" />
            <span id="label-filter-intensity" style="font-size: 8px;">${clip.filterIntensity || 50}%</span>
          </div>
        </div>

        <!-- Retro Transitions -->
        <div class="inspector-group">
          <div class="group-title">TRANSICIÓN DE ENTRADA</div>
          <div class="prop-row" style="flex-direction: column; align-items: flex-start; gap: 4px;">
            <span>TIPO DE TRANSICIÓN:</span>
            <select id="prop-transition-select" class="prop-select" style="width: 100%;">
              ${transOptionsHtml}
            </select>
          </div>
          <div class="prop-row" style="margin-top: 6px;">
            <span>DURACIÓN:</span>
            <input type="range" id="prop-trans-dur" class="prop-slider" min="0.2" max="2.5" step="0.1" value="${clip.transitionDuration || 0.8}" />
            <span id="label-trans-dur" style="font-size: 8px;">${(clip.transitionDuration || 0.8).toFixed(1)}s</span>
          </div>
        </div>
      ` : ''}

      <!-- Video Audio Track Extraction -->
      ${clip.type === 'video' && clip.audioBuffer ? `
        <div class="inspector-group">
          <div class="group-title">PISTA DE AUDIO DEL VIDEO</div>
          <div class="prop-row" style="flex-direction: column; align-items: stretch; gap: 6px;">
            <button id="btn-extract-audio" class="pixel-btn arcade-gold" style="font-size: 8px; padding: 8px 10px;">🎵 EXTRAER AUDIO EN PISTA INDEPENDIENTE</button>
            <div style="font-size: 7px; color: var(--color-cyan);">Desacopla el sonido en una pista de audio para recortar, aplicar efectos y volumen sin alterar el video.</div>
          </div>
        </div>
      ` : ''}

      <!-- Audio / Speed Controls (for audio clips or visual clips with audio) -->
      ${(clip.type === 'audio' || clip.audioBuffer || clip.type === 'video') ? `
        <div class="inspector-group">
          <div class="group-title">AUDIO & REPRODUCCIÓN</div>
          <div class="prop-row">
            <span>VOLUMEN:</span>
            <input type="range" id="prop-volume" class="prop-slider" min="0" max="3" step="0.05" value="${clip.volume !== undefined ? clip.volume : 1.0}" />
            <span id="label-volume" style="font-size: 8px;">${Math.round((clip.volume !== undefined ? clip.volume : 1.0) * 100)}%</span>
          </div>
          <div class="prop-row">
            <span>ESTADO:</span>
            <button id="btn-toggle-mute" class="pixel-btn ${clip.isMuted ? 'active' : ''}" style="font-size: 8px; padding: 4px 8px;">${clip.isMuted ? '🔇 SILENCIADO' : '🔊 ACTIVO'}</button>
          </div>
          <div class="prop-row">
            <span>VELOCIDAD:</span>
            <input type="range" id="prop-speed" class="prop-slider" min="0.25" max="3.0" step="0.25" value="${clip.speed || 1.0}" />
            <span id="label-speed" style="font-size: 8px;">${clip.speed || 1.0}x</span>
          </div>
          <div class="prop-row">
            <span>FADE IN:</span>
            <input type="range" id="prop-fade-in" class="prop-slider" min="0" max="3" step="0.1" value="${clip.fadeIn || 0}" />
            <span id="label-fade-in" style="font-size: 8px;">${(clip.fadeIn || 0).toFixed(1)}s</span>
          </div>
          <div class="prop-row">
            <span>FADE OUT:</span>
            <input type="range" id="prop-fade-out" class="prop-slider" min="0" max="3" step="0.1" value="${clip.fadeOut || 0}" />
            <span id="label-fade-out" style="font-size: 8px;">${(clip.fadeOut || 0).toFixed(1)}s</span>
          </div>
          <div class="prop-row" style="flex-direction: column; align-items: flex-start; gap: 4px;">
            <span>EFECTO DE AUDIO RETRO:</span>
            <select id="prop-audio-effect" class="prop-select" style="width: 100%;">
              ${audioEffectOptionsHtml}
            </select>
          </div>
          <div class="prop-row" id="row-audio-effect-intensity" style="margin-top: 4px; ${(!clip.audioEffect || clip.audioEffect === 'none') ? 'display: none;' : ''}">
            <span>INTENSIDAD:</span>
            <input type="range" id="prop-audio-effect-intensity" class="prop-slider" min="10" max="100" value="${clip.effectIntensity !== undefined ? clip.effectIntensity : 50}" />
            <span id="label-audio-effect-intensity" style="font-size: 8px;">${clip.effectIntensity !== undefined ? clip.effectIntensity : 50}%</span>
          </div>
        </div>

        <!-- Trim & Precision Editing Controls -->
        <div class="inspector-group">
          <div class="group-title">RECORTE Y DURACIÓN</div>
          <div class="prop-row">
            <span>INICIO DE RECORTE:</span>
            <input type="number" id="prop-trim-in" class="prop-input" style="width: 70px;" min="0" step="0.1" value="${(clip.trimIn || 0).toFixed(2)}" />
            <span style="font-size: 8px; color: var(--color-cyan);">seg</span>
          </div>
          <div class="prop-row">
            <span>DURACIÓN EN TIMELINE:</span>
            <input type="number" id="prop-clip-dur" class="prop-input" style="width: 70px;" min="0.2" step="0.1" value="${clip.duration.toFixed(2)}" />
            <span style="font-size: 8px; color: var(--color-gold);">seg</span>
          </div>
        </div>
      ` : ''}
    `;

    bindInspectorEvents(clip);
  }

  function bindInspectorEvents(clip) {
    const nameInput = document.getElementById('prop-clip-name');
    if (nameInput) {
      nameInput.addEventListener('input', (e) => {
        clip.name = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    // Extract Audio from Video Clip Button
    const extractAudioBtn = document.getElementById('btn-extract-audio');
    if (extractAudioBtn) {
      extractAudioBtn.addEventListener('click', () => {
        timelineEngine.extractAudioFromClip(clip.id);
      });
    }

    // Text bindings
    const textContent = document.getElementById('prop-text-content');
    if (textContent) {
      textContent.addEventListener('input', (e) => {
        clip.text = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const textAnim = document.getElementById('prop-text-anim');
    if (textAnim) {
      textAnim.addEventListener('change', (e) => {
        clip.textAnimation = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const fontSize = document.getElementById('prop-font-size');
    const labelFontSize = document.getElementById('label-font-size');
    if (fontSize) {
      fontSize.addEventListener('input', (e) => {
        clip.fontSize = parseInt(e.target.value);
        if (labelFontSize) labelFontSize.textContent = `${clip.fontSize}px`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const textColor = document.getElementById('prop-text-color');
    if (textColor) {
      textColor.addEventListener('input', (e) => {
        clip.textColor = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const strokeColor = document.getElementById('prop-stroke-color');
    if (strokeColor) {
      strokeColor.addEventListener('input', (e) => {
        clip.strokeColor = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    // Track toggle button
    const btnToggleTrack = document.getElementById('btn-inspector-toggle-track');
    if (btnToggleTrack) {
      btnToggleTrack.addEventListener('click', () => {
        const targetId = isV2 ? 'track-v1' : 'track-v2';
        timelineEngine.moveClipToTrack(clip.id, targetId);
        renderInspector(clip);
      });
    }

    // PIP Presets
    container.querySelectorAll('.btn-pip-preset').forEach(btn => {
      btn.addEventListener('click', () => {
        const mode = btn.dataset.pip;
        timelineEngine.pushState(`Preset PIP: ${mode}`);
        if (mode === 'top-right') {
          clip.scale = 0.35;
          clip.x = 0.8;
          clip.y = 0.2;
        } else if (mode === 'bottom-right') {
          clip.scale = 0.35;
          clip.x = 0.8;
          clip.y = 0.8;
        } else if (mode === 'center-50') {
          clip.scale = 0.5;
          clip.x = 0.5;
          clip.y = 0.5;
        } else if (mode === 'full-100') {
          clip.scale = 1.0;
          clip.x = 0.5;
          clip.y = 0.5;
        }
        timelineEngine.notify('clipupdated', { clip });
        renderInspector(clip);
      });
    });

    // Transform bindings
    const posX = document.getElementById('prop-pos-x');
    const labelPosX = document.getElementById('label-pos-x');
    if (posX) {
      posX.addEventListener('input', (e) => {
        clip.x = parseFloat(e.target.value);
        if (labelPosX) labelPosX.textContent = `${Math.round(clip.x * 100)}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const posY = document.getElementById('prop-pos-y');
    const labelPosY = document.getElementById('label-pos-y');
    if (posY) {
      posY.addEventListener('input', (e) => {
        clip.y = parseFloat(e.target.value);
        if (labelPosY) labelPosY.textContent = `${Math.round(clip.y * 100)}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const scale = document.getElementById('prop-scale');
    const labelScale = document.getElementById('label-scale');
    if (scale) {
      scale.addEventListener('input', (e) => {
        clip.scale = parseFloat(e.target.value);
        if (labelScale) labelScale.textContent = `${Math.round(clip.scale * 100)}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const rotation = document.getElementById('prop-rotation');
    const labelRotation = document.getElementById('label-rotation');
    if (rotation) {
      rotation.addEventListener('input', (e) => {
        clip.rotation = parseInt(e.target.value);
        if (labelRotation) labelRotation.textContent = `${clip.rotation}°`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const opacity = document.getElementById('prop-opacity');
    const labelOpacity = document.getElementById('label-opacity');
    if (opacity) {
      opacity.addEventListener('input', (e) => {
        clip.opacity = parseFloat(e.target.value);
        if (labelOpacity) labelOpacity.textContent = `${Math.round(clip.opacity * 100)}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    // Filter bindings
    const filterSelect = document.getElementById('prop-filter-select');
    if (filterSelect) {
      filterSelect.addEventListener('change', (e) => {
        timelineEngine.pushState('Cambiar Filtro');
        clip.filter = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const filterIntensity = document.getElementById('prop-filter-intensity');
    const labelIntensity = document.getElementById('label-filter-intensity');
    if (filterIntensity) {
      filterIntensity.addEventListener('change', () => timelineEngine.pushState('Intensidad Filtro'));
      filterIntensity.addEventListener('input', (e) => {
        clip.filterIntensity = parseInt(e.target.value);
        if (labelIntensity) labelIntensity.textContent = `${clip.filterIntensity}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    // Transition bindings
    const transSelect = document.getElementById('prop-transition-select');
    if (transSelect) {
      transSelect.addEventListener('change', (e) => {
        timelineEngine.pushState('Cambiar Transición');
        clip.transition = e.target.value;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const transDur = document.getElementById('prop-trans-dur');
    const labelTransDur = document.getElementById('label-trans-dur');
    if (transDur) {
      transDur.addEventListener('change', () => timelineEngine.pushState('Duración Transición'));
      transDur.addEventListener('input', (e) => {
        clip.transitionDuration = parseFloat(e.target.value);
        if (labelTransDur) labelTransDur.textContent = `${clip.transitionDuration.toFixed(1)}s`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    // Audio Controls bindings
    const volume = document.getElementById('prop-volume');
    const labelVolume = document.getElementById('label-volume');
    if (volume) {
      volume.addEventListener('change', () => timelineEngine.pushState('Ajustar Volumen'));
      volume.addEventListener('input', (e) => {
        clip.volume = parseFloat(e.target.value);
        if (labelVolume) labelVolume.textContent = `${Math.round(clip.volume * 100)}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const muteBtn = document.getElementById('btn-toggle-mute');
    if (muteBtn) {
      muteBtn.addEventListener('click', () => {
        clip.isMuted = !clip.isMuted;
        muteBtn.textContent = clip.isMuted ? '🔇 SILENCIADO' : '🔊 ACTIVO';
        muteBtn.classList.toggle('active', clip.isMuted);
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const speed = document.getElementById('prop-speed');
    const labelSpeed = document.getElementById('label-speed');
    if (speed) {
      speed.addEventListener('change', () => timelineEngine.pushState('Ajustar Velocidad'));
      speed.addEventListener('input', (e) => {
        clip.speed = parseFloat(e.target.value);
        if (labelSpeed) labelSpeed.textContent = `${clip.speed}x`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const fadeIn = document.getElementById('prop-fade-in');
    const labelFadeIn = document.getElementById('label-fade-in');
    if (fadeIn) {
      fadeIn.addEventListener('change', () => timelineEngine.pushState('Ajustar Fade In'));
      fadeIn.addEventListener('input', (e) => {
        clip.fadeIn = parseFloat(e.target.value);
        if (labelFadeIn) labelFadeIn.textContent = `${clip.fadeIn.toFixed(1)}s`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const fadeOut = document.getElementById('prop-fade-out');
    const labelFadeOut = document.getElementById('label-fade-out');
    if (fadeOut) {
      fadeOut.addEventListener('change', () => timelineEngine.pushState('Ajustar Fade Out'));
      fadeOut.addEventListener('input', (e) => {
        clip.fadeOut = parseFloat(e.target.value);
        if (labelFadeOut) labelFadeOut.textContent = `${clip.fadeOut.toFixed(1)}s`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const audioEffect = document.getElementById('prop-audio-effect');
    const rowEffectIntensity = document.getElementById('row-audio-effect-intensity');
    if (audioEffect) {
      audioEffect.addEventListener('change', (e) => {
        timelineEngine.pushState('Cambiar Efecto de Audio');
        clip.audioEffect = e.target.value;
        if (rowEffectIntensity) {
          rowEffectIntensity.style.display = (!clip.audioEffect || clip.audioEffect === 'none') ? 'none' : 'flex';
        }
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    const effectIntensity = document.getElementById('prop-audio-effect-intensity');
    const labelEffectIntensity = document.getElementById('label-audio-effect-intensity');
    if (effectIntensity) {
      effectIntensity.addEventListener('change', () => timelineEngine.pushState('Intensidad Efecto Audio'));
      effectIntensity.addEventListener('input', (e) => {
        clip.effectIntensity = parseInt(e.target.value);
        if (labelEffectIntensity) labelEffectIntensity.textContent = `${clip.effectIntensity}%`;
        timelineEngine.notify('clipupdated', { clip });
      });
    }

    // Trim In & Duration precision bindings
    const trimInInput = document.getElementById('prop-trim-in');
    if (trimInInput) {
      trimInInput.addEventListener('change', (e) => {
        timelineEngine.pushState('Ajustar Trim In');
        clip.trimIn = Math.max(0, parseFloat(e.target.value) || 0);
        timelineEngine.calculateTotalDuration();
        timelineEngine.notify('trackschange', { tracks: timelineEngine.tracks });
      });
    }

    const durInput = document.getElementById('prop-clip-dur');
    if (durInput) {
      durInput.addEventListener('change', (e) => {
        timelineEngine.pushState('Ajustar Duración');
        clip.duration = Math.max(0.15, parseFloat(e.target.value) || 0.15);
        timelineEngine.calculateTotalDuration();
        timelineEngine.notify('trackschange', { tracks: timelineEngine.tracks });
      });
    }

    const durMinus = document.getElementById('btn-inspector-dur-minus');
    const durPlus = document.getElementById('btn-inspector-dur-plus');
    if (durMinus) {
      durMinus.addEventListener('click', () => {
        timelineEngine.pushState('Reducir Duración');
        timelineEngine.setClipTrim(clip.id, 'right', Math.max(0.15, clip.duration - 0.5));
      });
    }
    if (durPlus) {
      durPlus.addEventListener('click', () => {
        timelineEngine.pushState('Ampliar Duración');
        timelineEngine.setClipTrim(clip.id, 'right', clip.duration + 0.5);
      });
    }
  }

  // Subscribe to timeline changes
  timelineEngine.subscribe((event, data) => {
    if (event === 'clipselected' || event === 'trackschange' || event === 'clipupdated') {
      const selectedClip = timelineEngine.getSelectedClip();
      renderInspector(selectedClip);
    }
  });

  // Initial render
  renderInspector(timelineEngine.getSelectedClip());
}
