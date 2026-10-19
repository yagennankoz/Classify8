using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Classify8.Core
{
    public sealed class LanguageInfo
    {
        public string Code { get; }
        public string DisplayName { get; }

        public LanguageInfo(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 画面やメッセージの文字列(多言語対応)。
    ///
    /// ・文字列は言語ごとの JSON ファイル (キー → 文字列) から読み込む
    ///   - アプリに埋め込まれたもの (Resources\Lang\ja.json, en.json)
    ///   - 実行ファイルの隣の Languages フォルダにあるもの (言語の追加、既存言語の一部上書きができる)
    /// ・足りないキーは「基準の言語(ja)」→「キー名そのもの」の順に補う
    /// ・文字列の中の {0} {1} は、F() で埋め込む値
    /// </summary>
    public static class Loc
    {
        /// <summary>基準の言語。必ず存在し、他の言語で足りないキーの補完に使う。</summary>
        public const string DefaultLanguage = "ja";

        /// <summary>OS の言語に対応する言語がないときに使う言語。</summary>
        public const string FallbackLanguage = "en";

        /// <summary>設定値: OS の言語に合わせる。</summary>
        public const string AutoSetting = "auto";

        private const string ResourcePrefix = "Classify8.Lang.";
        private const string NameKey = "@name";

        private static readonly object Sync = new object();
        private static string _externalDir;

        private static volatile Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static volatile Dictionary<string, string> _current = new Dictionary<string, string>();
        private static volatile Dictionary<string, string> _default = new Dictionary<string, string>();
        private static string _currentCode = DefaultLanguage;

        /// <summary>表示言語が変わったときに発生する。</summary>
        public static event EventHandler LanguageChanged;

        static Loc()
        {
            Reload();
        }

        public static string CurrentLanguage => _currentCode;

        /// <summary>書式(数値など)に使うカルチャ。</summary>
        public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

        /// <summary>言語ファイルを追加できるフォルダ。既定は実行ファイルの隣の Languages。</summary>
        public static string ExternalLanguageDirectory
        {
            get => _externalDir ?? Path.Combine(AppContext.BaseDirectory, "Languages");
            set => _externalDir = value;
        }

        // ==========================================
        // 読み込み
        // ==========================================

        /// <summary>言語ファイルを読み直す (外部ファイルを追加・変更した後に使う)。</summary>
        public static void Reload()
        {
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            LoadEmbedded(tables);
            LoadExternal(tables);

            lock (Sync)
            {
                _tables = tables;
                ApplyCurrentLanguage();
            }
        }

        private static void LoadEmbedded(Dictionary<string, Dictionary<string, string>> tables)
        {
            var assembly = typeof(Loc).Assembly;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                    !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                string code = name.Substring(ResourcePrefix.Length, name.Length - ResourcePrefix.Length - ".json".Length);
                try
                {
                    using (var stream = assembly.GetManifestResourceStream(name))
                    {
                        tables[code] = ParseJson(stream);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to load the built-in language resource: " + name, ex);
                }
            }
        }

        private static void LoadExternal(Dictionary<string, Dictionary<string, string>> tables)
        {
            string dir = ExternalLanguageDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                string code = Path.GetFileNameWithoutExtension(file);
                // 「_」や「.」で始まるファイル(ひな形など)は読み込まない
                if (code.StartsWith("_", StringComparison.Ordinal) || code.StartsWith(".", StringComparison.Ordinal)) continue;

                try
                {
                    Dictionary<string, string> loaded;
                    using (var stream = File.OpenRead(file))
                    {
                        loaded = ParseJson(stream);
                    }

                    // 同じコードの言語が既にあれば、書かれているキーだけ上書きする。無ければ新しい言語として追加する。
                    var existingKey = tables.Keys.FirstOrDefault(k => k.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (existingKey != null)
                    {
                        foreach (var kv in loaded) tables[existingKey][kv.Key] = kv.Value;
                    }
                    else
                    {
                        tables[code] = loaded;
                    }
                }
                catch (Exception ex)
                {
                    // 壊れた言語ファイルがあっても、アプリは起動できるようにする
                    AppLog.Error("Failed to load the language file: " + file, ex);
                }
            }
        }

        private static Dictionary<string, string> ParseJson(Stream stream)
        {
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip, // 翻訳者がコメントを書けるようにする
                AllowTrailingCommas = true
            };

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(stream, options))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new FormatException("The root of a language file must be a JSON object.");

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String) result[prop.Name] = prop.Value.GetString();
                }
            }
            return result;
        }

        // ==========================================
        // 言語の一覧と切り替え
        // ==========================================

        /// <summary>選べる言語の一覧 (基準の言語、英語、その他(コード順))。</summary>
        public static IReadOnlyList<LanguageInfo> AvailableLanguages
        {
            get
            {
                var tables = _tables;
                return tables
                    .OrderBy(kv => kv.Key.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase) ? 0
                                 : kv.Key.Equals(FallbackLanguage, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new LanguageInfo(kv.Key, kv.Value.TryGetValue(NameKey, out var name) && !string.IsNullOrWhiteSpace(name) ? name : kv.Key))
                    .ToList();
            }
        }

        /// <summary>
        /// 設定値("auto" または言語コード)から、実際に使う言語コードを決める。
        /// 指定された言語が無い場合や "auto" の場合は、OS の言語に合わせる (対応する言語が無ければ英語)。
        /// </summary>
        public static string ResolveLanguage(string setting, CultureInfo uiCulture)
        {
            var codes = _tables.Keys.ToList();

            if (!string.IsNullOrWhiteSpace(setting) && !setting.Equals(AutoSetting, StringComparison.OrdinalIgnoreCase))
            {
                var exact = codes.FirstOrDefault(k => k.Equals(setting, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                // 指定された言語が無い (言語ファイルが削除された等) 場合は、自動判定にする
            }

            string name = uiCulture?.Name ?? "";
            var byName = name.Length > 0 ? codes.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
            if (byName != null) return byName;

            string two = uiCulture?.TwoLetterISOLanguageName ?? "";
            if (two.Length == 2)
            {
                var byTwo = codes.FirstOrDefault(k => k.Equals(two, StringComparison.OrdinalIgnoreCase))
                         ?? codes.FirstOrDefault(k => k.StartsWith(two + "-", StringComparison.OrdinalIgnoreCase));
                if (byTwo != null) return byTwo;
            }

            return codes.FirstOrDefault(k => k.Equals(FallbackLanguage, StringComparison.OrdinalIgnoreCase)) ?? DefaultLanguage;
        }

        /// <summary>設定値("auto" または言語コード)に従って表示言語を決める。</summary>
        public static void Initialize(string setting)
        {
            SetLanguage(ResolveLanguage(setting, CultureInfo.CurrentUICulture));
        }

        /// <summary>表示言語を切り替える。存在しない言語コードの場合は基準の言語になる。</summary>
        /// <returns>言語が実際に変わったか</returns>
        public static bool SetLanguage(string code)
        {
            bool changed;
            lock (Sync)
            {
                var tables = _tables;
                var key = code == null ? null : tables.Keys.FirstOrDefault(k => k.Equals(code, StringComparison.OrdinalIgnoreCase));
                string resolved = key ?? DefaultLanguage;

                changed = !string.Equals(resolved, _currentCode, StringComparison.OrdinalIgnoreCase);
                _currentCode = resolved;
                ApplyCurrentLanguage();
            }

            if (changed) LanguageChanged?.Invoke(null, EventArgs.Empty);
            return changed;
        }

        // Sync のロック内で呼ぶこと
        private static void ApplyCurrentLanguage()
        {
            var tables = _tables;

            var codeKey = tables.Keys.FirstOrDefault(k => k.Equals(_currentCode, StringComparison.OrdinalIgnoreCase));
            if (codeKey == null)
            {
                _currentCode = DefaultLanguage;
                codeKey = tables.Keys.FirstOrDefault(k => k.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                _currentCode = codeKey;
            }

            var defaultKey = tables.Keys.FirstOrDefault(k => k.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase));
            _default = defaultKey != null ? tables[defaultKey] : new Dictionary<string, string>();
            _current = codeKey != null ? tables[codeKey] : _default;

            try { Culture = new CultureInfo(_currentCode); }
            catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
        }

        // ==========================================
        // 文字列の取得
        // ==========================================

        /// <summary>キーに対応する文字列を返す。現在の言語に無ければ基準の言語、それも無ければキー名そのもの。</summary>
        public static string T(string key)
        {
            if (key == null) return "";

            if (_current.TryGetValue(key, out var value)) return value;
            if (_default.TryGetValue(key, out value)) return value;
            return key;
        }

        /// <summary>キーに対応する文字列の {0} {1} ... に、値を埋め込んで返す。</summary>
        public static string F(string key, params object[] args)
        {
            string template = T(key);
            try
            {
                return string.Format(Culture, template, args);
            }
            catch (FormatException)
            {
                // 翻訳の書式が壊れていても、アプリが落ちないようにする
                return template;
            }
        }
    }
}
