using System;
using System.Windows.Markup;
using AICTrainer.Services;

namespace AICTrainer.Helpers
{
    [MarkupExtensionReturnType(typeof(System.Windows.Media.Imaging.BitmapSource))]
    public class StickerExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public StickerExtension() { }
        public StickerExtension(string key)
        {
            Key = key;
        }

        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            return StickerManager.Get(Key);
        }
    }
}
