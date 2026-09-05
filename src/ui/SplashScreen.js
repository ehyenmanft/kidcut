/**
 * KIDCUT RETRO SPLASH SCREEN
 * Arcade boot sequence with pixel progress bar & sound.
 */

import { audioEngine } from '../engine/AudioEngine.js';

export function setupSplashScreen(onReady) {
  const splash = document.getElementById('splash-screen');
  const bar = document.getElementById('splash-progress-bar');
  const statusText = document.getElementById('splash-status-text');
  const startBtn = document.getElementById('splash-start-btn');
  const consoleEl = document.getElementById('splash-console');

  let progress = 0;
  const messages = [
    'INICIALIZANDO MOTOR DE VIDEO RETRO... 15%',
    'CARGANDO GENERADOR CHIPTUNE 8-BIT... 45%',
    'CALIBRANDO LÍNEA DE TIEMPO MULTICANAL... 70%',
    'PREPARANDO CANVAS DE ALTA DEFINICIÓN... 90%',
    '¡KIDCUT LISTO! INSERTA UNA MONEDA... 100%'
  ];

  const interval = setInterval(() => {
    progress += Math.floor(Math.random() * 15) + 12;
    if (progress > 100) progress = 100;

    bar.style.width = `${progress}%`;
    const msgIdx = Math.min(messages.length - 1, Math.floor((progress / 100) * messages.length));
    statusText.textContent = messages[msgIdx];

    // Audio click
    if (progress < 100) {
      audioEngine.playBeep(300 + progress * 4, 'square', 0.03);
    }

    if (progress >= 100) {
      clearInterval(interval);
      audioEngine.playPowerup();
      startBtn.classList.remove('hidden');

      const launchEditor = () => {
        audioEngine.playCoin();
        splash.classList.add('fade-out');
        setTimeout(() => {
          splash.style.display = 'none';
          if (onReady) onReady();
        }, 500);
      };

      startBtn.addEventListener('click', launchEditor);

      // Auto start on Space or Enter key
      const keyHandler = (e) => {
        if (e.code === 'Space' || e.code === 'Enter') {
          window.removeEventListener('keydown', keyHandler);
          launchEditor();
        }
      };
      window.addEventListener('keydown', keyHandler);
    }
  }, 120);
}
