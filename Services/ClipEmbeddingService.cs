using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CopyHelper.Services
{
    public sealed class ClipEmbeddingService : IDisposable
    {
        private readonly InferenceSession _textSession;
        private readonly InferenceSession _imageSession;
        private readonly ClipTokenizer _tokenizer;

        private const int ImageSize = 224;
        private const int MaxTokens = 77;

        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

        public ClipEmbeddingService(string baseDirectory)
        {
            string clipDir = Path.Combine(baseDirectory, "CLIP");
            string textModel = Path.Combine(clipDir, "clip_text_encoder.onnx");
            string imageModel = Path.Combine(clipDir, "clip_image_encoder.onnx");
            string tokenizerPath = Path.Combine(clipDir, "tokenizer.json");

            _textSession = new InferenceSession(textModel);
            _imageSession = new InferenceSession(imageModel);
            _tokenizer = new ClipTokenizer(tokenizerPath);
        }

        public float[] EncodeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<float>();
            }

            (long[] inputIds, long[] attention) = _tokenizer.Encode(text, MaxTokens);

            DenseTensor<long> inputTensor = new DenseTensor<long>(inputIds, new[] { 1, MaxTokens });
            DenseTensor<long> attentionTensor = new DenseTensor<long>(attention, new[] { 1, MaxTokens });

            List<NamedOnnxValue> inputs = new List<NamedOnnxValue>();
            foreach (string name in _textSession.InputMetadata.Keys)
            {
                if (name.Contains("attention", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, attentionTensor));
                }
                else
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, inputTensor));
                }
            }

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _textSession.Run(inputs);
            float[] embedding = ExtractEmbedding(results, attention);
            NormalizeInPlace(embedding);
            return embedding;
        }

        public float[] EncodeImage(BitmapSource source)
        {
            DenseTensor<float> input = BuildImageTensor(source);
            string inputName = _imageSession.InputMetadata.Keys.First();

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                _imageSession.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });

            float[] embedding = ExtractEmbedding(results, null);
            NormalizeInPlace(embedding);
            return embedding;
        }

        private static DenseTensor<float> BuildImageTensor(BitmapSource source)
        {
            BitmapSource rgb = new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0);
            TransformedBitmap resized = new TransformedBitmap(rgb, new ScaleTransform(
                ImageSize / (double)rgb.PixelWidth, ImageSize / (double)rgb.PixelHeight));

            int stride = ImageSize * 3;
            byte[] pixels = new byte[ImageSize * ImageSize * 3];
            resized.CopyPixels(pixels, stride, 0);

            float[] data = new float[1 * 3 * ImageSize * ImageSize];
            int offsetR = 0;
            int offsetG = ImageSize * ImageSize;
            int offsetB = 2 * ImageSize * ImageSize;

            for (int y = 0; y < ImageSize; y++)
            {
                int row = y * stride;
                for (int x = 0; x < ImageSize; x++)
                {
                    int idx = row + x * 3;
                    float r = pixels[idx] / 255f;
                    float g = pixels[idx + 1] / 255f;
                    float b = pixels[idx + 2] / 255f;

                    int pix = y * ImageSize + x;
                    data[offsetR + pix] = (r - Mean[0]) / Std[0];
                    data[offsetG + pix] = (g - Mean[1]) / Std[1];
                    data[offsetB + pix] = (b - Mean[2]) / Std[2];
                }
            }

            return new DenseTensor<float>(data, new[] { 1, 3, ImageSize, ImageSize });
        }

        private static float[] ExtractEmbedding(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, long[]? attention)
        {
            foreach (DisposableNamedOnnxValue item in results)
            {
                if (item.Value is DenseTensor<float> tensor)
                {
                    if (tensor.Rank == 2)
                    {
                        return tensor.ToArray();
                    }

                    if (tensor.Rank == 3)
                    {
                        int seqLen = tensor.Dimensions[1];
                        int hidden = tensor.Dimensions[2];
                        float[] output = new float[hidden];
                        int count = 0;

                        for (int i = 0; i < seqLen; i++)
                        {
                            if (attention != null && i < attention.Length && attention[i] == 0)
                            {
                                continue;
                            }

                            int baseIndex = i * hidden;
                            for (int h = 0; h < hidden; h++)
                            {
                                output[h] += tensor.Buffer.Span[baseIndex + h];
                            }
                            count++;
                        }

                        if (count > 0)
                        {
                            for (int h = 0; h < hidden; h++)
                            {
                                output[h] /= count;
                            }
                        }

                        return output;
                    }
                }
            }

            return Array.Empty<float>();
        }

        private static void NormalizeInPlace(float[] vector)
        {
            if (vector.Length == 0)
            {
                return;
            }

            float sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }

            float norm = (float)Math.Sqrt(sum);
            if (norm <= 0)
            {
                return;
            }

            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        public void Dispose()
        {
            _textSession.Dispose();
            _imageSession.Dispose();
        }
    }
}
