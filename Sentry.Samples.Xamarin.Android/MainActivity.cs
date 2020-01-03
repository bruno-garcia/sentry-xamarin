using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;
using Java.Lang;
using Sentry.Extensibility;
using Sentry.Protocol;
using Exception = System.Exception;

namespace Sentry.Samples.Xamarin.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            global::Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            global::Android.Support.V7.Widget.Toolbar toolbar = FindViewById<global::Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            // TODO: double check but SDK calls .getApplicationContext so we can pass 'this' here instead.
            SentrySdk.Init(this, o =>
            {
                o.Dsn = new Dsn("https://f7f320d5c3a54709be7b28e0f2ca7081@sentry.io/1808954");
                o.Debug = true;
                o.Release = "test-release";
                o.BeforeSend = @event =>
                {
                    @event.SetTag("BeforeSend", "was called");
                    return @event;
                };
                o.AddEventProcessor(new TestProcessor());
            });

            FloatingActionButton fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
            fab.Click += FabOnClick;
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        public class TestProcessor : ISentryEventProcessor
        {
            public SentryEvent Process(SentryEvent @event)
            {

                @event.SetTag("EventProcessor", nameof(TestProcessor));
                return @event;
            }
        }

        private void FabOnClick(object sender, EventArgs eventArgs)
        {
            View view = (View)sender;
            Snackbar.Make(view, "Replace with your own action", Snackbar.LengthLong)
                .SetAction("Action", (View.IOnClickListener)null).Show();
//            throw new Exception("Xamarinrinrinrinrin...");
//            IO.Sentry.Core.Hub


            SentrySdk.ConfigureScope(s => s.SetTag("ConfigureScope right after init", "Xam"));
            SentrySdk.CaptureMessage("first message");

            using (SentrySdk.PushScope())
            {
                SentrySdk.ConfigureScope(s => s.SetTag("event from a scope", "1"));
                SentrySdk.CaptureEvent(new SentryEvent
                {
                    Release = "scope-release"
                });
            }

            SentrySdk.WithScope(s =>
            {
                s.Level = SentryLevel.Fatal;
                SentrySdk.CaptureMessage("Fatal using withScope");
            });

            try
            {
                int div = default;
                _ = 1 / div;
            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
            }

            SentrySdk.FlushAsync(TimeSpan.FromSeconds(5))
                // Block to flush everything
                .GetAwaiter().GetResult();

            throw null; // Crash. Should be picked up by Java on startup
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] global::Android.Content.PM.Permission[] grantResults)
        {
            global::Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}

