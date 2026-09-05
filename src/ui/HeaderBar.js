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

  // Export Modal Trigger
  const exportBtn = document.getElementById('btn-export-modal');
  exportBtn.addEventListener('click', () => {
    audioEngine.playCoin();
    if (onOpenExport) onOpenExport();
  });
}
