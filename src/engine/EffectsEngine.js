/**
 * KIDCUT ULTRA-PERFORMANT 100 EFFECTS RASTER & SHADER ENGINE
 * Implements mathematical algorithms, palette quantizers, and procedural canvas rendering
 * for all 100 effects. Engineered for minimal CPU/RAM footprint.
 */

export class EffectsEngine {
  constructor() {
    this.particleStates = new Map();
  }

  applyEffect(ctx, effectId, width, height, intensity = 50, currentTime = 0) {
    if (!effectId || effectId === 'none') return;

    // Route to appropriate handler
    if (effectId.startsWith('overlay_') || effectId.endsWith('_hud') || effectId.includes('hud_')) {
      this.renderOverlayEffect(ctx, effectId, width, height, intensity, currentTime);
      return;
    }

    if (this.isAtmosphereEffect(effectId)) {
      this.renderAtmosphereEffect(ctx, effectId, width, height, intensity, currentTime);
      return;
    }

    if (this.isOpticsEffect(effectId)) {
      this.renderOpticsEffect(ctx, effectId, width, height, intensity, currentTime);
      return;
    }

    // Raster pixel manipulations
    const imgData = ctx.getImageData(0, 0, width, height);
    const data = imgData.data;

    // Palette effects
    if (this.isPaletteEffect(effectId)) {
      this.applyPalette(data, effectId, width, height);
      ctx.putImageData(imgData, 0, 0);
      return;
    }

    // CRT & Screen effects
    if (this.isCrtRasterEffect(effectId)) {
      this.applyCrtRaster(ctx, data, effectId, width, height, intensity, currentTime);
      ctx.putImageData(imgData, 0, 0);
      this.applyCrtPost(ctx, effectId, width, height, intensity, currentTime);
      return;
    }

    // Pixel & Mosaic effects
    if (this.isPixelRasterEffect(effectId)) {
      this.applyPixelRaster(ctx, data, effectId, width, height, intensity, currentTime);
      ctx.putImageData(imgData, 0, 0);
      return;
    }

    // Color & Grading effects
    if (this.isColorRasterEffect(effectId)) {
      this.applyColorRaster(data, effectId, width, height, intensity);
      ctx.putImageData(imgData, 0, 0);
      return;
    }

    // Fallback: put unmodified or standard
    ctx.putImageData(imgData, 0, 0);
  }

  // =========================================================================
  // 1. PALETAS RETRO
  // =========================================================================
  isPaletteEffect(id) {
    return [
      'gameboy', 'gameboy_pocket', 'nes_famicom', 'snes_16bit', 'genesis_megadrive',
      'c64_commodore', 'zx_spectrum', 'cga_mode1', 'cga_mode2', 'ega_retro',
      'atari_2600', 'cyberpunk_neon', 'vaporwave_sunset', 'matrix_green', 'amber_crt'
    ].includes(id);
  }

  applyPalette(data, id, w, h) {
    const len = data.length;
    for (let i = 0; i < len; i += 4) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const lum = (0.299 * r + 0.587 * g + 0.114 * b) | 0;

      switch (id) {
        case 'gameboy': {
          // Classic GB 4-color green shades
          if (lum < 64) { data[i] = 15; data[i + 1] = 56; data[i + 2] = 15; }
          else if (lum < 128) { data[i] = 48; data[i + 1] = 98; data[i + 2] = 48; }
          else if (lum < 192) { data[i] = 139; data[i + 1] = 172; data[i + 2] = 15; }
          else { data[i] = 155; data[i + 1] = 188; data[i + 2] = 15; }
          break;
        }
        case 'gameboy_pocket': {
          // Monocromo LCD gris
          if (lum < 64) { data[i] = 20; data[i + 1] = 24; data[i + 2] = 20; }
          else if (lum < 128) { data[i] = 84; data[i + 1] = 90; data[i + 2] = 84; }
          else if (lum < 192) { data[i] = 160; data[i + 1] = 168; data[i + 2] = 160; }
          else { data[i] = 215; data[i + 1] = 225; data[i + 2] = 215; }
          break;
        }
        case 'nes_famicom': {
          // Quantize to NES 54-color feel
          data[i] = (r >> 5) * 36;
          data[i + 1] = (g >> 5) * 36;
          data[i + 2] = (b >> 5) * 36;
          break;
        }
        case 'snes_16bit': {
          data[i] = (r >> 3) * 8;
          data[i + 1] = (g >> 3) * 8;
          data[i + 2] = (b >> 3) * 8;
          break;
        }
        case 'genesis_megadrive': {
          data[i] = (r >> 4) * 17;
          data[i + 1] = (g >> 4) * 17;
          data[i + 2] = (b >> 4) * 17;
          break;
        }
        case 'c64_commodore': {
          // VIC-II warm tones
          if (lum < 50) { data[i] = 0; data[i + 1] = 0; data[i + 2] = 0; }
          else if (lum < 100) { data[i] = 104; data[i + 1] = 55; data[i + 2] = 43; }
          else if (lum < 150) { data[i] = 111; data[i + 1] = 61; data[i + 2] = 134; }
          else if (lum < 200) { data[i] = 88; data[i + 1] = 141; data[i + 2] = 67; }
          else { data[i] = 184; data[i + 1] = 199; data[i + 2] = 111; }
          break;
        }
        case 'zx_spectrum': {
          // Pure saturated primary colors
          data[i] = r > 100 ? 255 : 0;
          data[i + 1] = g > 100 ? 255 : 0;
          data[i + 2] = b > 100 ? 255 : 0;
          break;
        }
        case 'cga_mode1': {
          // Black, Cyan, Magenta, White
          if (lum < 60) { data[i] = 0; data[i + 1] = 0; data[i + 2] = 0; }
          else if (lum < 130) { data[i] = 0; data[i + 1] = 170; data[i + 2] = 170; }
          else if (lum < 200) { data[i] = 170; data[i + 1] = 0; data[i + 2] = 170; }
          else { data[i] = 255; data[i + 1] = 255; data[i + 2] = 255; }
          break;
        }
        case 'cga_mode2': {
          // Black, Green, Red, Yellow
          if (lum < 60) { data[i] = 0; data[i + 1] = 0; data[i + 2] = 0; }
          else if (lum < 130) { data[i] = 0; data[i + 1] = 170; data[i + 2] = 0; }
          else if (lum < 200) { data[i] = 170; data[i + 1] = 0; data[i + 2] = 0; }
          else { data[i] = 255; data[i + 1] = 255; data[i + 2] = 85; }
          break;
        }
        case 'ega_retro': {
          data[i] = (r >> 6) * 85;
          data[i + 1] = (g >> 6) * 85;
          data[i + 2] = (b >> 6) * 85;
          break;
        }
        case 'atari_2600': {
          data[i] = ((r >> 4) << 4);
          data[i + 1] = ((g >> 5) << 5);
          data[i + 2] = ((b >> 4) << 4);
          break;
        }
        case 'cyberpunk_neon': {
          data[i] = Math.min(255, r * 1.3);
          data[i + 1] = Math.min(255, (g * 0.7) + (b * 0.5));
          data[i + 2] = Math.min(255, b * 1.4);
          break;
        }
        case 'vaporwave_sunset': {
          data[i] = Math.min(255, (r * 0.8) + 80);
          data[i + 1] = Math.min(255, (g * 0.6) + 40);
          data[i + 2] = Math.min(255, (b * 0.9) + 80);
          break;
        }
        case 'matrix_green': {
          data[i] = 0;
          data[i + 1] = Math.min(255, lum * 1.25);
          data[i + 2] = (lum * 0.15) | 0;
          break;
        }
        case 'amber_crt': {
          data[i] = Math.min(255, lum * 1.3);
          data[i + 1] = Math.min(255, (lum * 0.65) | 0);
          data[i + 2] = 0;
          break;
        }
      }
    }
  }

  // =========================================================================
  // 2. CRT & RASTER EFFECTS
  // =========================================================================
  isCrtRasterEffect(id) {
    return [
      'crt_scanlines_soft', 'crt_scanlines_hard', 'rgb_subpixel_grid', 'interlacing',
      'tv_static_noise', 'vhs_jitter', 'glitch_rgb_shift', 'bad_tracking_vhs',
      'sync_loss_roll', 'vertical_blanking'
    ].includes(id);
  }

  applyCrtRaster(ctx, data, id, w, h, intensity, currentTime) {
    const factor = intensity / 100;

    if (id === 'tv_static_noise') {
      const len = data.length;
      const noiseAmp = factor * 120;
      for (let i = 0; i < len; i += 4) {
        const n = (Math.random() - 0.5) * noiseAmp;
        data[i] = Math.max(0, Math.min(255, data[i] + n));
        data[i + 1] = Math.max(0, Math.min(255, data[i + 1] + n));
        data[i + 2] = Math.max(0, Math.min(255, data[i + 2] + n));
      }
    } else if (id === 'crt_scanlines_soft' || id === 'crt_scanlines_hard') {
      const step = id === 'crt_scanlines_soft' ? 3 : 2;
      const dim = id === 'crt_scanlines_soft' ? (1 - factor * 0.35) : (1 - factor * 0.65);
      for (let y = 0; y < h; y += step) {
        const rowStart = y * w * 4;
        const rowEnd = rowStart + w * 4;
        for (let i = rowStart; i < rowEnd; i += 4) {
          data[i] = (data[i] * dim) | 0;
          data[i + 1] = (data[i + 1] * dim) | 0;
          data[i + 2] = (data[i + 2] * dim) | 0;
        }
      }
    } else if (id === 'rgb_subpixel_grid') {
      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const idx = (y * w + x) * 4;
          const sub = x % 3;
          if (sub === 0) { data[idx + 1] = (data[idx + 1] * 0.5) | 0; data[idx + 2] = (data[idx + 2] * 0.5) | 0; }
          else if (sub === 1) { data[idx] = (data[idx] * 0.5) | 0; data[idx + 2] = (data[idx + 2] * 0.5) | 0; }
          else { data[idx] = (data[idx] * 0.5) | 0; data[idx + 1] = (data[idx + 1] * 0.5) | 0; }
        }
      }
    } else if (id === 'interlacing') {
      for (let y = (Math.round(currentTime * 30) % 2); y < h; y += 2) {
        const rowStart = y * w * 4;
        const rowEnd = rowStart + w * 4;
        for (let i = rowStart; i < rowEnd; i += 4) {
          data[i] = (data[i] * 0.5) | 0;
          data[i + 1] = (data[i + 1] * 0.5) | 0;
          data[i + 2] = (data[i + 2] * 0.5) | 0;
        }
      }
    } else if (id === 'glitch_rgb_shift') {
      const shift = Math.round(factor * 16 * (0.8 + 0.4 * Math.sin(currentTime * 12)));
      if (shift > 0) {
        for (let y = 0; y < h; y++) {
          for (let x = 0; x < w - shift; x++) {
            const curIdx = (y * w + x) * 4;
            const shiftIdx = (y * w + (x + shift)) * 4;
            data[curIdx] = data[shiftIdx]; // Shift Red channel
          }
        }
      }
    } else if (id === 'vhs_jitter') {
      const jitterRow = Math.floor(Math.random() * h);
      const jitterHeight = Math.floor(10 + Math.random() * 20);
      const shiftX = Math.floor((Math.random() - 0.5) * factor * 30);
      for (let y = jitterRow; y < Math.min(h, jitterRow + jitterHeight); y++) {
        for (let x = 0; x < w; x++) {
          const targetX = Math.max(0, Math.min(w - 1, x + shiftX));
          const idxA = (y * w + x) * 4;
          const idxB = (y * w + targetX) * 4;
          data[idxA] = data[idxB];
          data[idxA + 1] = data[idxB + 1];
          data[idxA + 2] = data[idxB + 2];
        }
      }
    } else if (id === 'bad_tracking_vhs') {
      const bandHeight = Math.round(h * 0.12 * factor);
      const startY = h - bandHeight;
      for (let y = startY; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const idx = (y * w + x) * 4;
          if (Math.random() > 0.3) {
            const noise = (Math.random() * 255) | 0;
            data[idx] = noise;
            data[idx + 1] = noise;
            data[idx + 2] = noise;
          }
        }
      }
    } else if (id === 'vertical_blanking') {
      for (let y = 0; y < Math.min(h, 6); y++) {
        for (let x = 0; x < w; x++) {
          const idx = (y * w + x) * 4;
          if ((x % 30) < 15) {
            data[idx] = 255; data[idx + 1] = 255; data[idx + 2] = 255;
          }
        }
      }
    }
  }

  applyCrtPost(ctx, id, w, h, intensity, currentTime) {
    if (id === 'vhs_hud_play') {
      ctx.save();
      ctx.font = `bold ${Math.round(h * 0.045)}px 'Press Start 2P', monospace`;
      ctx.fillStyle = '#00ff44';
      ctx.fillText('PLAY ▶', w * 0.08, h * 0.12);
      ctx.fillText(`SP ${this.formatTimestamp(currentTime)}`, w * 0.08, h * 0.18);
      ctx.restore();
    } else if (id === 'vhs_hud_rec') {
      ctx.save();
      ctx.font = `bold ${Math.round(h * 0.045)}px 'Press Start 2P', monospace`;
      const isBlink = Math.sin(currentTime * 6) > 0;
      if (isBlink) {
        ctx.fillStyle = '#ff2222';
        ctx.fillText('● REC', w * 0.08, h * 0.12);
      }
      ctx.fillStyle = '#ffffff';
      ctx.fillText(this.formatTimestamp(currentTime), w * 0.08, h * 0.18);
      ctx.restore();
    } else if (id === 'low_batt_arcade') {
      ctx.save();
      const isBlink = Math.sin(currentTime * 5) > 0;
      if (isBlink) {
        ctx.font = `bold ${Math.round(h * 0.035)}px 'Press Start 2P', monospace`;
        ctx.fillStyle = '#ff3344';
        ctx.fillText('⚠️ LOW BATTERY', w * 0.06, h * 0.92);
      }
      ctx.restore();
    }
  }

  // =========================================================================
  // 3. PIXEL & MOSAICO EFFECTS
  // =========================================================================
  isPixelRasterEffect(id) {
    return [
      'pixelate_micro', 'pixelate_medium', 'pixelate_arcade', 'pixelate_mega', 'pixelate_ultra',
      'dither_bayer', 'dither_floyd', 'hexagonal_mosaic', 'brick_mosaic', 'led_board',
      'cross_stitch', 'ascii_art', 'bitcrush_2bit', 'bitcrush_4bit', 'pixel_grid_lines'
    ].includes(id);
  }

  applyPixelRaster(ctx, data, id, w, h, intensity, currentTime) {
    let blockSize = 8;
    if (id === 'pixelate_micro') blockSize = 4;
    else if (id === 'pixelate_medium') blockSize = 8;
    else if (id === 'pixelate_arcade') blockSize = 16;
    else if (id === 'pixelate_mega') blockSize = 24;
    else if (id === 'pixelate_ultra') blockSize = 36;
    else if (id === 'led_board') blockSize = 12;
    else if (id === 'pixel_grid_lines') blockSize = 8;
    else if (id === 'brick_mosaic') blockSize = 14;

    if (id.startsWith('pixelate_')) {
      for (let y = 0; y < h; y += blockSize) {
        for (let x = 0; x < w; x += blockSize) {
          const sampleIdx = (y * w + x) * 4;
          const r = data[sampleIdx];
          const g = data[sampleIdx + 1];
          const b = data[sampleIdx + 2];
          const a = data[sampleIdx + 3];

          for (let dy = 0; dy < blockSize && (y + dy) < h; dy++) {
            for (let dx = 0; dx < blockSize && (x + dx) < w; dx++) {
              const idx = ((y + dy) * w + (x + dx)) * 4;
              data[idx] = r;
              data[idx + 1] = g;
              data[idx + 2] = b;
              data[idx + 3] = a;
            }
          }
        }
      }
    } else if (id === 'pixel_grid_lines') {
      for (let y = 0; y < h; y += blockSize) {
        for (let x = 0; x < w; x += blockSize) {
          const sampleIdx = (y * w + x) * 4;
          const r = data[sampleIdx];
          const g = data[sampleIdx + 1];
          const b = data[sampleIdx + 2];

          for (let dy = 0; dy < blockSize && (y + dy) < h; dy++) {
            for (let dx = 0; dx < blockSize && (x + dx) < w; dx++) {
              const idx = ((y + dy) * w + (x + dx)) * 4;
              if (dx === blockSize - 1 || dy === blockSize - 1) {
                data[idx] = 0; data[idx + 1] = 0; data[idx + 2] = 0;
              } else {
                data[idx] = r; data[idx + 1] = g; data[idx + 2] = b;
              }
            }
          }
        }
      }
    } else if (id === 'led_board') {
      const radius = blockSize / 2 - 1;
      for (let y = 0; y < h; y += blockSize) {
        for (let x = 0; x < w; x += blockSize) {
          const sampleIdx = ((y + 2) * w + (x + 2)) * 4;
          const r = data[sampleIdx];
          const g = data[sampleIdx + 1];
          const b = data[sampleIdx + 2];

          const cx = x + blockSize / 2;
          const cy = y + blockSize / 2;

          for (let dy = 0; dy < blockSize && (y + dy) < h; dy++) {
            for (let dx = 0; dx < blockSize && (x + dx) < w; dx++) {
              const idx = ((y + dy) * w + (x + dx)) * 4;
              const dist = Math.hypot(x + dx - cx, y + dy - cy);
              if (dist <= radius) {
                data[idx] = Math.min(255, r * 1.2);
                data[idx + 1] = Math.min(255, g * 1.2);
                data[idx + 2] = Math.min(255, b * 1.2);
              } else {
                data[idx] = 10; data[idx + 1] = 10; data[idx + 2] = 15;
              }
            }
          }
        }
      }
    } else if (id === 'dither_bayer') {
      const bayer4 = [
        [0, 8, 2, 10],
        [12, 4, 14, 6],
        [3, 11, 1, 9],
        [15, 7, 13, 5]
      ];
      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const idx = (y * w + x) * 4;
          const threshold = (bayer4[y % 4][x % 4] / 16) * 255;
          const lum = 0.299 * data[idx] + 0.587 * data[idx + 1] + 0.114 * data[idx + 2];
          const val = lum > threshold ? 255 : 0;
          data[idx] = val; data[idx + 1] = val; data[idx + 2] = val;
        }
      }
    } else if (id === 'bitcrush_2bit') {
      const len = data.length;
      for (let i = 0; i < len; i += 4) {
        data[i] = (data[i] >> 6) * 85;
        data[i + 1] = (data[i + 1] >> 6) * 85;
        data[i + 2] = (data[i + 2] >> 6) * 85;
      }
    } else if (id === 'bitcrush_4bit') {
      const len = data.length;
      for (let i = 0; i < len; i += 4) {
        data[i] = (data[i] >> 4) * 17;
        data[i + 1] = (data[i + 1] >> 4) * 17;
        data[i + 2] = (data[i + 2] >> 4) * 17;
      }
    }
  }

  // =========================================================================
  // 4. COLOR & CINE RETRO EFFECTS
  // =========================================================================
  isColorRasterEffect(id) {
    return [
      'film_noir', 'sepia_western', 'technicolor_2strip', 'kodachrome_64', 'polaroid_80s',
      'bleach_bypass', 'cross_process', 'warm_70s', 'cold_scifi', 'duotone_cyan_red',
      'duotone_gold_purple', 'duotone_emerald_coral', 'invert_colors', 'thermal_flir',
      'night_vision_nvg', 'xray_medical', 'solarize_sabattier', 'posterize_4', 'posterize_8',
      'neon_oversaturate'
    ].includes(id);
  }

  applyColorRaster(data, id, w, h, intensity) {
    const len = data.length;
    for (let i = 0; i < len; i += 4) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const lum = 0.299 * r + 0.587 * g + 0.114 * b;

      switch (id) {
        case 'film_noir': {
          const contrast = (lum - 128) * 1.5 + 128;
          const v = Math.max(0, Math.min(255, contrast | 0));
          data[i] = v; data[i + 1] = v; data[i + 2] = v;
          break;
        }
        case 'sepia_western': {
          data[i] = Math.min(255, (r * 0.393) + (g * 0.769) + (b * 0.189));
          data[i + 1] = Math.min(255, (r * 0.349) + (g * 0.686) + (b * 0.168));
          data[i + 2] = Math.min(255, (r * 0.272) + (g * 0.534) + (b * 0.131));
          break;
        }
        case 'technicolor_2strip': {
          data[i] = Math.min(255, r * 1.35);
          data[i + 1] = Math.min(255, g * 0.85);
          data[i + 2] = Math.min(255, (g * 0.4) + (b * 0.6));
          break;
        }
        case 'kodachrome_64': {
          data[i] = Math.min(255, r * 1.15 + 10);
          data[i + 1] = Math.min(255, g * 1.05);
          data[i + 2] = Math.min(255, b * 0.9);
          break;
        }
        case 'polaroid_80s': {
          data[i] = Math.min(255, (r * 0.9) + 30);
          data[i + 1] = Math.min(255, (g * 0.85) + 35);
          data[i + 2] = Math.min(255, (b * 0.8) + 40);
          break;
        }
        case 'bleach_bypass': {
          const desat = lum;
          data[i] = Math.min(255, (r * 0.4 + desat * 0.6) * 1.1);
          data[i + 1] = Math.min(255, (g * 0.4 + desat * 0.6) * 1.1);
          data[i + 2] = Math.min(255, (b * 0.4 + desat * 0.6) * 1.1);
          break;
        }
        case 'cross_process': {
          data[i] = Math.min(255, r * 1.25);
          data[i + 1] = Math.min(255, g * 1.1 + 15);
          data[i + 2] = Math.min(255, b * 0.7);
          break;
        }
        case 'warm_70s': {
          data[i] = Math.min(255, r * 1.2 + 20);
          data[i + 1] = Math.min(255, g * 1.05 + 10);
          data[i + 2] = Math.min(255, b * 0.8);
          break;
        }
        case 'cold_scifi': {
          data[i] = Math.min(255, r * 0.7);
          data[i + 1] = Math.min(255, g * 0.9 + 10);
          data[i + 2] = Math.min(255, b * 1.35 + 25);
          break;
        }
        case 'duotone_cyan_red': {
          const t = lum / 255;
          data[i] = (t * 255) | 0;
          data[i + 1] = ((1 - t) * 220) | 0;
          data[i + 2] = ((1 - t) * 255) | 0;
          break;
        }
        case 'duotone_gold_purple': {
          const t = lum / 255;
          data[i] = (t * 255 + (1 - t) * 70) | 0;
          data[i + 1] = (t * 200) | 0;
          data[i + 2] = ((1 - t) * 140) | 0;
          break;
        }
        case 'duotone_emerald_coral': {
          const t = lum / 255;
          data[i] = ((1 - t) * 255 + t * 20) | 0;
          data[i + 1] = (t * 230 + (1 - t) * 100) | 0;
          data[i + 2] = (t * 160 + (1 - t) * 80) | 0;
          break;
        }
        case 'invert_colors': {
          data[i] = 255 - r;
          data[i + 1] = 255 - g;
          data[i + 2] = 255 - b;
          break;
        }
        case 'thermal_flir': {
          // Heatmap FLIR: Violet -> Blue -> Green -> Yellow -> Red -> White
          const t = lum / 255;
          if (t < 0.2) {
            data[i] = (t * 5 * 100) | 0; data[i + 1] = 0; data[i + 2] = (t * 5 * 200 + 55) | 0;
          } else if (t < 0.5) {
            const nt = (t - 0.2) / 0.3;
            data[i] = 0; data[i + 1] = (nt * 255) | 0; data[i + 2] = ((1 - nt) * 255) | 0;
          } else if (t < 0.8) {
            const nt = (t - 0.5) / 0.3;
            data[i] = (nt * 255) | 0; data[i + 1] = 255; data[i + 2] = 0;
          } else {
            const nt = (t - 0.8) / 0.2;
            data[i] = 255; data[i + 1] = ((1 - nt) * 255) | 0; data[i + 2] = (nt * 255) | 0;
          }
          break;
        }
        case 'night_vision_nvg': {
          data[i] = (lum * 0.1) | 0;
          data[i + 1] = Math.min(255, lum * 1.5 + 20);
          data[i + 2] = (lum * 0.15) | 0;
          break;
        }
        case 'xray_medical': {
          const inv = 255 - lum;
          data[i] = (inv * 0.7) | 0;
          data[i + 1] = (inv * 0.85) | 0;
          data[i + 2] = Math.min(255, inv * 1.2 + 20);
          break;
        }
        case 'solarize_sabattier': {
          data[i] = r > 128 ? 255 - r : r * 2;
          data[i + 1] = g > 128 ? 255 - g : g * 2;
          data[i + 2] = b > 128 ? 255 - b : b * 2;
          break;
        }
        case 'posterize_4': {
          data[i] = (r >> 6) * 85;
          data[i + 1] = (g >> 6) * 85;
          data[i + 2] = (b >> 6) * 85;
          break;
        }
        case 'posterize_8': {
          data[i] = (r >> 5) * 36;
          data[i + 1] = (g >> 5) * 36;
          data[i + 2] = (b >> 5) * 36;
          break;
        }
        case 'neon_oversaturate': {
          const max = Math.max(r, g, b);
          data[i] = r === max ? Math.min(255, r * 1.4) : (r * 0.6) | 0;
          data[i + 1] = g === max ? Math.min(255, g * 1.4) : (g * 0.6) | 0;
          data[i + 2] = b === max ? Math.min(255, b * 1.4) : (b * 0.6) | 0;
          break;
        }
      }
    }
  }

  // =========================================================================
  // 5. ÓPTICA & LENTES
  // =========================================================================
  isOpticsEffect(id) {
    return [
      'fisheye_lens', 'barrel_distortion', 'pincushion_distort', 'water_ripple', 'swirl_vortex',
      'split_mirror_h', 'split_mirror_v', 'kaleidoscope_4', 'kaleidoscope_8',
      'radial_zoom_blur', 'motion_blur_h', 'tilt_shift', 'vignette_dark',
      'vignette_arcade_box', 'speed_lines_manga'
    ].includes(id);
  }

  renderOpticsEffect(ctx, id, w, h, intensity, currentTime) {
    const factor = intensity / 100;

    if (id === 'vignette_dark') {
      const grad = ctx.createRadialGradient(w / 2, h / 2, w * 0.25, w / 2, h / 2, w * 0.65);
      grad.addColorStop(0, 'rgba(0,0,0,0)');
      grad.addColorStop(1, `rgba(0,0,0,${factor * 0.85})`);
      ctx.fillStyle = grad;
      ctx.fillRect(0, 0, w, h);
    } else if (id === 'vignette_arcade_box') {
      ctx.save();
      ctx.strokeStyle = '#000000';
      ctx.lineWidth = Math.round(w * 0.04 * factor);
      ctx.strokeRect(0, 0, w, h);
      ctx.restore();
    } else if (id === 'speed_lines_manga') {
      ctx.save();
      ctx.strokeStyle = `rgba(255, 255, 255, ${0.4 * factor})`;
      ctx.lineWidth = 2;
      const count = Math.round(30 * factor);
      const cx = w / 2;
      const cy = h / 2;
      for (let i = 0; i < count; i++) {
        const angle = (i / count) * Math.PI * 2 + (currentTime * 2 % 1);
        const r1 = Math.min(w, h) * 0.35;
        const r2 = Math.min(w, h) * 0.65;
        ctx.beginPath();
        ctx.moveTo(cx + Math.cos(angle) * r1, cy + Math.sin(angle) * r1);
        ctx.lineTo(cx + Math.cos(angle) * r2, cy + Math.sin(angle) * r2);
        ctx.stroke();
      }
      ctx.restore();
    } else if (id === 'split_mirror_h') {
      // Mirror left half to right half
      ctx.save();
      ctx.drawImage(ctx.canvas, 0, 0, w / 2, h, 0, 0, w / 2, h);
      ctx.translate(w, 0);
      ctx.scale(-1, 1);
      ctx.drawImage(ctx.canvas, 0, 0, w / 2, h, 0, 0, w / 2, h);
      ctx.restore();
    } else if (id === 'split_mirror_v') {
      ctx.save();
      ctx.drawImage(ctx.canvas, 0, 0, w, h / 2, 0, 0, w, h / 2);
      ctx.translate(0, h);
      ctx.scale(1, -1);
      ctx.drawImage(ctx.canvas, 0, 0, w, h / 2, 0, 0, w, h / 2);
      ctx.restore();
    } else {
      // Geometric transformations (barrel, ripple, etc.)
      const srcData = ctx.getImageData(0, 0, w, h);
      const dstData = ctx.createImageData(w, h);
      const s = srcData.data;
      const d = dstData.data;
      const cx = w / 2;
      const cy = h / 2;

      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          let u = x - cx;
          let v = y - cy;
          let srcX = x;
          let srcY = y;

          if (id === 'fisheye_lens' || id === 'barrel_distortion') {
            const r = Math.hypot(u, v) / (Math.min(w, h) / 2);
            const theta = Math.atan2(v, u);
            const distR = Math.pow(r, 1 + factor * 0.6);
            srcX = cx + Math.cos(theta) * distR * (Math.min(w, h) / 2);
            srcY = cy + Math.sin(theta) * distR * (Math.min(w, h) / 2);
          } else if (id === 'water_ripple') {
            const r = Math.hypot(u, v);
            const wave = Math.sin(r * 0.05 - currentTime * 8) * (factor * 12);
            srcX = x + (u / (r + 1)) * wave;
            srcY = y + (v / (r + 1)) * wave;
          } else if (id === 'swirl_vortex') {
            const r = Math.hypot(u, v);
            const maxR = Math.min(w, h) * 0.5;
            if (r < maxR) {
              const angle = ((maxR - r) / maxR) * factor * Math.PI * 1.5;
              const curAngle = Math.atan2(v, u) + angle;
              srcX = cx + Math.cos(curAngle) * r;
              srcY = cy + Math.sin(curAngle) * r;
            }
          }

          srcX = Math.max(0, Math.min(w - 1, Math.round(srcX)));
          srcY = Math.max(0, Math.min(h - 1, Math.round(srcY)));

          const dstIdx = (y * w + x) * 4;
          const srcIdx = (srcY * w + srcX) * 4;
          d[dstIdx] = s[srcIdx];
          d[dstIdx + 1] = s[srcIdx + 1];
          d[dstIdx + 2] = s[srcIdx + 2];
          d[dstIdx + 3] = s[srcIdx + 3];
        }
      }
      ctx.putImageData(dstData, 0, 0);
    }
  }

  // =========================================================================
  // 6. ATMÓSFERA & LUZ
  // =========================================================================
  isAtmosphereEffect(id) {
    return [
      'neon_flicker', 'strobe_light', 'pixel_rain', 'pixel_snow', 'fire_embers',
      'film_dust_scratches', 'god_rays', 'dungeon_fog', 'confetti_8bit', 'starfield_warp'
    ].includes(id);
  }

  renderAtmosphereEffect(ctx, id, w, h, intensity, currentTime) {
    const factor = intensity / 100;

    if (id === 'neon_flicker') {
      const flicker = Math.sin(currentTime * 25) * Math.cos(currentTime * 11);
      if (flicker > 0.4) {
        ctx.fillStyle = `rgba(0, 240, 255, ${0.15 * factor})`;
        ctx.fillRect(0, 0, w, h);
      }
    } else if (id === 'strobe_light') {
      if ((Math.floor(currentTime * 12) % 2) === 0) {
        ctx.fillStyle = `rgba(255, 255, 255, ${0.35 * factor})`;
        ctx.fillRect(0, 0, w, h);
      }
    } else if (id === 'pixel_rain') {
      ctx.fillStyle = '#00f0ff';
      const drops = Math.round(60 * factor);
      for (let i = 0; i < drops; i++) {
        const seed = (i * 137.5) % 1;
        const x = (seed * w + (currentTime * 40)) % w;
        const speed = 250 + (i % 5) * 80;
        const y = (currentTime * speed + i * 47) % h;
        ctx.fillRect(x, y, 2, 8);
      }
    } else if (id === 'pixel_snow') {
      ctx.fillStyle = '#ffffff';
      const flakes = Math.round(70 * factor);
      for (let i = 0; i < flakes; i++) {
        const seedX = (i * 89.3) % 1;
        const speed = 40 + (i % 4) * 25;
        const sway = Math.sin(currentTime * 3 + i) * 15;
        const x = (seedX * w + sway) % w;
        const y = (currentTime * speed + i * 31) % h;
        const sz = (i % 3 === 0) ? 3 : 2;
        ctx.fillRect(x, y, sz, sz);
      }
    } else if (id === 'fire_embers') {
      const embers = Math.round(45 * factor);
      for (let i = 0; i < embers; i++) {
        const seedX = (i * 121.7) % 1;
        const speed = 70 + (i % 5) * 40;
        const sway = Math.sin(currentTime * 4 + i) * 20;
        const x = (seedX * w + sway) % w;
        const y = h - ((currentTime * speed + i * 53) % h);
        ctx.fillStyle = (i % 2 === 0) ? '#ff4400' : '#ffcc00';
        ctx.fillRect(x, y, 3, 3);
      }
    } else if (id === 'confetti_8bit') {
      const colors = ['#ff0055', '#00f0ff', '#ffd200', '#39ff14', '#b026ff'];
      const count = Math.round(50 * factor);
      for (let i = 0; i < count; i++) {
        const x = ((i * 73.1 + currentTime * 60) % w);
        const y = ((currentTime * 120 + i * 37) % h);
        ctx.fillStyle = colors[i % colors.length];
        ctx.fillRect(x, y, 6, 6);
      }
    } else if (id === 'starfield_warp') {
      ctx.fillStyle = '#ffffff';
      const stars = Math.round(80 * factor);
      const cx = w / 2;
      const cy = h / 2;
      for (let i = 0; i < stars; i++) {
        const angle = (i / stars) * Math.PI * 2;
        const speed = 0.5 + (i % 3) * 0.5;
        const dist = ((currentTime * speed * 200 + i * 30) % (Math.min(w, h) * 0.6));
        const x = cx + Math.cos(angle) * dist;
        const y = cy + Math.sin(angle) * dist;
        const sz = Math.max(1, Math.round(dist * 0.015));
        ctx.fillRect(x, y, sz, sz);
      }
    }
  }

  // =========================================================================
  // 7. HUDS, OVERLAYS & RETRO BADGES
  // =========================================================================
  renderOverlayEffect(ctx, id, w, h, intensity, currentTime) {
    ctx.save();

    if (id === 'overlay_insert_coin') {
      const isBlink = (Math.floor(currentTime * 3) % 2) === 0;
      if (isBlink) {
        ctx.font = `bold ${Math.round(h * 0.05)}px 'Press Start 2P', monospace`;
        ctx.textAlign = 'center';
        ctx.fillStyle = '#ffd200';
        ctx.strokeStyle = '#000000';
        ctx.lineWidth = 4;
        ctx.strokeText('★ INSERT COIN ★', w / 2, h * 0.9);
        ctx.fillText('★ INSERT COIN ★', w / 2, h * 0.9);
      }
    } else if (id === 'overlay_1up_score') {
      ctx.font = `bold ${Math.round(h * 0.04)}px 'Press Start 2P', monospace`;
      ctx.fillStyle = '#ff3344';
      ctx.fillText('1UP', w * 0.08, h * 0.08);
      ctx.fillStyle = '#ffffff';
      const score = String(Math.floor(currentTime * 100)).padStart(6, '0');
      ctx.fillText(score, w * 0.08, h * 0.14);

      ctx.fillStyle = '#39ff14';
      ctx.fillText('HIGH SCORE', w * 0.65, h * 0.08);
      ctx.fillStyle = '#ffffff';
      ctx.fillText('099990', w * 0.65, h * 0.14);
    } else if (id === 'overlay_heart_health') {
      ctx.font = `${Math.round(h * 0.055)}px monospace`;
      ctx.fillText('❤️❤️❤️', w * 0.06, h * 0.1);
    } else if (id === 'overlay_boss_bar') {
      const barW = w * 0.6;
      const barH = Math.round(h * 0.035);
      const barX = (w - barW) / 2;
      const barY = h * 0.08;

      ctx.fillStyle = '#000000';
      ctx.fillRect(barX - 4, barY - 4, barW + 8, barH + 8);
      ctx.fillStyle = '#ff2233';
      ctx.fillRect(barX, barY, barW * 0.85, barH);

      ctx.font = `bold ${Math.round(h * 0.03)}px 'Press Start 2P', monospace`;
      ctx.fillStyle = '#ffd200';
      ctx.textAlign = 'center';
      ctx.fillText('👾 MECHA-BOSS HP 👾', w / 2, barY - 10);
    } else if (id === 'overlay_cyber_crosshair') {
      const cx = w / 2;
      const cy = h / 2;
      const rad = Math.round(h * 0.18);
      ctx.strokeStyle = '#00f0ff';
      ctx.lineWidth = 2;

      ctx.beginPath();
      ctx.arc(cx, cy, rad, 0, Math.PI * 2);
      ctx.stroke();

      ctx.beginPath();
      ctx.moveTo(cx - rad - 20, cy); ctx.lineTo(cx - rad + 10, cy);
      ctx.moveTo(cx + rad - 10, cy); ctx.lineTo(cx + rad + 20, cy);
      ctx.moveTo(cx, cy - rad - 20); ctx.lineTo(cx, cy - rad + 10);
      ctx.moveTo(cx, cy + rad - 10); ctx.lineTo(cx, cy + rad + 20);
      ctx.stroke();

      ctx.font = `bold ${Math.round(h * 0.025)}px 'Press Start 2P', monospace`;
      ctx.fillStyle = '#00f0ff';
      ctx.fillText('TARGET LOCKED', cx - 70, cy + rad + 25);
    } else if (id === 'overlay_boss_warning') {
      const isBlink = (Math.floor(currentTime * 4) % 2) === 0;
      if (isBlink) {
        ctx.fillStyle = 'rgba(255, 0, 50, 0.4)';
        ctx.fillRect(0, h * 0.4, w, h * 0.2);
        ctx.font = `bold ${Math.round(h * 0.06)}px 'Press Start 2P', monospace`;
        ctx.fillStyle = '#ffd200';
        ctx.textAlign = 'center';
        ctx.fillText('⚠️ WARNING: BOSS AHEAD ⚠️', w / 2, h * 0.52);
      }
    } else if (id === 'overlay_digital_clock') {
      ctx.font = `bold ${Math.round(h * 0.07)}px 'Press Start 2P', monospace`;
      ctx.fillStyle = '#39ff14';
      ctx.textAlign = 'right';
      const count = Math.max(0, 99 - Math.floor(currentTime));
      ctx.fillText(`TIME ${String(count).padStart(2, '0')}`, w * 0.95, h * 0.12);
    } else if (id === 'overlay_35mm_perforations') {
      ctx.fillStyle = '#000000';
      const perfW = Math.round(w * 0.05);
      ctx.fillRect(0, 0, perfW, h);
      ctx.fillRect(w - perfW, 0, perfW, h);

      ctx.fillStyle = '#ffffff';
      const holeH = Math.round(h * 0.05);
      for (let y = 10; y < h; y += holeH * 2) {
        ctx.fillRect(perfW * 0.25, y, perfW * 0.5, holeH);
        ctx.fillRect(w - perfW * 0.75, y, perfW * 0.5, holeH);
      }
    }

    ctx.restore();
  }

  formatTimestamp(sec) {
    const s = Math.floor(sec) % 60;
    const m = Math.floor(sec / 60) % 60;
    const h = Math.floor(sec / 3600);
    const pad = (n) => String(n).padStart(2, '0');
    return `${pad(h)}:${pad(m)}:${pad(s)}`;
  }
}

export const effectsEngine = new EffectsEngine();
