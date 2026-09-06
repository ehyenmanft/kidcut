/**
 * KIDCUT 100 PROCEDURAL RETRO & CINEMATIC TRANSITIONS ENGINE
 * Hardware-accelerated, mathematical canvas rendering for all 100 transitions.
 * Zero external video dependencies, 60fps deterministic rendering.
 */

export class TransitionsEngine {
  renderTransition(ctx, transitionType, progress, width, height) {
    if (!transitionType || transitionType === 'none' || progress <= 0) return;
    if (progress >= 1) return;

    ctx.save();
    const inv = 1 - progress;
    const cx = width / 2;
    const cy = height / 2;
    const maxDim = Math.hypot(width, height);

    // =========================================================================
    // 1. PIXEL & ARCADE (15)
    // =========================================================================
    if (transitionType === 'pixel_dissolve') {
      const blockSize = 24;
      ctx.fillStyle = '#000000';
      for (let y = 0; y < height; y += blockSize) {
        for (let x = 0; x < width; x += blockSize) {
          const hash = Math.sin(x * 12.9898 + y * 78.233) * 43758.5453;
          if ((hash - Math.floor(hash)) < inv) {
            ctx.fillRect(x, y, blockSize, blockSize);
          }
        }
      }
    } else if (transitionType === 'pixel_mosaic') {
      const blockSize = Math.max(4, Math.round(48 * inv));
      ctx.fillStyle = `rgba(0, 0, 0, ${inv * 0.8})`;
      for (let y = 0; y < height; y += blockSize * 2) {
        for (let x = 0; x < width; x += blockSize * 2) {
          ctx.fillRect(x, y, blockSize, blockSize);
        }
      }
    } else if (transitionType === 'pixel_fall') {
      const colW = 20;
      ctx.fillStyle = '#000000';
      for (let x = 0; x < width; x += colW) {
        const hash = ((Math.sin(x * 91.34) + 1) / 2) * 0.4;
        const colProg = Math.max(0, Math.min(1, (progress - hash) / 0.6));
        const fallH = height * (1 - colProg);
        ctx.fillRect(x, 0, colW, fallH);
      }
    } else if (transitionType === 'tetris_drop') {
      const block = 32;
      const colors = ['#00f0ff', '#ffd200', '#ff0055', '#39ff14', '#9d4edd'];
      for (let y = 0; y < height; y += block) {
        for (let x = 0; x < width; x += block) {
          const dropThresh = (y / height) * 0.7 + ((x % (block * 4)) / (block * 4)) * 0.3;
          if (inv > dropThresh) {
            ctx.fillStyle = colors[Math.floor((x + y) / block) % colors.length];
            ctx.fillRect(x, y, block - 2, block - 2);
          }
        }
      }
    } else if (transitionType === 'pacman_chomp') {
      const angle = (1 - Math.abs(progress - 0.5) * 2) * Math.PI * 0.45;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, maxDim / 2, angle, Math.PI * 2 - angle);
      ctx.lineTo(cx, cy);
      ctx.fill();
    } else if (transitionType === 'space_invader') {
      const step = 40;
      ctx.fillStyle = '#000000';
      const cutY = height * inv;
      ctx.fillRect(0, 0, width, cutY);
      // Invader teeth
      ctx.fillStyle = '#39ff14';
      for (let x = 0; x < width; x += step) {
        ctx.fillRect(x + 8, cutY - 12, 24, 12);
        ctx.fillRect(x + 14, cutY, 12, 8);
      }
    } else if (transitionType === 'bit_crush_wipe') {
      const steps = 8;
      const curStep = Math.floor(progress * steps);
      const stepH = height / steps;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, height - (curStep * stepH));
    } else if (transitionType === 'brick_wall_build') {
      const brickH = 24;
      const brickW = 48;
      const totalRows = Math.ceil(height / brickH);
      ctx.fillStyle = '#b84224';
      for (let r = 0; r < totalRows; r++) {
        const rowProg = Math.max(0, Math.min(1, (progress * totalRows - (totalRows - 1 - r))));
        if (rowProg < 1) {
          const y = r * brickH;
          const shift = (r % 2) * (brickW / 2);
          for (let x = -shift; x < width; x += brickW) {
            ctx.fillRect(x, y, brickW - 2, brickH - 2);
          }
        }
      }
    } else if (transitionType === 'dither_fade') {
      const dither = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
      const bSize = 6;
      ctx.fillStyle = '#000000';
      for (let y = 0; y < height; y += bSize * 4) {
        for (let x = 0; x < width; x += bSize * 4) {
          for (let dy = 0; dy < 4; dy++) {
            for (let dx = 0; dx < 4; dx++) {
              const rank = dither[dy * 4 + dx] / 16;
              if (rank < inv) {
                ctx.fillRect(x + dx * bSize, y + dy * bSize, bSize, bSize);
              }
            }
          }
        }
      }
    } else if (transitionType === 'arcade_curtain') {
      const curtainW = (width / 2) * inv;
      ctx.fillStyle = '#8b0000';
      ctx.fillRect(0, 0, curtainW, height);
      ctx.fillRect(width - curtainW, 0, curtainW, height);
      ctx.fillStyle = '#ffd200';
      ctx.fillRect(curtainW - 6, 0, 6, height);
      ctx.fillRect(width - curtainW, 0, 6, height);
    } else if (transitionType === 'scanline_sweep') {
      const sweepY = height * progress;
      ctx.fillStyle = 'rgba(0, 240, 255, 0.4)';
      ctx.fillRect(0, sweepY - 20, width, 40);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, sweepY - 2, width, 4);
      ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
      ctx.fillRect(0, sweepY + 20, width, height - sweepY);
    } else if (transitionType === 'voxel_explode') {
      const b = 30;
      ctx.fillStyle = '#16122c';
      for (let y = 0; y < height; y += b) {
        for (let x = 0; x < width; x += b) {
          const dist = Math.hypot(x - cx, y - cy);
          const size = Math.max(0, b * (1 - progress * (dist / cx)));
          ctx.fillRect(x + (b - size) / 2, y + (b - size) / 2, size, size);
        }
      }
    } else if (transitionType === 'ascii_matrix') {
      ctx.fillStyle = '#05040a';
      ctx.fillRect(0, 0, width, height * inv);
      ctx.fillStyle = '#39ff14';
      ctx.font = '12px monospace';
      for (let x = 0; x < width; x += 16) {
        const char = String.fromCharCode(33 + Math.floor(Math.sin(x + progress * 10) * 40));
        ctx.fillText(char, x, height * inv + 14);
      }
    } else if (transitionType === 'tv_turnoff') {
      ctx.fillStyle = '#000000';
      if (inv > 0.3) {
        const normH = (inv - 0.3) / 0.7;
        const barH = (height * (1 - normH)) / 2;
        ctx.fillRect(0, 0, width, barH);
        ctx.fillRect(0, height - barH, width, barH);
      } else {
        ctx.fillRect(0, 0, width, height);
        const barW = width * (inv / 0.3);
        ctx.fillStyle = '#ffffff';
        ctx.fillRect((width - barW) / 2, cy - 2, barW, 4);
      }
    } else if (transitionType === 'tv_turnon') {
      ctx.fillStyle = '#000000';
      if (progress < 0.3) {
        ctx.fillRect(0, 0, width, height);
        const barW = width * (progress / 0.3);
        ctx.fillStyle = '#ffffff';
        ctx.fillRect((width - barW) / 2, cy - 2, barW, 4);
      } else {
        const normH = (progress - 0.3) / 0.7;
        const barH = (height * (1 - normH)) / 2;
        ctx.fillRect(0, 0, width, barH);
        ctx.fillRect(0, height - barH, width, barH);
      }

    // =========================================================================
    // 2. CORTINILLAS & WIPES (20)
    // =========================================================================
    } else if (transitionType === 'wipe_left') {
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width * inv, height);
    } else if (transitionType === 'wipe_right') {
      ctx.fillStyle = '#000000';
      ctx.fillRect(width * progress, 0, width * inv, height);
    } else if (transitionType === 'wipe_up') {
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, height * inv);
    } else if (transitionType === 'wipe_down') {
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, height * progress, width, height * inv);
    } else if (transitionType === 'wipe_split_h') {
      const halfW = (width / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, halfW, height);
      ctx.fillRect(width - halfW, 0, halfW, height);
    } else if (transitionType === 'wipe_split_v') {
      const halfH = (height / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, halfH);
      ctx.fillRect(0, height - halfH, width, halfH);
    } else if (transitionType === 'wipe_diagonal_tl') {
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.lineTo(width * inv * 2, 0);
      ctx.lineTo(0, height * inv * 2);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'wipe_diagonal_tr') {
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(width, 0);
      ctx.lineTo(width - width * inv * 2, 0);
      ctx.lineTo(width, height * inv * 2);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'wipe_diagonal_bl') {
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(0, height);
      ctx.lineTo(width * inv * 2, height);
      ctx.lineTo(0, height - height * inv * 2);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'wipe_diagonal_br') {
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(width, height);
      ctx.lineTo(width - width * inv * 2, height);
      ctx.lineTo(width, height - height * inv * 2);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'wipe_zigzag') {
      const cutX = width * inv;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.lineTo(cutX, 0);
      const teeth = 16;
      const tH = height / teeth;
      for (let i = 0; i < teeth; i++) {
        const offset = (i % 2 === 0) ? 24 : -24;
        ctx.lineTo(cutX + offset, (i + 1) * tH);
      }
      ctx.lineTo(0, height);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'wipe_sawtooth') {
      const cutY = height * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, cutY, width, height - cutY);
      ctx.fillStyle = '#ffd200';
      const teeth = 20;
      const tW = width / teeth;
      ctx.beginPath();
      for (let i = 0; i < teeth; i++) {
        ctx.moveTo(i * tW, cutY);
        ctx.lineTo(i * tW + tW / 2, cutY - 14);
        ctx.lineTo((i + 1) * tW, cutY);
      }
      ctx.fill();
    } else if (transitionType === 'wipe_checkerboard') {
      const size = 40;
      ctx.fillStyle = '#000000';
      for (let y = 0; y < height; y += size) {
        for (let x = 0; x < width; x += size) {
          const isBlack = (Math.floor(x / size) + Math.floor(y / size)) % 2 === 0;
          if (isBlack && progress < 0.8) {
            ctx.fillRect(x, y, size * (1 - progress / 0.8), size * (1 - progress / 0.8));
          } else if (!isBlack && progress < 0.5) {
            ctx.fillRect(x, y, size * (1 - progress / 0.5), size * (1 - progress / 0.5));
          }
        }
      }
    } else if (transitionType === 'wipe_blinds_h') {
      const count = 12;
      const bH = height / count;
      ctx.fillStyle = '#000000';
      for (let i = 0; i < count; i++) {
        ctx.fillRect(0, i * bH, width, bH * inv);
      }
    } else if (transitionType === 'wipe_blinds_v') {
      const count = 16;
      const bW = width / count;
      ctx.fillStyle = '#000000';
      for (let i = 0; i < count; i++) {
        ctx.fillRect(i * bW, 0, bW * inv, height);
      }
    } else if (transitionType === 'wipe_spiral') {
      const rot = progress * Math.PI * 4;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, maxDim * inv, rot, rot + Math.PI * 1.5);
      ctx.fill();
    } else if (transitionType === 'wipe_clock') {
      const angle = progress * Math.PI * 2;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, maxDim, -Math.PI / 2 + angle, Math.PI * 1.5);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'wipe_curtain_open') {
      const openW = (width / 2) * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, (width / 2) - openW, height);
      ctx.fillRect(cx + openW, 0, (width / 2) - openW, height);
    } else if (transitionType === 'wipe_curtain_close') {
      const closeW = (width / 2) * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, closeW, height);
      ctx.fillRect(width - closeW, 0, closeW, height);
    } else if (transitionType === 'wipe_cross_split') {
      const wPart = (width / 2) * inv;
      const hPart = (height / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, wPart, hPart);
      ctx.fillRect(width - wPart, 0, wPart, hPart);
      ctx.fillRect(0, height - hPart, wPart, hPart);
      ctx.fillRect(width - wPart, height - hPart, wPart, hPart);

    // =========================================================================
    // 3. GLITCH & DISTORSIÓN (15)
    // =========================================================================
    } else if (transitionType === 'glitch_rgb_split') {
      const shift = Math.round(30 * Math.sin(progress * Math.PI));
      ctx.fillStyle = 'rgba(255, 0, 85, 0.4)';
      ctx.fillRect(shift, 0, width, height);
      ctx.fillStyle = 'rgba(0, 240, 255, 0.4)';
      ctx.fillRect(-shift, 0, width, height);
    } else if (transitionType === 'glitch_vhs_tear') {
      ctx.fillStyle = '#ffffff';
      const tearCount = 8;
      for (let i = 0; i < tearCount; i++) {
        const y = (i / tearCount) * height;
        const wOffset = Math.sin(i * 13 + progress * 20) * 80;
        ctx.fillRect(wOffset, y, width, 6);
      }
    } else if (transitionType === 'glitch_databend') {
      const blocks = 12;
      for (let i = 0; i < blocks; i++) {
        ctx.fillStyle = (i % 2 === 0) ? '#ff0055' : '#00f0ff';
        const bh = height / blocks;
        const bx = Math.sin(i * 4 + progress * 15) * 40 * inv;
        ctx.fillRect(bx, i * bh, width, bh);
      }
    } else if (transitionType === 'glitch_block_scramble') {
      const s = 48;
      ctx.fillStyle = '#000000';
      for (let y = 0; y < height; y += s) {
        for (let x = 0; x < width; x += s) {
          if (Math.sin(x * 3 + y * 7 + progress * 10) > 0.1 * inv) {
            ctx.fillRect(x, y, s, s);
          }
        }
      }
    } else if (transitionType === 'glitch_hsync_roll') {
      const rollY = (progress * height * 3) % height;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, rollY, width, 24);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, rollY + 24, width, 4);
    } else if (transitionType === 'glitch_static_noise') {
      const alpha = Math.sin(progress * Math.PI) * 0.75;
      ctx.fillStyle = `rgba(255, 255, 255, ${alpha})`;
      for (let i = 0; i < 200; i++) {
        ctx.fillRect(Math.random() * width, Math.random() * height, 8, 4);
      }
    } else if (transitionType === 'glitch_pixel_sort') {
      ctx.fillStyle = 'rgba(0, 240, 255, 0.5)';
      for (let x = 0; x < width; x += 16) {
        const len = Math.sin(x * 0.1 + progress * 5) * height * 0.4 * inv;
        ctx.fillRect(x, cy - len / 2, 8, len);
      }
    } else if (transitionType === 'glitch_corrupt_scan') {
      const y = height * progress;
      ctx.fillStyle = '#ff0055';
      ctx.fillRect(0, y, width, 8);
      ctx.fillStyle = '#00f0ff';
      ctx.fillRect(0, y + 8, width, 8);
      ctx.fillStyle = 'rgba(0,0,0,0.6)';
      ctx.fillRect(0, y + 16, width, height - y);
    } else if (transitionType === 'glitch_bit_flip') {
      if (Math.sin(progress * Math.PI * 8) > 0) {
        ctx.fillStyle = 'rgba(255, 255, 255, 0.8)';
        ctx.fillRect(0, 0, width, height);
      }
    } else if (transitionType === 'glitch_interlace') {
      ctx.fillStyle = '#000000';
      for (let y = 0; y < height; y += 4) {
        if ((y / 4) % 2 === 0) {
          ctx.fillRect(0, y, width * inv, 2);
        }
      }
    } else if (transitionType === 'glitch_quantum_jump') {
      const alpha = Math.sin(progress * Math.PI);
      ctx.fillStyle = `rgba(157, 78, 221, ${alpha * 0.6})`;
      ctx.fillRect(0, 0, width, height);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, cy - 10, width, 20);
    } else if (transitionType === 'glitch_signal_loss') {
      ctx.fillStyle = '#0011bb';
      ctx.fillRect(0, 0, width, height * inv);
      ctx.fillStyle = '#ffffff';
      ctx.font = 'bold 18px monospace';
      ctx.fillText('NO SIGNAL', cx - 50, cy);
    } else if (transitionType === 'glitch_frame_ghost') {
      ctx.fillStyle = `rgba(0, 240, 255, ${(1 - progress) * 0.5})`;
      ctx.fillRect(10, 10, width - 20, height - 20);
    } else if (transitionType === 'glitch_bad_tracking') {
      const trackH = height * 0.3 * (1 - progress);
      ctx.fillStyle = 'rgba(0, 0, 0, 0.85)';
      ctx.fillRect(0, height - trackH, width, trackH);
      ctx.fillStyle = '#ffffff';
      for (let i = 0; i < 15; i++) {
        ctx.fillRect(Math.random() * width, height - trackH + Math.random() * trackH, 30, 2);
      }
    } else if (transitionType === 'glitch_cyber_slice') {
      const y = height * progress;
      ctx.fillStyle = '#00f0ff';
      ctx.fillRect(0, y, width, 4);
      ctx.fillStyle = '#ffd200';
      ctx.fillRect(0, y - 4, width, 2);
      ctx.fillStyle = '#080611';
      ctx.fillRect(0, y + 4, width, height - y);

    // =========================================================================
    // 4. ZOOMS, MOVIMIENTO & WARP (15)
    // =========================================================================
    } else if (transitionType === 'zoom_in_punch') {
      const r = maxDim * (1 - progress);
      ctx.strokeStyle = '#ffd200';
      ctx.lineWidth = 12;
      ctx.strokeRect(cx - r / 2, cy - r / 2, r, r);
      ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
      ctx.fillRect(0, 0, width, cy - r / 2);
      ctx.fillRect(0, cy + r / 2, width, height);
    } else if (transitionType === 'zoom_out_spin') {
      const r = (maxDim / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.rect(width, 0, -width, height);
      ctx.fill();
    } else if (transitionType === 'spin_clockwise') {
      const rot = progress * Math.PI * 2;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, maxDim, rot, rot + Math.PI);
      ctx.fill();
    } else if (transitionType === 'spin_counter') {
      const rot = -progress * Math.PI * 2;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, maxDim, rot, rot + Math.PI);
      ctx.fill();
    } else if (transitionType === 'whip_pan_left') {
      const x = width * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, x, height);
      ctx.fillStyle = 'rgba(255, 255, 255, 0.4)';
      ctx.fillRect(x - 20, 0, 20, height);
    } else if (transitionType === 'whip_pan_right') {
      const x = width * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(x, 0, width - x, height);
      ctx.fillStyle = 'rgba(255, 255, 255, 0.4)';
      ctx.fillRect(x, 0, 20, height);
    } else if (transitionType === 'whip_pan_up') {
      const y = height * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, y);
      ctx.fillStyle = 'rgba(255, 255, 255, 0.4)';
      ctx.fillRect(0, y - 20, width, 20);
    } else if (transitionType === 'whip_pan_down') {
      const y = height * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, y, width, height - y);
      ctx.fillStyle = 'rgba(255, 255, 255, 0.4)';
      ctx.fillRect(0, y, width, 20);
    } else if (transitionType === 'warp_tunnel') {
      for (let i = 0; i < 5; i++) {
        const rad = maxDim * (1 - ((progress + i * 0.2) % 1.0));
        ctx.strokeStyle = '#00f0ff';
        ctx.lineWidth = 4;
        ctx.strokeRect(cx - rad / 2, cy - rad / 2, rad, rad);
      }
    } else if (transitionType === 'hyperspace_jump') {
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 2;
      for (let i = 0; i < 40; i++) {
        const ang = (i / 40) * Math.PI * 2;
        const len = progress * maxDim * 0.6;
        ctx.beginPath();
        ctx.moveTo(cx + Math.cos(ang) * 10, cy + Math.sin(ang) * 10);
        ctx.lineTo(cx + Math.cos(ang) * len, cy + Math.sin(ang) * len);
        ctx.stroke();
      }
    } else if (transitionType === 'fisheye_bulge') {
      const r = (maxDim / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.rect(width, 0, -width, height);
      ctx.fill();
    } else if (transitionType === 'swirl_vortex') {
      const rot = progress * Math.PI * 3;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, maxDim * inv, rot, rot + Math.PI);
      ctx.fill();
    } else if (transitionType === 'ripple_water') {
      const r = (maxDim / 2) * progress;
      ctx.strokeStyle = 'rgba(0, 240, 255, 0.7)';
      ctx.lineWidth = 14;
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.stroke();
    } else if (transitionType === 'shockwave_blast') {
      const r = maxDim * progress;
      ctx.strokeStyle = '#ff0055';
      ctx.lineWidth = 20;
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.stroke();
    } else if (transitionType === 'lens_bounce') {
      const bounce = Math.abs(Math.sin(progress * Math.PI * 2)) * 30 * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, bounce);
      ctx.fillRect(0, height - bounce, width, bounce);

    // =========================================================================
    // 5. FORMAS & IRIS (15)
    // =========================================================================
    } else if (transitionType === 'iris_circle_open') {
      const r = (maxDim / 2) * progress;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      ctx.arc(cx, cy, r, 0, Math.PI * 2, true);
      ctx.fill();
    } else if (transitionType === 'iris_circle_close') {
      const r = (maxDim / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      ctx.arc(cx, cy, r, 0, Math.PI * 2, true);
      ctx.fill();
    } else if (transitionType === 'iris_box_open') {
      const s = maxDim * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, cy - s / 2);
      ctx.fillRect(0, cy + s / 2, width, height);
      ctx.fillRect(0, 0, cx - s / 2, height);
      ctx.fillRect(cx + s / 2, 0, width, height);
    } else if (transitionType === 'iris_box_close') {
      const s = maxDim * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, width, cy - s / 2);
      ctx.fillRect(0, cy + s / 2, width, height);
      ctx.fillRect(0, 0, cx - s / 2, height);
      ctx.fillRect(cx + s / 2, 0, width, height);
    } else if (transitionType === 'iris_diamond') {
      const d = maxDim * progress;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      ctx.moveTo(cx, cy - d / 2);
      ctx.lineTo(cx + d / 2, cy);
      ctx.lineTo(cx, cy + d / 2);
      ctx.lineTo(cx - d / 2, cy);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'iris_triangle') {
      const t = maxDim * progress;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      ctx.moveTo(cx, cy - t / 2);
      ctx.lineTo(cx + t / 2, cy + t / 2);
      ctx.lineTo(cx - t / 2, cy + t / 2);
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'iris_pentagon' || transitionType === 'iris_hexagon') {
      const sides = transitionType === 'iris_pentagon' ? 5 : 6;
      const r = (maxDim / 2) * progress;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      for (let i = 0; i < sides; i++) {
        const a = (i / sides) * Math.PI * 2 - Math.PI / 2;
        const px = cx + Math.cos(a) * r;
        const py = cy + Math.sin(a) * r;
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      }
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'iris_heart') {
      const s = progress * 20;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      ctx.moveTo(cx, cy);
      for (let a = 0; a < Math.PI * 2; a += 0.05) {
        const x = 16 * Math.pow(Math.sin(a), 3);
        const y = -(13 * Math.cos(a) - 5 * Math.cos(2 * a) - 2 * Math.cos(3 * a) - Math.cos(4 * a));
        ctx.lineTo(cx + x * s, cy + y * s);
      }
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'iris_star') {
      const rOuter = (maxDim / 2) * progress;
      const rInner = rOuter / 2;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      for (let i = 0; i < 10; i++) {
        const r = (i % 2 === 0) ? rOuter : rInner;
        const a = (i / 10) * Math.PI * 2 - Math.PI / 2;
        const px = cx + Math.cos(a) * r;
        const py = cy + Math.sin(a) * r;
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      }
      ctx.closePath();
      ctx.fill();
    } else if (transitionType === 'concentric_rings') {
      for (let i = 0; i < 6; i++) {
        const r = (maxDim / 6) * i * inv;
        ctx.strokeStyle = (i % 2 === 0) ? '#00f0ff' : '#ff0055';
        ctx.lineWidth = 14;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.stroke();
      }
    } else if (transitionType === 'grid_reveal_4x4' || transitionType === 'grid_reveal_8x8') {
      const n = transitionType === 'grid_reveal_4x4' ? 4 : 8;
      const cw = width / n;
      const ch = height / n;
      ctx.fillStyle = '#000000';
      for (let y = 0; y < n; y++) {
        for (let x = 0; x < n; x++) {
          const thresh = (x + y) / (n * 2);
          if (inv > thresh) {
            ctx.fillRect(x * cw, y * ch, cw, ch);
          }
        }
      }
    } else if (transitionType === 'honeycomb_hex') {
      const s = 32;
      ctx.fillStyle = '#000000';
      for (let y = 0; y < height; y += s) {
        for (let x = 0; x < width; x += s) {
          if (Math.sin(x * 0.1 + y * 0.1 + progress * 5) < 0) {
            ctx.fillRect(x, y, s * inv, s * inv);
          }
        }
      }
    } else if (transitionType === 'cross_reveal') {
      const thick = maxDim * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, cx - thick / 2, cy - thick / 2);
      ctx.fillRect(cx + thick / 2, 0, cx - thick / 2, cy - thick / 2);
      ctx.fillRect(0, cy + thick / 2, cx - thick / 2, cy - thick / 2);
      ctx.fillRect(cx + thick / 2, cy + thick / 2, cx - thick / 2, cy - thick / 2);

    // =========================================================================
    // 6. LUZ, FUEGO & ENERGÍA (10)
    // =========================================================================
    } else if (transitionType === 'flash_white') {
      const alpha = Math.sin(progress * Math.PI);
      ctx.fillStyle = `rgba(255, 255, 255, ${alpha})`;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'flash_gold') {
      const alpha = Math.sin(progress * Math.PI);
      ctx.fillStyle = `rgba(255, 210, 0, ${alpha * 0.85})`;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'laser_sweep') {
      const lx = width * progress;
      ctx.fillStyle = '#00f0ff';
      ctx.fillRect(lx - 4, 0, 8, height);
      ctx.fillStyle = 'rgba(0, 240, 255, 0.4)';
      ctx.fillRect(lx - 20, 0, 40, height);
      ctx.fillStyle = '#080611';
      ctx.fillRect(0, 0, lx - 20, height);
    } else if (transitionType === 'neon_strobe') {
      const colors = ['#ff007f', '#00f0ff', '#39ff14', '#ffd200'];
      const col = colors[Math.floor(progress * 12) % colors.length];
      ctx.fillStyle = col;
      ctx.globalAlpha = Math.sin(progress * Math.PI) * 0.7;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'sunflare_burn') {
      const alpha = Math.sin(progress * Math.PI);
      const grad = ctx.createRadialGradient(cx, cy, 10, cx, cy, width * 0.7);
      grad.addColorStop(0, `rgba(255, 255, 200, ${alpha})`);
      grad.addColorStop(0.5, `rgba(255, 140, 0, ${alpha * 0.8})`);
      grad.addColorStop(1, `rgba(0, 0, 0, ${alpha * 0.9})`);
      ctx.fillStyle = grad;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'fire_inferno') {
      const y = height * inv;
      ctx.fillStyle = '#ff3300';
      ctx.fillRect(0, y, width, height - y);
      ctx.fillStyle = '#ffcc00';
      for (let x = 0; x < width; x += 20) {
        ctx.fillRect(x, y - 16, 14, 16);
      }
    } else if (transitionType === 'smoke_fog') {
      const alpha = Math.sin(progress * Math.PI) * 0.8;
      ctx.fillStyle = `rgba(200, 200, 220, ${alpha})`;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'sparkle_burst') {
      ctx.fillStyle = '#ffd200';
      for (let i = 0; i < 30; i++) {
        const a = (i / 30) * Math.PI * 2;
        const d = maxDim * progress * 0.5;
        ctx.fillRect(cx + Math.cos(a) * d, cy + Math.sin(a) * d, 6, 6);
      }
    } else if (transitionType === 'electric_arc') {
      ctx.strokeStyle = '#00f0ff';
      ctx.lineWidth = 4;
      ctx.beginPath();
      ctx.moveTo(0, cy);
      for (let x = 0; x < width; x += 30) {
        ctx.lineTo(x, cy + (Math.random() - 0.5) * 80 * inv);
      }
      ctx.stroke();
    } else if (transitionType === 'aurora_glow') {
      const alpha = Math.sin(progress * Math.PI) * 0.6;
      const grad = ctx.createLinearGradient(0, 0, width, height);
      grad.addColorStop(0, `rgba(0, 255, 150, ${alpha})`);
      grad.addColorStop(1, `rgba(180, 0, 255, ${alpha})`);
      ctx.fillStyle = grad;
      ctx.fillRect(0, 0, width, height);

    // =========================================================================
    // 7. CINE & PAPEL (10)
    // =========================================================================
    } else if (transitionType === 'film_burn') {
      const r = (maxDim / 2) * progress;
      ctx.fillStyle = '#080611';
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.fill();
      ctx.strokeStyle = '#ff7700';
      ctx.lineWidth = 10;
      ctx.stroke();
    } else if (transitionType === 'film_strip_roll') {
      const y = (progress * height * 2) % height;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, y, width, 20);
      ctx.fillStyle = '#ffffff';
      for (let x = 10; x < width; x += 40) {
        ctx.fillRect(x, y + 4, 16, 12);
      }
    } else if (transitionType === 'sepia_dissolve') {
      ctx.fillStyle = `rgba(112, 66, 20, ${Math.sin(progress * Math.PI) * 0.7})`;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'camera_shutter') {
      const s = (maxDim / 2) * inv;
      ctx.fillStyle = '#111111';
      ctx.beginPath();
      ctx.rect(0, 0, width, height);
      ctx.arc(cx, cy, s, 0, Math.PI * 2, true);
      ctx.fill();
    } else if (transitionType === 'crossfade_smooth') {
      ctx.fillStyle = `rgba(8, 6, 17, ${Math.sin(progress * Math.PI) * 0.5})`;
      ctx.fillRect(0, 0, width, height);
    } else if (transitionType === 'page_turn_left') {
      const x = width * inv;
      ctx.fillStyle = '#000000';
      ctx.fillRect(0, 0, x, height);
      ctx.fillStyle = '#333333';
      ctx.fillRect(x - 14, 0, 14, height);
    } else if (transitionType === 'page_turn_right') {
      const x = width * progress;
      ctx.fillStyle = '#000000';
      ctx.fillRect(x, 0, width - x, height);
      ctx.fillStyle = '#333333';
      ctx.fillRect(x, 0, 14, height);
    } else if (transitionType === 'paper_tear') {
      const halfH = (height / 2) * inv;
      ctx.fillStyle = '#080611';
      ctx.fillRect(0, 0, width, halfH);
      ctx.fillRect(0, height - halfH, width, halfH);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, halfH - 2, width, 4);
      ctx.fillRect(0, height - halfH - 2, width, 4);
    } else if (transitionType === 'black_hole') {
      const r = (maxDim / 2) * inv;
      ctx.fillStyle = '#000000';
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.rect(width, 0, -width, height);
      ctx.fill();
      ctx.strokeStyle = '#9d4edd';
      ctx.lineWidth = 6;
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.stroke();
    } else if (transitionType === 'vintage_projector') {
      if (Math.sin(progress * Math.PI * 10) > 0) {
        ctx.fillStyle = 'rgba(0, 0, 0, 0.4)';
        ctx.fillRect(0, 0, width, height);
      }
    }

    ctx.restore();
  }
}

export const transitionsEngine = new TransitionsEngine();
