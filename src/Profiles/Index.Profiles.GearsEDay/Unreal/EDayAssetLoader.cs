using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using Index.Profiles.GearsEDay.FileSystem;
using Serilog;

namespace Index.Profiles.GearsEDay.Unreal
{

  public static class EDayAssetLoader
  {

    #region Constants

    private const int MAX_MATERIAL_PARENT_DEPTH = 3;

    private static readonly string[] DIFFUSE_SUFFIXES = { "_d", "_bc", "_c", "_col", "_color", "_basecolor", "_albedo", "_diffuse", "_diff", "_a" };
    private static readonly string[] NORMAL_SUFFIXES = { "_n", "_nm", "_nmr", "_nrm", "_normal", "_norm" };

    #endregion

    #region Public Methods

    public static EDayProviderHost Host
      => EDayProviderHost.Current ?? throw new InvalidOperationException( "The game archives are not mounted." );

    /// <summary>
    ///   Loads the package's main asset, the export named after the package (e.g. "T_Rock_D" in "T_Rock_D.uasset").
    /// </summary>
    public static T LoadMainExport<T>( GameFile gameFile ) where T : UObject
    {
      var package = Host.Provider.LoadPackage( gameFile );

      var export = package.GetExportOrNull( gameFile.NameWithoutExtension, StringComparison.OrdinalIgnoreCase );
      if ( export is T typed )
        return typed;

      foreach ( var candidate in package.GetExports() )
        if ( candidate is T match )
          return match;

      throw new InvalidDataException(
        $"{gameFile.Path} does not contain a readable {typeof( T ).Name}" +
        ( export is null ? "." : $" (main export is a {export.ExportType})." ) );
    }

    /// <summary>
    ///   Finds the textures a material uses. Without mappings the material's parameters can't be read,
    ///   so this lists the textures the material package imports, walking up to parent materials if it has none.
    /// </summary>
    public static IReadOnlyList<string> GetMaterialTexturePaths( FPackageIndex? materialIndex )
    {
      var textures = new List<string>();
      if ( materialIndex is null || materialIndex.IsNull )
        return textures;

      try
      {
        var packagePath = GetPackagePath( materialIndex.ResolvedObject?.GetPathName() );
        for ( var depth = 0; packagePath is not null && depth < MAX_MATERIAL_PARENT_DEPTH && textures.Count == 0; depth++ )
          packagePath = CollectTextureImports( packagePath, textures );
      }
      catch ( Exception ex )
      {
        Log.Debug( ex, "Could not resolve textures for material {material}", materialIndex.Name );
      }

      return textures;
    }

    /// <summary>
    ///   Converts an object path ("/Game/X/T_Y.T_Y") into the asset name used by the explorer ("Game/X/T_Y").
    /// </summary>
    public static string? ToAssetName( string objectPath )
    {
      var packagePath = GetPackagePath( objectPath );
      if ( packagePath is null || !Host.Provider.TryGetGameFile( packagePath + ".uasset", out var gameFile ) )
        return null;

      return EDayFileNode.GetAssetPath( gameFile, keepExtension: false );
    }

    public static string? PickTexture( IEnumerable<string> textures, string[] suffixes )
      => textures.FirstOrDefault( texture =>
      {
        var name = Path.GetFileName( texture ).ToLowerInvariant();
        return suffixes.Any( suffix => name.EndsWith( suffix ) );
      } );

    public static string? PickDiffuseTexture( IReadOnlyList<string> textures )
      => PickTexture( textures, DIFFUSE_SUFFIXES ) ?? textures.FirstOrDefault( x => PickTexture( new[] { x }, NORMAL_SUFFIXES ) is null );

    public static string? PickNormalTexture( IReadOnlyList<string> textures )
      => PickTexture( textures, NORMAL_SUFFIXES );

    public static bool IsNormalMapName( string name )
      => PickTexture( new[] { name }, NORMAL_SUFFIXES ) is not null;

    #endregion

    #region Private Methods

    /// <summary>
    ///   Adds the texture imports of a material package. Returns the package path of the first imported
    ///   material (its parent) to continue the search with.
    /// </summary>
    private static string? CollectTextureImports( string packagePath, List<string> textures )
    {
      if ( !Host.Provider.TryLoadPackage( packagePath, out var package ) || package is not IoPackage ioPackage )
        return null;

      string? parent = null;
      foreach ( var import in ioPackage.ImportMap )
      {
        var resolved = ioPackage.ResolveObjectIndex( import );
        var className = resolved?.Class?.Name.Text;
        if ( resolved is null || className is null )
          continue;

        var objectPath = resolved.GetPathName();
        if ( className.StartsWith( "Texture", StringComparison.Ordinal ) )
        {
          var assetName = ToAssetName( objectPath );
          if ( assetName is not null && !textures.Contains( assetName ) )
            textures.Add( assetName );
        }
        else if ( parent is null && className.StartsWith( "Material", StringComparison.Ordinal ) && !className.StartsWith( "MaterialFunction" ) )
        {
          parent = GetPackagePath( objectPath );
        }
      }

      return parent;
    }

    private static string? GetPackagePath( string? objectPath )
    {
      if ( string.IsNullOrEmpty( objectPath ) )
        return null;

      var dot = objectPath.LastIndexOf( '.' );
      var slash = objectPath.LastIndexOf( '/' );
      return dot > slash ? objectPath[ ..dot ] : objectPath;
    }

    #endregion

  }

}
