using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Index.App.Models;
using Index.Domain.Database.Entities;
using Index.Domain.Database.Repositories;
using Index.Domain.GameProfiles;
using Index.Domain.Models;
using Index.UI.Services;
using Index.UI.ViewModels;
using Prism.Commands;

namespace Index.App.ViewModels
{

  public class LauncherViewModel : WindowViewModel
  {

    #region Data Members

    private readonly IEditorEnvironment _editorEnvironment;
    private readonly IFileDialogService _fileDialogService;
    private readonly IGameProfileManager _profileManager;
    private readonly IGamePathRepository _gamePathRepository;

    private readonly ObservableCollection<LauncherItem> _items;

    #endregion

    #region Properties

    public ObservableCollection<LauncherItem> Items => _items;
    public LauncherItem SelectedItem { get; set; }

    public DelegateCommand SelectGameFolderCommand { get; }
    public DelegateCommand RemoveSelectedPathCommand { get; }
    public DelegateCommand LaunchCommand { get; }

    #endregion

    #region Constructor

    public LauncherViewModel(
      IFileDialogService fileDialogService,
      IGameProfileManager profileManager,
      IGamePathRepository gamePathRepository,
      IEditorEnvironment editorEnvironment )
      : base( null )
    {
      ASSERT_NOT_NULL( fileDialogService );
      ASSERT_NOT_NULL( profileManager );
      ASSERT_NOT_NULL( gamePathRepository );
      ASSERT_NOT_NULL( editorEnvironment );

      _fileDialogService = fileDialogService;
      _profileManager = profileManager;
      _gamePathRepository = gamePathRepository;
      _editorEnvironment = editorEnvironment;

      _items = new ObservableCollection<LauncherItem>();

      SelectGameFolderCommand = new DelegateCommand( SelectGameFolder );
      RemoveSelectedPathCommand = new DelegateCommand( RemoveSelectedGamePath );
      LaunchCommand = new DelegateCommand( LaunchEditor );

      Title = "E-Day Extractor - Select your Gears of War: E-Day folder";

      ReloadLauncherItems();
    }

    #endregion

    #region Public Methods

    /// <summary>
    ///   Identifies the game under (or above) the folder and opens it. Used by the folder picker,
    ///   drag and drop, and the command line.
    /// </summary>
    internal bool OpenGameFolder( string folderPath )
    {
      if ( string.IsNullOrWhiteSpace( folderPath ) || !Directory.Exists( folderPath ) )
        return false;

      var result = _profileManager.ScanPathForSupportedGames( folderPath ).FirstOrDefault();
      if ( result.GameId is null || !_profileManager.Profiles.TryGetValue( result.GameId, out var profile ) )
      {
        ShowWarning(
          "Couldn't find a Gears of War: E-Day install in that folder.\n\n" +
          "Select the game's root folder, e.g. C:\\XboxGames\\Gears of War- E-Day.\n" +
          "It must contain Content\\FairlightConcept\\Content\\Paks.",
          "Game Not Found" );
        return false;
      }

      RememberGamePath( profile, result.GamePath );

      _editorEnvironment.UseGamePath( profile, result.GamePath );
      CloseAndLaunch();
      return true;
    }

    #endregion

    #region Private Methods

    private void SelectGameFolder()
    {
      var folderPath = _fileDialogService.BrowseForDirectory( "Select your Gears of War: E-Day game folder" );
      if ( string.IsNullOrWhiteSpace( folderPath ) )
        return;

      OpenGameFolder( folderPath );
    }

    private void LaunchEditor()
    {
      if ( SelectedItem is null )
        return;

      var path = SelectedItem.GamePath;
      if ( !Directory.Exists( path ) )
      {
        ShowWarning( $"The folder no longer exists:\n{path}", "Folder Not Found" );
        return;
      }

      _editorEnvironment.UseGamePath( SelectedItem.GameProfile, path );
      CloseAndLaunch();
    }

    private void CloseAndLaunch()
    {
      Window.DialogResult = true;
      Window.Close();
    }

    private void RememberGamePath( IGameProfile profile, string gamePath )
    {
      if ( _gamePathRepository.CheckIfGamePathAlreadyExists( gamePath ) )
        return;

      _gamePathRepository.Add( new GamePath
      {
        GameId = profile.GameId,
        Path = gamePath
      } );
      _gamePathRepository.SaveChanges();
    }

    private static void ShowWarning( string message, string caption )
      => MessageBox.Show( message, caption, MessageBoxButton.OK, MessageBoxImage.Warning );

    private void ReloadLauncherItems()
    {
      var newItems = new List<LauncherItem>();

      var gamePaths = _gamePathRepository.GetAll();
      foreach ( var gamePath in gamePaths )
      {
        if ( !_profileManager.Profiles.TryGetValue( gamePath.GameId, out var gameProfile ) )
          continue;

        newItems.Add( new LauncherItem
        {
          GameId = gameProfile.GameId,
          GameName = gameProfile.GameName,
          GameIcon = LoadGameIcon( gameProfile ),

          GamePath = gamePath.Path,
          GameProfile = gameProfile
        } );
      }

      _items.Clear();
      foreach ( var newItem in newItems )
        _items.Add( newItem );
    }

    private void RemoveSelectedGamePath()
    {
      if ( SelectedItem is null )
        return;

      var gamePath = _gamePathRepository.GetByPath( SelectedItem.GamePath );
      if ( gamePath is null )
        return;

      _gamePathRepository.Delete( gamePath );
      _gamePathRepository.SaveChanges();

      ReloadLauncherItems();
    }

    private ImageSource? LoadGameIcon( IGameProfile gameProfile )
    {
      var gameIconStream = gameProfile.LoadGameIcon();
      if ( gameIconStream is null )
        return null;

      var gameIcon = new BitmapImage();
      gameIcon.BeginInit();
      {
        gameIcon.StreamSource = gameIconStream;
        gameIcon.DecodePixelWidth = 64;
      }
      gameIcon.EndInit();

      return gameIcon;
    }

    #endregion

  }

}
