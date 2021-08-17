using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AngleSharp;
using AngleSharp.Dom;
using FolderBrowserDialog = FolderBrowserEx.FolderBrowserDialog;

namespace HozyaHtmlParser
{
    class Program
    {
        /*
         *  1) autosave - отключает диалог сохранения изображений
         *  2) 88005553535 - авто вставка номера телефона
         *  3) "http:\\hozya.ru" - адрес страницы объявления
         * 1
         */

        private static readonly GlobalSettings MyGlobalSettings = new GlobalSettings();

        private static readonly Regex PhoneCheckerRegex =
            new Regex(@"^((8|\+7)[\- ]?)?(\(?\d{3}\)?[\- ]?)?[\d\- ]{7,10}$");

        private static readonly Regex WebAddressCheckerRegex = new Regex(@"(https?:/{1,2})?\w+\.\w{2,255}.*");
        private static readonly Regex FileNamePickerRegex = new Regex(@"(?<filename>[^/]+\.\w+)$");
        private static readonly Regex PricePickerRegex = new Regex(@"(?<price>(\d+ *)+[а-яА-Я]+)");
        private static readonly Regex ApartmentTagPickerRegex = new Regex(@"^\d*[а-яА-Я]+");
        private static readonly Regex DigitCheckerRegex = new Regex(@"\d+");

        //private static readonly List<string> UserParams = new List<string>();

        [STAThread]
        static async Task Main(string[] args)
        {
            await SetupMenu("Hozya.ru html parser by TECTRO v1.2.2",
                new List<MenuItem>
                {
                    new MenuItem(
                        "1) Инструкция", async item =>
                        {
                            Console.Clear();
                            try
                            {
                                using (StreamReader sr = new StreamReader("README.txt"))
                                {
                                    ShowMessage(sr.ReadToEnd(), ConsoleColor.Green);
                                }
                            }
                            catch
                            {
                                ShowMessage("Файл README не найден!");
                            }

                            Console.ReadKey();
                            return false;
                        }),
                    new MenuItem(
                        "2) Запуск", async item =>
                        {
                            Console.Clear();

                            bool isExit = false;
                            do
                            {
                                ShowMessage("Добро пожадвать в рабочее окно парсера", ConsoleColor.Green);
                                ShowMessage(
                                    "   Ознакомиться с инструкцией по использованию можно в разделе 'инструкция' основного меню",
                                    ConsoleColor.Green);
                                ShowMessage("   для выхода в меню введите 'вых' после завершения скрипта",
                                    ConsoleColor.Green);
                                ShowMessage("\nВведите ссылку на сайт Hozya.ru:", ConsoleColor.Green);

                                bool crop = MyGlobalSettings.CropImage;
                                string phoneNumber = MyGlobalSettings.DefaultPhoneNumber;
                                string webAddress = Console.ReadLine();

                                if (webAddress == "вых")
                                    return false;

                                IDocument document = await LoadWebDocument(webAddress);

                                if (!(document is null))
                                {
                                    ShowMessage("Документ успешно загружен", ConsoleColor.Green);
                                    //////////////////////////////////////////////////////////////////////

                                    var pageName = GetPageName(document);

                                    var textTitle = GetTextTitle(document);
                                    var price = PricePickerRegex
                                        .Match(document.QuerySelector(".b-details__price").InnerHtml)
                                        .Groups["price"].Value;
                                    var mainText = document.QuerySelector("p[class='b-details__note noselect']")
                                        .InnerHtml;
                                    string apartmentTag = ApartmentTagPickerRegex.Match(pageName).Value;

                                    #region Сборка сообщения

                                    var bufferedText = new StringBuilder()
                                        .Append(textTitle)
                                        .Append('\n')
                                        .Append($"{Constants.PriceTextPart} {price}")
                                        .Append('\n')
                                        .Append($"{Constants.PhoneTextPart} {phoneNumber}")
                                        .Append('\n')
                                        .Append(mainText)
                                        .Append('\n')
                                        .Append($"#{GetFirstTagPart(apartmentTag)}{Constants.EndingTagPart}")
                                        .ToString();

                                    #endregion

                                    var clipboardThread = new Thread(() =>
                                    {
                                        Clipboard.Clear();
                                        Clipboard.SetText(bufferedText, TextDataFormat.UnicodeText);
                                        ShowMessage("Текст скопирован в буфер обмена", ConsoleColor.Cyan);
                                    });
                                    clipboardThread.SetApartmentState(ApartmentState.STA);
                                    clipboardThread.Start();

                                    var imageUris = GetImageUris(document).ToList();

                                    #region Отображение адресов изображений

                                    ShowMessage($"Распознано изображений: {imageUris.Count}", ConsoleColor.Green);
                                    foreach (var imageUri in imageUris)
                                    {
                                        ShowMessage($"   {imageUri}", ConsoleColor.Green);
                                    }

                                    #endregion

                                    string folderPath = GetFolderPath(Constants.DefaultFilePath);


                                    DownloadFiles(imageUris, folderPath, crop);
                                    var insertHeaderResult = InsertHeader(apartmentTag, folderPath);
                                    ShowMessage($"Изображения загружены в {new DirectoryInfo(folderPath).FullName}",
                                        ConsoleColor.Cyan);
                                    ShowMessage(
                                        insertHeaderResult
                                            ? "Заголовок добавлен"
                                            : $"Файл или каталог с заголовками в {new DirectoryInfo(Constants.DefaultHeadersPath).FullName} не найден",
                                        ConsoleColor.DarkCyan);
                                }
                                else
                                    ShowMessage("Веб адрес не распознан");

                                Console.WriteLine();
                            } while (!isExit);

                            return false;
                        }),
                    new MenuItem(
                        "3) Настройки", async (pos) =>
                        {
                            await SetupMenu("Настройки:",
                                new List<MenuItem>
                                {
                                    new SettingsMenuItem<bool>(
                                        "1) Кроп",
                                        MyGlobalSettings.CropImage,
                                        value => value ? "включен" : "выключен", async item =>
                                        {
                                            if (item is SettingsMenuItem<bool> settingsItem)
                                            {
                                                settingsItem.SettingsValue = !settingsItem.SettingsValue;

                                                MyGlobalSettings.CropImage = settingsItem.SettingsValue;
                                            }

                                            return false;
                                        }),

                                    new SettingsMenuItem<string>(
                                        "2) Подстановка номера телефона",
                                        MyGlobalSettings.DefaultPhoneNumber,
                                        value => value, async item =>
                                        {
                                            Console.Clear();
                                            ShowMessage("Введите номер телефона: ", ConsoleColor.Green);
                                            if (item is SettingsMenuItem<string> settingsItem)
                                            {
                                                var tempPhoneNumber = Console.ReadLine();

                                                if (string.IsNullOrEmpty(tempPhoneNumber))
                                                {
                                                    settingsItem.SettingsValue = "";

                                                    MyGlobalSettings.DefaultPhoneNumber = "";

                                                    ShowMessage("Номер стерт", ConsoleColor.Cyan);
                                                }
                                                else
                                                {
                                                    if (PhoneCheckerRegex.IsMatch(
                                                        !string.IsNullOrEmpty(tempPhoneNumber)
                                                            ? tempPhoneNumber
                                                            : ""))
                                                    {
                                                        MyGlobalSettings.DefaultPhoneNumber = tempPhoneNumber;

                                                        settingsItem.SettingsValue = tempPhoneNumber;

                                                        ShowMessage("Номер обновлен", ConsoleColor.Cyan);
                                                    }
                                                    else
                                                        ShowMessage("Неверный формат номера");
                                                }

                                                Console.ReadKey();
                                            }

                                            return false;
                                        }),

                                    new MenuItem(
                                        "3) Назад", async item => { return true; })
                                });
                            return false;
                        }),
                    new MenuItem(
                        "4) Выход", async pos => true)
                });
        }

        static async Task<IDocument> LoadWebDocument(string webAddress)
        {
            if (string.IsNullOrWhiteSpace(webAddress)) return null;
            try
            {
                var config = Configuration.Default.WithDefaultLoader();
                return await BrowsingContext.New(config).OpenAsync(webAddress);
            }
            catch
            {
                return null;
            }
        }

        static IEnumerable<string> GetImageUris(IDocument document)
        {
            return document.QuerySelectorAll(".tns-lazy-img")
                .Select(element => element
                    .Attributes
                    .FirstOrDefault(attr => attr
                        .Name
                        .Equals(
                            Constants.FullImageAttrName,
                            StringComparison.CurrentCultureIgnoreCase))
                    ?.Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();
        }

        static string GetFileNameFromUri(string uri)
        {
            return FileNamePickerRegex.Match(uri).Groups["filename"].Value;
        }

        static string GetPageName(IDocument document)
        {
            var rawPageName = document.QuerySelector("p[class='b-details__title visible-xs noselect']").InnerHtml;
            return string.Join(',', rawPageName.Split(',').SkipLast(1));
        }

        static string GetTextTitle(IDocument document)
        {
            var rawPageName = document.QuerySelector("h1[class='b-details__title hidden-xs noselect']").InnerHtml;
            return string.Join(',', rawPageName.Split(',').SkipLast(1));
        }

        static string GetUserFilePath()
        {
            string path = null;
            var dialog = new FolderBrowserDialog();
            dialog.AllowMultiSelect = false;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                path = dialog.SelectedFolder;
            }

            return path;
        }

        static IEnumerable<FileInfo> GetHeadersFiles()
        {
            var HeadersDirectory = Constants.DefaultHeadersPath;
            if (Directory.Exists(HeadersDirectory))
                return new DirectoryInfo(HeadersDirectory).GetFiles();
            return new List<FileInfo>();
        }

        static string GetFolderPath(string defaultPath, bool autoSave = true, bool cleanfolder = true)
        {
            string folderPath = defaultPath;
            if (!autoSave)
            {
                var userPath = GetUserFilePath();
                if (!string.IsNullOrWhiteSpace(userPath))
                    folderPath = userPath;
            }

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
            else
            {
                var dir = new DirectoryInfo(folderPath);
                foreach (var fileInfo in dir.GetFiles())
                {
                    fileInfo.Delete();
                }

                foreach (var directoryInfo in dir.GetDirectories())
                {
                    directoryInfo.Delete();
                }
            }

            return folderPath;
        }

        static bool InsertHeader(string apartmentTag, string folderToInsert)
        {
            var selectedHeader = GetHeadersFiles().FirstOrDefault(info =>
                info.Name.Contains(apartmentTag, StringComparison.CurrentCultureIgnoreCase));

            selectedHeader?.CopyTo($"{folderToInsert}\\zzzz_{apartmentTag}.{selectedHeader.Extension}");
            return selectedHeader != null;
        }

        static void DownloadFiles(IEnumerable<string> fileUris, string folder, bool cropWaterMark = false)
        {
            var token = new CancellationToken();

            if (!cropWaterMark)
            {
                Task.WaitAll(
                    fileUris.Select(imageUri =>
                        {
                            var task = new Task(() =>
                            {
                                try
                                {
                                    var rawUri = new Uri(imageUri);
                                    var correctedUri = new UriBuilder
                                        { Scheme = Uri.UriSchemeHttps, Path = rawUri.AbsolutePath, Host = rawUri.Host }.Uri;

                                    new WebClient()
                                        .DownloadFileTaskAsync(
                                            correctedUri,
                                            $"{folder}\\{GetFileNameFromUri(correctedUri.ToString())}"
                                        );
                                }
                                catch
                                {
                                    ShowMessage($"непредвиденная ошибка загрузки изображения по адресу {imageUri}");
                                }
                            });
                            task.Start();
                            return task;
                        })
                        .ToArray(), token);
            }
            else
            {
                List<Task> tasks = new List<Task>();
                foreach (var fileUri in fileUris)
                {
                    var loadTask = new Task(async () =>
                        {
                            try
                            {
                                using HttpClient httpClient = new HttpClient();

                                var rawUri = new Uri(fileUri);
                                var correctedUri = new UriBuilder
                                    {Scheme = Uri.UriSchemeHttps, Path = rawUri.AbsolutePath, Host = rawUri.Host}.Uri;
                                
                                Stream stream = await httpClient.GetStreamAsync(correctedUri); //client.OpenRead(fileUri);
                                var bitmap = new Bitmap(stream);

                                bitmap
                                    .Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height - 45), bitmap.PixelFormat)
                                    .Save($"{folder}\\{GetFileNameFromUri(fileUri)}", ImageFormat.Jpeg);
                            }
                            catch
                            {
                                ShowMessage($"непредвиденная ошибка загрузки изображения по адресу {fileUri}");
                            }
                        }
                    );
                    tasks.Add(loadTask);
                    loadTask.Start();
                }


                Task.WaitAll(tasks.ToArray());
            }

            token.ThrowIfCancellationRequested();
        }

        static string GetFirstTagPart(string apartmentTag)
        {
            string firstHashTagPart;
            if (DigitCheckerRegex.IsMatch(apartmentTag))
                firstHashTagPart = DigitCheckerRegex.Match(apartmentTag).Value + Constants.ApartmentTagPart;
            else if (apartmentTag.Equals("комн", StringComparison.CurrentCultureIgnoreCase))
                firstHashTagPart = Constants.RoomTagPart;
            else
                firstHashTagPart = Constants.HouseTagPart;
            return firstHashTagPart;
        }

        static void ShowMessage(string message, ConsoleColor color = ConsoleColor.Red)
        {
            var defaultConsoleColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = defaultConsoleColor;
        }

        static void ShowMessage(string message, ConsoleColor foregroundColor, ConsoleColor backgroundColor)
        {
            var defaultBackgroundColor = Console.BackgroundColor;
            Console.BackgroundColor = backgroundColor;
            ShowMessage(message, foregroundColor);
            Console.BackgroundColor = defaultBackgroundColor;
        }

        static void ShowSelectableMessage(string message, bool isSelect = false,
            ConsoleColor color1 = ConsoleColor.Green, ConsoleColor color2 = ConsoleColor.Black)
        {
            if (isSelect)
                ShowMessage(message, color2, color1);
            else
                ShowMessage(message, color1);
        }

        class MenuItem
        {
            public delegate bool MenuItemAction(MenuItem item);

            public virtual string Title { get; set; }
            public Func<MenuItem, Task<bool>> Action { get; }

            public MenuItem(string title, Func<MenuItem, Task<bool>> action)
            {
                Action = action;
                Title = title;
            }
        }

        class SettingsMenuItem<T> : MenuItem
        {
            public delegate string ValueInterpolator<in TT>(TT value);

            private readonly ValueInterpolator<T> _interpolator;

            private string _title;
            public T SettingsValue { get; set; }

            public override string Title
            {
                get => $"{_title}: {_interpolator(SettingsValue)}";
                set => _title = value;
            }

            public SettingsMenuItem(string title, T defaultValue, ValueInterpolator<T> interpolator,
                Func<MenuItem, Task<bool>> action) : base(title, action)
            {
                _interpolator = interpolator;
                SettingsValue = defaultValue;
            }
        }

        class GlobalSettings
        {
            public bool CropImage { get; set; }
            public string DefaultPhoneNumber { get; set; }

            public GlobalSettings()
            {
                CropImage = true;
            }
        }

        static async Task SetupMenu(string header, List<MenuItem> menuItems,
            ConsoleColor color1 = ConsoleColor.Green,
            ConsoleColor color2 = ConsoleColor.Black)
        {
            Console.Clear();

            ShowMessage(header, color1);
            Console.WriteLine();

            int selector = 0;
            bool isExit = false;

            bool isSubMenuEntered = false;

            do
            {
                if (isSubMenuEntered)
                {
                    Console.Clear();
                    ShowMessage(header, color1);
                    Console.WriteLine();
                }
                else if (Console.CursorTop - menuItems.Count >= 0)
                    Console.SetCursorPosition(0, Console.CursorTop - menuItems.Count);

                for (int i = 0; i < menuItems.Count; i++)
                    ShowSelectableMessage(menuItems[i].Title, selector == i, color1, color2);

                switch (Console.ReadKey().Key)
                {
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.S:
                    {
                        if (selector < menuItems.Count - 1)
                            selector++;
                        isSubMenuEntered = false;
                    }
                        break;

                    case ConsoleKey.UpArrow:
                    case ConsoleKey.W:
                    {
                        if (selector > 0)
                            selector--;
                        isSubMenuEntered = false;
                    }
                        break;

                    case ConsoleKey.Enter:
                    {
                        isExit = await menuItems[selector].Action(menuItems[selector]);
                        isSubMenuEntered = true;
                    }
                        break;

                    case ConsoleKey.Escape:
                    case ConsoleKey.Backspace:
                    {
                        isExit = true;
                    }
                        break;
                }
            } while (!isExit);
        }
    }
}