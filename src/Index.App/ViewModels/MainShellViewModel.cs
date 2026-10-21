using System.ComponentModel;
using Index.Domain;
using Index.Domain.Models;
using Index.Modules.JobManager.Views;
using Index.UI.Commands;
using Index.UI.ViewModels;
using Prism.Ioc;
using Serilog;

namespace Index.App.ViewModels
{

  public class MainShellViewModel : WindowViewModel
  {

    #region Properties

    protected IEditorEnvironment EditorEnvironment { get; }

    #endregion

    #region Constructor

    public MainShellViewModel( IContainerProvider container, IEditorEnvironment environment )
      : base( container )
    {
      Title = BuildTitle( environment );
      EditorEnvironment = environment;
    }

    #endregion

    #region Overrides

    protected override void OnWindowAppeared()
    {
      InitializeBottomTabPanel();
      Log.Logger.Information( "Welcome to E-Day Extractor! Built on Index by Haus and CUE4Parse." );
      Log.Logger.Information( "Using Profile '{ProfileName:l}' v{VersionName:l} by {Author:l}",
        EditorEnvironment.GameName,
        EditorEnvironment.GameProfile.Version,
        EditorEnvironment.GameProfile.Author );

      //var assetManager = Container.Resolve<IAssetManager>();
      //assetManager.TryGetAssetReference( typeof( IMeshAsset ), "a10/a10.lg", out var modelRef );
      //EditorCommands.NavigateToAssetCommand.Execute( modelRef );
      //assetManager.TryGetAssetReference( typeof( IMeshAsset ), "a10/cryotube_a10__h", out modelRef );
      //EditorCommands.NavigateToAssetCommand.Execute( modelRef );
      //assetManager.TryGetAssetReference( typeof( IMeshAsset ), "a10/cyborg", out modelRef );
      //EditorCommands.NavigateToAssetCommand.Execute( modelRef );
    }

    protected override void OnWindowClosing( CancelEventArgs e )
    {
      EditorCommands.CloseAllTabsCommand.Execute( null );
    }

    #endregion

    #region Private Methods

    private static string BuildTitle( IEditorEnvironment environment )
    {
      var title = $"E-Day Extractor | {environment.GameName}";

      var sourceFiles = environment.SourceFiles;
      if ( sourceFiles is { Count: 1 } )
        title += $" | {System.IO.Path.GetFileName( sourceFiles[ 0 ] )}";
      else if ( sourceFiles is { Count: > 1 } )
        title += $" | {sourceFiles.Count} files";

      return title;
    }

    private void InitializeBottomTabPanel()
    {
      RegionManager.RequestNavigate( RegionKeys.BottomPanelRegion, "LogView" );

      var region = RegionManager.Regions[ RegionKeys.BottomPanelRegion ];
      region.Add( Container.Resolve<JobsView>() );
    }

    #endregion

  }

}
