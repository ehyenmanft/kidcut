/**
 * KIDCUT TIMELINE VIEW
 * Multi-track CapCut-style visual editor with drag, trim, playhead scrub and ruler.
 */

import { audioEngine } from '../engine/AudioEngine.js';

export function setupTimelineView(timelineEngine) {
  const headersContainer = document.getElementById('timeline-track-headers');
  const tracksContainer = document.getElementById('tracks-content');
  const rulerCanvas = document.getElementById('ruler-canvas');
  const playhead = document.getElementById('timeline-playhead');
  const scrollArea = document.getElementById('timeline-scroll-area');

  let pxPerSecond = 80; // Zoom factor
  const minPxPerSecond = 20;
  const maxPxPerSecond = 360;

  // Zoom controls
  const zoomInBtn = document.getElementById('btn-zoom-in');
  const zoomOutBtn = document.getElementById('btn-zoom-out');
  const zoomLabel = document.getElementById('zoom-level-label');

  function setZoom(val, anchorClientX = null) {
    const prevZoom = pxPerSecond;
    pxPerSecond = Math.max(minPxPerSecond, Math.min(maxPxPerSecond, val));
    const percent = Math.round((pxPerSecond / 80) * 100);
    if (zoomLabel) zoomLabel.textContent = `${percent}%`;

    if (anchorClientX !== null && scrollArea) {
      const rect = scrollArea.getBoundingClientRect();
      const mouseX = Math.max(0, anchorClientX - rect.left);
      const timeAtCursor = (scrollArea.scrollLeft + mouseX) / prevZoom;
      render();
      scrollArea.scrollLeft = Math.max(0, (timeAtCursor * pxPerSecond) - mouseX);
    } else {
      render();
    }
  }

  zoomInBtn.addEventListener('click', () => {
    setZoom(pxPerSecond * 1.25);
    audioEngine.playBeep(500, 'square', 0.03);
  });
  zoomOutBtn.addEventListener('click', () => {
    setZoom(pxPerSecond / 1.25);
    audioEngine.playBeep(400, 'square', 0.03);
  });

  // Timeline Mouse Wheel Zoom (Ensanchar/reducir línea de tiempo con rueda del mouse)
  const timelinePanel = document.getElementById('timeline-body-wrapper') || scrollArea;
  const timelineContainerEl = document.querySelector('.timeline-container') || timelinePanel;

  const handleTimelineWheel = (e) => {
    // Check if event occurred over timeline container
    e.preventDefault();
    e.stopPropagation();

    // Wheel up (negative deltaY): zoom in (amplify / widen timeline)
    // Wheel down (positive deltaY): zoom out (shrink / reduce timeline)
    const zoomFactor = e.deltaY < 0 ? 1.15 : (1 / 1.15);
    setZoom(pxPerSecond * zoomFactor, e.clientX);
  };

  if (timelineContainerEl) {
    timelineContainerEl.addEventListener('wheel', handleTimelineWheel, { passive: false });
  }

  // Snapping checkbox
  const snapCheck = document.getElementById('chk-snap');
  snapCheck.addEventListener('change', (e) => {
    timelineEngine.isSnapping = e.target.checked;
  });

  // Toolbar action buttons
  const toolUndoBtn = document.getElementById('btn-tool-undo');
  const toolRedoBtn = document.getElementById('btn-tool-redo');

  if (toolUndoBtn) {
    toolUndoBtn.addEventListener('click', () => timelineEngine.undo());
  }
  if (toolRedoBtn) {
    toolRedoBtn.addEventListener('click', () => timelineEngine.redo());
  }

  const updateToolHistoryButtons = () => {
    const canUndo = timelineEngine.canUndo();
    const canRedo = timelineEngine.canRedo();
    if (toolUndoBtn) {
      toolUndoBtn.disabled = !canUndo;
      toolUndoBtn.classList.toggle('disabled', !canUndo);
    }
    if (toolRedoBtn) {
      toolRedoBtn.disabled = !canRedo;
      toolRedoBtn.classList.toggle('disabled', !canRedo);
    }

    const delBtn = document.getElementById('btn-tool-delete');
    const dupBtn = document.getElementById('btn-tool-duplicate');
    const count = timelineEngine.selectedClipIds ? timelineEngine.selectedClipIds.size : (timelineEngine.selectedClipId ? 1 : 0);
    if (delBtn) {
      delBtn.innerHTML = count > 1 ? `🗑 BORRAR (${count})` : '🗑 BORRAR';
      delBtn.title = count > 1 ? `Borrar ${count} clips agrupados (Supr/Del)` : 'Borrar clip seleccionado';
    }
    if (dupBtn) {
      dupBtn.innerHTML = count > 1 ? `📄 DUPLICAR (${count})` : '📄 DUPLICAR';
      dupBtn.title = count > 1 ? `Duplicar ${count} clips agrupados` : 'Duplicar clip seleccionado';
    }
  };

  timelineEngine.subscribe((event) => {
    if (event === 'historystatechange' || event === 'trackschange' || event === 'clipselected') {
      updateToolHistoryButtons();
    }
  });
  updateToolHistoryButtons();

  // Global Keyboard Shortcuts: Ctrl+Z (Undo), Ctrl+Y / Ctrl+Shift+Z (Redo), Delete/Backspace (Delete Selected)
  window.addEventListener('keydown', (e) => {
    const tag = document.activeElement ? document.activeElement.tagName.toLowerCase() : '';
    if (tag === 'input' || tag === 'textarea' || (document.activeElement && document.activeElement.isContentEditable)) {
      return;
    }

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z' && !e.shiftKey) {
      e.preventDefault();
      timelineEngine.undo();
    } else if ((e.ctrlKey || e.metaKey) && (e.key.toLowerCase() === 'y' || (e.shiftKey && e.key.toLowerCase() === 'z'))) {
      e.preventDefault();
      timelineEngine.redo();
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      e.preventDefault();
      timelineEngine.deleteSelectedClip();
    }
  });

  document.getElementById('btn-tool-split').addEventListener('click', () => {
    const success = timelineEngine.splitSelectedClip();
    if (!success) {
      alert('Coloca el cabezal rojo dentro de un clip seleccionado para dividirlo.');
    }
  });

  document.getElementById('btn-tool-delete').addEventListener('click', () => {
    timelineEngine.deleteSelectedClip();
  });

  document.getElementById('btn-tool-duplicate').addEventListener('click', () => {
    timelineEngine.duplicateSelectedClip();
  });

  document.getElementById('btn-add-video-track').addEventListener('click', () => {
    timelineEngine.addTrack('video');
  });

  document.getElementById('btn-add-audio-track').addEventListener('click', () => {
    timelineEngine.addTrack('audio');
  });

  const btnExtractAudio = document.getElementById('btn-tool-extract-audio');
  if (btnExtractAudio) {
    btnExtractAudio.addEventListener('click', () => {
      const clip = timelineEngine.getSelectedClip();
      if (!clip || clip.type !== 'video' || !clip.audioBuffer) {
        alert('Selecciona un clip de video que contenga audio para extraer su pista.');
        return;
      }
      timelineEngine.extractAudioFromClip(clip.id);
    });
  }

  const btnOverlay = document.getElementById('btn-tool-overlay');
  if (btnOverlay) {
    btnOverlay.addEventListener('click', () => {
      const selected = timelineEngine.getSelectedClip();
      if (!selected) {
        alert('Selecciona un clip en la línea de tiempo para superponerlo en paralelo en la pista V2.');
        return;
      }
      if (selected.type === 'audio') {
        timelineEngine.moveClipToTrack(selected.id, 'track-a2');
      } else {
        const currentTrack = timelineEngine.tracks.find(t => t.clips.some(c => c.id === selected.id));
        if (currentTrack && currentTrack.id === 'track-v2') {
          timelineEngine.moveClipToTrack(selected.id, 'track-v1');
        } else {
          timelineEngine.duplicateClipToOverlay(selected.id);
        }
      }
      render();
    });
  }

  // Draw Ruler (Accurate, dynamic scaling without overlapping text)
  function drawRuler() {
    const totalDuration = timelineEngine.calculateTotalDuration();
    const timelineWidth = Math.max(scrollArea.clientWidth, totalDuration * pxPerSecond + 400);
    
    rulerCanvas.width = timelineWidth;
    rulerCanvas.height = 28;
    rulerCanvas.style.width = `${timelineWidth}px`;
    rulerCanvas.style.height = '28px';

    const rulerHeader = document.getElementById('timeline-ruler');
    if (rulerHeader) {
      rulerHeader.style.width = `${timelineWidth}px`;
    }

    const ctx = rulerCanvas.getContext('2d');
    ctx.fillStyle = '#0b0817';
    ctx.fillRect(0, 0, timelineWidth, 28);

    // Determine optimal interval so labels never overlap (minimum 75px between timecodes)
    const minLabelDist = 75;
    const standardIntervals = [0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];
    const stepSec = standardIntervals.find(i => i * pxPerSecond >= minLabelDist) || 60;
    const subDivisions = stepSec >= 5 ? 5 : (stepSec >= 1 ? 4 : 2);
    const subStep = stepSec / subDivisions;

    const maxSeconds = Math.ceil(timelineWidth / pxPerSecond);

    // 1. Minor sub-ticks
    ctx.strokeStyle = '#2d224e';
    ctx.lineWidth = 1;
    for (let t = 0; t <= maxSeconds; t += subStep) {
      const x = Math.round(t * pxPerSecond);
      ctx.beginPath();
      ctx.moveTo(x, 18);
      ctx.lineTo(x, 28);
      ctx.stroke();
    }

    // 2. Major tick marks and clear, truthful timecode labels
    ctx.font = 'bold 9px "Segoe UI", -apple-system, sans-serif';
    for (let t = 0; t <= maxSeconds; t += stepSec) {
      const x = Math.round(t * pxPerSecond);

      ctx.strokeStyle = '#00f0ff';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(x, 12);
      ctx.lineTo(x, 28);
      ctx.stroke();

      const totalSec = Math.round(t * 100) / 100;
      const mins = Math.floor(totalSec / 60);
      const secs = Math.floor(totalSec % 60);
      const frac = Math.round((totalSec - Math.floor(totalSec)) * 10);
      
      const label = (stepSec < 1)
        ? `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}.${frac}`
        : `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;

      ctx.fillStyle = '#00f0ff';
      ctx.fillText(label, x + 4, 11);
    }
  }

  // Update Playhead Position
  function updatePlayhead() {
    const x = timelineEngine.currentTime * pxPerSecond;
    playhead.style.left = `${x}px`;
  }

  // Playhead scrubbing
  let isScrubbing = false;
  function handlePlayheadScrub(e) {
    const rect = scrollArea.getBoundingClientRect();
    const scrollLeft = scrollArea.scrollLeft;
    const clickX = e.clientX - rect.left + scrollLeft;
    const time = Math.max(0, clickX / pxPerSecond);
    timelineEngine.seek(time);
  }

  rulerCanvas.addEventListener('mousedown', (e) => {
    isScrubbing = true;
    handlePlayheadScrub(e);
  });

  window.addEventListener('mousemove', (e) => {
    if (isScrubbing) {
      handlePlayheadScrub(e);
    }
  });

  window.addEventListener('mouseup', () => {
    if (isScrubbing) {
      isScrubbing = false;
    }
  });

  // Marquee / Box Selection DOM Element
  const marqueeEl = document.createElement('div');
  marqueeEl.className = 'timeline-marquee';
  tracksContainer.appendChild(marqueeEl);

  // Marquee drag selection interaction (Hold & drag pointer to group clips)
  let isMarquee = false;
  let marqueeStartX = 0;
  let marqueeStartY = 0;

  tracksContainer.addEventListener('mousedown', (e) => {
    // If clicking inside a clip, handle, or track button, do not start marquee
    if (e.target.closest('.timeline-clip') || e.target.closest('.trim-handle') || e.target.closest('.track-icon-btn')) {
      return;
    }

    const rect = tracksContainer.getBoundingClientRect();
    const scrollLeft = scrollArea.scrollLeft;
    marqueeStartX = e.clientX - rect.left + scrollLeft;
    marqueeStartY = e.clientY - rect.top;
    isMarquee = false;

    const onMouseMove = (moveEvt) => {
      const currentX = moveEvt.clientX - rect.left + scrollArea.scrollLeft;
      const currentY = moveEvt.clientY - rect.top;

      const x1 = Math.min(marqueeStartX, currentX);
      const x2 = Math.max(marqueeStartX, currentX);
      const y1 = Math.min(marqueeStartY, currentY);
      const y2 = Math.max(marqueeStartY, currentY);

      if (x2 - x1 > 4 || y2 - y1 > 4) {
        isMarquee = true;
        marqueeEl.style.display = 'block';
        marqueeEl.style.left = `${x1}px`;
        marqueeEl.style.top = `${y1}px`;
        marqueeEl.style.width = `${x2 - x1}px`;
        marqueeEl.style.height = `${y2 - y1}px`;

        // Check intersection with all rendered clips
        const selectedIds = [];
        const clipEls = tracksContainer.querySelectorAll('.timeline-clip');
        clipEls.forEach(el => {
          const elLeft = el.offsetLeft;
          const elRight = el.offsetLeft + el.offsetWidth;
          const elTop = el.parentElement.offsetTop;
          const elBottom = elTop + el.parentElement.offsetHeight;

          const intersects = !(x2 < elLeft || x1 > elRight || y2 < elTop || y1 > elBottom);
          if (intersects) {
            el.classList.add('selected', 'multi-selected');
            if (el.dataset.clipId) {
              selectedIds.push(el.dataset.clipId);
            }
          } else {
            el.classList.remove('multi-selected');
            if (!selectedIds.includes(el.dataset.clipId)) {
              el.classList.remove('selected');
            }
          }
        });

        if (selectedIds.length > 0) {
          timelineEngine.selectClips(selectedIds);
          updateToolHistoryButtons();
        }
      }
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);

      marqueeEl.style.display = 'none';

      if (!isMarquee) {
        // Simple click on empty area clears selection
        timelineEngine.clearSelection();
        render();
      } else {
        render();
        audioEngine.playBeep(480, 'square', 0.05);
      }
      isMarquee = false;
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  });

  // Render Tracks & Clips
  function render() {
    drawRuler();
    updatePlayhead();

    headersContainer.innerHTML = '';
    tracksContainer.innerHTML = '';
    tracksContainer.appendChild(marqueeEl);

    const totalDuration = timelineEngine.calculateTotalDuration();
    const minWidth = Math.max(scrollArea.clientWidth, totalDuration * pxPerSecond + 400);
    tracksContainer.style.width = `${minWidth}px`;

    timelineEngine.tracks.forEach((track) => {
      // 1. Header Item
      const header = document.createElement('div');
      header.className = `track-header-item ${track.type === 'audio' ? 'audio' : ''}`;
      header.innerHTML = `
        <span class="track-label">${track.name}</span>
        <div class="track-btns">
          ${track.type === 'video' ? `
            <button class="track-icon-btn ${track.hidden ? 'active' : ''}" data-act="hide" title="Ocultar pista">👁</button>
          ` : `
            <button class="track-icon-btn ${track.muted ? 'active' : ''}" data-act="mute" title="Silenciar pista">🔇</button>
          `}
        </div>
      `;

      // Header buttons actions
      header.querySelectorAll('.track-icon-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          const act = btn.dataset.act;
          if (act === 'hide') {
            track.hidden = !track.hidden;
          } else if (act === 'mute') {
            track.muted = !track.muted;
          }
          timelineEngine.notify('trackschange');
        });
      });

      headersContainer.appendChild(header);

      // 2. Track Row
      const row = document.createElement('div');
      row.className = 'track-row';
      row.dataset.trackId = track.id;

      // Allow dragging assets directly from Asset Library onto this track
      row.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        row.style.background = 'rgba(0, 240, 255, 0.15)';
      });
      row.addEventListener('dragleave', () => {
        row.style.background = '';
      });
      row.addEventListener('drop', (e) => {
        e.preventDefault();
        row.style.background = '';
        const assetId = e.dataTransfer.getData('text/kidcut-asset-id');
        if (assetId && window.__kidcutGetAssetById && window.__kidcutAddAssetToTimeline) {
          const asset = window.__kidcutGetAssetById(assetId);
          if (asset) {
            const rect = row.getBoundingClientRect();
            const clickX = e.clientX - rect.left;
            const dropTime = Math.max(0, clickX / pxPerSecond);
            window.__kidcutAddAssetToTimeline(asset, track.id, dropTime);
          }
        }
      });

      // Render Clips in this track
      track.clips.forEach((clip) => {
        const clipEl = document.createElement('div');
        const isSelected = timelineEngine.isClipSelected(clip.id) || clip.id === timelineEngine.selectedClipId;
        const isMulti = timelineEngine.selectedClipIds && timelineEngine.selectedClipIds.size > 1 && isSelected;
        clipEl.className = `timeline-clip ${clip.type}-clip ${isSelected ? 'selected' : ''} ${isMulti ? 'multi-selected' : ''}`;
        clipEl.dataset.clipId = clip.id;
        
        const left = clip.start * pxPerSecond;
        const width = Math.max(20, clip.duration * pxPerSecond);

        clipEl.style.left = `${left}px`;
        clipEl.style.width = `${width}px`;

        const effectBadge = clip.audioEffect && clip.audioEffect !== 'none' ? `<span class="clip-fx-badge" title="Efecto: ${clip.audioEffect}">FX</span>` : '';
        const muteBadge = clip.isMuted ? `<span class="clip-mute-badge" title="Silenciado">🔇</span>` : '';
        const waveformCanvasHtml = clip.audioBuffer ? `<canvas class="timeline-waveform-canvas" width="${Math.round(Math.max(10, width))}" height="28"></canvas>` : '';

        clipEl.innerHTML = `
          <div class="trim-handle trim-left" data-edge="left"></div>
          ${waveformCanvasHtml}
          <div class="clip-title-wrap">
            <span class="clip-title">${clip.name}</span>
            ${effectBadge}
            ${muteBadge}
          </div>
          <div class="trim-handle trim-right" data-edge="right"></div>
        `;

        if (clip.audioBuffer) {
          const wfCanvas = clipEl.querySelector('.timeline-waveform-canvas');
          if (wfCanvas) {
            const wfCtx = wfCanvas.getContext('2d');
            const barsCount = Math.max(8, Math.min(80, Math.floor(width / 4)));
            if (!clip._waveformCache || clip._waveformCache.length !== barsCount) {
              clip._waveformCache = audioEngine.generateWaveformData(clip.audioBuffer, barsCount);
            }
            const data = clip._waveformCache;
            const mid = wfCanvas.height / 2;
            const barW = Math.max(1.5, width / data.length);
            wfCtx.fillStyle = clip.isMuted ? 'rgba(150, 150, 160, 0.4)' : 'rgba(0, 240, 255, 0.55)';
            for (let i = 0; i < data.length; i++) {
              const h = Math.max(2, data[i] * (wfCanvas.height - 4));
              wfCtx.fillRect(i * barW, mid - (h / 2), Math.max(1, barW - 1), h);
            }
          }
        }

        // Select clip on click (supports Shift / Ctrl multi-selection)
        clipEl.addEventListener('mousedown', (e) => {
          if (e.target.classList.contains('trim-handle')) return;
          e.stopPropagation();

          const isShiftOrCtrl = e.shiftKey || e.ctrlKey || e.metaKey;
          if (isShiftOrCtrl) {
            timelineEngine.selectClip(clip.id, true);
            render();
          } else {
            if (!timelineEngine.isClipSelected(clip.id)) {
              timelineEngine.selectClip(clip.id, false);
              render();
            }
          }
          startDraggingClip(clip, e);
        });

        // Trim handle drag
        const leftHandle = clipEl.querySelector('.trim-left');
        const rightHandle = clipEl.querySelector('.trim-right');

        leftHandle.addEventListener('mousedown', (e) => {
          e.stopPropagation();
          startTrimmingClip(clip, 'left', e);
        });

        rightHandle.addEventListener('mousedown', (e) => {
          e.stopPropagation();
          startTrimmingClip(clip, 'right', e);
        });

        row.appendChild(clipEl);
      });

      tracksContainer.appendChild(row);
    });
  }

  // Clip Drag & Move Handler (Supports Moving Single or Multiple Grouped Clips)
  function startDraggingClip(clip, startEvent) {
    const isMulti = timelineEngine.isClipSelected(clip.id) && timelineEngine.selectedClipIds.size > 1;
    const movingClips = isMulti ? timelineEngine.getSelectedClips() : [clip];

    timelineEngine.pushState(`Mover ${movingClips.length > 1 ? movingClips.length + ' clips' : clip.name}`);
    const startX = startEvent.clientX;

    const initialStarts = new Map();
    let minStart = Infinity;
    for (const c of movingClips) {
      initialStarts.set(c.id, c.start);
      if (c.start < minStart) minStart = c.start;
    }

    audioEngine.playBeep(420, 'square', 0.04);

    const onMouseMove = (e) => {
      const deltaX = e.clientX - startX;
      let deltaSeconds = deltaX / pxPerSecond;

      // Prevent dragging before 0:00
      if (minStart + deltaSeconds < 0) {
        deltaSeconds = -minStart;
      }

      // Magnetic snapping based on primary dragged clip
      if (timelineEngine.isSnapping) {
        const targetDraggedStart = initialStarts.get(clip.id) + deltaSeconds;
        const snappedStart = timelineEngine.applySnapping(clip.id, targetDraggedStart, clip.duration);
        deltaSeconds += (snappedStart - targetDraggedStart);
        if (minStart + deltaSeconds < 0) {
          deltaSeconds = -minStart;
        }
      }

      for (const c of movingClips) {
        const init = initialStarts.get(c.id);
        c.start = Math.max(0, init + deltaSeconds);
      }

      // Allow vertical dragging between tracks of the same type (e.g. V1 <-> V2)
      const hoverElements = document.elementsFromPoint(e.clientX, e.clientY);
      const targetRow = hoverElements ? hoverElements.find(el => el && el.classList && el.classList.contains('track-row')) : null;
      if (targetRow && targetRow.dataset.trackId) {
        const targetTrackId = targetRow.dataset.trackId;
        const currentTrack = timelineEngine.tracks.find(t => t.clips.some(item => item.id === clip.id));
        if (currentTrack && currentTrack.id !== targetTrackId) {
          const targetTrack = timelineEngine.tracks.find(t => t.id === targetTrackId);
          if (targetTrack && targetTrack.type === currentTrack.type) {
            timelineEngine.moveClipToTrack(clip.id, targetTrackId);
          }
        }
      }

      timelineEngine.calculateTotalDuration();
      timelineEngine.notify('trackschange');
      render();
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  }

  // Clip Trim In/Out Handler (Reducir o Ampliar clip o audio arrastrando los extremos)
  function startTrimmingClip(clip, edge, startEvent) {
    timelineEngine.selectClip(clip.id);
    const startX = startEvent.clientX;
    const initialStart = clip.start;
    const initialDuration = clip.duration;
    const initialTrimIn = clip.trimIn || 0;

    let maxSourceDuration = Infinity;
    if (clip.mediaElement && clip.mediaElement.duration && !isNaN(clip.mediaElement.duration)) {
      maxSourceDuration = clip.mediaElement.duration / (clip.speed || 1.0);
    } else if (clip.audioBuffer && clip.audioBuffer.duration) {
      maxSourceDuration = clip.audioBuffer.duration / (clip.speed || 1.0);
    }

    let hasChanged = false;
    audioEngine.playBeep(450, 'square', 0.03);

    // Floating tooltip
    let trimTooltip = document.getElementById('timeline-trim-tooltip');
    if (!trimTooltip) {
      trimTooltip = document.createElement('div');
      trimTooltip.id = 'timeline-trim-tooltip';
      trimTooltip.className = 'timeline-trim-tooltip';
      document.body.appendChild(trimTooltip);
    }
    trimTooltip.style.display = 'block';

    const updateTooltip = (e, text) => {
      trimTooltip.textContent = text;
      trimTooltip.style.left = `${e.clientX + 14}px`;
      trimTooltip.style.top = `${e.clientY - 28}px`;
    };

    updateTooltip(startEvent, `⏱ DURACIÓN: ${initialDuration.toFixed(2)}s`);

    const handleEl = startEvent.target;
    if (handleEl && handleEl.classList) {
      handleEl.classList.add('trimming');
    }

    const onMouseMove = (e) => {
      const deltaX = e.clientX - startX;
      let deltaSeconds = deltaX / pxPerSecond;

      if (edge === 'right') {
        let newDuration = initialDuration + deltaSeconds;

        if (timelineEngine.isSnapping) {
          const targetEnd = initialStart + newDuration;
          const snappedEnd = timelineEngine.applySnappingToPoint(clip.id, targetEnd);
          newDuration = snappedEnd - initialStart;
        }

        if (newDuration < 0.15) newDuration = 0.15;
        const availableDuration = maxSourceDuration - initialTrimIn;
        if (isFinite(availableDuration) && availableDuration > 0) {
          if (newDuration > availableDuration) {
            newDuration = availableDuration;
          }
        }

        clip.duration = Math.max(0.15, Number(newDuration.toFixed(3)));
        hasChanged = true;
        const diff = clip.duration - initialDuration;
        updateTooltip(e, `⏱ DURACIÓN: ${clip.duration.toFixed(2)}s (${diff >= 0 ? '+' : ''}${diff.toFixed(2)}s)`);
      } else if (edge === 'left') {
        let newStart = initialStart + deltaSeconds;

        if (timelineEngine.isSnapping) {
          newStart = timelineEngine.applySnappingToPoint(clip.id, newStart);
        }

        if (newStart < 0) newStart = 0;
        let shift = newStart - initialStart;

        if (initialDuration - shift < 0.15) {
          shift = initialDuration - 0.15;
          newStart = initialStart + shift;
        }

        if (initialTrimIn + shift < 0) {
          shift = -initialTrimIn;
          newStart = initialStart + shift;
        }

        clip.start = Math.max(0, Number(newStart.toFixed(3)));
        clip.duration = Math.max(0.15, Number((initialDuration - shift).toFixed(3)));
        clip.trimIn = Math.max(0, Number((initialTrimIn + shift).toFixed(3)));
        hasChanged = true;
        updateTooltip(e, `⏱ INICIO: ${clip.start.toFixed(2)}s | DUR: ${clip.duration.toFixed(2)}s`);
      }

      // Sync linked audio clip if present
      for (const track of timelineEngine.tracks) {
        if (track.type === 'audio') {
          const linked = track.clips.find(c => c.sourceVideoClipId === clip.id);
          if (linked) {
            linked.start = clip.start;
            linked.duration = clip.duration;
            linked.trimIn = clip.trimIn;
          }
        }
      }

      timelineEngine.calculateTotalDuration();
      timelineEngine.notify('trackschange');
      render();
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = '';
      if (handleEl && handleEl.classList) {
        handleEl.classList.remove('trimming');
      }
      if (trimTooltip) trimTooltip.style.display = 'none';

      if (hasChanged) {
        timelineEngine.pushState(`Recortar/Ampliar ${clip.name}`);
        audioEngine.playBeep(520, 'square', 0.04);
      }
    };

    document.body.style.cursor = 'ew-resize';
    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  }

  // Engine subscriptions
  timelineEngine.subscribe((event) => {
    if (event === 'timeupdate') {
      updatePlayhead();
    } else if (event === 'trackschange' || event === 'clipselected') {
      render();
    }
  });

  render();
}
