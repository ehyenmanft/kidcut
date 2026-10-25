/**
 * KIDCUT CONVERT MODAL
 * Re-encodes any local video, audio or image to another format through the native FFmpeg backend.
 */

import { audioEngine } from '../engine/AudioEngine.js';
import { nativeFetch, pickNativeFile } from '../engine/NativeBridge.js';

export function setupConvertModal() {
  const modal = document.getElementById('convert-modal');
  const sourceLabel = document.getElementById('convert-source');
  const statusLabel = document.getElementById('convert-status');
  const startBtn = document.getElementById('btn-start-convert');
  const revealBtn = document.getElementById('btn-reveal-convert');
  const formatButtons = modal.querySelectorAll('.convert-format-group .pixel-opt-btn');

  let sourcePath = null;
  let outputPath = null;
  let selectedFormat = 'mp4';
  let busy = false;

  function setSource(path) {
    sourcePath = path;
    sourceLabel.textContent = path || 'NINGUNO';
    startBtn.disabled = !path || busy;
  }

  function setStatus(text) {
    statusLabel.classList.toggle('hidden', !text);
    statusLabel.textContent = text || '';
  }

  // One selection across the video / audio / image groups
  formatButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      formatButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      selectedFormat = btn.dataset.format;
      audioEngine.playBeep(520, 'square', 0.04);
    });
  });

  function open(path = null) {
    if (path) setSource(path);
    revealBtn.classList.add('hidden');
    setStatus('');
    modal.classList.remove('hidden');
  }

  function close() {
    modal.classList.add('hidden');
  }

  document.getElementById('btn-close-convert').addEventListener('click', close);
  document.getElementById('btn-cancel-convert').addEventListener('click', close);
  document.getElementById('btn-convert-modal').addEventListener('click', () => open());

  document.getElementById('btn-convert-pick').addEventListener('click', async () => {
    const data = await pickNativeFile();
    if (data && data.success && data.filePath) {
      setSource(data.filePath);
      return;
    }
    if (!data || !data.cancelled) setStatus('⚠️ Abre KidCut.exe para convertir archivos con FFmpeg nativo.');
  });

  revealBtn.addEventListener('click', () => {
    if (outputPath) nativeFetch(`/api/reveal?path=${encodeURIComponent(outputPath)}`, { method: 'POST' }).catch(() => {});
  });

  startBtn.addEventListener('click', async () => {
    if (!sourcePath || busy) return;
    busy = true;
    startBtn.disabled = true;
    revealBtn.classList.add('hidden');
    setStatus(`⏳ CONVIRTIENDO A ${selectedFormat.toUpperCase()}... (los videos largos pueden tardar)`);
    try {
      const res = await nativeFetch('/api/convert', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: sourcePath, format: selectedFormat })
      });
      const data = await res.json();
      if (!data.success) throw new Error(data.error || 'Error desconocido');
      outputPath = data.outputPath;
      setStatus(`✔ LISTO: ${outputPath}`);
      revealBtn.classList.remove('hidden');
      audioEngine.playPowerup();
    } catch (err) {
      setStatus(`ERROR: ${err.message}`);
    } finally {
      busy = false;
      startBtn.disabled = !sourcePath;
    }
  });

  window.__kidcutOpenConverter = open;
  return { open, close };
}
