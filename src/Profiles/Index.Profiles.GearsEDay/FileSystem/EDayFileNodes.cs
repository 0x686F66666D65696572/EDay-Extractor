using CUE4Parse.FileProvider.Objects;
using Index.Domain.Assets;
using Index.Domain.Assets.Meshes;
using Index.Domain.Assets.Text;
using Index.Domain.Assets.Textures.Dxgi;
using Index.Domain.FileSystem;
using Index.Profiles.GearsEDay.Assets;

namespace Index.Profiles.GearsEDay.FileSystem
{

  public class EDayDirectoryNode : FileSystemNodeBase
  {
    public EDayDirectoryNode( IFileSystemDevice device, string name, IFileSystemNode? parent = null )
      : base( device, name, parent! )
    {
    }
  }

  /// <summary>
  ///   A file inside the game's archives. <see cref="DisplayName"/> is the asset path shown in the explorer,
  ///   e.g. "Game/Characters/Hero/Marcus/GP01/Hero_MarcusBody_GP01".
  /// </summary>
  public abstract class EDayFileNode : FileSystemNodeBase
  {

    #region Properties

    public GameFile GameFile { get; }
    public string AssetPath { get; }
    public string ClassName { get; }

    public override string DisplayName => AssetPath;

    #endregion

    #region Constructor

    protected EDayFileNode( IFileSystemDevice device, GameFile gameFile, string className, IFileSystemNode parent )
      : base( device, gameFile.Path, parent )
    {
      GameFile = gameFile;
      ClassName = className;
      AssetPath = GetAssetPath( gameFile, keepExtension: !gameFile.IsUePackage );
    }

    #endregion

    #region Private Methods

    /// <summary>
    ///   Shortens archive paths the way the Unreal editor shows them: "FairlightConcept/Content/X" becomes "Game/X".
    /// </summary>
    internal static string GetAssetPath( GameFile gameFile, bool keepExtension )
    {
      var path = gameFile.Path;
      if ( !keepExtension )
        path = path[ ..^( gameFile.Extension.Length + 1 ) ];

      const string GAME_CONTENT = "FairlightConcept/Content/";
      const string ENGINE_CONTENT = "Engine/Content/";

      if ( path.StartsWith( GAME_CONTENT, StringComparison.OrdinalIgnoreCase ) )
        return "Game/" + path[ GAME_CONTENT.Length.. ];
      if ( path.StartsWith( ENGINE_CONTENT, StringComparison.OrdinalIgnoreCase ) )
        return "Engine/" + path[ ENGINE_CONTENT.Length.. ];

      return path;
    }

    #endregion

  }

  public class EDayTextureNode : EDayFileNode, IFileSystemAssetNode<DxgiTextureAsset, EDayTextureAssetFactory>
  {
    public EDayTextureNode( IFileSystemDevice device, GameFile gameFile, string className, IFileSystemNode parent )
      : base( device, gameFile, className, parent )
    {
    }
  }

  public class EDayStaticMeshNode : EDayFileNode, IFileSystemAssetNode<EDayStaticMeshAsset, EDayMeshAssetFactory<EDayStaticMeshAsset>>
  {
    public EDayStaticMeshNode( IFileSystemDevice device, GameFile gameFile, string className, IFileSystemNode parent )
      : base( device, gameFile, className, parent )
    {
    }
  }

  public class EDaySkeletalMeshNode : EDayFileNode, IFileSystemAssetNode<EDaySkeletalMeshAsset, EDayMeshAssetFactory<EDaySkeletalMeshAsset>>
  {
    public EDaySkeletalMeshNode( IFileSystemDevice device, GameFile gameFile, string className, IFileSystemNode parent )
      : base( device, gameFile, className, parent )
    {
    }
  }

  public class EDayTextNode : EDayFileNode, IFileSystemAssetNode<TextAsset, EDayTextAssetFactory>
  {
    public EDayTextNode( IFileSystemDevice device, GameFile gameFile, IFileSystemNode parent )
      : base( device, gameFile, string.Empty, parent )
    {
    }
  }

}
