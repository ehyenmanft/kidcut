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
  const minPxPerSecond = 30;
  const maxPxPerSecond = 240;

  // Zoom controls
  const zoomInBtn = document.getElementById('btn-zoom-in');
  const zoomOutBtn = document.getElementById('btn-zoom-out');
  const zoomLabel = document.getElementById('zoom-level-label');

  function setZoom(val) {
    pxPerSecond = Math.max(minPxPerSecond, Math.min(maxPxPerSecond, val));
    const percent = Math.round((pxPerSecond / 80) * 100);
    zoomLabel.textContent = `${percent}%`;
    render();
  }

  zoomInBtn.addEventListener('click', () => {
    setZoom(pxPerSecond * 1.25);
    audioEngine.playBeep(500, 'square', 0.03);
  });
  zoomOutBtn.addEventListener('click', () => {
    setZoom(pxPerSecond / 1.25);
    audioEngine.playBeep(400, 'square', 0.03);
  });

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
  };

  timelineEngine.subscribe((event) => {
    if (event === 'historystatechange' || event === 'trackschange') {
      updateToolHistoryButtons();
    }
  });
  updateToolHistoryButtons();

  // Global Keyboard Shortcuts: Ctrl+Z (Undo), Ctrl+Y / Ctrl+Shift+Z (Redo)
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

  // Render Tracks & Clips
  function render() {
    drawRuler();
    updatePlayhead();

    headersContainer.innerHTML = '';
    tracksContainer.innerHTML = '';

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

      // Render Clips in this track
      track.clips.forEach((clip) => {
        const clipEl = document.createElement('div');
        clipEl.className = `timeline-clip ${clip.type}-clip ${clip.id === timelineEngine.selectedClipId ? 'selected' : ''}`;
        
        const left = clip.start * pxPerSecond;
        const width = Math.max(20, clip.duration * pxPerSecond);

        clipEl.style.left = `${left}px`;
        clipEl.style.width = `${width}px`;

        clipEl.innerHTML = `
          <div class="trim-handle trim-left" data-edge="left"></div>
          <span class="clip-title">${clip.name}</span>
          <div class="trim-handle trim-right" data-edge="right"></div>
        `;

        // Select clip on click
        clipEl.addEventListener('mousedown', (e) => {
          if (e.target.classList.contains('trim-handle')) return; // let handle deal with it
          timelineEngine.selectClip(clip.id);
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

  // Clip Drag & Move Handler
  function startDraggingClip(clip, startEvent) {
    timelineEngine.pushState(`Mover ${clip.name}`);
    const startX = startEvent.clientX;
    const initialStart = clip.start;
    audioEngine.playBeep(420, 'square', 0.04);

    const onMouseMove = (e) => {
      const deltaX = e.clientX - startX;
      const deltaSeconds = deltaX / pxPerSecond;
      timelineEngine.moveClip(clip.id, initialStart + deltaSeconds);
      render();
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  }

  // Clip Trim In/Out Handler
  function startTrimmingClip(clip, edge, startEvent) {
    timelineEngine.selectClip(clip.id);
    timelineEngine.pushState(`Recortar ${clip.name}`);
    const startX = startEvent.clientX;

    const onMouseMove = (e) => {
      const deltaX = e.clientX - startX;
      const deltaSeconds = deltaX / pxPerSecond;
      timelineEngine.trimClip(clip.id, edge, deltaSeconds);
      render();
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };

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
