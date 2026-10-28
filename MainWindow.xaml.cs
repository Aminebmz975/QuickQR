using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

// Alias explicites pour éliminer tout conflit d'espace de noms entre WPF et Windows Forms / GDI+
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Clipboard = System.Windows.Clipboard;

namespace QuickQR
{
    public partial class MainWindow : Window
    {
        private readonly Win32InteropService _win32Service = new();
        private readonly QrGeneratorService _qrService = new();

        private WinForms.NotifyIcon? _notifyIcon;
        private bool _isReallyClosing;
        private bool _isLiveSyncActive = true;
        private bool _isUpdatingFromClipboard;

        private List<string> _chunks = new() { string.Empty };
        private int _currentPageIndex;
        private int _maxChunkSize = 400;

        public MainWindow()
        {
            InitializeComponent();
            LoadWindowIcon();
            InitializeTrayIcon();

            // Gestion des raccourcis clavier de la fenêtre
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Événement de chargement complet
            Loaded += MainWindow_Loaded;
        }

        private void LoadWindowIcon()
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickQR.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
                }
            }
            catch
            {
                // Fallback silencieux : Windows utilisera automatiquement l'icône de l'exécutable PE
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Activate();
            Focus();
            UpdateChunksAndQr();
        }

        #region Initialisation Win32 & System Tray
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                _win32Service.Attach(handle);
                _win32Service.HotKeyPressed += OnHotKeyPressed;
                _win32Service.ClipboardUpdated += OnClipboardUpdated;

                // Capture initiale du presse-papiers au démarrage
                CaptureClipboardNow();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Win32 registration error: {ex.Message}");
            }
        }

        private void InitializeTrayIcon()
        {
            Drawing.Icon? trayIcon = null;
            try
            {
                var iconUri = new Uri("pack://application:,,,/QuickQR.ico");
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    using (streamInfo.Stream)
                    {
                        trayIcon = new Drawing.Icon(streamInfo.Stream);
                    }
                }
            }
            catch
            {
                // Fallback silencieux vers l'icône dynamique en mémoire si nécessaire
            }

            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "QuickQR - Générateur de QR Code instantané",
                Visible = true,
                Icon = trayIcon ?? CreateDynamicQrIcon()
            };

            // Menu contextuel natif du System Tray
            var contextMenu = new WinForms.ContextMenuStrip();

            var openItem = new WinForms.ToolStripMenuItem("Ouvrir (Ctrl+Alt+Q)");
            openItem.Click += (s, e) => RestoreAndActivate();
            contextMenu.Items.Add(openItem);

            var captureItem = new WinForms.ToolStripMenuItem("Capturer le presse-papiers");
            captureItem.Click += (s, e) =>
            {
                RestoreAndActivate();
                CaptureClipboardNow();
            };
            contextMenu.Items.Add(captureItem);

            contextMenu.Items.Add(new WinForms.ToolStripSeparator());

            var quitItem = new WinForms.ToolStripMenuItem("Quitter définitivement (Ctrl+Q)");
            quitItem.Click += (s, e) => CloseApplication();
            contextMenu.Items.Add(quitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Clic gauche : affiche et met la fenêtre au premier plan
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    RestoreAndActivate();
                }
            };
        }

        /// <summary>
        /// Génère dynamiquement une icône 16x16 de QR code en mémoire via GDI+
        /// pour garantir l'absence totale de dépendance vers un fichier .ico physique.
        /// </summary>
        private static Drawing.Icon CreateDynamicQrIcon()
        {
            using var bmp = new Drawing.Bitmap(16, 16);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(Drawing.Color.White);
                using var brush = new Drawing.SolidBrush(Drawing.Color.Black);

                // Coin supérieur gauche (Finder pattern)
                g.FillRectangle(brush, 1, 1, 5, 5);
                g.FillRectangle(Drawing.Brushes.White, 2, 2, 3, 3);
                g.FillRectangle(brush, 3, 3, 1, 1);

                // Coin supérieur droit (Finder pattern)
                g.FillRectangle(brush, 10, 1, 5, 5);
                g.FillRectangle(Drawing.Brushes.White, 11, 2, 3, 3);
                g.FillRectangle(brush, 12, 3, 1, 1);

                // Coin inférieur gauche (Finder pattern)
                g.FillRectangle(brush, 1, 10, 5, 5);
                g.FillRectangle(Drawing.Brushes.White, 2, 11, 3, 3);
                g.FillRectangle(brush, 3, 12, 1, 1);

                // Motifs de synchronisation et données
                g.FillRectangle(brush, 7, 2, 2, 2);
                g.FillRectangle(brush, 2, 7, 2, 2);
                g.FillRectangle(brush, 7, 7, 2, 2);
                g.FillRectangle(brush, 11, 8, 2, 2);
                g.FillRectangle(brush, 7, 12, 2, 2);
                g.FillRectangle(brush, 11, 11, 3, 3);
            }

            IntPtr hIcon = bmp.GetHicon();
            return Drawing.Icon.FromHandle(hIcon);
        }
        #endregion

        #region Cycle de Vie & Fermeture Intelligente
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isReallyClosing)
            {
                // Masquage intelligent dans le System Tray au lieu de fermer le processus
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Libération des ressources Win32 et System Tray
            _win32Service.Dispose();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            base.OnClosed(e);
        }

        private void CloseApplication()
        {
            _isReallyClosing = true;
            Close();
            Application.Current.Shutdown();
        }

        public void RestoreAndActivate()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();
        }
        #endregion

        #region Événements Système (Raccourci Global & Presse-papiers)
        private void OnHotKeyPressed()
        {
            Dispatcher.Invoke(() =>
            {
                bool wasHidden = !IsVisible || WindowState == WindowState.Minimized;
                RestoreAndActivate();

                // Si la fenêtre était cachée ou si Live Sync est actif, capturer immédiatement
                if (wasHidden || _isLiveSyncActive)
                {
                    CaptureClipboardNow();
                }
            });
        }

        private void OnClipboardUpdated()
        {
            Dispatcher.Invoke(() =>
            {
                // Synchronisation automatique seulement si le mode Live Sync est actif
                if (_isLiveSyncActive)
                {
                    CaptureClipboardNow();
                }
            });
        }

        private void CaptureClipboardNow()
        {
            if (Win32InteropService.TryGetClipboardText(out string text))
            {
                _isUpdatingFromClipboard = true;
                try
                {
                    txtSource.Text = text;
                    txtSource.SelectAll();
                }
                finally
                {
                    _isUpdatingFromClipboard = false;
                }

                UpdateChunksAndQr();
            }
        }
        #endregion

        #region Gestion du Texte, Carrousel & QR Code
        private void TxtSource_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Si l'utilisateur modifie manuellement le texte, passer automatiquement en mode PAUSE (Verrouillé)
            if (!_isUpdatingFromClipboard && _isLiveSyncActive)
            {
                SetLiveSyncState(false);
            }

            UpdateChunksAndQr();
        }

        private void UpdateChunksAndQr()
        {
            if (txtSource == null || imgQrCode == null || txtPageIndicator == null || txtStatusCounters == null)
            {
                return;
            }

            string fullText = txtSource.Text ?? string.Empty;

            // Découpage en blocs sans altération de contenu
            _chunks = TextChunkerService.SplitIntoChunks(fullText, _maxChunkSize);

            // Ajustement de l'index de la page courante
            if (_currentPageIndex >= _chunks.Count)
            {
                _currentPageIndex = Math.Max(0, _chunks.Count - 1);
            }
            if (_currentPageIndex < 0)
            {
                _currentPageIndex = 0;
            }

            // Génération du QR Code du bloc courant
            string currentChunk = _chunks[_currentPageIndex];
            imgQrCode.Source = _qrService.GenerateQrBitmap(currentChunk);

            // Mise à jour de l'indicateur ECC
            txtEccBadge.Text = currentChunk.Length < 150 ? "ECC Level: M (15%)" : "ECC Level: L (7%)";

            // Mise à jour des contrôles du carrousel
            txtPageIndicator.Text = $"Page {_currentPageIndex + 1} / {_chunks.Count}";
            btnPrevPage.IsEnabled = _currentPageIndex > 0;
            btnNextPage.IsEnabled = _currentPageIndex < _chunks.Count - 1;

            // Calcul des compteurs
            int charCount = fullText.Length;
            int byteCount = Encoding.UTF8.GetByteCount(fullText);
            int pagesCount = _chunks.Count;
            txtStatusCounters.Text = $"{charCount:N0} caractère{(charCount > 1 ? "s" : "")} | {byteCount:N0} octet{(byteCount > 1 ? "s" : "")} UTF-8 | {pagesCount} page{(pagesCount > 1 ? "s" : "")} QR";

            // Calcul de la tranche de caractères visualisée
            if (charCount == 0)
            {
                txtChunkRange.Text = string.Empty;
            }
            else
            {
                int chunkStart = 0;
                for (int i = 0; i < _currentPageIndex; i++)
                {
                    chunkStart += _chunks[i].Length;
                }
                int chunkEnd = chunkStart + currentChunk.Length;
                txtChunkRange.Text = $" (Affichage des car. {chunkStart + 1} à {chunkEnd})";
            }

            // Mise à jour de la Jauge de Densité et du badge coloré
            UpdateDensityGauge(currentChunk.Length, pagesCount);
        }

        private void UpdateDensityGauge(int currentChunkLength, int totalPages)
        {
            progressDensity.Value = Math.Min(currentChunkLength, 600);

            if (totalPages > 1)
            {
                // Mode carrousel actif
                progressDensity.Foreground = (Brush)FindResource("AccentOrange");
                badgeDensity.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03));
                badgeDensity.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                txtDensityLabel.Foreground = (Brush)FindResource("AccentOrange");
                txtDensityLabel.Text = $"● Découpé en {totalPages} QR Codes";
            }
            else if (currentChunkLength <= 250)
            {
                // Optimal
                progressDensity.Foreground = (Brush)FindResource("AccentGreen");
                badgeDensity.Background = new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B));
                badgeDensity.BorderBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
                txtDensityLabel.Foreground = (Brush)FindResource("AccentGreen");
                txtDensityLabel.Text = "● Optimal - Scan instantané";
            }
            else if (currentChunkLength <= 450)
            {
                // Dense
                progressDensity.Foreground = (Brush)FindResource("AccentOrange");
                badgeDensity.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03));
                badgeDensity.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
                txtDensityLabel.Foreground = (Brush)FindResource("AccentOrange");
                txtDensityLabel.Text = "● Dense - Éloigner légèrement le téléphone";
            }
            else
            {
                // Haute densité
                progressDensity.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                badgeDensity.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x0A, 0x0A));
                badgeDensity.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                txtDensityLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                txtDensityLabel.Text = "● Haute densité";
            }
        }
        #endregion

        #region Actions de la Barre d'Outils
        private void BtnLiveSync_Click(object sender, RoutedEventArgs e)
        {
            if (_isLiveSyncActive)
            {
                // Passage manuel en pause
                SetLiveSyncState(false);
            }
            else
            {
                // Réactivation et capture immédiate du presse-papiers
                SetLiveSyncState(true);
                CaptureClipboardNow();
            }
        }

        private void SetLiveSyncState(bool active)
        {
            _isLiveSyncActive = active;

            if (active)
            {
                txtLiveSyncIcon.Text = "🔄";
                txtLiveSyncLabel.Text = "Live Sync : ACTIF";
                txtLiveSyncLabel.Foreground = (Brush)FindResource("AccentGreen");
                txtEditorHint.Text = "Frappe manuelle = Pause Live Sync";
            }
            else
            {
                txtLiveSyncIcon.Text = "🔒";
                txtLiveSyncLabel.Text = "Live Sync : PAUSE (Cliquer pour capturer)";
                txtLiveSyncLabel.Foreground = (Brush)FindResource("AccentOrange");
                txtEditorHint.Text = "Édition manuelle protégée";
            }
        }

        private void BtnMinify_Click(object sender, RoutedEventArgs e)
        {
            string original = txtSource.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(original)) return;

            string minified;
            try
            {
                // Essai de minification JSON stricte
                using var jsonDoc = JsonDocument.Parse(original);
                minified = JsonSerializer.Serialize(jsonDoc.RootElement, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                // Minification texte générique : fusion des espaces et sauts de lignes multiples
                minified = Regex.Replace(original.Trim(), @"[ \t]+", " ");
                minified = Regex.Replace(minified, @"(\r?\n)+", "\n");
            }

            // Remplacement compatible avec la pile Undo native de WPF (Ctrl+Z)
            txtSource.Focus();
            txtSource.SelectAll();
            txtSource.SelectedText = minified;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtSource.Focus();
            txtSource.SelectAll();
            txtSource.SelectedText = string.Empty;
        }

        private void ComboChunkSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtSource == null) return;

            if (comboChunkSize.SelectedItem is ComboBoxItem item && 
                int.TryParse(item.Tag?.ToString(), out int size))
            {
                _maxChunkSize = size;
                _currentPageIndex = 0;
                UpdateChunksAndQr();
            }
        }

        private void BtnAlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;

            if (Topmost)
            {
                txtPinLabel.Text = "Au 1er plan : OUI";
                txtPinLabel.Foreground = (Brush)FindResource("AccentBlue");
            }
            else
            {
                txtPinLabel.Text = "Au 1er plan : NON";
                txtPinLabel.Foreground = (Brush)FindResource("TextMuted");
            }
        }
        #endregion

        #region Navigation Carrousel & Clavier
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            PreviousPage();
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            NextPage();
        }

        private void PreviousPage()
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex--;
                UpdateChunksAndQr();
            }
        }

        private void NextPage()
        {
            if (_currentPageIndex < _chunks.Count - 1)
            {
                _currentPageIndex++;
                UpdateChunksAndQr();
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 1. Quitter définitivement : Ctrl + Q
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
            {
                CloseApplication();
                e.Handled = true;
                return;
            }

            // 2. Cacher dans le Tray : Échap
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }

            // 3. Navigation carrousel via Alt + Gauche / Alt + Droite
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (e.SystemKey == Key.Left || e.Key == Key.Left)
                {
                    PreviousPage();
                    e.Handled = true;
                    return;
                }
                if (e.SystemKey == Key.Right || e.Key == Key.Right)
                {
                    NextPage();
                    e.Handled = true;
                    return;
                }
            }

            // 4. Flèche gauche / droite lorsque le focus n'est pas dans le TextBox
            if (!txtSource.IsKeyboardFocused)
            {
                if (e.Key == Key.Left)
                {
                    PreviousPage();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Right)
                {
                    NextPage();
                    e.Handled = true;
                    return;
                }
            }
        }
        #endregion
    }
}
