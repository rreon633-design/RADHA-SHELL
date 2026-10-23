using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Radha.App.ViewModels;
using Windows.System;

namespace Radha.App.Views;

public sealed partial class LauncherView : UserControl
{
    public LauncherView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is LauncherViewModel vm && sender is TextBox box)
            vm.Query = box.Text;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (DataContext is LauncherViewModel vm && e.ClickedItem is LauncherItemVm entry)
            vm.ActivateCommand.Execute(entry);
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (DataContext is not LauncherViewModel vm) return;

        switch (e.Key)
        {
            case VirtualKey.Escape:
                vm.Query = "";
                break;
            case VirtualKey.Enter when vm.Results.Count > 0:
                vm.ActivateCommand.Execute(vm.Results[0]);
                break;
        }
    }
}
