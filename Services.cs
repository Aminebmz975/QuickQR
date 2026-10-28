using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using QRCoder;

using Clipboard = System.Windows.Clipboard;

namespace QuickQR
{
    #region Win32 Interop Service
    /// <summary>
    /// Service d'interopérabilité Win32 natif pour l'interception des messages système :
    /// - Raccourci global système (RegisterHotKey / UnregisterHotKey)
    /// - Écoute événementielle du presse-papiers sans polling (AddClipboardFormatListener / RemoveClipboardFormatListener)
    /// - Gestion concurrente avec retry sur verrouillage transitoire du presse-papiers.
    /// </summary>
    public sealed class Win32InteropService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_Q = 0x51;

        private const int HOTKEY_ID = 9001;
        private const int CLIPBRD_E_CANT_OPEN = unchecked((int)0x800401D0);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

        private IntPtr _hWnd = IntPtr.Zero;
        private HwndSource? _hwndSource;
        private bool _isHotKeyRegistered;
        private bool _isClipboardListenerRegistered;
        private bool _disposed;

        public event Action? HotKeyPressed;
        public event Action? ClipboardUpdated;

        /// <summary>
        /// Attache les écoutes Win32 à la boucle de messages WndProc de la fenêtre WPF.
        /// </summary>
        public void Attach(IntPtr hWnd)
        {
            if (_hWnd != IntPtr.Zero)
            {
                Detach();
            }

            _hWnd = hWnd;
            _hwndSource = HwndSource.FromHwnd(hWnd);
            _hwndSource?.AddHook(WndProc);

            // Enregistrement du raccourci global Ctrl + Alt + Q
            _isHotKeyRegistered = RegisterHotKey(_hWnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_Q);

            // Enregistrement de l'écouteur d'événements presse-papiers
            _isClipboardListenerRegistered = AddClipboardFormatListener(_hWnd);
        }

        /// <summary>
        /// Détache les écoutes et libère les hooks Win32.
        /// </summary>
        public void Detach()
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            if (_isHotKeyRegistered && _hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID);
                _isHotKeyRegistered = false;
            }

            if (_isClipboardListenerRegistered && _hWnd != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_hWnd);
                _isClipboardListenerRegistered = false;
            }

            _hWnd = IntPtr.Zero;
        }

        /// <summary>
        /// Boucle de messages WndProc interceptant WM_HOTKEY et WM_CLIPBOARDUPDATE.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HotKeyPressed?.Invoke();
                handled = true;
            }
            else if (msg == WM_CLIPBOARDUPDATE)
            {
                ClipboardUpdated?.Invoke();
                handled = true;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Récupère le texte du presse-papiers Windows de manière résiliente face aux accès concurrents
        /// (3 tentatives espacées de 20 ms en cas de COMException CLIPBRD_E_CANT_OPEN).
        /// </summary>
        public static bool TryGetClipboardText(out string text)
        {
            text = string.Empty;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        text = Clipboard.GetText();
                        return true;
                    }
                    return false;
                }
                catch (COMException ex) when (ex.ErrorCode == CLIPBRD_E_CANT_OPEN || attempt < 2)
                {
                    Thread.Sleep(20);
                }
                catch (Exception)
                {
                    if (attempt == 2) return false;
                    Thread.Sleep(20);
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Detach();
                _disposed = true;
            }
        }
    }
    #endregion

    #region QR Generator Service
    /// <summary>
    /// Service de génération de QR Code haute performance et haute lisibilité :
    /// - Utilise QRCoder.PngByteQRCode sans dépendance GDI+ lourde
    /// - Correction d'erreur adaptative (M pour &lt; 150 car., L pour &gt;= 150 car.)
    /// - Quiet Zone forcée (au moins 4 modules de marge blanche)
    /// - Rendu UTF-8 garanti sans BOM parasite
    /// - Conversion directe en BitmapImage WPF mise en cache OnLoad et figée (Freeze)
    /// </summary>
    public sealed class QrGeneratorService
    {
        private readonly QRCodeGenerator _generator = new();

        public BitmapImage GenerateQrBitmap(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                text = " "; // Garantit un QR Code blanc/minimal si le texte est vide
            }

            // Correction d'erreur adaptative : M (15%) pour les courts textes, L (7%) pour minimiser la densité des longs textes
            QRCodeGenerator.ECCLevel ecc = text.Length < 150 
                ? QRCodeGenerator.ECCLevel.M 
                : QRCodeGenerator.ECCLevel.L;

            using QRCodeData qrData = _generator.CreateQrCode(text, ecc, forceUtf8: true, utf8BOM: false);
            using PngByteQRCode qrCode = new(qrData);

            // 20 pixels par module avec marge blanche Quiet Zone de 4 modules
            byte[] pngBytes = qrCode.GetGraphic(pixelsPerModule: 20, drawQuietZones: true);

            using var memoryStream = new MemoryStream(pngBytes);
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Libération immédiate du flux sous-jacent
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            bitmap.Freeze(); // Rendu immuable et performant pour le thread UI WPF

            return bitmap;
        }
    }
    #endregion

    #region Text Chunker Service
    /// <summary>
    /// Service de découpage intelligent du texte pour le carrousel multi-QR :
    /// - Respect absolu de l'intégrité des données (Zéro injection parasite dans le texte)
    /// - Découpe contextuelle : cherche le dernier délimiteur dans les 40 derniers caractères pour éviter de couper un mot
    /// - Découpe nette au seuil pour les tokens longs sans espaces (URLs, tokens JWT, base64)
    /// </summary>
    public static class TextChunkerService
    {
        private static readonly char[] Delimiters = { ' ', '\n', '\r', '\t' };

        public static List<string> SplitIntoChunks(string text, int maxChunkSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new List<string> { string.Empty };
            }

            if (maxChunkSize <= 0)
            {
                maxChunkSize = 400;
            }

            if (text.Length <= maxChunkSize)
            {
                return new List<string> { text };
            }

            List<string> chunks = new();
            int start = 0;
            int totalLength = text.Length;

            while (start < totalLength)
            {
                int remaining = totalLength - start;
                if (remaining <= maxChunkSize)
                {
                    chunks.Add(text.Substring(start, remaining));
                    break;
                }

                int candidateLength = maxChunkSize;
                int searchWindow = Math.Min(40, candidateLength);
                int searchStart = start + candidateLength - searchWindow;

                // Chercher le dernier délimiteur dans la fenêtre de 40 caractères
                int lastDelimIndex = text.LastIndexOfAny(Delimiters, start + candidateLength - 1, searchWindow);

                int chunkLength;
                if (lastDelimIndex >= searchStart)
                {
                    // Conserver le délimiteur à la fin du segment
                    chunkLength = (lastDelimIndex - start) + 1;
                }
                else
                {
                    // Aucun délimiteur proche : couper au seuil exact
                    chunkLength = candidateLength;
                }

                chunks.Add(text.Substring(start, chunkLength));
                start += chunkLength;
            }

            return chunks;
        }
    }
    #endregion
}
