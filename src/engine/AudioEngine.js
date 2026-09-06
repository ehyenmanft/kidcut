/**
 * KIDCUT AUDIO ENGINE & 8-BIT CHIPTUNE SYNTHESIZER
 * Zero-dependency Web Audio API multi-track mixer & retro sound generator.
 * Full 100 Audio Effects & DSP Modulation Processing Graph.
 */

import { ALL_AUDIO_EFFECTS, AUDIO_EFFECT_CATEGORIES } from './EffectsDatabase.js';

export function getPitchMultiplier(effectId) {
  switch (effectId) {
    case 'helium_voice':
    case 'pitch_high':
      return 1.35;
    case 'deep_monster':
    case 'pitch_low':
      return 0.75;
    case 'demon_growl':
      return 0.65;
    case 'giant_slow':
      return 0.55;
    case 'pitch_fifth_up':
      return 1.4983; // 2^(7/12)
    case 'pitch_octave_down':
      return 0.50; // 2^(-12/12)
    case 'game_over_pitch':
      return 0.72;
    default:
      return 1.0;
  }
}

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
    filter.frequency.setValueAtTime(600, this.ctx.currentTime);
    filter.frequency.linearRampToValueAtTime(40, this.ctx.currentTime + 0.3);
    const gain = this.ctx.createGain();
    gain.gain.setValueAtTime(0.25, this.ctx.currentTime);
    gain.gain.linearRampToValueAtTime(0.01, this.ctx.currentTime + 0.3);
    noise.connect(filter);
    filter.connect(gain);
    gain.connect(this.masterGain);
    noise.start();
    noise.stop(this.ctx.currentTime + 0.3);
  }

  // Generate an AudioBuffer of an 8-bit sound for adding to timeline track
  async generateSfxBuffer(type) {
    const sampleRate = 44100;
    const offlineCtx = new OfflineAudioContext(1, sampleRate * 0.5, sampleRate);
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

  // =========================================================================
  // DSP HELPER BUILDERS
  // =========================================================================

  makeDistortionCurve(amount) {
    const k = amount;
    const n_samples = 44100;
    const curve = new Float32Array(n_samples);
    const deg = Math.PI / 180;
    for (let i = 0; i < n_samples; ++i) {
      const x = (i * 2) / n_samples - 1;
      curve[i] = ((3 + k) * x * 20 * deg) / (Math.PI + k * Math.abs(x));
    }
    return curve;
  }

  makeHardClipCurve(threshold = 0.35) {
    const n_samples = 4096;
    const curve = new Float32Array(n_samples);
    for (let i = 0; i < n_samples; i++) {
      const x = (i / 2048) - 1;
      curve[i] = Math.max(-threshold, Math.min(threshold, x * 2.5)) / threshold;
    }
    return curve;
  }

  buildImpulseResponse(ctx, duration = 1.5, decayExp = 2.5, lpFreq = 8000) {
    const sampleRate = ctx.sampleRate;
    const length = Math.round(sampleRate * duration);
    const impulse = ctx.createBuffer(2, length, sampleRate);
    for (let i = 0; i < 2; i++) {
      const channel = impulse.getChannelData(i);
      for (let j = 0; j < length; j++) {
        channel[j] = (Math.random() * 2 - 1) * Math.pow(1 - j / length, decayExp);
      }
    }
    return impulse;
  }

  buildBitcrushNode(ctx, sourceNode, bits = 4, lpFreq = 3200) {
    const steps = Math.pow(2, bits);
    const curve = new Float32Array(4096);
    for (let i = 0; i < 4096; i++) {
      const x = (i / 2048) - 1;
      curve[i] = Math.round(x * steps) / steps;
    }
    const shaper = ctx.createWaveShaper();
    shaper.curve = curve;

    const lp = ctx.createBiquadFilter();
    lp.type = 'lowpass';
    lp.frequency.setValueAtTime(lpFreq, ctx.currentTime || 0);

    sourceNode.connect(shaper);
    shaper.connect(lp);
    return lp;
  }

  buildReverbNode(ctx, sourceNode, duration = 2.0, wetRatio = 0.5, lpFreq = 5000) {
    const convolver = ctx.createConvolver();
    convolver.buffer = this.buildImpulseResponse(ctx, duration);

    const dryGain = ctx.createGain();
    dryGain.gain.value = 1.0 - wetRatio * 0.5;

    const wetGain = ctx.createGain();
    wetGain.gain.value = wetRatio;

    const filter = ctx.createBiquadFilter();
    filter.type = 'lowpass';
    filter.frequency.setValueAtTime(lpFreq, ctx.currentTime || 0);

    const output = ctx.createGain();
    sourceNode.connect(dryGain);
    dryGain.connect(output);

    sourceNode.connect(convolver);
    convolver.connect(filter);
    filter.connect(wetGain);
    wetGain.connect(output);

    return output;
  }

  buildDelayNode(ctx, sourceNode, delayTime = 0.25, feedbackAmt = 0.4, wetAmt = 0.5, lpFeedback = 4000) {
    const delay = ctx.createDelay(2.0);
    delay.delayTime.setValueAtTime(delayTime, ctx.currentTime || 0);

    const feedback = ctx.createGain();
    feedback.gain.setValueAtTime(feedbackAmt, ctx.currentTime || 0);

    const filter = ctx.createBiquadFilter();
    filter.type = 'lowpass';
    filter.frequency.setValueAtTime(lpFeedback, ctx.currentTime || 0);

    const wetGain = ctx.createGain();
    wetGain.gain.setValueAtTime(wetAmt, ctx.currentTime || 0);

    const dryGain = ctx.createGain();
    dryGain.gain.value = 1.0;

    const output = ctx.createGain();

    sourceNode.connect(dryGain);
    dryGain.connect(output);

    sourceNode.connect(delay);
    delay.connect(filter);
    filter.connect(feedback);
    feedback.connect(delay);
    filter.connect(wetGain);
    wetGain.connect(output);

    return output;
  }

  buildRingModulator(ctx, sourceNode, freq = 50, blend = 0.6) {
    const carrier = ctx.createOscillator();
    carrier.type = 'sine';
    carrier.frequency.setValueAtTime(freq, ctx.currentTime || 0);

    const modGain = ctx.createGain();
    modGain.gain.value = blend;

    carrier.connect(modGain.gain);
    sourceNode.connect(modGain);
    try { carrier.start(); } catch (_) {}

    const shaper = ctx.createWaveShaper();
    shaper.curve = this.makeDistortionCurve(20);
    modGain.connect(shaper);
    return shaper;
  }

  buildTremoloNode(ctx, sourceNode, rate = 5, depth = 0.7, type = 'sine') {
    const osc = ctx.createOscillator();
    osc.type = type;
    osc.frequency.setValueAtTime(rate, ctx.currentTime || 0);

    const gainNode = ctx.createGain();
    gainNode.gain.value = 1 - (depth / 2);

    const lfoGain = ctx.createGain();
    lfoGain.gain.value = depth / 2;

    osc.connect(lfoGain);
    lfoGain.connect(gainNode.gain);

    sourceNode.connect(gainNode);
    try { osc.start(); } catch (_) {}
    return gainNode;
  }

  buildFlangerNode(ctx, sourceNode, delayTime = 0.005, rate = 0.5, depth = 0.003, feedback = 0.6) {
    const delay = ctx.createDelay(0.05);
    delay.delayTime.setValueAtTime(delayTime, ctx.currentTime || 0);

    const lfo = ctx.createOscillator();
    lfo.type = 'sine';
    lfo.frequency.setValueAtTime(rate, ctx.currentTime || 0);

    const lfoGain = ctx.createGain();
    lfoGain.gain.value = depth;

    lfo.connect(lfoGain);
    lfoGain.connect(delay.delayTime);

    const fb = ctx.createGain();
    fb.gain.value = feedback;

    const dry = ctx.createGain();
    dry.gain.value = 0.7;

    const wet = ctx.createGain();
    wet.gain.value = 0.7;

    const out = ctx.createGain();

    sourceNode.connect(dry);
    dry.connect(out);

    sourceNode.connect(delay);
    delay.connect(fb);
    fb.connect(delay);
    delay.connect(wet);
    wet.connect(out);

    try { lfo.start(); } catch (_) {}
    return out;
  }

  buildDistortionNode(ctx, sourceNode, amount = 30, postLpFreq = 5000, postHpFreq = 200) {
    const shaper = ctx.createWaveShaper();
    shaper.curve = this.makeDistortionCurve(amount);
    shaper.oversample = '4x';

    const hp = ctx.createBiquadFilter();
    hp.type = 'highpass';
    hp.frequency.setValueAtTime(postHpFreq, ctx.currentTime || 0);

    const lp = ctx.createBiquadFilter();
    lp.type = 'lowpass';
    lp.frequency.setValueAtTime(postLpFreq, ctx.currentTime || 0);

    sourceNode.connect(hp);
    hp.connect(shaper);
    shaper.connect(lp);
    return lp;
  }

  // =========================================================================
  // 100 AUDIO EFFECTS NODE GRAPH ROUTER
  // =========================================================================
  createEffectChain(ctx, sourceNode, effectId = 'none', intensity = 50) {
    if (!effectId || effectId === 'none') {
      return sourceNode;
    }

    const norm = Math.max(0.1, Math.min(1.0, intensity / 100));

    switch (effectId) {
      // ---------------------------------------------------------------------
      // 1. Chiptune & Gaming (15)
      // ---------------------------------------------------------------------
      case 'gameboy_apu':
        return this.buildBitcrushNode(ctx, sourceNode, 4, 3500);
      case 'nes_triangle': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(700, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }
      case 'snes_spc700': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(4200, ctx.currentTime || 0);
        lp.Q.setValueAtTime(1.8, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }
      case 'c64_sid_filter': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1100, ctx.currentTime || 0);
        bp.Q.setValueAtTime(4.2 * norm, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return bp;
      }
      case 'arcade_cabinet': {
        const peak = ctx.createBiquadFilter();
        peak.type = 'peaking';
        peak.frequency.setValueAtTime(1000, ctx.currentTime || 0);
        peak.gain.setValueAtTime(8 * norm, ctx.currentTime || 0);
        sourceNode.connect(peak);
        return peak;
      }
      case 'atari_pokey': {
        const curve = this.makeHardClipCurve(0.2);
        const shaper = ctx.createWaveShaper();
        shaper.curve = curve;
        return this.buildBitcrushNode(ctx, sourceNode, 3, 2800);
      }
      case 'bitcrush_8bit':
      case 'bitcrusher':
        return this.buildBitcrushNode(ctx, sourceNode, Math.round(8 - norm * 3), 4500);
      case 'bitcrush_4bit':
        return this.buildBitcrushNode(ctx, sourceNode, 4, 3000);
      case 'bitcrush_2bit':
        return this.buildBitcrushNode(ctx, sourceNode, 2, 2000);
      case 'quantize_crush':
        return this.buildBitcrushNode(ctx, sourceNode, 5, 2600);
      case 'chiptune_arp':
        return this.buildTremoloNode(ctx, sourceNode, 14 * norm, 0.9, 'square');
      case 'floppy_seek': {
        const hp = ctx.createBiquadFilter();
        hp.type = 'highpass';
        hp.frequency.setValueAtTime(1800, ctx.currentTime || 0);
        hp.Q.setValueAtTime(3.5, ctx.currentTime || 0);
        sourceNode.connect(hp);
        return hp;
      }
      case 'modem_dialup': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1600, ctx.currentTime || 0);
        bp.Q.setValueAtTime(3.0, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return bp;
      }
      case 'coin_ring': {
        const peak = ctx.createBiquadFilter();
        peak.type = 'peaking';
        peak.frequency.setValueAtTime(3200, ctx.currentTime || 0);
        peak.gain.setValueAtTime(14 * norm, ctx.currentTime || 0);
        peak.Q.setValueAtTime(6.0, ctx.currentTime || 0);
        sourceNode.connect(peak);
        return peak;
      }
      case 'game_over_pitch': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(550, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }

      // ---------------------------------------------------------------------
      // 2. Voces & Moduladores (15)
      // ---------------------------------------------------------------------
      case 'robot_dalek':
      case 'robot':
        return this.buildRingModulator(ctx, sourceNode, 50 * norm, 0.6);
      case 'robot_cyber': {
        const ring = this.buildRingModulator(ctx, sourceNode, 85, 0.5);
        return this.buildDelayNode(ctx, ring, 0.025, 0.4, 0.5, 4000);
      }
      case 'helium_voice':
      case 'pitch_high': {
        const hs = ctx.createBiquadFilter();
        hs.type = 'highshelf';
        hs.frequency.setValueAtTime(3500, ctx.currentTime || 0);
        hs.gain.setValueAtTime(8 * norm, ctx.currentTime || 0);
        sourceNode.connect(hs);
        return hs;
      }
      case 'deep_monster':
      case 'pitch_low': {
        const ls = ctx.createBiquadFilter();
        ls.type = 'lowshelf';
        ls.frequency.setValueAtTime(150, ctx.currentTime || 0);
        ls.gain.setValueAtTime(10 * norm, ctx.currentTime || 0);
        sourceNode.connect(ls);
        return ls;
      }
      case 'demon_growl': {
        const dist = this.buildDistortionNode(ctx, sourceNode, 55 * norm, 3500, 80);
        const ls = ctx.createBiquadFilter();
        ls.type = 'lowshelf';
        ls.frequency.setValueAtTime(100, ctx.currentTime || 0);
        ls.gain.setValueAtTime(12 * norm, ctx.currentTime || 0);
        dist.connect(ls);
        return ls;
      }
      case 'alien_flanger':
        return this.buildFlangerNode(ctx, sourceNode, 0.008, 0.35, 0.005, 0.75);
      case 'walkie_talkie': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1400, ctx.currentTime || 0);
        bp.Q.setValueAtTime(2.0, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return this.buildDistortionNode(ctx, bp, 35 * norm, 4000, 400);
      }
      case 'telephone_vintage': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1200, ctx.currentTime || 0);
        bp.Q.setValueAtTime(1.4, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return this.buildDistortionNode(ctx, bp, 18, 3400, 300);
      }
      case 'megaphone_police': {
        const peak = ctx.createBiquadFilter();
        peak.type = 'peaking';
        peak.frequency.setValueAtTime(1900, ctx.currentTime || 0);
        peak.gain.setValueAtTime(12, ctx.currentTime || 0);
        sourceNode.connect(peak);
        return this.buildDistortionNode(ctx, peak, 45 * norm, 4500, 500);
      }
      case 'intercom_space': {
        const hp = ctx.createBiquadFilter();
        hp.type = 'highpass';
        hp.frequency.setValueAtTime(500, ctx.currentTime || 0);
        sourceNode.connect(hp);
        const delay = this.buildDelayNode(ctx, hp, 0.065, 0.35, 0.45, 3000);
        return delay;
      }
      case 'radio_military': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1500, ctx.currentTime || 0);
        bp.Q.setValueAtTime(2.5, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return this.buildDistortionNode(ctx, bp, 30, 3200, 600);
      }
      case 'whisper_ghost': {
        const hp = ctx.createBiquadFilter();
        hp.type = 'highpass';
        hp.frequency.setValueAtTime(1800, ctx.currentTime || 0);
        sourceNode.connect(hp);
        return this.buildReverbNode(ctx, hp, 4.0, 0.75, 7000);
      }
      case 'giant_slow': {
        const ls = ctx.createBiquadFilter();
        ls.type = 'lowshelf';
        ls.frequency.setValueAtTime(120, ctx.currentTime || 0);
        ls.gain.setValueAtTime(12, ctx.currentTime || 0);
        sourceNode.connect(ls);
        return ls;
      }
      case 'darth_breather': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(850, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return this.buildTremoloNode(ctx, lp, 1.8, 0.4, 'sine');
      }
      case 'vocoder_synth':
        return this.buildRingModulator(ctx, sourceNode, 120, 0.7);

      // ---------------------------------------------------------------------
      // 3. Filtros & Ecualización (15)
      // ---------------------------------------------------------------------
      case 'underwater_muffle':
      case 'underwater': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(320 + (1 - norm) * 200, ctx.currentTime || 0);
        lp.Q.setValueAtTime(2.8, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }
      case 'bass_boost_sub': {
        const ls = ctx.createBiquadFilter();
        ls.type = 'lowshelf';
        ls.frequency.setValueAtTime(65, ctx.currentTime || 0);
        ls.gain.setValueAtTime(14 * norm, ctx.currentTime || 0);
        sourceNode.connect(ls);
        return ls;
      }
      case 'bass_boost_punch':
      case 'bass_boost': {
        const peak = ctx.createBiquadFilter();
        peak.type = 'peaking';
        peak.frequency.setValueAtTime(115, ctx.currentTime || 0);
        peak.gain.setValueAtTime(11 * norm, ctx.currentTime || 0);
        sourceNode.connect(peak);
        return peak;
      }
      case 'treble_boost_crisp': {
        const hs = ctx.createBiquadFilter();
        hs.type = 'highshelf';
        hs.frequency.setValueAtTime(6500, ctx.currentTime || 0);
        hs.gain.setValueAtTime(12 * norm, ctx.currentTime || 0);
        sourceNode.connect(hs);
        return hs;
      }
      case 'am_radio_lofi': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1350, ctx.currentTime || 0);
        bp.Q.setValueAtTime(1.8, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return bp;
      }
      case 'vinyl_gramophone': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1200, ctx.currentTime || 0);
        bp.Q.setValueAtTime(1.2, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return bp;
      }
      case 'cassette_tape_head': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(6500, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }
      case 'muffled_party': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(220, ctx.currentTime || 0);
        lp.Q.setValueAtTime(3.2, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }
      case 'subwoofer_shake': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(85, ctx.currentTime || 0);
        lp.Q.setValueAtTime(4.0, ctx.currentTime || 0);
        const ls = ctx.createBiquadFilter();
        ls.type = 'lowshelf';
        ls.frequency.setValueAtTime(60, ctx.currentTime || 0);
        ls.gain.setValueAtTime(14 * norm, ctx.currentTime || 0);
        sourceNode.connect(lp);
        lp.connect(ls);
        return ls;
      }
      case 'laser_notch': {
        const notch = ctx.createBiquadFilter();
        notch.type = 'notch';
        notch.frequency.setValueAtTime(1200, ctx.currentTime || 0);
        notch.Q.setValueAtTime(6.0, ctx.currentTime || 0);
        sourceNode.connect(notch);
        return notch;
      }
      case 'vocal_remover': {
        const notch = ctx.createBiquadFilter();
        notch.type = 'notch';
        notch.frequency.setValueAtTime(1000, ctx.currentTime || 0);
        notch.Q.setValueAtTime(1.8, ctx.currentTime || 0);
        sourceNode.connect(notch);
        return notch;
      }
      case 'highpass_tin': {
        const hp = ctx.createBiquadFilter();
        hp.type = 'highpass';
        hp.frequency.setValueAtTime(950, ctx.currentTime || 0);
        hp.Q.setValueAtTime(2.2, ctx.currentTime || 0);
        sourceNode.connect(hp);
        return hp;
      }
      case 'lowpass_club': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(500, ctx.currentTime || 0);
        lp.Q.setValueAtTime(2.5, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return lp;
      }
      case 'bandpass_vowel': {
        const p1 = ctx.createBiquadFilter();
        p1.type = 'peaking';
        p1.frequency.setValueAtTime(750, ctx.currentTime || 0);
        p1.gain.setValueAtTime(12, ctx.currentTime || 0);
        const p2 = ctx.createBiquadFilter();
        p2.type = 'peaking';
        p2.frequency.setValueAtTime(2100, ctx.currentTime || 0);
        p2.gain.setValueAtTime(10, ctx.currentTime || 0);
        sourceNode.connect(p1);
        p1.connect(p2);
        return p2;
      }
      case 'comb_filter_metallic':
        return this.buildDelayNode(ctx, sourceNode, 0.0025, 0.85, 0.6, 6000);

      // ---------------------------------------------------------------------
      // 4. Espacios & Reverb (15)
      // ---------------------------------------------------------------------
      case 'reverb_cathedral':
        return this.buildReverbNode(ctx, sourceNode, 4.2 * norm, 0.65, 4500);
      case 'reverb_dungeon':
      case 'reverb':
        return this.buildReverbNode(ctx, sourceNode, 2.8 * norm, 0.55, 2200);
      case 'reverb_hall':
        return this.buildReverbNode(ctx, sourceNode, 2.2 * norm, 0.45, 5500);
      case 'reverb_small_room':
        return this.buildReverbNode(ctx, sourceNode, 0.7 * norm, 0.35, 6000);
      case 'reverb_metallic_tank':
        return this.buildReverbNode(ctx, sourceNode, 2.6 * norm, 0.60, 7500);
      case 'reverb_outer_space':
        return this.buildReverbNode(ctx, sourceNode, 6.5 * norm, 0.75, 8000);
      case 'reverb_gargantua':
        return this.buildReverbNode(ctx, sourceNode, 5.5 * norm, 0.70, 3000);
      case 'reverb_bathroom':
        return this.buildReverbNode(ctx, sourceNode, 0.85 * norm, 0.50, 6500);
      case 'reverb_subway_tunnel':
        return this.buildReverbNode(ctx, sourceNode, 3.4 * norm, 0.60, 3200);
      case 'reverb_plate_vintage':
        return this.buildReverbNode(ctx, sourceNode, 1.8 * norm, 0.50, 4800);
      case 'reverb_spring_amp':
        return this.buildReverbNode(ctx, sourceNode, 1.4 * norm, 0.45, 4000);
      case 'reverb_endless_void':
        return this.buildReverbNode(ctx, sourceNode, 8.0 * norm, 0.80, 5000);
      case 'reverb_bunker':
        return this.buildReverbNode(ctx, sourceNode, 2.8 * norm, 0.50, 2000);
      case 'reverb_canyon':
        return this.buildReverbNode(ctx, sourceNode, 3.6 * norm, 0.55, 3800);
      case 'reverb_stadium':
        return this.buildReverbNode(ctx, sourceNode, 4.4 * norm, 0.60, 4200);

      // ---------------------------------------------------------------------
      // 5. Ecos & Delays (15)
      // ---------------------------------------------------------------------
      case 'echo_slapback':
        return this.buildDelayNode(ctx, sourceNode, 0.085, 0.15, 0.6, 5000);
      case 'echo_quarter':
        return this.buildDelayNode(ctx, sourceNode, 0.35, 0.45 * norm, 0.55, 4000);
      case 'echo_eighth':
        return this.buildDelayNode(ctx, sourceNode, 0.18, 0.40 * norm, 0.50, 4200);
      case 'echo_ping_pong':
        return this.buildDelayNode(ctx, sourceNode, 0.24, 0.50 * norm, 0.55, 4500);
      case 'echo_space_dub':
        return this.buildDelayNode(ctx, sourceNode, 0.38, 0.72 * norm, 0.65, 1800);
      case 'echo_tape_decay':
        return this.buildDelayNode(ctx, sourceNode, 0.28, 0.58 * norm, 0.55, 2200);
      case 'echo_infinite':
        return this.buildDelayNode(ctx, sourceNode, 0.42, 0.90 * norm, 0.60, 3500);
      case 'echo_reverse_sim':
        return this.buildDelayNode(ctx, sourceNode, 0.30, 0.50, 0.65, 3000);
      case 'echo_dotted':
        return this.buildDelayNode(ctx, sourceNode, 0.26, 0.50 * norm, 0.50, 4000);
      case 'echo_multi_tap':
        return this.buildDelayNode(ctx, sourceNode, 0.16, 0.60 * norm, 0.55, 3600);
      case 'echo_ambient_wash':
        return this.buildDelayNode(ctx, sourceNode, 0.48, 0.70 * norm, 0.65, 2400);
      case 'echo_retro_repeat':
      case 'echo':
        return this.buildDelayNode(ctx, sourceNode, 0.14, 0.48 * norm, 0.55, 3000);
      case 'echo_cascade':
        return this.buildDelayNode(ctx, sourceNode, 0.20, 0.65 * norm, 0.60, 3800);
      case 'echo_flutter':
        return this.buildDelayNode(ctx, sourceNode, 0.19, 0.45 * norm, 0.55, 4000);
      case 'echo_ghost_trail':
        return this.buildDelayNode(ctx, sourceNode, 0.38, 0.35 * norm, 0.35, 5500);

      // ---------------------------------------------------------------------
      // 6. Modulación & Movimiento (15)
      // ---------------------------------------------------------------------
      case 'chorus_thick':
        return this.buildFlangerNode(ctx, sourceNode, 0.025, 0.8, 0.005, 0.35);
      case 'chorus_vintage':
        return this.buildFlangerNode(ctx, sourceNode, 0.016, 1.2, 0.003, 0.30);
      case 'flanger_jet':
        return this.buildFlangerNode(ctx, sourceNode, 0.004, 0.4, 0.0035, 0.75);
      case 'flanger_deep':
        return this.buildFlangerNode(ctx, sourceNode, 0.007, 0.25, 0.006, 0.82);
      case 'phaser_4_stage':
      case 'phaser_space':
        return this.buildFlangerNode(ctx, sourceNode, 0.005, 0.6, 0.004, 0.70);
      case 'tremolo_surf':
        return this.buildTremoloNode(ctx, sourceNode, 5 * norm, 0.75, 'sine');
      case 'tremolo_chopper':
        return this.buildTremoloNode(ctx, sourceNode, 8 * norm, 0.95, 'square');
      case 'vibrato_warble':
        return this.buildFlangerNode(ctx, sourceNode, 0.008, 6.0, 0.003, 0.0);
      case 'ring_mod_bell':
        return this.buildRingModulator(ctx, sourceNode, 380, 0.6);
      case 'rotary_leslie':
        return this.buildTremoloNode(ctx, sourceNode, 6.2 * norm, 0.6, 'sine');
      case 'autopan_smooth':
        return this.buildTremoloNode(ctx, sourceNode, 0.8 * norm, 0.6, 'sine');
      case 'pitch_fifth_up':
      case 'pitch_octave_down':
        return sourceNode; // Handled natively in playbackRate
      case 'harmonizer_retro':
        return this.buildDelayNode(ctx, sourceNode, 0.022, 0.3, 0.5, 4500);

      // ---------------------------------------------------------------------
      // 7. Saturación, Distorsión & Texturas (10)
      // ---------------------------------------------------------------------
      case 'tube_warmth':
        return this.buildDistortionNode(ctx, sourceNode, 15 * norm, 6000, 100);
      case 'overdrive_vintage':
        return this.buildDistortionNode(ctx, sourceNode, 45 * norm, 4800, 200);
      case 'fuzz_heavy':
        return this.buildDistortionNode(ctx, sourceNode, 85 * norm, 4000, 150);
      case 'hard_clipper': {
        const curve = this.makeHardClipCurve(0.35);
        const shaper = ctx.createWaveShaper();
        shaper.curve = curve;
        sourceNode.connect(shaper);
        return shaper;
      }
      case 'tape_warmth':
        return this.buildDistortionNode(ctx, sourceNode, 22 * norm, 6500, 80);
      case 'vinyl_crackle': {
        const bp = ctx.createBiquadFilter();
        bp.type = 'bandpass';
        bp.frequency.setValueAtTime(1300, ctx.currentTime || 0);
        bp.Q.setValueAtTime(1.5, ctx.currentTime || 0);
        sourceNode.connect(bp);
        return bp;
      }
      case 'cassette_wow':
        return this.buildFlangerNode(ctx, sourceNode, 0.012, 0.6, 0.003, 0.1);
      case 'broken_speaker':
        return this.buildDistortionNode(ctx, sourceNode, 95 * norm, 3500, 300);
      case 'crushed_limiter': {
        const curve = this.makeHardClipCurve(0.2);
        const shaper = ctx.createWaveShaper();
        shaper.curve = curve;
        sourceNode.connect(shaper);
        return shaper;
      }
      case 'lofi_chill': {
        const lp = ctx.createBiquadFilter();
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(2200, ctx.currentTime || 0);
        sourceNode.connect(lp);
        return this.buildDistortionNode(ctx, lp, 18, 2500, 100);
      }

      default:
        return sourceNode;
    }
  }

  // Extract Waveform Points for UI visualizer in Timeline
  generateWaveformData(audioBuffer, numPoints = 120) {
    if (!audioBuffer) return [];
    const channelData = audioBuffer.getChannelData(0);
    const blockSize = Math.floor(channelData.length / numPoints);
    const waveform = [];

    for (let i = 0; i < numPoints; i++) {
      const start = i * blockSize;
      let max = 0;
      for (let j = 0; j < blockSize; j++) {
        const val = Math.abs(channelData[start + j]);
        if (val > max) max = val;
      }
      waveform.push(Math.min(1.0, max));
    }
    return waveform;
  }

  // Master Offline Audio Renderer: Mixes all timeline audio tracks deterministically
  async mixTimelineAudio(timelineEngine, exportDuration, sampleRate = 44100) {
    const totalSamples = Math.ceil(sampleRate * Math.max(0.5, exportDuration));
    const offlineCtx = new OfflineAudioContext(2, totalSamples, sampleRate);

    let hasAnyAudio = false;

    for (const track of timelineEngine.tracks) {
      if (track.muted || track.hidden) continue;

      for (const clip of track.clips) {
        if (!clip.audioBuffer || clip.isMuted) continue;

        hasAnyAudio = true;
        const source = offlineCtx.createBufferSource();
        source.buffer = clip.audioBuffer;

        // Effective speed & playbackRate with pitch modulation helper
        const pitchMult = getPitchMultiplier(clip.audioEffect);
        let effectiveSpeed = (clip.speed || 1.0) * pitchMult;
        source.playbackRate.value = effectiveSpeed;

        // Clip Volume & Gain Node
        const gainNode = offlineCtx.createGain();
        const baseVolume = clip.volume !== undefined ? clip.volume : 1.0;
        const clipStart = Math.max(0, clip.start);
        const clipDuration = clip.duration;

        // Fade in / Fade out automation
        const fadeIn = Math.min(clipDuration / 2, clip.fadeIn || 0);
        const fadeOut = Math.min(clipDuration / 2, clip.fadeOut || 0);

        if (fadeIn > 0 || fadeOut > 0) {
          gainNode.gain.setValueAtTime(fadeIn > 0 ? 0 : baseVolume, clipStart);
          if (fadeIn > 0) {
            gainNode.gain.linearRampToValueAtTime(baseVolume, clipStart + fadeIn);
          }
          if (fadeOut > 0) {
            gainNode.gain.setValueAtTime(baseVolume, clipStart + clipDuration - fadeOut);
            gainNode.gain.linearRampToValueAtTime(0, clipStart + clipDuration);
          }
        } else {
          gainNode.gain.setValueAtTime(baseVolume, clipStart);
        }

        // Apply 100 audio effect chain in offline context
        const effectOutput = this.createEffectChain(offlineCtx, source, clip.audioEffect, clip.effectIntensity || 50);
        effectOutput.connect(gainNode);
        gainNode.connect(offlineCtx.destination);

        const trimIn = clip.trimIn || 0;
        const sourceDurationToPlay = clipDuration * effectiveSpeed;
        source.start(clipStart, trimIn, sourceDurationToPlay);
      }
    }

    if (!hasAnyAudio) {
      return null;
    }

    const renderedBuffer = await offlineCtx.startRendering();
    return renderedBuffer;
  }

  // Timeline audio playback management (Real-time preview)
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

    const pitchMult = getPitchMultiplier(clip.audioEffect);
    let effectiveSpeed = (clip.speed || 1.0) * pitchMult;

    const offsetInClip = (currentTime - clipStart) * effectiveSpeed + (clip.trimIn || 0);
    if (offsetInClip < 0 || offsetInClip >= clip.audioBuffer.duration) return;

    this.stopClipAudio(clip.id);

    const source = this.ctx.createBufferSource();
    source.buffer = clip.audioBuffer;
    source.playbackRate.value = effectiveSpeed;

    const gain = this.ctx.createGain();
    const volume = (clip.volume !== undefined ? clip.volume : 1.0);
    gain.gain.value = volume;

    // Apply audio effects chain
    const effectOutput = this.createEffectChain(this.ctx, source, clip.audioEffect, clip.effectIntensity || 50);
    effectOutput.connect(gain);
    gain.connect(this.masterGain);

    const durationRemaining = (clip.duration - (currentTime - clipStart));
    const sourceDurationRemaining = durationRemaining * effectiveSpeed;
    source.start(0, offsetInClip, sourceDurationRemaining);

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

export const AUDIO_EFFECTS = ALL_AUDIO_EFFECTS;
export const audioEngine = new AudioEngine();
