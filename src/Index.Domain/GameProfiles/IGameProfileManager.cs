namespace Index.Domain.GameProfiles
{

  public interface IGameProfileManager
  {

    IReadOnlyDictionary<string, IGameProfile> Profiles { get; }

    IList<IdentifiedGamePath> ScanPathForSupportedGames( string path );
    IList<IGameProfile> FindProfilesForFile( string filePath );
    IGameProfile? ResolveProfileForFiles( IEnumerable<string> filePaths );
    IReadOnlyList<string> GetSupportedFileExtensions();

  }

}
