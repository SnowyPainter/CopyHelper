using System;
using System.Globalization;
using System.Threading.Tasks;
using CopyHelper.Utilities;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace CopyHelper.Services
{
    public sealed class OcrService
    {
        private readonly OcrEngine _engine;

        public OcrService()
        {
            _engine = TryCreateKoreanEngine() ?? OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))!;
        }

        public async Task<string> ReadTextAsync(Mat mat)
        {
            if (mat.Empty())
            {
                return string.Empty;
            }

            using var bitmap = ImageConversion.ToSoftwareBitmap(mat);
            OcrResult result = await _engine.RecognizeAsync(bitmap);
            return result.Text ?? string.Empty;
        }

        private static OcrEngine? TryCreateKoreanEngine()
        {
            Language? korean = null;
            foreach (Language lang in OcrEngine.AvailableRecognizerLanguages)
            {
                if (lang.LanguageTag.StartsWith("ko", true, CultureInfo.InvariantCulture))
                {
                    korean = lang;
                    break;
                }
            }

            return korean != null && OcrEngine.IsLanguageSupported(korean)
                ? OcrEngine.TryCreateFromLanguage(korean)
                : null;
        }
    }
}
