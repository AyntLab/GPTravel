using Microsoft.Maui.Storage;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Storage;
using System.Text;

namespace GPTravel
{
    public partial class LogPage : ContentPage
    {
        public ObservableCollection<TravelLog> Logs { get; set; } = new();

        private string _dbPath = Path.Combine(FileSystem.AppDataDirectory, "log.db");
        private int _currentPage = 0;
        private const int PageSize = 20;

        public LogPage()
        {
            InitializeComponent();
            BindingContext = this;
            LoadLogs();
            InitializeDatePickers();
        }

        private void InitializeDatePickers()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                // travel_log テーブルの最古・最新の日付を取得
                string sql = "SELECT MIN(datetime), MAX(datetime) FROM travel_log;";
                using var cmd = new SqliteCommand(sql, connection);
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                    {
                        DateTime minDate = DateTime.Parse(reader.GetString(0));
                        DateTime maxDate = DateTime.Parse(reader.GetString(1));

                        startDatePicker.Date = minDate;
                        endDatePicker.Date = maxDate;
                    }
                    else
                    {
                        // データがない場合は今日の日付を設定
                        startDatePicker.Date = DateTime.Today;
                        endDatePicker.Date = DateTime.Today;
                    }
                }
            }
            catch (Exception ex)
            {
                DisplayAlert("Error", $"Failed to load date range:\n{ex.Message}", "OK");
                startDatePicker.Date = DateTime.Today;
                endDatePicker.Date = DateTime.Today;
            }
        }

        private void OnDateRangeChanged(object sender, DateChangedEventArgs e)
        {
            // ページをリセットして再読込
            _currentPage = 0;
            LoadLogs();
        }

        private void LoadLogs()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                int offset = _currentPage * PageSize;

                // 🔸 From～Toの日付フィルターを適用
                string start = startDatePicker?.Date.ToString("yyyy-MM-dd") ?? "1900-01-01";
                string end = endDatePicker?.Date.AddDays(1).ToString("yyyy-MM-dd") ?? "2100-01-01";

                string query = $@"
            SELECT datetime, gps, model, prompt, latitude, longitude, response
            FROM travel_log
            WHERE datetime BETWEEN '{start}' AND '{end}'
            ORDER BY datetime DESC
            LIMIT {PageSize} OFFSET {offset};";

                using var command = new SqliteCommand(query, connection);
                using var reader = command.ExecuteReader();

                Logs.Clear();
                while (reader.Read())
                {
                    Logs.Add(new TravelLog
                    {
                        DateTime = reader.GetString(0),
                        Gps = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Model = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Prompt = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Latitude = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4),
                        Longitude = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
                        Response = reader.IsDBNull(6) ? "" : reader.GetString(6)
                    });
                }
            }
            catch (Exception ex)
            {
                DisplayAlert("Error", $"Failed to load logs:\n{ex.Message}", "OK");
            }
        }


        protected override void OnAppearing()
        {
            base.OnAppearing();
            Shell.SetNavBarIsVisible(this, false);
        }

        private void OnPreviousClicked(object sender, EventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                LoadLogs();
            }
            else
            {
                DisplayAlert("Notice", "You are on the first page.", "OK");
            }
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            _currentPage++;
            LoadLogs();

            if (Logs.Count == 0)
            {
                _currentPage--;
                DisplayAlert("Notice", "No more records.", "OK");
                LoadLogs();
            }
        }

        private async void OnPromptClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is TravelLog log)
            {
                bool copy = await DisplayAlert("Prompt Used", log.Prompt, "Copy", "Close");
                if (copy)
                {
                    await Clipboard.SetTextAsync(log.Prompt);
                    await DisplayAlert("Copied", "Prompt copied to clipboard.", "OK");
                }
            }
        }

        private async void OnMapClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is TravelLog log)
            {
                if (log.Latitude == 0 && log.Longitude == 0)
                {
                    await DisplayAlert("Map", "No location data recorded.", "OK");
                    return;
                }

                var uri = new Uri($"https://www.google.com/maps?q={log.Latitude},{log.Longitude}");
                await Launcher.OpenAsync(uri);
            }
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is TravelLog log)
            {
                bool confirm = await DisplayAlert("Delete", "Do you really want to delete this log?", "Delete", "Cancel");
                if (!confirm) return;

                try
                {
                    using var connection = new SqliteConnection($"Data Source={_dbPath}");
                    connection.Open();

                    string sql = "DELETE FROM travel_log WHERE datetime = @dt AND gps = @gps";
                    using var cmd = new SqliteCommand(sql, connection);
                    cmd.Parameters.AddWithValue("@dt", log.DateTime);
                    cmd.Parameters.AddWithValue("@gps", log.Gps);
                    cmd.ExecuteNonQuery();

                    if (logList.ItemsSource is ObservableCollection<TravelLog> logs)
                        logs.Remove(log);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to delete:\n{ex.Message}", "OK");
                }
            }
        }

        private async void OnExportKmlClicked(object sender, EventArgs e)
        {
            try
            {
                string startDisp = startDatePicker.Date.ToString("yyyy-MM-dd");
                string endDisp = endDatePicker.Date.ToString("yyyy-MM-dd");

                string start = startDatePicker.Date.ToString("yyyy-MM-dd");
                string end = endDatePicker.Date.AddDays(1).ToString("yyyy-MM-dd");

                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                string query = $@"
            SELECT datetime, model, latitude, longitude, response
            FROM travel_log
            WHERE datetime BETWEEN '{start}' AND '{end}'
            ORDER BY datetime DESC;";

                using var cmd = new SqliteCommand(query, connection);
                using var reader = cmd.ExecuteReader();

                var sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
                sb.AppendLine("<Document>");
                sb.AppendLine($"  <name>Travel Log {startDisp}～{endDisp}</name>");

                while (reader.Read())
                {
                    string dt = reader.GetString(0);
                    string model = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    double lat = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                    double lng = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                    string resp = reader.IsDBNull(4) ? "" : reader.GetString(4);

                    if (lat == 0 && lng == 0) continue; // 座標がない場合はスキップ

                    string safeDesc = System.Security.SecurityElement.Escape(resp);
                    string title = $"{dt} ({model})";

                    sb.AppendLine("  <Placemark>");
                    sb.AppendLine($"    <name>{System.Security.SecurityElement.Escape(title)}</name>");
                    sb.AppendLine($"    <description>{safeDesc}</description>");
                    sb.AppendLine("    <Point>");
                    sb.AppendLine($"      <coordinates>{lng:F5},{lat:F5},0</coordinates>");
                    sb.AppendLine("    </Point>");
                    sb.AppendLine("  </Placemark>");
                }

                sb.AppendLine("</Document>");
                sb.AppendLine("</kml>");

                // メモリ上に作成
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

                // 🔸 保存ダイアログを表示
                string suggested = $"travel_log_{DateTime.Now:yyyyMMdd_HHmmss}.kml";
                var result = await FileSaver.Default.SaveAsync(suggested, ms, CancellationToken.None);

                if (result.IsSuccessful)
                {
                    await DisplayAlert("KML Exported",
                        $"Exported period:\n{startDisp} ～ {endDisp}\n\nSaved to:\n{result.FilePath}",
                        "OK");
                }
                else
                {
                    var msg = result.Exception is null ? "Canceled." : $"Failed: {result.Exception.Message}";
                    await DisplayAlert("KML Export", msg, "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to export KML:\n{ex.Message}", "OK");
            }
        }
        private async void OnExportCsvClicked(object sender, EventArgs e)
        {
            try
            {
                // 🔸 期間（表示用は当日そのまま）
                var startDisp = startDatePicker.Date.ToString("yyyy-MM-dd");
                var endDisp = endDatePicker.Date.ToString("yyyy-MM-dd");

                // 🔸 抽出用（end は当日を含めるため +1日）
                string start = startDatePicker.Date.ToString("yyyy-MM-dd");
                string end = endDatePicker.Date.AddDays(1).ToString("yyyy-MM-dd");

                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                string query = $@"
            SELECT datetime, model, latitude, longitude, prompt, response
            FROM travel_log
            WHERE datetime BETWEEN '{start}' AND '{end}'
            ORDER BY datetime DESC;";

                using var cmd = new SqliteCommand(query, connection);
                using var reader = cmd.ExecuteReader();

                // まずメモリ上にCSVを作る
                using var ms = new MemoryStream();
                using (var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
                {
                    writer.WriteLine("date,model,latitude,longitude,prompt,response");

                    while (reader.Read())
                    {
                        string dt = reader.GetString(0).Replace(",", " ");
                        string model = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        double lat = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                        double lng = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                        string prompt = reader.IsDBNull(4) ? "" : reader.GetString(4).Replace("\r", " ").Replace("\n", " ");
                        string resp = reader.IsDBNull(5) ? "" : reader.GetString(5).Replace("\r", " ").Replace("\n", " ");

                        writer.WriteLine($"{dt},{model},{lat:F5},{lng:F5},\"{prompt}\",\"{resp}\"");
                    }
                    writer.Flush();
                }
                ms.Position = 0; // 先頭へ

                // 🔸 保存ダイアログを表示（Android でもOK）
                string suggested = $"travel_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var result = await FileSaver.Default.SaveAsync(suggested, ms, CancellationToken.None);

                if (result.IsSuccessful)
                {
                    await DisplayAlert("CSV Exported",
                        $"Exported period:\n{startDisp} ～ {endDisp}\n\nSaved to:\n{result.FilePath}",
                        "OK");
                }
                else
                {
                    // ユーザーキャンセルや失敗時
                    var msg = result.Exception is null ? "Canceled." : $"Failed: {result.Exception.Message}";
                    await DisplayAlert("CSV Export", msg, "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to export CSV:\n{ex.Message}", "OK");
            }
        }


        public class TravelLog
        {
            public string DateTime { get; set; } = "";
            public string Gps { get; set; } = "";
            public string Model { get; set; } = "";
            public string Prompt { get; set; } = "";
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public string Response { get; set; } = "";
        }
    }
}
