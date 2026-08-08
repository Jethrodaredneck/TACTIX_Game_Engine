using AppKit;
using Foundation;
using TACTIX.Engine.Core.Application;

namespace TACTIX;

internal static class Program
{
    private static void Main(string[] args)
    {
        NSApplication.Init();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;

        var delegateObj = new TactixAppDelegate();
        app.Delegate = delegateObj;

        app.Run();
    }
}

internal sealed class TactixAppDelegate : NSApplicationDelegate
{
    private TactixApplication? _app;

    public override void DidFinishLaunching(NSNotification notification)
    {
        _app = new TactixApplication();
        _app.Start();

        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
    }

    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
    {
        _app?.Stop();
        return NSApplicationTerminateReply.Now;
    }
}
