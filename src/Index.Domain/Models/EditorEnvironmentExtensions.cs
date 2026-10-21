using Index.Domain.GameProfiles;

namespace Index.Domain.Models
{

  public static class EditorEnvironmentExtensions
  {

    /// <summary>
    ///   Configures the environment to scan a game installation directory.
    /// </summary>
    public static void UseGamePath( this IEditorEnvironment environment, IGameProfile profile, string gamePath )
    {
      ASSERT_NOT_NULL( profile );
      ASSERT( Directory.Exists( gamePath ), "Directory does not exist: {0}", gamePath );

      environment.GameId = profile.GameId;
      environment.GameName = profile.GameName;
      environment.GameProfile = profile;
      environment.GamePath = gamePath;
      environment.SourceFiles = null;
    }

    /// <summary>
    ///   Configures the environment to load only the given archive files.
    /// </summary>
    public static void UseSourceFiles( this IEditorEnvironment environment, IGameProfile profile, IEnumerable<string> filePaths )
    {
      ASSERT_NOT_NULL( profile );

      var files = filePaths.Select( Path.GetFullPath ).ToList();
      ASSERT( files.Count > 0, "No source files were specified." );

      environment.GameId = profile.GameId;
      environment.GameName = profile.GameName;
      environment.GameProfile = profile;
      environment.GamePath = Path.GetDirectoryName( files[ 0 ] )!;
      environment.SourceFiles = files;
    }

  }

}
