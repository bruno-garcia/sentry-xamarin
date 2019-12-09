using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Sentry.Extensibility;
using Sentry.Infrastructure;
using Sentry.Internal;
using Sentry.Protocol;
using Object = Java.Lang.Object;

namespace Sentry
{
    /// <summary>
    /// Sentry SDK entrypoint
    /// </summary>
    /// <remarks>
    /// This is a façade to the SDK instance.
    /// It allows safe static access to a client and scope management.
    /// When the SDK is uninitialized, calls to this class result in no-op so no callbacks are invoked.
    /// </remarks>
    public static class SentrySdk
    {
        // TODO: I'd need to take the same approach as in Java (TLS for the hub)
        // Unless AsyncLocal works which again, I doubt, specially in the JNI bridge
        private static IHub _hub = DisabledHub.Instance;

        /// <summary>
        /// Last event id recorded in the current scope
        /// </summary>
        public static SentryId LastEventId
        {
            [DebuggerStepThrough] get => _hub.LastEventId;
        }

        /// <summary>
        /// Initializes the SDK while attempting to locate the DSN
        /// </summary>
        /// <remarks>
        /// If the DSN is not found, the SDK will not change state.
        /// </remarks>
        public static void Init(Context context) => Init(context, DsnLocator.FindDsnStringOrDisable());

        /// <summary>
        /// Initializes the SDK with the specified DSN
        /// </summary>
        /// <remarks>
        /// An empty string is interpreted as a disabled SDK
        /// </remarks>
        /// <seealso href="https://docs.sentry.io/clientdev/overview/#usage-for-end-users"/>
        /// <param name="dsn">The dsn</param>
        public static void Init(Context context, string dsn)
        {
            if (string.IsNullOrWhiteSpace(dsn))
            {
                Init(context, c =>
                {
                    c.Dsn = new Dsn(dsn);
                });
            }
        }
// Doesnt' make sense in Android (needs Context) and I'd like to hide the Dsn class
//        /// <summary>
//        /// Initializes the SDK with the specified DSN
//        /// </summary>
//        /// <param name="dsn">The dsn</param>
//        public static void Init(Dsn dsn) => Init(c => c.Dsn = dsn);

        /// <summary>
        /// Initializes the SDK with an optional configuration options callback.
        /// </summary>
        /// <param name="configureOptions">The configure options.</param>
        public static void Init(Context context, Action<SentryAndroidOptions> configureOptions)
        {
            var options = new SentryAndroidOptions();
            configureOptions?.Invoke(options);

            Init(context, options);
        }

        /// <summary>
        /// Initializes the SDK with the specified options instance
        /// </summary>
        /// <param name="options">The options instance</param>
        /// <remarks>
        /// Used by integrations which have their own delegates
        /// </remarks>
        /// <returns>A disposable to close the SDK.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Init(Context context, SentryAndroidOptions options)
        {
            if (options.Dsn == null)
            {
                if (!Dsn.TryParse(DsnLocator.FindDsnStringOrDisable(), out var dsn))
                {
                    options.DiagnosticLogger?.LogWarning(
                        "Init was called but no DSN was provided nor located. Sentry SDK will be disabled.");
                }

                options.Dsn = dsn;
            }

            // TODO: Can't hide members but can hide setters:
            // options.ReportAssemblies = true;
            // TODO: How am I hooking my Hub adapter if I"m integrating at this level?
            // Luckily Sentry.java is a thing layer, as is this one and it would be simple to replicate in C#

            // AndroidOptionsInitializer would need to be exposed. EventProcessors and Integrations would need to communicate
            // TODO: Interlocked.CompareExchange, [ThreadLocal] and copy on read
            var javaOptions = options.ToJavaAndroidOptions();
            // This wouldnt need to be done here if AndroidOptionsInitializer was made public
            // All integrations were made internal in the last API review. Could stay like that if we exposed AndroidOptionsInitializer
            // javaOptions.AddIntegration(new IO.Sentry.Android.Core.AnrIntegration());
            // javaOptions.AddIntegration(new  IO.Sentry.Android.Core.DefaultAndroidEventProcessor());
            // Hack just to take the default Android options stuff into the object
            var callback = new JavaOptionsCallback() {DotnetOptions = options};
            IO.Sentry.Android.Core.SentryAndroid.Init(context, callback);
            IO.Sentry.Core.Sentry.Close(); // TODO: Leave it open to allow Java code to use Sentry.capture?

            // TODO: This would go away and the original instance passed to the client's callback would contain all defaults.
            Apply(callback.AndroidOptions, javaOptions);

            _hub = new AndroidHub(new IO.Sentry.Core.Hub(javaOptions));
        }

        private static void Apply(
            // The default stuff (like what SentryAndroid/AndroidOptionsInitializer set to the options
            IO.Sentry.Android.Core.SentryAndroidOptions source,
            // What the user's callback created. Ideally he'd received the source instance and mutate but can't until java SDK is changed.
            IO.Sentry.Android.Core.SentryAndroidOptions target)
        {
            // TODO: Applies data from the source to target
            foreach (var integration in source.Integrations)
            {
                target.AddIntegration(integration);
            }
        }

        private class JavaOptionsCallback : Object, IO.Sentry.Core.Sentry.IOptionsConfiguration
        {
            // Huge hack to test things before adapting the Java SDK to expose what we need here
            public IO.Sentry.Android.Core.SentryAndroidOptions AndroidOptions { get; private set; }

            public SentryAndroidOptions DotnetOptions { private get; set; }

            // Need parameterless ctor
//            public JavaOptionsCallback(SentryAndroidOptions o) => _o = o;

            public void Dispose()
            {
                // TODO: needs disposal?
            }

            public IntPtr Handle { get; }

            public void Configure(Object p0)
            {
                // This would make sense if we were to use Java's init
                var o = (IO.Sentry.Android.Core.SentryAndroidOptions) p0;
                // Steal the reference
                AndroidOptions = o;
                DotnetOptions.Apply(o);
            }
        }

        private static IO.Sentry.Android.Core.SentryAndroidOptions ToJavaAndroidOptions(
            this SentryAndroidOptions options)
        {
            var javaOptions = new IO.Sentry.Android.Core.SentryAndroidOptions();
            options.Apply(javaOptions);
            return javaOptions;
        }

        private static void Apply(this SentryAndroidOptions options,
            IO.Sentry.Android.Core.SentryAndroidOptions javaOptions)
        {
            javaOptions.Dsn = options.Dsn?.ToString();
            // TODO: are we overriding something the user defined at the android-manifest metadata?
            // I guess it makes sense anyway since this is the explicit (hard-coded) option
            javaOptions.Debug = options.Debug;
            javaOptions.Release = options.Release;
            javaOptions.Environment = options.Environment;
            var callback = new BeforeSendCallback {BeforeSend = options.BeforeSend};
            javaOptions.BeforeSend = callback;

            javaOptions.AnrEnabled = options.AnrEnabled;
        }

        private class BeforeSendCallback : Object,  IO.Sentry.Core.SentryOptions.IBeforeSendCallback
        {
            private
//                readonly
                Func<SentryEvent, SentryEvent> _callback;

            public Func<SentryEvent, SentryEvent> BeforeSend { set => _callback = value; }

            // TODO: Can't have a ctor with parameter?
//            public BeforeSendCallback(Func<SentryEvent, SentryEvent> callback) =>
//                _callback = callback ?? throw new ArgumentNullException(nameof(callback));

            public void Dispose()
            {
            }

            public IntPtr Handle { get; }

            // TODO: big problem here are the unknown fields. Since we're newing up things here, they'd be lost on both directions
            public IO.Sentry.Core.SentryEvent Execute(IO.Sentry.Core.SentryEvent originalJavaEvent, Object hint)
            {
                // TODO: .NET needs to add Hint
                var dotnetEvent = new SentryEvent();
                // TODO: Mapping would need to be done somewhere for the whole event
                dotnetEvent.Logger = originalJavaEvent.Logger;
                // TODO: Sentry.Protocol would need to InternalsVisibleTo(Sentry.Xamarin)
                // dotnetEvent.EventId
                dotnetEvent.Platform = originalJavaEvent.Platform;
                var result = _callback(dotnetEvent);
                if (result is null)
                {
                    return null;
                }

                var finalJavaEvent = new IO.Sentry.Core.SentryEvent();
                // TODO: Protocol would need to match 100%
                // finalJavaEvent.Dist = result.Dist
                return finalJavaEvent;
            }
        }

        /// <summary>
        /// Flushes events queued up.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static Task FlushAsync(TimeSpan timeout) => _hub.FlushAsync(timeout);

        /// <summary>
        /// Close the SDK
        /// </summary>
        /// <remarks>
        /// Flushes the events and disables the SDK.
        /// This method is mostly used for testing the library since
        /// Init returns a IDisposable that can be used to shutdown the SDK.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Close()
        {
            var oldHub = Interlocked.Exchange(ref _hub, DisabledHub.Instance);
            (oldHub as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Whether the SDK is enabled or not
        /// </summary>
        public static bool IsEnabled
        {
            [DebuggerStepThrough] get => _hub.IsEnabled;
        }

        /// <summary>
        /// Creates a new scope that will terminate when disposed
        /// </summary>
        /// <remarks>
        /// Pushes a new scope while inheriting the current scope's data.
        /// </remarks>
        /// <typeparam name="TState"></typeparam>
        /// <param name="state">A state object to be added to the scope</param>
        /// <returns>A disposable that when disposed, ends the created scope.</returns>
        [DebuggerStepThrough]
        public static IDisposable PushScope<TState>(TState state) => _hub.PushScope(state);

        /// <summary>
        /// Creates a new scope that will terminate when disposed
        /// </summary>
        /// <returns>A disposable that when disposed, ends the created scope.</returns>
        [DebuggerStepThrough]
        public static IDisposable PushScope() => _hub.PushScope();

        /// <summary>
        /// Binds the client to the current scope.
        /// </summary>
        /// <param name="client">The client.</param>
        [DebuggerStepThrough]
        public static void BindClient(ISentryClient client) => _hub.BindClient(client);

        /// <summary>
        /// Adds a breadcrumb to the current Scope
        /// </summary>
        /// <param name="message">
        /// If a message is provided it’s rendered as text and the whitespace is preserved.
        /// Very long text might be abbreviated in the UI.</param>
        /// <param name="type">
        /// The type of breadcrumb.
        /// The default type is default which indicates no specific handling.
        /// Other types are currently http for HTTP requests and navigation for navigation events.
        /// <seealso href="https://docs.sentry.io/clientdev/interfaces/breadcrumbs/#breadcrumb-types"/>
        /// </param>
        /// <param name="category">
        /// Categories are dotted strings that indicate what the crumb is or where it comes from.
        /// Typically it’s a module name or a descriptive string.
        /// For instance ui.click could be used to indicate that a click happened in the UI or flask could be used to indicate that the event originated in the Flask framework.
        /// </param>
        /// <param name="data">
        /// Data associated with this breadcrumb.
        /// Contains a sub-object whose contents depend on the breadcrumb type.
        /// Additional parameters that are unsupported by the type are rendered as a key/value table.
        /// </param>
        /// <param name="level">Breadcrumb level.</param>
        /// <seealso href="https://docs.sentry.io/clientdev/interfaces/breadcrumbs/"/>
        [DebuggerStepThrough]
        public static void AddBreadcrumb(
            string message,
            string category = null,
            string type = null,
            IDictionary<string, string> data = null,
            BreadcrumbLevel level = default)
            => _hub.AddBreadcrumb(message, category, type, data, level);

        /// <summary>
        /// Adds a breadcrumb to the current scope
        /// </summary>
        /// <remarks>
        /// This overload is intended to be used by integrations only.
        /// The objective is to allow better testability by allowing control of the timestamp set to the breadcrumb.
        /// </remarks>
        /// <param name="clock">An optional <see cref="ISystemClock"/></param>
        /// <param name="message">The message.</param>
        /// <param name="type">The type.</param>
        /// <param name="category">The category.</param>
        /// <param name="data">The data.</param>
        /// <param name="level">The level.</param>
        [DebuggerStepThrough]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void AddBreadcrumb(
            ISystemClock clock,
            string message,
            string category = null,
            string type = null,
            IDictionary<string, string> data = null,
            BreadcrumbLevel level = default)
            => _hub.AddBreadcrumb(clock, message, category, type, data, level);

        /// <summary>
        /// Runs the callback with a new scope which gets dropped at the end
        /// </summary>
        /// <remarks>
        /// Pushes a new scope, runs the callback, pops the scope.
        /// </remarks>
        /// <see href="https://docs.sentry.io/learn/scopes/?platform=csharp#local-scopes"/>
        /// <param name="scopeCallback">The callback to run with the one time scope.</param>
        [DebuggerStepThrough]
        public static void WithScope(Action<Scope> scopeCallback)
            => _hub.WithScope(scopeCallback);

        /// <summary>
        /// Configures the scope through the callback.
        /// </summary>
        /// <param name="configureScope">The configure scope callback.</param>
        [DebuggerStepThrough]
        public static void ConfigureScope(Action<Scope> configureScope)
            => _hub.ConfigureScope(configureScope);

        /// <summary>
        /// Configures the scope asynchronously
        /// </summary>
        /// <param name="configureScope">The configure scope callback.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static Task ConfigureScopeAsync(Func<Scope, Task> configureScope)
            => _hub.ConfigureScopeAsync(configureScope);

        /// <summary>
        /// Captures the event.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static SentryId CaptureEvent(SentryEvent evt)
            => _hub.CaptureEvent(evt);

        /// <summary>
        /// Captures the event using the specified scope.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static SentryId CaptureEvent(SentryEvent evt, Scope scope)
            => _hub.CaptureEvent(evt, scope);

        /// <summary>
        /// Captures the exception.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static SentryId CaptureException(Exception exception)
            => _hub.CaptureException(exception);

        /// <summary>
        /// Captures the message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="level">The message level.</param>
        /// <returns>The Id of the event</returns>
        [DebuggerStepThrough]
        public static SentryId CaptureMessage(string message, SentryLevel level = SentryLevel.Info)
            => _hub.CaptureMessage(message, level);
    }
}