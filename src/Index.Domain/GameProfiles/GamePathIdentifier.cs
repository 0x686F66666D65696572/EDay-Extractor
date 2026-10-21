namespace Index.Domain.GameProfiles
{

  public class GamePathIdentifier
  {

    private readonly IGameProfileManager _profileManager;
    private readonly Dictionary<string, IGamePathIdentificationRule> _rules;

    public GamePathIdentifier( IGameProfileManager profileManager )
    {
      _profileManager = profileManager;

      var profiles = _profileManager.Profiles.Values;
      _rules = profiles.ToDictionary( x => x.GameId, x => x.IdentificationRule );
    }

    public IEnumerable<IdentifiedGamePath> IdentifyCompatableProfiles( string pathToIdentify )
    {
      var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
      var targetPaths = Directory
        .EnumerateDirectories( pathToIdentify, "*", options )
        .Append( pathToIdentify )
        .Concat( EnumerateAncestors( pathToIdentify ) )
        .ToList();

      foreach ( (string gameId, IGamePathIdentificationRule rule) in _rules )
      {
        if ( !TryIdentifyPathWithRule( rule, targetPaths, out var foundPath ) )
          continue;

        yield return new IdentifiedGamePath
        {
          GameId = gameId,
          GamePath = foundPath
        };
      }
    }

    /// <summary>
    ///   Lets a folder inside the install (e.g. the Paks folder) identify the game too.
    /// </summary>
    private static IEnumerable<string> EnumerateAncestors( string path )
    {
      var parent = Directory.GetParent( path );
      for ( var i = 0; parent is not null && i < 5; i++, parent = parent.Parent )
        yield return parent.FullName;
    }

    private bool TryIdentifyPathWithRule( IGamePathIdentificationRule rule, IEnumerable<string> targetPaths, out string foundPath )
    {
      foundPath = default;

      var baseDirectory = rule.BaseDirectoryName;
      foreach ( var targetPath in targetPaths )
      {
        // Check if it matches the rule's BaseDirectoryName
        var targetPathName = Path.GetFileNameWithoutExtension( targetPath );
        if ( !targetPathName.Equals( baseDirectory, StringComparison.InvariantCultureIgnoreCase ) )
          continue;

        if ( PathMatchesAllChildPathRules( targetPath, rule.ChildPaths ) )
        {
          foundPath = targetPath;
          return true;
        }
      }

      return false;
    }

    private bool PathMatchesAllChildPathRules( string targetPath, IEnumerable<string> childPaths )
    {
      foreach ( var childPath in childPaths )
      {
        var expectedPath = Path.Combine( targetPath, childPath );
        if ( !File.Exists( expectedPath ) && !Directory.Exists( expectedPath ) )
          return false;
      }

      return true;
    }
  }

  public struct IdentifiedGamePath
  {
    public string GameId;
    public string GamePath;
  }

}
