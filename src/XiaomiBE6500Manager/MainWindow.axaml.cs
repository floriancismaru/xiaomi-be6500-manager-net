using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using XiaomiBE6500Manager.Core;

namespace XiaomiBE6500Manager;

public partial class MainWindow : Window
{
    sealed record BrowserLang(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    readonly Settings _settings = Settings.Load();
    RouterProxy? _proxy;
    NativeWebView? _web;
    bool _busy, _updating;

    AppLang Lang => AppLang.ByCode(_settings.Lang);

    public MainWindow()
    {
        InitializeComponent();
        IpBox.Text = _settings.Ip;
        LangBox.ItemsSource = AppLang.All;
        LangBox.SelectedItem = Lang;
        LangBox.SelectionChanged += (_, _) =>
        {
            if (LangBox.SelectedItem is not AppLang l) return;
            _settings.Lang = l.Code;
            _settings.Save();
            ApplyTexts();
        };
        ConnectButton.Click += async (_, _) => await ConnectAsync();
        IpBox.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await ConnectAsync(); };
        HomeButton.Click += async (_, _) => await GoHomeAsync();
        BackButton.Click += (_, _) => _web?.GoBack();
        ForwardButton.Click += (_, _) => _web?.GoForward();
        ReloadButton.Click += (_, _) => _web?.Refresh();
        BrowserLangBox.SelectionChanged += (_, _) => OnBrowserLang();
        Closing += (_, _) => _proxy?.DisposeAsync().AsTask().Wait(2000);
        ApplyTexts();
    }

    void ApplyTexts()
    {
        var l = Lang;
        IpLabel.Text = Strings.Tr(l, "routerIp");
        LangLabel.Text = Strings.Tr(l, "language");
        ConnectButton.Content = Strings.Tr(l, _busy ? "connecting" : "connect");
        TitleText.Text = Strings.Tr(l, "title");
        ToolTip.SetTip(HomeButton, Strings.Tr(l, "home"));
        ToolTip.SetTip(BackButton, Strings.Tr(l, "back"));
        ToolTip.SetTip(ForwardButton, Strings.Tr(l, "forward"));
        ToolTip.SetTip(ReloadButton, Strings.Tr(l, "reload"));
        UpdateMissing();

        _updating = true;
        var code = _proxy?.Lang ?? l.Code;
        BrowserLangBox.ItemsSource = new[]
        {
            new BrowserLang("zh", Strings.Tr(l, "original")),
            new BrowserLang("en", AppLang.En.Label),
            new BrowserLang("ro", AppLang.Ro.Label),
        };
        BrowserLangBox.SelectedIndex = code switch { "zh" => 0, "ro" => 2, _ => 1 };
        _updating = false;
    }

    void ShowError(string? key)
    {
        ErrorText.IsVisible = key != null;
        ErrorText.Text = key == null ? null : Strings.Tr(Lang, key);
    }

    async Task ConnectAsync()
    {
        if (_busy) return;
        var ip = (IpBox.Text ?? "").Trim();
        if (!IpValidation.IsPrivateIpv4(ip)) { ShowError("invalidIp"); return; }
        ShowError(null);
        _busy = true;
        ApplyTexts();
        try
        {
            _settings.Ip = ip;
            _settings.Save();
            using (var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                using (await probe.GetAsync($"http://{ip}/cgi-bin/luci/web")) { }

            if (_proxy != null) await _proxy.DisposeAsync();
            _proxy = new RouterProxy(ip, _settings.Lang, TranslationAssets.Dictionary, TranslationAssets.Translator,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "XiaomiBE6500Manager", "untranslated.json"));
            await _proxy.StartAsync();
            _proxy.MissingChanged += () => Dispatcher.UIThread.Post(UpdateMissing);

            var web = new NativeWebView();
            // Keep the web engine's data out of the persistent profile: the router session ends with the proxy.
            web.EnvironmentRequested += (_, e) => { if (e is GtkWebViewEnvironmentRequestedEventArgs g) g.EphemeralDataManager = true; };
            _web = web;
            WebHost.Content = web;
            HomePanel.IsVisible = false;
            BrowserPanel.IsVisible = true;
            ApplyTexts();
            web.Navigate(new Uri(_proxy.EntryUrl));
        }
        catch
        {
            ShowError("unreachable");
        }
        finally
        {
            _busy = false;
            ApplyTexts();
        }
    }

    /// <summary>Back to the home screen: tears down the web view and the proxy, which discards the router session.</summary>
    async Task GoHomeAsync()
    {
        WebHost.Content = null;
        (_web as IDisposable)?.Dispose();
        _web = null;
        var p = _proxy;
        _proxy = null;
        if (p != null) await p.DisposeAsync();
        BrowserPanel.IsVisible = false;
        HomePanel.IsVisible = true;
        ApplyTexts();
    }

    /// <summary>Applies a language to the open page now; later pages get it from the proxy.</summary>
    void OnBrowserLang()
    {
        if (_updating || _proxy == null || BrowserLangBox.SelectedItem is not BrowserLang b) return;
        _proxy.Lang = b.Code;
        _ = _web?.InvokeScript($"window.__XT && __XT.setLang('{b.Code}')");
        if (b.Code != "zh")
        {
            _settings.Lang = b.Code;
            _settings.Save();
            _updating = true;
            LangBox.SelectedItem = Lang;
            _updating = false;
            ApplyTexts();
        }
    }

    void UpdateMissing()
    {
        var n = _proxy?.MissingCount ?? 0;
        MissingText.Text = string.Format(Strings.Tr(Lang, "untranslated"), n);
        ToolTip.SetTip(MissingText, string.Format(Strings.Tr(Lang, "untranslatedTip"), n, _proxy?.MissingFile));
    }
}
