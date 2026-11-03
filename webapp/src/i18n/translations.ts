/**
 * translations.ts — All UI strings for AnyDownloader
 *
 * Supports: English · 简体中文 · 繁體中文 · 日本語 · Español · Français · Deutsch · العربية
 *
 * Use {placeholder} syntax for dynamic values; the t() function replaces them.
 * Missing keys in non-English locales fall back to English automatically.
 */

export const LOCALES = ['en', 'zh-CN', 'zh-TW', 'ja', 'es', 'fr', 'de', 'ar'] as const
export type Locale = typeof LOCALES[number]

export const LOCALE_NAMES: Record<Locale, string> = {
  en:      'English',
  'zh-CN': '简体中文',
  'zh-TW': '繁體中文',
  ja:      '日本語',
  es:      'Español',
  fr:      'Français',
  de:      'Deutsch',
  ar:      'العربية',
}

/** Languages that read right-to-left */
export const RTL_LOCALES = new Set<Locale>(['ar'])

// ── English (source of truth) ─────────────────────────────────────────────────
const en = {
  // Header
  siteTitle:       'AnyDownloader',
  // Hero
  heroEyebrow:     'Free · Open Source · 1000+ Sites',
  heroTitle:       'Download Videos from Anywhere',
  heroSub:         'Native desktop apps and browser extension for downloading videos from 1000+ sites. Fast, private, runs on your device.',
  // Buttons
  btnDownload:     'Download',
  // Feature strip
  feat1Label: '1000+ Video Sites',
  feat1Desc:  'YouTube, Bilibili, TikTok, Twitter, and thousands more',
  feat3Label: 'Privacy First',
  feat3Desc:  'No accounts, no tracking — everything runs locally on your device',
  feat4Label: 'Fast Downloads',
  feat4Desc:  'Multi-threaded parallel downloads with pause & resume',
  featEdgeLabel: 'Your Device, Your Power',
  featEdgeDesc:  'Downloads run on your own computer — no cloud server limits',
  // Release card features
  featSites:      '1000+ supported sites',
  featBackground: 'Background downloading',
  featPrivacy:    'No data collection',
  // How it works
  howTitle:      'How It Works',
  howStep1:      'Install the App',
  howStep1Desc:  'Download the desktop app for macOS or Windows, or add the Chrome extension',
  howStep2:      'Find a Video',
  howStep2Desc:  'Browse your favorite video site — the extension auto-detects downloadable videos',
  howStep3:      'Download Locally',
  howStep3Desc:  'Click download and the file saves directly to your computer — fast and private',
  // Downloads
  dlComingSoon:    'Coming Soon',
  dlNotAvailable:  'Not yet available',
  // Misc
  footer:       '2025 AnyDownloader · Privacy first · Open source',
}

export type TKey = keyof typeof en

// ── Translations ──────────────────────────────────────────────────────────────

const zhCN: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     '免费 · 开源 · 支持1000+网站',
  heroTitle:       '从任何网站下载视频',
  heroSub:         '原生桌面应用和浏览器扩展，支持从1000+网站下载视频。快速、私密、在你的设备上运行。',
  btnDownload:     '下载',
  feat1Label: '1000+ 视频网站',
  feat1Desc:  'YouTube、Bilibili、TikTok、Twitter 等数千个网站',
  feat3Label: '隐私优先',
  feat3Desc:  '无需账号，不追踪——一切在你的设备本地运行',
  feat4Label: '高速下载',
  feat4Desc:  '多线程并行下载，支持暂停和恢复',
  featEdgeLabel: '你的设备，你的算力',
  featEdgeDesc:  '下载在你自己的电脑上运行——没有云服务器限制',
  featSites:      '支持1000+网站',
  featBackground: '后台下载',
  featPrivacy:    '不收集数据',
  howTitle:      '使用方法',
  howStep1:      '安装应用',
  howStep1Desc:  '下载 macOS 或 Windows 桌面应用，或安装 Chrome 扩展',
  howStep2:      '找到视频',
  howStep2Desc:  '浏览你喜欢的视频网站——扩展会自动检测可下载的视频',
  howStep3:      '本地下载',
  howStep3Desc:  '点击下载，文件直接保存到你的电脑——快速且私密',
  dlComingSoon:  '即将上线',
  dlNotAvailable:'暂未发布',
  footer:        '2025 AnyDownloader · 隐私优先 · 开源',
}

const zhTW: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     '免費 · 開源 · 支援1000+網站',
  heroTitle:       '從任何網站下載影片',
  heroSub:         '原生桌面應用和瀏覽器擴充功能，支援從1000+網站下載影片。快速、私密、在你的裝置上執行。',
  btnDownload:     '下載',
  feat1Label: '1000+ 影片網站',
  feat1Desc:  'YouTube、Bilibili、TikTok、Twitter 等數千個網站',
  feat3Label: '隱私優先',
  feat3Desc:  '無需帳號，不追蹤——一切在你的裝置本地執行',
  feat4Label: '高速下載',
  feat4Desc:  '多執行緒並行下載，支援暫停和恢復',
  featEdgeLabel: '你的裝置，你的算力',
  featEdgeDesc:  '下載在你自己的電腦上執行——沒有雲端伺服器限制',
  featSites:      '支援1000+網站',
  featBackground: '背景下載',
  featPrivacy:    '不收集資料',
  howTitle:      '使用方法',
  howStep1:      '安裝應用',
  howStep1Desc:  '下載 macOS 或 Windows 桌面應用，或安裝 Chrome 擴充功能',
  howStep2:      '找到影片',
  howStep2Desc:  '瀏覽你喜歡的影片網站——擴充功能會自動偵測可下載的影片',
  howStep3:      '本地下載',
  howStep3Desc:  '點擊下載，檔案直接儲存到你的電腦——快速且私密',
  dlComingSoon:  '即將推出',
  dlNotAvailable:'尚未發布',
  footer:        '2025 AnyDownloader · 隱私優先 · 開源',
}

const ja: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     '無料 · オープンソース · 1000以上のサイト',
  heroTitle:       'どこからでも動画をダウンロード',
  heroSub:         'ネイティブデスクトップアプリとブラウザ拡張機能で、1000以上のサイトから動画をダウンロード。高速・プライベート・デバイス上で実行。',
  btnDownload:     'ダウンロード',
  feat1Label: '1000以上の動画サイト',
  feat1Desc:  'YouTube、Bilibili、TikTok、Twitterなど数千サイト',
  feat3Label: 'プライバシー優先',
  feat3Desc:  'アカウント不要、追跡なし — すべてデバイス上でローカル実行',
  feat4Label: '高速ダウンロード',
  feat4Desc:  'マルチスレッド並列ダウンロード、一時停止・再開対応',
  featEdgeLabel: 'あなたのデバイス、あなたのパワー',
  featEdgeDesc:  'ダウンロードはあなた自身のコンピュータで実行 — クラウドサーバーの制限なし',
  featSites:      '1000以上のサイト対応',
  featBackground: 'バックグラウンドダウンロード',
  featPrivacy:    'データ収集なし',
  howTitle:      '使い方',
  howStep1:      'アプリをインストール',
  howStep1Desc:  'macOSまたはWindows向けデスクトップアプリをダウンロード、またはChrome拡張機能を追加',
  howStep2:      '動画を見つける',
  howStep2Desc:  'お気に入りの動画サイトを閲覧 — 拡張機能がダウンロード可能な動画を自動検出',
  howStep3:      'ローカルにダウンロード',
  howStep3Desc:  'ダウンロードをクリックすると、ファイルがコンピュータに直接保存 — 高速かつプライベート',
  dlComingSoon:  '近日公開',
  dlNotAvailable:'まだ利用不可',
  footer:        '2025 AnyDownloader · プライバシー優先 · オープンソース',
}

const es: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     'Gratis · Código abierto · Más de 1000 sitios',
  heroTitle:       'Descarga videos desde cualquier sitio',
  heroSub:         'Apps nativas de escritorio y extensión de navegador para descargar videos de más de 1000 sitios. Rápido, privado, funciona en tu dispositivo.',
  btnDownload:     'Descargar',
  feat1Label: 'Más de 1000 sitios',
  feat1Desc:  'YouTube, Bilibili, TikTok, Twitter y miles más',
  feat3Label: 'Privacidad primero',
  feat3Desc:  'Sin cuentas, sin rastreo — todo funciona localmente en tu dispositivo',
  feat4Label: 'Descargas rápidas',
  feat4Desc:  'Descargas paralelas multihilo con pausa y reanudación',
  featEdgeLabel: 'Tu dispositivo, tu potencia',
  featEdgeDesc:  'Las descargas se ejecutan en tu propia computadora — sin límites de servidor en la nube',
  featSites:      'Más de 1000 sitios',
  featBackground: 'Descarga en segundo plano',
  featPrivacy:    'Sin recopilación de datos',
  howTitle:      'Cómo funciona',
  howStep1:      'Instala la app',
  howStep1Desc:  'Descarga la app de escritorio para macOS o Windows, o añade la extensión de Chrome',
  howStep2:      'Encuentra un video',
  howStep2Desc:  'Navega por tu sitio favorito — la extensión detecta automáticamente los videos descargables',
  howStep3:      'Descarga localmente',
  howStep3Desc:  'Haz clic en descargar y el archivo se guarda directamente en tu computadora — rápido y privado',
  dlComingSoon:  'Próximamente',
  dlNotAvailable:'No disponible aún',
  footer:        '2025 AnyDownloader · Privacidad primero · Código abierto',
}

const fr: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     'Gratuit · Open source · Plus de 1000 sites',
  heroTitle:       'Téléchargez des vidéos de partout',
  heroSub:         'Applications de bureau natives et extension de navigateur pour télécharger des vidéos de plus de 1000 sites. Rapide, privé, fonctionne sur votre appareil.',
  btnDownload:     'Télécharger',
  feat1Label: 'Plus de 1000 sites',
  feat1Desc:  'YouTube, Bilibili, TikTok, Twitter et des milliers d\'autres',
  feat3Label: 'Confidentialité d\'abord',
  feat3Desc:  'Pas de comptes, pas de suivi — tout fonctionne localement sur votre appareil',
  feat4Label: 'Téléchargements rapides',
  feat4Desc:  'Téléchargements parallèles multi-thread avec pause et reprise',
  featEdgeLabel: 'Votre appareil, votre puissance',
  featEdgeDesc:  'Les téléchargements s\'exécutent sur votre propre ordinateur — pas de limites de serveur cloud',
  featSites:      'Plus de 1000 sites',
  featBackground: 'Téléchargement en arrière-plan',
  featPrivacy:    'Aucune collecte de données',
  howTitle:      'Comment ça marche',
  howStep1:      'Installez l\'app',
  howStep1Desc:  'Téléchargez l\'app de bureau pour macOS ou Windows, ou ajoutez l\'extension Chrome',
  howStep2:      'Trouvez une vidéo',
  howStep2Desc:  'Naviguez sur votre site favori — l\'extension détecte automatiquement les vidéos téléchargeables',
  howStep3:      'Téléchargez localement',
  howStep3Desc:  'Cliquez sur télécharger et le fichier se sauvegarde directement sur votre ordinateur — rapide et privé',
  dlComingSoon:  'Bientôt disponible',
  dlNotAvailable:'Pas encore disponible',
  footer:        '2025 AnyDownloader · Confidentialité d\'abord · Open source',
}

const de: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     'Kostenlos · Open Source · Über 1000 Seiten',
  heroTitle:       'Videos von überall herunterladen',
  heroSub:         'Native Desktop-Apps und Browser-Erweiterung zum Herunterladen von Videos von über 1000 Seiten. Schnell, privat, läuft auf Ihrem Gerät.',
  btnDownload:     'Herunterladen',
  feat1Label: 'Über 1000 Video-Seiten',
  feat1Desc:  'YouTube, Bilibili, TikTok, Twitter und tausende mehr',
  feat3Label: 'Datenschutz zuerst',
  feat3Desc:  'Keine Konten, kein Tracking — alles läuft lokal auf Ihrem Gerät',
  feat4Label: 'Schnelle Downloads',
  feat4Desc:  'Multi-Thread parallele Downloads mit Pause und Fortsetzen',
  featEdgeLabel: 'Ihr Gerät, Ihre Leistung',
  featEdgeDesc:  'Downloads laufen auf Ihrem eigenen Computer — keine Cloud-Server-Limits',
  featSites:      'Über 1000 Seiten',
  featBackground: 'Hintergrund-Downloads',
  featPrivacy:    'Keine Datenerfassung',
  howTitle:      'So funktioniert es',
  howStep1:      'App installieren',
  howStep1Desc:  'Desktop-App für macOS oder Windows herunterladen oder Chrome-Erweiterung hinzufügen',
  howStep2:      'Video finden',
  howStep2Desc:  'Ihre Lieblings-Videoseite durchsuchen — die Erweiterung erkennt automatisch herunterladbare Videos',
  howStep3:      'Lokal herunterladen',
  howStep3Desc:  'Auf Download klicken und die Datei wird direkt auf Ihrem Computer gespeichert — schnell und privat',
  dlComingSoon:  'Demnächst verfügbar',
  dlNotAvailable:'Noch nicht verfügbar',
  footer:        '2025 AnyDownloader · Datenschutz zuerst · Open Source',
}

const ar: Record<TKey, string> = {
  siteTitle:       'AnyDownloader',
  heroEyebrow:     'مجاني · مفتوح المصدر · أكثر من 1000 موقع',
  heroTitle:       'حمّل الفيديو من أي مكان',
  heroSub:         'تطبيقات سطح مكتب أصلية وإضافة متصفح لتنزيل الفيديو من أكثر من 1000 موقع. سريع وخاص ويعمل على جهازك.',
  btnDownload:     'تنزيل',
  feat1Label: 'أكثر من 1000 موقع',
  feat1Desc:  'YouTube و Bilibili و TikTok و Twitter وآلاف المواقع الأخرى',
  feat3Label: 'الخصوصية أولاً',
  feat3Desc:  'بدون حسابات، بدون تتبع — كل شيء يعمل محلياً على جهازك',
  feat4Label: 'تنزيلات سريعة',
  feat4Desc:  'تنزيل متوازي متعدد الخيوط مع إيقاف مؤقت واستئناف',
  featEdgeLabel: 'جهازك، قوتك',
  featEdgeDesc:  'التنزيلات تعمل على حاسوبك الخاص — بدون قيود خادم سحابي',
  featSites:      'أكثر من 1000 موقع',
  featBackground: 'تنزيل في الخلفية',
  featPrivacy:    'بدون جمع بيانات',
  howTitle:      'كيف يعمل',
  howStep1:      'ثبّت التطبيق',
  howStep1Desc:  'حمّل تطبيق سطح المكتب لـ macOS أو Windows، أو أضف إضافة Chrome',
  howStep2:      'ابحث عن فيديو',
  howStep2Desc:  'تصفح موقع الفيديو المفضل لديك — الإضافة تكتشف تلقائياً الفيديوهات القابلة للتنزيل',
  howStep3:      'حمّل محلياً',
  howStep3Desc:  'انقر على تنزيل ويُحفظ الملف مباشرة على حاسوبك — سريع وخاص',
  dlComingSoon:  'قريباً',
  dlNotAvailable:'غير متوفر بعد',
  footer:        '2025 AnyDownloader · الخصوصية أولاً · مفتوح المصدر',
}

export const TRANSLATIONS: Record<Locale, Record<TKey, string>> = {
  en,
  'zh-CN': zhCN,
  'zh-TW': zhTW,
  ja,
  es,
  fr,
  de,
  ar,
}
