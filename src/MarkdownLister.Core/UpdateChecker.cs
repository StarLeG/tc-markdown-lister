// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: проверка выхода новой версии плагина по GitHub Releases API.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace MarkdownLister.Core
{
    /// <summary>Результат проверки обновлений.</summary>
    public sealed class UpdateInfo
    {
        /// <summary>Доступна версия новее установленной.</summary>
        public bool UpdateAvailable { get; set; }

        /// <summary>Номер последней версии (три компонента, например <c>0.5.0</c>).</summary>
        public string LatestVersion { get; set; }

        /// <summary>URL страницы релиза.</summary>
        public string ReleaseUrl { get; set; }

        /// <summary>Текст ошибки проверки (пусто — проверка прошла успешно).</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Проверка новых версий плагина по GitHub Releases API.
    /// Единственный сетевой запрос плагина: GET публичного релиза, ничего о пользователе
    /// не отправляется. Выполнять вне UI-потока; ошибки возвращаются в
    /// <see cref="UpdateInfo.Error"/>, исключений не бросает.
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>GitHub API: последний опубликованный релиз репозитория.</summary>
        public const string ReleasesApiUrl =
            "https://api.github.com/repos/StarLeG/tc-markdown-lister/releases/latest";

        /// <summary>Страница релизов (запасной URL).</summary>
        public const string ReleasesPageUrl =
            "https://github.com/StarLeG/tc-markdown-lister/releases/latest";

        /// <summary>Таймаут запроса, мс.</summary>
        private const int TimeoutMs = 8000;

        /// <summary>
        /// Запрашивает последний релиз и сравнивает его версию с текущей.
        /// </summary>
        /// <param name="current">Текущая версия плагина (нормализуется до трёх компонентов).</param>
        public static UpdateInfo Check(Version current)
        {
            var result = new UpdateInfo();
            try
            {
                Version cur = Normalize(current);

                // .NET Framework 4.x: TLS 1.2 обязателен для api.github.com;
                // на старых ОС он может быть выключен в умолчаниях Schannel.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var request = (HttpWebRequest)WebRequest.Create(ReleasesApiUrl);
                request.Method = "GET";
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;
                request.UserAgent = "MarkdownLister/" + cur.ToString(3);
                request.Accept = "application/vnd.github+json";

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();

                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (data == null)
                {
                    result.Error = "empty response";
                    return result;
                }

                string tag = data.ContainsKey("tag_name") ? data["tag_name"] as string : null;
                string url = data.ContainsKey("html_url") ? data["html_url"] as string : null;

                Version latest;
                if (!TryParseVersion(tag, out latest))
                {
                    result.Error = "bad version tag: " + (tag ?? "(null)");
                    return result;
                }

                result.LatestVersion = latest.ToString(3);
                result.ReleaseUrl = string.IsNullOrEmpty(url) ? ReleasesPageUrl : url;
                result.UpdateAvailable = latest > cur;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>Разбирает номер версии из тега релиза (<c>v0.5.0</c>, <c>0.5.0</c>).</summary>
        /// <param name="tag">Тег релиза.</param>
        /// <param name="version">Разобранная версия.</param>
        public static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            string s = tag.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);

            // Предрелизные суффиксы и метаданные сборки в сравнении не участвуют
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut > 0)
                s = s.Substring(0, cut);

            Version parsed;
            if (!Version.TryParse(s, out parsed))
                return false;

            version = Normalize(parsed);
            return true;
        }

        /// <summary>Приводит версию к трём компонентам (недостающие — нули).</summary>
        private static Version Normalize(Version v)
        {
            if (v == null)
                return new Version(0, 0, 0);
            return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build < 0 ? 0 : v.Build));
        }
    }
}
