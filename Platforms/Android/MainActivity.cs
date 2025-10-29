using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using GPTravel.Platforms.Android.Services;

namespace GPTravel
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                              ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

#if ANDROID
            // ===== Foreground 停止（起動時に既存の常駐を一旦止める）=====
            try
            {
                var context = Android.App.Application.Context;
                var stopIntent = new Intent(context, typeof(TravelForegroundService));
                context.StopService(stopIntent);
            }
            catch { }

            // ===== 位置情報パーミッション確認・要求 =====
            RequestLocationPermission();
#endif
        }

        // ---- 実行時パーミッション要求 ----
        private void RequestLocationPermission()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                {
                    string[] permissions =
                    {
                        Manifest.Permission.AccessFineLocation,
                        Manifest.Permission.AccessCoarseLocation
                    };

                    bool needsRequest = false;

                    foreach (var perm in permissions)
                    {
                        if (ContextCompat.CheckSelfPermission(this, perm) != Permission.Granted)
                        {
                            needsRequest = true;
                            break;
                        }
                    }

                    if (needsRequest)
                    {
                        ActivityCompat.RequestPermissions(this, permissions, 1001);
                    }
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("GPTravel", $"パーミッション要求中に例外発生: {ex.Message}");
            }
        }
    }
}
