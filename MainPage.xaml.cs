using System.IO;
using GPTravel.Resources.Strings;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.Content;
using GPTravel.Platforms.Android.Services;
#endif

namespace GPTravel
{
    public partial class MainPage : ContentPage
    {
        private string _dbPath;

        public MainPage()
        {
            InitializeComponent();

            // SQLite 初期化
            _dbPath = Path.Combine(FileSystem.AppDataDirectory, "log.db");
            InitializeDatabase();

            // 設定ロード
            LoadSettings();

            // 入力変更イベント登録
            txtApiKey.TextChanged += OnSettingChanged;
            pickerModel.SelectedIndexChanged += OnModelChanged;
            editorPrompt.TextChanged += OnSettingChanged;
            pickerInterval.SelectedIndexChanged += OnSettingChanged;
            pickerLanguage.SelectedIndexChanged += OnLanguageChanged;

        }

        // ====== 設定の保存・読み込み ======
        private void LoadSettings()
        {
            txtApiKey.Text = Preferences.Get("ApiKey", string.Empty);
            editorPrompt.Text = Preferences.Get("Prompt", string.Empty);

            string model = Preferences.Get("Model", "gpt-4o");
            pickerModel.SelectedItem = model;

            string interval = Preferences.Get("Interval", "5");
            pickerInterval.SelectedItem = interval;

            string lang = Preferences.Get("Language", "日本語");
            pickerLanguage.SelectedItem = lang;

            LocalizationResourceManager.SetCulture(lang);

            ApplyLocalization();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (pickerLanguage.SelectedItem is string lang)
            {
                Preferences.Set("Language", lang);
                LocalizationResourceManager.SetCulture(lang);
                ApplyLocalization();
            }
        }


        private void ApplyLocalization()
        {
            Title = LocalizationResourceManager.GetString("Title");
            btnStartForeground.Text = LocalizationResourceManager.GetString("Start");
            btnShowLogs.Text = LocalizationResourceManager.GetString("Log");

            // XAML上のラベルやピッカーTitleをローカライズ
            txtApiKey.Placeholder = LocalizationResourceManager.GetString("ApiKeyPlaceholder");
            pickerLanguage.Title = LocalizationResourceManager.GetString("LanguagePickerTitle");
            pickerModel.Title = LocalizationResourceManager.GetString("ModelPickerTitle");
            pickerInterval.Title = LocalizationResourceManager.GetString("IntervalPickerTitle");

            // ラベル系（Textはx:Name付与が必要）
            lblApiKey.Text = LocalizationResourceManager.GetString("ApiKey");
            lblLanguage.Text = LocalizationResourceManager.GetString("Language");
            lblModel.Text = LocalizationResourceManager.GetString("Model");
            lblPrompt.Text = LocalizationResourceManager.GetString("Prompt");
            lblInterval.Text = LocalizationResourceManager.GetString("Interval");
            lblInfoLocation.Text = LocalizationResourceManager.GetString("Info_LocationAndTimeNotice");

            btnExamplePrompt.Text = LocalizationResourceManager.GetString("ExampleSentence");
        }

        private async void OnPaymentHelpClicked(object sender, EventArgs e)
        {
            string message = AppResources.PaymentRequiredNotice;
            await DisplayAlert("Info", message, "OK");
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            Preferences.Set("ApiKey", txtApiKey.Text);
            Preferences.Set("Prompt", editorPrompt.Text);
            if (pickerInterval.SelectedItem != null)
                Preferences.Set("Interval", pickerInterval.SelectedItem.ToString());
        }

        private void OnModelChanged(object sender, EventArgs e)
        {
            if (pickerModel.SelectedItem != null)
                Preferences.Set("Model", pickerModel.SelectedItem.ToString());
        }

        // ====== SQLite 初期化 ======
        private void InitializeDatabase()
        {
            try
            {
                if (!File.Exists(_dbPath))
                {
                    using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                    {
                        connection.Open();
                        string createTable = @"
                            CREATE TABLE IF NOT EXISTS travel_log (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            datetime TEXT NOT NULL,
                            gps TEXT,
                            latitude REAL,
                            longitude REAL,
                            model TEXT,
                            prompt TEXT,
                            response TEXT
                        );";
                        using (var command = new SqliteCommand(createTable, connection))
                            command.ExecuteNonQuery();
                    }

                    DisplayAlert("初期化", "ログデータベースを新規作成しました。", "OK");
                }
            }
            catch (Exception ex)
            {
                DisplayAlert("エラー", $"データベース初期化に失敗しました:\n{ex.Message}", "OK");
            }
        }

        private async void OnGetApiKeyClicked(object sender, EventArgs e)
        {
            try
            {
                var uri = new Uri("https://platform.openai.com/account/api-keys");
                await Launcher.OpenAsync(uri);
            }
            catch (Exception ex)
            {
                await DisplayAlert("エラー", $"ブラウザを開けませんでした:\n{ex.Message}", "OK");
            }
        }

        private void OnExamplePromptClicked(object sender, EventArgs e)
        {
            // 例文（BasePrompt_Default）を取得
            string exampleText = LocalizationResourceManager.GetString("BasePrompt_Default");

            if (!string.IsNullOrEmpty(exampleText))
            {
                editorPrompt.Text = exampleText;
                Preferences.Set("Prompt", exampleText);
            }
            else
            {
                DisplayAlert("Error", "Example prompt not found.", "OK");
            }
        }


        private void OnStartForegroundClicked(object sender, EventArgs e)
        {
#if ANDROID
    try
    {
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(GPTravel.Platforms.Android.Services.TravelForegroundService));
        context.StartForegroundService(intent);

        // MainPageを最小化（ホーム画面に戻す）
        var mainActivity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        Intent homeIntent = new Intent(Intent.ActionMain);
        homeIntent.AddCategory(Intent.CategoryHome);
        homeIntent.SetFlags(ActivityFlags.NewTask);
        mainActivity.StartActivity(homeIntent);
    }
    catch (Exception ex)
    {
        DisplayAlert("エラー", $"Foreground起動に失敗しました: {ex.Message}", "OK");
    }
#endif
        }



        private async void OnShowLogsClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new LogPage());
        }



    }
}