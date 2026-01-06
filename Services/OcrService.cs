using System;
using System.Drawing;
using System.IO;
using CopyHelper.Utilities;
using OpenCvSharp;
using Tesseract;

namespace CopyHelper.Services
{
    public sealed class OcrService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly TesseractEngine _engine;

        public OcrService(string dataPath, string languages)
        {
            _engine = new TesseractEngine(dataPath, languages, EngineMode.Default);
            _engine.DefaultPageSegMode = PageSegMode.Auto;
            _engine.SetVariable("preserve_interword_spaces", "1");
        }

        public string ReadText(Mat mat)
        {
            if (mat.Empty())
            {
                return string.Empty;
            }

            using Bitmap bitmap = ImageConversion.ToBitmap(mat);
            using MemoryStream stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            using Pix pix = Pix.LoadFromMemory(stream.ToArray());
            lock (_sync)
            {
                using Page page = _engine.Process(pix);
                return page.GetText() ?? string.Empty;
            }
        }

        public void Dispose()
        {
            _engine.Dispose();
        }
    }
}
