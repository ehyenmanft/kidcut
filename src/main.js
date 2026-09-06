/**
 * KIDCUT - MAIN ENTRYPOINT
 * Initializes retro engines, UI controllers & starter arcade demo project.
 */

import { timelineEngine } from './engine/TimelineEngine.js';
import { audioEngine } from './engine/AudioEngine.js';
import { setupSplashScreen } from './ui/SplashScreen.js';
import { setupHeaderBar } from './ui/HeaderBar.js';
import { setupAssetLibrary } from './ui/AssetLibrary.js';
import { setupPreviewCanvas } from './ui/PreviewCanvas.js';
import { setupTimelineView } from './ui/TimelineView.js';
import { setupInspectorView } from './ui/InspectorView.js';
import { setupExportModal } from './ui/ExportModal.js';
import { setupVoiceRecorder } from './ui/VoiceRecorder.js';

window.addEventListener('DOMContentLoaded', async () => {
  // 1. Setup Preview & Controllers
  const preview = setupPreviewCanvas(timelineEngine);
  const exportModal = setupExportModal(timelineEngine);
  setupHeaderBar(timelineEngine, () => exportModal.open());
  setupAssetLibrary(timelineEngine);
  setupTimelineView(timelineEngine);
  setupInspectorView(timelineEngine);
  setupVoiceRecorder(timelineEngine);

  // 2. Load Demo Starter Project (Instant arcade playground)
  await loadDemoProject();

  // 3. Initialize Retro Splash Screen
  setupSplashScreen(() => {
    preview.resizeCanvas();
  });
});

async function loadDemoProject() {
  // Generate sample starter audio buffers
  const coinBuffer = await audioEngine.generateSfxBuffer('coin');
  const laserBuffer = await audioEngine.generateSfxBuffer('laser');
  const powerupBuffer = await audioEngine.generateSfxBuffer('powerup');

  // Video Track V1: Base clips
  timelineEngine.addClip('track-v1', {
    name: 'INTRO RETRO (V1)',
    type: 'color',
    color: '#1a103c',
    start: 0,
    duration: 3.5
  });

  timelineEngine.addClip('track-v1', {
    name: 'ESCENA 2 (PIXEL)',
    type: 'color',
    color: '#0d2b38',
    start: 3.5,
    duration: 4.5,
    filter: 'pixelate',
    filterIntensity: 12
  });

  // Video Track V2: 8-Bit Text & Overlays
  timelineEngine.addClip('track-v2', {
    name: '★ KIDCUT STUDIO ★',
    type: 'text',
    text: '★ KIDCUT STUDIO ★',
    textColor: '#ffd200',
    strokeColor: '#000000',
    fontSize: 54,
    start: 0.2,
    duration: 3.2,
    y: 0.4
  });

  timelineEngine.addClip('track-v2', {
    name: 'Sticker Alien',
    type: 'sticker',
    emoji: '👾',
    start: 3.8,
    duration: 3.5,
    scale: 1.6,
    x: 0.5,
    y: 0.45
  });

  // Audio Track A2: Chiptune sound effects
  timelineEngine.addClip('track-a2', {
    name: 'SFX MONEDA',
    type: 'audio',
    audioBuffer: coinBuffer,
    start: 0.2,
    duration: coinBuffer.duration
  });

  timelineEngine.addClip('track-a2', {
    name: 'SFX LÁSER',
    type: 'audio',
    audioBuffer: laserBuffer,
    start: 3.8,
    duration: laserBuffer.duration
  });

  timelineEngine.addClip('track-a2', {
    name: 'SFX POWERUP',
    type: 'audio',
    audioBuffer: powerupBuffer,
    start: 5.5,
    duration: powerupBuffer.duration
  });

  timelineEngine.seek(0);
}
