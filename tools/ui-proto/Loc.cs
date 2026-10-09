using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace UiProto;

/// <summary>
/// Локализация интерфейса прототипа.
///
/// Ключ — исходная английская строка (ровно то, что стоит в XAML или в коде),
/// значение — перевод на языки из Global.LanguageOptions. Сам XAML не трогаем:
/// <see cref="Apply"/> обходит дерево окна, запоминает оригинал в attached-свойстве
/// и подменяет Text/Content/ToolTip — поэтому смена языка обратима и безопасна
/// для строк, которые код меняет на лету (они перерисовываются отдельно).
/// Отсутствующий в словаре ключ возвращает английский оригинал.
/// </summary>
public static class Loc
{
    /// <summary>Порядок переводов внутри каждого массива словаря.</summary>
    private static readonly string[] Order = ["ru", "zh-Hans", "zh-Hant", "fr", "hu", "id", "az", "fa"];

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "ru", "zh-Hans", "zh-Hant", "fr", "hu", "id", "az", "fa",
    };

    /// <summary>Текущий язык интерфейса (en — оригинал строк).</summary>
    public static string Lang { get; private set; } = "en";

    /// <summary>Язык, применённый последним Apply — чтобы не перетирать текст, назначенный кодом.</summary>
    private static string? _applied;

    private static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.RegisterAttached("SourceText", typeof(string), typeof(Loc));

    private static readonly DependencyProperty SourceTipProperty =
        DependencyProperty.RegisterAttached("SourceTip", typeof(string), typeof(Loc));

    /// <summary>Нормализовать код языка: неизвестный → en.</summary>
    public static string Normalize(string? code)
        => !string.IsNullOrWhiteSpace(code) && Known.Contains(code!) ? code! : "en";

    /// <summary>Задать язык (без применения к дереву — см. Apply).</summary>
    public static void SetLang(string? code) => Lang = Normalize(code);

    /// <summary>Перевод строки с текущим языком; ключ — английский оригинал.</summary>
    public static string T(string? english) => Translate(english, Lang);

    /// <summary>Формат вида "{0} rules" под текущий язык.</summary>
    public static string TF(string? english, params object[] args)
        => string.Format(T(english), args);

    private static string Translate(string? english, string lang)
    {
        if (string.IsNullOrEmpty(english))
            return string.Empty;

        var source = english!;
        if (lang == "en" || !Table.TryGetValue(source, out var rows))
            return source;

        var idx = System.Array.IndexOf(Order, lang);
        if (idx < 0 || idx >= rows.Length || string.IsNullOrEmpty(rows[idx]))
            return source;

        return rows[idx];
    }

    /// <summary>
    /// Применить язык ко всему дереву окна (включая содержимое Popup).
    /// Повторный вызов с тем же языком ничего не меняет; при смене языка
    /// переписываются только строки, не изменённые кодом.
    /// </summary>
    public static void Apply(DependencyObject root)
    {
        if (root is null) return;
        var prev = _applied ?? Lang;
        TraceLog($"APPLY lang={Lang} prev={prev} applied={_applied ?? "-"}");
        Visit(root, prev, []);
        _applied = Lang;
    }

    /// <summary>Диагностика локализации: UIPROTO_LOC=1 → %TEMP%\opencode\loc.log.</summary>
    private static void TraceLog(string line)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("UIPROTO_LOC") != "1") return;
            var path = Path.Combine(Path.GetTempPath(), "opencode", "loc.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\r\n");
        }
        catch
        {
            // диагностика не должна ломать приложение
        }
    }

    /// <summary>Сброс «последнего применённого языка» — используется, когда текст задаёт код.</summary>
    public static void MarkApplied() => _applied = Lang;

    /// <summary>Значение пришло из Binding — код меняет его сам, переводить нельзя.</summary>
    private static bool IsBindingValue(object? value)
        => value is BindingExpression or MultiBindingExpression or PriorityBindingExpression;

    /// <summary>
    /// Binding, включая шаблонный {Binding}: у элементов DataTemplate ReadLocalValue
    /// отдаёт UnsetValue, но GetBindingExpression выражение находит. Локальная запись
    /// в такой ячейке отвязала бы шаблон — текст навсегда замер на старом значении.
    /// </summary>
    private static bool IsDataBoundValue(FrameworkElement fe, DependencyProperty dp)
        => IsBindingValue(fe.ReadLocalValue(dp)) || fe.GetBindingExpression(dp) is not null;

    /// <summary>
    /// Элемент настоящий, а не зеркало Content внутри ControlTemplate: либо он вне
    /// шаблона, либо это контент элемента списка (DataTemplate → ContentPresenter).
    /// </summary>
    private static bool IsTemplateContent(DependencyObject? templatedParent)
        => templatedParent is null or ContentPresenter;

    private static void Visit(DependencyObject node, string prev, HashSet<DependencyObject> seen)
    {
        if (node is null || !seen.Add(node))
            return;

        try
        {
            if (node is TextBlock tb)
            {
                // Литерал (XAML Text="..." или запись из кода) плюс текст из шаблона:
                // у элементов DataTemplate ReadLocalValue отдаёт UnsetValue, но сам
                // текст настоящий — считаем его исходной строкой. Binding не трогаем:
                // запись локального значения отвязала бы шаблон. Шаблонные TextBlock'ы
                // внутри ControlTemplate (TemplatedParent = RadioButton и т.п.) — это
                // зеркало Content, его переводит ветка ContentControl.
                if (IsTemplateContent(tb.TemplatedParent))
                {
                    var local = tb.ReadLocalValue(TextBlock.TextProperty);
                    if (!IsDataBoundValue(tb, TextBlock.TextProperty))
                    {
                        var source = (string?)tb.GetValue(SourceTextProperty) ?? local as string ?? tb.Text;
                        tb.SetValue(SourceTextProperty, source);
                        var current = tb.Text;
                        var target = Translate(source, Lang);
                        var write = _applied is null || current == source || current == Translate(source, prev);
                        TraceLog($"TextBlock name='{tb.Name}' src='{source}' cur='{current}' prev='{prev}' -> '{target}' {(write ? "WRITE" : "skip")}");
                        if (write)
                            tb.Text = target;
                    }
                    else
                    {
                        TraceLog($"TextBlock(skip binding) name='{tb.Name}' text='{tb.Text}'");
                    }
                }
                else
                {
                    TraceLog($"TextBlock(mirror) name='{tb.Name}' text='{tb.Text}' tp={tb.TemplatedParent?.GetType().Name}");
                }
            }
            else if (node is ContentControl ctrl && IsTemplateContent(ctrl.TemplatedParent)
                     && ctrl.Content is string text
                     && !IsDataBoundValue(ctrl, ContentControl.ContentProperty))
            {
                var source = (string?)ctrl.GetValue(SourceTextProperty)
                             ?? ctrl.ReadLocalValue(ContentControl.ContentProperty) as string
                             ?? text;
                ctrl.SetValue(SourceTextProperty, source);
                var current = ctrl.Content as string;
                var target = Translate(source, Lang);
                var write = _applied is null || current == source || current == Translate(source, prev);
                TraceLog($"{ctrl.GetType().Name} name='{ctrl.Name}' src='{source}' cur='{current}' prev='{prev}' -> '{target}' {(write ? "WRITE" : "skip")}");
                if (write)
                    ctrl.Content = target;
            }

            if (node is FrameworkElement fe && IsTemplateContent(fe.TemplatedParent)
                && fe.ToolTip is string tip
                && !IsDataBoundValue(fe, FrameworkElement.ToolTipProperty))
            {
                var source = (string?)fe.GetValue(SourceTipProperty)
                             ?? fe.ReadLocalValue(FrameworkElement.ToolTipProperty) as string
                             ?? tip;
                fe.SetValue(SourceTipProperty, source);
                var current = fe.ToolTip as string;
                if (_applied is null || current == source || current == Translate(source, prev))
                    fe.ToolTip = Translate(source, Lang);
            }

            if (node is Popup popup && popup.Child is not null)
                Visit(popup.Child, prev, seen);

            if (node is Visual or Visual3D)
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                if (node is RadioButton or Control or Window)
                    TraceLog($"VIS {node.GetType().Name} count={count}");
                for (var i = 0; i < count; i++)
                    Visit(VisualTreeHelper.GetChild(node, i), prev, seen);
            }

            if (node is FrameworkElement or FrameworkContentElement)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(node))
                {
                    if (child is DependencyObject d)
                        Visit(d, prev, seen);
                }
            }
        }
        catch (Exception ex)
        {
            TraceLog($"EX {node.GetType().Name}: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// Словарь: английский оригинал → переводы в порядке <see cref="Order"/>
    /// (ru, zh-Hans, zh-Hant, fr, hu, id, az, fa). Язык en = сам ключ.
    /// </summary>
    private static readonly Dictionary<string, string[]> Table = new()
    {
        // ---------------------------------------------------------- сайдбар
        ["Proxy Client"] = ["Прокси-клиент", "代理客户端", "代理用戶端", "Client proxy", "Proxy kliens", "Klien proksi", "Proxy müştərisi", "کلاینت پروکسی"],
        ["Connect"] = ["Подключить", "连接", "連線", "Connexion", "Csatlakozás", "Hubungkan", "Qoşul", "اتصال"],
        ["Routing"] = ["Маршрутизация", "路由", "路由", "Routage", "Útválasztás", "Pemetaan", "Marşrutlaşdırma", "مسیریابی"],
        ["Settings"] = ["Настройки", "设置", "設定", "Paramètres", "Beállítások", "Pengaturan", "Tənzimləmələr", "تنظیمات"],
        ["Logs"] = ["Журнал", "日志", "日誌", "Journaux", "Naplók", "Log", "Jurnal", "گزارش‌ها"],
        ["v2crackN"] = ["v2crackN", "v2crackN", "v2crackN", "v2crackN", "v2crackN", "v2crackN", "v2crackN", "v2crackN"],
        ["Starting ServiceLib…"] = ["Запуск ServiceLib…", "正在启动 ServiceLib…", "正在啟動 ServiceLib…", "Démarrage de ServiceLib…", "ServiceLib indítása…", "Memulai ServiceLib…", "ServiceLib başladılır…", "در حال راه‌اندازی ServiceLib…"],
        ["Loading ServiceLib…"] = ["Загрузка ServiceLib…", "正在加载 ServiceLib…", "正在載入 ServiceLib…", "Chargement de ServiceLib…", "ServiceLib betöltése…", "Memuat ServiceLib…", "ServiceLib yüklənir…", "در حال بارگذاری ServiceLib…"],
        ["ServiceLib 1.1.2 · logic ON"] = ["ServiceLib 1.1.2 · логика включена", "ServiceLib 1.1.2 · 逻辑已启用", "ServiceLib 1.1.2 · 邏輯已啟用", "ServiceLib 1.1.2 · logique activée", "ServiceLib 1.1.2 · logika bekapcsolva", "ServiceLib 1.1.2 · logika aktif", "ServiceLib 1.1.2 · mantiq aktivdir", "ServiceLib 1.1.2 · منطق فعال"],
        ["v2crackN 1.1.2"] = ["v2crackN 1.1.2", "v2crackN 1.1.2", "v2crackN 1.1.2", "v2crackN 1.1.2", "v2crackN 1.1.2", "v2crackN 1.1.2", "v2crackN 1.1.2", "v2crackN 1.1.2"],
        ["About"] = ["О программе", "关于程序", "關於程式", "À propos", "A programról", "Tentang program", "Proqram haqqında", "درباره برنامه"],
        ["SOCKS: 127.0.0.1:{0}"] = ["SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}", "SOCKS: 127.0.0.1:{0}"],
        ["HTTP: 127.0.0.1:{0}"] = ["HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}", "HTTP: 127.0.0.1:{0}"],
        ["ready · socks 127.0.0.1:{0}"] = ["ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}", "ready · socks 127.0.0.1:{0}"],

        // ---------------------------------------------------------- шапка
        ["One tap to protect your traffic"] = ["Один тап — и трафик защищён", "一键保护您的流量", "一鍵保護您的流量", "Un clic pour protéger votre trafic", "Egy kattintás a forgalom védelméért", "Satu ketuk untuk melindungi lalu lintas", "Bir toxunuşla trafikinizi qoruyun", "با یک ضربه ترافیک خود را محافظت کنید"],
        ["Choose what goes through the tunnel"] = ["Выберите, что пойдёт через туннель", "选择哪些流量走隧道", "選擇哪些流量通過通道", "Choisissez ce qui passe par le tunnel", "Válassza ki, mi menjen az alagúton", "Pilih apa yang melewati terowongan", "Tuneli nədən keçəcəyini seçin", "انتخاب کنید چه چیزی از تونل عبور کند"],
        ["Raw client and core output"] = ["Сырой вывод клиента и ядра", "客户端与内核的原始输出", "用戶端與核心的原始輸出", "Sortie brute du client et du noyau", "A kliens és a mag nyers kimenete", "Keluaran mentah klien dan inti", "Müştəri və nüvənin xam çıxışı", "خروجی خام کلاینت و هسته"],
        ["Tune the client to your taste"] = ["Настройте клиент под себя", "按您的喜好调整客户端", "依您的喜好調整用戶端", "Ajustez le client à votre goût", "Igazítsa a klienst az ízléséhez", "Sesuaikan klien sesuai selera Anda", "Müstəriyi öz zövqünüzə uyğunlaşdırın", "کلاینت را به سلیقه خود تنظیم کنید"],
        ["Not connected"] = ["Не подключено", "未连接", "未連線", "Non connecté", "Nincs csatlakoztatva", "Tidak terhubung", "Qoşulmayıb", "متصل نیست"],
        ["Not Connected"] = ["Не подключено", "未连接", "未連線", "Non connecté", "Nincs csatlakoztatva", "Tidak terhubung", "Qoşulmayıb", "متصل نیست"],
        ["Connected"] = ["Подключено", "已连接", "已連線", "Connecté", "Csatlakoztatva", "Terhubung", "Qoşulub", "متصل است"],
        ["Error"] = ["Ошибка", "错误", "錯誤", "Erreur", "Hiba", "Galat", "Xəta", "خطا"],
        ["Connecting…"] = ["Подключение…", "正在连接…", "正在連線…", "Connexion…", "Csatlakozás…", "Menghubungkan…", "Qoşulur…", "در حال اتصال…"],
        ["Disconnecting…"] = ["Отключение…", "正在断开…", "正在中斷…", "Déconnexion…", "Bontás…", "Memutuskan…", "Ayırılır…", "در حال قطع…"],
        ["Reconnecting…"] = ["Переподключение…", "正在重新连接…", "正在重新連線…", "Reconnexion…", "Újracsatlakozás…", "Menyambung ulang…", "Yenidən qoşulur…", "در حال اتصال مجدد…"],
        ["core stopped"] = ["ядро остановлено", "内核已停止", "核心已停止", "noyau arrêté", "mag leállt", "inti berhenti", "nüvə dayandı", "هسته متوقف شد"],
        ["unknown error"] = ["неизвестная ошибка", "未知错误", "未知錯誤", "erreur inconnée", "ismeretlen hiba", "galat tidak diketahui", "naməlum xəta", "خطای ناشناخته"],

        // ---------------------------------------------------------- режим
        ["CONNECTION MODE"] = ["РЕЖИМ ПОДКЛЮЧЕНИЯ", "连接模式", "連線模式", "MODE DE CONNEXION", "KAPCSOLÓDÁSI MÓD", "MODE HUBUNGAN", "QOŞULMA REJİMİ", "حالت اتصال"],
        ["Proxy"] = ["Прокси", "代理", "代理", "Proxy", "Proxy", "Proksi", "Proxy", "پروکسی"],
        ["Tunnel"] = ["Туннель", "隧道", "隧道", "Tunnel", "Alagút", "Terowongan", "Tunnel", "تونل"],
        ["Tunnel - the system proxy sends apps through 127.0.0.1:{port}"] =
            ["Туннель — системный прокси гонит приложения через 127.0.0.1:{port}",
             "隧道 — 系统代理通过 127.0.0.1:{port} 转发所有应用",
             "隧道 — 系統代理透過 127.0.0.1:{port} 轉送所有應用程式",
             "Tunnel - le proxy système achemine les applis via 127.0.0.1:{port}",
             "Alagút - a rendszerproxy az alkalmazásokat a 127.0.0.1:{port} felé küldi",
             "Terowongan - proksi sistem mengirim aplikasi melalui 127.0.0.1:{port}",
             "Tunnel - sistem proxy tətbiqləri 127.0.0.1:{port} üzərindən göndərir",
             "تونل — پروکسی سیستم برنامه‌ها را از 127.0.0.1:{port} عبور می‌دهد"],
        ["Apps read the local port 127.0.0.1:{port} - only proxy-aware traffic goes out"] =
            ["Приложения читают локальный порт 127.0.0.1:{port} — наружу уходит только трафик, знающий про прокси",
             "应用读取本地端口 127.0.0.1:{port} — 只有懂代理的流量才出去",
             "應用程式讀取本機埠 127.0.0.1:{port} — 只有支援代理的流量才出去",
             "Les applis lisent le port local 127.0.0.1:{port} - seul le trafic compatible proxy sort",
             "Az alkalmazások a helyi 127.0.0.1:{port} portot olvassák - csak a proxy-képes forgalom megy ki",
             "Aplikasi membaca port lokal 127.0.0.1:{port} - hanya lalu lintas yang mendukung proxy yang keluar",
             "Tətbiqlər yerli 127.0.0.1:{port} portunu oxuyur - yalnız proxy-ni dəstəkləyən trafik xaricə çıxır",
             "برنامه‌ها پورت محلی 127.0.0.1:{port} را می‌خوانند — فقط ترافیک سازگار با پروکسی خارج می‌شود"],

        // ---------------------------------------------------------- статистика
        ["Download"] = ["Загрузка", "下载", "下載", "Téléchargement", "Letöltés", "Unduhan", "Yükləmə", "دانلود"],
        ["Upload"] = ["Выгрузка", "上传", "上傳", "Envoi", "Feltöltés", "Unggahan", "Göndərmə", "بارگذاری"],
        ["Session"] = ["Сессия", "会话", "會話", "Session", "Munkamenet", "Sesiya", "Seans", "نشست"],

        // ---------------------------------------------------------- серверы
        ["Servers"] = ["Серверы", "服务器", "伺服器", "Serveurs", "Szerverek", "Server", "Serverlər", "سرورها"],
        ["+ Subscription"] = ["+ Подписка", "+ 订阅", "+ 訂閱", "+ Abonnement", "+ Előfizetés", "+ Langganan", "+ Abunəlik", "+ اشتراک"],
        ["+ Server"] = ["+ Сервер", "+ 服务器", "+ 伺服器", "+ Serveur", "+ Szerver", "+ Server", "+ Server", "+ سرور"],
        ["+ Group"] = ["+ Группа", "+ 分组", "+ 分組", "+ Groupe", "+ Csoport", "+ Grup", "+ Qrup", "+ گروه"],
        ["Active"] = ["Активен", "活动", "啟用", "Actif", "Aktív", "Aktif", "Aktiv", "فعال"],
        ["Inactive"] = ["Не активен", "未启用", "未啟用", "Inactif", "Nem aktív", "Tidak aktif", "Aktiv deyil", "غیرفعال"],
        ["No servers yet — use + Server"] =
            ["Серверов пока нет — нажмите + Server", "还没有服务器 — 点 + Server", "尚無伺服器 — 請按 + Server",
             "Aucun serveur - utilisez + Server", "Még nincs szerver - használja a + Servert",
             "Belum ada server - gunakan + Server", "Hələ server yoxdur - + Server istifadə edin",
             "هنوز سروری وجود ندارد — از + Server استفاده کنید"],
        ["Server list"] = ["Список серверов", "服务器列表", "伺服器列表", "Lista de serveurs", "Szerverlista", "Daftar server", "Server siyahısı", "فهرست سرورها"],
        ["group"] = ["группа", "分组", "分組", "groupe", "csoport", "grup", "qrup", "گروه"],
        ["sub"] = ["подписка", "订阅", "訂閱", "abonnement", "előfizetés", "langganan", "abunəlik", "اشتراک"],

        // --------------------------------------------------- обновление и пинг
        ["⟳ Update"] = ["⟳ Обновить", "⟳ 更新", "⟳ 更新", "⟳ Mettre à jour", "⟳ Frissítés", "⟳ Perbarui", "⟳ Yenilə", "⟳ به‌روزرسانی"],
        ["⚡ Ping all"] = ["⚡ Пинг всех", "⚡ 全部 Ping", "⚡ 全部 Ping", "⚡ Tout pinger", "⚡ Minden ping", "⚡ Semua ping", "⚡ Hamısına ping", "⚡ پینگ همه"],
        ["Ping"] = ["Пинг", "Ping", "Ping", "Ping", "Ping", "Ping", "Ping", "پینگ"],
        ["tcp ping"] = ["TCP-пинг", "TCP Ping", "TCP Ping", "Ping TCP", "TCP ping", "Ping TCP", "TCP ping", "پینگ TCP"],
        ["real ping"] = ["реальный пинг", "真实 Ping", "真實 Ping", "Ping réel", "Valódi ping", "Ping sebenarnya", "Real ping", "پینگ واقعی"],
        ["udp test"] = ["UDP-тест", "UDP 测试", "UDP 測試", "Test UDP", "UDP teszt", "Uji UDP", "UDP testi", "تست UDP"],
        ["Test ping"] = ["Тест пинга", "测试 Ping", "測試 Ping", "Test de ping", "Ping teszt", "Uji ping", "Ping testi", "تست پینگ"],
        ["Update servers"] = ["Обновить серверы", "更新服务器", "更新伺服器", "Mettre à jour les serveurs", "Szerverek frissítése", "Perbarui server", "Serverləri yenilə", "به‌روزرسانی سرورها"],
        ["Updating…"] = ["Обновление…", "正在更新…", "正在更新…", "Mise à jour…", "Frissítés…", "Memperbarui…", "Yenilənir…", "در حال به‌روزرسانی…"],
        ["nothing to update"] = ["обновлять нечего", "没有可更新的内容", "沒有可更新的內容", "rien à mettre à jour", "nincs mit frissíteni", "tidak ada yang diperbarieu", "yeniləcək şey yoxdur", "چیزی برای به‌روزرسانی نیست"],
        ["pinging {0} server(s)…"] =
            ["пингую серверов: {0}…", "正在 ping {0} 个服务器…", "正在 ping {0} 個伺服器…",
             "ping de {0} serveur(s)…", "{0} szerver pingelése…", "ping {0} server…",
             "{0} serverə ping göndərilir…", "پینگ {0} سرور…"],
        ["Test finished"] =
            ["Тест завершён", "测试完成", "測試完成", "Test terminé",
             "Teszt befejezve", "Uji selesai", "Kiểm tra xong", "تست پایان یافت"],
        ["head ping"] = ["HEAD-пинг", "HEAD Ping", "HEAD Ping", "Ping HEAD", "HEAD ping", "Ping HEAD", "HEAD ping", "پینگ HEAD"],
        ["icmp ping"] = ["ICMP-пинг", "ICMP Ping", "ICMP Ping", "Ping ICMP", "ICMP ping", "Ping ICMP", "ICMP ping", "پینگ ICMP"],

        // ---------------------------------------------------------- группы
        ["Rename group"] = ["Переименовать группу", "重命名分组", "重新命名分組", "Renommer le groupe", "Csoport átnevezése", "Ganti nama grup", "Qrupu yenidən adlandır", "تغییر نام گروه"],
        ["Pin to top"] = ["Закрепить сверху", "置顶", "置頂", "Épingler en haut", "Rögzítés felülre", "Sematkan ke atas", "Yuxarı sabitlə", "سنجاق در بالا"],
        ["Delete group"] = ["Удалить группу", "删除分组", "刪除分組", "Supprimer le groupe", "Csoport törlése", "Hapus grup", "Qrupu sil", "حذف گروه"],
        ["Move to group"] = ["Переместить в группу", "移动到分组", "移動到分組", "Déplacer vers le groupe", "Áthelyezés csoportba", "Pindahkan ke grup", "Qrupa daşın", "انتقال به گروه"],
        ["Cancel"] = ["Отмена", "取消", "取消", "Annuler", "Mégse", "Batal", "Ləğv et", "انصراف"],
        ["Save"] = ["Сохранить", "保存", "保存", "Enregistrer", "Mentés", "Saxla", "Saxla", "ذخیره"],

        // ---------------------------------------------------------- выбор сервера
        ["Select server"] = ["Выбор сервера", "选择服务器", "選擇伺服器", "Sélectionner un serveur", "Szerver kiválasztása", "Pilih server", "Server seç", "انتخاب سرور"],
        ["Close"] = ["Закрыть", "关闭", "關閉", "Fermer", "Bezárás", "Tutup", "Bağla", "بستن"],
        ["Paste"] = ["Вставить", "粘贴", "貼上", "Coller", "Beillesztés", "Tempel", "Yapışdır", "چسباندن"],
        ["Add"] = ["Добавить", "添加", "新增", "Ajouter", "Hozzáadás", "Tambah", "Əlavə et", "افزودن"],
        ["Loading server…"] = ["Загрузка сервера…", "正在加载服务器…", "正在載入伺服器…", "Chargement du serveur…", "Szerver betöltése…", "Memuat server…", "Server yüklənir…", "در حال بارگذاری سرور…"],
        ["… · Selected configuration"] = ["… · Выбранная конфигурация", "… · 已选配置", "… · 已選設定", "… · Configuration sélectionnée", "… · Kiválasztott konfiguráció", "… · Konfigurasi terpilih", "… · Seçilmiş konfiqurasiya", "… · پیکربندی انتخاب‌شده"],
        [" · Selected configuration"] = [" · Выбранная конфигурация", " · 已选配置", " · 已選設定", " · Configuration sélectionnée", " · Kiválasztott konfiguráció", " · Konfigurasi terpilih", " · Seçilmiş konfiqurasiya", " · پیکربندی انتخاب‌شده"],
        ["No server selected"] = ["Сервер не выбран", "未选择服务器", "未選擇伺服器", "Aucun serveur sélectionné", "Nincs kiválasztott szerver", "Tidak ada server dipilih", "Server seçilməyib", "هیچ سروری انتخاب نشده"],
        ["Press Change to pick a server"] = ["Выберите сервер в списке", "请在列表中选择服务器", "請在清單中選擇伺服器", "Choisissez un serveur dans la liste", "Válasszon szerveret a listából", "Pilih server dari daftar", "Siyahıdan server seçin", "از فهرست یک سرور انتخاب کنید"],

        // ---------------------------------------------------------- роутинг
        ["\U0001F50D  Search presets or domains"] = ["\U0001F50D  Поиск пресетов и доменов", "\U0001F50D  搜索预设或域名", "\U0001F50D  搜尋預設或網域", "\U0001F50D  Rechercher des préréglages ou domaines", "\U0001F50D  Előbeállítások vagy domainek keresése", "\U0001F50D  Cari prasetel atau domain", "\U0001F50D  Preset və domen axtar", "\U0001F50D  جستجوی از پیش تنظیم‌ها یا دامنه‌ها"],
        ["+  New preset"] = ["+  Новый пресет", "+  新建预设", "+  新增預設", "+  Nouveau préréglage", "+  Új előbeállítás", "+  Prasetel baru", "+  Yeni preset", "+  از پیش تنظیم جدید"],
        ["Presets"] = ["Пресеты", "预设", "預設", "Préréglages", "Előbeállítások", "Prasetel", "Presetlər", "از پیش تنظیم‌ها"],
        ["Rules"] = ["Правила", "规则", "規則", "Règles", "Szabályok", "Aturan", "Qaydalar", "قوانین"],
        ["Select a preset on the left"] = ["Выберите пресет слева", "在左侧选择预设", "在左側選擇預設", "Sélectionnez un préréglage à gauche", "Válasszon előbeállítást balra", "Pilih prasetel di kiri", "Soldan bir preset seçin", "یک از پیش تنظیم را در سمت چپ انتخاب کنید"],
        ["Add rule"] = ["Добавить правило", "添加规则", "新增規則", "Ajouter une règle", "Szabály hozzáadása", "Tambah aturan", "Qayda əlavə et", "افزودن قانون"],
        ["Domain"] = ["Домен", "域名", "網域", "Domaine", "Domín", "Domain", "Domen", "دامنه"],
        ["IP"] = ["IP", "IP", "IP", "IP", "IP", "IP", "IP", "IP"],
        ["Process"] = ["Процесс", "进程", "程序", "Processus", "Folyamat", "Proses", "Proses", "فرایند"],
        ["Port"] = ["Порт", "端口", "埠", "Port", "Port", "Port", "Port", "پورت"],
        ["Direct"] = ["Напрямую", "直连", "直連", "Direct", "Közvetlen", "Langsung", "Birbaşa", "مستقیم"],
        ["Block"] = ["Блокировать", "阻止", "封鎖", "Bloquer", "Blokkolás", "Blokir", "Blokla", "مسدود کردن"],
        ["No rules yet — add the first one below"] =
            ["Правил пока нет — добавьте первое ниже", "还没有规则 — 在下方添加第一条", "尚無規則 — 請在下方新增第一條",
             "Aucune règle - ajoutez la première ci-dessous", "Még nincs szabály - adja hozzá az elsőt alább",
             "Belum ada aturan - tambahkan yang pertama di bawah", "Hələ qayda yoxdur - aşağıdakı ilkini əlavə edin",
             "هنوز قانونی نیست — اولین مورد را در پایین اضافه کنید"],
        ["New preset"] = ["Новый пресет", "新建预设", "新增預設", "Nouveau préréglage", "Új előbeállítás", "Prasetel baru", "Yeni preset", "از پیش تنظیم جدید"],
        ["Create"] = ["Создать", "创建", "建立", "Créer", "Létrehozás", "Buat", "Yarat", "ایجاد"],
        ["{0} rules"] = ["{0} правил", "{0} 条规则", "{0} 條規則", "{0} règles", "{0} szabály", "{0} aturan", "{0} qayda", "{0} قانون"],
        ["custom rules"] = ["свои правила", "自定义规则", "自訂規則", "règles personnalisées", "egyéni szabályok", "aturan khusus", "xüsusi qaydalar", "قوانین سفارشی"],
        ["ruleset by url"] = ["набор правил по URL", "按 URL 的规则集", "依 URL 的規則集", "règles par URL", "szabálykészlet URL-ről", "aturan dari URL", "URL-ə əsasən qayda dəsti", "مجموعه قوانین بر اساس URL"],
        ["Routing {0}"] = ["Роутинг {0}", "路由 {0}", "路由 {0}", "Routage {0}", "Útválasztás {0}", "Pemetaan {0}", "Marşrut {0}", "مسیریابی {0}"],

        // ---------------------------------------------------------- импорт
        ["Subscription URL — Add will fetch its servers right away"] =
            ["URL подписки — «Добавить» сразу подтянет её серверы",
             "订阅地址 — 点“添加”会立即拉取服务器", "訂閱網址 — 按「新增」會立即抓取伺服器",
             "URL d'abonnement - Ajouter récupérera ses serveurs tout de suite",
             "Előfizetési URL - a Hozzáadás azonnal lekéri a szervereit",
             "URL langganan - Tambah akan langsung mengambil servernya",
             "Abunəlik URL-i - Əlavə et dərhal serverlərini yükləyir",
             "آدرس اشتراک — «افزودن» بلافاصله سرورهایش را می‌گیرد"],
        ["Group name — move servers into it with the ⇄ button"] =
            ["Имя группы — перенесите в неё серверы кнопкой ⇄",
             "分组名 — 用 ⇄ 按钮把服务器移进来", "分組名 — 用 ⇄ 按鈕把伺服器移進來",
             "Nom de groupe - déplacez-y les serveurs avec le bouton ⇄",
             "Csoportnév - tegye át a szervereket a ⇄ gombbal",
             "Nama grup - pindahkan server ke dalamnya dengan tombol ⇄",
             "Qrup adı - ⇄ düyməsi ilə serverləri ora daşıyın",
             "نام گروه — سرورها را با دکمه ⇄ به آن منتقل کنید"],
        ["Server link (ss:// vmess:// vless:// trojan:// hysteria2:// …) or a base64 list"] =
            ["Ссылка сервера (ss:// vmess:// vless:// trojan:// hysteria2:// …) или base64-список",
             "服务器链接（ss:// vmess:// vless:// trojan:// hysteria2:// …）或 base64 列表",
             "伺服器連結（ss:// vmess:// vless:// trojan:// hysteria2:// …）或 base64 清單",
             "Lien de serveur (ss:// vmess:// vless:// trojan:// hysteria2:// ...) ou liste base64",
             "Szerverhivatkozás (ss:// vmess:// vless:// trojan:// hysteria2:// ...) vagy base64 lista",
             "Tautan server (ss:// vmess:// vless:// trojan:// hysteria2:// ...) atau daftar base64",
             "Server bağlantısı (ss:// vmess:// vless:// trojan:// hysteria2:// ...) və ya base64 siyahısı",
             "لینک سرور (ss:// vmess:// vless:// trojan:// hysteria2:// …) یا فهرست base64"],

        // ---------------------------------------------------------- настройки
        ["Traffic"] = ["Трафик", "流量", "流量", "Trafic", "Forgalom", "Trafik", "Trafik", "ترافیک"],
        ["Tunnel mode"] = ["Режим туннеля", "隧道模式", "隧道模式", "Mode tunnel", "Alagút mód", "Mode terowongan", "Tunnel rejimi", "حالت تونل"],
        ["Route all device traffic through the proxy"] =
            ["Пускать весь трафик устройства через прокси", "将设备全部流量经代理转发", "將裝置全部流量經代理轉送",
             "Acheminer tout le trafic de l'appareil via le proxy", "Az eszköz minden forgalmát a proxy felé",
             "Arahkan seluruh lalu lintas perangkat melalui proksi", "Cihazın bütün trafikini proxy üzərindən göndərin",
             "تمام ترافیک دستگاه را از پروکسی عبور دهید"],
        ["gVisor network stack"] = ["Сетевой стек gVisor", "gVisor 网络堆叠", "gVisor 網路堆疊", "Stack réseau gVisor", "gVisor hálózati verem", "Tumpukan jaringan gVisor", "gVisor şəbəkə yığını", "پشته شبکه gVisor"],
        ["Better compatibility, slightly slower"] =
            ["Лучшая совместимость, чуть медленнее", "兼容性更好，稍慢", "相容性更好，稍慢",
             "Meilleure compatibilité, un peu plus lent", "Jobb kompatibilitás, valamivel lassabb",
             "Kompatibilitas lebih baik, sedikit lebih lambat", "Daha yaxşı uyğunluq, bir qədər yavaş",
             "سازگاری بهتر، کمی کندتر"],
        ["Local SOCKS port"] = ["Локальный SOCKS-порт", "本地 SOCKS 端口", "本機 SOCKS 埠", "Port SOCKS local", "Helyi SOCKS-port", "Port SOCKS lokal", "Yerli SOCKS portu", "پورت SOCKS محلی"],
        ["127.0.0.1 — applied on next connect"] =
            ["127.0.0.1 — применится при следующем подключении", "127.0.0.1 — 下次连接时生效", "127.0.0.1 — 下次連線時生效",
             "127.0.0.1 - appliqué à la prochaine connexion", "127.0.0.1 - a következő csatlakozáskor lép érvénybe",
             "127.0.0.1 - diterapkan saat koneksi berikutnya", "127.0.0.1 - növbəti qoşuluşda tətbiq olunur",
             "127.0.0.1 — در اتصال بعدی اعمال می‌شود"],
        ["HTTP proxy port"] = ["Порт HTTP-прокси", "HTTP 代理端口", "HTTP 代理埠", "Port proxy HTTP", "HTTP proxy-port", "Port proksi HTTP", "HTTP proxy portu", "پورت پروکسی HTTP"],
        ["127.0.0.1 — follows the SOCKS port"] =
            ["127.0.0.1 — следует за SOCKS-портом", "127.0.0.1 — 跟随 SOCKS 端口", "127.0.0.1 — 跟隨 SOCKS 埠",
             "127.0.0.1 - suit le port SOCKS", "127.0.0.1 - a SOCKS-portot követi", "127.0.0.1 - mengikuti port SOCKS",
             "127.0.0.1 - SOCKS portunu izləyir", "127.0.0.1 — از پورت SOCKS پیروی می‌کند"],
        ["General"] = ["Основные", "常规", "一般", "Général", "Általános", "Umum", "Ümumi", "عمومی"],
        ["Launch at startup"] = ["Запуск при старте", "开机启动", "開機啟動", "Lancement au démarrage", "Indítás induláskor", "Mulai bersama sistem", "Başlanğıcda işə sal", "اجرا هنگام راه‌اندازی"],
        ["Start v2crackN when you sign in"] =
            ["Запускать v2crackN при входе", "登录时启动 v2crackN", "登入時啟動 v2crackN",
             "Démarrer v2crackN à la connexion", "A v2crackN indítása bejelentkezéskor",
             "Jalankan v2crackN saat Anda masuk", "Sistemə daxil olduqda v2crackN-i işə sal",
             "هنگام ورود v2crackN را اجرا کنید"],
        ["System proxy"] = ["Системный прокси", "系统代理", "系統代理", "Proxy système", "Rendszerproxy", "Proksi sistem", "Sistem proxy", "پروکسی سیستم"],
        ["Apply the proxy to the whole system"] =
            ["Применить прокси ко всей системе", "将代理应用到整个系统", "將代理套用到整個系統",
             "Appliquer le proxy à tout le système", "A proxy alkalmazása a teljes rendszerre",
             "Terapkan proksi ke seluruh sistem", "Proxy-u bütün sistemə tətbiq edin",
             "پروکسی را در کل سیستم اعمال کنید"],
        ["Bypass DPI"] = ["Обход DPI", "绕过 DPI", "躲過 DPI", "Contournement DPI", "DPI megkerülése", "Lewati DPI", "DPI-ni keç", "دور زدن DPI"],
        ["Packet fragmentation for deep packet inspection"] =
            ["Фрагментация пакетов для обхода глубокой инспекции", "分片数据包以绕过深度包检测", "封包分片以繞過深度封包偵測",
             "Fragmentation des paquets contre l'inspection approfondie", "Csomagfragmentálás a mély csomagvizsgálathoz",
             "Fragmentasi paket untuk inspeksi mendalam", "Dərin paket yoxlamasından keçmək üçün paket fraqlmentasiyası",
             "تکه‌تکه کردن بسته‌ها برای بازرسی عمیق بسته"],
        ["Appearance"] = ["Оформление", "外观", "外觀", "Apparence", "Megjelenés", "Tampilan", "Görünüş", "ظاهر"],
        ["Ping method"] = ["Способ пинга", "Ping 方式", "Ping 方式", "Méthode de ping", "Ping módja", "Metode ping", "Ping üsulu", "روش پینگ"],
        ["Mixed (all tests)"] = ["Смешанный (все тесты)", "混合（全部测试）", "混合（全部測試）", "Mixte (tous les tests)", "Vegyes (minden teszt)", "Campuran (semua tes)", "Qarışıq (bütün testlər)", "ترکیبی (همه تست‌ها)"],
        ["TCP, real ping and UDP in sequence"] =
            ["TCP, реальный пинг и UDP по очереди", "依次测试 TCP、真实 Ping 和 UDP", "依次測試 TCP、真實 Ping 和 UDP",
             "TCP, ping réel et UDP à la suite", "TCP, valódi ping és UDP egymás után",
             "TCP, ping nyata, dan UDP secara bergantian", "TCP, real ping və UDP ardıcıl olaraq",
             "TCP، پینگ واقعی و UDP به ترتیب"],
        ["HTTP GET through the server"] = ["HTTP GET через сервер", "通过服务器的 HTTP GET", "透過伺服器的 HTTP GET", "HTTP GET via le serveur", "HTTP GET a szerveren keresztül", "HTTP GET melalui server", "HTTP GET server vasitəsilə", "HTTP GET از طریق سرور"],
        ["HTTP HEAD through the server"] = ["HTTP HEAD через сервер", "通过服务器的 HTTP HEAD", "透過伺服器的 HTTP HEAD", "HTTP HEAD via le serveur", "HTTP HEAD a szerveren keresztül", "HTTP HEAD melalui server", "HTTP HEAD server vasitəsilə", "HTTP HEAD از طریق سرور"],
        ["TCP connect to the server address"] =
            ["TCP-соединение с адресом сервера", "连接服务器地址的 TCP", "連線伺服器位址的 TCP",
             "Connexion TCP à l'adresse du serveur", "TCP kapcsolat a szerver címére",
             "Koneksi TCP ke alamat server", "Server ünvanına TCP qoşulması", "اتصال TCP به آدرس سرور"],
        ["ICMP echo to the server address"] =
            ["ICMP-эхо на адрес сервера", "向服务器地址发送 ICMP echo", "向伺服器位址傳送 ICMP echo",
             "Echo ICMP vers l'adresse du serveur", "ICMP echo a szerver címére",
             "ICMP echo ke alamat server", "Server ünvanına ICMP echo", "ICMP echo به آدرس سرور"],
        ["Theme"] = ["Тема", "主题", "主題", "Thème", "Téma", "Tema", "Mövzu", "پوسته"],
        ["Dark"] = ["Тёмная", "深色", "深色", "Sombre", "Sötét", "Gelap", "Tündə", "تیره"],
        ["Light"] = ["Светлая", "浅色", "淺色", "Clair", "Világos", "Terang", "İşıqlı", "روشن"],
        ["Follow system"] = ["Следовать системе", "跟随系统", "跟隨系統", "Suivre le système", "Rendszer követése", "Ikuti sistem", "Sistemi izlə", "پیروی از سیستم"],
        ["Language"] = ["Язык", "语言", "語言", "Langue", "Nyelv", "Bahasa", "Dil", "زبان"],
        ["Interface language"] = ["Язык интерфейса", "界面语言", "介面語言", "Langue de l'interface", "A felület nyelve", "Bahasa antarmuka", "İnterfeys dili", "زبان رابط کاربری"],

        // ---------------------------------------------------------- журналы
        ["\U0001F4DC  Live output: core, ServiceLib and this client"] =
            ["\U0001F4DC  Живой вывод: ядро, ServiceLib и этот клиент",
             "\U0001F4DC  实时输出：内核、ServiceLib 与本客户端",
             "\U0001F4DC  即時輸出：核心、ServiceLib 與本用戶端",
             "\U0001F4DC  Sortie en direct : noyau, ServiceLib et ce client",
             "\U0001F4DC  Élő kimenet: mag, ServiceLib és ez a kliens",
             "\U0001F4DC  Keluaran langsung: inti, ServiceLib dan klien ini",
             "\U0001F4DC  Canlı çıxış: nüvə, ServiceLib və bu müştəri",
             "\U0001F4DC  خروجی زنده: هسته، ServiceLib و این کلاینت"],
        ["From file"] = ["Из файла", "从文件", "從檔案", "Depuis un fichier", "Fájlból", "Dari berkas", "Fayldan", "از فایل"],
        ["Clear"] = ["Очистить", "清除", "清除", "Effacer", "Törlés", "Bersihkan", "Təmizlə", "پاک کردن"],
        ["Folder"] = ["Папка", "文件夹", "資料夾", "Dossier", "Mappa", "Folder", "Qovluq", "پوشه"],

        // ---------------------------------------------------------- тост
        ["Reconnect to switch mode"] =
            ["Переподключитесь, чтобы сменить режим", "重新连接以切换模式", "重新連線以切換模式",
             "Reconnectez pour changer de mode", "Csatlakozzon újra a mód váltásához",
             "Sambungkan ulang untuk mengganti mode", "Rejimi dəyişmək üçün yenidən qoşulun",
             "برای تغییر حالت دوباره متصل شوید"],
        ["Reconnect to apply the mode"] =
            ["Переподключитесь, чтобы применить режим", "重新连接以应用模式", "重新連線以套用模式",
             "Reconnectez pour appliquer le mode", "Csatlakozzon újra a mód alkalmazásához",
             "Sambungkan ulang untuk menerapkan mode", "Rejimi tətbiq etmək üçün yenidən qoşulun",
             "برای اعمال حالت دوباره متصل شوید"],
        ["Tunnel and Proxy change only after reconnecting"] =
            ["Режимы Tunnel и Proxy меняются только при переподключении", "Tunnel 与 Proxy 仅在重新连接后切换", "Tunnel 與 Proxy 僅在重新連線後切換",
             "Tunnel et Proxy ne changent qu'après reconnexion", "Az Alagút és a Proxy csak újracsatlakozás után változik",
             "Tunnel dan Proxy hanya berubah setelah menyambung ulang", "Tunnel və Proxy yalnız yenidən qoşulduqdan sonra dəyişir",
             "تونل و پروکسی فقط پس از اتصال مجدد تغییر می‌کنند"],
        ["Tunnel and Proxy switch only after reconnecting"] =
            ["Tunnel и Proxy переключаются только при переподключении", "Tunnel 与 Proxy 只有重新连接后才切换", "Tunnel 與 Proxy 只有重新連線後才切換",
             "Tunnel et Proxy ne basculent qu'après reconnexion", "Az Alagút és a Proxy csak újracsatlakozás után vált",
             "Tunnel dan Proxy hanya berpindah setelah menyambung ulang", "Tunnel və Proxy yalnız yenidən qoşulduqdan sonra keçir",
             "تونل و پروکسی فقط پس از اتصال مجدد تعویض می‌شونд"],

        // ---------------------------------------------------------- статус-бар и логи
        ["guiLogs: no log file found yet"] = ["guiLogs: файл лога пока не найден", "guiLogs: 尚未找到日志文件", "guiLogs: 尚未找到日誌檔案", "guiLogs: aucun fichier journal trouvé", "guiLogs: még nincs naplófájl", "guiLogs: belum ada file log", "guiLogs: hələ jurnal faylı tapılmayıb", "guiLogs: هنوز فایل گزارشی یافت نشده"],
        ["imported {0} lines from {1}"] = ["импортировано {0} строк из {1}", "已导入 {0} 行，来自 {1}", "已匯入 {0} 行，來自 {1}", "{0} lignes importées depuis {1}", "{0} sor importálva {1} ből", "{0} baris diimpor dari {1}", "{0} sətir {1} -dən idxal edilib", "{0} خط از {1} وارد شد"],
        ["import failed: "] = ["ошибка импорта: ", "导入失败：", "匯入失敗：", "échec de l'import : ", "import sikertelen: ", "impor gagal: ", "import uğursuz: ", "ورود ناموفق: "],
        ["log view cleared (files in guiLogs are untouched)"] = ["журнал очищен (файлы в guiLogs не тронуты)", "日志视图已清除（guiLogs 中的文件未改动）", "日誌檢視已清除（guiLogs 中的檔案未更動）", "journal vidé (les fichiers guiLogs sont intacts)", "napló törölve (a guiLogs fájlok érintetlenek)", "tampilan log dibersihkan (file guiLogs tidak tersentuh)", "jurnal təmizləndi (guiLogs faylları dəyişməyib)", "نمایش گزارش پاک شد (فایل‌های guiLogs دست نخورده‌اند)"],
        ["open folder failed: "] = ["не удалось открыть папку: ", "无法打开文件夹：", "無法開啟資料夾：", "échec de l'ouverture du dossier : ", "mappa megnyitása sikertelen: ", "gagal membuka folder: ", "qovluq açılmadı: ", "پوشه باز نشد: "],
        ["core stopped unexpectedly"] = ["ядро остановлено неожиданно", "内核意外停止", "核心意外停止", "noyau arrêté inopinément", "mag váratlanul leállt", "inti berhenti tiba-tiba", "nüvə gözlənilməz dayandı", "هسته به طور غیرمنتظره متوقف شد"],
        ["port must be 1024-65535"] = ["порт должен быть 1024-65535", "端口必须为 1024-65535", "埠必須為 1024-65535", "le port doit être 1024-65535", "a portnak 1024-65535 kell lennie", "port harus 1024-65535", "port 1024-65535 olmalıdır", "پورت باید 1024-65535 باشد"],
        ["clipboard unavailable"] = ["буфер обмена недоступен", "剪贴板不可用", "剪貼簿無法使用", "presse-papiers indisponible", "vágólap nem érhető el", "clipboard tidak tersedia", "mübadilə buferi əlçatmazdır", "کلیپ‌بورد در دسترس نیست"],
        ["select a preset first"] = ["сначала выберите пресет", "请先选择预设", "請先選擇預設", "sélectionnez d'abord un préréglage", "előbb válasszon előbeállítást", "pilih prasetel terlebih dahulu", "əvvəl preset seçin", "ابتدا یک پریست انتخاب کنید"],
        ["AUTOTEST: PASS — autotest.log"] = ["АВТОТЕСТ: PASS — autotest.log", "自动测试：PASS — autotest.log", "自動測試：PASS — autotest.log", "AUTOTEST : PASS — autotest.log", "AUTOTEST: PASS — autotest.log", "AUTOTEST: PASS — autotest.log", "AVTO TEST: PASS — autotest.log", "آزمایش خودکار: PASS — autotest.log"],
        ["AUTOTEST: FAIL ({0}) — autotest.log"] = ["АВТОТЕСТ: FAIL ({0}) — autotest.log", "自动测试：FAIL ({0}) — autotest.log", "自動測試：FAIL ({0}) — autotest.log", "AUTOTEST : FAIL ({0}) — autotest.log", "AUTOTEST: FAIL ({0}) — autotest.log", "AUTOTEST: FAIL ({0}) — autotest.log", "AVTO TEST: FAIL ({0}) — autotest.log", "آزمایش خودکار: FAIL ({0}) — autotest.log"],
        ["ServiceLib init FAILED"] = ["ServiceLib не инициализирован", "ServiceLib 初始化失败", "ServiceLib 初始化失敗", "ServiceLib n'a pas pu s'initialiser", "ServiceLib nem inicializálható", "ServiceLib gagal diinisialisasi", "ServiceLib işə salına bilmədi", "ServiceLib مقداردهی نشد"],
        ["ServiceLib init failed — see guiLogs"] = ["ServiceLib не инициализирован — см. guiLogs", "ServiceLib 初始化失败 — 查看 guiLogs", "ServiceLib 初始化失敗 — 請查看 guiLogs", "ServiceLib n'a pas pu s'initialiser — voir guiLogs", "ServiceLib nem inicializálható — lásd a guiLogs-ot", "ServiceLib gagal diinisialisasi — lihat guiLogs", "ServiceLib işə salına bilmədi — guiLogs-a baxın", "ServiceLib مقداردهی نشد — به guiLogs مراجعه کنید"],
    };
}
