using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures.BC;
using Serilog;

namespace Index.Profiles.GearsEDay.Unreal
{

  /// <summary>
  ///   Owns the CUE4Parse file provider for a Gears of War: E-Day install.
  /// </summary>
  public sealed class EDayProviderHost : IDisposable
  {

    #region Constants

    public const string PAKS_DIRECTORY_NAME = "Paks";
    public const string GLOBAL_TOC_FILE_NAME = "global.utoc";

    #endregion

    #region Properties

    /// <summary>
    ///   The host for the currently opened game. Index opens one game per session.
    /// </summary>
    public static EDayProviderHost? Current { get; private set; }

    public static string DataDirectory
    {
      get
      {
        var path = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "EDayExtractor" );
        Directory.CreateDirectory( path );
        return path;
      }
    }

    public DefaultFileProvider Provider { get; }
    public string PaksDirectory { get; }
    public string? MappingsPath { get; }
    public bool HasMappings => MappingsPath is not null;

    #endregion

    #region Constructor

    private EDayProviderHost( DefaultFileProvider provider, string paksDirectory, string? mappingsPath )
    {
      Provider = provider;
      PaksDirectory = paksDirectory;
      MappingsPath = mappingsPath;
    }

    #endregion

    #region Public Methods

    public static async Task<EDayProviderHost> CreateAsync( string gamePath, Action<string>? reportStatus = null )
    {
      var paksDirectory = FindPaksDirectory( gamePath )
        ?? throw new DirectoryNotFoundException(
          $"Could not find the game's Paks folder (containing {GLOBAL_TOC_FILE_NAME}) under:\n{gamePath}" );

      reportStatus?.Invoke( "Preparing Oodle decompression" );
      await InitializeNativeLibraries();

      reportStatus?.Invoke( "Mounting game archives" );
      var provider = new DefaultFileProvider(
        paksDirectory,
        SearchOption.TopDirectoryOnly,
        isCaseInsensitive: true,
        versions: new VersionContainer( EGame.GAME_GearsofWarEDay ) );

      var mappingsPath = FindMappingsFile( gamePath );
      if ( mappingsPath is not null )
      {
        Log.Information( "Using mappings file {path}", mappingsPath );
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider( mappingsPath );
      }

      // Without mappings, read native data (meshes, textures) by skipping the unversioned property blocks.
      NoMappingsFallback.Enabled = mappingsPath is null;

      provider.Initialize();
      var mountedCount = provider.Mount();
      Log.Information( "Mounted {count} containers ({files} files) from {path}", mountedCount, provider.Files.Count, paksDirectory );

      if ( provider.Files.Count == 0 )
        throw new InvalidOperationException( "No files could be mounted from the game's archives." );

      var host = new EDayProviderHost( provider, paksDirectory, mappingsPath );
      Current?.Dispose();
      Current = host;
      return host;
    }

    /// <summary>
    ///   Finds the Paks directory at, above, or below the given path.
    /// </summary>
    public static string? FindPaksDirectory( string path )
    {
      if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
        return null;

      // The user may have picked the Paks folder itself, or a folder inside the install.
      for ( var dir = new DirectoryInfo( path ); dir is not null; dir = dir.Parent )
      {
        if ( File.Exists( Path.Combine( dir.FullName, GLOBAL_TOC_FILE_NAME ) ) )
          return dir.FullName;
      }

      var options = new EnumerationOptions
      {
        RecurseSubdirectories = true,
        MaxRecursionDepth = 6,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive
      };

      return Directory.EnumerateFiles( path, GLOBAL_TOC_FILE_NAME, options )
        .Select( Path.GetDirectoryName )
        .FirstOrDefault( x => x is not null );
    }

    #endregion

    #region Private Methods

    private static async Task InitializeNativeLibraries()
    {
      var oodlePath = Path.Combine( DataDirectory, OodleHelper.OodleFileName );
      if ( !File.Exists( oodlePath ) )
      {
        string? downloadPath = oodlePath;
        if ( !await OodleHelper.DownloadOodleDllAsync( ref downloadPath ) || !File.Exists( oodlePath ) )
          throw new InvalidOperationException(
            "The Oodle decompression library could not be downloaded. Check your internet connection, " +
            $"or place {OodleHelper.OodleFileName} in {DataDirectory}." );
      }
      OodleHelper.Initialize( oodlePath );

      // Detex decodes BC6H/BC7 textures; CUE4Parse ships it as an embedded resource.
      var detexPath = Path.Combine( DataDirectory, DetexHelper.DLL_NAME );
      if ( DetexHelper.LoadDll( detexPath ) )
        DetexHelper.Initialize( detexPath );
      else
        Log.Warning( "Detex could not be loaded; BC6H/BC7 textures will not decode." );
    }

    /// <summary>
    ///   Optional .usmap mappings give full property data. Looked up in the app's "Mappings" folder,
    ///   the user data folder, and the game folder.
    /// </summary>
    private static string? FindMappingsFile( string gamePath )
    {
      var candidates = new[]
      {
        Path.Combine( AppContext.BaseDirectory, "Mappings" ),
        Path.Combine( DataDirectory, "Mappings" ),
        gamePath
      };

      return candidates
        .Where( Directory.Exists )
        .SelectMany( x => Directory.EnumerateFiles( x, "*.usmap", SearchOption.TopDirectoryOnly ) )
        .OrderByDescending( File.GetLastWriteTimeUtc )
        .FirstOrDefault();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
      Provider.Dispose();
      if ( ReferenceEquals( Current, this ) )
        Current = null;
    }

    #endregion

  }

}
