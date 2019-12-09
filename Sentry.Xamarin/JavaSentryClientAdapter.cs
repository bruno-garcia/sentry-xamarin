using System;
using System.Threading.Tasks;
using IO.Sentry.Core.Protocol;
using Object = Java.Lang.Object;

namespace Sentry
{
    internal class JavaSentryClientAdapter : Object, IO.Sentry.Core.ISentryClient
    {
        private readonly ISentryClient _dotnetSentryClient;

        public JavaSentryClientAdapter(ISentryClient dotnetSentryClient) => _dotnetSentryClient =
            dotnetSentryClient ?? throw new ArgumentNullException(nameof(dotnetSentryClient));

        // TODO: Needs some MWC disposing here?
        public void Dispose() => (_dotnetSentryClient as IDisposable)?.Dispose();

        public IntPtr Handle { get; }
        public bool IsEnabled => _dotnetSentryClient.IsEnabled;

        public SentryId CaptureEvent(
            IO.Sentry.Core.SentryEvent javaEvent,
            IO.Sentry.Core.Scope javaScope,
            Object hint)
        {
            // TODO: The event/scope conversion magic
            var dotnetEvent = new SentryEvent();
            var dotnetScope = new Scope();

            // TODO: Hint is tricky! One derives from java.lang.Object, the other from System.Object.
            var javaId = _dotnetSentryClient.CaptureEvent(dotnetEvent, dotnetScope);
            return new SentryId(javaId.ToString());
        }

        public void Close() => Dispose();

        public void Flush(long timeoutMills) =>
            // TODO: Task.Run? or?
            Task.Run(() => _dotnetSentryClient.FlushAsync(TimeSpan.FromMilliseconds(timeoutMills)));
    }
}