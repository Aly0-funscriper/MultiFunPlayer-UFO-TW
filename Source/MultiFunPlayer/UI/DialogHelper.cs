using MaterialDesignThemes.Wpf;
using MultiFunPlayer.UI.Controls.ViewModels;
using MultiFunPlayer.UI.Dialogs.ViewModels;
using Stylet;
using StyletIoC;
using System.Windows;

namespace MultiFunPlayer.UI;

internal static class DialogHelper
{
    private static SettingsViewModel Settings { get; set; }
    private static IViewManager ViewManager { get; set; }
    private static ISnackbarMessageQueue SnackbarMessageQueue { get; set; }

    public static void Initialize(IContainer container)
    {
        Settings = container.Get<SettingsViewModel>();
        ViewManager = container.Get<IViewManager>();
        SnackbarMessageQueue = container.Get<ISnackbarMessageQueue>();
    }

    public static async Task ShowErrorAsync(Exception exception, string message, string dialogName)
    {
        var displayType = Settings?.General?.ErrorDisplayType ?? ErrorDisplayType.None;
        if (displayType == ErrorDisplayType.None)
            return;

        var dialogModel = new ErrorMessageDialog(exception, message);
        if (displayType == ErrorDisplayType.Dialog)
        {
            await ShowOnUIThreadAsync(dialogModel, dialogName);
        }
        else if (displayType == ErrorDisplayType.Snackbar)
        {
            await Execute.OnUIThreadAsync(() =>
                SnackbarMessageQueue.Enqueue(message, "Show",
                    async m => await ShowAsync(m, dialogName), dialogModel,
                    true, true, TimeSpan.FromSeconds(5)));
        }
    }

    public static Task ShowOnUIThreadAsync(object model, string dialogName)
        => Execute.OnUIThreadAsync(async () => await ShowAsync(model, dialogName));

    public static async Task<object> ShowAsync(object model, string dialogName)
    {
        var view = ViewManager.CreateAndBindViewForModelIfNecessary(model);

        var session = DialogHost.GetDialogSession(dialogName);
        if (session != null)
        {
            var sessionContext = (session.Content as FrameworkElement)?.DataContext;
            if (sessionContext?.Equals(model) == true)
                return null;

            if (!session.IsEnded)
                session.Close();
        }

        (model as IScreenState)?.Activate();
        var result = await DialogHost.Show(view, dialogName);
        (model as IScreenState)?.Deactivate();
        return result;
    }

    public static void CloseOnUIThread(object model, string dialogName) => Execute.OnUIThreadSync(() =>
    {
        var session = DialogHost.GetDialogSession(dialogName);
        if (session == null)
            return;

        var sessionContext = (session.Content as FrameworkElement)?.DataContext;
        if (sessionContext?.Equals(model) != true)
            return;

        if (!session.IsEnded)
            session.Close();

        (model as IScreenState)?.Deactivate();
    });

    public static void SafeClose(string dialogName) => SafeClose(dialogName, null);
    public static void SafeClose(string dialogName, object parameter)
    {
        try
        {
            if (DialogHost.GetDialogSession(dialogName) is DialogSession { IsEnded: false } session)
                session.Close(parameter);
        } catch { }
    }
}
