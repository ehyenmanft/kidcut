/**
 * KIDCUT EXPORT MODAL COMPONENT
 */

import { exporter } from '../engine/Exporter.js';
import { audioEngine } from '../engine/AudioEngine.js';

export function setupExportModal(timelineEngine) {
  const modal = document.getElementById('export-modal');
  const closeBtn = document.getElementById('btn-close-export');
  const cancelBtn = document.getElementById('btn-cancel-export');
  const startBtn = document.getElementById('btn-start-export');
  const progressWrap = document.getElementById('export-progress-wrap');
  const progressBar = document.getElementById('export-progress-bar');
  const msgStatus = document.getElementById('export-msg-status');
  const statsLabel = document.getElementById('export-stats');
  const downloadAnchor = document.getElementById('download-anchor');

  let selectedResolution = '1080';
  let selectedRatio = '16:9';
  let selectedFormat = 'webm';
  let selectedFps = 30;

  function setupButtonGroup(groupId, onSelect) {
    const buttons = document.querySelectorAll(`#${groupId} .pixel-opt-btn`);
    buttons.forEach(btn => {
      btn.addEventListener('click', () => {
        buttons.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        onSelect(btn);
        audioEngine.playBeep(520, 'square', 0.04);
      });
    });
  }

  setupButtonGroup('export-resolution-group', (btn) => {
    selectedResolution = btn.dataset.res;
  });

  setupButtonGroup('export-ratio-group', (btn) => {
    selectedRatio = btn.dataset.ratio;
  });

  setupButtonGroup('export-format-group', (btn) => {
    selectedFormat = btn.dataset.format;
  });

  setupButtonGroup('export-fps-group', (btn) => {
    selectedFps = parseInt(btn.dataset.fps, 10);
  });

  function open() {
    modal.classList.remove('hidden');
    progressWrap.classList.add('hidden');
    startBtn.disabled = false;
    startBtn.textContent = 'INICIAR RENDERIZADO';
  }

  function close() {
    if (exporter.isExporting) {
      if (confirm('¿Deseas cancelar el renderizado en curso?')) {
        exporter.cancel();
      } else {
        return;
      }
    }
    modal.classList.add('hidden');
  }

  closeBtn.addEventListener('click', close);
  cancelBtn.addEventListener('click', close);

  startBtn.addEventListener('click', async () => {
    if (exporter.isExporting) return;

    startBtn.disabled = true;
    startBtn.textContent = 'RENDERIZANDO...';
    progressWrap.classList.remove('hidden');
    progressBar.style.width = '0%';
    msgStatus.textContent = 'INICIANDO MOTOR DETERMINISTA DE EXPORTACIÓN...';

    audioEngine.playCoin();

    try {
      const result = await exporter.exportProject(
        timelineEngine,
        {
          resolution: selectedResolution,
          ratio: selectedRatio,
          format: selectedFormat,
          fps: selectedFps
        },
        (progress) => {
          progressBar.style.width = `${progress.percent}%`;
          msgStatus.textContent = progress.status;
          statsLabel.textContent = `CUADRO: ${progress.frame || 0} / ${progress.totalFrames || 0} (${progress.percent}%)`;
        }
      );

      // Render finished successfully!
      audioEngine.playPowerup();
      msgStatus.textContent = '¡RENDERIZADO COMPLETADO CON ÉXITO! DESCARGANDO...';
      progressBar.style.width = '100%';
      startBtn.textContent = 'DESCARGAR NUEVAMENTE';
      startBtn.disabled = false;

      // Trigger automatic browser download
      downloadAnchor.href = result.url;
      downloadAnchor.download = `kidcut_${selectedRatio.replace(':', 'x')}_${selectedResolution}p_${Date.now()}.${result.format}`;
      downloadAnchor.click();

    } catch (err) {
      console.error(err);
      msgStatus.textContent = `ERROR: ${err.message}`;
      startBtn.disabled = false;
      startBtn.textContent = 'REINTENTAR';
    }
  });

  return { open, close };
}
