using InstanciumLens.Core.Interfaces;
using Windows.Storage.Pickers;


namespace InstanciumLens.App.Services
{
    public class WindowsFolderPickerService : IFolderPickerService
    {
        public async Task<string?> PickFolderAsync()
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            // Get the current MAUI window handle (HWND) for Windows 10/11
            var window = App.Current?.Windows[0].Handler.PlatformView as MauiWinUIWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var result = await folderPicker.PickSingleFolderAsync();
            return result?.Path;
        }
    }
}
