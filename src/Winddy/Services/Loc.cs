using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Markup;

namespace Winddy.Services;

/// <summary>Tiny string table. English is the fallback, Russian is used when the UI culture is "ru".</summary>
public static class Loc
{
    private static readonly Dictionary<string, (string En, string Ru)> Strings = new()
    {
        ["AppTitle"] = ("Winddy", "Winddy"),
        ["SearchPlaceholder"] = ("type to search…", "введите для поиска…"),
        ["Clear"] = ("Clear", "Очистить"),
        ["ClearAll"] = ("Clear all", "Очистить всё"),
        ["Preferences"] = ("Preferences…", "Настройки…"),
        ["About"] = ("About", "О программе"),
        ["Quit"] = ("Quit", "Выход"),
        ["ClearTooltip"] = ("Clear unpinned items.\nHold Shift to clear all.", "Удалить незакреплённые элементы.\nУдерживайте Shift, чтобы удалить все."),
        ["ClearAllTooltip"] = ("Clear all items.", "Удалить все элементы."),
        ["AboutTooltip"] = ("Read more about the application.", "Подробнее о приложении."),
        ["QuitTooltip"] = ("Quit application.", "Закрыть приложение."),
        ["ClearAlertMessage"] = ("Are you sure you want to clear the history?", "Вы уверены, что хотите очистить историю?"),
        ["ClearAlertComment"] = ("You can't undo this action.", "Это действие нельзя отменить."),
        ["Cancel"] = ("Cancel", "Отмена"),
        ["DontAskAgain"] = ("Don't ask again", "Больше не спрашивать"),
        ["TrayTooltip"] = ("Winddy — clipboard history", "Winddy — история буфера обмена"),
        ["TrayPaused"] = ("paused", "на паузе"),
        ["Open"] = ("Open Winddy", "Открыть Winddy"),
        ["Pause"] = ("Pause (ignore new copies)", "Пауза (игнорировать новые копии)"),
        ["Resume"] = ("Resume", "Возобновить"),
        ["IgnoreNext"] = ("Ignore next copy", "Пропустить следующую копию"),
        ["Pin"] = ("Pin", "Закрепить"),
        ["Unpin"] = ("Unpin", "Открепить"),
        ["Delete"] = ("Delete", "Удалить"),
        ["Image"] = ("Image", "Изображение"),
        ["HotkeyInUse"] = ("The shortcut {0} is already used by another application. Choose another one in Preferences.", "Сочетание {0} уже занято другим приложением. Выберите другое в настройках."),
        ["NoResults"] = ("No matches", "Ничего не найдено"),

        // Preview
        ["PreviewApplication"] = ("Application:", "Приложение:"),
        ["PreviewFirstCopy"] = ("First copy time:", "Первое копирование:"),
        ["PreviewLastCopy"] = ("Last copy time:", "Последнее копирование:"),
        ["PreviewNumberOfCopies"] = ("Number of copies:", "Количество копий:"),
        ["PreviewPinKey"] = ("Press {0} to pin or unpin.", "Нажмите {0}, чтобы закрепить или открепить."),
        ["PreviewDeleteKey"] = ("Press {0} to delete.", "Нажмите {0}, чтобы удалить."),

        // Settings: common
        ["SettingsTitle"] = ("Winddy Preferences", "Настройки Winddy"),
        ["General"] = ("General", "Основные"),
        ["Storage"] = ("Storage", "Хранилище"),
        ["Appearance"] = ("Appearance", "Внешний вид"),
        ["Pins"] = ("Pins", "Закреплённые"),
        ["Ignore"] = ("Ignore", "Игнорирование"),
        ["Advanced"] = ("Advanced", "Дополнительно"),
        ["Add"] = ("Add", "Добавить"),
        ["Remove"] = ("Remove", "Удалить"),
        ["Browse"] = ("Browse…", "Обзор…"),
        ["Reset"] = ("Reset", "Сбросить"),

        // General
        ["LaunchAtLogin"] = ("Launch at login", "Запускать при входе в систему"),
        ["OpenHotkey"] = ("Open:", "Открыть:"),
        ["OpenHotkeyTooltip"] = ("Global shortcut to open the application.\nPressing the main key again while holding the modifiers selects the next item; releasing the modifiers then confirms the selection.\nDefault: Ctrl+Shift+C.", "Глобальное сочетание для открытия приложения.\nПовторное нажатие основной клавиши при удерживаемых модификаторах выбирает следующий элемент; отпускание модификаторов подтверждает выбор.\nПо умолчанию: Ctrl+Shift+C."),
        ["PinHotkey"] = ("Pin:", "Закрепить:"),
        ["DeleteHotkey"] = ("Delete:", "Удалить:"),
        ["PreviewHotkey"] = ("Preview:", "Просмотр:"),
        ["RecordHint"] = ("Click and press a shortcut", "Нажмите и введите сочетание"),
        ["Behavior"] = ("Behavior:", "Поведение:"),
        ["PasteAutomatically"] = ("Paste automatically", "Вставлять автоматически"),
        ["PasteWithoutFormatting"] = ("Paste without formatting", "Вставлять без форматирования"),
        ["Modifiers"] = ("Customize the behavior when selecting an item:\n• Select with {0} pressed to copy the item.\n• Select with {1} pressed to copy and paste the item.\n• Select with {2} pressed to copy, clear formatting, and paste the item.", "Поведение при выборе элемента:\n• Выбор с {0} — скопировать элемент.\n• Выбор с {1} — скопировать и вставить.\n• Выбор с {2} — скопировать, убрать форматирование и вставить."),
        ["Search"] = ("Search:", "Поиск:"),
        ["Exact"] = ("Exact", "Точный"),
        ["Fuzzy"] = ("Fuzzy", "Нечёткий"),
        ["Regexp"] = ("Regular expressions", "Регулярные выражения"),
        ["Mixed"] = ("Mixed", "Смешанный"),
        ["Language"] = ("Language:", "Язык:"),
        ["LanguageSystem"] = ("System", "Системный"),
        ["RestartNeeded"] = ("Restart Winddy to apply.", "Перезапустите Winddy, чтобы применить."),

        // Storage
        ["Save"] = ("Save:", "Сохранять:"),
        ["Files"] = ("Files", "Файлы"),
        ["Images"] = ("Images", "Изображения"),
        ["Text"] = ("Text", "Текст"),
        ["SaveDescription"] = ("Change what types of copied content should be stored.", "Какие типы скопированного содержимого сохранять."),
        ["Size"] = ("Size:", "Размер:"),
        ["SizeTooltip"] = ("Number of history items to keep.\nDefault: 200.", "Сколько элементов хранить в истории.\nПо умолчанию: 200."),
        ["SortBy"] = ("Sort by:", "Сортировка:"),
        ["LastCopiedAt"] = ("Time of last copy", "Время последнего копирования"),
        ["FirstCopiedAt"] = ("Time of first copy", "Время первого копирования"),
        ["NumberOfCopies"] = ("Number of copies", "Количество копий"),

        // Appearance
        ["PopupAt"] = ("Popup at:", "Показывать:"),
        ["Cursor"] = ("Cursor", "У курсора"),
        ["TrayIcon"] = ("Tray icon", "У значка в трее"),
        ["WindowCenter"] = ("Window center", "По центру окна"),
        ["ScreenCenter"] = ("Screen center", "По центру экрана"),
        ["LastPosition"] = ("Last position", "В последней позиции"),
        ["ResetPosition"] = ("Reset position", "Сбросить позицию"),
        ["PinTo"] = ("Pin to:", "Закреплённые:"),
        ["Top"] = ("Top", "Сверху"),
        ["Bottom"] = ("Bottom", "Снизу"),
        ["ImageHeight"] = ("Image height:", "Высота изображений:"),
        ["OpenPreviewAutomatically"] = ("Open preview automatically", "Открывать просмотр автоматически"),
        ["PreviewDelay"] = ("Preview delay (ms):", "Задержка просмотра (мс):"),
        ["HighlightMatches"] = ("Highlight matches:", "Подсветка совпадений:"),
        ["Bold"] = ("Bold", "Жирный"),
        ["Italic"] = ("Italic", "Курсив"),
        ["Underline"] = ("Underline", "Подчёркивание"),
        ["Color"] = ("Color", "Цвет"),
        ["ShowSpecialSymbols"] = ("Show special symbols", "Показывать спецсимволы"),
        ["ShowInTray"] = ("Show tray icon", "Показывать значок в трее"),
        ["ShowRecentCopyInTray"] = ("Show recent copy in tray tooltip", "Показывать последнюю копию в подсказке значка"),
        ["ShowSearch"] = ("Show search field", "Показывать поле поиска"),
        ["Always"] = ("Always", "Всегда"),
        ["DuringSearch"] = ("During search", "Во время поиска"),
        ["ShowTitle"] = ("Show title before search field", "Показывать заголовок перед полем поиска"),
        ["ShowApplicationIcons"] = ("Show application icons", "Показывать значки приложений"),
        ["ShowHexColorSwatch"] = ("Show hex color swatch", "Показывать образец цвета для HEX-кодов"),
        ["ShowFooter"] = ("Show footer", "Показывать нижнее меню"),
        ["FooterWarning"] = ("⚠ Press Ctrl+, to open preferences when the footer is hidden.", "⚠ Нажмите Ctrl+, чтобы открыть настройки, когда нижнее меню скрыто."),

        // Pins
        ["Key"] = ("Key", "Клавиша"),
        ["Alias"] = ("Title", "Заголовок"),
        ["Content"] = ("Content", "Содержимое"),
        ["ContentIsNotText"] = ("Non-editable content (image or file)", "Нередактируемое содержимое (изображение или файл)"),
        ["PinDescription"] = ("You can customize the shortcut, title, and content of any pinned item. Only plain text content can be edited; editing discards formatting.", "Можно изменить клавишу, заголовок и содержимое закреплённого элемента. Редактировать можно только обычный текст; форматирование при этом теряется."),

        // Ignore
        ["ApplicationsTab"] = ("Applications", "Приложения"),
        ["IgnoredAppsDescription"] = ("Ignore copies coming from certain applications (by executable name, e.g. KeePass.exe).\nThis is not bullet-proof, so prefer clipboard formats when possible.", "Игнорировать копирование из определённых приложений (по имени файла, например KeePass.exe).\nЭто не абсолютно надёжно, поэтому по возможности используйте форматы буфера."),
        ["IgnoreAllExceptListed"] = ("Ignore all applications except listed", "Игнорировать все приложения, кроме перечисленных"),
        ["FormatsTab"] = ("Clipboard formats", "Форматы буфера"),
        ["IgnoredFormatsDescription"] = ("Ignore clipboard contents that contain certain formats.\nContent marked with ExcludeClipboardContentFromMonitorProcessing or CanIncludeInClipboardHistory = 0 (password managers) is always ignored.", "Игнорировать содержимое буфера, содержащее определённые форматы.\nСодержимое с ExcludeClipboardContentFromMonitorProcessing или CanIncludeInClipboardHistory = 0 (менеджеры паролей) игнорируется всегда."),
        ["RegexpTab"] = ("Regular expressions", "Регулярные выражения"),
        ["IgnoredRegexpDescription"] = ("Ignore copies matching any of these regular expressions.", "Игнорировать копии, совпадающие с любым из этих регулярных выражений."),

        // Advanced
        ["TurnOff"] = ("Turn off", "Выключить"),
        ["TurnOffDescription"] = ("Temporarily ignore all new copies.\nYou can also click the tray icon with Alt pressed.\nTo ignore only the next copy, click with Alt+Shift.", "Временно игнорировать все новые копии.\nТакже можно щёлкнуть значок в трее с зажатым Alt.\nЧтобы пропустить только следующую копию — с Alt+Shift."),
        ["ClearOnQuit"] = ("Clear history on quit", "Очищать историю при выходе"),
        ["ClearOnQuitTooltip"] = ("Automatically remove all unpinned items before quitting.", "Удалять все незакреплённые элементы перед выходом."),
        ["ClearSystemClipboard"] = ("Clear the system clipboard too", "Очищать также системный буфер обмена"),
        ["ClearSystemClipboardTooltip"] = ("When enabled, clearing the history also clears the current system clipboard.", "При очистке истории также очищается текущий системный буфер обмена."),

        // About
        ["AboutDescription"] = ("Winddy is a lightweight clipboard manager for Windows.\nIt keeps the history of what you copy and lets you quickly navigate, search, and use previous clipboard contents.", "Winddy — лёгкий менеджер буфера обмена для Windows.\nОн хранит историю скопированного и позволяет быстро находить и использовать прошлое содержимое буфера."),
        ["AboutCredits"] = ("A Windows port of Maccy by Alex Rodionov (MIT License).", "Порт Maccy (автор — Alex Rodionov, лицензия MIT) для Windows."),
        ["Close"] = ("Close", "Закрыть"),
        ["Version"] = ("Version {0}", "Версия {0}"),

        // Updates
        ["CheckForUpdates"] = ("Check for updates automatically", "Автоматически проверять обновления"),
        ["CheckNow"] = ("Check now", "Проверить сейчас"),
        ["CheckForUpdatesMenu"] = ("Check for updates…", "Проверить обновления…"),
        ["UpdateTitle"] = ("Software Update", "Обновление программы"),
        ["UpdateHeadline"] = ("A new version of Winddy is available!", "Доступна новая версия Winddy!"),
        ["UpdateVersions"] = ("Winddy {0} is now available — you have {1}. Would you like to install it now?", "Доступна версия Winddy {0}, у вас установлена {1}. Установить сейчас?"),
        ["ReleaseNotes"] = ("Release notes:", "Что нового:"),
        ["SkipVersion"] = ("Skip this version", "Пропустить эту версию"),
        ["RemindLater"] = ("Remind me later", "Напомнить позже"),
        ["InstallUpdate"] = ("Install and relaunch", "Установить и перезапустить"),
        ["OpenReleasePage"] = ("Open download page", "Открыть страницу загрузки"),
        ["UpdateFailed"] = ("Could not install the update: {0}", "Не удалось установить обновление: {0}"),
        ["UpToDate"] = ("You're up to date! Winddy {0} is the newest version.", "У вас последняя версия Winddy ({0})."),
        ["CheckFailed"] = ("Could not check for updates: {0}", "Не удалось проверить обновления: {0}"),

        // Notifications, editing, paste stack, OCR
        ["NotificationsAndSounds"] = ("Notifications:", "Уведомления:"),
        ["ShowNotifications"] = ("Show a notification when something is copied", "Показывать уведомление при копировании"),
        ["PlaySounds"] = ("Play sounds", "Воспроизводить звуки"),
        ["EditItem"] = ("Edit…", "Изменить…"),
        ["SaveButton"] = ("Save", "Сохранить"),
        ["RichTextEditWarning"] = ("Editing will discard all formatting.", "При изменении форматирование будет потеряно."),
        ["CopyExtractedText"] = ("Copy text from image", "Скопировать текст с изображения"),
        ["StopPasteStack"] = ("Stop paste stack", "Остановить стек вставки"),
        ["PasteStackLeft"] = ("Paste stack: {0} left — press Ctrl+V", "Стек вставки: осталось {0} — нажимайте Ctrl+V"),
    };

    public static bool IsRussian { get; private set; }

    public static void Init(string? language)
    {
        var lang = string.IsNullOrEmpty(language) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : language;
        IsRussian = lang == "ru";
    }

    public static string T(string key) =>
        Strings.TryGetValue(key, out var s) ? (IsRussian ? s.Ru : s.En) : key;

    public static string T(string key, params object[] args) => string.Format(T(key), args);
}

/// <summary>XAML usage: Text="{l:Tr Key}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
