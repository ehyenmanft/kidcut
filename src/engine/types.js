/**
 * KIDCUT ENGINE TYPES & CONSTANTS
 */

export const AspectRatios = {
  '16:9': { width: 1920, height: 1080, ratio: 16 / 9, label: '16:9 (Horizontal/YT)' },
  '9:16': { width: 1080, height: 1920, ratio: 9 / 16, label: '9:16 (Vertical/TikTok/Reels)' },
  '1:1': { width: 1080, height: 1080, ratio: 1, label: '1:1 (Square/Instagram)' },
  '4:5': { width: 1080, height: 1350, ratio: 4 / 5, label: '4:5 (Feed/Portrait)' }
};

export const Resolutions = {
  '480': { scale: 0.444, name: '480p SD' },
  '720': { scale: 0.667, name: '720p HD' },
  '1080': { scale: 1.0, name: '1080p FHD' },
  '4k': { scale: 2.0, name: '4K UHD' }
};

export const ClipType = {
  VIDEO: 'video',
  AUDIO: 'audio',
  IMAGE: 'image',
  TEXT: 'text',
  STICKER: 'sticker',
  COLOR: 'color'
};

export const TrackType = {
  VIDEO: 'video', // for video, image, text, stickers
  AUDIO: 'audio'  // for music, voice, SFX
};

export const RetroFilters = {
  NONE: 'none',
  PIXELATE: 'pixelate',
  CRT: 'crt',
  GAMEBOY: 'gameboy',
  GLITCH: 'glitch',
  SEPIA: 'sepia',
  INVERT: 'invert',
  MONOCHROME: 'monochrome'
};
