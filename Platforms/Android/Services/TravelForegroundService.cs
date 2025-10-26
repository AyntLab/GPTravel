using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

using Android.App;
using Android.Content;
using Android.Gms.Extensions;
using Android.Gms.Location;
using Android.OS;
using AndroidX.Core.App;

using Android.Media;

using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

// ==== 衝突回避用エイリアス ====
using ALog = global::Android.Util.Log;
using ALocation = global::Android.Locations.Location;
using JLocale = global::Java.Util.Locale;

namespace GPTravel.Platforms.Android.Services
{
    [Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeLocation)]
    public class TravelForegroundService : Service, global::Android.Speech.Tts.TextToSpeech.IOnInitListener
    {
        private const int NotificationId = 1001;
        private const string ChannelId = "gptravel_channel";

        private Handler? _handler;
        private Action? _taskAction;
        private global::Android.Speech.Tts.TextToSpeech? _tts;
        private IFusedLocationProviderClient? _fusedLocation;
        private CancellationTokenSource? _cts;
        private string _dbPath = Path.Combine(FileSystem.AppDataDirectory, "log.db");

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            CreateNotificationChannel();

            var notificationIntent = new Intent(this, typeof(MainActivity));
            var pendingIntent = PendingIntent.GetActivity(
                this, 0, notificationIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

            // 小アイコンは drawable/mipmap を使用（raw は不可）
            var notification = new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("GPTraveling...")
                .SetContentText("AI is active")
                .SetSmallIcon(global::GPTravel.Resource.Drawable.ic_fore)
                .SetOngoing(true)
                .SetContentIntent(pendingIntent)
                .Build();

            StartForeground(NotificationId, notification);

            _tts = new global::Android.Speech.Tts.TextToSpeech(this, this);
            _fusedLocation = LocationServices.GetFusedLocationProviderClient(this);
            _handler = new Handler(Looper.MainLooper);
            _cts = new CancellationTokenSource();

            StartPeriodicTask();
            return StartCommandResult.Sticky;
        }

        public void OnInit(global::Android.Speech.Tts.OperationResult status)
        {
            if (status == global::Android.Speech.Tts.OperationResult.Success)
            {
                string lang = Preferences.Get("Language", "日本語");
                JLocale locale;

                switch (lang)
                {
                    case "日本語":
                    case "Japanese":
                        locale = new JLocale("ja", "JP");
                        break;

                    case "English":
                        locale = new JLocale("en", "US");
                        break;

                    case "Français":
                    case "French":
                        locale = new JLocale("fr", "FR");
                        break;

                    case "Deutsch":
                    case "German":
                        locale = new JLocale("de", "DE");
                        break;

                    case "Español":
                    case "Spanish":
                        locale = new JLocale("es", "ES");
                        break;

                    default:
                        locale = new JLocale("en", "US"); // fallback
                        break;
                }

                var result = _tts?.SetLanguage(locale);

                
            }
        }

        private void UpdateNotification(string message)
        {
            try
            {
                var notificationIntent = new Intent(this, typeof(MainActivity));
                var pendingIntent = PendingIntent.GetActivity(
                    this, 0, notificationIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

                var notification = new NotificationCompat.Builder(this, ChannelId)
                    .SetContentTitle("GPTraveling...")
                    .SetContentText(message)
                    .SetSmallIcon(global::GPTravel.Resource.Drawable.ic_fore)
                    .SetOngoing(true)
                    .SetContentIntent(pendingIntent)
                    .Build();

                var manager = (NotificationManager)GetSystemService(NotificationService)!;
                manager.Notify(NotificationId, notification);
            }
            catch (Exception ex)
            {
                ALog.Warn("GPTravel", $"通知更新失敗: {ex.Message}");
            }
        }


        private void StartPeriodicTask()
        {
            _taskAction = async () =>
            {
                await ExecuteTravelTask();

                int intervalMin = int.TryParse(Preferences.Get("Interval", "5"), out int i) ? i : 5;
                _handler?.PostDelayed(_taskAction!, intervalMin * 60 * 1000);
            };

            _handler?.Post(_taskAction!);
        }

        private async Task ExecuteTravelTask()
        {
            try
            {
                var (gpsDesc, lat, lng) = await GetCurrentLocationAsyncEx();

                string apiKey = Preferences.Get("ApiKey", "");
                string model = Preferences.Get("Model", "gpt-4o");
                string basePrompt = Preferences.Get("Prompt", "あなたは私の旅の同行者です。");

                string fullPrompt = $"{basePrompt}\n" +
                    $"Current time: {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                    $"Current location: {gpsDesc}\n";
                string response = await CallChatGPTAsync(apiKey, model, fullPrompt);

                if (!string.IsNullOrEmpty(response))
                {
                    Speak(response);
                    SaveToDatabase(gpsDesc, lat, lng, model, basePrompt, response);
                }

            }
            catch (Exception ex)
            {
                ALog.Error("GPTravel", $"定期処理エラー: {ex.Message}");
            }
        }


        private const string PrefKey_LastLat = "LastLat";
        private const string PrefKey_LastLng = "LastLng";

        // --- 修正後 ---
        private async Task<(string desc, double lat, double lng)> GetCurrentLocationAsyncEx()
        {
            try
            {
                if (global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                        this, global::Android.Manifest.Permission.AccessFineLocation) !=
                    global::Android.Content.PM.Permission.Granted)
                {
                    ALog.Warn("GPTravel", "Location permission denied");
                    return ("Location permission denied", 0, 0);
                }

                if (!await EnsureLocationSettingsAsync())
                {
                    ALog.Warn("GPTravel", "Location service is off");
                    return ("Location service is off", 0, 0);
                }

                var request = new LocationRequest.Builder(Priority.PriorityHighAccuracy, 1500)
                    .SetWaitForAccurateLocation(true)
                    .SetMaxUpdates(1)
                    .Build();

                var tcs = new TaskCompletionSource<ALocation?>();
                var callback = new FusedLocationCallback(loc =>
                {
                    if (loc != null) tcs.TrySetResult(loc);
                });

                _fusedLocation?.RequestLocationUpdates(request, callback, Looper.MainLooper);

                var timeout = Task.Delay(10000).ContinueWith(_ => (ALocation?)null);
                var completed = await Task.WhenAny(tcs.Task, timeout);
                var location = await completed;
                _fusedLocation?.RemoveLocationUpdates(callback);

                if (location == null)
                {
                    var last = await _fusedLocation!.LastLocation.AsAsync<ALocation>();
                    if (last != null) location = last;
                }

                if (location == null)
                {
                    double lat = Preferences.Get(PrefKey_LastLat, 0.0);
                    double lng = Preferences.Get(PrefKey_LastLng, 0.0);

                    if (lat != 0 || lng != 0)
                    {
                        ALog.Warn("GPTravel", "新規測位不可のため直前成功値を使用");
                        string desc = string.Format(
                            CultureInfo.InvariantCulture,
                            "Latitude {0:F5}, Longitude {1:F5}",
                            lat, lng
                        );
                        return (desc, lat, lng);
                    }

                    return ("Location permission denied", 0, 0);
                }

                // 現在の測位値を保存
                Preferences.Set(PrefKey_LastLat, location.Latitude);
                Preferences.Set(PrefKey_LastLng, location.Longitude);

                // ロケールに依存しないフォーマットで返す
                string currentDesc = string.Format(
                    CultureInfo.InvariantCulture,
                    "Latitude {0:F5}, Longitude {1:F5}",
                    location.Latitude, location.Longitude
                );

                return (currentDesc, location.Latitude, location.Longitude);
            }
            catch (Exception ex)
            {
                ALog.Warn("GPTravel", $"Location permission denied: {ex.Message}");
                return ("Location error", 0, 0);
            }
        }


        private async Task<bool> EnsureLocationSettingsAsync()
        {
            try
            {
                var settingsClient = LocationServices.GetSettingsClient(this);
                var req = new LocationSettingsRequest.Builder()
                    .AddLocationRequest(new LocationRequest.Builder(Priority.PriorityHighAccuracy, 1500).Build())
                    .Build();

                var result = await settingsClient.CheckLocationSettings(req).AsAsync<LocationSettingsResponse>();
                return result.LocationSettingsStates?.IsLocationUsable == true;
            }
            catch (Exception)
            {
                // 位置設定が満たされていない（OFF 等）
                return false;
            }
        }




        private async Task<string> CallChatGPTAsync(string apiKey, string model, string prompt)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                UpdateNotification("Payment required");
                Speak("Payment required");
                return "";
            }

            try
            {
                UpdateNotification("AI is thinking...");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var requestBody = new
                {
                    model = model,
                    messages = new[] { new { role = "user", content = prompt } }
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == (System.Net.HttpStatusCode)429 ||
                        error.Contains("billing") || error.Contains("payment", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateNotification("Payment required");
                        Speak("Payment required");
                    }
                    else
                    {
                        UpdateNotification("Connection error");
                        Speak("API connection error");
                    }
                    return "";
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                string result = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(result))
                {
                    UpdateNotification("AI is speaking...");
                    Speak(result);
                    UpdateNotification("AI is active");
                }

                return result;
            }
            catch (Exception ex)
            {
                ALog.Warn("GPTravel", $"API error: {ex.Message}");
                UpdateNotification("Connection error");
                Speak("API connection error");
                return "";
            }
        }



        private void Speak(string text)
        {
            try
            {
                // --- ① 音声再生（効果音） ---
                var player = global::Android.Media.MediaPlayer.Create(this, Resource.Raw.snd_notice);
                player.Start();

                // 再生終了後にリリース（メモリリーク防止）
                player.Completion += (s, e) =>
                {
                    player.Release();
                    player.Dispose();
                };

                // --- ② 少し待ってからTTS再生 ---
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500); // 効果音が鳴り終わるまでの待機時間（0.5秒）
                    if (_tts is not null && !string.IsNullOrWhiteSpace(text))
                        _tts.Speak(text, global::Android.Speech.Tts.QueueMode.Flush, null, null);
                });
            }
            catch (Exception ex)
            {
                ALog.Warn("GPTravel", $"効果音再生エラー: {ex.Message}");
                if (_tts is not null && !string.IsNullOrWhiteSpace(text))
                    _tts.Speak(text, global::Android.Speech.Tts.QueueMode.Flush, null, null);
            }
        }


        private void SaveToDatabase(string gps, double lat, double lng, string model, string prompt, string response)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                const string insert = @"INSERT INTO travel_log 
            (datetime, gps, latitude, longitude, model, prompt, response)
            VALUES (@dt, @gps, @lat, @lng, @model, @prompt, @resp)";

                using var cmd = new SqliteCommand(insert, connection);
                cmd.Parameters.AddWithValue("@dt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@gps", gps);
                cmd.Parameters.AddWithValue("@lat", lat);
                cmd.Parameters.AddWithValue("@lng", lng);
                cmd.Parameters.AddWithValue("@model", model);
                cmd.Parameters.AddWithValue("@prompt", prompt);
                cmd.Parameters.AddWithValue("@resp", response);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                ALog.Error("GPTravel", $"DB保存失敗: {ex.Message}");
            }
        }





        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "GPTravel Channel", NotificationImportance.Low)
                {
                    Description = "GPTravel Foreground Notification"
                };
                var manager = (NotificationManager)GetSystemService(NotificationService)!;
                manager.CreateNotificationChannel(channel);
            }
        }

        public override void OnDestroy()
        {
            _handler?.RemoveCallbacks(_taskAction!);
            _cts?.Cancel();
            _tts?.Stop();
            _tts?.Shutdown();
            StopForeground(true);
            StopSelf();
            base.OnDestroy();
        }

        // ==== コールバッククラス ====
        class FusedLocationCallback : LocationCallback
        {
            private readonly Action<ALocation?> _onLocation;

            public FusedLocationCallback(Action<ALocation?> onLocation)
            {
                _onLocation = onLocation;
            }

            public override void OnLocationResult(LocationResult? result)
            {
                base.OnLocationResult(result);
                var location = result?.LastLocation;
                _onLocation?.Invoke(location);
            }
        }

    }
}