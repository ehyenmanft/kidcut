/**
 * KIDCUT AUDIO ENGINE & 8-BIT CHIPTUNE SYNTHESIZER
 * Zero-dependency Web Audio API multi-track mixer & retro sound generator.
 */

export class AudioEngine {
  constructor() {
    this.ctx = null;
    this.masterGain = null;
    this.sfxEnabled = true;
    this.activeSources = new Map(); // clipId -> { sourceNode, gainNode }
  }

  init() {
    if (!this.ctx) {
      const AudioContextClass = window.AudioContext || window.webkitAudioContext;
      this.ctx = new AudioContextClass();
      this.masterGain = this.ctx.createGain();
      this.masterGain.gain.value = 1.0;
      this.masterGain.connect(this.ctx.destination);
    }
    if (this.ctx.state === 'suspended') {
      this.ctx.resume();
    }
  }

  setSfxEnabled(enabled) {
    this.sfxEnabled = enabled;
  }

  // UI Retro Chiptune SFX
  playBeep(freq = 440, type = 'square', duration = 0.08) {
    if (!this.sfxEnabled) return;
    this.init();
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, this.ctx.currentTime);
    gain.gain.setValueAtTime(0.12, this.ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + duration);
    osc.connect(gain);
    gain.connect(this.masterGain);
    osc.start();
    osc.stop(this.ctx.currentTime + duration);
  }

  playCoin() {
    if (!this.sfxEnabled) return;
    this.init();
    const now = this.ctx.currentTime;
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = 'square';
    osc.frequency.setValueAtTime(987.77, now); // B5
    osc.frequency.setValueAtTime(1318.51, now + 0.08); // E6
    gain.gain.setValueAtTime(0.18, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.4);
    osc.connect(gain);
    gain.connect(this.masterGain);
    osc.start();
    osc.stop(now + 0.4);
  }

  playLaser() {
    if (!this.sfxEnabled) return;
    this.init();
    const now = this.ctx.currentTime;
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(880, now);
    osc.frequency.exponentialRampToValueAtTime(110, now + 0.18);
    gain.gain.setValueAtTime(0.2, now);
    gain.gain.linearRampToValueAtTime(0.01, now + 0.18);
    osc.connect(gain);
    gain.connect(this.masterGain);
    osc.start();
    osc.stop(now + 0.18);
  }

  playJump() {
    if (!this.sfxEnabled) return;
    this.init();
    const now = this.ctx.currentTime;
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = 'square';
    osc.frequency.setValueAtTime(150, now);
    osc.frequency.linearRampToValueAtTime(600, now + 0.15);
    gain.gain.setValueAtTime(0.15, now);
    gain.gain.linearRampToValueAtTime(0.01, now + 0.15);
    osc.connect(gain);
    gain.connect(this.masterGain);
    osc.start();
    osc.stop(now + 0.15);
  }

  playPowerup() {
    if (!this.sfxEnabled) return;
    this.init();
    const notes = [330, 392, 659, 523, 587, 784];
    notes.forEach((freq, idx) => {
      setTimeout(() => {
        this.playBeep(freq, 'triangle', 0.08);
      }, idx * 60);
    });
  }

  playExplosion() {
    if (!this.sfxEnabled) return;
    this.init();
    const bufferSize = this.ctx.sampleRate * 0.3;
    const buffer = this.ctx.createBuffer(1, bufferSize, this.ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < bufferSize; i++) {
      data[i] = Math.random() * 2 - 1;
    }
    const noise = this.ctx.createBufferSource();
    noise.buffer = buffer;
    const filter = this.ctx.createBiquadFilter();
    filter.type = 'lowpass';
    filter.frequency.setValueAtTime(800, this.ctx.currentTime);
    filter.frequency.linearRampToValueAtTime(50, this.ctx.currentTime + 0.3);

    const gain = this.ctx.createGain();
    gain.gain.setValueAtTime(0.3, this.ctx.currentTime);
    gain.gain.linearRampToValueAtTime(0.01, this.ctx.currentTime + 0.3);

    noise.connect(filter);
    filter.connect(gain);
    gain.connect(this.masterGain);
    noise.start();
    noise.stop(this.ctx.currentTime + 0.3);
  }

  // Generate Audio Buffer for Chiptune SFX to insert into timeline as clips
  async generateSfxBuffer(type) {
    this.init();
    const sampleRate = 44100;
    let duration = 0.4;
    if (type === 'explosion') duration = 0.5;
    if (type === 'powerup') duration = 0.6;

    const offlineCtx = new OfflineAudioContext(1, sampleRate * duration, sampleRate);
    const gain = offlineCtx.createGain();
    gain.connect(offlineCtx.destination);

    if (type === 'coin') {
      const osc = offlineCtx.createOscillator();
      osc.type = 'square';
      osc.frequency.setValueAtTime(987.77, 0);
      osc.frequency.setValueAtTime(1318.51, 0.08);
      gain.gain.setValueAtTime(0.2, 0);
      gain.gain.exponentialRampToValueAtTime(0.001, 0.35);
      osc.connect(gain);
      osc.start(0);
      osc.stop(0.35);
    } else if (type === 'laser') {
      const osc = offlineCtx.createOscillator();
      osc.type = 'sawtooth';
      osc.frequency.setValueAtTime(880, 0);
      osc.frequency.exponentialRampToValueAtTime(110, 0.18);
      gain.gain.setValueAtTime(0.25, 0);
      gain.gain.linearRampToValueAtTime(0.01, 0.18);
      osc.connect(gain);
      osc.start(0);
      osc.stop(0.18);
    } else if (type === 'jump') {
      const osc = offlineCtx.createOscillator();
      osc.type = 'square';
      osc.frequency.setValueAtTime(150, 0);
      osc.frequency.linearRampToValueAtTime(600, 0.18);
      gain.gain.setValueAtTime(0.2, 0);
      gain.gain.linearRampToValueAtTime(0.01, 0.18);
      osc.connect(gain);
      osc.start(0);
      osc.stop(0.18);
    } else if (type === 'explosion') {
      const bufferSize = sampleRate * 0.4;
      const noiseBuffer = offlineCtx.createBuffer(1, bufferSize, sampleRate);
      const data = noiseBuffer.getChannelData(0);
      for (let i = 0; i < bufferSize; i++) data[i] = Math.random() * 2 - 1;
      const noise = offlineCtx.createBufferSource();
      noise.buffer = noiseBuffer;
      const filter = offlineCtx.createBiquadFilter();
      filter.type = 'lowpass';
      filter.frequency.setValueAtTime(600, 0);
      filter.frequency.linearRampToValueAtTime(40, 0.4);
      gain.gain.setValueAtTime(0.3, 0);
      gain.gain.linearRampToValueAtTime(0.01, 0.4);
      noise.connect(filter);
      filter.connect(gain);
      noise.start(0);
      noise.stop(0.4);
    } else {
      // Default beep
      const osc = offlineCtx.createOscillator();
      osc.type = 'square';
      osc.frequency.setValueAtTime(440, 0);
      gain.gain.setValueAtTime(0.2, 0);
      gain.gain.linearRampToValueAtTime(0.01, 0.15);
      osc.connect(gain);
      osc.start(0);
      osc.stop(0.15);
    }

    const renderedBuffer = await offlineCtx.startRendering();
    return renderedBuffer;
  }

  // Load Audio Buffer from File or URL
  async decodeAudioData(arrayBuffer) {
    this.init();
    return await this.ctx.decodeAudioData(arrayBuffer);
  }

  // Timeline audio playback management
  stopAll() {
    for (const [clipId, item] of this.activeSources.entries()) {
      try {
        item.sourceNode.stop();
        item.sourceNode.disconnect();
      } catch (e) {}
    }
    this.activeSources.clear();
  }

  playClipAudio(clip, currentTime) {
    if (!clip.audioBuffer || clip.isMuted) return;
    this.init();

    const clipStart = clip.start;
    const clipEnd = clip.start + clip.duration;
    if (currentTime < clipStart || currentTime >= clipEnd) return;

    const offsetInClip = (currentTime - clipStart) + (clip.trimIn || 0);
    if (offsetInClip < 0 || offsetInClip >= clip.audioBuffer.duration) return;

    this.stopClipAudio(clip.id);

    const source = this.ctx.createBufferSource();
    source.buffer = clip.audioBuffer;
    source.playbackRate.value = clip.speed || 1.0;

    const gain = this.ctx.createGain();
    const volume = (clip.volume !== undefined ? clip.volume : 1.0);
    gain.gain.value = volume;

    source.connect(gain);
    gain.connect(this.masterGain);

    const durationRemaining = (clip.duration - (currentTime - clipStart)) / (clip.speed || 1.0);
    source.start(0, offsetInClip, durationRemaining);

    this.activeSources.set(clip.id, { sourceNode: source, gainNode: gain });
  }

  stopClipAudio(clipId) {
    if (this.activeSources.has(clipId)) {
      const item = this.activeSources.get(clipId);
      try {
        item.sourceNode.stop();
        item.sourceNode.disconnect();
      } catch (e) {}
      this.activeSources.delete(clipId);
    }
  }
}

export const audioEngine = new AudioEngine();
