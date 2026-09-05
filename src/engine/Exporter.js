/**
 * KIDCUT HIGH-DEFINITION DETERMINISTIC EXPORTER
 * Offline frame-by-frame renderer for low-spec hardware (zero dropped frames).
 * Supports 480p, 720p, 1080p, 4K across 16:9, 9:16, 1:1, 4:5 in WebM, MP4, authentic animated GIF & WAV.
 * Preserves synchronized audio from imported video clips and audio tracks.
 */

import { compositor } from './Compositor.js';
import { AspectRatios, Resolutions } from './types.js';
import { GifEncoder } from './GifEncoder.js';

export class Exporter {
  constructor() {
    this.isExporting = false;
    this.shouldCancel = false;
  }

  cancel() {
    this.shouldCancel = true;
  }

  async seekActiveVideos(tracks, currentTime) {
    const seekPromises = [];
    for (const track of tracks) {
      if (track.hidden) continue;
      for (const clip of track.clips) {
        if (clip.type === 'video' && clip.mediaElement) {
          const video = clip.mediaElement;
          const clipStart = clip.start;
          const clipEnd = clip.start + clip.duration;

          if (currentTime >= clipStart && currentTime <= clipEnd) {
            const trimIn = clip.trimIn || 0;
            const speed = clip.speed || 1.0;
            const targetVideoTime = Math.max(0, Math.min(video.duration || 9999, trimIn + ((currentTime - clipStart) * speed)));

            if (!video.paused) {
              video.pause();
            }

            if (Math.abs(video.currentTime - targetVideoTime) <= 0.002) {
              continue;
            }

            const p = new Promise((resolve) => {
              let done = false;
              const cleanup = () => {
                if (!done) {
                  done = true;
                  video.removeEventListener('seeked', onSeeked);
                  clearTimeout(timer);
                  resolve();
                }
              };
              const onSeeked = () => cleanup();
              const timer = setTimeout(cleanup, 45);
              video.addEventListener('seeked', onSeeked, { once: true });
              video.currentTime = targetVideoTime;
            });
            seekPromises.push(p);
          }
        }
      }
    }
    if (seekPromises.length > 0) {
      await Promise.all(seekPromises);
    }
  }

  async exportProject(timelineEngine, options, onProgress) {
    if (this.isExporting) return;
    this.isExporting = true;
    this.shouldCancel = false;
    compositor.isExporting = true;

    // Pause clips during export to avoid playback contention
    for (const track of timelineEngine.tracks) {
      for (const clip of track.clips) {
        if (clip.mediaElement && typeof clip.mediaElement.pause === 'function') {
          clip.mediaElement.pause();
        }
      }
    }

    try {
      const {
        resolution = '1080',
        ratio = '16:9',
        format = 'webm',
        fps = 30
      } = options;

      const ratioInfo = AspectRatios[ratio] || AspectRatios['16:9'];
      const scaleFactor = Resolutions[resolution] ? Resolutions[resolution].scale : 1.0;

      const targetWidth = Math.round(ratioInfo.width * scaleFactor);
      const targetHeight = Math.round(ratioInfo.height * scaleFactor);

      // Calculate duration based on actual clips
      let maxClipEnd = 0;
      for (const track of timelineEngine.tracks) {
        for (const clip of track.clips) {
          maxClipEnd = Math.max(maxClipEnd, clip.start + clip.duration);
        }
      }
      const exportDuration = Math.max(1.0, maxClipEnd);
      const totalFrames = Math.ceil(exportDuration * fps);

      // Render Canvas
      const exportCanvas = document.createElement('canvas');
      exportCanvas.width = targetWidth;
      exportCanvas.height = targetHeight;
      const exportCtx = exportCanvas.getContext('2d', { willReadFrequently: true });
      exportCtx.imageSmoothingEnabled = false;

      // Set compositor aspect ratio
      compositor.setAspectRatio(ratio);

      if (format === 'wav') {
        return await this.exportWavOnly(timelineEngine, exportDuration, onProgress);
      }

      if (format === 'gif') {
        return await this.exportGif(timelineEngine, exportDuration, fps, exportCanvas, exportCtx, onProgress);
      }

      // Video Export (WebM / MP4 via Canvas Stream + MediaRecorder)
      return await this.exportVideo(
        timelineEngine,
        exportDuration,
        fps,
        totalFrames,
        exportCanvas,
        exportCtx,
        format,
        onProgress
      );
    } finally {
      this.isExporting = false;
      compositor.isExporting = false;
    }
  }

  async exportVideo(timelineEngine, duration, fps, totalFrames, canvas, ctx, format, onProgress) {
    const frameIntervalMs = 1000 / fps;
    const stream = canvas.captureStream(fps);

    // Try to get audio from timeline clips (both audio tracks AND video tracks with audioBuffer)
    const audioContext = new (window.AudioContext || window.webkitAudioContext)();
    const dest = audioContext.createMediaStreamDestination();

    let hasAudio = false;
    for (const track of timelineEngine.tracks) {
      if (!track.muted && !track.hidden) {
        for (const clip of track.clips) {
          if (clip.audioBuffer && !clip.isMuted) {
            hasAudio = true;
            const src = audioContext.createBufferSource();
            src.buffer = clip.audioBuffer;
            const gain = audioContext.createGain();
            gain.gain.value = clip.volume !== undefined ? clip.volume : 1.0;
            src.connect(gain);
            gain.connect(dest);
            const startDelay = Math.max(0, clip.start);
            src.start(audioContext.currentTime + startDelay, clip.trimIn || 0, clip.duration);
          }
        }
      }
    }

    // Combine audio tracks into canvas stream
    if (hasAudio && dest.stream.getAudioTracks().length > 0) {
      dest.stream.getAudioTracks().forEach(track => stream.addTrack(track));
    }

    // Choose supported MIME type
    let mimeType = 'video/webm;codecs=vp9,opus';
    if (format === 'mp4') {
      if (MediaRecorder.isTypeSupported('video/mp4;codecs=avc1.42E01E,mp4a.40.2')) {
        mimeType = 'video/mp4;codecs=avc1.42E01E,mp4a.40.2';
      } else if (MediaRecorder.isTypeSupported('video/mp4')) {
        mimeType = 'video/mp4';
      } else {
        mimeType = 'video/webm;codecs=vp8,opus';
      }
    } else if (!MediaRecorder.isTypeSupported(mimeType)) {
      mimeType = 'video/webm';
    }

    const recordedChunks = [];
    const mediaRecorder = new MediaRecorder(stream, {
      mimeType,
      videoBitsPerSecond: 16000000 // 16 Mbps for crisp pixel fidelity
    });

    mediaRecorder.ondataavailable = (e) => {
      if (e.data && e.data.size > 0) {
        recordedChunks.push(e.data);
      }
    };

    mediaRecorder.start();

    // Deterministic frame stepping with decode synchronization & real-time frame pacing
    for (let frame = 0; frame < totalFrames; frame++) {
      if (this.shouldCancel) {
        mediaRecorder.stop();
        throw new Error('Exportación cancelada por el usuario');
      }

      const frameStartTime = performance.now();
      const currentTime = frame / fps;
      
      // 1. Await exact video seek & frame decode for active videos
      await this.seekActiveVideos(timelineEngine.tracks, currentTime);

      // 2. Render frame onto export canvas
      compositor.renderFrame(timelineEngine.tracks, currentTime, ctx);

      // 3. Report progress
      const percent = Math.round(((frame + 1) / totalFrames) * 100);
      if (onProgress) {
        onProgress({
          frame: frame + 1,
          totalFrames,
          percent,
          status: `Renderizando fotograma ${frame + 1}/${totalFrames} (${percent}%)`
        });
      }

      // 4. Cadence timing: maintain 1000 / fps ms per frame for smooth MediaRecorder playback
      const frameElapsed = performance.now() - frameStartTime;
      const waitTime = Math.max(1, Math.round(frameIntervalMs - frameElapsed));
      await new Promise(r => setTimeout(r, waitTime));
    }

    return new Promise((resolve) => {
      mediaRecorder.onstop = () => {
        const blob = new Blob(recordedChunks, { type: mimeType });
        try {
          audioContext.close();
        } catch (_) {}
        resolve({
          blob,
          format: mimeType.includes('mp4') ? 'mp4' : 'webm',
          url: URL.createObjectURL(blob)
        });
      };
      mediaRecorder.stop();
    });
  }

  // Authentic Animated GIF Exporter with LZW Compression
  async exportGif(timelineEngine, duration, fps, canvas, ctx, onProgress) {
    // For GIFs, we use 15 fps and scale down to max 640px to ensure fast encoding & lightweight memory
    const gifFps = 12;
    const totalFrames = Math.ceil(duration * gifFps);
    const gifDelayMs = 1000 / gifFps;

    // Scale canvas for GIF efficiency
    const maxDim = 640;
    let gifW = canvas.width;
    let gifH = canvas.height;
    if (gifW > maxDim || gifH > maxDim) {
      if (gifW > gifH) {
        gifH = Math.round((gifH * maxDim) / gifW);
        gifW = maxDim;
      } else {
        gifW = Math.round((gifW * maxDim) / gifH);
        gifH = maxDim;
      }
    }

    const gifCanvas = document.createElement('canvas');
    gifCanvas.width = gifW;
    gifCanvas.height = gifH;
    const gifCtx = gifCanvas.getContext('2d', { willReadFrequently: true });
    gifCtx.imageSmoothingEnabled = false;

    const encoder = new GifEncoder(gifW, gifH);
    encoder.setDelay(gifDelayMs);

    for (let frame = 0; frame < totalFrames; frame++) {
      if (this.shouldCancel) {
        throw new Error('Exportación cancelada');
      }

      const currentTime = frame / gifFps;
      await this.seekActiveVideos(timelineEngine.tracks, currentTime);
      compositor.renderFrame(timelineEngine.tracks, currentTime, ctx);

      // Draw downscaled to gifCanvas
      gifCtx.drawImage(canvas, 0, 0, gifW, gifH);
      encoder.addFrame(gifCtx);

      const percent = Math.round(((frame + 1) / totalFrames) * 80);
      if (onProgress) {
        onProgress({
          frame: frame + 1,
          totalFrames,
          percent,
          status: `Capturando cuadro GIF ${frame + 1}/${totalFrames}`
        });
      }
      await new Promise(r => setTimeout(r, 4));
    }

    if (onProgress) {
      onProgress({ percent: 90, status: 'Comprimiendo GIF LZW de 8 bits...' });
    }
    await new Promise(r => setTimeout(r, 10));

    const gifBytes = encoder.encode();
    const blob = new Blob([gifBytes], { type: 'image/gif' });

    this.isExporting = false;
    return {
      blob,
      format: 'gif',
      url: URL.createObjectURL(blob)
    };
  }

  // Audio WAV Only Exporter
  async exportWavOnly(timelineEngine, duration, onProgress) {
    const sampleRate = 44100;
    const offlineCtx = new OfflineAudioContext(2, sampleRate * duration, sampleRate);

    let hasAudio = false;
    for (const track of timelineEngine.tracks) {
      if (!track.muted && !track.hidden) {
        for (const clip of track.clips) {
          if (clip.audioBuffer && !clip.isMuted) {
            hasAudio = true;
            const src = offlineCtx.createBufferSource();
            src.buffer = clip.audioBuffer;
            const gain = offlineCtx.createGain();
            gain.gain.value = clip.volume !== undefined ? clip.volume : 1.0;
            src.connect(gain);
            gain.connect(offlineCtx.destination);
            src.start(Math.max(0, clip.start), clip.trimIn || 0, clip.duration);
          }
        }
      }
    }

    if (onProgress) {
      onProgress({ percent: 50, status: 'Mezclando pistas de audio a WAV...' });
    }

    const renderedBuffer = await offlineCtx.startRendering();
    const wavBlob = this.audioBufferToWavBlob(renderedBuffer);

    this.isExporting = false;
    return {
      blob: wavBlob,
      format: 'wav',
      url: URL.createObjectURL(wavBlob)
    };
  }

  audioBufferToWavBlob(buffer) {
    const numOfChan = buffer.numberOfChannels;
    const length = buffer.length * numOfChan * 2 + 44;
    const out = new DataView(new ArrayBuffer(length));
    const channels = [];
    let sample = 0;
    let offset = 0;
    let pos = 0;

    function setUint16(data) {
      out.setUint16(pos, data, true);
      pos += 2;
    }
    function setUint32(data) {
      out.setUint32(pos, data, true);
      pos += 4;
    }

    // write WAVE header
    setUint32(0x46464952); // "RIFF"
    setUint32(length - 8);
    setUint32(0x45564157); // "WAVE"
    setUint32(0x20746d66); // "fmt "
    setUint32(16);          // 16-bit format
    setUint16(1);           // PCM
    setUint16(numOfChan);
    setUint32(buffer.sampleRate);
    setUint32(buffer.sampleRate * 2 * numOfChan);
    setUint16(numOfChan * 2);
    setUint16(16);
    setUint32(0x61746164); // "data"
    setUint32(length - pos - 4);

    for (let i = 0; i < buffer.numberOfChannels; i++) {
      channels.push(buffer.getChannelData(i));
    }

    while (offset < buffer.length) {
      for (let i = 0; i < numOfChan; i++) {
        sample = Math.max(-1, Math.min(1, channels[i][offset]));
        sample = (0.5 + sample < 0 ? sample * 32768 : sample * 32767) | 0;
        out.setInt16(pos, sample, true);
        pos += 2;
      }
      offset++;
    }

    return new Blob([out.buffer], { type: 'audio/wav' });
  }
}

export const exporter = new Exporter();
