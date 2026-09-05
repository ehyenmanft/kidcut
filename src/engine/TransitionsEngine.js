/**
 * KIDCUT 8-BIT RETRO TRANSITIONS ENGINE
 * Deterministic frame-stepping transitions between clips:
 * Pixel Dissolve, Wipe H/V, Iris Circle, Blinds, Glitch Wipe, TV Turn-Off.
 */

export class TransitionsEngine {
  renderTransition(ctx, transitionType, progress, width, height) {
    if (!transitionType || transitionType === 'none' || progress <= 0) return;
    if (progress >= 1) return;

    ctx.save();

    switch (transitionType) {
      case 'pixel_dissolve': {
        // Pixel block dissolve pattern
        const blockSize = 24;
        ctx.fillStyle = '#000000';
        for (let y = 0; y < height; y += blockSize) {
          for (let x = 0; x < width; x += blockSize) {
            // Deterministic pseudo-random based on coordinate hash
            const hash = Math.sin(x * 12.9898 + y * 78.233) * 43758.5453;
            const threshold = hash - Math.floor(hash);
            if (threshold < (1 - progress)) {
              ctx.fillRect(x, y, blockSize, blockSize);
            }
          }
        }
        break;
      }
      case 'wipe_horizontal': {
        const wipeX = width * (1 - progress);
        ctx.fillStyle = '#000000';
        ctx.fillRect(wipeX, 0, width - wipeX, height);
        // Retro jagged edge
        const step = 16;
        for (let y = 0; y < height; y += step) {
          if ((y / step) % 2 === 0) {
            ctx.fillRect(wipeX - 8, y, 8, step);
          }
        }
        break;
      }
      case 'wipe_vertical': {
        const wipeY = height * (1 - progress);
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, wipeY, width, height - wipeY);
        break;
      }
      case 'iris_circle': {
        const maxRadius = Math.hypot(width / 2, height / 2);
        const curRadius = maxRadius * (1 - progress);
        ctx.fillStyle = '#000000';
        ctx.beginPath();
        ctx.rect(0, 0, width, height);
        ctx.arc(width / 2, height / 2, Math.max(0, curRadius), 0, Math.PI * 2, true);
        ctx.fill();
        break;
      }
      case 'blinds': {
        const blindCount = 10;
        const blindH = height / blindCount;
        ctx.fillStyle = '#000000';
        const coverH = blindH * (1 - progress);
        for (let i = 0; i < blindCount; i++) {
          ctx.fillRect(0, i * blindH, width, coverH);
        }
        break;
      }
      case 'glitch_wipe': {
        const wipeY = height * (1 - progress);
        ctx.fillStyle = '#00f0ff';
        ctx.fillRect(0, wipeY, width, 6);
        ctx.fillStyle = '#ff0055';
        ctx.fillRect(0, wipeY + 6, width, 6);
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, wipeY + 12, width, height - wipeY);
        break;
      }
      case 'tv_turnoff': {
        // TV Turn-Off: collapses vertically into a thin glowing horizontal line, then horizontally into a dot
        const t = 1 - progress;
        ctx.fillStyle = '#000000';
        if (t > 0.3) {
          const normH = (t - 0.3) / 0.7;
          const barH = height * (1 - normH) / 2;
          ctx.fillRect(0, 0, width, barH);
          ctx.fillRect(0, height - barH, width, barH);
        } else {
          // Collapse horizontally into a point
          ctx.fillRect(0, 0, width, height);
          const normW = t / 0.3;
          const barW = width * normW;
          ctx.fillStyle = '#ffffff';
          ctx.fillRect((width - barW) / 2, (height - 4) / 2, barW, 4);
        }
        break;
      }
    }

    ctx.restore();
  }
}

export const transitionsEngine = new TransitionsEngine();
