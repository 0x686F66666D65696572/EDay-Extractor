using System.IO;
using System.Linq;
using System.Windows;
using Index.App.ViewModels;
using Index.UI.Windows;

namespace Index.App.Views
{

  public partial class LauncherView : IxWindow
  {

    public LauncherView( LauncherViewModel viewModel )
    {
      DataContext = viewModel;
      InitializeComponent();
    }

    private void OnDragOver( object sender, DragEventArgs e )
    {
      e.Effects = GetDroppedFiles( e ).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
      e.Handled = true;
    }

    private void OnDrop( object sender, DragEventArgs e )
    {
      var folders = GetDroppedFiles( e );
      if ( folders.Length == 0 )
        return;

      ( ( LauncherViewModel ) DataContext ).OpenGameFolder( folders[ 0 ] );
      e.Handled = true;
    }

    private static string[] GetDroppedFiles( DragEventArgs e )
    {
      if ( !e.Data.GetDataPresent( DataFormats.FileDrop ) )
        return System.Array.Empty<string>();

      var paths = e.Data.GetData( DataFormats.FileDrop ) as string[];
      return paths?.Where( Directory.Exists ).ToArray() ?? System.Array.Empty<string>();
    }

  }

}
