/**
 * KIDCUT ZERO-DEPENDENCY GIF89A ENCODER
 * High-speed palette quantizer + LZW compressed animated GIF generator.
 * Perfectly suited for 8-bit retro video clips without requiring heavy external binaries.
 */

export class GifEncoder {
  constructor(width, height) {
    this.width = width;
    this.height = height;
    this.frames = [];
    this.delay = 10; // in 100ths of a second (10 = 100ms = 10fps)
  }

  setDelay(delayMs) {
    this.delay = Math.round(delayMs / 10);
  }

  addFrame(ctx) {
    const imgData = ctx.getImageData(0, 0, this.width, this.height);
    this.frames.push(imgData.data);
  }

  encode() {
    const bytes = [];
    const push = (b) => bytes.push(b & 0xff);
    const pushShort = (v) => { push(v & 0xff); push((v >> 8) & 0xff); };
    const pushString = (s) => { for (let i = 0; i < s.length; i++) push(s.charCodeAt(i)); };

    // 1. Header: GIF89a
    pushString('GIF89a');

    // 2. Logical Screen Descriptor
    pushShort(this.width);
    pushShort(this.height);
    // Packed field: Global Color Table Flag (1), 7 (256 colors), Sort (0), 7 (256 colors)
    push(0xf7);
    push(0); // Background Color Index
    push(0); // Pixel Aspect Ratio

    // 3. Global Color Table (Standard 256 Color Web/Arcade Palette)
    const palette = this.generatePalette();
    for (let i = 0; i < 256; i++) {
      push(palette[i * 3]);
      push(palette[i * 3 + 1]);
      push(palette[i * 3 + 2]);
    }

    // 4. Netscape 2.0 Application Extension (Looping animation)
    push(0x21); // Extension Introducer
    push(0xff); // Application Extension Label
    push(11);   // Block Size
    pushString('NETSCAPE2.0');
    push(3);    // Sub-block size
    push(1);    // Loop sub-block ID
    pushShort(0); // Loop count (0 = infinite)
    push(0);    // Block Terminator

    // 5. Add Frames
    for (const frameData of this.frames) {
      // Graphics Control Extension
      push(0x21); // Extension Introducer
      push(0xf9); // Graphic Control Label
      push(4);    // Block Size
      push(0x04); // Packed field: Disposal method (01 = do not dispose, 00 = none)
      pushShort(this.delay); // Delay time in 1/100 sec
      push(0);    // Transparent color index
      push(0);    // Block Terminator

      // Image Descriptor
      push(0x2c); // Image Separator ','
      pushShort(0); // Left Position
      pushShort(0); // Top Position
      pushShort(this.width);
      pushShort(this.height);
      push(0); // Packed: No local color table

      // LZW Raster Data
      this.writeLZW(frameData, palette, push, pushShort);
    }

    // 6. Trailer
    push(0x3b); // Trailer ';'

    return new Uint8Array(bytes);
  }

  generatePalette() {
    // 6x6x6 color cube (216 colors) + 40 shades of grays and arcade retro colors
    const pal = new Uint8Array(256 * 3);
    let idx = 0;

    // 6x6x6 Color Cube
    for (let r = 0; r < 6; r++) {
      for (let g = 0; g < 6; g++) {
        for (let b = 0; b < 6; b++) {
          pal[idx++] = Math.round((r / 5) * 255);
          pal[idx++] = Math.round((g / 5) * 255);
          pal[idx++] = Math.round((b / 5) * 255);
        }
      }
    }

    // 40 Grayscales and retro arcade accents
    for (let i = 0; i < 40; i++) {
      const v = Math.round((i / 39) * 255);
      pal[idx++] = v;
      pal[idx++] = v;
      pal[idx++] = v;
    }

    return pal;
  }

  writeLZW(pixels, palette, push, pushShort) {
    const minCodeSize = 8;
    push(minCodeSize);

    const clearCode = 1 << minCodeSize;
    const endCode = clearCode + 1;

    let curCodeSize = minCodeSize + 1;
    let maxCode = 1 << curCodeSize;

    // Quantize pixels to palette indices
    const totalPixels = this.width * this.height;
    const indexed = new Uint8Array(totalPixels);
    for (let i = 0; i < totalPixels; i++) {
      const r = pixels[i * 4];
      const g = pixels[i * 4 + 1];
      const b = pixels[i * 4 + 2];
      // Fast color cube mapping: 36 * r + 6 * g + b
      const rIdx = Math.min(5, Math.floor((r / 256) * 6));
      const gIdx = Math.min(5, Math.floor((g / 256) * 6));
      const bIdx = Math.min(5, Math.floor((b / 256) * 6));
      indexed[i] = rIdx * 36 + gIdx * 6 + bIdx;
    }

    // Pack bits into sub-blocks
    const subBlock = [];
    let accumulator = 0;
    let bitsInAccumulator = 0;

    const emitBits = (code, codeSize) => {
      accumulator |= (code << bitsInAccumulator);
      bitsInAccumulator += codeSize;
      while (bitsInAccumulator >= 8) {
        subBlock.push(accumulator & 0xff);
        accumulator >>= 8;
        bitsInAccumulator -= 8;
        if (subBlock.length === 254) {
          push(subBlock.length);
          for (const b of subBlock) push(b);
          subBlock.length = 0;
        }
      }
    };

    emitBits(clearCode, curCodeSize);

    // Simple LZW dictionary
    const dict = new Map();
    let nextCode = endCode + 1;
    let prefix = indexed[0];

    for (let i = 1; i < totalPixels; i++) {
      const k = indexed[i];
      const key = (prefix << 8) | k;

      if (dict.has(key)) {
        prefix = dict.get(key);
      } else {
        emitBits(prefix, curCodeSize);
        if (nextCode < 4096) {
          dict.set(key, nextCode++);
          if (nextCode > maxCode && curCodeSize < 12) {
            curCodeSize++;
            maxCode = 1 << curCodeSize;
          }
        } else {
          // Clear dictionary when full
          emitBits(clearCode, curCodeSize);
          dict.clear();
          curCodeSize = minCodeSize + 1;
          maxCode = 1 << curCodeSize;
          nextCode = endCode + 1;
        }
        prefix = k;
      }
    }

    emitBits(prefix, curCodeSize);
    emitBits(endCode, curCodeSize);

    // Flush remaining bits
    if (bitsInAccumulator > 0) {
      subBlock.push(accumulator & 0xff);
    }
    if (subBlock.length > 0) {
      push(subBlock.length);
      for (const b of subBlock) push(b);
    }

    // Block Terminator
    push(0);
  }
}
