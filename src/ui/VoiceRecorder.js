/**
 * KIDCUT VOICE RECORDER
 * Captures microphone audio, visualizes live VU-meter, applies optional retro voice effects,
 * and inserts recorded voiceover directly onto an audio track at playhead position.
 */

import { audioEngine } from '../engine/AudioEngine.js';

export function setupVoiceRecorder(timelineEngine) {
  const modal = document.getElementById('voice-recorder-modal');
  const openBtn = document.getElementById('btn-tool-record-voice');
  const closeBtn = document.getElementById('btn-close-voice-modal');
  const cancelBtn = document.getElementById('btn-record-cancel');
  const startBtn = document.getElementById('btn-record-start');
  const stopBtn = document.getElementById('btn-record-stop');
  const insertBtn = document.getElementById('btn-record-insert');
  const timerDisplay = document.getElementById('voice-timer-display');
  const statusMsg = document.getElementById('voice-status-msg');
  const vuLevel = document.getElementById('voice-vu-level');
  const effectSelect = document.getElementById('voice-effect-select');
  const audioPreview = document.getElementById('voice-audio-preview');

  let mediaStream = null;
  let mediaRecorder = null;
  let recordedChunks = [];
  let recordedBlob = null;
  let recordedBuffer = null;
  let audioContext = null;
  let analyser = null;
  let animFrameId = null;
  let recordStartTime = 0;
  let timerInterval = null;

  function open() {
    modal.classList.remove('hidden');
    resetState();
    requestMicAccess();
    audioEngine.playCoin();
  }

  function close() {
    stopRecording(true);
    stopMicStream();
    modal.classList.add('hidden');
  }

  if (openBtn) {
    openBtn.addEventListener('click', open);
  }
  if (closeBtn) {
    closeBtn.addEventListener('click', close);
  }
  if (cancelBtn) {
    cancelBtn.addEventListener('click', close);
  }

  function resetState() {
    recordedChunks = [];
    recordedBlob = null;
    recordedBuffer = null;
    timerDisplay.textContent = '00:00.0';
    statusMsg.textContent = 'CONECTANDO MICRÓFONO...';
    startBtn.disabled = true;
    stopBtn.disabled = true;
    insertBtn.disabled = true;
    if (audioPreview) {
      audioPreview.classList.add('hidden');
      audioPreview.src = '';
    }
  }

  async function requestMicAccess() {
    try {
      mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
      audioEngine.init();

      const AudioCtxClass = window.AudioContext || window.webkitAudioContext;
      audioContext = new AudioCtxClass();
      const micSource = audioContext.createMediaStreamSource(mediaStream);
      analyser = audioContext.createAnalyser();
      analyser.fftSize = 256;
      micSource.connect(analyser);

      startVuMeter();
      statusMsg.textContent = '● LISTO PARA GRABAR (HABLA PARA PROBAR MIC)';
      startBtn.disabled = false;
    } catch (err) {
      console.error('Error accediendo al micrófono:', err);
      statusMsg.textContent = '⚠️ ACCESO DENEGADO O NO HAY MICRÓFONO DISPONIBLE';
      alert('No se pudo acceder al micrófono. Asegúrate de permitir el acceso en el navegador.');
    }
  }

  function startVuMeter() {
    if (!analyser) return;
    const dataArray = new Uint8Array(analyser.frequencyBinCount);

    const updateVu = () => {
      analyser.getByteFrequencyData(dataArray);
      let sum = 0;
      for (let i = 0; i < dataArray.length; i++) {
        sum += dataArray[i];
      }
      const avg = sum / dataArray.length;
      const percent = Math.min(100, Math.round((avg / 128) * 100));
      if (vuLevel) {
        vuLevel.style.width = `${percent}%`;
        if (percent > 80) {
          vuLevel.style.background = '#ff3344';
        } else if (percent > 45) {
          vuLevel.style.background = '#ffd200';
        } else {
          vuLevel.style.background = '#00f0ff';
        }
      }
      animFrameId = requestAnimationFrame(updateVu);
    };
    animFrameId = requestAnimationFrame(updateVu);
  }

  function stopMicStream() {
    if (animFrameId) {
      cancelAnimationFrame(animFrameId);
      animFrameId = null;
    }
    if (mediaStream) {
      mediaStream.getTracks().forEach(t => t.stop());
      mediaStream = null;
    }
    if (audioContext) {
      try { audioContext.close(); } catch (_) {}
      audioContext = null;
    }
    if (vuLevel) vuLevel.style.width = '0%';
  }

  // Record Actions
  startBtn.addEventListener('click', () => {
    if (!mediaStream) return;
    recordedChunks = [];
    recordedBlob = null;
    recordedBuffer = null;

    let mime = 'audio/webm';
    if (!MediaRecorder.isTypeSupported(mime)) mime = '';

    mediaRecorder = new MediaRecorder(mediaStream, mime ? { mimeType: mime } : undefined);
    mediaRecorder.ondataavailable = (e) => {
      if (e.data && e.data.size > 0) {
        recordedChunks.push(e.data);
      }
    };

    mediaRecorder.onstop = async () => {
      recordedBlob = new Blob(recordedChunks, { type: mediaRecorder.mimeType || 'audio/webm' });
      const arrayBuffer = await recordedBlob.arrayBuffer();
      try {
        audioEngine.init();
        recordedBuffer = await audioEngine.decodeAudioData(arrayBuffer);
        if (audioPreview) {
          audioPreview.src = URL.createObjectURL(recordedBlob);
          audioPreview.classList.remove('hidden');
        }
        statusMsg.textContent = `¡GRABACIÓN FINALIZADA! (${recordedBuffer.duration.toFixed(1)}s)`;
        insertBtn.disabled = false;
        audioEngine.playPowerup();
      } catch (err) {
        console.error('Error decodificando audio grabado:', err);
        statusMsg.textContent = 'Error al procesar la grabación.';
      }
    };

    mediaRecorder.start(100);
    recordStartTime = performance.now();
    statusMsg.textContent = '🔴 GRABANDO VOZ EN DIRECTO...';
    startBtn.disabled = true;
    stopBtn.disabled = false;
    insertBtn.disabled = true;

    audioEngine.playBeep(660, 'square', 0.1);

    timerInterval = setInterval(() => {
      const elapsedSec = (performance.now() - recordStartTime) / 1000;
      const mins = Math.floor(elapsedSec / 60);
      const secs = (elapsedSec % 60).toFixed(1);
      timerDisplay.textContent = `${String(mins).padStart(2, '0')}:${String(secs).padStart(4, '0')}`;
    }, 100);
  });

  function stopRecording(isCancelled = false) {
    if (timerInterval) {
      clearInterval(timerInterval);
      timerInterval = null;
    }
    if (mediaRecorder && mediaRecorder.state === 'recording') {
      mediaRecorder.stop();
    }
    startBtn.disabled = false;
    stopBtn.disabled = true;
    if (!isCancelled) {
      audioEngine.playBeep(440, 'square', 0.08);
    }
  }

  stopBtn.addEventListener('click', () => {
    stopRecording(false);
  });

  // Insert Voice Clip into Timeline
  insertBtn.addEventListener('click', () => {
    if (!recordedBuffer) return;

    // Find first available audio track or create a new one
    let targetTrack = timelineEngine.tracks.find(t => t.type === 'audio');
    if (!targetTrack) {
      targetTrack = timelineEngine.addTrack('audio');
    }

    const playheadTime = timelineEngine.currentTime;
    const selectedEffect = effectSelect ? effectSelect.value : 'none';

    timelineEngine.pushState('Añadir Grabación de Voz');
    const newVoiceClip = {
      name: `🎙️ Voz (${(timelineEngine.tracks.reduce((acc, t) => acc + t.clips.length, 0) + 1)})`,
      type: 'audio',
      start: playheadTime,
      duration: recordedBuffer.duration,
      trimIn: 0,
      speed: 1.0,
      volume: 1.0,
      isMuted: false,
      audioBuffer: recordedBuffer,
      audioEffect: selectedEffect,
      effectIntensity: 50
    };

    timelineEngine.addClip(targetTrack.id, newVoiceClip, true);
    timelineEngine.calculateTotalDuration();
    timelineEngine.notify('trackschange', { tracks: timelineEngine.tracks });

    audioEngine.playCoin();
    close();
  });

  return { open, close };
}
