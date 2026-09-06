/**
 * KIDCUT HEADER BAR COMPONENT
 */

import { compositor } from '../engine/Compositor.js';
import { audioEngine } from '../engine/AudioEngine.js';

export function setupHeaderBar(timelineEngine, onOpenExport) {
  // Aspect Ratio buttons
  const ratioButtons = document.querySelectorAll('.ratio-selector-group .pixel-tab-btn');
  ratioButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      ratioButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const ratio = btn.dataset.ratio;
      compositor.setAspectRatio(ratio);
      timelineEngine.notify('ratiochange', { ratio });
      audioEngine.playBeep(580, 'square', 0.05);
    });
  });

  // CRT Scanline Toggle
  const crtBtn = document.getElementById('btn-toggle-crt');
  crtBtn.addEventListener('click', () => {
    const isNowActive = document.body.classList.toggle('crt-active');
    crtBtn.textContent = isNowActive ? '📺 CRT: ON' : '📺 CRT: OFF';
    crtBtn.classList.toggle('active', isNowActive);
    audioEngine.playBeep(isNowActive ? 750 : 350, 'sawtooth', 0.06);
  });

  // Sound SFX Toggle
  const sfxBtn = document.getElementById('btn-sfx-toggle');
  let sfxOn = true;
  sfxBtn.addEventListener('click', () => {
    sfxOn = !sfxOn;
    audioEngine.setSfxEnabled(sfxOn);
    sfxBtn.textContent = sfxOn ? '🔊 SFX: ON' : '🔇 SFX: OFF';
    sfxBtn.classList.toggle('active', sfxOn);
    if (sfxOn) audioEngine.playCoin();
  });

  // Undo & Redo buttons
  const undoBtn = document.getElementById('btn-undo');
  const redoBtn = document.getElementById('btn-redo');

  if (undoBtn) {
    undoBtn.addEventListener('click', () => {
      timelineEngine.undo();
    });
  }

  if (redoBtn) {
    redoBtn.addEventListener('click', () => {
      timelineEngine.redo();
    });
  }

  // Update Undo/Redo button states
  const updateHistoryButtons = () => {
    const canUndo = timelineEngine.canUndo();
    const canRedo = timelineEngine.canRedo();
    if (undoBtn) {
      undoBtn.disabled = !canUndo;
      undoBtn.classList.toggle('disabled', !canUndo);
    }
    if (redoBtn) {
      redoBtn.disabled = !canRedo;
      redoBtn.classList.toggle('disabled', !canRedo);
    }
  };

  timelineEngine.subscribe((event) => {
    if (event === 'historystatechange' || event === 'trackschange') {
      updateHistoryButtons();
    }
  });
  updateHistoryButtons();

  // Export Modal Trigger
  const exportBtn = document.getElementById('btn-export-modal');
  exportBtn.addEventListener('click', () => {
    audioEngine.playCoin();
    if (onOpenExport) onOpenExport();
  });

  // 10 UI Themes Selector
  const themeSelect = document.getElementById('select-ui-theme');
  const allThemeClasses = [
    'theme-arcade', 'theme-cyberpunk', 'theme-gameboy', 'theme-vaporwave',
    'theme-nes', 'theme-matrix', 'theme-dracula', 'theme-amber',
    'theme-genesis', 'theme-bubblegum'
  ];

  function applyTheme(themeKey) {
    document.body.classList.remove(...allThemeClasses);
    document.body.classList.add(`theme-${themeKey}`);
  }

  if (themeSelect) {
    const savedTheme = localStorage.getItem('kidcut_ui_theme') || 'arcade';
    themeSelect.value = savedTheme;
    applyTheme(savedTheme);

    themeSelect.addEventListener('change', (e) => {
      const selected = e.target.value;
      applyTheme(selected);
      localStorage.setItem('kidcut_ui_theme', selected);
      audioEngine.playPowerup();
    });
  }
}
