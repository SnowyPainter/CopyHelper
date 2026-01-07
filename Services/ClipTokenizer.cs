using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CopyHelper.Services
{
    public sealed class ClipTokenizer
    {
        private static readonly Regex TokenPattern = new Regex(
            "'s|'t|'re|'ve|'m|'ll|'d| ?\\p{L}+| ?\\p{N}+| ?[^\\s\\p{L}\\p{N}]+|\\s+(?!\\S)|\\s+",
            RegexOptions.Compiled);

        private readonly Dictionary<string, int> _vocab;
        private readonly Dictionary<string, int> _bpeRanks;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private readonly Dictionary<byte, char> _byteEncoder;
        private readonly int _startId;
        private readonly int _endId;

        public ClipTokenizer(string tokenizerPath)
        {
            (_vocab, _bpeRanks) = LoadTokenizer(tokenizerPath);
            _byteEncoder = BuildByteEncoder();

            _vocab.TryGetValue("<|startoftext|>", out _startId);
            _vocab.TryGetValue("<|endoftext|>", out _endId);
        }

        public (long[] ids, long[] attention) Encode(string text, int maxTokens)
        {
            long[] ids = new long[maxTokens];
            long[] attention = new long[maxTokens];
            int index = 0;

            if (_startId != 0 && index < maxTokens)
            {
                ids[index] = _startId;
                attention[index] = 1;
                index++;
            }

            foreach (string token in Tokenize(text))
            {
                if (index >= maxTokens)
                {
                    break;
                }

                if (_vocab.TryGetValue(token, out int id))
                {
                    ids[index] = id;
                    attention[index] = 1;
                    index++;
                }
            }

            if (_endId != 0 && index < maxTokens)
            {
                ids[index] = _endId;
                attention[index] = 1;
            }

            return (ids, attention);
        }

        private IEnumerable<string> Tokenize(string text)
        {
            foreach (Match match in TokenPattern.Matches(text))
            {
                string value = match.Value;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                string encoded = EncodeBytes(value);
                string bpe = Bpe(encoded);
                foreach (string part in bpe.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return part;
                }
            }
        }

        private string EncodeBytes(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            char[] chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = _byteEncoder[bytes[i]];
            }

            return new string(chars);
        }

        private string Bpe(string token)
        {
            if (_cache.TryGetValue(token, out string cached))
            {
                return cached;
            }

            List<string> word = token.Select(c => c.ToString()).ToList();
            if (word.Count == 1)
            {
                _cache[token] = token;
                return token;
            }

            while (true)
            {
                int minRank = int.MaxValue;
                string? bestA = null;
                string? bestB = null;

                for (int i = 0; i < word.Count - 1; i++)
                {
                    string key = PairKey(word[i], word[i + 1]);
                    if (_bpeRanks.TryGetValue(key, out int rank) && rank < minRank)
                    {
                        minRank = rank;
                        bestA = word[i];
                        bestB = word[i + 1];
                    }
                }

                if (bestA == null || bestB == null)
                {
                    break;
                }

                List<string> newWord = new List<string>();
                int index = 0;
                while (index < word.Count)
                {
                    if (index < word.Count - 1 && word[index] == bestA && word[index + 1] == bestB)
                    {
                        newWord.Add(bestA + bestB);
                        index += 2;
                    }
                    else
                    {
                        newWord.Add(word[index]);
                        index++;
                    }
                }

                word = newWord;
                if (word.Count == 1)
                {
                    break;
                }
            }

            string result = string.Join(" ", word);
            _cache[token] = result;
            return result;
        }

        private static string PairKey(string a, string b) => $"{a}\u0001{b}";

        private static (Dictionary<string, int> vocab, Dictionary<string, int> merges) LoadTokenizer(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream);
            JsonElement model = doc.RootElement.GetProperty("model");

            Dictionary<string, int> vocab = new Dictionary<string, int>();
            foreach (JsonProperty item in model.GetProperty("vocab").EnumerateObject())
            {
                vocab[item.Name] = item.Value.GetInt32();
            }

            Dictionary<string, int> merges = new Dictionary<string, int>();
            int rank = 0;
            foreach (JsonElement merge in model.GetProperty("merges").EnumerateArray())
            {
                string? value = merge.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                merges[PairKey(parts[0], parts[1])] = rank++;
            }

            return (vocab, merges);
        }

        private static Dictionary<byte, char> BuildByteEncoder()
        {
            List<int> bytes = new List<int>();
            for (int i = 33; i <= 126; i++) bytes.Add(i);
            for (int i = 161; i <= 172; i++) bytes.Add(i);
            for (int i = 174; i <= 255; i++) bytes.Add(i);

            List<int> chars = new List<int>(bytes);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (!bytes.Contains(b))
                {
                    bytes.Add(b);
                    chars.Add(256 + n);
                    n++;
                }
            }

            Dictionary<byte, char> map = new Dictionary<byte, char>();
            for (int i = 0; i < bytes.Count; i++)
            {
                map[(byte)bytes[i]] = (char)chars[i];
            }

            return map;
        }
    }
}
