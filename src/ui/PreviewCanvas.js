/**
 * KIDCUT PREVIEW CANVAS & CONTROLLER
 * Interactive on-screen viewport with direct drag-to-move and scale handles
 * for stickers, text, images, and effects.
 */

import { compositor } from '../engine/Compositor.js';
import { audioEngine } from '../engine/AudioEngine.js';

export function setupPreviewCanvas(timelineEngine) {
  const canvas = document.getElementById('main-preview-canvas');
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  const screenFrame = document.getElementById('canvas-screen-frame');
  const selectionBox = document.getElementById('canvas-selection-box');

  const timecodeEl = document.getElementById('timecode-display');
  const playBtn = document.getElementById('btn-play-pause');
  const jumpStartBtn = document.getElementById('btn-jump-start');
  const stepBackBtn = document.getElementById('btn-step-back');
  const stepFwdBtn = document.getElementById('btn-step-fwd');
  const loopBtn = document.getElementById('btn-loop');

  // Build Selection Handles HTML inside selection box
  selectionBox.innerHTML = `
    <div class="sel-handle sel-handle-tl" data-handle="tl"></div>
    <div class="sel-handle sel-handle-tr" data-handle="tr"></div>
    <div class="sel-handle sel-handle-bl" data-handle="bl"></div>
    <div class="sel-handle sel-handle-br" data-handle="br"></div>
    <div class="sel-tag" id="sel-tag-name">CLIP</div>
  `;

  function resizeCanvas() {
    const ratioInfo = compositor.baseWidth / compositor.baseHeight;
    const parent = document.getElementById('canvas-viewport');
    const maxW = parent.clientWidth - 32;
    const maxH = parent.clientHeight - 32;

    let targetW = maxW;
    let targetH = maxW / ratioInfo;

    if (targetH > maxH) {
      targetH = maxH;
      targetW = maxH * ratioInfo;
    }

    canvas.width = Math.round(targetW);
    canvas.height = Math.round(targetH);

    screenFrame.style.width = `${Math.round(targetW)}px`;
    screenFrame.style.height = `${Math.round(targetH)}px`;

    render();
  }

  function render() {
    compositor.renderFrame(timelineEngine.tracks, timelineEngine.currentTime, ctx);
    updateTimecode();
    updateSelectionBox();
  }

  function formatTimecode(seconds, fps = 30) {
    const totalFrames = Math.floor(seconds * fps);
    const frames = totalFrames % fps;
    const totalSeconds = Math.floor(seconds);
    const secs = totalSeconds % 60;
    const mins = Math.floor(totalSeconds / 60) % 60;
    const hrs = Math.floor(totalSeconds / 3600);

    const pad = (n, len = 2) => String(n).padStart(len, '0');
    return `${pad(hrs)}:${pad(mins)}:${pad(secs)}:${pad(frames)}`;
  }

  function updateTimecode() {
    timecodeEl.textContent = formatTimecode(timelineEngine.currentTime, timelineEngine.fps);
  }

  // =========================================================================
  // ON-SCREEN DRAG-TO-MOVE & SCALE INTERACTION
  // =========================================================================
  function getClipBounds(clip) {
    const cw = canvas.width;
    const ch = canvas.height;
    const posX = (clip.x !== undefined ? clip.x : 0.5) * cw;
    const posY = (clip.y !== undefined ? clip.y : 0.5) * ch;
    const scale = clip.scale !== undefined ? clip.scale : 1.0;

    let w = 120 * scale;
    let h = 80 * scale;

    if (clip.type === 'sticker') {
      const sz = Math.round(ch * 0.25 * scale);
      w = sz;
      h = sz;
    } else if (clip.type === 'text') {
      const fSize = (clip.fontSize || Math.round(ch * 0.08)) * (ch / compositor.baseHeight);
      const textLen = (clip.text || 'TEXT').length;
      w = Math.max(80, Math.round(textLen * fSize * 0.7 * scale));
      h = Math.max(40, Math.round(fSize * 1.6 * scale));
    } else if (clip.type === 'image' || clip.type === 'video') {
      w = Math.round(cw * 0.5 * scale);
      h = Math.round(ch * 0.5 * scale);
    } else if (clip.type === 'color') {
      w = Math.round(cw * 0.6 * scale);
      h = Math.round(ch * 0.6 * scale);
    }

    return {
      left: Math.round(posX - w / 2),
      top: Math.round(posY - h / 2),
      width: Math.round(w),
      height: Math.round(h),
      centerX: posX,
      centerY: posY
    };
  }

  function updateSelectionBox() {
    const clip = timelineEngine.getSelectedClip();
    if (!clip || timelineEngine.currentTime < clip.start || timelineEngine.currentTime >= (clip.start + clip.duration)) {
      selectionBox.classList.add('hidden');
      return;
    }

    // Only video/visual clips have on-screen positions
    const track = timelineEngine.tracks.find(t => t.clips.some(c => c.id === clip.id));
    if (!track || track.type !== 'video') {
      selectionBox.classList.add('hidden');
      return;
    }

    const bounds = getClipBounds(clip);
    selectionBox.style.left = `${bounds.left}px`;
    selectionBox.style.top = `${bounds.top}px`;
    selectionBox.style.width = `${bounds.width}px`;
    selectionBox.style.height = `${bounds.height}px`;

    const tagEl = document.getElementById('sel-tag-name');
    if (tagEl) {
      tagEl.textContent = `${clip.name.substring(0, 14)} (${Math.round((clip.scale || 1) * 100)}%)`;
    }

    selectionBox.classList.remove('hidden');
  }

  // Mouse Drag to Move
  let isDragging = false;
  let isScaling = false;
  let startX = 0;
  let startY = 0;
  let initialClipX = 0.5;
  let initialClipY = 0.5;
  let initialScale = 1.0;
  let activeHandle = null;

  selectionBox.addEventListener('mousedown', (e) => {
    const clip = timelineEngine.getSelectedClip();
    if (!clip) return;

    e.stopPropagation();
    const handle = e.target.closest('.sel-handle');

    if (handle) {
      isScaling = true;
      activeHandle = handle.dataset.handle;
      startX = e.clientX;
      startY = e.clientY;
      initialScale = clip.scale !== undefined ? clip.scale : 1.0;
    } else {
      isDragging = true;
      startX = e.clientX;
      startY = e.clientY;
      initialClipX = clip.x !== undefined ? clip.x : 0.5;
      initialClipY = clip.y !== undefined ? clip.y : 0.5;
    }

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  });

  function onMouseMove(e) {
    const clip = timelineEngine.getSelectedClip();
    if (!clip) return;

    if (isDragging) {
      const dx = (e.clientX - startX) / canvas.width;
      const dy = (e.clientY - startY) / canvas.height;
      clip.x = Math.max(-0.2, Math.min(1.2, Number((initialClipX + dx).toFixed(3))));
      clip.y = Math.max(-0.2, Math.min(1.2, Number((initialClipY + dy).toFixed(3))));
      timelineEngine.notify('clipupdated', { clip });
      render();
    } else if (isScaling) {
      const bounds = getClipBounds(clip);
      const rect = canvas.getBoundingClientRect();
      const centerScreenX = rect.left + bounds.centerX;
      const centerScreenY = rect.top + bounds.centerY;

      const currentDist = Math.hypot(e.clientX - centerScreenX, e.clientY - centerScreenY);
      const startDist = Math.hypot(startX - centerScreenX, startY - centerScreenY) || 1;

      const scaleMultiplier = currentDist / startDist;
      clip.scale = Math.max(0.2, Math.min(4.0, Number((initialScale * scaleMultiplier).toFixed(2))));
      timelineEngine.notify('clipupdated', { clip });
      render();
    }
  }

  function onMouseUp() {
    isDragging = false;
    isScaling = false;
    activeHandle = null;
    window.removeEventListener('mousemove', onMouseMove);
    window.removeEventListener('mouseup', onMouseUp);
  }

  // Click on canvas to select clip under cursor
  canvas.addEventListener('mousedown', (e) => {
    const rect = canvas.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const clickY = e.clientY - rect.top;

    // Find top-most active clip at currentTime under click point
    const videoTracks = timelineEngine.tracks
      .filter(t => t.type === 'video' && !t.hidden)
      .sort((a, b) => b.order - a.order); // top tracks first

    for (const track of videoTracks) {
      for (const clip of track.clips) {
        if (timelineEngine.currentTime >= clip.start && timelineEngine.currentTime < (clip.start + clip.duration)) {
          const b = getClipBounds(clip);
          if (clickX >= b.left && clickX <= (b.left + b.width) &&
              clickY >= b.top && clickY <= (b.top + b.height)) {
            timelineEngine.selectClip(clip.id);
            audioEngine.playBeep(520, 'square', 0.04);
            render();
            return;
          }
        }
      }
    }
  });

  // Playback control events
  playBtn.addEventListener('click', () => {
    timelineEngine.togglePlay();
  });

  jumpStartBtn.addEventListener('click', () => {
    timelineEngine.jumpToStart();
    audioEngine.playBeep(400, 'square', 0.04);
  });

  stepBackBtn.addEventListener('click', () => {
    timelineEngine.stepFrames(-1);
    audioEngine.playBeep(350, 'square', 0.03);
  });

  stepFwdBtn.addEventListener('click', () => {
    timelineEngine.stepFrames(1);
    audioEngine.playBeep(450, 'square', 0.03);
  });

  loopBtn.addEventListener('click', () => {
    const isLoop = timelineEngine.toggleLoop();
    loopBtn.classList.toggle('active', isLoop);
    loopBtn.style.color = isLoop ? 'var(--color-cyan)' : '#fff';
    audioEngine.playBeep(520, 'triangle', 0.05);
  });

  // Global Keyboard Shortcuts
  window.addEventListener('keydown', (e) => {
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

    if (e.code === 'Space') {
      e.preventDefault();
      timelineEngine.togglePlay();
    } else if (e.code === 'KeyJ') {
      timelineEngine.stepFrames(-1);
    } else if (e.code === 'KeyL') {
      timelineEngine.stepFrames(1);
    } else if (e.code === 'KeyK') {
      timelineEngine.pause();
    } else if (e.code === 'KeyB' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      timelineEngine.splitSelectedClip();
    } else if (e.code === 'Delete' || e.code === 'Backspace') {
      timelineEngine.deleteSelectedClip();
    }
  });

  // Timeline engine listeners
  timelineEngine.subscribe((event) => {
    if (event === 'timeupdate') {
      render();
    } else if (event === 'playbackchange') {
      playBtn.textContent = timelineEngine.isPlaying ? '⏸' : '▶';
    } else if (event === 'ratiochange') {
      resizeCanvas();
    } else if (event === 'trackschange' || event === 'clipupdated' || event === 'clipselected') {
      render();
    }
  });

  window.addEventListener('resize', resizeCanvas);
  setTimeout(resizeCanvas, 50);

  return { render, resizeCanvas };
}
