using System.Reflection;
using System.Runtime.Loader;

namespace Index.Domain.GameProfiles
{

  public class GameProfileManager : IGameProfileManager
  {

    #region Constants

    private const string PROFILE_DIRECTORY_NAME = "GameProfiles";

    #endregion

    #region Data Members

    private Dictionary<string, IGameProfile> _profiles;

    #endregion

    #region Properties

    public IReadOnlyDictionary<string, IGameProfile> Profiles
    {
      get => _profiles;
    }

    #endregion

    #region Constructor

    public GameProfileManager()
    {
      _profiles = new Dictionary<string, IGameProfile>();

      LoadGameProfiles();
    }

    #endregion

    #region Public Methods

    public IList<IdentifiedGamePath> ScanPathForSupportedGames( string path )
    {
      var pathIdentifier = new GamePathIdentifier( this );
      return pathIdentifier.IdentifyCompatableProfiles( path ).ToList();
    }

    public IList<IGameProfile> FindProfilesForFile( string filePath )
    {
      var extension = Path.GetExtension( filePath );
      if ( string.IsNullOrEmpty( extension ) )
        return new List<IGameProfile>();

      return _profiles.Values
        .Where( x => x.SupportedFileExtensions.Contains( extension, StringComparer.OrdinalIgnoreCase ) )
        .OrderBy( x => x.GameName )
        .ToList();
    }

    /// <summary>
    ///   Finds a single profile that can open every one of the given files.
    ///   Returns null if no files were given, or if they don't all belong to the same game.
    /// </summary>
    public IGameProfile? ResolveProfileForFiles( IEnumerable<string> filePaths )
    {
      IEnumerable<IGameProfile>? candidates = null;
      foreach ( var filePath in filePaths )
      {
        var profiles = FindProfilesForFile( filePath );
        candidates = candidates is null ? profiles : candidates.Intersect( profiles ).ToList();
      }

      return candidates?.FirstOrDefault();
    }

    public IReadOnlyList<string> GetSupportedFileExtensions()
    {
      return _profiles.Values
        .SelectMany( x => x.SupportedFileExtensions )
        .Select( x => x.ToLowerInvariant() )
        .Distinct()
        .OrderBy( x => x )
        .ToList();
    }

    #endregion

    #region Private Methods

    private void LoadGameProfiles()
    {
      // Profiles built into the app (referenced by Index.App and copied next to it).
      var appPath = Path.GetDirectoryName( Environment.ProcessPath );
      foreach ( var assemblyPath in Directory.GetFiles( appPath, "Index.Profiles.*.dll" ) )
      {
        var assembly = Assembly.Load( AssemblyName.GetAssemblyName( assemblyPath ) );
        foreach ( var profileType in EnumerateGameProfileTypes( assembly ) )
        {
          var instance = ( IGameProfile ) Activator.CreateInstance( profileType );
          _profiles.TryAdd( instance.GameId, instance );
        }
      }

      var profilePath = ResolveProfilePath();
      if ( !Directory.Exists( profilePath ) )
        return;

      foreach ( var assemblyPath in GetGameProfileAssemblyPaths() )
      {
        var assembly = Assembly.LoadFrom( assemblyPath );
        foreach ( var profileType in EnumerateGameProfileTypes( assembly ) )
        {
          var instance = ( IGameProfile ) Activator.CreateInstance( profileType );
          _profiles.TryAdd( instance.GameId, instance );
        }
      }
    }

    private IEnumerable<string> GetGameProfileAssemblyPaths()
    {
      var profilePath = ResolveProfilePath();

      // Load Assemblies
      var preloadContext = new AssemblyLoadContext( "GameProfilePreloadContext", true );
      var assemblyPaths = Directory.GetFiles( profilePath, "*.dll" );
      foreach ( var assemblyPath in assemblyPaths )
      {
        var assembly = preloadContext.LoadFromAssemblyPath( assemblyPath );

        if ( EnumerateGameProfileTypes( assembly ).Any() )
          yield return assemblyPath;
      }

      preloadContext.Unload();
    }

    private static string ResolveProfilePath()
    {
      var appPath = Path.GetDirectoryName( Environment.ProcessPath );
      return Path.Combine( appPath, PROFILE_DIRECTORY_NAME );
    }

    private static IEnumerable<Type> EnumerateGameProfileTypes( Assembly assembly )
    {
      return assembly.GetTypes()
          .Where( x => x.IsAssignableTo( typeof( IGameProfile ) ) )
          .Where( x => !x.IsAbstract );
    }

    #endregion

  }

}
