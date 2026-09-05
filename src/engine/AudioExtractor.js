/**
 * KIDCUT AUDIO EXTRACTOR
 * High-performance audio extraction and preservation for video clips (MP4, WebM, MOV, etc.)
 * and audio files (MP3, WAV, OGG, AAC) into native Web Audio API AudioBuffers.
 */

import { audioEngine } from './AudioEngine.js';

export class AudioExtractor {
  /**
   * Extracts audio from a File or Blob URL into a native AudioBuffer
   * @param {File} file 
   * @param {string} blobUrl 
   * @returns {Promise<AudioBuffer|null>}
   */
  static async extractAudio(file, blobUrl) {
    audioEngine.init();
    const ctx = audioEngine.ctx;

    // 1. Audio files (MP3, WAV, OGG, AAC, FLAC, M4A)
    const isAudioFile = file.type && (file.type.startsWith('audio/') || /\.(mp3|wav|ogg|aac|m4a|flac)$/i.test(file.name));
    if (isAudioFile) {
      try {
        const buffer = await file.arrayBuffer();
        const audioBuffer = await ctx.decodeAudioData(buffer.slice(0));
        if (audioBuffer && audioBuffer.duration > 0) {
          return audioBuffer;
        }
      } catch (err) {
        console.warn('Fallo decodificación directa de archivo de audio:', err);
      }
    }

    // 2. Video files (MP4, WebM, MOV, MKV)
    let arrayBuffer = null;
    try {
      arrayBuffer = await file.arrayBuffer();
    } catch (e) {
      console.warn('No se pudo leer arrayBuffer del archivo:', e);
    }

    if (arrayBuffer) {
      // Strategy 2A: Direct decode (works for some WebM and audio-centric containers)
      try {
        const audioBuffer = await ctx.decodeAudioData(arrayBuffer.slice(0));
        if (audioBuffer && audioBuffer.duration > 0) {
          return audioBuffer;
        }
      } catch (e) {
        // Expected in Chromium for video containers containing video tracks
      }

      // Strategy 2B: High-speed ISO BMFF / MP4 Demuxer (removes video track from moov)
      try {
        const audioOnlyMp4 = createAudioOnlyMP4(arrayBuffer);
        if (audioOnlyMp4) {
          const audioBuffer = await ctx.decodeAudioData(audioOnlyMp4);
          if (audioBuffer && audioBuffer.duration > 0) {
            return audioBuffer;
          }
        }
      } catch (err) {
        console.warn('Fallo extracción demuxer MP4:', err);
      }
    }

    // Strategy 2C: Universal MediaElement recording capture (WebM, MOV, or exotic codecs)
    if (blobUrl) {
      try {
        const audioBuffer = await extractViaMediaElement(blobUrl, ctx);
        if (audioBuffer && audioBuffer.duration > 0) {
          return audioBuffer;
        }
      } catch (err) {
        console.warn('Fallo captura por MediaElement:', err);
      }
    }

    return null;
  }
}

/**
 * Fast in-memory MP4 / ISO BMFF container parser.
 * Removes all video ('vide') trak boxes from 'moov', leaving only sound ('soun') trak boxes.
 * When passed to Web Audio decodeAudioData, Chromium recognizes it as an audio/mp4 and decodes it instantly.
 */
function createAudioOnlyMP4(arrayBuffer) {
  const view = new DataView(arrayBuffer);
  let offset = 0;
  let moovBox = null;

  while (offset + 8 <= arrayBuffer.byteLength) {
    let size = view.getUint32(offset);
    const type = String.fromCharCode(
      view.getUint8(offset + 4),
      view.getUint8(offset + 5),
      view.getUint8(offset + 6),
      view.getUint8(offset + 7)
    );
    if (size === 1 && offset + 16 <= arrayBuffer.byteLength) {
      size = Number(view.getBigUint64(offset + 8));
    }
    if (size < 8 || offset + size > arrayBuffer.byteLength) {
      if (size === 0) size = arrayBuffer.byteLength - offset;
      else break;
    }

    if (type === 'moov') {
      moovBox = { type, offset, size };
      break;
    }
    offset += size;
  }

  if (!moovBox) return null;

  // Scan inside moov for trak boxes
  let moovOffset = moovBox.offset + 8;
  const moovEnd = moovBox.offset + moovBox.size;
  const excludedTrakOffsets = [];
  let foundSoundTrack = false;

  while (moovOffset + 8 <= moovEnd) {
    const subSize = view.getUint32(moovOffset);
    if (subSize < 8 || moovOffset + subSize > moovEnd) break;
    const subType = String.fromCharCode(
      view.getUint8(moovOffset + 4),
      view.getUint8(moovOffset + 5),
      view.getUint8(moovOffset + 6),
      view.getUint8(moovOffset + 7)
    );

    if (subType === 'trak') {
      let isVideo = false;
      let trakScan = moovOffset + 8;
      const trakEnd = moovOffset + subSize;

      while (trakScan + 8 <= trakEnd) {
        const tSize = view.getUint32(trakScan);
        if (tSize < 8 || trakScan + tSize > trakEnd) break;
        const tType = String.fromCharCode(
          view.getUint8(trakScan + 4),
          view.getUint8(trakScan + 5),
          view.getUint8(trakScan + 6),
          view.getUint8(trakScan + 7)
        );

        if (tType === 'mdia') {
          let mdiaScan = trakScan + 8;
          const mdiaEnd = trakScan + tSize;
          while (mdiaScan + 8 <= mdiaEnd) {
            const mSize = view.getUint32(mdiaScan);
            if (mSize < 8 || mdiaScan + mSize > mdiaEnd) break;
            const mType = String.fromCharCode(
              view.getUint8(mdiaScan + 4),
              view.getUint8(mdiaScan + 5),
              view.getUint8(mdiaScan + 6),
              view.getUint8(mdiaScan + 7)
            );
            if (mType === 'hdlr') {
              if (mdiaScan + 20 <= mdiaEnd) {
                const hdlrType = String.fromCharCode(
                  view.getUint8(mdiaScan + 16),
                  view.getUint8(mdiaScan + 17),
                  view.getUint8(mdiaScan + 18),
                  view.getUint8(mdiaScan + 19)
                );
                if (hdlrType === 'vide') isVideo = true;
                if (hdlrType === 'soun') foundSoundTrack = true;
              }
            }
            mdiaScan += mSize;
          }
        }
        trakScan += tSize;
      }

      if (isVideo) {
        excludedTrakOffsets.push({ offset: moovOffset, size: subSize });
      }
    }

    moovOffset += subSize;
  }

  if (excludedTrakOffsets.length === 0 || !foundSoundTrack) {
    return null;
  }

  const totalRemoved = excludedTrakOffsets.reduce((sum, item) => sum + item.size, 0);
  const newLength = arrayBuffer.byteLength - totalRemoved;
  const newBuffer = new Uint8Array(newLength);
  const src = new Uint8Array(arrayBuffer);

  let writePos = 0;
  let readPos = 0;

  // 1. Copy up to moov
  newBuffer.set(src.subarray(readPos, moovBox.offset), writePos);
  writePos += (moovBox.offset - readPos);
  readPos = moovBox.offset;

  // 2. Write new moov box header with adjusted size
  const newMoovSize = moovBox.size - totalRemoved;
  const moovHeader = new Uint8Array(8);
  const moovHeaderView = new DataView(moovHeader.buffer);
  moovHeaderView.setUint32(0, newMoovSize);
  moovHeader.set([0x6d, 0x6f, 0x6f, 0x76], 4);
  newBuffer.set(moovHeader, writePos);
  writePos += 8;
  readPos = moovBox.offset + 8;

  // 3. Copy sub-boxes in moov, skipping excluded traks
  for (const box of excludedTrakOffsets) {
    const len = box.offset - readPos;
    if (len > 0) {
      newBuffer.set(src.subarray(readPos, box.offset), writePos);
      writePos += len;
    }
    readPos = box.offset + box.size;
  }

  const remainingInMoov = moovEnd - readPos;
  if (remainingInMoov > 0) {
    newBuffer.set(src.subarray(readPos, moovEnd), writePos);
    writePos += remainingInMoov;
    readPos = moovEnd;
  }

  // 4. Copy remaining file (including mdat)
  if (readPos < arrayBuffer.byteLength) {
    newBuffer.set(src.subarray(readPos), writePos);
  }

  return newBuffer.buffer;
}

/**
 * Universal fallback using HTMLVideoElement and MediaStream / MediaRecorder
 */
function extractViaMediaElement(blobUrl, audioCtx) {
  return new Promise((resolve) => {
    const video = document.createElement('video');
    video.src = blobUrl;
    video.muted = false;
    video.volume = 1.0;
    video.preload = 'auto';

    let isDone = false;
    const cleanup = () => {
      if (isDone) return;
      isDone = true;
      try {
        video.pause();
        video.removeAttribute('src');
        video.load();
      } catch (e) {}
    };

    video.onloadedmetadata = async () => {
      const duration = video.duration;
      if (!duration || isNaN(duration) || duration <= 0) {
        cleanup();
        return resolve(null);
      }

      const stream = video.captureStream ? video.captureStream() : (video.mozCaptureStream ? video.mozCaptureStream() : null);
      if (!stream || stream.getAudioTracks().length === 0) {
        cleanup();
        return resolve(null);
      }

      try {
        const audioStream = new MediaStream(stream.getAudioTracks());
        let mime = 'audio/webm';
        if (!MediaRecorder.isTypeSupported(mime)) mime = '';

        const recorder = new MediaRecorder(audioStream, mime ? { mimeType: mime } : undefined);
        const chunks = [];

        recorder.ondataavailable = (e) => {
          if (e.data && e.data.size > 0) chunks.push(e.data);
        };

        recorder.onstop = async () => {
          cleanup();
          try {
            const blob = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' });
            const ab = await blob.arrayBuffer();
            const buffer = await audioCtx.decodeAudioData(ab);
            resolve(buffer);
          } catch (err) {
            resolve(null);
          }
        };

        recorder.start();
        video.playbackRate = 4.0;
        video.play().catch(() => {});

        video.onended = () => {
          if (recorder.state === 'recording') recorder.stop();
        };

        const maxWaitMs = Math.min(12000, (duration / 4.0) * 1000 + 800);
        setTimeout(() => {
          if (recorder.state === 'recording') recorder.stop();
        }, maxWaitMs);

      } catch (e) {
        cleanup();
        resolve(null);
      }
    };

    video.onerror = () => {
      cleanup();
      resolve(null);
    };

    // Safety timeout
    setTimeout(() => {
      cleanup();
      resolve(null);
    }, 15000);
  });
}
