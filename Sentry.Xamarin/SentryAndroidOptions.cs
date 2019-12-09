using Android.Content;

namespace Sentry
{
    public class SentryAndroidOptions : SentryOptions
    {
        public bool AnrEnabled { get; set; }

        // Hide some  properties that are irrelevant here?
        public new bool ReportAssemblies => false;

        public SentryAndroidOptions()
        {
            // TODO: Test this but I doubt this works in Java/Android.
            base.ReportAssemblies = false;
        }
    }
}