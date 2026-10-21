using Index.Domain.Assets;
using Index.Domain.FileSystem;
using Index.Domain.GameProfiles;

namespace Index.Domain.Models
{

  public interface IEditorEnvironment
  {

    public string GameId { get; set; }
    public string GameName { get; set; }
    public string GamePath { get; set; }

    /// <summary>
    ///   Archive files picked by the user. When set, only these files are loaded
    ///   instead of scanning <see cref="GamePath"/>.
    /// </summary>
    public IReadOnlyList<string>? SourceFiles { get; set; }
    public IGameProfile GameProfile { get; set; }

    public IAssetManager AssetManager { get; }
    public IFileSystem FileSystem { get; }

    public IParameterCollection GlobalParameters { get; }

  }

}
