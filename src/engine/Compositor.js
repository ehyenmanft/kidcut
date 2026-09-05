/**
 * KIDCUT CANVAS COMPOSITOR & 8-BIT RETRO FILTER ENGINE
 * High-performance offscreen 2D compositing with pixel shaders, 100 effects,
 * retro transitions, and animated 8-bit text.
 */

import { AspectRatios } from './types.js';
import { effectsEngine } from './EffectsEngine.js';
import { transitionsEngine } from './TransitionsEngine.js';

export class Compositor {
  constructor() {
    this.offscreenCanvas = document.createElement('canvas');
    this.offscreenCtx = this.offscreenCanvas.getContext('2d', { willReadFrequently: true });
    
    this.clipCanvas = document.createElement('canvas');
    this.clipCtx = this.clipCanvas.getContext('2d', { willReadFrequently: true });

    this.aspectRatioKey = '16:9';
    this.baseWidth = 1920;
    this.baseHeight = 1080;
    this.isExporting = false;
    this.updateDimensions();
  }

  setAspectRatio(ratioKey) {
    if (AspectRatios[ratioKey]) {
      this.aspectRatioKey = ratioKey;
      this.baseWidth = AspectRatios[ratioKey].width;
      this.baseHeight = AspectRatios[ratioKey].height;
      this.updateDimensions();
    }
  }

  updateDimensions(targetScale = 1.0) {
    const w = Math.round(this.baseWidth * targetScale);
    const h = Math.round(this.baseHeight * targetScale);
    this.offscreenCanvas.width = w;
    this.offscreenCanvas.height = h;
    this.clipCanvas.width = w;
    this.clipCanvas.height = h;
  }

  renderFrame(tracks, currentTime, targetCtx = null, scale = 1.0) {
    const w = Math.round(this.baseWidth * scale);
    const h = Math.round(this.baseHeight * scale);

    if (this.offscreenCanvas.width !== w || this.offscreenCanvas.height !== h) {
      this.updateDimensions(scale);
    }

    const ctx = this.offscreenCtx;
    ctx.imageSmoothingEnabled = false; // Authentic pixel rendering!
    
    // Clear background (deep retro dark)
    ctx.fillStyle = '#080611';
    ctx.fillRect(0, 0, w, h);

    // Filter video tracks (rendered bottom to top by order)
    const videoTracks = tracks
      .filter(t => t.type === 'video' && !t.hidden)
      .sort((a, b) => a.order - b.order);

    for (const track of videoTracks) {
      // Find clips active at currentTime
      for (const clip of track.clips) {
        if (currentTime >= clip.start && currentTime < (clip.start + clip.duration)) {
          this.renderClip(ctx, clip, currentTime, w, h);
        }
      }
    }

    // Check for any active global or track transition
    for (const track of videoTracks) {
      for (const clip of track.clips) {
        if (clip.transition && clip.transition !== 'none') {
          const transDur = clip.transitionDuration || 0.8;
          // Transition in
          if (currentTime >= clip.start && currentTime < (clip.start + transDur)) {
            const progress = (currentTime - clip.start) / transDur;
            transitionsEngine.renderTransition(ctx, clip.transition, progress, w, h);
          }
        }
      }
    }

    // If destination canvas is provided, draw final frame to it
    if (targetCtx) {
      targetCtx.imageSmoothingEnabled = false;
      targetCtx.clearRect(0, 0, targetCtx.canvas.width, targetCtx.canvas.height);
      targetCtx.drawImage(this.offscreenCanvas, 0, 0, targetCtx.canvas.width, targetCtx.canvas.height);
    }

    return this.offscreenCanvas;
  }

  renderClip(ctx, clip, currentTime, targetW, targetH) {
    ctx.save();

    // Text & Sticker position coordinates
    const posX = (clip.x !== undefined ? clip.x : 0.5) * targetW;
    const posY = (clip.y !== undefined ? clip.y : 0.5) * targetH;
    let scale = clip.scale !== undefined ? clip.scale : 1.0;
    let rotation = ((clip.rotation || 0) * Math.PI) / 180;
    let opacity = clip.opacity !== undefined ? clip.opacity : 1.0;

    // Handle Text Animation modifications (Pop Scale, Wave, Glitch)
    let textAnimOffsetX = 0;
    let textAnimOffsetY = 0;
    const clipElapsed = currentTime - clip.start;

    if (clip.type === 'text' && clip.textAnimation) {
      const anim = clip.textAnimation;
      if (anim === 'arcade_blink') {
        if ((Math.floor(clipElapsed * 4) % 2) === 1) {
          ctx.restore();
          return; // Skip drawing frame
        }
      } else if (anim === 'pop_scale') {
        const popTime = 0.35;
        if (clipElapsed < popTime) {
          const t = clipElapsed / popTime;
          scale *= Math.sin(t * Math.PI * 0.7) * 1.25;
        }
      } else if (anim === 'wave_float') {
        textAnimOffsetY = Math.sin(clipElapsed * 5) * (targetH * 0.025);
      } else if (anim === 'glitch_shake') {
        textAnimOffsetX = (Math.random() - 0.5) * (targetW * 0.015);
        textAnimOffsetY = (Math.random() - 0.5) * (targetH * 0.015);
      }
    }

    ctx.translate(posX + textAnimOffsetX, posY + textAnimOffsetY);
    ctx.rotate(rotation);
    ctx.scale(scale, scale);
    ctx.globalAlpha = opacity;

    if (clip.blendMode) {
      ctx.globalCompositeOperation = clip.blendMode;
    }

    // Render based on clip type
    if (clip.type === 'video') {
      this.renderVideoClip(ctx, clip, currentTime, targetW, targetH);
    } else if (clip.type === 'image') {
      this.renderImageClip(ctx, clip, targetW, targetH);
    } else if (clip.type === 'text') {
      this.renderTextClip(ctx, clip, currentTime, targetW, targetH);
    } else if (clip.type === 'sticker') {
      this.renderStickerClip(ctx, clip, targetW, targetH);
    } else if (clip.type === 'color') {
      this.renderColorClip(ctx, clip, targetW, targetH);
    }

    ctx.restore();

    // Apply Filter / Effect from 100 Effects Database
    if (clip.filter && clip.filter !== 'none') {
      effectsEngine.applyEffect(
        ctx,
        clip.filter,
        targetW,
        targetH,
        clip.filterIntensity || 50,
        currentTime
      );
    }
  }

  renderVideoClip(ctx, clip, currentTime, targetW, targetH) {
    const video = clip.mediaElement;
    if (!video) return;

    const clipStart = clip.start;
    const trimIn = clip.trimIn || 0;
    const speed = clip.speed || 1.0;
    const targetVideoTime = trimIn + ((currentTime - clipStart) * speed);

    if (this.isExporting) {
      // Offline export: exact time alignment without 0.25s throttling
      const clampedTime = Math.max(0, Math.min(video.duration || 9999, targetVideoTime));
      if (Math.abs(video.currentTime - clampedTime) > 0.001) {
        video.currentTime = clampedTime;
      }
    } else {
      // Smooth video synchronization during realtime playback: only seek if drift exceeds 0.25s
      const drift = Math.abs(video.currentTime - targetVideoTime);
      if (drift > 0.25) {
        video.currentTime = Math.max(0, Math.min(video.duration || 9999, targetVideoTime));
      }
    }
    if (video.playbackRate !== speed) {
      video.playbackRate = speed;
    }

    const vw = video.videoWidth || targetW;
    const vh = video.videoHeight || targetH;
    
    // Fit maintaining aspect ratio inside target
    const aspect = vw / vh;
    let drawW = targetW;
    let drawH = targetW / aspect;
    if (drawH > targetH) {
      drawH = targetH;
      drawW = targetH * aspect;
    }

    ctx.drawImage(video, -drawW / 2, -drawH / 2, drawW, drawH);
  }

  renderImageClip(ctx, clip, targetW, targetH) {
    const img = clip.mediaElement;
    if (!img) return;

    const iw = img.naturalWidth || img.width || 400;
    const ih = img.naturalHeight || img.height || 300;
    const aspect = iw / ih;

    let drawW = targetW;
    let drawH = targetW / aspect;
    if (drawH > targetH) {
      drawH = targetH;
      drawW = targetH * aspect;
    }

    ctx.drawImage(img, -drawW / 2, -drawH / 2, drawW, drawH);
  }

  renderTextClip(ctx, clip, currentTime, targetW, targetH) {
    let text = clip.text || 'KIDCUT';
    const clipElapsed = currentTime - clip.start;

    // Typewriter effect
    if (clip.textAnimation === 'typewriter') {
      const charsPerSec = 12;
      const visibleChars = Math.floor(clipElapsed * charsPerSec);
      text = text.substring(0, visibleChars);
    }

    const fontSize = clip.fontSize || Math.round(targetH * 0.08);
    const fontFam = clip.fontFamily || "'Press Start 2P', monospace";
    let color = clip.textColor || '#ffd200';
    const strokeColor = clip.strokeColor || '#000000';

    // Rainbow Cycle effect
    if (clip.textAnimation === 'rainbow_cycle') {
      const colors = ['#ffd200', '#00f0ff', '#ff0055', '#39ff14', '#b026ff', '#ffffff'];
      const cIdx = Math.floor(clipElapsed * 5) % colors.length;
      color = colors[cIdx];
    }

    ctx.font = `${fontSize}px ${fontFam}`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';

    // 8-Bit Pixel Shadow / Contour
    ctx.lineWidth = Math.max(4, Math.round(fontSize * 0.15));
    ctx.strokeStyle = strokeColor;
    ctx.strokeText(text, 0, 0);

    // Pixel Drop Shadow
    ctx.fillStyle = '#000000';
    ctx.fillText(text, 4, 4);

    // Front fill
    ctx.fillStyle = color;
    ctx.fillText(text, 0, 0);
  }

  renderStickerClip(ctx, clip, targetW, targetH) {
    if (clip.mediaElement) {
      const size = Math.round(targetH * 0.25);
      ctx.drawImage(clip.mediaElement, -size / 2, -size / 2, size, size);
    } else {
      // Draw procedural retro pixel emoji / icon
      const emoji = clip.emoji || '👾';
      const size = Math.round(targetH * 0.18);
      ctx.font = `${size}px monospace`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(emoji, 0, 0);
    }
  }

  renderColorClip(ctx, clip, targetW, targetH) {
    ctx.fillStyle = clip.color || '#ff0055';
    ctx.fillRect(-targetW / 2, -targetH / 2, targetW, targetH);
  }
}

export const compositor = new Compositor();
