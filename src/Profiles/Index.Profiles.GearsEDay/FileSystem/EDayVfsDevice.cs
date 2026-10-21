using CUE4Parse.FileProvider.Objects;
using Index.Domain.FileSystem;
using Index.Profiles.GearsEDay.Unreal;
using Serilog;

namespace Index.Profiles.GearsEDay.FileSystem
{

  /// <summary>
  ///   Exposes every IoStore/pak container of the install as one device. Textures, meshes and text files
  ///   become asset nodes; everything else stays reachable through CUE4Parse but isn't listed.
  /// </summary>
  public class EDayVfsDevice : FileSystemDeviceBase
  {

    #region Constants

    public const string ROOT_NAME = "Gears of War: E-Day";

    private static readonly HashSet<string> TEXT_EXTENSIONS = new( StringComparer.OrdinalIgnoreCase )
    {
      "ini", "txt", "json", "xml", "csv", "uplugin", "uproject", "html", "md"
    };

    #endregion

    #region Data Members

    private readonly string _gamePath;
    private readonly Action<string>? _reportStatus;
    private readonly Action<double>? _reportProgress;

    #endregion

    #region Properties

    public EDayProviderHost? Host { get; private set; }

    #endregion

    #region Constructor

    public EDayVfsDevice( string gamePath, Action<string>? reportStatus = null, Action<double>? reportProgress = null )
    {
      _gamePath = gamePath;
      _reportStatus = reportStatus;
      _reportProgress = reportProgress;
    }

    #endregion

    #region Overrides

    protected override async Task<IResult<IFileSystemNode>> OnInitializing( CancellationToken cancellationToken = default )
    {
      Host = await EDayProviderHost.CreateAsync( _gamePath, _reportStatus );

      _reportStatus?.Invoke( "Identifying assets (first launch only, this can take a minute)" );
      var classes = await Task.Run( () =>
        PackageClassifier.Classify( Host.Provider, Host.PaksDirectory, _reportProgress, cancellationToken ), cancellationToken );

      _reportStatus?.Invoke( "Building asset list" );
      var root = BuildNodeTree( classes );
      return Result.Successful( root );
    }

    public override Stream GetStream( IFileSystemNode node )
    {
      if ( node is not EDayFileNode fileNode )
        throw new NotSupportedException( $"{node.Name} is a directory." );

      return new MemoryStream( fileNode.GameFile.Read(), writable: false );
    }

    protected override void OnDisposing()
    {
      base.OnDisposing();
      Host?.Dispose();
      Host = null;
    }

    #endregion

    #region Private Methods

    private IFileSystemNode BuildNodeTree( IReadOnlyDictionary<string, string> classes )
    {
      var root = new EDayDirectoryNode( this, ROOT_NAME );
      int textures = 0, staticMeshes = 0, skeletalMeshes = 0, geometryCollections = 0, texts = 0;

      foreach ( var gameFile in Host!.Provider.Files.Values.OrderBy( x => x.Path, StringComparer.OrdinalIgnoreCase ) )
      {
        var node = CreateNode( gameFile, classes, root );
        if ( node is null )
          continue;

        switch ( node )
        {
          case EDayTextureNode: textures++; break;
          case EDayStaticMeshNode: staticMeshes++; break;
          case EDaySkeletalMeshNode: skeletalMeshes++; break;
          case EDayGeometryCollectionNode: geometryCollections++; break;
          case EDayTextNode: texts++; break;
        }

        root.AddChild( node );
      }

      Log.Information( "Listed {textures} textures, {static} static meshes, {skeletal} skeletal meshes, " +
        "{collections} geometry collections, {text} text files",
        textures, staticMeshes, skeletalMeshes, geometryCollections, texts );
      return root;
    }

    private EDayFileNode? CreateNode( GameFile gameFile, IReadOnlyDictionary<string, string> classes, IFileSystemNode root )
    {
      if ( gameFile.IsUePackage )
      {
        if ( !gameFile.Extension.Equals( "uasset", StringComparison.OrdinalIgnoreCase ) )
          return null;

        if ( !classes.TryGetValue( gameFile.Path, out var className ) )
          return null;

        return className switch
        {
          "Texture2D" => new EDayTextureNode( this, gameFile, className, root ),
          "StaticMesh" => new EDayStaticMeshNode( this, gameFile, className, root ),
          "SkeletalMesh" => new EDaySkeletalMeshNode( this, gameFile, className, root ),
          "GeometryCollection" => new EDayGeometryCollectionNode( this, gameFile, className, root ),
          _ => null
        };
      }

      if ( TEXT_EXTENSIONS.Contains( gameFile.Extension ) )
        return new EDayTextNode( this, gameFile, root );

      return null;
    }

    #endregion

  }

}
