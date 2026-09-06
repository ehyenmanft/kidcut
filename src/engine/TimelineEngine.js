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
    this.selectedClipIds = new Set();
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
      selectedClipIds: Array.from(this.selectedClipIds),
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
    this.selectedClipIds = new Set(state.selectedClipIds || (state.selectedClipId ? [state.selectedClipId] : []));

    this.calculateTotalDuration();
    this.syncAudioPlayback();
    this.syncVideoMediaElements();
    this.notify('trackschange', { tracks: this.tracks });
    this.notify('timeupdate', { currentTime: this.currentTime });
    this.notify('clipselected', { 
      clip: this.getSelectedClip(),
      selectedClipIds: Array.from(this.selectedClipIds)
    });
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

      // Synchronize timeline with active playing video element if present
      let masterVideoTime = null;
      for (const track of this.tracks) {
        if (track.type === 'video' && !track.hidden) {
          for (const clip of track.clips) {
            if (clip.mediaElement && clip.mediaElement.tagName === 'VIDEO') {
              const v = clip.mediaElement;
              const clipStart = clip.start;
              const clipEnd = clip.start + clip.duration;
              if (this.currentTime >= clipStart && this.currentTime < clipEnd && !v.paused && !v.seeking) {
                const speed = clip.speed || 1.0;
                const vTimelineTime = clipStart + ((v.currentTime - (clip.trimIn || 0)) / speed);
                if (Math.abs(this.currentTime - vTimelineTime) < 0.6) {
                  masterVideoTime = vTimelineTime;
                  break;
                }
              }
            }
          }
        }
        if (masterVideoTime !== null) break;
      }

      if (masterVideoTime !== null) {
        this.currentTime = masterVideoTime;
      } else {
        this.currentTime += deltaSec;
      }
      
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

      // Smooth audio transition across clips during playback
      for (const track of this.tracks) {
        if (!track.muted) {
          for (const clip of track.clips) {
            if (clip.audioBuffer && !clip.isMuted) {
              const isActive = this.currentTime >= clip.start && this.currentTime < (clip.start + clip.duration);
              const isPlaying = audioEngine.activeSources.has(clip.id);
              if (isActive && !isPlaying) {
                audioEngine.playClipAudio(clip, this.currentTime);
              } else if (!isActive && isPlaying) {
                audioEngine.stopClipAudio(clip.id);
              }
            }
          }
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
   * Groups by distinct video element to eliminate play/pause thrashing and buffer stalls when clips are cut.
   */
  syncVideoMediaElements() {
    const videoStates = new Map();

    for (const track of this.tracks) {
      if (track.type === 'video') {
        for (const clip of track.clips) {
          if (clip.mediaElement && clip.mediaElement.tagName === 'VIDEO') {
            const video = clip.mediaElement;
            if (!videoStates.has(video)) {
              videoStates.set(video, { activeClip: null, trackMuted: track.hidden || track.muted });
            }
            const clipStart = clip.start;
            const clipEnd = clip.start + clip.duration;
            if (this.currentTime >= clipStart && this.currentTime < clipEnd) {
              videoStates.set(video, { 
                activeClip: clip, 
                trackMuted: track.hidden || track.muted 
              });
            }
          }
        }
      }
    }

    for (const [video, state] of videoStates.entries()) {
      const clip = state.activeClip;
      if (clip) {
        const clipStart = clip.start;
        const targetTime = (clip.trimIn || 0) + ((this.currentTime - clipStart) * (clip.speed || 1.0));
        const speed = clip.speed || 1.0;

        const hasDedicatedAudioClip = this.tracks.some(t => 
          t.type === 'audio' && !t.muted && t.clips.some(c => (c.sourceVideoClipId === clip.id || c.sourceVideoClipId === clip.sourceVideoClipId) && !c.isMuted)
        );
        const isMuted = state.trackMuted || clip.isMuted || hasDedicatedAudioClip;
        video.muted = isMuted;
        video.volume = Math.max(0, Math.min(1, (clip.volume !== undefined ? clip.volume : 1.0)));

        if (video.playbackRate !== speed) {
          video.playbackRate = speed;
        }

        if (this.isPlaying) {
          // If video is paused, initialize position and start playback smoothly
          if (video.paused) {
            video.currentTime = Math.max(0, Math.min(video.duration || 9999, targetTime));
            video.play().catch(() => {});
          } else {
            // Video is actively playing: never seek unless drift is catastrophic (> 1.2s)
            const drift = Math.abs(video.currentTime - targetTime);
            if (drift > 1.2) {
              video.currentTime = Math.max(0, Math.min(video.duration || 9999, targetTime));
            }
          }
        } else {
          if (!video.paused) {
            video.pause();
          }
          const drift = Math.abs(video.currentTime - targetTime);
          if (drift > 0.02) {
            video.currentTime = Math.max(0, Math.min(video.duration || 9999, targetTime));
          }
        }
      } else {
        if (!video.paused) {
          video.pause();
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
      isMuted: clipData.isMuted || false,
      audioEffect: clipData.audioEffect || 'none',
      effectIntensity: clipData.effectIntensity !== undefined ? clipData.effectIntensity : 50,
      fadeIn: clipData.fadeIn || 0,
      fadeOut: clipData.fadeOut || 0,
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

  // Audio Extraction: Decouple audio from video clip into a dedicated audio track
  extractAudioFromClip(clipId) {
    let sourceClip = null;
    let sourceTrack = null;
    for (const track of this.tracks) {
      const c = track.clips.find(clip => clip.id === clipId);
      if (c) {
        sourceClip = c;
        sourceTrack = track;
        break;
      }
    }

    if (!sourceClip || sourceClip.type !== 'video' || !sourceClip.audioBuffer) {
      return null;
    }

    this.pushState(`Extraer Audio de ${sourceClip.name}`);

    // Mute the original video clip so it doesn't double-play
    sourceClip.isMuted = true;

    // Find first available audio track or create a new one
    let targetAudioTrack = this.tracks.find(t => t.type === 'audio');
    if (!targetAudioTrack) {
      targetAudioTrack = this.addTrack('audio');
    }

    const audioClip = {
      id: 'clip_' + Math.random().toString(36).substr(2, 9),
      name: `${sourceClip.name} (AUDIO EXTRAÍDO)`,
      type: 'audio',
      start: sourceClip.start,
      duration: sourceClip.duration,
      trimIn: sourceClip.trimIn || 0,
      speed: sourceClip.speed || 1.0,
      volume: sourceClip.volume !== undefined ? sourceClip.volume : 1.0,
      isMuted: false,
      audioBuffer: sourceClip.audioBuffer,
      audioEffect: sourceClip.audioEffect || 'none',
      effectIntensity: sourceClip.effectIntensity !== undefined ? sourceClip.effectIntensity : 50,
      fadeIn: 0,
      fadeOut: 0,
      sourceVideoClipId: sourceClip.id
    };

    targetAudioTrack.clips.push(audioClip);
    this.selectClip(audioClip.id);
    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playCoin();
    return audioClip;
  }

  /**
   * Moves a clip from its current track to a target track.
   */
  moveClipToTrack(clipId, targetTrackId) {
    let sourceTrack = null;
    let clip = null;
    for (const t of this.tracks) {
      const idx = t.clips.findIndex(c => c.id === clipId);
      if (idx !== -1) {
        sourceTrack = t;
        clip = t.clips[idx];
        break;
      }
    }
    if (!sourceTrack || !clip || sourceTrack.id === targetTrackId) return false;
    const targetTrack = this.tracks.find(t => t.id === targetTrackId);
    if (!targetTrack || targetTrack.type !== sourceTrack.type) return false;

    this.pushState(`Mover ${clip.name} a ${targetTrack.name}`);
    sourceTrack.clips = sourceTrack.clips.filter(c => c.id !== clipId);
    targetTrack.clips.push(clip);
    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playBeep(520, 'triangle', 0.05);
    return true;
  }

  /**
   * Duplicates a video clip onto the V2 Overlay track so they play in parallel.
   */
  duplicateClipToOverlay(clipId) {
    let sourceClip = null;
    for (const t of this.tracks) {
      const c = t.clips.find(item => item.id === clipId);
      if (c) {
        sourceClip = c;
        break;
      }
    }
    if (!sourceClip) return null;

    const v2Track = this.tracks.find(t => t.id === 'track-v2') || this.tracks.find(t => t.type === 'video');
    if (!v2Track) return null;

    this.pushState(`Superponer ${sourceClip.name} en V2`);
    const newClip = {
      ...sourceClip,
      id: 'clip_' + Math.random().toString(36).substr(2, 9),
      name: `${sourceClip.name} (OVERLAY)`,
      start: sourceClip.start,
      scale: 0.6,
      x: 0.75,
      y: 0.25
    };
    v2Track.clips.push(newClip);
    this.selectClip(newClip.id);
    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playCoin();
    return newClip;
  }

  extractAudioFromSelectedClip() {
    const selected = this.getSelectedClip();
    if (!selected || selected.type !== 'video' || !selected.audioBuffer) {
      return null;
    }
    return this.extractAudioFromClip(selected.id);
  }

  selectClip(clipId, multi = false) {
    if (!multi) {
      this.selectedClipIds.clear();
      if (clipId) {
        this.selectedClipIds.add(clipId);
      }
      this.selectedClipId = clipId;
    } else {
      if (clipId) {
        if (this.selectedClipIds.has(clipId)) {
          this.selectedClipIds.delete(clipId);
          this.selectedClipId = this.selectedClipIds.size > 0 ? Array.from(this.selectedClipIds)[this.selectedClipIds.size - 1] : null;
        } else {
          this.selectedClipIds.add(clipId);
          this.selectedClipId = clipId;
        }
      }
    }
    this.notify('clipselected', { 
      clip: this.getSelectedClip(),
      selectedClipIds: Array.from(this.selectedClipIds)
    });
  }

  selectClips(clipIds) {
    this.selectedClipIds = new Set(clipIds);
    this.selectedClipId = clipIds.length > 0 ? clipIds[clipIds.length - 1] : null;
    this.notify('clipselected', { 
      clip: this.getSelectedClip(),
      selectedClipIds: Array.from(this.selectedClipIds)
    });
  }

  clearSelection() {
    this.selectedClipIds.clear();
    this.selectedClipId = null;
    this.notify('clipselected', { 
      clip: null,
      selectedClipIds: [] 
    });
  }

  isClipSelected(clipId) {
    return this.selectedClipIds.has(clipId);
  }

  getSelectedClips() {
    const clips = [];
    for (const track of this.tracks) {
      for (const clip of track.clips) {
        if (this.selectedClipIds.has(clip.id)) {
          clips.push(clip);
        }
      }
    }
    return clips;
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

    // Split any linked audio clip on track A1
    for (const track of this.tracks) {
      if (track.type === 'audio') {
        const linkedAudio = track.clips.find(c => 
          c.sourceVideoClipId === clip.id && 
          splitTime > c.start + 0.05 && 
          splitTime < c.start + c.duration - 0.05
        );
        if (linkedAudio) {
          const audioFirstDur = splitTime - linkedAudio.start;
          const audioSecondDur = linkedAudio.duration - audioFirstDur;
          linkedAudio.duration = audioFirstDur;
          const secondAudio = {
            ...linkedAudio,
            id: 'clip_' + Math.random().toString(36).substr(2, 9),
            sourceVideoClipId: secondClip.id,
            name: linkedAudio.name + ' (2)',
            start: splitTime,
            duration: audioSecondDur,
            trimIn: (linkedAudio.trimIn || 0) + (audioFirstDur * (linkedAudio.speed || 1.0))
          };
          const aIdx = track.clips.findIndex(c => c.id === linkedAudio.id);
          if (aIdx !== -1) {
            track.clips.splice(aIdx + 1, 0, secondAudio);
          }
        }
      }
    }

    this.selectClip(secondClip.id);
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playBeep(600, 'square', 0.08); // retro cut sound
    return true;
  }

  // Delete clip(s) - Supports Multi-Selection / Grouped Deletion
  deleteSelectedClip() {
    const idsToDelete = (this.selectedClipIds && this.selectedClipIds.size > 0)
      ? Array.from(this.selectedClipIds)
      : (this.selectedClipId ? [this.selectedClipId] : []);

    if (idsToDelete.length === 0) return false;

    this.pushState(`Borrar ${idsToDelete.length > 1 ? idsToDelete.length + ' clips' : 'Clip'}`);

    for (const track of this.tracks) {
      track.clips = track.clips.filter(c => !idsToDelete.includes(c.id));
    }

    this.clearSelection();
    this.calculateTotalDuration();
    this.notify('clipselected', { clip: null, selectedClipIds: [] });
    this.notify('trackschange', { tracks: this.tracks });
    audioEngine.playBeep(220, 'sawtooth', 0.1);
    return true;
  }

  // Duplicate clip(s) - Supports Multi-Selection / Grouped Duplication
  duplicateSelectedClip() {
    const clipsToDup = this.getSelectedClips();
    if (clipsToDup.length === 0) {
      const single = this.getSelectedClip();
      if (single) clipsToDup.push(single);
    }
    if (clipsToDup.length === 0) return false;

    this.pushState(`Duplicar ${clipsToDup.length > 1 ? clipsToDup.length + ' clips' : 'Clip'}`);

    const newSelectedIds = [];
    for (const clip of clipsToDup) {
      const dup = {
        ...clip,
        id: 'clip_' + Math.random().toString(36).substr(2, 9),
        name: clip.name + ' (COPIA)',
        start: clip.start + clip.duration + 0.1
      };

      for (const track of this.tracks) {
        if (track.clips.some(c => c.id === clip.id)) {
          track.clips.push(dup);
          newSelectedIds.push(dup.id);
          break;
        }
      }
    }

    this.selectClips(newSelectedIds);
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
      if (clip.duration - actualDelta >= 0.15) {
        clip.start = newStart;
        clip.duration -= actualDelta;
        clip.trimIn = Math.max(0, (clip.trimIn || 0) + actualDelta);
      }
    } else if (edge === 'right') {
      const newDuration = Math.max(0.15, clip.duration + deltaSec);
      clip.duration = newDuration;
    }

    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
  }

  // Set clip trim / duration with full bounds and bounds verification
  setClipTrim(clipId, edge, newTimeValue) {
    const clip = this.getSelectedClip();
    if (!clip || clip.id !== clipId) return;

    if (edge === 'right') {
      const minDuration = 0.15;
      let maxDuration = Infinity;
      if (clip.mediaElement && clip.mediaElement.duration && !isNaN(clip.mediaElement.duration)) {
        maxDuration = (clip.mediaElement.duration / (clip.speed || 1.0)) - (clip.trimIn || 0);
      } else if (clip.audioBuffer && clip.audioBuffer.duration) {
        maxDuration = (clip.audioBuffer.duration / (clip.speed || 1.0)) - (clip.trimIn || 0);
      }
      
      let duration = Math.max(minDuration, newTimeValue);
      if (isFinite(maxDuration) && maxDuration > 0) {
        duration = Math.min(duration, maxDuration);
      }
      clip.duration = Number(duration.toFixed(3));
    } else if (edge === 'left') {
      const currentEnd = clip.start + clip.duration;
      let newStart = Math.max(0, newTimeValue);
      if (currentEnd - newStart < 0.15) {
        newStart = currentEnd - 0.15;
      }
      const shift = newStart - clip.start;
      const newTrimIn = (clip.trimIn || 0) + shift;
      if (newTrimIn < 0) {
        newStart = clip.start - (clip.trimIn || 0);
        clip.trimIn = 0;
      } else {
        clip.trimIn = Number(newTrimIn.toFixed(3));
      }
      clip.duration = Number((currentEnd - newStart).toFixed(3));
      clip.start = Number(newStart.toFixed(3));
    }

    this.calculateTotalDuration();
    this.notify('trackschange', { tracks: this.tracks });
  }

  applySnappingToPoint(clipId, timePoint) {
    const snapCandidates = [0, this.currentTime];

    for (const track of this.tracks) {
      for (const c of track.clips) {
        if (c.id !== clipId) {
          snapCandidates.push(c.start);
          snapCandidates.push(c.start + c.duration);
        }
      }
    }

    for (const snapPoint of snapCandidates) {
      if (Math.abs(timePoint - snapPoint) <= this.snapThresholdSeconds) {
        return snapPoint;
      }
    }

    return timePoint;
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
