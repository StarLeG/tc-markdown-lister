// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: определение кодировки текстовых файлов.

using System.Text;
using UtfUnknown;

namespace MarkdownLister.Core
{
    /// <summary>Определяет кодировку файла (UTF-8, UTF-16, ANSI) и декодирует его содержимое.</summary>
    public static class EncodingDetector
    {
        /// <summary>Максимальный размер префикса, анализируемого для определения кодировки.</summary>
        private const int DetectionSampleSize = 64 * 1024;

        /// <summary>Определяет кодировку и возвращает декодированный текст.</summary>
        /// <param name="bytes">Содержимое файла.</param>
        /// <param name="encoding">Распознанная кодировка.</param>
        /// <returns>Декодированный текст.</returns>
        public static string Decode(byte[] bytes, out Encoding encoding)
        {
            if (bytes == null || bytes.Length == 0)
            {
                encoding = Encoding.UTF8;
                return string.Empty;
            }

            // 1. BOM — надёжный признак.
            encoding = DetectByBom(bytes, out int bomLength);
            if (encoding != null)
                return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);

            // 2. UTF.Unknown (порт Mozilla Universal Charset Detector) — без BOM и ANSI.
            try
            {
                var sample = bytes.Length > DetectionSampleSize
                    ? new byte[DetectionSampleSize]
                    : bytes;
                if (sample.Length < bytes.Length)
                    System.Array.Copy(bytes, sample, sample.Length);

                var detection = CharsetDetector.DetectFromBytes(sample);
                if (detection?.Detected != null && detection.Detected.Confidence > 0.2f)
                {
                    encoding = detection.Detected.Encoding;
                    return encoding.GetString(bytes);
                }
            }
            catch
            {
                // Падение детектора не должно ронять Lister — используем UTF-8 ниже.
            }

            // 3. Фолбэк: UTF-8 с заменой невалидных последовательностей.
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            return encoding.GetString(bytes);
        }

        /// <summary>Определяет кодировку по BOM.</summary>
        /// <returns>Кодировка или <c>null</c>, если BOM отсутствует.</returns>
        private static Encoding DetectByBom(byte[] bytes, out int bomLength)
        {
            bomLength = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bomLength = 3;
                return Encoding.UTF8;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                bomLength = 2;
                return Encoding.Unicode;          // UTF-16 LE
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                bomLength = 2;
                return Encoding.BigEndianUnicode; // UTF-16 BE
            }
            return null;
        }
    }
}
