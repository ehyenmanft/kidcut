/**
 * KIDCUT 100 RETRO & CREATIVE EFFECTS DATABASE
 * Comprehensive catalog of 100 curated pixel, CRT, color, optical, atmospheric & HUD effects.
 */

export const EFFECT_CATEGORIES = [
  { id: 'all', name: 'TODOS (100)', icon: '⭐' },
  { id: 'palettes', name: 'PALETAS RETRO (15)', icon: '🕹️' },
  { id: 'crt', name: 'CRT & PANTALLAS (15)', icon: '📺' },
  { id: 'pixel', name: 'PIXEL & MOSAICO (15)', icon: '👾' },
  { id: 'color', name: 'COLOR & CINE (20)', icon: '🎨' },
  { id: 'optics', name: 'ÓPTICA & LENTES (15)', icon: '🔍' },
  { id: 'atmosphere', name: 'ATMÓSFERA & LUZ (10)', icon: '✨' },
  { id: 'huds', name: 'HUDS & OVERLAYS (10)', icon: '🎮' }
];

export const ALL_EFFECTS = [
  // ==========================================
  // 1. PALETAS RETRO & CONSOLAS CLÁSICAS (15)
  // ==========================================
  { id: 'gameboy', name: 'Game Boy Original', category: 'palettes', icon: '🟢', desc: 'Paleta verdosa clásica LCD 4 tonos', hasIntensity: false },
  { id: 'gameboy_pocket', name: 'Game Boy Pocket', category: 'palettes', icon: '🔘', desc: 'Monocromo plata y gris de matriz pasiva', hasIntensity: false },
  { id: 'nes_famicom', name: 'NES / Famicom', category: 'palettes', icon: '🔴', desc: 'Paleta arcade de 54 colores 8-bits', hasIntensity: false },
  { id: 'snes_16bit', name: 'Super Nintendo 16-Bit', category: 'palettes', icon: '🟣', desc: 'Tonos vibrantes y ricos de 16-bits', hasIntensity: false },
  { id: 'genesis_megadrive', name: 'Sega Genesis / Mega Drive', category: 'palettes', icon: '🔵', desc: 'Alto contraste metálico de 64 colores', hasIntensity: false },
  { id: 'c64_commodore', name: 'Commodore 64', category: 'palettes', icon: '🟤', desc: 'Paleta analógica VIC-II de 16 colores cálidos', hasIntensity: false },
  { id: 'zx_spectrum', name: 'ZX Spectrum', category: 'palettes', icon: '🟡', desc: 'Colores saturados primarios sin gradientes', hasIntensity: false },
  { id: 'cga_mode1', name: 'CGA Modo 1 (Cian/Magenta)', category: 'palettes', icon: '🦩', desc: 'El legendario Cian, Magenta, Blanco y Negro', hasIntensity: false },
  { id: 'cga_mode2', name: 'CGA Modo 2 (Rojo/Verde)', category: 'palettes', icon: '🚦', desc: 'Paleta alternativa CGA Rojo, Verde y Amarillo', hasIntensity: false },
  { id: 'ega_retro', name: 'EGA PC Retro', category: 'palettes', icon: '🖥️', desc: 'Paleta gráfica clásica de PC de los años 80', hasIntensity: false },
  { id: 'atari_2600', name: 'Atari 2600 TIA', category: 'palettes', icon: '🕹️', desc: 'Paleta icónica de la primera era de videojuegos', hasIntensity: false },
  { id: 'cyberpunk_neon', name: 'Cyberpunk Neón', category: 'palettes', icon: '🌃', desc: 'Fucsia neón, cian eléctrico y sombras oscuras', hasIntensity: false },
  { id: 'vaporwave_sunset', name: 'Vaporwave 1995', category: 'palettes', icon: '🌴', desc: 'Estética synthwave magenta, lavanda y turquesa', hasIntensity: false },
  { id: 'matrix_green', name: 'Matrix Terminal P1', category: 'palettes', icon: '🟩', desc: 'Fósforo verde de terminal hacker sobre negro', hasIntensity: false },
  { id: 'amber_crt', name: 'Ámbar CRT P3', category: 'palettes', icon: '🟧', desc: 'Fósforo ámbar cálido de terminal VT220', hasIntensity: false },

  // ==========================================
  // 2. CRT & PANTALLAS ANALÓGICAS (15)
  // ==========================================
  { id: 'crt_scanlines_soft', name: 'Scanlines CRT Suaves', category: 'crt', icon: '📺', desc: 'Líneas de barrido analógicas sutiles', hasIntensity: true, defaultIntensity: 50 },
  { id: 'crt_scanlines_hard', name: 'Scanlines CRT Arcade', category: 'crt', icon: '📼', desc: 'Líneas de barrido pronunciadas de monitor arcade', hasIntensity: true, defaultIntensity: 80 },
  { id: 'crt_curvature', name: 'Tubo Curvo CRT', category: 'crt', icon: '🌐', desc: 'Geometría convexa de televisor de tubo antiguo', hasIntensity: true, defaultIntensity: 30 },
  { id: 'phosphor_glow', name: 'Resplandor de Fósforo', category: 'crt', icon: '💡', desc: 'Persistencia luminosa de electrones en el tubo', hasIntensity: true, defaultIntensity: 40 },
  { id: 'rgb_subpixel_grid', name: 'Rejilla RGB Trinitron', category: 'crt', icon: '🧇', desc: 'Matriz de puntos fósforo rojo, verde y azul', hasIntensity: true, defaultIntensity: 50 },
  { id: 'interlacing', name: 'Intercalado NTSC', category: 'crt', icon: '📶', desc: 'Efecto de entrelazado de campos pares e impares', hasIntensity: true, defaultIntensity: 50 },
  { id: 'tv_static_noise', name: 'Ruido Estático de TV', category: 'crt', icon: '📻', desc: 'Nieve analógica de sintonía en canal vacío', hasIntensity: true, defaultIntensity: 35 },
  { id: 'vhs_jitter', name: 'Jitter de Cabezal VHS', category: 'crt', icon: '📼', desc: 'Temblor horizontal característico de cinta magnética', hasIntensity: true, defaultIntensity: 40 },
  { id: 'glitch_rgb_shift', name: 'Glitch Aberración RGB', category: 'crt', icon: '⚡', desc: 'Desplazamiento horizontal de canales cromáticos', hasIntensity: true, defaultIntensity: 50 },
  { id: 'bad_tracking_vhs', name: 'Bad Tracking VHS', category: 'crt', icon: '〰️', desc: 'Banda de interferencia en la parte inferior del video', hasIntensity: true, defaultIntensity: 60 },
  { id: 'sync_loss_roll', name: 'Pérdida de Sincronía V-Hold', category: 'crt', icon: '🔄', desc: 'Deslizamiento vertical del cuadro de video', hasIntensity: true, defaultIntensity: 45 },
  { id: 'vertical_blanking', name: 'Líneas de Retorno VBI', category: 'crt', icon: '➖', desc: 'Líneas blancas en la parte superior del barrido', hasIntensity: false },
  { id: 'vhs_hud_play', name: 'HUD VHS "PLAY ▶"', category: 'crt', icon: '▶️', desc: 'Texto clásico OSD verde de videograbadora', hasIntensity: false },
  { id: 'vhs_hud_rec', name: 'HUD VHS "REC ●"', category: 'crt', icon: '🔴', desc: 'Punto rojo de grabación parpadeante con timer', hasIntensity: false },
  { id: 'low_batt_arcade', name: 'HUD Arcade "LOW BATT"', category: 'crt', icon: '🪫', desc: 'Aviso parpadeante de batería de cartucho baja', hasIntensity: false },

  // ==========================================
  // 3. PIXELACIÓN, MOSAICO & DITHERING (15)
  // ==========================================
  { id: 'pixelate_micro', name: 'Pixelado Micro (4px)', category: 'pixel', icon: '▫️', desc: 'Pixelado fino de alta resolución retro', hasIntensity: false },
  { id: 'pixelate_medium', name: 'Pixelado Medio (8px)', category: 'pixel', icon: '◽', desc: 'Aspecto icónico de juegos de 8 bits', hasIntensity: false },
  { id: 'pixelate_arcade', name: 'Pixelado Arcade (16px)', category: 'pixel', icon: '◻️', desc: 'Bloques grandes estilo arcade retro clásico', hasIntensity: false },
  { id: 'pixelate_mega', name: 'Pixelado Mega (24px)', category: 'pixel', icon: '🔲', desc: 'Mosaico grueso de baja resolución extrema', hasIntensity: false },
  { id: 'pixelate_ultra', name: 'Pixelado Ultra (36px)', category: 'pixel', icon: '⬛', desc: 'Abstracción en super bloques de color', hasIntensity: false },
  { id: 'dither_bayer', name: 'Dithering Bayer 4x4', category: 'pixel', icon: '🏁', desc: 'Tramado ordenado por matriz de dispersión retro', hasIntensity: true, defaultIntensity: 50 },
  { id: 'dither_floyd', name: 'Dithering Floyd-Steinberg', category: 'pixel', icon: '📰', desc: 'Difusión de error clásica estilo Macintosh 1984', hasIntensity: false },
  { id: 'hexagonal_mosaic', name: 'Mosaico Hexagonal', category: 'pixel', icon: '🔷', desc: 'Red de hexágonos estilo colmena de ciencia ficción', hasIntensity: true, defaultIntensity: 14 },
  { id: 'brick_mosaic', name: 'Ladrillos Pixel Art', category: 'pixel', icon: '🧱', desc: 'Disposición de bloques rectangulares entrelazados', hasIntensity: true, defaultIntensity: 12 },
  { id: 'led_board', name: 'Panel LED Gigante', category: 'pixel', icon: '🟡', desc: 'Diodos circulares iluminados con rejilla negra', hasIntensity: true, defaultIntensity: 12 },
  { id: 'cross_stitch', name: 'Punto de Cruz 8-Bit', category: 'pixel', icon: '✖️', desc: 'Píxeles renderizados como cruces bordadas', hasIntensity: true, defaultIntensity: 10 },
  { id: 'ascii_art', name: 'ASCII Matrix Art', category: 'pixel', icon: '🔡', desc: 'Representación visual con caracteres tipográficos', hasIntensity: true, defaultIntensity: 12 },
  { id: 'bitcrush_2bit', name: 'Bit-Crush 2 Bits', category: 'pixel', icon: '2️⃣', desc: 'Cuantización extrema a solo 4 tonos de color', hasIntensity: false },
  { id: 'bitcrush_4bit', name: 'Bit-Crush 4 Bits', category: 'pixel', icon: '4️⃣', desc: 'Reducción a 16 colores puros posterizados', hasIntensity: false },
  { id: 'pixel_grid_lines', name: 'Malla de Píxeles Negra', category: 'pixel', icon: '📐', desc: 'Borde oscuro entre cada píxel estilo LCD gigante', hasIntensity: true, defaultIntensity: 8 },

  // ==========================================
  // 4. COLOR & CINE RETRO (20)
  // ==========================================
  { id: 'film_noir', name: 'Film Noir 1940', category: 'color', icon: '🎩', desc: 'Blanco y negro dramático de alto contraste', hasIntensity: false },
  { id: 'sepia_western', name: 'Sepia Western 1890', category: 'color', icon: '📜', desc: 'Tono marrón envejecido con calidez rústica', hasIntensity: false },
  { id: 'technicolor_2strip', name: 'Technicolor 2-Strip', category: 'color', icon: '🎞️', desc: 'Aspecto cinematográfico temprano rojo y cian', hasIntensity: false },
  { id: 'kodachrome_64', name: 'Kodachrome 64', category: 'color', icon: '📷', desc: 'Colores saturados profundos y grano fino vintage', hasIntensity: false },
  { id: 'polaroid_80s', name: 'Polaroid Instantánea', category: 'color', icon: '📸', desc: 'Sombras lavadas y tonos pastel de los 80', hasIntensity: false },
  { id: 'bleach_bypass', name: 'Bleach Bypass Plateado', category: 'color', icon: '⚔️', desc: 'Desaturación metálica con sombras duras', hasIntensity: false },
  { id: 'cross_process', name: 'Proceso Cruzado (X-Pro)', category: 'color', icon: '🧪', desc: 'Desviación química de tonos amarillos y cianes', hasIntensity: false },
  { id: 'warm_70s', name: 'Cine Cálido 70s', category: 'color', icon: '🌅', desc: 'Atmósfera dorada y nostálgica de película retro', hasIntensity: false },
  { id: 'cold_scifi', name: 'Sci-Fi Azul Espacial', category: 'color', icon: '🧊', desc: 'Tinte azul gélido de nave interestelar', hasIntensity: false },
  { id: 'duotone_cyan_red', name: 'Duotono Cian & Carmesí', category: 'color', icon: '🔴', desc: 'Dos colores complementarios de póster arcade', hasIntensity: false },
  { id: 'duotone_gold_purple', name: 'Duotono Oro & Púrpura', category: 'color', icon: '👑', desc: 'Contraste real de violeta profundo y dorado', hasIntensity: false },
  { id: 'duotone_emerald_coral', name: 'Duotono Esmeralda & Coral', category: 'color', icon: '🪸', desc: 'Tonalidad tropical retro de alta energía', hasIntensity: false },
  { id: 'invert_colors', name: 'Negativo Fotográfico', category: 'color', icon: '🔲', desc: 'Inversión completa de valores de color', hasIntensity: false },
  { id: 'thermal_flir', name: 'Cámara Térmica FLIR', category: 'color', icon: '🌡️', desc: 'Mapa de calor por gradiente violeta-rojo-amarillo', hasIntensity: false },
  { id: 'night_vision_nvg', name: 'Visión Nocturna Militar', category: 'color', icon: '🥽', desc: 'Monocromo verde con viñeta y alto brillo central', hasIntensity: false },
  { id: 'xray_medical', name: 'Rayos X Negativo', category: 'color', icon: '🩻', desc: 'Aspecto radiográfico azulado y translúcido', hasIntensity: false },
  { id: 'solarize_sabattier', name: 'Solarización Sabattier', category: 'color', icon: '☀️', desc: 'Efecto de sobreexposición con bordes invertidos', hasIntensity: false },
  { id: 'posterize_4', name: 'Posterizar 4 Niveles', category: 'color', icon: '🎨', desc: 'Banding gráfico estilizado en 4 pasos tonales', hasIntensity: false },
  { id: 'posterize_8', name: 'Posterizar 8 Niveles', category: 'color', icon: '🖼️', desc: 'Reducción tonal estilo serigrafía pop art', hasIntensity: false },
  { id: 'neon_oversaturate', name: 'Neón Pop Sobresaturado', category: 'color', icon: '🍭', desc: 'Saturación al límite para un impacto electrizante', hasIntensity: false },

  // ==========================================
  // 5. ÓPTICA, DEFORMACIÓN & LENTES (15)
  // ==========================================
  { id: 'fisheye_lens', name: 'Lente Ojo de Pez', category: 'optics', icon: '🐟', desc: 'Curvatura angular esférica ultra amplia', hasIntensity: true, defaultIntensity: 45 },
  { id: 'barrel_distortion', name: 'Distorsión de Barril', category: 'optics', icon: '🛢️', desc: 'Abombamiento central de óptica angular', hasIntensity: true, defaultIntensity: 35 },
  { id: 'pincushion_distort', name: 'Distorsión de Almohadilla', category: 'optics', icon: '📌', desc: 'Estiramiento hacia las cuatro esquinas', hasIntensity: true, defaultIntensity: 35 },
  { id: 'water_ripple', name: 'Onda Acuática Senoidal', category: 'optics', icon: '🌊', desc: 'Ondulaciones dinámicas en la superficie', hasIntensity: true, defaultIntensity: 25 },
  { id: 'swirl_vortex', name: 'Vórtice Espiral', category: 'optics', icon: '🌀', desc: 'Remolino giratorio centrado en la imagen', hasIntensity: true, defaultIntensity: 35 },
  { id: 'split_mirror_h', name: 'Espejo Horizontal 2x', category: 'optics', icon: '🪞', desc: 'Reflejo simétrico en el eje central', hasIntensity: false },
  { id: 'split_mirror_v', name: 'Espejo Vertical 2x', category: 'optics', icon: '🔄', desc: 'Reflejo simétrico de arriba hacia abajo', hasIntensity: false },
  { id: 'kaleidoscope_4', name: 'Caleidoscopio 4 Caras', category: 'optics', icon: '💠', desc: 'Patrón simétrico en cruz de 4 cuadrantes', hasIntensity: false },
  { id: 'kaleidoscope_8', name: 'Caleidoscopio 8 Caras', category: 'optics', icon: '☸️', desc: 'Fractal geométrico de ocho reflejos rotativos', hasIntensity: false },
  { id: 'radial_zoom_blur', name: 'Desenfoque Zoom Radial', category: 'optics', icon: '💥', desc: 'Sensación de velocidad y aceleración hacia adelante', hasIntensity: true, defaultIntensity: 30 },
  { id: 'motion_blur_h', name: 'Desenfoque de Movimiento', category: 'optics', icon: '💨', desc: 'Estela horizontal de alta velocidad arcade', hasIntensity: true, defaultIntensity: 30 },
  { id: 'tilt_shift', name: 'Miniatura Tilt-Shift', category: 'optics', icon: '🚂', desc: 'Enfoque selectivo horizontal que crea efecto maqueta', hasIntensity: true, defaultIntensity: 40 },
  { id: 'vignette_dark', name: 'Viñeta Circular Oscura', category: 'optics', icon: '⚫', desc: 'Oscurecimiento suave en los bordes de la toma', hasIntensity: true, defaultIntensity: 60 },
  { id: 'vignette_arcade_box', name: 'Viñeta Cuadrada Arcade', category: 'optics', icon: '⏹️', desc: 'Marco biselado oscuro de pantalla de gabinete', hasIntensity: true, defaultIntensity: 50 },
  { id: 'speed_lines_manga', name: 'Líneas de Velocidad Manga', category: 'optics', icon: '🗯️', desc: 'Trazos radiales de impacto y acción arcade', hasIntensity: true, defaultIntensity: 40 },

  // ==========================================
  // 6. ILUMINACIÓN & ATMÓSFERA (10)
  // ==========================================
  { id: 'neon_flicker', name: 'Parpadeo Neón Arcade', category: 'atmosphere', icon: '🏮', desc: 'Oscilación de luminosidad de letrero comercial', hasIntensity: true, defaultIntensity: 35 },
  { id: 'strobe_light', name: 'Luz Estroboscópica', category: 'atmosphere', icon: '🚨', desc: 'Flashes rítmicos de alta energía para clímax', hasIntensity: true, defaultIntensity: 50 },
  { id: 'pixel_rain', name: 'Lluvia de Píxeles 8-Bit', category: 'atmosphere', icon: '🌧️', desc: 'Gotas rectangulares cayendo verticalmente', hasIntensity: true, defaultIntensity: 40 },
  { id: 'pixel_snow', name: 'Nieve de Píxeles', category: 'atmosphere', icon: '❄️', desc: 'Copos de nieve cuadrados oscilando en la pantalla', hasIntensity: true, defaultIntensity: 40 },
  { id: 'fire_embers', name: 'Chispas de Fuego Arcade', category: 'atmosphere', icon: '🔥', desc: 'Partículas incandescentes ascendiendo', hasIntensity: true, defaultIntensity: 45 },
  { id: 'film_dust_scratches', name: 'Polvo y Rayones de Cinta', category: 'atmosphere', icon: '📽️', desc: 'Marcas de desgaste de proyector de 16mm', hasIntensity: true, defaultIntensity: 50 },
  { id: 'god_rays', name: 'Rayos de Luz Divina (God Rays)', category: 'atmosphere', icon: '✨', desc: 'Haces de luz volumétrica descendiendo en ángulo', hasIntensity: true, defaultIntensity: 40 },
  { id: 'dungeon_fog', name: 'Niebla de Mazmorra', category: 'atmosphere', icon: '🌫️', desc: 'Capa vaporosa rodante en la parte inferior', hasIntensity: true, defaultIntensity: 45 },
  { id: 'confetti_8bit', name: 'Confeti Pixel Festivo', category: 'atmosphere', icon: '🎉', desc: 'Cuadritos de colores celebrando victoria', hasIntensity: true, defaultIntensity: 50 },
  { id: 'starfield_warp', name: 'Campo Estelar Warp 3D', category: 'atmosphere', icon: '⭐', desc: 'Estrellas pixeladas viajando hacia la cámara', hasIntensity: true, defaultIntensity: 40 },

  // ==========================================
  // 7. HUDS, OVERLAYS & RETRO BADGES (10)
  // ==========================================
  { id: 'overlay_insert_coin', name: 'Overlay "INSERT COIN"', category: 'huds', icon: '🪙', desc: 'Letrero parpadeante de moneda arcade en la base', hasIntensity: false },
  { id: 'overlay_1up_score', name: 'HUD "1UP [ 00000 ]"', category: 'huds', icon: '👾', desc: 'Marcador retro superior estilo Space Invaders', hasIntensity: false },
  { id: 'overlay_heart_health', name: 'Barra de Vidas (Corazones)', category: 'huds', icon: '❤️', desc: '3 corazones pixelados en la esquina superior', hasIntensity: false },
  { id: 'overlay_boss_bar', name: 'Barra de Jefe "BOSS"', category: 'huds', icon: '🐉', desc: 'Medidor de energía rojo para batallas épicas', hasIntensity: false },
  { id: 'overlay_cyber_crosshair', name: 'Mira Telescópica Cyber', category: 'huds', icon: '🎯', desc: 'Retícula táctica futurista con telemetría', hasIntensity: false },
  { id: 'overlay_arcade_bezel', name: 'Marco de Cabina Arcade', category: 'huds', icon: '🕹️', desc: 'Gabinete con arte retro envolviendo el video', hasIntensity: false },
  { id: 'overlay_gameboy_border', name: 'Marco Portátil Game Boy', category: 'huds', icon: '📱', desc: 'Carcasa clásica gris con botones A/B y D-pad', hasIntensity: false },
  { id: 'overlay_digital_clock', name: 'Reloj Digital Retro "99"', category: 'huds', icon: '⏱️', desc: 'Contador regresivo arcade de tiempo restante', hasIntensity: false },
  { id: 'overlay_boss_warning', name: 'Alerta "WARNING / PELIGRO"', category: 'huds', icon: '⚠️', desc: 'Franja de advertencia con sirena visual roja', hasIntensity: false },
  { id: 'overlay_35mm_perforations', name: 'Perforaciones Película 35mm', category: 'huds', icon: '🎞️', desc: 'Orificios de arrastre laterales de celuloide', hasIntensity: false }
];

// =========================================================================
// 100 TRANSICIONES CINEMATOGRÁFICAS, PIXEL & RETRO
// =========================================================================
export const TRANSITION_CATEGORIES = [
  { id: 'pixel', name: 'PIXEL & ARCADE (15)', icon: '👾' },
  { id: 'wipes', name: 'CORTINILLAS & WIPES (20)', icon: '↔️' },
  { id: 'glitch', name: 'GLITCH & DISTORSIÓN (15)', icon: '⚡' },
  { id: 'zooms', name: 'ZOOMS & MOVIMIENTO (15)', icon: '🌀' },
  { id: 'shapes', name: 'FORMAS & IRIS (15)', icon: '⭕' },
  { id: 'light', name: 'LUZ, FUEGO & ENERGÍA (10)', icon: '✨' },
  { id: 'cinema', name: 'CINE & PAPEL (10)', icon: '🎞️' }
];

export const ALL_TRANSITIONS = [
  // 1. Pixel & Arcade (15)
  { id: 'pixel_dissolve', name: 'Disolvencia Pixel 8-Bit', category: 'pixel', icon: '🏁' },
  { id: 'pixel_mosaic', name: 'Mosaico Progresivo', category: 'pixel', icon: '▦' },
  { id: 'pixel_fall', name: 'Cascada de Píxeles', category: 'pixel', icon: '🌧️' },
  { id: 'tetris_drop', name: 'Lluvia Bloques Tetris', category: 'pixel', icon: '🧱' },
  { id: 'pacman_chomp', name: 'Mordisco Pac-Man', category: 'pixel', icon: '🟡' },
  { id: 'space_invader', name: 'Descenso Invaders', category: 'pixel', icon: '👾' },
  { id: 'bit_crush_wipe', name: 'Barrido Bit-Crush', category: 'pixel', icon: '🔲' },
  { id: 'brick_wall_build', name: 'Muro de Ladrillos', category: 'pixel', icon: '🧱' },
  { id: 'dither_fade', name: 'Dispersión Dither', category: 'pixel', icon: '📰' },
  { id: 'arcade_curtain', name: 'Telón Arcade Dorado', category: 'pixel', icon: '🪙' },
  { id: 'scanline_sweep', name: 'Barrido Scanlines', category: 'pixel', icon: '📺' },
  { id: 'voxel_explode', name: 'Explosión de Vóxeles', category: 'pixel', icon: '💥' },
  { id: 'ascii_matrix', name: 'Matrix ASCII Rain', category: 'pixel', icon: '🟩' },
  { id: 'tv_turnoff', name: 'Apagado TV Retro', category: 'pixel', icon: '📺' },
  { id: 'tv_turnon', name: 'Encendido Tubo CRT', category: 'pixel', icon: '💡' },

  // 2. Cortinillas & Wipes (20)
  { id: 'wipe_left', name: 'Cortinilla Izquierda', category: 'wipes', icon: '⬅️' },
  { id: 'wipe_right', name: 'Cortinilla Derecha', category: 'wipes', icon: '➡️' },
  { id: 'wipe_up', name: 'Cortinilla Arriba', category: 'wipes', icon: '⬆️' },
  { id: 'wipe_down', name: 'Cortinilla Abajo', category: 'wipes', icon: '⬇️' },
  { id: 'wipe_split_h', name: 'División Puertas H', category: 'wipes', icon: '🚪' },
  { id: 'wipe_split_v', name: 'División Puertas V', category: 'wipes', icon: '🪟' },
  { id: 'wipe_diagonal_tl', name: 'Diagonal Sup. Izq.', category: 'wipes', icon: '↖️' },
  { id: 'wipe_diagonal_tr', name: 'Diagonal Sup. Der.', category: 'wipes', icon: '↗️' },
  { id: 'wipe_diagonal_bl', name: 'Diagonal Inf. Izq.', category: 'wipes', icon: '↙️' },
  { id: 'wipe_diagonal_br', name: 'Diagonal Inf. Der.', category: 'wipes', icon: '↘️' },
  { id: 'wipe_zigzag', name: 'Zig-Zag Arcade', category: 'wipes', icon: '⚡' },
  { id: 'wipe_sawtooth', name: 'Dientes de Sierra', category: 'wipes', icon: '🪚' },
  { id: 'wipe_checkerboard', name: 'Tablero de Ajedrez', category: 'wipes', icon: '🏁' },
  { id: 'wipe_blinds_h', name: 'Persianas Horizontales', category: 'wipes', icon: '📶' },
  { id: 'wipe_blinds_v', name: 'Persianas Verticales', category: 'wipes', icon: '🪟' },
  { id: 'wipe_spiral', name: 'Espiral Rotativa', category: 'wipes', icon: '🌀' },
  { id: 'wipe_clock', name: 'Reloj Radar 360°', category: 'wipes', icon: '⏱️' },
  { id: 'wipe_curtain_open', name: 'Apertura de Telón', category: 'wipes', icon: '🎭' },
  { id: 'wipe_curtain_close', name: 'Cierre de Telón', category: 'wipes', icon: '🎬' },
  { id: 'wipe_cross_split', name: 'Cruz 4 Cuadrantes', category: 'wipes', icon: '➕' },

  // 3. Glitch & Distorsión (15)
  { id: 'glitch_rgb_split', name: 'Ruptura Cromática RGB', category: 'glitch', icon: '⚡' },
  { id: 'glitch_vhs_tear', name: 'Desgarro de Cinta VHS', category: 'glitch', icon: '📼' },
  { id: 'glitch_databend', name: 'Corrupción Databend', category: 'glitch', icon: '💾' },
  { id: 'glitch_block_scramble', name: 'Desorden Macrobloques', category: 'glitch', icon: '🔲' },
  { id: 'glitch_hsync_roll', name: 'Salto H-Sync NTSC', category: 'glitch', icon: '〰️' },
  { id: 'glitch_static_noise', name: 'Nieve Estática Ráfaga', category: 'glitch', icon: '📻' },
  { id: 'glitch_pixel_sort', name: 'Clasificación Pixel Sort', category: 'glitch', icon: '📊' },
  { id: 'glitch_corrupt_scan', name: 'Escaneo Corrupto', category: 'glitch', icon: '💻' },
  { id: 'glitch_bit_flip', name: 'Inversión de Bits', category: 'glitch', icon: '🔄' },
  { id: 'glitch_interlace', name: 'Desfase Entrelazado', category: 'glitch', icon: '📶' },
  { id: 'glitch_quantum_jump', name: 'Salto Cuántico', category: 'glitch', icon: '⚛️' },
  { id: 'glitch_signal_loss', name: 'Pérdida de Señal', category: 'glitch', icon: '🚫' },
  { id: 'glitch_frame_ghost', name: 'Fantasma de Cuadro', category: 'glitch', icon: '👻' },
  { id: 'glitch_bad_tracking', name: 'Tracking VHS Defectuoso', category: 'glitch', icon: '📼' },
  { id: 'glitch_cyber_slice', name: 'Corte Láser Cyberpunk', category: 'glitch', icon: '🗡️' },

  // 4. Zooms, Movimiento & Warp (15)
  { id: 'zoom_in_punch', name: 'Zoom In Golpe Frontal', category: 'zooms', icon: '👊' },
  { id: 'zoom_out_spin', name: 'Zoom Out con Giro', category: 'zooms', icon: '🔄' },
  { id: 'spin_clockwise', name: 'Giro Horario Dinámico', category: 'zooms', icon: '🔃' },
  { id: 'spin_counter', name: 'Giro Antihorario', category: 'zooms', icon: '🔄' },
  { id: 'whip_pan_left', name: 'Latigazo Izquierda', category: 'zooms', icon: '💨' },
  { id: 'whip_pan_right', name: 'Latigazo Derecha', category: 'zooms', icon: '💨' },
  { id: 'whip_pan_up', name: 'Latigazo Arriba', category: 'zooms', icon: '💨' },
  { id: 'whip_pan_down', name: 'Latigazo Abajo', category: 'zooms', icon: '💨' },
  { id: 'warp_tunnel', name: 'Túnel Espacial Warp', category: 'zooms', icon: '🌌' },
  { id: 'hyperspace_jump', name: 'Salto Hiperespacio', category: 'zooms', icon: '🚀' },
  { id: 'fisheye_bulge', name: 'Lente Ojo de Pez Pop', category: 'zooms', icon: '🐟' },
  { id: 'swirl_vortex', name: 'Vórtice Centrífugo', category: 'zooms', icon: '🌀' },
  { id: 'ripple_water', name: 'Onda de Agua', category: 'zooms', icon: '🌊' },
  { id: 'shockwave_blast', name: 'Onda Expansiva Sónica', category: 'zooms', icon: '💥' },
  { id: 'lens_bounce', name: 'Rebote Elástico Lente', category: 'zooms', icon: '🏀' },

  // 5. Formas & Iris (15)
  { id: 'iris_circle_open', name: 'Iris Circular Apertura', category: 'shapes', icon: '⭕' },
  { id: 'iris_circle_close', name: 'Iris Circular Cierre', category: 'shapes', icon: '⚫' },
  { id: 'iris_box_open', name: 'Caja Cuadrada Apertura', category: 'shapes', icon: '🔲' },
  { id: 'iris_box_close', name: 'Caja Cuadrada Cierre', category: 'shapes', icon: '⬛' },
  { id: 'iris_diamond', name: 'Diamante Expansivo', category: 'shapes', icon: '🔷' },
  { id: 'iris_triangle', name: 'Triángulo Trifuerza', category: 'shapes', icon: '🔺' },
  { id: 'iris_pentagon', name: 'Pentágono Futurista', category: 'shapes', icon: '⬟' },
  { id: 'iris_hexagon', name: 'Hexágono Cyber', category: 'shapes', icon: '⬢' },
  { id: 'iris_heart', name: 'Corazón Retro 8-Bit', category: 'shapes', icon: '❤️' },
  { id: 'iris_star', name: 'Estrella Victoria 5P', category: 'shapes', icon: '⭐' },
  { id: 'concentric_rings', name: 'Anillos Concéntricos', category: 'shapes', icon: '🪐' },
  { id: 'grid_reveal_4x4', name: 'Malla Reveladora 4x4', category: 'shapes', icon: '▦' },
  { id: 'grid_reveal_8x8', name: 'Malla Reveladora 8x8', category: 'shapes', icon: '▥' },
  { id: 'honeycomb_hex', name: 'Panal Hexagonal', category: 'shapes', icon: '🍯' },
  { id: 'cross_reveal', name: 'Cruz Malta Expansiva', category: 'shapes', icon: '➕' },

  // 6. Luz, Fuego & Energía (10)
  { id: 'flash_white', name: 'Flash Cámara Blanco', category: 'light', icon: '📸' },
  { id: 'flash_gold', name: 'Destello Dorado Arcade', category: 'light', icon: '✨' },
  { id: 'laser_sweep', name: 'Barrido Láser Cian', category: 'light', icon: '🔫' },
  { id: 'neon_strobe', name: 'Estroboscópico Neón', category: 'light', icon: '🏮' },
  { id: 'sunflare_burn', name: 'Quemadura Solar', category: 'light', icon: '☀️' },
  { id: 'fire_inferno', name: 'Llamarada de Fuego', category: 'light', icon: '🔥' },
  { id: 'smoke_fog', name: 'Cortina de Humo', category: 'light', icon: '🌫️' },
  { id: 'sparkle_burst', name: 'Lluvia de Chispas', category: 'light', icon: '🎇' },
  { id: 'electric_arc', name: 'Rayo Arco Eléctrico', category: 'light', icon: '⚡' },
  { id: 'aurora_glow', name: 'Resplandor Boreal', category: 'light', icon: '🌌' },

  // 7. Cine & Papel (10)
  { id: 'film_burn', name: 'Quemadura Celuloide', category: 'cinema', icon: '🎞️' },
  { id: 'film_strip_roll', name: 'Rollo Película 35mm', category: 'cinema', icon: '📽️' },
  { id: 'sepia_dissolve', name: 'Disolvencia Sepia', category: 'cinema', icon: '📜' },
  { id: 'camera_shutter', name: 'Obturador Réflex', category: 'cinema', icon: '📷' },
  { id: 'crossfade_smooth', name: 'Fundido Cruzado Suave', category: 'cinema', icon: '🌫️' },
  { id: 'page_turn_left', name: 'Página Hoja Izquierda', category: 'cinema', icon: '📖' },
  { id: 'page_turn_right', name: 'Página Hoja Derecha', category: 'cinema', icon: '📑' },
  { id: 'paper_tear', name: 'Rasgón de Papel', category: 'cinema', icon: '✂️' },
  { id: 'black_hole', name: 'Agujero Negro Gravit.', category: 'cinema', icon: '🕳️' },
  { id: 'vintage_projector', name: 'Proyector Vintage', category: 'cinema', icon: '🎬' }
];

// =========================================================================
// 100 EFECTOS DE AUDIO & DSP MODULATION
// =========================================================================
export const AUDIO_EFFECT_CATEGORIES = [
  { id: 'gaming', name: 'CHIPTUNE & GAMING (15)', icon: '🕹️' },
  { id: 'vocals', name: 'VOCES & MODULADORES (15)', icon: '🎙️' },
  { id: 'filters', name: 'FILTROS & ECUALIZACIÓN (15)', icon: '🎛️' },
  { id: 'reverb', name: 'ESPACIOS & REVERB (15)', icon: '⛰️' },
  { id: 'echo', name: 'ECOS & DELAYS (15)', icon: '🔊' },
  { id: 'modulation', name: 'MODULACIÓN & CHORUS (15)', icon: '〰️' },
  { id: 'distortion', name: 'SATURACIÓN & TEXTURA (10)', icon: '🔥' }
];

export const ALL_AUDIO_EFFECTS = [
  // 1. Chiptune & Gaming (15)
  { id: 'gameboy_apu', label: 'Game Boy DMG-01 APU', category: 'gaming', icon: '🟢', desc: 'Sonido 4-canales clásico Game Boy' },
  { id: 'nes_triangle', label: 'NES Canal Triangular 8-Bit', category: 'gaming', icon: '🔺', desc: 'Bajo sintetizado puro de Famicom' },
  { id: 'snes_spc700', label: 'Super Nintendo SPC700', category: 'gaming', icon: '🟣', desc: 'Interpolación gaussiana 16-Bit clásica' },
  { id: 'c64_sid_filter', label: 'Commodore 64 SID Chip', category: 'gaming', icon: '🟤', desc: 'Filtro resonante analógico cálido' },
  { id: 'arcade_cabinet', label: 'Altavoz Cabina Arcade', category: 'gaming', icon: '🕹️', desc: 'Resonancia de madera y cono arcade' },
  { id: 'atari_pokey', label: 'Atari 2600 TIA / POKEY', category: 'gaming', icon: '👾', desc: 'Distorsión pura de onda cuadrada primitiva' },
  { id: 'bitcrush_8bit', label: 'Bitcrusher Clásico 8-Bit', category: 'gaming', icon: '8️⃣', desc: 'Reducción a 8 bits de resolución' },
  { id: 'bitcrush_4bit', label: 'Bitcrusher Lo-Fi 4-Bit', category: 'gaming', icon: '4️⃣', desc: 'Crujiente grano de 4 bits arcade' },
  { id: 'bitcrush_2bit', label: 'Bitcrusher Extremo 2-Bit', category: 'gaming', icon: '2️⃣', desc: 'Cuantización extrema a 4 niveles' },
  { id: 'quantize_crush', label: 'Cuantización de Muestreo', category: 'gaming', icon: '📉', desc: 'Sample rate downsample a 8 kHz' },
  { id: 'chiptune_arp', label: 'Arpegiador Rápido 8-Bit', category: 'gaming', icon: '✨', desc: 'Modulación de trémolo a velocidad chiptune' },
  { id: 'floppy_seek', label: 'Ruido Disquetera 3.5"', category: 'gaming', icon: '💾', desc: 'Filtro paso-alto chirriante de disquete' },
  { id: 'modem_dialup', label: 'Tono Conexión Dial-up 56k', category: 'gaming', icon: '📞', desc: 'Banda estrecha con zumbido telefónico' },
  { id: 'coin_ring', label: 'Resonancia Moneda 1UP', category: 'gaming', icon: '🪙', desc: 'Realce brillante en armónicos de campana' },
  { id: 'game_over_pitch', label: 'Caída de Tono Game Over', category: 'gaming', icon: '💀', desc: 'Modulación descendente de decadencia' },

  // 2. Voces & Moduladores (15)
  { id: 'robot_dalek', label: 'Robot Dalek Anillo Metálico', category: 'vocals', icon: '🤖', desc: 'Modulación en anillo con tono metálico' },
  { id: 'robot_cyber', label: 'Cyber Cyborg Futurista', category: 'vocals', icon: '🦾', desc: 'Robotización con retardo cortante' },
  { id: 'helium_voice', label: 'Voz de Helio (Chipmunk)', category: 'vocals', icon: '🐿️', desc: 'Pitch agudo y formantes rápidos' },
  { id: 'deep_monster', label: 'Voz de Monstruo Cavernoso', category: 'vocals', icon: '👹', desc: 'Pitch grave y resonancia profunda' },
  { id: 'demon_growl', label: 'Rugido Demoníaco Gutural', category: 'vocals', icon: '👿', desc: 'Distorsión sub-armónica aterradora' },
  { id: 'alien_flanger', label: 'Voz Alienígena Flanger', category: 'vocals', icon: '👽', desc: 'Oscilación espacial extraterrestre' },
  { id: 'walkie_talkie', label: 'Walkie-Talkie Policial VHF', category: 'vocals', icon: '📻', desc: 'Paso-banda saturado de comunicaciones' },
  { id: 'telephone_vintage', label: 'Teléfono de Disco Antiguo', category: 'vocals', icon: '☎️', desc: 'Línea telefónica analógica de 300-3400 Hz' },
  { id: 'megaphone_police', label: 'Megáfono Portátil Saturado', category: 'vocals', icon: '📢', desc: 'Rango medio estridente con saturación' },
  { id: 'intercom_space', label: 'Intercomunicador de Nave', category: 'vocals', icon: '🚀', desc: 'Resonancia metálica con ruido sutil' },
  { id: 'radio_military', label: 'Radio Militar Onda Corta', category: 'vocals', icon: '🪖', desc: 'Interferencia atmosférica y paso-banda' },
  { id: 'whisper_ghost', label: 'Susurro Espectral Etéreo', category: 'vocals', icon: '👻', desc: 'Filtro aéreo con cola reflectante' },
  { id: 'giant_slow', label: 'Gigante Titán Ralentizado', category: 'vocals', icon: '🗿', desc: 'Efecto masa gigante y bajas frecuencias' },
  { id: 'darth_breather', label: 'Respirador Cyborg Oscuro', category: 'vocals', icon: '🥷', desc: 'Acentuación grave y reverberación mecánica' },
  { id: 'vocoder_synth', label: 'Vocoder Sintético Robótico', category: 'vocals', icon: '🎹', desc: 'Voz robotizada afinada en sintetizador' },

  // 3. Filtros & Ecualización (15)
  { id: 'underwater_muffle', label: 'Muffled Subacuático', category: 'filters', icon: '🌊', desc: 'Corte radical de agudos bajo el agua' },
  { id: 'bass_boost_sub', label: 'Sub-Bass Potente (808)', category: 'filters', icon: '💣', desc: 'Aumento masivo en 50-80 Hz' },
  { id: 'bass_boost_punch', label: 'Golpe de Bajos Punchy', category: 'filters', icon: '💥', desc: 'Acentuación contundente en 120 Hz' },
  { id: 'treble_boost_crisp', label: 'Agudos Cristalinos High-End', category: 'filters', icon: '✨', desc: 'Brillo y presencia por encima de 6 kHz' },
  { id: 'am_radio_lofi', label: 'Radio AM 540 kHz', category: 'filters', icon: '📻', desc: 'Banda angosta y reducción dinámica' },
  { id: 'vinyl_gramophone', label: 'Gramófono 78 RPM Vintage', category: 'filters', icon: '🎺', desc: 'Curva acústica pre-electrónica 1920' },
  { id: 'cassette_tape_head', label: 'Cabezal Casete C90', category: 'filters', icon: '📼', desc: 'Atenuación suave y compresión de cinta' },
  { id: 'muffled_party', label: 'Fiesta en Habitación Vecina', category: 'filters', icon: '🚪', desc: 'Graves filtrados a través de pared sólida' },
  { id: 'subwoofer_shake', label: 'Vibración de Subwoofer', category: 'filters', icon: '🔊', desc: 'Paso-bajo extremo a 90 Hz retumbante' },
  { id: 'laser_notch', label: 'Filtro Notch Láser Resonante', category: 'filters', icon: '🎯', desc: 'Barrido con muesca resonante central' },
  { id: 'vocal_remover', label: 'Supresión Central Vocal', category: 'filters', icon: '🎤', desc: 'Atenuación de frecuencias de voz' },
  { id: 'highpass_tin', label: 'Lata Metálica (Highpass)', category: 'filters', icon: '🥫', desc: 'Corte completo de graves por debajo de 800 Hz' },
  { id: 'lowpass_club', label: 'Entrada Club Nocturno', category: 'filters', icon: '🕺', desc: 'Lowpass dinámico con bombeo suave' },
  { id: 'bandpass_vowel', label: 'Formante Vocal O-A-E', category: 'filters', icon: '🗣️', desc: 'Filtros duales simulando tracto vocal' },
  { id: 'comb_filter_metallic', label: 'Filtro en Peine Metálico', category: 'filters', icon: '🪮', desc: 'Reflexiones ultra cortas estilo chapa' },

  // 4. Espacios & Reverb (15)
  { id: 'reverb_cathedral', label: 'Catedral Gótica Gigante', category: 'reverb', icon: '⛪', desc: 'Decaimiento de 4 segundos con calidez' },
  { id: 'reverb_dungeon', label: 'Mazmorra Medieval de Piedra', category: 'reverb', icon: '🏰', desc: 'Reflexiones densas en muros rocosos' },
  { id: 'reverb_hall', label: 'Sala de Conciertos Filarmónica', category: 'reverb', icon: '🎻', desc: 'Acústica sinfónica equilibrada' },
  { id: 'reverb_small_room', label: 'Habitación Pequeña Seca', category: 'reverb', icon: '🛋️', desc: 'Espacio íntimo con reflexiones cortas' },
  { id: 'reverb_metallic_tank', label: 'Tanque de Acero Industrial', category: 'reverb', icon: '🛢️', desc: 'Timbre metálico brillante y resonante' },
  { id: 'reverb_outer_space', label: 'Vacío Espacial Cósmico', category: 'reverb', icon: '🌌', desc: 'Reverberación monumental y etérea' },
  { id: 'reverb_gargantua', label: 'Gargantúa Caverna Abismal', category: 'reverb', icon: '🕳️', desc: 'Reflejos masivos en abismo subterráneo' },
  { id: 'reverb_bathroom', label: 'Baño de Azulejos', category: 'reverb', icon: '🛁', desc: 'Reflexiones tempranas brillantes y rápidas' },
  { id: 'reverb_subway_tunnel', label: 'Túnel de Metro Subterráneo', category: 'reverb', icon: '🚇', desc: 'Eco alargado en tubo de hormigón' },
  { id: 'reverb_plate_vintage', label: 'Placa EMT-140 Vintage', category: 'reverb', icon: '🎛️', desc: 'Reverb de placa analógica suave' },
  { id: 'reverb_spring_amp', label: 'Tanque de Resortes Guitarra', category: 'reverb', icon: '🎸', desc: 'Boing característico de resortes vintage' },
  { id: 'reverb_endless_void', label: 'Abismo Infinito Sin Fin', category: 'reverb', icon: '♾️', desc: 'Cola de decaimiento ultra larga de 8s' },
  { id: 'reverb_bunker', label: 'Búnker Nuclear Sellado', category: 'reverb', icon: '☢️', desc: 'Reflejos fríos en paredes de hormigón armado' },
  { id: 'reverb_canyon', label: 'Cañón Montañoso Ecoico', category: 'reverb', icon: '🏞️', desc: 'Ecos lejanos en garganta natural' },
  { id: 'reverb_stadium', label: 'Estadio Deportivo Olímpico', category: 'reverb', icon: '🏟️', desc: 'Espacio abierto gigante con rebote' },

  // 5. Ecos & Delays (15)
  { id: 'echo_slapback', label: 'Slapback Rockabilly 50s', category: 'echo', icon: '🎙️', desc: 'Retardo ultra rápido de 90ms vintage' },
  { id: 'echo_quarter', label: 'Delay de Negra Rítmico', category: 'echo', icon: '🎵', desc: 'Eco a compás musical tradicional' },
  { id: 'echo_eighth', label: 'Delay de Corchea Rápido', category: 'echo', icon: '🎶', desc: 'Repeticiones vivas en medio compás' },
  { id: 'echo_ping_pong', label: 'Ping-Pong Estéreo Izq/Der', category: 'echo', icon: '🏓', desc: 'Rebotes alternados de un canal a otro' },
  { id: 'echo_space_dub', label: 'Space Dub Echo Reggae', category: 'echo', icon: '🛸', desc: 'Delay saturado con filtro en la cola' },
  { id: 'echo_tape_decay', label: 'Cinta Magnética con Pérdida', category: 'echo', icon: '📼', desc: 'Cada repetición pierde agudos gradualmente' },
  { id: 'echo_infinite', label: 'Bucle Infinito con Feedback', category: 'echo', icon: '🔄', desc: 'Regeneración al 92% sin distorsionar' },
  { id: 'echo_reverse_sim', label: 'Eco Invertido Fantasmal', category: 'echo', icon: '⏪', desc: 'Repeticiones envolventes en crescendo' },
  { id: 'echo_dotted', label: 'Retardo Tresillo Mágico', category: 'echo', icon: '✨', desc: 'Eco con compás sincopado punteado' },
  { id: 'echo_multi_tap', label: 'Múltiples Grifos Rítmicos', category: 'echo', icon: '🥁', desc: 'Múltiples líneas de delay superpuestas' },
  { id: 'echo_ambient_wash', label: 'Nube Ambiental Prolongada', category: 'echo', icon: '☁️', desc: 'Difusión de ecos creando textura sonora' },
  { id: 'echo_retro_repeat', label: 'Repetición Arcade 8-Bit', category: 'echo', icon: '👾', desc: 'Ecos cortos con cuantización digital' },
  { id: 'echo_cascade', label: 'Cascada Descendente de Ecos', category: 'echo', icon: '📉', desc: 'Repeticiones con caída progresiva' },
  { id: 'echo_flutter', label: 'Eco con Trémolo Rápido', category: 'echo', icon: '🦋', desc: 'Modulación de amplitud sobre el eco' },
  { id: 'echo_ghost_trail', label: 'Estela Fantasmal Estéreo', category: 'echo', icon: '👻', desc: 'Eco sutil paneado en segundo plano' },

  // 6. Modulación & Movimiento (15)
  { id: 'chorus_thick', label: 'Chorus Grueso Ochentas', category: 'modulation', icon: '🌊', desc: 'Doble voz rica y ensanchamiento estéreo' },
  { id: 'chorus_vintage', label: 'Chorus Roland Juno Clásico', category: 'modulation', icon: '🎹', desc: 'Modulación sutil pero envolvente' },
  { id: 'flanger_jet', label: 'Flanger Avión a Reacción', category: 'modulation', icon: '✈️', desc: 'Barrido en peine con efecto jet engine' },
  { id: 'flanger_deep', label: 'Flanger Barrido Profundo', category: 'modulation', icon: '🌀', desc: 'Peinado de frecuencias de amplio rango' },
  { id: 'phaser_4_stage', label: 'Phaser 4 Fases Clásico', category: 'modulation', icon: '🛸', desc: 'Desplazamiento de fase cálido de pedal' },
  { id: 'phaser_space', label: 'Phaser Espacial Giratorio', category: 'modulation', icon: '🪐', desc: 'Remolino psicodélico de 8 polos' },
  { id: 'tremolo_surf', label: 'Trémolo Surf Rock 60s', category: 'modulation', icon: '🏄', desc: 'Oscilación senoidal clásica de volumen' },
  { id: 'tremolo_chopper', label: 'Trémolo Helicóptero Cuadrado', category: 'modulation', icon: '🚁', desc: 'Corte abrupto en onda cuadrada' },
  { id: 'vibrato_warble', label: 'Vibrato Ondulante Vintage', category: 'modulation', icon: '〰️', desc: 'Modulación de frecuencia tipo cinta' },
  { id: 'ring_mod_bell', label: 'Modulador de Anillo Campana', category: 'modulation', icon: '🔔', desc: 'Armónicos inarmónicos campaniformes' },
  { id: 'rotary_leslie', label: 'Altavoz Giratorio Leslie', category: 'modulation', icon: '🌪️', desc: 'Simulación de bocina giratoria Doppler' },
  { id: 'autopan_smooth', label: 'Auto-Panorámico Dinámico', category: 'modulation', icon: '🎧', desc: 'Movimiento oscilante izquierda-derecha' },
  { id: 'pitch_fifth_up', label: 'Armonía Quinta Arriba (+7)', category: 'modulation', icon: '🎼', desc: 'Intervalo consonante de quinta justa' },
  { id: 'pitch_octave_down', label: 'Sub-Octava Subterránea (-12)', category: 'modulation', icon: '📉', desc: 'Duplicación en octava grave masiva' },
  { id: 'harmonizer_retro', label: 'Armonizador Sintético Doble', category: 'modulation', icon: '🎹', desc: 'Textura coral sintetizada en paralelo' },

  // 7. Saturación, Distorsión & Texturas (10)
  { id: 'tube_warmth', label: 'Saturación a Válvulas Cálida', category: 'distortion', icon: '💡', desc: 'Armónicos pares redondeados y suaves' },
  { id: 'overdrive_vintage', label: 'Overdrive TS-808 Vintage', category: 'distortion', icon: '🎸', desc: 'Crujiente grano medio saturado' },
  { id: 'fuzz_heavy', label: 'Fuzz Grunge Pesado 90s', category: 'distortion', icon: '⚡', desc: 'Recorte cuadrado masivo con sustain' },
  { id: 'hard_clipper', label: 'Recorte Duro Agresivo (Hard Clip)', category: 'distortion', icon: '🪓', desc: 'Clipping digital industrial puro' },
  { id: 'tape_warmth', label: 'Calidez de Cinta Magnética', category: 'distortion', icon: '📼', desc: 'Compresión natural y calidez analógica' },
  { id: 'vinyl_crackle', label: 'Crepitar y Polvo de Vinilo', category: 'distortion', icon: '💿', desc: 'Textura nostálgica de tocadiscos LP' },
  { id: 'cassette_wow', label: 'Lloro y Fluctuación (Wow & Flutter)', category: 'distortion', icon: '📻', desc: 'Desafinación mecánica de cinta gastada' },
  { id: 'broken_speaker', label: 'Altavoz Roto Rasgado', category: 'distortion', icon: '📢', desc: 'Membrana rota rasgando el audio' },
  { id: 'crushed_limiter', label: 'Compresión Destructiva Pumping', category: 'distortion', icon: '🔨', desc: 'Aplastamiento dinámico de pared de sonido' },
  { id: 'lofi_chill', label: 'Atmósfera Lo-Fi Nostálgica', category: 'distortion', icon: '☕', desc: 'Textura relajada filtrada con calidez' }
];

export const RetroTransitions = {
  NONE: 'none',
  PIXEL_DISSOLVE: 'pixel_dissolve',
  WIPE_HORIZONTAL: 'wipe_horizontal',
  WIPE_VERTICAL: 'wipe_vertical',
  IRIS_CIRCLE: 'iris_circle',
  BLINDS: 'blinds',
  GLITCH_WIPE: 'glitch_wipe',
  TV_TURNOFF: 'tv_turnoff'
};

export const TextAnimations = {
  NONE: 'none',
  ARCADE_BLINK: 'arcade_blink',
  TYPEWRITER: 'typewriter',
  WAVE_FLOAT: 'wave_float',
  GLITCH_SHAKE: 'glitch_shake',
  RAINBOW_CYCLE: 'rainbow_cycle',
  POP_SCALE: 'pop_scale'
};

