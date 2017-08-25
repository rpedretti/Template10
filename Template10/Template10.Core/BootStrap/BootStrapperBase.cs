﻿using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Template10.Services.Container;
using Template10.Services.Logging;
using Template10.Navigation;
using Template10.Core;
using Template10.Strategies;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using static Template10.Core.StartArgsEx;
using Template10.Services.Messenger;
using Template10.Services.Serialization;
using Template10.Services.Gesture;
using Template10.Messages;
using Template10.Services.Resources;
using Template10.Services.Network;

namespace Template10.BootStrap
{
    public abstract partial class BootStrapperBase
    {
        public BootStrapperBase()
        {
            // start

            CreateDependecyContainer();
            try { var c = Central.ContainerService; }
            catch { throw new Exception($"IContainerService is required but is not defined in DI."); }

            RegisterDefaultDependencies(Central.ContainerService);
            RegisterCustomDependencies(Central.ContainerService);
            try { var m = Central.MessengerService; }
            catch { throw new Exception($"IMessengerService is required but is not registered in DI."); }

#if DEBUG
            TestDependecyInjection(Central.ContainerService);
#endif

            LogThis();

            ForwardMethods();

            SetupMessages();

            SetupEvents();
        }

        private IBootStrapperStrategy BootStrapperStrategy
            => Central.ContainerService.Resolve<IBootStrapperStrategy>();

        private void ForwardMethods()
        {
            LogThis();
            BootStrapperStrategy.OnStartAsyncDelegate = OnStartAsync;
            BootStrapperStrategy.OnInitAsyncDelegate = OnInitializeAsync;
            BootStrapperStrategy.CreateRootElementDelegate = CreateRootElement;
        }

        private void SetupMessages()
        {
            LogThis();
            Central.MessengerService.Subscribe<WindowCreatedMessage>(this, HandleAfterFirstWindowCreated);
            Central.MessengerService.Subscribe<AppVisibilityChangedMessage>(this, (e) => Central.AppVisibility = e.Visibility);
            Central.MessengerService.Subscribe<ExtendedSessionRevokedMessage>(this, (e) =>
            {
                var isUnspecified = e.ExtendedExecutionReason == Windows.ApplicationModel.ExtendedExecution.ExtendedExecutionReason.Unspecified;
                var isForeground = Central.AppVisibility == AppVisibilities.Foreground;
                if (isUnspecified && isForeground) // && Revoked
                {
                    OnClose(new ClosedEventArgs(e.TryToExtendAsync));
                }
            });
        }

        private void HandleAfterFirstWindowCreated(WindowCreatedMessage message)
        {
            LogThis();
            
            // unsubscribe so this is only called a single time
            Central.MessengerService.Unsubscribe<WindowCreatedMessage>(this, HandleAfterFirstWindowCreated);

            // these are the things delayed until after the first window is created
            Central.ContainerService.Resolve<IBackButtonService>().Setup();
            Central.ContainerService.Resolve<ITitleBarStrategy>().Update();
        }

        private void SetupEvents()
        {
            LogThis();
            base.EnteredBackground += (s, e) =>
            {
                Central.AppVisibility = AppVisibilities.Background;
                BootStrapperStrategy.HandleEnteredBackground(s, e);
            };
            base.LeavingBackground += (s, e) =>
            {
                Central.AppVisibility = AppVisibilities.Foreground;
                BootStrapperStrategy.HandleLeavingBackground(s, e); ;
            };
            base.Resuming += BootStrapperStrategy.HandleResuming;
            base.Suspending += BootStrapperStrategy.HandleSuspending;
            base.UnhandledException += BootStrapperStrategy.HandleUnhandledException;
        }
    }

    public abstract partial class BootStrapperBase : IBootStrapperXaml
    {
        // intended for XAML, properties

        public DataTemplate BusyIndicatorTemplate { get; set; }
        public DataTemplate SplashScreenTemplate { get; set; }
        public DataTemplate NetworkRequiredTemplate { get; set; }
    }

    public abstract partial class BootStrapperBase : IBootStrapperDependecyInjection
    {
        public abstract IContainerService CreateDependecyContainer();
        public abstract void RegisterCustomDependencies(IContainerContributor container);

        void RegisterDefaultDependencies(IContainerContributor container)
        {
            // services
            container.Register<ISessionState, SessionState>();
            container.Register<ILoggingService, LoggingService>();
            container.Register<ISerializationService, JsonSerializationService>();
            container.Register<IBackButtonService, BackButtonService>();
            container.Register<IKeyboardService, KeyboardService>();
            container.Register<IGestureService, GestureService>();
            container.Register<IResourceService, ResourceService>();
            container.Register<INetworkAvailableService, NetworkAvailableService>();

            // strategies
            container.RegisterInstance<IBootStrapperXaml>(this);
            container.Register<IBootStrapperStrategy, DefaultBootStrapperStrategy>();
            container.Register<ILifecycleStrategy, DefaultLifecycleStrategy>();
            container.Register<INavStateStrategy, DefaultNavStateStrategy>();
            container.Register<ITitleBarStrategy, DefaultTitleBarStrategy>();
            container.Register<IExtendedSessionStrategy, DefaultExtendedSessionStrategy>();
            container.Register<IViewModelActionStrategy, DefaultViewModelActionStrategy>();
            container.Register<IViewModelResolutionStrategy, DefaultViewModelResolutionStrategy>();
            container.Register<INetworkAvailableStrategy, DefaultNetworkAvailableStrategy>();
            container.Register<ISplashStrategy, DefaultSplashStrategy>();
        }

        private static void TestDependecyInjection(IContainerConsumer container)
        {
            container.Resolve<ISessionState>();
            container.Resolve<ILoggingService>();
            container.Resolve<ISerializationService>();
            container.Resolve<IBackButtonService>();
            container.Resolve<IKeyboardService>();
            container.Resolve<IGestureService>();
            container.Resolve<IResourceService>();
            container.Resolve<IBootStrapperStrategy>();
            container.Resolve<ILifecycleStrategy>();
            container.Resolve<INavStateStrategy>();
            container.Resolve<ITitleBarStrategy>();
            container.Resolve<IExtendedSessionStrategy>();
            container.Resolve<IViewModelActionStrategy>();
            container.Resolve<IViewModelResolutionStrategy>();
            container.Resolve<INetworkAvailableStrategy>();
            container.Resolve<ISplashStrategy>();
        }
    }

    public abstract partial class BootStrapperBase : IBootStrapperStartup
    {
        // methods intended for override

        public virtual Task OnInitializeAsync() => Task.CompletedTask;
        public virtual UIElement CreateRootElement(IStartArgsEx e) => null;
        public abstract Task OnStartAsync(IStartArgsEx e, INavigationService navService, ISessionState sessionState);
        public virtual void OnClose(ClosedEventArgs e) { /* empty */ }
    }

    public abstract partial class BootStrapperBase : Application
    {
        // clean up the Application overrides

        protected override sealed void OnActivated(IActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnCachedFileUpdaterActivated(CachedFileUpdaterActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnFileActivated(FileActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnFileOpenPickerActivated(FileOpenPickerActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnFileSavePickerActivated(FileSavePickerActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnSearchActivated(SearchActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnShareTargetActivated(ShareTargetActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Activate));
        protected override sealed void OnLaunched(LaunchActivatedEventArgs e) => LogThis(() => BootStrapperStrategy.StartOrchestrationAsync(e, StartKinds.Launch));
        protected override sealed void OnBackgroundActivated(BackgroundActivatedEventArgs e) => LogThis(() => Central.MessengerService.Send(new Messages.BackgroundActivatedMessage { EventArgs = e }));
        protected override sealed void OnWindowCreated(WindowCreatedEventArgs e) => LogThis(() => BootStrapperStrategy.OnWindowCreated(e));

        // override built-in Application events

#pragma warning disable CS0067
        private new event EventHandler<object> Resuming;
        private new event SuspendingEventHandler Suspending;
        private new event UnhandledExceptionEventHandler UnhandledException;
        private new event EnteredBackgroundEventHandler EnteredBackground;
        private new event LeavingBackgroundEventHandler LeavingBackground;
#pragma warning restore CS0067

        // clean up the object API

        public sealed override bool Equals(object obj) => base.Equals(obj);
        public sealed override int GetHashCode() => base.GetHashCode();
        public sealed override string ToString() => base.ToString();
    }

    public abstract partial class BootStrapperBase : ILoggable
    {
        ILoggingService ILoggable.LoggingService => Central.LoggingService;
        void LogThis(Action action, string text = null, Severities severity = Severities.Template10, [CallerMemberName]string caller = null)
        {
            action();
            (this as ILoggable).LogThis(text, severity, caller: $"{caller}");
        }
        void LogThis(string text = null, Severities severity = Severities.Template10, [CallerMemberName]string caller = null)
            => (this as ILoggable).LogThis(text, severity, caller: $"{caller}");
        void ILoggable.LogThis(string text, Severities severity, string caller)
            => (this as ILoggable).LoggingService.WriteLine(text, severity, caller: $"{caller}");
    }
}