using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AICTrainer.Services
{
    public static class StickerManager
    {
        private static BitmapImage? _sheet;
        private static readonly Dictionary<string, BitmapSource> _cache = new();

        private static readonly Dictionary<string, Int32Rect> Rects = new()
        {
            ["0_0"] = new Int32Rect(27, 31, 179, 180),
            ["0_1"] = new Int32Rect(229, 33, 178, 178),
            ["0_2"] = new Int32Rect(455, 33, 167, 176),
            ["0_3"] = new Int32Rect(661, 34, 176, 177),
            ["0_4"] = new Int32Rect(895, 31, 166, 180),
            ["0_5"] = new Int32Rect(1074, 30, 171, 181),
            ["0_6"] = new Int32Rect(1291, 29, 180, 182),
            ["0_7"] = new Int32Rect(1521, 30, 174, 181),
            ["1_0"] = new Int32Rect(29, 246, 176, 177),
            ["1_1"] = new Int32Rect(228, 247, 158, 177),
            ["1_2"] = new Int32Rect(441, 246, 184, 178),
            ["1_3"] = new Int32Rect(663, 246, 174, 177),
            ["1_4"] = new Int32Rect(893, 249, 159, 178),
            ["1_5"] = new Int32Rect(1104, 249, 173, 178),
            ["1_6"] = new Int32Rect(1300, 246, 188, 182),
            ["1_7"] = new Int32Rect(1528, 245, 182, 181),
            ["2_0"] = new Int32Rect(31, 463, 174, 171),
            ["2_1"] = new Int32Rect(240, 463, 172, 171),
            ["2_2"] = new Int32Rect(448, 463, 177, 171),
            ["2_3"] = new Int32Rect(652, 463, 176, 173),
            ["2_4"] = new Int32Rect(881, 463, 180, 174),
            ["2_5"] = new Int32Rect(1101, 463, 173, 173),
            ["2_6"] = new Int32Rect(1316, 463, 174, 174),
            ["2_7"] = new Int32Rect(1536, 460, 184, 178),
            ["3_0"] = new Int32Rect(31, 668, 171, 176),
            ["3_1"] = new Int32Rect(240, 668, 172, 175),
            ["3_2"] = new Int32Rect(450, 667, 171, 176),
            ["3_3"] = new Int32Rect(654, 670, 172, 174),
            ["3_4"] = new Int32Rect(879, 669, 177, 175),
            ["3_5"] = new Int32Rect(1098, 669, 178, 177),
            ["3_6"] = new Int32Rect(1314, 669, 179, 175),
            ["3_7"] = new Int32Rect(1536, 666, 184, 180)
        };

        private static readonly object _lock = new();

        private static void EnsureSheet()
        {
            if (_sheet != null) return;
            lock (_lock)
            {
                if (_sheet != null) return;
                try
                {
                    var uri1 = new Uri("pack://application:,,,/AICTrainer;component/Resources/stickers_sheet.png", UriKind.Absolute);
                    var sri = Application.GetResourceStream(uri1);
                    if (sri == null)
                    {
                        var uri2 = new Uri("pack://application:,,,/Resources/stickers_sheet.png", UriKind.Absolute);
                        sri = Application.GetResourceStream(uri2);
                    }

                    if (sri != null)
                    {
                        using var stream = sri.Stream;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = stream;
                        bi.EndInit();
                        bi.Freeze();
                        _sheet = bi;
                    }
                    else
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri("pack://application:,,,/Resources/stickers_sheet.png", UriKind.Absolute);
                        bi.EndInit();
                        bi.Freeze();
                        _sheet = bi;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StickerManager] Failed to load sticker sheet: {ex}");
                }
            }
        }

        public static BitmapSource? Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            try
            {
                EnsureSheet();
                if (_sheet != null && Rects.TryGetValue(key, out var rect))
                {
                    var cropped = new CroppedBitmap(_sheet, rect);
                    cropped.Freeze();
                    _cache[key] = cropped;
                    return cropped;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StickerManager] Error cropping {key}: {ex.Message}");
            }
            return null;
        }

        public static BitmapSource? Get(int row, int col) => Get($"{row}_{col}");
    }
}
