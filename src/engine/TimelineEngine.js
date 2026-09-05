/**
 * KIDCUT TIMELINE ENGINE
 * Multi-track state management, playback loop, splitting, trimming, snapping,
 * full Undo/Redo history stack, and synchronized native video & audio playback.
 */

import { audioEngine } from './AudioEngine.js';
import { compositor } from './Compositor.js';

export class TimelineEngine {
  constructor() {
    this.fps = 30;
    this.currentTime = 0; // in seconds
    this.isPlaying = false;
    this.isLooping = false;
    this.totalDuration = 15; // default seconds, expands dynamically
    this.selectedClipId = null;
    this.snapThresholdSeconds = 0.2;
    this.isSnapping = true;

    // Track hierarchy: V2 (top overlay), V1 (base video), A1 (audio 1), A2 (audio 2)
    this.tracks = [
      { id: 'track-v2', name: 'V2 (OVERLAY)', type: 'video', order: 2, hidden: false, locked: false, clips: [] },
      { id: 'track-v1', name: 'V1 (PRINCIPAL)', type: 'video', order: 1, hidden: false, locked: false, clips: [] },
      { id: 'track-a1', name: 'A1 (MÚSICA)', type: 'audio', order: 1, muted: false, locked: false, clips: [] },
      { id: 'track-a2', name: 'A2 (EFECTOS)', type: 'audio', order: 2, muted: false, locked: false, clips: [] }
    ];

    // Undo / Redo History Stacks
    this.history = [];
    this.redoStack = [];
    this.maxHistory = 50;

    this.listeners = new Set();
    this.animationFrameId = null;
    this.lastPlaybackTimestamp = null;
  }

  subscribe(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  notify(eventType, data = {}) {
    for (const listener of this.listeners) {
      listener(eventType, data);
    }
  }

  // =========================================================================
  // UNDO & REDO STATE MANAGEMENT
  // =========================================================================
  captureSnapshot(action = 'Modificación') {
    return {
      action,
      currentTime: this.currentTime,
      selectedClipId: this.selectedClipId,
      tracks: this.tracks.map(t => ({
        ...t,
        clips: t.clips.map(c => ({
          ...c,
          mediaElement: c.mediaElement,
          audioBuffer: c.audioBuffer
        }))
      }))
    };
  }

  pushState(action = 'Modificación') {
    const snapshot = this.captureSnapshot(action);
    this.history.push(snapshot);
    if (this.history.length > this.maxHistory) {
      this.history.shift();
    }
    // Clear redo stack upon new action
    this.redoStack = [];
    this.notify('historystatechange', {
      canUndo: this.canUndo(),
      canRedo: this.canRedo(),
      action
    });
  }

  undo() {
    if (!this.canUndo()) return false;

    // Save current state to redo stack
    const currentSnapshot = this.captureSnapshot('Current');
    this.redoStack.push(currentSnapshot);

    // Pop previous state
    const previousState = this.history.pop();
    this.restoreSnapshot(previousState);

    audioEngine.playBeep(300, 'sawtooth', 0.08);
    this.notify('historystatechange', {
      canUndo: this.canUndo(),
      canRedo: this.canRedo(),
      action: 'Undo'
    });
    return true;
  }

  redo() {
    if (!this.canRedo()) return false;

    // Save current state to history stack
    const currentSnapshot = this.captureSnapshot('Current');
    this.history.push(currentSnapshot);

    // Pop next state from redo stack
    const nextState = this.redoStack.pop();
    this.restoreSnapshot(nextState);

    audioEngine.playBeep(650, 'triangle', 0.08);
    this.notify('historystatechange', {
      canUndo: this.canUndo(),
      canRedo: this.canRedo(),
      action: 'Redo'
    });
    return true;
  }

  restoreSnapshot(state) {
    if (!state) return;
    this.tracks = state.tracks.map(t => ({
      ...t,
      clips: t.clips.map(c => ({ ...c }))
    }));
    this.currentTime = state.currentTime !== undefined ? state.currentTime : this.currentTime;
    this.selectedClipId = state.selectedClipId;

    this.calculateTotalDuration();
    this.syncAudioPlayback();
    this.syncVideoMediaElements();
    this.notify('trackschange', { tracks: this.tracks });
    this.notify('timeupdate', { currentTime: this.currentTime });
    this.notify('clipselected', { clip: this.getSelectedClip() });
  }

  canUndo() {
    return this.history.length > 0;
  }

  canRedo() {
    return this.redoStack.length > 0;
  }

  // =========================================================================
  // PLAYBACK CONTROLS & VIDEO / AUDIO SYNCHRONIZATION
  // =========================================================================
  play() {
    if (this.isPlaying) return;
    this.isPlaying = true;
    this.lastPlaybackTimestamp = performance.now();
    audioEngine.init();

    // Start playing all currently active audio clips
    this.syncAudioPlayback();
    this.syncVideoMediaElements();

    const loop = (timestamp) => {
      if (!this.isPlaying) return;
      const deltaSec = (timestamp - this.lastPlaybackTimestamp) / 1000;
      this.lastPlaybackTimestamp = timestamp;

      this.currentTime += deltaSec;
      
      const maxDur = this.calculateTotalDuration();
      if (this.currentTime >= maxDur) {
        if (this.isLooping) {
          this.currentTime = 0;
          this.syncAudioPlayback();
          this.syncVideoMediaElements();
        } else {
          this.currentTime = maxDur;
          this.pause();
          return;
        }
      }

      this.syncVideoMediaElements();
      this.notify('timeupdate', { currentTime: this.currentTime });
      this.animationFrameId = requestAnimationFrame(loop);
    };

    this.animationFrameId = requestAnimationFrame(loop);
    this.notify('playbackchange', { isPlaying: true });
  }

  pause() {
    if (!this.isPlaying) return;
    this.isPlaying = false;
    if (this.animationFrameId) {
      cancelAnimationFrame(this.animationFrameId);
      this.animationFrameId = null;
    }
    audioEngine.stopAll();
    this.syncVideoMediaElements();
    this.notify('playbackchange', { isPlaying: false });
  }

  togglePlay() {
    if (this.isPlaying) {
      this.pause();
    } else {
      this.play();
    }
  }

  seek(time, forceAudioSync = true) {
    const maxDur = Math.max(1, this.calculateTotalDuration());
    this.currentTime = Math.max(0, Math.min(maxDur, time));
    
    if (this.isPlaying && forceAudioSync) {
      this.syncAudioPlayback();
    } else if (!this.isPlaying) {
      audioEngine.stopAll();
    }
    this.syncVideoMediaElements();

    this.notify('timeupdate', { currentTime: this.currentTime });
  }

  stepFrames(frames = 1) {
    this.pause();
    const frameDuration = 1 / this.fps;
    this.seek(this.currentTime + (frames * frameDuration));
  }

  jumpToStart() {
    this.seek(0);
  }

  toggleLoop() {
    this.isLooping = !this.isLooping;
    this.notify('loopchange', { isLooping: this.isLooping });
    return this.isLooping;
  }

  syncAudioPlayback() {
    audioEngine.stopAll();
    for (const track of this.tracks) {
      if (!track.muted) {
        for (const clip of track.clips) {
          if (clip.audioBuffer && !clip.isMuted) {
            audioEngine.playClipAudio(clip, this.currentTime);
          }
        }
      }
    }
  }

  /**
   * Synchronizes native HTMLVideoElement audio & playback with the timeline.
   * Guarantees original video audio plays seamlessly without delay.
   */
  syncVideoMediaElements() {
    for (const track of this.tracks) {
      if (track.type === 'video') {
        for (const clip of track.clips) {
          if (clip.mediaElement && clip.mediaElement.tagName === 'VIDEO') {
            const video = clip.mediaElement;
            const clipStart = clip.start;
            const clipEnd = clip.start + clip.duration;
            const isActive = this.currentTime >= clipStart && this.currentTime < clipEnd;

            // If an audio clip exists on A1 for this video, mute the video element to avoid doubling
            const hasDedicatedAudioClip = this.tracks.some(t => 
              t.type === 'audio' && !t.muted && t.clips.some(c => c.sourceVideoClipId === clip.id && !c.isMuted)
            );

            const isMuted = track.muted || clip.isMuted || hasDedicatedAudioClip;
            video.muted = isMuted;
            video.volume = Math.max(0, Math.min(1, (clip.volume !== undefined ? clip.volume : 1.0)));

            if (this.isPlaying && isActive) {
              const targetTime = (clip.trimIn || 0) + ((this.currentTime - clipStart) * (clip.speed || 1.0));
              if (Math.abs(video.currentTime - targetTime) > 0.3) {
                video.currentTime = Math.max(0, Math.min(video.duration || 9999, targetTime));
              }
              video.playbackRate = clip.speed || 1.0;
              if (video.paused) {
                video.play().catch(() => {});
              }
            } else {
              if (!video.paused) {
                video.pause();
              }
              if (isActive) {
                const targetTime = (clip.trimIn || 0) + ((this.currentTime - clipStart) * (clip.speed || 1.0));
                if (Math.abs(video.currentTime - targetTime) > 0.2) {
                  video.currentTime = Math.max(0, Math.min(video.duration || 9999, targetTime));
                }
              }
            }
          }
        }
      }
    }
  }

  // =========================================================================
  // CLIP MANAGEMENT (WITH AUTOMATIC HISTORY TRACKING)
  // =========================================================================
  addClip(trackId, clipData, skipHistory = false) {
    const track = this.tracks.find(t => t.id === trackId);
    if (!track) return null;

    if (!skipHistory) {
      this.pushState(`Añadir ${clipData.name || 'Clip'}`);
    }

    const newClip = {
      id: 'clip_' + Math.random().toString(36).substr(2, 9),
      name: clipData.name || 'Nuevo Clip',
      type: clipData.type,
      start: clipData.start !== undefined ? clipData.start : this.currentTime,
      duration: clipData.duration || 5.0,
      trimIn: clipData.trimIn || 0,
      speed: clipData.speed || 1.0,
      volume: clipData.volume !== undefined ? clipData.volume : 1.0,
      opacity: clipData.opacity !== undefined ? clipData.opacity : 1.0,
      x: clipData.x !== undefined ? clipData.x : 0.5,
      y: clipData.y !== undefined ? clipData.y : 0.5,
      scale: clipData.scale !== undefined ? clipData.scale : 1.0,
      rotation: clipData.rotation || 0,
      filter: clipData.filter || 'none',
      filterIntensity: clipData.filterIntensity || 8,
      mediaElement: clipData.mediaElement || null,
      audioBuffer: clipData.audioBuffer || null,
      sourceVideoClipId: clipData.sourceVideoClipId || null,
      text: clipData.text || '',
      fontSize: clipData.fontSize || 48,
      textColor: clipData.textColor || '#ffd200',
      strokeColor: clipData.strokeColor || '#000000',
      emoji: clipData.emoji || '👾',
      color: clipData.color || '#ff0055'
    };

    track.clips.push(newClip);
    this.selectClip(newClip.id);
    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
    return newClip;
  }

  selectClip(clipId) {
    this.selectedClipId = clipId;
    this.notify('clipselected', { clip: this.getSelectedClip() });
  }

  getSelectedClip() {
    if (!this.selectedClipId) return null;
    for (const track of this.tracks) {
      const found = track.clips.find(c => c.id === this.selectedClipId);
      if (found) return found;
    }
    return null;
  }

  // CapCut-style Split Clip at Playhead
  splitSelectedClip() {
    const clip = this.getSelectedClip();
    if (!clip) return false;

    const splitTime = this.currentTime;
    const clipStart = clip.start;
    const clipEnd = clip.start + clip.duration;

    // Check if playhead is strictly inside the clip
    if (splitTime <= clipStart + 0.05 || splitTime >= clipEnd - 0.05) {
      return false; // Cannot split outside or at boundary
    }

    this.pushState('Cortar Clip');

    const firstDuration = splitTime - clipStart;
    const secondDuration = clip.duration - firstDuration;

    // Update first part
    clip.duration = firstDuration;

    // Create second part as duplicate starting at playhead
    const secondClip = {
      ...clip,
      id: 'clip_' + Math.random().toString(36).substr(2, 9),
      name: clip.name + ' (2)',
      start: splitTime,
      duration: secondDuration,
      trimIn: (clip.trimIn || 0) + (firstDuration * (clip.speed || 1.0))
    };

    // Insert into same track
    for (const track of this.tracks) {
      const idx = track.clips.findIndex(c => c.id === clip.id);
      if (idx !== -1) {
        track.clips.splice(idx + 1, 0, secondClip);
        break;
      }
    }

    this.selectClip(secondClip.id);
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playBeep(600, 'square', 0.08); // retro cut sound
    return true;
  }

  // Delete clip
  deleteSelectedClip() {
    if (!this.selectedClipId) return false;
    for (const track of this.tracks) {
      const idx = track.clips.findIndex(c => c.id === this.selectedClipId);
      if (idx !== -1) {
        this.pushState('Borrar Clip');
        track.clips.splice(idx, 1);
        this.selectedClipId = null;
        this.notify('clipselected', { clip: null });
        this.notify('trackschange', { tracks: this.tracks });
        audioEngine.playBeep(220, 'sawtooth', 0.1);
        return true;
      }
    }
    return false;
  }

  // Duplicate clip
  duplicateSelectedClip() {
    const clip = this.getSelectedClip();
    if (!clip) return false;

    this.pushState('Duplicar Clip');

    const dup = {
      ...clip,
      id: 'clip_' + Math.random().toString(36).substr(2, 9),
      name: clip.name + ' (COPIA)',
      start: clip.start + clip.duration + 0.1
    };

    for (const track of this.tracks) {
      if (track.clips.some(c => c.id === clip.id)) {
        track.clips.push(dup);
        break;
      }
    }

    this.selectClip(dup.id);
    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playBeep(520, 'square', 0.08);
    return true;
  }

  // Update clip properties from Inspector or Canvas interaction
  updateClip(clipId, properties, recordHistory = false) {
    if (recordHistory) {
      this.pushState('Editar Propiedades');
    }
    const clip = this.getSelectedClip();
    if (!clip || clip.id !== clipId) return;

    Object.assign(clip, properties);
    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
    this.notify('clipupdated', { clip });
  }

  // Move clip position in timeline
  moveClip(clipId, newStart, targetTrackId = null) {
    let currentTrack = null;
    let clip = null;

    for (const t of this.tracks) {
      const found = t.clips.find(c => c.id === clipId);
      if (found) {
        currentTrack = t;
        clip = found;
        break;
      }
    }

    if (!clip || !currentTrack) return;

    let finalStart = Math.max(0, newStart);

    // Magnetic Snapping
    if (this.isSnapping) {
      finalStart = this.applySnapping(clipId, finalStart, clip.duration);
    }

    clip.start = finalStart;

    // Track migration if moving between compatible tracks
    if (targetTrackId && targetTrackId !== currentTrack.id) {
      const targetTrack = this.tracks.find(t => t.id === targetTrackId);
      if (targetTrack && targetTrack.type === currentTrack.type) {
        currentTrack.clips = currentTrack.clips.filter(c => c.id !== clipId);
        targetTrack.clips.push(clip);
      }
    }

    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
  }

  // Trim clip edges (In / Out)
  trimClip(clipId, edge, deltaSec) {
    const clip = this.getSelectedClip();
    if (!clip || clip.id !== clipId) return;

    if (edge === 'left') {
      const newStart = Math.max(0, clip.start + deltaSec);
      const actualDelta = newStart - clip.start;
      if (clip.duration - actualDelta >= 0.2) {
        clip.start = newStart;
        clip.duration -= actualDelta;
        clip.trimIn = Math.max(0, (clip.trimIn || 0) + actualDelta);
      }
    } else if (edge === 'right') {
      const newDuration = Math.max(0.2, clip.duration + deltaSec);
      clip.duration = newDuration;
    }

    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
  }

  applySnapping(clipId, start, duration) {
    const snapCandidates = [0, this.currentTime];
    const snapEnd = start + duration;

    for (const track of this.tracks) {
      for (const c of track.clips) {
        if (c.id !== clipId) {
          snapCandidates.push(c.start);
          snapCandidates.push(c.start + c.duration);
        }
      }
    }

    // Check snap for start edge
    for (const snapPoint of snapCandidates) {
      if (Math.abs(start - snapPoint) <= this.snapThresholdSeconds) {
        return snapPoint;
      }
    }

    // Check snap for end edge
    for (const snapPoint of snapCandidates) {
      if (Math.abs(snapEnd - snapPoint) <= this.snapThresholdSeconds) {
        return snapPoint - duration;
      }
    }

    return start;
  }

  calculateTotalDuration() {
    let maxTime = 10;
    for (const track of this.tracks) {
      for (const clip of track.clips) {
        const end = clip.start + clip.duration;
        if (end > maxTime) {
          maxTime = end;
        }
      }
    }
    this.totalDuration = maxTime + 2.0; // padding
    return this.totalDuration;
  }

  addTrack(type) {
    this.pushState(`Añadir Pista ${type.toUpperCase()}`);
    const count = this.tracks.filter(t => t.type === type).length + 1;
    const prefix = type === 'video' ? 'V' : 'A';
    const newTrack = {
      id: `track-${type}-${Date.now()}`,
      name: `${prefix}${count} (${type.toUpperCase()})`,
      type: type,
      order: count,
      hidden: false,
      muted: false,
      locked: false,
      clips: []
    };
    this.tracks.push(newTrack);
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playCoin();
    return newTrack;
  }
}

export const timelineEngine = new TimelineEngine();
