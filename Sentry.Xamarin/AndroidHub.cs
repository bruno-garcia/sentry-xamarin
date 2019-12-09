using System;
using System.Threading.Tasks;
using IO.Sentry.Core;
using Sentry.Protocol;
using SentryLevel = Sentry.Protocol.SentryLevel;

namespace Sentry
{
    public class AndroidHub : IHub
    {
        private readonly IO.Sentry.Core.IHub _androidHub;

        public AndroidHub(IO.Sentry.Core.IHub androidHub) =>
            _androidHub = androidHub ?? throw new ArgumentNullException(nameof(androidHub));

        public bool IsEnabled => _androidHub.IsEnabled;

        public SentryId CaptureEvent(SentryEvent evt, Scope scope = null)
        {
            // TODO: Call the SentryEvent conversion method
            var javaEvent = new IO.Sentry.Core.SentryEvent();
//            _androidHub.CaptureEvent(javaEvent, )
            throw new NotImplementedException();
        }

        public Task FlushAsync(TimeSpan timeout)
        {
            // TODO: no-op = Close to .NET API but misleading
            // TODO: Remove method, less close to .NET
            // TODO: Add Flush Java. How to map to Task?
            return Task.CompletedTask;
        }

        public void ConfigureScope(Action<Scope> configureScope)
        {
            _androidHub.ConfigureScope(new ScopeCallback(configureScope));
        }

        private class ScopeCallback : Java.Lang.Object, IScopeCallback
        {
            private readonly Action<Scope> _callback;

            public ScopeCallback(Action<Scope> callback) =>
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public IntPtr Handle { get; }

            public void Run(IO.Sentry.Core.Scope javaScope)
            {
                // TODO: Scope conversion extension method
                // javaScope.ToDotnet();
                var dotnetScope = new Scope();
                _callback(dotnetScope);
                // TODO: Make an extension method to apply scope between Java and dotnet objects
                // dotnetScope.ApplyToJava(javaScope);

                javaScope.Level = GetLevel(dotnetScope.Level);
                javaScope.Transaction = dotnetScope.Transaction;

                static IO.Sentry.Core.SentryLevel GetLevel(SentryLevel? nullableLevel)
                    => nullableLevel is { } level
                        ? level switch
                        {
                            SentryLevel.Debug => IO.Sentry.Core.SentryLevel.Debug,
                            SentryLevel.Info => IO.Sentry.Core.SentryLevel.Info,
                            SentryLevel.Warning => IO.Sentry.Core.SentryLevel.Warning,
                            SentryLevel.Error => IO.Sentry.Core.SentryLevel.Error,
                            SentryLevel.Fatal => IO.Sentry.Core.SentryLevel.Fatal,
                            _ => null
                        }
                        : null;
            }
        }

        public Task ConfigureScopeAsync(Func<Scope, Task> configureScope)
        {
            // TODO: Use Task.Run? GUI only cares not to block the UI thread and not scaling so probably the best approach
            // TODO: Remove from the API? Create some wrapper with TaskCompletionSource?
            return Task.Run(() => ConfigureScope(s => configureScope(s).GetAwaiter().GetResult()));
        }

        public void BindClient(ISentryClient client)
        {
            var wrapper = new JavaSentryClientAdapter(client);
            _androidHub.BindClient(wrapper);
        }

        public IDisposable PushScope() => new PopScopeDisposable(_androidHub);

        private class PopScopeDisposable : IDisposable
        {
            private readonly IO.Sentry.Core.IHub _hub;
            public PopScopeDisposable(IO.Sentry.Core.IHub hub) => _hub = hub;

            public void Dispose() => _hub.PopScope();
        }

        public IDisposable PushScope<TState>(TState state)
        {
            // TODO: state here was added in .NET to support MEL and is not part of the unified API
            // Could just add an extra within the scope which is pretty much what happens in .NET
            _androidHub.PushScope();
            // TODO: Extra in .NET has object as value and serializes whatever you give it.
            // TODO: Just calling ToString here which is not ideal
            _androidHub.SetExtra("some-key-based-on-state", state.ToString());
            return new PopScopeDisposable(_androidHub);
        }

        public void WithScope(Action<Scope> scopeCallback) => _androidHub.WithScope(new ScopeCallback(scopeCallback));

        // TODO: Might blow up because parsing without dashes
        // TODO: Use ParseExact if it does
        public SentryId LastEventId => new SentryId(Guid.Parse(_androidHub.LastEventId.ToString()));
    }
}