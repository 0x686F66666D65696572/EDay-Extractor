using Index.Domain.FileSystem;
using Index.Domain.GameProfiles;
using Index.Profiles.GearsEDay.FileSystem;

namespace Index.Profiles.GearsEDay
{

  public class EDayGameProfile : GameProfileBase
  {

    #region Properties

    public override string GameId => "GearsOfWarEDay";
    public override string GameName => "Gears of War: E-Day";
    public override string Author => "EDay-Extractor (built on Index by Haus and CUE4Parse)";

    public override IFileSystemLoader FileSystemLoader => new EDayFileSystemLoader();
    public override IGamePathIdentificationRule IdentificationRule => new EDayGamePathIdentificationRule();

    #endregion

  }

  /// <summary>
  ///   Matches the install's project folder: "...\Content\FairlightConcept" containing "Content\Paks\global.utoc".
  ///   The launcher also accepts any folder above or below it, see <see cref="Unreal.EDayProviderHost.FindPaksDirectory"/>.
  /// </summary>
  public class EDayGamePathIdentificationRule : IGamePathIdentificationRule
  {

    public string BaseDirectoryName => "FairlightConcept";

    public IEnumerable<string> ChildPaths
    {
      get
      {
        yield return @"Content\Paks\global.utoc";
      }
    }

  }

}
