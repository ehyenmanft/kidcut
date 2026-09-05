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
