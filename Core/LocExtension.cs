using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Classify8.Core;

namespace Classify8.Localization
{
    /// <summary>
    /// XAML のバインディング元。表示言語が変わると、バインドされている文字列がすべて更新される。
    /// </summary>
    public sealed class LocSource : INotifyPropertyChanged
    {
        public static LocSource Instance { get; } = new LocSource();

        public event PropertyChangedEventHandler PropertyChanged;

        private LocSource()
        {
            Loc.LanguageChanged += (s, e) => RaiseChanged();
        }

        public string this[string key] => Loc.T(key);

        private void RaiseChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(RaiseChanged));
                return;
            }

            // インデクサ全体の変更を通知する → 「{loc:Tr ...}」でバインドされた文字列がすべて再取得される
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    /// <summary>
    /// XAML から多言語の文字列を使うためのマークアップ拡張。
    /// 例: Content="{loc:Tr common.cancel}"  (キーは Resources\Lang\*.json に定義する)
    /// 表示言語を切り替えると、再起動しなくても表示が切り替わる。
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public class TrExtension : MarkupExtension
    {
        [ConstructorArgument("key")]
        public string Key { get; set; }

        public TrExtension()
        {
        }

        public TrExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = LocSource.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
